using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using OrbitDrop.Conversion.Documents;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Conversion.Media;
using OrbitDrop.Conversion.Pdf;
using OrbitDrop.Core;
using OrbitDrop.Infrastructure;
using OrbitDrop.Jobs;
using OrbitDrop.Tools;
using OrbitDrop.Tools.Metadata;
using OrbitDrop.UI;
using OrbitDrop.UI.Localization;
using OrbitDrop.UI.Onboarding;
using OrbitDrop.UI.Settings;
using OrbitDrop.UI.Toasts;
using OrbitDrop.UI.Tray;
using OrbitDrop.UI.Wheel;
using OrbitDrop.Windows.DragDrop;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.App;

/// <summary>Composition root and lifecycle. OrbitDrop lives in the notification area; it has no main window.</summary>
public partial class App : Application, ISettingsHost, ITrayCommands
{
    private readonly AppPaths _paths = new();
    private SingleInstance? _instance;
    private SettingsStore _store = null!;
    private AppSettings _settings = new();
    private OperationRegistry _registry = null!;
    private MessageWindow? _messages;
    private SystemTheme? _systemTheme;
    private TrayIcon? _tray;
    private DragSessionDetector? _detector;
    private InteractionController? _interaction;
    private ShellIntegration _shell = null!;
    private ToastManager _toasts = null!;
    private SettingsWindow? _settingsWindow;
    private OnboardingWindow? _onboarding;
    private DispatcherTimer? _saveDebounce;
    private UpdateManager? _updates;
    private JobQueue? _jobs;
    private readonly InputBroker _inputs = new();
    private GlobalHotkey? _hotkey;
    private SettingsViewModel? _settingsVm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = e.Args;

        ThemeManager.Current.Initialize(this);
        UiText.Initialize();

