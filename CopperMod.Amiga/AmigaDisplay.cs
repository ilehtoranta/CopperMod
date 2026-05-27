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
        private static readonly long PalFrameCycles = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz));
        private static readonly double PalLineCycles = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines;
        private readonly AmigaBus _bus;
        private readonly List<PendingCustomWrite> _pendingWrites = new List<PendingCustomWrite>();
        private readonly ushort[] _colors = new ushort[32];
        private readonly uint[] _bitplanePointers = new uint[6];
        private readonly int[] _bitplaneBaseRows = new int[6];
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
        private int _lastBitplaneMinX;
        private int _lastBitplaneMinY;
        private int _lastBitplaneMaxX;
        private int _lastBitplaneMaxY;
        private bool _renderingCopperFrame;
        private bool _currentLongFrame;
        private int _currentCopperRow;

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

        public bool InterlaceEnabled => (_bplcon0 & 0x0004) != 0;

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
                _lastBitplaneMinX,
                _lastBitplaneMinY,
                _lastBitplaneMaxX,
                _lastBitplaneMaxY,
                bitplanePointers,
                colors);
        }

        public void Reset()
        {
            _pendingWrites.Clear();
            _pendingIndex = 0;
            Array.Clear(_bitplanePointers);
            Array.Clear(_bitplaneBaseRows);
            _copperListPointer = 0;
            _diwStart = DefaultDiwStart;
            _diwStop = DefaultDiwStop;
            _ddfStart = DefaultDdfStart;
            _ddfStop = DefaultDdfStop;
            _bplcon0 = 0;
            _bplcon1 = 0;
            _bpl1mod = 0;
            _bpl2mod = 0;
            ResetFrameCounters();
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
            RenderFrame(bgra, 0, long.MaxValue, useTimedWrites: false);
        }

        public void RenderFrame(Span<uint> bgra, long frameStartCycle, long frameEndCycle)
        {
            RenderFrame(bgra, frameStartCycle, frameEndCycle, useTimedWrites: true);
        }

        private void RenderFrame(Span<uint> bgra, long frameStartCycle, long frameEndCycle, bool useTimedWrites)
        {
            if (bgra.Length < Width * Height)
            {
                throw new ArgumentException("The framebuffer is smaller than the PAL low-res display.", nameof(bgra));
            }

            ApplyPendingWrites(useTimedWrites ? frameStartCycle : long.MaxValue);
            _currentLongFrame = useTimedWrites && ((frameEndCycle / PalFrameCycles) & 1) != 0;
            ResetFrameCounters();
            bgra = bgra.Slice(0, Width * Height);
            if (_copperListPointer != 0)
            {
                RenderCopperFrame(bgra, frameStartCycle, useTimedWrites);
            }
            else
            {
                RenderRows(bgra, 0, Height, frameStartCycle, useTimedWrites);
            }

            if (useTimedWrites)
            {
                ApplyPendingWrites(frameEndCycle);
            }

            RenderSprites(bgra);
        }

        private void RenderCopperFrame(Span<uint> bgra, long frameStartCycle, bool useTimedWrites)
        {
            _renderingCopperFrame = true;
            _currentCopperRow = 0;
            var currentRow = 0;
            try
            {
                var pc = _copperListPointer;
                for (var instruction = 0; instruction < 4096; instruction++)
                {
                    var first = _bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc, 0);
                    var second = _bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc + 2, 0);
                    pc += 4;
                    if (first == 0xFFFF && second == 0xFFFE)
                    {
                        break;
                    }

                    if ((first & 1) == 0)
                    {
                        _currentCopperRow = currentRow;
                        ApplyWrite((ushort)(first & 0x01FE), second);
                        continue;
                    }

                    if ((second & 1) == 0)
                    {
                        var waitRow = GetCopperWaitOutputRow(first, second);
                        if (waitRow < currentRow)
                        {
                            break;
                        }

                        if (waitRow > currentRow)
                        {
                            var rowStop = Math.Min(waitRow, Height);
                            RenderRows(bgra, currentRow, rowStop, frameStartCycle, useTimedWrites);
                            currentRow = rowStop;
                        }

                        continue;
                    }
                }

                if (currentRow < Height)
                {
                    RenderRows(bgra, currentRow, Height, frameStartCycle, useTimedWrites);
                }
            }
            finally
            {
                _renderingCopperFrame = false;
                _currentCopperRow = 0;
            }
        }

        private static int GetCopperWaitOutputRow(ushort first, ushort second)
        {
            var vertical = (first >> 8) & 0xFF;
            var mask = (second >> 8) & 0xFF;
            if (mask != 0)
            {
                vertical &= mask;
            }

            return Math.Clamp(vertical - StandardVStart, 0, AmigaConstants.PalLowResHeight);
        }

        private void RenderRows(Span<uint> bgra, int rowStart, int rowStop, long frameStartCycle, bool useTimedWrites)
        {
            if (!useTimedWrites)
            {
                FillRows(bgra, rowStart, rowStop);
                RenderBitplanes(bgra, rowStart, rowStop);
                return;
            }

            rowStart = Math.Clamp(rowStart, 0, Height);
            rowStop = Math.Clamp(rowStop, rowStart, Height);
            for (var row = rowStart; row < rowStop; row++)
            {
                ApplyPendingWrites(GetOutputRowStartCycle(frameStartCycle, row));
                FillRows(bgra, row, row + 1);
                RenderBitplanes(bgra, row, row + 1);
            }
        }

        private static long GetOutputRowStartCycle(long frameStartCycle, int row)
        {
            return frameStartCycle + (long)Math.Round((StandardVStart + row) * PalLineCycles);
        }

        private void FillRows(Span<uint> bgra, int rowStart, int rowStop)
        {
            rowStart = Math.Clamp(rowStart, 0, Height);
            rowStop = Math.Clamp(rowStop, rowStart, Height);
            var color = ConvertColor(_colors[0]);
            for (var y = rowStart; y < rowStop; y++)
            {
                bgra.Slice(y * Width, Width).Fill(color);
            }
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
                var oldPlaneCount = Math.Min((_bplcon0 >> 12) & 0x7, _bitplaneBaseRows.Length);
                var newPlaneCount = Math.Min((value >> 12) & 0x7, _bitplaneBaseRows.Length);
                _bplcon0 = value;
                if (newPlaneCount > oldPlaneCount)
                {
                    SetBitplaneBaseRows(oldPlaneCount, newPlaneCount, GetCurrentBitplaneBaseRow());
                }

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

                    if (!_renderingCopperFrame || ((_bplcon0 >> 12) & 0x7) != 0)
                    {
                        _bitplaneBaseRows[plane] = GetCurrentBitplaneBaseRow();
                    }
                }

                return;
            }

            if (offset >= 0x120 && offset < 0x180)
            {
                ApplySpriteWrite(offset, value);
            }
        }

        private int GetCurrentBitplaneBaseRow()
        {
            return _renderingCopperFrame
                ? _currentCopperRow
                : GetDisplayWindow().Y;
        }

        private void SetBitplaneBaseRows(int startPlane, int endPlane, int row)
        {
            startPlane = Math.Clamp(startPlane, 0, _bitplaneBaseRows.Length);
            endPlane = Math.Clamp(endPlane, startPlane, _bitplaneBaseRows.Length);
            for (var i = startPlane; i < endPlane; i++)
            {
                _bitplaneBaseRows[i] = row;
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

        private void ResetFrameCounters()
        {
            _lastBitplaneNonZeroPixels = 0;
            _lastBitplaneRows = 0;
            _lastBitplaneWords = 0;
            _lastBitplaneMinX = Width;
            _lastBitplaneMinY = Height;
            _lastBitplaneMaxX = -1;
            _lastBitplaneMaxY = -1;
        }

        private void RenderBitplanes(Span<uint> bgra, int bandStart, int bandStop)
        {
            var planeCount = (_bplcon0 >> 12) & 0x7;
            if (planeCount == 0)
            {
                return;
            }

            planeCount = Math.Min(planeCount, _bitplanePointers.Length);
            var planeWords = new ushort[6, 64];
            var planeHasRow = new bool[6];
            var window = GetDisplayWindow();
            var fetchWords = GetDataFetchWordCount();
            if (window.Width <= 0 || window.Height <= 0 || fetchWords <= 0)
            {
                return;
            }

            var fetchPixels = fetchWords * 16;
            var drawPixels = fetchPixels;
            var originX = Math.Max(window.X, GetDataFetchStartX());
            var clipLeft = Math.Max(0, window.X);
            var clipRight = Math.Min(Width, window.X + window.Width);
            var rowStart = Math.Max(Math.Max(0, bandStart), window.Y);
            var rowStop = Math.Min(Math.Min(Height, bandStop), window.Y + window.Height);
            var holdAndModify = (_bplcon0 & 0x0800) != 0 && planeCount >= 6;
            var laceSourceRowOffset = (_bplcon0 & 0x0004) != 0 && _currentLongFrame ? 1 : 0;
            for (var y = rowStart; y < rowStop; y++)
            {
                _lastBitplaneRows++;
                for (var plane = 0; plane < planeCount; plane++)
                {
                    var mod = (plane & 1) == 0 ? _bpl1mod : _bpl2mod;
                    var rowStride = (fetchWords * 2) + mod;
                    var displaySourceY = y - _bitplaneBaseRows[plane];
                    var planeSourceY = displaySourceY - laceSourceRowOffset;
                    planeHasRow[plane] = displaySourceY >= 0;
                    for (var word = 0; word < fetchWords; word++)
                    {
                        if (!planeHasRow[plane])
                        {
                            planeWords[plane, word] = 0;
                            continue;
                        }

                        var address = unchecked(_bitplanePointers[plane] + (uint)((planeSourceY * rowStride) + (word * 2)));
                        planeWords[plane, word] = _bus.ReadChipWordForDevice(AmigaBusRequester.Bitplane, AmigaBusAccessKind.Bitplane, address, 0);
                        _lastBitplaneWords++;
                    }
                }

                var xStart = Math.Max(clipLeft, Math.Max(0, originX));
                var xStop = Math.Min(clipRight, Math.Min(Width, originX + drawPixels + 16));
                var hamColor = _colors[0];
                for (var x = xStart; x < xStop; x++)
                {
                    var colorIndex = 0;
                    for (var plane = 0; plane < planeCount; plane++)
                    {
                        if (!planeHasRow[plane])
                        {
                            continue;
                        }

                        var relativeX = x - originX - GetPlaneHorizontalScroll(plane);
                        if (relativeX < 0 || relativeX >= drawPixels)
                        {
                            continue;
                        }

                        var word = relativeX >> 4;
                        if (word < 0 || word >= fetchWords)
                        {
                            continue;
                        }

                        var bit = 15 - (relativeX & 0x0F);
                        colorIndex |= ((planeWords[plane, word] >> bit) & 1) << plane;
                    }

                    if (colorIndex != 0)
                    {
                        _lastBitplaneNonZeroPixels++;
                        _lastBitplaneMinX = Math.Min(_lastBitplaneMinX, x);
                        _lastBitplaneMinY = Math.Min(_lastBitplaneMinY, y);
                        _lastBitplaneMaxX = Math.Max(_lastBitplaneMaxX, x);
                        _lastBitplaneMaxY = Math.Max(_lastBitplaneMaxY, y);
                    }

                    bgra[(y * Width) + x] = holdAndModify
                        ? ConvertHamPixel(colorIndex, ref hamColor)
                        : ConvertColorIndex(colorIndex);
                }
            }
        }

        private int GetDataFetchStartX()
        {
            var ddfStart = _ddfStart & 0x00FC;
            return ((ddfStart - DefaultDdfStart) / 8) * 16;
        }

        private int GetPlaneHorizontalScroll(int plane)
        {
            var playfield1Delay = _bplcon1 & 0x0F;
            return (plane & 1) == 0
                ? playfield1Delay
                : (_bplcon1 >> 4) & 0x0F;
        }

        private DisplayWindow GetDisplayWindow()
        {
            var hStart = _diwStart & 0x00FF;
            var hStop = (_diwStop & 0x00FF) + 0x100;

            var vStart = (_diwStart >> 8) & 0x00FF;
            var vStop = (_diwStop >> 8) & 0x00FF;
            if (vStop < 0x80)
            {
                vStop += 0x100;
            }

            if (vStop <= vStart)
            {
                vStop += 0x100;
            }

            return new DisplayWindow(
                hStart - StandardHStart,
                vStart - StandardVStart,
                hStop - hStart,
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

        private uint ConvertColorIndex(int colorIndex)
        {
            if (colorIndex < _colors.Length)
            {
                return ConvertColor(_colors[colorIndex]);
            }

            var baseColor = _colors[colorIndex & 0x1F];
            var r = (uint)((((baseColor >> 8) & 0x0F) * 17) / 2);
            var g = (uint)((((baseColor >> 4) & 0x0F) * 17) / 2);
            var b = (uint)(((baseColor & 0x0F) * 17) / 2);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private uint ConvertHamPixel(int colorIndex, ref ushort previousColor)
        {
            var data = (ushort)(colorIndex & 0x0F);
            switch ((colorIndex >> 4) & 0x03)
            {
                case 0:
                    previousColor = _colors[data];
                    break;
                case 1:
                    previousColor = (ushort)((previousColor & 0x0FF0) | data);
                    break;
                case 2:
                    previousColor = (ushort)((previousColor & 0x00FF) | (data << 8));
                    break;
                case 3:
                    previousColor = (ushort)((previousColor & 0x0F0F) | (data << 4));
                    break;
            }

            return ConvertColor(previousColor);
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
        public OcsDisplaySnapshot(ushort bplcon0, ushort diwStart, ushort diwStop, ushort ddfStart, ushort ddfStop, short bpl1mod, short bpl2mod, int lastBitplaneNonZeroPixels, int lastBitplaneRows, int lastBitplaneWords, int lastBitplaneMinX, int lastBitplaneMinY, int lastBitplaneMaxX, int lastBitplaneMaxY, uint[] bitplanePointers, ushort[] colors)
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
            LastBitplaneMinX = lastBitplaneMinX;
            LastBitplaneMinY = lastBitplaneMinY;
            LastBitplaneMaxX = lastBitplaneMaxX;
            LastBitplaneMaxY = lastBitplaneMaxY;
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

        public int LastBitplaneMinX { get; }

        public int LastBitplaneMinY { get; }

        public int LastBitplaneMaxX { get; }

        public int LastBitplaneMaxY { get; }

        public uint[] BitplanePointers { get; }

        public ushort[] Colors { get; }
    }

    internal sealed class AmigaCopper
    {
        public void ExecuteList(AmigaBus bus, uint listAddress, int maxInstructions = 1024, Action<ushort, ushort>? onMove = null)
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
                    onMove?.Invoke(register, second);
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
        private uint _sourceB;
        private uint _sourceC;
        private uint _destinationD;
        private short _sourceAModulo;
        private short _sourceBModulo;
        private short _sourceCModulo;
        private short _destinationDModulo;
        private ushort _bltcon0;
        private ushort _bltcon1;
        private ushort _firstWordMask = 0xFFFF;
        private ushort _lastWordMask = 0xFFFF;
        private ushort _dataA;
        private ushort _dataB;
        private ushort _dataC;

        public AmigaBlitter(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public void Reset()
        {
            _sourceA = 0;
            _sourceB = 0;
            _sourceC = 0;
            _destinationD = 0;
            _sourceAModulo = 0;
            _sourceBModulo = 0;
            _sourceCModulo = 0;
            _destinationDModulo = 0;
            _bltcon0 = 0;
            _bltcon1 = 0;
            _firstWordMask = 0xFFFF;
            _lastWordMask = 0xFFFF;
            _dataA = 0;
            _dataB = 0;
            _dataC = 0;
        }

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            switch (offset)
            {
                case 0x040:
                    _bltcon0 = value;
                    break;
                case 0x042:
                    _bltcon1 = value;
                    break;
                case 0x044:
                    _firstWordMask = value;
                    break;
                case 0x046:
                    _lastWordMask = value;
                    break;
                case 0x048:
                    _sourceC = (_sourceC & 0x0000_FFFF) | ((uint)value << 16);
                    break;
                case 0x04A:
                    _sourceC = (_sourceC & 0xFFFF_0000) | value;
                    break;
                case 0x04C:
                    _sourceB = (_sourceB & 0x0000_FFFF) | ((uint)value << 16);
                    break;
                case 0x04E:
                    _sourceB = (_sourceB & 0xFFFF_0000) | value;
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
                case 0x060:
                    _sourceCModulo = unchecked((short)value);
                    break;
                case 0x062:
                    _sourceBModulo = unchecked((short)value);
                    break;
                case 0x064:
                    _sourceAModulo = unchecked((short)value);
                    break;
                case 0x066:
                    _destinationDModulo = unchecked((short)value);
                    break;
                case 0x070:
                    _dataC = value;
                    break;
                case 0x072:
                    _dataB = value;
                    break;
                case 0x074:
                    _dataA = value;
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

            var useA = (_bltcon0 & 0x0800) != 0;
            var useB = (_bltcon0 & 0x0400) != 0;
            var useC = (_bltcon0 & 0x0200) != 0;
            var useD = (_bltcon0 & 0x0100) != 0;
            var minterm = (byte)(_bltcon0 & 0x00FF);
            var shiftA = (_bltcon0 >> 12) & 0x0F;
            var shiftB = (_bltcon1 >> 12) & 0x0F;
            var descending = (_bltcon1 & 0x0002) != 0;
            var step = descending ? -2 : 2;
            var sourceA = GetEffectiveBlitterAddress(_sourceA);
            var sourceB = GetEffectiveBlitterAddress(_sourceB);
            var sourceC = GetEffectiveBlitterAddress(_sourceC);
            var destinationD = GetEffectiveBlitterAddress(_destinationD);
            var previousA = useA ? (ushort)0 : _dataA;
            var previousB = useB ? (ushort)0 : _dataB;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < widthWords; x++)
                {
                    var mask = 0xFFFF;
                    if (x == 0)
                    {
                        mask &= _firstWordMask;
                    }

                    if (x == widthWords - 1)
                    {
                        mask &= _lastWordMask;
                    }

                    var rawA = useA ? ReadAndStep(ref sourceA, step, cycle) : _dataA;
                    var rawB = useB ? ReadAndStep(ref sourceB, step, cycle) : _dataB;
                    var rawC = useC ? ReadAndStep(ref sourceC, step, cycle) : _dataC;
                    rawA = (ushort)(rawA & mask);

                    var a = ShiftSource(rawA, ref previousA, shiftA, descending);
                    var b = ShiftSource(rawB, ref previousB, shiftB, descending);
                    var output = ApplyMinterm(minterm, a, b, rawC);
                    if (useD)
                    {
                        WriteAndStep(ref destinationD, step, output, cycle);
                    }
                }

                if (useA)
                {
                    sourceA = AddModulo(sourceA, _sourceAModulo, descending);
                }

                if (useB)
                {
                    sourceB = AddModulo(sourceB, _sourceBModulo, descending);
                }

                if (useC)
                {
                    sourceC = AddModulo(sourceC, _sourceCModulo, descending);
                }

                if (useD)
                {
                    destinationD = AddModulo(destinationD, _destinationDModulo, descending);
                }
            }

            if (useA)
            {
                _sourceA = sourceA;
                _dataA = previousA;
            }

            if (useB)
            {
                _sourceB = sourceB;
                _dataB = previousB;
            }

            if (useC)
            {
                _sourceC = sourceC;
            }

            if (useD)
            {
                _destinationD = destinationD;
            }
        }

        private ushort ReadAndStep(ref uint pointer, int step, long cycle)
        {
            var value = _bus.ReadChipWordForDevice(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, GetEffectiveBlitterAddress(pointer), cycle);
            pointer = unchecked((uint)((int)pointer + step));
            return value;
        }

        private void WriteAndStep(ref uint pointer, int step, ushort value, long cycle)
        {
            _bus.WriteChipWordForDevice(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, GetEffectiveBlitterAddress(pointer), value, cycle);
            pointer = unchecked((uint)((int)pointer + step));
        }

        private static uint AddModulo(uint pointer, short modulo, bool descending)
        {
            var evenModulo = modulo & ~1;
            return unchecked((uint)((int)pointer + (descending ? -evenModulo : evenModulo)));
        }

        private static uint GetEffectiveBlitterAddress(uint pointer)
        {
            return pointer & 0xFFFF_FFFEu;
        }

        private static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
        {
            if (shift == 0)
            {
                previous = current;
                return current;
            }

            uint combined = descending
                ? ((uint)current << 16) | previous
                : ((uint)previous << 16) | current;
            var value = descending
                ? (ushort)(combined >> (16 - shift))
                : (ushort)(combined >> shift);
            previous = current;
            return value;
        }

        private static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
        {
            ushort value = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                var mask = 1 << bit;
                var selector = 0;
                if ((sourceA & mask) != 0)
                {
                    selector |= 4;
                }

                if ((sourceB & mask) != 0)
                {
                    selector |= 2;
                }

                if ((sourceC & mask) != 0)
                {
                    selector |= 1;
                }

                if (((minterm >> selector) & 1) != 0)
                {
                    value |= (ushort)mask;
                }
            }

            return value;
        }
    }
}
