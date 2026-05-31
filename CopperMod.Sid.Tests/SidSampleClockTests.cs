namespace CopperMod.Sid.Tests;

public sealed class SidSampleClockTests
{
	[Theory]
	[MemberData(nameof(FrameCountSequences))]
	public void FrameCountsUseContinuousIntegerScheduler(
		int cpuCyclesPerSecond,
		int sampleRate,
		int tickCycles,
		int[] expectedCounts)
	{
		var clock = new SidSampleClock(cpuCyclesPerSecond, sampleRate);
		var cycle = 0L;
		var previousTarget = 0L;

		foreach (var expected in expectedCounts)
		{
			Assert.Equal(expected, clock.PeekFrameCount(cycle, tickCycles));

			var targets = clock.ConsumeSampleTargets(cycle, tickCycles);
			Assert.Equal(expected, targets.Length);
			foreach (var target in targets)
			{
				Assert.True(target > previousTarget);
				Assert.True(target <= cycle + tickCycles);
				previousTarget = target;
			}

			cycle += tickCycles;
		}
	}

	[Fact]
	public void CumulativeFrameCountMatchesRationalCycleExpectation()
	{
		var clock = new SidSampleClock(SidConstants.PalCpuCyclesPerSecond, sampleRate: 44100);
		var cycle = 0L;
		var renderedFrames = 0L;

		for (var tick = 0; tick < 1000; tick++)
		{
			var targets = clock.ConsumeSampleTargets(cycle, SidConstants.PalCyclesPerFrame);
			renderedFrames += targets.Length;
			cycle += SidConstants.PalCyclesPerFrame;
		}

		Assert.Equal(clock.CountSamplesThroughCycle(cycle), renderedFrames);
	}

	[Fact]
	public void SplitTicksProduceSameTargetCyclesAsWholeTick()
	{
		var whole = new SidSampleClock(SidConstants.PalCpuCyclesPerSecond, sampleRate: 44100);
		var split = new SidSampleClock(SidConstants.PalCpuCyclesPerSecond, sampleRate: 44100);

		var wholeTargets = whole.ConsumeSampleTargets(0, SidConstants.PalCyclesPerFrame);
		var splitTargets = split.ConsumeSampleTargets(0, 1000)
			.Concat(split.ConsumeSampleTargets(1000, 3000))
			.Concat(split.ConsumeSampleTargets(4000, SidConstants.PalCyclesPerFrame - 4000))
			.ToArray();

		Assert.Equal(wholeTargets, splitTargets);
	}

	[Fact]
	public void SampleTargetsUseRoundNearestHalfUpIntegerPolicy()
	{
		var clock = new SidSampleClock(cpuCyclesPerSecond: 10, sampleRate: 4);

		Assert.Equal(3, clock.GetSampleTargetCycle(1));
		Assert.Equal(5, clock.GetSampleTargetCycle(2));
		Assert.Equal(8, clock.GetSampleTargetCycle(3));
		Assert.Equal(1, clock.CountSamplesThroughCycle(3));
		Assert.Equal(2, clock.CountSamplesThroughCycle(5));
	}

	public static IEnumerable<object[]> FrameCountSequences()
	{
		yield return
		[
			SidConstants.PalCpuCyclesPerSecond,
			44100,
			SidConstants.PalCyclesPerFrame,
			new[] { 879, 880, 880, 880, 880, 879, 880, 880, 880, 880 }
		];
		yield return
		[
			SidConstants.PalCpuCyclesPerSecond,
			48000,
			SidConstants.PalCyclesPerFrame,
			new[] { 957, 958, 957, 958, 958, 957, 958, 957, 958, 958 }
		];
		yield return
		[
			SidConstants.NtscCpuCyclesPerSecond,
			44100,
			SidConstants.NtscCyclesPerFrame,
			new[] { 737, 737, 737, 737, 737, 737, 737, 738, 737, 737 }
		];
		yield return
		[
			SidConstants.PalCpuCyclesPerSecond,
			44100,
			(int)SidIntegerMath.DivRoundNearest(SidConstants.PalCpuCyclesPerSecond, SidConstants.CiaTimerRefreshHz),
			new[] { 735, 735, 735, 735, 735, 735, 735, 735, 735, 735 }
		];
	}
}
