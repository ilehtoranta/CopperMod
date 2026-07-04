using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kSystemTests
{
	[Fact]
	public void ResetInUserModeRaisesPrivilegeViolation()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E70); // RESET
		bus.WriteLong(8 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.ResetStackPointers(0x5000, 0x3000, supervisorMode: false);
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0, bus.ExternalResetCount);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry), cpu.State.StatusRegister);
		Assert.Equal((ushort)(M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry), bus.ReadWord(0x4FFA));
		Assert.Equal(0x1000u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void ResetInSupervisorModeNotifiesExternalDevices()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E70); // RESET

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);

		cpu.ExecuteInstruction();

		Assert.Equal(1, bus.ExternalResetCount);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void RteInUserModeRaisesPrivilegeViolationWithoutPoppingUserStack()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E73); // RTE
		bus.WriteLong(8 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.ResetStackPointers(0x5000, 0x3000, supervisorMode: false);
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry), cpu.State.StatusRegister);
		Assert.Equal((ushort)(M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry), bus.ReadWord(0x4FFA));
		Assert.Equal(0x1000u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void RteToOddProgramCounterRaisesAddressErrorAfterRestoringStatus()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E73); // RTE
		bus.WriteWord(0x5000, M68kCpuState.ResetStatusRegister);
		bus.WriteLong(0x5002, 0x2101);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF8u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x4FF8u, cpu.State.A[7]);
		Assert.Equal(0x4E76, bus.ReadWord(0x4FF8));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FFA));
		Assert.Equal(0x4E73, bus.ReadWord(0x4FFE));
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x5000));
		Assert.Equal(0x1002u, bus.ReadLong(0x5002));
	}

	[Fact]
	public void RtrRestoresConditionCodesAndReturns()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E77); // RTR
		bus.WriteWord(0x5000, 0x8B7B);
		bus.WriteLong(0x5002, 0x2100);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0100 |
			M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2100u, cpu.State.ProgramCounter);
		Assert.Equal(0x5006u, cpu.State.SupervisorStackPointer);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | 0x0100 |
			M68kCpuState.Extend | M68kCpuState.Negative |
			M68kCpuState.Overflow | M68kCpuState.Carry), cpu.State.StatusRegister);
	}

	[Fact]
	public void RtsToOddProgramCounterRaisesAddressErrorAfterPoppingUserStack()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E75); // RTS
		bus.WriteLong(0x3000, 0x2101);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.ResetStackPointers(0x5000, 0x3000, supervisorMode: false);
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3004u, cpu.State.UserStackPointer);
		Assert.Equal(0x4FF2u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x4E72, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x4E75, bus.ReadWord(0x4FF8));
		Assert.Equal((ushort)(M68kCpuState.Trace | M68kCpuState.Zero), bus.ReadWord(0x4FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void UnlinkOddFramePointerRaisesAddressErrorBeforeChangingStack()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E5C); // UNLK A4
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[4] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2101u, cpu.State.A[4]);
		Assert.Equal(0x4FF2u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4E55u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x4E5C, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}
}
