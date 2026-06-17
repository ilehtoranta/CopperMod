namespace CopperMod.Sid
{
    /// <summary>
    /// Selects the SID analog/emulation accuracy profile.
    /// </summary>
    public enum SidEmulationProfile
    {
        /// <summary>
        /// Default profile tuned for compatibility and stable playback behavior.
        /// </summary>
        Balanced,

        /// <summary>
        /// Opt-in measured profile using hardware-derived SID analog response data.
        /// </summary>
        ReferenceMeasured
    }
}
