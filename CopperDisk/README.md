# CopperDisk

CopperDisk is a managed Amiga floppy disk-image library for emulator hosts and
disk tools. It loads standard 880 KiB ADF sector images, modern UAE extended
ADF images, gzip-compressed ADZ images, read-only DMS images, decodes SPS/CAPS
IPF images and SuperCard Pro SCP flux captures into raw Amiga track streams,
and exposes both decoded AmigaDOS sectors and encoded track data through one
small API.

The package targets .NET 10 and has no external runtime dependencies.

## Supported Images

- `.adf`: standard 880 KiB AmigaDOS sector images, plus modern `UAE-1ADF`
  extended ADF images.
- `.adz`: gzip-compressed standard or modern extended ADF images, loaded read-only.
- `.dms`: unencrypted standard DD Disk Masher images, loaded read-only.
- `.ipf`: SPS/CAPS IPF images decoded to raw track streams.
- `.scp`: read-only Amiga DD SuperCard Pro flux captures decoded to raw track streams.
- `.zip`: an archive containing exactly one `.adf`, `.adz`, `.dms`, `.ipf`, or `.scp` entry.

Standard ADF media is writable at the sector-image level. Extended ADF, ADZ,
DMS, IPF, SCP, and encoded-track media are read-only, but still expose a
best-effort decoded sector view when sectors can be recognized. Extended ADF
support targets modern `UAE-1ADF` images; old `UAE--ADF` images are rejected.

## Third-Party Notices

DMS support follows the file-format validation and decrunch behavior of the
public-domain xDMS unpacker by Andre Rodrigues de la Rocha. See
`THIRD-PARTY-NOTICES.md` for details.

## Loading Disk Images

```csharp
using CopperDisk;

var loaded = AmigaDiskLoader.Load("Workbench.adf");

Console.WriteLine(loaded.DisplayName);

var media = loaded.Media;
var track = media.ReadTrack(cylinder: 0, head: 0);

Console.WriteLine(track.BitLength);
Console.WriteLine(track.Features);
```

`AmigaDiskLoader.Load` accepts direct ADF/ADZ/DMS/IPF/SCP paths and ZIP files
containing exactly one supported image. ADF and ADZ inputs are routed by content,
so modern extended ADF images can use the usual `.adf` or `.adz` extension.
Direct and ZIP-wrapped IPF/SCP files use the same optional aggregate options:

```csharp
var loaded = AmigaDiskLoader.Load(
    "protected-disk.zip",
    new AmigaDiskLoadOptions
    {
        Ipf = new IpfDecodeOptions
        {
            StartAtIndex = false,
            AlignTracksToWord = true
        },
        Scp = new ScpDecodeOptions
        {
            RevolutionIndex = 0
        }
    });
```

The older `Load(path, IpfDecodeOptions)` overload is retained as a compatibility
shortcut for IPF-only callers.

Use `FromIpfBytes` when IPF bytes are already in memory. The input is read
during decoding and is not retained by the returned media:

```csharp
ReadOnlySpan<byte> ipfBytes = File.ReadAllBytes("protected-disk.ipf");
IAmigaSectorDiskMedia ipf = AmigaDiskLoader.FromIpfBytes(ipfBytes);
```

Use `FromScpBytes` for in-memory SCP captures:

```csharp
ReadOnlySpan<byte> scpBytes = File.ReadAllBytes("flux-capture.scp");
IAmigaSectorDiskMedia scp = AmigaDiskLoader.FromScpBytes(scpBytes);
```

## Sector Media

Use `IAmigaSectorDiskMedia` when you need the decoded AmigaDOS sector view:

```csharp
if (loaded.Media is IAmigaSectorDiskMedia sectors)
{
    ReadOnlyMemory<byte> bootBlock = sectors.BootBlock;
    ReadOnlyMemory<byte> sector0 = sectors.ReadSector(0);
    ReadOnlyMemory<byte> diskBytes = sectors.SectorData;

    Console.WriteLine(sectors.HasCompleteDecodedSectorData);
}
```

`SectorData` is always the standard 880 KiB AmigaDOS sector image layout.
Missing or unreadable sectors are zero-filled and
`HasCompleteDecodedSectorData` reports whether every sector was decoded.

## Writable ADF Media

ADF loaders return `IWritableAmigaSectorDiskMedia`, so callers that need write
support do not need casts through unrelated interfaces:

```csharp
byte[] adfBytes = File.ReadAllBytes("blank.adf");
IWritableAmigaSectorDiskMedia adf = AmigaDiskLoader.FromAdfBytes(adfBytes);

IAmigaTrack encodedTrack = adf.ReadTrack(0, 0);
bool wroteAnySector = adf.TryWriteTrack(0, 0, encodedTrack);
if (adf.IsDirty)
{
    File.WriteAllBytes("blank.adf", adf.SectorData.ToArray());
}
```

