/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Diagnostics
{
    internal readonly struct CustomRegisterWrite
    {
        public CustomRegisterWrite(long cycle, ushort address, ushort value)
        {
            Cycle = cycle;
            Address = address;
            Value = value;
        }

        public long Cycle { get; }

        public ushort Address { get; }

        public ushort Value { get; }
    }

    internal sealed class BoundedWriteLog : IReadOnlyList<CustomRegisterWrite>
    {
        private readonly CustomRegisterWrite[] _buffer;
        private int _start;
        private int _count;

        public BoundedWriteLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new CustomRegisterWrite[capacity];
        }

        public int Count => _count;

        public CustomRegisterWrite this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _buffer[(_start + index) % _buffer.Length];
            }
        }

        public void Add(CustomRegisterWrite write)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = write;
                _count++;
                return;
            }

            _buffer[_start] = write;
            _start = (_start + 1) % _buffer.Length;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public IEnumerator<CustomRegisterWrite> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal readonly struct CustomRegisterRead
    {
        public CustomRegisterRead(
            ushort address,
            ushort value,
            long requestedCycle,
            long grantedCycle,
            long completedCycle,
            long sampleCycle,
            AmigaBusAccessKind kind)
        {
            Address = address;
            Value = value;
            RequestedCycle = requestedCycle;
            GrantedCycle = grantedCycle;
            CompletedCycle = completedCycle;
            SampleCycle = sampleCycle;
            Kind = kind;
        }

        public ushort Address { get; }

        public ushort Value { get; }

        public long RequestedCycle { get; }

        public long GrantedCycle { get; }

        public long CompletedCycle { get; }

        public long SampleCycle { get; }

        public AmigaBusAccessKind Kind { get; }
    }

    internal sealed class BoundedReadLog : IReadOnlyList<CustomRegisterRead>
    {
        private readonly CustomRegisterRead[] _buffer;
        private int _start;
        private int _count;

        public BoundedReadLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new CustomRegisterRead[capacity];
        }

        public int Count => _count;

        public CustomRegisterRead this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _buffer[(_start + index) % _buffer.Length];
            }
        }

        public void Add(CustomRegisterRead read)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = read;
                _count++;
                return;
            }

            _buffer[_start] = read;
            _start = (_start + 1) % _buffer.Length;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public IEnumerator<CustomRegisterRead> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal readonly struct CpuChipRamWriteTrace
    {
        public CpuChipRamWriteTrace(uint address, uint value, M68kOperandSize size, long cycle)
        {
            Address = address;
            Value = value;
            Size = size;
            Cycle = cycle;
        }

        public uint Address { get; }

        public uint Value { get; }

        public M68kOperandSize Size { get; }

        public long Cycle { get; }
    }

    internal sealed class BoundedCpuChipRamWriteTraceLog : IReadOnlyList<CpuChipRamWriteTrace>
    {
        private readonly CpuChipRamWriteTrace[] _buffer;
        private int _start;
        private int _count;

        public BoundedCpuChipRamWriteTraceLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new CpuChipRamWriteTrace[capacity];
        }

        public int Count => _count;

        public CpuChipRamWriteTrace this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _buffer[(_start + index) % _buffer.Length];
            }
        }

        public void Add(CpuChipRamWriteTrace trace)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = trace;
                _count++;
                return;
            }

            _buffer[_start] = trace;
            _start = (_start + 1) % _buffer.Length;
        }

        public IEnumerator<CpuChipRamWriteTrace> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
