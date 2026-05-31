using System;

namespace CopperMod.Sid
{
    internal static class SidIntegerMath
    {
        public static long DivRoundNearest(long numerator, long denominator)
        {
            if (numerator < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "Numerator must be non-negative.");
            }

            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must be positive.");
            }

            return (long)(((Int128)numerator + (denominator / 2)) / denominator);
        }

        public static long MulDivRoundNearest(long multiplicand, long multiplier, long denominator)
        {
            if (multiplicand < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(multiplicand), multiplicand, "Multiplicand must be non-negative.");
            }

            if (multiplier < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, "Multiplier must be non-negative.");
            }

            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must be positive.");
            }

            return (long)((((Int128)multiplicand * multiplier) + (denominator / 2)) / denominator);
        }

        public static TimeSpan CyclesToTimeSpan(long cycles, int cpuCyclesPerSecond)
        {
            return TimeSpan.FromTicks(MulDivRoundNearest(cycles, TimeSpan.TicksPerSecond, cpuCyclesPerSecond));
        }

        public static long TimeSpanToCycles(TimeSpan position, int cpuCyclesPerSecond)
        {
            if (position < TimeSpan.Zero)
            {
                return 0;
            }

            return MulDivRoundNearest(position.Ticks, cpuCyclesPerSecond, TimeSpan.TicksPerSecond);
        }
    }
}
