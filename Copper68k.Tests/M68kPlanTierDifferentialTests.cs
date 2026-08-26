/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using Copper68k;

namespace Copper68k.Tests;

/// <summary>
/// Drives the same program through every MC68000 dispatch tier and asserts the
/// architectural state <em>and</em> the retired cycle count come out identical.
/// This is the gate for widening plan/fixed-plan-run opcode coverage: a fast
/// path that disagrees with the scalar decoder by even one cycle fails here.
/// </summary>
public sealed class M68kPlanTierDifferentialTests
{
	private const uint ProgramBase = 0x0000_1000;
	private const uint StackBase = 0x0008_0000;
	private const uint DataBase = 0x0002_0000;

	/// <summary>
	/// Every dispatch configuration reachable through <c>ExecuteInstructions</c>.
	/// </summary>
	public enum Tier
	{
		/// <summary>Plan dispatch disabled: full scalar decode for every instruction.</summary>
		Scalar,

		/// <summary>Kind-table plan dispatch, but no batch-capable boundary, so no run admission.</summary>
		PlannedKindTable,

		/// <summary>Packed-plan dispatch, but no batch-capable boundary, so no run admission.</summary>
		PlannedPacked,

		/// <summary>Kind-table dispatch with a fixed-plan-run bus and a batch-capable boundary.</summary>
		FixedPlanRunKindTable,

		/// <summary>Packed-plan dispatch with a fixed-plan-run bus and a batch-capable boundary.</summary>
		FixedPlanRunPacked,

		/// <summary>Kind-table dispatch, batch-capable boundary, but no fast-memory bus.</summary>
		FixedPlanRunNoFastMemory,

		/// <summary>
		/// Batch-capable boundary and a bus that offers no run window, so cached-run
		/// admission declines and the fixed-plan batch path runs instead.
		/// </summary>
		FixedPlanBatchWithoutWindow
	}

	private static readonly Tier[] AllTiers = Enum.GetValues<Tier>();

	private static readonly Tier[] FastTiers =
	[
		Tier.FixedPlanRunKindTable,
		Tier.FixedPlanRunPacked,
		Tier.FixedPlanRunNoFastMemory,
		Tier.FixedPlanBatchWithoutWindow
	];

	[Theory]
	[InlineData((ushort)0x4E71)] // NOP
	[InlineData((ushort)0x7005)] // MOVEQ #5,D0
	[InlineData((ushort)0x5240)] // ADDQ.W #1,D0
	[InlineData((ushort)0x5280)] // ADDQ.L #1,D0
	[InlineData((ushort)0x8081)] // OR.L D1,D0
	[InlineData((ushort)0xB380)] // EOR.L D1,D0
	[InlineData((ushort)0xC081)] // AND.L D1,D0
	[InlineData((ushort)0xD081)] // ADD.L D1,D0
	public void AlreadyCoveredKindsAgreeAcrossEveryTier(ushort opcode)
	{
		AssertTiersAgree([opcode], expectFastPath: true);
	}

	/// <summary>
	/// Drives one representative opcode for every kind the fixed-plan batch and
	/// the cached run graph claim to support. Adding a kind to
	/// <c>IsFixedPlanBatchKind</c> without implementing it in all four paths
	/// makes this test fail rather than silently corrupting a run.
	/// </summary>
	[Fact]
	public void EveryAdmittedFixedPlanBatchKindIsImplementedInEveryTier()
	{
		var representatives = new SortedDictionary<M68kOpcodePlanKind, ushort>();
		for (var opcode = 0; opcode <= 0xFFFF; opcode++)
		{
			var word = (ushort)opcode;
			if (M68kDispatchTierTests.ClassifyTier(word) != M68kDispatchTier.FixedPlanBatch)
			{
				continue;
			}

			var kind = M68kOpcodePlanTable.Kinds[word];
			// The short unconditional branch is the loop terminator of every
			// case below, so it is covered without a dedicated body.
			if (kind == M68kOpcodePlanKind.ShortUnconditionalBranch)
			{
				continue;
			}

			representatives.TryAdd(kind, word);
		}

		Assert.NotEmpty(representatives);
		foreach (var (kind, opcode) in representatives)
		{
			try
			{
				AssertTiersAgree([opcode], expectFastPath: true);
			}
			catch (Exception error)
			{
				throw new Xunit.Sdk.XunitException(
					$"Kind {kind} (representative opcode 0x{opcode:X4}) is admitted to the " +
					$"fixed-plan batch but does not agree across dispatch tiers: {error.Message}");
			}
		}
	}

