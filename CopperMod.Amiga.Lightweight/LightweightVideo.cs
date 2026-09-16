using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Direct OCS Denise output for one uncropped PAL field. The completed and
/// rendering arrays are reused and exchanged at the field boundary.
/// </summary>
internal sealed class LightweightVideo
{
    internal const int PalRasterWidth =
        LightweightClock.CpuCyclesPerLine / LightweightClock.CpuCyclesPerColorClock * 2;
    internal const int PalRasterHeight = LightweightClock.PalLongFieldLines;
    private const int VisibleRasterXStart = 98;
    private const int VisibleRasterYStart = 26;
    private const int VisibleRasterYStop = 311;
    private const int MaxPlanes = 6;
    private const ulong FourShifterLaneMask = 0xFFFE_FFFE_FFFE_FFFEUL;
    private const uint TwoShifterLaneMask = 0xFFFE_FFFEu;
    private const ulong FourShifterPairLaneMask = 0xFFFC_FFFC_FFFC_FFFCUL;
    private const uint TwoShifterPairLaneMask = 0xFFFC_FFFCu;
    private const ulong FourShifterFirstPixelMask = 0x8000_8000_8000_8000UL;
    private const ulong FourShifterSecondPixelMask = 0x4000_4000_4000_4000UL;
    private const uint TwoShifterFirstPixelMask = 0x8000_8000u;
    private const uint TwoShifterSecondPixelMask = 0x4000_4000u;

    private readonly int[] _bufferA;
    private readonly int[] _bufferB;
    private readonly int _outputScale;
    private readonly ushort[] _dataLatches = new ushort[MaxPlanes];
    private readonly ushort[] _intermediate = new ushort[MaxPlanes];
    private ulong _shifters03;
    private uint _shifters45;
    private readonly int[] _palette = new int[64];
    private int[] _completed;
    private int[] _rendering;
    private ushort _effectiveBplcon0;
    private ushort _effectiveBplcon1;
    private int _hamColor;
    private int _effectiveSpritePlayfieldPlacement;
    private ushort _effectiveDiwStart;
    private ushort _effectiveDiwStop;
    private int _effectivePlaneCount;
    private int _effectivePlaneMask;
    private int _horizontalWindowStart;
    private int _horizontalWindowStop;
    private int _verticalWindowStart;
    private int _verticalWindowStop;
    private ushort _pendingControlValue;
    private ushort _pendingControlOffset;
    private long _pendingControlCycle;
    private ushort _pendingDataValue;
    private int _pendingDataPlane;
    private long _pendingDataCycle;
    private int _pendingReloadParity;
    private int _spriteOutputEnableLine;
    private int _renderLine;
    private int _renderX;
    private int _renderIndex;
    private bool _renderLineVerticallyBlanked;
    private bool _renderLineVerticallyInWindow;
    private bool _renderLineSpriteOutputEnabled;
    private bool _renderCursorInitialized;
    private bool _active;

    internal LightweightVideo(LightweightA500Configuration configuration)
    {
        if (configuration.FramebufferWidth != PalRasterWidth && configuration.FramebufferWidth != 2 * PalRasterWidth ||
            configuration.FramebufferHeight != PalRasterHeight)
        {
            throw new ArgumentException(
                $"OCS output requires an uncropped 454- or 908-pixel-wide PAL raster of height {PalRasterHeight}.",
                nameof(configuration));
        }
        _outputScale = configuration.FramebufferWidth / PalRasterWidth;
        _bufferA = new int[configuration.FramebufferWidth * PalRasterHeight];
        _bufferB = new int[configuration.FramebufferWidth * PalRasterHeight];
        _completed = _bufferA;
        _rendering = _bufferB;
        Reset();
    }

    internal ReadOnlyMemory<int> Framebuffer => _completed;
    internal long NextCycle { get; private set; }
    internal ushort EffectiveBplcon0 => _effectiveBplcon0;
    internal bool Active => _active;
    internal string? UnsupportedActiveFeature { get; private set; }

