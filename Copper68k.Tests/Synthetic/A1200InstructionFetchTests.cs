using Copper68k;

namespace Copper68k.Tests.Synthetic;

public sealed class A1200InstructionFetchTests
{
    [Fact]
    public void UncachedInstructionFetchUsesAlignedLongwords()
    {
        const uint code = 0x1000;
        var bus = new SparseRecordingBus();
        bus.Initialize(code, 0x4E714E71, 4);
        bus.Initialize(code + 4, 0x4E714E71, 4);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(code + 2, 0x8000);

        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();

        Assert.Collection(bus.Accesses.Where(access => access.Kind == M68kBusAccessKind.CpuInstructionFetch),
            access => Assert.Equal((code, 4), (access.Address, access.Width)),
            access => Assert.Equal((code + 4, 4), (access.Address, access.Width)));
        Assert.Equal(code + 8, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public void DisplacedLongImmediateMovePrefetchesBeforeTheDataWrite(uint cacheControl)
    {
        const uint code = 0x1000;
        const uint destination = 0xDFF050;
        var bus = new SparseRecordingBus();
        bus.Initialize(code - 4, 0x4E7B0002, 4); // MOVEC D0,CACR
        bus.Initialize(code, 0x2B7C0005, 4);
        bus.Initialize(code + 4, 0x32B60050, 4); // MOVE.L #$000532B6,$0050(A5)
        bus.Initialize(code + 8, 0xD1FC0000, 4);
        bus.Initialize(code + 12, 0x66004A39, 4); // ADDA.L #$6600,A0; TST.B ...
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(code - 4, 0x8000);
        cpu.State.D[0] = cacheControl;
        cpu.State.A[5] = 0xDFF000;
        cpu.ExecuteInstruction();
        bus.Accesses.Clear();

        cpu.ExecuteInstruction();

        var firstFetch = bus.Accesses.FindIndex(access =>
            access.Kind == M68kBusAccessKind.CpuInstructionFetch && access.Address == code + 8);
        var secondFetch = bus.Accesses.FindIndex(access =>
            access.Kind == M68kBusAccessKind.CpuInstructionFetch && access.Address == code + 12);
        var write = bus.Accesses.FindIndex(access => access.Write && access.Address == destination);
        Assert.True(firstFetch >= 0 && firstFetch < secondFetch && secondFetch < write);
        Assert.Equal(0x000532B6u, bus.Peek(destination, 4));

        cpu.ExecuteInstruction();

        Assert.Equal(0x6600u, cpu.State.A[0]);
        Assert.Equal(code + 14, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x1B7C, 1, 0xB6u)]
    [InlineData(0x3B7C, 2, 0x32B6u)]
    public void DisplacedByteAndWordImmediateMovesPrefetchBeforeTheDataWrite(
        ushort opcode, int width, uint writtenValue)
    {
        const uint code = 0x1000;
        const uint destination = 0xDFF050;
        var bus = new SparseRecordingBus();
        bus.Initialize(code, opcode, 2);
        bus.Initialize(code + 2, 0x32B60050, 4);
        bus.Initialize(code + 6, 0xD1FC0000, 4);
        bus.Initialize(code + 10, 0x66004E71, 4);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(code, 0x8000);
        cpu.State.A[5] = 0xDFF000;

        cpu.ExecuteInstruction();

        var refill = bus.Accesses.FindIndex(access =>
            access.Kind == M68kBusAccessKind.CpuInstructionFetch && access.Address == code + 8);
        var write = bus.Accesses.FindIndex(access => access.Write && access.Address == destination);
        Assert.True(refill >= 0 && refill < write);
        Assert.Equal(width, bus.Accesses[write].Width);
        Assert.Equal(writtenValue, bus.Peek(destination, width));

        cpu.ExecuteInstruction();

        Assert.Equal(0x6600u, cpu.State.A[0]);
        Assert.Equal(code + 12, cpu.State.ProgramCounter);
    }

    [Fact]
    public void IndexedImmediateMovePrefetchesBeforeReadingTheDestinationPointer()
    {
        const uint code = 0x1000;
        const uint pointer = 0x4100;
        const uint destination = 0xDFF050;
        var bus = new SparseRecordingBus();
        bus.Initialize(code, 0x2BBC0005, 4);
        bus.Initialize(code + 4, 0x32B60171, 4); // MOVE.L #$000532B6,([$100,A5])
        bus.Initialize(code + 8, 0x00000100, 4);
        bus.Initialize(code + 12, 0x4E714E71, 4);
        bus.Initialize(code + 16, 0x4E714E71, 4);
        bus.Initialize(pointer, destination, 4);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(code, 0x8000);
        cpu.State.A[5] = 0x4000;

        cpu.ExecuteInstruction();

        var pointerRead = bus.Accesses.FindIndex(access =>
            access.Kind == M68kBusAccessKind.CpuDataRead && access.Address == pointer);
        var firstRefill = bus.Accesses.FindIndex(access =>
            access.Kind == M68kBusAccessKind.CpuInstructionFetch && access.Address == code + 12);
        var secondRefill = bus.Accesses.FindIndex(access =>
            access.Kind == M68kBusAccessKind.CpuInstructionFetch && access.Address == code + 16);
        var write = bus.Accesses.FindIndex(access => access.Write && access.Address == destination);
        Assert.True(firstRefill >= 0 && firstRefill < secondRefill &&
            secondRefill < pointerRead && pointerRead < write);
        Assert.Equal(0x000532B6u, bus.Peek(destination, 4));
    }
}
