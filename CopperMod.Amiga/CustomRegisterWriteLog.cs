using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
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
}
