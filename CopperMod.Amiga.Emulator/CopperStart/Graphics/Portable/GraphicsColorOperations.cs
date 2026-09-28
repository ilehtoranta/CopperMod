using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-side ColorMap storage and palette conversion.  The public structure
/// follows graphics/view.h; the registry only tracks allocations and pen
/// ownership so malformed guest pointers cannot free unrelated memory.
/// </summary>
internal static class GraphicsColorOperations
{
    private const byte ColorMapTypeV39 = 0x02;
    private const uint MaxEntries = ushort.MaxValue;
    private const uint PenExclusive = 1u << 0;
    private const uint PenNoSetColor = 1u << 1;
    private const uint TagEnd = 0;
    private const uint TagIgnore = 1;
    private const uint TagMore = 2;
    private const uint TagSkip = 3;
    private const uint VTagChromaKeyClr = 0x8000_0000;
    private const uint VTagChromaKeySet = 0x8000_0001;
    private const uint VTagChromaKeyGet = 0x8000_0015;
    private const uint VTagBitPlaneKeyClr = 0x8000_0002;
    private const uint VTagBitPlaneKeySet = 0x8000_0003;
    private const uint VTagChromaPenClr = 0x8000_0008;
    private const uint VTagChromaPenSet = 0x8000_0009;
    private const uint VTagChromaPlaneSet = 0x8000_000A;
    private const uint VTagNextBufCm = 0x8000_000C;
    private const uint VTagBitPlaneKeyGet = 0x8000_0016;
    private const uint VTagBorderBlankClr = 0x8000_0004;
    private const uint VTagBorderBlankSet = 0x8000_0005;
    private const uint VTagBorderBlankGet = 0x8000_0017;
    private const uint VTagBorderNoTransClr = 0x8000_0006;
    private const uint VTagBorderNoTransSet = 0x8000_0007;
    private const uint VTagBorderNoTransGet = 0x8000_0018;
    private const uint VTagAttachCmSet = 0x8000_000B;
    private const uint VTagBatchCmClr = 0x8000_000D;
    private const uint VTagBatchCmSet = 0x8000_000E;
    private const uint VTagNormalDispGet = 0x8000_000F;
    private const uint VTagNormalDispSet = 0x8000_0010;
    private const uint VTagCoerceDispGet = 0x8000_0011;
    private const uint VTagCoerceDispSet = 0x8000_0012;
    private const uint VTagViewPortExtraGet = 0x8000_0013;
    private const uint VTagViewPortExtraSet = 0x8000_0014;
    private const uint VTagAttachCmGet = 0x8000_001B;
    private const uint VTagBatchCmGet = 0x8000_001C;
    private const uint VTagBatchItemsGet = 0x8000_001D;
    private const uint VTagBatchItemsSet = 0x8000_001E;
    private const uint VTagBatchItemsAdd = 0x8000_001F;
    private const uint VTagChromaPenGet = 0x8000_0019;
    private const uint VTagChromaPlaneGet = 0x8000_001A;
    private const uint VTagVpModeIdGet = 0x8000_0020;
    private const uint VTagVpModeIdSet = 0x8000_0021;
    private const uint VTagVpModeIdClr = 0x8000_0022;
    private const uint VTagUserClipGet = 0x8000_0023;
    private const uint VTagUserClipSet = 0x8000_0024;
    private const uint VTagUserClipClr = 0x8000_0025;
    private const uint VTagPf1BaseGet = 0x8000_0026;
    private const uint VTagPf2BaseGet = 0x8000_0027;
    private const uint VTagSpEvenBaseGet = 0x8000_0028;
    private const uint VTagSpOddBaseGet = 0x8000_0029;
    private const uint VTagPf1BaseSet = 0x8000_002A;
    private const uint VTagPf2BaseSet = 0x8000_002B;
    private const uint VTagSpEvenBaseSet = 0x8000_002C;
    private const uint VTagSpOddBaseSet = 0x8000_002D;
    private const uint VTagBorderSpriteGet = 0x8000_002E;
    private const uint VTagBorderSpriteSet = 0x8000_002F;
    private const uint VTagBorderSpriteClr = 0x8000_0030;
    private const uint VTagSpriteResnSet = 0x8000_0031;
    private const uint VTagSpriteResnGet = 0x8000_0032;
    private const uint VTagImmediate = 0x8000_0037;
    private const uint VTagFullPaletteSet = 0x8000_0038;
    private const uint VTagFullPaletteGet = 0x8000_0039;
    private const uint VTagFullPaletteClr = 0x8000_003A;
    private const uint VTagDefSpriteResnSet = 0x8000_003B;
    private const uint VTagDefSpriteResnGet = 0x8000_003C;
    private const uint VTagPf1ToSpritePriSet = 0x8000_0033;
    private const uint VTagPf1ToSpritePriGet = 0x8000_0034;
    private const uint VTagPf2ToSpritePriSet = 0x8000_0035;
    private const uint VTagPf2ToSpritePriGet = 0x8000_0036;
    private const uint VcIntermediateUpdate = 0x8000_0080;
    private const uint VcIntermediateUpdateQuery = 0x8000_0081;
    private const uint VcNoColorLoad = 0x8000_0082;
    private const uint VcNoColorLoadQuery = 0x8000_0083;
    private const uint VcDualPfDisable = 0x8000_0084;
    private const uint VcDualPfDisableQuery = 0x8000_0085;
    private const byte ColorMapFlagChromaKey = 1 << 0;
    private const byte ColorMapFlagBitPlaneKey = 1 << 1;
    private const byte ColorMapFlagBorderBlanking = 1 << 2;
    private const byte ColorMapFlagBorderNoTransparency = 1 << 3;
    private const byte ColorMapFlagVideoControlBatch = 1 << 4;
    private const byte ColorMapFlagUserCopperClip = 1 << 5;
    private const byte ColorMapFlagBorderSprites = 1 << 6;
    private const byte ColorMapAuxFullPalette = 1 << 0;
    private const byte ColorMapAuxNoIntermediateUpdate = 1 << 1;
    private const byte ColorMapAuxNoColorLoad = 1 << 2;
    private const byte ColorMapAuxDualPfDisable = 1 << 3;
    private const uint ObpPrecision = 0x8400_0000;
    private const uint ObpFailIfBad = 0x8400_0001;

    // The public values are defined by graphics/view.h.  Kickstart compares
    // the squared 8-bit RGB error against precision^2, relaxed by the ratio
    // of the sharable palette that is still free.  Keep the public constants
    // here so the portable and native entry points share the same tag ABI.
    internal const int PrecisionExact = -1;
    internal const int PrecisionImage = 0;
    internal const int PrecisionIcon = 16;
    internal const int PrecisionGui = 32;

    private static bool TryGetToleranceSquared(int precision, out long toleranceSquared)
    {
        // Kickstart's comparison treats PRECISION_EXACT specially, but the
        // precision tag itself is an arithmetic tolerance rather than an
        // enum gate.  The documented IMAGE/ICON/GUI values are the common
        // presets; accepting other non-negative values preserves the ROM's
        // useful behavior for callers that tune the tolerance.  Keep the
        // square in a signed 64-bit value so malformed host state cannot
        // wrap the later free-entry-scaled comparison.
        if (precision == PrecisionExact)
        {
            toleranceSquared = 0;
            return true;
        }

        if (precision < 0)
        {
            toleranceSquared = 0;
            return false;
        }

        toleranceSquared = (long)precision * precision;
        return true;
    }

    internal sealed class Registry
    {
        internal Dictionary<uint, Allocation> Maps { get; } = new();
    }

    internal sealed class Allocation
    {
        internal required uint Address { get; init; }
        internal required uint Count { get; init; }
        internal required uint ColorTable { get; init; }
        internal required uint LowColorBits { get; init; }
        internal required uint StructBytes { get; init; }
        internal required uint ColorBytes { get; init; }
        internal uint PaletteExtra { get; set; }
        internal uint PaletteRefCount { get; set; }
        internal uint PaletteAllocList { get; set; }
        internal uint PaletteViewPort { get; set; }
        // The last guest sharable-index value successfully synchronized from
        // this host shadow.  A native caller may legitimately narrow the
        // private pe_SharableColors field; the next portable call can then
        // project the derived lists for that new limit.  Other sidecar edits
        // are treated as foreign/corrupt state and remain unclaimed.
        internal int PublishedSharableColors { get; set; } = -1;
        internal uint ChromaPen { get; set; }
        internal uint Pf1ToSpritePriority { get; set; }
        internal uint Pf2ToSpritePriority { get; set; }
        // VTAG_VPMODEID_SET has a valid zero value: DEFAULT_MONITOR_ID is a
        // real display mode, while the public ColorMap word is also zero
        // after VTAG_VPMODEID_CLR. Keep presence separate from the stored
        // ModeID so GetVPModeID can distinguish those two guest states.
        internal bool ModeIdOverrideSet { get; set; }
        // PaletteExtra's private reference-count table is WORD-sized in the
        // native implementation.  Keep the host shadow at the same width so
        // repeated shared acquisitions do not stop at 255 while the guest
        // sidecar still has representable capacity.
        internal ushort[] RefCounts { get; init; } = Array.Empty<ushort>();
        internal bool[] Exclusive { get; init; } = Array.Empty<bool>();
        internal int[] AllocationNext { get; init; } = Array.Empty<int>();
        internal int FreeHead { get; set; } = -1;
        internal int SharedHead { get; set; } = -1;
        // The guest PaletteExtra begins with a SignalSemaphore.  Portable
        // calls serialize the corresponding palette state on this host lock;
        // the guest semaphore bytes are published separately below so native
        // 68k callers see the same public layout.
        internal object SyncRoot { get; } = new();
    }

    internal static bool TryGetAllocationSyncRoot(
        Registry registry,
        uint map,
        out object syncRoot)
    {
        if (registry.Maps.TryGetValue(map, out var allocation))
        {
            syncRoot = allocation.SyncRoot;
            return true;
        }

        syncRoot = null!;
        return false;
    }

    /// <summary>
    /// Mirrors the public Exec SignalSemaphore ownership fields while the
    /// host-side ColorMap lock is held.  A native task that already owns the
    /// PaletteExtra semaphore may re-enter it; a different task is declined
    /// without changing guest state so the native/provider path can perform
    /// the real blocking operation.  Host-only callers (no current-task
    /// identity) retain the compatibility lock behavior.
    /// </summary>
    internal static bool TryEnterPaletteSemaphore(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint currentTask)
    {
        if (currentTask == 0 ||
            !registry.Maps.TryGetValue(map, out var allocation) ||
            allocation.PaletteExtra == 0)
            return true;

        var ownerAddress = allocation.PaletteExtra +
            (uint)GraphicsLayouts.PaletteExtraSemaphoreOwner;
        var nestAddress = allocation.PaletteExtra +
            (uint)GraphicsLayouts.PaletteExtraSemaphoreNestCount;
        if (!memory.TryReadLong(ownerAddress, out var owner) ||
            !memory.TryReadWord(nestAddress, out var nestCount))
            return false;

        if (owner != 0 && owner != currentTask)
            return false;

        // Exec's SignalSemaphore cannot be owned with a zero nest count.
        // Do not repair that impossible state from the portable path: a
        // native/provider owner may still be publishing or tearing down the
        // envelope, so malformed guest state must remain available to that
        // boundary instead of being silently claimed here.
        if (owner != 0 && nestCount == 0)
            return false;

        if (owner == 0)
        {
            // A non-zero nest count without an owner is corrupt and must stay
            // available to the native implementation rather than being
            // silently repaired by a portable call.
            if (nestCount != 0 ||
                !memory.TryWriteLong(ownerAddress, currentTask))
                return false;

            if (memory.TryWriteWord(nestAddress, 1))
                return true;

            RestoreLong(memory, ownerAddress, 0);
            return false;
        }

        if (nestCount == ushort.MaxValue)
            return false;

        return memory.TryWriteWord(nestAddress, (ushort)(nestCount + 1));
    }

    internal static void ExitPaletteSemaphore(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint currentTask)
    {
        if (currentTask == 0 ||
            !registry.Maps.TryGetValue(map, out var allocation) ||
            allocation.PaletteExtra == 0)
            return;

        var ownerAddress = allocation.PaletteExtra +
            (uint)GraphicsLayouts.PaletteExtraSemaphoreOwner;
        var nestAddress = allocation.PaletteExtra +
            (uint)GraphicsLayouts.PaletteExtraSemaphoreNestCount;
        if (!memory.TryReadLong(ownerAddress, out var owner) ||
            owner != currentTask ||
            !memory.TryReadWord(nestAddress, out var nestCount) ||
            nestCount == 0)
            return;

        if (nestCount > 1)
        {
            if (!memory.TryWriteWord(nestAddress, (ushort)(nestCount - 1)))
                RestoreWord(memory, nestAddress, nestCount);

            return;
        }

        // Clear the nest count before the owner so a reader never sees an
        // owned semaphore with an impossible zero/zero ordering.
        if (!memory.TryWriteWord(nestAddress, 0))
        {
            RestoreWord(memory, nestAddress, nestCount);
            return;
        }

        if (!memory.TryWriteLong(ownerAddress, 0))
        {
            RestoreLong(memory, ownerAddress, currentTask);
            RestoreWord(memory, nestAddress, nestCount);
        }
    }

    internal static uint GetColorMap(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint entries)
    {
        if (entries == 0 || entries > MaxEntries || entries > uint.MaxValue / 2)
            return 0;

        var colorBytes = entries * 2;
        uint map = 0;
        uint colors = 0;
        uint lowColors = 0;
        var mapAllocated = allocator.TryAllocate(
            (uint)GraphicsLayouts.ColorMapSize,
            GraphicsMemoryClass.Public,
            out map);
        var colorsAllocated = mapAllocated && allocator.TryAllocate(
            colorBytes,
            GraphicsMemoryClass.Public,
            out colors);
        var lowColorsAllocated = colorsAllocated && allocator.TryAllocate(
            colorBytes,
            GraphicsMemoryClass.Public,
            out lowColors);
        if (!mapAllocated ||
            !colorsAllocated ||
            !lowColorsAllocated ||
            !IsEvenAddress(map) ||
            !IsEvenAddress(colors) ||
            !IsEvenAddress(lowColors))
        {
            if (lowColors != 0)
                allocator.Free(lowColors, colorBytes, GraphicsMemoryClass.Public);
            if (colors != 0)
                allocator.Free(colors, colorBytes, GraphicsMemoryClass.Public);
            if (map != 0)
                allocator.Free(map, (uint)GraphicsLayouts.ColorMapSize, GraphicsMemoryClass.Public);
            return 0;
        }

        var originalMap = Array.Empty<byte>();
        var originalColors = Array.Empty<byte>();
        var originalLowColors = Array.Empty<byte>();
        var envelopesCaptured =
            TryCaptureFreshEnvelope(
                memory,
                map,
                (uint)GraphicsLayouts.ColorMapSize,
                out originalMap) &&
            TryCaptureFreshEnvelope(
                memory,
                colors,
                colorBytes,
                out originalColors) &&
            TryCaptureFreshEnvelope(
                memory,
                lowColors,
                colorBytes,
                out originalLowColors);
        if (!envelopesCaptured ||
            !TryClear(memory, map, (uint)GraphicsLayouts.ColorMapSize) ||
            !TryInitializeMap(memory, map, entries, colors, lowColors) ||
            !TryClear(memory, colors, colorBytes) ||
            !TryClear(memory, lowColors, colorBytes))
        {
            RestoreSnapshot(memory, map, originalMap);
            RestoreSnapshot(memory, colors, originalColors);
            RestoreSnapshot(memory, lowColors, originalLowColors);
            allocator.Free(lowColors, colorBytes, GraphicsMemoryClass.Public);
            allocator.Free(colors, colorBytes, GraphicsMemoryClass.Public);
            allocator.Free(map, (uint)GraphicsLayouts.ColorMapSize, GraphicsMemoryClass.Public);
            return 0;
        }

        var allocation = new Allocation
        {
            Address = map,
            Count = entries,
            ColorTable = colors,
            LowColorBits = lowColors,
            StructBytes = (uint)GraphicsLayouts.ColorMapSize,
            ColorBytes = colorBytes,
            RefCounts = new ushort[(int)entries],
            Exclusive = new bool[(int)entries],
            AllocationNext = new int[(int)entries]
        };
        Array.Fill(allocation.AllocationNext, -1);
        registry.Maps[map] = allocation;
        return map;
    }