	[Fact]
	public void ScalarOnlyOpcodesStillAgreeAcrossEveryTier()
	{
		// One-word register-direct forms the plan table still does not cover.
		// NEGX joins the plan later; NBCD, SWAP, EXT and CMPA are the remaining
		// widening candidates.
		foreach (var opcode in new ushort[] { 0x4800, 0x4840, 0x4880, 0x48C0, 0xB1C0, 0xC2C0 })
		{
			AssertTiersAgree([opcode], expectFastPath: false);
		}
	}

	/// <summary>
	/// Mirror of <see cref="AlreadyCoveredKindsAgreeAcrossEveryTier"/>: proves the
	/// planned-interpreter counters really do distinguish a fast run from a
	/// scalar fallback, so <c>expectFastPath</c> is a meaningful gate rather than
	/// an assertion that always holds.
	/// </summary>
	[Fact]
	public void ScalarOnlyOpcodesReportScalarFallbacksOnTheFastTiers()
	{
		var program = BuildLoop([0x4800]); // NBCD D0
		foreach (var tier in FastTiers)
		{
			var snapshot = Run(tier, program, DefaultSeed, instructions: 64);
			Assert.True(
				snapshot.ScalarFallbackInstructions > 0,
				$"{tier} unexpectedly executed NBCD D0 without a scalar fallback.");
		}
	}

	/// <summary>
	/// CLR/NEG/NOT/TST on a data register are handled by the planned dispatch, so
	/// the planned tiers must not report a scalar fallback for them.
	/// </summary>
	[Fact]
	public void DataRegisterUnaryFormsAvoidTheScalarDecoderOnPlannedTiers()
	{
		foreach (var opcode in new ushort[] { 0x4200, 0x4240, 0x4280, 0x4400, 0x4600, 0x4680, 0x4A00, 0x4A80 })
		{
			var program = BuildLoop([opcode]);
			foreach (var tier in new[] { Tier.PlannedKindTable, Tier.PlannedPacked })
			{
				var snapshot = Run(tier, program, DefaultSeed, instructions: 64);
				Assert.True(
					snapshot.ScalarFallbackInstructions == 0,
					$"{tier} fell back to the scalar decoder for 0x{opcode:X4} " +
					$"({snapshot.ScalarFallbackInstructions} times).");
			}
		}
	}

	/// <summary>
	/// NEGX only ever <em>clears</em> the zero flag, so NEGX of zero with X clear
	/// must leave an already-set Z untouched. Each tier must reproduce that.
	/// </summary>
	[Theory]
	[InlineData((ushort)0x4000)] // NEGX.B D0
	[InlineData((ushort)0x4040)] // NEGX.W D0
	[InlineData((ushort)0x4080)] // NEGX.L D0
	public void NegateWithExtendOnlyClearsZero(ushort opcode)
	{
		foreach (var extend in new[] { false, true })
		{
			var capturedExtend = extend;
			AssertTiersAgree(
				[opcode],
				expectFastPath: true,
				seed: state =>
				{
					DefaultSeed(state);
					state.D[0] = 0;
					// Enter with Z set, which NEGX must preserve when the result
					// is zero and clear when it is not.
					state.StatusRegister = (ushort)(capturedExtend
						? state.StatusRegister | M68kCpuState.Zero | M68kCpuState.Extend
						: (state.StatusRegister | M68kCpuState.Zero) & ~M68kCpuState.Extend);
				},
				instructions: 4);
		}
	}

	/// <summary>
	/// Register shifts and rotates across every operation, direction, size, and
	/// both count sources. The retired cycle count varies with the shift count
	/// (six or eight plus two per bit), so this also pins variable-cycle
	/// accounting inside the cached run.
	/// </summary>
	[Theory]
	[InlineData(0)] // ASR / ASL
	[InlineData(1)] // LSR / LSL
	[InlineData(2)] // ROXR / ROXL
	[InlineData(3)] // ROR / ROL
	public void RegisterShiftFormsAgreeAcrossEveryTier(int type)
	{
		foreach (var left in new[] { false, true })
		{
			for (var sizeCode = 0; sizeCode <= 2; sizeCode++)
			{
				foreach (var countField in new[] { 0, 1, 5, 7 })
				{
					// A register-sourced shift count is deliberately not admitted
					// to the batch or run graph, because its cycle cost depends on
					// register state; it stays on the planned dispatch.
					foreach (var registerCount in new[] { false, true })
					{
						var opcode = (ushort)(0xE000 |
							(countField << 9) |
							(left ? 0x0100 : 0) |
							(sizeCode << 6) |
							(registerCount ? 0x0020 : 0) |
							(type << 3) |
							1); // destination D1
						AssertTiersAgree([opcode], expectFastPath: !registerCount, instructions: 16);
					}
				}
			}
		}
	}

