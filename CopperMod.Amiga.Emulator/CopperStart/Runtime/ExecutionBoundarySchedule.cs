using System;

namespace CopperMod.Amiga.CopperStart.Runtime;

/// <summary>
/// Composes the caller's device schedule with CopperStart's synthetic vblank
/// schedule.  It deliberately preserves the original boundary ordering:
/// external device advancement first, synthetic interrupt dispatch second.
/// </summary>
internal sealed class ExecutionBoundarySchedule : IAmigaExecutionBoundarySchedule
{
    private readonly Func<long, long, long> _nextSyntheticBoundary;
    private readonly Action<long, long> _advanceSynthetic;
    private Action<long, long>? _legacyAdvance;
    private IAmigaExecutionBoundarySchedule? _scheduledAdvance;

    public ExecutionBoundarySchedule(
        Func<long, long, long> nextSyntheticBoundary,
        Action<long, long> advanceSynthetic)
    {
        _nextSyntheticBoundary = nextSyntheticBoundary ?? throw new ArgumentNullException(nameof(nextSyntheticBoundary));
        _advanceSynthetic = advanceSynthetic ?? throw new ArgumentNullException(nameof(advanceSynthetic));
    }

    public bool HasOpaqueLegacyAdvance => _legacyAdvance != null && _scheduledAdvance == null;

    public void Reset(Action<long, long>? legacyAdvance, IAmigaExecutionBoundarySchedule? scheduledAdvance)
    {
        _legacyAdvance = legacyAdvance;
        _scheduledAdvance = scheduledAdvance;
    }

    public void BeginFrame() => _scheduledAdvance?.BeginFrame();
    public void BeginExecution(long startCycle, long endCycle) => _scheduledAdvance?.BeginExecution(startCycle, endCycle);

    public long GetNextBoundaryCycle(long currentCycle, long targetCycle)
    {
        var scheduledCycle = _scheduledAdvance?.GetNextBoundaryCycle(currentCycle, targetCycle) ?? targetCycle;
        return _nextSyntheticBoundary(currentCycle, Math.Min(targetCycle, scheduledCycle));
    }

    public void AdvanceThrough(long previousCycle, long currentCycle)
    {
        if (_scheduledAdvance != null) _scheduledAdvance.AdvanceThrough(previousCycle, currentCycle);
        else _legacyAdvance?.Invoke(previousCycle, currentCycle);
        _advanceSynthetic(previousCycle, currentCycle);
    }

    public void CompleteExecution(long endCycle) => _scheduledAdvance?.CompleteExecution(endCycle);
    public void CompleteFrame() => _scheduledAdvance?.CompleteFrame();
}
