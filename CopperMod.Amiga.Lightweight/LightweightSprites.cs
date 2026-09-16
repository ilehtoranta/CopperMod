using System.Runtime.CompilerServices;
using System.Numerics;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Compact OCS Denise sprite registers and pixel combiner. CPU, Copper and
/// Agnus DMA all feed the same physical register latches.
/// </summary>
internal sealed class LightweightSprites
{
    private enum PendingInputKind : byte
    {
        Register,
        DmaControl,
        DmaDataA,
        DmaDataB
    }

    private const int ChannelCount = 8;
    private readonly SpriteChannel[] _channels = new SpriteChannel[ChannelCount];
    private byte _armedMask;
    private byte _lineArmedMask;
    private byte _upcomingMask;
    private byte _shiftingMask;
    private byte _latchedAttachedMask;
    private byte _dirtyMask;
    private int _outputLine;
    private int _lastOutputX;
    private int _nextStart;
    private ushort _pendingOffset;
    private ushort _pendingValue;
    private ushort _pendingSecondValue;
    private int _pendingSprite;
    private PendingInputKind _pendingKind;
    private long _pendingInputCycle;

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long LastRegisterInputCycle { get; private set; } = long.MinValue;

    internal void Reset()
    {
        Array.Clear(_channels);
        _armedMask = 0;
        _lineArmedMask = 0;
        _upcomingMask = 0;
        _shiftingMask = 0;
        _latchedAttachedMask = 0;
        _dirtyMask = 0;
        _outputLine = -1;
        _lastOutputX = -1;
        _nextStart = int.MaxValue;
        _pendingOffset = 0;
        _pendingValue = 0;
        _pendingSecondValue = 0;
        _pendingSprite = -1;
        _pendingKind = default;
        _pendingInputCycle = long.MaxValue;
        NextCycle = long.MaxValue;
        LastRegisterInputCycle = long.MinValue;
    }

    internal bool OnRegisterWrite(ushort offset, ushort value, long cycle)
    {
        if (offset < LightweightRegisters.SpritePosFirst ||
            offset > LightweightRegisters.SpriteRegisterLast)
        {
            return false;
        }

        System.Diagnostics.Debug.Assert(
            _pendingInputCycle == long.MaxValue,
            "A prior sprite register write must enter Denise before another bus output.");
        _pendingOffset = offset;
        _pendingValue = value;
        _pendingKind = PendingInputKind.Register;
        _pendingInputCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
        NextCycle = Math.Min(NextCycle, _pendingInputCycle);
        return true;
    }

    internal void OnDmaControl(
        int sprite,
        ushort pos,
        ushort ctl,
        long outputCycle)
        => QueueDmaInput(
            PendingInputKind.DmaControl,
            sprite,
            pos,
            ctl,
            outputCycle);

    internal void OnDmaDataA(int sprite, ushort value, long outputCycle)
        => QueueDmaInput(
            PendingInputKind.DmaDataA,
            sprite,
            value,
            0,
            outputCycle);

    internal void OnDmaDataB(int sprite, ushort value, long outputCycle)
        => QueueDmaInput(
            PendingInputKind.DmaDataB,
            sprite,
            value,
            0,
            outputCycle);

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Sprites must advance at their published input phase.");

        if (_pendingInputCycle == cycle)
        {
            ApplyPendingInput(machine);
#if LIGHTWEIGHT_DIAGNOSTICS
            LastRegisterInputCycle = cycle;
#endif
            _pendingInputCycle = long.MaxValue;
        }

        NextCycle = _pendingInputCycle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsManualArmed(int sprite)
        => (uint)sprite < ChannelCount &&
            (_armedMask & (1 << sprite)) != 0;

