namespace CopperScreen.Tests;

public sealed class CopperScreenAllocationTests
{
	private const int SampleRate = 44_100;
	private const int Channels = 2;
	private const int WarmupFrames = 220;
	private const int MeasuredFrames = 40;

	public static TheoryData<string, string?> Workloads { get; } = new()
	{
		{ "no-disk", null },
		{ "Superfrog", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip" },
		{ "Lemmings", "Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip" },
		{ "Full Contact", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip" },
		{ "Shadow of the Beast", "Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip" },
	};

	[Theory]
	[MemberData(nameof(Workloads))]
	public void RenderFrameAndAudioAllocateZeroBytesAfterWarmup(string name, string? fileName)
	{
		var emulator = CreateEmulator(fileName);
		if (emulator == null)
		{
			return;
		}

		var audio = new float[emulator.AudioFramesPerAppFrame(SampleRate) * Channels];
		for (var frame = 0; frame < WarmupFrames; frame++)
		{
			emulator.RenderNextFrame();
			_ = emulator.RenderAudio(audio, SampleRate, Channels);
		}

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var frame = 0; frame < MeasuredFrames; frame++)
		{
			emulator.RenderNextFrame();
			_ = emulator.RenderAudio(audio, SampleRate, Channels);
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.True(allocated == 0, $"{name} allocated {allocated} bytes during measured frame/audio rendering.");
	}

	private static CopperScreenEmulator? CreateEmulator(string? fileName)
	{
		if (fileName == null)
		{
			return CopperScreenEmulator.CreateWithoutDisk();
		}

		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", fileName);
		return diskPath == null
			? null
			: CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
	}

	private static string? TryFindWorkspaceFile(params string[] parts)
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

		return null;
	}
}
