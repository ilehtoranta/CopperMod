/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Expansion
{
    internal sealed class AutoconfigFastRamBoard : AutoconfigBoard
    {
        public const ushort ManufacturerId = 0x07DB;
        public const byte ProductId = 0x49;

        private readonly AmigaLinearRamBackend _memory;

        public AutoconfigFastRamBoard(AmigaLinearRamBackend memory)
            : base(CreateIdentity(memory?.Length ?? throw new ArgumentNullException(nameof(memory))))
        {
            _memory = memory;
            _memory.Unmap();
        }

        public override bool IsDirectRam => true;

        public AmigaLinearRamBackend Memory => _memory;

        public static AutoconfigIdentity CreateIdentity(int size)
            => AutoconfigIdentity.CreateFastRam(size);

        public static uint GetDefaultBase(int size)
        {
            var identity = CreateIdentity(size);
            if (identity.BusKind == AutoconfigBusKind.ZorroII)
            {
                return 0x0020_0000u;
            }

            var alignment = (uint)size;
            return (0x1000_0000u + alignment - 1u) & ~(alignment - 1u);
        }

        public static void ValidateBase(int size, uint baseAddress)
        {
            var identity = CreateIdentity(size);
            var endExclusive = (ulong)baseAddress + (uint)size;
            if (identity.BusKind == AutoconfigBusKind.ZorroII)
            {
                if (baseAddress < 0x0020_0000u ||
                    endExclusive > 0x00A0_0000u ||
                    (baseAddress - 0x0020_0000u) % (uint)size != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(baseAddress),
                        baseAddress,
                        "Zorro II fast RAM must be naturally placed in the $00200000-$009FFFFF expansion range.");
                }

                return;
            }

            if (baseAddress < 0x1000_0000u ||
                (baseAddress & ((uint)size - 1u)) != 0 ||
                endExclusive > 0x8000_0000UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baseAddress),
                    baseAddress,
                    "Zorro III fast RAM must be naturally aligned at or above $10000000 and end no later than $80000000.");
            }
        }

        public override bool ContainsBoardAddress(uint address)
            => IsConfigured && _memory.TryGetOffset(address, out _);

        public override byte ReadBoardByte(uint address)
        {
            if (!_memory.TryGetOffset(address, out var offset))
            {
                throw new ArgumentOutOfRangeException(nameof(address), address, "Address is outside Autoconfig fast RAM.");
            }

            return _memory[offset];
        }

        public override bool TryWriteBoardByte(uint address, byte value)
        {
            if (!_memory.TryGetOffset(address, out var offset))
            {
                return false;
            }

            _memory[offset] = value;
            return true;
        }

        public override void ColdReset()
        {
            _memory.ClearData();
            _memory.ClearCodePageGenerations();
            base.ColdReset();
        }

        protected override void ValidateConfigurationBase(uint baseAddress)
            => ValidateBase(_memory.Length, baseAddress);

        protected override void OnConfigured(uint baseAddress)
            => _memory.Map(baseAddress);

        protected override void OnConfigurationRemoved()
            => _memory.Unmap();
    }
}
