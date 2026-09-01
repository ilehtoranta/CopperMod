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
        internal const int DmaconBltpriVisibilityDelaySlots = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool PublishCustomRegisterState(ushort offset, ushort value, long cycle)
            => _customRegisterFile.PublishStoredValue(offset, value, cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PublishDmaconrState(long cycle)
            => _customRegisterFile.PublishStoredValue(
                0x002,
                (ushort)(Paula.Dmacon | Blitter.GetDmaconStatusBitsAt(cycle)),
                cycle);

        private void PublishGamePortRegisterState(long cycle)
        {
            PublishCustomRegisterState(0x00A, ReadGamePortData(0), cycle);
            PublishCustomRegisterState(0x00C, ReadGamePortData(1), cycle);
            PublishCustomRegisterState(0x016, ReadPotGoData(), cycle);
        }

        private void PublishAgnusRegisterState(long cycle)
        {
            if (!_chipset.SupportsEcsDmaRegisters)
            {
                return;
            }

            var beam = _beamClock.GetPosition(Math.Max(0, cycle));
            for (ushort offset = 0x1C0; offset <= 0x1E2; offset += 2)
            {
                if (offset != 0x1DA && _agnusRegisters.TryRead(offset, beam, out var value))
                {
                    PublishCustomRegisterState(offset, value, cycle);
                }
            }
        }

        private byte ReadCustomByte(ushort offset, long sampleCycle, bool hostRead = false)
        {
            var wordOffset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            var handler = hostRead
                ? _customRegisterFile.GetHostReadHandler(wordOffset)
                : _customRegisterFile.GetCpuReadHandler(wordOffset);
            return ReadCustomByteRouted(offset, sampleCycle, handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ReadCustomByteRouted(
            ushort offset,
            long sampleCycle,
            CustomRegisterReadHandler handler)
        {
            var wordOffset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            switch (handler)
            {
                case CustomRegisterReadHandler.BeamPosition:
                    return TryReadBeamPositionByte(offset, sampleCycle, out var beamPositionValue)
                        ? beamPositionValue
                        : Paula.ReadByte(offset);
                case CustomRegisterReadHandler.GamePort:
                    return TryReadGamePortCustomByte(offset, out var gamePortValue)
                        ? gamePortValue
                        : Paula.ReadByte(offset);
                case CustomRegisterReadHandler.Collision:
                {
                    var value = Display.ReadCollisionData();
                    return (offset & 1) == 0 ? (byte)(value >> 8) : (byte)value;
                }
                case CustomRegisterReadHandler.Disk:
                    return Disk.TryReadByte(offset, out var diskValue)
                        ? diskValue
                        : Paula.ReadByte(offset);
                case CustomRegisterReadHandler.PotGo:
                {
                    var value = ReadPotGoData();
                    return (offset & 1) == 0 ? (byte)(value >> 8) : (byte)value;
                }
                case CustomRegisterReadHandler.Dmaconr:
                {
                    var value = (ushort)(Paula.Dmacon | Blitter.GetDmaconStatusBitsAt(
                        sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle));
                    return (offset & 1) == 0 ? (byte)(value >> 8) : (byte)value;
                }
                case CustomRegisterReadHandler.Agnus:
                case CustomRegisterReadHandler.Paula:
                case CustomRegisterReadHandler.ChipDataBusLatch:
                case CustomRegisterReadHandler.None:
                    return Paula.ReadByte(offset);
                case CustomRegisterReadHandler.LastWriteMirror:
                    if (!_customRegisterFile.TryGetLastWriteValue(wordOffset, out var lastWrite))
                    {
                        return 0;
                    }

                    return (offset & 1) == 0 ? (byte)(lastWrite >> 8) : (byte)lastWrite;
                case CustomRegisterReadHandler.StoredValue:
                {
                    var value = _customRegisterFile.GetStoredValue(wordOffset);
                    return (offset & 1) == 0 ? (byte)(value >> 8) : (byte)value;
                }
                default:
                    return Paula.ReadByte(offset);
            }
        }

        private ushort ReadCustomWord(ushort offset, long sampleCycle, bool hostRead = false)
        {
            offset = (ushort)(offset & 0x01FE);
            var handler = hostRead
                ? _customRegisterFile.GetHostReadHandler(offset)
                : _customRegisterFile.GetCpuReadHandler(offset);
            return ReadCustomWordRouted(offset, sampleCycle, handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadCustomWordRouted(
            ushort offset,
            long sampleCycle,
            CustomRegisterReadHandler handler)
        {
            switch (handler)
            {
                case CustomRegisterReadHandler.ChipDataBusLatch:
                    return _chipDataBusLatch;
                case CustomRegisterReadHandler.Agnus:
                {
                    var beamCycle = sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle;
                    return _agnusRegisters.TryRead(offset, _beamClock.GetPosition(beamCycle), out var agnusValue)
                        ? agnusValue
                        : (ushort)0;
                }
                case CustomRegisterReadHandler.Dmaconr:
                    return (ushort)(Paula.Dmacon | Blitter.GetDmaconStatusBitsAt(
                        sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle));
                case CustomRegisterReadHandler.BeamPosition:
                    CalculateBeamPosition(
                        sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle,
                        out var vposr,
                        out var vhposr);
                    return offset == 0x004 ? vposr : vhposr;
                case CustomRegisterReadHandler.Disk:
                    return Disk.ReadWord(offset);
                case CustomRegisterReadHandler.GamePort:
                    return ReadGamePortData(offset == 0x00A ? 0 : 1);
                case CustomRegisterReadHandler.Collision:
                    return Display.ReadCollisionData();
                case CustomRegisterReadHandler.PotGo:
                    return ReadPotGoData();
                case CustomRegisterReadHandler.Paula:
                    return offset switch
                    {
                        0x010 => Paula.Adkcon,
                        0x01C => Paula.Intena,
                        0x01E => Paula.Intreq,
                        _ => (ushort)((Paula.ReadByte(offset) << 8) | Paula.ReadByte((ushort)(offset + 1)))
                    };
                case CustomRegisterReadHandler.LastWriteMirror:
                    return _customRegisterFile.TryGetLastWriteValue(offset, out var lastWrite)
                        ? lastWrite
                        : (ushort)0;
                case CustomRegisterReadHandler.StoredValue:
                    return _customRegisterFile.GetStoredValue(offset);
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
            var handler = _customRegisterFile.GetCpuReadHandler(offset);
            ushort value;
            if (offset == 0x000)
            {
                value = capturedLatch;
            }
            else if (handler == CustomRegisterReadHandler.None)
            {
                value = dmaValueVisible ? capturedLatch : (ushort)0xFFFF;
                RememberChipDataBusWord(value, grantedCycle, wasDma: false);
                WriteCustomWord(
                    AmigaBusRequester.Cpu,
                    offset,
                    capturedLatch,
                    grantedCycle,
                    cause: CustomRegisterWriteCause.UnreadableReadSideEffect);
                return value;
            }
            else
            {
                if (handler == CustomRegisterReadHandler.Disk)
                {
                    Disk.SynchronizeInputThrough(grantedCycle);
                }

                value = ReadCustomWordRouted(offset, sampleCycle, handler);
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
            var handler = _customRegisterFile.GetCpuReadHandler(wordOffset);
            if (handler == CustomRegisterReadHandler.Disk)
            {
                Disk.SynchronizeInputThrough(grantedCycle);
            }

            byte value;
            if (wordOffset == 0x000)
            {
                value = capturedByte;
            }
            else if (handler == CustomRegisterReadHandler.None)
            {
                value = dmaValueVisible ? capturedByte : (byte)0xFF;
                RememberChipDataBusByte(value, grantedCycle, wasDma: false);
                WriteCustomByte(
                    AmigaBusRequester.Cpu,
                    offset,
                    capturedByte,
                    grantedCycle,
                    CustomRegisterWriteCause.UnreadableReadSideEffect);
                return value;
            }
            else
            {
                value = ReadCustomByteRouted(offset, sampleCycle, handler);
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
            if (TryCalculateExternalResyncBeamPosition(targetCycle, out vposr, out vhposr))
            {
                return;
            }

            var beam = _beamClock.GetPosition(targetCycle);
            var horizontal = EncodeVhposrHorizontal(beam.BeamHorizontal, targetCycle);
            var registerLine = beam.BeamLine;
            vposr = (ushort)(((beam.IsLongFrame ? 1 : 0) << 15) | ((registerLine >> 8) & 0x0001));
            vhposr = (ushort)(((registerLine & 0x00FF) << 8) | horizontal);
        }

        private void ApplyExternalResyncControl(ushort bplcon0, long cycle)
        {
            const ushort externalResync = 0x0002;
            var enabled = (bplcon0 & externalResync) != 0;
            if (enabled == _externalResyncEnabled)
            {
                return;
            }

            if (!enabled)
            {
                ResetExternalResyncState();
                return;
            }

            var writeCycle = Math.Max(0, cycle);
            var beam = _beamClock.GetPosition(writeCycle);
            _externalResyncEnabled = true;
            _externalResyncLine = beam.BeamLine;
            _externalResyncLongFrame = beam.IsLongFrame;
            _externalResyncLineStartCycle = _beamClock.GetLineStartCycle(writeCycle);
        }

        private bool TryCalculateExternalResyncBeamPosition(
            long targetCycle,
            out ushort vposr,
            out ushort vhposr)
        {
            if (!_externalResyncEnabled)
            {
                vposr = 0;
                vhposr = 0;
                return false;
            }

            var lineCycles = _beamClock.GetLineCyclesAt(_externalResyncLineStartCycle);
            var elapsed = Math.Max(0, targetCycle - _externalResyncLineStartCycle);
            // With ERSY asserted and no external sync input, the current
            // Agnus line counter finishes in its native phase, then waits at
            // h=0 without advancing the vertical counter.
            var horizontal = elapsed < lineCycles
                ? Math.Max(0, (int)(elapsed / _rasterTiming.CpuCyclesPerColorClock) - 1)
                : 0;
            vposr = (ushort)(((_externalResyncLongFrame ? 1 : 0) << 15) |
                ((_externalResyncLine >> 8) & 0x0001));
            vhposr = (ushort)(((_externalResyncLine & 0x00FF) << 8) | horizontal);
            return true;
        }

        private void ResetExternalResyncState()
        {
            _externalResyncEnabled = false;
            _externalResyncLine = 0;
            _externalResyncLongFrame = false;
            _externalResyncLineStartCycle = 0;
        }

        private int EncodeVhposrHorizontal(int beamHorizontal, long cycle)
        {
            var physicalHorizontal = Math.Clamp(beamHorizontal, 0, 0xE2);
            // CPU reads sample VHPOSR in the granted RGA phase. The externally
            // visible horizontal counter is four color clocks ahead of that
            // internal slot coordinate; transfer completion does not advance
            // the sampled value.
            return (int)((physicalHorizontal + 4) % Math.Max(
                1,
                _beamClock.GetLineCyclesAt(cycle) / _rasterTiming.CpuCyclesPerColorClock));
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
            // M68kCore's BeginCpuBusAccessCycle has already applied the 68000
            // bus-cycle start delay. Adding another slot here inserts a bubble
            // before every split custom-register longword (unlike word and
            // ordinary longword accesses).
            var grantRequestCycle = requestedCycle;
            _hardwareScheduler.DrainForCpuAccess(target, address, grantRequestCycle, isWrite: true, AmigaBusAccessSize.Long);
            if (Blitter.BusPipelineActive && !AgnusLiveBlitterEnabled)
            {
                grantRequestCycle = _hardwareScheduler.ExecuteThroughBlitterCpuStall(grantRequestCycle);
            }

            AdvanceCpuAccessToCausalBusHorizon(target, ref grantRequestCycle);

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
            if (Blitter.BusPipelineActive && !AgnusLiveBlitterEnabled)
            {
                secondSearchCycle = _hardwareScheduler.ExecuteThroughBlitterCpuStall(secondSearchCycle);
            }

            AdvanceCpuAccessToCausalBusHorizon(target, ref secondSearchCycle);

            GrantCpuCustomRegisterLongWriteWordPhase(
                accessKind,
                address,
                secondSearchCycle,
                grantRequestCycle,
                out var secondWordCycle,
                out var completedCycle);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, firstCompletedCycle, secondWordCycle, isWrite: true);
            WriteRawWord(address + 2, (ushort)value, secondWordCycle, default(CpuWritePolicy));

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
            if ((!Blitter.BusPipelineActive && Blitter.PendingCompletionSignalCycle == long.MaxValue) ||
                !TryGrantCpuDataLongWordPhaseChronologically(
                    accessKind,
                    target,
                    address,
                    searchCycle,
                    requestedCycle,
                    isWrite: true,
                    out grantedCycle,
                    out completedCycle))
            {
                _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                    accessKind,
                    target,
                    address,
                    searchCycle,
                    requestedCycle,
                    isWrite: true,
                    out grantedCycle,
                    out completedCycle);
            }
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
            long cycle,
            CustomRegisterObservationWidth width = CustomRegisterObservationWidth.Word,
            CustomRegisterByteLane lane = CustomRegisterByteLane.None,
            CustomRegisterWriteCause cause = CustomRegisterWriteCause.Explicit)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            _customRegisterFile.RecordWrite(offset, value, width, lane, cycle, requester, cause);
            _customRegisterWrites.Add(new CustomRegisterWrite(cycle, offset, value));
            if (_chipset.HasAgaComponent)
            {
                if (offset == (ushort)CustomRegister.Fmode && (value & 0xC00F) != 0)
                {
                    TraceUnsupportedAgaFeature(offset, value, cycle, "FMODE wide bitplane/sprite fetch");
                }
                else if (offset == (ushort)CustomRegister.Bplcon0 &&
                    (value & 0x0800) != 0 && (value & 0x0010) != 0)
                {
                    TraceUnsupportedAgaFeature(offset, value, cycle, "HAM8 composition");
                }
                else if (offset == (ushort)CustomRegister.Bplcon3 && (value & 0x00C0) != 0)
                {
                    TraceUnsupportedAgaFeature(offset, value, cycle, "AGA sprite resolution");
                }
                else if (offset == (ushort)CustomRegister.Bplcon4 && (value & 0xFF00) != 0)
                {
                    TraceUnsupportedAgaFeature(offset, value, cycle, "AGA bitplane palette XOR mask");
                }
                else if (offset == (ushort)CustomRegister.Clxcon2 && value != 0)
                {
                    TraceUnsupportedAgaFeature(offset, value, cycle, "AGA extended collision control");
                }
            }
            if (requester != AmigaBusRequester.Host &&
                cause != CustomRegisterWriteCause.UnreadableReadSideEffect)
            {
                RememberChipDataBusWord(
                    value,
                    cycle,
                    wasDma: requester != AmigaBusRequester.Cpu);
            }

            var write = new CustomRegisterWriteContext(requester, offset, value, cycle);
            if (offset == 0x100)
            {
                ApplyExternalResyncControl(value, cycle);
            }
            _agnusBusExecutor.ObserveDisplayControlWrite(requester, offset, value, cycle);
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
            ref readonly var entry = ref _customRegisterFile.Get(write.Offset);
            var targets = entry.WriteTargets;
            _customRegisterFile.ApplyRegisterFileWrite(write.Offset, write.Value, write.Cycle);

            var deferBltpri =
                write.Requester != AmigaBusRequester.Host &&
                write.Offset == 0x096 &&
                (write.Value & 0x0400) != 0;
            var immediateValue = deferBltpri
                ? (ushort)(write.Value & ~0x0400)
                : write.Value;
            var hasImmediateDeviceWrite =
                !deferBltpri ||
                (immediateValue & 0x7FFF) != 0;

            // Custom-register writes are broadcast on the shared chip fabric.
            // Ownership metadata is descriptive; each device filters offsets.
            if (hasImmediateDeviceWrite)
            {
                ApplyAgnusRegisterWrite(write.Offset, immediateValue, write.Cycle);

                if (write.Offset == 0x096)
                {
                    // Non-priority DMACON bits retain the existing write
                    // boundary. In particular, a DMA-gated blit must not
                    // resume from its stale trigger cycle when this write
                    // enables it.
                    _hardwareScheduler.SynchronizeBlitterThrough(write.Cycle);
                    Blitter.AdvanceDmaGateHorizonTo(write.Cycle);
                }

                if ((targets & CustomRegisterWriteTarget.Paula) != 0)
                {
                    Paula.ScheduleWrite(write.Cycle, write.Offset, immediateValue);
                    Paula.AdvanceTo(write.Cycle);
                }

                Display.ScheduleWrite(new AgnusDisplayRegisterWrite(
                    GetDisplayWriteCycle(in write),
                    write.Offset,
                    immediateValue));

                _hardwareScheduler.SynchronizeBlitterThrough(write.Cycle);
                Blitter.WriteRegister(write.Offset, immediateValue, write.Cycle);

                Disk.WriteRegister(write.Offset, immediateValue, write.Cycle);
            }

            if (deferBltpri)
            {
                var effectCycle =
                    write.Cycle +
                    (DmaconBltpriVisibilityDelaySlots *
                     AgnusChipSlotScheduler.SlotCycles);
                var priorityValue = (ushort)(
                    (write.Value & 0x8000) |
                    0x0400);

                // BLTPRI is sampled by Agnus two DMA cycles after the DMACON
                // bus transfer. Queue that one bit on the existing Paula and
                // display timelines so Copper can publish intervening slots
                // before the priority boundary becomes visible.
                if ((targets & CustomRegisterWriteTarget.Paula) != 0)
                {
                    Paula.ScheduleWrite(effectCycle, write.Offset, priorityValue);
                }

                Display.ScheduleWrite(new AgnusDisplayRegisterWrite(
                    effectCycle,
                    write.Offset,
                    priorityValue));
                _hardwareScheduler.NotifyWorkScheduled(effectCycle);
            }

            if (entry.StorageMode == CustomRegisterStorageMode.DevicePublished &&
                _agnusRegisters.TryRead(
                    write.Offset,
                    _beamClock.GetPosition(Math.Max(0, write.Cycle)),
                    out var storedValue))
            {
                _customRegisterFile.PublishStoredValue(write.Offset, storedValue, write.Cycle);
            }

            _hardwareScheduler.NotifyWorkScheduled(write.Cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetDisplayWriteCycle(in CustomRegisterWriteContext write)
            // Physical OCS transfers enter the shared display event stream at
            // OUT. Converting a cycle into a pixel must not postpone an older
            // CPU palette write until after a newer Copper palette write.
            => Display.UsesPhysicalCopperPipeline
                ? write.Cycle
                : GetDisplayWriteCycle(write.Requester, write.Offset, write.Cycle);

        internal static long GetDisplayWriteCycle(
            AmigaBusRequester requester,
            ushort offset,
            long cycle)
        {
            if (requester != AmigaBusRequester.Cpu ||
                !CustomRegisterScheduleClassifier.IsColorRegister(offset))
            {
                return cycle;
            }

            // Legacy presentation adapter for profiles not yet using physical
            // display events. OCS PAL bypasses this conversion: it maps the
            // unchanged OUT cycle through the Denise counter instead.
            return cycle + (6 * AgnusChipSlotScheduler.SlotCycles);
        }


        private void ApplyAgnusRegisterWrite(ushort offset, ushort value, long cycle)
        {
            var writeCycle = Math.Max(0, cycle);
            var result = _agnusRegisters.Write(offset, value, writeCycle);
            if (!result.BeamStateChanged)
            {
                return;
            }

            AdvanceRasterCoreTo(writeCycle);
            if (offset == AgnusRegisterBank.Vposw)
            {
                _beamClock.ApplyVposw(value, writeCycle);
                Display.RefreshLiveFrameStopAfterTimingChange();
                _agnusBusExecutor.UpdateGeometry(_beamClock.GetPosition(writeCycle).FrameCycles);
                _lineCycles = _beamClock.MaximumLineCycles;
                RecalculateRasterEvents(writeCycle);
            }
            else if (result.TimingChanged)
            {
                if (offset == AgnusRegisterBank.Hhposw)
                {
                    _beamClock.ApplyHorizontalPosition(_agnusRegisters.HhposWrite, writeCycle);
                }
                else
                {
                    var oldBeam = _beamClock.GetPosition(writeCycle);
                    _beamClock.ApplyGeometry(
                        _agnusRegisters.EffectiveColorClocksPerLine(_rasterTiming),
                        _agnusRegisters.EffectiveFrameLines(_rasterTiming, oldBeam.IsLongFrame),
                        writeCycle);
                }
                _agnusBusExecutor.UpdateGeometry(_beamClock.GetPosition(writeCycle).FrameCycles);
                _lineCycles = _beamClock.MaximumLineCycles;
                RecalculateRasterEvents(writeCycle);
            }
            else if (result.RasterEventsChanged)
            {
                RecalculateRasterEvents(writeCycle);
            }

            _nextVerticalBlankCycle = GetNextVerticalBlankCycle(writeCycle);
            _hardwareScheduler.NotifyWorkScheduled(_nextVerticalBlankCycle);
            NotifyCustomRegisterScheduleChanged(
                offset,
                writeCycle,
                CustomRegisterScheduleClassifier.GetPotentialImpact(_chipset, offset) &
                    HardwareScheduleImpact.Raster);
        }

        private void BeginCustomRegisterWrite(in CustomRegisterWriteContext write)
        {
            if (_customRegisterWriteContextDepth == 0)
            {
                _customRegisterWriteRequester = write.Requester;
                _customRegisterWriteOffset = write.Offset;
                _customRegisterWriteCycle = Math.Max(0, write.Cycle);
                _customRegisterWriteImpact = HardwareScheduleImpact.None;
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

            var impact = _customRegisterWriteImpact |
                CustomRegisterScheduleClassifier.GetPotentialImpact(_chipset, _customRegisterWriteOffset);
            _hardwareScheduler.RecordCopperQuiescentCustomRegisterWrite(
                _customRegisterWriteRequester,
                _customRegisterWriteOffset,
                _customRegisterWriteCycle,
                impact);
            _customRegisterWriteImpact = HardwareScheduleImpact.None;
        }

        internal void NotifyCustomRegisterScheduleChanged(
            ushort offset,
            long cycle,
            HardwareScheduleImpact impact)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            _agnusBusExecutor.InvalidateCpuVisibilityAgenda();

            // Until the unified executor is the production owner, legacy device
            // writes may legitimately be observed behind its diagnostic horizon.
            // They must not mutate (or be rejected by) the dormant executor.
            if (_agnusBusExecutor.ProductionEnabled &&
                (impact & CustomRegisterScheduleClassifier.DisplayDmaImpacts) != 0)
            {
                _agnusBusExecutor.ApplyControlEvent(
                    new AgnusControlEvent(Math.Max(0, cycle), AgnusBusAgendaSource.Display));
            }

            if (_agnusBusExecutor.ProductionEnabled &&
                (impact & HardwareScheduleImpact.Blitter) != 0)
            {
                _agnusBusExecutor.ApplyControlEvent(
                    new AgnusControlEvent(Math.Max(0, cycle), AgnusBusAgendaSource.Blitter));
            }

            if (_agnusBusExecutor.ProductionEnabled &&
                (impact & HardwareScheduleImpact.Audio) != 0)
            {
                _agnusBusExecutor.ApplyControlEvent(
                    new AgnusControlEvent(Math.Max(0, cycle), AgnusBusAgendaSource.Paula));
            }

            if (_agnusBusExecutor.ProductionEnabled &&
                (impact & HardwareScheduleImpact.Disk) != 0)
            {
                _agnusBusExecutor.ApplyControlEvent(
                    new AgnusControlEvent(Math.Max(0, cycle), AgnusBusAgendaSource.Disk));
            }

            if (_customRegisterWriteContextDepth > 0 &&
                offset == _customRegisterWriteOffset &&
                Math.Max(0, cycle) == _customRegisterWriteCycle)
            {
                _customRegisterWriteImpact |= impact;
            }
        }

        private void WriteCustomByte(
            AmigaBusRequester requester,
            ushort offset,
            byte value,
            long cycle,
            CustomRegisterWriteCause cause = CustomRegisterWriteCause.Explicit)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            var wordValue = (ushort)((value << 8) | value);
            WriteCustomWord(
                requester,
                wordOffset,
                wordValue,
                cycle,
                CustomRegisterObservationWidth.Byte,
                (offset & 1) == 0 ? CustomRegisterByteLane.High : CustomRegisterByteLane.Low,
                cause);
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
