using Avalonia;

namespace CopperPad.Gui;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
		=> BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

	private static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
}
