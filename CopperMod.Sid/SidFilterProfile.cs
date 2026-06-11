using System;

namespace CopperMod.Sid
{
    internal enum SidFilterProfileId
    {
        Auto,
        Mos6581DataSheet,
        Mos6581Balanced,
        Mos6581DarkR3,
        Mos8580Linear
    }

    internal sealed class SidFilterProfileDefinition
    {
        private SidFilterProfileDefinition(
            SidFilterProfileId id,
            double minCutoffHz,
            double maxCutoffHz,
            double cutoffExponent,
            double filterInputGain,
            double filterOutputGain,
            double baseDamping,
            double resonanceDamping,
            double minDamping,
            double maxDamping,
            double lowPassGain = 1.0,
            double bandPassGain = 1.0,
            double highPassGain = 1.0,
            double lowCutoffResonanceBoost = 0.0,
            double filterVoiceLeakageGain = 0.0,
            double[]? cutoffTable = null,
            double[]? dampingTable = null,
            bool usesNonlinearFilter = true,
            double filterDrive = 1.0,
            double filterInputSaturation = double.PositiveInfinity,
            double filterIntegratorLimit = double.PositiveInfinity,
            double filterOutputSaturation = double.PositiveInfinity,
            double cutoffSignalModulation = 0.0,
            SidMos6581AnalogParameters? analog6581Parameters = null)
        {
            Id = id;
            MinCutoffHz = minCutoffHz;
            MaxCutoffHz = maxCutoffHz;
            CutoffExponent = cutoffExponent;
            FilterInputGain = filterInputGain;
            FilterOutputGain = filterOutputGain;
            BaseDamping = baseDamping;
            ResonanceDamping = resonanceDamping;
            MinDamping = minDamping;
            MaxDamping = maxDamping;
            LowPassGain = lowPassGain;
            BandPassGain = bandPassGain;
            HighPassGain = highPassGain;
            LowCutoffResonanceBoost = lowCutoffResonanceBoost;
            FilterVoiceLeakageGain = filterVoiceLeakageGain;
            Analog6581Parameters = analog6581Parameters;
            Analog6581Model = analog6581Parameters == null ? null : new SidMos6581AnalogModel(analog6581Parameters);
            _cutoffTable = cutoffTable ?? Analog6581Model?.CutoffHz ?? BuildPowerCutoffTable(minCutoffHz, maxCutoffHz, cutoffExponent);
            _dampingTable = dampingTable ?? BuildLinearDampingTable(baseDamping, resonanceDamping, minDamping, maxDamping);
            UsesNonlinearFilter = usesNonlinearFilter;
            FilterDrive = filterDrive;
            FilterInputSaturation = filterInputSaturation;
            FilterIntegratorLimit = filterIntegratorLimit;
            FilterOutputSaturation = filterOutputSaturation;
            CutoffSignalModulation = cutoffSignalModulation;
        }

        private const int CutoffTableSize = 2048;
        private const int ResonanceTableSize = 16;
        private readonly double[] _cutoffTable;
        private readonly double[] _dampingTable;

        public SidFilterProfileId Id { get; }

        public double MinCutoffHz { get; }

        public double MaxCutoffHz { get; }

        public double CutoffExponent { get; }

        public double FilterInputGain { get; }

        public double FilterOutputGain { get; }

        public double BaseDamping { get; }

        public double ResonanceDamping { get; }

        public double MinDamping { get; }

        public double MaxDamping { get; }

        public double LowPassGain { get; }

        public double BandPassGain { get; }

        public double HighPassGain { get; }

        public double LowCutoffResonanceBoost { get; }

        public double FilterVoiceLeakageGain { get; }

        public bool UsesNonlinearFilter { get; }

        public bool UsesAnalog6581Filter => Analog6581Model != null;

        public double FilterDrive { get; }

        public double FilterInputSaturation { get; }

        public double FilterIntegratorLimit { get; }

