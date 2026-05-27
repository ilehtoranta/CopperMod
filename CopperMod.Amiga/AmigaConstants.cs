namespace CopperMod.Amiga
{
    internal static class AmigaConstants
    {
        public const double A500PalCpuClockHz = 7_093_789.2;
        public const double A500PalCiaClockHz = 709_378.92;
        public const double A500PalPaulaClockHz = 3_546_895.0;
        public const double A500PalVBlankHz = 50.0;
        public const ushort IntreqVerticalBlank = 0x0020;
        public const int A500PalRasterLines = 312;
        public const int PaulaChannelCount = 4;
        public const int DefaultChipRamSize = 2 * 1024 * 1024;
        public const int A500BootChipRamSize = 512 * 1024;
        public const int PalLowResWidth = 320;
        public const int PalLowResHeight = 256;
    }
}