	/// <summary>
	/// The extend flag is both an input and an output for ROXL/ROXR, and a zero
	/// register count must leave it untouched. Both directions and both entry
	/// states have to agree across tiers.
	/// </summary>
	[Theory]
	[InlineData((ushort)0xE2B0)] // ROXR.L D1,D0
	[InlineData((ushort)0xE3B0)] // ROXL.L D1,D0
	[InlineData((ushort)0xE290)] // ROXR.L #1,D0
	[InlineData((ushort)0xE390)] // ROXL.L #1,D0
	public void RotateWithExtendAgreesForEveryExtendState(ushort opcode)
	{
		foreach (var count in new uint[] { 0, 1, 31, 63 })
		{
			foreach (var extend in new[] { false, true })
			{
				var capturedCount = count;
				var capturedExtend = extend;
				AssertTiersAgree(
					[opcode],
					expectFastPath: true,
					seed: state =>
					{
						DefaultSeed(state);
						state.D[0] = 0x8000_0001;
						state.D[1] = capturedCount;
						state.StatusRegister = (ushort)(capturedExtend
							? state.StatusRegister | M68kCpuState.Extend
							: state.StatusRegister & ~M68kCpuState.Extend);
					},
					instructions: 8);
			}
		}
	}

	/// <summary>
	/// The exact shift sequence and seed state used by the
	/// <c>directcpu-immediate-shift-loop</c> benchmark workload, which mixes
	/// sizes, directions, immediate and register counts, and enters with the
	/// extend flag set.
	/// </summary>
	[Theory]
	[InlineData((ushort)0xE380)] // ASL.L #1,D0
	[InlineData((ushort)0xE440)] // ASR.W #2,D0
	[InlineData((ushort)0xE709)] // LSL.B #3,D1
	[InlineData((ushort)0xE889)] // LSR.L #4,D1
	[InlineData((ushort)0xEB5A)] // ROL.W #5,D2
	[InlineData((ushort)0xEC1A)] // ROR.B #6,D2
	[InlineData((ushort)0xEF93)] // ROXL.L #7,D3
	[InlineData((ushort)0xE053)] // ROXR.W D0,D3
	public void BenchmarkShiftSequenceAgreesAcrossEveryTier(ushort opcode)
	{
		AssertTiersAgree(
			[opcode],
			expectFastPath: true,
			seed: state =>
			{
				state.D[0] = 0x8123_4567;
				state.D[1] = 0x89AB_CDEF;
				state.D[2] = 0x1357_9BDF;
				state.D[3] = 0x2468_ACE0;
				state.StatusRegister |= M68kCpuState.Extend;
			},
			instructions: 16);
	}

	[Theory]
	[InlineData(4)]
	[InlineData(13)]
	[InlineData(90)]
	[InlineData(400_000)]
	public void BenchmarkShiftLoopAgreesAcrossEveryTier(int instructions)
	{
		AssertTiersAgree(
			[0xE380, 0xE440, 0xE709, 0xE889, 0xEB5A, 0xEC1A, 0xEF93, 0xE053],
			expectFastPath: true,
			seed: state =>
			{
				state.D[0] = 0x8123_4567;
				state.D[1] = 0x89AB_CDEF;
				state.D[2] = 0x1357_9BDF;
				state.D[3] = 0x2468_ACE0;
				state.StatusRegister |= M68kCpuState.Extend;
			},
			instructions: instructions);
	}

	/// <summary>
	/// Register-direct bit operations bypass the effective-address operand, so
	/// every operation, register, and bit number (including numbers above 31,
	/// which wrap) must still agree with the scalar decoder.
	/// </summary>
	[Theory]
	[InlineData(0)] // BTST
	[InlineData(1)] // BCHG
	[InlineData(2)] // BCLR
	[InlineData(3)] // BSET
	public void RegisterBitOperationsAgreeAcrossEveryTier(int operation)
	{
		// Dynamic form: Bxxx Dn,Dm.
		for (var register = 0; register < 8; register++)
		{
			var opcode = (ushort)(0x0100 | (2 << 9) | (operation << 6) | register);
			foreach (var bit in new uint[] { 0, 1, 31, 32, 63, 0xFF })
			{
				var capturedBit = bit;
				AssertTiersAgree(
					[opcode],
					expectFastPath: false,
					seed: state =>
					{
						DefaultSeed(state);
						state.D[2] = capturedBit;
					},
					instructions: 8);
			}
		}

		// Immediate form: Bxxx #bit,Dn.
		for (var register = 0; register < 8; register++)
		{
			foreach (var bit in new ushort[] { 0, 1, 15, 31 })
			{
				AssertTiersAgree(
					[(ushort)(0x0800 | (operation << 6) | register), bit],
					expectFastPath: false,
					instructions: 8);
			}
		}
	}

