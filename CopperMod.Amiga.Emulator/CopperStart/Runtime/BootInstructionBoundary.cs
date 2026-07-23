using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Runtime;

/// <summary>Concrete boot-lifecycle callbacks used at instruction boundaries.</summary>
internal sealed class BootInstructionBoundaryContext
{
    public BootInstructionBoundaryContext(AmigaBus bus, M68kCpuState cpu, uint dosReturnAddress,
        Func<bool> romBoot, Action activateRomExec, Func<bool> dispatchTasks, Func<bool> dispatchInterrupts,
        Action ensureTrapVectors, Action ensureLowMemory, Action ensureAutovectors,
        Func<bool> recoverTrap, Func<bool> startDosContinuation, Action recordNullPc,
        Func<bool> continueStartup, Action skipBootHeader, Action applyLanguage,
        Func<bool> bootReadComplete, Action dispatchHardwareInterrupt,
        Func<long, long, long> nextSyntheticBoundary, Action<long, long> advanceSynthetic)
    { Bus = bus; Cpu = cpu; DosReturnAddress = dosReturnAddress; RomBoot = romBoot; ActivateRomExec = activateRomExec; DispatchTasks = dispatchTasks; DispatchInterrupts = dispatchInterrupts; EnsureTrapVectors = ensureTrapVectors; EnsureLowMemory = ensureLowMemory; EnsureAutovectors = ensureAutovectors; RecoverTrap = recoverTrap; StartDosContinuation = startDosContinuation; RecordNullPc = recordNullPc; ContinueStartup = continueStartup; SkipBootHeader = skipBootHeader; ApplyLanguage = applyLanguage; BootReadComplete = bootReadComplete; DispatchHardwareInterrupt = dispatchHardwareInterrupt; NextSyntheticBoundary = nextSyntheticBoundary; AdvanceSynthetic = advanceSynthetic; }
    public AmigaBus Bus { get; } public M68kCpuState Cpu { get; } public uint DosReturnAddress { get; }
    public Func<bool> RomBoot { get; } public Action ActivateRomExec { get; } public Func<bool> DispatchTasks { get; } public Func<bool> DispatchInterrupts { get; }
    public Action EnsureTrapVectors { get; } public Action EnsureLowMemory { get; } public Action EnsureAutovectors { get; }
    public Func<bool> RecoverTrap { get; } public Func<bool> StartDosContinuation { get; } public Action RecordNullPc { get; }
    public Func<bool> ContinueStartup { get; } public Action SkipBootHeader { get; } public Action ApplyLanguage { get; } public Func<bool> BootReadComplete { get; }
    public Action DispatchHardwareInterrupt { get; } public Func<long, long, long> NextSyntheticBoundary { get; } public Action<long, long> AdvanceSynthetic { get; }
}

/// <summary>Boot CPU/JIT boundary and CopperStart boot-continuation policy.</summary>
internal sealed class BootInstructionBoundary :
    IM68kTraceBatchDiagnosticsBoundary, IM68kStoppedCpuFastForwardBoundary,
    IM68kPureCpuTraceBatchBoundary, IM68kBusAccessTraceBatchBoundary, IM68kDeferredCpuBusBatchBoundary
{
    private readonly BootInstructionBoundaryContext _context; private readonly ExecutionBoundarySchedule _schedule;
    private AmigaBootRunMode _runMode; private int _instructions;
    public BootInstructionBoundary(BootInstructionBoundaryContext context) { _context = context ?? throw new ArgumentNullException(nameof(context)); _schedule = new ExecutionBoundarySchedule(context.NextSyntheticBoundary, context.AdvanceSynthetic); }
    public bool Completed { get; private set; } public M68kTraceBatchWakeSource LastTraceBatchWakeSource { get; private set; }
    public void Reset(AmigaBootRunMode runMode, Action<long, long>? beforeDeviceAdvance, IAmigaExecutionBoundarySchedule? schedule) { _runMode = runMode; _schedule.Reset(beforeDeviceAdvance, schedule); _instructions = 0; Completed = false; }
    public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => _runMode != AmigaBootRunMode.StopAfterBootDiskRead && !_schedule.HasOpaqueLegacyAdvance && BeforeInstruction();
    public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle) => _schedule.GetNextBoundaryCycle(state.Cycles, targetCycle);
    public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount) => AfterBatch(previousCycle, currentCycle, instructionCount);
    public bool BeforeInstruction()
    {
        if (Completed) return false;
        if (_context.RomBoot()) { _context.ActivateRomExec(); if (_context.DispatchTasks() || _context.DispatchInterrupts()) return true; }
        else { _context.EnsureTrapVectors(); _context.EnsureLowMemory(); _context.EnsureAutovectors(); }
        if (_context.Cpu.ProgramCounter == 0 && _instructions > 0)
        { if (!_context.RomBoot() && (_context.RecoverTrap() || _context.StartDosContinuation())) return true; if (_context.RomBoot()) _context.RecordNullPc(); Completed = true; return false; }
        if (_context.Cpu.ProgramCounter == _context.DosReturnAddress) return HandleDosReturn();
        _context.SkipBootHeader(); _context.ApplyLanguage(); return _context.Cpu.ProgramCounter != _context.DosReturnAddress || HandleDosReturn();
    }
    private bool HandleDosReturn() { if (_context.ContinueStartup()) return true; Completed = true; return false; }
    public void AfterInstruction(long previousCycle, long currentCycle) => AfterBatch(previousCycle, currentCycle, 1);
    public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
    { batchTargetCycle = targetCycle; if (_runMode == AmigaBootRunMode.StopAfterBootDiskRead || targetCycle <= state.Cycles || !BeforeInstruction()) return false; var mask = (state.StatusRegister >> 8) & 7; batchTargetCycle = _context.Bus.GetNextCpuBatchWakeCandidateCycle(state.Cycles, targetCycle, mask, out var wake); LastTraceBatchWakeSource = wake; batchTargetCycle = ClampDeferredCpuBusBatchTarget(state, batchTargetCycle); batchTargetCycle = Math.Clamp(batchTargetCycle, state.Cycles + 1, targetCycle); return batchTargetCycle > state.Cycles; }
    public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount) => AfterBatch(previousCycle, currentCycle, instructionCount);
    public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle) { if (_schedule.HasOpaqueLegacyAdvance) { batchTargetCycle = targetCycle; return false; } return TryBeginPureCpuTraceBatch(state, targetCycle, out batchTargetCycle); }
    public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount) => AfterBatch(previousCycle, currentCycle, instructionCount);
    private void AfterBatch(long previousCycle, long currentCycle, int count) { if (count <= 0) return; _schedule.AdvanceThrough(previousCycle, currentCycle); var mask = (_context.Cpu.StatusRegister >> 8) & 7; _context.Bus.AdvanceHardwareEventsTo(currentCycle, mask); _context.DispatchHardwareInterrupt(); _instructions += count; if (_context.BootReadComplete() && _runMode == AmigaBootRunMode.StopAfterBootDiskRead) Completed = true; }
    public bool TryFastForwardStoppedInstruction(M68kCpuState state, long targetCycle, out long advancedCycles) { advancedCycles = 0; if (!BeforeInstruction() || targetCycle <= state.Cycles) return false; var previous = state.Cycles; var mask = (state.StatusRegister >> 8) & 7; var wake = _context.Bus.GetNextStoppedCpuWakeCandidateCycle(previous, targetCycle, mask); wake = Math.Clamp(wake, previous + 1, targetCycle); advancedCycles = wake - previous; state.Cycles = wake; AfterInstruction(previous, wake); return true; }
}
