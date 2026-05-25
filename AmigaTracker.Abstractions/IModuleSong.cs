using System;
using System.Collections.Generic;

namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Represents a loaded tracker module that can be rendered to interleaved floating-point PCM.
    /// </summary>
    public interface IModuleSong : IDisposable
    {
        /// <summary>
        /// Parsed song metadata.
        /// </summary>
        ModuleMetadata Metadata { get; }

        /// <summary>
        /// Features supported by this loader and song instance.
        /// </summary>
        ModulePlaybackCapabilities Capabilities { get; }

        /// <summary>
        /// Non-fatal parse or compatibility diagnostics collected while loading.
        /// </summary>
        IReadOnlyList<ModuleDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Song duration, if it can be calculated.
        /// </summary>
        SongDuration Duration { get; }

        /// <summary>
        /// Current playback position.
        /// </summary>
        PlaybackPosition Position { get; }

        /// <summary>
        /// Enables or disables playback looping when the implementation supports loop control.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Thrown by implementations that cannot change loop behavior.
        /// </exception>
        bool LoopingEnabled { get; set; }

        /// <summary>
        /// Returns the number of output frames required to render the current tracker tick.
        /// </summary>
        int GetCurrentTickFrameCount(AudioRenderOptions? options = null);

        /// <summary>
        /// Resets playback state to the beginning of the song.
        /// </summary>
        void Reset();

        /// <summary>
        /// Seeks to a time position.
        /// </summary>
        void Seek(TimeSpan position);

        /// <summary>
        /// Seeks to an exact tracker position when supported by the implementation.
        /// </summary>
        void Seek(TrackerPosition position);

        /// <summary>
        /// Renders as many PCM frames as fit into <paramref name="destination"/>.
        /// Samples are interleaved and normalized to the range -1.0 to 1.0.
        /// </summary>
        RenderResult Render(Span<float> destination, AudioRenderOptions? options = null);

        /// <summary>
        /// Renders the current tracker tick to <paramref name="destination"/>.
        /// The destination must have room for GetCurrentTickFrameCount(options) frames.
        /// </summary>
        RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null);
    }
}