    internal void Reset()
    {
        Array.Clear(_bufferA);
        Array.Clear(_bufferB);
        Array.Clear(_dataLatches);
        Array.Clear(_intermediate);
        _shifters03 = 0;
        _shifters45 = 0;
        Array.Fill(_palette, unchecked((int)0xFF000000));
        _completed = _bufferA;
        _rendering = _bufferB;
        _effectiveBplcon0 = 0;
        _effectiveBplcon1 = 0;
        _hamColor = unchecked((int)0xFF000000);
        _effectiveSpritePlayfieldPlacement = 0;
        _effectiveDiwStart = 0;
        _effectiveDiwStop = 0;
        _effectivePlaneCount = 0;
        _effectivePlaneMask = 0;
        UpdateWindowBounds();
        _pendingControlValue = 0;
        _pendingControlOffset = 0;
        _pendingControlCycle = long.MaxValue;
        _pendingDataValue = 0;
        _pendingDataPlane = -1;
        _pendingDataCycle = long.MaxValue;
        _pendingReloadParity = 0;
        _spriteOutputEnableLine = -1;
        _renderLine = 0;
        _renderX = 0;
        _renderIndex = 0;
        _renderLineVerticallyBlanked = true;
        _renderLineVerticallyInWindow = false;
        _renderLineSpriteOutputEnabled = false;
        _renderCursorInitialized = false;
        _active = false;
        NextCycle = long.MaxValue;
        UnsupportedActiveFeature = null;
    }

    internal void OnRegisterWrite(ushort offset, ushort value, long cycle)
    {
        if (offset is >= LightweightRegisters.ColorFirst and <= LightweightRegisters.ColorLast)
        {
            UpdatePalette((offset - LightweightRegisters.ColorFirst) >> 1, value);
        }
        if (offset is LightweightRegisters.Bplcon0 or LightweightRegisters.Bplcon1 or
            LightweightRegisters.Bplcon2 or
            LightweightRegisters.Diwstrt or LightweightRegisters.Diwstop)
        {
            _pendingControlOffset = offset;
            _pendingControlValue = value;
            _pendingControlCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
        }
        else if (offset >= LightweightRegisters.BpldatFirst &&
            offset <= LightweightRegisters.BpldatLast &&
            ((offset - LightweightRegisters.BpldatFirst) & 1) == 0)
        {
            AcceptBitplaneWord(
                (offset - LightweightRegisters.BpldatFirst) >> 1,
                value,
                cycle);
        }

        if (offset == LightweightRegisters.DmaconWrite ||
            offset is >= LightweightRegisters.ColorFirst and <= LightweightRegisters.ColorLast ||
            offset is LightweightRegisters.Bplcon0 or LightweightRegisters.Bplcon1 or
                LightweightRegisters.Bplcon2 or
                LightweightRegisters.Diwstrt or LightweightRegisters.Diwstop ||
            offset is >= LightweightRegisters.BpldatFirst and <= LightweightRegisters.BpldatLast)
        {
            ActivateAfter(cycle);
        }
    }

    internal void AcceptBitplaneWord(int plane, ushort value, long outputCycle)
    {
        if ((uint)plane >= MaxPlanes)
        {
            return;
        }
        _pendingDataPlane = plane;
        _pendingDataValue = value;
        _pendingDataCycle = outputCycle + LightweightClock.CpuCyclesPerColorClock;
        ActivateAfter(outputCycle);
    }

