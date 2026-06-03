using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace CopperMod.Tools;

internal static class CopperModTools
{
	private const int FloatChunkFrames = 4096;

	public static int Run(string[] args, TextWriter output, TextWriter error)
	{
		try
		{
			var options = RenderCommandOptions.Parse(args);
			Render(options, output);
			return 0;
		}
		catch (CommandLineException ex)
		{
			error.WriteLine(ex.Message);
			return 1;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ModuleLoadException)
		{
			error.WriteLine(ex.Message);
			return 1;
		}
	}

	public static void Render(RenderCommandOptions options, TextWriter output)
	{
		if (File.Exists(options.OutputPath) && !options.Overwrite)
		{
			throw new CommandLineException("Output file already exists. Use --overwrite to replace it.");
		}

		using var song = ModuleFormatRegistry.LoadFile(options.InputPath);
		ConfigureSong(song, options);
		var fixedDuration = ResolveFixedDuration(song, options);
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? ".");

		switch (options.Format)
		{
			case RenderFileFormat.Wav:
				RenderWav(song, options, fixedDuration);
				break;
			case RenderFileFormat.Pcm:
				RenderPcm(song, options, fixedDuration);
				break;
			case RenderFileFormat.Mp3:
				RenderMp3(song, options, fixedDuration);
				break;
			case RenderFileFormat.Bmp:
				RenderBmp(song, options, fixedDuration);
				break;
			default:
				throw new CommandLineException("Unsupported output format.");
		}

		output.WriteLine("Rendered " + Path.GetFileName(options.OutputPath));
	}

	internal static void ConfigureSong(IModuleSong song, RenderCommandOptions options)
	{
		if (song.Capabilities.SupportsLoopControl)
		{
			song.LoopingEnabled = false;
		}

		if (options.SubSong.HasValue)
		{
			if (song is not IModuleSubSongSelector selector)
			{
				throw new CommandLineException("The loaded module does not expose subtunes.");
			}

			var index = options.SubSong.Value - 1;
			if (index < 0 || index >= selector.SubSongCount)
			{
				throw new CommandLineException("Subtune is outside the available range.");
			}

			selector.SelectSubSong(index);
		}

		if (options.SidSoloVoice.HasValue)
		{
			if (song is not ISidVoiceMuteController sidVoiceMuteController)
			{
				throw new CommandLineException("The loaded module does not support SID voice muting.");
			}

			sidVoiceMuteController.MutedVoicesMask = 0x07 & ~(1 << (options.SidSoloVoice.Value - 1));
		}
	}

	internal static TimeSpan? ResolveFixedDuration(IModuleSong song, RenderCommandOptions options)
	{
		if (options.RenderDuration.HasValue)
		{
			return options.RenderDuration.Value;
		}

		if (song.Duration.Time.HasValue)
		{
			return null;
		}

		throw new CommandLineException("The module duration is unknown. Use --seconds to choose a render duration.");
	}

	private static void RenderWav(IModuleSong song, RenderCommandOptions options, TimeSpan? duration)
	{
		using var renderer = CreateRenderer(song, options);
		var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(options.SampleRate, options.ChannelCount);
		using var writer = new WaveFileWriter(options.OutputPath, waveFormat);
		RenderFloatSamples(renderer, options, duration, (buffer, count) => writer.WriteSamples(buffer, 0, count));
	}

	private static void RenderPcm(IModuleSong song, RenderCommandOptions options, TimeSpan? duration)
	{
		using var renderer = CreateRenderer(song, options);
		using var stream = File.Create(options.OutputPath);
		using var writer = new FloatPcmWriter(stream);
		RenderFloatSamples(renderer, options, duration, writer.Write);
	}

	private static void RenderMp3(IModuleSong song, RenderCommandOptions options, TimeSpan? duration)
	{
		if (!Mp3Encoder.IsAvailable(options.SampleRate, options.ChannelCount))
		{
			throw new CommandLineException("MP3 output requires the Windows Media Foundation MP3 encoder. Use WAV or PCM output instead.");
		}

		using var renderer = CreateRenderer(song, options);
		using var provider = new RenderedPcm16WaveProvider(renderer, duration);
		Mp3Encoder.Encode(provider, options.OutputPath, options.Mp3BitrateKbps);
	}

	private static void RenderBmp(IModuleSong song, RenderCommandOptions options, TimeSpan? duration)
	{
		using var renderer = CreateRenderer(song, options);
		var sampler = new WaveformBitmapSampler(
			options.ChannelCount,
			options.SampleRate,
			options.BitmapWidth,
			ResolveWaveformFrameCount(song, options, duration));

		RenderFloatSamples(renderer, options, duration, sampler.AddSamples);
		var image = WaveformBitmapRenderer.Render(sampler.CreateSnapshot(), options.BitmapWidth, options.BitmapHeight);
		using var stream = File.Create(options.OutputPath);
		WaveformBitmapWriter.Write(stream, image);
	}

	private static ModulePcmRenderer CreateRenderer(IModuleSong song, RenderCommandOptions options)
	{
		return new ModulePcmRenderer(song, options.ToRenderSettings());
	}

	private static long? ResolveWaveformFrameCount(IModuleSong song, RenderCommandOptions options, TimeSpan? duration)
	{
		var waveformDuration = duration ?? song.Duration.Time;
		if (!waveformDuration.HasValue)
		{
			return null;
		}

		return checked((long)Math.Ceiling(waveformDuration.Value.TotalSeconds * options.SampleRate));
	}

	private static void RenderFloatSamples(
		ModulePcmRenderer renderer,
		RenderCommandOptions options,
		TimeSpan? duration,
		Action<float[], int> write)
	{
		var chunkSamples = FloatChunkFrames * options.ChannelCount;
		var buffer = new float[chunkSamples];
		if (duration.HasValue)
		{
			var remainingSamples = checked((long)Math.Ceiling(duration.Value.TotalSeconds * options.SampleRate) * options.ChannelCount);
			while (remainingSamples > 0)
			{
				var count = (int)Math.Min(buffer.Length, remainingSamples);
				var written = renderer.Read(buffer.AsSpan(0, count));
				write(buffer, count);
				remainingSamples -= count;
				if (written < count)
				{
					Array.Clear(buffer, 0, count);
				}
			}

			return;
		}

		while (!renderer.EndOfSong)
		{
			var written = renderer.Read(buffer);
			if (written <= 0)
			{
				break;
			}

			write(buffer, written);
		}
	}

	private static class Mp3Encoder
	{
		public static bool IsAvailable(int sampleRate, int channels)
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}

			try
			{
				return MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_MP3, sampleRate, channels).Length > 0;
			}
			catch
			{
				return false;
			}
		}

		public static void Encode(IWaveProvider provider, string outputPath, int bitrateKbps)
		{
			try
			{
				MediaFoundationEncoder.EncodeToMp3(provider, outputPath, bitrateKbps * 1000);
			}
			catch (Exception ex)
			{
				throw new CommandLineException("MP3 encoding failed. The Windows Media Foundation MP3 encoder may be unavailable.", ex);
			}
		}
	}
}
