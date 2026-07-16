using CopperFloat;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kJitCoreTests
{
	private const uint FastCodeBase = AmigaConstants.A500BootPseudoFastRamBase;
	private const uint RealFastCodeBase = AmigaConstants.A500RealFastRamBase;
	private const int FastCodeSize = 64 * 1024;
	private const uint M68040InstructionCacheEnable = 0x0000_0001;

	[Fact]
	public void FactoryCreatesJitBackend()
	{
		var bus = new AmigaBus();

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68000, bus);
		var jit = Assert.IsType<M68kJitCore>(cpu);

		Assert.True(GetPrivateBool(jit, "_v2Enabled"));
		Assert.True(GetPrivateBool(jit, "_v2BusAccessEnabled"));
		Assert.True(GetPrivateBool(jit, "_v2BusGraphEnabled"));
	}

	[Fact]
	public void FactoryM68000JitUsesV2BusGraphForMemoryLoop()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x2010, 0x60FC); // MOVE.L (A0),D0; BRA.S MOVE
		bus.WriteLong(0x2000, 0x1234_5678);
		var jit = Assert.IsType<M68kJitCore>(
			AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68000, bus));
		jit.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = 0x2000;

		var executed = jit.ExecuteInstructions(220, jit.State.Cycles + 100_000, new PureBatchBoundary());

		Assert.Equal(220, executed);
		Assert.Equal(0x1234_5678u, jit.State.D[0]);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
	}

	[Fact]
	public void M68000JitTasChipRamLosesWriteBack()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4A, 0xD0); // TAS (A0)
		bus.ChipRam[0x2000] = 0x01;
		using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x01, bus.ChipRam[0x2000]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void HardwareSpecializedM68000JitFallbackPreservesExactPrefetchBusPhases()
	{
		var interpreterBus = new AmigaBus(captureBusAccesses: true, enableHardwareSpecialization: true);
		var jitBus = new AmigaBus(captureBusAccesses: true, enableHardwareSpecialization: true);
		Write(interpreterBus.ChipRam, 0x1000,
			0x3A, 0x13, // MOVE.W (A3),D5
			0x34, 0xC5, // MOVE.W D5,(A2)+
			0x60, 0xFA); // BRA.S loop
		Write(jitBus.ChipRam, 0x1000,
			0x3A, 0x13,
			0x34, 0xC5,
			0x60, 0xFA);
		var interpreter = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, interpreterBus);
		var jit = Assert.IsType<M68kJitCore>(
			AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68000, jitBus));
		interpreter.Reset(0x1000, 0x4000);
		jit.Reset(0x1000, 0x4000);
		interpreter.State.A[2] = jit.State.A[2] = 0x2000;
		interpreter.State.A[3] = jit.State.A[3] = 0x00DFF006;

		for (var instruction = 0; instruction < 30; instruction++)
		{
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();
		}

		var interpreterPhases = interpreterBus.CpuBusPhases
			.Select(phase => (
				phase.CpuPhase.InstructionProgramCounter,
				phase.CpuPhase.Address,
				phase.CpuPhase.RequestedCycle,
				phase.CpuPhase.CompletedCycle,
				phase.CpuPhase.AccessKind,
				phase.CpuPhase.IsWrite))
			.ToArray();
		var jitPhases = jitBus.CpuBusPhases
			.Select(phase => (
				phase.CpuPhase.InstructionProgramCounter,
				phase.CpuPhase.Address,
				phase.CpuPhase.RequestedCycle,
				phase.CpuPhase.CompletedCycle,
				phase.CpuPhase.AccessKind,
				phase.CpuPhase.IsWrite))
			.ToArray();

		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreterPhases, jitPhases);
		Assert.Equal(30, jit.Counters.FallbackInstructions);
		Assert.Equal(0, jit.Counters.TraceHits);
	}

	[Fact]
	public void FactoryCreatesM68040JitBackendWithM68040State()
	{
		using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, new AmigaBus());

		var jit = Assert.IsType<M68kJitCore>(cpu);
		Assert.True(jit.State.M68020StackModeEnabled);
		Assert.Equal(0u, jit.State.M68040Mmu.TranslationControl);
		Assert.Equal(0u, jit.State.M68040Fpu.Fpcr);
	}

	[Fact]
	public void M68040JitBenchmarkResetPreservesCompiledTraceAndResetsCpuState()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		using var jit = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true);
		jit.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(jit);
		ExecuteUntilTraceHits(jit, new PureBatchBoundary(), 1);

		jit.State.D[0] = 0xDEAD_BEEFu;
		jit.State.Cycles = 1234;
		jit.ResetForBenchmark(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(jit);

		Assert.Equal(0u, jit.State.D[0]);
		Assert.Equal(0L, jit.State.Cycles);
		Assert.Equal(0L, jit.Counters.CompiledTraces);

		var executed = jit.ExecuteInstructions(32, jit.State.Cycles + 100_000, new PureBatchBoundary());

		Assert.Equal(32, executed);
		Assert.Equal(1u, jit.State.D[0]);
		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void PublicFactoryKeepsM68040InterpreterByDefault()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, new AmigaBus());

		Assert.IsType<M68040Interpreter>(cpu);
	}

	[Fact]
	public void PublicFactoryCreatesM68040JitBackendWithOptions()
	{
		using var cpu = M68kCoreFactory.Default.Create(
			M68kCpuModel.M68040,
			new AmigaBus(),
			new M68kCoreOptions { ExecutionMode = M68kExecutionMode.Jit });

		Assert.IsType<M68kJitCore>(cpu);
	}

	[Theory]
	[InlineData(M68kCpuModel.M68000)]
	[InlineData(M68kCpuModel.M68020)]
	[InlineData(M68kCpuModel.M68030)]
	public void PublicFactoryRejectsJitForNonM68040Models(M68kCpuModel model)
	{
		var exception = Assert.Throws<M68kEmulationException>(() => M68kCoreFactory.Default.Create(
			model,
			new AmigaBus(),
			new M68kCoreOptions { ExecutionMode = M68kExecutionMode.Jit }));

		Assert.Contains("MC68040", exception.Message);
	}

	[Fact]
	public void PublicFactoryRejectsJitWithoutJitBusCapability()
	{
		var exception = Assert.Throws<M68kEmulationException>(() => M68kCoreFactory.Default.Create(
			M68kCpuModel.M68040,
			new PlainRamBus(64 * 1024),
			new M68kCoreOptions { ExecutionMode = M68kExecutionMode.Jit }));

		Assert.Contains(nameof(IM68kJitBus), exception.Message);
	}

	[Fact]
	public void M68040JitRunsWithGenericCopper68kJitBus()
	{
		var bus = new GenericJitRamBus(64 * 1024);
		WriteWords(bus, 0x1000, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		using var cpu = M68kCoreFactory.Default.Create(
			M68kCpuModel.M68040,
			bus,
			new M68kCoreOptions { ExecutionMode = M68kExecutionMode.Jit });
		var jit = Assert.IsType<M68kJitCore>(cpu);
		jit.Reset(0x1000, 0x2000);
		EnableM68040InstructionCache(jit);
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(jit, boundary, 1);

		Assert.Equal(0x0000_0001u, jit.State.D[0]);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void M68040JitStartsWithCacheDisabledAndUsesFallbackForRamUntilInstructionCacheIsEnabled()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();

		var interpreted = cpu.ExecuteInstructions(180, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(180, interpreted);
		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.Equal(0u, cpu.State.CacheControlRegister);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(0, cpu.Counters.TraceHits);
		Assert.Equal(180, cpu.Counters.FallbackInstructions);

		EnableM68040InstructionCache(cpu);
		ExecuteUntilTraceHits(cpu, boundary, 1);

		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
	}

	[Fact]
	public void M68040JitDoesNotTraceChipRamWhenInstructionCacheIsEnabled()
	{
		const uint chipCodeBase = 0x0000_1000;
		var bus = new AmigaBus();
		WriteWords(bus, chipCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(chipCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();

		var interpreted = cpu.ExecuteInstructions(220, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(220, interpreted);
		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.Equal(M68040InstructionCacheEnable, cpu.State.CacheControlRegister);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(0, cpu.Counters.TraceHits);
		Assert.Equal(220, cpu.Counters.FallbackInstructions);
	}

	[Fact]
	public void M68040JitAllowsRomTracesBeforeInstructionCacheIsEnabled()
	{
		const uint romBase = 0x00F8_0000;
		var bus = new AmigaBus();
		var rom = new byte[512 * 1024];
		rom[0] = 0x70; // MOVEQ #1,D0
		rom[1] = 0x01;
		rom[2] = 0x60; // BRA.S loop
		rom[3] = 0xFC;
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(romBase, 0x4000);
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(cpu, boundary, 1);

		Assert.Equal(0u, cpu.State.CacheControlRegister);
		Assert.Equal(0x0000_0001u, cpu.State.D[0]);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
	}

	[Fact]
	public void M68040JitFallbackMovecPcrProbeRaisesLineFWithoutClobberingDestination()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x4E7A, 0x1808); // MOVEC PCR,D1
		bus.WriteLong(4u * 4, 0x0000_3000);
		bus.WriteLong(11u * 4, 0x0000_2000);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		cpu.State.D[1] = 4;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(4u, cpu.State.D[1]);
		Assert.Equal(0x4000u - 8u, cpu.State.A[7]);
		Assert.Equal(RealFastCodeBase, bus.ReadLong(0x4000u - 6u));
		Assert.Equal(11 * 4, bus.ReadWord(0x4000u - 2u));
	}

	[Fact]
	public void M68040JitMaxSpeedRomDataReadLoopUsesZeroWaitReads()
	{
		const uint romBase = 0x00F8_0000;
		var bus = new AmigaBus();
		var rom = new byte[512 * 1024];
		WriteWords(
			rom,
			0,
			0x41F9, 0x00F8, 0x0100, // LEA $00F80100,A0
			0x323C, 0x03FF,         // MOVE.W #1023,D1
			0x7A00,                 // MOVEQ #0,D5
			0xDA98,                 // ADD.L (A0)+,D5
			0x51C9, 0xFFFC,         // DBRA D1,ADD.L
			0x60FE);                // BRA.S self
		for (var i = 0; i < 1024; i++)
		{
			WriteLong(rom, 0x100 + i * 4, 1);
		}

		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(romBase, 0x4000);
		var boundary = new PureBatchBoundary();

		cpu.ExecuteInstructions(10_000, 1_000_000, boundary);

		Assert.Equal(romBase + 0x12, cpu.State.ProgramCounter);
		Assert.Equal(1024u, cpu.State.D[5]);
		Assert.True(cpu.State.Cycles < 20_000, $"Expected max-speed ROM reads, cycles={cpu.State.Cycles}.");
	}

	[Fact]
	public void M68040JitDisabledMmuMemoryLoopUsesAllocationFreeZeroWaitAccesses()
	{
		const uint data = RealFastCodeBase + 0x1000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x41F9, unchecked((ushort)(data >> 16)), unchecked((ushort)data), // LEA data,A0
			0x2010, // MOVE.L (A0),D0
			0x5280, // ADDQ.L #1,D0
			0x2080, // MOVE.L D0,(A0)
			0x60F8); // BRA.S MOVE.L
		bus.WriteLong(data, 1);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);
		for (var i = 0; i < 6_000; i++)
		{
			cpu.ExecuteInstructions(64, cpu.State.Cycles + 1_000_000, boundary);
		}
		Thread.Sleep(20);
		cpu.ExecuteInstructions(20_000, cpu.State.Cycles + 1_000_000, boundary);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		cpu.ExecuteInstructions(100_000, cpu.State.Cycles + 1_000_000, boundary);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

		Assert.Equal(0, allocated);
		Assert.True(cpu.Counters.V2ZeroWaitReadRealFast > 0);
		Assert.True(cpu.Counters.V2ZeroWaitWriteRealFast > 0);
		Assert.Equal(0, cpu.Counters.V2ZeroWaitReadSlow);
		Assert.Equal(0, cpu.Counters.V2ZeroWaitWriteSlow);
	}

	[Theory]
	[InlineData(false, 0x1010, 0x1280, 1)]
	[InlineData(false, 0x3010, 0x3280, 2)]
	[InlineData(false, 0x2010, 0x2280, 4)]
	[InlineData(true, 0x1010, 0x1280, 1)]
	[InlineData(true, 0x3010, 0x3280, 2)]
	[InlineData(true, 0x2010, 0x2280, 4)]
	public void M68040JitDirectFastRamMatchesForcedSlowPath(
		bool pseudoFast,
		int readOpcode,
		int writeOpcode,
		int byteCount)
	{
		var directBus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		var slowBus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		directBus.ConfigureAutoconfigFastRamForHost();
		slowBus.ConfigureAutoconfigFastRamForHost();
		var data = (pseudoFast ? directBus.ExpansionRamBase : directBus.RealFastRamBase) + 0x1000;
		var destination = data + 0x20;
		var words = new ushort[]
		{
			(ushort)readOpcode,  // MOVE.size (A0),D0
			0x5281,              // ADDQ.L #1,D1
			(ushort)writeOpcode, // MOVE.size D0,(A1)
			0x60F8               // BRA.S start
		};
		WriteWords(directBus, RealFastCodeBase, words);
		WriteWords(slowBus, RealFastCodeBase, words);
		slowBus.RegisterHostTrapStub((data & 0x00FF_0000u) + 0xF000u, _ => { });
		var direct = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, directBus);
		var slow = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, slowBus);
		direct.Reset(RealFastCodeBase, 0x4000);
		slow.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(direct);
		EnableM68040InstructionCache(slow);
		PrepareM68040CompiledMemoryLoop(direct, data, destination);
		PrepareM68040CompiledMemoryLoop(slow, data, destination);
		var directBoundary = new PureBatchBoundary();
		var slowBoundary = new PureBatchBoundary();
		for (var index = 0; index < 250 &&
			(GetM68040DirectRamAccessCount(direct, pseudoFast) == 0 ||
				slow.Counters.V2BusAccessBatchExecutions == 0); index++)
		{
			direct.ExecuteInstructions(256, direct.State.Cycles + 100_000, directBoundary);
			slow.ExecuteInstructions(256, slow.State.Cycles + 100_000, slowBoundary);
			Thread.Sleep(1);
		}

		Assert.True(GetM68040DirectRamAccessCount(direct, pseudoFast) > 0);
		Assert.True(slow.Counters.V2BusAccessBatchExecutions > 0);
		directBus.ResetExternalDevices(0);
		slowBus.ResetExternalDevices(0);
		directBus.ConfigureAutoconfigFastRamForHost();
		slowBus.ConfigureAutoconfigFastRamForHost();
		directBus.WriteLong(data, 0x1234_5678);
		slowBus.WriteLong(data, 0x1234_5678);
		PrepareM68040CompiledMemoryLoop(direct, data, destination);
		PrepareM68040CompiledMemoryLoop(slow, data, destination);
		var slowDirectAccesses = GetM68040DirectRamAccessCount(slow, pseudoFast);
		var notifications = 0;
		var notifiedAddress = 0u;
		var notifiedByteCount = 0;
		directBus.JitEligibleMemoryWritten += (address, count) =>
		{
			notifications++;
			notifiedAddress = address;
			notifiedByteCount = count;
		};

		var measured = direct.ExecuteInstructions(512, 100_000, directBoundary);
		var slowMeasured = slow.ExecuteInstructions(512, 100_000, slowBoundary);

		Assert.Equal(measured, slowMeasured);
		Assert.Equal(slow.State.ProgramCounter, direct.State.ProgramCounter);
		Assert.Equal(slow.State.StatusRegister, direct.State.StatusRegister);
		Assert.Equal(slow.State.D, direct.State.D);
		Assert.Equal(slow.State.A, direct.State.A);
		Assert.Equal(slow.State.Cycles, direct.State.Cycles);
		Assert.Equal(
			ReadSizedValue(slowBus, destination, byteCount),
			ReadSizedValue(directBus, destination, byteCount));
		Assert.InRange(notifications, 1, measured / 4);
		Assert.Equal(destination, notifiedAddress);
		Assert.Equal(byteCount, notifiedByteCount);
		Assert.Equal(slowDirectAccesses, GetM68040DirectRamAccessCount(slow, pseudoFast));
		if (pseudoFast)
		{
			Assert.True(direct.Counters.M68040DirectPseudoFastTimingFlushes > 0);
		}

		Assert.True(direct.Counters.M68040DirectRamWriteCompletionFlushes > 0);
	}

	[Theory]
	[InlineData((int)M68040Move16CopyStrategy.UInt32, false, false)]
	[InlineData((int)M68040Move16CopyStrategy.UInt64, false, false)]
	[InlineData((int)M68040Move16CopyStrategy.Vector128, false, false)]
	[InlineData((int)M68040Move16CopyStrategy.UInt32, true, true)]
	[InlineData((int)M68040Move16CopyStrategy.UInt64, true, true)]
	[InlineData((int)M68040Move16CopyStrategy.Vector128, true, true)]
	[InlineData((int)M68040Move16CopyStrategy.UInt32, true, false)]
	[InlineData((int)M68040Move16CopyStrategy.UInt64, true, false)]
	[InlineData((int)M68040Move16CopyStrategy.Vector128, true, false)]
	[InlineData((int)M68040Move16CopyStrategy.UInt32, false, true)]
	[InlineData((int)M68040Move16CopyStrategy.UInt64, false, true)]
	[InlineData((int)M68040Move16CopyStrategy.Vector128, false, true)]
	public void M68040JitMove16DirectStrategiesMatchInterpreter(
		int strategyValue,
		bool sourcePseudoFast,
		bool destinationPseudoFast)
	{
		var strategy = (M68040Move16CopyStrategy)strategyValue;
		var directBus = new AmigaBus(expansionRamSize: 0x10000, realFastRamSize: 0x10000);
		var fallbackBus = new AmigaBus(expansionRamSize: 0x10000, realFastRamSize: 0x10000);
		var interpreterBus = new AmigaBus(expansionRamSize: 0x10000, realFastRamSize: 0x10000);
		directBus.ConfigureAutoconfigFastRamForHost();
		fallbackBus.ConfigureAutoconfigFastRamForHost();
		interpreterBus.ConfigureAutoconfigFastRamForHost();
		var source = (sourcePseudoFast ? directBus.ExpansionRamBase : directBus.RealFastRamBase) + 0x2000;
		var destination = (destinationPseudoFast ? directBus.ExpansionRamBase : directBus.RealFastRamBase) + 0x2100;
		var sourceAddress = source + 3;
		var destinationAddress = destination + 5;
		var words = CreateM68040Move16Loop(sourceAddress, destinationAddress, sourceRegister: 0, destinationRegister: 1);
		WriteWords(directBus, RealFastCodeBase, words);
		WriteWords(fallbackBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		for (var offset = 0u; offset < 16; offset++)
		{
			directBus.WriteByte(source + offset, (byte)(0x30 + offset), 0);
			fallbackBus.WriteByte(source + offset, (byte)(0x30 + offset), 0);
			interpreterBus.WriteByte(source + offset, (byte)(0x30 + offset), 0);
		}

		using var direct = M68kJitCore.CreateM68040ForTesting(directBus, enableV2: true, strategy);
		using var fallback = M68kJitCore.CreateM68040ForTesting(
			fallbackBus,
			enableV2: true,
			M68040Move16CopyStrategy.Fallback);
		using var interpreter = new M68040Interpreter(
			interpreterBus,
			M68020CpuProfile.Ocs68040JitMaxSpeed);
		direct.Reset(RealFastCodeBase, 0x4000);
		fallback.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(direct);
		EnableM68040InstructionCache(fallback);
		interpreter.State.CacheControlRegister |= M68040InstructionCacheEnable;
		_ = direct.ExecuteInstructions(800, 1_000_000, new PureBatchBoundary());
		_ = fallback.ExecuteInstructions(800, 1_000_000, new PureBatchBoundary());
		Assert.True(direct.Counters.M68040Move16DirectCopies > 0);
		Assert.True(fallback.Counters.M68040Move16Fallbacks > 0);
		for (var offset = 0u; offset < 16; offset++)
		{
			Assert.Equal((byte)(0x30 + offset), fallbackBus.ReadByte(destination + offset));
		}

		directBus.ResetExternalDevices(0);
		interpreterBus.ResetExternalDevices(0);
		directBus.ConfigureAutoconfigFastRamForHost();
		interpreterBus.ConfigureAutoconfigFastRamForHost();
		PrepareM68040Move16State(direct.State);
		PrepareM68040Move16State(interpreter.State);

		var directExecuted = direct.ExecuteInstructions(800, 1_000_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, directExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, direct.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, direct.State.StatusRegister);
		Assert.Equal(interpreter.State.D, direct.State.D);
		AssertM68040AddressRegistersEqualExceptStack(interpreter, direct);
		Assert.Equal(interpreter.State.Cycles, direct.State.Cycles);
		Assert.Equal(sourceAddress & 0xFu, direct.State.A[0] & 0xFu);
		Assert.Equal(destinationAddress & 0xFu, direct.State.A[1] & 0xFu);
		for (var offset = 0u; offset < 16; offset++)
		{
			Assert.Equal(interpreterBus.ReadByte(destination + offset), directBus.ReadByte(destination + offset));
		}

		Assert.True(direct.Counters.V2TraceHits > 0);
		Assert.True(direct.Counters.M68040Move16DirectCopies > 0);
		Assert.True(fallback.Counters.M68040Move16Fallbacks > 0);
		Assert.Equal(0, direct.Counters.M68040Move16Fallbacks);
		Assert.Equal(
			direct.Counters.M68040Move16DirectCopies,
			strategy switch
			{
				M68040Move16CopyStrategy.UInt32 => direct.Counters.M68040Move16UInt32Copies,
				M68040Move16CopyStrategy.UInt64 => direct.Counters.M68040Move16UInt64Copies,
				_ => direct.Counters.M68040Move16Vector128Copies
			});
		if (sourcePseudoFast || destinationPseudoFast)
		{
			Assert.True(direct.Counters.M68040DirectPseudoFastTimingFlushes > 0);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void M68040JitMove16SameRegisterAdvancesOnce(bool enableV2)
	{
		var bus = new AmigaBus(realFastRamSize: 0x10000);
		bus.ConfigureAutoconfigFastRamForHost();
		var source = bus.RealFastRamBase + 0x2000;
		WriteWords(bus, RealFastCodeBase, CreateM68040Move16Loop(source, source, 0, 0));
		using var cpu = M68kJitCore.CreateM68040ForTesting(
			bus,
			enableV2,
			M68040Move16CopyStrategy.UInt64);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		_ = cpu.ExecuteInstructions(800, 1_000_000, new PureBatchBoundary());
		var copies = cpu.Counters.M68040Move16DirectCopies;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.State.Cycles = 0;

		var executed = cpu.ExecuteInstructions(3, 100_000, new PureBatchBoundary());

		Assert.Equal(3, executed);
		Assert.Equal(source + 16, cpu.State.A[0]);
		Assert.True(cpu.Counters.M68040Move16DirectCopies > copies);
	}

	[Fact]
	public void M68040JitMove16UsesFallbackOutsideDirectRam()
	{
		const uint source = 0x0000_2000;
		const uint destination = 0x0000_2100;
		var bus = new AmigaBus(realFastRamSize: 0x10000);
		bus.ConfigureAutoconfigFastRamForHost();
		WriteWords(bus, RealFastCodeBase, CreateM68040Move16Loop(source, destination, 0, 1));
		for (var offset = 0u; offset < 16; offset++)
		{
			bus.WriteByte(source + offset, (byte)(0x80 + offset), 0);
		}

		using var cpu = M68kJitCore.CreateM68040ForTesting(
			bus,
			enableV2: true,
			M68040Move16CopyStrategy.Vector128);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);

		_ = cpu.ExecuteInstructions(800, 1_000_000, new PureBatchBoundary());

		for (var offset = 0u; offset < 16; offset++)
		{
			Assert.Equal((byte)(0x80 + offset), bus.ReadByte(destination + offset));
		}

		Assert.True(cpu.Counters.V2TraceHits > 0);
		Assert.True(cpu.Counters.M68040Move16Fallbacks > 0);
		Assert.Equal(0, cpu.Counters.M68040Move16DirectCopies);
	}

	[Fact]
	public void M68040JitDirectRamMapTracksOnlyCompleteUnshadowedBanks()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x18000,
			realFastRamSize: 0x20000);
		bus.ConfigureAutoconfigFastRamForHost();
		var provider = Assert.IsAssignableFrom<IM68kJitDirectRamBus>(bus);

		Assert.True(provider.TryGetJitDirectRamMap(out var map));
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.PseudoFast,
			map.BankKinds[bus.ExpansionRamBase >> map.BankShift]);
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.None,
			map.BankKinds[(bus.ExpansionRamBase + 0x10000) >> map.BankShift]);
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.RealFast,
			map.BankKinds[bus.RealFastRamBase >> map.BankShift]);
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.RealFast,
			map.BankKinds[(bus.RealFastRamBase + 0x10000) >> map.BankShift]);

		var bankKinds = map.BankKinds;
		bus.RegisterHostTrapStub(bus.ExpansionRamBase + 0x100, _ => { });

		Assert.True(provider.TryGetJitDirectRamMap(out var rebuiltMap));
		Assert.Same(bankKinds, rebuiltMap.BankKinds);
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.None,
			rebuiltMap.BankKinds[bus.ExpansionRamBase >> rebuiltMap.BankShift]);
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.RealFast,
			rebuiltMap.BankKinds[bus.RealFastRamBase >> rebuiltMap.BankShift]);
	}

	[Fact]
	public void M68040JitDirectRamAcceptsFinalByteOfCompleteBank()
	{
		var bus = new AmigaBus(realFastRamSize: 0x10000);
		bus.ConfigureAutoconfigFastRamForHost();
		var data = bus.RealFastRamBase + 0xFFFF;
		WriteWords(bus, RealFastCodeBase, 0x1010, 0x60FC); // MOVE.B (A0),D0; BRA.S MOVE
		bus.WriteByte(data, 0xA5, 0);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		cpu.State.A[0] = data;
		var boundary = new PureBatchBoundary();

		for (var index = 0; index < 250 && cpu.Counters.M68040DirectRealFastReads == 0; index++)
		{
			cpu.ExecuteInstructions(256, cpu.State.Cycles + 100_000, boundary);
			Thread.Sleep(1);
		}

		Assert.Equal(0xA5u, cpu.State.D[0] & 0xFFu);
		Assert.True(cpu.Counters.M68040DirectRealFastReads > 0);
		Assert.Equal(0, cpu.Counters.M68040DirectRamReadMisses);
	}

	[Theory]
	[InlineData(0x1001)]
	[InlineData(0xFFFE)]
	public void M68040JitDirectRamRejectsOddAndCrossBankLongAccess(int dataOffset)
	{
		var bus = new AmigaBus(realFastRamSize: 0x20000);
		bus.ConfigureAutoconfigFastRamForHost();
		var data = bus.RealFastRamBase + (uint)dataOffset;
		var code = bus.RealFastRamBase + 0x18000;
		WriteWords(bus, code, 0x2010, 0x60FC); // MOVE.L (A0),D0; BRA.S MOVE
		bus.WriteLong(data, 0x1234_5678);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(code, 0x4000);
		EnableM68040InstructionCache(cpu);
		cpu.State.A[0] = data;
		var boundary = new PureBatchBoundary();

		for (var index = 0; index < 250 && cpu.Counters.M68040DirectRamReadMisses == 0; index++)
		{
			cpu.ExecuteInstructions(256, cpu.State.Cycles + 100_000, boundary);
			Thread.Sleep(1);
		}

		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.True(cpu.Counters.M68040DirectRamReadMisses > 0);
		Assert.Equal(0, cpu.Counters.M68040DirectRealFastReads);
		if ((dataOffset & 1) != 0)
		{
			Assert.True(cpu.Counters.V2ZeroWaitReadSlow > 0);
		}
		else
		{
			Assert.True(cpu.Counters.V2ZeroWaitReadRealFast > 0);
		}
	}

	[Fact]
	public void M68040JitDirectRamRoutesOddLongWriteThroughGeneralPath()
	{
		var bus = new AmigaBus(realFastRamSize: 0x20000);
		bus.ConfigureAutoconfigFastRamForHost();
		var data = bus.RealFastRamBase + 0x1001;
		var code = bus.RealFastRamBase + 0x18000;
		WriteWords(bus, code, 0x2080, 0x5281, 0x60FA); // MOVE.L D0,(A0); ADDQ.L #1,D1; BRA.S MOVE
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(code, 0x4000);
		EnableM68040InstructionCache(cpu);
		cpu.State.A[0] = data;
		cpu.State.D[0] = 0x89AB_CDEF;
		var boundary = new PureBatchBoundary();

		for (var index = 0; index < 250 && cpu.Counters.M68040DirectRamWriteMisses == 0; index++)
		{
			cpu.ExecuteInstructions(256, cpu.State.Cycles + 100_000, boundary);
			Thread.Sleep(1);
		}

		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(data));
		Assert.True(cpu.Counters.M68040DirectRamWriteMisses > 0);
		Assert.Equal(0, cpu.Counters.M68040DirectRealFastWrites);
		Assert.True(cpu.Counters.V2ZeroWaitWriteSlow > 0);
	}

	[Fact]
	public void M68040JitTransparentDataMappingUsesZeroWaitAccesses()
	{
		const uint data = RealFastCodeBase + 0x1000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x41F9, unchecked((ushort)(data >> 16)), unchecked((ushort)data),
			0x2010,
			0x5280,
			0x2080,
			0x60F8);
		bus.WriteLong(data, 1);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.DataTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(cpu, boundary, 1);
		cpu.ExecuteInstructions(10_000, cpu.State.Cycles + 1_000_000, boundary);

		Assert.True(cpu.Counters.V2ZeroWaitReadRealFast > 0);
		Assert.True(cpu.Counters.V2ZeroWaitWriteRealFast > 0);
		Assert.Equal(0, cpu.Counters.V2ZeroWaitReadSlow);
		Assert.Equal(0, cpu.Counters.V2ZeroWaitWriteSlow);
	}

	[Fact]
	public void M68040JitWriteProtectedTransparentMappingKeepsWritesOnTranslatedPath()
	{
		const uint data = RealFastCodeBase + 0x1000;
		const uint root = 0x4000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x41F9, unchecked((ushort)(data >> 16)), unchecked((ushort)data),
			0x2010,
			0x5280,
			0x2080,
			0x60F8);
		bus.WriteLong(data, 1);
		bus.WriteLong(root + (((data >> 12) & 0x000F_FFFFu) * 4u), data | 1u);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x8000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.DataTransparentTranslation0 = 0x0000_8004;
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(cpu, boundary, 1);
		cpu.ExecuteInstructions(10_000, cpu.State.Cycles + 1_000_000, boundary);

		Assert.True(cpu.Counters.V2ZeroWaitReadRealFast > 0);
		Assert.Equal(0, cpu.Counters.V2ZeroWaitWriteRealFast);
		Assert.True(cpu.Counters.V2ZeroWaitWriteSlow > 0);
	}

	[Fact]
	public void M68040JitMmuGenerationChangeInvalidatesDisabledMmuTrace()
	{
		const uint logicalData = 0x0000_3000;
		const uint physicalData = RealFastCodeBase + 0x1000;
		const uint root = 0x4000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x2039, 0x0000, 0x3000, // MOVE.L logicalData,D0
			0x60F8); // BRA.S MOVE.L
		bus.WriteLong(logicalData, 0x1111_1111);
		bus.WriteLong(physicalData, 0x2222_2222);
		bus.WriteLong(root + 12, physicalData | 1u);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x8000);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);
		Assert.Equal(0x1111_1111u, cpu.State.D[0]);

		var guardExits = cpu.Counters.M68040MmuGenerationGuardExits;
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(0x2222_2222u, cpu.State.D[0]);
		Assert.True(cpu.Counters.M68040MmuGenerationGuardExits > guardExits);

		ExecuteUntilTraceHits(cpu, boundary, cpu.Counters.TraceHits + 1);
		guardExits = cpu.Counters.M68040MmuGenerationGuardExits;
		cpu.State.M68040Mmu.TranslationControl = 0;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(0x1111_1111u, cpu.State.D[0]);
		Assert.True(cpu.Counters.M68040MmuGenerationGuardExits > guardExits);
	}

	[Fact]
	public void M68040JitMaxSpeedCiaDelayLoopUsesFastPortAccesses()
	{
		const uint romBase = 0x00F8_0000;
		var bus = new AmigaBus();
		var rom = new byte[512 * 1024];
		WriteWords(
			rom,
			0,
			0x323C, 0x03FF,         // MOVE.W #1023,D1
			0x08F9, 0x0001,         // BSET #1,$00BFE001.L
			0x00BF, 0xE001,
			0x51C9, 0xFFF6,         // DBRA D1,BSET
			0x60FE);                // BRA.S self

		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(romBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(cpu, boundary, 1);
		var ciaAccessesBeforeMeasurement = bus.BusAccesses.Count(access => access.Request.Target == AmigaBusAccessTarget.Cia);
		var v2TraceHitsBeforeMeasurement = cpu.Counters.V2TraceHits;
		var v2BusAccessInstructionsBeforeMeasurement = cpu.Counters.V2BusAccessBatchInstructions;
		cpu.State.ProgramCounter = romBase + 4;
		cpu.State.D[1] = 0x03FF;
		cpu.ExecuteInstructions(2_048, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(romBase + 0x10, cpu.State.ProgramCounter);
		Assert.True(cpu.Counters.V2TraceHits > v2TraceHitsBeforeMeasurement);
		Assert.True(cpu.Counters.V2BusAccessBatchInstructions > v2BusAccessInstructionsBeforeMeasurement);
		Assert.False(bus.AudioFilterEnabled);
		Assert.Equal(ciaAccessesBeforeMeasurement, bus.BusAccesses.Count(access => access.Request.Target == AmigaBusAccessTarget.Cia));
	}

	[Fact]
	public void M68040JitCacheDisabledFallbackInvokesHostTrapStubs()
	{
		const uint trapAddress = RealFastCodeBase + 0x100;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x4EB9,
			unchecked((ushort)(trapAddress >> 16)),
			unchecked((ushort)trapAddress),
			0x7207); // JSR trap; MOVEQ #7,D1
		var hostHits = 0;
		bus.RegisterHostTrapStub(trapAddress, state =>
		{
			hostHits++;
			state.D[0] = 0x1234_5678;
		});
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(1, hostHits);
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(RealFastCodeBase + 6, cpu.State.ProgramCounter);
		Assert.Equal(0x4000u, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.CacheControlRegister);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(2, cpu.Counters.FallbackInstructions);
	}

	[Fact]
	public void M68040JitCompiledMoveLongAbsoluteLongToAddressDisplacementCopiesMemory()
	{
		const uint sourceAddress = 0x0000_0004;
		const uint sourceValue = 0x00C0_0DA8;
		var addressBase = RealFastCodeBase + 0x2000;
		var destinationAddress = addressBase + 0x0564;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x2779,
			0x0000,
			0x0004,
			0x0564, // MOVE.L $00000004.L,$0564(A3)
			0x60F6); // BRA.S start
		bus.WriteLong(sourceAddress, sourceValue);
		bus.WriteLong(destinationAddress, 0);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		cpu.State.A[3] = addressBase;
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(cpu, boundary, 1);

		Assert.Equal(sourceValue, bus.ReadLong(destinationAddress));
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
	}

	[Fact]
	public void M68040JitCompiledPrivilegeViolationBuildsFormat0ExceptionFrame()
	{
		const uint handler = RealFastCodeBase + 0x100;
		const uint supervisorStack = 0x0000_4000;
		const uint userStack = 0x0000_3000;
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x007C, 0x2000); // ORI.W #$2000,SR
		WriteWords(bus, handler, 0x4E71); // NOP
		bus.WriteLong(8u * 4u, handler);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, supervisorStack);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();

		for (var attempt = 0; attempt < 250 && cpu.Counters.TraceHits == 0; attempt++)
		{
			cpu.State.ResetStackPointers(supervisorStack, userStack, supervisorMode: false);
			cpu.State.StatusRegister = 0x0010;
			cpu.State.ProgramCounter = RealFastCodeBase;
			cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
			if (cpu.Counters.TraceHits == 0)
			{
				Thread.Sleep(1);
			}
		}

		Assert.True(cpu.Counters.TraceHits > 0, $"Expected compiled privilege trap, compiled={cpu.Counters.CompiledTraces}, fallback={cpu.Counters.FallbackInstructions}.");
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(8, cpu.State.LastExceptionVector);
		Assert.Equal(RealFastCodeBase, cpu.State.LastExceptionStackedProgramCounter);
		Assert.Equal(supervisorStack - 8, cpu.State.A[7]);
		Assert.Equal(0x0010, bus.ReadWord(cpu.State.A[7]));
		Assert.Equal(RealFastCodeBase, bus.ReadLong(cpu.State.A[7] + 2));
		Assert.Equal(8u * 4u, bus.ReadWord(cpu.State.A[7] + 6));
	}

	[Fact]
	public void M68040JitCompiledKickstartTaskTrapHandlerReturnsThroughTaskTrapCode()
	{
		const uint execBase = 0x0000_1000;
		const uint task = 0x0000_1800;
		const uint trapCode = RealFastCodeBase + 0x80;
		const uint stack = 0x0000_4000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x48E7, 0x00C0,       // MOVEM.L D6-D7,-(A7)
			0x2078, 0x0004,       // MOVEA.L $0004.W,A0
			0x2068, 0x0114,       // MOVEA.L $0114(A0),A0
			0x2F68, 0x0032, 0x0004, // MOVE.L $32(A0),$4(A7)
			0x205F,               // MOVEA.L (A7)+,A0
			0x4E75);              // RTS
		WriteWords(bus, trapCode, 0x7207); // MOVEQ #7,D1
		bus.WriteLong(4, execBase);
		bus.WriteLong(execBase + 0x114, task);
		bus.WriteLong(task + 0x32, trapCode);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, stack);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();

		for (var attempt = 0; attempt < 250 && cpu.Counters.TraceHits == 0; attempt++)
		{
			cpu.State.ProgramCounter = RealFastCodeBase;
			cpu.State.A[7] = stack;
			cpu.State.A[0] = 0;
			cpu.State.D[1] = 0;
			cpu.State.D[6] = 0xD600_0006;
			cpu.State.D[7] = 0xD700_0007;
			cpu.ExecuteInstructions(7, cpu.State.Cycles + 100_000, boundary);
			if (cpu.Counters.TraceHits == 0)
			{
				Thread.Sleep(1);
			}
		}

		Assert.True(cpu.Counters.TraceHits > 0, $"Expected compiled task-trap handler, compiled={cpu.Counters.CompiledTraces}, fallback={cpu.Counters.FallbackInstructions}.");
		Assert.Equal(trapCode + 2, cpu.State.ProgramCounter);
		Assert.Equal(stack, cpu.State.A[7]);
		Assert.Equal(0x0000_0007u, cpu.State.D[1]);
		Assert.Equal(trapCode, bus.ReadLong(stack - 4));
	}

	[Fact]
	public void M68040JitKeepsStaleTraceUntilCacheFlushInstruction()
	{
		const uint flushCode = RealFastCodeBase + 0x100;
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		WriteWords(bus, flushCode, 0xF500, 0x0000); // PFLUSH
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		var boundary = new PureBatchBoundary();

		ExecuteUntilTraceHits(cpu, boundary, 1);
		var compiledBeforeWrite = cpu.Counters.CompiledTraces;
		WriteWords(bus, RealFastCodeBase, 0x7002, 0x60FC); // MOVEQ #2,D0; BRA.S loop
		cpu.ExecuteInstructions(32, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(compiledBeforeWrite, cpu.Counters.CompiledTraces);

		cpu.State.ProgramCounter = flushCode;
		cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
		cpu.State.ProgramCounter = RealFastCodeBase;
		ExecuteUntilTraceHits(cpu, boundary, cpu.Counters.TraceHits + 1);

		Assert.Equal(2u, cpu.State.D[0]);
		Assert.True(cpu.Counters.Invalidations > 0);
		Assert.True(cpu.Counters.CompiledTraces > compiledBeforeWrite);
	}

	[Fact]
	public void M68040JitTranslatesInstructionFetchAndDataAccesses()
	{
		const uint logicalCode = 0x0000_1000;
		const uint physicalCode = RealFastCodeBase;
		const uint physicalData = RealFastCodeBase + 0x1000;
		const uint root = 0x4000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			physicalCode,
			0x2039, 0x0000, 0x3000, // MOVE.L $3000.L,D0
			0x60F8); // BRA.S logicalCode
		bus.WriteLong(physicalData, 0xCAFE_BABEu);
		bus.WriteLong(root + 4, physicalCode | 1u);
		bus.WriteLong(root + 12, physicalData | 1u);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(logicalCode, 0x8000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;

		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);

		Assert.Equal(0xCAFE_BABEu, cpu.State.D[0]);
		Assert.Equal(logicalCode, cpu.State.ProgramCounter);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
		Assert.True(cpu.Counters.V2ZeroWaitReadSlow > 0);
		Assert.Equal(0, cpu.Counters.V2ZeroWaitReadRealFast);
	}

	[Fact]
	public void M68040JitTranslatedHostTrapRootUsesFallbackInsteadOfTrace()
	{
		const uint logicalTrap = 0x0000_1000;
		const uint physicalTrap = RealFastCodeBase;
		const uint root = 0x4000;
		var bus = CreateRealFastCodeBus();
		var hostHits = 0;
		bus.RegisterHostTrapStub(physicalTrap, state =>
		{
			hostHits++;
			state.ProgramCounter = logicalTrap + 8;
		});
		bus.WriteLong(root + 4, physicalTrap | 1u);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(logicalTrap, 0x8000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;

		var executed = cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, new PureBatchBoundary());

		Assert.Equal(1, executed);
		Assert.Equal(1, hostHits);
		Assert.Equal(logicalTrap + 8, cpu.State.ProgramCounter);
		Assert.Equal(0, cpu.Counters.CompiledTraces);
		Assert.Equal(0, cpu.Counters.TraceHits);
		Assert.Equal(1, cpu.Counters.FallbackInstructions);
	}

	[Fact]
	public void M68040JitCompiledDataMmuFaultUsesFallbackExceptionState()
	{
		const uint handler = RealFastCodeBase + 0x100;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0x2039, 0x0000, 0x3000, // MOVE.L $3000.L,D0
			0x60F8); // BRA.S loop
		WriteWords(bus, handler, 0x4E71); // NOP
		bus.WriteLong(0x0000_3000, 0x1234_5678u);
		bus.WriteLong(2 * 4, handler);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x8000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.DataTransparentTranslation0 = 0x0000_8000;
		cpu.State.M68040Mmu.SupervisorRootPointer = 0x4000;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;
		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);

		var sideExitsBeforeFault = cpu.Counters.SideExits;
		var traceHitsBeforeFault = cpu.Counters.TraceHits;
		cpu.State.M68040Mmu.DataTransparentTranslation0 = 0;
		cpu.State.M68040Mmu.SupervisorRootPointer = 0;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);

		Assert.True(cpu.Counters.TraceHits > traceHitsBeforeFault);
		Assert.True(cpu.Counters.SideExits > sideExitsBeforeFault);
		Assert.NotEqual(0u, cpu.State.M68040Mmu.Status);
		Assert.Equal(handler, cpu.State.ProgramCounter);
	}

	[Fact]
	public void M68040JitFallbackFetchBeyondConfiguredChipRamRaisesBusErrorInsteadOfMirroring()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		bus.WriteLong(2 * 4, 0x0000_2400);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(0x0010_0004, 0x8000);
		EnableM68040InstructionCache(cpu);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.Counters.CompiledTraces);
		Assert.Equal(1, cpu.Counters.FallbackInstructions);
		Assert.Equal(0x0010_0004u, bus.ReadLong(0x8000u - 6u));
	}

	[Fact]
	public void M68040JitExecutesFpuInstructionsThroughSharedM68040State()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0xF200, 0x4080, // FMOVE.L D0,FP1
			0xF200, 0x00A2, // FADD.X FP0,FP1
			0x60F6); // BRA.S first FMOVE
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		cpu.State.D[0] = 3;
		cpu.State.M68040Fpu.FP[0] = F80(2.5);

		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);

		var fpuFallbackAfterTraceInstall = cpu.Counters.M68040FpuFallbackInstructions;
		var nativeFpuBeforeCompiledRun = cpu.Counters.NativeM68040FpuIlInstructions;
		var sawAddResult = false;
		for (var i = 0; i < 32 && !sawAddResult; i++)
		{
			cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);
			sawAddResult = Math.Abs(F64(cpu.State.M68040Fpu.FP[1]) - 5.5) < 0.00000001;
		}

		Assert.True(sawAddResult);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
		Assert.True(cpu.Counters.NativeM68040FpuIlInstructions > nativeFpuBeforeCompiledRun);
		Assert.Equal(fpuFallbackAfterTraceInstall, cpu.Counters.M68040FpuFallbackInstructions);
	}

	[Fact]
	public void M68040JitNativeFpuMatchesInterpreterForRegisterControlAndArithmeticOps()
	{
		var words = new ushort[]
		{
			0xF200, 0xB000, // FMOVE.L D0,FPCR
			0xF201, 0x9000, // FMOVE.L FPCR,D1
			0xF202, 0x4480, // FMOVE.S D2,FP1
			0xF200, 0x4080, // FMOVE.L D0,FP1
			0xF201, 0x4100, // FMOVE.L D1,FP2
			0xF200, 0x0504, // FSQRT.X FP1,FP2
			0xF200, 0x0D98, // FABS.X FP3,FP3
			0xF200, 0x021A, // FNEG.X FP0,FP4
			0xF200, 0x00A0, // FDIV.X FP0,FP1
			0xF200, 0x00A2, // FADD.X FP0,FP1
			0xF200, 0x00A3, // FMUL.X FP0,FP1
			0xF200, 0x00A8, // FSUB.X FP0,FP1
			0xF200, 0x00B8, // FCMP.X FP0,FP1
			0xF200, 0x043A, // FTST.X FP1
			0x60C6          // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(
			words,
			state =>
			{
				state.D[0] = 4;
				state.D[1] = 16;
				state.D[2] = unchecked((uint)BitConverter.SingleToInt32Bits(1.25f));
				state.M68040Fpu.FP[0] = F80(2.0);
				state.M68040Fpu.FP[3] = F80(-8.0);
			});

		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > 0);
		Assert.Equal(result.Interpreter.State.D, result.Jit.State.D);
		AssertM68040AddressRegistersEqualExceptStack(result.Interpreter, result.Jit);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpcr, result.Jit.State.M68040Fpu.Fpcr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpiar, result.Jit.State.M68040Fpu.Fpiar);
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void M68040JitSpecializedFpuRegisterHelperMatchesInterpreterForClassicAndV2(bool enableV2)
	{
		var words = new ushort[]
		{
			0xF200, 0x00A2, // FADD.X FP0,FP1
			0xF200, 0x00A8, // FSUB.X FP0,FP1
			0xF200, 0x00A3, // FMUL.X FP0,FP1
			0xF200, 0x00A0, // FDIV.X FP0,FP1
			0x60EE          // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		var interpreter = new M68040Interpreter(
			interpreterBus,
			M68020CpuProfile.Ocs68040Accelerator25Mhz);
		using var jit = M68kJitCore.CreateM68040ForTesting(jitBus, enableV2);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.M68040Fpu.FP[0] = jit.State.M68040Fpu.FP[0] = F80(2.0);
		interpreter.State.M68040Fpu.FP[1] = jit.State.M68040Fpu.FP[1] = F80(8.0);
		EnableM68040InstructionCache(jit);

		var boundary = new PureBatchBoundary();
		var compiled = 0;
		for (var i = 0; i < 250; i++)
		{
			compiled += jit.ExecuteInstructions(64, jit.State.Cycles + 100_000, boundary);
			var hasTraceHit = enableV2
				? jit.Counters.V2TraceHits > 0
				: jit.Counters.TraceHits > 0;
			if (hasTraceHit && jit.Counters.NativeM68040FpuIlInstructions > 0)
			{
				break;
			}
		}

		var interpreted = interpreter.ExecuteInstructions(compiled, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.M68040Fpu.Fpsr, jit.State.M68040Fpu.Fpsr);
		Assert.Equal(interpreter.State.M68040Fpu.Fpiar, jit.State.M68040Fpu.Fpiar);
		AssertM68040FpRegistersEqual(interpreter, jit);
		Assert.True(
			jit.Counters.NativeM68040FpuIlInstructions > 0,
			$"compiled={jit.Counters.CompiledTraces}, traceHits={jit.Counters.TraceHits}, " +
			$"v2Hits={jit.Counters.V2TraceHits}, fallback={jit.Counters.FallbackInstructions}");
		Assert.True(
			enableV2 ? jit.Counters.V2TraceHits > 0 : jit.Counters.TraceHits > 0,
			$"compiled={jit.Counters.CompiledTraces}, traceHits={jit.Counters.TraceHits}, " +
			$"v2Hits={jit.Counters.V2TraceHits}");
	}

	[Theory]
	[InlineData(0x0000u)]
	[InlineData(0x0010u)]
	[InlineData(0x0020u)]
	[InlineData(0x0030u)]
	[InlineData(0x0040u)]
	[InlineData(0x0050u)]
	[InlineData(0x0060u)]
	[InlineData(0x0070u)]
	[InlineData(0x0080u)]
	[InlineData(0x0090u)]
	[InlineData(0x00A0u)]
	[InlineData(0x00B0u)]
	public void M68040JitSpecializedFpuRegisterHelpersMatchInterpreterAcrossFpcrContexts(uint fpcr)
	{
		var words = new ushort[]
		{
			0xF200, 0x00A2, // FADD.X FP0,FP1
			0xF200, 0x00A8, // FSUB.X FP0,FP1
			0xF200, 0x00A3, // FMUL.X FP0,FP1
			0xF200, 0x00A0, // FDIV.X FP0,FP1
			0x60EE          // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(
			words,
			state =>
			{
				state.M68040Fpu.Fpcr = fpcr;
				state.M68040Fpu.FP[0] = ExtF80.FromBits(0x3FFF, 0x8000_0000_0000_0001);
				state.M68040Fpu.FP[1] = ExtF80.FromBits(0x4000, 0x8000_0000_0000_0003);
			});

		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpcr, result.Jit.State.M68040Fpu.Fpcr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpiar, result.Jit.State.M68040Fpu.Fpiar);
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > 0);
	}

	[Theory]
	[InlineData((ushort)0x00C1)] // FSQRT.S FP0,FP1
	[InlineData((ushort)0x00C5)] // FSQRT.D FP0,FP1
	[InlineData((ushort)0x00E2)] // FADD.S FP0,FP1
	[InlineData((ushort)0x00E8)] // FSUB.S FP0,FP1
	[InlineData((ushort)0x00E3)] // FMUL.S FP0,FP1
	[InlineData((ushort)0x00E0)] // FDIV.S FP0,FP1
	[InlineData((ushort)0x00E6)] // FADD.D FP0,FP1
	[InlineData((ushort)0x00EC)] // FSUB.D FP0,FP1
	[InlineData((ushort)0x00E7)] // FMUL.D FP0,FP1
	[InlineData((ushort)0x00E4)] // FDIV.D FP0,FP1
	public void M68040JitSpecializedForcedPrecisionOperationMatchesInterpreter(ushort command)
	{
		var result = ExecuteM68040FpuComparison(
			[
				0xF200, 0x0080, // FMOVE.X FP0,FP1
				0xF200, command,
				0x60F6          // BRA.S start
			],
			state =>
			{
				state.M68040Fpu.FP[0] = ExtF80Math.FromInt32(3);
				state.M68040Fpu.FP[1] = ExtF80Math.FromInt32(1);
			});

		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpiar, result.Jit.State.M68040Fpu.Fpiar);
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
	}

	[Theory]
	[InlineData(0x1000u)]
	[InlineData(0x0004u)]
	public void M68040JitSpecializedFpuExceptionRestoresV2RegisterStateBeforeSideExit(uint handlerOffset)
	{
		var handler = RealFastCodeBase + handlerOffset;
		var words = new ushort[]
		{
			0xF200, 0x00A0, // FDIV.X FP0,FP1
			0x60FA          // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		interpreterBus.WriteLong(50u * 4, handler);
		jitBus.WriteLong(50u * 4, handler);
		var interpreter = new M68040Interpreter(interpreterBus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		using var jit = M68kJitCore.CreateM68040ForTesting(jitBus, enableV2: true);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.M68040Fpu.FP[0] = jit.State.M68040Fpu.FP[0] = ExtF80Math.FromInt32(1);
		interpreter.State.M68040Fpu.FP[1] = jit.State.M68040Fpu.FP[1] = ExtF80Math.FromInt32(1);
		EnableM68040InstructionCache(jit);
		ExecuteUntilTraceHits(jit, new PureBatchBoundary(), 1);

		interpreter.State.M68040Fpu.FP[0] = jit.State.M68040Fpu.FP[0] = ExtF80.PositiveZero;
		interpreter.State.M68040Fpu.Fpcr = jit.State.M68040Fpu.Fpcr = M68040FpuState.ExceptionDivideByZero;
		interpreter.State.ProgramCounter = jit.State.ProgramCounter = RealFastCodeBase;

		interpreter.ExecuteInstruction();
		jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new PureBatchBoundary());

		Assert.Equal(handler, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreter.State.M68040Fpu.Fpsr, jit.State.M68040Fpu.Fpsr);
		Assert.Equal(interpreter.State.M68040Fpu.Fpiar, jit.State.M68040Fpu.Fpiar);
		AssertM68040FpRegistersEqual(interpreter, jit);
	}

	[Fact]
	public void M68040JitSpecializedFpuExceptionCommitsPriorV2AddressRegisterWriteback()
	{
		const uint handler = RealFastCodeBase + 0x1000;
		var words = new ushort[]
		{
			0x598F,          // SUBQ.L #4,A7
			0xF200, 0x00A0, // FDIV.X FP0,FP1
			0x60F8          // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		interpreterBus.WriteLong(50u * 4, handler);
		jitBus.WriteLong(50u * 4, handler);
		var interpreter = new M68040Interpreter(interpreterBus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		using var jit = M68kJitCore.CreateM68040ForTesting(jitBus, enableV2: true);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.M68040Fpu.FP[0] = jit.State.M68040Fpu.FP[0] = ExtF80Math.FromInt32(1);
		interpreter.State.M68040Fpu.FP[1] = jit.State.M68040Fpu.FP[1] = ExtF80Math.FromInt32(1);
		EnableM68040InstructionCache(jit);
		ExecuteUntilTraceHits(jit, new PureBatchBoundary(), 1);

		interpreter.State.A[7] = jit.State.A[7] = 0x4000;
		interpreter.State.M68040Fpu.FP[0] = jit.State.M68040Fpu.FP[0] = ExtF80.PositiveZero;
		interpreter.State.M68040Fpu.Fpcr = jit.State.M68040Fpu.Fpcr = M68040FpuState.ExceptionDivideByZero;
		interpreter.State.ProgramCounter = jit.State.ProgramCounter = RealFastCodeBase;

		interpreter.ExecuteInstruction();
		interpreter.ExecuteInstruction();
		jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, new PureBatchBoundary());

		Assert.Equal(handler, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreter.State.M68040Fpu.Fpsr, jit.State.M68040Fpu.Fpsr);
		AssertM68040FpRegistersEqual(interpreter, jit);
	}

	[Fact]
	public void M68040JitNativeFpuMatchesInterpreterForFsglAndUnaryOverwrite()
	{
		var words = new ushort[]
		{
			0xF203, 0x43DC, // FDABS.L D3,FP7
			0xF225, 0x00A7, // FSGLMUL.X FP0,FP1
			0xF200, 0x1BA4, // FSGLDIV.X FP6,FP7
			0x60F2          // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(
			words,
			state =>
			{
				state.D[3] = 0x02C5_6A5A;
				state.M68040Fpu.FP[0] = ExtF80.FromBits(0x41EE, 0xF794_47F9_9200_51CC);
				state.M68040Fpu.FP[1] = ExtF80.FromBits(0x0F28, 0x8795_E9E6_EF87_E2F2);
				state.M68040Fpu.FP[6] = ExtF80.FromBits(0x1341, 0xA489_0933_B04A_5764);
				state.M68040Fpu.FP[7] = ExtF80.FromBits(0x081D, 0x270A_F833_7990_6295);
			},
			instructions: 120);

		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > 0);
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpiar, result.Jit.State.M68040Fpu.Fpiar);
	}

	[Fact]
	public void M68040JitNativeFpuMatchesInterpreterForPostincrementAndPredecrementEas()
	{
		const uint sourceAddress = RealFastCodeBase + 0x2000;
		const uint destinationEnd = RealFastCodeBase + 0x3000;
		var words = new ushort[]
		{
			0xF218, 0x4080, // FMOVE.L (A0)+,FP1
			0xF221, 0x6080, // FMOVE.L FP1,-(A1)
			0x60F6          // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(
			words,
			state =>
			{
				state.A[0] = sourceAddress;
				state.A[1] = destinationEnd;
			},
			(interpreterBus, jitBus) =>
			{
				interpreterBus.WriteLong(sourceAddress, 42);
				jitBus.WriteLong(sourceAddress, 42);
			},
			instructions: 60);

		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > 0);
		Assert.Equal(result.Interpreter.State.A[0], result.Jit.State.A[0]);
		Assert.Equal(result.Interpreter.State.A[1], result.Jit.State.A[1]);
		Assert.Equal(
			result.InterpreterBus.ReadLong(result.Interpreter.State.A[1]),
			result.JitBus.ReadLong(result.Jit.State.A[1]));
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
	}

	[Fact]
	public void M68040JitFallbackFpuConditionalsMatchInterpreter()
	{
		var words = new ushort[]
		{
			0xF28F, 0x0006,       // FBT.W +6
			0x7001,               // MOVEQ #1,D0 (skipped)
			0x4E71,               // NOP (skipped)
			0xF240, 0x000F,       // FST D0
			0xF249, 0x0000, 0xFFF8 // FDBF D1,-8
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		var interpreter = new M68040Interpreter(interpreterBus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		var jit = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, jitBus);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.D[1] = jit.State.D[1] = 1;
		EnableM68040InstructionCache(jit);

		var interpreted = interpreter.ExecuteInstructions(3, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(3, jit.State.Cycles + 100_000, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.M68040Fpu.Fpsr, jit.State.M68040Fpu.Fpsr);
	}

	[Theory]
	[MemberData(nameof(M68040V2FpuConditionalLoops))]
	public void M68040JitV2FpuConditionalLoopsExecuteNatively(ushort[] words, uint data0)
	{
		var result = ExecuteM68040FpuComparison(
			words,
			state => state.D[0] = data0,
			instructions: 2_000);

		Assert.True(result.Jit.Counters.V2TraceHits > 0);
		Assert.True(result.Jit.Counters.M68040NativeFpuConditionSites > 0);
		Assert.True(result.Jit.Counters.M68040FpuFallbackInstructions < 128,
			$"FPU fallback count was {result.Jit.Counters.M68040FpuFallbackInstructions}.");
		Assert.Equal(result.Interpreter.State.D, result.Jit.State.D);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void M68040JitV2FpuConditionMatrixMatchesInterpreter()
	{
		uint[] statuses =
		[
			0,
			M68040FpuState.ConditionZero,
			M68040FpuState.ConditionNegative,
			M68040FpuState.ConditionNan,
			M68040FpuState.ConditionInfinity,
			M68040FpuState.ConditionInfinity | M68040FpuState.ConditionNegative
		];

		for (var condition = 0; condition < 32; condition++)
		{
			var words = new ushort[]
			{
				0xF240, (ushort)condition, // FScc D0
				0x60FA                    // BRA.S start
			};
			foreach (var status in statuses)
			{
				var result = ExecuteM68040FpuComparison(
					words,
					state =>
					{
						state.D[0] = 0xA5A5_5A5A;
						state.M68040Fpu.Fpsr = status;
					},
					instructions: 80);

				Assert.Equal(result.Interpreter.State.D[0], result.Jit.State.D[0]);
				Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
				Assert.Equal(result.Interpreter.State.ProgramCounter, result.Jit.State.ProgramCounter);
			}
		}
	}

	[Fact]
	public void M68040JitV2CompareAndTestFeedNativeConditionals()
	{
		var words = new ushort[]
		{
			0xF200, 0x08B8, // FCMP.X FP2,FP1
			0xF240, 0x0001, // FSEQ D0
			0xF200, 0x0C3A, // FTST.X FP3
			0xF240, 0x0004, // FSLT D0
			0x60EE          // BRA.S start
		};
		var result = ExecuteM68040FpuComparison(
			words,
			state =>
			{
				state.M68040Fpu.FP[1] = ExtF80Math.FromInt32(3);
				state.M68040Fpu.FP[2] = ExtF80Math.FromInt32(3);
				state.M68040Fpu.FP[3] = ExtF80Math.FromInt32(-1);
			},
			instructions: 2_000);

		Assert.True(result.Jit.Counters.V2TraceHits > 0);
		Assert.Equal(result.Interpreter.State.D, result.Jit.State.D);
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpiar, result.Jit.State.M68040Fpu.Fpiar);
	}

	public static TheoryData<ushort[], uint> M68040V2FpuConditionalLoops => new()
	{
		{ [0xF28F, 0xFFFE], 0u },                         // FBT.W self
		{ [0xF2CF, 0xFFFF, 0xFFFE], 0u },                 // FBT.L self
		{ [0x7005, 0xF248, 0x0000, 0xFFFC, 0x60F6], 0u }, // MOVEQ/FDBF/BRA
		{ [0xF240, 0x000F, 0x60FA], 0u },                 // FST D0/BRA
		{ [0xF27C, 0x0000, 0x60FA], 0u }                  // FTRAPF/BRA
	};

	[Fact]
	public void M68040JitV2FtrapccTrueConditionUsesArchitecturalTrapPath()
	{
		const uint handler = RealFastCodeBase + 0x1200;
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase,
			0xF27C, 0x0001, // FTRAPEQ
			0x60FA);        // BRA.S start
		bus.WriteLong(7u * 4, handler);
		var jit = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		jit.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(jit);
		ExecuteUntilTraceHits(jit, new PureBatchBoundary(), 1);

		jit.State.M68040Fpu.Fpsr = M68040FpuState.ConditionZero;
		jit.State.ProgramCounter = RealFastCodeBase;
		jit.ExecuteInstructions(1, jit.State.Cycles + 100_000, new CountingBoundary());

		Assert.Equal(handler, jit.State.ProgramCounter);
		Assert.True(jit.Counters.M68040FpuTrapSlowPathExits > 0);
	}

	[Fact]
	public void M68040JitV2SignalingUnorderedConditionPreservesPriorOperationBeforeBsun()
	{
		const uint handler = RealFastCodeBase + 0x1000;
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase,
			0xF200, 0x00A2, // FADD.X FP0,FP1
			0xF200, 0x08B8, // FCMP.X FP2,FP1
			0xF298, 0xFFF6); // FBSUN.W start
		bus.WriteLong(48u * 4, handler);
		var jit = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		jit.Reset(RealFastCodeBase, 0x4000);
		jit.State.M68040Fpu.FP[0] = ExtF80Math.FromInt32(1);
		jit.State.M68040Fpu.FP[1] = ExtF80Math.FromInt32(2);
		jit.State.M68040Fpu.FP[2] = ExtF80.FromBits(0x7FFF, 0xC000_0000_0000_0001);
		EnableM68040InstructionCache(jit);

		ExecuteUntilTraceHits(jit, new PureBatchBoundary(), 1);
		jit.State.M68040Fpu.Fpcr = M68040FpuState.ExceptionBranchUnordered;
		jit.State.M68040Fpu.FP[1] = ExtF80Math.FromInt32(2);
		jit.State.M68040Fpu.Fpsr = 0;
		jit.State.ProgramCounter = RealFastCodeBase;
		jit.ExecuteInstructions(3, jit.State.Cycles + 100_000, new CountingBoundary());

		Assert.Equal(handler, jit.State.ProgramCounter);
		Assert.Equal(ExtF80Math.FromInt32(3), jit.State.M68040Fpu.FP[1]);
		Assert.NotEqual(0u, jit.State.M68040Fpu.Fpsr & M68040FpuState.ExceptionBranchUnordered);
		Assert.NotEqual(0u, jit.State.M68040Fpu.Fpsr & M68040FpuState.AccruedInvalid);
		Assert.True(jit.Counters.M68040FpuBsunSlowPathExits > 0);
	}

	[Fact]
	public void M68040JitNativeFpuMatchesInterpreterForIntegerBitDontCareInfinity()
	{
		var words = new ushort[]
		{
			0xF206, 0x12E0, // FSDIV.X FP4,FP5
			0x60FA          // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(
			words,
			state =>
			{
				state.M68040Fpu.FP[4] = ExtF80.FromBits(0x7FFF, 0);
				state.M68040Fpu.FP[5] = ExtF80.FromBits(0xFFFF, 0);
			});

		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > 0);
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
		Assert.Equal(result.Interpreter.State.M68040Fpu.Fpsr, result.Jit.State.M68040Fpu.Fpsr);
	}

	[Fact]
	public void M68040JitNativeFpuMatchesInterpreterForImmediateAndMemoryFormats()
	{
		const uint singleAddress = RealFastCodeBase + 0x1000;
		const uint doubleAddress = RealFastCodeBase + 0x1100;
		var words = new ushort[]
		{
			0xF23C, 0x5500, 0x400C, 0x0000, 0x0000, 0x0000, // FMOVE.D #3.5,FP2
			0xF239, 0x7500, 0x0020, 0x1100,                 // FMOVE.D FP2,$201100.L
			0xF239, 0x4480, 0x0020, 0x1000,                 // FMOVE.S $201000.L,FP1
			0xF202, 0x6480,                                 // FMOVE.S FP1,D2
			0x60DE                                          // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(
			words,
			state => state.D[2] = 0,
			(interpreterBus, jitBus) =>
			{
				interpreterBus.WriteLong(singleAddress, unchecked((uint)BitConverter.SingleToInt32Bits(-2.25f)));
				jitBus.WriteLong(singleAddress, unchecked((uint)BitConverter.SingleToInt32Bits(-2.25f)));
			});

		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > 0);
		Assert.Equal(result.Interpreter.State.D, result.Jit.State.D);
		Assert.Equal(result.InterpreterBus.ReadLong(doubleAddress), result.JitBus.ReadLong(doubleAddress));
		Assert.Equal(result.InterpreterBus.ReadLong(doubleAddress + 4), result.JitBus.ReadLong(doubleAddress + 4));
		AssertM68040FpRegistersEqual(result.Interpreter, result.Jit);
	}

	[Fact]
	public void M68040JitNativeFmoveTakesNonMaskableIntegerOperandError()
	{
		const uint handler = RealFastCodeBase + 0x1000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0xF202, 0x6080, // FMOVE.L FP1,D2
			0x60FA);        // BRA.S start
		WriteWords(bus, handler, 0x60FE);
		bus.WriteLong(52u * 4, handler);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Fpu.FP[1] = ExtF80Math.FromInt32(1);

		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);
		var nativeBefore = cpu.Counters.NativeM68040FpuIlInstructions;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.State.D[2] = 0xA5A5_5A5A;
		cpu.State.M68040Fpu.Fpcr = 0;
		cpu.State.M68040Fpu.FP[1] = ExtF80.QuietNaN;

		cpu.ExecuteInstructions(1, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(0xC000_0000u, cpu.State.D[2]);
		Assert.NotEqual(0u, cpu.State.M68040Fpu.Fpsr & M68040FpuState.ExceptionOperandError);
		Assert.True(cpu.Counters.NativeM68040FpuIlInstructions > nativeBefore);
	}

	[Fact]
	public void M68040JitNativeFpuMatchesInterpreterForFsaveAndFrestore()
	{
		var words = new ushort[]
		{
			0xF327, // FSAVE -(A7)
			0xF35F, // FRESTORE (A7)+
			0x60FA  // BRA.S start
		};

		var result = ExecuteM68040FpuComparison(words, _ => { }, instructions: 200);

		Assert.Equal(result.Interpreter.State.A[7], result.Jit.State.A[7]);
		Assert.Equal(M68040FpuHelpers.NullStateFrame, result.InterpreterBus.ReadLong(0x4000 - M68040FpuHelpers.NullStateFrameSize));
		Assert.Equal(
			result.InterpreterBus.ReadLong(0x4000 - M68040FpuHelpers.NullStateFrameSize),
			result.JitBus.ReadLong(0x4000 - M68040FpuHelpers.NullStateFrameSize));
		var fpuFallbackAfterTraceInstall = result.Jit.Counters.M68040FpuFallbackInstructions;
		var nativeFpuBeforeCompiledRun = result.Jit.Counters.NativeM68040FpuIlInstructions;
		result.Jit.ExecuteInstructions(32, result.Jit.State.Cycles + 100_000, new PureBatchBoundary());

		Assert.True(result.Jit.Counters.NativeM68040FpuIlInstructions > nativeFpuBeforeCompiledRun);
		Assert.Equal(fpuFallbackAfterTraceInstall, result.Jit.Counters.M68040FpuFallbackInstructions);
	}

	[Fact]
	public void M68040JitNativeFpuUsesTranslatedMemoryHelpers()
	{
		const uint logicalCode = 0x0000_1000;
		const uint physicalCode = RealFastCodeBase;
		const uint physicalData = RealFastCodeBase + 0x1000;
		const uint root = 0x4000;
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			physicalCode,
			0xF239, 0x4480, 0x0000, 0x3000, // FMOVE.S $3000.L,FP1
			0xF239, 0x6480, 0x0000, 0x3004, // FMOVE.S FP1,$3004.L
			0x60EE);                        // BRA.S logicalCode
		bus.WriteLong(physicalData, unchecked((uint)BitConverter.SingleToInt32Bits(6.25f)));
		bus.WriteLong(root + 4, physicalCode | 1u);
		bus.WriteLong(root + 12, physicalData | 1u);
		var cpu = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, bus);
		cpu.Reset(logicalCode, 0x8000);
		EnableM68040InstructionCache(cpu);
		cpu.State.M68040Mmu.SupervisorRootPointer = root;
		cpu.State.M68040Mmu.TranslationControl = 0x8000_0000;

		var boundary = new PureBatchBoundary();
		ExecuteUntilTraceHits(cpu, boundary, 1);

		var fpuFallbackAfterTraceInstall = cpu.Counters.M68040FpuFallbackInstructions;
		var nativeFpuBeforeCompiledRun = cpu.Counters.NativeM68040FpuIlInstructions;
		cpu.State.ProgramCounter = logicalCode;
		cpu.ExecuteInstructions(15, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(unchecked((uint)BitConverter.SingleToInt32Bits(6.25f)), bus.ReadLong(physicalData + 4));
		Assert.Equal(logicalCode, cpu.State.ProgramCounter);
		Assert.True(cpu.Counters.NativeM68040FpuIlInstructions > nativeFpuBeforeCompiledRun);
		Assert.Equal(fpuFallbackAfterTraceInstall, cpu.Counters.M68040FpuFallbackInstructions);
	}

	[Fact]
	public void M68040JitDecoderClassifiesFpuOpsBeforeFallback()
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(
			bus,
			RealFastCodeBase,
			0xF200, 0x00A2, // FADD.X FP0,FP1
			0xF200, 0x4078, // unsupported 68881-style opmode
			0xF200, 0x4001, // recognized unimplemented FINT.L D0,FP0
			0xF327,         // FSAVE -(A7)
			0xF35F);        // FRESTORE (A7)+

		Assert.True(M68kDecoder.TryDecode(
			bus,
			RealFastCodeBase,
			out var supported,
			out var supportedReason,
			M68kJitCpuModel.M68040));
		Assert.Equal(M68kJitBailoutReason.None, supportedReason);
		Assert.Equal(M68kJitOperation.M68040Fpu, supported.Operation);
		Assert.Equal((int)M68040FpuJitKind.Operation, supported.Variant);

		Assert.True(M68kDecoder.TryDecode(
			bus,
			RealFastCodeBase + 4,
			out var unsupported,
			out var unsupportedReason,
			M68kJitCpuModel.M68040));
		Assert.Equal(M68kJitBailoutReason.None, unsupportedReason);
		Assert.Equal(M68kJitOperation.M68040Fpu, unsupported.Operation);
		Assert.Equal((int)M68040FpuJitKind.LineFTrap, unsupported.Variant);

		Assert.True(M68kDecoder.TryDecode(
			bus,
			RealFastCodeBase + 8,
			out var unimplemented,
			out var unimplementedReason,
			M68kJitCpuModel.M68040));
		Assert.Equal(M68kJitBailoutReason.None, unimplementedReason);
		Assert.Equal(M68kJitOperation.M68040Fpu, unimplemented.Operation);
		Assert.Equal((int)M68040FpuJitKind.UnimplementedOperation, unimplemented.Variant);

		Assert.True(M68kDecoder.TryDecode(
			bus,
			RealFastCodeBase + 12,
			out var saveState,
			out var saveStateReason,
			M68kJitCpuModel.M68040));
		Assert.Equal(M68kJitBailoutReason.None, saveStateReason);
		Assert.Equal(M68kJitOperation.M68040Fpu, saveState.Operation);
		Assert.Equal((int)M68040FpuJitKind.SaveState, saveState.Variant);

		Assert.True(M68kDecoder.TryDecode(
			bus,
			RealFastCodeBase + 14,
			out var restoreState,
			out var restoreStateReason,
			M68kJitCpuModel.M68040));
		Assert.Equal(M68kJitBailoutReason.None, restoreStateReason);
		Assert.Equal(M68kJitOperation.M68040Fpu, restoreState.Operation);
		Assert.Equal((int)M68040FpuJitKind.RestoreState, restoreState.Variant);
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
		bus.RegisterHostTrapStub(RealFastCodeBase, _ => { });
		Assert.True(bus.TryCaptureJitCodeSnapshot(RealFastCodeBase, 16, out var snapshot));
		var reader = new M68kSnapshotCodeReader(snapshot);

		Assert.False(M68kDecoder.TryDecode(reader, RealFastCodeBase, out _, out var reason));

		Assert.Equal(M68kJitBailoutReason.ExceptionInstruction, reason);
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
		var cpu = new M68kJitCore(bus, enableV2: false);
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
	public void M68000JitCompiledMoveToCcrPreservesPostincrementOnAddressError()
	{
		var bus = CreateCodeBus();
		var data = FastCodeBase + 0x2000;
		WriteWords(bus, FastCodeBase, 0x44D8, 0x60FC); // MOVE (A0)+,CCR; BRA.S loop
		for (var i = 0; i < 128; i++)
		{
			bus.WriteWord(data + (uint)(i * 2), 0x0015);
		}

		bus.WriteLong(3 * 4, 0x4000);
		using var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: true);
		cpu.Reset(FastCodeBase, 0x5000);
		cpu.State.A[0] = data;
		_ = cpu.ExecuteInstructions(160, null, new CountingBoundary());
		Assert.True(cpu.Counters.TraceHits > 0);

		cpu.State.ProgramCounter = FastCodeBase;
		cpu.State.A[0] = data;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		var alignedStartCycles = cpu.State.Cycles;
		var alignedTraceHits = cpu.Counters.TraceHits;
		Assert.Equal(1, cpu.ExecuteInstructions(1, null, new CountingBoundary()));
		Assert.True(cpu.Counters.TraceHits > alignedTraceHits);
		Assert.Equal(alignedStartCycles + 16, cpu.State.Cycles);
		Assert.Equal(data + 2, cpu.State.A[0]);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | 0x0015), cpu.State.StatusRegister);

		var traceHits = cpu.Counters.TraceHits;
		cpu.State.ProgramCounter = FastCodeBase;
		cpu.State.A[0] = 0x2101;
		cpu.State.A[7] = 0x5000;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Carry;

		Assert.Equal(1, cpu.ExecuteInstructions(1, null, new CountingBoundary()));

		Assert.True(cpu.Counters.TraceHits > traceHits);
		Assert.Equal(0x2103u, cpu.State.A[0]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x44D5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x44D8, bus.ReadWord(0x4FF8));
		Assert.Equal(FastCodeBase + 2, bus.ReadLong(0x4FFC));
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
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
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
		var cpu = new M68kJitCore(bus, enableV2: false);
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
		WriteWords(bus, FastCodeBase + 4, 0x60FA); // BRA.S trap
		var hostHits = 0;
		bus.RegisterHostTrapStub(FastCodeBase, state =>
		{
			hostHits++;
			state.ProgramCounter = FastCodeBase + 4;
		});
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);

		cpu.ExecuteInstructions(180, null, new CountingBoundary());

		Assert.True(hostHits > 0);
		Assert.True(cpu.Counters.FallbackInstructions >= hostHits);
	}

	[Fact]
	public void JitInvalidatesTraceWhenSourceOpcodeChanges()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus, enableV2: false);
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
		var cpu = new M68kJitCore(bus, enableV2: false);
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
		var cpu = new M68kJitCore(bus, enableV2: false);
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
			0x4041,                 // NEGX.W D1
			0x4441,                 // NEG.W D1
			0x4641,                 // NOT.W D1
			0x60E6);                // BRA.S loop
	}

	[Fact]
	public void JitDirectIlExecutesNegxWithStickyZero()
	{
		var words = new ushort[]
		{
			0x4041, // NEGX.W D1
			0x60FC  // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus, enableV2: false);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.D[1] = jit.State.D[1] = 1;
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var interpreted = interpreter.ExecuteInstructions(400, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(400, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.DirectIlInstructions > 0);
		Assert.Equal(0, jit.Counters.HelperIlInstructions);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
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
		var jit = new M68kJitCore(jitBus, enableV2: false);
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
	public void JitV2ImmediateStatusSequenceMatchesEachArchitecturalSrTransition()
	{
		var bus = CreateRealFastCodeBus();
		var cases = new (ushort Opcode, ushort Immediate, ushort Input, ushort Expected)[]
		{
			(0x003C, 0x0011, 0x2700, 0x2711), // ORI #$0011,CCR
			(0x023C, 0xFFFE, 0x2711, 0x2710), // ANDI #$FFFE,CCR
			(0x0A3C, 0x0001, 0x2710, 0x2711), // EORI #$0001,CCR
			(0x007C, 0x0700, 0x2711, 0x2711), // ORI #$0700,SR
			(0x027C, 0xF8FF, 0x2711, 0x2011), // ANDI #$F8FF,SR
			(0x0A7C, 0x0100, 0x2011, 0x2111), // EORI #$0100,SR
			(0x44FC, 0x001F, 0x2111, 0x211F), // MOVE #$001F,CCR
			(0x46FC, 0x2700, 0x211F, 0x2700)  // MOVE #$2700,SR
		};
		for (var i = 0; i < cases.Length; i++)
		{
			var address = RealFastCodeBase + (uint)(i * 8);
			WriteWords(bus, address, cases[i].Opcode, cases[i].Immediate, 0x60FA); // BRA.S address
		}

		using var jit = new M68kJitCore(bus, enableV2: true);
		jit.Reset(RealFastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		for (var i = 0; i < cases.Length; i++)
		{
			var instructionAddress = RealFastCodeBase + (uint)(i * 8);
			jit.State.ProgramCounter = instructionAddress;
			jit.State.StatusRegister = cases[i].Input;
			_ = jit.ExecuteInstructions(128, jit.State.Cycles + 100_000, boundary);

			jit.State.ProgramCounter = instructionAddress;
			jit.State.StatusRegister = cases[i].Input;
			var hitsBefore = jit.Counters.V2TraceHits;

			Assert.Equal(2, jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary));
			Assert.Equal(cases[i].Expected, jit.State.StatusRegister);
			Assert.Equal(instructionAddress, jit.State.ProgramCounter);
			Assert.True(
				jit.Counters.V2TraceHits > hitsBefore,
				$"instruction={i}, compiled={jit.Counters.CompiledTraces}, " +
				$"v2Hits={jit.Counters.V2TraceHits}, fallback={jit.Counters.FallbackInstructions}");
		}
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
		Assert.InRange(jit.State.Cycles, interpreter.State.Cycles, interpreter.State.Cycles + 4);
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
		Assert.Contains("17-32", cpu.Counters.PureTraceBatchLengthHistogram ?? string.Empty);
		Assert.DoesNotContain("3-4", cpu.Counters.PureTraceBatchLengthHistogram ?? string.Empty);
		Assert.Contains("blitter", cpu.Counters.PureTraceBatchWakeSourceTop ?? string.Empty);
	}

	[Fact]
	public void JitM68000PureGraphCompilesV2OnlyForSupportedLoop()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x5280, 0x60FA); // MOVEQ #1,D0; ADDQ.L #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: false, enableV2BusGraph: false);
		cpu.Reset(FastCodeBase, 0x4000);

		var executed = cpu.ExecuteInstructions(220, 100_000, new PureBatchBoundary());

		Assert.Equal(220, executed);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.V2TraceHits > 0);
		Assert.True(cpu.Counters.V2TraceMethodsCompiled > 0);
		Assert.Equal(0, cpu.Counters.ClassicTraceMethodsCompiled);
		Assert.Equal(0, cpu.Counters.PureClassicTraceMethodsCompiled);
		Assert.True(cpu.Counters.PureTraceBatchExecutions > 0);
	}

	[Fact]
	public void JitGraphEnabledPureCpuTraceUsesV2OnlyDelegate()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x5280, 0x60FA); // MOVEQ #1,D0; ADDQ.L #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		cpu.Reset(FastCodeBase, 0x4000);

		var executed = cpu.ExecuteInstructions(220, 100_000, new PureBatchBoundary());

		Assert.Equal(220, executed);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.V2TraceMethodsCompiled > 0);
		Assert.Equal(0, cpu.Counters.ClassicTraceMethodsCompiled);
		Assert.Equal(0, cpu.Counters.PureClassicTraceMethodsCompiled);
	}

	[Fact]
	public void JitM68000PureGraphV2OnlyTraceFallsBackWithoutBatchTarget()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x5280, 0x60FA); // MOVEQ #1,D0; ADDQ.L #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: false, enableV2BusGraph: false);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.ExecuteInstructions(220, 100_000, new PureBatchBoundary());
		Assert.Equal(0, cpu.Counters.ClassicTraceMethodsCompiled);
		Assert.Equal(0, cpu.Counters.PureClassicTraceMethodsCompiled);

		cpu.State.ProgramCounter = FastCodeBase;
		cpu.State.D[0] = 0;
		var fallbackBefore = cpu.Counters.FallbackInstructions;
		var v2HitsBefore = cpu.Counters.V2TraceHits;

		var executed = cpu.ExecuteInstructions(1, null, new CountingBoundary());

		Assert.Equal(1, executed);
		Assert.Equal(FastCodeBase + 2, cpu.State.ProgramCounter);
		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(fallbackBefore + 1, cpu.Counters.FallbackInstructions);
		Assert.Equal(v2HitsBefore, cpu.Counters.V2TraceHits);
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
	public void JitGraphUsesBusAccessBatchForBusTouchingTrace()
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
		Assert.True(cpu.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(cpu.Counters.V2BusAccessBatchInstructions > 0);
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

		Assert.InRange(executed, 1, 19);
		Assert.True(cpu.State.Cycles >= wakeCycle);
		Assert.Contains("target-cycle", cpu.Counters.V2TraceHandoffBlockTop ?? string.Empty);
		Assert.DoesNotContain("target-cycle", cpu.Counters.V2TraceHandoffFailureTop ?? string.Empty);
		Assert.True(cpu.Counters.V2TraceHandoffNoCycleSlack > 0);
	}

	[Fact]
	public void JitV2CachesRepeatedBlockedHandoffTarget()
	{
		var root = FastCodeBase;
		var target = FastCodeBase + 0x104;
		var branchToTarget = unchecked((ushort)(target - (root + 4)));
		var bus = CreateCodeBus();
		WriteWords(bus, root, 0x7005, 0x6000, branchToTarget); // MOVEQ #5,D0; BRA.W target
		WriteWords(bus, target, 0xB348); // CMPM.W (A0)+,(A1)+ is not pure V2.
		var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: false);
		cpu.Reset(root, 0x4000);
		var boundary = new PureBatchBoundary();
		for (var i = 0; i < 80; i++)
		{
			cpu.State.ProgramCounter = root;
			cpu.ExecuteInstructions(20, cpu.State.Cycles + 100_000, boundary);
		}

		cpu.State.ProgramCounter = root;
		cpu.ExecuteInstructions(20, cpu.State.Cycles + 100_000, boundary);
		var stores = cpu.Counters.V2TraceHandoffCacheStores;
		var hits = cpu.Counters.V2TraceHandoffCacheHits;
		var failures = cpu.Counters.V2TraceHandoffFailures;

		cpu.State.ProgramCounter = root;
		cpu.ExecuteInstructions(20, cpu.State.Cycles + 100_000, boundary);

		Assert.Equal(0x0000_0005u, cpu.State.D[0]);
		Assert.True(
			stores > 0,
			$"stores={stores}, hits={cpu.Counters.V2TraceHandoffCacheHits}, attempts={cpu.Counters.V2TraceHandoffAttempts}, " +
			$"failures={cpu.Counters.V2TraceHandoffFailures}, block={cpu.Counters.V2TraceHandoffBlockTop}");
		Assert.True(cpu.Counters.V2TraceHandoffCacheHits > hits);
		Assert.True(cpu.Counters.V2TraceHandoffFailures > failures);
		Assert.Contains("bus-disabled", cpu.Counters.V2TraceHandoffBlockTop ?? string.Empty);
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
			using var cpu = new M68kJitCore(bus);
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
			Assert.Null(GetInstalledTraceDelegate(cpu, rootA, "Compiled"));
			Assert.NotNull(GetInstalledTraceDelegate(cpu, rootA, "V2Compiled"));
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
	public void JitPureTraceInvalidatedByHostTrapDoesNotCallBatchEnd()
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = new PureBatchBoundary();
		cpu.ExecuteInstructions(220, 100_000, boundary);
		var batchAfterCount = boundary.BatchAfterCount;
		WriteWords(bus, FastCodeBase + 4, 0x4E71);
		var hostHits = 0;
		bus.RegisterHostTrapStub(FastCodeBase, state =>
		{
			hostHits++;
			state.ProgramCounter = FastCodeBase + 4;
		});
		cpu.State.ProgramCounter = FastCodeBase;

		cpu.ExecuteInstructions(2, cpu.State.Cycles + 10_000, boundary);

		Assert.Equal(1, hostHits);
		Assert.Equal(batchAfterCount, boundary.BatchAfterCount);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void JitGenerationGuardExitsWhenTraceBytesChangeWithoutWriteNotification(bool enableV2)
	{
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, 0x7001, 0x60FC); // MOVEQ #1,D0; BRA.S loop
		var cpu = new M68kJitCore(bus, enableV2);
		cpu.Reset(FastCodeBase, 0x4000);
		var boundary = enableV2 ? new PureBatchBoundary() : new CountingBoundary();
		cpu.ExecuteInstructions(220, enableV2 ? 100_000 : null, boundary);
		Assert.True(enableV2 ? cpu.Counters.V2TraceHits > 0 : cpu.Counters.TraceHits > 0);
		var generationGuardExits = cpu.Counters.GenerationGuardExits;
		var generationMismatches = cpu.Counters.GenerationMismatches;

		bus.ExpansionRam[0] = 0x70;
		bus.ExpansionRam[1] = 0x02;
		var touchCodePage = typeof(AmigaBus).GetMethod(
			"TouchCodePage",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		touchCodePage.Invoke(bus, new object[] { FastCodeBase });
		cpu.State.ProgramCounter = FastCodeBase;

		cpu.ExecuteInstructions(1, enableV2 ? cpu.State.Cycles + 10_000 : null, boundary);

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
	public void JitV2ChecksBccFromPendingFlagsAndMatchesInterpreter()
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
		Assert.True(jit.Counters.V2BranchPendingFlagChecks > 0);
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

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void JitV2BlockGraphClosesBackwardLoopWhenHotRootFollowsHeader(bool m68040)
	{
		var words = new ushort[]
		{
			0x7001, // loop header: MOVEQ #1,D0
			0x5280, // hot root: ADDQ.L #1,D0
			0x60FA  // BRA.S loop header
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = m68040
			? (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, jitBus)
			: new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase + 2, 0x4000);
		interpreter.Reset(RealFastCodeBase + 2, 0x4000);
		if (m68040)
		{
			EnableM68040InstructionCache(jit);
		}

		var boundary = new PureBatchBoundary();
		var warmup = 0;
		for (var i = 0; i < 250 && jit.Counters.PureTraceBatchExecutions == 0; i++)
		{
			warmup += jit.ExecuteInstructions(300, jit.State.Cycles + 100_000, boundary);
			if (jit.Counters.PureTraceBatchExecutions == 0)
			{
				Thread.Sleep(1);
			}
		}

		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.Equal(RealFastCodeBase + 2, jit.State.ProgramCounter);
		var outOfBlockExits = jit.Counters.V2SideExitOutOfBlockBranch;
		var beforeGraphExits = jit.Counters.V2SideExitBeforeGraph;
		var v2Hits = jit.Counters.V2TraceHits;
		var batches = jit.Counters.PureTraceBatchExecutions;
		var batchInstructions = jit.Counters.PureTraceBatchInstructions;

		var measured = jit.ExecuteInstructions(300, jit.State.Cycles + 100_000, boundary);
		var interpreted = interpreter.ExecuteInstructions(warmup + measured, null, new CountingBoundary());

		Assert.Equal(warmup + measured, interpreted);
		Assert.Equal(300, measured);
		Assert.True(jit.Counters.V2TraceHits > v2Hits);
		Assert.Equal(batches + 1, jit.Counters.PureTraceBatchExecutions);
		Assert.Equal(batchInstructions + measured, jit.Counters.PureTraceBatchInstructions);
		Assert.Equal(outOfBlockExits, jit.Counters.V2SideExitOutOfBlockBranch);
		Assert.Equal(beforeGraphExits, jit.Counters.V2SideExitBeforeGraph);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void JitV2BusAccessBlockGraphClosesBackwardLoopWhenHotRootFollowsHeader(bool m68040)
	{
		var words = new ushort[]
		{
			0x4E71, // loop header: NOP
			0x2010, // hot root: MOVE.L (A0),D0
			0x5281, // ADDQ.L #1,D1
			0x60F8  // BRA.S loop header
		};
		var data = RealFastCodeBase + 0x2000;
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		jitBus.WriteLong(data, 0x1234_5678);
		interpreterBus.WriteLong(data, 0x1234_5678);
		var jit = m68040
			? (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, jitBus)
			: new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase + 2, 0x4000);
		interpreter.Reset(RealFastCodeBase + 2, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;
		if (m68040)
		{
			EnableM68040InstructionCache(jit);
		}

		var boundary = new PureBatchBoundary();
		var warmup = 0;
		for (var i = 0; i < 250 && jit.Counters.V2BusAccessBatchExecutions == 0; i++)
		{
			warmup += jit.ExecuteInstructions(320, jit.State.Cycles + 100_000, boundary);
			if (jit.Counters.V2BusAccessBatchExecutions == 0)
			{
				Thread.Sleep(1);
			}
		}

		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.Equal(RealFastCodeBase + 2, jit.State.ProgramCounter);
		var outOfBlockExits = jit.Counters.V2SideExitOutOfBlockBranch;
		var batches = jit.Counters.V2BusAccessBatchExecutions;
		var batchInstructions = jit.Counters.V2BusAccessBatchInstructions;

		var measured = jit.ExecuteInstructions(320, jit.State.Cycles + 100_000, boundary);
		var interpreted = interpreter.ExecuteInstructions(warmup + measured, null, new CountingBoundary());

		Assert.Equal(warmup + measured, interpreted);
		Assert.Equal(320, measured);
		Assert.Equal(batches + 1, jit.Counters.V2BusAccessBatchExecutions);
		Assert.Equal(batchInstructions + measured, jit.Counters.V2BusAccessBatchInstructions);
		Assert.Equal(outOfBlockExits, jit.Counters.V2SideExitOutOfBlockBranch);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
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

		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true, enableV2BusGraph: false);
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
		Assert.True(jit.Counters.V2ZeroWaitReadRealFast > 0);
		Assert.Equal(0, jit.Counters.V2ZeroWaitReadSlow);
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
		var readFastBefore = jit.Counters.V2ZeroWaitReadRealFast;
		var writeFastBefore = jit.Counters.V2ZeroWaitWriteRealFast;
		var slowReadBefore = jit.Counters.V2ZeroWaitReadSlow;
		var slowWriteBefore = jit.Counters.V2ZeroWaitWriteSlow;
		jitBus.WriteLong(data, 0);
		interpreterBus.WriteLong(data, 0);
		var notifiedAddress = 0u;
		var notifiedBytes = 0;
		jitBus.JitEligibleMemoryWritten += (address, byteCount) =>
		{
			notifiedAddress = address;
			notifiedBytes = byteCount;
		};
		var accessStart = jitBus.BusAccesses.Count;
		jit.State.ProgramCounter = RealFastCodeBase;
		interpreter.State.ProgramCounter = RealFastCodeBase;

		var measuredJit = jit.ExecuteInstructions(4, jit.State.Cycles + 100_000, boundary);
		var measuredInterpreter = interpreter.ExecuteInstructions(4, null, new CountingBoundary());

		Assert.Equal(measuredInterpreter, measuredJit);
		Assert.Equal(0x1234_5679u, jitBus.ReadLong(data));
		Assert.Equal(interpreterBus.ReadLong(data), jitBus.ReadLong(data));
		Assert.Equal(data, notifiedAddress);
		Assert.Equal(4, notifiedBytes);
		Assert.True(jit.Counters.V2ZeroWaitReadRealFast > readFastBefore);
		Assert.True(jit.Counters.V2ZeroWaitWriteRealFast > writeFastBefore);
		Assert.Equal(slowReadBefore, jit.Counters.V2ZeroWaitReadSlow);
		Assert.Equal(slowWriteBefore, jit.Counters.V2ZeroWaitWriteSlow);
		Assert.Equal(0, CountCpuDataAccesses(jitBus, accessStart, data, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Long));
		Assert.Equal(0, CountCpuDataAccesses(jitBus, accessStart, data, AmigaBusAccessKind.CpuDataWrite, AmigaBusAccessSize.Long));
	}

	[Theory]
	[MemberData(nameof(CiaByteMovePrograms))]
	public void JitV2MatchesInterpreterCyclesForCiaByteMoves(ushort[] words, int programKind)
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		if (programKind == 0)
		{
			jit.State.A[1] = 0x00BF_D000;
			interpreter.State.A[1] = 0x00BF_D000;
		}
		else
		{
			jit.State.D[1] = 0xFF;
			interpreter.State.D[1] = 0xFF;
		}

		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(900, jit.State.Cycles + 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(900, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		var jitWrite = jitBus.BusAccesses.Last(access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Address == 0x00BF_D100);
		var interpreterWrite = interpreterBus.BusAccesses.Last(access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Address == 0x00BF_D100);
		Assert.Equal(interpreterWrite.GrantedCycle, jitWrite.GrantedCycle);
		Assert.Equal(interpreterWrite.CompletedCycle, jitWrite.CompletedCycle);
	}

	public static IEnumerable<object[]> CiaByteMovePrograms()
	{
		yield return new object[]
		{
			new ushort[]
			{
				0x137C, 0x00FF, 0x0100, // MOVE.B #$FF,0x0100(A1)
				0x60F8                  // BRA.S start
			},
			0
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x13C1, 0x00BF, 0xD100, // MOVE.B D1,$00BFD100
				0x60F8                  // BRA.S start
			},
			1
		};
	}

	[Fact]
	public void JitV2MatchesInterpreterCyclesForRomDriveControlMiniSequence()
	{
		var words = new ushort[]
		{
			0x122B, 0x0041,         // MOVE.B $41(A3),D1
			0x0001, 0x007F,         // ORI.B #$7F,D1
			0x13C1, 0x00BF, 0xD100, // MOVE.B D1,$00BFD100
			0x60F0                  // BRA.S start
		};
		foreach (var source in new[] { RealFastCodeBase + 0x1000, 0x2000u })
		{
			var jitBus = CreateRealFastCodeBus();
			var interpreterBus = CreateRealFastCodeBus();
			WriteWords(jitBus, RealFastCodeBase, words);
			WriteWords(interpreterBus, RealFastCodeBase, words);
			jitBus.WriteByte(source, 0x00, 0);
			interpreterBus.WriteByte(source, 0x00, 0);
			var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
			var interpreter = new M68kInterpreter(interpreterBus);
			jit.Reset(RealFastCodeBase, 0x4000);
			interpreter.Reset(RealFastCodeBase, 0x4000);
			jit.State.A[3] = source - 0x41;
			interpreter.State.A[3] = source - 0x41;
			var boundary = new PureBatchBoundary();

			var jitExecuted = jit.ExecuteInstructions(900, jit.State.Cycles + 500_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(900, null, new CountingBoundary());

			Assert.Equal(interpreterExecuted, jitExecuted);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.D, jit.State.D);
			Assert.Equal(interpreter.State.A, jit.State.A);
			Assert.True(jit.Counters.V2TraceHits > 0);
			Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
			var jitWrite = jitBus.BusAccesses.Last(access =>
				access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Address == 0x00BF_D100);
			var interpreterWrite = interpreterBus.BusAccesses.Last(access =>
				access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Address == 0x00BF_D100);
			Assert.Equal(interpreterWrite.GrantedCycle, jitWrite.GrantedCycle);
			Assert.Equal(interpreterWrite.CompletedCycle, jitWrite.CompletedCycle);
		}
	}

	[Fact]
	public void JitV2PublishesCurrentInstructionContextBeforeBusSideEffects()
	{
		var previousTrace = Environment.GetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE");
		try
		{
			Environment.SetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE", "1");
			var words = new ushort[]
			{
				0x137C, 0x00FF, 0x0100, // MOVE.B #$FF,0x0100(A1)
				0x13C1, 0x00BF, 0xD100, // MOVE.B D1,$00BFD100
				0x60F2                  // BRA.S start
			};
			var bus = CreateRealFastCodeBus();
			WriteWords(bus, RealFastCodeBase, words);
			var cpu = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: true);
			cpu.Reset(RealFastCodeBase, 0x4000);
			cpu.State.A[1] = 0x00BF_D000;
			cpu.State.D[1] = 0xBF;
			bus.ConfigureDiskDivergenceTrace(
				"jit",
				() => new AmigaDiskTraceCpuContext(
					cpu.State.ProgramCounter,
					cpu.State.LastInstructionProgramCounter,
					cpu.State.LastOpcode,
					cpu.State.Cycles));
			var boundary = new PureBatchBoundary();
			_ = cpu.ExecuteInstructions(900, cpu.State.Cycles + 500_000, boundary);
			Assert.True(cpu.Counters.V2TraceHits > 0);

			bus.Disk.ClearDmaTrace();
			var hits = cpu.Counters.V2TraceHits;
			cpu.State.ProgramCounter = RealFastCodeBase;
			var targetCycle = cpu.State.Cycles + 100_000;
			for (var attempt = 0; attempt < 64; attempt++)
			{
				if (bus.Disk.CaptureDivergenceTrace()
					.Count(entry => entry.Kind == AmigaDiskTraceEventKind.CiaBDriveControlWrite) >= 2)
				{
					break;
				}

				_ = cpu.ExecuteInstructions(4, targetCycle, boundary);
			}

			Assert.True(cpu.Counters.V2TraceHits > hits);
			var writes = bus.Disk.CaptureDivergenceTrace()
				.Where(entry => entry.Kind == AmigaDiskTraceEventKind.CiaBDriveControlWrite)
				.Take(2)
				.ToArray();
			Assert.Equal(2, writes.Length);
			Assert.Equal(RealFastCodeBase, writes[0].LastInstructionProgramCounter);
			Assert.Equal(0x137C, writes[0].LastOpcode);
			Assert.Equal(RealFastCodeBase + 6, writes[1].LastInstructionProgramCounter);
			Assert.Equal(0x13C1, writes[1].LastOpcode);
		}
		finally
		{
			Environment.SetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE", previousTrace);
		}
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
		Assert.True(jit.Counters.V2ZeroWaitReadRom > 0);

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
		jitBus.ConfigureAutoconfigFastRamForHost();
		interpreterBus.ConfigureAutoconfigFastRamForHost();
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
	public void JitV2BatchesDataRegisterMemoryMovesThroughBusAccesses()
	{
		var words = new ushort[]
		{
			0x2080, // MOVE.L D0,(A0)
			0x30C1, // MOVE.W D1,(A0)+
			0x20C9, // MOVE.L A1,(A0)+
			0x20C8, // MOVE.L A0,(A0)+
			0x10C2  // MOVE.B D2,(A0)+
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
			jit.State.D[1] = 0x89AB_CDEF;
			jit.State.D[2] = 0x7654_32A5;
			jit.State.A[0] = 0x2000;
			jit.State.A[1] = 0x2468_ACE0;
			jitBus.WriteLong(0x2000, 0);
			jitBus.WriteLong(0x2004, 0);
			jitBus.WriteLong(0x2008, 0);
			interpreter.State.ProgramCounter = FastCodeBase;
			interpreter.State.D[0] = 0x1234_5678;
			interpreter.State.D[1] = 0x89AB_CDEF;
			interpreter.State.D[2] = 0x7654_32A5;
			interpreter.State.A[0] = 0x2000;
			interpreter.State.A[1] = 0x2468_ACE0;
			interpreterBus.WriteLong(0x2000, 0);
			interpreterBus.WriteLong(0x2004, 0);
			interpreterBus.WriteLong(0x2008, 0);
			var jitExecuted = jit.ExecuteInstructions(5, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(5, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
		}

		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2000), jitBus.ReadLong(0x2000));
		Assert.Equal(interpreterBus.ReadLong(0x2004), jitBus.ReadLong(0x2004));
		Assert.Equal(interpreterBus.ReadLong(0x2008), jitBus.ReadLong(0x2008));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
		Assert.DoesNotContain("MOVE.L Dn->(An)", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("MOVE.W Dn->(An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("MOVE.L An->(An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.DoesNotContain("MOVE.B Dn->(An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
	}

	[Fact]
	public void JitM68000PureGraphDoesNotTreatMemoryWriteMoveAsPureV2()
	{
		var words = new ushort[]
		{
			0x20C0, // MOVE.L D0,(A0)+
			0x60FC  // BRA.S MOVE
		};
		var bus = CreateCodeBus();
		WriteWords(bus, FastCodeBase, words);
		var jit = new M68kJitCore(bus, enableV2: true, enableV2BusAccess: false, enableV2BusGraph: false);
		jit.FallbackAttributionEnabled = true;
		jit.Reset(FastCodeBase, 0x4000);
		jit.State.D[0] = 0x1234_5678;
		jit.State.A[0] = 0x2000;

		var executed = jit.ExecuteInstructions(220, jit.State.Cycles + 1_000_000, new PureBatchBoundary());

		Assert.Equal(220, executed);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.Equal(0, jit.Counters.V2TraceHits);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchExecutions);
		Assert.Equal(0, jit.Counters.V2BusAccessBatchInstructions);
		Assert.True(jit.Counters.V2RejectedUnsupportedEa > 0);
		Assert.Contains("MOVE.L Dn->(An)+", jit.Counters.V2UnsupportedEaTop ?? string.Empty);
		Assert.Contains("MOVE.L Dn->(An)+", jit.Counters.FallbackInstructionTop ?? string.Empty);
		Assert.Contains("0x", jit.Counters.FallbackRootTop ?? string.Empty);
	}

	[Fact]
	public void JitV2MemoryWriteMoveDoesNotReadDestination()
	{
		var words = new ushort[]
		{
			0x20C0,         // MOVE.L D0,(A0)+
			0x30FC, 0x55AA  // MOVE.W #$55AA,(A0)+
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
			_ = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
		}

		jit.State.ProgramCounter = FastCodeBase;
		jit.State.D[0] = 0x89AB_CDEF;
		jit.State.A[0] = 0x2000;
		interpreter.State.ProgramCounter = FastCodeBase;
		interpreter.State.D[0] = 0x89AB_CDEF;
		interpreter.State.A[0] = 0x2000;
		var firstDestination = 0x2000u;
		var secondDestination = 0x2004u;
		jitBus.WriteLong(firstDestination, 0);
		jitBus.WriteWord(secondDestination, 0);
		interpreterBus.WriteLong(firstDestination, 0);
		interpreterBus.WriteWord(secondDestination, 0);
		var accessStart = jitBus.BusAccesses.Count;

		var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(firstDestination), jitBus.ReadLong(firstDestination));
		Assert.Equal(interpreterBus.ReadWord(secondDestination), jitBus.ReadWord(secondDestination));
		Assert.Equal(0, CountCpuDataAccesses(jitBus, accessStart, firstDestination, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Long));
		Assert.Equal(0, CountCpuDataAccesses(jitBus, accessStart, secondDestination, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Word));
		Assert.True(CountCpuDataAccesses(jitBus, accessStart, firstDestination, AmigaBusAccessKind.CpuDataWrite, AmigaBusAccessSize.Long) > 0);
		Assert.True(CountCpuDataAccesses(jitBus, accessStart, secondDestination, AmigaBusAccessKind.CpuDataWrite, AmigaBusAccessSize.Word) > 0);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.DirectMemoryIlInstructions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
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
		jit.State.Cycles = 0;
		jit.State.Halted = false;
		jit.State.Stopped = false;
		jit.State.LastOpcode = 0;
		jit.State.LastInstructionProgramCounter = 0;
		Array.Clear(jit.State.D);
		Array.Clear(jit.State.A);
		jit.State.ResetStackPointers(0x4000, 0, supervisorMode: true);
		jit.State.StatusRegister = M68kCpuState.ResetStatusRegister;
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
	public void JitV2MatchesInterpreterCyclesForMemoryClrWithZeroWaitCode()
	{
		var data = 0x2000u;
		var words = new ushort[]
		{
			0x4258, // CLR.W (A0)+
			0x60FC  // BRA.S back to CLR
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		for (var i = 0; i < 512; i++)
		{
			var address = data + (uint)(i * 2);
			jitBus.WriteWord(address, (ushort)(0x4000 + i));
			interpreterBus.WriteWord(address, (ushort)(0x4000 + i));
		}

		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.State.A[0] = data;
		interpreter.State.A[0] = data;
		var boundary = new PureBatchBoundary();

		var jitExecuted = jit.ExecuteInstructions(900, jit.State.Cycles + 500_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(900, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadWord(data), jitBus.ReadWord(data));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
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
			interpreter.Reset(FastCodeBase, 0x4000);
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
		var accessStart = jitBus.BusAccesses.Count;

		var measuredJitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
		var measuredInterpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());

		Assert.Equal(measuredInterpreterExecuted, measuredJitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x2008), jitBus.ReadLong(0x2008));
		Assert.Equal(interpreterBus.ReadLong(0x3000), jitBus.ReadLong(0x3000));
		Assert.True(CountCpuDataAccesses(jitBus, accessStart, 0x2004, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Long) > 0);
		Assert.True(CountCpuDataAccesses(jitBus, accessStart, 0x2000, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Long) > 0);
		Assert.Equal(0, CountCpuDataAccesses(jitBus, accessStart, 0x2008, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Long));
		Assert.Equal(0, CountCpuDataAccesses(jitBus, accessStart, 0x3000, AmigaBusAccessKind.CpuDataRead, AmigaBusAccessSize.Long));
		Assert.True(CountCpuDataAccesses(jitBus, accessStart, 0x2008, AmigaBusAccessKind.CpuDataWrite, AmigaBusAccessSize.Long) > 0);
		Assert.True(CountCpuDataAccesses(jitBus, accessStart, 0x3000, AmigaBusAccessKind.CpuDataWrite, AmigaBusAccessSize.Long) > 0);
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
			interpreter.Reset(FastCodeBase, 0x4000);
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
			interpreter.Reset(FastCodeBase, 0x4000);
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

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void JitMatchesInterpreterCyclesForJsrAddressRegisterAndRts(bool enableV2)
	{
		var words = new ushort[]
		{
			0x4E90, // JSR (A0)
			0x7002, // MOVEQ #2,D0
			0x60FA, // BRA.S start
			0x7001, // target: MOVEQ #1,D0
			0x4E75  // RTS
		};
		var target = RealFastCodeBase + 6;
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.State.A[0] = target;
		interpreter.State.A[0] = target;

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, new CountingBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x3FFC), jitBus.ReadLong(0x3FFC));
		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void JitV2MatchesInterpreterCyclesForJsrDisplacementAndRts()
	{
		var target = RealFastCodeBase + 8;
		const ushort displacement = 0x0020;
		var baseAddress = target - displacement;
		var words = new ushort[]
		{
			0x4EAE, displacement, // JSR displacement(A6)
			0x7002,               // MOVEQ #2,D0
			0x60F8,               // BRA.S start
			0x7001,               // target: MOVEQ #1,D0
			0x4E75                // RTS
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.State.A[6] = baseAddress;
		interpreter.State.A[6] = baseAddress;
		var jitBoundary = new PureBatchBoundary();
		var interpreterBoundary = new CountingBoundary();

		var jitExecuted = jit.ExecuteInstructions(1200, 500_000, jitBoundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, interpreterBoundary);

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadLong(0x3FFC), jitBus.ReadLong(0x3FFC));
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
	}

	[Fact]
	public void JitV2JsrHostTrapTargetUsesRealJsrReturnAddress()
	{
		var hostTarget = FastCodeBase + 0x100;
		var jitBus = CreateCodeBus();
		var interpreterBus = CreateCodeBus();
		WriteWords(jitBus, FastCodeBase, 0x4E90, 0x60FC); // JSR (A0); BRA.S self
		WriteWords(interpreterBus, FastCodeBase, 0x4E90, 0x60FC);
		var jitHostHits = 0;
		var interpreterHostHits = 0;
		jitBus.RegisterHostTrapStub(hostTarget, state =>
		{
			jitHostHits++;
		});
		interpreterBus.RegisterHostTrapStub(hostTarget, state =>
		{
			interpreterHostHits++;
		});
		var jit = new M68kJitCore(jitBus, enableV2: true);
		var interpreter = new M68kInterpreter(interpreterBus);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.State.A[0] = hostTarget;
		interpreter.State.A[0] = hostTarget;

		var jitExecuted = jit.ExecuteInstructions(219, 500_000, new PureBatchBoundary());
		var interpreterExecuted = interpreter.ExecuteInstructions(219, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreterHostHits, jitHostHits);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x4000u, jit.State.A[7]);
		Assert.Equal(FastCodeBase + 2, jitBus.ReadLong(0x3FFC));
		Assert.True(jit.Counters.V2TraceHits > 0);
	}

	[Fact]
	public void JitV2BatchesRegisterBitOperations()
	{
		var words = new ushort[]
		{
			0x70FF,       // MOVEQ #-1,D0
			0x4240,       // CLR.W D0
			0x7214,       // MOVEQ #20,D1
			0x08C0, 0x0014, // BSET #20,D0
			0x0800, 0x0014, // BTST #20,D0
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
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
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
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
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
	public void JitV2MovemPostincrementMaskPreservesExcludedDataRegisters()
	{
		var words = new ushort[]
		{
			0x4CDE, 0x3F0F, // MOVEM.L (A6)+,D0-D3/A0-A5
			0x60FA          // BRA.S loop
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
			interpreter.State.ProgramCounter = FastCodeBase;
			jit.State.A[6] = 0x200;
			interpreter.State.A[6] = 0x200;
			for (var register = 4; register < 8; register++)
			{
				jit.State.D[register] = 0xD400_0000u + (uint)register;
				interpreter.State.D[register] = 0xD400_0000u + (uint)register;
			}

			for (var word = 0; word < 10; word++)
			{
				var value = 0x1000_0000u + (uint)(word + i);
				jitBus.WriteLong(0x200u + (uint)(word * 4), value);
				interpreterBus.WriteLong(0x200u + (uint)(word * 4), value);
			}

			var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
			Assert.Equal(interpreter.State.D, jit.State.D);
			Assert.Equal(interpreter.State.A, jit.State.A);
		}

		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
	}

	[Fact]
	public void JitM68040MovemPostincrementMaskPreservesExcludedDataRegisters()
	{
		var words = new ushort[]
		{
			0x4CDE, 0x3F0F, // MOVEM.L (A6)+,D0-D3/A0-A5
			0x60FA          // BRA.S loop
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, jitBus);
		var interpreter = new M68040Interpreter(interpreterBus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(jit);
		var boundary = new PureBatchBoundary();
		for (var i = 0; i < 180; i++)
		{
			jit.State.ProgramCounter = RealFastCodeBase;
			interpreter.State.ProgramCounter = RealFastCodeBase;
			jit.State.A[6] = 0x200;
			interpreter.State.A[6] = 0x200;
			for (var register = 4; register < 8; register++)
			{
				jit.State.D[register] = 0xD400_0000u + (uint)register;
				interpreter.State.D[register] = 0xD400_0000u + (uint)register;
			}

			for (var word = 0; word < 10; word++)
			{
				var value = 0x1000_0000u + (uint)(word + i);
				jitBus.WriteLong(0x200u + (uint)(word * 4), value);
				interpreterBus.WriteLong(0x200u + (uint)(word * 4), value);
			}

			var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
			var interpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());
			Assert.Equal(interpreterExecuted, jitExecuted);
			Assert.Equal(interpreter.State.D, jit.State.D);
			Assert.Equal(interpreter.State.A, jit.State.A);
		}

		if (jit.Counters.V2TraceHits == 0)
		{
			WaitForV2TraceHit(jit, boundary);
		}

		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.V2BusAccessBatchExecutions > 0);
		Assert.True(jit.Counters.V2BusAccessBatchInstructions > 0);
	}

	[Fact]
	public void JitM68040MovemPostincrementChipLoadsAdvanceThroughChipBus()
	{
		var words = new ushort[]
		{
			0x4CDE, 0x3F0F, // MOVEM.L (A6)+,D0-D3/A0-A5
			0x60FA          // BRA.S loop
		};
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, jitBus);
		var interpreter = new M68040Interpreter(interpreterBus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		EnableM68040InstructionCache(jit);
		var boundary = new PureBatchBoundary();
		PrepareM68040MovemState(jit.State, jitBus, 0);
		jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
		WaitForV2TraceHit(jit, boundary);

		PrepareM68040MovemState(jit.State, jitBus, 1);
		PrepareM68040MovemState(interpreter.State, interpreterBus, 1);
		var jitCycleBefore = jit.State.Cycles;

		var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
		var interpreterExecuted = interpreter.ExecuteInstructions(2, null, new CountingBoundary());

		Assert.Equal(interpreterExecuted, jitExecuted);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.State.Cycles - jitCycleBefore >= 10 * 2 * AgnusChipSlotScheduler.SlotCycles);
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
		var cpu = new M68kJitCore(bus, enableV2: false);
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
	[InlineData(0xC501, (int)M68kJitOperation.Abcd)]
	[InlineData(0xC509, (int)M68kJitOperation.Abcd)]
	[InlineData(0x8501, (int)M68kJitOperation.Sbcd)]
	[InlineData(0x8509, (int)M68kJitOperation.Sbcd)]
	[InlineData(0x4802, (int)M68kJitOperation.Nbcd)]
	public void JitDecoderClassifiesBcdOpcodesBeforeGenericArithmetic(ushort opcode, int operation)
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, opcode);

		Assert.True(M68kDecoder.TryDecode(bus, RealFastCodeBase, out var instruction, out var reason));
		Assert.Equal(M68kJitBailoutReason.None, reason);
		Assert.Equal((M68kJitOperation)operation, instruction.Operation);
	}

	[Theory]
	[InlineData(0x0108, (int)M68kOperandSize.Word, 0)]
	[InlineData(0x0148, (int)M68kOperandSize.Long, 0)]
	[InlineData(0x0188, (int)M68kOperandSize.Word, 1)]
	[InlineData(0x01C8, (int)M68kOperandSize.Long, 1)]
	public void JitDecoderClassifiesMovepBeforeDynamicBitOpcodes(ushort opcode, int size, int variant)
	{
		var bus = CreateRealFastCodeBus();
		WriteWords(bus, RealFastCodeBase, opcode, 0x0010);

		Assert.True(M68kDecoder.TryDecode(bus, RealFastCodeBase, out var instruction, out var reason));
		Assert.Equal(M68kJitBailoutReason.None, reason);
		Assert.Equal(M68kJitOperation.Movep, instruction.Operation);
		Assert.Equal((M68kOperandSize)size, instruction.Size);
		Assert.Equal(variant, instruction.Variant);
		Assert.Equal(4, instruction.Length);
	}

	[Fact]
	public void JitCompiledHelperMatchesInterpreterForMovepLoop()
	{
		var words = new ushort[]
		{
			0x0549, 0x0011, // MOVEP.L $11(A1),D2
			0x0189, 0x0019, // MOVEP.W D0,$19(A1)
			0x60F6          // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, 0x2010, 0x0012, 0x0034, 0x0056, 0x0078);
		WriteWords(jitBus, 0x2010, 0x0012, 0x0034, 0x0056, 0x0078);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		jit.FallbackAttributionEnabled = true;
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.A[1] = jit.State.A[1] = 0x2000;
		interpreter.State.D[0] = jit.State.D[0] = 0xAABB_CDEF;
		interpreter.State.D[2] = jit.State.D[2] = 0xFFFF_FFFF;
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		var interpreted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(0x12, jitBus.ReadByte(0x2011));
		Assert.Equal(0x34, jitBus.ReadByte(0x2013));
		Assert.Equal(0x56, jitBus.ReadByte(0x2015));
		Assert.Equal(0x78, jitBus.ReadByte(0x2017));
		Assert.Equal(interpreterBus.ReadByte(0x2019), jitBus.ReadByte(0x2019));
		Assert.Equal(interpreterBus.ReadByte(0x201B), jitBus.ReadByte(0x201B));
		Assert.Equal(0xCD, jitBus.ReadByte(0x2019));
		Assert.Equal(0xEF, jitBus.ReadByte(0x201B));
	}

	[Fact]
	public void JitCompiledHelperMatchesInterpreterForRegisterBcdLoop()
	{
		var words = new ushort[]
		{
			0xC501, // ABCD D1,D2
			0x8903, // SBCD D3,D4
			0x4805, // NBCD D5
			0x60F8  // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus, enableV2: false);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.D[1] = jit.State.D[1] = 0x01;
		interpreter.State.D[2] = jit.State.D[2] = 0x10;
		interpreter.State.D[3] = jit.State.D[3] = 0x01;
		interpreter.State.D[4] = jit.State.D[4] = 0x50;
		interpreter.State.D[5] = jit.State.D[5] = 0x01;
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var interpreted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
	}

	[Fact]
	public void JitV2BatchesRegisterBcdOperations()
	{
		var words = new ushort[]
		{
			0xC501, // ABCD D1,D2
			0x8903, // SBCD D3,D4
			0x4805, // NBCD D5
			0xCD06, // ABCD D6,D6
			0x60F6  // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.D[1] = jit.State.D[1] = 0x09;
		interpreter.State.D[2] = jit.State.D[2] = 0x90;
		interpreter.State.D[3] = jit.State.D[3] = 0x01;
		interpreter.State.D[4] = jit.State.D[4] = 0x50;
		interpreter.State.D[5] = jit.State.D[5] = 0x01;
		interpreter.State.D[6] = jit.State.D[6] = 0x49;
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var interpreted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(800, 500_000, new PureBatchBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
		Assert.DoesNotContain("ABCD", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.DoesNotContain("SBCD", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.DoesNotContain("NBCD", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
	}

	[Fact]
	public void JitV2BatchesNegxDataRegisterWithStickyZero()
	{
		var words = new ushort[]
		{
			0x4041, // NEGX.W D1
			0x4082, // NEGX.L D2
			0x60FA  // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.D[1] = jit.State.D[1] = 1;
		interpreter.State.D[2] = jit.State.D[2] = 0;
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var interpreted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(800, 500_000, new PureBatchBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.V2TraceHits > 0);
		Assert.True(jit.Counters.PureTraceBatchExecutions > 0);
		Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
		Assert.DoesNotContain("NEGX", jit.Counters.V2UnsupportedOperationTop ?? string.Empty);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
	}

	[Fact]
	public void JitV2DoesNotTreatMemoryBcdAsPureWhenBusAccessIsDisabled()
	{
		var words = new ushort[]
		{
			0xC308, // ABCD -(A0),-(A1)
			0x8308, // SBCD -(A0),-(A1)
			0x4812, // NBCD (A2)
			0x60F8  // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		interpreterBus.WriteWord(0x1F00, 0x4900);
		interpreterBus.WriteWord(0x2000, 0x4900);
		interpreterBus.WriteWord(0x20FF, 0x5000);
		interpreterBus.WriteWord(0x2100, 0x5000);
		interpreterBus.WriteWord(0x2200, 0x2000);
		jitBus.WriteWord(0x1F00, 0x4900);
		jitBus.WriteWord(0x2000, 0x4900);
		jitBus.WriteWord(0x20FF, 0x5000);
		jitBus.WriteWord(0x2100, 0x5000);
		jitBus.WriteWord(0x2200, 0x2000);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus, enableV2: true, enableV2BusAccess: false);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2001;
		interpreter.State.A[1] = jit.State.A[1] = 0x2101;
		interpreter.State.A[2] = jit.State.A[2] = 0x2200;
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var interpreted = interpreter.ExecuteInstructions(80, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(80, 500_000, new PureBatchBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.Equal(0, jit.Counters.PureTraceBatchExecutions);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadWord(0x1F00), jitBus.ReadWord(0x1F00));
		Assert.Equal(interpreterBus.ReadWord(0x2000), jitBus.ReadWord(0x2000));
		Assert.Equal(interpreterBus.ReadWord(0x20FF), jitBus.ReadWord(0x20FF));
		Assert.Equal(interpreterBus.ReadWord(0x2100), jitBus.ReadWord(0x2100));
		Assert.Equal(interpreterBus.ReadWord(0x2200), jitBus.ReadWord(0x2200));
	}

	[Fact]
	public void JitCompiledHelperMatchesInterpreterForMemoryBcdLoop()
	{
		var words = new ushort[]
		{
			0x207C, 0x0000, 0x2001, // MOVEA.L #$2001,A0
			0x227C, 0x0000, 0x2101, // MOVEA.L #$2101,A1
			0xC308,                 // ABCD -(A0),-(A1)
			0x207C, 0x0000, 0x2001, // MOVEA.L #$2001,A0
			0x227C, 0x0000, 0x2101, // MOVEA.L #$2101,A1
			0x8308,                 // SBCD -(A0),-(A1)
			0x247C, 0x0000, 0x2200, // MOVEA.L #$2200,A2
			0x4812,                 // NBCD (A2)
			0x60DA                  // BRA.S start
		};
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		interpreterBus.WriteWord(0x2000, 0x4900);
		interpreterBus.WriteWord(0x2100, 0x5000);
		interpreterBus.WriteWord(0x2200, 0x2000);
		jitBus.WriteWord(0x2000, 0x4900);
		jitBus.WriteWord(0x2100, 0x5000);
		jitBus.WriteWord(0x2200, 0x2000);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.StatusRegister = jit.State.StatusRegister =
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var interpreted = interpreter.ExecuteInstructions(800, null, new CountingBoundary());
		var compiled = jit.ExecuteInstructions(800, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.HelperIlInstructions > 0);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ReadWord(0x2000), jitBus.ReadWord(0x2000));
		Assert.Equal(interpreterBus.ReadWord(0x2100), jitBus.ReadWord(0x2100));
		Assert.Equal(interpreterBus.ReadWord(0x2200), jitBus.ReadWord(0x2200));
	}

	[Theory]
	[MemberData(nameof(RepresentativeGenericTracePrograms))]
	public void JitAndInterpreterAgreeForRepresentativeGenericTraces(ushort[] words, bool exactCycles)
	{
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
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
		if (exactCycles)
		{
			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
		}
		else
		{
			Assert.InRange(jit.State.Cycles, interpreter.State.Cycles - 4, interpreter.State.Cycles);
		}
		Assert.Equal(interpreter.State.D, jit.State.D);
		Assert.Equal(interpreter.State.A, jit.State.A);
		Assert.Equal(interpreterBus.ChipRam[0x2000..0x2008], jitBus.ChipRam[0x2000..0x2008]);
	}

	[Fact]
	public void ExactM68000MovePostincrementLoopMatchesAtEveryCompiledBoundary()
	{
		ushort[] words =
		[
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x7012,                 // MOVEQ #$12,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x7034,                 // MOVEQ #$34,D0
			0x10C0,                 // MOVE.B D0,(A0)+
			0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
			0x3210,                 // MOVE.W (A0),D1
			0x60E8                  // BRA.S start
		];
		var interpreterBus = CreateCodeBus();
		var jitBus = CreateCodeBus();
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var interpreterStartPc = interpreter.State.ProgramCounter;
			var jitStartPc = jit.State.ProgramCounter;
			var interpreterQueue = ((IM68000PrefetchDiagnostics)interpreter).CapturePrefetchDiagnosticState();
			var jitQueue = ((IM68000PrefetchDiagnostics)jit).CapturePrefetchDiagnosticState();
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			var interpreterCycles = interpreter.ExecuteInstruction();
			var jitCycles = jit.ExecuteInstruction();
			var interpreterAccesses = string.Join(",", interpreterBus.BusAccesses.Skip(interpreterAccessStart).Select(access =>
				$"{access.Request.Kind}:0x{access.Request.Address:X6}@{access.RequestedCycle}-{access.GrantedCycle}-{access.CompletedCycle}"));
			var jitAccesses = string.Join(",", jitBus.BusAccesses.Skip(jitAccessStart).Select(access =>
				$"{access.Request.Kind}:0x{access.Request.Address:X6}@{access.RequestedCycle}-{access.GrantedCycle}-{access.CompletedCycle}"));
			if (interpreterStartPc is FastCodeBase + 0x14 or FastCodeBase + 0x16)
			{
				Assert.True(
					interpreterAccesses == jitAccesses,
					$"scalarQueue={FormatPrefetch(interpreterQueue)}, jitQueue={FormatPrefetch(jitQueue)}, " +
					$"scalarBus={interpreterAccesses}, jitBus={jitAccesses}");
			}
			Assert.True(
				interpreter.State.Cycles == jit.State.Cycles,
				$"instruction={instruction}, scalarPc=0x{interpreterStartPc:X8}, jitPc=0x{jitStartPc:X8}, " +
				$"scalarDelta={interpreterCycles}, jitDelta={jitCycles}, " +
				$"scalarCycle={interpreter.State.Cycles}, jitCycle={jit.State.Cycles}, " +
				$"jitTraces={jit.Counters.CompiledTraces}, jitHits={jit.Counters.TraceHits}, " +
				$"jitFallback={jit.Counters.FallbackInstructions}, " +
				$"scalarQueue={FormatPrefetch(interpreterQueue)}, jitQueue={FormatPrefetch(jitQueue)}, " +
				$"scalarBus={interpreterAccesses}, jitBus={jitAccesses}");
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void ExactM68000MovePostincrementLoopPreservesQueueAcrossMemorySideExit()
	{
		ushort[] words =
		[
			0x207C, 0x0000, 0x2000,
			0x7012,
			0x10C0,
			0x7034,
			0x10C0,
			0x207C, 0x0000, 0x2000,
			0x3210,
			0x60E8
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.ExecuteInstructions(488, null, new CountingBoundary());
		jit.ExecuteInstructions(488, null, new CountingBoundary());
		Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);

		for (var instruction = 488; instruction < 504; instruction++)
		{
			var interpreterStartPc = interpreter.State.ProgramCounter;
			var jitStartPc = jit.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			var interpreterCycles = interpreter.ExecuteInstruction();
			var jitCycles = jit.ExecuteInstruction();
			var interpreterAccesses = string.Join(",", interpreterBus.BusAccesses.Skip(interpreterAccessStart).Select(access =>
				$"{access.Request.Kind}:0x{access.Request.Address:X6}@{access.RequestedCycle}-{access.GrantedCycle}-{access.CompletedCycle}"));
			var jitAccesses = string.Join(",", jitBus.BusAccesses.Skip(jitAccessStart).Select(access =>
				$"{access.Request.Kind}:0x{access.Request.Address:X6}@{access.RequestedCycle}-{access.GrantedCycle}-{access.CompletedCycle}"));
			Assert.True(
				interpreter.State.Cycles == jit.State.Cycles,
				$"instruction={instruction}, scalarPc=0x{interpreterStartPc:X8}, jitPc=0x{jitStartPc:X8}, " +
				$"scalarDelta={interpreterCycles}, jitDelta={jitCycles}, " +
				$"scalarCycle={interpreter.State.Cycles}, jitCycle={jit.State.Cycles}, " +
				$"scalarBus={interpreterAccesses}, jitBus={jitAccesses}");
		}
	}

	[Fact]
	public void ExactM68000MoveWordDataToPostincrementMatchesBusTimelineWhenCompiled()
	{
		ushort[] words =
		[
			0x34C5, // MOVE.W D5,(A2)+
			0x60FC  // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.D[5] = jit.State.D[5] = 0x38C2;
		interpreter.State.A[2] = jit.State.A[2] = 0x2000;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.A[2], jit.State.A[2]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void ExactM68000MoveWordDisplacementSourceConsumesCommittedExtensionWhenCompiled()
	{
		ushort[] words =
		[
			0x3228, 0x0000, // MOVE.W 0(A0),D1
			0x60FA          // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		interpreterBus.WriteHostWord(0x2000, 0xA55A);
		jitBus.WriteHostWord(0x2000, 0xA55A);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.D[1], jit.State.D[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void ExactM68000MoveWordDisplacementDestinationWritesBeforeFinalPrefetchWhenCompiled()
	{
		ushort[] words =
		[
			0x3545, 0x0000, // MOVE.W D5,0(A2)
			0x60FA          // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.D[5] = jit.State.D[5] = 0x38C2;
		interpreter.State.A[2] = jit.State.A[2] = 0x2000;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreterBus.ReadHostWord(0x2000), jitBus.ReadHostWord(0x2000));
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Fact]
	public void ExactM68000MoveWordMemoryToMemoryReadsWritesThenPrefetchesWhenCompiled()
	{
		ushort[] words =
		[
			0x3290, // MOVE.W (A0),(A1)
			0x60FC  // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		interpreterBus.WriteHostWord(0x2000, 0xA55A);
		jitBus.WriteHostWord(0x2000, 0xA55A);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;
		interpreter.State.A[1] = jit.State.A[1] = 0x2100;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreterBus.ReadHostWord(0x2100), jitBus.ReadHostWord(0x2100));
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.Equal(0xA55A, interpreterBus.ReadHostWord(0x2100));
		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x1010)] // MOVE.B (A0),D0
	[InlineData(0x12C0)] // MOVE.B D0,(A1)+
	[InlineData(0x1290)] // MOVE.B (A0),(A1)
	public void ExactM68000MoveByteNoExtensionMatchesBusTimelineWhenCompiled(int opcode)
	{
		ushort[] words =
		[
			(ushort)opcode,
			0x60FC // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		interpreterBus.WriteHostWord(0x2000, 0x5AA5);
		jitBus.WriteHostWord(0x2000, 0x5AA5);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.D[0] = jit.State.D[0] = 0xA5;
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;
		interpreter.State.A[1] = jit.State.A[1] = 0x2100;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.D[0], jit.State.D[0]);
			Assert.Equal(interpreter.State.A[0], jit.State.A[0]);
			Assert.Equal(interpreter.State.A[1], jit.State.A[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x1228)] // MOVE.B 0(A0),D1
	[InlineData(0x1340)] // MOVE.B D0,0(A1)
	public void ExactM68000MoveByteDisplacementMatchesBusTimelineWhenCompiled(int opcode)
	{
		ushort[] words =
		[
			(ushort)opcode, 0x0000,
			0x60FA // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		interpreterBus.WriteHostWord(0x2000, 0x5AA5);
		jitBus.WriteHostWord(0x2000, 0x5AA5);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.D[0] = jit.State.D[0] = 0xA5;
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;
		interpreter.State.A[1] = jit.State.A[1] = 0x2100;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.D[1], jit.State.D[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x3218)] // MOVE.W (A0)+,D1
	[InlineData(0x1218)] // MOVE.B (A0)+,D1
	public void ExactM68000MovePostincrementSourceToDataRegisterMatchesBusTimelineWhenCompiled(int opcode)
	{
		ushort[] words =
		[
			(ushort)opcode,
			0x60FC // BRA.S start
		];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		for (var offset = 0; offset < 0x200; offset += 2)
		{
			interpreterBus.WriteHostWord((uint)(0x2000 + offset), (ushort)(0x5000 + offset));
			jitBus.WriteHostWord((uint)(0x2000 + offset), (ushort)(0x5000 + offset));
		}
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.D[1], jit.State.D[1]);
			Assert.Equal(interpreter.State.A[0], jit.State.A[0]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x3298)] // MOVE.W (A0)+,(A1)
	[InlineData(0x32D0)] // MOVE.W (A0),(A1)+
	[InlineData(0x32D8)] // MOVE.W (A0)+,(A1)+
	[InlineData(0x1298)] // MOVE.B (A0)+,(A1)
	[InlineData(0x12D0)] // MOVE.B (A0),(A1)+
	[InlineData(0x12D8)] // MOVE.B (A0)+,(A1)+
	public void ExactM68000MoveMemoryPostincrementCombinationsMatchBusTimelineWhenCompiled(int opcode)
	{
		ushort[] words = [(ushort)opcode, 0x60FC];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		for (var offset = 0; offset < 0x200; offset += 2)
		{
			interpreterBus.WriteHostWord((uint)(0x2000 + offset), (ushort)(0x6000 + offset));
			jitBus.WriteHostWord((uint)(0x2000 + offset), (ushort)(0x6000 + offset));
		}
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;
		interpreter.State.A[1] = jit.State.A[1] = 0x2400;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.A[0], jit.State.A[0]);
			Assert.Equal(interpreter.State.A[1], jit.State.A[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x3300)] // MOVE.W D0,-(A1)
	[InlineData(0x1300)] // MOVE.B D0,-(A1)
	public void ExactM68000MoveDataRegisterToPredecrementPrefetchesBeforeWriteWhenCompiled(int opcode)
	{
		ushort[] words = [(ushort)opcode, 0x60FC];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.D[0] = jit.State.D[0] = 0xA55A;
		interpreter.State.A[1] = jit.State.A[1] = 0x2400;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.A[1], jit.State.A[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle))
					.ToArray();
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle))
					.ToArray();
				var writeIndex = Array.FindIndex(interpreterAccesses, access => access.Kind == AmigaBusAccessKind.CpuDataWrite);
				Assert.True(writeIndex > 0);
				Assert.Equal(AmigaBusAccessKind.CpuInstructionFetch, interpreterAccesses[writeIndex - 1].Kind);
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x3310)] // MOVE.W (A0),-(A1)
	[InlineData(0x3318)] // MOVE.W (A0)+,-(A1)
	[InlineData(0x1310)] // MOVE.B (A0),-(A1)
	[InlineData(0x1318)] // MOVE.B (A0)+,-(A1)
	public void ExactM68000MoveMemoryToPredecrementReadsPrefetchesThenWritesWhenCompiled(int opcode)
	{
		ushort[] words = [(ushort)opcode, 0x60FC];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		for (var offset = 0; offset < 0x200; offset += 2)
		{
			interpreterBus.WriteHostWord((uint)(0x2000 + offset), (ushort)(0x7000 + offset));
			jitBus.WriteHostWord((uint)(0x2000 + offset), (ushort)(0x7000 + offset));
		}
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;
		interpreter.State.A[1] = jit.State.A[1] = 0x2600;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.A[0], jit.State.A[0]);
			Assert.Equal(interpreter.State.A[1], jit.State.A[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle))
					.ToArray();
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle))
					.ToArray();
				var readIndex = Array.FindIndex(interpreterAccesses, access => access.Kind == AmigaBusAccessKind.CpuDataRead);
				var writeIndex = Array.FindIndex(interpreterAccesses, access => access.Kind == AmigaBusAccessKind.CpuDataWrite);
				Assert.True(readIndex >= 0 && writeIndex > readIndex + 1);
				Assert.Contains(
					interpreterAccesses[(readIndex + 1)..writeIndex],
					access => access.Kind == AmigaBusAccessKind.CpuInstructionFetch);
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x3328)] // MOVE.W 0(A0),-(A1)
	[InlineData(0x1328)] // MOVE.B 0(A0),-(A1)
	public void ExactM68000MoveDisplacementToPredecrementPreservesBothRefillsWhenCompiled(int opcode)
	{
		ushort[] words = [(ushort)opcode, 0x0000, 0x60FA];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		interpreterBus.WriteHostWord(0x2000, 0x7AA7);
		jitBus.WriteHostWord(0x2000, 0x7AA7);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.A[0] = jit.State.A[0] = 0x2000;
		interpreter.State.A[1] = jit.State.A[1] = 0x2600;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.A[1], jit.State.A[1]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle))
					.ToArray();
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle))
					.ToArray();
				var readIndex = Array.FindIndex(interpreterAccesses, access => access.Kind == AmigaBusAccessKind.CpuDataRead);
				var writeIndex = Array.FindIndex(interpreterAccesses, access => access.Kind == AmigaBusAccessKind.CpuDataWrite);
				Assert.True(readIndex > 0 && writeIndex > readIndex + 1);
				Assert.Contains(interpreterAccesses[..readIndex], access => access.Kind == AmigaBusAccessKind.CpuInstructionFetch);
				Assert.Contains(interpreterAccesses[(readIndex + 1)..writeIndex], access => access.Kind == AmigaBusAccessKind.CpuInstructionFetch);
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(jit.Counters.TraceHits > 0);
	}

	[Theory]
	[InlineData(0x303C, 0x0001)] // MOVE.W #1,D0
	[InlineData(0x103C, 0x0001)] // MOVE.B #1,D0
	[InlineData(0x0C40, 0x0001)] // CMPI.W #1,D0
	[InlineData(0x0640, 0x0001)] // ADDI.W #1,D0
	[InlineData(0x0440, 0x0001)] // SUBI.W #1,D0
	[InlineData(0x0240, 0x00FF)] // ANDI.W #$FF,D0
	[InlineData(0x0040, 0x0100)] // ORI.W #$100,D0
	[InlineData(0x0A40, 0x0001)] // EORI.W #1,D0
	[InlineData(0x0800, 0x0000)] // BTST #0,D0
	public void ExactM68000OneExtensionRegisterOperationMatchesBusTimelineWhenCompiled(int opcode, int extension)
	{
		ushort[] words = [(ushort)opcode, (ushort)extension, 0x60FA];
		var interpreterBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		var jitBus = new AmigaBus(expansionRamSize: FastCodeSize, captureBusAccesses: true);
		WriteWords(interpreterBus, FastCodeBase, words);
		WriteWords(jitBus, FastCodeBase, words);
		var interpreter = new M68kInterpreter(interpreterBus);
		var jit = new M68kJitCore(jitBus);
		jit.FallbackAttributionEnabled = true;
		interpreter.Reset(FastCodeBase, 0x4000);
		jit.Reset(FastCodeBase, 0x4000);
		interpreter.State.D[0] = jit.State.D[0] = 0x1234;

		for (var instruction = 0; instruction < 256; instruction++)
		{
			var pc = interpreter.State.ProgramCounter;
			var interpreterAccessStart = interpreterBus.BusAccesses.Count;
			var jitAccessStart = jitBus.BusAccesses.Count;
			interpreter.ExecuteInstruction();
			jit.ExecuteInstruction();

			Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
			Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
			Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
			Assert.Equal(interpreter.State.D[0], jit.State.D[0]);
			if (pc == FastCodeBase)
			{
				var interpreterAccesses = interpreterBus.BusAccesses.Skip(interpreterAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				var jitAccesses = jitBus.BusAccesses.Skip(jitAccessStart)
					.Select(access => (access.Request.Kind, access.Request.Address, access.RequestedCycle, access.CompletedCycle));
				Assert.Equal(interpreterAccesses, jitAccesses);
			}
		}

		Assert.True(
			jit.Counters.TraceHits > 0,
			$"fallbacks={jit.Counters.FallbackReasonTop}; instructions={jit.Counters.FallbackInstructionTop}");
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
			},
			true
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
			},
			true
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
			},
			true
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
			},
			true
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
			},
			true
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
			},
			true
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
			},
			true
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x207C, 0x0000, 0x0000, // MOVEA.L #0,A0
				0x5248,                 // ADDQ.W #1,A0
				0x5348,                 // SUBQ.W #1,A0
				0x7001,                 // MOVEQ #1,D0
				0xD0BC, 0x0000, 0x0001, // ADD.L #1,D0
				0x60EC                  // BRA.S start
			},
			true
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
				0x7001,                 // MOVEQ #1,D0
				0x10C0,                 // MOVE.B D0,(A0)+
				0x60F4                  // BRA.S start
			},
			true
		};
		yield return new object[]
		{
			new ushort[]
			{
				0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
				0x3210,                 // MOVE.W (A0),D1
				0x60F6                  // BRA.S start
			},
			false // One initial queue-refill cycle remains at the fallback-to-trace transition.
		};
	}

	private static string FormatPrefetch(M68000PrefetchDiagnosticState state)
		=> $"pc=0x{state.ProgramCounter:X8},addr=0x{state.PrefetchAddress:X8},count={state.PrefetchCount}," +
			$"ready={state.ReadyCycle0}/{state.ReadyCycle1},bus={state.BusCycle}," +
			$"pending={(state.HasPendingPrefetch ? $"0x{state.PendingPrefetchAddress:X8}@{state.PendingPrefetchEarliestCycle}" : "none")}";

	private static AmigaBus CreateCodeBus()
		=> new AmigaBus(expansionRamSize: FastCodeSize);

	private static bool GetPrivateBool(M68kJitCore cpu, string fieldName)
		=> (bool)typeof(M68kJitCore).GetField(
			fieldName,
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(cpu)!;

	private static AmigaBus CreateRealFastCodeBus()
	{
		var bus = new AmigaBus(realFastRamSize: FastCodeSize);
		bus.ConfigureAutoconfigFastRamForHost();
		return bus;
	}

	private static void EnableM68040InstructionCache(M68kJitCore cpu)
		=> cpu.State.CacheControlRegister |= M68040InstructionCacheEnable;

	private static long GetM68040DirectRamAccessCount(M68kJitCore cpu, bool pseudoFast)
		=> pseudoFast
			? cpu.Counters.M68040DirectPseudoFastReads + cpu.Counters.M68040DirectPseudoFastWrites
			: cpu.Counters.M68040DirectRealFastReads + cpu.Counters.M68040DirectRealFastWrites;

	private static void PrepareM68040CompiledMemoryLoop(M68kJitCore cpu, uint source, uint destination)
	{
		Array.Clear(cpu.State.D);
		Array.Clear(cpu.State.A);
		cpu.State.A[0] = source;
		cpu.State.A[1] = destination;
		cpu.State.A[7] = 0x4000;
		cpu.State.ProgramCounter = RealFastCodeBase;
		cpu.State.LastInstructionProgramCounter = 0;
		cpu.State.LastOpcode = 0;
		cpu.State.StatusRegister = M68kCpuState.ResetStatusRegister;
		cpu.State.Cycles = 0;
		cpu.State.NativeCycles = 0;
		cpu.State.Halted = false;
		cpu.State.Stopped = false;
	}

	private static uint ReadSizedValue(AmigaBus bus, uint address, int byteCount)
		=> byteCount switch
		{
			1 => bus.ReadByte(address),
			2 => bus.ReadWord(address),
			_ => bus.ReadLong(address)
		};

	private static void ExecuteUntilTraceHits(M68kJitCore cpu, PureBatchBoundary boundary, long minimumTraceHits)
	{
		for (var i = 0; i < 250 && cpu.Counters.TraceHits < minimumTraceHits; i++)
		{
			cpu.ExecuteInstructions(64, cpu.State.Cycles + 100_000, boundary);
			if (cpu.Counters.TraceHits < minimumTraceHits)
			{
				Thread.Sleep(1);
			}
		}

		var counters = cpu.Counters;
		Assert.True(
			counters.TraceHits >= minimumTraceHits,
			$"Expected at least {minimumTraceHits} trace hits, hits={counters.TraceHits}, compiled={counters.CompiledTraces}, fallback={counters.FallbackInstructions}, blacklisted={counters.BlacklistCount}, sideExits={counters.SideExits}, asyncQueued={counters.AsyncRequestsQueued}, asyncInstalled={counters.AsyncCompletedInstalled}, asyncFailed={counters.AsyncWorkerCompilesFailed}, unsupportedOpcode={counters.UnsupportedOpcode}, unsupportedEa={counters.UnsupportedEa}, v2Top={counters.V2UnsupportedOperationTop}.");
	}

	private static void WaitForV2TraceHit(M68kJitCore cpu, PureBatchBoundary boundary)
	{
		for (var i = 0; i < 250 && cpu.Counters.V2TraceHits == 0; i++)
		{
			PrepareM68040MovemState(cpu.State, null, i + 1);
			cpu.ExecuteInstructions(2, cpu.State.Cycles + 100_000, boundary);
			if (cpu.Counters.V2TraceHits == 0)
			{
				Thread.Sleep(1);
			}
		}

		Assert.True(
			cpu.Counters.V2TraceHits > 0,
			$"Expected a 040 V2 trace hit, traceHits={cpu.Counters.TraceHits}, compiled={cpu.Counters.CompiledTraces}, fallback={cpu.Counters.FallbackInstructions}.");
	}

	private static void PrepareM68040MovemState(M68kCpuState state, AmigaBus? bus, int seed)
	{
		state.ProgramCounter = RealFastCodeBase;
		state.A[6] = 0x200;
		for (var register = 4; register < 8; register++)
		{
			state.D[register] = 0xD400_0000u + (uint)register;
		}

		if (bus == null)
		{
			return;
		}

		for (var word = 0; word < 10; word++)
		{
			var value = 0x1000_0000u + (uint)(word + seed);
			bus.WriteLong(0x200u + (uint)(word * 4), value);
		}
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

	private static ushort[] CreateM68040Move16Loop(
		uint source,
		uint destination,
		int sourceRegister,
		int destinationRegister)
	{
		var sourceLea = (ushort)(0x41F9 | (sourceRegister << 9));
		var destinationLea = (ushort)(0x41F9 | (destinationRegister << 9));
		return
		[
			sourceLea, (ushort)(source >> 16), (ushort)source,
			destinationLea, (ushort)(destination >> 16), (ushort)destination,
			(ushort)(0xF620 | sourceRegister), (ushort)(destinationRegister << 12),
			0x60EE
		];
	}

	private static void PrepareM68040Move16State(M68kCpuState state)
	{
		Array.Clear(state.D);
		Array.Clear(state.A);
		state.A[7] = 0x4000;
		state.ProgramCounter = RealFastCodeBase;
		state.LastInstructionProgramCounter = 0;
		state.LastOpcode = 0;
		state.StatusRegister = M68kCpuState.ResetStatusRegister;
		state.Cycles = 0;
		state.NativeCycles = 0;
		state.Halted = false;
		state.Stopped = false;
	}

	private static void WriteWords(PlainRamBus bus, uint address, params ushort[] words)
	{
		long cycle = 0;
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i], ref cycle, M68kBusAccessKind.CpuDataWrite);
		}
	}

	private static void WriteWords(byte[] memory, int address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			var offset = address + i * 2;
			memory[offset] = (byte)(words[i] >> 8);
			memory[offset + 1] = (byte)words[i];
		}
	}

	private static void WriteLong(byte[] memory, int address, uint value)
	{
		memory[address] = (byte)(value >> 24);
		memory[address + 1] = (byte)(value >> 16);
		memory[address + 2] = (byte)(value >> 8);
		memory[address + 3] = (byte)value;
	}

	private static (
		M68040Interpreter Interpreter,
		M68kJitCore Jit,
		AmigaBus InterpreterBus,
		AmigaBus JitBus) ExecuteM68040FpuComparison(
		ushort[] words,
		Action<M68kCpuState> initializeState,
		Action<AmigaBus, AmigaBus>? initializeBus = null,
		int instructions = 800)
	{
		var interpreterBus = CreateRealFastCodeBus();
		var jitBus = CreateRealFastCodeBus();
		WriteWords(interpreterBus, RealFastCodeBase, words);
		WriteWords(jitBus, RealFastCodeBase, words);
		initializeBus?.Invoke(interpreterBus, jitBus);
		var interpreter = new M68040Interpreter(interpreterBus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		var jit = (M68kJitCore)AmigaM68kCoreFactory.Default.Create(M68kBackendKind.JitM68040, jitBus);
		interpreter.Reset(RealFastCodeBase, 0x4000);
		jit.Reset(RealFastCodeBase, 0x4000);
		interpreter.State.ResetStackPointers(0x4000, 0, supervisorMode: true);
		jit.State.ResetStackPointers(0x4000, 0, supervisorMode: true);
		initializeState(interpreter.State);
		initializeState(jit.State);
		EnableM68040InstructionCache(jit);

		var boundary = new PureBatchBoundary();
		var compiled = 0;
		for (var i = 0; i < 250 && (compiled < instructions || jit.Counters.TraceHits == 0); i++)
		{
			compiled += jit.ExecuteInstructions(64, jit.State.Cycles + 100_000, boundary);
			if (jit.Counters.TraceHits == 0)
			{
				Thread.Sleep(1);
			}
		}

		var interpreted = interpreter.ExecuteInstructions(compiled, null, new CountingBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
		Assert.True(jit.Counters.CompiledTraces > 0);
		Assert.True(jit.Counters.TraceHits > 0);
		return (interpreter, jit, interpreterBus, jitBus);
	}

	private static void AssertM68040FpRegistersEqual(M68040Interpreter interpreter, M68kJitCore jit)
	{
		for (var i = 0; i < interpreter.State.M68040Fpu.FP.Length; i++)
		{
			Assert.Equal(interpreter.State.M68040Fpu.FP[i], jit.State.M68040Fpu.FP[i]);
		}
	}

	private static void AssertM68040AddressRegistersEqualExceptStack(M68040Interpreter interpreter, M68kJitCore jit)
	{
		for (var i = 0; i < 7; i++)
		{
			Assert.Equal(interpreter.State.A[i], jit.State.A[i]);
		}
	}

	private static void AssertDirectIlMatchesInterpreter(int instructions, params ushort[] words)
	{
		var jitBus = CreateRealFastCodeBus();
		var interpreterBus = CreateRealFastCodeBus();
		WriteWords(jitBus, RealFastCodeBase, words);
		WriteWords(interpreterBus, RealFastCodeBase, words);
		var jit = new M68kJitCore(jitBus, enableV2: false);
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

	private static int CountCpuDataAccesses(
		AmigaBus bus,
		int startIndex,
		uint address,
		AmigaBusAccessKind kind,
		AmigaBusAccessSize size)
	{
		var count = 0;
		for (var i = startIndex; i < bus.BusAccesses.Count; i++)
		{
			var request = bus.BusAccesses[i].Request;
			if (request.Requester == AmigaBusRequester.Cpu &&
				request.Kind == kind &&
				request.Address == address &&
				request.Size == size)
			{
				count++;
			}
		}

		return count;
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

	private class PlainRamBus : IM68kBus
	{
		private readonly byte[] _memory;

		public PlainRamBus(int size)
		{
			_memory = new byte[size];
		}

		protected int MemoryLength => _memory.Length;

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return _memory[Normalize(address)];
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> (ushort)((ReadByte(address, ref cycle, accessKind) << 8) |
				ReadByte(address + 1, ref cycle, accessKind));

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> ((uint)ReadWord(address, ref cycle, accessKind) << 16) |
				ReadWord(address + 2, ref cycle, accessKind);

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			_memory[Normalize(address)] = value;
			OnWrite(address, 1);
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			WriteByte(address, (byte)(value >> 8), ref cycle, accessKind);
			WriteByte(address + 1, (byte)value, ref cycle, accessKind);
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			WriteWord(address, (ushort)(value >> 16), ref cycle, accessKind);
			WriteWord(address + 2, (ushort)value, ref cycle, accessKind);
		}

		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}

		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}

		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}

		protected virtual void OnWrite(uint address, int byteCount)
		{
			_ = address;
			_ = byteCount;
		}

		protected bool IsValidRange(uint address, int byteCount)
			=> byteCount > 0 &&
				address < (uint)_memory.Length &&
				(uint)byteCount <= (uint)_memory.Length - address;

		protected byte[] CopyRange(uint address, int byteCount)
		{
			var bytes = new byte[byteCount];
			Array.Copy(_memory, checked((int)address), bytes, 0, byteCount);
			return bytes;
		}

		protected int Normalize(uint address) => checked((int)(address % (uint)_memory.Length));
	}

	private sealed class GenericJitRamBus : PlainRamBus, IM68kJitBus
	{
		private const int PageSize = 256;
		private readonly uint[] _pageGenerations;

		public GenericJitRamBus(int size)
			: base(size)
		{
			_pageGenerations = new uint[(size + PageSize - 1) / PageSize];
		}

		public event Action<uint, int>? JitCodeRangeWritten;

		public bool IsJitCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			return IsValidRange(physicalAddress, byteCount);
		}

		public bool IsJitReadOnlyCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
		{
			_ = physicalAddress;
			_ = byteCount;
			_ = accessKind;
			return false;
		}

		public ushort ReadJitCodeWord(uint physicalAddress)
		{
			long cycle = 0;
			return ReadWord(physicalAddress, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
		}

		public uint GetJitCodePageGeneration(uint physicalAddress)
			=> _pageGenerations[GetPageIndex(physicalAddress)];

		public bool JitCodeRangeGenerationMatches(
			uint physicalAddress,
			int byteCount,
			uint startGeneration,
			uint endGeneration)
		{
			if (!IsValidRange(physicalAddress, byteCount))
			{
				return false;
			}

			var startPage = GetPageIndex(physicalAddress);
			var endPage = GetPageIndex(physicalAddress + (uint)byteCount - 1);
			return _pageGenerations[startPage] == startGeneration &&
				_pageGenerations[endPage] == endGeneration;
		}

		public bool TryCaptureJitCodeSnapshot(uint physicalRoot, int maxBytes, out M68kJitCodeSnapshot snapshot)
		{
			if (maxBytes <= 0 || physicalRoot >= (uint)MemoryLength)
			{
				snapshot = default;
				return false;
			}

			var byteCount = Math.Min(maxBytes, MemoryLength - checked((int)physicalRoot));
			var startPage = GetPageIndex(physicalRoot);
			var endPage = GetPageIndex(physicalRoot + (uint)byteCount - 1);
			var pageCount = endPage - startPage + 1;
			var pages = new uint[pageCount];
			var generations = new uint[pageCount];
			for (var i = 0; i < pageCount; i++)
			{
				pages[i] = (uint)((startPage + i) * PageSize);
				generations[i] = _pageGenerations[startPage + i];
			}

			snapshot = new M68kJitCodeSnapshot(
				physicalRoot,
				CopyRange(physicalRoot, byteCount),
				new M68kCodeGenerationStamp(pages, generations),
				Array.Empty<uint>());
			return true;
		}

		protected override void OnWrite(uint address, int byteCount)
		{
			if (!IsValidRange(address, byteCount))
			{
				return;
			}

			var startPage = GetPageIndex(address);
			var endPage = GetPageIndex(address + (uint)byteCount - 1);
			for (var page = startPage; page <= endPage; page++)
			{
				_pageGenerations[page]++;
			}

			JitCodeRangeWritten?.Invoke(address, byteCount);
		}

		private int GetPageIndex(uint address) => Math.Min((int)(address / PageSize), _pageGenerations.Length - 1);
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

	private static ExtF80 F80(double value)
		=> ExtF80Math.FromBinary64Bits(unchecked((ulong)BitConverter.DoubleToInt64Bits(value))).Value;

	private static double F64(ExtF80 value)
		=> ExtF80Math.ToDouble(value).Value;

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
