using Copper68k;

namespace CopperMod.Amiga.Tests;

public sealed class M68kTimingFormulaTests
{
	[Fact]
	public void M68020DescriptorsRoundTripCompatibilityPlans()
	{
		foreach (var key in Enum.GetValues<M68kInstructionTimingKey>())
		{
			if (IsDynamicTimingKey(key))
			{
				continue;
			}

			var descriptor = M68020TimingModel.GetDescriptor(key);
			var formulaPlan = M68kTimingFormula.CreatePlan(descriptor);
			var expectedPlan = M68kTimingDescriptor.UsesSpecialControlFormula(key)
				? CreateExpectedSpecialControlPlan(key, useHeadTail: false)
				: formulaPlan;

			Assert.Equal(expectedPlan, formulaPlan);
			AssertDescriptorMatchesPlanShape(descriptor, expectedPlan);
			Assert.False(descriptor.Plan.UsesHeadTail);
			Assert.Equal(
				ExpectedFormulaKind(key),
				descriptor.FormulaKind);
		}
	}

	[Fact]
	public void M68030DescriptorsRoundTripCompatibilityPlans()
	{
		foreach (var key in Enum.GetValues<M68kInstructionTimingKey>())
		{
			if (IsDynamicTimingKey(key))
			{
				continue;
			}

			var descriptor = M68030TimingModel.GetDescriptor(key);
			var formulaPlan = M68kTimingFormula.CreatePlan(descriptor);
			var expectedPlan = M68kTimingDescriptor.UsesSpecialControlFormula(key)
				? CreateExpectedSpecialControlPlan(key, useHeadTail: true)
				: formulaPlan;

			Assert.Equal(expectedPlan, formulaPlan);
			AssertDescriptorMatchesPlanShape(descriptor, expectedPlan);
			Assert.Equal(
				ExpectedFormulaKind(key),
				descriptor.FormulaKind);
		}
	}

	[Fact]
	public void OperandShapeFamiliesComputeCyclesWithoutTheCompatibilitySwitch()
	{
		foreach (var key in Enum.GetValues<M68kInstructionTimingKey>())
		{
			if (!IsOperandShapeFormulaKey(key))
			{
				continue;
			}

			Assert.Throws<UnsupportedM68kTimingException>(() => M68020TimingModel.GetCompatibilityPlan(key));
			Assert.Throws<UnsupportedM68kTimingException>(() => M68030TimingModel.GetCompatibilityPlan(key));

			var m68020Descriptor = M68020TimingModel.GetDescriptor(key);
			var m68030Descriptor = M68030TimingModel.GetDescriptor(key);
			var m68020Plan = M68kTimingFormula.CreatePlan(m68020Descriptor);
			var m68030Plan = M68kTimingFormula.CreatePlan(m68030Descriptor);

			Assert.Equal(M68kTimingFormulaKind.OperandShape, m68020Descriptor.FormulaKind);
			Assert.Equal(M68kTimingFormulaKind.OperandShape, m68030Descriptor.FormulaKind);
			Assert.Equal(0, m68020Descriptor.Plan.NativeCycles);
			Assert.Equal(0, m68030Descriptor.Plan.NativeCycles);
			Assert.False(m68020Descriptor.Plan.UsesHeadTail);
			Assert.True(m68030Descriptor.Plan.UsesHeadTail);
			Assert.Equal(ExpectedOperandShapeHeadTail(key), m68030Descriptor.Plan.HeadCycles);
			Assert.Equal(ExpectedOperandShapeHeadTail(key), m68030Descriptor.Plan.TailCycles);
			Assert.NotEqual(0, m68020Plan.NativeCycles);
			Assert.Equal(m68020Plan.NativeCycles, m68030Plan.NativeCycles);
			Assert.Equal(m68020Plan.Name, m68030Plan.Name);
			Assert.Equal(m68020Plan.Barriers, m68030Plan.Barriers);
		}
	}

