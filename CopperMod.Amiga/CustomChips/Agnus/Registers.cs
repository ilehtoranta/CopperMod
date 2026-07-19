/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Agnus;

[Flags]
internal enum AgnusRegisterWriteEffects : byte
{
    None = 0,
    Stored = 1 << 0,
    BeamState = 1 << 1,
    TimingChanged = 1 << 2,
    DisplayHistory = 1 << 3,
    CopperJump1 = 1 << 4,
    CopperJump2 = 1 << 5,
    RasterEventsChanged = 1 << 6
}

internal readonly record struct AgnusRegisterWriteResult(AgnusRegisterWriteEffects Effects)
{
    public bool Handled => Effects != AgnusRegisterWriteEffects.None;
    public bool BeamStateChanged => (Effects & AgnusRegisterWriteEffects.BeamState) != 0;
    public bool TimingChanged => (Effects & AgnusRegisterWriteEffects.TimingChanged) != 0;
    public bool RasterEventsChanged => (Effects & AgnusRegisterWriteEffects.RasterEventsChanged) != 0;
}

internal readonly record struct AgnusDisplayRegisterWrite(long Cycle, ushort Offset, ushort Value)
{
    public AgnusDisplayRegisterWrite Normalize()
        => new(Cycle, (ushort)(Offset & 0x01FE), Value);
}

internal static class AgnusCopperRegisterAccess
{
    internal const ushort CopperDanger = 0x0002;

    public static bool CanWrite(ushort offset, ushort copcon)
    {
        offset &= 0x01FE;
        return offset >= 0x010 &&
            (offset >= 0x020 || (copcon & CopperDanger) != 0);
    }

    public static bool StopsCopper(ushort offset, ushort copcon)
    {
        offset &= 0x01FE;
        return offset < 0x010 ||
            (offset < 0x020 && (copcon & CopperDanger) == 0);
    }
}

internal sealed class AgnusRegisterBank
{
    internal const ushort Vposw = 0x02A;
    internal const ushort Copcon = 0x02E;
    internal const ushort Cop1lch = 0x080;
    internal const ushort Cop1lcl = 0x082;
    internal const ushort Cop2lch = 0x084;
    internal const ushort Cop2lcl = 0x086;
    internal const ushort Copjmp1 = 0x088;
    internal const ushort Copjmp2 = 0x08A;
    internal const ushort Diwstrt = 0x08E;
    internal const ushort Diwstop = 0x090;
    internal const ushort Ddfstrt = 0x092;
    internal const ushort Ddfstop = 0x094;
    internal const ushort Bpl1mod = 0x108;
    internal const ushort Bpl2mod = 0x10A;
    internal const ushort BplPointerFirst = 0x0E0;
    internal const ushort BplPointerLast = 0x0F6;
    internal const ushort SpritePointerFirst = 0x120;
    internal const ushort SpritePointerLast = 0x13E;

    internal const ushort Htotal = 0x1C0;
    internal const ushort Hsstop = 0x1C2;
    internal const ushort Hbstrt = 0x1C4;
    internal const ushort Hbstop = 0x1C6;
    internal const ushort Vtotal = 0x1C8;
    internal const ushort Vsstop = 0x1CA;
    internal const ushort Vbstrt = 0x1CC;
    internal const ushort Vbstop = 0x1CE;
    internal const ushort Sprhstrt = 0x1D0;
    internal const ushort Sprhstop = 0x1D2;
    internal const ushort Bplhstrt = 0x1D4;
    internal const ushort Bplhstop = 0x1D6;
    internal const ushort Hhposw = 0x1D8;
    internal const ushort Hhposr = 0x1DA;
    internal const ushort Beamcon0 = 0x1DC;
    internal const ushort Hsstrt = 0x1DE;
    internal const ushort Vsstrt = 0x1E0;
    internal const ushort Hcenter = 0x1E2;
    internal const ushort Diwhigh = 0x1E4;

