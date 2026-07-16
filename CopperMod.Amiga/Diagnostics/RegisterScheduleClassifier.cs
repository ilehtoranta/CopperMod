/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.Diagnostics
{
    internal static class CustomRegisterScheduleClassifier
    {
        public static ushort NormalizeOffset(ushort offset)
            => (ushort)(offset & 0x01FE);

        public static bool IsOcsReadableRegister(ushort offset)
            => NormalizeOffset(offset) <= 0x01E;

        public static bool IsScheduleAffectingCustomWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return !IsKnownBusScheduleBenignWrite(offset);
        }

        public static bool IsColorRegister(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset is >= 0x180 and < 0x1C0;
        }

        public static bool IsKnownBusScheduleBenignWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return IsColorRegister(offset) ||
                IsSpriteRegister(offset) ||
                IsAudioVolumeRegister(offset) ||
                IsBitplanePointerRegister(offset) ||
                IsBitplaneDataRegister(offset) ||
                offset is 0x08E or 0x090 or 0x102 or 0x104 or 0x108 or 0x10A;
        }

        public static bool IsDisplayBusScheduleAffectingWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset is 0x02E or
                0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A or
                0x092 or 0x094 or 0x096 or 0x100;
        }

        public static AgnusLiveDisplaySlotOwnerMask GetPreparedDisplaySlotOwnerChanges(
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

            if (offset is 0x092 or 0x094 or 0x100)
            {
                return AgnusLiveDisplaySlotOwnerMask.Bitplane;
            }

            return offset is 0x02E or 0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A
                ? AgnusLiveDisplaySlotOwnerMask.Copper
                : AgnusLiveDisplaySlotOwnerMask.None;
        }

        public static bool IsPaulaBusScheduleAffectingWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            if (offset is 0x096 or 0x09A or 0x09C or 0x09E)
            {
                return true;
            }

            var channelRegister = GetAudioChannelRegister(offset);
            return channelRegister is 0x00 or 0x02 or 0x04 or 0x06 or 0x0A;
        }

        public static bool IsBlitterBusScheduleAffectingWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset == 0x058;
        }

        public static bool IsDiskBusScheduleAffectingWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset is 0x024 or 0x026 or 0x07E or 0x09E;
        }

        public static bool IsCpuEventHorizonAffectingCopperMove(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset < 0x020 ||
                IsCopperControlFlowWrite(offset) ||
                offset is 0x020 or 0x022 or 0x024 or 0x058 or
                    0x088 or 0x08A or 0x096 or 0x09A or 0x09C or 0x09E ||
                GetAudioChannelRegister(offset) is 0x00 or 0x02 or 0x04 or 0x06 or 0x0A;
        }

        public static bool IsCopperControlFlowWrite(ushort offset)
        {
            offset = NormalizeOffset(offset);
            return offset is 0x02E or
                0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A;
        }

        private static bool IsSpriteRegister(ushort offset)
            => offset is >= 0x120 and < 0x180;

        private static bool IsBitplanePointerRegister(ushort offset)
            => offset is >= 0x0E0 and <= 0x0F6;

        private static bool IsBitplaneDataRegister(ushort offset)
            => offset is >= 0x110 and <= 0x11A;

        private static bool IsAudioVolumeRegister(ushort offset)
            => GetAudioChannelRegister(offset) == 0x08;

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
