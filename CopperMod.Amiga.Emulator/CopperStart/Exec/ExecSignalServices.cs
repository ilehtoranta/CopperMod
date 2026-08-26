using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>ROM-task signal delivery and the single blocking Wait primitive.</summary>
internal sealed class ExecSignalServices
{
    private const int SigAlloc = 0x12, SigWait = 0x16, SigRecvd = 0x1A, TaskReadyOffset = 0x196, TaskWaitOffset = 0x1A4;
    private readonly CopperStartExecContext _context;
    private readonly Func<bool> _usesRomTasks;
    private readonly Dictionary<uint, PendingWait> _pending = new();
    private Func<uint, bool>? _isIoActive;
    private int _nextCompatibilityBit;
    private uint _compatibilityAllocated, _compatibilityPending, _compatibilityExcept;

    private enum PendingKind { Signals, Port, Io }
    private readonly record struct PendingWait(PendingKind Kind, uint Mask, uint Target);

    public ExecSignalServices(CopperStartExecContext context, Func<bool> usesRomTasks)
    { _context = context; _usesRomTasks = usesRomTasks; }

    public void SetIoActiveProbe(Func<uint, bool> isIoActive) => _isIoActive = isIoActive;
    public void Reset() { _pending.Clear(); ResetCompatibility(); }

    /// <summary>
    /// Shares Wait's task-transition mechanism with blocking Exec services
    /// whose wake condition is not a signal mask (for example semaphores).
    /// Dispatch remains deferred to the outer instruction boundary.
    /// </summary>
    public M68kHostGatewayResult BlockCurrentTask(M68kCpuState state)
    {
        if (!_usesRomTasks())
        {
            return M68kHostGatewayResult.Completed;
        }

        var task = _context.GetCurrentTask();
        Write(task, SigWait, 0);
        _context.MoveTaskToList(task, _context.GetExecBase() + TaskWaitOffset, state);
        if (!_context.SuspendThroughNativeScheduler(state))
        {
            _context.MoveTaskToList(task, _context.GetExecBase() + TaskReadyOffset, state);
            return M68kHostGatewayResult.Completed;
        }

        return M68kHostGatewayResult.BlockCurrentTask;
    }

    public M68kHostGatewayResult Wait(M68kCpuState state)
        => BeginWait(state, state.D[0], PendingKind.Signals, 0);

    public M68kHostGatewayResult WaitForPort(M68kCpuState state, uint port)
    {
        if (!_usesRomTasks()) { state.D[0] = 0; return M68kHostGatewayResult.Completed; }
        if (HasPortMessage(port)) { state.D[0] = _context.Memory.ReadLong(port + 0x14); return M68kHostGatewayResult.Completed; }
        var bit = _context.Memory.ReadByte(port + 0x0F);
        return bit < 32 ? BeginWait(state, 1u << bit, PendingKind.Port, port) : M68kHostGatewayResult.Completed;
    }

    public M68kHostGatewayResult WaitForIo(M68kCpuState state, uint io)
    {
        if (!_usesRomTasks() || _isIoActive is null || !_isIoActive(io))
        { state.D[0] = ReadIoError(io); return M68kHostGatewayResult.Completed; }
        var replyPort = _context.Memory.ReadLong(io + 0x0E);
        var bit = replyPort != 0 && _context.Memory.IsMapped(replyPort + 0x0F, 1) ? (int)_context.Memory.ReadByte(replyPort + 0x0F) : 0xFF;
        return bit < 32 ? BeginWait(state, 1u << bit, PendingKind.Io, io) : M68kHostGatewayResult.Completed;
    }

