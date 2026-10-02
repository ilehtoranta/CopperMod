using System;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guarded guest ABI handling for graphics.library QBlit/QBSBlit.
///
/// The queue itself is owned by the scheduler-facing blitter backend.  This
/// layer validates the classic <c>hardware/blit.h</c> prefix and forwards the
/// request without attempting to execute a guest function pointer on the host.
/// </summary>
internal static class GraphicsBlitterQueueOperations
{
    private const byte CleanupFlag = 0x40;

    internal static bool Queue(
        IGraphicsMemory memory,
        IGraphicsBlitterBackend blitter,
        uint nodeAddress,
        bool beamSynchronized)
        => Queue(memory, blitter, nodeAddress, beamSynchronized, cycle: null);

    internal static bool Queue(
        IGraphicsMemory memory,
        IGraphicsBlitterBackend blitter,
        uint nodeAddress,
        bool beamSynchronized,
        long cycle)
        => Queue(memory, blitter, nodeAddress, beamSynchronized, (long?)cycle);

    private static bool Queue(
        IGraphicsMemory memory,
        IGraphicsBlitterBackend blitter,
        uint nodeAddress,
        bool beamSynchronized,
        long? cycle)
    {
        if (nodeAddress == 0 ||
            !TryReadNode(memory, nodeAddress, out var functionAddress, out var status, out var beamSync, out var cleanupAddress))
        {
            return false;
        }

        // A queued node with no callable function would fault when the
        // scheduler eventually grants it.  Reject it while the caller still
        // has a well-defined, guarded gateway boundary.
        if (functionAddress == 0 ||
            (functionAddress & 1u) != 0 ||
            ((status & CleanupFlag) != 0 &&
             (cleanupAddress == 0 || (cleanupAddress & 1u) != 0)))
        {
            return false;
        }

        // The portable FIFO/CopperStart owner is the only path that publishes
        // the classic system-owned bn_Next links. Provider and legacy
        // address-only queues remain untouched so a native or CyberGraphX
        // implementation can retain ownership of the guest node envelope.
        if (blitter is IGraphicsQueuedBlitterLinkBackend linked &&
            linked.PublishesGuestLinks)
        {
            return cycle.HasValue
                ? linked.TrySubmitQueued(
                    memory,
                    nodeAddress,
                    beamSynchronized,
                    beamSync,
                    cycle.Value)
                : linked.TrySubmitQueued(
                    memory,
                    nodeAddress,
                    beamSynchronized,
                    beamSync);
        }

        if (cycle.HasValue && blitter is IGraphicsTimedBlitterBackend timedBackend)
        {
            return timedBackend.TrySubmitQueued(
                nodeAddress,
                beamSynchronized,
                beamSync,
                cycle.Value);
        }

        if (blitter is IGraphicsQueuedBlitterStatusBackend statusBackend)
        {
            return statusBackend.TrySubmitQueued(nodeAddress, beamSynchronized, beamSync);
        }

        if (blitter is IGraphicsQueuedBlitterBackend queued)
        {
            queued.SubmitQueued(nodeAddress, beamSynchronized, beamSync);
        }
        else
        {
            // Existing host adapters may only understand an operation
            // address.  They still receive the exact guest node and can parse
            // the beam field themselves if their scheduler supports it.
            blitter.Submit(nodeAddress);
        }

        return true;
    }

    private static bool TryReadNode(
        IGraphicsMemory memory,
        uint nodeAddress,
        out uint functionAddress,
        out byte status,
        out short beamSync,
        out uint cleanupAddress)
    {
        functionAddress = 0;
        status = 0;
        beamSync = 0;
        cleanupAddress = 0;

        // bltnode is intentionally kept at the packed 68k offsets used by
        // hardware/blit.h: n(0), function(4), stat(8), blitsize(9),
        // beamsync(11), cleanup(13), total prefix size 17 bytes.
        if ((nodeAddress & 1u) != 0 ||
            !TryAddress(nodeAddress, 0, GraphicsLayouts.BltNodeSize))
        {
            return false;
        }

        if (!memory.TryReadLong(nodeAddress + (uint)GraphicsLayouts.BltNodeNext, out _) ||
            !memory.TryReadLong(nodeAddress + (uint)GraphicsLayouts.BltNodeFunction, out functionAddress) ||
            !memory.TryReadByte(nodeAddress + (uint)GraphicsLayouts.BltNodeStatus, out status) ||
            !memory.TryReadWord(nodeAddress + (uint)GraphicsLayouts.BltNodeBlitSize, out _) ||
            !memory.TryReadWord(nodeAddress + (uint)GraphicsLayouts.BltNodeBeamSync, out var beamWord) ||
            !memory.TryReadLong(nodeAddress + (uint)GraphicsLayouts.BltNodeCleanup, out cleanupAddress))
        {
            return false;
        }

        beamSync = unchecked((short)beamWord);
        return true;
    }

    private static bool TryAddress(uint baseAddress, int offset, int byteCount)
        => baseAddress <= uint.MaxValue - (uint)offset &&
            baseAddress + (uint)offset <= uint.MaxValue - (uint)(byteCount - 1);
}
