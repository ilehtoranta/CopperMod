using System;

namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Base exception for module loading failures.
    /// </summary>
    public class ModuleLoadException : Exception
    {
        /// <summary>
        /// Creates a module loading exception.
        /// </summary>
        public ModuleLoadException()
        {
        }

        /// <summary>
        /// Creates a module loading exception with a message.
        /// </summary>
        public ModuleLoadException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a module loading exception with a message and inner exception.
        /// </summary>
        public ModuleLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Indicates that a loader does not support the supplied module data.
    /// </summary>
    public sealed class UnsupportedModuleFormatException : ModuleLoadException
    {
        /// <summary>
        /// Creates an unsupported format exception.
        /// </summary>
        public UnsupportedModuleFormatException()
        {
        }

        /// <summary>
        /// Creates an unsupported format exception with a message.
        /// </summary>
        public UnsupportedModuleFormatException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an unsupported format exception with a message and inner exception.
        /// </summary>
        public UnsupportedModuleFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
