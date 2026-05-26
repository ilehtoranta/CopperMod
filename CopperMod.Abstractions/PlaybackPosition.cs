using System;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Current playback position expressed in both time and optional tracker coordinates.
    /// </summary>
    public readonly struct PlaybackPosition : IEquatable<PlaybackPosition>
    {
        /// <summary>
        /// Creates a playback position.
        /// </summary>
        public PlaybackPosition(TimeSpan time, TrackerPosition? tracker = null, int completedLoops = 0)
        {
            if (time < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(time), time, "Position time cannot be negative.");
            }

            if (completedLoops < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(completedLoops), completedLoops, "Completed loops cannot be negative.");
            }

            Time = time;
            Tracker = tracker;
            CompletedLoops = completedLoops;
        }

        /// <summary>
        /// Elapsed playback time.
        /// </summary>
        public TimeSpan Time { get; }

        /// <summary>
        /// Optional order/row/tick tracker position.
        /// </summary>
        public TrackerPosition? Tracker { get; }

        /// <summary>
        /// Number of completed loops observed during playback.
        /// </summary>
        public int CompletedLoops { get; }

        /// <summary>
        /// Whole minutes component of the elapsed playback time.
        /// </summary>
        public int Minutes => (int)Time.TotalMinutes;

        /// <summary>
        /// Seconds component within the current minute.
        /// </summary>
        public int Seconds => Time.Seconds;

        /// <summary>
        /// Creates a time-only playback position.
        /// </summary>
        public static PlaybackPosition FromTime(TimeSpan time)
        {
            return new PlaybackPosition(time);
        }

        /// <inheritdoc />
        public bool Equals(PlaybackPosition other)
        {
            return Time.Equals(other.Time)
                && Nullable.Equals(Tracker, other.Tracker)
                && CompletedLoops == other.CompletedLoops;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is PlaybackPosition other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Time.GetHashCode();
                hashCode = (hashCode * 397) ^ Tracker.GetHashCode();
                hashCode = (hashCode * 397) ^ CompletedLoops;
                return hashCode;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Tracker.HasValue
                ? $"{Time} ({Tracker.Value}, loops: {CompletedLoops})"
                : $"{Time} (loops: {CompletedLoops})";
        }

        /// <summary>
        /// Compares two playback positions for equality.
        /// </summary>
        public static bool operator ==(PlaybackPosition left, PlaybackPosition right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two playback positions for inequality.
        /// </summary>
        public static bool operator !=(PlaybackPosition left, PlaybackPosition right)
        {
            return !left.Equals(right);
        }
    }
}
