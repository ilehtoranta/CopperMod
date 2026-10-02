using System;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class GraphicsMonitorOperations
{
    internal sealed partial class Registry
    {
        // A mapped public MonitorList is the name-selection authority. The
        // compact host shim (no list suffix) retains its separate PAL/NTSC
        // backend registration path. This lookup never adopts list members.
        // As with the native unit, callers must serialize list/lifetime edits.
        private bool TrySelectRegisteredName(IGraphicsMemory memory, uint name, out uint monitor, out bool absent)
        {
            monitor = 0;
            absent = false;
            if (_graphicsLibraryBase == 0)
                return false;
            if ((_graphicsLibraryBase & 1u) != 0 || _graphicsLibraryBase > uint.MaxValue -
                (uint)(GraphicsLayouts.GfxBaseDefaultMonitor + sizeof(uint) - 1))
                return true;
            // Default alias wins even over a list node with the same name.
            // Exact registered names compare raw bytes, not ASCII replacement
            // characters or locale-sensitive text, and take precedence over ID.
            if (TryReadCString(memory, name, out var alias) &&
                string.Equals(alias, "default.monitor", StringComparison.OrdinalIgnoreCase))
            {
                if (!memory.TryReadLong(_graphicsLibraryBase + GraphicsLayouts.GfxBaseDefaultMonitor,
                        out var selected))
                    return memory.TryReadLong(_graphicsLibraryBase + GraphicsLayouts.GfxBaseMonitorListHead, out _);
                if (IsRegisteredEnvelope(memory, selected))
                    monitor = selected;
                return true;
            }

            if (!TryReadMonitorList(memory, _graphicsLibraryBase, out var list,
                    out var head, out var tail, out var tailPred, out var type, out var pad))
                // Only an absent list head denotes the compact shim. A mapped
                // head with an unreadable suffix is malformed, not permission
                // to allocate through the compatibility backend.
                return memory.TryReadLong(_graphicsLibraryBase + GraphicsLayouts.GfxBaseMonitorListHead, out _);
            if (tail != 0 || type != 0 || pad != 0)
                return true;
            var previous = list;
            var current = head;
            var tailSentinel = list + 4u;
            for (var count = 0; count <= 256; count++)
            {
                if (current == tailSentinel)
                {
                    // Only a consistent, complete traversal establishes a
                    // claimed NULL result. Malformed lists retain the provider
                    // request even though the direct API also returns zero.
                    absent = tailPred == previous;
                    return true;
                }
                if (count == 256 || !IsRegisteredEnvelope(memory, current) ||
                    !memory.TryReadLong(current + 4u, out var predecessor) || predecessor != previous ||
                    !memory.TryReadLong(current + GraphicsLayouts.MonitorSpecNodeName, out var residentName) ||
                    !TryCompareRegisteredName(memory, name, residentName, out var matches))
                    return true;
                if (matches)
                {
                    monitor = current;
                    return true;
                }
                previous = current;
                if (!memory.TryReadLong(current, out current))
                    return true;
            }
            return true;
        }

        private bool IsRegisteredEnvelope(IGraphicsMemory memory, uint monitor)
            => monitor != 0 && (monitor & 1u) == 0 &&
               monitor <= uint.MaxValue - (GraphicsLayouts.MonitorSpecSize - 1u) &&
               IsAssociatedMonitor(memory, monitor);

        private static bool TryCompareRegisteredName(IGraphicsMemory memory, uint requested,
            uint resident, out bool matches)
        {
            matches = false;
            if (requested == 0 || resident == 0)
                return false;
            for (uint index = 0; index < 64; index++)
            {
                if (requested > uint.MaxValue - index || resident > uint.MaxValue - index ||
                    !memory.TryReadByte(requested + index, out var left) ||
                    !memory.TryReadByte(resident + index, out var right))
                    return false;
                if (left != right)
                    return true;
                if (left == 0)
                {
                    matches = true;
                    return true;
                }
            }
            return false;
        }

        private static uint OpenRegisteredReference(IGraphicsMemory memory, uint monitor)
        {
            var address = monitor + GraphicsLayouts.MonitorSpecOpenCount;
            if (!memory.TryReadWord(address, out var count) || count == ushort.MaxValue ||
                !TrySnapshot(memory, address, sizeof(ushort), out var original))
                return 0;
            if (memory.TryWriteWord(address, (ushort)(count + 1)))
                return monitor;
            Restore(memory, address, original);
            return 0;
        }

        internal bool TryOpen(IGraphicsMemory memory, IGraphicsAllocatorBackend allocator,
            uint name, uint displayId, out uint result)
        {
            if (name != 0 && TrySelectRegisteredName(memory, name, out var monitor, out var absent))
            {
                result = monitor == 0 ? 0 : OpenRegisteredReference(memory, monitor);
                return result != 0 || absent;
            }
            if (name == 0 && TrySelectRegisteredId(memory, displayId, out var selected, out var missing))
            {
                result = selected == 0 ? 0 : OpenRegisteredReference(memory, selected);
                return result != 0 || missing;
            }
            result = OpenBackend(memory, allocator, name, displayId);
            return result != 0;
        }
    }
}
