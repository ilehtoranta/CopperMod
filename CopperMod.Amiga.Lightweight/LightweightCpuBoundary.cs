using Copper68k;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Minimal Copper68k boundary for the single-clock A500 engine.
/// </summary>
/// <remarks>
/// Batches are bounded by the next device-visible edge. Bus transfers still
/// enter <see cref="IM68kBus"/> individually; this class does not speculate,
/// replay accesses, or maintain a second device timeline.
/// </remarks>
internal sealed class LightweightCpuBoundary :
    IM68kInstructionBoundary,
    IM68kStoppedCpuFastForwardBoundary,
    IM68kConservativeBusAccessBatchBoundary
{
    private readonly LightweightA500Machine _machine;
    private int _scalarInstructionBudget;
    private int _scalarInstructions;

    internal LightweightCpuBoundary(LightweightA500Machine machine)
    {
        _machine = machine;
    }

    internal void BeginBatchProbe(int scalarInstructionBudget)
    {
        _scalarInstructionBudget = scalarInstructionBudget;
        _scalarInstructions = 0;
        _machine.AdvanceHardwareToCpuCycle();
    }

    public bool BeforeInstruction()
    {
        _machine.AdvanceHardwareToCpuCycle();
        return !_machine.DispatchPendingCpuInterrupt() &&
            _scalarInstructions < _scalarInstructionBudget;
    }

    public void AfterInstruction(long previousCycle, long currentCycle)
    {
        _ = previousCycle;
        _scalarInstructions++;
        _machine.AdvanceHardwareTo(currentCycle);
    }

    public bool TryBeginBusAccessTraceBatch(
        M68kCpuState state,
        long targetCycle,
        out long batchTargetCycle)
    {
        _machine.AdvanceHardwareTo(state.Cycles);
        batchTargetCycle = _machine.ClampCpuBatchTarget(targetCycle);
        return batchTargetCycle > state.Cycles;
    }

    public void AfterBusAccessTraceBatch(
        long previousCycle,
        long currentCycle,
        int instructionCount)
    {
        _ = previousCycle;
        if (instructionCount > 0)
        {
            _machine.AdvanceHardwareTo(currentCycle);
        }
    }

    public bool TryFastForwardStoppedInstruction(
        M68kCpuState state,
        long targetCycle,
        out long advancedCycles)
    {
        var previousCycle = state.Cycles;
        var wakeCycle = _machine.ClampCpuBatchTarget(targetCycle);
        if (wakeCycle <= previousCycle)
        {
            advancedCycles = 0;
            return false;
        }

        _machine.AdvanceHardwareTo(wakeCycle);
        state.Cycles = wakeCycle;
        advancedCycles = wakeCycle - previousCycle;
        return true;
    }
}
