using CopperMod.Abstractions;
using CopperMod.Rendering;

namespace CopperMod.Tools.Tests;

public sealed class RenderCommandOptionsTests
{
	[Fact]
	public void InfersOutputFormatFromExtension()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.mod", "--out", "output.pcm" });

		Assert.Equal(RenderFileFormat.Pcm, options.Format);
	}

	[Fact]
	public void InfersBitmapOutputFormatFromExtension()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.mod", "--out", "output.bmp" });

		Assert.Equal(RenderFileFormat.Bmp, options.Format);
	}

	[Fact]
	public void ExplicitFormatOverridesExtension()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.mod", "--out", "output.bin", "--format", "wav" });

		Assert.Equal(RenderFileFormat.Wav, options.Format);
	}

	[Fact]
	public void ParsesBitmapDimensions()
	{
		var options = RenderCommandOptions.Parse(new[]
		{
			"render",
			"input.mod",
			"--out",
			"output.bmp",
			"--bitmap-width",
			"320",
			"--bitmap-height",
			"120"
		});

		Assert.Equal(320, options.BitmapWidth);
		Assert.Equal(120, options.BitmapHeight);
	}

	[Fact]
	public void BitmapDimensionsRequireBitmapOutput()
	{
		Assert.Throws<CommandLineException>(() =>
			RenderCommandOptions.Parse(new[] { "render", "input.mod", "--out", "output.wav", "--bitmap-width", "320" }));
	}

	[Fact]
	public void UnknownDurationRequiresSeconds()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav" });
		using var song = new FakeSong(SongDuration.Unknown);

		Assert.Throws<CommandLineException>(() => CopperModTools.ResolveFixedDuration(song, options));
	}

	[Fact]
	public void SubSongOneMapsToInternalIndexZero()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav", "--seconds", "1", "--subsong", "1" });
		using var song = new FakeSong(SongDuration.Unknown, subSongs: 2);

		CopperModTools.ConfigureSong(song, options);

		Assert.Equal(0, song.CurrentSubSongIndex);
	}

	[Fact]
	public void ProfileFlagsRequirePlayerOutputMode()
	{
		Assert.Throws<CommandLineException>(() =>
			RenderCommandOptions.Parse(new[] { "render", "input.mod", "--out", "output.wav", "--amiga-profile", "led" }));
	}

	[Fact]
	public void ExistingOutputRequiresOverwrite()
	{
		using var temp = TemporaryDirectory.Create();
		var output = Path.Combine(temp.Path, "output.wav");
		File.WriteAllText(output, "existing");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = CopperModTools.Run(new[] { "render", "missing.mod", "--out", output }, stdout, stderr);

		Assert.Equal(1, exitCode);
		Assert.Contains("--overwrite", stderr.ToString());
	}

	private sealed class FakeSong : IModuleSong, IModuleSubSongSelector
	{
		private readonly SongDuration _duration;

		public FakeSong(SongDuration duration, int subSongs = 1)
		{
			_duration = duration;
			SubSongCount = subSongs;
			SubSongs = Enumerable.Range(0, subSongs)
				.Select(index => new ModuleSubSongMetadata(index))
				.ToArray();
		}

		public ModuleMetadata Metadata { get; } = new ModuleMetadata(null, "Fake");

		public ModulePlaybackCapabilities Capabilities { get; } = new ModulePlaybackCapabilities(supportsLoopControl: true, supportsSubSongs: true);

		public IReadOnlyList<ModuleDiagnostic> Diagnostics { get; } = Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => _duration;

		public PlaybackPosition Position => PlaybackPosition.FromTime(TimeSpan.Zero);

		public bool LoopingEnabled { get; set; } = true;

		public int SubSongCount { get; }

		public int DefaultSubSongIndex => 0;

		public int CurrentSubSongIndex { get; private set; }

		public IReadOnlyList<ModuleSubSongMetadata> SubSongs { get; }

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null) => 1;

		public void Reset()
		{
		}

		public void Seek(TimeSpan position)
		{
		}

		public void Seek(TrackerPosition position)
		{
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null) => RenderTick(destination, options);

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			destination[0] = 0.25f;
			return new RenderResult(1, 1, Position, true);
		}

		public void SelectSubSong(int index)
		{
			CurrentSubSongIndex = index;
		}

		public void Dispose()
		{
		}
	}
}
