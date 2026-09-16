using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Compact OCS Agnus sprite DMA sequencer. Each fixed sprite slot has an
/// address-input CCK and a following Chip-RAM output CCK. Accepted addresses
/// remain physical transactions even if control or pointers change meanwhile.
/// </summary>
internal sealed class LightweightSpriteDma
{
    private enum WordPurpose : byte
    {
        ControlPos,
        ControlCtl,
        DataA,
        DataB
    }

    private const int ChannelCount = 8;
    private const int PalControlReloadLine = 25;
    private const ushort DmaconMaster = 0x0200;
    private const ushort DmaconSprite = 0x0020;
    private const ushort DmaconBitplane = 0x0100;
    private const int FirstOutputHorizontal = 0x18;
    private const int LastOutputHorizontal = 0x36;
    private const int FirstInputHorizontal = FirstOutputHorizontal - 1;
    private const int LastInputHorizontal = LastOutputHorizontal - 1;
    private const long FirstFieldInputOffset =
        PalControlReloadLine * LightweightClock.CpuCyclesPerLine +
        FirstInputHorizontal * LightweightClock.CpuCyclesPerColorClock;
    private const int NormalLowResolutionDdfStart = 0x38;
    private const int NormalHighResolutionDdfStart = 0x3C;
    private const uint OcsChipAddressMask = 0x0007_FFFEu;

    private readonly uint[] _pointers = new uint[ChannelCount];
    private readonly ChannelState[] _channels = new ChannelState[ChannelCount];
    private long _nextInputCycle;
    private long _firstFieldInputCycle;
    private bool _hasPendingOutput;
    private int _pendingChannel;
    private WordPurpose _pendingPurpose;
    private uint _pendingAddress;
    private long _pendingOutputCycle;

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long PendingOutputCycle => _hasPendingOutput
        ? _pendingOutputCycle
        : long.MaxValue;
    internal uint PendingAddress => _hasPendingOutput ? _pendingAddress : 0;
    internal long LastInputCycle { get; private set; } = long.MinValue;
    internal long LastOutputCycle { get; private set; } = long.MinValue;
    internal int LastChannel { get; private set; } = -1;
    internal int LastWord { get; private set; } = -1;
    internal uint LastAddress { get; private set; }

    internal void Reset(long frameStartCycle)
    {
        Array.Clear(_pointers);
        Array.Clear(_channels);
        _nextInputCycle = long.MaxValue;
        _firstFieldInputCycle = frameStartCycle + FirstFieldInputOffset;
        _hasPendingOutput = false;
        _pendingChannel = -1;
        _pendingPurpose = default;
        _pendingAddress = 0;
        _pendingOutputCycle = long.MaxValue;
        NextCycle = long.MaxValue;
        LastInputCycle = long.MinValue;
        LastOutputCycle = long.MinValue;
        LastChannel = -1;
        LastWord = -1;
        LastAddress = 0;
    }

    internal void OnDmaconChanged(
        ushort previous,
        ushort current,
        long cycle)
    {
        var wasEnabled = IsDmaEnabled(previous);
        var enabled = IsDmaEnabled(current);
        if (!wasEnabled && enabled)
        {
            ScheduleInputAfter(Math.Max(cycle, _firstFieldInputCycle - 1));
        }
        else if (wasEnabled && !enabled)
        {
            _nextInputCycle = long.MaxValue;
        }
        PublishNextCycle();
    }

    internal void OnRegisterWrite(
        ushort offset,
        long cycle,
        LightweightA500Machine machine)
    {
        if (offset < LightweightRegisters.SpritePointerFirst ||
            offset > LightweightRegisters.SpritePointerLast)
        {
            return;
        }

        var channel = (offset - LightweightRegisters.SpritePointerFirst) >> 2;
        _pointers[channel] = machine.GetSpritePointer(channel);
        if (IsDmaEnabled(machine.Dmacon))
        {
            // A late write changes only a future address input. It cannot
            // recreate a fixed slot that has already passed this line.
            ScheduleInputAfter(Math.Max(cycle, _firstFieldInputCycle - 1));
        }
        PublishNextCycle();
    }

    internal void OnFrameStart(long cycle, LightweightA500Machine machine)
    {
        _firstFieldInputCycle = cycle + FirstFieldInputOffset;
        // Prepare the next field's control reload. No sprite address input is
        // eligible before the PAL vertical-blank reset line; pointers remain
        // available for CPU/Copper rewrites throughout early vertical blank.
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            ref var state = ref _channels[channel];
            state.Active = false;
            state.Exhausted = false;
            state.HasPendingPos = false;
        }

