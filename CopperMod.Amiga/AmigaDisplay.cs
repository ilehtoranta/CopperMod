using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal sealed class OcsDisplay
    {
        private const int MaxPendingWrites = 65536;
        private const int StandardHStart = 0x81;
        private const int StandardVStart = 0x2C;
        private const ushort DefaultDiwStart = 0x2C81;
        private const ushort DefaultDiwStop = 0x2CC1;
        private const ushort DefaultDdfStart = 0x0038;
        private const ushort DefaultDdfStop = 0x00D0;
        private readonly AmigaBus _bus;
        private readonly List<PendingCustomWrite> _pendingWrites = new List<PendingCustomWrite>();
        private readonly ushort[] _colors = new ushort[32];
        private readonly uint[] _bitplanePointers = new uint[6];
        private readonly SpriteState[] _sprites = new SpriteState[8];
        private int _pendingIndex;
        private uint _copperListPointer;
        private ushort _diwStart;
        private ushort _diwStop;
        private ushort _ddfStart;
        private ushort _ddfStop;
        private ushort _bplcon0;
        private ushort _bplcon1;
        private short _bpl1mod;
        private short _bpl2mod;
        private int _lastBitplaneNonZeroPixels;
        private int _lastBitplaneRows;
        private int _lastBitplaneWords;

        public OcsDisplay(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = new SpriteState();
            }

            Reset();
        }

        public int Width => AmigaConstants.PalLowResWidth;

        public int Height => AmigaConstants.PalLowResHeight;

        public OcsDisplaySnapshot CaptureSnapshot()
        {
            var bitplanePointers = new uint[_bitplanePointers.Length];
            Array.Copy(_bitplanePointers, bitplanePointers, bitplanePointers.Length);
            var colors = new ushort[_colors.Length];
            Array.Copy(_colors, colors, colors.Length);
            return new OcsDisplaySnapshot(
                _bplcon0,
                _diwStart,
                _diwStop,
                _ddfStart,
                _ddfStop,
                _bpl1mod,
                _bpl2mod,
                _lastBitplaneNonZeroPixels,
                _lastBitplaneRows,
                _lastBitplaneWords,
                bitplanePointers,
                colors);
        }

        public void Reset()
        {
            _pendingWrites.Clear();
            _pendingIndex = 0;
            Array.Clear(_bitplanePointers);
            _copperListPointer = 0;
            _diwStart = DefaultDiwStart;
            _diwStop = DefaultDiwStop;
            _ddfStart = DefaultDdfStart;
            _ddfStop = DefaultDdfStop;
            _bplcon0 = 0;
            _bplcon1 = 0;
            _bpl1mod = 0;
            _bpl2mod = 0;
            _lastBitplaneNonZeroPixels = 0;
            _lastBitplaneRows = 0;
            _lastBitplaneWords = 0;
            Array.Clear(_colors);
            _colors[0] = 0x005;
            _colors[1] = 0xFFF;
            foreach (var sprite in _sprites)
            {
                sprite.Reset();
            }
        }

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
            if (_pendingWrites.Count >= MaxPendingWrites)
            {
                _pendingWrites.RemoveRange(0, MaxPendingWrites / 2);
                _pendingIndex = Math.Max(0, _pendingIndex - (MaxPendingWrites / 2));
            }

            _pendingWrites.Add(new PendingCustomWrite(cycle, offset, value));
        }

        public void RenderFrame(Span<uint> bgra)
        {
            if (bgra.Length < Width * Height)
            {
                throw new ArgumentException("The framebuffer is smaller than the PAL low-res display.", nameof(bgra));
            }

            ApplyPendingWrites(long.MaxValue);
            if (_copperListPointer != 0)
            {
                new AmigaCopper().ExecuteList(_bus, _copperListPointer, maxInstructions: 4096);
                ApplyPendingWrites(long.MaxValue);
            }

            bgra = bgra.Slice(0, Width * Height);
            bgra.Fill(ConvertColor(_colors[0]));
            RenderBitplanes(bgra);
            RenderSprites(bgra);
        }

        private void ApplyPendingWrites(long cycle)
        {
            while (_pendingIndex < _pendingWrites.Count && _pendingWrites[_pendingIndex].Cycle <= cycle)
            {
                var write = _pendingWrites[_pendingIndex++];
                ApplyWrite(write.Offset, write.Value);
            }

            if (_pendingIndex > 1024 && _pendingIndex * 2 > _pendingWrites.Count)
            {
                _pendingWrites.RemoveRange(0, _pendingIndex);
                _pendingIndex = 0;
            }
        }

        private void ApplyWrite(ushort offset, ushort value)
        {
            if (offset == 0x100)
            {
                _bplcon0 = value;
                return;
            }

            if (offset == 0x102)
            {
                _bplcon1 = value;
                return;
            }

            if (offset == 0x080)
            {
                _copperListPointer = (_copperListPointer & 0x0000_FFFF) | ((uint)value << 16);
                return;
            }

            if (offset == 0x082)
            {
                _copperListPointer = (_copperListPointer & 0xFFFF_0000) | value;
                return;
            }

            if (offset == 0x088)
            {
                return;
            }

            if (offset == 0x08E)
            {
                _diwStart = value;
                return;
            }

            if (offset == 0x090)
            {
                _diwStop = value;
                return;
            }

            if (offset == 0x092)
            {
                _ddfStart = value;
                return;
            }

            if (offset == 0x094)
            {
                _ddfStop = value;
                return;
            }

            if (offset == 0x108)
            {
                _bpl1mod = unchecked((short)value);
                return;
            }

            if (offset == 0x10A)
            {
                _bpl2mod = unchecked((short)value);
                return;
            }

            if (offset >= 0x180 && offset < 0x1C0)
            {
                _colors[(offset - 0x180) / 2] = (ushort)(value & 0x0FFF);
                return;
            }

            if (offset >= 0x0E0 && offset <= 0x0F6)
            {
                var plane = (offset - 0x0E0) / 4;
                if (plane < _bitplanePointers.Length)
                {
                    if ((offset & 2) == 0)
                    {
                        _bitplanePointers[plane] = (_bitplanePointers[plane] & 0x0000_FFFF) | ((uint)value << 16);
                    }
                    else
                    {
                        _bitplanePointers[plane] = (_bitplanePointers[plane] & 0xFFFF_0000) | value;
                    }
                }

                return;
            }

            if (offset >= 0x120 && offset < 0x180)
            {
                ApplySpriteWrite(offset, value);
            }
        }

        private void ApplySpriteWrite(ushort offset, ushort value)
        {
            if (offset >= 0x120 && offset < 0x140)
            {
                var sprite = (offset - 0x120) / 4;
                if (sprite < _sprites.Length)
                {
                    if ((offset & 2) == 0)
                    {
                        _sprites[sprite].Pointer = (_sprites[sprite].Pointer & 0x0000_FFFF) | ((uint)value << 16);
                    }
                    else
                    {
                        _sprites[sprite].Pointer = (_sprites[sprite].Pointer & 0xFFFF_0000) | value;
                    }
                }

                return;
            }

            if (offset >= 0x140 && offset < 0x180)
            {
                var sprite = (offset - 0x140) / 8;
                var register = (offset - 0x140) % 8;
                if (sprite >= _sprites.Length)
                {
                    return;
                }

                switch (register)
                {
                    case 0:
                        _sprites[sprite].Pos = value;
                        break;
                    case 2:
                        _sprites[sprite].Ctl = value;
                        break;
                    case 4:
                        _sprites[sprite].DataA = value;
                        break;
                    case 6:
                        _sprites[sprite].DataB = value;
                        break;
                }
            }
        }

        private void RenderBitplanes(Span<uint> bgra)
        {
            _lastBitplaneNonZeroPixels = 0;
            _lastBitplaneRows = 0;
            _lastBitplaneWords = 0;
            var planeCount = (_bplcon0 >> 12) & 0x7;
            if (planeCount == 0)
            {
                return;
            }

            planeCount = Math.Min(planeCount, _bitplanePointers.Length);
            var planeWords = new ushort[6];
            var window = GetDisplayWindow();
            var fetchWords = GetDataFetchWordCount();
            if (window.Width <= 0 || window.Height <= 0 || fetchWords <= 0)
            {
                return;
            }

            var fetchPixels = fetchWords * 16;
            var drawPixels = Math.Min(window.Width, fetchPixels);
            var rowStart = Math.Max(0, window.Y);
            var rowStop = Math.Min(Height, window.Y + window.Height);
            for (var y = rowStart; y < rowStop; y++)
            {
                _lastBitplaneRows++;
                var sourceY = y - window.Y;
                var wordStart = Math.Max(0, (-window.X) / 16);
                var wordStop = Math.Min(fetchWords, (Math.Min(Width, window.X + drawPixels) - window.X + 15) / 16);
                for (var word = wordStart; word < wordStop; word++)
                {
                    _lastBitplaneWords++;
                    for (var plane = 0; plane < planeCount; plane++)
                    {
                        var mod = (plane & 1) == 0 ? _bpl1mod : _bpl2mod;
                        var rowStride = (fetchWords * 2) + mod;
                        var address = unchecked(_bitplanePointers[plane] + (uint)((sourceY * rowStride) + (word * 2)));
                        planeWords[plane] = _bus.ReadChipWordForDevice(AmigaBusRequester.Bitplane, AmigaBusAccessKind.Bitplane, address, 0);
                    }

                    for (var bit = 15; bit >= 0; bit--)
                    {
                        var relativeX = (word * 16) + (15 - bit);
                        if (relativeX >= drawPixels)
                        {
                            continue;
                        }

                        var x = window.X + relativeX;
                        if (x < 0 || x >= Width)
                        {
                            continue;
                        }

                        var colorIndex = 0;
                        for (var plane = 0; plane < planeCount; plane++)
                        {
                            colorIndex |= ((planeWords[plane] >> bit) & 1) << plane;
                        }

                        if (colorIndex != 0)
                        {
                            _lastBitplaneNonZeroPixels++;
                        }

                        bgra[(y * Width) + x] = ConvertColor(_colors[colorIndex]);
                    }
                }
            }
        }

        private DisplayWindow GetDisplayWindow()
        {
            var hStart = _diwStart & 0x00FF;
            var hStop = _diwStop & 0x00FF;
            if (hStop <= hStart)
            {
                hStop += 0x100;
            }

            var vStart = (_diwStart >> 8) & 0x00FF;
            var vStop = (_diwStop >> 8) & 0x00FF;
            if (vStop <= vStart)
            {
                vStop += 0x100;
            }

            return new DisplayWindow(
                (hStart - StandardHStart) * 4,
                vStart - StandardVStart,
                (hStop - hStart) * 4,
                vStop - vStart);
        }

        private int GetDataFetchWordCount()
        {
            var ddfStart = _ddfStart & 0x00FC;
            var ddfStop = _ddfStop & 0x00FC;
            if (ddfStop < ddfStart)
            {
                return 0;
            }

            return Math.Clamp(((ddfStop - ddfStart) / 8) + 1, 0, 64);
        }

        private void RenderSprites(Span<uint> bgra)
        {
            for (var spriteIndex = 0; spriteIndex < _sprites.Length; spriteIndex++)
            {
                var sprite = _sprites[spriteIndex];
                var y = (sprite.Pos >> 8) & 0xFF;
                var x = sprite.Pos & 0xFF;
                if (y < 0 || y >= Height || x >= Width)
                {
                    continue;
                }

                for (var bit = 15; bit >= 0; bit--)
                {
                    var pixel = (((sprite.DataB >> bit) & 1) << 1) | ((sprite.DataA >> bit) & 1);
                    if (pixel == 0)
                    {
                        continue;
                    }

                    var px = x + (15 - bit);
                    if (px >= Width)
                    {
                        continue;
                    }

                    var color = Math.Min(31, 16 + (spriteIndex * 2) + pixel);
                    bgra[(y * Width) + px] = ConvertColor(_colors[color]);
                }
            }
        }

        private static uint ConvertColor(ushort amigaColor)
        {
            var r = (uint)(((amigaColor >> 8) & 0x0F) * 17);
            var g = (uint)(((amigaColor >> 4) & 0x0F) * 17);
            var b = (uint)((amigaColor & 0x0F) * 17);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private readonly struct DisplayWindow
        {
            public DisplayWindow(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public int X { get; }

            public int Y { get; }

            public int Width { get; }

            public int Height { get; }
        }

        private readonly struct PendingCustomWrite
        {
            public PendingCustomWrite(long cycle, ushort offset, ushort value)
            {
                Cycle = cycle;
                Offset = offset;
                Value = value;
            }

            public long Cycle { get; }

            public ushort Offset { get; }

            public ushort Value { get; }
        }

        private sealed class SpriteState
        {
            public uint Pointer { get; set; }

            public ushort Pos { get; set; }

            public ushort Ctl { get; set; }

            public ushort DataA { get; set; }

            public ushort DataB { get; set; }

            public void Reset()
            {
                Pointer = 0;
                Pos = 0;
                Ctl = 0;
                DataA = 0;
                DataB = 0;
            }
        }
    }

    internal readonly struct OcsDisplaySnapshot
    {
        public OcsDisplaySnapshot(ushort bplcon0, ushort diwStart, ushort diwStop, ushort ddfStart, ushort ddfStop, short bpl1mod, short bpl2mod, int lastBitplaneNonZeroPixels, int lastBitplaneRows, int lastBitplaneWords, uint[] bitplanePointers, ushort[] colors)
        {
            Bplcon0 = bplcon0;
            DiwStart = diwStart;
            DiwStop = diwStop;
            DdfStart = ddfStart;
            DdfStop = ddfStop;
            Bpl1Mod = bpl1mod;
            Bpl2Mod = bpl2mod;
            LastBitplaneNonZeroPixels = lastBitplaneNonZeroPixels;
            LastBitplaneRows = lastBitplaneRows;
            LastBitplaneWords = lastBitplaneWords;
            BitplanePointers = bitplanePointers;
            Colors = colors;
        }

        public ushort Bplcon0 { get; }

        public ushort DiwStart { get; }

        public ushort DiwStop { get; }

        public ushort DdfStart { get; }

        public ushort DdfStop { get; }

        public short Bpl1Mod { get; }

        public short Bpl2Mod { get; }

        public int LastBitplaneNonZeroPixels { get; }

        public int LastBitplaneRows { get; }

        public int LastBitplaneWords { get; }

        public uint[] BitplanePointers { get; }

        public ushort[] Colors { get; }
    }

    internal sealed class AmigaCopper
    {
        public void ExecuteList(AmigaBus bus, uint listAddress, int maxInstructions = 1024)
        {
            ArgumentNullException.ThrowIfNull(bus);
            var pc = listAddress;
            for (var i = 0; i < maxInstructions; i++)
            {
                var first = bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc, 0);
                var second = bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc + 2, 0);
                pc += 4;
                if (first == 0xFFFF && second == 0xFFFE)
                {
                    return;
                }

                if ((first & 1) == 0)
                {
                    var register = (ushort)(first & 0x01FE);
                    bus.WriteDeviceWord(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, 0x00DFF000u + register, second, 0);
                    continue;
                }

                if ((second & 1) != 0)
                {
                    continue;
                }
            }
        }
    }

    internal sealed class AmigaBlitter
    {
        private readonly AmigaBus _bus;
        private uint _sourceA;
        private uint _destinationD;
        private short _sourceAModulo;
        private short _destinationDModulo;
        private ushort _bltcon0;

        public AmigaBlitter(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public void Reset()
        {
            _sourceA = 0;
            _destinationD = 0;
            _sourceAModulo = 0;
            _destinationDModulo = 0;
            _bltcon0 = 0;
        }

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            switch (offset)
            {
                case 0x040:
                    _bltcon0 = value;
                    break;
                case 0x050:
                    _sourceA = (_sourceA & 0x0000_FFFF) | ((uint)value << 16);
                    break;
                case 0x052:
                    _sourceA = (_sourceA & 0xFFFF_0000) | value;
                    break;
                case 0x054:
                    _destinationD = (_destinationD & 0x0000_FFFF) | ((uint)value << 16);
                    break;
                case 0x056:
                    _destinationD = (_destinationD & 0xFFFF_0000) | value;
                    break;
                case 0x058:
                    Run(value, cycle);
                    break;
                case 0x064:
                    _sourceAModulo = unchecked((short)value);
                    break;
                case 0x066:
                    _destinationDModulo = unchecked((short)value);
                    break;
            }
        }

        private void Run(ushort bltsize, long cycle)
        {
            var widthWords = bltsize & 0x3F;
            if (widthWords == 0)
            {
                widthWords = 64;
            }

            var height = (bltsize >> 6) & 0x03FF;
            if (height == 0)
            {
                height = 1024;
            }

            var minterm = (byte)(_bltcon0 & 0xFF);
            for (var y = 0; y < height; y++)
            {
                var source = _sourceA + (uint)(y * ((widthWords * 2) + _sourceAModulo));
                var destination = _destinationD + (uint)(y * ((widthWords * 2) + _destinationDModulo));
                for (var x = 0; x < widthWords; x++)
                {
                    var value = _bus.ReadChipWordForDevice(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, source + (uint)(x * 2), cycle);
                    _bus.WriteChipWordForDevice(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, destination + (uint)(x * 2), ApplyMinterm(minterm, value), cycle);
                }
            }
        }

        private static ushort ApplyMinterm(byte minterm, ushort sourceA)
        {
            return minterm switch
            {
                0x00 => 0x0000,
                0xFF => 0xFFFF,
                _ => sourceA
            };
        }
    }
}
