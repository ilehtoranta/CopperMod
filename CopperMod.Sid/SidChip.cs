using System;
using System.Numerics;

namespace CopperMod.Sid
{
    internal sealed class SidChip
    {
        internal const long OpenBusDecayCycles = 0x2000;

        private readonly SidVoice[] _voices = { new SidVoice(), new SidVoice(), new SidVoice() };
        private readonly byte[] _registers = new byte[32];
        private readonly byte[] _forwardedRegisters = new byte[32];
        private readonly byte[] _pendingRegisters = new byte[32];
        private readonly SidFilterProfileDefinition _filterProfile;
        private readonly int _cpuCyclesPerSecond;
        private readonly double _outputLowPassAlpha;
        private readonly double _filterInputGain;
        private readonly double _voiceMixGain;
        private readonly double _filterLowPassGain;
        private readonly double _filterBandPassGain;
        private readonly double _filterHighPassGain;
        private readonly double _volumeRegisterTransientGain;
        private readonly double _volumeRegisterTransientLimit;
        private readonly double _volumeRegisterTransientSlew;
        private readonly double _volumeRegisterTransientDecay;
        private readonly SidMos6581AnalogFilter? _analog6581Filter;
        private readonly double _analogOutputLowPassAlpha;
        private uint _pendingRegisterBits;
        private double _filterIntegrator1;
        private double _filterIntegrator2;
        private double _filterG;
        private double _filterDamping;
        private double _filterDenominator = 1.0;
        private double _filterOutputGain;
        private int _filterRouting;
        private int _filterMode;
        private int _filterCutoffRegister;
        private int _filterResonanceNibble;
        private double _filterCutoffHz;
        private double _lastLowPass;
        private double _lastBandPass;
        private double _lastHighPass;
        private double _filterVoiceLeakageGain;
        private bool _filterCoefficientsDirty = true;
        private bool _voice3Muted;
        private double _masterVolume;
        private double _volumeOffset;
        private double _volumeRegisterTransient;
        private double _volumeRegisterTransientTarget;
        private double _outputLowPassState;
        private double _analogOutputLowPassVoltage;
        private double _lastOutput;
        private byte _busValue;
        private long _busLastDrivenCycle;
        private long _cycle;

        public SidChip(
            SidChipModel model,
            ushort baseAddress,
            int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond,
            SidFilterProfileId filterProfile = SidFilterProfileId.Auto,
            SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            Model = model == SidChipModel.Mos8580 ? SidChipModel.Mos8580 : SidChipModel.Mos6581;
            BaseAddress = baseAddress;
            SidEmulationProfile = sidEmulationProfile;
            foreach (var voice in _voices)
            {
                voice.ConfigureEmulationProfile(sidEmulationProfile);
            }
            _cpuCyclesPerSecond = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            _filterProfile = SidFilterProfileDefinition.Resolve(Model, filterProfile, SidEmulationProfile);
            _outputLowPassAlpha = 1.0 - Math.Exp(-2.0 * Math.PI * SidAnalog.OutputLowPassCutoffHz(Model, SidEmulationProfile) / _cpuCyclesPerSecond);
            _filterInputGain = _filterProfile.FilterInputGain;
            _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(0);
            _voiceMixGain = SidAnalog.VoiceMixGain(Model, SidEmulationProfile);
            _filterLowPassGain = _filterProfile.LowPassGain;
            _filterBandPassGain = _filterProfile.BandPassGain;
            _filterHighPassGain = _filterProfile.HighPassGain;
            _filterOutputGain = _filterProfile.MapFilterOutputGain(0, 0);
            _volumeRegisterTransientGain = SidAnalog.VolumeRegisterTransientGain(Model, SidEmulationProfile);
            _volumeRegisterTransientLimit = SidAnalog.VolumeRegisterTransientLimit(Model, SidEmulationProfile);
            _volumeRegisterTransientSlew = SidAnalog.VolumeRegisterTransientSlew(Model, _cpuCyclesPerSecond, SidEmulationProfile);
            _volumeRegisterTransientDecay = SidAnalog.VolumeRegisterTransientDecay(Model, _cpuCyclesPerSecond, SidEmulationProfile);
            _masterVolume = SidAnalog.ConvertVolume(0, Model, SidEmulationProfile);
            _volumeOffset = SidAnalog.VolumeOffset(0, Model, SidEmulationProfile);
            _analog6581Filter = _filterProfile.UsesAnalog6581Filter
                ? new SidMos6581AnalogFilter(_filterProfile, _cpuCyclesPerSecond)
                : null;
            _analogOutputLowPassAlpha = _analog6581Filter == null
                ? _outputLowPassAlpha
                : 1.0 - Math.Exp(
                    -2.0 *
                    Math.PI *
                    SidAnalog.ApplyOutputLowPassCalibration(Model, SidEmulationProfile, _analog6581Filter.OutputLowPassCutoffHz) /
                    _cpuCyclesPerSecond);
            _analogOutputLowPassVoltage = _analog6581Filter?.OutputRestVoltage ?? 0.0;
        }

