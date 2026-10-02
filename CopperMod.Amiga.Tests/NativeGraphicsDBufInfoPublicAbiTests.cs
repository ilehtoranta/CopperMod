using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDBufInfoPublicAbiTests
{
    private const uint CapturedD0 = 0xA1B2_C3D4;
    private const uint AllocationBase = 0x00D0_0100;
    private const uint PrefixBytes = 4;
    private const uint Info = AllocationBase + PrefixBytes;
    private const uint RequestedBytes = PrefixBytes + (uint)GraphicsLayouts.DBufInfoSize;
    private const uint ViewPort = 0x00D1_0000;
    private const uint RasInfo = ViewPort + 0x60;
    private const uint HostileA0 = 0xA0A0_A0A1;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> PublicAbiCases()
    {
        var scenarios = new[]
        {
            "alloc-success", "alloc-null-result", "alloc-odd-result", "alloc-missing-exec",
            "alloc-null-viewport", "alloc-odd-viewport", "alloc-wrapping-viewport", "alloc-extended-mode",
            "alloc-null-rasinfo", "alloc-odd-rasinfo", "alloc-wrapping-rasinfo", "alloc-linked-rasinfo",
            "free-owned", "free-null", "free-foreign-marker", "free-odd", "free-short", "free-missing-exec"
        };
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in scenarios)
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void DBufInfoPublicEntriesPreserveCalleeSavedRegistersAcrossImagesAndExitPaths(
        bool relocated, bool autoInitEntry, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var allocating = scenario.StartsWith("alloc-", StringComparison.Ordinal);
        var vector = allocating ? GraphicsLvo.AllocDBufInfo : GraphicsLvo.FreeDBufInfo;
        var a0 = allocating ? ViewPort : HostileA0;
        var a1 = allocating ? 0xA1A1_A1A1u : Info;
        if (!allocating)
            fixture.SeedInfo();
        switch (scenario)
        {
            case "alloc-null-result":
                fixture.AllocationResult = 0;
                break;
            case "alloc-odd-result":
                fixture.AllocationResult = AllocationBase + 1;
                break;
            case "alloc-missing-exec":
            case "free-missing-exec":
                fixture.SetExecAvailable(false);
                break;
            case "alloc-null-viewport":
                a0 = 0;
                break;
            case "alloc-odd-viewport":
                a0 = ViewPort + 1;
                break;
            case "alloc-wrapping-viewport":
                a0 = 0xFFFF_FFDA;
                break;
            case "alloc-extended-mode":
                fixture.WriteByte(ViewPort + (uint)GraphicsLayouts.ViewPortExtendedModes, 1);
                break;
            case "alloc-null-rasinfo":
                fixture.WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, 0);
                break;
            case "alloc-odd-rasinfo":
                fixture.WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, RasInfo + 1);
                break;
            case "alloc-wrapping-rasinfo":
                fixture.WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, 0xFFFF_FFF6);
                break;
            case "alloc-linked-rasinfo":
                fixture.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoNext, RasInfo);
                break;
            case "free-null":
                a1 = 0;
                break;
            case "free-foreign-marker":
                fixture.WriteWord(AllocationBase, 0x464F);
                break;
            case "free-odd":
                a1 = Info + 1;
                break;
            case "free-short":
                a1 = 2;
                break;
        }
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(vector, a0, a1);
        var claimed = scenario is "alloc-success" or "free-owned" or "free-null";
        var expectedD0 = scenario == "alloc-success" ? Info : claimed ? 0u : CapturedD0;
        var failures = new List<string>();

        // Preserve independent allocator/publication evidence in the red
        // frame run; never let the first changed saved register hide it.
        Check(failures, "Exec allocation", () => Assert.Equal(
            scenario is "alloc-success" or "alloc-null-result" or "alloc-odd-result"
                ? new[] { (fixture.AllocationResult, RequestedBytes, AllocationFlags) }
                : Array.Empty<(uint, uint, uint)>(), fixture.Allocations));
        Check(failures, "Exec A1/D0 release", () => Assert.Equal(scenario switch
        {
            "free-owned" => new[] { (AllocationBase, RequestedBytes) },
            "alloc-odd-result" => new[] { (AllocationBase + 1, RequestedBytes) },
            _ => Array.Empty<(uint, uint)>()
        }, fixture.Frees));
        Check(failures, "retirement memory", () => AssertRetirementMemory(fixture, before));
        Check(failures, "publication or rollback", () =>
        {
            if (scenario == "alloc-success")
            {
                fixture.AssertPublishedInfo();
                fixture.AssertOutsideAllocationUnchanged(before);
            }
            else
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
            }
        });
        Check(failures, "public library return", () => AssertPublicReturn(result, expectedD0, !claimed));

        if (scenario == "alloc-success")
        {
            // Use the constructor's original header and returned pointer.
            // A failed Alloc callee-save assertion must not prevent the
            // independent Free callee-save check from executing as well.
            if (!result.UsedFallback && result.Value == Info)
            {
                var beforeFree = fixture.CaptureMemory();
                var freed = fixture.Invoke(GraphicsLvo.FreeDBufInfo, HostileA0, result.Value);
                Check(failures, "roundtrip release", () => Assert.Equal(
                    (AllocationBase, RequestedBytes), Assert.Single(fixture.Frees)));
                Check(failures, "roundtrip allocation count", () => Assert.Single(fixture.Allocations));
                Check(failures, "roundtrip retirement memory", () => AssertRetirementMemory(fixture, beforeFree));
                Check(failures, "roundtrip memory", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
                Check(failures, "roundtrip Free public return", () => AssertPublicReturn(freed, 0, false));
            }
            else
            {
                failures.Add("Roundtrip Free was not attempted because no native DBufInfo pointer was returned.");
            }
        }
        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static byte[] ExpectedEnvelope()
    {
        var expected = new byte[checked((int)RequestedBytes)];
        expected[0] = 0x44;
        expected[1] = 0x42;
        foreach (var message in new[] { GraphicsLayouts.DBufInfoSafeMessage, GraphicsLayouts.DBufInfoDispMessage })
        {
            var length = (int)PrefixBytes + message + GraphicsLayouts.ExecMessageLength;
            expected[length] = (byte)(GraphicsLayouts.ExecMessageSize >> 8);
            expected[length + 1] = (byte)GraphicsLayouts.ExecMessageSize;
        }
        return expected;
    }

    private static void AssertRetirementMemory(Fixture fixture, MemorySnapshot expected)
    {
        Assert.Equal(fixture.Frees.Count, fixture.MemoryAtFrees.Count);
        foreach (var snapshot in fixture.MemoryAtFrees)
            AssertMemoryEqual(expected, snapshot);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.Structures, actual.Structures);
        Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
    }

    private static void AssertPublicReturn(CallResult call, uint expectedD0, bool expectFallback)
    {
        var differences = call.RegisterDifferences.ToList();
        if (call.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {call.Value:X8}");
        if (call.UsedFallback != expectFallback)
            differences.Add($"private fallback expected {expectFallback}, observed {call.UsedFallback}");
        if (call.ProgramCounter != call.CallerReturnAddress)
            differences.Add($"PC expected {call.CallerReturnAddress:X8}, actual {call.ProgramCounter:X8}");
        if (call.StackPointer != call.CallerStackPointer)
            differences.Add($"SP expected {call.CallerStackPointer:X8}, actual {call.StackPointer:X8}");
        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] Structures, byte[] GraphicsBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer, string[] RegisterDifferences);

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
        private const int StructuresSize = 0x100;
        private const int LowMemorySize = 0x100;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505
        };
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
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
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
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.True(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            Assert.Equal((ushort)0x4AFC, Bus.ReadWord(residentAddress));
            _functionArray = Bus.ReadLong(Bus.ReadLong(residentAddress + 0x16) + 4);
            var heap = new byte[HeapSize];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            Bus.MapWritableMemory(ViewPort, new byte[StructuresSize]);
            WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, RasInfo);
            WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
            WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
            for (var index = 0; index < LowMemorySize; index++)
                WriteByte((uint)index, 0xA5);
            SetExecAvailable(true);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((AllocationResult, size, flags));
                if (AllocationResult != 0 && (AllocationResult & 1) == 0)
                {
                    Assert.InRange(AllocationResult, HeapAddress, HeapAddress + (uint)HeapSize - 1);
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
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal uint AllocationResult { get; set; } = AllocationBase;
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal void SetExecAvailable(bool available) => WriteLong(4, available ? ExecBase : 0);
        internal void WriteByte(uint address, byte value) => Bus.WriteByte(address, value, 0);
        internal void WriteWord(uint address, ushort value) => Bus.WriteWord(address, value);
        internal void WriteLong(uint address, uint value) => Bus.WriteLong(address, value);
        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(ViewPort, StructuresSize),
                ReadBytes(_graphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void SeedInfo()
        {
            var envelope = ExpectedEnvelope();
            for (var index = 0; index < envelope.Length; index++)
                WriteByte(AllocationBase + (uint)index, envelope[index]);
        }

        internal void AssertPublishedInfo()
        {
            Assert.Equal(88u, RequestedBytes);
            Assert.Equal(20, GraphicsLayouts.ExecMessageSize);
            Assert.Equal(ExpectedEnvelope(), ReadBytes(AllocationBase, (int)RequestedBytes));
        }

        internal void AssertOutsideAllocationUnchanged(MemorySnapshot before)
        {
            var after = CaptureMemory();
            var start = (int)(AllocationBase - HeapAddress);
            var end = start + (int)RequestedBytes;
            Assert.Equal(before.Heap.Take(start), after.Heap.Take(start));
            Assert.Equal(before.Heap.Skip(end), after.Heap.Skip(end));
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Structures, after.Structures);
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

        internal CallResult Invoke(GraphicsLvo vector, uint a0, uint a1)
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            var functionIndex = (-(int)vector / NativeGraphicsLibraryImageBuilder.VectorStubSize) - 1;
            var functionEntry = Bus.ReadLong(_functionArray + (uint)(functionIndex * 4));
            Assert.Equal(entry, functionEntry);
            var firstTarget = _autoInitEntry ? functionEntry : checked((uint)((long)_graphicsBase + (int)vector));
            var returnAddress = CallerAddress + (_autoInitEntry ? 2u : 4u);
            if (_autoInitEntry)
            {
                Bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2), never consume Free's A1 argument
            }
            else
            {
                Assert.Equal((ushort)0x4EF9, Bus.ReadWord(firstTarget));
                Assert.Equal(entry, Bus.ReadLong(firstTarget + 2));
                Bus.WriteWord(CallerAddress, 0x4EAE); // actual negative-LVO JSR d16(A6)
                Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            }
            Bus.WriteWord(returnAddress, 0x4E71);
            var expectedAddresses = AddressCanaries.ToArray();
            // On the function-pointer route, the entry address itself is
            // A2's unique preservation canary. A1 stays a public argument.
            if (_autoInitEntry)
                expectedAddresses[0] = functionEntry;
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = a0;
            _cpu.State.A[1] = a1;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
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
                // CapturedD0 is nonzero and never a native success result.
                // This observes both exported and linker-local fallback RTS.
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (Bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == CapturedD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != returnAddress)
                    continue;
                Assert.True(enteredBody, "The DBufInfo public entry was not reached.");
                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    var actual = _cpu.State.D[index + 2];
                    if (actual != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, actual {actual:X8}");
                }
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    var actual = _cpu.State.A[index + 2];
                    if (actual != expectedAddresses[index])
                        differences.Add($"A{index + 2} expected {expectedAddresses[index]:X8}, actual {actual:X8}");
                }
                if (_cpu.State.A[6] != _graphicsBase)
                    differences.Add($"A6 expected {_graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                // Full public callee-save proof is not transparent volatile
                // D1/A0/A1 provider-tailchain parity or message-lifecycle proof.
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], returnAddress, StackPointer, differences.ToArray());
            }
            throw new InvalidOperationException(
                $"{Route}, {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
