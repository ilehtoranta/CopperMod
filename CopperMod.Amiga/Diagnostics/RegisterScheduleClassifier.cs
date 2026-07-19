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
            offset = NormalizeOffset(offset);
            if (CustomRegisterMetadata.TryGet(offset, out var descriptor))
            {
                return descriptor.IsCpuReadable(chipset);
            }

            return offset <= 0x01E ||
                (chipset.Denise == DeniseModel.Ecs && offset == (ushort)CustomRegister.DeniseId) ||
                (chipset.Agnus == AgnusModel.Ecs && offset is >= AgnusRegisterBank.Htotal and <= AgnusRegisterBank.Hcenter);
        }

        public static HardwareScheduleImpact GetPotentialImpact(AmigaChipset chipset, ushort offset)
        {
            offset = NormalizeOffset(offset);

            if (CustomRegisterMetadata.TryGet(offset, out var descriptor) &&
                !descriptor.IsPresent(chipset))
            {
                return HardwareScheduleImpact.None;
            }

            if (IsColorRegister(offset))
            {
                return HardwareScheduleImpact.Composition;
            }

            if (IsSpriteRegister(offset))
            {
                return HardwareScheduleImpact.Composition;
            }

            if (IsBitplanePointerRegister(offset) || IsBitplaneDataRegister(offset))
            {
                return HardwareScheduleImpact.Composition;
            }

            if (offset == AgnusRegisterBank.Vposw)
            {
                return HardwareScheduleImpact.Raster;
            }

            if (offset is >= AgnusRegisterBank.Htotal and <= AgnusRegisterBank.Hcenter)
            {
                return chipset.Agnus == AgnusModel.Ecs && offset != AgnusRegisterBank.Hhposr
                    ? HardwareScheduleImpact.Raster
                    : HardwareScheduleImpact.None;
            }

            if (offset == AgnusRegisterBank.Diwhigh)
            {
                var impact = HardwareScheduleImpact.None;
                if (chipset.Agnus == AgnusModel.Ecs)
                {
                    impact |= HardwareScheduleImpact.Bitplane;
                }
                if (chipset.Denise == DeniseModel.Ecs)
                {
                    impact |= HardwareScheduleImpact.Composition;
                }

                return impact;
            }

            if (offset is 0x08E or 0x090)
            {
                return HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition;
            }

            if (offset is 0x092 or 0x094 or 0x108 or 0x10A)
            {
                return HardwareScheduleImpact.Bitplane;
            }

            if (offset == 0x100)
            {
                return HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition;
            }

            if (offset is 0x102 or 0x104)
            {
                return HardwareScheduleImpact.Composition;
            }

            if (offset == 0x106)
            {
                return chipset.Denise == DeniseModel.Ecs
                    ? HardwareScheduleImpact.Sprite | HardwareScheduleImpact.Composition
                    : HardwareScheduleImpact.None;
            }

            if (offset is 0x02E or 0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A)
            {
                return HardwareScheduleImpact.Copper;
            }

            if (offset == 0x096)
            {
                return HardwareScheduleImpact.Bitplane |
                    HardwareScheduleImpact.Sprite |
                    HardwareScheduleImpact.Copper |
                    HardwareScheduleImpact.Blitter |
                    HardwareScheduleImpact.Audio |
                    HardwareScheduleImpact.Disk;
            }

            if (offset is 0x09A or 0x09C)
            {
                return EventSchedulingImpacts;
            }

            if (offset == 0x09E)
            {
                return HardwareScheduleImpact.Audio | HardwareScheduleImpact.Disk;
            }

            if (offset is >= 0x040 and <= 0x074)
            {
                if (offset is 0x05A or 0x05C or 0x05E && chipset.Agnus != AgnusModel.Ecs)
                {
                    return HardwareScheduleImpact.None;
                }

                return HardwareScheduleImpact.Blitter;
            }

            if (offset is 0x020 or 0x022 or 0x024 or 0x026 or 0x07E)
            {
                return HardwareScheduleImpact.Disk;
            }

            var audioRegister = GetAudioChannelRegister(offset);
            if (audioRegister >= 0)
            {
                return audioRegister == 0x08
                    ? HardwareScheduleImpact.None
                    : HardwareScheduleImpact.Audio;
            }

            return HardwareScheduleImpact.All;
        }

        public static HardwareScheduleImpact GetChangedImpact(
            AmigaChipset chipset,
            ushort offset,
            ushort oldValue,
            ushort newValue)
        {
            offset = NormalizeOffset(offset);

            if (offset == 0x058 ||
                (chipset.Agnus == AgnusModel.Ecs && offset == 0x05E) ||
                offset is 0x088 or 0x08A)
            {
                return GetPotentialImpact(chipset, offset);
            }

            if (offset == 0x106)
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

            if (offset == AgnusRegisterBank.Diwhigh)
            {
                var changed = (ushort)((oldValue ^ newValue) & AgnusRegisterBank.DiwhighWritableMask);
                return changed != 0 ? GetPotentialImpact(chipset, offset) : HardwareScheduleImpact.None;
            }

            if (offset == 0x100)
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

            if (oldValue == newValue)
            {
                return HardwareScheduleImpact.None;
            }

            return GetPotentialImpact(chipset, offset);
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

        private static bool IsSpriteRegister(ushort offset)
            => offset is >= 0x120 and < 0x180;

        private static bool IsBitplanePointerRegister(ushort offset)
            => offset is >= 0x0E0 and <= 0x0F6;

        private static bool IsBitplaneDataRegister(ushort offset)
            => offset is >= 0x110 and <= 0x11A;

        private static int GetAudioChannelRegister(ushort offset)
        {
            if (offset < 0x0A0 || offset > 0x0DA)
            {
                return -1;
            }

            var channel = (offset - 0x0A0) / 0x10;
            var register = (offset - 0x0A0) % 0x10;
            return channel < AmigaConstants.PaulaChannelCount && register <= 0x0A
                ? register
                : -1;
        }
    }
}
