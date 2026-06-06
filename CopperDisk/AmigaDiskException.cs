using System;

namespace CopperDisk;

public class AmigaDiskException : Exception
{
    public AmigaDiskException(string message)
        : base(message)
    {
    }

    public AmigaDiskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