    internal const ushort DefaultDiwStart = 0x2C81;
    internal const ushort DefaultDiwStop = 0x2CC1;
    internal const ushort DefaultDdfStart = 0x0038;
    internal const ushort DefaultDdfStop = 0x00D0;
    // ECS Agnus implements V11 in addition to the documented V8-V10 bits.
    internal const ushort DiwhighWritableMask = 0x2F2F;

    internal const ushort VarBeamEnable = 0x0080;
    internal const ushort VarHSyncEnable = 0x0100;
    internal const ushort VarVSyncEnable = 0x0200;
    internal const ushort VarVBlankEnable = 0x1000;

    private readonly bool _ecs;
    private readonly RasterTiming _timing;
    private readonly ChipDmaAddressing _dmaAddressing;
    private readonly ushort[] _beamValues = new ushort[0x12];
    private readonly uint[] _bitplanePointers = new uint[6];
    private readonly long[] _bitplanePointerWriteCycles = new long[6];
    private readonly uint[] _spritePointers = new uint[8];
    private ushort _vposw;
    private ushort _copcon;
    private uint _copperListPointer1;
    private uint _copperListPointer2;
    private ushort _diwStart;
    private ushort _diwStop;
    private ushort _diwHigh;
    private bool _diwHighValid;
    private ushort _ddfStart;
    private ushort _ddfStop;
    private short _bpl1mod;
    private short _bpl2mod;

    public AgnusRegisterBank(AgnusModel model, ChipDmaAddressing dmaAddressing, RasterTiming timing)
    {
        _ecs = model == AgnusModel.Ecs;
        _dmaAddressing = dmaAddressing;
        _timing = timing;
        Reset();
    }

    public AgnusRegisterBank(AgnusModel model, ChipDmaAddressing dmaAddressing)
        : this(model, dmaAddressing, RasterTiming.Pal)
    {
    }

    public bool IsEcs => _ecs;
    public ushort VposWrite => _vposw;
    public ushort CopperControl => _copcon;
    public uint CopperListPointer1 => _copperListPointer1;
    public uint CopperListPointer2 => _copperListPointer2;
    public ushort DiwStart => _diwStart;
    public ushort DiwStop => _diwStop;
    public ushort DiwHigh => _diwHigh;
    public bool DiwHighValid => _diwHighValid;
    public ushort DdfStart => _ddfStart;
    public ushort DdfStop => _ddfStop;
    public short BitplaneModulo1 => _bpl1mod;
    public short BitplaneModulo2 => _bpl2mod;
    public ushort BeamControl => this[Beamcon0];
    public bool VariableBeamEnabled => (BeamControl & VarBeamEnable) != 0;
    public bool VariableHSyncEnabled => (BeamControl & VarHSyncEnable) != 0;
    public bool VariableVSyncEnabled => (BeamControl & VarVSyncEnable) != 0;
    public bool VariableVBlankEnabled => (BeamControl & VarVBlankEnable) != 0;
    public int HSyncStart => this[Hsstrt];
    public int VSyncStart => this[Vsstrt];
    public int VBlankStart => this[Vbstrt];
    public int HhposWrite => this[Hhposw];

    public uint GetBitplanePointer(int plane)
        => (uint)plane < (uint)_bitplanePointers.Length ? _bitplanePointers[plane] : 0;

    public uint GetSpritePointer(int sprite)
        => (uint)sprite < (uint)_spritePointers.Length ? _spritePointers[sprite] : 0;

    public void SetBitplanePointerFromDma(int plane, uint pointer, long cycle)
    {
        if ((uint)plane < (uint)_bitplanePointers.Length &&
            cycle >= _bitplanePointerWriteCycles[plane])
        {
            _bitplanePointers[plane] = _dmaAddressing.Mask(pointer);
        }
    }

