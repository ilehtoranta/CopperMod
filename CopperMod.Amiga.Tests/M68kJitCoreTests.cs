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
	public void JitCodeSnapshotOnlyCapturesJitEligibleMemory()
	{
		var realFastBus = CreateRealFastCodeBus();
		WriteWords(realFastBus, RealFastCodeBase, 0x7001, 0x5280, 0x60FC);
		Assert.True(realFastBus.TryCaptureJitCodeSnapshot(RealFastCodeBase, 16, out var realFastSnapshot));
		Assert.False(realFastSnapshot.IsEmpty);

		var pseudoFastBus = CreateCodeBus();
		WriteWords(pseudoFastBus, FastCodeBase, 0x7001, 0x5280, 0x60FC);
		Assert.True(pseudoFastBus.TryCaptureJitCodeSnapshot(FastCodeBase, 16, out var pseudoFastSnapshot));
		Assert.False(pseudoFastSnapshot.IsEmpty);

		var chipBus = new AmigaBus();
		WriteWords(chipBus, 0x1000, 0x7001, 0x5280, 0x60FC);
		Assert.False(chipBus.TryCaptureJitCodeSnapshot(0x1000, 16, out _));
	}

	[Fact]
	public void JitCodeSnapshotReaderDecodesCapturedBytesWithoutLiveBus()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x7001, 0x5280, 0x60FC);
		Assert.True(bus.TryCaptureJitCodeSnapshot(RealFastCodeBase, 16, out var snapshot));
		WriteWords(bus, RealFastCodeBase, 0x7002, 0x60FC);
		var reader = new M68kSnapshotCodeReader(snapshot);

		Assert.True(M68kDecoder.TryDecode(reader, RealFastCodeBase, out var instruction, out var reason));

		Assert.Equal(M68kJitBailoutReason.None, reason);
		Assert.Equal(0x7001, instruction.Opcode);
		Assert.Equal(1, instruction.QuickValue);
	}

	[Fact]
	public void JitCodeSnapshotReaderRejectsCapturedHostTrap()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x7001, 0x5280, 0x60FC);
		bus.RegisterHostCallback(RealFastCodeBase, _ => { });
		Assert.True(bus.TryCaptureJitCodeSnapshot(RealFastCodeBase, 16, out var snapshot));
		var reader = new M68kSnapshotCodeReader(snapshot);

		Assert.False(M68kDecoder.TryDecode(reader, RealFastCodeBase, out _, out var reason));

		Assert.Equal(M68kJitBailoutReason.HostTrap, reason);
	}

	[Fact]
	public void JitAsyncHotRootCompilesAndInstallsWithoutSynchronousFallback()
	{
		var previousAsync = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC");
		var previousWorkers = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS");
		var previousSyncFallback = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK");
		try
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", null);
			var words = new ushort[]
			{
				0x7001, // MOVEQ #1,D0
				0x5280, // ADDQ.L #1,D0
				0x60FA  // BRA.S start
			};
			var bus = CreateRealFastCodeBus();
			WriteWords(bus, RealFastCodeBase, words);
			using var jit = new M68kJitCore(bus, enableV2: true);
			jit.Reset(RealFastCodeBase, 0x4000);
			var boundary = new PureBatchBoundary();

			for (var i = 0; i < 200 && jit.Counters.AsyncCompletedInstalled == 0; i++)
			{
				jit.ExecuteInstructions(64, jit.State.Cycles + 100_000, boundary);
				Thread.Sleep(1);
			}

			var counters = jit.Counters;
			Assert.True(counters.AsyncRequestsQueued > 0);
			Assert.True(counters.AsyncWorkerCompilesCompleted > 0);
			Assert.True(counters.AsyncCompletedInstalled > 0);
			Assert.True(counters.CompiledTraces > 0);
			Assert.True(counters.V2Tier0CompiledTraces + counters.V2Tier1CompiledTraces > 0);
			Assert.True(counters.V2WorkerExpandedGraphs > 0);
		}
		finally
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", previousAsync);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", previousWorkers);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", previousSyncFallback);
		}
	}

	[Fact]
	public void JitZeroWaitReadHelperOnlyAcceptsRomOrRealFastMemory()
	{
		var realFastBus = CreateRealFastCodeBus();
		realFastBus.WriteLong(RealFastCodeBase, 0x1234_5678);
		Assert.True(realFastBus.TryReadJitZeroWaitMemory(RealFastCodeBase, M68kOperandSize.Long, out var realFastValue));
		Assert.Equal(0x1234_5678u, realFastValue);

		var pseudoFastBus = CreateCodeBus();
		pseudoFastBus.WriteLong(FastCodeBase, 0x89AB_CDEF);
		Assert.False(pseudoFastBus.TryReadJitZeroWaitMemory(FastCodeBase, M68kOperandSize.Long, out _));

		var chipBus = new AmigaBus();
		chipBus.WriteLong(0x2000, 0xAABB_CCDD);
		Assert.False(chipBus.TryReadJitZeroWaitMemory(0x2000, M68kOperandSize.Long, out _));
		Assert.False(realFastBus.TryReadJitZeroWaitMemory(RealFastCodeBase + 1, M68kOperandSize.Word, out _));
	}

	[Fact]
	public void JitZeroWaitWriteHelperOnlyAcceptsRealFastMemoryAndNotifiesInvalidation()
	{
		var realFastBus = CreateRealFastCodeBus();
		var notifiedAddress = 0u;
		var notifiedBytes = 0;
		realFastBus.JitEligibleMemoryWritten += (address, byteCount) =>
		{
			notifiedAddress = address;
			notifiedBytes = byteCount;
		};

		Assert.True(realFastBus.TryWriteJitZeroWaitMemory(RealFastCodeBase, 0x1234_5678, M68kOperandSize.Long));
		Assert.Equal(0x1234_5678u, realFastBus.ReadLong(RealFastCodeBase));
		Assert.Equal(RealFastCodeBase, notifiedAddress);
		Assert.Equal(4, notifiedBytes);
		Assert.False(realFastBus.TryWriteJitZeroWaitMemory(RealFastCodeBase + 1, 0xAAAA, M68kOperandSize.Word));

		var pseudoFastBus = CreateCodeBus();
		pseudoFastBus.WriteLong(FastCodeBase, 0x89AB_CDEF);
		Assert.False(pseudoFastBus.TryWriteJitZeroWaitMemory(FastCodeBase, 0x1111_2222, M68kOperandSize.Long));
		Assert.Equal(0x89AB_CDEFu, pseudoFastBus.ReadLong(FastCodeBase));

		var chipBus = new AmigaBus();
		chipBus.WriteLong(0x2000, 0xAABB_CCDD);
		Assert.False(chipBus.TryWriteJitZeroWaitMemory(0x2000, 0x1111_2222, M68kOperandSize.Long));
		Assert.Equal(0xAABB_CCDDu, chipBus.ReadLong(0x2000));
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
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
	}

	[Fact]
	public void JitCompiledDirectMemoryExecutesCmpm()
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
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ChipRam[0x3000..0x3104], jitBus.ChipRam[0x3000..0x3104]);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
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
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
	}

	[Fact]
	public void JitCompiledSpecializedHelperExecutesMovem()
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
		Assert.True(jit.Counters.SpecializedHelperIlInstructions > 0);
	}

	[Fact]
	public void JitV2CompilesImmediateStatusInstructions()
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		var words = new ushort[]
		{
			0x003C, 0x0011, // ORI #$0011,CCR
			0x023C, 0xFFFE, // ANDI #$FFFE,CCR
			0x0A3C, 0x0001, // EORI #$0001,CCR
			0x007C, 0x0700, // ORI #$0700,SR
			0x027C, 0xF8FF, // ANDI #$F8FF,SR
			0x0A7C, 0x0100, // EORI #$0100,SR
			0x44FC, 0x001F, // MOVE #$001F,CCR
			0x46FC, 0x2700, // MOVE #$2700,SR
			0x60DE          // BRA.S loop
		};
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, jit.State.Cycles + 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.Equal(0, jit.Counters.SystemInstructionBailouts);
		Assert.Equal(0, jit.Counters.UnsupportedOpcode);
	}

	[Fact]
	public void JitV2CompilesPeaAddressForms()
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		var words = new ushort[]
		{
			0x207C, 0x0000, 0x3000, // MOVEA.L #$3000,A0
			0x4878, 0x1234,         // PEA $1234.W
			0x4868, 0x0008,         // PEA 8(A0)
			0x60F0                  // BRA.S loop
		};
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(800, jit.State.Cycles + 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ChipRam[0x3000..0x4000], jitBus.ChipRam[0x3000..0x4000]);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.Equal(0, jit.Counters.V2RejectedDecode);
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

		boundary.WakeSource = M68kTraceBatchWakeSource.Blitter;
		var executed = cpu.ExecuteInstructions(30, cpu.State.Cycles + 10_000, boundary);

		Assert.Equal(30, executed);
		Assert.True(cpu.Counters.PureTraceBatchExecutions > batches);
		Assert.True(cpu.Counters.PureTraceBatchInstructions > batchInstructions);
		Assert.True(cpu.Counters.PureTraceBatchBoundaryCallsSaved > 0);
		Assert.True(boundary.BatchAfterCount > batchAfterCount);
		Assert.True(boundary.AfterCount - perInstructionAfterCount <= 1);
		Assert.Contains("3-4", cpu.Counters.PureTraceBatchLengthHistogram ?? string.Empty);
		Assert.Contains("blitter", cpu.Counters.PureTraceBatchWakeSourceTop ?? string.Empty);
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
	public void JitV2TraceHandoffDiagnosticsRecordTargetCycleClamp()
	{
		var rootA = FastCodeBase;
		var rootB = FastCodeBase + 0x200;
		var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, rootA, 0x7001, 0x6000, branchAToB); // MOVEQ #1,D0; BRA.W rootB
		WriteWords(jitBus, rootB, 0x5280, 0x60FC); // ADDQ.L #1,D0; BRA.S rootB
		WriteWords(interpreterBus, rootA, 0x7001, 0x6000, branchAToB);
		WriteWords(interpreterBus, rootB, 0x5280, 0x60FC);
		var cpu = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		cpu.Reset(rootA, 0x4000);
		interpreter.Reset(rootA, 0x4000);

		for (var i = 0; i < 80; i++)
		{
			cpu.State.ProgramCounter = rootA;
			_ = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, new PureBatchBoundary());
			cpu.State.ProgramCounter = rootB;
			_ = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, new PureBatchBoundary());
		}

		var probeStartCycles = interpreter.State.Cycles;
		Assert.Equal(2, interpreter.ExecuteInstructions(2, null, new CountingBoundary()));
		var branchEndDelta = interpreter.State.Cycles - probeStartCycles;
		var boundary = new PureBatchBoundary();
		var wakeCycle = cpu.State.Cycles + branchEndDelta;
		boundary.WakeCycle = wakeCycle;
		cpu.State.ProgramCounter = rootA;

		var executed = cpu.ExecuteInstructions(20, wakeCycle, boundary);

		Assert.InRange(executed, 1, 3);
		Assert.Contains("target-cycle", cpu.Counters.V2TraceHandoffBlockTop ?? string.Empty);
		Assert.DoesNotContain("target-cycle", cpu.Counters.V2TraceHandoffFailureTop ?? string.Empty);
	}

	[Fact]
	public void JitV2QueuesSupportedColdHandoffTargetForAsyncInstall()
	{
		var previousAsync = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC");
		var previousWorkers = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS");
		var previousSyncFallback = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK");
		try
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", null);
			var rootA = FastCodeBase;
			var rootB = FastCodeBase + 0x200;
			var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
			var jitBus = CreateCodeBus();
			var interpreterBus = CreateCodeBus();
			WriteWords(jitBus, rootA, 0x7001, 0x6000, branchAToB); // MOVEQ #1,D0; BRA.W rootB
			WriteWords(jitBus, rootB, 0x5280, 0x60FC); // ADDQ.L #1,D0; BRA.S rootB
			WriteWords(interpreterBus, rootA, 0x7001, 0x6000, branchAToB);
			WriteWords(interpreterBus, rootB, 0x5280, 0x60FC);
			using var cpu = new M68kJitCore(jitBus, enableV2: true);
			var interpreter = new M68kInterpreter(interpreterBus);
			cpu.Reset(rootA, 0x4000);
			interpreter.Reset(rootA, 0x4000);
			var boundary = new PureBatchBoundary();
			for (var i = 0; i < 200 && cpu.Counters.CompiledTraces == 0; i++)
			{
				cpu.State.ProgramCounter = rootA;
				_ = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
				Thread.Sleep(1);
			}

			Assert.True(cpu.Counters.CompiledTraces > 0);
			var queuedBefore = cpu.Counters.AsyncRequestsQueued;
			cpu.State.ProgramCounter = rootA;
			_ = cpu.ExecuteInstructions(20, cpu.State.Cycles + 100_000, boundary);

			Assert.True(cpu.Counters.V2TraceHandoffAttempts > 0);
			Assert.True(
				cpu.Counters.AsyncCompletedInstalled >= 2 ||
				cpu.Counters.AsyncRequestsQueued > queuedBefore ||
				cpu.Counters.AsyncRequestsDeduped > 0);

			for (var i = 0; i < 200 && cpu.Counters.AsyncCompletedInstalled < 2; i++)
			{
				_ = cpu.ExecuteInstructions(4, cpu.State.Cycles + 100_000, boundary);
				Thread.Sleep(1);
			}

			var handoffExecutions = cpu.Counters.V2TraceHandoffExecutions;
			cpu.State.ProgramCounter = rootA;

			var jitExecuted = cpu.ExecuteInstructions(20, cpu.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(20, null, new CountingBoundary());

			Assert.Equal(interpreterExecuted, jitExecuted);
			Assert.Equal(interpreter.State.ProgramCounter, cpu.State.ProgramCounter);
			Assert.Equal(interpreter.State.StatusRegister, cpu.State.StatusRegister);
			Assert.Equal(interpreter.State.D, cpu.State.D);
			Assert.True(cpu.Counters.AsyncCompletedInstalled >= 2);
			Assert.True(cpu.Counters.V2TraceHandoffExecutions > handoffExecutions);
		}
		finally
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", previousAsync);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", previousWorkers);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", previousSyncFallback);
		}
	}

	[Fact]
	public void JitV2PrequeuesExternalHandoffTargetWhenTraceInstalls()
	{
		var previousAsync = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC");
		var previousWorkers = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS");
		var previousSyncFallback = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK");
		try
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", null);
			var rootA = FastCodeBase;
			var rootB = FastCodeBase + 0x200;
			var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
			var bus = CreateCodeBus();
			WriteWords(bus, rootA, 0x7001, 0x6000, branchAToB); // MOVEQ #1,D0; BRA.W rootB
			WriteWords(bus, rootB, 0x5280, 0x60FC); // ADDQ.L #1,D0; BRA.S rootB
			using var cpu = new M68kJitCore(bus, enableV2: true);
			cpu.Reset(rootA, 0x4000);
			var boundary = new PureBatchBoundary();

			for (var i = 0; i < 500 && cpu.Counters.AsyncCompletedInstalled < 2; i++)
			{
				cpu.State.ProgramCounter = rootA;
				_ = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
				Thread.Sleep(1);
			}

			Assert.True(cpu.Counters.AsyncRequestsQueued >= 2 || cpu.Counters.AsyncRequestsDeduped > 0);
			Assert.True(cpu.Counters.AsyncCompletedInstalled >= 2);

			var fallback = cpu.Counters.FallbackInstructions;
			var v2Hits = cpu.Counters.V2TraceHits;
			cpu.State.ProgramCounter = rootB;

			var executed = cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);

			Assert.Equal(2, executed);
			Assert.Equal(fallback, cpu.Counters.FallbackInstructions);
			Assert.True(cpu.Counters.V2TraceHits > v2Hits);
		}
		finally
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", previousAsync);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", previousWorkers);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", previousSyncFallback);
		}
	}

	[Fact]
	public void JitV2AsyncHandoffTargetInstallsV2OnlyTraceAndStillHandoffs()
	{
		var previousAsync = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC");
		var previousWorkers = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS");
		var previousSyncFallback = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK");
		try
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", null);
			var rootA = FastCodeBase;
			var rootB = FastCodeBase + 0x200;
			var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
			var bus = CreateCodeBus();
			WriteWords(bus, rootA, 0x7001, 0x6000, branchAToB); // MOVEQ #1,D0; BRA.W rootB
			WriteWords(bus, rootB, 0x5280, 0x60FC); // ADDQ.L #1,D0; BRA.S rootB
			using var cpu = new M68kJitCore(bus, enableV2: true);
			cpu.Reset(rootA, 0x4000);
			var boundary = new PureBatchBoundary();

			for (var i = 0; i < 500 && cpu.Counters.AsyncCompletedInstalled < 2; i++)
			{
				cpu.State.ProgramCounter = rootA;
				_ = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
				Thread.Sleep(1);
				_ = cpu.ExecuteInstructions(0, cpu.State.Cycles + 100_000, boundary);
			}

			Assert.True(cpu.Counters.AsyncCompletedInstalled >= 2);
			Assert.NotNull(GetInstalledTraceDelegate(cpu, rootA, "Compiled"));
			Assert.Null(GetInstalledTraceDelegate(cpu, rootB, "Compiled"));
			Assert.NotNull(GetInstalledTraceDelegate(cpu, rootB, "V2Compiled"));

			var fallback = cpu.Counters.FallbackInstructions;
			var batchAfterCount = boundary.BatchAfterCount;
			cpu.State.ProgramCounter = rootB;

			var singleExecuted = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);

			Assert.Equal(1, singleExecuted);
			Assert.Equal(fallback, cpu.Counters.FallbackInstructions);
			Assert.True(boundary.BatchAfterCount > batchAfterCount);

			var handoffExecutions = cpu.Counters.V2TraceHandoffExecutions;
			var handoffInstructions = cpu.Counters.V2TraceHandoffInstructions;
			cpu.State.ProgramCounter = rootA;

			var executed = cpu.ExecuteInstructions(20, cpu.State.Cycles + 100_000, boundary);

			Assert.True(executed > 2);
			Assert.Equal(fallback, cpu.Counters.FallbackInstructions);
			Assert.True(cpu.Counters.V2TraceHandoffExecutions > handoffExecutions);
			Assert.True(cpu.Counters.V2TraceHandoffInstructions > handoffInstructions);
		}
		finally
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", previousAsync);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", previousWorkers);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", previousSyncFallback);
		}
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
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

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
		WriteWords(bus, FastCodeBase + 0x104, 0xB348); // unsupported v2 target keeps this as a side-exit test
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
		Assert.True(cpu.Counters.V2SideExitBeyondGraph > 0);
		Assert.True(cpu.Counters.V2LazyWritebacks > 0);
	}

	[Fact]
	public void JitV2PromotesLoopTraceFromTier0ToTier1AfterHotHits()
	{
		var bus = CreateCodeBus();
		var words = new ushort[90];
		words[0] = 0x7001; // MOVEQ #1,D0
		words[1] = 0x66FC; // BNE.S loop, with cold fallthrough graph after it
		Array.Fill<ushort>(words, 0x4E71, 2, words.Length - 2);
		WriteWords(bus, FastCodeBase, words);
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
	public void JitV2PromotesBranchExitHeavyTraceFromPressure()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7005, 0x6000, 0x0100); // MOVEQ #5,D0; BRA.W outside tier0 graph
		WriteWords(bus, FastCodeBase + 0x104, 0xB348); // unsupported v2 target keeps branch-exit pressure observable
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 700; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.True(cpu.Counters.V2SideExitOutOfBlockBranch > 0);
		Assert.True(cpu.Counters.V2Tier1CompiledTraces > 0);
		Assert.True(cpu.Counters.V2TierPressurePromotions > 0);
	}

	[Fact]
	public void JitV2Tier3RequiresOptInAndPromotesFromBranchPressure()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7005, 0x6000, 0x0180); // MOVEQ #5,D0; BRA.W outside tier2 graph
		WriteWords(bus, FastCodeBase + 0x184, 0xB348); // unsupported v2 target keeps branch-exit pressure observable
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2Tier3: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 7600; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.True(cpu.Counters.V2Tier3CompiledTraces > 0);
		Assert.True(cpu.Counters.V2TierPressurePromotions > 0);
		Assert.True(cpu.Counters.V2SideExitOutOfBlockBranch > 0);
	}

	[Fact]
	public void JitV2KeepsRootExecutableAfterTierCeilingBranchExitPressure()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7005, 0x6000, 0x0200); // MOVEQ #5,D0; BRA.W outside tier2 graph
		WriteWords(bus, FastCodeBase + 0x204, 0xB348); // unsupported v2 target keeps branch-exit pressure observable
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 5200; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.Equal(0, cpu.Counters.V2DisabledBranchExitRoots);
		Assert.True(cpu.Counters.V2BranchPressureLimitedRoots > 0);
		Assert.False(string.IsNullOrEmpty(cpu.Counters.V2BranchPressureLimitTop));
		var v2Hits = cpu.Counters.V2TraceHits;
		var branchExits = cpu.Counters.V2SideExitOutOfBlockBranch;

		for (var i = 0; i < 40; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.Equal(0, cpu.Counters.V2DisabledBranchExitRoots);
		Assert.True(cpu.Counters.V2TraceHits > v2Hits);
		Assert.True(cpu.Counters.V2SideExitOutOfBlockBranch > branchExits);
	}

	[Fact]
	public void JitV2QueuesTierCeilingBranchPressureTarget()
	{
		var previousAsync = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC");
		var previousWorkers = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS");
		var previousSyncFallback = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK");
		try
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", "1");
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", null);
			var target = FastCodeBase + 0x204;
			var bus = CreateCodeBus();
			WriteWords(bus, FastCodeBase, 0x7005, 0x6000, 0x0200); // MOVEQ #5,D0; BRA.W target
			WriteWords(bus, target, 0x7201, 0x60FC); // MOVEQ #1,D1; BRA.S target
			using var cpu = new M68kJitCore(bus, enableV2: true);
			cpu.Reset(FastCodeBase, 0x4000);
			var boundary = new PureBatchBoundary();

			for (var i = 0; i < 8000 && cpu.Counters.V2BranchPressureLimitedRoots == 0; i++)
			{
				cpu.State.ProgramCounter = FastCodeBase;
				cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
				Thread.Sleep(1);
			}

			Assert.True(cpu.Counters.V2BranchPressureLimitedRoots > 0);
			Assert.Contains("MOVEQ.L #imm->Dn", cpu.Counters.V2BranchPressureLimitTop ?? string.Empty);
			var targetStateTop = cpu.Counters.V2BranchPressureTargetStateTop ?? string.Empty;
			Assert.True(
				targetStateTop.Contains("queued:MOVEQ.L #imm->Dn") ||
				targetStateTop.Contains("deduped-pending:MOVEQ.L #imm->Dn") ||
				targetStateTop.Contains("deduped-compiling:MOVEQ.L #imm->Dn") ||
				targetStateTop.Contains("linked:MOVEQ.L #imm->Dn"));

			for (var i = 0; i < 8; i++)
			{
				cpu.State.ProgramCounter = FastCodeBase;
				cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
			}

			Assert.DoesNotContain("throttled:MOVEQ.L #imm->Dn", cpu.Counters.V2BranchPressureTargetStateTop ?? string.Empty);

			for (var i = 0;
				i < 1000 &&
				cpu.Counters.AsyncCompletedInstalled + cpu.Counters.AsyncWorkerCompilesFailed < cpu.Counters.AsyncRequestsQueued;
				i++)
			{
				Thread.Sleep(1);
				cpu.ExecuteInstructions(0, cpu.State.Cycles + 100_000, boundary);
			}

			var v2Hits = cpu.Counters.V2TraceHits;
			var fallback = cpu.Counters.FallbackInstructions;
			cpu.State.ProgramCounter = target;

			var executed = cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);

			Assert.Equal(2, executed);
			Assert.True(cpu.Counters.V2TraceHits > v2Hits);
			Assert.Equal(fallback, cpu.Counters.FallbackInstructions);
		}
		finally
		{
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC", previousAsync);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS", previousWorkers);
			Environment.SetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK", previousSyncFallback);
		}
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
	public void JitV2BlockGraphIncludesBackwardLoopHeaderBeforeHotRoot()
	{
		var words = new ushort[]
		{
			0x7001, // loop header: MOVEQ #1,D0
			0x5280, // hot root: ADDQ.L #1,D0
			0x60FA  // BRA.S loop header
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase + 2, 0x4000);
		interpreter.Reset(FastCodeBase + 2, 0x4000);
		var boundary = new PureBatchBoundary();
		var warmup = jit.ExecuteInstructions(220, 100_000, boundary);
		var outOfBlockExits = jit.Counters.V2SideExitOutOfBlockBranch;
		var beforeGraphExits = jit.Counters.V2SideExitBeforeGraph;
		var v2Hits = jit.Counters.V2TraceHits;

		var measured = jit.ExecuteInstructions(160, jit.State.Cycles + 100_000, boundary);
		var interpreted = interpreter.ExecuteInstructions(warmup + measured, null, new CountingBoundary());

		Assert.Equal(warmup + measured, interpreted);
		Assert.Equal(160, measured);
		Assert.True(jit.Counters.V2TraceHits > v2Hits);
		Assert.Equal(outOfBlockExits, jit.Counters.V2SideExitOutOfBlockBranch);
		Assert.Equal(beforeGraphExits, jit.Counters.V2SideExitBeforeGraph);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
	}

	[Fact]
	public void JitV2RecordsRejectedCandidateReasons()
	{
		var eaBus = CreateCodeBus();
		WriteWords(eaBus, FastCodeBase, 0x3030, 0x0000, 0x60FA); // MOVE.W d8(A0,D0.W),D0; BRA.S loop
		var eaCpu = new M68kJitCore(eaBus, enableV2: true, enableV2BusAccess: false);
		eaCpu.Reset(FastCodeBase, 0x4000);
		eaCpu.State.A[0] = 0x2000;
		eaCpu.ExecuteInstructions(180, null, new CountingBoundary());

		var opBus = CreateCodeBus();
		WriteWords(opBus, FastCodeBase, 0xB348, 0x60FC); // CMPM.W (A0)+,(A1)+; BRA.S loop
		var opCpu = new M68kJitCore(opBus, enableV2: true);
		opCpu.Reset(FastCodeBase, 0x4000);
		opCpu.State.A[0] = 0x2000;
		opCpu.State.A[1] = 0x2100;
		opCpu.ExecuteInstructions(180, null, new CountingBoundary());

		Assert.True(eaCpu.Counters.V2RejectedCandidates > 0);
		Assert.True(eaCpu.Counters.V2RejectedUnsupportedEa > 0);
		Assert.Contains("MOVE.W d8(An,Xn)->Dn", eaCpu.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.True(opCpu.Counters.V2RejectedCandidates > 0);
		Assert.True(opCpu.Counters.V2RejectedUnsupportedOperation > 0);
		Assert.Contains("CMPM.W (An)+->(An)+", opCpu.Counters.V2UnsupportedOperationTop ?? string.Empty);
	}

	[Fact]
	public void JitV2RecordsGraphHoleCause()
	{
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x6702, // BEQ.S unsupported
			0x6002, // BRA.S after unsupported
			0xB348, // unsupported: CMPM.W (A0)+,(A1)+
			0x4E71); // after unsupported: NOP
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		for (var i = 0; i < 80; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.State.StatusRegister = 0;
			cpu.State.D[0] = 1;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		var graphHoles = cpu.Counters.V2SideExitGraphHole;
		cpu.State.ProgramCounter = FastCodeBase;
		cpu.State.StatusRegister = M68kCpuState.Zero;
		cpu.State.D[0] = 1;

		cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);

		Assert.True(cpu.Counters.V2SideExitGraphHole > graphHoles);
		Assert.Contains("CMPM.W (An)+->(An)+", cpu.Counters.V2GraphHoleTop ?? string.Empty);
	}

	[Fact]
	public void JitV2FillsFastReadGraphHoleTargetAsChainableTrace()
	{
		var data = RealFastCodeBase + 0x1000;
		var bus = CreateRealFastCodeBus();
		bus.WriteLong(data, 0x89AB_CDEF);
		WriteWords(
			bus,
			RealFastCodeBase,
			0x6706, // BEQ.S fast-read target
			0x7201, // fallthrough: MOVEQ #1,D1
			0x6006, // BRA.S after target
			0x4E71, // filler
			0x2010, // target: MOVE.L (A0),D0
			0x4E71, // target: NOP
			0x4E71); // after target: NOP
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		cpu.Reset(RealFastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 90; i++)
		{
			cpu.State.ProgramCounter = RealFastCodeBase;
			cpu.State.StatusRegister = M68kCpuState.Zero;
			cpu.State.A[0] = data;
			cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
		}

		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.State.StatusRegister = M68kCpuState.Zero;
		cpu.State.A[0] = data;
		cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);

		Assert.True(
			cpu.Counters.V2GraphHoleTargetCompiles > 0,
			$"holeCompiles={cpu.Counters.V2GraphHoleTargetCompiles}, " +
			$"holes={cpu.Counters.V2SideExitGraphHole}, " +
			$"beyond={cpu.Counters.V2SideExitBeyondGraph}, " +
			$"before={cpu.Counters.V2SideExitBeforeGraph}, " +
			$"v2Hits={cpu.Counters.V2TraceHits}, " +
			$"compiled={cpu.Counters.CompiledTraces}, " +
			$"rejected={cpu.Counters.V2RejectedCandidates}, " +
			$"rejOp={cpu.Counters.V2UnsupportedOperationTop}, " +
			$"rejEa={cpu.Counters.V2UnsupportedEaTop}, " +
			$"top={cpu.Counters.V2GraphHoleTop}");
		Assert.Equal(0, cpu.Counters.V2DisabledGraphHoleRoots);
		var graphHoles = cpu.Counters.V2SideExitGraphHole;
		var v2Hits = cpu.Counters.V2TraceHits;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.State.StatusRegister = M68kCpuState.Zero;
		cpu.State.A[0] = data;

		var executed = cpu.ExecuteInstructions(3, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(3, executed);
		Assert.Equal(0x89AB_CDEFu, cpu.State.D[0]);
		Assert.Equal(RealFastCodeBase + 12, cpu.State.ProgramCounter);
		Assert.True(cpu.Counters.V2TraceHits >= v2Hits + 2);
		Assert.Equal(graphHoles, cpu.Counters.V2SideExitGraphHole);
	}

	[Fact]
	public void JitV2FillsBusAccessGraphHoleTargetAsChainableTrace()
	{
		var data = FastCodeBase + 0x2000;
		var words = new ushort[]
		{
			0x4A80, // loop: TST.L D0
			0x6704, // BEQ.S memoryTarget
			0x7201, // fallthrough: MOVEQ #1,D1
			0x6004, // BRA.S afterTarget
			0x3218, // memoryTarget: MOVE.W (A0)+,D1
			0x4E71, // target filler: NOP
			0x60F2  // afterTarget: BRA.S loop
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		for (var i = 0; i < 512; i++)
		{
			jitBus.WriteWord(data + (uint)(i * 2), (ushort)(0x4000 + i));
			interpreterBus.WriteWord(data + (uint)(i * 2), (ushort)(0x4000 + i));
		}

		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.D[0] = 1;
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;
		var boundary = new PureBatchBoundary();
		_ = jit.ExecuteInstructions(260, jit.State.Cycles + 1_000_000, boundary);

		jit.State.ProgramCounter = FastCodeBase;
		jit.State.StatusRegister = 0;
		jit.State.D[0] = 0;
		jit.State.D[1] = 0;
		jit.State.A[0] = data;
		interpreter.State.ProgramCounter = FastCodeBase;
		interpreter.State.StatusRegister = 0;
		interpreter.State.D[0] = 0;
		interpreter.State.D[1] = 0;
		interpreter.State.A[0] = data;
		var graphHoles = jit.Counters.V2SideExitGraphHole;
		var busInstructions = jit.Counters.V2BusAccessBatchInstructions;
		var graphHoleTargetCompiles = jit.Counters.V2GraphHoleTargetCompiles;
		var v2Hits = jit.Counters.V2TraceHits;

		var measured = jit.ExecuteInstructions(160, jit.State.Cycles + 1_000_000, boundary);
		var interpreted = interpreter.ExecuteInstructions(160, null, new CountingBoundary());

		Assert.Equal(interpreted, measured);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2GraphHoleTargetCompiles > graphHoleTargetCompiles);
		Assert.True(jit.Counters.V2TraceHits > v2Hits);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > busInstructions);
		Assert.Equal(graphHoles, jit.Counters.V2SideExitGraphHole);
		Assert.DoesNotContain("MOVE.W (An)+->Dn", jit.Counters.V2GraphHoleTop ?? string.Empty);
	}

	[Fact]
	public void JitV2DisablesRootAfterRepeatedUnsupportedGraphHole()
	{
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x6702, // BEQ.S unsupported v2 hole
			0x6002, // BRA.S after unsupported
			0xB348, // unsupported by v2: CMPM.W (A0)+,(A1)+
			0x4E71); // after unsupported: NOP
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 160; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.State.StatusRegister = M68kCpuState.Zero;
			cpu.State.D[0] = 1;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.True(cpu.Counters.V2DisabledGraphHoleRoots > 0);
		var v2Hits = cpu.Counters.V2TraceHits;
		var graphHoleExits = cpu.Counters.V2SideExitGraphHole;

		for (var i = 0; i < 20; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.State.StatusRegister = M68kCpuState.Zero;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.Equal(v2Hits, cpu.Counters.V2TraceHits);
		Assert.Equal(graphHoleExits, cpu.Counters.V2SideExitGraphHole);
	}

	[Fact]
	public void JitV2DisablesRootAfterRepeatedUncollectedBusGraphHole()
	{
		var bus = CreateCodeBus();
		WriteWords(
			bus,
			FastCodeBase,
			0x6702,       // BEQ.S MOVEM
			0x6004,       // BRA.S after MOVEM
			0x48E7, 0x8000, // MOVEM.L D0,-(A7)
			0x4E71);      // after MOVEM: NOP
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: false);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.State.D[0] = 0x1234_5678;
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 160; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.State.StatusRegister = M68kCpuState.Zero;
			cpu.State.A[7] = 0x4000;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.True(cpu.Counters.V2SideExitGraphHole > 0);
		Assert.True(cpu.Counters.V2DisabledGraphHoleRoots > 0);
		Assert.Contains("uncollected:MOVEM.L -(An)->-(An)", cpu.Counters.V2GraphHoleTop ?? string.Empty);
		var v2Hits = cpu.Counters.V2TraceHits;
		var graphHoleExits = cpu.Counters.V2SideExitGraphHole;

		for (var i = 0; i < 20; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.State.StatusRegister = M68kCpuState.Zero;
			cpu.State.A[7] = 0x4000;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		Assert.Equal(v2Hits, cpu.Counters.V2TraceHits);
		Assert.Equal(graphHoleExits, cpu.Counters.V2SideExitGraphHole);
	}

	[Fact]
	public void JitV2ChainsPureOutOfBlockTracesInsideSingleBatch()
	{
		var target = FastCodeBase + 0x100;
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x4EF9, unchecked((ushort)(target >> 16)), unchecked((ushort)target)); // JMP target
		WriteWords(bus, target, 0x4EF9, unchecked((ushort)(FastCodeBase >> 16)), unchecked((ushort)FastCodeBase)); // JMP root
		var cpu = new M68kJitCore(bus, enableV2: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		cpu.ExecuteInstructions(220, cpu.State.Cycles + 1_000_000, boundary);
		cpu.State.ProgramCounter = FastCodeBase;
		var batchAfterCount = boundary.BatchAfterCount;
		var batchInstructionCount = boundary.BatchInstructionCount;
		var v2Hits = cpu.Counters.V2TraceHits;

		var executed = cpu.ExecuteInstructions(20, cpu.State.Cycles + 1_000_000, boundary);

		Assert.Equal(20, executed);
		Assert.Equal(batchAfterCount + 1, boundary.BatchAfterCount);
		Assert.Equal(batchInstructionCount + 20, boundary.BatchInstructionCount);
		Assert.True(cpu.Counters.V2TraceHits >= v2Hits + 2);
		Assert.Equal(FastCodeBase, cpu.State.ProgramCounter);
	}

	[Fact]
	public void JitV2HandoffRunsCompiledBusTraceAfterPureOutOfBlockBranch()
	{
		var target = FastCodeBase + 0x104;
		var data = FastCodeBase + 0x2000;
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7005, 0x6000, 0x0100); // MOVEQ #5,D0; BRA.W target
		WriteWords(bus, target, 0x2080, 0x4E71); // MOVE.L D0,(A0); NOP
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: true);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 80; i++)
		{
			cpu.State.ProgramCounter = target;
			cpu.State.A[0] = data;
			cpu.State.D[0] = 0xCAFE_0000u + (uint)i;
			cpu.ExecuteInstructions(2, null, boundary);
		}

		for (var i = 0; i < 80; i++)
		{
			cpu.State.ProgramCounter = FastCodeBase;
			cpu.State.A[0] = data;
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
		}

		var handoffAttempts = cpu.Counters.V2TraceHandoffAttempts;
		var handoffExecutions = cpu.Counters.V2TraceHandoffExecutions;
		var handoffInstructions = cpu.Counters.V2TraceHandoffInstructions;
		var branchExits = cpu.Counters.V2SideExitOutOfBlockBranch;
		bus.WriteLong(data, 0);
		cpu.State.ProgramCounter = FastCodeBase;
		cpu.State.A[0] = data;

		var executed = cpu.ExecuteInstructions(4, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(4, executed);
		Assert.Equal(0x0000_0005u, bus.ReadLong(data));
		Assert.Equal(target + 4, cpu.State.ProgramCounter);
		Assert.True(cpu.Counters.V2TraceHandoffAttempts > handoffAttempts);
		Assert.True(cpu.Counters.V2TraceHandoffExecutions > handoffExecutions);
		Assert.True(cpu.Counters.V2TraceHandoffInstructions > handoffInstructions);
		Assert.Equal(branchExits, cpu.Counters.V2SideExitOutOfBlockBranch);
		Assert.True(cpu.Counters.V2BusAccessBatchExecutions > 0);
	}

	[Fact]
	public void JitV2CompilesRegisterAddressImmediateAndShiftForms()
	{
		var words = new ushort[]
		{
			0x203C, 0x0000, 0x2000, // MOVE.L #$2000,D0
			0x2040,                 // MOVEA.L D0,A0
			0x2208,                 // MOVE.L A0,D1
			0x0641, 0x0003,         // ADDI.W #3,D1
			0x45E8, 0x0010,         // LEA 16(A0),A2
			0xD5C1,                 // ADDA.L D1,A2
			0xB5C1,                 // CMPA.L D1,A2
			0xE589,                 // LSL.L #2,D1
			0x60E6                  // BRA.S loop
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.State.Cycles > 0);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectCpuIlInstructions > 0);
	}

	[Fact]
	public void JitV2BatchesRegisterCmpDnDn()
	{
		var words = new ushort[]
		{
			0x7002, // MOVEQ #2,D0
			0x7201, // MOVEQ #1,D1
			0xB081, // CMP.L D1,D0
			0x60F8  // BRA.S start
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

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.DoesNotContain("CMP.L Dn->Dn", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2DecodesCmpaAddressRegisterSource()
	{
		var words = new ushort[]
		{
			0x207C, 0x0000, 0x1000, // MOVEA.L #$1000,A0
			0x247C, 0x0000, 0x2000, // MOVEA.L #$2000,A2
			0xB5C8,                 // CMPA.L A0,A2
			0x60F0                  // BRA.S start
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

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.DoesNotContain("0xB5C8", jit.Counters.V2GraphHoleTop ?? string.Empty);
	}

	[Fact]
	public void JitV2DecodesArithmeticAddressRegisterSource()
	{
		var words = new ushort[]
		{
			0x287C, 0x0000, 0x0003, // MOVEA.L #3,A4
			0x7401,                 // MOVEQ #1,D2
			0xD48C,                 // ADD.L A4,D2
			0x60F4                  // BRA.S start
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

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.DoesNotContain("0xD48C", jit.Counters.V2GraphHoleTop ?? string.Empty);
	}

	[Fact]
	public void JitV2CompilesJmpAddressRegisterAsSideExit()
	{
		var target = FastCodeBase + 8;
		var words = new ushort[]
		{
			0x207C, (ushort)(target >> 16), (ushort)target, // MOVEA.L #target,A0
			0x4ED0, // JMP (A0)
			0x7002, // target: MOVEQ #2,D0
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

		var jitExecuted = jit.ExecuteInstructions(400, 100_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(400, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2SideExitOutOfBlockBranch > 0);
		Assert.Equal(0x0000_0002u, jit.State.D[0]);
	}

	[Fact]
	public void JitV2BusAccessBlockGraphKeepsBranchToMemoryReadInternal()
	{
		var data = FastCodeBase + 0x2000;
		var words = new ushort[]
		{
			0x4A80, // loop: TST.L D0
			0x6704, // BEQ.S memoryTarget
			0x7201, // fallthrough: MOVEQ #1,D1
			0x60F8, // BRA.S loop
			0x3218, // memoryTarget: MOVE.W (A0)+,D1
			0x60F4  // BRA.S loop
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		for (var i = 0; i < 512; i++)
		{
			jitBus.WriteWord(data + (uint)(i * 2), (ushort)(0x4000 + i));
			interpreterBus.WriteWord(data + (uint)(i * 2), (ushort)(0x4000 + i));
		}

		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;
		var boundary = new PureBatchBoundary();
		var warmup = jit.ExecuteInstructions(360, jit.State.Cycles + 1_000_000, boundary);
		var interpretedWarmup = interpreter.ExecuteInstructions(360, null, new CountingBoundary());
		Assert.Equal(interpretedWarmup, warmup);
		var graphHoles = jit.Counters.V2SideExitGraphHole;
		var branchExits = jit.Counters.V2SideExitOutOfBlockBranch;
		var busInstructions = jit.Counters.V2BusAccessBatchInstructions;

		var measured = jit.ExecuteInstructions(160, jit.State.Cycles + 1_000_000, boundary);
		var interpreted = interpreter.ExecuteInstructions(160, null, new CountingBoundary());

		Assert.Equal(interpreted, measured);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > busInstructions);
		Assert.Equal(graphHoles, jit.Counters.V2SideExitGraphHole);
		Assert.Equal(branchExits, jit.Counters.V2SideExitOutOfBlockBranch);
	}

	[Fact]
	public void JitV2BatchesMemoryReadMoveAndMoveaThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x2010,         // MOVE.L (A0),D0
			0x2268, 0x0004, // MOVEA.L 4(A0),A1
			0x5280          // ADDQ.L #1,D0
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		jitBus.WriteLong(0x2000, 0x1234_5678);
		jitBus.WriteLong(0x2004, 0x0000_3456);
		interpreterBus.WriteLong(0x2000, 0x1234_5678);
		interpreterBus.WriteLong(0x2004, 0x0000_3456);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2MemoryRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.A[0] = 0x2000;
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.A[0] = 0x2000;
			var jitExecuted = jit.ExecuteInstructions(3, jit.State.Cycles + 100_000, jitBoundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(3, null, interpreterBoundary);
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.True(jitBoundary.BatchAfterCount > 0);
		Assert.True(jitBoundary.AfterCount + jitBoundary.BatchAfterCount < interpreterBoundary.AfterCount);
	}

	[Fact]
	public void JitV2BatchesPseudoFastMemoryReadsThroughSlotAwarePath()
	{
		var data = FastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x207C, unchecked((ushort)(data >> 16)), unchecked((ushort)data), // MOVEA.L #data,A0
			0x2010, // MOVE.L (A0),D0
			0x5280, // ADDQ.L #1,D0
			0x60F4  // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_5678);
		interpreterBus.WriteLong(data, 0x1234_5678);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2MemoryRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(900, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(900, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x1234_5679u, jit.State.D[0]);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);

		var hotBusAccesses = jitBus.BusAccesses.Count;
		jit.ExecuteInstructions(60, jit.State.Cycles + 100_000, boundary);
		Assert.True(jitBus.BusAccesses.Count > hotBusAccesses);
	}

	[Fact]
	public void JitV2BatchesRealFastMemoryReadsThroughZeroWaitPath()
	{
		var data = RealFastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x207C, unchecked((ushort)(data >> 16)), unchecked((ushort)data), // MOVEA.L #data,A0
			0x2010,         // MOVE.L (A0),D0
			0x2268, 0x0004, // MOVEA.L 4(A0),A1
			0x5280,         // ADDQ.L #1,D0
			0x60F0          // BRA.S start
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_5678);
		jitBus.WriteLong(data + 4, 0x0000_3456);
		interpreterBus.WriteLong(data, 0x1234_5678);
		interpreterBus.WriteLong(data + 4, 0x0000_3456);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2MemoryRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		var jitExecuted = jit.ExecuteInstructions(900, 500_000, jitBoundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(900, null, interpreterBoundary);

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x1234_5679u, jit.State.D[0]);
		Assert.Equal(0x0000_3456u, jit.State.A[1]);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jitBoundary.BatchAfterCount > 0);
		Assert.True(jitBoundary.AfterCount + jitBoundary.BatchAfterCount < interpreterBoundary.AfterCount);
	}

	[Fact]
	public void JitV2BatchesRealFastMemoryWritesThroughBusAccessPath()
	{
		var data = RealFastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x203C, 0x1234, 0x5678, // MOVE.L #$12345678,D0
			0x207C, unchecked((ushort)(data >> 16)), unchecked((ushort)data), // MOVEA.L #data,A0
			0x2140, 0x0000,         // MOVE.L D0,0(A0)
			0x52A8, 0x0000          // ADDQ.L #1,0(A0)
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = RealFastCodeBase;
			interpreter.State.ProgramCounter = RealFastCodeBase;
			jitBus.WriteLong(data, 0);
			interpreterBus.WriteLong(data, 0);
			var jitExecuted = jit.ExecuteInstructions(4, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(4, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x1234_5679u, jitBus.ReadLong(data));
		Assert.Equal(interpreterBus.ReadLong(data), jitBus.ReadLong(data));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
	}

	[Fact]
	public void JitV2BatchesRomMemoryReadsThroughZeroWaitPath()
	{
		const uint romBase = 0x00F8_0000;
		var data = romBase + 0x1000;
		var rom = new byte[0x0008_0000];
		rom[0x1000] = 0x12;
		rom[0x1001] = 0x34;
		rom[0x1002] = 0x56;
		rom[0x1003] = 0x78;
		var words = new ushort[]
		{
			0x2010, // MOVE.L (A0),D0
			0x5280, // ADDQ.L #1,D0
			0x60FA  // BRA.S start
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		jitBus.MapReadOnlyMemory(romBase, rom);
		interpreterBus.MapReadOnlyMemory(romBase, rom);
		Assert.True(jitBus.TryReadJitZeroWaitMemory(data, M68kOperandSize.Long, out var romValue));
		Assert.Equal(0x1234_5678u, romValue);
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;
		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(900, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(900, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x1234_5679u, jit.State.D[0]);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);

		var hotBusAccesses = jitBus.BusAccesses.Count;
		jit.ExecuteInstructions(60, jit.State.Cycles + 100_000, boundary);
		Assert.Equal(hotBusAccesses, jitBus.BusAccesses.Count);
	}

	[Fact]
	public void JitV2FastReadOnlyTraceBatchesRealFastReadAsPureCpu()
	{
		var data = RealFastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x2010, // MOVE.L (A0),D0
			0x5280, // ADDQ.L #1,D0
			0x60FA  // BRA.S start
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_5678);
		interpreterBus.WriteLong(data, 0x1234_5678);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;
		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
		Assert.DoesNotContain("MOVE.L (An)->Dn", jit.Counters.V2UnsupportedEaTop ?? string.Empty);

		var hotBusAccesses = jitBus.BusAccesses.Count;
		jit.ExecuteInstructions(60, jit.State.Cycles + 100_000, boundary);
		Assert.Equal(hotBusAccesses, jitBus.BusAccesses.Count);
	}

	[Fact]
	public void JitV2FastReadOnlyTraceBatchesMultipleRealFastReadsAsPureCpu()
	{
		var data = RealFastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x2039, unchecked((ushort)(data >> 16)), unchecked((ushort)data), // MOVE.L data,D0
			0x2239, unchecked((ushort)((data + 4) >> 16)), unchecked((ushort)(data + 4)), // MOVE.L data+4,D1
			0xD081, // ADD.L D1,D0
			0x60F0  // BRA.S start
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_0000);
		jitBus.WriteLong(data + 4, 0x0000_0002);
		interpreterBus.WriteLong(data, 0x1234_0000);
		interpreterBus.WriteLong(data + 4, 0x0000_0002);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
		Assert.DoesNotContain("MOVE.L abs.L->Dn", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2FastReadOnlyTraceBatchesLaterPcIndexRealFastReadAsPureCpu()
	{
		var data = RealFastCodeBase + 0x1000;
		var index = unchecked((ushort)(data - (RealFastCodeBase + 8)));
		var words = new ushort[]
		{
			0x303C, index,  // MOVE.W #index,D0
			0x7201,         // MOVEQ #1,D1
			0x243B, 0x0000, // MOVE.L 0(PC,D0.W),D2
			0x5281,         // ADDQ.L #1,D1
			0x60F2          // BRA.S start
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_5678);
		interpreterBus.WriteLong(data, 0x1234_5678);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
		Assert.DoesNotContain("MOVE.L d8(PC,Xn)->Dn", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("MOVE.L d8(PC,Xn)->Dn", jit.Counters.V2GraphHoleTop ?? string.Empty);
	}

	[Fact]
	public void JitV2FastReadOnlyTraceDoesNotAdmitLaterStaticPseudoFastRead()
	{
		var realFastData = RealFastCodeBase + 0x1000;
		var pseudoFastData = FastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x2039, unchecked((ushort)(realFastData >> 16)), unchecked((ushort)realFastData), // MOVE.L realFastData,D0
			0x2239, unchecked((ushort)(pseudoFastData >> 16)), unchecked((ushort)pseudoFastData), // MOVE.L pseudoFastData,D1
			0xD081, // ADD.L D1,D0
			0x60F0  // BRA.S start
		};
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, realFastRamSize: FastCodeSize);
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, realFastRamSize: FastCodeSize);
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.WriteLong(realFastData, 0x1234_0000);
		jitBus.WriteLong(pseudoFastData, 0x0000_0002);
		interpreterBus.WriteLong(realFastData, 0x1234_0000);
		interpreterBus.WriteLong(pseudoFastData, 0x0000_0002);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x1234_0002u, jit.State.D[0]);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
	}

	[Fact]
	public void JitV2FastReadOnlyTraceSideExitsPseudoFastReadBeforeStateCommit()
	{
		var data = FastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x2010, // MOVE.L (A0),D0
			0x5280, // ADDQ.L #1,D0
			0x60FA  // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_5678);
		interpreterBus.WriteLong(data, 0x1234_5678);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.State.Cycles > 0);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchSideExits > 0);
		Assert.True(jit.Counters.V2DisabledEntryMismatchRoots > 0);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
	}

	[Fact]
	public void JitV2BusAccessTraceHandlesDynamicMemoryReadWhenFastReadIsEnabled()
	{
		var data = FastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x2028, 0x0004, // MOVE.L 4(A0),D0
			0x5280,         // ADDQ.L #1,D0
			0x60F8          // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		jitBus.WriteLong(data + 4, 0x1234_5678);
		interpreterBus.WriteLong(data + 4, 0x1234_5678);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2FastRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.Equal(0, jit.Counters.PureTraceBatchSideExits);
		Assert.Equal(0, jit.Counters.V2DisabledEntryMismatchRoots);
	}

	[Fact]
	public void JitV2BatchesMemoryWriteMoveThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x2140, 0x0004, // MOVE.L D0,4(A0)
			0x30FC, 0x55AA, // MOVE.W #$55AA,(A0)+
			0x2109          // MOVE.L A1,-(A0)
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2MemoryRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.D[0] = 0x1234_5678;
			jit.State.A[0] = 0x2000;
			jit.State.A[1] = 0x89AB_CDEF;
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.D[0] = 0x1234_5678;
			interpreter.State.A[0] = 0x2000;
			interpreter.State.A[1] = 0x89AB_CDEF;
			var jitExecuted = jit.ExecuteInstructions(3, jit.State.Cycles + 100_000, jitBoundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(3, null, interpreterBoundary);
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2004), jitBus.ReadLong(0x2004));
		Assert.Equal(interpreterBus.ReadWord(0x2000), jitBus.ReadWord(0x2000));
		Assert.Equal(interpreterBus.ReadLong(0x1FFE), jitBus.ReadLong(0x1FFE));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.True(jitBoundary.BatchAfterCount > 0);
		Assert.True(jitBoundary.AfterCount + jitBoundary.BatchAfterCount < interpreterBoundary.AfterCount);
	}

	[Fact]
	public void JitV2BatchesClrWordPostincrementThroughBusAccess()
	{
		var words = new ushort[]
		{
			0x4258, // CLR.W (A0)+
			0x4258, // CLR.W (A0)+
			0x60FA  // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		for (var i = 0; i < 512; i++)
		{
			var address = 0x2000u + (uint)(i * 2);
			jitBus.WriteWord(address, (ushort)(0x4000 + i));
			interpreterBus.WriteWord(address, (ushort)(0x4000 + i));
		}

		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = 0x2000;
		interpreter.State.A[0] = 0x2000;
		_ = jit.ExecuteInstructions(600, jit.State.Cycles + 500_000, new PureBatchBoundary());
		Assert.True(jit.Counters.V2TraceHits > 0);
		jit.State.ProgramCounter = FastCodeBase;
		jit.State.StatusRegister = M68kCpuState.Supervisor;
		jit.State.Cycles = 0;
		jit.State.Halted = false;
		jit.State.Stopped = false;
		jit.State.LastOpcode = 0;
		jit.State.LastInstructionProgramCounter = 0;
		Array.Clear(jit.State.D);
		Array.Clear(jit.State.A);
		jit.State.ResetStackPointers(0x4000, 0, supervisorMode: true);
		jit.State.A[0] = 0x2000;
		for (var i = 0; i < 512; i++)
		{
			var address = 0x2000u + (uint)(i * 2);
			jitBus.WriteWord(address, (ushort)(0x4000 + i));
		}

		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(600, jit.State.Cycles + 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(600, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		for (var i = 0; i < 512; i++)
		{
			var address = 0x2000u + (uint)(i * 2);
			Assert.Equal(interpreterBus.ReadWord(address), jitBus.ReadWord(address));
		}

		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.DoesNotContain("CLR.W (An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("CLR.W (An)+", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessTraceChainsToNextBusTraceInsideSameBoundary()
	{
		var rootA = FastCodeBase;
		var rootB = FastCodeBase + 0x200;
		var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
		var branchBToA = unchecked((ushort)(rootA - (rootB + 4)));
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, rootA, 0x2010, 0x6000, branchAToB); // MOVE.L (A0),D0; BRA.W rootB
		WriteWords(jitBus, rootB, 0x2211, 0x6000, branchBToA); // MOVE.L (A1),D1; BRA.W rootA
		WriteWords(interpreterBus, rootA, 0x2010, 0x6000, branchAToB);
		WriteWords(interpreterBus, rootB, 0x2211, 0x6000, branchBToA);
		jitBus.WriteLong(0x2000, 0x1234_5678);
		jitBus.WriteLong(0x3000, 0x89AB_CDEF);
		interpreterBus.WriteLong(0x2000, 0x1234_5678);
		interpreterBus.WriteLong(0x3000, 0x89AB_CDEF);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(rootA, 0x4000);
		interpreter.Reset(rootA, 0x4000);
		jit.State.A[0] = 0x2000;
		jit.State.A[1] = 0x3000;
		interpreter.State.A[0] = 0x2000;
		interpreter.State.A[1] = 0x3000;

		for (var i = 0; i < 80; i++)
		{
			jit.State.ProgramCounter = rootA;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());
			jit.State.ProgramCounter = rootB;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());
		}

		var handoffExecutionsBefore = jit.Counters.V2TraceHandoffExecutions;
		var handoffInstructionsBefore = jit.Counters.V2TraceHandoffInstructions;
		var boundary = new PureBatchBoundary();
		jit.State.ProgramCounter = rootA;

		var jitExecuted = jit.ExecuteInstructions(4, jit.State.Cycles + 100_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(4, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(4, jitExecuted);
		Assert.Equal(1, boundary.BatchAfterCount);
		Assert.Equal(4, boundary.BatchInstructionCount);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2TraceHandoffExecutions > handoffExecutionsBefore);
		Assert.True(jit.Counters.V2TraceHandoffInstructions > handoffInstructionsBefore);
		Assert.True(jit.Counters.V2TraceHandoffBlockTop?.Contains("no-trace") != true);
	}

	[Fact]
	public void JitV2BusAccessTraceChainsToPureTraceInsideSameBoundary()
	{
		var rootA = FastCodeBase;
		var rootB = FastCodeBase + 0x200;
		var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, rootA, 0x2010, 0x6000, branchAToB); // MOVE.L (A0),D0; BRA.W rootB
		WriteWords(jitBus, rootB, 0x5280, 0x6002, 0x4E71, 0x4E71); // ADDQ.L #1,D0; BRA.S +2; NOP; NOP
		WriteWords(interpreterBus, rootA, 0x2010, 0x6000, branchAToB);
		WriteWords(interpreterBus, rootB, 0x5280, 0x6002, 0x4E71, 0x4E71);
		jitBus.WriteLong(0x2000, 0x1234_5678);
		interpreterBus.WriteLong(0x2000, 0x1234_5678);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(rootA, 0x4000);
		interpreter.Reset(rootA, 0x4000);
		jit.State.A[0] = 0x2000;
		interpreter.State.A[0] = 0x2000;

		for (var i = 0; i < 80; i++)
		{
			jit.State.ProgramCounter = rootA;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());
			jit.State.ProgramCounter = rootB;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());
		}

		var handoffExecutionsBefore = jit.Counters.V2TraceHandoffExecutions;
		var handoffInstructionsBefore = jit.Counters.V2TraceHandoffInstructions;
		var directCpuBefore = jit.Counters.DirectCpuIlInstructions;
		var directMemoryBefore = jit.Counters.DirectMemoryIlInstructions;
		var boundary = new PureBatchBoundary();
		boundary.WakeSource = M68kTraceBatchWakeSource.Disk;
		jit.State.ProgramCounter = rootA;
		jit.State.A[0] = 0x2000;

		var jitExecuted = jit.ExecuteInstructions(5, jit.State.Cycles + 100_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(5, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(5, jitExecuted);
		Assert.Equal(1, boundary.BatchAfterCount);
		Assert.Equal(5, boundary.BatchInstructionCount);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2TraceHandoffExecutions > handoffExecutionsBefore);
		Assert.True(jit.Counters.V2TraceHandoffInstructions > handoffInstructionsBefore);
		Assert.True(jit.Counters.DirectCpuIlInstructions > directCpuBefore);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > directMemoryBefore);
		Assert.True(jit.Counters.V2TraceHandoffBlockTop?.Contains("pure") != true);
		Assert.Contains("5-8", jit.Counters.V2BusAccessBatchLengthHistogram ?? string.Empty);
		Assert.Contains("disk", jit.Counters.V2BusAccessBatchWakeSourceTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessTraceHandoffUsesFinalInstructionSlot()
	{
		var rootA = FastCodeBase;
		var rootB = FastCodeBase + 0x200;
		var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, rootA, 0x2010, 0x6000, branchAToB); // MOVE.L (A0),D0; BRA.W rootB
		WriteWords(jitBus, rootB, 0x2211, 0x4E71); // MOVE.L (A1),D1; NOP
		WriteWords(interpreterBus, rootA, 0x2010, 0x6000, branchAToB);
		WriteWords(interpreterBus, rootB, 0x2211, 0x4E71);
		jitBus.WriteLong(0x2000, 0x1234_5678);
		jitBus.WriteLong(0x3000, 0x89AB_CDEF);
		interpreterBus.WriteLong(0x2000, 0x1234_5678);
		interpreterBus.WriteLong(0x3000, 0x89AB_CDEF);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(rootA, 0x4000);
		interpreter.Reset(rootA, 0x4000);
		jit.State.A[0] = 0x2000;
		jit.State.A[1] = 0x3000;
		interpreter.State.A[0] = 0x2000;
		interpreter.State.A[1] = 0x3000;

		for (var i = 0; i < 80; i++)
		{
			jit.State.ProgramCounter = rootA;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());
			jit.State.ProgramCounter = rootB;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());
		}

		var handoffExecutionsBefore = jit.Counters.V2TraceHandoffExecutions;
		var handoffInstructionsBefore = jit.Counters.V2TraceHandoffInstructions;
		var boundary = new PureBatchBoundary();
		jit.State.ProgramCounter = rootA;

		var jitExecuted = jit.ExecuteInstructions(3, jit.State.Cycles + 100_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(3, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(3, jitExecuted);
		Assert.Equal(0, boundary.AfterCount);
		Assert.Equal(1, boundary.BatchAfterCount);
		Assert.Equal(3, boundary.BatchInstructionCount);
		Assert.True(jit.Counters.V2TraceHandoffExecutions > handoffExecutionsBefore);
		Assert.True(jit.Counters.V2TraceHandoffInstructions > handoffInstructionsBefore);
	}

	[Fact]
	public void JitV2BusHandoffDiagnosticsPreserveBusBlockReasonForMemoryTarget()
	{
		var rootA = FastCodeBase;
		var rootB = FastCodeBase + 0x200;
		var branchAToB = unchecked((ushort)(rootB - (rootA + 4)));
		var jitBus = CreateCodeBus();
		WriteWords(jitBus, rootA, 0x2010, 0x6000, branchAToB); // MOVE.L (A0),D0; BRA.W rootB
		WriteWords(jitBus, rootB, 0x2211, 0x4E71); // MOVE.L (A1),D1; NOP
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		jit.Reset(rootA, 0x4000);
		jit.State.A[0] = 0x2000;
		jit.State.A[1] = 0x3000;
		jitBus.WriteLong(0x2000, 0x1234_5678);
		jitBus.WriteLong(0x3000, 0x89AB_CDEF);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 80; i++)
		{
			jit.State.ProgramCounter = rootA;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, boundary);
			jit.State.ProgramCounter = rootB;
			_ = jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, boundary);
		}

		var disabledRootsField = typeof(M68kJitCore).GetField(
			"_v2DisabledRoots",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		var disabledRoots = Assert.IsType<HashSet<uint>>(disabledRootsField.GetValue(jit));
		disabledRoots.Add(rootB);
		jit.State.ProgramCounter = rootA;
		jit.State.A[0] = 0x2000;
		jit.State.A[1] = 0x3000;

		_ = jit.ExecuteInstructions(4, jit.State.Cycles + 100_000, boundary);

		Assert.Contains("disabled:MOVE.L (An)->Dn", jit.Counters.V2TraceHandoffBlockTop ?? string.Empty);
		Assert.Contains("disabled:MOVE.L (An)->Dn", jit.Counters.V2TraceHandoffFailureTop ?? string.Empty);
		Assert.DoesNotContain("not-pure:MOVE.L (An)->Dn", jit.Counters.V2TraceHandoffBlockTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessBatchesMemoryWriteAndQuickModifyWithoutMemoryReadOption()
	{
		var words = new ushort[]
		{
			0x20C0,         // MOVE.L D0,(A0)+
			0x52A8, 0x0004, // ADDQ.L #1,4(A0)
			0x42A8, 0x0008, // CLR.L 8(A0)
			0x08D0, 0x0003  // BSET #3,(A0)
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.D[0] = 0x1234_5678;
			jit.State.A[0] = 0x2000;
			jitBus.WriteByte(0x2004, 0, 0);
			jitBus.WriteLong(0x2008, 0x0000_0010);
			jitBus.WriteLong(0x200C, 0xFFFF_FFFF);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.D[0] = 0x1234_5678;
			interpreter.State.A[0] = 0x2000;
			interpreterBus.WriteByte(0x2004, 0, 0);
			interpreterBus.WriteLong(0x2008, 0x0000_0010);
			interpreterBus.WriteLong(0x200C, 0xFFFF_FFFF);
			var jitExecuted = jit.ExecuteInstructions(4, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(4, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2000), jitBus.ReadLong(0x2000));
		Assert.Equal(interpreterBus.ReadByte(0x2004), jitBus.ReadByte(0x2004));
		Assert.Equal(interpreterBus.ReadLong(0x2008), jitBus.ReadLong(0x2008));
		Assert.Equal(interpreterBus.ReadLong(0x200C), jitBus.ReadLong(0x200C));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("MOVE.L Dn->(An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("ADDQ.L #q->d16(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessBatchesMemoryToMemoryMoveWithoutMemoryReadOption()
	{
		var words = new ushort[]
		{
			0x2168, 0x0004, 0x0008, // MOVE.L 4(A0),8(A0)
			0x22D8                  // MOVE.L (A0)+,(A1)+
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.A[0] = 0x2000;
			jit.State.A[1] = 0x3000;
			jitBus.WriteLong(0x2000, 0x1234_5678);
			jitBus.WriteLong(0x2004, 0x89AB_CDEF);
			jitBus.WriteLong(0x2008, 0);
			jitBus.WriteLong(0x3000, 0);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.A[0] = 0x2000;
			interpreter.State.A[1] = 0x3000;
			interpreterBus.WriteLong(0x2000, 0x1234_5678);
			interpreterBus.WriteLong(0x2004, 0x89AB_CDEF);
			interpreterBus.WriteLong(0x2008, 0);
			interpreterBus.WriteLong(0x3000, 0);
			var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2008), jitBus.ReadLong(0x2008));
		Assert.Equal(interpreterBus.ReadLong(0x3000), jitBus.ReadLong(0x3000));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("MOVE.L d16(An)->d16(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("MOVE.L (An)+->(An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessBatchesMemoryArithmeticCompareAndCmpaWithoutMemoryReadOption()
	{
		var words = new ushort[]
		{
			0xD1A8, 0x0004,       // ADD.L D0,4(A0)
			0x0C28, 0x0005, 0x0004, // CMPI.B #5,4(A0)
			0xB3E8, 0x0004        // CMPA.L 4(A0),A1
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.D[0] = 0x0000_0001;
			jit.State.A[0] = 0x2000;
			jit.State.A[1] = 0x0000_0020;
			jitBus.WriteLong(0x2004, 0x0000_0010);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.D[0] = 0x0000_0001;
			interpreter.State.A[0] = 0x2000;
			interpreter.State.A[1] = 0x0000_0020;
			interpreterBus.WriteLong(0x2004, 0x0000_0010);
			var jitExecuted = jit.ExecuteInstructions(3, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(3, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2004), jitBus.ReadLong(0x2004));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("ADD.L d16(An)->d16(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("CMPI.B #imm->d16(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("CMPA.L d16(An)->An", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessBatchesCmpmBytePostincrementWithoutMemoryReadOption()
	{
		var words = new ushort[]
		{
			0xB308, // CMPM.B (A0)+,(A1)+
			0x60FC  // BRA.S CMPM
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = 0x2000;
		jit.State.A[1] = 0x3000;
		interpreter.State.A[0] = 0x2000;
		interpreter.State.A[1] = 0x3000;
		for (var i = 0; i < 256; i++)
		{
			jitBus.WriteByte(0x2000u + (uint)i, 0x20, 0);
			jitBus.WriteByte(0x3000u + (uint)i, 0x10, 0);
			interpreterBus.WriteByte(0x2000u + (uint)i, 0x20, 0);
			interpreterBus.WriteByte(0x3000u + (uint)i, 0x10, 0);
		}

		var boundary = new PureBatchBoundary();
		_ = jit.ExecuteInstructions(220, 500_000, boundary);
		var directMemoryBefore = jit.Counters.DirectMemoryIlInstructions;
		var busBatchBefore = jit.Counters.V2BusAccessBatchExecutions;
		jit.State.ProgramCounter = FastCodeBase;
		jit.State.StatusRegister = 0;
		jit.State.A[0] = 0x2000;
		jit.State.A[1] = 0x3000;
		interpreter.State.StatusRegister = 0;

		var jitExecuted = jit.ExecuteInstructions(64, jit.State.Cycles + 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(64, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > directMemoryBefore);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > busBatchBefore);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("CMPM.B (An)+->(An)+", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.DoesNotContain("CMPM.B (An)+->(An)+", jit.Counters.V2GraphHoleTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessBatchesMemoryLogicalOpsWithoutMemoryReadOption()
	{
		var words = new ushort[]
		{
			0xC1A8, 0x0004,             // AND.L D0,4(A0)
			0x81A8, 0x0008,             // OR.L D0,8(A0)
			0xB1A8, 0x000C,             // EOR.L D0,12(A0)
			0x0228, 0x000F, 0x0010,     // ANDI.B #$0F,16(A0)
			0x0068, 0x00F0, 0x0012,     // ORI.W #$00F0,18(A0)
			0x0AA8, 0x0000, 0x00FF, 0x0014 // EORI.L #$FF,20(A0)
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.D[0] = 0x0F0F_0F0F;
			jit.State.A[0] = 0x2000;
			jitBus.WriteLong(0x2004, 0xFFFF_0000);
			jitBus.WriteLong(0x2008, 0x0000_00F0);
			jitBus.WriteLong(0x200C, 0xFFFF_FFFF);
			jitBus.WriteByte(0x2010, 0xF5, 0);
			jitBus.WriteWord(0x2012, 0x0005);
			jitBus.WriteLong(0x2014, 0x1234_5600);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.D[0] = 0x0F0F_0F0F;
			interpreter.State.A[0] = 0x2000;
			interpreterBus.WriteLong(0x2004, 0xFFFF_0000);
			interpreterBus.WriteLong(0x2008, 0x0000_00F0);
			interpreterBus.WriteLong(0x200C, 0xFFFF_FFFF);
			interpreterBus.WriteByte(0x2010, 0xF5, 0);
			interpreterBus.WriteWord(0x2012, 0x0005);
			interpreterBus.WriteLong(0x2014, 0x1234_5600);
			var jitExecuted = jit.ExecuteInstructions(6, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(6, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2004), jitBus.ReadLong(0x2004));
		Assert.Equal(interpreterBus.ReadLong(0x2008), jitBus.ReadLong(0x2008));
		Assert.Equal(interpreterBus.ReadLong(0x200C), jitBus.ReadLong(0x200C));
		Assert.Equal(interpreterBus.ReadByte(0x2010), jitBus.ReadByte(0x2010));
		Assert.Equal(interpreterBus.ReadWord(0x2012), jitBus.ReadWord(0x2012));
		Assert.Equal(interpreterBus.ReadLong(0x2014), jitBus.ReadLong(0x2014));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("AND.L d16(An)->d16(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("ORI.W #imm->d16(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BatchesBsrAndRtsThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x6104, // BSR.S target
			0x7002, // MOVEQ #2,D0
			0x60FA, // BRA.S start
			0x7001, // target: MOVEQ #1,D0
			0x4E75  // RTS
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2MemoryRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, jitBoundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, interpreterBoundary);

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x3FFC), jitBus.ReadLong(0x3FFC));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.True(jit.Counters.V2SideExitOutOfBlockBranch > 0);
		Assert.True(jitBoundary.BatchAfterCount > 0);
		Assert.True(jitBoundary.AfterCount + jitBoundary.BatchAfterCount < interpreterBoundary.AfterCount);
	}

	[Fact]
	public void JitV2BatchesJsrAndRtsThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x4E90, // JSR (A0)
			0x7002, // MOVEQ #2,D0
			0x60FA, // BRA.S start
			0x7001, // target: MOVEQ #1,D0
			0x4E75  // RTS
		};
		var target = FastCodeBase + 6;
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = target;
		interpreter.State.A[0] = target;
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, jitBoundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, interpreterBoundary);

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x3FFC), jitBus.ReadLong(0x3FFC));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.True(jit.Counters.V2SideExitOutOfBlockBranch > 0);
		Assert.True(jitBoundary.BatchAfterCount > 0);
		Assert.True(jitBoundary.AfterCount + jitBoundary.BatchAfterCount < interpreterBoundary.AfterCount);
	}

	[Fact]
	public void JitV2JsrHostTrapSideExitDoesNotPushReturnAddress()
	{
		var hostTarget = FastCodeBase + 0x100;
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, 0x4E90); // JSR (A0)
		WriteWords(interpreterBus, FastCodeBase, 0x4E90);
		var jitHostHits = 0;
		var interpreterHostHits = 0;
		jitBus.RegisterHostCallback(hostTarget, state =>
		{
			jitHostHits++;
			state.ProgramCounter = FastCodeBase;
		});
		interpreterBus.RegisterHostCallback(hostTarget, state =>
		{
			interpreterHostHits++;
			state.ProgramCounter = FastCodeBase;
		});
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = hostTarget;
		interpreter.State.A[0] = hostTarget;

		var jitExecuted = jit.ExecuteInstructions(220, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(220, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreterHostHits, jitHostHits);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x4000u, jit.State.A[7]);
		Assert.Equal(0u, jitBus.ReadLong(0x3FFC));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.HostTrapBailouts > 0);
		Assert.True(jit.Counters.V2SideExitHostTrap > 0);
	}

	[Fact]
	public void JitV2BatchesRegisterBitOperations()
	{
		var words = new ushort[]
		{
			0x70FF,       // MOVEQ #-1,D0
			0x4240,       // CLR.W D0
			0x7201,       // MOVEQ #1,D1
			0x08C0, 0x0003, // BSET #3,D0
			0x0800, 0x0003, // BTST #3,D0
			0x0380,       // BCLR D1,D0
			0x0340,       // BCHG D1,D0
			0x03C0,       // BSET D1,D0
			0x60EA        // BRA.S start
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

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
		Assert.True(boundary.BatchAfterCount > 0);
	}

	[Fact]
	public void JitV2BatchesRegisterDivideOperations()
	{
		var words = new ushort[]
		{
			0x203C, 0x0000, 0x0006, // MOVE.L #6,D0
			0x323C, 0x0003,         // MOVE.W #3,D1
			0x80C1,                 // DIVU.W D1,D0
			0x203C, 0xFFFF, 0xFFFA, // MOVE.L #-6,D0
			0x323C, 0xFFFD,         // MOVE.W #-3,D1
			0x81C1,                 // DIVS.W D1,D0
			0x60EA                  // BRA.S start
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

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.DoesNotContain("DIVU", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.DoesNotContain("DIVS", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BusAccessBatchesMemoryMultiplyAndDivideSourcesWithoutMemoryReadOption()
	{
		var words = new ushort[]
		{
			0x203C, 0x0000, 0x0006, // MOVE.L #6,D0
			0xC0E8, 0x0004,         // MULU.W 4(A0),D0
			0x203C, 0xFFFF, 0xFFFA, // MOVE.L #-6,D0
			0xC1E8, 0x0006,         // MULS.W 6(A0),D0
			0x203C, 0x0000, 0x0006, // MOVE.L #6,D0
			0x80E8, 0x0008,         // DIVU.W 8(A0),D0
			0x203C, 0xFFFF, 0xFFFA, // MOVE.L #-6,D0
			0x81E8, 0x000A          // DIVS.W 10(A0),D0
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.A[0] = 0x2000;
			jitBus.WriteWord(0x2004, 0x0003);
			jitBus.WriteWord(0x2006, 0xFFFD);
			jitBus.WriteWord(0x2008, 0x0003);
			jitBus.WriteWord(0x200A, 0xFFFD);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.A[0] = 0x2000;
			interpreterBus.WriteWord(0x2004, 0x0003);
			interpreterBus.WriteWord(0x2006, 0xFFFD);
			interpreterBus.WriteWord(0x2008, 0x0003);
			interpreterBus.WriteWord(0x200A, 0xFFFD);
			var jitExecuted = jit.ExecuteInstructions(8, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(8, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("MULU", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("MULS", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("DIVU", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("DIVS", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitV2DivideByZeroUsesExceptionPath()
	{
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, 0x81FC, 0x0000, 0x60FA); // DIVS.W #0,D0; BRA.S back to DIVS
		WriteWords(interpreterBus, FastCodeBase, 0x81FC, 0x0000, 0x60FA);
		WriteWords(jitBus, 0x2000, 0x4E73); // RTE
		WriteWords(interpreterBus, 0x2000, 0x4E73);
		jitBus.WriteLong(5 * 4, 0x0000_2000);
		interpreterBus.WriteLong(5 * 4, 0x0000_2000);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.D[0] = 0x1234_5678;
		interpreter.State.D[0] = 0x1234_5678;

		var jitExecuted = jit.ExecuteInstructions(260, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(260, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.False(jit.State.Halted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
	}

	[Fact]
	public void JitV2BatchesMemoryClrTstAndBitOperationsThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x7203,                 // MOVEQ #3,D1
			0x42A8, 0x0004,         // CLR.L 4(A0)
			0x4A10,                 // TST.B (A0)
			0x08D0, 0x0003,         // BSET #3,(A0)
			0x0810, 0x0003,         // BTST #3,(A0)
			0x0390,                 // BCLR D1,(A0)
			0x0850, 0x0000,         // BCHG #0,(A0)
			0x60E2                  // BRA.S start
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		jitBus.WriteByte(0x2000, 0, 0);
		jitBus.WriteLong(0x2004, 0xFFFF_FFFF);
		interpreterBus.WriteByte(0x2000, 0, 0);
		interpreterBus.WriteLong(0x2004, 0xFFFF_FFFF);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2MemoryRead: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, jitBoundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, interpreterBoundary);

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadByte(0x2000), jitBus.ReadByte(0x2000));
		Assert.Equal(interpreterBus.ReadLong(0x2004), jitBus.ReadLong(0x2004));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.True(jitBoundary.BatchAfterCount > 0);
		Assert.True(jitBoundary.AfterCount + jitBoundary.BatchAfterCount < interpreterBoundary.AfterCount);
		Assert.DoesNotContain("BIT", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.DoesNotContain("CLR", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
	}

	[Fact]
	public void JitV2BatchesMovemPredecrementStoreThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x7001,       // MOVEQ #1,D0
			0x48E7, 0x8000, // MOVEM.L D0,-(A7)
			0x60FA        // BRA.S loop
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

		var jitExecuted = jit.ExecuteInstructions(600, 1_000_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(600, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(interpreter.State.A[7]), jitBus.ReadLong(jit.State.A[7]));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.True(boundary.BatchAfterCount > 0);
	}

	[Fact]
	public void JitV2BusAccessDisabledKeepsMovemOnFallback()
	{
		var words = new ushort[]
		{
			0x7001,       // MOVEQ #1,D0
			0x48E7, 0x8000, // MOVEM.L D0,-(A7)
			0x60FA        // BRA.S loop
		};
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, words);
		WriteWords(interpreterBus, FastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);

		var jitExecuted = jit.ExecuteInstructions(600, 1_000_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(600, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(interpreter.State.A[7]), jitBus.ReadLong(jit.State.A[7]));
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchInstructions);
	}

	[Fact]
	public void JitV2BatchesMovemPostincrementLoadThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x4CDF, 0x0003, // MOVEM.L (A7)+,D0-D1
			0x60FA        // BRA.S loop
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
		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = FastCodeBase;
			jit.State.A[7] = 0x3000;
			jitBus.WriteLong(0x3000, 0x1234_5678);
			jitBus.WriteLong(0x3004, 0x89AB_CDEF);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.A[7] = 0x3000;
			interpreterBus.WriteLong(0x3000, 0x1234_5678);
			interpreterBus.WriteLong(0x3004, 0x89AB_CDEF);
			var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
	}

	[Fact]
	public void JitV2BatchesImmediateMultiply()
	{
		var words = new ushort[]
		{
			0x203C, 0x0000, 0x0006, // MOVE.L #6,D0
			0xC0FC, 0x0003,         // MULU.W #3,D0
			0x203C, 0xFFFF, 0xFFFA, // MOVE.L #-6,D0
			0xC1FC, 0xFFFD,         // MULS.W #-3,D0
			0x60EC                  // BRA.S loop
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

		var jitExecuted = jit.ExecuteInstructions(1200, 1_000_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
		Assert.True(boundary.BatchAfterCount > 0);
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
		Assert.True(cpu.Counters.DirectMemoryIlInstructions > 0);
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

	[Fact]
	public void JitSlotAwareMemoryHelperMatchesRegularExpansionRamBusAccess()
	{
		var regularBus = CreateCodeBus();
		var jitBus = CreateCodeBus();
		var address = FastCodeBase + 0x2000;
		regularBus.WriteLong(address, 0x1234_5678);
		jitBus.WriteLong(address, 0x1234_5678);
		var regularReadCycle = 123L;
		var jitReadCycle = 123L;

		var regularValue = regularBus.ReadLong(address, ref regularReadCycle, AmigaBusAccessKind.CpuDataRead);
		var jitValue = jitBus.ReadJitSlotAwareMemory(ref jitReadCycle, address, M68kOperandSize.Long);

		Assert.Equal(regularValue, jitValue);
		Assert.Equal(regularReadCycle, jitReadCycle);

		var regularWriteCycle = 456L;
		var jitWriteCycle = 456L;
		regularBus.WriteLong(address, 0x89AB_CDEF, ref regularWriteCycle, AmigaBusAccessKind.CpuDataWrite);
		jitBus.WriteJitSlotAwareMemory(ref jitWriteCycle, address, 0x89AB_CDEF, M68kOperandSize.Long);

		Assert.Equal(regularWriteCycle, jitWriteCycle);
		Assert.Equal(regularBus.ReadLong(address), jitBus.ReadLong(address));
		Assert.Equal(regularBus.BusAccesses[^1].GrantedCycle, jitBus.BusAccesses[^1].GrantedCycle);
		Assert.Equal(regularBus.BusAccesses[^1].CompletedCycle, jitBus.BusAccesses[^1].CompletedCycle);
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

	private static object? GetInstalledTraceDelegate(M68kJitCore cpu, uint root, string propertyName)
	{
		var field = typeof(M68kJitCore).GetField(
			"_traces",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		var traces = (System.Collections.IDictionary)field.GetValue(cpu)!;
		if (!traces.Contains(root))
		{
			return null;
		}

		var trace = traces[root]!;
		return trace
			.GetType()
			.GetProperty(
				propertyName,
				System.Reflection.BindingFlags.Instance |
					System.Reflection.BindingFlags.Public |
					System.Reflection.BindingFlags.NonPublic)!
			.GetValue(trace);
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

	private sealed class PureBatchBoundary :
		CountingBoundary,
		IM68kTraceBatchDiagnosticsBoundary,
		IM68kPureCpuTraceBatchBoundary,
		IM68kBusAccessTraceBatchBoundary
	{
		public int BatchBeginCount { get; private set; }

		public int BatchAfterCount { get; private set; }

		public int BatchInstructionCount { get; private set; }

		public long? WakeCycle { get; set; }

		public M68kTraceBatchWakeSource WakeSource { get; set; } = M68kTraceBatchWakeSource.TargetCycle;

		public M68kTraceBatchWakeSource LastTraceBatchWakeSource { get; private set; } = M68kTraceBatchWakeSource.TargetCycle;

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
			LastTraceBatchWakeSource = WakeSource;
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

		public bool TryBeginBusAccessTraceBatch(
			M68kCpuState state,
			long targetCycle,
			out long batchTargetCycle)
			=> TryBeginPureCpuTraceBatch(state, targetCycle, out batchTargetCycle);

		public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount)
			=> AfterPureCpuTraceBatch(previousCycle, currentCycle, instructionCount);
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
