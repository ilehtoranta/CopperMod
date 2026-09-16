using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Direct OCS low/high-resolution DDF and bitplane DMA path. One accepted address
/// is retained until the following CCK samples Chip RAM.
/// </summary>
internal sealed class LightweightBitplanes
{
    private const ushort DmaconMaster = 0x0200;
    private const ushort DmaconBitplane = 0x0100;
    private const int DdfMask = 0x00FC;
    private const int DdfLimitReleaseHorizontal = 0x18;
    private const int DdfHardStopCompareHorizontal = 0xD7;
    private const int DdfHardStopTargetHorizontal = 0xD8;
    private const int FetchUnitColorClocks = 8;
    private const uint OcsChipAddressMask = 0x0007_FFFEu;
    private static ReadOnlySpan<sbyte> LowResPlaneBySlot =>
        [-1, 3, 5, 1, -1, 2, 4, 0];
    private static ReadOnlySpan<sbyte> HighResPlaneBySlot => [3, 1, 2, 0, 3, 1, 2, 0];

    private readonly uint[] _pointers = new uint[6];
    private readonly ushort[] _dataLatches = new ushort[6];
    private ushort _effectiveBplcon0;
    private int _effectivePlaneCount;
    private ushort _pendingBplcon0;
    private long _pendingBplcon0Cycle;
    private bool _startPermit;
    private bool _runActive;
    private long _sequenceOriginCycle;
    private long _terminalUnitStartCycle;
    private long _completionCycle;
    private int _ownerLine;
    private bool _hasPendingOutput;
    private int _pendingPlane;
    private uint _pendingAddress;
    private bool _pendingModulo;
    private long _pendingOutputCycle;

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long PendingOutputCycle => _hasPendingOutput ? _pendingOutputCycle : long.MaxValue;
    internal long LastInputCycle { get; private set; } = long.MinValue;
    internal long LastOutputCycle { get; private set; } = long.MinValue;
    internal int LastPlane { get; private set; } = -1;
    internal uint LastAddress { get; private set; }
    internal ushort EffectiveBplcon0 => _effectiveBplcon0;
    internal bool RunActive => _runActive;
    internal int OwnerLine => _ownerLine;

    internal void Reset()
    {
        Array.Clear(_pointers);
        Array.Clear(_dataLatches);
        _effectiveBplcon0 = 0;
        _effectivePlaneCount = 0;
        _pendingBplcon0 = 0;
        _pendingBplcon0Cycle = long.MaxValue;
        _startPermit = false;
        _runActive = false;
        _sequenceOriginCycle = long.MaxValue;
        _terminalUnitStartCycle = long.MaxValue;
        _completionCycle = long.MaxValue;
        _ownerLine = -1;
        _hasPendingOutput = false;
        _pendingPlane = -1;
        _pendingAddress = 0;
        _pendingModulo = false;
        _pendingOutputCycle = long.MaxValue;
        NextCycle = long.MaxValue;
        LastInputCycle = long.MinValue;
        LastOutputCycle = long.MinValue;
        LastPlane = -1;
        LastAddress = 0;
    }

    internal void OnRegisterWrite(
        ushort offset,
        ushort value,
        long cycle,
        LightweightA500Machine machine)
    {
        if (offset == LightweightRegisters.Bplcon0)
        {
            _pendingBplcon0 = value;
            _pendingBplcon0Cycle = cycle + LightweightClock.CpuCyclesPerColorClock;
            Publish(_pendingBplcon0Cycle);
        }
        else if (offset >= LightweightRegisters.BplPointerFirst &&
            offset <= LightweightRegisters.BplPointerLast)
        {
            var plane = (offset - LightweightRegisters.BplPointerFirst) >> 2;
            if ((uint)plane < (uint)_pointers.Length)
            {
                _pointers[plane] = machine.GetBitplanePointer(plane);
            }
        }

        if (ShouldClock(machine))
        {
            Publish(NextCckAfter(cycle));
        }
    }

    internal void OnFrameStart(long cycle, LightweightA500Machine machine)
    {
        _startPermit = false;
        _runActive = false;
        _sequenceOriginCycle = long.MaxValue;
        _terminalUnitStartCycle = long.MaxValue;
        _completionCycle = long.MaxValue;
        _ownerLine = -1;
        if (ShouldClock(machine))
        {
            Publish(NextCckAfter(cycle));
        }
    }

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Bitplanes must advance at their published CCK.");

