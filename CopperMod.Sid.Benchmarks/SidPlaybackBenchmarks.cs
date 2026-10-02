using BenchmarkDotNet.Attributes;

namespace CopperMod.Sid.Benchmarks;

[MemoryDiagnoser]
public class SidPlaybackBenchmarks
{
    private SidSystem _sid = null!;
    private SidSampleClock _clock = null!;
    private long _sampleIndex;

    [Params(44100, 48000)] public int SampleRate { get; set; }
    [Params(SidEmulationProfile.Balanced, SidEmulationProfile.ReferenceMeasured)]
    public SidEmulationProfile Profile { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sid = new SidSystem([new SidChipPlacement(0, 0xd400)], SidChipModel.Mos6581, sidEmulationProfile: Profile);
        _sid.ConfigureOutput(SampleRate);
        _clock = new SidSampleClock(SidConstants.PalCpuCyclesPerSecond, SampleRate);
        for (var voice = 0; voice < 3; ++voice)
        {
            var address = (ushort)(0xd400 + 7 * voice);
            _sid.TryWrite(address, 0x35, 0);
            _sid.TryWrite((ushort)(address + 1), (byte)(0x18 + voice * 8), 0);
            _sid.TryWrite((ushort)(address + 3), 8, 0);
            _sid.TryWrite((ushort)(address + 4), (byte)((0x20 << voice) | 1), 0);
            _sid.TryWrite((ushort)(address + 6), 0xf0, 0);
        }
        _sid.TryWrite(0xd416, 0x80, 0);
        _sid.TryWrite(0xd417, 0xf7, 0);
        _sid.TryWrite(0xd418, 0x1f, 0);
    }

    [Benchmark(OperationsPerInvoke = 1024)]
    public float Render1024HostSamples()
    {
        float sum = 0;
        for (var i = 0; i < 1024; ++i)
        {
            var index = ++_sampleIndex;
            sum += _sid.RenderSample(_clock.GetSampleTargetCycle(index), _clock.GetSampleFractionalCycle(index));
        }
        return sum;
    }
}
