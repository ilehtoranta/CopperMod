using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68010InterpreterTests
{
    [Fact]
    public void InvalidRteFrameIsRetainedBelowFormatErrorFrame()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x4E73);
        bus.WriteLong(14 * 4, 0x2000);
        WriteWords(bus, 0x3000, 0x2700, 0, 0x1800, 0x1000);
        var cpu = new M68010Interpreter(bus); cpu.Reset(CodeBase, 0x3000);
        cpu.ExecuteInstruction();
        Assert.Equal(14, cpu.State.LastExceptionVector);
        Assert.Equal(0x2FF8u, cpu.State.A[7]); Assert.Equal(0x2000u, cpu.State.ProgramCounter);
        Assert.Equal(0x1000, bus.ReadWord(0x3006));
        Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
    }

    [Fact]
    public void Format8RteConsumesEntireFrameAndRetainsLiveStackBank()
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, 0x3211);
        WriteWords(bus, 0x2000, 0x4E73);
        bus.WriteLong(12, 0x2000);
        var cpu = new M68010Interpreter(bus); cpu.Reset(CodeBase, 0x3000); cpu.State.A[1] = 1;
        cpu.ExecuteInstruction();
        // Exercise an unmarked structural frame, not a marked MOVE restart.
        bus.WriteWord(cpu.State.A[7] + 28, 0);
        bus.WriteLong(cpu.State.A[7] + 2, CodeBase + 2);
        cpu.ExecuteInstruction();
        Assert.Equal(0x3000u, cpu.State.A[7]);
        Assert.Equal(0x3000u, cpu.State.SupervisorStackPointer);
        Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Fact]
    public void MoveFromSrIsPrivilegedButMoveFromCcrIsAvailableInUserMode()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x42C0, 0x40C1);
        bus.WriteLong(8 * 4, 0x2000);
        var cpu = new M68010Interpreter(bus); cpu.Reset(CodeBase, 0x3000);
        cpu.State.ResetStackPointers(0x3000, 0x4000, supervisorMode: false);
        cpu.State.StatusRegister = 0x15;
        cpu.ExecuteInstruction(); Assert.Equal(0x15u, cpu.State.D[0]);
        cpu.ExecuteInstruction(); Assert.Equal(8, cpu.State.LastExceptionVector); Assert.Equal(0x2000u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void RtdPopsReturnPcAndAppliesSignedStackDisplacement()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x4E74, 0xFFF8);
        bus.WriteLong(0x3000, 0x2000);
        var cpu = new M68010Interpreter(bus); cpu.Reset(CodeBase, 0x3000);
        cpu.ExecuteInstruction(); Assert.Equal(0x2000u, cpu.State.ProgramCounter); Assert.Equal(0x2FFCu, cpu.State.A[7]);
    }

	[Fact]
	public void FactoryCreatesM68010CoreWithoutM68020StackMode()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68010, new Copper68kTestBus());
		var interpreter = Assert.IsType<M68010Interpreter>(cpu);
		Assert.False(interpreter.State.M68020StackModeEnabled);
	}

	[Fact]
	public void MovecInUserModeRaisesPrivilegeViolationWithoutChangingControlRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E7B, 0x0801); // MOVEC D0,VBR
		bus.WriteLong(0x0400 + (8u * 4), 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.ResetStackPointers(0x3000, 0x4000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0400u, cpu.State.VectorBaseRegister);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(0, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(8 * 4, bus.ReadWord(0x2FFE));
	}

	[Fact]
	public void MovecUnsupportedControlRegisterRaisesIllegalWithoutChangingDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E7A, 0x1808); // MOVEC PCR,D1
		bus.WriteLong(0x0400 + (4u * 4), 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.D[1] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.D[1]);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(4 * 4, bus.ReadWord(0x2FFE));
	}

	[Fact]
	public void ExceptionUsesVectorBaseAndFormatZeroFrameAndRteRestoresState()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xA000);
		bus.WriteLong(0x0400 + (10u * 4), 0x0000_2000);
		WriteWords(bus, 0x2000, 0x4E73); // RTE
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(10 * 4, bus.ReadWord(0x2FFE));

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, cpu.State.StatusRegister);
	}

	[Fact]
	public void InterruptUsesVectorBaseAndFormatZeroFrame()
	{
		var bus = new ZeroWaitCodeBus();
		bus.WriteLong(0x0400 + (27u * 4), 0x0000_2400);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.RequestInterrupt(3, 27u * 4);

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(27 * 4, bus.ReadWord(0x2FFE));
		Assert.Equal(3, (cpu.State.StatusRegister >> 8) & 7);
	}

	[Fact]
	public void SharedInstructionsMatchM68000ExecutionModel()
	{
		var m68000Bus = new ZeroWaitCodeBus();
		var m68010Bus = new ZeroWaitCodeBus();
		var program = new ushort[]
		{
			0x7001, // MOVEQ #1,D0
			0x5280, // ADDQ.L #1,D0
			0x1080, // MOVE.B D0,(A0)
			0x4E71 // NOP
		};
		WriteWords(m68000Bus, CodeBase, program);
		WriteWords(m68010Bus, CodeBase, program);
		var m68000 = new M68kInterpreter(m68000Bus);
		var m68010 = new M68010Interpreter(m68010Bus);
		m68000.Reset(CodeBase, 0x3000);
		m68010.Reset(CodeBase, 0x3000);
		m68000.State.A[0] = 0x2000;
		m68010.State.A[0] = 0x2000;

		for (var i = 0; i < program.Length; i++)
		{
			m68000.ExecuteInstruction();
			m68010.ExecuteInstruction();
		}

		Assert.Equal(m68000.State.ProgramCounter, m68010.State.ProgramCounter);
		Assert.Equal(m68000.State.StatusRegister, m68010.State.StatusRegister);
		Assert.Equal(m68000.State.Cycles, m68010.State.Cycles);
		Assert.Equal(m68000.State.NativeCycles, m68010.State.NativeCycles);
		Assert.Equal(m68000.State.D, m68010.State.D);
		Assert.Equal(m68000.State.A, m68010.State.A);
		Assert.Equal(ReadByte(m68000Bus, 0x2000), ReadByte(m68010Bus, 0x2000));
		Assert.Equal(m68000Bus.InstructionFetchWords, m68010Bus.InstructionFetchWords);
	}

	[Fact]
	public void OddWordReadRaisesFormat8AddressErrorFrame()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3211); // MOVE.W (A1),D1
		bus.WriteLong(3u * 4u, 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 1;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
        Assert.Equal(0x2FC6u, cpu.State.A[7]);
        Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FC6));
        Assert.Equal(CodeBase, bus.ReadLong(0x2FC8));
        Assert.Equal(0x800Cu, bus.ReadWord(0x2FCC));
        Assert.Equal(0x1105, bus.ReadWord(0x2FCE));
        Assert.Equal(1u, bus.ReadLong(0x2FD0));
        Assert.Equal(0, bus.ReadWord(0x2FDE)); // Instruction input is unqualified.
	}

	[Fact]
	public void RejectsM68020OnlyExtbLong()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x49C0); // EXTB.L D0
		bus.WriteLong(4u * 4u, 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0080;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_0080u, cpu.State.D[0]);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(4 * 4, bus.ReadWord(0x2FFE));
	}

	private const uint CodeBase = 0x1000;
    [Theory]
    [InlineData(0x0E10, 0x1800, 0x123456ABu, 0xABu)]
    [InlineData(0x0E50, 0x1800, 0x1234FEDCu, 0xFEDCu)]
    [InlineData(0x0E90, 0x1800, 0x12345678u, 0x12345678u)]
    public void MovesWritesSizedOperandWithoutChangingCcr(int opcode, int extension, uint value, uint expected)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(CodeBase, (ushort)opcode, (ushort)extension);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68010, bus);
        cpu.Reset(CodeBase, 0x7000); cpu.State.StatusRegister = 0x271F;
        cpu.State.A[0] = 0x2000; cpu.State.D[1] = value;
        cpu.ExecuteInstruction();
        var actual = (opcode & 0xC0) switch { 0 => bus.Memory[0x2000], 0x40 => bus.ReadWord(0x2000), _ => bus.ReadLong(0x2000) };
        Assert.Equal(expected, actual); Assert.Equal(0x271F, cpu.State.StatusRegister);
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Fact]
    public void MovesWordSignExtendsAddressRegisterAndIsPrivileged()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(CodeBase, 0x0E50, 0x9000); bus.WriteWord(0x2000, 0xFEDC);
        bus.WriteLong(32, 0x4000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68010, bus);
        cpu.Reset(CodeBase, 0x7000); cpu.State.A[0] = 0x2000;
        cpu.ExecuteInstruction(); Assert.Equal(0xFFFFFEDCu, cpu.State.A[1]);
        cpu.Reset(CodeBase, 0x7000); cpu.State.A[0] = 0x2000; cpu.State.StatusRegister = 0;
        cpu.ExecuteInstruction(); Assert.Equal(0x4000u, cpu.State.ProgramCounter);
        Assert.Equal(8, cpu.State.LastExceptionVector); Assert.Equal(0x2000u, cpu.State.A[0]);
    }
}
