using System;
using System.Collections.Generic;
using CopperMod.Abstractions;

namespace CopperMod.Sid
{
    internal enum SidRegisterReadAdvanceKind
    {
        BusOnly,
        Digital
    }

    internal sealed class SidSystem
    {
        private const int MaxCapturedWrites = 65536;
        private readonly BoundedSidWriteLog _writes = new BoundedSidWriteLog(MaxCapturedWrites);
        private readonly List<PendingSidWrite> _pendingWrites = new List<PendingSidWrite>(4096);
        private readonly int _channelCount;
        private readonly SidChip[] _registerChips;
        private long _lastCycle;
        private long _registerLastCycle;
        private long _registerBusLastCycle;
        private double _sampleAccumulator;
        private long _sampleCycles;
        private int _pendingWriteIndex;
        private int _registerPendingWriteIndex;
        private int _registerBusPendingWriteIndex;
        private double[]? _channelAccumulator;
        private double[]? _channelScratch;
        private float[][]? _captureSamples;
        private int _captureFrameIndex;
        private int _captureSampleRate;
        private int _mutedVoicesMask;
        private SidCycleTrace? _trace;
        private const int DefaultOutputSampleRate = 44100;
        private readonly SidWindowedSincResampler _resampler;

        public SidSystem(
            IReadOnlyList<SidChipPlacement> placements,
            SidChipModel model,
            int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond,
            SidFilterProfileId filterProfile = SidFilterProfileId.Auto,
            SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            if (placements is null || placements.Count == 0)
            {
                throw new ArgumentException("At least one SID chip placement is required.", nameof(placements));
            }

            Chips = new SidChip[placements.Count];
            _registerChips = new SidChip[placements.Count];
            for (var i = 0; i < placements.Count; i++)
            {
                var chipModel = placements[i].ResolveModel(model);
                Chips[i] = new SidChip(chipModel, placements[i].BaseAddress, cpuCyclesPerSecond, filterProfile, sidEmulationProfile);
                Chips[i].TraceChipIndex = i;
                _registerChips[i] = new SidChip(chipModel, placements[i].BaseAddress, cpuCyclesPerSecond, filterProfile, sidEmulationProfile);
                _registerChips[i].TraceChipIndex = i;
            }

            _channelCount = Chips.Length * 3;
            _resampler = new SidWindowedSincResampler(cpuCyclesPerSecond, DefaultOutputSampleRate);
        }

        public SidChip[] Chips { get; }

        public IReadOnlyList<SidRegisterWrite> Writes => _writes;

        public SidCycleTrace? Trace
        {
            get => _trace;
            set
            {
                _trace = value;
                foreach (var chip in Chips)
                {
                    chip.Trace = value;
                }
            }
        }

        public int MutedVoicesMask
        {
            get => _mutedVoicesMask;
            set
            {
                _mutedVoicesMask = value & 0x07;
                foreach (var chip in Chips)
                {
                    chip.MutedVoicesMask = _mutedVoicesMask;
                }

                foreach (var chip in _registerChips)
                {
                    chip.MutedVoicesMask = _mutedVoicesMask;
                }
            }
        }

        public void ConfigureOutput(int sampleRate)
        {
            _resampler.Configure(sampleRate);
        }

        /// <summary>
        /// When true, RenderSample returns the raw full-rate average instead of the
        /// band-limited resampler output. Analysis consumers (loop/duration detection,
        /// D418 transition measurement) opt in so the anti-aliasing filter's group delay
        /// and smoothing do not reshape the signal envelope they inspect. Real audio
        /// playback leaves this false.
        /// </summary>
        public bool UseUnfilteredOutput { get; set; }

        public void Reset()
        {
            _lastCycle = 0;
            _registerLastCycle = 0;
            _registerBusLastCycle = 0;
            _sampleAccumulator = 0;
            _sampleCycles = 0;
            _resampler.Reset();
            _pendingWriteIndex = 0;
            _registerPendingWriteIndex = 0;
            _registerBusPendingWriteIndex = 0;
            _channelAccumulator = null;
            _channelScratch = null;
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            _writes.Clear();
            _pendingWrites.Clear();
            foreach (var chip in Chips)
            {
                chip.Reset();
                chip.MutedVoicesMask = _mutedVoicesMask;
            }

            foreach (var chip in _registerChips)
            {
                chip.Reset();
                chip.MutedVoicesMask = _mutedVoicesMask;
            }
        }

