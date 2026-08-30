/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>
/// Reset-scoped guest backing objects and geometry for CopperStart's synthetic UI.
/// This state intentionally contains no bus, copper, or scheduler operations.
/// </summary>
internal sealed class SyntheticUiDisplayState
{
    private readonly int _defaultWidth;
    private readonly int _defaultHeight;
    private readonly int _defaultDepth;

    public SyntheticUiDisplayState(int defaultWidth, int defaultHeight, int defaultDepth)
    {
        _defaultWidth = defaultWidth;
        _defaultHeight = defaultHeight;
        _defaultDepth = defaultDepth;
        Reset();
    }

    public uint ScreenAddress { get; set; }
    public uint WindowAddress { get; set; }
    /// <summary>
    /// Whether the compatibility window has been explicitly returned by
    /// OpenWindow.  OpenScreen keeps a private Window-shaped backing object
    /// for RastPort presentation; that object is not an open Intuition
    /// window and must not by itself block CloseScreen.
    /// </summary>
    public bool WindowOpen { get; set; }
    /// <summary>
    /// Number of explicit OpenWindow claims represented by the shared
    /// compatibility window.  The host bridge reuses one reset-scoped
    /// Window-shaped object, but CloseScreen must remain refused until every
    /// matching CloseWindow has retired its claim.
    /// </summary>
    public uint WindowOpenCount { get; set; }
    public uint UserPortAddress { get; set; }
    public uint MessageAddress { get; set; }
    public uint HostObjectAddress { get; set; }
    public uint ViewAddress { get; set; }
    public uint RasInfoAddress { get; set; }
    public uint SecondRasInfoAddress { get; set; }
    public uint BitMapAddress { get; set; }
    /// <summary>
    /// Caller-owned standard-planar bitmap selected by SA_BitMap.  The
    /// synthetic screen may borrow this header and its plane storage, but it
    /// must never release or overwrite the caller's bitmap on teardown.
    /// </summary>
    public uint CustomBitMapAddress { get; set; }
    /// <summary>
    /// Optional ColorMap allocated for a synthetic screen that carries
    /// SA_VideoControl.  The map is a graphics-library-owned guest object,
    /// but its lifetime follows the Intuition Screen/ViewPort session.
    /// </summary>
    public uint ColorMapAddress { get; set; }
    public uint RastPortAddress { get; set; }
    public uint FontAddress { get; set; }
    /// <summary>
    /// Guest <c>TextFont *</c> opened for the embedded Screen.RastPort. The
    /// compatibility screen may open a caller-selected TextAttr against the
    /// resident font list; the boolean records whether CloseScreen must
    /// release that accessor.
    /// </summary>
    public uint ScreenTextFontAddress { get; set; }
    public bool ScreenTextFontOpened { get; set; }
    /// <summary>
    /// Guest <c>Screen.Font</c> (a <c>TextAttr *</c>), separate from the
    /// RastPort's opened <c>TextFont *</c>.  Keeping this as its own handle
    /// preserves the public 68k Screen layout for native callers.
    /// </summary>
    public uint ScreenFontAttrAddress { get; set; }
    /// <summary>
    /// Whether the synthetic Window.RPort must keep using GfxBase.DefaultFont
    /// for an SA_SysFont=1 screen while the embedded Screen.RastPort uses the
    /// screen-preference font.  The compatibility preference currently maps
    /// to the reset-scoped default, but retaining the distinction keeps the
    /// guest layout ready for a native preference provider.
    /// </summary>
    public bool WindowUsesDefaultFont { get; set; }
    public uint ScreenDefaultTitleAddress { get; set; }
    public uint PlaneAddress { get; set; }
    public uint GadgetListAddress { get; set; }
    public uint UserPortSignalMask { get; set; }
    public uint IdcmpFlags { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int ScreenDepth { get; set; }
    public int ScreenLeft { get; set; }
    public int ScreenTop { get; set; }
    public byte ScreenDetailPen { get; set; }
    public byte ScreenBlockPen { get; set; }
    public ushort ScreenFlags { get; set; }
    public bool ScreenInterleaved { get; set; }
    public int WindowLeft { get; set; }
    public int WindowTop { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public ushort ScreenViewModes { get; set; }
    // The RGB4 projection remains useful for OCS/ECS copper-list building,
    // while the RGB32 shadow feeds AGA's eight colour-register banks. The
    // active profile/depth controls how much of these 256-entry stores is
    // published; retaining the full standard-planar capacity here keeps a
    // successful eight-plane screen self-contained across RethinkDisplay.
    public ushort[] Palette { get; } = new ushort[256];
    public uint[] PaletteRgb32 { get; } = new uint[256];
    public bool PaletteLoaded { get; set; }
    public bool PaletteRgb32Loaded { get; set; }

    public void Reset()
    {
        ScreenAddress = WindowAddress = UserPortAddress = MessageAddress = HostObjectAddress = ViewAddress = 0;
        WindowOpen = false;
        WindowOpenCount = 0;
        RasInfoAddress = SecondRasInfoAddress = BitMapAddress = CustomBitMapAddress = ColorMapAddress = RastPortAddress = FontAddress = ScreenFontAttrAddress = ScreenTextFontAddress = PlaneAddress = GadgetListAddress = 0;
        ScreenTextFontOpened = false;
        WindowUsesDefaultFont = false;
        ScreenDefaultTitleAddress = 0;
        UserPortSignalMask = IdcmpFlags = 0;
        ScreenWidth = _defaultWidth;
        ScreenHeight = _defaultHeight;
        ScreenDepth = _defaultDepth;
        ScreenLeft = ScreenTop = 0;
        ScreenDetailPen = 0;
        ScreenBlockPen = 1;
        // Intuition shows a screen title by default; SA_ShowTitle(FALSE) or
        // ShowTitle(screen, FALSE) can explicitly clear this bit later.
        // A synthetic screen is a custom Intuition screen by default.  Keep
        // the classic CUSTOMSCREEN type nibble alongside SHOWTITLE so the
        // published Screen envelope can be cloned by native-style callers.
        ScreenFlags = 0x001F;
        ScreenInterleaved = false;
        WindowLeft = WindowTop = 0;
        WindowWidth = _defaultWidth;
        WindowHeight = _defaultHeight;
        ScreenViewModes = 0;
        PaletteLoaded = false;
        PaletteRgb32Loaded = false;
        System.Array.Clear(Palette);
        System.Array.Clear(PaletteRgb32);
    }
}
