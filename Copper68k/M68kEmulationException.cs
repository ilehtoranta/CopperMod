using System;

namespace Copper68k
{
    /// <summary>
    /// Base exception type for Copper68k emulation errors.
    /// </summary>
    public class M68kEmulationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="M68kEmulationException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public M68kEmulationException(string message)
            : base(message)
        {
        }
    }
}
