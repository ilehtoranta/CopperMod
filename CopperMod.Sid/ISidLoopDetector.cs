namespace CopperMod.Sid
{
    /// <summary>
    /// Provides SID write-stream loop detection for loaded SID songs.
    /// </summary>
    public interface ISidLoopDetector
    {
        /// <summary>
        /// Scans playback ticks for a repeating SID register write sequence.
        /// </summary>
        SidLoopDetectionResult DetectLoop(SidLoopDetectionOptions? options = null);
    }
}
