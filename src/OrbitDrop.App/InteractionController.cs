using System.Diagnostics;
using System.Windows.Threading;
using OrbitDrop.Core;
using OrbitDrop.Infrastructure;
using OrbitDrop.Jobs;
using OrbitDrop.UI.Toasts;
using OrbitDrop.UI.Wheel;
using OrbitDrop.Windows.DragDrop;
using OrbitDrop.Windows.Interop;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.App;

/// <summary>
/// The signature interaction, ported from Orbit Probe's validated state machine:
/// drag armed → trigger tap → wheel → OLE DragEnter (files known before the drop) → hover → drop → job → toast.
/// Changes from the probe are only those the feasibility log required (R4, R7, R8, R9).
/// </summary>
public sealed class InteractionController : IDropSink, IDisposable
{
    private sealed class Session
    {
        public FileSet Files = FileSet.Empty;
        public OperationMode Mode = OperationMode.Convert;
        /// <summary>Every option for the current mode; <see cref="Items"/> is what the wheel shows (top level or one group).</summary>
        public IReadOnlyList<OperationDescriptor> Flat = [];
        public IReadOnlyList<OperationDescriptor> Items = [];
        public OperationDescriptor? OpenGroup;
        /// <summary>Opened from the keyboard on selected files: the pointer hovers and clicks instead of dragging.</summary>
        public bool ClickMode;
        public int LastHit = WheelGeometry.HitOutside;
        public bool EnterSeen, OverWheel, Dropped;
        /// <summary>The drag came from inside a ZIP (or a phone or an email): there are no real files to work on.</summary>
        public bool FromArchive;
        /// <summary>Set when Convert had nothing for these files and the wheel opened in another mode.</summary>
        public string? UnsupportedNote;
        public Win32.POINT Anchor;
        public long OpenedAt;
        /// <summary>Where results go instead of next to the files (e.g. a clipboard image goes to the folder in front).</summary>
        public string? OutputFolder;
        /// <summary>What the centre calls the files instead of their name ("Copied image").</summary>
        public string? SourceLabel;
        /// <summary>Estimated result sizes by option id (Settings → Wheel → Show estimated size), and the files' own total.</summary>
        public IReadOnlyDictionary<string, long>? Estimates;
        public long OriginalBytes;
        /// <summary>Options on the busiest page or group for these files: the wheel's size for the whole drag (0: not worked out yet).</summary>
        public int Busiest;
        public CancellationTokenSource? EstimateCts;
    }

