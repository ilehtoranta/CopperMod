/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        // intuition/screens.h: SA_Dummy is TAG_USER + 32.
        private const uint ScreenTagWidth = 0x8000_0023;
        private const uint ScreenTagHeight = 0x8000_0024;
        private const uint ScreenTagDepth = 0x8000_0025;
        private const uint ScreenTagType = 0x8000_002D;
        private const uint ScreenTagBitMap = 0x8000_002E;
        private const uint ScreenTagDisplayId = 0x8000_0032;
        private const int NewScreenTypeOffset = 0x0E;
        private const int NewScreenCustomBitMapOffset = 0x1C;
        private const int ExtNewScreenExtensionOffset = 0x20;
        private const int ViewPortColorMapOffset = 0x04;
        private const ushort NewScreenExtended = 0x1000;
        private const ushort ScreenBehind = 0x0001;

        private readonly List<RtgOpenScreenContext> _rtgOpenScreenContexts = new();
        private readonly HashSet<uint> _rtgIntuitionScreens = new();
        private uint _rtgOpenScreenContinuationAddress;

        bool ICyberGraphicsGuestServices.TryInvokeIntuitionLibraryPatch(
            int vectorOffset,
            uint originalTarget,
            M68kCpuState state)
        {
            switch (vectorOffset)
            {
                case -66: // CloseScreen
                    if (!IsRtgScreen(state.A[0])) return false;
                    _rtgIntuitionScreens.Remove(state.A[0]);
                    CyberGraphics.UnregisterViewPort(state.A[0] + ScreenViewPortOffset);
                    CyberGraphics.UnregisterRastPort(state.A[0] + ScreenRastPortOffset);
                    // Intuition still owns and closes the Screen and its layers.
                    return false;
                case -198: // OpenScreen
                case -612: // OpenScreenTagList
                    return TryBeginRtgOpenScreen(vectorOffset, originalTarget, state);
                case -246: // ScreenToBack
                    if (!IsRtgScreen(state.A[0])) return false;
                    // Preserve Intuition's screen-list and layer bookkeeping.
                    return false;
                case -252: // ScreenToFront
                    if (!IsRtgScreen(state.A[0])) return false;
                    return false;
                case -378: // MakeScreen
                case -384: // RemakeDisplay
                case -390: // RethinkDisplay
                    // The native calls must still rebuild Intuition's View and
                    // screen-family state. Patched graphics LoadView observes the
                    // resulting RTG ViewPort when it becomes the displayed view.
                    return false;
                default:
                    // Native Intuition remains responsible for screen/window and
                    // ScreenBuffer structures. Its V39 buffer swap ultimately
                    // reaches the patched graphics.library ChangeVPBitMap vector.
                    return false;
            }
        }

        private bool TryBeginRtgOpenScreen(int vectorOffset, uint originalTarget, M68kCpuState state)
        {
            if (originalTarget == 0 || state.A[7] > uint.MaxValue - 4 ||
                !_machine.Bus.IsMappedMemoryRange(state.A[7], 4) ||
                !TryReadRtgScreenRequest(vectorOffset, state, out var request))
            {
                return false;
            }

            var continuation = EnsureRtgOpenScreenContinuation();
            if (continuation == 0)
            {
                return false;
            }

            request.OriginalReturnAddress = _machine.Bus.ReadLong(state.A[7]);
            request.ExpectedStackPointer = state.A[7] + 4;
            _rtgOpenScreenContexts.Add(request);
            _machine.Bus.WriteLong(state.A[7], continuation);

            // This is an around-call rather than a replacement: Intuition consumes
            // the existing call frame, creates its real Screen, then returns through
            // our one-shot continuation before resuming the application.
            state.ProgramCounter = originalTarget;
            return true;
        }

        private bool TryReadRtgScreenRequest(
            int vectorOffset,
            M68kCpuState state,
            out RtgOpenScreenContext request)
        {
            request = null!;
            var newScreen = state.A[0];
            Dictionary<uint, uint>? extensionTags = null;
            var tags = vectorOffset == -612 ? ReadPatchTags(state.A[1]) : null;

            ushort newScreenType = 0;
            uint customBitMap = 0;
            uint width = 0;
            uint height = 0;
            uint depth = 0;
            if (newScreen != 0 && _machine.Bus.IsMappedMemoryRange(newScreen, ExtNewScreenExtensionOffset + 4))
            {
                width = _machine.Bus.ReadWord(newScreen + NewScreenWidthOffset);
                height = _machine.Bus.ReadWord(newScreen + NewScreenHeightOffset);
                depth = _machine.Bus.ReadByte(newScreen + NewScreenDepthOffset);
                newScreenType = _machine.Bus.ReadWord(newScreen + NewScreenTypeOffset);
                customBitMap = _machine.Bus.ReadLong(newScreen + NewScreenCustomBitMapOffset);
                if ((newScreenType & NewScreenExtended) != 0)
                {
                    extensionTags = ReadPatchTags(
                        _machine.Bus.ReadLong(newScreen + ExtNewScreenExtensionOffset));
                }
            }

            // OpenScreenTagList tags take precedence over ExtNewScreen tags. A
            // 16-bit ViewModes field can never identify a CyberGraphX ModeID.
            var displayId = GetTag(tags, ScreenTagDisplayId,
                GetTag(extensionTags, ScreenTagDisplayId, CyberGraphicsLibrary.InvalidDisplayId));
            if (!CyberGraphics.TryGetMode(displayId, out var mode))
            {
                return false;
            }

            width = GetTag(tags, ScreenTagWidth, GetTag(extensionTags, ScreenTagWidth, width));
            height = GetTag(tags, ScreenTagHeight, GetTag(extensionTags, ScreenTagHeight, height));
            depth = GetTag(tags, ScreenTagDepth, GetTag(extensionTags, ScreenTagDepth, depth));
            var screenType = (ushort)GetTag(tags, ScreenTagType,
                GetTag(extensionTags, ScreenTagType, newScreenType));
            customBitMap = GetTag(tags, ScreenTagBitMap,
                GetTag(extensionTags, ScreenTagBitMap, customBitMap));

            if (width > ushort.MaxValue || height > ushort.MaxValue || depth > ushort.MaxValue)
            {
                return false;
            }

            request = new RtgOpenScreenContext(
                mode,
                width == 0 ? mode.Width : (ushort)width,
                height == 0 ? mode.Height : (ushort)height,
                depth == 0 ? mode.Depth : (ushort)depth,
                screenType,
                customBitMap);
            return true;
        }

        private uint EnsureRtgOpenScreenContinuation()
        {
            if (_rtgOpenScreenContinuationAddress != 0 &&
                _machine.Bus.HasHostTrapStub(_rtgOpenScreenContinuationAddress))
            {
                return _rtgOpenScreenContinuationAddress;
            }

            var address = ((ICyberGraphicsGuestServices)this).Allocate(4);
            if (address == 0)
            {
                return 0;
            }

            _rtgOpenScreenContinuationAddress = address;
            _machine.Bus.RegisterHostTrapStub(address, CompleteRtgOpenScreen);
            return address;
        }

        private void CompleteRtgOpenScreen(M68kCpuState state)
        {
            for (var index = _rtgOpenScreenContexts.Count - 1; index >= 0; index--)
            {
                var request = _rtgOpenScreenContexts[index];
                if (request.ExpectedStackPointer != state.A[7])
                {
                    continue;
                }

                _rtgOpenScreenContexts.RemoveAt(index);
                if (state.D[0] != 0)
                {
                    AssociateRtgIntuitionScreen(state.D[0], request);
                }

                state.ProgramCounter = request.OriginalReturnAddress;
                return;
            }
        }

        private void AssociateRtgIntuitionScreen(uint screen, RtgOpenScreenContext request)
        {
            var viewPort = screen + ScreenViewPortOffset;
            var rastPort = screen + ScreenRastPortOffset;
            var embeddedBitMap = screen + ScreenBitMapOffset;
            var candidates = new List<uint>(4) { embeddedBitMap };
            if (_machine.Bus.IsMappedMemoryRange(rastPort + RastPortBitMapOffset, 4))
            {
                candidates.Add(_machine.Bus.ReadLong(rastPort + RastPortBitMapOffset));
            }

            if (_machine.Bus.IsMappedMemoryRange(viewPort + ViewPortRasInfoOffset, 4))
            {
                var rasInfo = _machine.Bus.ReadLong(viewPort + ViewPortRasInfoOffset);
                if (rasInfo != 0 && _machine.Bus.IsMappedMemoryRange(rasInfo + RasInfoBitMapOffset, 4))
                {
                    candidates.Add(_machine.Bus.ReadLong(rasInfo + RasInfoBitMapOffset));
                }
            }

            CyberGraphicsSurface? surface = null;
            foreach (var bitMap in candidates)
            {
                if (bitMap != 0 && CyberGraphics.TryResolveBitMapSurface(bitMap, out surface))
                {
                    break;
                }
            }

            if (surface == null && request.AllocatedBitMaps.Count != 0)
            {
                CyberGraphics.TryGetBitMapSurface(request.AllocatedBitMaps[^1], out surface!);
            }

            if (surface == null)
            {
                return;
            }

            if (_machine.Bus.IsMappedMemoryRange(viewPort + ViewPortColorMapOffset, 4))
            {
                var colorMap = _machine.Bus.ReadLong(viewPort + ViewPortColorMapOffset);
                if (colorMap != 0)
                {
                    surface.AssociateColorMap(colorMap);
                    InitializeRtgPaletteFromColorMap(surface, colorMap);
                }
            }

            foreach (var bitMap in candidates)
            {
                // Screen.BitMap and even RastPort.BitMap may only be opaque
                // compatibility shells for an RTG screen. Their plane fields
                // are not a framebuffer discovery mechanism. The association
                // is established by the selected ModeID and the bitmap that was
                // allocated while Intuition's OpenScreen call was active.
                if (bitMap != 0 && _machine.Bus.IsMappedMemoryRange(bitMap, BitMapPlanesOffset))
                {
                    CyberGraphics.RegisterBitMap(bitMap, surface);
                }
            }

            CyberGraphics.RegisterRastPort(rastPort, surface);
            CyberGraphics.RegisterViewPort(viewPort, surface, request.Mode);
            _rtgIntuitionScreens.Add(screen);
            if ((request.ScreenType & ScreenBehind) == 0)
            {
                CyberGraphics.SelectFrontViewPort(viewPort);
            }
        }

        private void InitializeRtgPaletteFromColorMap(CyberGraphicsSurface surface, uint colorMap)
        {
            const int colorMapTypeOffset = 0x01;
            const int colorMapCountOffset = 0x02;
            const int colorMapColorTableOffset = 0x04;
            const int colorMapLowColorBitsOffset = 0x0C;
            if (!_machine.Bus.IsMappedMemoryRange(colorMap, colorMapLowColorBitsOffset + 4))
            {
                return;
            }

            var count = Math.Min(surface.Palette.Length, _machine.Bus.ReadWord(colorMap + colorMapCountOffset));
            var highTable = _machine.Bus.ReadLong(colorMap + colorMapColorTableOffset);
            var lowTable = _machine.Bus.ReadByte(colorMap + colorMapTypeOffset) != 0
                ? _machine.Bus.ReadLong(colorMap + colorMapLowColorBitsOffset)
                : 0;
            if (count == 0 || highTable == 0 ||
                !_machine.Bus.IsMappedMemoryRange(highTable, count * 2) ||
                (lowTable != 0 && !_machine.Bus.IsMappedMemoryRange(lowTable, count * 2)))
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                var high = _machine.Bus.ReadWord(highTable + (uint)(index * 2));
                var low = lowTable != 0
                    ? _machine.Bus.ReadWord(lowTable + (uint)(index * 2))
                    : (ushort)0;
                var red = (uint)(((high >> 8) & 0x0F) << 4) | ((uint)(low >> 8) & 0x0F);
                var green = (uint)(((high >> 4) & 0x0F) << 4) | ((uint)(low >> 4) & 0x0F);
                var blue = (uint)((high & 0x0F) << 4) | ((uint)low & 0x0F);
                surface.Palette[index] = 0xFF00_0000u | (red << 16) | (green << 8) | blue;
            }
        }

        private bool TryGetPendingRtgScreenAllocation(
            M68kCpuState state,
            out CyberGraphicsMode mode)
        {
            mode = default;
            if (_rtgOpenScreenContexts.Count == 0)
            {
                return false;
            }

            var request = _rtgOpenScreenContexts[^1];
            if (request.CustomBitMap != 0 || request.AllocatedBitMaps.Count != 0 ||
                state.D[0] is 0 or > 32768 || state.D[1] is 0 or > 32768 ||
                state.D[2] is 0 or > 32)
            {
                return false;
            }

            var widthMatches = state.D[0] == request.Width || state.D[0] == request.Mode.Width;
            var heightMatches = state.D[1] == request.Height || state.D[1] == request.Mode.Height;
            var depthMatches = state.D[2] == request.Depth || state.D[2] == request.Mode.Depth;
            if (!widthMatches || !heightMatches || !depthMatches)
            {
                return false;
            }

            mode = request.Mode;
            return true;
        }

        private void RecordPendingRtgScreenBitMap(uint bitMap)
        {
            if (bitMap != 0 && _rtgOpenScreenContexts.Count != 0)
            {
                _rtgOpenScreenContexts[^1].AllocatedBitMaps.Add(bitMap);
            }
        }

        private bool IsRtgScreen(uint screen)
            => screen != 0 &&
                (_rtgIntuitionScreens.Contains(screen) ||
                    (_machine.Bus.IsMappedMemoryRange(
                        screen + ScreenBitMapOffset,
                        BitMapPlanesOffset + 4) &&
                    CyberGraphics.IsRtgBitMap(screen + ScreenBitMapOffset)));

        private sealed class RtgOpenScreenContext
        {
            public RtgOpenScreenContext(
                CyberGraphicsMode mode,
                ushort width,
                ushort height,
                ushort depth,
                ushort screenType,
                uint customBitMap)
            {
                Mode = mode;
                Width = width;
                Height = height;
                Depth = depth;
                ScreenType = screenType;
                CustomBitMap = customBitMap;
            }

            public CyberGraphicsMode Mode { get; }
            public ushort Width { get; }
            public ushort Height { get; }
            public ushort Depth { get; }
            public ushort ScreenType { get; }
            public uint CustomBitMap { get; }
            public List<uint> AllocatedBitMaps { get; } = new();
            public uint OriginalReturnAddress { get; set; }
            public uint ExpectedStackPointer { get; set; }
        }
    }
}
