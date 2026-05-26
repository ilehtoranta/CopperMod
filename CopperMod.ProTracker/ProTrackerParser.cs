using System;
using System.Collections.Generic;
using CopperMod.Abstractions;

namespace CopperMod.ProTracker
{
    internal static class ProTrackerParser
    {
        public readonly struct Identity
        {
            public Identity(bool recognized, bool isPacked, ModLayout layout, string? signature)
            {
                Recognized = recognized;
                IsPacked = isPacked;
                Layout = layout;
                Signature = signature;
            }

            public bool Recognized { get; }

            public bool IsPacked { get; }

            public ModLayout Layout { get; }

            public string? Signature { get; }
        }

        public static Identity Identify(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                return default;
            }

            if (ProTrackerConstants.Matches(data, 0, "PACK") || ProTrackerConstants.Matches(data, 0, "PP20"))
            {
                return new Identity(true, true, ModLayout.ProTracker31, null);
            }

            if (data.Length >= ProTrackerConstants.ProTrackerHeaderLength &&
                ProTrackerConstants.IsProTrackerSignature(data, 1080))
            {
                var signature = ModEndian.ReadFixedString(data, 1080, 4);
                return new Identity(true, false, ModLayout.ProTracker31, signature);
            }

            if (LooksLikeLegacy15(data))
            {
                return new Identity(true, false, ModLayout.Legacy15, null);
            }

            return default;
        }

        public static ProTrackerModule Parse(byte[] data, Identity identity)
        {
            var span = (ReadOnlySpan<byte>)data;
            var diagnostics = new List<ModuleDiagnostic>();
            var layout = identity.Layout;
            var sampleCount = layout == ModLayout.ProTracker31
                ? ProTrackerConstants.ProTrackerSampleCount
                : ProTrackerConstants.LegacySampleCount;
            var songLengthOffset = layout == ModLayout.ProTracker31 ? 950 : 470;
            var restartOffset = songLengthOffset + 1;
            var orderOffset = songLengthOffset + 2;
            var patternOffset = layout == ModLayout.ProTracker31
                ? ProTrackerConstants.ProTrackerHeaderLength
                : ProTrackerConstants.LegacyHeaderLength;

            ModEndian.RequireRange(span, 0, patternOffset, "MOD header");
            var title = ModEndian.ReadFixedString(span, 0, 20);
            var songLength = span[songLengthOffset];
            var restart = span[restartOffset];
            if (songLength < 1 || songLength > 128)
            {
                throw new ModuleLoadException("The ProTracker song length is outside the valid range 1-128.");
            }

            var orders = new byte[128];
            span.Slice(orderOffset, 128).CopyTo(orders);
            var patternCount = GetPatternCount(orders);
            if (patternCount <= 0)
            {
                throw new ModuleLoadException("The ProTracker module does not reference any patterns.");
            }

            ModEndian.RequireRange(span, patternOffset, checked(patternCount * ProTrackerConstants.PatternLength), "pattern data");
            var sampleDataOffset = patternOffset + (patternCount * ProTrackerConstants.PatternLength);
            var legacyProfile = layout == ModLayout.Legacy15
                ? DetectLegacyProfile(span, patternOffset, patternCount)
                : LegacyReplayProfile.None;
            var loopUnitsAreWords = layout == ModLayout.ProTracker31 || legacyProfile == LegacyReplayProfile.NoiseTracker;
            var samples = ParseSamples(span, sampleCount, loopUnitsAreWords, sampleDataOffset, diagnostics);
            var patterns = ParsePatterns(span, patternOffset, patternCount);
            var sampleArea = DecodeSampleArea(span, sampleDataOffset);
            ApplyReplaySampleFixups(sampleArea, samples);

            return new ProTrackerModule(
                data,
                layout,
                legacyProfile,
                identity.Signature,
                title,
                sampleCount,
                songLength,
                restart,
                orders,
                samples,
                patterns,
                sampleArea,
                sampleDataOffset,
                diagnostics);
        }

