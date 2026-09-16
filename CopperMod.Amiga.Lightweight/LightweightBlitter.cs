using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Compact OCS blitter sequencer. Control inputs and their following physical
/// output phases are kept separate; only enabled channels own the Chip-RAM
/// bus.
/// </summary>
internal sealed class LightweightBlitter
{
    private enum AreaChannel : byte
    {
        Idle,
        A,
        B,
        C,
        D
    }

    private const ushort DmaconMaster = 0x0200;
    private const ushort DmaconBlitter = 0x0040;
    private const ushort DmaconNasty = 0x0400;
    private const ushort BlitterInterrupt = 0x0040;
    private const ushort Bltcon1LineMode = 0x0001;
    private const ushort Bltcon1SingleDot = 0x0002;
    private const ushort Bltcon1LineAul = 0x0004;
    private const ushort Bltcon1LineSul = 0x0008;
    private const ushort Bltcon1LineSud = 0x0010;
    private const ushort Bltcon1LineSign = 0x0040;
    private const ushort Bltcon1Descending = 0x0002;
    private const ushort Bltcon1FillCarryIn = 0x0004;
    private const ushort Bltcon1InclusiveFill = 0x0008;
    private const ushort Bltcon1ExclusiveFill = 0x0010;
    private const uint OcsChipAddressMask = 0x0007_FFFEu;
    private const int NiceDmaGrantsBeforeYield = 3;

    private bool _active;
    private bool _busy;
    private bool _zero;
    private bool _useA;
    private bool _useB;
    private bool _useC;
    private bool _useD;
    private bool _dOnly;
    private bool _bOnly;
    private bool _requiresDma;
    private bool _lineMode;
    private bool _lineDraw;
    private bool _lineSingleDot;
    private bool _lineSud;
    private bool _lineSul;
    private bool _lineAul;
    private bool _lineSign;
    private bool _descending;
    private bool _fillEnabled;
    private bool _fillExclusive;
    private bool _fillCarryInitial;
    private bool _fillCarry;
    private bool _effectiveNasty;
    private bool _pendingNasty;
    private long _nastyVisibilityCycle;
    private bool _cpuRequestPending;
    private long _cpuRequestCandidateCycle;
    private int _niceDmaGrants;
    private int _startupRemaining;
    private int _phaseIndex;
    private int _phaseCount;
    private int _widthWords;
    private int _height;
    private int _wordX;
    private int _rowY;
    private int _lineIndex;
    private int _lineLength;
    private int _lineBit;
    private int _lineY;
    private int _lineLastDrawnY;
    private int _lineError;
    private int _lineSourceRowStride;
    private int _lineBPatternStride;
    private byte _minterm;
    private int _shiftA;
    private int _shiftB;
    private short _moduloA;
    private short _moduloB;
    private short _moduloC;
    private short _moduloD;
    private uint _pointerA;
    private uint _pointerB;
    private uint _pointerC;
    private uint _pointerD;
    private ushort _firstWordMask;
    private ushort _lastWordMask;
    private ushort _dataA;
    private ushort _dataB;
    private ushort _dataC;
    private ushort _previousA;
    private ushort _previousB;
    private bool _hasPendingOutput;
    private bool _pendingUsesBus;
    private AreaChannel _pendingChannel;
    private uint _pendingAddress;
    private ushort _pendingWriteValue;
    private long _pendingOutputCycle;
    private bool _deferredStart;
    private ushort _deferredSize;
    private long _startCycle;

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long PendingOutputCycle => _hasPendingOutput
        ? _pendingOutputCycle
        : long.MaxValue;
    internal long LastAcceptedInputCycle { get; private set; } = long.MinValue;
    internal long LastBusOutputCycle { get; private set; } = long.MinValue;
    internal long LastCompletionCycle { get; private set; } = long.MinValue;
    internal long LastTerminationCycle { get; private set; } = long.MinValue;
    internal bool Busy => _busy;
    internal bool Zero => _zero;
    internal bool Active => _active;
    internal bool EffectiveNasty => _effectiveNasty;
    internal ushort StatusBits => (ushort)((_busy ? 0x4000 : 0) |
        (_zero ? 0x2000 : 0));

