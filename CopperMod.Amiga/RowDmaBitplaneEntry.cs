namespace CopperMod.Amiga
{
    internal readonly struct RowDmaBitplaneEntry
    {
        public RowDmaBitplaneEntry(
            long cycle,
            int plane,
            int word,
            int slot,
            uint address,
            bool rowPresent)
        {
            Cycle = cycle;
            Plane = plane;
            Word = word;
            Slot = slot;
            Address = address;
            RowPresent = rowPresent;
        }

        public long Cycle { get; }

        public int Plane { get; }

        public int Word { get; }

        public int Slot { get; }

        public uint Address { get; }

        public bool RowPresent { get; }
    }
}
