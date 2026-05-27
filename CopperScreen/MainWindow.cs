using Avalonia.Controls;
using Avalonia.Threading;

namespace CopperScreen;

internal sealed class MainWindow : Window
{
	private readonly CopperScreenEmulator _emulator;
	private readonly FramebufferPresenter _presenter;
	private readonly DispatcherTimer _timer;

	public MainWindow(string[] args)
	{
		Title = "CopperScreen";
		Width = 960;
		Height = 768;
		_emulator = CopperScreenEmulator.Create(args, AppContext.BaseDirectory);
		_presenter = new FramebufferPresenter(_emulator.Width, _emulator.Height)
		{
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
		};
		Content = _presenter;
		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
		_timer.Tick += (_, _) => PresentFrame();
		Opened += (_, _) =>
		{
			PresentFrame();
			_timer.Start();
		};
		Closed += (_, _) => _timer.Stop();
	}

	private void PresentFrame()
	{
		_emulator.RenderNextFrame();
		_presenter.Update(_emulator.Framebuffer);
		Title = "CopperScreen - " + _emulator.StatusText;
	}
}
