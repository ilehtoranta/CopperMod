using System;

namespace CopperMod.Sid
{
    internal sealed class SidMos6581ResistorNetwork
    {
        public SidMos6581ResistorNetwork(
            double r1Ohms,
            double r2Ohms,
            double r6Ohms,
            double r8Ohms,
            double r24Ohms,
            double outputExternalOhms,
            double mixerDriveTrim = 1.0,
            double summerDriveTrim = 1.0,
            double resonanceFeedbackTrim = 1.0,
            double resonanceOutputTrim = 1.0,
            double outputGainTrim = 1.0)
        {
            R1Ohms = ValidatePositive(r1Ohms, nameof(r1Ohms));
            R2Ohms = ValidatePositive(r2Ohms, nameof(r2Ohms));
            R6Ohms = ValidatePositive(r6Ohms, nameof(r6Ohms));
            R8Ohms = ValidatePositive(r8Ohms, nameof(r8Ohms));
            R24Ohms = ValidatePositive(r24Ohms, nameof(r24Ohms));
            OutputExternalOhms = ValidatePositive(outputExternalOhms, nameof(outputExternalOhms));
            MixerDriveTrim = ValidatePositive(mixerDriveTrim, nameof(mixerDriveTrim));
            SummerDriveTrim = ValidatePositive(summerDriveTrim, nameof(summerDriveTrim));
            ResonanceFeedbackTrim = ValidatePositive(resonanceFeedbackTrim, nameof(resonanceFeedbackTrim));
            ResonanceOutputTrim = ValidatePositive(resonanceOutputTrim, nameof(resonanceOutputTrim));
            OutputGainTrim = ValidatePositive(outputGainTrim, nameof(outputGainTrim));
        }

        // Approximate 470 pF 6581 values from public SID filter circuit notes:
        // R24 ~= 1.5 MOhm and R1/R2/R6/R8 are the 1:2:6:8 on-die NMOS ratios.
        public static SidMos6581ResistorNetwork Default470Pf { get; } = new SidMos6581ResistorNetwork(
            r1Ohms: 64_000.0,
            r2Ohms: 128_000.0,
            r6Ohms: 384_000.0,
            r8Ohms: 512_000.0,
            r24Ohms: 1_500_000.0,
            outputExternalOhms: 1_000.0);

        public double R1Ohms { get; }

        public double R2Ohms { get; }

        public double R6Ohms { get; }

        public double R8Ohms { get; }

        public double R24Ohms { get; }

        public double OutputExternalOhms { get; }

        public double MixerDriveTrim { get; }

        public double SummerDriveTrim { get; }

        public double ResonanceFeedbackTrim { get; }

        public double ResonanceOutputTrim { get; }

        public double OutputGainTrim { get; }

        public SidMos6581ResistorNetwork WithTrims(
            double mixerDriveTrim,
            double summerDriveTrim,
            double resonanceFeedbackTrim,
            double resonanceOutputTrim,
            double outputGainTrim)
        {
            return new SidMos6581ResistorNetwork(
                R1Ohms,
                R2Ohms,
                R6Ohms,
                R8Ohms,
                R24Ohms,
                OutputExternalOhms,
                mixerDriveTrim,
                summerDriveTrim,
                resonanceFeedbackTrim,
                resonanceOutputTrim,
                outputGainTrim);
        }

        private static double ValidatePositive(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name, value, "MOS6581 analog resistor values must be finite and positive.");
            }