        private static bool LooksLikeLegacy15(ReadOnlySpan<byte> data)
        {
            if (data.Length < ProTrackerConstants.LegacyHeaderLength)
            {
                return false;
            }

            if (data.Length >= ProTrackerConstants.ProTrackerHeaderLength &&
                IsPrintableSignature(data.Slice(1080, 4)))
            {
                return false;
            }

            var songLength = data[470];
            if (songLength < 1 || songLength > 128)
            {
                return false;
            }

            var plausibleSamples = 0;
            for (var i = 0; i < ProTrackerConstants.LegacySampleCount; i++)
            {
                var offset = 20 + (i * ProTrackerConstants.SampleHeaderLength);
                var volume = data[offset + 25];
                if (volume > 64)
                {
                    return false;
                }

                if (ModEndian.ReadUInt16(data, offset + 22, "sample length") > 0 || volume > 0)
                {
                    plausibleSamples++;
                }
            }

            var maxPattern = 0;
            for (var i = 0; i < 128; i++)
            {
                maxPattern = Math.Max(maxPattern, data[472 + i]);
            }

            var patternBytes = checked((maxPattern + 1) * ProTrackerConstants.PatternLength);
            return plausibleSamples > 0 && ModEndian.HasRange(data, ProTrackerConstants.LegacyHeaderLength, patternBytes);
        }

        private static bool IsPrintableSignature(ReadOnlySpan<byte> signature)
        {
            for (var i = 0; i < signature.Length; i++)
            {
                if (signature[i] < 32 || signature[i] > 126)
                {
                    return false;
                }
            }

            return true;
        }

        private static int GetPatternCount(byte[] orders)
        {
            var max = 0;
            for (var i = 0; i < orders.Length; i++)
            {
                max = Math.Max(max, orders[i]);
            }

            return max + 1;
        }

        private static List<ProTrackerSample> ParseSamples(
            ReadOnlySpan<byte> data,
            int sampleCount,
            bool loopUnitsAreWords,
            int sampleDataOffset,
            List<ModuleDiagnostic> diagnostics)
        {
            var samples = new List<ProTrackerSample>(sampleCount);
            var sampleAreaOffset = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var offset = 20 + (i * ProTrackerConstants.SampleHeaderLength);
                var lengthWords = ModEndian.ReadUInt16(data, offset + 22, "sample length");
                var repeatUnits = ModEndian.ReadUInt16(data, offset + 26, "sample repeat offset");
                var repeatLengthUnits = ModEndian.ReadUInt16(data, offset + 28, "sample repeat length");
                var repeatLengthWords = loopUnitsAreWords
                    ? repeatLengthUnits
                    : Math.Max(0, repeatLengthUnits / 2);
                if (repeatLengthWords == 0)
                {
                    repeatLengthWords = 1;
                }

                var sample = new ProTrackerSample
                {
                    Name = ModEndian.ReadFixedString(data, offset, 22),
                    LengthWords = lengthWords,
                    FineTune = data[offset + 24] & 0x0F,
                    Volume = Math.Min(64, (int)data[offset + 25]),
                    RepeatOffsetUnits = repeatUnits,
                    RepeatLengthUnits = repeatLengthUnits,
                    RepeatOffsetBytes = repeatUnits * (loopUnitsAreWords ? 2 : 1),
                    RepeatLengthWords = repeatLengthWords,
                    SampleAreaOffset = sampleAreaOffset
                };

                if (sampleDataOffset + sampleAreaOffset + sample.LengthBytes > data.Length)
                {
                    diagnostics.Add(new ModuleDiagnostic(
                        ModuleDiagnosticSeverity.Warning,
                        $"Sample {i + 1} extends beyond the supplied file and will be zero-padded by the renderer.",
                        "MOD_SAMPLE_TRUNCATED"));
                }

                samples.Add(sample);
                sampleAreaOffset += sample.LengthBytes;
            }

