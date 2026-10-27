using System.Windows;
using OrbitDrop.Core;
using OrbitDrop.Infrastructure;
using OrbitDrop.UI.Common;
using OrbitDrop.UI.Localization;

namespace OrbitDrop.UI.Settings;

/// <summary>What the Settings window needs from the app. Every member has a real effect.</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }
    /// <summary>Applies a change, persists it and triggers its side effects (theme, detector, …).</summary>
    void Update(Action<AppSettings> change);
    bool LaunchAtStartup { get; set; }
    bool AccentFallbackActive { get; }
    IEnumerable<(string Category, string Detail)> Capabilities { get; }
    string VersionText { get; }
    string SettingsPath { get; }
    string LogsPath { get; }
    string? LicensePath { get; }
    void PreviewWheel();
    void ShowOnboarding();
    void OpenPath(string path);
    void ResetSettings();
    // Updates (defaults keep design-review hosts simple)
    string UpdateStatusText => "You're up to date";
    string UpdateButtonText => "Check now";
    bool UpdateBusy => false;
    bool KeyboardShortcutAvailable => true;
    /// <summary>The Store version: the Microsoft Store handles updates, so the update switches are hidden.</summary>
    bool StoreManagedUpdates => false;
    /// <summary>Every option OrbitDrop has, for the Wheel page (built by the app from the live registry).</summary>
    IReadOnlyList<WheelCategory> WheelCatalog => [];
    void UpdateButtonClicked() { }
    /// <summary>"Rate" in the Store version (the Store's own rating window); "Star on GitHub" otherwise.</summary>
    string RateButtonText => "Rate";
    string RateHint => "A quick rating helps other people find OrbitDrop.";
    void RateClicked() { }
    /// <summary>The upgrade guide, opened because the user's actions reached the free limit.</summary>
    void ShowActionsUpgrade() { }
    /// <summary>About › Switch to the Microsoft Store version.</summary>
    void SwitchToStore() { }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;

    public SettingsViewModel(ISettingsHost host)
    {
        _host = host;
        PreviewWheelCommand = new RelayCommand(host.PreviewWheel);
        ShowOnboardingCommand = new RelayCommand(host.ShowOnboarding);
        OpenLogsCommand = new RelayCommand(() => host.OpenPath(host.LogsPath));
        OpenSettingsFileCommand = new RelayCommand(() => host.OpenPath(Path.GetDirectoryName(host.SettingsPath)!));
        OpenLicenseCommand = new RelayCommand(() => { if (host.LicensePath is { } p) host.OpenPath(p); }, () => host.LicensePath is not null);
        ResetCommand = new RelayCommand(() => { host.ResetSettings(); RaiseAll(); });
        UpdateCommand = new RelayCommand(host.UpdateButtonClicked);
        RateCommand = new RelayCommand(host.RateClicked);
    }

    private AppSettings S => _host.Settings;

    private void Set(Action<AppSettings> change, params string[] related)
    {
        _host.Update(change);
        foreach (var r in related) Raise(r);
    }

    // ---- General
    public bool LaunchAtStartup { get => _host.LaunchAtStartup; set { _host.LaunchAtStartup = value; Raise(); } }
    public bool ShowCompletionNotifications { get => S.ShowCompletionNotifications; set => Set(s => s.ShowCompletionNotifications = value, nameof(ShowCompletionNotifications)); }
    public bool RevealWhenDone { get => S.RevealWhenDone; set => Set(s => s.RevealWhenDone = value, nameof(RevealWhenDone)); }

    // ---- Appearance
    public bool ThemeSystem { get => S.Theme == AppTheme.System; set { if (value) SetTheme(AppTheme.System); } }
    public bool ThemeLight { get => S.Theme == AppTheme.Light; set { if (value) SetTheme(AppTheme.Light); } }
    public bool ThemeDark { get => S.Theme == AppTheme.Dark; set { if (value) SetTheme(AppTheme.Dark); } }
    private void SetTheme(AppTheme t) => Set(s => s.Theme = t, nameof(ThemeSystem), nameof(ThemeLight), nameof(ThemeDark), nameof(AccentFallbackVisible));

    public bool UseWindowsAccent { get => S.UseWindowsAccent; set => Set(s => s.UseWindowsAccent = value, nameof(UseWindowsAccent), nameof(AccentFallbackVisible)); }
    public bool AccentFallbackVisible => S.UseWindowsAccent && _host.AccentFallbackActive;

    public bool SizeCompact { get => S.WheelSize == WheelSize.Compact; set { if (value) SetSize(WheelSize.Compact); } }
    public bool SizeStandard { get => S.WheelSize == WheelSize.Standard; set { if (value) SetSize(WheelSize.Standard); } }
    public bool SizeLarge { get => S.WheelSize == WheelSize.Large; set { if (value) SetSize(WheelSize.Large); } }
    private void SetSize(WheelSize size) => Set(s => s.WheelSize = size, nameof(SizeCompact), nameof(SizeStandard), nameof(SizeLarge));

    public bool ReduceMotion { get => S.ReduceMotion; set => Set(s => s.ReduceMotion = value, nameof(ReduceMotion)); }

    // ---- Shortcuts
    public bool TriggerShift { get => S.TriggerKey == TriggerKey.Shift; set { if (value) SetTrigger(TriggerKey.Shift); } }
    public bool TriggerCtrl { get => S.TriggerKey == TriggerKey.Ctrl; set { if (value) SetTrigger(TriggerKey.Ctrl); } }
    public string TriggerName => S.TriggerKey == TriggerKey.Ctrl ? "Ctrl" : "Shift";
    public bool KeyboardShortcut { get => S.KeyboardShortcut; set => Set(s => s.KeyboardShortcut = value, nameof(KeyboardShortcut), nameof(KeyboardShortcutHint), nameof(UseClipboardHint)); }
    public string KeyboardShortcutHint => UiText.T(_host.KeyboardShortcutAvailable
        ? "Select files in File Explorer, on the desktop or in VS Code, press it, then click an option. Click the centre (or press it again) for Tools and Actions."
        : "Another app is using this shortcut, so OrbitDrop couldn't claim it.");
    public bool UseClipboard { get => S.UseClipboard; set => Set(s => s.UseClipboard = value, nameof(UseClipboard)); }
    public string UseClipboardHint => UiText.T(S.KeyboardShortcut
        ? "With nothing selected, Ctrl + Alt + Space opens the wheel for what you copied: files, or an image such as a screenshot. Copied images are saved to the folder you're in, or Pictures."
        : "Needs Ctrl + Alt + Space (above), which is off.");
    public bool ContextMenu { get => S.ContextMenu; set => Set(s => s.ContextMenu = value, nameof(ContextMenu)); }
    public string ContextMenuHint => UiText.T(OrbitDrop.Windows.Shell.AppPackage.IsPackaged
        ? "Right-click files or folders in File Explorer and choose OrbitDrop to open the wheel for them."
        : "Right-click files or folders in File Explorer and choose OrbitDrop (on Windows 11, under “Show more options”).");
    public bool LastUsedFirst { get => S.LastUsedFirst; set => Set(s => s.LastUsedFirst = value, nameof(LastUsedFirst)); }
    public bool ShowSizeEstimates { get => S.ShowSizeEstimates; set => Set(s => s.ShowSizeEstimates = value, nameof(ShowSizeEstimates)); }
    public bool OfferUndo { get => S.OfferUndo; set => Set(s => s.OfferUndo = value, nameof(OfferUndo)); }
    public bool KeepOpenOnCentre { get => S.KeepOpenOnCentre; set => Set(s => s.KeepOpenOnCentre = value, nameof(KeepOpenOnCentre), nameof(CentreReleaseHint)); }
    public string CentreReleaseHint => UiText.T(S.KeepOpenOnCentre ? "Keep the wheel open: click an option, or outside to close" : "Cancel without doing anything");
    private void SetTrigger(TriggerKey k) => Set(s => s.TriggerKey = k, nameof(TriggerShift), nameof(TriggerCtrl), nameof(TriggerName));

    // ---- Conversions
    public bool OutputBeside { get => S.OutputLocation == OutputLocation.BesideOriginal; set { if (value) Set(s => s.OutputLocation = OutputLocation.BesideOriginal, nameof(OutputBeside), nameof(OutputToFolder)); } }
    public bool OutputToFolder { get => S.OutputLocation == OutputLocation.Folder; set { if (value && S.OutputFolder is not null) Set(s => s.OutputLocation = OutputLocation.Folder, nameof(OutputBeside), nameof(OutputToFolder)); } }
    public string OutputFolderText => S.OutputFolder ?? UiText.T("No folder chosen");
    public bool HasOutputFolder => S.OutputFolder is not null;

    public void ChooseOutputFolder(string folder) =>
        Set(s => { s.OutputFolder = folder; s.OutputLocation = OutputLocation.Folder; },
            nameof(OutputFolderText), nameof(HasOutputFolder), nameof(OutputBeside), nameof(OutputToFolder));

    public bool Quality75 { get => S.JpegQuality == 75; set { if (value) SetQuality(75); } }
    public bool Quality85 { get => S.JpegQuality == 85; set { if (value) SetQuality(85); } }
    public bool Quality92 { get => S.JpegQuality == 92; set { if (value) SetQuality(92); } }
    public bool Quality100 { get => S.JpegQuality == 100; set { if (value) SetQuality(100); } }
    private void SetQuality(int q) => Set(s => s.JpegQuality = q, nameof(Quality75), nameof(Quality85), nameof(Quality92), nameof(Quality100));

    public IReadOnlyList<CapabilityRow> Capabilities =>
        _host.Capabilities.Select(c => new CapabilityRow(UiText.T(c.Category), UiText.T(c.Detail))).ToList();

    // ---- Advanced
    public bool ExtendedDragDetection { get => S.ExtendedDragDetection; set => Set(s => s.ExtendedDragDetection = value, nameof(ExtendedDragDetection)); }
    public bool DetailedLogging { get => S.DetailedLogging; set => Set(s => s.DetailedLogging = value, nameof(DetailedLogging)); }
    public bool WorkOnlyInExplorer { get => S.WorkOnlyInExplorer; set => Set(s => s.WorkOnlyInExplorer = value, nameof(WorkOnlyInExplorer)); }
    public bool ShowPremiumTools { get => S.ShowPremiumTools; set => Set(s => s.ShowPremiumTools = value, nameof(ShowPremiumTools)); }

    // ---- Your actions (the first two are free; more with the Store version's upgrade)
    /// <summary>This version has no upgrade: it's in the Microsoft Store version.</summary>
    public bool Licensed => false;
    public IReadOnlyList<CustomAction> CustomActions => S.CustomActions;
    public void SetCustomActions(IEnumerable<CustomAction> actions) => _host.Update(s => s.CustomActions = actions.Select(a => a.Clone()).ToArray());
    /// <summary>Adding past the free actions: the upgrade guide, saying so.</summary>
    public void ShowActionsUpgrade() => _host.ShowActionsUpgrade();
    public void SwitchToStore() => _host.SwitchToStore();
    public string SettingsPath => _host.SettingsPath;
    public string LogsPath => _host.LogsPath;

    // ---- Wheel (customization)
    public IReadOnlyList<WheelCategory> WheelCatalog => _host.WheelCatalog;
    public IReadOnlySet<string> HiddenOptions => new HashSet<string>(S.HiddenOptions, StringComparer.Ordinal);

    public IReadOnlyList<string> OptionOrder => S.OptionOrder;

    public void SetWheel(IEnumerable<string> hidden, IEnumerable<string> order) =>
        Set(s => { s.HiddenOptions = hidden.ToArray(); s.OptionOrder = order.ToArray(); }, nameof(HiddenCount));

    public int HiddenCount => S.HiddenOptions.Length;

    // ---- About
    public string VersionText => _host.VersionText.StartsWith("Version ", StringComparison.Ordinal)
        ? UiText.Format("Version {0}", _host.VersionText["Version ".Length..])
        : UiText.T(_host.VersionText);
    public Visibility UpdateSwitchesVisibility => _host.StoreManagedUpdates ? Visibility.Collapsed : Visibility.Visible;
    public bool CheckForUpdates { get => S.CheckForUpdates; set => Set(s => s.CheckForUpdates = value, nameof(CheckForUpdates)); }
    public bool AutoInstallUpdates { get => S.AutoInstallUpdates; set => Set(s => s.AutoInstallUpdates = value, nameof(AutoInstallUpdates)); }
    public string UpdateStatusText => UiText.T(_host.UpdateStatusText);
    public string UpdateButtonText => UiText.T(_host.UpdateButtonText);
    public bool UpdateButtonEnabled => !_host.UpdateBusy;
    public RelayCommand UpdateCommand { get; }

    /// <summary>Called by the app when an update check or download changes state.</summary>
    public void RefreshUpdates()
    {
        Raise(nameof(UpdateStatusText));
        Raise(nameof(UpdateButtonText));
        Raise(nameof(UpdateButtonEnabled));
    }

    public RelayCommand RateCommand { get; }
    public string RateButtonText => UiText.T(_host.RateButtonText);
    public string RateHint => UiText.T(_host.RateHint);
    public RelayCommand PreviewWheelCommand { get; }
    public RelayCommand ShowOnboardingCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenSettingsFileCommand { get; }
    public RelayCommand OpenLicenseCommand { get; }
    public RelayCommand ResetCommand { get; }

    /// <summary>Refreshes values that can change outside the window (e.g. accent fallback after a system theme change).</summary>
    public void Refresh() => RaiseAll();
}

public sealed record CapabilityRow(string Category, string Detail);
