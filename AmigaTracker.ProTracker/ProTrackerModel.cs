using System.Collections.Generic;
using AmigaTracker.Abstractions;

namespace AmigaTracker.ProTracker
{
    internal enum ModLayout
    {
        ProTracker31,
        Legacy15
    }

    internal enum LegacyReplayProfile
    {
        None,
        UltimateSoundTracker,
        SoundTracker,
        NoiseTracker
    }

    internal sealed class ProTrackerModule
    {
        public ProTrackerModule(
            byte[] data,
            ModLayout layout,
            LegacyReplayProfile legacyProfile,
            string? signature,
            string title,
            int sampleCount,
            int songLength,
            int restartPosition,
            byte[] orderTable,
            IReadOnlyList<ProTrackerSample> samples,
            IReadOnlyList<ProTrackerPattern> patterns,
            float[] sampleArea,
            int sampleDataOffset,
            IReadOnlyList<ModuleDiagnostic> diagnostics)
        {
            Data = data;
            Layout = layout;
            LegacyProfile = legacyProfile;
            Signature = signature;
            Title = title;
            SampleCount = sampleCount;
            SongLength = songLength;
            RestartPosition = restartPosition;
            OrderTable = orderTable;
            Samples = samples;
            Patterns = patterns;
            SampleArea = sampleArea;
            SampleDataOffset = sampleDataOffset;
            Diagnostics = diagnostics;
        }

        public byte[] Data { get; }

        public ModLayout Layout { get; }

        public LegacyReplayProfile LegacyProfile { get; }

        public string? Signature { get; }

        public string Title { get; }

        public int SampleCount { get; }

        public int SongLength { get; }

        public int RestartPosition { get; }

        public byte[] OrderTable { get; }

        public IReadOnlyList<ProTrackerSample> Samples { get; }

        public IReadOnlyList<ProTrackerPattern> Patterns { get; }

        public float[] SampleArea { get; }

        public int SampleDataOffset { get; }

        public IReadOnlyList<ModuleDiagnostic> Diagnostics { get; }

        public string FormatVersion => Layout == ModLayout.ProTracker31
            ? Signature ?? "31-sample MOD"
            : LegacyProfile.ToString();
    }

    internal sealed class ProTrackerSample
    {
        public string Name { get; set; } = string.Empty;

        public int LengthWords { get; set; }

        public int LengthBytes => LengthWords * 2;

        public int FineTune { get; set; }

        public int Volume { get; set; }

        public int RepeatOffsetUnits { get; set; }

        public int RepeatLengthUnits { get; set; }

        public int RepeatOffsetBytes { get; set; }

        public int RepeatLengthWords { get; set; }

        public int RepeatLengthBytes => RepeatLengthWords * 2;

        public int SampleAreaOffset { get; set; }
    }

    internal sealed class ProTrackerPattern
    {
        public ProTrackerPattern(int index)
        {
            Index = index;
            Cells = new ProTrackerCell[ProTrackerConstants.RowsPerPattern, ProTrackerConstants.ChannelCount];
        }

        public int Index { get; }

        public ProTrackerCell[,] Cells { get; }
    }

    internal readonly struct ProTrackerCell
    {
        public ProTrackerCell(int period, int sampleNumber, int effect, int parameter)
        {
            Period = period;
            SampleNumber = sampleNumber;
            Effect = effect;
            Parameter = parameter;
        }

        public int Period { get; }

        public int SampleNumber { get; }

        public int Effect { get; }

        public int Parameter { get; }

        public bool IsEmpty => Period == 0 && SampleNumber == 0 && Effect == 0 && Parameter == 0;
    }
}
