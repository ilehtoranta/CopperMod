using System;
using System.Collections.Generic;

namespace CopperDisk;

/// <summary>
/// Represents Amiga floppy media that can expose encoded track streams.
/// </summary>
/// <remarks>
/// This is the common media surface for emulator floppy-controller paths. Use
/// <see cref="IAmigaSectorDiskMedia"/> when a decoded AmigaDOS sector image is also required.
/// </remarks>
public interface IAmigaDiskMedia
{
    /// <summary>
    /// Gets the number of cylinders exposed by the media.
    /// </summary>
    int Cylinders { get; }

    /// <summary>
    /// Gets the number of heads exposed by the media.
    /// </summary>
    int Heads { get; }

    /// <summary>
    /// Reads an encoded track stream for the specified physical cylinder and head.
    /// </summary>
    /// <param name="cylinder">The zero-based cylinder number.</param>
    /// <param name="head">The zero-based head number.</param>
    /// <returns>The encoded track stream.</returns>
    IAmigaTrack ReadTrack(int cylinder, int head);
}

/// <summary>
/// Marks media whose encoded track stream preserves source track data.
/// </summary>
/// <remarks>
/// Hosts can use this marker when file extensions are not enough to identify track-preserving media, such as
/// UAE extended ADF images that commonly still use the <c>.adf</c> extension.
/// </remarks>
public interface IAmigaPreservedTrackDiskMedia : IAmigaDiskMedia
{
}

/// <summary>
/// Represents one encoded Amiga floppy track stream.
/// </summary>
public interface IAmigaTrack
{
    /// <summary>
    /// Gets the number of meaningful bits in <see cref="EncodedData"/>.
    /// </summary>
    int BitLength { get; }

    /// <summary>
    /// Gets the physical index bit position of the decoded stream.
    /// </summary>
    int StartBit { get; }

    /// <summary>
    /// Gets the encoded track bytes. Only the first <see cref="BitLength"/> bits are meaningful.
    /// </summary>
    /// <remarks>The returned memory is a read-only view over media-owned backing storage.</remarks>
    ReadOnlyMemory<byte> EncodedData { get; }

    /// <summary>
    /// Gets feature flags that describe the encoded stream.
    /// </summary>
    AmigaTrackFeatures Features { get; }

    /// <summary>
    /// Gets feature-marked bit ranges inside the encoded stream.
    /// </summary>
    IReadOnlyList<AmigaTrackRegion> Regions { get; }
}

/// <summary>
/// Represents Amiga floppy media with a decoded AmigaDOS sector view.
/// </summary>
/// <remarks>
/// The decoded sector view always uses the standard 880 KiB AmigaDOS layout. Media loaded from encoded tracks may
/// contain zero-filled sectors when a sector could not be decoded.
/// </remarks>
public interface IAmigaSectorDiskMedia : IAmigaDiskMedia
{
    /// <summary>
    /// Gets a value indicating whether all sectors were decoded from the source media.
    /// </summary>
    bool HasCompleteDecodedSectorData { get; }

    /// <summary>
    /// Gets the decoded AmigaDOS sector image. Missing sectors are zero-filled.
    /// </summary>
    /// <remarks>The returned memory is a read-only view. Writable media may update the backing data after successful writes.</remarks>
    ReadOnlyMemory<byte> SectorData { get; }

    /// <summary>
    /// Gets the first two sectors of the decoded sector image.
    /// </summary>
    ReadOnlyMemory<byte> BootBlock { get; }

    /// <summary>
    /// Reads one decoded sector by physical position.
    /// </summary>
    /// <param name="cylinder">The zero-based cylinder number.</param>
    /// <param name="head">The zero-based head number.</param>
    /// <param name="sector">The zero-based sector number within the track.</param>
    /// <returns>The decoded 512-byte sector.</returns>
    ReadOnlyMemory<byte> ReadSector(int cylinder, int head, int sector);

    /// <summary>
    /// Reads one decoded sector by logical sector index.
    /// </summary>
    /// <param name="logicalSector">The zero-based logical sector index.</param>
    /// <returns>The decoded 512-byte sector.</returns>
    ReadOnlyMemory<byte> ReadSector(int logicalSector);

    /// <summary>
    /// Reads a byte range from the decoded sector image.
    /// </summary>
    /// <param name="byteOffset">The zero-based byte offset.</param>
    /// <param name="byteCount">The number of bytes to read.</param>
    /// <returns>The requested decoded bytes.</returns>
    ReadOnlyMemory<byte> ReadBytes(int byteOffset, int byteCount);
}

/// <summary>
/// Represents Amiga floppy media that accepts encoded track writes.
/// </summary>
/// <remarks>
/// Write support is best-effort at the encoded-track level. Implementations may reject tracks when no recognizable
/// sectors can be decoded from the supplied stream.
/// </remarks>
public interface IWritableAmigaDiskMedia : IAmigaDiskMedia
{
    /// <summary>
    /// Gets a value indicating whether the media has been modified through a successful write.
    /// </summary>
    bool IsDirty { get; }

    /// <summary>
    /// Attempts to write one encoded track stream.
    /// </summary>
    /// <param name="cylinder">The zero-based cylinder number.</param>
    /// <param name="head">The zero-based head number.</param>
    /// <param name="track">The encoded track to write.</param>
    /// <returns><see langword="true"/> when at least one sector was decoded and written; otherwise, <see langword="false"/>.</returns>
    bool TryWriteTrack(int cylinder, int head, IAmigaTrack track);
}

/// <summary>
/// Represents Amiga sector media that also supports encoded track writes.
/// </summary>
/// <remarks>
/// Standard ADF media implements this interface so hosts can use decoded sectors and write encoded tracks through
/// one package-facing contract.
/// </remarks>
public interface IWritableAmigaSectorDiskMedia : IAmigaSectorDiskMedia, IWritableAmigaDiskMedia
{
}

/// <summary>
/// Describes optional characteristics of an encoded Amiga track stream.
/// </summary>
[Flags]
public enum AmigaTrackFeatures
{
    /// <summary>
    /// No additional track features are known.
    /// </summary>
    None = 0,

    /// <summary>
    /// The track preserves source track bytes rather than only regenerated AmigaDOS sector data.
    /// </summary>
    PreservedTrackData = 1 << 0,

    /// <summary>
    /// The source describes weak or unstable magnetic data.
    /// </summary>
    WeakData = 1 << 1,

    /// <summary>
    /// Weak data was approximated into deterministic bytes.
    /// </summary>
    ApproximateWeakData = 1 << 2,

    /// <summary>
    /// The track was decoded from a flux-level capture.
    /// </summary>
    FluxCapture = 1 << 3,

    /// <summary>
    /// The physical index position is approximate rather than capture-aligned.
    /// </summary>
    ApproximateIndex = 1 << 4,

    /// <summary>
    /// The source describes an interval without flux transitions.
    /// </summary>
    NoFlux = 1 << 5
}
