/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace CopperMod.Amiga
{
    internal sealed partial class CyberGraphicsLibrary
    {
        public const string Name = "cybergraphics.library";
        public const ushort Version = 52;
        public const ushort Revision = 1;

        public const int OpenOffset = -6;
        public const int CloseOffset = -12;
        public const int ExpungeOffset = -18;
        public const int ReservedOffset = -24;

        public const uint InvalidDisplayId = 0xFFFF_FFFFu;

        internal const uint TagDone = 0;
        internal const uint TagIgnore = 1;
        internal const uint TagMore = 2;
        internal const uint TagSkip = 3;

        internal const uint CyberMapAttrXMod = 0x8000_0001;
        internal const uint CyberMapAttrBytesPerPixel = 0x8000_0002;
        internal const uint CyberMapAttrDisplayAddress = 0x8000_0003;
        internal const uint CyberMapAttrPixelFormat = 0x8000_0004;
        internal const uint CyberMapAttrWidth = 0x8000_0005;
        internal const uint CyberMapAttrHeight = 0x8000_0006;
        internal const uint CyberMapAttrDepth = 0x8000_0007;
        internal const uint CyberMapAttrIsCyberGfx = 0x8000_0008;
        internal const uint CyberMapAttrIsLinearMemory = 0x8000_0009;
        internal const uint CyberMapAttrColorMap = 0x8000_000A;

        internal const uint CyberIdAttrPixelFormat = 0x8000_0001;
        internal const uint CyberIdAttrWidth = 0x8000_0002;
        internal const uint CyberIdAttrHeight = 0x8000_0003;
        internal const uint CyberIdAttrDepth = 0x8000_0004;
        internal const uint CyberIdAttrBytesPerPixel = 0x8000_0005;

        internal const uint BestModeDepth = 0x8005_0000;
        internal const uint BestModeNominalWidth = 0x8005_0001;
        internal const uint BestModeNominalHeight = 0x8005_0002;
        internal const uint BestModeMonitorId = 0x8005_0003;
        internal const uint BestModeBoardName = 0x8005_0005;

        internal const uint ModeRequestMinDepth = 0x8004_0000;
        internal const uint ModeRequestMaxDepth = 0x8004_0001;
        internal const uint ModeRequestMinWidth = 0x8004_0002;
        internal const uint ModeRequestMaxWidth = 0x8004_0003;
        internal const uint ModeRequestMinHeight = 0x8004_0004;
        internal const uint ModeRequestMaxHeight = 0x8004_0005;
        internal const uint ModeRequestColorModelArray = 0x8004_0006;

        internal const uint SetVideoDpmsLevel = 0x8800_2001;

        internal const uint LockWidth = 0x8400_1001;
        internal const uint LockHeight = 0x8400_1002;
        internal const uint LockDepth = 0x8400_1003;
        internal const uint LockPixelFormat = 0x8400_1004;
        internal const uint LockBytesPerPixel = 0x8400_1005;
        internal const uint LockBytesPerRow = 0x8400_1006;
        internal const uint LockBaseAddress = 0x8400_1007;
        internal const uint UnlockReally = 0x8500_1002;

        internal const uint BltMixLevel = 0x8880_2000;
        internal const uint BltUseSourceAlpha = 0x8880_2001;
        internal const uint BltDestinationAlpha = 0x8880_2002;

        private const int DisplayNameLength = 32;
        private const int ExecListSize = 14;
        private const int CyberModeNodeSize = 60;
        private const int CDrawMessageSize = 26;

        private static readonly CyberGraphicsVector[] ApiVectorArray =
        [
            new(-54, CyberGraphicsFunction.IsCyberModeId, 40),
            new(-60, CyberGraphicsFunction.BestCModeIdTagList, 40),
            new(-66, CyberGraphicsFunction.CModeRequestTagList, 40),
            new(-72, CyberGraphicsFunction.AllocCModeListTagList, 40),
            new(-78, CyberGraphicsFunction.FreeCModeList, 40),
            new(-90, CyberGraphicsFunction.ScalePixelArray, 40),
            new(-96, CyberGraphicsFunction.GetCyberMapAttr, 40),
            new(-102, CyberGraphicsFunction.GetCyberIdAttr, 40),
            new(-108, CyberGraphicsFunction.ReadRgbPixel, 40),
            new(-114, CyberGraphicsFunction.WriteRgbPixel, 40),
            new(-120, CyberGraphicsFunction.ReadPixelArray, 40),
            new(-126, CyberGraphicsFunction.WritePixelArray, 40),
            new(-132, CyberGraphicsFunction.MovePixelArray, 40),
            new(-144, CyberGraphicsFunction.InvertPixelArray, 40),
            new(-150, CyberGraphicsFunction.FillPixelArray, 40),
            new(-156, CyberGraphicsFunction.DoCDrawMethodTagList, 40),
            new(-162, CyberGraphicsFunction.CVideoCtrlTagList, 40),
            new(-168, CyberGraphicsFunction.LockBitMapTagList, 40),
            new(-174, CyberGraphicsFunction.UnLockBitMap, 40),
            new(-180, CyberGraphicsFunction.UnLockBitMapTagList, 40),
            new(-186, CyberGraphicsFunction.ExtractColor, 41),
            new(-198, CyberGraphicsFunction.WriteLutPixelArray, 41),
            new(-216, CyberGraphicsFunction.WritePixelArrayAlpha, 43),
            new(-222, CyberGraphicsFunction.BltTemplateAlpha, 43),
            new(-228, CyberGraphicsFunction.ProcessPixelArray, 43),
            new(-234, CyberGraphicsFunction.BltBitMapAlpha, 50),
            new(-240, CyberGraphicsFunction.BltBitMapRastPortAlpha, 50),
            new(-252, CyberGraphicsFunction.ScalePixelArrayAlpha, 51),
            new(-258, CyberGraphicsFunction.ScaleMapRastPortAlpha, 52)
        ];

        private static readonly CyberGraphicsVector[] AllVectorArray =
        [
            new(OpenOffset, CyberGraphicsFunction.Open, 40),
            new(CloseOffset, CyberGraphicsFunction.Close, 40),
            new(ExpungeOffset, CyberGraphicsFunction.Expunge, 40),
            new(ReservedOffset, CyberGraphicsFunction.Reserved, 40),
            .. ApiVectorArray
        ];

        private readonly AmigaBus _bus;
        private ICyberGraphicsGuestServices? _guestServices;
        private readonly CyberGraphicsRtgDevice? _rtgDevice;
        private readonly Dictionary<int, CyberGraphicsVector> _vectorsByOffset;
        private readonly Dictionary<uint, CyberGraphicsMode> _modes = new Dictionary<uint, CyberGraphicsMode>();
        private readonly Dictionary<uint, CyberGraphicsSurface> _bitmaps = new Dictionary<uint, CyberGraphicsSurface>();
        private readonly Dictionary<uint, CyberGraphicsSurface> _rastPorts = new Dictionary<uint, CyberGraphicsSurface>();
        private readonly Dictionary<uint, CyberGraphicsSurface> _viewPorts = new Dictionary<uint, CyberGraphicsSurface>();
        private readonly Dictionary<uint, CyberGraphicsMode> _viewPortModes = new Dictionary<uint, CyberGraphicsMode>();
        private readonly Dictionary<uint, CyberGraphicsSurface> _locks = new Dictionary<uint, CyberGraphicsSurface>();
        private readonly Dictionary<uint, int> _modeListAllocations = new Dictionary<uint, int>();
        private readonly Dictionary<uint, uint> _viewPortDpmsLevels = new Dictionary<uint, uint>();
        private readonly CyberGraphicsLibraryPatchManager _systemPatches;
        private uint _libraryBase;
        private uint _nextLockHandle = 1;
        private ushort _openCount;

        public CyberGraphicsLibrary(
            AmigaBus bus,
            ICyberGraphicsGuestServices? guestServices = null,
            CyberGraphicsRtgDevice? rtgDevice = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _guestServices = guestServices;
            _rtgDevice = rtgDevice ?? (bus.RtgVram.IsPresent ? new CyberGraphicsRtgDevice(bus) : null);
            _systemPatches = new CyberGraphicsLibraryPatchManager(bus, () => _guestServices);
            _vectorsByOffset = AllVectorArray.ToDictionary(vector => vector.Offset);
            if (_rtgDevice != null)
            {
                foreach (var mode in _rtgDevice.CreateModes())
                {
                    RegisterMode(mode);
                }
            }
        }

        public static IReadOnlyList<CyberGraphicsVector> ApiVectors => ApiVectorArray;

        public static IReadOnlyList<CyberGraphicsVector> AllVectors => AllVectorArray;

        public uint LibraryBase => _libraryBase;

        public ushort OpenCount => _openCount;

        internal CyberGraphicsRtgDevice? RtgDevice => _rtgDevice;

        internal CyberGraphicsLibraryPatchManager SystemPatches => _systemPatches;

        internal IReadOnlyList<CyberGraphicsMode> RegisteredModes
            => _modes.Values.OrderBy(mode => mode.DisplayId).ToArray();

        internal bool RtgScanoutSelected => _rtgDevice?.FrontViewPort != 0;

        public Action<CyberGraphicsFunction>? CallObserved { get; set; }

        internal void AttachGuestServices(ICyberGraphicsGuestServices guestServices)
            => _guestServices = guestServices ?? throw new ArgumentNullException(nameof(guestServices));

        public void RegisterMode(CyberGraphicsMode mode)
        {
            if (mode.DisplayId == 0 || mode.DisplayId == InvalidDisplayId)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), "A CyberGraphX mode must have a valid display id.");
            }

            _ = CyberGraphicsSurface.GetBytesPerPixel(mode.PixelFormat);
            _modes[mode.DisplayId] = mode;
        }

        public bool UnregisterMode(uint displayId)
            => _modes.Remove(displayId);

        internal bool TryGetMode(uint displayId, out CyberGraphicsMode mode)
            => _modes.TryGetValue(displayId, out mode);

        public void RegisterBitMap(uint bitMapAddress, CyberGraphicsSurface surface)
        {
            ValidateObjectAddress(bitMapAddress, nameof(bitMapAddress));
            _bitmaps[bitMapAddress] = surface ?? throw new ArgumentNullException(nameof(surface));
            _rtgDevice?.RegisterBitMap(bitMapAddress, surface);
        }

        public void RegisterRastPort(uint rastPortAddress, CyberGraphicsSurface surface)
        {
            ValidateObjectAddress(rastPortAddress, nameof(rastPortAddress));
            _rastPorts[rastPortAddress] = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public void RegisterViewPort(
            uint viewPortAddress,
            CyberGraphicsSurface surface,
            CyberGraphicsMode? mode = null)
        {
            ValidateObjectAddress(viewPortAddress, nameof(viewPortAddress));
            _viewPorts[viewPortAddress] = surface ?? throw new ArgumentNullException(nameof(surface));
            if (mode.HasValue)
            {
                _viewPortModes[viewPortAddress] = mode.Value;
            }
            else if (TryInferMode(surface, out var inferredMode))
            {
                _viewPortModes[viewPortAddress] = inferredMode;
            }

            var bitMap = _bitmaps.FirstOrDefault(pair => ReferenceEquals(pair.Value, surface)).Key;
            if (bitMap != 0)
            {
                _rtgDevice?.RegisterViewPort(viewPortAddress, bitMap);
            }
        }

        public bool UnregisterBitMap(uint address)
        {
            _rtgDevice?.UnregisterBitMap(address);
            return _bitmaps.Remove(address);
        }

        public bool UnregisterRastPort(uint address)
            => _rastPorts.Remove(address);

        public bool UnregisterViewPort(uint address)
        {
            _viewPortDpmsLevels.Remove(address);
            _viewPortModes.Remove(address);
            _rtgDevice?.UnregisterViewPort(address);
            return _viewPorts.Remove(address);
        }

        internal void UnregisterSurface(CyberGraphicsSurface surface)
        {
            foreach (var address in _bitmaps
                .Where(pair => ReferenceEquals(pair.Value, surface))
                .Select(pair => pair.Key)
                .ToArray())
            {
                _rtgDevice?.UnregisterBitMap(address);
                _bitmaps.Remove(address);
            }

            foreach (var address in _rastPorts
                .Where(pair => ReferenceEquals(pair.Value, surface))
                .Select(pair => pair.Key)
                .ToArray())
            {
                _rastPorts.Remove(address);
            }

            foreach (var address in _viewPorts
                .Where(pair => ReferenceEquals(pair.Value, surface))
                .Select(pair => pair.Key)
                .ToArray())
            {
                UnregisterViewPort(address);
            }
        }

        public uint GetViewPortDpmsLevel(uint address)
            => _viewPortDpmsLevels.TryGetValue(address, out var level) ? level : 0;

        internal CyberGraphicsSurface? AllocateRtgSurface(
            int width,
            int height,
            CyberGraphicsPixelFormat pixelFormat)
            => _rtgDevice?.AllocateSurface(width, height, pixelFormat);

        internal bool FreeRtgSurface(CyberGraphicsSurface surface)
            => _rtgDevice?.FreeSurface(surface) == true;

        internal bool TryGetBitMapSurface(uint bitMapAddress, out CyberGraphicsSurface surface)
            => _bitmaps.TryGetValue(bitMapAddress, out surface!);

        internal bool TryResolveBitMapSurface(uint bitMapAddress, out CyberGraphicsSurface surface)
        {
            if (_bitmaps.TryGetValue(bitMapAddress, out surface!))
            {
                return true;
            }

            if (bitMapAddress != 0 &&
                _bus.IsMappedMemoryRange(bitMapAddress + 8, 4) &&
                _rtgDevice?.TryGetSurfaceByBase(_bus.ReadLong(bitMapAddress + 8), out surface!) == true)
            {
                return true;
            }

            surface = null!;
            return false;
        }

        internal bool IsRtgBitMap(uint bitMapAddress)
            => _bitmaps.ContainsKey(bitMapAddress);

        internal bool IsRtgRastPort(uint rastPortAddress)
        {
            if (rastPortAddress != 0 &&
                _bus.IsMappedMemoryRange(rastPortAddress, 8) &&
                TryResolveBitMapSurface(_bus.ReadLong(rastPortAddress + 4), out _))
            {
                return true;
            }

            return _rastPorts.ContainsKey(rastPortAddress);
        }

        private bool TryGetRastPortSurface(uint rastPortAddress, out CyberGraphicsSurface surface)
        {
            if (rastPortAddress != 0 &&
                _bus.IsMappedMemoryRange(rastPortAddress, 8) &&
                TryResolveBitMapSurface(_bus.ReadLong(rastPortAddress + 4), out surface!))
            {
                return true;
            }

            return _rastPorts.TryGetValue(rastPortAddress, out surface!);
        }

        internal bool IsRtgViewPort(uint viewPortAddress)
            => _viewPorts.ContainsKey(viewPortAddress);

        internal bool TryGetViewPortSurface(uint viewPortAddress, out CyberGraphicsSurface surface)
            => _viewPorts.TryGetValue(viewPortAddress, out surface!);

        internal void SelectFrontViewPort(uint viewPortAddress)
            => _rtgDevice?.SelectFrontViewPort(viewPortAddress);

        internal bool ChangeViewPortBitMap(uint viewPortAddress, uint bitMapAddress)
        {
            if (!_bitmaps.TryGetValue(bitMapAddress, out var surface))
            {
                return false;
            }

            if (_viewPorts.TryGetValue(viewPortAddress, out var previousSurface) &&
                previousSurface.ColorMapAddress != 0)
            {
                surface.AssociateColorMap(previousSurface.ColorMapAddress, previousSurface.Palette);
            }

            _viewPorts[viewPortAddress] = surface;
            _rtgDevice?.RegisterViewPort(viewPortAddress, bitMapAddress);
            return true;
        }

        internal bool TryGetColorMapPalette(uint colorMapAddress, out uint[] palette)
        {
            if (colorMapAddress != 0)
            {
                foreach (var surface in _viewPorts.Values.Concat(_bitmaps.Values))
                {
                    if (surface.ColorMapAddress == colorMapAddress)
                    {
                        palette = surface.Palette;
                        return true;
                    }
                }
            }

            palette = null!;
            return false;
        }

        internal bool TryRenderRtgFrame(out CyberGraphicsRtgFrame frame)
        {
            if (_rtgDevice != null &&
                GetViewPortDpmsLevel(_rtgDevice.FrontViewPort) == 0 &&
                _rtgDevice.TryRenderFrontFrame(out frame))
            {
                return true;
            }

            frame = default;
            return false;
        }

        internal bool TryBuildDisplayComposition(
            uint viewAddress,
            int planarWidth,
            int planarHeight,
            out CyberGraphicsDisplayComposition composition)
        {
            const int viewPortSize = 0x28;
            const int nextOffset = 0x00;
            const int widthOffset = 0x18;
            const int heightOffset = 0x1A;
            const int xOffset = 0x1C;
            const int yOffset = 0x1E;
            const int modesOffset = 0x20;
            const int rasInfoOffset = 0x24;
            const int rasInfoRxOffset = 0x08;
            const int rasInfoRyOffset = 0x0A;
            const int maximumViewPorts = 128;
            const ushort viewPortHidden = 0x2000;

            composition = null!;
            if (viewAddress == 0 || planarWidth <= 0 || planarHeight <= 0 ||
                !_bus.IsMappedMemoryRange(viewAddress, 4))
            {
                return false;
            }

            var viewPort = _bus.ReadLong(viewAddress);
            var visited = new HashSet<uint>();
            var layers = new List<CyberGraphicsDisplayLayer>();
            var hasRtg = false;
            var topIsRtg = false;
            var topDpmsOff = false;
            var outputWidth = planarWidth;
            var outputHeight = planarHeight;
            for (var count = 0;
                viewPort != 0 && count < maximumViewPorts && visited.Add(viewPort);
                count++)
            {
                if ((viewPort & 1) != 0 || !_bus.IsMappedMemoryRange(viewPort, viewPortSize))
                {
                    break;
                }

                var nextViewPort = _bus.ReadLong(viewPort + nextOffset);
                if ((_bus.ReadWord(viewPort + modesOffset) & viewPortHidden) != 0)
                {
                    viewPort = nextViewPort;
                    continue;
                }

                var isRtg = _viewPorts.TryGetValue(viewPort, out var surface);
                _viewPortModes.TryGetValue(viewPort, out var mode);
                if (isRtg && mode.DisplayId == 0)
                {
                    _ = TryInferMode(surface!, out mode);
                }

                if (layers.Count == 0 && isRtg)
                {
                    topIsRtg = true;
                    topDpmsOff = GetViewPortDpmsLevel(viewPort) != 0;
                    outputWidth = mode.Width != 0 ? mode.Width : surface!.Width;
                    outputHeight = mode.Height != 0 ? mode.Height : surface!.Height;
                }

                var width = _bus.ReadWord(viewPort + widthOffset);
                var height = _bus.ReadWord(viewPort + heightOffset);
                if (width == 0)
                {
                    width = checked((ushort)Math.Min(ushort.MaxValue,
                        isRtg ? surface!.Width : outputWidth));
                }

                if (height == 0)
                {
                    height = checked((ushort)Math.Min(ushort.MaxValue,
                        isRtg ? surface!.Height : outputHeight));
                }

                var sourceX = 0;
                var sourceY = 0;
                var rasInfo = _bus.ReadLong(viewPort + rasInfoOffset);
                if (rasInfo != 0 && _bus.IsMappedMemoryRange(rasInfo, rasInfoRyOffset + 2))
                {
                    sourceX = unchecked((short)_bus.ReadWord(rasInfo + rasInfoRxOffset));
                    sourceY = unchecked((short)_bus.ReadWord(rasInfo + rasInfoRyOffset));
                }

                uint[]? pixels = null;
                var sourceWidth = (int)width;
                var sourceHeight = (int)height;
                var background = 0xFF00_0000u;
                var dpmsOff = false;
                if (isRtg)
                {
                    hasRtg = true;
                    sourceWidth = surface!.Width;
                    sourceHeight = surface.Height;
                    background = surface.PixelFormat == CyberGraphicsPixelFormat.Lut8
                        ? surface.Palette[0]
                        : 0xFF00_0000u;
                    dpmsOff = GetViewPortDpmsLevel(viewPort) != 0;
                    pixels = _rtgDevice?.GetSurfacePixels(surface);
                }

                layers.Add(new CyberGraphicsDisplayLayer(
                    viewPort,
                    isRtg,
                    unchecked((short)_bus.ReadWord(viewPort + xOffset)),
                    unchecked((short)_bus.ReadWord(viewPort + yOffset)),
                    width,
                    height,
                    sourceX,
                    sourceY,
                    sourceWidth,
                    sourceHeight,
                    background,
                    dpmsOff,
                    pixels));
                viewPort = nextViewPort;
            }

            if (!hasRtg || layers.Count == 0 || outputWidth <= 0 || outputHeight <= 0)
            {
                return false;
            }

            composition = new CyberGraphicsDisplayComposition(
                outputWidth,
                outputHeight,
                topIsRtg,
                topDpmsOff,
                layers);
            return true;
        }

        internal void ResetRtgState()
        {
            _bitmaps.Clear();
            _rastPorts.Clear();
            _viewPorts.Clear();
            _viewPortModes.Clear();
            _locks.Clear();
            _viewPortDpmsLevels.Clear();
            _systemPatches.Reset();
            _rtgDevice?.Reset();
        }

        private bool TryInferMode(CyberGraphicsSurface surface, out CyberGraphicsMode mode)
        {
            foreach (var candidate in _modes.Values)
            {
                if (candidate.Width == surface.Width &&
                    candidate.Height == surface.Height &&
                    candidate.PixelFormat == surface.PixelFormat)
                {
                    mode = candidate;
                    return true;
                }
            }

            mode = default;
            return false;
        }

        internal int InstallSystemPatches(uint execBase)
            => _systemPatches.TryInstallAvailable(execBase);

        internal int BlitPlanarToRtg(
            uint sourceBitMap,
            int sourceX,
            int sourceY,
            uint destinationBitMap,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            byte planeMask,
            uint maskPlane = 0)
        {
            if (!_bitmaps.TryGetValue(destinationBitMap, out var destination) ||
                destination.GuestBaseAddress == 0)
            {
                return 0;
            }

            return CyberGraphicsPlanarBlitter.Blit(
                _bus,
                sourceBitMap,
                sourceX,
                sourceY,
                destination,
                destinationX,
                destinationY,
                width,
                height,
                minterm,
                planeMask,
                maskPlane);
        }

        internal int BlitRtgToRtg(
            uint sourceBitMap,
            int sourceX,
            int sourceY,
            uint destinationBitMap,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            byte writeMask,
            uint maskPlane = 0)
        {
            if (!_bitmaps.TryGetValue(sourceBitMap, out var source) ||
                !_bitmaps.TryGetValue(destinationBitMap, out var destination) ||
                width <= 0 || height <= 0)
            {
                return 0;
            }

            var sourceIsIndexed = source.PixelFormat == CyberGraphicsPixelFormat.Lut8;
            var destinationIsIndexed = destination.PixelFormat == CyberGraphicsPixelFormat.Lut8;
            var maskBytesPerRow = ((source.Width + 15) / 16) * 2;
            if (maskPlane != 0 && !_bus.IsMappedMemoryRange(maskPlane, checked(maskBytesPerRow * source.Height)))
            {
                return 0;
            }

            var pixels = new uint[checked(width * height)];
            var valid = new bool[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sx = sourceX + x;
                    var sy = sourceY + y;
                    var index = y * width + x;
                    if (source.Contains(sx, sy) &&
                        IsRtgMaskSet(maskPlane, maskBytesPerRow, sx, sy))
                    {
                        pixels[index] = sourceIsIndexed
                            ? source.ReadByte(_bus, checked(sy * source.BytesPerRow + sx))
                            : ReadSurfaceArgb(source, sx, sy);
                        valid[index] = true;
                    }
                }
            }

            var operation = (byte)((minterm >> 4) & 0x0F);
            var written = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var dx = destinationX + x;
                    var dy = destinationY + y;
                    if (!valid[index] || !destination.Contains(dx, dy))
                    {
                        continue;
                    }

                    if (sourceIsIndexed && destinationIsIndexed)
                    {
                        var sourcePen = (byte)pixels[index];
                        var offset = checked(dy * destination.BytesPerRow + dx);
                        var destinationPen = destination.ReadByte(_bus, offset);
                        var resultPen = (byte)ApplyRtgMinterm(sourcePen, destinationPen, operation);
                        destination.WriteByte(
                            _bus,
                            offset,
                            (byte)((resultPen & writeMask) | (destinationPen & ~writeMask)));
                        written++;
                        continue;
                    }

                    var sourceColor = sourceIsIndexed
                        ? source.Palette[(byte)pixels[index]]
                        : pixels[index];
                    var destinationColor = ReadSurfaceArgb(destination, dx, dy);
                    var result = ApplyRtgMinterm(sourceColor, destinationColor, operation);
                    if (destination.PixelFormat == CyberGraphicsPixelFormat.Lut8 && writeMask != 0xFF)
                    {
                        var sourcePen = FindNearestPaletteIndex(destination.Palette, result);
                        var destinationPen = FindNearestPaletteIndex(destination.Palette, destinationColor);
                        result = destination.Palette[(sourcePen & writeMask) | (destinationPen & ~writeMask)];
                    }

                    WriteSurfaceArgb(destination, dx, dy, result);
                    written++;
                }
            }

            return written;
        }

        internal int BlitRtgToPlanar(
            uint sourceBitMap,
            int sourceX,
            int sourceY,
            uint destinationBitMap,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            byte planeMask,
            uint maskPlane = 0)
        {
            const int bitMapBytesPerRowOffset = 0;
            const int bitMapRowsOffset = 2;
            const int bitMapDepthOffset = 5;
            const int bitMapPlanesOffset = 8;
            const int maximumPlanes = 8;
            if (!_bitmaps.TryGetValue(sourceBitMap, out var source) ||
                destinationBitMap == 0 || width <= 0 || height <= 0 ||
                !_bus.IsMappedMemoryRange(destinationBitMap, bitMapPlanesOffset + maximumPlanes * 4))
            {
                return 0;
            }

            var sourceIsIndexed = source.PixelFormat == CyberGraphicsPixelFormat.Lut8;
            if (!sourceIsIndexed && source.ColorMapAddress == 0)
            {
                // A native planar BitMap carries no ColorMap. Deep pixels can
                // only be reduced to pens through the CyberGraphX bitmap's
                // screen-derived associated palette.
                return 0;
            }

            var maskBytesPerRow = ((source.Width + 15) / 16) * 2;
            if (maskPlane != 0 && !_bus.IsMappedMemoryRange(maskPlane, checked(maskBytesPerRow * source.Height)))
            {
                return 0;
            }

            var bytesPerRow = _bus.ReadWord(destinationBitMap + bitMapBytesPerRowOffset);
            var rows = _bus.ReadWord(destinationBitMap + bitMapRowsOffset);
            var depth = Math.Min(maximumPlanes, (int)_bus.ReadByte(destinationBitMap + bitMapDepthOffset));
            if (bytesPerRow == 0 || rows == 0 || depth == 0)
            {
                return 0;
            }

            Span<uint> planes = stackalloc uint[maximumPlanes];
            for (var plane = 0; plane < depth; plane++)
            {
                planes[plane] = _bus.ReadLong(destinationBitMap + bitMapPlanesOffset + (uint)(plane * 4));
            }

            var operation = (byte)((minterm >> 4) & 0x0F);
            var written = 0;
            for (var y = 0; y < height; y++)
            {
                var sy = sourceY + y;
                var dy = destinationY + y;
                if ((uint)sy >= (uint)source.Height || (uint)dy >= rows)
                {
                    continue;
                }

                for (var x = 0; x < width; x++)
                {
                    var sx = sourceX + x;
                    var dx = destinationX + x;
                    if (!source.Contains(sx, sy) || dx < 0 || dx >= bytesPerRow * 8 ||
                        !IsRtgMaskSet(maskPlane, maskBytesPerRow, sx, sy))
                    {
                        continue;
                    }

                    var sourcePen = sourceIsIndexed
                        ? source.ReadByte(_bus, checked(sy * source.BytesPerRow + sx))
                        : FindNearestPaletteIndex(source.Palette, ReadSurfaceArgb(source, sx, sy));
                    var byteOffset = checked((uint)(dy * bytesPerRow + (dx >> 3)));
                    var bit = (byte)(0x80 >> (dx & 7));
                    var destinationPen = 0;
                    for (var plane = 0; plane < depth; plane++)
                    {
                        var planeAddress = planes[plane];
                        if (planeAddress == uint.MaxValue ||
                            (planeAddress != 0 && (_bus.ReadByte(planeAddress + byteOffset) & bit) != 0))
                        {
                            destinationPen |= 1 << plane;
                        }
                    }

                    var resultPen = (byte)ApplyRtgMinterm(sourcePen, (byte)destinationPen, operation);
                    for (var plane = 0; plane < depth; plane++)
                    {
                        if ((planeMask & (1 << plane)) == 0 || planes[plane] is 0 or uint.MaxValue)
                        {
                            continue;
                        }

                        var address = planes[plane] + byteOffset;
                        var value = _bus.ReadByte(address);
                        value = ((resultPen >> plane) & 1) != 0
                            ? (byte)(value | bit)
                            : (byte)(value & (byte)~bit);
                        _bus.WriteByte(address, value, 0);
                    }

                    written++;
                }
            }

            return written;
        }

        private bool IsRtgMaskSet(uint maskPlane, int maskBytesPerRow, int x, int y)
        {
            if (maskPlane == 0)
            {
                return true;
            }

            var offset = checked((uint)(y * maskBytesPerRow + (x >> 3)));
            return ((_bus.ReadByte(maskPlane + offset) >> (7 - (x & 7))) & 1) != 0;
        }

        private static uint ApplyRtgMinterm(uint source, uint destination, byte operation)
        {
            var result = 0u;
            if ((operation & 0x8) != 0) result |= source & destination;
            if ((operation & 0x4) != 0) result |= source & ~destination;
            if ((operation & 0x2) != 0) result |= ~source & destination;
            if ((operation & 0x1) != 0) result |= ~source & ~destination;
            return result;
        }

        public void InstallTrapVectors(uint libraryBase)
        {
            if (libraryBase < 258)
            {
                throw new ArgumentOutOfRangeException(nameof(libraryBase));
            }

            _libraryBase = libraryBase;
            foreach (var vector in AllVectorArray)
            {
                var capturedOffset = vector.Offset;
                _bus.RegisterHostTrapStub(AddOffset(libraryBase, capturedOffset), state => Invoke(capturedOffset, state));
            }
        }

        public bool Invoke(int vectorOffset, M68kCpuState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (!_vectorsByOffset.TryGetValue(vectorOffset, out var vector))
            {
                return false;
            }

            CallObserved?.Invoke(vector.Function);
            switch (vector.Function)
            {
                case CyberGraphicsFunction.Open:
                    _systemPatches.TryInstallAvailable(_bus.ReadLong(4));
                    _openCount++;
                    state.D[0] = _libraryBase;
                    break;
                case CyberGraphicsFunction.Close:
                    if (_openCount != 0)
                    {
                        _openCount--;
                    }

                    state.D[0] = 0;
                    break;
                case CyberGraphicsFunction.Expunge:
                case CyberGraphicsFunction.Reserved:
                    state.D[0] = 0;
                    break;
                case CyberGraphicsFunction.IsCyberModeId:
                    state.D[0] = _modes.ContainsKey(state.D[0]) ? 1u : 0u;
                    break;
                case CyberGraphicsFunction.BestCModeIdTagList:
                    state.D[0] = FindBestMode(state.A[0], requestTags: false);
                    break;
                case CyberGraphicsFunction.CModeRequestTagList:
                    state.D[0] = FindBestMode(state.A[1], requestTags: true);
                    break;
                case CyberGraphicsFunction.AllocCModeListTagList:
                    state.D[0] = AllocateModeList(state.A[1]);
                    break;
                case CyberGraphicsFunction.FreeCModeList:
                    FreeModeList(state.A[0]);
                    break;
                case CyberGraphicsFunction.ScalePixelArray:
                    state.D[0] = ScalePixelArray(state, alpha: false);
                    break;
                case CyberGraphicsFunction.GetCyberMapAttr:
                    state.D[0] = GetCyberMapAttribute(state.A[0], state.D[0]);
                    break;
                case CyberGraphicsFunction.GetCyberIdAttr:
                    state.D[0] = GetCyberIdAttribute(state.D[0], state.D[1]);
                    break;
                case CyberGraphicsFunction.ReadRgbPixel:
                    state.D[0] = ReadRgbPixel(state.A[1], UWord(state.D[0]), UWord(state.D[1]));
                    break;
                case CyberGraphicsFunction.WriteRgbPixel:
                    state.D[0] = WriteRgbPixel(state.A[1], UWord(state.D[0]), UWord(state.D[1]), state.D[2]);
                    break;
                case CyberGraphicsFunction.ReadPixelArray:
                    state.D[0] = ReadPixelArray(state);
                    break;
                case CyberGraphicsFunction.WritePixelArray:
                    state.D[0] = WritePixelArray(state, alpha: false);
                    break;
                case CyberGraphicsFunction.MovePixelArray:
                    state.D[0] = MovePixelArray(state);
                    break;
                case CyberGraphicsFunction.InvertPixelArray:
                    state.D[0] = InvertPixelArray(state);
                    break;
                case CyberGraphicsFunction.FillPixelArray:
                    state.D[0] = FillPixelArray(state);
                    break;
                case CyberGraphicsFunction.DoCDrawMethodTagList:
                    DoCDrawMethod(state.A[0], state.A[1]);
                    break;
                case CyberGraphicsFunction.CVideoCtrlTagList:
                    SetVideoControl(state.A[0], state.A[1]);
                    break;
                case CyberGraphicsFunction.LockBitMapTagList:
                    state.D[0] = LockBitMap(state.A[0], state.A[1]);
                    break;
                case CyberGraphicsFunction.UnLockBitMap:
                    _locks.Remove(state.A[0]);
                    break;
                case CyberGraphicsFunction.UnLockBitMapTagList:
                    UnlockBitMap(state.A[0], state.A[1]);
                    break;
                case CyberGraphicsFunction.ExtractColor:
                    state.D[0] = ExtractColor(state);
                    break;
                case CyberGraphicsFunction.WriteLutPixelArray:
                    state.D[0] = WriteLutPixelArray(state);
                    break;
                case CyberGraphicsFunction.WritePixelArrayAlpha:
                    state.D[0] = WritePixelArray(state, alpha: true);
                    break;
                case CyberGraphicsFunction.BltTemplateAlpha:
                    BltTemplateAlpha(state);
                    break;
                case CyberGraphicsFunction.ProcessPixelArray:
                    ProcessPixelArray(state);
                    break;
                case CyberGraphicsFunction.BltBitMapAlpha:
                    state.D[0] = BltBitMapAlpha(state, destinationIsRastPort: false);
                    break;
                case CyberGraphicsFunction.BltBitMapRastPortAlpha:
                    state.D[0] = BltBitMapAlpha(state, destinationIsRastPort: true);
                    break;
                case CyberGraphicsFunction.ScalePixelArrayAlpha:
                    state.D[0] = ScalePixelArray(state, alpha: true);
                    break;
                case CyberGraphicsFunction.ScaleMapRastPortAlpha:
                    state.D[0] = ScaleMapRastPortAlpha(state);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown CyberGraphX function {vector.Function}.");
            }

            return true;
        }

        public void Reset()
        {
            foreach (var allocation in _modeListAllocations)
            {
                _guestServices?.Free(allocation.Key, allocation.Value);
            }

            _modeListAllocations.Clear();
            _locks.Clear();
            _viewPortDpmsLevels.Clear();
            _bitmaps.Clear();
            _rastPorts.Clear();
            _viewPorts.Clear();
            _libraryBase = 0;
            _openCount = 0;
            _nextLockHandle = 1;
        }

        private uint FindBestMode(uint tagList, bool requestTags)
        {
            var requestedDepth = requestTags ? 0u : GetTagData(tagList, BestModeDepth, 8);
            var requestedWidth = requestTags ? 0u : GetTagData(tagList, BestModeNominalWidth, 800);
            var requestedHeight = requestTags ? 0u : GetTagData(tagList, BestModeNominalHeight, 600);
            var requestedMonitor = requestTags ? 0u : GetTagData(tagList, BestModeMonitorId, 0);
            var requestedBoardName = requestTags ? null : ReadAscii(GetTagData(tagList, BestModeBoardName, 0));
            var minDepth = requestTags ? GetTagData(tagList, ModeRequestMinDepth, 8) : 0;
            var maxDepth = requestTags ? GetTagData(tagList, ModeRequestMaxDepth, 32) : uint.MaxValue;
            var minWidth = requestTags ? GetTagData(tagList, ModeRequestMinWidth, 320) : 0;
            var maxWidth = requestTags ? GetTagData(tagList, ModeRequestMaxWidth, 1920) : uint.MaxValue;
            var minHeight = requestTags ? GetTagData(tagList, ModeRequestMinHeight, 240) : 0;
            var maxHeight = requestTags ? GetTagData(tagList, ModeRequestMaxHeight, 1600) : uint.MaxValue;
            var colorModels = requestTags ? GetTagData(tagList, ModeRequestColorModelArray, 0) : 0;

            CyberGraphicsMode? best = null;
            ulong bestScore = ulong.MaxValue;
            foreach (var mode in _modes.Values)
            {
                if (mode.Depth < minDepth || mode.Depth > maxDepth ||
                    mode.Width < minWidth || mode.Width > maxWidth ||
                    mode.Height < minHeight || mode.Height > maxHeight ||
                    (requestedDepth != 0 && mode.Depth != requestedDepth) ||
                    (requestedMonitor != 0 && mode.MonitorId != requestedMonitor) ||
                    (requestedBoardName != null && !string.Equals(mode.BoardName, requestedBoardName, StringComparison.OrdinalIgnoreCase)) ||
                    (colorModels != 0 && !ColorModelArrayContains(colorModels, mode.PixelFormat)))
                {
                    continue;
                }

                var widthDelta = AbsoluteDelta(mode.Width, requestedWidth);
                var heightDelta = AbsoluteDelta(mode.Height, requestedHeight);
                var depthDelta = AbsoluteDelta(mode.Depth, requestedDepth);
                var score = ((ulong)widthDelta << 32) | ((ulong)heightDelta << 16) | depthDelta;
                if (score < bestScore || (score == bestScore && mode.DisplayId < best?.DisplayId))
                {
                    best = mode;
                    bestScore = score;
                }
            }

            return best?.DisplayId ?? (requestTags ? 0u : InvalidDisplayId);
        }

        private uint AllocateModeList(uint tagList)
        {
            if (_guestServices == null)
            {
                return 0;
            }

            var modes = _modes.Values
                .Where(mode => ModeMatchesRequest(mode, tagList))
                .OrderBy(mode => mode.Width)
                .ThenBy(mode => mode.Height)
                .ThenBy(mode => mode.Depth)
                .ThenBy(mode => mode.DisplayId)
                .ToArray();
            if (modes.Length == 0)
            {
                return 0;
            }

            var size = checked(ExecListSize + (modes.Length * CyberModeNodeSize));
            var list = _guestServices.Allocate(size);
            if (list == 0 || !_bus.IsMappedMemoryRange(list, size))
            {
                if (list != 0)
                {
                    _guestServices.Free(list, size);
                }

                return 0;
            }

            _bus.ClearMemory(list, size);
            var tail = list + 4;
            var firstNode = modes.Length == 0 ? tail : list + ExecListSize;
            var lastNode = modes.Length == 0 ? list : list + ExecListSize + (uint)((modes.Length - 1) * CyberModeNodeSize);
            _bus.WriteLong(list, firstNode);
            _bus.WriteLong(list + 4, 0);
            _bus.WriteLong(list + 8, lastNode);
            for (var i = 0; i < modes.Length; i++)
            {
                var node = list + ExecListSize + (uint)(i * CyberModeNodeSize);
                var previous = i == 0 ? list : node - CyberModeNodeSize;
                var next = i + 1 == modes.Length ? tail : node + CyberModeNodeSize;
                var mode = modes[i];
                _bus.WriteLong(node, next);
                _bus.WriteLong(node + 4, previous);
                _bus.WriteLong(node + 10, node + 14);
                WriteFixedAscii(node + 14, DisplayNameLength, mode.Name);
                _bus.WriteLong(node + 46, mode.DisplayId);
                _bus.WriteWord(node + 50, mode.Width);
                _bus.WriteWord(node + 52, mode.Height);
                _bus.WriteWord(node + 54, mode.Depth);
                _bus.WriteLong(node + 56, 0);
            }

            _modeListAllocations[list] = size;
            return list;
        }

        private void FreeModeList(uint list)
        {
            if (_modeListAllocations.Remove(list, out var size))
            {
                _guestServices?.Free(list, size);
            }
        }

        private uint GetCyberMapAttribute(uint bitMap, uint attribute)
        {
            if (!_bitmaps.TryGetValue(bitMap, out var surface))
            {
                return attribute is CyberMapAttrIsCyberGfx or CyberMapAttrIsLinearMemory ? 0u : InvalidDisplayId;
            }

            return attribute switch
            {
                CyberMapAttrXMod => (uint)surface.BytesPerRow,
                CyberMapAttrBytesPerPixel => (uint)surface.BytesPerPixel,
                CyberMapAttrDisplayAddress => surface.GuestBaseAddress,
                CyberMapAttrPixelFormat => (uint)surface.PixelFormat,
                CyberMapAttrWidth => (uint)surface.Width,
                CyberMapAttrHeight => (uint)surface.Height,
                CyberMapAttrDepth => (uint)surface.Depth,
                CyberMapAttrIsCyberGfx => uint.MaxValue,
                CyberMapAttrIsLinearMemory => surface.GuestBaseAddress != 0 ? uint.MaxValue : 0u,
                CyberMapAttrColorMap => surface.ColorMapAddress,
                _ => InvalidDisplayId
            };
        }

        private uint GetCyberIdAttribute(uint attribute, uint displayId)
        {
            if (!_modes.TryGetValue(displayId, out var mode))
            {
                return InvalidDisplayId;
            }

            return attribute switch
            {
                CyberIdAttrPixelFormat => (uint)mode.PixelFormat,
                CyberIdAttrWidth => mode.Width,
                CyberIdAttrHeight => mode.Height,
                CyberIdAttrDepth => mode.Depth,
                CyberIdAttrBytesPerPixel => (uint)CyberGraphicsSurface.GetBytesPerPixel(mode.PixelFormat),
                _ => InvalidDisplayId
            };
        }

        private uint LockBitMap(uint bitMap, uint tagList)
        {
            if (!_bitmaps.TryGetValue(bitMap, out var surface) || surface.GuestBaseAddress == 0)
            {
                return 0;
            }

            foreach (var (tag, outputAddress) in EnumerateTags(tagList))
            {
                var value = tag switch
                {
                    LockWidth => (uint)surface.Width,
                    LockHeight => (uint)surface.Height,
                    LockDepth => (uint)surface.Depth,
                    LockPixelFormat => (uint)surface.PixelFormat,
                    LockBytesPerPixel => (uint)surface.BytesPerPixel,
                    LockBytesPerRow => (uint)surface.BytesPerRow,
                    LockBaseAddress => surface.GuestBaseAddress,
                    _ => InvalidDisplayId
                };
                if (value != InvalidDisplayId && outputAddress != 0 && _bus.IsMappedMemoryRange(outputAddress, 4))
                {
                    _bus.WriteLong(outputAddress, value);
                }
            }

            var handle = _nextLockHandle++;
            if (handle == 0)
            {
                handle = _nextLockHandle++;
            }

            _locks[handle] = surface;
            return handle;
        }

        private void UnlockBitMap(uint handle, uint tagList)
        {
            var reallyUnlock = GetTagData(tagList, UnlockReally, 1) != 0;
            if (reallyUnlock)
            {
                _locks.Remove(handle);
            }
        }

        private void SetVideoControl(uint viewPort, uint tagList)
        {
            if (!_viewPorts.ContainsKey(viewPort))
            {
                return;
            }

            var level = GetTagData(tagList, SetVideoDpmsLevel, GetViewPortDpmsLevel(viewPort));
            if (level <= 3)
            {
                _viewPortDpmsLevels[viewPort] = level;
            }
        }

        private void DoCDrawMethod(uint hook, uint rastPort)
        {
            if (_guestServices == null ||
                !TryGetRastPortSurface(rastPort, out var surface) ||
                surface.GuestBaseAddress == 0 ||
                hook == 0 ||
                !_bus.IsMappedMemoryRange(hook, 12))
            {
                return;
            }

            var entry = _bus.ReadLong(hook + 8);
            if (entry == 0)
            {
                return;
            }

            var message = _guestServices.Allocate(CDrawMessageSize);
            if (message == 0 || !_bus.IsMappedMemoryRange(message, CDrawMessageSize))
            {
                if (message != 0)
                {
                    _guestServices.Free(message, CDrawMessageSize);
                }

                return;
            }

            _bus.WriteLong(message, surface.GuestBaseAddress);
            _bus.WriteLong(message + 4, 0);
            _bus.WriteLong(message + 8, 0);
            _bus.WriteLong(message + 12, (uint)surface.Width);
            _bus.WriteLong(message + 16, (uint)surface.Height);
            _bus.WriteWord(message + 20, checked((ushort)surface.BytesPerRow));
            _bus.WriteWord(message + 22, checked((ushort)surface.BytesPerPixel));
            _bus.WriteWord(message + 24, checked((ushort)surface.PixelFormat));
            _guestServices.InvokeHook(entry, rastPort, message);
            _guestServices.Free(message, CDrawMessageSize);
        }

        private uint GetTagData(uint tagList, uint wantedTag, uint defaultValue)
        {
            foreach (var (tag, data) in EnumerateTags(tagList))
            {
                if (tag == wantedTag)
                {
                    return data;
                }
            }

            return defaultValue;
        }

        private IEnumerable<KeyValuePair<uint, uint>> EnumerateTags(uint tagList)
        {
            var current = tagList;
            var visitedLists = new HashSet<uint>();
            var remaining = 4096;
            while (current != 0 && remaining-- > 0 && visitedLists.Add(current))
            {
                while (remaining-- > 0 && _bus.IsMappedMemoryRange(current, 8))
                {
                    var tag = _bus.ReadLong(current);
                    var data = _bus.ReadLong(current + 4);
                    current += 8;
                    switch (tag)
                    {
                        case TagDone:
                            current = 0;
                            break;
                        case TagIgnore:
                            continue;
                        case TagMore:
                            current = data;
                            break;
                        case TagSkip:
                            var skipBytes = (ulong)data * 8UL;
                            current = skipBytes <= uint.MaxValue - current
                                ? current + (uint)skipBytes
                                : 0;
                            continue;
                        default:
                            yield return new KeyValuePair<uint, uint>(tag, data);
                            continue;
                    }

                    break;
                }
            }
        }

        private bool ModeMatchesRequest(CyberGraphicsMode mode, uint tagList)
        {
            var minDepth = GetTagData(tagList, ModeRequestMinDepth, 8);
            var maxDepth = GetTagData(tagList, ModeRequestMaxDepth, 32);
            var minWidth = GetTagData(tagList, ModeRequestMinWidth, 320);
            var maxWidth = GetTagData(tagList, ModeRequestMaxWidth, 1920);
            var minHeight = GetTagData(tagList, ModeRequestMinHeight, 240);
            var maxHeight = GetTagData(tagList, ModeRequestMaxHeight, 1600);
            var colorModels = GetTagData(tagList, ModeRequestColorModelArray, 0);
            return mode.Depth >= minDepth && mode.Depth <= maxDepth &&
                mode.Width >= minWidth && mode.Width <= maxWidth &&
                mode.Height >= minHeight && mode.Height <= maxHeight &&
                (colorModels == 0 || ColorModelArrayContains(colorModels, mode.PixelFormat));
        }

        private bool ColorModelArrayContains(uint address, CyberGraphicsPixelFormat pixelFormat)
        {
            for (var i = 0; i < 256 && _bus.IsMappedMemoryRange(address, 2); i++, address += 2)
            {
                var value = _bus.ReadWord(address);
                if (value == ushort.MaxValue)
                {
                    return false;
                }

                if (value == (ushort)pixelFormat)
                {
                    return true;
                }
            }

            return false;
        }

        private string? ReadAscii(uint address)
        {
            if (address == 0)
            {
                return null;
            }

            var characters = new char[255];
            var length = 0;
            while (length < characters.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1))
            {
                var value = _bus.ReadByte(address + (uint)length);
                if (value == 0)
                {
                    return new string(characters, 0, length);
                }

                characters[length++] = value <= 0x7F ? (char)value : '?';
            }

            return null;
        }

        private void WriteFixedAscii(uint address, int byteCount, string value)
        {
            for (var i = 0; i < byteCount; i++)
            {
                var character = i < value.Length ? value[i] : '\0';
                _bus.WriteByte(address + (uint)i, character <= 0x7F ? (byte)character : (byte)'?', 0);
            }
        }

        private static uint AbsoluteDelta(uint value, uint target)
            => target == 0 ? 0 : value >= target ? value - target : target - value;

        private static ushort UWord(uint value)
            => (ushort)value;

        private static short Word(uint value)
            => unchecked((short)value);

        private static uint AddOffset(uint address, int offset)
            => unchecked((uint)((int)address + offset));

        private static void ValidateObjectAddress(uint address, string parameterName)
        {
            if (address == 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
