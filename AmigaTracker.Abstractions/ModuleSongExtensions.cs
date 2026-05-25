using System;

namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Convenience helpers for module song implementations.
    /// </summary>
    public static class ModuleSongExtensions
    {
        /// <summary>
        /// Seeks using a minute and second pair.
        /// </summary>
        public static void Seek(this IModuleSong song, int minutes, int seconds)
        {
            if (song is null)
            {
                throw new ArgumentNullException(nameof(song));
            }

            if (minutes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Minutes cannot be negative.");
            }

            if (seconds < 0 || seconds >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Seconds must be in the range 0..59.");
            }

            song.Seek(new TimeSpan(0, minutes, seconds));
        }

        /// <summary>
        /// Returns the interleaved sample count required to render the current tracker tick.
        /// </summary>
        public static int GetCurrentTickSampleCount(this IModuleSong song, AudioRenderOptions? options = null)
        {
            if (song is null)
            {
                throw new ArgumentNullException(nameof(song));
            }

            options ??= AudioRenderOptions.Default;
            return options.GetSampleCount(song.GetCurrentTickFrameCount(options));
        }
    }
}
