using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRasterExecBoundaryTests
{
    private const uint WidthArgument = 0xABCD_0011;
    private const uint HeightArgument = 0x1234_0003;
    private const ushort Width = 17;
    private const ushort Height = 3;
    private const ushort RasterMarker = 0x5253;
    private const uint RasterBase = 0x00D0_0040;
    private const uint HeaderBytes = 10;
    private const uint PayloadBytes = 12;
    private const uint AllocationBytes = HeaderBytes + PayloadBytes;
    private const uint RasterPublic = RasterBase + HeaderBytes;
    private const uint AllocationFlags = 0x0001_0003; // PUBLIC | CHIP | CLEAR

    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Fact]
    public void RasterRoundtripUsesUwordDimensionsAndExactExecAllocationSpan()
    {
        using var fixture = new Fixture();
        var before = fixture.CaptureMemory();

        var allocated = fixture.Execute(GraphicsLvo.AllocRaster, WidthArgument, HeightArgument);

        Assert.False(allocated.UsedFallback);
        Assert.Equal(RasterPublic, allocated.Value);
        Assert.Equal((RasterBase, AllocationBytes, AllocationFlags), Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        Assert.Equal(RasterMarker, fixture.ReadWord(RasterBase));
        Assert.Equal(Width, fixture.ReadWord(RasterBase + 2));
        Assert.Equal(Height, fixture.ReadWord(RasterBase + 4));
        Assert.Equal(AllocationBytes, fixture.ReadLong(RasterBase + 6));
        Assert.All(fixture.ReadBytes(RasterPublic, (int)PayloadBytes), value => Assert.Equal((byte)0, value));
        fixture.AssertCanariesUnchanged(before);

        var beforeFree = fixture.CaptureMemory();
        fixture.ExpectedMemoryAtFree = beforeFree;
        var freed = fixture.Execute(GraphicsLvo.FreeRaster, WidthArgument, HeightArgument, RasterPublic);

        Assert.False(freed.UsedFallback);
        Assert.Equal(0u, freed.Value);
        fixture.AssertMemoryUnchanged(beforeFree);
        Assert.Single(fixture.Allocations);
        Assert.Equal((RasterBase, AllocationBytes), Assert.Single(fixture.Frees));
    }

    [Fact]
    public void SeededRasterHeaderFreesItsRecordedSpanThroughExecA1D0()
    {
        using var fixture = new Fixture();
        fixture.SeedRaster();
        var before = fixture.CaptureMemory();
        fixture.ExpectedMemoryAtFree = before;

        var result = fixture.Execute(GraphicsLvo.FreeRaster, WidthArgument, HeightArgument, RasterPublic);

        Assert.False(result.UsedFallback);
        Assert.Equal(0u, result.Value);
        Assert.Empty(fixture.Allocations);
        fixture.AssertMemoryUnchanged(before);
        Assert.Equal((RasterBase, AllocationBytes), Assert.Single(fixture.Frees));
    }

    [Fact]
    public void OddRasterAllocationReleasesUntouchedBlockAndRestoresFullCapturedWidth()
    {
        using var fixture = new Fixture { AllocationResult = RasterBase + 1 };
        var before = fixture.CaptureMemory();
        Assert.All(before.Heap, value => Assert.Equal((byte)0xA5, value));
        fixture.ExpectedMemoryAtFree = before;

        var result = fixture.Execute(GraphicsLvo.AllocRaster, WidthArgument, HeightArgument);

        AssertDeclined(result, WidthArgument);
        fixture.AssertMemoryUnchanged(before);
        Assert.Equal((RasterBase + 1, AllocationBytes, AllocationFlags), Assert.Single(fixture.Allocations));
        Assert.Equal((RasterBase + 1, AllocationBytes), Assert.Single(fixture.Frees));
    }

    [Fact]
    public void NullRasterAllocationDeclinesWithoutReleaseOrPublication()
    {
        using var fixture = new Fixture { AllocationResult = 0 };
        var before = fixture.CaptureMemory();

        var result = fixture.Execute(GraphicsLvo.AllocRaster, WidthArgument, HeightArgument);

        AssertDeclined(result, WidthArgument);
        Assert.Equal((0u, AllocationBytes, AllocationFlags), Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        fixture.AssertMemoryUnchanged(before);
    }

    [Theory]
    [InlineData(0xABCD_0000u, HeightArgument, true)]
    [InlineData(WidthArgument, 0x1234_0000u, true)]
    [InlineData(WidthArgument, HeightArgument, false)]
    public void RasterAllocationPreflightDeclinesWithoutExecCallsOrMutation(
        uint widthArgument, uint heightArgument, bool execAvailable)
    {
        using var fixture = new Fixture();
        fixture.SetExecAvailable(execAvailable);
        var before = fixture.CaptureMemory();

        var result = fixture.Execute(GraphicsLvo.AllocRaster, widthArgument, heightArgument);

        AssertDeclined(result, widthArgument);
        Assert.Empty(fixture.Allocations);
        Assert.Empty(fixture.Frees);
        fixture.AssertMemoryUnchanged(before);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(RasterPublic + 1)]
    [InlineData(2u)]
    [InlineData(4u)]
    [InlineData(6u)]
    [InlineData(8u)]
    public void InvalidRasterPublicPointersDeclineWithoutReleaseOrMutation(uint pointer)
    {
        using var fixture = new Fixture();
        fixture.SeedRaster();
        var before = fixture.CaptureMemory();

        var result = fixture.Execute(GraphicsLvo.FreeRaster, WidthArgument, HeightArgument, pointer);

        AssertDeclined(result, WidthArgument);
        Assert.Empty(fixture.Allocations);
        Assert.Empty(fixture.Frees);
        fixture.AssertMemoryUnchanged(before);
    }

    [Theory]
    [InlineData("foreign-marker")]
    [InlineData("width-mismatch")]
    [InlineData("height-mismatch")]
    [InlineData("zero-width")]
    [InlineData("zero-height")]
    [InlineData("zero-stored-span")]
    [InlineData("missing-exec")]
    public void InvalidRasterHeaderOrDimensionsDeclineWithoutReleaseOrMutation(string scenario)
    {
        using var fixture = new Fixture();
        fixture.SeedRaster();
        var widthArgument = WidthArgument;
        var heightArgument = HeightArgument;
        switch (scenario)
        {
            case "foreign-marker":
                fixture.SeedRaster(marker: 0x5252);
                break;
            case "width-mismatch":
                widthArgument++;
                break;
            case "height-mismatch":
                heightArgument++;
                break;
            case "zero-width":
                widthArgument &= 0xFFFF_0000u;
                break;
            case "zero-height":
                heightArgument &= 0xFFFF_0000u;
                break;
            case "zero-stored-span":
                fixture.SeedRaster(storedSpan: 0);
                break;
            case "missing-exec":
                fixture.SetExecAvailable(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = fixture.CaptureMemory();

        var result = fixture.Execute(GraphicsLvo.FreeRaster, widthArgument, heightArgument, RasterPublic);

        AssertDeclined(result, widthArgument);
        Assert.Empty(fixture.Allocations);
        Assert.Empty(fixture.Frees);
        fixture.AssertMemoryUnchanged(before);
    }

    private static void AssertDeclined(CallResult result, uint capturedWidth)
    {
        Assert.True(result.UsedFallback);
        Assert.Equal(capturedWidth, result.Value);
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low);
    private readonly record struct CallResult(uint Value, bool UsedFallback);

    private sealed class Fixture : IDisposable
    {
        private const uint ExecBase = 0x0077_0000;
        private const uint CodeAddress = 0x0087_0000;
        private const uint HeapAddress = 0x00D0_0000;
        private const int HeapSize = 0x100;
        private const int LowMemorySize = 0x100;
        private const uint Stack = 0x00C7_0000;
        private const uint StackPointer = Stack + 0x200;
        private const uint ReturnAddress = 0x00F7_0000;
        private readonly IM68kCore _cpu;

        internal Fixture()
        {
            var heap = new byte[HeapSize];
            var low = new byte[LowMemorySize];
            Array.Fill(heap, (byte)0xA5);
            Array.Fill(low, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            Bus.MapWritableMemory(0, low);
            SetExecAvailable(true);
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(Stack, new byte[0x400]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                var address = AllocationResult;
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));

                // Honor CLEAR for a usable result. The deliberately odd
                // provider leaves its block as A5 so premature publication
                // cannot be concealed by an allocator-side clear.
                if (address != 0 && (address & 1) == 0)
                {
                    Assert.InRange(address, HeapAddress, HeapAddress + (uint)HeapSize - 1u);
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - address);
                    if ((flags & 0x0001_0000u) != 0)
                        Bus.ClearMemory(address, checked((int)size));
                }
                state.D[0] = address;
                PoisonExecVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                // These are Exec's real inputs, not the old D0/D1 pair.
                Frees.Add((state.A[1], state.D[0]));
                if (ExpectedMemoryAtFree is { } expected)
                    AssertMemoryUnchanged(expected);
                PoisonExecVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal uint AllocationResult { get; set; } = RasterBase;
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal MemorySnapshot? ExpectedMemoryAtFree { get; set; }

        internal void SetExecAvailable(bool available) => Bus.WriteLong(4, available ? ExecBase : 0);
        internal ushort ReadWord(uint address) => Bus.ReadWord(address);
        internal uint ReadLong(uint address) => Bus.ReadLong(address);
        internal byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal void SeedRaster(ushort marker = RasterMarker, uint storedSpan = AllocationBytes)
        {
            // Free tests do not depend on the native allocation path.
            Bus.WriteWord(RasterBase, marker);
            Bus.WriteWord(RasterBase + 2, Width);
            Bus.WriteWord(RasterBase + 4, Height);
            Bus.WriteLong(RasterBase + 6, storedSpan);
        }

        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize));

        internal void AssertMemoryUnchanged(MemorySnapshot expected)
        {
            var actual = CaptureMemory();
            Assert.Equal(expected.Heap, actual.Heap);
            Assert.Equal(expected.Low, actual.Low);
        }

        internal void AssertCanariesUnchanged(MemorySnapshot expected)
        {
            var actual = CaptureMemory();
            var beforeHeader = checked((int)(RasterBase - HeapAddress));
            var afterPayload = beforeHeader + checked((int)AllocationBytes);
            Assert.Equal(expected.Heap.Take(beforeHeader), actual.Heap.Take(beforeHeader));
            Assert.Equal(expected.Heap.Skip(afterPayload), actual.Heap.Skip(afterPayload));
            Assert.Equal(expected.Low, actual.Low);
        }

        private static void PoisonExecVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Execute(GraphicsLvo vector, uint d0, uint d1, uint a0 = 0)
        {
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = d0;
            _cpu.State.D[1] = d1;
            _cpu.State.A[0] = a0;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            var usedFallback = false;
            for (var instruction = 0; instruction < 4096; instruction++)
            {
                // Distant branches may use a local RTS copy. Every decline
                // must restore the FULL original D0 and caller stack depth;
                // a native pointer or FreeRaster's zero cannot match d0.
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
                return new CallResult(_cpu.State.D[0], usedFallback);
            }

            throw new InvalidOperationException(
                $"Native {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
