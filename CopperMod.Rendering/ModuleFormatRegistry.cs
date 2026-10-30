using CopperMod.Abstractions;
using CopperMod.Ahx;
using CopperMod.Cust;
using CopperMod.EaglePlayer;
using CopperMod.Med;
using CopperMod.ProTracker;
using CopperMod.Sid;
using CopperMod.Replayers;

namespace CopperMod.Rendering;

public static class ModuleFormatRegistry
{
	public static IReadOnlyList<IModuleFormat> CreateDefaultFormats()
	{
		return new IModuleFormat[]
		{
			new AhxFormat(),
			new MmdFormat(),
			new ProTrackerFormat(),
			new CustFormat(),
			new TfmxFormat(),
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

	/// <summary>Returns playable members of an archive in a stable order.</summary>
	public static IReadOnlyList<string> GetArchiveMembers(ModuleArchive archive, IReadOnlyList<IModuleFormat>? formats = null)
	{
		formats ??= CreateDefaultFormats();
		return archive.Members.Where(name =>
		{
			var context = archive.CreateContext(name);
			return !ModuleArchive.IsArchive(context.DataSpan) && formats.Any(format =>
				format is IModuleFormatWithContext contextual ? contextual.CanLoad(context) : format.CanLoad(context.DataSpan));
		}).ToArray();
	}

	/// <summary>Loads a specific archive member.</summary>
	public static IModuleSong LoadArchiveMember(ModuleArchive archive, string member, IReadOnlyList<IModuleFormat>? formats = null)
	{
		var context = archive.CreateContext(member);
		if (ModuleArchive.IsArchive(context.DataSpan)) throw new ModuleLoadException("Nested archives are not supported.");
		return LoadModule(context, formats);
	}

	public static IModuleSong Load(ReadOnlySpan<byte> data, IReadOnlyList<IModuleFormat>? formats = null)
	{
		return Load(new ModuleLoadContext(data.ToArray()), formats);
	}

	public static IModuleSong Load(ModuleLoadContext context, IReadOnlyList<IModuleFormat>? formats = null)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (ModuleArchive.IsArchive(context.DataSpan))
		{
			var archive = ModuleArchive.Open(context.Data);
			var members = GetArchiveMembers(archive, formats);
			if (members.Count == 0) throw new ModuleLoadException("Archive contains no playable modules.");
			return LoadArchiveMember(archive, members[0], formats);
		}
		return LoadModule(context, formats);
	}

	private static IModuleSong LoadModule(ModuleLoadContext context, IReadOnlyList<IModuleFormat>? formats)
	{
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
