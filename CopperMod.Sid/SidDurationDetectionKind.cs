namespace CopperMod.Sid
{
    /// <summary>
    /// Describes the signal that ended SID duration detection.
    /// </summary>
    public enum SidDurationDetectionKind
    {
        /// <summary>
        /// No duration was detected within the search window.
        /// </summary>
        None,

        /// <summary>
        /// Duration was detected from an exact repeated SID write sequence.
        /// </summary>
        Loop,

        /// <summary>
        /// Duration was detected from sustained low-range audio after activity.
        /// </summary>
        Silence
    }
}