    internal void Reset()
    {
        _active = false;
        _busy = false;
        _zero = true;
        _useA = false;
        _useB = false;
        _useC = false;
        _useD = false;
        _dOnly = false;
        _bOnly = false;
        _requiresDma = false;
        _lineMode = false;
        _lineDraw = false;
        _lineSingleDot = false;
        _lineSud = false;
        _lineSul = false;
        _lineAul = false;
        _lineSign = false;
        _descending = false;
        _fillEnabled = false;
        _fillExclusive = false;
        _fillCarryInitial = false;
        _fillCarry = false;
        _effectiveNasty = false;
        _pendingNasty = false;
        _nastyVisibilityCycle = long.MaxValue;
        _cpuRequestPending = false;
        _cpuRequestCandidateCycle = long.MaxValue;
        _niceDmaGrants = 0;
        _startupRemaining = 0;
        _phaseIndex = 0;
        _phaseCount = 0;
        _widthWords = 0;
        _height = 0;
        _wordX = 0;
        _rowY = 0;
        _lineIndex = 0;
        _lineLength = 0;
        _lineBit = 0;
        _lineY = 0;
        _lineLastDrawnY = int.MinValue;
        _lineError = 0;
        _lineSourceRowStride = 0;
        _lineBPatternStride = 0;
        _minterm = 0;
        _shiftA = 0;
        _shiftB = 0;
        _moduloA = 0;
        _moduloB = 0;
        _moduloC = 0;
        _moduloD = 0;
        _pointerA = 0;
        _pointerB = 0;
        _pointerC = 0;
        _pointerD = 0;
        _firstWordMask = 0;
        _lastWordMask = 0;
        _dataA = 0;
        _dataB = 0;
        _dataC = 0;
        _previousA = 0;
        _previousB = 0;
        _hasPendingOutput = false;
        _pendingUsesBus = false;
        _pendingChannel = default;
        _pendingAddress = 0;
        _pendingWriteValue = 0;
        _pendingOutputCycle = long.MaxValue;
        _deferredStart = false;
        _deferredSize = 0;
        _startCycle = long.MinValue;
        NextCycle = long.MaxValue;
        LastAcceptedInputCycle = long.MinValue;
        LastBusOutputCycle = long.MinValue;
        LastCompletionCycle = long.MinValue;
        LastTerminationCycle = long.MinValue;
    }

    internal void OnDmaconChanged(
        ushort previous,
        ushort current,
        long cycle,
        LightweightA500Machine machine)
    {
        if (((previous ^ current) & DmaconNasty) != 0)
        {
            _pendingNasty = (current & DmaconNasty) != 0;
            _nastyVisibilityCycle =
                cycle + (2 * LightweightClock.CpuCyclesPerColorClock);
            Publish(_nastyVisibilityCycle);
        }

        if (_active && IsDmaEnabled(current))
        {
            Publish(NextCckAfter(cycle));
        }

        RefreshNextCycle(cycle, machine);
    }

    internal void OnRegisterWrite(
        ushort offset,
        ushort value,
        long cycle,
        LightweightA500Machine machine)
    {
        if (offset != LightweightRegisters.Bltsize)
        {
            return;
        }

        if (_active)
        {
            _deferredStart = true;
            _deferredSize = value;
            return;
        }

        Start(value, cycle, machine);
    }

    internal void BeginCpuRequest(long candidateCycle)
    {
        _cpuRequestPending = true;
        _cpuRequestCandidateCycle = candidateCycle;
        _niceDmaGrants = 0;
    }

    internal void UpdateCpuRequestCandidate(long candidateCycle)
        => _cpuRequestCandidateCycle = candidateCycle;

    internal void EndCpuRequest()
    {
        _cpuRequestPending = false;
        _cpuRequestCandidateCycle = long.MaxValue;
        _niceDmaGrants = 0;
    }

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Blitter must advance at its published CCK.");

