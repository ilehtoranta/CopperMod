using System.Buffers.Binary;
using System.IO.Compression;
using CopperDisk;

namespace CopperDisk.Tests;

public sealed class ScpDecoderTests
{
    [Fact]
    public void DecodeIndexedAmigaScpTrackToBitcells()
    {
        var disk = ScpDecoder.Decode(CreateScp(fluxTicks: [80, 160]));

        var track = Assert.Single(disk.Tracks);
        Assert.Equal(0, track.Cylinder);
        Assert.Equal(0, track.Head);
        Assert.Equal(ScpDecodeOptions.AmigaDoubleDensityBitCellsPerRevolution, track.BitLength);
        Assert.Equal(0xA0, track.EncodedData.Span[0]);
        Assert.Equal(AmigaTrackFeatures.PreservedTrackData | AmigaTrackFeatures.FluxCapture, track.Features);
        Assert.Empty(track.Regions);
    }

    [Fact]
    public void DecodeNonIndexedScpMarksApproximateIndex()
    {
        var disk = ScpDecoder.Decode(CreateScp(flags: 0, fluxTicks: [80]));

        var track = Assert.Single(disk.Tracks);
        Assert.True((track.Features & AmigaTrackFeatures.ApproximateIndex) != 0);
    }

    [Fact]
    public void DecodeNoFluxOverflowMarksNoFluxRegion()
    {
        var disk = ScpDecoder.Decode(CreateScp(fluxTicks: [0, 80]));

        var track = Assert.Single(disk.Tracks);
        var region = Assert.Single(track.Regions);
        Assert.Equal(0, region.StartBit);
        Assert.Equal(820, region.BitLength);
        Assert.Equal(AmigaTrackFeatures.NoFlux, region.Features);
        Assert.True((track.Features & AmigaTrackFeatures.NoFlux) != 0);
    }

    [Fact]
    public void DecodeRejectsUnsupportedScpMediaByDefault()
    {
        Assert.Throws<ScpDecodeException>(() => ScpDecoder.Decode(CreateScp(diskType: 0x08, fluxTicks: [80])));
        Assert.Throws<ScpDecodeException>(() => ScpDecoder.Decode(CreateScp(diskType: 0x30, fluxTicks: [80])));
    }

    [Fact]
    public void LoadAcceptsDirectScpWithOptions()
    {
        using var temp = new TempDiskFile(".scp");
        File.WriteAllBytes(temp.Path, CreateScp(fluxTicks: [80, 160], targetBitCells: 32));

        var result = AmigaDiskLoader.Load(
            temp.Path,
            new AmigaDiskLoadOptions { Scp = new ScpDecodeOptions { BitCellsPerRevolution = 32 } });

        var track = result.Media.ReadTrack(0, 0);
        Assert.Equal(32, track.BitLength);
        Assert.Equal(0xA0, track.EncodedData.Span[0]);
    }

    [Fact]
    public void LoadAcceptsZipWrappedScp()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.scp", CreateScp(fluxTicks: [80], targetBitCells: 32)));

        var result = AmigaDiskLoader.Load(
            temp.Path,
            new AmigaDiskLoadOptions { Scp = new ScpDecodeOptions { BitCellsPerRevolution = 32 } });

        Assert.Equal("disk.scp", result.DisplayName);
        Assert.Equal(32, result.Media.ReadTrack(0, 0).BitLength);
    }

    private static byte[] CreateScp(
        byte flags = 0x01,
        byte diskType = 0x04,
        ushort[]? fluxTicks = null,
        int targetBitCells = ScpDecodeOptions.AmigaDoubleDensityBitCellsPerRevolution)
    {
        fluxTicks ??= [80];
        const int TrackOffset = 0x2B0;
        const int TrackHeaderLength = 16;
        var image = new byte[TrackOffset + TrackHeaderLength + (fluxTicks.Length * 2)];
        image[0] = (byte)'S';
        image[1] = (byte)'C';
        image[2] = (byte)'P';
        image[3] = 0x25;
        image[4] = diskType;
        image[5] = 1;
        image[6] = 0;
        image[7] = 0;
        image[8] = flags;
        image[9] = 0;
        image[0x0A] = 0;
        image[0x0B] = 0;
        WriteUInt32Little(image, 0x10, TrackOffset);
        image[TrackOffset] = (byte)'T';
        image[TrackOffset + 1] = (byte)'R';
        image[TrackOffset + 2] = (byte)'K';
        image[TrackOffset + 3] = 0;
        WriteUInt32Little(image, TrackOffset + 4, checked((uint)(targetBitCells * 80)));
        WriteUInt32Little(image, TrackOffset + 8, (uint)fluxTicks.Length);
        WriteUInt32Little(image, TrackOffset + 12, TrackHeaderLength);
        for (var index = 0; index < fluxTicks.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(TrackOffset + TrackHeaderLength + (index * 2), 2), fluxTicks[index]);
        }

        return image;
    }

    private static void WriteUInt32Little(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
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
