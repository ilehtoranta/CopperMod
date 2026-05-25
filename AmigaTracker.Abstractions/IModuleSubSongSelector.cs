using System;
using System.Collections.Generic;

namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Optional interface for module formats that expose multiple selectable subtunes.
    /// </summary>
    public interface IModuleSubSongSelector
    {
        /// <summary>
        /// Total number of selectable subtunes.
        /// </summary>
        int SubSongCount { get; }

        /// <summary>
        /// Zero-based default subtune index.
        /// </summary>
        int DefaultSubSongIndex { get; }

        /// <summary>
        /// Zero-based current subtune index.
        /// </summary>
        int CurrentSubSongIndex { get; }

        /// <summary>
        /// Optional metadata for each subtune.
        /// </summary>
        IReadOnlyList<ModuleSubSongMetadata> SubSongs { get; }

        /// <summary>
        /// Selects a subtune by zero-based index and resets playback for that subtune.
        /// </summary>
        void SelectSubSong(int index);
    }

    /// <summary>
    /// Describes one selectable subtune.
    /// </summary>
    public sealed class ModuleSubSongMetadata
    {
        /// <summary>
        /// Creates subtune metadata.
        /// </summary>
        public ModuleSubSongMetadata(int index, string? title = null, SongDuration? duration = null)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Subtune index cannot be negative.");
            }

            Index = index;
            Title = title;
            Duration = duration ?? SongDuration.Unknown;
        }

        /// <summary>
        /// Zero-based subtune index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Optional subtune title.
        /// </summary>
        public string? Title { get; }

        /// <summary>
        /// Optional subtune duration.
        /// </summary>
        public SongDuration Duration { get; }
    }
}
