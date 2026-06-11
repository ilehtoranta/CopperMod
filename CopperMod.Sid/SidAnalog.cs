using System;

namespace CopperMod.Sid
{
    internal static class SidAnalog
    {
        private static readonly SidAnalogProfile Mos6581Profile = BuildProfile(SidChipModel.Mos6581);
        private static readonly SidAnalogProfile Mos8580Profile = BuildProfile(SidChipModel.Mos8580);
        private static readonly double[] Mos6581WaveformDac = BuildWaveformDac(SidChipModel.Mos6581);
        private static readonly double[] Mos8580WaveformDac = BuildWaveformDac(SidChipModel.Mos8580);
        private static readonly double[] Mos6581Envelope = BuildEnvelope(SidChipModel.Mos6581);
        private static readonly double[] Mos8580Envelope = BuildEnvelope(SidChipModel.Mos8580);
        private static readonly ushort[][] Mos6581CombinedWaveformDac = BuildMos6581CombinedWaveformDacTables();
        private static readonly double[] Mos6581CombinedWaveformGain = BuildMos6581CombinedWaveformGain();
        private static readonly double[] Mos6581CombinedWaveformBias = BuildMos6581CombinedWaveformBias();

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
            return GetProfile(model).VolumeGain[Math.Clamp(volume, 0, 15)];
        }

        public static double VolumeOffset(int volume, SidChipModel model)
        {
            return GetProfile(model).VolumeDc[Math.Clamp(volume, 0, 15)];
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
                : Math.Pow(0.34, activeWaveforms - 1);
        }

        public static bool UsesCombinedWaveformTable(int waveformMask, SidChipModel model)
        {
            return model == SidChipModel.Mos6581 &&
                waveformMask != 0x50 &&
                Mos6581CombinedWaveformDac[waveformMask & 0xF0] != null;
        }

        public static uint MapCombinedWaveformDac12(uint value, int waveformMask, SidChipModel model)
        {
            var table = model == SidChipModel.Mos6581
                ? Mos6581CombinedWaveformDac[waveformMask & 0xF0]
                : null;
            return table == null ? value & 0x0FFFu : table[value & 0x0FFFu];
        }

        public static double ConvertCombinedWaveformDac12(uint value, int waveformMask, SidChipModel model)
        {
            if (model != SidChipModel.Mos6581)
            {
                return ConvertWaveformDac12(value, model) *
                    CombinedWaveformScale(CountSelectedWaveforms(waveformMask), model);
            }

            waveformMask &= 0xF0;
            return Math.Clamp(
                (ConvertWaveformDac12(value, model) * Mos6581CombinedWaveformGain[waveformMask]) +
                    Mos6581CombinedWaveformBias[waveformMask],
                -1.0,
                1.0);
        }

        public static double SoftClip(double sample)
        {
            var shaped = sample / (1.0 + (Math.Abs(sample) * 0.16));
            return Math.Clamp(shaped, -0.999, 0.999);
        }

        public static double OutputLowPassCutoffHz(SidChipModel model)
        {
            return GetProfile(model).ChipOutputLowPassCutoffHz;
        }

        public static double VolumeRegisterTransientGain(SidChipModel model)
        {
            return GetProfile(model).VolumeStepTransientGain;
        }

        public static double VolumeRegisterTransientLimit(SidChipModel model)
        {
            return GetProfile(model).VolumeStepTransientLimit;
        }

        public static double VolumeRegisterTransientSlew(SidChipModel model, int cpuCyclesPerSecond)
        {
            var attackSeconds = GetProfile(model).VolumeStepAttackSeconds;
            if (attackSeconds <= 0.0)
            {
                return 1.0;
            }

            var clock = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            return 1.0 - Math.Exp(-1.0 / (clock * attackSeconds));
        }

        public static double VolumeRegisterTransientDecay(SidChipModel model, int cpuCyclesPerSecond)
        {
            var decaySeconds = GetProfile(model).VolumeStepDecaySeconds;
            if (decaySeconds <= 0.0)
            {
                return 0.0;
            }

            var clock = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            return Math.Exp(-1.0 / (clock * decaySeconds));
        }

        private static double[] GetWaveformDac(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580WaveformDac : Mos6581WaveformDac;
        }

        private static double[] GetEnvelope(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580Envelope : Mos6581Envelope;
        }

        private static SidAnalogProfile GetProfile(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580Profile : Mos6581Profile;
        }