        // Start directly at the first legal input, not at the prohibited blank
        // slots. Enable/pointer writes preserve this lower bound. Ordinary
        // slot advancement therefore needs no per-input blanking check.
        _nextInputCycle = IsDmaEnabled(machine.Dmacon)
            ? _firstFieldInputCycle
            : long.MaxValue;
        PublishNextCycle();
    }

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Sprite DMA must advance at its published physical phase.");

        if (_hasPendingOutput && _pendingOutputCycle == cycle)
        {
            CompleteOutput(cycle, machine);
        }

        if (_nextInputCycle == cycle)
        {
            _nextInputCycle = long.MaxValue;
            if (IsDmaEnabled(machine.Dmacon))
            {
                TryAcceptInput(cycle, machine);
                ScheduleInputAfter(cycle);
            }
        }

        PublishNextCycle();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool OwnsOutputSlot(long cycle)
        => _hasPendingOutput && _pendingOutputCycle == cycle ||
            LastOutputCycle == cycle;

    internal uint GetPointer(int channel)
        => (uint)channel < ChannelCount ? _pointers[channel] : 0;

    internal ushort GetPos(int channel)
        => (uint)channel < ChannelCount ? _channels[channel].Pos : (ushort)0;

    internal ushort GetCtl(int channel)
        => (uint)channel < ChannelCount ? _channels[channel].Ctl : (ushort)0;

    internal ushort GetDataA(int channel)
        => (uint)channel < ChannelCount ? _channels[channel].DataA : (ushort)0;

    internal ushort GetDataB(int channel)
        => (uint)channel < ChannelCount ? _channels[channel].DataB : (ushort)0;

    internal bool IsActive(int channel)
        => (uint)channel < ChannelCount && _channels[channel].Active;

    internal bool IsExhausted(int channel)
        => (uint)channel < ChannelCount && _channels[channel].Exhausted;

    private void TryAcceptInput(long cycle, LightweightA500Machine machine)
    {
        var outputHorizontal = machine.BeamColorClock + 1;
        var relative = outputHorizontal - FirstOutputHorizontal;
        if ((uint)relative > LastOutputHorizontal - FirstOutputHorizontal ||
            (relative & 1) != 0)
        {
            return;
        }

        var channel = relative >> 2;
        var word = (relative >> 1) & 1;
        ref var state = ref _channels[channel];
        if (state.Exhausted)
        {
            return;
        }

        var line = machine.BeamLine;
        if (word == 0 && state.Active && line >= GetVerticalStop(in state))
        {
            state.Active = false;
            state.HasPendingPos = false;
        }

        if (!TryGetPurpose(in state, line, word, out var purpose) ||
            !IsSlotAvailable(channel, word, machine))
        {
            return;
        }

        var outputCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
        if (!machine.CanSpriteOwnOutputSlot(outputCycle))
        {
            return;
        }

        _hasPendingOutput = true;
        _pendingChannel = channel;
        _pendingPurpose = purpose;
        _pendingAddress = _pointers[channel];
        _pendingOutputCycle = outputCycle;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastInputCycle = cycle;
#endif
    }

    private void CompleteOutput(long cycle, LightweightA500Machine machine)
    {
        var channel = _pendingChannel;
        var purpose = _pendingPurpose;
        var address = _pendingAddress;
        var value = machine.ReadChipWordDma(address);
        _hasPendingOutput = false;
        _pendingOutputCycle = long.MaxValue;

        ref var state = ref _channels[channel];
        switch (purpose)
        {
            case WordPurpose.ControlPos:
                state.Pos = value;
                state.HasPendingPos = true;
                break;
            case WordPurpose.ControlCtl:
                state.Ctl = value;
                state.HasPendingPos = false;
                machine.OnSpriteDmaControlOutput(
                    channel,
                    state.Pos,
                    state.Ctl,
                    cycle);
                if ((state.Pos | state.Ctl) == 0 ||
                    GetVerticalStop(in state) <= GetVerticalStart(in state))
                {
                    state.Active = false;
                    state.Exhausted = true;
                }
                else
                {
                    state.Active = true;
                }
                break;
            case WordPurpose.DataA:
                state.DataA = value;
                machine.OnSpriteDmaDataAOutput(channel, value, cycle);
                break;
            case WordPurpose.DataB:
                state.DataB = value;
                machine.OnSpriteDmaDataBOutput(channel, value, cycle);
                break;
        }

        var pointer = MaskAddress(address + 2);
        _pointers[channel] = pointer;
        machine.SetSpritePointerFromDma(channel, pointer);
        LastOutputCycle = cycle;
#if LIGHTWEIGHT_DIAGNOSTICS
        LastChannel = channel;
        LastWord = purpose is WordPurpose.ControlPos or WordPurpose.DataA ? 0 : 1;
        LastAddress = address;
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetPurpose(
        in ChannelState state,
        int line,
        int word,
        out WordPurpose purpose)
    {
        if (!state.Active)
        {
            purpose = word == 0
                ? WordPurpose.ControlPos
                : WordPurpose.ControlCtl;
            return word == 0 || state.HasPendingPos;
        }

        if (line < GetVerticalStart(in state) || line >= GetVerticalStop(in state))
        {
            purpose = default;
            return false;
        }

        purpose = word == 0 ? WordPurpose.DataA : WordPurpose.DataB;
        return true;
    }

    private static bool IsSlotAvailable(
        int channel,
        int word,
        LightweightA500Machine machine)
    {
        var bplcon0 = machine.BitplaneEffectiveBplcon0;
        var planeCount = (bplcon0 >> 12) & 7;
        if (planeCount == 0 ||
            (machine.Dmacon & (DmaconMaster | DmaconBitplane)) !=
                (DmaconMaster | DmaconBitplane))
        {
            return true;
        }

        var ddfStart = machine.GetCustomRegister(LightweightRegisters.Ddfstrt) & 0x00FC;
        var normalStart = (bplcon0 & 0x8000) == 0
            ? NormalLowResolutionDdfStart
            : NormalHighResolutionDdfStart;
        if (ddfStart >= normalStart)
        {
            return true;
        }

        // With the earliest low-resolution start the first sprite's POS slot
        // remains, while its CTL slot and every later channel are stolen.
        if ((bplcon0 & 0x8000) == 0 && ddfStart <= 0x18 && channel == 0)
        {
            return word == 0;
        }

        var usableChannels = ddfStart switch
        {
            <= 0x18 => 0,
            <= 0x1C => 1,
            >= 0x30 => 7,
            _ => Math.Clamp(((ddfStart - 0x1C) / 4) + 1, 1, 7)
        };
        return channel < usableChannels;
    }

    private void ScheduleInputAfter(long cycle)
    {
        var candidate = LightweightBusArbiter.AlignToSlot(cycle + 1);
        var horizontal = LightweightBusArbiter.GetHorizontal(candidate);
        var lineStart = candidate -
            ((long)horizontal * LightweightClock.CpuCyclesPerColorClock);
        var inputHorizontal = Math.Max(horizontal, FirstInputHorizontal);
        if ((inputHorizontal & 1) == 0)
        {
            inputHorizontal++;
        }
        if (inputHorizontal > LastInputHorizontal)
        {
            lineStart += LightweightClock.CpuCyclesPerLine;
            inputHorizontal = FirstInputHorizontal;
        }

        var next = lineStart +
            ((long)inputHorizontal * LightweightClock.CpuCyclesPerColorClock);
        _nextInputCycle = Math.Min(_nextInputCycle, next);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PublishNextCycle()
        => NextCycle = Math.Min(
            _nextInputCycle,
            _hasPendingOutput ? _pendingOutputCycle : long.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetVerticalStart(in ChannelState state)
        => ((state.Pos >> 8) & 0xFF) |
            ((state.Ctl & 0x0004) != 0 ? 0x100 : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetVerticalStop(in ChannelState state)
        => ((state.Ctl >> 8) & 0xFF) |
            ((state.Ctl & 0x0002) != 0 ? 0x100 : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDmaEnabled(ushort dmacon)
        => (dmacon & (DmaconMaster | DmaconSprite)) ==
            (DmaconMaster | DmaconSprite);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MaskAddress(uint address)
        => address & OcsChipAddressMask;

    private struct ChannelState
    {
        internal ushort Pos;
        internal ushort Ctl;
        internal ushort DataA;
        internal ushort DataB;
        internal bool HasPendingPos;
        internal bool Active;
        internal bool Exhausted;
    }
}
