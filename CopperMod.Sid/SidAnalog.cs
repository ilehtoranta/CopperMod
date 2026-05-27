using System;

namespace CopperMod.Sid
{
    internal static class SidAnalog
    {
        private const int WaveformDacBits = 12;
        private const int WaveformDacSize = 1 << WaveformDacBits;
        private static readonly double[] Mos6581WaveformDac = BuildWaveformDac(SidChipModel.Mos6581);
        private static readonly double[] Mos8580WaveformDac = BuildWaveformDac(SidChipModel.Mos8580);
        private static readonly double[] Mos6581Envelope = BuildEnvelope(SidChipModel.Mos6581);
        private static readonly double[] Mos8580Envelope = BuildEnvelope(SidChipModel.Mos8580);
        private static readonly double[] Mos6581Volume = BuildVolume(SidChipModel.Mos6581);
        private static readonly double[] Mos8580Volume = BuildVolume(SidChipModel.Mos8580);
        private static readonly double[] Mos6581VolumeOffset = BuildVolumeOffset(SidChipModel.Mos6581);
        private static readonly double[] Mos8580VolumeOffset = BuildVolumeOffset(SidChipModel.Mos8580);

        public static double ConvertWaveformDac12(uint value, SidChipModel model)
        {
            return GetWaveformDac(model)[value & 0x0FFF];
        }

        public static double ConvertEnvelope(int envelope, SidChipModel model)
        {
            return GetEnvelope(model)[Math.Clamp(envelope, 0, 255)];
        }

        public static double ConvertVolume(int volume, SidChipModel model)
        {
            return GetVolume(model)[Math.Clamp(volume, 0, 15)];
        }

        public static double VolumeOffset(int volume, SidChipModel model)
        {
            return GetVolumeOffset(model)[Math.Clamp(volume, 0, 15)];
        }

        public static double VoiceMixGain(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? 0.32 : 0.33;
        }

        public static double CombinedWaveformScale(int activeWaveforms, SidChipModel model)
        {
            if (activeWaveforms <= 1)
            {
                return 1.0;
            }

            return model == SidChipModel.Mos8580
                ? Math.Pow(0.82, activeWaveforms - 1)
                : Math.Pow(0.58, activeWaveforms - 1);
        }

        public static double SoftClip(double sample)
        {
            var shaped = sample / (1.0 + (Math.Abs(sample) * 0.16));
            return Math.Clamp(shaped, -0.999, 0.999);
        }

        public static double OutputLowPassCutoffHz(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? 14_000.0 : 9_500.0;
        }

        private static double[] GetWaveformDac(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580WaveformDac : Mos6581WaveformDac;
        }

        private static double[] GetEnvelope(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580Envelope : Mos6581Envelope;
        }

        private static double[] GetVolume(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580Volume : Mos6581Volume;
        }

        private static double[] GetVolumeOffset(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580VolumeOffset : Mos6581VolumeOffset;
        }

        private static double[] BuildWaveformDac(SidChipModel model)
        {
            var table = new double[WaveformDacSize];
            if (model == SidChipModel.Mos8580)
            {
                for (var value = 0; value < table.Length; value++)
                {
                    table[value] = (value / 2047.5) - 1.0;
                }

                return table;
            }

            var weights = new double[WaveformDacBits];
            var max = 0.0;
            for (var bit = 0; bit < weights.Length; bit++)
            {
                var ideal = 1 << bit;
                var position = bit / (double)(WaveformDacBits - 1);
                var mismatch = 1.0 + ((position - 0.5) * 0.10) + (Math.Sin((bit + 1) * 1.73) * 0.018);
                weights[bit] = ideal * mismatch;
                max += weights[bit];
            }

            for (var value = 0; value < table.Length; value++)
            {
                var raw = 0.0;
                for (var bit = 0; bit < weights.Length; bit++)
                {
                    if ((value & (1 << bit)) != 0)
                    {
                        raw += weights[bit];
                    }
                }

                var normalized = raw / max;
                normalized = Math.Pow(normalized, 0.92);
                table[value] = Math.Clamp((normalized * 2.0) - 1.0, -1.0, 1.0);
            }

            return table;
        }

        private static double[] BuildEnvelope(SidChipModel model)
        {
            var table = new double[256];
            for (var value = 0; value < table.Length; value++)
            {
                var normalized = value / 255.0;
                table[value] = model == SidChipModel.Mos8580
                    ? normalized
                    : Math.Pow(normalized, 1.045);
            }

            return table;
        }

        private static double[] BuildVolume(SidChipModel model)
        {
            var table = new double[16];
            for (var value = 0; value < table.Length; value++)
            {
                var normalized = value / 15.0;
                table[value] = model == SidChipModel.Mos8580
                    ? normalized
                    : Math.Pow(normalized, 1.10);
            }

            return table;
        }

        private static double[] BuildVolumeOffset(SidChipModel model)
        {
            var table = new double[16];
            for (var value = 0; value < table.Length; value++)
            {
                var normalized = value / 15.0;
                table[value] = model == SidChipModel.Mos8580
                    ? (normalized - 0.5) * 0.018
                    : ((Math.Pow(normalized, 1.35) - 0.5) * 0.17) - 0.015;
            }

            return table;
        }
    }
}