    /// <summary>Fixed host gateway run only after a previously blocked task is selected.</summary>
    public M68kHostGatewayResult ContinueWait(M68kCpuState state)
    {
        var task = _context.GetCurrentTask();
        if (!_pending.Remove(task, out var pending)) return M68kHostGatewayResult.Completed;
        return pending.Kind switch
        {
            PendingKind.Signals => CompleteSignalWait(state, pending),
            PendingKind.Port => HasPortMessage(pending.Target)
                ? Complete(state, _context.Memory.ReadLong(pending.Target + 0x14))
                : BeginWait(state, pending.Mask, PendingKind.Port, pending.Target),
            PendingKind.Io => _isIoActive?.Invoke(pending.Target) == true
                ? BeginWait(state, pending.Mask, PendingKind.Io, pending.Target)
                : Complete(state, ReadIoError(pending.Target)),
            _ => M68kHostGatewayResult.Completed
        };
    }

    public uint SetSignal(M68kCpuState state)
    {
        if (!_usesRomTasks()) { var compatibilityPrevious = _compatibilityPending; _compatibilityPending = (_compatibilityPending & ~state.D[1]) | (state.D[0] & state.D[1]); return compatibilityPrevious; }
        var task = _context.GetCurrentTask(); var previous = Read(task, SigRecvd); Write(task, SigRecvd, (previous & ~state.D[1]) | (state.D[0] & state.D[1])); return previous;
    }
    public uint SetExcept(M68kCpuState state)
    {
        if (!_usesRomTasks()) { var compatibilityPrevious = _compatibilityExcept; _compatibilityExcept = (_compatibilityExcept & ~state.D[1]) | (state.D[0] & state.D[1]); return compatibilityPrevious; }
        var task = _context.GetCurrentTask(); var previous = Read(task, 0x1E); Write(task, 0x1E, (previous & ~state.D[1]) | (state.D[0] & state.D[1])); return previous;
    }
    public M68kHostGatewayResult Signal(M68kCpuState state)
    {
        if (!_usesRomTasks()) { _compatibilityPending |= state.D[0]; return M68kHostGatewayResult.Completed; }
        var task = state.A[1]; if (task == 0) return M68kHostGatewayResult.Completed;
        var pending = Read(task, SigRecvd) | state.D[0]; Write(task, SigRecvd, pending);
        if ((pending & Read(task, SigWait)) == 0) return M68kHostGatewayResult.Completed;
        Write(task, SigWait, 0); _context.MoveTaskToList(task, _context.GetExecBase() + TaskReadyOffset, state); _context.RequestDispatch();
        return M68kHostGatewayResult.Reschedule;
    }
    public uint AllocSignal(M68kCpuState state)
    {
        if (!_usesRomTasks()) return AllocateCompatibility(unchecked((int)state.D[0]));
        var task = _context.GetCurrentTask(); var allocated = Read(task, SigAlloc); var requested = unchecked((int)state.D[0]);
        for (var bit = requested is >= 0 and < 32 ? requested : 0; bit < 32; bit++) if ((allocated & (1u << bit)) == 0) { Write(task, SigAlloc, allocated | (1u << bit)); return (uint)bit; }
        return 0xFFFF_FFFF;
    }
    public uint FreeSignal(M68kCpuState state)
    {
        if (!_usesRomTasks()) { FreeCompatibility(unchecked((int)state.D[0])); return 0; }
        var bit = unchecked((int)state.D[0]); if (bit is < 0 or >= 32) return 0; var task = _context.GetCurrentTask(); var mask = ~(1u << bit);
        Write(task, SigAlloc, Read(task, SigAlloc) & mask); Write(task, SigRecvd, Read(task, SigRecvd) & mask); return 0;
    }
    public int EnsureCompatibilitySignalBit() => (int)AllocateCompatibility(-1);
    public void SignalCompatibility(uint mask) => _compatibilityPending |= mask;
    public void ClearCompatibility(uint mask) => _compatibilityPending &= ~mask;
    public void ResetCompatibility() { _nextCompatibilityBit = 0; _compatibilityAllocated = _compatibilityPending = _compatibilityExcept = 0; }

