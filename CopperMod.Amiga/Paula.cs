using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CopperMod.Amiga
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
        private readonly PaulaTimelineState _audioTimeline = new PaulaTimelineState();
        private readonly PaulaTimelineState _registerTimeline = new PaulaTimelineState();
        private readonly List<PendingWrite> _pendingWrites = new List<PendingWrite>();
        private readonly List<PaulaDmaFetchRecord> _dmaFetches = new List<PaulaDmaFetchRecord>();
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
        private float[][]? _captureSamples;
        private int _captureFrameIndex;
        private int _captureSampleRate;

        public Paula(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public IReadOnlyList<CustomRegisterWrite> Writes => _writes;

        public ushort Adkcon => _registerTimeline.Adkcon;

        public ushort Dmacon => _registerTimeline.Dmacon;

        public ushort Intena => _registerTimeline.Intena;

        public ushort Intreq => _registerTimeline.Intreq;

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
            _dmaFetches.Clear();
            _pendingInterrupts.Clear();
            _writes.Clear();
            Array.Clear(_cpuInterruptReleaseCycles);
            _audioTimeline.Reset();
            _registerTimeline.Reset();
            _vposr = 0;
            _vhposr = 0;
            _lastCpuActiveInterruptBits = 0;
            _copperInterruptRecognitionCycle = long.MinValue;
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
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

            long? candidate = null;
            if (_registerTimeline.PendingWriteIndex < _pendingWrites.Count)
            {
                candidate = MinWakeCandidate(candidate, _pendingWrites[_registerTimeline.PendingWriteIndex].Cycle);
            }

            for (var i = 0; i < _registerTimeline.Channels.Length; i++)
            {
                candidate = MinWakeCandidate(candidate, _registerTimeline.Channels[i].GetNextWakeCandidateCycle());
            }

            return ClampWakeCandidate(candidate, currentCycle, targetCycle);
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

        public void RenderSample(long targetCycle, Span<float> destination, int frame, int channels)
        {
            AdvanceAudioTo(targetCycle);
            AdvanceRegisterObservableTo(targetCycle);
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

        public void AdvanceRegisterWritesTo(long targetCycle)
            => ApplyRegisterWritesTo(targetCycle);

        internal bool HasRegisterObservableWorkThrough(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < _registerTimeline.LastCycle)
            {
                return false;
            }

            if (HasPendingWriteThrough(_registerTimeline, targetCycle))
            {
                return true;
            }

            foreach (var channel in _registerTimeline.Channels)
            {
                if (channel.GetNextWakeCandidateCycle() <= targetCycle)
                {
                    return true;
                }
            }

            return false;
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
                RefreshCpuInterruptVisibility(targetCycle);
            }

            CompactPendingWrites();
            CompactDmaFetches();
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

            RefreshCpuInterruptVisibility(targetCycle);
            CompactPendingWrites();
            CompactDmaFetches();
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
                    WriteRegisterWord(0x002, timeline.Dmacon);
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
                    WriteRegisterWord(0x01C, timeline.Intena);
                    QueuePendingEnabledAudioInterrupts(timeline.LastCycle);
                    RefreshCpuInterruptVisibility(timeline.LastCycle);
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
                    WriteRegisterWord(0x01E, timeline.Intreq);
                    RefreshCpuInterruptVisibility(timeline.LastCycle);
                }

                return;
            }

            if (offset == 0x09E)
            {
                ApplySetClear(ref timeline.Adkcon, value);
                if (kind == PaulaTimelineKind.Register)
                {
                    WriteRegisterWord(0x010, timeline.Adkcon);
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
                    channel.LengthWords = Math.Max(1, (int)value);
                    break;
                case 0x06:
                    channel.Period = value;
                    break;
                case 0x08:
                    var volume = value & 0x7F;
                    if (volume == 0 && (value & 0x7F00) != 0)
                    {
                        volume = (value >> 8) & 0x7F;
                    }

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
            if (kind != PaulaTimelineKind.Register)
            {
                return;
            }

            if ((uint)channel < AmigaConstants.PaulaChannelCount)
            {
                var bit = GetAudioInterruptBit(channel);
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

            _registerTimeline.Intreq |= bit;
            WriteRegisterWord(0x01E, _registerTimeline.Intreq);
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

        private void RefreshCpuInterruptVisibility(long cycle)
        {
            cycle = Math.Max(0, cycle);
            var active = ActiveInterruptBits;
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
                    _cpuInterruptReleaseCycles[bitIndex] = GetCpuInterruptReleaseCycle(bit, cycle);
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

        private long GetCpuInterruptReleaseCycle(ushort bit, long cycle)
        {
            var recognitionCycle = cycle;
            if ((bit & AmigaConstants.IntreqCopper) != 0 && _copperInterruptRecognitionCycle > recognitionCycle)
            {
                recognitionCycle = _copperInterruptRecognitionCycle;
            }

            return recognitionCycle + AmigaConstants.A500InterruptRecognitionDelayCpuCycles;
        }

        private void ApplyModulationFrom(PaulaTimelineState timeline, int sourceChannel, ushort value)
        {
            var targetChannel = sourceChannel + 1;
            if (sourceChannel < 0 || targetChannel >= timeline.Channels.Length)
            {
                return;
            }

            if (IsVolumeAttached(timeline, sourceChannel))
            {
                timeline.Channels[targetChannel].Volume = Math.Min(64, value & 0x7F);
            }

            if (IsPeriodAttached(timeline, sourceChannel))
            {
                timeline.Channels[targetChannel].Period = value;
            }
        }

        private bool IsAttachedSource(PaulaTimelineState timeline, int channel)
        {
            return IsVolumeAttached(timeline, channel) || IsPeriodAttached(timeline, channel);
        }

        private bool IsVolumeAttached(PaulaTimelineState timeline, int sourceChannel)
        {
            return sourceChannel is >= 0 and <= 2 && (timeline.Adkcon & (1 << sourceChannel)) != 0;
        }

        private bool IsPeriodAttached(PaulaTimelineState timeline, int sourceChannel)
        {
            return sourceChannel is >= 0 and <= 2 && (timeline.Adkcon & (1 << (sourceChannel + 4))) != 0;
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

        private void WriteRegisterWord(ushort offset, ushort value)
        {
            if (offset + 1 >= _registerBytes.Length)
            {
                return;
            }

            _registerBytes[offset] = (byte)(value >> 8);
            _registerBytes[offset + 1] = (byte)value;
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

        private static long GetPeriodCycles(int period)
        {
            return GetEffectivePeriod(period) * AmigaConstants.A500PalCpuCyclesPerColorClock;
        }

        private static long GetDmaPeriodCycles(int period, AmigaBus bus)
        {
            var effectivePeriod = GetEffectivePeriod(period);
            var dmaPeriod = UsesAudioDmaRefillMinimum(bus)
                ? Math.Max(effectivePeriod, bus.AudioDmaMinimumPeriod)
                : effectivePeriod;
            return dmaPeriod * AmigaConstants.A500PalCpuCyclesPerColorClock;
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

        private static bool UsesAudioDmaRefillMinimum(AmigaBus bus)
            => bus.LiveAgnusDmaEnabled;

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
            var consumed = Math.Min(_audioTimeline.PendingWriteIndex, _registerTimeline.PendingWriteIndex);
            if (consumed < 64 || consumed * 2 < _pendingWrites.Count)
            {
                return;
            }

            _pendingWrites.RemoveRange(0, consumed);
            _audioTimeline.PendingWriteIndex -= consumed;
            _registerTimeline.PendingWriteIndex -= consumed;
        }

        private PaulaDmaFetchRecord GetOrCreateDmaFetchRecord(int channel, uint address, long requestedCycle, PaulaTimelineKind kind)
        {
            address = _bus.MaskChipDmaAddress(address);
            for (var i = 0; i < _dmaFetches.Count; i++)
            {
                var fetch = _dmaFetches[i];
                if (fetch.Channel == channel &&
                    fetch.Address == address &&
                    fetch.RequestedCycle == requestedCycle &&
                    !fetch.IsConsumed(kind))
                {
                    fetch.MarkConsumed(kind);
                    return fetch;
                }
            }

            var read = _bus.ReadPaulaDmaWord(address, requestedCycle);
            var record = new PaulaDmaFetchRecord(channel, address, requestedCycle, read);
            record.MarkConsumed(kind);
            _dmaFetches.Add(record);
            return record;
        }

        private void CompactDmaFetches()
        {
            if (_dmaFetches.Count < 128)
            {
                return;
            }

            var removeCount = 0;
            while (removeCount < _dmaFetches.Count && _dmaFetches[removeCount].ConsumedByBoth)
            {
                removeCount++;
            }

            if (removeCount != 0)
            {
                _dmaFetches.RemoveRange(0, removeCount);
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
            public PaulaTimelineState()
            {
                Channels = new PaulaChannel[AmigaConstants.PaulaChannelCount];
                for (var i = 0; i < Channels.Length; i++)
                {
                    Channels[i] = new PaulaChannel(i);
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

        private sealed class PaulaDmaFetchRecord
        {
            public PaulaDmaFetchRecord(int channel, uint address, long requestedCycle, PaulaDmaReadResult read)
            {
                Channel = channel;
                Address = address;
                RequestedCycle = requestedCycle;
                Read = read;
            }

            public int Channel { get; }

            public uint Address { get; }

            public long RequestedCycle { get; }

            public PaulaDmaReadResult Read { get; }

            public bool AudioConsumed { get; private set; }

            public bool RegisterConsumed { get; private set; }

            public bool ConsumedByBoth => AudioConsumed && RegisterConsumed;

            public bool IsConsumed(PaulaTimelineKind kind)
                => kind == PaulaTimelineKind.Audio ? AudioConsumed : RegisterConsumed;

            public void MarkConsumed(PaulaTimelineKind kind)
            {
                if (kind == PaulaTimelineKind.Audio)
                {
                    AudioConsumed = true;
                }
                else
                {
                    RegisterConsumed = true;
                }
            }
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
            private ushort _dataWord;
            private bool _hasDataWord;
            private bool _nextByteIsLow;
            private long _nextSampleCycle;
            private long _nextDmaFetchCycle;
            private ushort _prefetchedDmaWord;
            private bool _hasPrefetchedDmaWord;
            private ushort _pendingDmaWord;
            private bool _hasPendingDmaWord;
            private long _pendingDmaLoadCycle;
            private long _pendingDmaNextFetchCycle;
            private int _pendingDmaInterruptCount;
            private DmaLoadTarget _pendingDmaLoadTarget;
            private uint _currentAddress;
            private int _remainingWords;

            public PaulaChannel(int index)
            {
                Index = index;
                Reset();
            }

            public int Index { get; }

            public uint Location { get; set; }

            public int LengthWords { get; set; }

            public int Period { get; set; }

            public int Volume { get; set; }

            public sbyte CurrentSample { get; set; }

            public bool DmaEnabled { get; set; }

            public void Reset()
            {
                Location = 0;
                LengthWords = 1;
                Period = 428;
                Volume = 64;
                CurrentSample = 0;
                DmaEnabled = false;
                _dataWord = 0;
                _hasDataWord = false;
                _nextByteIsLow = false;
                _nextSampleCycle = 0;
                _nextDmaFetchCycle = long.MaxValue;
                _prefetchedDmaWord = 0;
                _hasPrefetchedDmaWord = false;
                _pendingDmaWord = 0;
                _hasPendingDmaWord = false;
                _pendingDmaLoadCycle = long.MaxValue;
                _pendingDmaNextFetchCycle = long.MaxValue;
                _pendingDmaInterruptCount = 0;
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
                    _hasDataWord = false;
                    _nextByteIsLow = false;
                    _nextDmaFetchCycle = long.MaxValue;
                    ClearPrefetchedDmaWord();
                    ClearPendingDmaWord();
                    return;
                }

                DmaEnabled = true;
                _hasDataWord = false;
                _nextByteIsLow = false;
                ClearPrefetchedDmaWord();
                ClearPendingDmaWord();
                _currentAddress = bus.MaskChipDmaAddress(Location);
                _remainingWords = Math.Max(1, LengthWords);
                _nextDmaFetchCycle = cycle;
                RequestDmaWord(bus, cycle, paula, timeline, kind, DmaLoadTarget.StartupDiscard, forceInterrupt: true);
            }

            public void WriteData(ushort value, long cycle, Paula paula, PaulaTimelineState timeline, PaulaTimelineKind kind)
            {
                _dataWord = value;
                _hasDataWord = true;
                ClearPrefetchedDmaWord();
                ClearPendingDmaWord();
                _nextByteIsLow = true;
                CurrentSample = unchecked((sbyte)(value >> 8));
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
                _nextDmaFetchCycle = long.MaxValue;
                paula.RequestAudioInterrupt(kind, Index, cycle);
            }

            public void AdvanceTo(long targetCycle, AmigaBus bus, Paula paula, PaulaTimelineState timeline, PaulaTimelineKind kind)
            {
                if (!_hasDataWord && !DmaEnabled && !_hasPendingDmaWord && !_hasPrefetchedDmaWord)
                {
                    return;
                }

                while (true)
                {
                    if (_hasPendingDmaWord)
                    {
                        var sampleDue = _hasDataWord && _nextSampleCycle <= targetCycle;
                        if (_pendingDmaLoadCycle <= targetCycle &&
                            (!sampleDue || _pendingDmaLoadCycle <= _nextSampleCycle))
                        {
                            CompletePendingDmaWord(bus, paula, timeline, kind);
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
                        paula.ApplyModulationFrom(timeline, Index, _dataWord);
                        _nextSampleCycle += GetPeriodCycles(Period);
                        if (DmaEnabled && UsesAudioDmaRefillMinimum(bus))
                        {
                            _nextSampleCycle = Math.Max(_nextSampleCycle, _nextDmaFetchCycle);
                        }

                        continue;
                    }

                    if (DmaEnabled)
                    {
                        if (_hasPrefetchedDmaWord)
                        {
                            var word = _prefetchedDmaWord;
                            ClearPrefetchedDmaWord();
                            StartDmaWordOutput(
                                word,
                                _nextSampleCycle,
                                bus,
                                paula,
                                timeline,
                                kind);
                            continue;
                        }

                        if (!_hasPendingDmaWord)
                        {
                            RequestDmaWord(
                                bus,
                                _nextSampleCycle,
                                paula,
                                timeline,
                                kind,
                                DmaLoadTarget.Prefetch,
                                forceInterrupt: false);
                        }

                        _nextSampleCycle += GetPeriodCycles(Period);
                    }
                    else
                    {
                        _hasDataWord = false;
                        break;
                    }
                }
            }

            public long? GetNextWakeCandidateCycle()
            {
                long? candidate = null;
                if (_hasPendingDmaWord)
                {
                    candidate = _pendingDmaLoadCycle;
                }

                if (DmaEnabled || _hasDataWord)
                {
                    candidate = MinWakeCandidate(candidate, _nextSampleCycle);
                }

                return candidate;
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
                    _hasDataWord,
                    _nextByteIsLow,
                    _nextSampleCycle);
            }

            private void RequestDmaWord(
                AmigaBus bus,
                long cycle,
                Paula paula,
                PaulaTimelineState timeline,
                PaulaTimelineKind kind,
                DmaLoadTarget loadTarget,
                bool forceInterrupt)
            {
                if (_hasPendingDmaWord || _hasPrefetchedDmaWord)
                {
                    return;
                }

                var requestCycle = UsesAudioDmaRefillMinimum(bus)
                    ? Math.Max(cycle, _nextDmaFetchCycle)
                    : cycle;
                var interruptCount = 0;
                if (_remainingWords <= 0)
                {
                    _currentAddress = bus.MaskChipDmaAddress(Location);
                    _remainingWords = Math.Max(1, LengthWords);
                    interruptCount++;
                }

                var dmaFetch = paula.GetOrCreateDmaFetchRecord(Index, _currentAddress, requestCycle, kind);
                var dmaRead = dmaFetch.Read;
                _currentAddress = bus.AddChipDmaPointerOffset(_currentAddress, 2);
                _remainingWords--;
                _pendingDmaWord = dmaRead.Value;
                _hasPendingDmaWord = true;
                _pendingDmaLoadCycle = dmaRead.BusAccess.CompletedCycle;
                _pendingDmaNextFetchCycle = requestCycle + (GetDmaPeriodCycles(Period, bus) * 2);
                _pendingDmaLoadTarget = loadTarget;
                _nextDmaFetchCycle = _pendingDmaNextFetchCycle;
                if (!_hasDataWord)
                {
                    _nextSampleCycle = _pendingDmaLoadCycle;
                }

                if (forceInterrupt || _remainingWords == 0)
                {
                    interruptCount++;
                }

                _pendingDmaInterruptCount = interruptCount;
                if (_pendingDmaLoadCycle <= cycle)
                {
                    CompletePendingDmaWord(bus, paula, timeline, kind);
                }
            }

            private void CompletePendingDmaWord(
                AmigaBus bus,
                Paula paula,
                PaulaTimelineState timeline,
                PaulaTimelineKind kind)
            {
                var loadCycle = _pendingDmaLoadCycle;
                var interruptCount = _pendingDmaInterruptCount;
                var word = _pendingDmaWord;
                var loadTarget = _pendingDmaLoadTarget;
                ClearPendingDmaWord();
                for (var i = 0; i < interruptCount; i++)
                {
                    paula.RequestAudioInterrupt(kind, Index, loadCycle);
                }

                if (loadTarget == DmaLoadTarget.StartupDiscard)
                {
                    if (DmaEnabled)
                    {
                        RequestDmaWord(bus, loadCycle, paula, timeline, kind, DmaLoadTarget.Prefetch, forceInterrupt: false);
                    }

                    return;
                }

                if (!_hasDataWord)
                {
                    StartDmaWordOutput(word, loadCycle, bus, paula, timeline, kind);
                    return;
                }

                _prefetchedDmaWord = word;
                _hasPrefetchedDmaWord = true;
            }

            private void StartDmaWordOutput(
                ushort word,
                long cycle,
                AmigaBus bus,
                Paula paula,
                PaulaTimelineState timeline,
                PaulaTimelineKind kind)
            {
                _dataWord = word;
                _hasDataWord = true;
                _nextByteIsLow = true;
                CurrentSample = unchecked((sbyte)(word >> 8));
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
                if (DmaEnabled)
                {
                    RequestDmaWord(bus, cycle, paula, timeline, kind, DmaLoadTarget.Prefetch, forceInterrupt: false);
                }
            }

            private void ClearPrefetchedDmaWord()
            {
                _prefetchedDmaWord = 0;
                _hasPrefetchedDmaWord = false;
            }

            private void ClearPendingDmaWord()
            {
                _pendingDmaWord = 0;
                _hasPendingDmaWord = false;
                _pendingDmaLoadCycle = long.MaxValue;
                _pendingDmaNextFetchCycle = long.MaxValue;
                _pendingDmaInterruptCount = 0;
                _pendingDmaLoadTarget = DmaLoadTarget.Prefetch;
            }

            private enum DmaLoadTarget
            {
                StartupDiscard,
                Prefetch
            }

        }
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
            bool hasDataWord,
            bool nextByteIsLow,
            long nextSampleCycle)
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
            HasDataWord = hasDataWord;
            NextByteIsLow = nextByteIsLow;
            NextSampleCycle = nextSampleCycle;
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

        public bool HasDataWord { get; }

        public bool NextByteIsLow { get; }

        public long NextSampleCycle { get; }
    }
}
