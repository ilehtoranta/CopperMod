using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace CopperDisk;

/// <summary>
/// Decodes SuperCard Pro flux captures into raw Amiga track byte streams.
/// </summary>
public static class ScpDecoder
{
    private const int HeaderLength = 0x10;
    private const int FloppyTrackTableOffset = 0x10;
    private const int ExtendedTrackTableOffset = 0x80;
    private const int FloppyTrackTableEntries = 168;
    private const int TrackHeaderEntrySize = 12;
    private const byte FlagsIndexAligned = 1 << 0;
    private const byte FlagsWriteCapable = 1 << 4;
    private const byte FlagsExtendedMode = 1 << 6;
    private const byte DiskTypeMask = 0x0F;
    private const byte AmigaDdDiskType = 0x04;
    private const byte AmigaHdDiskType = 0x08;

    /// <summary>
    /// Decodes an SCP image into raw track streams.
    /// </summary>
    /// <param name="image">The SCP image bytes.</param>
    /// <param name="options">Optional decode options.</param>
    /// <returns>The decoded SCP disk image.</returns>
    public static ScpDiskImage Decode(ReadOnlySpan<byte> image, ScpDecodeOptions? options = null)
    {
        options ??= ScpDecodeOptions.Default;
        var parser = new Parser(image);
        return parser.Decode(options);
    }

    private sealed class Parser
    {
        private readonly byte[] _image;

        public Parser(ReadOnlySpan<byte> image)
        {
            _image = image.ToArray();
        }

        public ScpDiskImage Decode(ScpDecodeOptions options)
        {
            if (options.RevolutionIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The SCP revolution index must be non-negative.");
            }

            if (options.BitCellsPerRevolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The SCP bit-cell count must be positive.");
            }

            var data = _image.AsSpan();
            if (data.Length < HeaderLength ||
                data[0] != (byte)'S' ||
                data[1] != (byte)'C' ||
                data[2] != (byte)'P')
            {
                throw new ScpDecodeException("The image does not contain an SCP header.");
            }

            var diskType = data[0x04];
            var revolutionCount = data[0x05];
            var startTrack = data[0x06];
            var endTrack = data[0x07];
            var flags = data[0x08];
            var encoding = data[0x09];
            var heads = data[0x0A];
            var resolution = data[0x0B];
            var checksum = ReadUInt32Little(data, 0x0C);
            if (revolutionCount is < 1 or > 5)
            {
                throw new ScpDecodeException("SCP images must contain between one and five revolutions.");
            }

            if (options.RevolutionIndex >= revolutionCount)
            {
                throw new ScpDecodeException("The requested SCP revolution is not present in the image.");
            }

            if (startTrack > endTrack || endTrack >= FloppyTrackTableEntries)
            {
                throw new ScpDecodeException("The SCP track range is invalid for a floppy image.");
            }

            if ((flags & FlagsExtendedMode) != 0)
            {
                throw new ScpDecodeException("SCP extended-mode hard-drive and tape images are not supported.");
            }

            if ((flags & FlagsWriteCapable) != 0)
            {
                throw new ScpDecodeException("Writable SCP images are not supported.");
            }

            if (encoding != 0)
            {
                throw new ScpDecodeException("Only 16-bit SCP flux entries are supported.");
            }

            if (heads > 2)
            {
                throw new ScpDecodeException("The SCP head metadata is invalid.");
            }

            if (!options.AllowNonAmigaDiskTypes)
            {
                var mediaType = (byte)(diskType & DiskTypeMask);
                if (mediaType == AmigaHdDiskType)
                {
                    throw new ScpDecodeException("Amiga HD SCP images are not supported by the A500 DD decoder.");
                }

                if (mediaType != AmigaDdDiskType)
                {
                    throw new ScpDecodeException($"Unsupported SCP disk type 0x{diskType:X2}; expected Amiga DD.");
                }
            }

            ValidateChecksum(data, checksum);

            var tracks = new List<ScpTrack>();
            var tableOffset = (flags & FlagsExtendedMode) == 0 ? FloppyTrackTableOffset : ExtendedTrackTableOffset;
            if (data.Length < tableOffset + (FloppyTrackTableEntries * 4))
            {
                throw new ScpDecodeException("The SCP track offset table is truncated.");
            }

            for (var tableIndex = startTrack; tableIndex <= endTrack; tableIndex++)
            {
                if (!HeadEntryIsIncluded(heads, tableIndex))
                {
                    continue;
                }

                var trackOffset = checked((int)ReadUInt32Little(data, tableOffset + (tableIndex * 4)));
                if (trackOffset == 0)
                {
                    continue;
                }

                tracks.Add(DecodeTrack(data, tableIndex, trackOffset, revolutionCount, flags, resolution, options));
            }

            return new ScpDiskImage(startTrack, endTrack, revolutionCount, (flags & FlagsIndexAligned) != 0, tracks);
        }

