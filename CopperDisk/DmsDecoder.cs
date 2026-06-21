namespace CopperDisk;

// DMS decoding follows the public-domain xDMS format and decrunch behavior by
// Andre Rodrigues de la Rocha: https://github.com/timofonic-retro/xdms.
internal static class DmsDecoder
{
    private const int HeaderLength = 56;
    private const int TrackHeaderLength = 20;
    private const int TrackBufferLength = 32000;
    private const int CylinderBytes = AmigaDiskGeometry.HeadCount * AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize;

    public static byte[] Decode(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderLength)
        {
            throw Error("File is too short to contain a DMS header.");
        }

        if (image[0] != (byte)'D' || image[1] != (byte)'M' || image[2] != (byte)'S' || image[3] != (byte)'!')
        {
            throw Error("File header does not start with DMS!.");
        }

        var headerCrc = ReadUInt16(image, HeaderLength - 2);
        if (headerCrc != CreateCrc(image.Slice(4, HeaderLength - 6)))
        {
            throw Error("Header CRC is invalid.");
        }

        var generalInfo = ReadUInt16(image, 10);
        var unpackedSize = ReadUInt24(image, 25);
        var diskType = ReadUInt16(image, 50);
        var commonMode = ReadUInt16(image, 52);

        if ((generalInfo & 0x02) != 0)
        {
            throw Error("Encrypted DMS images are not supported.");
        }

        if ((generalInfo & 0x10) != 0)
        {
            throw Error("High-density DMS images are not supported.");
        }

        if ((generalInfo & 0x20) != 0)
        {
            throw Error("MS-DOS DMS images are not supported.");
        }

        if (diskType == 7)
        {
            throw Error("FMS archives are not Amiga disk images.");
        }

        if (diskType > 6)
        {
            throw Error($"Unsupported DMS disk type {diskType}.");
        }

        if (commonMode > 6)
        {
            throw Error($"Unsupported DMS compression mode {commonMode}.");
        }

        if (unpackedSize != AmigaDiskGeometry.StandardAdfSize)
        {
            throw Error($"Only standard {AmigaDiskGeometry.StandardAdfSize}-byte DMS disk images are supported.");
        }

        var disk = new byte[AmigaDiskGeometry.StandardAdfSize];
        var seenCylinders = new bool[AmigaDiskGeometry.CylinderCount];
        var decruncher = new Decruncher();
        var offset = HeaderLength;

        while (offset < image.Length)
        {
            if (image.Length - offset < TrackHeaderLength)
            {
                if (IsTrailingPadding(image[offset..]))
                {
                    break;
                }

                throw Error("Unexpected end of file while reading a DMS track header.");
            }

            var trackHeader = image.Slice(offset, TrackHeaderLength);
            if (trackHeader[0] != (byte)'T' || trackHeader[1] != (byte)'R')
            {
                if (IsTrailingPadding(image[offset..]))
                {
                    break;
                }

                throw Error("Track header marker was not found.");
            }

            var storedTrackHeaderCrc = ReadUInt16(trackHeader, TrackHeaderLength - 2);
            if (storedTrackHeaderCrc != CreateCrc(trackHeader[..(TrackHeaderLength - 2)]))
            {
                throw Error("Track header CRC is invalid.");
            }

            var cylinder = ReadUInt16(trackHeader, 2);
            var packedLength = ReadUInt16(trackHeader, 6);
            var firstUnpackedLength = ReadUInt16(trackHeader, 8);
            var finalLength = ReadUInt16(trackHeader, 10);
            var flags = trackHeader[12];
            var mode = trackHeader[13];
            var expectedChecksum = ReadUInt16(trackHeader, 14);
            var expectedDataCrc = ReadUInt16(trackHeader, 16);

            offset += TrackHeaderLength;
            if (packedLength > TrackBufferLength ||
                firstUnpackedLength > TrackBufferLength ||
                finalLength > TrackBufferLength)
            {
                throw Error("A DMS track is larger than the supported track buffer.");
            }

            if (image.Length - offset < packedLength)
            {
                throw Error("Unexpected end of file while reading DMS track data.");
            }

            var packed = image.Slice(offset, packedLength);
            offset += packedLength;

            if (CreateCrc(packed) != expectedDataCrc)
            {
                throw Error($"Track {FormatCylinder(cylinder)} data CRC is invalid.");
            }

            if (mode > 6)
            {
                throw Error($"Unsupported track compression mode {mode}.");
            }

            if (cylinder >= AmigaDiskGeometry.CylinderCount || finalLength <= 2048)
            {
                continue;
            }

            if (seenCylinders[cylinder])
            {
                throw Error($"Track {cylinder} appears more than once.");
            }

            if (finalLength != CylinderBytes)
            {
                throw Error($"Track {cylinder} has {finalLength} unpacked bytes; expected {CylinderBytes}.");
            }

            var decoded = decruncher.UnpackTrack(packed, firstUnpackedLength, finalLength, mode, flags);
            if (CalcChecksum(decoded) != expectedChecksum)
            {
                throw Error($"Track {cylinder} checksum is invalid after unpacking.");
            }

            decoded.CopyTo(disk.AsSpan(cylinder * CylinderBytes, CylinderBytes));
            seenCylinders[cylinder] = true;
        }

