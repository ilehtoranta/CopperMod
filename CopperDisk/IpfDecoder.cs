using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CopperDisk;

/// <summary>
/// Decodes Software Preservation Society IPF disk images into raw track byte streams.
/// </summary>
/// <remarks>
/// This is an expert API for emulator and disk-tool hosts that need direct access to decoded track streams.
/// The decoder currently materializes weak data deterministically and reports
/// <see cref="AmigaTrackFeatures.ApproximateWeakData"/>; stored IPF gap streams and unknown block encoders
/// are rejected with <see cref="IpfDecodeException"/>.
/// </remarks>
public static class IpfDecoder
{
    private const string CapsChunkId = "CAPS";
    private const string InfoChunkId = "INFO";
    private const string ImageChunkId = "IMGE";
    private const string DataChunkId = "DATA";
    private const int CapsEncoder = 1;
    private const int SpsEncoder = 2;
    private const int MfmEncoder = 1;
    private const int RawEncoder = 2;
    private const int DataMask = 0x1F;
    private const int SizeShift = 5;
    private const int BlockDescriptorSize = 32;
    private const int BlockFlagGap0 = 1 << 0;
    private const int BlockFlagGap1 = 1 << 1;
    private const int BlockFlagDataSizeInBits = 1 << 2;

    /// <summary>
    /// Decodes an IPF image into raw track streams.
    /// </summary>
    /// <param name="image">The IPF image bytes.</param>
    /// <param name="options">Optional decode options.</param>
    /// <returns>The decoded IPF disk image.</returns>
    public static IpfDiskImage Decode(ReadOnlySpan<byte> image, IpfDecodeOptions? options = null)
    {
        options ??= IpfDecodeOptions.Default;
        var parser = new Parser(image);
        return parser.Decode(options);
    }

    private sealed class Parser
    {
        private readonly ReadOnlyMemory<byte> _image;
        private readonly Dictionary<uint, ImageDescriptor> _imageDescriptors = new Dictionary<uint, ImageDescriptor>();
        private readonly Dictionary<uint, DataDescriptor> _dataDescriptors = new Dictionary<uint, DataDescriptor>();
        private InfoDescriptor? _info;
        private bool _hasCapsHeader;

        public Parser(ReadOnlySpan<byte> image)
        {
            _image = image.ToArray();
        }

        public IpfDiskImage Decode(IpfDecodeOptions options)
        {
            ParseChunks();
            if (!_hasCapsHeader)
            {
                throw new IpfDecodeException("The image does not contain a CAPS header.");
            }

            var info = _info ?? throw new IpfDecodeException("The IPF image does not contain an INFO chunk.");
            if (info.Encoder is not CapsEncoder and not SpsEncoder)
            {
                throw new IpfDecodeException($"Unsupported IPF encoder {info.Encoder}.");
            }

            var tracks = new List<IpfTrack>();
            foreach (var descriptor in _imageDescriptors.Values.OrderBy(track => track.Cylinder).ThenBy(track => track.Head))
            {
                if (descriptor.BlockCount == 0)
                {
                    continue;
                }

                if (!_dataDescriptors.TryGetValue(descriptor.DataId, out var dataDescriptor))
                {
                    throw new IpfDecodeException($"Track {descriptor.Cylinder}/{descriptor.Head} references missing DATA id {descriptor.DataId}.");
                }

                tracks.Add(DecodeTrack(info, descriptor, dataDescriptor, options));
            }

            return new IpfDiskImage(info.MinCylinder, info.MaxCylinder, info.MinHead, info.MaxHead, tracks);
        }

        private void ParseChunks()
        {
            var data = _image.Span;
            var offset = 0;
            while (offset < data.Length)
            {
                if (offset + 12 > data.Length)
                {
                    throw new IpfDecodeException($"Truncated IPF chunk header at offset 0x{offset:X}.");
                }

                var id = Encoding.ASCII.GetString(data.Slice(offset, 4));
                var chunkSize = checked((int)ReadUInt32(data, offset + 4));
                if (chunkSize < 12 || offset + chunkSize > data.Length)
                {
                    throw new IpfDecodeException($"Invalid IPF chunk size {chunkSize} for {id} at offset 0x{offset:X}.");
                }

                var payloadOffset = offset + 12;
                var nextOffset = offset + chunkSize;
                switch (id)
                {
                    case CapsChunkId:
                        _hasCapsHeader = true;
                        break;
                    case InfoChunkId:
                        _info = ReadInfo(data, payloadOffset, chunkSize - 12);
                        break;
                    case ImageChunkId:
                        var imageDescriptor = ReadImageDescriptor(data, payloadOffset, chunkSize - 12);
                        _imageDescriptors[imageDescriptor.DataId] = imageDescriptor;
                        break;
                    case DataChunkId:
                        var dataDescriptor = ReadDataDescriptor(data, payloadOffset, chunkSize - 12, nextOffset);
                        _dataDescriptors[dataDescriptor.DataId] = dataDescriptor;
                        nextOffset = checked(nextOffset + dataDescriptor.Data.Length);
                        if (nextOffset > data.Length)
                        {
                            throw new IpfDecodeException($"DATA id {dataDescriptor.DataId} extends beyond the IPF image.");
                        }

                        break;
                }

                offset = nextOffset;
            }
        }

