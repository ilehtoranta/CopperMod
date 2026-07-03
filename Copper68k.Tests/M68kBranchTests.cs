using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kBranchTests
{
	[Fact]
	public void TakenBccToOddTargetRaisesAddressError()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0xBADCB2, 0x69AB);
		bus.WriteLong(0x00000C, 0x51C0_2176);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0xBADCB2, 0x000C_6B00);
		cpu.State.StatusRegister = 0x220A;

		cpu.ExecuteInstruction();

		Assert.Equal(0x000C_6AF2u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x69B6, bus.ReadWord(0x000C_6AF2));
		Assert.Equal(0x00BA_DC5Fu, bus.ReadLong(0x000C_6AF4));
		Assert.Equal(0x69AB, bus.ReadWord(0x000C_6AF8));
		Assert.Equal(0x220A, bus.ReadWord(0x000C_6AFA));
		Assert.Equal(0x00BA_DCB4u, bus.ReadLong(0x000C_6AFC));
		Assert.Equal(0x220A, cpu.State.StatusRegister);
		Assert.Equal(0x51C0_2176u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void TakenDbccToOddTargetRaisesAddressErrorBeforeCounterCommit()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1D782A, 0x5ECF, 0x97FD);
		bus.WriteLong(0x00000C, 0xD29C_B826);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1D782A, 0x00F4_8626);
		cpu.State.D[7] = 0xD06D_94BA;
		cpu.State.StatusRegister = 0xA102;

		cpu.ExecuteInstruction();

		Assert.Equal(0xD06D_94BAu, cpu.State.D[7]);
		Assert.Equal(0x00F4_8618u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x5ED6, bus.ReadWord(0x00F4_8618));
		Assert.Equal(0x001D_1029u, bus.ReadLong(0x00F4_861A));
		Assert.Equal(0x5ECF, bus.ReadWord(0x00F4_861E));
		Assert.Equal(0xA102, bus.ReadWord(0x00F4_8620));
		Assert.Equal(0x001D_782Eu, bus.ReadLong(0x00F4_8622));
		Assert.Equal(0x2102, cpu.State.StatusRegister);
		Assert.Equal(0xD29C_B826u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void BsrToOddTargetRaisesAddressErrorBeforeReturnPush()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x9D0E6A, 0x61E5);
		bus.WriteLong(0x00000C, 0xA92C_7906);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x9D0E6A, 0x006A_AE54);
		cpu.State.ResetStackPointers(0x006A_AE54, 0x00B7_255A, supervisorMode: false);
		cpu.State.StatusRegister = 0x030F;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00B7_2556u, cpu.State.UserStackPointer);
		Assert.Equal(0x009D_0E6Cu, bus.ReadLong(0x00B7_2556));
		Assert.Equal(0x006A_AE46u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x61F2, bus.ReadWord(0x006A_AE46));
		Assert.Equal(0x009D_0E51u, bus.ReadLong(0x006A_AE48));
		Assert.Equal(0x61E5, bus.ReadWord(0x006A_AE4C));
		Assert.Equal(0x030F, bus.ReadWord(0x006A_AE4E));
		Assert.Equal(0x009D_0E51u, bus.ReadLong(0x006A_AE50));
		Assert.Equal(0x230F, cpu.State.StatusRegister);
		Assert.Equal(0xA92C_7906u, cpu.State.ProgramCounter);
	}
}
