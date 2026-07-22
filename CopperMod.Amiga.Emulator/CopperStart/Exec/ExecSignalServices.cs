using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>ROM-task signal state. All state lives in guest Task fields.</summary>
internal sealed class ExecSignalServices
{
    private const int SigAlloc = 0x12, SigWait = 0x16, SigRecvd = 0x1A;
    private const int TaskReadyOffset = 0x196, TaskWaitOffset = 0x1A4;
    private readonly CopperStartExecContext _context;
    public ExecSignalServices(CopperStartExecContext context) => _context = context;
    public uint Wait(M68kCpuState state)
    {
        var task = _context.GetCurrentTask(); var pending = Read(task, SigRecvd); var delivered = pending & state.D[0];
        Write(task, SigWait, delivered == 0 ? state.D[0] : 0); Write(task, SigRecvd, pending & ~delivered); return delivered;
    }
    public uint SetSignal(M68kCpuState state)
    {
        var task = _context.GetCurrentTask(); var old = Read(task, SigRecvd); Write(task, SigRecvd, (old & ~state.D[1]) | (state.D[0] & state.D[1])); return old;
    }
    public uint Signal(M68kCpuState state)
    {
        var pending = Read(state.A[1], SigRecvd) | state.D[0]; Write(state.A[1], SigRecvd, pending);
        if ((pending & Read(state.A[1], SigWait)) != 0)
        {
            Write(state.A[1], SigWait, 0);
            _context.MoveTaskToList(state.A[1], _context.GetExecBase() + TaskReadyOffset, state);
            _context.RequestDispatch();
        }
        return 0;
    }
    public uint AllocSignal(M68kCpuState state)
    {
        var task = _context.GetCurrentTask(); var allocated = Read(task, SigAlloc); var requested = unchecked((int)state.D[0]);
        for (var bit = requested is >= 0 and < 32 ? requested : 0; bit < 32; bit++) if ((allocated & (1u << bit)) == 0) { Write(task, SigAlloc, allocated | (1u << bit)); return (uint)bit; }
        return 0xFFFF_FFFF;
    }
    public uint FreeSignal(M68kCpuState state)
    {
        var bit = unchecked((int)state.D[0]); if (bit is < 0 or >= 32) return 0; var task = _context.GetCurrentTask(); var mask = ~(1u << bit);
        Write(task, SigAlloc, Read(task, SigAlloc) & mask); Write(task, SigRecvd, Read(task, SigRecvd) & mask); return 0;
    }
    public uint WaitAndSchedule(M68kCpuState state)
    {
        var delivered = Wait(state);
        if (delivered == 0)
        {
            var task = _context.GetCurrentTask();
            _context.MoveTaskToList(task, _context.GetExecBase() + TaskWaitOffset, state);
            _context.RequestDispatch();
        }
        return delivered;
    }
    private uint Read(uint task, int offset) => task != 0 && _context.Memory.IsMapped(task + (uint)offset, 4) ? _context.Memory.ReadLong(task + (uint)offset) : 0;
    private void Write(uint task, int offset, uint value) { if (task != 0 && _context.Memory.IsMapped(task + (uint)offset, 4)) _context.Memory.WriteLong(task + (uint)offset, value); }
}
