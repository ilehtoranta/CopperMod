using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;

namespace CopperMod.Tests;

public sealed class SidRegressionTests
{
	[Fact]
	public void RealGreatGianaSistersSubtuneFiveKeepsResonantFilterSweepBrightWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Tough", "Great_Giana_Sisters.sid");
		if (!File.Exists(path))
		{
			return;
		}

		using var song = new SidFormat().Load(File.ReadAllBytes(path));
		var selector = (IModuleSubSongSelector)song;
		if (selector.SubSongCount < 5)
		{
			return;
		}

		selector.SelectSubSong(4);
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var outputStage = new C64OutputStage(C64OutputProfile.C64);
		var crossingCount = 0;
		var totalFrames = 0;
		var hasPrevious = false;
		var previous = 0.0f;
		var peak = 0.0f;
		var sumSquares = 0.0;

		for (var tick = 0; tick < 400; tick++)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			song.RenderTick(buffer, options);
			outputStage.Process(buffer, options.ChannelCount, options.SampleRate);

			for (var sampleIndex = 0; sampleIndex < buffer.Length; sampleIndex += options.ChannelCount)
			{
				var sample = buffer[sampleIndex];
				Assert.True(float.IsFinite(sample));
				Assert.InRange(sample, -1.0f, 1.0f);

				if (hasPrevious &&
					((previous < 0 && sample >= 0) || (previous >= 0 && sample < 0)))
				{
					crossingCount++;
				}

				previous = sample;
				hasPrevious = true;
				peak = Math.Max(peak, Math.Abs(sample));
				sumSquares += sample * sample;
				totalFrames++;
			}
		}

		var zeroCrossingRate = crossingCount / (double)Math.Max(1, totalFrames - 1);
		var rms = Math.Sqrt(sumSquares / Math.Max(1, totalFrames));
		Assert.True(peak > 0.3f, $"Expected subtune 5 resonant sweep to remain prominent, peak was {peak:0.000}.");
		Assert.True(rms > 0.08, $"Expected subtune 5 resonant sweep to stay audible, RMS was {rms:0.000}.");
		Assert.True(zeroCrossingRate > 0.025, $"Expected subtune 5 high-resonance sweep to retain bright motion, zero-crossing rate was {zeroCrossingRate:0.000}.");
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			if (parts.Length > 0)
			{
				var searchRoot = Path.Combine(new[] { directory.FullName }.Concat(parts.Take(parts.Length - 1)).ToArray());
				if (Directory.Exists(searchRoot))
				{
					var recursiveCandidate = Directory.EnumerateFiles(searchRoot, parts[^1], SearchOption.AllDirectories).FirstOrDefault();
					if (recursiveCandidate != null)
					{
						return recursiveCandidate;
					}
				}
			}

			directory = directory.Parent;
		}

		return string.Join(Path.DirectorySeparatorChar, parts);
	}
}
