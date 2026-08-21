using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;
namespace Copper68k.Tests;
public sealed class M68020InterpreterTests
{
	[Fact]
	public void FactoryCreatesAccurateM68020Backend()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68020, new Copper68kTestBus());
		var interpreter = Assert.IsType<M68020Interpreter>(cpu);
		Assert.Same(M68020CpuProfile.OcsAccelerator14Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}
	[Fact]
	public void FactoryCreatesAccurateM68030Backend()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68030, new Copper68kTestBus());
		var interpreter = Assert.IsType<M68030Interpreter>(cpu);
		Assert.Same(M68020CpuProfile.Ocs68030Accelerator14Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}
	[Fact]
	public void MovecTransfersVectorBaseRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, 0x0801, // MOVEC D0,VBR
			0x4E7A, 0x1801); // MOVEC VBR,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0400;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0400u, cpu.State.VectorBaseRegister);
		Assert.Equal(0x0000_0400u, cpu.State.D[1]);
		Assert.Equal(24, cpu.State.NativeCycles);
		Assert.Equal(12, cpu.State.Cycles);
	}
	[Fact]
	public void M68020ExecutesM68020OnlyExtbLong()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x49C0); // EXTB.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0080;
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(0xFFFF_FF80u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}
	[Fact]
	public void MovecUnsupportedControlRegisterRaisesIllegalWithoutClobberingDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E7A, 0x1808); // MOVEC PCR,D1
		bus.WriteLong(4u * 4, 0x0000_2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_5678;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x1234_5678u, cpu.State.D[1]);
		Assert.Equal(0x3000u - 8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x3000u - 6u));
		Assert.Equal(4 * 4, bus.ReadWord(0x3000u - 2u));
	}
	[Fact]
	public void MovepLongReadsSpacedMemoryBytesIntoDataRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0549, 0x0010); // MOVEP.L $10(A1),D2
		bus.WriteWord(0x3010, 0x1200);
		bus.WriteWord(0x3012, 0x3400);
		bus.WriteWord(0x3014, 0x5600);
		bus.WriteWord(0x3016, 0x7800);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x3000;
		cpu.State.D[2] = 0xFFFF_FFFF;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.D[2]);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void MovepWordWritesSpacedMemoryBytesAndPreservesStatusRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0189, 0x0010); // MOVEP.W D0,$10(A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x3000;
		cpu.State.D[0] = 0xAABB_CDEF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xCD, ReadByte(bus, 0x3010));
		Assert.Equal(0xEF, ReadByte(bus, 0x3012));
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void LongBraUsesThirtyTwoBitDisplacement()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x60FF, 0x0000, 0x0008);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 0x0Au, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void LineAExceptionUsesVectorBaseAndFormatZeroFrame()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xA000);
		bus.WriteLong(0x0400 + (10u * 4), 0x0000_2000);
		WriteWords(bus, 0x2000, 0x4E73); // RTE
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(bus.ReadWord(0x2FF8) & M68kCpuState.Supervisor));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(10 * 4, bus.ReadWord(0x2FFE));
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}
	[Fact]
	public void ImmediateToStatusRegisterInUserModeUsesFormatZeroPrivilegeFrame()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x007C, 0x2000); // ORI.W #$2000,SR
		bus.WriteLong(8u * 4, 0x0000_2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x4000, supervisorMode: false);
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(cpu.State.StatusRegister & M68kCpuState.Supervisor));
		Assert.Equal(0x0000, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(8 * 4, bus.ReadWord(0x2FFE));
		Assert.Equal(0x4000u, cpu.State.UserStackPointer);
	}
	[Fact]
	public void ImmediateToStatusRegisterInSupervisorModeUpdatesStatusRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x007C, 0x0700); // ORI.W #$0700,SR
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(0x2700, cpu.State.StatusRegister);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}
	[Fact]
	public void ResetNotifiesExternalDevicesWithoutResettingCpu()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E70); // RESET
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[0] = 0x8765_4321;
		var initialStatus = cpu.State.StatusRegister;

		cpu.ExecuteInstruction();

		Assert.Equal(1, bus.ExternalResetCount);
		Assert.True(bus.LastExternalResetCycle >= 0);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(0x8765_4321u, cpu.State.A[0]);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(initialStatus, cpu.State.StatusRegister);
		Assert.Equal(518, cpu.State.NativeCycles);
		Assert.Equal(259, cpu.State.Cycles);
	}
	[Fact]
	public void ResetInUserModeRaisesPrivilegeViolationWithoutResettingDevices()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E70); // RESET
		bus.WriteLong(8u * 4, 0x0000_2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x4000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.Equal(0, bus.ExternalResetCount);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFAu));
		Assert.Equal(8 * 4, bus.ReadWord(0x2FFEu));
	}

	[Fact]
	public void StopWaitsAtTheFollowingInstructionUntilAnAcceptedInterrupt()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E72, 0x2200, 0x4E71); // STOP #$2200; NOP
		bus.WriteLong(28u * 4, 0x0000_2400); // level 4 autovector
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);

		var stopCycles = cpu.ExecuteInstruction();

		Assert.True(cpu.State.Stopped);
		Assert.False(cpu.State.Halted);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal((ushort)0x2200, cpu.State.StatusRegister);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, stopCycles);

		var idleCycles = cpu.ExecuteInstruction();
		Assert.True(cpu.State.Stopped);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(1, idleCycles);

		cpu.RequestInterrupt(2, 28u * 4);
		Assert.True(cpu.State.Stopped);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);

		cpu.RequestInterrupt(4, 28u * 4);
		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x0000_2400u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal((ushort)0x2200, bus.ReadWord(0x2FF8u));
		Assert.Equal(CodeBase + 4u, bus.ReadLong(0x2FFAu));
		Assert.Equal(28 * 4, bus.ReadWord(0x2FFEu));
	}

	[Fact]
	public void StopInUserModeRaisesPrivilegeViolationBeforeReadingItsImmediateWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E72, 0x2700); // STOP #$2700
		bus.WriteLong(8u * 4, 0x0000_2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x4000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFAu));
		Assert.Equal(8 * 4, bus.ReadWord(0x2FFEu));
	}

	[Fact]
	public void MoveUspUsesBothSupervisorDirectionsWithoutChangingActiveStack()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E60, 0x4E69); // MOVE A0,USP; MOVE USP,A1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x1234_5678;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.UserStackPointer);
		Assert.Equal(0x1234_5678u, cpu.State.A[1]);
		Assert.Equal(0x0000_3000u, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressDisplacementToAbsoluteLongCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x33E9, 0x0010, 0x0000, 0x6000); // MOVE.W $10(A1),$6000.L
		var cycle = 0L;
		bus.WriteWord(0x0000_4010, 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x8001, bus.ReadWord(0x0000_6000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveStatusRegisterToAddressIndirectWritesEntireStatusWithoutChangingIt()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x40D7); // MOVE.W SR,(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[7] = 0x0000_2400;
		var status = (ushort)(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry);
		cpu.State.StatusRegister = status;

		cpu.ExecuteInstruction();

		Assert.Equal(status, bus.ReadWord(0x0000_2400));
		Assert.Equal(status, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(5, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveStatusRegisterToDataRegisterWritesLowWordWithoutChangingStatus()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x40C0); // MOVE.W SR,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		var status = (ushort)(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry);
		cpu.State.StatusRegister = status;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_201Fu, cpu.State.D[0]);
		Assert.Equal(status, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void MoveDataRegisterToStatusRegisterReplacesEntireStatusInSupervisorMode()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x46C0); // MOVE.W D0,SR
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_2305;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x2305, cpu.State.StatusRegister);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveStatusRegisterInUserModeRaisesPrivilegeViolationBeforeWritingMemory()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x40D7); // MOVE.W SR,(A7)
		bus.WriteLong(8u * 4, 0x0000_2000);
		bus.WriteWord(0x0000_4000, 0xA55A);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x4000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0xA55A, bus.ReadWord(0x0000_4000));
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFAu));
		Assert.Equal(8 * 4, bus.ReadWord(0x2FFEu));
	}

	[Fact]
	public void InterruptUsesVectorBaseRegister()
	{
		var bus = new ZeroWaitCodeBus();
		bus.WriteLong(0x0400 + (27u * 4), 0x0000_2400);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.RequestInterrupt(3, 27u * 4);
		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(27 * 4, bus.ReadWord(0x2FFE));
		Assert.Equal(3, (cpu.State.StatusRegister >> 8) & 7);
	}
	[Fact]
	public void LinkLongUsesThirtyTwoBitFrameDisplacement()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x480E, 0xFFFF, 0xFFF0); // LINK.L A6,#-16
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x1234_5678;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2FFCu, cpu.State.A[6]);
		Assert.Equal(0x2FECu, cpu.State.A[7]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void LinkWordAndUnlinkCreateAndRestoreFrame()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E56, 0xFFF0, // LINK.W A6,#-16
			0x4E5E); // UNLK A6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFCu, cpu.State.A[6]);
		Assert.Equal(0x2FECu, cpu.State.A[7]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x2FFC));

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0x1234_5678u, cpu.State.A[6]);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}
	[Fact]
	public void ExtLongDataRegisterSignExtendsWordToLong()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48C0); // EXT.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_8003;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_8003u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void ExtWordDataRegisterSignExtendsByteToWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4880); // EXT.W D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_0080;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FF80u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void NotByteDataRegisterComplementsLowByteAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4600); // NOT.B D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_56FFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void NotByteAddressDisplacementComplementsMemoryAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x462D, 0x0010); // NOT.B $10(A5)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x7F, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal((byte)0x80, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void NotWordDataRegisterComplementsLowWordAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4643); // NOT.W D3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[3] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void NotLongDataRegisterComplementsValueAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4685); // NOT.L D5
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[5] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xEDCB_A9FFu, cpu.State.D[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void ClrLongDataRegisterClearsRegisterAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4280); // CLR.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void ClrWordDataRegisterClearsLowWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4240); // CLR.W D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0000u, cpu.State.D[0]);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void ClrLongPostIncrementClearsMemoryIncrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x429B); // CLR.L (A3)+
		bus.WriteLong(0x0006_C47A, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0006_C47A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, bus.ReadLong(0x0006_C47A));
		Assert.Equal(0x0006_C47Eu, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void ClrLongAddressIndirectClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4290); // CLR.L (A0)
		bus.WriteLong(0x0000_0400, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, bus.ReadLong(0x0000_0400));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void ClrLongAddressDisplacementClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x42A8, 0x0004); // CLR.L $0004(A0)
		bus.WriteLong(0x0000_0404, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, bus.ReadLong(0x0000_0404));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void ClrLongAbsoluteLongClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x42B9, 0x0000, 0x0700); // CLR.L $700.L
		bus.WriteLong(0x0000_0700, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, bus.ReadLong(0x0000_0700));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void ClrWordAddressDisplacementClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4268, 0x0008); // CLR.W $0008(A0)
		bus.WriteWord(0x0000_0408, 0xFFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0, bus.ReadWord(0x0000_0408));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void ClrByteAddressIndirectClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4210); // CLR.B (A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_0400, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0000_0400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void ClrByteAddressDisplacementClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x422E, 0x0245); // CLR.B $0245(A6)
		var cycle = 0L;
		bus.WriteByte(0x0006_E6C3, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0006_E6C3, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void ClrLongAbsoluteWordClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x42B8, 0x4000); // CLR.L $4000.W
		bus.WriteLong(0x0000_4000, 0x1234_5678);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0u, bus.ReadLong(0x0000_4000));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void ClrWordPostIncrementClearsMemoryIncrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4258); // CLR.W (A0)+
		bus.WriteWord(0x0000_2400, 0xA55A);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000, bus.ReadWord(0x0000_2400));
		Assert.Equal(0x0000_2402u, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void ClrBytePostIncrementUsesStackPointerByteIncrementAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x421F); // CLR.B (A7)+
		var cycle = 0L;
		bus.WriteByte(0x0000_3000, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0000_3000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_3002u, cpu.State.A[7]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void ClrLongPredecrementClearsMemoryDecrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x42A0); // CLR.L -(A0)
		bus.WriteLong(0x0000_23FC, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0u, bus.ReadLong(0x0000_23FC));
		Assert.Equal(0x0000_23FCu, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void ClrWordPredecrementClearsMemoryDecrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4260); // CLR.W -(A0)
		bus.WriteWord(0x0000_23FE, 0xFFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0, bus.ReadWord(0x0000_23FE));
		Assert.Equal(0x0000_23FEu, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void ClrByteAbsoluteLongClearsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4239, 0x0006, 0xE6C3); // CLR.B $0006E6C3.L
		var cycle = 0L;
		bus.WriteByte(0x0006_E6C3, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0006_E6C3, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void LeaAbsoluteLongLoadsAddressRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x41F9, 0x0000, 0x0400); // LEA $00000400.L,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0400u, cpu.State.A[0]);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void LeaAbsoluteLongLoadsActiveStackPointerForA7()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4FF9, 0x0000, 0x0400); // LEA $00000400.L,A7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0400u, cpu.State.A[7]);
		Assert.Equal(0x0000_0400u, cpu.State.SupervisorStackPointer);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void LeaAbsoluteWordLoadsSignExtendedAddressRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x41F8, 0xFFFE); // LEA $FFFE.W,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFEu, cpu.State.A[0]);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void LeaAddressDisplacementLoadsAddressRegisterWithoutChangingFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x41EE, 0x0040); // LEA $0040(A6),A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0006_E4BEu, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteImmediateToAbsoluteLongWritesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x13FC, 0x00FF, 0x00BF, 0xE200); // MOVE.B #$FF,$BFE200.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0xFF, bus.ReadByte(0x00BF_E200, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteImmediateToAddressIndirectWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x10BC, 0x0024); // MOVE.B #$24,(A0)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x24, bus.ReadByte(0x0000_0400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordImmediateToAbsoluteLongWritesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x33FC, 0x8001, 0x00DF, 0xF180); // MOVE.W #$8001,$DFF180.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8001, bus.ReadWord(0x00DF_F180));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToAbsoluteLongWritesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x23FC, 0x00F8, 0x9CB0, 0x0000, 0x0008); // MOVE.L #$00F89CB0,$000008.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_9CB0u, bus.ReadLong(0x0000_0008));
		Assert.Equal(CodeBase + 10u, cpu.State.ProgramCounter);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToAbsoluteWordWritesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x21FC, 0x89AB, 0xCDEF, 0x0800); // MOVE.L #$89ABCDEF,$0800.W
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_0800));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}

	[Fact]
	public void MoveWordImmediateToAddressIndirectWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x30BC, 0x8000); // MOVE.W #$8000,(A0)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8000, bus.ReadWord(0x0000_2400));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToAddressIndirectWritesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2CBC, 0x3333, 0x3333); // MOVE.L #$33333333,(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x3333_3333u, bus.ReadLong(0x0000_0400));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToAddressDisplacementWritesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2D7C, 0x00F9, 0x4B06, 0x0086); // MOVE.L #$00F94B06,$0086(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F9_4B06u, bus.ReadLong(0x0006_E504));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToPostIncrementWritesMemoryIncrementsAddressAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2AFC, 0x4250, 0x4C31); // MOVE.L #$42504C31,(A5)+
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0006_F634;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x4250_4C31u, bus.ReadLong(0x0006_F634));
		Assert.Equal(0x0006_F638u, cpu.State.A[5]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToPredecrementUpdatesAddressBeforeWritingAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2F3C, 0x8000, 0x0001); // MOVE.L #$80000001,-(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_2FFC));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveqSignExtendsImmediateAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x72FF); // MOVEQ #-1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFFu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void NegLongDataRegisterSetsCarryAndExtendForNonZero()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4480); // NEG.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 1;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void NegLongDataRegisterClearsCarryAndExtendForZero()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4480); // NEG.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void NegxLongDataRegisterUsesExtendAndStickyZero()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4080); // NEGX.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 1;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFEu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void OriByteImmediateToAbsoluteLongUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0039, 0x00F8, 0x00BF, 0xD100); // ORI.B #$F8,$BFD100.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_D100, 0x01, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0xF9, bus.ReadByte(0x00BF_D100, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void AndiByteImmediateToAbsoluteLongUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0239, 0x0087, 0x00BF, 0xD100); // ANDI.B #$87,$BFD100.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_D100, 0x78, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x00BF_D100, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongImmediateToDataRegisterWritesRegisterAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2C3C, 0x0000, 0x0005); // MOVE.L #$00000005,D6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0005u, cpu.State.D[6]);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongDataToDataRegisterWritesRegisterAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2C01); // MOVE.L D1,D6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongDataToAddressIndirectWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2C85); // MOVE.L D5,(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[5] = 0x3333_3333;
		cpu.State.A[6] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x3333_3333u, bus.ReadLong(0x0000_0400));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongDataToPostIncrementWritesMemoryAndUpdatesAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x20C1); // MOVE.L D1,(A0)+
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x8000_0001;
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_2400));
		Assert.Equal(0x0000_2404u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongDataToPredecrementUpdatesAddressBeforeWriting()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2702); // MOVE.L D2,-(A3)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x89AB_CDEF;
		cpu.State.A[3] = 0x0000_2404;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2400u, cpu.State.A[3]);
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_2400));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressIndirectToPredecrementReadsSourceBeforeUpdatingSharedAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2F17); // MOVE.L (A7),-(A7)
		bus.WriteLong(0x0000_2400, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x2400);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_23FCu, cpu.State.A[7]);
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_23FC));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordPostincrementToPredecrementReadsSourceBeforeUpdatingSharedAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3F1F); // MOVE.W (A7)+,-(A7)
		var cycle = 0L;
		bus.WriteWord(0x0000_2400, 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x2400);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x0000_2400u, cpu.State.A[7]);
		Assert.Equal(0x8001, bus.ReadWord(0x0000_2400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongPostincrementToPredecrementReadsSourceBeforeUpdatingSharedAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2F1F); // MOVE.L (A7)+,-(A7)
		bus.WriteLong(0x0000_2400, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x2400);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2400u, cpu.State.A[7]);
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_2400));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressIndirectToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2752, 0x0010); // MOVE.L (A2),$10(A3)
		bus.WriteLong(0x0000_2400, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.A[3] = 0x0000_2504;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2400u, cpu.State.A[2]);
		Assert.Equal(0x0000_2504u, cpu.State.A[3]);
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_2514));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongDataToAddressDisplacementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2D41, 0x00C0); // MOVE.L D1,$00C0(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0xFFFF_FFFF;
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFFu, bus.ReadLong(0x0006_E53E));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongDataToBriefIndexedWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2780, 0x1810); // MOVE.L D0,$10(A3,D1.L)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0001;
		cpu.State.D[1] = 2;
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_4012));
		Assert.Equal(0x8000_0001u, cpu.State.D[0]);
		Assert.Equal(2u, cpu.State.D[1]);
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordImmediateToDataRegisterWritesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x323C, 0x0100); // MOVE.W #$0100,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0100u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3403); // MOVE.W D3,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[3] = 0x1234_8001;
		cpu.State.D[2] = 0xABCD_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xABCD_8001u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordDataToBriefIndexedWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3781, 0x2810); // MOVE.W D1,$10(A3,D2.L)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_8001;
		cpu.State.D[2] = 2;
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8001, bus.ReadWord(0x0000_4012));
		Assert.Equal(0x1234_8001u, cpu.State.D[1]);
		Assert.Equal(2u, cpu.State.D[2]);
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordBriefIndexedToAddressDisplacementConsumesSourceExtensionFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3570, 0x0804, 0x0010); // MOVE.W 4(A0,D0.L),$10(A2)
		bus.WriteWord(0x0000_400C, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.A[2] = 0x0000_5000;
		cpu.State.D[0] = 8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8001, bus.ReadWord(0x0000_5010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void NegWordDataRegisterPreservesUpperWordAndSetsOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4442); // NEG.W D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_8000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void ClrByteDataRegisterClearsLowByteAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4200); // CLR.B D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5600u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordImmediateToAddressDisplacementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3D7C, 0x0005, 0x0082); // MOVE.W #5,$0082(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0005, bus.ReadWord(0x0006_E500));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordImmediateToPredecrementUpdatesAddressAndWritesMemory()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x313C, 0x8001); // MOVE.W #$8001,-(A0)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4002;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.Equal(0x8001, bus.ReadWord(0x0000_4000));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToPredecrementUpdatesAddressBeforeWritingAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3F02); // MOVE.W D2,-(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x2FFEu, cpu.State.A[7]);
		Assert.Equal(0x8001, bus.ReadWord(0x0000_2FFE, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void MoveByteImmediateToDataRegisterWritesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x123C, 0x00AA); // MOVE.B #$AA,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_56AAu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteImmediateToAddressDisplacementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1D7C, 0x0000, 0x00FA); // MOVE.B #0,$00FA(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0, bus.ReadByte(0x0006_E578, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteImmediateToBriefIndexedWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x11BC, 0x0000, 0x0000); // MOVE.B #0,0(A0,D0.W)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2000;
		cpu.State.D[0] = 0x0001_0010;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0, bus.ReadByte(0x0000_2010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongDataToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2840); // MOVEA.L D0,A4
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.A[4]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongImmediateToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2A7C, 0x00F8, 0x245A); // MOVEA.L #$00F8245A,A5
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_245Au, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaWordImmediateToAddressRegisterSignExtendsAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x307C, 0x8001); // MOVEA.W #$8001,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_8001u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressToDataRegisterSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2E0C); // MOVE.L A4,D7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[4] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[7]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongAddressToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2C4D); // MOVEA.L A5,A6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x00FA_73FA;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00FA_73FAu, cpu.State.A[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressToAddressIndirectWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x268B); // MOVE.L A3,(A3)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0008_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0008_0000u, bus.ReadLong(0x0008_0000));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressToStackPredecrementCapturesSourceBeforeUpdatingA7()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2F0F); // MOVE.L A7,-(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0x0000_3000u, bus.ReadLong(0x0000_2FFC));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressToAddressDisplacementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2D4E, 0x0008); // MOVE.L A6,$0008(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0006_E47Eu, bus.ReadLong(0x0006_E486));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressToPostIncrementWritesOriginalAddressAndIncrements()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x26CB); // MOVE.L A3,(A3)+
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0006_C47A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0006_C47Au, bus.ReadLong(0x0006_C47A));
		Assert.Equal(0x0006_C47Eu, cpu.State.A[3]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressIndirectToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2015); // MOVE.L (A5),D0
		bus.WriteLong(0x0000_2000, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongAddressIndirectToAddressRegisterReadsMemoryAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2250); // MOVEA.L (A0),A1
		bus.WriteLong(0x00F9_0B2E, 0x00F8_FC32);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00F9_0B2E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_FC32u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongPostIncrementToDataRegisterReadsMemoryIncrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2019); // MOVE.L (A1)+,D0
		bus.WriteLong(0x0006_E48A, 0x0006_F638);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0006_E48A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0006_F638u, cpu.State.D[0]);
		Assert.Equal(0x0006_E48Eu, cpu.State.A[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongPostIncrementToAddressRegisterReadsMemoryIncrementsSourceAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2059); // MOVEA.L (A1)+,A0
		bus.WriteLong(0x00F9_0D02, 0x00F8_FC3C);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x00F9_0D02;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_FC3Cu, cpu.State.A[0]);
		Assert.Equal(0x00F9_0D06u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongPredecrementToDataRegisterUpdatesAddressBeforeReadAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2021); // MOVE.L -(A1),D0
		bus.WriteLong(0x0000_4000, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4004;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, cpu.State.D[0]);
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveaWordPostIncrementToAddressRegisterSignExtendsAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x305A); // MOVEA.W (A2)+,A0
		bus.WriteWord(0x0000_2400, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_8001u, cpu.State.A[0]);
		Assert.Equal(0x0000_2402u, cpu.State.A[2]);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveaWordAddressIndirectToAddressRegisterSignExtendsAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3450); // MOVEA.W (A0),A2
		bus.WriteWord(0x0000_4000, 0xFFFC);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_FFFCu, cpu.State.A[2]);
		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressDisplacementToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x222E, 0x00A0); // MOVE.L $00A0(A6),D1
		bus.WriteLong(0x0006_E51E, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongAddressDisplacementToAddressRegisterReadsMemoryAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x206E, 0x0586); // MOVEA.L $0586(A6),A0
		bus.WriteLong(0x0006_EA04, 0x00F9_05A4);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F9_05A4u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressDisplacementToPostIncrementUsesOriginalBaseAndIncrementsDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x20E8, 0x0004); // MOVE.L $0004(A0),(A0)+
		bus.WriteLong(0x0000_0404, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_0400));
		Assert.Equal(0x0000_0404u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongBriefIndexedToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2030, 0x0000); // MOVE.L 0(A0,D0.W),D0
		bus.WriteLong(0x00F9_0AFA, 0x00DF_F032);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00F9_0AF6;
		cpu.State.D[0] = 0x0000_0004;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00DF_F032u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaLongBriefIndexedToAddressRegisterReadsMemoryAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2070, 0x0800); // MOVEA.L 0(A0,D0.L),A0
		bus.WriteLong(0x00F9_0B12, 0x00F8_FC26);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00F9_0B12;
		cpu.State.D[0] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_FC26u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveaLongPcBriefIndexedToAddressRegisterUsesExtensionAddressAsBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x207B, 0x1808); // MOVEA.L 8(PC,D1.L),A0
		bus.WriteLong(CodeBase + 2u + 0x100u + 8u, 0x1234_5678);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x0000_0100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.A[0]);
		Assert.Equal((ushort)0x201F, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressIndirectToAddressIndirectCopiesLongAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2C95); // MOVE.L (A5),(A6)
		bus.WriteLong(0x0000_2000, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_2000;
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_2400));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
		[Fact]
		public void MoveLongAbsoluteWordToDataRegisterSignExtendsAddressAndSetsFlags()
		{
			var bus = new ZeroWaitCodeBus();
			WriteWords(bus, CodeBase, 0x2238, 0xFFE6); // MOVE.L $FFE6.W,D1
			bus.WriteLong(0xFFFF_FFE6, 0x8000_0000);
			var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
			cpu.Reset(CodeBase, 0x3000);
			cpu.State.StatusRegister = M68kCpuState.Supervisor |
				M68kCpuState.Extend |
				M68kCpuState.Zero |
				M68kCpuState.Overflow |
				M68kCpuState.Carry;

			cpu.ExecuteInstruction();

			Assert.Equal(0x8000_0000u, cpu.State.D[1]);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
			Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
			Assert.Equal(6, cpu.State.NativeCycles);
			Assert.Equal(3, cpu.State.Cycles);
		}

		[Fact]
		public void MoveLongAbsoluteWordToAddressRegisterSignExtendsAddressAndPreservesFlags()
		{
			var bus = new ZeroWaitCodeBus();
			WriteWords(bus, CodeBase, 0x2478, 0xFFE6); // MOVEA.L $FFE6.W,A2
			bus.WriteLong(0xFFFF_FFE6, 0x8123_4567);
			var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
			cpu.Reset(CodeBase, 0x3000);
			cpu.State.StatusRegister = M68kCpuState.Supervisor |
				M68kCpuState.Extend |
				M68kCpuState.Zero |
				M68kCpuState.Overflow |
				M68kCpuState.Carry;

			cpu.ExecuteInstruction();

			Assert.Equal(0x8123_4567u, cpu.State.A[2]);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
			Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
			Assert.Equal(6, cpu.State.NativeCycles);
			Assert.Equal(3, cpu.State.Cycles);
		}

		[Fact]
		public void MoveLongAbsoluteLongToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2839, 0x00FF, 0xFFE6); // MOVE.L $FFFFE6.L,D4
		bus.WriteLong(0x00FF_FFE6, 0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[4]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongDataToAbsoluteLongWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x23C0, 0x0000, 0x0004); // MOVE.L D0,$000004.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_0004));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressToAbsoluteLongWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x23CC, 0x0000, 0x0004); // MOVE.L A4,$000004.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[4] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_0004));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveBytePostIncrementToDataRegisterIncrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1E18); // MOVE.B (A0)+,D7
		var cycle = 0L;
		bus.WriteByte(0x0000_2000, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2000;
		cpu.State.D[7] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2001u, cpu.State.A[0]);
		Assert.Equal(0x1234_5680u, cpu.State.D[7]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteAddressIndirectToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1010); // MOVE.B (A0),D0
		var cycle = 0L;
		bus.WriteByte(0x0000_2000, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2000;
		cpu.State.D[0] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2000u, cpu.State.A[0]);
		Assert.Equal(0x1234_5600u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteAddressDisplacementToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1C2E, 0x0100); // MOVE.B $0100(A6),D6
		var cycle = 0L;
		bus.WriteByte(0x0006_E57E, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.D[6] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5680u, cpu.State.D[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteBriefIndexedToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1230, 0x0000); // MOVE.B 0(A0,D0.W),D1
		var cycle = 0L;
		bus.WriteByte(0x00F9_0AFA, 0x7F, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00F9_0AF6;
		cpu.State.D[0] = 0x0000_0004;
		cpu.State.D[1] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_567Fu, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToDataRegisterWritesLowByteAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1207); // MOVE.B D7,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_5600;
		cpu.State.D[7] = 0x0000_00FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_56FFu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteAbsoluteLongToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1239, 0x00BF, 0xE001); // MOVE.B $BFE001.L,D1
		var cycle = 0L;
		bus.WriteByte(0x00BF_E001, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5600u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordAbsoluteLongToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3239, 0x00DF, 0xF018); // MOVE.W $DFF018.L,D1
		bus.WriteWord(0x00DF_F018, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordAddressDisplacementToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x302E, 0x0082); // MOVE.W $0082(A6),D0
		bus.WriteWord(0x0006_E500, 0x0004);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_0000;
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0004u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MoveBytePcBriefIndexedToDataRegisterUsesExtensionAddressAsBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x103B, 0x1808); // MOVE.B 8(PC,D1.L),D0
		var cycle = 0L;
		bus.WriteByte(CodeBase + 2u + 0x100u + 8u, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.D[1] = 0x0000_0100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5680u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressIndirectToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3152, 0x0010); // MOVE.W (A2),$10(A0)
		bus.WriteWord(0x0000_4000, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_4000;
		cpu.State.A[0] = 0x0000_5000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8001, bus.ReadWord(0x0000_5010));
		Assert.Equal(0x0000_4000u, cpu.State.A[2]);
		Assert.Equal(0x0000_5000u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordBriefIndexedToDataRegisterPreservesUpperWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3030, 0x1810); // MOVE.W $10(A0,D1.L),D0
		bus.WriteWord(0x0000_4012, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.D[0] = 0x1234_0000;
		cpu.State.D[1] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8001u, cpu.State.D[0]);
		Assert.Equal(2u, cpu.State.D[1]);
		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAddressDisplacementToAbsoluteLongCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x23EF, 0x0010, 0x0000, 0x6000); // MOVE.L $10(A7),$6000.L
		var cycle = 0L;
		bus.WriteLong(0x0000_3010, 0x8000_0001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_6000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongPcBriefIndexedToAddressDisplacementUsesExtensionAddressAsBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x297B, 0x0020, 0x0010); // MOVE.L $20(PC,D0.W),$10(A4)
		bus.WriteLong(CodeBase + 0x22, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0;
		cpu.State.A[4] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_2410));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveaWordAbsoluteLongToAddressRegisterSignExtendsAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3079, 0x0000, 0x5000); // MOVEA.W $5000.L,A0
		var cycle = 0L;
		bus.WriteWord(0x0000_5000, 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_8001u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordPcBriefIndexedToDataRegisterUsesExtensionWordAddressAsBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x323B, 0x0004); // MOVE.W 4(PC,D0.W),D1
		bus.WriteWord(CodeBase + 0x16u, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0010;
		cpu.State.D[1] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordPcBriefIndexedToPredecrementUsesExtensionAddressAndUpdatesDestinationAfterRead()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x313B, 0x1810); // MOVE.W $10(PC,D1.L),-(A0)
		var cycle = 0L;
		bus.WriteWord(0x0000_4012, 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x0000_3000;
		cpu.State.A[0] = 0x0000_5000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x0000_4FFEu, cpu.State.A[0]);
		Assert.Equal(0x8001, bus.ReadWord(0x0000_4FFE, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordPostIncrementToPostIncrementCopiesMemoryAndIncrementsAddresses()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x30D9); // MOVE.W (A1)+,(A0)+
		bus.WriteWord(0x0000_2400, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2600;
		cpu.State.A[1] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8001, bus.ReadWord(0x0000_2600));
		Assert.Equal(0x0000_2602u, cpu.State.A[0]);
		Assert.Equal(0x0000_2402u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToPostIncrementWritesMemoryAndIncrementsDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x30C1); // MOVE.W D1,(A0)+
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_8001;
		cpu.State.A[0] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8001, bus.ReadWord(0x0000_2400));
		Assert.Equal(0x0000_2402u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongPostIncrementToPostIncrementCopiesMemoryAndIncrementsAddresses()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x22D8); // MOVE.L (A0)+,(A1)+
		bus.WriteLong(0x0000_2400, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.A[1] = 0x0000_2600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_2600));
		Assert.Equal(0x0000_2404u, cpu.State.A[0]);
		Assert.Equal(0x0000_2604u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordPostIncrementToAddressDisplacementCopiesMemoryAndIncrementsSource()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x355B, 0x0010); // MOVE.W (A3)+,$10(A2)
		bus.WriteWord(0x0000_3000, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x4000);
		cpu.State.A[2] = 0x0000_2000;
		cpu.State.A[3] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8001, bus.ReadWord(0x0000_2010));
		Assert.Equal(0x0000_3002u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToAbsoluteLongWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x33C1, 0x00DF, 0xF030); // MOVE.W D1,$DFF030.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8001, bus.ReadWord(0x00DF_F030));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToAddressDisplacementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3141, 0x0002); // MOVE.W D1,$0002(A0)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0006_EA3C;
		cpu.State.D[1] = 0xEEBA_0006;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0006, bus.ReadWord(0x0006_EA3E));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordAbsoluteLongToAbsoluteLongCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x33F9, 0x00DF, 0xF006, 0x00DF, 0xF180); // MOVE.W $DFF006.L,$DFF180.L
		bus.WriteWord(0x00DF_F006, 0x8123);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8123, bus.ReadWord(0x00DF_F180));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 10u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordAbsoluteLongToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3D79, 0x00DF, 0xF000, 0x0548); // MOVE.W $DFF000.L,$0548(A6)
		bus.WriteWord(0x00DF_F000, 0x4000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x4000, bus.ReadWord(0x0006_E9C6));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveLongAbsoluteLongToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2779, 0x0000, 0x0004, 0x0564); // MOVE.L $00000004.L,$0564(A3)
		bus.WriteLong(0x0000_0004, 0x00C0_0DA8);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x00C0_7264;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00C0_0DA8u, bus.ReadLong(0x00C0_77C8));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToAbsoluteLongWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x13C1, 0x0000, 0x0400); // MOVE.B D1,$000400.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_56AA;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0xAA, bus.ReadByte(0x0000_0400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToAddressIndirectWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1285); // MOVE.B D5,(A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_0400;
		cpu.State.D[5] = 0x1234_5680;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x80, bus.ReadByte(0x0000_0400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToAddressIndirectWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3281); // MOVE.W D1,(A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_0400;
		cpu.State.D[1] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x8001, bus.ReadWord(0x0000_0400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordDataToAddressRegisterSignExtendsAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3245); // MOVEA.W D5,A1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[5] = 0x1234_8001;
		cpu.State.A[1] = 0xCAFE_BABE;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_8001u, cpu.State.A[1]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToAddressDisplacementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1344, 0x0001); // MOVE.B D4,$0001(A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_0400;
		cpu.State.D[4] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x00, bus.ReadByte(0x0000_0401, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteAddressDisplacementToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x136D, 0x0010, 0x0020); // MOVE.B $10(A5),$20(A1)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_2400;
		cpu.State.A[1] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x80, bus.ReadByte(0x0000_3020, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveByteBriefIndexedToAddressDisplacementConsumesSourceExtensionFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1B71, 0x2810, 0x0020); // MOVE.B $10(A1,D2.L),$20(A5)
		var cycle = 0L;
		bus.WriteByte(0x0000_4012, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.A[5] = 0x0000_5000;
		cpu.State.D[2] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x80, bus.ReadByte(0x0000_5020, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.Equal(0x0000_5000u, cpu.State.A[5]);
		Assert.Equal(2u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveWordAbsoluteLongToPredecrementReadsSourceBeforeUpdatingDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3F39, 0x0000, 0x5000); // MOVE.W $5000.L,-(A7)
		var cycle = 0L;
		bus.WriteWord(0x0000_5000, 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x2FFEu, cpu.State.A[7]);
		Assert.Equal(0x8001, bus.ReadWord(0x0000_2FFE, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressDisplacementToPredecrementReadsSourceBeforeUpdatingSharedAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3F2F, 0x0010); // MOVE.W $10(A7),-(A7)
		var cycle = 0L;
		bus.WriteWord(0x0000_2410, 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x2400);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x0000_23FEu, cpu.State.A[7]);
		Assert.Equal(0x8001, bus.ReadWord(0x0000_23FE, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressDisplacementToAddressIndirectCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x20AE, 0x0010); // MOVE.L $10(A6),(A0)
		bus.WriteLong(0x0000_2410, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.A[0] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_3000));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressDisplacementToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x236D, 0x0010, 0x0020); // MOVE.L $10(A5),$20(A1)
		bus.WriteLong(0x0000_2410, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_2400;
		cpu.State.A[1] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_3020));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressDisplacementToBriefIndexedConsumesSourceExtensionFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x25AD, 0x0010, 0x1810); // MOVE.L $10(A5),$10(A2,D1.L)
		bus.WriteLong(0x0000_4010, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.A[2] = 0x0000_5000;
		cpu.State.D[1] = 0x0000_0002;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_5012));
		Assert.Equal(0x0000_4000u, cpu.State.A[5]);
		Assert.Equal(0x0000_5000u, cpu.State.A[2]);
		Assert.Equal(0x0000_0002u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongBriefIndexedToPredecrementEvaluatesAliasedSourceFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2F37, 0x1010); // MOVE.L $10(A7,D1.W),-(A7)
		bus.WriteLong(0x0000_5010, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x5000);
		cpu.State.D[1] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4FFCu, cpu.State.A[7]);
		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_4FFC));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongBriefIndexedToAddressDisplacementConsumesSourceExtensionFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2372, 0x3810, 0x0020); // MOVE.L $10(A2,D3.L),$20(A1)
		bus.WriteLong(0x0000_4012, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_4000;
		cpu.State.A[1] = 0x0000_5000;
		cpu.State.D[3] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_5020));
		Assert.Equal(0x0000_4000u, cpu.State.A[2]);
		Assert.Equal(0x0000_5000u, cpu.State.A[1]);
		Assert.Equal(2u, cpu.State.D[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongBriefIndexedToBriefIndexedConsumesSourceExtensionFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2BB1, 0x1810, 0x2820); // MOVE.L $10(A1,D1.L),$20(A5,D2.L)
		bus.WriteLong(0x0000_4012, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.A[5] = 0x0000_5000;
		cpu.State.D[1] = 2;
		cpu.State.D[2] = 3;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_5023));
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.Equal(0x0000_5000u, cpu.State.A[5]);
		Assert.Equal(2u, cpu.State.D[1]);
		Assert.Equal(3u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAbsoluteLongToPredecrementPushesValueAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x2F39, 0x0000, 0x6000); // MOVE.L ($00006000).L,-(A7)
		bus.WriteLong(0x0000_6000, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x5000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4FFCu, cpu.State.A[7]);
		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_4FFC));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveByteDataToBriefIndexedWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1B85, 0x6000); // MOVE.B D5,0(A5,D6.W)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_2000;
		cpu.State.D[5] = 0x1234_5630;
		cpu.State.D[6] = 0x0000_0010;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x30, bus.ReadByte(0x0000_2010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteBriefIndexedToPredecrementWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1131, 0x2000); // MOVE.B 0(A1,D2.W),-(A0)
		bus.WriteWord(0x0000_3010, 0x8F00);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x4000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.A[1] = 0x0000_3000;
		cpu.State.D[2] = 0x0000_0010;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x8F, bus.ReadByte(0x0000_3FFF, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_3FFFu, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToPostIncrementWritesMemoryIncrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x10C1); // MOVE.B D1,(A0)+
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.D[1] = 0x1234_567F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x7F, bus.ReadByte(0x0000_0400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_0401u, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToPredecrementWritesMemoryDecrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1100); // MOVE.B D0,-(A0)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.D[0] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x00, bus.ReadByte(0x0000_03FF, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_03FFu, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteDataToPredecrementUsesWordStepForStackPointer()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1F03); // MOVE.B D3,-(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[7] = 0x0000_0400;
		cpu.State.D[3] = 0x1234_5680;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x80, bus.ReadByte(0x0000_03FE, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_03FEu, cpu.State.A[7]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void MoveBytePostIncrementToPostIncrementCopiesMemoryAndIncrementsAddresses()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x12D8); // MOVE.B (A0)+,(A1)+
		var cycle = 0L;
		bus.WriteByte(0x0000_2000, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2000;
		cpu.State.A[1] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0L;
		Assert.Equal(0x80, bus.ReadByte(0x0000_2400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_2001u, cpu.State.A[0]);
		Assert.Equal(0x0000_2401u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveByteAddressIndirectToAddressIndirectCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x1091); // MOVE.B (A1),(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_2000, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.A[1] = 0x0000_2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0L;
		Assert.Equal(0x80, bus.ReadByte(0x0000_2400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_2400u, cpu.State.A[0]);
		Assert.Equal(0x0000_2000u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteAddressIndirectToAbsoluteLongWritesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x13D5, 0x00DF, 0xF180); // MOVE.B (A5),$DFF180.L
		bus.WriteWord(0x0000_0400, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x80, bus.ReadByte(0x00DF_F180, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MoveByteAbsoluteLongToAbsoluteLongCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x13F9, 0x00DF, 0xF007, 0x00DF, 0xF181); // MOVE.B $DFF007.L,$DFF181.L
		bus.WriteWord(0x00DF_F006, 0x0081);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		var cycle = 0L;
		Assert.Equal(0x81, bus.ReadByte(0x00DF_F181, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 10u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiLongImmediateToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C85, 0x5050, 0x4321); // CMPI.L #$50504321,D5
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[5] = 0x5050_4321;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiLongImmediateToPostIncrementReadsMemoryIncrementsAddressAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C9D, 0x0000, 0x0000); // CMPI.L #0,(A5)+
		bus.WriteLong(0x0000_2000, 0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2004u, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiLongImmediateToAddressDisplacementReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0CAE, 0x0000, 0x0000, 0x00A8); // CMPI.L #0,$00A8(A6)
		bus.WriteLong(0x0006_E526, 0x0000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiLongImmediateToAbsoluteLongSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0CB9, 0x2050, 0x5043, 0x00F0, 0x0092); // CMPI.L #$20505043,$F00092.L
		bus.WriteLong(0x00F0_0092, 0x2050_5044);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 10u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiByteImmediateToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C07, 0x0000); // CMPI.B #$00,D7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[7] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5600u, cpu.State.D[7]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiByteImmediateToAddressIndirectReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C10, 0x0002); // CMPI.B #2,(A0)
		var cycle = 0L;
		bus.WriteByte(0x00F9_0503, 2, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00F9_0503;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiByteImmediateToAddressDisplacementReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C2E, 0x0001, 0x008A); // CMPI.B #1,$008A(A6)
		var cycle = 0L;
		bus.WriteByte(0x0006_E508, 1, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiWordImmediateToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C41, 0x0032); // CMPI.W #$0032,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x0000_0033;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void CmpiLongImmediateToAddressIndirectReadsMemoryWithoutWritingIt()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C90, 0x0000, 0x0001); // CMPI.L #1,(A0)
		bus.WriteLong(0x0000_4000, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_4000));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiWordImmediateToAddressIndirectReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C51, 0x0032); // CMPI.W #$0032,(A1)
		bus.WriteWord(0x0006_E500, 0x0032);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0006_E500;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiWordImmediateToAddressDisplacementReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C6E, 0x0005, 0x0082); // CMPI.W #5,$0082(A6)
		bus.WriteWord(0x0006_E500, 5);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void CmpiWordImmediateToAbsoluteLongSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0C79, 0x1114, 0x0000, 0x0000); // CMPI.W #$1114,$0.L
		bus.WriteWord(0x0000_0000, 0x1114);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaLongImmediateToAddressRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xBDFC, 0x0000, 0x0400); // CMPA.L #$00000400,A6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaLongDataToAddressRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB7C3); // CMPA.L D3,A3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0006_C47E;
		cpu.State.D[3] = 0x0006_C47A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaLongAddressToAddressRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB7C8); // CMPA.L A0,A3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0008_0000;
		cpu.State.A[3] = 0x0007_FFF0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaLongAddressIndirectToAddressRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xBBD3); // CMPA.L (A3),A5
		bus.WriteLong(0x0040_0000, 0x00C0_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0040_0000;
		cpu.State.A[5] = 0x00C0_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0040_0000u, cpu.State.A[3]);
		Assert.Equal(0x00C0_0000u, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaWordImmediateToAddressRegisterSignExtendsSourceAndSetsLongFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB0FC, 0xFFFF); // CMPA.W #-$1,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0xFFFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_FFFFu, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaWordAddressRegisterSignExtendsSourceAndSetsLongFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB6CA); // CMPA.W A2,A3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x1234_FFFC;
		cpu.State.A[3] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FFFCu, cpu.State.A[2]);
		Assert.Equal(0u, cpu.State.A[3]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaWordDataRegisterSignExtendsSourceAndSetsLongFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB4C2); // CMPA.W D2,A2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_FFFC;
		cpu.State.A[2] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FFFCu, cpu.State.D[2]);
		Assert.Equal(0u, cpu.State.A[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpaLongAddressDisplacementToAddressRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB3E8, 0x0010); // CMPA.L $10(A0),A1
		bus.WriteLong(0x0000_2410, 0x00C0_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.A[1] = 0x00C0_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void CmpaLongPredecrementToAddressRegisterUpdatesSourceAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB3E0); // CMPA.L -(A0),A1
		bus.WriteLong(0x0000_5000, 0x00C0_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_5004;
		cpu.State.A[1] = 0x00C0_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_5000u, cpu.State.A[0]);
		Assert.Equal(0x00C0_0000u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void CmpLongAddressToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB08D); // CMP.L A5,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_2000;
		cpu.State.A[5] = 0x0000_2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpLongDataToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB087); // CMP.L D7,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_00A0;
		cpu.State.D[7] = 0x0000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpLongAddressIndirectToDataRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB895); // CMP.L (A5),D4
		bus.WriteLong(0x0000_2000, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[4] = 0xFFFF_FFFF;
		cpu.State.A[5] = 0x0000_2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpLongPostIncrementToDataRegisterReadsMemoryIncrementsAddressAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB099); // CMP.L (A1)+,D0
		bus.WriteLong(0x00FA_7378, 0xCED3_BA58);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x00FA_7378;
		cpu.State.D[0] = 0xCED3_BA58;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xCED3_BA58u, cpu.State.D[0]);
		Assert.Equal(0x00FA_737Cu, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpByteDataToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB400); // CMP.B D0,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_00F0;
		cpu.State.D[2] = 0x0000_00F0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpByteAddressIndirectToDataRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xBE10); // CMP.B (A0),D7
		var cycle = 0L;
		bus.WriteByte(0x0000_0400, 0x20, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_0400;
		cpu.State.D[7] = 0x0000_001F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void CmpBytePostIncrementToDataRegisterUsesA7ByteStepAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB01F); // CMP.B (A7)+,D0
		var cycle = 0L;
		bus.WriteByte(0x0000_4000, 0x20, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x4000);
		cpu.State.D[0] = 0x1234_5620;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4002u, cpu.State.A[7]);
		Assert.Equal(0x1234_5620u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpByteAddressDisplacementToDataRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB02E, 0x0079); // CMP.B $0079(A6),D0
		var cycle = 0L;
		bus.WriteByte(0x0006_E4F7, 3, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0003;
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpByteAbsoluteLongToDataRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB039, 0x0000, 0x0400); // CMP.B $00000400.L,D0
		var cycle = 0L;
		bus.WriteByte(0x0000_0400, 0x0A, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_560A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void CmpWordDataToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB240); // CMP.W D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_AAAA;
		cpu.State.D[1] = 0x0000_AAAA;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void CmpWordAddressToDataRegisterUsesLowWordAndPreservesOperandsAndExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB048); // CMP.W A0,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x1234_FFFF;
		cpu.State.D[0] = 0x5678_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_FFFFu, cpu.State.A[0]);
		Assert.Equal(0x5678_7FFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void CmpWordImmediateToDataRegisterSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB27C, 0x8001); // CMP.W #$8001,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8001u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void CmpByteImmediateToDataRegisterSetsCompareFlagsWithoutWriteback()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB03C, 0x0001); // CMP.B #1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5680;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5680u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void CmpLongImmediateToDataRegisterSetsCompareFlagsWithoutWriteback()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB2BC, 0xFFFF, 0xFFFF); // CMP.L #$FFFFFFFF,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x7FFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7FFF_FFFFu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void CmpWordAddressIndirectToDataRegisterReadsMemoryWithoutUpdatingAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB053); // CMP.W (A3),D0
		bus.WriteWord(0x0000_2400, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_2400;
		cpu.State.D[0] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2400u, cpu.State.A[3]);
		Assert.Equal(0x1234_8001u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void CmpWordPostIncrementToDataRegisterReadsMemoryAndUpdatesAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB45C); // CMP.W (A4)+,D2
		bus.WriteWord(0x0000_2400, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[4] = 0x0000_2400;
		cpu.State.D[2] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2402u, cpu.State.A[4]);
		Assert.Equal(0x1234_8001u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void CmpmBytePostIncrementComparesMemoryAndUpdatesBothAddresses()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB308); // CMPM.B (A0)+,(A1)+
		var cycle = 0L;
		bus.WriteByte(0x0000_2400, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		bus.WriteByte(0x0000_3000, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x4000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.A[1] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2401u, cpu.State.A[0]);
		Assert.Equal(0x0000_3001u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void CmpWordAddressDisplacementToDataRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB06E, 0x0242); // CMP.W $0242(A6),D0
		bus.WriteWord(0x0006_E6C0, 0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0;
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void SubiByteImmediateToDataRegisterSubtractsLowByteAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0407, 0x0001); // SUBI.B #1,D7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[7] = 0x1234_561A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5619u, cpu.State.D[7]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void SubiByteImmediateToAddressDisplacementSubtractsMemoryAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x042E, 0x0008, 0x0245); // SUBI.B #8,$0245(A6)
		var cycle = 0L;
		bus.WriteByte(0x0006_E6C3, 0x03, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0xFB, bus.ReadByte(0x0006_E6C3, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void SubiLongImmediateToDataRegisterSubtractsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0482, 0x0000, 0x0001); // SUBI.L #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFFu, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void SubLongDataToDataRegisterSubtractsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9083); // SUB.L D3,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0006_C47E;
		cpu.State.D[3] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0006_C07Eu, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddWordDataToDataRegisterAddsLowWordAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD642); // ADD.W D2,D3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x0000_00FF;
		cpu.State.D[3] = 0x1234_FF01;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0000u, cpu.State.D[3]);
		Assert.Equal(0x0000_00FFu, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongDataToDataRegisterAddsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD081); // ADD.L D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xFFFF_FBFF;
		cpu.State.D[1] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongDataToDataRegisterAlternateDestinationAddsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD480); // ADD.L D0,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0200;
		cpu.State.D[2] = 0xFFFF_FE00;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0000u, cpu.State.D[2]);
		Assert.Equal(0x0000_0200u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddxLongDataRegisterUsesExtendAndStickyZero()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD180); // ADDX.L D0,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void SubxLongDataRegisterUsesExtendAndStickyZero()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9181); // SUBX.L D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0;
		cpu.State.D[1] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_FFFFu, cpu.State.D[0]);
		Assert.Equal(0u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongPostIncrementToDataRegisterReadsMemoryIncrementsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD09D); // ADD.L (A5)+,D0
		bus.WriteLong(0x00F8_0000, 0x0000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x00F8_0000;
		cpu.State.D[0] = 0x7FFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[0]);
		Assert.Equal(0x00F8_0004u, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
		[Fact]
		public void AddLongAddressDisplacementToDataRegisterReadsMemoryAndSetsFlags()
		{
			var bus = new ZeroWaitCodeBus();
			WriteWords(bus, CodeBase, 0xD2AE, 0xFFFC); // ADD.L -4(A6),D1
			bus.WriteLong(0x0000_3FFC, 1);
			var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
			cpu.Reset(CodeBase, 0x3000);
			cpu.State.A[6] = 0x0000_4000;
			cpu.State.D[1] = 0x7FFF_FFFF;
			cpu.State.StatusRegister = M68kCpuState.Supervisor |
				M68kCpuState.Extend |
				M68kCpuState.Zero |
				M68kCpuState.Carry;

			cpu.ExecuteInstruction();

			Assert.Equal(0x8000_0000u, cpu.State.D[1]);
			Assert.Equal(0x0000_4000u, cpu.State.A[6]);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
			Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
			Assert.Equal(7, cpu.State.NativeCycles);
			Assert.Equal(4, cpu.State.Cycles);
		}

		[Fact]
		public void SubByteDataToDataRegisterSubtractsLowByteAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9200); // SUB.B D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0002;
		cpu.State.D[1] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_56FFu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongAddressToDataRegisterAddsAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD288); // ADD.L A0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 1;
		cpu.State.D[1] = 0x7FFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void SubiWordImmediateToDataRegisterPreservesUpperWordAndSetsBorrowFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0440, 0x0001); // SUBI.W #1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FFFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void AddxByteDataRegisterUsesExtendAndPreservesUpperBytes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD301); // ADDX.B D1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_567F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_56FFu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddxWordDataRegisterUsesExtendStickyZeroAndPreservesUpperWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD140); // ADDX.W D0,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FFFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void AddByteDataToDataRegisterAddsLowByteAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD201); // ADD.B D1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_567F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_56FEu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void SubLongAddressToDataRegisterSubtractsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9088); // SUB.L A0,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0006_E4B2;
		cpu.State.D[0] = 0x0007_E4B2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0001_0000u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void SubLongAddressDisplacementToDataRegisterReadsMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x90AE, 0x00B0); // SUB.L $00B0(A6),D0
		bus.WriteLong(0x0006_E52E, 0xC0C0_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.D[0] = 0xC0C0_C0C8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_C0C8u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AddiWordImmediateToDataRegisterAddsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0642, 0x0001); // ADDI.W #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0000u, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void AddiByteImmediateToDataRegisterAddsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0606, 0x0001); // ADDI.B #1,D6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[6] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5600u, cpu.State.D[6]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void AddiByteImmediateToAddressIndirectAddsMemoryAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0610, 0x0030); // ADDI.B #$30,(A0)
		var cycle = 0L;
		bus.WriteByte(0x0006_E4C2, 0x03, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0006_E4C2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x33, bus.ReadByte(0x0006_E4C2, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AddiByteImmediateToAddressDisplacementAddsMemoryAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x062E, 0x0001, 0x0101); // ADDI.B #1,$0101(A6)
		var cycle = 0L;
		bus.WriteByte(0x0006_E57F, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0006_E57F, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AddiLongImmediateToDataRegisterAddsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0681, 0x0000, 0x0001); // ADDI.L #1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0xFFFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AddiLongImmediateToAbsoluteLongAddsMemoryAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x06B9, 0x0000, 0x0001, 0x0000, 0x0404); // ADDI.L #1,$404.L
		bus.WriteLong(0x0000_0404, 0xFFFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0u, bus.ReadLong(0x0000_0404));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 10u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}
	[Fact]
	public void AddWordDataToAddressDisplacementAddsToMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xDF6E, 0x0212); // ADD.W D7,$0212(A6)
		bus.WriteWord(0x0006_E690, 0x00C0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.D[7] = 0x0000_C0C0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xC180, bus.ReadWord(0x0006_E690));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongDataToAddressDisplacementAddsToMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD7A9, 0x0004); // ADD.L D3,$0004(A1)
		bus.WriteLong(0x0000_2004, 0x0000_0030);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_2000;
		cpu.State.A[3] = 0xDEAD_BEEF;
		cpu.State.D[3] = 0x0007_7328;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0007_7358u, bus.ReadLong(0x0000_2004));
		Assert.Equal(0xDEAD_BEEFu, cpu.State.A[3]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(13, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}
	[Fact]
	public void AddaLongImmediateToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xDBFC, 0x0000, 0x0008); // ADDA.L #8,A5
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x00FA_73FA;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00FA_7402u, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddiLongImmediateToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x06A9, 0x0000, 0x0001, 0x0010); // ADDI.L #1,$10(A1)
		bus.WriteLong(0x0000_4010, 0x7FFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_4010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongImmediateToDataRegisterUpdatesResultAndArithmeticFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD4BC, 0x0000, 0x0001); // ADD.L #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x7FFF_FFFF;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AddaWordImmediateToAddressRegisterSignExtendsAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD4FC, 0xFFF8); // ADDA.W #-8,A2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_1000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0FF8u, cpu.State.A[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void AddaWordDataToAddressRegisterSignExtendsAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD0C0); // ADDA.W D0,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_1000;
		cpu.State.D[0] = 0x1234_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0FFFu, cpu.State.A[0]);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddaLongDataToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD1C2); // ADDA.L D2,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00F9_4905;
		cpu.State.D[2] = 0x0000_0004;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F9_4909u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddaLongAddressToAddressRegisterWrapsAndLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD5C9); // ADDA.L A1,A2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0xFFFF_FFFF;
		cpu.State.A[2] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(1u, cpu.State.A[2]);
		Assert.Equal(0xFFFF_FFFFu, cpu.State.A[1]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddaLongAddressDisplacementToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD3EE, 0x0382); // ADDA.L $0382(A6),A1
		bus.WriteLong(0x0006_E800, 0x0000_1001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x00C7_FFFF;
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00C8_1000u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void SubaLongImmediateToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9DFC, 0x0000, 0x0400); // SUBA.L #$00000400,A6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0008_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0008_0000u, cpu.State.A[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void DivuWordImmediateToDataRegisterDividesUnsignedLongByWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x80FC, 0x0400); // DIVU.W #$0400,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0001_2345;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0345_0048u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(46, cpu.State.NativeCycles);
		Assert.Equal(23, cpu.State.Cycles);
	}
	[Fact]
	public void DivuWordImmediateToDataRegisterSetsOverflowWhenQuotientDoesNotFit()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x80FC, 0x0400); // DIVU.W #$0400,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xA0A0_A0A8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative;
		cpu.ExecuteInstruction();
		Assert.Equal(0xA0A0_A0A8u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(46, cpu.State.NativeCycles);
		Assert.Equal(23, cpu.State.Cycles);
	}
	[Fact]
	public void DivsWordImmediateToDataRegisterDividesSignedLongByWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x81FC, 0x000A); // DIVS.W #10,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0003;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0003_0000u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(58, cpu.State.NativeCycles);
		Assert.Equal(29, cpu.State.Cycles);
	}
	[Fact]
	public void DivuWordDataRegisterToDataRegisterDividesUnsignedLongByWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x80C1); // DIVU.W D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0001_2345;
		cpu.State.D[1] = 0x0000_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0345_0048u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(46, cpu.State.NativeCycles);
		Assert.Equal(23, cpu.State.Cycles);
	}
	[Fact]
	public void MuluLongDataRegisterStoresLowLongAndOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4C01, 0x0000); // MULU.L D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xFFFF_FFFF;
		cpu.State.D[1] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFEu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(44, cpu.State.NativeCycles);
		Assert.Equal(22, cpu.State.Cycles);
	}
	[Fact]
	public void MuluLongDataRegisterPairStoresFullProduct()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4C01, 0x0401); // MULU.L D1,D1:D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xFFFF_FFFF;
		cpu.State.D[1] = 2;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFEu, cpu.State.D[0]);
		Assert.Equal(0x0000_0001u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void DivuLongDataRegisterStoresQuotientAndRemainder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4C41, 0x0001); // DIVU.L D1,D1:D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 15;
		cpu.State.D[1] = 12;
		cpu.ExecuteInstruction();
		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(3u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(76, cpu.State.NativeCycles);
		Assert.Equal(38, cpu.State.Cycles);
	}
	[Fact]
	public void DivsLongDataRegisterStoresSignedQuotientAndRemainder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4C41, 0x0801); // DIVS.L D1,D1:D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = unchecked((uint)-15);
		cpu.State.D[1] = 4;
		cpu.ExecuteInstruction();
		Assert.Equal(unchecked((uint)-3), cpu.State.D[0]);
		Assert.Equal(unchecked((uint)-3), cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(82, cpu.State.NativeCycles);
		Assert.Equal(41, cpu.State.Cycles);
	}
	[Fact]
	public void AndLongImmediateToDataRegisterUpdatesLongAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0282, 0x0000, 0x000F); // ANDI.L #$0000000F,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0xFFFF_FF1A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_000Au, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void EoriWordImmediateToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0A42, 0x00F0); // EORI.W #$00F0,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_0F0F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0FFFu, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void EoriLongImmediateToDataRegisterUpdatesLongAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0A80, 0x1001, 0x0000); // EORI.L #$10010000,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0088;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1001_0088u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void MuluWordImmediateToDataRegisterReplacesLongRegisterAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC6FC, 0x0006); // MULU.W #6,D3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[3] = 0xFFFF_001B;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_00A2u, cpu.State.D[3]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void MulsWordAddressDisplacementToDataRegisterMultipliesSignedWords()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC5E9, 0x0004); // MULS.W 4(A1),D2
		bus.WriteWord(0x3004, 0xFFFE);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x3000;
		cpu.State.D[2] = 0xFFFF_0003;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFAu, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(46, cpu.State.NativeCycles);
		Assert.Equal(23, cpu.State.Cycles);
	}
	[Fact]
	public void AndiWordImmediateToDataRegisterUsesEaRegisterBits()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0242, 0x00FE); // ANDI.W #$00FE,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0xAAAA_5555;
		cpu.State.D[2] = 0x1234_55FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0xAAAA_5555u, cpu.State.D[1]);
		Assert.Equal(0x1234_00FEu, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void AndiWordImmediateToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x026B, 0x80F0, 0x0010); // ANDI.W #$80F0,$10(A3)
		bus.WriteWord(0x0000_4010, 0xCCFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x80F0, bus.ReadWord(0x0000_4010));
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AndByteDataRegisterToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC401); // AND.B D1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x0000_00F0;
		cpu.State.D[2] = 0x1234_56CC;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_56C0u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AbcdByteDataRegisterUsesExtendStickyZeroAndDecimalCarry()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC501); // ABCD D1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x49;
		cpu.State.D[2] = 0x1234_5650;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5600u, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void SbcdByteDataRegisterUsesExtendStickyZeroAndDecimalBorrow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8501); // SBCD D1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x01;
		cpu.State.D[2] = 0x1234_5620;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5618u, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AbcdBytePredecrementMemoryUsesDecimalCarryAndUpdatesAddresses()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC509); // ABCD -(A1),-(A2)
		bus.WriteWord(0x2000, 0x4900);
		bus.WriteWord(0x2100, 0x5000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x2001;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0x2100u, cpu.State.A[2]);
		Assert.Equal(0x0000, bus.ReadWord(0x2100));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(18, cpu.State.NativeCycles);
		Assert.Equal(9, cpu.State.Cycles);
	}
	[Fact]
	public void SbcdBytePredecrementMemorySubtractsPackedDecimalWithExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8509); // SBCD -(A1),-(A2)
		bus.WriteWord(0x2000, 0x0100);
		bus.WriteWord(0x2100, 0x2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x2001;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0x2100u, cpu.State.A[2]);
		Assert.Equal(0x1800, bus.ReadWord(0x2100));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(18, cpu.State.NativeCycles);
		Assert.Equal(9, cpu.State.Cycles);
	}
	[Fact]
	public void NbcdByteDataRegisterNegatesPackedDecimalWithExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4802); // NBCD D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5698u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void NbcdByteAddressDisplacementNegatesPackedDecimalWithTiming()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4828, 0x0002); // NBCD (2,A0)
		bus.WriteWord(0x2002, 0x2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000, bus.ReadWord(0x2002));
		Assert.Equal(0x2000u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void SwapDataRegisterSwapsWordsAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4842); // SWAP D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x8000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0001_8000u, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void TstWordDataRegisterSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A40); // TST.W D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0003;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void TstWordAbsoluteLongReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A79, 0x0000, 0x5000); // TST.W $5000.L
		var cycle = 0L;
		bus.WriteWord(0x0000_5000, 0x8000, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void TstByteAbsoluteLongReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A39, 0x0000, 0x5000); // TST.B $5000.L
		var cycle = 0L;
		bus.WriteByte(0x0000_5000, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void TstByteDataRegisterSetsConditionCodesWithoutChangingDataOrExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A00); // TST.B D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5680;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5680u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void CmpLongAddressDisplacementToDataRegisterReadsMemoryAndSetsCompareFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB4AB, 0xFFFC); // CMP.L -4(A3),D2
		bus.WriteLong(0x0000_3FFC, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.D[2] = 0x7FFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7FFF_FFFFu, cpu.State.D[2]);
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubaLongDataToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x93C1); // SUBA.L D1,A1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0020_0000;
		cpu.State.D[1] = 7;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x001F_FFF9u, cpu.State.A[1]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void AndLongImmediateEffectiveAddressToDataRegisterUpdatesLongAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC2BC, 0x0000, 0x0007); // AND.L #$00000007,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0xFFFF_FFFA;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(2u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void OrLongImmediateEffectiveAddressToDataRegisterUpdatesLongAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x82BC, 0x8000, 0x0001); // OR.L #$80000001,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0003u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void LeaBriefIndexedCalculatesAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x47F1, 0x0800); // LEA (0,A1,D0.W),A3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xFFFF_FFFC;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_3FFCu, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressIndirectToPostIncrementCopiesMemoryAndUpdatesDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x26D1); // MOVE.L (A1),(A3)+
		bus.WriteLong(0x0000_4000, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.A[3] = 0x0000_5000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_5000));
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.Equal(0x0000_5004u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void SubLongDataToAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x95AA, 0x0004); // SUB.L D2,4(A2)
		bus.WriteLong(0x0000_4004, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_4000;
		cpu.State.D[2] = 1;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7FFF_FFFFu, bus.ReadLong(0x0000_4004));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(13, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}

	[Fact]
	public void SubaWordImmediateToAddressRegisterSignExtendsAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9CFC, 0xFFFC); // SUBA.W #-4,A6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4004u, cpu.State.A[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void SubaWordDataRegisterSignExtendsAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x94C6); // SUBA.W D6,A2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[6] = 0x1234_FFFC;
		cpu.State.A[2] = 0x0000_1000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_1004u, cpu.State.A[2]);
		Assert.Equal(0x1234_FFFCu, cpu.State.D[6]);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordPcDisplacementToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3D7A, 0x0010, 0x0004); // MOVE.W 16(PC),4(A6)
		bus.WriteWord(CodeBase + 0x12u, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8000, bus.ReadWord(0x0000_4004));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressRegisterToDataRegisterCopiesLowWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3E0A); // MOVE.W A2,D7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x1234_8001;
		cpu.State.D[7] = 0xCAFE_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xCAFE_8001u, cpu.State.D[7]);
		Assert.Equal(0x1234_8001u, cpu.State.A[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongPcDisplacementToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x277A, 0x0010, 0x0004); // MOVE.L 16(PC),4(A3)
		bus.WriteLong(CodeBase + 0x12u, 0x8000_0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0001u, bus.ReadLong(0x0000_4004));
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressDisplacementToAddressDisplacementCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3B6E, 0x0010, 0x0004); // MOVE.W 16(A6),4(A5)
		bus.WriteWord(0x0000_4010, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_5000;
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8000, bus.ReadWord(0x0000_5004));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressDisplacementToAddressIndirectCopiesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x30AD, 0x0010); // MOVE.W $10(A5),(A0)
		bus.WriteWord(0x0000_4010, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_5000;
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8001, bus.ReadWord(0x0000_5000));
		Assert.Equal(0x0000_5000u, cpu.State.A[0]);
		Assert.Equal(0x0000_4000u, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordAddressToAddressDisplacementWritesLowWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3D4A, 0x0004); // MOVE.W A2,4(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x1234_8000;
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8000, bus.ReadWord(0x0000_4004));
		Assert.Equal(0x1234_8000u, cpu.State.A[2]);
		Assert.Equal(0x0000_4000u, cpu.State.A[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void MovemLongRegistersToAddressIndirectStoresNormalMaskOrder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48D1, 0x0201); // MOVEM.L D0/A1,(A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0000_4000));
		Assert.Equal(0x0000_4000u, bus.ReadLong(0x0000_4004));
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry), cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(14, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}

	[Fact]
	public void MovemLongRegistersToAddressDisplacementStoresNormalMaskOrder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48EB, 0x0201, 0x0004); // MOVEM.L D0/A1,4(A3)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[1] = 0x89AB_CDEF;
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0000_4004));
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_4008));
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry), cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(16, cpu.State.NativeCycles);
		Assert.Equal(8, cpu.State.Cycles);
	}

	[Fact]
	public void MovemLongRegistersToBriefIndexedStoresNormalMaskOrder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48F6, 0x0201, 0x1804); // MOVEM.L D0/A1,4(A6,D1.L)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.D[1] = 0x0000_0010;
		cpu.State.A[1] = 0x89AB_CDEF;
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0000_4014));
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x0000_4018));
		Assert.Equal(0x0000_4000u, cpu.State.A[6]);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry), cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(18, cpu.State.NativeCycles);
		Assert.Equal(9, cpu.State.Cycles);
	}

	[Fact]
	public void CmpiLongImmediateToAbsoluteWordSignExtendsAddressAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0CB8, 0x8000, 0x0000, 0xFFE6); // CMPI.L #$80000000,$FFE6.W
		bus.WriteLong(0xFFFF_FFE6, 0x7FFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveWordPostIncrementToDataUpdatesLowWordAndSourceAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3219); // MOVE.W (A1)+,D1
		bus.WriteWord(0x0000_4000, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.D[1] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.Equal(0x0000_4002u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AndWordImmediateToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC47C, 0x8001); // AND.W #$8001,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8001u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
			Assert.Equal(2, cpu.State.Cycles);
		}

	[Fact]
	public void OrWordImmediateToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x807C, 0x8001); // OR.W #$8001,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_0002;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8003u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void OrByteImmediateToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8E3C, 0x0081); // OR.B #$81,D7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[7] = 0x1234_5602;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5683u, cpu.State.D[7]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void OriWordImmediateToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0041, 0x8001); // ORI.W #$8001,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0002;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8003u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void OriWordImmediateToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0068, 0x8001, 0x0010); // ORI.W #$8001,$10(A0)
		bus.WriteWord(0x0000_3010, 0x0002);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8003, bus.ReadWord(0x0000_3010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void OriLongImmediateToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x00A9, 0x8000, 0x0001, 0x0010); // ORI.L #$80000001,$10(A1)
		bus.WriteLong(0x0000_3010, 0x0000_0002);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0003u, bus.ReadLong(0x0000_3010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void OriWordImmediateToAddressIndirectUpdatesMemoryWithoutChangingAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0057, 0x8001); // ORI.W #$8001,(A7)
		bus.WriteWord(0x0000_3000, 0x0002);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_3000u, cpu.State.A[7]);
		Assert.Equal((ushort)0x8003, bus.ReadWord(0x0000_3000));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AndWordDataToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC042); // AND.W D2,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_F0F0;
		cpu.State.D[2] = 0xABCD_800F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void AndByteDataRegisterToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC329, 0x0010); // AND.B D1,$10(A1)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0xCC, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x0000_00F0;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var readCycle = 0L;
		Assert.Equal(0xC0, bus.ReadByte(0x0000_4010, ref readCycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void AndByteAddressDisplacementToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC02E, 0x0010); // AND.B $10(A6),D0
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0xF0, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_56CC;
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_56C0u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AndWordAddressDisplacementToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC268, 0x0010); // AND.W $10(A0),D1
		var cycle = 0L;
		bus.WriteWord(0x0000_4010, 0xF000, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_CCCC;
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_C000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AndWordDataToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC36E, 0x0010); // AND.W D1,$10(A6)
		bus.WriteWord(0x0000_4010, 0xF0F0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0FF0;
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00F0, bus.ReadWord(0x0000_4010));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void AndLongDataToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC3A9, 0x0010); // AND.L D1,$10(A1)
		bus.WriteLong(0x0000_4010, 0xFF00_F0F0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0xF0FF_00FF;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xF000_00F0u, bus.ReadLong(0x0000_4010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(13, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}
	[Fact]
	public void OrWordAddressDisplacementToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x806E, 0x0010); // OR.W $10(A6),D0
		bus.WriteWord(0x0000_4010, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_0002;
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8003u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void OrLongDataToAddressIndirectUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8190); // OR.L D0,(A0)
		bus.WriteLong(0x0000_4000, 0x7000_00F0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0F00;
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xF000_0FF0u, bus.ReadLong(0x0000_4000));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void OrLongDataToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x81AA, 0x0010); // OR.L D0,$10(A2)
		bus.WriteLong(0x0000_4010, 0x7000_00F0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0F00;
		cpu.State.A[2] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xF000_0FF0u, bus.ReadLong(0x0000_4010));
		Assert.Equal(0x0000_4000u, cpu.State.A[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(13, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}
	[Fact]
	public void EorLongDataToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB1A9, 0x0010); // EOR.L D0,$10(A1)
		bus.WriteLong(0x0000_4010, 0x7000_00F0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0F00;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xF000_0FF0u, bus.ReadLong(0x0000_4010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(13, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}

	[Fact]
	public void EorByteDataToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB52A, 0x0010); // EOR.B D2,$10(A2)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x7F, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_4000;
		cpu.State.D[2] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal((byte)0x80, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void EorWordDataToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xB541); // EOR.W D2,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_7FFF;
		cpu.State.D[2] = 0x5678_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.Equal(0x5678_FFFFu, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void OrByteAddressDisplacementToDataRegisterPreservesUpperBytesAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x822E, 0x0010); // OR.B $10(A6),D1
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_5601;
		cpu.State.A[6] = 0x0000_4000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5681u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void OrByteDataRegisterToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8128, 0x0010); // OR.B D0,$10(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var readCycle = 0L;
		Assert.Equal(0x81, bus.ReadByte(0x0000_4010, ref readCycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void OrByteDataRegisterToAddressIndirectUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8110); // OR.B D0,(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_4000, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0080;
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var readCycle = 0L;
		Assert.Equal(0x80, bus.ReadByte(0x0000_4000, ref readCycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void OrByteDataToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8001); // OR.B D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5680;
		cpu.State.D[1] = 0x0000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5681u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void OrWordDataToDataRegisterUpdatesLowWordAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8046); // OR.W D6,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_000F;
		cpu.State.D[6] = 0x0000_80F0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_80FFu, cpu.State.D[0]);
		Assert.Equal(0x0000_80F0u, cpu.State.D[6]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void OrWordDataToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x856E, 0x0010); // OR.W D2,$10(A6)
		bus.WriteWord(0x0000_4010, 0x0002);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.D[2] = 0x1234_8001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8003, bus.ReadWord(0x0000_4010));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void OrWordDataToAddressIndirectUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x8555); // OR.W D2,(A5)
		bus.WriteWord(0x0000_4000, 0x0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.D[2] = 0x1234_8000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8001, bus.ReadWord(0x0000_4000));
		Assert.Equal(0x0000_4000u, cpu.State.A[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

		[Fact]
	public void ExgDataAddressSwapsRegistersAndPreservesFlags()
		{
			var bus = new ZeroWaitCodeBus();
			WriteWords(bus, CodeBase, 0xC58B); // EXG D2,A3
			var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
			cpu.Reset(CodeBase, 0x3000);
			cpu.State.D[2] = 0x1234_5678;
			cpu.State.A[3] = 0x89AB_CDEF;
			cpu.State.StatusRegister = M68kCpuState.Supervisor |
				M68kCpuState.Extend |
				M68kCpuState.Negative |
				M68kCpuState.Zero |
				M68kCpuState.Overflow |
				M68kCpuState.Carry;

			cpu.ExecuteInstruction();

			Assert.Equal(0x89AB_CDEFu, cpu.State.D[2]);
			Assert.Equal(0x1234_5678u, cpu.State.A[3]);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
			Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
			Assert.Equal(4, cpu.State.NativeCycles);
			Assert.Equal(2, cpu.State.Cycles);
		}

	[Fact]
	public void ExgDataDataSwapsRegistersAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC141); // EXG D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.D[1] = 0x89AB_CDEF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x89AB_CDEFu, cpu.State.D[0]);
		Assert.Equal(0x1234_5678u, cpu.State.D[1]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void ExgAddressAddressSwapsRegistersAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xCB4E); // EXG A5,A6
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x1234_5678;
		cpu.State.A[6] = 0x89AB_CDEF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x89AB_CDEFu, cpu.State.A[5]);
		Assert.Equal(0x1234_5678u, cpu.State.A[6]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

		[Fact]
		public void SubaLongAddressToAddressRegisterLeavesFlagsUnchanged()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x93CA); // SUBA.L A2,A1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x1234_5678;
		cpu.State.A[2] = 0x0000_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_0000u, cpu.State.A[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void SubaLongAddressDisplacementToSameAddressRegisterUsesOriginalBaseAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x91E8, 0x0010); // SUBA.L $10(A0),A0
		bus.WriteLong(0x0000_2410, 0x0000_0100);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2300u, cpu.State.A[0]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void SubLongImmediateToDataRegisterSubtractsAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x92BC, 0x0000, 0x0001); // SUB.L #1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7FFF_FFFFu, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void TstLongDataRegisterSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A81); // TST.L D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x8000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void TstLongAddressIndirectReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A90); // TST.L (A0)
		bus.WriteLong(0x0000_2400, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void TstLongAddressDisplacementReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4AAA, 0x0010); // TST.L $10(A2)
		bus.WriteLong(0x0000_2410, 0);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void TstLongBriefIndexedReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4AB3, 0x1810); // TST.L $10(A3,D1.L)
		bus.WriteLong(0x0000_4012, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.D[1] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.Equal(2u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void TstByteAddressDisplacementReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A28, 0x0010); // TST.B $10(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void TstByteBriefIndexedReadsComputedAddressAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A30, 0x1010); // TST.B $10(A0,D1.W)
		var cycle = 0L;
		bus.WriteByte(0x0000_4012, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.D[1] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void TstByteAddressIndirectReadsMemoryWithoutChangingAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A10); // TST.B (A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_2400, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2400u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void TstBytePostIncrementReadsMemoryAndAdvancesAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A18); // TST.B (A0)+
		var cycle = 0L;
		bus.WriteByte(0x0000_2400, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2401u, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void TstWordAddressDisplacementReadsMemoryAndSetsConditionCodes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4A6A, 0x0010); // TST.W $10(A2)
		bus.WriteWord(0x0000_2410, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubqLongDataRegisterSubtractsImmediateAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5381); // SUBQ.L #1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 1;

		cpu.ExecuteInstruction();

		Assert.Equal(0u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void SubqByteDataRegisterPreservesUpperBytesAndSetsBorrowFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5302); // SUBQ.B #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_5600;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_56FFu, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void SubqWordDataRegisterPreservesUpperWordAndSetsBorrowFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5342); // SUBQ.W #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_0000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FFFFu, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void SubWordAddressIndirectToDataRegisterPreservesUpperWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9052); // SUB.W (A2),D0
		bus.WriteWord(0x0000_2400, 0x0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.D[0] = 0x1234_8000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_7FFFu, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void SubWordAddressDisplacementToDataRegisterUsesSignedDisplacement()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x906A, 0xFFFC); // SUB.W -4(A2),D0
		bus.WriteWord(0x0000_23FC, 0x0002);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.D[0] = 0x1234_0001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_FFFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void SubWordDataToDataRegisterPreservesUpperWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9240); // SUB.W D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xAAAA_0002;
		cpu.State.D[1] = 0x1234_0001;

		cpu.ExecuteInstruction();

		Assert.Equal(0xAAAA_0002u, cpu.State.D[0]);
		Assert.Equal(0x1234_FFFFu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void SubWordDataToPostIncrementUpdatesMemoryThenAdvancesAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9158); // SUB.W D0,(A0)+
		bus.WriteWord(0x0000_4000, 0x8000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 1;
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x7FFF, bus.ReadWord(0x0000_4000));
		Assert.Equal(0x0000_4002u, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void SubByteAddressDisplacementToDataRegisterPreservesUpperBytesAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x982B, 0xFFFC); // SUB.B -4(A3),D4
		var cycle = 0L;
		bus.WriteByte(0x0000_23FC, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_2400;
		cpu.State.D[4] = 0x1234_5601;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_56FFu, cpu.State.D[4]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubByteDataToAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x9129, 0x0010); // SUB.B D0,$10(A1)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x80, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 1;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x7F, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void AddWordAddressIndirectToDataRegisterPreservesUpperWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD252); // ADD.W (A2),D1
		bus.WriteWord(0x0000_2400, 0x0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.D[1] = 0x1234_7FFF;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AddLongDataToAddressIndirectUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD190); // ADD.L D0,(A0)
		bus.WriteLong(0x0000_2400, 0x7FFF_FFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 1;
		cpu.State.A[0] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0000u, bus.ReadLong(0x0000_2400));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void AddqLongAddressRegisterAddsImmediateAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5888); // ADDQ.L #4,A0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x00FF_FFFEu;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0100_0002u, cpu.State.A[0]);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AddqWordAddressRegisterAddsToFullAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x584F); // ADDQ.W #4,A7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x0001_FFFE);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0002_0002u, cpu.State.A[7]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void AddqLongAddressIndirectUpdatesMemoryAndFlagsWithoutChangingAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5890); // ADDQ.L #4,(A0)
		bus.WriteLong(0x0000_2400, 0x7FFF_FFFE);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0002u, bus.ReadLong(0x0000_2400));
		Assert.Equal(0x0000_2400u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubqLongAddressIndirectUpdatesMemoryAndFlagsWithoutChangingAddress()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5390); // SUBQ.L #1,(A0)
		bus.WriteLong(0x0000_2400, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7FFF_FFFFu, bus.ReadLong(0x0000_2400));
		Assert.Equal(0x0000_2400u, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubqLongAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x53AA, 0x0010); // SUBQ.L #1,$10(A2)
		bus.WriteLong(0x0000_4010, 0x8000_0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7FFF_FFFFu, bus.ReadLong(0x0000_4010));
		Assert.Equal(0x0000_4000u, cpu.State.A[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void AddqWordDataRegisterAddsImmediateUpdatesFlagsAndPreservesUpperWord()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5240); // ADDQ.W #1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void AddWordPredecrementToDataRegisterUpdatesAddressResultAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD260); // ADD.W -(A0),D1
		bus.WriteWord(0x0000_23FE, 0x0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.D[1] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_23FEu, cpu.State.A[0]);
		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void AddWordPostIncrementToDataRegisterUpdatesAddressResultAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD258); // ADD.W (A0)+,D1
		bus.WriteWord(0x0000_2400, 0x0001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.D[1] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2402u, cpu.State.A[0]);
		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddWordAddressDisplacementToDataRegisterAddsLowWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD06D, 0x0010); // ADD.W $10(A5),D0
		bus.WriteWord(0x0000_3010, 0x0002);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_3000;
		cpu.State.D[0] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8001u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddWordImmediateToDataRegisterPreservesUpperWordAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD27C, 0x0001); // ADD.W #1,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_7FFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_8000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void AddByteImmediateToDataRegisterPreservesUpperBytesAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD03C, 0x0001); // ADD.B #1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_567F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5680u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void AddaWordAddressDisplacementSignExtendsSourceAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD4EF, 0xFFFE); // ADDA.W -2(A7),A2
		bus.WriteWord(0x0000_2FFE, 0xFFFC);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x0001_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_FFFCu, cpu.State.A[2]);
		Assert.Equal(0x0000_3000u, cpu.State.A[7]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddaWordBriefIndexedSignExtendsSourceAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xD0F0, 0x1800); // ADDA.W 0(A0,D1.L),A0
		bus.WriteWord(0x0000_4010, 0xFFFC);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.D[1] = 0x0000_0010;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_3FFCu, cpu.State.A[0]);
		Assert.Equal(0x0000_0010u, cpu.State.D[1]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void AddqByteAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x522E, 0x0010); // ADDQ.B #1,$10(A6)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0000_2410, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddqByteDataRegisterPreservesUpperBitsAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5202); // ADDQ.B #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_567F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5680u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}

	[Fact]
	public void AddqByteAddressIndirectTreatsEncodedZeroAsEightAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x5010); // ADDQ.B #8,(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_2400, 0xF8, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0000_2400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddqWordAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x526E, 0x0010); // ADDQ.W #1,$10(A6)
		bus.WriteWord(0x0000_2410, 0x7FFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x8000, bus.ReadWord(0x0000_2410));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void AddqLongAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x50AD, 0x0010); // ADDQ.L #8,$10(A5)
		var cycle = 0L;
		bus.WriteLong(0x0000_4010, 0x7FFF_FFFC, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x8000_0004u, bus.ReadLong(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void SubqByteAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x532E, 0x0010); // SUBQ.B #1,$10(A6)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0xFF, bus.ReadByte(0x0000_2410, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubqWordAddressDisplacementUpdatesMemoryAndSetsFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x536E, 0x0010); // SUBQ.W #1,$10(A6)
		bus.WriteWord(0x0000_2410, 0x0000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0xFFFF, bus.ReadWord(0x0000_2410));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void SubqLongAddressRegisterSubtractsImmediateAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x598F); // SUBQ.L #4,A7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry, cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(2, cpu.State.NativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
	}
	[Fact]
	public void AsrLongImmediateDataRegisterShiftsWithSignAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE081); // ASR.L #8,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x8000_0080;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0xFF80_0000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AsrLongRegisterDataRegisterUsesLowSixCountBitsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE0A1); // ASR.L D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0048; // low six bits select a count of eight
		cpu.State.D[1] = 0x8000_0080;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFF80_0000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void LsrLongRegisterDataRegisterUsesLowSixCountBitsAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE2A8); // LSR.L D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0080;
		cpu.State.D[1] = 0x0000_0048; // low six bits select a count of eight
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0080_0000u, cpu.State.D[0]);
		Assert.Equal(0x0000_0048u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void LsrWordAddressDisplacementUsesMemoryFormOpcode()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE2E8, 0x0010); // LSR.W $10(A0)
		bus.WriteWord(0x0000_4010, 0x8001);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0x4000, bus.ReadWord(0x0000_4010));
		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void AsrWordImmediateDataRegisterShiftsLowWordWithSign()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE041); // ASR.W #8,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0004u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void LsrLongImmediateDataRegisterShiftsLogicalAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE888); // LSR.L #4,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0006_C47A;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_6C47u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AslWordImmediateDataRegisterShiftsAndSetsOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE342); // ASL.W #1,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x1234_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_8000u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AslLongImmediateDataRegisterShiftsAndSetsOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE184); // ASL.L #8,D4
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[4] = 0x0080_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[4]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void AslLongRegisterCountDataRegisterUsesLowSixCountBitsAndSetsOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE1A1); // ASL.L D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0041; // Register counts use the low six bits: 65 becomes 1.
		cpu.State.D[1] = 0x4000_0000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0041u, cpu.State.D[0]);
		Assert.Equal(0x8000_0000u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void LslLongRegisterCountDataRegisterUsesLowSixCountBitsAndSetsCarry()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE1A9); // LSL.L D0,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0041; // Register counts use the low six bits: 65 becomes 1.
		cpu.State.D[1] = 0x8000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0041u, cpu.State.D[0]);
		Assert.Equal(0x0000_0002u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void LsrWordRegisterCountPreservesUpperWordAndUsesLowSixCountBits()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE268); // LSR.W D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_0001;
		cpu.State.D[1] = 0x0000_0041; // Register counts use the low six bits: 65 becomes 1.
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_0000u, cpu.State.D[0]);
		Assert.Equal(0x0000_0041u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void LslWordRegisterCountPreservesUpperWordAndUsesLowSixCountBits()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE368); // LSL.W D1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_8001;
		cpu.State.D[1] = 0x0000_0041; // Register counts use the low six bits: 65 becomes 1.
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_0002u, cpu.State.D[0]);
		Assert.Equal(0x0000_0041u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void AslByteRegisterCountPreservesUpperBytesAndUsesLowSixCountBits()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE520); // ASL.B D2,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5640;
		cpu.State.D[2] = 0x0000_0041;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Carry |
			M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5680u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void RorByteImmediateDataRegisterRotatesLowByteAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE218); // ROR.B #1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5680u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void RorWordImmediateDataRegisterRotatesLowWordAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE859); // ROR.W #4,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_ABCD;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_DABCu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void RorLongImmediateEightRotatesAllBitsAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE098); // ROR.L #8,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8123_4580;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8081_2345u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void RolWordImmediateDataRegisterRotatesLowWordAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE959); // ROL.W #4,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_ABCD;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_BCDAu, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void OriByteImmediateToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0000, 0x0080); // ORI.B #$80,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5681u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void BtstByteImmediateAbsoluteLongOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0839, 0x0006, 0x00BF, 0xE001); // BTST #6,$BFE001.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_E001, 0x40, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void BtstByteImmediateAddressDisplacementOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0829, 0x0006, 0x0010); // BTST #6,$10(A1)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0x40, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void BtstByteDynamicAddressDisplacementUsesModuloEightAndDoesNotWriteMemory()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x012D, 0x0010); // BTST D0,$10(A5)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 9; // Memory bit numbers are reduced modulo 8.
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x02, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void BtstByteDynamicBriefIndexedUsesModuloEightAndDoesNotWriteMemory()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0131, 0x2804); // BTST D0,4(A1,D2.L)
		var cycle = 0L;
		bus.WriteByte(0x0000_4014, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 9;
		cpu.State.D[2] = 0x0000_0010;
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x02, bus.ReadByte(0x0000_4014, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void BtstByteDynamicAbsoluteLongUsesModuloEightAndDoesNotWriteMemory()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0339, 0x0000, 0x4000); // BTST D1,$4000.L
		var cycle = 0L;
		bus.WriteByte(0x0000_4000, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 9;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal((byte)0x02, bus.ReadByte(0x0000_4000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void BchgByteDynamicAddressIndirectUsesModuloEightAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0553); // BCHG D2,(A3)
		var cycle = 0L;
		bus.WriteByte(0x0000_4000, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 9; // Memory bit numbers are reduced modulo 8.
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0000_4000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void BtstByteImmediateAddressIndirectUsesByteModuloAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0817, 0x000B); // BTST #11,(A7), memory bit number is modulo 8
		var cycle = 0L;
		bus.WriteByte(0x0000_2400, 0x08, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[7] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x08, bus.ReadByte(0x0000_2400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void BtstByteImmediatePostIncrementUsesA7ByteStepAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x081F, 0x000B); // BTST #11,(A7)+, memory bit number is modulo 8
		var cycle = 0L;
		bus.WriteByte(0x0000_2400, 0x08, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x2400);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x08, bus.ReadByte(0x0000_2400, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_2402u, cpu.State.A[7]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}

	[Fact]
	public void RolLongImmediateDataRegisterRotatesAllBitsAndPreservesExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE19A); // ROL.L #8,D2
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[2] = 0x8123_4567;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2345_6781u, cpu.State.D[2]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void LslWordImmediateDataRegisterShiftsAndClearsOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE548); // LSL.W #2,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_4001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_0004u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void LslByteImmediateDataRegisterShiftsLowByteAndSetsCarryAndExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE30B); // LSL.B #1,D3
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[3] = 0x1234_5680;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Overflow;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5600u, cpu.State.D[3]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void LslLongImmediateDataRegisterShiftsAndClearsOverflow()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE788); // LSL.L #3,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x2000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0008u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void BchgByteImmediateAbsoluteLongTogglesMemoryAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0879, 0x0001, 0x00BF, 0xE001); // BCHG #1,$BFE001.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_E001, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x00BF_E001, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}
	[Fact]
	public void BchgByteImmediateAddressDisplacementTogglesMemoryAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x086B, 0x0001, 0x0010); // BCHG #1,$10(A3)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x02, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_4000u, cpu.State.A[3]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void BclrByteImmediateAbsoluteLongClearsMemoryAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x08B9, 0x0006, 0x00BF, 0xEE01); // BCLR #6,$BFEE01.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_EE01, 0x40, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x00BF_EE01, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}
	[Fact]
	public void BclrByteImmediateAddressDisplacementClearsMemoryAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x08A9, 0x0006, 0x0010); // BCLR #6,$10(A1)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0xC0, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x80, bus.ReadByte(0x0000_2410, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void BsetByteImmediateAbsoluteLongSetsMemoryAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x08F9, 0x0001, 0x00BF, 0xE001); // BSET #1,$BFE001.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_E001, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x02, bus.ReadByte(0x00BF_E001, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}
	[Fact]
	public void BsetByteImmediateAddressDisplacementSetsMemoryAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x08EE, 0x0005, 0x00FD); // BSET #5,$00FD(A6)
		var cycle = 0L;
		bus.WriteByte(0x0006_E57B, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0006_E47E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x20, bus.ReadByte(0x0006_E57B, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void BsetByteDynamicAddressDisplacementUsesModuloAndReportsPreviousBit()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x01EA, 0x0010); // BSET D0,$10(A2)
		var cycle = 0L;
		bus.WriteByte(0x0000_2410, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 10; // Memory bit number is modulo 8, selects bit 2.
		cpu.State.A[2] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x04, bus.ReadByte(0x0000_2410, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void BtstImmediateDataOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0807, 0x0007); // BTST #7,D7
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[7] = 0x0000_0080;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void BtstDynamicDataUsesModuloBitNumberAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0D05); // BTST D6,D5
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[5] = 0x00C0_0000;
		cpu.State.D[6] = 0x0000_001F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00C0_0000u, cpu.State.D[5]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void BsetImmediateDataSetsBitAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x08C0, 0x0001); // BSET #1,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0003u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void BsetDynamicDataUsesModuloBitNumberAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x05C0); // BSET D2,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;
		cpu.State.D[2] = 0x0000_0021;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0003u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void BclrDynamicDataClearsBitAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0D85); // BCLR D6,D5
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[5] = 0x8000_0001;
		cpu.State.D[6] = 0x0000_003F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0001u, cpu.State.D[5]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void BclrImmediateDataClearsBitAndOnlyUpdatesZeroFlag()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0880, 0x001F); // BCLR #31,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x8000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry |
			M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void WordBranchUsesSignedDisplacementWhenTaken()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x6600, 0x0040); // BNE.W +$40
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 2u + 0x40u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void ByteBranchUsesSignedDisplacementWhenTaken()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x6604); // BNE.B +4
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 2u + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void ByteBranchFallsThroughWhenNotTaken()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x6704); // BEQ.B +4
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void SccDataStoresConditionResultWithoutChangingFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x56C0); // SNE D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_5600;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_56FFu, cpu.State.D[0]);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow | M68kCpuState.Carry),
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void SccAbsoluteLongStoresConditionResultWithoutChangingFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x51F9, 0x00BF, 0xEC01); // SF $BFEC01.L
		var cycle = 0L;
		bus.WriteByte(0x00BF_EC01, 0xAA, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x00, bus.ReadByte(0x00BF_EC01, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void DbccBranchesWhenConditionFalseAndCounterNotExpired()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x51C9, 0xFFFE); // DBF D1,back to opcode
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0001;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0000u, cpu.State.D[1]);
		Assert.Equal(CodeBase, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void DbccFallsThroughWhenCounterExpires()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x51C9, 0xFFFE); // DBF D1,back to opcode
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0000;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_FFFFu, cpu.State.D[1]);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(12, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}
	[Fact]
	public void DbccConditionTrueDoesNotDecrementCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x57C9, 0xFFFE); // DBEQ D1,back to opcode
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_0001u, cpu.State.D[1]);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void WordBranchFallsThroughWhenNotTaken()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x6700, 0x0046); // BEQ.W +$46
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void RtdPullsProgramCounterAndDropsParameterBytes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E74, 0x0008); // RTD #8
		bus.WriteLong(0x3000, 0x0000_2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x300Cu, cpu.State.A[7]);
	}
	[Fact]
	public void RtsPullsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E75); // RTS
		bus.WriteLong(0x3000, 0x00F8_214E);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_214Eu, cpu.State.ProgramCounter);
		Assert.Equal(0x3004u, cpu.State.A[7]);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void JmpAddressIndirectLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4ED1); // JMP (A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_2400;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2400u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}

	[Fact]
	public void AndByteImmediateToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC23C, 0x0081); // AND.B #$81,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5681u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void AndiByteImmediateToDataRegisterUpdatesLowByteAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0201, 0x0081); // ANDI.B #$81,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_56FF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5681u, cpu.State.D[1]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(2, cpu.State.Cycles);
	}
	[Fact]
	public void AndiByteImmediateToAddressIndirectUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0210, 0x0080); // ANDI.B #$80,(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_4000, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x80, bus.ReadByte(0x0000_4000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void AndiByteImmediateToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x022E, 0x00F0, 0x0010); // ANDI.B #$F0,$10(A6)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0xCC, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0xC0, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void OriByteImmediateToAddressDisplacementUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x002E, 0x0080, 0x0010); // ORI.B #$80,$10(A6)
		var cycle = 0L;
		bus.WriteByte(0x0000_4010, 0x01, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		cycle = 0;
		Assert.Equal(0x81, bus.ReadByte(0x0000_4010, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void OriByteImmediateToAddressIndirectUpdatesMemoryAndFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x0010, 0x0080); // ORI.B #$80,(A0)
		var cycle = 0L;
		bus.WriteByte(0x0000_4000, 0x01, ref cycle, M68kBusAccessKind.CpuDataWrite);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		cycle = 0;
		Assert.Equal(0x81, bus.ReadByte(0x0000_4000, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void JmpAddressDisplacementLoadsProgramCounterWithoutChangingAddressRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EEE, 0x0010); // JMP $10(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2410u, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_2400u, cpu.State.A[6]);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void LsrByteImmediateDataRegisterShiftsLogicalAndSetsExtend()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xE809); // LSR.B #4,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x1234_568F;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5608u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void JmpPcBriefIndexedLoadsProgramCounterAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EFB, 0x0004); // JMP 4(PC,D0.W)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0010;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 0x16u, cpu.State.ProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void JmpAbsoluteLongLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EF9, 0x00F8, 0x9676); // JMP $F89676.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_9676u, cpu.State.ProgramCounter);
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void JsrAbsoluteLongPushesReturnAddressAndLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EB9, 0x00F8, 0xB448); // JSR $F8B448.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x00F8_B448u, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 6u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void JsrAddressDisplacementPushesReturnAddressAndLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EAE, 0xFF9A); // JSR -$66(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_239Au, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void JsrAddressIndirectPushesReturnAddressAndLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E91); // JSR (A1)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x00F8_2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00F8_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 2u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry, cpu.State.StatusRegister);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongAddressDisplacementToPredecrementEvaluatesSourceFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x272B, 0x0008); // MOVE.L 8(A3),-(A3)
		bus.WriteLong(0x0000_2408, 0xCAFE_BABE);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_23FCu, cpu.State.A[3]);
		Assert.Equal(0xCAFE_BABEu, bus.ReadLong(0x0000_23FC));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void MoveLongPostIncrementToAddressDisplacementEvaluatesSourceFirst()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x275B, 0x0004); // MOVE.L (A3)+,4(A3)
		bus.WriteLong(0x0000_2400, 0x1234_5678);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[3] = 0x0000_2400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2404u, cpu.State.A[3]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0000_2408));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void JsrPcDisplacementPushesReturnAddressAndLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EBA, 0x0010); // JSR $10(PC)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 0x12u, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, bus.ReadLong(0x0000_2FFC));
	}

	[Fact]
	public void JsrPcBriefIndexedPushesReturnAddressAndLoadsProgramCounter()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4EBB, 0x1808); // JSR 8(PC,D1.L)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[1] = 0x0000_0020;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 0x2Au, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(11, cpu.State.NativeCycles);
		Assert.Equal(6, cpu.State.Cycles);
	}

	[Fact]
	public void PeaPcDisplacementPushesComputedAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x487A, 0x0010); // PEA $10(PC)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 0x12u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}

	[Fact]
	public void PeaAddressIndirectPushesAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4852); // PEA (A2)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[2] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(0x1234_5678u, cpu.State.A[2]);
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(5, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
	}
	[Fact]
	public void PeaAddressDisplacementPushesComputedAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x486E, 0xFFF0); // PEA -$10(A6)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_4100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0x0000_40F0u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void PeaBriefIndexedPushesComputedAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4870, 0x1010); // PEA $10(A0,D1.W)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.D[1] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0x0000_4012u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(9, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void PeaAbsoluteWordSignExtendsAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4878, 0xFFF0); // PEA $FFF0.W
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0xFFFF_FFF0u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void PeaAbsoluteLongPushesAddressAndPreservesFlags()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4879, 0x1234, 0x5678); // PEA $12345678.L
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2FFCu, cpu.State.A[7]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0000_2FFC));
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(7, cpu.State.NativeCycles);
		Assert.Equal(4, cpu.State.Cycles);
	}
	[Fact]
	public void MovemLongRegistersToPredecrementUsesReversedMaskOrder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48E7, 0xFFFE); // MOVEM.L D0-D7/A0-A6,-(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		for (var register = 0; register < 8; register++)
		{
			cpu.State.D[register] = 0xD000_0000u + (uint)register;
			if (register < 7)
			{
				cpu.State.A[register] = 0xA000_0000u + (uint)register;
			}
		}
		cpu.ExecuteInstruction();
		Assert.Equal(0x2FC4u, cpu.State.A[7]);
		for (var register = 0; register < 8; register++)
		{
			Assert.Equal(0xD000_0000u + (uint)register, bus.ReadLong(0x2FC4u + (uint)(register * 4)));
		}
		for (var register = 0; register < 7; register++)
		{
			Assert.Equal(0xA000_0000u + (uint)register, bus.ReadLong(0x2FE4u + (uint)(register * 4)));
		}
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(53, cpu.State.NativeCycles);
		Assert.Equal(27, cpu.State.Cycles);
	}
	[Fact]
	public void MovemWordRegistersToPredecrementUsesReversedMaskAndOriginalAddressSnapshot()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48A7, 0x8081); // MOVEM.W D0/A0/A7,-(A7)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_D001;
		cpu.State.A[0] = 0x5678_A001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2FFAu, cpu.State.A[7]);
		Assert.Equal((ushort)0xD001, bus.ReadWord(0x0000_2FFA));
		Assert.Equal((ushort)0xA001, bus.ReadWord(0x0000_2FFC));
		Assert.Equal((ushort)0x3000, bus.ReadWord(0x0000_2FFE));
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(14, cpu.State.NativeCycles);
		Assert.Equal(7, cpu.State.Cycles);
	}

	[Fact]
	public void MovemWordRegistersToAddressDisplacementUsesNormalMaskOrderAndSnapshotsBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x48A8, 0x0103, 0x0010); // MOVEM.W D0-D1/A0,$10(A0)
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x1234_D001;
		cpu.State.D[1] = 0x5678_D102;
		cpu.State.A[0] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)0xD001, bus.ReadWord(0x0000_4010));
		Assert.Equal((ushort)0xD102, bus.ReadWord(0x0000_4012));
		Assert.Equal((ushort)0x4000, bus.ReadWord(0x0000_4014));
		Assert.Equal(0x0000_4000u, cpu.State.A[0]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(16, cpu.State.NativeCycles);
		Assert.Equal(8, cpu.State.Cycles);
	}

	[Fact]
	public void MovemWordAddressDisplacementToRegistersSignExtendsInNormalMaskOrder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4CA9, 0x0103, 0x0010); // MOVEM.W $10(A1),D0-D1/A0
		bus.WriteWord(0x0000_4010, 0x8001);
		bus.WriteWord(0x0000_4012, 0x0002);
		bus.WriteWord(0x0000_4014, 0xFFFF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 0x0000_4000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_8001u, cpu.State.D[0]);
		Assert.Equal(2u, cpu.State.D[1]);
		Assert.Equal(0xFFFF_FFFFu, cpu.State.A[0]);
		Assert.Equal(0x0000_4000u, cpu.State.A[1]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(20, cpu.State.NativeCycles);
		Assert.Equal(10, cpu.State.Cycles);
	}
	[Fact]
	public void MovemLongPostIncrementToRegistersRestoresNormalMaskOrder()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4CDF, 0x7FFF); // MOVEM.L (A7)+,D0-D7/A0-A6
		for (var register = 0; register < 8; register++)
		{
			bus.WriteLong(0x3000u + (uint)(register * 4), 0xD000_0000u + (uint)register);
		}
		for (var register = 0; register < 7; register++)
		{
			bus.WriteLong(0x3020u + (uint)(register * 4), 0xA000_0000u + (uint)register);
		}
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		for (var register = 0; register < 8; register++)
		{
			Assert.Equal(0xD000_0000u + (uint)register, cpu.State.D[register]);
		}
		for (var register = 0; register < 7; register++)
		{
			Assert.Equal(0xA000_0000u + (uint)register, cpu.State.A[register]);
		}
		Assert.Equal(0x303Cu, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(72, cpu.State.NativeCycles);
		Assert.Equal(36, cpu.State.Cycles);
	}
	[Fact]
	public void MovemLongAddressIndirectToRegistersRestoresNormalMaskOrderWithoutUpdatingBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4CD0, 0x0201); // MOVEM.L (A0),D0/A1
		bus.WriteLong(0x0000_2400, 0x1234_5678);
		bus.WriteLong(0x0000_2404, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[0] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(0x89AB_CDEFu, cpu.State.A[1]);
		Assert.Equal(0x0000_2400u, cpu.State.A[0]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(20, cpu.State.NativeCycles);
		Assert.Equal(10, cpu.State.Cycles);
	}

	[Fact]
	public void MovemLongAddressDisplacementToRegistersRestoresNormalMaskOrderWithoutUpdatingBase()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4CEE, 0x0201, 0x0004); // MOVEM.L 4(A6),D0/A1
		bus.WriteLong(0x0000_2404, 0x1234_5678);
		bus.WriteLong(0x0000_2408, 0x89AB_CDEF);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.A1200Ec02014Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x0000_2400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(0x89AB_CDEFu, cpu.State.A[1]);
		Assert.Equal(0x0000_2400u, cpu.State.A[6]);
		Assert.Equal(
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(22, cpu.State.NativeCycles);
		Assert.Equal(11, cpu.State.Cycles);
	}
	[Fact]
	public void M68030HeadTailOverlapReducesAdjacentInstructionCycles()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x49C0, 0x49C1); // EXTB.L D0; EXTB.L D1
		var cpu = new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(0, cpu.Timing.LastInstructionTiming.OverlapCycles);
		cpu.ExecuteInstruction();
		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
		Assert.Equal(2, cpu.Timing.LastInstructionTiming.OverlapCycles);
	}
	[Fact]
	public void M68030TakenBranchSuppressesNextHeadTailOverlap()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x49C0, // EXTB.L D0
			0x60FF, 0x0000, 0x0004, // BRA.L to next instruction
			0x49C1); // EXTB.L D1
		var cpu = new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(18, cpu.State.NativeCycles);
		Assert.Equal(0, cpu.Timing.LastInstructionTiming.OverlapCycles);
	}
	[Fact]
	public void NopSynchronizesPendingPostedWrite()
	{
		var bus = new ZeroWaitCodeBus { WriteMachineDelay = 20 };
		WriteWords(bus, CodeBase, 0x480E, 0x0000, 0x0000, 0x4E71); // LINK.L A6,#0; NOP
		var cpu = new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x1234_5678;
		cpu.ExecuteInstruction();
		Assert.Equal(16, cpu.State.NativeCycles);
		Assert.Equal(40, cpu.Timing.BusControllerAvailableNativeCycle);
		cpu.ExecuteInstruction();
		Assert.Equal(44, cpu.State.NativeCycles);
		Assert.Equal(22, cpu.State.Cycles);
	}
	[Fact]
	public void EnabledInstructionCacheAvoidsSecondFetchWithinLine()
	{
		const uint cacheableCodeBase = 0x0020_0000;
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			cacheableCodeBase,
			0x4E7B, 0x0002, // MOVEC D0,CACR
			0x4E71, // NOP
			0x4E71); // NOP, same 4-byte cache line as previous NOP
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(cacheableCodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(3, bus.InstructionFetchWords);
		Assert.True(cpu.Timing.InstructionCache.Enabled);
	}

	[Fact]
	public void InstructionCacheRetainsGuestCodeUntilCacrFlush()
	{
		const uint cacheableCodeBase = 0x0020_0000;
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			cacheableCodeBase,
			0x4E7B, 0x0002, // MOVEC D0,CACR: enable the instruction cache
			0x4E71, // NOP: cached with the branch below
			0x60FC, // BRA.S back to the NOP
			0x4E7B, 0x0002); // MOVEC D0,CACR: explicit cache flush
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(cacheableCodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(cacheableCodeBase + 4, cpu.State.ProgramCounter);

		// A data write changes RAM but the current I-cache line remains guest-visible.
		bus.WriteWord(cacheableCodeBase + 4, 0x7207); // MOVEQ #7,D1
		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[1]);

		// CACR bit 3 clears the instruction cache. The next fetch must observe RAM.
		cpu.State.ProgramCounter = cacheableCodeBase + 8;
		cpu.State.D[0] = 0x0000_0009;
		cpu.ExecuteInstruction();
		cpu.State.ProgramCounter = cacheableCodeBase + 4;
		cpu.ExecuteInstruction();
		Assert.Equal(7u, cpu.State.D[1]);
	}

	[Fact]
	public void EnabledInstructionCacheDoesNotCacheChipRamFetches()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, 0x0002, // MOVEC D0,CACR
			0x4E71, // NOP
			0x4E71); // NOP, same 4-byte cache line as previous NOP
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(4, bus.InstructionFetchWords);
		Assert.True(cpu.Timing.InstructionCache.Enabled);
	}
	[Fact]
	public void ProfileWaitStatesDelayBlockingInstructionFetch()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E71); // NOP
		var profile = M68020CpuProfile.CreateForTesting(
			"test-wait",
			M68kAcceleratorModel.M68020,
			2,
			new M68020BusTimingRule(M68020MemoryTarget.ChipRam, M68020BusWidth.Word, 3));
		var cpu = new M68020Interpreter(bus, profile);
		cpu.Reset(CodeBase, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}
	[Fact]
	public void MuluWordDataRegisterToDataRegisterExecutesWithExactTiming()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xC0C0); // MULU.W D0,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0xFFFF_0007;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0031u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.Equal(CodeBase + 2u, cpu.State.ProgramCounter);
		Assert.Equal(46, cpu.State.NativeCycles);
		Assert.Equal(23, cpu.State.Cycles);
	}
	private const uint CodeBase = 0x1000;
}
