using System.Windows;
using OrbitDrop.Core;
using OrbitDrop.Infrastructure;
using OrbitDrop.Tools;
using OrbitDrop.Tools.Metadata;
using OrbitDrop.UI.Metadata;
using OrbitDrop.Windows.Interop;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.App;

/// <summary>The windows and pickers that collect choices for options that need them (see <see cref="InputBroker"/>).</summary>
public partial class App
{
    private void RegisterInputs()
    {
        _inputs.Register(MetadataProvider.InfoId, AskMetadataAsync);
        _inputs.Register(ImageToolsProvider.EditId, AskPhotoEditAsync);
        _inputs.Register(OrbitDrop.Conversion.Media.MediaFoundationProvider.TrimId, AskTrimAsync);
        _inputs.Register(FileActionsProvider.CopyToPrefix + FileActionsProvider.ChooseSuffix, (op, files, _) => AskFolderAsync("Copy to", files));
        _inputs.Register(FileActionsProvider.MoveToPrefix + FileActionsProvider.ChooseSuffix, (op, files, _) => AskFolderAsync("Move to", files));
        // A crowned Premium tool (or a third of your own actions): how to get it, on the website, about that tool.
        _inputs.Register(OperationRegistry.LockedPrefix, (op, files, _) =>
        {
            OpenUpgradePage(op.Id[OperationRegistry.LockedPrefix.Length..], op.Label);
            return Task.FromResult<(bool, object?)>((false, null));
        });
    }

    /// <summary>The website's guide to the Store version, about what was tried (no external payment: it leads to the Store).</summary>
    private const string UpgradePage = "https://orbitdrop.essam.biz/upgrade";

    private static void OpenUpgradePage(string optionId, string label)
    {
        string query = optionId.StartsWith(CustomActionsProvider.Prefix, StringComparison.Ordinal)
            ? $"reason=actions&used={CustomAction.FreeActions}&limit={CustomAction.FreeActions}"
            : $"tool={Uri.EscapeDataString(optionId)}&name={Uri.EscapeDataString(label)}";
        Log.Info($"Upgrade page for {optionId}");
        UpdateManager.OpenUrl($"{UpgradePage}?{query}&from=github");
    }

