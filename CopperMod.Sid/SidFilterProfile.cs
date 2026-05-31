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
            double highPassGain = 1.0)
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
        }

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

        public double MapCutoff(int cutoffRegister)
        {
            var normalized = Math.Clamp(cutoffRegister / 2047.0, 0.0, 1.0);
            return MinCutoffHz + (Math.Pow(normalized, CutoffExponent) * (MaxCutoffHz - MinCutoffHz));
        }

        public double MapDamping(int resonanceNibble)
        {
            return MapDamping(resonanceNibble, 2047);
        }

        public double MapDamping(int resonanceNibble, int cutoffRegister)
        {
            var resonance = Math.Clamp(resonanceNibble, 0, 15) / 15.0;
            return Math.Clamp(BaseDamping - (resonance * ResonanceDamping), MinDamping, MaxDamping);
        }

        public double MapFilterOutputGain(int resonanceNibble, int cutoffRegister)
        {
            if (Id != SidFilterProfileId.Mos6581Balanced)
            {
                return FilterOutputGain;
            }

            var resonance = Math.Clamp(resonanceNibble, 0, 15) / 15.0;
            var lowCutoffWeight = 1.0 - Math.Clamp((Math.Clamp(cutoffRegister, 0, 2047) - 640.0) / 128.0, 0.0, 1.0);
            return FilterOutputGain * (1.0 + (Math.Pow(resonance, 2.0) * lowCutoffWeight * 1.20));
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

        private static readonly SidFilterProfileDefinition Mos6581DataSheet = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos6581DataSheet,
            minCutoffHz: 30.0,
            maxCutoffHz: 10000.0,
            cutoffExponent: 1.0,
            filterInputGain: 0.72,
            filterOutputGain: 0.92,
            baseDamping: 1.82,
            resonanceDamping: 1.20,
            minDamping: 0.45,
            maxDamping: 1.95,
            bandPassGain: 0.82);

        private static readonly SidFilterProfileDefinition Mos6581Balanced = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos6581Balanced,
            minCutoffHz: 25.0,
            maxCutoffHz: 11000.0,
            cutoffExponent: 1.55,
            filterInputGain: 0.72,
            filterOutputGain: 0.92,
            baseDamping: 1.82,
            resonanceDamping: 1.22,
            minDamping: 0.42,
            maxDamping: 1.95,
            bandPassGain: 0.90);

        private static readonly SidFilterProfileDefinition Mos6581DarkR3 = new SidFilterProfileDefinition(
            SidFilterProfileId.Mos6581DarkR3,
            minCutoffHz: 20.0,
            maxCutoffHz: 8500.0,
            cutoffExponent: 1.90,
            filterInputGain: 0.74,
            filterOutputGain: 0.96,
            baseDamping: 1.88,
            resonanceDamping: 1.34,
            minDamping: 0.36,
            maxDamping: 2.05,
            bandPassGain: 0.92);

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
            maxDamping: 1.95);
    }
}
