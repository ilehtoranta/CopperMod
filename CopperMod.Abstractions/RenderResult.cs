using System;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Result returned after rendering PCM frames.
    /// </summary>
    public readonly struct RenderResult
    {
        /// <summary>
        /// Creates a render result.
        /// </summary>
        public RenderResult(
            int framesWritten,
            int samplesWritten,
            PlaybackPosition position,
            bool endOfSong = false,
            bool loopReached = false,
            int loopsCompleted = 0)
        {
            if (framesWritten < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(framesWritten), framesWritten, "Frame count cannot be negative.");
            }

            if (samplesWritten < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(samplesWritten), samplesWritten, "Sample count cannot be negative.");
            }

            if (loopsCompleted < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(loopsCompleted), loopsCompleted, "Completed loops cannot be negative.");
            }

            FramesWritten = framesWritten;
            SamplesWritten = samplesWritten;
            Position = position;
            EndOfSong = endOfSong;
            LoopReached = loopReached;
            LoopsCompleted = loopsCompleted;
        }

        /// <summary>
        /// Number of complete audio frames written.
        /// </summary>
        public int FramesWritten { get; }

        /// <summary>
        /// Number of interleaved samples written to the destination buffer.
        /// </summary>
        public int SamplesWritten { get; }

        /// <summary>
        /// Playback position after rendering.
        /// </summary>
        public PlaybackPosition Position { get; }

        /// <summary>
        /// Whether rendering reached the end of the song.
        /// </summary>
        public bool EndOfSong { get; }

        /// <summary>
        /// Whether rendering crossed a song loop point.
        /// </summary>
        public bool LoopReached { get; }

        /// <summary>
        /// Number of loops completed during this render call.
        /// </summary>
        public int LoopsCompleted { get; }
    }
}
