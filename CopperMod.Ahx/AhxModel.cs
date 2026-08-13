namespace CopperMod.Ahx;

internal sealed record AhxModule(int Version, int SpeedMultiplier, int Restart, int[] SubSongs, int TrackLength,
    AhxPosition[] Positions, AhxStep[][] Tracks, AhxInstrument[] Instruments, string Title, string[] InstrumentNames);
internal readonly record struct AhxPosition(byte[] Tracks, sbyte[] Transposes);
internal readonly record struct AhxStep(int Note, int Instrument, int Command, int Parameter);
internal sealed record AhxInstrument(int Volume, int WaveLength, int AttackLength, int AttackVolume,
    int DecayLength, int DecayVolume, int SustainLength, int ReleaseLength, int ReleaseVolume,
    int VibratoDelay, int VibratoDepth, int VibratoSpeed, int SquareLower, int SquareUpper,
    int SquareSpeed, int PlaylistSpeed, AhxPerfStep[] Playlist, int FilterLower, int FilterUpper,
    int FilterSpeed, int HardCutReleaseFrames, bool HardCutRelease);
internal readonly record struct AhxPerfStep(int Fx1, int Fx2, int Waveform, bool Fixed, int Note, int Param1, int Param2);