## Encoded Tracks

All media exposes encoded floppy-controller data through `IAmigaDiskMedia` and
`IAmigaTrack`.

```csharp
IAmigaTrack track = media.ReadTrack(cylinder: 40, head: 1);

ReadOnlyMemory<byte> encodedBytes = track.EncodedData;
int encodedBits = track.BitLength;
int indexBit = track.StartBit;
AmigaTrackFeatures features = track.Features;
IReadOnlyList<AmigaTrackRegion> regions = track.Regions;
```

Use `AmigaEncodedTrack` when a tool needs byte-backed track helpers:

```csharp
var encoded = new AmigaEncodedTrack(track.EncodedData, track.BitLength, track.StartBit, track.Features);

ushort sync = encoded.ReadUInt16AtBit(0);
int wrappedOffset = AmigaEncodedTrack.WrapBitOffset(-1, encoded.BitLength);
```

`Regions` preserves track spans that need special handling in an emulator host,
including weak data, flux-capture origin, approximate index materialization, and
no-flux intervals. Region start and length are expressed in bit offsets within
the encoded track.

`FromEncodedTracks` accepts exactly `AmigaDiskGeometry.TrackCount` tracks.
`FromEncodedTrackBytes` accepts exactly the same number of byte arrays and treats
`null` entries as unformatted tracks:

```csharp
byte[]?[] tracks = new byte[AmigaDiskGeometry.TrackCount][];
tracks[0] = File.ReadAllBytes("track-00.0.raw");

IAmigaSectorDiskMedia disk = AmigaDiskLoader.FromEncodedTrackBytes(tracks);
```

## Ownership And Mutability

ADF and encoded-track byte-array entry points take ownership of the supplied
arrays and do not make defensive copies. After passing an owned array to
CopperDisk, do not mutate it while the returned media or track object is still
in use.

This applies to:

- `AmigaDiskLoader.FromAdfBytes`
- `AmigaDiskLoader.FromEncodedTrackBytes`
- `AmigaEncodedTrack.FromBytes`

Public output uses `ReadOnlyMemory<byte>` where possible. Treat these values as
read-only views over CopperDisk-owned media. Writable ADF media updates its
sector view after successful writes.

## IPF Decoder Notes

`IpfDecoder` is an expert API for hosts that need decoded IPF track streams
directly:

```csharp
byte[] ipfBytes = File.ReadAllBytes("disk.ipf");
IpfDiskImage image = IpfDecoder.Decode(ipfBytes);

foreach (IpfTrack track in image.Tracks)
{
    Console.WriteLine($"{track.Cylinder}/{track.Head}: {track.BitLength} bits");
}
```

The managed IPF decoder currently supports the SPS/CAPS stream forms needed by
CopperScreen's floppy path, with these limitations:

- Weak data is materialized deterministically and marked with
  `AmigaTrackFeatures.WeakData` and
  `AmigaTrackFeatures.ApproximateWeakData`.
- Stored IPF gap streams are decoded from their stream elements when present;
  malformed gap offsets and invalid stream endings are rejected.
- Unknown IPF block encoders are rejected with `IpfDecodeException`.

Use `AmigaDiskLoader.FromIpfBytes` or `AmigaDiskLoader.Load` for normal disk
loading. Use `IpfDecoder` only when a tool needs the IPF model types directly.

## SCP Decoder Notes

`ScpDecoder` is a read-only Amiga DD decoder for SuperCard Pro captures:

```csharp
byte[] scpBytes = File.ReadAllBytes("disk.scp");
ScpDiskImage image = ScpDecoder.Decode(scpBytes);

foreach (ScpTrack track in image.Tracks)
{
    Console.WriteLine($"{track.Cylinder}/{track.Head}: {track.BitLength} bits");
}
```

The decoder validates normal floppy SCP headers, nonzero checksums, `TRK`
records, 1-5 revolutions, 16-bit flux entries, selected head data, and 25 ns
resolution scaling. It defaults to Amiga DD read-only media and rejects HD,
write-capable mutation, non-Amiga disk types, and unsupported bit-cell entry
widths unless options explicitly allow the format.

Flux transitions are converted to raw MFM bitcells for the selected revolution.
Overflow/no-flux entries are preserved as `NoFlux` regions, while captures
without a reliable index are marked with `ApproximateIndex`.

## AmigaDOS Track Helpers

`AmigaDosTrackEncoder` and `AmigaDosTrackDecoder` are expert APIs for standard
AmigaDOS MFM track data. They are useful for emulator tests, disk-inspection
tools, and import/export paths that need raw floppy-controller streams rather
than a filesystem-level API.

They do not validate Amiga filesystems. The decoder scans for standard sector
headers and checksums, returns a best-effort 880 KiB sector image, and leaves
unreadable sectors zero-filled.