	/// <summary>
	/// CMPA, ADDA, and SUBA with a register source bypass the effective-address
	/// operand. Word and long forms, data- and address-register sources, and
	/// values that differ in sign after word sign-extension must all agree.
	/// </summary>
	[Theory]
	[InlineData(0xB0C0)] // CMPA.W
	[InlineData(0xB1C0)] // CMPA.L
	[InlineData(0x90C0)] // SUBA.W
	[InlineData(0x91C0)] // SUBA.L
	[InlineData(0xD0C0)] // ADDA.W
	[InlineData(0xD1C0)] // ADDA.L
	public void RegisterSourceAddressArithmeticAgreesAcrossEveryTier(int baseOpcode)
	{
		foreach (var sourceMode in new[] { 0, 1 })
		{
			for (var sourceRegister = 0; sourceRegister < 8; sourceRegister++)
			{
				for (var destinationRegister = 0; destinationRegister < 7; destinationRegister++)
				{
					var opcode = (ushort)(baseOpcode |
						(destinationRegister << 9) |
						(sourceMode << 3) |
						sourceRegister);
					AssertTiersAgree([opcode], expectFastPath: false, instructions: 8);
				}
			}
		}
	}

	[Theory]
	[InlineData((ushort)0xB0C0)] // CMPA.W D0,A0
	[InlineData((ushort)0xB1C0)] // CMPA.L D0,A0
	[InlineData((ushort)0xD0C0)] // ADDA.W D0,A0
	[InlineData((ushort)0x90C0)] // SUBA.W D0,A0
	public void RegisterSourceAddressArithmeticAgreesForSignExtensionEdges(ushort opcode)
	{
		foreach (var seed in new uint[] { 0, 1, 0x7FFF, 0x8000, 0xFFFF, 0x0001_8000, 0x8000_0000, 0xFFFF_FFFF })
		{
			var capturedSeed = seed;
			AssertTiersAgree(
				[opcode],
				expectFastPath: false,
				seed: state =>
				{
					DefaultSeed(state);
					state.D[0] = capturedSeed;
				},
				instructions: 8);
		}
	}

	/// <summary>
	/// ADDQ/SUBQ with A7 as the destination is an admitted kind, and the scalar
	/// decoder routes A7 writes through <c>SetActiveStackPointer</c>, which also
	/// mirrors the value into the saved supervisor or user stack pointer. The
	/// cached run commits its register snapshot directly, so this checks the
	/// mirror does not go stale — a stale mirror resurfaces as a corrupted A7 on
	/// the next supervisor/user transition.
	/// </summary>
	[Theory]
	[InlineData((ushort)0x5E8F)] // ADDQ.L #7,A7
	[InlineData((ushort)0x5F8F)] // SUBQ.L #7,A7
	[InlineData((ushort)0x5E4F)] // ADDQ.W #7,A7
	public void StackPointerWritesInsideACachedRunKeepTheSavedCopyInSync(ushort opcode)
	{
		var program = BuildLoop([opcode]);
		var reference = RunWithStackPointers(Tier.Scalar, program);
		foreach (var tier in FastTiers)
		{
			var actual = RunWithStackPointers(tier, program);
			Assert.Equal(reference.A7, actual.A7);
			Assert.True(
				reference.SupervisorStackPointer == actual.SupervisorStackPointer,
				$"{tier}: saved supervisor stack pointer is 0x{actual.SupervisorStackPointer:X8} " +
				$"but A7 is 0x{actual.A7:X8} (scalar keeps both at 0x{reference.A7:X8}). " +
				"The next supervisor/user transition would restore the stale copy.");
		}
	}

	private static (uint A7, uint SupervisorStackPointer) RunWithStackPointers(Tier tier, ushort[] program)
	{
		var bus = tier == Tier.FixedPlanBatchWithoutWindow
			? new PlanTierWindowlessTestBus()
			: tier == Tier.FixedPlanRunNoFastMemory
				? new PlanTierTestBus()
				: (PlanTierTestBus)new PlanTierFastMemoryTestBus();
		bus.WriteWords(ProgramBase, program);
		var state = new M68kCpuState();
		using var cpu = new M68kInterpreter(
			bus,
			state,
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: tier != Tier.Scalar,
			opcodePlanDispatch: tier is Tier.PlannedPacked or Tier.FixedPlanRunPacked
				? M68kOpcodePlanDispatch.PackedPlan
				: M68kOpcodePlanDispatch.KindTable);
		cpu.Reset(ProgramBase, StackBase);
		IM68kInstructionBoundary boundary = tier == Tier.Scalar
			? new PlanTierPlainBoundary()
			: new PlanTierBatchBoundary();
		var executed = 0;
		while (executed < 64)
		{
			var step = ((IM68kBatchCore)cpu).ExecuteInstructions(64 - executed, long.MaxValue, boundary);
			if (step == 0)
			{
				break;
			}

			executed += step;
		}

		return (state.A[7], state.SupervisorStackPointer);
	}

