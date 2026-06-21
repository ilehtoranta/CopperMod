using System.IO.Compression;
using CopperDisk;

namespace CopperDisk.Tests;

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
        Assert.Equal(AmigaTrackFeatures.PreservedTrackData, track.Features);
        Assert.Equal(new byte[] { 0x44, 0x89, 0x44, 0xA9 }, track.EncodedData.ToArray());
    }

    [Fact]
    public void IpfLoadedMediaIsReadOnly()
    {
        var media = AmigaDiskLoader.FromIpfBytes(CreateSingleTrackIpf());

        Assert.False(media is IWritableAmigaDiskMedia);
    }

    [Fact]
    public void IpfLoaderAcceptsReadOnlySpanInput()
    {
        var padded = new byte[CreateSingleTrackIpf().Length + 4];
        var image = CreateSingleTrackIpf();
        image.CopyTo(padded.AsSpan(2));

        var media = AmigaDiskLoader.FromIpfBytes(padded.AsSpan(2, image.Length));

        Assert.Equal(32, media.ReadTrack(0, 0).BitLength);
    }

    [Fact]
    public void DecodeExposesReadOnlyTrackOutputs()
    {
        var disk = IpfDecoder.Decode(CreateSingleTrackIpf());
        var track = Assert.Single(disk.Tracks);

        Assert.False(disk.Tracks is IpfTrack[]);
        Assert.IsType<ReadOnlyMemory<byte>>(track.EncodedData);
        Assert.Throws<NotSupportedException>(() => ((IList<IpfTrack>)disk.Tracks)[0] = track);
    }

    [Fact]
    public void DefaultOptionsUseInitOnlyProperties()
    {
        Assert.True(HasInitOnlySetter(nameof(IpfDecodeOptions.AlignTracksToWord)));
        Assert.True(HasInitOnlySetter(nameof(IpfDecodeOptions.StartAtIndex)));
        Assert.True(IpfDecodeOptions.Default.AlignTracksToWord);
        Assert.True(IpfDecodeOptions.Default.StartAtIndex);
    }

    [Fact]
    public void DecodePlacesStreamAtStartBitWhenStartingAtIndex()
    {
        var image = CreateSingleRawTrackIpf(startBit: 8);

        var disk = IpfDecoder.Decode(image);

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(8, track.StartBit);
        Assert.Equal(new byte[] { 0xA5, 0xDE, 0xAD, 0xBE, 0xEF, 0xA5 }, track.EncodedData.ToArray());
    }

    [Fact]
    public void DecodeCanStartAtDataStreamInsteadOfIndex()
    {
        var image = CreateSingleRawTrackIpf(startBit: 8);

        var disk = IpfDecoder.Decode(image, new IpfDecodeOptions { StartAtIndex = false });

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(0, track.StartBit);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xA5, 0xA5 }, track.EncodedData.ToArray());
    }

    [Fact]
    public void DecodePlacesNonByteStartBitAtPhysicalIndex()
    {
        var image = CreateSingleRawTrackIpf(startBit: 5, new byte[] { 0x44, 0x89, 0x12, 0x34 });

        var disk = IpfDecoder.Decode(image);

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(5, track.StartBit);
        Assert.Equal(0x4489u, ReadBits(track.EncodedData.Span, track.BitLength, 5, 16));
        Assert.Equal(0x1234u, ReadBits(track.EncodedData.Span, track.BitLength, 21, 16));
    }

    [Fact]
    public void DecodeCanIgnoreNonByteStartBitForDataRelativeMaterialization()
    {
        var image = CreateSingleRawTrackIpf(startBit: 5, new byte[] { 0x44, 0x89, 0x12, 0x34 });

        var disk = IpfDecoder.Decode(image, new IpfDecodeOptions { StartAtIndex = false });

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(0, track.StartBit);
        Assert.Equal(0x4489u, ReadBits(track.EncodedData.Span, track.BitLength, 0, 16));
        Assert.Equal(0x1234u, ReadBits(track.EncodedData.Span, track.BitLength, 16, 16));
    }

    [Fact]
    public void DecodeGeneratedMfmGapContinuesPreviousDataBitAndPreservesBoundary()
    {
        var image = CreateTwoBlockMfmGapBoundaryIpf();

        var disk = IpfDecoder.Decode(image);

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(48, track.BitLength);
        Assert.Equal(0xAAA9u, ReadBits(track.EncodedData.Span, track.BitLength, 0, 16));
        Assert.Equal(0x2Au, ReadBits(track.EncodedData.Span, track.BitLength, 16, 8));
        Assert.Equal(0x4489u, ReadBits(track.EncodedData.Span, track.BitLength, 24, 16));
    }

    [Fact]
    public void LoadPassesIpfOptionsToDirectIpfImages()
    {
        using var temp = new TempDiskFile(".ipf");
        File.WriteAllBytes(temp.Path, CreateSingleRawTrackIpf(startBit: 8));

        var result = AmigaDiskLoader.Load(temp.Path, new IpfDecodeOptions { StartAtIndex = false });

        Assert.Equal(0, result.Media.ReadTrack(0, 0).StartBit);
    }

    [Fact]
    public void LoadPassesIpfOptionsToZipWrappedIpfImages()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.ipf", CreateSingleRawTrackIpf(startBit: 8)));

        var result = AmigaDiskLoader.Load(temp.Path, new IpfDecodeOptions { StartAtIndex = false });

        Assert.Equal(0, result.Media.ReadTrack(0, 0).StartBit);
    }

    [Fact]
    public void DecodeExactModePreservesNonWordAndNonByteAlignedDescriptorTrackBits()
    {
        var image = CreateSingleRawTrackIpf(
            startBit: 5,
            new byte[] { 0x44, 0x89, 0x12, 0x34 },
            gapBits: 2,
            trackBits: 42);

        var disk = IpfDecoder.Decode(image, new IpfDecodeOptions { AlignTracksToWord = false });

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(42, track.BitLength);
        Assert.Equal(5, track.StartBit);
        Assert.Equal(6, track.EncodedData.Length);
        Assert.Equal(0x4489u, ReadBits(track.EncodedData.Span, track.BitLength, 5, 16));
        Assert.Equal(0x1234u, ReadBits(track.EncodedData.Span, track.BitLength, 21, 16));
    }

    [Fact]
    public void DecodeDefaultKeepsPublicWordAlignedIpfTracks()
    {
        var image = CreateSingleRawTrackIpf(
            startBit: 5,
            new byte[] { 0x44, 0x89, 0x12, 0x34 },
            gapBits: 2,
            trackBits: 42);

        var disk = IpfDecoder.Decode(image);

        Assert.Equal(48, Assert.Single(disk.Tracks).BitLength);
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
        return CreateSingleRawTrackIpf(startBit, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    }

    private static byte[] CreateSingleRawTrackIpf(int startBit, byte[] data)
    {
        return CreateSingleRawTrackIpf(startBit, data, gapBits: 8, trackBits: data.Length * 8 + 8);
    }

    private static byte[] CreateSingleRawTrackIpf(int startBit, byte[] data, int gapBits, int trackBits)
    {
        using var stream = new MemoryStream();
        WriteChunk(stream, "CAPS", Array.Empty<byte>());
        WriteChunk(stream, "INFO", BuildInfo());
        WriteChunk(stream, "IMGE", BuildRawImageDescriptor(startBit, data.Length * 8, gapBits, trackBits, blockCount: 1));
        var payload = BuildRawDataPayload(data, gapBits);
        WriteChunk(stream, "DATA", BuildDataHeader(payload.Length), payload);
        return stream.ToArray();
    }

    private static byte[] CreateTwoBlockMfmGapBoundaryIpf()
    {
        var payload = BuildTwoBlockMfmGapBoundaryPayload();
        using var stream = new MemoryStream();
        WriteChunk(stream, "CAPS", Array.Empty<byte>());
        WriteChunk(stream, "INFO", BuildInfo());
        WriteChunk(stream, "IMGE", BuildRawImageDescriptor(startBit: 0, dataBits: 32, gapBits: 8, trackBits: 40, blockCount: 2));
        WriteChunk(stream, "DATA", BuildDataHeader(payload.Length), payload);
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

    private static byte[] BuildRawImageDescriptor(int startBit, int dataBits, int gapBits, int trackBits, int blockCount)
    {
        using var stream = new MemoryStream();
        var trackBytes = (trackBits + 7) / 8;
        WriteUInt32(stream, 0); // cylinder
        WriteUInt32(stream, 0); // head
        WriteUInt32(stream, 2); // automatic density
        WriteUInt32(stream, 1); // 2us signal
        WriteUInt32(stream, (uint)trackBytes); // track size
        WriteUInt32(stream, 0); // start position
        WriteUInt32(stream, (uint)startBit);
        WriteUInt32(stream, (uint)dataBits); // data bits
        WriteUInt32(stream, (uint)gapBits); // gap bits
        WriteUInt32(stream, (uint)trackBits); // track bits
        WriteUInt32(stream, (uint)blockCount); // block count
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

    private static byte[] BuildRawDataPayload(byte[] data, int gapBits = 8)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, (uint)(data.Length * 8)); // block bits
        WriteUInt32(stream, (uint)gapBits);
        WriteUInt32(stream, (uint)data.Length); // CAPS block byte size
        WriteUInt32(stream, (uint)((gapBits + 7) / 8)); // CAPS gap byte size
        WriteUInt32(stream, 2); // raw encoder
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 0xA5); // gap value
        WriteUInt32(stream, 32); // stream offset
        stream.WriteByte(0x24); // raw, one-byte size
        stream.WriteByte((byte)data.Length);
        stream.Write(data);
        stream.WriteByte(0x00); // end
        return stream.ToArray();
    }

    private static byte[] BuildTwoBlockMfmGapBoundaryPayload()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 16); // block 0 bits
        WriteUInt32(stream, 8); // block 0 gap bits
        WriteUInt32(stream, 1); // CAPS block byte size
        WriteUInt32(stream, 1); // CAPS gap byte size
        WriteUInt32(stream, 1); // MFM encoder
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 0x00); // gap value
        WriteUInt32(stream, 64); // stream offset
        WriteUInt32(stream, 16); // block 1 bits
        WriteUInt32(stream, 0); // block 1 gap bits
        WriteUInt32(stream, 2); // CAPS block byte size
        WriteUInt32(stream, 0); // CAPS gap byte size
        WriteUInt32(stream, 1); // MFM encoder
        WriteUInt32(stream, 0); // flags
        WriteUInt32(stream, 0x00); // gap value
        WriteUInt32(stream, 68); // stream offset
        stream.WriteByte(0x22); // data, one-byte size
        stream.WriteByte(0x01);
        stream.WriteByte(0x01);
        stream.WriteByte(0x00); // end block 0
        stream.WriteByte(0x21); // mark, one-byte size
        stream.WriteByte(0x02);
        stream.WriteByte(0x44);
        stream.WriteByte(0x89);
        stream.WriteByte(0x00); // end
        return stream.ToArray();
    }

    private static uint ReadBits(ReadOnlySpan<byte> data, int bitLength, int bitOffset, int bitCount)
    {
        var value = 0u;
        for (var bit = 0; bit < bitCount; bit++)
        {
            var trackBit = (bitOffset + bit) % bitLength;
            value = (value << 1) | (uint)((data[trackBit >> 3] >> (7 - (trackBit & 7))) & 1);
        }

        return value;
    }

    private static bool HasInitOnlySetter(string propertyName)
    {
        var property = typeof(IpfDecodeOptions).GetProperty(propertyName);
        var modifiers = property?.SetMethod?.ReturnParameter.GetRequiredCustomModifiers();
        return modifiers?.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true;
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

    private static void WriteZip(string path, params (string Name, byte[] Data)[] entries)
    {
        using var file = File.Open(path, FileMode.Create, FileAccess.ReadWrite);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var zipEntry = archive.CreateEntry(entry.Name);
            using var stream = zipEntry.Open();
            stream.Write(entry.Data);
        }
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private sealed class TempDiskFile : IDisposable
    {
        public TempDiskFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
        }
    }
}