    /// <summary>
    /// Returns an OCS palette index, or -1 when sprite output is transparent
    /// or behind the normal playfield at this pixel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ComposeColorIndex(
        int line,
        int x,
        int playfieldColorIndex,
        int playfieldPlacement)
    {
        // Keep the overwhelmingly common transparent interval out of the
        // comparator/shifter body.  This preserves one incremental pixel
        // clock without inlining the full sprite compositor into Denise's
        // already-large render loop.
        if (_outputLine == line &&
            x > _lastOutputX &&
            _dirtyMask == 0 &&
            _nextStart > x)
        {
            _lastOutputX = x;
            return _shiftingMask == 0
                ? -1
                : ComposeShifterColorIndex(
                    playfieldColorIndex,
                    playfieldPlacement);
        }

        return ComposeColorIndexSlow(
            line,
            x,
            playfieldColorIndex,
            playfieldPlacement);
    }

    /// <summary>
    /// Composes the two low-resolution pixels produced by one physical CCK.
    /// Comparator and register-input boundaries still fall back to the scalar
    /// path so each pixel observes the same state transitions as before.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ComposeColorIndexes(
        int line,
        int x,
        int firstPlayfieldColorIndex,
        int secondPlayfieldColorIndex,
        int playfieldPlacement,
        out int first,
        out int second)
    {
        if (_outputLine != line ||
            x <= _lastOutputX ||
            _dirtyMask != 0 ||
            _nextStart <= x + 1)
        {
            first = ComposeColorIndex(
                line,
                x,
                firstPlayfieldColorIndex,
                playfieldPlacement);
            second = ComposeColorIndex(
                line,
                x + 1,
                secondPlayfieldColorIndex,
                playfieldPlacement);
            return;
        }

        _lastOutputX = x + 1;
        var shifting = (uint)_shiftingMask;
        if (shifting == 0)
        {
            first = -1;
            second = -1;
            return;
        }

        if ((shifting & (shifting - 1)) == 0)
        {
            var sprite = BitOperations.TrailingZeroCount(shifting);
            ref var state = ref _channels[sprite];
            if (state.Remaining >= 2)
            {
                ComposeSingleShifterPair(
                    sprite,
                    firstPlayfieldColorIndex,
                    secondPlayfieldColorIndex,
                    playfieldPlacement,
                    out first,
                    out second);
                return;
            }
        }

        first = ComposeShifterColorIndex(
            firstPlayfieldColorIndex,
            playfieldPlacement);
        second = _shiftingMask == 0
            ? -1
            : ComposeShifterColorIndex(
                secondPlayfieldColorIndex,
                playfieldPlacement);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ComposeSingleShifterPair(
        int sprite,
        int firstPlayfieldColorIndex,
        int secondPlayfieldColorIndex,
        int playfieldPlacement,
        out int first,
        out int second)
    {
        var bit = (byte)(1 << sprite);
        ref var state = ref _channels[sprite];
        var firstPixel = ((state.ShiftA >> 15) & 1) |
            (((state.ShiftB >> 15) & 1) << 1);
        var secondPixel = ((state.ShiftA >> 14) & 1) |
            (((state.ShiftB >> 14) & 1) << 1);
        var group = sprite >> 1;
        var attached = (sprite & 1) != 0 &&
            (_latchedAttachedMask & bit) != 0;
        if (attached)
        {
            if ((_lineArmedMask & (1 << (sprite - 1))) != 0)
            {
                firstPixel <<= 2;
                secondPixel <<= 2;
            }
            else
            {
                firstPixel = 0;
                secondPixel = 0;
            }
        }

        var paletteBase = attached ? 16 : 16 + (group * 4);
        var inFrontOfFirst = firstPlayfieldColorIndex == 0 ||
            group < playfieldPlacement;
        var inFrontOfSecond = secondPlayfieldColorIndex == 0 ||
            group < playfieldPlacement;
        first = firstPixel != 0 && inFrontOfFirst
            ? paletteBase + firstPixel
            : -1;
        second = secondPixel != 0 && inFrontOfSecond
            ? paletteBase + secondPixel
            : -1;

        state.ShiftA <<= 2;
        state.ShiftB <<= 2;
        state.Remaining -= 2;
        if (state.Remaining == 0)
        {
            _shiftingMask &= (byte)~bit;
            _latchedAttachedMask &= (byte)~bit;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ComposeColorIndexSlow(
        int line,
        int x,
        int playfieldColorIndex,
        int playfieldPlacement)
    {
        if (_outputLine != line || x <= _lastOutputX)
        {
            BeginLine(line, x);
        }
        else if (_dirtyMask != 0)
        {
            ApplyDirtyRegisters(line, x);
        }

        if (_nextStart <= x)
        {
            LatchComparatorsThrough(x);
        }
        _lastOutputX = x;

        if (_shiftingMask == 0)
        {
            return -1;
        }

        return ComposeShifterColorIndex(
            playfieldColorIndex,
            playfieldPlacement);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ComposeShifterColorIndex(
        int playfieldColorIndex,
        int playfieldPlacement)
    {
        var shifting = (uint)_shiftingMask;
        if ((shifting & (shifting - 1)) == 0)
        {
            // Non-overlapping sprites are the normal case. Avoid building a
            // pair mask and probing both channels when exactly one two-bit
            // shifter owns this pixel.
            var sprite = BitOperations.TrailingZeroCount(shifting);
            var bit = (byte)(1 << sprite);
            ref var state = ref _channels[sprite];
            var pixel = ((state.ShiftA >> 15) & 1) |
                (((state.ShiftB >> 15) & 1) << 1);
            var group = sprite >> 1;
            var attached = (sprite & 1) != 0 &&
                (_latchedAttachedMask & bit) != 0;
            if (attached)
            {
                pixel = (_lineArmedMask & (1 << (sprite - 1))) != 0
                    ? pixel << 2
                    : 0;
            }

            var singleResult = pixel != 0 &&
                (playfieldColorIndex == 0 ||
                    group < playfieldPlacement)
                ? 16 + (attached ? pixel : (group * 4) + pixel)
                : -1;

            state.ShiftA <<= 1;
            state.ShiftB <<= 1;
            if (--state.Remaining == 0)
            {
                _shiftingMask &= (byte)~bit;
                _latchedAttachedMask &= (byte)~bit;
            }
            return singleResult;
        }

        var result = -1;
        var pairMask = (uint)((_shiftingMask | (_shiftingMask >> 1)) & 0x55);
        while (pairMask != 0)
        {
            var even = BitOperations.TrailingZeroCount(pairMask);
            pairMask &= pairMask - 1;
            var odd = even + 1;
            var group = even >> 1;
            var evenPixel = GetShiftPixel(even);
            var oddPixel = GetShiftPixel(odd);

            if ((_latchedAttachedMask & (1 << odd)) != 0)
            {
                // For manually armed sprites Denise needs the even partner;
                // a transparent but armed even partner still contributes zero
                // low bits to an attached color.
                if ((_lineArmedMask & (1 << even)) == 0)
                {
                    continue;
                }

                var pixel = evenPixel | (oddPixel << 2);
                if (pixel != 0)
                {
                    result = playfieldColorIndex == 0 || group < playfieldPlacement
                        ? 16 + pixel
                        : -1;
                    break;
                }

                continue;
            }

            if (evenPixel != 0)
            {
                result = playfieldColorIndex == 0 || group < playfieldPlacement
                    ? 16 + (group * 4) + evenPixel
                    : -1;
                break;
            }

            if (oddPixel != 0)
            {
                result = playfieldColorIndex == 0 || group < playfieldPlacement
                    ? 16 + (group * 4) + oddPixel
                    : -1;
                break;
            }
        }

        AdvanceShifters();
        return result;
    }

    // Resolve one lores sprite sample against two hires playfield samples,
    // then advance the sprite exactly once. The ordinary lores path is unchanged.
    internal void ComposeHiresColorIndexes(int line, int x, int firstPlayfield,
        int secondPlayfield, int placement, out int first, out int second)
    {
        if (_outputLine != line || x <= _lastOutputX) BeginLine(line, x);
        else if (_dirtyMask != 0) ApplyDirtyRegisters(line, x);
        if (_nextStart <= x) LatchComparatorsThrough(x);
        _lastOutputX = x;
        first = second = -1;
        if (_shiftingMask == 0) return;
        var pairs = (uint)((_shiftingMask | (_shiftingMask >> 1)) & 0x55);
        while (pairs != 0)
        {
            var even = BitOperations.TrailingZeroCount(pairs);
            pairs &= pairs - 1;
            var odd = even + 1;
            var group = even >> 1;
            var a = GetShiftPixel(even);
            var b = GetShiftPixel(odd);
            var attached = (_latchedAttachedMask & (1 << odd)) != 0;
            var pixel = attached
                ? ((_lineArmedMask & (1 << even)) != 0 ? a | (b << 2) : 0)
                : (a != 0 ? a : b);
            if (pixel == 0) continue;
            var color = 16 + (attached ? pixel : group * 4 + pixel);
            if (firstPlayfield == 0 || group < placement) first = color;
            if (secondPlayfield == 0 || group < placement) second = color;
            break;
        }
        AdvanceShifters();
    }

    private void QueueDmaInput(
        PendingInputKind kind,
        int sprite,
        ushort value,
        ushort secondValue,
        long outputCycle)
    {
        if ((uint)sprite >= ChannelCount)
        {
            return;
        }

        System.Diagnostics.Debug.Assert(
            _pendingInputCycle == long.MaxValue,
            "A prior sprite value must enter Denise before another bus output.");
        _pendingKind = kind;
        _pendingSprite = sprite;
        _pendingValue = value;
        _pendingSecondValue = secondValue;
        _pendingInputCycle = outputCycle + LightweightClock.CpuCyclesPerColorClock;
        NextCycle = Math.Min(NextCycle, _pendingInputCycle);
    }

    private void ApplyPendingInput(LightweightA500Machine machine)
    {
        if (_pendingKind == PendingInputKind.Register)
        {
            ApplyPendingRegister();
            return;
        }

        var sprite = _pendingSprite;
        var bit = (byte)(1 << sprite);
        ref var state = ref _channels[sprite];
        switch (_pendingKind)
        {
            case PendingInputKind.DmaControl:
                state.Pos = _pendingValue;
                state.Ctl = _pendingSecondValue;
                _armedMask &= (byte)~bit;
                machine.SetSpriteRegisterFromDma(sprite, 0, _pendingValue);
                machine.SetSpriteRegisterFromDma(sprite, 2, _pendingSecondValue);
                break;
            case PendingInputKind.DmaDataA:
                state.DataA = _pendingValue;
                _armedMask |= bit;
                machine.SetSpriteRegisterFromDma(sprite, 4, _pendingValue);
                break;
            case PendingInputKind.DmaDataB:
                state.DataB = _pendingValue;
                machine.SetSpriteRegisterFromDma(sprite, 6, _pendingValue);
                break;
        }
        _dirtyMask |= bit;
    }

    private void ApplyPendingRegister()
    {
        var relative = _pendingOffset - LightweightRegisters.SpritePosFirst;
        var sprite = relative >> 3;
        var register = relative & 7;
        ref var state = ref _channels[sprite];
        switch (register)
        {
            case 0:
                state.Pos = _pendingValue;
                break;
            case 2:
                state.Ctl = _pendingValue;
                _armedMask &= (byte)~(1 << sprite);
                break;
            case 4:
                state.DataA = _pendingValue;
                _armedMask |= (byte)(1 << sprite);
                break;
            case 6:
                state.DataB = _pendingValue;
                break;
        }
        _dirtyMask |= (byte)(1 << sprite);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetShiftPixel(int sprite)
    {
        ref var state = ref _channels[sprite];
        return (_shiftingMask & (1 << sprite)) == 0
            ? 0
            : ((state.ShiftA >> 15) & 1) |
                (((state.ShiftB >> 15) & 1) << 1);
    }

    private void BeginLine(int line, int x)
    {
        _outputLine = line;
        _lastOutputX = x - 1;
        _lineArmedMask = 0;
        _upcomingMask = 0;
        _shiftingMask = 0;
        _latchedAttachedMask = 0;
        for (var sprite = 0; sprite < ChannelCount; sprite++)
        {
            var bit = (byte)(1 << sprite);
            if ((_armedMask & bit) == 0 ||
                !IsVerticallyActive(in _channels[sprite], line))
            {
                continue;
            }

            _lineArmedMask |= bit;
            var start = GetHorizontalStart(in _channels[sprite]);
            if (start >= x)
            {
                _upcomingMask |= bit;
            }
            else if (x < start + 16)
            {
                LatchSprite(sprite, x - start);
            }
        }
        _dirtyMask = 0;
        RefreshNextStart();
    }

    private void ApplyDirtyRegisters(int line, int x)
    {
        var dirty = _dirtyMask;
        _dirtyMask = 0;
        for (var sprite = 0; sprite < ChannelCount; sprite++)
        {
            var bit = (byte)(1 << sprite);
            if ((dirty & bit) == 0)
            {
                continue;
            }

            var wasUpcoming = (_upcomingMask & bit) != 0;
            _upcomingMask &= (byte)~bit;
            if ((_shiftingMask & bit) != 0)
            {
                // Data and control were latched at the comparator. A later
                // write affects a future line, never the active shifter.
                continue;
            }

            var start = GetHorizontalStart(in _channels[sprite]);
            if ((_armedMask & bit) != 0 &&
                IsVerticallyActive(in _channels[sprite], line) &&
                start >= x)
            {
                _lineArmedMask |= bit;
                _upcomingMask |= bit;
            }
            else if (wasUpcoming)
            {
                _lineArmedMask &= (byte)~bit;
            }
        }
        RefreshNextStart();
    }

    private void LatchComparatorsThrough(int x)
    {
        var upcoming = _upcomingMask;
        for (var sprite = 0; sprite < ChannelCount; sprite++)
        {
            var bit = (byte)(1 << sprite);
            if ((upcoming & bit) == 0)
            {
                continue;
            }

            var start = GetHorizontalStart(in _channels[sprite]);
            if (start > x)
            {
                continue;
            }

            _upcomingMask &= (byte)~bit;
            if (x < start + 16)
            {
                LatchSprite(sprite, x - start);
            }
        }
        RefreshNextStart();
    }

    private void LatchSprite(int sprite, int consumedPixels)
    {
        ref var state = ref _channels[sprite];
        state.ShiftA = (ushort)(state.DataA << consumedPixels);
        state.ShiftB = (ushort)(state.DataB << consumedPixels);
        state.Remaining = (byte)(16 - consumedPixels);
        var bit = (byte)(1 << sprite);
        _shiftingMask |= bit;
        if ((state.Ctl & 0x0080) != 0)
        {
            _latchedAttachedMask |= bit;
        }
        else
        {
            _latchedAttachedMask &= (byte)~bit;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceShifters()
    {
        var active = (uint)_shiftingMask;
        while (active != 0)
        {
            var sprite = BitOperations.TrailingZeroCount(active);
            active &= active - 1;
            var bit = (byte)(1 << sprite);
            ref var state = ref _channels[sprite];
            state.ShiftA <<= 1;
            state.ShiftB <<= 1;
            if (--state.Remaining == 0)
            {
                _shiftingMask &= (byte)~bit;
                _latchedAttachedMask &= (byte)~bit;
            }
        }
    }

    private void RefreshNextStart()
    {
        var next = int.MaxValue;
        for (var sprite = 0; sprite < ChannelCount; sprite++)
        {
            if ((_upcomingMask & (1 << sprite)) != 0)
            {
                next = Math.Min(next, GetHorizontalStart(in _channels[sprite]));
            }
        }
        _nextStart = next;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsVerticallyActive(in SpriteChannel state, int line)
    {
        var start = ((state.Pos >> 8) & 0xFF) |
            ((state.Ctl & 0x0004) != 0 ? 0x100 : 0);
        var stop = ((state.Ctl >> 8) & 0xFF) |
            ((state.Ctl & 0x0002) != 0 ? 0x100 : 0);
        return stop > start && line >= start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHorizontalStart(in SpriteChannel state)
        // SPRxPOS contains H8-H1 and SPRxCTL contains H0. In the uncropped
        // raster the comparator's first visible low-resolution pixel is +1.
        => (((state.Pos & 0xFF) << 1) | (state.Ctl & 1)) + 1;

    private struct SpriteChannel
    {
        internal ushort Pos;
        internal ushort Ctl;
        internal ushort DataA;
        internal ushort DataB;
        internal ushort ShiftA;
        internal ushort ShiftB;
        internal byte Remaining;
    }
}
