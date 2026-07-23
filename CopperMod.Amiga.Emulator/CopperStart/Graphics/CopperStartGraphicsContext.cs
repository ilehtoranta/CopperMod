using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// Reset-scoped concrete bridge for CopperStart graphics services.
///
/// Ordinary graphics structures (View, ViewPort, RastPort and BitMap) may use
/// <see cref="Memory"/>.  Any operation that reaches the custom chips or changes
/// display scheduling must use its explicit callback instead, retaining normal
/// bus side effects and ordering.
/// </summary>
internal sealed class CopperStartGraphicsContext
{
    public CopperStartGraphicsContext(
        HostGuestMemory memory,
        Action<M68kCpuState> waitTof,
        Action<uint, ushort, long> writeCustomRegister,
        Action<uint> selectFrontViewPort,
        Action requestDisplayRebuild,
        Action<uint> initializeCompatibilityViewPort,
        Func<uint, bool> isMappedRastPort,
        Func<uint> ensureCompatibilityFont,
        Action<string, int> logCall,
        Func<M68kCpuState, uint> bltBitMap,
        Func<M68kCpuState, uint> clipBlit,
        Func<M68kCpuState, uint> bltBitMapRastPort,
        Func<M68kCpuState, uint> allocBitMap,
        Action<uint> freeBitMap,
        Func<uint, uint, uint> getBitMapAttr,
        Func<uint, uint, uint> changeViewPortBitMap,
        Func<M68kCpuState, uint> mergeCopperLists,
        Func<M68kCpuState, uint> makeViewPort,
        Action<M68kCpuState> loadView,
        Action<M68kCpuState> loadRgb4,
        Action<M68kCpuState> setRgb4,
        Func<uint> ensureCompatibilityHostObject,
        Action<M68kCpuState> draw,
        Action<M68kCpuState> text,
        Action<M68kCpuState> setRast,
        Action<M68kCpuState> rectFill)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        WaitTof = waitTof ?? throw new ArgumentNullException(nameof(waitTof));
        WriteCustomRegister = writeCustomRegister ?? throw new ArgumentNullException(nameof(writeCustomRegister));
        SelectFrontViewPort = selectFrontViewPort ?? throw new ArgumentNullException(nameof(selectFrontViewPort));
        RequestDisplayRebuild = requestDisplayRebuild ?? throw new ArgumentNullException(nameof(requestDisplayRebuild));
        InitializeCompatibilityViewPort = initializeCompatibilityViewPort ?? throw new ArgumentNullException(nameof(initializeCompatibilityViewPort));
        IsMappedRastPort = isMappedRastPort ?? throw new ArgumentNullException(nameof(isMappedRastPort));
        EnsureCompatibilityFont = ensureCompatibilityFont ?? throw new ArgumentNullException(nameof(ensureCompatibilityFont));
        LogCall = logCall ?? throw new ArgumentNullException(nameof(logCall));
        BltBitMap = bltBitMap ?? throw new ArgumentNullException(nameof(bltBitMap));
        ClipBlit = clipBlit ?? throw new ArgumentNullException(nameof(clipBlit));
        BltBitMapRastPort = bltBitMapRastPort ?? throw new ArgumentNullException(nameof(bltBitMapRastPort));
        AllocBitMap = allocBitMap ?? throw new ArgumentNullException(nameof(allocBitMap));
        FreeBitMap = freeBitMap ?? throw new ArgumentNullException(nameof(freeBitMap));
        GetBitMapAttr = getBitMapAttr ?? throw new ArgumentNullException(nameof(getBitMapAttr));
        ChangeViewPortBitMap = changeViewPortBitMap ?? throw new ArgumentNullException(nameof(changeViewPortBitMap));
        MergeCopperLists = mergeCopperLists ?? throw new ArgumentNullException(nameof(mergeCopperLists));
        MakeViewPort = makeViewPort ?? throw new ArgumentNullException(nameof(makeViewPort));
        LoadView = loadView ?? throw new ArgumentNullException(nameof(loadView));
        LoadRgb4 = loadRgb4 ?? throw new ArgumentNullException(nameof(loadRgb4));
        SetRgb4 = setRgb4 ?? throw new ArgumentNullException(nameof(setRgb4));
        EnsureCompatibilityHostObject = ensureCompatibilityHostObject ?? throw new ArgumentNullException(nameof(ensureCompatibilityHostObject));
        Draw = draw ?? throw new ArgumentNullException(nameof(draw));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        SetRast = setRast ?? throw new ArgumentNullException(nameof(setRast));
        RectFill = rectFill ?? throw new ArgumentNullException(nameof(rectFill));
    }

    public HostGuestMemory Memory { get; }
    public Action<M68kCpuState> WaitTof { get; }
    public Action<uint, ushort, long> WriteCustomRegister { get; }
    public Action<uint> SelectFrontViewPort { get; }
    public Action RequestDisplayRebuild { get; }
    public Action<uint> InitializeCompatibilityViewPort { get; }
    public Func<uint, bool> IsMappedRastPort { get; }
    public Func<uint> EnsureCompatibilityFont { get; }
    public Action<string, int> LogCall { get; }
    public Func<M68kCpuState, uint> BltBitMap { get; }
    public Func<M68kCpuState, uint> ClipBlit { get; }
    public Func<M68kCpuState, uint> BltBitMapRastPort { get; }
    public Func<M68kCpuState, uint> AllocBitMap { get; }
    public Action<uint> FreeBitMap { get; }
    public Func<uint, uint, uint> GetBitMapAttr { get; }
    public Func<uint, uint, uint> ChangeViewPortBitMap { get; }
    public Func<M68kCpuState, uint> MergeCopperLists { get; }
    public Func<M68kCpuState, uint> MakeViewPort { get; }
    public Action<M68kCpuState> LoadView { get; }
    public Action<M68kCpuState> LoadRgb4 { get; }
    public Action<M68kCpuState> SetRgb4 { get; }
    public Func<uint> EnsureCompatibilityHostObject { get; }
    public Action<M68kCpuState> Draw { get; }
    public Action<M68kCpuState> Text { get; }
    public Action<M68kCpuState> SetRast { get; }
    public Action<M68kCpuState> RectFill { get; }
}
