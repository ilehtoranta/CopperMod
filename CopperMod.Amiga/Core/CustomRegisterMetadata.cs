/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Core;

[Flags]
internal enum CustomRegisterOwner : byte
{
    None = 0,
    Agnus = 1 << 0,
    Denise = 1 << 1,
    Paula = 1 << 2,
    Blitter = 1 << 3,
    Disk = 1 << 4
}

internal enum CustomRegisterPresence : byte
{
    Common,
    EcsAgnus,
    EcsDenise,
    EcsAgnusOrDenise,
    Absent
}

internal enum CustomRegisterAccess : byte
{
    ReadOnly,
    WriteOnly,
    ReadWrite,
    Strobe
}

internal enum CustomRegisterReadback : byte
{
    OpenBus,
    Stored,
    Hardware
}

internal readonly record struct CustomRegisterDescriptor(
    ushort Offset,
    string Name,
    CustomRegisterOwner Owner,
    CustomRegisterPresence Presence,
    CustomRegisterAccess Access,
    ushort ResetValue,
    ushort OcsWritableMask,
    ushort EcsWritableMask,
    CustomRegisterReadback Readback)
{
    public bool IsPresent(AmigaChipset chipset)
        => Presence switch
        {
            CustomRegisterPresence.Common => true,
            CustomRegisterPresence.EcsAgnus => chipset.Agnus == AgnusModel.Ecs,
            CustomRegisterPresence.EcsDenise => chipset.Denise == DeniseModel.Ecs,
            CustomRegisterPresence.EcsAgnusOrDenise =>
                chipset.Agnus == AgnusModel.Ecs || chipset.Denise == DeniseModel.Ecs,
            _ => false
        };

    public ushort GetWritableMask(AmigaChipset chipset)
    {
        if (!IsPresent(chipset) || Access is CustomRegisterAccess.ReadOnly or CustomRegisterAccess.Strobe)
        {
            return 0;
        }

        var ecsOwnerPresent =
            (Owner.HasFlag(CustomRegisterOwner.Agnus) && chipset.Agnus == AgnusModel.Ecs) ||
            (Owner.HasFlag(CustomRegisterOwner.Denise) && chipset.Denise == DeniseModel.Ecs);
        return ecsOwnerPresent ? EcsWritableMask : OcsWritableMask;
    }

    public bool IsCpuReadable(AmigaChipset chipset)
        => IsPresent(chipset) && Access is CustomRegisterAccess.ReadOnly or CustomRegisterAccess.ReadWrite;

    public bool CanCopperWrite(AmigaChipset chipset, ushort copcon)
        => IsPresent(chipset) &&
            Access != CustomRegisterAccess.ReadOnly &&
            AgnusCopperRegisterAccess.CanWrite(Offset, copcon);
}

internal static class CustomRegisterMetadata
{
    private static readonly Dictionary<ushort, CustomRegisterDescriptor> Descriptors = BuildDescriptors();

    public static IReadOnlyCollection<CustomRegisterDescriptor> All => Descriptors.Values;

    public static bool TryGet(ushort offset, out CustomRegisterDescriptor descriptor)
        => Descriptors.TryGetValue(Normalize(offset), out descriptor);

    public static CustomRegisterDescriptor Get(ushort offset)
        => Descriptors[Normalize(offset)];

    private static ushort Normalize(ushort offset)
        => (ushort)(offset & 0x01FE);

