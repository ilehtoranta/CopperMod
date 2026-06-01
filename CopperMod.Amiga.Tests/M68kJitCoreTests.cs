using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kJitCoreTests
{
	[Fact]
	public void FactoryCreatesJitBackend()
	{
		var bus = new AmigaBus();

		var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.JitM68000, bus);

		Assert.IsType<M68kJitCore>(cpu);
	}

	[Fact]
	public void JitCompilesHotMoveqBranchTraceAndPreservesInstructionBoundaries()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x70, 0x01, 0x60, 0xFC); // MOVEQ #1,D0; BRA.S $1000
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);
		var boundary = new CountingBoundary();

		var executed = cpu.ExecuteInstructions(180, null, boundary);

		Assert.Equal(180, executed);
		Assert.Equal(180, boundary.AfterCount);
		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
		Assert.True(cpu.Counters.FallbackInstructions > 0);
	}

	[Fact]
	public void JitDispatchesInterruptsBetweenCompiledInstructions()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x70, 0x01, 0x60, 0xFC); // MOVEQ #1,D0; BRA.S $1000
		bus.WriteLong(0x0070, 0x0000_2000);
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		cpu.State.ProgramCounter = 0x1000;
		cpu.State.Cycles = 0;
		cpu.State.SetActiveStackPointer(0x4000);
		var boundary = new InterruptAfterFirstInstructionBoundary(cpu);

		var executed = cpu.ExecuteInstructions(4, null, boundary);

		Assert.Equal(1, executed);
		Assert.Equal(1, boundary.AfterCount);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3FFAu, cpu.State.A[7]);
		Assert.Equal(0x0000_1002u, bus.ReadLong(0x3FFC));
	}

	[Fact]
	public void JitFallsBackForLineAExceptionBehavior()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xA0, 0x00);
		bus.WriteLong(10 * 4, 0x0000_2000);
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3FFAu, cpu.State.A[7]);
		Assert.True(cpu.Counters.FallbackInstructions > 0);
	}

	[Fact]
	public void JitDoesNotCompileAcrossHostTrapAddresses()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1002, 0x60, 0xFC); // BRA.S $1000
		var hostHits = 0;
		bus.RegisterHostCallback(0x1000, state =>
		{
			hostHits++;
			state.ProgramCounter = 0x1002;
		});
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);

		cpu.ExecuteInstructions(180, null, new CountingBoundary());

		Assert.True(hostHits > 64);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.FallbackInstructions >= hostHits);
	}

	[Fact]
	public void JitInvalidatesTraceWhenSourceOpcodeChanges()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x70, 0x01, 0x60, 0xFC); // MOVEQ #1,D0; BRA.S $1000
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		Assert.True(cpu.Counters.CompiledTraces > 0);
		bus.WriteWord(0x1000, 0x7002);
		cpu.State.ProgramCounter = 0x1000;

		cpu.ExecuteInstructions(1, null, new CountingBoundary());

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.True(cpu.Counters.Invalidations > 0);
	}

	[Fact]
	public void JitCompilesGenericImmediateAndQuickTrace()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x7001,             // MOVEQ #1,D0
			0x5280,             // ADDQ.L #1,D0
			0x5380,             // SUBQ.L #1,D0
			0x0080, 0x0000, 0x0010, // ORI.L #$10,D0
			0x0280, 0x0000, 0x001F, // ANDI.L #$1F,D0
			0x0A80, 0x0000, 0x0001, // EORI.L #1,D0
			0x0C80, 0x0000, 0x0010, // CMPI.L #$10,D0
			0x60E0);            // BRA.S $1000
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);
		var boundary = new CountingBoundary();

		var executed = cpu.ExecuteInstructions(800, null, boundary);

		Assert.Equal(800, executed);
		Assert.Equal(800, boundary.AfterCount);
		Assert.Equal(0x0000_0010u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
	}

	[Fact]
	public void JitCompilesGenericMoveMemoryTrace()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x7012,                 // MOVEQ #$12,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x7034,                 // MOVEQ #$34,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x3210,                 // MOVE.W (A0),D1
			0x60E8);                // BRA.S $1000
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);

		var executed = cpu.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(800, executed);
		Assert.Equal(0x12, bus.ChipRam[0x2000]);
		Assert.Equal(0x34, bus.ChipRam[0x2001]);
		Assert.Equal(0x0000_1234u, cpu.State.D[1] & 0xFFFF);
		Assert.Equal(0x0000_2000u, cpu.State.A[0]);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
	}

	[Fact]
	public void JitInvalidatesTraceWhenSourceExtensionWordChanges()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x203C, 0x0000, 0x0001, // MOVE.L #1,D0
			0x60F8);                // BRA.S $1000
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		Assert.True(cpu.Counters.CompiledTraces > 0);

		bus.WriteWord(0x1004, 0x0002);
		cpu.State.ProgramCounter = 0x1000;
		cpu.ExecuteInstructions(1, null, new CountingBoundary());

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.True(cpu.Counters.Invalidations > 0);
	}

	[Theory]
	[MemberData(nameof(RepresentativeGenericTracePrograms))]
	public void JitAndInterpreterAgreeForRepresentativeGenericTraces(ushort[] words)
	{
		var interpreterBus = new AmigaBus();
		var jitBus = new AmigaBus();
		WriteWords(interpreterBus, 0x1000, words);
		WriteWords(jitBus, 0x1000, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(0x1000, 0x4000);
		jit.Reset(0x1000, 0x4000);

		var interpreted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ChipRam[0x2000..0x2008], jitBus.ChipRam[0x2000..0x2008]);
	}

	public static IEnumerable<object[]> RepresentativeGenericTracePrograms()
	{
		yield return new object[]
		{
			new ushort[]
			{
				0x7001,
				0x5280,
				0x5380,
				0x0080, 0x0000, 0x0010,
				0x0280, 0x0000, 0x001F,
				0x0A80, 0x0000, 0x0001,
				0x0C80, 0x0000, 0x0010,
				0x60E0
			}
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x207C, 0x0000, 0x2000,
				0x7012,
				0x10C0,
				0x7034,
				0x10C0,
				0x207C, 0x0000, 0x2000,
				0x3210,
				0x60E8
			}
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x70FF,
				0x4880,
				0x48C0,
				0x4840,
				0x7201,
				0xC141,
				0x60F2
			}
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x41FA, 0x0004,
				0x7001,
				0xB1C0,
				0x51C8, 0xFFFC,
				0x60F2
			}
		};
	}

	private static void Write(byte[] memory, int address, params byte[] bytes)
	{
		Array.Copy(bytes, 0, memory, address, bytes.Length);
	}

	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	private class CountingBoundary : IM68kInstructionBoundary
	{
		public int BeforeCount { get; private set; }

		public int AfterCount { get; private set; }

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
	}

	private sealed class InterruptAfterFirstInstructionBoundary : CountingBoundary
	{
		private readonly M68kJitCore _cpu;
		private bool _stop;

		public InterruptAfterFirstInstructionBoundary(M68kJitCore cpu)
		{
			_cpu = cpu;
		}

		public override bool BeforeInstruction()
		{
			return !_stop && base.BeforeInstruction();
		}

		public override void AfterInstruction(long previousCycle, long currentCycle)
		{
			base.AfterInstruction(previousCycle, currentCycle);
			_cpu.RequestInterrupt(4, 0x70);
			_stop = true;
		}
	}
}
