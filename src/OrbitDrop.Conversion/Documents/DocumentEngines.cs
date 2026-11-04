using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OrbitDrop.Conversion.Documents;

public enum DocumentKind { Text, Sheet, Slides, Drawing, Pdf }

/// <summary>Windows couldn't start Word, Excel or PowerPoint to do the conversion (twice).</summary>
public sealed class OfficeWontStartException(string app, COMException inner)
    : Exception($"{app} didn't start (Windows error 0x{inner.HResult:X8}). This usually means {app} is waiting on a message: open {app} once, "
        + $"close anything it shows (sign-in, activation, \"start in safe mode?\"), close {app}, then try again. If you run {app} as "
        + "administrator, open it normally instead.", inner)
{
    public string App { get; } = app;
}

/// <summary>
/// The apps that do the actual conversion, found on this PC: Microsoft Word, Excel and PowerPoint (driven through COM,
/// invisibly, files opened read-only) and LibreOffice (headless, with its own private profile); Office is preferred for
/// its own formats. CorelDRAW drawings need neither: OrbitDrop reads them itself (<see cref="CorelDraw"/>). Results go
/// to a private temp folder that is deleted on <see cref="Dispose"/>; the provider copies them to their place with SafeOutput.
/// </summary>
public sealed class DocumentEngines : IDisposable
{
    private static readonly Dictionary<string, DocumentKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = DocumentKind.Text, [".docx"] = DocumentKind.Text, [".docm"] = DocumentKind.Text, [".rtf"] = DocumentKind.Text,
        [".odt"] = DocumentKind.Text,
        [".xls"] = DocumentKind.Sheet, [".xlsx"] = DocumentKind.Sheet, [".xlsm"] = DocumentKind.Sheet, [".ods"] = DocumentKind.Sheet,
        [".ppt"] = DocumentKind.Slides, [".pptx"] = DocumentKind.Slides, [".pptm"] = DocumentKind.Slides, [".pps"] = DocumentKind.Slides,
        [".ppsx"] = DocumentKind.Slides, [".odp"] = DocumentKind.Slides,
        [".cdr"] = DocumentKind.Drawing, [".cmx"] = DocumentKind.Drawing,
        [".pdf"] = DocumentKind.Pdf, // to Word: Word (or LibreOffice) rebuilds the text and layout as an editable document
    };

    /// <summary>Some password is needed so Office fails instead of asking; a file without one ignores it.</summary>
    private const string NoPassword = "orbitdrop-no-password";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static readonly Lazy<bool> Word = new(() => HasProgId("Word.Application")), Excel = new(() => HasProgId("Excel.Application")),
        PowerPoint = new(() => HasProgId("PowerPoint.Application"));
    private static readonly AppFinder LibreOffice = new(() => FindApp("soffice.exe", @"LibreOffice\program\soffice.exe", @"scoop\apps\libreoffice\current\LibreOffice\program\soffice.exe"));
    /// <summary>
    /// Finds an app and remembers it. While it isn't there, looks again every 15 seconds, so an app installed while
    /// OrbitDrop is running is picked up without a restart.
    /// </summary>
    private sealed class AppFinder(Func<string?> find)
    {
        private readonly object _gate = new();
        private string? _path;
        private bool _searched;
        private long _searchedAt;

        public string? Value
        {
            get
            {
                lock (_gate)
                {
                    long now = Environment.TickCount64;
                    bool stale = !_searched || (_path is null ? now - _searchedAt > 15_000 : !File.Exists(_path));
                    if (stale) { _path = find(); _searched = true; _searchedAt = now; }
                    return _path;
                }
            }
        }
    }

    public static DocumentKind? KindOf(string path) => Kinds.TryGetValue(Path.GetExtension(path), out var k) ? k : null;

    public static bool CanOpen(DocumentKind kind) => kind switch
    {
        DocumentKind.Text => Word.Value || LibreOffice.Value is not null,
        DocumentKind.Sheet => Excel.Value || LibreOffice.Value is not null,
        DocumentKind.Slides => PowerPoint.Value || LibreOffice.Value is not null,
        DocumentKind.Pdf => Word.Value || LibreOffice.Value is not null,
        DocumentKind.Drawing => CorelDraw.Available,
        _ => false,
    };

    /// <summary>The apps found, for Settings → About's capability list.</summary>
    public static string[] Describe() => new (bool Found, string Name)[]
    {
        (Word.Value, "Word"), (Excel.Value, "Excel"), (PowerPoint.Value, "PowerPoint"), (LibreOffice.Value is not null, "LibreOffice"),
    }.Where(a => a.Found).Select(a => a.Name).ToArray();

    // Short names all the way down: a long file name inside a deep temp folder passes Windows' 260-character path
    // limit, which LibreOffice and Office can't write to. The real name is only used for the final result.
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "OrbitDrop", "docs", Guid.NewGuid().ToString("N")[..8]);
    private int _jobs;

    /// <summary>A fresh, short-named folder inside this conversion's private temp folder (deleted with it).</summary>
    public string NewWorkFolder()
    {
        string dir = Path.Combine(_temp, (++_jobs).ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Converts <paramref name="file"/> to <paramref name="extension"/> in a private temp folder; returns the new file.</summary>
    public async Task<string> ConvertToTempAsync(string file, string extension, CancellationToken ct)
    {
        var kind = KindOf(file) ?? throw new NotSupportedException(Path.GetExtension(file));
        if (!File.Exists(file)) throw new FileNotFoundException(null, file);
        string dir = Path.Combine(_temp, (++_jobs).ToString());
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "source" + Path.GetExtension(file).ToLowerInvariant());
        File.Copy(file, input);
        string output = Path.Combine(dir, "source" + extension); // LibreOffice names it after the input, too

        string? office = kind switch
        {
            DocumentKind.Text or DocumentKind.Pdf when Word.Value => "Word.Application",
            DocumentKind.Sheet when Excel.Value => "Excel.Application",
            DocumentKind.Slides when PowerPoint.Value => "PowerPoint.Application",
            _ => null,
        };
        async Task WithLibreOffice(string soffice)
        {
            // A profile of its own: works while the user has LibreOffice open, and never touches their settings.
            string profile = new Uri(Path.Combine(Path.GetTempPath(), "OrbitDrop", "libreoffice-profile")).AbsoluteUri;
            string[] import = kind == DocumentKind.Pdf ? ["--infilter=writer_pdf_import"] : []; // open a PDF in Writer, not Draw
            await RunAsync(soffice, ["--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck", $"-env:UserInstallation={profile}",
                .. import, "--convert-to", LibreOfficeFilter(extension), "--outdir", dir, input], ct);
        }

        if (office is not null)
        {
            Task WithOffice() => StaWorker.RunAsync(() => ConvertWithOffice(office, kind, input, output), "OrbitDrop Office");
            try { await WithOffice(); }
            catch (COMException first) when (WontStart(first))
            {
                // Word (or Excel, PowerPoint) didn't start. Often it's only slow the first time (an update, a cold
                // start), so once more after a moment; then LibreOffice if it's here; then say what usually causes it.
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                try { await WithOffice(); }
                catch (COMException again) when (WontStart(again))
                {
                    if (LibreOffice.Value is { } soffice) await WithLibreOffice(soffice);
                    else throw new OfficeWontStartException(AppName(office), again);
                }
            }
        }
        else if (kind != DocumentKind.Drawing && LibreOffice.Value is { } soffice) await WithLibreOffice(soffice);
        else throw new NotSupportedException("No app on this PC can open this file.");

        if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("The conversion didn't produce a file.");
        return output;
    }

    /// <summary>Errors that mean Windows couldn't start the Office app at all (not that the file was bad).</summary>
    private static bool WontStart(COMException ex) => (uint)ex.HResult is
        0x80080005 // CO_E_SERVER_EXEC_FAILURE: it started but never answered
        or 0x800401F3 or 0x80040154 // not registered (a broken or half-removed Office)
        or 0x80010001 or 0x8001010A // busy: it's showing a message
        or 0x800706BA or 0x800706BE; // it closed while starting

    private static string AppName(string progId) => progId.Split('.')[0];

    private static string LibreOfficeFilter(string extension) => extension switch
    {
        ".docx" => "docx:MS Word 2007 XML",
        ".xlsx" => "xlsx:Calc MS Excel 2007 XML",
        ".pptx" => "pptx:Impress MS PowerPoint 2007 XML",
        _ => extension.TrimStart('.'),
    };

    // ------------------------------------------------------------------ Microsoft Office (late-bound COM, on an STA thread)

    private static void ConvertWithOffice(string progId, DocumentKind kind, string file, string output)
    {
        bool pdf = output.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        // PowerPoint runs as one shared instance: if the user has it open, leave it running afterwards.
        bool wasRunning = kind == DocumentKind.Slides && Process.GetProcessesByName("POWERPNT").Length > 0;
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;
        try
        {
            switch (kind)
            {
                case DocumentKind.Text or DocumentKind.Pdf:
                    app.DisplayAlerts = 0; // wdAlertsNone
                    dynamic doc = app.Documents.Open(file, false, true, false, NoPassword); // ConfirmConversions, ReadOnly, AddToRecentFiles
                    try
                    {
                        if (pdf) doc.ExportAsFixedFormat(output, 17); // wdExportFormatPDF
                        else doc.SaveAs2(output, 16);                   // wdFormatDocumentDefault (.docx)
                    }
                    finally { doc.Close(0); Release(doc); }
                    break;

                case DocumentKind.Sheet:
                    app.DisplayAlerts = false;
                    dynamic book = app.Workbooks.Open(file, 0, true, Missing.Value, NoPassword); // UpdateLinks: no, ReadOnly
                    try
                    {
                        if (pdf) book.ExportAsFixedFormat(0, output); // xlTypePDF
                        else book.SaveAs(output, 51);                  // xlOpenXMLWorkbook (.xlsx)
                    }
                    finally { book.Close(false); Release(book); }
                    break;

                case DocumentKind.Slides:
                    dynamic deck = app.Presentations.Open(file, -1, 0, 0); // ReadOnly, not Untitled, no window
                    try { deck.SaveAs(output, pdf ? 32 : 24); }            // ppSaveAsPDF / ppSaveAsOpenXMLPresentation
                    finally { deck.Close(); Release(deck); }
                    break;
            }
        }
        finally
        {
            try
            {
                if (kind is DocumentKind.Text or DocumentKind.Pdf) app.Quit(0);
                else if (kind == DocumentKind.Sheet) app.Quit();
                else if (!wasRunning && app.Presentations.Count == 0) app.Quit();
            }
            catch (COMException) { /* already gone */ }
            Release(app);
        }
    }

    private static void Release(object o)
    {
        try { Marshal.FinalReleaseComObject(o); } catch (ArgumentException) { }
    }

    private static bool HasProgId(string progId)
    {
        try { return Type.GetTypeFromProgID(progId) is not null; }
        catch { return false; }
    }

    // ------------------------------------------------------------------ LibreOffice (command line)

    /// <summary>
    /// App Paths, Program Files, per-user installs, scoop, the Microsoft Store version (its command-line alias) and
    /// PATH. Prefers the console launcher (.com), which waits for the result.
    /// </summary>
    private static string? FindApp(string exe, string programFilesPath, string homePath)
    {
        var candidates = new List<string?>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                candidates.Add(key?.GetValue(null) as string);
            }
            catch { /* no access */ }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), programFilesPath));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), programFilesPath));
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Programs", programFilesPath));                                     // per-user installer
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), homePath)); // scoop
        candidates.Add(Path.Combine(local, "Microsoft", "WindowsApps", exe));                                 // Microsoft Store alias
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim('"'), exe)); } catch (ArgumentException) { /* a malformed PATH entry */ }
        }
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            string path = c.Trim('"');
            string console = Path.ChangeExtension(path, ".com");
            if (File.Exists(console)) return console;
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static async Task RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new IOException($"{Path.GetFileName(exe)} didn't start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException();
        }
        if (process.ExitCode != 0)
        {
            string detail = (await stderr).Trim() is { Length: > 0 } e ? e : (await stdout).Trim();
            throw new IOException($"{Path.GetFileNameWithoutExtension(exe)} couldn't convert it{(detail.Length > 0 ? ": " + detail.Split('\n')[0] : ".")}");
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true); } catch { /* best effort */ }
    }
}