        public SidChipModel Model { get; }

        public ushort BaseAddress { get; }

        public SidEmulationProfile SidEmulationProfile { get; }

        public byte[] Registers => _registers;

        public SidChipDebugState DebugState => CreateDebugState();

        public int MutedVoicesMask { get; set; }

        public int TraceChipIndex { get; set; }

        public SidCycleTrace? Trace { get; set; }

        internal SidOutputStageTrace? OutputStageTrace { get; set; }

        public void Reset()
        {
            Array.Clear(_registers);
            Array.Clear(_forwardedRegisters);
            Array.Clear(_pendingRegisters);
            _pendingRegisterBits = 0;
            foreach (var voice in _voices)
            {
                voice.Reset();
            }

            _filterIntegrator1 = 0;
            _filterIntegrator2 = 0;
            _filterG = 0;
            _filterDamping = 0;
            _filterDenominator = 1.0;
            _filterOutputGain = _filterProfile.MapFilterOutputGain(0, 0);
            _filterRouting = 0;
            _filterMode = 0;
            _filterCutoffRegister = 0;
            _filterResonanceNibble = 0;
            _filterCutoffHz = 0;
            _lastLowPass = 0;
            _lastBandPass = 0;
            _lastHighPass = 0;
            _analog6581Filter?.Reset();
            _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(0);
            _filterCoefficientsDirty = true;
            _voice3Muted = false;
            _masterVolume = SidAnalog.ConvertVolume(0, Model, SidEmulationProfile);
            _volumeOffset = SidAnalog.VolumeOffset(0, Model, SidEmulationProfile);
            _volumeRegisterTransient = 0;
            _volumeRegisterTransientTarget = 0;
            _outputLowPassState = 0;
            _analogOutputLowPassVoltage = _analog6581Filter?.OutputRestVoltage ?? 0.0;
            _lastOutput = 0;
            _busValue = 0;
            _busLastDrivenCycle = 0;
            _cycle = 0;
        }

        public void CopyStateFrom(SidChip source)
        {
            ArgumentNullException.ThrowIfNull(source);
            Array.Copy(source._registers, _registers, _registers.Length);
            Array.Copy(source._forwardedRegisters, _forwardedRegisters, _forwardedRegisters.Length);
            Array.Copy(source._pendingRegisters, _pendingRegisters, _pendingRegisters.Length);
            _pendingRegisterBits = source._pendingRegisterBits;
            for (var i = 0; i < _voices.Length; i++)
            {
                _voices[i].CopyStateFrom(source._voices[i]);
            }

            _filterIntegrator1 = source._filterIntegrator1;
            _filterIntegrator2 = source._filterIntegrator2;
            _filterG = source._filterG;
            _filterDamping = source._filterDamping;
            _filterDenominator = source._filterDenominator;
            _filterOutputGain = source._filterOutputGain;
            _filterRouting = source._filterRouting;
            _filterMode = source._filterMode;
            _filterCutoffRegister = source._filterCutoffRegister;
            _filterResonanceNibble = source._filterResonanceNibble;
            _filterCutoffHz = source._filterCutoffHz;
            _lastLowPass = source._lastLowPass;
            _lastBandPass = source._lastBandPass;
            _lastHighPass = source._lastHighPass;
            _filterVoiceLeakageGain = source._filterVoiceLeakageGain;
            _filterCoefficientsDirty = source._filterCoefficientsDirty;
            _voice3Muted = source._voice3Muted;
            _masterVolume = source._masterVolume;
            _volumeOffset = source._volumeOffset;
            _volumeRegisterTransient = source._volumeRegisterTransient;
            _volumeRegisterTransientTarget = source._volumeRegisterTransientTarget;
            _outputLowPassState = source._outputLowPassState;
            _analogOutputLowPassVoltage = source._analogOutputLowPassVoltage;
            _lastOutput = source._lastOutput;
            _busValue = source._busValue;
            _busLastDrivenCycle = source._busLastDrivenCycle;
            _cycle = source._cycle;
            if (_analog6581Filter != null && source._analog6581Filter != null)
            {
                _analog6581Filter.CopyStateFrom(source._analog6581Filter);
            }
        }

