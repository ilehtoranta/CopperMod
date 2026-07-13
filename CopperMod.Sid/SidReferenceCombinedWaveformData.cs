using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Checked-in ReferenceMeasured combined-waveform calibration. These values
    /// are fitted to the pinned sidplayfp oracle and are provisional until a
    /// matching hardware capture replaces them.
    /// </summary>
    internal static class SidReferenceCombinedWaveformData
    {
        public const string Authority = "sidplayfp-emulator-derived";
        public const string SourceVersion = "sidplayfp 3.0.2-ucrt64";
        public const string SourceSha256 = "a08d4f24a4baab726b49ea41d7e0b33026d856d2fe687879054b653da0506e35";

        private static readonly SidCombinedWaveformCalibration[] Mos6581ByMask = BuildMos6581ByMask();

        // The 8580 combines the selected digital waveforms before its much more
        // linear DAC. Per-mask gains are still explicit so calibration does not
        // silently inherit the 6581 selector model.
        private static readonly SidCombinedWaveformCalibration[] Mos8580ByMask = BuildMos8580ByMask();
        private static readonly ushort[][] Mos6581Pulldown = BuildPulldownTables(SidChipModel.Mos6581);
        private static readonly ushort[][] Mos8580Pulldown = BuildPulldownTables(SidChipModel.Mos8580);

        public static SidCombinedWaveformCalibration Get(SidChipModel model, int waveformMask)
        {
            waveformMask &= 0xF0;
            var table = model == SidChipModel.Mos8580 ? Mos8580ByMask : Mos6581ByMask;
            return table[waveformMask];
        }

        public static bool TryGet(SidChipModel model, int waveformMask, out SidCombinedWaveformCalibration calibration)
        {
            waveformMask &= 0xF0;
            calibration = Get(model, waveformMask);
            return calibration.WaveformMask == waveformMask && calibration.ActiveWaveforms > 1;
        }

        public static uint ApplyPulldown(SidChipModel model, int waveformMask, uint value)
        {
            var tableIndex = PulldownTableIndex(waveformMask);
            if (tableIndex < 0)
            {
                return value & 0x0FFFu;
            }

            var tables = model == SidChipModel.Mos8580 ? Mos8580Pulldown : Mos6581Pulldown;
            return tables[tableIndex][value & 0x0FFFu];
        }

        private static SidCombinedWaveformCalibration[] BuildMos6581ByMask()
        {
            var table = new SidCombinedWaveformCalibration[256];
            Add(table, new(0x30, 2, 0.290000, -0.009000, 0x0F9F, 0x0021, 1));
            Add(table, new(0x50, 2, 0.400000, 0.000000, 0x0FFF, 0x0000, 0));
            Add(table, new(0x60, 2, 0.040000, -0.110000, 0x0F3F, 0x0021, 1));
            Add(table, new(0x70, 3, 0.105800, -0.018000, 0x0F1F, 0x0125, 2));
            Add(table, new(0x90, 2, 0.202400, 0.101200, 0x0ECF, 0x0001, 2));
            Add(table, new(0xA0, 2, 0.015000, 0.007500, 0x0EAF, 0x0001, 2));
            Add(table, new(0xB0, 3, 0.093104, 0.046552, 0x0E8F, 0x0001, 3));
            Add(table, new(0xC0, 2, 0.202400, 0.101200, 0x0E6F, 0x0001, 2));
            Add(table, new(0xD0, 3, 0.093104, 0.046552, 0x0E4F, 0x0001, 3));
            Add(table, new(0xE0, 3, 0.093104, 0.046552, 0x0E2F, 0x0001, 3));
            Add(table, new(0xF0, 4, 0.04282784, 0.02141392, 0x0E0F, 0x0001, 4));
            return table;
        }

        private static SidCombinedWaveformCalibration[] BuildMos8580ByMask()
        {
            var table = new SidCombinedWaveformCalibration[256];
            // The pulldown table supplies the 8580 combined-wave shape. A
            // second n-wave gain reduction double-counts that attenuation, so
            // the calibrated analog gain remains unity for every selector.
            Add(table, new(0x30, 2, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0x50, 2, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0x60, 2, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0x70, 3, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0x90, 2, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            // Noise+saw retains the sidplayfp writeback residue but reaches the
            // output mixer at roughly one quarter of the non-noise selectors.
            Add(table, new(0xA0, 2, 0.250000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0xB0, 3, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0xC0, 2, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0xD0, 3, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0xE0, 3, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            Add(table, new(0xF0, 4, 1.000000, 0.0, 0x0FFF, 0x0000, 0));
            return table;
        }

        private static void Add(SidCombinedWaveformCalibration[] table, SidCombinedWaveformCalibration calibration)
        {
            table[calibration.WaveformMask] = calibration;
        }

        private static ushort[][] BuildPulldownTables(SidChipModel model)
        {
            // Checked-in parameters from the pinned sidplayfp model. The five
            // lanes are T+S, P+T, P+S, P+T+S, and N+P. They remain tagged as
            // emulator-derived even though the upstream fit used SID samples.
            ReadOnlySpan<SidPulldownConfig> configs = model == SidChipModel.Mos8580
                ?
                [
                    new(SidPulldownDistance.Exponential, 0.853578329, 1.09615636, 0.0, 1.8819375, 6.80794907),
                    new(SidPulldownDistance.Exponential, 0.929835618, 1.0, 1.12836814, 1.10453653, 1.48065746),
                    new(SidPulldownDistance.Quadratic, 0.911938608, 0.996440411, 1.2278074, 0.000117214302, 0.18948476),
                    new(SidPulldownDistance.Exponential, 0.938004673, 1.04827631, 1.21178246, 0.915959001, 1.42698038),
                    new(SidPulldownDistance.Exponential, 0.950, 1.000, 1.150, 1.000, 1.450)
                ]
                :
                [
                    new(SidPulldownDistance.Exponential, 0.877322257, 1.11349654, 0.0, 2.14537621, 9.08618164),
                    new(SidPulldownDistance.Linear, 0.941692829, 1.0, 1.80072665, 0.033124879, 0.232303441),
                    new(SidPulldownDistance.Linear, 1.66494179, 1.03760982, 5.62705326, 0.291590303, 0.283631504),
                    new(SidPulldownDistance.Linear, 1.09762526, 0.975265801, 1.52196741, 0.151528224, 0.841949463),
                    new(SidPulldownDistance.Exponential, 0.960, 1.000, 2.500, 1.100, 1.200)
                ];

            var tables = new ushort[configs.Length][];
            for (var i = 0; i < configs.Length; i++)
            {
                tables[i] = BuildPulldownTable(configs[i]);
            }

            return tables;
        }

        private static ushort[] BuildPulldownTable(SidPulldownConfig config)
        {
            var distances = new float[25];
            distances[12] = 1.0f;
            for (var distance = 1; distance <= 12; distance++)
            {
                distances[12 - distance] = DistanceWeight(config.Distance, config.DistanceBelow, distance);
                distances[12 + distance] = DistanceWeight(config.Distance, config.DistanceAbove, distance);
            }

            var table = new ushort[4096];
            Span<float> bits = stackalloc float[12];
            for (var input = 0; input < table.Length; input++)
            {
                for (var bit = 0; bit < bits.Length; bit++)
                {
                    bits[bit] = (input & (1 << bit)) != 0 ? 1.0f : 0.0f;
                }

                bits[11] *= (float)config.TopBitStrength;
                var output = 0;
                for (var sourceBit = 0; sourceBit < bits.Length; sourceBit++)
                {
                    var pull = 0.0f;
                    var totalWeight = 0.0f;
                    for (var coupledBit = 0; coupledBit < bits.Length; coupledBit++)
                    {
                        if (coupledBit == sourceBit)
                        {
                            continue;
                        }

                        var weight = distances[sourceBit - coupledBit + 12];
                        pull += (1.0f - bits[coupledBit]) * weight;
                        totalWeight += weight;
                    }

                    var normalizedPull = (pull - (float)config.PulseStrength) / totalWeight;
                    var bitValue = bits[sourceBit] > 0.0f ? 1.0f - normalizedPull : 0.0f;
                    if (bitValue > (float)config.Threshold)
                    {
                        output |= 1 << sourceBit;
                    }
                }

                table[input] = (ushort)output;
            }

            return table;
        }

        private static float DistanceWeight(SidPulldownDistance kind, double distance, int bitDistance)
        {
            var value = (float)distance;
            return kind switch
            {
                SidPulldownDistance.Linear => 1.0f / (1.0f + (bitDistance * value)),
                SidPulldownDistance.Quadratic => 1.0f / (1.0f + (bitDistance * bitDistance * value)),
                _ => MathF.Pow(value, -bitDistance)
            };
        }

        private static int PulldownTableIndex(int waveformMask)
        {
            var selected = (waveformMask >> 4) & 0x07;
            return selected switch
            {
                0x03 => 0,
                0x04 when (waveformMask & 0x80) != 0 => 4,
                0x05 => 1,
                0x06 => 2,
                0x07 => 3,
                _ => -1
            };
        }
    }

    internal readonly record struct SidCombinedWaveformCalibration(
        int WaveformMask,
        int ActiveWaveforms,
        double Gain,
        double Bias,
        int RetentionMask,
        int WeakMask,
        int ContentionShift);

    internal readonly record struct SidPulldownConfig(
        SidPulldownDistance Distance,
        double Threshold,
        double TopBitStrength,
        double PulseStrength,
        double DistanceBelow,
        double DistanceAbove);

    internal enum SidPulldownDistance
    {
        Exponential,
        Linear,
        Quadratic
    }
}
