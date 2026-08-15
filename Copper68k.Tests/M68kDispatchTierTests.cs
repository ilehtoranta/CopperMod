/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;

namespace Copper68k.Tests;

/// <summary>
/// The dispatch tier an opcode can reach in the MC68000 interpreter, ordered
/// from slowest to fastest.
/// </summary>
public enum M68kDispatchTier
{
	/// <summary>Full scalar decode through <c>DecodeByOpcodeLine</c>.</summary>
	Scalar = 0,

	/// <summary>Planned scalar dispatch only; still pays the per-instruction retirement cost.</summary>
	PlannedOnly,

	/// <summary>Admitted to the cached fixed-plan-run graph as a conditional-branch edge.</summary>
	ShortConditionalBranch,

	/// <summary>Admitted to the cached fixed-plan-run graph only when the bus exposes fast memory.</summary>
	FastMemoryRun,

	/// <summary>Admitted to the fixed-plan batch and the cached fixed-plan-run graph.</summary>
	FixedPlanBatch
}

/// <summary>
/// Pins the dispatch tier every MC68000 opcode reaches so widening the
/// currently admitted kind set cannot happen silently.
/// </summary>
public sealed class M68kDispatchTierTests
{
	private static readonly Type CoreType = typeof(M68kInterpreter).BaseType!;

	private static readonly Func<ushort, M68kOpcodePlanKind, bool> IsFixedPlanBatchOpcode =
		CreateOpcodeKindPredicate("IsFixedPlanBatchOpcode");

	private static readonly Func<ushort, M68kOpcodePlanKind, bool> IsShortConditionalBranch =
		CreateOpcodeKindPredicate("IsShortConditionalBranch");

	private static readonly Func<ushort, M68kOpcodePlanKind, bool> IsFastMemoryRunKind =
		CreateOpcodeKindPredicate("IsFastMemoryRunKind");

	private static readonly Func<M68kOpcodePlanKind, bool> IsFixedPlanBatchKind =
		CoreType
			.GetMethod("IsFixedPlanBatchKind", BindingFlags.NonPublic | BindingFlags.Static)!
			.CreateDelegate<Func<M68kOpcodePlanKind, bool>>();

	private static readonly Func<ushort, bool, bool> IsFixedPlanRunEntryOpcode =
		CoreType
			.GetMethod("IsFixedPlanRunEntryOpcode", BindingFlags.NonPublic | BindingFlags.Static)!
			.CreateDelegate<Func<ushort, bool, bool>>();

	/// <summary>
	/// The kinds the cached fixed-plan-run graph and the fixed-plan batch accept
	/// today. Widening coverage must update this list deliberately.
	/// </summary>
	private static readonly M68kOpcodePlanKind[] ExpectedFixedPlanBatchKinds =
	[
		M68kOpcodePlanKind.Nop,
		M68kOpcodePlanKind.Moveq,
		M68kOpcodePlanKind.ShortUnconditionalBranch,
		M68kOpcodePlanKind.QuickRegister,
		M68kOpcodePlanKind.QuickLongDataRegister,
		M68kOpcodePlanKind.DataRegisterUnary,
		M68kOpcodePlanKind.RegisterShift,
		M68kOpcodePlanKind.DataRegisterLongOrToRegister,
		M68kOpcodePlanKind.DataRegisterLongEorToDestination,
		M68kOpcodePlanKind.DataRegisterLongAndToRegister,
		M68kOpcodePlanKind.DataRegisterLongAddToRegister
	];