        // Design-review renders run alongside a live instance and never touch settings or the screen.
        int film = Array.IndexOf(args, "--render-film");
        if (film >= 0)
        {
            string dir = film + 1 < args.Length ? args[film + 1] : Path.Combine(Environment.CurrentDirectory, "film-assets");
            int code;
            try { code = FilmAssets.Run(dir, BuildRegistry()); }
            catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        int store = Array.IndexOf(args, "--render-store");
        if (store >= 0)
        {
            string dir = store + 1 < args.Length ? args[store + 1] : Path.Combine(Environment.CurrentDirectory, "store-art");
            int code;
            try { code = StoreArt.Run(dir, BuildRegistry()); }
            catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        int frames = Array.IndexOf(args, "--render-guide-frames");
        if (frames >= 0)
        {
            string dir = frames + 1 < args.Length ? args[frames + 1] : Path.Combine(Environment.CurrentDirectory, "guide-frames");
            int code;
            try { code = GuideExport.Frames(dir, BuildRegistry()); }
            catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        int guide = Array.IndexOf(args, "--render-guide");
        if (guide >= 0)
        {
            string dir = guide + 1 < args.Length ? args[guide + 1] : Path.Combine(Environment.CurrentDirectory, "guide");
            int code;
            try { code = GuideExport.Run(dir, BuildRegistry()); }
            catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        int github = Array.IndexOf(args, "--render-github");
        if (github >= 0 && github + 1 < args.Length)
        {
            int code;
            try { code = UiReview.RunGitHub(args[github + 1], BuildRegistry()); }
            catch (Exception ex) { Directory.CreateDirectory(args[github + 1]); File.WriteAllText(Path.Combine(args[github + 1], "error.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        int render = Array.IndexOf(args, "--render-ui");
        if (render >= 0)
        {
            string dir = render + 1 < args.Length ? args[render + 1] : Path.Combine(Environment.CurrentDirectory, "ui-review");
            ThemeManager.Current.Apply(AppTheme.Light, false, false);
            int code;
            try { code = UiReview.Run(dir, BuildRegistry()); }
            catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            _instance.SignalFirstInstance(); // the running copy opens Settings
            Shutdown(0);
            return;
        }

        Log.Initialize(_paths.LogsDirectory);
        _store = new SettingsStore(_paths.SettingsFile);
        _settings = _store.Load();
        Log.Detailed = _settings.DetailedLogging;
        OrbitDrop.Windows.Diagnostics.Sink = Log.Info;
        Log.Info($"OrbitDrop {VersionText} starting | {Environment.OSVersion} | .NET {Environment.Version}");

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI exception", ex.Exception);
            ex.Handled = true; // keep the tray app alive; the failure is logged locally
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled exception", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("Unobserved task exception", ex.Exception); ex.SetObserved(); };

        ThemeManager.Current.Apply(_settings.Theme, _settings.UseWindowsAccent, _settings.ReduceMotion);

        ClipboardCaptures.Folder = Path.Combine(_paths.LocalDirectory, "Clipboard");
        _registry = BuildRegistry(new FileActionsProvider(() => _settings.RecentFolders, RememberFolder), () => _settings.CustomActions, () => _settings.ShowPremiumTools);
        _jobs = new JobQueue(_registry, maxConcurrentJobs: 2);
        var jobs = _jobs;
        _shell = new ShellIntegration(Dispatcher) { FloatHandler = ShowShelf };
        _toasts = new ToastManager { NotifyOnCompletion = () => _settings.ShowCompletionNotifications };

        _messages = new MessageWindow();
        _systemTheme = new SystemTheme(_messages);
        _messages.Message += OnShellMessage; // files chosen in File Explorer's right-click menu
        _systemTheme.Changed += () => Dispatcher.BeginInvoke(ApplyAppearance);

        _detector = new DragSessionDetector();
        _detector.Start();
        OrbitDrop.UI.Onboarding.InternalDrag.Starting = () => _detector?.ArmInternal();
        ApplyInteractionSettings();

        var wheel = new RadialWheel();
        wheel.Prewarm();
        RegisterInputs();
        _interaction = new InteractionController(wheel, _detector, _registry, jobs, _toasts, () => _settings, CreateContext, _shell, _inputs.AskAsync);
        _interaction.JobFinished += (_, outputs) => { if (outputs.Count > 0) OnSuccessfulJob(); };
        _interaction.RememberChoice = (key, slot) => Update(s => s.LastUsed[key] = slot);

        var iconStream = GetResourceStream(new Uri("pack://application:,,,/OrbitDrop.UI;component/Assets/orbitdrop.ico"))!.Stream;
        using (var ms = new MemoryStream()) { iconStream.CopyTo(ms); _tray = new TrayIcon(_messages, ms.ToArray(), TrayTooltip); }
        _tray.Invoked += pt => ShowTrayMenu(pt);
        _tray.ContextMenuRequested += pt => ShowTrayMenu(pt);
        _tray.Show();

        _updates = new UpdateManager(_toasts, () => _settings, Update, () => _instance?.ReleaseOwnership(), Quit, IsInUse);
        _updates.Changed += () => _settingsVm?.RefreshUpdates();
        if (!AppPackage.IsPackaged) _updates.Start(); // GitHub version: our own updater
        else StartStoreUpdates();                      // Store version: install Store updates as soon as idle

        ApplyHotkey();
        ApplyContextMenu();
        _instance.ListenForActivation(() => Dispatcher.BeginInvoke(() => OpenSettings()));

        // The silent installer relaunches with --updated after an in-app update.
        bool updated = args.Contains("--updated");
        bool background = args.Contains("--background") || updated || AppPackage.LaunchedAtSignIn;
        if (updated)
            _toasts.ShowMessage(ToastKind.Success, "OrbitDrop updated", $"You're now on version {UpdateManager.Current}. Your settings were kept.", ScreenInfo.CursorPosition);
        else if (!_settings.OnboardingCompleted && !background) ShowOnboarding();
        else if (!background)
            _toasts.ShowMessage(ToastKind.Info, "OrbitDrop is running",
                $"Drag files and tap {TriggerKeyName}. Find OrbitDrop in the notification area.", ScreenInfo.CursorPosition);

        Log.Info("Ready");
    }

    /// <param name="showPremium">Show the Store version's Premium tools with a crown (Settings › General › Show Premium tools).</param>
    private static OperationRegistry BuildRegistry(FileActionsProvider? actions = null, Func<IReadOnlyList<CustomAction>>? customActions = null, Func<bool>? showPremium = null) =>
        new([new ImageConversionProvider(), new PdfProvider(), new PdfToolsProvider(), new DocumentProvider(), new MediaFoundationProvider(), new MediaEditingProvider(),
             new ImageToolsProvider(), new ImageTextProvider(), new ArchiveProvider(), new MetadataProvider(), actions ?? new FileActionsProvider(),
             new CustomActionsProvider(customActions ?? (() => [])), new PrintProvider(), new PremiumPreviewProvider(showPremium ?? (() => true))],
            licensed: () => false, showLocked: showPremium ?? (() => true));

    private OperationContext CreateContext() => new()
    {
        Output = new OutputOptions(_settings.OutputLocation, _settings.OutputFolder),
        JpegQuality = _settings.JpegQuality,
        Shell = _shell,
    };

    private string TrayTooltip => Paused ? "OrbitDrop (paused)" : $"OrbitDrop · drag files and tap {TriggerKeyName}";

    private void ApplyAppearance()
    {
        ThemeManager.Current.Apply(_settings.Theme, _settings.UseWindowsAccent, _settings.ReduceMotion);
    }

    private void ApplyInteractionSettings()
    {
        if (_detector is null) return;
        _detector.SetTriggerKey(_settings.TriggerKey == TriggerKey.Ctrl);
        _detector.SetExtendedDetection(_settings.ExtendedDragDetection);
        _detector.ExplorerOnly = _settings.WorkOnlyInExplorer;
    }

    private void ShowTrayMenu(OrbitDrop.Windows.Interop.Win32.POINT anchor) => new TrayFlyout(this).ShowAt(anchor);

    // ------------------------------------------------------------------ ITrayCommands

    public bool Paused
    {
        get => _detector is { Enabled: false };
        set
        {
            if (_detector is null) return;
            _detector.Enabled = !value;
            _tray?.SetTooltip(TrayTooltip);
            Log.Info(value ? "Paused" : "Resumed");
        }
    }

    public string TriggerKeyName => _settings.TriggerKey == TriggerKey.Ctrl ? "Ctrl" : "Shift";

    public void OpenSettings(string? page = null)
    {
        if (_settingsWindow is null)
        {
            _settingsVm = new SettingsViewModel(this);
            _settingsWindow = new SettingsWindow(_settingsVm);
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _settingsVm = null; };
            _settingsWindow.Show();
        }
        if (page is not null) _settingsWindow.ShowPage(page);
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public void ShowOnboarding()
    {
        if (_onboarding is not null) { _onboarding.Activate(); return; }
        var assets = new GuideAssets(_registry);
        _onboarding = new OnboardingWindow(TriggerKeyName, assets, Path.Combine(_paths.LocalDirectory, "Guide"));
        _onboarding.Completed += () => Update(s => s.OnboardingCompleted = true);
        _onboarding.ShowResultRequested += path => _shell.Reveal([path]);
        void OnJob(IReadOnlyList<string> sources, IReadOnlyList<string> outputs) => _onboarding?.OnJobFinished(sources, outputs);
        if (_interaction is not null) _interaction.JobFinished += OnJob;
        _onboarding.Closed += (_, _) =>
        {
            if (_interaction is not null) _interaction.JobFinished -= OnJob;
            assets.Dispose();
            _onboarding = null;
        };
        _onboarding.Show();
        _onboarding.Activate();
    }

    public void Quit()
    {
        Log.Info("Exiting");
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _saveDebounce?.Stop();
        _updates?.Stop();
        if (_store is not null && _instance?.IsFirst == true) SaveNow();
        _hotkey?.Dispose();
        _interaction?.Dispose();
        _detector?.Dispose();
        _tray?.Dispose();
        _systemTheme?.Dispose();
        _messages?.Dispose();
        _instance?.Dispose();
        Log.Shutdown();
        base.OnExit(e);
    }

    // ------------------------------------------------------------------ ISettingsHost

    AppSettings ISettingsHost.Settings => _settings;

    public void Update(Action<AppSettings> change)
    {
        var before = _settings.Clone();
        change(_settings);
        _settings.Normalize();

        if (before.Theme != _settings.Theme || before.UseWindowsAccent != _settings.UseWindowsAccent || before.ReduceMotion != _settings.ReduceMotion)
            ApplyAppearance();
        if (before.KeyboardShortcut != _settings.KeyboardShortcut) ApplyHotkey();
        if (before.ContextMenu != _settings.ContextMenu) ApplyContextMenu();
        if (before.TriggerKey != _settings.TriggerKey || before.ExtendedDragDetection != _settings.ExtendedDragDetection || before.WorkOnlyInExplorer != _settings.WorkOnlyInExplorer)
        {
            ApplyInteractionSettings();
            _tray?.SetTooltip(TrayTooltip);
        }
        Log.Detailed = _settings.DetailedLogging;
        ScheduleSave();
    }

    public bool LaunchAtStartup
    {
        get { try { return StartupRegistration.IsEnabled; } catch { return false; } }
        set
        {
            try { StartupRegistration.Set(value); Log.Info($"Launch at sign-in {(value ? "on" : "off")}"); }
            catch (Exception ex) { Log.Error("Could not change startup registration", ex); }
        }
    }

    public bool AccentFallbackActive => ThemeManager.Current.AccentFallbackActive;

    public IEnumerable<(string Category, string Detail)> Capabilities => _registry.Providers.SelectMany(p => p.DescribeCapabilities());

    public string VersionText =>
        UiText.Format("Version {0}", Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev");

    public string SettingsPath => _paths.SettingsFile;
    public string LogsPath => _paths.LogsDirectory;

    public string? LicensePath
    {
        get
        {
            string p = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            return File.Exists(p) ? p : null;
        }
    }

    public void PreviewWheel() => _interaction?.Preview();

    public void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error($"Could not open {path}", ex); }
    }

    public void ResetSettings()
    {
        bool onboarded = _settings.OnboardingCompleted;
        _settings = new AppSettings { OnboardingCompleted = onboarded };
        ApplyAppearance();
        ApplyInteractionSettings();
        Log.Detailed = false;
        SaveNow();
        Log.Info("Settings reset to defaults");
    }

    // ------------------------------------------------------------------ Updates (Settings → About and the tray menu)

    public string UpdateStatusText
    {
        get
        {
            if (AppPackage.IsPackaged) return UiText.Format("You have version {0}. The Microsoft Store keeps OrbitDrop up to date.", UpdateManager.Current);
            if (_updates is null) return "";
            string last = _settings.LastUpdateCheck is { } t ? UiText.Format(" · checked {0}", Ago(t)) : "";
            return _updates.State switch
            {
                UpdateState.Checking => UiText.T("Checking GitHub for a newer version…"),
                UpdateState.Ready when _updates.Available is { } r =>
                    UiText.Format("OrbitDrop {0} is downloaded and installs by itself the next time you're not using OrbitDrop.", r.Version),
                UpdateState.Available or UpdateState.Downloading or UpdateState.Installing when _updates.Available is { } r =>
                    UpdateManager.IsInstalled ? UiText.Format("OrbitDrop {0} is available. Installing takes a few seconds and keeps your settings.", r.Version)
                                              : UiText.Format("OrbitDrop {0} is available. This is the portable copy, so download it from GitHub.", r.Version),
                UpdateState.Failed => $"{_updates.Error}{last}",
                _ when _settings.LastUpdateCheck is null => UiText.Format("You have version {0}. Not checked yet.", UpdateManager.Current),
                _ => UiText.Format("You're up to date (version {0}){1}", UpdateManager.Current, last),
            };
        }
    }

    public string UpdateButtonText => UiText.T(AppPackage.IsPackaged ? "Open Store" : _updates?.State switch
    {
        UpdateState.Checking => "Checking…",
        UpdateState.Downloading => "Downloading…",
        UpdateState.Installing => "Installing…",
        UpdateState.Available => UpdateManager.IsInstalled ? "Install update" : "Download",
        UpdateState.Ready => "Install now",
        _ => "Check now",
    });

    public bool UpdateBusy => _updates?.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;

    /// <summary>The Store version has no updater of its own: these open the Store's Downloads page.</summary>
    public bool StoreManagedUpdates => AppPackage.IsPackaged;

    public void UpdateButtonClicked()
    {
        if (AppPackage.IsPackaged) { UpdateManager.OpenUrl("ms-windows-store://downloadsandupdates"); return; }
        if (_updates is null) return;
        if (_updates.State is UpdateState.Available or UpdateState.Ready) _ = _updates.InstallAsync();
        else _ = _updates.CheckAsync(manual: true);
    }

    private IReadOnlyList<OrbitDrop.UI.Settings.WheelCategory>? _wheelCatalog;
    public IReadOnlyList<OrbitDrop.UI.Settings.WheelCategory> WheelCatalog => _wheelCatalog ??= WheelCatalogBuilder.Build(_registry);

    public bool UpdateAvailable => _updates?.Available is not null;

    public string UpdateMenuText => _updates?.Available is { } r
        ? $"{(UpdateManager.IsInstalled ? "Install" : "Download")} OrbitDrop {r.Version}"
        : "Check for updates";

    public void UpdateMenuClicked() => UpdateButtonClicked();

    /// <summary>
    /// OrbitDrop is in use: the wheel is open, something is converting, or one of its windows (Settings, editor,
    /// guide, Float shelf, a picker…) is open. Automatic updates wait for none of these to be true.
    /// </summary>
    private bool IsInUse()
    {
        if (_interaction?.IsActive == true || (_jobs?.ActiveJobs ?? 0) > 0) return true;
        foreach (Window w in Windows)
            if (w.IsVisible && w is not ToastWindow && w is not RadialWheel) return true;
        return false;
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.UtcNow - t;
        if (d < TimeSpan.FromMinutes(1)) return UiText.T("just now");
        if (d < TimeSpan.FromHours(1)) return UiText.Format("{0} min ago", (int)d.TotalMinutes);
        if (d < TimeSpan.FromDays(1)) return UiText.Format("{0} h ago", (int)d.TotalHours);
        return t.ToLocalTime().ToString("d MMM");
    }

    private void ScheduleSave()
    {
        _saveDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveDebounce.Tick -= OnSaveTick;
        _saveDebounce.Tick += OnSaveTick;
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        _saveDebounce?.Stop();
        SaveNow();
    }

    private void SaveNow()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) { Log.Error("Could not save settings", ex); }
    }
}
