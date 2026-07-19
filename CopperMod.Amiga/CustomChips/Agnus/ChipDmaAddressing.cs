/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.CustomChips.Agnus;

internal readonly struct ChipDmaAddressing
{
    public ChipDmaAddressing(DmaChipModel model)
    {
        AddressMask = model.SupportsEcsRegisters()
            ? AmigaConstants.EcsChipDmaAddressMask
            : AmigaConstants.OcsChipDmaAddressMask;
    }

    public uint AddressMask { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Mask(uint address)
        => address & AddressMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint WritePointerHigh(uint pointer, ushort highWord)
        => (((uint)highWord << 16) | (pointer & 0x0000_FFFEu)) & AddressMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint WritePointerLow(uint pointer, ushort lowWord)
        => ((pointer & 0x00FF_0000u) | (uint)(lowWord & 0xFFFE)) & AddressMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint AddPointerOffset(uint pointer, int byteOffset)
        => (pointer + unchecked((uint)byteOffset)) & AddressMask;

    public static bool IsStandardPhysicalSize(int size)
        => size is AmigaConstants.A500BootChipRamSize or
            AmigaConstants.DefaultChipRamSize or
            AmigaConstants.MaxChipRamSize;

    public static bool SupportsPhysicalSize(DmaChipModel model, int size)
        => IsStandardPhysicalSize(size) &&
            (model.SupportsEcsRegisters() || size <= AmigaConstants.DefaultChipRamSize);
}
