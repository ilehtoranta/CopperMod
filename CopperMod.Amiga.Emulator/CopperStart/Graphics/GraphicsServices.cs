using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// CopperStart's graphics.library contribution point.
///
/// This intentionally owns no broad fallback table.  ROM mode will register
/// only LVOs added here after their native behavior has been migrated and
/// tested; all remaining graphics.library vectors continue through Kickstart.
/// </summary>
internal sealed class GraphicsServices
{
    private const int ViewBytes = 0x12;
    private const int ViewPortBytes = 0x28;
    private const int RastPortFgPenOffset = 0x19;
    private const int RastPortBgPenOffset = 0x1A;
    private const int RastPortDrawModeOffset = 0x1C;
    private const int RastPortCurrentXOffset = 0x24, RastPortCurrentYOffset = 0x26;
    private const int RastPortFontOffset = 0x34, RastPortTextHeightOffset = 0x3A, RastPortTextWidthOffset = 0x3C, RastPortTextBaselineOffset = 0x3E, RastPortTextSpacingOffset = 0x40;
    private readonly CopperStartGraphicsContext _context;

    public GraphicsServices(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Dispatches the CopperStart graphics.library compatibility table.</summary>
    public void Invoke(M68kCpuState state, int displacement)
    {
        _context.LogCall("graphics.library", displacement);
        switch (displacement)
        {
            case -30: state.D[0] = _context.BltBitMap(state); return;
            case -552: state.D[0] = _context.ClipBlit(state); return;
            case -606: state.D[0] = _context.BltBitMapRastPort(state); return;
            case -918: state.D[0] = _context.AllocBitMap(state); return;
            case -924: _context.FreeBitMap(state.A[0]); state.D[0] = 0; return;
            case -942: state.D[0] = _context.ChangeViewPortBitMap(state.A[0], state.A[1]); return;
            case -960: state.D[0] = _context.GetBitMapAttr(state.A[0], state.D[1]); return;
            case -0xD2: state.D[0] = _context.MergeCopperLists(state); return;
            case -0xD8: state.D[0] = _context.MakeViewPort(state); return;
            case -0xDE: _context.LoadView(state); state.D[0] = 0; return;
            case -0x36: state.D[0] = TextLength(state); return;
            case -0x3C: state.D[0] = Text(state); return;
            case -0x42: state.D[0] = SetFont(state); return;
            case -0x48: state.D[0] = _context.EnsureCompatibilityFont(); return;
            case -0x4E: state.D[0] = 0; return;
            case -0xC0: _context.LoadRgb4(state); state.D[0] = 0; return;
            case -0xEA: state.D[0] = SetRast(state); return;
            case -0xF0: state.D[0] = Move(state); return;
            case -0xF6: state.D[0] = Draw(state); return;
            case -0x132: state.D[0] = RectFill(state); return;
            case -0x156: state.D[0] = SetAPen(state); return;
            case -0x15C: state.D[0] = SetBPen(state); return;
            case -0x162: state.D[0] = SetDrMd(state); return;
            case -0x168: state.D[0] = InitView(state); return;
            case -0xCC: state.D[0] = InitVPort(state); return;
            case -0x120: _context.SetRgb4(state); state.D[0] = 0; return;
            case -0xE4:
            case -0x1C8:
            case -0x1CE: state.D[0] = 0; return;
            case -0x10E: state.D[0] = WaitTof(state); return;
            default: state.D[0] = _context.EnsureCompatibilityHostObject(); return;
        }
    }

    /// <summary>Executes the migrated WaitTOF service using the scheduler path.</summary>
    public uint WaitTof(M68kCpuState state)
    {
        _context.WaitTof(state);
        return 0;
    }

    /// <summary>
    /// Writes a custom register through the context's emulated-bus callback.
    /// It is deliberately not implemented with HostGuestMemory.
    /// </summary>
    public void WriteCustomRegister(uint address, ushort value, long cycles)
        => _context.WriteCustomRegister(address, value, cycles);

    public uint InitView(M68kCpuState state)
    {
        Clear(state.A[1], ViewBytes);
        return 0;
    }

    public uint InitVPort(M68kCpuState state)
    {
        var viewPort = state.A[0];
        if (!Clear(viewPort, ViewPortBytes)) return 0;
        _context.InitializeCompatibilityViewPort(viewPort);
        return 0;
    }

    public uint SetAPen(M68kCpuState state) => SetRastPortByte(state.A[1], RastPortFgPenOffset, state.D[0]);
    public uint SetBPen(M68kCpuState state) => SetRastPortByte(state.A[1], RastPortBgPenOffset, state.D[0]);
    public uint SetDrMd(M68kCpuState state) => SetRastPortByte(state.A[1], RastPortDrawModeOffset, state.D[0]);
    public uint TextLength(M68kCpuState state) => Math.Min(state.D[0], 512u) * 8u;
    public uint SetFont(M68kCpuState state)
    {
        var rastPort = state.A[1]; if (!_context.IsMappedRastPort(rastPort)) return 0;
        var font = state.A[0] != 0 ? state.A[0] : _context.EnsureCompatibilityFont();
        _context.Memory.WriteLong(rastPort + RastPortFontOffset, font);
        _context.Memory.WriteWord(rastPort + RastPortTextHeightOffset, 8); _context.Memory.WriteWord(rastPort + RastPortTextWidthOffset, 8);
        _context.Memory.WriteWord(rastPort + RastPortTextBaselineOffset, 7); _context.Memory.WriteWord(rastPort + RastPortTextSpacingOffset, 0); return 0;
    }
    public uint Move(M68kCpuState state)
    {
        var rastPort = state.A[1]; if (!_context.IsMappedRastPort(rastPort)) return 0;
        _context.Memory.WriteWord(rastPort + RastPortCurrentXOffset, unchecked((ushort)(short)state.D[0])); _context.Memory.WriteWord(rastPort + RastPortCurrentYOffset, unchecked((ushort)(short)state.D[1])); return 0;
    }
    public uint Draw(M68kCpuState state) { _context.Draw(state); return 0; }
    public uint Text(M68kCpuState state) { _context.Text(state); return 0; }
    public uint SetRast(M68kCpuState state) { _context.SetRast(state); return 0; }
    public uint RectFill(M68kCpuState state) { _context.RectFill(state); return 0; }

    private uint SetRastPortByte(uint rastPort, int offset, uint value)
    {
        if (_context.Memory.IsMapped(rastPort + (uint)offset, 1)) _context.Memory.WriteByte(rastPort + (uint)offset, (byte)value);
        return 0;
    }

    private bool Clear(uint address, int bytes)
    {
        if (address == 0 || !_context.Memory.IsMapped(address, bytes)) return false;
        for (var offset = 0; offset < bytes; offset++) _context.Memory.WriteByte(address + (uint)offset, 0);
        return true;
    }
}
