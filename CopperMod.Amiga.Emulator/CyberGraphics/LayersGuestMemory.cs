/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using Amiga;

namespace CopperMod.Amiga;

internal sealed partial class AmigaBootController
{
    /// <summary>
    /// SDK-codec view over the emulator's guarded big-endian guest bus.
    /// Public structure offsets remain owned by CopperSharp.Sdk.Amiga.
    /// </summary>
    private readonly struct LayersGuestMemory : IAmigaGuestMemory
    {
        private readonly AmigaBootController _owner;

        internal LayersGuestMemory(AmigaBootController owner)
            => _owner = owner;

        public byte ReadUInt8(APTR address, int offset = 0)
            => _owner._machine.Bus.ReadByte(Add(address, offset));

        public ushort ReadUInt16(APTR address, int offset = 0)
            => _owner._machine.Bus.ReadWord(Add(address, offset));

        public uint ReadUInt32(APTR address, int offset = 0)
            => _owner._machine.Bus.ReadLong(Add(address, offset));

        public void WriteUInt8(APTR address, int offset, byte value)
            => _owner._machine.Bus.WriteByte(Add(address, offset), value, 0);

        public void WriteUInt16(APTR address, int offset, ushort value)
            => _owner._machine.Bus.WriteWord(Add(address, offset), value, 0);

        public void WriteUInt32(APTR address, int offset, uint value)
            => _owner._machine.Bus.WriteLong(Add(address, offset), value);

        public void Clear(APTR address, uint byteCount)
        {
            for (var offset = 0u; offset < byteCount; offset++)
                _owner._machine.Bus.WriteByte(address.Raw + offset, 0, 0);
        }

        public void Copy(APTR source, APTR destination, uint byteCount)
        {
            if (destination.Raw > source.Raw &&
                (ulong)destination.Raw < (ulong)source.Raw + byteCount)
            {
                for (var offset = byteCount; offset != 0; offset--)
                {
                    var index = offset - 1;
                    _owner._machine.Bus.WriteByte(
                        destination.Raw + index,
                        _owner._machine.Bus.ReadByte(source.Raw + index),
                        0);
                }
                return;
            }

            for (var offset = 0u; offset < byteCount; offset++)
            {
                _owner._machine.Bus.WriteByte(
                    destination.Raw + offset,
                    _owner._machine.Bus.ReadByte(source.Raw + offset),
                    0);
            }
        }

        public bool IsMapped(APTR address, uint byteSize)
            => byteSize <= int.MaxValue &&
                address.Raw <= uint.MaxValue - byteSize &&
                _owner._machine.Bus.IsMappedMemoryRange(
                    address.Raw,
                    checked((int)byteSize));

        private static uint Add(APTR address, int offset)
            => unchecked(address.Raw + (uint)offset);
    }
}