    private M68kHostGatewayResult BeginWait(M68kCpuState state, uint mask, PendingKind kind, uint target)
    {
        if (!_usesRomTasks()) { state.D[0] = WaitCompatibility(mask); return M68kHostGatewayResult.Completed; }
        var task = _context.GetCurrentTask(); var delivered = Read(task, SigRecvd) & mask;
        if (delivered != 0 && kind == PendingKind.Signals) return CompleteSignalWait(state, new(PendingKind.Signals, mask, 0));
        _pending[task] = new(kind, mask, target); Write(task, SigWait, mask);
        _context.MoveTaskToList(task, _context.GetExecBase() + TaskWaitOffset, state);
        // Do not manufacture a managed copy of another task's CPU state here.
        // Schedule saves/restores its own tc_SPReg frame, so it also handles
        // ROM tasks that pre-date the overlay or bypass AddTask.
        if (!_context.SuspendThroughNativeScheduler(state))
        {
            // Never leave a task stranded in TaskWait if the native vector is
            // not executable (for example a malformed ROM fixture).
            _pending.Remove(task);
            Write(task, SigWait, 0);
            _context.MoveTaskToList(task, _context.GetExecBase() + TaskReadyOffset, state);
            return M68kHostGatewayResult.Completed;
        }
        return M68kHostGatewayResult.BlockCurrentTask;
    }
    private M68kHostGatewayResult CompleteSignalWait(M68kCpuState state, PendingWait pending)
    {
        var task = _context.GetCurrentTask(); var delivered = Read(task, SigRecvd) & pending.Mask;
        if (delivered == 0) return BeginWait(state, pending.Mask, PendingKind.Signals, 0);
        Write(task, SigWait, 0); Write(task, SigRecvd, Read(task, SigRecvd) & ~delivered); return Complete(state, delivered);
    }
    private static M68kHostGatewayResult Complete(M68kCpuState state, uint value) { state.D[0] = value; return M68kHostGatewayResult.Completed; }
    private bool HasPortMessage(uint port) => port != 0 && _context.Memory.IsMapped(port + 0x18, 4) && _context.Memory.ReadLong(port + 0x14) != port + 0x18;
    private uint ReadIoError(uint io) => io != 0 && _context.Memory.IsMapped(io + 0x1F, 1) ? _context.Memory.ReadByte(io + 0x1F) : 0u;
    private uint WaitCompatibility(uint requested) { requested = requested != 0 ? requested : 1u; var delivered = _compatibilityPending & requested; if (delivered == 0) delivered = requested; _compatibilityPending &= ~delivered; return delivered; }
    private uint AllocateCompatibility(int requested) { if (requested is >= 0 and < 32) { var mask = 1u << requested; if ((_compatibilityAllocated & mask) != 0) return 0xFFFF_FFFF; _compatibilityAllocated |= mask; _nextCompatibilityBit = Math.Max(_nextCompatibilityBit, requested + 1); return (uint)requested; } for (var offset = 0; offset < 32; offset++) { var bit = (_nextCompatibilityBit + offset) & 31; var mask = 1u << bit; if ((_compatibilityAllocated & mask) == 0) { _compatibilityAllocated |= mask; _nextCompatibilityBit = (bit + 1) & 31; return (uint)bit; } } return 0xFFFF_FFFF; }
    private void FreeCompatibility(int bit) { if (bit is < 0 or >= 32) return; var mask = 1u << bit; _compatibilityAllocated &= ~mask; _compatibilityPending &= ~mask; _nextCompatibilityBit = Math.Min(_nextCompatibilityBit, bit); }
    private uint Read(uint task, int offset) => task != 0 && _context.Memory.IsMapped(task + (uint)offset, 4) ? _context.Memory.ReadLong(task + (uint)offset) : 0;
    private void Write(uint task, int offset, uint value) { if (task != 0 && _context.Memory.IsMapped(task + (uint)offset, 4)) _context.Memory.WriteLong(task + (uint)offset, value); }
}
