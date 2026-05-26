using CopperMod.Abstractions;
using CopperMod.Cust;
using CopperMod.Med;
using CopperMod.ProTracker;
using CopperMod.Sid;

namespace CopperMod.Rendering;

public static class ModuleFormatRegistry
{
	public static IReadOnlyList<IModuleFormat> CreateDefaultFormats()
	{
		return new IModuleFormat[]
		{
			new MmdFormat(),
			new ProTrackerFormat(),
			new CustFormat(),
			new SidFormat()
		};
	}

	public static IModuleSong LoadFile(string path, IReadOnlyList<IModuleFormat>? formats = null)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("A module path is required.", nameof(path));
		}

		var fullPath = Path.GetFullPath(path);
		return Load(File.ReadAllBytes(fullPath), formats);
	}

	public static IModuleSong Load(ReadOnlySpan<byte> data, IReadOnlyList<IModuleFormat>? formats = null)
	{
		formats ??= CreateDefaultFormats();
		foreach (var format in formats)
		{
			if (format.CanLoad(data))
			{
				return format.Load(data);
			}
		}

		throw new UnsupportedModuleFormatException("The input file is not a supported CopperMod module.");
	}
}
