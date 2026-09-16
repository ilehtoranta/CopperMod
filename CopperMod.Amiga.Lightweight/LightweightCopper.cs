using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Compact OCS Copper state. Address input and Chip-RAM output are separate
/// CCK phases; accepted addresses survive later pointer and control writes.
/// </summary>
internal sealed class LightweightCopper
{
    private enum ControlStage : byte
    {
        Dormant,
        ReadFirst,
        ReadSecond,
        WaitIdle,
        WaitCompare,
        SkipIdle,
        SkipDelay,
        SkipCompare,
        End
    }

    private enum WordPurpose : byte
    {
        First,
        Second
    }

    private const ushort DmaconMaster = 0x0200;
    private const ushort DmaconCopper = 0x0080;
    private const uint OcsChipAddressMask = 0x0007_FFFEu;

    private ControlStage _stage;
    private WordPurpose _pendingPurpose;
    private uint _programCounter;
    private uint _pendingAddress;
    private long _pendingOutputCycle;
    private uint _decodeGeneration;
    private uint _pendingGeneration;
    private ushort _firstWord;
    private ushort _waitFirst;
    private ushort _waitSecond;
    private bool _skipNextMove;

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long PendingOutputCycle => _pendingOutputCycle;
    internal long LastOutputCycle { get; private set; } = long.MinValue;
    internal long LastAcceptedInputCycle { get; private set; } = long.MinValue;
    internal long LastInstructionFirstInputCycle { get; private set; } = long.MinValue;
    internal long LastMoveCycle { get; private set; } = long.MinValue;
    internal uint ProgramCounter => _programCounter;
    internal bool IsWaiting => _stage is ControlStage.WaitIdle or ControlStage.WaitCompare or ControlStage.End;
    internal bool HasPendingOutput => _pendingOutputCycle != long.MaxValue;

    internal void Reset()
    {
        _stage = ControlStage.Dormant;
        _pendingPurpose = default;
        _programCounter = 0;
        _pendingAddress = 0;
        _pendingOutputCycle = long.MaxValue;
        _decodeGeneration = 0;
        _pendingGeneration = 0;
        _firstWord = 0;
        _waitFirst = 0;
        _waitSecond = 0;
        _skipNextMove = false;
        NextCycle = long.MaxValue;
        LastOutputCycle = long.MinValue;
        LastAcceptedInputCycle = long.MinValue;
        LastInstructionFirstInputCycle = long.MinValue;
        LastMoveCycle = long.MinValue;
    }

    internal void OnDmaconChanged(
        ushort previous,
        ushort current,
        long cycle,
        LightweightA500Machine machine)
    {
        var wasEnabled = IsDmaEnabled(previous);
        var enabled = IsDmaEnabled(current);
        if (!wasEnabled && enabled)
        {
            if (_stage == ControlStage.Dormant)
            {
                Restart(machine.GetCopperListPointer(secondList: false), cycle);
            }
            ActivateAfter(cycle);
        }
        else if (wasEnabled && !enabled)
        {
            NextCycle = HasPendingOutput ? _pendingOutputCycle : long.MaxValue;
        }
    }

    internal void OnRegisterWrite(
        ushort offset,
        long cycle,
        LightweightA500Machine machine)
    {
        if (offset == LightweightRegisters.Copjmp1)
        {
            Restart(machine.GetCopperListPointer(secondList: false), cycle);
        }
        else if (offset == LightweightRegisters.Copjmp2)
        {
            Restart(machine.GetCopperListPointer(secondList: true), cycle);
        }

        if (IsDmaEnabled(machine.Dmacon) &&
            offset is LightweightRegisters.Copjmp1 or LightweightRegisters.Copjmp2)
        {
            ActivateAfter(cycle);
        }
    }

    internal void OnFrameStart(long cycle, LightweightA500Machine machine)
    {
        Restart(machine.GetCopperListPointer(secondList: false), cycle);
        if (IsDmaEnabled(machine.Dmacon))
        {
            ActivateAfter(cycle);
        }
    }

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Copper must advance at its published CCK.");

        if (_pendingOutputCycle == cycle)
        {
            CompleteWord(cycle, machine);
        }

        if (IsDmaEnabled(machine.Dmacon))
        {
            var horizontal = machine.BeamColorClock;
            var phase = ClassifyInput(horizontal);
            if (phase == CopperInputPhase.Normal)
            {
                AdvanceControlInput(cycle, machine);
            }
            // The 227-CCK wrap dummy consumes no instruction address. Its
            // undocumented data-bus value is deliberately not exposed in H2.
        }

        NextCycle = HasPendingOutput ||
            IsDmaEnabled(machine.Dmacon) && _stage != ControlStage.Dormant
                ? cycle + LightweightClock.CpuCyclesPerColorClock
                : long.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool OwnsOutputSlot(long cycle)
        => _pendingOutputCycle == cycle || LastOutputCycle == cycle;