	[Fact]
	public void SpecialControlFamiliesComputeCyclesWithoutTheCompatibilitySwitch()
	{
		foreach (var key in Enum.GetValues<M68kInstructionTimingKey>())
		{
			if (!M68kTimingDescriptor.UsesSpecialControlFormula(key))
			{
				continue;
			}

			Assert.Throws<UnsupportedM68kTimingException>(() => M68020TimingModel.GetCompatibilityPlan(key));
			Assert.Throws<UnsupportedM68kTimingException>(() => M68030TimingModel.GetCompatibilityPlan(key));

			var m68020Descriptor = M68020TimingModel.GetDescriptor(key);
			var m68030Descriptor = M68030TimingModel.GetDescriptor(key);

			Assert.Equal(M68kTimingFormulaKind.SpecialControl, m68020Descriptor.FormulaKind);
			Assert.Equal(M68kTimingFormulaKind.SpecialControl, m68030Descriptor.FormulaKind);
			Assert.Equal(0, m68020Descriptor.Plan.NativeCycles);
			Assert.Equal(0, m68030Descriptor.Plan.NativeCycles);
			Assert.Equal(CreateExpectedSpecialControlPlan(key, useHeadTail: false), M68kTimingFormula.CreatePlan(m68020Descriptor));
			Assert.Equal(CreateExpectedSpecialControlPlan(key, useHeadTail: true), M68kTimingFormula.CreatePlan(m68030Descriptor));
		}
	}

	[Theory]
	[InlineData((int)M68kAcceleratorModel.M68020, true, 3, 17, false, 0, 0)]
	[InlineData((int)M68kAcceleratorModel.M68020, false, 3, 24, false, 0, 0)]
	[InlineData((int)M68kAcceleratorModel.M68030, true, 3, 14, true, 2, 0)]
	[InlineData((int)M68kAcceleratorModel.M68030, false, 3, 24, true, 2, 0)]
	[InlineData((int)M68kAcceleratorModel.M68040, true, 3, 17, false, 0, 0)]
	public void MovemLongFormulaPreservesRegisterCountTiming(
		int modelValue,
		bool registerToMemory,
		int registerCount,
		int expectedCycles,
		bool expectedHeadTail,
		int expectedHeadCycles,
		int expectedTailCycles)
	{
		var plan = M68kTimingFormula.CreateMovemLongPlan(
			M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
			"MOVEM.L registers,-(An)",
			registerCount,
			registerToMemory,
			(M68kAcceleratorModel)modelValue,
			fixedCycles: null);

		Assert.Equal(expectedCycles, plan.NativeCycles);
		Assert.Equal(expectedHeadTail, plan.UsesHeadTail);
		Assert.Equal(expectedHeadCycles, plan.HeadCycles);
		Assert.Equal(expectedTailCycles, plan.TailCycles);
	}

	[Fact]
	public void MovemLongFormulaUsesFixedCyclesForFixedTimingProfiles()
	{
		var plan = M68kTimingFormula.CreateMovemLongPlan(
			M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
			"MOVEM.L registers,-(An)",
			registerCount: 16,
			registerToMemory: true,
			M68kAcceleratorModel.M68040,
			fixedCycles: 1);

		Assert.Equal(1, plan.NativeCycles);
		Assert.False(plan.UsesHeadTail);
		Assert.Equal(0, plan.HeadCycles);
		Assert.Equal(0, plan.TailCycles);
	}

	[Fact]
	public void MovemLongFormulaKeepsM68030HeadTailShapeWithFixedCycles()
	{
		var plan = M68kTimingFormula.CreateMovemLongPlan(
			M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
			"MOVEM.L registers,-(An)",
			registerCount: 16,
			registerToMemory: true,
			M68kAcceleratorModel.M68030,
			fixedCycles: 1);

		Assert.Equal(1, plan.NativeCycles);
		Assert.True(plan.UsesHeadTail);
		Assert.Equal(2, plan.HeadCycles);
		Assert.Equal(0, plan.TailCycles);
	}

	private static bool IsDynamicTimingKey(M68kInstructionTimingKey key)
		=> key is M68kInstructionTimingKey.MovemLongRegistersToPredecrement or
			M68kInstructionTimingKey.MovemLongPostIncrementToRegisters;

