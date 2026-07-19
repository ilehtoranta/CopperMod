/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Diagnostics
{
    [Flags]
    internal enum HardwareScheduleImpact : ushort
    {
        None = 0,
        Raster = 1 << 0,
        Bitplane = 1 << 1,
        Sprite = 1 << 2,
        Copper = 1 << 3,
        Blitter = 1 << 4,
        Composition = 1 << 5,
        Audio = 1 << 6,
        Disk = 1 << 7,
        All = Raster | Bitplane | Sprite | Copper | Blitter | Composition | Audio | Disk
    }

    internal static class CustomRegisterScheduleClassifier
    {
        private const ushort Bplcon0FetchMask = 0xF040;
        private const ushort EcsBplcon3WritableMask = 0x0037;
        private const ushort Bplcon3BorderSpriteEnable = 0x0002;

        public const HardwareScheduleImpact EventSchedulingImpacts =
            HardwareScheduleImpact.All & ~HardwareScheduleImpact.Composition;

        public const HardwareScheduleImpact DisplayDmaImpacts =
            HardwareScheduleImpact.Bitplane |
            HardwareScheduleImpact.Sprite |
            HardwareScheduleImpact.Copper;

        public const HardwareScheduleImpact CompositionImpacts = HardwareScheduleImpact.Composition;

        public static ushort NormalizeOffset(ushort offset)
            => (ushort)(offset & 0x01FE);

        public static bool IsReadableRegister(AmigaChipset chipset, ushort offset)
        {
            ref readonly var entry = ref CustomRegisterFile.GetResolved(chipset, NormalizeOffset(offset));
            return entry.IsPresent && entry.ReadName != null;
        }

        public static HardwareScheduleImpact GetPotentialImpact(AmigaChipset chipset, ushort offset)
        {
            ref readonly var entry = ref CustomRegisterFile.GetResolved(chipset, NormalizeOffset(offset));
            return entry.PotentialImpact;
        }

        public static HardwareScheduleImpact GetChangedImpact(
            AmigaChipset chipset,
            ushort offset,
            ushort oldValue,
            ushort newValue)
        {
            offset = NormalizeOffset(offset);
            ref readonly var entry = ref CustomRegisterFile.GetResolved(chipset, offset);
            switch (entry.ImpactRule)
            {
                case CustomRegisterImpactRule.Always:
                    return entry.PotentialImpact;

                case CustomRegisterImpactRule.Bplcon3Fields:
                {
                    if (chipset.Denise != DeniseModel.Ecs)
                    {
                        return HardwareScheduleImpact.None;
                    }

                    var changed = (ushort)((oldValue ^ newValue) & EcsBplcon3WritableMask);
                    if (changed == 0)
                    {
                        return HardwareScheduleImpact.None;
                    }

                    var impact = HardwareScheduleImpact.Composition;
                    if ((changed & Bplcon3BorderSpriteEnable) != 0)
                    {
                        impact |= HardwareScheduleImpact.Sprite;
                    }

                    return impact;
                }

                case CustomRegisterImpactRule.DiwhighOwners:
                {
                    var changed = (ushort)((oldValue ^ newValue) & AgnusRegisterBank.DiwhighWritableMask);
                    return changed != 0 ? entry.PotentialImpact : HardwareScheduleImpact.None;
                }

                case CustomRegisterImpactRule.Bplcon0Fields:
                {
                    var changed = (ushort)(oldValue ^ newValue);
                    if (changed == 0)
                    {
                        return HardwareScheduleImpact.None;
                    }

                    var impact = HardwareScheduleImpact.Composition;
                    var fetchMask = chipset.Agnus == AgnusModel.Ecs
                        ? Bplcon0FetchMask
                        : (ushort)(Bplcon0FetchMask & ~0x0040);
                    if ((changed & fetchMask) != 0)
                    {
                        impact |= HardwareScheduleImpact.Bitplane;
                    }

                    return impact;
                }

                default:
                    return oldValue == newValue
                        ? HardwareScheduleImpact.None
                        : entry.PotentialImpact;
            }
        }

        public static HardwareScheduleImpact GetPotentialEventScheduleImpact(
            AmigaChipset chipset,
            ushort offset)
        {
            offset = NormalizeOffset(offset);
            if (offset is 0x108 or 0x10A)
            {
                // Modulo changes alter future addresses, not ownership of chip-bus slots.
                return HardwareScheduleImpact.None;
            }

            return GetPotentialImpact(chipset, offset) & EventSchedulingImpacts;
        }

        public static bool AffectsEventSchedule(HardwareScheduleImpact impact)
            => (impact & EventSchedulingImpacts) != HardwareScheduleImpact.None;

        public static bool IsColorRegister(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset is >= 0x180 and < 0x1C0;
        }

        public static AgnusLiveDisplaySlotOwnerMask GetPreparedDisplaySlotOwnerChanges(
            AmigaChipset chipset,
            ushort offset,
            ushort value)
        {
            offset = NormalizeOffset(offset);
            if (offset == 0x096)
            {
                if ((value & 0x0200) != 0)
                {
                    return AgnusLiveDisplaySlotOwnerMask.All;
                }

                var owners = AgnusLiveDisplaySlotOwnerMask.None;
                if ((value & 0x0100) != 0)
                {
                    owners |= AgnusLiveDisplaySlotOwnerMask.Bitplane;
                }
                if ((value & 0x0080) != 0)
                {
                    owners |= AgnusLiveDisplaySlotOwnerMask.Copper;
                }
                if ((value & 0x0020) != 0)
                {
                    owners |= AgnusLiveDisplaySlotOwnerMask.Sprite;
                }

                return owners;
            }

            var impact = GetPotentialEventScheduleImpact(chipset, offset) & DisplayDmaImpacts;
            var result = AgnusLiveDisplaySlotOwnerMask.None;
            if ((impact & HardwareScheduleImpact.Bitplane) != 0)
            {
                result |= AgnusLiveDisplaySlotOwnerMask.Bitplane;
            }
            if ((impact & HardwareScheduleImpact.Sprite) != 0)
            {
                result |= AgnusLiveDisplaySlotOwnerMask.Sprite;
            }
            if ((impact & HardwareScheduleImpact.Copper) != 0)
            {
                result |= AgnusLiveDisplaySlotOwnerMask.Copper;
            }

            return result;
        }

    }
}