        public void ResetClock()
        {
            _lastCycle = 0;
            _registerLastCycle = 0;
            _registerBusLastCycle = 0;
            _registerPendingWriteIndex = _pendingWriteIndex;
            _registerBusPendingWriteIndex = _pendingWriteIndex;
            for (var i = 0; i < Chips.Length; i++)
            {
                _registerChips[i].CopyStateFrom(Chips[i]);
            }

            DiscardAccumulatedOutput();
            _resampler.Reset();
            CompactPendingWrites();
        }

        public void DiscardAccumulatedOutput()
        {
            _sampleAccumulator = 0;
            _sampleCycles = 0;
            if (_channelAccumulator != null)
            {
                Array.Clear(_channelAccumulator);
            }
        }

        internal void ClearCapturedWrites()
        {
            _writes.Clear();
        }

        [HotPath]
        public bool TryWrite(ushort address, byte value, long cycle)
        {
            var chipIndex = TryMapRegister(address, out var register);
            if (chipIndex < 0)
            {
                return false;
            }

            CaptureWrite(new SidRegisterWrite(cycle, chipIndex, register, value));
            if (cycle <= _lastCycle)
            {
                Chips[chipIndex].Write(register, value, cycle);
                _registerChips[chipIndex].Write(register, value, cycle);
            }
            else
            {
                _pendingWrites.Add(new PendingSidWrite(cycle, chipIndex, register, value));
            }

            return true;
        }

        [HotPath]
        public bool TryRead(ushort address, out byte value)
        {
            return TryRead(address, _lastCycle, out value);
        }

        [HotPath]
        public bool TryRead(ushort address, long cycle, out byte value)
        {
            var chipIndex = TryMapRegister(address, out var register);
            if (chipIndex < 0)
            {
                value = 0;
                return false;
            }

            if (Trace != null)
            {
                if (cycle > _lastCycle)
                {
                    AdvanceTo(cycle);
                }

                value = Chips[chipIndex].Read(register, cycle);
                return true;
            }

            AdvanceRegisterObservableTo(cycle, GetRegisterReadAdvanceKind(register));
            value = _registerChips[chipIndex].Read(register, cycle);
            return true;
        }

        [HotPath]
        private int TryMapRegister(ushort address, out byte register)
        {
            for (var i = 0; i < Chips.Length; i++)
            {
                var chip = Chips[i];
                if (address >= chip.BaseAddress && address < chip.BaseAddress + 0x20)
                {
                    register = (byte)(address - chip.BaseAddress);
                    return i;
                }
            }

            if (address >= 0xD400 && address <= 0xD7FF)
            {
                for (var i = 0; i < Chips.Length; i++)
                {
                    var chip = Chips[i];
                    if (chip.BaseAddress == SidConstants.DefaultSidBaseAddress)
                    {
                        register = (byte)((address - chip.BaseAddress) & 0x1F);
                        return i;
                    }
                }
            }

            register = 0;
            return -1;
        }

        [HotPath]
        private void CaptureWrite(SidRegisterWrite write)
        {
            _writes.Add(write);
        }

        [HotPath]
        public float RenderSample(long cycle)
        {
            AdvanceTo(cycle);
            if (_sampleCycles == 0)
            {
                AccumulateOneCycle(_lastCycle + 1);
            }

            // Real audio output is band-limited by the windowed-sinc resampler. Analysis
            // consumers opt into the raw full-rate average (see UseUnfilteredOutput), since
            // they inspect the signal envelope and must not be reshaped or delayed by the
            // anti-aliasing filter.
            var sample = UseUnfilteredOutput ? _sampleAccumulator / _sampleCycles : _resampler.Read();
            CaptureChannelSample();
            DiscardAccumulatedOutput();
            return (float)Math.Clamp(sample, -1.0, 1.0);
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

            _captureSamples = new float[_channelCount][];
            for (var i = 0; i < _captureSamples.Length; i++)
            {
                _captureSamples[i] = new float[frames];
            }

            _captureFrameIndex = 0;
            _captureSampleRate = sampleRate;
            _channelAccumulator ??= new double[_channelCount];
            _channelScratch ??= new double[_channelCount];
            Array.Clear(_channelAccumulator);
            Array.Clear(_channelScratch);
        }

