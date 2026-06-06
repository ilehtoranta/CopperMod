namespace CopperMod.Sid
{
    /// <summary>
    /// Provides SID duration detection from loop restarts or sustained silence.
    /// </summary>
    public interface ISidDurationDetector
    {
        /// <summary>
        /// Scans playback for a likely SID duration.
        /// </summary>
        SidDurationDetectionResult DetectDuration(SidDurationDetectionOptions? options = null);
    }
}
