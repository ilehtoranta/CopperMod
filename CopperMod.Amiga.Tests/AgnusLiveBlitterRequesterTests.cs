using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.Runtime;
using CopperDisk;
using Copper68k;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusLiveBlitterRequesterTests
{
    private const string EvidenceModeVariable =
        "COPPER_G3L_EVIDENCE_MODE";
    private const string EvidenceOutputVariable =
        "COPPER_G3L_EVIDENCE_OUTPUT";
    private const uint SourceA = 0x3000;
    private const uint SourceB = 0x3200;
    private const uint SourceC = 0x3400;
    private const uint DestinationD = 0x3800;
    private const uint CopperList = 0x2400;

    [Fact]
    public void CandidateIsExplicitlyOptInAndRequiresAcceptedEarlierLiveStages()
    {
        var legacy = new AmigaBus(captureBusAccesses: false);
        var candidate = CreateBus(candidate: true, captureBusAccesses: false);

        Assert.False(legacy.AgnusLiveBlitterEnabled);
        Assert.True(candidate.AgnusLiveDisplayLedgerEnabled);
        Assert.True(candidate.AgnusLiveCopperEnabled);
        Assert.True(candidate.AgnusLiveBlitterEnabled);
        Assert.Throws<ArgumentException>(() => new AmigaBus(
            captureBusAccesses: false,
            enableAgnusLiveBlitter: true));
        Assert.Throws<NotSupportedException>(() => new AmigaBus(
            captureBusAccesses: false,
            chipset: AmigaChipset.OcsNtsc,
            enableAgnusLiveDisplayLedger: true,
            enableAgnusLiveCopper: true,
            enableAgnusLiveBlitter: true));
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
    public void EveryAreaChannelCombinationMatchesHardwareAndFunctionalReference(
        int channelMask)
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureChannelFixture(legacy, channelMask);
        ConfigureChannelFixture(candidate, channelMask);

        RunUntilIdle(legacy);
        RunUntilIdle(candidate);

        if ((channelMask & 0x1) == 0)
        {
            AssertEquivalent(legacy, candidate, DestinationD, wordCount: 2);
        }
        else
        {
            // The legacy engine writes D in the same logical word. OCS/ECS
            // instead pipelines D: the initial D phase is idle, each main
            // D phase writes the previous word, and a final D write drains
            // after BBUSY clears. WinUAE and vAmiga both model this ordering.
            AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 2);
            AssertAreaChannelProgram(candidate, channelMask, wordCount: 2);
            AssertCompletedBlitPublished(
                candidate,
                allowsPostCompletionPipelineDrain: true);
        }

        Assert.Equal(
            channelMask == 0 ? 0 : 1,
            candidate.AgnusLiveBlitterDiagnostics.Completions);
    }

    [Fact]
    public void AreaReadsWritesPointersModulosAndMasksMatchLegacy()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(legacy);
        ConfigureAreaMatrixFixture(candidate);

        RunUntilIdle(legacy);
        RunUntilIdle(candidate);

        AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 8);
        AssertAreaChannelProgram(
            candidate,
            channelMask: 0xF,
            wordCount: 8,
            widthWords: 4,
            sourceAModulo: 2,
            sourceBModulo: 4,
            sourceCModulo: 6,
            destinationDModulo: 8);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.Equal(32, diagnostics.AreaMicroOps);
        Assert.Equal(8, diagnostics.AreaWords);
        Assert.Equal(24, diagnostics.GrantedReads);
        Assert.Equal(8, diagnostics.GrantedWrites);
        Assert.Equal(0, diagnostics.LineMicroOps);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void DescendingExclusiveFillStateAndOutputMatchLegacy()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureDescendingFillFixture(legacy);
        ConfigureDescendingFillFixture(candidate);

        RunUntilIdle(legacy);
        RunUntilIdle(candidate);

        AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 4);
        AssertAreaChannelProgram(
            candidate,
            channelMask: 0x9,
            wordCount: 4,
            sourceA: SourceA + 6,
            destinationD: DestinationD + 6,
            addressStep: -2);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
        Assert.True(candidate.AgnusLiveBlitterDiagnostics.AreaWords >= 4);
    }

    [Fact]
    public void LineMicroOperationsAndFinalAddressStateMatchLegacy()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureLineFixture(legacy);
        ConfigureLineFixture(candidate);

        RunUntilIdle(legacy);
        RunUntilIdle(candidate);

        AssertEquivalent(legacy, candidate, DestinationD, wordCount: 64);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.True(diagnostics.LineMicroOps > 0);
        Assert.True(diagnostics.LinePixels > 0);
        Assert.Equal(0, diagnostics.AreaMicroOps);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void NiceCpuContentionMatchesLegacy()
        => AssertNiceCpuContentionParity();

    [Fact]
    public void NiceLiveBlitterYieldsOnlyAfterThreeCommittedDmaCycles()
    {
        var bus = CreateBus(candidate: true);
        var slots = bus.CausalBusExecutor.Slots;
        var slotCycles = AgnusChipSlotScheduler.SlotCycles;
        var firstSlot = 596L;
        slots.BeginPendingCpuSlotRequest(
            AmigaBusAccessKind.CpuDataRead,
            AmigaBusAccessTarget.ChipRam,
            0x3000,
            AmigaBusAccessSize.Word,
            firstSlot,
            isWrite: false);
        try
        {
            for (var slot = 0; slot < 3; slot++)
            {
                var slotCycle = firstSlot + (slot * slotCycles);
                Assert.True(slots.TryReserveBlitterDmaWordExactSlot(
                    (uint)(0x1000 + (slot * 2)),
                    firstSlot,
                    slotCycle,
                    isWrite: false,
                    out _));
                Assert.False(slots.TryGrantCpuDataSingleExactSlot(
                    AmigaBusAccessKind.CpuDataRead,
                    AmigaBusAccessTarget.ChipRam,
                    0x3000,
                    AmigaBusAccessSize.Word,
                    firstSlot,
                    slotCycle,
                    isWrite: false,
                    allowNiceBlitterSteal: true,
                    out _));
            }

            var yieldCycle = firstSlot + (3 * slotCycles);
            Assert.False(slots.TryReserveBlitterDmaWordExactSlot(
                0x1006,
                firstSlot,
                yieldCycle,
                isWrite: false,
                out _));
            Assert.True(slots.TryGrantCpuDataSingleExactSlot(
                AmigaBusAccessKind.CpuDataRead,
                AmigaBusAccessTarget.ChipRam,
                0x3000,
                AmigaBusAccessSize.Word,
                firstSlot,
                yieldCycle,
                isWrite: false,
                allowNiceBlitterSteal: true,
                out var completedCycle));
            Assert.Equal(yieldCycle + slotCycles, completedCycle);
        }
        finally
        {
            slots.ClearPendingCpuSlotRequest();
        }
    }

    [Fact]
    public void ConsecutiveNiceBlitterCpuWordsYieldAfterFourAllocatedMemoryCycles()
    {
        var bus = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(
            bus,
            widthWords: 50,
            height: 50,
            nasty: false,
            startCycle: 596);
        var slotCycles = AgnusChipSlotScheduler.SlotCycles;
        var cycle = bus.Blitter.GetRawBusEligibilityCycle();

        _ = bus.ReadWord(
            SourceA,
            ref cycle,
            AmigaBusAccessKind.CpuInstructionFetch);
        cycle += slotCycles;
        _ = bus.ReadWord(
            SourceA + 2,
            ref cycle,
            AmigaBusAccessKind.CpuInstructionFetch);

        var cpu = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu)
            .TakeLast(2)
            .ToArray();
        Assert.Equal(2, cpu.Length);
        Assert.True(bus.Blitter.Busy);
        Assert.Equal(
            cpu[0].CompletedCycle + slotCycles,
            cpu[1].RequestedCycle);
        var grantCadence =
            cpu[1].GrantedCycle - cpu[0].GrantedCycle;
        var contenders = Enumerable.Range(
                0,
                (int)((cpu[1].GrantedCycle -
                    cpu[1].RequestedCycle) / slotCycles))
            .Select(index =>
            {
                var slotCycle =
                    cpu[1].RequestedCycle + (index * slotCycles);
                return bus.TryGetCommittedAgnusSlotOwner(
                    slotCycle,
                    out var owner)
                        ? $"{slotCycle}:{owner}"
                        : $"{slotCycle}:uncommitted";
            });
        Assert.True(
            grantCadence == 5 * slotCycles,
            $"cadence={grantCadence}, " +
            $"first={cpu[0].RequestedCycle}->{cpu[0].GrantedCycle}.." +
            $"{cpu[0].CompletedCycle}, " +
            $"second={cpu[1].RequestedCycle}->{cpu[1].GrantedCycle}.." +
            $"{cpu[1].CompletedCycle}, " +
            $"contenders=[{string.Join(",", contenders)}], " +
            $"blitter=[{string.Join(",", BlitterSignature(bus))}], " +
            $"slots=[{SlotSignature(bus, cpu[1].CompletedCycle)}]");
        AssertCpuAndBlitterSlotsAreExclusive(bus, cpu);
    }

    [Fact]
    public void ConsecutiveNiceBlitterCpuCustomWritesYieldAfterFourAllocatedMemoryCycles()
    {
        var bus = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(
            bus,
            widthWords: 50,
            height: 50,
            nasty: false,
            startCycle: 596);
        var slotCycles = AgnusChipSlotScheduler.SlotCycles;
        var cycle = bus.Blitter.GetRawBusEligibilityCycle();

        bus.WriteWord(
            0x00DFF180,
            0x044F,
            ref cycle,
            AmigaBusAccessKind.CpuDataWrite);
        cycle += slotCycles;
        bus.WriteWord(
            0x00DFF180,
            0x0FF0,
            ref cycle,
            AmigaBusAccessKind.CpuDataWrite);

        var cpu = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu &&
                access.Request.Target == AmigaBusAccessTarget.CustomRegisters)
            .TakeLast(2)
            .ToArray();
        Assert.Equal(2, cpu.Length);
        Assert.True(bus.Blitter.Busy);
        Assert.Equal(
            cpu[0].CompletedCycle + slotCycles,
            cpu[1].RequestedCycle);
        Assert.Equal(
            5 * slotCycles,
            cpu[1].GrantedCycle - cpu[0].GrantedCycle);
        AssertCpuAndBlitterSlotsAreExclusive(bus, cpu);
    }

    [Fact]
    public void NastyStartupIdleCyclesRemainCpuUsableBeforeFirstAreaDma()
    {
        var bus = CreateBus(candidate: true);
        const long startCycle = 596;
        ConfigureChannelFixture(
            bus,
            channelMask: 0xF,
            widthWords: 2,
            height: 1,
            nasty: true,
            startCycle);
        var slotCycles = AgnusChipSlotScheduler.SlotCycles;
        var cycle = startCycle + slotCycles;

        _ = bus.ReadWord(
            0x1000,
            ref cycle,
            AmigaBusAccessKind.CpuInstructionFetch);
        _ = bus.ReadWord(
            0x1002,
            ref cycle,
            AmigaBusAccessKind.CpuInstructionFetch);
        _ = bus.ReadWord(
            0x1004,
            ref cycle,
            AmigaBusAccessKind.CpuInstructionFetch);
        RunUntilIdle(bus, startCycle + 256);

        var cpu = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu)
            .TakeLast(3)
            .ToArray();
        var firstBlitter = BlitterAccesses(bus)[0];
        Assert.Equal(
            new[]
            {
                startCycle + slotCycles,
                startCycle + (2 * slotCycles),
                startCycle + (3 * slotCycles)
            },
            cpu.Select(access => access.GrantedCycle));
        Assert.Equal(
            startCycle + (4 * slotCycles),
            firstBlitter.GrantedCycle);
        AssertCpuAndBlitterSlotsAreExclusive(bus, cpu);
    }

    [Fact]
    public void NastyBlitterKeepsEveryAvailableSlotAcrossPendingCpuRead()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(
            legacy,
            widthWords: 4,
            height: 1,
            nasty: true);
        ConfigureAreaMatrixFixture(
            candidate,
            widthWords: 4,
            height: 1,
            nasty: true);
        var candidateSlotAudit = new List<AgnusSlotScheduleAuditEntry>();
        candidate.SetSlotScheduleAuditSink(candidateSlotAudit.Add);
        // Enter arbitration when the first blitter word is eligible. Starting
        // the CPU at cycle 2 only proves that it can use a genuinely idle slot
        // before this fixture's first DMA request at cycle 8.
        var legacyCycle = 8L;
        var candidateCycle = 8L;

        var legacyValue = legacy.ReadWord(
            SourceA,
            ref legacyCycle,
            AmigaBusAccessKind.CpuDataRead);
        var candidateValue = candidate.ReadWord(
            SourceA,
            ref candidateCycle,
            AmigaBusAccessKind.CpuDataRead);

        Assert.Equal(legacyValue, candidateValue);
        Assert.True(
            candidateCycle < legacyCycle,
            "The live requester must not reproduce the retrospective legacy " +
            $"BLTPRI idle slots: legacy={legacyCycle}, candidate={candidateCycle}.");
        Assert.DoesNotContain(candidateSlotAudit, entry =>
            entry.ReplacedExisting &&
            entry.Owner is AgnusChipSlotOwner.Cpu or AgnusChipSlotOwner.Blitter &&
            entry.ReplacedOwner is AgnusChipSlotOwner.Cpu or AgnusChipSlotOwner.Blitter);
        var accesses = BlitterAccesses(candidate);
        Assert.NotEmpty(accesses);
        foreach (var access in accesses)
        {
            for (var cycle = AgnusChipSlotScheduler.AlignToSlot(
                     access.RequestedCycle);
                 cycle < access.GrantedCycle;
                 cycle += AgnusChipSlotScheduler.SlotCycles)
            {
                Assert.True(
                    candidate.TryGetCommittedAgnusSlotOwner(
                        cycle,
                        out var owner) &&
                    owner is not (
                        AgnusChipSlotOwner.Free or
                        AgnusChipSlotOwner.Cpu or
                        AgnusChipSlotOwner.Blitter),
                    $"BLTPRI left eligible cycle {cycle} unused: " +
                    $"slots=[{SlotSignature(candidate, 64)}].");
            }
        }

        var cpuRead = Assert.Single(
            candidate.BusAccesses,
            access =>
                access.Request.Requester == AmigaBusRequester.Cpu &&
                access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
                access.Request.Address == SourceA);
        Assert.Equal(
            accesses[^1].CompletedCycle,
            cpuRead.GrantedCycle);
        RunUntilIdle(legacy);
        RunUntilIdle(candidate);
        AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 4);
        AssertAreaChannelProgram(candidate, channelMask: 0xF, wordCount: 4);
        AssertNoUnexplainedEligibleIdleSlots(candidate);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.Equal(diagnostics.GrantedRequests, diagnostics.NastyGrants);
        Assert.Equal(0, diagnostics.ContractViolations);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Theory]
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
    public void EveryActiveAreaChannelCombinationIsExactUnderSustainedNastyCpuContention(
        int channelMask)
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureChannelFixture(
            legacy,
            channelMask,
            widthWords: 16,
            height: 2,
            nasty: true);
        ConfigureChannelFixture(
            candidate,
            channelMask,
            widthWords: 16,
            height: 2,
            nasty: true);
        var nominalCandidateCompletion =
            candidate.Blitter.GetPredictedCompletionCycle();

        var legacyCpuReads = RunSustainedCpuContention(legacy);
        var candidateCpuReads = RunSustainedCpuContention(candidate);

        Assert.True(legacyCpuReads > 0);
        Assert.True(candidateCpuReads > 0);
        AssertFunctionalStateEquivalent(
            legacy,
            candidate,
            wordCount: 32);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: (channelMask & 0x1) != 0);
        AssertNoUnexplainedEligibleIdleSlots(candidate);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.True(diagnostics.GrantedRequests > 0);
        Assert.Equal(diagnostics.GrantedRequests, diagnostics.NastyGrants);
        if ((channelMask & 0x1) == 0)
        {
            AssertCompletionMatchesNominalAndDeniedSlots(
                candidate,
                nominalCandidateCompletion,
                channelMask == 0x4
                    ? GetBOnlyInternalPauseDelay(candidate)
                    : 0);
        }
        Assert.Equal(0, diagnostics.ContractViolations);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void NoChannelAreaProgramCompletesExactlyDuringSustainedCpuTraffic()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureChannelFixture(
            legacy,
            channelMask: 0,
            widthWords: 16,
            height: 2,
            nasty: true);
        ConfigureChannelFixture(
            candidate,
            channelMask: 0,
            widthWords: 16,
            height: 2,
            nasty: true);
        var nominalCompletion =
            candidate.Blitter.GetPredictedCompletionCycle();

        Assert.True(RunSustainedCpuContention(legacy) > 0);
        Assert.True(RunSustainedCpuContention(candidate) > 0);

        AssertFunctionalStateEquivalent(
            legacy,
            candidate,
            wordCount: 32);
        Assert.Empty(BlitterAccesses(candidate));
        AssertCompletedBlitPublished(candidate);
        Assert.Equal(nominalCompletion, candidate.Blitter.LastCompletionCycle);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.Equal(0, diagnostics.GrantedRequests);
        Assert.Equal(0, diagnostics.DeniedRequests);
        Assert.Equal(0, diagnostics.NastyGrants);
        Assert.Equal(0, diagnostics.ContractViolations);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void NastyDestinationOnlyBlitKeepsHrmFourCycleCadenceUnderSustainedCpuContention()
    {
        var bus = CreateBus(candidate: true);
        ConfigureChannelFixture(
            bus,
            channelMask: 0x1,
            widthWords: 8,
            height: 1,
            nasty: true,
            startCycle: 596);
        _ = RunSustainedCpuContention(bus);

        var accesses = BlitterAccesses(bus);
        var cpuGrantCycles = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu)
            .Select(access => access.GrantedCycle)
            .ToHashSet();
        Assert.Equal(8, accesses.Length);
        for (var index = 1; index < accesses.Length; index++)
        {
            var spacing =
                accesses[index].GrantedCycle -
                accesses[index - 1].GrantedCycle;
            Assert.True(
                spacing == 2 * AgnusChipSlotScheduler.SlotCycles,
                $"D-only grant spacing at index {index} was {spacing}; " +
                $"completion={bus.Blitter.LastCompletionCycle}, " +
                $"transitions=[{string.Join(
                    ",",
                    bus.Blitter.LiveAfterSlotTransitionCycles)}], " +
                $"accesses=[{string.Join(",", BlitterSignature(bus))}], " +
                $"slots=[{SlotWindowSignature(
                    bus,
                    accesses[index - 1].GrantedCycle,
                    accesses[index].GrantedCycle)}].");
            Assert.Contains(
                accesses[index - 1].GrantedCycle +
                    AgnusChipSlotScheduler.SlotCycles,
                cpuGrantCycles);
        }

        Assert.Equal(
            bus.Blitter.LastCompletionCycle +
                (2 * AgnusChipSlotScheduler.SlotCycles),
            accesses[^1].CompletedCycle);
        AssertCompletedBlitPublished(
            bus,
            allowsPostCompletionPipelineDrain: true);
        AssertNoUnexplainedEligibleIdleSlots(bus);
        Assert.Equal(
            bus.AgnusLiveBlitterDiagnostics.GrantedRequests,
            bus.AgnusLiveBlitterDiagnostics.NastyGrants);
    }

    [Fact]
    public void DelayedFinalAreaWritePublishesOcsCompletionBeforePipelineDrain()
    {
        var bus = CreateBus(candidate: true);
        ConfigureChannelFixture(
            bus,
            channelMask: 0x1,
            widthWords: 1,
            height: 1,
            nasty: true,
            startCycle: 596);
        var initialDestination = BigEndian.ReadUInt16(
            bus.ChipRam,
            (int)DestinationD,
            "G3L initial final D destination");
        var nominalCompletion =
            bus.Blitter.GetPredictedCompletionCycle();
        var writeRequestCycle =
            nominalCompletion + AgnusChipSlotScheduler.SlotCycles;
        var lastBlockedWriteCycle =
            writeRequestCycle + AgnusChipSlotScheduler.SlotCycles;
        for (var cycle = writeRequestCycle;
             cycle <= lastBlockedWriteCycle;
             cycle += AgnusChipSlotScheduler.SlotCycles)
        {
            Assert.True(bus.TryReserveDisplayDmaSlot(
                AmigaBusRequester.Bitplane,
                AmigaBusAccessKind.Bitplane,
                0x7000u + (uint)cycle,
                cycle,
                out var reservation));
            Assert.Equal(cycle, reservation.GrantedCycle);
        }

        bus.AdvanceDmaTo(nominalCompletion);

        Assert.False(bus.Blitter.Busy);
        Assert.Equal(0, bus.Blitter.DmaconStatusBits & 0x4000);
        Assert.True(bus.Blitter.BusPipelineActive);
        Assert.Equal(nominalCompletion, bus.Blitter.LastCompletionCycle);
        Assert.Equal(
            nominalCompletion,
            Assert.Single(bus.Blitter.CompletionCycles));
        Assert.NotEqual(
            0,
            bus.Paula.Intreq & AmigaConstants.IntreqBlitter);
        Assert.Equal(
            initialDestination,
            BigEndian.ReadUInt16(
                bus.ChipRam,
                (int)DestinationD,
                "G3L pending final D write"));

        var delayedGrant =
            lastBlockedWriteCycle + AgnusChipSlotScheduler.SlotCycles;
        var cpuCycle = delayedGrant;
        _ = bus.ReadWord(
            0x1800,
            ref cpuCycle,
            AmigaBusAccessKind.CpuInstructionFetch);
        Assert.True(cpuCycle > delayedGrant);

        var finalWrite = Assert.Single(BlitterAccesses(bus));
        Assert.True(finalWrite.Request.IsWrite);
        Assert.Equal(writeRequestCycle, finalWrite.RequestedCycle);
        Assert.Equal(delayedGrant, finalWrite.GrantedCycle);
        var cpuFetch = Assert.Single(
            bus.BusAccesses,
            access =>
                access.Request.Requester == AmigaBusRequester.Cpu &&
                access.Request.Kind ==
                    AmigaBusAccessKind.CpuInstructionFetch &&
                access.Request.Address == 0x1800);
        Assert.True(cpuFetch.GrantedCycle > finalWrite.GrantedCycle);
        Assert.False(bus.Blitter.Busy);
        Assert.False(bus.Blitter.BusPipelineActive);
        Assert.Equal(nominalCompletion, bus.Blitter.LastCompletionCycle);
        Assert.Equal(
            nominalCompletion,
            Assert.Single(bus.Blitter.CompletionCycles));
        Assert.NotEqual(
            0,
            bus.Paula.Intreq & AmigaConstants.IntreqBlitter);
        Assert.Equal(
            (ushort)0,
            BigEndian.ReadUInt16(
                bus.ChipRam,
                (int)DestinationD,
                "G3L delayed final D write"));
        Assert.Equal(
            DestinationD + 2,
            bus.Blitter.CaptureSnapshot().DestinationD);
        AssertNoUnexplainedEligibleIdleSlots(bus);
        var diagnostics = bus.AgnusLiveBlitterDiagnostics;
        Assert.Equal(1, diagnostics.GrantedRequests);
        Assert.Equal(1, diagnostics.NastyGrants);
        Assert.Equal(2, diagnostics.DeniedRequests);
        Assert.Equal(1, diagnostics.Completions);
        Assert.Equal(1, diagnostics.Interrupts);
    }

    [Fact]
    public void NastyLineModeRemainsExactUnderSustainedCpuContention()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureLineFixture(legacy, nasty: true, startCycle: 596);
        ConfigureLineFixture(candidate, nasty: true, startCycle: 596);
        var nominalCandidateCompletion =
            candidate.Blitter.GetPredictedCompletionCycle();

        _ = RunSustainedCpuContention(legacy);
        _ = RunSustainedCpuContention(candidate);

        AssertFunctionalStateEquivalent(
            legacy,
            candidate,
            wordCount: 64);
        AssertNoUnexplainedEligibleIdleSlots(candidate);
        var accesses = BlitterAccesses(candidate);
        Assert.NotEmpty(accesses);
        Assert.Contains(accesses, access => !access.Request.IsWrite);
        Assert.Contains(accesses, access => access.Request.IsWrite);
        var cpuGrantCycles = candidate.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu)
            .Select(access => access.GrantedCycle)
            .ToHashSet();
        Assert.Equal(0, accesses.Length & 1);
        for (var index = 0; index < accesses.Length; index += 2)
        {
            var read = accesses[index];
            var write = accesses[index + 1];
            Assert.False(read.Request.IsWrite);
            Assert.True(write.Request.IsWrite);
            Assert.Equal(
                2 * AgnusChipSlotScheduler.SlotCycles,
                write.GrantedCycle - read.GrantedCycle);
            Assert.Contains(
                read.GrantedCycle + AgnusChipSlotScheduler.SlotCycles,
                cpuGrantCycles);
            if (index + 2 < accesses.Length)
            {
                Assert.Equal(
                    2 * AgnusChipSlotScheduler.SlotCycles,
                    accesses[index + 2].GrantedCycle - write.GrantedCycle);
                Assert.Contains(
                    write.GrantedCycle + AgnusChipSlotScheduler.SlotCycles,
                    cpuGrantCycles);
            }
        }
        AssertCompletedBlitPublished(
            candidate);
        Assert.Equal(
            accesses[^1].CompletedCycle,
            candidate.Blitter.LastCompletionCycle);
        AssertCompletionMatchesNominalAndDeniedSlots(
            candidate,
            nominalCandidateCompletion);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.True(diagnostics.LinePixels > 0);
        Assert.Equal(diagnostics.GrantedRequests, diagnostics.NastyGrants);
        Assert.Equal(0, diagnostics.ContractViolations);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void NastyBlitterYieldsToEveryHigherPriorityAgnusOwner()
    {
        var bus = CreateBus(candidate: true);
        SeedSources(bus, wordCount: 300);
        ConfigureAreaRegisters(bus, 0x0FCA);
        EnableBlitterDma(bus, nasty: true);
        ConfigureCopperHaltList(bus);
        StartBlit(bus, widthWords: 32, height: 8);

        const long secondLineStart =
            AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var fixedSlots = new[]
        {
            (
                Owner: AgnusChipSlotOwner.Disk,
                Requester: AmigaBusRequester.Disk,
                Kind: AmigaBusAccessKind.DiskDma,
                Cycle: secondLineStart + (0x08 * AgnusChipSlotScheduler.SlotCycles)),
            (
                Owner: AgnusChipSlotOwner.Paula,
                Requester: AmigaBusRequester.Paula,
                Kind: AmigaBusAccessKind.PaulaDma,
                Cycle: secondLineStart + (0x10 * AgnusChipSlotScheduler.SlotCycles)),
            (
                Owner: AgnusChipSlotOwner.Sprite,
                Requester: AmigaBusRequester.Sprite,
                Kind: AmigaBusAccessKind.Sprite,
                Cycle: secondLineStart + (0x18 * AgnusChipSlotScheduler.SlotCycles)),
            (
                Owner: AgnusChipSlotOwner.Bitplane,
                Requester: AmigaBusRequester.Bitplane,
                Kind: AmigaBusAccessKind.Bitplane,
                Cycle: secondLineStart + (0x40 * AgnusChipSlotScheduler.SlotCycles))
        };
        foreach (var slot in fixedSlots)
        {
            Assert.True(bus.TryReserveDisplayDmaSlot(
                slot.Requester,
                slot.Kind,
                0x6000u + (uint)slot.Cycle,
                slot.Cycle,
                out var reservation));
            Assert.Equal(slot.Cycle, reservation.GrantedCycle);
        }

        _ = RunSustainedCpuContention(bus);

        AssertNoUnexplainedEligibleIdleSlots(bus);
        var blockedOwners = GetOwnersBlockingBlitter(bus);
        Assert.Contains(AgnusChipSlotOwner.Refresh, blockedOwners);
        Assert.Contains(AgnusChipSlotOwner.Copper, blockedOwners);
        foreach (var slot in fixedSlots)
        {
            Assert.True(
                bus.TryGetCommittedAgnusSlotOwner(
                    slot.Cycle,
                    out var owner));
            Assert.Equal(slot.Owner, owner);
            Assert.Contains(slot.Owner, blockedOwners);
        }

        AssertCompletedBlitPublished(
            bus,
            allowsPostCompletionPipelineDrain: true);
    }

    [Fact]
    public void LiveAudioDmaRetainsPriorityOverNastyBlitter()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureFastAudioDma(legacy);
        ConfigureFastAudioDma(candidate);
        ConfigureAreaMatrixFixture(
            legacy,
            widthWords: 32,
            height: 4,
            nasty: true);
        ConfigureAreaMatrixFixture(
            candidate,
            widthWords: 32,
            height: 4,
            nasty: true);

        _ = RunSustainedCpuContention(legacy);
        _ = RunSustainedCpuContention(candidate);

        AssertFunctionalStateEquivalent(
            legacy,
            candidate,
            wordCount: 128);
        Assert.Equal(
            legacy.Paula.GetChannelSnapshot(0),
            candidate.Paula.GetChannelSnapshot(0));
        var audio = candidate.BusAccesses
            .Where(access =>
                access.Request.Kind == AmigaBusAccessKind.PaulaDma)
            .ToArray();
        Assert.NotEmpty(audio);
        foreach (var access in audio)
        {
            Assert.True(candidate.TryGetCommittedAgnusSlotOwner(
                access.GrantedCycle,
                out var owner));
            Assert.Equal(AgnusChipSlotOwner.Paula, owner);
        }

        Assert.Contains(
            AgnusChipSlotOwner.Paula,
            GetOwnersBlockingBlitter(candidate));
        AssertNoUnexplainedEligibleIdleSlots(candidate);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
    }

    [Fact]
    public void LiveDiskDmaRetainsPriorityOverNastyBlitter()
    {
        const uint diskDestination = 0x7800;
        var bus = CreateBus(candidate: true);
        InsertSingleTrackDisk(bus, 0x1111, 0x2222);
        var readyCycle = SelectDriveAndAdvanceToMotorReady(bus);
        bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
        bus.Paula.AdvanceTo(readyCycle);
        WritePointer(bus, 0x00DFF020, diskDestination);
        bus.WriteWord(0x00DFF024, 0x8001, readyCycle);
        bus.WriteWord(0x00DFF024, 0x8001, readyCycle);
        var diskCompletion =
            bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
        var diskSlot =
            diskCompletion - AgnusChipSlotScheduler.SlotCycles;

        SeedSources(bus, wordCount: 136);
        ConfigureAreaRegisters(bus, 0x0FCA);
        EnableBlitterDma(bus, nasty: true);
        StartBlit(
            bus,
            widthWords: 32,
            height: 4,
            startCycle: Math.Max(
                readyCycle,
                diskSlot - 64));

        _ = RunSustainedCpuContention(bus);

        var diskAccess = Assert.Single(
            bus.BusAccesses,
            access =>
                access.Request.Kind == AmigaBusAccessKind.DiskDma);
        Assert.Equal(diskSlot, diskAccess.GrantedCycle);
        Assert.True(bus.TryGetCommittedAgnusSlotOwner(
            diskSlot,
            out var owner));
        Assert.Equal(AgnusChipSlotOwner.Disk, owner);
        Assert.Contains(
            AgnusChipSlotOwner.Disk,
            GetOwnersBlockingBlitter(bus));
        AssertNoUnexplainedEligibleIdleSlots(bus);
        Assert.Equal(
            (ushort)0x1111,
            BigEndian.ReadUInt16(
                bus.ChipRam,
                (int)diskDestination,
                "G3L disk priority result"));
        Assert.NotEqual(
            0,
            bus.Paula.Intreq & AmigaConstants.IntreqBlitter);
        AssertCompletedBlitPublished(
            bus,
            allowsPostCompletionPipelineDrain: true);
    }

    [Fact]
    public void CopperCanEnableBltpriMidBlitAndCpuLosesOnlyFutureEligibleSlots()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureCopperBltpriToggleFixture(
            legacy,
            initiallyNasty: false,
            toggleValue: 0x8400);
        ConfigureCopperBltpriToggleFixture(
            candidate,
            initiallyNasty: false,
            toggleValue: 0x8400);

        _ = RunSustainedCpuContention(legacy);
        _ = RunSustainedCpuContention(candidate);

        AssertFunctionalStateEquivalent(
            legacy,
            candidate,
            wordCount: 128);
        var toggleCycle = GetCustomWriteCycle(
            candidate,
            address: 0x096,
            value: 0x8400);
        var accesses = BlitterAccesses(candidate);
        Assert.Contains(
            accesses,
            access => access.GrantedCycle < toggleCycle);
        Assert.Contains(
            accesses,
            access => access.GrantedCycle > toggleCycle);
        var priorityEffectCycle =
            toggleCycle +
            (CopperMod.Amiga.Bus.Bus.DmaconBltpriVisibilityDelaySlots *
             AgnusChipSlotScheduler.SlotCycles);
        AssertNoUnexplainedEligibleIdleSlots(
            candidate,
            firstEnforcedCycle: priorityEffectCycle);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        var waitRestartSlot =
            0x40L * AgnusChipSlotScheduler.SlotCycles;
        var firstMoveWord = Assert.Single(
            candidate.BusAccesses,
            access =>
                access.Request.Requester == AmigaBusRequester.Copper &&
                access.Request.Address == CopperList + 4);
        Assert.Equal(waitRestartSlot, firstMoveWord.RequestedCycle);
        Assert.Equal(waitRestartSlot, firstMoveWord.GrantedCycle);
        Assert.True(
            candidate.TryGetCommittedAgnusSlotOwner(
                waitRestartSlot,
                out var waitRestartOwner));
        Assert.Equal(AgnusChipSlotOwner.Copper, waitRestartOwner);
        Assert.Contains(
            accesses,
            access =>
                access.RequestedCycle == waitRestartSlot &&
                access.GrantedCycle > waitRestartSlot);
        Assert.InRange(
            diagnostics.NastyGrants,
            1,
            diagnostics.GrantedRequests - 1);
        Assert.NotEqual(0, candidate.Paula.Dmacon & 0x0400);
        AssertNoCollidingPhysicalSlots(candidate);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
    }

    [Fact]
    public void CopperCanDisableBltpriMidBlitAndCpuRegainsThreeMissYield()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureCopperBltpriToggleFixture(
            legacy,
            initiallyNasty: true,
            toggleValue: 0x0400);
        ConfigureCopperBltpriToggleFixture(
            candidate,
            initiallyNasty: true,
            toggleValue: 0x0400);

        _ = RunSustainedCpuContention(legacy);
        _ = RunSustainedCpuContention(candidate);

        AssertFunctionalStateEquivalent(
            legacy,
            candidate,
            wordCount: 128);
        var toggleCycle = GetCustomWriteCycle(
            candidate,
            address: 0x096,
            value: 0x0400);
        var completionCycle = candidate.Blitter.LastCompletionCycle;
        var cpuAccesses = candidate.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu &&
                access.GrantedCycle > toggleCycle &&
                access.GrantedCycle < completionCycle)
            .ToArray();
        var cpuGrants = cpuAccesses
            .Select(access => access.GrantedCycle)
            .ToArray();
        Assert.True(
            cpuGrants.Length > 0,
            $"toggle={toggleCycle}, completion={completionCycle}, " +
            $"dmacon={candidate.Paula.Dmacon:X4}, " +
            $"blitter=[{string.Join(",", BlitterSignature(candidate))}], " +
            $"slots=[{SlotSignature(candidate, completionCycle)}]");
        var cpuGrant = cpuGrants[0];
        for (var cycle = cpuGrant -
                 (3 * AgnusChipSlotScheduler.SlotCycles);
             cycle < cpuGrant;
             cycle += AgnusChipSlotScheduler.SlotCycles)
        {
            var owned = candidate.TryGetCommittedAgnusSlotOwner(
                cycle,
                out var owner);
            Assert.True(
                owned,
                $"CPU grant {cpuGrant} did not follow three occupied DMA " +
                $"opportunities; missing={cycle}, toggle={toggleCycle}, " +
                $"slots=[{SlotSignature(candidate, cpuGrant)}].");
            Assert.NotEqual(AgnusChipSlotOwner.Free, owner);
            Assert.NotEqual(AgnusChipSlotOwner.Cpu, owner);
        }

        AssertCpuAndBlitterSlotsAreExclusive(candidate, cpuAccesses);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.InRange(
            diagnostics.NastyGrants,
            1,
            diagnostics.GrantedRequests - 1);
        Assert.Equal(0, candidate.Paula.Dmacon & 0x0400);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
    }

    [Fact]
    public void BltpriBecomesVisibleTwoDmaCyclesAfterDmaconTransfer()
    {
        var bus = CreateBus(candidate: true);

        bus.WriteWord(0x00DFF096, 0x8400, 20);
        var transferCycle = GetCustomWriteCycle(
            bus,
            address: 0x096,
            value: 0x8400);
        var effectCycle =
            transferCycle +
            (CopperMod.Amiga.Bus.Bus.DmaconBltpriVisibilityDelaySlots *
             AgnusChipSlotScheduler.SlotCycles);

        Assert.Equal(0, bus.Paula.Dmacon & 0x0400);
        bus.Paula.AdvanceRegisterObservableTo(effectCycle - 1);
        Assert.Equal(0, bus.Paula.Dmacon & 0x0400);
        bus.Paula.AdvanceRegisterObservableTo(effectCycle);
        Assert.NotEqual(0, bus.Paula.Dmacon & 0x0400);
    }

    [Fact]
    public void BfdWaitFetchesNextInstructionThreeSlotsAfterFinalBlitterDrain()
    {
        var bus = CreateBus(candidate: true);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList, 0x0041);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 2, 0x7FFE);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 4, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 6, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 8, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 10, 0xFFFE);
        WritePointer(bus, 0x00DFF080, CopperList);
        bus.WriteWord(0x00DFF088, 0);
        bus.WriteWord(0x00DFF096, 0x8280);
        ConfigureAreaMatrixFixture(
            bus,
            widthWords: 32,
            height: 4,
            nasty: false,
            startCycle: AgnusChipSlotScheduler.SlotCycles);

        RunUntilIdle(bus);

        var finalBlitterDrain = BlitterAccesses(bus).Last();
        var expectedFetchCycle =
            finalBlitterDrain.GrantedCycle +
            (2L * AgnusChipSlotScheduler.SlotCycles);
        while (!AgnusHrmOcsSlotTable.IsCopperAccessSlot(expectedFetchCycle))
        {
            expectedFetchCycle += AgnusChipSlotScheduler.SlotCycles;
        }
        var firstPostWaitFetch = bus.BusAccesses
            .First(access =>
                access.Request.Requester == AmigaBusRequester.Copper &&
                access.Request.Address == CopperList + 4 &&
                access.GrantedCycle > finalBlitterDrain.GrantedCycle);
        Assert.Equal(
            expectedFetchCycle,
            firstPostWaitFetch.GrantedCycle);
        AssertNoCollidingPhysicalSlots(bus);
    }

    [Fact]
    public void NiceBlitterTakenBnePublishesTargetAfterFallthroughAndInternalSlot()
    {
        var bus = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(
            bus,
            widthWords: 32,
            height: 4,
            nasty: false);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x3413);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x0242);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1006, 0x0C42);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1008, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x100A, 0x66F4);
        BigEndian.WriteUInt16(bus.ChipRam, 0x100C, 0x4E71);
        var cpu = AmigaM68kCoreFactory.Default.Create(
            M68kBackendKind.AccurateM68000,
            bus);
        cpu.Reset(0x1000, 0x2F00);
        cpu.State.Cycles = AgnusChipSlotScheduler.AlignToSlot(
            bus.CausalBusExecutor.ExecutedThroughCycle + 1);
        cpu.State.A[3] = 0x00DFF006;

        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();

        var cpuAccesses = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Cpu &&
                access.Request.Kind ==
                    AmigaBusAccessKind.CpuInstructionFetch)
            .ToArray();
        var target = cpuAccesses.Last(access =>
            access.Request.Address == 0x1000);
        var fallthrough = cpuAccesses
            .Where(access =>
                access.Request.Address == 0x100C &&
                access.GrantedCycle < target.GrantedCycle)
            .Last();

        // A taken short branch completes the queued fall-through fetch, spends
        // one internal slot, and then publishes the target fetch. Measure from
        // completion so the assertion does not count the fall-through bus slot
        // itself as an additional internal slot.
        Assert.Equal(
            fallthrough.CompletedCycle + AgnusChipSlotScheduler.SlotCycles,
            target.Request.RequestedCycle);
        Assert.Equal(
            3 * AgnusChipSlotScheduler.SlotCycles,
            target.GrantedCycle - target.Request.RequestedCycle);
        Assert.True(bus.Blitter.Busy);
    }

    [Fact]
    public void CopperBltpriClearReleasesPendingCpuCustomRegisterWriteBeforeBlitCompletion()
    {
        var bus = CreateBus(candidate: true);
        ConfigureCopperBltpriToggleFixture(
            bus,
            initiallyNasty: true,
            toggleValue: 0x0400);

        var requestedCycle = bus.Blitter.GetRawBusEligibilityCycle();
        bus.WriteWord(0x00DFF180, 0x0123, requestedCycle);

        var toggleCycle = GetCustomWriteCycle(
            bus,
            address: 0x096,
            value: 0x0400);
        var cpuWriteCycle = GetCustomWriteCycle(
            bus,
            address: 0x180,
            value: 0x0123);
        Assert.Equal(
            toggleCycle +
            ((CopperMod.Amiga.Bus.Bus.DmaconBltpriVisibilityDelaySlots + 1) *
             AgnusChipSlotScheduler.SlotCycles),
            cpuWriteCycle);
        Assert.True(bus.Blitter.Busy);
    }

    [Fact]
    public void CopperBltpriClearKeepsReleasingSubsequentCpuChipWritesBeforeBlitCompletion()
    {
        var bus = CreateBus(candidate: true);
        ConfigureCopperBltpriToggleFixture(
            bus,
            initiallyNasty: true,
            toggleValue: 0x0400);
        var cycle = 2L;

        _ = bus.ReadWord(
            SourceA,
            ref cycle,
            AmigaBusAccessKind.CpuInstructionFetch);
        Assert.True(bus.Blitter.Busy);
        bus.WriteWord(0x00001000, 0x0123, cycle);

        Assert.True(bus.Blitter.Busy);
    }

    [Fact]
    public void CpuTimingSequenceClearsRequestPublishedByMidSequenceBltpriDisable()
    {
        var bus = CreateBus(candidate: true);
        ConfigureCopperBltpriToggleFixture(
            bus,
            initiallyNasty: true,
            toggleValue: 0x0400);
        var request = new CpuTimingSequenceRequest(
            AmigaBusAccessKind.CpuInstructionFetch,
            AmigaBusAccessTarget.ExpansionRam,
            AmigaConstants.A500BootPseudoFastRamBase,
            firstRequestedCycle: 2,
            wordCount: 8,
            isWrite: false,
            instructionFetchShapeBits: 0xFF);

        Assert.True(
            bus.CausalBusExecutor.TryExecuteCpuTimingSequence(
                request,
                out var result));

        var toggleCycle = GetCustomWriteCycle(
            bus,
            address: 0x096,
            value: 0x0400);
        Assert.InRange(
            toggleCycle,
            result.FirstGrantedCycle + 1,
            result.LastGrantedCycle);
        Assert.True(bus.Blitter.Busy);
        Assert.False(
            bus.CausalBusExecutor.PendingCpuRequestPublishedToHrm);
    }

    [Fact]
    public void CopperTriggeredBlitAndFixedDisplayContentionMatchLegacy()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureCopperDisplayBlitFixture(legacy);
        ConfigureCopperDisplayBlitFixture(candidate);
        var target = 48L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

        legacy.AdvanceDmaTo(target);
        candidate.AdvanceDmaTo(target);
        RunUntilIdle(legacy, target + 2_000);
        RunUntilIdle(candidate, target + 2_000);

        AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 16);
        AssertAreaChannelProgram(candidate, channelMask: 0xF, wordCount: 16);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
        Assert.Equal(
            FixedDisplaySignature(legacy),
            FixedDisplaySignature(candidate));
        Assert.Equal(CopperSignature(legacy), CopperSignature(candidate));
        Assert.True(candidate.AgnusLiveBlitterDiagnostics.DeniedRequests > 0);
    }

    [Fact]
    public void MidOperationBltcon0MutationCancelsOnlyFutureDestinationIntent()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        foreach (var bus in new[] { legacy, candidate })
        {
            ConfigureAreaMatrixFixture(bus, widthWords: 16, height: 1);
            bus.AdvanceDmaTo(52);
            bus.Blitter.WriteRegister(0x040, 0x0ECA, 52);
        }

        RunUntilIdle(legacy);
        RunUntilIdle(candidate);

        AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 16);
        var destinationWrites = BlitterAccesses(candidate)
            .Where(access => access.Request.IsWrite)
            .ToArray();
        Assert.Equal(5, destinationWrites.Length);
        Assert.Equal(
            Enumerable.Range(0, 5)
                .Select(index => DestinationD + (uint)(index * 2)),
            destinationWrites.Select(access => access.Request.Address));
        AssertCompletedBlitPublished(candidate);
    }

    [Fact]
    public void CompletionBusyZeroAndInterruptPublicationMatchLegacy()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(legacy);
        ConfigureAreaMatrixFixture(candidate);

        RunUntilIdle(legacy);
        RunUntilIdle(candidate);

        Assert.Equal(legacy.Blitter.LastCompletionCycle, candidate.Blitter.LastCompletionCycle);
        Assert.Equal(
            legacy.Paula.GetHighestPendingInterruptLevel(),
            candidate.Paula.GetHighestPendingInterruptLevel());
        Assert.Equal(
            legacy.ReadWord(0x00DFF002) & 0x6000,
            candidate.ReadWord(0x00DFF002) & 0x6000);
        var diagnostics = candidate.AgnusLiveBlitterDiagnostics;
        Assert.Equal(1, diagnostics.Completions);
        Assert.Equal(1, diagnostics.Interrupts);
        Assert.Equal(0, diagnostics.ContractViolations);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void WarmedCandidateExecutionAllocatesNoManagedMemory()
    {
        _ = RunAllocationFixture(measure: false);

        var allocated = RunAllocationFixture(measure: true);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void G3LSeparateProcessEvidenceProbe()
    {
        var mode = Environment.GetEnvironmentVariable(EvidenceModeVariable);
        var outputPath =
            Environment.GetEnvironmentVariable(EvidenceOutputVariable);
        if (string.IsNullOrWhiteSpace(mode) ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var candidate = string.Equals(
            mode,
            "candidate",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(
            candidate ||
            string.Equals(mode, "legacy", StringComparison.OrdinalIgnoreCase),
            $"Unsupported G3L evidence mode '{mode}'.");
        var bus = CreateBus(candidate);
        ConfigureCopperDisplayBlitFixture(bus);
        var target =
            48L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
        bus.AdvanceDmaTo(target);
        RunUntilIdle(bus, target + 2_000);
        var diagnostics = bus.AgnusLiveBlitterDiagnostics;

        var lineBus = CreateBus(candidate);
        ConfigureLineFixture(lineBus);
        RunUntilIdle(lineBus);
        var lineDiagnostics = lineBus.AgnusLiveBlitterDiagnostics;
        File.WriteAllLines(
            outputPath,
            [
                $"blitter_access_hash={HashStrings(BlitterSignature(bus)):X16}",
                $"blitter_access_count={BlitterAccesses(bus).Length}",
                $"destination_hash={HashWords(ReadWords(bus, DestinationD, 8)):X16}",
                $"snapshot_hash={HashStrings([SnapshotSignature(bus)]):X16}",
                $"completion_cycle={bus.Blitter.LastCompletionCycle}",
                $"interrupt_level={bus.Paula.GetHighestPendingInterruptLevel()}",
                $"fixed_hash={HashStrings(FixedDisplaySignature(bus)):X16}",
                $"copper_hash={HashStrings(CopperSignature(bus)):X16}",
                $"line_access_hash={HashStrings(BlitterSignature(lineBus)):X16}",
                $"line_access_count={BlitterAccesses(lineBus).Length}",
                $"line_destination_hash={HashWords(ReadWords(lineBus, DestinationD, 64)):X16}",
                $"line_snapshot_hash={HashStrings([SnapshotSignature(lineBus)]):X16}",
                $"line_completion_cycle={lineBus.Blitter.LastCompletionCycle}",
                $"line_interrupt_level={lineBus.Paula.GetHighestPendingInterruptLevel()}",
                $"live_requests={diagnostics.PublishedRequests + lineDiagnostics.PublishedRequests}",
                $"live_grants={diagnostics.GrantedRequests + lineDiagnostics.GrantedRequests}",
                $"live_denials={diagnostics.DeniedRequests + lineDiagnostics.DeniedRequests}",
                $"live_reads={diagnostics.GrantedReads + lineDiagnostics.GrantedReads}",
                $"live_writes={diagnostics.GrantedWrites + lineDiagnostics.GrantedWrites}",
                $"live_area_ops={diagnostics.AreaMicroOps + lineDiagnostics.AreaMicroOps}",
                $"live_line_ops={diagnostics.LineMicroOps + lineDiagnostics.LineMicroOps}",
                $"live_transitions={diagnostics.CommittedTransitions + lineDiagnostics.CommittedTransitions}",
                $"live_completions={diagnostics.Completions + lineDiagnostics.Completions}",
                $"live_interrupts={diagnostics.Interrupts + lineDiagnostics.Interrupts}",
                $"contract_violations={diagnostics.ContractViolations + lineDiagnostics.ContractViolations}",
                $"forbidden_calls={diagnostics.ForbiddenCalls + lineDiagnostics.ForbiddenCalls}"
            ]);
    }

    private static void AssertNiceCpuContentionParity()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureAreaMatrixFixture(
            legacy,
            widthWords: 4,
            height: 1,
            nasty: false,
            startCycle: 596);
        ConfigureAreaMatrixFixture(
            candidate,
            widthWords: 4,
            height: 1,
            nasty: false,
            startCycle: 596);
        // Begin at the first common unexecuted slot. The live stages may have
        // materialized the timestamped BLTSIZE write farther than the legacy
        // path, and a CPU request behind that horizon would be retroactive.
        var firstCpuCycle = AgnusChipSlotScheduler.AlignToSlot(
            Math.Max(
                legacy.CausalBusExecutor.ExecutedThroughCycle,
                candidate.CausalBusExecutor.ExecutedThroughCycle) + 1);
        var legacyCycle = firstCpuCycle;
        var candidateCycle = firstCpuCycle;

        var legacyValue = legacy.ReadWord(
            SourceA,
            ref legacyCycle,
            AmigaBusAccessKind.CpuDataRead);
        var candidateValue = candidate.ReadWord(
            SourceA,
            ref candidateCycle,
            AmigaBusAccessKind.CpuDataRead);

        Assert.Equal(legacyValue, candidateValue);
        Assert.True(
            legacyCycle == candidateCycle,
            $"legacyCycle={legacyCycle}, " +
            $"candidateCycle={candidateCycle}, " +
            $"legacySnapshot={SnapshotSignature(legacy)}, " +
            $"candidateSnapshot={SnapshotSignature(candidate)}, " +
            $"legacyTrace=[{string.Join(",", BlitterSignature(legacy))}], " +
            $"candidateTrace=[{string.Join(",", BlitterSignature(candidate))}], " +
            $"legacySlots=[{SlotSignature(legacy, 64)}], " +
            $"candidateSlots=[{SlotSignature(candidate, 64)}], " +
            $"diagnostics={candidate.AgnusLiveBlitterDiagnostics}");
        RunUntilIdle(legacy);
        RunUntilIdle(candidate);
        AssertFunctionalStateEquivalent(legacy, candidate, wordCount: 4);
        AssertAreaChannelProgram(candidate, channelMask: 0xF, wordCount: 4);
        AssertCompletedBlitPublished(
            candidate,
            allowsPostCompletionPipelineDrain: true);
        Assert.Equal(0, candidate.AgnusLiveBlitterDiagnostics.NastyGrants);
    }

    private static long RunAllocationFixture(bool measure)
    {
        var bus = CreateBus(candidate: true, captureBusAccesses: false);
        ConfigureAreaMatrixFixture(bus, widthWords: 4, height: 1);
        RunUntilIdle(bus, 10_000);
        ConfigureAreaMatrixFixture(bus, widthWords: 32, height: 4);
        if (!measure)
        {
            RunUntilIdle(bus, 100_000);
            return 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        bus.AdvanceDmaTo(100_000);
        var allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(bus.Blitter.Busy);
        return allocated;
    }

    private static void ConfigureChannelFixture(
        AmigaBus bus,
        int channelMask,
        int widthWords = 2,
        int height = 1,
        bool nasty = false,
        long startCycle = 0)
    {
        SeedSources(bus, wordCount: widthWords * height);
        ConfigureAreaRegisters(
            bus,
            (ushort)((channelMask << 8) | 0xCA));
        EnableBlitterDma(bus, nasty);
        StartBlit(bus, widthWords, height, startCycle);
    }

    private static void ConfigureAreaMatrixFixture(
        AmigaBus bus,
        int widthWords = 4,
        int height = 2,
        bool nasty = false,
        long startCycle = 0)
    {
        SeedSources(bus, widthWords * height + 8);
        ConfigureAreaRegisters(bus, 0x0FCA);
        bus.WriteWord(0x00DFF044, 0x0FF0);
        bus.WriteWord(0x00DFF046, 0xF00F);
        bus.WriteWord(0x00DFF064, 2);
        bus.WriteWord(0x00DFF062, 4);
        bus.WriteWord(0x00DFF060, 6);
        bus.WriteWord(0x00DFF066, 8);
        EnableBlitterDma(bus, nasty);
        StartBlit(bus, widthWords, height, startCycle);
    }

    private static void ConfigureDescendingFillFixture(AmigaBus bus)
    {
        SeedSources(bus, wordCount: 8);
        ConfigureAreaRegisters(
            bus,
            bltcon0: 0x09F0,
            bltcon1: 0x0012,
            sourceA: SourceA + 6,
            destinationD: DestinationD + 6);
        EnableBlitterDma(bus);
        StartBlit(bus, widthWords: 4, height: 1);
    }

    private static void ConfigureLineFixture(
        AmigaBus bus,
        bool nasty = false,
        long startCycle = 0)
    {
        const ushort rowStride = 0x20;
        SeedSources(bus, wordCount: 64);
        ConfigureAreaRegisters(
            bus,
            bltcon0: 0x0BCA,
            bltcon1: 0x0001);
        WritePointer(bus, 0x00DFF048, DestinationD);
        WritePointer(bus, 0x00DFF054, DestinationD);
        bus.WriteWord(0x00DFF060, rowStride);
        bus.WriteWord(0x00DFF062, 0);
        bus.WriteWord(0x00DFF064, 0);
        bus.WriteWord(0x00DFF066, rowStride);
        bus.WriteWord(0x00DFF072, 0xFFFF);
        bus.WriteWord(0x00DFF074, 0x8000);
        EnableBlitterDma(bus, nasty);
        StartBlit(bus, bltSize: 0x0104, startCycle);
    }

    private static void ConfigureCopperDisplayBlitFixture(AmigaBus bus)
    {
        SeedSources(bus, wordCount: 16);
        ConfigureAreaRegisters(bus, 0x0FCA);
        EnableBlitterDma(bus);
        ConfigureDisplay(bus);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList, 0x0058);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 2, 0x0088);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 4, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList + 6, 0xFFFE);
        WritePointer(bus, 0x00DFF080, CopperList);
        bus.WriteWord(0x00DFF088, 0);
        bus.WriteWord(0x00DFF096, 0x8280);
    }

    private static void ConfigureCopperHaltList(AmigaBus bus)
    {
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList,
            0xFFFF);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList + 2,
            0xFFFE);
        WritePointer(bus, 0x00DFF080, CopperList);
        bus.WriteWord(0x00DFF088, 0);
        bus.WriteWord(0x00DFF096, 0x8280);
    }

    private static void ConfigureCopperBltpriToggleFixture(
        AmigaBus bus,
        bool initiallyNasty,
        ushort toggleValue)
    {
        SeedSources(bus, wordCount: 136);
        ConfigureAreaRegisters(bus, 0x0FCA);
        EnableBlitterDma(bus, initiallyNasty);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList,
            0x0041);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList + 2,
            0xFFFE);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList + 4,
            0x0096);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList + 6,
            toggleValue);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList + 8,
            0xFFFF);
        BigEndian.WriteUInt16(
            bus.ChipRam,
            (int)CopperList + 10,
            0xFFFE);
        WritePointer(bus, 0x00DFF080, CopperList);
        bus.WriteWord(0x00DFF088, 0);
        bus.WriteWord(0x00DFF096, 0x8280);
        StartBlit(bus, widthWords: 32, height: 4);
    }

    private static void ConfigureFastAudioDma(AmigaBus bus)
    {
        const uint audioSource = 0x7000;
        for (var offset = 0; offset < 256; offset += 2)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)audioSource + offset,
                (ushort)(0x4000 + offset));
        }

        bus.WriteWord(0x00DFF0A0, (ushort)(audioSource >> 16), 0);
        bus.WriteWord(0x00DFF0A2, (ushort)audioSource, 0);
        bus.WriteWord(0x00DFF0A4, 0x0080, 0);
        bus.WriteWord(0x00DFF0A6, 0x0002, 0);
        bus.WriteWord(0x00DFF096, 0x8201, 0);
        bus.Paula.AdvanceTo(0);
    }

    private static void InsertSingleTrackDisk(
        AmigaBus bus,
        params ushort[] words)
    {
        var data = new byte[Math.Max(1, words.Length) * 2];
        for (var index = 0; index < words.Length; index++)
        {
            BigEndian.WriteUInt16(
                data,
                index * 2,
                words[index]);
        }

        var blank = AmigaEncodedTrack.FromBytes(
            new byte[] { 0xAA, 0xAA });
        var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
        Array.Fill(tracks, blank);
        tracks[0] = AmigaEncodedTrack.FromBytes(data);
        bus.Disk.Drive0.Insert(
            AmigaDiskImage.FromEncodedTracks(tracks));
    }

    private static long SelectDriveAndAdvanceToMotorReady(
        AmigaBus bus)
    {
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        var readyCycle = 10 +
            Math.Max(
                1,
                (long)Math.Round(
                    AmigaConstants.A500PalCpuClockHz * 0.5));
        bus.AdvanceDmaTo(readyCycle);
        return readyCycle;
    }

    private static void ConfigureDisplay(AmigaBus bus)
    {
        const uint bitplane = 0x5000;
        for (var offset = 0; offset < 512; offset += 2)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)bitplane + offset,
                (ushort)(0xA500 ^ offset));
        }

        WritePointer(bus, 0x00DFF0E0, bitplane);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF08E, 0x2C81);
        bus.WriteWord(0x00DFF090, 0xF4C1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF096, 0x8300);
    }

    private static void ConfigureAreaRegisters(
        AmigaBus bus,
        ushort bltcon0,
        ushort bltcon1 = 0,
        uint sourceA = SourceA,
        uint sourceB = SourceB,
        uint sourceC = SourceC,
        uint destinationD = DestinationD)
    {
        bus.WriteWord(0x00DFF040, bltcon0);
        bus.WriteWord(0x00DFF042, bltcon1);
        WritePointer(bus, 0x00DFF050, sourceA);
        WritePointer(bus, 0x00DFF04C, sourceB);
        WritePointer(bus, 0x00DFF048, sourceC);
        WritePointer(bus, 0x00DFF054, destinationD);
    }

    private static void SeedSources(AmigaBus bus, int wordCount)
    {
        for (var index = 0; index < wordCount; index++)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)SourceA + (index * 2),
                (ushort)(0x1100 + index));
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)SourceB + (index * 2),
                (ushort)(0x2200 + (index * 3)));
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)SourceC + (index * 2),
                (ushort)(0x3300 + (index * 5)));
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)DestinationD + (index * 2),
                0x5A5A);
        }
    }

    private static void EnableBlitterDma(AmigaBus bus, bool nasty = false)
        => bus.WriteWord(
            0x00DFF096,
            (ushort)(0x8240 | (nasty ? 0x0400 : 0)));

    private static void StartBlit(
        AmigaBus bus,
        int widthWords,
        int height,
        long startCycle = 0)
        => StartBlit(
            bus,
            (ushort)((height << 6) | (widthWords & 0x3F)),
            startCycle);

    private static void StartBlit(
        AmigaBus bus,
        ushort bltSize,
        long startCycle = 0)
    {
        if (startCycle == 0)
        {
            bus.WriteWord(0x00DFF058, bltSize);
            return;
        }

        bus.Blitter.WriteRegister(0x058, bltSize, startCycle);
    }

    private static int RunSustainedCpuContention(
        AmigaBus bus,
        int maximumReads = 4_096)
    {
        var cycle = Math.Max(
            0,
            bus.Blitter.GetRawBusEligibilityCycle());
        var reads = 0;
        // OCS can clear BBUSY before a denied, pipelined final D write reaches
        // the bus. Sustained CPU contention must continue through that drain;
        // stopping at the architectural completion edge can leave the final
        // destination word legitimately pending and make parity look corrupt.
        while (bus.Blitter.BusPipelineActive && reads < maximumReads)
        {
            _ = bus.ReadWord(
                0x1000u + (uint)((reads & 0x1F) * 2),
                ref cycle,
                AmigaBusAccessKind.CpuInstructionFetch);
            reads++;
        }

        Assert.False(
            bus.Blitter.BusPipelineActive,
            $"BLTPRI contention did not complete after {reads} CPU reads; " +
            $"cycle={cycle}, snapshot={SnapshotSignature(bus)}.");
        return reads;
    }

    private static void AssertNoUnexplainedEligibleIdleSlots(
        AmigaBus bus,
        long firstEnforcedCycle = 0)
    {
        var afterSlotTransitions =
            bus.Blitter.LiveAfterSlotTransitionCycles.ToHashSet();
        foreach (var access in BlitterAccesses(bus))
        {
            var firstCycle = AgnusChipSlotScheduler.AlignToSlot(
                Math.Max(
                    access.RequestedCycle,
                    firstEnforcedCycle));
            for (var cycle = firstCycle;
                 cycle < access.GrantedCycle;
                 cycle += AgnusChipSlotScheduler.SlotCycles)
            {
                // An after-slot transition can only publish the next DMA
                // intent after this cycle's owner has already been selected.
                // Such a cycle is not an eligible opportunity for that newly
                // published request.
                if (afterSlotTransitions.Contains(cycle))
                {
                    continue;
                }

                Assert.True(
                    bus.TryGetCommittedAgnusSlotOwner(
                        cycle,
                        out var owner) &&
                    owner is not (
                        AgnusChipSlotOwner.Free or
                        AgnusChipSlotOwner.Cpu or
                        AgnusChipSlotOwner.Blitter),
                    $"BLTPRI left eligible cycle {cycle} unused: " +
                    $"request={access.RequestedCycle}, " +
                    $"grant={access.GrantedCycle}, " +
                    $"transitions=[{string.Join(
                        ",",
                        bus.Blitter.LiveAfterSlotTransitionCycles.Where(
                            transition =>
                                transition >= access.RequestedCycle - 8 &&
                                transition <= access.GrantedCycle))}], " +
                    $"slots=[{SlotWindowSignature(
                        bus,
                        access.RequestedCycle - 8,
                        access.GrantedCycle)}].");
            }
        }
    }

    private static HashSet<AgnusChipSlotOwner> GetOwnersBlockingBlitter(
        AmigaBus bus)
    {
        var owners = new HashSet<AgnusChipSlotOwner>();
        foreach (var access in BlitterAccesses(bus))
        {
            for (var cycle = AgnusChipSlotScheduler.AlignToSlot(
                     access.RequestedCycle);
                 cycle < access.GrantedCycle;
                 cycle += AgnusChipSlotScheduler.SlotCycles)
            {
                if (bus.TryGetCommittedAgnusSlotOwner(
                    cycle,
                    out var owner))
                {
                    owners.Add(owner);
                }
            }
        }

        return owners;
    }

    private static long GetBOnlyInternalPauseDelay(AmigaBus bus)
    {
        var delay = 0L;
        foreach (var access in BlitterAccesses(bus))
        {
            // The HRM/vAmiga B-only sequence is B,--,--. The two internal
            // sequencer slots following each B read pause behind fixed DMA
            // even though no memory transfer is pending in those slots.
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(
                access.CompletedCycle);
            var phaseEnd =
                access.CompletedCycle +
                (2 * AgnusChipSlotScheduler.SlotCycles);
            while (slotCycle < phaseEnd)
            {
                var paused =
                    bus.IsMandatoryRefreshSlot(slotCycle) ||
                    (bus.TryGetCommittedAgnusSlotOwner(
                         slotCycle,
                         out var owner) &&
                     owner is AgnusChipSlotOwner.Copper or
                         AgnusChipSlotOwner.Paula or
                         AgnusChipSlotOwner.Disk or
                         AgnusChipSlotOwner.Sprite or
                         AgnusChipSlotOwner.Bitplane);
                if (paused)
                {
                    phaseEnd += AgnusChipSlotScheduler.SlotCycles;
                    delay += AgnusChipSlotScheduler.SlotCycles;
                }

                slotCycle += AgnusChipSlotScheduler.SlotCycles;
            }
        }

        return delay;
    }

    private static void AssertCompletionMatchesNominalAndDeniedSlots(
        AmigaBus bus,
        long nominalCompletion,
        long explainedInternalPause = 0)
    {
        var diagnostics = bus.AgnusLiveBlitterDiagnostics;
        var expected =
            nominalCompletion +
            (diagnostics.DeniedRequests *
             AgnusChipSlotScheduler.SlotCycles) +
            explainedInternalPause;
        Assert.True(
            expected == bus.Blitter.LastCompletionCycle,
            $"nominal={nominalCompletion}, denied={diagnostics.DeniedRequests}, " +
            $"internalPause={explainedInternalPause}, expected={expected}, " +
            $"actual={bus.Blitter.LastCompletionCycle}, " +
            $"accesses=[{string.Join(",", BlitterSignature(bus))}]");
    }

    private static long GetCustomWriteCycle(
        AmigaBus bus,
        ushort address,
        ushort value)
        => Assert.Single(
            bus.CustomRegisterWrites,
            write =>
                write.Address == address &&
                write.Value == value).Cycle;

    private static void RunUntilIdle(
        AmigaBus bus,
        long targetCycle = 1_000_000)
    {
        bus.AdvanceDmaTo(targetCycle);
        Assert.False(
            bus.Blitter.Busy,
            $"snapshot={SnapshotSignature(bus)}, diagnostics={bus.AgnusLiveBlitterDiagnostics}");
    }

    private static void AssertEquivalent(
        AmigaBus legacy,
        AmigaBus candidate,
        uint destination,
        int wordCount)
    {
        var legacyBlitter = BlitterSignature(legacy);
        var candidateBlitter = BlitterSignature(candidate);
        Assert.True(
            legacyBlitter.SequenceEqual(candidateBlitter),
            $"legacy=[{string.Join(",", legacyBlitter)}], " +
            $"candidate=[{string.Join(",", candidateBlitter)}], " +
            $"diagnostics={candidate.AgnusLiveBlitterDiagnostics}");
        Assert.Equal(
            ReadWords(legacy, destination, wordCount),
            ReadWords(candidate, destination, wordCount));
        Assert.Equal(
            SnapshotSignature(legacy),
            SnapshotSignature(candidate));
        Assert.Equal(
            legacy.Blitter.LastCompletionCycle,
            candidate.Blitter.LastCompletionCycle);
        Assert.True(
            legacy.Blitter.CompletionCycles.SequenceEqual(
                candidate.Blitter.CompletionCycles),
            $"legacy completion=[{string.Join(",", legacy.Blitter.CompletionCycles)}], " +
            $"candidate completion=[{string.Join(",", candidate.Blitter.CompletionCycles)}]");
        Assert.Equal(
            legacy.Paula.Intreq & AmigaConstants.IntreqBlitter,
            candidate.Paula.Intreq & AmigaConstants.IntreqBlitter);
        Assert.Equal(
            legacy.Paula.GetHighestPendingInterruptLevel(),
            candidate.Paula.GetHighestPendingInterruptLevel());
    }

    private static void AssertFunctionalStateEquivalent(
        AmigaBus legacy,
        AmigaBus candidate,
        int wordCount)
    {
        var expectedWords =
            ReadWords(legacy, DestinationD, wordCount);
        var actualWords =
            ReadWords(candidate, DestinationD, wordCount);
        Assert.True(
            expectedWords.SequenceEqual(actualWords),
            $"destination mismatch; " +
            $"legacyPipeline={legacy.Blitter.BusPipelineActive}, " +
            $"candidatePipeline={candidate.Blitter.BusPipelineActive}, " +
            $"legacyCompletion={legacy.Blitter.LastCompletionCycle}, " +
            $"candidateCompletion={candidate.Blitter.LastCompletionCycle}, " +
            $"candidateBlitter=[{string.Join(",", BlitterSignature(candidate).TakeLast(12))}], " +
            $"candidateSlots=[{SlotSignature(candidate, candidate.Blitter.LastCompletionCycle)}], " +
            $"diagnostics={candidate.AgnusLiveBlitterDiagnostics}");
        var expected = legacy.Blitter.CaptureSnapshot();
        var actual = candidate.Blitter.CaptureSnapshot();
        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.Bltcon0, actual.Bltcon0);
        Assert.Equal(expected.Bltcon1, actual.Bltcon1);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.SourceB, actual.SourceB);
        Assert.Equal(expected.SourceC, actual.SourceC);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.WidthWords, actual.WidthWords);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.WordX, actual.WordX);
        Assert.Equal(expected.RowY, actual.RowY);
        Assert.Equal(expected.LineMode, actual.LineMode);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(
            legacy.Paula.Intreq & AmigaConstants.IntreqBlitter,
            candidate.Paula.Intreq & AmigaConstants.IntreqBlitter);
        Assert.Equal(
            legacy.Paula.GetHighestPendingInterruptLevel(),
            candidate.Paula.GetHighestPendingInterruptLevel());
    }

    private static void AssertAreaChannelProgram(
        AmigaBus bus,
        int channelMask,
        int wordCount,
        uint sourceA = SourceA,
        uint sourceB = SourceB,
        uint sourceC = SourceC,
        uint destinationD = DestinationD,
        int addressStep = 2,
        int widthWords = int.MaxValue,
        int sourceAModulo = 0,
        int sourceBModulo = 0,
        int sourceCModulo = 0,
        int destinationDModulo = 0)
    {
        var expected = new List<(bool Write, uint Address)>();
        for (var word = 0; word < wordCount; word++)
        {
            if ((channelMask & 0x8) != 0)
            {
                expected.Add((
                    false,
                    OffsetBlitterAddress(
                        sourceA,
                        word,
                        addressStep,
                        widthWords,
                        sourceAModulo)));
            }

            if ((channelMask & 0x4) != 0)
            {
                expected.Add((
                    false,
                    OffsetBlitterAddress(
                        sourceB,
                        word,
                        addressStep,
                        widthWords,
                        sourceBModulo)));
            }

            if ((channelMask & 0x2) != 0)
            {
                expected.Add((
                    false,
                    OffsetBlitterAddress(
                        sourceC,
                        word,
                        addressStep,
                        widthWords,
                        sourceCModulo)));
            }

            if (word != 0)
            {
                expected.Add((
                    true,
                    OffsetBlitterAddress(
                        destinationD,
                        word - 1,
                        addressStep,
                        widthWords,
                        destinationDModulo)));
            }
        }

        expected.Add((
            true,
            OffsetBlitterAddress(
                destinationD,
                wordCount - 1,
                addressStep,
                widthWords,
                destinationDModulo)));
        var actual = BlitterAccesses(bus);
        Assert.Equal(expected.Count, actual.Length);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Write, actual[index].Request.IsWrite);
            Assert.Equal(expected[index].Address, actual[index].Request.Address);
        }
    }

    private static uint OffsetBlitterAddress(
        uint address,
        int word,
        int addressStep,
        int widthWords,
        int rowModulo)
    {
        var rows = widthWords == int.MaxValue ? 0 : word / widthWords;
        var moduloDirection = addressStep < 0 ? -1 : 1;
        return unchecked((uint)(
            (long)address +
            ((long)word * addressStep) +
            ((long)rows * rowModulo * moduloDirection)));
    }

    private static void AssertCompletedBlitPublished(
        AmigaBus bus,
        bool completesWithFinalDma = false,
        bool allowsPostCompletionPipelineDrain = false)
    {
        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.False(snapshot.Busy);
        var completionCycle = Assert.Single(bus.Blitter.CompletionCycles);
        Assert.Equal(completionCycle, bus.Blitter.LastCompletionCycle);
        if (allowsPostCompletionPipelineDrain)
        {
            Assert.True(snapshot.CurrentCycle >= completionCycle);
        }
        else
        {
            Assert.Equal(completionCycle, snapshot.CurrentCycle);
        }
        var accesses = BlitterAccesses(bus);
        if (accesses.Length != 0)
        {
            var finalDma = accesses[^1];
            Assert.Equal(finalDma.GrantedCycle, snapshot.LastDmaCycle);
            if (completesWithFinalDma)
            {
                Assert.True(finalDma.Request.IsWrite);
                Assert.Equal(
                    finalDma.RequestedCycle +
                        AgnusChipSlotScheduler.SlotCycles,
                    completionCycle);
            }
            else if (allowsPostCompletionPipelineDrain)
            {
                Assert.True(finalDma.Request.IsWrite);
                Assert.True(finalDma.CompletedCycle >= completionCycle);
                Assert.Equal(finalDma.CompletedCycle, snapshot.CurrentCycle);
            }
        }

        Assert.NotEqual(
            0,
            bus.Paula.Intreq & AmigaConstants.IntreqBlitter);
    }

    private static AmigaBus CreateBus(
        bool candidate,
        bool captureBusAccesses = true)
        => new(
            captureBusAccesses: captureBusAccesses,
            enableLiveAgnusDma: true,
            enableLiveDisplayDma: true,
            enableAgnusLiveDisplayLedger: candidate,
            enableAgnusLiveCopper: candidate,
            enableAgnusLiveBlitter: candidate);

    private static AmigaBusAccessResult[] BlitterAccesses(AmigaBus bus)
        => bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Blitter &&
                access.Request.Kind == AmigaBusAccessKind.Blitter)
            .ToArray();

    private static void AssertCpuAndBlitterSlotsAreExclusive(
        AmigaBus bus,
        IEnumerable<AmigaBusAccessResult> cpuAccesses)
    {
        foreach (var cpuAccess in cpuAccesses)
        {
            var sameSlot = bus.BusAccesses
                .Where(access =>
                    access.GrantedCycle == cpuAccess.GrantedCycle &&
                    access.Request.Requester is
                        AmigaBusRequester.Cpu or
                        AmigaBusRequester.Blitter)
                .ToArray();
            Assert.True(
                sameSlot.Length == 1 &&
                sameSlot[0].Request.Requester == AmigaBusRequester.Cpu,
                $"CPU and blitter both committed slot " +
                $"{cpuAccess.GrantedCycle}: " +
                $"{string.Join(", ", sameSlot.Select(access => access.Request.Requester))}");
            Assert.True(
                bus.TryGetCommittedAgnusSlotOwner(
                    cpuAccess.GrantedCycle,
                    out var owner) &&
                owner == AgnusChipSlotOwner.Cpu,
                $"CPU slot {cpuAccess.GrantedCycle} is owned by {owner}.");
        }
    }

    private static void AssertNoCollidingPhysicalSlots(AmigaBus bus)
    {
        var collisions = bus.BusAccesses
            .GroupBy(access => access.GrantedCycle)
            .Where(group => group.Select(access => access.Request.Requester).Distinct().Count() > 1)
            .Select(group =>
                $"{group.Key}:" +
                string.Join(
                    '+',
                    group
                        .Select(access => access.Request.Requester)
                        .Distinct()
                        .OrderBy(requester => requester)))
            .ToArray();
        Assert.True(
            collisions.Length == 0,
            $"Multiple requesters committed the same physical slot: " +
            $"[{string.Join(",", collisions)}].");
    }

    private static string[] BlitterSignature(AmigaBus bus)
        => BlitterAccesses(bus)
            .Select(access =>
                $"{(access.Request.IsWrite ? 'W' : 'R')}:" +
                $"{access.Request.Address:X8}:" +
                $"{access.RequestedCycle}:" +
                $"{access.GrantedCycle}:" +
                $"{access.CompletedCycle}")
            .ToArray();

    private static string SlotSignature(AmigaBus bus, long throughCycle)
        => string.Join(
            ",",
            Enumerable
                .Range(0, (int)(throughCycle / 2) + 1)
                .Select(index => (long)index * 2)
                .Select(cycle =>
                    bus.TryGetCommittedAgnusSlotOwner(cycle, out var owner)
                        ? $"{cycle}:{owner}"
                        : $"{cycle}:-"));

    private static string SlotWindowSignature(
        AmigaBus bus,
        long fromCycle,
        long throughCycle)
    {
        var first = AgnusChipSlotScheduler.AlignToSlot(
            Math.Max(0, fromCycle));
        return string.Join(
            ",",
            Enumerable
                .Range(
                    0,
                    (int)((throughCycle - first) /
                        AgnusChipSlotScheduler.SlotCycles) + 1)
                .Select(index =>
                    first +
                    ((long)index * AgnusChipSlotScheduler.SlotCycles))
                .Select(cycle =>
                    bus.TryGetCommittedAgnusSlotOwner(cycle, out var owner)
                        ? $"{cycle}:{owner}"
                        : $"{cycle}:-"));
    }

    private static string[] FixedDisplaySignature(AmigaBus bus)
        => bus.BusAccesses
            .Where(access =>
                access.Request.Requester is
                    AmigaBusRequester.Bitplane or
                    AmigaBusRequester.Sprite)
            .Select(access =>
                $"{access.Request.Requester}:{access.Request.Address:X8}:" +
                $"{access.RequestedCycle}:{access.GrantedCycle}:" +
                $"{access.CompletedCycle}")
            .ToArray();

    private static string[] CopperSignature(AmigaBus bus)
        => bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Copper)
            .Select(access =>
                $"{access.Request.Address:X8}:{access.RequestedCycle}:" +
                $"{access.GrantedCycle}:{access.CompletedCycle}")
            .ToArray();

    private static string SnapshotSignature(AmigaBus bus)
    {
        var snapshot = bus.Blitter.CaptureSnapshot();
        return $"{snapshot.Busy}:{snapshot.Zero}:{snapshot.Bltcon0:X4}:" +
            $"{snapshot.Bltcon1:X4}:{snapshot.CurrentCycle}:" +
            $"{snapshot.SourceA:X8}:{snapshot.SourceB:X8}:" +
            $"{snapshot.SourceC:X8}:{snapshot.DestinationD:X8}:" +
            $"{snapshot.WidthWords}:{snapshot.Height}:" +
            $"{snapshot.WordX}:{snapshot.RowY}:{snapshot.LineMode}:" +
            $"{snapshot.LastDmaCycle}:{snapshot.CompletedMicroOps}";
    }

    private static ushort[] ReadWords(
        AmigaBus bus,
        uint address,
        int wordCount)
    {
        var words = new ushort[wordCount];
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = BigEndian.ReadUInt16(
                bus.ChipRam,
                (int)address + (index * 2),
                "G3L blitter destination");
        }

        return words;
    }

    private static void WritePointer(
        AmigaBus bus,
        uint highRegisterAddress,
        uint pointer)
    {
        bus.WriteWord(highRegisterAddress, (ushort)(pointer >> 16));
        bus.WriteWord(highRegisterAddress + 2, (ushort)pointer);
    }

    private static ulong HashStrings(IEnumerable<string> values)
    {
        var hash = 14695981039346656037UL;
        foreach (var value in values)
        {
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }

    private static ulong HashWords(IEnumerable<ushort> values)
    {
        var hash = 14695981039346656037UL;
        foreach (var value in values)
        {
            hash ^= (byte)(value >> 8);
            hash *= 1099511628211UL;
            hash ^= (byte)value;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
