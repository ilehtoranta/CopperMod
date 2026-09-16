using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Compact four-channel Paula audio state on the machine's canonical clock.
/// Register, manual playback, fixed-slot DMA and direct stereo output.
/// </summary>
internal sealed partial class LightweightPaulaAudio
{
    private const ushort DmaconMaster = 0x0200;
    private readonly Channel[] _channels =
    {
        new(0),
        new(1),
        new(2),
        new(3)
    };

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long PendingOutputCycle { get; private set; } = long.MaxValue;
    internal long LastOutputCycle { get; private set; } = long.MinValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool OwnsOutputSlot(long cycle)
        => PendingOutputCycle == cycle || LastOutputCycle == cycle;

    internal void Reset()
    {
        ResetOutput();
        ResetHardware(0);
    }

    internal void ResetHardware(long cycle)
    {
        EmitOutputTo(cycle);
        _volumeAttachMask = _periodAttachMask = 0;
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            _channels[channel].Reset();
        }
        NextCycle = long.MaxValue;
        PendingOutputCycle = long.MaxValue;
        LastOutputCycle = long.MinValue;
    }

    internal void OnRegisterWrite(
        ushort offset,
        ushort value,
        long cycle,
        LightweightA500Machine machine)
    {
        EmitOutputTo(cycle);
        var channelIndex = GetChannelIndex(offset);
        if (channelIndex < 0)
        {
            return;
        }

        ref var channel = ref _channels[channelIndex];
        var register = offset - (LightweightRegisters.Aud0lch + (channelIndex * 0x10));
        switch (register)
        {
            case 0x00:
                channel.Location =
                    ((uint)(value & 0x0007) << 16) |
                    (channel.Location & 0x0000_FFFFu);
                break;
            case 0x02:
                channel.Location =
                    (channel.Location & 0x0007_0000u) |
                    (uint)(value & 0xFFFE);
                break;
            case 0x04:
                channel.LengthWords = value == 0 ? 65_536 : value;
                break;
            case 0x06:
                channel.Period = value;
                break;
            case 0x08:
                channel.Volume = Math.Min(value & 0x007F, 64);
                break;
            case 0x0A:
                channel.WriteData(value, cycle, machine, this);
                RefreshNextCycle();
                break;
        }
        // Location/length apply at DMA reload; period applies at the next
        // byte transition. None changes an already-published physical phase.
    }

    internal void OnDmaconChanged(
        ushort previous,
        ushort current,
        long cycle,
        LightweightA500Machine machine)
    {
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            var bit = (ushort)(1 << channel);
            if (IsAudioDmaEnabled(previous, bit) != IsAudioDmaEnabled(current, bit))
            {
                _channels[channel].SetDmaEnabled(
                    IsAudioDmaEnabled(current, bit), cycle, machine);
            }
        }
        RefreshNextCycle();
    }

    internal void OnAdkconChanged(
        ushort current,
        long cycle)
    {
        EmitOutputTo(cycle);
        _volumeAttachMask = current & 15;
        _periodAttachMask = (current >> 4) & 15;
    }

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Paula must advance at its published physical phase.");

        EmitOutputTo(cycle);
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            if (_channels[channel].DmaOutputCycle == cycle)
                LastOutputCycle = cycle;
            _channels[channel].AdvanceAt(cycle, machine, this);
        }

        RefreshNextCycle();
    }

    internal LightweightPaulaChannelSnapshot GetChannelSnapshot(int channel)
    {
        if ((uint)channel >= _channels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        return _channels[channel].CaptureSnapshot();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAudioDmaEnabled(ushort dmacon, ushort channelBit)
        => (dmacon & (DmaconMaster | channelBit)) ==
            (DmaconMaster | channelBit);

    private static int GetChannelIndex(ushort offset)
    {
        if (offset < LightweightRegisters.Aud0lch ||
            offset > LightweightRegisters.Aud3dat)
        {
            return -1;
        }

        var relative = offset - LightweightRegisters.Aud0lch;
        var channel = relative >> 4;
        var register = relative & 0x0F;
        return channel < 4 && register <= 0x0A && (register & 1) == 0
            ? channel
            : -1;
    }

    private void RefreshNextCycle()
    {
        var next = long.MaxValue;
        var output = long.MaxValue;
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            var candidate = _channels[channel].NextEventCycle;
            if (candidate < next)
            {
                next = candidate;
            }
            output = Math.Min(output, _channels[channel].DmaOutputCycle);
        }
        NextCycle = next;
        PendingOutputCycle = output;
    }

    private struct Channel
    {
        private readonly int _index;
        private ushort _holdingLatch;
        private bool _holdingLatchWritten;
        private ushort _outputLatch;
        private bool _hasOutputWord;
        private bool _nextByteIsLow;
        private bool _manualPlayback;
        private int _irqCheck;
        private long _manualIrqCheckCycle;
        private long _nextSampleCycle;
        private bool _dmaEnabled;
        private bool _streamInitialized;
        private bool _discardRequest;
        private bool _pendingDiscard;
        private bool _pendingReload;
        private bool _delayedInterrupt;
        private bool _hasPrefetch;
        private uint _currentAddress;
        private int _remainingWords;
        private uint _pendingAddress;
        private int _pendingRemainingWords;
        private ushort _pendingData;
        private long _dmaInputCycle;
        private long _dmaOutputCycle;
        private long _dmaLoadCycle;

        internal Channel(int index)
        {
            this = default;
            _index = index;
            Reset();
        }

        internal uint Location { get; set; }
        internal int LengthWords { get; set; }
        internal int Period { get; set; }
        internal int Volume { get; set; }
        internal sbyte CurrentSample { get; private set; }
#if LIGHTWEIGHT_DIAGNOSTICS
        internal LightweightPaulaAudioState State { get; private set; }
#else
        internal LightweightPaulaAudioState State => LightweightPaulaAudioState.NotRecorded;
#endif

        [System.Diagnostics.Conditional("LIGHTWEIGHT_DIAGNOSTICS")]
        private void RecordState(LightweightPaulaAudioState state)
        {
#if LIGHTWEIGHT_DIAGNOSTICS
            State = state;
#endif
        }
        internal long DmaOutputCycle => _dmaOutputCycle;
        internal long NextEventCycle => Math.Min(Math.Min(
            _nextSampleCycle, _manualIrqCheckCycle),
            Math.Min(_dmaInputCycle, Math.Min(_dmaOutputCycle, _dmaLoadCycle)));

        internal void Reset()
        {
            Location = 0;
            LengthWords = 1;
            Period = 428;
            Volume = 64;
            CurrentSample = 0;
            _holdingLatch = 0;
            _holdingLatchWritten = false;
            _outputLatch = 0;
            _hasOutputWord = false;
            _nextByteIsLow = false;
            _manualPlayback = false;
            _irqCheck = 0;
            _manualIrqCheckCycle = long.MaxValue;
            _nextSampleCycle = long.MaxValue;
            RecordState(LightweightPaulaAudioState.Idle);
            _dmaEnabled = false;
            _streamInitialized = false;
            _discardRequest = false;
            _pendingDiscard = false;
            _pendingReload = false;
            _delayedInterrupt = false;
            _hasPrefetch = false;
            _currentAddress = 0;
            _remainingWords = 0;
            _pendingAddress = 0;
            _pendingRemainingWords = 0;
            _pendingData = 0;
            _dmaInputCycle = _dmaOutputCycle = _dmaLoadCycle = long.MaxValue;
        }

        internal void SetDmaEnabled(bool enabled, long cycle, LightweightA500Machine machine)
        {
            _dmaEnabled = enabled;
            if (!enabled)
            {
                _dmaInputCycle = long.MaxValue;
                // An accepted physical transfer finishes even after DMAOFF.
                if (!_hasOutputWord)
                    EnterIdle();
                return;
            }

            _manualPlayback = false;
            _manualIrqCheckCycle = long.MaxValue;
            if (_hasOutputWord && _streamInitialized)
            {
                RequestWord(cycle);
                return;
            }
            _hasOutputWord = false;
            _nextSampleCycle = long.MaxValue;
            _hasPrefetch = false;
            _currentAddress = Location;
            _remainingWords = LengthWords;
            _streamInitialized = true;
            _discardRequest = true;
            RecordState(LightweightPaulaAudioState.DmaStartup);
            ConsumeDelayedInterrupt(cycle, machine);
            RequestWord(cycle);
        }

        private void RequestWord(long cycle)
        {
            if (!_dmaEnabled || _hasPrefetch || _dmaInputCycle != long.MaxValue ||
                _dmaOutputCycle != long.MaxValue || _dmaLoadCycle != long.MaxValue)
                return;
            // One request uses the next channel-specific address phase. Late
            // enables never recreate an input that has already happened.
            var lineStart = cycle - cycle % LightweightClock.CpuCyclesPerLine;
            var input = lineStart + (0x0F + _index * 2) * 2;
            if (input <= cycle) input += LightweightClock.CpuCyclesPerLine;
            _dmaInputCycle = input;
        }

        private void AdvanceDmaAt(long cycle, LightweightA500Machine machine, LightweightPaulaAudio audio)
        {
            if (_dmaInputCycle == cycle)
            {
                _dmaInputCycle = long.MaxValue;
                _pendingReload = _remainingWords == 0;
                _pendingAddress = _pendingReload ? Location : _currentAddress;
                _pendingRemainingWords = (_pendingReload ? LengthWords : _remainingWords) - 1;
                _pendingDiscard = _discardRequest;
                _dmaOutputCycle = cycle + 2;
            }
            if (_dmaOutputCycle == cycle)
            {
                _pendingData = machine.ReadChipWordDma(_pendingAddress);
                _currentAddress = (_pendingAddress + 2) & 0x0007_FFFEu;
                _remainingWords = _pendingRemainingWords;
                if (_pendingReload && !_pendingDiscard && _hasOutputWord)
                    _delayedInterrupt = true;
                _dmaOutputCycle = long.MaxValue;
                _dmaLoadCycle = cycle + 2;
                // If the low byte has exhausted its period, accepted data
                // cannot be consumed before the following Paula input phase.
                if (_hasOutputWord && !_nextByteIsLow && _nextSampleCycle < _dmaLoadCycle)
                    _nextSampleCycle = _dmaLoadCycle;
            }
            if (_dmaLoadCycle == cycle)
            {
                _dmaLoadCycle = long.MaxValue;
                _holdingLatch = _pendingData;
                _holdingLatchWritten = !_pendingDiscard;
                if (_pendingDiscard)
                {
                    _discardRequest = false;
                    machine.LatchAudioInterrupt(_index, cycle);
                    if (_dmaEnabled)
                    {
                        RecordState(LightweightPaulaAudioState.DmaStartupData);
                        RequestWord(cycle);
                    }
                }
                else if (_dmaEnabled && !_hasOutputWord)
                    StartDmaWord(_pendingData, cycle, machine, audio);
                else
                {
                    _hasPrefetch = true;
                }
            }
        }

        private void StartDmaWord(ushort word, long cycle, LightweightA500Machine machine, LightweightPaulaAudio audio)
        {
            _outputLatch = word;
            _holdingLatchWritten = false;
            _hasOutputWord = true;
            _nextByteIsLow = true;
            _manualPlayback = false;
            _manualIrqCheckCycle = long.MaxValue;
            _irqCheck = 0;
            CurrentSample = unchecked((sbyte)(word >> 8));
            RecordState(LightweightPaulaAudioState.HighByte);
            _nextSampleCycle = cycle + GetPeriodCycles(Period);
            audio.ApplyVolume(_index, word);
            if (audio.UsesHighByteRequest(_index))
            {
                ConsumeDelayedInterrupt(cycle, machine);
                RequestWord(cycle);
            }
        }

        private void ConsumeDelayedInterrupt(long cycle, LightweightA500Machine machine)
        {
            if (!_delayedInterrupt || (machine.Intreq & GetInterruptBit(_index)) != 0)
                return;
            _delayedInterrupt = false;
            machine.LatchAudioInterrupt(_index, cycle);
        }

        internal void WriteData(
            ushort value,
            long cycle,
            LightweightA500Machine machine,
            LightweightPaulaAudio audio)
        {
            _holdingLatch = value;
            _holdingLatchWritten = true;

            var dmaBit = (ushort)(1 << _index);
            var dmaEnabled = IsAudioDmaEnabled(machine.Dmacon, dmaBit);
            var interruptPending = (machine.Intreq & GetInterruptBit(_index)) != 0;
            if (dmaEnabled && _hasOutputWord)
                _hasPrefetch = true;
            if (dmaEnabled || _hasOutputWord || interruptPending)
            {
                return;
            }

            StartManualWord(cycle, machine, audio);
        }

        internal void AdvanceAt(long cycle, LightweightA500Machine machine, LightweightPaulaAudio audio)
        {
            AdvanceDmaAt(cycle, machine, audio);
            if (_manualIrqCheckCycle == cycle)
            {
                _irqCheck = (machine.Intreq & GetInterruptBit(_index)) != 0
                    ? 1
                    : -1;
                _manualIrqCheckCycle = long.MaxValue;
            }

            if (_nextSampleCycle != cycle)
            {
                return;
            }

            if (_hasOutputWord && _nextByteIsLow)
            {
                CurrentSample = unchecked((sbyte)_outputLatch);
                _nextByteIsLow = false;
                RecordState(LightweightPaulaAudioState.LowByte);
                audio.ApplyPeriod(_index, _outputLatch);
                if (_dmaEnabled && audio.IsPeriodAttached(_index))
                {
                    ConsumeDelayedInterrupt(cycle, machine);
                    RequestWord(cycle);
                }
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
                if (!_dmaEnabled) BeginManualCompletionPeriod(machine);
                return;
            }

            if (_dmaEnabled)
            {
                if (audio.UsesHighByteRequest(_index))
                    ConsumeDelayedInterrupt(cycle, machine);
                if (_hasPrefetch)
                {
                    _hasPrefetch = false;
                    StartDmaWord(_holdingLatch, cycle, machine, audio);
                }
                else
                {
                    // An underrun holds the low DAC byte; it does not invent
                    // a bus transfer or replay the high byte.
                    if (audio.UsesHighByteRequest(_index)) RequestWord(cycle);
                    _nextSampleCycle = cycle + GetPeriodCycles(Period);
                }
                return;
            }

            if (_manualPlayback)
            {
                if (_irqCheck == 0)
                {
                    _irqCheck = (machine.Intreq & GetInterruptBit(_index)) != 0
                        ? 1
                        : -1;
                }

                if (_irqCheck < 0 && _holdingLatchWritten)
                {
                    StartManualWord(cycle, machine, audio);
                    return;
                }
            }

            EnterIdle();
        }

        internal LightweightPaulaChannelSnapshot CaptureSnapshot()
            => new(
                _index,
                Location,
                LengthWords,
                Period,
                Volume,
                CurrentSample,
                _holdingLatch,
                _holdingLatchWritten,
                _outputLatch,
                _hasOutputWord,
                _nextByteIsLow,
                _manualPlayback,
                State,
                _irqCheck,
                _nextSampleCycle,
                _manualIrqCheckCycle,
                _dmaEnabled, _currentAddress, _remainingWords, _delayedInterrupt,
                _hasPrefetch, _holdingLatch, _dmaInputCycle, _dmaOutputCycle,
                _dmaLoadCycle, _pendingAddress, _pendingData);

        private void StartManualWord(
            long cycle,
            LightweightA500Machine machine,
            LightweightPaulaAudio audio)
        {
            _outputLatch = _holdingLatch;
            _holdingLatchWritten = false;
            _hasOutputWord = true;
            _nextByteIsLow = true;
            _manualPlayback = true;
            _irqCheck = 0;
            _manualIrqCheckCycle = long.MaxValue;
            CurrentSample = unchecked((sbyte)(_outputLatch >> 8));
            audio.ApplyVolume(_index, _outputLatch);
            RecordState(LightweightPaulaAudioState.HighByte);
            _nextSampleCycle = cycle + GetPeriodCycles(Period);
            machine.LatchAudioInterrupt(_index, cycle);
        }

        private void BeginManualCompletionPeriod(
            LightweightA500Machine machine)
        {
            var periodCycles = GetPeriodCycles(Period);
            if (periodCycles <= LightweightClock.CpuCyclesPerColorClock)
            {
                _irqCheck = (machine.Intreq & GetInterruptBit(_index)) != 0
                    ? 1
                    : -1;
                _manualIrqCheckCycle = long.MaxValue;
                return;
            }

            RecordState(LightweightPaulaAudioState.ManualPeriodOne);
            _irqCheck = 0;
            _manualIrqCheckCycle =
                _nextSampleCycle - LightweightClock.CpuCyclesPerColorClock;
        }

        private void EnterIdle()
        {
            _streamInitialized = false;
            _hasPrefetch = false;
            _hasOutputWord = false;
            _nextByteIsLow = false;
            _manualPlayback = false;
            _irqCheck = 0;
            _manualIrqCheckCycle = long.MaxValue;
            _nextSampleCycle = long.MaxValue;
            RecordState(LightweightPaulaAudioState.Idle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetPeriodCycles(int period)
            => (long)(period == 0 ? 65_536 : period) *
                LightweightClock.CpuCyclesPerColorClock;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort GetInterruptBit(int channel)
        => (ushort)(0x0080 << channel);
}

internal enum LightweightPaulaAudioState
{
    NotRecorded = -1,
    Idle = 0,
    HighByte = 1,
    LowByte = 2,
    ManualPeriodOne = 3,
    DmaStartup = 4,
    DmaStartupData = 5
}

internal readonly record struct LightweightPaulaChannelSnapshot(
    int Index,
    uint Location,
    int LengthWords,
    int Period,
    int Volume,
    sbyte CurrentSample,
    ushort HoldingLatch,
    bool HoldingLatchWritten,
    ushort OutputLatch,
    bool HasOutputWord,
    bool NextByteIsLow,
    bool ManualPlayback,
    LightweightPaulaAudioState State,
    int IrqCheck,
    long NextSampleCycle,
    long ManualIrqCheckCycle,
    bool DmaEnabled,
    uint CurrentAddress,
    int RemainingWords,
    bool DelayedInterruptPending,
    bool HasPrefetch,
    ushort Prefetch,
    long DmaInputCycle,
    long DmaOutputCycle,
    long DmaLoadCycle,
    uint PendingAddress,
    ushort PendingData);
