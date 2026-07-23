using Copper68k;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Task lifecycle and lookup over the active ExecBase's native lists.</summary>
internal sealed class ExecTaskServices
{
    private const int ReadyOffset = 0x196, WaitOffset = 0x1A4, StackPointerOffset = 0x36, MemEntryOffset = 0x4A, NameOffset = 0x0A;
    private const int TaskSizeThroughMemEntry = MemEntryOffset + 14;
    private readonly CopperStartExecContext _context; private readonly ExecListServices _lists;
    private readonly Queue<uint> _deferredReap = new();
    public ExecTaskServices(CopperStartExecContext context, ExecListServices lists) { _context = context; _lists = lists; }
    public void Reset() => _deferredReap.Clear();
    public uint AddTask(M68kCpuState state)
    {
        var task = state.A[1]; var initial = state.A[2]; var final = state.A[3];
        if (!_lists.IsValidNode(task) || !_context.Memory.IsMapped(task, TaskSizeThroughMemEntry) ||
            initial == 0 || !_context.IsFetchable(initial)) return 0;

        // KS owns the saved-register frame when it first schedules the task.
        // AddTask's only caller-visible stack contract is the outer final-PC
        // return address, placed below tc_SPReg.  Do not copy the caller's CPU
        // state into a new task: every new register starts clear.
        var stack = _context.Memory.ReadLong(task + StackPointerOffset);
        if (stack < 4 || !_context.Memory.IsMapped(stack - 4, 4)) return 0;
        if (final != 0)
        {
            if (!_context.IsFetchable(final)) return 0;
            stack -= 4;
            _context.Memory.WriteLong(stack, final);
            _context.Memory.WriteLong(task + StackPointerOffset, stack);
        }

        _lists.Ensure(task + MemEntryOffset);
        var context = new M68kCpuState();
        context.ProgramCounter = initial;
        context.A[7] = stack;
        _context.RegisterTask(task, context);
        MoveToReady(task, state);
        _context.RequestDispatch();
        return task;
    }
    public M68kHostGatewayResult RemTask(M68kCpuState state)
    {
        var task = state.A[1] != 0 ? state.A[1] : _context.GetCurrentTask();
        if (!_lists.IsValidNode(task)) return M68kHostGatewayResult.Completed;
        var isCurrent = task == _context.GetCurrentTask();
        _lists.Remove(task);
        _context.RemoveTask(task);
        _context.RequestDispatch();
        if (!isCurrent)
        {
            ReapTaskMemory(task);
            return M68kHostGatewayResult.Completed;
        }

        // RemTask(NULL) is terminal for its caller. Preserve its active frame
        // only long enough for the untouched KS Switch vector to select the
        // next ready task; never let the host gateway's ordinary RTS resume a
        // task which has just been unlinked.
        if (!_context.SuspendThroughNativeScheduler(state)) return M68kHostGatewayResult.Completed;
        _deferredReap.Enqueue(task);
        return M68kHostGatewayResult.BlockCurrentTask;
    }
    public uint SetTaskPri(M68kCpuState state)
    {
        var task = state.A[1];
        if (!_lists.IsValidNode(task)) return 0;
        var old = unchecked((sbyte)_context.Memory.ReadByte(task + 9));
        _context.Memory.WriteByte(task + 9, unchecked((byte)(sbyte)state.D[0]));
        if (_context.Memory.ReadByte(task + 0x0F) == 3)
        {
            _lists.Remove(task);
            MoveToReady(task, state);
        }
        _context.RequestDispatch();
        return unchecked((uint)old);
    }
    public M68kHostGatewayResult Reschedule(M68kCpuState state)
    {
        _context.RequestDispatch();
        return M68kHostGatewayResult.Reschedule;
    }
    public uint FindTask(M68kCpuState state)
    {
        if (state.A[1] == 0) return _context.GetCurrentTask(); var name = _context.ReadString(state.A[1], 96); var current = _context.GetCurrentTask();
        if (!_context.UsesRomExec())
            return name.Equals("CopperScreen", System.StringComparison.OrdinalIgnoreCase) ||
                (name.Equals("Workbench", System.StringComparison.OrdinalIgnoreCase) && _context.HasCompatibilityWorkbench())
                ? current : 0;
        if (NameEquals(current, name)) return current; var exec = _context.GetExecBase(); return FindInList(exec + ReadyOffset, name) is var ready && ready != 0 ? ready : FindInList(exec + WaitOffset, name);
    }
    public uint Disable(M68kCpuState state) => ChangeNesting(0x126, 1, requestDispatchWhenEnabled: false);
    public uint Enable(M68kCpuState state) => ChangeNesting(0x126, -1, requestDispatchWhenEnabled: false);
    public uint Forbid(M68kCpuState state) => ChangeNesting(0x127, 1, requestDispatchWhenEnabled: false);
    public uint Permit(M68kCpuState state) => ChangeNesting(0x127, -1, requestDispatchWhenEnabled: true);

    /// <summary>Called only from an outer instruction boundary after KS has switched away.</summary>
    public void ReapDeferredTasks()
    {
        while (_deferredReap.Count != 0) ReapTaskMemory(_deferredReap.Dequeue());
    }

    private uint ChangeNesting(int offset, int delta, bool requestDispatchWhenEnabled)
    {
        var address = _context.GetExecBase() + (uint)offset;
        var value = _context.Memory.ReadByte(address);
        if (delta < 0 && value == 0) return 0;
        var next = unchecked((byte)(value + delta));
        _context.Memory.WriteByte(address, next);
        if (requestDispatchWhenEnabled && next == 0) _context.RequestDispatch();
        return 0;
    }

    private void MoveToReady(uint task, M68kCpuState state)
    {
        if (_context.Memory.ReadLong(task + 4) != 0) _lists.Remove(task);
        _lists.Enqueue(_context.GetExecBase() + ReadyOffset, task);
        _context.Memory.WriteByte(task + 0x0F, 3);
    }
    private uint FindInList(uint list, string name) { if (!_lists.IsValidList(list)) return 0; var tail = list + 4; for (var task = _context.Memory.ReadLong(list); task != tail && _lists.IsValidNode(task); task = _context.Memory.ReadLong(task)) if (NameEquals(task, name)) return task; return 0; }
    private bool NameEquals(uint task, string name) => task != 0 && string.Equals(name, _context.ReadString(_context.Memory.ReadLong(task + NameOffset), 96), System.StringComparison.OrdinalIgnoreCase);

    private void ReapTaskMemory(uint task)
    {
        var list = task + MemEntryOffset;
        if (!_lists.IsValidList(list)) return;
        var tail = list + 4;
        for (var entry = _context.Memory.ReadLong(list); entry != tail && _lists.IsValidNode(entry);)
        {
            var next = _context.Memory.ReadLong(entry);
            FreeMemList(entry);
            entry = next;
        }
    }

    private void FreeMemList(uint list)
    {
        if (!_context.Memory.IsMapped(list, 16)) return;
        var count = _context.Memory.ReadWord(list + 14);
        var bytes = 16 + count * 8;
        if (!_context.Memory.IsMapped(list, bytes)) return;
        for (var index = 0; index < count; index++)
        {
            var entry = list + 16u + (uint)(index * 8);
            var address = _context.Memory.ReadLong(entry);
            var length = _context.Memory.ReadLong(entry + 4);
            if (address != 0 && length is > 0 and <= int.MaxValue)
                _context.MemoryOperations.Free(address, (int)length);
        }
        _context.MemoryOperations.Free(list, bytes);
    }
}
