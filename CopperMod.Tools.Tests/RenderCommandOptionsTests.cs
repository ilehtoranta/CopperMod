using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;
using System.Text;

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
	public void ParsesSidLoopDetectionOptions()
	{
		var options = RenderCommandOptions.Parse(new[]
		{
			"render",
			"input.sid",
			"--out",
			"output.wav",
			"--sid-detect-loop",
			"--sid-detect-max-seconds",
			"8.5"
		});

		Assert.True(options.SidDetectLoop);
		Assert.False(options.SidDetectDuration);
		Assert.Equal(8.5, options.SidDetectMaxSeconds);
	}

	[Fact]
	public void ParsesSidDurationDetectionOptions()
	{
		var options = RenderCommandOptions.Parse(new[]
		{
			"render",
			"input.sid",
			"--out",
			"output.wav",
			"--sid-detect-duration",
			"--sid-detect-max-seconds",
			"12.5"
		});

		Assert.True(options.SidDetectDuration);
		Assert.False(options.SidDetectLoop);
		Assert.Equal(12.5, options.SidDetectMaxSeconds);
	}

	[Fact]
	public void SecondsAutoEnablesSidDurationDetection()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav", "--seconds", "auto" });

		Assert.True(options.SidDetectDuration);
		Assert.Null(options.Seconds);
	}

	[Fact]
	public void SidLoopDetectionCannotBeCombinedWithSeconds()
	{
		Assert.Throws<CommandLineException>(() =>
			RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav", "--seconds", "1", "--sid-detect-loop" }));
	}

	[Fact]
	public void SidDetectionMaxSecondsRequiresSidDetection()
	{
		Assert.Throws<CommandLineException>(() =>
			RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav", "--sid-detect-max-seconds", "8" }));
	}

	[Fact]
	public void SidLoopAndDurationDetectionCannotBeCombined()
	{
		Assert.Throws<CommandLineException>(() =>
			RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav", "--sid-detect-loop", "--sid-detect-duration" }));
	}

	[Fact]
	public void SidLoopDetectionCanResolveUnknownSidDuration()
	{
		var options = RenderCommandOptions.Parse(new[]
		{
			"render",
			"input.sid",
			"--out",
			"output.wav",
			"--sid-detect-loop",
			"--sid-detect-max-seconds",
			"8"
		});
		using var song = new SidFormat().Load(CreateLoopingPsid());
		using var output = new StringWriter();

		var duration = CopperModTools.ResolveFixedDuration(song, options, output);

		Assert.NotNull(duration);
		Assert.InRange(duration.Value.TotalSeconds, 2.4, 2.7);
		Assert.Contains("Detected SID loop", output.ToString());
	}

	[Fact]
	public void SecondsAutoCanResolveUnknownSidDurationBySilence()
	{
		var options = RenderCommandOptions.Parse(new[] { "render", "input.sid", "--out", "output.wav", "--seconds", "auto", "--sid-detect-max-seconds", "4" });
		using var song = new SidFormat().Load(CreateSilentEndingPsid());
		using var output = new StringWriter();

		var duration = CopperModTools.ResolveFixedDuration(song, options, output);

		Assert.NotNull(duration);
		Assert.InRange(duration.Value.TotalMilliseconds, 80.0, 180.0);
		Assert.Contains("by silence", output.ToString());
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

	private static byte[] CreateLoopingPsid()
	{
		return CreatePsid(
			new byte[]
			{
				0xA9, 0x00,       // LDA #$00
				0x8D, 0x00, 0x20, // STA $2000
				0x60,             // RTS
				0xAE, 0x00, 0x20, // LDX $2000
				0x8A,             // TXA
				0x8D, 0x00, 0xD4, // STA $D400
				0xE8,             // INX
				0xE0, 0x80,       // CPX #$80
				0xD0, 0x02,       // BNE store
				0xA2, 0x00,       // LDX #$00
				0x8E, 0x00, 0x20, // STX $2000
				0x60              // RTS
			},
			playAddress: 0x1006);
	}

	private static byte[] CreateSilentEndingPsid()
	{
		return CreatePsid(
			new byte[]
			{
				0xA9, 0x00,       // LDA #$00
				0x8D, 0x00, 0x20, // STA $2000
				0x60,             // RTS
				0xEE, 0x00, 0x20, // INC $2000
				0xAD, 0x00, 0x20, // LDA $2000
				0xC9, 0x06,       // CMP #$06
				0xB0, 0x20,       // BCS silence
				0x8D, 0x00, 0xD4, // STA $D400
				0xA9, 0x20,       // LDA #$20
				0x8D, 0x01, 0xD4, // STA $D401
				0xA9, 0x08,       // LDA #$08
				0x8D, 0x03, 0xD4, // STA $D403
				0xA9, 0x41,       // LDA #$41
				0x8D, 0x04, 0xD4, // STA $D404
				0xA9, 0xF0,       // LDA #$F0
				0x8D, 0x05, 0xD4, // STA $D405
				0x8D, 0x06, 0xD4, // STA $D406
				0xA9, 0x0F,       // LDA #$0F
				0x8D, 0x18, 0xD4, // STA $D418
				0x60,             // RTS
				0xC9, 0x06,       // CMP #$06
				0xD0, 0x08,       // BNE return
				0xA9, 0x00,       // LDA #$00
				0x8D, 0x18, 0xD4, // STA $D418
				0x8D, 0x04, 0xD4, // STA $D404
				0x60              // RTS
			},
			playAddress: 0x1006);
	}

	private static byte[] CreatePsid(byte[] payload, ushort playAddress)
	{
		var data = new byte[0x7C + payload.Length];
		WriteAscii(data, 0, "PSID");
		WriteBigEndian(data, 4, (ushort)2);
		WriteBigEndian(data, 6, (ushort)0x7C);
		WriteBigEndian(data, 8, (ushort)0x1000);
		WriteBigEndian(data, 0x0A, (ushort)0x1000);
		WriteBigEndian(data, 0x0C, playAddress);
		WriteBigEndian(data, 0x0E, (ushort)1);
		WriteBigEndian(data, 0x10, (ushort)1);
		WriteBigEndian(data, 0x12, 0U);
		WriteFixed(data, 0x16, "Generated SID");
		WriteFixed(data, 0x36, "CopperMod");
		WriteFixed(data, 0x56, "2026");
		WriteBigEndian(data, 0x76, (ushort)((1 << 2) | (2 << 4)));
		payload.CopyTo(data, 0x7C);
		return data;
	}

	private static void WriteAscii(byte[] data, int offset, string text)
	{
		Encoding.ASCII.GetBytes(text, data.AsSpan(offset, text.Length));
	}

	private static void WriteFixed(byte[] data, int offset, string text)
	{
		Encoding.ASCII.GetBytes(text, data.AsSpan(offset, Math.Min(32, text.Length)));
	}

	private static void WriteBigEndian(byte[] data, int offset, ushort value)
	{
		data[offset] = (byte)(value >> 8);
		data[offset + 1] = (byte)value;
	}

	private static void WriteBigEndian(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
	}
}