        if (_nastyVisibilityCycle <= cycle)
        {
            var changed = _effectiveNasty != _pendingNasty;
            _effectiveNasty = _pendingNasty;
            _nastyVisibilityCycle = long.MaxValue;
            if (changed && !_effectiveNasty)
            {
                _niceDmaGrants = 0;
            }
        }

        var ownedBusThisCycle = false;
        if (_hasPendingOutput && _pendingOutputCycle == cycle)
        {
            ownedBusThisCycle = _pendingUsesBus;
            CompleteOutput(cycle, machine);
        }

        if (_active && !_hasPendingOutput && _startCycle != cycle &&
            (!_requiresDma || IsDmaEnabled(machine.Dmacon)))
        {
            if (_startupRemaining != 0)
            {
                AdvanceStartup(cycle, machine, ownedBusThisCycle);
            }
            else
            {
                TryAcceptPhase(cycle, machine, ownedBusThisCycle);
            }
        }

        NextCycle = long.MaxValue;
        RefreshNextCycle(cycle, machine);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool OwnsOutputSlot(long cycle)
        => (_hasPendingOutput && _pendingUsesBus &&
                _pendingOutputCycle == cycle) ||
            LastBusOutputCycle == cycle;

    private void Start(
        ushort size,
        long cycle,
        LightweightA500Machine machine)
    {
        var bltcon0 = machine.GetCustomRegister(LightweightRegisters.Bltcon0);
        var bltcon1 = machine.GetCustomRegister(LightweightRegisters.Bltcon1);
        _lineMode = (bltcon1 & Bltcon1LineMode) != 0;
        _useA = (bltcon0 & 0x0800) != 0;
        _useB = (bltcon0 & 0x0400) != 0;
        _useC = (bltcon0 & 0x0200) != 0;
        _useD = (bltcon0 & 0x0100) != 0;
        _descending = !_lineMode && (bltcon1 & Bltcon1Descending) != 0;
        _fillEnabled = !_lineMode && _descending &&
            (bltcon1 & (Bltcon1InclusiveFill | Bltcon1ExclusiveFill)) != 0;
        _fillExclusive = !_lineMode &&
            (bltcon1 & Bltcon1ExclusiveFill) != 0;
        _fillCarryInitial = !_lineMode &&
            (bltcon1 & Bltcon1FillCarryIn) != 0;
        _fillCarry = _fillCarryInitial;
        _dOnly = !_lineMode && !_useA && !_useB && !_useC && _useD &&
            !_fillEnabled;
        _bOnly = !_lineMode && !_useA && _useB && !_useC && !_useD &&
            !_fillEnabled;
        // OCS line mode only runs its DMA sequence when C is enabled. The D
        // enable bit does not suppress the physical destination write.
        _requiresDma = _lineMode
            ? _useC
            : _useA || _useB || _useC || _useD;
        _widthWords = size & 0x003F;
        if (_widthWords == 0)
        {
            _widthWords = 64;
        }
        if (_lineMode && _widthWords != 2)
        {
            machine.ReportUnsupportedFeature(
                "OCS line-mode BLTSIZE widths other than two are not modeled");
        }
        _height = (size >> 6) & 0x03FF;
        if (_height == 0)
        {
            _height = 1024;
        }

        _minterm = (byte)bltcon0;
        _shiftA = bltcon0 >> 12;
        _shiftB = bltcon1 >> 12;
        _firstWordMask = machine.GetCustomRegister(LightweightRegisters.Bltafwm);
        _lastWordMask = machine.GetCustomRegister(LightweightRegisters.Bltalwm);
        _moduloA = unchecked((short)machine.GetCustomRegister(LightweightRegisters.Bltamod));
        _moduloB = unchecked((short)machine.GetCustomRegister(LightweightRegisters.Bltbmod));
        _moduloC = unchecked((short)machine.GetCustomRegister(LightweightRegisters.Bltcmod));
        _moduloD = unchecked((short)machine.GetCustomRegister(LightweightRegisters.Bltdmod));
        _pointerA = machine.GetBlitterPointer(LightweightRegisters.Bltapth);
        _pointerB = machine.GetBlitterPointer(LightweightRegisters.Bltbpth);
        _pointerC = machine.GetBlitterPointer(LightweightRegisters.Bltcpth);
        _pointerD = machine.GetBlitterPointer(LightweightRegisters.Bltdpth);
        _dataA = machine.GetCustomRegister(LightweightRegisters.Bltadat);
        _dataB = machine.GetCustomRegister(LightweightRegisters.Bltbdat);
        _dataC = machine.GetCustomRegister(LightweightRegisters.Bltcdat);
        _previousA = 0;
        _previousB = 0;
        _wordX = 0;
        _rowY = 0;
        _phaseIndex = 0;
        if (_lineMode)
        {
            StartLine(bltcon1);
        }
        else
        {
            _phaseCount = 2 + (_useB ? 1 : 0) +
                (_useC || _fillEnabled ? 1 : 0);
            _startupRemaining = _dOnly ? 6 : _bOnly ? 2 : 3;
        }
        _hasPendingOutput = false;
        _pendingUsesBus = false;
        _pendingOutputCycle = long.MaxValue;
        _active = true;
        _busy = true;
        _zero = true;
        _deferredStart = false;
        _startCycle = cycle;
        LastCompletionCycle = long.MinValue;
        LastTerminationCycle = long.MinValue;
        Publish(NextCckAfter(cycle));
    }

