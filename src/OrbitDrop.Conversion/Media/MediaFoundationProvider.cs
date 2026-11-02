using OrbitDrop.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OrbitDrop.Conversion.Media;

/// <summary>
/// Video and audio conversion through <b>Windows Media Foundation</b> (MediaTranscoder), which is part of Windows.
/// Nothing is bundled or downloaded. Decoding uses whatever codecs Windows has installed; if one is missing, the file
/// fails with a clear message rather than producing a broken output.
/// Outputs: MP4 (H.264/AAC), WMV, M4A (AAC), MP3, WAV, WMA. Tools: compress to 720p, remove audio.
/// </summary>
public sealed class MediaFoundationProvider : IOperationProvider
{
    public const string ProviderId = "media.mf";
    public const string TrimId = "tool.media.trim";
    public string Id => ProviderId;

    private static readonly HashSet<string> VideoInputs = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".3gp", ".mts", ".m2ts", ".ts" };
    private static readonly HashSet<string> AudioInputs = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" };

    private sealed record Target(string Label, string Extension, bool IsVideo);

    private static readonly Target[] VideoTargets = [new("MP4", ".mp4", true), new("WMV", ".wmv", true)];
    private static readonly Target[] AudioTargets = [new("MP3", ".mp3", false), new("M4A", ".m4a", false), new("WAV", ".wav", false), new("WMA", ".wma", false), new("FLAC", ".flac", false)];

    private static readonly OperationGroup VideoCompress = new("video.compress", "Compress", "\uE73F");
    private static readonly OperationGroup AudioCompress = new("audio.compress", "Compress", "\uE73F");
    private static readonly OperationGroup Channels = new("audio.channels", "Channels", "\uE995");

    /// <summary>Video compression presets: a resolution box, or (Box = null) the same size at half the bitrate.</summary>
    private sealed record VideoPreset(string Key, string Label, string Hint, uint BoxW, uint BoxH);

    private static readonly VideoPreset[] VideoPresets =
    [
        new("720", "720p", "Compress to 720p", 1280, 720),
        new("1080", "1080p", "Compress to 1080p", 1920, 1080),
        new("480", "480p", "Compress to 480p", 854, 480),
        new("half", "Half size", "Same resolution, about half the size", 0, 0),
    ];

    private static readonly uint[] AudioBitrates = [128, 96, 64];

    public static bool IsVideo(string path) => VideoInputs.Contains(Path.GetExtension(path));
    public static bool IsAudio(string path) => AudioInputs.Contains(Path.GetExtension(path));
    private static bool IsMedia(string path) => IsVideo(path) || IsAudio(path);

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (!files.All(IsMedia)) yield break;
        bool allVideo = files.All(IsVideo);
        var extensions = files.Extensions.ToList();
        bool Same(Target t) => extensions.Count == 1 && (extensions[0] == t.Extension || (t.Extension == ".mp4" && extensions[0] == ".m4v"));

        if (mode == OperationMode.Convert)
        {
            // Video files can become video or audio; audio files only audio (the intersection rule).
            var targets = allVideo ? VideoTargets.Concat(AudioTargets) : AudioTargets;
            int order = 20;
            foreach (var t in targets)
            {
                order++;
                if (Same(t)) continue;
                yield return new($"convert.media{t.Extension}", ProviderId, OperationMode.Convert, t.Label, null,
                    allVideo && !t.IsVideo ? "Extract audio as %1" : "Convert to %1", t.Label,
                    allVideo && !t.IsVideo ? $"Extracting audio as {t.Label}" : $"Converting to {t.Label}", order);
            }
        }
        if (mode == OperationMode.Tools && files.Count == 1)
            yield return new(TrimId, ProviderId, OperationMode.Tools, "Trim", "", "Keep just the part you choose", "",
                "Trimming", 110, NeedsInput: true);

        if (mode == OperationMode.Tools && allVideo)
        {
            int o = 120;
            foreach (var p in VideoPresets)
                yield return new($"tool.video.compress.{p.Key}", ProviderId, OperationMode.Tools, p.Label, null,
                    p.Hint, "", "Compressing video", o++, Group: VideoCompress);
            yield return new("tool.video.mute", ProviderId, OperationMode.Tools, "Remove audio", "î",
                "%1", "Remove audio", "Removing audio", 130);
        }
        else if (mode == OperationMode.Tools)
        {
            int o = 120;
            foreach (uint kbps in AudioBitrates)
                yield return new($"tool.audio.compress.{kbps}", ProviderId, OperationMode.Tools, $"{kbps} kbps", null,
                    $"Compress to {kbps} kbps", "", "Compressing audio", o++, Group: AudioCompress);
            yield return new("tool.audio.mono", ProviderId, OperationMode.Tools, "Mono", "\uE995", "Make it mono", "", "Converting to mono", 130, Group: Channels);
            yield return new("tool.audio.stereo", ProviderId, OperationMode.Tools, "Stereo", "\uE995", "Make it stereo", "", "Converting to stereo", 131, Group: Channels);
            yield return new("tool.audio.normalize", ProviderId, OperationMode.Tools, "Normalize", "", "As loud as it can be without clipping", "", "Normalizing", 125);
        }
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (operation.Id == TrimId && context.Input is TrimRange range)
                {
                    progress.Report(await TrimAsync(file, range, context, ct));
                    continue;
                }
                if (operation.Id == "tool.audio.normalize")
                {
                    progress.Report(await NormalizeAsync(file, context, ct));
                    continue;
                }
                var (profile, extension, suffix) = await ProfileForAsync(operation, file);
                if (extension == Path.GetExtension(file).ToLowerInvariant() && suffix is null)
                {
                    progress.Report(new FileOutcome(file, OutcomeStatus.Skipped, Message: $"Already {operation.Label}"));
                    continue;
                }
                string output = await Task.Run(() => SafeOutput.WriteToPath(file, context.Output, extension, suffix,
                    writeToTemp: temp => TranscodeWithFallback(file, temp, profile, context.ReportFileFraction, ct),
                    validate: temp => ValidateContainer(temp, extension),
                    ct, hideWhileWriting: false), ct);
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex)));
            }
        }
    }

    private static async Task<(MediaEncodingProfile Profile, string Extension, string? Suffix)> ProfileForAsync(OperationDescriptor op, string file)
    {
        switch (op.Id)
        {
            case "convert.media.mp4": return (MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto), ".mp4", null);
            case "convert.media.wmv": return (MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Auto), ".wmv", null);
            case "convert.media.mp3": return (MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High), ".mp3", null);
            case "convert.media.m4a": return (MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), ".m4a", null);
            case "convert.media.wav": return (MediaEncodingProfile.CreateWav(AudioEncodingQuality.High), ".wav", null);
            case "convert.media.wma": return (MediaEncodingProfile.CreateWma(AudioEncodingQuality.High), ".wma", null);
            case "tool.video.mute":
            {
                var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
                p.Audio = null;
                return (p, ".mp4", "no audio");
            }
            case "convert.media.flac": return (MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High), ".flac", null);
            case var id when id.StartsWith("tool.video.compress."):
            {
                var preset = VideoPresets.Single(v => id == $"tool.video.compress.{v.Key}");
                // An explicit profile, as for trimming: Auto with a changed size leaves the frame rate unset, which
                // some sources (60 fps game recordings) and encoders refuse with no reason given.
                var source = await StorageFile.GetFileFromPathAsync(file);
                var props = await source.Properties.GetVideoPropertiesAsync();
                var p = MediaEncodingProfile.CreateMp4(props.Width > 0 && props.Height > 0 ? VideoEncodingQuality.HD1080p : VideoEncodingQuality.Auto);
                double fps = await KeepFrameRateAsync(p, source);
                if (preset.BoxW == 0)
                {
                    // Same resolution, half the bitrate. Windows may not report a bitrate; estimate it from size Ã· length.
                    uint bitrate = props.Bitrate;
                    if (bitrate == 0 && props.Duration.TotalSeconds > 0)
                        bitrate = (uint)Math.Min(uint.MaxValue, new FileInfo(file).Length * 8 / props.Duration.TotalSeconds);
                    (uint w, uint h) = FitWithin(props.Width, props.Height, 7680, 4320);
                    if (w > 0 && h > 0) { p.Video.Width = w; p.Video.Height = h; }
                    if (bitrate > 0) p.Video.Bitrate = (uint)Math.Max(300_000, bitrate * 0.5 - 128_000);
                }
                else
                {
                    (uint w, uint h) = FitWithin(props.Width, props.Height, preset.BoxW, preset.BoxH);
                    if (w > 0 && h > 0)
                    {
                        p.Video.Width = w;
                        p.Video.Height = h;
                        p.Video.Bitrate = PresetBitrate(w, h, fps, props.Bitrate); // â2.4 Mbps at 720p, 5.4 at 1080p
                    }
                }
                if (p.Audio is not null) p.Audio.Bitrate = 128_000;
                return (p, ".mp4", "compressed");
            }
            case var id when id.StartsWith("tool.audio.compress."):
            {
                uint kbps = uint.Parse(id["tool.audio.compress.".Length..]);
                var (p, ext) = SameKindAudio(file, lossyOnly: true);
                p.Audio.Bitrate = kbps * 1000;
                return (p, ext, "compressed");
            }
            case "tool.audio.mono" or "tool.audio.stereo":
            {
                var (p, ext) = SameKindAudio(file, lossyOnly: false);
                p.Audio.ChannelCount = op.Id == "tool.audio.mono" ? 1u : 2u;
                return (p, ext, op.Id == "tool.audio.mono" ? "mono" : "stereo");
            }
            default: throw new NotSupportedException(op.Id);
        }
    }

    /// <summary>
    /// Copies the source's frame rate into the profile; left as the profile's own when Windows doesn't report one.
    /// Returns the frame rate copied, or 0 when it is unknown.
    /// </summary>
    private static async Task<double> KeepFrameRateAsync(MediaEncodingProfile profile, StorageFile source)
    {
        var rate = await source.Properties.RetrievePropertiesAsync(["System.Video.FrameRate"]);
        if (rate.TryGetValue("System.Video.FrameRate", out var fr) && fr is uint milli and > 0)
        {
            profile.Video.FrameRate.Numerator = milli; // frames per 1000 seconds
            profile.Video.FrameRate.Denominator = 1000;
            return milli / 1000.0;
        }
        return 0;
    }

    /// <summary>
    /// Bitrate for a resolution preset: about 2.6 bits per pixel per second (≈2.4 Mbps at 720p, 5.4 at 1080p), and up to
    /// 1.5× that at 60 fps, which has twice the frames to spend it on. The extra never takes the output above 80% of the
    /// source's bitrate, so a high-frame-rate video that was already small doesn't grow.
    /// </summary>
    public static uint PresetBitrate(uint width, uint height, double fps, uint sourceBitrate)
    {
        double bitrate = Math.Clamp(width * (double)height * 2.6, 400_000, 6_000_000);
        double factor = Math.Clamp(1 + (fps - 30) / 60, 1.0, 1.5);
        if (factor > 1)
        {
            double boosted = Math.Min(bitrate * factor, 9_000_000);
            if (sourceBitrate > 0) boosted = Math.Min(boosted, Math.Max(bitrate, sourceBitrate * 0.8));
            bitrate = boosted;
        }
        return (uint)bitrate;
    }

    /// <summary>Length of a video or audio file, from the Windows property system (zero if unknown).</summary>
    public static async Task<TimeSpan> DurationAsync(string file)
    {
        var storage = await StorageFile.GetFileFromPathAsync(file);
        var props = await storage.Properties.GetMusicPropertiesAsync(); // Duration is filled in for video too
        return props.Duration;
    }

    /// <summary>Keeps <paramref name="range"/>: video becomes MP4, audio keeps its format where Windows can write it.</summary>
    private static async Task<FileOutcome> TrimAsync(string file, TrimRange range, OperationContext context, CancellationToken ct)
    {
        var duration = await DurationAsync(file);
        if (range.Start <= TimeSpan.FromMilliseconds(50) && (duration == TimeSpan.Zero || range.End >= duration - TimeSpan.FromMilliseconds(50)))
            return new FileOutcome(file, OutcomeStatus.Skipped, Message: "Nothing was trimmed.");
        // Trimming rejects VideoEncodingQuality.Auto, so the video profile is spelled out from the source.
        MediaEncodingProfile profile;
        string extension;
        if (IsVideo(file))
        {
            profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            var source = await StorageFile.GetFileFromPathAsync(file);
            var props = await source.Properties.GetVideoPropertiesAsync();
            await KeepFrameRateAsync(profile, source); // HD720p alone would turn a 60 fps recording into 30
            var (w, h) = FitWithin(props.Width, props.Height, 3840, 2160);
            if (w > 0 && h > 0)
            {
                profile.Video.Width = w;
                profile.Video.Height = h;
                profile.Video.Bitrate = props.Bitrate > 0 ? Math.Clamp(props.Bitrate, 1_000_000u, 40_000_000u) : (uint)Math.Clamp(w * h * 4.0, 1_000_000, 16_000_000);
            }
            extension = ".mp4";
        }
        else (profile, extension) = SameKindAudio(file, lossyOnly: false);
        string output = await Task.Run(() => SafeOutput.WriteToPath(file, context.Output, extension, "trimmed",
            writeToTemp: temp => TranscodeWithFallback(file, temp, profile, context.ReportFileFraction, ct, range, duration),
            validate: temp => ValidateContainer(temp, extension),
            ct, hideWhileWriting: false), ct);
        return new FileOutcome(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>
    /// Transcodes with hardware encoding, and if the graphics driver can't do it (no GPU, as on servers, or a driver
    /// that refuses the settings: a generic E_FAIL, or a transcode Windows won't prepare without saying why), once
    /// more with Windows' software encoder. MP4 retries on the file-based pipeline, which reads some recordings the
    /// stream-based one refuses. Each attempt starts from an empty output. (Issue #3, diagnosed by teecil.)
    /// </summary>
    private static void TranscodeWithFallback(string file, string temp, MediaEncodingProfile profile, Action<double>? fraction,
        CancellationToken ct, TrimRange? range = null, TimeSpan duration = default)
    {
        try
        {
            TranscodeAsync(file, temp, profile, fraction, ct, range, duration).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && (ex.HResult == unchecked((int)0x80004005) || ex is MediaFormatException { Unexplained: true }))
        {
            bool fileBased = range is not null || profile.Container?.Subtype == "MPEG4";
            try { TranscodeAsync(file, temp, profile, fraction, ct, range, duration, hardware: false, fileBased).GetAwaiter().GetResult(); }
            catch (Exception retry) when (retry is not OperationCanceledException && VideoFormat.Describe(file) is { } format)
            {
                // Windows gives no reason worth showing: say what the video is, so the person (and a bug report) knows.
                throw new MediaFormatException(format.Unsupported
                    ? $"This video is stored as {format.Name}, which Windows can't convert."
                    : $"{Friendly(retry).TrimEnd('.')} (the video is {format.Name}).");
            }
        }
    }

    /// <summary>Peak target for Normalize: −1 dBFS leaves headroom for lossy encoders.</summary>
    public const double NormalizePeak = 0.891;

    /// <summary>
    /// Decodes to 16-bit PCM, scales every sample so the loudest peak reaches <see cref="NormalizePeak"/>, then
    /// encodes back to the source's format. A file already at full volume is skipped rather than re-encoded.
    /// </summary>
    private static async Task<FileOutcome> NormalizeAsync(string file, OperationContext context, CancellationToken ct)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "OrbitDrop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var source = await StorageFile.GetFileFromPathAsync(file);
            var props = await source.Properties.RetrievePropertiesAsync(["System.Audio.SampleRate", "System.Audio.ChannelCount"]);
            uint rate = props.TryGetValue("System.Audio.SampleRate", out var r) && r is uint sr and > 0 ? sr : 44100;
            uint channels = props.TryGetValue("System.Audio.ChannelCount", out var c) && c is uint ch and > 0 ? Math.Min(ch, 2u) : 2;

            var pcm = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
            pcm.Audio = AudioEncodingProperties.CreatePcm(rate, channels, 16);
            string decoded = Path.Combine(scratch, "decoded.wav");
            File.Create(decoded).Dispose();
            await TranscodeAsync(file, decoded, pcm, f => context.ReportFileFraction?.Invoke(f * 0.4), ct);

            byte[] wav = File.ReadAllBytes(decoded);
            double gain = WavPcm.NormalizeGain(wav, NormalizePeak);
            if (gain <= 1.02) return new FileOutcome(file, OutcomeStatus.Skipped, Message: "It's already at full volume.");
            WavPcm.ApplyGain(wav, gain);
            string louder = Path.Combine(scratch, "louder.wav");
            File.WriteAllBytes(louder, wav);

            var (profile, extension) = SameKindAudio(file, lossyOnly: false);
            if (extension == ".wav") profile.Audio = AudioEncodingProperties.CreatePcm(rate, channels, 16);
            string output = await Task.Run(() => SafeOutput.WriteToPath(file, context.Output, extension, "normalized",
                writeToTemp: temp => TranscodeAsync(louder, temp, profile, f => context.ReportFileFraction?.Invoke(0.4 + f * 0.6), ct).GetAwaiter().GetResult(),
                validate: temp => ValidateContainer(temp, extension),
                ct, hideWhileWriting: false), ct);
            return new FileOutcome(file, OutcomeStatus.Succeeded, output);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// A profile in the source's own audio format where Windows can write it. Lossless or unwritable sources become
    /// M4A when <paramref name="lossyOnly"/> (a bitrate only means something for lossy formats).
    /// </summary>
    private static (MediaEncodingProfile Profile, string Extension) SameKindAudio(string file, bool lossyOnly) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".mp3" => (MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High), ".mp3"),
            ".wma" => (MediaEncodingProfile.CreateWma(AudioEncodingQuality.High), ".wma"),
            ".wav" when !lossyOnly => (MediaEncodingProfile.CreateWav(AudioEncodingQuality.High), ".wav"),
            ".flac" when !lossyOnly => (MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High), ".flac"),
            _ => (MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), ".m4a"),
        };

    /// <summary>Scales to fit inside a box, keeping aspect ratio, never upscaling, with even dimensions (H.264 requirement).</summary>
    public static (uint W, uint H) FitWithin(uint width, uint height, uint maxW, uint maxH)
    {
        if (width == 0 || height == 0) return (0, 0);
        bool portrait = height > width;
        (uint boxW, uint boxH) = portrait ? (maxH, maxW) : (maxW, maxH);
        double scale = Math.Min(1.0, Math.Min((double)boxW / width, (double)boxH / height));
        uint w = (uint)Math.Round(width * scale) & ~1u, h = (uint)Math.Round(height * scale) & ~1u;
        return (Math.Max(2, w), Math.Max(2, h));
    }

    private static async Task TranscodeAsync(string file, string destinationPath, MediaEncodingProfile profile,
        Action<double>? fraction, CancellationToken ct, TrimRange? trim = null, TimeSpan duration = default, bool hardware = true,
        bool fileBased = false)
    {
        fileBased |= trim is not null;
        // A failed earlier attempt may have left bytes behind, and the file-based pipeline writes over them without
        // shortening the file.
        new FileStream(destinationPath, FileMode.Truncate, FileAccess.Write).Dispose();
        var source = await StorageFile.GetFileFromPathAsync(file);
        var destination = await StorageFile.GetFileFromPathAsync(destinationPath);
        // A native Windows stream: muxers such as ASF (WMV/WMA) need features a wrapped .NET stream lacks.
        // Trimming needs the file-based pipeline instead (the stream-based one refuses trim settings), which opens
        // the files itself.
        using IRandomAccessStream? input = fileBased ? null : await source.OpenAsync(FileAccessMode.Read);
        using IRandomAccessStream? output = fileBased ? null : await destination.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware };
        if (trim is not null)
        {
            // Both are positions in the source: output starts at TrimStartTime and stops at TrimStopTime.
            transcoder.TrimStartTime = trim.Start;
            if (duration == TimeSpan.Zero || trim.End < duration) transcoder.TrimStopTime = trim.End;
        }
        var prepared = fileBased
            ? await transcoder.PrepareFileTranscodeAsync(source, destination, profile)
            : await transcoder.PrepareStreamTranscodeAsync(input!, output!, profile);
        if (!prepared.CanTranscode)
            throw prepared.FailureReason switch
            {
                TranscodeFailureReason.CodecNotFound => new MediaFormatException("Windows doesn't have a codec for this file. Installing the matching Video Extension from the Microsoft Store may help."),
                TranscodeFailureReason.InvalidProfile => new MediaFormatException(profile.Audio is not null && profile.Video is null
                    ? "This file has no sound to extract."
                    : "This file can't be converted to that format."),
                _ => new MediaFormatException("Windows couldn't read this file.", Unexplained: true),
            };
        var progress = new Progress<double>(p => fraction?.Invoke(p / 100.0));
        await prepared.TranscodeAsync().AsTask(ct, progress);
        if (output is not null) await output.FlushAsync();
        fraction?.Invoke(1);
    }

    /// <summary>Checks the container signature, so a transcoder that "succeeds" with garbage can't publish a file.</summary>
    public static string? ValidateContainer(string path, string extension)
    {
        var head = new byte[16];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (fs.Length < 64) return "the output is too small to be valid";
            fs.ReadExactly(head);
        }
        bool ok = extension switch
        {
            ".mp4" or ".m4a" => head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p',
            ".wav" => head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E',
            ".mp3" => (head[0] == 'I' && head[1] == 'D' && head[2] == '3') || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0),
            ".wmv" or ".wma" => head[0] == 0x30 && head[1] == 0x26 && head[2] == 0xB2 && head[3] == 0x75, // ASF header GUID
            ".flac" => head[0] == 'f' && head[1] == 'L' && head[2] == 'a' && head[3] == 'C',
            _ => true,
        };
        return ok ? null : $"the output isn't a valid {extension.TrimStart('.').ToUpperInvariant()} file";
    }

    private static string Friendly(Exception ex) => ex switch
    {
        MediaFormatException m => m.Message,
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        IOException io when io.HResult == unchecked((int)0x80070070) => "There isn't enough disk space.",
        _ when ex.HResult == unchecked((int)0xC00D36C4) => "Windows doesn't support this file's format.", // MF_E_UNSUPPORTED_BYTESTREAM_TYPE
        _ => $"Windows reported an error converting this file (0x{ex.HResult:X8}).",
    };

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Video", "MP4, WMV Â· extract audio Â· compress (720p, 1080p, 480p, half size) Â· remove audio (via Windows Media Foundation)");
        yield return ("Audio", "MP3, M4A, WAV, WMA, FLAC Â· compress (128/96/64 kbps) Â· mono/stereo");
    }

    /// <param name="Unexplained">Windows refused without a reason, which is often the hardware encoder: worth a retry.</param>
    private sealed class MediaFormatException(string message, bool Unexplained = false) : Exception(message)
    {
        public bool Unexplained { get; } = Unexplained;
    }
}
