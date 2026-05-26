namespace CopperMod.Abstractions
{
    /// <summary>
    /// Current Amiga hardware state exposed by renderers that model machine controls.
    /// </summary>
    public readonly struct AmigaHardwareState
    {
        /// <summary>
        /// Creates an Amiga hardware state snapshot.
        /// </summary>
        public AmigaHardwareState(bool audioFilterEnabled)
        {
            AudioFilterEnabled = audioFilterEnabled;
        }

        /// <summary>
        /// Whether the Amiga LED-controlled hardware audio low-pass filter is enabled.
        /// </summary>
        public bool AudioFilterEnabled { get; }
    }
}