        public double FilterOutputSaturation { get; }

        public double CutoffSignalModulation { get; }

        public int CutoffTableLength => _cutoffTable.Length;

        public int ResonanceTableLength => _dampingTable.Length;

        public SidMos6581AnalogParameters? Analog6581Parameters { get; }

        public SidMos6581AnalogModel? Analog6581Model { get; }

        public double MapCutoff(int cutoffRegister)
        {
            return _cutoffTable[Math.Clamp(cutoffRegister, 0, _cutoffTable.Length - 1)];
        }

        public double MapDamping(int resonanceNibble)
        {
            return MapDamping(resonanceNibble, 2047);
        }

        public double MapDamping(int resonanceNibble, int cutoffRegister)
        {
            return _dampingTable[Math.Clamp(resonanceNibble, 0, _dampingTable.Length - 1)];
        }

        public double MapFilterOutputGain(int resonanceNibble, int cutoffRegister)
        {
            if (Id != SidFilterProfileId.Mos6581Balanced)
            {
                return FilterOutputGain;
            }

            var resonance = Math.Clamp(resonanceNibble, 0, 15) / 15.0;
            var lowCutoffWeight = 1.0 - Math.Clamp((Math.Clamp(cutoffRegister, 0, 2047) - 640.0) / 128.0, 0.0, 1.0);
            return FilterOutputGain * (1.0 + (Math.Pow(resonance, 2.0) * lowCutoffWeight * LowCutoffResonanceBoost));
        }

        public double MapFilterVoiceLeakageGain(int cutoffRegister)
        {
            if (Id != SidFilterProfileId.Mos6581Balanced)
            {
                return FilterVoiceLeakageGain;
            }

            var highCutoffWeight = Math.Clamp((Math.Clamp(cutoffRegister, 0, 2047) - 768.0) / 384.0, 0.0, 1.0);
            return FilterVoiceLeakageGain * (0.30 + (0.70 * highCutoffWeight));
        }

        public static SidFilterProfileDefinition Resolve(SidChipModel model, SidFilterProfileId requested)
        {
            if (requested == SidFilterProfileId.Auto)
            {
                requested = model == SidChipModel.Mos8580
                    ? SidFilterProfileId.Mos8580Linear
                    : SidFilterProfileId.Mos6581Balanced;
            }

            if (model == SidChipModel.Mos8580 && requested != SidFilterProfileId.Mos8580Linear)
            {
                requested = SidFilterProfileId.Mos8580Linear;
            }

            return requested switch
            {
                SidFilterProfileId.Mos6581DataSheet => Mos6581DataSheet,
                SidFilterProfileId.Mos6581DarkR3 => Mos6581DarkR3,
                SidFilterProfileId.Mos8580Linear => Mos8580Linear,
                _ => Mos6581Balanced
            };
        }

        private static double[] BuildPowerCutoffTable(double minCutoffHz, double maxCutoffHz, double cutoffExponent)
        {
            var table = new double[CutoffTableSize];
            for (var register = 0; register < table.Length; register++)
            {
                var normalized = register / 2047.0;
                table[register] = minCutoffHz + (Math.Pow(normalized, cutoffExponent) * (maxCutoffHz - minCutoffHz));
            }

            return table;
        }

        private static double[] BuildAnchoredCutoffTable(params (int Register, double CutoffHz)[] anchors)
        {
            if (anchors.Length < 2 ||
                anchors[0].Register != 0 ||
                anchors[^1].Register != CutoffTableSize - 1)
            {
                throw new ArgumentException("Cutoff anchors must cover the complete SID cutoff register range.", nameof(anchors));
            }

            var table = new double[CutoffTableSize];
            var anchorIndex = 0;
            for (var register = 0; register < table.Length; register++)
            {
                while (anchorIndex < anchors.Length - 2 && register > anchors[anchorIndex + 1].Register)
                {
                    anchorIndex++;
                }

                var lower = anchors[anchorIndex];
                var upper = anchors[anchorIndex + 1];
                var span = Math.Max(1, upper.Register - lower.Register);
                var t = Math.Clamp((register - lower.Register) / (double)span, 0.0, 1.0);
                var shaped = (t * t) * (3.0 - (2.0 * t));
                var lowerLog = Math.Log(Math.Max(1.0, lower.CutoffHz));
                var upperLog = Math.Log(Math.Max(1.0, upper.CutoffHz));
                table[register] = Math.Exp(lowerLog + ((upperLog - lowerLog) * shaped));
            }

            table[0] = anchors[0].CutoffHz;
            table[^1] = anchors[^1].CutoffHz;
            return table;
        }

