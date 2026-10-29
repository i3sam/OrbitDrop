using System.Buffers.Binary;
using System.Text;

namespace OrbitDrop.Conversion.Media;

/// <summary>
/// What a video is stored as, read from its MP4/MOV header (the sample description, and for H.264 its profile), for
/// failures Windows explains no better than "couldn't read this file". <see cref="Unsupported"/> formats are ones
/// Windows has no decoder for, so no retry can convert them.
/// </summary>
/// <param name="Name">For people: "Apple ProRes", "H.264 (10-bit)", "Motion JPEG"…</param>
public sealed record VideoFormat(string Name, string FourCc, bool Unsupported)
{
    /// <summary>Null when the file isn't an MP4/MOV, or its video track can't be found.</summary>
    public static VideoFormat? Describe(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FromMovie(ReadMoov(fs));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The first video sample entry in a 'moov' box's contents. Public for tests.</summary>
    public static VideoFormat? FromMovie(byte[]? moov)
    {
        if (moov is null) return null;
        for (int at = IndexOf(moov, "stsd"u8, 0); at >= 0; at = IndexOf(moov, "stsd"u8, at + 4))
        {
            // 'stsd', version and flags (4), entry count (4), then the first entry: size (4) and its format (4).
            int entry = at + 4 + 8;
            if (entry + 8 > moov.Length) break;
            string fourCc = Encoding.ASCII.GetString(moov, entry + 4, 4);
            if (Recognize(fourCc, moov, entry) is { } format) return format;
        }
        return null;
    }

    private static VideoFormat? Recognize(string fourCc, byte[] moov, int entry) => fourCc switch
    {
        "avc1" or "avc3" => H264(moov, entry),
        "hvc1" or "hev1" => new("HEVC (H.265)", fourCc, false),
        "dvh1" or "dvhe" => new("HEVC with Dolby Vision", fourCc, false),
        "av01" => new("AV1", fourCc, false),
        "vp09" => new("VP9", fourCc, false),
        "mp4v" => Mp4Visual(moov, entry),
        "jpeg" or "mjpa" or "mjpb" or "MJPG" => new("Motion JPEG", fourCc, true),
        "apch" or "apcn" or "apcs" or "apco" or "ap4h" or "ap4x" => new("Apple ProRes", fourCc, true),
        "dvc " or "dvcp" or "dv5n" or "dv5p" or "dvhp" or "dvh5" or "dvh6" => new("DV", fourCc, true),
        "cvid" or "rle " or "smc " or "8BPS" or "png " or "tiff" => new($"an old QuickTime format ({fourCc.Trim()})", fourCc, true),
        _ => null, // not a video entry (audio, timecode, metadata): keep looking
    };

    /// <summary>
    /// 'mp4v' only says "described by an MPEG-4 descriptor": the codec is its object type (esds › ES_Descriptor ›
    /// DecoderConfigDescriptor). MP4 files from cameras and FFmpeg store Motion JPEG this way.
    /// </summary>
    private static VideoFormat Mp4Visual(byte[] moov, int entry) => ObjectType(moov, entry) switch
    {
        0x6C or 0x6D => new("Motion JPEG", "mp4v", true),
        >= 0x60 and <= 0x65 => new("MPEG-2", "mp4v", false),
        0x6A => new("MPEG-1", "mp4v", false),
        _ => new("MPEG-4 Part 2", "mp4v", false),
    };

    private static int ObjectType(byte[] moov, int entry)
    {
        int at = IndexOf(moov, "esds"u8, entry);
        if (at < 0) return 0;
        int i = at + 8; // 'esds', version and flags
        int Length() { int n = 0; for (int k = 0; k < 4 && i < moov.Length; k++) { byte b = moov[i++]; n = (n << 7) | (b & 0x7F); if ((b & 0x80) == 0) break; } return n; }
        if (i >= moov.Length || moov[i++] != 0x03) return 0; // ES_Descriptor
        Length();
        i += 2; // ES_ID
        if (i >= moov.Length) return 0;
        byte flags = moov[i++];
        if ((flags & 0x80) != 0) i += 2;                                   // dependsOn_ES_ID
        if ((flags & 0x40) != 0 && i < moov.Length) i += 1 + moov[i];      // URL
        if ((flags & 0x20) != 0) i += 2;                                   // OCR_ES_ID
        if (i >= moov.Length || moov[i++] != 0x04) return 0; // DecoderConfigDescriptor
        Length();
        return i < moov.Length ? moov[i] : 0;
    }

    /// <summary>H.264, with the profiles Windows' decoder can't read (10-bit and 4:2:2/4:4:4 colour) marked unsupported.</summary>
    private static VideoFormat H264(byte[] moov, int entry)
    {
        int avcC = IndexOf(moov, "avcC"u8, entry);
        int profile = avcC >= 0 && avcC + 6 < moov.Length ? moov[avcC + 5] : 0; // after 'avcC': version, then profile
        return profile switch
        {
            110 => new("H.264 (10-bit)", "avc1", true),
            122 => new("H.264 (4:2:2 colour)", "avc1", true),
            244 => new("H.264 (4:4:4 colour)", "avc1", true),
            _ => new("H.264", "avc1", false),
        };
    }

    /// <summary>The contents of the top-level 'moov' box (wherever it is in the file), or null. Capped at 64 MB.</summary>
    private static byte[]? ReadMoov(FileStream fs)
    {
        var head = new byte[16];
        long pos = 0, length = fs.Length;
        while (pos + 8 <= length)
        {
            fs.Position = pos;
            if (fs.Read(head, 0, 16) < 8) return null;
            long size = BinaryPrimitives.ReadUInt32BigEndian(head);
            int header = 8;
            if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(8)); header = 16; }
            else if (size == 0) size = length - pos;
            if (size < header) return null;
            if (head[4] == 'm' && head[5] == 'o' && head[6] == 'o' && head[7] == 'v')
            {
                long body = size - header;
                if (body <= 0 || body > 64L * 1024 * 1024) return null;
                var moov = new byte[body];
                fs.Position = pos + header;
                fs.ReadExactly(moov);
                return moov;
            }
            pos += size;
        }
        return null;
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> tag, int from) =>
        from >= data.Length ? -1 : data.AsSpan(from).IndexOf(tag) is var i and >= 0 ? from + i : -1;
}
