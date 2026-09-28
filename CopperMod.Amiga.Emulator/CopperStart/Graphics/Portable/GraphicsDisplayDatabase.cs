using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Portable native OCS/ECS display database.  Nonzero mode handles remain the
/// public ModeIDs, while the valid zero default-lores record uses an explicit
/// nonzero compatibility handle so it cannot collide with NULL.  Records and
/// data chunks stay private to this layer so a future native monitor
/// implementation can replace them without changing the guest ABI.
/// CyberGraphX/RTG records continue through its own patch.
/// </summary>
internal static class GraphicsDisplayDatabase
{
    internal const int Failure = -1;
    internal const uint DiAvailNoChips = 0x0001;
    internal const uint DiAvailNoMonitor = 0x0002;

    // DEFAULT_MONITOR_ID and the lores key are both zero.  A mode-backed
    // handle may therefore not use the raw ModeID for that one record: zero
    // is also the public NULL/"no handle" value accepted by
    // GetDisplayInfoData.  Keep the other native ModeIDs as their compact
    // compatibility handles, but reserve the value immediately below
    // INVALID_ID for the valid default-lores record.
    internal const uint DefaultModeHandle = 0xFFFF_FFFEu;

    internal const uint DtagDisp = 0x8000_0000u;
    internal const uint DtagDims = 0x8000_1000u;
    internal const uint DtagMntr = 0x8000_2000u;
    internal const uint DtagName = 0x8000_3000u;
    // DTAG_VEC is present in the V39/V40 headers for monitor-driver use only.
    // It is not a public native query chunk: leave it available to the
    // monitor/provider boundary instead of claiming it as an empty record.
    internal const uint DtagVec = 0x8000_4000u;

    // struct DisplayInfo includes the QueryHeader, public fields, padding,
    // and two reserved ULONG terminators. This is the backing allocation,
    // not the public transfer count: Kickstart 3.1 DISP stops at48 bytes.
    internal const int DisplayInfoChunkSize = 0x38;

    internal const uint BidTagDipfMustHave = 0x8000_0001u;
    internal const uint BidTagDipfMustNotHave = 0x8000_0002u;
    internal const uint BidTagViewPort = 0x8000_0003u;
    internal const uint BidTagNominalWidth = 0x8000_0004u;
    internal const uint BidTagNominalHeight = 0x8000_0005u;
    internal const uint BidTagDesiredWidth = 0x8000_0006u;
    internal const uint BidTagDesiredHeight = 0x8000_0007u;
    internal const uint BidTagDepth = 0x8000_0008u;
    internal const uint BidTagMonitorId = 0x8000_0009u;
    internal const uint BidTagSourceId = 0x8000_000Au;
    internal const uint BidTagRedBits = 0x8000_000Bu;
    internal const uint BidTagBlueBits = 0x8000_000Cu;
    internal const uint BidTagGreenBits = 0x8000_000Du;

    // graphics/coerce.h flags accepted by CoerceMode.
    internal const uint CoercePreserveColors = 0x0000_0001u;
    internal const uint CoerceAvoidFlicker = 0x0000_0002u;
    internal const uint CoerceIgnoreMonitorCompatibility = 0x0000_0004u;

    private const uint MonitorMask = 0xFFFF_1000u;
    private const uint ModeKeyMask = 0x0000_EFFFu;
    private const uint TagSkip = 3;
    // graphics/displayinfo.h DIPF_* values.  Keep the names beside the
    // mode database so property matching and the guest data chunks cannot
    // drift apart as more monitor families are added.
    internal const uint DipfIsLace = 0x0000_0001u;
    internal const uint DipfIsDualPf = 0x0000_0002u;
    internal const uint DipfIsPf2Pri = 0x0000_0004u;
    internal const uint DipfIsHam = 0x0000_0008u;
    internal const uint DipfIsEcs = 0x0000_0010u;
    internal const uint DipfIsPal = 0x0000_0020u;
    internal const uint DipfIsSprites = 0x0000_0040u;
    internal const uint DipfIsGenlock = 0x0000_0080u;
    internal const uint DipfIsWb = 0x0000_0100u;
    internal const uint DipfIsDraggable = 0x0000_0200u;
    internal const uint DipfIsPanelled = 0x0000_0400u;
    internal const uint DipfIsBeamsync = 0x0000_0800u;
    internal const uint DipfIsExtraHalfBrite = 0x0000_1000u;
    internal const uint DipfIsSpritesAttached = 0x0000_2000u;
    internal const uint DipfIsSpritesChangeResolution = 0x0000_4000u;
    internal const uint DipfIsSpritesBorder = 0x0000_8000u;
    internal const uint DipfIsAa = 0x0001_0000u;
    internal const uint DipfIsScanDoubled = 0x0002_0000u;
    internal const uint DipfIsSpritesChangeBase = 0x0004_0000u;
    internal const uint DipfIsSpritesChangePriority = 0x0008_0000u;
    internal const uint DipfIsDbuffer = 0x0010_0000u;
    internal const uint DipfIsProgrammedBeam = 0x0020_0000u;
    internal const uint DipfIsForeign = 0x8000_0000u;
    private const uint SpecialFlags = DipfIsDualPf |
                                       DipfIsPf2Pri |
                                       DipfIsHam |
                                       DipfIsExtraHalfBrite;
    // Physical monitor timing is shared with the native OpenMonitor
    // boundary. DTAG_MNTR describes this geometry, rather than the
    // viewport's nominal raster height.

    private static readonly ModeRecord[] NativeModes = BuildNativeModes();

    // GfxBase.DisplayInfoDataBase is a private APTR in the Kickstart ABI. It
    // is not the embedded Exec List used by MonitorSpec.  The host/native
    // bridge therefore publishes a small, self-describing sidecar only when
    // it owns a full native GfxBase envelope.  This is deliberately a private
    // CopperScreen/CopperSharp68k handoff object, not a replacement for the
    // undocumented ROM database internals; the public FindDisplayInfo and
    // GetDisplayInfoData semantics remain mode-ID based above.
    internal const uint NativeDatabaseMagic = 0x434D_4442u; // "CMDB"
    internal const uint NativeDatabaseVersion = 3u;
    internal const int NativeDatabaseHeaderSize = 0x10;
    internal const int NativeDatabaseRecordSize = 0x04;
    internal static int NativeDatabaseRecordCount => NativeModes.Length;
    // Version2 retains the version1 mode table and appends two fixed monitor
    // records: canonical ID, current Point, original Point (all guest LONGs).
    // Each Point is two signed WORDs. Original coordinates never follow edits.
    internal const int NativeMonitorPositionRecordSize = 12;
    internal const int NativeMonitorPositionCount = 2;
    internal static int NativeMonitorPositionsOffset
        => (NativeDatabaseHeaderSize +
            (NativeModes.Length * NativeDatabaseRecordSize) + 3) & ~3;
    // Version3 preserves the mode table and both version2 position records.
    // Its trailer records stable default-family identity and two borrowed
    // MonitorSpec pointers (NTSC, PAL). Zero means no registration, never
    // implicit ownership or permission to allocate. CMMO remains independent.
    internal static int NativeDefaultMonitorIdOffset
        => NativeMonitorPositionsOffset + NativeMonitorPositionCount * NativeMonitorPositionRecordSize;
    internal static int NativeMonitorRegistrationsOffset => NativeDefaultMonitorIdOffset + sizeof(uint);
    internal static int NativeDatabaseSize => NativeMonitorRegistrationsOffset + NativeMonitorPositionCount * sizeof(uint);
    // Optional private monitor-driver mutation storage.  The public CMDB3
    // envelope remains unchanged; native images that opt into -750 append two
    // complete MonitorInfo records after the registration cells.
    internal const int NativeMonitorMutationRecordSize = GraphicsMonitorInfoImage.TransferSize;
    internal const int NativeMonitorMutationRecordCount = NativeMonitorPositionCount;
    internal static int NativeMonitorMutationRecordsOffset
        => (NativeDatabaseSize + 3) & ~3;
    internal static int NativeDatabaseMutationSize
        => NativeMonitorMutationRecordsOffset +
           NativeMonitorMutationRecordCount * NativeMonitorMutationRecordSize;

