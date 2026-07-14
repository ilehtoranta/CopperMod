namespace Copper68k.Tests;

public sealed class M68040MmuTests
{
	private const uint CodeBase = 0x1000;
	private const uint StackBase = 0x8000;

	[Fact]
	public void MovecTransfersM68040MmuControlRegisters()
	{
		var bus = new Copper68kTestBus();
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
	public void CacheControlWithIllegalScopeRaisesLineF()
	{
		const uint handler = 0x2A00;
		var bus = new Copper68kTestBus();
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

	[Fact]
	public void MmuDisabledUsesIdentityTranslation()
	{
		var bus = new Copper68kTestBus();
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
		var bus = new Copper68kTestBus();
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
		var bus = new Copper68kTestBus();
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
		var bus = new Copper68kTestBus();
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
		var bus = new Copper68kTestBus();
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

	private static void WriteWords(Copper68kTestBus bus, uint address, params ushort[] words)
		=> bus.WriteWords(address, words);

}
