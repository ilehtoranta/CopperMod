/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        object? IGraphicsTransactionalBitMapBackend.CaptureBitMap(
            uint bitMapAddress)
            => _cyberGraphics?.CaptureBitMapSnapshot(bitMapAddress);

        bool IGraphicsTransactionalBitMapBackend.RestoreBitMap(
            uint bitMapAddress,
            object snapshot)
            => snapshot is CyberGraphicsBitMapSnapshot rtgSnapshot &&
                _cyberGraphics?.RestoreBitMapSnapshot(
                    bitMapAddress,
                    rtgSnapshot) == true;

        object? IGraphicsTransactionalBitMapBackend.CaptureRectangle(
            uint bitMapAddress,
            int x,
            int y,
            int width,
            int height,
            int maximumSnapshotBytes,
            out int snapshotBytes)
        {
            var snapshot = _cyberGraphics?.CaptureBitMapRectangleSnapshot(
                bitMapAddress,
                x,
                y,
                width,
                height,
                maximumSnapshotBytes);
            snapshotBytes = snapshot?.Pixels.Length ?? 0;
            return snapshot;
        }

        bool IGraphicsTransactionalBitMapBackend.RestoreRectangle(
            uint bitMapAddress,
            object snapshot)
            => snapshot is CyberGraphicsBitMapRectangleSnapshot rtgSnapshot &&
                _cyberGraphics?.RestoreBitMapRectangleSnapshot(
                    bitMapAddress,
                rtgSnapshot) == true;

        void IGraphicsTransactionalBitMapBackend.ReleaseSnapshot(object snapshot)
        {
            if (_cyberGraphics is null)
                return;
            if (snapshot is CyberGraphicsBitMapSnapshot bitMap)
                _cyberGraphics.ReleaseBitMapTransactionSnapshot(bitMap);
            else if (snapshot is CyberGraphicsBitMapRectangleSnapshot rectangle)
                _cyberGraphics.ReleaseRectangleTransactionSnapshot(rectangle);
        }

        void IGraphicsTransactionalBitMapBackend.ResetSnapshotPool()
            => _cyberGraphics?.ResetLayersTransactionSnapshotPools();

        bool IGraphicsTransactionalBitMapBackend.CopyFromSnapshot(
            object sourceSnapshot,
            uint destinationBitMap,
            int sourceX,
            int sourceY,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            uint maskPlane)
            => sourceSnapshot is CyberGraphicsBitMapSnapshot rtgSnapshot &&
                _cyberGraphics?.CopyFromBitMapSnapshot(
                    rtgSnapshot,
                    destinationBitMap,
                    sourceX,
                    sourceY,
                    destinationX,
                    destinationY,
                    width,
                    height,
                    minterm,
                    maskPlane) == true;

        bool IGraphicsTransactionalBitMapBackend.Backfill(
            uint rastPort,
            uint destinationBitMap,
            int destinationX,
            int destinationY,
            int width,
            int height)
        {
            if (_cyberGraphics?.TryGetBitMapSurface(destinationBitMap, out _) != true ||
                !_machine.Bus.IsMappedMemoryRange(
                    rastPort,
                    checked((int)RastPort.Size)) ||
                width <= 0 || height <= 0 ||
                (long)destinationX + width - 1 > int.MaxValue ||
                (long)destinationY + height - 1 > int.MaxValue)
            {
                return false;
            }

            var backgroundPen = _machine.Bus.ReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortBgPen);
            FillBitMapRect(
                destinationBitMap,
                destinationX,
                destinationY,
                destinationX + width - 1,
                destinationY + height - 1,
                backgroundPen,
                ReadRastPortMask(rastPort));
            return true;
        }
    }
}
