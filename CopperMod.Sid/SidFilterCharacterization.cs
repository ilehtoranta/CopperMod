using System;

namespace CopperMod.Sid
{
    internal readonly struct SidFilterCharacterizationRequest
    {
        public SidFilterCharacterizationRequest(
            SidChipModel model,
            SidFilterProfileId profile,
            int cutoffRegister,
            int resonanceNibble,
            int filterMode,
            int routedVoiceCount = 1,
            double inputLevel = 1.0,
            ushort frequency = 0x1000,
            int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond,
            int warmupCycles = 12000,
            int measuredCycles = 16384)
        {
            Model = model;
            Profile = profile;
            CutoffRegister = Math.Clamp(cutoffRegister, 0, 2047);
            ResonanceNibble = Math.Clamp(resonanceNibble, 0, 15);
            FilterMode = filterMode & 0x70;
            RoutedVoiceCount = Math.Clamp(routedVoiceCount, 1, 3);
            InputLevel = Math.Clamp(inputLevel, 0.0, 1.0);
            Frequency = frequency;
            CpuCyclesPerSecond = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            WarmupCycles = Math.Max(0, warmupCycles);
            MeasuredCycles = Math.Max(1, measuredCycles);
        }

        public SidChipModel Model { get; }

        public SidFilterProfileId Profile { get; }

        public int CutoffRegister { get; }

        public int ResonanceNibble { get; }

        public int FilterMode { get; }

        public int RoutedVoiceCount { get; }

        public double InputLevel { get; }

        public ushort Frequency { get; }

        public int CpuCyclesPerSecond { get; }

        public int WarmupCycles { get; }

        public int MeasuredCycles { get; }
    }

    internal readonly struct SidFilterCharacterizationPoint
    {
        public SidFilterCharacterizationPoint(
            SidFilterCharacterizationRequest request,
            double filterCutoffHz,
            double filterDamping,
            double dc,
            double rms,
            double acRms,
            double peak,
            double peakToPeak,
            double zeroCrossingRate,
            double meanAbsDelta,
            double brightness,
            double lowPassPeak,
            double bandPassPeak,
            double highPassPeak,
            double estimatedRingingFrequencyHz,
            double ringDecayRatio,
            int saturationOrPlateauCount,
            double lowToBandPeakRatio,
            double highToBandPeakRatio)
        {
            Request = request;
            FilterCutoffHz = filterCutoffHz;
            FilterDamping = filterDamping;
            Dc = dc;
            Rms = rms;
            AcRms = acRms;
            Peak = peak;
            PeakToPeak = peakToPeak;
            ZeroCrossingRate = zeroCrossingRate;
            MeanAbsDelta = meanAbsDelta;
            Brightness = brightness;
            LowPassPeak = lowPassPeak;
            BandPassPeak = bandPassPeak;
            HighPassPeak = highPassPeak;
            EstimatedRingingFrequencyHz = estimatedRingingFrequencyHz;
            RingDecayRatio = ringDecayRatio;
            SaturationOrPlateauCount = saturationOrPlateauCount;
            LowToBandPeakRatio = lowToBandPeakRatio;
            HighToBandPeakRatio = highToBandPeakRatio;
        }

        public SidFilterCharacterizationRequest Request { get; }

        public double FilterCutoffHz { get; }

        public double FilterDamping { get; }

        public double Dc { get; }

        public double Rms { get; }

        public double AcRms { get; }

        public double Peak { get; }

        public double PeakToPeak { get; }

        public double ZeroCrossingRate { get; }

        public double MeanAbsDelta { get; }

        public double Brightness { get; }

        public double LowPassPeak { get; }

        public double BandPassPeak { get; }

        public double HighPassPeak { get; }

        public double EstimatedRingingFrequencyHz { get; }

        public double RingDecayRatio { get; }

        public int SaturationOrPlateauCount { get; }

        public double LowToBandPeakRatio { get; }

        public double HighToBandPeakRatio { get; }
    }

