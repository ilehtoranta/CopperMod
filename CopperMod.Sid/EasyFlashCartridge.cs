using System;

namespace CopperMod.Sid
{
    internal enum EasyFlashMemoryMode
    {
        Off,
        Ultimax,
        Cartridge8K,
        Cartridge16K
    }

    internal sealed class EasyFlashCartridge
    {
        private readonly C64CartridgeImage _image;
        private int _bank;
        private byte _control;

        public EasyFlashCartridge(C64CartridgeImage image)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            Reset();
        }

        public int Bank => _bank;

        public byte Control => _control;

        public EasyFlashMemoryMode Mode => DecodeMode(_control);

        public void Reset()
        {
            _bank = 0;
            _control = 0x07; // 16K EasyFlash boot: ROML at $8000 and ROMH at $A000.
        }

        public bool TryRead(ushort address, out byte value)
        {
            var mode = Mode;
            if (address >= 0x8000 && address <= 0x9FFF && mode is EasyFlashMemoryMode.Ultimax or EasyFlashMemoryMode.Cartridge8K or EasyFlashMemoryMode.Cartridge16K)
            {
                value = _image.RomlBanks[_bank][address - 0x8000];
                return true;
            }

            if (address >= 0xA000 && address <= 0xBFFF && mode == EasyFlashMemoryMode.Cartridge16K)
            {
                value = _image.RomhBanks[_bank][address - 0xA000];
                return true;
            }

            if (address >= 0xE000 && mode == EasyFlashMemoryMode.Ultimax)
            {
                value = _image.RomhBanks[_bank][address - 0xE000];
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryWriteIo1(ushort address, byte value)
        {
            if (address == 0xDE00)
            {
                _bank = value & 0x3F;
                return true;
            }

            if (address == 0xDE02)
            {
                _control = (byte)(value & 0x87);
                return true;
            }

            return address >= 0xDE00 && address <= 0xDEFF;
        }

        private static EasyFlashMemoryMode DecodeMode(byte control)
        {
            var mode = control & 0x07;
            return mode switch
            {
                0x04 => EasyFlashMemoryMode.Off,
                0x05 => EasyFlashMemoryMode.Ultimax,
                0x06 => EasyFlashMemoryMode.Cartridge8K,
                0x07 => EasyFlashMemoryMode.Cartridge16K,
                _ => EasyFlashMemoryMode.Ultimax
            };
        }
    }
}
