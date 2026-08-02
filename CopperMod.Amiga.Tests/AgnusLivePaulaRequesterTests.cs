using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.Runtime;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusLivePaulaRequesterTests
{
    private const string EvidenceModeVariable =
        "COPPER_G4L_EVIDENCE_MODE";
    private const string EvidenceOutputVariable =
        "COPPER_G4L_EVIDENCE_OUTPUT";
    private const uint AudioBase = 0x5000;

    [Fact]
    public void CandidateIsExplicitlyOptInAndRequiresAcceptedEarlierLiveStages()
    {
        var legacy = new AmigaBus(captureBusAccesses: false);
        var candidate = CreateBus(candidate: true, captureBusAccesses: false);

        Assert.False(legacy.AgnusLivePaulaEnabled);
        Assert.True(candidate.AgnusLiveDisplayLedgerEnabled);
        Assert.True(candidate.AgnusLiveCopperEnabled);
        Assert.True(candidate.AgnusLiveBlitterEnabled);
        Assert.True(candidate.AgnusLivePaulaEnabled);
        Assert.Throws<ArgumentException>(() => new AmigaBus(
            captureBusAccesses: false,
            enableAgnusLivePaula: true));
        Assert.Throws<NotSupportedException>(() => new AmigaBus(
            captureBusAccesses: false,
            chipset: AmigaChipset.OcsNtsc,
            enableAgnusLiveDisplayLedger: true,
            enableAgnusLiveCopper: true,
            enableAgnusLiveBlitter: true,
            enableAgnusLivePaula: true));
    }

    [Fact]
    public void FourIndependentChannelsMatchLegacyStateAndExactFixedSlots()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureFourChannels(legacy, lengthWords: 3, period: 113);
        ConfigureFourChannels(candidate, lengthWords: 3, period: 113);

        const long targetCycle = 8_000;
        legacy.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);

        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            Assert.Equal(
                legacy.Paula.GetChannelSnapshot(channel),
                candidate.Paula.GetChannelSnapshot(channel));
        }

        Assert.True(
            legacy.Paula.Intreq == candidate.Paula.Intreq,
            $"legacy_intreq={legacy.Paula.Intreq:X4}, candidate_intreq={candidate.Paula.Intreq:X4}, diagnostics={candidate.AgnusLivePaulaDiagnostics}");
        var accesses = PaulaAccesses(candidate);
        Assert.NotEmpty(accesses);
        Assert.All(
            accesses,
            access => Assert.True(
                AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(
                    AgnusChipSlotOwner.Paula,
                    access.GrantedCycle,
                    access.Request.Channel)));
        var diagnostics = candidate.AgnusLivePaulaDiagnostics;
        Assert.True(diagnostics.GrantedRequests > 0);
        Assert.True(diagnostics.Channel0Grants > 0);
        Assert.True(diagnostics.Channel1Grants > 0);
        Assert.True(diagnostics.Channel2Grants > 0);
        Assert.True(diagnostics.Channel3Grants > 0);
        Assert.Equal(0, diagnostics.ForbiddenCalls);
        Assert.Equal(0, diagnostics.ContractViolations);
        Assert.True(diagnostics.HighSampleTransitions > 0);
        Assert.True(diagnostics.LowSampleTransitions > 0);
        Assert.True(diagnostics.LengthReloads > 0);
        Assert.Equal(4, diagnostics.DmaEnableTransitions);
        Assert.True(diagnostics.Interrupts > 0);
    }

    [Fact]
    public void MidStreamDmaDisablePreservesAddressLengthSampleAndInterruptState()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureFourChannels(legacy, lengthWords: 8, period: 80);
        ConfigureFourChannels(candidate, lengthWords: 8, period: 80);
        legacy.Paula.ScheduleWrite(2_000, 0x096, 0x000F);
        candidate.Paula.ScheduleWrite(2_000, 0x096, 0x000F);

        legacy.AdvanceDmaTo(6_000);
        candidate.AdvanceDmaTo(6_000);

        AssertEquivalentState(legacy, candidate);
        Assert.Equal(4, candidate.AgnusLivePaulaDiagnostics.DmaDisableTransitions);
        Assert.Equal(0, candidate.AgnusLivePaulaDiagnostics.ForbiddenCalls);
        Assert.DoesNotContain(
            PaulaAccesses(candidate),
            access => access.Request.RequestedCycle > 2_000);
    }

    [Fact]
    public void RecurringReloadInterruptParityKeepsGrantAndCompletionDistinct()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        const long phaseStartCycle = 96 * AmigaConstants.A500PalCpuCyclesPerRasterLine;
        const long enableCycle = phaseStartCycle + 164;
        ConfigureChannel0(legacy, lengthWords: 1, period: 124, enableCycle);
        ConfigureChannel0(candidate, lengthWords: 1, period: 124, enableCycle);
        var stopCycle = enableCycle +
            (8 * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var targetCycle = enableCycle +
            (10 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        legacy.AdvanceDmaTo(enableCycle);
        candidate.AdvanceDmaTo(enableCycle);
        for (var cycle = enableCycle + AgnusChipSlotScheduler.SlotCycles;
            cycle <= targetCycle;
            cycle += AgnusChipSlotScheduler.SlotCycles)
        {
            legacy.AdvanceDmaTo(cycle);
            candidate.AdvanceDmaTo(cycle);
            if (cycle == stopCycle)
            {
                legacy.Paula.ScheduleWrite(cycle, 0x096, 0x0001);
                candidate.Paula.ScheduleWrite(cycle, 0x096, 0x0001);
            }

            if ((legacy.Paula.Intreq & 0x0080) != 0)
            {
                legacy.Paula.ScheduleWrite(cycle, 0x09C, 0x0080);
            }

            if ((candidate.Paula.Intreq & 0x0080) != 0)
            {
                candidate.Paula.ScheduleWrite(cycle, 0x09C, 0x0080);
            }
        }

        var expectedInterruptCycles = Audio0InterruptCycles(legacy);
        var actualInterruptCycles = Audio0InterruptCycles(candidate);
        Assert.True(
            expectedInterruptCycles.Length > 2,
            $"legacy=[{string.Join(',', expectedInterruptCycles)}], " +
            $"candidate=[{string.Join(',', actualInterruptCycles)}], " +
            $"accesses=[{string.Join(';', PaulaAccessSignature(candidate))}]");
        Assert.Equal(expectedInterruptCycles, actualInterruptCycles);

        var exactSlotReloads = PaulaAccesses(candidate)
            .Where(access =>
                access.Request.Channel == 0 &&
                access.Request.RequestedCycle == access.GrantedCycle)
            .ToArray();
        Assert.True(
            exactSlotReloads.Length != 0,
            $"interrupts=[{string.Join(',', actualInterruptCycles)}], " +
            $"accesses=[{string.Join(';', PaulaAccessSignature(candidate))}]");
        Assert.All(
            exactSlotReloads,
            access => Assert.NotEqual(access.CompletedCycle, access.GrantedCycle));
    }

    [Fact]
    public void DeniedFixedAudioSlotDefersToNextPhysicalChannelSlot()
    {
        var candidate = CreateBus(candidate: true);
        ConfigureChannel0(candidate, lengthWords: 2, period: 124, enableCycle: 0);
        var firstSlot = candidate.FindNextFixedDmaSlot(
            0,
            AgnusChipSlotOwner.Paula,
            channel: 0);
        var nextSlot = candidate.FindNextFixedDmaSlot(
            firstSlot + AgnusChipSlotScheduler.SlotCycles,
            AgnusChipSlotOwner.Paula,
            channel: 0);

        Assert.True(candidate.TryExecutePaulaDmaWordReadExactSlot(
            channel: 0,
            address: 0x7000,
            firstSlot,
            out _));

        candidate.AdvanceDmaTo(nextSlot);

        var liveGrant = Assert.Single(
            PaulaAccesses(candidate),
            access => access.Request.Address == AudioBase);
        Assert.Equal(nextSlot, liveGrant.GrantedCycle);
        Assert.Equal(1, candidate.AgnusLivePaulaDiagnostics.DeniedRequests);
    }

    [Fact]
    public void RenderedStereoOutputMatchesLegacyAfterLiveDmaCommits()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        ConfigureFourChannels(legacy, lengthWords: 4, period: 64);
        ConfigureFourChannels(candidate, lengthWords: 4, period: 64);
        legacy.AdvanceDmaTo(4_000);
        candidate.AdvanceDmaTo(4_000);

        var legacySamples = new float[64];
        var candidateSamples = new float[64];
        for (var frame = 0; frame < 32; frame++)
        {
            var cycle = frame * 128L;
            legacy.Paula.RenderSample(
                cycle,
                legacySamples,
                frame,
                channels: 2,
                advanceRegisterObservable: false);
            candidate.Paula.RenderSample(
                cycle,
                candidateSamples,
                frame,
                channels: 2,
                advanceRegisterObservable: false);
        }

        Assert.Equal(legacySamples, candidateSamples);
    }

    [Fact]
    public void WarmedCandidateExecutionAllocatesNoManagedMemory()
    {
        _ = RunAllocationFixture(measure: false);
        Assert.Equal(0, RunAllocationFixture(measure: true));
    }

    [Fact]
    public void G4LSeparateProcessEvidenceProbe()
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
            $"Unsupported G4L evidence mode '{mode}'.");
        var bus = CreateBus(candidate);
        ConfigureFourChannels(bus, lengthWords: 5, period: 80);
        bus.Paula.ScheduleWrite(3_000, 0x096, 0x0008);
        bus.AdvanceDmaTo(8_000);
        var samples = new float[64];
        for (var frame = 0; frame < 32; frame++)
        {
            bus.Paula.RenderSample(
                frame * 160L,
                samples,
                frame,
                channels: 2,
                advanceRegisterObservable: false);
        }

        var diagnostics = bus.AgnusLivePaulaDiagnostics;
        File.WriteAllLines(
            outputPath,
            [
                $"access_hash={HashStrings(PaulaAccessSignature(bus)):X16}",
                $"access_count={PaulaAccesses(bus).Length}",
                $"state_hash={HashStrings(ChannelStateSignature(bus)):X16}",
                $"sample_hash={HashSamples(samples):X16}",
                $"intreq={bus.Paula.Intreq}",
                $"live_requests={diagnostics.PublishedRequests}",
                $"live_grants={diagnostics.GrantedRequests}",
                $"channel0_grants={diagnostics.Channel0Grants}",
                $"channel1_grants={diagnostics.Channel1Grants}",
                $"channel2_grants={diagnostics.Channel2Grants}",
                $"channel3_grants={diagnostics.Channel3Grants}",
                $"high_transitions={diagnostics.HighSampleTransitions}",
                $"low_transitions={diagnostics.LowSampleTransitions}",
                $"reloads={diagnostics.LengthReloads}",
                $"interrupts={diagnostics.Interrupts}",
                $"contract_violations={diagnostics.ContractViolations}",
                $"forbidden_calls={diagnostics.ForbiddenCalls}"
            ]);
    }

    private static long RunAllocationFixture(bool measure)
    {
        var bus = CreateBus(candidate: true, captureBusAccesses: false);
        ConfigureFourChannels(bus, lengthWords: 32, period: 64);
        bus.AdvanceDmaTo(20_000);
        bus.Reset();
        ConfigureFourChannels(bus, lengthWords: 32, period: 64);
        if (!measure)
        {
            bus.AdvanceDmaTo(20_000);
            return 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        bus.AdvanceDmaTo(20_000);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AssertEquivalentState(
        AmigaBus expected,
        AmigaBus actual)
    {
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            Assert.Equal(
                expected.Paula.GetChannelSnapshot(channel),
                actual.Paula.GetChannelSnapshot(channel));
        }

        Assert.Equal(expected.Paula.Dmacon, actual.Paula.Dmacon);
        Assert.Equal(expected.Paula.Intreq, actual.Paula.Intreq);
    }

    private static void ConfigureFourChannels(
        AmigaBus bus,
        int lengthWords,
        int period)
    {
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var address = AudioBase + (uint)(channel * 0x100);
            for (var word = 0; word < lengthWords; word++)
            {
                BigEndian.WriteUInt16(
                    bus.ChipRam,
                    (int)address + (word * 2),
                    (ushort)(0x1100 + (channel * 0x101) + word));
            }

            var registerBase = (ushort)(0x0A0 + (channel * 0x10));
            bus.Paula.ScheduleWrite(0, registerBase, (ushort)(address >> 16));
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 2), (ushort)address);
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 4), (ushort)lengthWords);
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 6), (ushort)period);
            bus.Paula.ScheduleWrite(0, (ushort)(registerBase + 8), (ushort)(32 + channel));
        }

        bus.Paula.ScheduleWrite(0, 0x09A, 0xC780);
        bus.Paula.ScheduleWrite(0, 0x096, 0x820F);
    }

    private static void ConfigureChannel0(
        AmigaBus bus,
        int lengthWords,
        int period,
        long enableCycle)
    {
        for (var word = 0; word < lengthWords; word++)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                (int)AudioBase + (word * 2),
                (ushort)(0x1100 + word));
        }

        bus.Paula.ScheduleWrite(0, 0x0A0, (ushort)(AudioBase >> 16));
        bus.Paula.ScheduleWrite(0, 0x0A2, (ushort)AudioBase);
        bus.Paula.ScheduleWrite(0, 0x0A4, (ushort)lengthWords);
        bus.Paula.ScheduleWrite(0, 0x0A6, (ushort)period);
        bus.Paula.ScheduleWrite(0, 0x0A8, 32);
        bus.Paula.ScheduleWrite(0, 0x09A, 0xC080);
        bus.Paula.ScheduleWrite(enableCycle, 0x096, 0x8201);
    }

    private static long[] Audio0InterruptCycles(AmigaBus bus)
        => bus.Paula.DrainInterrupts()
            .Where(interrupt => interrupt.Channel == 0)
            .Select(interrupt => interrupt.Cycle)
            .ToArray();

    private static AmigaBusAccessResult[] PaulaAccesses(AmigaBus bus)
        => bus.BusAccesses
            .Where(access =>
                access.Request.Requester == AmigaBusRequester.Paula &&
                access.Request.Kind == AmigaBusAccessKind.PaulaDma)
            .ToArray();

    private static string[] PaulaAccessSignature(AmigaBus bus)
        => PaulaAccesses(bus)
            .Select(access =>
                $"{access.Request.Channel}:{access.Request.Address:X8}:" +
                $"{access.Request.RequestedCycle}:{access.GrantedCycle}:" +
                $"{access.CompletedCycle}")
            .ToArray();

    private static string[] ChannelStateSignature(AmigaBus bus)
        => Enumerable.Range(0, AmigaConstants.PaulaChannelCount)
            .Select(channel =>
                bus.Paula.GetChannelSnapshot(channel).ToString() ??
                string.Empty)
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

    private static ulong HashSamples(IEnumerable<float> samples)
    {
        var hash = 14695981039346656037UL;
        foreach (var sample in samples)
        {
            hash ^= (uint)BitConverter.SingleToInt32Bits(sample);
            hash *= 1099511628211UL;
        }

        return hash;
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
            enableAgnusLiveBlitter: candidate,
            enableAgnusLivePaula: candidate);
}
