using Terminal.Gui.App;

namespace CopperMod;

internal static class SixelCapability
{
	public static bool IsSupported(IApplication application)
	{
		if (application is null)
		{
			throw new ArgumentNullException(nameof(application));
		}

		return IsSupported(
			Environment.OSVersion.Version,
			OperatingSystem.IsWindows(),
			application.Driver?.SixelSupport?.IsSupported == true);
	}

	internal static bool IsSupported(Version osVersion, bool isWindows, bool terminalSupportsSixel)
	{
		return terminalSupportsSixel && IsSupportedWindowsBuild(osVersion, isWindows);
	}

	internal static bool IsSupportedWindowsBuild(Version osVersion, bool isWindows)
	{
		if (osVersion is null)
		{
			throw new ArgumentNullException(nameof(osVersion));
		}

		return isWindows && osVersion.Major >= 10 && osVersion.Build >= 22000;
	}
}