    public int EffectiveColorClocksPerLine(RasterTiming timing)
        => VariableBeamEnabled ? this[Htotal] + 1 : timing.ColorClocksPerLine;

    public int EffectiveFrameLines(RasterTiming timing, bool longFrame)
        => VariableBeamEnabled ? this[Vtotal] + 1 : longFrame ? timing.LongFrameLines : timing.ShortFrameLines;

    public void Reset()
    {
        Array.Clear(_beamValues);
        Array.Clear(_bitplanePointers);
        Array.Fill(_bitplanePointerWriteCycles, long.MinValue);
        Array.Clear(_spritePointers);
        _vposw = 0;
        _copcon = 0;
        _copperListPointer1 = 0;
        _copperListPointer2 = 0;
        _diwStart = DefaultDiwStart;
        _diwStop = DefaultDiwStop;
        _diwHigh = 0;
        _diwHighValid = false;
        _ddfStart = DefaultDdfStart;
        _ddfStop = DefaultDdfStop;
        _bpl1mod = 0;
        _bpl2mod = 0;
        this[Htotal] = (ushort)(_timing.ColorClocksPerLine - 1);
        this[Vtotal] = (ushort)(_timing.LongFrameLines - 1);
    }

    public bool IsReadable(ushort offset)
        => _ecs && (offset & 0x01FE) is >= Htotal and <= Hcenter;

    public bool IsSupported(ushort offset)
    {
        offset &= 0x01FE;
        if (offset == Diwhigh || offset is >= Htotal and <= Hcenter)
        {
            return _ecs;
        }

        return IsCommonRegister(offset);
    }

    public bool TryRead(ushort offset, AgnusBeamPosition beam, out ushort value)
    {
        offset &= 0x01FE;
        if (!IsReadable(offset))
        {
            value = 0;
            return false;
        }

        value = offset == Hhposr ? (ushort)(beam.BeamHorizontal & 0x01FF) : this[offset];
        return true;
    }

    public AgnusRegisterWriteResult Write(ushort offset, ushort value, long cycle = long.MinValue)
    {
        offset &= 0x01FE;
        if (offset == Vposw)
        {
            _vposw = value;
            return Result(AgnusRegisterWriteEffects.Stored |
                AgnusRegisterWriteEffects.BeamState |
                AgnusRegisterWriteEffects.TimingChanged);
        }

        if (TryWriteCommonRegister(offset, value, cycle, out var effects))
        {
            return Result(effects);
        }

        if (!_ecs || offset is < Htotal or > Diwhigh || offset == Hhposr)
        {
            return default;
        }

        if (offset == Diwhigh)
        {
            _diwHigh = (ushort)(value & DiwhighWritableMask);
            _diwHighValid = true;
            return Result(AgnusRegisterWriteEffects.Stored | AgnusRegisterWriteEffects.DisplayHistory);
        }

        var mask = offset switch
        {
            Vtotal or Vsstop or Vbstrt or Vbstop or Vsstrt => (ushort)0x07FF,
            Beamcon0 => (ushort)0x7FFF,
            _ => (ushort)0x01FF
        };
        var masked = (ushort)(value & mask);
        var changed = this[offset] != masked;
        this[offset] = masked;
        var timingChanged = changed && (offset == Beamcon0 ||
            (VariableBeamEnabled && offset is Htotal or Vtotal) ||
            offset == Hhposw);
        var beamEffects = AgnusRegisterWriteEffects.Stored | AgnusRegisterWriteEffects.BeamState;
        if (timingChanged)
        {
            beamEffects |= AgnusRegisterWriteEffects.TimingChanged;
        }

        // These registers control future sync/blanking events, but do not
        // redefine the beam's line/frame geometry. Keep them separate from
        // TimingChanged so a mid-frame write cannot re-anchor the beam.
        if (changed && offset is Hsstop or Hbstrt or Hbstop or Vsstop or Vbstrt or Vbstop or Hsstrt or Vsstrt or Hcenter)
        {
            beamEffects |= AgnusRegisterWriteEffects.RasterEventsChanged;
        }

        return Result(beamEffects);
    }

