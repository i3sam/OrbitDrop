using OrbitDrop.Conversion.Media;
using OrbitDrop.Core;

namespace OrbitDrop.Tests;

/// <summary>Skips when Windows Media Foundation is absent (e.g. Windows Server images without the feature).</summary>
public sealed class MediaFactAttribute : FactAttribute
{
    public MediaFactAttribute()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll")))
            Skip = "Windows Media Foundation is not installed on this machine.";
    }
}

/// <summary><see cref="MediaFactAttribute"/> for theories.</summary>
public sealed class MediaTheoryAttribute : TheoryAttribute
{
    public MediaTheoryAttribute()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll")))
            Skip = "Windows Media Foundation is not installed on this machine.";
    }
}

public class MediaRegistryTests
{
    private static readonly MediaFoundationProvider Provider = new();
    private static FileSet Files(params string[] n) => new(n.Select(x => Path.Combine(@"C:\x", x)));
    private static List<string> Labels(FileSet f, OperationMode m) => Provider.GetOperations(f, m).OrderBy(d => d.Order).Select(d => d.Label).ToList();
    /// <summary>What the wheel shows: groups collapsed to one segment.</summary>
    private static List<string> Wheel(FileSet f, OperationMode m) => OperationMenu.TopLevel(Provider.GetOperations(f, m).OrderBy(d => d.Order).ToList()).Select(d => d.Label).ToList();

    [Fact]
    public void Mp4_offers_wmv_and_audio_extraction_but_not_mp4()
    {
        Assert.Equal(["WMV", "MP3", "M4A", "WAV", "WMA", "FLAC"], Labels(Files("clip.mp4"), OperationMode.Convert));
        Assert.Equal("Extract audio as %1", Provider.GetOperations(Files("clip.mp4"), OperationMode.Convert).First(d => d.Label == "MP3").DropText);
    }

    [Fact]
    public void Video_and_audio_get_their_own_grouped_tools()
    {
        Assert.Equal(["Trim", "Compress", "Remove audio"], Wheel(Files("a.mov"), OperationMode.Tools));
        Assert.Equal(["Trim", "720p", "1080p", "480p", "Half size", "Remove audio"], Labels(Files("a.mov"), OperationMode.Tools));
        Assert.Equal(["Trim", "Compress", "Normalize", "Channels"], Wheel(Files("a.mp3"), OperationMode.Tools));
        Assert.Equal(["Trim", "128 kbps", "96 kbps", "64 kbps", "Normalize", "Mono", "Stereo"], Labels(Files("a.mp3"), OperationMode.Tools));
        Assert.DoesNotContain("Trim", Labels(Files("a.mp3", "b.mp3"), OperationMode.Tools)); // one file at a time
    }

    [Fact]
    public void Audio_and_mixed_media_selections_get_audio_targets_only()
    {
        Assert.Equal(["M4A", "WAV", "WMA", "FLAC"], Labels(Files("song.mp3"), OperationMode.Convert));
        Assert.Equal(["MP3", "M4A", "WAV", "WMA", "FLAC"], Labels(Files("clip.mp4", "song.flac"), OperationMode.Convert));
    }

    [Fact]
    public void Non_media_in_the_selection_disables_media_options() =>
        Assert.Empty(Labels(Files("clip.mp4", "photo.jpg"), OperationMode.Convert));

    [Theory]
    [InlineData(1920u, 1080u, 1280u, 720u)]
    [InlineData(1080u, 1920u, 720u, 1280u)]   // portrait stays portrait
    [InlineData(640u, 480u, 640u, 480u)]      // never upscale
    [InlineData(1281u, 721u, 1278u, 720u)]    // aspect kept, rounded down to even (H.264)
    public void Compress_fits_within_720p_keeping_aspect(uint w, uint h, uint ew, uint eh) =>
        Assert.Equal((ew, eh), MediaFoundationProvider.FitWithin(w, h, 1280, 720));
}

