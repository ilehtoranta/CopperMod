using System;

namespace CopperMod.Sid
{
    internal static class SidAnalog
    {
        private const double Mos6581D418DcRange = 0.300;
        private const double Mos8580D418LowNibbleDcRange = 0.017;

        private static readonly double[] Mos6581D418Amplitude = BuildMos6581D418AmplitudeTable();
        private static readonly double[] Mos8580D418Amplitude = BuildMos8580D418AmplitudeTable();
        private static readonly SidAnalogProfile Mos6581BalancedProfile = BuildProfile(SidChipModel.Mos6581, SidEmulationProfile.Balanced);
        private static readonly SidAnalogProfile Mos8580BalancedProfile = BuildProfile(SidChipModel.Mos8580, SidEmulationProfile.Balanced);
        private static readonly SidAnalogProfile Mos6581ReferenceProfile = BuildProfile(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
        private static readonly SidAnalogProfile Mos8580ReferenceProfile = BuildProfile(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured);
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

        public static double ConvertVolume(int volume, SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            return GetProfile(model, sidEmulationProfile).VolumeGain[Math.Clamp(volume, 0, 15)];
        }

        public static double VolumeOffset(int volume, SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            var profile = GetProfile(model, sidEmulationProfile);
            return profile.VolumeDc[Math.Clamp(volume, 0, profile.VolumeDc.Length - 1)];
        }

        public static int Mos6581D418AmplitudeTableLength => Mos6581D418Amplitude.Length;

        public static double Mos6581D418MeasuredAmplitude(int registerValue)
        {
            return Mos6581D418Amplitude[registerValue & 0xFF];
        }

        public static int Mos8580D418AmplitudeTableLength => Mos8580D418Amplitude.Length;

        public static double Mos8580D418MeasuredAmplitude(int registerValue)
        {
            return Mos8580D418Amplitude[registerValue & 0xFF];
        }

        public static int D418TransitionMatrixLength => SidD418TransitionMatrices.MatrixLength;

        public static double Mos6581D418TransitionPreWriteAmplitude(int previousRegisterValue, int nextRegisterValue)
        {
            return SidD418TransitionMatrices.GetMos6581PreWrite(previousRegisterValue, nextRegisterValue);
        }

        public static double Mos6581D418TransitionPostWriteAmplitude(int previousRegisterValue, int nextRegisterValue)
        {
            return SidD418TransitionMatrices.GetMos6581PostWrite(previousRegisterValue, nextRegisterValue);
        }

        public static double Mos8580D418TransitionPreWriteAmplitude(int previousRegisterValue, int nextRegisterValue)
        {
            return SidD418TransitionMatrices.GetMos8580PreWrite(previousRegisterValue, nextRegisterValue);
        }

        public static double Mos8580D418TransitionPostWriteAmplitude(int previousRegisterValue, int nextRegisterValue)
        {
            return SidD418TransitionMatrices.GetMos8580PostWrite(previousRegisterValue, nextRegisterValue);
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

        public static double OutputLowPassCutoffHz(SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            return GetProfile(model, sidEmulationProfile).ChipOutputLowPassCutoffHz;
        }

        public static double VolumeRegisterTransientGain(SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            return GetProfile(model, sidEmulationProfile).VolumeStepTransientGain;
        }

        public static double VolumeRegisterTransientLimit(SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            return GetProfile(model, sidEmulationProfile).VolumeStepTransientLimit;
        }

        public static double VolumeRegisterTransientAttackSeconds(SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            return GetProfile(model, sidEmulationProfile).VolumeStepAttackSeconds;
        }

        public static double VolumeRegisterTransientSlew(SidChipModel model, int cpuCyclesPerSecond, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            var attackSeconds = GetProfile(model, sidEmulationProfile).VolumeStepAttackSeconds;
            if (attackSeconds <= 0.0)
            {
                return 1.0;
            }

            var clock = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            return 1.0 - Math.Exp(-1.0 / (clock * attackSeconds));
        }

        public static double VolumeRegisterTransientDecaySeconds(SidChipModel model, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            return GetProfile(model, sidEmulationProfile).VolumeStepDecaySeconds;
        }

        public static double VolumeRegisterTransientDecay(SidChipModel model, int cpuCyclesPerSecond, SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            var decaySeconds = GetProfile(model, sidEmulationProfile).VolumeStepDecaySeconds;
            if (decaySeconds <= 0.0)
            {
                return 0.0;
            }

            var clock = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            return Math.Exp(-1.0 / (clock * decaySeconds));
        }

        public static double D418TransitionTransient(
            int previousRegisterValue,
            int nextRegisterValue,
            SidChipModel model,
            SidEmulationProfile sidEmulationProfile)
        {
            if (sidEmulationProfile != SidEmulationProfile.ReferenceMeasured ||
                (previousRegisterValue & 0xFF) == (nextRegisterValue & 0xFF))
            {
                return 0.0;
            }

            var profile = GetProfile(model, sidEmulationProfile);
            var measuredPostWriteAmplitude = D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue, model);
            var settledTargetAmplitude = D418MeasuredAmplitude(nextRegisterValue, model);
            var impulse = D418AmplitudeDeltaToVolumeOffset(measuredPostWriteAmplitude - settledTargetAmplitude, model);
            return Math.Clamp(impulse, -profile.VolumeStepTransientLimit, profile.VolumeStepTransientLimit);
        }

        public static double FilterRoutingTransient(
            int previousRouting,
            int nextRouting,
            int currentMode,
            SidChipModel model,
            SidEmulationProfile sidEmulationProfile)
        {
            if (sidEmulationProfile != SidEmulationProfile.ReferenceMeasured)
            {
                return 0.0;
            }

            previousRouting &= 0x07;
            nextRouting &= 0x07;
            var changed = previousRouting ^ nextRouting;
            if (changed == 0)
            {
                return 0.0;
            }

            var profile = GetProfile(model, sidEmulationProfile);
            var routedIn = CountBits(nextRouting & changed);
            var routedOut = CountBits(previousRouting & changed);
            var polarity = routedIn == routedOut ? 1.0 : Math.Sign(routedIn - routedOut);
            var modeWeight = (currentMode & 0x70) == 0 ? 0.62 : 1.0;
            var impulse = polarity * CountBits(changed) * profile.FilterRoutingTransientGain * modeWeight;
            return Math.Clamp(impulse, -profile.VolumeStepTransientLimit, profile.VolumeStepTransientLimit);
        }

        public static double FilterModeTransient(
            int previousD418,
            int nextD418,
            SidChipModel model,
            SidEmulationProfile sidEmulationProfile)
        {
            if (sidEmulationProfile != SidEmulationProfile.ReferenceMeasured)
            {
                return 0.0;
            }

            var previousMode = previousD418 & 0x70;
            var nextMode = nextD418 & 0x70;
            var modeChanged = previousMode != nextMode;
            var voice3MuteChanged = ((previousD418 ^ nextD418) & 0x80) != 0;
            if (!modeChanged && !voice3MuteChanged)
            {
                return 0.0;
            }

            var profile = GetProfile(model, sidEmulationProfile);
            var selectedDelta = CountBits(nextMode) - CountBits(previousMode);
            var modePolarity = selectedDelta == 0 ? Math.Sign(nextMode - previousMode) : Math.Sign(selectedDelta);
            var modeImpulse = modeChanged ? modePolarity * profile.FilterModeTransientGain : 0.0;
            var voice3Impulse = voice3MuteChanged
                ? (((nextD418 & 0x80) != 0 ? -1.0 : 1.0) * profile.Voice3MuteTransientGain)
                : 0.0;
            return Math.Clamp(modeImpulse + voice3Impulse, -profile.VolumeStepTransientLimit, profile.VolumeStepTransientLimit);
        }

        private static double[] GetWaveformDac(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580WaveformDac : Mos6581WaveformDac;
        }

        private static double[] GetEnvelope(SidChipModel model)
        {
            return model == SidChipModel.Mos8580 ? Mos8580Envelope : Mos6581Envelope;
        }

        private static SidAnalogProfile GetProfile(SidChipModel model, SidEmulationProfile sidEmulationProfile)
        {
            if (sidEmulationProfile == SidEmulationProfile.ReferenceMeasured)
            {
                return model == SidChipModel.Mos8580 ? Mos8580ReferenceProfile : Mos6581ReferenceProfile;
            }

            return model == SidChipModel.Mos8580 ? Mos8580BalancedProfile : Mos6581BalancedProfile;
        }

        private static double D418MeasuredAmplitude(int registerValue, SidChipModel model)
        {
            return model == SidChipModel.Mos8580
                ? Mos8580D418MeasuredAmplitude(registerValue)
                : Mos6581D418MeasuredAmplitude(registerValue);
        }

        private static double D418TransitionPostWriteAmplitude(int previousRegisterValue, int nextRegisterValue, SidChipModel model)
        {
            return model == SidChipModel.Mos8580
                ? Mos8580D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue)
                : Mos6581D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue);
        }