        private static double[] BuildLinearDampingTable(double baseDamping, double resonanceDamping, double minDamping, double maxDamping)
        {
            var table = new double[ResonanceTableSize];
            for (var resonanceNibble = 0; resonanceNibble < table.Length; resonanceNibble++)
            {
                var resonance = resonanceNibble / 15.0;
                table[resonanceNibble] = Math.Clamp(baseDamping - (resonance * resonanceDamping), minDamping, maxDamping);
            }

            return table;
        }

        private static double[] BuildDampingTable(params double[] values)
        {
            if (values.Length != ResonanceTableSize)
            {
                throw new ArgumentException("SID resonance tables must contain 16 entries.", nameof(values));
            }

            var table = new double[ResonanceTableSize];
            for (var i = 0; i < values.Length; i++)
            {
                table[i] = Math.Clamp(values[i], 0.05, 4.0);
            }

            return table;
        }

        private static readonly SidFilterProfileDefinition Mos6581DataSheet = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos6581DataSheet,
            minCutoffHz: 220.0,
            maxCutoffHz: 10000.0,
            cutoffExponent: 1.0,
            filterInputGain: 0.72,
            filterOutputGain: 0.92,
            baseDamping: 1.82,
            resonanceDamping: 1.20,
            minDamping: 0.45,
            maxDamping: 1.95,
            bandPassGain: 0.82,
            dampingTable: BuildDampingTable(
                1.82, 1.76, 1.69, 1.61,
                1.52, 1.43, 1.34, 1.24,
                1.15, 1.06, 0.97, 0.88,
                0.78, 0.69, 0.60, 0.50),
            filterDrive: 1.08,
            filterInputSaturation: 2.20,
            filterIntegratorLimit: 2.40,
            filterOutputSaturation: 2.10,
            cutoffSignalModulation: 0.035,
            analog6581Parameters: new SidMos6581AnalogParameters(
                cutoffCircuit: new SidMos6581CutoffCircuit(
                    minimumCutoffHz: 220.0,
                    fullScaleCutoffHz: 10000.0),
                voiceVoltageRange: 1.45,
                voiceDcVoltage: 5.00,
                filterInputGain: 0.68,
                filterOutputGain: 0.86,
                filterLeakageGain: 0.008,
                opAmpDrive: 1.05,
                opAmpOutputScale: 2.90,
                integratorGain: 1.00,
                vcrSignalModulation: 0.75,
                resonanceDampingScale: 1.05,
                resistorNetwork: SidMos6581ResistorNetwork.Default470Pf.WithTrims(
                    mixerDriveTrim: 0.94,
                    summerDriveTrim: 0.96,
                    resonanceFeedbackTrim: 0.93,
                    resonanceOutputTrim: 0.90,
                    outputGainTrim: 1.01)));

