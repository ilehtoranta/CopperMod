using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Guest-owned Exec message ports and queues.</summary>
internal sealed class ExecPortServices
{
    private const int PortListOffset = 0x188, MsgListOffset = 0x14, SigBitOffset = 0x0F, SigTaskOffset = 0x10, ReplyPortOffset = 0x0E;
    private const int SigRecvdOffset = 0x1A;
    private readonly CopperStartExecContext _context;
    private readonly ExecListServices _lists;

    public ExecPortServices(CopperStartExecContext context, ExecListServices lists) { _context = context; _lists = lists; }

    public uint AddPort(M68kCpuState state)
    {
        var port = state.A[1]; if (!IsPort(port)) return 0;
        _lists.Ensure(port + MsgListOffset); var ports = _context.GetExecBase() + PortListOffset; _lists.Ensure(ports);
        if (!_lists.Contains(ports, port)) _lists.AddTail(ports, port); return 0;
    }
    public uint RemPort(M68kCpuState state)
    {
        var port = state.A[1]; var ports = _context.GetExecBase() + PortListOffset;
        if (IsPort(port) && _lists.Contains(ports, port)) _lists.Remove(port); return 0;
    }
    public uint PutMsg(M68kCpuState state)
    {
        var port = state.A[0]; var message = state.A[1];
        if (!IsPort(port) || !IsMessage(message) || !_lists.Contains(_context.GetExecBase() + PortListOffset, port)) return 0;
        _lists.Ensure(port + MsgListOffset); _lists.AddTail(port + MsgListOffset, message);
        var bit = _context.Memory.ReadByte(port + SigBitOffset);
        if (bit < 32) SignalTask(_context.Memory.ReadLong(port + SigTaskOffset), 1u << bit, state);
        return 0;
    }
    public uint GetMsg(M68kCpuState state)
    {
        var message = _lists.RemoveEnd(state.A[0] + MsgListOffset, true); ClearSignalIfEmpty(state.A[0]); return message;
    }
    public uint ReplyMsg(M68kCpuState state)
    {
        var message = state.A[1]; if (!IsMessage(message)) return 0; var replyPort = _context.Memory.ReadLong(message + ReplyPortOffset);
        if (replyPort != 0) { state.A[0] = replyPort; PutMsg(state); } return 0;
    }
    public uint FindPort(M68kCpuState state) => _lists.FindName(_context.GetExecBase() + PortListOffset, state.A[1]);
    public uint WaitPort(M68kCpuState state)
    {
        var port = state.A[0]; var list = port + MsgListOffset;
        if (!IsPort(port)) return 0;
        if (_context.Memory.ReadLong(list) != list + 4) return _context.Memory.ReadLong(list);
        var bit = _context.Memory.ReadByte(port + SigBitOffset);
        if (bit < 32) _context.Memory.WriteLong(_context.GetCurrentTask() + SigRecvdOffset - 4, 1u << bit);
        return 0;
    }
    private void SignalTask(uint task, uint mask, M68kCpuState state)
    {
        const int wait = 0x16, received = 0x1A, ready = 0x196;
        if (task == 0 || !_context.Memory.IsMapped(task + received, 4)) return;
        var pending = _context.Memory.ReadLong(task + received) | mask; _context.Memory.WriteLong(task + received, pending);
        if ((pending & _context.Memory.ReadLong(task + wait)) == 0) return;
        _context.Memory.WriteLong(task + wait, 0); _context.MoveTaskToList(task, _context.GetExecBase() + ready, state); _context.RequestDispatch();
    }
    private void ClearSignalIfEmpty(uint port)
    {
        if (!IsPort(port) || _context.Memory.ReadLong(port + MsgListOffset) != port + MsgListOffset + 4) return;
        var bit = _context.Memory.ReadByte(port + SigBitOffset); if (bit >= 32) return; var task = _context.Memory.ReadLong(port + SigTaskOffset);
        if (task != 0 && _context.Memory.IsMapped(task + SigRecvdOffset, 4)) _context.Memory.WriteLong(task + SigRecvdOffset, _context.Memory.ReadLong(task + SigRecvdOffset) & ~(1u << bit));
    }
    private bool IsPort(uint port) => port != 0 && _context.Memory.IsMapped(port, MsgListOffset + 14);
    private bool IsMessage(uint message) => message != 0 && _context.Memory.IsMapped(message, ReplyPortOffset + 4);
}
