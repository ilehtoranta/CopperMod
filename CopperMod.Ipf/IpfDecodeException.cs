using System;

namespace CopperMod.Ipf;

/// <summary>
/// Represents an error while parsing or decoding an IPF disk image.
/// </summary>
public sealed class IpfDecodeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpfDecodeException"/> class.
    /// </summary>
    public IpfDecodeException(string message)
        : base(message)
    {
    }
}