    private readonly RadialWheel _wheel;
    private readonly DragSessionDetector _detector;
    private readonly OperationRegistry _registry;
    private readonly JobQueue _jobs;
    private readonly ToastManager _toasts;
    private readonly Func<AppSettings> _settings;
    private readonly Func<OperationContext> _context;
    private readonly IShellIntegration _shell;
    private readonly NativeDropTarget _dropTarget;
    private Session? _session;
    private DispatcherTimer? _deferredDismiss;
    private DispatcherTimer? _previewTimer;
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromMilliseconds(DwellMs) };
    private readonly Func<OperationDescriptor, FileSet, Win32.POINT, Task<(bool Ok, object? Input)>> _askInput;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>How long the pointer rests on a group (or on the centre inside one) before the wheel switches.</summary>
    private const int DwellMs = 380;

    public InteractionController(RadialWheel wheel, DragSessionDetector detector, OperationRegistry registry, JobQueue jobs,
        ToastManager toasts, Func<AppSettings> settings, Func<OperationContext> context, IShellIntegration shell,
        Func<OperationDescriptor, FileSet, Win32.POINT, Task<(bool Ok, object? Input)>> askInput)
    {
        _askInput = askInput;
        _dwell.Tick += (_, _) => OnDwell();
        _hover.Tick += (_, _) => { if (_session is { ClickMode: true }) OnDragOver(ScreenInfo.CursorPosition); };
        _wheel = wheel;
        _detector = detector;
        _registry = registry;
        _jobs = jobs;
        _toasts = toasts;
        _settings = settings;
        _context = context;
        _shell = shell;

        _dropTarget = new NativeDropTarget(wheel.Handle, this);
        int hr = _dropTarget.Register();
        Log.Info($"Wheel drop target registered (hr=0x{hr:X8})");

        _detector.TriggerPressed += OnTrigger;
        _detector.EscapePressed += () => Dismiss(confirmed: false, "Esc");
        _detector.DragButtonReleased += OnDragButtonReleased;
    }

    /// <summary>Saves the slice last used for a wheel (key from <see cref="WheelMemory.KeyFor"/>), when that setting is on.</summary>
    public Action<string, string>? RememberChoice { get; set; }

    /// <summary>A wheel is open (dragging, keyboard flow or preview).</summary>
    public bool IsActive => _session is not null;

    /// <summary>Every finished job: (input files, files written). Used by the guide's practice step.</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>>? JobFinished;

    /// <summary>Every finished job: the option that ran and the files it wrote (counts free tries of paid options).</summary>
    public event Action<OperationDescriptor, IReadOnlyList<string>>? OperationFinished;

    private string TriggerName => _settings().TriggerKey == TriggerKey.Ctrl ? "Ctrl" : "Shift";

    // ------------------------------------------------------------------ session lifecycle

    private void OnTrigger()
    {
        if (_session is not null) { CycleMode(); return; }
        Open();
    }

    /// <summary>The page the wheel opens on (the app passes the user's choice when the upgrade is unlocked).</summary>
    public Func<OperationMode> StartMode { get; set; } = () => OperationMode.Convert;

    private void Open()
    {
        _previewTimer?.Stop();
        _deferredDismiss?.Stop();
        long t0 = Stopwatch.GetTimestamp();
        var cursor = ScreenInfo.CursorPosition;
        _session = new Session { Anchor = cursor, OpenedAt = t0, Mode = StartMode() };
        _detector.SessionOpen = true;
        _wheel.SetContent(_session.Mode, [], "Move here", "", animate: false);
        _wheel.Open(cursor, WheelGeometry.SizeFactor(_settings().WheelSize), animate: true);
        Log.Info($"Wheel opened in {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:F1} ms");
    }

    /// <summary>
    /// What the wheel offers for these files: the registry's options minus the ones the user turned off, in the user's
    /// order, and (only if they turned that on) with the last one used for these files at the top.
    /// </summary>
    private IReadOnlyList<OperationDescriptor> Options(FileSet files, OperationMode mode)
    {
        var settings = _settings();
        var options = WheelOrder.Apply(WheelFilter.Apply(_registry.Query(files, mode), new HashSet<string>(settings.HiddenOptions, StringComparer.Ordinal)),
            settings.OptionOrder);
        // A copied image lives in a staging folder: of the actions, only sharing and the shelf make sense for it.
        if (mode == OperationMode.Actions && files.All(ClipboardCaptures.IsCapture))
            options = options.Where(o => o.Id is "action.share" or "action.float").ToList();
        return settings.LastUsedFirst && settings.LastUsed.TryGetValue(WheelMemory.KeyFor(mode, files), out var last)
            ? WheelMemory.Apply(options, last) : options;
    }

    /// <summary>"Tools" / "Actions" / "Convert": what the next switch leads to (for the centre's hint).</summary>
    private string NextModeLabel(Session s) => NextMode(s, s.Mode, includeCurrent: false).ToString();

    /// <summary>Another mode has options for these files (otherwise switching does nothing).</summary>
    private bool HasOtherMode(Session s) => NextMode(s, s.Mode, includeCurrent: false) != s.Mode;

    /// <summary>The keyboard shortcut pressed again while its wheel is open: switch to the next mode.</summary>
    public bool CycleIfOpenFromKeyboard()
    {
        if (_session is not { ClickMode: true }) return false;
        CycleMode();
        return true;
    }

    private void CycleMode()
    {
        if (_session is not { } s) return;
        var next = NextMode(s, s.Mode, includeCurrent: false);
        if (next == s.Mode && s.EnterSeen) return; // only one mode has options: nothing to switch to
        s.Mode = next;
        s.OpenGroup = null;
        Refresh(animate: true);
    }

    private OperationMode NextMode(Session s, OperationMode from, bool includeCurrent)
    {
        for (int i = includeCurrent ? 0 : 1; i <= 3; i++)
        {
            var m = (OperationMode)(((int)from + i) % 3);
            if (!s.EnterSeen || Options(s.Files, m).Count > 0) return m;
        }
        return includeCurrent ? from : (OperationMode)(((int)from + 1) % 3); // nothing anywhere
    }

    private void Refresh(bool animate)
    {
        if (_session is not { } s) return;
        s.Flat = Options(s.Files, s.Mode);
        // One size for the whole drag, from the busiest page or group, so moving between pages never makes it jump.
        if (s.EnterSeen && s.Busiest == 0)
            s.Busiest = Math.Max(1, Enum.GetValues<OperationMode>().Max(m => OperationMenu.Busiest(Options(s.Files, m))));
        _wheel.Busiest = s.Busiest;
        s.Items = s.OpenGroup is { } g ? OperationMenu.Members(s.Flat, g.Group!.Id) : OperationMenu.TopLevel(s.Flat);
        s.LastHit = WheelGeometry.HitOutside;
        _dwell.Stop();
        var (value, detail) = Summary(s);
        _wheel.SetContent(s.Mode, s.Items, value, detail, animate);
    }

    private (string Value, string Detail) Summary(Session s)
    {
        if (!s.EnterSeen) return ("Move here", "");
        if (s.OpenGroup is { } g) return (g.Label, s.ClickMode ? "Click the centre to go back" : "Rest on the centre to go back");
        if (s.Files.IsEmpty) return s.FromArchive ? ("Extract it first", "Files inside a ZIP aren't on disk yet") : ("No files", "Only files can be dropped here");
        if (s.Items.Count == 0) return ("Not supported", $"{KindSummary(s.Files)} isn't supported yet");
        if (s.Files.Count > 1) return ($"{s.Files.Count} files", s.UnsupportedNote ?? KindSummary(s.Files));
        string size;
        try { size = HumanSize(new FileInfo(s.Files[0]).Length); } catch { size = "1 file"; }
        return (size, s.UnsupportedNote ?? s.SourceLabel ?? ShortName(Path.GetFileName(s.Files[0])));
    }

    /// <summary>Long names lose their middle, never their end: "Filee is a free, open…converter.mp4".</summary>
    internal static string ShortName(string name, int max = 30)
    {
        if (name.Length <= max) return name;
        string ext = Path.GetExtension(name);
        int tail = Math.Min(name.Length - ext.Length, 8) + ext.Length, head = max - tail - 1;
        if (head < 6) return name[..(max - 1)] + "…"; // an absurdly long extension: plain trim
        return name[..head].TrimEnd() + "…" + name[^tail..].TrimStart();
    }

    private void OnDragButtonReleased()
    {
        if (_session is not { } s) return;
        if (s.ClickMode) { OnClick(s); return; }
        if (CanKeepOpen(s) && _wheel.HitTest(ScreenInfo.CursorPosition) == WheelGeometry.HitHub) { KeepOpen(s); return; }
        // If the pointer is over the wheel, OLE's Drop may still be on its way: wait briefly for it.
        if (s.OverWheel && !s.Dropped)
        {
            _deferredDismiss?.Stop();
            _deferredDismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _deferredDismiss.Tick += (_, _) => { _deferredDismiss?.Stop(); if (_session == s) Dismiss(false, "released, no drop"); };
            _deferredDismiss.Start();
        }
        else Dismiss(confirmed: false, "released");
    }

    /// <summary>Settings → Shortcuts allows it, and the files are known, so there is something to click.</summary>
    private bool CanKeepOpen(Session s) => _settings().KeepOpenOnCentre && s.EnterSeen && !s.Files.IsEmpty && s.Items.Count > 0;

    /// <summary>
    /// The drag was let go in the centre: nothing is dropped, and the wheel stays open in click mode (as from the
    /// keyboard shortcut). Hover and click a choice; the centre or the wheel key switches mode; clicking outside or Esc closes.
    /// </summary>
    private void KeepOpen(Session s)
    {
        _deferredDismiss?.Stop();
        _dwell.Stop();
        s.ClickMode = true;
        s.OverWheel = true;
        s.LastHit = WheelGeometry.HitOutside;
        _detector.EnsurePolling();
        _hover.Start();
        OnDragOver(ScreenInfo.CursorPosition);
        Log.Info("Wheel kept open (let go in the centre)");
    }

    private void Dismiss(bool confirmed, string reason)
    {
        _deferredDismiss?.Stop();
        _dwell.Stop();
        _hover.Stop();
        if (_session is null && !_wheel.IsVisible) return;
        _session?.EstimateCts?.Cancel();
        _session = null;
        _detector.SessionOpen = false;
        _wheel.Dismiss(confirmed);
        Log.Info($"Wheel closed ({reason})");
    }

    // ------------------------------------------------------------------ OLE drop target

    public void OnDragEnter(string[] files, Win32.POINT pt)
    {
        if (_session is not { } s) return;
        s.OverWheel = true;
        if (!s.EnterSeen)
        {
            s.EnterSeen = true;
            s.Files = new FileSet(files);
            s.FromArchive = files.Length == 0 && _dropTarget.LastDragFromArchive;
            Log.Info($"DragEnter after {Stopwatch.GetElapsedTime(s.OpenedAt).TotalMilliseconds:F1} ms: {s.Files.Count} file(s)");
            // Never open on an empty ring: start in the first mode that has options and say why.
            var preferred = StartMode();
            var start = NextMode(s, preferred, includeCurrent: true);
            if (preferred == OperationMode.Convert && start != OperationMode.Convert && !s.Files.IsEmpty)
                s.UnsupportedNote = $"No conversions for {KindSummary(s.Files)} yet";
            s.Mode = start;
            Refresh(animate: false);
            StartEstimates(s);
        }
        OnDragOver(pt);
    }

    public DragFeedback OnDragOver(Win32.POINT pt)
    {
        if (_session is not { } s) return new(false, null, null);
        int hit = _wheel.HitTest(pt);
        _wheel.SetHighlight(hit);
        if (hit != s.LastHit)
        {
            s.LastHit = hit;
            _dwell.Stop();
            // Resting on a group opens it; resting on the centre inside a group goes back.
            bool group = hit >= 0 && hit < s.Items.Count && s.Items[hit].IsGroupHeader;
            if (group || (hit == WheelGeometry.HitHub && s.OpenGroup is not null)) _dwell.Start();
        }
        if (hit >= 0 && hit < s.Items.Count)
        {
            var item = s.Items[hit];
            if (item.IsGroupHeader)
            {
                var first = OperationMenu.DefaultFor(s.Flat, item);
                _wheel.SetHub(item.Label, s.ClickMode ? "Click for options" : $"Hold for options · drop: {first.Label}");
                return new(true, first.DropText, first.DropInsert);
            }
            // Formats read "Convert to PNG"; tools and actions already say what they do, so show the file instead.
            string line = EstimateLine(s, item.Id) ?? (item.DropText == "%1" ? Summary(s).Detail : item.DropText.Replace("%1", item.DropInsert));
            _wheel.SetHub(item.Label, line);
            return new(true, item.DropText, item.DropInsert);
        }
        var (value, detail) = Summary(s);
        if (hit == WheelGeometry.HitHub && s.Items.Count > 0)
        {
            _wheel.SetHub(value, (s.OpenGroup is null, s.ClickMode) switch
            {
                (true, true) => HasOtherMode(s) ? $"Click for {NextModeLabel(s)}" : "Esc to close",
                (true, false) => CanKeepOpen(s) ? "Let go to keep it open" : "Release to cancel",
                (false, true) => "Click to go back",
                _ => "Going back…",
            });
            return new(false, "Cancel", null);
        }
        _wheel.SetHub(value, detail);
        return new(false, null, null);
    }

    /// <summary>The pointer rested long enough: open the group under it, or leave the open group from the centre.</summary>
    private void OnDwell()
    {
        _dwell.Stop();
        if (_session is not { } s) return;
        int hit = s.LastHit;
        if (hit >= 0 && hit < s.Items.Count && s.Items[hit].IsGroupHeader)
        {
            s.OpenGroup = s.Items[hit];
            Refresh(animate: true);
        }
        else if (hit == WheelGeometry.HitHub && s.OpenGroup is not null)
        {
            s.OpenGroup = null;
            Refresh(animate: true);
        }
        // The pointer hasn't moved, so OLE won't call DragOver: re-evaluate what is under it now.
        if (_session is { OverWheel: true }) OnDragOver(ScreenInfo.CursorPosition);
    }

    public void OnDragLeave()
    {
        _dwell.Stop();
        if (_session is not { } s) return;
        s.OverWheel = false;
        _wheel.SetHighlight(WheelGeometry.HitOutside);
        var (value, detail) = Summary(s);
        _wheel.SetHub(value, detail);
    }

    public void OnDrop(string[] files, Win32.POINT pt)
    {
        if (_session is not { } s) return;
        int hit = _wheel.IsVisible ? _wheel.HitTest(pt) : _wheel.Highlight;
        s.Dropped = true;
        var item = hit >= 0 && hit < s.Items.Count ? s.Items[hit] : null;
        if (item is { IsGroupHeader: true }) item = OperationMenu.DefaultFor(s.Flat, item);
        var set = files.Length > 0 ? new FileSet(files) : s.Files;
        Dismiss(confirmed: item is not null, item is null ? "dropped on centre" : "dropped");
        if (item is null || set.IsEmpty) return;
        if (item.NeedsInput) _ = RunWithInputAsync(item, set, s);
        else Run(item, set, s, null);
    }

    /// <summary>Asks for the option's choices first (a window or picker); runs only if the user confirms.</summary>
    private async Task RunWithInputAsync(OperationDescriptor item, FileSet set, Session s)
    {
        try
        {
            var (ok, input) = await _askInput(item, set, s.Anchor);
            if (ok) Run(item, set, s, input);
            else Log.Info($"{item.Id} canceled before running");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not prepare {item.Id}", ex);
            _toasts.ShowMessage(ToastKind.Error, "That didn't work", ex.Message, s.Anchor);
        }
    }

    private void Run(OperationDescriptor item, FileSet set, Session s, object? input)
    {
        Log.Info($"Run {item.Id} on {set.Count} file(s)");
        var settings = _settings();
        if (settings.LastUsedFirst) RememberChoice?.Invoke(WheelMemory.KeyFor(item.Mode, set), WheelOrder.SlotKey(item));
        var context = _context() with { Input = input };
        if (s.OutputFolder is { } folder) context = context with { Output = new OutputOptions(OutputLocation.Folder, folder) };
        var anchor = s.Anchor;
        var job = _jobs.Enqueue(item, set, context);
        _toasts.ShowJob(job, anchor, _shell.Reveal, settings.OfferUndo ? Recycle : null);
        job.Changed += j =>
        {
            if (!j.IsFinished) return;
            Log.Info($"Job {j.Id} {j.State}: {j.Succeeded} ok, {j.Skipped} skipped, {j.Failed} failed");
            foreach (var o in j.Outcomes.Where(o => o.Status == OutcomeStatus.Failed))
                Log.Warn($"  {Log.Path(o.Source)}: {o.Message}");
            var outputs = j.Outcomes.Where(o => o.Output is not null).Select(o => o.Output!).ToList();
            if (settings.RevealWhenDone && outputs.Count > 0) _shell.Reveal(outputs);
            JobFinished?.Invoke(j.Files, outputs);
            OperationFinished?.Invoke(item, outputs);
        };
    }

    private int Recycle(IReadOnlyList<string> paths)
    {
        int n = _shell.Recycle(paths);
        Log.Info($"Undo: {n} item(s) moved to the Recycle Bin");
        return n;
    }

    // ------------------------------------------------------------------ keyboard flow (selected files + shortcut)

    /// <summary>
    /// Opens the wheel at the pointer for files selected in File Explorer. The pointer hovers, a click picks;
    /// clicking a group opens it, clicking the centre goes back (or closes); Shift switches mode, Esc closes.
    /// </summary>
    /// <param name="outputFolder">Where results go instead of next to the files (a clipboard image's are saved here).</param>
    /// <param name="label">What the centre calls the files instead of their name.</param>
    public void OpenForSelection(FileSet files, string? outputFolder = null, string? label = null)
    {
        Dismiss(false, "reopen");
        var cursor = ScreenInfo.CursorPosition;
        var s = new Session { Files = files, EnterSeen = true, ClickMode = true, OverWheel = true, Anchor = cursor, OpenedAt = Stopwatch.GetTimestamp(),
            OutputFolder = outputFolder, SourceLabel = label };
        _session = s;
        var preferred = StartMode();
        s.Mode = NextMode(s, preferred, includeCurrent: true);
        if (preferred == OperationMode.Convert && s.Mode != OperationMode.Convert) s.UnsupportedNote = $"No conversions for {KindSummary(files)} yet";
        _detector.SessionOpen = true;
        _detector.EnsurePolling();
        // Open first, then fill: opening starts a new wheel at its normal size, and the fill sizes it for the busiest
        // page (as a drag does when the files arrive). The other way round, the first page switch made it grow.
        _wheel.Open(cursor, WheelGeometry.SizeFactor(_settings().WheelSize), animate: true);
        Refresh(animate: false);
        StartEstimates(s);
        _hover.Start();
        Log.Info($"Wheel opened from the keyboard for {files.Count} item(s)");
    }

    private void OnClick(Session s)
    {
        var at = ScreenInfo.CursorPosition;
        int hit = _wheel.HitTest(at);
        if (hit >= 0 && hit < s.Items.Count)
        {
            var item = s.Items[hit];
            if (item.IsGroupHeader)
            {
                s.OpenGroup = item;
                Refresh(animate: true);
                return;
            }
            Dismiss(confirmed: true, "clicked");
            if (item.NeedsInput) _ = RunWithInputAsync(item, s.Files, s);
            else Run(item, s.Files, s, null);
        }
        else if (hit == WheelGeometry.HitHub && s.OpenGroup is not null)
        {
            s.OpenGroup = null;
            Refresh(animate: true);
        }
        else if (hit == WheelGeometry.HitHub && HasOtherMode(s)) CycleMode(); // the centre switches Convert → Tools → Actions
        else if (hit == WheelGeometry.HitHub) Dismiss(confirmed: false, "clicked centre");
        else Dismiss(confirmed: false, "clicked outside");
    }

    // ------------------------------------------------------------------ preview (Settings → Appearance)

    /// <summary>Shows the wheel with sample options at the pointer for a few seconds (no drag involved; Esc closes).</summary>
    public void Preview()
    {
        Dismiss(false, "preview");
        var sample = new FileSet([Path.Combine(Path.GetTempPath(), "sample.jpg")]);
        var flat = Options(sample, OperationMode.Convert);
        var items = OperationMenu.TopLevel(flat);
        _session = new Session { Files = sample, Flat = flat, Items = items, EnterSeen = true, Anchor = ScreenInfo.CursorPosition };
        _detector.SessionOpen = true;
        _detector.EnsurePolling();
        _wheel.Open(_session.Anchor, WheelGeometry.SizeFactor(_settings().WheelSize), animate: true);
        _wheel.SetContent(OperationMode.Convert, items, "Preview", "Sample options", animate: false);
        if (items.Count > 0) { _wheel.SetHighlight(0); _wheel.SetHub(items[0].Label, items[0].DropText.Replace("%1", items[0].DropInsert)); }
        _previewTimer?.Stop();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _previewTimer.Tick += (_, _) => { _previewTimer?.Stop(); Dismiss(false, "preview ended"); };
        _previewTimer.Start();
    }

    // ------------------------------------------------------------------ size estimates (Settings → Wheel, off by default)

    private const int MaxEstimatedFiles = 8;
    /// <summary>Estimates per file (path, size, last write), so reopening the wheel on the same file is instant.</summary>
    private static readonly Dictionary<(string, long, DateTime), Dictionary<string, long>> EstimateCache = [];

    /// <summary>Works out likely result sizes in the background as soon as the files are known; never delays the wheel.</summary>
    private void StartEstimates(Session s)
    {
        if (!_settings().ShowSizeEstimates || s.Files.IsEmpty || s.Files.Count > MaxEstimatedFiles) return;
        var estimators = _registry.Providers.OfType<IEstimatesSize>().ToList();
        if (estimators.Count == 0) return;
        var files = s.Files.ToList();
        var context = _context();
        var cts = s.EstimateCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                Dictionary<string, long>? total = null;
                long original = 0;
                foreach (var file in files)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var info = new FileInfo(file);
                    if (!info.Exists) return;
                    original += info.Length;
                    var key = (file, info.Length, info.LastWriteTimeUtc);
                    Dictionary<string, long>? perFile;
                    lock (EstimateCache) EstimateCache.TryGetValue(key, out perFile);
                    if (perFile is null)
                    {
                        perFile = [];
                        foreach (var estimator in estimators)
                        {
                            var found = new List<(string, long)>();
                            await OrbitDrop.Conversion.StaWorker.RunAsync(() => found.AddRange(estimator.EstimateSizes(file, context)), "OrbitDrop estimate");
                            foreach (var (id, bytes) in found) perFile[id] = bytes;
                        }
                        lock (EstimateCache) { if (EstimateCache.Count > 32) EstimateCache.Clear(); EstimateCache[key] = perFile; }
                    }
                    // Several files: only options every file has an estimate for, added up.
                    total = total is null ? new(perFile)
                        : total.Where(p => perFile.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value + perFile[p.Key]);
                }
                if (total is null || cts.IsCancellationRequested) return;
                _ = _dispatcher.BeginInvoke(() =>
                {
                    if (_session != s) return;
                    s.Estimates = total;
                    s.OriginalBytes = original;
                    if (s.LastHit >= 0) OnDragOver(ScreenInfo.CursorPosition); // the pointer may already rest on an option
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Info($"Size estimate skipped: {ex.Message}"); }
        });
    }

    /// <summary>"≈ 640 KB · −73%" for a hovered option, or null when there's no estimate (or the setting is off).</summary>
    private string? EstimateLine(Session s, string optionId)
    {
        if (!_settings().ShowSizeEstimates || s.Estimates is null || s.OriginalBytes <= 0 || !s.Estimates.TryGetValue(optionId, out long bytes)) return null;
        double change = (double)bytes / s.OriginalBytes - 1;
        string delta = Math.Abs(change) < 0.03 ? "about the same" : change < 0 ? $"−{Math.Round(-change * 100)}%" : $"+{Math.Round(change * 100)}%";
        return $"≈ {HumanSize(bytes)} · {delta}";
    }

    // ------------------------------------------------------------------ helpers

    private static string KindSummary(FileSet files)
    {
        var exts = files.Extensions.Select(e => e.TrimStart('.').ToUpperInvariant()).ToList();
        return exts.Count <= 3 ? string.Join(" · ", exts) : $"{exts.Count} file types";
    }

    private static string HumanSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F1} GB",
    };

    public void Dispose()
    {
        _dropTarget.Revoke();
        _deferredDismiss?.Stop();
        _previewTimer?.Stop();
    }
}
