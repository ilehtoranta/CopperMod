using System;

namespace Copper68k
{
    public class M68kEmulationException : Exception
    {
        public M68kEmulationException(string message)
            : base(message)
        {
        }
    }
}
