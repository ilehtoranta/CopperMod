using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsUCopperListExecAbiTests
{
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint CopListBytes = 54;
    private const uint InstructionBytes = 6;
    private const uint HeapAddress = 0x00D0_0000;
    private const int HeapSize = 0x1000;
    private const uint MockCopList = HeapAddress + 0x100;
    private const uint MockInstructions = HeapAddress + 0x200;
    private const uint OwnerMemoryAddress = 0x00D2_0000;
    private const int OwnerMemorySize = 0x100;
    private const uint UserList = OwnerMemoryAddress + 0x40;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "constructor-and-free", "seeded-free", "second-allocation-failure", "first-allocation-failure",
            "null-free", "empty-free", "foreign-marker"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void UserCopperListsUseActualExecFreeArgumentsAndOrderedOwnerPublication(bool relocated, string scenario)
    {
        using var fixture = new Fixture(relocated);
        var initializing = scenario is "constructor-and-free" or "second-allocation-failure" or "first-allocation-failure";
        if (scenario is "seeded-free" or "foreign-marker")
            fixture.SeedOwnedList(MockCopList, MockInstructions);
        if (scenario == "empty-free")
            fixture.SeedBytes(MockCopList, new byte[CopListBytes]);
        if (scenario == "foreign-marker")
            fixture.Bus.WriteWord(MockCopList + (uint)GraphicsLayouts.CopListFlags, 0x464F);
        fixture.FailAllocationNumber = scenario switch
        {
            "first-allocation-failure" => 1,
            "second-allocation-failure" => 2,
            _ => 0
        };

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(initializing ? GraphicsLvo.UCopperListInit : GraphicsLvo.FreeCopList,
            initializing ? UserList : scenario == "null-free" ? 0 : MockCopList);
        var failures = new List<string>();
        var afterFirstClear = WithHeapBytes(before, MockCopList, new byte[CopListBytes]);
        var expectedAllocations = scenario switch
        {
            "constructor-and-free" => new[]
            {
                (MockCopList, CopListBytes, AllocationFlags),
                (MockInstructions, InstructionBytes, AllocationFlags)
            },
            "second-allocation-failure" => new[]
            {
                (MockCopList, CopListBytes, AllocationFlags), (0u, InstructionBytes, AllocationFlags)
            },
            "first-allocation-failure" => new[] { (0u, CopListBytes, AllocationFlags) },
            _ => Array.Empty<(uint, uint, uint)>()
        };
        Check(failures, "Exec AllocMem D0/D1", () => Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "no native publication before both allocations", () =>
        {
            var expectedSnapshots = scenario switch
            {
                "constructor-and-free" or "second-allocation-failure" => new[] { before, afterFirstClear },
                "first-allocation-failure" => new[] { before },
                _ => Array.Empty<MemorySnapshot>()
            };
            AssertSnapshots(expectedSnapshots, fixture.MemoryAtAllocations);
        });

        if (scenario == "constructor-and-free")
        {
            var published = PublishedMemory(before, MockCopList, MockInstructions);
            Check(failures, "constructor header, buffers, links, and guards", () =>
                AssertMemoryEqual(published, fixture.CaptureMemory()));
            Check(failures, "no premature retirement", () => Assert.Empty(fixture.Frees));
            Check(failures, "UCopperListInit caller", () => AssertReturn(result, MockCopList, expectFallback: false));
            if (!result.UsedFallback && result.Value == MockCopList)
            {
                // Feed the constructor's actual header to FreeCopList. No
                // seed, header repair, or argument adapter runs between calls.
                var freed = fixture.Invoke(GraphicsLvo.FreeCopList, result.Value);
                Check(failures, "roundtrip exact Exec releases", () => Assert.Equal(new[]
                {
                    (MockInstructions, InstructionBytes), (MockCopList, CopListBytes)
                }, fixture.Frees));
                Check(failures, "roundtrip retirement boundaries", () => AssertSnapshots(
                    new[] { published, WithOwnerLinks(published, 0) }, fixture.MemoryAtFrees));
                Check(failures, "roundtrip final bytes", () =>
                    AssertMemoryEqual(WithOwnerLinks(published, 0), fixture.CaptureMemory()));
                Check(failures, "roundtrip allocation count", () =>
                    Assert.Equal(expectedAllocations, fixture.Allocations));
                Check(failures, "FreeCopList caller", () => AssertReturn(freed, 0, expectFallback: false));
            }
            else
            {
                failures.Add("No native CopList pointer returned; the unchanged-constructor Free could not run.");
            }
        }
        else
        {
            var expectedFrees = scenario switch
            {
                "seeded-free" => new[] { (MockInstructions, InstructionBytes), (MockCopList, CopListBytes) },
                "second-allocation-failure" => new[] { (MockCopList, CopListBytes) },
                _ => Array.Empty<(uint, uint)>()
            };
            var expectedFinal = scenario switch
            {
                "seeded-free" => WithOwnerLinks(before, 0),
                "second-allocation-failure" => afterFirstClear,
                _ => before
            };
            Check(failures, "Exec FreeMem A1/D0", () => Assert.Equal(expectedFrees, fixture.Frees));
            Check(failures, "retirement publication boundaries", () =>
            {
                var snapshots = scenario switch
                {
                    "seeded-free" => new[] { before, WithOwnerLinks(before, 0) },
                    "second-allocation-failure" => new[] { afterFirstClear },
                    _ => Array.Empty<MemorySnapshot>()
                };
                AssertSnapshots(snapshots, fixture.MemoryAtFrees);
            });
            Check(failures, "final owned bytes and guards", () => AssertMemoryEqual(expectedFinal, fixture.CaptureMemory()));
            var claimed = scenario is "seeded-free" or "null-free" or "empty-free";
            Check(failures, "caller return and fallback", () =>
                AssertReturn(result, claimed ? 0u : initializing ? 1u : CapturedFreeD0, !claimed));
        }
        Assert.True(failures.Count == 0,
            $"{(relocated ? "HUNK" : "fixed")}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserCopperListsRoundtripThroughProductionExecAndReturnBothClassicHeapChunks(bool relocated)
    {
        using var fixture = new Fixture(relocated, productionExec: true);
        var copList = fixture.FirstFreeChunk;
        var instructions = copList + 56; // request54 occupies56; request6 occupies8
        var initialFreeBytes = fixture.FreeBytes;
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.UCopperListInit, UserList);

        AssertReturn(allocated, copList, expectFallback: false);
        Assert.Equal(new[]
        {
            (copList, CopListBytes, AllocationFlags), (instructions, InstructionBytes, AllocationFlags)
        }, fixture.Allocations);
        Assert.Empty(fixture.Frees);
        Assert.Equal(2, fixture.MemoryAtAllocations.Count);
        AssertMemoryEqual(before, fixture.MemoryAtAllocations[0]);
        var beforeSecondAllocation = fixture.MemoryAtAllocations[1];
        Assert.Equal(new byte[CopListBytes], HeapBytes(beforeSecondAllocation, copList, (int)CopListBytes));
        AssertOwner(beforeSecondAllocation, 0);
        AssertFreeChunk(beforeSecondAllocation, instructions, initialFreeBytes - 56);
        AssertNonHeapMemoryEqual(before, beforeSecondAllocation);

        fixture.AssertPublishedList(copList, instructions);
        fixture.AssertSingleFreeChunk(copList + 64, initialFreeBytes - 64);
        var published = fixture.CaptureMemory();
        AssertOwner(published, copList);
        AssertNonHeapMemoryEqual(WithOwnerLinks(before, copList), published);
        for (var index = 54u; index < 56; index++)
            Assert.Equal((byte)0xA5, fixture.Bus.ReadByte(copList + index));
        // This tiny allocation starts on a free-chunk header. Its two
        // unrequested padding bytes retain metadata from before AllocMem,
        // rather than the arena's original A5 pattern.
        for (var index = 6u; index < 8; index++)
            Assert.Equal(beforeSecondAllocation.Heap[checked((int)(instructions + index - HeapAddress))],
                fixture.Bus.ReadByte(instructions + index));

        // These are real ExecMemoryServices calls with real A1/D0. Incorrect
        // arguments are never repaired, skipped, or redirected to a mock.
        var freed = fixture.Invoke(GraphicsLvo.FreeCopList, allocated.Value);
        AssertReturn(freed, 0, expectFallback: false);
        Assert.Equal(new[] { (instructions, InstructionBytes), (copList, CopListBytes) }, fixture.Frees);
        Assert.Equal(2, fixture.Allocations.Count);
        Assert.Equal(2, fixture.MemoryAtFrees.Count);
        AssertMemoryEqual(published, fixture.MemoryAtFrees[0]);
        var beforeSecondFree = fixture.MemoryAtFrees[1];
        AssertOwner(beforeSecondFree, 0); // Next, FirstCopList, and CopList all clear before release54
        Assert.Equal(ExpectedCopList(instructions), HeapBytes(beforeSecondFree, copList, (int)CopListBytes));
        AssertFreeChunk(beforeSecondFree, instructions, initialFreeBytes - 56);
        AssertNonHeapMemoryEqual(WithOwnerLinks(published, 0), beforeSecondFree);
        fixture.AssertSingleFreeChunk(copList, initialFreeBytes);
        AssertNonHeapMemoryEqual(WithOwnerLinks(before, 0), fixture.CaptureMemory());
        // Freed bytes now belong to the classic MemChunk lists. Their
        // metadata changes are legitimate; the caller-owned UCopList is not.
    }

    private static byte[] ExpectedCopList(uint instructions)
    {
        Assert.Equal((int)CopListBytes, GraphicsLayouts.CopListSize);
        Assert.Equal((int)InstructionBytes, GraphicsLayouts.CopInsSize);
        Assert.Equal(12, GraphicsLayouts.UCopListSize);
        var header = new byte[CopListBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListSystem), UserList);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListCopIns), instructions);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(GraphicsLayouts.CopListCopPtr), instructions);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(GraphicsLayouts.CopListMaxCount), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(GraphicsLayouts.CopListFlags), 0x5543);
        return header; // Count, every unconsumed pointer, and reserved words remain clear.
    }

    private static MemorySnapshot PublishedMemory(MemorySnapshot before, uint copList, uint instructions)
        => WithOwnerLinks(WithHeapBytes(WithHeapBytes(before, copList, ExpectedCopList(instructions)),
            instructions, new byte[InstructionBytes]), copList);

    private static MemorySnapshot WithHeapBytes(MemorySnapshot source, uint address, byte[] bytes)
    {
        var heap = source.Heap.ToArray();
        bytes.CopyTo(heap, checked((int)(address - HeapAddress)));
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithOwnerLinks(MemorySnapshot source, uint copList)
    {
        var owner = source.Owner.ToArray();
        var fields = owner.AsSpan((int)(UserList - OwnerMemoryAddress), GraphicsLayouts.UCopListSize);
        fields.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(fields[GraphicsLayouts.UCopListFirstCopList..], copList);
        BinaryPrimitives.WriteUInt32BigEndian(fields[GraphicsLayouts.UCopListCopList..], copList);
        return source with { Owner = owner };
    }

    private static byte[] HeapBytes(MemorySnapshot snapshot, uint address, int size)
        => snapshot.Heap.AsSpan(checked((int)(address - HeapAddress)), size).ToArray();

    private static void AssertOwner(MemorySnapshot snapshot, uint copList)
        => Assert.Equal(WithOwnerLinks(snapshot, copList).Owner, snapshot.Owner);

    private static void AssertFreeChunk(MemorySnapshot snapshot, uint address, uint bytes)
    {
        Assert.Equal(address, BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.First)));
        Assert.Equal(bytes, BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.Free)));
        var chunk = snapshot.Heap.AsSpan(checked((int)(address - HeapAddress)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(chunk[(int)ExecLayout.MemChunk.Next..]));
        Assert.Equal(bytes, BinaryPrimitives.ReadUInt32BigEndian(chunk[(int)ExecLayout.MemChunk.Bytes..]));
    }

    private static void AssertSnapshots(IReadOnlyList<MemorySnapshot> expected, IReadOnlyList<MemorySnapshot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
            AssertMemoryEqual(expected[index], actual[index]);
    }

    private static void AssertNonHeapMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Owner, actual.Owner);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        AssertNonHeapMemoryEqual(expected, actual);
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertReturn(CallResult result, uint expectedD0, bool expectFallback)
    {
        Assert.Equal(expectedD0, result.Value);
        Assert.Equal(expectFallback, result.UsedFallback);
        Assert.Equal(result.CallerReturnAddress, result.ProgramCounter);
        Assert.Equal(result.CallerStackPointer, result.StackPointer);
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Owner, byte[] Low, byte[] GraphicsImage);
    private sealed record CallResult(uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint ReturnAddress = CallerAddress + 4;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private readonly IM68kCore _cpu;
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture(bool relocated, bool productionExec = false)
        {
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
            MapCanaries(OwnerMemoryAddress, OwnerMemorySize);
            Bus.ClearMemory(UserList, GraphicsLayouts.UCopListSize);
            for (var index = 0; index < 0x100; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            if (productionExec)
                InstallProductionExec();
            else
                InstallMockExec();
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        internal AmigaBus Bus { get; } = new();
        internal int FailAllocationNumber { get; set; }
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal uint FirstFreeChunk => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.First);
        internal uint FreeBytes => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.Free);

        private void MapCanaries(uint address, int count)
        {
            var bytes = new byte[count];
            Array.Fill(bytes, (byte)0xA5);
            Bus.MapWritableMemory(address, bytes);
        }

        private void InstallMockExec()
        {
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var number = Allocations.Count + 1;
                var result = number == FailAllocationNumber ? 0u : number switch
                {
                    1 => MockCopList,
                    2 => MockInstructions,
                    _ => 0u
                };
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((result, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());
                if (result != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + HeapSize - result);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        Bus.ClearMemory(result, checked((int)size));
                }
                state.D[0] = result;
                PoisonVolatile(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: false);
            });
        }

        private void InstallProductionExec()
        {
            Bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            _platform = new AmigaBusExecMemoryPlatform(Bus, () => 0, ThrowAlert, null,
                (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { });
            PortableExec.ExecListCore.Initialize(ref _platform, ExecBase + (uint)ExecLayout.ExecBase.MemList);
            PortableExec.ExecMemoryCore.AddMemList<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, (uint)HeapSize, ExecApi.MemoryFlags.Public, 0, HeapAddress, APTR.Null);
            var context = new ExecMemoryContext(
                bus: Bus,
                allocate: Allocate,
                allocateAbsolute: (_, _) => throw new InvalidOperationException("Unexpected AllocAbs."),
                free: Free,
                available: flags => PortableExec.ExecMemoryCore.AvailMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                    ref _platform, ExecBase, (ExecApi.MemoryFlags)flags),
                allocateFromHeader: (_, _, _) => throw new InvalidOperationException("Unexpected Allocate."),
                deallocateToHeader: (_, _, _) => throw new InvalidOperationException("Unexpected Deallocate."),
                typeOfMemory: _ => throw new InvalidOperationException("Unexpected TypeOfMem."),
                recordAlloc: (size, flags, address) => Allocations.Add((address, checked((uint)size), flags)),
                recordAllocAbs: (_, _, _) => throw new InvalidOperationException("Unexpected AllocAbs record."),
                recordFree: (_, _) => { },
                getExecBase: () => ExecBase,
                allocator: PortableExec.ExecMemoryAllocatorKind.Classic,
                getCurrentTask: () => 0,
                alert: ThrowAlert,
                invokeMemoryHandler: (_, _, _, _, _) => MemoryHandlerResult.DidNothing,
                expungeLibraries: _ => { },
                setActiveState: _ => { });
            var services = new ExecMemoryServices(context);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                MemoryAtAllocations.Add(CaptureMemory());
                services.AllocMem(state);
                PoisonVolatile(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                services.FreeMem(state);
                PoisonVolatile(state, preserveD0: false);
            });
        }

        private uint Allocate(int size, uint flags)
            => PortableExec.ExecMemoryCore.AllocMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, checked((uint)size), (ExecApi.MemoryFlags)flags).Raw;
        private void Free(uint address, int size)
            => PortableExec.ExecMemoryCore.FreeMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, address, checked((uint)size));
        private static void ThrowAlert(uint alert)
            => throw new InvalidOperationException($"Unexpected Exec alert 0x{alert:X8}.");

        internal void AssertSingleFreeChunk(uint address, uint bytes)
        {
            Assert.Equal(address, FirstFreeChunk);
            Assert.Equal(bytes, FreeBytes);
            Assert.Equal(0u, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Next));
            Assert.Equal(bytes, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Bytes));
        }

        internal void SeedOwnedList(uint copList, uint instructions)
        {
            SeedBytes(copList, ExpectedCopList(instructions));
            SeedBytes(instructions, new byte[InstructionBytes]);
            Bus.WriteLong(UserList + (uint)GraphicsLayouts.UCopListNext, 0);
            Bus.WriteLong(UserList + (uint)GraphicsLayouts.UCopListFirstCopList, copList);
            Bus.WriteLong(UserList + (uint)GraphicsLayouts.UCopListCopList, copList);
        }

        internal void SeedBytes(uint address, byte[] bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
                Bus.WriteByte(address + (uint)index, bytes[index], 0);
        }

        internal void AssertPublishedList(uint copList, uint instructions)
        {
            Assert.Equal(ExpectedCopList(instructions), ReadBytes(copList, (int)CopListBytes));
            Assert.Equal(new byte[InstructionBytes], ReadBytes(instructions, (int)InstructionBytes));
            AssertOwner(CaptureMemory(), copList);
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(OwnerMemoryAddress, OwnerMemorySize),
                ReadBytes(0, 0x100), ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        private static void PoisonVolatile(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, uint list)
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
            var originalD0 = vector == GraphicsLvo.UCopperListInit ? 1u : CapturedFreeD0;
            _cpu.State.D[0] = originalD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = list;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            // This gate proves allocator-call ABI only. Each independent
            // public caller supplies GfxBase again; saved registers and
            // volatile provider-tailchain parity are separate future units.
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
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], ReturnAddress, StackPointer);
            }
            throw new InvalidOperationException(
                $"{vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
