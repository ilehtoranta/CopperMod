using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.CustomChips.Denise;
using CopperMod.Amiga.Runtime;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusCommittedDisplayLedgerTests
{
    private const string G1LEvidenceModeVariable = "COPPER_G1L_DISPLAY_EVIDENCE_MODE";
    private const string G1LEvidenceOutputVariable = "COPPER_G1L_DISPLAY_EVIDENCE_OUTPUT";

    [Fact]
    public void ProductionEvidenceIsExplicitlyOptIn()
    {
        var legacy = new AmigaBus(captureBusAccesses: false);
        var evidence = new AmigaBus(
            captureBusAccesses: false,
            enableAgnusLiveDisplayLedger: true);

        Assert.False(legacy.AgnusLiveDisplayLedgerEnabled);
        Assert.Equal(0, legacy.Display.CommittedDisplayLedgerReservedBytes);
        Assert.True(evidence.AgnusLiveDisplayLedgerEnabled);
        Assert.True(evidence.Display.CommittedDisplayLedgerReservedBytes > 0);
    }

    [Fact]
    public void FixedDisplayKernelRejectsUnsupportedChipsetProfiles()
    {
        Assert.Throws<NotSupportedException>(() =>
            new AmigaBus(
                chipset: AmigaChipset.OcsNtsc,
                enableAgnusLiveDisplayLedger: true));
        Assert.Throws<NotSupportedException>(() =>
            new AmigaBus(
                chipset: AmigaChipset.EcsPal,
                enableAgnusLiveDisplayLedger: true));
    }

    [Fact]
    public void OptInLiveDisplayCaptureRetainsCommittedRowsBeyondPlanRing()
    {
        var bus = new AmigaBus(
            captureBusAccesses: false,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: true);
        for (var row = 0; row < 8; row++)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                0x1000 + (row * 2),
                (ushort)(0x8000 >> (row & 7)));
        }

        bus.WriteWord(0x00DFF096, 0x8300);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.EnableLiveAgnusDma();
        var firstRow = AmigaConstants.PalLowResOverscanBorderY;
        var lastRow = firstRow + 5;
        bus.Display.AdvanceLiveDmaTo(
            OutputRowStartCycle(lastRow + 1) - 1);

        var firstIndex = FindBitplaneSample(bus, firstRow, plane: 0, word: 0);
        var lastIndex = FindBitplaneSample(bus, lastRow, plane: 0, word: 0);

        Assert.True(firstIndex >= 0);
        Assert.True(lastIndex > firstIndex);
        Assert.Equal((uint)0x1000, bus.Display.GetCommittedDisplayEvent(firstIndex).Address);
        Assert.Equal(firstRow, bus.Display.GetCommittedDisplayEvent(firstIndex).Row);
        Assert.Equal(lastRow, bus.Display.GetCommittedDisplayEvent(lastIndex).Row);
        Assert.False(bus.Display.CommittedDisplayLedgerOverflowed);
    }

    [Fact]
    public void LedgerRetainsChronologicalSamplesBeyondThreeRowPlanRing()
    {
        var ledger = new AgnusCommittedDisplayLedger(capacity: 8);
        ledger.Reset(frameStartCycle: 100, frameStopCycle: 1000);

        Assert.True(ledger.Append(AgnusCommittedDisplayEvent.BitplaneSample(
            cycle: 120,
            row: 0,
            plane: 0,
            word: 0,
            address: 0x1000,
            addressValid: true,
            value: 0x1111,
            granted: true)));
        Assert.True(ledger.Append(AgnusCommittedDisplayEvent.SpriteSample(
            cycle: 160,
            row: 3,
            sprite: 2,
            word: 1,
            address: 0x2002,
            addressValid: true,
            value: 0x2222,
            granted: true)));
        Assert.True(ledger.Append(AgnusCommittedDisplayEvent.BitplaneSample(
            cycle: 200,
            row: 6,
            plane: 1,
            word: 2,
            address: 0x3004,
            addressValid: true,
            value: 0x3333,
            granted: true)));

        Assert.Equal(3, ledger.Count);
        Assert.Equal(0, ledger.GetEvent(0).Row);
        Assert.Equal((ushort)0x1111, ledger.GetEvent(0).Value);
        Assert.Equal(3, ledger.GetEvent(1).Row);
        Assert.Equal(AgnusCommittedDisplayEventKind.SpriteSample, ledger.GetEvent(1).Kind);
        Assert.Equal(6, ledger.GetEvent(2).Row);
        Assert.Equal((uint)0x3004, ledger.GetEvent(2).Address);
    }

    [Fact]
    public void LedgerPreservesSameCycleAppendOrder()
    {
        var ledger = new AgnusCommittedDisplayLedger(capacity: 4);
        ledger.Reset(frameStartCycle: 0, frameStopCycle: 100);

        ledger.Append(AgnusCommittedDisplayEvent.RegisterWrite(
            cycle: 40,
            register: 0x100,
            value: 0x1200,
            isCopper: true));
        ledger.Append(AgnusCommittedDisplayEvent.BitplaneSample(
            cycle: 40,
            row: 1,
            plane: 0,
            word: 0,
            address: 0x4000,
            addressValid: true,
            value: 0xABCD,
            granted: true));

        Assert.Equal(AgnusCommittedDisplayEventKind.RegisterWrite, ledger.GetEvent(0).Kind);
        Assert.True(ledger.GetEvent(0).IsCopperWrite);
        Assert.Equal(AgnusCommittedDisplayEventKind.BitplaneSample, ledger.GetEvent(1).Kind);
    }

    [Fact]
    public void LedgerRejectsEventsBehindCommittedChronology()
    {
        var ledger = new AgnusCommittedDisplayLedger(capacity: 4);
        ledger.Reset(frameStartCycle: 0, frameStopCycle: 100);
        ledger.Append(AgnusCommittedDisplayEvent.RegisterWrite(
            cycle: 40,
            register: 0x096,
            value: 0x8300,
            isCopper: false));

        Assert.Throws<InvalidOperationException>(() =>
            ledger.Append(AgnusCommittedDisplayEvent.BitplaneSample(
                cycle: 36,
                row: 0,
                plane: 0,
                word: 0,
                address: 0,
                addressValid: false,
                value: 0,
                granted: false)));
    }

    [Fact]
    public void LedgerOverflowFailsClosedWithoutOverwritingEvidence()
    {
        var ledger = new AgnusCommittedDisplayLedger(capacity: 1);
        ledger.Reset(frameStartCycle: 0, frameStopCycle: 100);
        var first = AgnusCommittedDisplayEvent.RegisterWrite(
            cycle: 20,
            register: 0x180,
            value: 0x0123,
            isCopper: true);

        Assert.True(ledger.Append(first));
        Assert.False(ledger.Append(AgnusCommittedDisplayEvent.RegisterWrite(
            cycle: 24,
            register: 0x182,
            value: 0x0456,
            isCopper: true)));

        Assert.True(ledger.Overflowed);
        Assert.Equal(1, ledger.Count);
        Assert.Equal(first.Value, ledger.GetEvent(0).Value);
    }

    [Fact]
    public void WarmLedgerResetAndAppendAllocateNothing()
    {
        var ledger = new AgnusCommittedDisplayLedger(capacity: 256);
        RunFrame(ledger, frameStart: 0, count: 128);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0UL;
        for (var frame = 1; frame <= 32; frame++)
        {
            checksum ^= RunFrame(ledger, frame * 1000L, count: 128);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotEqual(0UL, checksum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OptInCaptureDefersPresentationUntilCompletionAndConsumesLedger()
    {
        var bus = CreateConfiguredDisplayBus(enableCommittedDisplayLedger: true);
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);

        try
        {
            bus.AdvanceDmaTo(OutputRowStartCycle(12) - 1);

            Assert.Equal(0, bus.Display.PresentationCallsDuringLiveCapture);
            Assert.Equal(0, bus.Display.CommittedDisplayPresentationCursor);
            Assert.True(bus.Display.CommittedDisplayEventCount > 0);

            bus.Display.CompletePresentationFrame(frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }

        Assert.Equal(0, bus.Display.PresentationCallsDuringLiveCapture);
        Assert.Equal(
            bus.Display.CommittedDisplayEventCount,
            bus.Display.CommittedDisplayPresentationCursor);
    }

    [Fact]
    public void SlotKernelFrameBoundaryFinalizesBoundPresentationBeforeTimelineReset()
    {
        var bus = CreateConfiguredDisplayBus(
            enableCommittedDisplayLedger: true,
            AgnusBusArbitrationMode.SlotKernel);
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);

        try
        {
            var cycle = (long)frameCycles;
            _ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
            Assert.True(cycle >= frameCycles);

            bus.Display.CompletePresentationFrame(frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }

        var snapshot = bus.Display.CaptureSnapshot();
        Assert.True(snapshot.LastTimelineSegmentCount > 0);
        Assert.Contains(frame, pixel => pixel != 0xFF000000u);
    }

    [Fact]
    public void DelayedLedgerPresentationMatchesLegacyIncrementalPresentation()
    {
        var (legacy, legacyPointer) =
            RenderConfiguredDisplayFrame(enableCommittedDisplayLedger: false);
        var (delayed, delayedPointer) =
            RenderConfiguredDisplayFrame(enableCommittedDisplayLedger: true);

        Assert.Equal(legacy, delayed);
        Assert.Equal(legacyPointer, delayedPointer);
    }

    [Fact]
    public void DelayedLedgerPresentationPreservesCpuMutationOrderAndOutput()
    {
        var (legacy, _, _) = RenderCpuMutationFrame(enableCommittedDisplayLedger: false);
        var (delayed, firstWriteIndex, secondWriteIndex) =
            RenderCpuMutationFrame(enableCommittedDisplayLedger: true);

        Assert.Equal(legacy, delayed);
        Assert.True(firstWriteIndex >= 0);
        Assert.True(secondWriteIndex > firstWriteIndex);
    }

    [Fact]
    public void DelayedLedgerPresentationPreservesSpriteSamplesAndOutput()
    {
        var (legacy, _) = RenderSpriteDmaFrame(enableCommittedDisplayLedger: false);
        var (delayed, spriteSamples) =
            RenderSpriteDmaFrame(enableCommittedDisplayLedger: true);

        Assert.Equal(legacy, delayed);
        Assert.True(spriteSamples >= 4);
    }

    [Fact]
    public void LateSpriteDmaEnableDoesNotReplayRowsRetainedByCommittedLedger()
    {
        var bus = new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: true);
        bus.EnableLiveAgnusDma();
        var enableCycle = OutputRowStartCycle(180) +
            (80 * AmigaConstants.A500PalCpuCyclesPerColorClock);
        bus.AdvanceDmaTo(enableCycle);
        var cpuCycle = enableCycle + AgnusChipSlotScheduler.SlotCycles;

        bus.WriteWord(
            0x00DFF096,
            0x8220,
            ref cpuCycle,
            AmigaBusAccessKind.CpuDataWrite);
        bus.AdvanceDmaTo(OutputRowStartCycle(184));

        var dmaconWriteCycle = -1L;
        for (var index = 0; index < bus.Display.CommittedDisplayEventCount; index++)
        {
            ref readonly var entry = ref bus.Display.GetCommittedDisplayEvent(index);
            if (entry.Kind == AgnusCommittedDisplayEventKind.RegisterWrite &&
                entry.Register == 0x096 &&
                entry.Value == 0x8220)
            {
                dmaconWriteCycle = entry.Cycle;
            }

            if (entry.Kind == AgnusCommittedDisplayEventKind.SpriteSample)
            {
                Assert.True(
                    dmaconWriteCycle >= 0 && entry.Cycle >= dmaconWriteCycle,
                    $"Sprite sample at {entry.Cycle} preceded DMACON enable at {dmaconWriteCycle}.");
            }
        }

        Assert.True(dmaconWriteCycle >= 0);
    }

    [Fact]
    public void LateSpritePointerWriteDoesNotReplayPastSpriteSlots()
    {
        var bus = new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: true);
        var (pos, ctl) = EncodeSpritePosition(48, 230, height: 2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x0000);
        var (sprite0Pos, sprite0Ctl) = EncodeSpritePosition(32, 230, height: 4);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3100, sprite0Pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3102, sprite0Ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3114, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3116, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3100);
        bus.WriteWord(0x00DFF138, 0x0000);
        bus.WriteWord(0x00DFF13A, 0x3000);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF100, 0x6000);
        bus.WriteWord(0x00DFF096, 0x8320);
        bus.EnableLiveAgnusDma();
        var rowStartCycle = OutputRowStartCycle(232);
        var writeCycle = OutputRowStartCycle(232) +
            (110 * AmigaConstants.A500PalCpuCyclesPerColorClock);
        bus.Display.ScheduleWrite(new AgnusDisplayRegisterWrite(
            rowStartCycle + (4 * AmigaConstants.A500PalCpuCyclesPerColorClock),
            0x092,
            0x002C));
        bus.Display.ScheduleWrite(new AgnusDisplayRegisterWrite(
            writeCycle,
            0x138,
            0x0000));
        bus.AdvanceDmaTo(OutputRowStartCycle(233));

        var pointerWriteCycle = -1L;
        for (var index = 0; index < bus.Display.CommittedDisplayEventCount; index++)
        {
            ref readonly var entry = ref bus.Display.GetCommittedDisplayEvent(index);
            if (entry.Kind == AgnusCommittedDisplayEventKind.RegisterWrite &&
                entry.Register == 0x138)
            {
                pointerWriteCycle = entry.Cycle;
            }

            if (entry.Kind == AgnusCommittedDisplayEventKind.SpriteSample &&
                pointerWriteCycle >= 0)
            {
                Assert.True(
                    entry.Cycle >= pointerWriteCycle,
                    $"Sprite sample at {entry.Cycle} followed SPR6PTH write at {pointerWriteCycle}.");
            }
        }

        Assert.True(pointerWriteCycle >= 0);
    }

    [Fact]
    public void FullFrameRetentionIsBoundedAndAllocatesNothingAfterWarmup()
    {
        var bus = new AmigaBus(
            captureBusAccesses: false,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: true);
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.WriteWord(0x00DFF096, 0x8300);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.EnableLiveAgnusDma();

        for (var field = 0; field < 2; field++)
        {
            RenderEmptyField(bus, frame, field * frameCycles, frameCycles);
        }

        var measuredStart = frameCycles * 2;
        var diagnosticsBefore = bus.AgnusLiveFixedDisplayDiagnostics;
        var before = GC.GetAllocatedBytesForCurrentThread();
        RenderEmptyField(bus, frame, measuredStart, frameCycles);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var diagnosticsAfter = bus.AgnusLiveFixedDisplayDiagnostics;

        Assert.InRange(
            bus.Display.PresentationStateReservedBytes,
            1,
            128 * 1024 * 1024);
        Assert.Equal(0, allocated);
        Assert.True(
            diagnosticsAfter.PublishedRequests >
            diagnosticsBefore.PublishedRequests);
        Assert.Equal(
            diagnosticsAfter.PublishedRequests,
            diagnosticsAfter.GrantedRequests + diagnosticsAfter.DeniedRequests);
        Assert.Equal(
            diagnosticsAfter.GrantedRequests,
            diagnosticsAfter.ChipWordTransfers);
        Assert.Equal(
            diagnosticsAfter.GrantedRequests,
            diagnosticsAfter.RequesterCommits);
        Assert.Equal(0, diagnosticsAfter.OutstandingRequests);
        Assert.Equal(0, diagnosticsAfter.ContractViolations);
    }

    [Fact]
    public void G1LSeparateProcessEvidenceProbe()
    {
        var mode = Environment.GetEnvironmentVariable(G1LEvidenceModeVariable);
        var outputPath = Environment.GetEnvironmentVariable(G1LEvidenceOutputVariable);
        if (string.IsNullOrWhiteSpace(mode) || string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var enableCommittedDisplayLedger = mode switch
        {
            "legacy" => false,
            "candidate" => true,
            _ => throw new InvalidOperationException($"Unsupported G1L evidence mode '{mode}'.")
        };
        var evidence = CaptureG1LEvidence(enableCommittedDisplayLedger);
        File.WriteAllLines(
            outputPath,
            [
                $"mode={mode}",
                $"framebuffer_hash={evidence.FramebufferHash:X16}",
                $"fixed_access_hash={evidence.FixedAccessHash:X16}",
                $"fixed_access_count={evidence.FixedAccessCount}",
                $"sampled_word_hash={evidence.SampledWordHash:X16}",
                $"sampled_word_count={evidence.SampledWordCount}",
                $"custom_write_hash={evidence.CustomWriteHash:X16}",
                $"custom_write_count={evidence.CustomWriteCount}",
                $"pointer_hash={evidence.PointerHash:X16}",
                $"display_cursor_hash={evidence.DisplayCursorHash:X16}",
                $"sample_hash={evidence.SampleHash:X16}",
                $"sample_count={evidence.SampleCount}",
                $"register_hash={evidence.RegisterHash:X16}",
                $"register_count={evidence.RegisterCount}",
                $"ledger_count={evidence.LedgerCount}",
                $"presentation_cursor={evidence.PresentationCursor}",
                $"presentation_calls_during_capture={evidence.PresentationCallsDuringCapture}",
                $"ledger_overflowed={evidence.LedgerOverflowed}",
                $"live_requests={evidence.LiveRequests}",
                $"live_grants={evidence.LiveGrants}",
                $"live_denials={evidence.LiveDenials}",
                $"live_transfers={evidence.LiveTransfers}",
                $"live_requester_commits={evidence.LiveRequesterCommits}",
                $"live_outstanding={evidence.LiveOutstanding}",
                $"live_contract_violations={evidence.LiveContractViolations}"
            ]);

        if (enableCommittedDisplayLedger)
        {
            Assert.True(evidence.SampleCount > 0);
            Assert.True(evidence.RegisterCount >= 2);
            Assert.Equal(evidence.LedgerCount, evidence.PresentationCursor);
            Assert.Equal(0, evidence.PresentationCallsDuringCapture);
            Assert.False(evidence.LedgerOverflowed);
            Assert.True(evidence.LiveRequests > 0);
            Assert.Equal(
                evidence.LiveRequests,
                evidence.LiveGrants + evidence.LiveDenials);
            Assert.Equal(evidence.LiveGrants, evidence.LiveTransfers);
            Assert.Equal(evidence.LiveGrants, evidence.LiveRequesterCommits);
            Assert.Equal(0, evidence.LiveOutstanding);
            Assert.Equal(0, evidence.LiveContractViolations);
        }
    }

    private static ulong RunFrame(
        AgnusCommittedDisplayLedger ledger,
        long frameStart,
        int count)
    {
        ledger.Reset(frameStart, frameStart + 1000);
        ulong checksum = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = AgnusCommittedDisplayEvent.BitplaneSample(
                frameStart + index,
                row: index >> 3,
                plane: index & 7,
                word: index,
                address: (uint)(index * 2),
                addressValid: true,
                value: (ushort)index,
                granted: true);
            if (!ledger.Append(entry))
            {
                throw new InvalidOperationException("Unexpected test-ledger overflow.");
            }

            checksum = unchecked(
                (checksum * 1099511628211UL) ^
                (ulong)entry.Cycle ^
                entry.Address ^
                entry.Value);
        }

        return checksum;
    }

    private static int FindBitplaneSample(
        AmigaBus bus,
        int row,
        int plane,
        int word)
    {
        for (var index = 0; index < bus.Display.CommittedDisplayEventCount; index++)
        {
            ref readonly var entry = ref bus.Display.GetCommittedDisplayEvent(index);
            if (entry.Kind == AgnusCommittedDisplayEventKind.BitplaneSample &&
                entry.Row == row &&
                entry.Channel == plane &&
                entry.Word == word)
            {
                return index;
            }
        }

        return -1;
    }

    private static (uint[] Frame, uint BitplanePointer) RenderConfiguredDisplayFrame(
        bool enableCommittedDisplayLedger)
    {
        var bus = CreateConfiguredDisplayBus(enableCommittedDisplayLedger);
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);
        try
        {
            bus.AdvanceDmaTo(frameCycles - 1);
            bus.Display.CompletePresentationFrame(frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }

        return (frame, bus.Display.CaptureSnapshot().BitplanePointers[0]);
    }

    private static (uint[] Frame, int FirstWriteIndex, int SecondWriteIndex)
        RenderCpuMutationFrame(bool enableCommittedDisplayLedger)
    {
        var bus = new AmigaBus(
            captureBusAccesses: false,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: enableCommittedDisplayLedger);
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var row0Cycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
        var row1Cycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY + 1);
        bus.WriteWord(0x00DFF180, 0x0000, 0);
        bus.WriteWord(0x00DFF182, 0x0F00, 0);
        bus.WriteWord(0x00DFF092, 0x0038, 0);
        bus.WriteWord(0x00DFF094, 0x0038, 0);
        bus.WriteWord(0x00DFF0E0, 0x0000, 0);
        bus.WriteWord(0x00DFF0E2, 0x1000, 0);
        bus.WriteWord(0x00DFF096, 0x8300, 0);
        bus.WriteWord(0x00001000, 0x8000, 0);
        bus.WriteWord(0x00001002, 0x8000, 4);
        bus.EnableLiveAgnusDma();
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);
        try
        {
            bus.WriteWord(0x00DFF100, 0x1000, row0Cycle);
            bus.WriteWord(0x00DFF100, 0x0000, row1Cycle);
            bus.Display.CompletePresentationFrame(frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }

        var firstWriteIndex = FindRegisterWrite(bus, 0x100, 0x1000);
        var secondWriteIndex = FindRegisterWrite(bus, 0x100, 0x0000);
        return (frame, firstWriteIndex, secondWriteIndex);
    }

    private static (uint[] Frame, int SpriteSamples) RenderSpriteDmaFrame(
        bool enableCommittedDisplayLedger)
    {
        var bus = new AmigaBus(
            captureBusAccesses: false,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: enableCommittedDisplayLedger);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A4, 0x00F0);
        var (pos, ctl) = EncodeSpritePosition(48, 30, height: 2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        bus.EnableLiveAgnusDma();

        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);
        try
        {
            bus.AdvanceDmaTo(frameCycles - 1);
            bus.Display.CompletePresentationFrame(frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }

        var spriteSamples = 0;
        for (var index = 0; index < bus.Display.CommittedDisplayEventCount; index++)
        {
            if (bus.Display.GetCommittedDisplayEvent(index).Kind ==
                AgnusCommittedDisplayEventKind.SpriteSample)
            {
                spriteSamples++;
            }
        }

        return (frame, spriteSamples);
    }

    private static G1LEvidence CaptureG1LEvidence(
        bool enableCommittedDisplayLedger)
    {
        var bus = new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: true,
            enableAgnusLiveDisplayLedger: enableCommittedDisplayLedger);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A4, 0x00F0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        for (var address = 0x1000; address < 0x3000; address += 2)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                address,
                (ushort)(0x8000 >> ((address >> 1) & 0x0F)));
        }

        var (pos, ctl) = EncodeSpritePosition(48, 30, height: 2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        bus.WriteWord(0x00DFF096, 0x8320);
        bus.EnableLiveAgnusDma();

        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var enableCycle =
            OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
        var disableCycle =
            OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY + 3);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);
        try
        {
            bus.WriteWord(0x00DFF100, 0x1000, enableCycle);
            bus.WriteWord(0x00DFF100, 0x0000, disableCycle);
            bus.Display.CompletePresentationFrame(frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }

        var fixedAccessHash = FnvOffset;
        var fixedAccessCount = 0;
        foreach (var access in bus.BusAccesses)
        {
            if (access.Request.Requester is not (
                AmigaBusRequester.Bitplane or AmigaBusRequester.Sprite))
            {
                continue;
            }

            fixedAccessCount++;
            fixedAccessHash = Mix(fixedAccessHash, (ulong)access.Request.Requester);
            fixedAccessHash = Mix(fixedAccessHash, (ulong)access.Request.Kind);
            fixedAccessHash = Mix(fixedAccessHash, access.Request.Address);
            fixedAccessHash = Mix(fixedAccessHash, (ulong)access.Request.RequestedCycle);
            fixedAccessHash = Mix(fixedAccessHash, (ulong)access.GrantedCycle);
            fixedAccessHash = Mix(fixedAccessHash, (ulong)access.CompletedCycle);
            fixedAccessHash = Mix(fixedAccessHash, (ulong)(access.Request.Channel + 1));
        }

        var sampleHash = FnvOffset;
        var registerHash = FnvOffset;
        var sampleCount = 0;
        var registerCount = 0;
        for (var index = 0; index < bus.Display.CommittedDisplayEventCount; index++)
        {
            ref readonly var entry = ref bus.Display.GetCommittedDisplayEvent(index);
            if (entry.Kind == AgnusCommittedDisplayEventKind.RegisterWrite)
            {
                registerCount++;
                registerHash = Mix(registerHash, (ulong)entry.Cycle);
                registerHash = Mix(registerHash, entry.Register);
                registerHash = Mix(registerHash, entry.Value);
                registerHash = Mix(registerHash, entry.IsCopperWrite ? 1UL : 0UL);
                continue;
            }

            sampleCount++;
            sampleHash = Mix(sampleHash, (ulong)entry.Kind);
            sampleHash = Mix(sampleHash, (ulong)entry.Cycle);
            sampleHash = Mix(sampleHash, (ulong)entry.Row);
            sampleHash = Mix(sampleHash, (ulong)entry.Channel);
            sampleHash = Mix(sampleHash, (ulong)entry.Word);
            sampleHash = Mix(sampleHash, entry.Address);
            sampleHash = Mix(sampleHash, entry.Value);
            sampleHash = Mix(sampleHash, entry.Granted ? 1UL : 0UL);
            sampleHash = Mix(sampleHash, entry.AddressValid ? 1UL : 0UL);
        }

        var customWriteHash = FnvOffset;
        foreach (var write in bus.CustomRegisterWrites)
        {
            customWriteHash = Mix(customWriteHash, (ulong)write.Cycle);
            customWriteHash = Mix(customWriteHash, write.Address);
            customWriteHash = Mix(customWriteHash, write.Value);
        }

        var snapshot = bus.Display.CaptureSnapshot();
        var liveDiagnostics = bus.AgnusLiveFixedDisplayDiagnostics;
        var pointerHash = FnvOffset;
        foreach (var pointer in snapshot.BitplanePointers)
        {
            pointerHash = Mix(pointerHash, pointer);
        }

        return new G1LEvidence(
            Hash(frame),
            fixedAccessHash,
            fixedAccessCount,
            bus.CapturedFixedDisplayWordHash,
            bus.CapturedFixedDisplayWordCount,
            customWriteHash,
            bus.CustomRegisterWrites.Count,
            pointerHash,
            bus.Display.LiveDisplayCursorFingerprint,
            sampleHash,
            sampleCount,
            registerHash,
            registerCount,
            bus.Display.CommittedDisplayEventCount,
            bus.Display.CommittedDisplayPresentationCursor,
            bus.Display.PresentationCallsDuringLiveCapture,
            bus.Display.CommittedDisplayLedgerOverflowed,
            liveDiagnostics.PublishedRequests,
            liveDiagnostics.GrantedRequests,
            liveDiagnostics.DeniedRequests,
            liveDiagnostics.ChipWordTransfers,
            liveDiagnostics.RequesterCommits,
            liveDiagnostics.OutstandingRequests,
            liveDiagnostics.ContractViolations);
    }

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static ulong Hash(uint[] values)
    {
        var hash = FnvOffset;
        foreach (var value in values)
        {
            hash = Mix(hash, value);
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value)
        => unchecked((hash ^ value) * FnvPrime);

    private static void RenderEmptyField(
        AmigaBus bus,
        uint[] frame,
        long frameStart,
        long frameCycles)
    {
        bus.Display.BeginPresentationFrame(
            new PresentationFrameTarget(frame),
            frameStart,
            frameStart + frameCycles);
        try
        {
            bus.Display.CompletePresentationFrame(frameStart + frameCycles);
        }
        catch
        {
            bus.Display.AbortPresentationFrame();
            throw;
        }
    }

    private static AmigaBus CreateConfiguredDisplayBus(
        bool enableCommittedDisplayLedger,
        AgnusBusArbitrationMode agnusBusArbitration = AgnusBusArbitrationMode.Legacy)
    {
        var enableSlotKernel = agnusBusArbitration == AgnusBusArbitrationMode.SlotKernel;
        var bus = new AmigaBus(
            captureBusAccesses: false,
            enableLiveAgnusDma: true,
            enableHardwareSpecialization: enableSlotKernel,
            agnusBusArbitration: agnusBusArbitration,
            enableAgnusLiveDisplayLedger: enableCommittedDisplayLedger,
            enableAgnusLiveCopper: enableSlotKernel,
            enableAgnusLiveBlitter: enableSlotKernel,
            enableAgnusLivePaula: enableSlotKernel,
            enableAgnusLiveDisk: enableSlotKernel);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x9381);
        bus.WriteWord(0x00DFF090, 0xC3C1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, 0x8300);
        for (var address = 0x1000; address < 0x3000; address += 2)
        {
            BigEndian.WriteUInt16(
                bus.ChipRam,
                address,
                (ushort)(0x8000 >> ((address >> 1) & 0x0F)));
        }

        bus.EnableLiveAgnusDma();
        return bus;
    }

    private static int FindRegisterWrite(
        AmigaBus bus,
        ushort register,
        ushort value)
    {
        for (var index = 0; index < bus.Display.CommittedDisplayEventCount; index++)
        {
            ref readonly var entry = ref bus.Display.GetCommittedDisplayEvent(index);
            if (entry.Kind == AgnusCommittedDisplayEventKind.RegisterWrite &&
                entry.Register == register &&
                entry.Value == value)
            {
                return index;
            }
        }

        return -1;
    }

    private static (ushort Pos, ushort Ctl) EncodeSpritePosition(
        int x,
        int y,
        int height)
    {
        var hStart = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
        var vStop = vStart + height;
        var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
        var ctl = (ushort)(((vStop & 0xFF) << 8) |
            (hStart & 0x0001) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0));
        return (pos, ctl);
    }

    private static long OutputRowStartCycle(int row)
    {
        var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
        return (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
    }

    private readonly record struct G1LEvidence(
        ulong FramebufferHash,
        ulong FixedAccessHash,
        int FixedAccessCount,
        ulong SampledWordHash,
        int SampledWordCount,
        ulong CustomWriteHash,
        int CustomWriteCount,
        ulong PointerHash,
        ulong DisplayCursorHash,
        ulong SampleHash,
        int SampleCount,
        ulong RegisterHash,
        int RegisterCount,
        int LedgerCount,
        int PresentationCursor,
        int PresentationCallsDuringCapture,
        bool LedgerOverflowed,
        long LiveRequests,
        long LiveGrants,
        long LiveDenials,
        long LiveTransfers,
        long LiveRequesterCommits,
        long LiveOutstanding,
        long LiveContractViolations);
}
