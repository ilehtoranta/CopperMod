namespace CopperMod.Sid
{
    internal sealed class C64ClockProfile
    {
        private C64ClockProfile(
            string name,
            int cpuCyclesPerSecond,
            int cyclesPerFrame,
            int refreshRateHz,
            int rasterLines,
            int cyclesPerRasterLine)
        {
            Name = name;
            CpuCyclesPerSecond = cpuCyclesPerSecond;
            CyclesPerFrame = cyclesPerFrame;
            RefreshRateHz = refreshRateHz;
            RasterLines = rasterLines;
            CyclesPerRasterLine = cyclesPerRasterLine;
        }

        public string Name { get; }

        public int CpuCyclesPerSecond { get; }

        public int CyclesPerFrame { get; }

        public int RefreshRateHz { get; }

        public int RasterLines { get; }

        public int CyclesPerRasterLine { get; }

        public static C64ClockProfile FromSidClock(SidClock clock)
        {
            return clock == SidClock.Ntsc
                ? new C64ClockProfile("NTSC", SidConstants.NtscCpuCyclesPerSecond, SidConstants.NtscCyclesPerFrame, SidConstants.NtscRefreshHz, rasterLines: 263, cyclesPerRasterLine: 65)
                : new C64ClockProfile("PAL", SidConstants.PalCpuCyclesPerSecond, SidConstants.PalCyclesPerFrame, SidConstants.PalRefreshHz, rasterLines: 312, cyclesPerRasterLine: 63);
        }
    }
}
