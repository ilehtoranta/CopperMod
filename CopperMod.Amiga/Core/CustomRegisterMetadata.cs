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

internal readonly record struct CustomRegisterAvailability(
    AmigaChipCapabilities RequiredAll,
    AmigaChipCapabilities RequiredAny,
    AmigaChipCapabilities ForbiddenAny,
    bool Never = false)
{
    public static CustomRegisterAvailability Common { get; } = new(
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability EcsDmaOrLater { get; } = new(
        AmigaChipCapabilities.EcsDmaRegisters,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability EcsDisplayOrLater { get; } = new(
        AmigaChipCapabilities.EcsDisplayRegisters,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability EcsDmaOrDisplayOrLater { get; } = new(
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.EcsDmaRegisters | AmigaChipCapabilities.EcsDisplayRegisters,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability AgaAlice { get; } = new(
        AmigaChipCapabilities.AgaAliceRegisters,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability AgaLisa { get; } = new(
        AmigaChipCapabilities.AgaLisaRegisters,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability AgaAliceAndLisa { get; } = new(
        AmigaChipCapabilities.AgaAliceRegisters | AmigaChipCapabilities.AgaLisaRegisters,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None);

    public static CustomRegisterAvailability Absent { get; } = new(
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None,
        AmigaChipCapabilities.None,
        Never: true);

    public bool Matches(AmigaChipCapabilities capabilities)
        => !Never &&
            (capabilities & RequiredAll) == RequiredAll &&
            (RequiredAny == AmigaChipCapabilities.None || (capabilities & RequiredAny) != 0) &&
            (capabilities & ForbiddenAny) == 0;
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

internal enum DisplayChipIdentification : byte
{
    EcsDenise = 0xFC,
    AgaLisa = 0xF8
}

internal readonly record struct CustomRegisterDescriptor(
    ushort Offset,
    string Name,
    CustomRegisterOwner Owner,
    CustomRegisterAvailability Availability,
    CustomRegisterAccess Access,
    ushort ResetValue,
    ushort OcsWritableMask,
    ushort EcsWritableMask,
    CustomRegisterReadback Readback,
    ushort? EcsResetValue = null,
    ushort? AgaResetValue = null,
    ushort? AgaWritableMask = null,
    ushort AgaResetKnownMask = 0xFFFF)
{
    public bool IsPresent(AmigaChipset chipset)
        => Availability.Matches(chipset.Capabilities);

    public ushort GetWritableMask(AmigaChipset chipset)
    {
        if (!IsPresent(chipset) || Access is CustomRegisterAccess.ReadOnly or CustomRegisterAccess.Strobe)
        {
            return 0;
        }

        if (HasAgaOwner(chipset))
        {
            return AgaWritableMask ?? EcsWritableMask;
        }

        return HasEcsOwner(chipset) ? EcsWritableMask : OcsWritableMask;
    }

    public ushort GetResetValue(AmigaChipset chipset)
        => HasAgaOwner(chipset)
            ? AgaResetValue ?? EcsResetValue ?? ResetValue
            : HasEcsOwner(chipset)
                ? EcsResetValue ?? ResetValue
                : ResetValue;

    public ushort GetResetKnownMask(AmigaChipset chipset)
        => HasAgaOwner(chipset) ? AgaResetKnownMask : (ushort)0xFFFF;

    public bool IsCpuReadable(AmigaChipset chipset)
        => IsPresent(chipset) && Access is CustomRegisterAccess.ReadOnly or CustomRegisterAccess.ReadWrite;

    public bool CanCopperWrite(AmigaChipset chipset, ushort copcon)
        => IsPresent(chipset) &&
            Access != CustomRegisterAccess.ReadOnly &&
            AgnusCopperRegisterAccess.CanWrite(Offset, copcon);

    private bool HasEcsOwner(AmigaChipset chipset)
        => (Owner.HasFlag(CustomRegisterOwner.Agnus) && chipset.SupportsEcsDmaRegisters) ||
            (Owner.HasFlag(CustomRegisterOwner.Denise) && chipset.SupportsEcsDisplayRegisters);

    private bool HasAgaOwner(AmigaChipset chipset)
        => (Owner.HasFlag(CustomRegisterOwner.Agnus) &&
                chipset.HasAllCapabilities(AmigaChipCapabilities.AgaAliceRegisters)) ||
            (Owner.HasFlag(CustomRegisterOwner.Denise) &&
                chipset.HasAllCapabilities(AmigaChipCapabilities.AgaLisaRegisters));
}

internal static class CustomRegisterMetadata
{
    private const int RegisterCount = 0x100;

    private static readonly CustomRegisterDescriptor[] Descriptors;
    private static readonly bool[] Declared;

    static CustomRegisterMetadata()
    {
        Descriptors = BuildDescriptors(out var declared);
        Declared = declared;
    }

    public static IReadOnlyList<CustomRegisterDescriptor> All => Descriptors;

    // Compatibility query for callers that still distinguish the old sparse
    // metadata from reserved address-space entries. New register-file code
    // uses GetComplete so every normalized word has a descriptor.
    public static bool TryGet(ushort offset, out CustomRegisterDescriptor descriptor)
    {
        var index = Normalize(offset) >> 1;
        descriptor = Descriptors[index];
        return Declared[index];
    }

    public static CustomRegisterDescriptor Get(ushort offset)
        => Descriptors[Normalize(offset) >> 1];

    public static CustomRegisterDescriptor GetComplete(ushort offset)
        => Descriptors[Normalize(offset) >> 1];

    public static bool IsDeclared(ushort offset)
        => Declared[Normalize(offset) >> 1];

    private static ushort Normalize(ushort offset)
        => (ushort)(offset & 0x01FE);

    private static CustomRegisterDescriptor[] BuildDescriptors(out bool[] declared)
    {
        var result = new CustomRegisterDescriptor[RegisterCount];
        declared = new bool[RegisterCount];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = (ushort)(index << 1);
            result[index] = new CustomRegisterDescriptor(
                offset,
                $"RESERVED_{offset:X3}",
                CustomRegisterOwner.None,
                CustomRegisterAvailability.Absent,
                CustomRegisterAccess.WriteOnly,
                0,
                0,
                0,
                CustomRegisterReadback.OpenBus);
        }

        AddRead(result, declared, 0x000, "BLTDDAT", CustomRegisterOwner.Blitter, CustomRegisterReadback.Hardware);
        AddRead(result, declared, 0x002, "DMACONR", CustomRegisterOwner.Agnus | CustomRegisterOwner.Paula | CustomRegisterOwner.Blitter, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x004, "VPOSR", CustomRegisterOwner.Agnus, CustomRegisterReadback.Hardware);
        AddRead(result, declared, 0x006, "VHPOSR", CustomRegisterOwner.Agnus, CustomRegisterReadback.Hardware);
        AddRead(result, declared, 0x008, "DSKDATR", CustomRegisterOwner.Disk, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x00A, "JOY0DAT", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x00C, "JOY1DAT", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x00E, "CLXDAT", CustomRegisterOwner.Denise, CustomRegisterReadback.Hardware);
        AddRead(result, declared, 0x010, "ADKCONR", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x012, "POT0DAT", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x014, "POT1DAT", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x016, "POTGOR", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x018, "SERDATR", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x01A, "DSKBYTR", CustomRegisterOwner.Disk, CustomRegisterReadback.Hardware);
        AddRead(result, declared, 0x01C, "INTENAR", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);
        AddRead(result, declared, 0x01E, "INTREQR", CustomRegisterOwner.Paula, CustomRegisterReadback.Stored);

        Add(result, 0x020, "DSKPTH", CustomRegisterOwner.Disk | CustomRegisterOwner.Agnus,
            CustomRegisterAvailability.Common, CustomRegisterAccess.WriteOnly, 0, 0x000F, 0x001F,
            CustomRegisterReadback.OpenBus, declared);
        AddWrite(result, declared, 0x022, "DSKPTL", CustomRegisterOwner.Disk | CustomRegisterOwner.Agnus, 0xFFFE);
        AddWrite(result, declared, 0x024, "DSKLEN", CustomRegisterOwner.Disk, 0xFFFF);
        AddWrite(result, declared, 0x026, "DSKDAT", CustomRegisterOwner.Disk, 0xFFFF);
        AddWrite(result, declared, 0x028, "REFPTR", CustomRegisterOwner.Agnus, 0xFFFF);
        AddWrite(result, declared, 0x02A, "VPOSW", CustomRegisterOwner.Agnus, 0xFFFF);
        AddWrite(result, declared, 0x02C, "VHPOSW", CustomRegisterOwner.Agnus, 0xFFFF);
        Add(result, 0x02E, "COPCON", CustomRegisterOwner.Agnus, CustomRegisterAvailability.Common,
            CustomRegisterAccess.WriteOnly, 0, 0x0002, 0x0002, CustomRegisterReadback.OpenBus);
        Declare(declared, 0x02E);
        AddWrite(result, declared, 0x030, "SERDAT", CustomRegisterOwner.Paula, 0xFFFF);
        AddWrite(result, declared, 0x032, "SERPER", CustomRegisterOwner.Paula, 0x7FFF);
        AddWrite(result, declared, 0x034, "POTGO", CustomRegisterOwner.Paula, 0xFFFF);
        AddWrite(result, declared, 0x036, "JOYTEST", CustomRegisterOwner.Paula, 0xFFFF);
        AddStrobe(result, declared, 0x038, "STREQU", CustomRegisterOwner.Agnus);
        AddStrobe(result, declared, 0x03A, "STRVBL", CustomRegisterOwner.Agnus);
        AddStrobe(result, declared, 0x03C, "STRHOR", CustomRegisterOwner.Agnus);
        AddStrobe(result, declared, 0x03E, "STRLONG", CustomRegisterOwner.Agnus);

        AddWrite(result, declared, 0x040, "BLTCON0", CustomRegisterOwner.Blitter, 0xFFFF);
        Add(result, 0x042, "BLTCON1", CustomRegisterOwner.Blitter, CustomRegisterAvailability.Common,
            CustomRegisterAccess.WriteOnly, 0, 0xFFFF, 0xFFFF, CustomRegisterReadback.OpenBus);
        Declare(declared, 0x042);
        AddWrite(result, declared, 0x044, "BLTAFWM", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x046, "BLTALWM", CustomRegisterOwner.Blitter, 0xFFFF);

        AddDmaPointer(result, declared, 0x048, "BLTCPTH", "BLTCPTL", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddDmaPointer(result, declared, 0x04C, "BLTBPTH", "BLTBPTL", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddDmaPointer(result, declared, 0x050, "BLTAPTH", "BLTAPTL", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddDmaPointer(result, declared, 0x054, "BLTDPTH", "BLTDPTL", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus);
        AddWrite(result, declared, 0x058, "BLTSIZE", CustomRegisterOwner.Blitter, 0xFFFF);

        Add(result, 0x05A, "BLTCON0L", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus,
            CustomRegisterAvailability.EcsDmaOrLater, CustomRegisterAccess.WriteOnly, 0, 0, 0x00FF,
            CustomRegisterReadback.OpenBus, declared);
        Add(result, 0x05C, "BLTSIZV", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus,
            CustomRegisterAvailability.EcsDmaOrLater, CustomRegisterAccess.WriteOnly, 0, 0, 0x7FFF,
            CustomRegisterReadback.OpenBus, declared);
        Add(result, 0x05E, "BLTSIZH", CustomRegisterOwner.Blitter | CustomRegisterOwner.Agnus,
            CustomRegisterAvailability.EcsDmaOrLater, CustomRegisterAccess.WriteOnly, 0, 0, 0x07FF,
            CustomRegisterReadback.OpenBus, declared);
        AddWrite(result, declared, 0x060, "BLTCMOD", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x062, "BLTBMOD", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x064, "BLTAMOD", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x066, "BLTDMOD", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x070, "BLTCDAT", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x072, "BLTBDAT", CustomRegisterOwner.Blitter, 0xFFFF);
        AddWrite(result, declared, 0x074, "BLTADAT", CustomRegisterOwner.Blitter, 0xFFFF);

        Add(result, 0x07C, "DENISEID", CustomRegisterOwner.Denise, CustomRegisterAvailability.EcsDisplayOrLater,
            CustomRegisterAccess.ReadOnly, (ushort)DisplayChipIdentification.EcsDenise, 0, 0,
            CustomRegisterReadback.Stored, declared,
            agaResetValue: (ushort)DisplayChipIdentification.AgaLisa,
            agaResetKnownMask: 0x00FF);
        AddWrite(result, declared, 0x07E, "DSKSYNC", CustomRegisterOwner.Disk, 0xFFFF);

        AddDmaPointer(result, declared, 0x080, "COP1LCH", "COP1LCL", CustomRegisterOwner.Agnus);
        AddDmaPointer(result, declared, 0x084, "COP2LCH", "COP2LCL", CustomRegisterOwner.Agnus);
        AddStrobe(result, declared, 0x088, "COPJMP1", CustomRegisterOwner.Agnus);
        AddStrobe(result, declared, 0x08A, "COPJMP2", CustomRegisterOwner.Agnus);
        AddWrite(result, declared, 0x08C, "COPINS", CustomRegisterOwner.Agnus, 0);
        AddWrite(result, declared, 0x08E, "DIWSTRT", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise, 0xFFFF);
        AddWrite(result, declared, 0x090, "DIWSTOP", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise, 0xFFFF);
        AddWrite(result, declared, 0x092, "DDFSTRT", CustomRegisterOwner.Agnus, 0xFFFF);
        AddWrite(result, declared, 0x094, "DDFSTOP", CustomRegisterOwner.Agnus, 0xFFFF);
        AddSetClear(result, declared, 0x096, "DMACON", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise | CustomRegisterOwner.Paula | CustomRegisterOwner.Blitter | CustomRegisterOwner.Disk, 0x07FF);
        AddWrite(result, declared, 0x098, "CLXCON", CustomRegisterOwner.Denise, 0xFFFF);
        AddSetClear(result, declared, 0x09A, "INTENA", CustomRegisterOwner.Paula, 0x7FFF);
        AddSetClear(result, declared, 0x09C, "INTREQ", CustomRegisterOwner.Paula, 0x7FFF);
        AddSetClear(result, declared, 0x09E, "ADKCON", CustomRegisterOwner.Paula | CustomRegisterOwner.Disk, 0x7FFF);

        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var baseOffset = (ushort)(0x0A0 + (channel * 0x10));
            AddDmaPointer(result, declared, baseOffset, $"AUD{channel}LCH", $"AUD{channel}LCL", CustomRegisterOwner.Paula | CustomRegisterOwner.Agnus);
            AddWrite(result, declared, (ushort)(baseOffset + 0x04), $"AUD{channel}LEN", CustomRegisterOwner.Paula, 0xFFFF);
            Add(result, (ushort)(baseOffset + 0x06), $"AUD{channel}PER", CustomRegisterOwner.Paula,
                CustomRegisterAvailability.Common, CustomRegisterAccess.WriteOnly, 0, 0xFFFF, 0xFFFF,
                CustomRegisterReadback.OpenBus, declared);
            AddWrite(result, declared, (ushort)(baseOffset + 0x08), $"AUD{channel}VOL", CustomRegisterOwner.Paula, 0x007F);
            AddWrite(result, declared, (ushort)(baseOffset + 0x0A), $"AUD{channel}DAT", CustomRegisterOwner.Paula, 0xFFFF);
        }

        for (var plane = 0; plane < 6; plane++)
        {
            var offset = (ushort)(0x0E0 + (plane * 4));
            AddDmaPointer(result, declared, offset, $"BPL{plane + 1}PTH", $"BPL{plane + 1}PTL", CustomRegisterOwner.Agnus);
        }

        Add(result, 0x100, "BPLCON0", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise,
            CustomRegisterAvailability.Common, CustomRegisterAccess.WriteOnly, 0, 0xFF0F, 0xFF4F,
            CustomRegisterReadback.OpenBus, declared);
        AddWrite(result, declared, 0x102, "BPLCON1", CustomRegisterOwner.Denise, 0x00FF);
        Add(result, 0x104, "BPLCON2", CustomRegisterOwner.Denise, CustomRegisterAvailability.Common,
            CustomRegisterAccess.WriteOnly, 0, 0x007F, 0x007F, CustomRegisterReadback.OpenBus);
        Declare(declared, 0x104);
        Add(result, 0x106, "BPLCON3", CustomRegisterOwner.Denise, CustomRegisterAvailability.EcsDisplayOrLater,
            CustomRegisterAccess.WriteOnly, 0, 0, 0x0037, CustomRegisterReadback.OpenBus, declared);
        AddWrite(result, declared, 0x108, "BPL1MOD", CustomRegisterOwner.Agnus, 0xFFFF);
        AddWrite(result, declared, 0x10A, "BPL2MOD", CustomRegisterOwner.Agnus, 0xFFFF);
        AddAbsent(result, declared, 0x10C, "BPLCON4");
        AddAbsent(result, declared, 0x10E, "CLXCON2");

        for (var plane = 0; plane < 6; plane++)
        {
            AddWrite(result, declared, (ushort)(0x110 + (plane * 2)), $"BPL{plane + 1}DAT", CustomRegisterOwner.Denise, 0xFFFF);
        }
        AddAbsent(result, declared, 0x11C, "BPL7DAT");
        AddAbsent(result, declared, 0x11E, "BPL8DAT");

        for (var sprite = 0; sprite < 8; sprite++)
        {
            var pointerOffset = (ushort)(0x120 + (sprite * 4));
            AddDmaPointer(result, declared, pointerOffset, $"SPR{sprite}PTH", $"SPR{sprite}PTL", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise);
        }

        for (var sprite = 0; sprite < 8; sprite++)
        {
            var baseOffset = (ushort)(0x140 + (sprite * 8));
            AddWrite(result, declared, baseOffset, $"SPR{sprite}POS", CustomRegisterOwner.Denise, 0xFFFF);
            AddWrite(result, declared, (ushort)(baseOffset + 2), $"SPR{sprite}CTL", CustomRegisterOwner.Denise, 0xFFFF);
            AddWrite(result, declared, (ushort)(baseOffset + 4), $"SPR{sprite}DATA", CustomRegisterOwner.Denise, 0xFFFF);
            AddWrite(result, declared, (ushort)(baseOffset + 6), $"SPR{sprite}DATB", CustomRegisterOwner.Denise, 0xFFFF);
        }

        for (var color = 0; color < 32; color++)
        {
            AddWrite(result, declared, (ushort)(0x180 + (color * 2)), $"COLOR{color:00}", CustomRegisterOwner.Denise, 0x0FFF);
        }

        AddEcsBeamRegister(result, declared, 0x1C0, "HTOTAL", 0x00E2, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1C2, "HSSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1C4, "HBSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1C6, "HBSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1C8, "VTOTAL", 0x0138, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1CA, "VSSTOP", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1CC, "VBSTRT", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1CE, "VBSTOP", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1D0, "SPRHSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1D2, "SPRHSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1D4, "BPLHSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1D6, "BPLHSTOP", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1D8, "HHPOSW", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        Add(result, 0x1DA, "HHPOSR", CustomRegisterOwner.Agnus, CustomRegisterAvailability.EcsDmaOrLater,
            CustomRegisterAccess.ReadOnly, 0, 0, 0, CustomRegisterReadback.Hardware, declared);
        AddEcsBeamRegister(result, declared, 0x1DC, "BEAMCON0", 0, 0x7FFF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1DE, "HSSTRT", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1E0, "VSSTRT", 0, 0x07FF, CustomRegisterAccess.ReadWrite);
        AddEcsBeamRegister(result, declared, 0x1E2, "HCENTER", 0, 0x01FF, CustomRegisterAccess.ReadWrite);
        Add(result, 0x1E4, "DIWHIGH", CustomRegisterOwner.Agnus | CustomRegisterOwner.Denise,
            CustomRegisterAvailability.EcsDmaOrDisplayOrLater, CustomRegisterAccess.WriteOnly, 0, 0, 0x2F2F,
            CustomRegisterReadback.OpenBus, declared);

        AddAbsent(result, declared, 0x1E6, "BPLHMOD");
        AddAbsent(result, declared, 0x1E8, "SPRHPT");
        AddAbsent(result, declared, 0x1EA, "BPLHPT");

        Add(result, 0x1FC, "FMODE", CustomRegisterOwner.None, CustomRegisterAvailability.Absent,
            CustomRegisterAccess.WriteOnly, 0, 0, 0, CustomRegisterReadback.OpenBus, declared);
        return result;
    }

    private static void AddRead(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort offset,
        string name,
        CustomRegisterOwner owner,
        CustomRegisterReadback readback)
        => Add(result, offset, name, owner, CustomRegisterAvailability.Common, CustomRegisterAccess.ReadOnly,
            0, 0, 0, readback, declared);

    private static void AddWrite(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort offset,
        string name,
        CustomRegisterOwner owner,
        ushort writableMask)
        => Add(result, offset, name, owner, CustomRegisterAvailability.Common, CustomRegisterAccess.WriteOnly,
            0, writableMask, writableMask, CustomRegisterReadback.OpenBus, declared);

    private static void AddSetClear(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort offset,
        string name,
        CustomRegisterOwner owner,
        ushort writableMask)
        => AddWrite(result, declared, offset, name, owner, writableMask);

    private static void AddStrobe(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort offset,
        string name,
        CustomRegisterOwner owner)
        => Add(result, offset, name, owner, CustomRegisterAvailability.Common, CustomRegisterAccess.Strobe,
            0, 0, 0, CustomRegisterReadback.OpenBus, declared);

    private static void AddAbsent(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort offset,
        string name)
        => Add(result, offset, name, CustomRegisterOwner.None, CustomRegisterAvailability.Absent,
            CustomRegisterAccess.WriteOnly, 0, 0, 0, CustomRegisterReadback.OpenBus, declared);

    private static void AddDmaPointer(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort highOffset,
        string highName,
        string lowName,
        CustomRegisterOwner owner)
    {
        Add(result, highOffset, highName, owner, CustomRegisterAvailability.Common, CustomRegisterAccess.WriteOnly,
            0, 0x000F, 0x001F, CustomRegisterReadback.OpenBus, declared);
        AddWrite(result, declared, (ushort)(highOffset + 2), lowName, owner, 0xFFFE);
    }

    private static void AddEcsBeamRegister(
        CustomRegisterDescriptor[] result,
        bool[] declared,
        ushort offset,
        string name,
        ushort resetValue,
        ushort writableMask,
        CustomRegisterAccess access)
        => Add(result, offset, name, CustomRegisterOwner.Agnus, CustomRegisterAvailability.EcsDmaOrLater,
            access, resetValue, 0, writableMask,
            access == CustomRegisterAccess.WriteOnly ? CustomRegisterReadback.OpenBus : CustomRegisterReadback.Stored,
            declared);

    private static void Add(
        CustomRegisterDescriptor[] result,
        ushort offset,
        string name,
        CustomRegisterOwner owner,
        CustomRegisterAvailability availability,
        CustomRegisterAccess access,
        ushort resetValue,
        ushort ocsWritableMask,
        ushort ecsWritableMask,
        CustomRegisterReadback readback,
        bool[]? declared = null,
        ushort? ecsResetValue = null,
        ushort? agaResetValue = null,
        ushort? agaWritableMask = null,
        ushort agaResetKnownMask = 0xFFFF)
    {
        var normalized = Normalize(offset);
        result[normalized >> 1] = new CustomRegisterDescriptor(
            normalized,
            name,
            owner,
            availability,
            access,
            resetValue,
            ocsWritableMask,
            ecsWritableMask,
            readback,
            ecsResetValue,
            agaResetValue,
            agaWritableMask,
            agaResetKnownMask);
        if (declared != null)
        {
            Declare(declared, normalized);
        }
    }

    private static void Declare(bool[] declared, ushort offset)
        => declared[Normalize(offset) >> 1] = true;
}
