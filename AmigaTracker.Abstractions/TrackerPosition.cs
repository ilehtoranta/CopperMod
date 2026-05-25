using System;

namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Position inside a tracker song.
    /// </summary>
    public readonly struct TrackerPosition : IEquatable<TrackerPosition>
    {
        /// <summary>
        /// Creates an order, row, and tick position.
        /// </summary>
        public TrackerPosition(int order, int row, int tick = 0)
        {
            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(order), order, "Order cannot be negative.");
            }

            if (row < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(row), row, "Row cannot be negative.");
            }

            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick cannot be negative.");
            }

            Order = order;
            Row = row;
            Tick = tick;
        }

        /// <summary>
        /// Sequence/order index.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Row index inside the current block or pattern.
        /// </summary>
        public int Row { get; }

        /// <summary>
        /// Tick index inside the current row.
        /// </summary>
        public int Tick { get; }

        /// <inheritdoc />
        public bool Equals(TrackerPosition other)
        {
            return Order == other.Order
                && Row == other.Row
                && Tick == other.Tick;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is TrackerPosition other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Order;
                hashCode = (hashCode * 397) ^ Row;
                hashCode = (hashCode * 397) ^ Tick;
                return hashCode;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Order}:{Row}:{Tick}";
        }

        /// <summary>
        /// Compares two tracker positions for equality.
        /// </summary>
        public static bool operator ==(TrackerPosition left, TrackerPosition right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two tracker positions for inequality.
        /// </summary>
        public static bool operator !=(TrackerPosition left, TrackerPosition right)
        {
            return !left.Equals(right);
        }
    }
}
