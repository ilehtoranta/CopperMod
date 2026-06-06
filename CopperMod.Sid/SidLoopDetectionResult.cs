using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Result of SID write-stream loop detection.
    /// </summary>
    public sealed class SidLoopDetectionResult
    {
        private SidLoopDetectionResult(
            bool detected,
            TimeSpan? loopStart,
            TimeSpan? loopEnd,
            TimeSpan? loopLength,
            int? loopStartTick,
            int? loopEndTick,
            int? loopLengthTicks,
            int ticksAnalyzed,
            TimeSpan searchDuration)
        {
            Detected = detected;
            LoopStart = loopStart;
            LoopEnd = loopEnd;
            LoopLength = loopLength;
            LoopStartTick = loopStartTick;
            LoopEndTick = loopEndTick;
            LoopLengthTicks = loopLengthTicks;
            TicksAnalyzed = ticksAnalyzed;
            SearchDuration = searchDuration;
        }

        /// <summary>
        /// Whether a repeating SID write sequence was confirmed.
        /// </summary>
        public bool Detected { get; }

        /// <summary>
        /// Playback position where the repeated sequence begins.
        /// </summary>
        public TimeSpan? LoopStart { get; }

        /// <summary>
        /// Playback position where the first sequence ends and the repeat begins.
        /// </summary>
        public TimeSpan? LoopEnd { get; }

        /// <summary>
        /// Duration of the repeated sequence.
        /// </summary>
        public TimeSpan? LoopLength { get; }

        /// <summary>
        /// Tick index where the repeated sequence begins.
        /// </summary>
        public int? LoopStartTick { get; }

        /// <summary>
        /// Tick index where the first sequence ends and the repeat begins.
        /// </summary>
        public int? LoopEndTick { get; }

        /// <summary>
        /// Number of ticks in the repeated sequence.
        /// </summary>
        public int? LoopLengthTicks { get; }

        /// <summary>
        /// Total number of ticks inspected.
        /// </summary>
        public int TicksAnalyzed { get; }

        /// <summary>
        /// Emulated playback time inspected.
        /// </summary>
        public TimeSpan SearchDuration { get; }

        internal static SidLoopDetectionResult DetectedLoop(
            int startTick,
            int endTick,
            int lengthTicks,
            int ticksAnalyzed,
            TimeSpan loopStart,
            TimeSpan loopEnd,
            TimeSpan searchDuration)
        {
            return new SidLoopDetectionResult(
                true,
                loopStart,
                loopEnd,
                loopEnd - loopStart,
                startTick,
                endTick,
                lengthTicks,
                ticksAnalyzed,
                searchDuration);
        }

        internal static SidLoopDetectionResult NotDetected(int ticksAnalyzed, TimeSpan searchDuration)
        {
            return new SidLoopDetectionResult(
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                ticksAnalyzed,
                searchDuration);
        }
    }
}
