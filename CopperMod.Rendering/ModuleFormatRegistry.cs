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
		return Load(new ModuleLoadContext(File.ReadAllBytes(fullPath), fullPath), formats);
	}

	public static IModuleSong Load(ReadOnlySpan<byte> data, IReadOnlyList<IModuleFormat>? formats = null)
	{
		return Load(new ModuleLoadContext(data.ToArray()), formats);
	}

	public static IModuleSong Load(ModuleLoadContext context, IReadOnlyList<IModuleFormat>? formats = null)
	{
		ArgumentNullException.ThrowIfNull(context);
		formats ??= CreateDefaultFormats();
		foreach (var format in formats)
		{
			if (format is IModuleFormatWithContext contextualFormat)
			{
				if (contextualFormat.CanLoad(context))
				{
					return contextualFormat.Load(context);
				}

				continue;
			}

			if (format.CanLoad(context.DataSpan))
			{
				return format.Load(context.DataSpan);
			}
		}

		throw new UnsupportedModuleFormatException("The input file is not a supported CopperMod module.");
	}
}
