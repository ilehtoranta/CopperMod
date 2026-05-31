namespace CopperMod.Sid
{
    /// <summary>
    /// Allows diagnostic renderers to mute individual SID voices without stopping their oscillators.
    /// </summary>
    public interface ISidVoiceMuteController
    {
        /// <summary>
        /// Bit mask of muted SID voices, where bit 0 is voice 1.
        /// </summary>
        int MutedVoicesMask { get; set; }
    }
}
