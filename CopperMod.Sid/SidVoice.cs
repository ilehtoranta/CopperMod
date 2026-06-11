using System;

namespace CopperMod.Sid
{
    [HotPath]
    internal sealed class SidVoice
    {
        private const int Attack = 0;
        private const int Decay = 1;
        private const int Sustain = 2;
        private const int Release = 3;
        private const int RateCounterMask = 0x7FFF;
        private const uint PhaseMask = 0x00FFFFFF;
        private const uint PhaseMsb = 0x00800000;
        private const uint NoiseClockBit = 0x00080000;
        private const uint NoiseRegisterMask = 0x007FFFFF;
        private const uint NoiseResetValue = 0x7FFFF8;
        private static readonly int[] NoiseDacRegisterBits = { 22, 20, 16, 13, 11, 7, 4, 2 };
        private static readonly int[] NoiseDacWaveformBits = { 11, 10, 9, 8, 7, 6, 5, 4 };
        private static readonly int[] RatePeriods =
        {
            9, 32, 63, 95, 149, 220, 267, 313,
            392, 977, 1954, 3126, 3907, 11720, 19532, 31251
        };

        private uint _phase;
        private uint _noise = NoiseResetValue;
        private uint _noiseShiftLatch = NoiseResetValue;
        private int _envelopeCounter;
        private int _rateCounter;
        private int _exponentialCounter;
        private int _envelopeState = Release;
        private bool _previousGate;
        private bool _envelopeZeroHold = true;
        private bool _envelopeMaxHold;
        private bool _noiseResetHeld;
        private bool _noiseResetReleasePending;
        private byte _oscillatorReadLatch;
        private byte _oscillatorReadPipeline;
        private byte _control;
        private SidCycleTraceEvents _cycleEvents;

        public ushort Frequency { get; private set; }

        public ushort PulseWidth { get; private set; }

        public byte AttackDecay { get; private set; }

        public byte SustainRelease { get; private set; }

        public byte Control => _control;

        public uint Phase => _phase;

        public uint NoiseShiftRegister => _noise;

        public int EnvelopeCounter => _envelopeCounter;

        public int RateCounter => _rateCounter;

        public int ExponentialCounter => _exponentialCounter;

        public int EnvelopeState => _envelopeState;

        public bool SyncEnabled => (_control & 0x02) != 0;

        public bool TestEnabled => (_control & 0x08) != 0;

        public SidCycleTraceEvents CycleEvents => _cycleEvents;

        public void Reset()
        {
            _phase = 0;
            _noise = NoiseResetValue;
            _noiseShiftLatch = NoiseResetValue;
            _envelopeCounter = 0;
            _rateCounter = 0;
            _exponentialCounter = 0;
            _envelopeState = Release;
            _previousGate = false;
            _envelopeZeroHold = true;
            _envelopeMaxHold = false;
            _noiseResetHeld = false;
            _noiseResetReleasePending = false;
            _oscillatorReadLatch = 0;
            _oscillatorReadPipeline = 0;
            _control = 0;
            _cycleEvents = SidCycleTraceEvents.None;
            Frequency = 0;
            PulseWidth = 0;
            AttackDecay = 0;
            SustainRelease = 0;
        }

        public void BeginCycleTrace()
        {
            _cycleEvents = SidCycleTraceEvents.None;
        }

        public void MarkForwardedWrite()
        {
            _cycleEvents |= SidCycleTraceEvents.ForwardedWrite;
        }

