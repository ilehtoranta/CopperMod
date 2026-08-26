using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.Runtime;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusLiveCopperRequesterTests
{
    private const string G2LEvidenceModeVariable = "COPPER_G2L_EVIDENCE_MODE";
    private const string G2LEvidenceOutputVariable = "COPPER_G2L_EVIDENCE_OUTPUT";
    private const uint CopperList1 = 0x2400;
    private const uint CopperList2 = 0x2600;

    [Fact]
    public void CandidateIsExplicitlyOptInAndRequiresTheAcceptedFixedDisplayPath()
    {
        var legacy = new AmigaBus(captureBusAccesses: false);
        var candidate = CreateBus(candidate: true);

        Assert.False(legacy.AgnusLiveCopperEnabled);
        Assert.True(candidate.AgnusLiveCopperEnabled);
        Assert.True(candidate.AgnusLiveDisplayLedgerEnabled);
        Assert.Throws<ArgumentException>(() => new AmigaBus(
            captureBusAccesses: false,
            enableAgnusLiveCopper: true));
    }

    [Fact]
    public void FirstAndSecondWordsAreSampledOnlyByTheirGrantedSlotsAndMoveCommitsAtSecondWord()
    {
        var bus = CreateBus(candidate: true);
        WriteCopperList(
            bus,
            CopperList1,
            (0x0180, 0x0F00),
            (0xFFFF, 0xFFFE));
        StartCopper(bus, CopperList1);

        bus.AdvanceDmaTo(128);

        var reads = CopperReads(bus);
        var matchingWrites = bus.Display.CopperDisplayWrites.Where(write =>
            write.Address == 0x180 &&
            write.Value == 0x0F00).ToArray();
        Assert.True(
            matchingWrites.Length == 1,
            $"writes=[{string.Join(",", bus.Display.CopperDisplayWrites.Select(write =>
                $"{write.Address:X3}:{write.Value:X4}@{write.Cycle}"))}], " +
            $"reads=[{string.Join(",", CopperReadSignature(bus))}], " +
            $"diagnostics={bus.AgnusLiveCopperDiagnostics}");
        var colorWrite = matchingWrites[0];
        Assert.True(reads.Length >= 2);
        Assert.Equal(CopperList1, reads[0].Request.Address);
        Assert.Equal(CopperList1 + 2, reads[1].Request.Address);
        Assert.Equal(reads[1].GrantedCycle, colorWrite.Cycle);
        Assert.Equal((ushort)0x0F00, bus.Display.CaptureSnapshot().Colors[0]);

        var diagnostics = bus.AgnusLiveCopperDiagnostics;
        Assert.Equal(2, diagnostics.GrantedFirstWords);
        Assert.Equal(2, diagnostics.GrantedSecondWords);
        Assert.Equal(1, diagnostics.CommittedMoves);
        Assert.Equal(0, diagnostics.LegacyStepCalls);
        Assert.Equal(0, diagnostics.PredictionCalls);
        Assert.Equal(0, diagnostics.EventDiscoveryCalls);
        Assert.Equal(0, diagnostics.BlitterSynchronizationCalls);
        Assert.Equal(0, diagnostics.SchedulerDrainCalls);
        Assert.Equal(0, diagnostics.RangeAdvanceCalls);
    }

    [Fact]
    public void OperandMutationBetweenFirstAndSecondGrantIsSampledAtSecondGrant()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        foreach (var bus in new[] { legacy, candidate })
        {
            WriteCopperList(
                bus,
                CopperList1,
                (0x0180, 0x0111),
                (0xFFFF, 0xFFFE));
            StartCopper(bus, CopperList1);
            bus.AdvanceDmaTo(18);
            BigEndian.WriteUInt16(bus.ChipRam, (int)CopperList1 + 2, 0x0ABC);
            bus.AdvanceDmaTo(128);
        }

        Assert.Equal(CopperReadSignature(legacy), CopperReadSignature(candidate));
        Assert.Equal(
            CopperColorWriteSignature(legacy),
            CopperColorWriteSignature(candidate));
        Assert.Contains(
            candidate.Display.CopperDisplayWrites,
            write => write.Address == 0x180 && write.Value == 0x0ABC);
    }

    [Fact]
    public void WaitSkipCopjmpAndInterruptTransitionsMatchLegacy()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureControlFlowFixture(legacy);
        ConfigureControlFlowFixture(candidate);
        var target = 3L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

        legacy.AdvanceDmaTo(target);
        candidate.AdvanceDmaTo(target);

        Assert.Equal(FixedDisplaySignature(legacy), FixedDisplaySignature(candidate));
        Assert.Equal(CopperReadSignature(legacy), CopperReadSignature(candidate));
        Assert.Equal(CopperWriteSignature(legacy), CopperWriteSignature(candidate));
        Assert.Equal(
            legacy.Paula.GetHighestPendingInterruptLevel(),
            candidate.Paula.GetHighestPendingInterruptLevel());
        Assert.Equal(
            legacy.Display.CaptureSnapshot().Colors,
            candidate.Display.CaptureSnapshot().Colors);

        var diagnostics = candidate.AgnusLiveCopperDiagnostics;
        Assert.True(diagnostics.WaitComparisons > 0);
        Assert.True(diagnostics.SkipComparisons > 0);
        Assert.Equal(1, diagnostics.CommittedCopjmps);
        Assert.Equal(1, diagnostics.CommittedInterruptMoves);
        Assert.True(diagnostics.PublishedNextRequests > 0);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void BfdWaitObservesBusyStateWithoutSynchronizingTheBlitter()
    {
        var legacy = CreateBus(candidate: false);
        var bus = CreateBus(candidate: true);
        StartLongBlit(legacy);
        StartLongBlit(bus);
        WriteCopperList(
            legacy,
            CopperList1,
            (0x0001, 0x00FE),
            (0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
            (0xFFFF, 0xFFFE));
        WriteCopperList(
            bus,
            CopperList1,
            (0x0001, 0x00FE),
            (0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
            (0xFFFF, 0xFFFE));
        StartCopper(legacy, CopperList1);
        StartCopper(bus, CopperList1);
        var predictedReadyCycle = bus.Blitter.GetPredictedCompletionCycle();

        legacy.AdvanceDmaTo(
            predictedReadyCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bus.AdvanceDmaTo(
            predictedReadyCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var legacyIntreqWrite = legacy.CustomRegisterWrites.Last(write =>
            write.Address == 0x09C &&
            (write.Value & AmigaConstants.IntreqCopper) != 0);
        var intreqWrite = bus.CustomRegisterWrites.Last(write =>
            write.Address == 0x09C &&
            (write.Value & AmigaConstants.IntreqCopper) != 0);
        Assert.Equal(
            CopperReads(legacy).Select(access => access.Request.Address),
            CopperReads(bus).Select(access => access.Request.Address));
        Assert.True(intreqWrite.Cycle < legacyIntreqWrite.Cycle);
        Assert.Equal(legacy.Blitter.LastCompletionCycle, bus.Blitter.LastCompletionCycle);
        Assert.Equal(
            bus.Blitter.LastTerminationCycle + AgnusChipSlotScheduler.SlotCycles,
            bus.Blitter.LastCompletionCycle);
        Assert.Equal(
            bus.Blitter.LastTerminationCycle +
                (4 * AgnusChipSlotScheduler.SlotCycles),
            intreqWrite.Cycle);
        Assert.True(bus.AgnusLiveCopperDiagnostics.BfdBusyObservations > 0);
        Assert.Equal(0, bus.AgnusLiveCopperDiagnostics.BlitterSynchronizationCalls);
    }

    [Fact]
    public void CopperRetriesBehindCommittedBitplaneSlotsWithoutChangingEitherTrace()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureDisplayContentionFixture(legacy);
        ConfigureDisplayContentionFixture(candidate);
        var target = 48L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

        legacy.AdvanceDmaTo(target);
        candidate.AdvanceDmaTo(target);

        Assert.Equal(CopperReadSignature(legacy), CopperReadSignature(candidate));
        Assert.Equal(FixedDisplaySignature(legacy), FixedDisplaySignature(candidate));
        Assert.Equal(
            legacy.Display.CaptureSnapshot().BitplanePointers,
            candidate.Display.CaptureSnapshot().BitplanePointers);
        Assert.True(candidate.AgnusLiveCopperDiagnostics.DeniedRequests > 0);
    }

    [Fact]
    public void ConsecutiveSatisfiedWaitRunMatchesLegacyPhysicalWriteIntervals()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureSatisfiedWaitRun(legacy);
        ConfigureSatisfiedWaitRun(candidate);
        var target = 101L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

        legacy.AdvanceDmaTo(target);
        candidate.AdvanceDmaTo(target);

        Assert.Equal(FixedDisplaySignature(legacy), FixedDisplaySignature(candidate));
        Assert.Equal(CopperReadSignature(legacy), CopperReadSignature(candidate));
        Assert.Equal(
            CopperColorWriteSignature(legacy),
            CopperColorWriteSignature(candidate));
    }

    [Fact]
    public void VerticalWrapWaitAndLineStartWaitMatchLegacy()
    {
        AssertWaitFixtureParity(
            [
                (0x1001, 0xFFFE),
                (0x09C, 0x8008),
                (0xFFFF, 0xFFFE)
            ],
            17L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        AssertWaitFixtureParity(
            [
                (0x1001, 0xFFFE),
                (0x09C, 0x8008),
                (0x4001, 0xFFFE),
                (0x0100, 0x2200),
                (0xFF81, 0xFFFE),
                (0x009C, 0x8004),
                (0xFFFF, 0xFFFE)
            ],
            257L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
    }

    [Fact]
    public void UncontendedFutureWaitPublishesFirstWordAtProgrammedHorizontal()
    {
        var bus = CreateBus(candidate: true);
        WriteCopperList(
            bus,
            CopperList1,
            (0x6039, 0xFFFE),
            (0x0180, 0x044F),
            (0x009C, 0x8004),
            (0xFFFF, 0xFFFE));
        StartCopper(bus, CopperList1);
        var targetCycle =
            (96L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
            (56L * AmigaConstants.A500PalCpuCyclesPerColorClock);

        bus.AdvanceDmaTo(targetCycle + 16);

        var postWaitFetches = bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Copper &&
                access.Request.Address is CopperList1 + 4 or CopperList1 + 6)
            .OrderBy(access => access.GrantedCycle)
            .ToArray();
        Assert.Equal(2, postWaitFetches.Length);
        Assert.Equal(targetCycle, postWaitFetches[0].GrantedCycle);
        Assert.Equal(
            targetCycle + (2L * AmigaConstants.A500PalCpuCyclesPerColorClock),
            postWaitFetches[1].GrantedCycle);
        var paletteTransition = Assert.Single(
            bus.Display.CopperPresentationTransitions,
            transition =>
                transition.Offset == 0x180 &&
                transition.Value == 0x044F);
        Assert.Equal(70, paletteTransition.Row);
        Assert.Equal(18, paletteTransition.X);
    }

    [Fact]
    public void ScheduleChangingMovesMutateOnlyFutureDisplayRequests()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureScheduleMutationFixture(legacy);
        ConfigureScheduleMutationFixture(candidate);
        var target = 48L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

        legacy.AdvanceDmaTo(target);
        candidate.AdvanceDmaTo(target);

        Assert.Equal(CopperReadSignature(legacy), CopperReadSignature(candidate));
        Assert.Equal(FixedDisplaySignature(legacy), FixedDisplaySignature(candidate));
        var legacyDisplay = legacy.Display.CaptureSnapshot();
        var candidateDisplay = candidate.Display.CaptureSnapshot();
        Assert.Equal(legacyDisplay.BitplanePointers, candidateDisplay.BitplanePointers);
        Assert.Equal(legacyDisplay.DdfStart, candidateDisplay.DdfStart);
    }

    [Fact]
    public void WarmedCandidateExecutionAllocatesNoManagedMemory()
    {
        _ = RunAllocationFixture(measure: false);

        var allocated = RunAllocationFixture(measure: true);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void G2LSeparateProcessEvidenceProbe()
    {
        var mode = Environment.GetEnvironmentVariable(G2LEvidenceModeVariable);
        var outputPath = Environment.GetEnvironmentVariable(G2LEvidenceOutputVariable);
        if (string.IsNullOrWhiteSpace(mode) || string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var candidate = string.Equals(
            mode,
            "candidate",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(
            candidate || string.Equals(mode, "legacy", StringComparison.OrdinalIgnoreCase),
            $"Unsupported G2L evidence mode '{mode}'.");
        var bus = CreateBus(candidate);
        ConfigureControlFlowFixture(bus);
        bus.AdvanceDmaTo(3L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        var diagnostics = bus.AgnusLiveCopperDiagnostics;
        File.WriteAllLines(
            outputPath,
            [
                $"copper_read_hash={HashStrings(CopperReadSignature(bus)):X16}",
                $"copper_read_count={CopperReads(bus).Length}",
                $"copper_write_hash={HashStrings(CopperWriteSignature(bus)):X16}",
                $"copper_write_count={CopperWriteSignature(bus).Length}",
                $"color_hash={HashWords(bus.Display.CaptureSnapshot().Colors):X16}",
                $"interrupt_level={bus.Paula.GetHighestPendingInterruptLevel()}",
                $"live_requests={diagnostics.PublishedRequests}",
                $"live_grants={diagnostics.GrantedRequests}",
                $"live_denials={diagnostics.DeniedRequests}",
                $"live_first_words={diagnostics.GrantedFirstWords}",
                $"live_second_words={diagnostics.GrantedSecondWords}",
                $"live_transitions={diagnostics.CommittedTransitions}",
                $"live_wait_comparisons={diagnostics.WaitComparisons}",
                $"live_skip_comparisons={diagnostics.SkipComparisons}",
                $"live_copjmps={diagnostics.CommittedCopjmps}",
                $"live_interrupt_moves={diagnostics.CommittedInterruptMoves}",
                $"live_next_requests={diagnostics.PublishedNextRequests}",
                $"live_contract_violations={diagnostics.ContractViolations}",
                $"forbidden_calls={diagnostics.ForbiddenCalls}"
            ]);
    }

    private static long RunAllocationFixture(bool measure)
    {
        var bus = CreateBus(candidate: true, captureBusAccesses: false);
        WriteCopperList(
            bus,
            CopperList1,
            (0x0180, 0x0001),
            (0x0182, 0x0002),
            (0x0184, 0x0003),
            (0xFFFF, 0xFFFE));
        StartCopper(bus, CopperList1);
        if (!measure)
        {
            bus.AdvanceDmaTo(256);
            return 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        bus.AdvanceDmaTo(256);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AssertWaitFixtureParity(
        (ushort First, ushort Second)[] instructions,
        long targetCycle)
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        WriteCopperList(legacy, CopperList1, instructions);
        WriteCopperList(candidate, CopperList1, instructions);
        StartCopper(legacy, CopperList1);
        StartCopper(candidate, CopperList1);

        legacy.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);

        Assert.Equal(CopperReadSignature(legacy), CopperReadSignature(candidate));
        Assert.Equal(
            legacy.CustomRegisterWrites
                .Where(write => write.Address == 0x09C)
                .Select(write => $"{write.Value:X4}:{write.Cycle}"),
            candidate.CustomRegisterWrites
                .Where(write => write.Address == 0x09C)
                .Select(write => $"{write.Value:X4}:{write.Cycle}"));
    }

    private static AmigaBus CreateBus(bool candidate, bool captureBusAccesses = true)
        => new(
            captureBusAccesses: captureBusAccesses,
            enableLiveAgnusDma: true,
            enableLiveDisplayDma: true,
            enableAgnusLiveDisplayLedger: candidate,
            enableAgnusLiveCopper: candidate);

    private static void ConfigureControlFlowFixture(AmigaBus bus)
    {
        WriteCopperList(
            bus,
            CopperList1,
            (0x0180, 0x0111),
            (0x0001, 0xFFFF),
            (0x0180, 0x0222),
            (0x0101, 0xFFFE),
            (0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
            (0x008A, 0x0000),
            (0xFFFF, 0xFFFE));
        WriteCopperList(
            bus,
            CopperList2,
            (0x0182, 0x0333),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, list: 2, CopperList2);
        StartCopper(bus, CopperList1);
        bus.WriteWord(
            0x00DFF09A,
            (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqCopper));
    }

    private static void ConfigureDisplayContentionFixture(AmigaBus bus)
    {
        for (var offset = 0; offset < 2048; offset += 2)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                0x4000 + offset,
                (ushort)(0x8000 | offset));
        }

        WriteCopperList(
            bus,
            CopperList1,
            (0x2C01, 0xFFFE),
            (0x0180, 0x0001),
            (0x0180, 0x0002),
            (0x0180, 0x0003),
            (0x0180, 0x0004),
            (0x0180, 0x0005),
            (0x0180, 0x0006),
            (0x0180, 0x0007),
            (0x0180, 0x0008),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, list: 1, CopperList1);
        bus.WriteWord(0x00DFF08E, 0x2C81);
        bus.WriteWord(0x00DFF090, 0x2CC1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x4000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, 0x8380);
        bus.WriteWord(0x00DFF088, 0x0000);
        bus.EnableLiveAgnusDma();
    }

    private static void ConfigureSatisfiedWaitRun(AmigaBus bus)
    {
        WriteCopperList(
            bus,
            CopperList1,
            (0x6241, 0xFFFE),
            (0x0180, 0x0F00),
            (0x6241, 0xFFFE),
            (0x6241, 0xFFFE),
            (0x0180, 0x0FF0),
            (0x6241, 0xFFFE),
            (0x6241, 0xFFFE),
            (0x6241, 0xFFFE),
            (0x0180, 0x00FF),
            (0x6241, 0xFFFE),
            (0x6241, 0xFFFE),
            (0x6241, 0xFFFE),
            (0x6241, 0xFFFE),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, list: 1, CopperList1);
        bus.WriteWord(0x00DFF08E, 0x2C71);
        bus.WriteWord(0x00DFF090, 0x2CD1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF100, 0x5200);
        for (var plane = 0; plane < 5; plane++)
        {
            var pointerRegister = 0x00DFF0E0u + ((uint)plane * 4u);
            var pointer = 0x4000u + ((uint)plane * 0x1000u);
            bus.WriteWord(pointerRegister, (ushort)(pointer >> 16));
            bus.WriteWord(pointerRegister + 2, (ushort)pointer);
        }

        bus.WriteWord(0x00DFF096, 0x8380);
        bus.WriteWord(0x00DFF088, 0x0000);
        bus.EnableLiveAgnusDma();
    }

    private static void ConfigureScheduleMutationFixture(AmigaBus bus)
    {
        for (var offset = 0; offset < 4096; offset += 2)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                0x4000 + offset,
                (ushort)offset);
        }

        WriteCopperList(
            bus,
            CopperList1,
            (0x2C41, 0xFFFE),
            (0x00E2, 0x5000),
            (0x0092, 0x0040),
            (0x0096, 0x0100),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, list: 1, CopperList1);
        bus.WriteWord(0x00DFF08E, 0x2C81);
        bus.WriteWord(0x00DFF090, 0x2CC1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x4000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, 0x8380);
        bus.WriteWord(0x00DFF088, 0x0000);
        bus.EnableLiveAgnusDma();
    }

    private static void StartCopper(AmigaBus bus, uint address)
    {
        SetCopperPointer(bus, list: 1, address);
        bus.WriteWord(0x00DFF096, 0x8280);
        bus.WriteWord(0x00DFF088, 0x0000);
        bus.EnableLiveAgnusDma();
    }

    private static void SetCopperPointer(AmigaBus bus, int list, uint address)
    {
        var highOffset = list == 1 ? 0x00DFF080u : 0x00DFF084u;
        bus.WriteWord(highOffset, (ushort)(address >> 16));
        bus.WriteWord(highOffset + 2, (ushort)address);
    }

    private static void WriteCopperList(
        AmigaBus bus,
        uint address,
        params (ushort First, ushort Second)[] instructions)
    {
        for (var i = 0; i < instructions.Length; i++)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)address + (i * 4),
                instructions[i].First);
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)address + (i * 4) + 2,
                instructions[i].Second);
        }
    }

    private static void StartLongBlit(AmigaBus bus)
    {
        bus.WriteWord(0x00DFF040, 0x09F0, 0);
        bus.WriteWord(0x00DFF042, 0x0000, 0);
        bus.WriteWord(0x00DFF050, 0x0000, 0);
        bus.WriteWord(0x00DFF052, 0x3000, 0);
        bus.WriteWord(0x00DFF054, 0x0000, 0);
        bus.WriteWord(0x00DFF056, 0x4000, 0);
        bus.WriteWord(0x00DFF096, 0x8240, 0);
        bus.WriteWord(0x00DFF058, (ushort)((8 << 6) | 8), 0);
    }

    private static AmigaBusAccessResult[] CopperReads(AmigaBus bus)
        => bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Copper &&
                access.Request.Kind == AmigaBusAccessKind.Copper)
            .ToArray();

    private static string[] CopperReadSignature(AmigaBus bus)
        => CopperReads(bus)
            .Select(access =>
                $"{access.Request.Address:X6}:{access.Request.RequestedCycle}:" +
                $"{access.GrantedCycle}:{access.CompletedCycle}")
            .ToArray();

    private static string[] FixedDisplaySignature(AmigaBus bus)
        => bus.BusAccesses
            .Where(access =>
                access.Request.Requester is
                    AmigaBusRequester.Bitplane or AmigaBusRequester.Sprite)
            .Select(access =>
                $"{access.Request.Requester}:{access.Request.Address:X6}:" +
                $"{access.Request.RequestedCycle}:{access.GrantedCycle}:" +
                $"{access.CompletedCycle}")
            .ToArray();

    private static string[] CopperWriteSignature(AmigaBus bus)
        => bus.Display.CopperDisplayWrites
            .Concat(bus.CustomRegisterWrites)
            .Where(write => write.Address is 0x180 or 0x182 or 0x09C or 0x08A)
            .OrderBy(write => write.Cycle)
            .ThenBy(write => write.Address)
            .Select(write => $"{write.Address:X3}:{write.Value:X4}:{write.Cycle}")
            .ToArray();

    private static string[] CopperColorWriteSignature(AmigaBus bus)
        => bus.Display.CopperDisplayWrites
            .Where(write => write.Address == 0x180)
            .Select(write => $"{write.Value:X4}:{write.Cycle}")
            .ToArray();

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
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
