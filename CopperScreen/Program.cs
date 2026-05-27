using Avalonia;

namespace CopperScreen;

internal static class Program
{
	public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

	[STAThread]
	public static void Main(string[] args)
	{
		StartupArgs = args ?? Array.Empty<string>();
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(StartupArgs);
	}

	private static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
	}
}