	/// <summary>
	/// Proves the register unary forms are carried by the cached fixed-plan-run
	/// graph rather than merely by the planned scalar dispatch: the batch-capable
	/// boundary must report the whole loop as pure-CPU batch instructions.
	/// </summary>
	[Theory]
	[InlineData((ushort)0x4200)] // CLR.B D0
	[InlineData((ushort)0x4280)] // CLR.L D0
	[InlineData((ushort)0x4480)] // NEG.L D0
	[InlineData((ushort)0x4680)] // NOT.L D0
	[InlineData((ushort)0x4A80)] // TST.L D0
	public void DataRegisterUnaryRunsInsideTheCachedFixedPlanRunGraph(ushort opcode)
	{
		var program = BuildLoop([opcode]);
		foreach (var tier in new[] { Tier.FixedPlanRunKindTable, Tier.FixedPlanRunPacked, Tier.FixedPlanRunNoFastMemory })
		{
			var snapshot = Run(tier, program, DefaultSeed, instructions: 64);
			Assert.Equal(0, snapshot.ScalarFallbackInstructions);
			Assert.True(
				snapshot.PureCpuBatchInstructions >= 62,
				$"{tier} only ran {snapshot.PureCpuBatchInstructions} of 64 instructions through the " +
				$"cached fixed-plan-run graph for 0x{opcode:X4}.");
		}
	}

	/// <summary>
	/// Every CLR/NEG/NOT/TST register form, across all eight registers and all
	/// three sizes, must agree with the scalar decoder in state and cycles.
	/// </summary>
	[Theory]
	[InlineData(0x4000)] // NEGX
	[InlineData(0x4200)] // CLR
	[InlineData(0x4400)] // NEG
	[InlineData(0x4600)] // NOT
	[InlineData(0x4A00)] // TST
	public void DataRegisterUnaryFormsAgreeAcrossEveryTier(int group)
	{
		for (var sizeCode = 0; sizeCode <= 2; sizeCode++)
		{
			for (var register = 0; register < 8; register++)
			{
				var opcode = (ushort)(group | (sizeCode << 6) | register);
				AssertTiersAgree([opcode], expectFastPath: true);
			}
		}
	}

	/// <summary>
	/// The same forms seeded with values that exercise every flag transition:
	/// zero, the sized sign bits, all ones, and values whose low byte and word
	/// disagree in sign with the full long.
	/// </summary>
	[Theory]
	[InlineData((ushort)0x4000)] // NEGX.B D0
	[InlineData((ushort)0x4040)] // NEGX.W D0
	[InlineData((ushort)0x4080)] // NEGX.L D0
	[InlineData((ushort)0x4200)] // CLR.B D0
	[InlineData((ushort)0x4240)] // CLR.W D0
	[InlineData((ushort)0x4280)] // CLR.L D0
	[InlineData((ushort)0x4400)] // NEG.B D0
	[InlineData((ushort)0x4440)] // NEG.W D0
	[InlineData((ushort)0x4480)] // NEG.L D0
	[InlineData((ushort)0x4600)] // NOT.B D0
	[InlineData((ushort)0x4640)] // NOT.W D0
	[InlineData((ushort)0x4680)] // NOT.L D0
	[InlineData((ushort)0x4A00)] // TST.B D0
	[InlineData((ushort)0x4A40)] // TST.W D0
	[InlineData((ushort)0x4A80)] // TST.L D0
	public void DataRegisterUnaryFormsAgreeForEveryFlagSeed(ushort opcode)
	{
		var seeds = new uint[]
		{
			0x0000_0000,
			0x0000_0001,
			0x0000_0080,
			0x0000_8000,
			0x8000_0000,
			0xFFFF_FFFF,
			0x7FFF_FF80,
			0x0000_FF00
		};

		foreach (var seed in seeds)
		{
			foreach (var extend in new[] { false, true })
			{
				var capturedSeed = seed;
				var capturedExtend = extend;
				AssertTiersAgree(
					[opcode],
					expectFastPath: true,
					seed: state =>
					{
						DefaultSeed(state);
						state.D[0] = capturedSeed;
						state.StatusRegister = (ushort)(capturedExtend
							? state.StatusRegister | M68kCpuState.Extend
							: state.StatusRegister & ~M68kCpuState.Extend);
					},
					instructions: 8);
			}
		}
	}