    private void StartLine(ushort bltcon1)
    {
        _lineIndex = 0;
        _lineLength = _height;
        _lineBit = _shiftA & 0x0F;
        _lineY = 0;
        _lineLastDrawnY = int.MinValue;
        _lineSingleDot = (bltcon1 & Bltcon1SingleDot) != 0;
        _lineSud = (bltcon1 & Bltcon1LineSud) != 0;
        _lineSul = (bltcon1 & Bltcon1LineSul) != 0;
        _lineAul = (bltcon1 & Bltcon1LineAul) != 0;
        _lineSign = (bltcon1 & Bltcon1LineSign) != 0;
        _lineError = unchecked((short)_pointerA);
        _lineSourceRowStride = _moduloC & ~1;
        _lineBPatternStride = _moduloB & ~1;
        _phaseCount = 4;
        // The line engine begins after four non-reserving startup CCKs. Each
        // pixel then consumes exactly four retained input/output phases.
        _startupRemaining = 4;
        PrepareLinePixel();
    }

    private void AdvanceStartup(
        long cycle,
        LightweightA500Machine machine,
        bool ownedBusThisCycle)
    {
        // Line startup clocks are internal and can overlap another owner's
        // output. Area startup retains the control/output exclusion already
        // established by H3a.
        if (!_lineMode && !machine.CanBlitterAdvanceControl(cycle))
        {
            return;
        }

        _startupRemaining--;
        if (_startupRemaining != 0)
        {
            return;
        }

        if (_dOnly)
        {
            _phaseIndex = _phaseCount - 1;
        }
        TryAcceptPhase(cycle, machine, ownedBusThisCycle);
    }

    private void TryAcceptPhase(
        long cycle,
        LightweightA500Machine machine,
        bool ownedBusThisCycle)
    {
        if (_lineMode)
        {
            TryAcceptLinePhase(cycle, machine, ownedBusThisCycle);
            return;
        }

        if (_phaseIndex >= _phaseCount ||
            !machine.CanBlitterAdvanceControl(cycle))
        {
            return;
        }

        var channel = GetPhaseChannel(_phaseIndex);
        var usesBus = ChannelEnabled(channel);
        if (usesBus && ShouldYieldToCpu(cycle, machine, ownedBusThisCycle))
        {
            return;
        }

        _pendingChannel = channel;
        _pendingUsesBus = usesBus;
        _pendingAddress = channel switch
        {
            AreaChannel.A => _pointerA,
            AreaChannel.B => _pointerB,
            AreaChannel.C => _pointerC,
            _ => _pointerD
        };
        if (channel == AreaChannel.D)
        {
            _pendingWriteValue = ComputeAreaOutput();
            if (_pendingWriteValue != 0)
            {
                _zero = false;
            }

            if (_dOnly && IsFinalWord())
            {
                PublishMainCompletion(cycle, machine);
            }
        }

        _hasPendingOutput = true;
        _pendingOutputCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastAcceptedInputCycle = cycle;
#endif
    }

