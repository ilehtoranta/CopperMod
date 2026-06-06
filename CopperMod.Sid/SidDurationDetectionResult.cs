using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Result of SID duration detection.
    /// </summary>
    public sealed class SidDurationDetectionResult
    {
        private SidDurationDetectionResult(
            SidDurationDetectionKind kind,
            TimeSpan? duration,
            SidLoopDetectionResult? loop,
            TimeSpan? silenceStart,
            TimeSpan? silenceDuration,
            int ticksAnalyzed,
            TimeSpan searchDuration)
        {
            Kind = kind;
            Duration = duration;
            Loop = loop;
            SilenceStart = silenceStart;
            SilenceDuration = silenceDuration;
            TicksAnalyzed = ticksAnalyzed;
            SearchDuration = searchDuration;
        }

        /// <summary>
        /// Detection kind.
        /// </summary>
        public SidDurationDetectionKind Kind { get; }

        /// <summary>
        /// Whether a duration was detected.
        /// </summary>
        public bool Detected => Kind != SidDurationDetectionKind.None && Duration.HasValue;

        /// <summary>
        /// Detected playback duration.
        /// </summary>
        public TimeSpan? Duration { get; }

        /// <summary>
        /// Loop detection details when <see cref="Kind" /> is <see cref="SidDurationDetectionKind.Loop" />.
        /// </summary>
        public SidLoopDetectionResult? Loop { get; }

        /// <summary>
        /// Playback position where sustained silence began.
        /// </summary>
        public TimeSpan? SilenceStart { get; }

        /// <summary>
        /// Confirmed sustained silence duration.
        /// </summary>
        public TimeSpan? SilenceDuration { get; }

        /// <summary>
        /// Total number of ticks inspected.
        /// </summary>
        public int TicksAnalyzed { get; }

        /// <summary>
        /// Emulated playback time inspected.
        /// </summary>
        public TimeSpan SearchDuration { get; }

        internal static SidDurationDetectionResult FromLoop(SidLoopDetectionResult loop)
        {
            ArgumentNullException.ThrowIfNull(loop);
            return new SidDurationDetectionResult(
                SidDurationDetectionKind.Loop,
                loop.LoopEnd,
                loop,
                null,
                null,
                loop.TicksAnalyzed,
                loop.SearchDuration);
        }

        internal static SidDurationDetectionResult FromSilence(
            TimeSpan silenceStart,
            TimeSpan silenceDuration,
            int ticksAnalyzed,
            TimeSpan searchDuration)
        {
            return new SidDurationDetectionResult(
                SidDurationDetectionKind.Silence,
                silenceStart,
                null,
                silenceStart,
                silenceDuration,
                ticksAnalyzed,
                searchDuration);
        }

        internal static SidDurationDetectionResult NotDetected(int ticksAnalyzed, TimeSpan searchDuration)
        {
            return new SidDurationDetectionResult(
                SidDurationDetectionKind.None,
                null,
                null,
                null,
                null,
                ticksAnalyzed,
                searchDuration);
        }
    }
}
