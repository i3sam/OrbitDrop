using System.Windows;
using OrbitDrop.Core;
using OrbitDrop.Jobs;
using OrbitDrop.Windows.Interop;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.UI.Toasts;

/// <summary>
/// Shows one toast per job, stacked upward from the bottom-right of the monitor where the files were dropped.
/// Toasts never take focus. Errors always show; success toasts respect "Show completion notifications".
/// </summary>
public sealed class ToastManager
{
    private const int MaxVisible = 4;
    private readonly List<(ToastWindow Window, Win32.POINT Anchor)> _toasts = [];

    public Func<bool> NotifyOnCompletion { get; set; } = () => true;

    /// <summary>When set, a job that failed completely offers "Report" on its card (set by the app).</summary>
    public Action<Job>? ReportProblem { get; set; }

    /// <param name="recycle">When set, a finished card offers Undo: what the job made goes to the Recycle Bin (see <see cref="Undo"/>).</param>
    public void ShowJob(Job job, Win32.POINT anchor, Action<IReadOnlyList<string>> reveal, Func<IReadOnlyList<string>, int>? recycle = null)
    {
        var toast = Create(anchor);
        if (recycle is not null) toast.UndoRequested += () => UndoJob(toast, job, recycle);
        toast.CancelRequested += job.Cancel;
        toast.ShowRequested += () =>
        {
            if (ReportProblem is not null && job.IsFinished && Summarize(job).Kind == ToastKind.Error)
            {
                ReportProblem(job);
                toast.FadeOutAndClose();
                return;
            }
            var outputs = job.Outcomes.Where(o => o.Output is not null).Select(o => o.Output!).ToList();
            reveal(outputs.Count > 0 ? outputs : job.Files.ToList());
            toast.FadeOutAndClose();
        };
        Update(toast, job, recycle is not null);
        job.Changed += j => Update(toast, j, recycle is not null);
    }

    private static IReadOnlyList<string> UndoTargets(Job job) => Undo.Targets(job.Operation, job.Files.ToList(), job.Outcomes, job.CreatedUtc);

