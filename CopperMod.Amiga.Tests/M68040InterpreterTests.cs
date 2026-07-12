using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68040InterpreterTests
{
	private const uint CodeBase = 0x1000;
	private const uint StackBase = 0x8000;

	[Fact]
	public void FactoryCreatesAccurateM68040Backend()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68040, new AmigaBus());

		var interpreter = Assert.IsType<M68040Interpreter>(cpu);
		Assert.Same(M68020CpuProfile.Ocs68040Accelerator25Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}

	[Fact]
	public void MovecTransfersM68040MmuControlRegisters()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, 0x0003, // MOVEC D0,TC
			0x4E7A, 0x1003); // MOVEC TC,D1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 0x0000_1234;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_1234u, cpu.State.M68040Mmu.TranslationControl);
		Assert.Equal(0x0000_1234u, cpu.State.D[1]);
	}

	[Fact]
	public void MovecM68060ProcessorConfigurationRegisterProbeRaisesLineF()
	{
		var bus = new AmigaBus();
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
		var bus = new AmigaBus();
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
	public void ApproximateIntegerFallbackExecutesCommonM68000Arithmetic()
	{
		var bus = new AmigaBus();
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
		var bus = new AmigaBus();
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
		var bus = new AmigaBus();
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
	public void M68040JitMaxSpeedProfileUsesFastRomInstructionFetches()
	{
		const uint RomBase = 0x00F8_0000;
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(RomBase, new byte[] { 0x70, 0x01 }); // MOVEQ #1,D0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastRealFastRamDataAccesses()
	{
		const uint RomBase = 0x00F8_0000;
		const uint FastBase = 0x0020_0000;
		var bus = new AmigaBus(realFastRamSize: 0x1000, realFastRamBase: FastBase);
		bus.MapReadOnlyMemory(
			RomBase,
			new byte[]
			{
				0x23, 0xFC, // MOVE.L #$12345678,$00200000.L
				0x12, 0x34,
				0x56, 0x78,
				0x00, 0x20,
				0x00, 0x00
			});
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, bus.ReadLong(FastBase));
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastCiaAPortAAccesses()
	{
		const uint RomBase = 0x00F8_0000;
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(
			RomBase,
			new byte[]
			{
				0x08, 0xB9, // BCLR #1,$00BFE001.L
				0x00, 0x01,
				0x00, 0xBF,
				0xE0, 0x01
			});
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.True(bus.AudioFilterEnabled);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.True(cpu.State.Cycles > 0);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastCiaAPortAWriteAccesses()
	{
		const uint RomBase = 0x00F8_0000;
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(
			RomBase,
			new byte[]
			{
				0x08, 0xF9, // BSET #1,$00BFE001.L
				0x00, 0x01,
				0x00, 0xBF,
				0xE0, 0x01
			});
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.False(bus.AudioFilterEnabled);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.True(cpu.State.Cycles > 0);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void UnsupportedM68040InstructionFormDoesNotReportTimingGap()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x5C00); // FMOVE unsupported packed-decimal format from D0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		var exception = Assert.Throws<UnsupportedM68040InstructionException>(() => cpu.ExecuteInstruction());

		Assert.Equal(0xF200, exception.Opcode);
		Assert.Equal(CodeBase, exception.ProgramCounter);
		Assert.Equal(M68020CpuProfile.Ocs68040Accelerator25Mhz.Name, exception.ProfileName);
		Assert.DoesNotContain("timing", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void MovemLongPostIncrementCanLoadTheAddressRegisterUsedForTheSource()
	{
		var bus = new AmigaBus();
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
	public void ApproximateIntegerFallbackStillUsesTranslatedChipBusAccesses()
	{
		const uint chipAddress = 0x3000;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0x5290); // ADDQ.L #1,(A0)
		bus.WriteLong(chipAddress, 0x0000_0004);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = chipAddress;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0005u, bus.ReadLong(chipAddress));
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
			access.Request.Address == chipAddress &&
			access.Request.Size == AmigaBusAccessSize.Long);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Address == chipAddress &&
			access.Request.Size == AmigaBusAccessSize.Long);
	}

	[Fact]
	public void MoveLongRegisterToAbsoluteWordUsesSingleExtensionWord()
	{
		var bus = new AmigaBus();
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
	public void FmoveControlTransfersFpcrThroughDataRegister()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF200, 0xB000, // FMOVE.L D0,FPCR
			0xF201, 0x9000); // FMOVE.L FPCR,D1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 0x0000_0040;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0040u, cpu.State.M68040Fpu.Fpcr);
		Assert.Equal(0x0000_0040u, cpu.State.D[1]);
	}

	[Fact]
	public void FmoveControlTransfersFpcrThroughMemoryPredecrementAndPostIncrement()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF225, 0xBC00, // FMOVE.L FPCR,-(A5)
			0xF21D, 0x9C00); // FMOVE.L (A5)+,FPCR
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[5] = 0x2000;
		cpu.State.M68040Fpu.Fpcr = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1FFCu, cpu.State.A[5]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x1FFC));

		cpu.State.M68040Fpu.Fpcr = 0;
		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.A[5]);
		Assert.Equal(0x1234_5678u, cpu.State.M68040Fpu.Fpcr);
	}

	[Fact]
	public void FmovemPredecrementAndPostIncrementRoundTripsFpRegisters()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF225, 0xE0FF, // FMOVEM FP0-FP7,-(A5)
			0xF21D, 0xD0FF); // FMOVEM (A5)+,FP0-FP7
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[5] = 0x3000;
		for (var i = 0; i < 8; i++)
		{
			cpu.State.M68040Fpu.FP[i] = i + 0.5;
		}

		cpu.ExecuteInstruction();

		Assert.Equal(0x2FA0u, cpu.State.A[5]);
		for (var i = 0; i < 8; i++)
		{
			cpu.State.M68040Fpu.FP[i] = 0;
		}

		cpu.ExecuteInstruction();

		Assert.Equal(0x3000u, cpu.State.A[5]);
		for (var i = 0; i < 8; i++)
		{
			Assert.Equal(i + 0.5, cpu.State.M68040Fpu.FP[i]);
		}
	}

	[Fact]
	public void FpuMovesDataRegisterToFpRegisterAndAddsFpRegister()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF200, 0x4080, // FMOVE.L D0,FP1
			0xF200, 0x00A2); // FADD.X FP0,FP1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 3;
		cpu.State.M68040Fpu.FP[0] = 2.5;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(5.5, cpu.State.M68040Fpu.FP[1], precision: 8);
		Assert.Equal((uint)0, cpu.State.M68040Fpu.Fpsr & M68040FpuState.ConditionZero);
	}

	[Fact]
	public void FpuMoveStoresFpRegisterToDataRegister()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF202, 0x6080); // FMOVE.L FP1,D2
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[1] = -12.0;

		cpu.ExecuteInstruction();

		Assert.Equal(unchecked((uint)-12), cpu.State.D[2]);
	}

	[Fact]
	public void UnsupportedFpuOperationRaisesLineFException()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x4078);
		bus.WriteLong(11u * 4, 0x0000_2000);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(StackBase - 6u));
		Assert.Equal(11 * 4, bus.ReadWord(StackBase - 2u));
	}

	[Fact]
	public void FsaveAndFrestoreUseM68040NullStateFrameAfterReset()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF327, // FSAVE -(A7)
			0xF35F); // FRESTORE (A7)+
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(StackBase - M68040FpuHelpers.NullStateFrameSize, cpu.State.A[7]);
		Assert.Equal(M68040FpuHelpers.NullStateFrame, bus.ReadLong(cpu.State.A[7]));

		cpu.ExecuteInstruction();

		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
	}

	[Fact]
	public void FsaveUsesM68040IdleStateFrameAfterFpuActivity()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF200,
			0x0080, // FMOVE.X FP0,FP1
			0xF327); // FSAVE -(A7)
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(StackBase - M68040FpuHelpers.IdleStateFrameSize, cpu.State.A[7]);
		Assert.Equal(M68040FpuHelpers.IdleStateFrame, bus.ReadLong(cpu.State.A[7]));
	}

	[Fact]
	public void MmuDisabledUsesIdentityTranslation()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0x2039, 0x0000, 0x2000); // MOVE.L $2000.L,D0
		bus.WriteLong(0x2000, 0xCAFE_BABEu);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0xCAFE_BABEu, cpu.State.D[0]);
	}

	[Fact]
	public void MmuTransparentRangesBypassTables()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0x2039, 0x0000, 0x2000); // MOVE.L $2000.L,D0
		bus.WriteLong(0x2000, 0x1234_5678);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.DataTransparentTranslation0 = 0x0000_8000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
	}

	[Fact]
	public void MmuTranslatesInstructionAndDataAccessesThroughPhysicalTable()
	{
		var bus = new AmigaBus();
		const uint root = 0x4000;
		WriteWords(bus, 0x1000, 0x2039, 0x0000, 0x2000); // MOVE.L $2000.L,D0
		bus.WriteLong(root + 4, 0x0000_1001);
		bus.WriteLong(root + 8, 0x0000_5001);
		bus.WriteLong(0x5000, 0xDEAD_BEEFu);

		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;

		cpu.ExecuteInstruction();

		Assert.Equal(0xDEAD_BEEFu, cpu.State.D[0]);
	}

	[Fact]
	public void PflushInvalidatesCachedTranslation()
	{
		var bus = new AmigaBus();
		const uint root = 0x4000;
		WriteWords(
			bus,
			0x1000,
			0x2039, 0x0000, 0x2000, // MOVE.L $2000.L,D0
			0xF500, 0x0000, // PFLUSH
			0x2239, 0x0000, 0x2000); // MOVE.L $2000.L,D1
		bus.WriteLong(root + 4, 0x0000_1001);
		bus.WriteLong(root + 8, 0x0000_5001);
		bus.WriteLong(0x5000, 0x1111_2222);
		bus.WriteLong(0x6000, 0x3333_4444);

		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;

		cpu.ExecuteInstruction();
		bus.WriteLong(root + 8, 0x0000_6001);
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x1111_2222u, cpu.State.D[0]);
		Assert.Equal(0x3333_4444u, cpu.State.D[1]);
	}

	[Fact]
	public void MmuFaultBuildsBusErrorExceptionFrame()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0x2039, 0x0000, 0x2000); // MOVE.L $2000.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Mmu.SupervisorRootPointer = 0;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_8000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(StackBase - 6u));
		Assert.Equal(2 * 4, bus.ReadWord(StackBase - 2u));
		Assert.NotEqual(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuFetchBeyondConfiguredChipRamRaisesBusErrorInsteadOfMirroring()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(0x0010_0004, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.Equal(0x0010_0004u, bus.ReadLong(StackBase - 6u));
		Assert.Equal(2 * 4, bus.ReadWord(StackBase - 2u));
		Assert.NotEqual(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuDataAccessBeyondConfiguredChipRamKeepsExistingMirrorBehavior()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		bus.WriteLong(0x0000_0004, 0x00F1_0000);
		WriteWords(bus, CodeBase, 0x2039, 0x0010, 0x0004); // MOVE.L $00100004.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0x00F1_0000u, cpu.State.D[0]);
		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuHighUnmappedDataProbeUsesOpenBusInsteadOfBusError()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		WriteWords(bus, CodeBase, 0x2039, 0x00F0, 0x0000); // MOVE.L $00F00000.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuHighThirtyTwoBitUnmappedDataProbeUsesOpenBusInsteadOfBusError()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		WriteWords(bus, CodeBase, 0x2039, 0xFFA0, 0x4A80); // MOVE.L $FFA04A80.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.M68040Mmu.Status);
	}

	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}
}
