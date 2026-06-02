using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenStartupOptions
{
	private CopperScreenStartupOptions(
		CopperScreenProfile profile,
		string? diskPath,
		string? kickstartRomPath,
		AgnusTimingMode? agnusTimingModeOverride,
		M68kBackendKind? cpuBackendOverride,
		string baseDirectory,
		string? error)
	{
		Profile = profile;
		DiskPath = diskPath;
		KickstartRomPath = kickstartRomPath;
		AgnusTimingModeOverride = agnusTimingModeOverride;
		CpuBackendOverride = cpuBackendOverride;
		BaseDirectory = baseDirectory;
		Error = error;
	}

	public CopperScreenProfile Profile { get; }

	public string? DiskPath { get; }

	public string? KickstartRomPath { get; }

	public AgnusTimingMode? AgnusTimingModeOverride { get; }

	public M68kBackendKind? CpuBackendOverride { get; }

	public string BaseDirectory { get; }

	public string? Error { get; }

	public static CopperScreenStartupOptions Default(string baseDirectory)
	{
		var profile = CopperScreenProfile.LoadDefault(baseDirectory, out var error);
		return new CopperScreenStartupOptions(
			profile,
			null,
			null,
			null,
			null,
			baseDirectory,
			error);
	}

	public static CopperScreenStartupOptions Parse(string[]? args, string baseDirectory)
	{
		var startupArgs = args ?? Array.Empty<string>();
		var profile = CopperScreenProfile.LoadDefault(baseDirectory, out var defaultProfileError);
		var error = defaultProfileError;
		var profileExplicit = false;
		string? diskPath = null;
		string? kickstartRomPath = null;
		AgnusTimingMode? agnusTimingModeOverride = null;
		M68kBackendKind? cpuBackendOverride = null;

		for (var i = 0; i < startupArgs.Length; i++)
		{
			var arg = startupArgs[i];
			if (string.IsNullOrWhiteSpace(arg))
			{
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--profile", "-p", out var profileValue))
			{
				if (!CopperScreenProfile.TryLoad(profileValue, baseDirectory, out profile, out var profileError))
				{
					error ??= profileError;
				}
				else if (error == defaultProfileError)
				{
					error = null;
				}

				profileExplicit = true;
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--kickstart", "--kickstart-rom", out var romValue) ||
				TryReadOptionValue(startupArgs, ref i, arg, "--rom", null, out romValue))
			{
				kickstartRomPath = ResolveOptionalPath(romValue, baseDirectory);
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--agnus-timing", "--agnus", out var agnusTimingValue))
			{
				try
				{
					agnusTimingModeOverride = CopperScreenProfile.ParseAgnusTimingMode(agnusTimingValue);
				}
				catch (InvalidOperationException ex)
				{
					error ??= ex.Message;
				}

				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--cpu", null, out var cpuBackendValue))
			{
				try
				{
					cpuBackendOverride = CopperScreenProfile.ParseCpuBackend(cpuBackendValue);
				}
				catch (InvalidOperationException ex)
				{
					error ??= ex.Message;
				}

				continue;
			}

			if (IsOption(arg, "--jit"))
			{
				cpuBackendOverride = M68kBackendKind.JitM68000;
				continue;
			}

			if (IsOption(arg, "--real-kickstart"))
			{
				if (!CopperScreenProfile.TryLoadWithKickstartSource(
					profile,
					CopperScreenKickstartSource.Kickstart13Rom,
					baseDirectory,
					out profile,
					out var profileError))
				{
					error ??= profileError;
				}
				else if (error == defaultProfileError)
				{
					error = null;
				}

				profileExplicit = true;
				continue;
			}

			if (IsOption(arg, "--copperstart"))
			{
				if (!CopperScreenProfile.TryLoadWithKickstartSource(
					profile,
					CopperScreenKickstartSource.CopperStart,
					baseDirectory,
					out profile,
					out var profileError))
				{
					error ??= profileError;
				}
				else if (error == defaultProfileError)
				{
					error = null;
				}

				profileExplicit = true;
				continue;
			}

			if (arg.StartsWith("-", StringComparison.Ordinal))
			{
				error ??= $"Unknown CopperScreen option '{arg}'.";
				continue;
			}

			if (diskPath == null)
			{
				diskPath = ResolveExistingFile(arg, baseDirectory);
			}
		}

		if (kickstartRomPath != null && !profileExplicit)
		{
			if (!CopperScreenProfile.TryLoadWithKickstartSource(
				profile,
				CopperScreenKickstartSource.Kickstart13Rom,
				baseDirectory,
				out profile,
				out var profileError))
			{
				error ??= profileError;
			}
			else if (error == defaultProfileError)
			{
				error = null;
			}
		}
		else if (kickstartRomPath != null && !profile.UsesKickstartRom)
		{
			error ??= "A Kickstart ROM path was supplied with a CopperStart profile.";
		}

		return new CopperScreenStartupOptions(profile, diskPath, kickstartRomPath, agnusTimingModeOverride, cpuBackendOverride, baseDirectory, error);
	}

	private static bool TryReadOptionValue(
		string[] args,
		ref int index,
		string arg,
		string longName,
		string? shortName,
		out string value)
	{
		value = string.Empty;
		if (arg.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase))
		{
			value = arg[(longName.Length + 1)..];
			return true;
		}

		if (shortName != null && arg.StartsWith(shortName + "=", StringComparison.OrdinalIgnoreCase))
		{
			value = arg[(shortName.Length + 1)..];
			return true;
		}

		if (!IsOption(arg, longName) && (shortName == null || !IsOption(arg, shortName)))
		{
			return false;
		}

		if (index + 1 < args.Length)
		{
			index++;
			value = args[index];
		}

		return true;
	}

	private static bool IsOption(string arg, string option)
	{
		return string.Equals(arg, option, StringComparison.OrdinalIgnoreCase);
	}

	private static string? ResolveExistingFile(string path, string baseDirectory)
	{
		var resolved = ResolveOptionalPath(path, baseDirectory);
		return resolved != null && File.Exists(resolved) ? resolved : null;
	}

	private static string? ResolveOptionalPath(string? path, string baseDirectory)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		if (Path.IsPathFullyQualified(path))
		{
			return Path.GetFullPath(path);
		}

		var currentDirectoryCandidate = Path.GetFullPath(path);
		if (File.Exists(currentDirectoryCandidate))
		{
			return currentDirectoryCandidate;
		}

		return Path.GetFullPath(Path.Combine(baseDirectory, path));
	}
}
