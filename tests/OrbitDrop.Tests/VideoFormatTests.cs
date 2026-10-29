using OrbitDrop.Conversion.Media;
using OrbitDrop.Core;

namespace OrbitDrop.Tests;

/// <summary>
/// Videos Windows can't decode used to fail with "Windows couldn't read this file" (a user's report, 0.4.9). Now the
/// message says what the video is stored as.
/// </summary>
public sealed class VideoFormatTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("prores.mp4", "Apple ProRes", true)]
    [InlineData("mjpeg.mp4", "Motion JPEG", true)]
    [InlineData("h264-10bit.mp4", "H.264 (10-bit)", true)]
    [InlineData("testpattern.mp4", "H.264", false)]
    public void Reads_what_a_video_is_stored_as(string file, string name, bool unsupported)
    {
        var format = VideoFormat.Describe(Fixture(file));
        Assert.NotNull(format);
        Assert.Equal(name, format.Name);
        Assert.Equal(unsupported, format.Unsupported);
    }

    [Fact]
    public void Not_a_movie_has_no_format()
    {
        using var t = new TempFolder();
        string notes = t.File("notes.mp4");
        File.WriteAllText(notes, "not a video at all, just text pretending");
        Assert.Null(VideoFormat.Describe(notes));
        Assert.Null(VideoFormat.FromMovie(null));
    }

    [MediaTheory]
    [InlineData("prores.mp4", "Apple ProRes")]
    [InlineData("mjpeg.mp4", "Motion JPEG")]
    public async Task Compressing_a_video_Windows_cant_decode_says_what_it_is(string fixture, string name)
    {
        using var t = new TempFolder();
        string src = t.File(fixture);
        File.Copy(Fixture(fixture), src);
        var provider = new MediaFoundationProvider();
        var files = new FileSet([src]);
        var op = Enum.GetValues<OperationMode>().SelectMany(m => provider.GetOperations(files, m)).Single(d => d.Id == "tool.video.compress.720");
        var c = new Collector();
        await provider.ExecuteAsync(op, files, Ctx.Default(), c, CancellationToken.None);
        var outcome = Assert.Single(c.Outcomes);
        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Equal($"This video is stored as {name}, which Windows can't convert.", outcome.Message);
    }
}
