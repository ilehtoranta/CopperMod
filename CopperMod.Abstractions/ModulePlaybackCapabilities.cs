namespace CopperMod.Abstractions
{
    /// <summary>
    /// Describes optional features supported by a loaded module song.
    /// </summary>
    public sealed class ModulePlaybackCapabilities
    {
        /// <summary>
        /// Conservative default capabilities.
        /// </summary>
        public static readonly ModulePlaybackCapabilities Minimal = new ModulePlaybackCapabilities();

        /// <summary>
        /// Creates playback capabilities.
        /// </summary>
        public ModulePlaybackCapabilities(
            bool canSeekByTime = false,
            bool canSeekByTrackerPosition = false,
            bool canReportDuration = false,
            bool canReportExactDuration = false,
            bool supportsTickRendering = true,
            bool supportsLoopControl = false,
            bool supportsStereoOutput = true,
            bool supportsSubSongs = false)
        {
            CanSeekByTime = canSeekByTime;
            CanSeekByTrackerPosition = canSeekByTrackerPosition;
            CanReportDuration = canReportDuration;
            CanReportExactDuration = canReportExactDuration;
            SupportsTickRendering = supportsTickRendering;
            SupportsLoopControl = supportsLoopControl;
            SupportsStereoOutput = supportsStereoOutput;
            SupportsSubSongs = supportsSubSongs;
        }

        /// <summary>
        /// Whether time-based seeking is available.
        /// </summary>
        public bool CanSeekByTime { get; }

        /// <summary>
        /// Whether order/row/tick seeking is available.
        /// </summary>
        public bool CanSeekByTrackerPosition { get; }

        /// <summary>
        /// Whether any duration estimate can be reported.
        /// </summary>
        public bool CanReportDuration { get; }

        /// <summary>
        /// Whether the reported duration is exact for the selected loop policy.
        /// </summary>
        public bool CanReportExactDuration { get; }

        /// <summary>
        /// Whether the song can render exactly one tracker tick at a time.
        /// </summary>
        public bool SupportsTickRendering { get; }

        /// <summary>
        /// Whether loop behavior can be changed by the caller.
        /// </summary>
        public bool SupportsLoopControl { get; }

        /// <summary>
        /// Whether stereo output is supported.
        /// </summary>
        public bool SupportsStereoOutput { get; }

        /// <summary>
        /// Whether the format supports multiple subsongs.
        /// </summary>
        public bool SupportsSubSongs { get; }
    }
}
