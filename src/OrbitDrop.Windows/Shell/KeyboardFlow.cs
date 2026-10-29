using System.Runtime.InteropServices;
using System.Text;

namespace OrbitDrop.Windows.Shell;

/// <summary>A system-wide shortcut registered on the message window (RegisterHotKey). Disposing unregisters it.</summary>
public sealed class GlobalHotkey : IDisposable
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20;
    private const int WM_HOTKEY = 0x0312;
    private static int _nextId = 0x0D10;

    private readonly MessageWindow _window;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly Action _pressed;

    public GlobalHotkey(MessageWindow window, uint modifiers, uint key, Action pressed)
    {
        _window = window;
        _pressed = pressed;
        Modifiers = modifiers;
        Key = key;
        Registered = RegisterHotKey(window.Handle, _id, modifiers | MOD_NOREPEAT, key);
        if (Registered) window.Message += OnMessage;
    }

    public uint Modifiers { get; }
    public uint Key { get; }

    /// <summary>False when another app already owns the combination.</summary>
    public bool Registered { get; }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_HOTKEY || wParam.ToInt32() != _id) return false;
        _pressed();
        return true;
    }

    public void Dispose()
    {
        if (!Registered) return;
        _window.Message -= OnMessage;
        UnregisterHotKey(_window.Handle, _id);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

/// <summary>
/// The files selected in the foreground File Explorer window or on the desktop, via the Shell automation objects
/// (Shell.Application). Must run on an STA thread. Returns an empty list when nothing usable is selected.
/// </summary>
public static class ExplorerSelection
{
    /// <summary>What happened, for the log: which window, which tab, how many items.</summary>
    public static string LastResult { get; private set; } = "";

    /// <summary>The folder open in that File Explorer tab (or the desktop), when there was one: where new files belong.</summary>
    public static string? LastFolder { get; private set; }

    /// <summary>
    /// The foreground was a File Explorer view that isn't a folder on disk (Gallery, Home, This PC…), where Shell
    /// automation may not see the selection: worth asking with Ctrl+C (<see cref="VsCodeSelection.ReadExplorerCopyAsync"/>).
    /// </summary>
    public static bool LastWasExplorerView { get; private set; }

    public static IReadOnlyList<string> FromForegroundWindow()
    {
        LastFolder = null;
        LastWasExplorerView = false;
        IntPtr foreground = GetForegroundWindow();
        string cls = ClassOf(foreground);
        if (cls is not ("Progman" or "WorkerW" or "CabinetWClass")) { LastResult = $"foreground is {cls}, not File Explorer"; return []; }
        LastWasExplorerView = cls == "CabinetWClass";

        // Explorer can briefly refuse automation calls while it's busy (RPC_E_CALL_REJECTED and the like): retry.
        for (int attempt = 1; ; attempt++)
        {
            try { return Read(foreground, cls); }
            catch (Exception ex) when (attempt < 4)
            {
                LastResult = $"attempt {attempt} failed: {ex.Message}";
                Thread.Sleep(60 * attempt);
            }
            catch (Exception ex) { LastResult = $"failed: {ex.Message}"; return []; }
        }
    }

    private static IReadOnlyList<string> Read(IntPtr foreground, string cls)
    {
        LastResult = "";
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) { LastResult = "Shell.Application unavailable"; return []; }
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic? view = null;

        if (cls is "Progman" or "WorkerW")
        {
            // The desktop is a shell view too: SWC_DESKTOP (8) with SWFO_NEEDDISPATCH (1).
            object location = 0, root = Type.Missing;
            view = shell.Windows().FindWindowSW(ref location, ref root, 8, out int _, 1);
        }
        else
        {
            // Windows 11 tabs share one top-level window. The active tab is the topmost ShellTabWindowClass child;
            // each tab's browser reports its own tab window, so match on that (the title is only a fallback).
            IntPtr activeTab = FindWindowEx(foreground, IntPtr.Zero, "ShellTabWindowClass", null);
            var matches = new List<dynamic>();
            foreach (dynamic w in shell.Windows())
            {
                try { if (new IntPtr((long)w.HWND) == foreground) matches.Add(w); } catch { /* not an Explorer view */ }
            }
            view = matches.FirstOrDefault(m => activeTab != IntPtr.Zero && TabWindowOf((object)m) == activeTab);
            if (view is null && matches.Count > 0)
            {
                string title = TitleOf(foreground);
                view = matches.FirstOrDefault(m => title.StartsWith((string)m.LocationName, StringComparison.OrdinalIgnoreCase)) ?? matches[0];
            }
            LastResult = $"{matches.Count} tab(s)";
        }
        if (view is null) { LastResult = "no Explorer view found"; return []; }

        try { string? folder = view.Document.Folder.Self.Path; if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) LastFolder = folder; } catch { /* a virtual folder (This PC, a ZIP …) */ }
        if (LastFolder is not null) LastWasExplorerView = false; // a real folder: its selection is complete
        var paths = new List<string>();
        foreach (dynamic item in view.Document.SelectedItems())
        {
            string? path = item.Path;
            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path))) paths.Add(path);
        }
        LastResult = $"{(cls == "CabinetWClass" ? "File Explorer" : "desktop")}: {paths.Count} selected ({LastResult})";
        return paths;
    }

    /// <summary>The tab window behind one Explorer browser: IServiceProvider → IShellBrowser → IOleWindow::GetWindow.</summary>
    private static IntPtr TabWindowOf(object browser)
    {
        try
        {
            if (browser is not IServiceProvider sp) return IntPtr.Zero;
            Guid service = SID_STopLevelBrowser, iid = IID_IShellBrowser;
            if (sp.QueryService(ref service, ref iid, out IntPtr shellBrowser) != 0 || shellBrowser == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                IntPtr getWindow = Marshal.ReadIntPtr(Marshal.ReadIntPtr(shellBrowser), 3 * IntPtr.Size); // IOleWindow::GetWindow
                var call = Marshal.GetDelegateForFunctionPointer<GetWindowFn>(getWindow);
                return call(shellBrowser, out IntPtr hwnd) == 0 ? hwnd : IntPtr.Zero;
            }
            finally { Marshal.Release(shellBrowser); }
        }
        catch { return IntPtr.Zero; }
    }

    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetWindowFn(IntPtr self, out IntPtr hwnd);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr obj);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string TitleOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
}