    internal void ActivateForSprite(long cycle)
        => ActivateAfter(cycle);

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(
            cycle == NextCycle,
            "Video must advance at its published CCK.");
        RenderPriorPhysicalCck(machine);
        ApplyPendingInputs(cycle, machine);
        NextCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
    }

    internal void CompleteFrame(
        long boundaryCycle,
        int completedFieldLines,
        LightweightA500Machine machine)
    {
        if (_active)
        {
            var finalRow = (completedFieldLines - 1) * PalRasterWidth;
            _renderLine = completedFieldLines - 1;
            UpdateRenderLineState();
            RenderPixelPair(
                completedFieldLines - 1,
                PalRasterWidth - 4,
                finalRow + PalRasterWidth - 4,
                machine);
            ApplyPendingInputs(boundaryCycle, machine);
            RenderPixelPair(
                completedFieldLines - 1,
                PalRasterWidth - 2,
                finalRow + PalRasterWidth - 2,
                machine);
        }

        (_completed, _rendering) = (_rendering, _completed);
        if (completedFieldLines < PalRasterHeight)
        {
            Array.Clear(
                _completed,
                completedFieldLines * PalRasterWidth * _outputScale,
                (PalRasterHeight - completedFieldLines) * PalRasterWidth * _outputScale);
        }
        _spriteOutputEnableLine = -1;
        _renderCursorInitialized = false;
        NextCycle = _active
            ? boundaryCycle + LightweightClock.CpuCyclesPerColorClock
            : long.MaxValue;
    }

    private void ApplyPendingInputs(
        long cycle,
        LightweightA500Machine machine)
    {
        var refreshRenderLine = false;
        if (_pendingControlCycle <= cycle)
        {
            switch (_pendingControlOffset)
            {
                case LightweightRegisters.Bplcon0:
                    _effectiveBplcon0 = _pendingControlValue;
                    _effectivePlaneCount = GetDecodePlaneCount(_effectiveBplcon0);
                    _effectivePlaneMask = (1 << _effectivePlaneCount) - 1;
                    UpdateUnsupportedMode();
                    refreshRenderLine = true;
                    break;
                case LightweightRegisters.Bplcon1:
                    _effectiveBplcon1 = _pendingControlValue;
                    break;
                case LightweightRegisters.Bplcon2:
                    _effectiveSpritePlayfieldPlacement = Math.Min(
                        (_pendingControlValue >> 3) & 7,
                        4);
                    break;
                case LightweightRegisters.Diwstrt:
                    _effectiveDiwStart = _pendingControlValue;
                    UpdateWindowBounds();
                    refreshRenderLine = true;
                    break;
                case LightweightRegisters.Diwstop:
                    _effectiveDiwStop = _pendingControlValue;
                    UpdateWindowBounds();
                    refreshRenderLine = true;
                    break;
            }
            _pendingControlCycle = long.MaxValue;
        }

        if (_pendingDataCycle <= cycle)
        {
            var plane = _pendingDataPlane;
            _dataLatches[plane] = _pendingDataValue;
            if (plane == 0)
            {
                _intermediate[0] = _dataLatches[0];
                _intermediate[1] = _dataLatches[1];
                _intermediate[2] = _dataLatches[2];
                _intermediate[3] = _dataLatches[3];
                _intermediate[4] = _dataLatches[4];
                _intermediate[5] = _dataLatches[5];
                _pendingReloadParity = 3;
                _spriteOutputEnableLine = machine.BeamLine;
                refreshRenderLine = true;
            }
            _pendingDataCycle = long.MaxValue;
        }

        if (refreshRenderLine && _renderCursorInitialized)
        {
            UpdateRenderLineState();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderPriorPhysicalCck(LightweightA500Machine machine)
    {
        if (!_renderCursorInitialized)
        {
            var currentHorizontal = machine.BeamColorClock;
            var previousHorizontal = currentHorizontal == 0
                ? (LightweightClock.CpuCyclesPerLine /
                    LightweightClock.CpuCyclesPerColorClock) - 1
                : currentHorizontal - 1;
            _renderLine = currentHorizontal <= 1
                ? machine.BeamLine - 1
                : machine.BeamLine;
            _renderX = previousHorizontal == 0
                ? PalRasterWidth - 2
                : (previousHorizontal - 1) * 2;
            _renderIndex = (_renderLine * PalRasterWidth) + _renderX;
            _renderCursorInitialized = true;
            UpdateRenderLineState();
        }

        if ((uint)_renderLine < PalRasterHeight)
        {
            RenderPixelPair(
                _renderLine,
                _renderX,
                _renderIndex,
                machine);
        }

        _renderX += 2;
        _renderIndex += 2;
        if (_renderX == PalRasterWidth)
        {
            _renderX = 0;
            _renderLine++;
            UpdateRenderLineState();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderPixelPair(
        int line, int x, int index, LightweightA500Machine machine)
    {
        if (_outputScale == 1)
        {
            RenderLowResPair(line, x, index, machine);
            return;
        }
        index *= 2;
        if ((_effectiveBplcon0 & 0x8000) != 0)
        {
            RenderHiresCck(line, x, index, machine);
            return;
        }
        RenderLowResPair(line, x, index, machine, wide: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RenderHiresCck(int line, int x, int index, LightweightA500Machine machine)
    {
        // No device input occurs between these samples within one CCK.
        // Shift even in blanking/borders, retaining pending reload phases.
        var firstPair = ShiftPlayfieldPair(x * 2);
        var secondPair = ShiftPlayfieldPair(x * 2 + 2);
        if (_renderLineVerticallyBlanked || x + 1 < VisibleRasterXStart)
        {
            FillHiresCck(index, unchecked((int)0xFF000000));
            return;
        }
        if (x >= VisibleRasterXStart && (!_renderLineVerticallyInWindow ||
            x + 1 < _horizontalWindowStart || x >= _horizontalWindowStop))
        {
            FillHiresCck(index, _palette[0]);
            return;
        }
        if (x >= VisibleRasterXStart && x >= _horizontalWindowStart &&
            x + 1 < _horizontalWindowStop)
        {
            RenderHiresVisiblePair(line, x, index, firstPair, machine);
            RenderHiresVisiblePair(line, x + 1, index + 2, secondPair, machine);
            return;
        }
        RenderHiresBoundaryCck(line, x, index, firstPair, secondPair, machine);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillHiresCck(int index, int color)
    {
        ref var destination = ref _rendering[index];
        destination = color;
        Unsafe.Add(ref destination, 1) = color;
        Unsafe.Add(ref destination, 2) = color;
        Unsafe.Add(ref destination, 3) = color;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderHiresVisiblePair(int line, int x, int index, int pair,
        LightweightA500Machine machine)
    {
        var first = pair & 0x3F;
        var second = (pair >> 8) & 0x3F;
        machine.ComposeHiresSpriteColorIndexes(line, x, first, second,
            _effectiveSpritePlayfieldPlacement, out var sprite1, out var sprite2);
        if (_renderLineSpriteOutputEnabled)
        {
            if (sprite1 >= 0) first = sprite1;
            if (sprite2 >= 0) second = sprite2;
        }
        _rendering[index] = _palette[first];
        _rendering[index + 1] = _palette[second];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RenderHiresBoundaryCck(int line, int x, int index, int firstPair,
        int secondPair, LightweightA500Machine machine)
    {
        for (var pixel = 0; pixel < 2; pixel++)
        {
            var lowX = x + pixel;
            var destination = index + pixel * 2;
            if (lowX >= VisibleRasterXStart && _renderLineVerticallyInWindow &&
                lowX >= _horizontalWindowStart && lowX < _horizontalWindowStop)
                RenderHiresVisiblePair(line, lowX, destination,
                    pixel == 0 ? firstPair : secondPair, machine);
            else
            {
                var color = lowX < VisibleRasterXStart ? unchecked((int)0xFF000000) : _palette[0];
                _rendering[destination] = color;
                _rendering[destination + 1] = color;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderLowResPair(
        int line,
        int x,
        int index,
        LightweightA500Machine machine,
        bool wide = false)
    {
        var playfieldPair = ShiftPlayfieldPair(x);
        if ((_effectiveBplcon0 & 0x0800) != 0)
        {
            RenderHamPair(line, x, index, playfieldPair, machine, wide);
            return;
        }
        var firstPlayfield = playfieldPair & 0x3F;
        var secondPlayfield = (playfieldPair >> 8) & 0x3F;

        if (_renderLineVerticallyBlanked || x + 1 < VisibleRasterXStart)
        {
            WriteLowResPair(index, unchecked((int)0xFF000000), unchecked((int)0xFF000000), wide);
            return;
        }

        if (!_renderLineVerticallyInWindow ||
            x + 1 < _horizontalWindowStart ||
            x >= _horizontalWindowStop)
        {
            var border = _palette[0];
            WriteLowResPair(index, border, border, wide);
            return;
        }

        if (x >= _horizontalWindowStart && x + 1 < _horizontalWindowStop)
        {
            machine.ComposeSpriteColorIndexes(
                line,
                x,
                firstPlayfield,
                secondPlayfield,
                _effectiveSpritePlayfieldPlacement,
                out var firstSprite,
                out var secondSprite);
            if (_renderLineSpriteOutputEnabled && firstSprite >= 0)
            {
                firstPlayfield = firstSprite;
            }
            if (_renderLineSpriteOutputEnabled && secondSprite >= 0)
            {
                secondPlayfield = secondSprite;
            }

            WriteLowResPair(index, _palette[firstPlayfield], _palette[secondPlayfield], wide);
            return;
        }

        RenderWindowBoundaryPair(
            line,
            x,
            index,
            firstPlayfield,
            secondPlayfield,
            machine,
            wide);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RenderHamPair(int line, int x, int index, int pair,
        LightweightA500Machine machine, bool wide)
    {
        // One held playfield color, updated in physical pixel order. Sprite
        // colors overlay the result but must never feed the hold register.
        var first = RenderHamPixel(line, x, pair & 63, machine);
        var second = RenderHamPixel(line, x + 1, (pair >> 8) & 63, machine);
        WriteLowResPair(index, first, second, wide);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int RenderHamPixel(int line, int x, int code, LightweightA500Machine machine)
    {
        if (_renderLineVerticallyBlanked || x < VisibleRasterXStart)
        {
            _hamColor = _palette[0];
            return unchecked((int)0xFF000000);
        }
        if (!_renderLineVerticallyInWindow ||
            x < _horizontalWindowStart || x >= _horizontalWindowStop)
            return _hamColor = _palette[0];

        // HRM: plane 6/5 = 00 palette, 01 blue, 10 red, 11 green.
        var component = (code & 15) * 17;
        var color = (code >> 4) switch
        {
            0 => _palette[code],
            1 => (_hamColor & ~0xFF) | component,
            2 => (_hamColor & ~0xFF0000) | (component << 16),
            _ => (_hamColor & ~0xFF00) | (component << 8)
        };
        _hamColor = color;
        var sprite = machine.ComposeSpriteColorIndex(line, x, code,
            _effectiveSpritePlayfieldPlacement);
        return _renderLineSpriteOutputEnabled && sprite >= 0 ? _palette[sprite] : color;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLowResPair(int index, int first, int second, bool wide)
    {
        // The caller reserves one physical CCK: two narrow or four wide pixels.
        // Both call sites pass a constant width, so the hot paths specialize.
        ref var destination = ref _rendering[index];
        destination = first;
        Unsafe.Add(ref destination, wide ? 2 : 1) = second;
        if (wide)
        {
            Unsafe.Add(ref destination, 1) = first;
            Unsafe.Add(ref destination, 3) = second;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RenderWindowBoundaryPair(
        int line,
        int x,
        int index,
        int firstPlayfield,
        int secondPlayfield,
        LightweightA500Machine machine,
        bool wide)
    {
        var secondX = x + 1;
        var firstBlanked = x < VisibleRasterXStart;
        var secondBlanked = secondX < VisibleRasterXStart;
        var firstInWindow = _renderLineVerticallyInWindow &&
            x >= _horizontalWindowStart && x < _horizontalWindowStop;
        var secondInWindow = _renderLineVerticallyInWindow &&
            secondX >= _horizontalWindowStart && secondX < _horizontalWindowStop;
        var firstOutput = firstInWindow ? firstPlayfield : 0;
        var secondOutput = secondInWindow ? secondPlayfield : 0;
        var firstSpriteEligible = !firstBlanked && firstInWindow;
        var secondSpriteEligible = !secondBlanked && secondInWindow;
        var spriteOutputEnabled = _renderLineSpriteOutputEnabled;

        if (firstSpriteEligible && secondSpriteEligible)
        {
            machine.ComposeSpriteColorIndexes(
                line,
                x,
                firstPlayfield,
                secondPlayfield,
                _effectiveSpritePlayfieldPlacement,
                out var firstSprite,
                out var secondSprite);
            if (spriteOutputEnabled && firstSprite >= 0)
            {
                firstOutput = firstSprite;
            }
            if (spriteOutputEnabled && secondSprite >= 0)
            {
                secondOutput = secondSprite;
            }
        }
        else
        {
            if (firstSpriteEligible)
            {
                var sprite = machine.ComposeSpriteColorIndex(
                    line,
                    x,
                    firstPlayfield,
                    _effectiveSpritePlayfieldPlacement);
                if (spriteOutputEnabled && sprite >= 0)
                {
                    firstOutput = sprite;
                }
            }
            if (secondSpriteEligible)
            {
                var sprite = machine.ComposeSpriteColorIndex(
                    line,
                    secondX,
                    secondPlayfield,
                    _effectiveSpritePlayfieldPlacement);
                if (spriteOutputEnabled && sprite >= 0)
                {
                    secondOutput = sprite;
                }
            }
        }

        WriteLowResPair(index,
            firstBlanked ? unchecked((int)0xFF000000) : _palette[firstOutput],
            secondBlanked ? unchecked((int)0xFF000000) : _palette[secondOutput], wide);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateRenderLineState()
    {
        var line = _renderLine;
        _renderLineVerticallyBlanked =
            line < VisibleRasterYStart || line >= VisibleRasterYStop;
        _renderLineVerticallyInWindow =
            line >= _verticalWindowStart && line < _verticalWindowStop;
        _renderLineSpriteOutputEnabled = IsSpriteOutputEnabled(line);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ShiftPlayfieldPair(int x)
    {
        if (_pendingReloadParity != 0)
        {
            return ShiftPlayfieldPairWithReload(x);
        }

        var shifters03 = _shifters03;
        var shifters45 = _shifters45;
        var first = ExtractPlayfieldPixel(
            shifters03,
            shifters45,
            FourShifterFirstPixelMask,
            TwoShifterFirstPixelMask);
        var second = ExtractPlayfieldPixel(
            shifters03,
            shifters45,
            FourShifterSecondPixelMask,
            TwoShifterSecondPixelMask);
        _shifters03 = (shifters03 << 2) & FourShifterPairLaneMask;
        _shifters45 = (shifters45 << 2) & TwoShifterPairLaneMask;
        var planeMask = _effectivePlaneMask;
        return (first & planeMask) | ((second & planeMask) << 8);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ShiftPlayfieldPairWithReload(int x)
    {
        var first = ShiftPlayfieldPixel(x);
        var second = ShiftPlayfieldPixel(x + 1);
        return first | (second << 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ShiftPlayfieldPixel(int x)
    {
        var shifters03 = _shifters03;
        var shifters45 = _shifters45;
        var colorIndex = ExtractPlayfieldPixel(
            shifters03,
            shifters45,
            FourShifterFirstPixelMask,
            TwoShifterFirstPixelMask);
        _shifters03 = (shifters03 << 1) & FourShifterLaneMask;
        _shifters45 = (shifters45 << 1) & TwoShifterLaneMask;
        colorIndex &= _effectivePlaneMask;

        var pendingReload = _pendingReloadParity;
        if (pendingReload != 0)
        {
            for (var parity = 0; parity < 2; parity++)
            {
                var flag = 1 << parity;
                var scroll = (_effectiveBplcon1 >> (parity * 4)) & 15;
                if ((_effectiveBplcon0 & 0x8000) != 0)
                    scroll = ((scroll & 7) << 1) | 1;
                if ((pendingReload & flag) == 0 || (x & 15) != scroll)
                {
                    continue;
                }
                if (parity == 0)
                {
                    if (_effectivePlaneCount > 0)
                    {
                        _shifters03 = (_shifters03 & 0xFFFF_FFFF_FFFF_0000UL) |
                            _intermediate[0];
                    }
                    if (_effectivePlaneCount > 2)
                    {
                        _shifters03 = (_shifters03 & 0xFFFF_0000_FFFF_FFFFUL) |
                            ((ulong)_intermediate[2] << 32);
                    }
                    if (_effectivePlaneCount > 4)
                    {
                        _shifters45 = (_shifters45 & 0xFFFF_0000u) |
                            _intermediate[4];
                    }
                }
                else
                {
                    if (_effectivePlaneCount > 1)
                    {
                        _shifters03 = (_shifters03 & 0xFFFF_FFFF_0000_FFFFUL) |
                            ((ulong)_intermediate[1] << 16);
                    }
                    if (_effectivePlaneCount > 3)
                    {
                        _shifters03 = (_shifters03 & 0x0000_FFFF_FFFF_FFFFUL) |
                            ((ulong)_intermediate[3] << 48);
                    }
                    if (_effectivePlaneCount > 5)
                    {
                        _shifters45 = (_shifters45 & 0x0000_FFFFu) |
                            ((uint)_intermediate[5] << 16);
                    }
                }
                _pendingReloadParity &= ~flag;
            }
        }

        return colorIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExtractPlayfieldPixel(
        ulong shifters03,
        uint shifters45,
        ulong mask03,
        uint mask45)
    {
        if (Bmi2.X64.IsSupported)
        {
            return (int)Bmi2.X64.ParallelBitExtract(shifters03, mask03) |
                ((int)Bmi2.ParallelBitExtract(shifters45, mask45) << 4);
        }

        return mask03 == FourShifterFirstPixelMask
            ? (int)((shifters03 >> 15) & 0x01) |
                (int)((shifters03 >> 30) & 0x02) |
                (int)((shifters03 >> 45) & 0x04) |
                (int)((shifters03 >> 60) & 0x08) |
                (int)((shifters45 >> 11) & 0x10) |
                (int)((shifters45 >> 26) & 0x20)
            : (int)((shifters03 >> 14) & 0x01) |
                (int)((shifters03 >> 29) & 0x02) |
                (int)((shifters03 >> 44) & 0x04) |
                (int)((shifters03 >> 59) & 0x08) |
                (int)((shifters45 >> 10) & 0x10) |
                (int)((shifters45 >> 25) & 0x20);
    }

    private void UpdateWindowBounds()
    {
        _horizontalWindowStart = _effectiveDiwStart & 0x00FF;
        _horizontalWindowStop = (_effectiveDiwStop & 0x00FF) + 0x100;
        _verticalWindowStart = (_effectiveDiwStart >> 8) & 0x00FF;
        _verticalWindowStop = (_effectiveDiwStop >> 8) & 0x00FF;
        if (_verticalWindowStop < 0x80)
        {
            _verticalWindowStop += 0x100;
        }
        if (_verticalWindowStop <= _verticalWindowStart)
        {
            _verticalWindowStop += 0x100;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsSpriteOutputEnabled(int line)
    {
        var requestedPlanes = (_effectiveBplcon0 >> 12) & 7;
        return requestedPlanes == 0
            ? (_effectiveBplcon0 & 0x0200) == 0
            : _spriteOutputEnableLine == line;
    }

    private void UpdatePalette(int colorIndex, ushort encoded)
    {
        var r = ((encoded >> 8) & 0x0F) * 17;
        var g = ((encoded >> 4) & 0x0F) * 17;
        var b = (encoded & 0x0F) * 17;
        _palette[colorIndex] = unchecked((int)(0xFF000000u |
            ((uint)r << 16) | ((uint)g << 8) | (uint)b));
        _palette[colorIndex + 32] = unchecked((int)(0xFF000000u |
            ((uint)(r >> 1) << 16) | ((uint)(g >> 1) << 8) | (uint)(b >> 1)));
    }

    private void UpdateUnsupportedMode()
    {
        var hires = (_effectiveBplcon0 & 0x8000) != 0;
        var feature = hires && _outputScale != 2
            ? "OCS hires output"
            : hires && ((_effectiveBplcon0 >> 12) & 7) > 4
                ? "OCS hires BPU above four"
            : (_effectiveBplcon0 & 0x0800) != 0 &&
                (hires || _effectivePlaneCount is < 5 or > 6)
                ? "OCS HAM outside five/six-plane lores"
                : (_effectiveBplcon0 & 0x0400) != 0
                    ? "OCS dual-playfield output"
                    : ((_effectiveBplcon0 >> 12) & 7) == 7
                        ? "OCS BPU=7 output"
                        : null;
        if (feature is not null)
        {
            UnsupportedActiveFeature = feature;
        }
    }

    private void ActivateAfter(long cycle)
    {
        if (!_active)
        {
            Array.Clear(_rendering);
            _active = true;
        }
        var next = LightweightBusArbiter.AlignToSlot(cycle + 1);
        NextCycle = Math.Min(NextCycle, next);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDecodePlaneCount(ushort bplcon0)
    {
        var count = (bplcon0 >> 12) & 7;
        return Math.Min(count, (bplcon0 & 0x8000) != 0 ? 4 : MaxPlanes);
    }
}
