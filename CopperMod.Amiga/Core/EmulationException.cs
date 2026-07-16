using System;

namespace CopperMod.Amiga.Core
{
    internal class AmigaEmulationException : Exception
    {
        public AmigaEmulationException(string message)
            : base(message)
        {
        }

        public AmigaEmulationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
