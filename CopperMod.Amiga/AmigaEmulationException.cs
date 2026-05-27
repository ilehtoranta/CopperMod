using System;

namespace CopperMod.Amiga
{
    internal class AmigaEmulationException : Exception
    {
        public AmigaEmulationException(string message)
            : base(message)
        {
        }
    }
}
