using System;

namespace CopperMod.Sid
{
    [HotPath]
    internal sealed class SidVoice
    {
        private const uint PhaseMask = 0x00FFFFFF;
        private const uint PhaseResetValue = 0x00555555;
        private const uint PhaseMsb = 0x00800000;
        private const uint NoiseClockBit = 0x00080000;
        private const uint NoiseRegisterMask = 0x007FFFFF;
        // Use the register convention before the two additional shifts implicit
        // in the legacy taps. Reset release clocks all ones once; TEST feedback
        // and combined-waveform writeback must operate in this same convention.
        private const uint NoiseResetValue = 0x7FFFFE;
        private const int Mos6581FloatingOutputTtlCycles = 54000;
        private const int Mos6581FloatingOutputFadeCycles = 1400;
        private const int Mos8580FloatingOutputTtlCycles = 800000;
        private const int Mos8580FloatingOutputFadeCycles = 50000;
        internal const int NoiseTestAllOnesDelayCycles = 50000 + 22 * 15000;
        private static readonly int[] NoiseDacRegisterBits = { 20, 18, 14, 11, 9, 5, 2, 0 };
        private static readonly int[] NoiseDacWaveformBits = { 11, 10, 9, 8, 7, 6, 5, 4 };
        private const uint NoiseDacWaveformMask = 0x0FF0;
        private uint _phase;
        private uint _noise = NoiseResetValue;
        private uint _noiseShiftLatch = NoiseResetValue;
        private uint _floatingWaveformDac;
        private double _floatingWaveformOutput;
        private int _floatingWaveformTtl;
        private uint _pulseDac;
        private uint _pulseNextDac;
        private SidEnvelopeGenerator _envelope;
        private SidChipModel _model = SidChipModel.Mos6581;
        private bool _previousGate;
        private bool _noiseResetHeld;
        private bool _noiseResetReleasePending;
        private bool _testBitResetJustAsserted;
        private int _noiseShiftNextPhase;
        private int _noiseShiftActivePhase;
        private int _noiseTestHeldCycles;
        private byte _oscillatorReadLatch;
        private uint _oscillatorReadPipeline;
        private uint _oscillatorReadCurrent;
        private byte _control;
        private SidCycleTraceEvents _cycleEvents;
        private SidEmulationProfile _sidEmulationProfile = SidEmulationProfile.Balanced;

        public SidVoice() => _envelope.Reset();

        public SidEnvelopeGenerator EnvelopeDebugState => _envelope;

        public byte ReadEnvelope() => _envelope.ReadLatch;

        public ushort Frequency { get; private set; }

        public ushort PulseWidth { get; private set; }

        public byte AttackDecay { get; private set; }

        public byte SustainRelease { get; private set; }

        public byte Control => _control;

        public uint Phase => _phase;

        public bool PhaseMsbSet => (_phase & PhaseMsb) != 0;

        public uint NoiseShiftRegister => _noise;

        public int EnvelopeCounter => _envelope.Counter;

        public int RateCounter => _envelope.RateCounter;

        public int ExponentialCounter => _envelope.DividerCounter;

        public int EnvelopeState => _envelope.State;

        public bool SyncEnabled => (_control & 0x02) != 0;

        public bool TestEnabled => (_control & 0x08) != 0;

        public SidCycleTraceEvents CycleEvents => _cycleEvents;

