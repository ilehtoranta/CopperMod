using System.IO.Compression;
using CopperDisk;

namespace CopperDisk.Tests;

public sealed class AmigaDiskLoaderTests
{
    [Fact]
    public void FromAdfBytesExposesSectorDataAndEncodedTracks()
    {
        var data = CreateStandardAdf();
        data[0] = (byte)'D';
        data[1] = (byte)'O';
        data[2] = (byte)'S';
        var expectedOffset = (((3 * AmigaDiskGeometry.HeadCount) + 1) * AmigaDiskGeometry.SectorsPerTrack + 7) * AmigaDiskGeometry.SectorSize;
        data[expectedOffset] = 0x42;
        data[expectedOffset + 511] = 0x99;

        var media = AmigaDiskLoader.FromAdfBytes(data);
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.Equal(AmigaDiskGeometry.CylinderCount, media.Cylinders);
        Assert.Equal(AmigaDiskGeometry.HeadCount, media.Heads);
        Assert.True(sectorMedia.HasCompleteSectorData);
        Assert.Equal((byte)'D', sectorMedia.BootBlock.Span[0]);
        var sector = sectorMedia.ReadSector(3, 1, 7).Span;
        Assert.Equal(0x42, sector[0]);
        Assert.Equal(0x99, sector[^1]);

        var track = Assert.IsType<AmigaEncodedTrack>(media.ReadTrack(0, 0));
        Assert.Equal(AmigaDosTrackEncoder.EncodedTrackBytes * 8, track.BitLength);
        Assert.Equal(0, track.StartBit);
        Assert.Equal(AmigaTrackFeatures.None, track.Features);
        Assert.True(ContainsWord(track, 0x4489));
    }

