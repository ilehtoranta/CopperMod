using System.Reflection;
using CopperMod.Abstractions;
using CopperMod.Sid;
using CopperMod.Sid.Tests;

public sealed class SidPlaybackTimingTests
{
    [Theory]
    [InlineData(44100)] [InlineData(48000)]
    public void SongUsesFractionalPositionsThroughCpuPlayback(int sampleRate)
    {
        using var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
        var machine = (C64Machine)typeof(SidSong).GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(song)!;
        var source = machine.Sid.Chips[0];
        var expected = new SidSystem([new SidChipPlacement(0, source.BaseAddress)], source.Model,
            machine.Clock.CpuCyclesPerSecond, sidEmulationProfile: source.SidEmulationProfile);
        expected.Chips[0].CopyStateFrom(source);
        expected.ResetClock();
        expected.ConfigureOutput(sampleRate);
        var clock = new SidSampleClock(machine.Clock.CpuCyclesPerSecond, sampleRate, machine.Cycle);
        var firstSample = clock.NextSampleIndex;
        var writeCount = song.SidWrites.Count;
        var options = new AudioRenderOptions(sampleRate: sampleRate, channelCount: 1);
        var actual = new float[song.GetCurrentTickFrameCount(options)];
        song.RenderTick(actual, options);
        foreach (var write in song.SidWrites.Skip(writeCount))
            expected.TryWrite((ushort)(source.BaseAddress + write.Register), write.Value, write.Cycle);
        for (var i = 0; i < actual.Length; ++i)
        {
            var index = firstSample + i;
            var sample = expected.RenderSample(clock.GetSampleTargetCycle(index), clock.GetSampleFractionalCycle(index));
            Assert.Equal(Math.Clamp(sample, -0.999f, 0.999f), actual[i]);
        }
        Assert.Contains(actual, sample => Math.Abs(sample) > 0.001);
    }

    [Fact]
    public void ReadingAnotherHostSampleAtTheSameCycleDoesNotClockTheChip()
    {
        var sid = new SidSystem([new SidChipPlacement(0, 0xd400)], SidChipModel.Mos6581);
        sid.TryWrite(0xd400, 0xff, 0); sid.TryWrite(0xd401, 0xff, 0);
        sid.RenderSample(20);
        var before = sid.Chips[0].DebugState.Voices[0];
        sid.RenderSample(20, 0.25);
        var after = sid.Chips[0].DebugState.Voices[0];
        Assert.Equal(before, after);
    }
}
