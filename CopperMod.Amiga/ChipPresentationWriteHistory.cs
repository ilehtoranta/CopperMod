using System;

namespace CopperMod.Amiga
{
    internal sealed class ChipPresentationWriteHistory
    {
        private const int InitialCapturedWriteCapacity = 65536;
        private readonly int[] _headByOffset;
        private readonly int[] _tailByOffset;
        private readonly int[] _touchedOffsets;
        private int[] _nextByWrite;
        private ChipByteWrite[] _writes;
        private readonly bool[] _outOfOrderByOffset;
        private int _touchedOffsetCount;
        private int _writeCount;
        private long _latestWriteCycle = long.MinValue;
        private bool _hasOutOfOrderWrites;

        public ChipPresentationWriteHistory(int chipRamSize)
        {
            if (chipRamSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size must be positive.");
            }

            _headByOffset = new int[chipRamSize];
            _tailByOffset = new int[chipRamSize];
            _touchedOffsets = new int[chipRamSize];
            _nextByWrite = new int[InitialCapturedWriteCapacity];
            _writes = new ChipByteWrite[InitialCapturedWriteCapacity];
            _outOfOrderByOffset = new bool[chipRamSize];
            Array.Fill(_headByOffset, -1);
            Array.Fill(_tailByOffset, -1);
        }

        public void RecordByte(int offset, byte oldValue, byte newValue, long cycle)
        {
            if ((uint)offset >= (uint)_headByOffset.Length)
            {
                return;
            }

            var tailIndex = _tailByOffset[offset];
            if (tailIndex >= 0 && cycle < _writes[tailIndex].Cycle)
            {
                _outOfOrderByOffset[offset] = true;
                _hasOutOfOrderWrites = true;
            }

            oldValue = GetValueAtCycle(offset, cycle, oldValue);
            if (oldValue == newValue)
            {
                return;
            }

            if (_writeCount == _writes.Length)
            {
                GrowWriteCapacity();
            }

            if (_headByOffset[offset] < 0)
            {
                _touchedOffsets[_touchedOffsetCount++] = offset;
                _headByOffset[offset] = _writeCount;
            }
            else
            {
                _nextByWrite[_tailByOffset[offset]] = _writeCount;
            }

            _tailByOffset[offset] = _writeCount;
            _writes[_writeCount] = new ChipByteWrite(cycle, oldValue, newValue);
            _nextByWrite[_writeCount] = -1;
            _writeCount++;
            _latestWriteCycle = Math.Max(_latestWriteCycle, cycle);
        }

        private void GrowWriteCapacity()
        {
            var newCapacity = checked(_writes.Length * 2);
            Array.Resize(ref _writes, newCapacity);
            Array.Resize(ref _nextByWrite, newCapacity);
        }

        public bool HasWrites => _writeCount != 0;

        public bool MayNeedPresentationRead(long cycle)
        {
            return _writeCount != 0 && (_hasOutOfOrderWrites || _latestWriteCycle > cycle);
        }

        public bool NeedsPresentationRead(int offset, long cycle)
        {
            if ((uint)offset >= (uint)_headByOffset.Length)
            {
                return false;
            }

            var headIndex = _headByOffset[offset];
            if (headIndex < 0)
            {
                return false;
            }

            if (_outOfOrderByOffset[offset])
            {
                return true;
            }

            var tailIndex = _tailByOffset[offset];
            return tailIndex >= 0 && _writes[tailIndex].Cycle > cycle;
        }

        public byte ReadByte(byte[] currentMemory, int offset, long cycle)
        {
            if ((uint)offset >= (uint)currentMemory.Length)
            {
                return 0;
            }

            if ((uint)offset >= (uint)_headByOffset.Length)
            {
                return currentMemory[offset];
            }

            return GetValueAtCycle(offset, cycle, currentMemory[offset]);
        }

        private byte GetValueAtCycle(int offset, long cycle, byte fallbackValue)
        {
            var index = _headByOffset[offset];
            if (index < 0)
            {
                return fallbackValue;
            }

            if (!_outOfOrderByOffset[offset])
            {
                var tailIndex = _tailByOffset[offset];
                if (tailIndex >= 0 && _writes[tailIndex].Cycle <= cycle)
                {
                    return fallbackValue;
                }

                while (index >= 0)
                {
                    var write = _writes[index];
                    if (write.Cycle > cycle)
                    {
                        return write.OldValue;
                    }

                    index = _nextByWrite[index];
                }

                return fallbackValue;
            }

            var latestPastCycle = long.MinValue;
            var latestPastValue = (byte)0;
            var hasPast = false;
            var earliestFutureCycle = long.MaxValue;
            var earliestFutureOldValue = (byte)0;
            var hasFuture = false;
            while (index >= 0)
            {
                var write = _writes[index];
                if (write.Cycle <= cycle)
                {
                    if (!hasPast || write.Cycle >= latestPastCycle)
                    {
                        latestPastCycle = write.Cycle;
                        latestPastValue = write.NewValue;
                        hasPast = true;
                    }
                }
                else if (!hasFuture || write.Cycle < earliestFutureCycle)
                {
                    earliestFutureCycle = write.Cycle;
                    earliestFutureOldValue = write.OldValue;
                    hasFuture = true;
                }

                index = _nextByWrite[index];
            }

            if (hasPast)
            {
                return latestPastValue;
            }

            return hasFuture ? earliestFutureOldValue : fallbackValue;
        }

        public void Clear()
        {
            for (var i = 0; i < _touchedOffsetCount; i++)
            {
                var offset = _touchedOffsets[i];
                _headByOffset[offset] = -1;
                _tailByOffset[offset] = -1;
                _outOfOrderByOffset[offset] = false;
            }

            _touchedOffsetCount = 0;
            _writeCount = 0;
            _latestWriteCycle = long.MinValue;
            _hasOutOfOrderWrites = false;
        }

        private readonly struct ChipByteWrite
        {
            public ChipByteWrite(long cycle, byte oldValue, byte newValue)
            {
                Cycle = cycle;
                OldValue = oldValue;
                NewValue = newValue;
            }

            public long Cycle { get; }

            public byte OldValue { get; }

            public byte NewValue { get; }
        }
    }
}
