/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using Copper68k;

namespace Copper68k.Tests;

public sealed class M68000CachedDbraInterruptFenceTests
{
	private const uint ProgramAddress = 0x1000;
	private const uint StackAddress = 0x80000;
	private const uint InitialD3 = 0x1200_1000;
	private const ushort SavedStatus = 0x2015;

	[Theory]
	[InlineData((int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData((int)M68kOpcodePlanDispatch.PackedPlan)]
	public void CachedTakenDbraRetainsCommittedTailWithoutPrechargingExceptionSetup(int dispatchValue)
	{
		var scalarBus = new PlanTierFastMemoryTestBus();
		var cachedBus = new PlanTierFastMemoryTestBus();
		using var scalar = CreateCpu(scalarBus, enableOpcodePlan: false);
		using var cached = CreateCpu(cachedBus, enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);

		// Obtain the entry IR/IRC through execution, not an invented imported queue.
		scalar.ExecuteInstruction();
		cached.ExecuteInstruction();
		AssertArchitectureEqual(scalar.State, cached.State);
		cached.PlannedInterpreterCountersEnabled = true;
		cached.ResetPlannedInterpreterCounters();

		var scalarBoundary = new PlanTierBatchBoundary();
		var cachedBoundary = new PlanTierBatchBoundary();
		const int instructionLimit = 64;
		var targetCycle = scalar.State.Cycles + 37;
		var scalarCount = ((IM68kBatchCore)scalar).ExecuteInstructions(
			instructionLimit, targetCycle, scalarBoundary);
		var cachedCount = ((IM68kBatchCore)cached).ExecuteInstructions(
			instructionLimit, targetCycle, cachedBoundary);

		// A DBRA-only graph has no data accesses. The pure-CPU callback proves
		// cached graph admission; ordinary dispatch and fixed-plan batches cannot
		// satisfy this assertion. Counters also prevent loop fast-forward here.
		Assert.InRange(cachedCount, 2, instructionLimit - 1);
		Assert.Equal(scalarCount, cachedCount);
		Assert.Equal(0, scalarBoundary.PureCpuBatchInstructions);
		Assert.Equal(cachedCount, cachedBoundary.PureCpuBatchInstructions);
		Assert.Equal(0, cachedBoundary.BusAccessBatchInstructions);
		var counters = cached.CapturePlannedInterpreterCounters();
		Assert.Equal((long)cachedCount, counters.FastInstructions);
		Assert.Equal((long)cachedCount, counters.DbccInstructions);
		Assert.Equal(0, counters.ScalarFallbackInstructions);
		Assert.True(cached.State.Cycles >= targetCycle);
		Assert.Equal(InitialD3 - (uint)(cachedCount + 1), cached.State.D[3]);
		Assert.Equal(SavedStatus, cached.State.StatusRegister);
		Assert.Equal(ProgramAddress, cached.State.ProgramCounter);
		Assert.Equal(ProgramAddress, cached.State.LastInstructionProgramCounter);
		Assert.Equal((ushort)0x51CB, cached.State.LastOpcode);
		AssertArchitectureEqual(scalar.State, cached.State);

		var expected = ((IM68000PipelineStateTransfer)scalar).ExportM68000PipelineState();
		var actual = ((IM68000PipelineStateTransfer)cached).ExportM68000PipelineState();
		Assert.Equal(2, actual.PrefetchCount);
		Assert.Equal(ProgramAddress, actual.PrefetchAddress);
		Assert.Equal((ushort)0x51CB, actual.Word0);
		Assert.Equal((ushort)0xFFFE, actual.Word1);
		Assert.False(actual.HasPendingPrefetch);
		Assert.True(actual.ReadyCycle1 > 0);
		Assert.True(actual.ReadyCycle1 >= actual.ReadyCycle0);
		Assert.True(actual.RetireBusCycle >= actual.ReadyCycle1);
		Assert.True(actual.NextBusTransferCycle >= actual.ReadyCycle1);
		Assert.Equal(actual.ReadyCycle1, actual.LastBusReadyCycle);

		// Both words are already committed. The extension completion is the
		// fence; interrupt dispatch owns its separate internal setup interval.
		Assert.Equal(actual.ReadyCycle1, actual.ExceptionEntryNotBeforeCycle);
		Assert.Equal(expected.ReadyCycle0, actual.ReadyCycle0);
		Assert.Equal(expected.ReadyCycle1, actual.ReadyCycle1);
		Assert.Equal(expected.LastBusReadyCycle, actual.LastBusReadyCycle);
		Assert.Equal(expected.NextBusTransferCycle, actual.NextBusTransferCycle);
		Assert.Equal(expected.RetireBusCycle, actual.RetireBusCycle);
		Assert.Equal(expected.ExceptionEntryNotBeforeCycle, actual.ExceptionEntryNotBeforeCycle);
	}

	private static M68kInterpreter CreateCpu(
		PlanTierFastMemoryTestBus bus,
		bool enableOpcodePlan,
		M68kOpcodePlanDispatch dispatch = M68kOpcodePlanDispatch.KindTable)
	{
		bus.WriteWords(ProgramAddress, 0x51CB, 0xFFFE, 0x4E71, 0x4E71);
		var cpu = new M68kInterpreter(bus, new M68kCpuState(), enableCpuBusPhaseTrace: false,
			enableOpcodePlan: enableOpcodePlan, opcodePlanDispatch: dispatch);
		cpu.Reset(ProgramAddress, StackAddress);
		cpu.State.StatusRegister = SavedStatus;
		cpu.State.D[3] = InitialD3;
		return cpu;
	}

	private static void AssertArchitectureEqual(M68kCpuState expected, M68kCpuState actual)
	{
		Assert.Equal(expected.D, actual.D);
		Assert.Equal(expected.A, actual.A);
		Assert.Equal(expected.ProgramCounter, actual.ProgramCounter);
		Assert.Equal(expected.StatusRegister, actual.StatusRegister);
		Assert.Equal(expected.Cycles, actual.Cycles);
		Assert.Equal(expected.NativeCycles, actual.NativeCycles);
		Assert.Equal(expected.SupervisorStackPointer, actual.SupervisorStackPointer);
		Assert.Equal(expected.UserStackPointer, actual.UserStackPointer);
		Assert.Equal(expected.LastInstructionProgramCounter, actual.LastInstructionProgramCounter);
		Assert.Equal(expected.LastOpcode, actual.LastOpcode);
		Assert.Equal(expected.Stopped, actual.Stopped);
		Assert.Equal(expected.Halted, actual.Halted);
	}
}