        private static ScpTrack DecodeTrack(
            ReadOnlySpan<byte> data,
            int tableIndex,
            int trackOffset,
            int revolutionCount,
            byte flags,
            byte resolution,
            ScpDecodeOptions options)
        {
            if (trackOffset < 0 || trackOffset + 4 + (revolutionCount * TrackHeaderEntrySize) > data.Length)
            {
                throw new ScpDecodeException($"SCP track {tableIndex} header is outside the image.");
            }

            if (data[trackOffset] != (byte)'T' ||
                data[trackOffset + 1] != (byte)'R' ||
                data[trackOffset + 2] != (byte)'K')
            {
                throw new ScpDecodeException($"SCP track {tableIndex} does not contain a TRK header.");
            }

            if (data[trackOffset + 3] != tableIndex)
            {
                throw new ScpDecodeException($"SCP track header number {data[trackOffset + 3]} does not match table entry {tableIndex}.");
            }

            var entryOffset = trackOffset + 4 + (options.RevolutionIndex * TrackHeaderEntrySize);
            var durationTicks = ReadUInt32Little(data, entryOffset);
            var fluxEntryCount = ReadUInt32Little(data, entryOffset + 4);
            var fluxOffset = ReadUInt32Little(data, entryOffset + 8);
            if (durationTicks == 0 || fluxEntryCount == 0 || fluxEntryCount > int.MaxValue / 2)
            {
                throw new ScpDecodeException($"SCP track {tableIndex} revolution metadata is invalid.");
            }

            var fluxStart = checked(trackOffset + (int)fluxOffset);
            var fluxBytes = checked((int)fluxEntryCount * 2);
            if (fluxOffset < 4 + (uint)(revolutionCount * TrackHeaderEntrySize) ||
                fluxStart < 0 ||
                fluxStart + fluxBytes > data.Length)
            {
                throw new ScpDecodeException($"SCP track {tableIndex} flux data is outside the image.");
            }

            var decode = DecodeFlux(
                data.Slice(fluxStart, fluxBytes),
                durationTicks,
                resolution,
                options.BitCellsPerRevolution);
            var features = AmigaTrackFeatures.PreservedTrackData | AmigaTrackFeatures.FluxCapture;
            if ((flags & FlagsIndexAligned) == 0)
            {
                features |= AmigaTrackFeatures.ApproximateIndex;
            }

            foreach (var region in decode.Regions)
            {
                features |= region.Features;
            }

            return new ScpTrack(
                cylinder: tableIndex / AmigaDiskGeometry.HeadCount,
                head: tableIndex % AmigaDiskGeometry.HeadCount,
                bitLength: options.BitCellsPerRevolution,
                startBit: 0,
                encodedData: decode.Data,
                features,
                decode.Regions);
        }

        private static FluxDecodeResult DecodeFlux(
            ReadOnlySpan<byte> fluxData,
            uint durationTicks,
            byte resolution,
            int targetBitCells)
        {
            var output = new byte[(targetBitCells + 7) / 8];
            var regions = new List<AmigaTrackRegion>();
            var nanosecondsPerTick = 25.0 * (resolution + 1);
            var revolutionNanoseconds = durationTicks * nanosecondsPerTick;
            var nanosecondsPerBitCell = revolutionNanoseconds / targetBitCells;
            var bitPosition = 0;
            var overflowTicks = 0L;
            var overflowStart = -1;
            for (var offset = 0; offset < fluxData.Length; offset += 2)
            {
                var entry = BinaryPrimitives.ReadUInt16BigEndian(fluxData.Slice(offset, 2));
                if (entry == 0)
                {
                    if (overflowStart < 0)
                    {
                        overflowStart = bitPosition;
                    }

                    overflowTicks = checked(overflowTicks + 65_536);
                    continue;
                }

                var totalTicks = overflowTicks + entry;
                overflowTicks = 0;
                var cellCount = Math.Max(1, (int)Math.Round((totalTicks * nanosecondsPerTick) / nanosecondsPerBitCell));
                if (overflowStart >= 0)
                {
                    AddRegion(regions, overflowStart, Math.Min(cellCount, targetBitCells - Math.Min(overflowStart, targetBitCells)), targetBitCells, AmigaTrackFeatures.NoFlux);
                    overflowStart = -1;
                }

                bitPosition += cellCount;
                if (bitPosition <= 0)
                {
                    continue;
                }

                var transitionBit = bitPosition - 1;
                if (transitionBit >= targetBitCells)
                {
                    break;
                }

                output[transitionBit >> 3] |= (byte)(0x80 >> (transitionBit & 7));
            }

            if (overflowStart >= 0 && overflowStart < targetBitCells)
            {
                AddRegion(regions, overflowStart, targetBitCells - overflowStart, targetBitCells, AmigaTrackFeatures.NoFlux);
            }

            return new FluxDecodeResult(output, regions);
        }

        private static void AddRegion(List<AmigaTrackRegion> regions, int startBit, int bitCount, int trackBits, AmigaTrackFeatures features)
        {
            if (bitCount <= 0 || startBit >= trackBits)
            {
                return;
            }

            var length = Math.Min(bitCount, trackBits - startBit);
            regions.Add(new AmigaTrackRegion(startBit, length, features));
        }

        private static bool HeadEntryIsIncluded(byte heads, int tableIndex)
        {
            return heads switch
            {
                0 => true,
                1 => (tableIndex & 1) == 0,
                2 => (tableIndex & 1) == 1,
                _ => false
            };
        }

        private static void ValidateChecksum(ReadOnlySpan<byte> data, uint expected)
        {
            if (expected == 0)
            {
                return;
            }

            var sum = 0u;
            for (var index = HeaderLength; index < data.Length; index++)
            {
                sum += data[index];
            }

            if (sum != expected)
            {
                throw new ScpDecodeException("The SCP checksum does not match the image contents.");
            }
        }

        private static uint ReadUInt32Little(ReadOnlySpan<byte> data, int offset)
        {
            if ((uint)offset > (uint)(data.Length - 4))
            {
                throw new ScpDecodeException("Unexpected end of SCP data.");
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        }

        private readonly record struct FluxDecodeResult(byte[] Data, IReadOnlyList<AmigaTrackRegion> Regions);
    }
}