	/// <summary>
	/// Runs <paramref name="body"/> in a tight loop terminated by BRA.S back to
	/// the loop head, then compares every tier against the scalar reference.
	/// </summary>
	internal static void AssertTiersAgree(
		ushort[] body,
		bool expectFastPath,
		Action<M68kCpuState>? seed = null,
		int instructions = 64)
	{
		seed ??= DefaultSeed;
		var program = BuildLoop(body);
		var reference = Run(Tier.Scalar, program, seed, instructions);
		foreach (var tier in AllTiers)
		{
			if (tier == Tier.Scalar)
			{
				continue;
			}

			var actual = Run(tier, program, seed, instructions);
			AssertSnapshotsEqual(reference, actual, tier, body);
		}

		// The planned-interpreter counters suppress AdvanceCachedFixedPlanRunLoop,
		// so a counters-enabled run never exercises the loop fast-forward. Repeat
		// the fast tiers with counters off: that path replays a loop body through
		// a separate architectural-operation switch, and a kind missing from it
		// silently drops its register effect while still charging its cycles.
		foreach (var tier in FastTiers)
		{
			var actual = Run(tier, program, seed, instructions, enableCounters: false);
			AssertSnapshotsEqual(reference, actual, tier, body);
		}

		if (!expectFastPath)
		{
			return;
		}

		foreach (var tier in FastTiers)
		{
			var actual = Run(tier, program, seed, instructions);
			Assert.True(
				actual.ScalarFallbackInstructions == 0,
				$"{tier} fell back to the scalar decoder {actual.ScalarFallbackInstructions} times " +
				$"for body {Describe(body)}; the fast path was not actually exercised.");
			Assert.True(
				actual.FastInstructions > 0,
				$"{tier} reported no planned-fast instructions for body {Describe(body)}.");
		}
	}

	private static void AssertSnapshotsEqual(
		TierSnapshot expected,
		TierSnapshot actual,
		Tier tier,
		ushort[] body)
	{
		var context = $"tier={tier} body={Describe(body)}";
		Assert.Equal(expected.ProgramCounter, actual.ProgramCounter);
		for (var i = 0; i < 8; i++)
		{
			Assert.True(
				expected.D[i] == actual.D[i],
				$"D{i} differs ({context}): expected 0x{expected.D[i]:X8}, actual 0x{actual.D[i]:X8}");
			Assert.True(
				expected.A[i] == actual.A[i],
				$"A{i} differs ({context}): expected 0x{expected.A[i]:X8}, actual 0x{actual.A[i]:X8}");
		}

		Assert.True(
			expected.StatusRegister == actual.StatusRegister,
			$"SR differs ({context}): expected 0x{expected.StatusRegister:X4}, actual 0x{actual.StatusRegister:X4}");
		Assert.True(
			expected.SupervisorStackPointer == actual.SupervisorStackPointer,
			$"saved supervisor stack pointer differs ({context}): " +
			$"expected 0x{expected.SupervisorStackPointer:X8}, actual 0x{actual.SupervisorStackPointer:X8}");
		Assert.True(
			expected.UserStackPointer == actual.UserStackPointer,
			$"saved user stack pointer differs ({context}): " +
			$"expected 0x{expected.UserStackPointer:X8}, actual 0x{actual.UserStackPointer:X8}");
		Assert.True(
			expected.Cycles == actual.Cycles,
			$"retired cycles differ ({context}): expected {expected.Cycles}, actual {actual.Cycles}");
		Assert.True(
			expected.ExecutedInstructions == actual.ExecutedInstructions,
			$"executed instruction count differs ({context}): " +
			$"expected {expected.ExecutedInstructions}, actual {actual.ExecutedInstructions}");
	}

	private static ushort[] BuildLoop(ushort[] body)
	{
		var displacement = -(2 * (body.Length + 1));
		var program = new ushort[body.Length + 1];
		Array.Copy(body, program, body.Length);
		program[body.Length] = (ushort)(0x6000 | (byte)(sbyte)displacement);
		return program;
	}

	private static void DefaultSeed(M68kCpuState state)
	{
		state.D[0] = 0x8123_4567;
		state.D[1] = 0x0000_00FF;
		state.D[2] = 0x89AB_CDEF;
		state.D[3] = 0x0000_0010;
		state.D[4] = 0xFFFF_FFFF;
		state.D[5] = 0x0000_0001;
		state.D[6] = 0x7FFF_8000;
		state.D[7] = 0x0000_0000;
		state.A[0] = DataBase;
		state.A[1] = DataBase + 0x40;
		state.A[2] = DataBase + 0x80;
		state.A[3] = DataBase + 0xC0;
		state.A[4] = DataBase + 0x100;
		state.A[5] = DataBase + 0x140;
		state.A[6] = DataBase + 0x180;
	}

