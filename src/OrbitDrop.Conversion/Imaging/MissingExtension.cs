using System.Diagnostics;

namespace OrbitDrop.Conversion.Imaging;

/// <summary>
/// Windows reads AVIF and HEIC photos through free Microsoft Store extensions, and many PCs (most Windows 10 ones) have
/// the photo part but not the video codec inside it: the file is offered, then fails with "No suitable transform was
/// found" (0xC00D5212). This turns that into a plain message, and opens the extension's Store page once per run, so one
/// click on Get (then trying again) fixes it.
/// </summary>
public sealed class MissingExtension(string format, string extension, string storeId, Exception inner)
    : Exception($"Windows needs the free {extension} from the Microsoft Store to read {format} photos. OrbitDrop opened it for you: "
        + "click Get, then try again.", inner)
{
    public string StoreId { get; } = storeId;

    private const uint NoTransform = 0xC00D5212, NoCodec = 0x88982F50, NoComponent = 0x88982F44;
    private static int _opened;

    /// <summary>Opens the Store page (the first time only). Tests and renders switch it off.</summary>
    public static bool OpenStore { get; set; } = true;

    public static MissingExtension? For(string path, Exception ex)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".avif" or ".heic" or ".heif")) return null;
        bool codec = false;
        for (var e = ex; e is not null; e = e.InnerException)
            if ((uint)e.HResult is NoTransform or NoCodec or NoComponent || e.Message.Contains("0xC00D5212", StringComparison.OrdinalIgnoreCase)) codec = true;
        if (!codec) return null;
        var missing = ext == ".avif"
            ? new MissingExtension("AVIF", "AV1 Video Extension", "9MVZQVXJBQ9V", ex)
            : new MissingExtension("HEIC", "HEVC Video Extensions from Device Manufacturer", "9N4WGH0Z6VHQ", ex);
        if (OpenStore && Interlocked.Exchange(ref _opened, 1) == 0)
        {
            try { Process.Start(new ProcessStartInfo($"ms-windows-store://pdp/?ProductId={missing.StoreId}") { UseShellExecute = true }); }
            catch { /* no Store on this PC: the message still says what to get */ }
        }
        return missing;
    }
}