    /// <summary>About › Switch to the Microsoft Store version: install it, then hand over (see <see cref="StoreSwitch"/>).</summary>
    public void SwitchToStore()
    {
        Log.Info("Switch to the Store version: asked");
        bool portable = !File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));
        ShowInFront(new OrbitDrop.UI.Settings.SwitchWindow(
            install: (progress, ct) => StoreSwitch.InstallAsync(progress, ct),
            handOver: () => { SaveNow(); StoreSwitch.HandOver(_paths); Quit(); },
            openStorePage: () => UpdateManager.OpenUrl(StoreSwitch.StorePage),
            portable));
    }

    /// <summary>Settings › Your actions › Add, past the free ones: the upgrade guide, saying so.</summary>
    public void ShowActionsUpgrade() => OpenUpgradePage(CustomActionsProvider.Prefix, "");

    /// <summary>Opens the photo editor; the edited full-resolution bitmap becomes the job's input.</summary>
    private async Task<(bool, object?)> AskPhotoEditAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        var image = OrbitDrop.Conversion.Imaging.WicImageCodec.Load(files[0]); // upright, BGRA32
        var editor = new OrbitDrop.UI.Editor.PhotoEditorWindow(image, Path.GetFileName(files[0]));
        ShowInFront(editor);
        var edited = await editor.Result;
        return edited is null ? (false, null) : (true, edited);
    }

    /// <summary>Opens the Trim window; the chosen range becomes the job's input.</summary>
    private async Task<(bool, object?)> AskTrimAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        string file = files[0];
        var length = await OrbitDrop.Conversion.Media.MediaFoundationProvider.DurationAsync(file);
        var window = new OrbitDrop.UI.Trim.TrimWindow(file, length, OrbitDrop.Conversion.Media.MediaFoundationProvider.IsVideo(file));
        ShowInFront(window);
        var range = await window.Result;
        return range is null ? (false, null) : (true, range);
    }

    /// <summary>A folder picker in front of everything; the chosen folder becomes the job's input.</summary>
    private Task<(bool, object?)> AskFolderAsync(string verb, FileSet files)
    {
        // An invisible, activated owner makes the dialog open in front (OrbitDrop has no window of its own).
        var owner = new Window
        {
            Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = true,
            AllowsTransparency = true, Opacity = 0, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        owner.Show();
        owner.Activate();
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = files.Count == 1 ? $"{verb}: choose a folder for {Path.GetFileName(Path.TrimEndingDirectorySeparator(files[0]))}" : $"{verb}: choose a folder for {files.Count} items",
                InitialDirectory = _settings.RecentFolders.FirstOrDefault(Directory.Exists) ?? Path.GetDirectoryName(files[0]),
            };
            return Task.FromResult<(bool, object?)>(dialog.ShowDialog(owner) == true ? (true, dialog.FolderName) : (false, null));
        }
        finally { owner.Close(); }
    }

    // ------------------------------------------------------------------ Float shelf

    private OrbitDrop.UI.Shelf.ShelfWindow? _shelf;

    /// <summary>One shelf at a time: floating more files adds them to the open one.</summary>
    private void ShowShelf(IReadOnlyList<string> paths)
    {
        if (_shelf is null)
        {
            _shelf = new OrbitDrop.UI.Shelf.ShelfWindow();
            _shelf.Closed += (_, _) => _shelf = null;
        }
        _shelf.Add(paths);
        Log.Info($"Floated {paths.Count} item(s)");
    }

    // ------------------------------------------------------------------ keyboard flow

    /// <summary>The wheel's keyboard shortcut for people: "Ctrl + Alt + Space", or the user's own.</summary>
    private string ShortcutName => KeyCombo.Display(_settings.WheelShortcut);

    /// <summary>Claims (or releases) the wheel's keyboard shortcut (Ctrl+Alt+Space unless the user picked another).</summary>
    private void ApplyHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (!_settings.KeyboardShortcut || _messages is null) { Log.Info("Keyboard shortcut off"); return; }
        if (!KeyCombo.TryParse(_settings.WheelShortcut, out uint modifiers, out uint key)) KeyCombo.TryParse(KeyCombo.DefaultWheel, out modifiers, out key);
        // KeyCombo's modifier bits are RegisterHotKey's own (MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8).
        _hotkey = new GlobalHotkey(_messages, modifiers, key, OnHotkey);
        Log.Info(_hotkey.Registered ? $"Keyboard shortcut {_settings.WheelShortcut} ready" : $"{_settings.WheelShortcut} is taken by another app");
    }

    public bool KeyboardShortcutAvailable => _hotkey?.Registered ?? true;

    private bool _readingSelection;

    private async void OnHotkey()
    {
        if (Paused || _readingSelection) return;
        if (_interaction?.CycleIfOpenFromKeyboard() == true) return; // pressed again: next mode
        IReadOnlyList<string> files = ExplorerSelection.FromForegroundWindow();
        Log.Info($"Keyboard shortcut: {ExplorerSelection.LastResult}");
        bool vsCode = files.Count == 0 && _messages is not null && VsCodeSelection.IsForeground(out _);
        if (vsCode)
        {
            _readingSelection = true;
            try { files = await VsCodeSelection.ReadAsync(_messages!.Handle); }
            catch (Exception ex) { Log.Warn($"Reading the VS Code selection failed: {ex.Message}"); }
            finally { _readingSelection = false; }
            Log.Info($"Keyboard shortcut in VS Code: {files.Count} item(s)");
        }
        if (files.Count == 0 && !vsCode && _settings.UseClipboard && FromClipboard(ExplorerSelection.LastFolder) is { } clip)
        {
            Log.Info($"Keyboard shortcut: nothing selected, using the clipboard ({clip.Files.Count} item(s))");
            _interaction?.OpenForSelection(clip.Files, clip.OutputFolder, clip.Label);
            return;
        }
        if (files.Count == 0)
        {
            _toasts.ShowMessage(OrbitDrop.UI.Toasts.ToastKind.Info, "Select files first",
                vsCode ? $"Select files in VS Code's Explorer (or open a file), then press {ShortcutName}."
                : _settings.UseClipboard ? $"Select files in File Explorer, on the desktop or in VS Code, or copy an image, then press {ShortcutName}."
                : $"Select files in File Explorer, on the desktop or in VS Code, then press {ShortcutName}.",
                ScreenInfo.CursorPosition);
            return;
        }
        _interaction?.OpenForSelection(new FileSet(files));
    }

    /// <summary>"OrbitDrop" in File Explorer's right-click menu (Settings → Shortcuts): registered or removed to match.</summary>
    private void ApplyContextMenu()
    {
        string version = $"{UpdateManager.Current.Major}.{UpdateManager.Current.Minor}.{UpdateManager.Current.Patch}";
        bool ok = ContextMenu.Apply(_settings.ContextMenu, AppPackage.IsPackaged, AppContext.BaseDirectory, version);
        Log.Info($"Right-click menu {(_settings.ContextMenu ? "on" : "off")}{(ok ? "" : " (couldn't update the registration)")}");
    }

    /// <summary>The right-click menu sent files (WM_COPYDATA, checked by <see cref="ContextMenu.TryReadFiles"/>): open the wheel for them.</summary>
    private bool OnShellMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (ContextMenu.TryReadFiles(msg, lParam) is not { } files) return false;
        Log.Info($"Right-click menu: {files.Count} item(s)");
        if (files.Count > 0 && !Paused) Dispatcher.BeginInvoke(() => _interaction?.OpenForSelection(new FileSet(files)));
        return true;
    }

    /// <summary>Puts a Copy to / Move to destination at the top of the recent folders.</summary>
    private void RememberFolder(string folder) =>
        Dispatcher.BeginInvoke(() => Update(s => s.RecentFolders = [folder, .. s.RecentFolders]));

    private async Task<(bool, object?)> AskMetadataAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        var snapshot = await FileProperties.ReadAsync(files[0]);
        var window = new MetadataWindow(snapshot);
        ShowInFront(window);
        var edit = await window.Result;
        return edit is { IsEmpty: false } ? (true, edit) : (false, null);
    }

    /// <summary>
    /// Shows a window centred on the screen and brings it forward. OrbitDrop runs in the background, so Windows
    /// may otherwise open it behind File Explorer.
    /// </summary>
    private static void ShowInFront(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        Log.Info($"Opened {window.GetType().Name}");
    }
}