        public void Write(byte register, byte value)
        {
            Write(register, value, _cycle);
        }

        public void Write(byte register, byte value, long cycle)
        {
            register = (byte)(register & 0x1F);
            _registers[register] = value;
            _pendingRegisters[register] = value;
            _pendingRegisterBits |= 1u << register;
            DriveOpenBus(value, cycle);
        }

        public void WriteBusValueOnly(byte value)
        {
            WriteBusValueOnly(value, _cycle);
        }

        public void WriteBusValueOnly(byte value, long cycle)
        {
            DriveOpenBus(value, cycle);
        }

        public byte Read(byte register)
        {
            return Read(register, _cycle);
        }

        public byte Read(byte register, long cycle)
        {
            register = (byte)(register & 0x1F);
            ApplyOpenBusDecay(cycle);
            var value = register switch
            {
                0x19 => (byte)0xFF,
                0x1A => (byte)0xFF,
                0x1B => _voices[2].ReadOscillator(_voices[1], Model),
                0x1C => (byte)_voices[2].EnvelopeCounter,
                _ => _busValue
            };
            DriveOpenBus(value, cycle);
            return value;
        }

        private void DriveOpenBus(byte value, long cycle)
        {
            _busValue = value;
            _busLastDrivenCycle = cycle;
        }

        private void ApplyOpenBusDecay(long cycle)
        {
            if (_busValue == 0 ||
                cycle < _busLastDrivenCycle ||
                cycle - _busLastDrivenCycle < OpenBusDecayCycles)
            {
                return;
            }

            _busValue = 0;
            _busLastDrivenCycle = cycle;
        }

        [HotPath]
        public void AdvanceRegisterObservable(long firstCycle, long cycles)
        {
            for (var i = 0L; i < cycles; i++)
            {
                _cycle = firstCycle + i;
                ClockRegisterObservableCycle();
            }
        }

        [HotPath]
        public double Render(long cycles, double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            if (voiceOutputs == null && Trace == null)
            {
                for (var i = 0L; i < cycles; i++)
                {
                    _cycle++;
                    _lastOutput = ClockOneCycleFast();
                }

                return _lastOutput;
            }

            var voice1 = 0.0;
            var voice2 = 0.0;
            var voice3 = 0.0;
            for (var i = 0L; i < cycles; i++)
            {
                _lastOutput = ClockOneCycle(++_cycle, out voice1, out voice2, out voice3);
            }

            if (voiceOutputs != null)
            {
                voiceOutputs[voiceOffset] = voice1;
                voiceOutputs[voiceOffset + 1] = voice2;
                voiceOutputs[voiceOffset + 2] = voice3;
            }

            return _lastOutput;
        }

        [HotPath]
        public double RenderOneCycle(double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            return RenderOneCycle(-1, voiceOutputs, voiceOffset);
        }

        [HotPath]
        public double RenderOneCycle(long cycle, double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            if (cycle < 0)
            {
                cycle = ++_cycle;
            }
            else
            {
                _cycle = cycle;
            }

            if (voiceOutputs == null && Trace == null)
            {
                _lastOutput = ClockOneCycleFast();
                return _lastOutput;
            }

            _lastOutput = ClockOneCycle(cycle, out var voice1, out var voice2, out var voice3);
            if (voiceOutputs != null)
            {
                voiceOutputs[voiceOffset] = voice1;
                voiceOutputs[voiceOffset + 1] = voice2;
                voiceOutputs[voiceOffset + 2] = voice3;
            }

            return _lastOutput;
        }

        [HotPath]
        public double RenderAndSumFast(long firstCycle, long cycles)
        {
            var sum = 0.0;
            for (var i = 0L; i < cycles; i++)
            {
                _cycle = firstCycle + i;
                _lastOutput = ClockOneCycleFast();
                sum += _lastOutput;
            }

            return sum;
        }