    internal static class SidFilterCharacterizer
    {
        public static SidFilterCharacterizationPoint Measure(SidFilterCharacterizationRequest request)
        {
            var chip = CreateCharacterizationChip(request);
            RenderCycles(chip, request.WarmupCycles);

            var minimum = double.MaxValue;
            var maximum = double.MinValue;
            var peak = 0.0;
            var sum = 0.0;
            var sumSquares = 0.0;
            var absDeltaSum = 0.0;
            var zeroCrossings = 0;
            var hasPrevious = false;
            var previous = 0.0;
            var lowPassPeak = 0.0;
            var bandPassPeak = 0.0;
            var highPassPeak = 0.0;
            var firstWindowBandPeak = 0.0;
            var lastWindowBandPeak = 0.0;
            var bandZeroCrossings = 0;
            var hasPreviousBand = false;
            var previousBand = 0.0;
            var saturationOrPlateauCount = 0;
            var edgeWindowLength = Math.Max(1, request.MeasuredCycles / 4);
            var lastWindowStart = request.MeasuredCycles - edgeWindowLength;

            for (var i = 0; i < request.MeasuredCycles; i++)
            {
                var sample = chip.Render(1);
                var debug = chip.DebugState;
                var absBand = Math.Abs(debug.BandPassOutput);
                minimum = Math.Min(minimum, sample);
                maximum = Math.Max(maximum, sample);
                peak = Math.Max(peak, Math.Abs(sample));
                sum += sample;
                sumSquares += sample * sample;
                lowPassPeak = Math.Max(lowPassPeak, Math.Abs(debug.LowPassOutput));
                bandPassPeak = Math.Max(bandPassPeak, absBand);
                highPassPeak = Math.Max(highPassPeak, Math.Abs(debug.HighPassOutput));
                if (i < edgeWindowLength)
                {
                    firstWindowBandPeak = Math.Max(firstWindowBandPeak, absBand);
                }

                if (i >= lastWindowStart)
                {
                    lastWindowBandPeak = Math.Max(lastWindowBandPeak, absBand);
                }

                if (Math.Abs(debug.LowPassOutput) > 1.74 ||
                    Math.Abs(debug.BandPassOutput) > 1.74 ||
                    Math.Abs(debug.HighPassOutput) > 1.74 ||
                    Math.Abs(sample) > 0.998)
                {
                    saturationOrPlateauCount++;
                }

                if (hasPrevious)
                {
                    absDeltaSum += Math.Abs(sample - previous);
                    if (Math.Abs(sample - previous) < 1.0e-12 && Math.Abs(sample) > 0.90)
                    {
                        saturationOrPlateauCount++;
                    }

                    if ((previous < 0.0 && sample >= 0.0) ||
                        (previous >= 0.0 && sample < 0.0))
                    {
                        zeroCrossings++;
                    }
                }

                previous = sample;
                hasPrevious = true;
                if (hasPreviousBand &&
                    ((previousBand < 0.0 && debug.BandPassOutput >= 0.0) ||
                        (previousBand >= 0.0 && debug.BandPassOutput < 0.0)))
                {
                    bandZeroCrossings++;
                }

                previousBand = debug.BandPassOutput;
                hasPreviousBand = true;
            }

            var debugState = chip.DebugState;
            var measured = request.MeasuredCycles;
            var mean = sum / measured;
            var rms = Math.Sqrt(sumSquares / measured);
            var variance = Math.Max(0.0, (sumSquares / measured) - (mean * mean));
            var acRms = Math.Sqrt(variance);
            var transitionCount = Math.Max(1, measured - 1);
            var meanAbsDelta = absDeltaSum / transitionCount;
            var brightness = meanAbsDelta / Math.Max(acRms, 1.0e-12);
            var durationSeconds = measured / (double)Math.Max(1, request.CpuCyclesPerSecond);
            var estimatedRingingFrequencyHz = bandZeroCrossings / Math.Max(1.0e-12, 2.0 * durationSeconds);
            var ringDecayRatio = lastWindowBandPeak / Math.Max(firstWindowBandPeak, 1.0e-12);
            var lowToBandPeakRatio = lowPassPeak / Math.Max(bandPassPeak, 1.0e-12);
            var highToBandPeakRatio = highPassPeak / Math.Max(bandPassPeak, 1.0e-12);

            return new SidFilterCharacterizationPoint(
                request,
                debugState.FilterCutoffHz,
                debugState.FilterDamping,
                mean,
                rms,
                acRms,
                peak,
                maximum - minimum,
                zeroCrossings / (double)transitionCount,
                meanAbsDelta,
                brightness,
                lowPassPeak,
                bandPassPeak,
                highPassPeak,
                estimatedRingingFrequencyHz,
                ringDecayRatio,
                saturationOrPlateauCount,
                lowToBandPeakRatio,
                highToBandPeakRatio);
        }