    /// <summary>Deterministic boot image embedded by the native CMDB publisher.</summary>
    internal static byte[] CreateNativeDatabaseImage(bool supportsEcsDisplay, bool defaultMonitorNtsc = false,
        bool includeMonitorMutation = false)
    {
        var data = new byte[includeMonitorMutation ? NativeDatabaseMutationSize : NativeDatabaseSize];
        var modes = supportsEcsDisplay ? NativeModes : Array.FindAll(NativeModes, mode => !mode.IsSuperHires);
        WriteLong(data, 0, NativeDatabaseMagic);
        WriteLong(data, 4, NativeDatabaseVersion);
        WriteLong(data, 8, (uint)data.Length);
        WriteLong(data, 12, (uint)modes.Length);
        WriteLong(data, NativeDefaultMonitorIdOffset, defaultMonitorNtsc ? 0x11000u : 0x21000u);
        for (var index = 0; index < modes.Length; index++)
            WriteLong(data, NativeDatabaseHeaderSize + index * NativeDatabaseRecordSize, modes[index].ModeId);
        for (var index = 0; index < NativeMonitorPositionCount; index++)
        {
            var record = NativeMonitorPositionsOffset + index * NativeMonitorPositionRecordSize;
            WriteLong(data, record, index == 0 ? 0x11000u : 0x21000u);
            WriteLong(data, record + 4, GraphicsMonitorViewPosition.BootDefault.WordPair);
            WriteLong(data, record + 8, GraphicsMonitorViewPosition.BootDefault.WordPair);
        }
        return data;
    }

    /// <summary>
    /// Allocates and initializes the private native display-database sidecar.
    /// The caller publishes the returned pointer into GfxBase only after this
    /// complete image is written, so a failed allocation or guest write cannot
    /// expose a partially initialized database to a native 68k caller.
    /// </summary>
    internal static bool TryCreateNativeDatabase(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        bool supportsEcsDisplay,
        out uint address,
        Func<uint, GraphicsMonitorViewPosition>? monitorPositionProvider = null,
        bool defaultMonitorNtsc = false,
        uint ntscMonitor = 0,
        uint palMonitor = 0,
        Func<uint, GraphicsMonitorViewPosition>? originalMonitorPositionProvider = null,
        bool includeMonitorMutation = false)
    {
        address = 0;
        var byteCount = (uint)(includeMonitorMutation ? NativeDatabaseMutationSize : NativeDatabaseSize);
        if (!allocator.TryAllocate(byteCount, GraphicsMemoryClass.Public, out var candidate) ||
            candidate == 0 ||
            (candidate & 3u) != 0 ||
            candidate > uint.MaxValue - (byteCount - 1u))
        {
            if (candidate != 0)
                allocator.Free(candidate, byteCount, GraphicsMemoryClass.Public);

            return false;
        }

        // Keep the recycled public span transactional as well as the final
        // GfxBase APTR. A sparse/native adapter may accept the first bytes of
        // a LONG and then reject the operation; restoring the original image
        // lets a later native retry observe the same guest envelope instead
        // of a half-published database.
        var original = new byte[(int)byteCount];
        var snapshotComplete = true;
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (memory.TryReadByte(candidate + offset, out var value))
            {
                original[(int)offset] = value;
                continue;
            }

            snapshotComplete = false;
            break;
        }

        // Public allocator contracts do not require a newly returned span to
        // be zeroed.  The sidecar is a CopperSharp68k-facing guest object, so
        // make its complete envelope deterministic before publishing the
        // GfxBase pointer; otherwise a recycled/poisoned block could expose
        // stale records or future extension fields to a native 68k caller.
        var success = snapshotComplete;
        if (success)
        {
            for (var offset = 0u; offset < byteCount; offset++)
            {
                if (memory.TryWriteByte(candidate + offset, 0))
                    continue;

                success = false;
                break;
            }
        }

        var publishedModes = supportsEcsDisplay
            ? NativeModes
            : Array.FindAll(NativeModes, mode => !mode.IsSuperHires);

        success = success &&
                  memory.TryWriteLong(candidate, NativeDatabaseMagic) &&
                  memory.TryWriteLong(candidate + 4u, NativeDatabaseVersion) &&
                  memory.TryWriteLong(candidate + 8u, byteCount) &&
                  memory.TryWriteLong(candidate + 12u, (uint)publishedModes.Length);

        if (success)
        {
            for (var index = 0; index < publishedModes.Length; index++)
            {
                var offset = (uint)(NativeDatabaseHeaderSize +
                                     (index * NativeDatabaseRecordSize));
                if (!memory.TryWriteLong(candidate + offset, publishedModes[index].ModeId))
                {
                    success = false;
                    break;
                }
            }
        }

        if (success)
        {
            for (var index = 0; index < NativeMonitorPositionCount; index++)
            {
                var record = candidate + (uint)(NativeMonitorPositionsOffset +
                    index * NativeMonitorPositionRecordSize);
                var monitorId = index == 0 ? 0x00011000u : 0x00021000u;
                var initial = GraphicsMonitorViewPosition.BootDefault.WordPair;
                var current = monitorPositionProvider?.Invoke(monitorId).WordPair ?? initial;
                var originalPosition = originalMonitorPositionProvider?.Invoke(monitorId).WordPair ?? initial;
                if (memory.TryWriteLong(record, monitorId) &&
                    memory.TryWriteLong(record + 4, current) &&
                    memory.TryWriteLong(record + 8, originalPosition))
                    continue;
                success = false;
                break;
            }
        }

        // Bootstrap mappings are supplied by the lifecycle owner, not inferred
        // from the public list or DefaultMonitor. Stage them in the unpublished
        // image, under the same whole-allocation rollback as every other field.
        success = success && memory.TryWriteLong(candidate + (uint)NativeDefaultMonitorIdOffset,
            defaultMonitorNtsc ? 0x11000u : 0x21000u) &&
            memory.TryWriteLong(candidate + (uint)NativeMonitorRegistrationsOffset, ntscMonitor) &&
            memory.TryWriteLong(candidate + (uint)NativeMonitorRegistrationsOffset + 4, palMonitor);

        if (!success)
        {
            if (snapshotComplete)
            {
                for (var offset = 0u; offset < byteCount; offset++)
                    _ = memory.TryWriteByte(candidate + offset, original[(int)offset]);
            }

            allocator.Free(candidate, byteCount, GraphicsMemoryClass.Public);
            return false;
        }

