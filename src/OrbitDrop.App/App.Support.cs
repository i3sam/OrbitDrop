using OrbitDrop.Core;
using OrbitDrop.Infrastructure;
using OrbitDrop.Jobs;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.App;

/// <summary>
/// Help and problem reports, from Settings → About, the tray menu, the License page and a failed job's card. They all
/// open the website's form (orbitdrop.essam.biz/feedback), which emails the developer. The link carries only the topic,
/// OrbitDrop's version and edition, and the Windows version. Anything that could hold a file name (an error, the log)
/// goes on the clipboard instead, for the user to paste or leave out.
/// </summary>
public partial class App
{
    private const string SupportUrl = "https://orbitdrop.essam.biz/feedback";

    /// <param name="topic">"help", "bug", "purchase" or "feature": the form's tab.</param>
    /// <param name="from">Where in the app it was opened, for the developer ("about", "tray", "license", "error").</param>
    /// <param name="details">Copied to the clipboard, and the form says so.</param>
    public void OpenSupport(string topic, string from, string? details = null)
    {
        if (details is not null) _shell.SetClipboardText(details);
        string url = $"{SupportUrl}?topic={topic}&from={from}&v={Uri.EscapeDataString(UpdateManager.Current.ToString())}" +
            $"&edition={(AppPackage.IsPackaged ? "store" : "github")}&os={Environment.OSVersion.Version.Build}{(details is null ? "" : "&pasted=1")}";
        Log.Info($"Support form opened ({topic}, from {from})");
        UpdateManager.OpenUrl(url);
    }

    /// <summary>What to paste into a report: versions, the upgrade's state and the last problems in the log.</summary>
    public string SupportDetails()
    {
        var lines = new List<string>
        {
            $"OrbitDrop {UpdateManager.Current} ({(AppPackage.IsPackaged ? "Microsoft Store" : "GitHub")})",
            $"Windows {Environment.OSVersion.Version}",
        };
        var log = RecentLog();
        var problems = log.Where(l => l.Contains(" WARN ") || l.Contains(" ERROR ") || l.Contains(" License: ")).TakeLast(15).ToList();
        if (problems.Count > 0) { lines.Add(""); lines.Add("Recent log:"); lines.AddRange(problems); }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A failed job, for its card's Report button: what was tried and why it failed.</summary>
    private string JobDetails(Job job)
    {
        var failures = job.Outcomes.Where(o => o.Status == OutcomeStatus.Failed).Take(5)
            .Select(o => $"{Path.GetExtension(o.Source)} file: {o.Message}");
        return $"Tried: {job.Operation.Label} ({job.Operation.Id}) on {job.Total} file(s){Environment.NewLine}" +
               string.Join(Environment.NewLine, failures) + Environment.NewLine + Environment.NewLine + SupportDetails();
    }

    /// <summary>Today's and yesterday's log lines, oldest first; empty when they can't be read.</summary>
    private List<string> RecentLog()
    {
        var lines = new List<string>();
        foreach (var day in new[] { DateTime.Now.AddDays(-1), DateTime.Now })
        {
            string file = Path.Combine(_paths.LogsDirectory, $"orbitdrop-{day:yyyyMMdd}.log");
            try
            {
                // The log is open for writing while OrbitDrop runs.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line) lines.Add(line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return lines;
    }
}
