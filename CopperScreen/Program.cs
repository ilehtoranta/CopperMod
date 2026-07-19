using System.Runtime;
using System.Runtime.Loader;
using System.Diagnostics;
using Avalonia;
using CopperPad;

namespace CopperScreen;

internal static class Program
{
	public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

	[STAThread]
	public static void Main(string[] args)
	{
		StartupArgs = args ?? Array.Empty<string>();
		CopperScreenCrashLog.Install(StartupArgs);
		InstallCopperPadAssemblyResolver();
		try
		{
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
			//GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
			GCSettings.LatencyMode = GCLatencyMode.Interactive;
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(StartupArgs);
		}
		catch (Exception ex)
		{
			CopperScreenCrashLog.WriteException("Program.Main", ex, ex);
			throw;
		}
		finally
		{
			CopperScreenCrashLog.Shutdown();
		}
	}

	private static void InstallCopperPadAssemblyResolver()
	{
		// The current CopperPad.HidSharp preview requests CopperPad 2.0.0.0,
		// while its companion package ships the same API as assembly version 1.0.0.0.
		var copperPadAssembly = typeof(CopperControllerHost).Assembly;
		AssemblyLoadContext.Default.Resolving += (_, requestedAssembly) =>
			string.Equals(requestedAssembly.Name, copperPadAssembly.GetName().Name, StringComparison.Ordinal)
				? copperPadAssembly
				: null;
	}

	private static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
	}
}
