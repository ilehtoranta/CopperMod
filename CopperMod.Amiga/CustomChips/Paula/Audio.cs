/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CopperMod.Amiga.CustomChips.Paula
{
    internal sealed class Paula
    {
        private const ushort IntenaMasterEnable = 0x4000;
        private const ushort AudioInterruptMask = 0x0780;
        private const ushort WritableDmaconMask = 0x07FF;
        private const ushort WritableIntreqMask = 0x3FFF;
        private const ushort IdleSerdatr = 0x3000;
        private const float VoiceScale = 0.25f;
        private const int MaxCapturedWrites = 65536;
        private const int MaxPendingInterruptEvents = 65536;
        private readonly AmigaBus _bus;
        private readonly PaulaTimelineState _audioTimeline;
        private readonly PaulaTimelineState _registerTimeline;
        private readonly List<PendingWrite> _pendingWrites = new List<PendingWrite>();
        private readonly PaulaDmaReadLatchQueue[] _dmaReadLatchQueues = CreateDmaReadLatchQueues();
        private readonly List<PaulaInterruptEvent> _pendingInterrupts = new List<PaulaInterruptEvent>();
        private readonly PaulaInterruptEvent[] _drainedInterruptBuffer = new PaulaInterruptEvent[MaxPendingInterruptEvents];
        private readonly ReusableReadOnlyList<PaulaInterruptEvent> _drainedInterrupts = new ReusableReadOnlyList<PaulaInterruptEvent>();
        private readonly BoundedWriteLog _writes = new BoundedWriteLog(MaxCapturedWrites);
        private readonly byte[] _registerBytes = new byte[0x200];
        private readonly long[] _cpuInterruptReleaseCycles = new long[14];
        private ushort _vposr;
        private ushort _vhposr;
        private ushort _lastCpuActiveInterruptBits;
        private long _copperInterruptRecognitionCycle = long.MinValue;
        private int _interruptSourcePendingWriteIndex;
        private ulong _registerWakeVersion;
        private ulong _registerWakeCandidateVersion = ulong.MaxValue;
        private long _registerWakeCandidateCycle = long.MaxValue;
        private ulong _dmaWakeCandidateVersion = ulong.MaxValue;
        private long _dmaWakeCandidateCycle = long.MaxValue;
        private ulong _interruptSourceWakeCandidateVersion = ulong.MaxValue;
        private long _interruptSourceWakeCandidateCycle = long.MaxValue;
        private ulong _pendingInterruptSourceWriteCandidateVersion = ulong.MaxValue;
        private int _pendingInterruptSourceWriteCandidateIndex = -1;
        private long _pendingInterruptSourceWriteCandidateCycle = long.MaxValue;
        private float[][]? _captureSamples;
        private int _captureFrameIndex;
        private int _captureSampleRate;
        private long _startDmaWordOutputCount;
        private long _interruptSourceDmaSkippedCount;
        private long _interruptSourceDmaForcedCatchUpCount;
        private long _paulaDmaWordReservationCount;
        private long _registerDmaFastForwardIterationCount;
        private bool _hotCounterDiagnosticsEnabled;
        private bool _registerDmaFastForwardEnabled;

        public Paula(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            var cpuCyclesPerColorClock = bus.RasterTiming.CpuCyclesPerColorClock;
            _audioTimeline = new PaulaTimelineState(cpuCyclesPerColorClock);
            _registerTimeline = new PaulaTimelineState(cpuCyclesPerColorClock);
            PublishResetReadState();
        }

        public IReadOnlyList<CustomRegisterWrite> Writes => _writes;

        public ushort Adkcon => _registerTimeline.Adkcon;

        public ushort Dmacon => _registerTimeline.Dmacon;

        internal long StartDmaWordOutputCount => _startDmaWordOutputCount;

        internal long InterruptSourceDmaSkippedCount => _interruptSourceDmaSkippedCount;

        internal long InterruptSourceDmaForcedCatchUpCount => _interruptSourceDmaForcedCatchUpCount;

        internal long PaulaDmaWordReservationCount => _paulaDmaWordReservationCount;

        internal long RegisterDmaFastForwardIterationCount => _registerDmaFastForwardIterationCount;

        internal bool RegisterDmaFastForwardEnabled
        {
            get => _registerDmaFastForwardEnabled;
            set => _registerDmaFastForwardEnabled = value;
        }

        internal bool HotCounterDiagnosticsEnabled
        {
            set => _hotCounterDiagnosticsEnabled = value;
        }

        public ushort Intena => _registerTimeline.Intena;

        public ushort Intreq => _registerTimeline.Intreq;

        internal bool AreAllAudioInterruptsPending => (_registerTimeline.Intreq & AudioInterruptMask) == AudioInterruptMask;

        internal ulong RegisterWakeVersion => _registerWakeVersion;

        public ushort ActiveInterruptBits
        {
            get
            {
                if ((_registerTimeline.Intena & IntenaMasterEnable) == 0)
                {
                    return 0;
                }

                return (ushort)(_registerTimeline.Intreq & _registerTimeline.Intena & 0x3FFF);
            }
        }

        public void Reset()
        {
            Array.Clear(_registerBytes);
            _pendingWrites.Clear();
            ResetDmaReadLatchQueues();
            _pendingInterrupts.Clear();
            _writes.Clear();
            Array.Clear(_cpuInterruptReleaseCycles);
            _audioTimeline.Reset();
            _registerTimeline.Reset();
            _vposr = 0;
            _vhposr = 0;
            _lastCpuActiveInterruptBits = 0;
            _copperInterruptRecognitionCycle = long.MinValue;
            _interruptSourcePendingWriteIndex = 0;
            InvalidateRegisterWakeCandidateCache();
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            _startDmaWordOutputCount = 0;
            _interruptSourceDmaSkippedCount = 0;
            _interruptSourceDmaForcedCatchUpCount = 0;
            _paulaDmaWordReservationCount = 0;
            _registerDmaFastForwardIterationCount = 0;
            PublishResetReadState();
        }

        private void PublishResetReadState()
        {
            _bus.PublishCustomRegisterState(0x010, _registerTimeline.Adkcon, 0);
            _bus.PublishCustomRegisterState(0x012, 0, 0);
            _bus.PublishCustomRegisterState(0x014, 0, 0);
            _bus.PublishCustomRegisterState(0x018, IdleSerdatr, 0);
            _bus.PublishCustomRegisterState(0x01C, _registerTimeline.Intena, 0);
            _bus.PublishCustomRegisterState(0x01E, _registerTimeline.Intreq, 0);
        }

        public byte ReadByte(ushort offset)
        {
            if (offset == 0x004)
            {
                return (byte)(_vposr >> 8);
            }

            if (offset == 0x005)
            {
                return (byte)_vposr;
            }

            if (offset == 0x010)
            {
                return (byte)(_registerTimeline.Adkcon >> 8);
            }

            if (offset == 0x018)
            {
                return (byte)(IdleSerdatr >> 8);
            }

            if (offset == 0x019)
            {
                return (byte)((int)IdleSerdatr & 0x00FF);
            }

            if (offset == 0x011)
            {
                return (byte)_registerTimeline.Adkcon;
            }

            if (offset == 0x002)
            {
                return (byte)((_registerTimeline.Dmacon | _bus.Blitter.DmaconStatusBits) >> 8);
            }

            if (offset == 0x003)
            {
                return (byte)(_registerTimeline.Dmacon | _bus.Blitter.DmaconStatusBits);
            }

            if (offset == 0x006)
            {
                return (byte)(_vhposr >> 8);
            }

            if (offset == 0x007)
            {
                return (byte)_vhposr;
            }

            if (offset == 0x01C)
            {
                return (byte)(_registerTimeline.Intena >> 8);
            }

            if (offset == 0x01D)
            {
                return (byte)_registerTimeline.Intena;
            }

            if (offset == 0x01E)
            {
                return (byte)(_registerTimeline.Intreq >> 8);
            }

            if (offset == 0x01F)
            {
                return (byte)_registerTimeline.Intreq;
            }

            return offset < _registerBytes.Length ? _registerBytes[offset] : (byte)0;
        }

        public void SetBeamPosition(int verticalLine, int horizontalPosition, bool longFrame)
        {
            verticalLine = Math.Clamp(verticalLine, 0, 0x1FF);
            horizontalPosition = Math.Clamp(horizontalPosition, 0, 0xFF);
            _vposr = (ushort)(((longFrame ? 1 : 0) << 15) | ((verticalLine >> 8) & 0x0001));
            _vhposr = (ushort)(((verticalLine & 0x00FF) << 8) | horizontalPosition);
        }

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            var impact = CustomRegisterScheduleClassifier.GetPotentialImpact(_bus.Chipset, offset) &
                HardwareScheduleImpact.Audio;
            if (impact != HardwareScheduleImpact.None)
            {
                _bus.NotifyCustomRegisterScheduleChanged(offset, cycle, impact);
            }

            var pending = new PendingWrite(cycle, offset, value);
            _writes.Add(new CustomRegisterWrite(cycle, offset, value));
            var insertIndex = _pendingWrites.Count;
            var consumedFloor = Math.Min(_audioTimeline.PendingWriteIndex, _registerTimeline.PendingWriteIndex);
            while (insertIndex > consumedFloor && _pendingWrites[insertIndex - 1].Cycle > cycle)
            {
                insertIndex--;
            }

            _pendingWrites.Insert(insertIndex, pending);
            PreserveTimelinePendingWriteIndex(_audioTimeline, insertIndex, cycle, PaulaTimelineKind.Audio, offset, value);
            PreserveTimelinePendingWriteIndex(_registerTimeline, insertIndex, cycle, PaulaTimelineKind.Register, offset, value);
            InvalidateRegisterWakeCandidateCache();
        }

        private void PreserveTimelinePendingWriteIndex(
            PaulaTimelineState timeline,
            int insertIndex,
            long cycle,
            PaulaTimelineKind kind,
            ushort offset,
            ushort value)
        {
            if (insertIndex < timeline.PendingWriteIndex ||
                (insertIndex == timeline.PendingWriteIndex && cycle < timeline.LastCycle))
            {
                timeline.PendingWriteIndex++;
                if (cycle <= timeline.LastCycle)
                {
                    ApplyWrite(timeline, kind, offset, value);
                }
            }
        }

        public IReadOnlyList<PaulaInterruptEvent> DrainInterrupts()
        {
            if (_pendingInterrupts.Count == 0)
            {
                return Array.Empty<PaulaInterruptEvent>();
            }

            var count = Math.Min(_pendingInterrupts.Count, _drainedInterruptBuffer.Length);
            for (var i = 0; i < count; i++)
            {
                _drainedInterruptBuffer[i] = _pendingInterrupts[i];
            }

            SortPaulaInterrupts(_drainedInterruptBuffer, count);
            _pendingInterrupts.Clear();
            _drainedInterrupts.Reset(_drainedInterruptBuffer, count);
            return _drainedInterrupts;
        }

        private static void SortPaulaInterrupts(PaulaInterruptEvent[] events, int count)
        {
            for (var i = 1; i < count; i++)
            {
                var value = events[i];
                var j = i - 1;
                while (j >= 0 && ComparePaulaInterrupts(events[j], value) > 0)
                {
                    events[j + 1] = events[j];
                    j--;
                }

                events[j + 1] = value;
            }
        }

        private static int ComparePaulaInterrupts(PaulaInterruptEvent left, PaulaInterruptEvent right)
        {
            var cycleCompare = left.Cycle.CompareTo(right.Cycle);
            return cycleCompare != 0 ? cycleCompare : left.Channel.CompareTo(right.Channel);
        }

        public int GetHighestPendingInterruptLevel()
            => GetHighestInterruptLevel(ActiveInterruptBits);

        public int GetHighestCpuVisibleInterruptLevel(long cycle)
            => GetHighestInterruptLevel(GetCpuVisibleInterruptBits(cycle));

        internal long? GetCpuInterruptReleaseCycleForLevel(int level, long cycle)
        {
            RefreshCpuInterruptVisibility(cycle);
            var active = ActiveInterruptBits;
            long? releaseCycle = null;
            for (var bitIndex = 0; bitIndex < _cpuInterruptReleaseCycles.Length; bitIndex++)
            {
                var bit = (ushort)(1 << bitIndex);
                if ((active & bit) == 0 || GetInterruptLevelForBit(bit) != level)
                {
                    continue;
                }

                var candidate = _cpuInterruptReleaseCycles[bitIndex];
                if (candidate <= cycle && (!releaseCycle.HasValue || candidate < releaseCycle.Value))
                {
                    releaseCycle = candidate;
                }
            }

            return releaseCycle;
        }

        public long? GetNextCpuVisibleInterruptCycle(long currentCycle, long targetCycle, int cpuInterruptMask)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            RefreshCpuInterruptVisibility(currentCycle);
            var active = ActiveInterruptBits;
            if (active == 0)
            {
                return null;
            }

            long? candidate = null;
            for (var bitIndex = 0; bitIndex < _cpuInterruptReleaseCycles.Length; bitIndex++)
            {
                var bit = (ushort)(1 << bitIndex);
                if ((active & bit) == 0)
                {
                    continue;
                }

                var level = GetInterruptLevelForBit(bit);
                if (level <= 0 || (cpuInterruptMask >= 0 && level <= (cpuInterruptMask & 0x07)))
                {
                    continue;
                }

                var releaseCycle = Math.Max(currentCycle + 1, _cpuInterruptReleaseCycles[bitIndex]);
                if (releaseCycle <= targetCycle && (!candidate.HasValue || releaseCycle < candidate.Value))
                {
                    candidate = releaseCycle;
                }
            }

            return candidate;
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            var candidate = GetRegisterWakeCandidateCycle();
            return candidate == long.MaxValue
                ? null
                : ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        internal long? GetNextDmaWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            var candidate = GetDmaWakeCandidateCycle();
            return candidate == long.MaxValue
                ? null
                : ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        internal long? GetNextInterruptSourceWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            var candidate = GetInterruptSourceWakeCandidateCycle();
            return candidate == long.MaxValue
                ? null
                : ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        internal long? GetNextCpuWakeCandidateCycle(long currentCycle, long targetCycle, int cpuInterruptMask)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            var candidate = GetCpuWakeCandidateCycle(cpuInterruptMask);
            return candidate == long.MaxValue
                ? null
                : ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        internal long? GetCpuVisibleInterruptRequestCycle(
            ushort bit,
            long requestCycle,
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask)
        {
            bit = (ushort)(bit & 0x3FFF);
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            requestCycle = Math.Max(0, requestCycle);
            if (bit == 0 ||
                targetCycle <= currentCycle ||
                !IsInterruptEnabled(bit))
            {
                return null;
            }

            var level = GetInterruptLevelForBit(bit);
            if (level <= 0 || (cpuInterruptMask >= 0 && level <= (cpuInterruptMask & 0x07)))
            {
                return null;
            }

            var releaseCycle = Math.Max(currentCycle + 1, GetCpuInterruptReleaseCycle(bit, requestCycle));
            return releaseCycle <= targetCycle ? releaseCycle : null;
        }

        internal bool HasCpuWakeWorkThrough(long targetCycle, int cpuInterruptMask)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < _registerTimeline.LastCycle)
            {
                return false;
            }

            return GetCpuWakeCandidateCycle(cpuInterruptMask) <= targetCycle;
        }

        public PaulaChannelSnapshot GetChannelSnapshot(int channel)
        {
            if ((uint)channel >= _audioTimeline.Channels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Paula channel index is outside the supported range.");
            }

            return _audioTimeline.Channels[channel].GetSnapshot();
        }

        public void BeginChannelCapture(int frames, int sampleRate)
        {
            if (frames <= 0 || sampleRate <= 0)
            {
                _captureSamples = null;
                _captureFrameIndex = 0;
                _captureSampleRate = 0;
                return;
            }

            _captureSamples = new float[_audioTimeline.Channels.Length][];
            for (var i = 0; i < _captureSamples.Length; i++)
            {
                _captureSamples[i] = new float[frames];
            }

            _captureFrameIndex = 0;
            _captureSampleRate = sampleRate;
        }

        public AmigaChannelWaveform? FinishChannelCapture()
        {
            if (_captureSamples == null)
            {
                return null;
            }

            var channels = new AmigaChannelWaveformChannel[_captureSamples.Length];
            for (var i = 0; i < channels.Length; i++)
            {
                channels[i] = new AmigaChannelWaveformChannel(i, _captureSamples[i], IsActive(_captureSamples[i]));
            }

            var result = new AmigaChannelWaveform(channels, _captureFrameIndex, _captureSampleRate);
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            return result;
        }

        public void RenderSample(
            long targetCycle,
            Span<float> destination,
            int frame,
            int channels,
            bool advanceRegisterObservable = true)
        {
            AdvanceAudioTo(targetCycle);
            if (advanceRegisterObservable)
            {
                AdvanceRegisterObservableTo(targetCycle);
            }

            var left = 0.0f;
            var right = 0.0f;
            var capture = _captureSamples;
            for (var i = 0; i < _audioTimeline.Channels.Length; i++)
            {
                if (IsAttachedSource(_audioTimeline, i))
                {
                    if (capture != null && _captureFrameIndex < capture[i].Length)
                    {
                        capture[i][_captureFrameIndex] = 0.0f;
                    }

                    continue;
                }

                var channel = _audioTimeline.Channels[i];
                var raw = channel.CurrentSample / 128.0f;
                var sample = raw * (channel.Volume / 64.0f) * VoiceScale;
                if (capture != null && _captureFrameIndex < capture[i].Length)
                {
                    capture[i][_captureFrameIndex] = sample;
                }

                if (i == 0 || i == 3)
                {
                    left += sample;
                }
                else
                {
                    right += sample;
                }
            }

            var offset = frame * channels;
            if (channels == 1)
            {
                destination[offset] = Math.Clamp((left + right) * 0.5f, -1.0f, 1.0f);
            }
            else
            {
                destination[offset] = Math.Clamp(left, -1.0f, 1.0f);
                destination[offset + 1] = Math.Clamp(right, -1.0f, 1.0f);
                for (var extra = 2; extra < channels; extra++)
                {
                    destination[offset + extra] = Math.Clamp((left + right) * 0.5f, -1.0f, 1.0f);
                }
            }

            if (capture != null)
            {
                _captureFrameIndex++;
            }
        }

        public void AdvanceTo(long targetCycle)
        {
            AdvanceTimelineTo(_audioTimeline, targetCycle, PaulaTimelineKind.Audio);
            AdvanceTimelineTo(_registerTimeline, targetCycle, PaulaTimelineKind.Register);
        }

        public void AdvanceRegisterObservableTo(long targetCycle)
            => AdvanceTimelineTo(_registerTimeline, targetCycle, PaulaTimelineKind.Register);

        internal void AdvanceDmaObservableTo(long targetCycle)
            => AdvanceTimelineTo(_registerTimeline, targetCycle, PaulaTimelineKind.Register);

        internal void AdvanceInterruptSourcesTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (WillAudioInterruptBitsRemainPendingAfterInterruptSourceWrites(targetCycle))
            {
                ApplyInterruptSourceWritesTo(targetCycle);
                _interruptSourceDmaSkippedCount++;
                return;
            }

            _interruptSourceDmaForcedCatchUpCount++;
            AdvanceDmaObservableTo(targetCycle);
        }

        public void AdvanceRegisterWritesTo(long targetCycle)
            => ApplyRegisterWritesTo(targetCycle);

        internal bool HasRegisterObservableWorkThrough(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < _registerTimeline.LastCycle)
            {
                return false;
            }

            return GetRegisterWakeCandidateCycle() <= targetCycle;
        }

        internal bool HasDmaWorkThrough(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < _registerTimeline.LastCycle)
            {
                return false;
            }

            return GetDmaWakeCandidateCycle() <= targetCycle;
        }

        internal bool HasInterruptSourceWorkThrough(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            return GetInterruptSourceWakeCandidateCycle() <= targetCycle;
        }

        private void AdvanceAudioTo(long targetCycle)
            => AdvanceTimelineTo(_audioTimeline, targetCycle, PaulaTimelineKind.Audio);

        private void AdvanceTimelineTo(PaulaTimelineState timeline, long targetCycle, PaulaTimelineKind kind)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < timeline.LastCycle ||
                (targetCycle == timeline.LastCycle && !HasPendingWriteThrough(timeline, targetCycle)))
            {
                return;
            }

            while (timeline.PendingWriteIndex < _pendingWrites.Count &&
                _pendingWrites[timeline.PendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[timeline.PendingWriteIndex++];
                var writeCycle = Math.Max(timeline.LastCycle, write.Cycle);
                AdvanceChannels(timeline, kind, writeCycle);
                ApplyWrite(timeline, kind, write.Offset, write.Value);
            }

            AdvanceChannels(timeline, kind, targetCycle);
            if (kind == PaulaTimelineKind.Register)
            {
                SyncInterruptSourcePendingWriteIndexTo(timeline.PendingWriteIndex);
                RefreshCpuInterruptVisibility(targetCycle);
                InvalidateRegisterWakeCandidateCache();
            }

            CompactPendingWrites();
            CompactDmaReadLatches();
        }

        private void ApplyRegisterWritesTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < _registerTimeline.LastCycle ||
                (targetCycle == _registerTimeline.LastCycle && !HasPendingWriteThrough(_registerTimeline, targetCycle)))
            {
                return;
            }

            while (_registerTimeline.PendingWriteIndex < _pendingWrites.Count &&
                _pendingWrites[_registerTimeline.PendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_registerTimeline.PendingWriteIndex++];
                var writeCycle = Math.Max(_registerTimeline.LastCycle, write.Cycle);
                AdvanceChannels(_registerTimeline, PaulaTimelineKind.Register, writeCycle);
                ApplyWrite(_registerTimeline, PaulaTimelineKind.Register, write.Offset, write.Value);
            }

            SyncInterruptSourcePendingWriteIndexTo(_registerTimeline.PendingWriteIndex);
            RefreshCpuInterruptVisibility(targetCycle);
            InvalidateRegisterWakeCandidateCache();
            CompactPendingWrites();
            CompactDmaReadLatches();
        }

        private void ApplyInterruptSourceWritesTo(long targetCycle)
        {
            if (_interruptSourcePendingWriteIndex >= _pendingWrites.Count ||
                _pendingWrites[_interruptSourcePendingWriteIndex].Cycle > targetCycle)
            {
                return;
            }

            while (_interruptSourcePendingWriteIndex < _pendingWrites.Count &&
                _pendingWrites[_interruptSourcePendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_interruptSourcePendingWriteIndex++];
                switch (write.Offset)
                {
                    case 0x096:
                        var dmaconMask = (ushort)(write.Value & WritableDmaconMask);
                        if ((write.Value & 0x8000) != 0)
                        {
                            _registerTimeline.Dmacon |= dmaconMask;
                        }
                        else
                        {
                            _registerTimeline.Dmacon &= (ushort)~dmaconMask;
                        }

                        WriteRegisterWord(0x002, _registerTimeline.Dmacon, write.Cycle);
                        break;
                    case 0x09A:
                        ApplySetClear(ref _registerTimeline.Intena, write.Value);
                        WriteRegisterWord(0x01C, _registerTimeline.Intena, write.Cycle);
                        RefreshCpuInterruptVisibility(
                            write.Cycle,
                            AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles);
                        break;
                    case 0x09C:
                        var mask = (ushort)(write.Value & WritableIntreqMask);
                        if ((write.Value & 0x8000) != 0)
                        {
                            _registerTimeline.Intreq |= mask;
                            QueuePendingEnabledAudioInterrupts(write.Cycle, mask);
                        }
                        else
                        {
                            _registerTimeline.Intreq &= (ushort)~mask;
                        }

                        WriteRegisterWord(0x01E, _registerTimeline.Intreq, write.Cycle);
                        RefreshCpuInterruptVisibility(
                            write.Cycle,
                            AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles);
                        break;
                    case 0x09E:
                        ApplySetClear(ref _registerTimeline.Adkcon, write.Value);
                        WriteRegisterWord(0x010, _registerTimeline.Adkcon, write.Cycle);
                        break;
                }
            }

            InvalidateRegisterWakeCandidateCache();
            CompactPendingWrites();
        }

        private bool WillAudioInterruptBitsRemainPendingAfterInterruptSourceWrites(long targetCycle)
        {
            if (GetPendingInterruptSourceWriteCycle() > targetCycle)
            {
                return AreAllAudioInterruptsPending;
            }

            var audioBits = (ushort)(_registerTimeline.Intreq & AudioInterruptMask);
            var startIndex = _pendingInterruptSourceWriteCandidateIndex >= 0
                ? _pendingInterruptSourceWriteCandidateIndex
                : _interruptSourcePendingWriteIndex;
            for (var i = startIndex; i < _pendingWrites.Count; i++)
            {
                var write = _pendingWrites[i];
                if (write.Cycle > targetCycle)
                {
                    break;
                }

                if (write.Offset != 0x09C)
                {
                    continue;
                }

                var mask = (ushort)(write.Value & WritableIntreqMask & AudioInterruptMask);
                if ((write.Value & 0x8000) != 0)
                {
                    audioBits |= mask;
                }
                else
                {
                    audioBits &= (ushort)~mask;
                }
            }

            return audioBits == AudioInterruptMask;
        }

        private void SyncInterruptSourcePendingWriteIndexTo(int pendingWriteIndex)
        {
            if (pendingWriteIndex > _interruptSourcePendingWriteIndex)
            {
                _interruptSourcePendingWriteIndex = pendingWriteIndex;
            }
        }

        private bool HasPendingWriteThrough(PaulaTimelineState timeline, long targetCycle)
        {
            return timeline.PendingWriteIndex < _pendingWrites.Count &&
                _pendingWrites[timeline.PendingWriteIndex].Cycle <= targetCycle;
        }

        private void AdvanceChannels(PaulaTimelineState timeline, PaulaTimelineKind kind, long targetCycle)
        {
            if (targetCycle <= timeline.LastCycle)
            {
                return;
            }

            foreach (var channel in timeline.Channels)
            {
                channel.AdvanceTo(targetCycle, _bus, this, timeline, kind);
            }

            timeline.LastCycle = targetCycle;
        }

        private void ApplyWrite(PaulaTimelineState timeline, PaulaTimelineKind kind, ushort offset, ushort value)
        {
            if (kind == PaulaTimelineKind.Register && offset + 1 < _registerBytes.Length)
            {
                _registerBytes[offset] = (byte)(value >> 8);
                _registerBytes[offset + 1] = (byte)value;
            }

            if (offset == 0x096)
            {
                var old = timeline.Dmacon;
                var mask = (ushort)(value & WritableDmaconMask);
                if ((value & 0x8000) != 0)
                {
                    timeline.Dmacon |= mask;
                }
                else
                {
                    timeline.Dmacon &= (ushort)~mask;
                }

                if (kind == PaulaTimelineKind.Register)
                {
                    WriteRegisterWord(0x002, timeline.Dmacon, timeline.LastCycle);
                }

                for (var i = 0; i < timeline.Channels.Length; i++)
                {
                    var bit = (ushort)(1 << i);
                    var wasEnabled = IsAudioDmaEnabled(old, bit);
                    var enabled = IsAudioDmaEnabled(timeline.Dmacon, bit);
                    if (enabled && !wasEnabled)
                    {
                        timeline.Channels[i].SetDmaEnabled(true, timeline.LastCycle, _bus, this, timeline, kind);
                    }
                    else if (!enabled && wasEnabled)
                    {
                        timeline.Channels[i].SetDmaEnabled(false, timeline.LastCycle, _bus, this, timeline, kind);
                    }
                }

                return;
            }

            if (offset == 0x09A)
            {
                ApplySetClear(ref timeline.Intena, value);
                if (kind == PaulaTimelineKind.Register)
                {
                    WriteRegisterWord(0x01C, timeline.Intena, timeline.LastCycle);
                    QueuePendingEnabledAudioInterrupts(timeline.LastCycle);
                    RefreshCpuInterruptVisibility(
                        timeline.LastCycle,
                        AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles);
                }

                return;
            }

            if (offset == 0x09C)
            {
                var mask = (ushort)(value & WritableIntreqMask);
                if ((value & 0x8000) != 0)
                {
                    timeline.Intreq |= mask;
                    if (kind == PaulaTimelineKind.Register)
                    {
                        QueuePendingEnabledAudioInterrupts(timeline.LastCycle, mask);
                    }
                }
                else
                {
                    timeline.Intreq &= (ushort)~mask;
                }

                if (kind == PaulaTimelineKind.Register)
                {
                    WriteRegisterWord(0x01E, timeline.Intreq, timeline.LastCycle);
                    RefreshCpuInterruptVisibility(
                        timeline.LastCycle,
                        AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles);
                }

                return;
            }

            if (offset == 0x09E)
            {
                ApplySetClear(ref timeline.Adkcon, value);

                if (kind == PaulaTimelineKind.Register)
                {
                    WriteRegisterWord(0x010, timeline.Adkcon, timeline.LastCycle);
                }

                return;
            }

            var channelIndex = GetAudioChannelIndex(offset);
            if (channelIndex < 0)
            {
                return;
            }

            var channel = timeline.Channels[channelIndex];
            var register = offset - (0x0A0 + (channelIndex * 0x10));
            switch (register)
            {
                case 0x00:
                    channel.Location = _bus.WriteChipDmaPointerHigh(channel.Location, value);
                    break;
                case 0x02:
                    channel.Location = _bus.WriteChipDmaPointerLow(channel.Location, value);
                    break;
                case 0x04:
                    channel.LengthWords = value == 0 ? 65_536 : value;
                    break;
                case 0x06:
                    channel.Period = value;
                    break;
                case 0x08:
                    var volume = value & 0x7F;
                    channel.Volume = Math.Min(64, volume);
                    break;
                case 0x0A:
                    channel.WriteData(value, timeline.LastCycle, this, timeline, kind);
                    break;
            }
        }

        private static int GetAudioChannelIndex(ushort offset)
        {
            if (offset < 0x0A0 || offset > 0x0DA)
            {
                return -1;
            }

            var channel = (offset - 0x0A0) / 0x10;
            var register = (offset - 0x0A0) % 0x10;
            return channel < AmigaConstants.PaulaChannelCount && register <= 0x0A ? channel : -1;
        }

        private static bool IsAudioDmaEnabled(ushort dmacon, ushort channelBit)
        {
            return (dmacon & 0x0200) != 0 && (dmacon & channelBit) != 0;
        }

        private void RequestAudioInterrupt(PaulaTimelineKind kind, int channel, long cycle)
        {
            if ((uint)channel < AmigaConstants.PaulaChannelCount)
            {
                var bit = GetAudioInterruptBit(channel);
                if (kind == PaulaTimelineKind.Audio)
                {
                    _audioTimeline.Intreq |= bit;
                    return;
                }

                RequestInterrupt(bit, cycle);
            }
        }

        internal void RequestInterrupt(ushort bit, long cycle)
        {
            bit = (ushort)(bit & 0x3FFF);
            if (bit == 0)
            {
                return;
            }

            if ((bit & AudioInterruptMask) != 0 &&
                (_registerTimeline.Intreq & AudioInterruptMask) == AudioInterruptMask)
            {
                return;
            }

            _registerTimeline.Intreq |= bit;
            WriteRegisterWord(0x01E, _registerTimeline.Intreq, cycle);
            QueuePendingEnabledAudioInterrupts(cycle, bit);
            RefreshCpuInterruptVisibility(cycle);
        }

        internal void DelayCopperInterruptRecognition(long cycle)
            => _copperInterruptRecognitionCycle = Math.Max(
                _copperInterruptRecognitionCycle,
                Math.Max(0, cycle) + AmigaConstants.A500CopperIntreqDelayCpuCycles);

        private ushort GetCpuVisibleInterruptBits(long cycle)
        {
            cycle = Math.Max(0, cycle);
            RefreshCpuInterruptVisibility(cycle);
            var active = ActiveInterruptBits;
            if (active == 0)
            {
                return 0;
            }

            ushort visible = 0;
            for (var bitIndex = 0; bitIndex < _cpuInterruptReleaseCycles.Length; bitIndex++)
            {
                var bit = (ushort)(1 << bitIndex);
                if ((active & bit) != 0 && cycle >= _cpuInterruptReleaseCycles[bitIndex])
                {
                    visible |= bit;
                }
            }

            return visible;
        }

        private void RefreshCpuInterruptVisibility(long cycle, int? releaseDelayCycles = null)
        {
            cycle = Math.Max(0, cycle);
            var active = ActiveInterruptBits;
            if (active == _lastCpuActiveInterruptBits)
            {
                return;
            }

            var newlyActive = (ushort)(active & ~_lastCpuActiveInterruptBits);
            for (var bitIndex = 0; bitIndex < _cpuInterruptReleaseCycles.Length; bitIndex++)
            {
                var bit = (ushort)(1 << bitIndex);
                if ((active & bit) == 0)
                {
                    _cpuInterruptReleaseCycles[bitIndex] = 0;
                    continue;
                }

                if ((newlyActive & bit) != 0 || _cpuInterruptReleaseCycles[bitIndex] == 0)
                {
                    _cpuInterruptReleaseCycles[bitIndex] = GetCpuInterruptReleaseCycle(
                        bit,
                        cycle,
                        releaseDelayCycles);
                }
            }

            _lastCpuActiveInterruptBits = active;
        }

        private static int GetHighestInterruptLevel(ushort active)
        {
            if ((active & 0x2000) != 0)
            {
                return 6;
            }

            if ((active & 0x1800) != 0)
            {
                return 5;
            }

            if ((active & 0x0780) != 0)
            {
                return 4;
            }

            if ((active & 0x0070) != 0)
            {
                return 3;
            }

            if ((active & 0x0008) != 0)
            {
                return 2;
            }

            return (active & 0x0007) != 0 ? 1 : 0;
        }

        private static int GetInterruptLevelForBit(ushort bit)
            => GetHighestInterruptLevel(bit);

        private long GetCpuInterruptReleaseCycle(ushort bit, long cycle, int? releaseDelayCycles = null)
        {
            var recognitionCycle = cycle;
            if ((bit & AmigaConstants.IntreqCopper) != 0 && _copperInterruptRecognitionCycle > recognitionCycle)
            {
                recognitionCycle = _copperInterruptRecognitionCycle;
            }

            return recognitionCycle +
                (releaseDelayCycles ?? AmigaConstants.A500IntreqToIplDelayCpuCycles);
        }

        private void ApplyVolumeModulationFrom(PaulaTimelineState timeline, int sourceChannel, ushort value)
        {
            var targetChannel = sourceChannel + 1;
            if (sourceChannel < 0 ||
                targetChannel >= timeline.Channels.Length ||
                !IsVolumeAttached(timeline, sourceChannel))
            {
                return;
            }

            timeline.Channels[targetChannel].Volume = Math.Min(64, value & 0x7F);
        }

        private void ApplyPeriodModulationFrom(PaulaTimelineState timeline, int sourceChannel, ushort value)
        {
            var targetChannel = sourceChannel + 1;
            if (sourceChannel < 0 ||
                targetChannel >= timeline.Channels.Length ||
                !IsPeriodAttached(timeline, sourceChannel))
            {
                return;
            }

            timeline.Channels[targetChannel].Period = value;
        }

        private bool IsAttachedSource(PaulaTimelineState timeline, int channel)
        {
            return IsVolumeAttached(timeline, channel) || IsPeriodAttached(timeline, channel);
        }

        private bool UsesNormalOrVolumeDmaTransition(PaulaTimelineState timeline, int channel)
        {
            var volumeAttached = IsVolumeAttached(timeline, channel);
            var periodAttached = IsPeriodAttached(timeline, channel);
            return (!volumeAttached && !periodAttached) || volumeAttached;
        }

        private bool IsVolumeAttached(PaulaTimelineState timeline, int sourceChannel)
        {
            return sourceChannel is >= 0 and < AmigaConstants.PaulaChannelCount &&
                (timeline.Adkcon & (1 << sourceChannel)) != 0;
        }

        private bool IsPeriodAttached(PaulaTimelineState timeline, int sourceChannel)
        {
            return sourceChannel is >= 0 and < AmigaConstants.PaulaChannelCount &&
                (timeline.Adkcon & (1 << (sourceChannel + 4))) != 0;
        }

        public bool IsInterruptEnabled(ushort bit)
        {
            return (_registerTimeline.Intena & IntenaMasterEnable) != 0 && (_registerTimeline.Intena & bit) != 0;
        }

        private void QueuePendingEnabledAudioInterrupts(long cycle, ushort mask = AudioInterruptMask)
        {
            var active = (ushort)(_registerTimeline.Intreq & _registerTimeline.Intena & mask & AudioInterruptMask);
            if ((_registerTimeline.Intena & IntenaMasterEnable) == 0 || active == 0)
            {
                return;
            }

            for (var channel = 0; channel < _registerTimeline.Channels.Length; channel++)
            {
                var bit = GetAudioInterruptBit(channel);
                if ((active & bit) != 0)
                {
                    _pendingInterrupts.Add(new PaulaInterruptEvent(channel, bit, cycle));
                }
            }
        }

        private void WriteRegisterWord(ushort offset, ushort value, long cycle)
        {
            if (offset + 1 >= _registerBytes.Length)
            {
                return;
            }

            _registerBytes[offset] = (byte)(value >> 8);
            _registerBytes[offset + 1] = (byte)value;
            if (offset == 0x002)
            {
                _bus.PublishDmaconrState(cycle);
            }
            else
            {
                _bus.PublishCustomRegisterState(offset, value, cycle);
            }
        }

        private static void ApplySetClear(ref ushort register, ushort value)
        {
            var mask = (ushort)(value & 0x7FFF);
            if ((value & 0x8000) != 0)
            {
                register |= mask;
            }
            else
            {
                register &= (ushort)~mask;
            }
        }

        private static ushort GetAudioInterruptBit(int channel)
        {
            return (ushort)(0x0080 << channel);
        }

        private static long GetEffectivePeriod(int period)
        {
            if (period == 0)
            {
                return 65_536L;
            }

            Debug.Assert(period > 0, "Paula audio period must be a raw non-negative register value.");
            return period;
        }

        private static bool UsesLiveAgnusAudioDma(AmigaBus bus)
            => bus.LiveAgnusDmaEnabled;

        private void InvalidateRegisterWakeCandidateCache()
        {
            unchecked
            {
                _registerWakeVersion++;
            }

            _registerWakeCandidateVersion = ulong.MaxValue;
            _dmaWakeCandidateVersion = ulong.MaxValue;
            _interruptSourceWakeCandidateVersion = ulong.MaxValue;
            _pendingInterruptSourceWriteCandidateVersion = ulong.MaxValue;
        }

        private long GetRegisterWakeCandidateCycle()
        {
            if (_registerWakeCandidateVersion == _registerWakeVersion)
            {
                return _registerWakeCandidateCycle;
            }

            var candidate = GetPendingRegisterWriteCycle();

            for (var i = 0; i < _registerTimeline.Channels.Length; i++)
            {
                var channelCandidate = _registerTimeline.Channels[i].GetNextRegisterWakeCandidateCycle(
                    _registerTimeline,
                    this);
                if (channelCandidate.HasValue)
                {
                    candidate = Math.Min(candidate, channelCandidate.Value);
                }
            }

            _registerWakeCandidateCycle = candidate;
            _registerWakeCandidateVersion = _registerWakeVersion;
            return candidate;
        }

        private long GetPendingRegisterWriteCycle()
            => _registerTimeline.PendingWriteIndex < _pendingWrites.Count
                ? _pendingWrites[_registerTimeline.PendingWriteIndex].Cycle
                : long.MaxValue;

        private long GetDmaWakeCandidateCycle()
        {
            if (_dmaWakeCandidateVersion == _registerWakeVersion)
            {
                return _dmaWakeCandidateCycle;
            }

            var candidate = GetPendingRegisterWriteCycle();

            for (var i = 0; i < _registerTimeline.Channels.Length; i++)
            {
                var channelCandidate = _registerTimeline.Channels[i].GetNextDmaWakeCandidateCycle(
                    _registerTimeline,
                    this);
                if (channelCandidate.HasValue)
                {
                    candidate = Math.Min(candidate, channelCandidate.Value);
                }
            }

            _dmaWakeCandidateCycle = candidate;
            _dmaWakeCandidateVersion = _registerWakeVersion;
            return candidate;
        }

        private long GetInterruptSourceWakeCandidateCycle()
        {
            if (_interruptSourceWakeCandidateVersion == _registerWakeVersion)
            {
                return _interruptSourceWakeCandidateCycle;
            }

            var candidate = GetPendingInterruptSourceWriteCycle();
            if (!AreAllAudioInterruptsPending)
            {
                candidate = Math.Min(candidate, GetDmaWakeCandidateCycle());
            }

            _interruptSourceWakeCandidateCycle = candidate;
            _interruptSourceWakeCandidateVersion = _registerWakeVersion;
            return candidate;
        }

        private long GetPendingInterruptSourceWriteCycle()
        {
            if (_pendingInterruptSourceWriteCandidateVersion == _registerWakeVersion)
            {
                return _pendingInterruptSourceWriteCandidateCycle;
            }

            _pendingInterruptSourceWriteCandidateIndex = -1;
            _pendingInterruptSourceWriteCandidateCycle = long.MaxValue;
            for (var i = _interruptSourcePendingWriteIndex; i < _pendingWrites.Count; i++)
            {
                var write = _pendingWrites[i];
                if (IsInterruptSourceWriteOffset(write.Offset))
                {
                    _pendingInterruptSourceWriteCandidateIndex = i;
                    _pendingInterruptSourceWriteCandidateCycle = write.Cycle;
                    break;
                }
            }

            _pendingInterruptSourceWriteCandidateVersion = _registerWakeVersion;
            return _pendingInterruptSourceWriteCandidateCycle;
        }

        private static bool IsInterruptSourceWriteOffset(ushort offset)
            => offset is 0x096 or 0x09A or 0x09C or 0x09E;

        private long GetCpuWakeCandidateCycle(int cpuInterruptMask)
        {
            var candidate = GetPendingRegisterWriteCycle();
            if (!CanAudioInterruptReachCpu(cpuInterruptMask))
            {
                return candidate;
            }

            for (var i = 0; i < _registerTimeline.Channels.Length; i++)
            {
                var channelCandidate = _registerTimeline.Channels[i].GetNextRegisterWakeCandidateCycle(
                    _registerTimeline,
                    this);
                if (channelCandidate.HasValue)
                {
                    candidate = Math.Min(candidate, channelCandidate.Value);
                }
            }

            return candidate;
        }

        private bool CanAudioInterruptReachCpu(int cpuInterruptMask)
        {
            if ((_registerTimeline.Intena & IntenaMasterEnable) == 0 ||
                (_registerTimeline.Intena & AudioInterruptMask) == 0)
            {
                return false;
            }

            return cpuInterruptMask < 0 || GetHighestInterruptLevel(AudioInterruptMask) > (cpuInterruptMask & 0x07);
        }

        private static long? MinWakeCandidate(long? candidate, long? eventCycle)
        {
            if (!eventCycle.HasValue)
            {
                return candidate;
            }

            if (!candidate.HasValue)
            {
                return eventCycle;
            }

            return Math.Min(candidate.Value, eventCycle.Value);
        }

        private static long? ClampWakeCandidate(long? candidate, long currentCycle, long targetCycle)
        {
            if (!candidate.HasValue || candidate.Value > targetCycle)
            {
                return null;
            }

            return candidate.Value <= currentCycle ? currentCycle + 1 : candidate.Value;
        }

        private void CompactPendingWrites()
        {
            var consumed = Math.Min(
                Math.Min(_audioTimeline.PendingWriteIndex, _registerTimeline.PendingWriteIndex),
                _interruptSourcePendingWriteIndex);
            if (consumed < 64 || consumed * 2 < _pendingWrites.Count)
            {
                return;
            }

            _pendingWrites.RemoveRange(0, consumed);
            _audioTimeline.PendingWriteIndex -= consumed;
            _registerTimeline.PendingWriteIndex -= consumed;
            _interruptSourcePendingWriteIndex -= consumed;
        }

        private PaulaDmaReadLatch GetOrCreateDmaReadLatch(int channel, uint address, long requestedCycle, PaulaTimelineKind kind)
        {
            address = _bus.MaskChipDmaAddress(address);
            var queue = _dmaReadLatchQueues[channel];
            if (queue.TryConsume(address, requestedCycle, kind, out var cachedLatch))
            {
                return cachedLatch;
            }

            var reservation = _bus.ReservePaulaDmaWord(channel, address, requestedCycle);
            _paulaDmaWordReservationCount++;
            var latch = new PaulaDmaReadLatch(channel, address, requestedCycle, reservation);
            queue.AddConsumed(latch, kind);
            return latch;
        }

        private bool TryGetOrCreateDmaReadLatchAtSlot(
            int channel,
            uint address,
            long requestCycle,
            long slotCycle,
            PaulaTimelineKind kind,
            out PaulaDmaReadLatch latch)
        {
            address = _bus.MaskChipDmaAddress(address);
            var queue = _dmaReadLatchQueues[channel];
            if (queue.TryConsume(address, requestCycle, kind, out latch))
            {
                return true;
            }

            if (!_bus.TryReservePaulaDmaWordExactSlot(channel, address, slotCycle, out var reservation))
            {
                latch = default;
                return false;
            }

            _paulaDmaWordReservationCount++;
            latch = new PaulaDmaReadLatch(channel, address, requestCycle, reservation);
            queue.AddConsumed(latch, kind);
            return true;
        }

        private long GetNextAudioDmaSlotCycle(int channel, long cycle)
        {
            cycle = Math.Max(0, cycle);
            return _bus.FindNextFixedDmaSlot(
                cycle,
                AgnusChipSlotOwner.Paula,
                channel);
        }

        private void CompactDmaReadLatches()
        {
            for (var i = 0; i < _dmaReadLatchQueues.Length; i++)
            {
                _dmaReadLatchQueues[i].CompactConsumedPrefix();
            }
        }

        private void RecordStartDmaWordOutput()
        {
            if (!_hotCounterDiagnosticsEnabled)
            {
                return;
            }

            _startDmaWordOutputCount++;
        }

        private void RecordRegisterDmaFastForwardIterations(long count)
        {
            if (count <= 0)
            {
                return;
            }

            _registerDmaFastForwardIterationCount += count;
        }

        private void ResetDmaReadLatchQueues()
        {
            for (var i = 0; i < _dmaReadLatchQueues.Length; i++)
            {
                _dmaReadLatchQueues[i].Reset();
            }
        }

        private static bool IsActive(ReadOnlySpan<float> samples)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                if (Math.Abs(samples[i]) > 0.001f)
                {
                    return true;
                }
            }

            return false;
        }

        private enum PaulaTimelineKind
        {
            Audio,
            Register
        }

        private sealed class PaulaTimelineState
        {
            public PaulaTimelineState(int cpuCyclesPerColorClock)
            {
                Channels = new PaulaChannel[AmigaConstants.PaulaChannelCount];
                for (var i = 0; i < Channels.Length; i++)
                {
                    Channels[i] = new PaulaChannel(i, cpuCyclesPerColorClock);
                }
            }

            public PaulaChannel[] Channels { get; }

            public int PendingWriteIndex { get; set; }

            public ushort Adkcon;

            public ushort Dmacon;

            public ushort Intena;

            public ushort Intreq;

            public long LastCycle;

            public void Reset()
            {
                PendingWriteIndex = 0;
                Adkcon = 0;
                Dmacon = 0;
                Intena = 0;
                Intreq = 0;
                LastCycle = 0;
                foreach (var channel in Channels)
                {
                    channel.Reset();
                }
            }
        }

        private static PaulaDmaReadLatchQueue[] CreateDmaReadLatchQueues()
        {
            var queues = new PaulaDmaReadLatchQueue[AmigaConstants.PaulaChannelCount];
            for (var i = 0; i < queues.Length; i++)
            {
                queues[i] = new PaulaDmaReadLatchQueue();
            }

            return queues;
        }

        private sealed class PaulaDmaReadLatchQueue
        {
            private const int InitialCapacity = 16;
            private PaulaDmaReadLatchRecord[] _records = new PaulaDmaReadLatchRecord[InitialCapacity];
            private int _start;
            private int _count;

            public void Reset()
            {
                _start = 0;
                _count = 0;
            }

            public bool TryConsume(uint address, long requestedCycle, PaulaTimelineKind kind, out PaulaDmaReadLatch latch)
            {
                CompactConsumedPrefix();
                for (var i = 0; i < _count; i++)
                {
                    ref var record = ref _records[IndexOf(i)];
                    if (record.IsConsumed(kind))
                    {
                        continue;
                    }

                    if (record.RequestedCycle < requestedCycle)
                    {
                        record.MarkConsumed(kind);
                        continue;
                    }

                    if (!record.Matches(address, requestedCycle))
                    {
                        latch = default;
                        CompactConsumedPrefix();
                        return false;
                    }

                    record.MarkConsumed(kind);
                    latch = record.Latch;
                    CompactConsumedPrefix();
                    return true;
                }

                latch = default;
                return false;
            }

            public void AddConsumed(PaulaDmaReadLatch latch, PaulaTimelineKind kind)
            {
                CompactConsumedPrefix();
                EnsureCapacity(_count + 1);
                ref var record = ref _records[IndexOf(_count)];
                record = new PaulaDmaReadLatchRecord(latch);
                record.MarkConsumed(kind);
                _count++;
            }

            public void CompactConsumedPrefix()
            {
                while (_count != 0 && _records[_start].ConsumedByBoth)
                {
                    _records[_start] = default;
                    _start++;
                    if (_start == _records.Length)
                    {
                        _start = 0;
                    }

                    _count--;
                }

                if (_count == 0)
                {
                    _start = 0;
                }
            }

            private void EnsureCapacity(int required)
            {
                if (required <= _records.Length)
                {
                    return;
                }

                var next = new PaulaDmaReadLatchRecord[_records.Length * 2];
                for (var i = 0; i < _count; i++)
                {
                    next[i] = _records[IndexOf(i)];
                }

                _records = next;
                _start = 0;
            }

            private int IndexOf(int logicalIndex)
            {
                var index = _start + logicalIndex;
                return index < _records.Length ? index : index - _records.Length;
            }
        }

        private struct PaulaDmaReadLatchRecord
        {
            public PaulaDmaReadLatchRecord(PaulaDmaReadLatch latch)
            {
                Latch = latch;
                AudioConsumed = false;
                RegisterConsumed = false;
            }

            public PaulaDmaReadLatch Latch { get; }

            public bool AudioConsumed { get; private set; }

            public bool RegisterConsumed { get; private set; }

            public bool ConsumedByBoth => AudioConsumed && RegisterConsumed;

            public long RequestedCycle => Latch.RequestedCycle;

            public bool Matches(uint address, long requestedCycle)
                => Latch.Address == address && Latch.RequestedCycle == requestedCycle;

            public bool IsConsumed(PaulaTimelineKind kind)
                => kind switch
                {
                    PaulaTimelineKind.Audio => AudioConsumed,
                    _ => RegisterConsumed
                };

            public void MarkConsumed(PaulaTimelineKind kind)
            {
                switch (kind)
                {
                    case PaulaTimelineKind.Audio:
                        AudioConsumed = true;
                        break;
                    default:
                        RegisterConsumed = true;
                        break;
                }
            }
        }

        private readonly struct PaulaDmaReadLatch
        {
            public PaulaDmaReadLatch(
                int channel,
                uint address,
                long requestedCycle,
                AmigaDmaWordReservation reservation)
            {
                Channel = channel;
                Address = address;
                RequestedCycle = requestedCycle;
                Reservation = reservation;
                LoadCycle = reservation.CompletedCycle;
            }

            public int Channel { get; }

            public uint Address { get; }

            public long RequestedCycle { get; }

            public AmigaDmaWordReservation Reservation { get; }

            public long LoadCycle { get; }
        }

        private readonly struct PendingWrite
        {
            public PendingWrite(long cycle, ushort offset, ushort value)
            {
                Cycle = cycle;
                Offset = offset;
                Value = value;
            }

            public long Cycle { get; }

            public ushort Offset { get; }

            public ushort Value { get; }
        }

        private sealed class PaulaChannel
        {
            private readonly int _cpuCyclesPerColorClock;
            private ushort _dataLatch;
            private bool _dataLatchWritten;
            private ushort _dataWord;
            private bool _hasDataWord;
            private bool _nextByteIsLow;
            private bool _manualPlayback;
            private PaulaAudioState _state;
            private bool _intreq2;
            private long _intreq2ArmCycle;
            private bool _dataRequest;
            private bool _dmaSpecialRequest;
            private bool _periodBufferLoad;
            private int _irqCheck;
            private long _manualIrqCheckCycle;
            private bool _dmaStreamInitialized;
            private long _nextSampleCycle;
            private PaulaDmaReadLatch _prefetchedDmaLatch;
            private bool _hasPrefetchedDmaWord;
            private PaulaDmaReadLatch _pendingDmaLatch;
            private bool _hasPendingDmaWord;
            private bool _pendingDmaServed;
            private uint _pendingDmaRequestAddress;
            private long _pendingDmaRequestCycle;
            private long _pendingDmaServiceCycle;
            private long _pendingDmaLoadCycle;
            private int _pendingDmaInterruptCount;
            private bool _pendingDmaArmsDelayedInterrupt;
            private DmaLoadTarget _pendingDmaLoadTarget;
            private uint _currentAddress;
            private int _remainingWords;

            public PaulaChannel(int index, int cpuCyclesPerColorClock)
            {
                Index = index;
                _cpuCyclesPerColorClock = cpuCyclesPerColorClock;
                Reset();
            }

            public int Index { get; }

            private long GetPeriodCycles(int period)
                => GetEffectivePeriod(period) * _cpuCyclesPerColorClock;

            public uint Location { get; set; }

            public int LengthWords { get; set; }

            public int Period { get; set; }

            public int Volume { get; set; }

            public sbyte CurrentSample { get; set; }

            public bool DmaEnabled { get; set; }

            public long LastDmaEnableCycle { get; private set; }

            public void Reset()
            {
                Location = 0;
                LengthWords = 1;
                Period = 428;
                Volume = 64;
                CurrentSample = 0;
                DmaEnabled = false;
                LastDmaEnableCycle = -1;
                _dataLatch = 0;
                _dataLatchWritten = false;
                _dataWord = 0;
                _hasDataWord = false;
                _nextByteIsLow = false;
                _manualPlayback = false;
                _state = PaulaAudioState.Idle;
                _intreq2 = false;
                _intreq2ArmCycle = long.MaxValue;
                _dataRequest = false;
                _dmaSpecialRequest = false;
                _periodBufferLoad = false;
                _irqCheck = 0;
                _manualIrqCheckCycle = long.MaxValue;
                _dmaStreamInitialized = false;
                _nextSampleCycle = 0;
                _prefetchedDmaLatch = default;
                _hasPrefetchedDmaWord = false;
                _pendingDmaLatch = default;
                _hasPendingDmaWord = false;
                _pendingDmaServed = false;
                _pendingDmaRequestAddress = 0;
                _pendingDmaRequestCycle = long.MaxValue;
                _pendingDmaServiceCycle = long.MaxValue;
                _pendingDmaLoadCycle = long.MaxValue;
                _pendingDmaInterruptCount = 0;
                _pendingDmaArmsDelayedInterrupt = false;
                _pendingDmaLoadTarget = DmaLoadTarget.Prefetch;
                _currentAddress = 0;
                _remainingWords = 0;
            }

            public void SetDmaEnabled(
                bool enabled,
                long cycle,
                AmigaBus bus,
                Paula paula,
                PaulaTimelineState timeline,
                PaulaTimelineKind kind)
            {
                if (!enabled)
                {
                    DmaEnabled = false;
                    _dataRequest = false;
                    _dmaSpecialRequest = false;
                    if (_state is PaulaAudioState.DmaStartup or PaulaAudioState.DmaStartupData)
                    {
                        EnterIdle(clearDmaPipeline: true);
                        return;
                    }

                    if (!_hasDataWord)
                    {
                        EnterIdle(clearDmaPipeline: true);
                    }

                    return;
                }

                DmaEnabled = true;
                LastDmaEnableCycle = cycle;
                _manualPlayback = false;
                if (_hasDataWord && _dmaStreamInitialized)
                {
                    return;
                }

                _hasDataWord = false;
                _nextByteIsLow = false;
                ClearPrefetchedDmaWord();
                ClearPendingDmaWord();
                _currentAddress = bus.MaskChipDmaAddress(Location);
                _remainingWords = Math.Max(1, LengthWords);
                _dmaStreamInitialized = true;
                _state = PaulaAudioState.DmaStartup;
                _dataRequest = true;
                _dmaSpecialRequest = true;
                _periodBufferLoad = false;
                _irqCheck = 0;
                TryConsumeDelayedInterrupt(paula, timeline, kind, cycle);

                var context = new DmaContext(bus, paula, timeline, kind);
                RequestStartupDiscardWord(cycle, in context);
            }

            public void WriteData(ushort value, long cycle, Paula paula, PaulaTimelineState timeline, PaulaTimelineKind kind)
            {
                _dataLatch = value;
                _dataLatchWritten = true;
                if (DmaEnabled || _hasDataWord || (timeline.Intreq & GetAudioInterruptBit(Index)) != 0)
                {
                    return;
                }

                StartManualWord(cycle, paula, timeline, kind);
            }

            public void AdvanceTo(long targetCycle, AmigaBus bus, Paula paula, PaulaTimelineState timeline, PaulaTimelineKind kind)
            {
                if (!_hasDataWord && !DmaEnabled && !_hasPendingDmaWord && !_hasPrefetchedDmaWord)
                {
                    return;
                }

                var context = new DmaContext(bus, paula, timeline, kind);
                while (true)
                {
                    if (TryFastForwardStableRegisterDmaTo(targetCycle, in context))
                    {
                        continue;
                    }

                    ServicePendingDmaRequestThrough(targetCycle, in context);
                    if (TryArmDelayedInterruptBeforeNextAction(targetCycle))
                    {
                        continue;
                    }

                    if (TryEnterManualFinalPeriodPhase(targetCycle, context.Timeline))
                    {
                        continue;
                    }

                    if (_hasPendingDmaWord)
                    {
                        var sampleDue = _hasDataWord && _nextSampleCycle <= targetCycle;
                        if (_pendingDmaServed &&
                            _pendingDmaLoadCycle <= targetCycle &&
                            (!sampleDue || _pendingDmaLoadCycle <= _nextSampleCycle))
                        {
                            CompletePendingDmaWord(in context, targetCycle);
                            continue;
                        }

                        if (!sampleDue)
                        {
                            return;
                        }
                    }

                    if (!_hasDataWord && !DmaEnabled)
                    {
                        return;
                    }

                    if (_nextSampleCycle > targetCycle)
                    {
                        return;
                    }

                    if (_hasDataWord && _nextByteIsLow)
                    {
                        CurrentSample = unchecked((sbyte)_dataWord);
                        _nextByteIsLow = false;
                        _state = PaulaAudioState.LowByte;
                        _periodBufferLoad = true;
                        context.Paula.ApplyPeriodModulationFrom(context.Timeline, Index, _dataWord);
                        if (DmaEnabled && context.Paula.IsPeriodAttached(context.Timeline, Index))
                        {
                            TryConsumeDelayedInterrupt(
                                context.Paula,
                                context.Timeline,
                                context.Kind,
                                _nextSampleCycle);
                            QueueDataRequest(_nextSampleCycle, in context);
                        }

                        _periodBufferLoad = false;
                        _nextSampleCycle += GetPeriodCycles(Period);
                        if (!DmaEnabled)
                        {
                            BeginManualLowPeriod(context.Timeline);
                        }

                        if (DmaEnabled &&
                            UsesLiveAgnusAudioDma(context.Bus) &&
                            !_hasPrefetchedDmaWord &&
                            _hasPendingDmaWord &&
                            _pendingDmaServed)
                        {
                            _nextSampleCycle = Math.Max(_nextSampleCycle, _pendingDmaLoadCycle);
                        }

                        continue;
                    }

                    if (DmaEnabled)
                    {
                        if (context.Paula.UsesNormalOrVolumeDmaTransition(context.Timeline, Index))
                        {
                            TryConsumeDelayedInterrupt(
                                context.Paula,
                                context.Timeline,
                                context.Kind,
                                _nextSampleCycle);
                        }

                        if (_hasPrefetchedDmaWord)
                        {
                            var latch = _prefetchedDmaLatch;
                            ClearPrefetchedDmaWord();
                            StartDmaWordOutput(
                                latch,
                                _nextSampleCycle,
                                in context,
                                targetCycle);
                            continue;
                        }

                        if (!_hasPendingDmaWord &&
                            context.Paula.UsesNormalOrVolumeDmaTransition(context.Timeline, Index))
                        {
                            QueueDataRequest(_nextSampleCycle, in context);
                        }

                        if (_hasPrefetchedDmaWord)
                        {
                            continue;
                        }

                        _nextSampleCycle += GetPeriodCycles(Period);
                        if (UsesLiveAgnusAudioDma(context.Bus) &&
                            _hasPendingDmaWord &&
                            _pendingDmaServed)
                        {
                            _nextSampleCycle = Math.Max(_nextSampleCycle, _pendingDmaLoadCycle);
                        }
                    }
                    else if (_manualPlayback)
                    {
                        if (_irqCheck == 0)
                        {
                            _irqCheck = (context.Timeline.Intreq & GetAudioInterruptBit(Index)) != 0 ? 1 : -1;
                        }

                        if (_irqCheck < 0 && _dataLatchWritten)
                        {
                            StartManualWord(
                                _nextSampleCycle,
                                context.Paula,
                                context.Timeline,
                                context.Kind);
                            continue;
                        }

                        EnterIdle(clearDmaPipeline: false);
                        break;
                    }
                    else
                    {
                        _irqCheck = (context.Timeline.Intreq & GetAudioInterruptBit(Index)) != 0 ? 1 : -1;
                        EnterIdle(clearDmaPipeline: true);
                        break;
                    }
                }
            }

            private void StartManualWord(
                long cycle,
                Paula paula,
                PaulaTimelineState timeline,
                PaulaTimelineKind kind)
            {
                _dataWord = _dataLatch;
                _dataLatchWritten = false;
                _hasDataWord = true;
                _nextByteIsLow = true;
                _manualPlayback = true;
                _state = PaulaAudioState.HighByte;
                _periodBufferLoad = false;
                _irqCheck = 0;
                _manualIrqCheckCycle = long.MaxValue;
                CurrentSample = unchecked((sbyte)(_dataWord >> 8));
                paula.ApplyVolumeModulationFrom(timeline, Index, _dataWord);
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
                paula.RequestAudioInterrupt(kind, Index, cycle);
            }

            private bool TryConsumeDelayedInterrupt(
                Paula paula,
                PaulaTimelineState timeline,
                PaulaTimelineKind kind,
                long cycle)
            {
                if (!_intreq2 ||
                    (timeline.Intreq & GetAudioInterruptBit(Index)) != 0)
                {
                    return false;
                }

                _intreq2 = false;
                paula.RequestAudioInterrupt(kind, Index, cycle);
                return true;
            }

            private bool TryArmDelayedInterruptBeforeNextAction(long targetCycle)
            {
                if (_intreq2ArmCycle > targetCycle)
                {
                    return false;
                }

                var nextActionCycle = long.MaxValue;
                if (_hasPendingDmaWord && _pendingDmaServed)
                {
                    nextActionCycle = _pendingDmaLoadCycle;
                }

                if (_hasDataWord)
                {
                    nextActionCycle = Math.Min(nextActionCycle, _nextSampleCycle);
                }

                if (_intreq2ArmCycle > nextActionCycle)
                {
                    return false;
                }

                _intreq2 = true;
                _intreq2ArmCycle = long.MaxValue;
                return true;
            }

            private void BeginManualLowPeriod(PaulaTimelineState timeline)
            {
                var periodCycles = GetPeriodCycles(Period);
                if (periodCycles <= AgnusChipSlotScheduler.SlotCycles)
                {
                    _state = PaulaAudioState.LowByte;
                    _irqCheck = (timeline.Intreq & GetAudioInterruptBit(Index)) != 0 ? 1 : -1;
                    _manualIrqCheckCycle = long.MaxValue;
                    return;
                }

                _state = PaulaAudioState.ManualPeriodOne;
                _irqCheck = 0;
                _manualIrqCheckCycle = _nextSampleCycle - AgnusChipSlotScheduler.SlotCycles;
            }

            private bool TryEnterManualFinalPeriodPhase(long targetCycle, PaulaTimelineState timeline)
            {
                if (_manualIrqCheckCycle > targetCycle)
                {
                    return false;
                }

                _state = PaulaAudioState.LowByte;
                _irqCheck = (timeline.Intreq & GetAudioInterruptBit(Index)) != 0 ? 1 : -1;
                _manualIrqCheckCycle = long.MaxValue;
                return true;
            }

            private void EnterIdle(bool clearDmaPipeline)
            {
                _state = PaulaAudioState.Idle;
                _irqCheck = 0;
                _periodBufferLoad = false;
                _dataRequest = false;
                _dmaSpecialRequest = false;
                _manualIrqCheckCycle = long.MaxValue;
                _hasDataWord = false;
                _nextByteIsLow = false;
                _manualPlayback = false;
                _dmaStreamInitialized = false;
                if (!clearDmaPipeline)
                {
                    return;
                }

                _intreq2ArmCycle = long.MaxValue;
                ClearPrefetchedDmaWord();
                ClearPendingDmaWord();
            }

            private bool TryFastForwardStableRegisterDmaTo(long targetCycle, in DmaContext context)
            {
                if (!context.Paula.RegisterDmaFastForwardEnabled ||
                    context.Kind == PaulaTimelineKind.Audio ||
                    !DmaEnabled ||
                    !_hasDataWord ||
                    _nextByteIsLow ||
                    context.Paula.IsAttachedSource(context.Timeline, Index) ||
                    context.Paula.HasPendingWriteThrough(context.Timeline, targetCycle))
                {
                    return false;
                }

                var iterations = 0L;
                while (DmaEnabled &&
                    _hasDataWord &&
                    !_nextByteIsLow &&
                    !context.Paula.IsAttachedSource(context.Timeline, Index) &&
                    !context.Paula.HasPendingWriteThrough(context.Timeline, targetCycle))
                {
                    if (_hasPendingDmaWord)
                    {
                        ServicePendingDmaRequestThrough(targetCycle, in context);
                        if (TryArmDelayedInterruptBeforeNextAction(targetCycle))
                        {
                            iterations++;
                            continue;
                        }

                        if (_pendingDmaLoadTarget == DmaLoadTarget.StartupDiscard ||
                            !_pendingDmaServed ||
                            _pendingDmaLoadCycle > targetCycle)
                        {
                            break;
                        }

                        CompletePendingDmaWord(in context, targetCycle);
                        iterations++;
                        continue;
                    }

                    if (_nextSampleCycle > targetCycle)
                    {
                        break;
                    }

                    if (_hasPrefetchedDmaWord)
                    {
                        var latch = _prefetchedDmaLatch;
                        ClearPrefetchedDmaWord();
                        StartDmaWordOutput(
                            latch,
                            _nextSampleCycle,
                            in context,
                            targetCycle);
                        iterations++;
                        continue;
                    }

                    RequestPrefetchWord(_nextSampleCycle, in context);
                    iterations++;
                    if (!_hasPrefetchedDmaWord &&
                        (!_hasPendingDmaWord || !_pendingDmaServed || _pendingDmaLoadCycle > targetCycle))
                    {
                        break;
                    }
                }

                context.Paula.RecordRegisterDmaFastForwardIterations(iterations);
                return iterations > 0;
            }

            public long? GetNextWakeCandidateCycle()
            {
                long? candidate = null;
                if (_intreq2ArmCycle != long.MaxValue)
                {
                    candidate = _intreq2ArmCycle;
                }

                if (_manualIrqCheckCycle != long.MaxValue)
                {
                    candidate = MinWakeCandidate(candidate, _manualIrqCheckCycle);
                }

                if (_hasPendingDmaWord)
                {
                    candidate = MinWakeCandidate(
                        candidate,
                        _pendingDmaServed ? _pendingDmaLoadCycle : _pendingDmaServiceCycle);
                }

                if (DmaEnabled || _hasDataWord)
                {
                    candidate = MinWakeCandidate(candidate, _nextSampleCycle);
                }

                return candidate;
            }

            public long? GetNextRegisterWakeCandidateCycle(PaulaTimelineState timeline, Paula paula)
            {
                long? candidate = null;
                if (_intreq2ArmCycle != long.MaxValue)
                {
                    candidate = _intreq2ArmCycle;
                }

                if (_manualIrqCheckCycle != long.MaxValue)
                {
                    candidate = MinWakeCandidate(candidate, _manualIrqCheckCycle);
                }

                if (_hasPendingDmaWord)
                {
                    candidate = MinWakeCandidate(
                        candidate,
                        _pendingDmaServed ? _pendingDmaLoadCycle : _pendingDmaServiceCycle);
                }

                if (_hasDataWord && _nextByteIsLow && paula.IsAttachedSource(timeline, Index))
                {
                    candidate = MinWakeCandidate(candidate, _nextSampleCycle);
                }

                if (DmaEnabled)
                {
                    candidate = MinWakeCandidate(candidate, GetNextDmaRegisterBoundaryCycle());
                }

                return candidate;
            }

            public long? GetNextDmaWakeCandidateCycle(PaulaTimelineState timeline, Paula paula)
            {
                long? candidate = null;
                if (_intreq2ArmCycle != long.MaxValue)
                {
                    candidate = _intreq2ArmCycle;
                }

                if (_manualIrqCheckCycle != long.MaxValue)
                {
                    candidate = MinWakeCandidate(candidate, _manualIrqCheckCycle);
                }

                if (_hasPendingDmaWord)
                {
                    candidate = MinWakeCandidate(
                        candidate,
                        _pendingDmaServed ? _pendingDmaLoadCycle : _pendingDmaServiceCycle);
                }

                if (_hasDataWord && _nextByteIsLow && paula.IsAttachedSource(timeline, Index))
                {
                    candidate = MinWakeCandidate(candidate, _nextSampleCycle);
                }

                if (DmaEnabled)
                {
                    candidate = MinWakeCandidate(candidate, GetNextDmaRegisterBoundaryCycle());
                }

                return candidate;
            }

            private long? GetNextDmaRegisterBoundaryCycle()
            {
                if (!_hasDataWord)
                {
                    return _hasPrefetchedDmaWord ? _nextSampleCycle : null;
                }

                if (!_nextByteIsLow)
                {
                    return _nextSampleCycle;
                }

                var nextWordCycle = _nextSampleCycle + GetPeriodCycles(Period);
                return nextWordCycle;
            }

            public PaulaChannelSnapshot GetSnapshot()
            {
                return new PaulaChannelSnapshot(
                    Index,
                    Location,
                    _currentAddress,
                    LengthWords,
                    _remainingWords,
                    Period,
                    Volume,
                    CurrentSample,
                    DmaEnabled,
                    _dataWord,
                    _dataLatch,
                    _dataLatchWritten,
                    _hasDataWord,
                    _nextByteIsLow,
                    _manualPlayback,
                    _state,
                    _intreq2,
                    _dataRequest,
                    _dmaSpecialRequest,
                    _periodBufferLoad,
                    _irqCheck,
                    _nextSampleCycle,
                    LastDmaEnableCycle);
            }

            private void RequestStartupDiscardWord(long cycle, in DmaContext context)
                => RequestDmaWord(cycle, in context, DmaLoadTarget.StartupDiscard, forceInterrupt: true);

            private void RequestPrefetchWord(long cycle, in DmaContext context)
                => RequestDmaWord(cycle, in context, DmaLoadTarget.Prefetch, forceInterrupt: false);

            private void QueueDataRequest(long cycle, in DmaContext context)
            {
                if (_hasPendingDmaWord || _hasPrefetchedDmaWord)
                {
                    return;
                }

                _dataRequest = true;
                RequestPrefetchWord(cycle, in context);
            }

            private void RequestDmaWord(
                long cycle,
                in DmaContext context,
                DmaLoadTarget loadTarget,
                bool forceInterrupt)
            {
                if (_hasPendingDmaWord || _hasPrefetchedDmaWord)
                {
                    return;
                }

                var requestCycle = cycle;
                var interruptCount = 0;
                var armsDelayedInterrupt = false;
                if (_remainingWords <= 0)
                {
                    _currentAddress = context.Bus.MaskChipDmaAddress(Location);
                    _remainingWords = Math.Max(1, LengthWords);
                    armsDelayedInterrupt = loadTarget == DmaLoadTarget.Prefetch &&
                        _state is PaulaAudioState.HighByte or PaulaAudioState.LowByte;
                }

                var requestAddress = _currentAddress;
                _currentAddress = context.Bus.AddChipDmaPointerOffset(_currentAddress, 2);
                _remainingWords--;
                _hasPendingDmaWord = true;
                _pendingDmaServed = false;
                _pendingDmaRequestAddress = requestAddress;
                _pendingDmaRequestCycle = requestCycle;
                _pendingDmaServiceCycle = UsesLiveAgnusAudioDma(context.Bus)
                    ? context.Paula.GetNextAudioDmaSlotCycle(Index, requestCycle)
                    : requestCycle;
                _pendingDmaLoadCycle = long.MaxValue;
                _pendingDmaLoadTarget = loadTarget;
                _dataRequest = false;
                _dmaSpecialRequest = false;

                if (forceInterrupt)
                {
                    interruptCount++;
                }

                _pendingDmaInterruptCount = interruptCount;
                _pendingDmaArmsDelayedInterrupt = armsDelayedInterrupt;
                if (UsesLiveAgnusAudioDma(context.Bus))
                {
                    ServicePendingDmaRequestThrough(cycle, in context);
                }
                else
                {
                    var dmaLatch = context.Paula.GetOrCreateDmaReadLatch(Index, requestAddress, requestCycle, context.Kind);
                    MarkPendingDmaServed(dmaLatch);
                }

                if (_pendingDmaServed && _pendingDmaLoadCycle <= cycle)
                {
                    CompletePendingDmaWord(in context, cycle);
                }
            }

            private void ServicePendingDmaRequestThrough(long targetCycle, in DmaContext context)
            {
                if (!_hasPendingDmaWord || _pendingDmaServed)
                {
                    return;
                }

                if (!UsesLiveAgnusAudioDma(context.Bus))
                {
                    var dmaLatch = context.Paula.GetOrCreateDmaReadLatch(
                        Index,
                        _pendingDmaRequestAddress,
                        _pendingDmaRequestCycle,
                        context.Kind);
                    MarkPendingDmaServed(dmaLatch);
                    return;
                }

                while (_pendingDmaServiceCycle <= targetCycle)
                {
                    if (context.Paula.TryGetOrCreateDmaReadLatchAtSlot(
                        Index,
                        _pendingDmaRequestAddress,
                        _pendingDmaRequestCycle,
                        _pendingDmaServiceCycle,
                        context.Kind,
                        out var dmaLatch))
                    {
                        MarkPendingDmaServed(dmaLatch);
                        return;
                    }

                    _pendingDmaServiceCycle = context.Paula.GetNextAudioDmaSlotCycle(
                        Index,
                        _pendingDmaServiceCycle + AgnusChipSlotScheduler.SlotCycles);
                }
            }

            private void MarkPendingDmaServed(PaulaDmaReadLatch dmaLatch)
            {
                _pendingDmaLatch = dmaLatch;
                _pendingDmaServed = true;
                _pendingDmaLoadCycle = dmaLatch.LoadCycle;
                _pendingDmaServiceCycle = long.MaxValue;
                if (_pendingDmaArmsDelayedInterrupt)
                {
                    _intreq2ArmCycle = Math.Min(
                        _intreq2ArmCycle,
                        dmaLatch.Reservation.GrantedCycle);
                }

                if (!_hasDataWord)
                {
                    _nextSampleCycle = _pendingDmaLoadCycle;
                }
            }

            private void CompletePendingDmaWord(
                in DmaContext context,
                long targetCycle)
            {
                var loadCycle = _pendingDmaLoadCycle;
                var interruptCount = _pendingDmaInterruptCount;
                var latch = _pendingDmaLatch;
                var loadTarget = _pendingDmaLoadTarget;
                ClearPendingDmaWord();
                for (var i = 0; i < interruptCount; i++)
                {
                    context.Paula.RequestAudioInterrupt(context.Kind, Index, loadCycle);
                }

                if (loadTarget == DmaLoadTarget.StartupDiscard)
                {
                    if (DmaEnabled)
                    {
                        _state = PaulaAudioState.DmaStartupData;
                        _dataRequest = true;
                        RequestPrefetchWord(loadCycle, in context);
                    }

                    return;
                }

                if (!_hasDataWord)
                {
                    StartDmaWordOutput(latch, loadCycle, in context, targetCycle);
                    return;
                }

                _prefetchedDmaLatch = latch;
                _hasPrefetchedDmaWord = true;
            }

            private void StartDmaWordOutput(
                PaulaDmaReadLatch latch,
                long cycle,
                in DmaContext context,
                long targetCycle)
            {
                var reservation = latch.Reservation;
                StartDmaWordOutput(
                    context.Bus.CommitPaulaDmaWord(in reservation).Value,
                    cycle,
                    in context,
                    targetCycle);
            }

            private void StartDmaWordOutput(
                ushort word,
                long cycle,
                in DmaContext context,
                long targetCycle)
            {
                context.Paula.RecordStartDmaWordOutput();
                _manualPlayback = false;
                _dataWord = word;
                _hasDataWord = true;
                _state = PaulaAudioState.HighByte;
                _periodBufferLoad = false;
                _irqCheck = 0;
                CurrentSample = unchecked((sbyte)(word >> 8));
                context.Paula.ApplyVolumeModulationFrom(context.Timeline, Index, word);
                var periodCycles = GetPeriodCycles(Period);
                if (context.Kind != PaulaTimelineKind.Audio &&
                    TrySkipRegisterLowByteDmaOutput(cycle, in context, periodCycles))
                {
                    return;
                }

                _nextByteIsLow = true;
                _nextSampleCycle = cycle + periodCycles;
                if (DmaEnabled &&
                    context.Paula.UsesNormalOrVolumeDmaTransition(context.Timeline, Index))
                {
                    QueueDataRequest(cycle, in context);
                }

                if (context.Kind == PaulaTimelineKind.Audio &&
                    _nextSampleCycle <= targetCycle &&
                    !context.Paula.IsAttachedSource(context.Timeline, Index))
                {
                    CurrentSample = unchecked((sbyte)word);
                    _nextByteIsLow = false;
                    _state = PaulaAudioState.LowByte;
                    _nextSampleCycle += periodCycles;
                    if (DmaEnabled &&
                        UsesLiveAgnusAudioDma(context.Bus) &&
                        !_hasPrefetchedDmaWord &&
                        _hasPendingDmaWord &&
                        _pendingDmaServed)
                    {
                        _nextSampleCycle = Math.Max(_nextSampleCycle, _pendingDmaLoadCycle);
                    }
                }
            }

            private bool TrySkipRegisterLowByteDmaOutput(
                long cycle,
                in DmaContext context,
                long periodCycles)
            {
                if (!DmaEnabled || context.Paula.IsAttachedSource(context.Timeline, Index))
                {
                    return false;
                }

                var nextWordCycle = cycle + (periodCycles * 2);
                if (context.Paula.HasPendingWriteThrough(context.Timeline, nextWordCycle))
                {
                    return false;
                }

                _nextByteIsLow = false;
                _state = PaulaAudioState.LowByte;
                _nextSampleCycle = nextWordCycle;
                QueueDataRequest(cycle, in context);
                if (UsesLiveAgnusAudioDma(context.Bus) &&
                    !_hasPrefetchedDmaWord &&
                    _hasPendingDmaWord &&
                    _pendingDmaServed)
                {
                    _nextSampleCycle = Math.Max(_nextSampleCycle, _pendingDmaLoadCycle);
                }

                return true;
            }

            private void ClearPrefetchedDmaWord()
            {
                _prefetchedDmaLatch = default;
                _hasPrefetchedDmaWord = false;
            }

            private void ClearPendingDmaWord()
            {
                _pendingDmaLatch = default;
                _hasPendingDmaWord = false;
                _pendingDmaServed = false;
                _pendingDmaRequestAddress = 0;
                _pendingDmaRequestCycle = long.MaxValue;
                _pendingDmaServiceCycle = long.MaxValue;
                _pendingDmaLoadCycle = long.MaxValue;
                _pendingDmaInterruptCount = 0;
                _pendingDmaArmsDelayedInterrupt = false;
                _pendingDmaLoadTarget = DmaLoadTarget.Prefetch;
            }

            private readonly struct DmaContext
            {
                public DmaContext(AmigaBus bus, Paula paula, PaulaTimelineState timeline, PaulaTimelineKind kind)
                {
                    Bus = bus;
                    Paula = paula;
                    Timeline = timeline;
                    Kind = kind;
                }

                public readonly AmigaBus Bus;
                public readonly Paula Paula;
                public readonly PaulaTimelineState Timeline;
                public readonly PaulaTimelineKind Kind;
            }

            private enum DmaLoadTarget
            {
                StartupDiscard,
                Prefetch
            }

        }
    }

    internal enum PaulaAudioState
    {
        Idle = 0,
        DmaStartup = 1,
        HighByte = 2,
        LowByte = 3,
        DmaStartupData = 5,
        ManualPeriodOne = 0x13
    }

    internal readonly struct PaulaInterruptEvent
    {
        public PaulaInterruptEvent(int channel, ushort intreqBit, long cycle)
        {
            Channel = channel;
            IntreqBit = intreqBit;
            Cycle = cycle;
        }

        public int Channel { get; }

        public ushort IntreqBit { get; }

        public long Cycle { get; }
    }

    internal readonly struct PaulaChannelSnapshot
    {
        public PaulaChannelSnapshot(
            int index,
            uint location,
            uint currentAddress,
            int lengthWords,
            int remainingWords,
            int period,
            int volume,
            sbyte currentSample,
            bool dmaEnabled,
            ushort dataWord,
            ushort dataLatch,
            bool dataLatchWritten,
            bool hasDataWord,
            bool nextByteIsLow,
            bool manualPlayback,
            PaulaAudioState state,
            bool delayedInterruptPending,
            bool dataRequest,
            bool dmaSpecialRequest,
            bool periodBufferLoad,
            int irqCheck,
            long nextSampleCycle,
            long lastDmaEnableCycle)
        {
            Index = index;
            Location = location;
            CurrentAddress = currentAddress;
            LengthWords = lengthWords;
            RemainingWords = remainingWords;
            Period = period;
            Volume = volume;
            CurrentSample = currentSample;
            DmaEnabled = dmaEnabled;
            DataWord = dataWord;
            DataLatch = dataLatch;
            DataLatchWritten = dataLatchWritten;
            HasDataWord = hasDataWord;
            NextByteIsLow = nextByteIsLow;
            ManualPlayback = manualPlayback;
            State = state;
            DelayedInterruptPending = delayedInterruptPending;
            DataRequest = dataRequest;
            DmaSpecialRequest = dmaSpecialRequest;
            PeriodBufferLoad = periodBufferLoad;
            IrqCheck = irqCheck;
            NextSampleCycle = nextSampleCycle;
            LastDmaEnableCycle = lastDmaEnableCycle;
        }

        public int Index { get; }

        public uint Location { get; }

        public uint CurrentAddress { get; }

        public int LengthWords { get; }

        public int RemainingWords { get; }

        public int Period { get; }

        public int Volume { get; }

        public sbyte CurrentSample { get; }

        public bool DmaEnabled { get; }

        public ushort DataWord { get; }

        public ushort DataLatch { get; }

        public bool DataLatchWritten { get; }

        public bool HasDataWord { get; }

        public bool NextByteIsLow { get; }

        public bool ManualPlayback { get; }

        public PaulaAudioState State { get; }

        public bool DelayedInterruptPending { get; }

        public bool SecondaryInterruptPending => DelayedInterruptPending;

        public bool DataRequest { get; }

        public bool DmaSpecialRequest { get; }

        public bool PeriodBufferLoad { get; }

        public int IrqCheck { get; }

        public long NextSampleCycle { get; }

        public long LastDmaEnableCycle { get; }
    }
}
