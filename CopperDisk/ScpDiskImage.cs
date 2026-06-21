using System;
using System.Collections.Generic;

namespace CopperDisk;

/// <summary>
/// A decoded SuperCard Pro floppy disk image.
/// </summary>
public sealed class ScpDiskImage
{
    internal ScpDiskImage(int startTrack, int endTrack, int revolutionCount, bool indexAligned, IReadOnlyList<ScpTrack> tracks)
    {
        StartTrack = startTrack;
        EndTrack = endTrack;
        RevolutionCount = revolutionCount;
        IndexAligned = indexAligned;
        Tracks = Array.AsReadOnly(new List<ScpTrack>(tracks).ToArray());
    }

    /// <summary>
    /// Gets the first SCP track-table entry described by the image metadata.
    /// </summary>
    public int StartTrack { get; }

    /// <summary>
    /// Gets the last SCP track-table entry described by the image metadata.
    /// </summary>
    public int EndTrack { get; }

    /// <summary>
    /// Gets the number of revolutions stored per track.
    /// </summary>
    public int RevolutionCount { get; }

    /// <summary>
    /// Gets a value indicating whether the capture started at the physical index pulse.
    /// </summary>
    public bool IndexAligned { get; }

    /// <summary>
    /// Gets the decoded track streams.
    /// </summary>
    public IReadOnlyList<ScpTrack> Tracks { get; }
}
