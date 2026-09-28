using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsDisplayInfoTransferBoundaryTests
{
    // Independent336 ROM observations across OCS/ECS PAL/NTSC and both ECS
    // chip-memory sizes. Keep literal expected values separate from production.
    internal static readonly (uint Key, ushort Depth)[] CapturedOcsEcsDepths =
    {
        (0x0000, 5), (0x0004, 5), (0x0080, 6), (0x0084, 6),
        (0x0400, 6), (0x0404, 6), (0x0440, 6), (0x0444, 6),
        (0x0800, 6), (0x0804, 6), (0x8000, 4), (0x8004, 4),
        (0x8020, 2), (0x8024, 2), (0x8400, 4), (0x8404, 4),
        (0x8420, 2), (0x8424, 2), (0x8440, 4), (0x8444, 4),
        (0x8460, 2), (0x8464, 2)
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataDimsDepthMatchesCapturedOcsEcsRecords(bool ntsc, bool ecs)
    {
        foreach (var (key, depth) in CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        foreach (var useHandle in new[] { false, true })
        foreach (var requested in new uint[] { 18, 66, 88 })
        {
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFF_FFFEu : mode : 0;
            var memory = new ByteWindow(0x1000, 96);
            var expectedSize = requested == 18 ? 18 : 66;
            Assert.Equal(expectedSize, GraphicsDisplayDatabase.GetDisplayInfoData(memory,
                handle, 0x1002, requested, 0x80001000, mode,
                defaultMonitorNtsc: ntsc, supportsEcsDisplay: ecs));
            Assert.Equal(depth, (ushort)((memory.Bytes[18] << 8) | memory.Bytes[19]));
            Assert.All(memory.Bytes.Take(2), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Bytes.Skip(2 + expectedSize), value => Assert.Equal((byte)0xA5, value));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataDimsRasterLimitsMatchCapturedOcsEcsRecords(bool ntsc, bool ecs)
    {
        foreach (var (key, _) in CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        {
            var memory = new ByteWindow(0x1000, 70);
            Assert.Equal(66, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0,
                0x1002, 88, 0x80001000, owner | key,
                defaultMonitorNtsc: ntsc, supportsEcsDisplay: ecs));
            Assert.Equal(CapturedRasterLimits(key), memory.Bytes.Skip(20).Take(8));
        }
    }

    // Literal scalar fields from the independent336 ROM captures: lores,
    // hires and superhires MinRasterWidth16/32/64, MinRasterHeight1,
    // MaxRasterWidth1008 and MaxRasterHeight1024. Nominal/overscan rectangles
    // are different fields and must not supply these raster limits.
    internal static byte[] CapturedRasterLimits(uint key)
        => new byte[] { 0, (byte)((key & 0x20) != 0 ? 64 : (key & 0x8000) != 0 ? 32 : 16),
            0, 1, 3, 0xF0, 4, 0 };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataDimsRectanglesMatchCapturedOcsEcsRecords(bool ntsc, bool ecs)
    {
        foreach (var (key, _) in CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        foreach (var useHandle in new[] { false, true })
        foreach (var buffer in new uint[] { 0x1002, 0x1003 })
        foreach (var requested in new uint[] { 26, 27, 33, 34, 41, 42, 49, 50,
                     57, 58, 65, 66, 88, uint.MaxValue })
        {
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFF_FFFEu : mode : 0;
            var expectedSize = 26 + 8 * ((int)(Math.Min(requested, 66u) - 26) / 8);
            var memory = new ByteWindow(0x1000, 96);
            Assert.Equal(expectedSize, GraphicsDisplayDatabase.GetDisplayInfoData(memory,
                handle, buffer, requested, 0x80001000, mode,
                defaultMonitorNtsc: ntsc, supportsEcsDisplay: ecs));
            var offset = (int)(buffer - memory.Start);
            Assert.Equal(CapturedRasterLimits(key), memory.Bytes.Skip(offset + 18).Take(8));
            Assert.Equal(CapturedDimensionRectangles(key, ntsc).Take(expectedSize - 26),
                memory.Bytes.Skip(offset + 26).Take(expectedSize - 26));
            Assert.All(memory.Bytes.Take(offset), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Bytes.Skip(offset + expectedSize), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, buffer + (uint)expectedSize - 1));
            Assert.False(memory.UnmappedAccess);
        }
    }

    // Literal signed Rectangle fields from independent 336 and 341 original-ROM
    // captures. All 22 mode keys agree within their resolution/lace group across
    // OCS 512K, ECS 512K and ECS 1MiB. Keep this oracle independent of production
    // geometry: notably SuperHires is 1440 pixels wide, not 4*the lores 362.
    // Returns Nominal/MaxOScan/VideoOScan/TxtOScan/StdOScan bytes at DIMS+26.
    internal static byte[] CapturedDimensionRectangles(uint key, bool ntsc)
    {
        var resolution = (key & 0x20) != 0 ? 2 : (key & 0x8000) != 0 ? 1 : 0;
        var lace = (key & 4) != 0;
        var (nominalRight, nominalBottom, left, top, maxRight, videoRight, bottom) =
            (ntsc, resolution, lace) switch
        {
            (false, 0, false) => (319, 255, -36, -15, 325, 331, 267),
            (false, 0, true) => (319, 511, -36, -30, 325, 331, 535),
            (false, 1, false) => (639, 255, -72, -15, 651, 663, 267),
            (false, 1, true) => (639, 511, -72, -30, 651, 663, 535),
            (false, 2, false) => (1279, 255, -144, -15, 1295, 1295, 267),
            (false, 2, true) => (1279, 511, -144, -30, 1295, 1295, 535),
            (true, 0, false) => (319, 199, -36, -23, 325, 331, 217),
            (true, 0, true) => (319, 399, -36, -46, 325, 331, 435),
            (true, 1, false) => (639, 199, -72, -23, 651, 663, 217),
            (true, 1, true) => (639, 399, -72, -46, 651, 663, 435),
            (true, 2, false) => (1279, 199, -144, -23, 1295, 1295, 217),
            (true, 2, true) => (1279, 399, -144, -46, 1295, 1295, 435),
            _ => throw new InvalidOperationException("Unknown captured resolution group.")
        };
        int[] words =
        {
            0, 0, nominalRight, nominalBottom,
            left, top, maxRight, bottom,
            left, top, videoRight, bottom,
            0, 0, nominalRight, nominalBottom,
            0, 0, nominalRight, nominalBottom
        };
        var bytes = new byte[40];
        for (var index = 0; index < words.Length; index++)
        {
            var value = unchecked((ushort)words[index]);
            bytes[index * 2] = (byte)(value >> 8);
            bytes[index * 2 + 1] = (byte)value;
        }
        return bytes;
    }

    // Independent OCS Kickstart 3.1 observations: DISP transfers at most48
    // bytes, not the SDK backing structure's56-byte allocation envelope.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataDispTransferAndAdmissionUseTheRomByteCount(bool ntsc, bool useHandle)
    {
        foreach (var owner in new uint[] { 0, 0x11000, 0x21000 })
        foreach (var key in new uint[] { 0, 0x8000, 0x8020 })
        foreach (var requested in Enumerable.Range(0, 66).Select(value => (uint)value)
                     .Concat(new uint[] { 88, 96, 128, uint.MaxValue }))
        {
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFF_FFFEu : mode : 0;
            var expected = Math.Min(requested, 48u);
            var memory = new ByteWindow(0x1000, 132);
            var buffer = memory.Start + 2;
            Assert.True(GraphicsDisplayDatabase.TryGetDisplayInfoDataOutputLength(
                handle, requested, 0x80000000, mode, ntsc, false, out var admitted));
            Assert.Equal(expected, admitted);
            var copied = GraphicsDisplayDatabase.GetDisplayInfoData(memory, handle, buffer,
                requested, 0x80000000, mode, defaultMonitorNtsc: ntsc, supportsEcsDisplay: false);
            Assert.Equal((int)expected, copied);
            Assert.Equal((byte)0xA5, memory.Bytes[0]);
            Assert.Equal((byte)0xA5, memory.Bytes[1]);
            Assert.All(memory.Bytes.Skip(2 + (int)expected), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, buffer + expected - 1));
            if (expected == 0)
                Assert.Empty(memory.Accesses);
        }
    }

    [Theory]
    [InlineData(0x1000u, 0x1002u)]
    [InlineData(0xFFFF_FFD0u, 0xFFFF_FFD0u)]
    public void DisplayInfoDataDispDoesNotRequireUntouchedTailMapping(uint start, uint buffer)
    {
        var memory = new ByteWindow(start, checked((int)(buffer - start + 48)));
        Assert.Equal(48, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, buffer,
            uint.MaxValue, 0x80000000, 0x21000, supportsEcsDisplay: false));
        Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, buffer + 47));
        Assert.False(memory.UnmappedAccess);
    }

    [Fact]
    public void DisplayInfoDataDispRejectsAnActuallyWrappingTransferWithoutMemoryAccess()
    {
        var memory = new ByteWindow(0xFFFF_FFD1, 47);
        Assert.Equal(0, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, memory.Start,
            uint.MaxValue, 0x80000000, 0x21000, supportsEcsDisplay: false));
        Assert.Empty(memory.Accesses);
        Assert.All(memory.Bytes, value => Assert.Equal((byte)0xA5, value));
    }

    // ROM observations publish the26-byte scalar prefix, then only complete
    // eight-byte rectangles. The SDK88-byte allocation is not a transfer span.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataDimsTransferAndAdmissionUseCompleteRomRectangles(bool ntsc, bool useHandle)
    {
        foreach (var owner in new uint[] { 0, 0x11000, 0x21000 })
        foreach (var key in new uint[] { 0, 0x8000, 0x8020 })
        foreach (var requested in Enumerable.Range(0, 130).Select(value => (uint)value)
                     .Append(uint.MaxValue))
        {
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFF_FFFEu : mode : 0;
            var expected = requested <= 26 ? requested : 26 + 8 * ((Math.Min(requested, 66u) - 26) / 8);
            var memory = new ByteWindow(0x1000, 134);
            var buffer = memory.Start + 2;
            Assert.True(GraphicsDisplayDatabase.TryGetDisplayInfoDataOutputLength(
                handle, requested, 0x80001000, mode, ntsc, false, out var admitted));
            Assert.Equal(expected, admitted);
            var copied = GraphicsDisplayDatabase.GetDisplayInfoData(memory, handle, buffer,
                requested, 0x80001000, mode, defaultMonitorNtsc: ntsc, supportsEcsDisplay: false);
            Assert.Equal((int)expected, copied);
            Assert.All(memory.Bytes.Take(2), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Bytes.Skip(2 + (int)expected), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, buffer + expected - 1));
            Assert.False(memory.UnmappedAccess);
            if (expected == 0)
                Assert.Empty(memory.Accesses);
        }
    }

    [Theory]
    [InlineData(0xFFFF_FFE6u, 33u, 26)]
    [InlineData(0xFFFF_FFDEu, 41u, 34)]
    [InlineData(0xFFFF_FFBEu, uint.MaxValue, 66)]
    public void DisplayInfoDataDimsDoesNotRequireUntransferredRectangleOrTailMapping(
        uint buffer, uint requested, int expected)
    {
        var memory = new ByteWindow(buffer, expected);
        Assert.Equal(expected, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, buffer,
            requested, 0x80001000, 0x21000, supportsEcsDisplay: false));
        Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, uint.MaxValue));
        Assert.False(memory.UnmappedAccess);
    }

    [Theory]
    [InlineData(0xFFFF_FFE7u, 33u, 25)]
    [InlineData(0xFFFF_FFDFu, 41u, 33)]
    [InlineData(0xFFFF_FFBFu, uint.MaxValue, 65)]
    public void DisplayInfoDataDimsRejectsActuallyWrappingRectanglesWithoutMemoryAccess(
        uint buffer, uint requested, int available)
    {
        var memory = new ByteWindow(buffer, available);
        Assert.Equal(0, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, buffer,
            requested, 0x80001000, 0x21000, supportsEcsDisplay: false));
        Assert.Empty(memory.Accesses);
        Assert.All(memory.Bytes, value => Assert.Equal((byte)0xA5, value));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataMntrTransferAndAdmissionUseTheRomByteCount(bool ntsc, bool useHandle)
    {
        foreach (var owner in new uint[] { 0, 0x11000, 0x21000 })
        foreach (var key in new uint[] { 0, 0x8000, 0x8020 })
        foreach (var requested in Enumerable.Range(0, 130).Select(value => (uint)value)
                     .Append(uint.MaxValue))
        {
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFF_FFFEu : mode : 0;
            var expected = Math.Min(requested, 88u);
            var memory = new ByteWindow(0x1000, 134);
            var buffer = memory.Start + 2;
            Assert.True(GraphicsDisplayDatabase.TryGetDisplayInfoDataOutputLength(
                handle, requested, 0x80002000, mode, ntsc, false, out var admitted));
            Assert.Equal(expected, admitted);
            Assert.Equal((int)expected, GraphicsDisplayDatabase.GetDisplayInfoData(memory,
                handle, buffer, requested, 0x80002000, mode, monitorSpecProvider: _ => 0x00600000,
                defaultMonitorNtsc: ntsc, supportsEcsDisplay: false));
            Assert.All(memory.Bytes.Take(2), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Bytes.Skip(2 + (int)expected), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, buffer + expected - 1));
            Assert.False(memory.UnmappedAccess);
            if (expected == 0)
                Assert.Empty(memory.Accesses);
        }
    }

    [Theory]
    [InlineData(0x1001u)]
    [InlineData(0xFFFF_FFA8u)]
    public void DisplayInfoDataMntrDoesNotRequireUntouchedTailMapping(uint buffer)
    {
        var memory = new ByteWindow(buffer, 88);
        var providerCalls = 0;
        Assert.Equal(88, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, buffer,
            uint.MaxValue, 0x80002000, 0x21000,
            monitorSpecProvider: _ => { providerCalls++; return 0x00600000; }, supportsEcsDisplay: false));
        Assert.Equal(1, providerCalls);
        Assert.All(memory.Accesses, address => Assert.InRange(address, buffer, buffer + 87));
        Assert.False(memory.UnmappedAccess);
    }

    [Fact]
    public void DisplayInfoDataMntrRejectsActualWrapBeforeMonitorAllocationOrMemoryAccess()
    {
        var memory = new ByteWindow(0xFFFF_FFA9, 87);
        var providerCalls = 0;
        Assert.Equal(0, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, memory.Start,
            uint.MaxValue, 0x80002000, 0x21000,
            monitorSpecProvider: _ => { providerCalls++; return 0x00600000; }, supportsEcsDisplay: false));
        Assert.Equal(0, providerCalls);
        Assert.Empty(memory.Accesses);
        Assert.All(memory.Bytes, value => Assert.Equal((byte)0xA5, value));
    }

    private sealed class ByteWindow(uint start, int size) : IGraphicsMemory
    {
        internal uint Start { get; } = start;
        internal byte[] Bytes { get; } = Enumerable.Repeat((byte)0xA5, size).ToArray();
        internal List<uint> Accesses { get; } = new();
        internal bool UnmappedAccess { get; private set; }

        private bool TryOffset(uint address, out int offset)
        {
            Accesses.Add(address);
            if (address < Start || (ulong)address - Start >= (ulong)Bytes.Length)
            {
                UnmappedAccess = true;
                offset = 0;
                return false;
            }
            offset = (int)(address - Start);
            return true;
        }

        public bool TryReadByte(uint address, out byte value)
        {
            var valid = TryOffset(address, out var offset);
            value = valid ? Bytes[offset] : (byte)0;
            return valid;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            if (!TryOffset(address, out var offset)) return false;
            Bytes[offset] = value;
            return true;
        }

        public bool TryReadWord(uint address, out ushort value) => throw new NotSupportedException();
        public bool TryReadLong(uint address, out uint value) => throw new NotSupportedException();
        public bool TryWriteWord(uint address, ushort value) => throw new NotSupportedException();
        public bool TryWriteLong(uint address, uint value) => throw new NotSupportedException();
    }
}
