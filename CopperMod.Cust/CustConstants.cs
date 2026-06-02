using CopperMod.Amiga;

namespace CopperMod.Cust
{
    internal static class CustConstants
    {
        public const double A500PalCpuClockHz = AmigaConstants.A500PalCpuClockHz;
        public const double A500PalCiaClockHz = AmigaConstants.A500PalCiaClockHz;
        public const double A500PalPaulaClockHz = AmigaConstants.A500PalPaulaClockHz;
        public const double A500PalVBlankHz = AmigaConstants.A500PalVBlankHz;
        public const int PaulaChannelCount = AmigaConstants.PaulaChannelCount;
        public const int DefaultChipRamSize = AmigaConstants.DefaultChipRamSize;
        public const uint DefaultModuleBaseAddress = 0x0004_0000;
        public const uint HostBlockAddress = 0x0000_1000;
        public const uint ExternalAllocationLimitAddress = 0x001F_F000;
        public const uint StackTopAddress = AmigaConstants.A500BootPseudoFastRamBase + 0x0001_0000;
        public const uint HostCallbackBaseAddress = 0x00F0_0000;
        public const long SubroutineCycleBudget = 1_000_000;
        public const int SubroutineInstructionBudget = 250_000;
        public const int SubroutineWallClockBudgetMilliseconds = 250;
        public const int MaxRenderFramesPerTick = 1_000_000;

        public const uint DtpPlayerVersion = 0x8000_4455;
        public const uint DtpCheck = 0x8000_4456;
        public const uint DtpInterrupt = 0x8000_445E;
        public const uint DtpCheck2 = 0x8000_445F;
        public const uint DtpSubSongRange = 0x8000_4462;
        public const uint DtpInitPlayer = 0x8000_4463;
        public const uint DtpEndPlayer = 0x8000_4464;
        public const uint DtpInitSound = 0x8000_4465;
        public const uint DtpEndSound = 0x8000_4466;
        public const uint DtpVolume = 0x8000_4467;
        public const uint DtpBalance = 0x8000_4468;
        public const uint DtpVoices = 0x8000_4469;
        public const uint DtpModuleInfo = 0x8000_446F;
        public const uint DtpSampleInfo = 0x8000_4470;
        public const uint DtpFlags = 0x8000_4473;
        public const uint TagDone = 0;

        public const int DtgDosBaseOffset = 0x04;
        public const int DtgExecBaseOffset = 0x08;
        public const int DtgPathBufferOffset = 0x20;
        public const int DtgSoundNumberOffset = 0x2C;
        public const int DtgSoundVolumeOffset = 0x2E;
        public const int DtgSoundLeftBalanceOffset = 0x30;
        public const int DtgSoundRightBalanceOffset = 0x32;
        public const int DtgResetPathOffset = 0x40;
        public const int DtgAudioAllocOffset = 0x48;
        public const int DtgGetListDataOffset = 0x4C;
        public const int DtgAudioFreeOffset = 0x50;
        public const int DtgSongEndOffset = 0x54;
        public const int DtgTimerOffset = 0x64;
    }
}
