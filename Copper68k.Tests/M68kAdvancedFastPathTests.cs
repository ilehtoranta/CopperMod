using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68kAdvancedFastPathTests
{
	private const uint CodeBase = 0x0000_1000;
	private const uint CacheableCodeBase = 0x0020_0000;

	[Fact]
	public void AdvancedInstructionPipeConsumesThreeWordsAndMetadataInOrder()
	{
		var pipe = new M68kAdvancedInstructionPipe();
		var first = new M68kInstructionFetchMetadata(false, true, 11, 21);
		var second = new M68kInstructionFetchMetadata(true, false, 12, 22);
		var third = new M68kInstructionFetchMetadata(false, false, 13, 23);
		pipe.Reset(CodeBase);
		pipe.Append(CodeBase, 0x1111, first);
		pipe.Append(CodeBase + 2, 0x2222, second);
		pipe.Append(CodeBase + 4, 0x3333, third);

		Assert.Equal(3, pipe.Count);
		Assert.Equal(CodeBase + 6, pipe.NextAddress);
		Assert.True(pipe.TryConsumeKnownHead(out var word0, out var metadata0));
		Assert.True(pipe.TryConsumeKnownHead(out var word1, out var metadata1));
		Assert.True(pipe.TryConsumeKnownHead(out var word2, out var metadata2));
		Assert.Equal((ushort)0x1111, word0);
		Assert.Equal((ushort)0x2222, word1);
		Assert.Equal((ushort)0x3333, word2);
		Assert.Equal(first, metadata0);
		Assert.Equal(second, metadata1);
		Assert.Equal(third, metadata2);
		Assert.True(pipe.IsEmpty);
		Assert.Equal(CodeBase + 6, pipe.NextAddress);
	}

	[Fact]
	public void RegisterFastPlansMatchGeneralExecution()
	{
		var result = ExecutePair(
			[
				0x7001, // MOVEQ #1,D0
				0xD081, // ADD.L D1,D0
				0x5482, // ADDQ.L #2,D2
				0x4E71  // NOP
			],
			state =>
			{
				state.D[1] = 0x10;
				state.D[2] = 0x20;
			},
			instructionCount: 4);

		AssertEquivalent(result);
	}

	[Fact]
	public void TakenByteBranchFastPlanMatchesGeneralExecution()
	{
		var result = ExecutePair([0x60FE], _ => { }, instructionCount: 1);

		AssertEquivalent(result);
	}

	[Fact]
	public void MemoryTransformFastPlansMatchGeneralExecution()
	{
		var result = ExecutePair(
			[
				0x2018, // MOVE.L (A0)+,D0
				0x23C0, 0x0008, 0x0000 // MOVE.L D0,$00080000.L
			],
			state => state.A[0] = 0x0002_0000,
			instructionCount: 2,
			configureBus: bus => bus.WriteLong(0x0002_0000, 0x1020_3040));

		AssertEquivalent(result);
		Assert.Equal(0x1020_3040u, result.FastBus.ReadLong(0x0008_0000));
	}

	[Fact]
	public void M68030RegisterFastPlansMatchGeneralExecution()
	{
		var result = ExecutePair(
			[
				0x7001, // MOVEQ #1,D0
				0xD081, // ADD.L D1,D0
				0x5482, // ADDQ.L #2,D2
				0x4E71  // NOP
			],
			state => state.D[1] = 0x10,
			instructionCount: 4,
			profile: M68020CpuProfile.Ocs68030Accelerator14Mhz);

		AssertEquivalent(result);
	}

	[Fact]
	public void M68040RegisterFastPlansMatchGeneralExecution()
	{
		var result = ExecutePair(
			[
				0x7001, // MOVEQ #1,D0
				0xD081, // ADD.L D1,D0
				0x5482, // ADDQ.L #2,D2
				0x4E71  // NOP
			],
			state => state.D[1] = 0x10,
			instructionCount: 4,
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz);

		AssertEquivalent(result);
	}

	[Fact]
	public void M68040TakenByteBranchFastPlanMatchesGeneralExecution()
	{
		var result = ExecutePair(
			[0x60FE],
			_ => { },
			instructionCount: 1,
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz);

		AssertEquivalent(result);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RegisterHotBlockMatchesGeneralBatchExecution(bool m68030)
	{
		var result = ExecuteBatchPair(
			[
				0x7001, // MOVEQ #1,D0
				0x7202, // MOVEQ #2,D1
				0x7400, // MOVEQ #0,D2
				0xD081, // ADD.L D1,D0
				0x5482, // ADDQ.L #2,D2
				0x4E71, // NOP
				0x60F8  // BRA.S loop
			],
			state => state.D[7] = 0x0F0F_0F0F,
			instructionCount: 70,
			profile: m68030
				? M68020CpuProfile.Ocs68030Accelerator14Mhz
				: M68020CpuProfile.OcsAccelerator14Mhz);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MemoryTransformHotBlockMatchesGeneralBatchExecution(bool m68030)
	{
		var result = ExecuteBatchPair(
			[
				0x207C, 0x0002, 0x0000, // MOVEA.L #$00020000,A0
				0x7402, // MOVEQ #2,D2
				0x2018, // MOVE.L (A0)+,D0
				0xD081, // ADD.L D1,D0
				0x23C0, 0x0008, 0x0000, // MOVE.L D0,$00080000.L
				0x51CA, 0xFFF4 // DBRA D2,loop
			],
			state => state.D[1] = 0x0101_0101,
			instructionCount: 14,
			configureBus: bus =>
			{
				bus.WriteLong(0x0002_0000, 0x1020_3040);
				bus.WriteLong(0x0002_0004, 0x1121_3141);
				bus.WriteLong(0x0002_0008, 0x1222_3242);
			},
			profile: m68030
				? M68020CpuProfile.Ocs68030Accelerator14Mhz
				: M68020CpuProfile.OcsAccelerator14Mhz);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void M68040RegisterHotBlockMatchesGeneralBatchExecution()
	{
		var result = ExecuteBatchPair(
			[
				0x7001, // MOVEQ #1,D0
				0x7202, // MOVEQ #2,D1
				0x7400, // MOVEQ #0,D2
				0xD081, // ADD.L D1,D0
				0x5482, // ADDQ.L #2,D2
				0x4E71, // NOP
				0x60F8  // BRA.S loop
			],
			state => state.D[7] = 0x0F0F_0F0F,
			instructionCount: 70,
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void M68040MemoryTransformHotBlockMatchesGeneralBatchExecution()
	{
		var result = ExecuteBatchPair(
			[
				0x207C, 0x0002, 0x0000, // MOVEA.L #$00020000,A0
				0x7402, // MOVEQ #2,D2
				0x2018, // MOVE.L (A0)+,D0
				0xD081, // ADD.L D1,D0
				0x23C0, 0x0008, 0x0000, // MOVE.L D0,$00080000.L
				0x51CA, 0xFFF4 // DBRA D2,loop
			],
			state => state.D[1] = 0x0101_0101,
			instructionCount: 14,
			configureBus: bus =>
			{
				bus.WriteLong(0x0002_0000, 0x1020_3040);
				bus.WriteLong(0x0002_0004, 0x1121_3141);
				bus.WriteLong(0x0002_0008, 0x1222_3242);
			},
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void HotBlockFallsBackWhenStoreChangesANotYetFetchedOpcode()
	{
		var changedOpcodeAddress = CodeBase + 6;
		var result = ExecuteBatchPair(
			[
				0x23C0, (ushort)(changedOpcodeAddress >> 16), (ushort)changedOpcodeAddress,
				0x4E71,
				0x4E71
			],
			state => state.D[0] = 0x7001_60FE,
			instructionCount: 2);

		AssertEquivalent(result);
		Assert.Equal(1u, result.Fast.State.D[0]);
		Assert.Equal((ushort)0x7001, result.FastBus.ReadWord(changedOpcodeAddress));
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void CacheEnabledHotBlockMatchesGeneralPipeAndFetchBehavior()
	{
		var result = ExecuteBatchPair(
			[
				0x4E7B, 0x0002, // MOVEC D0,CACR
				0x4E71,
				0x4E71,
				0x60FE
			],
			state => state.D[0] = 1,
			instructionCount: 4,
			codeBase: CacheableCodeBase);

		AssertEquivalent(result);
		Assert.True(result.Fast.Timing.InstructionCache.Enabled);
		Assert.Equal(result.GeneralBus.InstructionFetchWords, result.FastBus.InstructionFetchWords);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void M68040CacheEnabledHotBlockMatchesGeneralPipeAndFetchBehavior()
	{
		var result = ExecuteBatchPair(
			[
				0x4E7B, 0x0002, // MOVEC D0,CACR
				0x4E71,
				0x4E71,
				0x60FE
			],
			state => state.D[0] = 1,
			instructionCount: 4,
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz,
			codeBase: CacheableCodeBase);

		AssertEquivalent(result);
		Assert.True(result.Fast.Timing.InstructionCache.Enabled);
		Assert.Equal(result.GeneralBus.InstructionFetchWords, result.FastBus.InstructionFetchWords);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void LongMemoryHotHelpersPreserveProfileWaitStates()
	{
		var profile = M68020CpuProfile.CreateForTesting(
			"wait-state-parity",
			M68kAcceleratorModel.M68020,
			2,
			new M68020BusTimingRule(M68020MemoryTarget.ChipRam, M68020BusWidth.Word, 3));
		var result = ExecuteBatchPair(
			[
				0x2018, // MOVE.L (A0)+,D0
				0x23C0, 0x0008, 0x0000 // MOVE.L D0,$00080000.L
			],
			state => state.A[0] = 0x0002_0000,
			instructionCount: 2,
			configureBus: bus => bus.WriteLong(0x0002_0000, 0x1020_3040),
			profile: profile);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void HotBlockStopsAtTheSameTargetCycleAsGeneralExecution()
	{
		var result = ExecuteBatchPair(
			[0x7001, 0xD081, 0x60FA],
			state => state.D[1] = 2,
			instructionCount: 100,
			targetCycle: 30);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralExecutedInstructions, result.FastExecutedInstructions);
		Assert.InRange(result.FastExecutedInstructions, 1, 99);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void M68040HotBlockStopsAtTheSameTargetCycleAsGeneralExecution()
	{
		var result = ExecuteBatchPair(
			[0x7001, 0xD081, 0x60FA],
			state => state.D[1] = 2,
			instructionCount: 100,
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz,
			targetCycle: 30);

		AssertEquivalent(result);
		Assert.Equal(result.GeneralExecutedInstructions, result.FastExecutedInstructions);
		Assert.InRange(result.FastExecutedInstructions, 1, 99);
		Assert.Equal(result.GeneralBoundaries!.Cycles, result.FastBoundaries!.Cycles);
	}

	[Fact]
	public void HotBlockRecordsInstructionFrequencyLikeGeneralExecution()
	{
		var result = ExecuteBatchPair(
			[0x7001, 0xD081, 0x60FA],
			state => state.D[1] = 2,
			instructionCount: 30,
			enableInstructionFrequency: true);
		var fast = result.Fast.CaptureInstructionFrequency();
		var general = result.General.CaptureInstructionFrequency();

		Assert.Equal(general.TotalInstructions, fast.TotalInstructions);
		Assert.Equal(general.Families, fast.Families);
		Assert.Equal(general.Opcodes, fast.Opcodes);
		Assert.Equal(general.HotPcs, fast.HotPcs);
		Assert.Equal(general.HotLoops, fast.HotLoops);
	}

	[Fact]
	public void M68040HotBlockRecordsInstructionFrequencyLikeGeneralExecution()
	{
		var result = ExecuteBatchPair(
			[0x7001, 0xD081, 0x60FA],
			state => state.D[1] = 2,
			instructionCount: 30,
			profile: M68020CpuProfile.Ocs68040Accelerator25Mhz,
			enableInstructionFrequency: true);
		var fast = result.Fast.CaptureInstructionFrequency();
		var general = result.General.CaptureInstructionFrequency();

		Assert.Equal(general.TotalInstructions, fast.TotalInstructions);
		Assert.Equal(general.Families, fast.Families);
		Assert.Equal(general.Opcodes, fast.Opcodes);
		Assert.Equal(general.HotPcs, fast.HotPcs);
		Assert.Equal(general.HotLoops, fast.HotLoops);
	}

	private static FastPathResult ExecutePair(
		ushort[] program,
		Action<M68kCpuState> configureState,
		int instructionCount,
		Action<ZeroWaitCodeBus>? configureBus = null,
		M68020CpuProfile? profile = null)
	{
		profile ??= M68020CpuProfile.OcsAccelerator14Mhz;
		var fastBus = new ZeroWaitCodeBus();
		var generalBus = new ZeroWaitCodeBus();
		WriteWords(fastBus, CodeBase, program);
		WriteWords(generalBus, CodeBase, program);
		configureBus?.Invoke(fastBus);
		configureBus?.Invoke(generalBus);

		var fast = CreateInterpreter(fastBus, profile, enableAdvancedFastPath: true);
		var general = CreateInterpreter(generalBus, profile, enableAdvancedFastPath: false);
		fast.Reset(CodeBase, 0x0000_3000);
		general.Reset(CodeBase, 0x0000_3000);
		configureState(fast.State);
		configureState(general.State);

		for (var i = 0; i < instructionCount; i++)
		{
			fast.ExecuteInstruction();
			general.ExecuteInstruction();
		}

		return new FastPathResult(
			fast,
			general,
			fastBus,
			generalBus,
			null,
			null,
			instructionCount,
			instructionCount);
	}

	private static FastPathResult ExecuteBatchPair(
		ushort[] program,
		Action<M68kCpuState> configureState,
		int instructionCount,
		Action<ZeroWaitCodeBus>? configureBus = null,
		M68020CpuProfile? profile = null,
		uint codeBase = CodeBase,
		long? targetCycle = null,
		bool enableInstructionFrequency = false)
	{
		profile ??= M68020CpuProfile.OcsAccelerator14Mhz;
		var fastBus = new ZeroWaitCodeBus();
		var generalBus = new ZeroWaitCodeBus();
		WriteWords(fastBus, codeBase, program);
		WriteWords(generalBus, codeBase, program);
		configureBus?.Invoke(fastBus);
		configureBus?.Invoke(generalBus);

		var fast = CreateInterpreter(fastBus, profile, enableAdvancedFastPath: true);
		var general = CreateInterpreter(generalBus, profile, enableAdvancedFastPath: false);
		fast.Reset(codeBase, 0x0000_3000);
		general.Reset(codeBase, 0x0000_3000);
		configureState(fast.State);
		configureState(general.State);
		fast.InstructionFrequencyEnabled = enableInstructionFrequency;
		general.InstructionFrequencyEnabled = enableInstructionFrequency;
		var fastBoundary = new RecordingBoundary();
		var generalBoundary = new RecordingBoundary();

		var fastExecuted = fast.ExecuteInstructions(instructionCount, targetCycle, fastBoundary);
		var generalExecuted = general.ExecuteInstructions(instructionCount, targetCycle, generalBoundary);
		Assert.Equal(generalExecuted, fastExecuted);
		if (!targetCycle.HasValue)
		{
			Assert.Equal(instructionCount, fastExecuted);
		}

		return new FastPathResult(
			fast,
			general,
			fastBus,
			generalBus,
			fastBoundary,
			generalBoundary,
			fastExecuted,
			generalExecuted);
	}

	private static M68kAdvancedTimingInterpreter CreateInterpreter(
		ZeroWaitCodeBus bus,
		M68020CpuProfile profile,
		bool enableAdvancedFastPath)
	{
		if (!enableAdvancedFastPath)
		{
			return profile.Model == M68kAcceleratorModel.M68040
				? new M68040Interpreter(
					bus,
					profile,
					new M68kCpuState(),
					enableAdvancedFastPath: false)
				: new M68kAdvancedTimingInterpreter(
					bus,
					profile,
					new M68kCpuState(),
					enableAdvancedFastPath: false);
		}

		return profile.Model switch
		{
			M68kAcceleratorModel.M68030 => new M68030Interpreter(bus, profile),
			M68kAcceleratorModel.M68040 => new M68040Interpreter(bus, profile),
			_ => new M68020Interpreter(bus, profile)
		};
	}

	private static void AssertEquivalent(FastPathResult result)
	{
		Assert.Equal(result.General.State.ProgramCounter, result.Fast.State.ProgramCounter);
		Assert.Equal(result.General.State.StatusRegister, result.Fast.State.StatusRegister);
		Assert.Equal(result.General.State.Cycles, result.Fast.State.Cycles);
		Assert.Equal(result.General.State.NativeCycles, result.Fast.State.NativeCycles);
		Assert.Equal(result.General.State.LastOpcode, result.Fast.State.LastOpcode);
		Assert.Equal(
			result.General.State.LastInstructionProgramCounter,
			result.Fast.State.LastInstructionProgramCounter);
		Assert.Equal(result.General.State.D, result.Fast.State.D);
		Assert.Equal(result.General.State.A, result.Fast.State.A);
		Assert.Equal(result.GeneralBus.ReadLong(0x0008_0000), result.FastBus.ReadLong(0x0008_0000));
		Assert.Equal(result.General.Timing.LastInstructionTiming, result.Fast.Timing.LastInstructionTiming);
	}

	private sealed class RecordingBoundary : IM68kInstructionBoundary
	{
		internal List<(long Previous, long Current)> Cycles { get; } = [];

		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
			=> Cycles.Add((previousCycle, currentCycle));
	}

	private sealed record FastPathResult(
		M68kAdvancedTimingInterpreter Fast,
		M68kAdvancedTimingInterpreter General,
		ZeroWaitCodeBus FastBus,
		ZeroWaitCodeBus GeneralBus,
		RecordingBoundary? FastBoundaries,
		RecordingBoundary? GeneralBoundaries,
		int FastExecutedInstructions,
		int GeneralExecutedInstructions);
}
