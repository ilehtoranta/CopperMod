using CopperFloat;
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
	public void Move16SameRegisterCopiesLineOverItselfAndAdvancesOnce()
	{
		var bus = new AmigaBus();
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
	public void FmovecrUsesUnimplementedInstructionFormat2Frame()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x5C00); // FMOVECR #0,FP0
		bus.WriteLong(11u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 12u, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, bus.ReadLong(StackBase - 10u));
		Assert.Equal(0x2000 | (11 * 4), bus.ReadWord(StackBase - 6u));
		Assert.Equal(0u, bus.ReadLong(StackBase - 4u));
		Assert.Equal(CodeBase, cpu.State.M68040Fpu.Fpiar);
		Assert.Equal(M68040FpuFrameKind.Unimplemented, cpu.State.M68040Fpu.StateFrameKind);
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
			0xF200, 0x9000, // FMOVE.L D0,FPCR
			0xF201, 0xB000); // FMOVE.L FPCR,D1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 0x0000_0040;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0040u, cpu.State.M68040Fpu.Fpcr);
		Assert.Equal(0x0000_0040u, cpu.State.D[1]);
		Assert.Equal(0u, cpu.State.M68040Fpu.Fpiar);
	}

	[Fact]
	public void FpcrCachesEveryRoundingAndPrecisionEncoding()
	{
		var fpu = new M68040FpuState();
		var cases = new (uint Value, ExtF80RoundingMode Rounding, ExtF80Precision Precision)[]
		{
			(0x00, ExtF80RoundingMode.ToNearestEven, ExtF80Precision.Extended),
			(0x10, ExtF80RoundingMode.TowardZero, ExtF80Precision.Extended),
			(0x20, ExtF80RoundingMode.TowardNegativeInfinity, ExtF80Precision.Extended),
			(0x30, ExtF80RoundingMode.TowardPositiveInfinity, ExtF80Precision.Extended),
			(0x40, ExtF80RoundingMode.ToNearestEven, ExtF80Precision.Single),
			(0x80, ExtF80RoundingMode.ToNearestEven, ExtF80Precision.Double),
			(0xC0, ExtF80RoundingMode.ToNearestEven, ExtF80Precision.Double)
		};

		foreach (var testCase in cases)
		{
			fpu.Fpcr = testCase.Value;

			Assert.Equal(testCase.Rounding, fpu.Context.RoundingMode);
			Assert.Equal(testCase.Precision, fpu.Context.Precision);
			Assert.Equal(ExtF80TininessMode.BeforeRounding, fpu.Context.TininessMode);
		}
	}

	[Fact]
	public void FpcrCachedExceptionMaskTracksSubsequentWrites()
	{
		var fpu = new M68040FpuState();
		fpu.Fpcr = M68040FpuState.ExceptionDivideByZero;

		Assert.Equal(50, fpu.ApplyExceptions(FloatingPointExceptionFlags.DivideByZero));

		fpu.Fpsr = 0;
		fpu.Fpcr = 0;

		Assert.Equal(0, fpu.ApplyExceptions(FloatingPointExceptionFlags.DivideByZero));
		Assert.NotEqual(0u, fpu.Fpsr & M68040FpuState.ExceptionDivideByZero);
	}

	[Fact]
	public void FmoveControlTransfersFpcrThroughMemoryPredecrementAndPostIncrement()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF225, 0xB000, // FMOVE.L FPCR,-(A5)
			0xF21D, 0x9000); // FMOVE.L (A5)+,FPCR
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[5] = 0x2000;
		cpu.State.M68040Fpu.Fpcr = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1FFCu, cpu.State.A[5]);
		Assert.Equal(0x0000_5678u, bus.ReadLong(0x1FFC));

		cpu.State.M68040Fpu.Fpcr = 0;
		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.A[5]);
		Assert.Equal(0x0000_5678u, cpu.State.M68040Fpu.Fpcr);
	}

	[Fact]
	public void FmoveControlPostincrementUpdatesActiveUserStackPointer()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF21F, 0xAB97); // FMOVE.L FPSR,(A7)+
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;
		cpu.State.SetActiveStackPointer(0x2200);
		cpu.State.M68040Fpu.Fpsr = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2204u, cpu.State.A[7]);
		Assert.Equal(0x2204u, cpu.State.UserStackPointer);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x2200));
	}

	[Fact]
	public void FmoveControlRegisterToPcRelativeEaRaisesLineF()
	{
		const uint handler = 0x2A00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF23A, 0xA5BB, 0x8E2E);
		bus.WriteLong(11u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(11, cpu.State.LastExceptionVector);
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(CodeBase, cpu.State.LastExceptionStackedProgramCounter);
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
			cpu.State.M68040Fpu.FP[i] = F80(i + 0.5);
		}

		cpu.ExecuteInstruction();

		Assert.Equal(0x2FA0u, cpu.State.A[5]);
		for (var i = 0; i < 8; i++)
		{
			cpu.State.M68040Fpu.FP[i] = ExtF80.PositiveZero;
		}

		cpu.ExecuteInstruction();

		Assert.Equal(0x3000u, cpu.State.A[5]);
		for (var i = 0; i < 8; i++)
		{
			Assert.Equal(i + 0.5, F64(cpu.State.M68040Fpu.FP[i]));
		}
	}

	[Fact]
	public void FmovemPostincrementListWithPredecrementEaKeepsExtendedSlotWordOrder()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF226, 0xF780); // FMOVEM.X FP0,-(A6), postincrement list mode
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[6] = 0x220C;
		cpu.State.M68040Fpu.FP[0] = ExtF80.FromBits(0x3FFF, 0x8123_4567_89AB_CDEF);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2200u, cpu.State.A[6]);
		Assert.Equal(0x3FFF_0000u, bus.ReadLong(0x2200));
		Assert.Equal(0x8123_4567u, bus.ReadLong(0x2204));
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x2208));
	}

	[Fact]
	public void FmovemPredecrementListModeWithControlEaUsesNormalRegisterOrder()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF210, 0xC850); // FMOVEM.X (A0),D1 with predecrement list mode
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x2200;
		cpu.State.D[1] = 0x80;
		var one = ExtF80Math.FromInt32(1);
		bus.WriteLong(0x2200, (uint)one.SignExponent << 16);
		bus.WriteLong(0x2204, (uint)(one.Significand >> 32));
		bus.WriteLong(0x2208, (uint)one.Significand);
		var fp7 = cpu.State.M68040Fpu.FP[7];

		cpu.ExecuteInstruction();

		Assert.Equal(one, cpu.State.M68040Fpu.FP[0]);
		Assert.Equal(fp7, cpu.State.M68040Fpu.FP[7]);
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void FmovemInertPredecrementListModeConsumesDisplacementEa()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF22A, 0xE99F, 0x0010); // FMOVEM.X list-mode 01,d16(A2)
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[2] = 0x3000;

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[2]);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void FmovemDynamicMaskSelectsOnlyD0ThroughD3()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF22A, 0xD9DF, 0x0010); // FMOVEM.X d16(A2),D1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[2] = 0x3000;
		cpu.State.D[1] = 0;
		cpu.State.D[5] = 0x80;
		var one = ExtF80Math.FromInt32(1);
		cpu.State.M68040Fpu.FP[0] = one;
		bus.WriteLong(0x3010, 0);
		bus.WriteLong(0x3014, 0x1234_5678);
		bus.WriteLong(0x3018, 0x9ABC_DEF0);

		cpu.ExecuteInstruction();

		Assert.Equal(one, cpu.State.M68040Fpu.FP[0]);
		Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
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
		cpu.State.M68040Fpu.FP[0] = F80(2.5);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(5.5, F64(cpu.State.M68040Fpu.FP[1]), precision: 8);
		Assert.Equal((uint)0, cpu.State.M68040Fpu.Fpsr & M68040FpuState.ConditionZero);
	}

	[Fact]
	public void FmoveFpiarToA7UpdatesActiveUserStackPointer()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF20F, 0xA59E); // FMOVE.L FPIAR,A7
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;
		cpu.State.SetActiveStackPointer(0x3200);
		cpu.State.M68040Fpu.Fpiar = CodeBase;

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase, cpu.State.A[7]);
		Assert.Equal(CodeBase, cpu.State.UserStackPointer);
		Assert.Equal(CodeBase, cpu.State.M68040Fpu.Fpiar);
	}

	[Fact]
	public void FdivTreatsExtendedIntegerBitAsDontCareForInfinity()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF206, 0x12E0); // FSDIV.X FP4,FP5
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[4] = ExtF80.FromBits(0x7FFF, 0);
		cpu.State.M68040Fpu.FP[5] = ExtF80.FromBits(0xFFFF, 0);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 4u, cpu.State.ProgramCounter);
		Assert.Equal(ExtF80.FromBits(0x7FFF, ulong.MaxValue), cpu.State.M68040Fpu.FP[5]);
		Assert.Equal(
			M68040FpuState.ConditionNan |
			M68040FpuState.ExceptionOperandError |
			M68040FpuState.AccruedInvalid,
			cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FdabsPreservesInfinityIntegerBitEncoding()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF208, 0x135C); // FDABS.X FP4,FP6
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[4] = ExtF80.FromBits(0x7FFF, 0);

		cpu.ExecuteInstruction();

		Assert.Equal(ExtF80.FromBits(0x7FFF, 0), cpu.State.M68040Fpu.FP[6]);
		Assert.Equal(M68040FpuState.ConditionInfinity, cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FdabsDataRegisterSourceOverwritesUnsupportedDestination()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF203, 0x43DC); // FDABS.L D3,FP7
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[3] = 0x02C5_6A5A;
		cpu.State.M68040Fpu.FP[7] = ExtF80.FromBits(0x081D, 0x270A_F833_7990_6295);

		cpu.ExecuteInstruction();

		Assert.Equal(ExtF80Math.FromInt32(0x02C5_6A5A), cpu.State.M68040Fpu.FP[7]);
		Assert.Equal(0u, cpu.State.M68040Fpu.Fpsr);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void FmoveExtendedPreservesRawInfinityIntegerBitEncoding()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF226, 0x6A72); // FMOVE.X FP4,-(A6)
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[6] = 0x220C;
		cpu.State.M68040Fpu.FP[4] = ExtF80.FromBits(0x7FFF, 0);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2200u, cpu.State.A[6]);
		Assert.Equal(0x7FFF_0000u, bus.ReadLong(0x2200));
		Assert.Equal(0u, bus.ReadLong(0x2204));
		Assert.Equal(0u, bus.ReadLong(0x2208));
	}

	[Fact]
	public void FsnegPreservesNanSignAndPayload()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x0C5A); // FSNEG.X FP3,FP0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		var nan = ExtF80.FromBits(0x7FFF, ulong.MaxValue);
		cpu.State.M68040Fpu.FP[3] = nan;

		cpu.ExecuteInstruction();

		Assert.Equal(nan, cpu.State.M68040Fpu.FP[0]);
		Assert.Equal(M68040FpuState.ConditionNan, cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FdsqrtForcedPrecisionUnderflowIsNonMaskable()
	{
		const uint handler = 0x2C00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF20E, 0x19C5); // FDSQRT.X FP6,FP3
		bus.WriteLong(51u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		var originalDestination = cpu.State.M68040Fpu.FP[3];
		cpu.State.M68040Fpu.FP[6] = ExtF80.FromBits(0x1341, 0xA489_0933_B04A_5764);

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(originalDestination, cpu.State.M68040Fpu.FP[3]);
		Assert.Equal(
			M68040FpuState.ConditionZero |
			M68040FpuState.ExceptionUnderflow |
			M68040FpuState.ExceptionInexact2 |
			M68040FpuState.AccruedUnderflow |
			M68040FpuState.AccruedInexact,
			cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FsgldivUsesExtendedExponentRange()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x1BA4); // FSGLDIV.X FP6,FP7
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[6] = ExtF80.FromBits(0x1341, 0xA489_0933_B04A_5764);
		cpu.State.M68040Fpu.FP[7] = ExtF80.FromBits(0x2CD7, 0xA7CD_45C7_E1C9_ADDD);

		cpu.ExecuteInstruction();

		Assert.Equal(
			ExtF80.FromBits(0x5995, 0x828A_8D00_0000_0000),
			cpu.State.M68040Fpu.FP[7]);
		Assert.Equal(
			M68040FpuState.ExceptionInexact2 | M68040FpuState.AccruedInexact,
			cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FsglmulTruncatesOperandsBeforeMultiplication()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF225, 0x00A7); // FSGLMUL.X FP0,FP1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[0] = ExtF80.FromBits(0x41EE, 0xF794_47F9_9200_51CC);
		cpu.State.M68040Fpu.FP[1] = ExtF80.FromBits(0x0F28, 0x8795_E9E6_EF87_E2F2);

		cpu.ExecuteInstruction();

		Assert.Equal(
			ExtF80.FromBits(0x1118, 0x8320_2C00_0000_0000),
			cpu.State.M68040Fpu.FP[1]);
		Assert.Equal(
			M68040FpuState.ExceptionInexact2 | M68040FpuState.AccruedInexact,
			cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FmoveDoubleUnderflowStoresRoundedMemoryResultBeforeException()
	{
		const uint handler = 0x2E00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF221, 0x7726); // FMOVE.D FP6,-(A1)
		bus.WriteLong(51u * 4, handler);
		bus.WriteLong(0x78, 0xAAAA_AAAA);
		bus.WriteLong(0x7C, 0xBBBB_BBBB);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[1] = 0x80;
		cpu.State.M68040Fpu.FP[6] = ExtF80.FromBits(0x1341, 0xA489_0933_B04A_5764);

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(0x78u, cpu.State.A[1]);
		Assert.Equal(0u, bus.ReadLong(0x78));
		Assert.Equal(0u, bus.ReadLong(0x7C));
	}

	[Fact]
	public void FdabsForcedDoubleUnderflowIsNonMaskable()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF235, 0x1DDC, // FDABS.X FP7,FP3
			0x7274); // MOVEQ #$74,D1
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[7] = ExtF80.FromBits(0x2CD7, 0xA7CD_45C7_E1C9_ADDD);

		cpu.ExecuteInstruction();

		Assert.Equal(0u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[1]);
		Assert.Equal(M68040FpuState.DefaultNan, cpu.State.M68040Fpu.FP[3]);
		Assert.Equal(
			M68040FpuState.ConditionZero |
			M68040FpuState.ExceptionUnderflow |
			M68040FpuState.ExceptionInexact2 |
			M68040FpuState.AccruedUnderflow |
			M68040FpuState.AccruedInexact,
			cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FsaddPreservesExtendedNanPayload()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF20D, 0x0D62); // FSADD.X FP3,FP2
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[2] = ExtF80.PositiveZero;
		cpu.State.M68040Fpu.FP[3] = ExtF80.FromBits(0x7FFF, ulong.MaxValue);

		cpu.ExecuteInstruction();

		Assert.Equal(ExtF80.FromBits(0x7FFF, ulong.MaxValue), cpu.State.M68040Fpu.FP[2]);
		Assert.Equal(M68040FpuState.ConditionNan, cpu.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void FpuMoveStoresFpRegisterToDataRegister()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF202, 0x6080); // FMOVE.L FP1,D2
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.FP[1] = F80(-12.0);

		cpu.ExecuteInstruction();

		Assert.Equal(unchecked((uint)-12), cpu.State.D[2]);
	}

	[Fact]
	public void FmoveEmptyControlRegisterMaskIsNoOp()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF211, 0x83AC); // FMOVE.L (A1),<empty control list>
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[1] = 0;
		cpu.State.M68040Fpu.Fpiar = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.M68040Fpu.Fpiar);
		Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
	}

	[Fact]
	public void FmoveEmptyControlRegisterMaskConsumesEaExtensions()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF229, 0x8123, 0x0010); // FMOVE.L 16(A1),<empty control list>
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[1] = 0x2200;

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void FmoveEmptyControlRegisterMaskToPcIndexedEaRaisesLineF()
	{
		const uint handler = 0x2A00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF23B, 0xA313, 0xF832, 0x4E71);
		bus.WriteLong(11u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(11, cpu.State.LastExceptionVector);
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(CodeBase, cpu.State.LastExceptionStackedProgramCounter);
	}

	[Fact]
	public void CacheControlWithIllegalScopeRaisesLineF()
	{
		const uint handler = 0x2A00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF481);
		bus.WriteLong(11u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(11, cpu.State.LastExceptionVector);
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(CodeBase, cpu.State.LastExceptionStackedProgramCounter);
	}

	[Theory]
	[InlineData(0, 0x6080, 54)]
	[InlineData(1, 0x6080, 52)]
	[InlineData(2, 0x7880, 52)]
	public void FmoveIntegerDestinationExceptionsAreNonMaskable(int sourceKind, ushort extension, int vector)
	{
		const uint handler = 0x2800;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF202, extension); // FMOVE FP1,D2
		bus.WriteLong((uint)vector * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[2] = 0xA5A5_5A5A;
		cpu.State.M68040Fpu.Fpcr = 0;
		cpu.State.M68040Fpu.FP[1] = sourceKind switch
		{
			0 => ExtF80.FromBits(0x7FFF, 0x8000_0000_0000_0001),
			1 => ExtF80.QuietNaN,
			_ => ExtF80Math.FromInt32(sbyte.MinValue)
		};

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		var expectedDestination = sourceKind switch
		{
			0 => 0x8000_0000u,
			1 => 0xC000_0000u,
			_ => 0xA5A5_5A80u
		};
		Assert.Equal(expectedDestination, cpu.State.D[2]);
		Assert.Equal(CodeBase, cpu.State.M68040Fpu.Fpiar);
		Assert.Equal(StackBase - 12u, cpu.State.A[7]);
		Assert.Equal(0x3000 | (vector * 4), bus.ReadWord(StackBase - 6u));
		Assert.NotEqual(
			0u,
			cpu.State.M68040Fpu.Fpsr &
				(vector == 54 ? M68040FpuState.ExceptionSignalingNan : M68040FpuState.ExceptionOperandError));
	}

	[Fact]
	public void FmoveUnsupportedExtendedSourceRaisesPostInstructionException()
	{
		const uint handler = 0x2A00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF210, 0x6880); // FMOVE.X FP1,(A0)
		bus.WriteLong(55u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x2200;
		cpu.State.M68040Fpu.FP[1] = ExtF80.FromBits(0, 1);

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(0u, bus.ReadLong(0x2200));
		Assert.Equal(M68040FpuFrameKind.Busy, cpu.State.M68040Fpu.StateFrameKind);
		Assert.Equal(0x3000 | (55 * 4), bus.ReadWord(StackBase - 6u));
		Assert.Equal(0x2200u, bus.ReadLong(StackBase - 4u));
	}

	[Fact]
	public void ResetInitializesFpRegistersToDocumentedNonsignalingNan()
	{
		var cpu = new M68040Interpreter(new AmigaBus(), M68020CpuProfile.Ocs68040Accelerator25Mhz);

		cpu.Reset(CodeBase, StackBase);

		Assert.All(cpu.State.M68040Fpu.FP, value =>
		{
			Assert.Equal((ushort)0x7FFF, value.SignExponent);
			Assert.Equal(ulong.MaxValue, value.Significand);
			Assert.Equal(ExtF80Class.QuietNaN, value.Classification);
		});
	}

	[Fact]
	public void FmoveExtendedMemoryRoundTripPreservesAll96BitSlotFields()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF210, 0x4880, // FMOVE.X (A0),FP1
			0xF211, 0x6880); // FMOVE.X FP1,(A1)
		bus.WriteLong(0x2000, 0xC123_ABCD);
		bus.WriteLong(0x2004, 0x89AB_CDEF);
		bus.WriteLong(0x2008, 0x0123_4567);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[1] = 0x2100;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(ExtF80.FromBits(0xC123, 0x89AB_CDEF_0123_4567), cpu.State.M68040Fpu.FP[1]);
		Assert.Equal(0xC123_0000u, bus.ReadLong(0x2100));
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(0x2104));
		Assert.Equal(0x0123_4567u, bus.ReadLong(0x2108));
	}

	[Fact]
	public void ForcedSingleMoveRoundsWithoutChangingFpcrPrecision()
	{
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x0440); // FSMOVE.X FP1,FP0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.Fpcr = 0;
		cpu.State.M68040Fpu.FP[1] = ExtF80.FromBits(0x3FFF, 0x8000_0080_0000_0000);

		cpu.ExecuteInstruction();

		Assert.Equal(ExtF80Math.FromInt32(1), cpu.State.M68040Fpu.FP[0]);
		Assert.Equal(M68040FpuState.ExceptionInexact2, cpu.State.M68040Fpu.Fpsr & M68040FpuState.ExceptionInexact2);
		Assert.Equal(M68040FpuState.AccruedInexact, cpu.State.M68040Fpu.Fpsr & M68040FpuState.AccruedInexact);
	}

	[Fact]
	public void EnabledDivideByZeroVectorsAndSuppressesDestinationWrite()
	{
		const uint handler = 0x2400;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x0420); // FDIV.X FP1,FP0
		bus.WriteLong(50u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.Fpcr = M68040FpuState.ExceptionDivideByZero;
		cpu.State.M68040Fpu.FP[0] = ExtF80Math.FromInt32(1);
		cpu.State.M68040Fpu.FP[1] = ExtF80.PositiveZero;

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(ExtF80Math.FromInt32(1), cpu.State.M68040Fpu.FP[0]);
		Assert.Equal(CodeBase, cpu.State.M68040Fpu.Fpiar);
		Assert.NotEqual(0u, cpu.State.M68040Fpu.Fpsr & M68040FpuState.ExceptionDivideByZero);
		Assert.NotEqual(0u, cpu.State.M68040Fpu.Fpsr & M68040FpuState.AccruedDivideByZero);
		Assert.Equal(StackBase - 12u, cpu.State.A[7]);
		Assert.Equal(CodeBase + 4u, bus.ReadLong(StackBase - 10u));
		Assert.Equal(0x3000 | (50 * 4), bus.ReadWord(StackBase - 6u));
		Assert.Equal(0u, bus.ReadLong(StackBase - 4u));
	}

	[Fact]
	public void FbccFsccAndFdbccUseFpsrConditions()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF28F, 0x0006, // FBT.W +6
			0x7001,         // MOVEQ #1,D0 (skipped)
			0x4E71,         // NOP (skipped)
			0xF240, 0x000F, // FST D0
			0xF249, 0x0000, 0xFFF8); // FDBF D1,-8
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[1] = 1;

		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);

		cpu.ExecuteInstruction();
		Assert.Equal(0xFFu, cpu.State.D[0] & 0xFF);

		cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[1] & 0xFFFF);
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void SignalingUnorderedConditionRaisesBsunWhenEnabled()
	{
		const uint handler = 0x2600;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF298, 0x0002); // FBSUN.W +2
		bus.WriteLong(48u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Fpu.Fpcr = M68040FpuState.ExceptionBranchUnordered;
		cpu.State.M68040Fpu.Fpsr = M68040FpuState.ConditionNan;

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.NotEqual(0u, cpu.State.M68040Fpu.Fpsr & M68040FpuState.ExceptionBranchUnordered);
		Assert.NotEqual(0u, cpu.State.M68040Fpu.Fpsr & M68040FpuState.AccruedInvalid);
	}

	[Fact]
	public void FbccReservedConditionRaisesLineF()
	{
		const uint handler = 0x2A00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF2A0, 0x0074);
		bus.WriteLong(11u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(11, cpu.State.LastExceptionVector);
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(CodeBase, cpu.State.LastExceptionStackedProgramCounter);
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
	public void FsaveAndFrestoreSupportAbsoluteWordStateFrameAddress()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			CodeBase,
			0xF338, 0x3000, // FSAVE ($3000).W
			0xF378, 0x3000); // FRESTORE ($3000).W
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(M68040FpuHelpers.NullStateFrame, bus.ReadLong(0x3000));
		Assert.Equal(CodeBase + 8, cpu.State.ProgramCounter);
		Assert.Equal(M68040FpuFrameKind.Null, cpu.State.M68040Fpu.StateFrameKind);
	}

	[Theory]
	[InlineData(0xF318)] // FSAVE (A0)+
	[InlineData(0xF360)] // FRESTORE -(A0)
	public void FpuStateInstructionsRejectDirectionSpecificInvalidEa(ushort opcode)
	{
		const uint handler = 0x2C00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, opcode);
		bus.WriteLong(11u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x3000;

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[0]);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
	}

	[Theory]
	[InlineData(0xF310)] // FSAVE (A0)
	[InlineData(0xF350)] // FRESTORE (A0)
	public void FpuStateInstructionsArePrivileged(ushort opcode)
	{
		const uint handler = 0x2E00;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, opcode);
		bus.WriteLong(8u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.ResetStackPointers(StackBase, 0x5000, supervisorMode: true);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(8 * 4, bus.ReadWord(StackBase - 2u));
	}

	[Fact]
	public void MovesRaisesPrivilegeViolationBeforeItsExtensionWordInUserMode()
	{
		const uint handler = 0x2F00;
		var bus = new AmigaBus();
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
		var bus = new AmigaBus();
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
		var bus = new AmigaBus();
		var rom = new byte[0x40000];
		rom[CodeBase] = 0xCE;
		rom[CodeBase + 1] = 0xB8;
		rom[CodeBase + 2] = 0x4E;
		rom[CodeBase + 3] = 0x71; // AND.L ($4E71).W,D7
		rom[0x4E71] = 0x82;
		rom[0x4E72] = 0xA2;
		rom[0x4E73] = 0x88;
		rom[0x4E74] = 0x28;
		bus.MapReadOnlyMemory(0x00FC_0000, rom);
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
		var bus = new AmigaBus();
		var rom = new byte[0x40000];
		rom[CodeBase] = 0x88;
		rom[CodeBase + 1] = 0xAD;
		rom[CodeBase + 2] = 0x4E;
		rom[CodeBase + 3] = 0x71; // OR.L $4E71(A5),D4
		rom[0x4D71] = 0x00;
		rom[0x4D72] = 0x00;
		rom[0x4D73] = 0x00;
		rom[0x4D74] = 0x2C;
		bus.MapReadOnlyMemory(0x00FC_0000, rom);
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
		var bus = new AmigaBus();
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
	public void FsaveWritesPopulatedM68040UnimplementedStateFrame()
	{
		const uint handler = 0x2400;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x5C00); // FMOVECR #0,FP0
		WriteWords(bus, handler, 0xF326); // FSAVE -(A6)
		bus.WriteLong(11u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[6] = 0x4000;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		var frameAddress = 0x4000u - M68040FpuHelpers.UnimplementedStateFrameSize;
		Assert.Equal(frameAddress, cpu.State.A[6]);
		Assert.Equal(M68040FpuHelpers.UnimplementedStateFrame, bus.ReadLong(frameAddress));
		Assert.Equal(0x5C00_0000u, bus.ReadLong(frameAddress + 0x10));
		Assert.Equal(M68040FpuHelpers.UnimplementedStateFrameSize, cpu.State.M68040Fpu.LastStateFrameSize);
		Assert.Equal(M68040FpuFrameKind.Idle, cpu.State.M68040Fpu.StateFrameKind);
	}

	[Fact]
	public void FsaveAndFrestoreRoundTripM68040BusyStateFrame()
	{
		const uint handler = 0x2600;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0xF200, 0x0423); // FMUL.X FP1,FP0
		WriteWords(
			bus,
			handler,
			0xF326, // FSAVE -(A6)
			0xF356); // FRESTORE (A6)
		bus.WriteLong(53u * 4, handler);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[6] = 0x5000;
		cpu.State.M68040Fpu.Fpcr = M68040FpuState.ExceptionOverflow;
		cpu.State.M68040Fpu.FP[0] = ExtF80.FromBits(0x7FFE, 0xFFFF_FFFF_FFFF_FFFF);
		cpu.State.M68040Fpu.FP[1] = ExtF80Math.FromInt32(2);

		cpu.ExecuteInstruction();
		Assert.Equal(M68040FpuFrameKind.Busy, cpu.State.M68040Fpu.StateFrameKind);

		cpu.ExecuteInstruction();

		var frameAddress = 0x5000u - M68040FpuHelpers.BusyStateFrameSize;
		Assert.Equal(frameAddress, cpu.State.A[6]);
		Assert.Equal(M68040FpuHelpers.BusyStateFrame, bus.ReadLong(frameAddress));
		Assert.Equal(CodeBase, bus.ReadLong(frameAddress + 0x28));
		Assert.Equal(0x0423_0000u, bus.ReadLong(frameAddress + 0x40));
		Assert.Equal(M68040FpuFrameKind.Idle, cpu.State.M68040Fpu.StateFrameKind);

		cpu.ExecuteInstruction();

		Assert.Equal(M68040FpuFrameKind.Busy, cpu.State.M68040Fpu.StateFrameKind);
		Assert.Equal((ushort)0x0423, cpu.State.M68040Fpu.StateFrameCommand);
		Assert.Equal(CodeBase, cpu.State.M68040Fpu.Fpiar);
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

	[Fact]
	public void PhysicalMapCacheObservesStrictMappingChanges()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		WriteWords(
			bus,
			CodeBase,
			0x2039, 0x00F0, 0x0000, // MOVE.L $00F00000.L,D0
			0x2239, 0x00F0, 0x0000); // MOVE.L $00F00000.L,D1
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);

		bus.StrictCpuPhysicalDataMapping = true;
		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.NotEqual(0u, cpu.State.M68040Mmu.Status);
	}

	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	private static ExtF80 F80(double value)
		=> ExtF80Math.FromBinary64Bits(unchecked((ulong)BitConverter.DoubleToInt64Bits(value))).Value;

	private static double F64(ExtF80 value)
		=> ExtF80Math.ToDouble(value).Value;
}