        [HotPath]
        private double ClockOneCycle(long cycle, out double voice1, out double voice2, out double voice3)
        {
            var chipVoice1 = _voices[0];
            var chipVoice2 = _voices[1];
            var chipVoice3 = _voices[2];
            var trace = Trace;
            var tracing = trace != null;
            var before1 = default(SidVoiceDebugState);
            var before2 = default(SidVoiceDebugState);
            var before3 = default(SidVoiceDebugState);
            if (tracing)
            {
                chipVoice1.BeginCycleTrace();
                chipVoice2.BeginCycleTrace();
                chipVoice3.BeginCycleTrace();
                before1 = chipVoice1.GetDebugState();
                before2 = chipVoice2.GetDebugState();
                before3 = chipVoice3.GetDebugState();
            }

            CommitPendingRegisters();

            chipVoice1.ClockEnvelope();
            chipVoice2.ClockEnvelope();
            chipVoice3.ClockEnvelope();

            var previousPhase1 = chipVoice1.Phase;
            var previousPhase2 = chipVoice2.Phase;
            var previousPhase3 = chipVoice3.Phase;
            chipVoice1.ClockOscillator();
            chipVoice2.ClockOscillator();
            chipVoice3.ClockOscillator();

            var advancedPhase1 = chipVoice1.Phase;
            var advancedPhase2 = chipVoice2.Phase;
            var advancedPhase3 = chipVoice3.Phase;
            var voice1MsbRising = SidVoice.MsbRising(previousPhase1, advancedPhase1);
            var voice2MsbRising = SidVoice.MsbRising(previousPhase2, advancedPhase2);
            var voice3MsbRising = SidVoice.MsbRising(previousPhase3, advancedPhase3);
            chipVoice1.ClockPulse();
            chipVoice2.ClockPulse();
            chipVoice3.ClockPulse();

            chipVoice1.ClockNoise(SidVoice.NoiseClockRising(previousPhase1, chipVoice1.Phase));
            chipVoice2.ClockNoise(SidVoice.NoiseClockRising(previousPhase2, chipVoice2.Phase));
            chipVoice3.ClockNoise(SidVoice.NoiseClockRising(previousPhase3, chipVoice3.Phase));

            var model = Model;
            if (tracing)
            {
                voice1 = chipVoice1.RenderOutput(chipVoice3, model, out var waveform1, out var waveformTrace1);
                voice2 = chipVoice2.RenderOutput(chipVoice1, model, out var waveform2, out var waveformTrace2);
                voice3 = chipVoice3.RenderOutput(chipVoice2, model, out var waveform3, out var waveformTrace3);
                var output = Mix(voice1, voice2, voice3);
                ApplyHardSync(
                    chipVoice1,
                    chipVoice2,
                    chipVoice3,
                    voice1MsbRising,
                    voice2MsbRising,
                    voice3MsbRising);
                TraceVoice(cycle, 0, before1, waveformTrace1, waveform1, voice1);
                TraceVoice(cycle, 1, before2, waveformTrace2, waveform2, voice2);
                TraceVoice(cycle, 2, before3, waveformTrace3, waveform3, voice3);
                return output;
            }

            voice1 = chipVoice1.RenderOutputFast(chipVoice3, model);
            voice2 = chipVoice2.RenderOutputFast(chipVoice1, model);
            voice3 = chipVoice3.RenderOutputFast(chipVoice2, model);
            var fastOutput = Mix(voice1, voice2, voice3);
            ApplyHardSync(
                chipVoice1,
                chipVoice2,
                chipVoice3,
                voice1MsbRising,
                voice2MsbRising,
                voice3MsbRising);
            return fastOutput;
        }

        [HotPath]
        private double ClockOneCycleFast()
        {
            var chipVoice1 = _voices[0];
            var chipVoice2 = _voices[1];
            var chipVoice3 = _voices[2];

            CommitPendingRegisters();

            chipVoice1.ClockEnvelope();
            chipVoice2.ClockEnvelope();
            chipVoice3.ClockEnvelope();

            var previousPhase1 = chipVoice1.Phase;
            var previousPhase2 = chipVoice2.Phase;
            var previousPhase3 = chipVoice3.Phase;
            chipVoice1.ClockOscillator();
            chipVoice2.ClockOscillator();
            chipVoice3.ClockOscillator();

            var advancedPhase1 = chipVoice1.Phase;
            var advancedPhase2 = chipVoice2.Phase;
            var advancedPhase3 = chipVoice3.Phase;
            var voice1MsbRising = SidVoice.MsbRising(previousPhase1, advancedPhase1);
            var voice2MsbRising = SidVoice.MsbRising(previousPhase2, advancedPhase2);
            var voice3MsbRising = SidVoice.MsbRising(previousPhase3, advancedPhase3);
            chipVoice1.ClockPulse();
            chipVoice2.ClockPulse();
            chipVoice3.ClockPulse();

            chipVoice1.ClockNoise(SidVoice.NoiseClockRising(previousPhase1, chipVoice1.Phase));
            chipVoice2.ClockNoise(SidVoice.NoiseClockRising(previousPhase2, chipVoice2.Phase));
            chipVoice3.ClockNoise(SidVoice.NoiseClockRising(previousPhase3, chipVoice3.Phase));

            var model = Model;
            var voice1 = chipVoice1.RenderOutputFast(chipVoice3, model);
            var voice2 = chipVoice2.RenderOutputFast(chipVoice1, model);
            var voice3 = chipVoice3.RenderOutputFast(chipVoice2, model);
            var output = Mix(voice1, voice2, voice3);
            ApplyHardSync(
                chipVoice1,
                chipVoice2,
                chipVoice3,
                voice1MsbRising,
                voice2MsbRising,
                voice3MsbRising);
            return output;
        }

