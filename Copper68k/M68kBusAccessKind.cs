namespace Copper68k
{
    /// <summary>
    /// Identifies why the CPU is accessing the emulated bus.
    /// </summary>
    public enum M68kBusAccessKind
    {
        /// <summary>
        /// The CPU is fetching instruction words from memory.
        /// </summary>
        CpuInstructionFetch,

        /// <summary>
        /// The CPU is reading data from memory or a device.
        /// </summary>
        CpuDataRead,

        /// <summary>
        /// The CPU is writing data to memory or a device.
        /// </summary>
        CpuDataWrite
    }
}
