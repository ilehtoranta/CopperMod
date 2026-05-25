namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Describes the hardware family whose output character a renderer already models.
    /// </summary>
    public enum ModuleOutputFamily
    {
        /// <summary>
        /// No hardware-specific output family is declared.
        /// </summary>
        Neutral,

        /// <summary>
        /// Amiga Paula-style output.
        /// </summary>
        Amiga,

        /// <summary>
        /// Commodore 64 SID-style output.
        /// </summary>
        Commodore64
    }

    /// <summary>
    /// Optional interface for module renderers that declare their modeled output family.
    /// </summary>
    public interface IModuleOutputFamilyProvider
    {
        /// <summary>
        /// Hardware/output family modeled by the renderer.
        /// </summary>
        ModuleOutputFamily OutputFamily { get; }
    }
}
