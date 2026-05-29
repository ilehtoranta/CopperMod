using CopperMod.Ipf;

namespace CopperMod.Ipf.Tests;

public sealed class IpfDecoderTests
{
    [Fact]
    public void DecodeSynthesizesMfmTrackFromCapsDataStream()
    {
        var image = CreateSingleTrackIpf();

        var disk = IpfDecoder.Decode(image);

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(0, track.Cylinder);
        Assert.Equal(0, track.Head);
        Assert.Equal(32, track.BitLength);
        Assert.Equal(new byte[] { 0x44, 0x89, 0x44, 0xA9 }, track.Data);
    }

    [Fact]
    public void DecodePlacesStreamAtStartBitWhenStartingAtIndex()
    {
        var image = CreateSingleRawTrackIpf(startBit: 8);

        var disk = IpfDecoder.Decode(image);

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(8, track.StartBit);
        Assert.Equal(new byte[] { 0xA5, 0xDE, 0xAD, 0xBE, 0xEF, 0xA5 }, track.Data);
    }

    [Fact]
    public void DecodeCanStartAtDataStreamInsteadOfIndex()
    {
        var image = CreateSingleRawTrackIpf(startBit: 8);

        var disk = IpfDecoder.Decode(image, new IpfDecodeOptions { StartAtIndex = false });

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(0, track.StartBit);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xA5, 0xA5 }, track.Data);
    }

    private static byte[] CreateSingleTrackIpf()
    {
        using var stream = new MemoryStream();
        WriteChunk(stream, "CAPS", Array.Empty<byte>());
        WriteChunk(stream, "INFO", BuildInfo());
        WriteChunk(stream, "IMGE", BuildImageDescriptor());
        WriteChunk(stream, "DATA", BuildDataHeader(40), BuildDataPayload());
        return stream.ToArray();
    }

    private static byte[] CreateSingleRawTrackIpf(int startBit)
    {
        using var stream = new MemoryStream();
        WriteChunk(stream, "CAPS", Array.Empty<byte>());
        WriteChunk(stream, "INFO", BuildInfo());
        WriteChunk(stream, "IMGE", BuildRawImageDescriptor(startBit));
        WriteChunk(stream, "DATA", BuildDataHeader(39), BuildRawDataPayload());
        return stream.ToArray();
    }

    private static byte[] BuildInfo()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 1); // floppy disk
        WriteUInt32(stream, 1); // CAPS encoder
        WriteUInt32(stream, 1); // encoder revision
        WriteUInt32(stream, 0); // release
        WriteUInt32(stream, 0); // revision
        WriteUInt32(stream, 0); // origin
        WriteUInt32(stream, 0); // min cylinder
        WriteUInt32(stream, 0); // max cylinder
        WriteUInt32(stream, 0); // min head
        WriteUInt32(stream, 0); // max head
        WriteUInt32(stream, 0); // date
        WriteUInt32(stream, 0); // time
        WriteUInt32(stream, 1); // Amiga platform
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 1); // disk number
        WriteUInt32(stream, 0); // user id
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        return stream.ToArray();
    }

    private static byte[] BuildImageDescriptor()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 0); // cylinder
        WriteUInt32(stream, 0); // head
        WriteUInt32(stream, 2); // automatic density
        WriteUInt32(stream, 1); // 2us signal
        WriteUInt32(stream, 4); // track size
        WriteUInt32(stream, 0); // start position
        WriteUInt32(stream, 0); // start bit
        WriteUInt32(stream, 32); // data bits
        WriteUInt32(stream, 0); // gap bits
        WriteUInt32(stream, 32); // track bits
        WriteUInt32(stream, 1); // block count
        WriteUInt32(stream, 0); // process
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 1); // DATA id
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        return stream.ToArray();
    }

    private static byte[] BuildRawImageDescriptor(int startBit)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 0); // cylinder
        WriteUInt32(stream, 0); // head
        WriteUInt32(stream, 2); // automatic density
        WriteUInt32(stream, 1); // 2us signal
        WriteUInt32(stream, 5); // track size
        WriteUInt32(stream, 0); // start position
        WriteUInt32(stream, (uint)startBit);
        WriteUInt32(stream, 32); // data bits
        WriteUInt32(stream, 8); // gap bits
        WriteUInt32(stream, 40); // track bits
        WriteUInt32(stream, 1); // block count
        WriteUInt32(stream, 0); // process
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 1); // DATA id
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        return stream.ToArray();
    }

    private static byte[] BuildDataHeader(int dataSize)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, (uint)dataSize);
        WriteUInt32(stream, (uint)(dataSize * 8));
        WriteUInt32(stream, 0); // data CRC ignored by the first managed decoder
        WriteUInt32(stream, 1); // DATA id
        return stream.ToArray();
    }

    private static byte[] BuildDataPayload()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 32); // block bits
        WriteUInt32(stream, 0); // gap bits
        WriteUInt32(stream, 4); // CAPS block byte size
        WriteUInt32(stream, 0); // CAPS gap byte size
        WriteUInt32(stream, 1); // MFM encoder
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 0); // gap value
        WriteUInt32(stream, 32); // stream offset
        stream.WriteByte(0x21); // mark, one-byte size
        stream.WriteByte(0x02);
        stream.WriteByte(0x44);
        stream.WriteByte(0x89);
        stream.WriteByte(0x22); // data, one-byte size
        stream.WriteByte(0x01);
        stream.WriteByte(0xA1);
        stream.WriteByte(0x00); // end
        return stream.ToArray();
    }

    private static byte[] BuildRawDataPayload()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 32); // block bits
        WriteUInt32(stream, 8); // gap bits
        WriteUInt32(stream, 4); // CAPS block byte size
        WriteUInt32(stream, 1); // CAPS gap byte size
        WriteUInt32(stream, 2); // raw encoder
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 0xA5); // gap value
        WriteUInt32(stream, 32); // stream offset
        stream.WriteByte(0x24); // raw, one-byte size
        stream.WriteByte(0x04);
        stream.WriteByte(0xDE);
        stream.WriteByte(0xAD);
        stream.WriteByte(0xBE);
        stream.WriteByte(0xEF);
        stream.WriteByte(0x00); // end
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string id, byte[] payload, byte[]? trailingData = null)
    {
        var chunkSize = 12 + payload.Length;
        stream.Write(System.Text.Encoding.ASCII.GetBytes(id));
        WriteUInt32(stream, (uint)chunkSize);
        WriteUInt32(stream, 0);
        stream.Write(payload);
        if (trailingData != null)
        {
            stream.Write(trailingData);
        }
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
