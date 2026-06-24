using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace CopperPad.Gui;

internal sealed class App : Application
{
	public override void Initialize()
	{
		RequestedThemeVariant = ThemeVariant.Dark;
		Styles.Add(new FluentTheme());
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			try
			{
				desktop.MainWindow = new MainWindow();
			}
			catch (Exception ex)
			{
				CrashLog.Write("Main window initialization failed", ex);
				desktop.MainWindow = CreateCrashWindow(ex);
			}
		}

		base.OnFrameworkInitializationCompleted();
	}

	private static Window CreateCrashWindow(Exception exception)
		=> new()
		{
			Title = "CopperPad startup failed",
			Width = 760,
			Height = 360,
			Content = new StackPanel
			{
				Margin = new Thickness(16),
				Spacing = 10,
				Children =
				{
					new TextBlock
					{
						Text = "CopperPad could not start.",
						FontSize = 20,
						FontWeight = Avalonia.Media.FontWeight.SemiBold
					},
					new TextBlock
					{
						Text = exception.Message,
						TextWrapping = Avalonia.Media.TextWrapping.Wrap
					},
					new TextBlock
					{
						Text = "Crash log: " + CrashLog.Path,
						TextWrapping = Avalonia.Media.TextWrapping.Wrap
					}
				}
			}
		};
}
