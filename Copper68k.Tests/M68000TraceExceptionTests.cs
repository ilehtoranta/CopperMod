using Copper68k;

namespace Copper68k.Tests;

// Motorola MC68000 User's Manual, sections 6.2.3 and 6.3.8:
// sample T at instruction entry, stack post-instruction SR/PC, suppress trace
// on an aborted instruction, and process an instruction trap before tracing.
public sealed class M68000TraceExceptionTests
{
    private static (M68kInterpreter Cpu, ZeroWaitCodeBus Bus) Create(params ushort[] code)
        => CreateConfigured(true, code);

    private static (M68kInterpreter Cpu, ZeroWaitCodeBus Bus) CreateConfigured(bool planned, params ushort[] code)
    {
        var bus = new ZeroWaitCodeBus();
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, code);
        bus.WriteLong(9 * 4, 0x4000);
        M68kInterpreterTestHelpers.WriteWords(bus, 0x4000, 0x4E71, 0x4E71);
        var cpu = new M68kInterpreter(bus, new M68kCpuState(), enableOpcodePlan: planned);
        cpu.Reset(0x1000, 0x8000);
        cpu.State.StatusRegister = 0xA700;
        return (cpu, bus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NopStacksNextPcAndTraceSrThenClearsLiveTrace(bool planned)
    {
        var (cpu, bus) = CreateConfigured(planned, 0x4E71);
        using (cpu)
        {
            Assert.Equal(38, cpu.ExecuteInstruction()); // NOP 4 + trace 34 clocks.
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.Equal(0x7FFAu, cpu.State.A[7]);
            Assert.Equal(0xA700, bus.ReadWord(0x7FFA));
            Assert.Equal(0x1002u, bus.ReadLong(0x7FFC));
            Assert.Equal(0x2700, cpu.State.StatusRegister);
            cpu.ExecuteInstruction();
            Assert.Equal(0x4002u, cpu.State.ProgramCounter);
            Assert.Equal(0x7FFAu, cpu.State.A[7]);
        }
    }

    [Fact]
    public void EnablingTraceDoesNotTraceThatInstruction()
    {
        var (cpu, bus) = Create(0x007C, 0x8000, 0x4E71); // ORI #T,SR; NOP
        using (cpu)
        {
            cpu.State.StatusRegister = 0x2700;
            cpu.ExecuteInstruction();
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
            Assert.Equal(0x8000u, cpu.State.A[7]);
            cpu.ExecuteInstruction();
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.Equal(0x1006u, bus.ReadLong(0x7FFC));
        }
    }

    [Theory]
    [InlineData(0x027C, 0x2700)] // ANDI #$7FFF,SR
    [InlineData(0x4E72, 0x271F)] // STOP #$7FFF
    public void ClearingTraceDuringInstructionStillTracesAndWakesStop(int opcode, int savedSr)
    {
        var (cpu, bus) = Create((ushort)opcode, 0x7FFF);
        using (cpu)
        {
            cpu.ExecuteInstruction();
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.False(cpu.State.Stopped);
            Assert.Equal(savedSr, bus.ReadWord(0x7FFA));
            Assert.Equal(0x1004u, bus.ReadLong(0x7FFC));
        }
    }

    [Fact]
    public void RteEnablesTraceForReturnedInstructionAndUsesUserStackCorrectly()
    {
        var (cpu, bus) = Create(0x4E73);
        using (cpu)
        {
            cpu.State.ResetStackPointers(0x8000, 0x6000, supervisorMode: true);
            cpu.State.StatusRegister = 0x2700;
            bus.WriteWord(0x8000, 0x8010);
            bus.WriteLong(0x8002, 0x2000);
            M68kInterpreterTestHelpers.WriteWords(bus, 0x2000, 0x7000); // MOVEQ #0,D0
            cpu.ExecuteInstruction();
            Assert.Equal(0x2000u, cpu.State.ProgramCounter);
            Assert.Equal(0x6000u, cpu.State.A[7]);
            cpu.ExecuteInstruction();
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.Equal(0x6000u, cpu.State.UserStackPointer);
            Assert.Equal(0x8000u, cpu.State.A[7]);
            Assert.Equal(0x8014, bus.ReadWord(0x8000)); // updated Z, preserved X
            Assert.Equal(0x2002u, bus.ReadLong(0x8002));
        }
    }

    [Theory]
    [InlineData(0x4AFC, 4)]
    [InlineData(0xA000, 10)]
    [InlineData(0xF000, 11)]
    public void IllegalInstructionsSuppressTrace(int opcode, int vector)
    {
        var (cpu, bus) = Create((ushort)opcode);
        using (cpu)
        {
            bus.WriteLong((uint)(vector * 4), 0x5000);
            cpu.ExecuteInstruction();
            Assert.Equal(0x5000u, cpu.State.ProgramCounter);
            Assert.Equal(0x7FFAu, cpu.State.A[7]);
            Assert.Equal(0x1000u, bus.ReadLong(0x7FFC));
        }
    }

    [Fact]
    public void TrapIsStackedBeforeTraceWithoutExecutingItsHandler()
    {
        var (cpu, bus) = Create(0x4E40); // TRAP #0
        using (cpu)
        {
            bus.WriteLong(32 * 4, 0x5000);
            cpu.ExecuteInstruction();
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.Equal(0x7FF4u, cpu.State.A[7]);
            Assert.Equal(0x2700, bus.ReadWord(0x7FF4));
            Assert.Equal(0x5000u, bus.ReadLong(0x7FF6));
            Assert.Equal(0xA700, bus.ReadWord(0x7FFA));
            Assert.Equal(0x1002u, bus.ReadLong(0x7FFC));
        }
    }

    [Fact]
    public void PendingInterruptStacksAfterTraceBeforeEitherHandlerExecutes()
    {
        var (cpu, bus) = Create(0x4E71);
        using (cpu)
        {
            cpu.State.StatusRegister = 0xA000;
            bus.WriteLong(27 * 4, 0x5000);
            cpu.ExecuteInstruction();
            cpu.RequestInterrupt(3, 27 * 4);
            Assert.Equal(0x5000u, cpu.State.ProgramCounter);
            Assert.Equal(0x7FF4u, cpu.State.A[7]);
            Assert.Equal(0x2000, bus.ReadWord(0x7FF4));
            Assert.Equal(0x4000u, bus.ReadLong(0x7FF6));
            Assert.Equal(0xA000, bus.ReadWord(0x7FFA));
            Assert.Equal(0x1002u, bus.ReadLong(0x7FFC));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BatchingCannotSkipTraceBetweenInstructions(bool packed, bool windowless)
    {
        PlanTierTestBus bus = windowless ? new PlanTierWindowlessTestBus() : new PlanTierFastMemoryTestBus();
        bus.WriteWords(0x1000, 0x4E71, 0x4E71, 0x4E71, 0x60F8);
        bus.WriteLong(9 * 4, 0x4000);
        bus.WriteWords(0x4000, 0x702A, 0x4E71);
        using var cpu = new M68kInterpreter(bus, new M68kCpuState(), enableCpuBusPhaseTrace: false,
            opcodePlanDispatch: packed ? M68kOpcodePlanDispatch.PackedPlan : M68kOpcodePlanDispatch.KindTable);
        cpu.Reset(0x1000, 0x8000);
        cpu.State.StatusRegister = 0xA700;
        Assert.Equal(2, ((IM68kBatchCore)cpu).ExecuteInstructions(2, long.MaxValue, new PlanTierBatchBoundary()));
        Assert.Equal(42u, cpu.State.D[0]);
        Assert.Equal(0x4002u, cpu.State.ProgramCounter);
        Assert.Equal(0x7FFAu, cpu.State.A[7]);
    }
}
