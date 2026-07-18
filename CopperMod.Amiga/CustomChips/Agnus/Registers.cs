/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.CustomChips.Agnus;

internal readonly record struct AgnusRegisterWriteResult(bool Handled, bool TimingChanged);

internal sealed class AgnusRegisterBank
{
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

    internal const ushort VarBeamEnable = 0x0080;
    internal const ushort VarHSyncEnable = 0x0100;
    internal const ushort VarVSyncEnable = 0x0200;
    internal const ushort VarVBlankEnable = 0x1000;

    private readonly bool _ecs;
    private readonly ushort[] _values = new ushort[0x12];

    public AgnusRegisterBank(AgnusModel model)
    {
        _ecs = model == AgnusModel.Ecs;
        Reset();
    }

    public bool IsEcs => _ecs;
    public ushort BeamControl => this[Beamcon0];
    public bool VariableBeamEnabled => (BeamControl & VarBeamEnable) != 0;
    public bool VariableHSyncEnabled => (BeamControl & VarHSyncEnable) != 0;
    public bool VariableVSyncEnabled => (BeamControl & VarVSyncEnable) != 0;
    public bool VariableVBlankEnabled => (BeamControl & VarVBlankEnable) != 0;
    public int HSyncStart => this[Hsstrt];
    public int VSyncStart => this[Vsstrt];
    public int VBlankStart => this[Vbstrt];
    public int HhposWrite => this[Hhposw];
    public int EffectiveColorClocksPerLine(RasterTiming timing)
        => VariableBeamEnabled ? this[Htotal] + 1 : timing.ColorClocksPerLine;
    public int EffectiveFrameLines(RasterTiming timing, bool longFrame)
        => VariableBeamEnabled ? this[Vtotal] + 1 : longFrame ? timing.LongFrameLines : timing.ShortFrameLines;

    public void Reset()
    {
        System.Array.Clear(_values);
        this[Htotal] = (ushort)(RasterTiming.Pal.ColorClocksPerLine - 1);
        this[Vtotal] = (ushort)(RasterTiming.Pal.LongFrameLines - 1);
    }

    public bool IsReadable(ushort offset)
        => _ecs && offset is >= Htotal and <= Hcenter;

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

    public AgnusRegisterWriteResult Write(ushort offset, ushort value)
    {
        offset &= 0x01FE;
        if (offset == 0x02A)
            return new(true, true);
        if (!_ecs || offset is < Htotal or > Hcenter || offset == Hhposr)
            return default;

        var mask = offset switch
        {
            Vtotal or Vsstop or Vbstrt or Vbstop or Vsstrt => (ushort)0x07FF,
            Beamcon0 => (ushort)0x7FFF,
            _ => (ushort)0x01FF
        };
        var masked = (ushort)(value & mask);
        var changed = this[offset] != masked;
        this[offset] = masked;
        var affectsTiming = changed && (offset == Beamcon0 ||
            (VariableBeamEnabled && offset is Htotal or Vtotal) ||
            (offset == Hhposw));
        return new(true, affectsTiming);
    }

    private ushort this[ushort offset]
    {
        get => _values[(offset - Htotal) >> 1];
        set => _values[(offset - Htotal) >> 1] = value;
    }
}
