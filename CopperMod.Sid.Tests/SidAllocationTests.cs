using CopperMod.Abstractions;
using CopperMod.TestSupport;

namespace CopperMod.Sid.Tests;

public sealed class SidAllocationTests
{
	private const int SampleRate = 44_100;
	private const int Channels = 2;
	private const int WarmupTicks = 120;
	private const int MeasuredTicks = 24;
	private const int MaxFramesPerTick = 4096;

	public static TheoryData<string, int, string> Workloads { get; } = new()
	{
		{ "Commando", 0, SidCorpusFixtures.Commando },
		{ "Great Giana Sisters subtune 5", 4, SidCorpusFixtures.GreatGianaSisters },
		{ "Spijkerhoek (Edwin van Santen)", 0, SidCorpusFixtures.SpijkerhoekVanSanten },
		{ "Spijkerhoek (Rodney Balai)", 0, SidCorpusFixtures.SpijkerhoekBalai },
		{ "Flimbo intro", 0, SidCorpusFixtures.FlimbosQuestIntro },
		{ "Tetris RSID", 0, SidCorpusFixtures.Tetris },
	};

	[Theory]
	[InlineData((int)SidChipModel.Mos6581)]
	[InlineData((int)SidChipModel.Mos8580)]
	public void ReferenceMeasuredCycleRenderingAllocatesZeroBytesAfterWarmup(int modelValue)
	{
		var model = (SidChipModel)modelValue;
		var chip = new SidChip(
			model,
			SidConstants.DefaultSidBaseAddress,
			SidConstants.PalCpuCyclesPerSecond,
			sidEmulationProfile: SidEmulationProfile.ReferenceMeasured);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x20);
		chip.Write(0x02, 0x00);
		chip.Write(0x03, 0x08);
		chip.Write(0x04, 0x31);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x18, 0x0F);
		_ = chip.RenderAndSumFast(1, 8192);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		var before = GC.GetAllocatedBytesForCurrentThread();
		_ = chip.RenderAndSumFast(8193, 65536);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[SidEvidenceTheory("SID_CORPUS_TESTS")]
	[MemberData(nameof(Workloads))]
	public void RenderTickAllocatesZeroBytesAfterWarmup(string name, int subSongIndex, string corpusPath)
	{
		var path = SidCorpusFixtures.Find(corpusPath);

		using var song = new SidFormat().Load(File.ReadAllBytes(path));
		if (subSongIndex != 0)
		{
			var selector = (IModuleSubSongSelector)song;
			Assert.True(subSongIndex < selector.SubSongCount, "SID corpus fixture has too few subtunes.");

			selector.SelectSubSong(subSongIndex);
		}

		var options = new AudioRenderOptions(SampleRate, Channels);
		var buffer = new float[options.GetSampleCount(MaxFramesPerTick)];
		for (var tick = 0; tick < WarmupTicks; tick++)
		{
			RenderOneTick(song, options, buffer);
		}

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var tick = 0; tick < MeasuredTicks; tick++)
		{
			RenderOneTick(song, options, buffer);
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.True(allocated == 0, $"{name} allocated {allocated} bytes during measured SID tick rendering.");
	}

	private static void RenderOneTick(IModuleSong song, AudioRenderOptions options, float[] buffer)
	{
		var frames = song.GetCurrentTickFrameCount(options);
		var samples = options.GetSampleCount(frames);
		if (samples > buffer.Length)
		{
			throw new InvalidOperationException("SID allocation test buffer is too small.");
		}

		_ = song.RenderTick(buffer.AsSpan(0, samples), options);
	}

}