    private void AdvanceControlInput(long cycle, LightweightA500Machine machine)
    {
        if (HasPendingOutput)
        {
            return;
        }

        switch (_stage)
        {
            case ControlStage.ReadFirst:
                TryAcceptWord(cycle, WordPurpose.First, machine);
                break;
            case ControlStage.ReadSecond:
                TryAcceptWord(cycle, WordPurpose.Second, machine);
                break;
            case ControlStage.WaitIdle:
                _stage = ControlStage.WaitCompare;
                break;
            case ControlStage.WaitCompare:
                if (ComparisonSatisfied(machine, _waitFirst, _waitSecond))
                {
                    _stage = ControlStage.ReadFirst;
                }
                break;
            case ControlStage.SkipIdle:
                _stage = ControlStage.SkipDelay;
                break;
            case ControlStage.SkipDelay:
                _stage = ControlStage.SkipCompare;
                break;
            case ControlStage.SkipCompare:
                _skipNextMove = ComparisonSatisfied(machine, _waitFirst, _waitSecond);
                _stage = ControlStage.ReadFirst;
                TryAcceptWord(cycle, WordPurpose.First, machine);
                break;
        }
    }

    private void TryAcceptWord(
        long inputCycle,
        WordPurpose purpose,
        LightweightA500Machine machine)
    {
        var outputCycle = inputCycle + LightweightClock.CpuCyclesPerColorClock;
        if (!machine.CanCopperOwnOutputSlot(outputCycle))
        {
            return;
        }

        _pendingPurpose = purpose;
        _pendingAddress = _programCounter;
        _pendingOutputCycle = outputCycle;
        _pendingGeneration = _decodeGeneration;
        _programCounter = MaskAddress(_programCounter + 2);
#if LIGHTWEIGHT_DIAGNOSTICS
        LastAcceptedInputCycle = inputCycle;
        if (purpose == WordPurpose.First)
        {
            LastInstructionFirstInputCycle = inputCycle;
        }
#endif
    }

    private void CompleteWord(long cycle, LightweightA500Machine machine)
    {
        var purpose = _pendingPurpose;
        var address = _pendingAddress;
        var generation = _pendingGeneration;
        _pendingOutputCycle = long.MaxValue;
        LastOutputCycle = cycle;
        var value = machine.ReadChipWordDma(address);
        if (generation != _decodeGeneration)
        {
            return;
        }

        if (purpose == WordPurpose.First)
        {
            _firstWord = value;
            _stage = ControlStage.ReadSecond;
            return;
        }

        DecodeInstruction(value, cycle, machine);
    }

    private void DecodeInstruction(
        ushort secondWord,
        long cycle,
        LightweightA500Machine machine)
    {
        var firstWord = _firstWord;
        if ((firstWord & 1) == 0)
        {
            _stage = ControlStage.ReadFirst;
            if (_skipNextMove)
            {
                _skipNextMove = false;
                return;
            }

            var offset = (ushort)(firstWord & 0x01FE);
            if (!CanWriteRegister(offset, machine.GetCustomRegister(LightweightRegisters.Copcon)))
            {
                _stage = ControlStage.Dormant;
                return;
            }

#if LIGHTWEIGHT_DIAGNOSTICS
            LastMoveCycle = cycle;
#endif
            machine.WriteCustomRegisterFromCopper(offset, secondWord, cycle);
            return;
        }

        _waitFirst = firstWord;
        _waitSecond = secondWord;
        if (firstWord == 0xFFFF && secondWord == 0xFFFE)
        {
            _stage = ControlStage.End;
            return;
        }

        _stage = (secondWord & 1) == 0
            ? ControlStage.WaitIdle
            : ControlStage.SkipIdle;
    }

    private static bool ComparisonSatisfied(
        LightweightA500Machine machine,
        ushort firstWord,
        ushort secondWord)
    {
        if ((secondWord & 0x8000) == 0 && machine.BlitterBusy)
        {
            return false;
        }

        // VP7 is always compared. IR2 bit 15 controls BFD, not its mask.
        var mask = (ushort)(0x8000 | (secondWord & 0x7FFE));
        var beam = (ushort)(((machine.BeamLine & 0xFF) << 8) |
            ((machine.BeamColorClock + 3) & 0x00FE));
        var target = (ushort)(firstWord & mask);
        var observed = (ushort)(beam & mask);
        return observed >= target;
    }

    private void Restart(uint address, long cycle)
    {
        _decodeGeneration++;
        _programCounter = MaskAddress(address);
        _firstWord = 0;
        _waitFirst = 0;
        _waitSecond = 0;
        _skipNextMove = false;
        _stage = ControlStage.ReadFirst;
        if (NextCycle != long.MaxValue)
        {
            ActivateAfter(cycle);
        }
    }

    private void ActivateAfter(long cycle)
    {
        var next = LightweightBusArbiter.AlignToSlot(cycle + 1);
        if (next <= cycle)
        {
            next = cycle + LightweightClock.CpuCyclesPerColorClock;
        }
        NextCycle = HasPendingOutput ? Math.Min(_pendingOutputCycle, next) : next;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDmaEnabled(ushort dmacon)
        => (dmacon & (DmaconMaster | DmaconCopper)) ==
            (DmaconMaster | DmaconCopper);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MaskAddress(uint address) => address & OcsChipAddressMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanWriteRegister(ushort offset, ushort copcon)
        => offset >= ((copcon & 0x0002) == 0 ? 0x080 : 0x040);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CopperInputPhase ClassifyInput(int physicalHorizontal)
    {
        if (physicalHorizontal == 224)
        {
            return CopperInputPhase.WrapDummy;
        }
        if (physicalHorizontal == 225 ||
            physicalHorizontal < 224 && (physicalHorizontal & 1) == 0)
        {
            return CopperInputPhase.Normal;
        }
        return CopperInputPhase.None;
    }

    private enum CopperInputPhase : byte
    {
        None,
        Normal,
        WrapDummy
    }
}
