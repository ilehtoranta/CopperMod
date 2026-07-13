using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidAllocationTests
{
	private const int SampleRate = 44_100;
	private const int Channels = 2;
	private const int WarmupTicks = 120;
	private const int MeasuredTicks = 24;
	private const int MaxFramesPerTick = 4096;

	public static TheoryData<string, int, string[]> Workloads { get; } = new()
	{
		{ "Commando", 0, new[] { "TestTunes", "SID", "Tough", "Commando.sid" } },
		{ "Great Giana Sisters subtune 5", 4, new[] { "TestTunes", "SID", "Tough", "Great_Giana_Sisters.sid" } },
		{ "Spijkerhoek", 0, new[] { "TestTunes", "SID", "Tough", "Spijkerhoek.sid" } },
		{ "Flimbo intro", 0, new[] { "TestTunes", "SID", "Tough", "Flimbos_Quest_intro.sid" } },
		{ "Tetris RSID", 0, new[] { "TestTunes", "SID", "Wally Beben", "Tetris.sid" } },
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

	[Theory]
	[MemberData(nameof(Workloads))]
	public void RenderTickAllocatesZeroBytesAfterWarmup(string name, int subSongIndex, string[] pathParts)
	{
		var path = FindWorkspaceFile(pathParts);
		if (!File.Exists(path))
		{
			return;
		}

		var song = new SidFormat().Load(File.ReadAllBytes(path));
		if (subSongIndex != 0)
		{
			var selector = (IModuleSubSongSelector)song;
			if (subSongIndex >= selector.SubSongCount)
			{
				return;
			}

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

	private static string FindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var segments = new string[parts.Length + 1];
			segments[0] = directory.FullName;
			Array.Copy(parts, 0, segments, 1, parts.Length);
			var candidate = Path.Combine(segments);
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return Path.Combine(parts);
	}
}
