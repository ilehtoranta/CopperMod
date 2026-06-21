using System;

namespace CopperDisk;

/// <summary>
/// Represents an error while parsing or decoding a SuperCard Pro image.
/// </summary>
public sealed class ScpDecodeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScpDecodeException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ScpDecodeException(string message)
        : base(message)
    {
    }
}