	private static TierSnapshot Run(
		Tier tier,
		ushort[] program,
		Action<M68kCpuState> seed,
		int instructions,
		bool enableCounters = true)
	{
		PlanTierTestBus bus = tier switch
		{
			Tier.FixedPlanRunNoFastMemory => new PlanTierTestBus(),
			Tier.FixedPlanBatchWithoutWindow => new PlanTierWindowlessTestBus(),
			_ => new PlanTierFastMemoryTestBus()
		};
		bus.WriteWords(ProgramBase, program);
		for (var offset = 0u; offset < 0x200u; offset += 4)
		{
			bus.WriteLong(DataBase + offset, 0x1122_3344u + offset);
		}

		var state = new M68kCpuState();
		using var cpu = new M68kInterpreter(
			bus,
			state,
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: tier != Tier.Scalar,
			opcodePlanDispatch: tier is Tier.PlannedPacked or Tier.FixedPlanRunPacked
				? M68kOpcodePlanDispatch.PackedPlan
				: M68kOpcodePlanDispatch.KindTable);
		cpu.Reset(ProgramBase, StackBase);
		seed(state);
		cpu.PlannedInterpreterCountersEnabled = enableCounters;
		cpu.ResetPlannedInterpreterCounters();

		IM68kInstructionBoundary boundary = tier is
			Tier.FixedPlanRunKindTable or
			Tier.FixedPlanRunPacked or
			Tier.FixedPlanRunNoFastMemory or
			Tier.FixedPlanBatchWithoutWindow
			? new PlanTierBatchBoundary()
			: new PlanTierPlainBoundary();

		var executed = 0;
		while (executed < instructions)
		{
			var step = ((IM68kBatchCore)cpu).ExecuteInstructions(
				instructions - executed,
				long.MaxValue,
				boundary);
			if (step == 0)
			{
				break;
			}

			executed += step;
		}

		var counters = cpu.CapturePlannedInterpreterCounters();
		var batchBoundary = boundary as PlanTierBatchBoundary;
		return new TierSnapshot(
			(uint[])state.D.Clone(),
			(uint[])state.A.Clone(),
			state.StatusRegister,
			state.ProgramCounter,
			state.Cycles,
			executed,
			counters.FastInstructions,
			counters.ScalarFallbackInstructions,
			batchBoundary?.PureCpuBatchInstructions ?? 0,
			batchBoundary?.BusAccessBatchInstructions ?? 0,
			state.SupervisorStackPointer,
			state.UserStackPointer);
	}

	private static string Describe(ushort[] body)
		=> string.Join(
			' ',
			body.Select(word => $"0x{word:X4}({M68kInstructionClassifier.GetMnemonic(word)})"));

	private sealed record TierSnapshot(
		uint[] D,
		uint[] A,
		ushort StatusRegister,
		uint ProgramCounter,
		long Cycles,
		int ExecutedInstructions,
		long FastInstructions,
		long ScalarFallbackInstructions,
		int PureCpuBatchInstructions,
		int BusAccessBatchInstructions,
		uint SupervisorStackPointer,
		uint UserStackPointer);
}

/// <summary>
/// Zero-wait-state bus that exposes a fixed-plan-run window so the cached run
/// graph can be admitted, but no fast-memory path.
/// </summary>
internal class PlanTierTestBus :
	IM68kBus,
	IM68kCodeReader,
	IM68kFixedPlanRunBus,
	IM68kFixedPhysicalAddressMap
{
	private const int WindowShift = 8;
	private const uint WindowSize = 1u << WindowShift;

	protected readonly byte[] Memory = new byte[0x0010_0000];
	private readonly uint[][] _windowGenerations;

	public PlanTierTestBus()
	{
		_windowGenerations = new uint[Memory.Length >> WindowShift][];
		for (var i = 0; i < _windowGenerations.Length; i++)
		{
			_windowGenerations[i] = [1u];
		}
	}

	public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		return Memory[Offset(address)];
	}

	public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		return ReadWord(address);
	}

	public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		return ReadLong(address);
	}

	public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		Memory[Offset(address)] = value;
	}

	public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		WriteWord(address, value);
	}

	public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		WriteLong(address, value);
	}

	public bool HasHostGateway(uint address)
	{
		_ = address;
		return false;
	}

	public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
	{
		_ = instructionProgramCounter;
		_ = token;
		_ = state;
		return false;
	}

	public void ResetExternalDevices(long cycle)
	{
		_ = cycle;
	}

	public ushort ReadHostWord(uint address)
		=> ReadWord(address);

	/// <summary>
	/// Whether this bus offers fixed-plan-run windows. Overridden to disable
	/// cached-run admission so the fixed-plan batch path can be reached.
	/// </summary>
	protected virtual bool ProvidesFixedPlanRunWindow => true;

	public bool TryGetFixedPlanRunWindow(uint address, out M68kFixedPlanRunWindow window)
	{
		window = default;
		if (!ProvidesFixedPlanRunWindow || (address & 1) != 0 || address + 1 >= Memory.Length)
		{
			return false;
		}

		var startAddress = address & ~(WindowSize - 1);
		var endAddress = Math.Min(startAddress + WindowSize, (uint)Memory.Length);
		var generationSource = _windowGenerations[startAddress >> WindowShift];
		var fetchWindow = new M68kInstructionFetchWindow(
			Memory,
			(int)startAddress,
			startAddress,
			endAddress,
			(uint)(Memory.Length - 1),
			0,
			generationSource,
			generationSource[0]);
		window = new M68kFixedPlanRunWindow(
			in fetchWindow,
			readyCycleOffset: 0,
			nextBusCycleOffset: 0,
			deferredBatchEligible: false);
		return true;
	}

	public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		return address + (uint)byteCount <= Memory.Length;
	}

	public bool TryGetCpuPhysicalAddressMappedRange(
		M68kBusAccessKind accessKind,
		out uint startAddress,
		out uint endAddress)
	{
		_ = accessKind;
		startAddress = 0;
		endAddress = (uint)(Memory.Length - 1);
		return true;
	}

	public void WriteWords(uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	public ushort ReadWord(uint address)
	{
		var offset = Offset(address);
		return (ushort)((Memory[offset] << 8) | Memory[offset + 1]);
	}

	public uint ReadLong(uint address)
		=> ((uint)ReadWord(address) << 16) | ReadWord(address + 2);

	public void WriteWord(uint address, ushort value)
	{
		var offset = Offset(address);
		Memory[offset] = (byte)(value >> 8);
		Memory[offset + 1] = (byte)value;
	}

	public void WriteLong(uint address, uint value)
	{
		WriteWord(address, (ushort)(value >> 16));
		WriteWord(address + 2, (ushort)value);
	}

	protected int Offset(uint address)
		=> (int)(address & (uint)(Memory.Length - 1));
}