            return samples;
        }

        private static List<ProTrackerPattern> ParsePatterns(ReadOnlySpan<byte> data, int patternOffset, int patternCount)
        {
            var patterns = new List<ProTrackerPattern>(patternCount);
            for (var pattern = 0; pattern < patternCount; pattern++)
            {
                var parsed = new ProTrackerPattern(pattern);
                for (var row = 0; row < ProTrackerConstants.RowsPerPattern; row++)
                {
                    for (var channel = 0; channel < ProTrackerConstants.ChannelCount; channel++)
                    {
                        var offset = patternOffset +
                            (pattern * ProTrackerConstants.PatternLength) +
                            (row * ProTrackerConstants.PatternRowLength) +
                            (channel * ProTrackerConstants.PatternCellLength);
                        var b0 = data[offset];
                        var b1 = data[offset + 1];
                        var b2 = data[offset + 2];
                        var b3 = data[offset + 3];
                        var sample = (b0 & 0xF0) | ((b2 & 0xF0) >> 4);
                        var period = ((b0 & 0x0F) << 8) | b1;
                        var effect = b2 & 0x0F;
                        parsed.Cells[row, channel] = new ProTrackerCell(period, sample, effect, b3);
                    }
                }

                patterns.Add(parsed);
            }

            return patterns;
        }

        private static float[] DecodeSampleArea(ReadOnlySpan<byte> data, int sampleDataOffset)
        {
            if (sampleDataOffset >= data.Length)
            {
                return new float[2];
            }

            var length = data.Length - sampleDataOffset;
            var samples = new float[length + 2];
            for (var i = 0; i < length; i++)
            {
                samples[i] = unchecked((sbyte)data[sampleDataOffset + i]) / 128.0f;
            }

            return samples;
        }

        private static void ApplyReplaySampleFixups(float[] sampleArea, IReadOnlyList<ProTrackerSample> samples)
        {
            foreach (var sample in samples)
            {
                if (sample.LengthWords <= 0)
                {
                    continue;
                }

                if (sample.RepeatOffsetUnits + sample.RepeatLengthUnits > 1)
                {
                    continue;
                }

                if (sample.SampleAreaOffset < sampleArea.Length)
                {
                    sampleArea[sample.SampleAreaOffset] = 0.0f;
                }

                if (sample.SampleAreaOffset + 1 < sampleArea.Length)
                {
                    sampleArea[sample.SampleAreaOffset + 1] = 0.0f;
                }
            }
        }

        private static LegacyReplayProfile DetectLegacyProfile(ReadOnlySpan<byte> data, int patternOffset, int patternCount)
        {
            var hasExtendedOrPtEffect = false;
            var hasNoiseTrackerEffect = false;
            for (var pattern = 0; pattern < patternCount; pattern++)
            {
                for (var row = 0; row < ProTrackerConstants.RowsPerPattern; row++)
                {
                    for (var channel = 0; channel < ProTrackerConstants.ChannelCount; channel++)
                    {
                        var offset = patternOffset +
                            (pattern * ProTrackerConstants.PatternLength) +
                            (row * ProTrackerConstants.PatternRowLength) +
                            (channel * ProTrackerConstants.PatternCellLength);
                        var effect = data[offset + 2] & 0x0F;
                        var parameter = data[offset + 3];
                        if (effect == 0xE || effect >= 0x3)
                        {
                            hasExtendedOrPtEffect = true;
                        }

                        if ((effect == 0x1 || effect == 0x2 || effect == 0x3 || effect == 0x4) && parameter > 0)
                        {
                            hasNoiseTrackerEffect = true;
                        }
                    }
                }
            }

            if (hasExtendedOrPtEffect)
            {
                return LegacyReplayProfile.NoiseTracker;
            }

            return hasNoiseTrackerEffect ? LegacyReplayProfile.SoundTracker : LegacyReplayProfile.UltimateSoundTracker;
        }
    }
}