        public void Write(int offset, byte value)
        {
            switch (offset)
            {
                case 0:
                    Frequency = (ushort)((Frequency & 0xFF00) | value);
                    break;
                case 1:
                    Frequency = (ushort)((Frequency & 0x00FF) | (value << 8));
                    break;
                case 2:
                    PulseWidth = (ushort)((PulseWidth & 0x0F00) | value);
                    break;
                case 3:
                    PulseWidth = (ushort)((PulseWidth & 0x00FF) | ((value & 0x0F) << 8));
                    break;
                case 4:
                    WriteControl(value);
                    break;
                case 5:
                    var oldAttackDecayPeriod = GetRatePeriod();
                    AttackDecay = value;
                    HandleEnvelopeRateWrite(oldAttackDecayPeriod, GetRatePeriod());
                    break;
                case 6:
                    var oldSustainReleasePeriod = GetRatePeriod();
                    SustainRelease = value;
                    HandleEnvelopeRateWrite(oldSustainReleasePeriod, GetRatePeriod());
                    break;
            }
        }

        public void ClockEnvelope()
        {
            _rateCounter = (_rateCounter + 1) & RateCounterMask;
            if (_rateCounter != GetRatePeriod())
            {
                return;
            }

            _rateCounter = 0;
            switch (_envelopeState)
            {
                case Attack:
                    if (!_envelopeMaxHold)
                    {
                        StepEnvelopeUp();
                    }

                    if (_envelopeCounter >= 0xFF)
                    {
                        _envelopeCounter = 0xFF;
                        _envelopeMaxHold = true;
                        _exponentialCounter = 0;
                        _envelopeState = Decay;
                    }

                    break;
                case Decay:
                case Sustain:
                    ClockDecaySustain();
                    break;
                case Release:
                    if (!_envelopeZeroHold && ClockExponentialCounter())
                    {
                        StepEnvelopeDown(holdAtZero: true);
                    }

                    break;
            }
        }

        private void ClockDecaySustain()
        {
            var sustain = GetSustainLevel();
            if (_envelopeCounter <= sustain)
            {
                _envelopeCounter = sustain;
                _envelopeZeroHold = sustain == 0;
                _exponentialCounter = 0;
                _envelopeState = Sustain;
                return;
            }

            _envelopeState = Decay;
            if (ClockExponentialCounter())
            {
                StepEnvelopeDown(holdAtZero: sustain == 0);
            }

            if (_envelopeCounter <= sustain)
            {
                _envelopeCounter = sustain;
                _envelopeZeroHold = sustain == 0;
                _exponentialCounter = 0;
                _envelopeState = Sustain;
            }
        }

        private void StepEnvelopeUp()
        {
            _envelopeCounter = (_envelopeCounter + 1) & 0xFF;
            _envelopeZeroHold = false;
            _envelopeMaxHold = _envelopeCounter == 0xFF;
            _cycleEvents |= SidCycleTraceEvents.EnvelopeStep;
        }

        private void StepEnvelopeDown(bool holdAtZero)
        {
            _envelopeCounter = (_envelopeCounter - 1) & 0xFF;
            _envelopeMaxHold = false;
            if (_envelopeCounter == 0 && holdAtZero)
            {
                _envelopeZeroHold = true;
            }

            _cycleEvents |= SidCycleTraceEvents.EnvelopeStep;
        }

        private void HandleEnvelopeRateWrite(int oldPeriod, int newPeriod)
        {
            if (_envelopeState == Release &&
                _envelopeCounter == 0 &&
                _envelopeZeroHold &&
                oldPeriod != newPeriod &&
                _rateCounter > newPeriod)
            {
                _envelopeZeroHold = false;
                _exponentialCounter = Math.Max(_exponentialCounter, GetExponentialPeriod(0) - 1);
            }
        }

        public void ClockOscillator()
        {
            if (TestEnabled)
            {
                ResetForTestBit();
                return;
            }

            _phase = (_phase + Frequency) & PhaseMask;
        }

        public void ResetOscillator()
        {
            _phase = 0;
            _cycleEvents |= SidCycleTraceEvents.SyncReset;
        }

