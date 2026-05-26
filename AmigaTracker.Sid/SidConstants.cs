namespace AmigaTracker.Sid
{
    internal static class SidConstants
    {
        public const uint PsidMagic = 0x50534944;
        public const uint RsidMagic = 0x52534944;
        public const int V1HeaderLength = 0x76;
        public const int V2HeaderLength = 0x7C;
        public const int MaxSubSongs = 256;
        public const int MaxSidChips = 3;
        public const ushort DefaultSidBaseAddress = 0xD400;
        public const ushort DefaultBankRegister = 0x0037;
        public const double PalCpuClock = 985248.0;
        public const double NtscCpuClock = 1022727.0;
        public const int PalCyclesPerFrame = 19656;
        public const int NtscCyclesPerFrame = 17095;
        public const int PalRefreshHz = 50;
        public const int NtscRefreshHz = 60;
        public const int CiaTimerRefreshHz = 60;
    }
}
