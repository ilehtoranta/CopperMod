using Copper68k;

namespace CopperMod.Amiga.Tests;

public sealed class M68kTimingFormulaTests
{
	public static IEnumerable<object[]> LegacyTimingKeys =>
		Enum.GetValues<M68kInstructionTimingKey>()
			.Where(key => key is not M68kInstructionTimingKey.MovemLongRegistersToPredecrement and
				not M68kInstructionTimingKey.MovemLongPostIncrementToRegisters)
			.Select(key => new object[] { key });

	[Theory]
	[MemberData(nameof(LegacyTimingKeys))]
	public void LegacyTimingKeysProduceDescriptorPlansForM68020AndM68030(object keyObject)
	{
		var key = (M68kInstructionTimingKey)keyObject;
		var descriptor = M68kTimingDescriptor.FromLegacyKey(key);

		var plan20 = M68kTimingFormula.GetPlan(descriptor, M68kAcceleratorModel.M68020);
		var plan30 = M68kTimingFormula.GetPlan(descriptor, M68kAcceleratorModel.M68030);

		Assert.Equal(key, descriptor.Key);
		Assert.Equal(key, plan20.Key);
		Assert.Equal(key, plan30.Key);
		Assert.Equal(plan20.Name, plan30.Name);
		Assert.Equal(plan20.NativeCycles, plan30.NativeCycles);
		Assert.False(plan20.UsesHeadTail);
		Assert.True(plan30.UsesHeadTail);
	}

	[Fact]
	public void LegacyMovemKeysRequireRegisterCountDescriptor()
	{
		Assert.Throws<UnsupportedM68kTimingException>(
			() => M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.MovemLongRegistersToPredecrement));
		Assert.Throws<UnsupportedM68kTimingException>(
			() => M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.MovemLongPostIncrementToRegisters));
	}

	[Fact]
	public void DescriptorCapturesSharedMoveOperands()
	{
		var immediateToDisplacement = M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.MoveLongImmediateToAddressDisplacement);
		Assert.Equal(M68kTimingOperation.Move, immediateToDisplacement.Operation);
		Assert.Equal(M68kOperandSize.Long, immediateToDisplacement.Size);
		Assert.Equal(M68kTimingOperand.Immediate, immediateToDisplacement.Source);
		Assert.Equal(M68kTimingOperand.AddressDisplacement, immediateToDisplacement.Destination);

		var indexedToData = M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.MoveByteBriefIndexedToData);
		Assert.Equal(M68kOperandSize.Byte, indexedToData.Size);
		Assert.Equal(M68kTimingOperand.BriefIndexed, indexedToData.Source);
		Assert.Equal(M68kTimingOperand.DataRegister, indexedToData.Destination);
	}

	[Fact]
	public void M68030FormulaDerivesHeadTailAndModelSpecificBarriers()
	{
		AssertPlan(
			M68kTimingFormula.GetPlan(M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.MoveLongDataToData), M68kAcceleratorModel.M68030),
			nativeCycles: 2,
			headCycles: 1,
			tailCycles: 1,
			M68kTimingBarrier.None);

		AssertPlan(
			M68kTimingFormula.GetPlan(M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.LinkLong), M68kAcceleratorModel.M68030),
			nativeCycles: 16,
			headCycles: 2,
			tailCycles: 2,
			M68kTimingBarrier.None);

		AssertPlan(
			M68kTimingFormula.GetPlan(M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.BranchByteTaken), M68kAcceleratorModel.M68030),
			nativeCycles: 6,
			headCycles: 0,
			tailCycles: 0,
			M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch);

		AssertPlan(
			M68kTimingFormula.GetPlan(M68kTimingDescriptor.FromLegacyKey(M68kInstructionTimingKey.Movec), M68kAcceleratorModel.M68030),
			nativeCycles: 12,
			headCycles: 0,
			tailCycles: 0,
			M68kTimingBarrier.CacheControl | M68kTimingBarrier.SynchronizeBus);
	}

	[Fact]
	public void MovemLongFormulaUsesRegisterCountAndModel()
	{
		var registerToMemory = M68kTimingDescriptor.MovemLong(
			M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
			"MOVEM.L <list>,-(An)",
			registerCount: 3,
			registerToMemory: true);
		var memoryToRegister = M68kTimingDescriptor.MovemLong(
			M68kInstructionTimingKey.MovemLongPostIncrementToRegisters,
			"MOVEM.L (An)+,<list>",
			registerCount: 3,
			registerToMemory: false);

		AssertPlan(
			M68kTimingFormula.GetPlan(registerToMemory, M68kAcceleratorModel.M68020),
			nativeCycles: 17,
			usesHeadTail: false);
		AssertPlan(
			M68kTimingFormula.GetPlan(registerToMemory, M68kAcceleratorModel.M68030),
			nativeCycles: 14,
			headCycles: 2,
			tailCycles: 0,
			M68kTimingBarrier.None);
		AssertPlan(
			M68kTimingFormula.GetPlan(memoryToRegister, M68kAcceleratorModel.M68030),
			nativeCycles: 24,
			headCycles: 2,
			tailCycles: 0,
			M68kTimingBarrier.None);
		AssertPlan(
			M68kTimingFormula.GetPlan(registerToMemory, M68kAcceleratorModel.M68040),
			nativeCycles: 17,
			usesHeadTail: false);
	}

	[Fact]
	public void FixedCycleProfileOverridesDescriptorFormula()
	{
		var timing = new M68kTimingEngine(M68020CpuProfile.Ocs68040JitMaxSpeed, new M68kCpuState());
		var descriptor = M68kTimingDescriptor.MovemLong(
			M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
			"MOVEM.L <list>,-(An)",
			registerCount: 8,
			registerToMemory: true);

		var plan = timing.GetPlan(descriptor);

		Assert.Equal(1, plan.NativeCycles);
		Assert.False(plan.UsesHeadTail);
		Assert.Equal("fixed JIT fallback", plan.Name);
	}

	private static void AssertPlan(
		M68kInstructionPlan plan,
		int nativeCycles,
		int headCycles = 0,
		int tailCycles = 0,
		M68kTimingBarrier barriers = M68kTimingBarrier.None,
		bool usesHeadTail = true)
	{
		Assert.Equal(nativeCycles, plan.NativeCycles);
		Assert.Equal(usesHeadTail, plan.UsesHeadTail);
		Assert.Equal(headCycles, plan.HeadCycles);
		Assert.Equal(tailCycles, plan.TailCycles);
		Assert.Equal(barriers, plan.Barriers);
	}
}
