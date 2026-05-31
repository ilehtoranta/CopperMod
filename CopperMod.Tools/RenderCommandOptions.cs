using System.Globalization;
using CopperMod.Rendering;

namespace CopperMod.Tools;

internal sealed class RenderCommandOptions
{
	private RenderCommandOptions(
		string inputPath,
		string outputPath,
		RenderFileFormat format,
		double? seconds,
		int? subSong,
		int sampleRate,
		int channelCount,
		int? sidSoloVoice,
		ModuleRenderOutputMode outputMode,
		AmigaOutputProfile amigaProfile,
		C64OutputProfile c64Profile,
		int mp3BitrateKbps,
		bool overwrite)
	{
		InputPath = inputPath;
		OutputPath = outputPath;
		Format = format;
		Seconds = seconds;
		SubSong = subSong;
		SampleRate = sampleRate;
		ChannelCount = channelCount;
		SidSoloVoice = sidSoloVoice;
		OutputMode = outputMode;
		AmigaProfile = amigaProfile;
		C64Profile = c64Profile;
		Mp3BitrateKbps = mp3BitrateKbps;
		Overwrite = overwrite;
	}

	public string InputPath { get; }

	public string OutputPath { get; }

	public RenderFileFormat Format { get; }

	public double? Seconds { get; }

	public int? SubSong { get; }

	public int SampleRate { get; }

	public int ChannelCount { get; }

	public int? SidSoloVoice { get; }

	public ModuleRenderOutputMode OutputMode { get; }

	public AmigaOutputProfile AmigaProfile { get; }

	public C64OutputProfile C64Profile { get; }

	public int Mp3BitrateKbps { get; }

	public bool Overwrite { get; }

	public TimeSpan? RenderDuration => Seconds.HasValue ? TimeSpan.FromSeconds(Seconds.Value) : null;

	public ModuleRenderSettings ToRenderSettings()
	{
		return new ModuleRenderSettings(
			SampleRate,
			ChannelCount,
			OutputMode,
			AmigaProfile,
			C64Profile);
	}

	public static RenderCommandOptions Parse(string[] args)
	{
		if (args.Length == 0 || IsHelp(args[0]))
		{
			throw new CommandLineException(Usage.Text);
		}

		if (!string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase))
		{
			throw new CommandLineException("Unknown command. Expected: render");
		}

		var positional = new List<string>();
		string? outputPath = null;
		RenderFileFormat? format = null;
		double? seconds = null;
		int? subSong = null;
		var sampleRate = 44100;
		var channelCount = 2;
		int? sidSoloVoice = null;
		var outputMode = ModuleRenderOutputMode.Raw;
		var amigaProfile = AmigaOutputProfile.A500;
		var c64Profile = C64OutputProfile.C64;
		var mp3Bitrate = 192;
		var overwrite = false;
		var amigaProfileSpecified = false;
		var c64ProfileSpecified = false;

		for (var i = 1; i < args.Length; i++)
		{
			var arg = args[i];
			if (!arg.StartsWith("--", StringComparison.Ordinal))
			{
				positional.Add(arg);
				continue;
			}

			switch (arg)
			{
				case "--out":
					outputPath = RequireValue(args, ref i, arg);
					break;
				case "--format":
					format = ParseFormat(RequireValue(args, ref i, arg));
					break;
				case "--seconds":
					seconds = ParsePositiveDouble(RequireValue(args, ref i, arg), arg);
					break;
				case "--subsong":
					subSong = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
					break;
				case "--sample-rate":
					sampleRate = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
					break;
				case "--channels":
					channelCount = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
					break;
				case "--sid-solo":
					sidSoloVoice = ParseSidVoice(RequireValue(args, ref i, arg), arg);
					break;
				case "--output":
					outputMode = ParseOutputMode(RequireValue(args, ref i, arg));
					break;
				case "--amiga-profile":
					amigaProfile = ParseAmigaProfile(RequireValue(args, ref i, arg));
					amigaProfileSpecified = true;
					break;
				case "--c64-profile":
					c64Profile = ParseC64Profile(RequireValue(args, ref i, arg));
					c64ProfileSpecified = true;
					break;
				case "--mp3-bitrate":
					mp3Bitrate = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
					break;
				case "--overwrite":
					overwrite = true;
					break;
				case "--help":
					throw new CommandLineException(Usage.Text);
				default:
					throw new CommandLineException("Unknown option: " + arg);
			}
		}

		if (positional.Count != 1)
		{
			throw new CommandLineException("Render requires exactly one input file.");
		}

		if (string.IsNullOrWhiteSpace(outputPath))
		{
			throw new CommandLineException("Missing required option: --out");
		}

		if (outputMode == ModuleRenderOutputMode.Raw && (amigaProfileSpecified || c64ProfileSpecified))
		{
			throw new CommandLineException("Output profile options require --output player.");
		}

		format ??= InferFormat(outputPath);

		return new RenderCommandOptions(
			positional[0],
			outputPath,
			format.Value,
			seconds,
			subSong,
			sampleRate,
			channelCount,
			sidSoloVoice,
			outputMode,
			amigaProfile,
			c64Profile,
			mp3Bitrate,
			overwrite);
	}

	private static bool IsHelp(string value)
	{
		return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase);
	}

	private static string RequireValue(string[] args, ref int index, string option)
	{
		if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
		{
			throw new CommandLineException("Missing value for " + option + ".");
		}

		index++;
		return args[index];
	}

	private static RenderFileFormat InferFormat(string outputPath)
	{
		return Path.GetExtension(outputPath).ToLowerInvariant() switch
		{
			".wav" => RenderFileFormat.Wav,
			".pcm" => RenderFileFormat.Pcm,
			".mp3" => RenderFileFormat.Mp3,
			_ => throw new CommandLineException("Cannot infer output format. Use --format wav, pcm, or mp3.")
		};
	}

	private static RenderFileFormat ParseFormat(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"wav" => RenderFileFormat.Wav,
			"pcm" => RenderFileFormat.Pcm,
			"mp3" => RenderFileFormat.Mp3,
			_ => throw new CommandLineException("Unsupported output format: " + value)
		};
	}

	private static ModuleRenderOutputMode ParseOutputMode(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"raw" => ModuleRenderOutputMode.Raw,
			"player" => ModuleRenderOutputMode.Player,
			_ => throw new CommandLineException("Unsupported output mode: " + value)
		};
	}

	private static AmigaOutputProfile ParseAmigaProfile(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"clean" => AmigaOutputProfile.None,
			"a500" => AmigaOutputProfile.A500,
			"led" => AmigaOutputProfile.A500LedFilter,
			_ => throw new CommandLineException("Unsupported Amiga output profile: " + value)
		};
	}

	private static C64OutputProfile ParseC64Profile(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"clean" => C64OutputProfile.Clean,
			"c64" => C64OutputProfile.C64,
			_ => throw new CommandLineException("Unsupported C64 output profile: " + value)
		};
	}

	private static int ParsePositiveInt(string value, string option)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result <= 0)
		{
			throw new CommandLineException(option + " must be a positive integer.");
		}

		return result;
	}

	private static double ParsePositiveDouble(string value, string option)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result <= 0)
		{
			throw new CommandLineException(option + " must be a positive number.");
		}

		return result;
	}

	private static int ParseSidVoice(string value, string option)
	{
		var result = ParsePositiveInt(value, option);
		if (result is < 1 or > 3)
		{
			throw new CommandLineException(option + " must be 1, 2, or 3.");
		}

		return result;
	}
}
