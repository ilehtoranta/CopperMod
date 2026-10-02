using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-memory foundation for the classic GELS system.  This unit owns the
/// standard VSprite/GelsInfo envelope and collision-mask construction; linked
/// object ordering, callback invocation, and copper/DMA publication remain
/// explicit host/native responsibilities, while Bob background restoration is
/// coordinated by the portable animation operation.
/// </summary>
internal static class GraphicsGelsOperations
{
    internal static bool InitGels(
        IGraphicsMemory memory,
        uint head,
        uint tail,
        uint gelsInfo)
    {
        if (!ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            head == tail ||
            RangesOverlap(head, GraphicsLayouts.VSpriteSize, tail, GraphicsLayouts.VSpriteSize) ||
            RangesOverlap(head, GraphicsLayouts.VSpriteSize, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            RangesOverlap(tail, GraphicsLayouts.VSpriteSize, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler,
                out var collisionHandler))
        {
            return false;
        }

        if (collisionHandler != 0 && !ProbeRange(memory, collisionHandler, 4))
            return false;

        byte[] originalCollisionHandler = Array.Empty<byte>();
        if (!TrySnapshotRange(memory, head, GraphicsLayouts.VSpriteSize, out var originalHead) ||
            !TrySnapshotRange(memory, tail, GraphicsLayouts.VSpriteSize, out var originalTail) ||
            !TrySnapshotRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize, out var originalGelsInfo) ||
            (collisionHandler != 0 &&
             !TrySnapshotRange(memory, collisionHandler, 4, out originalCollisionHandler)))
        {
            return false;
        }

        // The two dummy VSprites are system-owned list sentinels.  Clearing
        // their standard envelopes avoids exposing stale links or positions,
        // while caller-owned GelsInfo bounds/reservations remain untouched.
        if (!ClearRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ClearRange(memory, tail, GraphicsLayouts.VSpriteSize))
        {
            RestoreRange(memory, head, originalHead);
            RestoreRange(memory, tail, originalTail);
            RestoreRange(memory, gelsInfo, originalGelsInfo);
            if (collisionHandler != 0)
                RestoreRange(memory, collisionHandler, originalCollisionHandler);
            return false;
        }

        if (memory.TryWriteLong(
                head + (uint)GraphicsLayouts.VSpriteNext,
                tail) &&
            memory.TryWriteLong(
                head + (uint)GraphicsLayouts.VSpriteClearPath,
                tail) &&
            memory.TryWriteLong(
                head + (uint)GraphicsLayouts.VSpritePrev,
                0) &&
            memory.TryWriteLong(
                tail + (uint)GraphicsLayouts.VSpriteNext,
                0) &&
            memory.TryWriteLong(
                tail + (uint)GraphicsLayouts.VSpritePrev,
                head) &&
            memory.TryWriteWord(
                head + (uint)GraphicsLayouts.VSpriteOldY,
                unchecked((ushort)short.MinValue)) &&
            memory.TryWriteWord(
                head + (uint)GraphicsLayouts.VSpriteOldX,
                unchecked((ushort)short.MinValue)) &&
            memory.TryWriteWord(
                head + (uint)GraphicsLayouts.VSpriteY,
                unchecked((ushort)short.MinValue)) &&
            memory.TryWriteWord(
                head + (uint)GraphicsLayouts.VSpriteX,
                unchecked((ushort)short.MinValue)) &&
            memory.TryWriteWord(
                tail + (uint)GraphicsLayouts.VSpriteOldY,
                unchecked((ushort)short.MaxValue)) &&
            memory.TryWriteWord(
                tail + (uint)GraphicsLayouts.VSpriteOldX,
                unchecked((ushort)short.MaxValue)) &&
            memory.TryWriteWord(
                tail + (uint)GraphicsLayouts.VSpriteY,
                unchecked((ushort)short.MaxValue)) &&
            memory.TryWriteWord(
                tail + (uint)GraphicsLayouts.VSpriteX,
                unchecked((ushort)short.MaxValue)) &&
            memory.TryWriteLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoHead,
                head) &&
            memory.TryWriteLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoTail,
                tail) &&
            memory.TryWriteByte(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoFlags,
                0) &&
            (collisionHandler == 0 || memory.TryWriteLong(collisionHandler, 0)))
        {
            return true;
        }

        RestoreRange(memory, head, originalHead);
        RestoreRange(memory, tail, originalTail);
        RestoreRange(memory, gelsInfo, originalGelsInfo);
        if (collisionHandler != 0)
            RestoreRange(memory, collisionHandler, originalCollisionHandler);
        return false;
    }

    /// <summary>
    /// Describes the guest destinations that <see cref="InitGels"/> clears or
    /// republishes.  The native overlay uses this structural pass to reject a
    /// resident/provider-owned GELS envelope before the portable transaction
    /// can clear either sentinel or rewrite the collision table entry.
    /// </summary>
    internal static bool TryGetInitGelsPublicationSpans(
        IGraphicsMemory memory,
        uint head,
        uint tail,
        uint gelsInfo,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            head == tail ||
            RangesOverlap(
                head,
                GraphicsLayouts.VSpriteSize,
                tail,
                GraphicsLayouts.VSpriteSize) ||
            RangesOverlap(
                head,
                GraphicsLayouts.VSpriteSize,
                gelsInfo,
                GraphicsLayouts.GelsInfoSize) ||
            RangesOverlap(
                tail,
                GraphicsLayouts.VSpriteSize,
                gelsInfo,
                GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler,
                out var collisionHandler))
        {
            return false;
        }

        if (collisionHandler != 0 && !ProbeRange(memory, collisionHandler, 4))
            return false;

        // InitGels clears both complete sentinel envelopes before publishing
        // their links and coordinates.  The GelsInfo itself is not cleared;
        // only these three public fields and the optional boundary routine
        // are written by this vector.
        spans.Add((head, (ulong)GraphicsLayouts.VSpriteSize));
        spans.Add((tail, (ulong)GraphicsLayouts.VSpriteSize));
        spans.Add((
            gelsInfo + (uint)GraphicsLayouts.GelsInfoFlags,
            sizeof(byte)));
        spans.Add((
            gelsInfo + (uint)GraphicsLayouts.GelsInfoHead,
            sizeof(uint)));
        spans.Add((
            gelsInfo + (uint)GraphicsLayouts.GelsInfoTail,
            sizeof(uint)));
        if (collisionHandler != 0)
            spans.Add((collisionHandler, sizeof(uint)));

        return true;
    }

    internal static bool SetCollision(
        IGraphicsMemory memory,
        uint number,
        uint routine,
        uint gelsInfo)
    {
        if (number >= GraphicsLayouts.CollisionTableEntries ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler,
                out var collisionHandler) ||
            collisionHandler == 0)
        {
            return false;
        }

        var entry = collisionHandler + (number * 4u);
        if (!ProbeRange(memory, entry, 4) ||
            !memory.TryReadLong(entry, out var original))
        {
            return false;
        }

        if (memory.TryWriteLong(entry, routine))
            return true;

        RestoreLong(memory, entry, original);
        return false;
    }

    /// <summary>
    /// Resolves the single collision-table LONG published by
    /// <see cref="SetCollision"/>.  Native-overlay admission must validate
    /// the handler table and selected entry before the portable path claims a
    /// read-only resident/provider table.
    /// </summary>
    internal static bool TryGetSetCollisionPublicationSpans(
        IGraphicsMemory memory,
        uint number,
        uint gelsInfo,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (number >= GraphicsLayouts.CollisionTableEntries ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler,
                out var collisionHandler) ||
            collisionHandler == 0)
        {
            return false;
        }

        var entry = collisionHandler + (number * 4u);
        if (!ProbeRange(memory, entry, sizeof(uint)) ||
            !memory.TryReadLong(entry, out _))
        {
            return false;
        }

        spans.Add((entry, sizeof(uint)));
        return true;
    }

    /// <summary>
    /// Builds the standard collision masks from planar image words.  A Bob's
    /// ImageShadow is accepted as the collision-mask destination when its
    /// VSprite envelope does not carry an explicit CollMask pointer.
    /// </summary>
    internal static bool InitMasks(IGraphicsMemory memory, uint sprite)
    {
        if (!ProbeRange(memory, sprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteFlags,
                out var flags) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteHeight,
                out var height) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteWidth,
                out var width) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteDepth,
                out var depth) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteImageData,
                out var imageData) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteBorderLine,
                out var borderLine) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteCollMask,
                out var collMask) ||
            height == 0 || width == 0 || depth == 0 ||
            imageData == 0 || borderLine == 0)
        {
            return false;
        }

        if ((flags & GraphicsLayouts.VSpriteFlag) == 0 &&
            (!memory.TryReadLong(
                 sprite + (uint)GraphicsLayouts.VSpriteVSBob,
                 out var bob) ||
             bob == 0 ||
             !ProbeRange(memory, bob, GraphicsLayouts.BobSize) ||
             (collMask == 0 &&
              !memory.TryReadLong(
                  bob + (uint)GraphicsLayouts.BobImageShadow,
                  out collMask))))
        {
            return false;
        }

        if (collMask == 0)
            return false;

        var planeWords = (ulong)height * width;
        var imageWords = planeWords * depth;
        var borderBytes = (ulong)width * 2ul;
        var collBytes = planeWords * 2ul;
        if (planeWords > int.MaxValue || imageWords > int.MaxValue ||
            borderBytes > uint.MaxValue || collBytes > uint.MaxValue ||
            imageWords > int.MaxValue / 2 ||
            borderBytes > int.MaxValue ||
            collBytes > int.MaxValue ||
            !ProbeRange(memory, imageData, checked((int)(imageWords * 2ul))) ||
            !ProbeRange(memory, borderLine, checked((int)borderBytes)) ||
            !ProbeRange(memory, collMask, checked((int)collBytes)))
        {
            return false;
        }

        var collisionWords = new ushort[checked((int)planeWords)];
        var borderWords = new ushort[width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                ushort combined = 0;
                for (var plane = 0; plane < depth; plane++)
                {
                    var wordIndex = ((ulong)plane * planeWords) +
                                    ((ulong)row * width) +
                                    (ulong)column;
                    if (!memory.TryReadWord(
                            imageData + checked((uint)(wordIndex * 2ul)),
                            out var word))
                    {
                        return false;
                    }

                    combined |= word;
                }

                collisionWords[(row * width) + column] = combined;
                borderWords[column] |= combined;
            }
        }

        if (!TrySnapshotRange(
                memory,
                borderLine,
                checked((int)borderBytes),
                out var originalBorder) ||
            !TrySnapshotRange(
                memory,
                collMask,
                checked((int)collBytes),
                out var originalCollision))
        {
            return false;
        }

        for (var index = 0; index < borderWords.Length; index++)
        {
            if (!memory.TryWriteWord(
                    borderLine + (uint)(index * 2),
                    borderWords[index]))
            {
                RestoreRange(memory, borderLine, originalBorder);
                RestoreRange(memory, collMask, originalCollision);
                return false;
            }
        }

        for (var index = 0; index < collisionWords.Length; index++)
        {
            if (!memory.TryWriteWord(
                    collMask + (uint)(index * 2),
                    collisionWords[index]))
            {
                RestoreRange(memory, borderLine, originalBorder);
                RestoreRange(memory, collMask, originalCollision);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the caller-owned border and collision-mask buffers written by
    /// <see cref="InitMasks"/>.  The native overlay uses this same bounded
    /// envelope before claiming a resident/provider VSprite whose masks are
    /// read-only.
    /// </summary>
    internal static bool TryGetInitMasksPublicationSpans(
        IGraphicsMemory memory,
        uint sprite,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, sprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteFlags,
                out var flags) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteHeight,
                out var height) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteWidth,
                out var width) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteDepth,
                out var depth) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteImageData,
                out var imageData) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteBorderLine,
                out var borderLine) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteCollMask,
                out var collMask) ||
            height == 0 || width == 0 || depth == 0 ||
            imageData == 0 || borderLine == 0)
        {
            return false;
        }

        if ((flags & GraphicsLayouts.VSpriteFlag) == 0)
        {
            if (!memory.TryReadLong(
                    sprite + (uint)GraphicsLayouts.VSpriteVSBob,
                    out var bob) ||
                bob == 0 ||
                !ProbeRange(memory, bob, GraphicsLayouts.BobSize))
            {
                return false;
            }

            if (collMask == 0 &&
                !memory.TryReadLong(
                    bob + (uint)GraphicsLayouts.BobImageShadow,
                    out collMask))
            {
                return false;
            }
        }

        if (collMask == 0)
            return false;

        var planeWords = (ulong)height * width;
        var imageWords = planeWords * depth;
        var borderBytes = (ulong)width * 2ul;
        var collBytes = planeWords * 2ul;
        if (planeWords > int.MaxValue || imageWords > int.MaxValue ||
            borderBytes > uint.MaxValue || collBytes > uint.MaxValue ||
            imageWords > int.MaxValue / 2 ||
            borderBytes > int.MaxValue || collBytes > int.MaxValue ||
            !ProbeRange(memory, imageData, checked((int)(imageWords * 2ul))) ||
            !ProbeRange(memory, borderLine, checked((int)borderBytes)) ||
            !ProbeRange(memory, collMask, checked((int)collBytes)))
        {
            return false;
        }

        spans.Add((borderLine, borderBytes));
        spans.Add((collMask, collBytes));
        return true;
    }

    /// <summary>
    /// Resolves every guest field that AddVSprite publishes after validating
    /// the same bounded, reciprocal GELS list walk as the portable mutation.
    /// Native-overlay callers use this read-only pass to avoid claiming a
    /// provider-owned list whose first link or flag write would be partial.
    /// </summary>
    internal static bool TryGetAddVSpritePublicationSpans(
        IGraphicsMemory memory,
        uint sprite,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !ProbeRange(memory, sprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortGelsInfo,
                out var gelsInfo) ||
            gelsInfo == 0 ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoHead,
                out var head) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoTail,
                out var tail) ||
            head == 0 || tail == 0 || head == tail ||
            sprite == head || sprite == tail ||
            !ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteNext,
                out var existingNext) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpritePrev,
                out var existingPrev) ||
            existingNext != 0 || existingPrev != 0 ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteFlags,
                out _) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteY,
                out var spriteY) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteX,
                out var spriteX))
        {
            return false;
        }

        var visited = new HashSet<uint> { head };
        var previous = head;
        var current = head;
        var found = false;
        for (var count = 0; count < 4096; count++)
        {
            if (!memory.TryReadLong(
                    current + (uint)GraphicsLayouts.VSpriteNext,
                    out var next) ||
                next == 0 ||
                !visited.Add(next))
            {
                return false;
            }

            if (next == tail)
            {
                current = next;
                found = true;
                break;
            }

            if (!ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
                !memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteY,
                    out var nextY) ||
                !memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteX,
                    out var nextX))
            {
                return false;
            }

            if (ComparePosition(nextY, nextX, spriteY, spriteX) >= 0)
            {
                current = next;
                found = true;
                break;
            }

            previous = next;
            current = next;
        }

        if (!found || current == head ||
            !ProbeRange(memory, current, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                current + (uint)GraphicsLayouts.VSpritePrev,
                out var currentPrevious) ||
            currentPrevious != previous)
        {
            return false;
        }

        spans.Add((sprite + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        spans.Add((sprite + (uint)GraphicsLayouts.VSpriteNext, sizeof(uint)));
        spans.Add((previous + (uint)GraphicsLayouts.VSpriteNext, sizeof(uint)));
        spans.Add((current + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        spans.Add((sprite + (uint)GraphicsLayouts.VSpriteFlags, sizeof(ushort)));
        return true;
    }

    /// <summary>Resolves the four reciprocal links cleared by RemVSprite.</summary>
    internal static bool TryGetRemVSpritePublicationSpans(
        IGraphicsMemory memory,
        uint sprite,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, sprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpritePrev,
                out var previous) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteNext,
                out var next) ||
            previous == 0 || next == 0 || previous == sprite || next == sprite ||
            !ProbeRange(memory, previous, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                previous + (uint)GraphicsLayouts.VSpriteNext,
                out var previousNext) ||
            !memory.TryReadLong(
                next + (uint)GraphicsLayouts.VSpritePrev,
                out var nextPrevious) ||
            previousNext != sprite || nextPrevious != sprite)
        {
            return false;
        }

        spans.Add((previous + (uint)GraphicsLayouts.VSpriteNext, sizeof(uint)));
        spans.Add((next + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        spans.Add((sprite + (uint)GraphicsLayouts.VSpriteNext, sizeof(uint)));
        spans.Add((sprite + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        return true;
    }

    /// <summary>
    /// Resolves every link rewritten by SortGList after validating the same
    /// bounded reciprocal chain and stable y/x ordering input as the portable
    /// sorter.
    /// </summary>
    internal static bool TryGetSortGListPublicationSpans(
        IGraphicsMemory memory,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortGelsInfo,
                out var gelsInfo) ||
            gelsInfo == 0 ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoHead,
                out var head) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoTail,
                out var tail) ||
            head == 0 || tail == 0 || head == tail ||
            !ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize))
        {
            return false;
        }

        var nodes = new List<(uint Address, short Y, short X, int Order)>();
        var visited = new HashSet<uint> { head };
        var current = head;
        for (var count = 0; count < 4096; count++)
        {
            if (!memory.TryReadLong(
                    current + (uint)GraphicsLayouts.VSpriteNext,
                    out var next) ||
                next == 0 ||
                !visited.Add(next) ||
                !ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
                !memory.TryReadLong(
                    next + (uint)GraphicsLayouts.VSpritePrev,
                    out var nextPrevious) ||
                nextPrevious != current)
            {
                return false;
            }

            if (next == tail)
            {
                if (!memory.TryReadLong(
                        tail + (uint)GraphicsLayouts.VSpriteNext,
                        out var tailNext) ||
                    tailNext != 0)
                {
                    return false;
                }

                current = tail;
                break;
            }

            if (!memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteY,
                    out var y) ||
                !memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteX,
                    out var x))
            {
                return false;
            }

            nodes.Add((next, (short)y, (short)x, nodes.Count));
            current = next;
        }

        if (current != tail)
            return false;

        nodes.Sort(static (left, right) =>
        {
            var y = left.Y.CompareTo(right.Y);
            if (y != 0)
                return y;

            var x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Order.CompareTo(right.Order);
        });

        var first = nodes.Count == 0 ? tail : nodes[0].Address;
        spans.Add((head + (uint)GraphicsLayouts.VSpriteNext, sizeof(uint)));
        spans.Add((first + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        for (var index = 0; index < nodes.Count; index++)
        {
            var address = nodes[index].Address;
            var next = index + 1 < nodes.Count ? nodes[index + 1].Address : tail;
            spans.Add((address + (uint)GraphicsLayouts.VSpriteNext, sizeof(uint)));
            spans.Add((next + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        }

        spans.Add((tail + (uint)GraphicsLayouts.VSpritePrev, sizeof(uint)));
        return true;
    }

    /// <summary>
    /// Inserts an unlinked true VSprite into the GelsInfo list in the
    /// documented y-then-x order.  This is list ownership only; rendering and
    /// hardware-sprite assignment remain outside the portable boundary.
    /// </summary>
    internal static bool AddVSprite(
        IGraphicsMemory memory,
        uint sprite,
        uint rastPort)
    {
        if (!ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !ProbeRange(memory, sprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortGelsInfo,
                out var gelsInfo) ||
            gelsInfo == 0 ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoHead,
                out var head) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoTail,
                out var tail) ||
            head == 0 || tail == 0 || head == tail ||
            sprite == head || sprite == tail ||
            !ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteNext,
                out var existingNext) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpritePrev,
                out var existingPrev) ||
            existingNext != 0 || existingPrev != 0 ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteFlags,
                out var originalFlags) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteY,
                out var spriteY) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.VSpriteX,
                out var spriteX))
        {
            return false;
        }

        var visited = new HashSet<uint> { head };
        var previous = head;
        var current = head;
        var found = false;
        for (var count = 0; count < 4096; count++)
        {
            if (!memory.TryReadLong(
                    current + (uint)GraphicsLayouts.VSpriteNext,
                    out var next) ||
                next == 0 ||
                !visited.Add(next))
            {
                return false;
            }

            if (next == tail)
            {
                current = next;
                found = true;
                break;
            }

            if (!ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
                !memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteY,
                    out var nextY) ||
                !memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteX,
                    out var nextX))
            {
                return false;
            }

            if (ComparePosition(nextY, nextX, spriteY, spriteX) >= 0)
            {
                current = next;
                found = true;
                break;
            }

            previous = next;
            current = next;
        }

        if (!found || current == head || !ProbeRange(memory, current, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                current + (uint)GraphicsLayouts.VSpritePrev,
                out var currentPrevious) ||
            currentPrevious != previous)
        {
            return false;
        }

        var addresses = new[]
        {
            sprite + (uint)GraphicsLayouts.VSpritePrev,
            sprite + (uint)GraphicsLayouts.VSpriteNext,
            previous + (uint)GraphicsLayouts.VSpriteNext,
            current + (uint)GraphicsLayouts.VSpritePrev,
            sprite + (uint)GraphicsLayouts.VSpriteFlags
        };
        if (!TrySnapshotLongs(memory, addresses[..4], out var originalLinks))
            return false;

        if (memory.TryWriteLong(addresses[0], previous) &&
            memory.TryWriteLong(addresses[1], current) &&
            memory.TryWriteLong(addresses[2], sprite) &&
            memory.TryWriteLong(addresses[3], sprite) &&
            memory.TryWriteWord(
                addresses[4],
                (ushort)(originalFlags & GraphicsLayouts.VSpriteUserFlags)))
        {
            return true;
        }

        RestoreLongs(memory, addresses[..4], originalLinks);
        RestoreWord(memory, addresses[4], originalFlags);
        return false;
    }

    /// <summary>Unlinks a VSprite after validating both reciprocal neighbors.</summary>
    internal static bool RemVSprite(IGraphicsMemory memory, uint sprite)
    {
        if (!ProbeRange(memory, sprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpritePrev,
                out var previous) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.VSpriteNext,
                out var next) ||
            previous == 0 || next == 0 || previous == sprite || next == sprite ||
            !ProbeRange(memory, previous, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                previous + (uint)GraphicsLayouts.VSpriteNext,
                out var previousNext) ||
            !memory.TryReadLong(
                next + (uint)GraphicsLayouts.VSpritePrev,
                out var nextPrevious) ||
            previousNext != sprite || nextPrevious != sprite)
        {
            return false;
        }

        var addresses = new[]
        {
            previous + (uint)GraphicsLayouts.VSpriteNext,
            next + (uint)GraphicsLayouts.VSpritePrev,
            sprite + (uint)GraphicsLayouts.VSpriteNext,
            sprite + (uint)GraphicsLayouts.VSpritePrev
        };
        if (!TrySnapshotLongs(memory, addresses, out var original))
            return false;

        if (memory.TryWriteLong(addresses[0], next) &&
            memory.TryWriteLong(addresses[1], previous) &&
            memory.TryWriteLong(addresses[2], 0) &&
            memory.TryWriteLong(addresses[3], 0))
        {
            return true;
        }

        RestoreLongs(memory, addresses, original);
        return false;
    }

    /// <summary>Reorders a validated GelsInfo list by signed y then x.</summary>
    internal static bool SortGList(IGraphicsMemory memory, uint rastPort)
    {
        if (!ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortGelsInfo,
                out var gelsInfo) ||
            gelsInfo == 0 ||
            !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoHead,
                out var head) ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoTail,
                out var tail) ||
            head == 0 || tail == 0 || head == tail ||
            !ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize))
        {
            return false;
        }

        var nodes = new List<(uint Address, short Y, short X, int Order)>();
        var visited = new HashSet<uint> { head };
        var current = head;
        for (var count = 0; count < 4096; count++)
        {
            if (!memory.TryReadLong(
                    current + (uint)GraphicsLayouts.VSpriteNext,
                    out var next) ||
                next == 0 ||
                !visited.Add(next) ||
                !ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
                !memory.TryReadLong(
                    next + (uint)GraphicsLayouts.VSpritePrev,
                    out var nextPrevious) ||
                nextPrevious != current)
            {
                return false;
            }

            if (next == tail)
            {
                if (!memory.TryReadLong(
                        tail + (uint)GraphicsLayouts.VSpriteNext,
                        out var tailNext) || tailNext != 0)
                {
                    return false;
                }

                current = tail;
                break;
            }

            if (!memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteY,
                    out var y) ||
                !memory.TryReadWord(
                    next + (uint)GraphicsLayouts.VSpriteX,
                    out var x))
            {
                return false;
            }

            nodes.Add((next, (short)y, (short)x, nodes.Count));
            current = next;
        }

        if (current != tail)
            return false;

        nodes.Sort(static (left, right) =>
        {
            var y = left.Y.CompareTo(right.Y);
            if (y != 0)
                return y;

            var x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Order.CompareTo(right.Order);
        });

        var first = nodes.Count == 0 ? tail : nodes[0].Address;
        var linkAddresses = new List<uint>
        {
            head + (uint)GraphicsLayouts.VSpriteNext,
            first + (uint)GraphicsLayouts.VSpritePrev
        };
        for (var index = 0; index < nodes.Count; index++)
        {
            var address = nodes[index].Address;
            var next = index + 1 < nodes.Count ? nodes[index + 1].Address : tail;
            linkAddresses.Add(address + (uint)GraphicsLayouts.VSpriteNext);
            linkAddresses.Add(next + (uint)GraphicsLayouts.VSpritePrev);
        }

        linkAddresses.Add(tail + (uint)GraphicsLayouts.VSpritePrev);
        var addresses = linkAddresses.ToArray();
        if (!TrySnapshotLongs(memory, addresses, out var original))
            return false;

        if (!memory.TryWriteLong(
                head + (uint)GraphicsLayouts.VSpriteNext,
                first) ||
            !memory.TryWriteLong(
                first + (uint)GraphicsLayouts.VSpritePrev,
                head))
        {
            RestoreLongs(memory, addresses, original);
            return false;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var address = nodes[index].Address;
            var next = index + 1 < nodes.Count ? nodes[index + 1].Address : tail;
            if (!memory.TryWriteLong(
                    address + (uint)GraphicsLayouts.VSpriteNext,
                    next) ||
                !memory.TryWriteLong(
                    next + (uint)GraphicsLayouts.VSpritePrev,
                    address))
            {
                RestoreLongs(memory, addresses, original);
                return false;
            }
        }

        if (memory.TryWriteLong(
                tail + (uint)GraphicsLayouts.VSpritePrev,
                nodes.Count == 0 ? head : nodes[^1].Address))
        {
            return true;
        }

        RestoreLongs(memory, addresses, original);
        return false;
    }

    private static int ComparePosition(
        ushort leftY,
        ushort leftX,
        ushort rightY,
        ushort rightX)
    {
        var y = ((short)leftY).CompareTo((short)rightY);
        return y != 0 ? y : ((short)leftX).CompareTo((short)rightX);
    }

    private static bool ClearRange(IGraphicsMemory memory, uint address, int byteCount)
    {
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(address + (uint)offset, 0))
                return false;
        }

        return true;
    }

    private static bool ProbeRange(IGraphicsMemory memory, uint address, int byteCount)
    {
        if (address == 0 || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        return true;
    }

    private static bool TrySnapshotRange(
        IGraphicsMemory memory,
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (!ProbeRange(memory, address, byteCount))
            return false;

        original = new byte[byteCount];
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void RestoreRange(
        IGraphicsMemory memory,
        uint address,
        byte[] original)
    {
        for (var offset = 0; offset < original.Length; offset++)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }

    private static bool TrySnapshotLongs(
        IGraphicsMemory memory,
        uint[] addresses,
        out uint[] original)
    {
        original = new uint[addresses.Length];
        for (var index = 0; index < addresses.Length; index++)
        {
            if (!memory.TryReadLong(addresses[index], out original[index]))
            {
                original = Array.Empty<uint>();
                return false;
            }
        }

        return true;
    }

    private static void RestoreLongs(
        IGraphicsMemory memory,
        uint[] addresses,
        uint[] original)
    {
        for (var index = 0; index < original.Length; index++)
            RestoreLong(memory, addresses[index], original[index]);
    }

    private static void RestoreLong(
        IGraphicsMemory memory,
        uint address,
        uint value)
    {
        _ = memory.TryWriteByte(address, (byte)(value >> 24));
        _ = memory.TryWriteByte(address + 1u, (byte)(value >> 16));
        _ = memory.TryWriteByte(address + 2u, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 3u, (byte)value);
    }

    private static void RestoreWord(
        IGraphicsMemory memory,
        uint address,
        ushort value)
    {
        _ = memory.TryWriteByte(address, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 1u, (byte)value);
    }

    private static bool RangesOverlap(
        uint first,
        int firstLength,
        uint second,
        int secondLength)
        => (ulong)first < (ulong)second + (uint)secondLength &&
           (ulong)second < (ulong)first + (uint)firstLength;
}
