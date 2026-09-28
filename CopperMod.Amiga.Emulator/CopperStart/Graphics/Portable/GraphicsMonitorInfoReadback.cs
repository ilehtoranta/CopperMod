using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Read-only CMDB selection. Query data is not an acquired MonitorSpec.</summary>
internal static class GraphicsMonitorInfoReadback
{
    internal enum Admission { HostBackend, Invalid, Registered }

    internal readonly record struct Snapshot(uint Family, uint MonitorSpec,
        GraphicsMonitorViewPosition Current, GraphicsMonitorViewPosition Original);

    internal static Admission Read(IGraphicsMemory memory, uint graphics, uint family,
        uint byteCount, out Snapshot snapshot)
    {
        snapshot = default;
        if (graphics == 0) return Admission.HostBackend;
        if ((graphics & 1) != 0 || graphics > uint.MaxValue -
            (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u)) return Admission.Invalid;

        var hasSize = memory.TryReadWord(graphics + 0x12, out var positiveSize);
        var hasTag = memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag, out var tag);
        var hasVersion = memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, out var version);
        var fullEnvelope = hasSize && positiveSize >= GraphicsLibraryImageLayout.NativeRuntimeImageSize;
        var marked = hasTag && tag == GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag ||
            hasVersion && version == GraphicsLibraryImageLayout.NativeRuntimeDescriptorCurrentVersion ||
            fullEnvelope && (hasTag && tag != 0 || hasVersion && version != 0);
        if (!marked)
            return fullEnvelope && (!hasTag || !hasVersion) ? Admission.Invalid : Admission.HostBackend;

        if (!fullEnvelope || !hasTag || tag != GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag ||
            !hasVersion || version != GraphicsLibraryImageLayout.NativeRuntimeDescriptorCurrentVersion ||
            !memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner, out var owner) || owner != graphics ||
            !memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize, out var extent) || extent != GraphicsDisplayDatabase.NativeDatabaseSize ||
            !memory.TryReadLong(graphics + GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase, out var database) ||
            !memory.TryReadLong(graphics + GraphicsLayouts.GfxBaseDisplayInfoDataBase, out var published) || published != database ||
            database == 0 || (database & 3) != 0 || database > uint.MaxValue - ((uint)GraphicsDisplayDatabase.NativeDatabaseSize - 1))
            return Admission.Invalid;
        if (!memory.TryReadLong(database, out var magic) || magic != GraphicsDisplayDatabase.NativeDatabaseMagic ||
            !memory.TryReadLong(database + 4, out var databaseVersion) || databaseVersion != GraphicsDisplayDatabase.NativeDatabaseVersion ||
            !memory.TryReadLong(database + 8, out var size) || size != GraphicsDisplayDatabase.NativeDatabaseSize ||
            !memory.TryReadLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset, out var ntsc) || ntsc != GraphicsModeIds.NtscMonitor ||
            !memory.TryReadLong(database + (uint)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset + GraphicsDisplayDatabase.NativeMonitorPositionRecordSize), out var pal) || pal != GraphicsModeIds.PalMonitor ||
            !memory.TryReadLong(database + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, out var defaultFamily) ||
            defaultFamily is not GraphicsModeIds.NtscMonitor and not GraphicsModeIds.PalMonitor)
            return Admission.Invalid;

        if (family == 0) family = defaultFamily;
        var index = family == GraphicsModeIds.NtscMonitor ? 0u : 1u;
        var slot = database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + index * 4;
        var point = database + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            index * (uint)GraphicsDisplayDatabase.NativeMonitorPositionRecordSize + 4;
        uint monitor = 0, current = GraphicsMonitorViewPosition.BootDefault.WordPair, original = current;
        // A partial field needs its complete source value, but later fields
        // must not become dependencies. Snapshot before any overlapping writes.
        if (byteCount > 16 && !memory.TryReadLong(slot, out monitor) ||
            byteCount > 20 && !memory.TryReadLong(point, out current) ||
            byteCount > 80 && !memory.TryReadLong(point + 4, out original))
            return Admission.Invalid;
        snapshot = new(family, monitor, Point(current), Point(original));
        return Admission.Registered;
    }

    private static GraphicsMonitorViewPosition Point(uint value)
        => new(unchecked((short)(value >> 16)), unchecked((short)value));
}
