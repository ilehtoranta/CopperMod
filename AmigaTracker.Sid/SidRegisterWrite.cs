namespace AmigaTracker.Sid
{
    internal readonly struct SidRegisterWrite
    {
        public SidRegisterWrite(long cycle, int chipIndex, byte register, byte value)
        {
            Cycle = cycle;
            ChipIndex = chipIndex;
            Register = register;
            Value = value;
        }

        public long Cycle { get; }

        public int ChipIndex { get; }

        public byte Register { get; }

        public byte Value { get; }
    }
}
