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
	public void JitDirectIlDivsByZeroVectorsAndCanReturnThroughRte()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x81FC, 0x0000, 0x60FA); // DIVS.W #0,D0; BRA.S back to DIVS
		WriteWords(bus, 0x2000, 0x4E73); // RTE
		bus.WriteLong(5 * 4, 0x0000_2000);
		var cpu = new M68kJitCore(bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		cpu.State.D[0] = 0x1234_5678;

		var executed = cpu.ExecuteInstructions(210, null, new CountingBoundary());

		Assert.Equal(210, executed);
		Assert.False(cpu.State.Halted);
		Assert.Equal(RealFastCodeBase, cpu.State.ProgramCounter);
		Assert.Equal(0x4000u, cpu.State.A[7]);
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
		Assert.True(cpu.Counters.DirectIlInstructions > 0);
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
	public void JitAndInterpreterAgreeForHotRegisterAndAddressForms()
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		var words = new ushort[]
		{
			0x303C, 0x0001,       // MOVE.W #1,D0
			0x2040,               // MOVEA.L D0,A0
			0xD1C0,               // ADDA.L D0,A0
			0xB1C0,               // CMPA.L D0,A0
			0x4840,               // SWAP D0
			0x4880,               // EXT.W D0
			0xE589,               // LSL.L #2,D1
			0xC141,               // EXG D0,D1
			0x60F0                // BRA.S loop
		};
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, null, new CountingBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
	}

	[Fact]
	public void JitCompiledHelperExecutesCmpm()
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		var words = new ushort[]
		{
			0x207C, 0x0000, 0x3000, // MOVEA.L #$3000,A0
			0x227C, 0x0000, 0x3100, // MOVEA.L #$3100,A1
			0xB308,                 // CMPM.B (A0)+,(A1)+
			0x60FC                  // BRA.S CMPM
		};
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.ChipRam[0x3000] = 0x20;
		interpreterBus.ChipRam[0x3000] = 0x20;
		jitBus.ChipRam[0x3100] = 0x20;
		interpreterBus.ChipRam[0x3100] = 0x20;
		var jit = new M68kJitCore(jitBus);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, null, new CountingBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ChipRam[0x3000..0x3104], jitBus.ChipRam[0x3000..0x3104]);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
	}

	[Fact]
	public void JitAndInterpreterAgreeForBsrAndRtsTrace()
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		var words = new ushort[]
		{
			0x6100, 0x0004, // BSR.W subroutine
			0x60FA,         // BRA.S BSR
			0x7203,         // MOVEQ #3,D1
			0x4E75          // RTS
		};
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, null, new CountingBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
	}

	[Fact]
	public void JitCompiledHelperExecutesMovem()
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		var words = new ushort[]
		{
			0x7001,       // MOVEQ #1,D0
			0x48E7, 0x8000, // MOVEM.L D0,-(A7)
			0x60FA        // BRA.S loop
		};
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(200, null, new CountingBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
	}

	[Fact]
	public void JitPureTraceBatchesRegisterOnlyMoveqAddqBraLoop()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x5280, 0x60FA); // MOVEQ #1,D0; ADDQ.L #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		cpu.ExecuteInstructions(220, 100_000, boundary);
		var batches = cpu.Counters.PureTraceBatchExecutions;
		var batchInstructions = cpu.Counters.PureTraceBatchInstructions;
		var batchAfterCount = boundary.BatchAfterCount;
		var perInstructionAfterCount = boundary.AfterCount;

		var executed = cpu.ExecuteInstructions(30, cpu.State.Cycles + 10_000, boundary);

		Assert.Equal(30, executed);
		Assert.True(cpu.Counters.PureTraceBatchExecutions > batches);
		Assert.True(cpu.Counters.PureTraceBatchInstructions > batchInstructions);
		Assert.True(cpu.Counters.PureTraceBatchBoundaryCallsSaved > 0);
		Assert.True(boundary.BatchAfterCount > batchAfterCount);
		Assert.True(boundary.AfterCount - perInstructionAfterCount <= 1);
	}

	[Fact]
	public void JitPureTraceBatchesDbccAndMatchesInterpreter()
	{
		var words = new ushort[]
		{
			0x7003,       // MOVEQ #3,D0
			0x7200,       // MOVEQ #0,D1
			0x5281,       // ADDQ.L #1,D1
			0x51C8, 0xFFFC, // DBF D0,ADDQ
			0x7003,       // MOVEQ #3,D0
			0x60F6        // BRA.S ADDQ
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, 100_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, 100_000, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.True(jit.Counters.PureTraceBatchInstructions > jit.Counters.PureTraceBatchExecutions);
	}

	[Fact]
	public void JitDoesNotPureBatchBusTouchingTrace()
	{
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x7001,                 // MOVEQ #1,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x60F4);                // BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		var executed = cpu.ExecuteInstructions(800, 200_000, boundary);

		Assert.Equal(800, executed);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.HelperIlInstructions > 0 || cpu.Counters.DirectMemoryIlInstructions > 0);
		Assert.Equal(0, cpu.Counters.PureTraceBatchExecutions);
		Assert.Equal(0, boundary.BatchBeginCount);
		Assert.True(boundary.AfterCount > 0);
	}

	[Fact]
	public void JitPureTraceBatchUsesBoundaryWakeCandidateAsTarget()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x5280, 0x60FA); // MOVEQ #1,D0; ADDQ.L #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		cpu.ExecuteInstructions(220, 100_000, boundary);
		var wakeCycle = cpu.State.Cycles + 8;
		boundary.WakeCycle = wakeCycle;
		boundary.FirstBatchTargetCycle = null;

		cpu.ExecuteInstructions(30, cpu.State.Cycles + 10_000, boundary);

		Assert.Equal(wakeCycle, boundary.FirstBatchTargetCycle);
		Assert.True(cpu.Counters.PureTraceBatchExecutions > 0);
	}

	[Fact]
	public void JitPureTraceSideExitForHostTrapDoesNotCallBatchEnd()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		cpu.ExecuteInstructions(220, 100_000, boundary);
		var batchAfterCount = boundary.BatchAfterCount;
		var sideExits = cpu.Counters.PureTraceBatchSideExits;
		bus.RegisterHostCallback(FastCodeBase, state => state.ProgramCounter = FastCodeBase + 2);
		cpu.State.ProgramCounter = FastCodeBase;

		cpu.ExecuteInstructions(2, cpu.State.Cycles + 10_000, boundary);

		Assert.True(cpu.Counters.PureTraceBatchSideExits > sideExits);
		Assert.Equal(batchAfterCount, boundary.BatchAfterCount);
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
	public void JitV2BatchesLoopWithDeferredFlagsAndLazyWriteback()
	{
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, 0x7001, 0x7002, 0x60FA); // MOVEQ #1,D0; MOVEQ #2,D0; BRA.S loop
		WriteWords(interpreterBus, FastCodeBase, 0x7001, 0x7002, 0x60FA);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		jit.ExecuteInstructions(220, 100_000, boundary);
		var materializations = jit.Counters.V2FlagMaterializations;
		var direct = jit.Counters.DirectCpuIlInstructions;

		var jitExecuted = jit.ExecuteInstructions(120, jit.State.Cycles + 100_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(340, null, new CountingBoundary());

		Assert.Equal(340, jitExecuted + 220);
		Assert.Equal(interpreterExecuted, jitExecuted + 220);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2LazyWritebacks > 0);
		Assert.True(jit.Counters.V2FlagMaterializations - materializations < jit.Counters.DirectCpuIlInstructions - direct);
	}

	[Fact]
	public void JitV2MaterializesFlagsForBccAndMatchesInterpreter()
	{
		var words = new ushort[]
		{
			0x7000, // MOVEQ #0,D0
			0x4A80, // TST.L D0
			0x6702, // BEQ.S ADDQ
			0x7201, // MOVEQ #1,D1
			0x5281, // ADDQ.L #1,D1
			0x60F4  // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(800, 100_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2FlagMaterializations > 0);
	}

	[Fact]
	public void JitV2FlushesLazyStateOnOutOfBlockBranch()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7005, 0x6000, 0x0100); // MOVEQ #5,D0; BRA.W outside v2 block
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		for (var i = 0; i < 80; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		cpu.State.ProgramCounter = FastCodeBase;
		var sideExits = cpu.Counters.V2SideExits;

		cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(0x0000_0005u, cpu.State.D[0]);
		Assert.Equal(FastCodeBase + 0x104, cpu.State.ProgramCounter);
		Assert.True(cpu.Counters.V2SideExits > sideExits);
		Assert.True(cpu.Counters.V2SideExitOutOfBlockBranch > 0);
		Assert.True(cpu.Counters.V2LazyWritebacks > 0);
	}

	[Fact]
	public void JitV2PromotesLoopTraceFromTier0ToTier1AfterHotHits()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		cpu.ExecuteInstructions(220, 100_000, boundary);

		for (var i = 0; i < 4096; i++)
		{
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.True(cpu.Counters.V2Tier0CompiledTraces > 0);
		Assert.True(cpu.Counters.V2Tier1CompiledTraces > 0);
		Assert.True(cpu.Counters.V2TierPromotions > 0);
	}

	[Fact]
	public void JitV2BlockGraphKeepsForwardConditionalBranchInternal()
	{
		var words = new ushort[]
		{
			0x7000, // MOVEQ #0,D0
			0x4A80, // TST.L D0
			0x6704, // BEQ.S target
			0x7201, // MOVEQ #1,D1
			0x60F6, // BRA.S start
			0x7202, // target: MOVEQ #2,D1
			0x60F2  // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		var warmup = jit.ExecuteInstructions(220, 100_000, boundary);
		var outOfBlockExits = jit.Counters.V2SideExitOutOfBlockBranch;
		var v2Hits = jit.Counters.V2TraceHits;

		var measured = jit.ExecuteInstructions(160, jit.State.Cycles + 100_000, boundary);
		var interpreted = interpreter.ExecuteInstructions(warmup + measured, null, new CountingBoundary());

		Assert.Equal(warmup + measured, interpreted);
		Assert.Equal(160, measured);
		Assert.True(jit.Counters.V2TraceHits > v2Hits);
		Assert.Equal(outOfBlockExits, jit.Counters.V2SideExitOutOfBlockBranch);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(0x0000_0002u, jit.State.D[1]);
	}

	[Fact]
	public void JitV2RecordsRejectedCandidateReasons()
	{
		var eaBus = CreateCodeBus();
		WriteWords(eaBus, FastCodeBase, 0x3010, 0x60FC); // MOVE.W (A0),D0; BRA.S loop
		var eaCpu = new M68kJitCore(eaBus, enableV2: true);
		eaCpu.Reset(FastCodeBase, 0x4000);
		eaCpu.State.A[0] = 0x2000;
		eaCpu.ExecuteInstructions(180, null, new CountingBoundary());

		var opBus = CreateCodeBus();
		WriteWords(opBus, FastCodeBase, 0x207C, 0x0000, 0x2000, 0x60F8); // MOVEA.L #$2000,A0; BRA.S loop
		var opCpu = new M68kJitCore(opBus, enableV2: true);
		opCpu.Reset(FastCodeBase, 0x4000);
		opCpu.ExecuteInstructions(180, null, new CountingBoundary());

		Assert.True(eaCpu.Counters.V2RejectedCandidates > 0);
		Assert.True(eaCpu.Counters.V2RejectedUnsupportedEa > 0);
		Assert.True(opCpu.Counters.V2RejectedCandidates > 0);
		Assert.True(opCpu.Counters.V2RejectedUnsupportedOperation > 0);
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

	private sealed class PureBatchBoundary : CountingBoundary, IM68kPureCpuTraceBatchBoundary
	{
		public int BatchBeginCount { get; private set; }

		public int BatchAfterCount { get; private set; }

		public int BatchInstructionCount { get; private set; }

		public long? WakeCycle { get; set; }

		public long? FirstBatchTargetCycle { get; set; }

		public bool TryBeginPureCpuTraceBatch(
			M68kCpuState state,
			long targetCycle,
			out long batchTargetCycle)
		{
			BatchBeginCount++;
			if (!BeforeInstruction())
			{
				batchTargetCycle = targetCycle;
				return false;
			}

			batchTargetCycle = WakeCycle.HasValue
				? Math.Clamp(WakeCycle.Value, state.Cycles + 1, targetCycle)
				: targetCycle;
			FirstBatchTargetCycle ??= batchTargetCycle;
			return batchTargetCycle > state.Cycles;
		}

		public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = currentCycle;
			BatchAfterCount++;
			BatchInstructionCount += instructionCount;
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
