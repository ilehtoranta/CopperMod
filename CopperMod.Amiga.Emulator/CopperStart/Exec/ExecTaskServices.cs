using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Task lifecycle and lookup over the active ExecBase's native lists.</summary>
internal sealed class ExecTaskServices
{
    private const int ReadyOffset = 0x196, WaitOffset = 0x1A4, StackPointerOffset = 0x36, NameOffset = 0x0A;
    private readonly CopperStartExecContext _context; private readonly ExecListServices _lists;
    public ExecTaskServices(CopperStartExecContext context, ExecListServices lists) { _context = context; _lists = lists; }
    public uint AddTask(M68kCpuState state)
    {
        var task = state.A[1]; var initial = state.A[2]; var final = state.A[3];
        if (!_lists.IsValidNode(task) || initial == 0 || !_context.IsFetchable(initial)) return 0;
        var context = new M68kCpuState(); context.CopyFrom(state); context.ProgramCounter = initial; context.A[7] = _context.Memory.ReadLong(task + StackPointerOffset);
        if (context.A[7] >= 4 && final != 0) { context.A[7] -= 4; _context.Memory.WriteLong(context.A[7], final); }
        _context.RegisterTask(task, context); _context.MoveTaskToList(task, _context.GetExecBase() + ReadyOffset, state); _context.RequestDispatch(); return task;
    }
    public uint RemTask(M68kCpuState state) { var task = state.A[1] != 0 ? state.A[1] : _context.GetCurrentTask(); if (_lists.IsValidNode(task)) _lists.Remove(task); _context.RemoveTask(task); return 0; }
    public uint SetTaskPri(M68kCpuState state) { var task = state.A[1]; if (!_lists.IsValidNode(task)) return 0; var old = unchecked((sbyte)_context.Memory.ReadByte(task + 9)); _context.Memory.WriteByte(task + 9, unchecked((byte)(sbyte)state.D[0])); _context.RequestDispatch(); return unchecked((uint)old); }
    public uint FindTask(M68kCpuState state)
    {
        if (state.A[1] == 0) return _context.GetCurrentTask(); var name = _context.ReadString(state.A[1], 96); var current = _context.GetCurrentTask();
        if (NameEquals(current, name)) return current; var exec = _context.GetExecBase(); return FindInList(exec + ReadyOffset, name) is var ready && ready != 0 ? ready : FindInList(exec + WaitOffset, name);
    }
    public uint Disable(M68kCpuState state) => ChangeNesting(0x126, 1); public uint Enable(M68kCpuState state) => ChangeNesting(0x126, -1); public uint Forbid(M68kCpuState state) => ChangeNesting(0x127, 1); public uint Permit(M68kCpuState state) => ChangeNesting(0x127, -1);
    private uint ChangeNesting(int offset, int delta) { var address = _context.GetExecBase() + (uint)offset; var value = _context.Memory.ReadByte(address); if (delta < 0 && value == 0) return 0; _context.Memory.WriteByte(address, unchecked((byte)(value + delta))); return 0; }
    private uint FindInList(uint list, string name) { if (!_lists.IsValidList(list)) return 0; var tail = list + 4; for (var task = _context.Memory.ReadLong(list); task != tail && _lists.IsValidNode(task); task = _context.Memory.ReadLong(task)) if (NameEquals(task, name)) return task; return 0; }
    private bool NameEquals(uint task, string name) => task != 0 && string.Equals(name, _context.ReadString(_context.Memory.ReadLong(task + NameOffset), 96), System.StringComparison.OrdinalIgnoreCase);
}
