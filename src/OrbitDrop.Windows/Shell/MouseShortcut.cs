using System.Runtime.InteropServices;
using System.Windows.Threading;
using OrbitDrop.Windows.Interop;

namespace OrbitDrop.Windows.Shell;

/// <summary>
/// A mouse button as a shortcut: the side buttons (back, forward) alone or with keys, or the middle button with Ctrl
/// or Alt. One low-level mouse hook serves them all, and it's only there while at least one is set. The press that is
/// a shortcut is swallowed (down and up), so the browser doesn't also go back a page. The hook only compares a button
/// and the modifier keys, then hands the work to the app's thread, so the mouse never lags.
/// </summary>
public sealed class MouseShortcut : IShortcut
{
    private const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    private const int VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private static readonly List<MouseShortcut> Active = [];
    private static readonly Win32.LowLevelProc Proc = OnMouse; // kept alive for as long as the hook
    private static IntPtr _hook;
    /// <summary>Buttons whose press was a shortcut: their release is swallowed too.</summary>
    private static readonly HashSet<uint> Swallowed = [];

    private readonly uint _modifiers, _button;
    private readonly Action _pressed;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public MouseShortcut(uint modifiers, uint button, Action pressed)
    {
        _modifiers = modifiers & 0xF;
        _button = button;
        _pressed = pressed;
        lock (Active)
        {
            // Two of OrbitDrop's own shortcuts can't share a button.
            if (Active.Any(a => a._button == button && a._modifiers == _modifiers)) return;
            if (_hook == IntPtr.Zero) _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, Proc, Win32.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) return;
            Active.Add(this);
            Registered = true;
        }
    }

    public bool Registered { get; private set; }
    public uint Modifiers => _modifiers;
    public uint Key => _button;

    private static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            uint button = 0;
            bool down = false;
            if (msg is WM_MBUTTONDOWN or WM_MBUTTONUP) { button = 0x04; down = msg == WM_MBUTTONDOWN; }
            else if (msg is WM_XBUTTONDOWN or WM_XBUTTONUP)
            {
                var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                button = (info.mouseData >> 16) == 1 ? 0x05u : 0x06u;
                down = msg == WM_XBUTTONDOWN;
            }
            if (button != 0)
            {
                if (!down) { lock (Active) if (Swallowed.Remove(button)) return 1; }
                else
                {
                    uint mods = (Win32.IsDown(Win32.VK_CONTROL) ? 0x2u : 0) | (Win32.IsDown(VK_MENU) ? 0x1u : 0) | (Win32.IsDown(Win32.VK_SHIFT) ? 0x4u : 0)
                        | (Win32.IsDown(VK_LWIN) || Win32.IsDown(VK_RWIN) ? 0x8u : 0);
                    MouseShortcut? match;
                    lock (Active) match = Active.FirstOrDefault(a => a._button == button && a._modifiers == mods);
                    if (match is not null)
                    {
                        lock (Active) Swallowed.Add(button);
                        match._dispatcher.BeginInvoke(match._pressed);
                        return 1;
                    }
                }
            }
        }
        return Win32.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (!Registered) return;
        Registered = false;
        lock (Active)
        {
            Active.Remove(this);
            if (Active.Count == 0 && _hook != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; Swallowed.Clear(); }
        }
    }
}
