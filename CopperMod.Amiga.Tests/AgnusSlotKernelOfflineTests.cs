using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.CustomChips.Paula;
using CopperMod.Amiga.Runtime;
using CopperMod.Amiga.Storage.Floppy;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusSlotKernelOfflineTests
{
    private const int ChipRamSize = 64 * 1024;
    private const int TestSlotCount = 96;

    [Fact]
    public void OfflineKernelReplaysRefreshFixedBitplaneSpriteAndCpuOracle()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, TestSlotCount);
        var memory = CreateMemory();
        WriteWord(memory, 0x1000, 0x1357);
        WriteWord(memory, 0x3000, 0x2468);
        WriteWord(memory, 0x4000, 0xA55A);
        fixture.SetInitialFixed(
            slotCycle: 0x18 * AgnusChipSlotScheduler.SlotCycles,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Sprite, channel: 0, phase: 0),
            address: 0x3000);
        fixture.SetInitialFixed(
            slotCycle: 0x38 * AgnusChipSlotScheduler.SlotCycles,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 0),
            address: 0x1000);
        fixture.AddCpuRequest(new CpuWordRequest(
            address: 0x4000,
            requestedCycle: 0,
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));
        fixture.AddCpuRequest(new CpuWordRequest(
            address: 0x4000,
            requestedCycle: 0x18 * AgnusChipSlotScheduler.SlotCycles,
            AmigaBusAccessKind.CpuInstructionFetch,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        var replayMemory = (byte[])memory.Clone();
        kernel.Load(trace, replayMemory);

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal(trace.SlotCount, kernel.CommittedSlotCount);
        Assert.Equal(trace.SlotCount, kernel.SlotDecisions);
        Assert.Equal(2, kernel.CpuResultCount);
        Assert.Equal((ushort)0xA55A, kernel.GetCpuResult(0).Value);
        Assert.Equal((ushort)0xA55A, kernel.GetCpuResult(1).Value);
        Assert.Contains(
            Enumerable.Range(0, kernel.CommittedSlotCount)
                .Select(index => kernel.GetCommittedSlot(index)),
            record =>
                record.Owner == AgnusChipSlotOwner.Sprite &&
                record.Address == 0x3000 &&
                record.Value == 0x2468);
        Assert.Contains(
            Enumerable.Range(0, kernel.CommittedSlotCount)
                .Select(index => kernel.GetCommittedSlot(index)),
            record =>
                record.Owner == AgnusChipSlotOwner.Bitplane &&
                record.Address == 0x1000 &&
                record.Value == 0x1357);
    }

    [Fact]
    public void OfflineKernelAppliesMidLineDmaconOnlyToFutureSlots()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, TestSlotCount);
        var memory = CreateMemory();
        var firstFetch = SlotCycle(0x28);
        var secondFetch = SlotCycle(0x30);
        var thirdFetch = SlotCycle(0x38);
        var mutationCycle = SlotCycle(0x2C);
        fixture.SetInitialFixed(
            firstFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 0),
            0x1000);
        fixture.SetInitialFixed(
            secondFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
            0x1002);
        fixture.SetInitialFixed(
            thirdFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 2),
            0x1004);
        fixture.AddControlMutation(
            mutationCycle,
            register: 0x096,
            value: 0x0100,
            [
                new AgnusOfflineFixedSlotPatch(secondFetch, default),
                new AgnusOfflineFixedSlotPatch(thirdFetch, default)
            ]);
        fixture.AddCpuRequest(new CpuWordRequest(
            0x2000,
            secondFetch,
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal(1, trace.ControlMutationCount);
        Assert.Equal(1, kernel.ControlMutationsApplied);
        Assert.Equal(AgnusChipSlotOwner.Bitplane, kernel.GetCommittedSlot(SlotIndex(firstFetch)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Cpu, kernel.GetCommittedSlot(SlotIndex(secondFetch)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Free, kernel.GetCommittedSlot(SlotIndex(thirdFetch)).Owner);
    }

    [Fact]
    public void OfflineKernelAppliesMidLineDdfPlanReplacement()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, TestSlotCount);
        var memory = CreateMemory();
        WriteWord(memory, 0x1200, 0x1111);
        WriteWord(memory, 0x1202, 0x2222);
        WriteWord(memory, 0x2200, 0x3333);
        WriteWord(memory, 0x2202, 0x4444);
        var oldFirst = SlotCycle(0x28);
        var oldSecond = SlotCycle(0x30);
        var newFirst = SlotCycle(0x2C);
        var newSecond = SlotCycle(0x34);
        var mutationCycle = SlotCycle(0x24);
        fixture.SetInitialFixed(
            oldFirst,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 0),
            0x1200);
        fixture.SetInitialFixed(
            oldSecond,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
            0x1202);
        fixture.AddControlMutation(
            mutationCycle,
            register: 0x092,
            value: 0x002C,
            [
                new AgnusOfflineFixedSlotPatch(oldFirst, default),
                new AgnusOfflineFixedSlotPatch(oldSecond, default),
                new AgnusOfflineFixedSlotPatch(
                    newFirst,
                    new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 0),
                    0x2200),
                new AgnusOfflineFixedSlotPatch(
                    newSecond,
                    new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
                    0x2202)
            ]);
        fixture.AddCpuRequest(new CpuWordRequest(
            0x3000,
            oldFirst,
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal(AgnusChipSlotOwner.Cpu, kernel.GetCommittedSlot(SlotIndex(oldFirst)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Bitplane, kernel.GetCommittedSlot(SlotIndex(newFirst)).Owner);
        Assert.Equal((ushort)0x3333, kernel.GetCommittedSlot(SlotIndex(newFirst)).Value);
        Assert.Equal(AgnusChipSlotOwner.Bitplane, kernel.GetCommittedSlot(SlotIndex(newSecond)).Owner);
        Assert.Equal((ushort)0x4444, kernel.GetCommittedSlot(SlotIndex(newSecond)).Value);
    }

    [Fact]
    public void OfflineKernelAppliesMidLinePointerWriteToFutureFetchAddress()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, TestSlotCount);
        var memory = CreateMemory();
        WriteWord(memory, 0x1000, 0x1111);
        WriteWord(memory, 0x1002, 0x2222);
        WriteWord(memory, 0x2000, 0xBEEF);
        var firstFetch = SlotCycle(0x28);
        var secondFetch = SlotCycle(0x30);
        var mutationCycle = SlotCycle(0x2C);
        fixture.SetInitialFixed(
            firstFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 0),
            0x1000);
        fixture.SetInitialFixed(
            secondFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
            0x1002);
        fixture.AddControlMutation(
            mutationCycle,
            register: 0x0E2,
            value: 0x2000,
            [
                new AgnusOfflineFixedSlotPatch(
                    secondFetch,
                    new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
                    0x2000)
            ]);

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        var first = kernel.GetCommittedSlot(SlotIndex(firstFetch));
        var second = kernel.GetCommittedSlot(SlotIndex(secondFetch));
        Assert.Equal((uint)0x1000, first.Address);
        Assert.Equal((ushort)0x1111, first.Value);
        Assert.Equal((uint)0x2000, second.Address);
        Assert.Equal((ushort)0xBEEF, second.Value);
    }

    [Fact]
    public void OfflineKernelMakesCpuSelfModifiedInstructionVisibleToLaterFetch()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 32);
        var memory = CreateMemory();
        WriteWord(memory, 0x2400, 0x4E71);
        fixture.AddCpuRequest(new CpuWordRequest(
            0x2400,
            requestedCycle: SlotCycle(0x0A),
            AmigaBusAccessKind.CpuDataWrite,
            isWrite: true,
            value: 0x4E75));
        fixture.AddCpuRequest(new CpuWordRequest(
            0x2400,
            requestedCycle: SlotCycle(0x0C),
            AmigaBusAccessKind.CpuInstructionFetch,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        var replayMemory = (byte[])memory.Clone();
        kernel.Load(trace, replayMemory);

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal((ushort)0x4E75, kernel.GetCpuResult(0).Value);
        Assert.Equal((ushort)0x4E75, kernel.GetCpuResult(1).Value);
        Assert.Equal((ushort)0x4E75, ReadWord(replayMemory, 0x2400));
    }

    [Fact]
    public void OfflineKernelGrantCpuLongUsesTwoChronologicalWordGrants()
    {
        var trace = new AgnusOfflineReplayTrace(firstCycle: 0, slotCount: 32);
        var memory = CreateMemory();
        WriteWord(memory, 0x2800, 0x1234);
        WriteWord(memory, 0x2802, 0x5678);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, memory);

        var result = kernel.GrantCpuLong(new CpuLongRequest(
            0x2800,
            requestedCycle: SlotCycle(0x08),
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));

        Assert.Equal(0x12345678u, result.Value);
        Assert.True(result.FirstGrantedCycle >= SlotCycle(0x08));
        Assert.True(result.SecondGrantedCycle >=
            result.FirstGrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles));
        Assert.Equal(
            result.SecondGrantedCycle + AgnusChipSlotScheduler.SlotCycles,
            result.CompletedCycle);
    }

    [Fact]
    public void OfflineKernelReplayAllocatesNothingAfterWarmup()
    {
        var fixture = CreatePointerFixture();
        var memory = CreateMemory();
        WriteWord(memory, 0x1000, 0x1111);
        WriteWord(memory, 0x2000, 0x2222);
        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());
        AssertReplayEqual(kernel.Replay());
        var replayMemory = (byte[])memory.Clone();
        kernel.Load(trace, replayMemory);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var comparison = kernel.Replay();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        AssertReplayEqual(comparison);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OfflineTraceCaptureIsDeterministic()
    {
        var memory = CreateMemory();
        WriteWord(memory, 0x1000, 0x1111);
        WriteWord(memory, 0x2000, 0x2222);
        var first = CreatePointerFixture().CaptureExpected(memory);
        var second = CreatePointerFixture().CaptureExpected(memory);

        Assert.NotEqual(0UL, first.CaptureDeterministicHash());
        Assert.Equal(first.CaptureDeterministicHash(), second.CaptureDeterministicHash());
    }

    [Fact]
    public void OfflineKernelRejectsUnsupportedChipsetProfiles()
    {
        Assert.Throws<NotSupportedException>(() =>
            new AgnusSlotKernel(AmigaChipset.OcsNtsc));
        Assert.Throws<NotSupportedException>(() =>
            new AgnusSlotKernel(AmigaChipset.EcsPal));
    }

    [Fact]
    public void OfflineTraceRejectsBoundedBufferOverflow()
    {
        var trace = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 8,
            maximumCpuRequests: 1,
            maximumControlMutations: 1,
            maximumFixedPatches: 1);
        trace.AddCpuRequest(new CpuWordRequest(
            0x1000,
            requestedCycle: SlotCycle(0x02),
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));
        Assert.Throws<InvalidOperationException>(() =>
            trace.AddCpuRequest(new CpuWordRequest(
                0x1002,
                requestedCycle: SlotCycle(0x03),
                AmigaBusAccessKind.CpuDataRead,
                isWrite: false)));
        trace.AddControlMutation(
            cycle: SlotCycle(0x01),
            register: 0x096,
            value: 0x0100,
            [
                new AgnusOfflineFixedSlotPatch(
                    SlotCycle(0x02),
                    new FixedSlotPlanEntry(
                        AgnusChipSlotOwner.Bitplane,
                        channel: 0,
                        phase: 0),
                    address: 0x2000)
            ]);
        Assert.Throws<InvalidOperationException>(() =>
            trace.AddControlMutation(
                cycle: SlotCycle(0x02),
                register: 0x092,
                value: 0x0038,
                ReadOnlySpan<AgnusOfflineFixedSlotPatch>.Empty));
    }

    [Fact]
    public void OfflineCopperFetchOwnershipMatchesOracleAcrossFixedDmaCpuAndRefresh()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 240);
        var memory = CreateMemory();
        const uint copperList = 0x2400;
        var spriteCycle = SlotCycle(0x18);
        var firstCopperCycle = SlotCycle(0x1A);
        var bitplaneCycle = SlotCycle(0x1C);
        var cpuCycle = SlotCycle(0x1B);
        var secondCopperCycle = SlotCycle(0x1E);
        fixture.SetInitialFixed(
            spriteCycle,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Sprite, channel: 2, phase: 0),
            0x3000);
        fixture.SetInitialFixed(
            bitplaneCycle,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 1, phase: 3),
            0x1000);
        WriteWord(memory, 0x3000, 0xAAAA);
        WriteWord(memory, 0x1000, 0xBBBB);
        WriteWord(memory, 0x4000, 0xCCCC);
        WriteCopperInstruction(memory, copperList, 0x0180, 0x0F00);
        WriteCopperInstruction(memory, copperList + 4, 0xFFFF, 0xFFFE);
        fixture.StartCopper(copperList, spriteCycle);
        fixture.AddCopperInstruction(
            copperList,
            0x0180,
            0x0F00,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x0F00);
        fixture.AddCopperInstruction(
            copperList + 4,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);
        fixture.AddCpuRequest(new CpuWordRequest(
            0x4000,
            firstCopperCycle,
            AmigaBusAccessKind.CpuInstructionFetch,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal(AgnusChipSlotOwner.Sprite, kernel.GetCommittedSlot(SlotIndex(spriteCycle)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Copper, kernel.GetCommittedSlot(SlotIndex(firstCopperCycle)).Owner);
        Assert.Equal((byte)0, kernel.GetCommittedSlot(SlotIndex(firstCopperCycle)).Phase);
        Assert.Equal(AgnusChipSlotOwner.Bitplane, kernel.GetCommittedSlot(SlotIndex(bitplaneCycle)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Cpu, kernel.GetCommittedSlot(SlotIndex(cpuCycle)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Copper, kernel.GetCommittedSlot(SlotIndex(secondCopperCycle)).Owner);
        Assert.Equal((byte)1, kernel.GetCommittedSlot(SlotIndex(secondCopperCycle)).Phase);
        Assert.Equal(
            AgnusChipSlotOwner.Refresh,
            kernel.GetCommittedSlot(SlotIndex(SlotCycle(0x00))).Owner);
        Assert.Equal((ushort)0xCCCC, kernel.GetCpuResult(0).Value);
        Assert.Equal(trace.CopperInstructionCount * 2, kernel.CopperFetchesCommitted);
    }

    [Fact]
    public void OfflineCopperWaitAndSkipPreserveExactStateTransitions()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 160);
        var memory = CreateMemory();
        const uint copperList = 0x2400;
        WriteCopperInstruction(memory, copperList, 0x0001, 0x00FF);
        WriteCopperInstruction(memory, copperList + 4, 0x0180, 0x0F00);
        WriteCopperInstruction(memory, copperList + 8, 0x4001, 0xFFFE);
        WriteCopperInstruction(memory, copperList + 12, 0x0180, 0x00F0);
        WriteCopperInstruction(memory, copperList + 16, 0xFFFF, 0xFFFE);
        fixture.StartCopper(copperList, SlotCycle(0x20));
        fixture.AddCopperInstruction(
            copperList,
            0x0001,
            0x00FF,
            AgnusOfflineCopperAction.Skip,
            comparisonSatisfied: true);
        fixture.AddCopperInstruction(
            copperList + 4,
            0x0180,
            0x0F00,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x0F00);
        fixture.AddCopperInstruction(
            copperList + 8,
            0x4001,
            0xFFFE,
            AgnusOfflineCopperAction.Wait,
            waitResumeCycle: SlotCycle(0x50));
        fixture.AddCopperInstruction(
            copperList + 12,
            0x0180,
            0x00F0,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x00F0);
        fixture.AddCopperInstruction(
            copperList + 16,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal(AgnusOfflineCopperAction.Skip, kernel.GetCopperTransition(0).Action);
        Assert.True(kernel.GetCopperTransition(0).ComparisonSatisfied);
        Assert.True(kernel.GetCopperTransition(1).MoveSuppressed);
        Assert.False(kernel.GetCopperTransition(1).MutationCommitted);
        Assert.Equal(AgnusOfflineCopperAction.Wait, kernel.GetCopperTransition(2).Action);
        Assert.True(kernel.GetCopperTransition(2).Waiting);
        Assert.Equal(SlotCycle(0x50), kernel.GetCopperTransition(2).NextRequestedCycle);
        Assert.False(kernel.GetCopperTransition(3).MoveSuppressed);
        Assert.True(kernel.GetCopperTransition(3).MutationCommitted);
        Assert.Equal(1, kernel.CopperMutationCount);
        Assert.Equal((ushort)0x00F0, kernel.GetCopperMutation(0).Value);
        Assert.True(kernel.CopperStopped);
    }

    [Fact]
    public void OfflineCopperBfdWaitRemainsIdleUntilRecordedBlitterCompletionBoundary()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 160);
        var memory = CreateMemory();
        const uint copperList = 0x2400;
        WriteCopperInstruction(memory, copperList, 0x4001, 0x7FFE);
        WriteCopperInstruction(memory, copperList + 4, 0x0180, 0x0123);
        WriteCopperInstruction(memory, copperList + 8, 0xFFFF, 0xFFFE);
        fixture.StartCopper(copperList, SlotCycle(0x20));
        fixture.AddCopperInstruction(
            copperList,
            0x4001,
            0x7FFE,
            AgnusOfflineCopperAction.Wait,
            waitResumeCycle: SlotCycle(0x60),
            waitForBlitter: true);
        fixture.AddCopperInstruction(
            copperList + 4,
            0x0180,
            0x0123,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x0123);
        fixture.AddCopperInstruction(
            copperList + 8,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);
        fixture.AddCpuRequest(new CpuWordRequest(
            0x3000,
            SlotCycle(0x30),
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));
        WriteWord(memory, 0x3000, 0xBFD0);

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        var wait = kernel.GetCopperTransition(0);
        Assert.True(wait.WaitForBlitter);
        Assert.Equal(SlotCycle(0x60), wait.NextRequestedCycle);
        Assert.Equal((ushort)0xBFD0, kernel.GetCpuResult(0).Value);
        Assert.DoesNotContain(
            Enumerable.Range(0, kernel.CommittedSlotCount)
                .Select(index => kernel.GetCommittedSlot(index)),
            record =>
                record.Owner == AgnusChipSlotOwner.Copper &&
                record.Cycle > wait.TransitionCycle &&
                record.Cycle < wait.NextRequestedCycle);
    }

    [Fact]
    public void OfflineCopperMoveCommitsAtSecondFetchAndChangesOnlyFuturePlan()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 96);
        var memory = CreateMemory();
        const uint copperList = 0x2400;
        var patchedBitplaneCycle = SlotCycle(0x24);
        WriteCopperInstruction(memory, copperList, 0x0092, 0x0024);
        WriteCopperInstruction(memory, copperList + 4, 0xFFFF, 0xFFFE);
        WriteWord(memory, 0x1800, 0xDDF0);
        fixture.StartCopper(copperList, SlotCycle(0x20));
        fixture.AddCopperInstruction(
            copperList,
            0x0092,
            0x0024,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x092,
            moveValue: 0x0024,
            patches:
            [
                new AgnusOfflineFixedSlotPatch(
                    patchedBitplaneCycle,
                    new FixedSlotPlanEntry(
                        AgnusChipSlotOwner.Bitplane,
                        channel: 0,
                        phase: 0),
                    0x1800)
            ]);
        fixture.AddCopperInstruction(
            copperList + 4,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        var move = kernel.GetCopperTransition(0);
        var mutation = kernel.GetCopperMutation(0);
        Assert.Equal(move.SecondGrantedCycle, mutation.Cycle);
        Assert.Equal(AgnusChipSlotOwner.Bitplane, kernel.GetCommittedSlot(SlotIndex(patchedBitplaneCycle)).Owner);
        Assert.Equal((ushort)0xDDF0, kernel.GetCommittedSlot(SlotIndex(patchedBitplaneCycle)).Value);
        Assert.Equal(patchedBitplaneCycle, kernel.GetCopperTransition(1).FirstRequestedCycle);
        Assert.Equal(SlotCycle(0x26), kernel.GetCopperTransition(1).FirstGrantedCycle);
    }

    [Fact]
    public void OfflineCopperCopjmpMutatesAtDataCycleAndContinuesAtJumpTarget()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 128);
        var memory = CreateMemory();
        const uint firstList = 0x2400;
        const uint secondList = 0x2600;
        WriteCopperInstruction(memory, firstList, 0x0088, 0x0000);
        WriteCopperInstruction(memory, secondList, 0x0180, 0x0ABC);
        WriteCopperInstruction(memory, secondList + 4, 0xFFFF, 0xFFFE);
        fixture.StartCopper(firstList, SlotCycle(0x20));
        fixture.AddCopperInstruction(
            firstList,
            0x0088,
            0x0000,
            AgnusOfflineCopperAction.Copjmp,
            nextPc: secondList,
            commitMove: true,
            moveRegister: 0x088,
            moveValue: 0x0000);
        fixture.AddCopperInstruction(
            secondList,
            0x0180,
            0x0ABC,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x0ABC);
        fixture.AddCopperInstruction(
            secondList + 4,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        var comparison = kernel.Replay();

        AssertReplayEqual(comparison);
        Assert.Equal(AgnusOfflineCopperAction.Copjmp, kernel.GetCopperTransition(0).Action);
        Assert.Equal(secondList, kernel.GetCopperTransition(0).NextPc);
        Assert.Equal(secondList, kernel.GetCopperTransition(1).Pc);
        Assert.Equal(2, kernel.CopperMutationCount);
        Assert.Equal((ushort)0x088, kernel.GetCopperMutation(0).Register);
        Assert.Equal(kernel.GetCopperTransition(0).SecondGrantedCycle, kernel.GetCopperMutation(0).Cycle);
        Assert.Equal((ushort)0x180, kernel.GetCopperMutation(1).Register);
    }

    [Fact]
    public void OfflineCopperReplayAllocatesNothingAfterWarmup()
    {
        var memory = CreateMemory();
        var trace = CreateCopperMoveFixture(memory).CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());
        AssertReplayEqual(kernel.Replay());
        kernel.Load(trace, (byte[])memory.Clone());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var comparison = kernel.Replay();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        AssertReplayEqual(comparison);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OfflineCopperDiagnosticsReportNoForbiddenProductionCalls()
    {
        var memory = CreateMemory();
        var trace = CreateCopperMoveFixture(memory).CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        AssertReplayEqual(kernel.Replay());
        var diagnostics = kernel.CaptureDiagnostics();

        Assert.Equal(trace.SlotCount, diagnostics.SlotDecisions);
        Assert.Equal(trace.CopperInstructionCount * 2, diagnostics.CopperFetches);
        Assert.Equal(trace.CopperInstructionCount, diagnostics.CopperTransitions);
        Assert.Equal(trace.ExpectedCopperMutationCount, diagnostics.CopperMutations);
        Assert.Equal(0, diagnostics.GenericSchedulerDrains);
        Assert.Equal(0, diagnostics.DeviceWideAdvanceCalls);
        Assert.Equal(0, diagnostics.ProductionBusCalls);
    }

    [Fact]
    public void OfflineCopperTraceCaptureIsDeterministic()
    {
        var firstMemory = CreateMemory();
        var first = CreateCopperMoveFixture(firstMemory).CaptureExpected(firstMemory);
        var secondMemory = CreateMemory();
        var second = CreateCopperMoveFixture(secondMemory).CaptureExpected(secondMemory);

        Assert.NotEqual(0UL, first.CaptureDeterministicHash());
        Assert.Equal(first.CaptureDeterministicHash(), second.CaptureDeterministicHash());
    }

    [Fact]
    public void OfflineTraceRejectsCopperInstructionBufferOverflow()
    {
        var trace = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 32,
            maximumCopperInstructions: 1);
        trace.AddCopperInstruction(
            pc: 0x1000,
            firstRequestedCycle: SlotCycle(0x02),
            firstGrantedCycle: SlotCycle(0x02),
            secondRequestedCycle: SlotCycle(0x04),
            secondGrantedCycle: SlotCycle(0x04),
            firstWord: 0x0180,
            secondWord: 0x0123,
            AgnusOfflineCopperAction.Move,
            transitionCycle: SlotCycle(0x04),
            nextPc: 0x1004,
            nextRequestedCycle: SlotCycle(0x06));

        Assert.Throws<InvalidOperationException>(() =>
            trace.AddCopperInstruction(
                pc: 0x1004,
                firstRequestedCycle: SlotCycle(0x06),
                firstGrantedCycle: SlotCycle(0x06),
                secondRequestedCycle: SlotCycle(0x08),
                secondGrantedCycle: SlotCycle(0x08),
                firstWord: 0xFFFF,
                secondWord: 0xFFFE,
                AgnusOfflineCopperAction.End,
                transitionCycle: SlotCycle(0x0A),
                nextPc: 0,
                nextRequestedCycle: -1));

        var mutationTrace = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 32,
            maximumCopperInstructions: 2,
            maximumCopperMutations: 1);
        mutationTrace.AddCopperInstruction(
            pc: 0x2000,
            firstRequestedCycle: SlotCycle(0x02),
            firstGrantedCycle: SlotCycle(0x02),
            secondRequestedCycle: SlotCycle(0x04),
            secondGrantedCycle: SlotCycle(0x04),
            firstWord: 0x0180,
            secondWord: 0x0001,
            AgnusOfflineCopperAction.Move,
            transitionCycle: SlotCycle(0x04),
            nextPc: 0x2004,
            nextRequestedCycle: SlotCycle(0x06),
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x0001);
        Assert.Throws<InvalidOperationException>(() =>
            mutationTrace.AddCopperInstruction(
                pc: 0x2004,
                firstRequestedCycle: SlotCycle(0x06),
                firstGrantedCycle: SlotCycle(0x06),
                secondRequestedCycle: SlotCycle(0x08),
                secondGrantedCycle: SlotCycle(0x08),
                firstWord: 0x0180,
                secondWord: 0x0002,
                AgnusOfflineCopperAction.Move,
                transitionCycle: SlotCycle(0x08),
                nextPc: 0x2008,
                nextRequestedCycle: SlotCycle(0x0A),
                commitMove: true,
                moveRegister: 0x180,
                moveValue: 0x0002));
    }

    [Fact]
    public void OfflineBlitterReplaysBasicAreaMicroOpsFromProductionOracle()
    {
        var run = CaptureBlitter(
            bus =>
            {
                WriteWord(bus.ChipRam, 0x3000, 0xFF00);
                WriteWord(bus.ChipRam, 0x3200, 0x0F0F);
                WriteWord(bus.ChipRam, 0x3400, 0x3333);
                ConfigureAreaBlitter(bus, 0x0FCA);
            },
            bltSize: 0x0041,
            ClassifyAreaBlitterAccess);

        var replay = ReplayBlitter(run);

        Assert.Equal(run.FinalMemory, replay.Memory);
        Assert.Collection(
            run.Operation.MicroOps,
            op => Assert.Equal(AgnusOfflineBlitterMicroOpKind.AreaReadA, op.Kind),
            op => Assert.Equal(AgnusOfflineBlitterMicroOpKind.AreaReadB, op.Kind),
            op => Assert.Equal(AgnusOfflineBlitterMicroOpKind.AreaReadC, op.Kind),
            op => Assert.Equal(AgnusOfflineBlitterMicroOpKind.AreaWriteD, op.Kind));
        Assert.False(replay.Kernel.BlitterBusy);
        Assert.Equal(1, replay.Kernel.BlitterCompletionCount);
    }

    [Theory]
    [InlineData(0x0)]
    [InlineData(0x1)]
    [InlineData(0x2)]
    [InlineData(0x3)]
    [InlineData(0x4)]
    [InlineData(0x5)]
    [InlineData(0x6)]
    [InlineData(0x7)]
    [InlineData(0x8)]
    [InlineData(0x9)]
    [InlineData(0xA)]
    [InlineData(0xB)]
    [InlineData(0xC)]
    [InlineData(0xD)]
    [InlineData(0xE)]
    [InlineData(0xF)]
    public void OfflineBlitterReplaysEveryAreaChannelCombination(int channelMask)
    {
        var run = CaptureBlitter(
            bus =>
            {
                WriteWord(bus.ChipRam, 0x3000, 0xFF00);
                WriteWord(bus.ChipRam, 0x3200, 0x0F0F);
                WriteWord(bus.ChipRam, 0x3400, 0x3333);
                WriteWord(bus.ChipRam, 0x4000, 0x5A5A);
                bus.WriteWord(0x00DFF074, 0x00FF);
                bus.WriteWord(0x00DFF072, 0xF0F0);
                bus.WriteWord(0x00DFF070, 0xCCCC);
                ConfigureAreaBlitter(
                    bus,
                    (ushort)((channelMask << 8) | 0xCA));
            },
            bltSize: 0x0041,
            ClassifyAreaBlitterAccess);

        var replay = ReplayBlitter(run);

        Assert.Equal(run.FinalMemory, replay.Memory);
        var expectedMicroOps =
            ((channelMask & 0x8) != 0 ? 1 : 0) +
            ((channelMask & 0x4) != 0 ? 1 : 0) +
            ((channelMask & 0x2) != 0 ? 1 : 0) +
            ((channelMask & 0x1) != 0 ? 1 : 0);
        Assert.Equal(expectedMicroOps, replay.Kernel.BlitterMicroOpCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfflineBlitterReplaysMasksPointersModulosAndDirection(bool descending)
    {
        var source = descending ? 0x3010u : 0x3000u;
        var destination = descending ? 0x4010u : 0x4000u;
        var run = CaptureBlitter(
            bus =>
            {
                for (var address = 0x2FF0u; address <= 0x3020; address += 2)
                {
                    WriteWord(
                        bus.ChipRam,
                        address,
                        (ushort)(0x8000 | (address & 0x0FFF)));
                }

                ConfigureAreaBlitter(
                    bus,
                    0x09F0,
                    descending ? (ushort)0x0002 : (ushort)0,
                    sourceA: source,
                    destinationD: destination);
                bus.WriteWord(0x00DFF044, 0x0FF0);
                bus.WriteWord(0x00DFF046, 0xF00F);
                bus.WriteWord(0x00DFF064, 4);
                bus.WriteWord(0x00DFF066, 4);
            },
            bltSize: 0x0082,
            ClassifyAreaBlitterAccess);

        var replay = ReplayBlitter(run);

        Assert.Equal(run.FinalMemory, replay.Memory);
        Assert.Equal(run.Operation.SourceA, replay.Kernel.GetBlitterCompletion().SourceA);
        Assert.Equal(
            run.Operation.DestinationD,
            replay.Kernel.GetBlitterCompletion().DestinationD);
    }

    [Fact]
    public void OfflineBlitterReplaysLineModeMicroOps()
    {
        const uint lineBase = 0x3800;
        var run = CaptureBlitter(
            bus =>
            {
                WriteWord(bus.ChipRam, 0x3200, 0x8000);
                for (var address = lineBase; address < lineBase + 0x80; address += 2)
                {
                    WriteWord(bus.ChipRam, address, 0);
                }

                bus.WriteWord(0x00DFF040, 0x0FCA);
                bus.WriteWord(0x00DFF042, 0x0001);
                WritePointer(bus, 0x00DFF048, lineBase);
                WritePointer(bus, 0x00DFF04C, 0x3200);
                WritePointer(bus, 0x00DFF050, 0);
                WritePointer(bus, 0x00DFF054, lineBase);
                bus.WriteWord(0x00DFF060, 0x20);
                bus.WriteWord(0x00DFF062, 0);
                bus.WriteWord(0x00DFF064, 0);
                bus.WriteWord(0x00DFF066, 0x20);
                bus.WriteWord(0x00DFF072, 0x8000);
                bus.WriteWord(0x00DFF074, 0x8000);
            },
            bltSize: 0x0102,
            access => access.Request.IsWrite
                ? AgnusOfflineBlitterMicroOpKind.LineWriteD
                : access.Request.Address >= 0x3200 &&
                    access.Request.Address < 0x3400
                    ? AgnusOfflineBlitterMicroOpKind.LineReadB
                    : AgnusOfflineBlitterMicroOpKind.LineReadC);

        var replay = ReplayBlitter(run);

        Assert.Equal(run.FinalMemory, replay.Memory);
        Assert.Contains(
            run.Operation.MicroOps,
            op => op.Kind == AgnusOfflineBlitterMicroOpKind.LineReadB);
        Assert.Contains(
            run.Operation.MicroOps,
            op => op.Kind == AgnusOfflineBlitterMicroOpKind.LineReadC);
        Assert.Contains(
            run.Operation.MicroOps,
            op => op.Kind == AgnusOfflineBlitterMicroOpKind.LineWriteD);
    }

    [Fact]
    public void OfflineBlitterNastyPriorityWinsCpuCollision()
    {
        var run = CaptureBlitter(
            bus =>
            {
                for (var index = 0; index < 4; index++)
                {
                    WriteWord(bus.ChipRam, 0x3000u + (uint)(index * 2), (ushort)(0x1200 + index));
                }

                ConfigureAreaBlitter(bus, 0x09F0);
            },
            bltSize: 0x0044,
            ClassifyAreaBlitterAccess,
            nasty: true);
        var collisionCycle = run.Operation.MicroOps[0].RequestedCycle;
        var replay = ReplayBlitter(
            run,
            fixture => fixture.AddCpuRequest(new CpuWordRequest(
                0x5000,
                collisionCycle,
                AmigaBusAccessKind.CpuDataRead,
                isWrite: false)));

        Assert.Equal(run.FinalMemory, replay.Memory);
        Assert.Equal(
            replay.Kernel.GetBlitterMicroOp(0).GrantedCycle,
            collisionCycle);
        Assert.True(
            replay.Kernel.GetCpuResult(0).GrantedCycle >
            replay.Kernel.GetBlitterMicroOp(0).GrantedCycle);
    }

    [Fact]
    public void OfflineCpuWriteFeedsBlitterAndFetchSeesCommittedDestinationWrite()
    {
        const long cpuWriteCycle = 0x70;
        const ushort replacement = 0xBEEF;
        var run = CaptureBlitter(
            bus =>
            {
                WriteWord(bus.ChipRam, 0x3000, 0x1111);
                ConfigureAreaBlitter(bus, 0x09F0);
            },
            bltSize: 0x0041,
            ClassifyAreaBlitterAccess,
            afterStart: bus => bus.WriteWord(0x3000, replacement, cpuWriteCycle));
        var finalWrite = Assert.Single(
            run.Operation.MicroOps.Where(op => op.IsWrite));
        var replay = ReplayBlitter(
            run,
            fixture =>
            {
                fixture.AddCpuRequest(new CpuWordRequest(
                    0x3000,
                    cpuWriteCycle,
                    AmigaBusAccessKind.CpuDataWrite,
                    isWrite: true,
                    replacement));
                fixture.AddCpuRequest(new CpuWordRequest(
                    0x4000,
                    finalWrite.RequestedCycle + AgnusChipSlotScheduler.SlotCycles,
                    AmigaBusAccessKind.CpuInstructionFetch,
                    isWrite: false));
            });

        Assert.Equal(replacement, ReadWord(replay.Memory, 0x4000));
        Assert.Equal(replacement, replay.Kernel.GetCpuResult(1).Value);
        Assert.True(
            replay.Kernel.GetCpuResult(1).GrantedCycle >
            replay.Kernel.GetBlitterMicroOp(replay.Kernel.BlitterMicroOpCount - 1).GrantedCycle);
    }

    [Fact]
    public void OfflineBlitterReplaysMidOperationBltcon0ControlChange()
    {
        var run = CaptureBlitter(
            bus =>
            {
                for (var index = 0; index < 3; index++)
                {
                    WriteWord(bus.ChipRam, 0x3000u + (uint)(index * 2), (ushort)(0x7000 + index));
                    WriteWord(bus.ChipRam, 0x4000u + (uint)(index * 2), 0x5A5A);
                }

                ConfigureAreaBlitter(bus, 0x09F0);
            },
            bltSize: 0x0043,
            ClassifyAreaBlitterAccess,
            afterStart: bus =>
            {
                var controlCycle =
                    bus.Blitter.CaptureSnapshot().NextDmaCycle +
                    (2 * AgnusChipSlotScheduler.SlotCycles);
                bus.AdvanceDmaTo(controlCycle);
                bus.Blitter.WriteRegister(0x040, 0x08F0, controlCycle);
            });

        var replay = ReplayBlitter(run);

        Assert.Equal(run.FinalMemory, replay.Memory);
        Assert.Single(run.Operation.MicroOps.Where(op => op.IsWrite));
        Assert.Equal(
            3,
            run.Operation.MicroOps.Count(
                op => op.Kind == AgnusOfflineBlitterMicroOpKind.AreaReadA));
    }

    [Fact]
    public void OfflineCopperTriggeredBlitterPreservesFixedAndCpuChronology()
    {
        const uint copperList = 0x2400;
        var run = CaptureBlitter(
            bus =>
            {
                WriteWord(bus.ChipRam, 0x3000, 0xCAFE);
                ConfigureAreaBlitter(bus, 0x09F0);
            },
            bltSize: 0x0041,
            ClassifyAreaBlitterAccess,
            startWriteCycle: SlotCycle(0x22));
        WriteCopperInstruction(run.InitialMemory, copperList, 0x0058, 0x0041);
        WriteCopperInstruction(run.InitialMemory, copperList + 4, 0xFFFF, 0xFFFE);
        WriteCopperInstruction(run.FinalMemory, copperList, 0x0058, 0x0041);
        WriteCopperInstruction(run.FinalMemory, copperList + 4, 0xFFFF, 0xFFFE);
        var fixedCycle = run.Operation.MicroOps[0].RequestedCycle;
        var spriteCycle = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(
            fixedCycle + AgnusChipSlotScheduler.SlotCycles,
            AgnusChipSlotOwner.Sprite,
            channel: 2);
        var finalWrite = Assert.Single(run.Operation.MicroOps.Where(op => op.IsWrite));
        var replay = ReplayBlitter(
            run,
            fixture =>
            {
                fixture.StartCopper(copperList, SlotCycle(0x20));
                fixture.AddCopperInstruction(
                    copperList,
                    0x0058,
                    0x0041,
                    AgnusOfflineCopperAction.Move,
                    commitMove: true,
                    moveRegister: 0x058,
                    moveValue: 0x0041);
                fixture.AddCopperInstruction(
                    copperList + 4,
                    0xFFFF,
                    0xFFFE,
                    AgnusOfflineCopperAction.End);
                fixture.SetInitialFixed(
                    fixedCycle,
                    new FixedSlotPlanEntry(
                        AgnusChipSlotOwner.Bitplane,
                        channel: 0,
                        phase: 0),
                    0x5000);
                fixture.SetInitialFixed(
                    spriteCycle,
                    new FixedSlotPlanEntry(
                        AgnusChipSlotOwner.Sprite,
                        channel: 2,
                        phase: 1),
                    0x5002);
                fixture.AddCpuRequest(new CpuWordRequest(
                    0x4000,
                    finalWrite.RequestedCycle + (2 * AgnusChipSlotScheduler.SlotCycles),
                    AmigaBusAccessKind.CpuInstructionFetch,
                    isWrite: false));
            });

        Assert.Equal(AgnusChipSlotOwner.Bitplane, replay.Kernel.GetCommittedSlot(SlotIndex(fixedCycle)).Owner);
        Assert.Equal(AgnusChipSlotOwner.Sprite, replay.Kernel.GetCommittedSlot(SlotIndex(spriteCycle)).Owner);
        Assert.Equal(2, replay.Kernel.CopperTransitionCount);
        Assert.Equal(run.FinalMemory, replay.Memory);
    }

    [Fact]
    public void OfflineBlitterCompletionInterruptDiagnosticsAndAllocationGate()
    {
        var run = CaptureBlitter(
            bus =>
            {
                WriteWord(bus.ChipRam, 0x3000, 0x0000);
                ConfigureAreaBlitter(bus, 0x09F0);
            },
            bltSize: 0x0041,
            ClassifyAreaBlitterAccess);
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        fixture.SetBlitterOperation(run.Operation);
        var trace = fixture.CaptureExpected(run.InitialMemory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])run.InitialMemory.Clone());
        Assert.False(kernel.BlitterBusy);
        kernel.AdvanceThrough(
            run.Operation.StartCycle - AgnusChipSlotScheduler.SlotCycles);
        Assert.False(kernel.BlitterBusy);
        kernel.AdvanceThrough(run.Operation.StartCycle);
        Assert.True(kernel.BlitterBusy);
        AssertReplayEqual(kernel.Replay());
        kernel.Load(trace, (byte[])run.InitialMemory.Clone());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var comparison = kernel.Replay();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var diagnostics = kernel.CaptureDiagnostics();

        AssertReplayEqual(comparison);
        Assert.Equal(0, allocated);
        Assert.Equal(trace.BlitterMicroOpCount, diagnostics.BlitterMicroOps);
        Assert.Equal(1, diagnostics.BlitterCompletions);
        Assert.Equal(0, diagnostics.GenericSchedulerDrains);
        Assert.Equal(0, diagnostics.DeviceWideAdvanceCalls);
        Assert.Equal(0, diagnostics.ProductionBusCalls);
        Assert.Equal(0, diagnostics.GenericEventDiscoveries);
        Assert.Equal(0, diagnostics.VisibilityHorizonQueries);
        Assert.Equal(0, diagnostics.SpeculativePredictions);
        Assert.Equal(0, diagnostics.Rollbacks);
        var completion = kernel.GetBlitterCompletion();
        Assert.True(completion.Zero);
        Assert.Equal(completion.Cycle, completion.InterruptCycle);
        Assert.Equal(completion.Cycle, kernel.BlitterInterruptCycle);
    }

    [Fact]
    public void OfflineBlitterTraceCaptureIsDeterministicAndBounded()
    {
        static CapturedBlitterRun Capture()
            => CaptureBlitter(
                bus =>
                {
                    WriteWord(bus.ChipRam, 0x3000, 0x1234);
                    ConfigureAreaBlitter(bus, 0x09F0);
                },
                bltSize: 0x0041,
                ClassifyAreaBlitterAccess);

        var firstRun = Capture();
        var firstFixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        firstFixture.SetBlitterOperation(firstRun.Operation);
        var first = firstFixture.CaptureExpected(firstRun.InitialMemory);
        var secondRun = Capture();
        var secondFixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        secondFixture.SetBlitterOperation(secondRun.Operation);
        var second = secondFixture.CaptureExpected(secondRun.InitialMemory);

        Assert.NotEqual(0UL, first.CaptureDeterministicHash());
        Assert.Equal(first.CaptureDeterministicHash(), second.CaptureDeterministicHash());

        var bounded = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 64,
            maximumBlitterMicroOps: 1);
        bounded.StartBlitterReplay(SlotCycle(0x10), nasty: false);
        bounded.AddBlitterMicroOp(
            AgnusOfflineBlitterMicroOpKind.AreaReadA,
            0x3000,
            0x1234,
            isWrite: false,
            requestedCycle: SlotCycle(0x10),
            grantedCycle: SlotCycle(0x10),
            completedCycle: SlotCycle(0x11));
        Assert.Throws<InvalidOperationException>(() =>
            bounded.AddBlitterMicroOp(
                AgnusOfflineBlitterMicroOpKind.AreaWriteD,
                0x4000,
                0x1234,
                isWrite: true,
                requestedCycle: SlotCycle(0x11),
                grantedCycle: SlotCycle(0x11),
                completedCycle: SlotCycle(0x12)));
    }

    [Fact]
    public void OfflinePaulaArbitratesAllFourChannelsOnExactHrmSlots()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 128);
        var memory = CreateMemory();
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var address = (uint)(0x5000 + (channel * 2));
            WriteWord(memory, address, (ushort)(0x1100 + channel));
            fixture.AddPaulaWord(channel, address, requestedCycle: 0);
        }

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(4, kernel.PaulaDmaWordCount);
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var word = kernel.GetPaulaDmaWord(channel);
            Assert.Equal(channel, word.Channel);
            Assert.Equal(SlotCycle(0x10 + (channel * 2)), word.GrantedCycle);
            Assert.Equal((ushort)(0x1100 + channel), word.Value);
            var slot = kernel.GetCommittedSlot(SlotIndex(word.GrantedCycle));
            Assert.Equal(AgnusChipSlotOwner.Paula, slot.Owner);
            Assert.Equal(AmigaBusRequester.Paula, slot.Requester);
            Assert.Equal(AmigaBusAccessKind.PaulaDma, slot.Kind);
            Assert.Equal((byte)channel, slot.Channel);
            Assert.Equal((byte)0, slot.Phase);
        }
    }

    [Fact]
    public void OfflinePaulaPreservesOracleContentionAndCommitTimeSampling()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        var memory = CreateMemory();
        WriteWord(memory, 0x5200, 0xCAFE);
        fixture.SetInitialFixed(
            SlotCycle(0x10),
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 3),
            0x3000);
        fixture.AddPaulaWord(channel: 0, address: 0x5200, requestedCycle: 0);
        fixture.AddCpuRequest(new CpuWordRequest(
            0x5400,
            requestedCycle: 0,
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        var replayMemory = (byte[])memory.Clone();
        kernel.Load(trace, replayMemory);
        WriteWord(replayMemory, 0x5200, 0xBEEF);

        var comparison = kernel.Replay();

        Assert.False(comparison.Equal);
        Assert.Equal(SlotIndex(
            kernel.GetPaulaDmaWord(0).GrantedCycle), comparison.MismatchIndex);
        Assert.Equal((ushort)0xBEEF, comparison.Actual.Value);
        Assert.Equal((ushort)0xBEEF, kernel.GetPaulaDmaWord(0).Value);
        Assert.True(kernel.GetPaulaDmaWord(0).GrantedCycle >=
            AmigaConstants.A500PalCpuCyclesPerRasterLine + SlotCycle(0x10));
    }

    [Fact]
    public void OfflinePaulaContendsChronologicallyWithCopperBlitterAndCpu()
    {
        const uint copperList = 0x2400;
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        var memory = CreateMemory();
        WriteCopperInstruction(memory, copperList, 0xFFFF, 0xFFFE);
        WriteWord(memory, 0x5600, 0xA0A0);
        WriteWord(memory, 0x5800, 0xB0B0);
        fixture.StartCopper(copperList, SlotCycle(0x12));
        fixture.AddCopperInstruction(
            copperList,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);
        fixture.SetBlitterOperation(new CapturedBlitterOperation(
            StartCycle: SlotCycle(0x11),
            Nasty: true,
            CompletionDelay: 0,
            Zero: false,
            SourceA: 0x5602,
            SourceB: 0,
            SourceC: 0,
            DestinationD: 0,
            MicroOps:
            [
                new CapturedBlitterMicroOp(
                    AgnusOfflineBlitterMicroOpKind.AreaReadA,
                    0x5600,
                    0xA0A0,
                    IsWrite: false,
                    RequestedCycle: SlotCycle(0x11),
                    DelayAfterPreviousCompletion: 0)
            ]));
        fixture.AddPaulaWord(0, 0x5800, SlotCycle(0x10));
        fixture.AddCpuRequest(new CpuWordRequest(
            0x5A00,
            SlotCycle(0x11),
            AmigaBusAccessKind.CpuInstructionFetch,
            isWrite: false));

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        AssertReplayEqual(kernel.Replay());
        var paula = kernel.GetPaulaDmaWord(0);
        Assert.Equal(SlotCycle(0x10), paula.GrantedCycle);
        Assert.True(kernel.GetCopperTransition(0).FirstGrantedCycle > paula.GrantedCycle);
        Assert.True(kernel.GetBlitterMicroOp(0).GrantedCycle > paula.GrantedCycle);
        Assert.True(kernel.GetCpuResult(0).GrantedCycle > paula.GrantedCycle);
    }

    [Fact]
    public void OfflinePaulaPublishesNormalizedRegisterSampleReloadManualAndInterruptEvents()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 128);
        var kinds = new[]
        {
            AgnusOfflinePaulaEventKind.RegisterWrite,
            AgnusOfflinePaulaEventKind.DmaEnabled,
            AgnusOfflinePaulaEventKind.WordLoaded,
            AgnusOfflinePaulaEventKind.SampleHigh,
            AgnusOfflinePaulaEventKind.SampleLow,
            AgnusOfflinePaulaEventKind.LengthReloaded,
            AgnusOfflinePaulaEventKind.ManualData,
            AgnusOfflinePaulaEventKind.DmaDisabled,
            AgnusOfflinePaulaEventKind.Interrupt
        };
        for (var index = 0; index < kinds.Length; index++)
        {
            fixture.AddPaulaEvent(new AgnusOfflinePaulaEventRecord(
                cycle: index * AgnusChipSlotScheduler.SlotCycles,
                kinds[index],
                channel: index & 3,
                location: 0x6000,
                currentAddress: (uint)(0x6000 + (index * 2)),
                lengthWords: 2,
                remainingWords: Math.Max(0, 2 - index),
                period: 113 + index,
                volume: Math.Min(64, 32 + index),
                currentSample: (sbyte)index,
                dmaEnabled: index is >= 1 and < 7,
                dataWord: (ushort)(0x7000 + index),
                state: (byte)(index % 6),
                intreq: kinds[index] == AgnusOfflinePaulaEventKind.Interrupt
                    ? (ushort)(0x0080 << (index & 3))
                    : (ushort)0));
        }

        var trace = fixture.CaptureExpected(CreateMemory());
        var kernel = CreateKernel(trace);
        kernel.Load(trace, CreateMemory());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(kinds.Length, kernel.PaulaEventCount);
        for (var index = 0; index < kinds.Length; index++)
        {
            Assert.Equal(kinds[index], kernel.GetPaulaEvent(index).Kind);
        }
    }

    [Fact]
    public void OfflinePaulaReplayIsDeterministicBoundedAndAllocationFree()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        var memory = CreateMemory();
        for (var word = 0; word < 8; word++)
        {
            var channel = word & 3;
            var address = (uint)(0x6800 + (word * 2));
            WriteWord(memory, address, (ushort)(0x8000 + word));
            fixture.AddPaulaWord(
                channel,
                address,
                requestedCycle: word < 4
                    ? 0
                    : AmigaConstants.A500PalCpuCyclesPerRasterLine);
        }

        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());
        AssertReplayEqual(kernel.Replay());
        kernel.Load(trace, (byte[])memory.Clone());
        var before = GC.GetAllocatedBytesForCurrentThread();
        var comparison = kernel.Replay();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        AssertReplayEqual(comparison);
        Assert.Equal(0, allocated);
        Assert.Equal(8, kernel.CaptureDiagnostics().PaulaDmaWords);
        Assert.NotEqual(0UL, trace.CaptureDeterministicHash());

        var bounded = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 128,
            maximumPaulaDmaWords: 1);
        bounded.AddPaulaDmaWord(
            0,
            0x1000,
            0,
            requestedCycle: 0,
            grantedCycle: SlotCycle(0x10),
            completedCycle: SlotCycle(0x11));
        Assert.Throws<InvalidOperationException>(() =>
            bounded.AddPaulaDmaWord(
                1,
                0x1002,
                0,
                requestedCycle: 0,
                grantedCycle: SlotCycle(0x12),
                completedCycle: SlotCycle(0x13)));
    }

    [Fact]
    public void OfflinePaulaConsumesProductionAudioDmaAndStateTransitionsAsOracle()
    {
        var bus = new AmigaBus(captureBusAccesses: true);
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var address = (uint)(0x7000 + (channel * 0x20));
            WriteWord(bus.ChipRam, address, (ushort)(0x1200 + channel));
            WriteWord(bus.ChipRam, address + 2, (ushort)(0x3400 + channel));
            var registerBase = (ushort)(0x0A0 + (channel * 0x10));
            bus.Paula.ScheduleWrite(0, registerBase, (ushort)(address >> 16));
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 2), (ushort)address);
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 4), 2);
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 6), 113);
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 8), (ushort)(32 + channel));
        }

        bus.Paula.ScheduleWrite(0, 0x09A, 0xC780);
        bus.Paula.ScheduleWrite(0, 0x096, 0x820F);
        bus.Paula.AdvanceTo(0);
        var initialMemory = (byte[])bus.ChipRam.Clone();
        var events = new List<AgnusOfflinePaulaEventRecord>();
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            events.Add(CreatePaulaEvent(
                0,
                AgnusOfflinePaulaEventKind.DmaEnabled,
                bus.Paula.GetChannelSnapshot(channel),
                bus.Paula.Intreq));
        }

        bus.Paula.AdvanceTo(226);
        events.Add(CreatePaulaEvent(
            226,
            AgnusOfflinePaulaEventKind.SampleLow,
            bus.Paula.GetChannelSnapshot(0),
            bus.Paula.Intreq));
        bus.Paula.ScheduleWrite(400, 0x0A6, 80);
        bus.Paula.ScheduleWrite(400, 0x0A8, 64);
        bus.Paula.AdvanceTo(400);
        events.Add(CreatePaulaEvent(
            400,
            AgnusOfflinePaulaEventKind.RegisterWrite,
            bus.Paula.GetChannelSnapshot(0),
            bus.Paula.Intreq));
        bus.Paula.ScheduleWrite(600, 0x096, 0x0001);
        bus.Paula.AdvanceTo(600);
        events.Add(CreatePaulaEvent(
            600,
            AgnusOfflinePaulaEventKind.DmaDisabled,
            bus.Paula.GetChannelSnapshot(0),
            bus.Paula.Intreq));
        bus.Paula.ScheduleWrite(604, 0x09C, 0x0080);
        bus.Paula.ScheduleWrite(604, 0x0AA, 0x7F81);
        bus.Paula.AdvanceTo(604);
        events.Add(CreatePaulaEvent(
            604,
            AgnusOfflinePaulaEventKind.ManualData,
            bus.Paula.GetChannelSnapshot(0),
            bus.Paula.Intreq));
        bus.Paula.AdvanceTo(1800);
        events.Add(CreatePaulaEvent(
            1800,
            AgnusOfflinePaulaEventKind.LengthReloaded,
            bus.Paula.GetChannelSnapshot(1),
            bus.Paula.Intreq));
        foreach (var interruptEvent in bus.Paula.DrainInterrupts())
        {
            events.Add(CreatePaulaEvent(
                interruptEvent.Cycle,
                AgnusOfflinePaulaEventKind.Interrupt,
                bus.Paula.GetChannelSnapshot(interruptEvent.Channel),
                bus.Paula.Intreq));
        }

        events.Sort((left, right) => left.Cycle.CompareTo(right.Cycle));
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 1024);
        var accesses = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Paula &&
                access.Request.Kind == AmigaBusAccessKind.PaulaDma &&
                access.GrantedCycle <= SlotCycle(1023))
            .OrderBy(access => access.GrantedCycle)
            .ToArray();
        Assert.NotEmpty(accesses);
        Assert.Equal(4, accesses.Select(access => access.Request.Channel).Distinct().Count());
        foreach (var access in accesses)
        {
            fixture.AddPaulaWord(
                access.Request.Channel,
                access.Request.Address,
                access.RequestedCycle);
        }

        foreach (var record in events.Where(record => record.Cycle <= SlotCycle(1023)))
        {
            fixture.AddPaulaEvent(record);
        }

        var trace = fixture.CaptureExpected(initialMemory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])initialMemory.Clone());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(accesses.Length, kernel.PaulaDmaWordCount);
        Assert.Equal(events.Count, kernel.PaulaEventCount);
    }

    [Fact]
    public void OfflineDiskReadDmaCommitsChronologicalWordsAndMemoryWrites()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 128);
        fixture.AddDiskWord(0x7200, 0x1111, writeMode: false, SlotCycle(0x08));
        fixture.AddDiskWord(0x7202, 0x2222, writeMode: false, SlotCycle(0x09));
        fixture.AddDiskWord(0x7204, 0x3333, writeMode: false, SlotCycle(0x0B));
        var memory = CreateMemory();
        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        var replayMemory = (byte[])memory.Clone();
        kernel.Load(trace, replayMemory);

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(3, kernel.DiskDmaWordCount);
        Assert.Equal((ushort)0x1111, ReadWord(replayMemory, 0x7200));
        Assert.Equal((ushort)0x3333, ReadWord(replayMemory, 0x7204));
        for (var index = 0; index < 3; index++)
        {
            var word = kernel.GetDiskDmaWord(index);
            Assert.False(word.WriteMode);
            Assert.Equal(AgnusChipSlotOwner.Disk,
                kernel.GetCommittedSlot(SlotIndex(word.GrantedCycle)).Owner);
            Assert.True(AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(
                AgnusChipSlotOwner.Disk,
                word.GrantedCycle));
        }
    }

    [Fact]
    public void OfflineDiskWriteDmaSamplesChipRamAtGrantedSlot()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 128);
        var memory = CreateMemory();
        WriteWord(memory, 0x7400, 0x5AA5);
        fixture.AddDiskWord(0x7400, 0x5AA5, writeMode: true, SlotCycle(0x08));
        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        var replayMemory = (byte[])memory.Clone();
        kernel.Load(trace, replayMemory);
        WriteWord(replayMemory, 0x7400, 0xBEEF);

        var comparison = kernel.Replay();

        Assert.False(comparison.Equal);
        Assert.Equal(SlotIndex(kernel.GetDiskDmaWord(0).GrantedCycle), comparison.MismatchIndex);
        Assert.Equal((ushort)0xBEEF, kernel.GetDiskDmaWord(0).Value);
        Assert.True(kernel.GetDiskDmaWord(0).WriteMode);
        Assert.True(comparison.Actual.IsWrite);
    }

    [Fact]
    public void OfflineDiskPublishesNormalizedControlSyncFifoAndInterruptEvents()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 128);
        var kinds = new[]
        {
            AgnusOfflineDiskEventKind.RegisterWrite,
            AgnusOfflineDiskEventKind.DmaStarted,
            AgnusOfflineDiskEventKind.SyncMatch,
            AgnusOfflineDiskEventKind.DmaWord,
            AgnusOfflineDiskEventKind.SyncMissing,
            AgnusOfflineDiskEventKind.DmaCancelled,
            AgnusOfflineDiskEventKind.DmaStopped,
            AgnusOfflineDiskEventKind.DmaCompleted,
            AgnusOfflineDiskEventKind.Interrupt
        };
        for (var index = 0; index < kinds.Length; index++)
        {
            fixture.AddDiskEvent(new AgnusOfflineDiskEventRecord(
                cycle: index * AgnusChipSlotScheduler.SlotCycles,
                kinds[index],
                diskPointer: (uint)(0x7600 + (index * 2)),
                dsklen: (ushort)(0x8008 - index),
                dsksync: 0x4489,
                adkcon: index >= 2 ? (ushort)0x0400 : (ushort)0,
                dskbytr: (ushort)(0x8000 | index),
                dskdatr: (ushort)(0x8100 + index),
                activeDma: index is >= 1 and < 7,
                writeMode: index == 6,
                requestedWords: 8,
                transferredWords: index,
                sourceBit: index * 16,
                shiftRegister: (ushort)(0x8100 + index),
                fifoWords: (byte)(index & 1),
                intreq: kinds[index] == AgnusOfflineDiskEventKind.Interrupt
                    ? AmigaConstants.IntreqDiskBlock
                    : (ushort)0));
        }

        var trace = fixture.CaptureExpected(CreateMemory());
        var kernel = CreateKernel(trace);
        kernel.Load(trace, CreateMemory());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(kinds.Length, kernel.DiskEventCount);
        for (var index = 0; index < kinds.Length; index++)
        {
            Assert.Equal(kinds[index], kernel.GetDiskEvent(index).Kind);
        }
    }

    [Fact]
    public void OfflineDiskReplayIsDeterministicBoundedAndAllocationFree()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        var memory = CreateMemory();
        var requestedCycle = SlotCycle(0x08);
        for (var index = 0; index < 8; index++)
        {
            var address = (uint)(0x7800 + (index * 2));
            var value = (ushort)(0x9000 + index);
            fixture.AddDiskWord(address, value, writeMode: false, requestedCycle);
            requestedCycle += index % 3 == 2
                ? AmigaConstants.A500PalCpuCyclesPerRasterLine - SlotCycle(0x04)
                : SlotCycle(0x02);
        }

        var trace = fixture.CaptureExpected(memory);
        var hash = trace.CaptureDeterministicHash();
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());
        AssertReplayEqual(kernel.Replay());
        kernel.Load(trace, (byte[])memory.Clone());
        var before = GC.GetAllocatedBytesForCurrentThread();
        var comparison = kernel.Replay();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        AssertReplayEqual(comparison);
        Assert.Equal(0, allocated);
        Assert.Equal(8, kernel.CaptureDiagnostics().DiskDmaWords);
        Assert.Equal(hash, trace.CaptureDeterministicHash());

        var bounded = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 128,
            maximumDiskDmaWords: 1);
        bounded.AddDiskDmaWord(
            0x1000,
            0x1234,
            writeMode: false,
            requestedCycle: SlotCycle(0x08),
            grantedCycle: SlotCycle(0x08),
            completedCycle: SlotCycle(0x09));
        Assert.Throws<InvalidOperationException>(() =>
            bounded.AddDiskDmaWord(
                0x1002,
                0x5678,
                writeMode: false,
                requestedCycle: SlotCycle(0x09),
                grantedCycle: SlotCycle(0x0A),
                completedCycle: SlotCycle(0x0B)));

        var boundedEvents = new AgnusOfflineReplayTrace(
            firstCycle: 0,
            slotCount: 8,
            maximumDiskEvents: 1);
        boundedEvents.AddDiskEvent(default);
        Assert.Throws<InvalidOperationException>(() =>
            boundedEvents.AddDiskEvent(default));
    }

    [Fact]
    public void OfflineDiskConsumesProductionReadWordSyncMissAndWriteTransitions()
    {
        AssertProductionDiskReplay(
            trackWords: [0x1111, 0x2222, 0x3333],
            transferWords: 3,
            wordSync: false,
            writeMode: false);
        AssertProductionDiskReplay(
            trackWords: [0x9999, 0x4489, 0xABCD, 0x1357],
            transferWords: 2,
            wordSync: true,
            writeMode: false);
        AssertProductionDiskReplay(
            trackWords: [0xAAAA, 0xBBBB],
            transferWords: 2,
            wordSync: false,
            writeMode: true);

        var missing = CreateSyntheticDiskBus(0x1111, 0x2222);
        var missingCycle = PrepareSyntheticDiskDma(missing);
        missing.WriteWord(0x00DFF09E, 0x8100, missingCycle);
        missing.WriteWord(0x00DFF09E, 0x8400, missingCycle);
        missing.Paula.AdvanceTo(missingCycle);
        StartSyntheticDiskDma(missing, 0x7C00, words: 1, missingCycle);
        var missed = Assert.Single(
            missing.Disk.CaptureDmaTrace()
                .Where(entry => entry.Kind == AmigaDiskDmaTraceKind.SyncMissing));
        var firstCycle = FloorToSlot(missed.Cycle);
        var fixture = new OfflineOracleFixture(firstCycle, slotCount: 64);
        fixture.AddDiskEvent(CreateDiskEvent(
            missed,
            AgnusOfflineDiskEventKind.SyncMissing,
            missing.Paula.Intreq));
        var trace = fixture.CaptureExpected((byte[])missing.ChipRam.Clone());
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])missing.ChipRam.Clone());
        AssertReplayEqual(kernel.Replay());
        Assert.Equal(0, kernel.DiskDmaWordCount);
        Assert.Equal(AgnusOfflineDiskEventKind.SyncMissing, kernel.GetDiskEvent(0).Kind);
    }

    [Fact]
    public void OfflineDiskContendsWithEveryAcceptedRequesterInPriorityOrder()
    {
        const uint copperList = 0x2400;
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        var memory = CreateMemory();
        WriteWord(memory, 0x7E00, 0xD15C);
        WriteWord(memory, 0x7E20, 0xA0D0);
        WriteWord(memory, 0x7E40, 0xB117);
        WriteCopperInstruction(memory, copperList, 0xFFFF, 0xFFFE);
        fixture.SetInitialFixed(
            SlotCycle(0x08),
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 2),
            0x7E20);
        fixture.AddDiskWord(0x7E00, 0xD15C, writeMode: false, SlotCycle(0x08));
        fixture.AddPaulaWord(channel: 0, address: 0x7E40, requestedCycle: 0);
        fixture.StartCopper(copperList, SlotCycle(0x12));
        fixture.AddCopperInstruction(
            copperList,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);
        fixture.SetBlitterOperation(new CapturedBlitterOperation(
            StartCycle: SlotCycle(0x11),
            Nasty: true,
            CompletionDelay: 0,
            Zero: false,
            SourceA: 0x7E22,
            SourceB: 0,
            SourceC: 0,
            DestinationD: 0,
            MicroOps:
            [
                new CapturedBlitterMicroOp(
                    AgnusOfflineBlitterMicroOpKind.AreaReadA,
                    0x7E20,
                    0xA0D0,
                    IsWrite: false,
                    RequestedCycle: SlotCycle(0x11),
                    DelayAfterPreviousCompletion: 0)
            ]));
        fixture.AddCpuRequest(new CpuWordRequest(
            0x7E60,
            SlotCycle(0x11),
            AmigaBusAccessKind.CpuDataRead,
            isWrite: false));
        var trace = fixture.CaptureExpected(memory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])memory.Clone());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(AgnusChipSlotOwner.Bitplane,
            kernel.GetCommittedSlot(SlotIndex(SlotCycle(0x08))).Owner);
        Assert.Equal(SlotCycle(0x0A), kernel.GetDiskDmaWord(0).GrantedCycle);
        Assert.Equal(SlotCycle(0x10), kernel.GetPaulaDmaWord(0).GrantedCycle);
        Assert.True(kernel.GetBlitterMicroOp(0).GrantedCycle >
            kernel.GetDiskDmaWord(0).GrantedCycle);
        Assert.True(kernel.GetCopperTransition(0).FirstGrantedCycle >
            kernel.GetDiskDmaWord(0).GrantedCycle);
        Assert.True(kernel.GetCpuResult(0).GrantedCycle >
            kernel.GetDiskDmaWord(0).GrantedCycle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfflineDiskReplaysProductionMidTransferControlStop(bool disableDmacon)
    {
        var bus = CreateSyntheticDiskBus(0x1111, 0x2222, 0x3333, 0x4444);
        var startCycle = PrepareSyntheticDiskDma(bus);
        bus.WriteWord(0x00DFF09E, 0x8100, startCycle);
        bus.Paula.AdvanceTo(startCycle);
        StartSyntheticDiskDma(bus, 0x7C00, words: 4, startCycle);
        var initialMemory = (byte[])bus.ChipRam.Clone();
        var predictedCompletion = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
        var cancelCycle = startCycle;
        while (cancelCycle < predictedCompletion &&
            !bus.BusAccesses.Any(access => access.Request.Kind == AmigaBusAccessKind.DiskDma))
        {
            cancelCycle += AgnusChipSlotScheduler.SlotCycles;
            bus.AdvanceDmaTo(cancelCycle);
        }

        if (disableDmacon)
        {
            bus.WriteWord(0x00DFF096, 0x0010, cancelCycle);
            bus.Paula.AdvanceTo(cancelCycle);
            cancelCycle += AgnusChipSlotScheduler.SlotCycles;
            bus.AdvanceDmaTo(cancelCycle);
        }
        else
        {
            bus.WriteWord(0x00DFF024, 0, cancelCycle);
        }

        var traceEntries = bus.Disk.CaptureDmaTrace();
        var started = Assert.Single(
            traceEntries.Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
        var stopKind = disableDmacon
            ? AmigaDiskDmaTraceKind.Stopped
            : AmigaDiskDmaTraceKind.Cancelled;
        var cancelled = Assert.Single(
            traceEntries.Where(entry => entry.Kind == stopKind));
        var accesses = bus.BusAccesses
            .Where(access => access.Request.Kind == AmigaBusAccessKind.DiskDma)
            .OrderBy(access => access.GrantedCycle)
            .ToArray();
        Assert.NotEmpty(accesses);
        Assert.True(accesses.Length < 4);
        var firstCycle = FloorToSlot(started.Cycle);
        var slotCount = checked((int)(
            ((FloorToSlot(cancelled.Cycle) - firstCycle) /
                AgnusChipSlotScheduler.SlotCycles) + 33));
        var fixture = new OfflineOracleFixture(firstCycle, slotCount);
        foreach (var access in accesses)
        {
            fixture.AddDiskWord(
                access.Request.Address,
                ReadWord(bus.ChipRam, access.Request.Address),
                writeMode: false,
                access.RequestedCycle);
        }

        fixture.AddDiskEvent(CreateDiskEvent(
            started,
            AgnusOfflineDiskEventKind.DmaStarted,
            intreq: 0));
        fixture.AddDiskEvent(CreateDiskEvent(
            cancelled,
            disableDmacon
                ? AgnusOfflineDiskEventKind.DmaStopped
                : AgnusOfflineDiskEventKind.DmaCancelled,
            bus.Paula.Intreq));
        var trace = fixture.CaptureExpected(initialMemory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])initialMemory.Clone());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(accesses.Length, kernel.DiskDmaWordCount);
        Assert.Equal(
            disableDmacon
                ? AgnusOfflineDiskEventKind.DmaStopped
                : AgnusOfflineDiskEventKind.DmaCancelled,
            kernel.GetDiskEvent(1).Kind);
        Assert.Equal(0, kernel.GetDiskEvent(1).Intreq & AmigaConstants.IntreqDiskBlock);
    }

    [Fact]
    public void OfflineDiskReplaysProductionSyncAcrossWordBoundary()
    {
        var bus = CreateSyntheticDiskBus(CreateBitSlippedSyntheticTrack());
        var startCycle = PrepareSyntheticDiskDma(bus);
        bus.WriteWord(0x00DFF09E, 0x8500, startCycle);
        bus.Paula.AdvanceTo(startCycle);
        StartSyntheticDiskDma(bus, 0x7D00, words: 4, startCycle);
        var initialMemory = (byte[])bus.ChipRam.Clone();
        var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
        bus.AdvanceDmaTo(completionCycle);
        bus.Paula.AdvanceTo(completionCycle);
        var accesses = bus.BusAccesses
            .Where(access => access.Request.Kind == AmigaBusAccessKind.DiskDma)
            .OrderBy(access => access.GrantedCycle)
            .ToArray();
        Assert.Equal(4, accesses.Length);
        Assert.Equal((ushort)0x1111, ReadWord(bus.ChipRam, 0x7D00));
        Assert.Equal((ushort)0x2222, ReadWord(bus.ChipRam, 0x7D02));
        Assert.Equal((ushort)0xAA24, ReadWord(bus.ChipRam, 0x7D04));
        Assert.Equal((ushort)0x3333, ReadWord(bus.ChipRam, 0x7D06));
        var dmaTrace = bus.Disk.CaptureDmaTrace();
        var started = Assert.Single(
            dmaTrace.Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
        var completed = Assert.Single(
            dmaTrace.Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Completed));
        var firstCycle = FloorToSlot(started.Cycle);
        var slotCount = checked((int)(
            ((FloorToSlot(completed.Cycle) - firstCycle) /
                AgnusChipSlotScheduler.SlotCycles) + 33));
        var fixture = new OfflineOracleFixture(firstCycle, slotCount);
        foreach (var access in accesses)
        {
            fixture.AddDiskWord(
                access.Request.Address,
                ReadWord(bus.ChipRam, access.Request.Address),
                writeMode: false,
                access.RequestedCycle);
        }

        fixture.AddDiskEvent(CreateDiskEvent(
            started,
            AgnusOfflineDiskEventKind.DmaStarted,
            intreq: 0));
        fixture.AddDiskEvent(new AgnusOfflineDiskEventRecord(
            accesses[2].GrantedCycle,
            AgnusOfflineDiskEventKind.SyncMatch,
            diskPointer: accesses[2].Request.Address,
            dsklen: 0x8002,
            started.Dsksync,
            started.Adkcon,
            dskbytr: 0x1000,
            dskdatr: 0x4489,
            activeDma: true,
            writeMode: false,
            requestedWords: 4,
            transferredWords: 2,
            sourceBit: started.SourceBit + 32,
            shiftRegister: 0x4489,
            fifoWords: 0,
            intreq: 0));
        fixture.AddDiskEvent(CreateDiskEvent(
            completed,
            AgnusOfflineDiskEventKind.DmaCompleted,
            bus.Paula.Intreq));
        var trace = fixture.CaptureExpected(initialMemory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])initialMemory.Clone());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(AgnusOfflineDiskEventKind.SyncMatch, kernel.GetDiskEvent(1).Kind);
    }

    private static OfflineOracleFixture CreateCopperMoveFixture(byte[] memory)
    {
        const uint copperList = 0x2400;
        WriteCopperInstruction(memory, copperList, 0x0180, 0x0123);
        WriteCopperInstruction(memory, copperList + 4, 0xFFFF, 0xFFFE);
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 96);
        fixture.StartCopper(copperList, SlotCycle(0x20));
        fixture.AddCopperInstruction(
            copperList,
            0x0180,
            0x0123,
            AgnusOfflineCopperAction.Move,
            commitMove: true,
            moveRegister: 0x180,
            moveValue: 0x0123);
        fixture.AddCopperInstruction(
            copperList + 4,
            0xFFFF,
            0xFFFE,
            AgnusOfflineCopperAction.End);
        return fixture;
    }

    private static OfflineOracleFixture CreatePointerFixture()
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, TestSlotCount);
        var firstFetch = SlotCycle(0x28);
        var secondFetch = SlotCycle(0x30);
        fixture.SetInitialFixed(
            firstFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 0),
            0x1000);
        fixture.SetInitialFixed(
            secondFetch,
            new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
            0x1002);
        fixture.AddControlMutation(
            SlotCycle(0x2C),
            register: 0x0E2,
            value: 0x2000,
            [
                new AgnusOfflineFixedSlotPatch(
                    secondFetch,
                    new FixedSlotPlanEntry(AgnusChipSlotOwner.Bitplane, channel: 0, phase: 1),
                    0x2000)
            ]);
        return fixture;
    }

    private static AgnusSlotKernel CreateKernel(AgnusOfflineReplayTrace trace)
        => new(
            AmigaChipset.OcsPal,
            maximumSlots: trace.SlotCount,
            maximumCpuRequests: Math.Max(2, trace.CpuRequestCount),
            maximumCopperInstructions: Math.Max(2, trace.CopperInstructionCount),
            maximumCopperMutations: Math.Max(2, trace.ExpectedCopperMutationCount),
            maximumBlitterMicroOps: Math.Max(2, trace.BlitterMicroOpCount),
            maximumPaulaDmaWords: Math.Max(2, trace.PaulaDmaWordCount),
            maximumPaulaEvents: Math.Max(2, trace.PaulaEventCount),
            maximumDiskDmaWords: Math.Max(2, trace.DiskDmaWordCount),
            maximumDiskEvents: Math.Max(2, trace.DiskEventCount));

    private static void AssertReplayEqual(AgnusOfflineReplayComparison comparison)
    {
        Assert.True(
            comparison.Equal,
            $"slot={comparison.MismatchIndex}; " +
            $"copperTransition={comparison.CopperTransitionMismatchIndex}; " +
            $"copperMutation={comparison.CopperMutationMismatchIndex}; " +
            $"blitterMicroOp={comparison.BlitterMicroOpMismatchIndex}; " +
            $"blitterCompletion={comparison.BlitterCompletionMismatch}; " +
            $"paulaWord={comparison.PaulaDmaWordMismatchIndex}; " +
            $"paulaEvent={comparison.PaulaEventMismatchIndex}; " +
            $"diskWord={comparison.DiskDmaWordMismatchIndex}; " +
            $"diskEvent={comparison.DiskEventMismatchIndex}; " +
            $"expected={Format(comparison.Expected)}; actual={Format(comparison.Actual)}; " +
            $"expectedCopper={Format(comparison.ExpectedCopperTransition)}; " +
            $"actualCopper={Format(comparison.ActualCopperTransition)}; " +
            $"expectedBlitter={Format(comparison.ExpectedBlitterMicroOp)}; " +
            $"actualBlitter={Format(comparison.ActualBlitterMicroOp)}");
    }

    private static string Format(AgnusCommittedSlotRecord record)
        => $"{record.Cycle}:{record.Owner}:{record.Requester}:{record.Kind}:" +
            $"0x{record.Address:X6}:0x{record.Value:X4}:" +
            $"req={record.RequestedCycle}:done={record.CompletedCycle}:" +
            $"grant={record.Granted}:address={record.AddressValid}:value={record.ValueValid}:" +
            $"write={record.IsWrite}:channel={record.Channel}:phase={record.Phase}";

    private static string Format(AgnusOfflineCopperTransitionRecord record)
        => $"{record.InstructionIndex}:pc=0x{record.Pc:X6}:{record.Action}:" +
            $"first={record.FirstRequestedCycle}/{record.FirstGrantedCycle}/0x{record.FirstWord:X4}:" +
            $"second={record.SecondRequestedCycle}/{record.SecondGrantedCycle}/0x{record.SecondWord:X4}:" +
            $"transition={record.TransitionCycle}:next=0x{record.NextPc:X6}/{record.NextRequestedCycle}:" +
            $"skip={record.MoveSuppressed}:mutation={record.MutationCommitted}:" +
            $"wait={record.Waiting}:stop={record.Stopped}:bfd={record.WaitForBlitter}";

    private static string Format(AgnusOfflineBlitterMicroOpRecord record)
        => $"{record.Index}:{record.Kind}:0x{record.Address:X6}:0x{record.Value:X4}:" +
            $"req={record.RequestedCycle}:grant={record.GrantedCycle}:" +
            $"done={record.CompletedCycle}:write={record.IsWrite}";

    private static AgnusOfflinePaulaEventRecord CreatePaulaEvent(
        long cycle,
        AgnusOfflinePaulaEventKind kind,
        PaulaChannelSnapshot snapshot,
        ushort intreq)
        => new(
            cycle,
            kind,
            snapshot.Index,
            snapshot.Location,
            snapshot.CurrentAddress,
            snapshot.LengthWords,
            snapshot.RemainingWords,
            snapshot.Period,
            snapshot.Volume,
            snapshot.CurrentSample,
            snapshot.DmaEnabled,
            snapshot.DataWord,
            (byte)snapshot.State,
            intreq);

    private static void AssertProductionDiskReplay(
        ushort[] trackWords,
        ushort transferWords,
        bool wordSync,
        bool writeMode)
    {
        const uint targetAddress = 0x7A00;
        var bus = CreateSyntheticDiskBus(trackWords);
        for (var index = 0; index < transferWords; index++)
        {
            WriteWord(bus.ChipRam, targetAddress + (uint)(index * 2),
                (ushort)(0xD000 + index));
        }

        var startCycle = PrepareSyntheticDiskDma(bus);
        bus.WriteWord(0x00DFF09E, 0x8100, startCycle);
        bus.Paula.AdvanceTo(startCycle);
        if (wordSync)
        {
            bus.WriteWord(0x00DFF07E, 0x4489, startCycle);
            bus.WriteWord(0x00DFF09E, 0x8400, startCycle);
            bus.Paula.AdvanceTo(startCycle);
        }

        StartSyntheticDiskDma(
            bus,
            targetAddress,
            transferWords,
            startCycle,
            writeMode);
        var initialMemory = (byte[])bus.ChipRam.Clone();
        var snapshot = bus.Disk.CaptureSnapshot();
        Assert.True(snapshot.ActiveDma);
        bus.AdvanceDmaTo(snapshot.ActiveDmaCompletionCycle);
        bus.Paula.AdvanceTo(snapshot.ActiveDmaCompletionCycle);
        var accesses = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Disk &&
                access.Request.Kind == AmigaBusAccessKind.DiskDma)
            .OrderBy(access => access.GrantedCycle)
            .ToArray();
        Assert.Equal(transferWords, accesses.Length);

        var dmaTrace = bus.Disk.CaptureDmaTrace();
        var started = Assert.Single(
            dmaTrace.Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
        var completed = Assert.Single(
            dmaTrace.Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Completed));
        var firstCycle = FloorToSlot(Math.Min(started.Cycle, accesses[0].GrantedCycle));
        var lastCycle = Math.Max(completed.Cycle, accesses[^1].CompletedCycle);
        var slotCount = checked((int)(
            ((FloorToSlot(lastCycle) - firstCycle) /
                AgnusChipSlotScheduler.SlotCycles) + 33));
        var fixture = new OfflineOracleFixture(firstCycle, slotCount);
        for (var index = 0; index < accesses.Length; index++)
        {
            var access = accesses[index];
            var value = writeMode
                ? ReadWord(initialMemory, access.Request.Address)
                : ReadWord(bus.ChipRam, access.Request.Address);
            fixture.AddDiskWord(
                access.Request.Address,
                value,
                writeMode,
                access.RequestedCycle);
        }

        fixture.AddDiskEvent(CreateDiskEvent(
            started,
            AgnusOfflineDiskEventKind.DmaStarted,
            intreq: 0));
        for (var index = 0; index < accesses.Length; index++)
        {
            var access = accesses[index];
            var value = writeMode
                ? ReadWord(initialMemory, access.Request.Address)
                : ReadWord(bus.ChipRam, access.Request.Address);
            fixture.AddDiskEvent(new AgnusOfflineDiskEventRecord(
                access.GrantedCycle,
                AgnusOfflineDiskEventKind.DmaWord,
                diskPointer: access.Request.Address + 2,
                dsklen: (ushort)(
                    0x8000 |
                    (writeMode ? 0x4000 : 0) |
                    (transferWords - index - 1)),
                started.Dsksync,
                started.Adkcon,
                dskbytr: 0,
                dskdatr: value,
                activeDma: true,
                writeMode,
                requestedWords: transferWords,
                transferredWords: index + 1,
                sourceBit: started.SourceBit + (index * 16),
                shiftRegister: value,
                fifoWords: 0,
                intreq: 0));
        }

        fixture.AddDiskEvent(CreateDiskEvent(
            completed,
            AgnusOfflineDiskEventKind.DmaCompleted,
            bus.Paula.Intreq));
        fixture.AddDiskEvent(CreateDiskEvent(
            completed,
            AgnusOfflineDiskEventKind.Interrupt,
            bus.Paula.Intreq));
        var trace = fixture.CaptureExpected(initialMemory);
        var kernel = CreateKernel(trace);
        kernel.Load(trace, (byte[])initialMemory.Clone());

        AssertReplayEqual(kernel.Replay());
        Assert.Equal(accesses.Length, kernel.DiskDmaWordCount);
        Assert.Equal(wordSync, started.WordSyncEnabled);
        Assert.Equal(writeMode, accesses.All(access => access.Request.IsWrite));
        Assert.NotEqual(0, kernel.GetDiskEvent(kernel.DiskEventCount - 1).Intreq &
            AmigaConstants.IntreqDiskBlock);
    }

    private static AgnusOfflineDiskEventRecord CreateDiskEvent(
        in AmigaDiskDmaTraceEntry entry,
        AgnusOfflineDiskEventKind kind,
        ushort intreq)
        => new(
            entry.Cycle,
            kind,
            diskPointer: entry.TargetAddress + (uint)(entry.TransferredWords * 2),
            entry.Dsklen,
            entry.Dsksync,
            entry.Adkcon,
            entry.Dskbytr,
            entry.Dskdatr,
            activeDma: kind == AgnusOfflineDiskEventKind.DmaStarted,
            writeMode: (entry.Dsklen & 0x4000) != 0,
            entry.RequestedWords,
            entry.TransferredWords,
            entry.SourceBit,
            shiftRegister: entry.Dskdatr,
            fifoWords: 0,
            intreq);

    private static AmigaBus CreateSyntheticDiskBus(params ushort[] words)
    {
        var data = new byte[Math.Max(1, words.Length) * 2];
        for (var index = 0; index < words.Length; index++)
        {
            WriteWord(data, (uint)(index * 2), words[index]);
        }

        return CreateSyntheticDiskBus(AmigaEncodedTrack.FromBytes(data));
    }

    private static AmigaBus CreateSyntheticDiskBus(AmigaEncodedTrack track)
    {
        var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
        var blank = AmigaEncodedTrack.FromBytes([0xAA, 0xAA]);
        Array.Fill(tracks, blank);
        tracks[0] = track;
        var bus = new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: false);
        bus.Disk.Drive0.Insert(
            AmigaDiskImage.FromEncodedTracks(tracks),
            writeProtected: false);
        return bus;
    }

    private static AmigaEncodedTrack CreateBitSlippedSyntheticTrack()
    {
        const int noiseBits = 5;
        var bitLength = (6 * 16) + noiseBits;
        var data = new byte[(bitLength + 7) / 8];
        var bitOffset = 0;
        WriteSyntheticBits(data, ref bitOffset, 0x4489, 16);
        WriteSyntheticBits(data, ref bitOffset, 0x1111, 16);
        WriteSyntheticBits(data, ref bitOffset, 0x2222, 16);
        WriteSyntheticBits(data, ref bitOffset, 0b10101, noiseBits);
        WriteSyntheticBits(data, ref bitOffset, 0x4489, 16);
        WriteSyntheticBits(data, ref bitOffset, 0x3333, 16);
        WriteSyntheticBits(data, ref bitOffset, 0x4444, 16);
        return new AmigaEncodedTrack(data, bitLength);
    }

    private static void WriteSyntheticBits(
        Span<byte> data,
        ref int bitOffset,
        uint value,
        int bitCount)
    {
        for (var bit = bitCount - 1; bit >= 0; bit--)
        {
            if (((value >> bit) & 1) != 0)
            {
                data[bitOffset >> 3] |= (byte)(1 << (7 - (bitOffset & 7)));
            }

            bitOffset++;
        }
    }

    private static long PrepareSyntheticDiskDma(AmigaBus bus)
    {
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        var ciaCycle = AgnusChipSlotScheduler.AlignToSlot(
            AmigaConstants.A500PalCpuCyclesPerCiaTick);
        var readyCycle = AgnusChipSlotScheduler.AlignToSlot(
            ciaCycle +
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
        bus.AdvanceDmaTo(readyCycle);
        bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
        bus.Paula.AdvanceTo(readyCycle);
        return readyCycle;
    }

    private static void StartSyntheticDiskDma(
        AmigaBus bus,
        uint address,
        ushort words,
        long cycle,
        bool writeMode = false)
    {
        bus.WriteWord(0x00DFF020, (ushort)(address >> 16), cycle);
        bus.WriteWord(0x00DFF022, (ushort)address, cycle);
        var dsklen = (ushort)(0x8000 | (writeMode ? 0x4000 : 0) | words);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
    }

    private static long FloorToSlot(long cycle)
        => cycle - (cycle % AgnusChipSlotScheduler.SlotCycles);

    private static byte[] CreateMemory()
        => new byte[ChipRamSize];

    private static long SlotCycle(int horizontal)
        => (long)horizontal * AgnusChipSlotScheduler.SlotCycles;

    private static int SlotIndex(long slotCycle)
        => checked((int)(slotCycle / AgnusChipSlotScheduler.SlotCycles));

    private static ushort ReadWord(byte[] memory, uint address)
    {
        var mask = memory.Length - 1;
        var first = (int)(address & (uint)mask);
        var second = (int)((address + 1) & (uint)mask);
        return (ushort)((memory[first] << 8) | memory[second]);
    }

    private static void WriteWord(byte[] memory, uint address, ushort value)
    {
        var mask = memory.Length - 1;
        var first = (int)(address & (uint)mask);
        var second = (int)((address + 1) & (uint)mask);
        memory[first] = (byte)(value >> 8);
        memory[second] = (byte)value;
    }

    private static void WriteCopperInstruction(
        byte[] memory,
        uint address,
        ushort first,
        ushort second)
    {
        WriteWord(memory, address, first);
        WriteWord(memory, address + 2, second);
    }

    private static CapturedBlitterRun CaptureBlitter(
        Action<AmigaBus> configure,
        ushort bltSize,
        Func<AmigaBusAccessResult, AgnusOfflineBlitterMicroOpKind> classify,
        bool nasty = false,
        long startWriteCycle = 0x80,
        Action<AmigaBus>? afterStart = null)
    {
        var bus = new AmigaBus(captureBusAccesses: true);
        bus.CausalBusExecutor.SetRecentCommittedSlotsEnabled(true);
        configure(bus);
        bus.WriteWord(
            0x00DFF096,
            (ushort)(0x8240 | (nasty ? 0x0400 : 0)));
        bus.AdvanceDmaTo(0);
        var initialMemory = (byte[])bus.ChipRam.Clone();
        bus.Blitter.WriteRegister(0x058, bltSize, startWriteCycle);
        var startSnapshot = bus.Blitter.CaptureSnapshot();
        afterStart?.Invoke(bus);
        bus.AdvanceDmaTo(4096);
        var finalSnapshot = bus.Blitter.CaptureSnapshot();
        Assert.False(finalSnapshot.Busy);
        Assert.NotEqual(0, bus.Paula.Intreq & AmigaConstants.IntreqBlitter);

        var accesses = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Blitter &&
                access.Request.Kind == AmigaBusAccessKind.Blitter)
            .ToArray();
        var diagnostics = new List<AgnusCommittedSlotDiagnostic>();
        for (var newestOffset = bus.CausalBusExecutor.RecentCommittedSlotCount - 1;
            newestOffset >= 0;
            newestOffset--)
        {
            Assert.True(
                bus.CausalBusExecutor.TryGetRecentCommittedSlot(
                    newestOffset,
                    out var diagnostic));
            if (diagnostic.Requester == AmigaBusRequester.Blitter)
            {
                diagnostics.Add(diagnostic);
            }
        }

        Assert.Equal(accesses.Length, diagnostics.Count);
        var microOps = new CapturedBlitterMicroOp[accesses.Length];
        for (var index = 0; index < accesses.Length; index++)
        {
            var access = accesses[index];
            var diagnostic = diagnostics[index];
            Assert.Equal(access.GrantedCycle, diagnostic.Cycle);
            Assert.Equal(access.Request.Address, diagnostic.Address);
            Assert.Equal(access.Request.IsWrite, diagnostic.IsWrite);
            Assert.True(diagnostic.ValueValid);
            var delay = index == 0
                ? access.RequestedCycle - startSnapshot.CurrentCycle
                : access.RequestedCycle - accesses[index - 1].CompletedCycle;
            Assert.True(delay >= 0);
            microOps[index] = new CapturedBlitterMicroOp(
                classify(access),
                diagnostic.Address,
                diagnostic.Value,
                diagnostic.IsWrite,
                access.RequestedCycle,
                delay);
        }

        var completionBase = accesses.Length == 0
            ? startWriteCycle
            : accesses[^1].CompletedCycle;
        var operation = new CapturedBlitterOperation(
            startWriteCycle,
            nasty,
            bus.Blitter.LastCompletionCycle - completionBase,
            finalSnapshot.Zero,
            finalSnapshot.SourceA,
            finalSnapshot.SourceB,
            finalSnapshot.SourceC,
            finalSnapshot.DestinationD,
            microOps);
        return new CapturedBlitterRun(
            operation,
            initialMemory,
            (byte[])bus.ChipRam.Clone());
    }

    private static (
        AgnusSlotKernel Kernel,
        AgnusOfflineReplayTrace Trace,
        byte[] Memory) ReplayBlitter(
            CapturedBlitterRun run,
            Action<OfflineOracleFixture>? configureFixture = null)
    {
        var fixture = new OfflineOracleFixture(firstCycle: 0, slotCount: 512);
        fixture.SetBlitterOperation(run.Operation);
        configureFixture?.Invoke(fixture);
        var trace = fixture.CaptureExpected(run.InitialMemory);
        var kernel = CreateKernel(trace);
        var memory = (byte[])run.InitialMemory.Clone();
        kernel.Load(trace, memory);
        AssertReplayEqual(kernel.Replay());
        return (kernel, trace, memory);
    }

    private static void ConfigureAreaBlitter(
        AmigaBus bus,
        ushort bltcon0,
        ushort bltcon1 = 0,
        uint sourceA = 0x3000,
        uint sourceB = 0x3200,
        uint sourceC = 0x3400,
        uint destinationD = 0x4000)
    {
        bus.WriteWord(0x00DFF040, bltcon0);
        bus.WriteWord(0x00DFF042, bltcon1);
        WritePointer(bus, 0x00DFF050, sourceA);
        WritePointer(bus, 0x00DFF04C, sourceB);
        WritePointer(bus, 0x00DFF048, sourceC);
        WritePointer(bus, 0x00DFF054, destinationD);
    }

    private static void WritePointer(AmigaBus bus, uint highRegister, uint pointer)
    {
        bus.WriteWord(highRegister, (ushort)(pointer >> 16));
        bus.WriteWord(highRegister + 2, (ushort)pointer);
    }

    private static AgnusOfflineBlitterMicroOpKind ClassifyAreaBlitterAccess(
        AmigaBusAccessResult access)
    {
        if (access.Request.IsWrite)
        {
            return AgnusOfflineBlitterMicroOpKind.AreaWriteD;
        }

        return access.Request.Address switch
        {
            >= 0x3000 and < 0x3200 => AgnusOfflineBlitterMicroOpKind.AreaReadA,
            >= 0x3200 and < 0x3400 => AgnusOfflineBlitterMicroOpKind.AreaReadB,
            _ => AgnusOfflineBlitterMicroOpKind.AreaReadC
        };
    }

    private sealed record CapturedBlitterRun(
        CapturedBlitterOperation Operation,
        byte[] InitialMemory,
        byte[] FinalMemory);

    private sealed record CapturedBlitterOperation(
        long StartCycle,
        bool Nasty,
        long CompletionDelay,
        bool Zero,
        uint SourceA,
        uint SourceB,
        uint SourceC,
        uint DestinationD,
        CapturedBlitterMicroOp[] MicroOps);

    private readonly record struct CapturedBlitterMicroOp(
        AgnusOfflineBlitterMicroOpKind Kind,
        uint Address,
        ushort Value,
        bool IsWrite,
        long RequestedCycle,
        long DelayAfterPreviousCompletion);

    private sealed class OfflineOracleFixture
    {
        private const int MaximumCpuRequests = 32;
        private const int MaximumCopperInstructions = 32;
        private const int MaximumCopperPatches = 128;
        private const int MaximumBlitterMicroOps = 256;
        private const int MaximumPaulaWords = 128;
        private const int MaximumDiskWords = 128;
        private readonly AgnusOfflineReplayTrace _trace;
        private readonly FixedSlotPlanEntry[] _finalPlan;
        private readonly uint[] _finalAddresses;
        private readonly CpuWordRequest[] _cpuRequests = new CpuWordRequest[MaximumCpuRequests];
        private readonly CopperOracleInstruction[] _copperInstructions =
            new CopperOracleInstruction[MaximumCopperInstructions];
        private readonly AgnusOfflineFixedSlotPatch[] _copperPatches =
            new AgnusOfflineFixedSlotPatch[MaximumCopperPatches];
        private readonly PaulaOracleWord[] _paulaWords =
            new PaulaOracleWord[MaximumPaulaWords];
        private readonly DiskOracleWord[] _diskWords =
            new DiskOracleWord[MaximumDiskWords];
        private CapturedBlitterOperation? _blitterOperation;
        private int _cpuRequestCount;
        private int _copperInstructionCount;
        private int _copperPatchCount;
        private int _paulaWordCount;
        private int _diskWordCount;
        private uint _copperStartPc;
        private long _copperStartCycle;
        private bool _copperStarted;

        public OfflineOracleFixture(long firstCycle, int slotCount)
        {
            _trace = new AgnusOfflineReplayTrace(
                firstCycle,
                slotCount,
                maximumCpuRequests: MaximumCpuRequests,
                maximumFixedPatches:
                    AgnusOfflineReplayTrace.DefaultMaximumFixedPatches +
                    MaximumCopperPatches,
                maximumCopperInstructions: MaximumCopperInstructions,
                maximumCopperMutations: MaximumCopperInstructions,
                maximumBlitterMicroOps: MaximumBlitterMicroOps,
                maximumPaulaDmaWords: MaximumPaulaWords,
                maximumPaulaEvents: 256,
                maximumDiskDmaWords: MaximumDiskWords,
                maximumDiskEvents: 256);
            _finalPlan = new FixedSlotPlanEntry[slotCount];
            _finalAddresses = new uint[slotCount];
        }

        public void SetInitialFixed(
            long slotCycle,
            in FixedSlotPlanEntry entry,
            uint address)
        {
            var index = _trace.GetSlotIndex(slotCycle);
            _trace.SetInitialFixedSlot(slotCycle, entry, address);
            _finalPlan[index] = entry;
            _finalAddresses[index] = address;
        }

        public void AddControlMutation(
            long cycle,
            ushort register,
            ushort value,
            ReadOnlySpan<AgnusOfflineFixedSlotPatch> patches)
        {
            _trace.AddControlMutation(cycle, register, value, patches);
            for (var index = 0; index < patches.Length; index++)
            {
                var patch = patches[index];
                var slotIndex = _trace.GetSlotIndex(patch.SlotCycle);
                _finalPlan[slotIndex] = patch.Entry;
                _finalAddresses[slotIndex] = patch.Address;
            }
        }

        public void AddCpuRequest(in CpuWordRequest request)
        {
            if (_cpuRequestCount == _cpuRequests.Length)
            {
                throw new InvalidOperationException("The focused oracle CPU-request buffer is full.");
            }

            _trace.AddCpuRequest(request);
            _cpuRequests[_cpuRequestCount++] = request;
        }

        public void SetBlitterOperation(CapturedBlitterOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (_blitterOperation is not null)
            {
                throw new InvalidOperationException(
                    "The focused oracle already has a normalized blitter operation.");
            }

            if (operation.MicroOps.Length > MaximumBlitterMicroOps)
            {
                throw new InvalidOperationException(
                    "The focused oracle blitter micro-operation buffer is full.");
            }

            _blitterOperation = operation;
        }

        public void AddPaulaWord(int channel, uint address, long requestedCycle)
        {
            if (_paulaWordCount == _paulaWords.Length)
            {
                throw new InvalidOperationException(
                    "The focused oracle Paula-word buffer is full.");
            }

            _paulaWords[_paulaWordCount++] =
                new PaulaOracleWord(channel, address, requestedCycle);
        }

        public void AddPaulaEvent(in AgnusOfflinePaulaEventRecord record)
            => _trace.AddPaulaEvent(record);

        public void AddDiskWord(
            uint address,
            ushort value,
            bool writeMode,
            long requestedCycle)
        {
            if (_diskWordCount == _diskWords.Length)
            {
                throw new InvalidOperationException(
                    "The focused oracle disk-word buffer is full.");
            }

            _diskWords[_diskWordCount++] =
                new DiskOracleWord(address, value, writeMode, requestedCycle);
        }

        public void AddDiskEvent(in AgnusOfflineDiskEventRecord record)
            => _trace.AddDiskEvent(record);

        public void StartCopper(uint pc, long requestedCycle)
        {
            if (_copperStarted)
            {
                throw new InvalidOperationException("The focused Copper oracle is already started.");
            }

            _copperStarted = true;
            _copperStartPc = pc;
            _copperStartCycle = requestedCycle;
        }

        public void AddCopperInstruction(
            uint pc,
            ushort firstWord,
            ushort secondWord,
            AgnusOfflineCopperAction action,
            long waitResumeCycle = -1,
            uint nextPc = 0,
            bool comparisonSatisfied = false,
            bool waitForBlitter = false,
            bool commitMove = false,
            ushort moveRegister = 0,
            ushort moveValue = 0,
            ReadOnlySpan<AgnusOfflineFixedSlotPatch> patches = default)
        {
            if (!_copperStarted)
            {
                throw new InvalidOperationException("Start the focused Copper oracle first.");
            }

            if (_copperInstructionCount == _copperInstructions.Length)
            {
                throw new InvalidOperationException(
                    "The focused oracle Copper-instruction buffer is full.");
            }

            if (patches.Length > _copperPatches.Length - _copperPatchCount)
            {
                throw new InvalidOperationException(
                    "The focused oracle Copper-patch buffer is full.");
            }

            if (action == AgnusOfflineCopperAction.Wait && waitResumeCycle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(waitResumeCycle));
            }

            if (action != AgnusOfflineCopperAction.Copjmp &&
                action != AgnusOfflineCopperAction.End &&
                nextPc == 0)
            {
                nextPc = pc + 4;
            }

            var patchStart = _copperPatchCount;
            for (var index = 0; index < patches.Length; index++)
            {
                var patch = patches[index];
                _copperPatches[_copperPatchCount++] = patch;
                var slotIndex = _trace.GetSlotIndex(patch.SlotCycle);
                _finalPlan[slotIndex] = patch.Entry;
                _finalAddresses[slotIndex] = patch.Address;
            }

            _copperInstructions[_copperInstructionCount++] =
                new CopperOracleInstruction(
                    pc,
                    firstWord,
                    secondWord,
                    action,
                    waitResumeCycle,
                    nextPc,
                    comparisonSatisfied,
                    waitForBlitter,
                    commitMove,
                    moveRegister,
                    moveValue,
                    patchStart,
                    patches.Length);
        }

        public AgnusOfflineReplayTrace CaptureExpected(byte[] initialMemory)
        {
            var engine = new AgnusHrmSlotEngine(captureSlotDebug: true);
            for (var index = 0; index < _trace.SlotCount; index++)
            {
                var entry = _finalPlan[index];
                if (!entry.Occupied)
                {
                    continue;
                }

                var cycle = _trace.FirstCycle +
                    ((long)index * AgnusChipSlotScheduler.SlotCycles);
                var address = _finalAddresses[index];
                if (entry.Owner == AgnusChipSlotOwner.Bitplane)
                {
                    Assert.True(engine.TryCommitPlannedBitplaneSlot(address, cycle, out _));
                }
                else
                {
                    var request = new AmigaBusAccessRequest(
                        AmigaBusRequester.Sprite,
                        AmigaBusAccessKind.Sprite,
                        AmigaBusAccessTarget.ChipRam,
                        address,
                        AmigaBusAccessSize.Word,
                        cycle,
                        isWrite: false,
                        entry.Channel);
                    Assert.True(
                        engine.TryReserveExactFixedDmaSlot(request, out _),
                        $"owner={entry.Owner}; cycle={cycle}; horizontal=" +
                        $"{AgnusHrmOcsSlotTable.GetHorizontal(cycle)}");
                }
            }

            var diskGrants = new OracleDiskGrant[_diskWordCount];
            for (var index = 0; index < _diskWordCount; index++)
            {
                var word = _diskWords[index];
                var candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(
                    word.RequestedCycle,
                    AgnusChipSlotOwner.Disk);
                AmigaBusAccessResult access;
                while (true)
                {
                    var request = new AmigaBusAccessRequest(
                        AmigaBusRequester.Disk,
                        AmigaBusAccessKind.DiskDma,
                        AmigaBusAccessTarget.ChipRam,
                        word.Address,
                        AmigaBusAccessSize.Word,
                        candidate,
                        word.WriteMode);
                    if (engine.TryReserveExactFixedDmaSlot(request, out access))
                    {
                        break;
                    }

                    candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(
                        candidate + AgnusChipSlotScheduler.SlotCycles,
                        AgnusChipSlotOwner.Disk);
                }

                _trace.AddDiskDmaWord(
                    word.Address,
                    word.Value,
                    word.WriteMode,
                    word.RequestedCycle,
                    access.GrantedCycle,
                    access.CompletedCycle);
                diskGrants[index] = new OracleDiskGrant(word, access);
            }

            var copperGrants = new OracleCopperGrant[_copperInstructionCount * 2];
            var copperGrantCount = 0;
            if (_copperStarted)
            {
                var pc = _copperStartPc;
                var firstRequestedCycle = _copperStartCycle;
                for (var instructionIndex = 0;
                    instructionIndex < _copperInstructionCount;
                    instructionIndex++)
                {
                    var instruction = _copperInstructions[instructionIndex];
                    Assert.Equal(pc, instruction.Pc);
                    var firstAccess = engine.ReserveCopperDmaSlot(
                        pc,
                        firstRequestedCycle);
                    var secondRequestedCycle = Math.Max(
                        firstAccess.CompletedCycle,
                        firstRequestedCycle +
                            (2L * AgnusChipSlotScheduler.SlotCycles));
                    var secondAddress = pc + 2;
                    var secondAccess = engine.ReserveCopperDmaSlot(
                        secondAddress,
                        secondRequestedCycle);
                    var moveStopCycle = Math.Max(
                        secondAccess.CompletedCycle,
                        firstRequestedCycle +
                            (4L * AgnusChipSlotScheduler.SlotCycles));
                    var controlStopCycle = Math.Max(
                        secondAccess.CompletedCycle,
                        firstRequestedCycle +
                            (6L * AgnusChipSlotScheduler.SlotCycles));
                    var transitionCycle = instruction.Action switch
                    {
                        AgnusOfflineCopperAction.Move => secondAccess.GrantedCycle,
                        AgnusOfflineCopperAction.Copjmp => secondAccess.GrantedCycle,
                        AgnusOfflineCopperAction.Wait => controlStopCycle,
                        AgnusOfflineCopperAction.Skip => controlStopCycle,
                        _ => moveStopCycle
                    };
                    var nextRequestedCycle = instruction.Action switch
                    {
                        AgnusOfflineCopperAction.Move => moveStopCycle,
                        AgnusOfflineCopperAction.Copjmp => moveStopCycle,
                        AgnusOfflineCopperAction.Skip => controlStopCycle,
                        AgnusOfflineCopperAction.Wait => instruction.WaitResumeCycle,
                        _ => -1
                    };
                    var nextPc = instruction.Action == AgnusOfflineCopperAction.End
                        ? 0
                        : instruction.NextPc;
                    _trace.AddCopperInstruction(
                        pc,
                        firstRequestedCycle,
                        firstAccess.GrantedCycle,
                        secondRequestedCycle,
                        secondAccess.GrantedCycle,
                        instruction.FirstWord,
                        instruction.SecondWord,
                        instruction.Action,
                        transitionCycle,
                        nextPc,
                        nextRequestedCycle,
                        instruction.ComparisonSatisfied,
                        instruction.WaitForBlitter,
                        instruction.CommitMove,
                        instruction.MoveRegister,
                        instruction.MoveValue,
                        preserveSecondWordPhysicalPhase: false,
                        patches: _copperPatches.AsSpan(
                            instruction.PatchStart,
                            instruction.PatchCount));
                    copperGrants[copperGrantCount++] = new OracleCopperGrant(
                        instructionIndex,
                        0,
                        pc,
                        instruction.FirstWord,
                        firstAccess);
                    copperGrants[copperGrantCount++] = new OracleCopperGrant(
                        instructionIndex,
                        1,
                        secondAddress,
                        instruction.SecondWord,
                        secondAccess);
                    pc = nextPc;
                    firstRequestedCycle = nextRequestedCycle;
                }
            }

            var blitterGrants = Array.Empty<OracleBlitterGrant>();
            if (_blitterOperation is not null)
            {
                var operation = _blitterOperation;
                engine.BlitterPriorityEnabled = operation.Nasty;
                _trace.StartBlitterReplay(operation.StartCycle, operation.Nasty);
                blitterGrants = new OracleBlitterGrant[operation.MicroOps.Length];
                var requestedCycle = operation.MicroOps.Length > 0
                    ? operation.MicroOps[0].RequestedCycle
                    : operation.StartCycle;
                for (var index = 0; index < operation.MicroOps.Length; index++)
                {
                    var microOp = operation.MicroOps[index];
                    if (index > 0)
                    {
                        requestedCycle =
                            blitterGrants[index - 1].Access.CompletedCycle +
                            microOp.DelayAfterPreviousCompletion;
                    }

                    var access = engine.ReserveBlitterDmaWordSlot(
                        microOp.Address,
                        requestedCycle,
                        microOp.IsWrite);
                    _trace.AddBlitterMicroOp(
                        microOp.Kind,
                        microOp.Address,
                        microOp.Value,
                        microOp.IsWrite,
                        requestedCycle,
                        access.GrantedCycle,
                        access.CompletedCycle);
                    blitterGrants[index] = new OracleBlitterGrant(microOp, access);
                }

                var completionBase = operation.MicroOps.Length == 0
                    ? operation.StartCycle
                    : blitterGrants[^1].Access.CompletedCycle;
                _trace.CompleteBlitterReplay(
                    completionBase + operation.CompletionDelay,
                    operation.Zero,
                    operation.SourceA,
                    operation.SourceB,
                    operation.SourceC,
                    operation.DestinationD);
            }

            var paulaGrants = new OraclePaulaGrant[_paulaWordCount];
            for (var index = 0; index < _paulaWordCount; index++)
            {
                var word = _paulaWords[index];
                var access = engine.ReservePaulaDmaWordSlot(
                    word.Channel,
                    word.Address,
                    word.RequestedCycle);
                var value = ReadWord(initialMemory, word.Address);
                _trace.AddPaulaDmaWord(
                    word.Channel,
                    word.Address,
                    value,
                    word.RequestedCycle,
                    access.GrantedCycle,
                    access.CompletedCycle);
                paulaGrants[index] = new OraclePaulaGrant(word, value, access);
            }

            var grants = new OracleCpuGrant[_cpuRequestCount];
            for (var index = 0; index < _cpuRequestCount; index++)
            {
                var request = _cpuRequests[index];
                engine.GrantCpuDataSingleSlot(
                    request.Kind,
                    AmigaBusAccessTarget.ChipRam,
                    request.Address,
                    AmigaBusAccessSize.Word,
                    request.RequestedCycle,
                    request.IsWrite,
                    out var grantedCycle,
                    out var completedCycle);
                grants[index] = new OracleCpuGrant(request, grantedCycle, completedCycle);
            }

            var memory = (byte[])initialMemory.Clone();
            for (var index = 0; index < _trace.SlotCount; index++)
            {
                var cycle = _trace.FirstCycle +
                    ((long)index * AgnusChipSlotScheduler.SlotCycles);
                var committed = engine.TryGetCommittedSlotOwner(cycle, out var owner);
                if (!committed)
                {
                    _trace.SetExpectedSlot(index, CreateFreeRecord(cycle));
                    continue;
                }

                if (owner == AgnusChipSlotOwner.Refresh)
                {
                    _trace.SetExpectedSlot(index, CreateRefreshRecord(cycle));
                    continue;
                }

                var fixedEntry = _finalPlan[index];
                if (owner is AgnusChipSlotOwner.Bitplane or AgnusChipSlotOwner.Sprite)
                {
                    Assert.True(fixedEntry.Occupied);
                    Assert.Equal(fixedEntry.Owner, owner);
                    Assert.True(engine.TryGetCommittedSlotSnapshot(cycle, out var snapshot));
                    var address = _finalAddresses[index];
                    Assert.Equal(address, snapshot.Address);
                    var value = ReadWord(memory, address);
                    var requester = owner == AgnusChipSlotOwner.Bitplane
                        ? AmigaBusRequester.Bitplane
                        : AmigaBusRequester.Sprite;
                    var kind = owner == AgnusChipSlotOwner.Bitplane
                        ? AmigaBusAccessKind.Bitplane
                        : AmigaBusAccessKind.Sprite;
                    _trace.SetExpectedSlot(index, new AgnusCommittedSlotRecord(
                        cycle,
                        cycle,
                        cycle + AgnusChipSlotScheduler.SlotCycles,
                        owner,
                        requester,
                        kind,
                        AmigaBusAccessTarget.ChipRam,
                        MaskAddress(memory, address),
                        value,
                        addressValid: true,
                        valueValid: true,
                        granted: true,
                        isWrite: false,
                        fixedEntry.Channel,
                        fixedEntry.Phase));
                    continue;
                }

                if (owner == AgnusChipSlotOwner.Copper)
                {
                    var copperGrantIndex = FindCopperGrant(
                        copperGrants,
                        copperGrantCount,
                        cycle);
                    Assert.True(
                        copperGrantIndex >= 0,
                        $"Missing Copper oracle request for slot {cycle}.");
                    var copperGrant = copperGrants[copperGrantIndex];
                    Assert.True(engine.TryGetCommittedSlotSnapshot(cycle, out var snapshot));
                    Assert.Equal(copperGrant.Address, snapshot.Address);
                    var value = ReadWord(memory, copperGrant.Address);
                    Assert.Equal(copperGrant.ExpectedValue, value);
                    _trace.SetExpectedSlot(index, new AgnusCommittedSlotRecord(
                        cycle,
                        copperGrant.Access.RequestedCycle,
                        copperGrant.Access.CompletedCycle,
                        AgnusChipSlotOwner.Copper,
                        AmigaBusRequester.Copper,
                        AmigaBusAccessKind.Copper,
                        AmigaBusAccessTarget.ChipRam,
                        MaskAddress(memory, copperGrant.Address),
                        value,
                        addressValid: true,
                        valueValid: true,
                        granted: true,
                        isWrite: false,
                        channel: 0,
                        copperGrant.Phase));
                    continue;
                }

                if (owner == AgnusChipSlotOwner.Blitter)
                {
                    var blitterGrantIndex = FindBlitterGrant(blitterGrants, cycle);
                    Assert.True(
                        blitterGrantIndex >= 0,
                        $"Missing blitter oracle request for slot {cycle}.");
                    var blitterGrant = blitterGrants[blitterGrantIndex];
                    Assert.True(engine.TryGetCommittedSlotSnapshot(cycle, out var snapshot));
                    Assert.Equal(blitterGrant.MicroOp.Address, snapshot.Address);
                    ushort value;
                    if (blitterGrant.MicroOp.IsWrite)
                    {
                        value = blitterGrant.MicroOp.Value;
                        WriteWord(memory, blitterGrant.MicroOp.Address, value);
                    }
                    else
                    {
                        value = ReadWord(memory, blitterGrant.MicroOp.Address);
                        Assert.Equal(blitterGrant.MicroOp.Value, value);
                    }

                    _trace.SetExpectedSlot(index, new AgnusCommittedSlotRecord(
                        cycle,
                        blitterGrant.Access.RequestedCycle,
                        blitterGrant.Access.CompletedCycle,
                        AgnusChipSlotOwner.Blitter,
                        AmigaBusRequester.Blitter,
                        AmigaBusAccessKind.Blitter,
                        AmigaBusAccessTarget.ChipRam,
                        MaskAddress(memory, blitterGrant.MicroOp.Address),
                        value,
                        addressValid: true,
                        valueValid: true,
                        granted: true,
                        blitterGrant.MicroOp.IsWrite,
                        channel: 0,
                        phase: 0));
                    continue;
                }

                if (owner == AgnusChipSlotOwner.Paula)
                {
                    var paulaGrantIndex = FindPaulaGrant(paulaGrants, cycle);
                    Assert.True(
                        paulaGrantIndex >= 0,
                        $"Missing Paula oracle request for slot {cycle}.");
                    var paulaGrant = paulaGrants[paulaGrantIndex];
                    var value = ReadWord(memory, paulaGrant.Word.Address);
                    Assert.Equal(paulaGrant.Value, value);
                    _trace.SetExpectedSlot(index, new AgnusCommittedSlotRecord(
                        cycle,
                        paulaGrant.Word.RequestedCycle,
                        paulaGrant.Access.CompletedCycle,
                        AgnusChipSlotOwner.Paula,
                        AmigaBusRequester.Paula,
                        AmigaBusAccessKind.PaulaDma,
                        AmigaBusAccessTarget.ChipRam,
                        MaskAddress(memory, paulaGrant.Word.Address),
                        value,
                        addressValid: true,
                        valueValid: true,
                        granted: true,
                        isWrite: false,
                        (byte)paulaGrant.Word.Channel,
                        phase: 0));
                    continue;
                }

                if (owner == AgnusChipSlotOwner.Disk)
                {
                    var diskGrantIndex = FindDiskGrant(diskGrants, cycle);
                    Assert.True(
                        diskGrantIndex >= 0,
                        $"Missing disk oracle request for slot {cycle}.");
                    var diskGrant = diskGrants[diskGrantIndex];
                    ushort value;
                    if (diskGrant.Word.WriteMode)
                    {
                        value = ReadWord(memory, diskGrant.Word.Address);
                    }
                    else
                    {
                        value = diskGrant.Word.Value;
                        WriteWord(memory, diskGrant.Word.Address, value);
                    }

                    _trace.SetExpectedSlot(index, new AgnusCommittedSlotRecord(
                        cycle,
                        diskGrant.Word.RequestedCycle,
                        diskGrant.Access.CompletedCycle,
                        AgnusChipSlotOwner.Disk,
                        AmigaBusRequester.Disk,
                        AmigaBusAccessKind.DiskDma,
                        AmigaBusAccessTarget.ChipRam,
                        MaskAddress(memory, diskGrant.Word.Address),
                        value,
                        addressValid: true,
                        valueValid: true,
                        granted: true,
                        diskGrant.Word.WriteMode,
                        channel: 0,
                        phase: 0));
                    continue;
                }

                Assert.Equal(AgnusChipSlotOwner.Cpu, owner);
                var grantIndex = FindCpuGrant(grants, cycle);
                Assert.True(grantIndex >= 0, $"Missing CPU oracle request for slot {cycle}.");
                var grant = grants[grantIndex];
                ushort cpuValue;
                if (grant.Request.IsWrite)
                {
                    WriteWord(memory, grant.Request.Address, grant.Request.Value);
                    cpuValue = grant.Request.Value;
                }
                else
                {
                    cpuValue = ReadWord(memory, grant.Request.Address);
                }

                _trace.SetExpectedSlot(index, new AgnusCommittedSlotRecord(
                    cycle,
                    grant.Request.RequestedCycle,
                    grant.CompletedCycle,
                    AgnusChipSlotOwner.Cpu,
                    AmigaBusRequester.Cpu,
                    grant.Request.Kind,
                    AmigaBusAccessTarget.ChipRam,
                    MaskAddress(memory, grant.Request.Address),
                    cpuValue,
                    addressValid: true,
                    valueValid: true,
                    granted: true,
                    grant.Request.IsWrite,
                    channel: 0,
                    phase: 0));
            }

            return _trace;
        }

        private static int FindCopperGrant(
            OracleCopperGrant[] grants,
            int grantCount,
            long cycle)
        {
            for (var index = 0; index < grantCount; index++)
            {
                if (grants[index].Access.GrantedCycle == cycle)
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindCpuGrant(OracleCpuGrant[] grants, long cycle)
        {
            for (var index = 0; index < grants.Length; index++)
            {
                if (grants[index].GrantedCycle == cycle)
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindBlitterGrant(OracleBlitterGrant[] grants, long cycle)
        {
            for (var index = 0; index < grants.Length; index++)
            {
                if (grants[index].Access.GrantedCycle == cycle)
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindPaulaGrant(OraclePaulaGrant[] grants, long cycle)
        {
            for (var index = 0; index < grants.Length; index++)
            {
                if (grants[index].Access.GrantedCycle == cycle)
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindDiskGrant(OracleDiskGrant[] grants, long cycle)
        {
            for (var index = 0; index < grants.Length; index++)
            {
                if (grants[index].Access.GrantedCycle == cycle)
                {
                    return index;
                }
            }

            return -1;
        }

        private static uint MaskAddress(byte[] memory, uint address)
            => address & (uint)(memory.Length - 1);

        private static AgnusCommittedSlotRecord CreateRefreshRecord(long cycle)
            => new(
                cycle,
                cycle,
                cycle + AgnusChipSlotScheduler.SlotCycles,
                AgnusChipSlotOwner.Refresh,
                AmigaBusRequester.Host,
                AmigaBusAccessKind.HostTrap,
                AmigaBusAccessTarget.ChipRam,
                address: 0,
                value: 0,
                addressValid: false,
                valueValid: false,
                granted: true,
                isWrite: false,
                channel: 0,
                phase: 0);

        private static AgnusCommittedSlotRecord CreateFreeRecord(long cycle)
            => new(
                cycle,
                cycle,
                cycle,
                AgnusChipSlotOwner.Free,
                AmigaBusRequester.Host,
                AmigaBusAccessKind.HostTrap,
                AmigaBusAccessTarget.ChipRam,
                address: 0,
                value: 0,
                addressValid: false,
                valueValid: false,
                granted: false,
                isWrite: false,
                channel: 0,
                phase: 0);

        private readonly record struct OracleCpuGrant(
            CpuWordRequest Request,
            long GrantedCycle,
            long CompletedCycle);

        private readonly record struct OracleBlitterGrant(
            CapturedBlitterMicroOp MicroOp,
            AmigaBusAccessResult Access);

        private readonly record struct PaulaOracleWord(
            int Channel,
            uint Address,
            long RequestedCycle);

        private readonly record struct OraclePaulaGrant(
            PaulaOracleWord Word,
            ushort Value,
            AmigaBusAccessResult Access);

        private readonly record struct DiskOracleWord(
            uint Address,
            ushort Value,
            bool WriteMode,
            long RequestedCycle);

        private readonly record struct OracleDiskGrant(
            DiskOracleWord Word,
            AmigaBusAccessResult Access);

        private readonly record struct CopperOracleInstruction(
            uint Pc,
            ushort FirstWord,
            ushort SecondWord,
            AgnusOfflineCopperAction Action,
            long WaitResumeCycle,
            uint NextPc,
            bool ComparisonSatisfied,
            bool WaitForBlitter,
            bool CommitMove,
            ushort MoveRegister,
            ushort MoveValue,
            int PatchStart,
            int PatchCount);

        private readonly record struct OracleCopperGrant(
            int InstructionIndex,
            byte Phase,
            uint Address,
            ushort ExpectedValue,
            AmigaBusAccessResult Access);
    }
}
