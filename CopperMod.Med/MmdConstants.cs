namespace CopperMod.Med
{
    internal static class MmdConstants
    {
        public const uint Mmd0 = 0x4D4D4430;
        public const uint Mmd1 = 0x4D4D4431;
        public const uint Mmd2 = 0x4D4D4432;
        public const uint Mmd3 = 0x4D4D4433;

        public const int HeaderLength = 52;
        public const int SongLength = 788;
        public const int MaxLegacyInstruments = 63;

        public const byte FlagFilterOn = 0x01;
        public const byte FlagVolHex = 0x10;
        public const byte FlagStSlide = 0x20;
        public const byte Flag8Channel = 0x40;
        public const byte FlagSlowHq = 0x80;

        public const byte Flag2Bpm = 0x20;
        public const byte Flag2Mix = 0x80;

        public const uint Flag3Stereo = 0x1;
        public const uint Flag3FreePan = 0x2;

        public const byte InstrFlagLoop = 0x01;
        public const byte InstrFlagPingPong = 0x08;

        public const int OutputStd = 0;

        public const double PalCiaHz = 709379.0;
        public const double PalScanlineHz = 15625.0;
        public const double PalPaulaClock = 3546895.0;
        public const double PalTimerDiv = 470000.0;
        public const double PalBpmDiv = 3546895.0 / 2.0;
        public const double PaulaDmaStartDelaySeconds = 2.0 / PalScanlineHz;
    }
}
