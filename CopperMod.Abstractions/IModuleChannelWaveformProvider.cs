namespace CopperMod.Abstractions
{
    /// <summary>
    /// Optional provider for renderers that can expose per-tracker-channel waveform data for visualization.
    /// </summary>
    public interface IModuleChannelWaveformProvider
    {
        /// <summary>
        /// Enables or disables per-channel waveform capture during rendering.
        /// </summary>
        bool ChannelWaveformCaptureEnabled { get; set; }

        /// <summary>
        /// The last captured per-channel waveform, when capture is enabled and a tick has been rendered.
        /// </summary>
        ModuleChannelWaveform? LastChannelWaveform { get; }
    }
}