        [HotPath]
        private void ClockRegisterObservableCycle()
        {
            var chipVoice1 = _voices[0];
            var chipVoice2 = _voices[1];
            var chipVoice3 = _voices[2];

            CommitPendingRegisters();

            chipVoice1.ClockEnvelope();
            chipVoice2.ClockEnvelope();
            chipVoice3.ClockEnvelope();

            var previousPhase1 = chipVoice1.Phase;
            var previousPhase2 = chipVoice2.Phase;
            var previousPhase3 = chipVoice3.Phase;
            chipVoice1.ClockOscillator();
            chipVoice2.ClockOscillator();
            chipVoice3.ClockOscillator();

            var advancedPhase1 = chipVoice1.Phase;
            var advancedPhase2 = chipVoice2.Phase;
            var advancedPhase3 = chipVoice3.Phase;
            var voice1MsbRising = SidVoice.MsbRising(previousPhase1, advancedPhase1);
            var voice2MsbRising = SidVoice.MsbRising(previousPhase2, advancedPhase2);
            var voice3MsbRising = SidVoice.MsbRising(previousPhase3, advancedPhase3);
            chipVoice1.ClockPulse();
            chipVoice2.ClockPulse();
            chipVoice3.ClockPulse();

            chipVoice1.ClockNoise(SidVoice.NoiseClockRising(previousPhase1, chipVoice1.Phase));
            chipVoice2.ClockNoise(SidVoice.NoiseClockRising(previousPhase2, chipVoice2.Phase));
            chipVoice3.ClockNoise(SidVoice.NoiseClockRising(previousPhase3, chipVoice3.Phase));

            var model = Model;
            chipVoice1.RefreshRegisterObservableReadback(chipVoice3, model);
            chipVoice2.RefreshRegisterObservableReadback(chipVoice1, model);
            chipVoice3.RefreshRegisterObservableReadback(chipVoice2, model);
            ApplyHardSync(
                chipVoice1,
                chipVoice2,
                chipVoice3,
                voice1MsbRising,
                voice2MsbRising,
                voice3MsbRising);
        }

        [HotPath]
        private static void ApplyHardSync(
            SidVoice voice1,
            SidVoice voice2,
            SidVoice voice3,
            bool voice1MsbRising,
            bool voice2MsbRising,
            bool voice3MsbRising)
        {
            // Combined-waveform pulldown can clear a 6581 accumulator MSB and
            // suppress the sync edge before destinations are evaluated.
            voice1MsbRising &= voice1.PhaseMsbSet;
            voice2MsbRising &= voice2.PhaseMsbSet;
            voice3MsbRising &= voice3.PhaseMsbSet;

            // A source that is itself synchronized on this cycle does not pass
            // its transient MSB edge on to the next oscillator in the ring.
            var resetVoice1 = voice1.SyncEnabled && voice3MsbRising &&
                !(voice3.SyncEnabled && voice2MsbRising);
            var resetVoice2 = voice2.SyncEnabled && voice1MsbRising &&
                !(voice1.SyncEnabled && voice3MsbRising);
            var resetVoice3 = voice3.SyncEnabled && voice2MsbRising &&
                !(voice2.SyncEnabled && voice1MsbRising);

            if (resetVoice1)
            {
                voice1.ResetOscillator();
            }

            if (resetVoice2)
            {
                voice2.ResetOscillator();
            }

            if (resetVoice3)
            {
                voice3.ResetOscillator();
            }
        }

