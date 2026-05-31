using System;

namespace CopperMod.Sid
{
    internal sealed class SidSampleClock
    {
        private long _nextSampleIndex;

        public SidSampleClock(int cpuCyclesPerSecond, int sampleRate, long currentCycle = 0)
        {
            if (cpuCyclesPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cpuCyclesPerSecond), cpuCyclesPerSecond, "CPU cycle rate must be positive.");
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            }

            CpuCyclesPerSecond = cpuCyclesPerSecond;
            SampleRate = sampleRate;
            Reset(currentCycle);
        }

        public int CpuCyclesPerSecond { get; }

        public int SampleRate { get; }

        public long NextSampleIndex => _nextSampleIndex;

        public bool Matches(int cpuCyclesPerSecond, int sampleRate)
        {
            return CpuCyclesPerSecond == cpuCyclesPerSecond && SampleRate == sampleRate;
        }

        public void Reset(long currentCycle = 0)
        {
            _nextSampleIndex = CountSamplesThroughCycle(currentCycle) + 1;
        }

        public int PeekFrameCount(long currentCycle, long cycleCount)
        {
            if (cycleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycleCount), cycleCount, "Cycle count cannot be negative.");
            }

            var endCycle = checked(currentCycle + cycleCount);
            var count = CountSamplesThroughCycle(endCycle) - _nextSampleIndex + 1;
            if (count <= 0)
            {
                return 0;
            }

            if (count > int.MaxValue)
            {
                throw new InvalidOperationException("The tick contains more output frames than can fit in an integer count.");
            }

            return (int)count;
        }

        public long[] PeekSampleTargets(long currentCycle, long cycleCount)
        {
            var count = PeekFrameCount(currentCycle, cycleCount);
            var targets = new long[count];
            for (var i = 0; i < targets.Length; i++)
            {
                targets[i] = GetSampleTargetCycle(_nextSampleIndex + i);
            }

            return targets;
        }

        public long[] ConsumeSampleTargets(long currentCycle, long cycleCount)
        {
            var targets = PeekSampleTargets(currentCycle, cycleCount);
            AdvanceFrames(targets.Length);
            return targets;
        }

        public void AdvanceFrames(int frameCount)
        {
            if (frameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count cannot be negative.");
            }

            _nextSampleIndex += frameCount;
        }

        public long GetSampleTargetCycle(long sampleIndex)
        {
            if (sampleIndex <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleIndex), sampleIndex, "Sample index must be one-based.");
            }

            return SidIntegerMath.MulDivRoundNearest(sampleIndex, CpuCyclesPerSecond, SampleRate);
        }

        public long CountSamplesThroughCycle(long cycle)
        {
            if (cycle < 0)
            {
                return 0;
            }

            var numerator = (((Int128)cycle + 1) * SampleRate) - 1 - (SampleRate / 2);
            if (numerator < CpuCyclesPerSecond)
            {
                return 0;
            }

            return (long)(numerator / CpuCyclesPerSecond);
        }
    }
}
