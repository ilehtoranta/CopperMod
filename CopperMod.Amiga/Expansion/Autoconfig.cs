/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Expansion
{
    internal enum AutoconfigBusKind
    {
        ZorroII,
        ZorroIII
    }

    internal readonly record struct AutoconfigIdentity(
        AutoconfigBusKind BusKind,
        byte Type,
        byte ProductId,
        byte Flags,
        ushort ManufacturerId,
        uint SerialNumber,
        ushort DiagnosticRomVector,
        int Size)
    {
        public static AutoconfigIdentity CreateFastRam(int size)
        {
            var (busKind, sizeCode) = size switch
            {
                64 * 1024 => (AutoconfigBusKind.ZorroII, 1),
                128 * 1024 => (AutoconfigBusKind.ZorroII, 2),
                256 * 1024 => (AutoconfigBusKind.ZorroII, 3),
                512 * 1024 => (AutoconfigBusKind.ZorroII, 4),
                1024 * 1024 => (AutoconfigBusKind.ZorroII, 5),
                2 * 1024 * 1024 => (AutoconfigBusKind.ZorroII, 6),
                4 * 1024 * 1024 => (AutoconfigBusKind.ZorroII, 7),
                8 * 1024 * 1024 => (AutoconfigBusKind.ZorroII, 0),
                16 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 0),
                32 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 1),
                64 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 2),
                128 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 3),
                256 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 4),
                512 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 5),
                1024 * 1024 * 1024 => (AutoconfigBusKind.ZorroIII, 6),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(size),
                    size,
                    "Autoconfig fast RAM must use a standard Zorro II or extended Zorro III size.")
            };

            var type = (byte)((busKind == AutoconfigBusKind.ZorroII ? 0xE0 : 0xA0) | sizeCode);
            var flags = busKind == AutoconfigBusKind.ZorroII
                ? (byte)0x80
                : (byte)0xB0;
            return new AutoconfigIdentity(
                busKind,
                type,
                AutoconfigFastRamBoard.ProductId,
                flags,
                AutoconfigFastRamBoard.ManufacturerId,
                0,
                0,
                size);
        }

        public static AutoconfigIdentity CreateIoBoard(
            int size,
            ushort manufacturerId,
            byte productId,
            ushort diagnosticRomVector)
        {
            var sizeCode = size switch
            {
                64 * 1024 => 1,
                128 * 1024 => 2,
                256 * 1024 => 3,
                512 * 1024 => 4,
                1024 * 1024 => 5,
                2 * 1024 * 1024 => 6,
                4 * 1024 * 1024 => 7,
                8 * 1024 * 1024 => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, "The Zorro II board size is invalid.")
            };
            var type = (byte)(0xC0 | (diagnosticRomVector != 0 ? 0x10 : 0) | sizeCode);
            return new AutoconfigIdentity(
                AutoconfigBusKind.ZorroII,
                type,
                productId,
                0,
                manufacturerId,
                0,
                diagnosticRomVector,
                size);
        }

        public byte ReadLogicalRegister(int register)
        {
            return register switch
            {
                0x00 => Type,
                0x04 => ProductId,
                0x08 => Flags,
                0x10 => (byte)(ManufacturerId >> 8),
                0x14 => (byte)ManufacturerId,
                0x18 => (byte)(SerialNumber >> 24),
                0x1C => (byte)(SerialNumber >> 16),
                0x20 => (byte)(SerialNumber >> 8),
                0x24 => (byte)SerialNumber,
                0x28 => (byte)(DiagnosticRomVector >> 8),
                0x2C => (byte)DiagnosticRomVector,
                _ => 0
            };
        }
    }

    internal interface IAutoconfigBoard
    {
        AutoconfigIdentity Identity { get; }

        bool IsPresent { get; }

        bool IsConfigured { get; }

        bool IsShutUp { get; }

        bool IsDirectRam { get; }

        uint ConfiguredBase { get; }

        void Configure(uint baseAddress);

        void ShutUp();

        void ResetConfiguration();

        void ColdReset();

        bool ContainsBoardAddress(uint address);

        byte ReadBoardByte(uint address);

        bool TryWriteBoardByte(uint address, byte value);
    }

    internal abstract class AutoconfigBoard : IAutoconfigBoard
    {
        protected AutoconfigBoard(AutoconfigIdentity identity)
        {
            Identity = identity;
        }

        public AutoconfigIdentity Identity { get; }

        public virtual bool IsPresent => true;

        public bool IsConfigured { get; private set; }

        public bool IsShutUp { get; private set; }

        public virtual bool IsDirectRam => false;

        public uint ConfiguredBase { get; private set; }

        public void Configure(uint baseAddress)
        {
            ValidateConfigurationBase(baseAddress);
            ConfiguredBase = baseAddress;
            IsConfigured = true;
            IsShutUp = false;
            OnConfigured(baseAddress);
        }

        public void ShutUp()
        {
            ConfiguredBase = 0;
            IsConfigured = false;
            IsShutUp = true;
            OnConfigurationRemoved();
        }

        public virtual void ResetConfiguration()
        {
            ConfiguredBase = 0;
            IsConfigured = false;
            IsShutUp = false;
            OnConfigurationRemoved();
        }

        public virtual void ColdReset()
            => ResetConfiguration();

        public abstract bool ContainsBoardAddress(uint address);

        public abstract byte ReadBoardByte(uint address);

        public abstract bool TryWriteBoardByte(uint address, byte value);

        protected virtual void OnConfigured(uint baseAddress)
        {
        }

        protected virtual void ValidateConfigurationBase(uint baseAddress)
        {
        }

        protected virtual void OnConfigurationRemoved()
        {
        }
    }

    internal sealed class AutoconfigChain
    {
        public const uint ZorroIIConfigBase = 0x00E8_0000;
        public const uint ZorroIIIConfigBase = 0xFF00_0000;
        public const uint ConfigSize = 0x0001_0000;

        private readonly IReadOnlyList<IAutoconfigBoard> _boards;
        private int _currentBoardIndex;
        private byte _zorroIIHighNibble;
        private byte _zorroIILowNibble;
        private bool _zorroIIHighWritten;
        private bool _zorroIILowWritten;
        private byte _zorroIIIMiddleByte;

        public AutoconfigChain(IReadOnlyList<IAutoconfigBoard> boards)
        {
            _boards = boards ?? throw new ArgumentNullException(nameof(boards));
            MoveToNextBoard();
        }

        public event Action<IAutoconfigBoard>? BoardConfigured;

        public event Action? AddressMapChanged;

        public IReadOnlyList<IAutoconfigBoard> Boards => _boards;

        public IAutoconfigBoard? CurrentBoard
            => _currentBoardIndex < _boards.Count ? _boards[_currentBoardIndex] : null;

        public bool ContainsConfigurationAddress(uint address)
        {
            var board = CurrentBoard;
            return board != null && TryGetConfigurationOffset(board.Identity.BusKind, address, out _);
        }

        public bool TryReadByte(uint address, out byte value)
        {
            var board = CurrentBoard;
            if (board == null || !TryGetConfigurationOffset(board.Identity.BusKind, address, out var offset))
            {
                value = 0;
                return false;
            }

            value = ReadIdentityByte(board.Identity, offset);
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            var board = CurrentBoard;
            if (board == null || !TryGetConfigurationOffset(board.Identity.BusKind, address, out var offset))
            {
                return false;
            }

            if (offset == 0x4C)
            {
                board.ShutUp();
                AdvanceAfterBoard();
                return true;
            }

            if (board.Identity.BusKind == AutoconfigBusKind.ZorroII)
            {
                WriteZorroIIByte(board, offset, value);
            }
            else
            {
                WriteZorroIIIByte(board, offset, value);
            }

            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
        {
            var board = CurrentBoard;
            if (board == null || !TryGetConfigurationOffset(board.Identity.BusKind, address, out var offset))
            {
                return false;
            }

            if (board.Identity.BusKind == AutoconfigBusKind.ZorroIII && offset == 0x44)
            {
                ConfigureCurrent(board, (uint)value << 16);
                return true;
            }

            TryWriteByte(address, (byte)(value >> 8));
            if (CurrentBoard == board)
            {
                TryWriteByte(address + 1, (byte)value);
            }

            return true;
        }

        public bool ConfigureBoardForHost(IAutoconfigBoard board, uint baseAddress)
        {
            ArgumentNullException.ThrowIfNull(board);
            if (board.IsConfigured)
            {
                return board.ConfiguredBase == baseAddress;
            }

            if (CurrentBoard != board)
            {
                return false;
            }

            ConfigureCurrent(board, baseAddress);
            return true;
        }

        public bool ContainsConfiguredAddress(uint address, bool includeDirectRam)
        {
            for (var i = 0; i < _boards.Count; i++)
            {
                var board = _boards[i];
                if ((includeDirectRam || !board.IsDirectRam) && board.ContainsBoardAddress(address))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryReadConfiguredByte(uint address, out byte value)
        {
            for (var i = 0; i < _boards.Count; i++)
            {
                var board = _boards[i];
                if (board.ContainsBoardAddress(address))
                {
                    value = board.ReadBoardByte(address);
                    return true;
                }
            }

            value = 0;
            return false;
        }

        public bool TryWriteConfiguredByte(uint address, byte value)
        {
            for (var i = 0; i < _boards.Count; i++)
            {
                if (_boards[i].TryWriteBoardByte(address, value))
                {
                    return true;
                }
            }

            return false;
        }

        public void ResetConfiguration()
        {
            for (var i = 0; i < _boards.Count; i++)
            {
                _boards[i].ResetConfiguration();
            }

            ResetChainPosition();
            AddressMapChanged?.Invoke();
        }

        public void ColdReset()
        {
            for (var i = 0; i < _boards.Count; i++)
            {
                _boards[i].ColdReset();
            }

            ResetChainPosition();
            AddressMapChanged?.Invoke();
        }

        private static byte ReadIdentityByte(AutoconfigIdentity identity, uint offset)
        {
            int register;
            bool lowNibble;
            if (identity.BusKind == AutoconfigBusKind.ZorroII)
            {
                if (offset >= 0x80 || (offset & 1) != 0)
                {
                    return 0;
                }

                register = (int)(offset & 0x7C);
                lowNibble = (offset & 2) != 0;
            }
            else
            {
                var registerOffset = offset & 0xFF;
                if (registerOffset >= 0x80 || (registerOffset & 3) != 0 || (offset & ~0x1FFu) != 0)
                {
                    return 0;
                }

                register = (int)registerOffset;
                lowNibble = (offset & 0x100) != 0;
            }

            var logical = identity.ReadLogicalRegister(register);
            var nibble = lowNibble ? logical & 0x0F : logical >> 4;
            var encoded = (byte)(nibble << 4);
            return register is 0x00 or 0x40 ? encoded : (byte)(encoded ^ 0xFF);
        }

        private static bool TryGetConfigurationOffset(
            AutoconfigBusKind busKind,
            uint address,
            out uint offset)
        {
            var baseAddress = busKind == AutoconfigBusKind.ZorroII
                ? ZorroIIConfigBase
                : ZorroIIIConfigBase;
            offset = address - baseAddress;
            return address >= baseAddress && offset < ConfigSize;
        }

        private void WriteZorroIIByte(IAutoconfigBoard board, uint offset, byte value)
        {
            if (offset == 0x4A)
            {
                _zorroIILowNibble = (byte)(value >> 4);
                _zorroIILowWritten = true;
                if (_zorroIIHighWritten)
                {
                    ConfigureCurrent(board, BuildZorroIIBase());
                }

                return;
            }

            if (offset != 0x48)
            {
                return;
            }

            _zorroIIHighNibble = (byte)(value >> 4);
            _zorroIIHighWritten = true;
            if ((value & 0x0F) != 0)
            {
                _zorroIILowNibble = (byte)(value & 0x0F);
                _zorroIILowWritten = true;
            }

            if (_zorroIILowWritten)
            {
                ConfigureCurrent(board, BuildZorroIIBase());
            }
        }

        private void WriteZorroIIIByte(IAutoconfigBoard board, uint offset, byte value)
        {
            if (offset == 0x48)
            {
                _zorroIIIMiddleByte = value;
            }
            else if (offset == 0x44)
            {
                ConfigureCurrent(board, ((uint)value << 24) | ((uint)_zorroIIIMiddleByte << 16));
            }
        }

        private uint BuildZorroIIBase()
        {
            var baseAddress = ((uint)_zorroIIHighNibble << 20) | ((uint)_zorroIILowNibble << 16);
            return baseAddress is >= 0x0008_0000 and < 0x0010_0000
                ? baseAddress | 0x00E0_0000u
                : baseAddress;
        }

        private void ConfigureCurrent(IAutoconfigBoard board, uint baseAddress)
        {
            board.Configure(baseAddress);
            BoardConfigured?.Invoke(board);
            AdvanceAfterBoard();
        }

        private void AdvanceAfterBoard()
        {
            _currentBoardIndex++;
            ClearAddressLatches();
            MoveToNextBoard();
            AddressMapChanged?.Invoke();
        }

        private void ResetChainPosition()
        {
            _currentBoardIndex = 0;
            ClearAddressLatches();
            MoveToNextBoard();
        }

        private void MoveToNextBoard()
        {
            while (_currentBoardIndex < _boards.Count)
            {
                var board = _boards[_currentBoardIndex];
                if (board.IsPresent && !board.IsConfigured && !board.IsShutUp)
                {
                    return;
                }

                _currentBoardIndex++;
            }
        }

        private void ClearAddressLatches()
        {
            _zorroIIHighNibble = 0;
            _zorroIILowNibble = 0;
            _zorroIIHighWritten = false;
            _zorroIILowWritten = false;
            _zorroIIIMiddleByte = 0;
        }
    }
}