        public static SidFilterCharacterizationPoint[] SweepCutoff(
            SidChipModel model,
            SidFilterProfileId profile,
            ReadOnlySpan<int> cutoffRegisters,
            int resonanceNibble,
            int filterMode,
            double inputLevel = 1.0,
            ushort frequency = 0x1000,
            int routedVoiceCount = 1)
        {
            var points = new SidFilterCharacterizationPoint[cutoffRegisters.Length];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = Measure(new SidFilterCharacterizationRequest(
                    model,
                    profile,
                    cutoffRegisters[i],
                    resonanceNibble,
                    filterMode,
                    routedVoiceCount: routedVoiceCount,
                    inputLevel: inputLevel,
                    frequency: frequency));
            }

            return points;
        }

        public static SidFilterCharacterizationPoint[] SweepResonance(
            SidChipModel model,
            SidFilterProfileId profile,
            int cutoffRegister,
            ReadOnlySpan<int> resonanceNibbles,
            int filterMode,
            int routedVoiceCount = 1,
            double inputLevel = 1.0,
            ushort frequency = 0x1000)
        {
            var points = new SidFilterCharacterizationPoint[resonanceNibbles.Length];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = Measure(new SidFilterCharacterizationRequest(
                    model,
                    profile,
                    cutoffRegister,
                    resonanceNibbles[i],
                    filterMode,
                    routedVoiceCount,
                    inputLevel,
                    frequency));
            }

            return points;
        }

        private static SidChip CreateCharacterizationChip(SidFilterCharacterizationRequest request)
        {
            var chip = new SidChip(
                request.Model,
                SidConstants.DefaultSidBaseAddress,
                request.CpuCyclesPerSecond,
                request.Profile);

            var sustain = Math.Clamp((int)Math.Round(request.InputLevel * 15.0), 1, 15);
            for (var voice = 0; voice < request.RoutedVoiceCount; voice++)
            {
                WritePulseVoice(
                    chip,
                    voice,
                    (ushort)(request.Frequency + (voice * 0x0111)),
                    (ushort)(0x0800 + (voice * 0x0040)),
                    (byte)((sustain << 4) | 0x00));
            }

            chip.Write(0x15, (byte)(request.CutoffRegister & 0x07));
            chip.Write(0x16, (byte)((request.CutoffRegister >> 3) & 0xFF));
            chip.Write(0x17, (byte)((request.ResonanceNibble << 4) | ((1 << request.RoutedVoiceCount) - 1)));
            chip.Write(0x18, (byte)(request.FilterMode | 0x0F));
            return chip;
        }

        private static void WritePulseVoice(SidChip chip, int voice, ushort frequency, ushort pulseWidth, byte sustainRelease)
        {
            var offset = voice * 7;
            chip.Write((byte)(offset + 0), (byte)(frequency & 0xFF));
            chip.Write((byte)(offset + 1), (byte)(frequency >> 8));
            chip.Write((byte)(offset + 2), (byte)(pulseWidth & 0xFF));
            chip.Write((byte)(offset + 3), (byte)((pulseWidth >> 8) & 0x0F));
            chip.Write((byte)(offset + 5), 0x00);
            chip.Write((byte)(offset + 6), sustainRelease);
            chip.Write((byte)(offset + 4), 0x41);
        }

        private static void RenderCycles(SidChip chip, int cycles)
        {
            const int Chunk = 128;
            while (cycles >= Chunk)
            {
                chip.Render(Chunk);
                cycles -= Chunk;
            }

            if (cycles > 0)
            {
                chip.Render(cycles);
            }
        }
    }
}
