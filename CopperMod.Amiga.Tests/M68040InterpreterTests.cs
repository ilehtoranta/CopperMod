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

	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}
}