        public void ConfigureEmulationProfile(SidEmulationProfile sidEmulationProfile, SidChipModel model = SidChipModel.Mos6581)
        {
            _sidEmulationProfile = sidEmulationProfile;
            _model = model;
        }

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
            _envelope.Reset();
            _previousGate = false;
            _noiseResetHeld = false;
            _noiseResetReleasePending = false;
            _testBitResetJustAsserted = false;
            _noiseShiftNextPhase = 0;
            _noiseShiftActivePhase = 0;
            _noiseTestHeldCycles = 0;
            _oscillatorReadLatch = 0;
            _oscillatorReadPipeline = 0;
            _oscillatorReadCurrent = 0;
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
            _envelope = source._envelope;
            _model = source._model;
            _previousGate = source._previousGate;
            _noiseResetHeld = source._noiseResetHeld;
            _noiseResetReleasePending = source._noiseResetReleasePending;
            _testBitResetJustAsserted = source._testBitResetJustAsserted;
            _noiseShiftNextPhase = source._noiseShiftNextPhase;
            _noiseShiftActivePhase = source._noiseShiftActivePhase;
            _noiseTestHeldCycles = source._noiseTestHeldCycles;
            _oscillatorReadLatch = source._oscillatorReadLatch;
            _oscillatorReadPipeline = source._oscillatorReadPipeline;
            _oscillatorReadCurrent = source._oscillatorReadCurrent;
            _control = source._control;
            _cycleEvents = source._cycleEvents;
            _sidEmulationProfile = source._sidEmulationProfile;
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
                    AttackDecay = value;
                    _envelope.WriteAttackDecay(value);
                    break;
                case 6:
                    SustainRelease = value;
                    _envelope.WriteSustainRelease(value);
                    break;
            }
        }

        public void ClockEnvelope()
        {
            if (_envelope.Clock()) _cycleEvents |= SidCycleTraceEvents.EnvelopeStep;
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
            waveform = SidAnalog.ScaleWaveformOutput(waveform, _control & 0xF0, model, _sidEmulationProfile);
            return waveform * SidAnalog.ConvertEnvelope(_envelope.Counter, model, _sidEmulationProfile);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform, out SidWaveformTrace trace)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: true, applyNoiseWriteback: true, out trace);
            waveform = SidAnalog.ScaleWaveformOutput(waveform, _control & 0xF0, model, _sidEmulationProfile);
            return waveform * SidAnalog.ConvertEnvelope(_envelope.Counter, model, _sidEmulationProfile);
        }

        public double RenderOutputFast(SidVoice? syncSource, SidChipModel model)
        {
            var waveform = RenderWaveformFast(syncSource, model);
            waveform = SidAnalog.ScaleWaveformOutput(waveform, _control & 0xF0, model, _sidEmulationProfile);
            return waveform * SidAnalog.ConvertEnvelope(_envelope.Counter, model, _sidEmulationProfile);
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
                _envelope.Counter,
                _envelope.RateCounter,
                _envelope.DividerCounter,
                _envelope.State,
                _control)
            {
                EnvelopeTiming = _envelope,
                NoiseShiftPhase = _noiseShiftNextPhase,
                NoiseShiftLatch = _noiseShiftLatch,
                NoiseReleasePending = _noiseResetReleasePending,
                OscillatorReadLatch = _oscillatorReadLatch
            };
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
            _oscillatorReadLatch = _model == SidChipModel.Mos8580 && (_control & 0x30) != 0
                ? (byte)(_oscillatorReadCurrent >> 4) : (byte)(waveformDac >> 4);
        }

        private void WriteControl(byte value)
        {
            var wasTestEnabled = TestEnabled;
            var gate = (value & 0x01) != 0;
            if (gate != _previousGate)
            {
                _envelope.WriteGate(gate);
                _cycleEvents |= gate ? SidCycleTraceEvents.GateRising : SidCycleTraceEvents.GateFalling;
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
            _model = model;
            var waveformMask = _control & 0xF0;
            var pulseDac = GetPulseDac();
            var pulseHigh = pulseDac != 0;
            var triangleDac = GetTriangleDac(
                syncSource,
                out var syncSourceMsb,
                out var ringModInverted,
                out var triangleInverted);

            if (model == SidChipModel.Mos8580 && (waveformMask & 0x30) != 0)
            {
                // Only tri/saw passes through the extra 8580 read stage. Pulse
                // and noise gate its delayed value on the CURRENT cycle.
                var readDac = _oscillatorReadPipeline;
                if ((waveformMask & 0x40) != 0) readDac &= pulseDac;
                if ((waveformMask & 0x80) != 0) readDac &= GetNoiseDac();
                _oscillatorReadCurrent = _sidEmulationProfile == SidEmulationProfile.ReferenceMeasured
                    ? SidReferenceCombinedWaveformData.ApplyPulldown(model, waveformMask, readDac)
                    : readDac;
                var saw = GetSawDac();
                _oscillatorReadPipeline = (waveformMask & 0x30) switch
                {
                    0x10 => triangleDac,
                    0x20 => saw,
                    _ => _sidEmulationProfile == SidEmulationProfile.ReferenceMeasured
                        ? saw & ((saw << 1) & 0xfff) : saw & triangleDac
                };
            }

            if (waveformMask == 0)
            {
                return SelectFloatingWaveform(
                    model,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted);
            }

            if (model == SidChipModel.Mos6581 &&
                waveformMask == 0x50 &&
                _sidEmulationProfile == SidEmulationProfile.Balanced)
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
                out var outputs,
                _sidEmulationProfile);
            if (model == SidChipModel.Mos6581 &&
                (waveformMask & 0x20) != 0 &&
                (selectorDac & 0x0800) == 0)
            {
                // On a 6581 the shared combined-waveform bus can pull the
                // accumulator MSB low when saw is selected. This feeds the
                // digital state back into the next cycle, rather than being
                // merely an analog output-shaping effect.
                _phase &= ~PhaseMsb;
            }

            ApplyNoiseCombinedWriteback(model, waveformMask, selectorDac, applyNoiseWriteback);
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

            var output = SidAnalog.UsesCombinedWaveformTable(waveformMask, model, _sidEmulationProfile)
                ? SidAnalog.ConvertCombinedWaveformDac12(selectorDac, waveformMask, model, _sidEmulationProfile)
                : SidAnalog.ConvertWaveformDac12(selectorDac, model, _sidEmulationProfile) *
                    SidAnalog.CombinedWaveformScale(outputs, model, _sidEmulationProfile);
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
                var mutedPulseOutput = (SidAnalog.ConvertWaveformDac12(0, SidChipModel.Mos6581, _sidEmulationProfile) *
                    SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581, _sidEmulationProfile)) +
                    GetMos6581TrianglePulseBias();
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

            var output = (SidAnalog.ConvertWaveformDac12(triangleDac, SidChipModel.Mos6581, _sidEmulationProfile) *
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
                    : SidAnalog.ConvertWaveformDac12(_floatingWaveformDac, model, _sidEmulationProfile);
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
            return SidAnalog.TrianglePulseBias(
                (_control & 0x01) != 0,
                SidChipModel.Mos6581,
                _sidEmulationProfile);
        }

        private double GetMos6581TrianglePulseContentionScale()
        {
            return SidAnalog.TrianglePulseContentionScale(
                (_control & 0x04) != 0,
                SidChipModel.Mos6581,
                _sidEmulationProfile);
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

            var initialDelay = _model == SidChipModel.Mos8580 ? 986000 : 50000;
            var fadeDelay = _model == SidChipModel.Mos8580 ? 314300 : 15000;
            ++_noiseTestHeldCycles;
            if (_noiseTestHeldCycles >= initialDelay &&
                (_noiseTestHeldCycles - initialDelay) % fadeDelay == 0)
            {
                _noise |= 1u | (_noise << 1);
                _noise &= NoiseRegisterMask;
                _noiseShiftLatch = _noise;
            }
        }

        private void BeginNoiseReset()
        {
            _noiseResetHeld = true;
            _noiseResetReleasePending = false;
            _noiseTestHeldCycles = 0;
            _noiseShiftLatch = _noise;
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

            // TEST forces the bit-22 input of the feedback XOR high on release.
            _noise = ((_noiseShiftLatch << 1) | ((~_noiseShiftLatch >> 17) & 1)) & NoiseRegisterMask;
            _noiseShiftLatch = _noise;
            _cycleEvents |= SidCycleTraceEvents.NoiseShift;
            _noiseResetReleasePending = false;
        }

        private void ApplyNoiseCombinedWriteback(
            SidChipModel model,
            int waveformMask,
            uint waveformDac,
            bool applyNoiseWriteback)
        {
            if (!applyNoiseWriteback ||
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
            var ringModEnabled = (_control & 0x24) == 0x04;
            var accumulatorMsb = (_phase & PhaseMsb) != 0;
            var invert = accumulatorMsb ^ (ringModEnabled && !syncSourceMsb);
            ringModInverted = ringModEnabled && !syncSourceMsb;
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
            dac |= ((_noise >> 20) & 1u) << 11;
            dac |= ((_noise >> 18) & 1u) << 10;
            dac |= ((_noise >> 14) & 1u) << 9;
            dac |= ((_noise >> 11) & 1u) << 8;
            dac |= ((_noise >> 9) & 1u) << 7;
            dac |= ((_noise >> 5) & 1u) << 6;
            dac |= ((_noise >> 2) & 1u) << 5;
            dac |= (_noise & 1u) << 4;
            return dac;
        }

    }
}
