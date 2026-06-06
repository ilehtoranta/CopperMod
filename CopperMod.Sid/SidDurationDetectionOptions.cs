using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Controls SID duration detection.
    /// </summary>
    public sealed class SidDurationDetectionOptions
    {
        /// <summary>
        /// Default maximum emulated playback time to scan.
        /// </summary>
        public static readonly TimeSpan DefaultMaxSearchDuration = SidLoopDetectionOptions.DefaultMaxSearchDuration;

        /// <summary>
        /// Default minimum loop duration.
        /// </summary>
        public static readonly TimeSpan DefaultMinimumLoopDuration = SidLoopDetectionOptions.DefaultMinimumLoopDuration;

        /// <summary>
        /// Default sustained silence window.
        /// </summary>
        public static readonly TimeSpan DefaultSilenceDuration = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Default audio sample rate used during detection.
        /// </summary>
        public const int DefaultSampleRate = 11025;

        /// <summary>
        /// Default per-tick sample range needed before silence detection can arm.
        /// </summary>
        public const double DefaultActiveRangeThreshold = 0.005;

        /// <summary>
        /// Default per-tick sample range considered silent.
        /// </summary>
        public const double DefaultSilenceRangeThreshold = 0.0005;

        /// <summary>
        /// Creates duration detection options.
        /// </summary>
        public SidDurationDetectionOptions(
            TimeSpan? maxSearchDuration = null,
            TimeSpan? minimumStartTime = null,
            TimeSpan? minimumLoopDuration = null,
            TimeSpan? silenceDuration = null,
            double activeRangeThreshold = DefaultActiveRangeThreshold,
            double silenceRangeThreshold = DefaultSilenceRangeThreshold,
            int sampleRate = DefaultSampleRate,
            int maximumActiveCandidates = 8192)
        {
            MaxSearchDuration = maxSearchDuration ?? DefaultMaxSearchDuration;
            MinimumStartTime = minimumStartTime ?? SidLoopDetectionOptions.DefaultMinimumStartTime;
            MinimumLoopDuration = minimumLoopDuration ?? DefaultMinimumLoopDuration;
            SilenceDuration = silenceDuration ?? DefaultSilenceDuration;
            ActiveRangeThreshold = activeRangeThreshold;
            SilenceRangeThreshold = silenceRangeThreshold;
            SampleRate = sampleRate;
            MaximumActiveCandidates = maximumActiveCandidates;

            if (MaxSearchDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSearchDuration), maxSearchDuration, "Maximum search duration must be positive.");
            }

            if (MinimumStartTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumStartTime), minimumStartTime, "Minimum start time cannot be negative.");
            }

            if (MinimumLoopDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLoopDuration), minimumLoopDuration, "Minimum loop duration cannot be negative.");
            }

            if (SilenceDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(silenceDuration), silenceDuration, "Silence duration must be positive.");
            }

            if (activeRangeThreshold <= 0 || !double.IsFinite(activeRangeThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(activeRangeThreshold), activeRangeThreshold, "Active range threshold must be a positive finite value.");
            }

            if (silenceRangeThreshold < 0 || !double.IsFinite(silenceRangeThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(silenceRangeThreshold), silenceRangeThreshold, "Silence range threshold must be a finite non-negative value.");
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            }

            if (maximumActiveCandidates <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumActiveCandidates), maximumActiveCandidates, "Maximum active candidates must be positive.");
            }
        }

        /// <summary>
        /// Maximum emulated playback time to scan before giving up.
        /// </summary>
        public TimeSpan MaxSearchDuration { get; }

        /// <summary>
        /// Earliest playback position that can be considered a loop or silence start.
        /// </summary>
        public TimeSpan MinimumStartTime { get; }

        /// <summary>
        /// Shortest repeating write sequence accepted as a loop.
        /// </summary>
        public TimeSpan MinimumLoopDuration { get; }

        /// <summary>
        /// Required duration of continuous silence before silence is accepted.
        /// </summary>
        public TimeSpan SilenceDuration { get; }

        /// <summary>
        /// Per-tick sample range needed before silence detection can arm.
        /// </summary>
        public double ActiveRangeThreshold { get; }

        /// <summary>
        /// Per-tick sample range considered silent.
        /// </summary>
        public double SilenceRangeThreshold { get; }

        /// <summary>
        /// Mono sample rate used while measuring silence.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Safety limit for concurrently verified loop candidates.
        /// </summary>
        public int MaximumActiveCandidates { get; }
    }
}
