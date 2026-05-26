using System;
using System.Collections;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal sealed class BoundedSidWriteLog : IReadOnlyList<SidRegisterWrite>
    {
        private readonly SidRegisterWrite[] _items;
        private int _start;
        private int _count;

        public BoundedSidWriteLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new SidRegisterWrite[capacity];
        }

        public int Count => _count;

        public SidRegisterWrite this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[PhysicalIndex(index)];
            }
        }

        public void Add(SidRegisterWrite write)
        {
            if (_count < _items.Length)
            {
                _items[PhysicalIndex(_count)] = write;
                _count++;
                return;
            }

            _items[_start] = write;
            _start = (_start + 1) % _items.Length;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public IEnumerator<SidRegisterWrite> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private int PhysicalIndex(int index)
        {
            return (_start + index) % _items.Length;
        }
    }
}
