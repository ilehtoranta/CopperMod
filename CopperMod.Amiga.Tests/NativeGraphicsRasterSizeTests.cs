using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRasterSizeTests
{
    private const uint PrefixBytes = 10;
    private const uint AllocationBase = 0x00D0_0040;
    private const uint Raster = AllocationBase + PrefixBytes;
    private const uint AllocationFlags =
        (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(17u, 3u, 22u)]
    [InlineData(0xABCD_0011u, 0x1234_0003u, 22u)]
    [InlineData(65520u, 1u, 8200u)]
    [InlineData(65521u, 1u, 8202u)]
    [InlineData(65535u, 1u, 8202u)]
    [InlineData(65535u, 2u, 16394u)]
    [InlineData(0xABCD_FFF0u, 0x1234_0001u, 8200u)]
    [InlineData(0xABCD_FFF1u, 0x1234_0001u, 8202u)]
    [InlineData(0xABCD_FFFFu, 0x1234_0001u, 8202u)]
    [InlineData(0xABCD_FFFFu, 0x1234_0002u, 16394u)]
    public void RasterRowRoundingAllocatesAndFreesTheExactUwordEnvelope(
        uint width, uint height, uint expectedBytes)
    {
        Assert.Equal(expectedBytes, RequestedSize(width, height));
        using var fixture = new Fixture();
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.AllocRaster, width, height);
        var failures = new List<string>();

        Check(failures, "Exec allocation size/flags", () =>
            Assert.Equal((AllocationBase, expectedBytes, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "private header", () => fixture.AssertHeader(width, height, expectedBytes));
        Check(failures, "cleared payload and A5 padding", () => fixture.AssertPayloadAndPadding(expectedBytes));
        Check(failures, "allocation canaries", () => fixture.AssertOutsideAllocationUnchanged(before, expectedBytes));
        Check(failures, "no premature free", () => Assert.Empty(fixture.Frees));

        // Continue through FreeRaster even when the request/header size is
        // wrong, so the initial red run also exposes the wrong release span.
        // Do not manufacture a valid header to conceal allocation defects.
        if (!allocated.UsedFallback && allocated.Value == Raster)
        {
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeRaster, width, height, allocated.Value);
            Check(failures, "Exec release address/size", () =>
                Assert.Equal((AllocationBase, expectedBytes), Assert.Single(fixture.Frees)));
            Check(failures, "no allocation during release", () => Assert.Single(fixture.Allocations));
            Check(failures, "memory at release", () =>
            {
                Assert.NotNull(fixture.MemoryAtFree);
                AssertMemoryEqual(beforeFree, fixture.MemoryAtFree!);
            });
            Check(failures, "memory after release", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeRaster public ABI", () => AssertReturn(freed, 0, expectFallback: false));
        }
        else
        {
            failures.Add("FreeRaster was not attempted because allocation did not return the native raster.");
        }

        Check(failures, "AllocRaster public ABI", () => AssertReturn(allocated, Raster, expectFallback: false));
        AssertNoFailures(failures, width, height);
    }

    [Theory]
    [InlineData(65521u, 65535u)]
    [InlineData(65535u, 65535u)]
    [InlineData(0xABCD_FFF1u, 0x1234_FFFFu)]
    [InlineData(0xABCD_FFFFu, 0x1234_FFFFu)]
    public void MaximumHeightReportsTheFullSizeToANullAllocatorWithoutAllocatingHugeMemory(
        uint width, uint height)
    {
        const uint expectedBytes = 536_862_730; // 8192 bytes/row * 65535 rows + 10
        Assert.Equal(expectedBytes, RequestedSize(width, height));
        using var fixture = new Fixture { AllocationResult = 0 };
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocRaster, width, height);
        var failures = new List<string>();
        Check(failures, "full Exec request", () =>
            Assert.Equal((0u, expectedBytes, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "no null-result release", () => Assert.Empty(fixture.Frees));
        Check(failures, "no publication", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "full captured-width fallback", () => AssertReturn(result, width, expectFallback: true));
        AssertNoFailures(failures, width, height);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(65535u, 0u)]
    [InlineData(0xABCD_0000u, 0x1234_FFFFu)]
    [InlineData(0xABCD_FFF1u, 0x1234_0000u)]
    public void ZeroUwordDimensionsDeclineBeforeAllocation(uint width, uint height)
    {
        using var fixture = new Fixture();
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocRaster, width, height);
        var failures = new List<string>();
        Check(failures, "no allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "no release", () => Assert.Empty(fixture.Frees));
        Check(failures, "no publication", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "captured-width fallback", () => AssertReturn(result, width, expectFallback: true));
        AssertNoFailures(failures, width, height);
    }

    private static uint RequestedSize(uint width, uint height)
    {
        // Independent arithmetic oracle: UWORD arguments, widened before
        // rounding to a whole sixteen-pixel word. No native opcode decoding.
        var wordWidth = width & ushort.MaxValue;
        var wordHeight = height & ushort.MaxValue;
        var wordsPerRow = (wordWidth + 15u) / 16u;
        return checked(wordsPerRow * 2u * wordHeight + PrefixBytes);
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertNoFailures(List<string> failures, uint width, uint height)
        => Assert.True(failures.Count == 0,
            $"D0={width:X8}, D1={height:X8}:\n" + string.Join("\n", failures));

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
    }

    private static void AssertReturn(CallResult call, uint expectedD0, bool expectFallback)
    {
        var differences = call.RegisterDifferences.ToList();
        if (call.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {call.Value:X8}");
        if (call.UsedFallback != expectFallback)
            differences.Add($"private fallback expected {expectFallback}, observed {call.UsedFallback}");
        if (call.StackPointer != call.CallerStackPointer)
            differences.Add($"SP expected {call.CallerStackPointer:X8}, actual {call.StackPointer:X8}");
        if (call.ProgramCounter != call.CallerReturnAddress)
            differences.Add($"PC expected {call.CallerReturnAddress:X8}, actual {call.ProgramCounter:X8}");
        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer, string[] RegisterDifferences);

    private sealed class Fixture : IDisposable
    {
        private const uint CodeAddress = 0x0040_0000;
        private const uint GraphicsBase = 0x0070_0000;
        private const uint ResidentAddress = 0x0072_0000;
        private const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint ReturnAddress = CallerAddress + 4;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private const uint HeapAddress = 0x00D0_0000;
        private const int HeapSize = 0x5000;
        private const int LowMemorySize = 0x100;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505, GraphicsBase
        };
        private readonly IM68kCore _cpu;

        internal Fixture()
        {
            var entries = Image.Value.Entries.ToDictionary(entry => entry.Key,
                entry => CodeAddress + (uint)entry.Value);
            var fallback = CodeAddress + (uint)Image.Value.Fallback;
            var library = NativeGraphicsLibraryImageBuilder.Build(
                GraphicsBase, ResidentAddress, fallback, entries, fallback);
            Assert.True((ulong)CodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
            Bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
            Bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
            var heap = new byte[HeapSize];
            var low = new byte[LowMemorySize];
            Array.Fill(heap, (byte)0xA5);
            Array.Fill(low, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            Bus.MapWritableMemory(0, low);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((AllocationResult, size, flags));
                // NULL probes record the half-gigabyte request but never
                // map or clear it. Successful payloads need at most 16 KiB.
                if (AllocationResult != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - AllocationResult);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        Bus.ClearMemory(AllocationResult, checked((int)size));
                }
                state.D[0] = AllocationResult;
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFree = CaptureMemory();
                PoisonVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal uint AllocationResult { get; set; } = AllocationBase;
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal MemorySnapshot? MemoryAtFree { get; private set; }
        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void AssertHeader(uint width, uint height, uint requestedBytes)
        {
            Assert.Equal((ushort)0x5253, Bus.ReadWord(AllocationBase));
            Assert.Equal((ushort)width, Bus.ReadWord(AllocationBase + 2));
            Assert.Equal((ushort)height, Bus.ReadWord(AllocationBase + 4));
            Assert.Equal(requestedBytes, Bus.ReadLong(AllocationBase + 6));
        }

        internal void AssertPayloadAndPadding(uint requestedBytes)
        {
            var payload = ReadBytes(Raster, checked((int)(requestedBytes - PrefixBytes)));
            var firstUncleared = Array.FindIndex(payload, value => value != 0);
            Assert.True(firstUncleared < 0,
                $"Payload byte {firstUncleared} was not cleared in the {payload.Length}-byte raster.");
            var occupiedBytes = (requestedBytes + 7u) / 8u * 8u;
            Assert.All(ReadBytes(AllocationBase + requestedBytes, (int)(occupiedBytes - requestedBytes)),
                value => Assert.Equal((byte)0xA5, value));
        }

        internal void AssertOutsideAllocationUnchanged(MemorySnapshot before, uint requestedBytes)
        {
            var after = CaptureMemory();
            var start = (int)(AllocationBase - HeapAddress);
            var end = start + (int)requestedBytes;
            Assert.Equal(before.Heap.Take(start), after.Heap.Take(start));
            Assert.Equal(before.Heap.Skip(end), after.Heap.Skip(end));
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsBase, after.GraphicsBase);
        }

        private static void PoisonVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, uint width, uint height, uint raster = 0)
        {
            var entry = CodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // actual JSR d16(A6)
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = width;
            _cpu.State.D[1] = height;
            _cpu.State.A[0] = raster;
            _cpu.State.A[1] = 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                _cpu.State.A[index + 2] = AddressCanaries[index];
            _cpu.ExecuteInstruction();
            Assert.Equal(vectorAddress, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, Bus.ReadLong(_cpu.State.A[7]));

            var usedFallback = false;
            var arrivedByBranch = false;
            var enteredBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                var opcode = Bus.ReadWord(pc);
                enteredBody |= pc == entry;
                // Distant fallbacks can be linker-local RTS copies. Their
                // branch entry restores the original full-width D0 value.
                usedFallback |= pc == CodeAddress + (uint)Image.Value.Fallback ||
                    (opcode == 0x4E75 && arrivedByBranch && _cpu.State.D[0] == width);
                uint? branchTarget = null;
                if ((opcode & 0xF000) == 0x6000 && (opcode & 0x0F00) != 0x0100)
                {
                    var displacement = (opcode & 0xFF) == 0
                        ? unchecked((short)Bus.ReadWord(pc + 2)) : unchecked((sbyte)opcode);
                    branchTarget = unchecked((uint)((long)pc + 2 + displacement));
                }
                _cpu.ExecuteInstruction();
                arrivedByBranch = branchTarget == _cpu.State.ProgramCounter;
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;

                Assert.True(enteredBody, "The physical graphics vector did not reach its native body.");
                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    var actual = _cpu.State.D[index + 2];
                    if (actual != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, actual {actual:X8}");
                }
                for (var index = 0; index < AddressCanaries.Length; index++)
                {
                    var actual = _cpu.State.A[index + 2];
                    if (actual != AddressCanaries[index])
                        differences.Add($"A{index + 2} expected {AddressCanaries[index]:X8}, actual {actual:X8}");
                }
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter, _cpu.State.A[7],
                    ReturnAddress, StackPointer, differences.ToArray());
            }
            throw new InvalidOperationException(
                $"{vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
