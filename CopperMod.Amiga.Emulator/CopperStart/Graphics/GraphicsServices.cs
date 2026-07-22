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
    private const int ViewBytes = 0x1C;
    private const int ViewPortBytes = 0x28;
    private const int RastPortFgPenOffset = 0x19;
    private const int RastPortBgPenOffset = 0x1A;
    private const int RastPortDrawModeOffset = 0x1C;
    private readonly CopperStartGraphicsContext _context;

    public GraphicsServices(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

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
