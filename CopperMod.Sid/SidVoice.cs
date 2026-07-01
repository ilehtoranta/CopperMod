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
        private const uint PhaseResetValue = 0x00555555;
        private const uint PhaseMsb = 0x00800000;
        private const uint NoiseClockBit = 0x00080000;
        private const uint NoiseRegisterMask = 0x007FFFFF;
        private const uint NoiseResetValue = 0x7FFFF8;
        private const int Mos6581FloatingOutputTtlCycles = 54000;
        private const int Mos6581FloatingOutputFadeCycles = 1400;
        private const int Mos8580FloatingOutputTtlCycles = 800000;
        private const int Mos8580FloatingOutputFadeCycles = 50000;
        private const double Mos6581TrianglePulseContentionScale = 0.66;
        private const double Mos6581TrianglePulseRingContentionScale = 0.48;
        internal const int NoiseTestAllOnesDelayCycles = 0x4000;
        private static readonly int[] NoiseDacRegisterBits = { 22, 20, 16, 13, 11, 7, 4, 2 };
        private static readonly int[] NoiseDacWaveformBits = { 11, 10, 9, 8, 7, 6, 5, 4 };
        private const uint NoiseDacWaveformMask = 0x0FF0;
        private static readonly int[] RatePeriods =
        {
            9, 32, 63, 95, 149, 220, 267, 313,
            392, 977, 1954, 3126, 3907, 11720, 19532, 31251
        };

        private uint _phase;
        private uint _noise = NoiseResetValue;
        private uint _noiseShiftLatch = NoiseResetValue;
        private uint _floatingWaveformDac;
        private double _floatingWaveformOutput;
        private int _floatingWaveformTtl;
        private uint _pulseDac;
        private uint _pulseNextDac;
        private int _envelopeCounter;
        private int _rateCounter;
        private int _exponentialCounter;
        private int _envelopeState = Release;
        private bool _previousGate;
        private bool _envelopeZeroHold = true;
        private bool _envelopeMaxHold;
        private bool _envelopeCountingUp;
        private bool _envelopeCounterEnabled;
        private bool _envelopeDirectionChangePending;
        private bool _noiseResetHeld;
        private bool _noiseResetReleasePending;
        private bool _testBitResetJustAsserted;
        private int _noiseShiftNextPhase;
        private int _noiseShiftActivePhase;
        private int _noiseTestHeldCycles;
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
            _phase = PhaseResetValue;
            _noise = NoiseResetValue;
            _noiseShiftLatch = NoiseResetValue;
            _floatingWaveformDac = 0;
            _floatingWaveformOutput = 0.0;
            _floatingWaveformTtl = 0;
            _pulseDac = 0;
            _pulseNextDac = 0;
            _envelopeCounter = 0;
            _rateCounter = 0;
            _exponentialCounter = 0;
            _envelopeState = Release;
            _previousGate = false;
            _envelopeZeroHold = true;
            _envelopeMaxHold = false;
            _envelopeCountingUp = false;
            _envelopeCounterEnabled = false;
            _envelopeDirectionChangePending = false;
            _noiseResetHeld = false;
            _noiseResetReleasePending = false;
            _testBitResetJustAsserted = false;
            _noiseShiftNextPhase = 0;
            _noiseShiftActivePhase = 0;
            _noiseTestHeldCycles = 0;
            _oscillatorReadLatch = 0;
            _oscillatorReadPipeline = 0;
            _control = 0;
            _cycleEvents = SidCycleTraceEvents.None;
            Frequency = 0;
            PulseWidth = 0;
            AttackDecay = 0;
            SustainRelease = 0;
        }

        public void CopyStateFrom(SidVoice source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _phase = source._phase;
            _noise = source._noise;
            _noiseShiftLatch = source._noiseShiftLatch;
            _floatingWaveformDac = source._floatingWaveformDac;
            _floatingWaveformOutput = source._floatingWaveformOutput;
            _floatingWaveformTtl = source._floatingWaveformTtl;
            _pulseDac = source._pulseDac;
            _pulseNextDac = source._pulseNextDac;
            _envelopeCounter = source._envelopeCounter;
            _rateCounter = source._rateCounter;
            _exponentialCounter = source._exponentialCounter;
            _envelopeState = source._envelopeState;
            _previousGate = source._previousGate;
            _envelopeZeroHold = source._envelopeZeroHold;
            _envelopeMaxHold = source._envelopeMaxHold;
            _envelopeCountingUp = source._envelopeCountingUp;
            _envelopeCounterEnabled = source._envelopeCounterEnabled;
            _envelopeDirectionChangePending = source._envelopeDirectionChangePending;
            _noiseResetHeld = source._noiseResetHeld;
            _noiseResetReleasePending = source._noiseResetReleasePending;
            _testBitResetJustAsserted = source._testBitResetJustAsserted;
            _noiseShiftNextPhase = source._noiseShiftNextPhase;
            _noiseShiftActivePhase = source._noiseShiftActivePhase;
            _noiseTestHeldCycles = source._noiseTestHeldCycles;
            _oscillatorReadLatch = source._oscillatorReadLatch;
            _oscillatorReadPipeline = source._oscillatorReadPipeline;
            _control = source._control;
            _cycleEvents = source._cycleEvents;
            Frequency = source.Frequency;
            PulseWidth = source.PulseWidth;
            AttackDecay = source.AttackDecay;
            SustainRelease = source.SustainRelease;
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
            if (!ClockEnvelopeRateCounter())
            {
                return;
            }

            switch (_envelopeState)
            {
                case Attack:
                    ClockAttack();
                    break;
                case Decay:
                case Sustain:
                    ClockDecaySustain();
                    break;
                case Release:
                    if (!_envelopeZeroHold && ClockExponentialCounter())
                    {
                        StepEnvelope(up: false, holdAtTerminal: true);
                    }

                    break;
            }
        }

        private bool ClockEnvelopeRateCounter()
        {
            _rateCounter = (_rateCounter + 1) & RateCounterMask;
            if (_rateCounter != GetRatePeriod())
            {
                return false;
            }

            _rateCounter = 0;
            return true;
        }

        private void ClockAttack()
        {
            if (!_envelopeMaxHold)
            {
                StepEnvelope(up: true, holdAtTerminal: true);
            }

            if (_envelopeCounter >= 0xFF)
            {
                _envelopeCounter = 0xFF;
                _envelopeMaxHold = true;
                _exponentialCounter = 0;
                SetEnvelopePhase(Decay);
            }
        }

        private void ClockDecaySustain()
        {
            var sustain = GetSustainLevel();
            if (_envelopeCounter == sustain)
            {
                _envelopeZeroHold = _envelopeCounter == 0;
                _exponentialCounter = 0;
                _envelopeState = Sustain;
                _envelopeCounterEnabled = false;
                return;
            }

            _envelopeState = Decay;
            _envelopeCounterEnabled = true;
            if (ClockExponentialCounter())
            {
                StepEnvelope(up: false, holdAtTerminal: sustain == 0);
            }

            if (_envelopeCounter == sustain)
            {
                _envelopeZeroHold = _envelopeCounter == 0;
                _exponentialCounter = 0;
                _envelopeState = Sustain;
                _envelopeCounterEnabled = false;
            }
        }

        private void StepEnvelope(bool up, bool holdAtTerminal)
        {
            _envelopeCounter = up
                ? (_envelopeCounter + 1) & 0xFF
                : (_envelopeCounter - 1) & 0xFF;
            if (_envelopeDirectionChangePending)
            {
                _envelopeDirectionChangePending = false;
            }

            _envelopeCounterEnabled = true;
            if (up)
            {
                _envelopeZeroHold = false;
                _envelopeMaxHold = _envelopeCounter == 0xFF && holdAtTerminal;
                if (_envelopeMaxHold)
                {
                    _envelopeCounterEnabled = false;
                }
            }
            else
            {
                _envelopeMaxHold = false;
                _envelopeZeroHold = _envelopeCounter == 0 && holdAtTerminal;
                if (_envelopeZeroHold)
                {
                    _envelopeCounterEnabled = false;
                }
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
                _envelopeCounterEnabled = true;
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

        public void ClockPulse()
        {
            if (TestEnabled)
            {
                _pulseDac = 0x0FFF;
                _pulseNextDac = 0x0FFF;
                return;
            }

            _pulseDac = _pulseNextDac;
            _pulseNextDac = GetPulseComparatorDac();
        }

        public void ClockNoise(bool oscillatorBit19Rising)
        {
            ApplyPendingNoiseResetRelease();
            _noiseShiftActivePhase = 0;
            if (_noiseResetHeld)
            {
                ClearNoiseShiftState();
                return;
            }

            if (_noiseShiftNextPhase == 1)
            {
                _noiseShiftActivePhase = 1;
                var feedback = ((_noise >> 22) ^ (_noise >> 17)) & 1;
                _noiseShiftLatch = ((_noise << 1) | feedback) & NoiseRegisterMask;
                _noiseShiftNextPhase = 2;
            }
            else if (_noiseShiftNextPhase == 2)
            {
                _noiseShiftActivePhase = 2;
                _noise = _noiseShiftLatch & NoiseRegisterMask;
                _noiseShiftNextPhase = 0;
                _cycleEvents |= SidCycleTraceEvents.NoiseShift;
            }

            if (oscillatorBit19Rising && _noiseShiftNextPhase == 0)
            {
                _noiseShiftNextPhase = 1;
            }
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model)
        {
            return RenderOutputFast(syncSource, model);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: false, applyNoiseWriteback: true, out _);
            waveform = SidAnalog.ScaleWaveformOutput(waveform, _control & 0xF0, model);
            waveform = SidAnalog.ScalePulseWidthEdgeOutput(waveform, _control & 0xF0, PulseWidth, model);
            waveform = ScaleModulatedTriangleOutput(waveform, model);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform, out SidWaveformTrace trace)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: true, applyNoiseWriteback: true, out trace);
            waveform = SidAnalog.ScaleWaveformOutput(waveform, _control & 0xF0, model);
            waveform = SidAnalog.ScalePulseWidthEdgeOutput(waveform, _control & 0xF0, PulseWidth, model);
            waveform = ScaleModulatedTriangleOutput(waveform, model);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public double RenderOutputFast(SidVoice? syncSource, SidChipModel model)
        {
            var waveform = RenderWaveformFast(syncSource, model);
            waveform = SidAnalog.ScaleWaveformOutput(waveform, _control & 0xF0, model);
            waveform = SidAnalog.ScalePulseWidthEdgeOutput(waveform, _control & 0xF0, PulseWidth, model);
            waveform = ScaleModulatedTriangleOutput(waveform, model);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public byte ReadOscillator(SidVoice? syncSource, SidChipModel model)
        {
            return _oscillatorReadLatch;
        }

        public void RefreshRegisterObservableReadback(SidVoice? syncSource, SidChipModel model)
        {
            RefreshRegisterObservableWaveform(syncSource, model);
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
                var attackFromZeroHold = _envelopeState == Release &&
                    _envelopeCounter == 0 &&
                    _envelopeZeroHold;
                var holdAtMaximum = _envelopeCounter == 0xFF && !_envelopeCounterEnabled;
                SetEnvelopePhase(Attack);
                _envelopeZeroHold = false;
                _envelopeMaxHold = holdAtMaximum;
                _envelopeCounterEnabled = !_envelopeMaxHold;
                if (attackFromZeroHold)
                {
                    _rateCounter = 0;
                    _exponentialCounter = 0;
                }

                _cycleEvents |= SidCycleTraceEvents.GateRising;
            }
            else if (!gate && _previousGate)
            {
                var wasCounterEnabled = _envelopeCounterEnabled;
                SetEnvelopePhase(Release);
                _envelopeMaxHold = false;
                _envelopeZeroHold = _envelopeCounter == 0 && !wasCounterEnabled;
                _envelopeCounterEnabled = !_envelopeZeroHold;
                _cycleEvents |= SidCycleTraceEvents.GateFalling;
            }

            _previousGate = gate;
            _control = value;
            var testEnabled = TestEnabled;
            if (testEnabled && !wasTestEnabled)
            {
                _phase = 0;
                _testBitResetJustAsserted = true;
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
            var selection = SelectWaveform(syncSource, model, applyNoiseWriteback);
            trace = captureTrace ? selection.ToTrace() : default;
            return selection.Output;
        }

        private WaveformSelection SelectWaveform(SidVoice? syncSource, SidChipModel model, bool applyNoiseWriteback)
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
                return SelectFloatingWaveform(
                    model,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted);
            }

            if (model == SidChipModel.Mos6581 && waveformMask == 0x50)
            {
                return SelectMos6581TrianglePulse(
                    triangleDac,
                    pulseDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted);
            }

            var noiseSelected = (waveformMask & 0x80) != 0;
            var noiseDac = 0u;
            if (noiseSelected)
            {
                if (model != SidChipModel.Mos6581 && NoiseCombinedWithOtherWaveforms(waveformMask))
                {
                    _noise = 0;
                }

                if (_noise == 0)
                {
                    return CompleteWaveformSelection(
                        0,
                        0.0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false);
                }

                noiseDac = GetNoiseDac();
            }

            var selectorDac = SidAnalog.MapCombinedWaveformDac12(
                triangleDac,
                GetSawDac(),
                pulseDac,
                noiseDac,
                waveformMask,
                model,
                out var outputs);
            if (model == SidChipModel.Mos6581 &&
                waveformMask == 0x60 &&
                (_control & 0x04) != 0)
            {
                selectorDac = 0;
            }

            ApplyMos6581NoiseCombinedWriteback(model, waveformMask, selectorDac, applyNoiseWriteback);
            if (outputs == 0)
            {
                LatchFloatingWaveform(0, 0.0, model);
                return CompleteWaveformSelection(
                    0,
                    0.0,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    noiseUsesPostShiftRegister: false);
            }

            var output = SidAnalog.UsesCombinedWaveformTable(waveformMask, model)
                ? SidAnalog.ConvertCombinedWaveformDac12(selectorDac, waveformMask, model)
                : SidAnalog.ConvertWaveformDac12(selectorDac, model) * SidAnalog.CombinedWaveformScale(outputs, model);
            LatchFloatingWaveform(selectorDac, output, model);
            return CompleteWaveformSelection(
                selectorDac,
                output,
                pulseHigh,
                syncSourceMsb,
                ringModInverted,
                triangleInverted,
                noiseSelected);
        }

        private WaveformSelection SelectMos6581TrianglePulse(
            uint triangleDac,
            uint pulseDac,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted)
        {
            if (pulseDac == 0)
            {
                var mutedPulseOutput = (SidAnalog.ConvertWaveformDac12(0, SidChipModel.Mos6581) *
                    SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581)) + GetMos6581TrianglePulseBias();
                LatchFloatingWaveform(0, mutedPulseOutput, SidChipModel.Mos6581);
                return CompleteWaveformSelection(
                    0,
                    mutedPulseOutput,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    noiseUsesPostShiftRegister: false);
            }

            var output = (SidAnalog.ConvertWaveformDac12(triangleDac, SidChipModel.Mos6581) *
                GetMos6581TrianglePulseContentionScale()) + GetMos6581TrianglePulseBias();
            LatchFloatingWaveform(triangleDac, output, SidChipModel.Mos6581);
            return CompleteWaveformSelection(
                triangleDac,
                output,
                pulseHigh,
                syncSourceMsb,
                ringModInverted,
                triangleInverted,
                noiseUsesPostShiftRegister: false);
        }

        private WaveformSelection CompleteWaveformSelection(
            uint dac,
            double output,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted,
            bool noiseUsesPostShiftRegister)
        {
            UpdateOscillatorReadLatch(dac);
            return new WaveformSelection(
                dac,
                output,
                pulseHigh,
                syncSourceMsb,
                ringModInverted,
                triangleInverted,
                noiseUsesPostShiftRegister);
        }

        private WaveformSelection SelectFloatingWaveform(
            SidChipModel model,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted)
        {
            if (_floatingWaveformTtl > 0)
            {
                _floatingWaveformTtl--;
            }
            else if (_floatingWaveformDac != 0)
            {
                _floatingWaveformDac &= _floatingWaveformDac >> 1;
                _floatingWaveformOutput = _floatingWaveformDac == 0
                    ? 0.0
                    : SidAnalog.ConvertWaveformDac12(_floatingWaveformDac, model);
                if (_floatingWaveformDac != 0)
                {
                    _floatingWaveformTtl = FloatingOutputFadeCycles(model);
                }
            }

            return CompleteWaveformSelection(
                _floatingWaveformDac,
                _floatingWaveformOutput,
                pulseHigh,
                syncSourceMsb,
                ringModInverted,
                triangleInverted,
                noiseUsesPostShiftRegister: false);
        }

        private void LatchFloatingWaveform(uint dac, double output, SidChipModel model)
        {
            _floatingWaveformDac = dac & 0x0FFF;
            _floatingWaveformOutput = output;
            _floatingWaveformTtl = FloatingOutputTtlCycles(model);
        }

        private static int FloatingOutputTtlCycles(SidChipModel model)
            => model == SidChipModel.Mos8580 ? Mos8580FloatingOutputTtlCycles : Mos6581FloatingOutputTtlCycles;

        private static int FloatingOutputFadeCycles(SidChipModel model)
            => model == SidChipModel.Mos8580 ? Mos8580FloatingOutputFadeCycles : Mos6581FloatingOutputFadeCycles;

        private double ScaleModulatedTriangleOutput(double waveform, SidChipModel model)
        {
            if (model != SidChipModel.Mos6581 ||
                (_control & 0xF0) != 0x10 ||
                (_control & 0x04) == 0)
            {
                return waveform;
            }

            return waveform * (((_control & 0x02) != 0) ? 1.29 : 0.86);
        }

        private double RenderWaveformFast(SidVoice? syncSource, SidChipModel model)
        {
            return SelectWaveform(syncSource, model, applyNoiseWriteback: true).Output;
        }

        private void RefreshRegisterObservableWaveform(SidVoice? syncSource, SidChipModel model)
        {
            SelectWaveform(syncSource, model, applyNoiseWriteback: true);
        }

        private double GetMos6581TrianglePulseBias()
        {
            return (_control & 0x01) == 0 ? -0.55 : -1.4;
        }

        private double GetMos6581TrianglePulseContentionScale()
        {
            return (_control & 0x04) == 0
                ? Mos6581TrianglePulseContentionScale
                : Mos6581TrianglePulseRingContentionScale;
        }

        private readonly record struct WaveformSelection(
            uint Dac,
            double Output,
            bool PulseHigh,
            bool SyncSourceMsb,
            bool RingModInverted,
            bool TriangleInverted,
            bool NoiseUsesPostShiftRegister)
        {
            public SidWaveformTrace ToTrace()
                => new SidWaveformTrace(
                    Dac,
                    PulseHigh,
                    SyncSourceMsb,
                    RingModInverted,
                    TriangleInverted,
                    NoiseUsesPostShiftRegister);
        }

        private void ResetForTestBit()
        {
            _cycleEvents |= _phase == 0 ? SidCycleTraceEvents.TestBitHeld : SidCycleTraceEvents.TestBitReset;
            if (_testBitResetJustAsserted)
            {
                _cycleEvents &= ~SidCycleTraceEvents.TestBitHeld;
                _cycleEvents |= SidCycleTraceEvents.TestBitReset;
                _testBitResetJustAsserted = false;
            }

            _phase = 0;
            if (!_noiseResetHeld)
            {
                BeginNoiseReset();
            }

            if (_noiseTestHeldCycles < NoiseTestAllOnesDelayCycles)
            {
                _noiseTestHeldCycles++;
            }

            if (_noiseTestHeldCycles >= NoiseTestAllOnesDelayCycles)
            {
                _noise = NoiseRegisterMask;
                _noiseShiftLatch = NoiseRegisterMask;
            }
            else
            {
                _noise = NoiseResetValue;
                _noiseShiftLatch = NoiseResetValue;
            }
        }

        private void BeginNoiseReset()
        {
            _noiseResetHeld = true;
            _noiseResetReleasePending = false;
            _noiseTestHeldCycles = 0;
            _noise = NoiseResetValue;
            _noiseShiftLatch = NoiseResetValue;
            ClearNoiseShiftState();
        }

        private void ReleaseNoiseReset()
        {
            _noiseResetHeld = false;
            _noiseResetReleasePending = true;
            _noiseTestHeldCycles = 0;
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

        private void ApplyMos6581NoiseCombinedWriteback(
            SidChipModel model,
            int waveformMask,
            uint waveformDac,
            bool applyNoiseWriteback)
        {
            if (!applyNoiseWriteback ||
                model != SidChipModel.Mos6581 ||
                TestEnabled ||
                !NoiseCombinedWithOtherWaveforms(waveformMask))
            {
                return;
            }

            var pulledLowBits = (~waveformDac) & NoiseDacWaveformMask;
            if (_noiseShiftActivePhase == 1)
            {
                return;
            }

            if (pulledLowBits == 0)
            {
                return;
            }

            var updatedNoise = ClearNoiseDacBitsFromWaveform(_noise, pulledLowBits);
            if (updatedNoise == _noise)
            {
                return;
            }

            _noise = updatedNoise;
            _noiseShiftLatch = _noise;
            _cycleEvents |= SidCycleTraceEvents.NoiseWriteback;
        }

        private void ClearNoiseShiftState()
        {
            _noiseShiftNextPhase = 0;
            _noiseShiftActivePhase = 0;
        }

        private static uint ClearNoiseDacBitsFromWaveform(uint noiseRegister, uint pulledLowBits)
        {
            for (var i = 0; i < NoiseDacRegisterBits.Length; i++)
            {
                if ((pulledLowBits & (1u << NoiseDacWaveformBits[i])) != 0)
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
            syncSourceMsb = syncSource != null && (syncSource._phase & PhaseMsb) != 0;
            var ringModEnabled = (_control & 0x04) != 0;
            var accumulatorMsb = (_phase & PhaseMsb) != 0;
            var invert = ringModEnabled ? accumulatorMsb ^ syncSourceMsb : accumulatorMsb;
            ringModInverted = ringModEnabled && syncSourceMsb;
            triangleInverted = invert;
            var phase = ((_phase >> 12) & 0x07FF) << 1;
            return invert ? phase ^ 0x0FFEu : phase;
        }

        private uint GetSawDac()
        {
            return (_phase >> 12) & 0x0FFF;
        }

        private uint GetPulseDac()
        {
            return _pulseDac;
        }

        private uint GetPulseComparatorDac()
        {
            var pulseWidth = PulseWidth & 0x0FFF;
            return ((_phase >> 12) & 0x0FFF) >= pulseWidth ? 0x0FFFu : 0u;
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

        private void SetEnvelopePhase(int state)
        {
            var countingUp = state == Attack;
            if (_envelopeCountingUp != countingUp)
            {
                _envelopeDirectionChangePending = true;
            }

            _envelopeCountingUp = countingUp;
            _envelopeState = state;
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
