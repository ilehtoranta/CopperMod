using System.Text;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsImageProfileTests
{
    private const int NativeImageSize = 0x24C;
    private readonly ITestOutputHelper _output;

    public NativeGraphicsImageProfileTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBootProfileCodeHasAuditedWordBranches(bool ntsc)
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out _, out var audit, defaultMonitorNtsc: ntsc);
        Assert.Equal(audit.BranchCount, audit.Patches.Count);
        Assert.All(audit.Patches, patch =>
        {
            Assert.InRange(patch.Displacement, (int)short.MinValue, (int)short.MaxValue);
            Assert.InRange(patch.TargetOffset, 0, code.Length - 2);
            Assert.Equal(0, patch.TargetOffset & 1);
        });
        Assert.All(audit.FragmentBoundaries, boundary =>
        {
            Assert.False(string.IsNullOrWhiteSpace(boundary.Name));
            Assert.InRange(boundary.Offset, 0, code.Length - 2);
            Assert.Equal(0, boundary.Offset & 1);
        });
        Assert.NotEmpty(audit.LocalFallbackOffsets);
        var branchTargets = audit.Patches.Select(patch => patch.TargetOffset).ToHashSet();
        Assert.All(audit.LocalFallbackOffsets, offset =>
        {
            Assert.InRange(offset, 0, code.Length - 2);
            Assert.Equal(0, offset & 1);
            Assert.Equal(0x4E75, (code[offset] << 8) | code[offset + 1]);
            Assert.Contains(offset, branchTargets);
        });
        _output.WriteLine($"native-profile-audit:ntsc={ntsc}:bytes={code.Length}:branches={audit.BranchCount}:relays={audit.RelayCount}:chains={audit.ChainCount}:boundaries={audit.FragmentBoundaryCount}");
        // Registered MNTR readback adds1062 bytes/74 branches;
        // the audit above independently proves every target and relay boundary.
        Assert.Equal((ntsc ? 1504272 : 1504372, 76374, 3428, 7, 3346),
            (code.Length, audit.BranchCount, audit.RelayCount, audit.ChainCount, audit.FragmentBoundaryCount));
        // Prove the convenience builders use the selected code stream as well
        // as the selected positive-image bytes. Fixed images publish entry
        // addresses; relocatable images additionally carry the complete stream.
        const uint codeAddress = 0x00400000;
        var image = NativeGraphicsLibraryImageBuilder.BuildFromRasterBodies(0x00800000,
            0x00A00000, codeAddress, codeAddress, NativeImageSize, Profile(ntsc));
        foreach (var entry in entries)
            Assert.Equal(codeAddress + (uint)entry.Value, Long(offset => image.VectorBytes[offset],
                NativeGraphicsLibraryImageBuilder.VectorTableSize + (int)entry.Key + 2));
        var hunk = NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(NativeImageSize, Profile(ntsc));
        Assert.True(hunk.Bytes.AsSpan().IndexOf(code.AsSpan()) >= 0,
            "The relocatable image did not contain the selected boot-profile code stream.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeProfileKeepsStringsOutsideGfxBaseAndPublishesDisplayFlags(bool ntsc)
    {
        const uint address = 0x00200000;
        var bytes = Create(address, NativeImageSize, NativeImageSize, Profile(ntsc));
        AssertNativeImage(offset => bytes[offset], address, ntsc);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FixedNativeProfilePublishesCoherentImageAndAutoinitSize(bool ntsc, bool rasterBodies)
    {
        const uint address = 0x00200000;
        var entries = Enum.GetValues<GraphicsLvo>().ToDictionary(vector => vector, _ => 0x00100000u);
        var image = rasterBodies
            ? NativeGraphicsLibraryImageBuilder.BuildFromRasterBodies(address, 0x00500000,
                0x00600000, 0x00600000, NativeImageSize, Profile(ntsc))
            : NativeGraphicsLibraryImageBuilder.Build(address, 0x00300000, 0x00100000,
                entries, 0x00100000, NativeImageSize, Profile(ntsc));
        Assert.Equal(NativeImageSize, image.PositiveBytes.Length);
        AssertNativeImage(offset => image.PositiveBytes[offset], address, ntsc);
        Assert.Equal((uint)NativeImageSize, Long(offset => image.ResidentBytes[offset], 0x50));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RelocatedNativeProfileRelocatesStringsWithoutChangingDisplayFlags(bool ntsc, bool rasterBodies)
    {
        var hunk = rasterBodies
            ? NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(NativeImageSize, Profile(ntsc))
            : NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 }, NativeImageSize, Profile(ntsc));
        var bus = new AmigaBus();
        var next = 0x00800000u;
        var loader = new AmigaHunkProgramLoader(bus, byteCount =>
        {
            var address = next;
            next += (uint)byteCount + 0x100;
            bus.MapWritableMemory(address, new byte[byteCount]);
            return address;
        });
        var program = loader.Load(hunk.Bytes);
        Assert.Equal(2, program.SegmentBases.Count);
        var address = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
        AssertNativeImage(offset => bus.ReadByte(address + (uint)offset), address, ntsc);
        Assert.Equal((uint)NativeImageSize, bus.ReadLong(program.SegmentBases[1] + 0x50));
        Assert.True(hunk.NativeCodeOffset >= NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeImageSize);
        var target = bus.ReadLong(address + unchecked((uint)(int)GraphicsLvo.InitRastPort) + 2);
        if (rasterBodies)
            Assert.True(target > program.SegmentBases[0] + (uint)hunk.NativeCodeOffset);
        else
            Assert.Equal(program.SegmentBases[0] + (uint)hunk.NativeCodeOffset, target);
    }

    [Fact]
    public void CompactProfilePreservesHistoricalStringOffsetsAndReportedSize()
    {
        var bytes = Create(0x200000, 0xF0, 0x7C, GraphicsLibraryImageProfile.CompactHost);
        Assert.Equal(0x2000A0u, Long(offset => bytes[offset], 0x0A));
        Assert.Equal(0x2000B4u, Long(offset => bytes[offset], 0x18));
        Assert.Equal((byte)0x7C, bytes[0x13]);
        Assert.Equal((byte)'g', bytes[0xA0]);
    }

    [Theory]
    [InlineData(0x220, 0x220)]
    [InlineData(0x24C, 0x220)]
    [InlineData(0x24C, 0x250)]
    public void NativeProfileRejectsInsufficientOrIncoherentAllocation(int length, int reported)
        => Assert.ThrowsAny<ArgumentException>(() => Create(0x200000, length, reported, Profile(false)));

    [Fact]
    public void NativeProfileRejectsWrappedImageExtent()
        => Assert.ThrowsAny<ArgumentException>(() => Create(0xFFFFFDC0, NativeImageSize, NativeImageSize, Profile(false)));

    [Fact]
    public void InvalidImageProfileIsRejected()
        => Assert.ThrowsAny<ArgumentException>(() => Create(0x200000, NativeImageSize, NativeImageSize, (GraphicsLibraryImageProfile)99));

    [Fact]
    public void NativeStringsCannotOverlapOrOutgrowTheReportedAllocation()
    {
        Assert.ThrowsAny<ArgumentException>(() => GraphicsLibraryImageLayout.CreateGuestImage(
            0x200000, NativeImageSize, 0, NativeImageSize, 40, 68,
            new string('N', 20), "id", Profile(false)));
        Assert.ThrowsAny<ArgumentException>(() => GraphicsLibraryImageLayout.CreateGuestImage(
            0x200000, 0x300, 0, NativeImageSize, 40, 68,
            "graphics.library", new string('I', 24), Profile(false)));
    }

    [Fact]
    public void NativeImageMayEndExactlyAtTheAddressSpaceBoundary()
    {
        const uint address = 0xFFFFFDB4;
        var bytes = Create(address, NativeImageSize, NativeImageSize, Profile(false));
        AssertNativeImage(offset => bytes[offset], address, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRuntimeDescriptorIsOptInInertAndOutsidePublicImage(bool ntsc)
    {
        const int size = 0x264;
        var plain = Create(0x200000, size, size, Profile(ntsc));
        var opted = GraphicsLibraryImageLayout.CreateGuestImage(0x200000, size, 0,
            size, 40, 68, "graphics.library", "graphics.library 40.68", Profile(ntsc),
            includeNativeRuntimeDescriptor: true);
        Assert.Equal(plain.AsSpan(0, 0x250).ToArray(), opted.AsSpan(0, 0x250).ToArray());
        Assert.All(plain.AsSpan(0x250).ToArray(), value => Assert.Equal((byte)0, value));
        Assert.Equal(0u, Long(i => opted[i], 0x250));
        Assert.Equal(1u, Long(i => opted[i], 0x254));
        Assert.Equal(0u, Long(i => opted[i], 0x258));
        Assert.Equal(0u, Long(i => opted[i], 0x25C));
        Assert.Equal(0u, Long(i => opted[i], 0x260));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeRuntimeDescriptorSurvivesFixedAndRelocatedBuilders(bool ntsc, bool rasterBodies)
    {
        const int size = 0x264;
        var entries = Enum.GetValues<GraphicsLvo>().ToDictionary(vector => vector, _ => 0x100000u);
        var fixedImage = rasterBodies
            ? NativeGraphicsLibraryImageBuilder.BuildFromRasterBodies(0x200000, 0x500000,
                0x600000, 0x600000, size, Profile(ntsc), true)
            : NativeGraphicsLibraryImageBuilder.Build(0x200000, 0x300000, 0x100000,
                entries, 0x100000, size, Profile(ntsc), true);
        AssertDescriptor(i => fixedImage.PositiveBytes[i]);
        Assert.Equal((uint)size, Long(i => fixedImage.ResidentBytes[i], 0x50));
        var hunk = rasterBodies
            ? NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, Profile(ntsc), true)
            : NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 }, size, Profile(ntsc), true);
        var bus = new AmigaBus();
        var next = 0x800000u;
        var loader = new AmigaHunkProgramLoader(bus, count =>
        {
            var result = next;
            next += (uint)count + 0x100;
            bus.MapWritableMemory(result, new byte[count]);
            return result;
        });
        var program = loader.Load(hunk.Bytes);
        var address = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
        AssertDescriptor(i => bus.ReadByte(address + (uint)i));
        Assert.Equal(address + 0x220, bus.ReadLong(address + 0x0A));
        Assert.Equal(address + 0x234, bus.ReadLong(address + 0x18));
        Assert.Equal((uint)size, bus.ReadLong(program.SegmentBases[1] + 0x50));
        Assert.True(hunk.NativeCodeOffset >= NativeGraphicsLibraryImageBuilder.VectorTableSize + size);

        static void AssertDescriptor(Func<int, byte> read)
        {
            Assert.Equal(0x264, (read(0x12) << 8) | read(0x13));
            Assert.Equal(0u, Long(read, 0x250));
            Assert.Equal(1u, Long(read, 0x254));
            Assert.Equal(0u, Long(read, 0x258));
            Assert.Equal(0u, Long(read, 0x25C));
            Assert.Equal(0u, Long(read, 0x260));
        }
    }

    [Fact]
    public void NativeRuntimeDescriptorRejectsCompactShortOrOverlappingImages()
    {
        foreach (var profile in new[] { GraphicsLibraryImageProfile.CompactHost, Profile(false) })
        {
            var size = profile == GraphicsLibraryImageProfile.CompactHost ? 0x264 : 0x24C;
            Assert.Throws<ArgumentException>(() => GraphicsLibraryImageLayout.CreateGuestImage(
                0x200000, size, 0, size, 40, 68, "graphics.library", "id", profile, true));
        }
        Assert.Throws<ArgumentException>(() => GraphicsLibraryImageLayout.CreateGuestImage(
            0x200000, 0x300, 0, 0x300, 40, 68, "graphics.library", new string('I', 28), Profile(false), true));
        Assert.Throws<ArgumentException>(() => GraphicsLibraryImageLayout.CreateGuestImage(
            0x200000, 0x264, 0, 0x24C, 40, 68, "graphics.library", "id", Profile(false), true));
    }

    private static GraphicsLibraryImageProfile Profile(bool ntsc)
        => ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;

    private static byte[] Create(uint address, int length, int reported, GraphicsLibraryImageProfile profile)
        => GraphicsLibraryImageLayout.CreateGuestImage(address, length, 0, reported, 40, 68,
            "graphics.library", "graphics.library 40.68", profile);

    private static uint Long(Func<int, byte> read, int offset)
        => ((uint)read(offset) << 24) | ((uint)read(offset + 1) << 16) |
           ((uint)read(offset + 2) << 8) | read(offset + 3);

    private static void AssertNativeImage(Func<int, byte> read, uint address, bool ntsc)
    {
        Assert.Equal(address + 0x220, Long(read, 0x0A));
        Assert.Equal(address + 0x234, Long(read, 0x18));
        Assert.Equal(NativeImageSize, (read(0x12) << 8) | read(0x13));
        Assert.Equal(ntsc ? 1 : 4, (read(0xCE) << 8) | read(0xCF));
        Assert.All(Enumerable.Range(0xA0, 0xCE - 0xA0), offset => Assert.Equal((byte)0, read(offset)));
        Assert.Equal(Encoding.ASCII.GetBytes("graphics.library\0"), Enumerable.Range(0x220, 17).Select(read));
        Assert.Equal(Encoding.ASCII.GetBytes("graphics.library 40.68\0"), Enumerable.Range(0x234, 23).Select(read));
        Assert.Equal(address + 0x90, Long(read, 0x8C));
        Assert.Equal(address + 0x8C, Long(read, 0x94));
        Assert.Equal(address + 0x184, Long(read, 0x180));
        Assert.Equal(0u, Long(read, 0x184));
        Assert.Equal(address + 0x180, Long(read, 0x188));
    }
}
