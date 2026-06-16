using System;
using System.Collections.Generic;
using CopperMod.Abstractions;

namespace CopperMod.Sid
{
    internal enum C64CartridgeType
    {
        EasyFlash = 32
    }

    internal sealed class C64CartridgeImage
    {
        public C64CartridgeImage(
            C64CartridgeType type,
            string name,
            byte initialExrom,
            byte initialGame,
            byte[][] romlBanks,
            byte[][] romhBanks)
        {
            Type = type;
            Name = name;
            InitialExrom = initialExrom;
            InitialGame = initialGame;
            RomlBanks = romlBanks ?? throw new ArgumentNullException(nameof(romlBanks));
            RomhBanks = romhBanks ?? throw new ArgumentNullException(nameof(romhBanks));
            if (RomlBanks.Length == 0 || RomlBanks.Length != RomhBanks.Length)
            {
                throw new ArgumentException("EasyFlash cartridge must contain paired ROML and ROMH banks.");
            }
        }

        public C64CartridgeType Type { get; }

        public string Name { get; }

        public byte InitialExrom { get; }

        public byte InitialGame { get; }

        public byte[][] RomlBanks { get; }

        public byte[][] RomhBanks { get; }

        public int BankCount => RomlBanks.Length;
    }

    internal static class C64CartridgeParser
    {
        private const int CrtHeaderLengthOffset = 0x10;
        private const int CrtVersionOffset = 0x14;
        private const int CrtTypeOffset = 0x16;
        private const int CrtExromOffset = 0x18;
        private const int CrtGameOffset = 0x19;
        private const int CrtNameOffset = 0x20;
        private const int CrtNameLength = 32;
        private const int ChipHeaderLength = 16;
        private const int EasyFlashType = 32;
        private const int EasyFlashBankCount = 64;
        private const int EasyFlashBankSize = 0x2000;

        public static bool Identify(ReadOnlySpan<byte> data)
        {
            return data.Length >= 0x40 &&
                data[0] == (byte)'C' &&
                data[1] == (byte)'6' &&
                data[2] == (byte)'4' &&
                data[3] == (byte)' ' &&
                data[4] == (byte)'C' &&
                data[5] == (byte)'A' &&
                data[6] == (byte)'R' &&
                data[7] == (byte)'T' &&
                data[8] == (byte)'R' &&
                data[9] == (byte)'I' &&
                data[10] == (byte)'D' &&
                data[11] == (byte)'G' &&
                data[12] == (byte)'E';
        }

        public static C64CartridgeImage Parse(ReadOnlySpan<byte> data)
        {
            if (!Identify(data))
            {
                throw new UnsupportedModuleFormatException("The data is not a C64 CRT cartridge image.");
            }

            var headerLength = BigEndian.ReadUInt32(data, CrtHeaderLengthOffset, "crtHeaderLength");
            if (headerLength < 0x40 || headerLength > data.Length)
            {
                throw new ModuleLoadException("CRT header length is outside the file.");
            }

            _ = BigEndian.ReadUInt16(data, CrtVersionOffset, "crtVersion");
            var cartridgeType = BigEndian.ReadUInt16(data, CrtTypeOffset, "crtType");
            if (cartridgeType != EasyFlashType)
            {
                throw new UnsupportedModuleFormatException($"Unsupported CRT cartridge type {cartridgeType}; only EasyFlash type 32 is supported.");
            }

            var name = ReadFixedString(data, CrtNameOffset, CrtNameLength);
            var roml = CreateBanks();
            var romh = CreateBanks();
            var seenRoml = new bool[EasyFlashBankCount];
            var seenRomh = new bool[EasyFlashBankCount];
            var offset = checked((int)headerLength);
            while (offset < data.Length)
            {
                if (offset + ChipHeaderLength > data.Length)
                {
                    throw new ModuleLoadException("CRT CHIP packet header extends beyond the file.");
                }

                if (data[offset] != (byte)'C' || data[offset + 1] != (byte)'H' || data[offset + 2] != (byte)'I' || data[offset + 3] != (byte)'P')
                {
                    throw new ModuleLoadException("CRT contains an invalid CHIP packet signature.");
                }

                var packetLength = BigEndian.ReadUInt32(data, offset + 4, "chipPacketLength");
                if (packetLength < ChipHeaderLength || offset + packetLength > data.Length)
                {
                    throw new ModuleLoadException("CRT CHIP packet length is outside the file.");
                }

                _ = BigEndian.ReadUInt16(data, offset + 8, "chipType");
                var bank = BigEndian.ReadUInt16(data, offset + 10, "chipBank");
                var address = BigEndian.ReadUInt16(data, offset + 12, "chipAddress");
                var length = BigEndian.ReadUInt16(data, offset + 14, "chipLength");
                if (length != EasyFlashBankSize || packetLength != ChipHeaderLength + EasyFlashBankSize)
                {
                    throw new UnsupportedModuleFormatException("EasyFlash CRT support requires 8 KiB CHIP packets.");
                }

                if (bank >= EasyFlashBankCount)
                {
                    throw new UnsupportedModuleFormatException("EasyFlash CRT bank number is outside the supported 0..63 range.");
                }

                var dataOffset = offset + ChipHeaderLength;
                if (address == 0x8000)
                {
                    data.Slice(dataOffset, EasyFlashBankSize).CopyTo(roml[bank]);
                    seenRoml[bank] = true;
                }
                else if (address == 0xA000)
                {
                    data.Slice(dataOffset, EasyFlashBankSize).CopyTo(romh[bank]);
                    seenRomh[bank] = true;
                }
                else
                {
                    throw new UnsupportedModuleFormatException("EasyFlash CRT support requires CHIP packets at $8000 or $A000.");
                }

                offset += checked((int)packetLength);
            }

            for (var bank = 0; bank < EasyFlashBankCount; bank++)
            {
                if (!seenRoml[bank] || !seenRomh[bank])
                {
                    throw new ModuleLoadException("EasyFlash CRT is missing a paired ROML/ROMH bank.");
                }
            }

            return new C64CartridgeImage(
                C64CartridgeType.EasyFlash,
                name,
                data[CrtExromOffset],
                data[CrtGameOffset],
                roml,
                romh);
        }

        private static byte[][] CreateBanks()
        {
            var banks = new byte[EasyFlashBankCount][];
            for (var i = 0; i < banks.Length; i++)
            {
                banks[i] = new byte[EasyFlashBankSize];
            }

            return banks;
        }

        private static string ReadFixedString(ReadOnlySpan<byte> data, int offset, int length)
        {
            var span = data.Slice(offset, length);
            var zero = span.IndexOf((byte)0);
            if (zero >= 0)
            {
                span = span.Slice(0, zero);
            }

            return System.Text.Encoding.Latin1.GetString(span).TrimEnd();
        }
    }
}