        [HotPath]
        private void CommitPendingRegisters()
        {
            var pending = _pendingRegisterBits;
            _pendingRegisterBits = 0;
            var filterCoefficientsDirty = false;
            while (pending != 0)
            {
                var register = BitOperations.TrailingZeroCount(pending);
                pending &= ~(1u << register);
                var value = _pendingRegisters[register];
                var previousValue = _forwardedRegisters[register];
                _forwardedRegisters[register] = value;
                if (register < 21)
                {
                    var voice = _voices[register / 7];
                    voice.MarkForwardedWrite();
                    voice.Write(register % 7, value);
                }
                else if (register >= 0x15 && register <= 0x18)
                {
                    if (register == 0x15 || register == 0x16)
                    {
                        filterCoefficientsDirty = true;
                    }
                    else if (register == 0x17)
                    {
                        var previousRouting = _filterRouting;
                        // Keep the EXT IN routing bit even though the external source is
                        // intentionally zero until a public injection path is introduced.
                        _filterRouting = value & 0x0F;
                        AddRegisterTransient(SidAnalog.FilterRoutingTransient(
                            previousRouting,
                            _filterRouting,
                            _filterMode,
                            Model,
                            SidEmulationProfile));
                        filterCoefficientsDirty = true;
                    }
                    else
                    {
                        var volume = value & 0x0F;
                        var nextVolumeOffset = SidAnalog.VolumeOffset(value, Model, SidEmulationProfile);
                        var genericStepDelta = nextVolumeOffset - _volumeOffset;
                        if (Model == SidChipModel.Mos6581)
                        {
                            genericStepDelta = -genericStepDelta;
                        }

                        var genericStepImpulse = genericStepDelta * _volumeRegisterTransientGain;
                        var matrixImpulse = SidAnalog.D418TransitionTransient(
                            previousValue,
                            value,
                            Model,
                            SidEmulationProfile);
                        var filterModeImpulse = SidAnalog.FilterModeTransient(
                            previousValue,
                            value,
                            Model,
                            SidEmulationProfile);
                        AddRegisterTransient(genericStepImpulse);
                        AddRegisterTransient(matrixImpulse);
                        AddRegisterTransient(filterModeImpulse);
                        var outputStageTrace = OutputStageTrace;
                        if (outputStageTrace is { IsCapturing: true })
                        {
                            outputStageTrace.AddD418Write(genericStepImpulse, matrixImpulse, filterModeImpulse);
                        }

                        _masterVolume = SidAnalog.ConvertVolume(volume, Model, SidEmulationProfile);
                        _volumeOffset = nextVolumeOffset;
                        _voice3Muted = (value & 0x80) != 0;
                        filterCoefficientsDirty |= _filterMode != (value & 0x70);
                    }
                }
            }

            if (filterCoefficientsDirty)
            {
                UpdateFilterCoefficients();
            }
        }

        [HotPath]
        private void AddRegisterTransient(double impulse)
        {
            if (impulse == 0.0 || _volumeRegisterTransientLimit <= 0.0)
            {
                return;
            }

            _volumeRegisterTransientTarget = Math.Clamp(
                _volumeRegisterTransientTarget + impulse,
                -_volumeRegisterTransientLimit,
                _volumeRegisterTransientLimit);
        }

        [HotPath]
        private void TraceVoice(
            long cycle,
            int voiceIndex,
            SidVoiceDebugState before,
            SidWaveformTrace waveformTrace,
            double waveformOutput,
            double voiceOutput)
        {
            var voice = _voices[voiceIndex];
            var after = voice.GetDebugState();
            Trace?.Add(new SidCycleTraceFrame(
                cycle,
                TraceChipIndex,
                voiceIndex,
                voice.CycleEvents,
                voice.Frequency,
                voice.PulseWidth,
                after.Control,
                before.Accumulator,
                after.Accumulator,
                before.NoiseShiftRegister,
                after.NoiseShiftRegister,
                after.NoiseDac,
                before.EnvelopeCounter,
                after.EnvelopeCounter,
                after.RateCounter,
                after.ExponentialCounter,
                after.EnvelopeState,
                waveformTrace.WaveformDac,
                waveformTrace.PulseHigh,
                waveformTrace.SyncSourceMsb,
                waveformTrace.RingModInverted,
                waveformTrace.TriangleInverted,
                waveformTrace.NoiseUsesPostShiftRegister,
                waveformOutput,
                voiceOutput));
        }

        [HotPath]
        private double Mix(double voice1, double voice2, double voice3)
        {
            if ((MutedVoicesMask & 0x01) != 0)
            {
                voice1 = 0;
            }

            if ((MutedVoicesMask & 0x02) != 0)
            {
                voice2 = 0;
            }

            if ((MutedVoicesMask & 0x04) != 0)
            {
                voice3 = 0;
            }

            if (_analog6581Filter != null)
            {
                var analogOutput = _analog6581Filter.Process(
                    voice1,
                    voice2,
                    voice3,
                    _filterRouting,
                    _filterMode,
                    _voice3Muted,
                    _filterCutoffRegister,
                    _filterResonanceNibble);
                _lastLowPass = _analog6581Filter.LastLowPass;
                _lastBandPass = _analog6581Filter.LastBandPass;
                _lastHighPass = _analog6581Filter.LastHighPass;
                return ApplyMos6581AnalogOutputStage(_analog6581Filter, analogOutput);
            }

            var filterRouting = _filterRouting;
            var direct = 0.0;
            var filtered = 0.0;
            if ((filterRouting & 0x01) != 0)
            {
                filtered += voice1;
            }
            else
            {
                direct += voice1;
            }

            if ((filterRouting & 0x02) != 0)
            {
                filtered += voice2;
            }
            else
            {
                direct += voice2;
            }

            if ((filterRouting & 0x04) != 0)
            {
                filtered += voice3;
            }
            else if (!_voice3Muted)
            {
                direct += voice3;
            }

            var leaked = _filterMode == 0 ? 0.0 : filtered * _filterVoiceLeakageGain;
            var voiceSignal = (direct + leaked + ApplyFilter(filtered * _filterInputGain)) *
                _voiceMixGain *
                _masterVolume;
            return ApplyOutputStage(voiceSignal);
        }

