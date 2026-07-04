using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kJumpTests
{
	[Fact]
	public void JmpOddTargetRaisesInstructionFetchAddressError()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4ED2); // JMP (A2)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[2] = 0x2001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x4ED6u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x4ED2, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void JmpAbsoluteWordOddTargetStacksExtensionWordProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4EF8, 0x2001); // JMP ($2001).W
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x4EF6u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x4EF8, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void JsrOddTargetRaisesAddressErrorBeforePushingReturnAddress()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E90); // JSR (A0)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x2000, supervisorMode: false);
		cpu.State.A[0] = 0x2101;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.UserStackPointer);
		Assert.Equal(0x2FF2u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4E92u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x4E90, bus.ReadWord(0x2FF8));
		Assert.Equal(0x0000, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}
}
