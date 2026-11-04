using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Core;

namespace OrbitDrop.Conversion.Imaging;

public sealed record ImageFormat(string Name, string Extension, bool SupportsAlpha);

/// <summary>
/// Image engine on the Windows Imaging Component (via WPF imaging), built into Windows. WebP, which Windows can't
/// write (and reads only with a Store extension), goes through <see cref="WebpCodec"/>.
/// Must be used from an STA thread (see <see cref="StaWorker"/>).
/// Limitations (documented in the capability matrix): EXIF orientation is not applied, animated GIF/multi-page TIFF
/// use the first frame, colour profiles are not carried over.
/// </summary>
public static class WicImageCodec
{
    public static readonly ImageFormat Png = new("PNG", ".png", true);
    public static readonly ImageFormat Jpg = new("JPG", ".jpg", false);
    public static readonly ImageFormat Bmp = new("BMP", ".bmp", false);
    public static readonly ImageFormat Tiff = new("TIFF", ".tiff", true);
    public static readonly ImageFormat Gif = new("GIF", ".gif", true);
    public static readonly ImageFormat Webp = new("WEBP", ".webp", true);
    /// <summary>A JPEG under the .jfif name (what some browsers save images as): same encoder as JPG.</summary>
    public static readonly ImageFormat Jfif = new("JFIF", ".jfif", false);
    /// <summary>Read only (drawn by <see cref="SvgCodec"/>); results that keep the format become PNG.</summary>
    public static readonly ImageFormat Svg = new("SVG", ".svg", true);

    /// <summary>Output formats, in wheel order.</summary>
    public static IReadOnlyList<ImageFormat> OutputFormats { get; } = [Png, Jpg, Webp, Bmp, Tiff, Gif, Jfif];

