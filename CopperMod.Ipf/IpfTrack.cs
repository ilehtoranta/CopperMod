namespace CopperMod.Ipf;

/// <summary>
/// A single decoded floppy track.
/// </summary>
public sealed class IpfTrack
{
    internal IpfTrack(int cylinder, int head, int bitLength, int startBit, byte[] data)
    {
        Cylinder = cylinder;
        Head = head;
        BitLength = bitLength;
        StartBit = startBit;
        Data = data;
    }

    /// <summary>
    /// Gets the physical cylinder number.
    /// </summary>
    public int Cylinder { get; }

    /// <summary>
    /// Gets the physical head number.
    /// </summary>
    public int Head { get; }

    /// <summary>
    /// Gets the number of meaningful bits in <see cref="Data"/>.
    /// </summary>
    public int BitLength { get; }

    /// <summary>
    /// Gets the decoded stream start bit.
    /// </summary>
    public int StartBit { get; }

    /// <summary>
    /// Gets the decoded track bytes.
    /// </summary>
    public byte[] Data { get; }
}
