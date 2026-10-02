using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsUCopperListAllocationSpanTests
{
    // Activate after the independent Exec-call and public-frame prerequisites.
    // Keep the existing count-WORD admission; this unit only validates the two
    // allocation results before the constructor publishes any guest links.
    // Exact-end mocks prove logical span arithmetic, not allocation provenance
    // or a real machine's ability to allocate at these high logical addresses.
    private const uint CapturedInitD0 = 0xABCD_0001;
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint CopListBytes = 54;
    private const uint InstructionBytes = 6;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x400;
    private const uint OrdinaryCopList = ArenaAddress + 0x100;
    private const uint OrdinaryInstructions = ArenaAddress + 0x200;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const int HighSize = 0x100;
    private const int LowSize = 0x100;
    private const uint OwnerAddress = 0x00D2_0000;
    private const int OwnerSize = 0x100;
    private const uint UserList = OwnerAddress + 0x40;
    private const uint PhysicalMask = 0x00FF_FFFF;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> AllocationCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary", "coplist-exact-end", "instructions-exact-end",
            "first-null", "first-odd", "first-wrap",
            "second-null", "second-odd", "second-wrap"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(AllocationCases))]
    public void UserCopperConstructorValidatesBothAllocationSpansBeforePublication(bool relocated, string scenario)
    {
        var copList = scenario switch
        {
            "coplist-exact-end" => 0xFFFF_FFCAu,
            "first-null" => 0u,
            "first-odd" => OrdinaryCopList + 1,
            "first-wrap" => 0xFFFF_FFCCu,
            _ => OrdinaryCopList
        };
        var instructions = scenario switch
        {
            "instructions-exact-end" => 0xFFFF_FFFAu,
            "second-null" => 0u,
            "second-odd" => OrdinaryInstructions + 1,
            "second-wrap" => 0xFFFF_FFFCu,
            _ => OrdinaryInstructions
        };
        var firstRejected = scenario.StartsWith("first-", StringComparison.Ordinal);
        var secondRejected = scenario.StartsWith("second-", StringComparison.Ordinal);
        var succeeds = !firstRejected && !secondRejected;
        Assert.Equal(!firstRejected, IsWordAlignedNonwrapping(copList, CopListBytes));
        Assert.Equal(!secondRejected, IsWordAlignedNonwrapping(instructions, InstructionBytes));
        if (scenario == "coplist-exact-end")
            Assert.Equal((ulong)uint.MaxValue, (ulong)copList + CopListBytes - 1);
        if (scenario == "instructions-exact-end")
            Assert.Equal((ulong)uint.MaxValue, (ulong)instructions + InstructionBytes - 1);
        if (scenario == "first-wrap")
            Assert.Equal(0x1_0000_0001UL, (ulong)copList + CopListBytes - 1);
        if (scenario == "second-wrap")
            Assert.Equal(0x1_0000_0001UL, (ulong)instructions + InstructionBytes - 1);

        using var fixture = new Fixture(relocated, copList, instructions);
        var before = fixture.CaptureMemory();
        var firstCleared = firstRejected ? before : WithAllocationBytes(before, copList, new byte[(int)CopListBytes]);
        var bothCleared = succeeds ? WithAllocationBytes(firstCleared, instructions, new byte[(int)InstructionBytes]) : firstCleared;
        var failures = new List<string>();
        CallResult? initialized = null;
        Check(failures, "native constructor execution", () =>
            initialized = fixture.Invoke(GraphicsLvo.UCopperListInit, UserList, CapturedInitD0));

        var expectedAllocations = firstRejected
            ? new[] { (copList, CopListBytes, AllocationFlags) }
            : new[]
            {
                (copList, CopListBytes, AllocationFlags), (instructions, InstructionBytes, AllocationFlags)
            };
        Check(failures, "exact Exec allocations; rejected first result never requests slot two", () =>
            Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "unpublished state at every AllocMem entry", () => AssertSnapshots(
            firstRejected ? new[] { before } : new[] { before, firstCleared }, fixture.MemoryAtAllocations));
        Check(failures, "explicit mock CLEAR behavior", () => AssertSnapshots(
            firstRejected ? new[] { before } : new[] { firstCleared, bothCleared }, fixture.MemoryAfterAllocations));

        var expectedCleanup = scenario switch
        {
            "first-odd" or "first-wrap" => new[] { (copList, CopListBytes) },
            "second-null" => new[] { (copList, CopListBytes) },
            "second-odd" or "second-wrap" => new[]
            {
                (instructions, InstructionBytes), (copList, CopListBytes)
            },
            _ => Array.Empty<(uint, uint)>()
        };
        Check(failures, "raw Exec FreeMem A1/D0 cleanup order", () => Assert.Equal(expectedCleanup, fixture.Frees));
        Check(failures, "no owner or header publication before rollback", () => AssertSnapshots(
            Enumerable.Repeat(firstCleared, expectedCleanup.Length).ToArray(), fixture.MemoryAtFrees));
        var expectedMemory = succeeds
            ? WithOwnerLinks(WithAllocationBytes(bothCleared, copList, ExpectedCopList(instructions)), copList)
            : firstCleared;
        Check(failures, "complete owner, headers, buffers, low aliases, and guards", () =>
            AssertMemoryEqual(expectedMemory, fixture.CaptureMemory()));
        Check(failures, "SysBase survives the constructor", fixture.AssertSysBaseUnchanged);
        if (initialized is { } initialReturn)
            Check(failures, "constructor caller and unchanged count policy", () =>
                AssertReturn(initialReturn, succeeds ? copList : CapturedInitD0, expectFallback: !succeeds));

        if (succeeds)
        {
            if (initialized is { UsedFallback: false } constructed && constructed.Value == copList)
            {
                // Use the returned pointer and constructor-produced header as
                // they are. Exact-end cases are separate, disjoint allocations;
                // no header repair or logical-pointer normalization occurs.
                CallResult? freed = null;
                Check(failures, "native unchanged-constructor Free execution", () =>
                    freed = fixture.Invoke(GraphicsLvo.FreeCopList, constructed.Value, CapturedFreeD0));
                Check(failures, "roundtrip raw logical releases", () => Assert.Equal(new[]
                {
                    (instructions, InstructionBytes), (copList, CopListBytes)
                }, fixture.Frees));
                Check(failures, "roundtrip allocation count", () => Assert.Equal(expectedAllocations, fixture.Allocations));
                var clearedOwner = WithOwnerLinks(expectedMemory, 0);
                Check(failures, "instruction release precedes three-link clear and CopList release", () =>
                    AssertSnapshots(new[] { expectedMemory, clearedOwner }, fixture.MemoryAtFrees));
                Check(failures, "roundtrip header and guard preservation", () =>
                    AssertMemoryEqual(clearedOwner, fixture.CaptureMemory()));
                Check(failures, "SysBase survives the roundtrip", fixture.AssertSysBaseUnchanged);
                if (freed is { } freeReturn)
                    Check(failures, "Free caller and local VOID-vector D0 convention", () =>
                        AssertReturn(freeReturn, 0, expectFallback: false));
            }
            else
            {
                failures.Add("No native CopList pointer returned; the unmodified constructor-to-Free control could not run.");
            }
        }

        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static uint Physical(uint address) => address & PhysicalMask;

    private static bool IsWordAlignedNonwrapping(uint address, uint size)
        => address != 0 && (address & 1u) == 0 && size != 0 &&
            (ulong)address + size - 1 <= uint.MaxValue;

    private static byte[] ExpectedCopList(uint instructions)
    {
        Assert.Equal((int)CopListBytes, GraphicsLayouts.CopListSize);
        Assert.Equal((int)InstructionBytes, GraphicsLayouts.CopInsSize);
        Assert.Equal(12, GraphicsLayouts.UCopListSize);
        var header = new byte[(int)CopListBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListSystem), UserList);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListCopIns), instructions);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListCopPtr), instructions);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(GraphicsLayouts.CopListMaxCount), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(GraphicsLayouts.CopListFlags), 0x5543);
        return header;
    }

    private static MemorySnapshot WithAllocationBytes(MemorySnapshot source, uint logicalAddress, byte[] bytes)
    {
        // Translate the location of expected memory only, never a pointer value
        // stored in the header or passed across an Exec/library ABI boundary.
        var address = Physical(logicalAddress);
        if (address >= ArenaAddress && (ulong)address + (uint)bytes.Length <= ArenaAddress + ArenaSize)
        {
            var arena = source.Arena.ToArray();
            bytes.CopyTo(arena, checked((int)(address - ArenaAddress)));
            return source with { Arena = arena };
        }
        if (address >= HighPhysicalAddress && (ulong)address + (uint)bytes.Length <= 0x0100_0000UL)
        {
            var high = source.High.ToArray();
            bytes.CopyTo(high, checked((int)(address - HighPhysicalAddress)));
            return source with { High = high };
        }
        throw new InvalidOperationException("Expected allocation bytes escaped the bounded fixture memory.");
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
        Assert.Equal(expected.Arena, actual.Arena);
        Assert.Equal(expected.High, actual.High);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.Owner, actual.Owner);
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertReturn(CallResult call, uint expectedD0, bool expectFallback)
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
        // FreeCopList is VOID; zero on local success and captured D0 on decline
        // are this emitter's conventions, not an additional public ABI result.
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Arena, byte[] High, byte[] Low, byte[] Owner, byte[] GraphicsImage);
    private sealed record CallResult(uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
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

        internal Fixture(bool relocated, uint firstAllocation, uint secondAllocation)
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
            MapCanaries(ArenaAddress, ArenaSize, 0xA5);
            MapCanaries(HighPhysicalAddress, HighSize, 0xB7);
            MapCanaries(0, LowSize, 0xC3);
            MapCanaries(OwnerAddress, OwnerSize, 0xD6);
            Bus.ClearMemory(UserList, GraphicsLayouts.UCopListSize);
            Bus.WriteLong(4, ExecBase);
            AssertSysBaseUnchanged();
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var address = Allocations.Count switch
                {
                    0 => firstAllocation,
                    1 => secondAllocation,
                    _ => 0u
                };
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());

                // Valid mock results honor the actual CLEAR bit and requested
                // byte count. A malformed allocator deliberately returns an
                // untouched odd/wrapping pointer. It must not itself clear
                // low aliases, damage SysBase, or fabricate native publication.
                // The before/after snapshots independently assert this choice.
                if (IsWordAlignedNonwrapping(address, size) &&
                    (flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                {
                    Assert.InRange(size, 1u, CopListBytes);
                    for (var offset = 0u; offset < size; offset++)
                        Bus.WriteByte(Physical(address + offset), 0, 0);
                }
                MemoryAfterAllocations.Add(CaptureMemory());
                state.D[0] = address; // keep the full 32-bit logical pointer
                PoisonVolatile(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0])); // actual Exec ABI; do not mask or repair
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAfterAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();

        private void MapCanaries(uint address, int count, byte value)
        {
            var bytes = new byte[count];
            Array.Fill(bytes, value);
            Bus.MapWritableMemory(address, bytes);
            for (var index = 0; index < count; index++)
                Bus.WriteByte(address + (uint)index, value, 0);
            Assert.Equal(value, Bus.ReadByte(address));
            Assert.Equal(value, Bus.ReadByte(address + (uint)count - 1));
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => Bus.ReadByte(Physical(unchecked(address + (uint)index)))).ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, HighSize),
                ReadBytes(0, LowSize), ReadBytes(OwnerAddress, OwnerSize),
                ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void AssertSysBaseUnchanged() => Assert.Equal(ExecBase, Bus.ReadLong(4));

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
                Assert.True(enteredBody, "The public vector did not enter its requested native body.");
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
                // These memory-backed checks establish no mutation, not no
                // reads. They do not prove allocation provenance, mapping on
                // real hardware, or transparent volatile-register handoff.
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