        private static double[] BuildWaveformDac(SidChipModel model)
        {
            // The SID waveform DACs are R-2R ladder networks. The 6581 has a
            // mismatched R-2R ratio and lacks the bit-0 termination resistor, so
            // its lower DAC bits are audibly nonlinear. The 8580 is much closer
            // to an ideal, terminated ladder.
            bool is6581 = model != SidChipModel.Mos8580;
            double ratio = is6581 ? 2.25 : 2.00;
            bool term = !is6581;
            double leakage = is6581 ? 0.0075 : 0.0035;

            double[] normalized = BuildR2RDacTable(12, ratio, term, leakage);

            double waveMin = normalized[0x000];
            double waveMax = normalized[0xFFF];
            double scale = waveMax - waveMin;

            var table = new double[1 << 12];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = Math.Clamp((((normalized[i] - waveMin) / scale) * 2.0) - 1.0, -1.0, 1.0);
            }

            return table;
        }

        private static double[] BuildEnvelope(SidChipModel model)
        {
            bool is6581 = model != SidChipModel.Mos8580;
            double[] normalized = BuildR2RDacTable(
                8,
                is6581 ? 2.20 : 2.00,
                !is6581,
                is6581 ? 0.0075 : 0.0035);

            double max = normalized[255];
            var table = new double[256];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = normalized[i] / max;
            }

            return table;
        }

        private static ushort[][] BuildMos6581CombinedWaveformDacTables()
        {
            var tables = new ushort[256][];
            ReadOnlySpan<int> masks = stackalloc[] { 0x30, 0x60, 0x70, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0 };
            for (var i = 0; i < masks.Length; i++)
            {
                tables[masks[i]] = BuildMos6581CombinedWaveformDacTable(masks[i]);
            }

            return tables;
        }

        private static ushort[] BuildMos6581CombinedWaveformDacTable(int waveformMask)
        {
            var activeWaveforms = CountSelectedWaveforms(waveformMask);
            var hasNoise = (waveformMask & 0x80) != 0;
            var table = new ushort[1 << 12];
            var shift = Math.Clamp(activeWaveforms - 1 + (hasNoise ? 1 : 0), 1, 4);
            var retentionMask = GetMos6581CombinedRetentionMask(waveformMask);
            var weakMask = GetMos6581CombinedWeakMask(waveformMask);
            for (var dac = 0; dac < table.Length; dac++)
            {
                var value = (uint)dac;
                var adjacentLeakage = ((value << 1) | (value >> 1)) & (uint)weakMask;
                var contention = ((value >> shift) | adjacentLeakage) & (uint)retentionMask;
                if (hasNoise)
                {
                    contention &= (value | (value >> 2) | 0x001u) & 0x0FFFu;
                }

                table[dac] = (ushort)(contention & 0x0FFFu);
            }

            return table;
        }

        private static double[] BuildMos6581CombinedWaveformGain()
        {
            var gain = new double[256];
            for (var mask = 0; mask < gain.Length; mask += 0x10)
            {
                var active = CountSelectedWaveforms(mask);
                gain[mask] = active <= 1 ? 1.0 : Math.Pow(0.46, active - 1);
                if ((mask & 0x80) != 0 && active > 1)
                {
                    gain[mask] *= 0.88;
                }
            }

            return gain;
        }

        private static double[] BuildMos6581CombinedWaveformBias()
        {
            var bias = new double[256];
            for (var mask = 0; mask < bias.Length; mask += 0x10)
            {
                var active = CountSelectedWaveforms(mask);
                if (active <= 1)
                {
                    continue;
                }

                bias[mask] = -0.018 * (active - 1);
                if ((mask & 0x80) != 0)
                {
                    bias[mask] -= 0.010;
                }
            }

            return bias;
        }

        private static int GetMos6581CombinedRetentionMask(int waveformMask)
        {
            var mask = 0x0FFF;
            if ((waveformMask & 0x10) != 0)
            {
                mask &= 0x0FDF;
            }

            if ((waveformMask & 0x20) != 0)
            {
                mask &= 0x0FBF;
            }

            if ((waveformMask & 0x40) != 0)
            {
                mask &= 0x0F7F;
            }

            if ((waveformMask & 0x80) != 0)
            {
                mask &= 0x0EEF;
            }

            return mask;
        }

        private static int GetMos6581CombinedWeakMask(int waveformMask)
        {
            var active = CountSelectedWaveforms(waveformMask);
            var mask = active >= 3 ? 0x0125 : 0x0021;
            if ((waveformMask & 0x80) != 0)
            {
                mask &= 0x0011;
            }

            return mask;
        }

        private static int CountSelectedWaveforms(int waveformMask)
        {
            waveformMask = (waveformMask >> 4) & 0x0F;
            var count = 0;
            while (waveformMask != 0)
            {
                count += waveformMask & 1;
                waveformMask >>= 1;
            }

            return count;
        }

        private static SidAnalogProfile BuildProfile(SidChipModel model)
        {
            bool is6581 = model != SidChipModel.Mos8580;
            var volumeGain = BuildVolumeGain(model);
            var volumeDc = BuildVolumeDc(
                volumeGain,
                is6581 ? -0.015 : 0.0,
                is6581 ? 0.30 : 0.017);

            return new SidAnalogProfile(
                volumeGain,
                volumeDc,
                volumeStepTransientGain: is6581 ? 3.40 : 0.0,
                volumeStepTransientLimit: is6581 ? 0.62 : 0.0,
                volumeStepAttackSeconds: is6581 ? 0.00024 : 0.0,
                volumeStepDecaySeconds: is6581 ? 0.0030 : 0.0,
                chipOutputLowPassCutoffHz: is6581 ? 22_000.0 : 14_000.0);
        }

        private static double[] BuildVolumeGain(SidChipModel model)
        {
            var table = new double[16];
            for (var i = 0; i < table.Length; i++)
            {
                var normalized = i / 15.0;
                table[i] = model == SidChipModel.Mos8580
                    ? normalized
                    : Math.Pow(normalized, 1.10);
            }

            return table;
        }

        private static double[] BuildVolumeDc(double[] volumeGain, double dcBias, double dcRange)
        {
            var table = new double[16];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = dcBias + ((volumeGain[i] - 0.5) * dcRange);
            }

            return table;
        }

        // Compute normalized output voltages for an n-bit R-2R ladder DAC using
        // repeated Thevenin equivalent substitution. This is standard circuit
        // analysis, not implementation-derived tuning.
        private static double[] BuildR2RDacTable(int bits, double twoRDivR, bool termination, double leakage)
        {
            var vbit = new double[bits];
            for (var setBit = 0; setBit < bits; setBit++)
            {
                var vn = 1.0;
                var r = 1.0;
                var twoR = twoRDivR * r;
                var rTail = termination ? twoR : double.PositiveInfinity;

                for (var bit = 0; bit < setBit; bit++)
                {
                    rTail = double.IsInfinity(rTail)
                        ? r + twoR
                        : r + (twoR * rTail / (twoR + rTail));
                }

                if (double.IsInfinity(rTail))
                {
                    rTail = twoR;
                }
                else
                {
                    var rParallel = twoR * rTail / (twoR + rTail);
                    vn *= rParallel / twoR;
                    rTail = rParallel;
                }

                for (var bit = setBit + 1; bit < bits; bit++)
                {
                    rTail += r;
                    var i = vn / rTail;
                    rTail = twoR * rTail / (twoR + rTail);
                    vn = rTail * i;
                }

                vbit[setBit] = vn;
            }

            var size = 1 << bits;
            var table = new double[size];
            var maxVal = 0.0;
            for (var code = 0; code < size; code++)
            {
                var vo = 0.0;
                for (var j = 0; j < bits; j++)
                {
                    vo += ((code & (1 << j)) != 0 ? 1.0 : leakage) * vbit[j];
                }

                table[code] = vo;
                if (vo > maxVal)
                {
                    maxVal = vo;
                }
            }

            for (var code = 0; code < size; code++)
            {
                table[code] /= maxVal;
            }

            return table;
        }

        private sealed class SidAnalogProfile
        {
            public SidAnalogProfile(
                double[] volumeGain,
                double[] volumeDc,
                double volumeStepTransientGain,
                double volumeStepTransientLimit,
                double volumeStepAttackSeconds,
                double volumeStepDecaySeconds,
                double chipOutputLowPassCutoffHz)
            {
                VolumeGain = volumeGain;
                VolumeDc = volumeDc;
                VolumeStepTransientGain = volumeStepTransientGain;
                VolumeStepTransientLimit = volumeStepTransientLimit;
                VolumeStepAttackSeconds = volumeStepAttackSeconds;
                VolumeStepDecaySeconds = volumeStepDecaySeconds;
                ChipOutputLowPassCutoffHz = chipOutputLowPassCutoffHz;
            }

            public double[] VolumeGain { get; }

            public double[] VolumeDc { get; }

            public double VolumeStepTransientGain { get; }

            public double VolumeStepTransientLimit { get; }

            public double VolumeStepAttackSeconds { get; }

            public double VolumeStepDecaySeconds { get; }

            public double ChipOutputLowPassCutoffHz { get; }
        }
    }
}