        private IpfTrack DecodeTrack(
            InfoDescriptor info,
            ImageDescriptor image,
            DataDescriptor data,
            IpfDecodeOptions options)
        {
            var blocks = ReadBlocks(info, image, data.Data.Span);
            var descriptorBits = blocks.Sum(block => checked((int)block.BlockBits + (int)block.GapBits));
            var trackBits = image.TrackBits == 0
                ? descriptorBits
                : checked((int)image.TrackBits);
            if (trackBits < descriptorBits)
            {
                throw new IpfDecodeException($"Track {image.Cylinder}/{image.Head} descriptor length {trackBits} bits is shorter than its block streams ({descriptorBits} bits).");
            }

            if (options.AlignTracksToWord && (trackBits & 15) != 0)
            {
                trackBits += 16 - (trackBits & 15);
            }

            var writer = new TrackBitWriter(trackBits);
            var context = new TrackEncodingContext(writer);
            var startBit = options.StartAtIndex ? image.StartBit % Math.Max(1, trackBits) : 0;
            writer.Position = startBit;
            for (var index = 0; index < blocks.Length; index++)
            {
                var block = blocks[index];
                WriteDataStream(data.Data.Span, blocks, index, block, context);

                var gapBits = checked((int)block.GapBits);
                if (index == blocks.Length - 1)
                {
                    gapBits += trackBits - descriptorBits;
                }

                WriteGapStream(data.Data.Span, block, gapBits, context);
            }

            return new IpfTrack(
                image.Cylinder,
                image.Head,
                trackBits,
                startBit,
                writer.ToArray(),
                context.Features | AmigaTrackFeatures.PreservedTrackData,
                context.Regions);
        }

        private static ImageBlock[] ReadBlocks(InfoDescriptor info, ImageDescriptor image, ReadOnlySpan<byte> data)
        {
            if (image.BlockCount < 0 || data.Length < checked(image.BlockCount * BlockDescriptorSize))
            {
                throw new IpfDecodeException($"Track {image.Cylinder}/{image.Head} has a truncated block descriptor table.");
            }

            var blocks = new ImageBlock[image.BlockCount];
            for (var index = 0; index < blocks.Length; index++)
            {
                var offset = index * BlockDescriptorSize;
                var flag = ReadUInt32(data, offset + 20);
                blocks[index] = new ImageBlock(
                    ReadUInt32(data, offset),
                    ReadUInt32(data, offset + 4),
                    info.Encoder == CapsEncoder ? 0 : ReadUInt32(data, offset + 8),
                    ReadUInt32(data, offset + 16),
                    info.Encoder == CapsEncoder ? 0 : flag,
                    ReadUInt32(data, offset + 24),
                    ReadUInt32(data, offset + 28));
            }

            return blocks;
        }

        private static void WriteDataStream(
            ReadOnlySpan<byte> data,
            ImageBlock[] blocks,
            int blockIndex,
            ImageBlock block,
            TrackEncodingContext context)
        {
            if (block.BlockBits == 0)
            {
                return;
            }

            var streamStart = checked((int)block.DataOffset);
            var streamEnd = blockIndex == blocks.Length - 1
                ? data.Length
                : checked((int)blocks[blockIndex + 1].DataOffset);
            if (streamStart < blocks.Length * BlockDescriptorSize || streamStart >= streamEnd || streamEnd > data.Length)
            {
                throw new IpfDecodeException($"Invalid data stream bounds for block {blockIndex}.");
            }

            var state = new StreamReadState(data.Slice(streamStart, streamEnd - streamStart), (block.Flags & BlockFlagDataSizeInBits) != 0);
            var targetBits = checked((int)block.BlockBits);
            var startWritten = context.Writer.BitsWritten;
            while (context.Writer.BitsWritten - startWritten < targetBits)
            {
                var element = state.ReadElement();
                if (element.Type == StreamElementType.End)
                {
                    break;
                }

                var remaining = targetBits - (context.Writer.BitsWritten - startWritten);
                var progress = WriteElement(element, block.EncoderType, remaining, context);
                if (progress == 0)
                {
                    throw new IpfDecodeException($"Unable to make progress while decoding block {blockIndex}.");
                }
            }

            var written = context.Writer.BitsWritten - startWritten;
            if (written != targetBits)
            {
                throw new IpfDecodeException($"Block {blockIndex} decoded {written} bits but expected {targetBits} bits.");
            }
        }