/// <summary>Real transcodes through Media Foundation on synthetic fixtures; outputs are signature-checked.</summary>
public class MediaTranscodeTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static async Task<(FileOutcome Outcome, List<double> Fractions)> Run(string id, string source)
    {
        var provider = new MediaFoundationProvider();
        var files = new FileSet([source]);
        var op = Enum.GetValues<OperationMode>().SelectMany(m => provider.GetOperations(files, m)).Single(d => d.Id == id);
        var fractions = new List<double>();
        var ctx = Ctx.Default() with { ReportFileFraction = f => { lock (fractions) fractions.Add(f); } };
        var c = new Collector();
        await provider.ExecuteAsync(op, files, ctx, c, CancellationToken.None);
        return (Assert.Single(c.Outcomes), fractions);
    }

    private static string Copy(TempFolder t, string fixture)
    {
        string dst = t.File(fixture);
        File.Copy(Fixture(fixture), dst);
        return dst;
    }

    [MediaFact]
    public async Task Extracts_audio_from_video_as_wav()
    {
        using var t = new TempFolder();
        string src = Copy(t, "testpattern.mp4");
        string hash = SafeOutput.Sha256(src);
        var (o, fractions) = await Run("convert.media.wav", src);
        Assert.Equal(OutcomeStatus.Succeeded, o.Status);
        Assert.Equal(t.File("testpattern.wav"), o.Output);
        Assert.Null(MediaFoundationProvider.ValidateContainer(o.Output!, ".wav"));
        Assert.True(new FileInfo(o.Output!).Length > 100_000, "2 s of 44.1 kHz PCM should be well over 100 KB");
        Assert.Equal(1.0, fractions.Last());
        Assert.Equal(hash, SafeOutput.Sha256(src));
    }

    [MediaFact]
    public async Task Converts_video_to_m4a_and_wmv()
    {
        using var t = new TempFolder();
        string src = Copy(t, "testpattern.mp4");
        var (m4a, _) = await Run("convert.media.m4a", src);
        var (wmv, _) = await Run("convert.media.wmv", src);
        Assert.True(m4a.Status == OutcomeStatus.Succeeded, $"M4A: {m4a.Message}");
        Assert.True(wmv.Status == OutcomeStatus.Succeeded, $"WMV: {wmv.Message}");
        Assert.Null(MediaFoundationProvider.ValidateContainer(m4a.Output!, ".m4a"));
        Assert.Null(MediaFoundationProvider.ValidateContainer(wmv.Output!, ".wmv"));
    }

    [MediaFact]
    public async Task Remove_audio_and_compress_produce_valid_mp4s_with_suffixes()
    {
        using var t = new TempFolder();
        string src = Copy(t, "testpattern.mp4");
        var (muted, _) = await Run("tool.video.mute", src);
        var (small, _) = await Run("tool.video.compress.720", src);
        var (half, _) = await Run("tool.video.compress.half", src);
        Assert.True(half.Status == OutcomeStatus.Succeeded, $"Half: {half.Message}");
        Assert.Null(MediaFoundationProvider.ValidateContainer(half.Output!, ".mp4"));
        Assert.Equal(t.File("testpattern (no audio).mp4"), muted.Output);
        Assert.Equal(t.File("testpattern (compressed).mp4"), small.Output);
        Assert.Null(MediaFoundationProvider.ValidateContainer(muted.Output!, ".mp4"));
        Assert.Null(MediaFoundationProvider.ValidateContainer(small.Output!, ".mp4"));
    }

    [MediaFact]
    public async Task Extracting_audio_from_a_silent_video_fails_cleanly()
    {
        using var t = new TempFolder();
        string src = Copy(t, "silent.mp4");
        var (o, _) = await Run("convert.media.mp3", src);
        Assert.Equal(OutcomeStatus.Failed, o.Status);
        Assert.False(string.IsNullOrWhiteSpace(o.Message));
        Assert.Equal([src], Directory.GetFiles(t.Path)); // no output, no temp file
    }

    [MediaFact]
    public async Task Corrupt_video_fails_with_a_message_and_leaves_nothing()
    {
        using var t = new TempFolder();
        File.WriteAllBytes(t.File("broken.mp4"), new byte[4096]);
        var (o, _) = await Run("convert.media.wav", t.File("broken.mp4"));
        Assert.Equal(OutcomeStatus.Failed, o.Status);
        Assert.Single(Directory.GetFiles(t.Path));
    }
}

public class AudioToolTests
{
    private static async Task<FileOutcome> Run(string id, string file)
    {
        var provider = new MediaFoundationProvider();
        var files = new FileSet([file]);
        var op = Enum.GetValues<OperationMode>().SelectMany(m => provider.GetOperations(files, m)).Single(d => d.Id == id);
        var c = new Collector();
        await provider.ExecuteAsync(op, files, Ctx.Default(), c, CancellationToken.None);
        return Assert.Single(c.Outcomes);
    }

