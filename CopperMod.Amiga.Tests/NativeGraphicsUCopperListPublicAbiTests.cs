using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsUCopperListPublicAbiTests
{
    private const uint CapturedInitD0 = 0xABCD_0001; // existing native admission compares the count WORD
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint CopListBytes = 54;
    private const uint InstructionBytes = 6;
    private const uint HeapAddress = 0x00D0_0000;
    private const int HeapSize = 0x400;
    private const uint CopList = HeapAddress + 0x100;
    private const uint Instructions = HeapAddress + 0x200;
    private const uint OwnerAddress = 0x00D2_0000;
    private const int OwnerSize = 0x100;
    private const uint UserList = OwnerAddress + 0x40;
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
            "init-success", "init-first-allocation-failure", "init-second-allocation-failure",
            "init-null", "init-odd", "init-count-zero", "init-count-two", "init-linked",
            "init-missing-exec", "init-odd-exec",
            "free-owned", "free-null", "free-empty", "free-odd", "free-foreign-marker",
            "free-null-owner", "free-odd-instructions", "free-first-link-mismatch",
            "free-current-link-mismatch", "free-missing-exec"
        };
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in scenarios)
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void UserCopperPublicEntriesPreserveSavedRegistersAcrossImagesAndExitPaths(bool relocated, string scenario)
    {
        using var fixture = new Fixture(relocated);
        var initializing = scenario.StartsWith("init-", StringComparison.Ordinal);
        var vector = initializing ? GraphicsLvo.UCopperListInit : GraphicsLvo.FreeCopList;
        var argument = initializing ? UserList : CopList;
        var originalD0 = initializing ? CapturedInitD0 : CapturedFreeD0;
        if (!initializing && scenario != "free-empty")
            fixture.SeedOwnedList();
        switch (scenario)
        {
            case "init-first-allocation-failure":
                fixture.FailAllocationNumber = 1;
                break;
            case "init-second-allocation-failure":
                fixture.FailAllocationNumber = 2;
                break;
            case "init-null":
            case "free-null":
                argument = 0;
                break;
            case "init-odd":
            case "free-odd":
                argument++;
                break;
            case "init-count-zero":
                originalD0 = 0xABCD_0000;
                break;
            case "init-count-two":
                originalD0 = 0xABCD_0002;
                break;
            case "init-linked":
                fixture.WriteLong(UserList + (uint)GraphicsLayouts.UCopListFirstCopList, CopList);
                break;
            case "init-missing-exec":
            case "free-missing-exec":
                fixture.SetExecBase(0);
                break;
            case "init-odd-exec":
                fixture.SetExecBase(Fixture.ExecBase + 1);
                break;
            case "free-empty":
                fixture.SeedBytes(CopList, new byte[(int)CopListBytes]);
                break;
            case "free-foreign-marker":
                fixture.WriteWord(CopList + (uint)GraphicsLayouts.CopListFlags, 0x464F);
                break;
            case "free-null-owner":
                fixture.WriteLong(CopList + (uint)GraphicsLayouts.CopListSystem, 0);
                break;
            case "free-odd-instructions":
                fixture.WriteLong(CopList + (uint)GraphicsLayouts.CopListCopIns, Instructions + 1);
                break;
            case "free-first-link-mismatch":
                fixture.WriteLong(UserList + (uint)GraphicsLayouts.UCopListFirstCopList, 0);
                break;
            case "free-current-link-mismatch":
                fixture.WriteLong(UserList + (uint)GraphicsLayouts.UCopListCopList, 0);
                break;
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(vector, argument, originalD0);
        var failures = new List<string>();
        var firstBlockCleared = WithHeapBytes(before, CopList, new byte[(int)CopListBytes]);
        var expectedAllocations = scenario switch
        {
            "init-success" => new[]
            {
                (CopList, CopListBytes, AllocationFlags), (Instructions, InstructionBytes, AllocationFlags)
            },
            "init-first-allocation-failure" => new[] { (0u, CopListBytes, AllocationFlags) },
            "init-second-allocation-failure" => new[]
            {
                (CopList, CopListBytes, AllocationFlags), (0u, InstructionBytes, AllocationFlags)
            },
            _ => Array.Empty<(uint, uint, uint)>()
        };
        Check(failures, "Exec AllocMem D0/D1", () => Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "no publication before both allocations", () => AssertSnapshots(scenario switch
        {
            "init-success" or "init-second-allocation-failure" => new[] { before, firstBlockCleared },
            "init-first-allocation-failure" => new[] { before },
            _ => Array.Empty<MemorySnapshot>()
        }, fixture.MemoryAtAllocations));
        Check(failures, "Exec FreeMem A1/D0", () => Assert.Equal(scenario switch
        {
            "free-owned" => new[] { (Instructions, InstructionBytes), (CopList, CopListBytes) },
            "init-second-allocation-failure" => new[] { (CopList, CopListBytes) },
            _ => Array.Empty<(uint, uint)>()
        }, fixture.Frees));
        Check(failures, "retirement publication boundaries", () => AssertSnapshots(scenario switch
        {
            "free-owned" => new[] { before, WithOwnerLinks(before, 0) },
            "init-second-allocation-failure" => new[] { firstBlockCleared },
            _ => Array.Empty<MemorySnapshot>()
        }, fixture.MemoryAtFrees));
        var expectedMemory = scenario switch
        {
            "init-success" => PublishedMemory(before),
            "init-second-allocation-failure" => firstBlockCleared,
            "free-owned" => WithOwnerLinks(before, 0),
            _ => before
        };
        Check(failures, "complete header, links, buffers, and guards", () =>
            AssertMemoryEqual(expectedMemory, fixture.CaptureMemory()));

        // Keep allocator and publication evidence independent of the first
        // changed saved register; the red frame gate reports both together.
        var claimed = scenario is "init-success" or "free-owned" or "free-null" or "free-empty";
        Check(failures, "public callee-save, caller, and fallback", () => AssertPublicReturn(result, !claimed));
        var expectedD0 = scenario == "init-success" ? CopList : claimed ? 0u : originalD0;
        Check(failures, initializing ? "UCopperListInit D0 result" : "FreeCopList local D0 convention (VOID ABI)",
            () => Assert.Equal(expectedD0, result.Value));

        if (scenario == "init-success")
        {
            if (!result.UsedFallback && result.Value == CopList)
            {
                // Consume the constructor's actual pointer and header,
                // even when its register checks failed. Each separate
                // caller supplies GfxBase; no guest header is repaired.
                var freed = fixture.Invoke(GraphicsLvo.FreeCopList, result.Value, CapturedFreeD0);
                Check(failures, "roundtrip releases", () => Assert.Equal(new[]
                {
                    (Instructions, InstructionBytes), (CopList, CopListBytes)
                }, fixture.Frees));
                Check(failures, "roundtrip allocation count", () => Assert.Equal(expectedAllocations, fixture.Allocations));
                Check(failures, "roundtrip retirement boundaries", () => AssertSnapshots(new[]
                {
                    expectedMemory, WithOwnerLinks(expectedMemory, 0)
                }, fixture.MemoryAtFrees));
                Check(failures, "roundtrip memory", () =>
                    AssertMemoryEqual(WithOwnerLinks(expectedMemory, 0), fixture.CaptureMemory()));
                Check(failures, "roundtrip Free public return", () => AssertPublicReturn(freed, expectFallback: false));
                Check(failures, "roundtrip Free local D0 convention (VOID ABI)", () => Assert.Equal(0u, freed.Value));
            }
            else
            {
                failures.Add("Roundtrip Free could not run because no native CopList pointer was returned.");
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

    private static void AssertPublicReturn(CallResult call, bool expectFallback)
    {
        var differences = call.RegisterDifferences.ToList();
        if (call.UsedFallback != expectFallback)
            differences.Add($"private fallback expected {expectFallback}, observed {call.UsedFallback}");
        if (call.ProgramCounter != call.CallerReturnAddress)
            differences.Add($"PC expected {call.CallerReturnAddress:X8}, actual {call.ProgramCounter:X8}");
        if (call.StackPointer != call.CallerStackPointer)
            differences.Add($"SP expected {call.CallerStackPointer:X8}, actual {call.StackPointer:X8}");
        Assert.True(differences.Count == 0, string.Join("; ", differences));
        // FreeCopList is documented VOID(A0), with no D0 result contract:
        // https://d0.se/autodocs/graphics.library/FreeCopList
        // Native zero / captured-D0-on-decline is checked separately as
        // this implementation's local convention, not public ABI law.
    }

    private static byte[] ExpectedCopList()
    {
        Assert.Equal((int)CopListBytes, GraphicsLayouts.CopListSize);
        Assert.Equal((int)InstructionBytes, GraphicsLayouts.CopInsSize);
        Assert.Equal(12, GraphicsLayouts.UCopListSize);
        var header = new byte[(int)CopListBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListSystem), UserList);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListCopIns), Instructions);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListCopPtr), Instructions);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(GraphicsLayouts.CopListMaxCount), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(GraphicsLayouts.CopListFlags), 0x5543);
        return header;
    }

    private static MemorySnapshot PublishedMemory(MemorySnapshot before)
        => WithOwnerLinks(WithHeapBytes(WithHeapBytes(before, CopList, ExpectedCopList()),
            Instructions, new byte[(int)InstructionBytes]), CopList);

    private static MemorySnapshot WithHeapBytes(MemorySnapshot source, uint address, byte[] bytes)
    {
        var heap = source.Heap.ToArray();
        bytes.CopyTo(heap, checked((int)(address - HeapAddress)));
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithOwnerLinks(MemorySnapshot source, uint copList)
    {
        var owner = source.Owner.ToArray();
        var fields = owner.AsSpan((int)(UserList - OwnerAddress), GraphicsLayouts.UCopListSize);
        fields.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(fields[GraphicsLayouts.UCopListFirstCopList..], copList);
        BinaryPrimitives.WriteUInt32BigEndian(fields[GraphicsLayouts.UCopListCopList..], copList);
        return source with { Owner = owner };
    }

    private static void AssertSnapshots(IReadOnlyList<MemorySnapshot> expected, IReadOnlyList<MemorySnapshot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
            AssertMemoryEqual(expected[index], actual[index]);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Owner, actual.Owner);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Owner, byte[] Low, byte[] GraphicsImage);
    private sealed record CallResult(uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer, string[] RegisterDifferences);

    private sealed class Fixture : IDisposable
    {
        internal const uint ExecBase = 0x0077_0000;
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint ReturnAddress = CallerAddress + 4;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
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
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;

        internal Fixture(bool relocated)
        {
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/JSR d16(A6)";
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
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(item => item.Key,
                    item => FixedCodeAddress + (uint)item.Value);
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
            }
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.True(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            MapCanaries(HeapAddress, HeapSize);
            MapCanaries(OwnerAddress, OwnerSize);
            Bus.ClearMemory(UserList, GraphicsLayouts.UCopListSize);
            for (var index = 0; index < 0x100; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            SetExecBase(ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var number = Allocations.Count + 1;
                var address = number == FailAllocationNumber ? 0u : number switch
                {
                    1 => CopList,
                    2 => Instructions,
                    _ => 0u
                };
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());
                if (address != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - address);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        Bus.ClearMemory(address, checked((int)size));
                }
                state.D[0] = address;
                PoisonVolatile(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal int FailAllocationNumber { get; set; }
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();

        private void MapCanaries(uint address, int count)
        {
            var bytes = new byte[count];
            Array.Fill(bytes, (byte)0xA5);
            Bus.MapWritableMemory(address, bytes);
        }

        internal void SetExecBase(uint value) => WriteLong(4, value);
        internal void WriteWord(uint address, ushort value) => Bus.WriteWord(address, value);
        internal void WriteLong(uint address, uint value) => Bus.WriteLong(address, value);
        internal void SeedBytes(uint address, byte[] bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
                Bus.WriteByte(address + (uint)index, bytes[index], 0);
        }

        internal void SeedOwnedList()
        {
            SeedBytes(CopList, ExpectedCopList());
            SeedBytes(Instructions, new byte[(int)InstructionBytes]);
            WriteLong(UserList + (uint)GraphicsLayouts.UCopListNext, 0);
            WriteLong(UserList + (uint)GraphicsLayouts.UCopListFirstCopList, CopList);
            WriteLong(UserList + (uint)GraphicsLayouts.UCopListCopList, CopList);
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(OwnerAddress, OwnerSize), ReadBytes(0, 0x100),
                ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        private static void PoisonVolatile(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, uint argument, uint originalD0)
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)_graphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // actual public JSR d16(A6)
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = originalD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = argument;
            _cpu.State.A[1] = 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                _cpu.State.A[index + 2] = AddressCanaries[index];
            _cpu.State.A[6] = _graphicsBase;
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
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (opcode == 0x4E75 && arrivedByBranch && _cpu.State.D[0] == originalD0);
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
                Assert.True(enteredBody, "The public vector did not enter the requested native body.");
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
                // Callee-save parity does not assert transparent volatile
                // D1/A0/A1 provider handoff, a separate deferred contract.
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], ReturnAddress, StackPointer, differences.ToArray());
            }
            throw new InvalidOperationException(
                $"{Route}, {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
