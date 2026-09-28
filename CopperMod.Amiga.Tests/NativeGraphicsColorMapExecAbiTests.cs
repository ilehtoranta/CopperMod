using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsColorMapExecAbiTests
{
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(2, 72, 72)]
    [InlineData(3, 76, 80)]
    public void ColorMapRoundtripReturnsOriginalRequestedSizeThroughProductionExec(
        int count, int requestedBytes, int occupiedBytes)
    {
        using var fixture = new Fixture();
        var initialFree = fixture.FreeBytes;
        var allocationBase = fixture.FirstFreeChunk;

        var colorMap = fixture.Execute(GraphicsLvo.GetColorMap, checked((uint)count));

        Assert.Equal(allocationBase + (uint)GraphicsLayouts.NativeColorMapPrivatePrefixSize, colorMap);
        Assert.Equal(GraphicsLayouts.NativeColorMapBaseSize + count * 4, requestedBytes);
        Assert.Equal((allocationBase, requestedBytes,
            (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear)),
            Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        fixture.AssertSingleFreeChunk(allocationBase + (uint)occupiedBytes,
            initialFree - (uint)occupiedBytes);

        Assert.Equal(GraphicsLayouts.NativeColorMapMarker, fixture.Bus.ReadWord(allocationBase));
        Assert.Equal((ushort)0, fixture.Bus.ReadWord(allocationBase + 2));
        Assert.Equal((uint)requestedBytes, fixture.Bus.ReadLong(allocationBase + 4));
        Assert.Equal(0u, fixture.Bus.ReadLong(allocationBase + 8));
        Assert.Equal((byte)0, fixture.Bus.ReadByte(colorMap + (uint)GraphicsLayouts.ColorMapFlags));
        Assert.Equal((byte)2, fixture.Bus.ReadByte(colorMap + (uint)GraphicsLayouts.ColorMapType));
        Assert.Equal((ushort)count, fixture.Bus.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));

        var highTable = colorMap + (uint)GraphicsLayouts.ColorMapSize;
        var lowTable = highTable + checked((uint)(count * 2));
        Assert.Equal(highTable,
            fixture.Bus.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapColorTable));
        Assert.Equal(lowTable,
            fixture.Bus.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits));
        for (var index = 0; index < count; index++)
        {
            Assert.Equal((ushort)0, fixture.Bus.ReadWord(highTable + (uint)(index * 2)));
            Assert.Equal((ushort)0, fixture.Bus.ReadWord(lowTable + (uint)(index * 2)));
        }

        // AllocMem clears the requested envelope, not the allocator's
        // alignment padding. The three-entry row must still free 76, not 80.
        for (var offset = requestedBytes; offset < occupiedBytes; offset++)
            Assert.Equal((byte)0xA5, fixture.Bus.ReadByte(allocationBase + (uint)offset));

        Assert.Equal(0u, fixture.Execute(GraphicsLvo.FreeColorMap, 0xA1B2_C3D4, colorMap));
        Assert.Equal((allocationBase, requestedBytes), Assert.Single(fixture.Frees));
        Assert.Single(fixture.Allocations);
        fixture.AssertSingleFreeChunk(allocationBase, initialFree);
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);

    private sealed class Fixture : IDisposable
    {
        private const uint ExecBase = 0x0077_0000;
        private const uint CodeAddress = 0x0087_0000;
        private const uint HeapAddress = 0x00D0_0000;
        private const uint HeapSize = 0x1000;
        private const uint Stack = 0x00C7_0000;
        private const uint StackPointer = Stack + 0x200;
        private const uint ReturnAddress = 0x00F7_0000;
        private readonly IM68kCore _cpu;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture()
        {
            Bus.MapWritableMemory(4, new byte[4]);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            var heap = new byte[checked((int)HeapSize)];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
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

            // Production services interpret the CPU registers. Their real
            // MemList allocator must coalesce the released envelope again.
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

        internal void AssertSingleFreeChunk(uint address, uint bytes)
        {
            Assert.Equal(bytes, FreeBytes);
            Assert.Equal(address, FirstFreeChunk);
            Assert.Equal(0u, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Next));
            Assert.Equal(bytes, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Bytes));
        }

        internal uint Execute(GraphicsLvo vector, uint d0, uint a0 = 0)
        {
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = d0;
            _cpu.State.A[0] = a0;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                Assert.False(_cpu.State.ProgramCounter == CodeAddress + (uint)Image.Value.Fallback ||
                    (_cpu.State.D[0] == d0 && Bus.ReadWord(_cpu.State.ProgramCounter) == 0x4E75),
                    $"Native {vector} unexpectedly declined the production Exec fixture.");
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;

                Assert.Equal(StackPointer + 4, _cpu.State.A[7]);
                return _cpu.State.D[0];
            }

            throw new InvalidOperationException(
                $"Native {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
