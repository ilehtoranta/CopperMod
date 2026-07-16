/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        private byte ReadCustomByte(ushort offset, long sampleCycle)
        {
            if (TryReadBeamPositionByte(offset, sampleCycle, out var beamPositionValue))
            {
                return beamPositionValue;
            }

            if (TryReadGamePortCustomByte(offset, out var gamePortValue))
            {
                return gamePortValue;
            }

            if (Display.TryReadByte(offset, out var displayValue))
            {
                return displayValue;
            }

            return Disk.TryReadByte(offset, out var diskValue) ? diskValue : Paula.ReadByte(offset);
        }

        private ushort ReadCustomWord(ushort offset, long sampleCycle)
        {
            offset = (ushort)(offset & 0x01FE);
            switch (offset)
            {
                case 0x002:
                    return (ushort)(Paula.Dmacon | Blitter.DmaconStatusBits);
                case 0x004:
                    CalculateBeamPosition(sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle, out var vposr, out _);
                    return vposr;
                case 0x006:
                    CalculateBeamPosition(sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle, out _, out var vhposr);
                    return vhposr;
                case 0x008:
                case 0x01A:
                case 0x020:
                case 0x022:
                case 0x024:
                case 0x07E:
                    return Disk.ReadWord(offset);
                case 0x00A:
                    return ReadGamePortData(0);
                case 0x00C:
                    return ReadGamePortData(1);
                case 0x00E:
                    return 0x8000;
                case 0x010:
                    return Paula.Adkcon;
                case 0x016:
                    return ReadPotGoData();
                case 0x01C:
                    return Paula.Intena;
                case 0x01E:
                    return Paula.Intreq;
                default:
                    return (ushort)((Paula.ReadByte(offset) << 8) | Paula.ReadByte((ushort)(offset + 1)));
            }
        }

        private ushort ReadCpuCustomWordAtGranted(ushort offset, long grantedCycle, long sampleCycle)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            var capturedLatch = _chipDataBusLatch;
            var dmaValueVisible = _chipDataBusLatchWasDma &&
                _chipDataBusLatchCycle + AgnusChipSlotScheduler.SlotCycles == grantedCycle;
            ushort value;
            if (offset == 0x000)
            {
                value = capturedLatch;
            }
            else if (!CustomRegisterScheduleClassifier.IsOcsReadableRegister(offset))
            {
                WriteCustomWord(AmigaBusRequester.Cpu, offset, capturedLatch, grantedCycle);
                value = dmaValueVisible ? capturedLatch : (ushort)0xFFFF;
            }
            else
            {
                value = ReadCustomWord(offset, sampleCycle);
            }

            RememberChipDataBusWord(value, grantedCycle, wasDma: false);
            return value;
        }

        private byte ReadCpuCustomByteAtGranted(ushort offset, long grantedCycle, long sampleCycle)
        {
            var wordOffset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            var highLane = (offset & 1) == 0;
            var capturedLatch = _chipDataBusLatch;
            var capturedByte = highLane ? (byte)(capturedLatch >> 8) : (byte)capturedLatch;
            var dmaValueVisible = _chipDataBusLatchWasDma &&
                _chipDataBusLatchCycle + AgnusChipSlotScheduler.SlotCycles == grantedCycle;
            byte value;
            if (wordOffset == 0x000)
            {
                value = capturedByte;
            }
            else if (!CustomRegisterScheduleClassifier.IsOcsReadableRegister(wordOffset))
            {
                WriteCustomByte(AmigaBusRequester.Cpu, offset, capturedByte, grantedCycle);
                value = dmaValueVisible ? capturedByte : (byte)0xFF;
            }
            else
            {
                value = ReadCustomByte(offset, sampleCycle);
            }

            RememberChipDataBusByte(value, grantedCycle, wasDma: false);
            return value;
        }

        private uint ReadCpuCustomLongAtGranted(uint address, long firstWordCycle, long secondWordCycle)
        {
            var high = ReadCpuCustomWordAtGranted(
                (ushort)(address - CustomRegisterBaseAddress),
                firstWordCycle,
                firstWordCycle);
            var lowAddress = address + 2;
            var low = IsCustomRegisterWordAddress(lowAddress)
                ? ReadCpuCustomWordAtGranted(
                    (ushort)(lowAddress - CustomRegisterBaseAddress),
                    secondWordCycle,
                    secondWordCycle)
                : ReadRawWord(lowAddress, secondWordCycle);
            return ((uint)high << 16) | low;
        }

        private bool TryReadBeamPositionByte(ushort offset, long sampleCycle, out byte value)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            if (TryReadBeamPositionWord(wordOffset, sampleCycle, out var word))
            {
                value = (offset & 1) == 0 ? (byte)(word >> 8) : (byte)word;
                return true;
            }

            value = 0;
            return false;
        }

        private bool TryReadBeamPositionWord(ushort offset, long sampleCycle, out ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            if (offset != 0x004 && offset != 0x006)
            {
                value = 0;
                return false;
            }

            CalculateBeamPosition(sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle, out var vposr, out var vhposr);
            value = offset == 0x004 ? vposr : vhposr;
            return true;
        }

        private void CalculateBeamPosition(long targetCycle, out ushort vposr, out ushort vhposr)
        {
            var beam = _palBeamClock.GetPosition(targetCycle);
            var horizontal = EncodeVhposrHorizontal(beam.BeamHorizontal);
            var registerLine = beam.BeamLine;
            vposr = (ushort)(((beam.IsLongFrame ? 1 : 0) << 15) | ((registerLine >> 8) & 0x0001));
            vhposr = (ushort)(((registerLine & 0x00FF) << 8) | horizontal);
        }

        private static int EncodeVhposrHorizontal(int beamHorizontal)
        {
            var physicalHorizontal = Math.Clamp(beamHorizontal, 0, 0xE2);
            // Internal RGA slot coordinates and the externally visible HPOS counter
            // use different origins around horizontal sync.
            var offset = physicalHorizontal >= 0xE1
                ? 3
                : physicalHorizontal >= 0xDF
                    ? 4
                    : physicalHorizontal >= 0xB7
                        ? 3
                    : 8;
            return (physicalHorizontal + offset) % AmigaConstants.A500PalColorClocksPerRasterLine;
        }

        private uint ReadRawLong(uint address)
        {
            return ((uint)ReadRawWord(address) << 16) | ReadRawWord(address + 2);
        }



        private void WriteCpuCustomRegisterLongSplit(
            uint address,
            uint value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            const AmigaBusAccessTarget target = AmigaBusAccessTarget.CustomRegisters;
            var grantRequestCycle = requestedCycle;
            var liveScratchAttempted = false;
            var liveScratchSupported = false;
            var liveScratch = default(OcsLiveDmaScratchResult);
            if (ShouldRunDeferredCpuWaitSlotShadowAudit &&
                _deferredCpuWaitSlotShadowLiveAttempts < DeferredCpuWaitSlotShadowLiveMaxSamples &&
                LiveAgnusDmaEnabled &&
                Display.HasLiveDisplayWork() &&
                Display.GetNextLiveDisplayWakeCandidateCycle(grantRequestCycle, grantRequestCycle + PalLineCycles).HasValue &&
                IsDeferredCpuWaitSlotShadowGrantSupported(target, AmigaBusAccessSize.Long, grantRequestCycle + PalLineCycles))
            {
                liveScratchAttempted = true;
                var scratchSlots = _hrmSlotEngine.CreateShadowCopy();
                _deferredCpuWaitSlotShadowLiveAttempts++;
                liveScratchSupported = Display.TryRunCpuWaitLiveDmaScratch(
                    scratchSlots,
                    accessKind,
                    target,
                    address,
                    AmigaBusAccessSize.Long,
                    grantRequestCycle,
                    isWrite: true,
                    OcsLiveDmaScratchCpuWrite.Long(target, address, value),
                    out liveScratch);
                RecordDeferredCpuWaitSlotShadowLiveCoverage(AmigaBusAccessSize.Long, liveScratch);
                if (!liveScratchSupported)
                {
                    RecordDeferredCpuWaitSlotShadowUnsupported(
                        accessKind,
                        target,
                        address,
                        AmigaBusAccessSize.Long,
                        isWrite: true,
                        requestedCycle,
                        grantRequestCycle,
                        CpuWaitSlotShadowReason.Display,
                        liveScratch.ToDetailString());
                }
            }

            _hardwareScheduler.DrainForCpuAccess(target, address, grantRequestCycle, isWrite: true, AmigaBusAccessSize.Long);
            if (Blitter.Busy)
            {
                grantRequestCycle = Blitter.AdvanceThroughCpuStall(grantRequestCycle);
            }

            grantRequestCycle = Math.Max(0, grantRequestCycle);
            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target))
            {
                var fallbackFirstWordCycle = grantRequestCycle;
                var fallbackSecondWordCycle = GetCpuSecondWordCycle(AmigaBusAccessSize.Long, fallbackFirstWordCycle, fallbackFirstWordCycle);
                WriteCpuCustomRegisterWord(address, (ushort)(value >> 16), fallbackFirstWordCycle);
                WriteRawWord(address + 2, (ushort)value, fallbackSecondWordCycle, default(CpuWritePolicy));
                cycle = fallbackSecondWordCycle;
                return;
            }

            GrantCpuCustomRegisterLongWriteWordPhase(
                accessKind,
                address,
                grantRequestCycle,
                grantRequestCycle,
                out var firstWordCycle,
                out var firstCompletedCycle);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, firstWordCycle, isWrite: true);
            WriteCpuCustomRegisterWord(address, (ushort)(value >> 16), firstWordCycle);

            var secondSearchCycle = firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles;
            if (Blitter.Busy)
            {
                secondSearchCycle = Blitter.AdvanceThroughCpuStall(secondSearchCycle);
            }

            GrantCpuCustomRegisterLongWriteWordPhase(
                accessKind,
                address,
                secondSearchCycle,
                grantRequestCycle,
                out var secondWordCycle,
                out var completedCycle);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, firstCompletedCycle, secondWordCycle, isWrite: true);
            WriteRawWord(address + 2, (ushort)value, secondWordCycle, default(CpuWritePolicy));

            if (liveScratchAttempted && liveScratchSupported)
            {
                var referenceTimeline = _hrmSlotEngine.CaptureTimelineSignature(grantRequestCycle, completedCycle);
                RecordDeferredCpuWaitSlotShadowAudit(
                    accessKind,
                    target,
                    address,
                    AmigaBusAccessSize.Long,
                    isWrite: true,
                    requestedCycle,
                    grantRequestCycle,
                    liveScratch.GrantedCycle,
                    liveScratch.SecondWordCycle,
                    liveScratch.CompletedCycle,
                    firstWordCycle,
                    secondWordCycle,
                    completedCycle,
                    liveScratch.Timeline,
                    referenceTimeline,
                    liveScratch.ToDetailString());
            }

            RecordDeferredCpuWaitWindow(
                accessKind,
                target,
                AmigaBusAccessSize.Long,
                isWrite: true,
                requestedCycle,
                firstWordCycle);
            if (firstWordCycle > grantRequestCycle)
            {
                Agnus.RecordCpuChipWaitCycles(firstWordCycle - grantRequestCycle);
            }

            cycle = completedCycle;
        }

        private void GrantCpuCustomRegisterLongWriteWordPhase(
            AmigaBusAccessKind accessKind,
            uint address,
            long searchCycle,
            long requestedCycle,
            out long grantedCycle,
            out long completedCycle)
        {
            const AmigaBusAccessTarget target = AmigaBusAccessTarget.CustomRegisters;
            PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
                target,
                address,
                accessKind,
                requestedCycle,
                searchCycle);
            _hrmSlotEngine.GrantCpuDataLongWordPhaseSlot(
                accessKind,
                target,
                address,
                searchCycle,
                requestedCycle,
                isWrite: true,
                out grantedCycle,
                out completedCycle);
        }



        private void WriteCpuCustomRegisterByte(uint address, byte value, long grantedCycle)
        {
            if (TryGetCustomRegisterByteOffset(address, out var offset))
            {
                WriteCustomByte(AmigaBusRequester.Cpu, offset, value, grantedCycle);
                return;
            }

            WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
        }

        private void WriteCpuCustomRegisterWord(uint address, ushort value, long grantedCycle)
        {
            if (TryGetCustomRegisterWordOffset(address, out var offset))
            {
                WriteCustomWord(AmigaBusRequester.Cpu, offset, value, grantedCycle);
                return;
            }

            if (TryGetCustomRegisterByteOffset(address, out offset))
            {
                WriteCustomByte(AmigaBusRequester.Cpu, offset, (byte)(value >> 8), grantedCycle);
                WriteRawByte(address + 1, (byte)value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));
        }

        private void WriteCpuCustomRegisterLong(uint address, uint value, long firstWordCycle, long secondWordCycle)
        {
            WriteCpuCustomRegisterWord(address, (ushort)(value >> 16), firstWordCycle);
            WriteRawWord(address + 2, (ushort)value, secondWordCycle, default(CpuWritePolicy));
        }



        private void WriteCustomWord(
            AmigaBusRequester requester,
            ushort offset,
            ushort value,
            long cycle)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            if (requester != AmigaBusRequester.Host)
            {
                RememberChipDataBusWord(
                    value,
                    cycle,
                    wasDma: requester != AmigaBusRequester.Cpu);
            }

            var write = new CustomRegisterWriteContext(requester, offset, value, cycle);
            BeginCustomRegisterWrite(in write);
            try
            {
                DispatchCustomRegisterWrite(in write);
            }
            finally
            {
                EndCustomRegisterWrite();
            }
        }

        private void DispatchCustomRegisterWrite(in CustomRegisterWriteContext write)
        {
            ApplyBeamControlWrite(write.Offset, write.Value, write.Cycle);
            Paula.ScheduleWrite(write.Cycle, write.Offset, write.Value);
            Paula.AdvanceRegisterWritesTo(write.Cycle);
            Display.ScheduleWrite(write.Cycle, write.Offset, write.Value);
            Blitter.WriteRegister(write.Offset, write.Value, write.Cycle);
            Disk.WriteRegister(write.Offset, write.Value, write.Cycle);
            _hardwareScheduler.NotifyWorkScheduled(write.Cycle);
        }

        private void ApplyBeamControlWrite(ushort offset, ushort value, long cycle)
        {
            if (offset != 0x02A)
            {
                return;
            }

            var writeCycle = Math.Max(0, cycle);
            AdvanceRasterCoreTo(writeCycle);
            _palBeamClock.ApplyVposw(value, writeCycle);
            _nextVerticalBlankCycle = _palBeamClock.GetNextFrameStartCycle(writeCycle);
            _hardwareScheduler.NotifyWorkScheduled(_nextVerticalBlankCycle);
            NotifyCustomRegisterScheduleChanged(offset, writeCycle);
        }

        private void BeginCustomRegisterWrite(in CustomRegisterWriteContext write)
        {
            if (_customRegisterWriteContextDepth == 0)
            {
                _customRegisterWriteRequester = write.Requester;
                _customRegisterWriteOffset = write.Offset;
                _customRegisterWriteCycle = Math.Max(0, write.Cycle);
                _customRegisterWriteAffectsSchedule = false;
            }

            _customRegisterWriteContextDepth++;
        }

        private void EndCustomRegisterWrite()
        {
            if (_customRegisterWriteContextDepth <= 0)
            {
                return;
            }

            _customRegisterWriteContextDepth--;
            if (_customRegisterWriteContextDepth != 0)
            {
                return;
            }

            var affectsSchedule = _customRegisterWriteAffectsSchedule ||
                !CustomRegisterScheduleClassifier.IsKnownBusScheduleBenignWrite(_customRegisterWriteOffset);
            _hardwareScheduler.RecordCopperQuiescentCustomRegisterWrite(
                _customRegisterWriteRequester,
                _customRegisterWriteOffset,
                _customRegisterWriteCycle,
                affectsSchedule);
            _customRegisterWriteAffectsSchedule = false;
        }

        internal void NotifyCustomRegisterScheduleChanged(ushort offset, long cycle)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            if (_customRegisterWriteContextDepth > 0 &&
                offset == _customRegisterWriteOffset &&
                Math.Max(0, cycle) == _customRegisterWriteCycle)
            {
                _customRegisterWriteAffectsSchedule = true;
            }
        }

        private void WriteCustomByte(AmigaBusRequester requester, ushort offset, byte value, long cycle)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            var wordValue = (ushort)((value << 8) | value);
            WriteCustomWord(requester, wordOffset, wordValue, cycle);
        }

        private readonly struct CustomRegisterWriteContext
        {
            public CustomRegisterWriteContext(AmigaBusRequester requester, ushort offset, ushort value, long cycle)
            {
                Requester = requester;
                Offset = offset;
                Value = value;
                Cycle = cycle;
            }

            public AmigaBusRequester Requester { get; }

            public ushort Offset { get; }

            public ushort Value { get; }

            public long Cycle { get; }
        }

    }
}