            return value;
        }
    }

    internal sealed class SidMos6581CutoffCircuit
    {
        public SidMos6581CutoffCircuit(
            double minimumCutoffHz,
            double fullScaleCutoffHz,
            double capacitorFarads = 470e-12,
            double dacTwoRDivR = 1.879,
            bool dacTerminated = false,
            double thresholdVoltage = 1.31,
            double mobilityCox = 20e-6,
            double vcrWidthLength = 9.0,
            double processConductanceScale = 1.0,
            double probeVoltageDelta = 0.010)
        {
            MinimumCutoffHz = ValidatePositive(minimumCutoffHz, nameof(minimumCutoffHz));
            FullScaleCutoffHz = ValidatePositive(fullScaleCutoffHz, nameof(fullScaleCutoffHz));
            if (FullScaleCutoffHz <= MinimumCutoffHz)
            {
                throw new ArgumentOutOfRangeException(nameof(fullScaleCutoffHz), fullScaleCutoffHz, "MOS6581 cutoff full-scale target must be above the minimum target.");
            }

            CapacitorFarads = ValidatePositive(capacitorFarads, nameof(capacitorFarads));
            DacTwoRDivR = ValidatePositive(dacTwoRDivR, nameof(dacTwoRDivR));
            DacTerminated = dacTerminated;
            ThresholdVoltage = ValidatePositive(thresholdVoltage, nameof(thresholdVoltage));
            MobilityCox = ValidatePositive(mobilityCox, nameof(mobilityCox));
            VcrWidthLength = ValidatePositive(vcrWidthLength, nameof(vcrWidthLength));
            ProcessConductanceScale = ValidatePositive(processConductanceScale, nameof(processConductanceScale));
            ProbeVoltageDelta = ValidatePositive(probeVoltageDelta, nameof(probeVoltageDelta));
        }

        public double MinimumCutoffHz { get; }

        public double FullScaleCutoffHz { get; }

        public double CapacitorFarads { get; }

        public double DacTwoRDivR { get; }

        public bool DacTerminated { get; }

        public double ThresholdVoltage { get; }

        public double MobilityCox { get; }

        public double VcrWidthLength { get; }

        public double ProcessConductanceScale { get; }

        public double ProbeVoltageDelta { get; }

        private static double ValidatePositive(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name, value, "MOS6581 cutoff circuit values must be finite and positive.");
            }

            return value;
        }
    }

    internal sealed class SidMos6581OutputCircuit
    {
        public SidMos6581OutputCircuit(
            double workingPointVoltage = 4.54,
            double outputSignalGain = 0.42,
            double outputSoftClipAmount = 0.20,
            double outputLowPassCutoffHz = 12_000.0)
        {
            WorkingPointVoltage = ValidatePositive(workingPointVoltage, nameof(workingPointVoltage));
            OutputSignalGain = ValidatePositive(outputSignalGain, nameof(outputSignalGain));
            OutputSoftClipAmount = ValidatePositive(outputSoftClipAmount, nameof(outputSoftClipAmount));
            OutputLowPassCutoffHz = ValidatePositive(outputLowPassCutoffHz, nameof(outputLowPassCutoffHz));
        }

        public double WorkingPointVoltage { get; }

        public double OutputSignalGain { get; }

        public double OutputSoftClipAmount { get; }

        public double OutputLowPassCutoffHz { get; }

        private static double ValidatePositive(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name, value, "MOS6581 output circuit values must be finite and positive.");
            }

            return value;
        }
    }

    internal sealed class SidMos6581AnalogParameters
    {
        public SidMos6581AnalogParameters(
            SidMos6581CutoffCircuit cutoffCircuit,
            double voiceVoltageRange,
            double voiceDcVoltage,
            double filterInputGain,
            double filterOutputGain,
            double filterLeakageGain,
            double opAmpDrive,
            double opAmpOutputScale,
            double integratorGain,
            double vcrSignalModulation,
            double resonanceDampingScale,
            SidMos6581ResistorNetwork? resistorNetwork = null,
            SidMos6581OutputCircuit? outputCircuit = null)
        {
            CutoffCircuit = cutoffCircuit;
            VoiceVoltageRange = voiceVoltageRange;
            VoiceDcVoltage = voiceDcVoltage;
            FilterInputGain = filterInputGain;
            FilterOutputGain = filterOutputGain;
            FilterLeakageGain = filterLeakageGain;
            OpAmpDrive = opAmpDrive;
            OpAmpOutputScale = opAmpOutputScale;
            IntegratorGain = integratorGain;
            VcrSignalModulation = vcrSignalModulation;
            ResonanceDampingScale = resonanceDampingScale;
            ResistorNetwork = resistorNetwork ?? SidMos6581ResistorNetwork.Default470Pf;
            OutputCircuit = outputCircuit ?? new SidMos6581OutputCircuit();
        }

        public SidMos6581CutoffCircuit CutoffCircuit { get; }

        public double VoiceVoltageRange { get; }

        public double VoiceDcVoltage { get; }

        public double FilterInputGain { get; }

        public double FilterOutputGain { get; }

        public double FilterLeakageGain { get; }

        public double OpAmpDrive { get; }

        public double OpAmpOutputScale { get; }

        public double IntegratorGain { get; }

        public double VcrSignalModulation { get; }

        public double ResonanceDampingScale { get; }

        public SidMos6581ResistorNetwork ResistorNetwork { get; }

        public SidMos6581OutputCircuit OutputCircuit { get; }
    }

    internal sealed class SidMos6581AnalogModel
    {
        public const int CutoffTableSize = 2048;
        private const int OpAmpTableSize = 4096;
        private const int VcrSignalBins = 16;
        private const double Vdd = 12.18;
        private const double ThermalVoltage = 26.0e-3;
        private const double WorkingPoint = 4.54;
        private const double Vmin = 0.81;
        private const double Vmax = 10.31;
        private const double InfiniteResistance = 1.0e9;

        // Publicly documented 6581R4AR op-amp transfer points. These are data
        // constants, not derived from emulator code. Runtime tables below are
        // generated by local interpolation and circuit equations.
        private static readonly (double Input, double Output)[] OpAmpPoints =
        {
            (0.81, 10.31),
            (2.40, 10.31),
            (2.60, 10.30),
            (2.70, 10.29),
            (2.80, 10.26),
            (2.90, 10.17),
            (3.00, 10.04),
            (3.10, 9.83),
            (3.20, 9.58),
            (3.30, 9.32),
            (3.50, 8.69),
            (3.70, 8.00),
            (4.00, 6.89),
            (4.40, 5.21),
            (4.54, 4.54),
            (4.60, 4.19),
            (4.80, 3.00),
            (4.90, 2.30),
            (4.95, 2.03),
            (5.00, 1.88),
            (5.05, 1.77),
            (5.10, 1.69),
            (5.20, 1.58),
            (5.40, 1.44),
            (5.60, 1.33),
            (5.80, 1.26),
            (6.00, 1.21),
            (6.40, 1.12),
            (7.00, 1.02),
            (7.50, 0.97),
            (8.50, 0.89),
            (10.00, 0.81),
            (10.31, 0.81)
        };

        private readonly SidMos6581AnalogParameters _parameters;
        private readonly double[] _opAmpTransfer;
        private readonly double[] _opAmpReverse;
        private readonly double[] _cutoffControlVoltage;
        private readonly double[,] _vcrConductanceScale;
        private readonly double[] _mixerGain;
        private readonly double[,] _resonanceDampingScale;
        private readonly double[,] _resonanceOutputScale;
        private readonly double[] _summerGain;
        private readonly double[] _outputGain;
        private readonly double _opAmpOutputScaleInverse;
        private readonly double _outputSignalGain;
        private readonly double _outputSignalGainInverse;

        public SidMos6581AnalogModel(SidMos6581AnalogParameters parameters)
        {
            _parameters = parameters;
            _opAmpOutputScaleInverse = 1.0 / Math.Max(1.0e-9, parameters.OpAmpOutputScale);
            _outputSignalGain = parameters.OutputCircuit.OutputSignalGain;
            _outputSignalGainInverse = 1.0 / Math.Max(1.0e-9, parameters.OutputCircuit.OutputSignalGain);
            _opAmpTransfer = BuildOpAmpTransferTable();
            _opAmpReverse = BuildOpAmpReverseTable();
            _cutoffControlVoltage = BuildCutoffControlVoltageTable(parameters);
            _vcrConductanceScale = BuildVcrConductanceScaleTable(_cutoffControlVoltage, parameters);
            CutoffHz = BuildCutoffTable(_cutoffControlVoltage, parameters);
            _mixerGain = BuildMixerGainTable();
            _resonanceDampingScale = BuildResonanceDampingScaleTable();
            _resonanceOutputScale = BuildResonanceOutputScaleTable();
            _summerGain = BuildModeGainTable(outputTable: false);
            _outputGain = BuildModeGainTable(outputTable: true);
        }

        public SidMos6581AnalogParameters Parameters => _parameters;

        public SidMos6581ResistorNetwork ResistorNetwork => _parameters.ResistorNetwork;

        public SidMos6581CutoffCircuit CutoffCircuit => _parameters.CutoffCircuit;

        public SidMos6581OutputCircuit OutputCircuit => _parameters.OutputCircuit;

        public double[] CutoffHz { get; }

        public double WorkingPointVoltage => WorkingPoint;

        public double MinimumOpAmpVoltage => Vmin;

        public double MaximumOpAmpVoltage => Vmax;

        public int OpAmpTableLength => _opAmpTransfer.Length;

        public int CutoffDacTableLength => _cutoffControlVoltage.Length;

        public int MixerGainTableLength => _mixerGain.Length;

        public int ResonanceFeedbackTableLength => _resonanceDampingScale.GetLength(1);

        public int ResonanceFeedbackCutoffTableLength => _resonanceDampingScale.GetLength(0);

        public int SummerGainTableLength => _summerGain.Length;

        public int OutputGainTableLength => _outputGain.Length;

        public double MapOpAmp(double normalizedInput)
        {
            var voltage = WorkingPoint + (Math.Clamp(normalizedInput, -1.0, 1.0) * _parameters.OpAmpDrive);
            var shapedVoltage = MapOpAmpVoltage(voltage);
            return Math.Clamp((WorkingPoint - shapedVoltage) / _parameters.OpAmpOutputScale, -1.8, 1.8);
        }

        public double MapOpAmpVoltage(double inputVoltage)
        {
            return Lookup(_opAmpTransfer, Math.Clamp(inputVoltage, Vmin, Vmax));
        }

        public double MapOpAmpVoltageFromSignal(double signalVoltage)
        {
            var normalizedInput = signalVoltage * _opAmpOutputScaleInverse;
            if (normalizedInput > 1.0)
            {
                normalizedInput = 1.0;
            }
            else if (normalizedInput < -1.0)
            {
                normalizedInput = -1.0;
            }

            var inputVoltage = WorkingPoint + (normalizedInput * _parameters.OpAmpDrive);
            return MapOpAmpVoltage(inputVoltage);
        }

        public double MapReverseOpAmp(double normalizedOutput)
        {
            var voltage = WorkingPoint - (Math.Clamp(normalizedOutput, -1.0, 1.0) * _parameters.OpAmpOutputScale);
            var inputVoltage = Lookup(_opAmpReverse, voltage);
            return Math.Clamp((inputVoltage - WorkingPoint) / _parameters.OpAmpDrive, -1.8, 1.8);
        }

        public double MapCutoffControlVoltage(int cutoffRegister)
        {
            return _cutoffControlVoltage[Math.Clamp(cutoffRegister, 0, _cutoffControlVoltage.Length - 1)];
        }

        public double MapVcrConductanceScale(int cutoffRegister, double signalDelta)
        {
            var binPosition = Math.Abs(signalDelta) * ((VcrSignalBins - 1) / 1.8);
            var bin = binPosition >= VcrSignalBins - 1
                ? VcrSignalBins - 1
                : (int)(binPosition + 0.5);
            return _vcrConductanceScale[Math.Clamp(cutoffRegister, 0, CutoffTableSize - 1), bin];
        }

        public double MapMixerGain(int routedVoiceCount)
        {
            return _mixerGain[Math.Clamp(routedVoiceCount, 0, _mixerGain.Length - 1)];
        }

        public double MapResonanceFeedbackScale(int resonanceNibble)
        {
            return MapResonanceFeedbackScale(resonanceNibble, CutoffTableSize - 1);
        }

        public double MapResonanceFeedbackScale(int resonanceNibble, int cutoffRegister)
        {
            return _resonanceDampingScale[
                Math.Clamp(cutoffRegister, 0, _resonanceDampingScale.GetLength(0) - 1),
                Math.Clamp(resonanceNibble, 0, _resonanceDampingScale.GetLength(1) - 1)];
        }

        public double MapResonanceOutputScale(int resonanceNibble, int cutoffRegister)
        {
            return _resonanceOutputScale[
                Math.Clamp(cutoffRegister, 0, _resonanceOutputScale.GetLength(0) - 1),
                Math.Clamp(resonanceNibble, 0, _resonanceOutputScale.GetLength(1) - 1)];
        }

        public double MapSummerGain(int filterMode)
        {
            return _summerGain[filterMode & 0x70];
        }

        public double MapOutputGain(int filterMode)
        {
            return _outputGain[filterMode & 0x70];
        }

        public double SignalToNodeVoltage(double signalVoltage)
        {
            var voltage = WorkingPoint - signalVoltage;
            if (voltage < Vmin)
            {
                return Vmin;
            }

            return voltage > Vmax ? Vmax : voltage;
        }

        public double SignalToOutputNodeVoltage(double signalVoltage)
        {
            return WorkingPoint - signalVoltage;
        }

        public double NodeVoltageToSignal(double nodeVoltage)
        {
            if (nodeVoltage < Vmin)
            {
                nodeVoltage = Vmin;
            }
            else if (nodeVoltage > Vmax)
            {
                nodeVoltage = Vmax;
            }

            return WorkingPoint - nodeVoltage;
        }

        public double OutputNodeVoltageToSignal(double nodeVoltage)
        {
            return WorkingPoint - nodeVoltage;
        }

        public double SignalVoltageToDebug(double signalVoltage)
        {
            var debug = signalVoltage * _opAmpOutputScaleInverse;
            if (debug < -1.8)
            {
                return -1.8;
            }

            return debug > 1.8 ? 1.8 : debug;
        }

        public double NodeVoltageToDebug(double nodeVoltage)
        {
            return SignalVoltageToDebug(NodeVoltageToSignal(nodeVoltage));
        }

        public double ApplyOutputStageVoltage(
            double mixedVoltage,
            double volumeGain,
            double volumeOffsetSample,
            double volumeTransientSample)
        {
            var signalVoltage =
                (OutputNodeVoltageToSignal(mixedVoltage) * volumeGain) +
                SampleToOutputSignalVoltage(volumeOffsetSample + volumeTransientSample);
            var normalizedSample = SignalVoltageToSample(signalVoltage);
            var shapedSample = normalizedSample / (1.0 + (Math.Abs(normalizedSample) * _parameters.OutputCircuit.OutputSoftClipAmount));
            shapedSample = Math.Clamp(shapedSample, -0.999, 0.999);
            return SignalToOutputNodeVoltage(SampleToOutputSignalVoltage(shapedSample));
        }

        public double OutputVoltageToSample(double outputVoltage)
        {
            return Math.Clamp(SignalVoltageToSample(OutputNodeVoltageToSignal(outputVoltage)), -0.999, 0.999);
        }

        public double SampleToOutputVoltage(double sample)
        {
            return SignalToOutputNodeVoltage(SampleToOutputSignalVoltage(sample));
        }

        private double SignalVoltageToSample(double signalVoltage)
        {
            return signalVoltage * _opAmpOutputScaleInverse * _outputSignalGain;
        }

        private double SampleToOutputSignalVoltage(double sample)
        {
            return sample * _outputSignalGainInverse * _parameters.OpAmpOutputScale;
        }

        private static double[] BuildOpAmpTransferTable()
        {
            var table = new double[OpAmpTableSize];
            for (var i = 0; i < table.Length; i++)
            {
                var voltage = Vmin + ((Vmax - Vmin) * i / (table.Length - 1));
                table[i] = InterpolateOpAmpOutput(voltage);
            }

            return table;
        }

        private static double[] BuildOpAmpReverseTable()
        {
            var table = new double[OpAmpTableSize];
            for (var i = 0; i < table.Length; i++)
            {
                var outputVoltage = Vmin + ((Vmax - Vmin) * i / (table.Length - 1));
                table[i] = InvertOpAmpOutput(outputVoltage);
            }

            return table;
        }

        private static double[] BuildCutoffControlVoltageTable(SidMos6581AnalogParameters parameters)
        {
            var circuit = parameters.CutoffCircuit;
            var dac = BuildForcedMsbCutoffDacTable(circuit);
            var zeroControlVoltage = SolveControlVoltageForCutoff(circuit.MinimumCutoffHz, circuit);
            var fullScaleControlVoltage = SolveControlVoltageForCutoff(circuit.FullScaleCutoffHz, circuit);
            var table = new double[CutoffTableSize];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = zeroControlVoltage + (dac[i] * (fullScaleControlVoltage - zeroControlVoltage));
            }

            return table;
        }

        private static double[] BuildForcedMsbCutoffDacTable(SidMos6581CutoffCircuit circuit)
        {
            var physicalDac = BuildKinkedDacTable(bits: 12, twoRDivR: circuit.DacTwoRDivR, terminated: circuit.DacTerminated);
            var low = physicalDac[0x800];
            var high = physicalDac[0xFFF];
            var span = Math.Max(1.0e-12, high - low);
            var table = new double[CutoffTableSize];
            for (var register = 0; register < table.Length; register++)
            {
                table[register] = Math.Clamp((physicalDac[0x800 | register] - low) / span, 0.0, 1.0);
            }

            table[0] = 0.0;
            table[^1] = 1.0;
            return table;
        }

        private static double[] BuildCutoffTable(double[] controlVoltage, SidMos6581AnalogParameters parameters)
        {
            var circuit = parameters.CutoffCircuit;
            var table = new double[controlVoltage.Length];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = Math.Max(0.0, EstimateSmallSignalCutoffHz(controlVoltage[i], circuit));
            }

            for (var i = 1; i < table.Length; i++)
            {
                table[i] = Math.Max(table[i], table[i - 1]);
            }

            return table;
        }

        private static double EstimateSmallSignalCutoffHz(double controlVoltage, SidMos6581CutoffCircuit circuit)
        {
            var delta = circuit.ProbeVoltageDelta;
            var conductance = EstimateVcrConductance(WorkingPoint + delta, WorkingPoint - delta, controlVoltage, circuit);
            return conductance / (2.0 * Math.PI);
        }

        private static double[,] BuildVcrConductanceScaleTable(double[] controlVoltage, SidMos6581AnalogParameters parameters)
        {
            var circuit = parameters.CutoffCircuit;
            var table = new double[CutoffTableSize, VcrSignalBins];
            for (var cutoff = 0; cutoff < CutoffTableSize; cutoff++)
            {
                var referenceDelta = circuit.ProbeVoltageDelta;
                var reference = EstimateVcrConductance(WorkingPoint + referenceDelta, WorkingPoint - referenceDelta, controlVoltage[cutoff], circuit);
                reference = Math.Max(reference, 1.0e-18);
                for (var bin = 0; bin < VcrSignalBins; bin++)
                {
                    var delta = referenceDelta + (parameters.VcrSignalModulation * bin / (VcrSignalBins - 1));
                    var conductance = EstimateVcrConductance(WorkingPoint + delta, WorkingPoint - delta, controlVoltage[cutoff], circuit);
                    table[cutoff, bin] = Math.Clamp(conductance / reference, 0.35, 1.35);
                }
            }

            return table;
        }

        private double[] BuildMixerGainTable()
        {
            var table = new double[4];
            var network = _parameters.ResistorNetwork;
            var voiceConductance = Conductance(network.R8Ohms);
            var referenceConductance = (3.0 * voiceConductance) + Conductance(network.R24Ohms);
            table[0] = 1.0;
            for (var voices = 1; voices < table.Length; voices++)
            {
                var inputConductance = voices * voiceConductance;
                var load = inputConductance / referenceConductance;
                var drive = Math.Clamp(load * network.MixerDriveTrim, 0.0, 1.15);
                var compression = Math.Abs(MapOpAmp(drive) - drive);
                var loadingLoss = (load * 0.018) + (load * load * 0.030);
                table[voices] = Math.Clamp(1.025 - loadingLoss - (compression * 0.075), 0.90, 1.04);
            }

            return table;
        }

        private double[,] BuildResonanceDampingScaleTable()
        {
            var table = new double[CutoffTableSize, 16];
            var network = _parameters.ResistorNetwork;
            for (var cutoff = 0; cutoff < CutoffTableSize; cutoff++)
            {
                var cutoffWeight = Math.Sqrt(cutoff / (double)(CutoffTableSize - 1));
                var lowCutoffWeight = 1.0 - cutoffWeight;
                for (var resonance = 0; resonance < table.GetLength(1); resonance++)
                {
                    var feedback = ResonanceConductanceRatio(network, resonance) * network.ResonanceFeedbackTrim;
                    var drivenFeedback = Math.Clamp(feedback * 0.85, 0.0, 1.15);
                    var shaped = Math.Abs(MapOpAmp(drivenFeedback) - drivenFeedback);
                    var resonancePull = feedback * feedback * (0.065 + (0.070 * cutoffWeight));
                    var ladderLoss = Conductance(network.R24Ohms) / (Conductance(network.R24Ohms) + Conductance(network.R2Ohms));
                    var lowCutoffStabilizer = lowCutoffWeight * (0.021 + (ladderLoss * 0.018));
                    var opAmpStabilizer = shaped * 0.020;
                    table[cutoff, resonance] = Math.Clamp(
                        1.025 + lowCutoffStabilizer + opAmpStabilizer - resonancePull,
                        0.88,
                        1.08);
                }
            }

            return table;
        }

        private double[,] BuildResonanceOutputScaleTable()
        {
            var table = new double[CutoffTableSize, 16];
            var network = _parameters.ResistorNetwork;
            for (var cutoff = 0; cutoff < CutoffTableSize; cutoff++)
            {
                var cutoffWeight = cutoff / (double)(CutoffTableSize - 1);
                var midCutoffWeight = Math.Sin(cutoffWeight * Math.PI);
                for (var resonance = 0; resonance < table.GetLength(1); resonance++)
                {
                    var normalized = ResonanceConductanceRatio(network, resonance) * network.ResonanceOutputTrim;
                    var resonanceShape = Math.Pow(Math.Clamp(normalized, 0.0, 1.4), 1.45);
                    table[cutoff, resonance] = Math.Clamp(
                        1.0 + (resonanceShape * (0.08 + (0.12 * midCutoffWeight) + (0.04 * cutoffWeight))),
                        0.98,
                        1.28);
                }
            }

            return table;
        }

        private double[] BuildModeGainTable(bool outputTable)
        {
            var table = new double[128];
            var network = _parameters.ResistorNetwork;
            var modeConductance = Conductance(network.R6Ohms);
            var outputLoad = network.OutputExternalOhms / (network.OutputExternalOhms + network.R2Ohms);
            for (var mode = 0; mode < table.Length; mode += 0x10)
            {
                var selected = CountModeOutputs(mode);
                if (selected == 0)
                {
                    table[mode] = 1.0;
                    continue;
                }

                var selectedConductance = selected * modeConductance;
                var load = selectedConductance / ((3.0 * modeConductance) + Conductance(network.R24Ohms));
                var drive = Math.Clamp(load * network.SummerDriveTrim, 0.0, 1.15);
                var compression = Math.Abs(MapOpAmp(drive) - drive);
                var multiOutputLoss = selectedConductance <= modeConductance
                    ? 0.0
                    : (selectedConductance - modeConductance) / (selectedConductance + (4.0 * modeConductance));
                var selectedLoss = outputTable ? 0.112 : 0.080;
                var opAmpLoss = outputTable ? 0.050 : 0.034;
                var packageLoss = outputTable ? outputLoad * 0.72 : 0.0;
                var trim = outputTable ? network.OutputGainTrim : 1.0;
                table[mode] = Math.Clamp(
                    (1.020 - (multiOutputLoss * selectedLoss) - (compression * opAmpLoss) - packageLoss) * trim,
                    0.86,
                    1.04);
            }

            return table;
        }

        private static double Conductance(double resistanceOhms)
        {
            return 1.0 / Math.Max(resistanceOhms, 1.0e-12);
        }

        private static double ResonanceConductanceRatio(SidMos6581ResistorNetwork network, int resonanceNibble)
        {
            var conductance = ResonanceLadderConductance(network, resonanceNibble & 0x0F);
            var maximum = ResonanceLadderConductance(network, 0x0F);
            return maximum <= 0.0 ? 0.0 : Math.Clamp(conductance / maximum, 0.0, 1.0);
        }

        private static double ResonanceLadderConductance(SidMos6581ResistorNetwork network, int code)
        {
            var conductance = 0.0;
            for (var bit = 0; bit < 4; bit++)
            {
                if ((code & (1 << bit)) == 0)
                {
                    continue;
                }

                conductance += Conductance(network.R2Ohms * (1 << bit));
            }

            return conductance;
        }

        private static int CountModeOutputs(int filterMode)
        {
            var bits = (filterMode >> 4) & 0x07;
            var count = 0;
            while (bits != 0)
            {
                count += bits & 1;
                bits >>= 1;
            }

            return count;
        }

        private static double SolveControlVoltageForCutoff(double targetCutoffHz, SidMos6581CutoffCircuit circuit)
        {
            var lower = Vmin;
            var upper = Vdd - circuit.ThresholdVoltage;
            for (var i = 0; i < 80; i++)
            {
                var midpoint = (lower + upper) * 0.5;
                var cutoffHz = EstimateSmallSignalCutoffHz(midpoint, circuit);
                if (cutoffHz < targetCutoffHz)
                {
                    lower = midpoint;
                }
                else
                {
                    upper = midpoint;
                }
            }

            return (lower + upper) * 0.5;
        }

        private static double EstimateVcrConductance(double sourceVoltage, double drainVoltage, double controlVoltage, SidMos6581CutoffCircuit circuit)
        {
            sourceVoltage = Math.Clamp(sourceVoltage, Vmin, Vmax);
            drainVoltage = Math.Clamp(drainVoltage, Vmin, Vmax);
            var vddMinusThreshold = Vdd - circuit.ThresholdVoltage;
            controlVoltage = Math.Clamp(controlVoltage, Vmin, vddMinusThreshold);

            // EKV-style VCR approximation used by public SID circuit notes:
            // the control DAC biases a MOSFET gate, drain/source current is
            // estimated from log(1 + exp(V / 2Ut))^2 terms, and the 470 pF
            // integrator capacitor converts that current into a per-cycle step.
            var vddSource = vddMinusThreshold - sourceVoltage;
            var vddControl = vddMinusThreshold - controlVoltage;
            var gateVoltage = vddMinusThreshold - Math.Sqrt(((vddSource * vddSource) + (vddControl * vddControl)) * 0.5);
            var forward = EkvCurrentTerm(gateVoltage - circuit.ThresholdVoltage - sourceVoltage);
            var reverse = EkvCurrentTerm(gateVoltage - circuit.ThresholdVoltage - drainVoltage);
            var voltageDelta = Math.Max(Math.Abs(sourceVoltage - drainVoltage), 1.0e-6);
            var currentScale = (2.0 * circuit.MobilityCox * ThermalVoltage * ThermalVoltage) *
                circuit.VcrWidthLength *
                circuit.ProcessConductanceScale /
                circuit.CapacitorFarads;
            return Math.Abs((forward - reverse) * currentScale / voltageDelta);
        }

        private static double EkvCurrentTerm(double voltage)
        {
            var exponent = Math.Clamp(voltage / (2.0 * ThermalVoltage), -60.0, 60.0);
            var term = Math.Log(1.0 + Math.Exp(exponent));
            return term * term;
        }

        private static double[] BuildKinkedDacTable(int bits, double twoRDivR, bool terminated)
        {
            var bitWeights = new double[bits];
            for (var setBit = 0; setBit < bits; setBit++)
            {
                var voltage = 1.0;
                var resistance = 1.0;
                var twoR = twoRDivR * resistance;
                var tail = terminated ? twoR : InfiniteResistance;

                for (var bit = 0; bit < setBit; bit++)
                {
                    tail = tail >= InfiniteResistance * 0.5
                        ? resistance + twoR
                        : resistance + ((twoR * tail) / (twoR + tail));
                }

                if (tail >= InfiniteResistance * 0.5)
                {
                    tail = twoR;
                }
                else
                {
                    tail = (twoR * tail) / (twoR + tail);
                    voltage *= tail / twoR;
                }

                for (var bit = setBit + 1; bit < bits; bit++)
                {
                    tail += resistance;
                    var current = voltage / tail;
                    tail = (twoR * tail) / (twoR + tail);
                    voltage = tail * current;
                }

                bitWeights[setBit] = voltage;
            }

            var table = new double[1 << bits];
            var maximum = 0.0;
            for (var code = 0; code < table.Length; code++)
            {
                var output = 0.0;
                for (var bit = 0; bit < bits; bit++)
                {
                    if ((code & (1 << bit)) != 0)
                    {
                        output += bitWeights[bit];
                    }
                }

                table[code] = output;
                maximum = Math.Max(maximum, output);
            }

            if (maximum <= 0.0)
            {
                return table;
            }

            for (var code = 0; code < table.Length; code++)
            {
                table[code] /= maximum;
            }

            return table;
        }

        private static double Lookup(double[] table, double voltage)
        {
            var position = Math.Clamp((voltage - Vmin) / (Vmax - Vmin), 0.0, 1.0) * (table.Length - 1);
            var index = (int)position;
            if (index >= table.Length - 1)
            {
                return table[^1];
            }

            var fraction = position - index;
            return table[index] + ((table[index + 1] - table[index]) * fraction);
        }

        private static double InterpolateOpAmpOutput(double inputVoltage)
        {
            if (inputVoltage <= OpAmpPoints[0].Input)
            {
                return OpAmpPoints[0].Output;
            }

            for (var i = 0; i < OpAmpPoints.Length - 1; i++)
            {
                var lower = OpAmpPoints[i];
                var upper = OpAmpPoints[i + 1];
                if (inputVoltage > upper.Input)
                {
                    continue;
                }

                var span = Math.Max(1.0e-9, upper.Input - lower.Input);
                var t = (inputVoltage - lower.Input) / span;
                t = (t * t) * (3.0 - (2.0 * t));
                return lower.Output + ((upper.Output - lower.Output) * t);
            }

            return OpAmpPoints[^1].Output;
        }

        private static double InvertOpAmpOutput(double outputVoltage)
        {
            for (var i = 0; i < OpAmpPoints.Length - 1; i++)
            {
                var lower = OpAmpPoints[i];
                var upper = OpAmpPoints[i + 1];
                var hi = Math.Max(lower.Output, upper.Output);
                var lo = Math.Min(lower.Output, upper.Output);
                if (outputVoltage < lo || outputVoltage > hi)
                {
                    continue;
                }

                var span = lower.Output - upper.Output;
                if (Math.Abs(span) < 1.0e-9)
                {
                    return (lower.Input + upper.Input) * 0.5;
                }

                var t = Math.Clamp((lower.Output - outputVoltage) / span, 0.0, 1.0);
                return lower.Input + ((upper.Input - lower.Input) * t);
            }

            return outputVoltage >= OpAmpPoints[0].Output
                ? OpAmpPoints[0].Input
                : OpAmpPoints[^1].Input;
        }
    }

    internal sealed class SidMos6581AnalogFilter
    {
        private const int NonlinearCorrectionIterations = 1;
        private const double IntegratorStateLimit = 1.80;
        private const double VoiceReferenceVoltageRange = 1.50;
        private readonly SidFilterProfileDefinition _profile;
        private readonly SidMos6581AnalogModel _model;
        private readonly double[] _integratorStep;
        private readonly double _integratorSignalLimit;
        private double _bandVoltage;
        private double _lowVoltage;
        private double _lastHighVoltage;
        private double _lastBandVoltage;
        private double _lastLowVoltage;
        private double _lastHighDebug;
        private double _lastBandDebug;
        private double _lastLowDebug;

        public SidMos6581AnalogFilter(SidFilterProfileDefinition profile, int cpuCyclesPerSecond)
        {
            _profile = profile;
            _model = profile.Analog6581Model ?? throw new ArgumentException("Profile does not provide a MOS6581 analog model.", nameof(profile));
            var clock = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            _integratorStep = new double[SidMos6581AnalogModel.CutoffTableSize];
            for (var i = 0; i < _integratorStep.Length; i++)
            {
                var alpha = 1.0 - Math.Exp(-2.0 * Math.PI * _model.CutoffHz[i] / clock);
                _integratorStep[i] = Math.Clamp(alpha * _model.Parameters.IntegratorGain, 0.0, 0.35);
            }

            _integratorSignalLimit = IntegratorStateLimit * _model.Parameters.OpAmpOutputScale;
            Reset();
        }

        public double LastLowPass => _lastLowDebug;

        public double LastBandPass => _lastBandDebug;

        public double LastHighPass => _lastHighDebug;

        public double LastLowPassVoltage => _lastLowVoltage;

        public double LastBandPassVoltage => _lastBandVoltage;

        public double LastHighPassVoltage => _lastHighVoltage;

        public double OutputLowPassCutoffHz => _model.OutputCircuit.OutputLowPassCutoffHz;

        public double OutputRestVoltage => _model.SampleToOutputVoltage(0.0);

        public double OutputVoltageToSample(double outputVoltage)
        {
            return _model.OutputVoltageToSample(outputVoltage);
        }

        public double ApplyOutputStageVoltage(
            double mixedVoltage,
            double volumeGain,
            double volumeOffsetSample,
            double volumeTransientSample)
        {
            return _model.ApplyOutputStageVoltage(mixedVoltage, volumeGain, volumeOffsetSample, volumeTransientSample);
        }

        public void Reset()
        {
            var rest = _model.WorkingPointVoltage;
            _bandVoltage = rest;
            _lowVoltage = rest;
            _lastHighVoltage = rest;
            _lastBandVoltage = rest;
            _lastLowVoltage = rest;
            _lastHighDebug = 0.0;
            _lastBandDebug = 0.0;
            _lastLowDebug = 0.0;
        }

        public void CopyStateFrom(SidMos6581AnalogFilter source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _bandVoltage = source._bandVoltage;
            _lowVoltage = source._lowVoltage;
            _lastHighVoltage = source._lastHighVoltage;
            _lastBandVoltage = source._lastBandVoltage;
            _lastLowVoltage = source._lastLowVoltage;
            _lastHighDebug = source._lastHighDebug;
            _lastBandDebug = source._lastBandDebug;
            _lastLowDebug = source._lastLowDebug;
        }

        public double Process(
            double voice1,
            double voice2,
            double voice3,
            int filterRouting,
            int filterMode,
            bool voice3Muted,
            int cutoffRegister,
            int resonanceNibble)
        {
            var parameters = _model.Parameters;
            voice1 = MapVoiceDacToSignalVoltage(voice1, parameters);
            voice2 = MapVoiceDacToSignalVoltage(voice2, parameters);
            voice3 = MapVoiceDacToSignalVoltage(voice3, parameters);

            var direct = 0.0;
            var filtered = 0.0;
            var routedVoiceCount = 0;
            RouteSignal(voice1, (filterRouting & 0x01) != 0, ref direct, ref filtered, ref routedVoiceCount);
            RouteSignal(voice2, (filterRouting & 0x02) != 0, ref direct, ref filtered, ref routedVoiceCount);
            if ((filterRouting & 0x04) != 0)
            {
                filtered += voice3;
                routedVoiceCount++;
            }
            else if (!voice3Muted)
            {
                direct += voice3;
            }

            if ((filterMode & 0x70) == 0)
            {
                _lastHighDebug = 0.0;
                _lastBandDebug = 0.0;
                _lastLowDebug = 0.0;
                return _model.SignalToOutputNodeVoltage(direct);
            }

            if (routedVoiceCount == 0 && IsAtRest())
            {
                _lastHighDebug = 0.0;
                _lastBandDebug = 0.0;
                _lastLowDebug = 0.0;
                return _model.SignalToOutputNodeVoltage(direct);
            }

            filtered *= _model.MapMixerGain(routedVoiceCount);
            var filterOutput = ClockFilter(filtered, filterMode, cutoffRegister, resonanceNibble);
            var leakage = filtered * _model.Parameters.FilterLeakageGain;
            return _model.SignalToOutputNodeVoltage(direct + leakage + filterOutput);
        }

        private bool IsAtRest()
        {
            var rest = _model.WorkingPointVoltage;
            return Math.Abs(_bandVoltage - rest) < 1.0e-9 &&
                Math.Abs(_lowVoltage - rest) < 1.0e-9 &&
                Math.Abs(_lastHighVoltage - rest) < 1.0e-9;
        }

        private static double MapVoiceDacToSignalVoltage(double voice, SidMos6581AnalogParameters parameters)
        {
            var idleVoltage = parameters.VoiceDcVoltage;
            var voiceVoltage = idleVoltage - (Math.Clamp(voice, -1.25, 1.25) * parameters.VoiceVoltageRange);
            var normalizedVoice = (idleVoltage - voiceVoltage) / VoiceReferenceVoltageRange;
            return normalizedVoice * parameters.OpAmpOutputScale;
        }

        private double ClockFilter(double input, int filterMode, int cutoffRegister, int resonanceNibble)
        {
            cutoffRegister = Math.Clamp(cutoffRegister, 0, _integratorStep.Length - 1);
            var parameters = _model.Parameters;
            var drivenInput = input * parameters.FilterInputGain;
            var damping = _profile.MapDamping(resonanceNibble, cutoffRegister) *
                parameters.ResonanceDampingScale *
                _model.MapResonanceFeedbackScale(resonanceNibble, cutoffRegister);
            var substeps = DetermineSubsteps(cutoffRegister, resonanceNibble);
            const int correctionIterations = NonlinearCorrectionIterations;
            var substepBase = _integratorStep[cutoffRegister] / substeps;
            var bandSignal = _model.NodeVoltageToSignal(_bandVoltage);
            var lowSignal = _model.NodeVoltageToSignal(_lowVoltage);
            var highVoltage = EvaluateHighVoltageFromSignals(drivenInput, damping, bandSignal, lowSignal);

            for (var substep = 0; substep < substeps; substep++)
            {
                var startBandSignal = bandSignal;
                var startLowSignal = lowSignal;
                var startHighVoltage = EvaluateHighVoltageFromSignals(drivenInput, damping, startBandSignal, startLowSignal);
                var startHighSignal = _model.NodeVoltageToSignal(startHighVoltage);
                var bandGuessSignal = SaturateAnalog(startBandSignal + (substepBase * startHighSignal), _integratorSignalLimit);
                var lowGuessSignal = SaturateAnalog(startLowSignal + (substepBase * bandGuessSignal), _integratorSignalLimit);
                var correctedBandSignal = bandGuessSignal;
                var correctedLowSignal = lowGuessSignal;
                var correctedHighSignal = startHighSignal;

                for (var iteration = 0; iteration < correctionIterations; iteration++)
                {
                    var correctedHighVoltage = EvaluateHighVoltageFromSignals(drivenInput, damping, bandGuessSignal, lowGuessSignal);
                    correctedHighSignal = _model.NodeVoltageToSignal(correctedHighVoltage);
                    var signalDelta = Math.Abs(correctedHighSignal - bandGuessSignal) + Math.Abs(bandGuessSignal - lowGuessSignal);
                    var step = substepBase * _model.MapVcrConductanceScale(cutoffRegister, signalDelta);
                    correctedBandSignal = SaturateAnalog(
                        startBandSignal + (0.5 * step * (startHighSignal + correctedHighSignal)),
                        _integratorSignalLimit);
                    correctedLowSignal = SaturateAnalog(
                        startLowSignal + (0.5 * step * (startBandSignal + correctedBandSignal)),
                        _integratorSignalLimit);
                    bandGuessSignal = correctedBandSignal;
                    lowGuessSignal = correctedLowSignal;
                }

                _bandVoltage = _model.SignalToNodeVoltage(correctedBandSignal);
                _lowVoltage = _model.SignalToNodeVoltage(correctedLowSignal);
                bandSignal = correctedBandSignal;
                lowSignal = correctedLowSignal;
                highVoltage = EvaluateHighVoltageFromSignals(drivenInput, damping, bandSignal, lowSignal);
            }

            _lastHighVoltage = highVoltage;
            _lastBandVoltage = _bandVoltage;
            _lastLowVoltage = _lowVoltage;
            var highSignal = _model.NodeVoltageToSignal(_lastHighVoltage);
            _lastLowDebug = _model.SignalVoltageToDebug(lowSignal);
            _lastBandDebug = _model.SignalVoltageToDebug(bandSignal);
            _lastHighDebug = _model.SignalVoltageToDebug(highSignal);

            return SelectFilterOutput(lowSignal, bandSignal, highSignal, filterMode) *
                parameters.FilterOutputGain *
                _model.MapResonanceOutputScale(resonanceNibble, cutoffRegister) *
                _model.MapSummerGain(filterMode) *
                _model.MapOutputGain(filterMode);
        }

        private int DetermineSubsteps(int cutoffRegister, int resonanceNibble)
        {
            var cutoffHz = _model.CutoffHz[Math.Clamp(cutoffRegister, 0, _model.CutoffHz.Length - 1)];
            if (resonanceNibble >= 13 && cutoffHz >= 5000.0)
            {
                return 4;
            }

            return resonanceNibble >= 10 || cutoffHz >= 6500.0 ? 2 : 1;
        }

        private double EvaluateHighVoltageFromSignals(double drivenInput, double damping, double bandSignal, double lowSignal)
        {
            return _model.MapOpAmpVoltageFromSignal((drivenInput - (bandSignal * damping)) - lowSignal);
        }

        private static void RouteSignal(double signalVoltage, bool routed, ref double direct, ref double filtered, ref int routedVoiceCount)
        {
            if (routed)
            {
                filtered += signalVoltage;
                routedVoiceCount++;
            }
            else
            {
                direct += signalVoltage;
            }
        }

        private double SelectFilterOutput(double low, double band, double high, int filterMode)
        {
            return filterMode switch
            {
                0x10 => low * _profile.LowPassGain,
                0x20 => band * _profile.BandPassGain,
                0x30 => (low * _profile.LowPassGain) + (band * _profile.BandPassGain),
                0x40 => high * _profile.HighPassGain,
                0x50 => (low * _profile.LowPassGain) + (high * _profile.HighPassGain),
                0x60 => (band * _profile.BandPassGain) + (high * _profile.HighPassGain),
                0x70 => (low * _profile.LowPassGain) + (band * _profile.BandPassGain) + (high * _profile.HighPassGain),
                _ => 0.0
            };
        }

        private static double SaturateAnalog(double value, double limit)
        {
            if (Math.Abs(value) <= limit * 0.5)
            {
                return value;
            }

            var normalized = value / limit;
            return value / Math.Sqrt(1.0 + (normalized * normalized));
        }
    }
}