    internal static bool FreeColorMap(
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint map)
    {
        if (!registry.Maps.Remove(map, out var allocation))
            return false;

        var sidecarBytes = checked(allocation.Count * 2);
        if (allocation.PaletteAllocList != 0)
            allocator.Free(allocation.PaletteAllocList, sidecarBytes, GraphicsMemoryClass.Public);
        if (allocation.PaletteRefCount != 0)
            allocator.Free(allocation.PaletteRefCount, sidecarBytes, GraphicsMemoryClass.Public);
        if (allocation.PaletteExtra != 0)
            allocator.Free(
                allocation.PaletteExtra,
                (uint)GraphicsLayouts.PaletteExtraSize,
                GraphicsMemoryClass.Public);
        allocator.Free(allocation.LowColorBits, allocation.ColorBytes, GraphicsMemoryClass.Public);
        allocator.Free(allocation.ColorTable, allocation.ColorBytes, GraphicsMemoryClass.Public);
        allocator.Free(allocation.Address, allocation.StructBytes, GraphicsMemoryClass.Public);
        return true;
    }

    internal static int AttachPalExtra(
        IGraphicsAllocatorBackend allocator,
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint viewPort)
    {
        if (!registry.Maps.TryGetValue(map, out var allocation) ||
            !IsEvenAddress(viewPort) ||
            !ProbeRange(memory, viewPort, (uint)GraphicsLayouts.ViewPortSize))
            return 1;

        if (allocation.PaletteExtra != 0)
        {
            return TryPreparePaletteExtra(memory, allocation, out _) &&
                   memory.TryReadLong(
                       allocation.PaletteExtra + (uint)GraphicsLayouts.PaletteExtraViewPort,
                       out var existingViewPort) && existingViewPort == viewPort
                ? 0
                : 1;
        }

        if (!TryDetermineSharableCount(memory, viewPort, allocation.Count, out var sharableCount))
            return 1;

        if (!TryAdd(
                map,
                (uint)GraphicsLayouts.ColorMapPaletteExtra,
                out var paletteExtraField) ||
            !TrySnapshot(
                memory,
                paletteExtraField,
                sizeof(uint),
                out var originalPaletteExtraField))
        {
            return 1;
        }

        var count = allocation.Count;
        var sidecarBytes = checked(count * 2);
        uint paletteExtra = 0;
        uint refCount = 0;
        uint allocList = 0;
        var paletteExtraAllocated = allocator.TryAllocate(
            (uint)GraphicsLayouts.PaletteExtraSize,
            GraphicsMemoryClass.Public,
            out paletteExtra);
        var refCountAllocated = paletteExtraAllocated && allocator.TryAllocate(
            sidecarBytes,
            GraphicsMemoryClass.Public,
            out refCount);
        var allocListAllocated = refCountAllocated && allocator.TryAllocate(
            sidecarBytes,
            GraphicsMemoryClass.Public,
            out allocList);
        var originalPaletteExtra = Array.Empty<byte>();
        var originalRefCount = Array.Empty<byte>();
        var originalAllocList = Array.Empty<byte>();
        var envelopesReadable =
            paletteExtraAllocated &&
            refCountAllocated &&
            allocListAllocated &&
            TryCaptureFreshEnvelope(
                memory,
                paletteExtra,
                (uint)GraphicsLayouts.PaletteExtraSize,
                out originalPaletteExtra) &&
            TryCaptureFreshEnvelope(
                memory,
                refCount,
                sidecarBytes,
                out originalRefCount) &&
            TryCaptureFreshEnvelope(
                memory,
                allocList,
                sidecarBytes,
                out originalAllocList);
        if (!paletteExtraAllocated ||
            !refCountAllocated ||
            !allocListAllocated ||
            !IsEvenAddress(paletteExtra) ||
            !IsEvenAddress(refCount) ||
            !IsEvenAddress(allocList) ||
            !envelopesReadable ||
            !TryClear(memory, paletteExtra, (uint)GraphicsLayouts.PaletteExtraSize) ||
            !TryClear(memory, refCount, sidecarBytes) ||
            !TryClear(memory, allocList, sidecarBytes) ||
            // PaletteExtra starts with a public exec SignalSemaphore.  Keep
            // its Node type and empty MinList valid even when no host Exec
            // semaphore service is attached to this portable allocation.
            !memory.TryWriteByte(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreNodeType,
                15) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreWaitQueue,
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreWaitQueue + 4) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreWaitQueue + 4,
                0) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreWaitQueue + 8,
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreWaitQueue) ||
            !memory.TryWriteWord(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreNestCount,
                0) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreOwner,
                0) ||
            !memory.TryWriteWord(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSemaphoreQueueCount,
                0) ||
            !memory.TryWriteWord(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraNFree,
                checked((ushort)sharableCount)) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraRefCount,
                refCount) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraAllocList,
                allocList) ||
            !memory.TryWriteLong(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraViewPort,
                viewPort) ||
            !memory.TryWriteWord(
                paletteExtra + (uint)GraphicsLayouts.PaletteExtraSharableColors,
                checked((ushort)(sharableCount - 1))) ||
            !memory.TryWriteLong(
                paletteExtraField,
                paletteExtra))
        {
            RestoreSnapshot(memory, paletteExtra, originalPaletteExtra);
            RestoreSnapshot(memory, refCount, originalRefCount);
            RestoreSnapshot(memory, allocList, originalAllocList);
            RestoreSnapshot(memory, paletteExtraField, originalPaletteExtraField);
            if (allocList != 0)
                allocator.Free(allocList, sidecarBytes, GraphicsMemoryClass.Public);
            if (refCount != 0)
                allocator.Free(refCount, sidecarBytes, GraphicsMemoryClass.Public);
            if (paletteExtra != 0)
                allocator.Free(
                    paletteExtra,
                    (uint)GraphicsLayouts.PaletteExtraSize,
                    GraphicsMemoryClass.Public);
            return 1;
        }

        allocation.PaletteExtra = paletteExtra;
        allocation.PaletteRefCount = refCount;
        allocation.PaletteAllocList = allocList;
        allocation.PaletteViewPort = viewPort;
        allocation.FreeHead = checked((int)sharableCount - 1);
        allocation.SharedHead = -1;
        for (var index = 0; index < allocation.AllocationNext.Length; index++)
            allocation.AllocationNext[index] = index - 1;
        if (!SyncPaletteExtra(memory, allocation, allowFreshSnapshots: true))
        {
            RestoreSnapshot(memory, paletteExtra, originalPaletteExtra);
            RestoreSnapshot(memory, refCount, originalRefCount);
            RestoreSnapshot(memory, allocList, originalAllocList);
            RestoreSnapshot(memory, paletteExtraField, originalPaletteExtraField);
            allocation.PaletteExtra = 0;
            allocation.PaletteRefCount = 0;
            allocation.PaletteAllocList = 0;
            allocation.PaletteViewPort = 0;
            allocation.PublishedSharableColors = -1;
            allocator.Free(allocList, sidecarBytes, GraphicsMemoryClass.Public);
            allocator.Free(refCount, sidecarBytes, GraphicsMemoryClass.Public);
            allocator.Free(paletteExtra, (uint)GraphicsLayouts.PaletteExtraSize, GraphicsMemoryClass.Public);
            return 1;
        }
        return 0;
    }

    private static bool IsEvenAddress(uint address)
        => address != 0 && (address & 1u) == 0;

    private static bool ProbeRange(IGraphicsMemory memory, uint address, uint byteCount)
    {
        if (address == 0 || byteCount == 0 || address > uint.MaxValue - (byteCount - 1))
            return false;

        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + offset, out _))
                return false;
        }

        return true;
    }

    private static bool TryDetermineSharableCount(
        IGraphicsMemory memory,
        uint viewPort,
        uint colorCount,
        out uint sharableCount)
    {
        sharableCount = colorCount;
        if (!memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo) ||
            rasInfo == 0)
            return true;

        if (!ProbeRange(memory, rasInfo, (uint)GraphicsLayouts.RasInfoSize) ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var bitMap) ||
            bitMap == 0)
            return false;

        if (!ProbeRange(
                memory,
                bitMap,
                (uint)(GraphicsLayouts.BitMapDepth + sizeof(byte))) ||
            !memory.TryReadByte(
                bitMap + (uint)GraphicsLayouts.BitMapDepth,
                out var depth) ||
            depth == 0 || depth > 8)
            return false;

        // The depth is read from a compact, potentially BMF_MINPLANES,
        // header.  Validate its declared envelope before publishing any
        // sharable-color count through the color-map path.
        var structureBytes = GraphicsLayouts.BitMapPlanes + (depth * sizeof(uint));
        if (!ProbeRange(memory, bitMap, (uint)structureBytes))
            return false;

        // Pointer/sprite colors may extend the ColorMap beyond the number of
        // colors addressable by the viewport's planar depth.  Kickstart keeps
        // only 2^depth entries sharable for depths below eight.
        if (depth < 8)
            sharableCount = Math.Min(colorCount, 1u << depth);
        return sharableCount != 0;
    }

    internal static bool SetRgb4Cm(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint index,
        byte red,
        byte green,
        byte blue)
    {
        if (!TryGetEntry(memory, registry, map, index, out _, out var colorAddress, out var lowAddress))
            return false;

        if (!registry.Maps.TryGetValue(map, out var allocation))
            return false;

        lock (allocation.SyncRoot)
        {
            if (!memory.TryReadWord(colorAddress, out var oldHigh) ||
                !TryReadLowColorWord(memory, lowAddress, out var oldLow))
                return false;

            // The high ColorTable nibble is reserved state in the classic
            // layout (used by genlock/alpha integrations).  Both native
            // SetRGB4CM and SetRGB32CM preserve it while replacing the RGB
            // nibbles; the low-color table is reset for RGB4 writes.
            var value = (ushort)((oldHigh & 0xF000) |
                ((red & 0x0F) << 8) |
                ((green & 0x0F) << 4) |
                (blue & 0x0F));

            return TryApplyColorMapUpdates(
                memory,
                new[] { new ColorMapUpdate(colorAddress, lowAddress, oldHigh, oldLow, value, 0) });
        }
    }

    internal static bool SetRgb32Cm(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint index,
        uint red,
        uint green,
        uint blue)
    {
        if (!TryGetEntry(memory, registry, map, index, out _, out var colorAddress, out var lowAddress))
            return false;

        if (!registry.Maps.TryGetValue(map, out var allocation))
            return false;

        lock (allocation.SyncRoot)
        {
            var red8 = (byte)(red >> 24);
            var green8 = (byte)(green >> 24);
            var blue8 = (byte)(blue >> 24);
            if (!memory.TryReadWord(colorAddress, out var oldHigh) ||
                !TryReadLowColorWord(memory, lowAddress, out var oldLow))
                return false;

            var high = (ushort)((oldHigh & 0xF000) |
                ((red8 >> 4) << 8) |
                ((green8 >> 4) << 4) |
                (blue8 >> 4));
            var low = (ushort)(((red8 & 0x0F) << 8) | ((green8 & 0x0F) << 4) | (blue8 & 0x0F));

            return TryApplyColorMapUpdates(
                memory,
                new[] { new ColorMapUpdate(colorAddress, lowAddress, oldHigh, oldLow, high, low) });
        }
    }

    internal static int ObtainPen(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint requested,
        uint red,
        uint green,
        uint blue,
        uint flags,
        IGraphicsPaletteBackend? palette = null)
    {
        // Kickstart's pen-sharing path is enabled by AttachPalExtra.  A
        // ColorMap without that envelope is not eligible for ObtainPen;
        // keeping the check at the portable boundary also prevents a host
        // fallback from publishing a pen that native callers could not
        // subsequently release through the palette-sharing state.
        if (!registry.Maps.TryGetValue(map, out var allocation) || allocation.PaletteExtra == 0)
            return -1;

        var exclusive = (flags & PenExclusive) != 0;
        var noSetColor = (flags & PenNoSetColor) != 0;
        if (!TryPreparePaletteExtra(memory, allocation, out var sharableCount))
            return -1;
        var availableCount = exclusive ? (int)allocation.Count : sharableCount;
        var desired = ((byte)(red >> 24), (byte)(green >> 24), (byte)(blue >> 24));

        if (!exclusive && requested < (uint)availableCount && allocation.RefCounts[(int)requested] != 0 &&
            !allocation.Exclusive[(int)requested] &&
            TryReadColor8(memory, allocation, requested, out var requestedColor) && requestedColor == desired)
        {
            return TryObtainExistingShared(memory, allocation, (int)requested)
                ? (int)requested
                : -1;
        }

        if (!exclusive && requested == uint.MaxValue)
        {
            var shared = FindSharedColor(memory, allocation, availableCount, desired);
            if (shared >= 0)
                return TryObtainExistingShared(memory, allocation, shared) ? shared : -1;
        }

        var selected = requested == uint.MaxValue
            ? FindFree(allocation, availableCount)
            : requested < (uint)availableCount && allocation.RefCounts[(int)requested] == 0 &&
              (requested >= (uint)sharableCount || IsFreeEntry(allocation, (int)requested))
                ? (int)requested
                : -1;
        if (selected < 0)
            return -1;

        var selectedIsSharable = selected < sharableCount;
        var listSnapshot = CapturePenListState(allocation);
        if (selectedIsSharable && !RemoveFromFreeList(allocation, selected))
            return -1;

        allocation.RefCounts[selected] = 1;
        allocation.Exclusive[selected] = exclusive;
        if (selectedIsSharable && !exclusive)
            AddToSharedList(allocation, selected);
        if (!noSetColor && !SetRgb32Cm(memory, registry, map, (uint)selected, red, green, blue))
        {
            allocation.RefCounts[selected] = 0;
            allocation.Exclusive[selected] = false;
            RestorePenListState(allocation, listSnapshot);
            return -1;
        }

        if (!SyncPaletteExtra(memory, allocation))
        {
            allocation.RefCounts[selected] = 0;
            allocation.Exclusive[selected] = false;
            RestorePenListState(allocation, listSnapshot);
            SyncPaletteExtra(memory, allocation);
            return -1;
        }

        // A newly claimed pen changes the visible OCS/ECS palette when the
        // PaletteExtra carries a viewport back-pointer.  Publish only after
        // the guest ColorMap and sharing envelope have committed; shared
        // references and PEN_NO_SETCOLOR requests do not recolor.
        if (!noSetColor && palette is not null &&
            TryReadPaletteViewPort(memory, allocation, out var viewPort))
        {
            palette.SetRgb4(
                viewPort,
                unchecked((short)selected),
                (byte)(red >> 28),
                (byte)(green >> 28),
                (byte)(blue >> 28));
        }

        return selected;
    }

    internal static bool ReleasePen(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint pen)
    {
        if (pen == uint.MaxValue)
            return true;
        if (!registry.Maps.TryGetValue(map, out var allocation) || pen >= allocation.Count || allocation.RefCounts[(int)pen] == 0)
            return false;

        var index = (int)pen;
        var previousExclusive = allocation.Exclusive[index];
        var listSnapshot = CapturePenListState(allocation);
        var isSharable = false;
        if (allocation.PaletteExtra != 0)
        {
            if (!TryPreparePaletteExtra(memory, allocation, out var sharableCount))
                return false;
            isSharable = index < sharableCount;
        }

        allocation.RefCounts[index]--;
        if (allocation.RefCounts[index] == 0)
        {
            allocation.Exclusive[index] = false;
            if (isSharable)
            {
                if (!previousExclusive && !RemoveFromSharedList(allocation, index))
                {
                    allocation.RefCounts[index]++;
                    allocation.Exclusive[index] = previousExclusive;
                    RestorePenListState(allocation, listSnapshot);
                    return false;
                }
                AddToFreeList(allocation, index);
            }
        }
        if (!SyncPaletteExtra(memory, allocation))
        {
            allocation.RefCounts[index]++;
            allocation.Exclusive[index] = previousExclusive;
            RestorePenListState(allocation, listSnapshot);
            return false;
        }
        return true;
    }

    internal static int FindColor(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint red,
        uint green,
        uint blue,
        int maxPen)
    {
        if (!registry.Maps.TryGetValue(map, out var allocation))
            return -1;

        // FindColor's public maxpen is an ULONG.  Only the all-ones value is
        // the documented "use sharable pens" sentinel; other values whose
        // high bit is set are ordinary large limits and clamp to Count - 1.
        // Treating every negative Int32 representation as the sentinel would
        // incorrectly hide sprite/pointer entries for callers passing, for
        // example, 0xFFFFFFFE or 0x80000000.
        var sharableSentinel = maxPen == -1;
        var limit = sharableSentinel
            ? (int)allocation.Count - 1
            : maxPen < 0
                ? (int)allocation.Count - 1
                : Math.Min(maxPen, (int)allocation.Count - 1);
        if (sharableSentinel && allocation.PaletteExtra != 0)
        {
            // The documented -1 form means "pens renderable by this
            // viewport" once palette sharing has been initialized.  The
            // PaletteExtra field stores the highest sharable index, so use
            // the same bounded span as ObtainPen/ObtainBestPenA.  An
            // unattached map intentionally retains the full-map behavior.
            if (!TryPreparePaletteExtra(memory, allocation, out var sharableCount))
                return -1;
            limit = sharableCount - 1;
        }
        if (limit < 0)
            return -1;

        var target = ((int)(red >> 24), (int)(green >> 24), (int)(blue >> 24));
        var best = -1;
        var bestDistance = long.MaxValue;
        for (var index = 0; index <= limit; index++)
        {
            if (!TryReadColor8(memory, allocation, (uint)index, out var color))
                return -1;

            var dr = target.Item1 - color.Item1;
            var dg = target.Item2 - color.Item2;
            var db = target.Item3 - color.Item3;
            var distance = (long)dr * dr + (long)dg * dg + (long)db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    internal static int ObtainBestPenA(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint red,
        uint green,
        uint blue,
        uint tags,
        IGraphicsPaletteBackend? palette = null)
    {
        _ = TryObtainBestPenA(
            memory,
            registry,
            map,
            red,
            green,
            blue,
            tags,
            out var result,
            palette);
        return result;
    }

    internal static bool TryObtainBestPenA(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint red,
        uint green,
        uint blue,
        uint tags,
        out int result,
        IGraphicsPaletteBackend? palette = null)
    {
        result = -1;
        if (!registry.Maps.TryGetValue(map, out var allocation) || allocation.PaletteExtra == 0)
            return false;

        var precision = PrecisionImage;
        var failIfBad = false;
        if (tags != 0 && !TryReadBestPenTags(memory, tags, out precision, out failIfBad))
            return false;

        if (!TryPreparePaletteExtra(memory, allocation, out var sharableCount) ||
            !TryFindNearestSharable(
                memory,
                allocation,
                sharableCount,
                red,
                green,
                blue,
                out var nearest,
                out var distance))
        {
            // A failed nearest-color read means the shared ColorMap state is
            // malformed, not that the palette is merely empty.  Keep the
            // vector transparent so native Kickstart or a provider can own
            // the recovery; the public direct helper still reports -1.
            return false;
        }

        if (nearest < 0)
        {
            // ObtainBestPenA measures the nearest already-shared entry.  A
            // freshly attached map has no such entry yet, so let the normal
            // ObtainPen(-1) path claim the first free sharable slot instead
            // of duplicating its list-selection and publication rules.
            result = ObtainPen(memory, registry, map, uint.MaxValue, red, green, blue, 0, palette);
            return true;
        }

        if (!TryGetToleranceSquared(precision, out var toleranceSquared))
            return false;

        var freeCount = CountFree(allocation, sharableCount);
        if (IsWithinBestPenTolerance(
                distance,
                toleranceSquared,
                precision,
                freeCount,
                sharableCount))
        {
            if (allocation.RefCounts[nearest] == 0)
            {
                result = ObtainPen(memory, registry, map, (uint)nearest, red, green, blue, 0, palette);
                return true;
            }

            result = TryObtainExistingShared(memory, allocation, nearest) ? nearest : -1;
            return true;
        }

        var free = FindFree(allocation, sharableCount);
        if (free >= 0)
        {
            result = ObtainPen(memory, registry, map, (uint)free, red, green, blue, 0, palette);
            return true;
        }

        if (failIfBad || !TryObtainExistingShared(memory, allocation, nearest))
            return true;

        result = nearest;
        return true;
    }

    internal static bool VideoControl(
        IGraphicsMemory memory,
        Registry registry,
        GraphicsExtendedNodeOperations.Registry extendedNodes,
        uint map,
        uint tags)
    {
        if (!registry.Maps.ContainsKey(map) || tags == 0)
            return false;

        var items = new List<VideoControlItem>();
        if (!TryCollectVideoControlTags(memory, tags, items) ||
            !TryPreflightVideoControlOutputs(memory, items) ||
            !registry.Maps.TryGetValue(map, out var allocation))
            return false;

        var extendedNodeSnapshot = extendedNodes.CaptureState();
        var originalChromaPen = allocation.ChromaPen;
        var originalPf1ToSpritePriority = allocation.Pf1ToSpritePriority;
        var originalPf2ToSpritePriority = allocation.Pf2ToSpritePriority;
        var originalModeIdOverrideSet = allocation.ModeIdOverrideSet;
        var transaction = new GraphicsMemoryTransaction(memory);
        memory = transaction;
        var immediateOutputs = new List<uint>();
        var requiresViewportRemake = false;
        var committed = false;
        try
        {
            foreach (var item in items)
            {
                var tag = item.Tag;
                var data = item.Data;
                if (tag == VTagImmediate)
                {
                    // VTAG_IMMEDIATE is an output request, not a state
                    // mutation.  Defer the write until every preceding tag
                    // has committed so a late failure cannot leave the
                    // caller's "must remake" word claiming a successful
                    // partial transaction.
                    immediateOutputs.Add(data);
                    continue;
                }

                if (tag == VTagNextBufCm)
                {
                    // VTAG_NEXTBUF_CM is a command-buffer splice, not a
                    // ColorMap mutation.  The collector has already walked
                    // the continuation and bounded it before this loop.
                    continue;
                }

                // The portable owner does not patch an already-published
                // copper list in place.  Any non-query command therefore
                // requires the caller's normal MakeVPort/MrgCop rebuild;
                // pure GET/QUERY tags leave the immediate result clear.
                if (!IsVideoControlQueryTag(tag))
                    requiresViewportRemake = true;

                if (IsClassicVideoControlGetter(tag))
                {
                    if (!TryRewriteVideoControlGetter(memory, allocation, item.TagAddress, tag))
                        return false;
                    continue;
                }

                if (tag == VTagBatchItemsSet)
                {
                    // A null static list is the documented assembling/no-op
                    // form; a non-null list must be a bounded guest command
                    // buffer before the ColorMap head is published.
                    if ((data != 0 && !TryValidateBatchItems(memory, data)) ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapBatchItems, out var batchItemsAddress) ||
                        !memory.TryWriteLong(batchItemsAddress, data))
                        return false;
                }
                else if (tag == VTagBatchItemsGet)
                {
                    if (data == 0 ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapBatchItems, out var batchItemsAddress) ||
                        !memory.TryReadLong(batchItemsAddress, out var batchItems) ||
                        !memory.TryWriteLong(data, batchItems))
                        return false;
                }
                else if (tag == VTagBatchItemsAdd)
                {
                    if (!TryAppendBatchItems(memory, allocation, data))
                        return false;
                }
                else if (TryApplySpriteResolutionTag(memory, allocation, tag, data))
                {
                }
                else if (TryApplyColorBaseTag(memory, allocation, tag, data))
                {
                }
                else if (TryApplyChromaPenTag(memory, allocation, tag, data))
                {
                }
                else if (TryApplySpritePriorityTag(memory, allocation, tag, data))
                {
                }
                else if (tag == VTagChromaPlaneSet)
                {
                    if (data > byte.MaxValue ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapTransparencyPlane, out var planeAddress) ||
                        !memory.TryWriteByte(planeAddress, (byte)data))
                        return false;
                }
                else if (tag == VTagChromaPlaneGet)
                {
                    if (data == 0 ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapTransparencyPlane, out var planeAddress) ||
                        !memory.TryReadByte(planeAddress, out var plane) ||
                        !memory.TryWriteLong(data, plane))
                        return false;
                }
                else if (tag == VTagAttachCmSet)
                {
                    if (!IsEvenAddress(data) ||
                        !TryAdd(data, (uint)GraphicsLayouts.ViewPortColorMap, out _) ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapViewPort, out var mapVp) ||
                        !memory.TryWriteLong(mapVp, data) ||
                        !TryAdd(data, (uint)GraphicsLayouts.ViewPortColorMap, out var vpMap) ||
                        !memory.TryWriteLong(vpMap, map))
                        return false;
                }
                else if (tag == VTagAttachCmGet)
                {
                    if (data == 0 || !TryAdd(map, (uint)GraphicsLayouts.ColorMapViewPort, out var mapVp) ||
                        !memory.TryReadLong(mapVp, out var viewPort) ||
                        !memory.TryWriteLong(data, viewPort))
                        return false;
                }
                else if (tag == VTagViewPortExtraSet)
                {
                    if (!IsEvenAddress(data) ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapViewPort, out var mapVp) ||
                        !memory.TryReadLong(mapVp, out var viewPort) ||
                        !IsEvenAddress(viewPort) ||
                        !extendedNodes.AssociateViewPortExtra(memory, viewPort, data) ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapViewPortExtra, out var extraAddress) ||
                        !memory.TryWriteLong(extraAddress, data))
                        return false;
                }
                else if (tag == VTagViewPortExtraGet)
                {
                    if (data == 0 ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapViewPortExtra, out var extraAddress) ||
                        !memory.TryReadLong(extraAddress, out var viewPortExtra) ||
                        !memory.TryWriteLong(data, viewPortExtra))
                        return false;
                }
                else if (tag == VTagNormalDispSet || tag == VTagCoerceDispSet)
                {
                    if (!GraphicsDisplayDatabase.TryResolveDisplayInfoHandle(data, out _) ||
                        !TryAdd(
                            map,
                            (uint)(tag == VTagNormalDispSet
                                ? GraphicsLayouts.ColorMapNormalDisplayInfo
                                : GraphicsLayouts.ColorMapCoerceDisplayInfo),
                            out var displayInfoAddress) ||
                        !memory.TryWriteLong(displayInfoAddress, data))
                        return false;
                }
                else if (tag == VTagNormalDispGet || tag == VTagCoerceDispGet)
                {
                    if (data == 0 ||
                        !TryAdd(
                            map,
                            (uint)(tag == VTagNormalDispGet
                                ? GraphicsLayouts.ColorMapNormalDisplayInfo
                                : GraphicsLayouts.ColorMapCoerceDisplayInfo),
                            out var displayInfoAddress) ||
                        !memory.TryReadLong(displayInfoAddress, out var displayInfo) ||
                        !memory.TryWriteLong(data, displayInfo))
                        return false;
                }
                else if (tag == VTagVpModeIdSet)
                {
                    if (!IsValidViewPortModeId(data) ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapModeId, out var modeIdAddress) ||
                        !memory.TryWriteLong(modeIdAddress, data))
                        return false;

                    allocation.ModeIdOverrideSet = true;
                }
                else if (tag == VTagVpModeIdGet)
                {
                    if (data == 0 ||
                        !TryAdd(map, (uint)GraphicsLayouts.ColorMapModeId, out var modeIdAddress) ||
                        !memory.TryReadLong(modeIdAddress, out var modeId) ||
                        !memory.TryWriteLong(data, modeId))
                        return false;
                }
                else if (tag == VTagVpModeIdClr)
                {
                    if (!TryAdd(map, (uint)GraphicsLayouts.ColorMapModeId, out var modeIdAddress) ||
                        !memory.TryWriteLong(modeIdAddress, 0))
                        return false;

                    allocation.ModeIdOverrideSet = false;
                }
                else if (!TryApplyFlagTag(memory, allocation, tag, data))
                {
                    return false;
                }
            }

            foreach (var output in immediateOutputs)
            {
                if (!memory.TryWriteLong(output, requiresViewportRemake ? 1u : 0u))
                    return false;
            }

            committed = transaction.Commit();
            return committed;
        }
        finally
        {
            if (!committed)
            {
                transaction.Rollback();
                allocation.ChromaPen = originalChromaPen;
                allocation.Pf1ToSpritePriority = originalPf1ToSpritePriority;
                allocation.Pf2ToSpritePriority = originalPf2ToSpritePriority;
                allocation.ModeIdOverrideSet = originalModeIdOverrideSet;
                extendedNodes.RestoreState(extendedNodeSnapshot);
            }
        }
    }

    /// <summary>
    /// Applies an owned ColorMap's static batch list at the MakeVPort
    /// boundary. Batch mode is a guest-only operation: a foreign ColorMap is
    /// left for the resident/provider implementation, while an owned map
    /// must either complete its bounded VideoControl transaction or make the
    /// copper build decline before publication.
    /// </summary>
    internal static bool TryApplyEnabledVideoControlBatch(
        IGraphicsMemory memory,
        Registry registry,
        GraphicsExtendedNodeOperations.Registry extendedNodes,
        uint viewPort,
        out bool ownedBatch)
    {
        ownedBatch = false;
        if (viewPort == 0 ||
            !TryAdd(viewPort, (uint)GraphicsLayouts.ViewPortColorMap, out var mapAddress) ||
            !memory.TryReadLong(mapAddress, out var map) ||
            map == 0 ||
            !registry.Maps.TryGetValue(map, out var allocation))
        {
            return true;
        }

        if (!memory.TryReadByte(
                allocation.Address + (uint)GraphicsLayouts.ColorMapFlags,
                out var flags) ||
            (flags & ColorMapFlagVideoControlBatch) == 0)
        {
            return true;
        }

        ownedBatch = true;
        if (!memory.TryReadLong(
                allocation.Address + (uint)GraphicsLayouts.ColorMapBatchItems,
                out var batchItems))
        {
            return false;
        }

        // Batch mode with no installed list is a valid no-op while a
        // viewport is being assembled. A non-null list must be a readable,
        // word-aligned command buffer before VideoControl can claim it.
        if (batchItems == 0)
            return true;

        return IsEvenAddress(batchItems) &&
               ProbeRange(memory, batchItems, 8) &&
               VideoControl(memory, registry, extendedNodes, map, batchItems);
    }

    /// <summary>
    /// Resolves the owned ColorMap whose static VideoControl batch will be
    /// replayed by MakeVPort. The replay is transactional, but it still
    /// publishes scalar ColorMap fields before the copper builder is entered.
    /// Native-overlay callers therefore need the exact public ColorMap
    /// envelope before the portable path claims the vector. Foreign maps and
    /// maps without the batch flag are transparent to this helper.
    /// </summary>
    internal static bool TryGetEnabledVideoControlBatchColorMap(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        out uint colorMap,
        out bool hasBatch)
    {
        colorMap = 0;
        hasBatch = false;
        if (viewPort == 0 ||
            !TryAdd(viewPort, (uint)GraphicsLayouts.ViewPortColorMap, out var mapAddress) ||
            !memory.TryReadLong(mapAddress, out colorMap) ||
            colorMap == 0 ||
            !registry.Maps.TryGetValue(colorMap, out var allocation))
        {
            colorMap = 0;
            return true;
        }

        if (!memory.TryReadByte(
                allocation.Address + (uint)GraphicsLayouts.ColorMapFlags,
                out var flags))
        {
            // TryApplyEnabledVideoControlBatch treats an unreadable flag as
            // a non-batch map; keep the same compatibility behavior here.
            colorMap = 0;
            return true;
        }

        hasBatch = (flags & ColorMapFlagVideoControlBatch) != 0;
        if (!hasBatch)
            colorMap = 0;

        return true;
    }

    /// <summary>
    /// Preflights the non-ColorMap guest writes made while replaying a static
    /// VideoControl batch. Classic query tags are rewritten in place through
    /// the transaction, and immediate/query forms publish a LONG to the
    /// caller. Association setters/getters are deliberately left to the
    /// resident/provider boundary because they can touch an opaque viewport
    /// or ViewPortExtra owned outside this graphics registry.
    /// </summary>
    internal static bool IsNativeOverlayVideoControlBatchSafe(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        Func<uint, ulong, bool> canPublish)
    {
        if (!TryGetEnabledVideoControlBatchColorMap(
                memory,
                registry,
                viewPort,
                out var colorMap,
                out var hasBatch) ||
            !hasBatch)
        {
            return true;
        }

        if (!memory.TryReadLong(
                colorMap + (uint)GraphicsLayouts.ColorMapBatchItems,
                out var batchItems) ||
            batchItems == 0)
        {
            return true;
        }

        var items = new List<VideoControlItem>();
        if (!IsEvenAddress(batchItems) ||
            !ProbeRange(memory, batchItems, 8) ||
            !TryCollectVideoControlTags(memory, batchItems, items))
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item.Tag is VTagAttachCmSet or
                VTagViewPortExtraSet or
                VTagAttachCmGet or
                VTagViewPortExtraGet)
            {
                return false;
            }

            if (IsClassicVideoControlGetter(item.Tag) &&
                !canPublish(item.TagAddress, sizeof(uint) * 2u))
            {
                return false;
            }

            if (IsVideoControlExternalOutputTag(item.Tag) &&
                !canPublish(item.Data, sizeof(uint)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryPreflightVideoControlOutputs(
        IGraphicsMemory memory,
        IReadOnlyList<VideoControlItem> items)
    {
        foreach (var item in items)
        {
            if (!IsVideoControlExternalOutputTag(item.Tag))
                continue;

            if (!IsEvenAddress(item.Data) || !ProbeRange(memory, item.Data, 4))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Native-overlay admission for the guest-only portion of VideoControl.
    /// The transaction can safely own scalar ColorMap flags, in-place GET
    /// snapshots, and V40 query output buffers, but attachment/list setters
    /// carry pointers whose provider may belong to resident Kickstart or
    /// CyberGraphX. Keep those tags on the original vector until their
    /// ownership can be proven.
    /// </summary>
    internal static bool IsNativeOverlayVideoControlSafe(
        IGraphicsMemory memory,
        uint tags,
        Func<uint, ulong, bool>? canPublish = null)
    {
        if (tags == 0)
            return false;

        var items = new List<VideoControlItem>();
        if (!TryCollectVideoControlTags(memory, tags, items))
            return false;

        if (canPublish is not null)
        {
            foreach (var item in items)
            {
                // GET forms are rewritten in place by the transaction before
                // their normalized SET form is applied. The TagItem itself
                // is therefore a visible publication span, not merely an
                // input buffer.
                if (IsClassicVideoControlGetter(item.Tag) &&
                    !canPublish(item.TagAddress, sizeof(uint) * 2u))
                {
                    return false;
                }

                // VTAG_IMMEDIATE and the V40 query forms publish a LONG to
                // the caller after all preceding state has staged. Admit the
                // destination before entering the transaction so a failed
                // output cannot be mistaken for a native-owned call.
                if (IsVideoControlExternalOutputTag(item.Tag) &&
                    !canPublish(item.Data, sizeof(uint)))
                {
                    return false;
                }
            }
        }

        if (!TryPreflightVideoControlOutputs(memory, items))
            return false;

        foreach (var item in items)
        {
            var tag = item.Tag;
            if (tag is VTagAttachCmSet or
                VTagViewPortExtraSet or
                VTagBatchItemsSet or
                VTagBatchItemsAdd)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsVideoControlExternalOutputTag(uint tag)
        => tag is
            VTagImmediate or
            VcIntermediateUpdateQuery or
            VcNoColorLoadQuery or
            VcDualPfDisableQuery;

    private static bool IsClassicVideoControlGetter(uint tag)
        => tag is
            VTagBatchItemsGet or
            VTagChromaPlaneGet or
            VTagAttachCmGet or
            VTagViewPortExtraGet or
            VTagNormalDispGet or
            VTagCoerceDispGet or
            VTagVpModeIdGet or
            VTagSpriteResnGet or
            VTagDefSpriteResnGet or
            VTagPf1BaseGet or
            VTagPf2BaseGet or
            VTagSpEvenBaseGet or
            VTagSpOddBaseGet or
            VTagChromaPenGet or
            VTagPf1ToSpritePriGet or
            VTagPf2ToSpritePriGet or
            VTagChromaKeyGet or
            VTagBitPlaneKeyGet or
            VTagBorderBlankGet or
            VTagBorderNoTransGet or
            VTagBatchCmGet or
            VTagUserClipGet or
            VTagBorderSpriteGet or
            VTagFullPaletteGet;

    private static bool IsVideoControlQueryTag(uint tag)
        => IsClassicVideoControlGetter(tag) || tag == VTagNextBufCm || tag is
            VcIntermediateUpdateQuery or
            VcNoColorLoadQuery or
            VcDualPfDisableQuery;

    private static bool TryRewriteVideoControlGetter(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tagAddress,
        uint tag)
    {
        uint rewrittenTag;
        uint rewrittenData;
        switch (tag)
        {
            case VTagBatchItemsGet:
                if (!TryReadLongField(memory, allocation.Address, GraphicsLayouts.ColorMapBatchItems, out rewrittenData))
                    return false;
                rewrittenTag = VTagBatchItemsSet;
                break;
            case VTagChromaPlaneGet:
                if (!TryReadByteField(memory, allocation.Address, GraphicsLayouts.ColorMapTransparencyPlane, out var plane))
                    return false;
                rewrittenTag = VTagChromaPlaneSet;
                rewrittenData = plane;
                break;
            case VTagAttachCmGet:
                if (!TryReadLongField(memory, allocation.Address, GraphicsLayouts.ColorMapViewPort, out rewrittenData))
                    return false;
                rewrittenTag = VTagAttachCmSet;
                break;
            case VTagViewPortExtraGet:
                if (!TryReadLongField(memory, allocation.Address, GraphicsLayouts.ColorMapViewPortExtra, out rewrittenData))
                    return false;
                rewrittenTag = VTagViewPortExtraSet;
                break;
            case VTagNormalDispGet:
                if (!TryReadLongField(memory, allocation.Address, GraphicsLayouts.ColorMapNormalDisplayInfo, out rewrittenData))
                    return false;
                rewrittenTag = VTagNormalDispSet;
                break;
            case VTagCoerceDispGet:
                if (!TryReadLongField(memory, allocation.Address, GraphicsLayouts.ColorMapCoerceDisplayInfo, out rewrittenData))
                    return false;
                rewrittenTag = VTagCoerceDispSet;
                break;
            case VTagVpModeIdGet:
                if (!TryReadLongField(memory, allocation.Address, GraphicsLayouts.ColorMapModeId, out rewrittenData))
                    return false;
                rewrittenTag = rewrittenData == 0 && !allocation.ModeIdOverrideSet
                    ? VTagVpModeIdClr
                    : VTagVpModeIdSet;
                break;
            case VTagSpriteResnGet:
                if (!TryReadByteField(memory, allocation.Address, GraphicsLayouts.ColorMapSpriteResolution, out var spriteResolution))
                    return false;
                rewrittenTag = VTagSpriteResnSet;
                rewrittenData = spriteResolution == byte.MaxValue ? uint.MaxValue : spriteResolution;
                break;
            case VTagDefSpriteResnGet:
                if (!TryReadByteField(memory, allocation.Address, GraphicsLayouts.ColorMapSpriteResDefault, out var defaultSpriteResolution))
                    return false;
                rewrittenTag = VTagDefSpriteResnSet;
                rewrittenData = defaultSpriteResolution == byte.MaxValue ? uint.MaxValue : defaultSpriteResolution;
                break;
            case VTagPf1BaseGet:
                if (!TryReadWordField(memory, allocation.Address, GraphicsLayouts.ColorMapBitPlane0Base, out var pf1Base))
                    return false;
                rewrittenTag = VTagPf1BaseSet;
                rewrittenData = pf1Base;
                break;
            case VTagPf2BaseGet:
                if (!TryReadWordField(memory, allocation.Address, GraphicsLayouts.ColorMapBitPlane1Base, out var pf2Base))
                    return false;
                rewrittenTag = VTagPf2BaseSet;
                rewrittenData = pf2Base;
                break;
            case VTagSpEvenBaseGet:
                if (!TryReadWordField(memory, allocation.Address, GraphicsLayouts.ColorMapSpriteBaseEven, out var spEvenBase))
                    return false;
                rewrittenTag = VTagSpEvenBaseSet;
                rewrittenData = spEvenBase;
                break;
            case VTagSpOddBaseGet:
                if (!TryReadWordField(memory, allocation.Address, GraphicsLayouts.ColorMapSpriteBaseOdd, out var spOddBase))
                    return false;
                rewrittenTag = VTagSpOddBaseSet;
                rewrittenData = spOddBase;
                break;
            case VTagChromaPenGet:
                rewrittenData = allocation.ChromaPen;
                rewrittenTag = rewrittenData == 0 ? VTagChromaPenClr : VTagChromaPenSet;
                break;
            case VTagPf1ToSpritePriGet:
                rewrittenTag = VTagPf1ToSpritePriSet;
                rewrittenData = allocation.Pf1ToSpritePriority;
                break;
            case VTagPf2ToSpritePriGet:
                rewrittenTag = VTagPf2ToSpritePriSet;
                rewrittenData = allocation.Pf2ToSpritePriority;
                break;
            case VTagChromaKeyGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagChromaKey, VTagChromaKeySet, VTagChromaKeyClr);
            case VTagBitPlaneKeyGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagBitPlaneKey, VTagBitPlaneKeySet, VTagBitPlaneKeyClr);
            case VTagBorderBlankGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagBorderBlanking, VTagBorderBlankSet, VTagBorderBlankClr);
            case VTagBorderNoTransGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagBorderNoTransparency, VTagBorderNoTransSet, VTagBorderNoTransClr);
            case VTagBatchCmGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagVideoControlBatch, VTagBatchCmSet, VTagBatchCmClr);
            case VTagUserClipGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagUserCopperClip, VTagUserClipSet, VTagUserClipClr);
            case VTagBorderSpriteGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapFlags, ColorMapFlagBorderSprites, VTagBorderSpriteSet, VTagBorderSpriteClr);
            case VTagFullPaletteGet:
                return TryRewriteByteFlagGetter(memory, allocation.Address, tagAddress, GraphicsLayouts.ColorMapAuxFlags, ColorMapAuxFullPalette, VTagFullPaletteSet, VTagFullPaletteClr);
            default:
                return false;
        }

        return TryWriteVideoControlTag(memory, tagAddress, rewrittenTag, rewrittenData);
    }

    private static bool TryRewriteByteFlagGetter(
        IGraphicsMemory memory,
        uint allocationAddress,
        uint tagAddress,
        int flagsOffset,
        byte mask,
        uint setTag,
        uint clearTag)
    {
        if (!TryReadByteField(memory, allocationAddress, flagsOffset, out var flags))
            return false;

        return TryWriteVideoControlTag(
            memory,
            tagAddress,
            (flags & mask) != 0 ? setTag : clearTag,
            (flags & mask) != 0 ? 1u : 0u);
    }

    private static bool TryWriteVideoControlTag(
        IGraphicsMemory memory,
        uint tagAddress,
        uint tag,
        uint data)
        => TryAdd(tagAddress, 4, out var dataAddress) &&
           memory.TryWriteLong(tagAddress, tag) &&
           memory.TryWriteLong(dataAddress, data);

    private static bool TryReadLongField(
        IGraphicsMemory memory,
        uint baseAddress,
        int offset,
        out uint value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) &&
               memory.TryReadLong(address, out value);
    }

    private static bool TryReadWordField(
        IGraphicsMemory memory,
        uint baseAddress,
        int offset,
        out ushort value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) &&
               memory.TryReadWord(address, out value);
    }

    private static bool TryReadByteField(
        IGraphicsMemory memory,
        uint baseAddress,
        int offset,
        out byte value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) &&
               memory.TryReadByte(address, out value);
    }

    private readonly struct VideoControlItem
    {
        internal VideoControlItem(uint tagAddress, uint tag, uint data)
        {
            TagAddress = tagAddress;
            Tag = tag;
            Data = data;
        }

        internal uint TagAddress { get; }
        internal uint Tag { get; }
        internal uint Data { get; }
    }

    private static bool TryCollectVideoControlTags(
        IGraphicsMemory memory,
        uint tags,
        List<VideoControlItem> items)
    {
        var cursor = tags;
        for (var count = 0; count < 4096; count++)
        {
            // A TagItem occupies bytes [cursor..cursor+7].  The final
            // aligned guest start, $FFFF_FFF8, is complete; only a cursor
            // above that limit crosses the 32-bit address boundary.  Keep
            // the envelope check ahead of both LONG reads so a permissive
            // host map cannot make VideoControl consume a wrapped low alias.
            if ((cursor & 1u) != 0 ||
                cursor > uint.MaxValue - 7u ||
                !memory.TryReadLong(cursor, out var tag) ||
                !TryAdd(cursor, 4, out var dataAddress) ||
                !memory.TryReadLong(dataAddress, out var data))
                return false;

            if (tag == TagEnd)
                return true;
            if (tag == TagMore)
            {
                // A null continuation is the documented end-of-list form;
                // do not reject a valid list merely because it omits TAG_DONE.
                if (data == 0)
                    return true;
                cursor = data;
                continue;
            }

            if (!TryAdd(cursor, 8, out var nextCursor))
                return false;

            if (tag == TagIgnore)
            {
                cursor = nextCursor;
                continue;
            }
            if (tag == TagSkip)
            {
                if (data > (uint.MaxValue - nextCursor) / 8 ||
                    !TryAdd(nextCursor, data * 8, out cursor))
                    return false;
                continue;
            }

            if (tag == VTagNextBufCm)
            {
                // VideoControl's buffer-chain command switches to another
                // TagItem stream. A null buffer is the same clean end form
                // used by TAG_MORE; non-null buffers remain bounded by the
                // single collection budget above.
                if (data == 0)
                    return true;
                cursor = data;
                continue;
            }

            items.Add(new VideoControlItem(cursor, tag, data));
            cursor = nextCursor;
        }

        return false;
    }

    /// <summary>
    /// Appends a guest VideoControl list without allocating a host-side
    /// shadow. Kickstart's ColorMap stores one TagItem head; the ADD form
    /// extends that guest chain by replacing its terminal TAG_END (or a
    /// null TAG_MORE/VTAG_NEXTBUF_CM continuation) with a continuation to
    /// the caller's list. Every envelope is bounded before the staged write
    /// is made, so a malformed existing chain cannot publish a partial link.
    /// </summary>
    private static bool TryAppendBatchItems(
        IGraphicsMemory memory,
        Allocation allocation,
        uint addedItems)
    {
        if (!TryValidateBatchItems(memory, addedItems) ||
            !TryAdd(
                allocation.Address,
                (uint)GraphicsLayouts.ColorMapBatchItems,
                out var batchItemsAddress) ||
            !memory.TryReadLong(batchItemsAddress, out var existingItems))
        {
            return false;
        }

        if (existingItems == 0)
            return memory.TryWriteLong(batchItemsAddress, addedItems);

        if (!IsEvenAddress(existingItems))
            return false;

        var cursor = existingItems;
        for (var count = 0; count < 4096; count++)
        {
            if ((cursor & 1u) != 0 ||
                cursor > uint.MaxValue - 7u ||
                !memory.TryReadLong(cursor, out var tag) ||
                !TryAdd(cursor, 4, out var dataAddress) ||
                !memory.TryReadLong(dataAddress, out var data))
            {
                return false;
            }

            if (tag == TagEnd)
            {
                return memory.TryWriteLong(cursor, TagMore) &&
                       memory.TryWriteLong(dataAddress, addedItems);
            }

            if (tag == TagMore || tag == VTagNextBufCm)
            {
                if (data == 0)
                    return memory.TryWriteLong(dataAddress, addedItems);

                cursor = data;
                continue;
            }

            if (!TryAdd(cursor, 8, out var nextCursor))
                return false;

            if (tag == TagSkip)
            {
                if (data > (uint.MaxValue - nextCursor) / 8 ||
                    !TryAdd(nextCursor, data * 8, out cursor))
                {
                    return false;
                }

                continue;
            }

            cursor = nextCursor;
        }

        return false;
    }

    private static bool TryValidateBatchItems(
        IGraphicsMemory memory,
        uint batchItems)
    {
        if (batchItems == 0 ||
            !IsEvenAddress(batchItems) ||
            !ProbeRange(memory, batchItems, 8))
        {
            return false;
        }

        // Walk only the command-buffer envelope here.  Individual commands
        // are replayed by VideoControl at MakeVPort, but malformed alignment,
        // TAG_SKIP arithmetic, continuations, or a bounded-cycle overflow
        // must never be installed into ColorMap.BatchItems.
        return TryCollectVideoControlTags(
            memory,
            batchItems,
            new List<VideoControlItem>());
    }

    internal static uint GetNativeViewPortModeId(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        bool ntsc = false,
        bool requireAssociation = false)
    {
        if (!TryAdd(viewPort, (uint)GraphicsLayouts.ViewPortColorMap, out var mapAddress) ||
            !memory.TryReadLong(mapAddress, out var map))
        {
            return GraphicsModeIds.Invalid;
        }

        // A ViewPort may be assembled before Intuition attaches a ColorMap.
        // In that state the public Modes word is still sufficient to identify
        // the native OCS/ECS display record.  Keep a nonzero but foreign map
        // strict so CyberGraphX/native providers retain ownership of it.
        if (map != 0 && !registry.Maps.ContainsKey(map))
            return GraphicsModeIds.Invalid;

        var forcedModeId = 0u;
        if (map != 0)
        {
            if (!registry.Maps.TryGetValue(map, out var allocation) ||
                !TryAdd(map, (uint)GraphicsLayouts.ColorMapModeId, out var modeIdAddress) ||
                !memory.TryReadLong(modeIdAddress, out forcedModeId))
            {
                return GraphicsModeIds.Invalid;
            }

            var modeOverrideSet = allocation.ModeIdOverrideSet;
            if (modeOverrideSet || forcedModeId != 0)
            {
                return IsValidViewPortModeId(forcedModeId)
                    ? forcedModeId
                    : GraphicsModeIds.Invalid;
            }
        }

        var normalModeId = 0u;
        if (map != 0 &&
            (!TryAdd(map, (uint)GraphicsLayouts.ColorMapNormalDisplayInfo, out var normalAddress) ||
             !memory.TryReadLong(normalAddress, out normalModeId)))
        {
            return GraphicsModeIds.Invalid;
        }

        if (map != 0 && normalModeId != 0)
            return GraphicsDisplayDatabase.TryResolveDisplayInfoHandle(
                       normalModeId,
                       out var resolvedNormalModeId)
                ? resolvedNormalModeId
                : GraphicsModeIds.Invalid;

        // CoerceMode follows the resolved source association, not Modes alone.
        // Keep the pre-existing public GetVPModeID policy separate pending its
        // own compatibility correction and dependent-caller audit.
        if (requireAssociation)
            return GraphicsModeIds.Invalid;

        if (!TryAdd(viewPort, (uint)GraphicsLayouts.ViewPortModes, out var modesAddress) ||
            !memory.TryReadWord(modesAddress, out var modes) ||
            !GraphicsModeIds.TryGetNativeModeId(modes, ntsc, out var nativeModeId))
        {
            return GraphicsModeIds.Invalid;
        }

        return nativeModeId;
    }

    private static bool IsValidViewPortModeId(uint modeId)
    {
        // INVALID_ID is a query sentinel, not a display mode. The default
        // monitor's zero ModeID is nevertheless a valid native record and
        // maps to the reserved nonzero DisplayInfo handle.
        if (modeId == GraphicsModeIds.Invalid)
            return false;

        var handle = GraphicsDisplayDatabase.FindDisplayInfo(modeId);
        return modeId == GraphicsModeIds.DefaultMonitor
            ? handle == GraphicsDisplayDatabase.DefaultModeHandle
            : handle == modeId;
    }

    /// <summary>
    /// Determines whether the native GetVPModeID fallback owns the viewport's
    /// ColorMap association. A null ColorMap is valid while a viewport is
    /// being assembled. A non-null map belongs to this implementation only
    /// when it is present in the allocation registry; foreign or unreadable
    /// associations must remain available to a provider such as CyberGraphX.
    /// </summary>
    internal static bool ValidateNativeViewPortColorMap(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort)
    {
        if (!TryAdd(viewPort, (uint)GraphicsLayouts.ViewPortColorMap, out var mapAddress) ||
            !memory.TryReadLong(mapAddress, out var map))
        {
            return false;
        }

        if (map == 0)
            return true;

        // Registry membership alone is not enough to claim the association:
        // a native caller may have retained an address after its backing
        // allocation was truncated or unmapped.  Probe the complete public
        // ColorMap envelope before the portable fallback interprets any
        // fields or publishes the View.
        return registry.Maps.TryGetValue(map, out var allocation) &&
            allocation.StructBytes >= (uint)GraphicsLayouts.ColorMapSize &&
            ProbeRange(memory, map, (uint)GraphicsLayouts.ColorMapSize);
    }

    /// <summary>
    /// Validates the ColorMap ownership of every visible viewport linked from
    /// a View.  The resident display traversal consumes only the public
    /// Modes/Next fields of a VP_HIDE node, so its ColorMap is outside this
    /// portable publication envelope and must not block an otherwise valid
    /// display chain.  The display-provider path may consume foreign maps,
    /// but a direct portable publication must not reinterpret visible foreign
    /// state as native OCS/ECS state.
    /// </summary>
    internal static bool ValidateNativeViewColorMaps(
        IGraphicsMemory memory,
        Registry registry,
        uint view)
    {
        if (view == 0)
            return true;

        if (!TryAdd(view, (uint)GraphicsLayouts.ViewViewPort, out var viewPortAddress) ||
            !memory.TryReadLong(viewPortAddress, out var viewPort))
        {
            return false;
        }

        for (var index = 0; index < 64 && viewPort != 0; index++)
        {
            var currentViewPort = viewPort;
            if ((currentViewPort & 1u) != 0 ||
                !TryAdd(currentViewPort, (uint)GraphicsLayouts.ViewPortModes, out var modesAddress) ||
                !memory.TryReadWord(modesAddress, out var modes) ||
                !TryAdd(currentViewPort, (uint)GraphicsLayouts.ViewPortNext, out var nextAddress) ||
                !memory.TryReadLong(nextAddress, out var nextViewPort))
            {
                return false;
            }

            // VP_HIDE is a display-time filter.  Match ValidateViewForLoad:
            // the resident loop does not dereference ColorMap, RasInfo, or
            // any other payload for a hidden node, so malformed/foreign map
            // state there must not steal a valid portable LoadView call.
            if ((modes & GraphicsModeIds.ViewPortHidden) == 0 &&
                !ValidateNativeViewPortColorMap(memory, registry, currentViewPort))
            {
                return false;
            }

            viewPort = nextViewPort;
        }

        return viewPort == 0;
    }

    private static bool TryApplySpriteResolutionTag(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tag,
        uint data)
    {
        var offset = tag switch
        {
            VTagSpriteResnSet or VTagSpriteResnGet => GraphicsLayouts.ColorMapSpriteResolution,
            VTagDefSpriteResnSet or VTagDefSpriteResnGet => GraphicsLayouts.ColorMapSpriteResDefault,
            _ => -1
        };
        if (offset < 0)
            return false;

        if (tag == VTagSpriteResnGet || tag == VTagDefSpriteResnGet)
        {
            return data != 0 &&
                TryAdd(allocation.Address, (uint)offset, out var valueAddress) &&
                memory.TryReadByte(valueAddress, out var value) &&
                memory.TryWriteLong(data, value == byte.MaxValue ? uint.MaxValue : value);
        }

        if (data != 0 && data != 1 && data != 2 && data != 3 && data != uint.MaxValue)
            return false;
        return TryAdd(allocation.Address, (uint)offset, out var writeAddress) &&
            memory.TryWriteByte(writeAddress, data == uint.MaxValue ? byte.MaxValue : (byte)data);
    }

    private static bool TryApplyColorBaseTag(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tag,
        uint data)
    {
        var (offset, isSet, isGet) = tag switch
        {
            VTagPf1BaseSet => (GraphicsLayouts.ColorMapBitPlane0Base, true, false),
            VTagPf2BaseSet => (GraphicsLayouts.ColorMapBitPlane1Base, true, false),
            VTagSpEvenBaseSet => (GraphicsLayouts.ColorMapSpriteBaseEven, true, false),
            VTagSpOddBaseSet => (GraphicsLayouts.ColorMapSpriteBaseOdd, true, false),
            VTagPf1BaseGet => (GraphicsLayouts.ColorMapBitPlane0Base, false, true),
            VTagPf2BaseGet => (GraphicsLayouts.ColorMapBitPlane1Base, false, true),
            VTagSpEvenBaseGet => (GraphicsLayouts.ColorMapSpriteBaseEven, false, true),
            VTagSpOddBaseGet => (GraphicsLayouts.ColorMapSpriteBaseOdd, false, true),
            _ => (-1, false, false)
        };
        if (offset < 0)
            return false;

        if (isSet)
        {
            return data <= ushort.MaxValue &&
                TryAdd(allocation.Address, (uint)offset, out var writeAddress) &&
                memory.TryWriteWord(writeAddress, (ushort)data);
        }

        return isGet && data != 0 &&
            TryAdd(allocation.Address, (uint)offset, out var valueAddress) &&
            memory.TryReadWord(valueAddress, out var value) &&
            memory.TryWriteLong(data, value);
    }

    private static bool TryApplyChromaPenTag(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tag,
        uint data)
    {
        if (tag == VTagChromaPenSet)
        {
            if (data >= allocation.Count)
                return false;
            allocation.ChromaPen = data;
            return true;
        }

        if (tag == VTagChromaPenClr)
        {
            allocation.ChromaPen = 0;
            return true;
        }

        return tag == VTagChromaPenGet && data != 0 && memory.TryWriteLong(data, allocation.ChromaPen);
    }

    private static bool TryApplySpritePriorityTag(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tag,
        uint data)
    {
        if (tag == VTagPf1ToSpritePriSet || tag == VTagPf2ToSpritePriSet)
        {
            if (data > 1)
                return false;
            if (tag == VTagPf1ToSpritePriSet)
                allocation.Pf1ToSpritePriority = data;
            else
                allocation.Pf2ToSpritePriority = data;
            return true;
        }

        if (tag == VTagPf1ToSpritePriGet || tag == VTagPf2ToSpritePriGet)
        {
            return data != 0 && memory.TryWriteLong(
                data,
                tag == VTagPf1ToSpritePriGet
                    ? allocation.Pf1ToSpritePriority
                    : allocation.Pf2ToSpritePriority);
        }

        return false;
    }

    private static bool TryApplyFlagTag(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tag,
        uint data)
    {
        if (tag == VTagChromaKeySet || tag == VTagChromaKeyClr || tag == VTagChromaKeyGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagChromaKeySet, VTagChromaKeyClr, VTagChromaKeyGet, ColorMapFlagChromaKey, data);
        if (tag == VTagBitPlaneKeySet || tag == VTagBitPlaneKeyClr || tag == VTagBitPlaneKeyGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagBitPlaneKeySet, VTagBitPlaneKeyClr, VTagBitPlaneKeyGet, ColorMapFlagBitPlaneKey, data);
        if (tag == VTagBorderBlankSet || tag == VTagBorderBlankClr || tag == VTagBorderBlankGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagBorderBlankSet, VTagBorderBlankClr, VTagBorderBlankGet, ColorMapFlagBorderBlanking, data);
        if (tag == VTagBorderNoTransSet || tag == VTagBorderNoTransClr || tag == VTagBorderNoTransGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagBorderNoTransSet, VTagBorderNoTransClr, VTagBorderNoTransGet, ColorMapFlagBorderNoTransparency, data);
        if (tag == VTagBatchCmSet || tag == VTagBatchCmClr || tag == VTagBatchCmGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagBatchCmSet, VTagBatchCmClr, VTagBatchCmGet, ColorMapFlagVideoControlBatch, data);
        if (tag == VTagUserClipSet || tag == VTagUserClipClr || tag == VTagUserClipGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagUserClipSet, VTagUserClipClr, VTagUserClipGet, ColorMapFlagUserCopperClip, data);
        if (tag == VTagBorderSpriteSet || tag == VTagBorderSpriteClr || tag == VTagBorderSpriteGet)
            return ApplyByteFlag(memory, allocation, GraphicsLayouts.ColorMapFlags, tag, VTagBorderSpriteSet, VTagBorderSpriteClr, VTagBorderSpriteGet, ColorMapFlagBorderSprites, data);

        if (tag == VTagFullPaletteSet || tag == VTagFullPaletteGet || tag == VTagFullPaletteClr)
            return ApplyAuxFlag(memory, allocation, tag, VTagFullPaletteSet, VTagFullPaletteGet, ColorMapAuxFullPalette, data, invert: false, clear: VTagFullPaletteClr);
        if (tag == VcIntermediateUpdate || tag == VcIntermediateUpdateQuery)
            return ApplyAuxFlag(memory, allocation, tag, VcIntermediateUpdate, VcIntermediateUpdateQuery, ColorMapAuxNoIntermediateUpdate, data, invert: true);
        if (tag == VcNoColorLoad || tag == VcNoColorLoadQuery)
            return ApplyAuxFlag(memory, allocation, tag, VcNoColorLoad, VcNoColorLoadQuery, ColorMapAuxNoColorLoad, data, invert: false);
        if (tag == VcDualPfDisable || tag == VcDualPfDisableQuery)
            return ApplyAuxFlag(memory, allocation, tag, VcDualPfDisable, VcDualPfDisableQuery, ColorMapAuxDualPfDisable, data, invert: false);

        return false;
    }

    private static bool ApplyByteFlag(
        IGraphicsMemory memory,
        Allocation allocation,
        int offset,
        uint tag,
        uint set,
        uint clear,
        uint get,
        byte mask,
        uint data)
    {
        if (!memory.TryReadByte(allocation.Address + (uint)offset, out var value))
            return false;
        if (tag == get)
            return data != 0 && memory.TryWriteLong(data, (value & mask) != 0 ? 1u : 0u);
        if (tag == set)
            value |= mask;
        else if (tag == clear)
            value &= (byte)~mask;
        else
            return false;
        return memory.TryWriteByte(allocation.Address + (uint)offset, value);
    }

    private static bool ApplyAuxFlag(
        IGraphicsMemory memory,
        Allocation allocation,
        uint tag,
        uint set,
        uint query,
        byte mask,
        uint data,
        bool invert,
        uint clear = uint.MaxValue)
    {
        if (!memory.TryReadByte(allocation.Address + (uint)GraphicsLayouts.ColorMapAuxFlags, out var value))
            return false;
        if (tag == query)
            return data != 0 && memory.TryWriteLong(data, ((value & mask) != 0) ^ invert ? 1u : 0u);
        if (tag != set && tag != clear)
            return false;
        var enabled = tag == clear ? false : data != 0;
        if (invert)
            enabled = !enabled;
        if (enabled)
            value |= mask;
        else
            value &= (byte)~mask;
        return memory.TryWriteByte(allocation.Address + (uint)GraphicsLayouts.ColorMapAuxFlags, value);
    }

    internal static uint GetRgb4(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        int index)
    {
        // GetRGB4 is a single legacy ColorTable read.  The native routine
        // validates only the ColorMap count and ColorTable entry; the V36
        // LowColorBits extension and ColorMap.Type are not part of this
        // result.  Keep those optional fields out of the ownership envelope
        // so an otherwise readable high-nibble entry is not handed to a
        // provider merely because an unrelated extension word is unmapped.
        if (index < 0 ||
            !registry.Maps.TryGetValue(map, out var allocation) ||
            !TryAdd(allocation.Address, (uint)GraphicsLayouts.ColorMapCount, out var countAddress) ||
            !memory.TryReadWord(countAddress, out var guestCount) ||
            (uint)index >= guestCount ||
            (uint)index >= allocation.Count ||
            !TryAdd(allocation.ColorTable, (uint)index * 2, out var colorAddress)
            || !memory.TryReadWord(colorAddress, out var value))
            return uint.MaxValue;

        return (uint)(value & 0x0FFF);
    }

    internal static bool GetRgb32(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint first,
        uint count,
        uint table)
    {
        if (count > MaxEntries || first > MaxEntries || count > MaxEntries - first)
            return false;

        // A zero-count query has no result envelope to publish.  Keep the
        // classic no-op independent of the caller's table sentinel: requiring
        // an even, readable ULONG table here would make GetRGB32(…, 0, 0)
        // claim a provider/native address-error form even though no guest
        // memory is consumed.  Non-empty requests retain the normal table
        // alignment and rollback contract below.
        if (count == 0)
            return true;

        if (table == 0)
            return false;

        // The RGB32 result table is a guest ULONG[] written through native
        // long-word stores.  A byte-addressable host memory adapter may make
        // an odd table appear readable, but a 68000 caller cannot legally
        // publish this structure at an odd address.
        if ((table & 1u) != 0)
            return false;

        var highValues = new ushort[(int)count];
        var lowValues = new ushort[(int)count];
        for (uint index = 0; index < count; index++)
        {
            if (!TryGetEntry(memory, registry, map, first + index, out _, out var colorAddress, out var lowAddress) ||
                !memory.TryReadWord(colorAddress, out var high) ||
                !TryReadLowColorWord(memory, lowAddress, out var low))
            {
                return false;
            }

            highValues[(int)index] = high;
            lowValues[(int)index] = low;
        }

        var outputBytes = (ulong)count * 12UL;
        if (outputBytes == 0 || outputBytes > uint.MaxValue ||
            !TryAdd(table, (uint)(outputBytes - 1), out _))
        {
            return false;
        }

        var original = new byte[(int)outputBytes];
        for (var offset = 0u; offset < outputBytes; offset++)
        {
            if (!memory.TryReadByte(table + offset, out original[(int)offset]))
                return false;
        }

        for (uint index = 0; index < count; index++)
        {
            if (!TryAdd(table, index * 12, out var outAddress) ||
                !TryWriteRgb32(memory, outAddress, highValues[(int)index], lowValues[(int)index]))
            {
                for (var restore = 0u; restore < outputBytes; restore++)
                    _ = memory.TryWriteByte(table + restore, original[(int)restore]);

                return false;
            }
        }

        return true;
    }

    internal static bool LoadRgb4(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        uint colors,
        short count,
        IGraphicsPaletteBackend? palette,
        IGraphicsTimedPaletteBackend? timedPalette = null,
        long cycle = 0)
    {
        if (count <= 0)
            return true;

        if (colors == 0)
            return false;

        // LoadRGB4 consumes a guest UWORD[] through native word reads.  Keep
        // an odd non-null table available to the resident/provider path rather
        // than letting a byte-addressable host memory adapter hide a 68000
        // address error.
        if ((colors & 1u) != 0)
            return false;

        var hasColorMap = TryReadColorMap(memory, viewPort, out var map);

        var limit = Math.Min((uint)count, MaxEntries);
        var values = new ushort[(int)limit];
        for (uint index = 0; index < limit; index++)
        {
            if (!TryAdd(colors, index * 2, out var colorAddress) ||
                !memory.TryReadWord(colorAddress, out values[(int)index]))
                return false;
        }

        if (hasColorMap)
        {
            var updates = new List<ColorMapUpdate>((int)limit);
            for (uint index = 0; index < limit; index++)
            {
                var value = values[(int)index];
                if (!TryGetEntry(memory, registry, map, index, out _, out var colorAddress, out var lowAddress) ||
                    !memory.TryReadWord(colorAddress, out var oldHigh) ||
                    !TryReadLowColorWord(memory, lowAddress, out var oldLow))
                    return false;

                updates.Add(new ColorMapUpdate(
                    colorAddress,
                    lowAddress,
                    oldHigh,
                    oldLow,
                    (ushort)((oldHigh & 0xF000) | (value & 0x0FFF)),
                    0));
            }

            if (!TryApplyColorMapUpdates(memory, updates))
                return false;
        }

        if (timedPalette is not null)
            timedPalette.LoadRgb4(viewPort, colors, count, cycle);
        else
            palette?.LoadRgb4(viewPort, colors, count);
        return true;
    }

    internal static bool LoadRgb32(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        uint table,
        IGraphicsPaletteBackend? palette,
        IGraphicsTimedPaletteBackend? timedPalette = null,
        IGraphicsRgb32PaletteBackend? rgb32Palette = null,
        IGraphicsTimedRgb32PaletteBackend? timedRgb32Palette = null,
        long cycle = 0)
    {
        if (table == 0)
            return true;

        // Each descriptor and RGB component is read as a native LONG.  Keep
        // an odd non-null table available to the resident/provider path
        // instead of allowing host byte access to hide a 68000 address error.
        if ((table & 1u) != 0)
            return false;

        var hasColorMap = TryReadColorMap(memory, viewPort, out var map);
        var values = new List<PaletteValue>();

        var cursor = table;
        for (var records = 0; records < 4096; records++)
        {
            if (!memory.TryReadLong(cursor, out var descriptor))
                return false;

            var count = descriptor >> 16;
            var first = descriptor & 0xFFFF;
            if (count == 0)
            {
                if (hasColorMap)
                {
                    var updates = new List<ColorMapUpdate>(values.Count);
                    foreach (var value in values)
                    {
                        if (!TryGetEntry(memory, registry, map, value.Index, out _, out var colorAddress, out var lowAddress) ||
                            !memory.TryReadWord(colorAddress, out var oldHigh) ||
                            !TryReadLowColorWord(memory, lowAddress, out var oldLow))
                            return false;

                        var high = (ushort)((oldHigh & 0xF000) |
                                            ((value.Red >> 4) << 8) |
                                            ((value.Green >> 4) << 4) |
                                            (value.Blue >> 4));
                        var low = (ushort)(((value.Red & 0x0F) << 8) |
                                           ((value.Green & 0x0F) << 4) |
                                           (value.Blue & 0x0F));
                        updates.Add(new ColorMapUpdate(
                            colorAddress,
                            lowAddress,
                            oldHigh,
                            oldLow,
                            high,
                            low));
                    }

                    if (!TryApplyColorMapUpdates(memory, updates))
                        return false;
                }

                foreach (var value in values)
                {
                    if (timedRgb32Palette is not null)
                    {
                        timedRgb32Palette.SetRgb32(
                            viewPort,
                            value.Index,
                            value.Red,
                            value.Green,
                            value.Blue,
                            cycle);
                    }
                    else if (rgb32Palette is not null)
                    {
                        rgb32Palette.SetRgb32(
                            viewPort,
                            value.Index,
                            value.Red,
                            value.Green,
                            value.Blue);
                    }
                    else if (timedPalette is not null)
                    {
                        timedPalette.SetRgb4(
                            viewPort,
                            unchecked((short)value.Index),
                            (byte)(value.Red >> 4),
                            (byte)(value.Green >> 4),
                            (byte)(value.Blue >> 4),
                            cycle);
                    }
                    else
                    {
                        palette?.SetRgb4(
                            viewPort,
                            unchecked((short)value.Index),
                            (byte)(value.Red >> 4),
                            (byte)(value.Green >> 4),
                            (byte)(value.Blue >> 4));
                    }
                }

                return true;
            }
            if (count > MaxEntries || first > MaxEntries || count > MaxEntries - first)
                return false;

            if (!TryAdd(cursor, 4, out cursor))
                return false;
            for (uint index = 0; index < count; index++)
            {
                if (!memory.TryReadLong(cursor, out var red) ||
                    !TryAdd(cursor, 4, out var greenAddress) ||
                    !memory.TryReadLong(greenAddress, out var green) ||
                    !TryAdd(greenAddress, 4, out var blueAddress) ||
                    !memory.TryReadLong(blueAddress, out var blue))
                    return false;

                if ((uint)values.Count == MaxEntries)
                    return false;

                values.Add(new PaletteValue(
                    first + index,
                    (byte)(red >> 24),
                    (byte)(green >> 24),
                    (byte)(blue >> 24)));

                if (!TryAdd(blueAddress, 4, out cursor))
                    return false;
            }

        }

        return false;
    }

    internal static bool SetRgb4(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        int index,
        byte red,
        byte green,
        byte blue,
        IGraphicsPaletteBackend? palette,
        IGraphicsTimedPaletteBackend? timedPalette = null,
        long cycle = 0)
    {
        if (index < 0)
            return false;

        if (TryReadColorMap(memory, viewPort, out var map) &&
            !SetRgb4Cm(memory, registry, map, (uint)index, red, green, blue))
        {
            // A non-null viewport ColorMap is guest state, not an optional
            // hint.  Do not publish a host palette change when the portable
            // map is malformed or not owned by this graphics instance.
            return false;
        }
        if (timedPalette is not null)
        {
            timedPalette.SetRgb4(
                viewPort,
                (short)index,
                (byte)(red & 0x0F),
                (byte)(green & 0x0F),
                (byte)(blue & 0x0F),
                cycle);
        }
        else
        {
            palette?.SetRgb4(
                viewPort,
                (short)index,
                (byte)(red & 0x0F),
                (byte)(green & 0x0F),
                (byte)(blue & 0x0F));
        }
        return true;
    }

    internal static bool SetRgb32(
        IGraphicsMemory memory,
        Registry registry,
        uint viewPort,
        uint index,
        uint red,
        uint green,
        uint blue,
        IGraphicsPaletteBackend? palette,
        IGraphicsTimedPaletteBackend? timedPalette = null,
        IGraphicsRgb32PaletteBackend? rgb32Palette = null,
        IGraphicsTimedRgb32PaletteBackend? timedRgb32Palette = null,
        long cycle = 0)
    {
        if (TryReadColorMap(memory, viewPort, out var map) &&
            !SetRgb32Cm(memory, registry, map, index, red, green, blue))
        {
            return false;
        }

        if (timedRgb32Palette is not null)
        {
            timedRgb32Palette.SetRgb32(
                viewPort,
                index,
                (byte)(red >> 24),
                (byte)(green >> 24),
                (byte)(blue >> 24),
                cycle);
        }
        else if (rgb32Palette is not null)
        {
            rgb32Palette.SetRgb32(
                viewPort,
                index,
                (byte)(red >> 24),
                (byte)(green >> 24),
                (byte)(blue >> 24));
        }
        else if (timedPalette is not null)
        {
            timedPalette.SetRgb4(
                viewPort,
                (short)index,
                (byte)(red >> 28),
                (byte)(green >> 28),
                (byte)(blue >> 28),
                cycle);
        }
        else
        {
            palette?.SetRgb4(
                viewPort,
                (short)index,
                (byte)(red >> 28),
                (byte)(green >> 28),
                (byte)(blue >> 28));
        }
        return true;
    }

    private static bool TryInitializeMap(
        IGraphicsMemory memory,
        uint map,
        uint count,
        uint colors,
        uint lowColors)
        => memory.TryWriteByte(map + (uint)GraphicsLayouts.ColorMapType, ColorMapTypeV39) &&
           memory.TryWriteWord(map + (uint)GraphicsLayouts.ColorMapCount, (ushort)count) &&
           memory.TryWriteLong(map + (uint)GraphicsLayouts.ColorMapColorTable, colors) &&
           memory.TryWriteLong(map + (uint)GraphicsLayouts.ColorMapLowColorBits, lowColors) &&
           memory.TryWriteLong(map + (uint)GraphicsLayouts.ColorMapModeId, 0);

    private static bool TryClear(IGraphicsMemory memory, uint address, uint bytes)
    {
        for (uint offset = 0; offset < bytes; offset++)
        {
            if (!TryAdd(address, offset, out var current) || !memory.TryWriteByte(current, 0))
                return false;
        }

        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        uint bytes,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 || bytes == 0 || bytes > int.MaxValue ||
            address > uint.MaxValue - (bytes - 1))
        {
            return false;
        }

        original = new byte[(int)bytes];
        for (var offset = 0u; offset < bytes; offset++)
        {
            if (!memory.TryReadByte(address + offset, out original[(int)offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static bool TryCaptureFreshEnvelope(
        IGraphicsMemory memory,
        uint address,
        uint bytes,
        out byte[] original)
    {
        if (TrySnapshot(memory, address, bytes, out original))
            return true;

        // Allocator-owned public spans may be newly mapped but not yet
        // readable on a sparse host adapter. Preserve the historical
        // write-first admission for that fresh storage while still giving
        // rollback a deterministic zero image if a later publication fails.
        if (address == 0 || bytes == 0 || bytes > int.MaxValue ||
            address > uint.MaxValue - (bytes - 1))
        {
            original = Array.Empty<byte>();
            return false;
        }

        original = new byte[(int)bytes];
        return true;
    }

    private static void RestoreSnapshot(
        IGraphicsMemory memory,
        uint address,
        byte[] original)
    {
        if (address == 0 || original.Length == 0)
            return;

        for (var offset = original.Length - 1; offset >= 0; offset--)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }

    private static bool TryReadColorMap(IGraphicsMemory memory, uint viewPort, out uint map)
    {
        map = 0;
        return viewPort != 0 &&
            TryAdd(viewPort, (uint)GraphicsLayouts.ViewPortColorMap, out var address) &&
            memory.TryReadLong(address, out map) && map != 0;
    }

    private static bool TryGetEntry(
        IGraphicsMemory memory,
        Registry registry,
        uint map,
        uint index,
        out Allocation allocation,
        out uint colorAddress,
        out uint lowAddress)
    {
        allocation = null!;
        colorAddress = 0;
        lowAddress = 0;
        if (!registry.Maps.TryGetValue(map, out var found) || index >= found.Count ||
            !TryAdd(found.ColorTable, index * 2, out colorAddress) ||
            !TryAdd(found.Address, (uint)GraphicsLayouts.ColorMapType, out var typeAddress) ||
            !memory.TryReadByte(typeAddress, out var mapType))
            return false;

        allocation = found;

        if (!memory.TryReadWord(colorAddress, out _))
            return false;

        // Type V1.2 has no LowColorBits field.  Keep its optional address at
        // zero so setters and bulk-load transactions update only ColorTable;
        // V36/V39 still require the complete low-nibble entry envelope.
        if (mapType == 0)
            return true;

        return TryAdd(found.LowColorBits, index * 2, out lowAddress) &&
            memory.TryReadWord(lowAddress, out _);
    }

    private static bool TryReadLowColorWord(
        IGraphicsMemory memory,
        uint lowAddress,
        out ushort low)
    {
        low = 0;
        return lowAddress == 0 || memory.TryReadWord(lowAddress, out low);
    }

    private static bool TryApplyColorMapUpdates(
        IGraphicsMemory memory,
        IReadOnlyList<ColorMapUpdate> updates)
    {
        for (var index = 0; index < updates.Count; index++)
        {
            var update = updates[index];
            if (memory.TryWriteWord(update.ColorAddress, update.NewHigh) &&
                (update.LowAddress == 0 || memory.TryWriteWord(update.LowAddress, update.NewLow)))
                continue;

            for (var restore = index; restore >= 0; restore--)
            {
                var previous = updates[restore];
                RestoreWord(memory, previous.ColorAddress, previous.OldHigh);
                if (previous.LowAddress != 0)
                    RestoreWord(memory, previous.LowAddress, previous.OldLow);
            }

            return false;
        }

        return true;
    }

    private static void RestoreWord(
        IGraphicsMemory memory,
        uint address,
        ushort value)
    {
        // A failed guest WORD may have published only its first byte and the
        // adapter may reject the same WORD operation during rollback. Restore
        // both bytes independently so palette updates remain retryable.
        _ = memory.TryWriteByte(address, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 1u, (byte)value);
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

    private readonly record struct ColorMapUpdate(
        uint ColorAddress,
        uint LowAddress,
        ushort OldHigh,
        ushort OldLow,
        ushort NewHigh,
        ushort NewLow);

    private readonly record struct PaletteValue(
        uint Index,
        byte Red,
        byte Green,
        byte Blue);

    private static bool TryWriteRgb32(IGraphicsMemory memory, uint address, ushort high, ushort low)
    {
        var red = Expand((byte)(((high >> 8) & 0x0F) << 4 | ((low >> 8) & 0x0F)));
        var green = Expand((byte)(((high >> 4) & 0x0F) << 4 | ((low >> 4) & 0x0F)));
        var blue = Expand((byte)((high & 0x0F) << 4 | (low & 0x0F)));
        return TryAdd(address, 4, out var greenAddress) &&
            TryAdd(address, 8, out var blueAddress) &&
            memory.TryWriteLong(address, red) &&
            memory.TryWriteLong(greenAddress, green) &&
            memory.TryWriteLong(blueAddress, blue);
    }

    private static uint Expand(byte value) => (uint)value * 0x0101_0101u;

    private static int FindFree(Allocation allocation, int limit)
        => FindEligibleListHead(allocation, allocation.FreeHead, limit, free: true);

    private static int FindSharedColor(
        IGraphicsMemory memory,
        Allocation allocation,
        int limit,
        (byte Red, byte Green, byte Blue) desired)
    {
        var index = FindEligibleListHead(allocation, allocation.SharedHead, limit, free: false);
        for (var steps = 0; index >= 0 && steps < allocation.AllocationNext.Length; steps++)
        {
            if (TryReadColor8(memory, allocation, (uint)index, out var current) && current == desired)
                return index;

            index = FindEligibleListNext(
                allocation,
                allocation.AllocationNext[index],
                limit,
                free: false);
        }

        return -1;
    }

    private static int CountFree(Allocation allocation, int limit)
    {
        var count = 0;
        for (var index = 0; index < limit; index++)
        {
            if (allocation.RefCounts[index] == 0 && !allocation.Exclusive[index])
                count++;
        }

        return count;
    }

    private readonly record struct PenListSnapshot(
        int FreeHead,
        int SharedHead,
        int[] AllocationNext);

    private static PenListSnapshot CapturePenListState(Allocation allocation)
        => new(
            allocation.FreeHead,
            allocation.SharedHead,
            (int[])allocation.AllocationNext.Clone());

    private static void RestorePenListState(
        Allocation allocation,
        PenListSnapshot snapshot)
    {
        allocation.FreeHead = snapshot.FreeHead;
        allocation.SharedHead = snapshot.SharedHead;
        Array.Copy(snapshot.AllocationNext, allocation.AllocationNext, allocation.AllocationNext.Length);
    }

    private static bool RemoveFromFreeList(Allocation allocation, int entry)
        => RemoveFromList(allocation, entry, shared: false);

    private static bool RemoveFromSharedList(Allocation allocation, int entry)
        => RemoveFromList(allocation, entry, shared: true);

    private static bool RemoveFromList(Allocation allocation, int entry, bool shared)
    {
        if ((uint)entry >= (uint)allocation.AllocationNext.Length)
            return false;

        var head = shared ? allocation.SharedHead : allocation.FreeHead;
        var previous = -1;
        for (var steps = 0; head >= 0 && steps < allocation.AllocationNext.Length; steps++)
        {
            if (head == entry)
            {
                var next = allocation.AllocationNext[head];
                if (previous < 0)
                {
                    if (shared)
                        allocation.SharedHead = next;
                    else
                        allocation.FreeHead = next;
                }
                else
                {
                    allocation.AllocationNext[previous] = next;
                }

                allocation.AllocationNext[entry] = -1;
                return true;
            }

            previous = head;
            head = allocation.AllocationNext[head];
        }

        return false;
    }

    private static void AddToFreeList(Allocation allocation, int entry)
    {
        allocation.AllocationNext[entry] = allocation.FreeHead;
        allocation.FreeHead = entry;
    }

    private static void AddToSharedList(Allocation allocation, int entry)
    {
        allocation.AllocationNext[entry] = allocation.SharedHead;
        allocation.SharedHead = entry;
    }

    private static bool IsFreeEntry(Allocation allocation, int entry)
        => allocation.RefCounts[entry] == 0 && !allocation.Exclusive[entry];

    private static bool IsSharedEntry(Allocation allocation, int entry)
        => allocation.RefCounts[entry] != 0 && !allocation.Exclusive[entry];

    private static int FindEligibleListHead(
        Allocation allocation,
        int head,
        int limit,
        bool free)
    {
        for (var steps = 0; head >= 0 && steps < allocation.AllocationNext.Length; steps++)
        {
            if ((uint)head >= (uint)allocation.AllocationNext.Length)
                return -1;

            if (head < limit &&
                (free ? IsFreeEntry(allocation, head) : IsSharedEntry(allocation, head)))
                return head;

            head = allocation.AllocationNext[head];
        }

        return -1;
    }

    private static int FindEligibleListNext(
        Allocation allocation,
        int entry,
        int limit,
        bool free)
        => FindEligibleListHead(allocation, entry, limit, free);

    private static bool IsWithinBestPenTolerance(
        long distance,
        long toleranceSquared,
        int precision,
        int freeCount,
        int sharableCount)
    {
        // PRECISION_EXACT is the one mode that never relaxes a non-zero
        // distance, even when every sharable entry is occupied.
        if (precision == PrecisionExact && distance != 0)
            return false;

        if (freeCount < 0 || sharableCount <= 0)
            return false;

        // With no free entries the native comparison has a zero left-hand
        // side, so every non-exact precision accepts the nearest shared pen.
        if (freeCount == 0)
            return true;

        // pe_SharableColors is the highest sharable index, not a count.  The
        // native comparison is:
        //
        //   best_distance * pe_NFree <= precision^2 * pe_SharableColors
        //
        // Both operands are bounded by the ColorMap, but retain checked
        // guards so malformed guest state cannot wrap the comparison.
        var sharableColors = sharableCount - 1L;
        if (sharableColors < 0 || distance < 0 ||
            distance > long.MaxValue / freeCount ||
            sharableColors != 0 && toleranceSquared > long.MaxValue / sharableColors)
            return false;

        var left = distance * freeCount;
        var right = toleranceSquared * sharableColors;
        return left <= right;
    }

    private static bool TryReadColor8(
        IGraphicsMemory memory,
        Allocation allocation,
        uint index,
        out (byte Red, byte Green, byte Blue) color)
    {
        color = default;
        if (!TryAdd(allocation.ColorTable, index * 2, out var colorAddress) ||
            !TryAdd(allocation.Address, (uint)GraphicsLayouts.ColorMapType, out var typeAddress) ||
            !memory.TryReadByte(typeAddress, out var mapType) ||
            !memory.TryReadWord(colorAddress, out var high))
            return false;

        // V1.2 ColorMaps predate LowColorBits.  The native color_distance
        // helper deliberately ignores that table when Type == 0, even if a
        // readable extension happens to be present after the public fields.
        // V36/V39 maps retain the full 8-bit RGB value assembled from both
        // nibbles.
        var low = (ushort)0;
        if (mapType > 0)
        {
            if (!TryAdd(allocation.LowColorBits, index * 2, out var lowAddress) ||
                !memory.TryReadWord(lowAddress, out low))
            {
                return false;
            }
        }

        color = (
            (byte)(((high >> 8) & 0x0F) << 4 | ((low >> 8) & 0x0F)),
            (byte)(((high >> 4) & 0x0F) << 4 | ((low >> 4) & 0x0F)),
            (byte)((high & 0x0F) << 4 | (low & 0x0F)));
        return true;
    }

    private static bool TryFindNearestSharable(
        IGraphicsMemory memory,
        Allocation allocation,
        int sharableCount,
        uint red,
        uint green,
        uint blue,
        out int best,
        out long bestDistance)
    {
        best = -1;
        bestDistance = long.MaxValue;
        var target = ((int)(red >> 24), (int)(green >> 24), (int)(blue >> 24));
        var index = FindEligibleListHead(
            allocation,
            allocation.SharedHead,
            sharableCount,
            free: false);
        for (var steps = 0; index >= 0 && steps < allocation.AllocationNext.Length; steps++)
        {
            if (index < sharableCount && IsSharedEntry(allocation, index))
            {
                // A shared entry is part of the observable palette state.  A
                // failed color read is therefore a malformed shared entry,
                // not an entry that can be skipped while allocating another
                // pen.
                if (!TryReadColor8(memory, allocation, (uint)index, out var color))
                    return false;

                var dr = target.Item1 - color.Item1;
                var dg = target.Item2 - color.Item2;
                var db = target.Item3 - color.Item3;
                var distance = (long)dr * dr + (long)dg * dg + (long)db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            index = FindEligibleListNext(
                allocation,
                allocation.AllocationNext[index],
                sharableCount,
                free: false);
        }

        // A valid shared-list walk may legitimately find no shared entry.
        // The caller uses best == -1 to select ObtainPen(-1); false is
        // reserved for malformed guest color state.
        return true;
    }

    private static bool TryReadSharableCount(
        IGraphicsMemory memory,
        Allocation allocation,
        out int count)
    {
        count = 0;
        if (!TryReadSharableColors(memory, allocation, out var sharableColors) ||
            sharableColors >= allocation.Count)
        {
            return false;
        }

        // The public V39 field stores the highest sharable pen index, not a
        // count.  A value of N therefore exposes entries 0..N inclusive.
        count = sharableColors + 1;
        return true;
    }

    private static bool TryReadSharableColors(
        IGraphicsMemory memory,
        Allocation allocation,
        out ushort sharableColors)
    {
        sharableColors = 0;
        return allocation.PaletteExtra != 0 &&
               TryAdd(
                   allocation.PaletteExtra,
                   (uint)GraphicsLayouts.PaletteExtraSharableColors,
                   out var address) &&
               memory.TryReadWord(address, out sharableColors);
    }

    private static bool TryPreparePaletteExtra(
        IGraphicsMemory memory,
        Allocation allocation,
        out int sharableCount)
    {
        sharableCount = 0;
        if (!TryReadSharableColors(memory, allocation, out var sharableColors) ||
            sharableColors >= allocation.Count ||
            !TryValidatePaletteExtra(
                memory,
                allocation,
                sharableColors,
                out var needsResync))
        {
            return false;
        }

        // pe_SharableColors is a private field, but native callers may tune
        // it to a narrower viewport-visible span.  If every other guest
        // sidecar field still matches the last host publication, normalize
        // the derived lists for that new limit before proceeding.  Any other
        // edit is foreign/corrupt state and remains available to native code.
        if (needsResync && !SyncPaletteExtra(memory, allocation))
            return false;

        return TryReadSharableCount(memory, allocation, out sharableCount);
    }

    internal static bool IsPaletteExtraStateSafe(
        IGraphicsMemory memory,
        Registry registry,
        uint map)
    {
        if (!registry.Maps.TryGetValue(map, out var allocation))
            return false;

        // A map without PaletteExtra is a valid owned allocation; pen vectors
        // will return their documented failure result without claiming a
        // sharing envelope that does not exist.
        if (allocation.PaletteExtra == 0)
            return true;

        return TryReadSharableColors(memory, allocation, out var sharableColors) &&
               sharableColors < allocation.Count &&
               TryValidatePaletteExtra(memory, allocation, sharableColors, out _);
    }

    private static bool TryValidatePaletteExtra(
        IGraphicsMemory memory,
        Allocation allocation,
        ushort currentSharableColors,
        out bool needsResync)
    {
        needsResync = false;
        if (allocation.PaletteExtra == 0 ||
            allocation.PaletteRefCount == 0 ||
            allocation.PaletteAllocList == 0 ||
            allocation.PaletteViewPort == 0 ||
            allocation.PublishedSharableColors < 0 ||
            allocation.PublishedSharableColors >= allocation.Count ||
            (allocation.PaletteExtra & 1u) != 0 ||
            (allocation.PaletteRefCount & 1u) != 0 ||
            (allocation.PaletteAllocList & 1u) != 0 ||
            (allocation.PaletteViewPort & 1u) != 0 ||
            !ProbeRange(memory, allocation.PaletteExtra, (uint)GraphicsLayouts.PaletteExtraSize) ||
            !ProbeRange(memory, allocation.PaletteRefCount, checked(allocation.Count * 2)) ||
            !ProbeRange(memory, allocation.PaletteAllocList, checked(allocation.Count * 2)) ||
            !ProbeRange(memory, allocation.PaletteViewPort, (uint)GraphicsLayouts.ViewPortSize))
        {
            return false;
        }

        // PaletteExtra embeds a public Exec SignalSemaphore.  Pen-sharing
        // vectors may only claim the sidecar while that prefix still has its
        // native node type and a coherent owner/nesting pair.  Queue contents
        // are scheduler-owned and may legitimately be non-empty, so validate
        // only the fields whose invariant is local to this portable boundary.
        if (!TryReadSemaphoreState(memory, allocation))
            return false;

        if (!TryReadLongAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraRefCount,
                out var refCount) ||
            refCount != allocation.PaletteRefCount ||
            !TryReadLongAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraAllocList,
                out var allocList) ||
            allocList != allocation.PaletteAllocList ||
            !TryReadLongAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraViewPort,
                out var viewPort) ||
            viewPort != allocation.PaletteViewPort)
        {
            return false;
        }

        var expectedSharableColors = allocation.PublishedSharableColors;
        if (!TryReadPaletteListState(
                memory,
                allocation,
                expectedSharableColors,
                out var expectedFirstFree,
                out var expectedFreeCount,
                out var expectedFirstShared,
                out var expectedSharedCount,
                out var expectedNext))
        {
            return false;
        }

        if (!TryReadWordAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraFirstFree,
                out var firstFree) ||
            firstFree != EncodePenListIndex(expectedFirstFree) ||
            !TryReadWordAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraNFree,
                out var freeCount) ||
            freeCount != expectedFreeCount ||
            !TryReadWordAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraFirstShared,
                out var firstShared) ||
            firstShared != EncodePenListIndex(expectedFirstShared) ||
            !TryReadWordAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraNShared,
                out var sharedCount) ||
            sharedCount != expectedSharedCount)
        {
            return false;
        }

        for (var index = 0; index < allocation.Count; index++)
        {
            var expectedRefCount = allocation.Exclusive[index]
                ? (byte)0
                : allocation.RefCounts[index];
            if (!TryReadWordAt(
                    memory,
                    allocation.PaletteRefCount,
                    index * 2,
                    out var refWord) ||
                refWord != expectedRefCount ||
                !TryReadWordAt(
                    memory,
                    allocation.PaletteAllocList,
                    index * 2,
                    out var nextWord) ||
                nextWord != EncodePenListIndex(expectedNext[index]))
            {
                return false;
            }
        }

        needsResync = currentSharableColors != expectedSharableColors;
        return true;
    }

    private static bool TryReadPaletteListState(
        IGraphicsMemory memory,
        Allocation allocation,
        int sharableColors,
        out int firstFree,
        out ushort freeCount,
        out int firstShared,
        out ushort sharedCount,
        out int[] expectedNext)
    {
        firstFree = -1;
        freeCount = 0;
        firstShared = -1;
        sharedCount = 0;
        expectedNext = new int[allocation.AllocationNext.Length];
        Array.Fill(expectedNext, -1);

        if (sharableColors < 0 || sharableColors >= allocation.Count)
            return false;

        if (!TryBuildPaletteList(
                allocation,
                allocation.FreeHead,
                sharableColors + 1,
                free: true,
                expectedNext,
                out firstFree,
                out var freeSeen))
        {
            return false;
        }

        if (!TryBuildPaletteList(
                allocation,
                allocation.SharedHead,
                sharableColors + 1,
                free: false,
                expectedNext,
                out firstShared,
                out var sharedSeen))
        {
            return false;
        }

        for (var index = 0; index < allocation.Count; index++)
        {
            if (freeSeen[index] && sharedSeen[index])
                return false;

            if (index <= sharableColors)
            {
                if (IsFreeEntry(allocation, index))
                    freeCount++;
                else if (IsSharedEntry(allocation, index))
                    sharedCount++;
            }
        }

        return true;
    }

    private static bool TryBuildPaletteList(
        Allocation allocation,
        int rawHead,
        int limit,
        bool free,
        int[] expectedNext,
        out int normalizedHead,
        out bool[] seen)
    {
        normalizedHead = -1;
        seen = new bool[allocation.AllocationNext.Length];
        var rawSeen = new bool[allocation.AllocationNext.Length];
        var raw = rawHead;
        for (var steps = 0; raw >= 0; steps++)
        {
            if (steps >= allocation.AllocationNext.Length ||
                (uint)raw >= (uint)allocation.AllocationNext.Length ||
                rawSeen[raw])
            {
                return false;
            }

            rawSeen[raw] = true;
            raw = allocation.AllocationNext[raw];
        }

        normalizedHead = FindEligibleListHead(allocation, rawHead, limit, free);
        var current = normalizedHead;
        while (current >= 0)
        {
            if ((uint)current >= (uint)allocation.AllocationNext.Length ||
                current >= limit ||
                seen[current] ||
                (free ? !IsFreeEntry(allocation, current) : !IsSharedEntry(allocation, current)))
            {
                return false;
            }

            seen[current] = true;
            var next = FindEligibleListHead(
                allocation,
                allocation.AllocationNext[current],
                limit,
                free);
            expectedNext[current] = next;
            current = next;
        }

        return true;
    }

    private static bool TryReadSemaphoreState(
        IGraphicsMemory memory,
        Allocation allocation)
    {
        if (allocation.PaletteExtra == 0 ||
            !TryAdd(
                allocation.PaletteExtra,
                (uint)GraphicsLayouts.PaletteExtraSemaphoreNodeType,
                out var typeAddress) ||
            !memory.TryReadByte(typeAddress, out var nodeType) ||
            nodeType != 15 ||
            !TryReadWordAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraSemaphoreNestCount,
                out var nestCount) ||
            !TryReadLongAt(
                memory,
                allocation.PaletteExtra,
                GraphicsLayouts.PaletteExtraSemaphoreOwner,
                out var owner))
        {
            return false;
        }

        // Exec never publishes an owned semaphore with a zero nest count, or
        // a non-zero nest count without an owner.  Leave either impossible
        // state to the resident/provider boundary instead of repairing it.
        return (owner == 0) == (nestCount == 0);
    }

    private static bool TryReadWordAt(
        IGraphicsMemory memory,
        uint address,
        int offset,
        out ushort value)
    {
        value = 0;
        return TryAdd(address, checked((uint)offset), out var field) &&
               memory.TryReadWord(field, out value);
    }

    private static bool TryReadLongAt(
        IGraphicsMemory memory,
        uint address,
        int offset,
        out uint value)
    {
        value = 0;
        return TryAdd(address, checked((uint)offset), out var field) &&
               memory.TryReadLong(field, out value);
    }

    private static bool TryReadPaletteViewPort(
        IGraphicsMemory memory,
        Allocation allocation,
        out uint viewPort)
    {
        viewPort = 0;
        return allocation.PaletteExtra != 0 &&
            TryAdd(
                allocation.PaletteExtra,
                (uint)GraphicsLayouts.PaletteExtraViewPort,
                out var address) &&
            memory.TryReadLong(address, out viewPort) &&
            viewPort != 0;
    }

    private static bool TryObtainExistingShared(
        IGraphicsMemory memory,
        Allocation allocation,
        int index)
    {
        if ((uint)index >= allocation.Count || allocation.Exclusive[index] ||
            allocation.RefCounts[index] == 0 || allocation.RefCounts[index] == ushort.MaxValue)
            return false;

        allocation.RefCounts[index]++;
        if (!SyncPaletteExtra(memory, allocation))
        {
            allocation.RefCounts[index]--;
            return false;
        }
        return true;
    }

    private static bool SyncPaletteExtra(
        IGraphicsMemory memory,
        Allocation allocation,
        bool allowFreshSnapshots = false)
    {
        if (allocation.PaletteExtra == 0)
            return true;

        if (!TryReadSharableCount(memory, allocation, out var sharableCount))
            return false;

        var freeCount = 0;
        var sharedCount = 0;
        for (var index = 0; index < sharableCount; index++)
        {
            if (allocation.RefCounts[index] == 0 && !allocation.Exclusive[index])
            {
                freeCount++;
            }
            else if (!allocation.Exclusive[index])
            {
                sharedCount++;
            }
        }

        var firstFree = FindEligibleListHead(allocation, allocation.FreeHead, sharableCount, free: true);
        var firstShared = FindEligibleListHead(allocation, allocation.SharedHead, sharableCount, free: false);

        // The guest PaletteExtra and its two sidecar tables are a single
        // publication envelope.  A memory adapter may reject a WORD after
        // writing its first byte, so publishing the derived heads and lists
        // piecemeal can otherwise leave native callers observing a mixture of
        // the old and new pen state.  Capture the complete envelope before
        // the first write and restore it on every failure.  AttachPalExtra
        // starts with freshly allocated, write-first memory on some host
        // adapters; its opt-in path treats an unreadable fresh envelope as
        // zero-initialized rollback state, while established allocations must
        // remain readable and are declined without mutation.
        var originalPaletteExtra = Array.Empty<byte>();
        var originalRefCount = Array.Empty<byte>();
        var originalAllocList = Array.Empty<byte>();
        var sidecarBytes = checked(allocation.Count * 2);
        var snapshotsCaptured =
            (allowFreshSnapshots
                ? TryCaptureFreshEnvelope(
                    memory,
                    allocation.PaletteExtra,
                    (uint)GraphicsLayouts.PaletteExtraSize,
                    out originalPaletteExtra)
                : TrySnapshot(
                    memory,
                    allocation.PaletteExtra,
                    (uint)GraphicsLayouts.PaletteExtraSize,
                    out originalPaletteExtra)) &&
            (allowFreshSnapshots
                ? TryCaptureFreshEnvelope(
                    memory,
                    allocation.PaletteRefCount,
                    sidecarBytes,
                    out originalRefCount)
                : TrySnapshot(
                    memory,
                    allocation.PaletteRefCount,
                    sidecarBytes,
                    out originalRefCount)) &&
            (allowFreshSnapshots
                ? TryCaptureFreshEnvelope(
                    memory,
                    allocation.PaletteAllocList,
                    sidecarBytes,
                    out originalAllocList)
                : TrySnapshot(
                    memory,
                    allocation.PaletteAllocList,
                    sidecarBytes,
                    out originalAllocList));

        if (!snapshotsCaptured)
            return false;

        if (!memory.TryWriteWord(
                allocation.PaletteExtra + (uint)GraphicsLayouts.PaletteExtraFirstFree,
                EncodePenListIndex(firstFree)) ||
            !memory.TryWriteWord(
                allocation.PaletteExtra + (uint)GraphicsLayouts.PaletteExtraNFree,
                checked((ushort)freeCount)) ||
            !memory.TryWriteWord(
                allocation.PaletteExtra + (uint)GraphicsLayouts.PaletteExtraFirstShared,
                EncodePenListIndex(firstShared)) ||
            !memory.TryWriteWord(
                allocation.PaletteExtra + (uint)GraphicsLayouts.PaletteExtraNShared,
                checked((ushort)sharedCount)))
        {
            RestoreSnapshot(memory, allocation.PaletteExtra, originalPaletteExtra);
            RestoreSnapshot(memory, allocation.PaletteRefCount, originalRefCount);
            RestoreSnapshot(memory, allocation.PaletteAllocList, originalAllocList);
            return false;
        }

        for (var index = 0; index < allocation.Count; index++)
        {
            var guestRefCount = allocation.Exclusive[index] ? (ushort)0 : allocation.RefCounts[index];
            if (!memory.TryWriteWord(
                    allocation.PaletteRefCount + (uint)(index * 2),
                    guestRefCount) ||
                !memory.TryWriteWord(
                    allocation.PaletteAllocList + (uint)(index * 2),
                    ushort.MaxValue))
            {
                RestoreSnapshot(memory, allocation.PaletteExtra, originalPaletteExtra);
                RestoreSnapshot(memory, allocation.PaletteRefCount, originalRefCount);
                RestoreSnapshot(memory, allocation.PaletteAllocList, originalAllocList);
                return false;
            }
        }

        if (!WritePaletteChain(
                memory,
                allocation.PaletteAllocList,
                allocation,
                firstFree,
                sharableCount,
                free: true) ||
            !WritePaletteChain(
                memory,
                allocation.PaletteAllocList,
                allocation,
                firstShared,
                sharableCount,
                free: false))
        {
            RestoreSnapshot(memory, allocation.PaletteExtra, originalPaletteExtra);
            RestoreSnapshot(memory, allocation.PaletteRefCount, originalRefCount);
            RestoreSnapshot(memory, allocation.PaletteAllocList, originalAllocList);
            return false;
        }

        allocation.PublishedSharableColors = sharableCount - 1;
        return true;
    }

    private static ushort EncodePenListIndex(int index)
        => index < 0 ? ushort.MaxValue : checked((ushort)index);

    private static bool WritePaletteChain(
        IGraphicsMemory memory,
        uint allocationList,
        Allocation allocation,
        int head,
        int limit,
        bool free)
    {
        for (var steps = 0; head >= 0 && steps < allocation.AllocationNext.Length; steps++)
        {
            if (head >= limit ||
                (free ? !IsFreeEntry(allocation, head) : !IsSharedEntry(allocation, head)))
            {
                head = allocation.AllocationNext[head];
                continue;
            }

            var next = FindEligibleListNext(
                allocation,
                allocation.AllocationNext[head],
                limit,
                free);
            if (!TryAdd(allocationList, (uint)(head * 2), out var address) ||
                !memory.TryWriteWord(
                    address,
                    next < 0 ? ushort.MaxValue : checked((ushort)next)))
                return false;

            head = allocation.AllocationNext[head];
        }

        return head < 0;
    }

    private static bool TryReadBestPenTags(
        IGraphicsMemory memory,
        uint tags,
        out int precision,
        out bool failIfBad)
    {
        precision = PrecisionImage;
        failIfBad = false;
        var cursor = tags;
        for (var count = 0; count < 4096; count++)
        {
            // TagItem is two guest LONGs.  A permissive host memory adapter
            // may expose wrapped bytes at the top of the 32-bit address
            // space, but a 68000 cannot consume an item whose eight-byte
            // envelope crosses $FFFF_FFFF.  Decline before either field is
            // read so native/provider ownership remains available.
            if ((cursor & 1u) != 0 ||
                cursor > uint.MaxValue - 7u ||
                !memory.TryReadLong(cursor, out var tag) || !TryAdd(cursor, 4, out var dataAddress) ||
                !memory.TryReadLong(dataAddress, out var data))
                return false;
            if (tag == TagEnd)
                return true;

            if (tag == TagMore)
            {
                if (data == 0)
                    // Utility.library's NextTagItem treats a null
                    // TAG_MORE target as the end of the chain.  Keep the
                    // same clean termination here so a valid empty suffix
                    // does not turn an otherwise portable ObtainBestPenA
                    // request into a native/provider fallback.
                    return true;
                if ((data & 1u) != 0)
                    return false;
                cursor = data;
                continue;
            }

            // The next TagItem starts immediately after the current
            // tag/data pair.  Keep that cursor separate from dataAddress:
            // TAG_SKIP counts complete following TagItems, not bytes from
            // the data LONG.  Advancing from dataAddress would land four
            // bytes into the first non-skipped item.
            if (!TryAdd(cursor, 8, out var nextCursor))
                return false;

            if (tag == ObpPrecision)
            {
                precision = unchecked((int)data);
                if (!TryGetToleranceSquared(precision, out _))
                    return false;
            }
            if (tag == ObpFailIfBad)
                failIfBad = data != 0;
            if (tag == TagSkip)
            {
                if (data > (uint.MaxValue - nextCursor) / 8 ||
                    !TryAdd(nextCursor, data * 8, out cursor))
                    return false;
                continue;
            }

            cursor = nextCursor;
        }

        return false;
    }

    /// <summary>
    /// Checks the guest tag-list envelope without claiming the
    /// <c>ObtainBestPenA</c> vector.  Register adapters use this narrow probe
    /// so a malformed list can tail-chain to a native/provider owner instead
    /// of turning the portable operation's documented <c>-1</c> result into a
    /// compatibility claim.
    /// </summary>
    internal static bool IsBestPenTagListSafe(
        IGraphicsMemory memory,
        uint tags)
        => tags == 0 || TryReadBestPenTags(memory, tags, out _, out _);

    /// <summary>
    /// Stages VideoControl's guest-memory writes until every tag and its
    /// allocation-owned sidecar has succeeded.  Reads observe staged bytes,
    /// so a later tag sees the same state it would see on the native path.
    /// </summary>
    private sealed class GraphicsMemoryTransaction : IGraphicsMemory
    {
        private readonly IGraphicsMemory _backing;
        private readonly Dictionary<uint, byte> _writes = new();

        internal GraphicsMemoryTransaction(IGraphicsMemory backing)
        {
            _backing = backing;
        }

        public bool TryReadByte(uint address, out byte value)
        {
            if (_writes.TryGetValue(address, out value))
                return true;

            return _backing.TryReadByte(address, out value);
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            if (address > uint.MaxValue - 1u)
            {
                value = 0;
                return false;
            }

            if (!TryReadByte(address, out var high) ||
                !TryReadByte(address + 1u, out var low))
            {
                value = 0;
                return false;
            }

            value = (ushort)((high << 8) | low);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            value = 0;
            if (address > uint.MaxValue - 3u ||
                !TryReadByte(address, out var b0) ||
                !TryReadByte(address + 1u, out var b1) ||
                !TryReadByte(address + 2u, out var b2) ||
                !TryReadByte(address + 3u, out var b3))
            {
                return false;
            }

            value = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            // A staged write must still target mapped guest memory.  This
            // preserves the original IGraphicsMemory failure boundary while
            // avoiding any visible mutation before commit.
            if (!_writes.ContainsKey(address) && !_backing.TryReadByte(address, out _))
                return false;

            _writes[address] = value;
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
            => address <= uint.MaxValue - 1u &&
               TryWriteByte(address, (byte)(value >> 8)) &&
               TryWriteByte(address + 1u, (byte)value);

        public bool TryWriteLong(uint address, uint value)
            => address <= uint.MaxValue - 3u &&
               TryWriteByte(address, (byte)(value >> 24)) &&
               TryWriteByte(address + 1u, (byte)(value >> 16)) &&
               TryWriteByte(address + 2u, (byte)(value >> 8)) &&
               TryWriteByte(address + 3u, (byte)value);

        internal bool Commit()
        {
            if (_writes.Count == 0)
                return true;

            var originals = new Dictionary<uint, byte>(_writes.Count);
            foreach (var address in _writes.Keys)
            {
                if (!_backing.TryReadByte(address, out var original))
                    return false;
                originals[address] = original;
            }

            foreach (var pair in _writes)
            {
                if (_backing.TryWriteByte(pair.Key, pair.Value))
                    continue;

                foreach (var original in originals)
                    _ = _backing.TryWriteByte(original.Key, original.Value);
                _writes.Clear();
                return false;
            }

            _writes.Clear();
            return true;
        }

        internal void Rollback() => _writes.Clear();
    }

    private static bool TryAdd(uint address, uint offset, out uint result)
    {
        if (offset > uint.MaxValue - address)
        {
            result = 0;
            return false;
        }

        result = address + offset;
        return true;
    }
}