        private static void WriteGapStream(ReadOnlySpan<byte> data, ImageBlock block, int gapBits, TrackEncodingContext context)
        {
            if (gapBits <= 0)
            {
                return;
            }

            if ((block.Flags & (BlockFlagGap0 | BlockFlagGap1)) == 0)
            {
                WriteGeneratedGap(block, gapBits, context);
                return;
            }

            var streamStart = checked((int)block.GapOffset);
            if (streamStart < 0 || streamStart >= data.Length)
            {
                throw new IpfDecodeException("Stored IPF gap stream offset is outside the DATA chunk.");
            }

            var state = new StreamReadState(data.Slice(streamStart), (block.Flags & BlockFlagDataSizeInBits) != 0);
            var written = 0;
            while (written < gapBits)
            {
                var element = state.ReadElement();
                if (element.Type == StreamElementType.End)
                {
                    break;
                }

                var progress = WriteElement(element, block.EncoderType, gapBits - written, context);
                if (progress == 0)
                {
                    throw new IpfDecodeException("Unable to make progress while decoding a stored IPF gap stream.");
                }

                written += progress;
            }

            if (written != gapBits)
            {
                throw new IpfDecodeException($"Stored IPF gap stream decoded {written} bits but expected {gapBits} bits.");
            }
        }

        private static void WriteGeneratedGap(ImageBlock block, int gapBits, TrackEncodingContext context)
        {
            if (gapBits <= 0)
            {
                return;
            }

            if (block.EncoderType == RawEncoder)
            {
                context.Writer.WriteRepeatedByteBits((byte)block.GapValue, gapBits);
                return;
            }

            if (block.EncoderType != MfmEncoder)
            {
                throw new IpfDecodeException($"Unsupported block encoder {block.EncoderType}.");
            }

            Span<byte> gap = stackalloc byte[1];
            gap[0] = (byte)block.GapValue;
            var written = 0;
            while (written < gapBits)
            {
                var progress = context.WriteMfm(gap, gapBits - written);
                if (progress == 0)
                {
                    throw new IpfDecodeException("Unable to make progress while generating an MFM gap.");
                }

                written += progress;
            }
        }

        private static int WriteElement(
            StreamElement element,
            uint encoderType,
            int maxOutputBits,
            TrackEncodingContext context)
        {
            var startWritten = context.Writer.BitsWritten;
            switch (element.Type)
            {
                case StreamElementType.Mark:
                case StreamElementType.Raw:
                    context.WriteRaw(element.Data.Span, element.BitCount, maxOutputBits);
                    break;
                case StreamElementType.Data:
                case StreamElementType.Gap:
                    if (encoderType == MfmEncoder)
                    {
                        context.WriteMfm(element.Data.Span, maxOutputBits);
                    }
                    else if (encoderType == RawEncoder)
                    {
                        context.WriteRaw(element.Data.Span, element.BitCount, maxOutputBits);
                    }
                    else
                    {
                        throw new IpfDecodeException($"Unsupported block encoder {encoderType}.");
                    }

                    break;
                case StreamElementType.WeakData:
                    context.Features |= AmigaTrackFeatures.WeakData | AmigaTrackFeatures.ApproximateWeakData;
                    var weakBits = Math.Min(element.BitCount, maxOutputBits);
                    context.AddRegion(context.Writer.Position, weakBits, AmigaTrackFeatures.WeakData | AmigaTrackFeatures.ApproximateWeakData);
                    if (encoderType == MfmEncoder)
                    {
                        Span<byte> zero = stackalloc byte[1];
                        zero[0] = 0;
                        var written = 0;
                        while (written < weakBits)
                        {
                            var progress = context.WriteMfm(zero, weakBits - written);
                            if (progress == 0)
                            {
                                throw new IpfDecodeException("Unable to make progress while generating weak MFM data.");
                            }

                            written += progress;
                        }
                    }
                    else
                    {
                        context.Writer.WriteZeroBits(weakBits);
                    }

                    break;
                default:
                    throw new IpfDecodeException($"Unsupported stream element {element.Type}.");
            }

            return context.Writer.BitsWritten - startWritten;
        }

