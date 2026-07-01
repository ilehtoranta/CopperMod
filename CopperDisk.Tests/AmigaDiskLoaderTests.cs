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
        Assert.IsAssignableFrom<IWritableAmigaSectorDiskMedia>(media);

        Assert.Equal(AmigaDiskGeometry.CylinderCount, media.Cylinders);
        Assert.Equal(AmigaDiskGeometry.HeadCount, media.Heads);
        Assert.True(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal((byte)'D', sectorMedia.BootBlock.Span[0]);
        var sector = sectorMedia.ReadSector(3, 1, 7).Span;
        Assert.Equal(0x42, sector[0]);
        Assert.Equal(0x99, sector[^1]);

        var track = Assert.IsType<AmigaEncodedTrack>(media.ReadTrack(0, 0));
        Assert.Equal(AmigaDosTrackEncoder.EncodedTrackByteCount * 8, track.BitLength);
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
    public void StandardAdfTrackEncoderUsesPalSizedSyntheticRevolution()
    {
        const int expectedSectorBytes = 0x440 * AmigaDiskGeometry.SectorsPerTrack;
        const int expectedGapBytes = 0x2BC;
        const int expectedTrackBytes = expectedSectorBytes + expectedGapBytes;
        var media = AmigaDiskLoader.FromAdfBytes(CreateStandardAdf());

        var encoded = AmigaDosTrackEncoder.EncodeTrack(media, cylinder: 0, head: 0);
        var track = AmigaEncodedTrack.FromBytes(encoded);

        Assert.Equal(12_668, AmigaDosTrackEncoder.EncodedTrackByteCount);
        Assert.Equal(expectedTrackBytes, encoded.Length);
        Assert.Equal(101_344, track.BitLength);
        Assert.Equal(0x44, encoded[4]);
        Assert.Equal(0x89, encoded[5]);
        Assert.Equal(0x44, encoded[expectedSectorBytes - 0x440 + 4]);
        Assert.Equal(0x89, encoded[expectedSectorBytes - 0x440 + 5]);
        for (var offset = expectedSectorBytes; offset < encoded.Length; offset++)
        {
            Assert.Equal(0xAA, encoded[offset]);
        }
    }

    [Fact]
    public void EncodedTrackReadsAcrossByteBoundariesAndWrapsAtBitLength()
    {
        var track = new AmigaEncodedTrack(new byte[] { 0x12, 0x34, 0x50 }, 20);
        var fullTrack = new AmigaEncodedTrack(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x90 }, 40);

        Assert.Equal(0x12, track.ReadByteAtBit(0));
        Assert.Equal(0x2345, track.ReadUInt16AtBit(4));
        Assert.Equal(0x5123, track.ReadUInt16AtBit(16));
        Assert.Equal(0x2345u, track.ReadUInt32AtBit(4) >> 16);
        Assert.Equal(0x23456789u, fullTrack.ReadUInt32AtBit(4));
    }

    [Fact]
    public void UnformattedTrackUsesPalSizedSyntheticRevolution()
    {
        var track = AmigaDosTrackEncoder.CreateUnformattedTrack();

        Assert.Equal(12_668, track.Length);
        foreach (var value in track)
        {
            Assert.Equal(0xAA, value);
        }
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

        Assert.False(sectorMedia.HasCompleteDecodedSectorData);
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
        var cachedTrack = target.ReadTrack(0, 0).EncodedData.ToArray();
        var sourceData = CreateStandardAdf();
        sourceData[3 * AmigaDiskGeometry.SectorSize] = 0x4C;
        sourceData[(3 * AmigaDiskGeometry.SectorSize) + 511] = 0xA7;
        var source = AmigaDiskLoader.FromAdfBytes(sourceData);

        Assert.True(writable.TryWriteTrack(0, 0, source.ReadTrack(0, 0)));

        Assert.True(writable.IsDirty);
        var sector = targetSectorMedia.ReadSector(0, 0, 3).Span;
        Assert.Equal(0x4C, sector[0]);
        Assert.Equal(0xA7, sector[^1]);
        Assert.NotEqual(cachedTrack, target.ReadTrack(0, 0).EncodedData.ToArray());
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

        var expectedTrack = AmigaDiskLoader.FromAdfBytes(targetSectorMedia.SectorData.ToArray()).ReadTrack(7, 1);
        var actualTrack = target.ReadTrack(7, 1);
        Assert.Equal(expectedTrack.BitLength, actualTrack.BitLength);
        Assert.Equal(expectedTrack.EncodedData.ToArray(), actualTrack.EncodedData.ToArray());
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
    public void FromEncodedTrackBytesTreatsNullEntriesAsUnformattedTracks()
    {
        var tracks = new byte[]?[AmigaDiskGeometry.TrackCount];

        var media = AmigaDiskLoader.FromEncodedTrackBytes(tracks);
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.False(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal(
            AmigaDosTrackEncoder.CreateUnformattedTrack(),
            media.ReadTrack(0, 0).EncodedData.ToArray());
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
    public void LoadAcceptsStandardAdfAsWritableMedia()
    {
        using var temp = new TempDiskFile(".adf");
        File.WriteAllBytes(temp.Path, CreateStandardAdf());

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.IsAssignableFrom<IWritableAmigaSectorDiskMedia>(result.Media);
        Assert.False(result.Media is IAmigaPreservedTrackDiskMedia);
    }

    [Fact]
    public void FromExtendedAdfBytesLoadsNormalTracksAsReadOnlyPreservedMedia()
    {
        var data = CreateStandardAdf();
        data[0] = (byte)'D';
        data[1] = (byte)'O';
        data[2] = (byte)'S';
        data[AmigaDiskGeometry.SectorSize] = 0x63;
        FillTrackPattern(data, cylinder: 12, head: 1);

        var media = AmigaDiskLoader.FromExtendedAdfBytes(CreateExtendedAdf(data));
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.False(media is IWritableAmigaDiskMedia);
        Assert.True(media is IAmigaPreservedTrackDiskMedia);
        Assert.True(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal((byte)'D', sectorMedia.BootBlock.Span[0]);
        Assert.Equal(0x63, sectorMedia.ReadSector(1).Span[0]);
        Assert.Equal(data[((12 * AmigaDiskGeometry.HeadCount + 1) * AmigaDiskGeometry.SectorsPerTrack + 4) * AmigaDiskGeometry.SectorSize],
            sectorMedia.ReadSector(12, 1, 4).Span[0]);
        var track = Assert.IsType<AmigaEncodedTrack>(media.ReadTrack(0, 0));
        Assert.True((track.Features & AmigaTrackFeatures.PreservedTrackData) != 0);
        Assert.True(ContainsWord(track, 0x4489));
    }

    [Fact]
    public void FromExtendedAdfBytesPreservesRawMfmTrackBytesAndBitLength()
    {
        var raw = new byte[] { 0x44, 0x89, 0xA0, 0x00 };

        var media = AmigaDiskLoader.FromExtendedAdfBytes(CreateExtendedAdf(
            overrides: new ExtendedAdfTrackOverride(0, 1, raw, BitLength: 20)));
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);
        var track = Assert.IsType<AmigaEncodedTrack>(media.ReadTrack(0, 0));

        Assert.True(media is IAmigaPreservedTrackDiskMedia);
        Assert.False(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal(20, track.BitLength);
        Assert.Equal(raw, track.EncodedData.ToArray());
        Assert.True((track.Features & AmigaTrackFeatures.PreservedTrackData) != 0);
        Assert.Equal(0x4489, track.ReadUInt16AtBit(0));
    }

    [Fact]
    public void FromExtendedAdfBytesLoadsMixedTracksAsReadOnlyPreservedMedia()
    {
        var data = CreateStandardAdf();
        data[AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize] = 0x7E;
        var raw = new byte[] { 0x12, 0x34, 0x50, 0x00 };

        var media = AmigaDiskLoader.FromExtendedAdfBytes(CreateExtendedAdf(
            data,
            new ExtendedAdfTrackOverride(0, 1, raw, BitLength: 20)));
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.False(media is IWritableAmigaDiskMedia);
        Assert.True(media is IAmigaPreservedTrackDiskMedia);
        Assert.False(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal(0x7E, sectorMedia.ReadSector(0, 1, 0).Span[0]);
    }

    [Fact]
    public void LoadRoutesExtendedAdfByMagic()
    {
        using var temp = new TempDiskFile(".adf");
        File.WriteAllBytes(temp.Path, CreateExtendedAdf());

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.False(result.Media is IWritableAmigaDiskMedia);
        Assert.True(result.Media is IAmigaPreservedTrackDiskMedia);
    }

    [Fact]
    public void FromAdzBytesRoutesExtendedAdfByMagic()
    {
        var media = AmigaDiskLoader.FromAdzBytes(Gzip(CreateExtendedAdf()));

        Assert.False(media is IWritableAmigaDiskMedia);
        Assert.True(media is IAmigaPreservedTrackDiskMedia);
    }

    [Fact]
    public void LoadAcceptsZipWithSingleExtendedAdfImage()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.adf", CreateExtendedAdf()));

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.Equal("disk.adf", result.DisplayName);
        Assert.True(result.Media is IAmigaPreservedTrackDiskMedia);
    }

    [Fact]
    public void LoadAcceptsZipWithSingleExtendedAdzImage()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.adz", Gzip(CreateExtendedAdf())));

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.Equal("disk.adz", result.DisplayName);
        Assert.True(result.Media is IAmigaPreservedTrackDiskMedia);
    }

    [Theory]
    [InlineData(ExtendedAdfInvalidKind.OldFormat)]
    [InlineData(ExtendedAdfInvalidKind.UnsupportedTrackType)]
    [InlineData(ExtendedAdfInvalidKind.Multirevolution)]
    [InlineData(ExtendedAdfInvalidKind.WrongTrackCount)]
    [InlineData(ExtendedAdfInvalidKind.BadNormalTrackSize)]
    [InlineData(ExtendedAdfInvalidKind.InvalidBitLength)]
    [InlineData(ExtendedAdfInvalidKind.HighDensity)]
    [InlineData(ExtendedAdfInvalidKind.TruncatedData)]
    [InlineData(ExtendedAdfInvalidKind.TrailingData)]
    public void FromExtendedAdfBytesRejectsUnsupportedOrDamagedImages(ExtendedAdfInvalidKind kind)
    {
        var image = kind switch
        {
            ExtendedAdfInvalidKind.OldFormat => CreateOldExtendedAdf(),
            ExtendedAdfInvalidKind.UnsupportedTrackType => CreateExtendedAdf(overrides: new ExtendedAdfTrackOverride(0, 2, [0, 0], 16)),
            ExtendedAdfInvalidKind.Multirevolution => CreateExtendedAdf(overrides: new ExtendedAdfTrackOverride(0, 1, [0, 0], 16, RevolutionMinusOne: 1)),
            ExtendedAdfInvalidKind.WrongTrackCount => CreateExtendedAdf(trackCount: AmigaDiskGeometry.TrackCount - 1),
            ExtendedAdfInvalidKind.BadNormalTrackSize => CreateExtendedAdf(overrides: new ExtendedAdfTrackOverride(0, 0, [0, 0], 0)),
            ExtendedAdfInvalidKind.InvalidBitLength => CreateExtendedAdf(overrides: new ExtendedAdfTrackOverride(0, 1, [0, 0], 17)),
            ExtendedAdfInvalidKind.HighDensity => CreateExtendedAdf(overrides: new ExtendedAdfTrackOverride(0, 1, new byte[20_002], 16)),
            ExtendedAdfInvalidKind.TruncatedData => CreateExtendedAdf()[..^1],
            ExtendedAdfInvalidKind.TrailingData => [.. CreateExtendedAdf(), 0xAA],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.FromExtendedAdfBytes(image));
    }

    [Fact]
    public void FromAdzBytesLoadsReadOnlySectorMedia()
    {
        var data = CreateStandardAdf();
        data[0] = (byte)'D';
        data[1] = (byte)'O';
        data[2] = (byte)'S';
        data[AmigaDiskGeometry.SectorSize] = 0x52;
        var adz = Gzip(data);

        var media = AmigaDiskLoader.FromAdzBytes(adz);
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.False(media is IWritableAmigaDiskMedia);
        Assert.True(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal((byte)'D', sectorMedia.BootBlock.Span[0]);
        Assert.Equal(0x52, sectorMedia.ReadSector(1).Span[0]);
        Assert.True(ContainsWord(Assert.IsType<AmigaEncodedTrack>(media.ReadTrack(0, 0)), 0x4489));
    }

    [Fact]
    public void FromAdzBytesRejectsMalformedGzip()
    {
        Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.FromAdzBytes([1, 2, 3]));
    }

    [Fact]
    public void FromAdzBytesRejectsWrongSize()
    {
        var ex = Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.FromAdzBytes(Gzip([1, 2, 3])));

        Assert.Contains("standard", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromDmsBytesLoadsReadOnlySectorMedia()
    {
        var data = CreateStandardAdf();
        data[0] = (byte)'D';
        data[1] = (byte)'O';
        data[2] = (byte)'S';
        data[AmigaDiskGeometry.SectorSize * 17] = 0xA9;
        FillTrackPattern(data, cylinder: 7, head: 1);
        var dms = CreateDms(data, mode: 0);

        var media = AmigaDiskLoader.FromDmsBytes(dms);
        var sectorMedia = Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);

        Assert.False(media is IWritableAmigaDiskMedia);
        Assert.True(sectorMedia.HasCompleteDecodedSectorData);
        Assert.Equal((byte)'D', sectorMedia.BootBlock.Span[0]);
        Assert.Equal(0xA9, sectorMedia.ReadSector(17).Span[0]);
        Assert.Equal(data[((7 * AmigaDiskGeometry.HeadCount + 1) * AmigaDiskGeometry.SectorsPerTrack + 3) * AmigaDiskGeometry.SectorSize],
            sectorMedia.ReadSector(7, 1, 3).Span[0]);
        Assert.True(ContainsWord(Assert.IsType<AmigaEncodedTrack>(media.ReadTrack(0, 0)), 0x4489));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void FromDmsBytesSupportsCompressionModes(byte mode)
    {
        var media = AmigaDiskLoader.FromDmsBytes(CreateDms(CreateStandardAdf(), mode));

        Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(media);
        Assert.False(media is IWritableAmigaDiskMedia);
    }

    [Theory]
    [InlineData(DmsInvalidKind.Encrypted)]
    [InlineData(DmsInvalidKind.BadTrackDataCrc)]
    [InlineData(DmsInvalidKind.UnknownMode)]
    [InlineData(DmsInvalidKind.Fms)]
    [InlineData(DmsInvalidKind.HighDensity)]
    [InlineData(DmsInvalidKind.Incomplete)]
    public void FromDmsBytesRejectsUnsupportedOrDamagedImages(DmsInvalidKind kind)
    {
        var dms = kind switch
        {
            DmsInvalidKind.Encrypted => CreateDmsHeader(generalInfo: 0x02),
            DmsInvalidKind.UnknownMode => CreateDmsHeader(commonMode: 7),
            DmsInvalidKind.Fms => CreateDmsHeader(diskType: 7),
            DmsInvalidKind.HighDensity => CreateDmsHeader(generalInfo: 0x10),
            DmsInvalidKind.Incomplete => CreateDms(CreateStandardAdf(), mode: 0, cylinderCount: AmigaDiskGeometry.CylinderCount - 1),
            DmsInvalidKind.BadTrackDataCrc => CreateDmsWithCorruptTrackData(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        Assert.Throws<AmigaDiskException>(() => AmigaDiskLoader.FromDmsBytes(dms));
    }

    [Fact]
    public void LoadAcceptsZipWithSingleAdzImage()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.adz", Gzip(CreateStandardAdf())));

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.Equal("disk.adz", result.DisplayName);
        Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(result.Media);
        Assert.False(result.Media is IWritableAmigaDiskMedia);
    }

    [Fact]
    public void LoadAcceptsZipWithSingleDmsImage()
    {
        using var temp = new TempDiskFile(".zip");
        WriteZip(temp.Path, ("disk.dms", CreateDms(CreateStandardAdf(), mode: 0)));

        var result = AmigaDiskLoader.Load(temp.Path);

        Assert.Equal("disk.dms", result.DisplayName);
        Assert.IsAssignableFrom<IAmigaSectorDiskMedia>(result.Media);
        Assert.False(result.Media is IWritableAmigaDiskMedia);
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
            if (track.ReadUInt16AtBit(offset) == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] CreateExtendedAdf(
        byte[]? adf = null,
        ExtendedAdfTrackOverride? overrides = null,
        int trackCount = AmigaDiskGeometry.TrackCount)
    {
        return CreateExtendedAdf(adf, trackCount, overrides.HasValue ? [overrides.Value] : []);
    }

    private static byte[] CreateExtendedAdf(
        byte[]? adf,
        int trackCount,
        params ExtendedAdfTrackOverride[] overrides)
    {
        adf ??= CreateStandardAdf();
        var overrideByTrack = overrides.ToDictionary(item => item.Track);
        var trackData = new byte[trackCount][];
        var trackTypes = new byte[trackCount];
        var trackRevolutions = new byte[trackCount];
        var trackBitLengths = new int[trackCount];
        var totalLength = ExtendedAdfHeaderLength + (trackCount * ExtendedAdfTrackHeaderLength);
        for (var track = 0; track < trackCount; track++)
        {
            if (overrideByTrack.TryGetValue(track, out var trackOverride))
            {
                trackData[track] = trackOverride.Data;
                trackTypes[track] = trackOverride.Type;
                trackRevolutions[track] = trackOverride.RevolutionMinusOne;
                trackBitLengths[track] = trackOverride.BitLength;
            }
            else
            {
                trackData[track] = adf.AsSpan(track * ExtendedAdfNormalTrackBytes, ExtendedAdfNormalTrackBytes).ToArray();
                trackTypes[track] = 0;
                trackBitLengths[track] = 0;
            }

            totalLength += trackData[track].Length;
        }

        var image = new byte[totalLength];
        "UAE-1ADF"u8.CopyTo(image);
        WriteUInt16(image, 10, trackCount);
        var dataOffset = ExtendedAdfHeaderLength + (trackCount * ExtendedAdfTrackHeaderLength);
        for (var track = 0; track < trackCount; track++)
        {
            var headerOffset = ExtendedAdfHeaderLength + (track * ExtendedAdfTrackHeaderLength);
            WriteUInt16(image, headerOffset + 2, (trackRevolutions[track] << 8) | trackTypes[track]);
            WriteUInt32(image, headerOffset + 4, trackData[track].Length);
            WriteUInt32(image, headerOffset + 8, trackBitLengths[track]);
            trackData[track].CopyTo(image.AsSpan(dataOffset));
            dataOffset += trackData[track].Length;
        }

        return image;
    }

    private static byte[] CreateOldExtendedAdf()
    {
        var image = new byte[12];
        "UAE--ADF"u8.CopyTo(image);
        return image;
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

    private static byte[] Gzip(byte[] data)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return memory.ToArray();
    }

    private static byte[] CreateDms(byte[] adf, byte mode, int cylinderCount = AmigaDiskGeometry.CylinderCount)
    {
        var body = new List<byte>();
        for (var cylinder = 0; cylinder < cylinderCount; cylinder++)
        {
            var cylinderBytes = adf.AsSpan(cylinder * DmsCylinderBytes, DmsCylinderBytes).ToArray();
            var packed = PackDmsCylinder(cylinderBytes, mode, out var firstUnpackedLength, out var flags);
            var trackHeader = new byte[20];
            trackHeader[0] = (byte)'T';
            trackHeader[1] = (byte)'R';
            WriteUInt16(trackHeader, 2, (ushort)cylinder);
            WriteUInt16(trackHeader, 6, (ushort)packed.Length);
            WriteUInt16(trackHeader, 8, firstUnpackedLength);
            WriteUInt16(trackHeader, 10, (ushort)cylinderBytes.Length);
            trackHeader[12] = flags;
            trackHeader[13] = mode;
            WriteUInt16(trackHeader, 14, Checksum(cylinderBytes));
            WriteUInt16(trackHeader, 16, Crc(packed));
            WriteUInt16(trackHeader, 18, Crc(trackHeader.AsSpan(0, 18)));
            body.AddRange(trackHeader);
            body.AddRange(packed);
        }

        var header = CreateDmsHeader(commonMode: mode, packedDataSize: body.Count);
        var image = new byte[header.Length + body.Count];
        header.CopyTo(image, 0);
        body.CopyTo(image, header.Length);
        return image;
    }

    private static byte[] CreateDmsWithCorruptTrackData()
    {
        var dms = CreateDms(CreateStandardAdf(), mode: 0);
        dms[56 + 20] ^= 0x5A;
        return dms;
    }

    private static byte[] CreateDmsHeader(
        ushort generalInfo = 0,
        ushort diskType = 0,
        ushort commonMode = 0,
        int unpackedSize = AmigaDiskGeometry.StandardAdfSize,
        int packedDataSize = 0)
    {
        var header = new byte[56];
        header[0] = (byte)'D';
        header[1] = (byte)'M';
        header[2] = (byte)'S';
        header[3] = (byte)'!';
        WriteUInt16(header, 10, generalInfo);
        WriteUInt16(header, 16, 0);
        WriteUInt16(header, 18, AmigaDiskGeometry.CylinderCount - 1);
        WriteUInt24(header, 21, packedDataSize);
        WriteUInt24(header, 25, unpackedSize);
        WriteUInt16(header, 46, 111);
        WriteUInt16(header, 50, diskType);
        WriteUInt16(header, 52, commonMode);
        WriteUInt16(header, 54, Crc(header.AsSpan(4, 50)));
        return header;
    }

    private static byte[] PackDmsCylinder(byte[] cylinderBytes, byte mode, out ushort firstUnpackedLength, out byte flags)
    {
        flags = 0;
        if (mode == 0)
        {
            firstUnpackedLength = (ushort)cylinderBytes.Length;
            return cylinderBytes;
        }

        if (mode is >= 1 and <= 4)
        {
            var rle = EncodeRle(cylinderBytes);
            firstUnpackedLength = (ushort)rle.Length;
            return mode switch
            {
                1 => rle,
                2 => EncodeLiteralBitStream(rle),
                3 => EncodeLiteralBitStream(rle),
                4 => EncodeDeepLiterals(rle),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        if (mode is 5 or 6)
        {
            if (cylinderBytes.Any(value => value != cylinderBytes[0]))
            {
                throw new ArgumentException("Heavy DMS test packing only supports constant tracks.", nameof(cylinderBytes));
            }

            flags = 2;
            firstUnpackedLength = (ushort)cylinderBytes.Length;
            return EncodeHeavyConstant(cylinderBytes[0]);
        }

        throw new ArgumentOutOfRangeException(nameof(mode));
    }

    private static byte[] EncodeRle(byte[] data)
    {
        if (data.All(value => value == data[0]) && data.Length <= ushort.MaxValue)
        {
            return [(byte)0x90, 0xFF, data[0], (byte)(data.Length >> 8), (byte)data.Length];
        }

        var encoded = new List<byte>(data.Length);
        foreach (var value in data)
        {
            if (value == 0x90)
            {
                encoded.Add(0x90);
                encoded.Add(0);
            }
            else
            {
                encoded.Add(value);
            }
        }

        return encoded.ToArray();
    }

    private static byte[] EncodeLiteralBitStream(byte[] data)
    {
        var writer = new BitWriter();
        foreach (var value in data)
        {
            writer.WriteBits(1, 1);
            writer.WriteBits(value, 8);
        }

        return writer.ToArray();
    }

    private static byte[] EncodeHeavyConstant(byte value)
    {
        var writer = new BitWriter();
        writer.WriteBits(0, 9);
        writer.WriteBits(value, 9);
        writer.WriteBits(0, 5);
        writer.WriteBits(0, 5);
        return writer.ToArray();
    }

    private static byte[] EncodeDeepLiterals(byte[] data)
    {
        var encoder = new DeepLiteralEncoder();
        var writer = new BitWriter();
        foreach (var value in data)
        {
            encoder.WriteLiteral(writer, value);
        }

        return writer.ToArray();
    }

    private static ushort Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
            }
        }

        return (ushort)crc;
    }

    private static ushort Checksum(ReadOnlySpan<byte> data)
    {
        var checksum = 0;
        foreach (var value in data)
        {
            checksum = (checksum + value) & 0xFFFF;
        }

        return (ushort)checksum;
    }

    private static void WriteUInt16(Span<byte> data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void WriteUInt24(Span<byte> data, int offset, int value)
    {
        data[offset] = (byte)(value >> 16);
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)value;
    }

    private static void WriteUInt32(Span<byte> data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private const int ExtendedAdfHeaderLength = 12;
    private const int ExtendedAdfTrackHeaderLength = 12;
    private const int ExtendedAdfNormalTrackBytes = AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize;

    private const int DmsCylinderBytes = AmigaDiskGeometry.HeadCount * AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize;

    public enum ExtendedAdfInvalidKind
    {
        OldFormat,
        UnsupportedTrackType,
        Multirevolution,
        WrongTrackCount,
        BadNormalTrackSize,
        InvalidBitLength,
        HighDensity,
        TruncatedData,
        TrailingData
    }

    private readonly record struct ExtendedAdfTrackOverride(
        int Track,
        byte Type,
        byte[] Data,
        int BitLength,
        byte RevolutionMinusOne = 0);

    public enum DmsInvalidKind
    {
        Encrypted,
        BadTrackDataCrc,
        UnknownMode,
        Fms,
        HighDensity,
        Incomplete
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private byte _current;
        private int _bitCount;

        public void WriteBits(int value, int count)
        {
            for (var bit = count - 1; bit >= 0; bit--)
            {
                _current = (byte)((_current << 1) | ((value >> bit) & 1));
                _bitCount++;
                if (_bitCount == 8)
                {
                    _bytes.Add(_current);
                    _current = 0;
                    _bitCount = 0;
                }
            }
        }

        public byte[] ToArray()
        {
            if (_bitCount != 0)
            {
                _bytes.Add((byte)(_current << (8 - _bitCount)));
                _current = 0;
                _bitCount = 0;
            }

            return _bytes.ToArray();
        }
    }

    private sealed class DeepLiteralEncoder
    {
        private const int LookaheadSize = 60;
        private const int Threshold = 2;
        private const int CharCount = 256 - Threshold + LookaheadSize;
        private const int TableSize = (CharCount * 2) - 1;
        private const int Root = TableSize - 1;
        private const int MaxFrequency = 0x8000;

        private readonly ushort[] _frequency = new ushort[TableSize + 1];
        private readonly ushort[] _parent = new ushort[TableSize + CharCount];
        private readonly ushort[] _son = new ushort[TableSize];

        public DeepLiteralEncoder()
        {
            ushort i;
            for (i = 0; i < CharCount; i++)
            {
                _frequency[i] = 1;
                _son[i] = (ushort)(i + TableSize);
                _parent[i + TableSize] = i;
            }

            i = 0;
            var j = CharCount;
            while (j <= Root)
            {
                _frequency[j] = (ushort)(_frequency[i] + _frequency[i + 1]);
                _son[j] = i;
                _parent[i] = (ushort)j;
                _parent[i + 1] = (ushort)j;
                i += 2;
                j++;
            }

            _frequency[TableSize] = 0xFFFF;
            _parent[Root] = 0;
        }

        public void WriteLiteral(BitWriter writer, byte value)
        {
            var bits = new List<int>();
            if (!TryFindCode(_son[Root], value + TableSize, bits))
            {
                throw new InvalidOperationException("Unable to encode deep literal test value.");
            }

            foreach (var bit in bits)
            {
                writer.WriteBits(bit, 1);
            }

            Update(value);
        }

        private bool TryFindCode(int node, int targetLeaf, List<int> bits)
        {
            if (node >= TableSize)
            {
                return node == targetLeaf;
            }

            bits.Add(0);
            if (TryFindCode(_son[node], targetLeaf, bits))
            {
                return true;
            }

            bits[^1] = 1;
            if (TryFindCode(_son[node + 1], targetLeaf, bits))
            {
                return true;
            }

            bits.RemoveAt(bits.Count - 1);
            return false;
        }

        private void Update(ushort value)
        {
            if (_frequency[Root] == MaxFrequency)
            {
                throw new InvalidOperationException("The short DMS test stream should not need deep tree reconstruction.");
            }

            var node = _parent[value + TableSize];
            do
            {
                var frequency = ++_frequency[node];
                var next = node + 1;
                if (frequency > _frequency[next])
                {
                    do
                    {
                        next++;
                    }
                    while (frequency > _frequency[next]);
                    next--;
                    _frequency[node] = _frequency[next];
                    _frequency[next] = frequency;

                    var nodeSon = _son[node];
                    _parent[nodeSon] = (ushort)next;
                    if (nodeSon < TableSize)
                    {
                        _parent[nodeSon + 1] = (ushort)next;
                    }

                    var nextSon = _son[next];
                    _son[next] = nodeSon;

                    _parent[nextSon] = node;
                    if (nextSon < TableSize)
                    {
                        _parent[nextSon + 1] = node;
                    }

                    _son[node] = nextSon;
                    node = (ushort)next;
                }
            }
            while ((node = _parent[node]) != 0);
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
