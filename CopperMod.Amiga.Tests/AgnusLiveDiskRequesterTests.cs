using CopperDisk;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.Runtime;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusLiveDiskRequesterTests
{
    private const string EvidenceModeVariable =
        "COPPER_G5L_EVIDENCE_MODE";
    private const string EvidenceOutputVariable =
        "COPPER_G5L_EVIDENCE_OUTPUT";
    private const uint DmaBase = 0x6000;
    private const ushort SyncWord = 0x4489;

    [Fact]
    public void CandidateIsExplicitlyOptInAndRequiresAcceptedEarlierLiveStages()
    {
        var legacy = new AmigaBus(captureBusAccesses: false);
        var candidate = CreateBus(candidate: true, captureBusAccesses: false);

        Assert.False(legacy.AgnusLiveDiskEnabled);
        Assert.True(candidate.AgnusLiveDisplayLedgerEnabled);
        Assert.True(candidate.AgnusLiveCopperEnabled);
        Assert.True(candidate.AgnusLiveBlitterEnabled);
        Assert.True(candidate.AgnusLivePaulaEnabled);
        Assert.True(candidate.AgnusLiveDiskEnabled);
        Assert.Throws<ArgumentException>(() => new AmigaBus(
            captureBusAccesses: false,
            enableAgnusLiveDisk: true));
        Assert.Throws<NotSupportedException>(() => new AmigaBus(
            captureBusAccesses: false,
            chipset: AmigaChipset.OcsNtsc,
            enableAgnusLiveDisplayLedger: true,
            enableAgnusLiveCopper: true,
            enableAgnusLiveBlitter: true,
            enableAgnusLivePaula: true,
            enableAgnusLiveDisk: true));
    }

    [Fact]
    public void ReadWordsMatchLegacyAtExactFixedDiskSlots()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        InsertTrack(legacy, 0x1111, 0x2222, 0x3333, 0x4444);
        InsertTrack(candidate, 0x1111, 0x2222, 0x3333, 0x4444);

        var legacyReady = PrepareDiskDma(legacy);
        var candidateReady = PrepareDiskDma(candidate);
        Assert.Equal(legacyReady, candidateReady);
        StartDiskDma(legacy, words: 4, legacyReady);
        StartDiskDma(candidate, words: 4, candidateReady);
        var targetCycle = legacy.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;

        legacy.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);
        legacy.Paula.AdvanceTo(targetCycle);
        candidate.Paula.AdvanceTo(targetCycle);

        AssertEquivalentState(legacy, candidate, words: 4);
        var accesses = DiskAccesses(candidate);
        Assert.Equal(4, accesses.Length);
        Assert.All(
            accesses,
            access => Assert.True(
                AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(
                    AgnusChipSlotOwner.Disk,
                    access.GrantedCycle)));
        Assert.Equal(DiskAccessSignature(legacy), DiskAccessSignature(candidate));
        Assert.Equal(0, candidate.AgnusLiveDiskDiagnostics.ForbiddenCalls);
        Assert.Equal(0, candidate.AgnusLiveDiskDiagnostics.ContractViolations);
        Assert.Equal(4, candidate.AgnusLiveDiskDiagnostics.ReadWords);
        Assert.Equal(1, candidate.AgnusLiveDiskDiagnostics.CompletedBlocks);
    }

    [Fact]
    public void ActiveReadDmaKeepsPassiveInputLatchesInStage5Parity()
    {
        var stage5 = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        var words = Enumerable.Range(0, 64)
            .Select(index => (ushort)(0x5500 | index))
            .ToArray();
        InsertTrack(stage5, words);
        InsertTrack(candidate, words);

        var stage5Ready = PrepareDiskDma(stage5);
        var candidateReady = PrepareDiskDma(candidate);
        Assert.Equal(stage5Ready, candidateReady);
        StartDiskDma(stage5, words: 64, stage5Ready);
        StartDiskDma(candidate, words: 64, candidateReady);
        var targetCycle = stage5Ready + (DiskRevolutionCycles() / 4);

        stage5.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);

        var expected = stage5.Disk.CaptureSnapshot();
        var actual = candidate.Disk.CaptureSnapshot();
        Assert.True(expected.ActiveDma);
        Assert.True(actual.ActiveDma);
        Assert.Equal(expected.Dskbytr, actual.Dskbytr);
        Assert.Equal(expected.Dskdatr, actual.Dskdatr);
        Assert.Equal(DiskAccessSignature(stage5), DiskAccessSignature(candidate));
    }

    [Fact]
    public void WordSyncStartsCausallyAfterTheMatchingShiftRegisterEvent()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        InsertTrack(legacy, 0x9999, SyncWord, 0xABCD, 0x2468);
        InsertTrack(candidate, 0x9999, SyncWord, 0xABCD, 0x2468);
        ConfigureWordSync(legacy);
        ConfigureWordSync(candidate);

        var legacyReady = PrepareDiskDma(legacy);
        var candidateReady = PrepareDiskDma(candidate);
        StartDiskDma(legacy, words: 2, legacyReady);
        StartDiskDma(candidate, words: 2, candidateReady);
        Assert.True(legacy.Disk.CaptureSnapshot().ActiveDma);
        Assert.False(candidate.Disk.CaptureSnapshot().ActiveDma);

        var targetCycle = legacyReady + DiskRevolutionCycles() * 2;
        legacy.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);
        legacy.Paula.AdvanceTo(targetCycle);
        candidate.Paula.AdvanceTo(targetCycle);

        AssertEquivalentState(legacy, candidate, words: 2);
        Assert.Equal(0xABCD, ReadChipWord(candidate, DmaBase));
        Assert.Equal(0x2468, ReadChipWord(candidate, DmaBase + 2));
        Assert.True(candidate.AgnusLiveDiskDiagnostics.SyncMatches > 0);
        Assert.Equal(1, candidate.AgnusLiveDiskDiagnostics.CompletedBlocks);
    }

    [Fact]
    public void WriteWordsSampleChipRamAtTheGrantedDiskSlots()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        InsertTrack(legacy, 0xAAAA, 0xBBBB);
        InsertTrack(candidate, 0xAAAA, 0xBBBB);
        WriteChipWord(legacy, DmaBase, 0x1357);
        WriteChipWord(legacy, DmaBase + 2, 0x2468);
        WriteChipWord(candidate, DmaBase, 0x1357);
        WriteChipWord(candidate, DmaBase + 2, 0x2468);
        var legacyReady = PrepareDiskDma(legacy);
        var candidateReady = PrepareDiskDma(candidate);
        StartDiskDma(legacy, words: 2, legacyReady, writeMode: true);
        StartDiskDma(candidate, words: 2, candidateReady, writeMode: true);
        var targetCycle = legacy.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;

        legacy.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);
        legacy.Paula.AdvanceTo(targetCycle);
        candidate.Paula.AdvanceTo(targetCycle);

        AssertEquivalentState(legacy, candidate, words: 2);
        Assert.Equal(DiskAccessSignature(legacy), DiskAccessSignature(candidate));
        Assert.Equal(0x2468, candidate.Disk.CaptureSnapshot().Dskdatr);
        Assert.Equal(2, candidate.AgnusLiveDiskDiagnostics.WriteWords);
        Assert.Equal(1, candidate.AgnusLiveDiskDiagnostics.CompletedBlocks);
    }

    [Fact]
    public void DsklenCancellationWithdrawsTheStableWordGeneration()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        InsertTrack(legacy, 0x1111, 0x2222, 0x3333, 0x4444);
        InsertTrack(candidate, 0x1111, 0x2222, 0x3333, 0x4444);
        var legacyReady = PrepareDiskDma(legacy);
        var candidateReady = PrepareDiskDma(candidate);
        StartDiskDma(legacy, words: 4, legacyReady);
        StartDiskDma(candidate, words: 4, candidateReady);
        var cancelCycle = legacyReady + 1;

        legacy.WriteWord(0x00DFF024, 0, cancelCycle);
        candidate.WriteWord(0x00DFF024, 0, cancelCycle);
        var targetCycle = legacyReady + DiskRevolutionCycles();
        legacy.AdvanceDmaTo(targetCycle);
        candidate.AdvanceDmaTo(targetCycle);

        AssertEquivalentState(legacy, candidate, words: 4);
        Assert.Empty(DiskAccesses(candidate));
        Assert.Equal(1, candidate.AgnusLiveDiskDiagnostics.CancelledBlocks);
    }

    [Fact]
    public void ZeroLengthSecondStrobeCompletesWithoutPublishingADiskSlotIntent()
    {
        var legacy = CreateBus(candidate: false);
        var candidate = CreateBus(candidate: true);
        InsertTrack(legacy, 0x1234);
        InsertTrack(candidate, 0x1234);
        var legacyReady = PrepareDiskDma(legacy);
        var candidateReady = PrepareDiskDma(candidate);
        Assert.Equal(legacyReady, candidateReady);

        foreach (var bus in new[] { legacy, candidate })
        {
            bus.WriteWord(0x00DFF020, (ushort)(DmaBase >> 16), legacyReady);
            bus.WriteWord(0x00DFF022, (ushort)DmaBase, legacyReady);
            bus.WriteWord(0x00DFF024, 0x4000, legacyReady);
            bus.WriteWord(0x00DFF024, 0x80FF, legacyReady);
            bus.WriteWord(0x00DFF024, 0x8000, legacyReady);
        }

        AssertEquivalentState(legacy, candidate, words: 1);
        Assert.Equal(1, candidate.Disk.CaptureSnapshot().TransferCount);
        Assert.Equal(0, candidate.Disk.CaptureSnapshot().LastTransferWords);
        Assert.Empty(DiskAccesses(candidate));
        Assert.Equal(0, candidate.AgnusLiveDiskDiagnostics.PublishedRequests);
        Assert.Equal(0, candidate.AgnusLiveDiskDiagnostics.GrantedRequests);
        Assert.Equal(1, candidate.AgnusLiveDiskDiagnostics.CompletedBlocks);
        Assert.Equal(1, candidate.AgnusLiveDiskDiagnostics.Interrupts);
        Assert.Equal(0, candidate.AgnusLiveDiskDiagnostics.ForbiddenCalls);
    }

    [Fact]
    public void WarmedCandidateExecutionAllocatesNoManagedMemory()
    {
        _ = RunAllocationFixture(measure: false);
        var leastAllocated = long.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            leastAllocated = Math.Min(
                leastAllocated,
                RunAllocationFixture(measure: true));
        }

        Assert.Equal(0, leastAllocated);
    }

    [Fact]
    public void G5LSeparateProcessEvidenceProbe()
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
            $"Unsupported G5L evidence mode '{mode}'.");
        var bus = CreateBus(candidate);
        InsertTrack(
            bus,
            0x1111,
            0x2222,
            0x3333,
            0x4444,
            0x5555,
            0x6666,
            0x7777,
            0x8888);
        var readyCycle = PrepareDiskDma(bus);
        StartDiskDma(bus, words: 8, readyCycle);
        var targetCycle = readyCycle + (DiskRevolutionCycles() * 2);
        bus.AdvanceDmaTo(targetCycle);
        bus.Paula.AdvanceTo(targetCycle);

        var diagnostics = bus.AgnusLiveDiskDiagnostics;
        File.WriteAllLines(
            outputPath,
            [
                $"access_hash={HashStrings(DiskAccessSignature(bus)):X16}",
                $"access_count={DiskAccesses(bus).Length}",
                $"state_hash={HashStrings(DiskStateSignature(bus)):X16}",
                $"memory_hash={HashBytes(bus.ChipRam.AsSpan((int)DmaBase, 16)):X16}",
                $"intreq={bus.Paula.Intreq}",
                $"live_requests={diagnostics.PublishedRequests}",
                $"live_grants={diagnostics.GrantedRequests}",
                $"live_denials={diagnostics.DeniedRequests}",
                $"read_words={diagnostics.ReadWords}",
                $"sync_matches={diagnostics.SyncMatches}",
                $"completed_blocks={diagnostics.CompletedBlocks}",
                $"interrupts={diagnostics.Interrupts}",
                $"contract_violations={diagnostics.ContractViolations}",
                $"forbidden_calls={diagnostics.ForbiddenCalls}"
            ]);
    }

    private static long RunAllocationFixture(bool measure)
    {
        var bus = CreateBus(candidate: true, captureBusAccesses: false);
        InsertTrack(
            bus,
            Enumerable.Range(0, 64)
                .Select(index => (ushort)(0x1000 + index))
                .ToArray());
        var readyCycle = PrepareDiskDma(bus);
        StartDiskDma(bus, words: 32, readyCycle);
        var firstCompletion = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
        bus.AdvanceDmaTo(firstCompletion);
        var targetCycle = readyCycle + DiskRevolutionCycles();
        if (!measure)
        {
            bus.AdvanceDmaTo(targetCycle);
            return 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        bus.AdvanceDmaTo(targetCycle);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AssertEquivalentState(
        AmigaBus legacy,
        AmigaBus candidate,
        int words)
    {
        var expected = legacy.Disk.CaptureSnapshot();
        var actual = candidate.Disk.CaptureSnapshot();
        Assert.Equal(expected.DiskPointer, actual.DiskPointer);
        Assert.Equal(expected.Dsklen, actual.Dsklen);
        Assert.Equal(expected.Dskdatr, actual.Dskdatr);
        Assert.Equal(expected.ActiveDma, actual.ActiveDma);
        Assert.Equal(expected.TransferCount, actual.TransferCount);
        Assert.Equal(expected.LastTransferWords, actual.LastTransferWords);
        Assert.Equal(expected.LastTransferAddress, actual.LastTransferAddress);
        Assert.Equal(legacy.Paula.Intreq, candidate.Paula.Intreq);
        for (var word = 0; word < words; word++)
        {
            Assert.Equal(
                ReadChipWord(legacy, DmaBase + (uint)(word * 2)),
                ReadChipWord(candidate, DmaBase + (uint)(word * 2)));
        }
    }

    private static void InsertTrack(AmigaBus bus, params ushort[] words)
        => bus.Disk.Drive0.Insert(
            AmigaDiskImage.FromEncodedTracks(CreateTrackSet(words)));

    private static AmigaEncodedTrack[] CreateTrackSet(params ushort[] words)
    {
        var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
        var blank = AmigaEncodedTrack.FromBytes(WordsToBytes(0xAAAA));
        Array.Fill(tracks, blank);
        tracks[0] = AmigaEncodedTrack.FromBytes(WordsToBytes(words));
        return tracks;
    }

    private static byte[] WordsToBytes(params ushort[] words)
    {
        var bytes = new byte[Math.Max(1, words.Length) * 2];
        for (var index = 0; index < words.Length; index++)
        {
            BigEndian.WriteUInt16(bytes, index * 2, words[index]);
        }

        return bytes;
    }

    private static long PrepareDiskDma(AmigaBus bus)
    {
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        var readyCycle = ExpectedCiaAccessCycle() +
            Math.Max(
                1,
                (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
        bus.AdvanceDmaTo(readyCycle);
        bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
        bus.Paula.AdvanceTo(readyCycle);
        return readyCycle;
    }

    private static void StartDiskDma(
        AmigaBus bus,
        ushort words,
        long cycle,
        bool writeMode = false)
    {
        bus.WriteWord(0x00DFF020, (ushort)(DmaBase >> 16), cycle);
        bus.WriteWord(0x00DFF022, (ushort)DmaBase, cycle);
        var dsklen = (ushort)(0x8000 | (writeMode ? 0x4000 : 0) | words);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
    }

    private static void ConfigureWordSync(AmigaBus bus)
    {
        bus.WriteWord(0x00DFF07E, SyncWord);
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);
    }

    private static long ExpectedCiaAccessCycle()
        => AmigaConstants.A500PalCpuCyclesPerCiaTick;

    private static long DiskRevolutionCycles()
        => Math.Max(
            1,
            (long)Math.Ceiling(AmigaConstants.A500PalCpuClockHz / 5.0));

    private static ushort ReadChipWord(AmigaBus bus, uint address)
        => BigEndian.ReadUInt16(
            bus.ChipRam,
            checked((int)address),
            "live disk DMA word");

    private static void WriteChipWord(
        AmigaBus bus,
        uint address,
        ushort value)
        => BigEndian.WriteUInt16(
            bus.ChipRam,
            checked((int)address),
            value);

    private static AmigaBusAccessResult[] DiskAccesses(AmigaBus bus)
        => bus.BusAccesses
            .Where(access => access.Request.Kind == AmigaBusAccessKind.DiskDma)
            .ToArray();

    private static string[] DiskAccessSignature(AmigaBus bus)
        => DiskAccesses(bus)
            .Select(access =>
                $"{access.Request.Address:X8}:{access.Request.IsWrite}:" +
                $"{access.Request.RequestedCycle}:{access.GrantedCycle}:" +
                $"{access.CompletedCycle}")
            .ToArray();

    private static string[] DiskStateSignature(AmigaBus bus)
    {
        var state = bus.Disk.CaptureSnapshot();
        return
        [
            state.DiskPointer.ToString("X8"),
            state.Dsklen.ToString("X4"),
            state.Dsksync.ToString("X4"),
            state.Dskbytr.ToString("X4"),
            state.Dskdatr.ToString("X4"),
            state.Cylinder.ToString(),
            state.Head.ToString(),
            state.MotorOn.ToString(),
            state.Selected.ToString(),
            state.TransferCount.ToString(),
            state.LastTransferWords.ToString(),
            state.LastTransferAddress.ToString("X8"),
            state.ActiveDma.ToString(),
            state.ActiveDmaCompletionCycle.ToString()
        ];
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

    private static ulong HashBytes(ReadOnlySpan<byte> values)
    {
        var hash = 14695981039346656037UL;
        foreach (var value in values)
        {
            hash ^= value;
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
            enableAgnusLivePaula: candidate,
            enableAgnusLiveDisk: candidate);
}