        private static InfoDescriptor ReadInfo(ReadOnlySpan<byte> data, int offset, int payloadSize)
        {
            if (payloadSize < 84)
            {
                throw new IpfDecodeException("INFO chunk is too small.");
            }

            return new InfoDescriptor(
                checked((int)ReadUInt32(data, offset + 4)),
                checked((int)ReadUInt32(data, offset + 8)),
                checked((int)ReadUInt32(data, offset + 24)),
                checked((int)ReadUInt32(data, offset + 28)),
                checked((int)ReadUInt32(data, offset + 32)),
                checked((int)ReadUInt32(data, offset + 36)),
                checked((int)ReadUInt32(data, offset + 48)));
        }

        private static ImageDescriptor ReadImageDescriptor(ReadOnlySpan<byte> data, int offset, int payloadSize)
        {
            if (payloadSize < 68)
            {
                throw new IpfDecodeException("IMGE chunk is too small.");
            }

            return new ImageDescriptor(
                checked((int)ReadUInt32(data, offset)),
                checked((int)ReadUInt32(data, offset + 4)),
                ReadUInt32(data, offset + 8),
                ReadUInt32(data, offset + 12),
                ReadUInt32(data, offset + 16),
                ReadUInt32(data, offset + 20),
                checked((int)ReadUInt32(data, offset + 24)),
                ReadUInt32(data, offset + 28),
                ReadUInt32(data, offset + 32),
                ReadUInt32(data, offset + 36),
                checked((int)ReadUInt32(data, offset + 40)),
                ReadUInt32(data, offset + 44),
                ReadUInt32(data, offset + 48),
                ReadUInt32(data, offset + 52));
        }

        private static DataDescriptor ReadDataDescriptor(ReadOnlySpan<byte> data, int offset, int payloadSize, int dataOffset)
        {
            if (payloadSize < 16)
            {
                throw new IpfDecodeException("DATA chunk is too small.");
            }

            var size = checked((int)ReadUInt32(data, offset));
            var dataId = ReadUInt32(data, offset + 12);
            if (dataOffset < 0 || dataOffset + size > data.Length)
            {
                throw new IpfDecodeException($"DATA id {dataId} extends beyond the image.");
            }

            return new DataDescriptor(dataId, data.Slice(dataOffset, size).ToArray());
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        {
            if ((uint)offset > (uint)(data.Length - 4))
            {
                throw new IpfDecodeException("Unexpected end of IPF data.");
            }

            return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
        }
    }

    private sealed class StreamReadState
    {
        private readonly ReadOnlyMemory<byte> _stream;
        private readonly bool _sizeInBits;
        private int _offset;

        public StreamReadState(ReadOnlySpan<byte> stream, bool sizeInBits)
        {
            _stream = stream.ToArray();
            _sizeInBits = sizeInBits;
        }

        public StreamElement ReadElement()
        {
            var stream = _stream.Span;
            if (_offset >= stream.Length)
            {
                throw new IpfDecodeException("IPF stream ended before an end marker.");
            }

            var header = stream[_offset++];
            var sizeLength = header >> SizeShift;
            var type = (StreamElementType)(header & DataMask);
            var count = 0;
            for (var index = 0; index < sizeLength; index++)
            {
                if (_offset >= stream.Length)
                {
                    throw new IpfDecodeException("IPF stream size field is truncated.");
                }

                count = checked((count << 8) | stream[_offset++]);
            }

            if (type == StreamElementType.End)
            {
                if (count != 0)
                {
                    throw new IpfDecodeException("IPF stream end marker contains a size.");
                }

                return StreamElement.End;
            }

            if (type == StreamElementType.WeakData)
            {
                if (count <= 0)
                {
                    throw new IpfDecodeException("Weak-data stream element has no size.");
                }

                return new StreamElement(type, ReadOnlyMemory<byte>.Empty, _sizeInBits ? count : count * 8);
            }

            if (count <= 0)
            {
                throw new IpfDecodeException($"IPF stream element {type} has no sample data.");
            }

            var bitCount = _sizeInBits ? count : count * 8;
            var byteCount = (bitCount + 7) / 8;
            if (_offset + byteCount > stream.Length)
            {
                throw new IpfDecodeException($"IPF stream element {type} is truncated.");
            }

            var sample = stream.Slice(_offset, byteCount).ToArray();
            _offset += byteCount;
            return new StreamElement(type, sample, bitCount);
        }
    }

    private sealed class TrackEncodingContext
    {
        public TrackEncodingContext(TrackBitWriter writer)
        {
            Writer = writer;
        }

