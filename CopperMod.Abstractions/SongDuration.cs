using System;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Describes how reliable a duration value is.
    /// </summary>
    public enum SongDurationKind
    {
        /// <summary>
        /// Duration is not known.
        /// </summary>
        Unknown,

        /// <summary>
        /// Duration is exact for the selected loop policy.
        /// </summary>
        Exact,

        /// <summary>
        /// Duration is an estimate.
        /// </summary>
        Approximate,

        /// <summary>
        /// Song loops indefinitely.
        /// </summary>
        Infinite
    }

    /// <summary>
    /// Represents a module duration, including unknown and infinite loop cases.
    /// </summary>
    public readonly struct SongDuration : IEquatable<SongDuration>
    {
        /// <summary>
        /// Creates a duration value.
        /// </summary>
        public SongDuration(TimeSpan? time, SongDurationKind kind, int includedLoops = 0)
        {
            if (time < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(time), time, "Duration cannot be negative.");
            }

            if (includedLoops < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(includedLoops), includedLoops, "Included loops cannot be negative.");
            }

            Time = time;
            Kind = kind;
            IncludedLoops = includedLoops;
        }

        /// <summary>
        /// Unknown duration.
        /// </summary>
        public static SongDuration Unknown => new SongDuration(null, SongDurationKind.Unknown);

        /// <summary>
        /// Infinite duration.
        /// </summary>
        public static SongDuration Infinite => new SongDuration(null, SongDurationKind.Infinite);

        /// <summary>
        /// Optional finite duration.
        /// </summary>
        public TimeSpan? Time { get; }

        /// <summary>
        /// Duration reliability.
        /// </summary>
        public SongDurationKind Kind { get; }

        /// <summary>
        /// Number of loops included in the duration calculation.
        /// </summary>
        public int IncludedLoops { get; }

        /// <summary>
        /// Whether a finite time value is available.
        /// </summary>
        public bool HasTime => Time.HasValue;

        /// <summary>
        /// Whole minutes of the finite duration, when available.
        /// </summary>
        public int? Minutes => Time.HasValue ? (int)Time.Value.TotalMinutes : (int?)null;

        /// <summary>
        /// Seconds component within the current minute, when available.
        /// </summary>
        public int? Seconds => Time.HasValue ? Time.Value.Seconds : (int?)null;

        /// <summary>
        /// Creates an exact finite duration.
        /// </summary>
        public static SongDuration Exact(TimeSpan time, int includedLoops = 0)
        {
            return new SongDuration(time, SongDurationKind.Exact, includedLoops);
        }

        /// <summary>
        /// Creates an approximate finite duration.
        /// </summary>
        public static SongDuration Approximate(TimeSpan time, int includedLoops = 0)
        {
            return new SongDuration(time, SongDurationKind.Approximate, includedLoops);
        }

        /// <inheritdoc />
        public bool Equals(SongDuration other)
        {
            return Nullable.Equals(Time, other.Time)
                && Kind == other.Kind
                && IncludedLoops == other.IncludedLoops;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is SongDuration other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Time.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Kind;
                hashCode = (hashCode * 397) ^ IncludedLoops;
                return hashCode;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Time.HasValue
                ? $"{Kind}: {Time.Value} (loops: {IncludedLoops})"
                : Kind.ToString();
        }

        /// <summary>
        /// Compares two durations for equality.
        /// </summary>
        public static bool operator ==(SongDuration left, SongDuration right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two durations for inequality.
        /// </summary>
        public static bool operator !=(SongDuration left, SongDuration right)
        {
            return !left.Equals(right);
        }
    }
}
