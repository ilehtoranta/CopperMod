using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsBitMapDimensionTests
{
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint CapturedAttributeD0 = 0xE1E2_E3E4;
    private const uint HeapBase = 0x0090_0000;
    private const int HeapSize = 0x0020_0000;
    private const uint FirstAllocation = HeapBase + 0x40;
    private const uint NativeHeaderBytes = 56;
    private const uint PublicHeaderBytes = 40;
    private const uint BitmapFlags = 3; // BMF_CLEAR | BMF_DISPLAYABLE; friend is always NULL.
    private const uint NativeAllocationFlags = (uint)(ExecApi.MemoryFlags.Public |
        ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    public static IEnumerable<object[]> DimensionCases()
    {
        // Explicit public geometry is the oracle, independent of native
        // instructions and the host register adapter's argument extraction.
        var geometries = new (uint Width, uint Height, int RowBytes, uint PaddedWidth)[]
        {
            (17, 3, 4, 32),
            (65535, 1, 8192, 65536),
            (65536, 1, 8192, 65536),
            (65537, 1, 8194, 65552),
            (524272, 1, 65534, 524272),
            (1, 65535, 2, 16),
            (0, 1, 0, 0),
            (524273, 1, 0, 0),
            (0xFFFF_FFF0, 1, 0, 0),
            (0xFFFF_FFF1, 1, 0, 0),
            (0xFFFF_FFFF, 1, 0, 0),
            (1, 0, 0, 0),
            (1, 65536, 0, 0),
            (1, 65537, 0, 0),
            (0xCAFE_0011, 0xBEEF_0002, 0, 0)
        };
        foreach (var geometry in geometries)
        foreach (var depth in new[] { 1, 2, 4, 8 })
        foreach (var route in new[] { "native", "host-direct", "host-mapped" })
        {
            yield return new object[]
            {
                route, geometry.Width, geometry.Height, depth, geometry.RowBytes, geometry.PaddedWidth
            };
        }
    }

    [Theory]
    [MemberData(nameof(DimensionCases))]
    public void AllocBitMapHonorsCompleteUlongDimensionsAcrossNativeAndHostBoundaries(
        string route, uint width, uint height, int depth, int expectedRowBytes, uint expectedPaddedWidth)
    {
        var valid = expectedRowBytes != 0;
        using var fixture = new Fixture(route, allowAllocation: valid);
        var before = fixture.CaptureProtectedMemory();
        var failures = new List<string>();
        if (!valid && !fixture.IsNative)
        {
            // The pure Core already receives ULONGs. Keep its failure value
            // distinct from the adapter's unchanged-input decline contract.
            Check(failures, "pure Core rejects full dimensions before allocation", () =>
                fixture.AssertPureCoreDecline(width, height, depth));
        }

        var allocated = fixture.Invoke(GraphicsLvo.AllocBitMap, width, height, depth);
        if (!valid)
        {
            Check(failures, "no allocator call", () => Assert.Empty(fixture.Allocations));
            Check(failures, "no release", () => Assert.Empty(fixture.Frees));
            Check(failures, "no heap publication", fixture.AssertEntireHeapUntouched);
            Check(failures, "no protected-memory mutation", () => fixture.AssertProtectedMemory(before));
            Check(failures, "full input D0 and caller preserved on decline", () =>
                AssertReturn(allocated, width, expectFallback: true));
            AssertNoFailures(failures, route, width, height, depth);
            return;
        }

        Assert.Equal((uint)expectedRowBytes * 8, expectedPaddedWidth);
        Assert.Equal((ulong)(uint)expectedRowBytes, (((ulong)width + 15) >> 4) * 2);
        var expectedAllocations = ExpectedAllocations(fixture.IsNative, expectedRowBytes, height, depth);
        var expectedBitMap = FirstAllocation + (fixture.IsNative ? 16u : 0u);
        Check(failures, "exact route-specific allocation requests", () =>
            Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "no premature release", () => Assert.Empty(fixture.Frees));
        Check(failures, "complete public/private geometry and clear payload", () =>
            fixture.AssertPublication(width, height, depth, expectedRowBytes, expectedAllocations));
        Check(failures, "allocation guards", () => fixture.AssertHeapGuards(expectedAllocations));
        Check(failures, "protected memory after allocation", () => fixture.AssertProtectedMemory(before));

        if (!allocated.UsedFallback && Fixture.IsHeapPointer(allocated.Value))
        {
            fixture.BeginRetirementObservation();
            var attribute = fixture.Invoke(GraphicsLvo.GetBitMapAttr, width, height, depth, allocated.Value);
            Check(failures, "BMA_WIDTH returns padded ULONG width", () =>
                AssertReturn(attribute, expectedPaddedWidth, expectFallback: false, checkSavedRegisters: false));

            // Always free the constructor's actual pointer and header, even
            // if an earlier allocation ABI/geometry assertion was recorded.
            // GetBitMapAttr's separate callee-save defect is outside this unit.
            var freed = fixture.Invoke(GraphicsLvo.FreeBitMap, width, height, depth, allocated.Value);
            var expectedFrees = fixture.IsNative
                ? expectedAllocations.Select(item => (item.Address, item.Size))
                : expectedAllocations.Skip(1).Concat(expectedAllocations.Take(1))
                    .Select(item => (item.Address, item.Size));
            Check(failures, "exact owner retirement", () => Assert.Equal(expectedFrees, fixture.Frees));
            Check(failures, "no extra allocations during query or Free", () =>
                Assert.Equal(expectedAllocations, fixture.Allocations));
            Check(failures, "unmodified bytes before/at/after retirement", fixture.AssertRetirementUnchanged);
            Check(failures, "guards after retirement", () => fixture.AssertHeapGuards(expectedAllocations));
            Check(failures, "protected memory after retirement", () => fixture.AssertProtectedMemory(before));
            Check(failures, "FreeBitMap saved registers and caller", () =>
                AssertReturn(freed, 0, expectFallback: false));
        }
        else
        {
            failures.Add("No allocated public pointer was returned; constructor-to-Free roundtrip could not run.");
        }
        Check(failures, "AllocBitMap saved registers and caller", () =>
            AssertReturn(allocated, expectedBitMap, expectFallback: false));
        AssertNoFailures(failures, route, width, height, depth);
    }

    private static Allocation[] ExpectedAllocations(bool native, int rowBytes, uint height, int depth)
    {
        var planeBytes = checked((uint)rowBytes * height);
        if (native)
        {
            return new[]
            {
                new Allocation(FirstAllocation, checked(NativeHeaderBytes + planeBytes * (uint)depth),
                    NativeAllocationFlags)
            };
        }
        var result = new List<Allocation> { new(FirstAllocation, PublicHeaderBytes, 1) };
        var next = FollowingAllocation(FirstAllocation, PublicHeaderBytes);
        for (var plane = 0; plane < depth; plane++)
        {
            result.Add(new Allocation(next, planeBytes, 2)); // MEMF_CHIP, separately owned planes
            next = FollowingAllocation(next, planeBytes);
        }
        return result.ToArray();
    }

    private static uint FollowingAllocation(uint address, uint size)
        => checked(((address + size + 7u) & ~7u) + 0x20u);

    private static byte[] ExpectedHeader(
        bool native, uint width, uint height, int depth, int rowBytes, Allocation[] allocations)
    {
        Assert.Equal((int)PublicHeaderBytes, GraphicsLayouts.BitMapSize);
        var expected = new byte[native ? (int)NativeHeaderBytes : (int)PublicHeaderBytes];
        var publicOffset = native ? 16 : 0;
        if (native)
        {
            BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(0), 0x424D);
            // This private width field is only a stored low word. In
            // particular, ULONG width65536 legitimately stores zero here.
            BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), (ushort)(width & ushort.MaxValue));
            BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(4), checked((ushort)height));
            BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(6), checked((ushort)depth));
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(8), allocations[0].Size);
        }
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapBytesPerRow),
            checked((ushort)rowBytes));
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapRows),
            checked((ushort)height));
        expected[publicOffset + GraphicsLayouts.BitMapFlags] = native ? (byte)0x0A : (byte)0x0B;
        expected[publicOffset + GraphicsLayouts.BitMapDepth] = checked((byte)depth);
        var planeBytes = checked((uint)rowBytes * height);
        for (var plane = 0; plane < depth; plane++)
        {
            var address = native
                ? checked(FirstAllocation + NativeHeaderBytes + planeBytes * (uint)plane)
                : allocations[plane + 1].Address;
            BinaryPrimitives.WriteUInt32BigEndian(
                expected.AsSpan(publicOffset + GraphicsLayouts.BitMapPlanes + plane * 4), address);
        }
        return expected;
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertNoFailures(List<string> failures, string route, uint width, uint height, int depth)
        => Assert.True(failures.Count == 0,
            $"{route}, width={width:X8}, height={height:X8}, depth={depth}:\n" + string.Join("\n", failures));

    private static void AssertReturn(
        CallResult result, uint expectedD0, bool expectFallback, bool checkSavedRegisters = true)
    {
        var differences = result.StateDifferences.ToList();
        if (checkSavedRegisters)
            differences.AddRange(result.RegisterDifferences);
        if (result.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {result.Value:X8}");
        if (result.UsedFallback != expectFallback)
            differences.Add($"fallback expected {expectFallback}, actual {result.UsedFallback}");
        var expectedPc = expectFallback && result.ProviderTarget != 0
            ? result.ProviderTarget : result.CallerReturnAddress;
        if (result.ProgramCounter != expectedPc)
            differences.Add($"PC expected {expectedPc:X8}, actual {result.ProgramCounter:X8}");
        if (result.StackPointer != result.CallerStackPointer)
            differences.Add($"SP expected {result.CallerStackPointer:X8}, actual {result.StackPointer:X8}");
        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private sealed record Allocation(uint Address, uint Size, uint Flags);
    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record ProtectedMemory(byte[] Low, byte[] Vectors, byte[] GraphicsBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer, uint ProviderTarget,
        string[] RegisterDifferences, string[] StateDifferences);

    private sealed class Fixture : IDisposable
    {
        private const uint CodeAddress = 0x0040_0000;
        private const uint GraphicsBase = 0x0070_0000;
        private const uint ResidentAddress = 0x0072_0000;
        private const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint NativeReturnAddress = CallerAddress + 4;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private const long HostCycles = 211;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505, GraphicsBase
        };
        private readonly IM68kCore? _cpu;
        private readonly GraphicsServices? _services;
        private readonly GraphicsLibraryCore? _pureCore;
        private readonly bool _mapped;
        private readonly bool _allowAllocation;
        private readonly byte[] _heap;
        private readonly int _heapOffset;
        private uint _nextAllocation = FirstAllocation;
        private byte[]? _retirementHeap;
        private ProtectedMemory? _retirementProtected;

        internal Fixture(string route, bool allowAllocation)
        {
            IsNative = route == "native";
            _mapped = route == "host-mapped";
            _allowAllocation = allowAllocation;
            var initialHeap = new byte[HeapSize];
            Array.Fill(initialHeap, (byte)0xA5);
            Bus.MapWritableMemory(HeapBase, initialHeap);
            // MapWritableMemory copies its input. Obtain the actual mapped
            // backing through the existing read window for fast assertions,
            // not a stale setup array. This does not select a JIT CPU.
            Assert.True(Bus.TryGetJitZeroWaitReadMemory(HeapBase, 1, out _heap, out _heapOffset));
            Assert.InRange(_heapOffset, 0, _heap.Length - HeapSize);
            Assert.True(Bus.IsWritableMemoryRange(HeapBase, HeapSize));
            Assert.Equal(_heap[_heapOffset], Bus.ReadByte(HeapBase));
            Assert.Equal(_heap[_heapOffset + HeapSize - 1], Bus.ReadByte(HeapBase + HeapSize - 1));
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            for (var index = 0; index < 0x40; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            Bus.WriteLong(4, ExecBase);

            if (IsNative)
            {
                var entries = Image.Value.Entries.ToDictionary(item => item.Key,
                    item => CodeAddress + (uint)item.Value);
                var fallback = CodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    GraphicsBase, ResidentAddress, fallback, entries, fallback);
                Assert.True((ulong)CodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
                Bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                Bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                Bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                Bus.RegisterHostGateway(ExecBase - 198, state =>
                {
                    Assert.Equal(ExecBase, state.A[6]);
                    state.D[0] = Allocate(state.D[0], state.D[1], clear: true);
                    PoisonVolatile(state, preserveD0: true);
                });
                Bus.RegisterHostGateway(ExecBase - 210, state =>
                {
                    Assert.Equal(ExecBase, state.A[6]);
                    RecordFree(state.A[1], state.D[0]); // actual Exec ABI, never D0-address/D1-size
                    PoisonVolatile(state, preserveD0: false);
                });
                _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
            }
            else
            {
                var image = new byte[0x2000];
                foreach (var vector in new[] { GraphicsLvo.AllocBitMap, GraphicsLvo.FreeBitMap, GraphicsLvo.GetBitMapAttr })
                {
                    var offset = checked(0x1000 + (int)vector);
                    BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(offset), 0x4EF9);
                    BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(offset + 2), ProviderAddress(vector));
                }
                Bus.MapReadOnlyMemory(GraphicsBase - 0x1000, image);
                var context = CreateHostContext();
                _services = new GraphicsServices(context);
                _pureCore = new GraphicsLibraryCore(
                    new CopperStartGraphicsMemoryAdapter(context.Memory, context.IsDisplayDmaRange),
                    new CopperStartGraphicsAllocator(context), new CopperStartGraphicsBlitterAdapter(context),
                    new CopperStartGraphicsDisplayAdapter(context));
                if (_mapped)
                    Assert.True(_services.InstallKickstartRomOverlay(GraphicsBase));
            }
            Assert.Empty(Allocations); // setup must not allocate an unrelated display-database sidecar
            Assert.Empty(Frees);
        }

        private AmigaBus Bus { get; } = new();
        internal bool IsNative { get; }
        internal List<Allocation> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        private List<bool> RetirementMemoryChecks { get; } = new();
        private ReadOnlySpan<byte> Heap => _heap.AsSpan(_heapOffset, HeapSize);

        internal static bool IsHeapPointer(uint address)
            => address >= HeapBase && address < HeapBase + HeapSize;

        private uint Allocate(uint size, uint flags, bool clear)
        {
            // Incorrect invalid-dimension requests are recorded, then return
            // NULL. They still fail the no-allocation contract without any
            // enormous fake allocation/CLEAR masking the original bug.
            var result = _allowAllocation && size > 0 &&
                (ulong)_nextAllocation + size + 0x28 <= (ulong)HeapBase + HeapSize
                ? _nextAllocation : 0u;
            Allocations.Add(new Allocation(result, size, flags));
            if (result == 0)
                return 0;
            _nextAllocation = FollowingAllocation(result, size);
            if (clear && (flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
            {
                // The mock allocator owns this ordinary mapped writable RAM.
                _heap.AsSpan(_heapOffset + checked((int)(result - HeapBase)), checked((int)size)).Clear();
            }
            return result;
        }

        private void RecordFree(uint address, uint size)
        {
            Frees.Add((address, size));
            RetirementMemoryChecks.Add(_retirementHeap is not null &&
                Heap[.._retirementHeap.Length].SequenceEqual(_retirementHeap) &&
                _retirementProtected is not null && ProtectedMemoryMatches(_retirementProtected));
        }

        internal void AssertPureCoreDecline(uint width, uint height, int depth)
        {
            Assert.NotNull(_pureCore);
            Assert.Equal(0u, _pureCore.AllocBitMap(width, height, (uint)depth, BitmapFlags, 0));
            Assert.Empty(Allocations);
            Assert.Empty(Frees);
            AssertEntireHeapUntouched();
        }

        private CopperStartGraphicsContext CreateHostContext()
        {
            var memory = new HostGuestMemory(Bus);
            return new CopperStartGraphicsContext(
                memory: memory,
                waitTof: null,
                writeCustomRegister: static (_, _, _) => { },
                selectFrontViewPort: static _ => { },
                requestDisplayRebuild: null,
                initializeCompatibilityViewPort: static _ => { },
                isMappedRastPort: address => memory.IsMapped(address, GraphicsLayouts.RastPortMinimumSize),
                ensureCompatibilityFont: static () => 0,
                logCall: static (_, _) => { },
                bltBitMap: static _ => 0,
                clipBlit: static _ => 0,
                bltBitMapRastPort: static _ => 0,
                allocBitMap: static _ => throw new InvalidOperationException("No provider allocation is in scope."),
                freeBitMap: static _ => throw new InvalidOperationException("No provider retirement is in scope."),
                getBitMapAttr: static (_, _) => throw new InvalidOperationException("No provider attribute is in scope."),
                changeViewPortBitMap: static (_, _) => 0,
                mergeCopperLists: static _ => 0,
                makeViewPort: static _ => 0,
                loadView: null,
                loadRgb4: static _ => { },
                setRgb4: static _ => { },
                ensureCompatibilityHostObject: static () => 0,
                draw: static _ => { },
                text: static _ => { },
                setRast: static _ => { },
                rectFill: static _ => { },
                allocateMemory: (bytes, flags) => Allocate(checked((uint)bytes), flags, clear: false),
                freeMemory: (address, bytes) => RecordFree(address, checked((uint)bytes)),
                // The paired ECS/OCS Scaffold test separately retains the
                // ECS 32760 policy. This matrix exercises the OCS ULONG limit.
                supportsEcsDisplay: false,
                isDisplayDmaRange: (address, size) => size != 0 && address >= HeapBase &&
                    (ulong)address + size <= (ulong)HeapBase + HeapSize);
        }

        internal void AssertPublication(
            uint width, uint height, int depth, int rowBytes, Allocation[] expectedAllocations)
        {
            var header = ExpectedHeader(IsNative, width, height, depth, rowBytes, expectedAllocations);
            Assert.Equal(header, HeapSlice(FirstAllocation, (uint)header.Length).ToArray());
            if (IsNative)
            {
                AssertClear(FirstAllocation + NativeHeaderBytes, expectedAllocations[0].Size - NativeHeaderBytes);
            }
            else
            {
                foreach (var plane in expectedAllocations.Skip(1))
                    AssertClear(plane.Address, plane.Size);
            }
        }

        private ReadOnlySpan<byte> HeapSlice(uint address, uint size)
            => Heap.Slice(checked((int)(address - HeapBase)), checked((int)size));

        private void AssertClear(uint address, uint size)
        {
            var mismatch = HeapSlice(address, size).IndexOfAnyExcept((byte)0);
            Assert.True(mismatch < 0, $"Uncleared payload byte at {address + (uint)Math.Max(0, mismatch):X8}.");
        }

        internal void AssertEntireHeapUntouched()
            => Assert.True(Heap.IndexOfAnyExcept((byte)0xA5) < 0, "Declined geometry modified mapped heap RAM.");

        internal void AssertHeapGuards(Allocation[] expectedAllocations)
        {
            var previousEnd = HeapBase;
            foreach (var allocation in expectedAllocations)
            {
                Assert.True(HeapSlice(previousEnd, allocation.Address - previousEnd).IndexOfAnyExcept((byte)0xA5) < 0,
                    $"Guard before {allocation.Address:X8} changed.");
                previousEnd = checked(allocation.Address + allocation.Size);
            }
            Assert.True(HeapSlice(previousEnd, HeapBase + HeapSize - previousEnd).IndexOfAnyExcept((byte)0xA5) < 0,
                "Guard after the final allocation changed.");
        }

        internal void BeginRetirementObservation()
        {
            // One bounded backing-buffer copy, at most just over 1MiB. Every
            // Free callback compares spans, not a million bus-byte snapshots.
            _retirementHeap = Heap[..checked((int)(_nextAllocation - HeapBase))].ToArray();
            _retirementProtected = CaptureProtectedMemory();
        }

        internal void AssertRetirementUnchanged()
        {
            Assert.NotNull(_retirementHeap);
            Assert.Equal(Frees.Count, RetirementMemoryChecks.Count);
            Assert.All(RetirementMemoryChecks, value => Assert.True(value, "Memory changed before an allocator retirement."));
            Assert.True(Heap[.._retirementHeap.Length].SequenceEqual(_retirementHeap), "Free changed the allocated buffers.");
            Assert.NotNull(_retirementProtected);
            AssertProtectedMemory(_retirementProtected);
        }

        private byte[] ReadSmallBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal ProtectedMemory CaptureProtectedMemory()
            => new(ReadSmallBytes(0, 0x40), ReadSmallBytes(GraphicsBase - 0x420, 0x420),
                ReadSmallBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        private bool ProtectedMemoryMatches(ProtectedMemory expected)
        {
            var actual = CaptureProtectedMemory();
            return expected.Low.AsSpan().SequenceEqual(actual.Low) &&
                expected.Vectors.AsSpan().SequenceEqual(actual.Vectors) &&
                expected.GraphicsBase.AsSpan().SequenceEqual(actual.GraphicsBase);
        }

        internal void AssertProtectedMemory(ProtectedMemory expected)
        {
            Assert.Equal(ExecBase, Bus.ReadLong(4));
            Assert.True(ProtectedMemoryMatches(expected), "Low RAM, SysBase, vectors, or GfxBase changed.");
        }

        private static void PoisonVolatile(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        private static uint ProviderAddress(GraphicsLvo vector) => vector switch
        {
            GraphicsLvo.AllocBitMap => 0x0072_0100,
            GraphicsLvo.FreeBitMap => 0x0072_0200,
            GraphicsLvo.GetBitMapAttr => 0x0072_0300,
            _ => throw new ArgumentOutOfRangeException(nameof(vector))
        };

        internal CallResult Invoke(GraphicsLvo vector, uint width, uint height, int depth, uint bitMap = 0)
        {
            var allocating = vector == GraphicsLvo.AllocBitMap;
            var querying = vector == GraphicsLvo.GetBitMapAttr;
            var expectedData = DataCanaries.ToArray();
            if (allocating)
            {
                expectedData[0] = (uint)depth;
                expectedData[1] = BitmapFlags;
            }
            var originalD0 = allocating ? width : querying ? CapturedAttributeD0 : CapturedFreeD0;
            if (IsNative)
                _cpu!.Reset(CallerAddress, StackPointer);
            var state = _cpu?.State ?? new M68kCpuState();
            if (IsNative)
            {
                Assert.Equal(M68kCpuState.ResetStatusRegister, state.StatusRegister);
            }
            else
            {
                state.ProgramCounter = CallerAddress;
                state.StatusRegister = M68kCpuState.ResetStatusRegister;
                state.Cycles = HostCycles;
                state.A[7] = StackPointer;
            }
            state.D[0] = originalD0;
            state.D[1] = allocating ? height : querying ? 8u : 0xD1D1_0101u;
            state.A[0] = allocating ? 0 : bitMap;
            state.A[1] = 0xA1A1_A1A1;
            for (var index = 0; index < expectedData.Length; index++)
                state.D[index + 2] = expectedData[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                state.A[index + 2] = AddressCanaries[index];

            bool usedFallback;
            if (IsNative)
            {
                usedFallback = ExecuteNative(vector, originalD0);
            }
            else if (_mapped)
            {
                Assert.True(Bus.TryInvokeHostGatewayAt(checked((uint)((long)GraphicsBase + (int)vector)), state));
                usedFallback = state.ProgramCounter == ProviderAddress(vector);
            }
            else
            {
                _ = vector switch
                {
                    GraphicsLvo.AllocBitMap => _services!.AllocBitMap(state),
                    GraphicsLvo.FreeBitMap => _services!.FreeBitMap(state),
                    GraphicsLvo.GetBitMapAttr => _services!.GetBitMapAttr(state),
                    _ => throw new ArgumentOutOfRangeException(nameof(vector))
                };
                usedFallback = state.D[0] == originalD0;
            }

            var registers = new List<string>();
            for (var index = 0; index < expectedData.Length; index++)
            {
                if (state.D[index + 2] != expectedData[index])
                    registers.Add($"D{index + 2} expected {expectedData[index]:X8}, actual {state.D[index + 2]:X8}");
            }
            for (var index = 0; index < AddressCanaries.Length; index++)
            {
                if (state.A[index + 2] != AddressCanaries[index])
                    registers.Add($"A{index + 2} expected {AddressCanaries[index]:X8}, actual {state.A[index + 2]:X8}");
            }
            var stateDifferences = new List<string>();
            if (!IsNative)
            {
                if (state.StatusRegister != M68kCpuState.ResetStatusRegister)
                    stateDifferences.Add($"Host SR changed to {state.StatusRegister:X4}");
                if (state.Cycles != HostCycles || state.NativeCycles != 0)
                    stateDifferences.Add($"Host cycles changed: Cycles={state.Cycles}, NativeCycles={state.NativeCycles}");
            }
            return new CallResult(state.D[0], usedFallback, state.ProgramCounter, state.A[7],
                IsNative ? NativeReturnAddress : CallerAddress, StackPointer,
                _mapped ? ProviderAddress(vector) : 0, registers.ToArray(), stateDifferences.ToArray());
        }

        private bool ExecuteNative(GraphicsLvo vector, uint originalD0)
        {
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
            var entry = CodeAddress + (uint)Image.Value.Entries[vector];
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // actual JSR d16(A6), with A6=GfxBase
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(NativeReturnAddress, 0x4E71);
            _cpu!.ExecuteInstruction();
            Assert.Equal(vectorAddress, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(NativeReturnAddress, Bus.ReadLong(_cpu.State.A[7]));
            var usedFallback = false;
            var arrivedByBranch = false;
            var enteredBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                var opcode = Bus.ReadWord(pc);
                enteredBody |= pc == entry;
                usedFallback |= pc == CodeAddress + (uint)Image.Value.Fallback ||
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
                if (_cpu.State.ProgramCounter != NativeReturnAddress)
                    continue;
                Assert.True(enteredBody, "Physical vector never entered the requested native body.");
                return usedFallback;
            }
            throw new InvalidOperationException(
                $"{vector} failed to return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose()
        {
            _cpu?.Dispose();
            _services?.Dispose();
        }
    }
}
