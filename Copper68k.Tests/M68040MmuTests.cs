using Copper68k.Tests.Synthetic;

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
		cpu.State.D[0] = 0x0000_4000; // Implemented page-size bit; translation disabled.

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.M68040Mmu.TranslationControl);
		Assert.Equal(0x0000_4000u, cpu.State.D[1]);
	}

	[Fact]
	public void CacheControlWithIllegalScopeRaisesIllegalInstruction()
	{
		const uint handler = 0x2A00;
		var bus = new Copper68kTestBus();
		WriteWords(bus, CodeBase, 0xF481);
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
		cpu.State.M68040Mmu.TranslationControl = 0x8000;
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_A000;
		cpu.State.M68040Mmu.DataTransparentTranslation0 = 0x0000_A000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
	}

	[Fact]
	public void MmuTranslatesInstructionAndDataAccessesThroughPhysicalTable()
	{
		var bus = new Copper68kTestBus();
		const uint root = 0x4000;
		WriteWords(bus, 0x1000, 0x2039, 0x0000, 0x2000); // MOVE.L $2000.L,D0
		bus.WriteLong(root, 0x4202);
		bus.WriteLong(0x4200, 0x4402);
		bus.WriteLong(0x4404, 0x1001);
		bus.WriteLong(0x4408, 0x5001);
		bus.WriteLong(0x5000, 0xDEAD_BEEFu);

		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000;

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
			0xF518, // PFLUSHA (one word)
			0x2239, 0x0000, 0x2000); // MOVE.L $2000.L,D1
		bus.WriteLong(root, 0x4202);
		bus.WriteLong(0x4200, 0x4402);
		bus.WriteLong(0x4404, 0x1001);
		bus.WriteLong(0x4408, 0x5001);
		bus.WriteLong(0x5000, 0x1111_2222);
		bus.WriteLong(0x6000, 0x3333_4444);

		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000;

		cpu.ExecuteInstruction();
		bus.WriteLong(0x4408, 0x6001);
		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 8, cpu.State.ProgramCounter);
		cpu.ExecuteInstruction();

		Assert.Equal(0x1111_2222u, cpu.State.D[0]);
		Assert.Equal(0x3333_4444u, cpu.State.D[1]);
	}

	[Fact]
	public void PtestUsesDfcAndOpcodeReadWriteBitAndRefreshesTheSelectedAtcEntry()
	{
		// Retain DFC, R/W, register and I/D-cache routing with three-level tables.
		foreach (var dfc in new uint[] { 1, 2, 5, 6 })
		foreach (var read in new[] { false, true })
		for (var reg = 0; reg < 8; reg++)
		{
			var bus = new SparseRecordingBus();
			var opcode = (ushort)((read ? 0xf568 : 0xf548) | reg);
			bus.Initialize(CodeBase, opcode, 2);
			bus.Initialize(CodeBase + 2, 0x747b, 2);
			const uint userDescriptor = 0x4424, supervisorDescriptor = 0x6424;
			bus.Initialize(0x4100, 0x4202, 4); bus.Initialize(0x4200, 0x4402, 4);
			bus.Initialize(0x6100, 0x6202, 4); bus.Initialize(0x6200, 0x6402, 4);
			bus.Initialize(userDescriptor, 0x5001, 4);
			bus.Initialize(supervisorDescriptor, 0x6001, 4);
			var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
			cpu.Reset(CodeBase, StackBase);
			cpu.State.DestinationFunctionCode = dfc;
			cpu.State.A[reg] = 0x80009000;
			cpu.State.M68040Mmu.UserRootPointer = 0x4000;
			cpu.State.M68040Mmu.SupervisorRootPointer = 0x6000;
			cpu.State.M68040Mmu.TranslationControl = 0x8000;
			cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0xC000; // low-byte code only
			var instructionSpace = dfc is 2 or 6;
			var kind = instructionSpace ? M68kBusAccessKind.CpuInstructionFetch : M68kBusAccessKind.CpuDataRead;
			var otherKind = instructionSpace ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuInstructionFetch;
			var supervisor = dfc is 5 or 6;
			var mmu = cpu.State.M68040Mmu;
			Assert.True(mmu.TryTranslate(0x80009000, kind, false, supervisor, a => bus.Peek(a, 4), out _, out _, (a, v) => bus.Initialize(a, v, 4)));
			Assert.True(mmu.TryTranslate(0x80009000, otherKind, false, supervisor, a => bus.Peek(a, 4), out _, out _, (a, v) => bus.Initialize(a, v, 4)));
			var descriptor = supervisor ? supervisorDescriptor : userDescriptor;
			bus.Initialize(descriptor, 0x7005, 4); // changed mapping, write-protected
			bus.Accesses.Clear();

			cpu.ExecuteInstruction();

			Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
			Assert.Equal(-1, cpu.State.LastExceptionVector);
			Assert.Single(bus.Accesses.Where(a => a.Address == descriptor && !a.Write && a.Width == 4));
			Assert.DoesNotContain(bus.Accesses, a => a.Address == 0x80009000);
			Assert.Equal(read ? 0x7005u : 4u, mmu.Status);
			Assert.True(mmu.TryTranslate(0x80009000, otherKind, false, supervisor, a => bus.Peek(a, 4), out var otherSpace, out _));
			Assert.Equal(supervisor ? 0x6000u : 0x5000u, otherSpace); // unselected I/D entry remains cached
			cpu.ExecuteInstruction();
			Assert.Equal(123u, cpu.State.D[2]);
			Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
		}
	}

	[Theory]
	[InlineData(0xf500)]
	[InlineData(0xf508)]
	[InlineData(0xf510)]
	[InlineData(0xf518)]
	[InlineData(0xf548)]
	[InlineData(0xf568)]
	public void UserMmuInstructionDoesNotFlushOrProbeAtc(int opcode)
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(CodeBase, [(ushort)opcode, 0x747b]);
		bus.WriteLong(8 * 4, 0x9000);
		bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402);
		bus.WriteLong(0x4408, 0x5001);
		var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.SetUserStackPointer(0x7800);
		cpu.State.StatusRegister = 0x1f;
		cpu.State.DestinationFunctionCode = 1;
		cpu.State.A[0] = 0x2000;
		var mmu = cpu.State.M68040Mmu;
		mmu.UserRootPointer = 0x4000;
		mmu.SupervisorRootPointer = 0x4000;
		mmu.TranslationControl = 0x8000;
		mmu.InstructionTransparentTranslation0 = 0xC000;
		// Supervisor vector and stack mappings during the privilege fault.
		bus.WriteLong(0x4400, 0x0001);
		bus.WriteLong(0x441c, 0x7001);
		Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, false, bus.ReadLong, out _, out _, bus.WriteLong));
		bus.WriteLong(0x4408, 0x6001);

		cpu.ExecuteInstruction();

		Assert.Equal(8, cpu.State.LastExceptionVector);
		Assert.Equal(CodeBase, cpu.State.LastExceptionStackedProgramCounter);
		Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, false, bus.ReadLong, out var stillCached, out _));
		Assert.Equal(0x5000u, stillCached);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void JitFallbackMmuInstructionsKeepNextInstructionAndPrivilegeBoundary(bool enableV2)
	{
		foreach (var opcode in new ushort[] { 0xf500, 0xf508, 0xf510, 0xf518, 0xf548, 0xf568 })
		foreach (var supervisor in new[] { false, true })
		{
			var bus = new Copper68kTestBus();
			bus.WriteWords(CodeBase, [opcode, 0x747b, 0x4e71]);
			bus.WriteLong(8 * 4, 0x9000);
			using var jit = M68kJitCore.CreateM68040ForTesting(bus, enableV2);
			jit.Reset(CodeBase, StackBase);
			jit.State.SetUserStackPointer(0x7800);
			jit.State.StatusRegister = (ushort)((supervisor ? 0x2000 : 0) | 0x1f);
			jit.State.DestinationFunctionCode = 5;
			jit.ExecuteInstruction();
			if (supervisor)
			{
				Assert.Equal(CodeBase + 2, jit.State.ProgramCounter);
				Assert.Equal(0x201f, jit.State.StatusRegister);
				jit.ExecuteInstruction();
				Assert.Equal(CodeBase + 4, jit.State.ProgramCounter);
				Assert.Equal(123u, jit.State.D[2]);
			}
			else
			{
				Assert.Equal(8, jit.State.LastExceptionVector);
				Assert.Equal(0x9000u, jit.State.ProgramCounter);
				Assert.Equal(CodeBase, bus.ReadLong(StackBase - 6));
				Assert.Equal(0x1f, bus.ReadWord(StackBase - 8));
			}
		}
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
		cpu.State.M68040Mmu.TranslationControl = 0x8000;
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_A000;
		bus.WriteLong(0x4000, 0x4202);
		bus.WriteLong(0x4200, 0x4402);
		bus.WriteLong(0x4400, 1); // vector page
		bus.WriteLong(0x441C, 0x7001); // supervisor stack page
		cpu.State.M68040Mmu.SupervisorRootPointer = 0x4000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 60u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(StackBase - 58u));
		Assert.Equal(0x7008, bus.ReadWord(StackBase - 54u));
		Assert.Equal(0x2000u, bus.ReadLong(StackBase - 40u));
		Assert.Equal(0x505, bus.ReadWord(StackBase - 48u));
	}

	private static void WriteWords(Copper68kTestBus bus, uint address, params ushort[] words)
		=> bus.WriteWords(address, words);

}
