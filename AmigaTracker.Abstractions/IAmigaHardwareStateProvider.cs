namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Optional interface for Amiga module renderers that expose emulated hardware state.
    /// </summary>
    public interface IAmigaHardwareStateProvider
    {
        /// <summary>
        /// Current Amiga hardware state.
        /// </summary>
        AmigaHardwareState AmigaHardwareState { get; }
    }
}