        for (var cylinder = 0; cylinder < seenCylinders.Length; cylinder++)
        {
            if (!seenCylinders[cylinder])
            {
                throw Error($"DMS image is incomplete; track {cylinder} is missing.");
            }
        }

        return disk;
    }

    internal static ushort CreateCrc(ReadOnlySpan<byte> data)
    {
        var crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
            }
        }

        return (ushort)crc;
    }

    internal static ushort CalcChecksum(ReadOnlySpan<byte> data)
    {
        var checksum = 0;
        foreach (var value in data)
        {
            checksum = (checksum + value) & 0xFFFF;
        }

        return (ushort)checksum;
    }

    private static AmigaDiskException Error(string message)
        => new("Unable to decode DMS disk image: " + message);

    private static string FormatCylinder(ushort cylinder)
        => cylinder == 0xFFFF ? "banner" : cylinder.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsTrailingPadding(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt24(ReadOnlySpan<byte> data, int offset)
        => (uint)((data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2]);

    private sealed class Decruncher
    {
        private const int TextBufferLength = 32000;
        private const int QuickBitMask = 0x00FF;
        private const int MediumBitMask = 0x3FFF;
        private const int DeepBitMask = 0x3FFF;
        private const int DeepLookaheadSize = 60;
        private const int DeepThreshold = 2;
        private const int DeepCharCount = 256 - DeepThreshold + DeepLookaheadSize;
        private const int DeepTableSize = (DeepCharCount * 2) - 1;
        private const int DeepRoot = DeepTableSize - 1;
        private const int DeepMaxFrequency = 0x8000;
        private const int HeavyCharCount = 510;
        private const int HeavyPositionTableCount = 20;
        private const int HeavyN1 = 510;
        private const int HeavyOffset = 253;

        private readonly byte[] _text = new byte[TextBufferLength];
        private readonly ushort[] _deepFreq = new ushort[DeepTableSize + 1];
        private readonly ushort[] _deepParent = new ushort[DeepTableSize + DeepCharCount];
        private readonly ushort[] _deepSon = new ushort[DeepTableSize];
        private readonly ushort[] _left = new ushort[(2 * HeavyCharCount) - 1];
        private readonly ushort[] _right = new ushort[(2 * HeavyCharCount) - 1 + 9];
        private readonly byte[] _heavyCharLengths = new byte[HeavyCharCount];
        private readonly byte[] _heavyPositionLengths = new byte[HeavyPositionTableCount];
        private readonly ushort[] _heavyCharTable = new ushort[4096];
        private readonly ushort[] _heavyPositionTable = new ushort[256];

        private ushort _quickTextLocation;
        private ushort _mediumTextLocation;
        private ushort _deepTextLocation;
        private ushort _heavyTextLocation;
        private ushort _heavyLastLength;
        private ushort _heavyPositionCount;
        private bool _initializeDeepTables;

        public Decruncher()
        {
            Initialize();
        }

        public byte[] UnpackTrack(ReadOnlySpan<byte> packed, ushort firstUnpackedLength, ushort finalLength, byte mode, byte flags)
        {
            var final = new byte[finalLength];
            switch (mode)
            {
                case 0:
                    if (packed.Length < finalLength)
                    {
                        throw Error("Uncompressed track data is shorter than its declared size.");
                    }

                    packed[..finalLength].CopyTo(final);
                    break;
                case 1:
                    UnpackRle(packed, final);
                    break;
                case 2:
                    UnpackRle(UnpackQuick(packed, firstUnpackedLength), final);
                    break;
                case 3:
                    UnpackRle(UnpackMedium(packed, firstUnpackedLength), final);
                    break;
                case 4:
                    UnpackRle(UnpackDeep(packed, firstUnpackedLength), final);
                    break;
                case 5:
                case 6:
                    var first = UnpackHeavy(packed, (byte)(mode == 5 ? flags & 7 : flags | 8), firstUnpackedLength);
                    if ((flags & 4) != 0)
                    {
                        UnpackRle(first, final);
                    }
                    else
                    {
                        if (first.Length != final.Length)
                        {
                            throw Error("Heavy-compressed track length does not match the final track length.");
                        }

                        first.CopyTo(final, 0);
                    }

                    break;
                default:
                    throw Error($"Unsupported track compression mode {mode}.");
            }

            if ((flags & 1) == 0)
            {
                Initialize();
            }

            return final;
        }

        private void Initialize()
        {
            _quickTextLocation = 251;
            _mediumTextLocation = 0x3FBE;
            _heavyTextLocation = 0;
            _deepTextLocation = 0x3FC4;
            _initializeDeepTables = true;
            Array.Clear(_text, 0, 0x3FC8);
        }

        private static void UnpackRle(ReadOnlySpan<byte> input, Span<byte> output)
        {
            var inputOffset = 0;
            var outputOffset = 0;
            while (outputOffset < output.Length)
            {
                if (inputOffset >= input.Length)
                {
                    throw Error("RLE data ended before the declared output size.");
                }

                var value = input[inputOffset++];
                if (value != 0x90)
                {
                    output[outputOffset++] = value;
                    continue;
                }

                if (inputOffset >= input.Length)
                {
                    throw Error("RLE marker is missing a count byte.");
                }

                var countMarker = input[inputOffset++];
                if (countMarker == 0)
                {
                    output[outputOffset++] = value;
                    continue;
                }

                if (inputOffset >= input.Length)
                {
                    throw Error("RLE run is missing a value byte.");
                }

                value = input[inputOffset++];
                int count;
                if (countMarker == 0xFF)
                {
                    if (input.Length - inputOffset < 2)
                    {
                        throw Error("Long RLE run is missing its length.");
                    }

                    count = (input[inputOffset] << 8) | input[inputOffset + 1];
                    inputOffset += 2;
                }
                else
                {
                    count = countMarker;
                }

                if (count < 0 || outputOffset + count > output.Length)
                {
                    throw Error("RLE run exceeds the declared output size.");
                }

                output.Slice(outputOffset, count).Fill(value);
                outputOffset += count;
            }
        }

        private byte[] UnpackQuick(ReadOnlySpan<byte> input, int outputLength)
        {
            var reader = new BitReader(input);
            var output = new byte[outputLength];
            var outputOffset = 0;
            while (outputOffset < output.Length)
            {
                if (reader.GetBits(1) != 0)
                {
                    reader.DropBits(1);
                    var value = (byte)reader.GetBits(8);
                    reader.DropBits(8);
                    output[outputOffset++] = _text[_quickTextLocation++ & QuickBitMask] = value;
                }
                else
                {
                    reader.DropBits(1);
                    var count = reader.GetBits(2) + 2;
                    reader.DropBits(2);
                    var source = _quickTextLocation - reader.GetBits(8) - 1;
                    reader.DropBits(8);
                    CopyFromText(output, ref outputOffset, ref _quickTextLocation, source, count, QuickBitMask);
                }
            }

            _quickTextLocation = (ushort)((_quickTextLocation + 5) & QuickBitMask);
            return output;
        }

        private byte[] UnpackMedium(ReadOnlySpan<byte> input, int outputLength)
        {
            var reader = new BitReader(input);
            var output = new byte[outputLength];
            var outputOffset = 0;
            while (outputOffset < output.Length)
            {
                if (reader.GetBits(1) != 0)
                {
                    reader.DropBits(1);
                    var value = (byte)reader.GetBits(8);
                    reader.DropBits(8);
                    output[outputOffset++] = _text[_mediumTextLocation++ & MediumBitMask] = value;
                    continue;
                }

                reader.DropBits(1);
                var c = reader.GetBits(8);
                reader.DropBits(8);
                var count = DCode(c) + 3;
                var bits = DLen(c);
                c = (ushort)(((c << bits) | reader.GetBits(bits)) & 0xFF);
                reader.DropBits(bits);
                bits = DLen(c);
                c = (ushort)((DCode(c) << 8) | (((c << bits) | reader.GetBits(bits)) & 0xFF));
                reader.DropBits(bits);
                var source = _mediumTextLocation - c - 1;
                CopyFromText(output, ref outputOffset, ref _mediumTextLocation, source, count, MediumBitMask);
            }

            _mediumTextLocation = (ushort)((_mediumTextLocation + 66) & MediumBitMask);
            return output;
        }

        private byte[] UnpackDeep(ReadOnlySpan<byte> input, int outputLength)
        {
            var reader = new BitReader(input);
            if (_initializeDeepTables)
            {
                InitializeDeepTables();
            }

            var output = new byte[outputLength];
            var outputOffset = 0;
            while (outputOffset < output.Length)
            {
                var c = DecodeDeepChar(ref reader);
                if (c < 256)
                {
                    output[outputOffset++] = _text[_deepTextLocation++ & DeepBitMask] = (byte)c;
                    continue;
                }

                var count = c - 255 + DeepThreshold;
                var source = _deepTextLocation - DecodeDeepPosition(ref reader) - 1;
                CopyFromText(output, ref outputOffset, ref _deepTextLocation, source, count, DeepBitMask);
            }

            _deepTextLocation = (ushort)((_deepTextLocation + 60) & DeepBitMask);
            return output;
        }

        private byte[] UnpackHeavy(ReadOnlySpan<byte> input, byte flags, int outputLength)
        {
            _heavyPositionCount = (ushort)((flags & 8) != 0 ? 15 : 14);
            var bitMask = (flags & 8) != 0 ? 0x1FFF : 0x0FFF;
            var reader = new BitReader(input);
            if ((flags & 2) != 0)
            {
                ReadHeavyCharTree(ref reader);
                ReadHeavyPositionTree(ref reader);
            }

            var output = new byte[outputLength];
            var outputOffset = 0;
            while (outputOffset < output.Length)
            {
                var c = DecodeHeavyChar(ref reader);
                if (c < 256)
                {
                    output[outputOffset++] = _text[_heavyTextLocation++ & bitMask] = (byte)c;
                    continue;
                }

                var count = c - HeavyOffset;
                var source = _heavyTextLocation - DecodeHeavyPosition(ref reader) - 1;
                CopyFromText(output, ref outputOffset, ref _heavyTextLocation, source, count, bitMask);
            }

            return output;
        }

        private void InitializeDeepTables()
        {
            ushort i;
            for (i = 0; i < DeepCharCount; i++)
            {
                _deepFreq[i] = 1;
                _deepSon[i] = (ushort)(i + DeepTableSize);
                _deepParent[i + DeepTableSize] = i;
            }

            i = 0;
            var j = DeepCharCount;
            while (j <= DeepRoot)
            {
                _deepFreq[j] = (ushort)(_deepFreq[i] + _deepFreq[i + 1]);
                _deepSon[j] = i;
                _deepParent[i] = (ushort)j;
                _deepParent[i + 1] = (ushort)j;
                i += 2;
                j++;
            }

            _deepFreq[DeepTableSize] = 0xFFFF;
            _deepParent[DeepRoot] = 0;
            _initializeDeepTables = false;
        }

        private ushort DecodeDeepChar(ref BitReader reader)
        {
            var c = _deepSon[DeepRoot];
            while (c < DeepTableSize)
            {
                c = _deepSon[c + reader.GetBits(1)];
                reader.DropBits(1);
            }

            c -= DeepTableSize;
            UpdateDeepTree(c);
            return (ushort)c;
        }

        private ushort DecodeDeepPosition(ref BitReader reader)
        {
            var i = reader.GetBits(8);
            reader.DropBits(8);
            var c = DCode(i) << 8;
            var bits = DLen(i);
            i = (ushort)(((i << bits) | reader.GetBits(bits)) & 0xFF);
            reader.DropBits(bits);
            return (ushort)(c | i);
        }

        private void ReconstructDeepTree()
        {
            var j = 0;
            for (var i = 0; i < DeepTableSize; i++)
            {
                if (_deepSon[i] >= DeepTableSize)
                {
                    _deepFreq[j] = (ushort)((_deepFreq[i] + 1) / 2);
                    _deepSon[j] = _deepSon[i];
                    j++;
                }
            }

            for (var i = 0; j < DeepTableSize; i += 2, j++)
            {
                var k = i + 1;
                var frequency = (ushort)(_deepFreq[i] + _deepFreq[k]);
                _deepFreq[j] = frequency;
                k = j - 1;
                while (frequency < _deepFreq[k])
                {
                    k--;
                }

                k++;
                var count = j - k;
                Array.Copy(_deepFreq, k, _deepFreq, k + 1, count);
                _deepFreq[k] = frequency;
                Array.Copy(_deepSon, k, _deepSon, k + 1, count);
                _deepSon[k] = (ushort)i;
            }

            for (var i = 0; i < DeepTableSize; i++)
            {
                var k = _deepSon[i];
                if (k >= DeepTableSize)
                {
                    _deepParent[k] = (ushort)i;
                }
                else
                {
                    _deepParent[k] = (ushort)i;
                    _deepParent[k + 1] = (ushort)i;
                }
            }
        }

        private void UpdateDeepTree(ushort c)
        {
            if (_deepFreq[DeepRoot] == DeepMaxFrequency)
            {
                ReconstructDeepTree();
            }

            c = _deepParent[c + DeepTableSize];
            do
            {
                var k = ++_deepFreq[c];
                var l = c + 1;
                if (k > _deepFreq[l])
                {
                    do
                    {
                        l++;
                    }
                    while (k > _deepFreq[l]);
                    l--;
                    _deepFreq[c] = _deepFreq[l];
                    _deepFreq[l] = k;

                    var i = _deepSon[c];
                    _deepParent[i] = (ushort)l;
                    if (i < DeepTableSize)
                    {
                        _deepParent[i + 1] = (ushort)l;
                    }

                    var j = _deepSon[l];
                    _deepSon[l] = i;

                    _deepParent[j] = c;
                    if (j < DeepTableSize)
                    {
                        _deepParent[j + 1] = c;
                    }

                    _deepSon[c] = j;
                    c = (ushort)l;
                }
            }
            while ((c = _deepParent[c]) != 0);
        }

        private ushort DecodeHeavyChar(ref BitReader reader)
        {
            var j = _heavyCharTable[reader.GetBits(12)];
            if (j < HeavyN1)
            {
                reader.DropBits(_heavyCharLengths[j]);
            }
            else
            {
                reader.DropBits(12);
                var i = reader.GetBits(16);
                var mask = 0x8000;
                do
                {
                    j = (i & mask) != 0 ? _right[j] : _left[j];
                    mask >>= 1;
                }
                while (j >= HeavyN1);
                reader.DropBits(_heavyCharLengths[j] - 12);
            }

            return j;
        }

        private ushort DecodeHeavyPosition(ref BitReader reader)
        {
            var j = _heavyPositionTable[reader.GetBits(8)];
            if (j < _heavyPositionCount)
            {
                reader.DropBits(_heavyPositionLengths[j]);
            }
            else
            {
                reader.DropBits(8);
                var i = reader.GetBits(16);
                var mask = 0x8000;
                do
                {
                    j = (i & mask) != 0 ? _right[j] : _left[j];
                    mask >>= 1;
                }
                while (j >= _heavyPositionCount);
                reader.DropBits(_heavyPositionLengths[j] - 8);
            }

            if (j != _heavyPositionCount - 1)
            {
                if (j > 0)
                {
                    var bits = j - 1;
                    j = (ushort)(reader.GetBits(bits) | (1U << bits));
                    reader.DropBits(bits);
                }

                _heavyLastLength = j;
            }

            return _heavyLastLength;
        }

        private void ReadHeavyCharTree(ref BitReader reader)
        {
            var count = reader.GetBits(9);
            reader.DropBits(9);
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    _heavyCharLengths[i] = (byte)reader.GetBits(5);
                    reader.DropBits(5);
                }

                Array.Clear(_heavyCharLengths, count, _heavyCharLengths.Length - count);
                MakeTable(HeavyCharCount, _heavyCharLengths, 12, _heavyCharTable);
            }
            else
            {
                var value = reader.GetBits(9);
                reader.DropBits(9);
                Array.Clear(_heavyCharLengths);
                Array.Fill(_heavyCharTable, value);
            }
        }

        private void ReadHeavyPositionTree(ref BitReader reader)
        {
            var count = reader.GetBits(5);
            reader.DropBits(5);
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    _heavyPositionLengths[i] = (byte)reader.GetBits(4);
                    reader.DropBits(4);
                }

                Array.Clear(_heavyPositionLengths, count, _heavyPositionLengths.Length - count);
                MakeTable(_heavyPositionCount, _heavyPositionLengths, 8, _heavyPositionTable);
            }
            else
            {
                var value = reader.GetBits(5);
                reader.DropBits(5);
                Array.Clear(_heavyPositionLengths);
                Array.Fill(_heavyPositionTable, value);
            }
        }

        private void MakeTable(int charCount, byte[] bitLengths, int tableBits, ushort[] table)
        {
            var state = new TableBuilder(this, charCount, bitLengths, tableBits, table);
            state.Build();
        }

        private struct TableBuilder
        {
            private readonly Decruncher _owner;
            private readonly int _charCount;
            private readonly byte[] _bitLengths;
            private readonly ushort[] _table;
            private readonly int _tableSize;
            private readonly int _maxDepth;
            private readonly int _initialBit;
            private readonly int _maxNodes;

            private int _currentChar;
            private int _length;
            private int _depth;
            private int _available;
            private int _codeword;
            private int _bit;

            public TableBuilder(Decruncher owner, int charCount, byte[] bitLengths, int tableBits, ushort[] table)
            {
                _owner = owner;
                _charCount = charCount;
                _bitLengths = bitLengths;
                _table = table;
                _tableSize = 1 << tableBits;
                _maxDepth = tableBits + 1;
                _initialBit = _tableSize / 2;
                _maxNodes = (2 * charCount) - 1;
                _currentChar = -1;
                _length = 1;
                _depth = 1;
                _available = charCount;
                _codeword = 0;
                _bit = _initialBit;
            }

            public void Build()
            {
                var builder = this;
                builder.MakeSubtree();
                builder.MakeSubtree();
                if (builder._codeword != builder._tableSize)
                {
                    throw Error("Invalid heavy compression table.");
                }
            }

            private ushort MakeSubtree()
            {
                if (_length == _depth)
                {
                    while (++_currentChar < _charCount)
                    {
                        if (_bitLengths[_currentChar] != _length)
                        {
                            continue;
                        }

                        var start = _codeword;
                        _codeword += _bit;
                        if (_codeword > _tableSize)
                        {
                            throw Error("Heavy compression table overflows.");
                        }

                        while (start < _codeword)
                        {
                            _table[start++] = (ushort)_currentChar;
                        }

                        return (ushort)_currentChar;
                    }

                    _currentChar = -1;
                    _length++;
                    _bit >>= 1;
                }

                _depth++;
                ushort node;
                if (_depth < _maxDepth)
                {
                    MakeSubtree();
                    node = MakeSubtree();
                }
                else if (_depth > 32)
                {
                    throw Error("Heavy compression table is too deep.");
                }
                else
                {
                    var index = _available++;
                    if (index >= _maxNodes)
                    {
                        throw Error("Heavy compression table has too many nodes.");
                    }

                    _owner._left[index] = MakeSubtree();
                    _owner._right[index] = MakeSubtree();
                    if (_codeword >= _tableSize)
                    {
                        throw Error("Heavy compression table overflows.");
                    }

                    if (_depth == _maxDepth)
                    {
                        _table[_codeword++] = (ushort)index;
                    }

                    node = (ushort)index;
                }

                _depth--;
                return node;
            }
        }

        private void CopyFromText(
            Span<byte> output,
            ref int outputOffset,
            ref ushort textLocation,
            int source,
            int count,
            int bitMask)
        {
            if (outputOffset + count > output.Length)
            {
                throw Error("Compressed back-reference exceeds the declared output size.");
            }

            while (count-- > 0)
            {
                var value = _text[source++ & bitMask];
                output[outputOffset++] = _text[textLocation++ & bitMask] = value;
            }
        }

        private static ushort DCode(int value)
        {
            if (value < 32)
            {
                return 0;
            }

            if (value < 80)
            {
                return (ushort)(1 + ((value - 32) / 16));
            }

            if (value < 144)
            {
                return (ushort)(4 + ((value - 80) / 8));
            }

            if (value < 192)
            {
                return (ushort)(12 + ((value - 144) / 4));
            }

            if (value < 240)
            {
                return (ushort)(24 + ((value - 192) / 2));
            }

            return (ushort)(48 + (value - 240));
        }

        private static int DLen(int value)
        {
            if (value < 32)
            {
                return 3;
            }

            if (value < 80)
            {
                return 4;
            }

            if (value < 144)
            {
                return 5;
            }

            if (value < 192)
            {
                return 6;
            }

            if (value < 240)
            {
                return 7;
            }

            return 8;
        }
    }

    private ref struct BitReader
    {
        private static readonly uint[] MaskBits =
        [
            0x000000, 0x000001, 0x000003, 0x000007, 0x00000F, 0x00001F,
            0x00003F, 0x00007F, 0x0000FF, 0x0001FF, 0x0003FF, 0x0007FF,
            0x000FFF, 0x001FFF, 0x003FFF, 0x007FFF, 0x00FFFF, 0x01FFFF,
            0x03FFFF, 0x07FFFF, 0x0FFFFF, 0x1FFFFF, 0x3FFFFF, 0x7FFFFF,
            0xFFFFFF
        ];

        private readonly ReadOnlySpan<byte> _input;
        private int _offset;
        private uint _bitBuffer;
        private int _bitCount;

        public BitReader(ReadOnlySpan<byte> input)
        {
            _input = input;
            _offset = 0;
            _bitBuffer = 0;
            _bitCount = 0;
            DropBits(0);
        }

        public ushort GetBits(int count)
            => (ushort)(count == 0 ? 0 : _bitBuffer >> (_bitCount - count));

        public void DropBits(int count)
        {
            if (count < 0 || count > _bitCount)
            {
                throw Error("Compressed bit stream is invalid.");
            }

            _bitCount -= count;
            _bitBuffer &= MaskBits[_bitCount];
            while (_bitCount < 16)
            {
                _bitBuffer = (_bitBuffer << 8) | ReadByteOrZero();
                _bitCount += 8;
            }
        }

        private byte ReadByteOrZero()
        {
            if (_offset >= _input.Length)
            {
                return 0;
            }

            return _input[_offset++];
        }
    }
}