    private static readonly Dictionary<string, ImageFormat> InputByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = Png, [".jpg"] = Jpg, [".jpeg"] = Jpg, [".jpe"] = Jpg, [".jfif"] = Jfif, [".webp"] = Webp,
        [".bmp"] = Bmp, [".dib"] = Bmp, [".tif"] = Tiff, [".tiff"] = Tiff, [".gif"] = Gif, [".svg"] = Svg,
    };

    /// <summary>
    /// Formats Windows can read only through optional codecs (Microsoft Store extensions, camera RAW). They're accepted
    /// as input when this PC has the codec; nothing is ever written in them.
    /// </summary>
    private static readonly Dictionary<string, ImageFormat> OptionalInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        [".heic"] = new("HEIC", ".heic", false), [".heif"] = new("HEIC", ".heif", false),
        [".avif"] = new("AVIF", ".avif", true), [".jxl"] = new("JXL", ".jxl", true), [".jxr"] = new("JXR", ".jxr", true),
        [".wdp"] = new("JXR", ".wdp", true), [".dng"] = new("RAW", ".dng", false), [".cr2"] = new("RAW", ".cr2", false),
        [".cr3"] = new("RAW", ".cr3", false), [".nef"] = new("RAW", ".nef", false), [".arw"] = new("RAW", ".arw", false),
        [".orf"] = new("RAW", ".orf", false), [".rw2"] = new("RAW", ".rw2", false), [".raf"] = new("RAW", ".raf", false),
        [".ico"] = new("ICO", ".ico", true),
    };

    public static ImageFormat? FormatOf(string path)
    {
        string ext = Path.GetExtension(path);
        if (InputByExtension.TryGetValue(ext, out var f)) return f;
        return OptionalInputs.TryGetValue(ext, out var o) && WicCodecs.DecoderExtensions.Contains(ext) ? o : null;
    }

    public static bool IsSupportedInput(string path) => FormatOf(path) is not null;

    public static IEnumerable<string> InputExtensions =>
        InputByExtension.Keys.Concat(OptionalInputs.Keys.Where(WicCodecs.DecoderExtensions.Contains));

    /// <summary>The format a tool writes for <paramref name="path"/>: its own, or PNG/JPG for formats Windows can't write.</summary>
    public static ImageFormat OutputFormatFor(string path)
    {
        var f = FormatOf(path) ?? throw new NotSupportedException(Path.GetExtension(path));
        return OutputFormats.Contains(f) ? f : f.SupportsAlpha ? Png : Jpg;
    }

    /// <summary>
    /// Decodes the first frame, applies the EXIF orientation (so phone photos come out upright) and normalises to
    /// BGRA32. Metadata and colour contexts are dropped: not every encoder accepts them.
    /// </summary>
    public static BitmapSource Load(string path)
    {
        try { return LoadCore(path); }
        catch (Exception ex) when (MissingExtension.For(path, ex) is { } missing) { throw missing; }
    }

    private static BitmapSource LoadCore(string path)
    {
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)) return SvgCodec.Load(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            // No Windows codec for it (WebP without the Store extension): Skia reads it, or it really isn't an image.
            return WebpCodec.TryDecode(path) ?? throw ex;
        }
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The image contains no frames.");
        var frame = decoder.Frames[0];
        BitmapSource bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (OrientationTransform(Orientation(frame)) is { } t) bgra = new TransformedBitmap(bgra, t);
        // Materialise: the result must not depend on the (closed) stream or on lazy transforms.
        var result = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, Pixels(bgra), bgra.PixelWidth * 4);
        result.Freeze();
        return result;
    }

    /// <summary>EXIF orientation tag (1–8), or 1 when absent.</summary>
    public static int Orientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata meta) return 1;
        foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (meta.GetQuery(query) is ushort v && v is >= 1 and <= 8) return v;
            }
            catch { /* query not valid for this container */ }
        }
        return 1;
    }

    /// <summary>The transform that turns a stored image with EXIF <paramref name="orientation"/> upright.</summary>
    public static Transform? OrientationTransform(int orientation) => orientation switch
    {
        2 => new ScaleTransform(-1, 1),
        3 => new RotateTransform(180),
        4 => new ScaleTransform(1, -1),
        5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
        6 => new RotateTransform(90),
        7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
        8 => new RotateTransform(270),
        _ => null,
    };

    public static byte[] Pixels(BitmapSource bgra)
    {
        int stride = bgra.PixelWidth * 4;
        var px = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(px, stride, 0);
        return px;
    }

    /// <summary>True when any pixel is not fully opaque.</summary>
    public static bool HasTransparency(BitmapSource bgra)
    {
        var px = Pixels(bgra);
        for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) return true;
        return false;
    }

    /// <summary>High-quality (Fant) resample to exactly <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static BitmapSource Resize(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var bgra = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, Pixels(bgra), width * 4);
        result.Freeze();
        return result;
    }

    /// <summary>Scale so the longer side is at most <paramref name="maxSide"/> (never upscales). Null when already small enough.</summary>
    public static (int W, int H)? FitLongSide(int width, int height, int maxSide)
    {
        int longSide = Math.Max(width, height);
        if (longSide <= maxSide) return null;
        double k = (double)maxSide / longSide;
        return (Math.Max(1, (int)Math.Round(width * k)), Math.Max(1, (int)Math.Round(height * k)));
    }

    public static byte[] EncodeToBytes(BitmapSource image, ImageFormat format, int jpegQuality)
    {
        using var ms = new MemoryStream();
        Encode(image, format, ms, jpegQuality);
        return ms.ToArray();
    }

    public static void Encode(BitmapSource image, ImageFormat format, Stream stream, int jpegQuality)
    {
        if (format == Webp)
        {
            WebpCodec.Encode(image, stream, jpegQuality);
            return;
        }
        BitmapEncoder encoder = format.Extension switch
        {
            ".png" => new PngBitmapEncoder(),
            ".jpg" or ".jfif" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            ".bmp" => new BmpBitmapEncoder(),
            ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
            ".gif" => new GifBitmapEncoder(),
            _ => throw new NotSupportedException($"No encoder for {format.Name}"),
        };
        // Formats without alpha: composite onto white instead of letting transparent pixels turn black.
        encoder.Frames.Add(BitmapFrame.Create(format.SupportsAlpha ? image : FlattenOnWhite(image)));
        encoder.Save(stream);
    }

    /// <summary>Writes <paramref name="image"/> next to <paramref name="source"/> with full SafeOutput guarantees.</summary>
    public static string WriteOutput(BitmapSource image, string source, ImageFormat format, string? suffix,
        OperationContext context, CancellationToken ct)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        return SafeOutput.Write(source, context.Output, format.Extension, suffix,
            write: s => Encode(image, format, s, context.JpegQuality),
            validate: temp =>
            {
                var check = Load(temp);
                return check.PixelWidth == w && check.PixelHeight == h
                    ? null
                    : $"decoded as {check.PixelWidth}×{check.PixelHeight}, expected {w}×{h}";
            },
            ct);
    }

    public static BitmapSource FlattenOnWhite(BitmapSource source)
    {
        if (source.Format == PixelFormats.Gray8) return source;
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        var rgb = new byte[w * 3 * h];
        for (int i = 0, j = 0; i < px.Length; i += 4, j += 3)
        {
            int a = px[i + 3], inv = 255 - a;
            rgb[j] = (byte)((px[i] * a + 255 * inv) / 255);
            rgb[j + 1] = (byte)((px[i + 1] * a + 255 * inv) / 255);
            rgb[j + 2] = (byte)((px[i + 2] * a + 255 * inv) / 255);
        }
        var result = BitmapSource.Create(w, h, bgra.DpiX, bgra.DpiY, PixelFormats.Bgr24, null, rgb, w * 3);
        result.Freeze();
        return result;
    }

    public static BitmapSource Transform(BitmapSource source, Transform transform)
    {
        var t = new TransformedBitmap(source, transform);
        t.Freeze();
        return t;
    }

    public static BitmapSource Grayscale(BitmapSource source)
    {
        // Keep transparency: convert colour channels to luminance but preserve alpha.
        int w = source.PixelWidth, h = source.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        source.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            byte y = (byte)Math.Round(0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2]);
            px[i] = px[i + 1] = px[i + 2] = y;
        }
        var result = BitmapSource.Create(w, h, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, px, stride);
        result.Freeze();
        return result;
    }
}
