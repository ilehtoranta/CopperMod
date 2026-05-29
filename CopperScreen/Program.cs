using System.Runtime;
using System.Diagnostics;
using Avalonia;

namespace CopperScreen;

internal static class Program
{
	public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

	[STAThread]
	public static void Main(string[] args)
	{
		StartupArgs = args ?? Array.Empty<string>();
		CopperScreenCrashLog.Install(StartupArgs);
		try
		{
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
			GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
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

	private static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
	}
}
