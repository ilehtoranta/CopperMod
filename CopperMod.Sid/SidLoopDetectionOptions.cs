using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Controls SID write-stream loop detection.
    /// </summary>
    public sealed class SidLoopDetectionOptions
    {
        /// <summary>
        /// Default maximum emulated playback time to scan.
        /// </summary>
        public static readonly TimeSpan DefaultMaxSearchDuration = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Default minimum loop start time.
        /// </summary>
        public static readonly TimeSpan DefaultMinimumStartTime = TimeSpan.Zero;

        /// <summary>
        /// Default minimum loop duration.
        /// </summary>
        public static readonly TimeSpan DefaultMinimumLoopDuration = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Creates loop detection options.
        /// </summary>
        public SidLoopDetectionOptions(
            TimeSpan? maxSearchDuration = null,
            TimeSpan? minimumStartTime = null,
            TimeSpan? minimumLoopDuration = null,
            int maximumActiveCandidates = 8192)
        {
            MaxSearchDuration = maxSearchDuration ?? DefaultMaxSearchDuration;
            MinimumStartTime = minimumStartTime ?? DefaultMinimumStartTime;
            MinimumLoopDuration = minimumLoopDuration ?? DefaultMinimumLoopDuration;
            MaximumActiveCandidates = maximumActiveCandidates;

            if (MaxSearchDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSearchDuration), maxSearchDuration, "Maximum search duration must be positive.");
            }

            if (MinimumStartTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumStartTime), minimumStartTime, "Minimum loop start time cannot be negative.");
            }

            if (MinimumLoopDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLoopDuration), minimumLoopDuration, "Minimum loop duration cannot be negative.");
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
        /// Earliest playback position that can be considered a loop start.
        /// </summary>
        public TimeSpan MinimumStartTime { get; }

        /// <summary>
        /// Shortest repeating write sequence accepted as a loop.
        /// </summary>
        public TimeSpan MinimumLoopDuration { get; }

        /// <summary>
        /// Safety limit for concurrently verified loop candidates.
        /// </summary>
        public int MaximumActiveCandidates { get; }
    }
}
