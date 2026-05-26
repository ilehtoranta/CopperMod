namespace AmigaTracker.Sid
{
    internal sealed class VicII
    {
        private readonly C64ClockProfile _clock;
        private byte _irqFlags;
        private byte _irqMask;
        private ushort _rasterCompare;
        private int _rasterLine;
        private int _cycleInFrame;

        public VicII(C64ClockProfile clock)
        {
            _clock = clock;
        }

        public void Reset()
        {
            _irqFlags = 0;
            _irqMask = 0;
            _rasterCompare = 0;
            _rasterLine = 0;
            _cycleInFrame = 0;
        }

        public byte Read(byte register)
        {
            return (register & 0x3F) switch
            {
                0x11 => (byte)((_rasterLine & 0x100) >> 1),
                0x12 => (byte)_rasterLine,
                0x19 => ReadIrqFlags(),
                0x1A => _irqMask,
                _ => 0
            };
        }

        public void Write(byte register, byte value)
        {
            switch (register & 0x3F)
            {
                case 0x11:
                    _rasterCompare = (ushort)((_rasterCompare & 0x00FF) | ((value & 0x80) << 1));
                    break;
                case 0x12:
                    _rasterCompare = (ushort)((_rasterCompare & 0x0100) | value);
                    break;
                case 0x19:
                    _irqFlags &= (byte)~(value & 0x0F);
                    break;
                case 0x1A:
                    _irqMask = value;
                    break;
            }
        }

        public bool Tick()
        {
            _cycleInFrame++;
            var cyclesPerLine = _clock.CyclesPerFrame / 312;
            if (_cycleInFrame % cyclesPerLine != 0)
            {
                return false;
            }

            _rasterLine++;
            if (_rasterLine >= 312)
            {
                _rasterLine = 0;
                _cycleInFrame = 0;
            }

            if (_rasterLine == _rasterCompare)
            {
                _irqFlags |= 0x01;
            }

            return InterruptLineAsserted;
        }

        private byte ReadIrqFlags()
        {
            return (byte)(_irqFlags | (InterruptLineAsserted ? 0x80 : 0x00));
        }

        private bool InterruptLineAsserted => (_irqFlags & _irqMask & 0x0F) != 0;
    }
}
