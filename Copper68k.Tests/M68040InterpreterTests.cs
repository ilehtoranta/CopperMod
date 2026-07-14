namespace Copper68k.Tests;

public sealed class M68040InterpreterTests
{
	private const uint CodeBase = 0x1000;
	private const uint StackBase = 0x8000;

	[Fact]
	public void FactoryCreatesAccurateM68040Backend()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68040, new Copper68kTestBus());

		var interpreter = Assert.IsType<M68040Interpreter>(cpu);
		Assert.Same(M68020CpuProfile.Ocs68040Accelerator25Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}

	[Fact]
	public void MovecM68060ProcessorConfigurationRegisterProbeRaisesLineF()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x4E7A, 0x1808); // MOVEC PCR,D1
		bus.WriteLong(4u * 4, 0x0000_3000);
		bus.WriteLong(11u * 4, 0x0000_2000);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[1] = 4;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(4u, cpu.State.D[1]);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(StackBase - 6u));
		Assert.Equal(11 * 4, bus.ReadWord(StackBase - 2u));
	}

	[Fact]
	public void Move16CopiesAlignedBlockAndAdvancesAddressRegisters()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0xF620, 0x1000); // MOVE16 (A0)+,(A1)+
		for (var offset = 0u; offset < 16; offset += 4)
		{
			bus.WriteLong(0x3000 + offset, 0x1111_0000u + offset);
		}

		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x3003;
		cpu.State.A[1] = 0x4005;

		cpu.ExecuteInstruction();

		for (var offset = 0u; offset < 16; offset += 4)
		{
			Assert.Equal(0x1111_0000u + offset, bus.ReadLong(0x4000 + offset));
		}

		Assert.Equal(0x3013u, cpu.State.A[0]);
		Assert.Equal(0x4015u, cpu.State.A[1]);
	}

	[Fact]
	public void Move16SameRegisterCopiesLineOverItselfAndAdvancesOnce()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0xF620, 0x0000); // MOVE16 (A0)+,(A0)+
		for (var offset = 0u; offset < 16; offset += 4)
		{
			bus.WriteLong(0x3000 + offset, 0xA0A0_0000u + offset);
		}

		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x3007;

		cpu.ExecuteInstruction();

		for (var offset = 0u; offset < 16; offset += 4)
		{
			Assert.Equal(0xA0A0_0000u + offset, bus.ReadLong(0x3000 + offset));
		}

		Assert.Equal(0x3017u, cpu.State.A[0]);
	}

	[Fact]
	public void ApproximateIntegerFallbackExecutesCommonM68000Arithmetic()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x5280); // ADDQ.L #1,D0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 41;

		cpu.ExecuteInstruction();

		Assert.Equal(42u, cpu.State.D[0]);
		Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
		Assert.True(cpu.State.Cycles > 0);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
	}

	[Fact]
	public void M68040ProfilesUseOneNativeCycleForDirectInstructionTiming()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x7001); // MOVEQ #1,D0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesOneNativeCycleForInternalInstructionTiming()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x7001); // MOVEQ #1,D0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.True(cpu.State.Cycles > 0);
	}

	[Fact]
	public void MovemLongPostIncrementCanLoadTheAddressRegisterUsedForTheSource()
	{
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x4CD8, 0x0100); // MOVEM.L (A0)+,A0
		bus.WriteLong(0x3000, 0x1234_5678);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x3000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x3004u, cpu.State.A[0]);
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.True(cpu.State.Cycles > 0);
	}

	[Fact]
	public void MoveLongRegisterToAbsoluteWordUsesSingleExtensionWord()
	{
		var bus = new Copper68kTestBus();
		WriteWords(
			bus,
			CodeBase,
			0x21C8, 0x0010, // MOVE.L A0,($0010).W
			0x21C0, 0x0014); // MOVE.L D0,($0014).W
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x00F8_0404;
		cpu.State.D[0] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00F8_0404u, bus.ReadLong(0x0010));
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0014));
		Assert.Equal(CodeBase + 8, cpu.State.ProgramCounter);
	}

	[Fact]
	public void MovesRaisesPrivilegeViolationBeforeItsExtensionWordInUserMode()
	{
		const uint handler = 0x2F00;
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x0E27, 0x1234); // MOVES.B <ea>,Rn
		bus.WriteLong(8u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.ResetStackPointers(StackBase, 0x5000, supervisorMode: true);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.LastExceptionVector);
		Assert.Equal(CodeBase, bus.ReadLong(StackBase - 6u));
		Assert.Equal(8 * 4, bus.ReadWord(StackBase - 2u));
	}

	[Fact]
	public void ReservedChk2Cmp2SizeRaisesIllegalInstruction()
	{
		const uint handler = 0x2F40;
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x06D6, 0x1234);
		bus.WriteLong(4u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(CodeBase, bus.ReadLong(StackBase - 6u));
	}

	[Fact]
	public void AndLongAbsoluteWordUsesM68040UnalignedDataAccess()
	{
		var bus = new Copper68kTestBus();
		bus.Memory[CodeBase] = 0xCE;
		bus.Memory[CodeBase + 1] = 0xB8;
		bus.Memory[CodeBase + 2] = 0x4E;
		bus.Memory[CodeBase + 3] = 0x71; // AND.L ($4E71).W,D7
		bus.Memory[0x4E71] = 0x82;
		bus.Memory[0x4E72] = 0xA2;
		bus.Memory[0x4E73] = 0x88;
		bus.Memory[0x4E74] = 0x28;
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[7] = 0xFFFF_FFFF;

		cpu.ExecuteInstruction();

		Assert.Equal(0x82A2_8828u, cpu.State.D[7]);
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void OrLongAddressDisplacementUsesM68040UnalignedWrappedDataAccess()
	{
		var bus = new Copper68kTestBus();
		bus.Memory[CodeBase] = 0x88;
		bus.Memory[CodeBase + 1] = 0xAD;
		bus.Memory[CodeBase + 2] = 0x4E;
		bus.Memory[CodeBase + 3] = 0x71; // OR.L $4E71(A5),D4
		bus.Memory[0x4D71] = 0x00;
		bus.Memory[0x4D72] = 0x00;
		bus.Memory[0x4D73] = 0x00;
		bus.Memory[0x4D74] = 0x2C;
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[5] = 0xFFFF_FF00;
		cpu.State.D[4] = 0xFFFF_0000;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_002Cu, cpu.State.D[4]);
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void Chk2Cmp2PostIncrementEaRaisesIllegalInstruction()
	{
		const uint handler = 0x2A00;
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0x00DC);
		bus.WriteLong(4u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(CodeBase, cpu.State.LastExceptionStackedProgramCounter);
	}

	private static void WriteWords(Copper68kTestBus bus, uint address, params ushort[] words)
		=> bus.WriteWords(address, words);

}
