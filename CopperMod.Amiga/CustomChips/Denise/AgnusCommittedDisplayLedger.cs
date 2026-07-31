/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.InteropServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal enum AgnusCommittedDisplayEventKind : byte
    {
        BitplaneSample,
        SpriteSample,
        RegisterWrite
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal readonly struct AgnusCommittedDisplayEvent
    {
        private const byte GrantedFlag = 1 << 0;
        private const byte AddressValidFlag = 1 << 1;
        private const byte CopperWriteFlag = 1 << 2;

        private AgnusCommittedDisplayEvent(
            AgnusCommittedDisplayEventKind kind,
            long cycle,
            uint address,
            ushort value,
            ushort rowOrRegister,
            byte channel,
            byte word,
            byte flags)
        {
            _cycle = cycle;
            _address = address;
            _value = value;
            _rowOrRegister = rowOrRegister;
            _channel = channel;
            _word = word;
            _flags = flags;
            _kind = kind;
        }

        public static AgnusCommittedDisplayEvent BitplaneSample(
            long cycle,
            int row,
            int plane,
            int word,
            uint address,
            bool addressValid,
            ushort value,
            bool granted)
            => new AgnusCommittedDisplayEvent(
                AgnusCommittedDisplayEventKind.BitplaneSample,
                cycle,
                address,
                value,
                checked((ushort)row),
                checked((byte)plane),
                checked((byte)word),
                (byte)((granted ? GrantedFlag : 0) |
                    (addressValid ? AddressValidFlag : 0)));

        public static AgnusCommittedDisplayEvent SpriteSample(
            long cycle,
            int row,
            int sprite,
            int word,
            uint address,
            bool addressValid,
            ushort value,
            bool granted)
            => new AgnusCommittedDisplayEvent(
                AgnusCommittedDisplayEventKind.SpriteSample,
                cycle,
                address,
                value,
                checked((ushort)row),
                checked((byte)sprite),
                checked((byte)word),
                (byte)((granted ? GrantedFlag : 0) |
                    (addressValid ? AddressValidFlag : 0)));

        public static AgnusCommittedDisplayEvent RegisterWrite(
            long cycle,
            ushort register,
            ushort value,
            bool isCopper)
            => new AgnusCommittedDisplayEvent(
                AgnusCommittedDisplayEventKind.RegisterWrite,
                cycle,
                address: 0,
                value,
                register,
                channel: 0,
                word: 0,
                isCopper ? CopperWriteFlag : (byte)0);

        [FieldOffset(0)]
        private readonly long _cycle;

        [FieldOffset(8)]
        private readonly uint _address;

        [FieldOffset(12)]
        private readonly ushort _value;

        [FieldOffset(14)]
        private readonly ushort _rowOrRegister;

        [FieldOffset(16)]
        private readonly byte _channel;

        [FieldOffset(17)]
        private readonly byte _word;

        [FieldOffset(18)]
        private readonly byte _flags;

        [FieldOffset(19)]
        private readonly AgnusCommittedDisplayEventKind _kind;

        public AgnusCommittedDisplayEventKind Kind => _kind;

        public long Cycle => _cycle;

        public long CompletedCycle
            => Kind is AgnusCommittedDisplayEventKind.BitplaneSample or
                AgnusCommittedDisplayEventKind.SpriteSample
                    ? Cycle + AgnusChipSlotScheduler.SlotCycles
                    : Cycle;

        public uint Address => _address;

        public ushort Value => _value;

        public int Row => Kind == AgnusCommittedDisplayEventKind.RegisterWrite
            ? -1
            : _rowOrRegister;

        public ushort Register => Kind == AgnusCommittedDisplayEventKind.RegisterWrite
            ? _rowOrRegister
            : (ushort)0;

        public int Channel => _channel;

        public int Word => _word;

        public bool Granted => (_flags & GrantedFlag) != 0;

        public bool AddressValid => (_flags & AddressValidFlag) != 0;

        public bool IsCopperWrite => (_flags & CopperWriteFlag) != 0;
    }

    /// <summary>
    /// Opt-in G1L evidence ledger. It records already committed fixed display
    /// samples and display mutations in canonical order. It neither predicts
    /// ownership nor performs presentation.
    /// </summary>
    internal sealed class AgnusCommittedDisplayLedger
    {
        internal const int A500PalMaximumEventCapacity =
            (AmigaConstants.A500PalCpuCyclesPerFrame /
                AgnusChipSlotScheduler.SlotCycles) + 1024;

        private readonly AgnusCommittedDisplayEvent[] _events;
        private long _frameStartCycle;
        private long _frameStopCycle;
        private int _count;
        private bool _overflowed;

        public AgnusCommittedDisplayLedger(
            int capacity = A500PalMaximumEventCapacity)
        {
            if (capacity <= 0 || capacity > A500PalMaximumEventCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _events = new AgnusCommittedDisplayEvent[capacity];
        }

        public int Count => _count;

        public int Capacity => _events.Length;

        public int ReservedBytes => _events.Length * 24;

        public bool Overflowed => _overflowed;

        public long FrameStartCycle => _frameStartCycle;

        public long FrameStopCycle => _frameStopCycle;

        public void Reset(long frameStartCycle, long frameStopCycle)
        {
            if (frameStartCycle < 0 || frameStopCycle <= frameStartCycle)
            {
                throw new ArgumentOutOfRangeException(nameof(frameStopCycle));
            }

            _frameStartCycle = frameStartCycle;
            _frameStopCycle = frameStopCycle;
            _count = 0;
            _overflowed = false;
        }

        public bool Append(in AgnusCommittedDisplayEvent entry)
        {
            if (entry.Cycle < _frameStartCycle || entry.Cycle >= _frameStopCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entry),
                    "A committed display event must belong to the active frame.");
            }

            if (_count > 0 && entry.Cycle < _events[_count - 1].Cycle)
            {
                ref readonly var previous = ref _events[_count - 1];
                throw new InvalidOperationException(
                    $"Committed display events must be appended in canonical chronological order: " +
                    $"previous={previous.Kind}@{previous.Cycle}/reg=0x{previous.Register:X3}/" +
                    $"row={previous.Row}/channel={previous.Channel}/word={previous.Word}, " +
                    $"next={entry.Kind}@{entry.Cycle}/reg=0x{entry.Register:X3}/" +
                    $"row={entry.Row}/channel={entry.Channel}/word={entry.Word}, count={_count}, " +
                    $"frame=[{_frameStartCycle},{_frameStopCycle}).");
            }

            if (_count >= _events.Length)
            {
                _overflowed = true;
                return false;
            }

            _events[_count++] = entry;
            return true;
        }

        public ref readonly AgnusCommittedDisplayEvent GetEvent(int index)
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _events[index];
        }
    }
}