        public TrackBitWriter Writer { get; }

        public AmigaTrackFeatures Features { get; set; }

        public List<AmigaTrackRegion> Regions { get; } = new List<AmigaTrackRegion>();

        private bool PreviousDataBit { get; set; }

        public int WriteRaw(ReadOnlySpan<byte> data, int bitCount, int maxOutputBits)
        {
            var bits = Math.Min(bitCount, maxOutputBits);
            for (var index = 0; index < bits; index++)
            {
                var bit = ((data[index >> 3] >> (7 - (index & 7))) & 1) != 0;
                Writer.WriteBit(bit);
                PreviousDataBit = bit;
            }

            return bits;
        }

        public int WriteMfm(ReadOnlySpan<byte> data, int maxOutputBits)
        {
            var written = 0;
            for (var index = 0; index < data.Length && written < maxOutputBits; index++)
            {
                var value = data[index];
                for (var bitIndex = 7; bitIndex >= 0 && written < maxOutputBits; bitIndex--)
                {
                    var dataBit = ((value >> bitIndex) & 1) != 0;
                    var clockBit = !PreviousDataBit && !dataBit;
                    Writer.WriteBit(clockBit);
                    written++;
                    if (written < maxOutputBits)
                    {
                        Writer.WriteBit(dataBit);
                        written++;
                    }

                    PreviousDataBit = dataBit;
                }
            }

            return written;
        }

        public void AddRegion(int startBit, int bitCount, AmigaTrackFeatures features)
        {
            if (bitCount <= 0 || Writer.BitLength <= 0 || features == AmigaTrackFeatures.None)
            {
                return;
            }

            var start = AmigaEncodedTrack.WrapBitOffset(startBit, Writer.BitLength);
            var remaining = Math.Min(bitCount, Writer.BitLength);
            while (remaining > 0)
            {
                var length = Math.Min(remaining, Writer.BitLength - start);
                Regions.Add(new AmigaTrackRegion(start, length, features));
                remaining -= length;
                start = 0;
            }
        }
    }

    private sealed class TrackBitWriter
    {
        private readonly byte[] _data;

        public TrackBitWriter(int bitLength)
        {
            if (bitLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitLength));
            }

            BitLength = bitLength;
            _data = new byte[(bitLength + 7) / 8];
        }

        public int BitLength { get; }

        public int Position { get; set; }

        public int BitsWritten { get; private set; }

        public void WriteBit(bool bit)
        {
            if (BitLength == 0)
            {
                return;
            }

            var position = Position % BitLength;
            if (bit)
            {
                _data[position >> 3] |= (byte)(0x80 >> (position & 7));
            }
            else
            {
                _data[position >> 3] &= (byte)~(0x80 >> (position & 7));
            }

            Position = (position + 1) % BitLength;
            BitsWritten++;
        }

        public void WriteZeroBits(int bitCount)
        {
            for (var index = 0; index < bitCount; index++)
            {
                WriteBit(false);
            }
        }

        public void WriteRepeatedByteBits(byte value, int bitCount)
        {
            for (var index = 0; index < bitCount; index++)
            {
                WriteBit(((value >> (7 - (index & 7))) & 1) != 0);
            }
        }

        public byte[] ToArray()
        {
            return _data;
        }
    }

    private readonly record struct InfoDescriptor(
        int Encoder,
        int EncoderRevision,
        int MinCylinder,
        int MaxCylinder,
        int MinHead,
        int MaxHead,
        int Platform);

    private readonly record struct ImageDescriptor(
        int Cylinder,
        int Head,
        uint DensityType,
        uint SignalType,
        uint TrackSize,
        uint StartPosition,
        int StartBit,
        uint DataBits,
        uint GapBits,
        uint TrackBits,
        int BlockCount,
        uint Process,
        uint Flags,
        uint DataId);

    private readonly record struct ImageBlock(
        uint BlockBits,
        uint GapBits,
        uint GapOffset,
        uint EncoderType,
        uint Flags,
        uint GapValue,
        uint DataOffset);

    private readonly record struct DataDescriptor(uint DataId, ReadOnlyMemory<byte> Data);

    private enum StreamElementType
    {
        End = 0,
        Mark = 1,
        Data = 2,
        Gap = 3,
        Raw = 4,
        WeakData = 5
    }

    private readonly record struct StreamElement(StreamElementType Type, ReadOnlyMemory<byte> Data, int BitCount)
    {
        public static StreamElement End { get; } = new StreamElement(StreamElementType.End, ReadOnlyMemory<byte>.Empty, 0);
    }
}
