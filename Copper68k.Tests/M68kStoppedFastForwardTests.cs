using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kStoppedFastForwardTests
{
	private const uint FastCodeBase = 0x1000;

	[Fact]
	public void JitStoppedCpuFastForwardsToTargetAsOneLogicalInstruction()
	{
		var bus = new Copper68kTestBus();
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.State.Stopped = true;
		cpu.State.Cycles = 10;
		var boundary = new FastForwardBoundary();

		var executed = cpu.ExecuteInstructions(10, 100, boundary);

		Assert.Equal(1, executed);
		Assert.Equal(100, cpu.State.Cycles);
		Assert.Equal(FastCodeBase, cpu.State.ProgramCounter);
		Assert.Equal(1, boundary.FastForwardCount);
		Assert.Equal(1, boundary.BeforeCount);
		Assert.Equal(1, boundary.AfterCount);
		Assert.Equal(0, cpu.Counters.FallbackInstructions);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(0, cpu.Counters.TraceHits);
		Assert.Equal(0, cpu.Counters.BoundarySideExits);
		Assert.Equal(1, cpu.Counters.StoppedFastForwards);
		Assert.Equal(89, cpu.Counters.StoppedFastForwardCycles);
	}

	[Fact]
	public void InterpreterStoppedCpuFastForwardsToTargetAsOneLogicalInstruction()
	{
		var bus = new Copper68kTestBus();
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.State.Stopped = true;
		cpu.State.Cycles = 10;
		var boundary = new FastForwardBoundary();

		var executed = cpu.ExecuteInstructions(10, 100, boundary);

		Assert.Equal(1, executed);
		Assert.Equal(100, cpu.State.Cycles);
		Assert.Equal(FastCodeBase, cpu.State.ProgramCounter);
		Assert.Equal(1, boundary.FastForwardCount);
		Assert.Equal(1, boundary.BeforeCount);
		Assert.Equal(1, boundary.AfterCount);
	}

	[Fact]
	public void StoppedFastForwardBoundaryCanWakeCpuThroughNormalInterruptRequest()
	{
		var bus = new Copper68kTestBus();
		bus.WriteLong(0x70, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.State.Stopped = true;
		var boundary = new InterruptingFastForwardBoundary(() => cpu.RequestInterrupt(4, 0x70));

		var executed = cpu.ExecuteInstructions(1, 50, boundary);

		Assert.Equal(1, executed);
		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3FFAu, cpu.State.A[7]);
		Assert.Equal(FastCodeBase, bus.ReadLong(0x3FFC));
		Assert.Equal(0x2000, bus.ReadWord(0x3FFA));
		Assert.Equal(94, cpu.State.Cycles);
	}

	[Fact]
	public void StoppedCpuRecognizesAssertedInterruptWithoutInstructionRetirement()
	{
		var cpu = new M68kInterpreter(new Copper68kTestBus());
		cpu.Reset(FastCodeBase, 0x4000);
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(cpu);
		var pinAssertCycle = cpu.State.Cycles + 10;

		Assert.False(recognition.HasRecognizedInterrupt(pinAssertCycle));

		cpu.State.Stopped = true;

		Assert.True(recognition.HasRecognizedInterrupt(pinAssertCycle));
	}

	private class FastForwardBoundary : IM68kStoppedCpuFastForwardBoundary
	{
		public int BeforeCount { get; private set; }

		public int AfterCount { get; private set; }

		public int FastForwardCount { get; private set; }

		public virtual bool BeforeInstruction()
		{
			BeforeCount++;
			return true;
		}

		public virtual void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			_ = currentCycle;
			AfterCount++;
		}

		public virtual bool TryFastForwardStoppedInstruction(
			M68kCpuState state,
			long targetCycle,
			out long advancedCycles)
		{
			if (!BeforeInstruction())
			{
				advancedCycles = 0;
				return false;
			}

			FastForwardCount++;
			var previousCycle = state.Cycles;
			state.Cycles = targetCycle;
			advancedCycles = state.Cycles - previousCycle;
			AfterInstruction(previousCycle, state.Cycles);
			return true;
		}
	}

	private sealed class InterruptingFastForwardBoundary : FastForwardBoundary
	{
		private readonly Action _requestInterrupt;

		public InterruptingFastForwardBoundary(Action requestInterrupt)
		{
			_requestInterrupt = requestInterrupt;
		}

		public override void AfterInstruction(long previousCycle, long currentCycle)
		{
			base.AfterInstruction(previousCycle, currentCycle);
			_requestInterrupt();
		}
	}
}
