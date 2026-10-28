using OrbitDrop.Core;

namespace OrbitDrop.Infrastructure;

public enum AppTheme { System, Light, Dark }
public enum WheelSize { Compact, Standard, Large }
public enum TriggerKey { Shift, Ctrl }

/// <summary>
/// User preferences. Every property here is wired to real behaviour; nothing is decorative.
/// Bump <see cref="CurrentSchemaVersion"/> and extend <see cref="SettingsStore"/> migration when the shape changes.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxRecentFolders = 4;
    public const int MaxLastUsed = 200;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // General
    public bool ShowCompletionNotifications { get; set; } = true;
    public bool RevealWhenDone { get; set; }
    public bool OnboardingCompleted { get; set; }

    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool UseWindowsAccent { get; set; }
    public WheelSize WheelSize { get; set; } = WheelSize.Standard;
    public bool ReduceMotion { get; set; }

    // Shortcuts
    public TriggerKey TriggerKey { get; set; } = TriggerKey.Shift;
    /// <summary>A keyboard shortcut (<see cref="WheelShortcut"/>) opens the wheel for the files selected in File Explorer.</summary>
    public bool KeyboardShortcut { get; set; } = true;
    /// <summary>That shortcut, like "Ctrl+Alt+Space" (see <see cref="KeyCombo"/>).</summary>
    public string WheelShortcut { get; set; } = KeyCombo.DefaultWheel;
    /// <summary>Letting go of a drag in the wheel's centre keeps the wheel open, to choose with clicks instead of holding.</summary>
    public bool KeepOpenOnCentre { get; set; }
    /// <summary>Ctrl+Alt+Space with nothing selected opens the wheel for what's on the clipboard: copied files or an image.</summary>
    public bool UseClipboard { get; set; } = true;
    /// <summary>"OrbitDrop" in File Explorer's right-click menu.</summary>
    public bool ContextMenu { get; set; } = true;

    // Wheel extras: off by default, so the wheel stays exactly as people know it unless they choose otherwise.
    /// <summary>The option last used for a kind of file moves to the top of its wheel.</summary>
    public bool LastUsedFirst { get; set; }
    /// <summary>Hovering a format or compress option shows the estimated result size.</summary>
    public bool ShowSizeEstimates { get; set; }
    /// <summary>The last option used, per wheel mode and kind of file ("Convert|.png" → "convert.image.webp").</summary>
    public Dictionary<string, string> LastUsed { get; set; } = [];

    // Results
    /// <summary>An Undo button on the "done" card: moves what was just made to the Recycle Bin.</summary>
    public bool OfferUndo { get; set; } = true;

    // Conversions
    public OutputLocation OutputLocation { get; set; } = OutputLocation.BesideOriginal;
    public string? OutputFolder { get; set; }
    public int JpegQuality { get; set; } = 92;

    /// <summary>Only drags that start in File Explorer or on the desktop open the wheel (off: any app's file drags do).</summary>
    public bool WorkOnlyInExplorer { get; set; }

    /// <summary>The Microsoft Store version's Premium tools show on the wheel with a crown (choosing one explains how to get them).</summary>
    public bool ShowPremiumTools { get; set; } = true;

    /// <summary>The user's own Actions: a program or command run with the dropped files (Settings → Your actions).</summary>
    public CustomAction[] CustomActions { get; set; } = [];

    // Advanced
    public bool ExtendedDragDetection { get; set; }
    public bool DetailedLogging { get; set; }

    // Updates: one anonymous request to GitHub Releases at most once a day, only while this is on.
    public bool CheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    /// <summary>Download and install updates without asking, at a moment OrbitDrop isn't in use.</summary>
    public bool AutoInstallUpdates { get; set; } = true;

    // Wheel: options and groups the user turned off (see WheelFilter).
    public string[] HiddenOptions { get; set; } = [];
    /// <summary>Slice order the user arranged (see WheelOrder); empty means the default order.</summary>
    public string[] OptionOrder { get; set; } = [];

    // Actions: the last few folders used with Copy to / Move to (most recent first).
    public string[] RecentFolders { get; set; } = [];

    // Rating: jobs that produced a result, and whether OrbitDrop has asked for a rating (it asks once, ever).
    public int SuccessfulJobs { get; set; }
    public bool RatingAsked { get; set; }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.LastUsed = new Dictionary<string, string>(LastUsed, StringComparer.Ordinal);
        copy.CustomActions = CustomActions.Select(a => a.Clone()).ToArray();
        return copy;
    }

    /// <summary>
    /// Groups that no longer exist (Copy path moved into Actions › Open): a hidden old group stays hidden as its options.
    /// </summary>
    private static readonly Dictionary<string, string[]> RetiredGroups = new(StringComparer.Ordinal)
    {
        ["group:actions.copy"] = ["action.copy-path", "action.copy-name"],
        ["group:image.search"] = ["action.image-search.google", "action.image-search.bing", "action.image-search.tineye"],
    };

    /// <summary>Clamps values that may have been hand-edited into an invalid range.</summary>
    public void Normalize()
    {
        JpegQuality = Math.Clamp(JpegQuality, 50, 100);
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
        if (!Enum.IsDefined(WheelSize)) WheelSize = WheelSize.Standard;
        if (!Enum.IsDefined(TriggerKey)) TriggerKey = TriggerKey.Shift;
        WheelShortcut = KeyCombo.Normalize(WheelShortcut) ?? KeyCombo.DefaultWheel;
        if (OutputLocation == OutputLocation.Folder && string.IsNullOrWhiteSpace(OutputFolder))
            OutputLocation = OutputLocation.BesideOriginal;
        HiddenOptions = (HiddenOptions ?? []).SelectMany(k => RetiredGroups.TryGetValue(k ?? "", out var members) ? members : [k])
            .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToArray();
        CustomActions = (CustomActions ?? []).Where(a => a is not null).Take(CustomAction.MaxActions)
            .Select(a => { a.Name = (a.Name ?? "").Trim(); a.Program = (a.Program ?? "").Trim(); a.Arguments ??= ""; a.Extensions ??= ""; if (!CustomAction.Kinds.Contains(a.Kind)) a.Kind = "Everything"; return a; })
            .ToArray();
        OptionOrder = (OptionOrder ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToArray();
        LastUsed = (LastUsed ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .Take(MaxLastUsed).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        RecentFolders = (RecentFolders ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecentFolders).ToArray();
    }
}
