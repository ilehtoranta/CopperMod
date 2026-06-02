using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kJitCoreTests
{
	private const uint FastCodeBase = AmigaConstants.A500BootPseudoFastRamBase;
	private const uint RealFastCodeBase = AmigaConstants.A500RealFastRamBase;
	private const int FastCodeSize = 64 * 1024;

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
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
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
	public void JitDoesNotCompileChipRamTrace()
	{
		var bus = new AmigaBus();
		WriteWords(bus, 0x1000, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(0x1000, 0x4000);

		var executed = cpu.ExecuteInstructions(180, null, new CountingBoundary());

		Assert.Equal(180, executed);
		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(0, cpu.Counters.TraceHits);
		Assert.Equal(180, cpu.Counters.FallbackInstructions);
	}

	[Fact]
	public void JitDoesNotTraceStoppedCpuIdleTicks()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x4E71); // NOP at the stopped PC; it must not become a hot root.
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.State.Stopped = true;
		var boundary = new CountingBoundary();

		var executed = cpu.ExecuteInstructions(180, null, boundary);

		Assert.Equal(180, executed);
		Assert.Equal(180, cpu.State.Cycles);
		Assert.Equal(FastCodeBase, cpu.State.ProgramCounter);
		Assert.Equal(180, boundary.AfterCount);
		Assert.Equal(180, cpu.Counters.FallbackInstructions);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(0, cpu.Counters.TraceHits);
		Assert.Equal(0, cpu.Counters.BoundarySideExits);
	}

	[Fact]
	public void JitDispatchesInterruptsBetweenCompiledInstructions()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		bus.WriteLong(0x0070, 0x0000_2000);
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		cpu.State.ProgramCounter = FastCodeBase;
		cpu.State.Cycles = 0;
		cpu.State.SetActiveStackPointer(0x4000);
		var boundary = new InterruptAfterFirstInstructionBoundary(cpu);

		var executed = cpu.ExecuteInstructions(4, null, boundary);

		Assert.Equal(1, executed);
		Assert.Equal(1, boundary.AfterCount);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3FFAu, cpu.State.A[7]);
		Assert.Equal(FastCodeBase + 2, bus.ReadLong(0x3FFC));
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
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase + 2, 0x60FC); // BRA.S trap
		var hostHits = 0;
		bus.RegisterHostCallback(FastCodeBase, state =>
		{
			hostHits++;
			state.ProgramCounter = FastCodeBase + 2;
		});
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);

		cpu.ExecuteInstructions(180, null, new CountingBoundary());

		Assert.True(hostHits > 64);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.FallbackInstructions >= hostHits);
	}

	[Fact]
	public void JitInvalidatesTraceWhenSourceOpcodeChanges()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		Assert.True(cpu.Counters.CompiledTraces > 0);
		bus.WriteWord(FastCodeBase, 0x7002);
		cpu.State.ProgramCounter = FastCodeBase;

		cpu.ExecuteInstructions(1, null, new CountingBoundary());

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.True(cpu.Counters.Invalidations > 0);
	}

	[Fact]
	public void JitKeepsTraceWhenSamePageDataOutsideTraceChanges()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
		var invalidations = cpu.Counters.Invalidations;
		var traceHits = cpu.Counters.TraceHits;

		bus.WriteWord(FastCodeBase + 0x80, 0x4E71);
		cpu.State.ProgramCounter = FastCodeBase;
		cpu.ExecuteInstructions(6, null, new CountingBoundary());

		Assert.Equal(invalidations, cpu.Counters.Invalidations);
		Assert.True(cpu.Counters.TraceHits > traceHits);
	}

	[Fact]
	public void JitDirectIlExecutesMoveqAndBranchTrace()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);

		cpu.ExecuteInstructions(220, null, new CountingBoundary());

		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
		Assert.True(cpu.Counters.DirectIlInstructions > 0);
		Assert.Equal(0, cpu.Counters.HelperIlInstructions);
	}

	[Fact]
	public void JitDirectIlExecutesDataRegisterArithmeticCompareAndNegNot()
	{
		AssertDirectIlMatchesInterpreter(
			1200,
			0x7001,                 // MOVEQ #1,D0
			0x2200,                 // MOVE.L D0,D1
			0x5281,                 // ADDQ.L #1,D1
			0x5381,                 // SUBQ.L #1,D1
			0x4A81,                 // TST.L D1
			0x0C81, 0x0000, 0x0001, // CMPI.L #1,D1
			0xB081,                 // CMP.L D1,D0
			0x4441,                 // NEG.W D1
			0x4641,                 // NOT.W D1
			0x60E8);                // BRA.S loop
	}

	[Fact]
	public void JitDirectIlExecutesDbccTrace()
	{
		AssertDirectIlMatchesInterpreter(
			1200,
			0x7003,       // MOVEQ #3,D0
			0x7200,       // MOVEQ #0,D1
			0x5281,       // ADDQ.L #1,D1
			0x51C8, 0xFFFC, // DBF D0,ADDQ
			0x7003,       // MOVEQ #3,D0
			0x60F6);      // BRA.S ADDQ
	}

	[Fact]
	public void JitGenerationGuardExitsWhenTraceBytesChangeWithoutWriteNotification()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.ExecuteInstructions(220, null, new CountingBoundary());
		Assert.True(cpu.Counters.TraceHits > 0);
		var generationGuardExits = cpu.Counters.GenerationGuardExits;
		var generationMismatches = cpu.Counters.GenerationMismatches;

		bus.ExpansionRam[0] = 0x70;
		bus.ExpansionRam[1] = 0x02;
		var touchCodePage = typeof(AmigaBus).GetMethod(
			"TouchCodePage",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		touchCodePage.Invoke(bus, new object[] { FastCodeBase });
		cpu.State.ProgramCounter = FastCodeBase;

		cpu.ExecuteInstructions(1, null, new CountingBoundary());

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.True(cpu.Counters.GenerationGuardExits > generationGuardExits);
		Assert.True(cpu.Counters.GenerationMismatches > generationMismatches);
	}

	[Fact]
	public void JitCompilesGenericImmediateAndQuickTrace()
	{
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x7001,             // MOVEQ #1,D0
			0x5280,             // ADDQ.L #1,D0
			0x5380,             // SUBQ.L #1,D0
			0x0080, 0x0000, 0x0010, // ORI.L #$10,D0
			0x0280, 0x0000, 0x001F, // ANDI.L #$1F,D0
			0x0A80, 0x0000, 0x0001, // EORI.L #1,D0
			0x0C80, 0x0000, 0x0010, // CMPI.L #$10,D0
			0x60E0);            // BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
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
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x7012,                 // MOVEQ #$12,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x7034,                 // MOVEQ #$34,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x3210,                 // MOVE.W (A0),D1
			0x60E8);                // BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);

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
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x203C, 0x0000, 0x0001, // MOVE.L #1,D0
			0x60F8);                // BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.ExecuteInstructions(180, null, new CountingBoundary());
		Assert.True(cpu.Counters.CompiledTraces > 0);

		bus.WriteWord(FastCodeBase + 4, 0x0002);
		cpu.State.ProgramCounter = FastCodeBase;
		cpu.ExecuteInstructions(1, null, new CountingBoundary());

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.True(cpu.Counters.Invalidations > 0);
	}

	[Theory]
	[MemberData(nameof(RepresentativeGenericTracePrograms))]
	public void JitAndInterpreterAgreeForRepresentativeGenericTraces(ushort[] words)
	{
		var interpreterBus = CreateCodeBus();
		var jitBus = CreateCodeBus();
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);

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
		yield return new object[]
		{
			new ushort[]
			{
				0x303C, 0x0001,       // MOVE.W #1,D0
				0x4440,               // NEG.W D0
				0x4640,               // NOT.W D0
				0x4A40,               // TST.W D0
				0x60F6
			}
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x203C, 0x0000, 0x0006, // MOVE.L #6,D0
				0x323C, 0x0003,         // MOVE.W #3,D1
				0x80C1,                 // DIVU.W D1,D0
				0xC0C1,                 // MULU.W D1,D0
				0x60F0
			}
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x203C, 0xFFFF, 0xFFFA, // MOVE.L #-6,D0
				0x323C, 0xFFFD,         // MOVE.W #-3,D1
				0x81C1,                 // DIVS.W D1,D0
				0xC1C1,                 // MULS.W D1,D0
				0x60F0
			}
		};
	}

	private static AmigaBus CreateCodeBus()
		=> new AmigaBus(expansionRamSize: FastCodeSize);

	private static AmigaBus CreateRealFastCodeBus()
		=> new AmigaBus(realFastRamSize: FastCodeSize);

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

	private static void AssertDirectIlMatchesInterpreter(int instructions, params ushort[] words)
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(instructions, null, new CountingBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(instructions, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.TraceHits > 0);
		Assert.True(jit.Counters.DirectIlInstructions > 0);
		Assert.Equal(0, jit.Counters.HelperIlInstructions);
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
