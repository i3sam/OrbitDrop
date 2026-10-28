using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Core;
using OrbitDrop.Infrastructure;
using OrbitDrop.Tools;
using OrbitDrop.UI;
using OrbitDrop.UI.Onboarding;
using OrbitDrop.UI.Settings;
using OrbitDrop.UI.Toasts;
using OrbitDrop.UI.Tray;
using OrbitDrop.UI.Wheel;

namespace OrbitDrop.App;

/// <summary>
/// <c>OrbitDrop.exe --render-ui &lt;folder&gt;</c>: renders every surface offscreen, in light and dark, at 100% and 150%,
/// using the real registry and real view models. No screen capture, no settings written. Used for design review.
/// </summary>
internal static class UiReview
{
    /// <summary>
    /// <c>--render-github &lt;folder&gt;</c>: what's new in the GitHub version: the wheel with the Store version's Premium
    /// tools crowned (Tools and Actions for a photo, Actions › Your actions), Settings › General (Show Premium tools) and
    /// Settings › Your actions (two free, a third waiting for the Store version).
    /// </summary>
    public static int RunGitHub(string dir, OperationRegistry registry)
    {
        Directory.CreateDirectory(dir);
        int count = 0;
        CustomAction[] samples =
        [
            new() { Name = "Open in VS Code", Program = "code", Arguments = "{files}", PerFile = false },
            new() { Name = "Resize with my script", Program = "python", Arguments = "\"C:\\Tools\\resize.py\" {file} --width 1080", Kind = "Photos", ShowWindow = true },
            new() { Name = "Copy to my NAS", Program = "", Arguments = "robocopy {folder} \\\\nas\\photos {name}.{ext}" },
        ];
        ThemeManager.Current.Apply(AppTheme.Dark, useWindowsAccent: false, reduceMotion: true);
        var withActions = new OperationRegistry([.. registry.Providers.Where(p => p is not CustomActionsProvider), new CustomActionsProvider(() => samples)],
            licensed: () => false, showLocked: () => true);
        var files = new FileSet([Path.Combine(Path.GetTempPath(), "holiday photo.jpg")]);
        int busiest = Enum.GetValues<OperationMode>().Max(m => OperationMenu.Busiest(withActions.Query(files, m)));
        foreach (var (name, mode, group) in new (string, OperationMode, string?)[] { ("tools", OperationMode.Tools, null), ("actions", OperationMode.Actions, null),
            ("your-actions", OperationMode.Actions, CustomActionsProvider.Group.Id), ("share", OperationMode.Actions, FileActionsProvider.ShareGroup.Id) })
        {
            var flat = withActions.Query(files, mode);
            var items = group is null ? OperationMenu.TopLevel(flat) : OperationMenu.Members(flat, group);
            var wheel = new RadialWheel { Busiest = busiest };
            wheel.SetContent(mode, items, group is null ? "2.4 MB" : items[0].Group?.Label ?? "", group is null ? "holiday photo.jpg" : "Rest on the centre to go back", animate: false);
            wheel.SetHighlight(-2);
            Save(wheel.RenderPreview(Wallpaper(true), 1.5), Path.Combine(dir, $"github-wheel-{name}.png"), ref count);
            wheel.Close();
        }
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Current.Apply(theme, useWindowsAccent: false, reduceMotion: true);
            var bg = (Brush)Application.Current.FindResource("Orbit.SurfaceBase");
            string t = theme.ToString().ToLowerInvariant();
            for (int state = 0; state < 4; state++)
            {
                var sw = new SwitchWindow((_, ct) => Task.Delay(-1, ct).ContinueWith(_ => (false, "")), () => { }, () => { }, portable: false);
                sw.ReviewState(state);
                RenderElement((FrameworkElement)sw.Content, 520, double.NaN, bg, dir, $"github-switch-{state}-{t}", ref count, [1.0]);
                sw.Close();
            }
            foreach (var page in new[] { "General", "About", "MyActions" })
            {
                var host = new PreviewHost(registry);
                host.Settings.CustomActions = samples.Select(a => a.Clone()).ToArray();
                var window = new SettingsWindow(new SettingsViewModel(host));
                window.ShowPage(page);
                Settle(window.ContentRoot, 880, page == "MyActions" ? 1000 : 760);
                RenderElement(window.ContentRoot, 880, page == "MyActions" ? 1000 : 760, bg, dir, $"github-settings-{page.ToLowerInvariant()}-{t}", ref count, [1.0]);
                window.Close();
            }
        }
        return 0;
    }

    /// <summary>Lays <paramref name="root"/> out and lets short template animations (a switch's knob) finish before an offscreen render.</summary>
    private static void Settle(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        root.UpdateLayout();
    }

    public static int Run(string dir, OperationRegistry registry)
    {
        Directory.CreateDirectory(dir);
        int count = 0;
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Current.Apply(theme, useWindowsAccent: false, reduceMotion: true);
            string t = theme.ToString().ToLowerInvariant();
            var bg = (Brush)Application.Current.FindResource("Orbit.SurfaceBase");

            // --- wheel on a bright and a dark wallpaper
            foreach (var (wallName, wall) in new[] { ("bright", Wallpaper(false)), ("dark", Wallpaper(true)) })
            {
                var cases = new (string Name, OperationMode Mode, string[] Files, int Hi, string Value, string Detail, string? Group)[]
                {
                    ("convert", OperationMode.Convert, ["photo.jpg"], 1, "WEBP", "Convert to WEBP", null),
                    ("convert-batch", OperationMode.Convert, ["a.jpg", "b.png", "c.gif"], -2, "3 files", "JPG · PNG · GIF", null),
                    ("tools", OperationMode.Tools, ["photo.jpg"], 0, "Compress", "Hold for options · drop: 50%", null),
                    ("tools-compress", OperationMode.Tools, ["photo.jpg"], 3, "Under 1 MB", "Compress to under 1 MB", "image.compress"),
                    ("audio-tools", OperationMode.Tools, ["song.mp3"], 1, "Channels", "Hold for options · drop: Mono", null),
                    ("actions", OperationMode.Actions, ["report.pdf"], 2, "Copy to", "Hold for options · drop: Choose…", null),
                    ("actions-copy-to", OperationMode.Actions, ["report.pdf"], 1, "Desktop", "Copy to Desktop", "actions.copy-to"),
                    ("video", OperationMode.Convert, ["clip.mp4"], 1, "MP3", "Extract audio as MP3", null),
                    ("convert-estimate", OperationMode.Convert, ["photo.jpg"], 1, "WEBP", "≈ 640 KB · −73%", null),
                    ("long-name", OperationMode.Convert, ["clip.mp4"], -2, "3.4 MB", InteractionController.ShortName("Filee is a free, open-source converter that turns your files into other formats right on your computer.mp4"), null),
                    ("long-detail", OperationMode.Convert, ["clip.mp4"], -2, "3.4 MB", "Filee is a free, open-source converter that turns your files into other formats right on your computer", null),
                    ("video-tools", OperationMode.Tools, ["clip.mp4"], 0, "Compress", "Hold for options · drop: 720p", null),
                    ("unsupported", OperationMode.Convert, ["report.pdf"], -2, "No conversions yet", "Tap Shift for more", null),
                };
                foreach (var c in cases)
                {
                    var wheel = new RadialWheel();
                    var flat = registry.Query(new FileSet(c.Files.Select(f => Path.Combine(Path.GetTempPath(), f))), c.Mode);
                    var items = c.Group is null ? OperationMenu.TopLevel(flat) : OperationMenu.Members(flat, c.Group);
                    wheel.SetContent(c.Mode, items, c.Value, c.Detail, animate: false);
                    wheel.SetHighlight(c.Hi);
                    foreach (double scale in new[] { 1.0, 1.5 })
                        Save(wheel.RenderPreview(wall, scale), Path.Combine(dir, $"wheel-{c.Name}-{wallName}-{t}-{scale * 100:F0}.png"), ref count);
                    wheel.Close();
                }
            }

            // --- toasts
            var toastStates = new (string Name, Action<ToastWindow> Setup)[]
            {
                ("running", w => w.SetRunning("Converting to PNG", "2 of 5 · holiday-photo.jpg", 0.4, false)),
                ("success", w => w.SetResult(ToastKind.Success, "Saved holiday-photo.png", "In Pictures", true, TimeSpan.Zero)),
                ("success-undo", w => { w.SetResult(ToastKind.Success, "Saved holiday-photo.webp", "In Pictures", true, TimeSpan.Zero); w.SetUndo(true); }),
                ("undone", w => w.SetResult(ToastKind.Info, "Undone", "Moved to the Recycle Bin", false, TimeSpan.Zero)),
                ("partial", w => w.SetResult(ToastKind.Warning, "4 saved · 1 failed", "scan.tif: The file isn't a readable image.", true, TimeSpan.Zero)),
                ("error", w => { w.SetAction("Report"); w.SetResult(ToastKind.Error, "That didn't work", "notes.png: Windows denied access (read-only folder or file in use).", true, TimeSpan.Zero); }),
                ("canceled", w => w.SetResult(ToastKind.Info, "Canceled", "3 of 8 finished before you canceled; nothing partial was left behind.", false, TimeSpan.Zero)),
            };
            foreach (var (name, setup) in toastStates)
            {
                var toast = new ToastWindow();
                setup(toast);
                RenderElement(toast.ContentRoot, 372, double.NaN, bg, dir, $"toast-{name}-{t}", ref count);
                toast.Close();
            }

            // --- settings pages
            var host = new PreviewHost(registry);
            foreach (var page in SettingsWindow.PageNames)
            {
                var window = new SettingsWindow(new SettingsViewModel(host));
                window.ShowPage(page);
                RenderElement(window.ContentRoot, 880, 620, bg, dir, $"settings-{page.ToLowerInvariant()}-{t}", ref count);
                window.Close();
            }

            // --- onboarding
            var guideAssets = new GuideAssets(registry);
            var onboarding = new OnboardingWindow("Shift", guideAssets, Path.Combine(dir, "guide-practice"));
            for (int step = 0; step < onboarding.StepCount; step++)
            {
                onboarding.ShowStepStill(step);
                RenderElement(onboarding.ContentRoot, 680, double.NaN, bg, dir, $"onboarding-{step + 1}-{t}", ref count, [1.0]);
            }
            onboarding.Close();
            guideAssets.Dispose();

            // --- Info window (sample photo)
            var info = new OrbitDrop.UI.Metadata.MetadataWindow(new MetadataSnapshot(@"C:\Users\You\Pictures\IMG_2041.jpg", "Image",
                [
                    new("Type", "JPG image"), new("Size", "3.4 MB"), new("Dimensions", "4032 × 3024"),
                    new("Camera", "Apple iPhone 15 Pro"), new("Taken", "Saturday, 14 June 2026 18:42"),
                    new("Exposure", "1/120 s · ƒ/1.8 · ISO 64 · 7 mm"), new("Location", "51.50735, -0.12776"),
                    new("Modified", "Saturday, 14 June 2026 18:42"), new("Folder", @"C:\Users\You\Pictures"),
                ],
                [
                    new("System.Title", "Title", MetadataFieldKind.Text, "Golden hour"),
                    new("System.Author", "Authors", MetadataFieldKind.List, null, "Separate with ;"),
                    new("System.Keywords", "Tags", MetadataFieldKind.List, "london; summer"),
                    new("System.Rating", "Rating", MetadataFieldKind.Rating, "4"),
                    new("System.Comment", "Comments", MetadataFieldKind.MultiLine, null),
                ], HasLocation: true));
            RenderElement((FrameworkElement)info.Content, 520, double.NaN, bg, dir, $"info-{t}", ref count);
            info.Close();

            // --- photo editor (markup and crop)
            foreach (var mode in new[] { "Markup", "Crop" })
            {
                var photo = (BitmapSource)Wallpaper(false);
                var editor = new OrbitDrop.UI.Editor.PhotoEditorWindow(photo, "IMG_2041.jpg");
                double s = photo.PixelWidth / 356.0;
                editor.ReviewState(mode, mode == "Crop" ? [] :
                [
                    new OrbitDrop.UI.Editor.ArrowMark(Color.FromRgb(0xFF, 0x3B, 0x30), 3 * s, new Point(60 * s, 60 * s), new Point(150 * s, 140 * s)),
                    new OrbitDrop.UI.Editor.BoxMark(Color.FromRgb(0xFF, 0xCC, 0x00), 3 * s, new Rect(180 * s, 150 * s, 110 * s, 70 * s)),
                    new OrbitDrop.UI.Editor.TextMark(Colors.White, 0, new Point(40 * s, 250 * s), "Look here", 26 * s),
                    new OrbitDrop.UI.Editor.RedactMark(new Rect(230 * s, 40 * s, 90 * s, 50 * s), OrbitDrop.UI.Editor.RedactKind.Pixelate),
                ], mode == "Crop" ? new Rect(40 * s, 30 * s, 260 * s, 260 * s) : null);
                RenderElement((FrameworkElement)editor.Content, 1120, 780, bg, dir, $"editor-{mode.ToLowerInvariant()}-{t}", ref count, [1.0]);
                editor.Close();
            }

            // --- trim window (audio, so no video frame is needed)
            var trim = new OrbitDrop.UI.Trim.TrimWindow(@"C:\Users\You\Music\Interview.m4a", TimeSpan.FromSeconds(272), isVideo: false);
            trim.ReviewRange(0.18, 0.64);
            RenderElement((FrameworkElement)trim.Content, 920, 640, bg, dir, $"trim-{t}", ref count, [1.0]);
            trim.Close();

            // --- Float shelf (real sample files in the review folder)
            string samplePhoto = Path.Combine(dir, "Sunset.png"), sampleDoc = Path.Combine(dir, "Quarterly report.txt");
            if (!File.Exists(samplePhoto))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create((BitmapSource)Wallpaper(false)));
                using var fs = File.Create(samplePhoto);
                enc.Save(fs);
            }
            if (!File.Exists(sampleDoc)) File.WriteAllText(sampleDoc, new string('x', 48_000));
            var shelf = new OrbitDrop.UI.Shelf.ShelfWindow();
            shelf.Add([samplePhoto, sampleDoc, dir], show: false);
            RenderElement((FrameworkElement)shelf.Content, 268, double.NaN, bg, dir, $"shelf-{t}", ref count);
            shelf.Close();

            // --- tray flyout
            var flyout = new TrayFlyout(new PreviewTray());
            RenderElement(flyout.ContentRoot, 300, double.NaN, bg, dir, $"tray-{t}", ref count);
            flyout.Close();
        }
        File.WriteAllText(Path.Combine(dir, "README.txt"), $"{count} renders · {DateTime.Now:yyyy-MM-dd HH:mm}\r\n");
        return 0;
    }

    private static void RenderElement(FrameworkElement element, double width, double height, Brush? background, string dir, string name, ref int count, double[]? scales = null)
    {
        element.Measure(new Size(width, double.IsNaN(height) ? double.PositiveInfinity : height));
        double h = double.IsNaN(height) ? element.DesiredSize.Height : height;
        double w = double.IsNaN(width) ? element.DesiredSize.Width : width;
        element.Arrange(new Rect(0, 0, w, h));
        element.UpdateLayout();
        foreach (double scale in scales ?? [1.0, 1.5])
        {
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            if (background is not null)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
                bmp.Render(dv);
            }
            bmp.Render(element); // direct render keeps layout offsets (a VisualBrush would stretch to content bounds)
            Save(bmp, Path.Combine(dir, scales is null ? $"{name}-{scale * 100:F0}.png" : $"{name}.png"), ref count);
        }
    }

    private static void Save(BitmapSource bmp, string path, ref int count)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        count++;
    }

    /// <summary>Synthetic wallpapers: a busy bright gradient and a deep dark one, to check legibility on both.</summary>
    private static ImageSource Wallpaper(bool dark)
    {
        var dv = new DrawingVisual();
        double s = WheelGeometry.Extent;
        using (var dc = dv.RenderOpen())
        {
            var a = dark ? Color.FromRgb(0x10, 0x1A, 0x2E) : Color.FromRgb(0xFF, 0xC8, 0x5A);
            var b = dark ? Color.FromRgb(0x3A, 0x1F, 0x4F) : Color.FromRgb(0x4F, 0xC3, 0xF7);
            dc.DrawRectangle(new LinearGradientBrush(a, b, 35), null, new Rect(0, 0, s, s));
            dc.DrawEllipse(new SolidColorBrush(dark ? Color.FromArgb(120, 0x2E, 0x8B, 0x57) : Color.FromArgb(150, 0xFF, 0x4F, 0x81)), null, new Point(s * 0.3, s * 0.35), s * 0.22, s * 0.22);
            dc.DrawEllipse(new SolidColorBrush(dark ? Color.FromArgb(90, 0xFF, 0xFF, 0xFF) : Color.FromArgb(160, 0xFF, 0xFF, 0xFF)), null, new Point(s * 0.72, s * 0.7), s * 0.16, s * 0.16);
        }
        var bmp = new RenderTargetBitmap((int)s, (int)s, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private sealed class PreviewHost(OperationRegistry registry) : ISettingsHost
    {
        public AppSettings Settings { get; } = new()
        {
            OutputFolder = @"C:\Users\You\Pictures\Converted",
            // A few options turned off, so the Wheel page shows both chip states.
            HiddenOptions = ["convert.image.bmp", "convert.image.tiff", "tool.image.compress.p25", "group:image.resize"],
        };
        public IReadOnlyList<WheelCategory> WheelCatalog { get; } = WheelCatalogBuilder.Build(registry);
        public void Update(Action<AppSettings> change) => change(Settings);
        public bool LaunchAtStartup { get; set; } = true;
        public bool AccentFallbackActive => false;
        public IEnumerable<(string Category, string Detail)> Capabilities => registry.Providers.SelectMany(p => p.DescribeCapabilities());
        public string VersionText => "Version 0.1.0-alpha (design review)";
        public string SettingsPath => @"C:\Users\You\AppData\Roaming\OrbitDrop\settings.json";
        public string LogsPath => @"C:\Users\You\AppData\Local\OrbitDrop\logs";
        public string? LicensePath => "LICENSE.txt";
        public void PreviewWheel() { }
        public void ShowOnboarding() { }
        public void OpenPath(string path) { }
        public void ResetSettings() { }
    }

    private sealed class PreviewTray : ITrayCommands
    {
        public bool Paused { get; set; }
        public string TriggerKeyName => "Shift";
        public void OpenSettings(string? page = null) { }
        public void ShowOnboarding() { }
        public void Quit() { }
    }
}
