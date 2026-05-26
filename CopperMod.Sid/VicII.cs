namespace CopperMod.Sid
{
    internal sealed class VicII
    {
        private readonly C64ClockProfile _clock;
        private readonly byte[] _registers = new byte[0x40];
        private byte _irqFlags;
        private byte _irqMask;
        private ushort _rasterCompare;
        private int _rasterLine;
        private int _rasterCycle;
        private bool _rasterCompareMatched;

        public VicII(C64ClockProfile clock)
        {
            _clock = clock;
        }

        public VicDebugState DebugState => new VicDebugState(
            _rasterLine,
            _rasterCycle,
            _rasterCompare,
            _irqFlags,
            _irqMask,
            InterruptLineAsserted);

        public void Reset()
        {
            System.Array.Clear(_registers);
            _irqFlags = 0;
            _irqMask = 0;
            _rasterCompare = 0;
            _rasterLine = 0;
            _rasterCycle = 0;
            _rasterCompareMatched = false;
            EvaluateRasterCompare();
        }

        public byte Read(byte register)
        {
            return (register & 0x3F) switch
            {
                0x11 => (byte)((_registers[0x11] & 0x7F) | ((_rasterLine & 0x100) >> 1)),
                0x12 => (byte)_rasterLine,
                0x19 => ReadIrqFlags(),
                0x1A => _irqMask,
                _ => _registers[register & 0x3F]
            };
        }

        public void Write(byte register, byte value)
        {
            register = (byte)(register & 0x3F);
            _registers[register] = value;
            switch (register)
            {
                case 0x11:
                    _rasterCompare = (ushort)((_rasterCompare & 0x00FF) | ((value & 0x80) << 1));
                    EvaluateRasterCompareAfterCompareWrite();
                    break;
                case 0x12:
                    _rasterCompare = (ushort)((_rasterCompare & 0x0100) | value);
                    EvaluateRasterCompareAfterCompareWrite();
                    break;
                case 0x19:
                    _irqFlags &= (byte)~(value & 0x0F);
                    break;
                case 0x1A:
                    _irqMask = (byte)(value & 0x0F);
                    EvaluateRasterCompare();
                    break;
            }
        }

        public bool Tick()
        {
            _rasterCycle++;
            if (_rasterCycle >= _clock.CyclesPerRasterLine)
            {
                _rasterCycle = 0;
                _rasterLine++;
                if (_rasterLine >= _clock.RasterLines)
                {
                    _rasterLine = 0;
                }

                if (_rasterLine != _rasterCompare)
                {
                    _rasterCompareMatched = false;
                }

                EvaluateRasterCompare();
            }

            return InterruptLineAsserted;
        }

        private void EvaluateRasterCompareAfterCompareWrite()
        {
            if (_rasterLine != _rasterCompare)
            {
                _rasterCompareMatched = false;
            }

            EvaluateRasterCompare();
        }

        private void EvaluateRasterCompare()
        {
            if (_rasterLine == _rasterCompare && !_rasterCompareMatched)
            {
                _irqFlags |= 0x01;
                _rasterCompareMatched = true;
            }
        }

        private byte ReadIrqFlags()
        {
            return (byte)(_irqFlags | (InterruptLineAsserted ? 0x80 : 0x00));
        }

        private bool InterruptLineAsserted => (_irqFlags & _irqMask & 0x0F) != 0;
    }
}
