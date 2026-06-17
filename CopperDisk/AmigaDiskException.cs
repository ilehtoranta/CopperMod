using System;

namespace CopperDisk;

/// <summary>
/// Represents an error while loading or materializing an Amiga disk image.
/// </summary>
public class AmigaDiskException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaDiskException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AmigaDiskException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaDiskException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public AmigaDiskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