	[Fact]
	public void FixedPlanBatchKindSetMatchesTheDocumentedCoverage()
	{
		var actual = Enum.GetValues<M68kOpcodePlanKind>()
			.Where(kind => IsFixedPlanBatchKind(kind))
			.OrderBy(kind => (int)kind)
			.ToArray();
		var expected = ExpectedFixedPlanBatchKinds
			.OrderBy(kind => (int)kind)
			.ToArray();

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void PlanKindOrdinalsStayInsideThePackedFiveBitField()
	{
		// M68kPackedOpcodePlan stores Kind in five bits (FiveBitMask). Running
		// past 31 silently truncates every plan in the table.
		foreach (var kind in Enum.GetValues<M68kOpcodePlanKind>())
		{
			Assert.InRange((int)kind, 0, 31);
		}
	}

	[Theory]
	// CLR/NEG/NOT/TST with a data-register operand classify as DataRegisterUnary.
	[InlineData((ushort)0x4200, "DataRegisterUnary")] // CLR.B D0
	[InlineData((ushort)0x4247, "DataRegisterUnary")] // CLR.W D7
	[InlineData((ushort)0x4280, "DataRegisterUnary")] // CLR.L D0
	[InlineData((ushort)0x4403, "DataRegisterUnary")] // NEG.B D3
	[InlineData((ushort)0x4680, "DataRegisterUnary")] // NOT.L D0
	[InlineData((ushort)0x4A41, "DataRegisterUnary")] // TST.W D1
	[InlineData((ushort)0x4A80, "DataRegisterUnary")] // TST.L D0
	// Excluded: every non-register form, illegal size, and TAS keeps the scalar
	// decoder. NEGX is included in the group now that the fixed-plan-run path
	// models its extend-in, zero-accumulate condition codes.
	[InlineData((ushort)0x4000, "DataRegisterUnary")] // NEGX.B D0
	[InlineData((ushort)0x4080, "DataRegisterUnary")] // NEGX.L D0
	[InlineData((ushort)0x4010, "Unsupported")] // NEGX.B (A0)
	[InlineData((ushort)0x4210, "Unsupported")] // CLR.B (A0)
	[InlineData((ushort)0x4218, "Unsupported")] // CLR.B (A0)+
	[InlineData((ushort)0x42C0, "Unsupported")] // illegal size
	[InlineData((ushort)0x4AC0, "Unsupported")] // TAS D0
	[InlineData((ushort)0x44C0, "Unsupported")] // MOVE D0,CCR
	public void OpcodeClassifiesAsTheExpectedPlanKind(ushort opcode, string expected)
	{
		Assert.Equal(expected, M68kOpcodePlanTable.Kinds[opcode].ToString());
		Assert.Equal(expected, M68kOpcodePlanTable.PackedPlans[opcode].Kind.ToString());
	}

	[Theory]
	[InlineData((ushort)0x4200, M68kOperandSize.Byte, (byte)0, (byte)0x42)]
	[InlineData((ushort)0x4247, M68kOperandSize.Word, (byte)7, (byte)0x42)]
	[InlineData((ushort)0x4285, M68kOperandSize.Long, (byte)5, (byte)0x42)]
	[InlineData((ushort)0x4443, M68kOperandSize.Word, (byte)3, (byte)0x44)]
	[InlineData((ushort)0x4682, M68kOperandSize.Long, (byte)2, (byte)0x46)]
	[InlineData((ushort)0x4A01, M68kOperandSize.Byte, (byte)1, (byte)0x4A)]
	public void DataRegisterUnaryPackedPlanCarriesSizeRegisterAndOperation(
		ushort opcode,
		M68kOperandSize expectedSize,
		byte expectedRegister,
		byte expectedVariant)
	{
		var plan = M68kOpcodePlanTable.PackedPlans[opcode];
		Assert.Equal(M68kOpcodePlanKind.DataRegisterUnary, plan.Kind);
		Assert.Equal(expectedSize, plan.Size);
		Assert.Equal(expectedRegister, plan.Register);
		Assert.Equal(expectedVariant, plan.Variant);
	}

	[Fact]
	public void DataRegisterUnaryStaysOutOfTheJitMicrosequenceMapping()
	{
		// M68000MicrosequenceClass is read only by the MC68040 JIT, where
		// SequentialFinalPrefetch means a four-cycle sequential retirement.
		// CLR/NEG/NOT.L on a data register retire in six cycles, so this kind
		// must not claim that class until the JIT models the six-cycle form.
		foreach (var opcode in new ushort[] { 0x4200, 0x4247, 0x4285, 0x4443, 0x4682, 0x4A01, 0x4A80 })
		{
			Assert.Equal(M68kOpcodePlanKind.DataRegisterUnary, M68kOpcodePlanTable.Kinds[opcode]);
			Assert.Equal(
				M68000MicrosequenceClass.Unsupported,
				M68kOpcodePlanTable.PackedPlans[opcode].Microsequence);
		}
	}

	[Fact]
	public void DataRegisterUnaryCoversExactlyTheRegisterDirectUnaryForms()
	{
		var expected = new List<ushort>();
		foreach (var group in new[] { 0x4000, 0x4200, 0x4400, 0x4600, 0x4A00 })
		{
			for (var sizeCode = 0; sizeCode <= 2; sizeCode++)
			{
				for (var register = 0; register < 8; register++)
				{
					expected.Add((ushort)(group | (sizeCode << 6) | register));
				}
			}
		}

		var actual = new List<ushort>();
		for (var opcode = 0; opcode <= 0xFFFF; opcode++)
		{
			if (M68kOpcodePlanTable.Kinds[opcode] == M68kOpcodePlanKind.DataRegisterUnary)
			{
				actual.Add((ushort)opcode);
			}
		}

		Assert.Equal(expected.Order().ToArray(), actual.ToArray());
	}

	[Theory]
	// Line-4 register unary forms are admitted to the fixed-plan batch and the
	// cached fixed-plan-run graph.
	[InlineData((ushort)0x4200, M68kDispatchTier.FixedPlanBatch)] // CLR.B D0
	[InlineData((ushort)0x4240, M68kDispatchTier.FixedPlanBatch)] // CLR.W D0
	[InlineData((ushort)0x4287, M68kDispatchTier.FixedPlanBatch)] // CLR.L D7
	[InlineData((ushort)0x4680, M68kDispatchTier.FixedPlanBatch)] // NOT.L D0
	[InlineData((ushort)0x4A80, M68kDispatchTier.FixedPlanBatch)] // TST.L D0
	[InlineData((ushort)0x4400, M68kDispatchTier.FixedPlanBatch)] // NEG.B D0
	[InlineData((ushort)0x4000, M68kDispatchTier.FixedPlanBatch)] // NEGX.B D0
	[InlineData((ushort)0x4210, M68kDispatchTier.Scalar)] // CLR.B (A0)
	[InlineData((ushort)0x42C0, M68kDispatchTier.Scalar)] // illegal size
	[InlineData((ushort)0x4AC0, M68kDispatchTier.Scalar)] // TAS D0
	// Already-covered kinds.
	[InlineData((ushort)0x4E71, M68kDispatchTier.FixedPlanBatch)] // NOP
	[InlineData((ushort)0x7001, M68kDispatchTier.FixedPlanBatch)] // MOVEQ #1,D0
	[InlineData((ushort)0x5280, M68kDispatchTier.FixedPlanBatch)] // ADDQ.L #1,D0
	[InlineData((ushort)0x60FE, M68kDispatchTier.FixedPlanBatch)] // BRA.S *
	[InlineData((ushort)0x66FA, M68kDispatchTier.ShortConditionalBranch)] // BNE.S
	[InlineData((ushort)0x2018, M68kDispatchTier.FastMemoryRun)] // MOVE.L (A0)+,D0
	// Still scalar-only.
	[InlineData((ushort)0x0500, M68kDispatchTier.Scalar)] // BTST D2,D0
	[InlineData((ushort)0x4840, M68kDispatchTier.Scalar)] // SWAP D0
	[InlineData((ushort)0x48C0, M68kDispatchTier.Scalar)] // EXT.L D0
	[InlineData((ushort)0x2040, M68kDispatchTier.PlannedOnly)] // MOVEA.L D0,A0
	[InlineData((ushort)0xB1C0, M68kDispatchTier.Scalar)] // CMPA.L D0,A0
	[InlineData((ushort)0xE380, M68kDispatchTier.FixedPlanBatch)] // ASL.L #1,D0
	[InlineData((ushort)0xE1A8, M68kDispatchTier.PlannedOnly)] // ASL.L D0,D0 (register count)
	[InlineData((ushort)0xE0D0, M68kDispatchTier.Scalar)] // ASR.W (A0) memory form
	[InlineData((ushort)0xD081, M68kDispatchTier.FixedPlanBatch)] // ADD.L D1,D0
	public void OpcodeReachesTheExpectedDispatchTier(ushort opcode, M68kDispatchTier expected)
	{
		Assert.Equal(expected, ClassifyTier(opcode));
	}

	/// <summary>
	/// <c>AdvanceCachedFixedPlanRunLoop</c> measures one iteration of a
	/// single-path loop and multiplies its cycle cost by the repeat count, so
	/// every opcode admitted to the fixed-plan batch must retire in a number of
	/// cycles that depends only on the opcode, never on register or flag state.
	/// <c>IsSteadyFixedPlanRunLoop</c> only checks that the observed iteration was
	/// self-consistent; it cannot prove future iterations cost the same. This is
	/// the gate for that otherwise unwritten invariant: a register-sourced shift
	/// count, whose cost is 6/8 + 2 per shifted bit, is exactly what it catches.
	/// </summary>
	[Fact]
	public void EveryAdmittedOpcodeRetiresInOpcodeConstantCycles()
	{
		var seeds = new Action<M68kCpuState>[]
		{
			state => { },
			state =>
			{
				for (var i = 0; i < 8; i++)
				{
					state.D[i] = 0xFFFF_FFFF;
					state.A[i] = 0x0002_0000;
				}
			},
			state =>
			{
				for (var i = 0; i < 8; i++)
				{
					state.D[i] = 0x0000_003F;
					state.A[i] = 0x0002_0040;
				}
			},
			state =>
			{
				for (var i = 0; i < 8; i++)
				{
					state.D[i] = 0x8000_0001;
					state.A[i] = 0x0002_0080;
				}
				state.StatusRegister |= M68kCpuState.Extend;
			}
		};

		var bus = new ZeroWaitCodeBus();
		var offenders = new List<string>();
		for (var opcode = 0; opcode <= 0xFFFF; opcode++)
		{
			var word = (ushort)opcode;
			if (ClassifyTier(word) != M68kDispatchTier.FixedPlanBatch)
			{
				continue;
			}

			bus.WriteWord(0x1000, word);
			bus.WriteWord(0x1002, 0x4E71);
			var baseline = -1;
			for (var seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
			{
				using var cpu = new M68kInterpreter(
					bus,
					new M68kCpuState(),
					enableCpuBusPhaseTrace: false);
				cpu.Reset(0x1000, 0x0004_0000);
				seeds[seedIndex](cpu.State);

				var cycles = cpu.ExecuteInstruction();
				if (seedIndex == 0)
				{
					baseline = cycles;
				}
				else if (cycles != baseline)
				{
					offenders.Add(
						$"0x{word:X4} ({M68kInstructionClassifier.GetMnemonic(word)}, " +
						$"{M68kOpcodePlanTable.Kinds[word]}): seed 0 retired {baseline} cycles, " +
						$"seed {seedIndex} retired {cycles}");
					break;
				}
			}
		}

		Assert.True(
			offenders.Count == 0,
			"Opcodes admitted to the fixed-plan batch must retire in opcode-constant " +
			"cycles, otherwise the cached-run loop fast-forward mis-times them:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders.Take(20)));
	}


	internal static M68kDispatchTier ClassifyTier(ushort opcode)
	{
		var kind = M68kOpcodePlanTable.Kinds[opcode];
		if (IsFixedPlanBatchOpcode(opcode, kind))
		{
			return M68kDispatchTier.FixedPlanBatch;
		}

		if (IsShortConditionalBranch(opcode, kind))
		{
			return M68kDispatchTier.ShortConditionalBranch;
		}

		if (IsFastMemoryRunKind(opcode, kind))
		{
			return M68kDispatchTier.FastMemoryRun;
		}

		return kind == M68kOpcodePlanKind.Unsupported
			? M68kDispatchTier.Scalar
			: M68kDispatchTier.PlannedOnly;
	}

	private static Func<ushort, M68kOpcodePlanKind, bool> CreateOpcodeKindPredicate(string name)
		=> CoreType
			.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
			.CreateDelegate<Func<ushort, M68kOpcodePlanKind, bool>>();

	private static bool RunEntry(ushort opcode, bool allowFastMemory)
		=> IsFixedPlanRunEntryOpcode(opcode, allowFastMemory);

	[Fact]
	public void RunEntryAgreesWithTheTierClassification()
	{
		for (var opcode = 0; opcode <= 0xFFFF; opcode++)
		{
			var word = (ushort)opcode;
			var tier = ClassifyTier(word);
			var expectedWithoutFastMemory = tier is
				M68kDispatchTier.FixedPlanBatch or
				M68kDispatchTier.ShortConditionalBranch;
			var expectedWithFastMemory = expectedWithoutFastMemory ||
				tier == M68kDispatchTier.FastMemoryRun;

			Assert.Equal(expectedWithoutFastMemory, RunEntry(word, allowFastMemory: false));
			Assert.Equal(expectedWithFastMemory, RunEntry(word, allowFastMemory: true));
		}
	}
}