        public ModuleChannelWaveform? FinishChannelCapture()
        {
            if (_captureSamples == null)
            {
                return null;
            }

            var channels = new ModuleChannelWaveformChannel[_captureSamples.Length];
            for (var i = 0; i < _captureSamples.Length; i++)
            {
                channels[i] = new ModuleChannelWaveformChannel(i, _captureSamples[i], IsActive(_captureSamples[i]));
            }

            var result = new ModuleChannelWaveform(channels, _captureFrameIndex, _captureSampleRate);
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            return result;
        }

        internal SidSystemTimingSnapshot CaptureTimingSnapshot()
            => new SidSystemTimingSnapshot(
                _lastCycle,
                _registerLastCycle,
                _registerBusLastCycle,
                _sampleCycles,
                _sampleAccumulator,
                _captureFrameIndex,
                _pendingWrites.Count,
                _pendingWriteIndex,
                _registerPendingWriteIndex,
                _registerBusPendingWriteIndex);

        internal SidChipDebugState GetRegisterChipDebugState(int chipIndex)
            => _registerChips[chipIndex].DebugState;

        [HotPath]
        private static SidRegisterReadAdvanceKind GetRegisterReadAdvanceKind(byte register)
        {
            register = (byte)(register & 0x1F);
            return register is 0x1B or 0x1C
                ? SidRegisterReadAdvanceKind.Digital
                : SidRegisterReadAdvanceKind.BusOnly;
        }

        [HotPath]
        private void AdvanceRegisterObservableTo(long targetCycle, SidRegisterReadAdvanceKind kind)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (kind == SidRegisterReadAdvanceKind.Digital)
            {
                AdvanceRegisterDigitalTo(targetCycle);
            }
            else
            {
                AdvanceRegisterBusWritesTo(targetCycle);
            }

            CompactPendingWrites();
        }

        [HotPath]
        private void AdvanceRegisterBusWritesTo(long targetCycle)
        {
            if (targetCycle <= _registerBusLastCycle)
            {
                return;
            }

            while (_registerBusPendingWriteIndex < _pendingWrites.Count &&
                _pendingWrites[_registerBusPendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_registerBusPendingWriteIndex++];
                _registerChips[write.ChipIndex].WriteBusValueOnly(write.Value, write.Cycle);
            }

            _registerBusLastCycle = targetCycle;
        }

        [HotPath]
        private void AdvanceRegisterDigitalTo(long targetCycle)
        {
            if (targetCycle <= _registerLastCycle)
            {
                return;
            }

            while (_registerPendingWriteIndex < _pendingWrites.Count &&
                _pendingWrites[_registerPendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_registerPendingWriteIndex++];
                AdvanceRegisterChips(write.Cycle - _registerLastCycle);
                _registerChips[write.ChipIndex].Write(write.Register, write.Value, write.Cycle);
            }

            AdvanceRegisterChips(targetCycle - _registerLastCycle);
            _registerBusPendingWriteIndex = Math.Max(_registerBusPendingWriteIndex, _registerPendingWriteIndex);
            _registerBusLastCycle = Math.Max(_registerBusLastCycle, targetCycle);
        }

        [HotPath]
        private void AdvanceRegisterChips(long cycles)
        {
            if (cycles <= 0)
            {
                return;
            }

            var firstCycle = _registerLastCycle + 1;
            for (var i = 0; i < _registerChips.Length; i++)
            {
                _registerChips[i].AdvanceRegisterObservable(firstCycle, cycles);
            }

            _registerLastCycle += cycles;
        }

        [HotPath]
        public void AdvanceTo(long targetCycle)
        {
            if (targetCycle <= _lastCycle)
            {
                return;
            }

            while (_pendingWriteIndex < _pendingWrites.Count && _pendingWrites[_pendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_pendingWriteIndex++];
                AccumulateCycles(write.Cycle - _lastCycle);
                Chips[write.ChipIndex].Write(write.Register, write.Value, write.Cycle);
            }

            AccumulateCycles(targetCycle - _lastCycle);
            SyncRegisterTimelineToAudioForCompaction();
            CompactPendingWrites();
        }

