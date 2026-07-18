/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Diagnostics;

internal readonly record struct AmigaRasterlineScheduleCacheSnapshot(
    long HitCount,
    long MissCount,
    long RebuildCount,
    long InvalidationCount);

internal sealed class AmigaRasterlineScheduleCache
{
    private const long NoEvent = long.MaxValue;
    private const AmigaHardwareEventMask InterruptPollReadMask =
        AmigaHardwareEventMask.Raster |
        AmigaHardwareEventMask.CiaTimers |
        AmigaHardwareEventMask.PaulaRegister |
        AmigaHardwareEventMask.DiskEvents |
        AmigaHardwareEventMask.Blitter;

    private readonly AmigaBus _bus;

    private bool _valid;
    private long _lineStartCycle;
    private long _lineEndCycle;
    private AmigaHardwareEventMask _computedMask;

    private long _nextRasterCycle;
    private long _nextCiaTimerCycle;
    private long _nextPaulaCycle;
    private long _nextDiskEventCycle;
    private long _nextBlitterCycle;
    private long _nextInterruptPollCycle;
    private bool _interruptPollCycleComputed;

    private long _hitCount;
    private long _missCount;
    private long _rebuildCount;
    private long _invalidationCount;

    public AmigaRasterlineScheduleCache(AmigaBus bus)
    {
        _bus = bus;
        Reset();
    }

    public void Reset()
    {
        _valid = false;
        _lineStartCycle = 0;
        _lineEndCycle = -1;
        _computedMask = AmigaHardwareEventMask.None;
        ResetSourceCycles();
    }

    public void InvalidateFrom(long cycle, AmigaHardwareEventMask mask)
    {
        if (!_valid || mask == AmigaHardwareEventMask.None)
            return;

        if (cycle <= _lineEndCycle)
        {
            var affectedMask = mask & InterruptPollReadMask;
            if (affectedMask == AmigaHardwareEventMask.None)
                return;

            _computedMask &= ~affectedMask;
            _interruptPollCycleComputed = false;
            _invalidationCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySkipDrain(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
    {
        if (mask != InterruptPollReadMask)
            return false;

        var nextCycle = GetNextInterruptPollEventCycle(currentCycle, targetCycle);
        if (nextCycle > targetCycle)
        {
            _hitCount++;
            return true;
        }

        _missCount++;
        return false;
    }

    public AmigaRasterlineScheduleCacheSnapshot CaptureSnapshot()
        => new(_hitCount, _missCount, _rebuildCount, _invalidationCount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetNextInterruptPollEventCycle(long currentCycle, long targetCycle)
    {
        if (targetCycle < currentCycle)
            return NoEvent;

        EnsureLineFor(targetCycle);

        if (!_interruptPollCycleComputed)
        {
            _nextInterruptPollCycle = Math.Min(
                Math.Min(GetRasterCycle(), GetCiaTimerCycle()),
                Math.Min(
                    Math.Min(GetPaulaCycle(), GetDiskEventCycle()),
                    GetBlitterCycle()));
            _interruptPollCycleComputed = true;
        }

        if (_nextInterruptPollCycle == NoEvent || _nextInterruptPollCycle > targetCycle)
            return NoEvent;

        return _nextInterruptPollCycle <= currentCycle ? currentCycle : _nextInterruptPollCycle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureLineFor(long cycle)
    {
        if (_valid && cycle >= _lineStartCycle && cycle <= _lineEndCycle)
            return;

        var lineCycle = GetLineCycle(cycle);
        var lineStartCycle = cycle - lineCycle;
        var lineEndCycle = lineStartCycle + _bus.LineCycles - 1;

        _valid = true;
        _lineStartCycle = lineStartCycle;
        _lineEndCycle = lineEndCycle;
        _computedMask = AmigaHardwareEventMask.None;
        ResetSourceCycles();
        _rebuildCount++;
    }

    private long GetLineCycle(long cycle)
    {
        var lineCycles = _bus.LineCycles;
        var lineCycle = cycle % lineCycles;
        return lineCycle < 0 ? lineCycle + lineCycles : lineCycle;
    }

    private long ProbeCycle => _lineStartCycle > 0 ? _lineStartCycle - 1 : -1;

    private long GetRasterCycle()
    {
        if ((_computedMask & AmigaHardwareEventMask.Raster) == 0)
        {
            _nextRasterCycle = _bus.GetNextRasterEventCycle(ProbeCycle, _lineEndCycle);
            _computedMask |= AmigaHardwareEventMask.Raster;
        }

        return _nextRasterCycle;
    }

    private long GetCiaTimerCycle()
    {
        if ((_computedMask & AmigaHardwareEventMask.CiaTimers) == 0)
        {
            _nextCiaTimerCycle = _bus.GetNextCiaTimerEventCycle(ProbeCycle, _lineEndCycle);
            _computedMask |= AmigaHardwareEventMask.CiaTimers;
        }

        return _nextCiaTimerCycle;
    }

    private long GetPaulaCycle()
    {
        if ((_computedMask & AmigaHardwareEventMask.PaulaRegister) == 0)
        {
            _nextPaulaCycle = _bus.Paula.HasRegisterObservableWorkThrough(_lineStartCycle)
                ? _lineStartCycle
                : (_bus.Paula.GetNextWakeCandidateCycle(ProbeCycle, _lineEndCycle) ?? NoEvent);
            _computedMask |= AmigaHardwareEventMask.PaulaRegister;
        }

        return _nextPaulaCycle;
    }

    private long GetDiskEventCycle()
    {
        if ((_computedMask & AmigaHardwareEventMask.DiskEvents) == 0)
        {
            _nextDiskEventCycle = _bus.Disk.GetNextEventWakeCandidateCycle(
                ProbeCycle,
                _lineEndCycle,
                includeActiveDmaProgress: false) ?? NoEvent;
            _computedMask |= AmigaHardwareEventMask.DiskEvents;
        }

        return _nextDiskEventCycle;
    }

    private long GetBlitterCycle()
    {
        if ((_computedMask & AmigaHardwareEventMask.Blitter) == 0)
        {
            _nextBlitterCycle = _bus.Blitter.GetNextWakeCandidateCycle(ProbeCycle, _lineEndCycle) ?? NoEvent;
            _computedMask |= AmigaHardwareEventMask.Blitter;
        }

        return _nextBlitterCycle;
    }

    private void ResetSourceCycles()
    {
        _nextRasterCycle = NoEvent;
        _nextCiaTimerCycle = NoEvent;
        _nextPaulaCycle = NoEvent;
        _nextDiskEventCycle = NoEvent;
        _nextBlitterCycle = NoEvent;
        _nextInterruptPollCycle = NoEvent;
        _interruptPollCycleComputed = false;
    }
}
