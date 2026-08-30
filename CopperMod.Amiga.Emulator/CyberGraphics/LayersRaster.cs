/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        private bool _validatedRasterScopeActive;
        // LayersRasterCore rejects nested provider execution. Reuse one full
        // CPU frame behind that guard so validated raster dispatch does not
        // allocate a CPU/FPU/MMU object graph for every graphics call.
        private readonly M68kCpuState _validatedRasterState = new();
        private readonly ValidatedLayersRasterMemory _validatedLayerRasterMemory;
        private readonly ValidatedLayerDrawBoundsBackend
            _validatedLayerDrawBoundsBackend;
        private readonly Func<int, int, bool> _validatedRasterVisibility;
        private PooledValueBuffer<RasterClipFragment> _validatedVisibilityFragments;
        // Portable planar raster primitives require an atomic undo journal.
        // The validated Layers boundary is explicitly non-reentrant, so one
        // controller-owned pair can be cleared and reused without escaping or
        // sharing mutable storage across callers.
        private readonly List<uint> _validatedRasterSnapshotAddresses = new();
        private readonly List<byte> _validatedRasterSnapshotValues = new();
        private readonly List<uint> _validatedRasterNestedSnapshotAddresses = new();
        private readonly List<byte> _validatedRasterNestedSnapshotValues = new();
        private readonly List<uint> _validatedRasterTertiarySnapshotAddresses = new();
        private readonly List<byte> _validatedRasterTertiarySnapshotValues = new();
        private readonly List<int> _validatedRasterScrollPixels = new();
        private readonly List<(int X, int Y)> _validatedRasterEllipsePoints = new();
        private readonly List<(short X, short Y)> _validatedRasterPolyPoints = new();
        private readonly GraphicsRasterOperations.MintermScratch
            _validatedRasterMintermScratch = new();
        private readonly GraphicsRasterOperations.MintermScratch
            _validatedRasterNestedMintermScratch = new();
        private readonly GraphicsRasterOperations.VisibilityScratch
            _validatedRasterVisibilityScratch = new();
        private readonly GraphicsRasterOperations.VisibilityScratch
            _validatedRasterNestedVisibilityScratch = new();
        private readonly GraphicsRasterOperations.FloodScratch
            _validatedRasterFloodScratch = new();
        private readonly GraphicsRasterOperations.AreaScratch
            _validatedRasterAreaScratch = new();
        private readonly GraphicsBlitScratch _validatedRasterBlitScratch = new();
        private readonly CyberGraphicsRtgBlitScratch
            _validatedRasterRtgBlitScratch = new();
        private readonly GraphicsRastPortAttributeOperations.GetScratch
            _validatedRasterGetAttributesScratch = new();
        private readonly CopperStartGraphicsAllocator _validatedRasterGraphicsAllocator;
        private GraphicsValidatedLayerEndpoint _validatedPrimaryEndpoint;
        private GraphicsValidatedLayerEndpoint _validatedSecondaryEndpoint;

        /// <summary>
        /// Scoped DrawBounds provider for GetRPAttrsA. It consumes the exact
        /// endpoint already admitted by LayersRasterCore and never asks the
        /// legacy Layers bridge to rediscover RP.Layer/topology.
        /// </summary>
        private sealed class ValidatedLayerDrawBoundsBackend :
            IGraphicsLayerRasterBackend
        {
            private readonly AmigaBootController _owner;

            internal ValidatedLayerDrawBoundsBackend(AmigaBootController owner)
                => _owner = owner;

            public bool TryDraw(uint rastPortAddress, short x, short y)
            {
                _ = rastPortAddress;
                _ = x;
                _ = y;
                return false;
            }

            public bool TryText(
                uint rastPortAddress,
                uint textAddress,
                short length)
            {
                _ = rastPortAddress;
                _ = textAddress;
                _ = length;
                return false;
            }

            public bool TryGetDrawBounds(
                uint rastPortAddress,
                uint rectangleAddress)
            {
                var endpoint = _owner._validatedPrimaryEndpoint;
                var memory = new LayersGuestMemory(_owner);
                var layer = APTR.FromPointer(endpoint.Layer);
                var rectangle = APTR.FromPointer(rectangleAddress);
                if (!_owner._validatedRasterScopeActive ||
                    endpoint.RastPort != rastPortAddress ||
                    endpoint.Layer == 0 || rectangleAddress == 0 ||
                    !LayersRectangleCodec.IsMapped(ref memory, rectangle) ||
                    !LayersLayerCodec.TryReadBounds(
                        ref memory,
                        layer,
                        out var bounds))
                {
                    return false;
                }

                LayersRectangleCodec.Write(ref memory, rectangle, bounds);
                return true;
            }
        }

        bool IGraphicsValidatedLayerRasterBackend.TryExecuteLayeredRaster(
            short graphicsLvo,
            ref PortableLayers.LayersRegisterFrame registers,
            GraphicsValidatedLayerEndpoint primary,
            GraphicsValidatedLayerEndpoint secondary)
        {
            if (_validatedRasterScopeActive ||
                !TryValidateRasterEndpoints(
                    graphicsLvo,
                    registers,
                    primary,
                    secondary))
            {
                return false;
            }

            var state = _validatedRasterState;
            LoadRasterState(state, registers);
            _validatedPrimaryEndpoint = primary;
            _validatedSecondaryEndpoint = secondary;
            _validatedRasterScopeActive = true;
            try
            {
                if (!TryExecuteValidatedRaster((GraphicsLvo)graphicsLvo, state))
                    return false;

                ApplyRasterState(state, ref registers);
                return true;
            }
            finally
            {
                _validatedRasterScopeActive = false;
                _validatedPrimaryEndpoint = default;
                _validatedSecondaryEndpoint = default;
            }
        }

        private bool TryValidateRasterEndpoints(
            short graphicsLvo,
            PortableLayers.LayersRegisterFrame registers,
            GraphicsValidatedLayerEndpoint primary,
            GraphicsValidatedLayerEndpoint secondary)
        {
            if (!PortableLayers.LayersRasterCore.IsSupported(graphicsLvo) ||
                !TryValidateRasterEndpoint(primary) ||
                (secondary.RastPort == 0
                    ? secondary.Layer != 0 || secondary.FirstClipRect != 0 ||
                        secondary.FirstSuperClipRect != 0
                    : !TryValidateRasterEndpoint(secondary)))
            {
                return false;
            }

            var lvo = (GraphicsLvo)graphicsLvo;
            if (lvo == GraphicsLvo.ClipBlit)
            {
                if (secondary.RastPort != 0)
                {
                    return primary.RastPort == registers.A1 &&
                        secondary.RastPort == registers.A0 &&
                        registers.A0 != registers.A1;
                }

                return registers.A0 == registers.A1
                    ? primary.RastPort == registers.A0
                    : primary.RastPort == registers.A0 ||
                        primary.RastPort == registers.A1;
            }

            if (secondary.RastPort != 0)
                return false;

            return lvo is GraphicsLvo.ReadPixelLine8 or
                    GraphicsLvo.WritePixelLine8 or
                    GraphicsLvo.ReadPixelArray8 or
                    GraphicsLvo.WritePixelArray8 or
                    GraphicsLvo.WriteChunkyPixels
                ? primary.RastPort == registers.A0
                : primary.RastPort == registers.A1;
        }

        private bool TryValidateRasterEndpoint(
            GraphicsValidatedLayerEndpoint endpoint)
        {
            var memory = new LayersGuestMemory(this);
            var rastPort = APTR.FromPointer(endpoint.RastPort);
            var layer = APTR.FromPointer(endpoint.Layer);
            if (endpoint.RastPort == 0 || endpoint.Layer == 0 ||
                (endpoint.RastPort & 1) != 0 || (endpoint.Layer & 1) != 0 ||
                !LayersRastPortCodec.IsMapped(ref memory, rastPort) ||
                !memory.IsMapped(layer, LayersLayerCodec.Size) ||
                LayersRastPortCodec.ReadLayer(ref memory, rastPort) != layer ||
                LayersLayerCodec.ReadRastPort(ref memory, layer) != rastPort)
            {
                return false;
            }

            if (endpoint.FirstSuperClipRect != 0)
            {
                var superBitMap = LayersLayerCodec.ReadSuperBitMap(
                    ref memory,
                    layer).Raw;
                if (!TryGetValidatedBitMapExtent(
                        superBitMap,
                        out _,
                        out _,
                        out _))
                {
                    return false;
                }
            }

            return (endpoint.FirstClipRect == 0 ||
                    LayersClipRectCodec.IsMapped(
                        ref memory,
                        APTR.FromPointer(endpoint.FirstClipRect))) &&
                (endpoint.FirstSuperClipRect == 0 ||
                    LayersClipRectCodec.IsMapped(
                        ref memory,
                        APTR.FromPointer(endpoint.FirstSuperClipRect)));
        }

        private bool TryExecuteValidatedRaster(
            GraphicsLvo lvo,
            M68kCpuState state)
        {
            PooledValueBuffer<RasterClipFragment> projectedFragments = default;
            var projected = UsesValidatedRasterProjection(lvo);
            if (projected && !TryBeginValidatedRasterProjection(
                    state.A[1],
                    out projectedFragments))
            {
                return false;
            }

            try
            {
                switch (lvo)
                {
                    case GraphicsLvo.Draw:
                        return TryDrawLayer(state);
                    case GraphicsLvo.Text:
                        if (!TryTextLayer(state))
                            return false;

                        state.D[0] = 0;
                        return true;
                    case GraphicsLvo.SetRast:
                        return TrySetRastLayer(state);
                    case GraphicsLvo.RectFill:
                        return TryRectFillLayer(state);
                    case GraphicsLvo.ReadPixel:
                        if (!TryReadLayerPixel(
                            state.A[1],
                            Long(state.D[0]),
                            Long(state.D[1]),
                            out var color,
                            out var visible))
                    {
                        return false;
                    }

                    state.D[0] = visible ? unchecked((uint)color) : uint.MaxValue;
                        return true;
                    case GraphicsLvo.WritePixel:
                        if (!TryWriteLayerPixel(
                            state.A[1],
                            Long(state.D[0]),
                            Long(state.D[1]),
                            ReadRastPortFgPen(state.A[1]),
                            out _))
                    {
                        return false;
                    }

                    state.D[0] = 0;
                        return true;
                    case GraphicsLvo.DrawEllipse:
                        return TryDrawLayerEllipse(state);
                    case GraphicsLvo.PolyDraw:
                        return TryDrawLayerPolyLine(state);
                    case GraphicsLvo.ClearEOL:
                        return TryClearLayerText(state, clearScreen: false);
                    case GraphicsLvo.ClearScreen:
                        return TryClearLayerText(state, clearScreen: true);
                    case GraphicsLvo.EraseRect:
                        return TryEraseRectLayer(state);
                    case GraphicsLvo.ScrollRaster:
                        return TryScrollLayer(state, useBackgroundPen: true);
                    case GraphicsLvo.ScrollRasterBF:
                        return TryScrollLayer(state, useBackgroundPen: false);
                    case GraphicsLvo.AreaMove:
                    case GraphicsLvo.AreaDraw:
                    case GraphicsLvo.AreaEllipse:
                        return TryCollectLayerArea(lvo, state);
                    case GraphicsLvo.AreaEnd:
                        return TryEndLayerArea(state);
                    case GraphicsLvo.Flood:
                        return TryFloodLayer(state);
                    case GraphicsLvo.BltPattern:
                        return TryBltPatternLayer(state);
                    case GraphicsLvo.BltTemplate:
                        return TryBltTemplateLayer(state);
                    case GraphicsLvo.GetRPAttrsA:
                        return TryGetLayerRastPortAttributes(state);
                    case GraphicsLvo.ReadPixelLine8:
                    case GraphicsLvo.WritePixelLine8:
                    case GraphicsLvo.ReadPixelArray8:
                    case GraphicsLvo.WritePixelArray8:
                        return TryExecuteLayerPixelArray(lvo, state);
                    case GraphicsLvo.WriteChunkyPixels:
                        return TryWriteLayerChunkyPixels(state);
                    case GraphicsLvo.ClipBlit:
                    {
                        var pixels = BlitRastPortToRastPortClipped(
                        state,
                        state.A[0],
                        state.A[1]);
                        if (pixels < 0)
                            return false;
                        state.D[0] = pixels == 0 ? 0u : 1u;
                        return true;
                    }
                    case GraphicsLvo.BltBitMapRastPort:
                    {
                        var pixels = BlitBitMapToRastPortClipped(
                        state,
                        state.A[0],
                        state.A[1]);
                        if (pixels < 0)
                            return false;
                        state.D[0] = pixels == 0 ? 0u : 1u;
                        return true;
                    }
                    case GraphicsLvo.BltMaskBitMapRastPort:
                    {
                        // CyberGraphX owns every RTG endpoint.  Preserve its
                        // transactional provider path; a standard planar
                        // layer can instead reuse the portable masked-
                        // minterm operation against the projected layer-
                        // local BitMap.
                        if (HasRtgRasterEndpoint(state.A[0], state.A[1]))
                        {
                            var rtgPixels = BlitBitMapToRastPortClipped(
                                state,
                                state.A[0],
                                state.A[1],
                                state.A[2]);
                            if (rtgPixels < 0)
                                return false;
                            state.D[0] = rtgPixels == 0 ? 0u : 1u;
                            return true;
                        }

                        // The portable masked blitter receives virtual
                        // layer-local coordinates just like the other
                        // projected drawing primitives. Its source BitMap is
                        // caller-owned, but its RastPort destination must be
                        // translated through the selected Layer scroll before
                        // the virtual clipped surface is addressed.
                        if (!TryProjectLayerPoint(
                                state.D[2],
                                state.D[3],
                                out var destinationX,
                                out var destinationY))
                        {
                            return false;
                        }

                        var result = GraphicsBlitOperations.BltMaskBitMapRastPort(
                            _validatedLayerRasterMemory,
                            state.A[0],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            state.A[1],
                            destinationX,
                            destinationY,
                            unchecked((short)state.D[4]),
                            unchecked((short)state.D[5]),
                            (byte)state.D[6],
                            state.A[2]);
                        if (result == GraphicsRasterOperations.Failure)
                            return false;

                        state.D[0] = 0;
                        return true;
                    }
                    default:
                        return false;
                }
            }
            finally
            {
                if (projected)
                {
                    _validatedLayerRasterMemory.Reset();
                    _validatedVisibilityFragments = default;
                    projectedFragments.Dispose();
                }
            }
        }

        private static bool UsesValidatedRasterProjection(GraphicsLvo lvo)
            => lvo is GraphicsLvo.BltTemplate or GraphicsLvo.ClearEOL or
                GraphicsLvo.ClearScreen or GraphicsLvo.Text or
                GraphicsLvo.DrawEllipse or GraphicsLvo.SetRast or
                GraphicsLvo.Draw or GraphicsLvo.AreaEnd or
                GraphicsLvo.RectFill or GraphicsLvo.BltPattern or
                GraphicsLvo.Flood or GraphicsLvo.PolyDraw or
                GraphicsLvo.ScrollRaster or GraphicsLvo.EraseRect or
                GraphicsLvo.ScrollRasterBF or
                GraphicsLvo.BltMaskBitMapRastPort;

        private bool TryBeginValidatedRasterProjection(
            uint rastPort,
            out PooledValueBuffer<RasterClipFragment> fragments)
        {
            fragments = default;
            int width;
            int height;
            int depth;
            var superDomain =
                _validatedPrimaryEndpoint.FirstSuperClipRect != 0;
            if (superDomain)
            {
                var memory = new LayersGuestMemory(this);
                var superBitMap = LayersLayerCodec.ReadSuperBitMap(
                    ref memory,
                    APTR.FromPointer(_validatedPrimaryEndpoint.Layer)).Raw;
                if (!TryGetValidatedBitMapExtent(
                        superBitMap,
                        out width,
                        out height,
                        out depth))
                {
                    return false;
                }
            }
            else if (!TryGetRastPortExtent(
                         rastPort,
                         out width,
                         out height) ||
                     !TryGetValidatedRasterDepth(rastPort, out depth))
            {
                return false;
            }

            var layer = APTR.FromPointer(_validatedPrimaryEndpoint.Layer);
            var layerMemory = new LayersGuestMemory(this);
            var scrollX = LayersLayerCodec.ReadScrollX(ref layerMemory, layer);
            var scrollY = LayersLayerCodec.ReadScrollY(ref layerMemory, layer);

            var logicalOriginX = superDomain ? 0 : scrollX;
            var logicalOriginY = superDomain ? 0 : scrollY;
            fragments = GetRastPortClipFragments(
                rastPort,
                logicalOriginX,
                logicalOriginY,
                checked(logicalOriginX + width - 1),
                checked(logicalOriginY + height - 1));
            _validatedVisibilityFragments = fragments;
            if (_validatedLayerRasterMemory.Configure(
                    rastPort,
                    width,
                    height,
                    depth))
            {
                return true;
            }

            _validatedVisibilityFragments = default;
            fragments.Dispose();
            fragments = default;
            return false;
        }

        private bool TryGetValidatedRasterDepth(uint rastPort, out int depth)
        {
            depth = 0;
            if (!TryGetRastPortBitMap(rastPort, out var bitMap))
                return false;
            if (_cyberGraphics is { } cyberGraphics &&
                cyberGraphics.TryGetBitMapSurface(bitMap, out var surface))
            {
                depth = Math.Clamp(surface.Depth, 1, 8);
                return true;
            }
            var memory = new LayersGuestMemory(this);
            var bitMapAddress = APTR.FromPointer(bitMap);
            if (!LayersBitMapCodec.IsMapped(ref memory, bitMapAddress))
            {
                return false;
            }
            depth = LayersBitMapCodec.ReadDepth(ref memory, bitMapAddress);
            return depth is >= 1 and <= 8;
        }

        private bool TryGetValidatedBitMapExtent(
            uint bitMap,
            out int width,
            out int height,
            out int depth)
        {
            width = 0;
            height = 0;
            depth = 0;
            if (_cyberGraphics is { } cyberGraphics &&
                cyberGraphics.TryGetBitMapSurface(bitMap, out var surface))
            {
                width = surface.Width;
                height = surface.Height;
                depth = Math.Clamp(surface.Depth, 1, 8);
                return width > 0 && height > 0;
            }
            var memory = new LayersGuestMemory(this);
            var bitMapAddress = APTR.FromPointer(bitMap);
            if (!LayersBitMapCodec.IsMapped(ref memory, bitMapAddress))
            {
                return false;
            }

            var bytesPerRow = LayersBitMapCodec.ReadBytesPerRow(
                ref memory,
                bitMapAddress);
            height = LayersBitMapCodec.ReadRows(ref memory, bitMapAddress);
            depth = LayersBitMapCodec.ReadDepth(ref memory, bitMapAddress);
            width = bytesPerRow * 8;
            return width > 0 && height > 0 && depth is >= 1 and <= 8;
        }

        private bool IsValidatedRasterPointVisible(int x, int y)
        {
            // The projected portable operations receive coordinates relative
            // to the selected layer domain.  Clip fragments retain the
            // original RastPort logical coordinates, so restore the domain
            // origin before matching the provider-owned visibility envelope.
            var logicalX = (long)x + _validatedLayerRasterMemory.LogicalOriginX;
            var logicalY = (long)y + _validatedLayerRasterMemory.LogicalOriginY;
            foreach (var fragment in _validatedVisibilityFragments)
            {
                if (logicalX >= fragment.RequestLeft &&
                    logicalX <= fragment.RequestRight &&
                    logicalY >= fragment.RequestTop &&
                    logicalY <= fragment.RequestBottom)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryTextLayer(M68kCpuState state)
        {
            var result = GraphicsTextOperations.Text(
                _validatedLayerRasterMemory,
                state.A[1],
                state.A[0],
                state.D[0],
                _syntheticFontBackend,
                pixelVisible: _validatedRasterVisibility,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterVisibilityScratch);
            return result != GraphicsRasterOperations.Failure;
        }

        private bool TryDrawLayer(M68kCpuState state)
        {
            if (!TryProjectLayerPoint(
                    state.D[0],
                    state.D[1],
                    out var destinationX,
                    out var destinationY))
            {
                return false;
            }
            var result = GraphicsRasterOperations.Draw(
                _validatedLayerRasterMemory,
                state.A[1],
                destinationX,
                destinationY,
                pixelVisible: _validatedRasterVisibility,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryRectFillLayer(M68kCpuState state)
        {
            if (!TryProjectLayerRectangle(
                    state.D[0], state.D[1], state.D[2], state.D[3],
                    out var x0, out var y0, out var x1, out var y1))
            {
                return false;
            }
            var result = GraphicsRasterOperations.RectFill(
                _validatedLayerRasterMemory,
                state.A[1],
                x0,
                y0,
                x1,
                y1,
                _validatedRasterVisibility,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
            {
                return false;
            }

            state.D[0] = 0;
            return true;
        }

        private bool TrySetRastLayer(M68kCpuState state)
        {
            var result = GraphicsRasterOperations.SetRast(
                _validatedLayerRasterMemory,
                state.A[1],
                unchecked((byte)state.D[0]),
                _validatedRasterVisibility,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryEraseRectLayer(M68kCpuState state)
        {
            if (!TryProjectLayerRectangle(
                    state.D[0], state.D[1], state.D[2], state.D[3],
                    out var x0, out var y0, out var x1, out var y1))
            {
                return false;
            }
            var result = GraphicsRasterOperations.EraseRect(
                _validatedLayerRasterMemory,
                state.A[1],
                x0,
                y0,
                x1,
                y1,
                _validatedRasterVisibility,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterNestedSnapshotAddresses,
                _validatedRasterNestedSnapshotValues,
                _validatedRasterMintermScratch,
                _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryScrollLayer(M68kCpuState state, bool useBackgroundPen)
        {
            if (!TryProjectLayerRectangle(
                    state.D[2], state.D[3], state.D[4], state.D[5],
                    out var x0, out var y0, out var x1, out var y1))
            {
                return false;
            }
            var memory = _validatedLayerRasterMemory;
            var result = useBackgroundPen
                ? GraphicsRasterOperations.ScrollRaster(
                        memory,
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        x0,
                        y0,
                        x1,
                        y1,
                        _validatedRasterVisibility,
                        _validatedRasterScrollPixels,
                        _validatedRasterSnapshotAddresses,
                        _validatedRasterSnapshotValues,
                        _validatedRasterNestedSnapshotAddresses,
                        _validatedRasterNestedSnapshotValues,
                        _validatedRasterTertiarySnapshotAddresses,
                        _validatedRasterTertiarySnapshotValues,
                        _validatedRasterMintermScratch,
                        _validatedRasterVisibilityScratch,
                        _validatedRasterNestedVisibilityScratch,
                        _validatedRasterNestedMintermScratch)
                : GraphicsRasterOperations.ScrollRasterBF(
                        memory,
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        x0,
                        y0,
                        x1,
                        y1,
                        _validatedRasterVisibility,
                        _validatedRasterScrollPixels,
                        _validatedRasterSnapshotAddresses,
                        _validatedRasterSnapshotValues,
                        _validatedRasterNestedSnapshotAddresses,
                        _validatedRasterNestedSnapshotValues,
                        _validatedRasterTertiarySnapshotAddresses,
                        _validatedRasterTertiarySnapshotValues,
                        _validatedRasterMintermScratch,
                        _validatedRasterVisibilityScratch,
                        _validatedRasterNestedVisibilityScratch,
                        _validatedRasterNestedMintermScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryCollectLayerArea(GraphicsLvo lvo, M68kCpuState state)
        {
            var memory = _portableGraphicsMemory;
            var result = lvo switch
            {
                GraphicsLvo.AreaMove => GraphicsRasterOperations.AreaMove(
                    memory,
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    _validatedRasterAreaScratch,
                    allowLayered: true),
                GraphicsLvo.AreaDraw => GraphicsRasterOperations.AreaDraw(
                    memory,
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    _validatedRasterAreaScratch,
                    allowLayered: true),
                GraphicsLvo.AreaEllipse => GraphicsRasterOperations.AreaEllipse(
                    memory,
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    _validatedRasterAreaScratch,
                    allowLayered: true),
                _ => GraphicsRasterOperations.Failure
            };
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryEndLayerArea(M68kCpuState state)
        {
            var result = GraphicsRasterOperations.AreaEnd(
                _validatedLayerRasterMemory,
                _validatedRasterGraphicsAllocator,
                state.A[1],
                _validatedRasterAreaScratch,
                allowLayered: true,
                pixelVisible: _validatedRasterVisibility);
            if (result == GraphicsRasterOperations.Failure)
                return false;
            state.D[0] = 0;
            return true;
        }

        private bool TryFloodLayer(M68kCpuState state)
        {
            if (!TryProjectLayerPoint(
                    state.D[0],
                    state.D[1],
                    out var x,
                    out var y))
            {
                return false;
            }
            var result = GraphicsRasterOperations.Flood(
                _validatedLayerRasterMemory,
                _validatedRasterGraphicsAllocator,
                state.A[1],
                state.D[2],
                x,
                y,
                _validatedRasterFloodScratch,
                allowLayered: true,
                pixelVisible: _validatedRasterVisibility);
            if (result == GraphicsRasterOperations.Failure)
                return false;
            state.D[0] = unchecked((uint)result);
            return true;
        }

        private bool TryBltPatternLayer(M68kCpuState state)
        {
            if (!TryProjectLayerRectangle(
                    state.D[0], state.D[1], state.D[2], state.D[3],
                    out var x0, out var y0, out var x1, out var y1))
            {
                return false;
            }
            var result = GraphicsRasterOperations.BltPattern(
                _validatedLayerRasterMemory,
                state.A[1],
                state.A[0],
                x0,
                y0,
                x1,
                y1,
                unchecked((ushort)state.D[4]),
                _validatedRasterVisibility,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                visibilityScratch: _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;
            state.D[0] = 0;
            return true;
        }

        private bool TryBltTemplateLayer(M68kCpuState state)
        {
            if (!TryProjectLayerPoint(
                    state.D[2],
                    state.D[3],
                    out var destinationX,
                    out var destinationY))
            {
                return false;
            }
            var result = GraphicsBlitOperations.BltTemplate(
                _validatedLayerRasterMemory,
                state.A[0],
                unchecked((short)state.D[0]),
                unchecked((short)state.D[1]),
                state.A[1],
                destinationX,
                destinationY,
                unchecked((short)state.D[4]),
                unchecked((short)state.D[5]),
                _validatedRasterBlitScratch,
                pixelVisible: _validatedRasterVisibility);
            if (result == GraphicsRasterOperations.Failure)
                return false;
            state.D[0] = 0;
            return true;
        }

        private bool TryGetLayerRastPortAttributes(M68kCpuState state)
        {
            if (!GraphicsRastPortAttributeOperations.Get(
                    _portableGraphicsMemory,
                    state.A[1],
                    state.A[0],
                    _syntheticFontBackend,
                    _validatedLayerDrawBoundsBackend,
                    _validatedRasterGetAttributesScratch))
            {
                return false;
            }
            state.D[0] = 0;
            return true;
        }

        private bool TryDrawLayerPolyLine(M68kCpuState state)
        {
            _validatedLayerRasterMemory.ConfigurePolyPoints(
                state.A[0],
                unchecked((ushort)state.D[0]));
            var result = GraphicsRasterOperations.PolyDraw(
                _validatedLayerRasterMemory,
                state.A[1],
                unchecked((ushort)state.D[0]),
                state.A[0],
                _validatedRasterVisibility,
                _validatedRasterPolyPoints,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterNestedSnapshotAddresses,
                _validatedRasterNestedSnapshotValues,
                _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryDrawLayerEllipse(M68kCpuState state)
        {
            var rastPort = state.A[1];
            if (!TryProjectLayerPoint(
                    state.D[0],
                    state.D[1],
                    out var centerX,
                    out var centerY))
            {
                return false;
            }
            var result = GraphicsRasterOperations.DrawEllipse(
                _validatedLayerRasterMemory,
                rastPort,
                centerX,
                centerY,
                unchecked((short)state.D[2]),
                unchecked((short)state.D[3]),
                _validatedRasterVisibility,
                _validatedRasterEllipsePoints,
                _validatedRasterSnapshotAddresses,
                _validatedRasterSnapshotValues,
                _validatedRasterVisibilityScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryClearLayerText(M68kCpuState state, bool clearScreen)
        {
            var rastPort = state.A[1];
            var memory = _validatedLayerRasterMemory;
            var result = clearScreen
                ? GraphicsRasterOperations.ClearScreen(
                        memory,
                        rastPort,
                        _validatedRasterVisibility,
                        _validatedRasterSnapshotAddresses,
                        _validatedRasterSnapshotValues,
                        _validatedRasterNestedSnapshotAddresses,
                        _validatedRasterNestedSnapshotValues,
                        _validatedRasterVisibilityScratch,
                        _validatedRasterMintermScratch)
                : GraphicsRasterOperations.ClearEOL(
                        memory,
                        rastPort,
                        _validatedRasterVisibility,
                        _validatedRasterSnapshotAddresses,
                        _validatedRasterSnapshotValues,
                        _validatedRasterNestedSnapshotAddresses,
                        _validatedRasterNestedSnapshotValues,
                        _validatedRasterVisibilityScratch,
                        _validatedRasterMintermScratch);
            if (result == GraphicsRasterOperations.Failure)
                return false;

            state.D[0] = 0;
            return true;
        }

        private bool TryExecuteLayerPixelArray(GraphicsLvo lvo, M68kCpuState state)
        {
            var xStart = (ushort)state.D[0];
            var yStart = (ushort)state.D[1];
            uint width;
            uint height;
            if (lvo is GraphicsLvo.ReadPixelLine8 or GraphicsLvo.WritePixelLine8)
            {
                width = (ushort)state.D[2];
                height = 1;
            }
            else
            {
                var xStop = (ushort)state.D[2];
                var yStop = (ushort)state.D[3];
                if (xStop < xStart || yStop < yStart)
                    return false;
                width = (uint)xStop - xStart + 1;
                height = (uint)yStop - yStart + 1;
            }

            if (width == 0)
            {
                state.D[0] = 0;
                return true;
            }

            var stride = checked((width + 15u) & ~15u);
            var reads = lvo is GraphicsLvo.ReadPixelLine8 or GraphicsLvo.ReadPixelArray8;
            if (!reads)
            {
                if (!TryGetRastPortBitMap(state.A[0], out var bitMap) ||
                    !TryReadBitMapInfo(bitMap, out var bitMapInfo))
                {
                    return false;
                }

                var planeMask = bitMapInfo.Depth >= 8
                    ? byte.MaxValue
                    : (byte)((1 << bitMapInfo.Depth) - 1);
                if ((ReadRastPortMask(state.A[0]) & planeMask) == 0)
                {
                    // V36 treats an empty effective plane selection as a
                    // successful source-free no-op, with its zero plotted
                    // count. Geometry must already be valid, but the caller
                    // array and projected fragments are not consumed.
                    state.D[0] = 0;
                    return true;
                }
            }

            var byteCount = checked((ulong)stride * height);
            if (state.A[2] == 0 || byteCount > int.MaxValue ||
                !_machine.Bus.IsMappedMemoryRange(state.A[2], (int)byteCount) ||
                (reads && !_machine.Bus.IsWritableMemoryRange(
                    state.A[2],
                    (int)byteCount)))
            {
                return false;
            }

            var samples = new byte[checked((int)(width * height))];

            // Caller arrays may alias a projected source or destination
            // plane.  Complete the source phase before publishing either an
            // array byte or a planar pixel so traversal order cannot affect
            // the logical result.
            for (var row = 0u; row < height; row++)
            {
                for (var column = 0u; column < width; column++)
                {
                    var address = state.A[2] + checked(row * stride + column);
                    var x = checked((int)((uint)xStart + column));
                    var y = checked((int)((uint)yStart + row));
                    var index = checked((int)(row * width + column));
                    if (reads)
                    {
                        if (!TryReadLayerPixel(
                                state.A[0],
                                x,
                                y,
                                out var color,
                                out var visible))
                        {
                            return false;
                        }

                        samples[index] = visible ? (byte)color : (byte)0;
                    }
                    else
                    {
                        samples[index] = _machine.Bus.ReadByte(address);
                    }
                }
            }

            if (reads)
            {
                for (var row = 0u; row < height; row++)
                {
                    for (var column = 0u; column < width; column++)
                    {
                        var address = state.A[2] + checked(row * stride + column);
                        _machine.Bus.WriteByte(
                            address,
                            samples[checked((int)(row * width + column))],
                            0);
                    }
                }
            }
            else
            {
                // Validate every selected plane in every projected fragment
                // before the first write.  A malformed later cell must
                // decline the vector without exposing a prefix.
                for (var row = 0u; row < height; row++)
                {
                    for (var column = 0u; column < width; column++)
                    {
                        var x = checked((int)((uint)xStart + column));
                        var y = checked((int)((uint)yStart + row));
                        if (!TryPreflightLayerPixel(state.A[0], x, y))
                        {
                            return false;
                        }
                    }
                }

                for (var row = 0u; row < height; row++)
                {
                    for (var column = 0u; column < width; column++)
                    {
                        var x = checked((int)((uint)xStart + column));
                        var y = checked((int)((uint)yStart + row));
                        if (!TryWriteLayerPixel(
                                state.A[0],
                                x,
                                y,
                                samples[checked((int)(row * width + column))],
                                out _))
                        {
                            return false;
                        }
                    }
                }
            }

            // V36 returns the logical request size. Clipping only decides
            // whether a destination is sampled or written (and reads return
            // zero for hidden samples); it does not shorten a successful
            // pixel-line/array result.
            state.D[0] = checked(width * height);
            return true;
        }

        private bool TryWriteLayerChunkyPixels(M68kCpuState state)
        {
            var xStart = Long(state.D[0]);
            var yStart = Long(state.D[1]);
            var xStop = Long(state.D[2]);
            var yStop = Long(state.D[3]);
            var bytesPerRow = state.D[4];
            var width = (long)xStop - xStart + 1;
            var height = (long)yStop - yStart + 1;
            if (width <= 0 || height <= 0 || bytesPerRow < (ulong)width ||
                (ulong)bytesPerRow * (ulong)height > int.MaxValue ||
                !TryGetRastPortBitMap(state.A[0], out var bitMap) ||
                !TryReadBitMapInfo(bitMap, out var bitMapInfo))
            {
                return false;
            }

            var planeMask = bitMapInfo.Depth >= 8
                ? byte.MaxValue
                : (byte)((1 << bitMapInfo.Depth) - 1);
            if ((ReadRastPortMask(state.A[0]) & planeMask) == 0)
            {
                // The V40 zero-effective-mask case is a successful no-op
                // after geometry and RastPort admission.  It must not touch
                // the caller's source pointer or any layer surface.
                state.D[0] = 0;
                return true;
            }

            if (state.A[2] == 0 ||
                !_machine.Bus.IsMappedMemoryRange(
                    state.A[2],
                    checked((int)((ulong)bytesPerRow * (ulong)height))))
            {
                return false;
            }

            // WriteChunkyPixels permits the caller's byte array to alias a
            // projected layer surface.  Snapshot every logical source sample
            // before the first write so an early clipped write cannot change
            // a byte that a later row still needs to consume.
            var sourcePixels = new byte[checked((int)(width * height))];
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var source = state.A[2] + checked((uint)
                        ((ulong)row * bytesPerRow + (ulong)column));
                    sourcePixels[checked((int)(row * width + column))] =
                        _machine.Bus.ReadByte(source);
                }
            }

            // Source staging alone is not sufficient: inspect every
            // projected destination before publishing the first pixel so a
            // malformed later byte cannot leave a visible prefix.
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    if (!TryPreflightLayerPixel(
                            state.A[0],
                            checked(xStart + (int)column),
                            checked(yStart + (int)row)))
                    {
                        return false;
                    }
                }
            }

            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    if (!TryWriteLayerPixel(
                            state.A[0],
                            checked(xStart + (int)column),
                            checked(yStart + (int)row),
                            sourcePixels[checked((int)(row * width + column))],
                            out _))
                    {
                        return false;
                    }
                }
            }

            state.D[0] = 0;
            return true;
        }

        private bool TryReadLayerPixel(
            uint rastPort,
            int x,
            int y,
            out int color,
            out bool visible)
        {
            color = 0;
            visible = false;
            if (!TryGetRastPortBitMap(rastPort, out _))
                return false;

            using var fragments = GetRastPortClipFragments(
                rastPort,
                x,
                y,
                x,
                y);
            foreach (var fragment in fragments)
            {
                if (!TryReadBitMapInfo(fragment.BitMap, out var info))
                    return false;
                if (!TryReadHostBitMapPixel(
                        info,
                        fragment.BitMapLeft,
                        fragment.BitMapTop,
                        out color))
                {
                    return false;
                }

                visible = true;
                return true;
            }

            return true;
        }

        private bool TryPreflightLayerPixel(uint rastPort, int x, int y)
        {
            if (!TryGetRastPortBitMap(rastPort, out _))
                return false;

            using var fragments = GetRastPortClipFragments(
                rastPort, x, y, x, y);
            var writeMask = ReadRastPortMask(rastPort);
            foreach (var fragment in fragments)
            {
                if (!TryReadBitMapInfo(fragment.BitMap, out var info) ||
                    !CanWriteHostBitMapPixel(
                        info,
                        fragment.BitMapLeft,
                        fragment.BitMapTop,
                        writeMask))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryWriteLayerPixel(
            uint rastPort,
            int x,
            int y,
            int color,
            out bool visible)
        {
            visible = false;
            if (!TryPreflightLayerPixel(rastPort, x, y))
                return false;

            using var fragments = GetRastPortClipFragments(
                rastPort, x, y, x, y);
            var writeMask = ReadRastPortMask(rastPort);

            foreach (var fragment in fragments)
            {
                if (!TryReadBitMapInfo(fragment.BitMap, out var info))
                    return false;
                WriteBitMapPixel(
                    info,
                    fragment.BitMapLeft,
                    fragment.BitMapTop,
                    color,
                    writeMask);
                visible = true;
            }

            return true;
        }

        private bool CanWriteHostBitMapPixel(
            HostBitMapInfo info,
            int x,
            int y,
            byte writeMask)
        {
            if (x < 0 || y < 0 || x >= info.Width || y >= info.Height)
                return false;
            if (info.RtgSurface is not null || writeMask == 0)
                return true;

            var byteOffset = checked(y * info.RowStride + (x >> 3));
            for (var plane = 0; plane < info.Depth; plane++)
            {
                if ((writeMask & (1 << plane)) == 0)
                    continue;

                var planeAddress = info.GetPlane(plane);
                if (planeAddress == 0 ||
                    !_machine.Bus.IsMappedMemoryRange(
                        planeAddress + (uint)byteOffset,
                        1))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryReadHostBitMapPixel(
            HostBitMapInfo info,
            int x,
            int y,
            out int color)
        {
            color = 0;
            if (x < 0 || y < 0 || x >= info.Width || y >= info.Height)
                return false;

            if (info.RtgSurface is { } surface)
            {
                color = _cyberGraphics!.ReadSurfacePen(surface, x, y);
                return true;
            }

            var byteOffset = checked(y * info.BytesPerRow + (x >> 3));
            var bit = (byte)(0x80 >> (x & 7));
            for (var plane = 0; plane < info.Depth; plane++)
            {
                var planeAddress = info.GetPlane(plane);
                if (planeAddress == 0 ||
                    !_machine.Bus.IsMappedMemoryRange(
                        planeAddress + (uint)byteOffset,
                        1))
                {
                    return false;
                }

                if ((_machine.Bus.ReadByte(planeAddress + (uint)byteOffset) & bit) != 0)
                    color |= 1 << plane;
            }

            return true;
        }

        private bool HasRtgRasterEndpoint(uint sourceBitMap, uint rastPort)
        {
            if (_cyberGraphics?.IsRtgBitMap(sourceBitMap) == true)
                return true;

            using var fragments = GetRastPortClipFragments(
                rastPort,
                short.MinValue,
                short.MinValue,
                short.MaxValue,
                short.MaxValue);
            foreach (var fragment in fragments)
            {
                if (_cyberGraphics?.IsRtgBitMap(fragment.BitMap) == true)
                    return true;
            }

            return false;
        }

        private bool TryProjectLayerPoint(
            uint rawX,
            uint rawY,
            out short x,
            out short y)
        {
            x = 0;
            y = 0;
            return _validatedLayerRasterMemory.TryProjectX(
                       unchecked((short)rawX),
                       out x) &&
                _validatedLayerRasterMemory.TryProjectY(
                    unchecked((short)rawY),
                    out y);
        }

        private bool TryProjectLayerRectangle(
            uint rawX0,
            uint rawY0,
            uint rawX1,
            uint rawY1,
            out short x0,
            out short y0,
            out short x1,
            out short y1)
        {
            x0 = 0;
            y0 = 0;
            x1 = 0;
            y1 = 0;
            return TryProjectLayerPoint(rawX0, rawY0, out x0, out y0) &&
                TryProjectLayerPoint(rawX1, rawY1, out x1, out y1);
        }

        private static void LoadRasterState(
            M68kCpuState state,
            PortableLayers.LayersRegisterFrame frame)
        {
            Array.Clear(state.D);
            Array.Clear(state.A);
            state.StatusRegister = frame.StatusRegister;
            // StatusRegister may select a saved stack pointer. Raster frames
            // deliberately carry A0-A6 only, so keep the synthetic A7 null.
            state.A[7] = 0;
            state.Halted = false;
            state.Stopped = false;
            state.D[0] = frame.D0;
            state.D[1] = frame.D1;
            state.D[2] = frame.D2;
            state.D[3] = frame.D3;
            state.D[4] = frame.D4;
            state.D[5] = frame.D5;
            state.D[6] = frame.D6;
            state.D[7] = frame.D7;
            state.A[0] = frame.A0;
            state.A[1] = frame.A1;
            state.A[2] = frame.A2;
            state.A[3] = frame.A3;
            state.A[4] = frame.A4;
            state.A[5] = frame.A5;
            state.A[6] = frame.A6;
            state.ProgramCounter = frame.ProgramCounter;
        }

        private static void ApplyRasterState(
            M68kCpuState state,
            ref PortableLayers.LayersRegisterFrame frame)
        {
            frame.D0 = state.D[0];
            frame.D1 = state.D[1];
            frame.D2 = state.D[2];
            frame.D3 = state.D[3];
            frame.D4 = state.D[4];
            frame.D5 = state.D[5];
            frame.D6 = state.D[6];
            frame.D7 = state.D[7];
            frame.A0 = state.A[0];
            frame.A1 = state.A[1];
            frame.A2 = state.A[2];
            frame.A3 = state.A[3];
            frame.A4 = state.A[4];
            frame.A5 = state.A[5];
            frame.A6 = state.A[6];
            frame.ProgramCounter = state.ProgramCounter;
            frame.StatusRegister = state.StatusRegister;
        }
    }
}