        private static readonly SidFilterProfileDefinition Mos6581Balanced = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos6581Balanced,
            minCutoffHz: 210.0,
            maxCutoffHz: 11000.0,
            cutoffExponent: 0.56,
            filterInputGain: 0.72,
            filterOutputGain: 1.025,
            baseDamping: 1.82,
            resonanceDamping: 1.22,
            minDamping: 0.42,
            maxDamping: 1.95,
            bandPassGain: 0.90,
            lowCutoffResonanceBoost: 0.50,
            filterVoiceLeakageGain: 0.025,
            dampingTable: BuildDampingTable(
                1.84, 1.78, 1.70, 1.60,
                1.49, 1.36, 1.22, 1.08,
                0.94, 0.81, 0.69, 0.58,
                0.50, 0.45, 0.43, 0.42),
            filterDrive: 1.18,
            filterInputSaturation: 1.75,
            filterIntegratorLimit: 2.00,
            filterOutputSaturation: 1.65,
            cutoffSignalModulation: 0.075,
            analog6581Parameters: new SidMos6581AnalogParameters(
                cutoffCircuit: new SidMos6581CutoffCircuit(
                    minimumCutoffHz: 210.0,
                    fullScaleCutoffHz: 11000.0),
                voiceVoltageRange: 1.50,
                voiceDcVoltage: 5.00,
                filterInputGain: 0.72,
                filterOutputGain: 0.95,
                filterLeakageGain: 0.018,
                opAmpDrive: 1.14,
                opAmpOutputScale: 2.70,
                integratorGain: 1.04,
                vcrSignalModulation: 0.95,
                resonanceDampingScale: 1.00,
                resistorNetwork: SidMos6581ResistorNetwork.Default470Pf.WithTrims(
                    mixerDriveTrim: 1.00,
                    summerDriveTrim: 1.00,
                    resonanceFeedbackTrim: 1.00,
                    resonanceOutputTrim: 1.00,
                    outputGainTrim: 1.00)));

        private static readonly SidFilterProfileDefinition Mos6581DarkR3 = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos6581DarkR3,
            minCutoffHz: 80.0,
            maxCutoffHz: 7500.0,
            cutoffExponent: 1.90,
            filterInputGain: 0.74,
            filterOutputGain: 0.96,
            baseDamping: 1.88,
            resonanceDamping: 1.34,
            minDamping: 0.36,
            maxDamping: 2.05,
            bandPassGain: 0.92,
            dampingTable: BuildDampingTable(
                1.92, 1.86, 1.78, 1.68,
                1.55, 1.41, 1.26, 1.10,
                0.94, 0.79, 0.65, 0.53,
                0.44, 0.39, 0.36, 0.36),
            filterDrive: 1.25,
            filterInputSaturation: 1.45,
            filterIntegratorLimit: 1.65,
            filterOutputSaturation: 1.45,
            cutoffSignalModulation: 0.11,
            analog6581Parameters: new SidMos6581AnalogParameters(
                cutoffCircuit: new SidMos6581CutoffCircuit(
                    minimumCutoffHz: 80.0,
                    fullScaleCutoffHz: 7500.0,
                    dacTwoRDivR: 2.60),
                voiceVoltageRange: 1.50,
                voiceDcVoltage: 5.00,
                filterInputGain: 0.74,
                filterOutputGain: 0.90,
                filterLeakageGain: 0.006,
                opAmpDrive: 1.24,
                opAmpOutputScale: 2.55,
                integratorGain: 0.88,
                vcrSignalModulation: 1.15,
                resonanceDampingScale: 1.08,
                resistorNetwork: SidMos6581ResistorNetwork.Default470Pf.WithTrims(
                    mixerDriveTrim: 1.05,
                    summerDriveTrim: 1.03,
                    resonanceFeedbackTrim: 1.08,
                    resonanceOutputTrim: 1.06,
                    outputGainTrim: 0.98)));

        private static readonly SidFilterProfileDefinition Mos8580Linear = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos8580Linear,
            minCutoffHz: 35.0,
            maxCutoffHz: 14500.0,
            cutoffExponent: 1.10,
            filterInputGain: 0.72,
            filterOutputGain: 0.92,
            baseDamping: 1.62,
            resonanceDamping: 1.10,
            minDamping: 0.42,
            maxDamping: 1.95,
            usesNonlinearFilter: false);
    }
}
