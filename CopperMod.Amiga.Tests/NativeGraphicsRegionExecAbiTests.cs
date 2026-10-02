using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRegionExecAbiTests
{
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionReplacementAndRetirementReturnMemoryThroughProductionExec(
        bool clearBeforeDispose)
    {
        using var fixture = new Fixture();
        var initialFree = fixture.FreeBytes;
        var initialChunk = fixture.FirstFreeChunk;
        const uint regionBytes = GraphicsLayouts.NativeRegionAllocationSize;
        const uint nodeBytes = GraphicsLayouts.RegionRectangleSize;

        var region = fixture.Execute(GraphicsLvo.NewRegion);
        Assert.NotEqual(0u, region);
        var allocationBase = region - (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        Assert.Equal(initialChunk, allocationBase);
        Assert.Equal(initialFree - regionBytes, fixture.FreeBytes);
        var prefix = fixture.ReadBytes(allocationBase, GraphicsLayouts.NativeRegionPrivatePrefixSize);
        Assert.Equal(GraphicsLayouts.NativeRegionMarker, fixture.Bus.ReadWord(allocationBase));

        fixture.Union(region, 10, 10, 20, 20);
        var oldNode = fixture.Bus.ReadLong(region + (uint)GraphicsLayouts.RegionRectangle);
        Assert.NotEqual(0u, oldNode);
        Assert.Equal(initialFree - regionBytes - nodeBytes, fixture.FreeBytes);
        Assert.Empty(fixture.Frees);

        // The upper stem replaces the old single node with two canonical
        // nodes. Real FreeMem must retire oldNode from A1, not the emitter's
        // private A0 staging or the caller's Rectangle pointer.
        fixture.Union(region, 14, 5, 16, 15);
        var first = fixture.Bus.ReadLong(region + (uint)GraphicsLayouts.RegionRectangle);
        var second = fixture.Bus.ReadLong(first + (uint)GraphicsLayouts.RegionRectangleNext);
        Assert.NotEqual(oldNode, first);
        Assert.NotEqual(oldNode, second);
        Assert.NotEqual(first, second);
        Assert.NotEqual(0u, second);
        Assert.Equal(region + (uint)GraphicsLayouts.RegionRectangle,
            fixture.Bus.ReadLong(first + (uint)GraphicsLayouts.RegionRectanglePrevious));
        Assert.Equal(first,
            fixture.Bus.ReadLong(second + (uint)GraphicsLayouts.RegionRectanglePrevious));
        Assert.Equal(0u, fixture.Bus.ReadLong(second + (uint)GraphicsLayouts.RegionRectangleNext));
        Assert.Equal(new short[] { 10, 5, 20, 20 }, fixture.ReadBounds(region));
        Assert.Equal(new short[] { 4, 0, 6, 4 },
            fixture.ReadBounds(first + (uint)GraphicsLayouts.RegionRectangleBounds));
        Assert.Equal(new short[] { 0, 5, 10, 15 },
            fixture.ReadBounds(second + (uint)GraphicsLayouts.RegionRectangleBounds));
        Assert.Equal((oldNode, (int)nodeBytes), Assert.Single(fixture.Frees));
        Assert.Equal(initialFree - regionBytes - 2 * nodeBytes, fixture.FreeBytes);
        Assert.Equal(oldNode, fixture.FirstFreeChunk);
        Assert.Equal(nodeBytes,
            fixture.Bus.ReadLong(oldNode + (uint)ExecLayout.MemChunk.Bytes));
        Assert.Equal(4, fixture.Allocations.Count);
        Assert.Equal(allocationBase, fixture.Allocations[0].Address);
        Assert.Equal(oldNode, fixture.Allocations[1].Address);
        Assert.Equal(first, fixture.Allocations[2].Address);
        Assert.Equal(second, fixture.Allocations[3].Address);
        Assert.All(fixture.Allocations, allocation => Assert.Equal((int)nodeBytes, allocation.Size));
        Assert.Equal((uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear),
            fixture.Allocations[0].Flags);

        if (clearBeforeDispose)
        {
            Assert.Equal(0u, fixture.Execute(GraphicsLvo.ClearRegion, region));
            Assert.Equal(0u, fixture.Bus.ReadLong(region + (uint)GraphicsLayouts.RegionRectangle));
            Assert.Equal(prefix,
                fixture.ReadBytes(allocationBase, GraphicsLayouts.NativeRegionPrivatePrefixSize));
            fixture.AssertSingleFreeChunk(oldNode, initialFree - regionBytes);
            Assert.Equal(3, fixture.Frees.Count);

            // An already-empty clear cannot free either retired node twice.
            Assert.Equal(0u, fixture.Execute(GraphicsLvo.ClearRegion, region));
            Assert.Equal(3, fixture.Frees.Count);
            fixture.AssertSingleFreeChunk(oldNode, initialFree - regionBytes);
        }

        Assert.Equal(0u, fixture.Execute(GraphicsLvo.DisposeRegion, region));
        Assert.Equal(new[]
        {
            (oldNode, (int)nodeBytes),
            (first, (int)nodeBytes),
            (second, (int)nodeBytes),
            (allocationBase, (int)regionBytes),
        }, fixture.Frees.ToArray());
        Assert.Equal(4, fixture.Allocations.Count);
        fixture.AssertSingleFreeChunk(initialChunk, initialFree);
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);

    private sealed class Fixture : IDisposable
    {
        private const uint ExecBase = 0x0077_0000;
        private const uint CodeAddress = 0x0087_0000;
        private const uint HeapAddress = 0x00D0_0000;
        private const uint HeapSize = 0x1000;
        private const uint Rectangle = 0x00DC_0000;
        private const uint Stack = 0x00C7_0000;
        private const uint StackPointer = Stack + 0x200;
        private const uint ReturnAddress = 0x00F7_0000;
        private const uint CapturedFrame = 0xA1B2_C3D4;
        private readonly IM68kCore _cpu;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture()
        {
            Bus.MapWritableMemory(4, new byte[4]);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            Bus.MapWritableMemory(HeapAddress, new byte[checked((int)HeapSize)]);
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(Rectangle, new byte[GraphicsLayouts.RectangleSize]);
            Bus.MapWritableMemory(Stack, new byte[0x400]);
            _platform = new AmigaBusExecMemoryPlatform(
                Bus, () => 0, ThrowAlert, null,
                (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { });
            PortableExec.ExecListCore.Initialize(ref _platform,
                ExecBase + (uint)ExecLayout.ExecBase.MemList);
            PortableExec.ExecMemoryCore.AddMemList<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, HeapSize, ExecApi.MemoryFlags.Public, 0, HeapAddress, APTR.Null);

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
                recordAlloc: (size, flags, address) => Allocations.Add((address, size, flags)),
                recordAllocAbs: (_, _, _) => throw new InvalidOperationException("Unexpected AllocAbs record."),
                recordFree: (address, size) => Frees.Add((address, size)),
                getExecBase: () => ExecBase,
                allocator: PortableExec.ExecMemoryAllocatorKind.Classic,
                getCurrentTask: () => 0,
                alert: ThrowAlert,
                invokeMemoryHandler: (_, _, _, _, _) => MemoryHandlerResult.DidNothing,
                expungeLibraries: _ => { },
                setActiveState: _ => { });
            var services = new ExecMemoryServices(context);

            // These are the production ABI entry points, not test callbacks
            // interpreting address registers. Their allocator delegates use
            // the real guest MemList and classic free-chunk implementation.
            Bus.RegisterHostGateway(ExecBase - 198, services.AllocMem);
            Bus.RegisterHostGateway(ExecBase - 210, services.FreeMem);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        internal AmigaBus Bus { get; } = new();
        internal List<(uint Address, int Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, int Size)> Frees { get; } = new();
        internal uint FreeBytes => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.Free);
        internal uint FirstFreeChunk => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.First);

        private uint Allocate(int size, uint flags)
            => PortableExec.ExecMemoryCore.AllocMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, checked((uint)size), (ExecApi.MemoryFlags)flags).Raw;

        private void Free(uint address, int size)
            => PortableExec.ExecMemoryCore.FreeMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, address, checked((uint)size));

        private static void ThrowAlert(uint alert)
            => throw new InvalidOperationException($"Unexpected Exec alert 0x{alert:X8}.");

        internal void Union(uint region, short minX, short minY, short maxX, short maxY)
        {
            Bus.WriteWord(Rectangle, unchecked((ushort)minX));
            Bus.WriteWord(Rectangle + 2, unchecked((ushort)minY));
            Bus.WriteWord(Rectangle + 4, unchecked((ushort)maxX));
            Bus.WriteWord(Rectangle + 6, unchecked((ushort)maxY));
            Assert.Equal(1u, Execute(GraphicsLvo.OrRectRegion, region));
        }

        internal byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal short[] ReadBounds(uint address)
            => Enumerable.Range(0, 4).Select(index => unchecked((short)Bus.ReadWord(address + (uint)(2 * index)))).ToArray();

        internal void AssertSingleFreeChunk(uint address, uint bytes)
        {
            Assert.Equal(bytes, FreeBytes);
            Assert.Equal(address, FirstFreeChunk);
            Assert.Equal(0u, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Next));
            Assert.Equal(bytes, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Bytes));
        }

        internal uint Execute(GraphicsLvo vector, uint region = 0)
        {
            var input = ReadBytes(Rectangle, GraphicsLayouts.RectangleSize);
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = CapturedFrame;
            _cpu.State.A[0] = region;
            _cpu.State.A[1] = Rectangle;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                Assert.False(_cpu.State.ProgramCounter == CodeAddress + (uint)Image.Value.Fallback ||
                    (_cpu.State.D[0] == CapturedFrame && Bus.ReadWord(_cpu.State.ProgramCounter) == 0x4E75),
                    $"Native {vector} unexpectedly declined the production Exec fixture.");
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;

                Assert.Equal(StackPointer + 4, _cpu.State.A[7]);
                Assert.Equal(input, ReadBytes(Rectangle, GraphicsLayouts.RectangleSize));
                return _cpu.State.D[0];
            }

            throw new InvalidOperationException(
                $"Native {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
