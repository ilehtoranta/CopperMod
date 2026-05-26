namespace CopperMod.Cust
{
    internal static class CustConstants
    {
        public const double A500PalCpuClockHz = 7_093_789.2;
        public const double A500PalCiaClockHz = 709_378.92;
        public const double A500PalPaulaClockHz = 3_546_895.0;
        public const double A500PalVBlankHz = 50.0;
        public const int PaulaChannelCount = 4;
        public const int DefaultChipRamSize = 2 * 1024 * 1024;
        public const uint DefaultModuleBaseAddress = 0x0004_0000;
        public const uint HostBlockAddress = 0x0000_1000;
        public const uint StackTopAddress = 0x001F_F000;
        public const uint HostCallbackBaseAddress = 0x00F0_0000;
        public const long SubroutineCycleBudget = 1_000_000;
        public const int SubroutineInstructionBudget = 250_000;
        public const int SubroutineWallClockBudgetMilliseconds = 250;
        public const int MaxRenderFramesPerTick = 1_000_000;

        public const uint DtpPlayerVersion = 0x8000_4455;
        public const uint DtpInterrupt = 0x8000_445E;
        public const uint DtpSubSongRange = 0x8000_4462;
        public const uint DtpInitPlayer = 0x8000_4463;
        public const uint DtpEndPlayer = 0x8000_4464;
        public const uint DtpInitSound = 0x8000_4465;
        public const uint DtpEndSound = 0x8000_4466;
        public const uint TagDone = 0;

        public const int DtgSoundNumberOffset = 0x2C;
        public const int DtgAudioAllocOffset = 0x48;
        public const int DtgGetListDataOffset = 0x4C;
        public const int DtgAudioFreeOffset = 0x50;
        public const int DtgSongEndOffset = 0x54;
        public const int DtgTimerOffset = 0x64;
    }
}