/// <summary>
/// <see cref="PlanTierTestBus"/> plus the fast-memory capability, which unlocks
/// the fast-memory arm of cached run admission.
/// </summary>
internal sealed class PlanTierFastMemoryTestBus : PlanTierTestBus, IM68kFastMemoryBus
{
	public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
	{
		_ = accessKind;
		value = Memory[Offset(address)];
		return true;
	}

	public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
	{
		_ = accessKind;
		value = ReadWord(address);
		return true;
	}

	public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
	{
		_ = accessKind;
		value = ReadLong(address);
		return true;
	}

	public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		Memory[Offset(address)] = value;
		return true;
	}

	public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		WriteWord(address, value);
		return true;
	}

	public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		WriteLong(address, value);
		return true;
	}
}

/// <summary>
/// A fixed-plan-run bus that never yields a run window. Cached-run admission
/// therefore declines and execution falls to <c>TryExecuteFixedPlanBatch</c>,
/// which is otherwise unreachable because the cached run graph always wins.
/// Real hosts hit this whenever a program counter sits outside a window-backed
/// region.
/// </summary>
internal sealed class PlanTierWindowlessTestBus : PlanTierTestBus, IM68kFastMemoryBus
{
	protected override bool ProvidesFixedPlanRunWindow => false;

	public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
	{
		_ = accessKind;
		value = Memory[Offset(address)];
		return true;
	}

	public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
	{
		_ = accessKind;
		value = ReadWord(address);
		return true;
	}

	public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
	{
		_ = accessKind;
		value = ReadLong(address);
		return true;
	}

	public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		Memory[Offset(address)] = value;
		return true;
	}

	public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		WriteWord(address, value);
		return true;
	}

	public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		WriteLong(address, value);
		return true;
	}
}

/// <summary>Boundary without batch capabilities: keeps execution off the run paths.</summary>
internal sealed class PlanTierPlainBoundary : IM68kInstructionBoundary
{
	public bool BeforeInstruction()
		=> true;

	public void AfterInstruction(long previousCycle, long currentCycle)
	{
		_ = previousCycle;
		_ = currentCycle;
	}
}

/// <summary>Boundary that accepts both pure-CPU and bus-access trace batches.</summary>
internal sealed class PlanTierBatchBoundary :
	IM68kInstructionBoundary,
	IM68kPureCpuTraceBatchBoundary,
	IM68kBusAccessTraceBatchBoundary
{
	public int PureCpuBatchInstructions { get; private set; }

	public int BusAccessBatchInstructions { get; private set; }

	public bool BeforeInstruction()
		=> true;

	public void AfterInstruction(long previousCycle, long currentCycle)
	{
		_ = previousCycle;
		_ = currentCycle;
	}

	public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
	{
		_ = state;
		batchTargetCycle = targetCycle;
		return true;
	}

	public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
	{
		_ = previousCycle;
		_ = currentCycle;
		PureCpuBatchInstructions += instructionCount;
	}

	public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
	{
		_ = state;
		batchTargetCycle = targetCycle;
		return true;
	}

	public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount)
	{
		_ = previousCycle;
		_ = currentCycle;
		BusAccessBatchInstructions += instructionCount;
	}
}
