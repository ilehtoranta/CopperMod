using CopperMod.Abstractions;
using CopperMod.Sid;

namespace CopperMod;

internal sealed class PlayerStartupOptions
{
	private PlayerStartupOptions(
		string? initialPath,
		IReadOnlyList<string> c64AutostartKeys,
		TimeSpan c64AutostartDelay,
		TimeSpan c64AutostartHold,
		TimeSpan c64AutostartGap,
		string? c64RomPath)
	{
		InitialPath = initialPath;
		C64AutostartKeys = c64AutostartKeys;
		C64AutostartDelay = c64AutostartDelay;
		C64AutostartHold = c64AutostartHold;
		C64AutostartGap = c64AutostartGap;
		C64RomPath = c64RomPath;
	}

	public string? InitialPath { get; }

	public IReadOnlyList<string> C64AutostartKeys { get; }

	public TimeSpan C64AutostartDelay { get; }

	public TimeSpan C64AutostartHold { get; }

	public TimeSpan C64AutostartGap { get; }

	public string? C64RomPath { get; }

	public static PlayerStartupOptions Parse(string[] args, string? defaultPath)
	{
		string? initialPath = null;
		IReadOnlyList<string> c64AutostartKeys = Array.Empty<string>();
		var c64AutostartDelay = TimeSpan.FromSeconds(1.0);
		var c64AutostartHold = TimeSpan.FromSeconds(0.25);
		var c64AutostartGap = TimeSpan.FromSeconds(0.75);
		string? c64RomPath = null;

		for (var i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			if (!arg.StartsWith("--", StringComparison.Ordinal))
			{
				if (initialPath != null)
				{
					throw new ArgumentException("CopperMod accepts only one startup module path.");
				}

				initialPath = arg;
				continue;
			}

			switch (arg)
			{
				case "--c64-autostart-key":
					c64AutostartKeys = ParseC64AutostartKeys(RequireValue(args, ref i, arg));
					break;
				case "--c64-autostart-delay-seconds":
					c64AutostartDelay = TimeSpan.FromSeconds(ParseNonNegativeDouble(RequireValue(args, ref i, arg), arg));
					break;
				case "--c64-autostart-hold-seconds":
					c64AutostartHold = TimeSpan.FromSeconds(ParsePositiveDouble(RequireValue(args, ref i, arg), arg));
					break;
				case "--c64-autostart-gap-seconds":
					c64AutostartGap = TimeSpan.FromSeconds(ParseNonNegativeDouble(RequireValue(args, ref i, arg), arg));
					break;
				case "--c64-rom":
					c64RomPath = RequireValue(args, ref i, arg);
					break;
				case "--help":
					throw new ArgumentException(GetUsage());
				default:
					throw new ArgumentException("Unknown option: " + arg);
			}
		}

		return new PlayerStartupOptions(
			initialPath ?? defaultPath,
			c64AutostartKeys,
			c64AutostartDelay,
			c64AutostartHold,
			c64AutostartGap,
			c64RomPath);
	}

	public void Apply(IModuleSong song)
	{
		if (!string.IsNullOrWhiteSpace(C64RomPath))
		{
			if (song is not IC64RomController c64Rom)
			{
				throw new InvalidOperationException("The loaded module does not support C64 ROM selection.");
			}

			c64Rom.C64RomPath = C64RomPath;
		}

		if (C64AutostartKeys.Count == 0)
		{
			return;
		}

		if (song is not IC64AutostartController c64Autostart)
		{
			throw new InvalidOperationException("The loaded module does not support C64 autostart keys.");
		}

		var delay = C64AutostartDelay;
		foreach (var key in C64AutostartKeys)
		{
			c64Autostart.ScheduleAutostartKey(key, delay, C64AutostartHold);
			delay += C64AutostartHold + C64AutostartGap;
		}
	}

	private static string RequireValue(string[] args, ref int index, string option)
	{
		if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
		{
			throw new ArgumentException("Missing value for " + option + ".");
		}

		index++;
		return args[index];
	}

	private static double ParsePositiveDouble(string value, string option)
	{
		if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) || result <= 0)
		{
			throw new ArgumentException(option + " must be a positive number.");
		}

		return result;
	}

	private static double ParseNonNegativeDouble(string value, string option)
	{
		if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) || result < 0)
		{
			throw new ArgumentException(option + " must be a non-negative number.");
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
			throw new ArgumentException("--c64-autostart-key requires at least one key.");
		}

		foreach (var key in keys)
		{
			if (!IsSupportedC64AutostartKey(key))
			{
				throw new ArgumentException("--c64-autostart-key contains an unsupported C64 key: " + key + ".");
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

	private static string GetUsage()
	{
		return """
			Usage:
			  CopperMod.exe [module-path] [options]

			Options:
			  --c64-autostart-key a,return Schedule C64/PRG startup keys.
			  --c64-autostart-delay-seconds <n> Default: 1.
			  --c64-autostart-hold-seconds <n> Default: 0.25.
			  --c64-autostart-gap-seconds <n> Default: 0.75.
			  --c64-rom <path> Optional 16 KiB BASIC+KERNAL or 20 KiB combo ROM.
			""";
	}
}