        private static double D418AmplitudeDeltaToVolumeOffset(double amplitudeDelta, SidChipModel model)
        {
            return model == SidChipModel.Mos8580
                ? amplitudeDelta * Mos8580D418DcScale()
                : amplitudeDelta * Mos6581D418DcRange;
        }

        private static double Mos8580D418DcScale()
        {
            return Mos8580D418LowNibbleDcRange / (Mos8580D418Amplitude[0x0F] - Mos8580D418Amplitude[0x00]);
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

        private static int CountBits(int value)
        {
            value &= 0xFF;
            var count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private static SidAnalogProfile BuildProfile(SidChipModel model, SidEmulationProfile sidEmulationProfile)
        {
            bool is6581 = model != SidChipModel.Mos8580;
            bool referenceMeasured = sidEmulationProfile == SidEmulationProfile.ReferenceMeasured;
            var volumeGain = BuildVolumeGain(model);
            var volumeDc = is6581
                ? BuildMos6581MeasuredD418Offset()
                : BuildMos8580MeasuredD418Offset();

            return new SidAnalogProfile(
                volumeGain,
                volumeDc,
                volumeStepTransientGain: is6581 ? (referenceMeasured ? 3.65 : 3.40) : (referenceMeasured ? 0.18 : 0.0),
                volumeStepTransientLimit: is6581 ? (referenceMeasured ? 0.70 : 0.62) : (referenceMeasured ? 0.055 : 0.0),
                volumeStepAttackSeconds: is6581 ? (referenceMeasured ? SidD418TransitionMatrices.Mos6581TransientAttackSeconds : 0.00024) : (referenceMeasured ? SidD418TransitionMatrices.Mos8580TransientAttackSeconds : 0.0),
                volumeStepDecaySeconds: is6581 ? (referenceMeasured ? SidD418TransitionMatrices.Mos6581TransientDecaySeconds : 0.0030) : (referenceMeasured ? SidD418TransitionMatrices.Mos8580TransientDecaySeconds : 0.0),
                chipOutputLowPassCutoffHz: is6581 ? (referenceMeasured ? 24_000.0 : 22_000.0) : 14_000.0,
                d418TransitionOffsetGain: is6581 ? (referenceMeasured ? 0.36 : 0.0) : (referenceMeasured ? 0.11 : 0.0),
                d418TransitionAmplitudeGain: is6581 ? (referenceMeasured ? 0.030 : 0.0) : (referenceMeasured ? 0.0045 : 0.0),
                d418HighNibbleTransitionGain: is6581 ? (referenceMeasured ? 0.018 : 0.0) : (referenceMeasured ? 0.0025 : 0.0),
                filterRoutingTransientGain: is6581 ? (referenceMeasured ? 0.012 : 0.0) : (referenceMeasured ? 0.0020 : 0.0),
                filterModeTransientGain: is6581 ? (referenceMeasured ? 0.015 : 0.0) : (referenceMeasured ? 0.0025 : 0.0),
                voice3MuteTransientGain: is6581 ? (referenceMeasured ? 0.008 : 0.0) : (referenceMeasured ? 0.0015 : 0.0));
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

        private static double[] BuildMos6581D418AmplitudeTable()
        {
            return new[]
            {
                0.000086, 0.078433, 0.156209, 0.231024, 0.307690, 0.378565, 0.448696, 0.515601,
                0.603308, 0.665300, 0.726412, 0.784362, 0.843365, 0.897183, 0.950162, 1.000000,
                -0.004179, -0.024556, -0.044544, -0.063066, -0.081521, -0.098525, -0.115272, -0.130963,
                -0.151602, -0.166017, -0.180182, -0.193473, -0.206888, -0.219394, -0.231693, -0.243400,
                -0.000065, 0.078117, 0.155747, 0.230281, 0.306572, 0.377107, 0.446788, 0.513234,
                0.600298, 0.661856, 0.722471, 0.779941, 0.838519, 0.891821, 0.944257, 0.993670,
                -0.003885, -0.014557, -0.025069, -0.034769, -0.044310, -0.053237, -0.062063, -0.070251,
                -0.081120, -0.088694, -0.096163, -0.103111, -0.110014, -0.116566, -0.123005, -0.129079,
                -0.000598, 0.062600, 0.125077, 0.184902, 0.246071, 0.302364, 0.357843, 0.410717,
                0.480002, 0.528896, 0.576933, 0.622526, 0.669122, 0.711453, 0.753099, 0.792612,
                -0.004378, -0.025033, -0.045230, -0.063962, -0.082550, -0.099711, -0.116526, -0.132227,
                -0.153041, -0.167405, -0.181593, -0.194927, -0.208246, -0.220703, -0.233013, -0.244547,
                -0.000729, 0.064084, 0.128097, 0.189383, 0.251961, 0.309507, 0.366175, 0.420215,
                0.490810, 0.540626, 0.589607, 0.636005, 0.683332, 0.726454, 0.768713, 0.808805,
                -0.004135, -0.016167, -0.027995, -0.038889, -0.049613, -0.059619, -0.069502, -0.078647,
                -0.090824, -0.099261, -0.107537, -0.115289, -0.122993, -0.130225, -0.137417, -0.144156,
                -0.003176, -0.002153, -0.001294, -0.000331, 0.000786, 0.001658, 0.002413, 0.003247,
                0.004569, 0.005197, 0.005928, 0.006592, 0.007450, 0.008068, 0.008657, 0.009212,
                -0.008038, -0.084726, -0.158325, -0.226628, -0.294774, -0.356368, -0.416198, -0.472369,
                -0.547075, -0.598059, -0.647868, -0.694971, -0.742692, -0.786411, -0.829293, -0.869965,
                -0.002986, 0.007787, 0.018239, 0.028265, 0.038614, 0.047909, 0.057031, 0.065750,
                0.077309, 0.085296, 0.093201, 0.100736, 0.108584, 0.115538, 0.122572, 0.129133,
                -0.007022, -0.067578, -0.125870, -0.180039, -0.233970, -0.282941, -0.330587, -0.375266,
                -0.434452, -0.475052, -0.514810, -0.552379, -0.590348, -0.625363, -0.659726, -0.692313,
                -0.003658, -0.007161, -0.010732, -0.013928, -0.016903, -0.019846, -0.022738, -0.025347,
                -0.028787, -0.031180, -0.033594, -0.035799, -0.037789, -0.039903, -0.041917, -0.043782,
                -0.007618, -0.076203, -0.142110, -0.203299, -0.264221, -0.319423, -0.373066, -0.423379,
                -0.490150, -0.535812, -0.580477, -0.622698, -0.665400, -0.704636, -0.743203, -0.779710,
                -0.003445, 0.002169, 0.007504, 0.012721, 0.018201, 0.023043, 0.027707, 0.032291,
                0.038210, 0.042398, 0.046443, 0.050408, 0.054599, 0.058295, 0.061859, 0.065418,
                -0.006899, -0.062483, -0.115990, -0.165688, -0.215183, -0.260132, -0.303890, -0.344897,
                -0.399160, -0.436414, -0.472844, -0.507314, -0.542126, -0.574173, -0.605750, -0.635317
            };
        }

        private static double[] BuildMos8580D418AmplitudeTable()
        {
            return new[]
            {
                0.296841, 0.342174, 0.388113, 0.433498, 0.482807, 0.528693, 0.574186, 0.619396,
                0.676467, 0.722504, 0.767631, 0.812965, 0.863225, 0.908043, 0.953330, 1.000000,
                0.296585, 0.253814, 0.211532, 0.169380, 0.124805, 0.083160, 0.041534, 0.000451,
                -0.049665, -0.090874, -0.131023, -0.171055, -0.214557, -0.254048, -0.293259, -0.332582,
                0.296836, 0.341514, 0.386523, 0.431586, 0.480032, 0.524722, 0.570275, 0.614677,
                0.670630, 0.715622, 0.760787, 0.805202, 0.853724, 0.899193, 0.943526, 0.988241,
                0.296460, 0.253930, 0.211830, 0.169796, 0.125264, 0.083848, 0.042654, 0.001443,
                -0.048578, -0.089090, -0.129681, -0.169480, -0.212023, -0.252196, -0.291202, -0.330199,
                0.296649, 0.342981, 0.389236, 0.435599, 0.485816, 0.531765, 0.578136, 0.624629,
                0.682344, 0.728451, 0.775523, 0.821118, 0.871032, 0.917586, 0.963997, 1.009703,
                0.296347, 0.255303, 0.214417, 0.173850, 0.130589, 0.090463, 0.050517, 0.010497,
                -0.037865, -0.077090, -0.116374, -0.155345, -0.196354, -0.234968, -0.273508, -0.311138,
                0.296581, 0.342281, 0.387932, 0.433519, 0.482923, 0.528480, 0.574099, 0.619880,
                0.677029, 0.722269, 0.768037, 0.814036, 0.862826, 0.908465, 0.954833, 0.999726,
                0.296236, 0.255376, 0.214698, 0.174211, 0.131121, 0.091095, 0.051414, 0.011812,
                -0.036847, -0.075769, -0.114846, -0.153714, -0.194759, -0.232907, -0.271384, -0.309193,
                0.296631, 0.296051, 0.295420, 0.294675, 0.294027, 0.293355, 0.292672, 0.291986,
                0.291301, 0.290530, 0.289790, 0.289257, 0.288395, 0.287714, 0.287066, 0.286200,
                0.296024, 0.207189, 0.118702, 0.031332, -0.062120, -0.148408, -0.233674, -0.318311,
                -0.423175, -0.506204, -0.588661, -0.670987, -0.758686, -0.838896, -0.919460, -1.000000,
                0.296446, 0.295562, 0.294723, 0.293702, 0.292709, 0.291929, 0.290942, 0.289956,
                0.288819, 0.288125, 0.287000, 0.286121, 0.285209, 0.284152, 0.283296, 0.282474,
                0.296023, 0.207571, 0.119787, 0.032803, -0.059999, -0.145602, -0.230978, -0.314539,
                -0.418685, -0.501903, -0.583358, -0.664869, -0.752822, -0.832273, -0.911804, -0.991570,
                0.296631, 0.296919, 0.297435, 0.297892, 0.298243, 0.298690, 0.299148, 0.299392,
                0.299985, 0.300553, 0.300809, 0.301129, 0.301655, 0.302262, 0.302299, 0.302810,
                0.296092, 0.208841, 0.122328, 0.036532, -0.054881, -0.139210, -0.223246, -0.305961,
                -0.408225, -0.490167, -0.571972, -0.651102, -0.737570, -0.817478, -0.894767, -0.973014,
                0.296381, 0.296539, 0.296760, 0.297017, 0.297232, 0.297228, 0.297447, 0.297756,
                0.297718, 0.297945, 0.298221, 0.298231, 0.298337, 0.298736, 0.298706, 0.298886,
                0.295939, 0.209426, 0.123411, 0.038287, -0.053101, -0.136446, -0.219814, -0.302593,
                -0.403816, -0.485082, -0.566300, -0.646700, -0.730704, -0.810044, -0.889231, -0.964929
            };
        }

        private static double[] BuildMos6581MeasuredD418Offset()
        {
            const double dcBias = -0.165;
            var table = new double[Mos6581D418Amplitude.Length];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = dcBias + (Mos6581D418Amplitude[i] * Mos6581D418DcRange);
            }

            return table;
        }

        private static double[] BuildMos8580MeasuredD418Offset()
        {
            var table = new double[Mos8580D418Amplitude.Length];
            var scale = Mos8580D418DcScale();
            var bias = -(Mos8580D418LowNibbleDcRange * 0.5) - (Mos8580D418Amplitude[0x00] * scale);
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = bias + (Mos8580D418Amplitude[i] * scale);
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
                double chipOutputLowPassCutoffHz,
                double d418TransitionOffsetGain,
                double d418TransitionAmplitudeGain,
                double d418HighNibbleTransitionGain,
                double filterRoutingTransientGain,
                double filterModeTransientGain,
                double voice3MuteTransientGain)
            {
                VolumeGain = volumeGain;
                VolumeDc = volumeDc;
                VolumeStepTransientGain = volumeStepTransientGain;
                VolumeStepTransientLimit = volumeStepTransientLimit;
                VolumeStepAttackSeconds = volumeStepAttackSeconds;
                VolumeStepDecaySeconds = volumeStepDecaySeconds;
                ChipOutputLowPassCutoffHz = chipOutputLowPassCutoffHz;
                D418TransitionOffsetGain = d418TransitionOffsetGain;
                D418TransitionAmplitudeGain = d418TransitionAmplitudeGain;
                D418HighNibbleTransitionGain = d418HighNibbleTransitionGain;
                FilterRoutingTransientGain = filterRoutingTransientGain;
                FilterModeTransientGain = filterModeTransientGain;
                Voice3MuteTransientGain = voice3MuteTransientGain;
            }

            public double[] VolumeGain { get; }

            public double[] VolumeDc { get; }

            public double VolumeStepTransientGain { get; }

            public double VolumeStepTransientLimit { get; }

            public double VolumeStepAttackSeconds { get; }

            public double VolumeStepDecaySeconds { get; }

            public double ChipOutputLowPassCutoffHz { get; }

            public double D418TransitionOffsetGain { get; }

            public double D418TransitionAmplitudeGain { get; }

            public double D418HighNibbleTransitionGain { get; }

            public double FilterRoutingTransientGain { get; }

            public double FilterModeTransientGain { get; }

            public double Voice3MuteTransientGain { get; }
        }
    }
}
