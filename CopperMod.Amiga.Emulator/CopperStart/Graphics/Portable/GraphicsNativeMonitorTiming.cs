namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// The standard OCS/ECS monitor envelope shared by the portable display
/// database and the resident <c>MonitorSpec</c> allocator.
///
/// Keeping these values in one immutable record is important for the native
/// 68k path: <c>DTAG_MNTR</c>, <c>MonitorSpec</c>, and the host copper builder
/// share physical scan timing. MonitorInfo's ViewPositionRange, resolution,
/// and compatibility are separate family-record metadata; see
/// <see cref="GraphicsMonitorInfoImage"/> rather than reusing LegalView here.
/// </summary>
internal readonly record struct GraphicsNativeMonitorTiming(
    bool IsPal,
    ushort TotalRows,
    ushort TotalColorClocks,
    ushort DeniseMinDisplayColumn,
    ushort DeniseMaxDisplayColumn,
    ushort BeamCon0,
    ushort MinRow,
    short Compatibility,
    ushort MaxDisplayHeight,
    ushort LegalViewLeft,
    ushort LegalViewTop,
    ushort LegalViewRight,
    ushort LegalViewBottom)
{
    internal const ushort StandardColorClocks = 226;
    internal const ushort StandardDeniseMin = 93;
    internal const ushort StandardDeniseMax = 455;
    internal const ushort NtscRows = 262;
    internal const ushort PalRows = 312;
    internal const ushort NtscMinRow = 21;
    internal const ushort PalMinRow = 29;
    internal const ushort NtscMaxDisplayHeight = 241;
    internal const ushort PalMaxDisplayHeight = 283;
    internal const ushort PalBeamCon = 0x0020;

    internal static GraphicsNativeMonitorTiming For(bool pal)
        => pal
            ? new(
                IsPal: true,
                TotalRows: PalRows,
                TotalColorClocks: StandardColorClocks,
                DeniseMinDisplayColumn: StandardDeniseMin,
                DeniseMaxDisplayColumn: StandardDeniseMax,
                BeamCon0: PalBeamCon,
                MinRow: PalMinRow,
                Compatibility: 1,
                MaxDisplayHeight: PalMaxDisplayHeight,
                LegalViewLeft: 0,
                LegalViewTop: 0,
                LegalViewRight: StandardDeniseMax,
                LegalViewBottom: PalRows - 1)
            : new(
                IsPal: false,
                TotalRows: NtscRows,
                TotalColorClocks: StandardColorClocks,
                DeniseMinDisplayColumn: StandardDeniseMin,
                DeniseMaxDisplayColumn: StandardDeniseMax,
                BeamCon0: 0,
                MinRow: NtscMinRow,
                Compatibility: 1,
                MaxDisplayHeight: NtscMaxDisplayHeight,
                LegalViewLeft: 0,
                LegalViewTop: 0,
                LegalViewRight: StandardDeniseMax,
                LegalViewBottom: NtscRows - 1);
}
