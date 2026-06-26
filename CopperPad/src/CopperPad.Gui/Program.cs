using Avalonia;

namespace CopperPad.Gui;

internal static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		var options = GuiStartupOptions.Parse(args);
		App.StartupOptions = options;
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception exception)
			{
				CrashLog.Write("Unhandled exception", exception);
			}
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			CrashLog.Write("Unobserved task exception", args.Exception);
			args.SetObserved();
		};

		try
		{
			return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		catch (Exception ex)
		{
			CrashLog.Write("Fatal startup exception", ex);
			return 1;
		}
	}

	private static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
}

internal sealed record GuiStartupOptions(bool SmokeTest)
{
	public static GuiStartupOptions Parse(string[] args)
		=> new(args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase)));
}