        if (_hasPendingOutput && _pendingOutputCycle == cycle)
        {
            CompleteOutput(cycle, machine);
        }

        if (_pendingBplcon0Cycle <= cycle)
        {
            _effectiveBplcon0 = _pendingBplcon0;
            _effectivePlaneCount = GetSupportedPlaneCount(_effectiveBplcon0);
            _pendingBplcon0Cycle = long.MaxValue;
        }

        var dmaEnabled = IsDmaEnabled(machine.Dmacon);
        var planeCount = _effectivePlaneCount;
        AdvanceDdfControl(cycle, machine, dmaEnabled, planeCount);
        TryAcceptInput(cycle, machine, planeCount);

        NextCycle = long.MaxValue;
        if (_pendingBplcon0Cycle != long.MaxValue)
        {
            Publish(_pendingBplcon0Cycle);
        }
        if (_hasPendingOutput)
        {
            Publish(_pendingOutputCycle);
        }
        if (_runActive || _hasPendingOutput)
        {
            Publish(cycle + LightweightClock.CpuCyclesPerColorClock);
        }
        else if (dmaEnabled && planeCount != 0)
        {
            PublishNextInactiveControlCycle(cycle, machine);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool OwnsOutputSlot(long cycle)
        => _hasPendingOutput && _pendingOutputCycle == cycle || LastOutputCycle == cycle;

    internal uint GetPointer(int plane)
        => (uint)plane < (uint)_pointers.Length ? _pointers[plane] : 0;

    internal ushort GetDataLatch(int plane)
        => (uint)plane < (uint)_dataLatches.Length ? _dataLatches[plane] : (ushort)0;

    private void AdvanceDdfControl(
        long cycle,
        LightweightA500Machine machine,
        bool dmaEnabled,
        int planeCount)
    {
        var horizontal = machine.BeamColorClock;
        if (_runActive && (!dmaEnabled || planeCount == 0))
        {
            _runActive = false;
            _sequenceOriginCycle = long.MaxValue;
            _terminalUnitStartCycle = long.MaxValue;
            _completionCycle = long.MaxValue;
            _ownerLine = -1;
        }
        if (!_startPermit && horizontal == DdfLimitReleaseHorizontal)
        {
            _startPermit = true;
        }

        if (_runActive && cycle == _completionCycle)
        {
            _runActive = false;
            _startPermit = false;
            _sequenceOriginCycle = long.MaxValue;
            _terminalUnitStartCycle = long.MaxValue;
            _completionCycle = long.MaxValue;
            _ownerLine = -1;
        }

        var ddfStop = machine.DdfStop & DdfMask;
        if (_runActive && _completionCycle == long.MaxValue &&
            horizontal == ddfStop)
        {
            ArmStop(cycle, cycle);
        }
        if (_runActive && _completionCycle == long.MaxValue &&
            horizontal == DdfHardStopCompareHorizontal)
        {
            var lineStart = cycle -
                ((long)horizontal * LightweightClock.CpuCyclesPerColorClock);
            ArmStop(
                cycle,
                lineStart + ((long)DdfHardStopTargetHorizontal *
                    LightweightClock.CpuCyclesPerColorClock));
        }

        if (!_runActive && _startPermit && dmaEnabled && planeCount != 0 &&
            horizontal == (machine.GetCustomRegister(
                LightweightRegisters.Ddfstrt) & DdfMask) &&
            IsVerticalWindowOpen(machine))
        {
            _runActive = true;
            _sequenceOriginCycle = cycle;
            _terminalUnitStartCycle = long.MaxValue;
            _completionCycle = long.MaxValue;
            _ownerLine = machine.BeamLine;
        }
    }

    private void TryAcceptInput(
        long cycle,
        LightweightA500Machine machine,
        int planeCount)
    {
        if (!_runActive || cycle < _sequenceOriginCycle ||
            _completionCycle != long.MaxValue && cycle >= _completionCycle)
        {
            return;
        }

        // The guard above proves this delta is non-negative. One CCK is two
        // CPU cycles, so an arithmetic shift is the exact phase conversion
        // without the signed-division correction sequence.
        var deltaCcks = (cycle - _sequenceOriginCycle) >> 1;
        var slot = (int)(deltaCcks & (FetchUnitColorClocks - 1));
        var hires = (_effectiveBplcon0 & 0x8000) != 0;
        var plane = hires ? HighResPlaneBySlot[slot] : LowResPlaneBySlot[slot];
        if ((uint)plane >= (uint)planeCount)
        {
            return;
        }

        var outputCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
        if (!machine.CanBitplaneOwnOutputSlot(outputCycle))
        {
            return;
        }

        _hasPendingOutput = true;
        _pendingPlane = plane;
        _pendingAddress = _pointers[plane];
        _pendingModulo = _terminalUnitStartCycle != long.MaxValue &&
            cycle >= _terminalUnitStartCycle + (hires ? 8 : 0);
        _pendingOutputCycle = outputCycle;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastInputCycle = cycle;
#endif
    }

    private void CompleteOutput(long cycle, LightweightA500Machine machine)
    {
        var plane = _pendingPlane;
        var address = _pendingAddress;
        var applyModulo = _pendingModulo;
        _hasPendingOutput = false;
        _pendingOutputCycle = long.MaxValue;
        var value = machine.ReadChipWordDma(address);
        _dataLatches[plane] = value;
        machine.OnBitplaneDataOutput(plane, value, cycle);
        var pointer = MaskAddress(address + 2);
        if (applyModulo)
        {
            pointer = MaskAddress(unchecked(pointer +
                (uint)machine.GetBitplaneModulo(plane)));
        }
        _pointers[plane] = pointer;
        machine.SetBitplanePointerFromDma(plane, pointer);
        LastOutputCycle = cycle;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastPlane = plane;
        LastAddress = address;
#endif
    }

    private void ArmStop(long cycle, long targetCycle)
    {
        _ = cycle;
        var unitCycles = FetchUnitColorClocks *
            LightweightClock.CpuCyclesPerColorClock;
        var delta = Math.Max(0, targetCycle - _sequenceOriginCycle);
        var units = (delta + unitCycles - 1) / unitCycles;
        _terminalUnitStartCycle = _sequenceOriginCycle + (units * unitCycles);
        _completionCycle = _terminalUnitStartCycle + unitCycles;
    }

    private static bool IsVerticalWindowOpen(LightweightA500Machine machine)
    {
        var start = machine.GetCustomRegister(LightweightRegisters.Diwstrt) >> 8;
        var stop = machine.GetCustomRegister(LightweightRegisters.Diwstop) >> 8;
        // OCS supplies V8 as the complement of V7, not from a comparison
        // with VSTART (e.g. $3C means line $13C even when VSTART is $2C).
        if (stop < 0x80)
        {
            stop += 0x100;
        }
        if (stop <= start)
        {
            stop += 0x100;
        }
        return machine.BeamLine >= start && machine.BeamLine < stop;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetSupportedPlaneCount(ushort bplcon0)
    {
        var count = (bplcon0 >> 12) & 7;
        return count <= ((bplcon0 & 0x8000) != 0 ? 4 : 6) ? count : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldClock(LightweightA500Machine machine)
        => ShouldClock(
            IsDmaEnabled(machine.Dmacon),
            GetSupportedPlaneCount(_effectiveBplcon0));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldClock(bool dmaEnabled, int planeCount)
        => _runActive || _hasPendingOutput || dmaEnabled && planeCount != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDmaEnabled(ushort dmacon)
        => (dmacon & (DmaconMaster | DmaconBitplane)) ==
            (DmaconMaster | DmaconBitplane);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MaskAddress(uint address) => address & OcsChipAddressMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NextCckAfter(long cycle)
        => LightweightBusArbiter.AlignToSlot(cycle + 1);

    private void PublishNextInactiveControlCycle(
        long cycle,
        LightweightA500Machine machine)
    {
        var horizontal = machine.BeamColorClock;
        var target = !_startPermit
            ? DdfLimitReleaseHorizontal
            : machine.GetCustomRegister(LightweightRegisters.Ddfstrt) & DdfMask;
        var colorClocksPerLine = LightweightClock.CpuCyclesPerLine /
            LightweightClock.CpuCyclesPerColorClock;
        if ((uint)target >= (uint)colorClocksPerLine)
        {
            return;
        }

        var delta = target - horizontal;
        if (delta <= 0)
        {
            delta += colorClocksPerLine;
        }
        Publish(cycle + ((long)delta * LightweightClock.CpuCyclesPerColorClock));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Publish(long cycle)
        => NextCycle = Math.Min(NextCycle, cycle);
}
