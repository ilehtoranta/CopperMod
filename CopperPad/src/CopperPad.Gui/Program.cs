using Avalonia;

namespace CopperPad.Gui;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
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
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		catch (Exception ex)
		{
			CrashLog.Write("Fatal startup exception", ex);
			throw;
		}
	}

	private static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
}
