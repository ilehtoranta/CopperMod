using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Per-task Exec trap-bit allocation backed by the native Task fields.</summary>
internal sealed class ExecTrapServices
{
    private const int TrapAllocOffset = 0x22, TrapAbleOffset = 0x24;
    private readonly CopperStartExecContext _context;
    public ExecTrapServices(CopperStartExecContext context) => _context = context;
    public uint AllocTrap(M68kCpuState state)
    {
        var task = _context.GetCurrentTask(); if (task == 0 || !_context.Memory.IsMapped(task + TrapAbleOffset, 2)) return 0xFFFF_FFFF;
        var requested = unchecked((int)state.D[0]); var allocated = _context.Memory.ReadWord(task + TrapAllocOffset);
        if (requested is >= 0 and < 16) return Allocate(task, allocated, requested);
        if (requested != -1) return 0xFFFF_FFFF;
        for (var trap = 0; trap < 16; trap++) if ((allocated & (1 << trap)) == 0) return Allocate(task, allocated, trap);
        return 0xFFFF_FFFF;
    }
    public uint FreeTrap(M68kCpuState state)
    {
        var trap = unchecked((int)state.D[0]); var task = _context.GetCurrentTask(); if (trap is < 0 or >= 16 || task == 0) return 0;
        var mask = (ushort)~(1 << trap); _context.Memory.WriteWord(task + TrapAllocOffset, (ushort)(_context.Memory.ReadWord(task + TrapAllocOffset) & mask)); _context.Memory.WriteWord(task + TrapAbleOffset, (ushort)(_context.Memory.ReadWord(task + TrapAbleOffset) & mask)); return 0;
    }
    private uint Allocate(uint task, ushort allocated, int trap)
    {
        var mask = 1 << trap; if ((allocated & mask) != 0) return 0xFFFF_FFFF;
        _context.Memory.WriteWord(task + TrapAllocOffset, (ushort)(allocated | mask)); _context.Memory.WriteWord(task + TrapAbleOffset, (ushort)(_context.Memory.ReadWord(task + TrapAbleOffset) | mask)); return (uint)trap;
    }
}
