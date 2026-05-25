namespace AmigaTracker.Sid
{
    internal sealed class C64ClockProfile
    {
        private C64ClockProfile(string name, double cpuClockHz, int cyclesPerFrame, int refreshRateHz)
        {
            Name = name;
            CpuClockHz = cpuClockHz;
            CyclesPerFrame = cyclesPerFrame;
            RefreshRateHz = refreshRateHz;
        }

        public string Name { get; }

        public double CpuClockHz { get; }

        public int CyclesPerFrame { get; }

        public int RefreshRateHz { get; }

        public static C64ClockProfile FromSidClock(SidClock clock)
        {
            return clock == SidClock.Ntsc
                ? new C64ClockProfile("NTSC", SidConstants.NtscCpuClock, SidConstants.NtscCyclesPerFrame, SidConstants.NtscRefreshHz)
                : new C64ClockProfile("PAL", SidConstants.PalCpuClock, SidConstants.PalCyclesPerFrame, SidConstants.PalRefreshHz);
        }
    }
}