    [MediaFact]
    public async Task Audio_compresses_converts_to_flac_and_goes_mono()
    {
        using var t = new TempFolder();
        string video = Path.Combine(t.Path, "testpattern.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testpattern.mp4"), video);
        var m4a = await Run("convert.media.m4a", video);
        Assert.True(m4a.Status == OutcomeStatus.Succeeded, m4a.Message);

        var small = await Run("tool.audio.compress.64", m4a.Output!);
        var flac = await Run("convert.media.flac", m4a.Output!);
        var mono = await Run("tool.audio.mono", m4a.Output!);
        Assert.True(small.Status == OutcomeStatus.Succeeded, $"compress: {small.Message}");
        Assert.True(flac.Status == OutcomeStatus.Succeeded, $"flac: {flac.Message}");
        Assert.True(mono.Status == OutcomeStatus.Succeeded, $"mono: {mono.Message}");
        Assert.Equal(t.File("testpattern (compressed).m4a"), small.Output);
        Assert.Equal(t.File("testpattern (mono).m4a"), mono.Output);
        Assert.Null(MediaFoundationProvider.ValidateContainer(flac.Output!, ".flac"));
    }
}

public class NormalizeTests
{
    private static byte[] Tone(double amplitude, int seconds = 1, int rate = 44100)
    {
        int samples = rate * seconds;
        var data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * amplitude * 32767));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public void Gain_brings_the_peak_to_the_target()
    {
        var wav = Tone(0.25);
        double gain = WavPcm.NormalizeGain(wav, 0.891);
        Assert.InRange(gain, 3.5, 3.6);
        WavPcm.ApplyGain(wav, gain);
        Assert.InRange(WavPcm.NormalizeGain(wav, 0.891), 0.99, 1.01);
    }

    [MediaFact]
    public async Task Quiet_audio_is_normalized_and_loud_audio_is_skipped()
    {
        using var t = new TempFolder();
        string quiet = t.File("quiet.wav");
        File.WriteAllBytes(quiet, Tone(0.2));
        var provider = new MediaFoundationProvider();
        var op = provider.GetOperations(new FileSet([quiet]), OperationMode.Tools).Single(o => o.Id == "tool.audio.normalize");
        var c = new Collector();
        await provider.ExecuteAsync(op, new FileSet([quiet]), Ctx.Default(), c, CancellationToken.None);
        var o = Assert.Single(c.Outcomes);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("quiet (normalized).wav"), o.Output);
        Assert.InRange(WavPcm.NormalizeGain(File.ReadAllBytes(o.Output!), MediaFoundationProvider.NormalizePeak), 0.97, 1.03);

        string loud = t.File("loud.wav");
        File.WriteAllBytes(loud, Tone(0.95));
        var c2 = new Collector();
        await provider.ExecuteAsync(op, new FileSet([loud]), Ctx.Default(), c2, CancellationToken.None);
        Assert.Equal(OutcomeStatus.Skipped, Assert.Single(c2.Outcomes).Status);
    }
}

public class TrimTests
{
    [MediaFact]
    public async Task Trim_keeps_only_the_chosen_range()
    {
        using var t = new TempFolder();
        string clip = t.File("clip.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testpattern.mp4"), clip);
        var full = await MediaFoundationProvider.DurationAsync(clip);
        if (full == TimeSpan.Zero) return; // no MP4 property handler (some Windows Server images): length unknown
        Assert.True(full > TimeSpan.FromSeconds(1.5), $"fixture is {full}");

        var provider = new MediaFoundationProvider();
        var op = provider.GetOperations(new FileSet([clip]), OperationMode.Tools).Single(o => o.Id == MediaFoundationProvider.TrimId);
        var c = new Collector();
        var range = new TrimRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5));
        await provider.ExecuteAsync(op, new FileSet([clip]), Ctx.Default() with { Input = range }, c, CancellationToken.None);
        var o = Assert.Single(c.Outcomes);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("clip (trimmed).mp4"), o.Output);
        var trimmed = await MediaFoundationProvider.DurationAsync(o.Output!);
        Assert.InRange(trimmed.TotalSeconds, 0.8, 1.25);
    }
}