        [HotPath]
        private double ApplyOutputStage(double voiceSignal)
        {
            var volumeTransientTarget = _volumeRegisterTransientTarget;
            var volumeTransient = _volumeRegisterTransient +
                ((volumeTransientTarget - _volumeRegisterTransient) * _volumeRegisterTransientSlew);
            // Keep the SID-core output DC-bearing. Board coupling/DC blocking is handled by C64OutputStage.
            var sidCoreOutputSample = voiceSignal + _volumeOffset + volumeTransient;
            var output = SidAnalog.SoftClip(sidCoreOutputSample);
            _volumeRegisterTransient = volumeTransient;
            _volumeRegisterTransientTarget *= _volumeRegisterTransientDecay;
            _outputLowPassState += (output - _outputLowPassState) * _outputLowPassAlpha;
            var outputStageTrace = OutputStageTrace;
            if (outputStageTrace is { IsCapturing: true })
            {
                outputStageTrace.AddOutputCycle(new SidOutputStageCycle(
                    voiceSignal,
                    _volumeOffset,
                    volumeTransientTarget,
                    volumeTransient,
                    sidCoreOutputSample,
                    output,
                    0.0,
                    0.0,
                    0.0,
                    _outputLowPassState));
            }

            return _outputLowPassState;
        }

        [HotPath]
        private double ApplyMos6581AnalogOutputStage(SidMos6581AnalogFilter filter, double mixedVoltage)
        {
            var volumeTransientTarget = _volumeRegisterTransientTarget;
            var volumeTransient = _volumeRegisterTransient +
                ((volumeTransientTarget - _volumeRegisterTransient) * _volumeRegisterTransientSlew);
            // Keep the analog SID output DC-bearing. Board coupling/DC blocking is handled by C64OutputStage.
            var volumeControlSample = _volumeOffset + volumeTransient;
            var outputVoltage = filter.ApplyOutputStageVoltage(
                mixedVoltage,
                _masterVolume,
                _volumeOffset,
                volumeTransient);
            var outputSample = filter.OutputVoltageToSample(outputVoltage);
            _volumeRegisterTransient = volumeTransient;
            _volumeRegisterTransientTarget *= _volumeRegisterTransientDecay;
            _analogOutputLowPassVoltage += (outputVoltage - _analogOutputLowPassVoltage) * _analogOutputLowPassAlpha;
            var finalSample = filter.OutputVoltageToSample(_analogOutputLowPassVoltage);
            var outputStageTrace = OutputStageTrace;
            if (outputStageTrace is { IsCapturing: true })
            {
                outputStageTrace.AddOutputCycle(new SidOutputStageCycle(
                    0.0,
                    _volumeOffset,
                    volumeTransientTarget,
                    volumeTransient,
                    volumeControlSample,
                    outputSample,
                    mixedVoltage,
                    outputVoltage,
                    _analogOutputLowPassVoltage,
                    finalSample));
            }

            return finalSample;
        }

        [HotPath]
        private double ApplyFilter(double input)
        {
            return _filterProfile.UsesNonlinearFilter
                ? ApplyNonlinearFilter(input)
                : ApplyLinearFilter(input, _filterG, _filterDamping, _filterDenominator);
        }

        [HotPath]
        private double ApplyLinearFilter(double input, double g, double damping, double denominator)
        {
            var high = (input - ((damping + g) * _filterIntegrator1) - _filterIntegrator2) / denominator;
            var band = (g * high) + _filterIntegrator1;
            var low = (g * band) + _filterIntegrator2;
            _filterIntegrator1 = (g * high) + band;
            _filterIntegrator2 = (g * band) + low;
            _lastLowPass = low;
            _lastBandPass = band;
            _lastHighPass = high;

            return SelectFilterOutput(low, band, high) * _filterOutputGain;
        }

