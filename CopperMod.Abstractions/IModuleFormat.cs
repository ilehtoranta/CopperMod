using System;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Detects and loads one tracker module file format.
    /// </summary>
    public interface IModuleFormat
    {
        /// <summary>
        /// Human-readable format name, such as MMD0 or ProTracker MOD.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Returns true when this loader supports the supplied module data.
        /// </summary>
        bool CanLoad(ReadOnlySpan<byte> data);

        /// <summary>
        /// Loads a module song from bytes.
        /// </summary>
        /// <exception cref="UnsupportedModuleFormatException">
        /// Thrown when the data is valid bytes but not a supported format for this loader.
        /// </exception>
        /// <exception cref="ModuleLoadException">
        /// Thrown when the data looks supported but cannot be parsed.
        /// </exception>
        IModuleSong Load(ReadOnlySpan<byte> data);
    }

    /// <summary>
    /// Optional extension for formats that need the module source path or sibling files during load.
    /// </summary>
    public interface IModuleFormatWithContext : IModuleFormat
    {
        /// <summary>
        /// Returns true when this loader supports the supplied module data and context.
        /// </summary>
        bool CanLoad(ModuleLoadContext context);

        /// <summary>
        /// Loads a module song from bytes and source-file context.
        /// </summary>
        /// <exception cref="UnsupportedModuleFormatException">
        /// Thrown when the data is valid bytes but not a supported format for this loader.
        /// </exception>
        /// <exception cref="ModuleLoadException">
        /// Thrown when the data looks supported but cannot be parsed.
        /// </exception>
        IModuleSong Load(ModuleLoadContext context);
    }
}
