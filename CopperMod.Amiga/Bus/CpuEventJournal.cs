/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Bus
{
    internal enum CpuJournalInstructionPhase : byte
    {
        Operand,
        Retirement,
        ExceptionBoundary
    }

    [Flags]
    internal enum CpuJournalDependencyFlags : byte
    {
        None = 0,
        MemoryWrite = 1 << 0,
        OverlapBarrier = 1 << 1,
        CodePageBarrier = 1 << 2,
        LongWordFirstHalf = 1 << 3,
        LongWordSecondHalf = 1 << 4
    }

    internal struct CpuJournalEvent
    {
        public long RequestedCycle;
        public long GrantedCycle;
        public long CompletedCycle;
        public ulong Sequence;
        public uint Address;
        public uint Value;
        public AmigaBusAccessTarget Target;
        public AmigaBusAccessKind Kind;
        public AmigaBusAccessSize Size;
        public CpuJournalInstructionPhase Phase;
        public CpuJournalDependencyFlags Dependencies;
        public bool IsWrite;
        public bool Committed;
    }

    /// <summary>
    /// Fixed-capacity chronological CPU event ring. Storage is allocated once;
    /// resetting only advances indices and never clears the backing array.
    /// </summary>
    internal sealed class CpuEventJournal
    {
        public const int DefaultCapacity = 256;

        private readonly CpuJournalEvent[] _entries;
        private int _head;
        private int _count;
        private ulong _nextSequence;

        public CpuEventJournal(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entries = new CpuJournalEvent[capacity];
        }

        public int Capacity => _entries.Length;

        public int Count => _count;

        public int AvailableCount => _entries.Length - _count;

        public bool IsFull => _count == _entries.Length;

        public long EnqueuedEvents { get; private set; }

        public long CommittedEvents { get; private set; }

        public long FullBarriers { get; private set; }

        public long Resets { get; private set; }

        public bool TryEnqueue(
            long requestedCycle,
            CpuJournalInstructionPhase phase,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessKind kind,
            AmigaBusAccessSize size,
            bool isWrite,
            uint value,
            CpuJournalDependencyFlags dependencies,
            out ulong sequence)
        {
            if (requestedCycle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedCycle));
            }

            if (IsFull)
            {
                FullBarriers++;
                sequence = 0;
                return false;
            }

            if (_count != 0)
            {
                var tailIndex = (_head + _count - 1) % _entries.Length;
                if (requestedCycle < _entries[tailIndex].RequestedCycle)
                {
                    throw new InvalidOperationException(
                        "CPU journal events must be enqueued in nondecreasing requested-cycle order.");
                }
            }

            sequence = ++_nextSequence;
            var index = (_head + _count) % _entries.Length;
            _entries[index] = new CpuJournalEvent
            {
                RequestedCycle = requestedCycle,
                GrantedCycle = -1,
                CompletedCycle = -1,
                Sequence = sequence,
                Address = address,
                Value = value,
                Target = target,
                Kind = kind,
                Size = size,
                Phase = phase,
                Dependencies = dependencies,
                IsWrite = isWrite,
                Committed = false
            };
            _count++;
            EnqueuedEvents++;
            return true;
        }

        public ref CpuJournalEvent Peek()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("The CPU event journal is empty.");
            }

            return ref _entries[_head];
        }

        public bool HasPendingOverlap(uint address, int byteCount)
        {
            var end = (ulong)address + (uint)byteCount;
            for (var offset = 0; offset < _count; offset++)
            {
                ref var entry = ref _entries[(_head + offset) % _entries.Length];
                var entryBytes = entry.Size == AmigaBusAccessSize.Long ? 4u :
                    entry.Size == AmigaBusAccessSize.Word ? 2u : 1u;
                var entryEnd = (ulong)entry.Address + entryBytes;
                if ((ulong)address < entryEnd && (ulong)entry.Address < end)
                {
                    return true;
                }
            }

            return false;
        }

        public void CommitHead(long grantedCycle, long completedCycle)
        {
            ref var entry = ref Peek();
            if (grantedCycle < entry.RequestedCycle || completedCycle < grantedCycle)
            {
                throw new InvalidOperationException("A CPU journal event cannot commit before it was requested or granted.");
            }

            entry.GrantedCycle = grantedCycle;
            entry.CompletedCycle = completedCycle;
            entry.Committed = true;
            _head = (_head + 1) % _entries.Length;
            _count--;
            CommittedEvents++;
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _nextSequence = 0;
            Resets++;
        }
    }
}
