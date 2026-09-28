using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDBufInfoInputRegisterTests
{
    private const uint CapturedD0 = 0xA1B2_C3D4;
    private const uint OwnedInfo = 0x00D0_0044;
    private const uint ForeignInfo = 0x00D0_0104;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> InputCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "null-vs-owned", "null-vs-odd", "odd-vs-null", "foreign-vs-null",
            "short-vs-null", "owned-vs-null-without-exec", "foreign-vs-owned"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(InputCases))]
    public void FreeDBufInfoUsesA1AndIgnoresDistinctA0OnNonRetiringPaths(
        bool relocated, bool autoInitEntry, string scenario)
    {
        // The original AutoDoc specifies A1 and a NULL no-op:
        // https://d0.se/autodocs/graphics.library/FreeDBufInfo
        // This gate isolates input routing. Valid native Exec retirement and
        // preservation of all library-saved registers have separate gates.
        using var fixture = new Fixture(relocated, autoInitEntry);
        var (a0, a1) = scenario switch
        {
            "null-vs-owned" => (OwnedInfo, 0u),
            "null-vs-odd" => (OwnedInfo + 1, 0u),
            "odd-vs-null" => (0u, OwnedInfo + 1),
            "foreign-vs-null" => (0u, ForeignInfo),
            "short-vs-null" => (0u, 2u),
            "owned-vs-null-without-exec" => (0u, OwnedInfo),
            "foreign-vs-owned" => (OwnedInfo, ForeignInfo),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        if (scenario == "owned-vs-null-without-exec")
            fixture.SetExecAvailable(false);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(a0, a1);
        var failures = new List<string>();
        Check(failures, "no allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "no native retirement", () => Assert.Empty(fixture.Frees));
        Check(failures, "no memory mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "memory at any erroneous retirement", () =>
        {
            foreach (var snapshot in fixture.MemoryAtFrees)
                AssertMemoryEqual(before, snapshot);
        });
        Check(failures, "public return", () =>
        {
            Assert.Equal(a1 == 0 ? 0u : CapturedD0, result.Value);
            Assert.Equal(a1 != 0, result.UsedFallback);
            Assert.Equal(result.CallerReturnAddress, result.ProgramCounter);
            Assert.Equal(result.CallerStackPointer, result.StackPointer);
        });
        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}, A0={a0:X8}, A1={a1:X8}:\n" + string.Join("\n", failures));
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer);

    private sealed class Fixture : IDisposable
    {
        private const uint ExecBase = 0x0077_0000;
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private const uint HeapAddress = 0x00D0_0000;
        private const int HeapSize = 0x200;
        private const int LowMemorySize = 0x100;
        private readonly IM68kCore _cpu;
        private readonly bool _autoInitEntry;
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;
        private readonly uint _functionArray;

        internal Fixture(bool relocated, bool autoInitEntry)
        {
            _autoInitEntry = autoInitEntry;
            Route = $"{(relocated ? "relocated HUNK" : "fixed image")}/" +
                (autoInitEntry ? "AUTOINIT JSR(A2)" : "JSR d16(A6)");
            uint residentAddress;
            if (relocated)
            {
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(Bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit);
                    Bus.MapWritableMemory(address, new byte[size]);
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                _graphicsBase = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
                residentAddress = program.SegmentBases[1];
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(entry => entry.Key,
                    entry => FixedCodeAddress + (uint)entry.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                Bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                Bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                Bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                Bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                _graphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                residentAddress = library.ResidentAddress;
            }
            Assert.True(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            Assert.Equal((ushort)0x4AFC, Bus.ReadWord(residentAddress));
            _functionArray = Bus.ReadLong(Bus.ReadLong(residentAddress + 0x16) + 4);
            var heap = new byte[HeapSize];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            for (var index = 0; index < LowMemorySize; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            SetExecAvailable(true);
            // Native ownership is a four-byte prefix, independent of the
            // portable host registry. No native allocation call is needed.
            Bus.WriteWord(OwnedInfo - 4, 0x4442);
            Bus.WriteWord(OwnedInfo - 2, 0);
            Assert.Equal((ushort)0x4442, Bus.ReadWord(OwnedInfo - 4));
            Assert.NotEqual((ushort)0x4442, Bus.ReadWord(ForeignInfo - 4));
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Allocations.Add((state.D[0], state.D[1]));
                state.D[0] = 0;
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                // Every call here is unexpected. Record raw inputs without
                // endorsing the separate, known legacy Exec argument bug.
                Frees.Add((state.A[0], state.A[1], state.D[0], state.D[1]));
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal List<(uint D0, uint D1)> Allocations { get; } = new();
        internal List<(uint A0, uint A1, uint D0, uint D1)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal void SetExecAvailable(bool available) => Bus.WriteLong(4, available ? ExecBase : 0);
        private byte[] ReadBytes(uint address, int size)
            => Enumerable.Range(0, size).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(_graphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        private static void PoisonVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(uint a0, uint a1)
        {
            const GraphicsLvo vector = GraphicsLvo.FreeDBufInfo;
            var entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            var functionIndex = (-(int)vector / NativeGraphicsLibraryImageBuilder.VectorStubSize) - 1;
            var functionEntry = Bus.ReadLong(_functionArray + (uint)(functionIndex * 4));
            Assert.Equal(entry, functionEntry);
            var firstTarget = _autoInitEntry ? functionEntry : checked((uint)((long)_graphicsBase + (int)vector));
            var returnAddress = CallerAddress + (_autoInitEntry ? 2u : 4u);
            if (_autoInitEntry)
            {
                // A1 is the public argument; use another address register
                // for this independent AUTOINIT function-pointer call.
                Bus.WriteWord(CallerAddress, 0x4E92); // JSR (A2)
            }
            else
            {
                Assert.Equal((ushort)0x4EF9, Bus.ReadWord(firstTarget));
                Assert.Equal(entry, Bus.ReadLong(firstTarget + 2));
                Bus.WriteWord(CallerAddress, 0x4EAE); // JSR d16(A6)
                Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            }
            Bus.WriteWord(returnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.A[0] = a0;
            _cpu.State.A[1] = a1;
            _cpu.State.A[2] = functionEntry;
            _cpu.State.A[6] = _graphicsBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(firstTarget, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(returnAddress, Bus.ReadLong(_cpu.State.A[7]));
            var usedFallback = false;
            var enteredBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                enteredBody |= pc == entry;
                // All native claims in this fixture return zero. Preserved
                // nonzero captured D0 distinguishes local/shared fallback RTS.
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (Bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == CapturedD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != returnAddress)
                    continue;
                Assert.True(enteredBody, "FreeDBufInfo's native public entry was not reached.");
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], returnAddress, StackPointer);
            }
            throw new InvalidOperationException(
                $"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