        address = candidate;
        return true;
    }

    private static ModeRecord[] BuildNativeModes()
    {
        var modes = new List<ModeRecord>(66);

        // The zero monitor part is the jumper-selected default monitor from
        // graphics/modeid.h.  Keep its records first, followed by the
        // explicit PAL and NTSC monitor records.  Unprofiled portable callers
        // use PAL geometry for the default records; a display adapter can
        // replace the active default monitor without changing guest IDs.
        AddStandardModes(modes, GraphicsModeIds.DefaultMonitor, pal: true, monitorName: "Default");
        AddFeatureModes(modes, GraphicsModeIds.DefaultMonitor, pal: true, monitorName: "Default");
        AddStandardModes(modes, GraphicsModeIds.PalMonitor, pal: true);
        AddStandardModes(modes, GraphicsModeIds.NtscMonitor, pal: false);
        AddFeatureModes(modes, GraphicsModeIds.PalMonitor, pal: true);
        AddFeatureModes(modes, GraphicsModeIds.NtscMonitor, pal: false);
        return modes.ToArray();
    }

    private static void AddStandardModes(
        List<ModeRecord> modes,
        uint monitor,
        bool pal,
        string? monitorName = null)
    {
        monitorName ??= pal ? "PAL" : "NTSC";
        var height = pal ? (ushort)256 : (ushort)200;
        var laceHeight = pal ? (ushort)512 : (ushort)400;
        modes.Add(new(monitor, 320, height, false, pal, $"{monitorName} lores"));
        modes.Add(new(monitor | GraphicsModeIds.HiresKey, 640, height, true, pal, $"{monitorName} hires"));
        // ECS SuperHires uses the 35 ns pixel rate and doubles the native
        // Hires width.  Keep it as a first-class native record so
        // FindDisplayInfo, BestModeIDA, CoerceMode, and GetDisplayInfoData
        // all share the same ModeID-backed geometry.
        modes.Add(new(monitor | GraphicsModeIds.SuperHiresKey, 1280, height, true, pal, $"{monitorName} superhires", depth: 2));
        modes.Add(new(monitor | GraphicsModeIds.LoresLaceKey, 320, laceHeight, false, pal, $"{monitorName} lores interlace"));
        modes.Add(new(monitor | GraphicsModeIds.HiresLaceKey, 640, laceHeight, true, pal, $"{monitorName} hires interlace"));
        modes.Add(new(monitor | GraphicsModeIds.SuperHiresLaceKey, 1280, laceHeight, true, pal, $"{monitorName} superhires interlace", depth: 2));
    }

    private static void AddFeatureModes(
        List<ModeRecord> modes,
        uint monitor,
        bool pal,
        string? monitorName = null)
    {
        monitorName ??= pal ? "PAL" : "NTSC";
        var height = pal ? (ushort)256 : (ushort)200;
        var laceHeight = pal ? (ushort)512 : (ushort)400;

        AddFeatureMode(modes, monitor, GraphicsModeIds.HamKey, 320, height, pal, $"{monitorName} HAM");
        AddFeatureMode(modes, monitor, GraphicsModeIds.HamLaceKey, 320, laceHeight, pal, $"{monitorName} HAM interlace");
        AddFeatureMode(modes, monitor, GraphicsModeIds.ExtraHalfBriteKey, 320, height, pal, $"{monitorName} extra-halfbrite");
        AddFeatureMode(modes, monitor, GraphicsModeIds.ExtraHalfBriteLaceKey, 320, laceHeight, pal, $"{monitorName} extra-halfbrite interlace");
        AddFeatureMode(modes, monitor, GraphicsModeIds.LoresDualPlayfieldKey, 320, height, pal, $"{monitorName} dual-playfield");
        AddFeatureMode(modes, monitor, GraphicsModeIds.HiresDualPlayfieldKey, 640, height, pal, $"{monitorName} hires dual-playfield");
        // ECS SuperHires retains dual-playfield support, but the native
        // 35 ns fetch budget remains limited to two bitplanes.
        AddFeatureMode(modes, monitor, GraphicsModeIds.SuperHiresDualPlayfieldKey, 1280, height, pal, $"{monitorName} superhires dual-playfield", depth: 2);
        AddFeatureMode(modes, monitor, GraphicsModeIds.LoresDualPlayfieldLaceKey, 320, laceHeight, pal, $"{monitorName} dual-playfield interlace");
        AddFeatureMode(modes, monitor, GraphicsModeIds.HiresDualPlayfieldLaceKey, 640, laceHeight, pal, $"{monitorName} hires dual-playfield interlace");
        AddFeatureMode(modes, monitor, GraphicsModeIds.SuperHiresDualPlayfieldLaceKey, 1280, laceHeight, pal, $"{monitorName} superhires dual-playfield interlace", depth: 2);
        AddFeatureMode(modes, monitor, GraphicsModeIds.LoresDualPlayfieldTwoKey, 320, height, pal, $"{monitorName} dual-playfield 2");
        AddFeatureMode(modes, monitor, GraphicsModeIds.HiresDualPlayfieldTwoKey, 640, height, pal, $"{monitorName} hires dual-playfield 2");
        AddFeatureMode(modes, monitor, GraphicsModeIds.SuperHiresDualPlayfieldTwoKey, 1280, height, pal, $"{monitorName} superhires dual-playfield 2", depth: 2);
        AddFeatureMode(modes, monitor, GraphicsModeIds.LoresDualPlayfieldTwoLaceKey, 320, laceHeight, pal, $"{monitorName} dual-playfield 2 interlace");
        AddFeatureMode(modes, monitor, GraphicsModeIds.HiresDualPlayfieldTwoLaceKey, 640, laceHeight, pal, $"{monitorName} hires dual-playfield 2 interlace");
        AddFeatureMode(modes, monitor, GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, 1280, laceHeight, pal, $"{monitorName} superhires dual-playfield 2 interlace", depth: 2);
    }

    private static void AddFeatureMode(
        List<ModeRecord> modes,
        uint monitor,
        ushort key,
        ushort width,
        ushort height,
        bool pal,
        string name,
        ushort? depth = null)
        => modes.Add(new(monitor | key, width, height, (key & GraphicsModeIds.HiresMode) != 0, pal, name, depth));

    internal static uint FindDisplayInfo(uint modeId)
    {
        if (!TryGetMode(modeId, false, true, out _))
            return 0;

        return ToDisplayInfoHandle(modeId);
    }

    private static uint ToDisplayInfoHandle(uint modeId)
        => modeId == GraphicsModeIds.DefaultMonitor
            ? DefaultModeHandle
            : modeId;

    /// <summary>
    /// Identifies mode IDs owned by the portable OCS/ECS database.  Unknown
    /// IDs must remain unclaimed so an RTG/CyberGraphX or native monitor
    /// provider can answer the same query instead of being swallowed by a
    /// portable zero/availability sentinel.
    /// </summary>
    internal static bool IsNativeModeId(uint modeId)
        // INVALID_ID is the public no-record sentinel.  It is not a display
        // record, but the graphics.library query vectors still own it so a
        // caller receives the documented NULL/NO-MONITOR result instead of
        // stealing the call from a monitor or CyberGraphX provider.
        => modeId == GraphicsModeIds.Invalid ||
           TryGetMode(modeId, false, true, out _);

    internal static bool IsNativeDisplayInfoHandle(uint handle)
        => handle == DefaultModeHandle || IsNativeModeId(handle);

    /// <summary>
    /// Resolves the compact DisplayInfo handle used by the portable ColorMap
    /// fields back to its public ModeID.  Every non-default native record uses
    /// its ModeID as the handle; the real zero-monitor lores record is the one
    /// exception because a raw zero handle is the public NULL sentinel.
    /// </summary>
    internal static bool TryResolveDisplayInfoHandle(uint handle, out uint modeId)
    {
        modeId = GraphicsModeIds.Invalid;
        if (handle == DefaultModeHandle)
        {
            modeId = GraphicsModeIds.DefaultMonitor;
            return true;
        }

        if (handle == 0 || handle == GraphicsModeIds.Invalid ||
            !TryGetMode(handle, out var mode))
        {
            return false;
        }

        modeId = mode.ModeId;
        return true;
    }

    /// <summary>
    /// Returns the monitor timing bits that CalcIVG needs after the portable
    /// mode table has resolved a ViewPort mode ID.  Keeping this lookup beside
    /// the DisplayInfo records prevents the copper path from inventing a
    /// second PAL/NTSC or lace profile.
    /// </summary>
    internal static bool TryGetCalcIvgProfile(
        uint modeId,
        bool defaultMonitorNtsc,
        bool supportsEcsDisplay,
        out ushort totalColorClocks,
        out bool isLace,
        out bool isScanDoubled)
    {
        totalColorClocks = 0;
        isLace = false;
        isScanDoubled = false;
        if (!TryGetMode(modeId, defaultMonitorNtsc, supportsEcsDisplay, out var mode) ||
            !IsModeAvailable(mode, supportsEcsDisplay))
            return false;

        totalColorClocks = GraphicsNativeMonitorTiming.For(mode.IsPal).TotalColorClocks;
        isLace = mode.IsLace;
        isScanDoubled = (mode.Properties & DipfIsScanDoubled) != 0;
        return totalColorClocks != 0;
    }

    /// <summary>
    /// ModeNotAvailable also owns recognized monitor families whose feature
    /// key is not executable on the current chipset (for example DoubleScan
    /// on the OCS path).  Keep those queries portable while leaving foreign
    /// monitor IDs available to RTG/native monitor providers.
    /// </summary>
    internal static bool IsNativeModeQuery(uint modeId)
    {
        // ModeNotAvailable(INVALID_ID) is the defined no-record query.  Keep
        // this universal sentinel separate from arbitrary foreign monitor
        // IDs, which must remain available to their provider.
        if (modeId == GraphicsModeIds.Invalid)
            return true;

        var monitor = modeId & MonitorMask;
        if (monitor != GraphicsModeIds.DefaultMonitor &&
            monitor != GraphicsModeIds.PalMonitor &&
            monitor != GraphicsModeIds.NtscMonitor)
        {
            return false;
        }

        // The low 16 bits still carry the monitor's 0x1000 marker.  Strip
        // that monitor-part bit before matching the composite mode key; the
        // Hires bit at 0x8000 remains part of the key.
        return GraphicsModeIds.IsNativeModeQueryKey((ushort)(modeId & ModeKeyMask));
    }

    internal static uint NextDisplayInfo(uint lastModeId)
    {
        if (lastModeId == GraphicsModeIds.Invalid)
        {
            return NativeModes.Length == 0
                ? GraphicsModeIds.Invalid
                // NextDisplayInfo iterates public ModeID keys, not the
                // private/nonzero handle used by FindDisplayInfo for the
                // valid zero default-lores record.  Zero is therefore a
                // real first result and must not be replaced with
                // DefaultModeHandle here.
                : NativeModes[0].ModeId;
        }

        // Accept the private default handle as a compatibility courtesy for
        // older host callers, but always publish the public ModeID on the
        // result.  Native Kickstart callers pass the raw zero key after the
        // first iteration.
        var lastModeIdKey = lastModeId == DefaultModeHandle
            ? GraphicsModeIds.DefaultMonitor
            : lastModeId;

        for (var index = 0; index < NativeModes.Length; index++)
        {
            if (NativeModes[index].ModeId != lastModeIdKey)
                continue;

            return index + 1 < NativeModes.Length
                ? NativeModes[index + 1].ModeId
                : GraphicsModeIds.Invalid;
        }

        return GraphicsModeIds.Invalid;
    }

    internal static uint ModeNotAvailable(uint modeId, bool supportsEcsDisplay = true)
    {
        var monitor = modeId & MonitorMask;
        if (modeId == GraphicsModeIds.Invalid ||
            (monitor != GraphicsModeIds.DefaultMonitor &&
             monitor != GraphicsModeIds.PalMonitor &&
             monitor != GraphicsModeIds.NtscMonitor))
        {
            return DiAvailNoMonitor;
        }

        return TryGetMode(
                   modeId,
                   defaultMonitorNtsc: false,
                   supportsEcsDisplay: supportsEcsDisplay,
                   out var mode) &&
            IsModeAvailable(mode, supportsEcsDisplay)
            ? 0u
            : DiAvailNoChips;
    }

    internal static bool SupportsDoubleBuffering(
        uint modeId,
        bool defaultMonitorNtsc = false,
        bool supportsEcsDisplay = true)
    {
        // FindDisplayInfo exposes the zero-monitor default record through a
        // reserved non-zero handle because a raw zero is the public NULL
        // sentinel.  Capability queries must resolve that handle through the
        // same path as GetDisplayInfoData/ColorMap state; otherwise an
        // otherwise valid default viewport is denied double buffering merely
        // because its monitor part was represented by the compatibility
        // handle instead of ModeID 0.
        var resolvedModeId = modeId == DefaultModeHandle
            ? GraphicsModeIds.DefaultMonitor
            : modeId;

        return TryGetMode(
               resolvedModeId,
               defaultMonitorNtsc,
               supportsEcsDisplay,
               out var mode) &&
           IsModeAvailable(mode, supportsEcsDisplay) &&
           (mode.Properties & DipfIsDbuffer) != 0;
    }

    /// <summary>
    /// Selects the closest native mode for the documented V39 BestModeIDA
    /// tag list.  The score follows the classic aspect-ratio, desired-size,
    /// and depth ordering; a malformed guest list or an RTG-only request
    /// returns INVALID_ID so its separate patch can claim the call.
    /// </summary>
    internal static uint BestModeIDA(
        IGraphicsMemory memory,
        uint tagList,
        Func<uint, uint>? viewportModeProvider = null,
        bool rejectInvalidViewportMode = true,
        bool defaultMonitorNtsc = false,
        bool supportsEcsDisplay = true,
        bool supportsAgaDisplay = false)
    {
        if (!TryReadTags(memory, tagList, out var tags))
            return GraphicsModeIds.Invalid;

        if (!TryGetWordTag(tags, BidTagNominalWidth, 640, out var nominalWidth) ||
            !TryGetWordTag(tags, BidTagNominalHeight, 200, out var nominalHeight) ||
            !TryGetWordTag(tags, BidTagDesiredWidth, nominalWidth, out var desiredWidth) ||
            !TryGetWordTag(tags, BidTagDesiredHeight, nominalHeight, out var desiredHeight) ||
            !TryGetByteTag(tags, BidTagDepth, 1, out var minimumDepth) ||
            !TryGetByteTag(tags, BidTagRedBits, 4, out var minimumRed) ||
            !TryGetByteTag(tags, BidTagBlueBits, 4, out var minimumBlue) ||
            !TryGetByteTag(tags, BidTagGreenBits, 4, out var minimumGreen))
        {
            return GraphicsModeIds.Invalid;
        }

        // A zero BIDTAG_MonitorID is not the same as an omitted tag.  The
        // zero monitor part denotes the jumper-selected default monitor and
        // is a real native family in this portable database.  Keep presence
        // separate from the value so an explicit default constraint is not
        // widened into an arbitrary PAL/NTSC match.
        var monitorSpecified = tags.ContainsKey(BidTagMonitorId);
        var monitorId = GetLongTag(tags, BidTagMonitorId, 0);
        var mustHave = GetLongTag(tags, BidTagDipfMustHave, 0);
        var mustNotHave = GetLongTag(tags, BidTagDipfMustNotHave, SpecialFlags);

        if (nominalWidth == 0 || nominalHeight == 0 || desiredWidth == 0 ||
            desiredHeight == 0 ||
            minimumRed > (supportsAgaDisplay ? 8 : 4) ||
            minimumBlue > (supportsAgaDisplay ? 8 : 4) ||
            minimumGreen > (supportsAgaDisplay ? 8 : 4))
        {
            return GraphicsModeIds.Invalid;
        }

        // BIDTAG_ViewPort supplies the default geometry/depth and, when the
        // explicit display backend can identify it, the monitor association.
        // BIDTAG_SourceID intentionally takes precedence: the documented
        // contract says it replaces the viewport as the source record.
        if (tags.TryGetValue(BidTagViewPort, out var viewPort) &&
            !tags.ContainsKey(BidTagSourceId))
        {
            if (viewPort == 0 ||
                !TryReadViewPortDefaults(
                    memory,
                    viewPort,
                    out var viewPortWidth,
                    out var viewPortHeight,
                    out var viewPortDepth))
            {
                return GraphicsModeIds.Invalid;
            }

            if (!tags.ContainsKey(BidTagNominalWidth))
                nominalWidth = viewPortWidth;
            if (!tags.ContainsKey(BidTagNominalHeight))
                nominalHeight = viewPortHeight;
            if (!tags.ContainsKey(BidTagDesiredWidth))
                desiredWidth = nominalWidth;
            if (!tags.ContainsKey(BidTagDesiredHeight))
                desiredHeight = nominalHeight;
            if (!tags.ContainsKey(BidTagDepth))
                minimumDepth = (byte)viewPortDepth;

            if (viewportModeProvider is not null)
            {
                var viewPortModeId = viewportModeProvider(viewPort);
                if (!TryGetMode(
                        viewPortModeId,
                        defaultMonitorNtsc,
                        supportsEcsDisplay,
                        out var viewPortMode) ||
                    !IsModeAvailable(viewPortMode, supportsEcsDisplay))
                {
                    if (rejectInvalidViewportMode)
                        return GraphicsModeIds.Invalid;
                }
                else if (!monitorSpecified)
                {
                    monitorId = viewPortMode.ModeId & MonitorMask;
                    monitorSpecified = true;
                }
                else if ((monitorId & MonitorMask) != (viewPortMode.ModeId & MonitorMask))
                {
                    // The source viewport and requested monitor must remain
                    // on a compatible monitor family.  CyberGraphX/RTG
                    // providers own any non-native compatibility policy.
                    return GraphicsModeIds.Invalid;
                }

                // When a ViewPort is the source, the classic default is its
                // VPModeID.  Carry the source record's special properties
                // into the required-feature mask just as BIDTAG_SourceID
                // does; otherwise a HAM/DPF viewport can be silently
                // replaced by an ordinary mode with the same geometry.
                mustHave |= viewPortMode.GetProperties(supportsAgaDisplay) & SpecialFlags;
            }
        }

        if (tags.TryGetValue(BidTagSourceId, out var sourceId))
        {
            if (!TryGetMode(
                    sourceId,
                    defaultMonitorNtsc,
                    supportsEcsDisplay,
                    out var source) ||
                !IsModeAvailable(source, supportsEcsDisplay))
                return GraphicsModeIds.Invalid;

            mustHave |= source.GetProperties(supportsAgaDisplay) & SpecialFlags;
            if (!monitorSpecified)
            {
                monitorId = source.ModeId & MonitorMask;
                monitorSpecified = true;
            }
            else if ((monitorId & MonitorMask) != (source.ModeId & MonitorMask))
                return GraphicsModeIds.Invalid;
            if (!tags.ContainsKey(BidTagNominalWidth))
                nominalWidth = source.Width;
            if (!tags.ContainsKey(BidTagNominalHeight))
                nominalHeight = source.Height;
            if (!tags.ContainsKey(BidTagDesiredWidth))
                desiredWidth = nominalWidth;
            if (!tags.ContainsKey(BidTagDesiredHeight))
                desiredHeight = nominalHeight;
        }

        // SPECIAL_FLAGS is the default exclusion mask.  A source viewport or
        // explicit SourceID is authoritative for any special feature it
        // carries, so those bits must be removed from the exclusion mask
        // before checking for a contradiction.
        mustNotHave &= ~mustHave;

        if ((mustHave & mustNotHave) != 0)
            return GraphicsModeIds.Invalid;

        ModeRecord? best = null;
        long bestScore = long.MaxValue;
        foreach (var nativeMode in NativeModes)
        {
            if (!IsModeAvailable(nativeMode, supportsEcsDisplay))
                continue;

            var mode = nativeMode.ResolveDefaultProfile(defaultMonitorNtsc);
            var properties = mode.GetProperties(supportsAgaDisplay);
            if ((properties & mustHave) != mustHave ||
                (properties & mustNotHave) != 0 ||
                mode.GetDepth(supportsAgaDisplay) < minimumDepth ||
                mode.GetComponentBits(supportsAgaDisplay) < minimumRed ||
                mode.GetComponentBits(supportsAgaDisplay) < minimumBlue ||
                mode.GetComponentBits(supportsAgaDisplay) < minimumGreen ||
                (monitorSpecified &&
                 (mode.ModeId & MonitorMask) != (monitorId & MonitorMask)))
            {
                continue;
            }

            var aspectError = Math.Abs((long)mode.Width * nominalHeight -
                                       (long)mode.Height * nominalWidth);
            var sizeError = Math.Abs((long)mode.Width - desiredWidth) +
                            Math.Abs((long)mode.Height - desiredHeight);
            var depthError = mode.GetDepth(supportsAgaDisplay) - minimumDepth;
            var score = aspectError * 1024 + sizeError * 16 + depthError;
            if (score < bestScore)
            {
                best = mode;
                bestScore = score;
            }
        }

        return best?.ModeId ?? GraphicsModeIds.Invalid;
    }

    /// <summary>
    /// Distinguishes a well-formed native BestModeIDA request whose score has
    /// no matching record from malformed or provider-owned input.  The public
    /// vector must publish INVALID_ID for the former, while the latter must
    /// remain available to Kickstart or a monitor/RTG provider.
    /// </summary>
    internal static bool IsBestModeRequestOwned(
        IGraphicsMemory memory,
        uint tagList,
        Func<uint, uint>? viewportModeProvider = null,
        bool rejectInvalidViewportMode = true,
        bool defaultMonitorNtsc = false,
        bool supportsEcsDisplay = true,
        bool supportsAgaDisplay = false)
    {
        if (!TryReadTags(memory, tagList, out var tags) ||
            !TryGetWordTag(tags, BidTagNominalWidth, 640, out var nominalWidth) ||
            !TryGetWordTag(tags, BidTagNominalHeight, 200, out var nominalHeight) ||
            !TryGetWordTag(tags, BidTagDesiredWidth, nominalWidth, out var desiredWidth) ||
            !TryGetWordTag(tags, BidTagDesiredHeight, nominalHeight, out var desiredHeight) ||
            !TryGetByteTag(tags, BidTagDepth, 1, out _) ||
            !TryGetByteTag(tags, BidTagRedBits, 4, out var minimumRed) ||
            !TryGetByteTag(tags, BidTagBlueBits, 4, out var minimumBlue) ||
            !TryGetByteTag(tags, BidTagGreenBits, 4, out var minimumGreen) ||
            nominalWidth == 0 || nominalHeight == 0 ||
            desiredWidth == 0 || desiredHeight == 0 ||
            minimumRed > (supportsAgaDisplay ? 8 : 4) ||
            minimumBlue > (supportsAgaDisplay ? 8 : 4) ||
            minimumGreen > (supportsAgaDisplay ? 8 : 4))
        {
            return false;
        }

        var monitorSpecified = tags.ContainsKey(BidTagMonitorId);
        var monitorId = GetLongTag(tags, BidTagMonitorId, 0);
        if (monitorSpecified)
        {
            var monitor = monitorId & MonitorMask;
            if (monitor != GraphicsModeIds.DefaultMonitor &&
                monitor != GraphicsModeIds.PalMonitor &&
                monitor != GraphicsModeIds.NtscMonitor)
            {
                return false;
            }
        }

        if (tags.TryGetValue(BidTagViewPort, out var viewPort) &&
            !tags.ContainsKey(BidTagSourceId))
        {
            if (viewPort == 0 ||
                !TryReadViewPortDefaults(
                    memory,
                    viewPort,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            if (viewportModeProvider is not null && rejectInvalidViewportMode)
            {
                var viewPortModeId = viewportModeProvider(viewPort);
                if (!TryGetMode(
                        viewPortModeId,
                        defaultMonitorNtsc,
                        supportsEcsDisplay,
                        out var viewPortMode) ||
                    !IsModeAvailable(viewPortMode, supportsEcsDisplay))
                {
                    return false;
                }
            }
        }

        if (tags.TryGetValue(BidTagSourceId, out var sourceId) &&
            (!TryGetMode(
                sourceId,
                defaultMonitorNtsc,
                supportsEcsDisplay,
                out var source) ||
             !IsModeAvailable(source, supportsEcsDisplay)))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadViewPortDefaults(
        IGraphicsMemory memory,
        uint viewPort,
        out ushort width,
        out ushort height,
        out ushort depth)
    {
        width = 0;
        height = 0;
        depth = 0;
        if (!GraphicsRasterOperations.ValidateViewPortForScroll(memory, viewPort) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDWidth,
                out width) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDHeight,
                out height) ||
            width == 0 ||
            height == 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo) ||
            rasInfo == 0 ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var bitMap) ||
            bitMap == 0 ||
            !memory.TryReadByte(
                bitMap + (uint)GraphicsLayouts.BitMapDepth,
                out var byteDepth) ||
            byteDepth == 0 ||
            byteDepth > 8)
        {
            width = 0;
            height = 0;
            depth = 0;
            return false;
        }

        depth = byteDepth;
        return true;
    }

    /// <summary>
    /// Chooses a native mode that can host a real ViewPort's geometry.  The
    /// viewport and its first RasInfo/BitMap are validated before any mode is
    /// returned. The bitmap's depth constrains the destination with or without
    /// PRESERVE_COLORS; AVOID_FLICKER excludes interlaced records. Monitor
    /// compatibility is not modeled by the portable PAL/NTSC registry, so
    /// IGNORE_MCOMPAT is accepted but has no additional effect here.
    /// </summary>
    internal static uint CoerceMode(
        IGraphicsMemory memory,
        uint viewPort,
        uint monitorId,
        uint flags,
        bool defaultMonitorNtsc = false,
        bool supportsEcsDisplay = true,
        bool supportsAgaDisplay = false,
        uint sourceModeId = GraphicsModeIds.Invalid)
    {
        if (!GraphicsRasterOperations.ValidateViewPortForScroll(memory, viewPort) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDWidth,
                out var width) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDHeight,
                out var height) ||
            width == 0 ||
            height == 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo) ||
            rasInfo == 0 ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var bitMap) ||
            bitMap == 0 ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortModes,
                out var viewModes) ||
            !memory.TryReadByte(
                bitMap + (uint)GraphicsLayouts.BitMapDepth,
                out var depth) ||
            depth == 0 ||
            depth > 8)
        {
            return GraphicsModeIds.Invalid;
        }

        var requestedMonitor = monitorId & MonitorMask;
        if (requestedMonitor == 0)
            requestedMonitor = defaultMonitorNtsc
                ? GraphicsModeIds.NtscMonitor
                : GraphicsModeIds.PalMonitor;

        if (requestedMonitor != GraphicsModeIds.PalMonitor &&
            requestedMonitor != GraphicsModeIds.NtscMonitor)
        {
            return GraphicsModeIds.Invalid;
        }

        var minimumDepth = depth;
        var avoidFlicker = (flags & CoerceAvoidFlicker) != 0;
        // Preserve the native/foreign Modes admission boundary separately
        // from source association and destination scoring.
        if (!GraphicsModeIds.TryGetNativeModeId(viewModes, defaultMonitorNtsc, out _))
            return GraphicsModeIds.Invalid;
        // Original3.1 allows a five-plane hires source to choose lores.
        // Depth constrains the destination, not the source's nominal maximum.
        // Only an associated display record carries special feature semantics.
        uint sourceFeatures = 0;
        if (sourceModeId != GraphicsModeIds.Invalid)
        {
            if (!TryGetMode(sourceModeId, defaultMonitorNtsc, supportsEcsDisplay, out var sourceMode))
                return GraphicsModeIds.Invalid;
            sourceFeatures = sourceMode.GetProperties(supportsAgaDisplay) & SpecialFlags;
            // A resolved source supplies nominal dimensions, as in the ROM's
            // SourceID-based best-mode request; viewport geometry is the
            // fallback only when no source record is associated.
            width = (ushort)sourceMode.Width;
            height = (ushort)sourceMode.Height;
        }

        ModeRecord? best = null;
        long bestScore = long.MaxValue;
        foreach (var mode in NativeModes)
        {
            if (!IsModeAvailable(mode, supportsEcsDisplay) ||
                (mode.ModeId & MonitorMask) != requestedMonitor ||
                (mode.GetProperties(supportsAgaDisplay) & SpecialFlags) != sourceFeatures ||
                mode.GetDepth(supportsAgaDisplay) < minimumDepth ||
                (avoidFlicker && mode.IsLace))
            {
                continue;
            }

            var aspectError = Math.Abs((long)mode.Width * height -
                                       (long)mode.Height * width);
            var sizeError = Math.Abs((long)mode.Width - width) +
                            Math.Abs((long)mode.Height - height);
            var depthError = mode.GetDepth(supportsAgaDisplay) - minimumDepth;
            var score = aspectError * 1024 + sizeError * 16 + depthError;
            if (score < bestScore)
            {
                best = mode;
                bestScore = score;
            }
        }

        return best?.ModeId ?? GraphicsModeIds.Invalid;
    }

    /// <summary>
    /// Reports whether a CoerceMode request has a valid native input
    /// envelope.  A valid envelope may still have no compatible record and
    /// must then publish INVALID_ID; malformed viewports, unsupported
    /// monitor families, and foreign mode keys remain provider-owned.
    /// </summary>
    internal static bool IsCoerceRequestOwned(
        IGraphicsMemory memory,
        uint viewPort,
        uint monitorId,
        uint flags,
        bool defaultMonitorNtsc = false)
    {
        if (!GraphicsRasterOperations.ValidateViewPortForScroll(memory, viewPort) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDWidth,
                out var width) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDHeight,
                out var height) ||
            width == 0 || height == 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo) ||
            rasInfo == 0 ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var bitMap) ||
            bitMap == 0 ||
            !memory.TryReadByte(
                bitMap + (uint)GraphicsLayouts.BitMapDepth,
                out var depth) ||
            depth == 0 || depth > 8 ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortModes,
                out var viewModes))
        {
            return false;
        }

        var requestedMonitor = monitorId & MonitorMask;
        if (requestedMonitor == 0)
        {
            requestedMonitor = defaultMonitorNtsc
                ? GraphicsModeIds.NtscMonitor
                : GraphicsModeIds.PalMonitor;
        }

        if (requestedMonitor != GraphicsModeIds.PalMonitor &&
            requestedMonitor != GraphicsModeIds.NtscMonitor)
        {
            return false;
        }

        if (viewModes != 0 &&
            !GraphicsModeIds.TryGetNativeModeId(
                viewModes,
                requestedMonitor == GraphicsModeIds.NtscMonitor,
                out _))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns one V36 DisplayInfoData chunk.  A negative result means the
    /// handle/tag is not owned by the native database and lets a separate
    /// monitor/CyberGraphX patch claim the vector; zero is a valid native
    /// result for an unknown public data tag or an empty caller buffer.
    /// </summary>
    internal static int GetDisplayInfoData(
        IGraphicsMemory memory,
        uint handle,
        uint buffer,
        uint bufferSize,
        uint tag,
        uint modeId,
        Func<uint, uint>? monitorSpecProvider = null,
        bool defaultMonitorNtsc = false,
        bool supportsEcsDisplay = true,
        bool supportsAgaDisplay = false,
        Func<uint, GraphicsMonitorViewPosition>? monitorPositionProvider = null,
        Func<uint, GraphicsMonitorViewPosition>? monitorOriginalPositionProvider = null,
        Func<uint, byte[]?>? monitorInfoImageProvider = null)
    {
        // FindDisplayInfo(INVALID_ID) is guaranteed to return NULL.  The
        // companion GetDisplayInfoData(NULL, ..., INVALID_ID) therefore
        // reports the ordinary zero-byte/no-information result without
        // probing or mutating the caller's buffer.  Other unknown IDs remain
        // unclaimed so a native monitor or CyberGraphX provider can answer.
        if (handle == 0 && modeId == GraphicsModeIds.Invalid)
            return 0;

        // DTAG_VEC is an internal monitor-driver record.  It must not be
        // swallowed by the portable public-query path, because a resident
        // monitor or provider may use the same vector to expose its private
        // VecInfo envelope.
        if (tag == DtagVec)
            return Failure;

        if (tag == DtagMntr)
        {
            if (handle != 0)
            {
                handle = NormalizeMonitorAlias(handle);
                if (handle == 0) handle = DefaultModeHandle;
            }
            modeId = NormalizeMonitorAlias(modeId);
        }

        if (!TryResolveMode(
                handle,
                modeId,
                defaultMonitorNtsc,
                supportsEcsDisplay,
                out var mode))
            return Failure;

        // A zero-sized or NULL destination is a valid no-write query.  Do
        // not build DTAG_MNTR here: constructing that chunk may lazily create
        // and publish a resident MonitorSpec, which is a guest-visible side
        // effect even though the caller requested no bytes.
        if (buffer == 0 || bufferSize == 0)
            return 0;

        // Resolve the actual transfer span, not the backing allocation,
        // before invoking a lazily allocating MonitorSpec provider:
        // a caller whose destination is already unwritable has not opened a
        // monitor and must not leave one resident merely because its query
        // could not begin.
        var byteCount = GetDisplayInfoDataTransferLength(tag, bufferSize);
        if (byteCount == 0 ||
            buffer > uint.MaxValue - (byteCount - 1))
        {
            return 0;
        }

        var original = new byte[(int)byteCount];
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(buffer + offset, out original[offset]))
                return 0;
        }

        // IGraphicsMemory deliberately exposes no host-specific writability
        // predicate. Rewriting the complete snapshot is therefore the
        // portable admission probe: it rejects a read-only/mapped byte
        // anywhere in the requested span before BuildData(DTAG_MNTR) can
        // create a MonitorSpec. The actual writer below still covers every
        // byte and restores the complete snapshot if a later write becomes
        // unavailable dynamically.
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(buffer + offset, original[offset]))
                return 0;
        }

        var data = BuildData(
            mode,
            tag,
            byteCount > 16 ? monitorSpecProvider : null,
            supportsEcsDisplay,
            supportsAgaDisplay,
            byteCount > 20 ? monitorPositionProvider : null,
            byteCount > 80 ? monitorOriginalPositionProvider : null,
            monitorInfoImageProvider);
        if (data.Length == 0)
            return 0;

        // NameInfo carries a fixed 32-byte C string after its QueryHeader.
        // Kickstart keeps a caller-visible short copy terminated when the
        // requested buffer ends inside that string. Preserve the header for
        // header-only probes, but reserve the final byte of any partial name
        // payload for the terminator so a truncated display name cannot leak
        // an unterminated guest string into Intuition or screen-mode tools.
        if (tag == DtagName && byteCount > 16 && byteCount < (uint)data.Length)
            data[(int)byteCount - 1] = 0;

        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(buffer + offset, data[(int)offset]))
            {
                for (var restore = 0u; restore < byteCount; restore++)
                    _ = memory.TryWriteByte(buffer + restore, original[restore]);

                return 0;
            }
        }

        return (int)byteCount;
    }

    /// <summary>
    /// Resolves the guest-visible output length for a public display-data
    /// query without building a chunk or invoking the monitor-spec provider.
    /// Native-overlay dispatch uses this side-effect-free probe to admit the
    /// caller's output span before <see cref="GetDisplayInfoData"/> starts its
    /// read/rollback transaction.  The returned length is the number of
    /// bytes that the public routine may write after applying the caller's
    /// buffer-size limit; zero is a valid result for an unknown public tag,
    /// an empty buffer, or the documented NULL/INVALID_ID sentinel form.
    /// </summary>
    internal static bool TryGetDisplayInfoDataOutputLength(
        uint handle,
        uint bufferSize,
        uint tag,
        uint modeId,
        bool defaultMonitorNtsc,
        bool supportsEcsDisplay,
        out uint byteCount)
    {
        if (tag == DtagMntr)
        {
            if (handle != 0)
            {
                handle = NormalizeMonitorAlias(handle);
                if (handle == 0) handle = DefaultModeHandle;
            }
            modeId = NormalizeMonitorAlias(modeId);
        }
        byteCount = 0;

        // The internal monitor-driver chunk is deliberately not part of the
        // portable public query surface.  Keep it available to a resident
        // monitor/provider rather than treating it as an empty successful
        // query here.
        if (tag == DtagVec)
            return false;

        // A null handle with INVALID_ID is the universal empty query.  It is
        // valid even though no native mode record exists for that sentinel.
        if (handle == 0 && modeId == GraphicsModeIds.Invalid)
            return true;

        // Unknown public tags still resolve the mode first: the normal query
        // returns zero for an unknown tag only when its mode/handle belongs
        // to this database.  Unknown modes remain available to a monitor or
        // provider owner and therefore fail this admission probe.
        if (!TryResolveMode(
                handle,
                modeId,
                defaultMonitorNtsc,
                supportsEcsDisplay,
                out _))
        {
            return false;
        }

        byteCount = GetDisplayInfoDataTransferLength(tag, bufferSize);
        return true;
    }

    // Keep the public transfer spans shared by the side-effect-free
    // native-overlay admission probe and the actual guest transaction. A
    // mismatch here would either preflight too little guest memory or make a
    // valid public query spuriously provider-owned.
    private static uint GetDisplayInfoDataTransferLength(uint tag, uint requested)
    {
        // The scalar prefix is byte-addressable, but Kickstart only copies
        // complete eight-byte Rectangle fields after offset26. Clamp before
        // arithmetic so oversized public requests cannot overflow.
        if (tag == DtagDims)
            return requested <= 26 ? requested : 26 + 8 * ((Math.Min(requested, 66u) - 26) / 8);

        var limit = tag switch
        {
            DtagDisp => 48u,
            DtagMntr => (uint)GraphicsMonitorInfoImage.TransferSize,
            DtagName => 0x38u,
            _ => 0u
        };
        return Math.Min(requested, limit);
    }

    // The default-monitor marker is an alias, not a separate mode record.
    // Keep this MNTR-specific until other tags' alias contracts are qualified.
    private static uint NormalizeMonitorAlias(uint id)
        => (id >> 16) == 0 ? id & ~0x1000u : id;

    private static bool TryResolveMode(uint handle, uint modeId, out ModeRecord mode)
        => TryResolveMode(
            handle,
            modeId,
            defaultMonitorNtsc: false,
            supportsEcsDisplay: true,
            out mode);

    private static bool TryResolveMode(
        uint handle,
        uint modeId,
        bool defaultMonitorNtsc,
        bool supportsEcsDisplay,
        out ModeRecord mode)
    {
        // The final mode-id argument is used only when the public handle is
        // null.  A valid handle is authoritative even when callers leave D2
        // nonzero, and DEFAULT_MONITOR_ID (0) is a real mode key rather than
        // an omission marker once the default-monitor records are present.
        if (handle != 0)
        {
            var resolvedHandle = handle == DefaultModeHandle
                ? GraphicsModeIds.DefaultMonitor
                : handle;
            return TryGetMode(
                resolvedHandle,
                defaultMonitorNtsc,
                supportsEcsDisplay,
                out mode);
        }

        return TryGetMode(modeId, defaultMonitorNtsc, supportsEcsDisplay, out mode);
    }

    private static bool TryGetMode(uint modeId, out ModeRecord mode)
        => TryGetMode(
            modeId,
            defaultMonitorNtsc: false,
            supportsEcsDisplay: true,
            out mode);

    private static bool TryGetMode(
        uint modeId,
        bool defaultMonitorNtsc,
        bool supportsEcsDisplay,
        out ModeRecord mode)
    {
        foreach (var candidate in NativeModes)
        {
            // DisplayInfo records remain queryable even when the active
            // chipset cannot execute them; DI_AVAIL_NO_CHIPS is reported by
            // ModeNotAvailable/DTAG_DISP instead of hiding the record.
            if (candidate.ModeId == modeId)
            {
                mode = candidate.ResolveDefaultProfile(defaultMonitorNtsc);
                return true;
            }
        }

        mode = default;
        return false;
    }

    private static bool IsModeAvailable(ModeRecord mode, bool supportsEcsDisplay)
        => supportsEcsDisplay || !mode.IsSuperHires;

    private static bool TryReadTags(
        IGraphicsMemory memory,
        uint address,
        out Dictionary<uint, uint> tags)
    {
        tags = new Dictionary<uint, uint>();
        if (address == 0)
            return true;

        var visited = new HashSet<uint>();
        for (var count = 0; address != 0 && count < 512; count++)
        {
            if ((address & 1u) != 0 ||
                !visited.Add(address) ||
                // A TagItem is exactly eight bytes.  $FFFF_FFF8 is the last
                // aligned guest address whose envelope ends at
                // $FFFF_FFFF; reject only starts that would require a byte
                // beyond the 32-bit address space.
                address > uint.MaxValue - 7u ||
                !memory.TryReadLong(address, out var tag) ||
                !memory.TryReadLong(address + 4u, out var value))
            {
                return false;
            }

            var nextAddress = address + 8u;
            switch (tag)
            {
                case 0:
                    return true;
                case 1:
                    if (nextAddress < address)
                        return false;
                    address = nextAddress;
                    continue;
                case 2:
                    if (value == 0)
                        return true;
                    if ((value & 1u) != 0)
                        return false;
                    address = value;
                    continue;
                case 3:
                {
                    var skipBytes = (ulong)value * 8ul;
                    if (skipBytes > uint.MaxValue ||
                        nextAddress < address ||
                        nextAddress > uint.MaxValue - (uint)skipBytes)
                    {
                        return false;
                    }

                    address = nextAddress + (uint)skipBytes;
                    continue;
                }
                default:
                    if (nextAddress < address)
                        return false;

                    tags[tag] = value;
                    address = nextAddress;
                    continue;
            }
        }

        return address == 0;
    }

    private static bool TryGetWordTag(
        Dictionary<uint, uint> tags,
        uint tag,
        ushort fallback,
        out ushort value)
    {
        if (!tags.TryGetValue(tag, out var raw))
        {
            value = fallback;
            return true;
        }

        if (raw > ushort.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (ushort)raw;
        return true;
    }

    private static bool TryGetByteTag(
        Dictionary<uint, uint> tags,
        uint tag,
        byte fallback,
        out byte value)
    {
        if (!tags.TryGetValue(tag, out var raw))
        {
            value = fallback;
            return true;
        }

        if (raw > byte.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (byte)raw;
        return true;
    }

    private static uint GetLongTag(Dictionary<uint, uint> tags, uint tag, uint fallback)
        => tags.TryGetValue(tag, out var value) ? value : fallback;

    private static byte[] BuildData(
        ModeRecord mode,
        uint tag,
        Func<uint, uint>? monitorSpecProvider,
        bool supportsEcsDisplay,
        bool supportsAgaDisplay,
        Func<uint, GraphicsMonitorViewPosition>? monitorPositionProvider,
        Func<uint, GraphicsMonitorViewPosition>? monitorOriginalPositionProvider,
        Func<uint, byte[]?>? monitorInfoImageProvider)
    {
        var suppliedMonitorImageData = tag == DtagMntr
            ? monitorInfoImageProvider?.Invoke(mode.ModeId)
            : null;
        var suppliedMonitorImage = suppliedMonitorImageData is not null;
        var data = tag switch
        {
            DtagDisp => BuildDisplayInfo(mode, supportsEcsDisplay, supportsAgaDisplay),
            DtagDims => BuildDimensions(mode, supportsAgaDisplay),
            DtagMntr => suppliedMonitorImageData
                ?? BuildMonitorInfo(mode, monitorSpecProvider, monitorPositionProvider, monitorOriginalPositionProvider),
            DtagName => BuildNameInfo(mode),
            _ => Array.Empty<byte>()
        };

        if (data.Length == 0)
            return data;

        if (suppliedMonitorImage)
            return data;

        WriteLong(data, 0x00, tag);
        // Public verified record headers identify the selected database record,
        // not the caller's default alias. Resolve only this published identity:
        // ModeId remains the adapter handle used by enumeration and providers.
        var monitorId = mode.ModeId & MonitorMask;
        var headerId = mode.ModeId;
        if (tag is DtagDisp or DtagDims or DtagMntr)
        {
            if (monitorId == GraphicsModeIds.DefaultMonitor)
                monitorId = mode.IsPal ? GraphicsModeIds.PalMonitor : GraphicsModeIds.NtscMonitor;
            headerId = tag == DtagMntr ? monitorId : monitorId | (mode.ModeId & ModeKeyMask);
        }
        WriteLong(data, 0x04, headerId);
        WriteLong(data, 0x08, TagSkip);
        // Verified Kickstart records exclude the reserved terminator from
        // Length. This metadata is not the number of bytes transferred (DIMS
        // in particular has structured copy boundaries). NAME's existing
        // policy remains separate until a named ROM record is observed.
        WriteLong(data, 0x0C, tag switch
        {
            DtagDisp => 4u,
            DtagDims => 8u,
            DtagMntr => 9u,
            _ => (uint)((data.Length - 16) / 8)
        });
        return data;
    }

    private static byte[] BuildDisplayInfo(
        ModeRecord mode,
        bool supportsEcsDisplay,
        bool supportsAgaDisplay)
    {
        var data = new byte[DisplayInfoChunkSize];
        var properties = mode.GetProperties(supportsAgaDisplay);
        var pixelSpeed = mode.HorizontalTicksPerPixel switch
        {
            1 => (ushort)35,
            2 => (ushort)70,
            _ => (ushort)140
        };

        WriteWord(data, 0x10, (ushort)ModeNotAvailable(mode.ModeId, supportsEcsDisplay));
        WriteLong(data, 0x12, properties);
        WriteWord(data, 0x16, mode.HorizontalTicksPerPixel);
        WriteWord(data, 0x18, 1);
        WriteWord(data, 0x1A, pixelSpeed);
        // ECS SuperHires still exposes the eight standard hardware sprites;
        // only attached-sprite mode is unavailable.  Keep the numeric count
        // consistent with DIPF_IS_SPRITES while the property mask below
        // clears DIPF_IS_SPRITES_ATT.
        WriteWord(data, 0x1C, (ushort)((properties & DipfIsSprites) != 0 ? 8 : 0));
        // PaletteRange and the component bit counts describe the native
        // colour register precision, not the number of bitplanes.  OCS/ECS
        // lores/hires modes expose 4 bits per RGB component (4096 entries),
        // while progressive ECS SuperHires exposes 2 bits per component
        // (64 entries).  Interlaced SuperHires uses the full 4-bit colour
        // register precision, even though it remains a two-plane mode.
        var componentBits = mode.GetComponentBits(supportsAgaDisplay);
        var paletteRange = 1u << (componentBits * 3);
        WriteWord(data, 0x1E, (ushort)Math.Min(paletteRange, ushort.MaxValue));
        // ECS standard sprites remain 140 ns in lores/hires, while their
        // SuperHires positioning granularity is 70 ns (every other 35 ns
        // playfield pixel).  The public Point is expressed in the same
        // ticks-per-pixel units as Resolution.
        WriteWord(data, 0x20, (ushort)(mode.IsSuperHires ? 2 : 4));
        WriteWord(data, 0x22, 1);
        data[0x28] = (byte)componentBits;
        data[0x29] = (byte)componentBits;
        data[0x2A] = (byte)componentBits;
        return data;
    }

    private static byte[] BuildDimensions(ModeRecord mode, bool supportsAgaDisplay)
    {
        var data = new byte[0x58];
        var maxWidth = mode.MaxOverscanWidth;
        var maxHeight = mode.MaxOverscanHeight;
        // Overscan Rectangle coordinates are signed and relative to the
        // nominal display, not zero-origin allocation extents. Independent
        // OCS/ECS PAL/NTSC records distinguish lores/hires VideoOScan width
        // from MaxOScan; SuperHires uses the same 1440-pixel span for both.
        var left = -36 * (4 / mode.HorizontalTicksPerPixel);
        var top = (mode.IsPal ? -15 : -23) * (mode.IsLace ? 2 : 1);
        var videoWidth = mode.IsSuperHires ? 1440 : mode.IsHires ? 736 : 368;
        var bottom = (ushort)(top + maxHeight - 1);
        WriteWord(data, 0x10, mode.GetDepth(supportsAgaDisplay));
        // Raster allocation limits are not nominal or overscan dimensions.
        // Independent Kickstart 3.1 OCS/ECS PAL/NTSC records use the same
        // height and maximum limits, with resolution-specific minimum width.
        WriteWord(data, 0x12, mode.IsSuperHires ? (ushort)64 : mode.IsHires ? (ushort)32 : (ushort)16);
        WriteWord(data, 0x14, 1);
        WriteWord(data, 0x16, 1008);
        WriteWord(data, 0x18, 1024);
        WriteRectangle(data, 0x1A, mode.Width, mode.Height); // Nominal
        WriteRectangle(data, 0x22, unchecked((ushort)left), unchecked((ushort)top),
            (ushort)(left + maxWidth - 1), bottom); // MaxOScan
        WriteRectangle(data, 0x2A, unchecked((ushort)left), unchecked((ushort)top),
            (ushort)(left + videoWidth - 1), bottom); // VideoOScan
        WriteRectangle(data, 0x32, mode.Width, mode.Height); // TxtOScan
        WriteRectangle(data, 0x3A, mode.Width, mode.Height); // StdOScan

        return data;
    }

    private static byte[] BuildMonitorInfo(
        ModeRecord mode,
        Func<uint, uint>? monitorSpecProvider,
        Func<uint, GraphicsMonitorViewPosition>? monitorPositionProvider,
        Func<uint, GraphicsMonitorViewPosition>? monitorOriginalPositionProvider)
    {
        // Reuse the resident guest MonitorSpec owned by the monitor registry.
        // DTAG_MNTR lazily creates that resident node when a display record is
        // queried before OpenMonitor, and keeps it addressable after the
        // public open count reaches zero; the display database must not
        // manufacture a second allocation or make DTAG_MNTR handles transient.
        var monitorSpec = monitorSpecProvider?.Invoke(mode.ModeId) ?? 0;
        var viewPosition = monitorPositionProvider?.Invoke(mode.ModeId)
            ?? GraphicsMonitorViewPosition.BootDefault;
        var originalPosition = monitorOriginalPositionProvider?.Invoke(mode.ModeId)
            ?? GraphicsMonitorViewPosition.BootDefault;
        return GraphicsMonitorInfoImage.Create(mode.IsPal, monitorSpec, viewPosition, originalPosition);
    }

    private static byte[] BuildNameInfo(ModeRecord mode)
    {
        var data = new byte[0x38];
        var name = Encoding.ASCII.GetBytes(mode.Name);
        Array.Copy(name, 0, data, 0x10, Math.Min(31, name.Length));
        return data;
    }

    private static void WriteRectangle(byte[] data, int offset, ushort width, ushort height)
    {
        WriteRectangle(data, offset, 0, 0, (ushort)(width - 1), (ushort)(height - 1));
    }

    private static void WriteRectangle(
        byte[] data,
        int offset,
        ushort minX,
        ushort minY,
        ushort maxX,
        ushort maxY)
    {
        WriteWord(data, offset + 0, minX);
        WriteWord(data, offset + 2, minY);
        WriteWord(data, offset + 4, maxX);
        WriteWord(data, offset + 6, maxY);
    }

    private static void WriteWord(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void WriteLong(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private readonly struct ModeRecord
    {
        internal ModeRecord(
            uint modeId,
            ushort width,
            ushort height,
            bool hires,
            bool pal,
            string name,
            ushort? depth = null)
        {
            ModeId = modeId;
            Width = width;
            Height = height;
            IsHires = hires;
            IsPal = pal;
            IsLace = (modeId & GraphicsModeIds.InterlaceMode) != 0;
            // Kickstart's mode-specific maximum is not a uniform six-plane
            // hardware limit: ordinary lores uses five, hires four, and
            // lores HAM/EHB/dual-playfield six. Superhires explicitly supplies
            // two in the mode table. Keep selection and DIMS on the same value.
            Depth = depth ?? (ushort)(hires ? 4 :
                (modeId & (GraphicsModeIds.HamKey | GraphicsModeIds.ExtraHalfBriteKey |
                           GraphicsModeIds.LoresDualPlayfieldKey)) != 0 ? 6 : 5);
            Name = name;
        }

        internal uint ModeId { get; }
        internal ushort Width { get; }
        internal ushort Height { get; }
        internal bool IsHires { get; }
        internal bool IsSuperHires
            => (ModeId & GraphicsModeIds.SuperHiresMode) != 0;
        internal bool IsPal { get; }
        internal bool IsLace { get; }
        internal ushort Depth { get; }
        internal string Name { get; }
        internal ushort GetDepth(bool supportsAgaDisplay)
            => supportsAgaDisplay ? (ushort)8 : Depth;
        internal byte GetComponentBits(bool supportsAgaDisplay)
            => supportsAgaDisplay
                ? (byte)8
                : IsSuperHires && !IsLace ? (byte)2 : (byte)4;
        internal byte ComponentBits => GetComponentBits(false);
        internal ushort HorizontalTicksPerPixel
            => (ModeId & GraphicsModeIds.SuperHiresMode) != 0
                ? (ushort)1
                : IsHires ? (ushort)2 : (ushort)4;
        internal ushort MaxOverscanWidth
            => HorizontalTicksPerPixel switch
            {
                1 => 1440,
                2 => 724,
                _ => 362
            };
        internal ushort MaxOverscanHeight
            => (ushort)(GraphicsNativeMonitorTiming.For(IsPal).MaxDisplayHeight *
                        (IsLace ? 2 : 1));
        internal uint GetProperties(bool supportsAgaDisplay)
        {
            // OCS/ECS standard planar modes support attached sprite
            // pairs. ECS SuperHires retains standard sprites but does
            // not support attached-sprite mode.
            var properties = DipfIsSprites |
                             DipfIsSpritesAttached |
                             DipfIsWb |
                             DipfIsDbuffer;
            if ((ModeId & GraphicsModeIds.SuperHiresMode) != 0)
            {
                // ECS SuperHires is a 35 ns, four-colour native mode. It
                // retains standard sprites but is not an attached-sprite
                // mode; advertise the ECS requirement explicitly.
                properties &= ~DipfIsSpritesAttached;
                properties |= DipfIsEcs;
            }
            if (IsLace)
                properties |= DipfIsLace;
            if (IsPal)
                properties |= DipfIsPal;
            var key = (ushort)(ModeId & 0xFFFFu);
            if ((key & GraphicsModeIds.DualPlayfieldMode) != 0)
                properties |= DipfIsDualPf;
            if ((key & GraphicsModeIds.PlayfieldBitAssignment) != 0)
                properties |= DipfIsPf2Pri;
            if ((key & GraphicsModeIds.HamMode) != 0)
                properties |= DipfIsHam;
            if ((key & GraphicsModeIds.ExtraHalfBriteMode) != 0)
                properties |= DipfIsExtraHalfBrite;
            if (supportsAgaDisplay)
                properties |= DipfIsAa;
            return properties;
        }
        internal uint Properties => GetProperties(false);

        internal ModeRecord ResolveDefaultProfile(bool defaultMonitorNtsc)
        {
            if (!defaultMonitorNtsc ||
                (ModeId & MonitorMask) != GraphicsModeIds.DefaultMonitor)
            {
                return this;
            }

            var profileName = Name.StartsWith("Default", StringComparison.Ordinal)
                ? "NTSC" + Name.Substring("Default".Length)
                : Name;
            var height = IsLace ? (ushort)400 : (ushort)200;
            return new ModeRecord(
                ModeId,
                Width,
                height,
                IsHires,
                pal: false,
                profileName,
                Depth);
        }
    }
}
