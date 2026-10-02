using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsColorMapPublicAbiTests
{
    private const uint Count = 3;
    private const uint CapturedFrame = 0xA1B2_C3D4;
    private const uint RejectedCount = 0xABCD_0003;
    private const uint AllocationBase = 0x00D0_0040;
    private const uint ColorMap = AllocationBase + (uint)GraphicsLayouts.NativeColorMapPrivatePrefixSize;
    private const uint RequestedBytes = (uint)GraphicsLayouts.NativeColorMapBaseSize + Count * 4;
    private const uint AllocationFlags = 0x0001_0001; // PUBLIC | CLEAR

    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> PublicColorMapAbiCases()
    {
        var scenarios = new[]
        {
            "get-success", "get-zero", "get-count-rejected", "get-allocation-null",
            "get-allocation-odd", "get-missing-exec", "free-success", "free-null",
            "free-foreign-marker", "free-span-mismatch", "free-table-mismatch", "free-missing-exec"
        };
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in scenarios)
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicColorMapAbiCases))]
    public void PublicColorMapCallsPreserveLibraryAbiAcrossImagesAndExitPaths(
        bool relocated, bool autoInitEntry, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var isGet = scenario.StartsWith("get-", StringComparison.Ordinal);
        var vector = isGet ? GraphicsLvo.GetColorMap : GraphicsLvo.FreeColorMap;
        var d0 = isGet ? Count : CapturedFrame;
        var a0 = isGet || scenario == "free-null" ? 0u : ColorMap;
        if (!isGet && a0 != 0)
            fixture.SeedColorMap();

        switch (scenario)
        {
            case "get-zero":
                d0 = 0;
                break;
            case "get-count-rejected":
                d0 = RejectedCount;
                break;
            case "get-allocation-null":
                fixture.AllocationResult = 0;
                break;
            case "get-allocation-odd":
                fixture.AllocationResult = AllocationBase + 1;
                break;
            case "get-missing-exec":
            case "free-missing-exec":
                fixture.SetExecAvailable(false);
                break;
            case "free-foreign-marker":
                fixture.WriteWord(AllocationBase, 0x464F);
                break;
            case "free-span-mismatch":
                fixture.WriteLong(AllocationBase + 4, RequestedBytes + 4);
                break;
            case "free-table-mismatch":
                fixture.WriteLong(ColorMap + (uint)GraphicsLayouts.ColorMapColorTable,
                    ColorMap + (uint)GraphicsLayouts.ColorMapSize + 2);
                break;
        }

        var before = fixture.CaptureMemory();
        fixture.ExpectedMemoryAtFree = before;
        var call = fixture.Invoke(vector, d0, a0);
        var expectFallback = scenario is not ("get-success" or "get-zero" or "free-success" or "free-null");
        var expectedValue = expectFallback ? d0 : scenario == "get-success" ? ColorMap : 0u;

        // Keep the public-register, Exec-argument, and publication checks
        // independent. A register-frame failure must not hide useful proof
        // that the same invocation allocated/freed the intended envelope.
        var failures = new List<string>();
        void Check(string contract, Action assertion)
        {
            try
            {
                assertion();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failures.Add(contract + ": " + exception.Message);
            }
        }

        Check("public library return", () => AssertPublicReturn(call, expectedValue, expectFallback));
        Check("Exec arguments", () =>
        {
            var allocated = scenario is "get-success" or "get-allocation-null" or "get-allocation-odd";
            var expectedAllocations = allocated
                ? new[] { (fixture.AllocationResult, RequestedBytes, AllocationFlags) }
                : Array.Empty<(uint, uint, uint)>();
            Assert.Equal(expectedAllocations, fixture.Allocations);
            var expectedFrees = scenario switch
            {
                "get-allocation-odd" => new[] { (AllocationBase + 1, RequestedBytes) },
                "free-success" => new[] { (AllocationBase, RequestedBytes) },
                _ => Array.Empty<(uint, uint)>()
            };
            Assert.Equal(expectedFrees, fixture.Frees);
        });
        Check("memory publication", () =>
        {
            if (scenario == "get-success")
            {
                fixture.AssertPublishedColorMap();
                fixture.AssertOutsideAllocationUnchanged(before);
            }
            else
            {
                fixture.AssertMemoryUnchanged(before);
            }
        });

        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void AssertPublicReturn(CallResult call, uint expectedD0, bool expectFallback)
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
        private const uint ExecBase = 0x0077_0000;
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint CallerStackPointer = StackAddress + 0x300;
        private const uint HeapAddress = 0x00D0_0000;
        private const int HeapSize = 0x200;
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
                (autoInitEntry ? "AUTOINIT JSR(A1)" : "JSR d16(A6)");
            uint residentAddress;
            if (relocated)
            {
                var segmentAddresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(Bus, size =>
                {
                    var address = segmentAddresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    Bus.MapWritableMemory(address, new byte[size]);
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(segmentAddresses);
                _graphicsBase = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
                residentAddress = program.SegmentBases[1];
            }
            else
            {
                var absoluteEntries = Image.Value.Entries.ToDictionary(
                    entry => entry.Key, entry => FixedCodeAddress + (uint)entry.Value);
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase, FixedResidentAddress,
                    FixedCodeAddress + (uint)Image.Value.Fallback, absoluteEntries,
                    FixedCodeAddress + (uint)Image.Value.Fallback);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase,
                    "Fixed code overlaps the graphics vector table.");
                Bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                Bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                Bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                Bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                _graphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                residentAddress = library.ResidentAddress;
            }

            // Fixed and relocated code/table mappings all stay above the
            // built-in Chip RAM decoder. AUTOINIT entries remain in logical
            // LVO order, independently of the physical negative-vector order.
            Assert.True(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize >= 0x0020_0000);
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            Assert.Equal((ushort)0x4AFC, Bus.ReadWord(residentAddress));
            var initTable = Bus.ReadLong(residentAddress + 0x16);
            _functionArray = Bus.ReadLong(initTable + 4);

            var heap = new byte[HeapSize];
            var low = new byte[LowMemorySize];
            Array.Fill(heap, (byte)0xA5);
            Array.Fill(low, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            Bus.MapWritableMemory(0, low);
            SetExecAvailable(true);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var address = AllocationResult;
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));
                // A violating odd provider leaves its A5-filled envelope
                // untouched, exposing any publication before validation.
                if (address != 0 && (address & 1) == 0)
                {
                    Assert.InRange(address, HeapAddress, HeapAddress + (uint)HeapSize - 1);
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - address);
                    if ((flags & 0x0001_0000u) != 0)
                        Bus.ClearMemory(address, checked((int)size));
                }
                state.D[0] = address;
                PoisonExecVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                if (ExpectedMemoryAtFree is { } expected)
                    AssertMemoryUnchanged(expected);
                PoisonExecVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal uint AllocationResult { get; set; } = AllocationBase;
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal MemorySnapshot? ExpectedMemoryAtFree { get; set; }

        internal void SetExecAvailable(bool available) => Bus.WriteLong(4, available ? ExecBase : 0);
        internal void WriteWord(uint address, ushort value) => Bus.WriteWord(address, value);
        internal void WriteLong(uint address, uint value) => Bus.WriteLong(address, value);
        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal void SeedColorMap()
        {
            Bus.ClearMemory(AllocationBase, (int)RequestedBytes);
            Bus.WriteWord(AllocationBase, GraphicsLayouts.NativeColorMapMarker);
            Bus.WriteLong(AllocationBase + 4, RequestedBytes);
            Bus.WriteByte(ColorMap + (uint)GraphicsLayouts.ColorMapType, 2, 0);
            Bus.WriteWord(ColorMap + (uint)GraphicsLayouts.ColorMapCount, (ushort)Count);
            Bus.WriteLong(ColorMap + (uint)GraphicsLayouts.ColorMapColorTable,
                ColorMap + (uint)GraphicsLayouts.ColorMapSize);
            Bus.WriteLong(ColorMap + (uint)GraphicsLayouts.ColorMapLowColorBits,
                ColorMap + (uint)GraphicsLayouts.ColorMapSize + Count * 2);
        }

        internal void AssertPublishedColorMap()
        {
            Assert.Equal(GraphicsLayouts.NativeColorMapMarker, Bus.ReadWord(AllocationBase));
            Assert.Equal((ushort)0, Bus.ReadWord(AllocationBase + 2));
            Assert.Equal(RequestedBytes, Bus.ReadLong(AllocationBase + 4));
            Assert.Equal(0u, Bus.ReadLong(AllocationBase + 8));
            Assert.Equal((byte)0, Bus.ReadByte(ColorMap + (uint)GraphicsLayouts.ColorMapFlags));
            Assert.Equal((byte)2, Bus.ReadByte(ColorMap + (uint)GraphicsLayouts.ColorMapType));
            Assert.Equal((ushort)Count, Bus.ReadWord(ColorMap + (uint)GraphicsLayouts.ColorMapCount));
            var highTable = ColorMap + (uint)GraphicsLayouts.ColorMapSize;
            Assert.Equal(highTable, Bus.ReadLong(ColorMap + (uint)GraphicsLayouts.ColorMapColorTable));
            Assert.Equal(highTable + Count * 2, Bus.ReadLong(ColorMap + (uint)GraphicsLayouts.ColorMapLowColorBits));
            Assert.All(ReadBytes(highTable, (int)(Count * 4)), value => Assert.Equal((byte)0, value));
        }

        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(_graphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void AssertMemoryUnchanged(MemorySnapshot expected)
        {
            var actual = CaptureMemory();
            Assert.Equal(expected.Heap, actual.Heap);
            Assert.Equal(expected.Low, actual.Low);
            Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
        }

        internal void AssertOutsideAllocationUnchanged(MemorySnapshot expected)
        {
            var actual = CaptureMemory();
            var beforeHeader = (int)(AllocationBase - HeapAddress);
            var afterTables = beforeHeader + (int)RequestedBytes;
            Assert.Equal(expected.Heap.Take(beforeHeader), actual.Heap.Take(beforeHeader));
            Assert.Equal(expected.Heap.Skip(afterTables), actual.Heap.Skip(afterTables));
            Assert.Equal(expected.Low, actual.Low);
            Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
        }

        private static void PoisonExecVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, uint d0, uint a0)
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            var functionIndex = (-(int)vector / NativeGraphicsLibraryImageBuilder.VectorStubSize) - 1;
            var functionEntry = Bus.ReadLong(_functionArray + (uint)(functionIndex * 4));
            Assert.Equal(entry, functionEntry);

            uint firstTarget;
            uint returnAddress;
            if (_autoInitEntry)
            {
                Bus.WriteWord(CallerAddress, 0x4E91); // JSR (A1)
                firstTarget = functionEntry;
                returnAddress = CallerAddress + 2;
            }
            else
            {
                // The public ABI is literally GfxBase + its negative LVO;
                // never reuse a logical function-array slot as a byte offset.
                firstTarget = checked((uint)((long)_graphicsBase + (int)vector));
                Assert.Equal((ushort)0x4EF9, Bus.ReadWord(firstTarget));
                Assert.Equal(entry, Bus.ReadLong(firstTarget + 2));
                Bus.WriteWord(CallerAddress, 0x4EAE); // JSR d16(A6)
                Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
                returnAddress = CallerAddress + 4;
            }
            Bus.WriteWord(returnAddress, 0x4E71); // caller resumes at this NOP

            _cpu.Reset(CallerAddress, CallerStackPointer);
            _cpu.State.D[0] = d0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = a0;
            _cpu.State.A[1] = _autoInitEntry ? functionEntry : 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                _cpu.State.A[index + 2] = AddressCanaries[index];
            _cpu.State.A[6] = _graphicsBase;

            _cpu.ExecuteInstruction(); // execute the real caller-side JSR
            Assert.Equal(firstTarget, _cpu.State.ProgramCounter);
            Assert.Equal(CallerStackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(returnAddress, Bus.ReadLong(_cpu.State.A[7]));

            var usedFallback = false;
            var arrivedByBranch = false;
            var enteredPublicBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                var opcode = Bus.ReadWord(pc);
                enteredPublicBody |= pc == entry;
                // D0==0 alone cannot distinguish GetColorMap(0)'s native
                // return from fallback. Local fallback copies are entered by
                // a branch directly to RTS with the original D0 restored.
                // A public save/restore frame may surround that private RTS.
                if (pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (opcode == 0x4E75 && arrivedByBranch && _cpu.State.D[0] == d0))
                {
                    usedFallback = true;
                    Assert.Equal(d0, _cpu.State.D[0]);
                }

                var branchTarget = ConditionalOrAlwaysBranchTarget(pc, opcode);
                _cpu.ExecuteInstruction();
                arrivedByBranch = branchTarget == _cpu.State.ProgramCounter;
                if (_cpu.State.ProgramCounter != returnAddress)
                    continue;

                Assert.True(enteredPublicBody, "The public ColorMap entry was never reached.");
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
                if (_cpu.State.A[6] != _graphicsBase)
                    differences.Add($"A6 expected {_graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter, _cpu.State.A[7],
                    returnAddress, CallerStackPointer, differences.ToArray());
            }

            throw new InvalidOperationException(
                $"{Route}, {vector} did not return to caller {returnAddress:X8}: " +
                $"PC={_cpu.State.ProgramCounter:X8}, SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        private uint? ConditionalOrAlwaysBranchTarget(uint pc, ushort opcode)
        {
            if ((opcode & 0xF000) != 0x6000 || (opcode & 0x0F00) == 0x0100) // exclude BSR
                return null;
            var displacement = (opcode & 0x00FF) == 0
                ? unchecked((short)Bus.ReadWord(pc + 2))
                : unchecked((sbyte)opcode);
            return unchecked((uint)((long)pc + 2 + displacement));
        }

        public void Dispose() => _cpu.Dispose();
    }
}
