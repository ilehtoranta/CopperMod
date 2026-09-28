using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRasterExecAbiTests
{
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(17u, 3u)]
    [InlineData(0xABCD_0011u, 0x1234_0003u)]
    public void RasterRoundtripReturnsExactRequestedSpanThroughProductionExec(
        uint width, uint height)
    {
        using var fixture = new Fixture();
        var initialFree = fixture.FreeBytes;
        var allocationBase = fixture.FirstFreeChunk;
        const int prefixBytes = 10;
        const int payloadBytes = 12;
        const int requestedBytes = prefixBytes + payloadBytes;
        const int occupiedBytes = 24;

        var raster = fixture.Execute(GraphicsLvo.AllocRaster, width, height);

        Assert.Equal(allocationBase + prefixBytes, raster);
        Assert.Equal((allocationBase, requestedBytes,
            (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear)),
            Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        fixture.AssertSingleFreeChunk(allocationBase + occupiedBytes,
            initialFree - occupiedBytes);
        Assert.Equal((ushort)0x5253, fixture.Bus.ReadWord(allocationBase));
        Assert.Equal((ushort)17, fixture.Bus.ReadWord(allocationBase + 2));
        Assert.Equal((ushort)3, fixture.Bus.ReadWord(allocationBase + 4));
        Assert.Equal((uint)requestedBytes, fixture.Bus.ReadLong(allocationBase + 6));
        for (var offset = 0; offset < payloadBytes; offset++)
            Assert.Equal((byte)0, fixture.Bus.ReadByte(raster + (uint)offset));

        // CLEAR covers the 22 requested bytes, not the classic allocator's
        // two padding bytes. FreeRaster must return 22 through Exec D0 too.
        for (var offset = requestedBytes; offset < occupiedBytes; offset++)
            Assert.Equal((byte)0xA5, fixture.Bus.ReadByte(allocationBase + (uint)offset));

        Assert.Equal(0u, fixture.Execute(GraphicsLvo.FreeRaster, width, height, raster));
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
                ref _platform, ExecBase, HeapSize,
                ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip, 0, HeapAddress, APTR.Null);

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

            // Production entry points interpret A1/D0 for FreeMem. The real
            // guest allocator must recover the released raster allocation.
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

        internal uint Execute(GraphicsLvo vector, uint width, uint height, uint raster = 0)
        {
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = width;
            _cpu.State.D[1] = height;
            _cpu.State.A[0] = raster;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                Assert.False(_cpu.State.ProgramCounter == CodeAddress + (uint)Image.Value.Fallback ||
                    (_cpu.State.D[0] == width && Bus.ReadWord(_cpu.State.ProgramCounter) == 0x4E75),
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
