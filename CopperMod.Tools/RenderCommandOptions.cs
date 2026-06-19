using System.Globalization;
using CopperMod.Rendering;
using CopperMod.Sid;

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
		bool sidDetectLoop,
		bool sidDetectDuration,
		double sidDetectMaxSeconds,
		ModuleRenderOutputMode outputMode,
		AmigaOutputProfile amigaProfile,
		C64OutputProfile c64Profile,
		SidEmulationProfile sidProfile,
		int mp3BitrateKbps,
		int bitmapWidth,
		int bitmapHeight,
		string? c64AutostartKey,
		IReadOnlyList<string> c64AutostartKeys,
		double c64AutostartDelaySeconds,
		double c64AutostartHoldSeconds,
		double c64AutostartGapSeconds,
		string? c64RomPath,
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
		SidDetectLoop = sidDetectLoop;
		SidDetectDuration = sidDetectDuration;
		SidDetectMaxSeconds = sidDetectMaxSeconds;
		OutputMode = outputMode;
		AmigaProfile = amigaProfile;
		C64Profile = c64Profile;
		SidProfile = sidProfile;
		Mp3BitrateKbps = mp3BitrateKbps;
		BitmapWidth = bitmapWidth;
		BitmapHeight = bitmapHeight;
		C64AutostartKey = c64AutostartKey;
		C64AutostartKeys = c64AutostartKeys;
		C64AutostartDelaySeconds = c64AutostartDelaySeconds;
		C64AutostartHoldSeconds = c64AutostartHoldSeconds;
		C64AutostartGapSeconds = c64AutostartGapSeconds;
		C64RomPath = c64RomPath;
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

	public bool SidDetectLoop { get; }

	public bool SidDetectDuration { get; }

	public double SidDetectMaxSeconds { get; }

	public ModuleRenderOutputMode OutputMode { get; }

	public AmigaOutputProfile AmigaProfile { get; }

	public C64OutputProfile C64Profile { get; }

	public SidEmulationProfile SidProfile { get; }

	public int Mp3BitrateKbps { get; }

	public int BitmapWidth { get; }

	public int BitmapHeight { get; }

	public string? C64AutostartKey { get; }

	public IReadOnlyList<string> C64AutostartKeys { get; }

	public double C64AutostartDelaySeconds { get; }

	public double C64AutostartHoldSeconds { get; }

	public double C64AutostartGapSeconds { get; }

	public string? C64RomPath { get; }

	public bool Overwrite { get; }

	public TimeSpan? RenderDuration => Seconds.HasValue ? TimeSpan.FromSeconds(Seconds.Value) : null;

	public TimeSpan SidDetectMaxDuration => TimeSpan.FromSeconds(SidDetectMaxSeconds);

	public ModuleRenderSettings ToRenderSettings()
	{
		return new ModuleRenderSettings(
			SampleRate,
			ChannelCount,
			OutputMode,
			AmigaProfile,
			C64Profile,
			SidProfile);
	}

	public static RenderCommandOptions Parse(string[] args)
	{
		if (args.Length == 0 || IsHelp(args[0]))
		{
			throw new CommandLineException(Usage.Text);
		}

		if (!string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase))
		{
			throw new CommandLineException("Unknown command. Expected: render or " + SidD418MatrixGenerator.CommandName);
		}

		var positional = new List<string>();
		string? outputPath = null;
		RenderFileFormat? format = null;
		double? seconds = null;
		int? subSong = null;
		var sampleRate = 44100;
		var channelCount = 2;
		int? sidSoloVoice = null;
		var sidDetectLoop = false;
		var sidDetectDuration = false;
		var sidDetectMaxSeconds = SidDurationDetectionOptions.DefaultMaxSearchDuration.TotalSeconds;
		var sidDetectMaxSecondsSpecified = false;
		var outputMode = ModuleRenderOutputMode.Raw;
		var amigaProfile = AmigaOutputProfile.A500;
		var c64Profile = C64OutputProfile.C64;
		var sidProfile = SidEmulationProfile.Balanced;
		var mp3Bitrate = 192;
		var bitmapWidth = WaveformBitmapRenderer.DefaultWidth;
		var bitmapHeight = WaveformBitmapRenderer.DefaultHeight;
		string? c64AutostartKey = null;
		IReadOnlyList<string> c64AutostartKeys = Array.Empty<string>();
		var c64AutostartDelaySeconds = 1.0;
		var c64AutostartHoldSeconds = 0.25;
		var c64AutostartGapSeconds = 0.75;
		string? c64RomPath = null;
		var overwrite = false;
		var amigaProfileSpecified = false;
		var c64ProfileSpecified = false;
		var bitmapSizeSpecified = false;

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
					var secondsValue = RequireValue(args, ref i, arg);
					if (string.Equals(secondsValue, "auto", StringComparison.OrdinalIgnoreCase))
					{
						sidDetectDuration = true;
					}
					else
					{
						seconds = ParsePositiveDouble(secondsValue, arg);
					}
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
				case "--sid-detect-loop":
					sidDetectLoop = true;
					break;
				case "--sid-detect-duration":
					sidDetectDuration = true;
					break;
				case "--sid-detect-max-seconds":
					sidDetectMaxSeconds = ParsePositiveDouble(RequireValue(args, ref i, arg), arg);
					sidDetectMaxSecondsSpecified = true;
					break;
				case "--sid-loop-max-seconds":
					sidDetectMaxSeconds = ParsePositiveDouble(RequireValue(args, ref i, arg), arg);
					sidDetectMaxSecondsSpecified = true;
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
				case "--sid-profile":
					sidProfile = ParseSidProfile(RequireValue(args, ref i, arg));
					break;
				case "--mp3-bitrate":
					mp3Bitrate = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
					break;
				case "--bitmap-width":
					bitmapWidth = ParseBitmapDimension(RequireValue(args, ref i, arg), arg, WaveformBitmapRenderer.MinimumWidth, WaveformBitmapRenderer.MaximumWidth);
					bitmapSizeSpecified = true;
					break;
				case "--bitmap-height":
					bitmapHeight = ParseBitmapDimension(RequireValue(args, ref i, arg), arg, WaveformBitmapRenderer.MinimumHeight, WaveformBitmapRenderer.MaximumHeight);
					bitmapSizeSpecified = true;
					break;
				case "--c64-autostart-key":
					c64AutostartKeys = ParseC64AutostartKeys(RequireValue(args, ref i, arg));
					c64AutostartKey = string.Join(",", c64AutostartKeys);
					break;
				case "--c64-autostart-delay-seconds":
					c64AutostartDelaySeconds = ParseNonNegativeDouble(RequireValue(args, ref i, arg), arg);
					break;
				case "--c64-autostart-hold-seconds":
					c64AutostartHoldSeconds = ParsePositiveDouble(RequireValue(args, ref i, arg), arg);
					break;
				case "--c64-autostart-gap-seconds":
					c64AutostartGapSeconds = ParseNonNegativeDouble(RequireValue(args, ref i, arg), arg);
					break;
				case "--c64-rom":
					c64RomPath = RequireValue(args, ref i, arg);
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

		if (seconds.HasValue && (sidDetectLoop || sidDetectDuration))
		{
			throw new CommandLineException("Use either --seconds, --sid-detect-loop, or --sid-detect-duration.");
		}

		if (sidDetectLoop && sidDetectDuration)
		{
			throw new CommandLineException("Use either --sid-detect-loop or --sid-detect-duration, not both.");
		}

		if (sidDetectMaxSecondsSpecified && !sidDetectLoop && !sidDetectDuration)
		{
			throw new CommandLineException("--sid-detect-max-seconds requires --sid-detect-loop or --sid-detect-duration.");
		}

		format ??= InferFormat(outputPath);

		if (bitmapSizeSpecified && format.Value != RenderFileFormat.Bmp)
		{
			throw new CommandLineException("Bitmap size options require BMP output.");
		}

		return new RenderCommandOptions(
			positional[0],
			outputPath,
			format.Value,
			seconds,
			subSong,
			sampleRate,
			channelCount,
			sidSoloVoice,
			sidDetectLoop,
			sidDetectDuration,
			sidDetectMaxSeconds,
			outputMode,
			amigaProfile,
			c64Profile,
			sidProfile,
			mp3Bitrate,
			bitmapWidth,
			bitmapHeight,
			c64AutostartKey,
			c64AutostartKeys,
			c64AutostartDelaySeconds,
			c64AutostartHoldSeconds,
			c64AutostartGapSeconds,
			c64RomPath,
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
			".bmp" => RenderFileFormat.Bmp,
			_ => throw new CommandLineException("Cannot infer output format. Use --format wav, pcm, mp3, or bmp.")
		};
	}

	private static RenderFileFormat ParseFormat(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"wav" => RenderFileFormat.Wav,
			"pcm" => RenderFileFormat.Pcm,
			"mp3" => RenderFileFormat.Mp3,
			"bmp" => RenderFileFormat.Bmp,
			"bitmap" => RenderFileFormat.Bmp,
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

	private static SidEmulationProfile ParseSidProfile(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"balanced" => SidEmulationProfile.Balanced,
			"reference" => SidEmulationProfile.ReferenceMeasured,
			"reference-measured" => SidEmulationProfile.ReferenceMeasured,
			"measured" => SidEmulationProfile.ReferenceMeasured,
			_ => throw new CommandLineException("Unsupported SID emulation profile: " + value)
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

	private static double ParseNonNegativeDouble(string value, string option)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result < 0)
		{
			throw new CommandLineException(option + " must be a non-negative number.");
		}

		return result;
	}

	private static IReadOnlyList<string> ParseC64AutostartKeys(string value)
	{
		var keys = value
			.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Select(key => key.ToLowerInvariant())
			.ToArray();
		if (keys.Length == 0)
		{
			throw new CommandLineException("--c64-autostart-key requires at least one key.");
		}

		foreach (var key in keys)
		{
			if (!IsSupportedC64AutostartKey(key))
			{
				throw new CommandLineException("--c64-autostart-key contains an unsupported C64 key: " + key + ".");
			}
		}

		return keys;
	}

	private static bool IsSupportedC64AutostartKey(string key)
	{
		if (key.Length == 1)
		{
			var ch = key[0];
			return char.IsLetterOrDigit(ch) ||
				ch is ' ' or '+' or '-' or '.' or ':' or '@' or ',' or '*' or ';' or '=' or '/';
		}

		return key is
			"return" or
			"enter" or
			"space" or
			"f1" or
			"f3" or
			"f5" or
			"f7" or
			"runstop" or
			"run-stop" or
			"stop" or
			"delete" or
			"del" or
			"home" or
			"cursorright" or
			"right" or
			"cursordown" or
			"down";
	}

	private static int ParseBitmapDimension(string value, string option, int minimum, int maximum)
	{
		var result = ParsePositiveInt(value, option);
		if (result < minimum || result > maximum)
		{
			throw new CommandLineException(option + " must be between " + minimum.ToString(CultureInfo.InvariantCulture) + " and " + maximum.ToString(CultureInfo.InvariantCulture) + ".");
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