        [HotPath]
        private double ApplyNonlinearFilter(double input)
        {
            var drivenInput = Saturate(input * _filterProfile.FilterDrive, _filterProfile.FilterInputSaturation);
            var signalLevel = NormalizeAgainstLimit(drivenInput, _filterProfile.FilterInputSaturation);
            var cutoffScale = Math.Clamp(
                1.0 - (_filterProfile.CutoffSignalModulation * signalLevel * signalLevel),
                0.65,
                1.15);
            var g = _filterG * cutoffScale;
            var denominator = 1.0 + (_filterDamping * g) + (g * g);

            var high = (drivenInput - ((_filterDamping + g) * _filterIntegrator1) - _filterIntegrator2) / denominator;
            high = Saturate(high, _filterProfile.FilterOutputSaturation);
            var band = Saturate((g * high) + _filterIntegrator1, _filterProfile.FilterIntegratorLimit);
            var low = Saturate((g * band) + _filterIntegrator2, _filterProfile.FilterIntegratorLimit);
            _filterIntegrator1 = Saturate((g * high) + band, _filterProfile.FilterIntegratorLimit);
            _filterIntegrator2 = Saturate((g * band) + low, _filterProfile.FilterIntegratorLimit);
            _lastLowPass = low;
            _lastBandPass = band;
            _lastHighPass = high;

            return Saturate(
                SelectFilterOutput(low, band, high) * _filterOutputGain,
                _filterProfile.FilterOutputSaturation);
        }

        [HotPath]
        private double SelectFilterOutput(double low, double band, double high)
        {
            var output = _filterMode switch
            {
                0x10 => low * _filterLowPassGain,
                0x20 => band * _filterBandPassGain,
                0x30 => (low * _filterLowPassGain) + (band * _filterBandPassGain),
                0x40 => high * _filterHighPassGain,
                0x50 => (low * _filterLowPassGain) + (high * _filterHighPassGain),
                0x60 => (band * _filterBandPassGain) + (high * _filterHighPassGain),
                0x70 => ((low * _filterLowPassGain) + (band * _filterBandPassGain)) + (high * _filterHighPassGain),
                _ => 0.0
            };

            return output;
        }

        [HotPath]
        private static double Saturate(double value, double limit)
        {
            if (limit <= 0.0 || double.IsInfinity(limit))
            {
                return value;
            }

            if (Math.Abs(value) <= limit * 0.5)
            {
                return value;
            }

            var normalized = value / limit;
            return value / Math.Sqrt(1.0 + (normalized * normalized));
        }

        [HotPath]
        private static double NormalizeAgainstLimit(double value, double limit)
        {
            if (limit <= 0.0 || double.IsInfinity(limit))
            {
                return Math.Min(1.0, Math.Abs(value));
            }

            return Math.Min(1.0, Math.Abs(value) / limit);
        }

        [HotPath]
        private void UpdateFilterCoefficients()
        {
            _filterCutoffRegister = (_forwardedRegisters[0x16] << 3) | (_forwardedRegisters[0x15] & 0x07);
            _filterResonanceNibble = _forwardedRegisters[0x17] >> 4;
            _filterMode = _forwardedRegisters[0x18] & 0x70;
            _filterCutoffHz = _filterProfile.MapCutoff(_filterCutoffRegister);
            _filterG = Math.Tan(Math.PI * _filterCutoffHz / _cpuCyclesPerSecond);
            _filterDamping = _filterProfile.MapDamping(_filterResonanceNibble, _filterCutoffRegister);
            _filterDenominator = 1.0 + (_filterDamping * _filterG) + (_filterG * _filterG);
            _filterOutputGain = _filterProfile.MapFilterOutputGain(_filterResonanceNibble, _filterCutoffRegister);
            _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(_filterCutoffRegister);
            _filterCoefficientsDirty = false;
        }

        private SidChipDebugState CreateDebugState()
        {
            if (_filterCoefficientsDirty)
            {
                UpdateFilterCoefficients();
            }

            var voices = new SidVoiceDebugState[_voices.Length];
            for (var i = 0; i < voices.Length; i++)
            {
                voices[i] = _voices[i].GetDebugState();
            }

            return new SidChipDebugState(
                (byte[])_forwardedRegisters.Clone(),
                voices,
                _filterProfile.Id,
                _filterCutoffRegister,
                _filterCutoffHz,
                _filterResonanceNibble,
                _filterMode,
                _filterDamping,
                _lastLowPass,
                _lastBandPass,
                _lastHighPass);
        }
    }
}