    private bool TryWriteCommonRegister(
        ushort offset,
        ushort value,
        long cycle,
        out AgnusRegisterWriteEffects effects)
    {
        effects = AgnusRegisterWriteEffects.Stored | AgnusRegisterWriteEffects.DisplayHistory;
        switch (offset)
        {
            case Copcon:
                _copcon = value;
                return true;
            case Cop1lch:
                _copperListPointer1 = WritePointerHigh(_copperListPointer1, value);
                return true;
            case Cop1lcl:
                _copperListPointer1 = WritePointerLow(_copperListPointer1, value);
                return true;
            case Cop2lch:
                _copperListPointer2 = WritePointerHigh(_copperListPointer2, value);
                return true;
            case Cop2lcl:
                _copperListPointer2 = WritePointerLow(_copperListPointer2, value);
                return true;
            case Copjmp1:
                effects = AgnusRegisterWriteEffects.DisplayHistory | AgnusRegisterWriteEffects.CopperJump1;
                return true;
            case Copjmp2:
                effects = AgnusRegisterWriteEffects.DisplayHistory | AgnusRegisterWriteEffects.CopperJump2;
                return true;
            case Diwstrt:
                _diwStart = value;
                _diwHighValid = false;
                return true;
            case Diwstop:
                _diwStop = value;
                _diwHighValid = false;
                return true;
            case Ddfstrt:
                _ddfStart = value;
                return true;
            case Ddfstop:
                _ddfStop = value;
                return true;
            case Bpl1mod:
                _bpl1mod = unchecked((short)value);
                return true;
            case Bpl2mod:
                _bpl2mod = unchecked((short)value);
                return true;
        }

        if (offset is >= BplPointerFirst and <= BplPointerLast)
        {
            var plane = (offset - BplPointerFirst) / 4;
            if ((uint)plane < (uint)_bitplanePointers.Length)
            {
                _bitplanePointers[plane] = (offset & 2) == 0
                    ? WritePointerHigh(_bitplanePointers[plane], value)
                    : WritePointerLow(_bitplanePointers[plane], value);
                _bitplanePointerWriteCycles[plane] = cycle;
                return true;
            }
        }

        if (offset is >= SpritePointerFirst and <= SpritePointerLast)
        {
            var sprite = (offset - SpritePointerFirst) / 4;
            if ((uint)sprite < (uint)_spritePointers.Length)
            {
                _spritePointers[sprite] = (offset & 2) == 0
                    ? WritePointerHigh(_spritePointers[sprite], value)
                    : WritePointerLow(_spritePointers[sprite], value);
                return true;
            }
        }

        effects = AgnusRegisterWriteEffects.None;
        return false;
    }

    private static bool IsCommonRegister(ushort offset)
        => offset is Vposw or Copcon or
            Cop1lch or Cop1lcl or Cop2lch or Cop2lcl or Copjmp1 or Copjmp2 or
            Diwstrt or Diwstop or Ddfstrt or Ddfstop or Bpl1mod or Bpl2mod ||
            offset is >= BplPointerFirst and <= BplPointerLast ||
            offset is >= SpritePointerFirst and <= SpritePointerLast;

    private uint WritePointerHigh(uint pointer, ushort highWord)
        => _dmaAddressing.WritePointerHigh(pointer, highWord);

    private uint WritePointerLow(uint pointer, ushort lowWord)
        => _dmaAddressing.WritePointerLow(pointer, lowWord);

    private static AgnusRegisterWriteResult Result(AgnusRegisterWriteEffects effects)
        => new(effects);

    private ushort this[ushort offset]
    {
        get => _beamValues[(offset - Htotal) >> 1];
        set => _beamValues[(offset - Htotal) >> 1] = value;
    }
}
