using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsScreenPrefixDefaultsTests
{
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x1000;
    private const uint Screen = ArenaAddress + 0x100;
    private const uint View = ArenaAddress + 0x300;
    private const uint RasInfo = ArenaAddress + 0x400;
    private const uint Plane = ArenaAddress + 0x500;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly (uint Address, uint Size, uint Flags)[] Requests =
    {
        (Screen, (uint)GraphicsLayouts.ScreenSize, PublicClear),
        (View, (uint)GraphicsLayouts.ViewSize, PublicClear),
        (RasInfo, (uint)GraphicsLayouts.RasInfoSize, PublicClear),
        (Plane, PlaneBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out var constructor,
            out _, out _, out _, out _, out _, out var allocator);
        return new NativeImage(code, entries, fallback, constructor, allocator);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> Cases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[] { "preallocated", "allocator", "null-screen", "odd-plane" })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PrivateScreenPrefixDefaultsUseTheEmbeddedRastPortBase(bool relocated, string scenario)
    {
        var allocator = scenario == "allocator";
        var success = scenario is "preallocated" or "allocator";
        using var fixture = new Fixture(relocated, allocator);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(scenario);
        var after = fixture.CaptureMemory();
        var expected = success ? WithPublishedPrefix(before, allocator) : before;
        var failures = new List<string>();
        Check(failures, "private D0 result", () => Assert.Equal(success ? (allocator ? Screen : 1u) : 0u, result.Data[0]));
        Check(failures, "private A6 role", () => Assert.Equal(
            allocator ? Fixture.ExecBase : scenario == "null-screen" ? 0u : Screen, result.Address6));
        Check(failures, "native caller PC/SP and no fallback", () =>
        {
            Assert.Equal(Fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.False(result.HitFallback);
        });
        Check(failures, "actual Exec requests", () => Assert.Equal(
            allocator ? Requests : Array.Empty<(uint, uint, uint)>(), fixture.Allocations));
        Check(failures, "no retirement", () => Assert.Empty(fixture.Frees));
        if (allocator)
        {
            Check(failures, "four unmodified allocator ownership results", () =>
                Assert.Equal(new[] { Screen, View, RasInfo, Plane }, result.Data.Take(4)));
            Check(failures, "allocation boundary snapshot count", () =>
            {
                Assert.Equal(4, fixture.MemoryBeforeAllocations.Count);
                Assert.Equal(4, fixture.MemoryAfterAllocations.Count);
            });
            var expectedAllocationState = before;
            for (var index = 0; index < Requests.Length; index++)
            {
                var expectedBefore = expectedAllocationState;
                Check(failures, $"before allocation {index}", () =>
                    AssertMemoryEqual(expectedBefore, fixture.MemoryBeforeAllocations[index]));
                expectedAllocationState = WithClear(expectedAllocationState, Requests[index].Address, Requests[index].Size);
                var expectedAfter = expectedAllocationState;
                Check(failures, $"allocator-only CLEAR {index}", () =>
                    AssertMemoryEqual(expectedAfter, fixture.MemoryAfterAllocations[index]));
            }
        }
        else
        {
            Check(failures, "preallocated entry does not invoke Exec", () =>
            {
                Assert.Empty(fixture.MemoryBeforeAllocations);
                Assert.Empty(fixture.MemoryAfterAllocations);
            });
        }

        if (success)
        {
            // The private constructor promises these local no-font defaults;
            // this does not assert complete public InitRastPort equivalence.
            var rastPort = Screen + (uint)GraphicsLayouts.ScreenRastPort;
            Check(failures, "embedded RP Mask/FgPen/BgPen/OutlinePen/DrawMode", () => Assert.Equal(
                new byte[] { 0xFF, 0xFF, 0x00, 0xFF, 0x01 },
                ArenaBytes(after, rastPort + (uint)GraphicsLayouts.RastPortMask, 5)));
            Check(failures, "embedded RP LinePattern", () => Assert.Equal((ushort)0xFFFF,
                BinaryPrimitives.ReadUInt16BigEndian(ArenaBytes(after, rastPort + (uint)GraphicsLayouts.RastPortLinePattern, 2))));
            Check(failures, "old postincrement-target bytes stay zero", () => Assert.Equal(
                new byte[12], ArenaBytes(after, Screen + 0x120, 12)));
        }
        Check(failures, "complete Screen bytes", () => Assert.Equal(
            ArenaBytes(expected, Screen, GraphicsLayouts.ScreenSize), ArenaBytes(after, Screen, GraphicsLayouts.ScreenSize)));
        Check(failures, "complete View and RasInfo bytes", () =>
        {
            Assert.Equal(ArenaBytes(expected, View, GraphicsLayouts.ViewSize), ArenaBytes(after, View, GraphicsLayouts.ViewSize));
            Assert.Equal(ArenaBytes(expected, RasInfo, GraphicsLayouts.RasInfoSize), ArenaBytes(after, RasInfo, GraphicsLayouts.RasInfoSize));
        });
        Check(failures, "plane storage follows only allocator CLEAR", () => Assert.Equal(
            ArenaBytes(expected, Plane, (int)PlaneBytes), ArenaBytes(after, Plane, (int)PlaneBytes)));
        Check(failures, "complete arena, independent sentinels and caller guards", () => AssertMemoryEqual(expected, after));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static MemorySnapshot WithClear(MemorySnapshot source, uint address, uint count)
    {
        var arena = source.Arena.ToArray();
        arena.AsSpan(checked((int)(address - ArenaAddress)), checked((int)count)).Clear();
        return source with { Arena = arena };
    }

    private static MemorySnapshot WithPublishedPrefix(MemorySnapshot source, bool allocator)
    {
        // Expected bytes only: never seed/repair the guest after execution.
        var arena = source.Arena.ToArray();
        var screen = arena.AsSpan((int)(Screen - ArenaAddress), GraphicsLayouts.ScreenSize);
        var view = arena.AsSpan((int)(View - ArenaAddress), GraphicsLayouts.ViewSize);
        var rasInfo = arena.AsSpan((int)(RasInfo - ArenaAddress), GraphicsLayouts.RasInfoSize);
        screen.Clear();
        view.Clear();
        rasInfo.Clear();
        if (allocator)
            arena.AsSpan((int)(Plane - ArenaAddress), (int)PlaneBytes).Clear();
        BinaryPrimitives.WriteUInt16BigEndian(screen[GraphicsLayouts.ScreenWidth..], Width);
        BinaryPrimitives.WriteUInt16BigEndian(screen[GraphicsLayouts.ScreenHeight..], Height);
        BinaryPrimitives.WriteUInt32BigEndian(view[GraphicsLayouts.ViewViewPort..], Screen + (uint)GraphicsLayouts.ScreenViewPort);
        BinaryPrimitives.WriteUInt16BigEndian(view[GraphicsLayouts.ViewDyOffset..], (ushort)GraphicsLayouts.ViewDefaultDyOffset);
        BinaryPrimitives.WriteUInt16BigEndian(view[GraphicsLayouts.ViewDxOffset..], (ushort)GraphicsLayouts.ViewDefaultDxOffset);
        var viewPort = screen[GraphicsLayouts.ScreenViewPort..];
        BinaryPrimitives.WriteUInt16BigEndian(viewPort[GraphicsLayouts.ViewPortDWidth..], Width);
        BinaryPrimitives.WriteUInt16BigEndian(viewPort[GraphicsLayouts.ViewPortDHeight..], Height);
        viewPort[GraphicsLayouts.ViewPortSpritePriorities] = GraphicsLayouts.ViewPortDefaultSpritePriorities;
        BinaryPrimitives.WriteUInt32BigEndian(viewPort[GraphicsLayouts.ViewPortRasInfo..], RasInfo);
        var rastPort = screen[GraphicsLayouts.ScreenRastPort..];
        foreach (var offset in new[] { GraphicsLayouts.RastPortMask, GraphicsLayouts.RastPortFgPen, GraphicsLayouts.RastPortOutlinePen })
            rastPort[offset] = 0xFF;
        rastPort[GraphicsLayouts.RastPortBgPen] = 0;
        rastPort[GraphicsLayouts.RastPortDrawMode] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(rastPort[GraphicsLayouts.RastPortLinePattern..], 0xFFFF);
        BinaryPrimitives.WriteUInt32BigEndian(rastPort[GraphicsLayouts.RastPortBitMap..], Screen + (uint)GraphicsLayouts.ScreenBitMap);
        var bitMap = screen[GraphicsLayouts.ScreenBitMap..];
        BinaryPrimitives.WriteUInt16BigEndian(bitMap[GraphicsLayouts.BitMapBytesPerRow..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(bitMap[GraphicsLayouts.BitMapRows..], Height);
        bitMap[GraphicsLayouts.BitMapFlags] = 8;
        bitMap[GraphicsLayouts.BitMapDepth] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(bitMap[GraphicsLayouts.BitMapPlanes..], Plane);
        BinaryPrimitives.WriteUInt32BigEndian(rasInfo[GraphicsLayouts.RasInfoBitMap..], Screen + (uint)GraphicsLayouts.ScreenBitMap);
        return source with { Arena = arena };
    }

    private static byte[] ArenaBytes(MemorySnapshot snapshot, uint address, int count)
        => snapshot.Arena.AsSpan(checked((int)(address - ArenaAddress)), count).ToArray();
    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Arena, actual.Arena);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
        Assert.Equal(expected.ArenaGuards, actual.ArenaGuards);
        Assert.Equal(expected.StackGuards, actual.StackGuards);
        Assert.Equal(expected.Caller, actual.Caller);
    }
    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries,
        int Fallback, int Constructor, int Allocator);
    private sealed record MemorySnapshot(byte[] Arena, byte[] Low, byte[] GraphicsImage,
        byte[] ArenaGuards, byte[] StackGuards, byte[] Caller);
    private sealed record CallResult(uint[] Data, uint Address6, uint ProgramCounter, uint StackPointer, bool HitFallback);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        internal const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        internal const uint ReturnAddress = CallerAddress + 6;
        private const uint StackAddress = 0x00C7_0000;
        internal const uint StackPointer = StackAddress + 0x300;
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly bool _allocator;

        internal Fixture(bool relocated, bool allocator)
        {
            _allocator = allocator;
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/private JSR absolute";
            if (relocated)
            {
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                _graphicsBase = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(item => item.Key, item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                _graphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
            }
            Assert.NotEqual(ExecBase, _graphicsBase);
            _entry = _nativeCodeAddress + (uint)(allocator ? Image.Value.Allocator : Image.Value.Constructor);
            _bus.MapWritableMemory(ArenaAddress - 0x20, Enumerable.Repeat((byte)0xA5, ArenaSize + 0x40).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var index = 0; index < 0x100; index++)
                _bus.WriteByte((uint)index, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EB9); // JSR absolute.L: neither entry has a public LVO
            _bus.WriteLong(CallerAddress + 2, _entry);
            _bus.WriteWord(ReturnAddress, 0x4E71);
            InstallExec();
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryBeforeAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAfterAllocations { get; } = new();

        private void InstallExec()
        {
            _bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var index = Allocations.Count;
                Assert.InRange(index, 0, Requests.Length - 1);
                MemoryBeforeAllocations.Add(CaptureMemory());
                var address = Requests[index].Address;
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));
                Assert.InRange(size, 1u, ArenaAddress + ArenaSize - address);
                // Only Exec's requested CLEAR is simulated here. No defaults,
                // geometry, links, or other header fields are fabricated.
                if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                    _bus.ClearMemory(address, checked((int)size));
                MemoryAfterAllocations.Add(CaptureMemory());
                state.D[0] = address;
                PoisonVolatile(state, preserveD0: true);
            });
            _bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                PoisonVolatile(state, preserveD0: false);
            });
        }

        private static void PoisonVolatile(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }
        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(0, 0x100),
            ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(ArenaAddress - 0x20, 0x20).Concat(ReadBytes(ArenaAddress + ArenaSize, 0x20)).ToArray(),
            ReadBytes(StackAddress, 0x100).Concat(ReadBytes(StackAddress + 0x340, 0x2C0)).ToArray(),
            ReadBytes(CallerAddress, 8));

        internal CallResult Invoke(string scenario)
        {
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            for (var index = 0; index < 8; index++)
                _cpu.State.D[index] = 0xD000_0100u + (uint)index;
            for (var index = 0; index < 6; index++)
                _cpu.State.A[index] = 0xA000_0201u + (uint)(index * 0x10);
            _cpu.State.A[6] = ExecBase;
            _cpu.State.D[0] = Width;
            _cpu.State.D[1] = Height;
            _cpu.State.D[2] = _allocator ? PlaneBytes : scenario == "odd-plane" ? Plane + 1u : Plane;
            if (!_allocator)
            {
                _cpu.State.A[0] = scenario == "null-screen" ? 0u : Screen;
                _cpu.State.A[1] = View;
                _cpu.State.A[2] = RasInfo;
            }
            // A6 is ExecBase at entry, but only the allocator returns it.
            // The preallocated constructor intentionally returns A6=Screen;
            // this fixture does not invent a public callee-save contract.
            _cpu.ExecuteInstruction();
            Assert.Equal(_entry, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            var hitFallback = false;
            for (var instruction = 0; instruction < 100_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                hitFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                Assert.True((pc >= _nativeCodeAddress && (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length)
                    || pc == ExecBase - 198 || pc == ExecBase - 210, $"Unexpected execution address 0x{pc:X8}.");
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new CallResult(_cpu.State.D.ToArray(), _cpu.State.A[6], _cpu.State.ProgramCounter,
                        _cpu.State.A[7], hitFallback);
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
