using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsColorMapSpanTests
{
    private const uint CapturedFrame = 0xA1B2_C3D4;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(0xFFFF_FFB8u, 2, 72)]
    [InlineData(0xFFFF_FFB4u, 3, 76)]
    [InlineData(0xFFFB_FFC4u, 65535, 0x4003C)]
    public void ColorMapEnvelopeEndingAtLastGuestByteConstructsAndFreesNatively(
        uint allocationBase, int count, int requestedBytes)
    {
        using var fixture = new Fixture(allocationBase);
        Assert.Equal((ulong)uint.MaxValue, (ulong)allocationBase + (uint)requestedBytes - 1);
        Assert.Equal(GraphicsLayouts.NativeColorMapBaseSize + count * 4, requestedBytes);

        var created = fixture.Execute(GraphicsLvo.GetColorMap, checked((uint)count));

        Assert.False(created.UsedFallback);
        var colorMap = allocationBase + (uint)GraphicsLayouts.NativeColorMapPrivatePrefixSize;
        Assert.Equal(colorMap, created.Value);
        Assert.Equal((allocationBase, (uint)requestedBytes, 0x0001_0001u),
            Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        Assert.Equal(GraphicsLayouts.NativeColorMapMarker, fixture.ReadWord(allocationBase));
        Assert.Equal((uint)requestedBytes, fixture.ReadLong(allocationBase + 4));
        Assert.Equal((byte)2, fixture.ReadByte(colorMap + (uint)GraphicsLayouts.ColorMapType));
        Assert.Equal((ushort)count, fixture.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));

        var highTable = colorMap + (uint)GraphicsLayouts.ColorMapSize;
        var lowTable = highTable + checked((uint)(count * 2));
        Assert.Equal(highTable, fixture.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapColorTable));
        Assert.Equal(lowTable, fixture.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits));
        for (var index = 0; index < count; index++)
        {
            Assert.Equal((ushort)0, fixture.ReadWord(highTable + (uint)(index * 2)));
            Assert.Equal((ushort)0, fixture.ReadWord(lowTable + (uint)(index * 2)));
        }

        var freed = fixture.Execute(GraphicsLvo.FreeColorMap, CapturedFrame, colorMap);

        Assert.False(freed.UsedFallback);
        Assert.Equal(0u, freed.Value);
        Assert.Equal((allocationBase, (uint)requestedBytes), Assert.Single(fixture.Frees));
        Assert.Single(fixture.Allocations);
        fixture.AssertExecBaseUnchanged();
    }

    [Theory]
    [InlineData(0xFFFF_FFBAu)] // Public header fits; the low palette table wraps.
    [InlineData(0xFFFF_FFF8u)] // Even the returned public pointer would wrap.
    public void WrappingColorMapAllocationIsRetiredBeforeAnyEnvelopePublication(
        uint allocationBase)
    {
        using var fixture = new Fixture(allocationBase);
        const uint count = 2;
        const uint requestedBytes = 72;
        Assert.True((ulong)allocationBase + requestedBytes - 1 > uint.MaxValue);
        var original = fixture.CaptureMemory();
        fixture.ExpectedMemoryAtFree = original;

        var declined = fixture.Execute(GraphicsLvo.GetColorMap, count);

        Assert.True(declined.UsedFallback);
        Assert.Equal(count, declined.Value);
        Assert.Equal((allocationBase, requestedBytes, 0x0001_0001u),
            Assert.Single(fixture.Allocations));
        Assert.Equal((allocationBase, requestedBytes), Assert.Single(fixture.Frees));
        fixture.AssertMemoryUnchanged(original);
    }

    [Theory]
    [InlineData(0xFFFF_FFD0u)] // The 52-byte public header wraps into bytes 0..3.
    [InlineData(0xFFFF_FFC6u)] // Header fits, but its complete 72-byte envelope wraps.
    public void FreeColorMapDeclinesWrappedEnvelopesDespiteMatchingOwnershipFields(
        uint colorMap)
    {
        var allocationBase = colorMap - (uint)GraphicsLayouts.NativeColorMapPrivatePrefixSize;
        using var fixture = new Fixture(allocationBase);
        fixture.SeedColorMap(colorMap, 2);
        Assert.Equal(GraphicsLayouts.NativeColorMapMarker, fixture.ReadWord(allocationBase));
        Assert.Equal(72u, fixture.ReadLong(allocationBase + 4));
        Assert.Equal((ushort)2, fixture.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));
        Assert.Equal(unchecked(colorMap + (uint)GraphicsLayouts.ColorMapSize),
            fixture.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapColorTable));
        Assert.Equal(unchecked(colorMap + (uint)GraphicsLayouts.ColorMapSize + 4u),
            fixture.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits));
        fixture.AssertExecBaseUnchanged();
        var original = fixture.CaptureMemory();

        var declined = fixture.Execute(GraphicsLvo.FreeColorMap, CapturedFrame, colorMap);

        Assert.True(declined.UsedFallback);
        Assert.Equal(CapturedFrame, declined.Value);
        Assert.Empty(fixture.Allocations);
        Assert.Empty(fixture.Frees);
        fixture.AssertMemoryUnchanged(original);
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] High, byte[] Low);
    private readonly record struct CallResult(uint Value, bool UsedFallback);

    private sealed class Fixture : IDisposable
    {
        private const uint PhysicalMask = 0x00FF_FFFF;
        private const uint ExecBase = 0x0077_0000;
        private const uint CodeAddress = 0x0087_0000;
        private const uint Stack = 0x00C7_0000;
        private const uint StackPointer = Stack + 0x200;
        private const uint ReturnAddress = 0x00F7_0000;
        private const uint D4Canary = 0xD4D4_D4D4;
        private const int LowMemorySize = 0x100;
        private readonly IM68kCore _cpu;
        private readonly uint _highPhysicalBase;
        private readonly int _highMemorySize;

        internal Fixture(uint allocationBase)
        {
            // Accurate 68000 retains 32-bit logical pointer values in its
            // registers, while data accesses use their 24-bit bus aliases.
            _highPhysicalBase = Physical(allocationBase) & 0x00FF_FF00u;
            _highMemorySize = checked((int)(0x0100_0000u - _highPhysicalBase));
            var high = new byte[_highMemorySize];
            var low = new byte[LowMemorySize];
            Array.Fill(high, (byte)0xA5);
            Array.Fill(low, (byte)0xA5);
            Bus.MapWritableMemory(_highPhysicalBase, high);
            Bus.MapWritableMemory(0, low);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(Stack, new byte[0x400]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((allocationBase, size, flags));

                // Valid allocations honor CLEAR. A malformed allocator may
                // return an untouched wrapping pointer; its cleanup must not
                // depend on clearing wrapped aliases or disabling SysBase.
                if (size != 0 && (ulong)allocationBase + size - 1 <= uint.MaxValue &&
                    (flags & 0x0001_0000u) != 0)
                    Bus.ClearMemory(Physical(allocationBase), checked((int)size));
                state.D[0] = allocationBase;
                PoisonExecVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                // Observe the public Exec ABI before clobbering volatile
                // registers, including D0, as a real FreeMem call may do.
                Frees.Add((state.A[1], state.D[0]));
                if (ExpectedMemoryAtFree is { } expected)
                    AssertMemoryUnchanged(expected);
                PoisonExecVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal MemorySnapshot? ExpectedMemoryAtFree { get; set; }

        private static uint Physical(uint address) => address & PhysicalMask;
        internal byte ReadByte(uint address) => Bus.ReadByte(Physical(address));
        internal ushort ReadWord(uint address) => Bus.ReadWord(Physical(address));
        internal uint ReadLong(uint address) => Bus.ReadLong(Physical(address));

        internal void SeedColorMap(uint colorMap, ushort count)
        {
            var allocationBase = colorMap - (uint)GraphicsLayouts.NativeColorMapPrivatePrefixSize;
            for (var offset = 0u; offset < GraphicsLayouts.NativeColorMapBaseSize; offset++)
                Bus.WriteByte(Physical(unchecked(allocationBase + offset)), 0, 0);
            Bus.WriteWord(Physical(allocationBase), GraphicsLayouts.NativeColorMapMarker);
            Bus.WriteLong(Physical(allocationBase + 4),
                (uint)GraphicsLayouts.NativeColorMapBaseSize + count * 4u);
            Bus.WriteByte(Physical(colorMap + (uint)GraphicsLayouts.ColorMapType), 2, 0);
            Bus.WriteWord(Physical(colorMap + (uint)GraphicsLayouts.ColorMapCount), count);
            Bus.WriteLong(Physical(colorMap + (uint)GraphicsLayouts.ColorMapColorTable),
                unchecked(colorMap + (uint)GraphicsLayouts.ColorMapSize));
            Bus.WriteLong(Physical(colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits),
                unchecked(colorMap + (uint)GraphicsLayouts.ColorMapSize + count * 2u));

            // A forged table pointer may be 4 or 8. Keep Exec available so a
            // passing decline cannot be caused by an accidentally null base.
            Bus.WriteLong(4, ExecBase);
        }

        internal MemorySnapshot CaptureMemory()
            => new(ReadPhysicalBytes(_highPhysicalBase, _highMemorySize),
                ReadPhysicalBytes(0, LowMemorySize));

        private byte[] ReadPhysicalBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal void AssertMemoryUnchanged(MemorySnapshot expected)
        {
            Assert.Equal(expected.High, ReadPhysicalBytes(_highPhysicalBase, _highMemorySize));
            Assert.Equal(expected.Low, ReadPhysicalBytes(0, LowMemorySize));
            AssertExecBaseUnchanged();
        }

        internal void AssertExecBaseUnchanged() => Assert.Equal(ExecBase, Bus.ReadLong(4));

        private static void PoisonExecVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Execute(GraphicsLvo vector, uint d0, uint a0 = 0)
        {
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = d0;
            _cpu.State.D[4] = D4Canary;
            _cpu.State.A[0] = a0;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            var usedFallback = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                // Duplicated local fallback RTS instructions are recognized
                // by the restored original D0 as well as the exported entry.
                if (_cpu.State.ProgramCounter == CodeAddress + (uint)Image.Value.Fallback ||
                    (_cpu.State.D[0] == d0 && Bus.ReadWord(_cpu.State.ProgramCounter) == 0x4E75))
                {
                    usedFallback = true;
                    Assert.Equal(d0, _cpu.State.D[0]);
                    Assert.Equal(StackPointer, _cpu.State.A[7]);
                }

                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;

                Assert.Equal(StackPointer + 4, _cpu.State.A[7]);
                // The new extent guard must not add D4 to GetColorMap's
                // scratch set. Full public callee-save coverage is a
                // separate library-entry obligation, not this raw-body test.
                if (vector == GraphicsLvo.GetColorMap)
                    Assert.Equal(D4Canary, _cpu.State.D[4]);
                return new CallResult(_cpu.State.D[0], usedFallback);
            }

            throw new InvalidOperationException(
                $"Native {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
