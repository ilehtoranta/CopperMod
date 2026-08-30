/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Buffers;
using System.Collections.Generic;
using Amiga;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        // One endpoint may contain the maximum public partition plus the four
        // rectangular complements of a SuperBitMap viewport.
        private const int MaximumClipRects = 4096 + 4;

        private readonly record struct RasterClipFragment(
            uint BitMap,
            int RequestLeft,
            int RequestTop,
            int RequestRight,
            int RequestBottom,
            int BitMapLeft,
            int BitMapTop)
        {
            public int Width => RequestRight - RequestLeft + 1;
            public int Height => RequestBottom - RequestTop + 1;
        }

        private readonly record struct RasterBlitOperation(
            uint SourceBitMap,
            int SourceX,
            int SourceY,
            uint DestinationBitMap,
            int DestinationX,
            int DestinationY,
            int Width,
            int Height,
            bool SourceIsRtg,
            bool DestinationIsRtg);

        private enum ValidatedValueBufferKind : byte
        {
            ClipFragments,
            BlitOperations
        }

        /// <summary>
        /// Operation-scoped value buffer. Foreign/native callers rent
        /// independent storage. The non-reentrant validated Layers scope uses
        /// one of two controller-owned maximum-size arrays so a Gen2 pool trim
        /// cannot manufacture allocations inside a measured raster dispatch.
        /// </summary>
        private struct PooledValueBuffer<T> : IDisposable where T : struct
        {
            private T[]? _items;
            private AmigaBootController? _validatedOwner;
            private int _validatedLease;
            private ValidatedValueBufferKind _validatedKind;

            internal PooledValueBuffer(
                T[] items,
                AmigaBootController validatedOwner,
                int validatedLease,
                ValidatedValueBufferKind validatedKind)
            {
                _items = items;
                _validatedOwner = validatedOwner;
                _validatedLease = validatedLease;
                _validatedKind = validatedKind;
                Count = 0;
            }

            internal int Count { readonly get; private set; }

            internal readonly T this[int index] => _items![index];

            internal readonly T[]? Items => _items;

            internal void Add(T item)
            {
                if (!TryAdd(item))
                    throw new InvalidOperationException("Raster value capacity exceeded.");
            }

            internal bool TryAdd(T item)
            {
                if (Count >= MaximumClipRects)
                    return false;
                if (_items is null)
                {
                    _items = ArrayPool<T>.Shared.Rent(8);
                }
                else if (Count == _items.Length)
                {
                    if (_validatedOwner is not null)
                        throw new InvalidOperationException(
                            "Validated raster fragment capacity exceeded.");
                    var expanded = ArrayPool<T>.Shared.Rent(
                        Math.Min(MaximumClipRects, checked(Count * 2)));
                    Array.Copy(_items, expanded, Count);
                    ArrayPool<T>.Shared.Return(_items, clearArray: false);
                    _items = expanded;
                }

                _items[Count++] = item;
                return true;
            }

            internal readonly bool Contains(T item)
            {
                var comparer = EqualityComparer<T>.Default;
                for (var index = 0; index < Count; index++)
                {
                    if (comparer.Equals(_items![index], item))
                        return true;
                }
                return false;
            }

            public readonly Enumerator GetEnumerator()
                => new(_items, Count);

            public void Dispose()
            {
                if (_validatedOwner is { } owner)
                {
                    owner.ReleaseValidatedValueBuffer(
                        _validatedKind,
                        _validatedLease);
                }
                else if (_items is { } items)
                    ArrayPool<T>.Shared.Return(items, clearArray: false);
                _items = null;
                _validatedOwner = null;
                _validatedLease = 0;
                _validatedKind = default;
                Count = 0;
            }

            public struct Enumerator
            {
                private readonly T[]? _items;
                private readonly int _count;
                private int _index;

                internal Enumerator(T[]? items, int count)
                {
                    _items = items;
                    _count = count;
                    _index = -1;
                }

                public readonly T Current => _items![_index];

                public bool MoveNext() => ++_index < _count;
            }
        }

        private RasterClipFragment[]? _validatedRasterFragmentBuffer0;
        private RasterClipFragment[]? _validatedRasterFragmentBuffer1;
        private int _validatedRasterFragmentLeaseCount;
        private RasterBlitOperation[]? _validatedRasterBlitOperationBuffer;
        private bool _validatedRasterBlitOperationBufferLeased;
        private bool _failNextValidatedRasterAfterFirstBlitForTest;
        private bool _failNextValidatedRasterAfterAllBlitsForTest;

        private PooledValueBuffer<RasterClipFragment>
            AcquireValidatedRasterFragmentBuffer()
        {
            if (!TryAcquireValidatedRasterFragmentBuffer(out var buffer))
            {
                throw new InvalidOperationException(
                    "Validated raster fragment buffers are not reentrant.");
            }

            return buffer;
        }

        private bool TryAcquireValidatedRasterFragmentBuffer(
            out PooledValueBuffer<RasterClipFragment> fragments)
        {
            fragments = default;
            var lease = _validatedRasterFragmentLeaseCount;
            var buffer = lease switch
            {
                0 => _validatedRasterFragmentBuffer0 ??=
                    new RasterClipFragment[MaximumClipRects],
                1 => _validatedRasterFragmentBuffer1 ??=
                    new RasterClipFragment[MaximumClipRects],
                _ => null
            };
            if (buffer is null)
                return false;

            // Increment only after the lease and its storage have been
            // obtained. A rejected third/reentrant acquisition must leave the
            // LIFO counter unchanged so both active leases and later calls can
            // still release/recover normally.
            _validatedRasterFragmentLeaseCount = lease + 1;
            fragments = new PooledValueBuffer<RasterClipFragment>(
                buffer,
                this,
                lease,
                ValidatedValueBufferKind.ClipFragments);
            return true;
        }

        private bool TryAcquireRasterBlitOperationBuffer(
            out PooledValueBuffer<RasterBlitOperation> operations)
        {
            operations = default;
            if (!_validatedRasterScopeActive)
                return true;
            if (_validatedRasterBlitOperationBufferLeased)
                return false;

            var buffer = _validatedRasterBlitOperationBuffer ??=
                new RasterBlitOperation[MaximumClipRects];
            _validatedRasterBlitOperationBufferLeased = true;
            operations = new PooledValueBuffer<RasterBlitOperation>(
                buffer,
                this,
                0,
                ValidatedValueBufferKind.BlitOperations);
            return true;
        }

        private void ReleaseValidatedValueBuffer(
            ValidatedValueBufferKind kind,
            int lease)
        {
            if (kind == ValidatedValueBufferKind.BlitOperations)
            {
                if (!_validatedRasterBlitOperationBufferLeased || lease != 0)
                {
                    throw new InvalidOperationException(
                        "Validated raster blit buffer lease is invalid.");
                }

                _validatedRasterBlitOperationBufferLeased = false;
                return;
            }

            ReleaseValidatedRasterFragmentBuffer(lease);
        }

        private void ReleaseValidatedRasterFragmentBuffer(int lease)
        {
            if (_validatedRasterFragmentLeaseCount != lease + 1)
                throw new InvalidOperationException(
                    "Validated raster fragment buffers must be released in LIFO order.");
            _validatedRasterFragmentLeaseCount--;
        }

        private void ResetValidatedRasterFragmentBuffers()
        {
            _validatedRasterFragmentLeaseCount = 0;
            _validatedRasterFragmentBuffer0 = null;
            _validatedRasterFragmentBuffer1 = null;
            _validatedRasterBlitOperationBufferLeased = false;
            _validatedRasterBlitOperationBuffer = null;
            _failNextValidatedRasterAfterFirstBlitForTest = false;
            _failNextValidatedRasterAfterAllBlitsForTest = false;
            _validatedVisibilityFragments = default;
            _validatedRasterSnapshotAddresses.Clear();
            _validatedRasterSnapshotValues.Clear();
            _validatedRasterNestedSnapshotAddresses.Clear();
            _validatedRasterNestedSnapshotValues.Clear();
            _validatedRasterTertiarySnapshotAddresses.Clear();
            _validatedRasterTertiarySnapshotValues.Clear();
            _validatedRasterScrollPixels.Clear();
            _validatedRasterEllipsePoints.Clear();
            _validatedRasterPolyPoints.Clear();
            _validatedRasterSnapshotAddresses.TrimExcess();
            _validatedRasterSnapshotValues.TrimExcess();
            _validatedRasterNestedSnapshotAddresses.TrimExcess();
            _validatedRasterNestedSnapshotValues.TrimExcess();
            _validatedRasterTertiarySnapshotAddresses.TrimExcess();
            _validatedRasterTertiarySnapshotValues.TrimExcess();
            _validatedRasterScrollPixels.TrimExcess();
            _validatedRasterEllipsePoints.TrimExcess();
            _validatedRasterPolyPoints.TrimExcess();
            _validatedRasterBlitScratch.Reset();
            _validatedRasterRtgBlitScratch.Reset();
            _validatedRasterFloodScratch.Reset();
            _validatedRasterAreaScratch.Reset();
            _validatedLayerRasterMemory.Reset();
        }

        internal int ValidatedRasterFragmentBufferCountForTest
            => (_validatedRasterFragmentBuffer0 is null ? 0 : 1) +
                (_validatedRasterFragmentBuffer1 is null ? 0 : 1);

        internal bool ValidatedRasterFragmentLeaseRejectionRecoversForTest()
        {
            if (_validatedRasterFragmentLeaseCount != 0 ||
                !TryAcquireValidatedRasterFragmentBuffer(out var first))
            {
                return false;
            }

            try
            {
                if (!TryAcquireValidatedRasterFragmentBuffer(out var second))
                    return false;
                try
                {
                    if (TryAcquireValidatedRasterFragmentBuffer(out var rejected))
                    {
                        rejected.Dispose();
                        return false;
                    }
                }
                finally
                {
                    second.Dispose();
                }
            }
            finally
            {
                first.Dispose();
            }

            if (_validatedRasterFragmentLeaseCount != 0 ||
                !TryAcquireValidatedRasterFragmentBuffer(out var recovered))
            {
                return false;
            }

            recovered.Dispose();
            return _validatedRasterFragmentLeaseCount == 0;
        }

        private bool TryGetRastPortExtent(uint rastPort, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!TryGetRastPortBitMap(rastPort, out var bitMap))
            {
                return false;
            }

            if (!TryResolveRastPortLayer(
                    rastPort,
                    out var layer,
                    out _,
                    out _))
            {
                return false;
            }

            var memory = new LayersGuestMemory(this);
            if (layer != 0 && LayersLayerCodec.TryReadBounds(
                    ref memory,
                    APTR.FromPointer(layer),
                    out var bounds))
            {
                width = bounds.MaxX - bounds.MinX + 1;
                height = bounds.MaxY - bounds.MinY + 1;
                return width > 0 && height > 0;
            }

            if (!TryReadBitMapInfo(bitMap, out var info))
            {
                return false;
            }

            width = info.Width;
            height = info.Height;
            return true;
        }

        private PooledValueBuffer<RasterClipFragment> GetRastPortClipFragments(
            uint rastPort,
            int left,
            int top,
            int right,
            int bottom)
        {
            var fragments = _validatedRasterScopeActive
                ? AcquireValidatedRasterFragmentBuffer()
                : new PooledValueBuffer<RasterClipFragment>();
            if (!TryGetRastPortBitMap(rastPort, out var rastPortBitMap))
            {
                return fragments;
            }

            NormalizeRectangle(ref left, ref top, ref right, ref bottom);
            if (!TryResolveRastPortLayer(
                    rastPort,
                    out var layer,
                    out var firstClipRect,
                    out var firstSuperClipRect))
            {
                return fragments;
            }

            if (layer == 0)
            {
                if (TryReadBitMapInfo(rastPortBitMap, out var info) &&
                    TryIntersect(left, top, right, bottom, 0, 0, info.Width - 1, info.Height - 1,
                        out var intersection))
                {
                    fragments.Add(new RasterClipFragment(
                        rastPortBitMap,
                        intersection.Left,
                        intersection.Top,
                        intersection.Right,
                        intersection.Bottom,
                        intersection.Left,
                        intersection.Top));
                }

                return fragments;
            }

            var memory = new LayersGuestMemory(this);
            var layerAddress = APTR.FromPointer(layer);
            if (!LayersLayerCodec.TryReadBounds(
                    ref memory,
                    layerAddress,
                    out var layerBounds))
            {
                return fragments;
            }

            var scrollX = LayersLayerCodec.ReadScrollX(ref memory, layerAddress);
            var scrollY = LayersLayerCodec.ReadScrollY(ref memory, layerAddress);
            // RastPort coordinates are logical layer coordinates. Public
            // ClipRects are in screen coordinates, so selection applies the
            // native Layer transform before intersecting the validated head.
            var globalLeft = (long)left + layerBounds.MinX - scrollX;
            var globalTop = (long)top + layerBounds.MinY - scrollY;
            var globalRight = (long)right + layerBounds.MinX - scrollX;
            var globalBottom = (long)bottom + layerBounds.MinY - scrollY;
            if (globalLeft < int.MinValue || globalTop < int.MinValue ||
                globalRight > int.MaxValue || globalBottom > int.MaxValue)
            {
                return fragments;
            }

            // The CopperStart path is scoped by LayersRasterCore and must
            // consume the exact validated head.  Only foreign/native layer
            // ownership is allowed to discover Layer.ClipRect here.
            var clipRect = firstClipRect;
            var visited = new PooledValueBuffer<uint>();
            var validatedChain = _validatedRasterScopeActive;
            try
            {
                for (var count = 0;
                    clipRect != 0 && count < MaximumClipRects &&
                    (validatedChain || !visited.Contains(clipRect));
                    count++)
                {
                    if (!validatedChain)
                        visited.Add(clipRect);
                    var clipRectAddress = APTR.FromPointer(clipRect);
                    if (!LayersClipRectCodec.IsMapped(
                            ref memory,
                            clipRectAddress))
                    {
                        break;
                    }

                    var next = LayersClipRectCodec.ReadNext(
                        ref memory,
                        clipRectAddress).Raw;
                    var clipBounds = LayersClipRectCodec.ReadBounds(
                        ref memory,
                        clipRectAddress);
                    if (TryIntersect(
                        (int)globalLeft, (int)globalTop, (int)globalRight, (int)globalBottom,
                        clipBounds.MinX, clipBounds.MinY, clipBounds.MaxX, clipBounds.MaxY,
                        out var intersection))
                    {
                        var obscuringLayer = LayersClipRectCodec.ReadObscuringLayer(
                            ref memory,
                            clipRectAddress).Raw;
                        var clipBitMap = LayersClipRectCodec.ReadBitMap(
                            ref memory,
                            clipRectAddress).Raw;
                        // A SIMPLE layer has no save bitmap for an obscured
                        // partition.  It must not be redirected to the visible
                        // display bitmap merely because ClipRect.BitMap is NULL.
                        if (obscuringLayer != 0 && clipBitMap == 0)
                        {
                            clipRect = next;
                            continue;
                        }

                        var targetBitMap = clipBitMap != 0 ? clipBitMap : rastPortBitMap;
                        var bitMapLeft = clipBitMap != 0
                            ? intersection.Left - clipBounds.MinX
                            : intersection.Left;
                        var bitMapTop = clipBitMap != 0
                            ? intersection.Top - clipBounds.MinY
                            : intersection.Top;
                        fragments.Add(new RasterClipFragment(
                            targetBitMap,
                            intersection.Left - layerBounds.MinX + scrollX,
                            intersection.Top - layerBounds.MinY + scrollY,
                            intersection.Right - layerBounds.MinX + scrollX,
                            intersection.Bottom - layerBounds.MinY + scrollY,
                            bitMapLeft,
                            bitMapTop));
                    }

                    clipRect = next;
                }
            }
            finally
            {
                visited.Dispose();
            }

            if (firstSuperClipRect == 0)
                return fragments;

            var superBitMap = LayersLayerCodec.ReadSuperBitMap(
                ref memory,
                layerAddress).Raw;
            var superClipRect = firstSuperClipRect;
            visited = new PooledValueBuffer<uint>();
            try
            {
                for (var count = 0;
                    superClipRect != 0 && count < 4 &&
                    (validatedChain || !visited.Contains(superClipRect));
                    count++)
                {
                    if (!validatedChain)
                        visited.Add(superClipRect);
                    var superClipRectAddress = APTR.FromPointer(superClipRect);
                    if (superBitMap == 0 ||
                        !LayersClipRectCodec.IsMapped(
                            ref memory,
                            superClipRectAddress))
                    {
                        break;
                    }

                    var next = LayersClipRectCodec.ReadNext(
                        ref memory,
                        superClipRectAddress).Raw;
                    var superBounds = LayersClipRectCodec.ReadBounds(
                        ref memory,
                        superClipRectAddress);
                    if (LayersClipRectCodec.ReadObscuringLayer(
                            ref memory,
                            superClipRectAddress).IsNull &&
                        LayersClipRectCodec.ReadBitMap(
                            ref memory,
                            superClipRectAddress).IsNull &&
                        TryIntersect(
                            left,
                            top,
                            right,
                            bottom,
                            superBounds.MinX,
                            superBounds.MinY,
                            superBounds.MaxX,
                            superBounds.MaxY,
                            out var intersection))
                    {
                        // SuperClipRect bounds and the caller SuperBitMap both
                        // use logical/Super coordinates. Public fragments were
                        // appended first; the complement follows in exact
                        // forward-list order.
                        fragments.Add(new RasterClipFragment(
                            superBitMap,
                            intersection.Left,
                            intersection.Top,
                            intersection.Right,
                            intersection.Bottom,
                            intersection.Left,
                            intersection.Top));
                    }
                    superClipRect = next;
                }
            }
            finally
            {
                visited.Dispose();
            }

            return fragments;
        }

        bool ICyberGraphicsGuestServices.TryGetRastPortClipFragments(
            uint rastPortAddress,
            int x,
            int y,
            int width,
            int height,
            out IReadOnlyList<CyberGraphicsClipFragment> fragments)
        {
            fragments = Array.Empty<CyberGraphicsClipFragment>();
            // This escaping IReadOnlyList belongs to the foreign/native CGX
            // ProcessPixelArray contract. CopperStart-owned layered RPs are
            // rejected by TryResolveRastPortLayer unless LayersRasterCore has
            // opened its endpoint-exact validated scope; admitted graphics
            // LVOs consume PooledValueBuffer directly and never reach this
            // materializing interface boundary.
            if (!TryGetRastPortBitMap(rastPortAddress, out _) ||
                !TryResolveRastPortLayer(
                    rastPortAddress,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            using var clipFragments = GetRastPortClipFragments(
                rastPortAddress,
                x,
                y,
                x + width - 1,
                y + height - 1);
            var result = new List<CyberGraphicsClipFragment>(clipFragments.Count);
            foreach (var fragment in clipFragments)
            {
                result.Add(new CyberGraphicsClipFragment(
                    fragment.BitMap,
                    fragment.RequestLeft,
                    fragment.RequestTop,
                    fragment.BitMapLeft,
                    fragment.BitMapTop,
                    fragment.Width,
                    fragment.Height));
            }

            fragments = result;
            return true;
        }

        private bool TryResolveRastPortLayer(
            uint rastPort,
            out uint layer,
            out uint firstClipRect,
            out uint firstSuperClipRect)
        {
            layer = 0;
            firstClipRect = 0;
            firstSuperClipRect = 0;
            if (_validatedRasterScopeActive)
            {
                if (rastPort == _validatedPrimaryEndpoint.RastPort)
                {
                    layer = _validatedPrimaryEndpoint.Layer;
                    firstClipRect = _validatedPrimaryEndpoint.FirstClipRect;
                    firstSuperClipRect =
                        _validatedPrimaryEndpoint.FirstSuperClipRect;
                    return layer != 0;
                }

                if (rastPort == _validatedSecondaryEndpoint.RastPort &&
                    _validatedSecondaryEndpoint.RastPort != 0)
                {
                    layer = _validatedSecondaryEndpoint.Layer;
                    firstClipRect = _validatedSecondaryEndpoint.FirstClipRect;
                    firstSuperClipRect =
                        _validatedSecondaryEndpoint.FirstSuperClipRect;
                    return layer != 0;
                }
            }

            var memory = new LayersGuestMemory(this);
            var layerAddress = LayersRastPortCodec.ReadLayer(
                ref memory,
                APTR.FromPointer(rastPort));
            layer = layerAddress.Raw;
            if (layer == 0)
                return true;

            // A CopperStart-owned layer may be traversed only after
            // LayersRasterCore has validated and supplied its published head.
            // Native Kickstart/CyberGraphX ownership retains the historical
            // discovery path outside that scoped call.
            if (_layersHostServices.IsInstalled &&
                _layersHostServices.OwnsLayeredRastPort(rastPort))
            {
                layer = 0;
                return false;
            }

            firstClipRect = LayersLayerCodec.ReadClipRect(
                ref memory,
                layerAddress).Raw;
            firstSuperClipRect = LayersLayerCodec.ReadSuperClipRect(
                ref memory,
                layerAddress).Raw;
            return true;
        }

        private void FillRastPortRect(
            uint rastPort,
            int left,
            int top,
            int right,
            int bottom,
            int color)
        {
            var writeMask = ReadRastPortMask(rastPort);
            using var fragments = GetRastPortClipFragments(
                rastPort, left, top, right, bottom);
            foreach (var fragment in fragments)
            {
                FillBitMapRect(
                    fragment.BitMap,
                    fragment.BitMapLeft,
                    fragment.BitMapTop,
                    fragment.BitMapLeft + fragment.Width - 1,
                    fragment.BitMapTop + fragment.Height - 1,
                    color,
                    writeMask);
            }
        }

        internal void FailNextValidatedRasterAfterFirstBlitForTest()
            => _failNextValidatedRasterAfterFirstBlitForTest = true;

        internal void FailNextValidatedRasterAfterAllBlitsForTest()
            => _failNextValidatedRasterAfterAllBlitsForTest = true;

        private void WriteClippedRastPortPixel(
            in PooledValueBuffer<RasterClipFragment> fragments,
            int x,
            int y,
            int color,
            byte writeMask)
        {
            foreach (var fragment in fragments)
            {
                if (x >= fragment.RequestLeft && x <= fragment.RequestRight &&
                    y >= fragment.RequestTop && y <= fragment.RequestBottom &&
                    TryReadBitMapInfo(fragment.BitMap, out var info))
                {
                    WriteBitMapPixel(
                        info,
                        fragment.BitMapLeft + x - fragment.RequestLeft,
                        fragment.BitMapTop + y - fragment.RequestTop,
                        color,
                        writeMask);
                }
            }
        }

        private void DrawRastPortLine(uint rastPort, int x0, int y0, int x1, int y1, int color)
        {
            using var fragments = GetRastPortClipFragments(
                rastPort,
                Math.Min(x0, x1),
                Math.Min(y0, y1),
                Math.Max(x0, x1),
                Math.Max(y0, y1));
            var writeMask = ReadRastPortMask(rastPort);
            var dx = Math.Abs(x1 - x0);
            var sx = x0 < x1 ? 1 : -1;
            var dy = -Math.Abs(y1 - y0);
            var sy = y0 < y1 ? 1 : -1;
            var error = dx + dy;
            while (true)
            {
                WriteClippedRastPortPixel(fragments, x0, y0, color, writeMask);
                if (x0 == x1 && y0 == y1)
                {
                    return;
                }

                var doubleError = error * 2;
                if (doubleError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (doubleError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawRastPortGlyph(
            uint rastPort,
            char character,
            int x,
            int y,
            int foreground,
            int background,
            int drawMode)
        {
            using var fragments = GetRastPortClipFragments(
                rastPort, x, y, x + 7, y + 7);
            var writeMask = ReadRastPortMask(rastPort);
            var glyph = SyntheticGlyph(character);
            for (var row = 0; row < 8; row++)
            {
                for (var column = 0; column < 8; column++)
                {
                    var set = row < 7 && column < 5 &&
                        (((glyph >> ((6 - row) * 5)) & (ulong)(0x10 >> column)) != 0);
                    if (set)
                    {
                        WriteClippedRastPortPixel(
                            fragments, x + column, y + row, foreground, writeMask);
                    }
                    else if ((drawMode & 1) != 0)
                    {
                        WriteClippedRastPortPixel(
                            fragments, x + column, y + row, background, writeMask);
                    }
                }
            }
        }

        private int BlitBitMapToRastPortClipped(
            M68kCpuState state,
            uint sourceBitMap,
            uint destinationRastPort,
            uint maskPlane = 0)
        {
            var sourceX = Long(state.D[0]);
            var sourceY = Long(state.D[1]);
            var destinationX = Long(state.D[2]);
            var destinationY = Long(state.D[3]);
            var width = Long(state.D[4]);
            var height = Long(state.D[5]);
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            var sourceIsRtg = _cyberGraphics?.IsRtgBitMap(sourceBitMap) == true;
            if (!TryAcquireRasterBlitOperationBuffer(out var operations))
                return -1;
            using var fragments = GetRastPortClipFragments(
                destinationRastPort,
                destinationX,
                destinationY,
                destinationX + width - 1,
                destinationY + height - 1);
            try
            {
                foreach (var fragment in fragments)
                {
                    var destinationIsRtg =
                        _cyberGraphics?.IsRtgBitMap(fragment.BitMap) == true;
                    var deltaX = fragment.RequestLeft - destinationX;
                    var deltaY = fragment.RequestTop - destinationY;
                    if (!operations.TryAdd(new RasterBlitOperation(
                            sourceBitMap,
                            sourceX + deltaX,
                            sourceY + deltaY,
                            fragment.BitMap,
                            fragment.BitMapLeft,
                            fragment.BitMapTop,
                            fragment.Width,
                            fragment.Height,
                            sourceIsRtg,
                            destinationIsRtg)))
                    {
                        return -1;
                    }
                }

                return ExecuteRasterBlits(
                    operations,
                    destinationRastPort,
                    (byte)state.D[6],
                    ReadRastPortMask(destinationRastPort),
                    maskPlane,
                    destinationX - sourceX,
                    destinationY - sourceY);
            }
            finally
            {
                operations.Dispose();
            }
        }

        private int BlitRastPortToRastPortClipped(
            M68kCpuState state,
            uint sourceRastPort,
            uint destinationRastPort)
        {
            var sourceX = Long(state.D[0]);
            var sourceY = Long(state.D[1]);
            var destinationX = Long(state.D[2]);
            var destinationY = Long(state.D[3]);
            var width = Long(state.D[4]);
            var height = Long(state.D[5]);
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            using var sourceFragments = GetRastPortClipFragments(
                sourceRastPort, sourceX, sourceY, sourceX + width - 1, sourceY + height - 1);
            using var destinationFragments = GetRastPortClipFragments(
                destinationRastPort,
                destinationX,
                destinationY,
                destinationX + width - 1,
                destinationY + height - 1);
            if (!TryAcquireRasterBlitOperationBuffer(out var operations))
                return -1;
            try
            {
                foreach (var source in sourceFragments)
                {
                    var sourceOffsetLeft = source.RequestLeft - sourceX;
                    var sourceOffsetTop = source.RequestTop - sourceY;
                    var sourceOffsetRight = source.RequestRight - sourceX;
                    var sourceOffsetBottom = source.RequestBottom - sourceY;
                    foreach (var destination in destinationFragments)
                    {
                        var sourceIsRtg =
                            _cyberGraphics?.IsRtgBitMap(source.BitMap) == true;
                        var destinationIsRtg =
                            _cyberGraphics?.IsRtgBitMap(destination.BitMap) == true;
                        var destinationOffsetLeft = destination.RequestLeft - destinationX;
                        var destinationOffsetTop = destination.RequestTop - destinationY;
                        var destinationOffsetRight = destination.RequestRight - destinationX;
                        var destinationOffsetBottom = destination.RequestBottom - destinationY;
                        if (!TryIntersect(
                            sourceOffsetLeft, sourceOffsetTop, sourceOffsetRight, sourceOffsetBottom,
                            destinationOffsetLeft, destinationOffsetTop,
                            destinationOffsetRight, destinationOffsetBottom,
                            out var offsets))
                        {
                            continue;
                        }

                        var clippedSourceX = source.BitMapLeft +
                            (sourceX + offsets.Left - source.RequestLeft);
                        var clippedSourceY = source.BitMapTop +
                            (sourceY + offsets.Top - source.RequestTop);
                        var clippedDestinationX = destination.BitMapLeft +
                            (destinationX + offsets.Left - destination.RequestLeft);
                        var clippedDestinationY = destination.BitMapTop +
                            (destinationY + offsets.Top - destination.RequestTop);
                        var clippedWidth = offsets.Right - offsets.Left + 1;
                        var clippedHeight = offsets.Bottom - offsets.Top + 1;
                        if (!operations.TryAdd(new RasterBlitOperation(
                                source.BitMap,
                                clippedSourceX,
                                clippedSourceY,
                                destination.BitMap,
                                clippedDestinationX,
                                clippedDestinationY,
                                clippedWidth,
                                clippedHeight,
                                sourceIsRtg,
                                destinationIsRtg)))
                        {
                            return -1;
                        }
                    }
                }

                return ExecuteRasterBlits(
                    operations,
                    destinationRastPort,
                    (byte)state.D[6],
                    ReadRastPortMask(destinationRastPort),
                    0,
                    destinationX - sourceX,
                    destinationY - sourceY);
            }
            finally
            {
                operations.Dispose();
            }
        }

        private int ExecuteRasterBlits(
            in PooledValueBuffer<RasterBlitOperation> operations,
            uint destinationRastPort,
            byte minterm,
            byte writeMask,
            uint maskPlane,
            int moveX,
            int moveY)
        {
            var items = operations.Items;
            if (items is null)
                return 0;
            // Stable in-place insertion sort avoids Comparison<T> closure
            // allocation while preserving overlap-safe copy order.
            for (var index = 1; index < operations.Count; index++)
            {
                var current = items[index];
                var insertion = index - 1;
                while (insertion >= 0 &&
                    CompareRasterBlitOperations(
                        items[insertion],
                        current,
                        moveX,
                        moveY) > 0)
                {
                    items[insertion + 1] = items[insertion];
                    insertion--;
                }
                items[insertion + 1] = current;
            }

            // A layered blit is one guest operation even when its validated
            // public/backing partitions produce several provider calls. Stage
            // exact destination snapshots before the first write; cancellation
            // then restores planar and RTG cross-products as one unit.
            var transaction = _layersHostServices.BeginValidatedRasterUndo(
                operations.Count);
            if (transaction == 0)
                return -1;
            var completed = false;
            try
            {
                for (var index = 0; index < operations.Count; index++)
                {
                    var operation = items[index];
                    if (!_layersHostServices.StageValidatedRasterUndo(
                            transaction,
                            destinationRastPort,
                            operation.DestinationBitMap,
                            operation.DestinationX,
                            operation.DestinationY,
                            operation.Width,
                            operation.Height))
                    {
                        return -1;
                    }
                }
                if (!_layersHostServices.ApplyValidatedRasterUndo(transaction))
                    return -1;

                var written = 0;
                for (var index = 0; index < operations.Count; index++)
                {
                    var operation = items[index];
                    if (!TryExecuteRasterBlit(
                        operation,
                        minterm,
                        writeMask,
                        maskPlane,
                        out var operationWritten))
                    {
                        return -1;
                    }
                    written = checked(written + operationWritten);
                    if (_failNextValidatedRasterAfterFirstBlitForTest)
                    {
                        _failNextValidatedRasterAfterFirstBlitForTest = false;
                        return -1;
                    }
                }

                if (_failNextValidatedRasterAfterAllBlitsForTest)
                {
                    _failNextValidatedRasterAfterAllBlitsForTest = false;
                    return -1;
                }

                _layersHostServices.CompleteValidatedRasterUndo(transaction);
                completed = true;
                return written;
            }
            finally
            {
                if (!completed)
                    _layersHostServices.CancelValidatedRasterUndo(transaction);
            }
        }

        private bool TryExecuteRasterBlit(
            RasterBlitOperation operation,
            byte minterm,
            byte writeMask,
            uint maskPlane,
            out int written)
        {
            written = 0;
            if (!operation.SourceIsRtg && !operation.DestinationIsRtg)
            {
                var result = ExecutePlanarRasterBlit(
                    operation,
                    minterm,
                    writeMask,
                    maskPlane);
                if (result == GraphicsRasterOperations.Failure)
                    return false;
                written = result == 0
                    ? 0
                    : checked(operation.Width * operation.Height);
                return true;
            }

            written = (operation.SourceIsRtg, operation.DestinationIsRtg) switch
            {
                (true, true) => _cyberGraphics!.BlitRtgToRtg(
                    operation.SourceBitMap,
                    operation.SourceX,
                    operation.SourceY,
                    operation.DestinationBitMap,
                    operation.DestinationX,
                    operation.DestinationY,
                    operation.Width,
                    operation.Height,
                    minterm,
                    writeMask,
                    maskPlane,
                    _validatedRasterRtgBlitScratch),
                (true, false) => _cyberGraphics!.BlitRtgToPlanar(
                    operation.SourceBitMap,
                    operation.SourceX,
                    operation.SourceY,
                    operation.DestinationBitMap,
                    operation.DestinationX,
                    operation.DestinationY,
                    operation.Width,
                    operation.Height,
                    minterm,
                    writeMask,
                    maskPlane),
                _ => _cyberGraphics!.BlitPlanarToRtg(
                    operation.SourceBitMap,
                    operation.SourceX,
                    operation.SourceY,
                    operation.DestinationBitMap,
                    operation.DestinationX,
                    operation.DestinationY,
                    operation.Width,
                    operation.Height,
                    minterm,
                    writeMask,
                    maskPlane)
            };
            return written >= 0;
        }

        private static int CompareRasterBlitOperations(
            RasterBlitOperation left,
            RasterBlitOperation right,
            int moveX,
            int moveY)
        {
            var comparison = moveY switch
            {
                > 0 => right.SourceY.CompareTo(left.SourceY),
                < 0 => left.SourceY.CompareTo(right.SourceY),
                _ => 0
            };
            if (comparison != 0)
                return comparison;

            return moveX > 0
                ? right.SourceX.CompareTo(left.SourceX)
                : left.SourceX.CompareTo(right.SourceX);
        }

        private int ExecutePlanarRasterBlit(
            RasterBlitOperation operation,
            byte minterm,
            byte writeMask,
            uint maskPlane)
        {
            // The portable planar primitive uses TempA rather than the
            // BltMaskBitMapRastPort mask plane.  Keep masked planar/planar
            // work unclaimed until it has a matching exact-fragment helper.
            if (maskPlane != 0 ||
                !TryShort(operation.SourceX, out var sourceX) ||
                !TryShort(operation.SourceY, out var sourceY) ||
                !TryShort(operation.DestinationX, out var destinationX) ||
                !TryShort(operation.DestinationY, out var destinationY) ||
                !TryShort(operation.Width, out var width) ||
                !TryShort(operation.Height, out var height))
            {
                return 0;
            }

            var result = GraphicsBlitOperations.BltBitMap(
                _portableGraphicsMemory,
                operation.SourceBitMap,
                sourceX,
                sourceY,
                operation.DestinationBitMap,
                destinationX,
                destinationY,
                width,
                height,
                minterm,
                writeMask,
                0,
                _validatedRasterGraphicsAllocator,
                _validatedRasterScopeActive ? _validatedRasterBlitScratch : null);
            return result;
        }

        private static bool TryShort(int value, out short result)
        {
            if (value < short.MinValue || value > short.MaxValue)
            {
                result = 0;
                return false;
            }

            result = (short)value;
            return true;
        }

        private static void NormalizeRectangle(ref int left, ref int top, ref int right, ref int bottom)
        {
            if (left > right)
            {
                (left, right) = (right, left);
            }

            if (top > bottom)
            {
                (top, bottom) = (bottom, top);
            }
        }

        private static bool TryIntersect(
            int leftA, int topA, int rightA, int bottomA,
            int leftB, int topB, int rightB, int bottomB,
            out (int Left, int Top, int Right, int Bottom) intersection)
        {
            intersection = (
                Math.Max(leftA, leftB),
                Math.Max(topA, topB),
                Math.Min(rightA, rightB),
                Math.Min(bottomA, bottomB));
            return intersection.Right >= intersection.Left && intersection.Bottom >= intersection.Top;
        }
    }
}
