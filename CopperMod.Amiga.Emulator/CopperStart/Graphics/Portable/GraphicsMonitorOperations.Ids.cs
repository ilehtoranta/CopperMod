using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class GraphicsMonitorOperations
{
    internal sealed partial class Registry
    {
        // Explicit native CMDO state selects in guest memory. An absent compact
        // suffix retains the host-only backend; invalid/partial native state must
        // never turn into permission to allocate a replacement monitor.
        private bool TrySelectRegisteredId(IGraphicsMemory memory, uint displayId,
            out uint monitor, out bool absent)
        {
            monitor = 0;
            absent = false;
            if (displayId == GraphicsModeIds.Invalid)
            { absent = true; return true; } // native INVALID_ID needs no GfxBase access
            if (!GraphicsMonitorSelectors.TryGetFamily(displayId, out var family)) return true;
            var graphics = _graphicsLibraryBase;
            if (graphics == 0) return false;
            if ((graphics & 1) != 0 || graphics > uint.MaxValue -
                (GraphicsLayouts.GfxBaseDefaultMonitor + 3u)) return true;
            // A published nonnull default is independent even of unreadable
            // private runtime state. Empty compact defaults still use the backend.
            if (displayId == 0 && memory.TryReadLong(graphics + GraphicsLayouts.GfxBaseDefaultMonitor, out var defaultMonitor) &&
                defaultMonitor != 0)
            {
                if (IsRegisteredEnvelope(memory, defaultMonitor)) monitor = defaultMonitor;
                return true;
            }
            if (graphics > uint.MaxValue -
                (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u)) return true;

            var hasSize = memory.TryReadWord(graphics + 0x12, out var positiveSize);
            var hasTag = memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag, out var tag);
            var hasVersion = memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, out var version);
            var fullEnvelope = hasSize && positiveSize >= GraphicsLibraryImageLayout.NativeRuntimeImageSize;
            var marked = hasTag && tag == GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag ||
                hasVersion && version == GraphicsLibraryImageLayout.NativeRuntimeDescriptorCurrentVersion ||
                fullEnvelope && (hasTag && tag != 0 || hasVersion && version != 0);
            if (!marked)
                return fullEnvelope && (!hasTag || !hasVersion); // truncated native suffix, or compact backend

            // Exact zero is the public default pointer, independent of database
            // validity. Names are handled before entering this method.
            if (displayId == 0)
            {
                if (memory.TryReadLong(graphics + GraphicsLayouts.GfxBaseDefaultMonitor, out var selected) &&
                    IsRegisteredEnvelope(memory, selected)) monitor = selected;
                return true;
            }
            if (!fullEnvelope || !hasTag || tag != GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag ||
                !hasVersion || version != GraphicsLibraryImageLayout.NativeRuntimeDescriptorCurrentVersion ||
                !memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner, out var owner) || owner != graphics ||
                !memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize, out var extent) || extent != GraphicsDisplayDatabase.NativeDatabaseSize ||
                !memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase, out var database) ||
                !memory.TryReadLong(graphics + GraphicsLayouts.GfxBaseDisplayInfoDataBase, out var published) || published != database ||
                database == 0 || (database & 3) != 0 || database > uint.MaxValue - ((uint)GraphicsDisplayDatabase.NativeDatabaseSize - 1))
                return true;
            if (!memory.TryReadLong(database, out var magic) || magic != GraphicsDisplayDatabase.NativeDatabaseMagic ||
                !memory.TryReadLong(database + 4, out var databaseVersion) || databaseVersion != GraphicsDisplayDatabase.NativeDatabaseVersion ||
                !memory.TryReadLong(database + 8, out var size) || size != GraphicsDisplayDatabase.NativeDatabaseSize ||
                !memory.TryReadLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset, out var ntsc) || ntsc != GraphicsModeIds.NtscMonitor ||
                !memory.TryReadLong(database + (uint)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset + GraphicsDisplayDatabase.NativeMonitorPositionRecordSize), out var pal) || pal != GraphicsModeIds.PalMonitor ||
                !memory.TryReadLong(database + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, out var defaultFamily) ||
                defaultFamily is not GraphicsModeIds.NtscMonitor and not GraphicsModeIds.PalMonitor)
                return true;
            if (family == 0) family = defaultFamily;
            var slot = database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset +
                (family == GraphicsModeIds.NtscMonitor ? 0u : 4u);
            if (!memory.TryReadLong(slot, out var registered)) return true;
            if (registered == 0) absent = true;
            else if (IsRegisteredEnvelope(memory, registered)) monitor = registered;
            return true;
        }
    }
}
