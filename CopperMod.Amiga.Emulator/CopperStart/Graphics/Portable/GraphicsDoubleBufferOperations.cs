using System.Collections.Generic;

using System;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Portable ownership of the public DBufInfo envelope.  Message replies,
/// copper pointers, and beam positions remain private scheduler state; only
/// the documented guest structure and allocator contract are handled here.
/// </summary>
internal static class GraphicsDoubleBufferOperations
{
    internal sealed class Registry
    {
        private readonly HashSet<uint> _owned = new();

        internal bool Add(uint address) => _owned.Add(address);
        internal bool Remove(uint address) => _owned.Remove(address);
        internal bool Contains(uint address) => address != 0 && _owned.Contains(address);
    }

    internal static bool ValidateForChange(IGraphicsMemory memory, uint address)
    {
        if (address == 0)
            return true;

        return TryProbeRange(memory, address, GraphicsLayouts.DBufInfoSize);
    }

    internal static bool ValidateForChange(
        IGraphicsMemory memory,
        Registry registry,
        uint address)
    {
        if (address == 0)
            return true;

        // Kickstart documents DBufInfo as an opaque, allocator-owned object:
        // callers must obtain it from AllocDBufInfo and may not fabricate a
        // merely readable envelope.  Keep the range probe as a second guard
        // so stale ownership cannot claim an unmapped guest address.
        return registry.Contains(address) &&
            TryProbeRange(memory, address, GraphicsLayouts.DBufInfoSize);
    }

    internal static uint AllocDBufInfo(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint viewPort)
    {
        if (!GraphicsRasterOperations.ValidateViewPortForDoubleBuffer(memory, viewPort))
            return 0;

        var allocationSucceeded = allocator.TryAllocate(
            GraphicsLayouts.DBufInfoSize,
            GraphicsMemoryClass.Public,
            out var address);
        if (!allocationSucceeded)
        {
            // Allocators normally clear the out address on failure, but a
            // provider may return a provisional public span together with a
            // false status.  No DBufInfo state has been published yet, so
            // release that span at this boundary before declining.
            if (address != 0)
                allocator.Free(address, GraphicsLayouts.DBufInfoSize, GraphicsMemoryClass.Public);

            return 0;
        }

        if (address == 0 ||
            !TrySnapshot(memory, address, GraphicsLayouts.DBufInfoSize, out var original))
        {
            if (address != 0)
                allocator.Free(address, GraphicsLayouts.DBufInfoSize, GraphicsMemoryClass.Public);

            return 0;
        }

        if (!Clear(memory, address, GraphicsLayouts.DBufInfoSize) ||
            !memory.TryWriteWord(
                address + (uint)GraphicsLayouts.DBufInfoSafeMessage + (uint)GraphicsLayouts.ExecMessageLength,
                GraphicsLayouts.ExecMessageSize) ||
            !memory.TryWriteWord(
                address + (uint)GraphicsLayouts.DBufInfoDispMessage + (uint)GraphicsLayouts.ExecMessageLength,
                GraphicsLayouts.ExecMessageSize))
        {
            Restore(memory, address, original);
            allocator.Free(address, GraphicsLayouts.DBufInfoSize, GraphicsMemoryClass.Public);

            return 0;
        }

        if (!registry.Add(address))
        {
            // A defective allocator can reissue an address which is still
            // owned by this graphics instance.  The registry rejects that
            // duplicate before it becomes a second DBufInfo, but the first
            // owner's public envelope must remain byte-for-byte intact.
            Restore(memory, address, original);
            allocator.Free(address, GraphicsLayouts.DBufInfoSize, GraphicsMemoryClass.Public);
            return 0;
        }

        return address;
    }

    internal static int FreeDBufInfo(
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint address)
    {
        // Kickstart treats FreeDBufInfo(NULL) as an idempotent no-op.  Keep
        // that ABI-visible case claimed here, while still refusing a
        // non-zero envelope which was not allocated by this graphics-library
        // instance so native/provider ownership is not stolen.
        if (address == 0)
            return GraphicsRasterOperations.Success;

        if (!registry.Remove(address))
            return GraphicsRasterOperations.Failure;

        allocator.Free(address, GraphicsLayouts.DBufInfoSize, GraphicsMemoryClass.Public);
        return GraphicsRasterOperations.Success;
    }

    private static bool Clear(IGraphicsMemory memory, uint address, int byteCount)
    {
        if (address == 0 || (address & 1u) != 0)
            return false;

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(address + (uint)offset, 0))
                return false;
        }

        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 || (address & 1u) != 0 || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

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

    private static void Restore(
        IGraphicsMemory memory,
        uint address,
        byte[] original)
    {
        for (var offset = original.Length - 1; offset >= 0; offset--)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }

    private static bool TryProbeRange(IGraphicsMemory memory, uint address, int byteCount)
    {
        // DBufInfo contains word/long Exec message fields.  A readable odd
        // byte span is therefore not a valid 68k public envelope: keep both
        // the instance-owned and mapped guest paths aligned before a timed
        // ChangeVPBitMap call can publish the swap.
        if (address == 0 || (address & 1u) != 0 || byteCount <= 0 ||
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
}
