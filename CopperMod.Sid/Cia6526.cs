namespace CopperMod.Sid
{
    internal sealed class Cia6526
    {
        private ushort _timerALatch = 0xFFFF;
        private ushort _timerA = 0xFFFF;
        private ushort _timerBLatch = 0xFFFF;
        private ushort _timerB = 0xFFFF;
        private byte _controlA;
        private byte _controlB;
        private byte _interruptMask;
        private byte _interruptData;

        public void Reset(bool defaultTimerA60Hz)
        {
            _timerALatch = defaultTimerA60Hz ? (ushort)0x4025 : (ushort)0xFFFF;
            _timerA = _timerALatch;
            _timerBLatch = 0xFFFF;
            _timerB = 0xFFFF;
            _controlA = defaultTimerA60Hz ? (byte)0x11 : (byte)0x00;
            _controlB = 0;
            _interruptMask = defaultTimerA60Hz ? (byte)0x01 : (byte)0x00;
            _interruptData = 0;
        }

        public byte Read(byte register)
        {
            return (register & 0x0F) switch
            {
                0x04 => (byte)_timerA,
                0x05 => (byte)(_timerA >> 8),
                0x06 => (byte)_timerB,
                0x07 => (byte)(_timerB >> 8),
                0x0D => ReadInterruptData(),
                0x0E => _controlA,
                0x0F => _controlB,
                _ => 0
            };
        }

        public void Write(byte register, byte value)
        {
            switch (register & 0x0F)
            {
                case 0x04:
                    _timerALatch = (ushort)((_timerALatch & 0xFF00) | value);
                    break;
                case 0x05:
                    _timerALatch = (ushort)((_timerALatch & 0x00FF) | (value << 8));
                    break;
                case 0x06:
                    _timerBLatch = (ushort)((_timerBLatch & 0xFF00) | value);
                    break;
                case 0x07:
                    _timerBLatch = (ushort)((_timerBLatch & 0x00FF) | (value << 8));
                    break;
                case 0x0D:
                    if ((value & 0x80) != 0)
                    {
                        _interruptMask |= (byte)(value & 0x7F);
                    }
                    else
                    {
                        _interruptMask &= (byte)~(value & 0x7F);
                    }

                    break;
                case 0x0E:
                    _controlA = value;
                    if ((value & 0x10) != 0)
                    {
                        _timerA = _timerALatch;
                        _controlA &= 0xEF;
                    }

                    break;
                case 0x0F:
                    _controlB = value;
                    if ((value & 0x10) != 0)
                    {
                        _timerB = _timerBLatch;
                        _controlB &= 0xEF;
                    }

                    break;
            }
        }

        public bool Tick()
        {
            if ((_controlA & 0x01) != 0)
            {
                if (_timerA == 0)
                {
                    _timerA = _timerALatch;
                    _interruptData |= 0x01;
                    if ((_controlA & 0x08) != 0)
                    {
                        _controlA &= 0xFE;
                    }
                }
                else
                {
                    _timerA--;
                }
            }

            if ((_controlB & 0x01) != 0)
            {
                if (_timerB == 0)
                {
                    _timerB = _timerBLatch;
                    _interruptData |= 0x02;
                    if ((_controlB & 0x08) != 0)
                    {
                        _controlB &= 0xFE;
                    }
                }
                else
                {
                    _timerB--;
                }
            }

            return InterruptLineAsserted;
        }

        private byte ReadInterruptData()
        {
            var value = (byte)(_interruptData | (InterruptLineAsserted ? 0x80 : 0x00));
            _interruptData = 0;
            return value;
        }

        private bool InterruptLineAsserted => (_interruptData & _interruptMask & 0x7F) != 0;
    }
}
