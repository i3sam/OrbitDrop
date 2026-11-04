using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Conversion;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OrbitDrop.Tools;

/// <summary>
/// Actions › Print (free): photos and PDFs in one print job, through Windows' own print dialog (printer, pages,
/// copies). No other app opens. Each photo is a page, turned to fill the paper best; PDF pages are drawn at 300 dpi
/// one at a time while printing, so long PDFs don't fill the memory.
/// </summary>
public sealed class PrintProvider : IOperationProvider
{
    public const string ProviderId = "print";
    public const string PrintId = "action.print";
    public string Id => ProviderId;

    private const double PrintDpi = 300, MaxSide = 4200; // 300 dpi up to A3-ish; bigger pages are drawn at what fits

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Actions || files.IsEmpty || !files.All(Printable)) yield break;
        yield return new(PrintId, ProviderId, OperationMode.Actions, "Print", "", "%1", "Print", "Printing", 236, DoneTitle: "Sent to the printer");
    }

    private static bool Printable(string file) => IsPdf(file) || (WicImageCodec.IsSupportedInput(file) && !Directory.Exists(file));

    private static bool IsPdf(string file) => file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        var job = await PrepareAsync(files, ct);
        var (pages, failed) = (job.Pages, job.Failed);
        bool? printed = null;
        string? problem = null;
        if (pages.Count > 0)
        {
            // The dialog must be shown on the app's own thread: WPF's PrintDialog looks up the app's main window as its
            // owner, and that throws "The calling thread cannot access this object" from any other thread.
            Choice? choice = null;
            void Ask()
            {
                var dialog = new PrintDialog { UserPageRangeEnabled = pages.Count > 1, MinPage = 1, MaxPage = (uint)pages.Count };
                printed = dialog.ShowDialog();
                if (printed != true) return;
                bool range = dialog.PageRangeSelection == PageRangeSelection.UserPages;
                choice = new Choice(dialog.PrintQueue.FullName, Copy(dialog.PrintTicket),
                    new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight),
                    range ? dialog.PageRange.PageFrom : 1, range ? dialog.PageRange.PageTo : pages.Count);
            }
            try
            {
                var ui = Application.Current?.Dispatcher;
                if (ui is null) await StaWorker.RunAsync(Ask, "OrbitDrop print dialog");
                else if (ui.CheckAccess()) Ask();
                else await ui.InvokeAsync(Ask).Task;
            }
            catch (Exception ex) { problem = $"Printing didn't work: {ex.Message}"; }

            // The pages are drawn on a thread of their own, so a long PDF doesn't freeze OrbitDrop. A print queue only
            // works on the thread that opened it, so the printer picked above is opened again here, by name.
            if (choice is { } c)
                await StaWorker.RunAsync(() =>
                {
                    try
                    {
                        using var queue = new PrintQueue(new PrintServer(), c.Printer);
                        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
                        writer.Write(job.Paginator(c.Paper, c.From, c.To), c.Ticket);
                    }
                    catch (Exception ex) { problem = $"Printing didn't work: {ex.Message}"; }
                }, "OrbitDrop print");
        }

        foreach (var file in files)
        {
            if (failed.TryGetValue(file, out var why)) progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: why));
            else if (problem is not null) progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: problem));
            else if (printed != true) progress.Report(new FileOutcome(file, OutcomeStatus.Canceled, Message: "Printing canceled."));
            else progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded));
        }
    }

    /// <summary>What the print dialog was told: the printer (by name), its settings, the paper and the pages.</summary>
    private sealed record Choice(string Printer, PrintTicket Ticket, Size Paper, int From, int To);

    /// <summary>A copy of the print settings that isn't tied to the thread of the dialog that made them.</summary>
    private static PrintTicket Copy(PrintTicket ticket)
    {
        using var xml = ticket.GetXmlStream();
        var copy = new MemoryStream();
        xml.CopyTo(copy);
        copy.Position = 0;
        return new PrintTicket(copy);
    }

    /// <summary>Every page of <paramref name="files"/>, in order: a photo is one, a PDF as many as it has (opened, not drawn yet).</summary>
    public static async Task<PrintJob> PrepareAsync(IEnumerable<string> files, CancellationToken ct = default)
    {
        var pages = new List<PageSource>();
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsPdf(file)) { pages.Add(new PageSource(file, null, 0)); continue; }
            try
            {
                var pdf = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(file));
                if (pdf.IsPasswordProtected) { failed[file] = "This PDF is password-protected."; continue; }
                for (uint i = 0; i < pdf.PageCount; i++) pages.Add(new PageSource(file, pdf, i));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed[file] = "Windows couldn't open this PDF."; }
        }
        return new PrintJob(pages, failed);
    }

    /// <summary>What's to be printed: the pages, and the files that couldn't be (with why).</summary>
    public sealed class PrintJob(List<PageSource> pages, Dictionary<string, string> failed)
    {
        public IReadOnlyList<PageSource> Pages => pages;
        public IReadOnlyDictionary<string, string> Failed => failed;

        /// <summary>Pages <paramref name="from"/> to <paramref name="to"/> (1-based, as the print dialog counts) on paper of <paramref name="paper"/>.</summary>
        public DocumentPaginator Paginator(Size paper, int from = 1, int to = int.MaxValue)
        {
            int first = Math.Clamp(from, 1, Math.Max(1, pages.Count)), last = Math.Clamp(to, first, pages.Count);
            return new PagePaginator(pages.GetRange(first - 1, Math.Max(0, last - first + 1)), paper);
        }
    }

    /// <summary>One printed page: a photo (no <paramref name="Pdf"/>), or page <paramref name="Index"/> of a PDF.</summary>
    public sealed record PageSource(string File, PdfDocument? Pdf, uint Index);

    /// <summary>Hands the printer one page at a time, drawing each only when it's asked for.</summary>
    private sealed class PagePaginator(IReadOnlyList<PageSource> pages, Size paper) : DocumentPaginator
    {
        public override bool IsPageCountValid => true;
        public override int PageCount => pages.Count;
        public override Size PageSize { get; set; } = paper;
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var picture = Picture(pages[pageNumber]);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) Fit(dc, picture, PageSize);
            return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
        }

        private static BitmapSource Picture(PageSource page)
        {
            if (page.Pdf is null) return WicImageCodec.Load(page.File); // upright, as phones meant it
            using var pdfPage = page.Pdf.GetPage(page.Index);
            double k = Math.Min(PrintDpi / 96, MaxSide / Math.Max(pdfPage.Size.Width, pdfPage.Size.Height));
            using var stream = new InMemoryRandomAccessStream();
            pdfPage.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(pdfPage.Size.Width * k), DestinationHeight = (uint)Math.Round(pdfPage.Size.Height * k),
                BackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255),
            }).AsTask().GetAwaiter().GetResult();
            using var managed = stream.AsStreamForRead();
            var decoder = BitmapDecoder.Create(managed, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }

        /// <summary>Draws <paramref name="picture"/> as big as fits on the paper, centred, turned a quarter if that's bigger.</summary>
        private static void Fit(DrawingContext dc, BitmapSource picture, Size paper)
        {
            double w = picture.PixelWidth, h = picture.PixelHeight;
            double straight = Math.Min(paper.Width / w, paper.Height / h), turned = Math.Min(paper.Width / h, paper.Height / w);
            bool turn = turned > straight * 1.05;
            double k = turn ? turned : straight;
            double dw = w * k, dh = h * k;
            if (turn)
            {
                dc.PushTransform(new RotateTransform(90, paper.Width / 2, paper.Height / 2));
                dc.DrawImage(picture, new Rect((paper.Width - dw) / 2, (paper.Height - dh) / 2, dw, dh));
                dc.Pop();
            }
            else dc.DrawImage(picture, new Rect((paper.Width - dw) / 2, (paper.Height - dh) / 2, dw, dh));
        }
    }

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Print", "Photos and PDFs, through Windows' print dialog (Actions)");
    }
}
