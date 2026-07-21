using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// Host-owned execution contexts for ROM-owned Exec task structures.  Task
/// selection and list membership deliberately remain the responsibility of the
/// guest-facing Exec service.
/// </summary>
internal sealed class ExecTaskScheduler
{
    private readonly Dictionary<uint, M68kCpuState> _contexts = new();
    private uint _currentTask;
    private int _remainingQuantum;

    public bool DispatchPending { get; private set; }
    public uint CurrentTask => _currentTask;

    public void Reset()
    {
        _contexts.Clear();
        _currentTask = 0;
        _remainingQuantum = 0;
        DispatchPending = false;
    }

    public void CaptureCurrent(uint task, M68kCpuState state)
    {
        if (task == 0) return;
        if (!_contexts.TryGetValue(task, out var saved)) _contexts.Add(task, saved = new M68kCpuState());
        saved.CopyFrom(state);
        _currentTask = task;
    }

    public void Register(uint task, M68kCpuState initialState)
    {
        if (task == 0) throw new ArgumentOutOfRangeException(nameof(task));
        if (!_contexts.TryGetValue(task, out var saved)) _contexts.Add(task, saved = new M68kCpuState());
        saved.CopyFrom(initialState);
        DispatchPending = true;
    }

    public void Remove(uint task)
    {
        _contexts.Remove(task);
        if (_currentTask == task) { _currentTask = 0; DispatchPending = true; }
    }

    public void OnVBlank(int quantum)
    {
        if (_currentTask == 0) { DispatchPending = true; return; }
        if (--_remainingQuantum <= 0) DispatchPending = true;
        if (quantum <= 0) quantum = 1;
    }

    public bool TryRestore(uint task, M68kCpuState state, int quantum)
    {
        if (!_contexts.TryGetValue(task, out var saved)) return false;
        state.CopyFrom(saved);
        _currentTask = task;
        _remainingQuantum = Math.Max(1, quantum);
        DispatchPending = false;
        return true;
    }

    public void RequestDispatch() => DispatchPending = true;
}