        public void ClockNoise(bool oscillatorBit19Rising)
        {
            ApplyPendingNoiseResetRelease();
            if (!oscillatorBit19Rising)
            {
                return;
            }

            var feedback = ((_noise >> 22) ^ (_noise >> 17)) & 1;
            _noiseShiftLatch = ((_noise << 1) | feedback) & NoiseRegisterMask;
            _noise = _noiseShiftLatch;
            _cycleEvents |= SidCycleTraceEvents.NoiseShift;
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model)
        {
            return RenderOutputFast(syncSource, model);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: false, applyNoiseWriteback: true, out _);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform, out SidWaveformTrace trace)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: true, applyNoiseWriteback: true, out trace);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public double RenderOutputFast(SidVoice? syncSource, SidChipModel model)
        {
            var waveform = RenderWaveformFast(syncSource, model);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public byte ReadOscillator(SidVoice? syncSource, SidChipModel model)
        {
            return _oscillatorReadLatch;
        }

        public SidVoiceDebugState GetDebugState()
        {
            return new SidVoiceDebugState(
                _phase,
                _noise,
                GetNoiseDac(),
                _envelopeCounter,
                _rateCounter,
                _exponentialCounter,
                _envelopeState,
                _control);
        }

        public static bool MsbRising(uint previousPhase, uint currentPhase)
        {
            return (previousPhase & PhaseMsb) == 0 && (currentPhase & PhaseMsb) != 0;
        }

        public static bool NoiseClockRising(uint previousPhase, uint currentPhase)
        {
            return (previousPhase & NoiseClockBit) == 0 && (currentPhase & NoiseClockBit) != 0;
        }

        private void UpdateOscillatorReadLatch(uint waveformDac)
        {
            _oscillatorReadLatch = _oscillatorReadPipeline;
            _oscillatorReadPipeline = (byte)((waveformDac >> 4) & 0xFF);
        }

        private void WriteControl(byte value)
        {
            var wasTestEnabled = TestEnabled;
            var gate = (value & 0x01) != 0;
            if (gate && !_previousGate)
            {
                _envelopeState = Attack;
                _envelopeZeroHold = false;
                _envelopeMaxHold = _envelopeCounter == 0xFF;
                _cycleEvents |= SidCycleTraceEvents.GateRising;
            }
            else if (!gate && _previousGate)
            {
                _envelopeState = Release;
                _envelopeMaxHold = false;
                _envelopeZeroHold = _envelopeCounter == 0;
                _cycleEvents |= SidCycleTraceEvents.GateFalling;
            }

            _previousGate = gate;
            _control = value;
            var testEnabled = TestEnabled;
            if (testEnabled && !wasTestEnabled)
            {
                BeginNoiseReset();
            }
            else if (!testEnabled && wasTestEnabled)
            {
                ReleaseNoiseReset();
            }
        }

        private double RenderWaveform(
            SidVoice? syncSource,
            SidChipModel model,
            bool captureTrace,
            bool applyNoiseWriteback,
            out SidWaveformTrace trace)
        {
            var waveformMask = _control & 0xF0;
            var pulseDac = GetPulseDac();
            var pulseHigh = pulseDac != 0;
            var triangleDac = GetTriangleDac(
                syncSource,
                out var syncSourceMsb,
                out var ringModInverted,
                out var triangleInverted);
            if (waveformMask == 0)
            {
                UpdateOscillatorReadLatch(0);
                trace = captureTrace
                    ? new SidWaveformTrace(
                        0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false)
                    : default;
                return 0;
            }

            if (model == SidChipModel.Mos6581 && waveformMask == 0x50)
            {
                return RenderMos6581TrianglePulse(
                    triangleDac,
                    pulseDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    captureTrace,
                    out trace);
            }

            var outputs = 0;
            uint combinedDac = 0x0FFF;
            if ((waveformMask & 0x10) != 0)
            {
                combinedDac &= triangleDac;
                outputs++;
            }

            if ((waveformMask & 0x20) != 0)
            {
                combinedDac &= GetSawDac();
                outputs++;
            }

            if ((waveformMask & 0x40) != 0)
            {
                combinedDac &= pulseDac;
                outputs++;
            }

            var noiseSelected = (waveformMask & 0x80) != 0;
            if ((waveformMask & 0x80) != 0)
            {
                if (model != SidChipModel.Mos6581 && NoiseCombinedWithOtherWaveforms(waveformMask))
                {
                    _noise = 0;
                }

                if (_noise == 0)
                {
                    UpdateOscillatorReadLatch(0);
                    trace = captureTrace
                        ? new SidWaveformTrace(
                            0,
                            pulseHigh,
                            syncSourceMsb,
                            ringModInverted,
                            triangleInverted,
                            noiseUsesPostShiftRegister: false)
                        : default;
                    return 0;
                }

                combinedDac &= GetNoiseDac();
                outputs++;
            }

            if (outputs == 0)
            {
                UpdateOscillatorReadLatch(0);
                trace = captureTrace
                    ? new SidWaveformTrace(
                        0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false)
                    : default;
                return 0;
            }

            var selectorDac = SidAnalog.MapCombinedWaveformDac12(combinedDac, waveformMask, model);
            UpdateOscillatorReadLatch(selectorDac);
            trace = captureTrace
                ? new SidWaveformTrace(
                    selectorDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    noiseSelected)
                : default;
            ApplyMos6581NoiseWriteback(model, waveformMask, selectorDac, applyNoiseWriteback);
            return SidAnalog.UsesCombinedWaveformTable(waveformMask, model)
                ? SidAnalog.ConvertCombinedWaveformDac12(selectorDac, waveformMask, model)
                : SidAnalog.ConvertWaveformDac12(selectorDac, model) * SidAnalog.CombinedWaveformScale(outputs, model);
        }

        private double RenderMos6581TrianglePulse(
            uint triangleDac,
            uint pulseDac,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted,
            bool captureTrace,
            out SidWaveformTrace trace)
        {
            if (pulseDac == 0)
            {
                UpdateOscillatorReadLatch(0);
                trace = captureTrace
                    ? new SidWaveformTrace(
                        0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false)
                    : default;
                return SidAnalog.ConvertWaveformDac12(0, SidChipModel.Mos6581) *
                    SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581);
            }

            var saw = GetSawDac();
            var contentionMask = (_phase >> 10) & 0x0FFF;
            var contentionDac = (triangleDac & contentionMask) | ((triangleDac & saw) >> 1);
            if ((PulseWidth & 0x0FFF) != 0)
            {
                contentionDac ^= (saw & 0x20) == 0 ? 0u : 0x0FFFu;
            }

            var mos6581TrianglePulseBias = GetMos6581TrianglePulseBias();
            UpdateOscillatorReadLatch(contentionDac);
            trace = captureTrace
                ? new SidWaveformTrace(
                    contentionDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    noiseUsesPostShiftRegister: false)
                : default;
            return (SidAnalog.ConvertWaveformDac12(contentionDac, SidChipModel.Mos6581) *
                SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581)) + mos6581TrianglePulseBias;
        }

        private double RenderWaveformFast(SidVoice? syncSource, SidChipModel model)
        {
            var waveformMask = _control & 0xF0;
            switch (waveformMask)
            {
                case 0:
                    UpdateOscillatorReadLatch(0);
                    return 0;
                case 0x10:
                    var triangleDac = GetTriangleDac(syncSource, out _, out _, out _);
                    UpdateOscillatorReadLatch(triangleDac);
                    return SidAnalog.ConvertWaveformDac12(triangleDac, model);
                case 0x20:
                    var sawDac = GetSawDac();
                    UpdateOscillatorReadLatch(sawDac);
                    return SidAnalog.ConvertWaveformDac12(sawDac, model);
                case 0x40:
                    var pulseDac = GetPulseDac();
                    UpdateOscillatorReadLatch(pulseDac);
                    return SidAnalog.ConvertWaveformDac12(pulseDac, model);
                case 0x50 when model == SidChipModel.Mos6581:
                    return RenderMos6581TrianglePulseFast(syncSource);
                case 0x80:
                    if (_noise == 0)
                    {
                        UpdateOscillatorReadLatch(0);
                        return 0;
                    }

                    var noiseDac = GetNoiseDac();
                    UpdateOscillatorReadLatch(noiseDac);
                    return SidAnalog.ConvertWaveformDac12(noiseDac, model);
            }

            var outputs = 0;
            uint combinedDac = 0x0FFF;
            if ((waveformMask & 0x10) != 0)
            {
                combinedDac &= GetTriangleDac(syncSource, out _, out _, out _);
                outputs++;
            }

            if ((waveformMask & 0x20) != 0)
            {
                combinedDac &= GetSawDac();
                outputs++;
            }

            if ((waveformMask & 0x40) != 0)
            {
                combinedDac &= GetPulseDac();
                outputs++;
            }

            if ((waveformMask & 0x80) != 0)
            {
                if (model != SidChipModel.Mos6581 && NoiseCombinedWithOtherWaveforms(waveformMask))
                {
                    _noise = 0;
                }

                if (_noise == 0)
                {
                    UpdateOscillatorReadLatch(0);
                    return 0;
                }

                combinedDac &= GetNoiseDac();
                outputs++;
            }

            if (outputs == 0)
            {
                UpdateOscillatorReadLatch(0);
                return 0;
            }

            var selectorDac = SidAnalog.MapCombinedWaveformDac12(combinedDac, waveformMask, model);
            UpdateOscillatorReadLatch(selectorDac);
            ApplyMos6581NoiseWriteback(model, waveformMask, selectorDac, applyNoiseWriteback: true);
            return SidAnalog.UsesCombinedWaveformTable(waveformMask, model)
                ? SidAnalog.ConvertCombinedWaveformDac12(selectorDac, waveformMask, model)
                : SidAnalog.ConvertWaveformDac12(selectorDac, model) * SidAnalog.CombinedWaveformScale(outputs, model);
        }

        private double RenderMos6581TrianglePulseFast(SidVoice? syncSource)
        {
            var pulseDac = GetPulseDac();
            if (pulseDac == 0)
            {
                UpdateOscillatorReadLatch(0);
                return SidAnalog.ConvertWaveformDac12(0, SidChipModel.Mos6581) *
                    SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581);
            }

            var triangleDac = GetTriangleDac(syncSource, out _, out _, out _);
            var saw = GetSawDac();
            var contentionMask = (_phase >> 10) & 0x0FFF;
            var contentionDac = (triangleDac & contentionMask) | ((triangleDac & saw) >> 1);
            if ((PulseWidth & 0x0FFF) != 0)
            {
                contentionDac ^= (saw & 0x20) == 0 ? 0u : 0x0FFFu;
            }

            var mos6581TrianglePulseBias = GetMos6581TrianglePulseBias();
            UpdateOscillatorReadLatch(contentionDac);
            return (SidAnalog.ConvertWaveformDac12(contentionDac, SidChipModel.Mos6581) *
                SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581)) + mos6581TrianglePulseBias;
        }

        private double GetMos6581TrianglePulseBias()
        {
            return (_control & 0x01) == 0 ? -0.55 : -1.4;
        }

        private void ResetForTestBit()
        {
            _cycleEvents |= _phase == 0 ? SidCycleTraceEvents.TestBitHeld : SidCycleTraceEvents.TestBitReset;
            _phase = 0;
            _noise = NoiseResetValue;
            if (!_noiseResetHeld)
            {
                BeginNoiseReset();
            }
        }

        private void BeginNoiseReset()
        {
            _noiseResetHeld = true;
            _noiseResetReleasePending = false;
            _noise = NoiseResetValue;
            _noiseShiftLatch = NoiseResetValue;
        }

        private void ReleaseNoiseReset()
        {
            _noiseResetHeld = false;
            _noiseResetReleasePending = true;
        }

        private void ApplyPendingNoiseResetRelease()
        {
            if (!_noiseResetReleasePending)
            {
                return;
            }

            _noise = _noiseShiftLatch & NoiseRegisterMask;
            _noiseResetReleasePending = false;
        }

        private void ApplyMos6581NoiseWriteback(
            SidChipModel model,
            int waveformMask,
            uint waveformDac,
            bool applyNoiseWriteback)
        {
            if (!applyNoiseWriteback ||
                model != SidChipModel.Mos6581 ||
                !NoiseCombinedWithOtherWaveforms(waveformMask))
            {
                return;
            }

            if (_noiseResetHeld)
            {
                _noiseShiftLatch = ClearNoiseDacBitsFromWaveform(_noiseShiftLatch, waveformDac);
            }
            else
            {
                _noise = ClearNoiseDacBitsFromWaveform(_noise, waveformDac);
                _noiseShiftLatch = _noise;
            }

            _cycleEvents |= SidCycleTraceEvents.NoiseWriteback;
        }

        private static uint ClearNoiseDacBitsFromWaveform(uint noiseRegister, uint waveformDac)
        {
            for (var i = 0; i < NoiseDacRegisterBits.Length; i++)
            {
                if (((waveformDac >> NoiseDacWaveformBits[i]) & 1u) == 0)
                {
                    noiseRegister &= ~(1u << NoiseDacRegisterBits[i]);
                }
            }

            return noiseRegister & NoiseRegisterMask;
        }

        private static bool NoiseCombinedWithOtherWaveforms(int waveformMask)
        {
            return (waveformMask & 0x80) != 0 && (waveformMask & 0x70) != 0;
        }

        private uint GetTriangleDac(
            SidVoice? syncSource,
            out bool syncSourceMsb,
            out bool ringModInverted,
            out bool triangleInverted)
        {
            var phase = (_phase >> 11) & 0x0FFF;
            var invert = (_phase & PhaseMsb) != 0;
            syncSourceMsb = syncSource != null && (syncSource._phase & PhaseMsb) != 0;
            ringModInverted = (_control & 0x04) != 0 && syncSourceMsb;
            if (ringModInverted)
            {
                invert = !invert;
            }

            triangleInverted = invert;
            return invert ? phase ^ 0x0FFFu : phase;
        }

        private uint GetSawDac()
        {
            return (_phase >> 12) & 0x0FFF;
        }

        private uint GetPulseDac()
        {
            return ((_phase >> 12) & 0x0FFF) >= (PulseWidth & 0x0FFF) ? 0x0FFFu : 0u;
        }

        private uint GetNoiseDac()
        {
            var dac = 0u;
            dac |= ((_noise >> 22) & 1u) << 11;
            dac |= ((_noise >> 20) & 1u) << 10;
            dac |= ((_noise >> 16) & 1u) << 9;
            dac |= ((_noise >> 13) & 1u) << 8;
            dac |= ((_noise >> 11) & 1u) << 7;
            dac |= ((_noise >> 7) & 1u) << 6;
            dac |= ((_noise >> 4) & 1u) << 5;
            dac |= ((_noise >> 2) & 1u) << 4;
            return dac;
        }

        private int GetRatePeriod()
        {
            return _envelopeState switch
            {
                Attack => RatePeriods[(AttackDecay >> 4) & 0x0F],
                Decay => RatePeriods[AttackDecay & 0x0F],
                Sustain => RatePeriods[AttackDecay & 0x0F],
                Release => RatePeriods[SustainRelease & 0x0F],
                _ => int.MaxValue
            };
        }

        private int GetSustainLevel()
        {
            return ((SustainRelease >> 4) & 0x0F) * 0x11;
        }

        private bool ClockExponentialCounter()
        {
            _exponentialCounter++;
            if (_exponentialCounter < GetExponentialPeriod(_envelopeCounter))
            {
                return false;
            }

            _exponentialCounter = 0;
            return true;
        }

        private static int GetExponentialPeriod(int envelope)
        {
            return envelope switch
            {
                <= 0x06 => 30,
                <= 0x0E => 16,
                <= 0x1A => 8,
                <= 0x36 => 4,
                <= 0x5D => 2,
                _ => 1
            };
        }
    }
}