    private void TryAcceptLinePhase(
        long cycle,
        LightweightA500Machine machine,
        bool ownedBusThisCycle)
    {
        if (_phaseIndex >= _phaseCount)
        {
            return;
        }

        var channel = GetLinePhaseChannel(_phaseIndex);
        var usesBus = _lineDraw && channel != AreaChannel.Idle;
        if (usesBus &&
            (!machine.CanBlitterAdvanceControl(cycle) ||
             ShouldYieldToCpu(cycle, machine, ownedBusThisCycle)))
        {
            return;
        }

        _pendingChannel = channel;
        _pendingUsesBus = usesBus;
        _pendingAddress = channel switch
        {
            AreaChannel.B => _pointerB,
            AreaChannel.C => _pointerC,
            AreaChannel.D => _lineIndex == 0 ? _pointerD : _pointerC,
            _ => 0
        };
        if (usesBus && channel == AreaChannel.D)
        {
            _pendingWriteValue = ComputeLineOutput();
            if (_pendingWriteValue != 0)
            {
                _zero = false;
            }
        }

        _hasPendingOutput = true;
        _pendingOutputCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastAcceptedInputCycle = cycle;
#endif
    }

    private void CompleteOutput(long cycle, LightweightA500Machine machine)
    {
        var channel = _pendingChannel;
        var usesBus = _pendingUsesBus;
        var address = _pendingAddress;
        var writeValue = _pendingWriteValue;
        _hasPendingOutput = false;
        _pendingUsesBus = false;
        _pendingOutputCycle = long.MaxValue;

        if (usesBus)
        {
            LastBusOutputCycle = cycle;
            if (_cpuRequestPending)
            {
                _niceDmaGrants++;
            }
        }

        if (_lineMode)
        {
            CompleteLineOutput(
                channel,
                usesBus,
                address,
                writeValue,
                cycle,
                machine);
            return;
        }

        switch (channel)
        {
            case AreaChannel.A when _useA:
                _dataA = machine.ReadChipWordDma(address);
                _pointerA = AddPointer(_pointerA, _descending ? -2 : 2);
                machine.SetBlitterDataFromDma(LightweightRegisters.Bltadat, _dataA);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltapth, _pointerA);
                break;
            case AreaChannel.B when _useB:
                _dataB = machine.ReadChipWordDma(address);
                _pointerB = AddPointer(_pointerB, _descending ? -2 : 2);
                machine.SetBlitterDataFromDma(LightweightRegisters.Bltbdat, _dataB);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltbpth, _pointerB);
                if (_bOnly && IsFinalWord())
                {
                    PublishMainCompletion(cycle, machine);
                }
                break;
            case AreaChannel.C when _useC:
                _dataC = machine.ReadChipWordDma(address);
                _pointerC = AddPointer(_pointerC, _descending ? -2 : 2);
                machine.SetBlitterDataFromDma(LightweightRegisters.Bltcdat, _dataC);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltcpth, _pointerC);
                break;
            case AreaChannel.D:
                if (_useD)
                {
                    machine.WriteChipWordDma(address, writeValue);
                    _pointerD = AddPointer(_pointerD, _descending ? -2 : 2);
                    machine.SetBlitterPointerFromDma(LightweightRegisters.Bltdpth, _pointerD);
                }
                break;
        }

        _phaseIndex++;
        if (_phaseIndex < _phaseCount)
        {
            return;
        }

        AdvanceWord(cycle, machine);
    }

    private void CompleteLineOutput(
        AreaChannel channel,
        bool usesBus,
        uint address,
        ushort writeValue,
        long cycle,
        LightweightA500Machine machine)
    {
        if (usesBus)
        {
            switch (channel)
            {
                case AreaChannel.B:
                {
                    var value = machine.ReadChipWordDma(address);
                    // The first B access reloads the hidden latch. The second
                    // exposes the pattern word and advances by BLTBMOD.
                    if (_phaseIndex == 1)
                    {
                        _dataB = value;
                        _pointerB = AddPointer(_pointerB, _lineBPatternStride);
                        machine.SetBlitterDataFromDma(
                            LightweightRegisters.Bltbdat,
                            _dataB);
                        machine.SetBlitterPointerFromDma(
                            LightweightRegisters.Bltbpth,
                            _pointerB);
                    }
                    break;
                }
                case AreaChannel.C:
                    _dataC = machine.ReadChipWordDma(address);
                    machine.SetBlitterDataFromDma(
                        LightweightRegisters.Bltcdat,
                        _dataC);
                    break;
                case AreaChannel.D:
                    machine.WriteChipWordDma(address, writeValue);
                    _lineLastDrawnY = _lineY;
                    break;
            }
        }

        _phaseIndex++;
        if (_phaseIndex < _phaseCount)
        {
            return;
        }

        AdvanceLinePixel(cycle, machine);
    }

    private void AdvanceLinePixel(long cycle, LightweightA500Machine machine)
    {
        _lineIndex++;
        _rowY = _lineIndex;
        if (_lineIndex >= _lineLength)
        {
            PublishLineRegisters(machine);
            PublishMainCompletion(cycle, machine);
            FinishTermination(cycle, machine);
            return;
        }

        StepLineAddress();
        PublishLineRegisters(machine);
        _phaseIndex = 0;
        PrepareLinePixel();
    }

    private void PrepareLinePixel()
        => _lineDraw = _useC &&
            (!_lineSingleDot || _lineY != _lineLastDrawnY);

    private void StepLineAddress()
    {
        if (_lineSign)
        {
            _lineError = unchecked(_lineError + _moduloB);
        }
        else
        {
            _lineError = unchecked(_lineError + _moduloA);
            MoveLineMinorAxis();
        }

        MoveLineMajorAxis();
        _lineSign = _lineError < 0;
    }

    private void MoveLineMajorAxis()
    {
        if (_lineSud)
        {
            MoveLineX(_lineAul ? -1 : 1);
        }
        else
        {
            MoveLineY(_lineAul ? -1 : 1);
        }
    }

    private void MoveLineMinorAxis()
    {
        if (_lineSud)
        {
            MoveLineY(_lineSul ? -1 : 1);
        }
        else
        {
            MoveLineX(_lineSul ? -1 : 1);
        }
    }

    private void MoveLineX(int direction)
    {
        if (direction >= 0)
        {
            _lineBit++;
            if (_lineBit <= 15)
            {
                return;
            }

            _lineBit = 0;
            _pointerC = AddPointer(_pointerC, 2);
            _pointerD = AddPointer(_pointerD, 2);
            return;
        }

        _lineBit--;
        if (_lineBit >= 0)
        {
            return;
        }

        _lineBit = 15;
        _pointerC = AddPointer(_pointerC, -2);
        _pointerD = AddPointer(_pointerD, -2);
    }

    private void MoveLineY(int direction)
    {
        var offset = direction >= 0
            ? _lineSourceRowStride
            : -_lineSourceRowStride;
        _pointerC = AddPointer(_pointerC, offset);
        _pointerD = AddPointer(_pointerD, offset);
        _lineY += direction;
    }

    private void PublishLineRegisters(LightweightA500Machine machine)
    {
        if (_useA)
        {
            _pointerA = (uint)(ushort)_lineError;
            machine.SetBlitterPointerFromDma(
                LightweightRegisters.Bltapth,
                _pointerA);
        }
        if (_useB)
        {
            machine.SetBlitterPointerFromDma(
                LightweightRegisters.Bltbpth,
                _pointerB);
        }

        machine.SetBlitterPointerFromDma(
            LightweightRegisters.Bltcpth,
            _pointerC);
        if (_lineIndex >= _lineLength)
        {
            // On OCS BLTDPT is only used by the first pixel and finishes as a
            // mirror of the live C pointer.
            _pointerD = _pointerC;
        }
        machine.SetBlitterPointerFromDma(
            LightweightRegisters.Bltdpth,
            _pointerD);
    }

    private void AdvanceWord(long cycle, LightweightA500Machine machine)
    {
        _wordX++;
        if (_wordX < _widthWords)
        {
            _phaseIndex = 0;
            return;
        }

        _wordX = 0;
        _rowY++;
        if (_rowY < _height)
        {
            if (_useA)
            {
                _pointerA = AddModulo(_pointerA, _moduloA, _descending);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltapth, _pointerA);
            }
            if (_useB)
            {
                _pointerB = AddModulo(_pointerB, _moduloB, _descending);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltbpth, _pointerB);
            }
            if (_useC)
            {
                _pointerC = AddModulo(_pointerC, _moduloC, _descending);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltcpth, _pointerC);
            }
            if (_useD)
            {
                _pointerD = AddModulo(_pointerD, _moduloD, _descending);
                machine.SetBlitterPointerFromDma(LightweightRegisters.Bltdpth, _pointerD);
            }
            _fillCarry = _fillCarryInitial;
            _phaseIndex = 0;
            return;
        }

        if (_busy)
        {
            PublishMainCompletion(cycle, machine);
        }
        FinishTermination(cycle, machine);
    }

    private void PublishMainCompletion(long cycle, LightweightA500Machine machine)
    {
        if (!_busy)
        {
            return;
        }

        _busy = false;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastCompletionCycle = cycle;
#endif
        machine.LatchBlitterInterrupt(cycle);
    }

    private void FinishTermination(long cycle, LightweightA500Machine machine)
    {
        _active = false;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastTerminationCycle = cycle;
#endif
        if (!_deferredStart)
        {
            return;
        }

        var deferredSize = _deferredSize;
        _deferredStart = false;
        _deferredSize = 0;
        Start(deferredSize, cycle, machine);
    }

    private ushort ComputeAreaOutput()
    {
        ushort mask = 0xFFFF;
        if (_wordX == 0)
        {
            mask &= _firstWordMask;
        }
        if (_wordX == _widthWords - 1)
        {
            mask &= _lastWordMask;
        }

        var rawA = (ushort)(_dataA & mask);
        var sourceA = ShiftSource(
            rawA,
            ref _previousA,
            _shiftA,
            _descending);
        var sourceB = ShiftSource(
            _dataB,
            ref _previousB,
            _shiftB,
            _descending);
        var output = ApplyMinterm(_minterm, sourceA, sourceB, _dataC);
        return _fillEnabled
            ? ApplyFill(output, _fillExclusive, ref _fillCarry)
            : output;
    }

    private ushort ComputeLineOutput()
    {
        var lineMask = RotateRight(_dataA, _lineBit);
        var textureBit = (_dataB &
            (0x8000 >> ((_shiftB + _lineIndex) & 0x0F))) != 0;
        var texture = textureBit ? (ushort)0xFFFF : (ushort)0;
        return ApplyMinterm(_minterm, lineMask, texture, _dataC);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldYieldToCpu(
        long cycle,
        LightweightA500Machine machine,
        bool ownedBusThisCycle)
    {
        if (!_cpuRequestPending || _effectiveNasty ||
            _niceDmaGrants < NiceDmaGrantsBeforeYield)
        {
            return false;
        }

        var cpuReceivesCurrentSlot =
            _cpuRequestCandidateCycle == cycle &&
            !ownedBusThisCycle &&
            !machine.IsHigherPriorityOutputOwned(cycle);
        return !cpuReceivesCurrentSlot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private AreaChannel GetPhaseChannel(int index)
    {
        if (index == 0)
        {
            return AreaChannel.A;
        }

        index--;
        if (_useB)
        {
            if (index == 0)
            {
                return AreaChannel.B;
            }
            index--;
        }
        if (_useC || _fillEnabled)
        {
            if (index == 0)
            {
                return AreaChannel.C;
            }
        }
        return AreaChannel.D;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private AreaChannel GetLinePhaseChannel(int index)
    {
        if (_useB)
        {
            return index switch
            {
                0 or 1 => AreaChannel.B,
                2 => AreaChannel.C,
                _ => AreaChannel.D
            };
        }

        return index switch
        {
            1 => AreaChannel.C,
            3 => AreaChannel.D,
            _ => AreaChannel.Idle
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ChannelEnabled(AreaChannel channel)
        => channel switch
        {
            AreaChannel.A => _useA,
            AreaChannel.B => _useB,
            AreaChannel.C => _useC,
            _ => _useD
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFinalWord()
        => _rowY == _height - 1 && _wordX == _widthWords - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDmaEnabled(ushort dmacon)
        => (dmacon & (DmaconMaster | DmaconBlitter)) ==
            (DmaconMaster | DmaconBlitter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AddPointer(uint pointer, int offset)
        => unchecked(pointer + (uint)offset) & OcsChipAddressMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AddModulo(uint pointer, short modulo, bool descending)
    {
        var evenModulo = modulo & ~1;
        return AddPointer(pointer, descending ? -evenModulo : evenModulo);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NextCckAfter(long cycle)
        => LightweightBusArbiter.AlignToSlot(cycle + 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RefreshNextCycle(long cycle, LightweightA500Machine machine)
    {
        if (_hasPendingOutput)
        {
            Publish(_pendingOutputCycle);
        }
        if (_nastyVisibilityCycle != long.MaxValue)
        {
            Publish(_nastyVisibilityCycle);
        }
        if (_active && !_hasPendingOutput &&
            (!_requiresDma || IsDmaEnabled(machine.Dmacon)))
        {
            Publish(NextCckAfter(cycle));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Publish(long cycle)
        => NextCycle = Math.Min(NextCycle, cycle);

    private static ushort ShiftSource(
        ushort current,
        ref ushort previous,
        int shift,
        bool descending)
    {
        shift &= 0x0F;
        if (shift == 0)
        {
            previous = current;
            return current;
        }

        uint combined = descending
            ? ((uint)current << 16) | previous
            : ((uint)previous << 16) | current;
        var value = descending
            ? (ushort)(combined >> (16 - shift))
            : (ushort)(combined >> shift);
        previous = current;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort RotateRight(ushort value, int bits)
    {
        bits &= 0x0F;
        return bits == 0
            ? value
            : (ushort)((value >> bits) | (value << (16 - bits)));
    }

    private static ushort ApplyFill(
        ushort value,
        bool exclusive,
        ref bool fillCarry)
    {
        ushort output = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            var mask = (ushort)(1 << bit);
            var input = (value & mask) != 0;
            if (exclusive)
            {
                if (fillCarry)
                {
                    output |= mask;
                }
                if (input)
                {
                    fillCarry = !fillCarry;
                }
                continue;
            }

            if (fillCarry || input)
            {
                output |= mask;
            }
            if (input)
            {
                fillCarry = !fillCarry;
            }
        }
        return output;
    }

    private static ushort ApplyMinterm(
        byte minterm,
        ushort sourceA,
        ushort sourceB,
        ushort sourceC)
    {
        uint result = 0;
        var notA = (ushort)~sourceA;
        var notB = (ushort)~sourceB;
        var notC = (ushort)~sourceC;
        if ((minterm & 0x01) != 0) result |= (uint)(notA & notB & notC);
        if ((minterm & 0x02) != 0) result |= (uint)(notA & notB & sourceC);
        if ((minterm & 0x04) != 0) result |= (uint)(notA & sourceB & notC);
        if ((minterm & 0x08) != 0) result |= (uint)(notA & sourceB & sourceC);
        if ((minterm & 0x10) != 0) result |= (uint)(sourceA & notB & notC);
        if ((minterm & 0x20) != 0) result |= (uint)(sourceA & notB & sourceC);
        if ((minterm & 0x40) != 0) result |= (uint)(sourceA & sourceB & notC);
        if ((minterm & 0x80) != 0) result |= (uint)(sourceA & sourceB & sourceC);
        return (ushort)result;
    }
}
