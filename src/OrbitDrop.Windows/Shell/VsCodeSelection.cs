using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OrbitDrop.Windows.Shell;

/// <summary>
/// The files selected in VS Code (and editors built on it: Cursor, Windsurf, VSCodium) for Ctrl+Alt+Space. VS Code
/// doesn't share its selection with other apps, but its own "Copy Path" command (Shift+Alt+C on Windows) copies the
/// full paths of the items selected in its Explorer, or of the open file when an editor has focus. OrbitDrop presses
/// it, reads the paths, and puts the clipboard back exactly as it was.
/// </summary>
public static class VsCodeSelection
{
    private static readonly HashSet<string> Editors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Code - Insiders", "VSCodium", "Cursor", "Windsurf",
    };

    /// <summary>True when the foreground window belongs to VS Code or an editor built on it.</summary>
    public static bool IsForeground(out IntPtr window)
    {
        window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint pid);
        try { using var p = Process.GetProcessById((int)pid); return Editors.Contains(p.ProcessName); }
        catch { return false; }
    }

    /// <summary>
    /// The selected items' paths, or empty if VS Code didn't copy any. <paramref name="clipboardOwner"/> is one of
    /// OrbitDrop's own windows (Windows needs an owner to put the saved clipboard back). Runs on the UI thread.
    /// </summary>
    public static Task<IReadOnlyList<string>> ReadAsync(IntPtr clipboardOwner) =>
        CopyAndReadAsync(clipboardOwner, [VK_SHIFT, VK_MENU, 0x43], // Shift+Alt+C, VS Code's "Copy Path"
            owner => (ReadText(owner) ?? "").Split('\n').Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0).ToList());

    /// <summary>
    /// File Explorer views that aren't a folder (Gallery, Home) don't share their selection with Shell automation, but
    /// Ctrl+C copies the selected files there as everywhere. OrbitDrop presses it, reads the file list, and puts the
    /// clipboard back exactly as it was.
    /// </summary>
    public static Task<IReadOnlyList<string>> ReadExplorerCopyAsync(IntPtr clipboardOwner) =>
        CopyAndReadAsync(clipboardOwner, [VK_CONTROL, 0x43], ReadFileList);

    /// <summary>
    /// Presses <paramref name="keys"/> (an app's own copy command) once the user has let go of the shortcut, reads what
    /// it copied with <paramref name="read"/>, and restores the clipboard. Only paths that exist are returned.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CopyAndReadAsync(IntPtr clipboardOwner, ushort[] keys, Func<IntPtr, List<string>> read)
    {
        // Let go of the shortcut first, so the app sees exactly its copy keys.
        for (int i = 0; i < 100 && ModifiersDown(); i++) await Task.Delay(15);
        if (ModifiersDown()) return [];

        var saved = Snapshot(clipboardOwner);
        if (saved is null) return []; // couldn't read the clipboard safely: don't touch it
        uint before = GetClipboardSequenceNumber();
        try
        {
            Press(keys);
            for (int i = 0; i < 60 && GetClipboardSequenceNumber() == before; i++) await Task.Delay(15);
            if (GetClipboardSequenceNumber() == before) return [];
            await Task.Delay(30); // let the app finish writing
            return read(clipboardOwner).Where(l => Path.IsPathFullyQualified(l) && (File.Exists(l) || Directory.Exists(l)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            if (GetClipboardSequenceNumber() != before) Restore(clipboardOwner, saved);
        }
    }

    private static bool ModifiersDown() =>
        IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_SHIFT) || IsDown(VK_LWIN) || IsDown(VK_RWIN) || IsDown(VK_SPACE);

    /// <summary>Presses the keys in order, then lets go of them in reverse.</summary>
    private static void Press(ushort[] keys)
    {
        INPUT Key(ushort vk, bool up) => new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } };
        var inputs = keys.Select(k => Key(k, false)).Concat(keys.Reverse().Select(k => Key(k, true))).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>The paths in a copied file list (CF_HDROP).</summary>
    private static List<string> ReadFileList(IntPtr owner)
    {
        var paths = new List<string>();
        if (!Open(owner)) return paths;
        try
        {
            IntPtr h = GetClipboardData(CF_HDROP);
            if (h == IntPtr.Zero) return paths;
            uint n = DragQueryFile(h, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < n; i++)
            {
                var name = new StringBuilder((int)DragQueryFile(h, i, null, 0) + 1);
                DragQueryFile(h, i, name, (uint)name.Capacity);
                paths.Add(name.ToString());
            }
            return paths;
        }
        finally { CloseClipboard(); }
    }

    // ------------------------------------------------------------------ clipboard snapshot and restore

    /// <summary>
    /// Every clipboard format that is plain memory (text, HTML, file lists, images as DIB, app formats). Handle-based
    /// formats (bitmaps, metafiles, palettes) are skipped; Windows re-creates CF_BITMAP from CF_DIB. Null if the
    /// clipboard couldn't be opened or holds more than 64 MB.
    /// </summary>
    private static List<(uint Format, byte[] Data)>? Snapshot(IntPtr owner)
    {
        if (!Open(owner)) return null;
        try
        {
            var saved = new List<(uint, byte[])>();
            long total = 0;
            for (uint f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
            {
                if (IsHandleFormat(f)) continue;
                IntPtr h = GetClipboardData(f);
                if (h == IntPtr.Zero) continue;
                long size = (long)GlobalSize(h);
                if (size <= 0) continue;
                if ((total += size) > 64L * 1024 * 1024) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) continue;
                try { var bytes = new byte[size]; Marshal.Copy(p, bytes, 0, (int)size); saved.Add((f, bytes)); }
                finally { GlobalUnlock(h); }
            }
            return saved;
        }
        finally { CloseClipboard(); }
    }

    private static void Restore(IntPtr owner, List<(uint Format, byte[] Data)> saved)
    {
        if (!Open(owner)) return;
        try
        {
            EmptyClipboard();
            foreach (var (format, data) in saved)
            {
                IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (h == IntPtr.Zero) continue;
                IntPtr p = GlobalLock(h);
                Marshal.Copy(data, 0, p, data.Length);
                GlobalUnlock(h);
                if (SetClipboardData(format, h) == IntPtr.Zero) GlobalFree(h); // on success the clipboard owns it
            }
        }
        finally { CloseClipboard(); }
    }

    private static string? ReadText(IntPtr owner)
    {
        if (!Open(owner)) return null;
        try
        {
            IntPtr h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            IntPtr p = GlobalLock(h);
            try { return p == IntPtr.Zero ? null : Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Another app may hold the clipboard for a moment: retry briefly.</summary>
    private static bool Open(IntPtr owner)
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    private static bool IsHandleFormat(uint f) =>
        f is CF_BITMAP or CF_METAFILEPICT or CF_PALETTE or CF_ENHMETAFILE or CF_OWNERDISPLAY
            or CF_DSPBITMAP or CF_DSPMETAFILEPICT or CF_DSPENHMETAFILE
        || f is >= CF_GDIOBJFIRST and <= CF_GDIOBJLAST;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // ------------------------------------------------------------------ interop

    private const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint CF_HDROP = 15, CF_BITMAP = 2, CF_METAFILEPICT = 3, CF_PALETTE = 9, CF_UNICODETEXT = 13, CF_ENHMETAFILE = 14,
        CF_OWNERDISPLAY = 0x80, CF_DSPBITMAP = 0x82, CF_DSPMETAFILEPICT = 0x83, CF_DSPENHMETAFILE = 0x8E,
        CF_GDIOBJFIRST = 0x300, CF_GDIOBJLAST = 0x3FF;
    private const uint GMEM_MOVEABLE = 0x2, INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    /// <summary>INPUT with its union sized for the largest member (MOUSEINPUT), as Windows expects.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint size);
}
