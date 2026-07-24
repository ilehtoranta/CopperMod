using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Runtime;

/// <summary>Concrete runtime dependencies for execution-boundary processing.</summary>
internal sealed class RuntimeInstructionBoundaryContext
{
    public RuntimeInstructionBoundaryContext(AmigaBus bus, M68kCpuState cpu, Action dispatchInterrupt,
        Func<long, long, long> nextSyntheticBoundary, Action<long, long> advanceSynthetic,
        Action processHostDevices, Func<long, long, long> nextHostDeviceBoundary)
    { Bus = bus; Cpu = cpu; DispatchInterrupt = dispatchInterrupt; NextSyntheticBoundary = nextSyntheticBoundary; AdvanceSynthetic = advanceSynthetic; ProcessHostDevices = processHostDevices; NextHostDeviceBoundary = nextHostDeviceBoundary; }
    public AmigaBus Bus { get; } public M68kCpuState Cpu { get; } public Action DispatchInterrupt { get; }
    public Func<long, long, long> NextSyntheticBoundary { get; } public Action<long, long> AdvanceSynthetic { get; }
    public Action ProcessHostDevices { get; }
    public Func<long, long, long> NextHostDeviceBoundary { get; }
}

/// <summary>Runtime CPU/JIT boundary: batching, wakeups, hardware advancement, and interrupts.</summary>
internal sealed class RuntimeInstructionBoundary :
    IM68kTraceBatchDiagnosticsBoundary, IM68kStoppedCpuFastForwardBoundary,
    IM68kPureCpuTraceBatchBoundary, IM68kBusAccessTraceBatchBoundary, IM68kDeferredCpuBusBatchBoundary
{
    private readonly RuntimeInstructionBoundaryContext _context;
    private readonly ExecutionBoundarySchedule _schedule;
    public RuntimeInstructionBoundary(RuntimeInstructionBoundaryContext context)
    { _context = context ?? throw new ArgumentNullException(nameof(context)); _schedule = new ExecutionBoundarySchedule(context.NextSyntheticBoundary, context.AdvanceSynthetic, context.NextHostDeviceBoundary); }
    public void Reset(Action<long, long>? beforeDeviceAdvance, IAmigaExecutionBoundarySchedule? boundarySchedule) => _schedule.Reset(beforeDeviceAdvance, boundarySchedule);
    public M68kTraceBatchWakeSource LastTraceBatchWakeSource { get; private set; }
    public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => !_schedule.HasOpaqueLegacyAdvance;
    public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle) => _schedule.GetNextBoundaryCycle(state.Cycles, targetCycle);
    public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount) => AfterInstructionBatch(previousCycle, currentCycle, instructionCount);
    public bool BeforeInstruction() { _context.ProcessHostDevices(); return true; }
    public void AfterInstruction(long previousCycle, long currentCycle) => AfterInstructionBatch(previousCycle, currentCycle, 1);
    public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
    {
        batchTargetCycle = targetCycle; if (targetCycle <= state.Cycles) return false;
        var mask = (state.StatusRegister >> 8) & 7;
        batchTargetCycle = _context.Bus.GetNextCpuBatchWakeCandidateCycle(state.Cycles, targetCycle, mask, out var wake);
        LastTraceBatchWakeSource = wake; batchTargetCycle = ClampDeferredCpuBusBatchTarget(state, batchTargetCycle);
        batchTargetCycle = Math.Clamp(batchTargetCycle, state.Cycles + 1, targetCycle); return batchTargetCycle > state.Cycles;
    }
    public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount) => AfterInstructionBatch(previousCycle, currentCycle, instructionCount);
    public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
    { if (_schedule.HasOpaqueLegacyAdvance) { batchTargetCycle = targetCycle; return false; } return TryBeginPureCpuTraceBatch(state, targetCycle, out batchTargetCycle); }
    public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount) => AfterInstructionBatch(previousCycle, currentCycle, instructionCount);
    private void AfterInstructionBatch(long previousCycle, long currentCycle, int instructionCount)
    { if (instructionCount <= 0) return; _schedule.AdvanceThrough(previousCycle, currentCycle); var mask = (_context.Cpu.StatusRegister >> 8) & 7; _context.Bus.AdvanceHardwareEventsTo(currentCycle, mask); _context.DispatchInterrupt(); }
    public bool TryFastForwardStoppedInstruction(M68kCpuState state, long targetCycle, out long advancedCycles)
    {
        advancedCycles = 0; var previous = state.Cycles; if (targetCycle <= previous) return false;
        targetCycle = _schedule.GetNextBoundaryCycle(previous, targetCycle);
        var mask = (state.StatusRegister >> 8) & 7; var wake = _context.Bus.GetNextStoppedCpuWakeCandidateCycle(previous, targetCycle, mask);
        wake = Math.Clamp(wake, previous + 1, targetCycle); advancedCycles = wake - previous; state.Cycles = wake; AfterInstruction(previous, wake); return true;
    }
}