    [Fact]
    public void FromAdfBytesRejectsNonStandardSize()
    {
        Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.FromAdfBytes(new byte[1]));
    }

    [Fact]
    public void FromEncodedTracksPreservesTrackDataAndDecodesKnownSectorsBestEffort()
    {
        var data = CreateStandardAdf();
        var sectorOffset = AmigaDiskGeometry.SectorSize;
        data[sectorOffset] = 0x5A;
        data[sectorOffset + 511] = 0xA5;
        var source = AmigaDiskLoader.FromAdfBytes(data);
        var tracks = CreateUnformattedTrackSet();
        tracks[0] = Assert.IsType<AmigaEncodedTrack>(source.ReadTrack(0, 0));

        var media = AmigaDiskLoader.FromEncodedTracks(tracks);
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.False(sectorMedia.HasCompleteSectorData);
        var preservedTrack = media.ReadTrack(0, 0);
        Assert.True((preservedTrack.Features & AmigaTrackFeatures.PreservedTrackData) != 0);
        var decoded = sectorMedia.ReadSector(0, 0, 1).Span;
        Assert.Equal(0x5A, decoded[0]);
        Assert.Equal(0xA5, decoded[^1]);
    }

    [Fact]
    public void FromEncodedTracksRequiresFullTrackSet()
    {
        var tracks = new[] { AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack()) };

        Assert.Throws<ArgumentException>(() => AmigaDiskLoader.FromEncodedTracks(tracks));
    }

    [Fact]
    public void AdfWritableTrackWriteDecodesSectorsAndInvalidatesCachedTrack()
    {
        var target = AmigaDiskLoader.FromAdfBytes(CreateStandardAdf());
        var targetSectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(target);
        var writable = Assert.IsAssignableFrom<IWritableAmigaDiskMedia>(target);
        var cachedTrack = target.ReadTrack(0, 0).Data.ToArray();
        var sourceData = CreateStandardAdf();
        sourceData[3 * AmigaDiskGeometry.SectorSize] = 0x4C;
        sourceData[(3 * AmigaDiskGeometry.SectorSize) + 511] = 0xA7;
        var source = AmigaDiskLoader.FromAdfBytes(sourceData);

        Assert.True(writable.TryWriteTrack(0, 0, source.ReadTrack(0, 0)));

        Assert.True(writable.IsDirty);
        var sector = targetSectorMedia.ReadSector(0, 0, 3).Span;
        Assert.Equal(0x4C, sector[0]);
        Assert.Equal(0xA7, sector[^1]);
        Assert.NotEqual(cachedTrack, target.ReadTrack(0, 0).Data.ToArray());
    }

    [Fact]
    public void AdfWritableTrackWritePreservesAllSectorsAndRegeneratesCanonicalTrack()
    {
        var target = AmigaDiskLoader.FromAdfBytes(CreateStandardAdf());
        var targetSectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(target);
        var writable = Assert.IsAssignableFrom<IWritableAmigaDiskMedia>(target);
        var sourceData = CreateStandardAdf();
        FillTrackPattern(sourceData, cylinder: 7, head: 1);
        var source = AmigaDiskLoader.FromAdfBytes(sourceData);
        var sourceTrack = source.ReadTrack(7, 1);

        Assert.True(writable.TryWriteTrack(7, 1, sourceTrack));

        for (var sector = 0; sector < AmigaDiskGeometry.SectorsPerTrack; sector++)
        {
            var expected = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(source).ReadSector(7, 1, sector).ToArray();
            var actual = targetSectorMedia.ReadSector(7, 1, sector).ToArray();
            Assert.Equal(expected, actual);
        }

        var expectedTrack = AmigaDiskLoader.FromAdfBytes(targetSectorMedia.Data.ToArray()).ReadTrack(7, 1);
        var actualTrack = target.ReadTrack(7, 1);
        Assert.Equal(expectedTrack.BitLength, actualTrack.BitLength);
        Assert.Equal(expectedTrack.Data.ToArray(), actualTrack.Data.ToArray());
    }

    [Fact]
    public void AdfWritableTrackWriteRejectsInvalidTrackWithoutCorruptingSectors()
    {
        var data = CreateStandardAdf();
        data[AmigaDiskGeometry.SectorSize] = 0x77;
        var target = AmigaDiskLoader.FromAdfBytes(data);
        var targetSectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(target);
        var writable = Assert.IsAssignableFrom<IWritableAmigaDiskMedia>(target);
        var invalidTrack = AmigaEncodedTrack.FromBytes(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA });

        Assert.False(writable.TryWriteTrack(0, 0, invalidTrack));

        Assert.False(writable.IsDirty);
        Assert.Equal(0x77, targetSectorMedia.ReadSector(0, 0, 1).Span[0]);
    }

    [Fact]
    public void TrackBackedMediaIsReadOnly()
    {
        var source = AmigaDiskLoader.FromAdfBytes(CreateStandardAdf());
        var tracks = CreateUnformattedTrackSet();
        tracks[0] = Assert.IsType<AmigaEncodedTrack>(source.ReadTrack(0, 0));

        var media = AmigaDiskLoader.FromEncodedTracks(tracks);

        Assert.False(media is IWritableAmigaDiskMedia);
    }

    [Fact]
    public void ReadTrackAndSectorValidateBounds()
    {
        var media = AmigaDiskLoader.FromAdfBytes(CreateStandardAdf());
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.Throws<ArgumentOutOfRangeException>(() => media.ReadTrack(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => media.ReadTrack(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => sectorMedia.ReadSector(0, 0, 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => sectorMedia.ReadBytes(-1, 1));
    }

    [Fact]
    public void LoadAcceptsZipWithSingleDiskImage()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.adf", CreateStandardAdf()));

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.Equal("disk.adf", result.DisplayName);
        Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(result.Media);
    }

    [Fact]
    public void LoadRejectsZipWithMultipleDiskImages()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(
            temp.Path,
            ("disk1.adf", CreateStandardAdf()),
            ("disk2.adf", CreateStandardAdf()));

        Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.Load(temp.Path));
    }

    [Fact]
    public void LoadRejectsUnsupportedExtension()
    {
        using var temp = new TempDiskFile(".bin");
        File.WriteAllBytes(temp.Path, Array.Empty<byte>());

        Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.Load(temp.Path));
    }

    private static byte[] CreateStandardAdf()
    {
        return new byte[AmigaDiskGeometry.StandardAdfSize];
    }

    private static void FillTrackPattern(byte[] data, int cylinder, int head)
    {
        for (var sector = 0; sector < AmigaDiskGeometry.SectorsPerTrack; sector++)
        {
            var logicalSector = ((cylinder * AmigaDiskGeometry.HeadCount) + head) * AmigaDiskGeometry.SectorsPerTrack + sector;
            var offset = logicalSector * AmigaDiskGeometry.SectorSize;
            for (var index = 0; index < AmigaDiskGeometry.SectorSize; index++)
            {
                data[offset + index] = (byte)((sector * 17 + index * 3 + cylinder + head) & 0xFF);
            }
        }
    }

    private static AmigaEncodedTrack[] CreateUnformattedTrackSet()
    {
        var tracks = new AmigaEncodedTrack[AmigaDiskGeometry.TrackCount];
        var blank = AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack());
        for (var index = 0; index < tracks.Length; index++)
        {
            tracks[index] = blank;
        }

        return tracks;
    }

    private static bool ContainsWord(AmigaEncodedTrack track, ushort expected)
    {
        for (var offset = 0; offset < track.BitLength; offset++)
        {
            if (track.ReadUInt16(offset) == expected)
            {
                return true;
            }
        }

        return false;
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
