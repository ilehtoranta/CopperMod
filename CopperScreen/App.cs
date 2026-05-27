using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace CopperScreen;

internal sealed class App : Application
{
	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = new MainWindow(Program.StartupArgs);
		}

		base.OnFrameworkInitializationCompleted();
	}
}
