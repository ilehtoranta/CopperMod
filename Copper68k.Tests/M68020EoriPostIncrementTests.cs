using Copper68k;
using Copper68k.Tests.Synthetic;

namespace Copper68k.Tests;

public sealed class M68020EoriPostIncrementTests
{
    [Theory]
    [InlineData((int)M68kInstructionTimingKey.EoriByteImmediateToPostIncrement)]
    [InlineData((int)M68kInstructionTimingKey.EoriWordImmediateToPostIncrement)]
    [InlineData((int)M68kInstructionTimingKey.EoriLongImmediateToPostIncrement)]
    public void PostIncrementPlansRetainReadModifyWriteBarrier(int keyValue)
    {
        var key = (M68kInstructionTimingKey)keyValue;
        Assert.True((M68020TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
        Assert.True((M68030TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
    }

    // Motorola M68000PRM sections 2.2.4 and EORI: sized writes, one EA
    // increment (two for byte A7), X preserved, N/Z from result, V/C cleared.
    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x0A18, 0xFF9Eu, 0x01ADBEEFu, 0x9FADBEEFu, 1u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A18, 0xFF9Eu, 0x9EADBEEFu, 0x00ADBEEFu, 1u, 0x14)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A1F, 0x80u, 0x01ADBEEFu, 0x81ADBEEFu, 2u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A58, 0x8001u, 0x0001BEEFu, 0x8000BEEFu, 2u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A58, 0x0001u, 0x0001BEEFu, 0x0000BEEFu, 2u, 0x14)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A98, 0x80000001u, 1u, 0x80000000u, 4u, 0x18)]
    [InlineData(M68kCpuModel.M68020, 0x0A18, 0x9Eu, 0x01ADBEEFu, 0x9FADBEEFu, 1u, 0x18)]
    [InlineData(M68kCpuModel.M68020, 0x0A58, 0x8001u, 0x0001BEEFu, 0x8000BEEFu, 2u, 0x18)]
    [InlineData(M68kCpuModel.M68020, 0x0A98, 0x80000001u, 1u, 0x80000000u, 4u, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x0A18, 0x9Eu, 0x01ADBEEFu, 0x9FADBEEFu, 1u, 0x18)]
    public void EoriPostIncrementWritesOriginalAddressOnceAndUpdatesFlags(
        M68kCpuModel model, ushort opcode, uint immediate, uint value, uint expected, uint increment, int flags)
    {
        var bus = new SparseRecordingBus();
        var longSize = (opcode & 0xC0) == 0x80;
        var width = longSize ? 4 : (opcode & 0xC0) == 0x40 ? 2 : 1;
        bus.Initialize(0xF80000, opcode, 2);
        bus.Initialize(0xF80002, immediate, longSize ? 4 : 2);
        bus.Initialize(longSize ? 0xF80006u : 0xF80004u, 0x4E71, 2);
        bus.Initialize(0x2020, value, 4);
        bus.Initialize(0x2024, 0x12345678, 4);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.A[opcode & 7] = 0x2020;
        cpu.State.StatusRegister = 0x201F;
        var beforeD = (uint[])cpu.State.D.Clone();
        var beforeA = (uint[])cpu.State.A.Clone();
        bus.Accesses.Clear();
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.Peek(0x2020, 4));
        Assert.Equal(0x12345678u, bus.Peek(0x2024, 4));
        Assert.Equal(0x2020u + increment, cpu.State.A[opcode & 7]);
        Assert.Equal(beforeD, cpu.State.D);
        for (var register = 0; register < 8; register++)
            if (register != (opcode & 7)) Assert.Equal(beforeA[register], cpu.State.A[register]);
        var writes = bus.Accesses.Where(access => access.Write).ToArray();
        Assert.NotEmpty(writes);
        Assert.Equal(width, writes.Sum(access => access.Width));
        for (var offset = 0; offset < width; offset++)
            Assert.Single(writes.Where(access => access.Address <= 0x2020u + offset &&
                access.Address + access.Width > 0x2020u + offset));
        Assert.All(writes, access => Assert.InRange(access.Address, 0x2020u, 0x2020u + (uint)width - 1));
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(longSize ? 0xF80006u : 0xF80004u, cpu.State.ProgramCounter);
    }
}
