namespace CopperMod.Amiga
{
    internal static class AmigaConstants
    {
        public const int A500PalPaulaTicksPerSecond = 3_546_895;
        public const int A500PalCpuCyclesPerColorClock = 2;
        public const int A500PalCpuCyclesPerCiaTick = A500PalCpuCyclesPerColorClock * 5;
        public const int A500PalCpuCyclesPerSecond = A500PalPaulaTicksPerSecond * A500PalCpuCyclesPerColorClock;
        public const double A500PalPaulaClockHz = A500PalPaulaTicksPerSecond;
        public const double A500PalCpuClockHz = A500PalCpuCyclesPerSecond;
        public const double A500PalCiaClockHz = A500PalCpuClockHz / A500PalCpuCyclesPerCiaTick;
        public const int A500PalColorClocksPerRasterLine = 227;
        public const int A500PalRasterLines = 312;
        public const int A500PalCpuCyclesPerRasterLine = A500PalCpuCyclesPerColorClock * A500PalColorClocksPerRasterLine;
        public const int A500PalCpuCyclesPerFrame = A500PalCpuCyclesPerRasterLine * A500PalRasterLines;
        public const double A500PalVBlankHz = A500PalCpuClockHz / A500PalCpuCyclesPerFrame;
        public const ushort IntreqPorts = 0x0008;
        public const ushort IntreqCopper = 0x0010;
        public const ushort IntreqVerticalBlank = 0x0020;
        public const ushort IntreqBlitter = 0x0040;
        public const ushort IntreqExternal = 0x2000;
        public const int PaulaChannelCount = 4;
        public const int DefaultChipRamSize = 2 * 1024 * 1024;
        public const int MaxChipRamSize = 8 * 1024 * 1024;
        public const int A500BootChipRamSize = 512 * 1024;
        public const int A500BootPseudoFastRamSize = 512 * 1024;
        public const uint A500BootPseudoFastRamBase = 0x00C0_0000;
        public const int PalLowResStandardWidth = 320;
        public const int PalLowResStandardHeight = 256;
        public const int PalLowResOverscanBorderX = 16;
        public const int PalLowResOverscanBorderY = 16;
        public const int PalLowResWidth = PalLowResStandardWidth + (PalLowResOverscanBorderX * 2);
        public const int PalLowResHeight = PalLowResStandardHeight + (PalLowResOverscanBorderY * 2);
        public const int PalHighResWidth = PalLowResWidth * 2;
        public const int PalHighResHeight = PalLowResHeight * 2;
    }
}
