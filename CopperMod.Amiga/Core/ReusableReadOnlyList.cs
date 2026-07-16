using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Core
{
    internal sealed class ReusableReadOnlyList<T> : IReadOnlyList<T>
    {
        private T[] _items = Array.Empty<T>();
        private int _count;

        public int Count => _count;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
        }

        public void Reset(T[] items, int count)
        {
            if ((uint)count > (uint)items.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _items = items;
            _count = count;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return _items[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
