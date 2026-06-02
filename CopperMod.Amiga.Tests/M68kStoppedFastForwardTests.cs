using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kStoppedFastForwardTests
{
	private const uint FastCodeBase = AmigaConstants.A500BootPseudoFastRamBase;

	[Fact]
	public void JitStoppedCpuFastForwardsToTargetAsOneLogicalInstruction()
	{
		var bus = new AmigaBus(expansionRamSize: 64 * 1024);
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
		var bus = new AmigaBus(expansionRamSize: 64 * 1024);
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
		var bus = new AmigaBus(expansionRamSize: 64 * 1024);
		bus.WriteLong(0x70, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.State.Stopped = true;
		var boundary = new InterruptingFastForwardBoundary(() => cpu.RequestInterrupt(4, 0x70));

		var executed = cpu.ExecuteInstructions(1, 50, boundary);

		Assert.Equal(1, executed);
		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3FFAu, cpu.State.A[7]);
		Assert.Equal(FastCodeBase, bus.ReadLong(0x3FFC));
		Assert.Equal(0x2000, bus.ReadWord(0x3FFA));
		Assert.Equal(104, cpu.State.Cycles);
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
