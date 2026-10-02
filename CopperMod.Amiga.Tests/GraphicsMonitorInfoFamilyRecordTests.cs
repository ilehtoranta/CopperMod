using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsMonitorInfoFamilyRecordTests
{
    // Independently pinned by original-ROM test 457; do not derive these
    // expectations from the replacement image or MonitorSpec timing helpers.
    internal static byte[] Baseline(bool ntsc) => Convert.FromHexString(ntsc
        ? "80002000000110000000000300000009000000000081002C002C0034005D00150088003F010600E2001500000000000000000000000036FF0000289F0000000000000000000036FF0000289F0016001A0081002C00019000"
        : "80002000000210000000000300000009000000000081002C002C002C005D001D00880039013800E2001D00000000000000000000000036FF00002BFF0000000000000000000036FF00002BFF001600160081002C00029000");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MonitorInfoImagePreservesFamilyBytesAndOnlyPatchesDynamicFields(bool ntsc)
    {
        foreach (var pointer in new uint[] { 0, 1, 0x00600001, 0xFFFFFF60, 0xDEADBEEF, uint.MaxValue })
        foreach (var point in new[] { new GraphicsMonitorViewPosition(129, 44),
                     new GraphicsMonitorViewPosition(short.MinValue, short.MaxValue) })
        {
            var original = new GraphicsMonitorViewPosition(-7, 1234);
            var expected = new byte[96];
            Baseline(ntsc).CopyTo(expected, 0);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(16), pointer);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(20), point.WordPair);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(80), original.WordPair);
            var actual = GraphicsMonitorInfoImage.Create(!ntsc, pointer, point, original);
            Assert.Equal(expected, actual);
            actual.AsSpan().Fill(0xAA);
            Assert.Equal(expected, GraphicsMonitorInfoImage.Create(!ntsc, pointer, point, original));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisplayInfoDataMntrFamilyBytesMatchRomAcrossModesAndPartialTransfers(bool ntsc, bool useHandle)
    {
        foreach (var (key, _) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var family in new uint[] { 0, 0x11000, 0x21000 })
        foreach (var requested in new uint[] { 1, 16, 17, 20, 21, 24, 25, 43, 44, 45, 76, 77, 80, 81, 84, 85, 88, 96, uint.MaxValue })
        foreach (var odd in new[] { false, true })
        {
            var mode = family | key;
            var handle = useHandle ? mode == 0 ? 0xFFFFFFFEu : mode : 0;
            var memory = new Memory();
            var buffer = odd ? 3u : 2u;
            var expected = Baseline(family == 0 ? ntsc : family == 0x11000);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(16), 0xDEADBEEF);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(20), 0x80007FFF);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(80), 0xFFFF0001);
            var count = (int)Math.Min(requested, 88u);
            Assert.Equal(count, GraphicsDisplayDatabase.GetDisplayInfoData(memory,
                handle, buffer, requested, 0x80002000, useHandle ? 0xDEADBEEFu : mode,
                monitorSpecProvider: _ => 0xDEADBEEF, defaultMonitorNtsc: ntsc,
                monitorPositionProvider: _ => new(short.MinValue, short.MaxValue),
                monitorOriginalPositionProvider: _ => new(-1, 1)));
            Assert.Equal(expected.Take(count), memory.Bytes.Skip((int)buffer).Take(count));
            Assert.All(memory.Bytes.Take((int)buffer), value => Assert.Equal((byte)0xA5, value));
            Assert.All(memory.Bytes.Skip((int)buffer + count), value => Assert.Equal((byte)0xA5, value));
        }
    }

    private sealed class Memory : IGraphicsMemory
    {
        internal byte[] Bytes { get; } = Enumerable.Repeat((byte)0xA5, 100).ToArray();
        public bool TryReadByte(uint address, out byte value) { value = address < Bytes.Length ? Bytes[address] : (byte)0; return address < Bytes.Length; }
        public bool TryWriteByte(uint address, byte value) { if (address >= Bytes.Length) return false; Bytes[address] = value; return true; }
        public bool TryReadWord(uint address, out ushort value) => throw new NotSupportedException();
        public bool TryReadLong(uint address, out uint value) => throw new NotSupportedException();
        public bool TryWriteWord(uint address, ushort value) => throw new NotSupportedException();
        public bool TryWriteLong(uint address, uint value) => throw new NotSupportedException();
    }
}
