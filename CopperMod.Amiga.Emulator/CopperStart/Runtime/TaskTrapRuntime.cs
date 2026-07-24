using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Runtime;

/// <summary>
/// Owns the synthetic task-trap vector gateways.  Frame decoding and recovery
/// remain in the boot coordinator for now, because they are coupled to the
/// boot diagnostics and DOS continuation policy.
/// </summary>
internal sealed class TaskTrapRuntime
{
    private static readonly int[] Vectors = { 2, 3, 4, 8, 10, 11 };
    private readonly AmigaBus _bus;
    private readonly M68kCpuState _cpuState;
    private readonly Func<bool> _hasSyntheticExec;
    private readonly Func<uint> _currentTask;
    private readonly Func<uint> _execBase;
    private readonly Func<int, uint> _dispatcherAddress;
    private readonly Action<M68kCpuState> _defaultTrapCode;
    private readonly uint _defaultTrapCodeAddress;
    private readonly int _taskTrapCodeOffset;
    private readonly int _execTaskTrapCodeOffset;

    public TaskTrapRuntime(
        AmigaBus bus,
        M68kCpuState cpuState,
        Func<bool> hasSyntheticExec,
        Func<uint> currentTask,
        Func<uint> execBase,
        Func<int, uint> dispatcherAddress,
        Action<M68kCpuState> defaultTrapCode,
        uint defaultTrapCodeAddress,
        int taskTrapCodeOffset,
        int execTaskTrapCodeOffset)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _cpuState = cpuState ?? throw new ArgumentNullException(nameof(cpuState));
        _hasSyntheticExec = hasSyntheticExec ?? throw new ArgumentNullException(nameof(hasSyntheticExec));
        _currentTask = currentTask ?? throw new ArgumentNullException(nameof(currentTask));
        _execBase = execBase ?? throw new ArgumentNullException(nameof(execBase));
        _dispatcherAddress = dispatcherAddress ?? throw new ArgumentNullException(nameof(dispatcherAddress));
        _defaultTrapCode = defaultTrapCode ?? throw new ArgumentNullException(nameof(defaultTrapCode));
        _defaultTrapCodeAddress = defaultTrapCodeAddress;
        _taskTrapCodeOffset = taskTrapCodeOffset;
        _execTaskTrapCodeOffset = execTaskTrapCodeOffset;
    }

    public void Install()
    {
        _bus.RegisterHostGateway(_defaultTrapCodeAddress, _defaultTrapCode);
        for (var index = 0; index < Vectors.Length; index++)
        {
            var vector = Vectors[index];
            var captured = vector;
            _bus.RegisterHostGateway(_dispatcherAddress(captured), state => Dispatch(state, captured));
        }

        for (var trap = 0; trap < 16; trap++)
        {
            var vector = 32 + trap;
            var captured = vector;
            _bus.RegisterHostGateway(_dispatcherAddress(captured), state => Dispatch(state, captured));
        }

        RefreshVectors();
    }

    public void EnsureVectorsCurrent()
    {
        if (!_hasSyntheticExec() || !VectorsNeedRefresh()) return;
        RefreshVectors();
    }

    private bool VectorsNeedRefresh()
    {
        if (_cpuState.VectorBaseRegister != 0) return true;
        // Match the original coordinator policy: these probe vectors are ours
        // to refresh, while a guest may intentionally replace another TRAP #n
        // vector after boot (for example TRAP #4 in a boot block).
        for (var index = 0; index < Vectors.Length; index++)
        {
            var vector = Vectors[index];
            if (_bus.ReadLong((uint)(vector * 4)) != _dispatcherAddress(vector)) return true;
        }

        const int trapZeroVector = 32;
        if (_bus.ReadLong(trapZeroVector * 4) != _dispatcherAddress(trapZeroVector)) return true;
        return false;
    }

    private void RefreshVectors()
    {
        _cpuState.VectorBaseRegister = 0;
        for (var index = 0; index < Vectors.Length; index++)
        {
            var vector = Vectors[index];
            _bus.WriteLong((uint)(vector * 4), _dispatcherAddress(vector));
        }

        for (var trap = 0; trap < 16; trap++)
        {
            var vector = 32 + trap;
            _bus.WriteLong((uint)(vector * 4), _dispatcherAddress(vector));
        }
    }

    private void Dispatch(M68kCpuState state, int vector)
    {
        var task = _currentTask();
        var trapCode = task != 0 ? _bus.ReadLong(task + (uint)_taskTrapCodeOffset) : 0;
        if (trapCode == 0) trapCode = _bus.ReadLong(_execBase() + (uint)_execTaskTrapCodeOffset);
        if (trapCode == 0) trapCode = _defaultTrapCodeAddress;
        state.SetActiveStackPointer(state.A[7] - 4);
        _bus.WriteLong(state.A[7], (uint)vector);
        state.ProgramCounter = trapCode;
    }
}