        [HotPath]
        private void AccumulateCycles(long cycles)
        {
            if (cycles <= 0)
            {
                return;
            }

            // Every synthesized cycle must be pushed into the windowed-sinc resampler's
            // delay line, so the former single-chip batch-sum fast path cannot be used.
            for (var i = 0; i < cycles; i++)
            {
                AccumulateOneCycle(_lastCycle + i + 1);
            }

            _lastCycle += cycles;
        }

        [HotPath]
        private void CompactPendingWrites()
        {
            var consumed = Math.Min(
                _pendingWriteIndex,
                Math.Min(_registerPendingWriteIndex, _registerBusPendingWriteIndex));
            if (consumed < 64 || consumed * 2 < _pendingWrites.Count)
            {
                return;
            }

            _pendingWrites.RemoveRange(0, consumed);
            _pendingWriteIndex -= consumed;
            _registerPendingWriteIndex -= consumed;
            _registerBusPendingWriteIndex -= consumed;
        }

        private void SyncRegisterTimelineToAudioForCompaction()
        {
            if (_pendingWriteIndex < 64 || _pendingWriteIndex * 2 < _pendingWrites.Count)
            {
                return;
            }

            if (_lastCycle < _registerLastCycle ||
                _lastCycle < _registerBusLastCycle ||
                _pendingWriteIndex < _registerBusPendingWriteIndex)
            {
                return;
            }

            for (var i = 0; i < Chips.Length; i++)
            {
                _registerChips[i].CopyStateFrom(Chips[i]);
            }

            _registerLastCycle = _lastCycle;
            _registerBusLastCycle = _lastCycle;
            _registerPendingWriteIndex = _pendingWriteIndex;
            _registerBusPendingWriteIndex = _pendingWriteIndex;
        }

        [HotPath]
        private void AccumulateOneCycle(long cycle)
        {
            var sample = 0.0;
            var captureChannels = _captureSamples != null;
            var channelScratch = captureChannels ? _channelScratch : null;
            var channelAccumulator = captureChannels ? _channelAccumulator : null;

            for (var i = 0; i < Chips.Length; i++)
            {
                var offset = i * 3;
                sample += Chips[i].RenderOneCycle(cycle, channelScratch, offset);
                if (!captureChannels || channelScratch == null || channelAccumulator == null)
                {
                    continue;
                }

                channelAccumulator[offset] += channelScratch[offset];
                channelAccumulator[offset + 1] += channelScratch[offset + 1];
                channelAccumulator[offset + 2] += channelScratch[offset + 2];
            }

            // Multiple physical SIDs are summed by the output network. Do not normalize by
            // the number of installed chips: doing so attenuates active audio when another
            // declared chip is silent. Final headroom limiting happens at the sample boundary.
            _sampleAccumulator += sample;
            _sampleCycles++;

            // Feed the full-rate mixed sample into the anti-aliasing resampler. The
            // boxcar bookkeeping above is retained only for channel-capture averaging
            // and the timing snapshot; the audio output is taken from the resampler.
            _resampler.Push(sample);
        }

        [HotPath]
        private void CaptureChannelSample()
        {
            if (_captureSamples == null || _channelAccumulator == null || _sampleCycles == 0)
            {
                return;
            }

            if (_captureFrameIndex >= _captureSamples[0].Length)
            {
                return;
            }

            for (var channel = 0; channel < _captureSamples.Length; channel++)
            {
                _captureSamples[channel][_captureFrameIndex] = (float)Math.Clamp(_channelAccumulator[channel] / _sampleCycles, -1.0, 1.0);
            }

            _captureFrameIndex++;
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

        private readonly struct PendingSidWrite
        {
            public PendingSidWrite(long cycle, int chipIndex, byte register, byte value)
            {
                Cycle = cycle;
                ChipIndex = chipIndex;
                Register = register;
                Value = value;
            }

            public long Cycle { get; }

            public int ChipIndex { get; }

            public byte Register { get; }

            public byte Value { get; }
        }
    }
}