	private static bool IsOperandShapeFormulaKey(M68kInstructionTimingKey key)
	{
		var keyName = key.ToString();
		return key is M68kInstructionTimingKey.Moveq or M68kInstructionTimingKey.TstWordData ||
			keyName.StartsWith("MoveLong", StringComparison.Ordinal) ||
			keyName.StartsWith("MoveWord", StringComparison.Ordinal) ||
			keyName.StartsWith("MoveByte", StringComparison.Ordinal) ||
			keyName.StartsWith("Lea", StringComparison.Ordinal) ||
			keyName.StartsWith("Clr", StringComparison.Ordinal) ||
			keyName.StartsWith("ImmediateWordTo", StringComparison.Ordinal) ||
			keyName.StartsWith("ImmediateLogical", StringComparison.Ordinal) ||
			keyName.StartsWith("Add", StringComparison.Ordinal) ||
			keyName.StartsWith("Sub", StringComparison.Ordinal) ||
			keyName.StartsWith("Neg", StringComparison.Ordinal) ||
			keyName.StartsWith("Not", StringComparison.Ordinal) ||
			keyName.StartsWith("Ori", StringComparison.Ordinal) ||
			keyName.StartsWith("And", StringComparison.Ordinal) ||
			keyName.StartsWith("Eori", StringComparison.Ordinal) ||
			keyName.StartsWith("Cmpi", StringComparison.Ordinal) ||
			keyName.StartsWith("Cmpa", StringComparison.Ordinal) ||
			keyName.StartsWith("Cmp", StringComparison.Ordinal) ||
			keyName.StartsWith("Branch", StringComparison.Ordinal) ||
			keyName.StartsWith("Bsr", StringComparison.Ordinal) ||
			keyName.StartsWith("Dbcc", StringComparison.Ordinal) ||
			keyName.StartsWith("Scc", StringComparison.Ordinal) ||
			keyName.StartsWith("Btst", StringComparison.Ordinal) ||
			keyName.StartsWith("Bchg", StringComparison.Ordinal) ||
			keyName.StartsWith("Bclr", StringComparison.Ordinal) ||
			keyName.StartsWith("Bset", StringComparison.Ordinal) ||
			keyName.StartsWith("Asr", StringComparison.Ordinal) ||
			keyName.StartsWith("Asl", StringComparison.Ordinal) ||
			keyName.StartsWith("Lsr", StringComparison.Ordinal) ||
			keyName.StartsWith("Lsl", StringComparison.Ordinal) ||
			keyName.StartsWith("Ror", StringComparison.Ordinal) ||
			keyName.StartsWith("Rol", StringComparison.Ordinal) ||
			keyName.StartsWith("Roxr", StringComparison.Ordinal) ||
			keyName.StartsWith("Roxl", StringComparison.Ordinal) ||
			keyName.StartsWith("Abcd", StringComparison.Ordinal) ||
			keyName.StartsWith("Sbcd", StringComparison.Ordinal) ||
			keyName.StartsWith("Nbcd", StringComparison.Ordinal) ||
			keyName.StartsWith("Mulu", StringComparison.Ordinal) ||
			keyName.StartsWith("Muls", StringComparison.Ordinal) ||
			keyName.StartsWith("Divu", StringComparison.Ordinal) ||
			keyName.StartsWith("Divs", StringComparison.Ordinal);
	}

	private static M68kTimingFormulaKind ExpectedFormulaKind(M68kInstructionTimingKey key)
	{
		if (M68kTimingDescriptor.UsesSpecialControlFormula(key))
		{
			return M68kTimingFormulaKind.SpecialControl;
		}

		return IsOperandShapeFormulaKey(key)
			? M68kTimingFormulaKind.OperandShape
			: throw new ArgumentOutOfRangeException(nameof(key), key, null);
	}

	private static int ExpectedOperandShapeHeadTail(M68kInstructionTimingKey key)
		=> key is M68kInstructionTimingKey.ImmediateWordToStatusRegister or
			M68kInstructionTimingKey.BranchByteTaken or
			M68kInstructionTimingKey.BsrByte or
			M68kInstructionTimingKey.BranchWordTaken or
			M68kInstructionTimingKey.BsrWord or
			M68kInstructionTimingKey.DbccBranchTaken or
			M68kInstructionTimingKey.BranchLongTaken or
			M68kInstructionTimingKey.BsrLong
			? 0
			: 1;

