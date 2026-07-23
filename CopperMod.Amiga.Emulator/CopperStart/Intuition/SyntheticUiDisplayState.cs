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
    public uint UserPortAddress { get; set; }
    public uint MessageAddress { get; set; }
    public uint HostObjectAddress { get; set; }
    public uint ViewAddress { get; set; }
    public uint RasInfoAddress { get; set; }
    public uint BitMapAddress { get; set; }
    public uint RastPortAddress { get; set; }
    public uint FontAddress { get; set; }
    public uint PlaneAddress { get; set; }
    public uint GadgetListAddress { get; set; }
    public uint UserPortSignalMask { get; set; }
    public uint IdcmpFlags { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int ScreenDepth { get; set; }
    public int WindowLeft { get; set; }
    public int WindowTop { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public ushort ScreenViewModes { get; set; }
    public ushort[] Palette { get; } = new ushort[32];
    public bool PaletteLoaded { get; set; }

    public void Reset()
    {
        ScreenAddress = WindowAddress = UserPortAddress = MessageAddress = HostObjectAddress = ViewAddress = 0;
        RasInfoAddress = BitMapAddress = RastPortAddress = FontAddress = PlaneAddress = GadgetListAddress = 0;
        UserPortSignalMask = IdcmpFlags = 0;
        ScreenWidth = _defaultWidth;
        ScreenHeight = _defaultHeight;
        ScreenDepth = _defaultDepth;
        WindowLeft = WindowTop = 0;
        WindowWidth = _defaultWidth;
        WindowHeight = _defaultHeight;
        ScreenViewModes = 0;
        PaletteLoaded = false;
        System.Array.Clear(Palette);
    }
}