    private static Dictionary<ushort, CustomRegisterDescriptor> BuildDescriptors()
    {
        var result = new Dictionary<ushort, CustomRegisterDescriptor>();

        Add(result, 0x004, "VPOSR", CustomRegisterOwner.Agnus, CustomRegisterPresence.Common,
            CustomRegisterAccess.ReadOnly, 0, 0, 0, CustomRegisterReadback.Hardware);
        Add(result, 0x012, "POT0DAT", CustomRegisterOwner.Paula, CustomRegisterPresence.Common,
            CustomRegisterAccess.ReadOnly, 0, 0, 0, CustomRegisterReadback.Hardware);
        Add(result, 0x014, "POT1DAT", CustomRegisterOwner.Paula, CustomRegisterPresence.Common,
            CustomRegisterAccess.ReadOnly, 0, 0, 0, CustomRegisterReadback.Hardware);
        Add(result, 0x020, "DSKPTH", CustomRegisterOwner.Disk | CustomRegisterOwner.Agnus,
            CustomRegisterPresence.Common, CustomRegisterAccess.WriteOnly, 0, 0x000F, 0x001F,
            CustomRegisterReadback.OpenBus);
        Add(result, 0x02E, "COPCON", CustomRegisterOwner.Agnus, CustomRegisterPresence.Common,
            CustomRegisterAccess.WriteOnly, 0, 0x0002, 0x0002, CustomRegisterReadback.OpenBus);
        Add(result, 0x038, "STRLONG", CustomRegisterOwner.Agnus, CustomRegisterPresence.Common,
            CustomRegisterAccess.Strobe, 0, 0, 0, CustomRegisterReadback.OpenBus);
        Add(result, 0x042, "BLTCON1", CustomRegisterOwner.Blitter, CustomRegisterPresence.Common,
            CustomRegisterAccess.WriteOnly, 0, 0xFFFF, 0xFFFF, CustomRegisterReadback.OpenBus);

        AddDmaPointerHigh(result, 0x048, "BLTCPTH", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddDmaPointerHigh(result, 0x04C, "BLTBPTH", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddDmaPointerHigh(result, 0x050, "BLTAPTH", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddDmaPointerHigh(result, 0x054, "BLTDPTH", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);

        Add(result, 0x05A, "BLTCON0L", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus,
            CustomRegisterPresence.EcsAgnus, CustomRegisterAccess.WriteOnly, 0, 0, 0x00FF,
            CustomRegisterReadback.OpenBus);
        Add(result, 0x05C, "BLTSIZV", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus,
            CustomRegisterPresence.EcsAgnus, CustomRegisterAccess.WriteOnly, 0, 0, 0x7FFF,
            CustomRegisterReadback.OpenBus);
        Add(result, 0x05E, "BLTSIZH", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus,
            CustomRegisterPresence.EcsAgnus, CustomRegisterAccess.WriteOnly, 0, 0, 0x07FF,
            CustomRegisterReadback.OpenBus);
        Add(result, 0x07C, "DENISEID", CustomRegisterOwner.Denise, CustomRegisterPresence.EcsDenise,
            CustomRegisterAccess.ReadOnly, 0x00FC, 0, 0, CustomRegisterReadback.Hardware);

        AddDmaPointerHigh(result, 0x080, "COP1LCH", CustomRegisterOwner.Agnus);
        AddDmaPointerHigh(result, 0x084, "COP2LCH", CustomRegisterOwner.Agnus);
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var baseOffset = (ushort)(0x0A0 + (channel * 0x10));
            AddDmaPointerHigh(result, baseOffset, $"AUD{channel}LCH", CustomRegisterOwner.Paula | CustomRegisterOwner.Agnus);
            Add(result, (ushort)(baseOffset + 0x06), $"AUD{channel}PER", CustomRegisterOwner.Paula,
                CustomRegisterPresence.Common, CustomRegisterAccess.WriteOnly, 0, 0xFFFF, 0xFFFF,
                CustomRegisterReadback.OpenBus);
        }

        for (var plane = 0; plane < 6; plane++)
        {
            AddDmaPointerHigh(result, (ushort)(0x0E0 + (plane * 4)), $"BPL{plane + 1}PTH", CustomRegisterOwner.Agnus);
        }

        Add(result, 0x100, "BPLCON0", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise,
            CustomRegisterPresence.Common, CustomRegisterAccess.WriteOnly, 0, 0xFF0F, 0xFF4F,
            CustomRegisterReadback.OpenBus);
        Add(result, 0x104, "BPLCON2", CustomRegisterOwner.Denise, CustomRegisterPresence.Common,
            CustomRegisterAccess.WriteOnly, 0, 0x007F, 0x007F, CustomRegisterReadback.OpenBus);
        Add(result, 0x106, "BPLCON3", CustomRegisterOwner.Denise, CustomRegisterPresence.EcsDenise,
            CustomRegisterAccess.WriteOnly, 0, 0, 0x0037, CustomRegisterReadback.OpenBus);

        for (var sprite = 0; sprite < 8; sprite++)
        {
            Add(result, (ushort)(0x142 + (sprite * 8)), $"SPR{sprite}CTL", CustomRegisterOwner.Denise,
                CustomRegisterPresence.Common, CustomRegisterAccess.WriteOnly, 0, 0xFFFF, 0xFFFF,
                CustomRegisterReadback.OpenBus);
        }

        AddEcsBeamRegister(result, 0x1C0, "HTOTAL", 0x00E2, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1C2, "HSSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1C4, "HBSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1C6, "HBSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1C8, "VTOTAL", 0x0138, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1CA, "VSSTOP", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1CC, "VBSTRT", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1CE, "VBSTOP", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1D0, "SPRHSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1D2, "SPRHSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1D4, "BPLHSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1D6, "BPLHSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1D8, "HHPOSW", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1DA, "HHPOSR", 0, 0, CustomRegisterAccess.ReadOnly);
        AddEcsBeamRegister(result, 0x1DC, "BEAMCON0", 0, 0x7FFF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1DE, "HSSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1E0, "VSSTRT", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, 0x1E2, "HCENTER", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        Add(result, 0x1E4, "DIWHIGH", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise,
            CustomRegisterPresence.EcsAgnusOrDenise, CustomRegisterAccess.WriteOnly, 0, 0, 0x2F2F,
            CustomRegisterReadback.OpenBus);

        Add(result, 0x1FC, "FMODE", CustomRegisterOwner.None, CustomRegisterPresence.Absent,
            CustomRegisterAccess.WriteOnly, 0, 0, 0, CustomRegisterReadback.OpenBus);
        return result;
    }

    private static void AddDmaPointerHigh(
        Dictionary<ushort, CustomRegisterDescriptor> result,
        ushort offset,
        string name,
        CustomRegisterOwner owner)
        => Add(result, offset, name, owner, CustomRegisterPresence.Common, CustomRegisterAccess.WriteOnly,
            0, 0x000F, 0x001F, CustomRegisterReadback.OpenBus);

    private static void AddEcsBeamRegister(
        Dictionary<ushort, CustomRegisterDescriptor> result,
        ushort offset,
        string name,
        ushort resetValue,
        ushort writableMask,
        CustomRegisterAccess access)
        => Add(result, offset, name, CustomRegisterOwner.Agnus, CustomRegisterPresence.EcsAgnus,
            access, resetValue, 0, writableMask,
            access == CustomRegisterAccess.WriteOnly ? CustomRegisterReadback.OpenBus : CustomRegisterReadback.Stored);

    private static void Add(
        Dictionary<ushort, CustomRegisterDescriptor> result,
        ushort offset,
        string name,
        CustomRegisterOwner owner,
        CustomRegisterPresence presence,
        CustomRegisterAccess access,
        ushort resetValue,
        ushort ocsWritableMask,
        ushort ecsWritableMask,
        CustomRegisterReadback readback)
        => result.Add(offset, new CustomRegisterDescriptor(
            offset,
            name,
            owner,
            presence,
            access,
            resetValue,
            ocsWritableMask,
            ecsWritableMask,
            readback));
}