	private static M68kInstructionPlan CreateExpectedSpecialControlPlan(M68kInstructionTimingKey key, bool useHeadTail)
	{
		var (label, cycles, barriers) = key switch
		{
			M68kInstructionTimingKey.Idle => ("IDLE", 2, M68kTimingBarrier.SynchronizeBus),
			M68kInstructionTimingKey.Nop => ("NOP", 4, M68kTimingBarrier.SynchronizeBus),
			M68kInstructionTimingKey.LineAException => ("LINEA", 34, ExceptionBarrier()),
			M68kInstructionTimingKey.LineFException => ("LINEF", 34, ExceptionBarrier()),
			M68kInstructionTimingKey.IllegalInstruction => ("ILLEGAL", 20, ExceptionBarrier()),
			M68kInstructionTimingKey.PrivilegeViolation => ("PRIVILEGE", 20, ExceptionBarrier()),
			M68kInstructionTimingKey.FormatError => ("FORMAT", 20, ExceptionBarrier()),
			M68kInstructionTimingKey.InterruptAcknowledge => ("INTERRUPT", 44, ExceptionBarrier()),
			M68kInstructionTimingKey.Movec => ("MOVEC", 12, useHeadTail
				? M68kTimingBarrier.CacheControl | M68kTimingBarrier.SynchronizeBus
				: M68kTimingBarrier.CacheControl),
			M68kInstructionTimingKey.Rte => ("RTE", 20, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus),
			M68kInstructionTimingKey.Rtd => ("RTD", 16, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
			M68kInstructionTimingKey.Rts => ("RTS", 7, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
			M68kInstructionTimingKey.LinkLong => ("LINK.L", 16, M68kTimingBarrier.None),
			M68kInstructionTimingKey.ExtbLong => ("EXTB.L", 4, M68kTimingBarrier.None),
			M68kInstructionTimingKey.ExtWordData => ("EXT.W Dn", 2, M68kTimingBarrier.None),
			M68kInstructionTimingKey.SwapData => ("SWAP Dn", 4, M68kTimingBarrier.None),
			M68kInstructionTimingKey.JsrAbsoluteLong => ("JSR (xxx).L", 7, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
			M68kInstructionTimingKey.JmpAddressIndirect => ("JMP (An)", 4, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
			M68kInstructionTimingKey.JmpAbsoluteLong => ("JMP (xxx).L", 6, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
			_ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
		};

		if (!useHeadTail)
		{
			return M68kInstructionPlan.CreateFlat(key, label, cycles, barriers);
		}

		var (headCycles, tailCycles) = key switch
		{
			M68kInstructionTimingKey.LinkLong => (2, 2),
			M68kInstructionTimingKey.ExtbLong => (2, 2),
			M68kInstructionTimingKey.ExtWordData => (1, 1),
			M68kInstructionTimingKey.SwapData => (1, 1),
			_ => (0, 0)
		};
		return M68kInstructionPlan.CreateHeadTail(key, label, cycles, headCycles, tailCycles, barriers);
	}

	private static M68kTimingBarrier ExceptionBarrier()
		=> M68kTimingBarrier.Exception | M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus;

	private static void AssertDescriptorMatchesPlanShape(M68kTimingDescriptor descriptor, M68kInstructionPlan plan)
	{
		Assert.Equal(plan.Key, descriptor.LegacyKey);
		Assert.Equal(plan.Name, descriptor.LegacyLabel);
		Assert.Equal(plan.Barriers, descriptor.Barriers);
		Assert.Equal(plan.HeadCycles, descriptor.Plan.HeadCycles);
		Assert.Equal(plan.TailCycles, descriptor.Plan.TailCycles);
		Assert.Equal(plan.UsesHeadTail, descriptor.Plan.UsesHeadTail);
		Assert.NotEqual(M68kTimingFormulaKind.Compatibility, descriptor.FormulaKind);
	}
}