    private static void UndoJob(ToastWindow toast, Job job, Func<IReadOnlyList<string>, int> recycle)
    {
        toast.SetUndo(false);
        try
        {
            int n = recycle(UndoTargets(job));
            toast.SetResult(ToastKind.Info, "Undone", n == 1 ? "Moved to the Recycle Bin" : $"{n} items moved to the Recycle Bin",
                canShow: false, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            toast.SetResult(ToastKind.Warning, "Couldn't undo", ex.Message, canShow: false, TimeSpan.FromSeconds(8));
        }
    }

    public void ShowMessage(ToastKind kind, string title, string detail, Win32.POINT anchor, TimeSpan? duration = null)
    {
        var toast = Create(anchor);
        toast.SetResult(kind, title, detail, canShow: false, duration ?? TimeSpan.FromSeconds(5));
    }

    /// <summary>A result card with one action button (e.g. "Install"). Only the button triggers the action.</summary>
    public void ShowAction(ToastKind kind, string title, string detail, string actionLabel, Action action, Win32.POINT anchor, TimeSpan? duration = null)
    {
        var toast = Create(anchor);
        toast.SetAction(actionLabel);
        toast.ShowRequested += () => { toast.FadeOutAndClose(); action(); };
        toast.SetResult(kind, title, detail, canShow: true, duration ?? TimeSpan.FromSeconds(12));
    }

    /// <summary>An empty card the caller drives with <see cref="ToastWindow.SetRunning"/> / <see cref="ToastWindow.SetResult"/>.</summary>
    public ToastWindow ShowProgress(Win32.POINT anchor) => Create(anchor);

    private ToastWindow Create(Win32.POINT anchor)
    {
        while (_toasts.Count >= MaxVisible) _toasts[0].Window.FadeOutAndClose();
        var toast = new ToastWindow();
        toast.Closed += (_, _) => { _toasts.RemoveAll(t => t.Window == toast); Reflow(); };
        toast.SizeChanged += (_, _) => Reflow();
        _toasts.Add((toast, anchor));
        toast.Opacity = 0;
        toast.Show();
        Reflow();
        toast.Opacity = 1;
        toast.PlayEnter();
        return toast;
    }

    private void Update(ToastWindow toast, Job job, bool offerUndo)
    {
        if (!job.IsFinished)
        {
            double fraction = job.Total == 0 ? 0 : (job.Processed + job.CurrentFraction) / job.Total;
            string progressDetail = job.Total == 1
                ? Path.GetFileName(job.Files[0])
                : $"{job.Processed} of {job.Total} · {Path.GetFileName(job.CurrentFile ?? job.Files[0])}";
            toast.SetRunning(job.Operation.ProgressTitle, progressDetail, fraction, indeterminate: job.Processed == 0 && job.CurrentFraction == 0);
            return;
        }

        var (kind, title, detail) = Summarize(job);
        bool hasOutputs = job.Outcomes.Any(o => o.Output is not null);
        bool canShow = hasOutputs || (job.Operation.Mode == OperationMode.Convert && job.Succeeded > 0);
        TimeSpan close = kind switch
        {
            ToastKind.Success when !NotifyOnCompletion() => TimeSpan.FromMilliseconds(600),
            ToastKind.Success or ToastKind.Info => TimeSpan.FromSeconds(4),
            _ => TimeSpan.FromSeconds(12),
        };
        // Undo when the job made something new; the card then stays a little longer, so there's time to reach it.
        // (Not when completion cards are turned off: those close at once, and stay out of the way.)
        bool undo = offerUndo && (kind == ToastKind.Warning || (kind == ToastKind.Success && NotifyOnCompletion())) && UndoTargets(job).Count > 0;
        if (undo && close < TimeSpan.FromSeconds(8)) close = TimeSpan.FromSeconds(8);
        if (kind == ToastKind.Error && ReportProblem is not null)
        {
            toast.SetAction("Report");
            canShow = true;
        }
        toast.SetResult(kind, title, detail, canShow, close);
        toast.SetUndo(undo);
    }

    /// <summary>Plain-language summary of a finished job. Public for unit tests.</summary>
    public static (ToastKind Kind, string Title, string Detail) Summarize(Job job)
    {
        var outcomes = job.Outcomes;
        var outputs = outcomes.Where(o => o.Output is not null).Select(o => o.Output!).ToList();
        int ok = job.Succeeded, failed = job.Failed, skipped = job.Skipped, total = job.Total;
        var firstFailure = outcomes.FirstOrDefault(o => o.Status == OutcomeStatus.Failed);
        string FailureLine() => firstFailure is null ? "" : $"{Path.GetFileName(firstFailure.Source)}: {firstFailure.Message}";

        if (job.State == JobState.Canceled)
            return (ToastKind.Info, "Canceled", ok == 0 ? "No files were changed." : $"{ok} of {total} finished before you canceled; nothing partial was left behind.");

        if (failed == 0 && ok > 0)
        {
            if (job.Operation.Mode == OperationMode.Actions || outputs.Count == 0) // nothing new was written: say what happened
                return (ToastKind.Success, job.Operation.DoneTitle ?? job.Operation.Label, ok == 1 ? Path.GetFileName(job.Files[0]) : $"{ok} files");
            string? folder = outputs.Count > 0 ? Path.GetFileName(Path.GetDirectoryName(outputs[0])?.TrimEnd('\\')) : null;
            string where = folder is { Length: > 0 } ? $"In {folder}" : "";
            string title = outputs.Count == 1 ? $"Saved {Path.GetFileName(outputs[0])}" : $"{outputs.Count} files saved";
            string extra = skipped > 0 ? $" · {skipped} skipped" : "";
            return (ToastKind.Success, title, $"{where}{extra}".Trim(' ', '·'));
        }

        if (ok == 0 && failed == 0)
        {
            var skip = outcomes.FirstOrDefault(o => o.Status == OutcomeStatus.Skipped);
            return (ToastKind.Info, "Nothing to do", skip?.Message ?? "The files are already in that format.");
        }

        if (ok == 0)
            return (ToastKind.Error, total == 1 ? "That didn't work" : $"None of the {total} files could be processed", FailureLine());

        return (ToastKind.Warning, $"{ok} saved · {failed} failed", FailureLine());
    }

    private void Reflow()
    {
        double offset = 0;
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var (window, anchor) = _toasts[i];
            if (!window.IsLoaded) continue;
            var monitor = ScreenInfo.MonitorAt(anchor);
            double scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? monitor.Scale;
            int w = (int)Math.Round(window.ActualWidth * scale), h = (int)Math.Round(window.ActualHeight * scale);
            int x = monitor.WorkArea.Right - w - (int)(8 * scale);
            int y = monitor.WorkArea.Bottom - h - (int)(8 * scale) - (int)offset;
            Win32.SetWindowPos(window.Handle, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            offset += h - 24 * scale; // toast margins overlap so cards sit ~8 px apart
        }
    }
}
