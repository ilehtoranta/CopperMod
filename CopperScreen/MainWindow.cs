using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CopperScreen;

internal sealed class MainWindow : Window
{
	private const int AudioSampleRate = 44_100;
	private const int AudioChannels = 2;
	private const int AudioOutputBufferCount = 8;
	private const int TargetQueuedAudioBuffers = 5;
	private const int MaxFramesPerTick = 5;
	private readonly CopperScreenEmulator _emulator;
	private readonly CopperBenchViewModel _bench;
	private readonly FramebufferPresenter _presenter;
	private readonly Grid _root;
	private readonly Border _toolbar;
	private readonly Border _benchPanel;
	private readonly TextBlock _toolbarStatus;
	private readonly TextBlock _benchPath;
	private readonly TextBlock _benchDetails;
	private readonly Button _benchToggleButton;
	private readonly Button _pauseButton;
	private readonly ListBox _entryList;
	private readonly DispatcherTimer _timer;
	private readonly WaveOutAudioOutput? _audio;
	private readonly float[] _audioBuffer;
	private JoystickKeys _pressedJoystickKeys;
	private double? _lastMouseX;
	private double? _lastMouseY;

	public MainWindow(string[] args)
	{
		Title = "CopperScreen";
		Width = 960;
		Height = 768;
		Focusable = true;
		_emulator = CopperScreenEmulator.Create(args, AppContext.BaseDirectory);
		_bench = new CopperBenchViewModel(_emulator);
		_audioBuffer = new float[_emulator.AudioFramesPerAppFrame(AudioSampleRate) * AudioChannels];
		_audio = WaveOutAudioOutput.TryCreate(AudioSampleRate, AudioChannels, _audioBuffer.Length / AudioChannels, AudioOutputBufferCount);
		_presenter = new FramebufferPresenter(_emulator.Width, _emulator.Height)
		{
			Focusable = true,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
		};
		_toolbarStatus = new TextBlock();
		_benchPath = new TextBlock();
		_benchDetails = new TextBlock();
		_benchToggleButton = CreateToolbarButton("Bench", ToggleCopperBench);
		_pauseButton = CreateToolbarButton("Pause", TogglePause);
		_entryList = new ListBox();
		_root = new Grid();
		_benchPanel = CreateCopperBenchPanel();
		_toolbar = CreateToolbar();
		_root.Children.Add(_presenter);
		_root.Children.Add(_benchPanel);
		_root.Children.Add(_toolbar);
		Content = _root;
		RefreshCopperBenchUi();
		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
		_timer.Tick += (_, _) => PresentFrame();
		Opened += (_, _) =>
		{
			_presenter.Focus();
			PresentFrame(catchUpAudio: true);
			_timer.Start();
		};
		_presenter.PointerMoved += (_, args) => UpdateMousePort(args);
		_presenter.PointerPressed += (_, args) =>
		{
			_presenter.Focus();
			UpdateMousePort(args);
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
		};
		_presenter.PointerReleased += (_, args) =>
		{
			UpdateMousePort(args);
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
		};
		_presenter.PointerExited += (_, _) =>
		{
			_lastMouseX = null;
			_lastMouseY = null;
		};
		KeyDown += OnKeyDown;
		KeyUp += OnKeyUp;
		Deactivated += (_, _) => ReleaseInteractiveInput();
		Closed += (_, _) =>
		{
			_timer.Stop();
			_audio?.Dispose();
		};
	}

	private void PresentFrame(bool catchUpAudio = true)
	{
		var framesToRender = _emulator.IsPaused ? 0 : CalculateFramesToRender(_audio?.QueuedBufferCount, catchUpAudio);
		for (var frame = 0; frame < framesToRender; frame++)
		{
			_emulator.RenderNextFrame();
			var audioFrames = _emulator.RenderAudio(_audioBuffer, AudioSampleRate, AudioChannels);
			_audio?.Submit(_audioBuffer.AsSpan(0, audioFrames * AudioChannels));
		}

		_presenter.Update(_emulator.Framebuffer);
		UpdateToolbarStatus();
		Title = "CopperScreen - " + _emulator.StatusText + " - F10 CopperBench, F12 next disk";
	}

	private void UpdateMousePort(PointerEventArgs args)
	{
		var position = args.GetPosition(_presenter);
		var bounds = _presenter.Bounds;
		if (bounds.Width > 0 && bounds.Height > 0)
		{
			var mouseX = position.X * _emulator.Width / bounds.Width;
			var mouseY = position.Y * _emulator.Height / bounds.Height;
			if (_lastMouseX.HasValue && _lastMouseY.HasValue)
			{
				var deltaX = (int)Math.Round(mouseX - _lastMouseX.Value);
				var deltaY = (int)Math.Round(mouseY - _lastMouseY.Value);
				if (deltaX != 0 || deltaY != 0)
				{
					_emulator.MoveMousePort(deltaX, deltaY);
				}
			}

			_lastMouseX = mouseX;
			_lastMouseY = mouseY;
		}

		var properties = args.GetCurrentPoint(_presenter).Properties;
		_emulator.SetMouseButtons(properties.IsLeftButtonPressed, properties.IsRightButtonPressed);
	}

	private void OnKeyDown(object? sender, KeyEventArgs args)
	{
		if (args.Key == Key.F10)
		{
			ToggleCopperBench();
			args.Handled = true;
			return;
		}

		if (args.Key == Key.F9)
		{
			_bench.ToggleToolbar();
			RefreshCopperBenchUi();
			args.Handled = true;
			return;
		}

		if (args.Key == Key.Escape && _bench.IsOverlayVisible)
		{
			_bench.HideOverlay();
			RefreshCopperBenchUi();
			args.Handled = true;
			return;
		}

		if (args.Key == Key.Enter && _bench.IsOverlayVisible)
		{
			_bench.ActivateSelected();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
			args.Handled = true;
			return;
		}

		if (args.Key == Key.F12)
		{
			_bench.InsertNextDisk();
			RefreshCopperBenchUi();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
			return;
		}

		if (args.Key is Key.Space or Key.Enter)
		{
			_emulator.PulsePrimaryFire();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
			return;
		}

		if (IsJoystickKey(args.Key))
		{
			_pressedJoystickKeys |= GetJoystickKey(args.Key);
			UpdateJoystickPort();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
		}
	}

	private void OnKeyUp(object? sender, KeyEventArgs args)
	{
		if (!IsJoystickKey(args.Key))
		{
			return;
		}

		_pressedJoystickKeys &= ~GetJoystickKey(args.Key);
		UpdateJoystickPort();
		args.Handled = true;
		PresentFrame(catchUpAudio: false);
	}

	private void UpdateJoystickPort()
	{
		var up = IsPressed(JoystickKeys.NumPad8 | JoystickKeys.NumPad7 | JoystickKeys.NumPad9);
		var down = IsPressed(JoystickKeys.NumPad2 | JoystickKeys.NumPad1 | JoystickKeys.NumPad3);
		var left = IsPressed(JoystickKeys.NumPad4 | JoystickKeys.NumPad7 | JoystickKeys.NumPad1);
		var right = IsPressed(JoystickKeys.NumPad6 | JoystickKeys.NumPad9 | JoystickKeys.NumPad3);
		var primaryFire = IsPressed(JoystickKeys.NumPad5);
		var secondFire = IsPressed(JoystickKeys.Decimal | JoystickKeys.Delete);
		_emulator.SetJoystickPort(up, down, left, right, primaryFire, secondFire);
	}

	private bool IsPressed(JoystickKeys keys)
	{
		return (_pressedJoystickKeys & keys) != 0;
	}

	private static bool IsJoystickKey(Key key)
	{
		return key is Key.NumPad1 or Key.NumPad2 or Key.NumPad3 or Key.NumPad4 or Key.NumPad5 or
			Key.NumPad6 or Key.NumPad7 or Key.NumPad8 or Key.NumPad9 or Key.Decimal or Key.Delete;
	}

	private static JoystickKeys GetJoystickKey(Key key)
	{
		return key switch
		{
			Key.NumPad1 => JoystickKeys.NumPad1,
			Key.NumPad2 => JoystickKeys.NumPad2,
			Key.NumPad3 => JoystickKeys.NumPad3,
			Key.NumPad4 => JoystickKeys.NumPad4,
			Key.NumPad5 => JoystickKeys.NumPad5,
			Key.NumPad6 => JoystickKeys.NumPad6,
			Key.NumPad7 => JoystickKeys.NumPad7,
			Key.NumPad8 => JoystickKeys.NumPad8,
			Key.NumPad9 => JoystickKeys.NumPad9,
			Key.Decimal => JoystickKeys.Decimal,
			Key.Delete => JoystickKeys.Delete,
			_ => JoystickKeys.None
		};
	}

	private void ReleaseInteractiveInput()
	{
		_pressedJoystickKeys = JoystickKeys.None;
		_lastMouseX = null;
		_lastMouseY = null;
		_emulator.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
		_emulator.SetJoystickPort(
			up: false,
			down: false,
			left: false,
			right: false,
			primaryFirePressed: false,
			secondFirePressed: false);
	}

	private Border CreateToolbar()
	{
		var bar = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			VerticalAlignment = VerticalAlignment.Center
		};
		bar.Children.Add(_benchToggleButton);
		bar.Children.Add(_pauseButton);
		bar.Children.Add(CreateToolbarButton("Reset", () =>
		{
			_bench.Reset();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));
		bar.Children.Add(CreateToolbarButton("Fire", () =>
		{
			_bench.PulseFire();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));
		bar.Children.Add(CreateToolbarButton("Disk", OpenDiskPicker));
		bar.Children.Add(CreateToolbarButton("Next", () =>
		{
			_bench.InsertNextDisk();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));
		_toolbarStatus.Foreground = Brushes.White;
		_toolbarStatus.VerticalAlignment = VerticalAlignment.Center;
		_toolbarStatus.TextWrapping = TextWrapping.NoWrap;
		bar.Children.Add(_toolbarStatus);

		return new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(220, 18, 22, 28)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(70, 78, 92)),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(8, 6),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Top,
			Child = bar
		};
	}

	private Border CreateCopperBenchPanel()
	{
		var panel = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(new GridLength(1, GridUnitType.Star)),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto)
			}
		};

		var title = new TextBlock
		{
			Text = "CopperBench",
			FontSize = 18,
			FontWeight = FontWeight.SemiBold,
			Foreground = Brushes.White,
			Margin = new Thickness(0, 0, 0, 6)
		};
		Grid.SetRow(title, 0);
		panel.Children.Add(title);

		var navigation = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			Margin = new Thickness(0, 0, 0, 8)
		};
		navigation.Children.Add(CreatePanelButton("Up", () =>
		{
			_bench.GoUp();
			RefreshCopperBenchUi();
		}));
		navigation.Children.Add(CreatePanelButton("Refresh", () =>
		{
			_bench.Refresh();
			RefreshCopperBenchUi();
		}));
		_benchPath.Foreground = Brushes.White;
		_benchPath.VerticalAlignment = VerticalAlignment.Center;
		navigation.Children.Add(_benchPath);
		Grid.SetRow(navigation, 1);
		panel.Children.Add(navigation);

		_entryList.Background = new SolidColorBrush(Color.FromArgb(230, 8, 10, 14));
		_entryList.Foreground = Brushes.White;
		_entryList.SelectionChanged += (_, _) =>
		{
			_bench.SelectIndex(_entryList.SelectedIndex);
			RefreshCopperBenchDetails();
		};
		_entryList.DoubleTapped += (_, _) =>
		{
			_bench.ActivateSelected();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		};
		Grid.SetRow(_entryList, 2);
		panel.Children.Add(_entryList);

		_benchDetails.Foreground = new SolidColorBrush(Color.FromRgb(220, 226, 235));
		_benchDetails.TextWrapping = TextWrapping.Wrap;
		_benchDetails.Margin = new Thickness(0, 8, 0, 8);
		Grid.SetRow(_benchDetails, 3);
		panel.Children.Add(_benchDetails);

		var commands = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6
		};
		commands.Children.Add(CreatePanelButton("Open/Run", () =>
		{
			_bench.ActivateSelected();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));
		commands.Children.Add(CreatePanelButton("Hide", () =>
		{
			_bench.HideOverlay();
			RefreshCopperBenchUi();
		}));
		Grid.SetRow(commands, 4);
		panel.Children.Add(commands);

		return new Border
		{
			Width = 460,
			Margin = new Thickness(12, 48, 0, 12),
			Padding = new Thickness(12),
			CornerRadius = new CornerRadius(6),
			Background = new SolidColorBrush(Color.FromArgb(235, 20, 24, 31)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(92, 108, 132)),
			BorderThickness = new Thickness(1),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Stretch,
			Child = panel
		};
	}

	private static Button CreateToolbarButton(string text, Action action)
	{
		var button = new Button
		{
			Content = text,
			Padding = new Thickness(8, 3),
			MinWidth = 54
		};
		button.Click += (_, _) => action();
		return button;
	}

	private static Button CreatePanelButton(string text, Action action)
	{
		var button = new Button
		{
			Content = text,
			Padding = new Thickness(10, 4)
		};
		button.Click += (_, _) => action();
		return button;
	}

	private void ToggleCopperBench()
	{
		_bench.ToggleOverlay();
		RefreshCopperBenchUi();
	}

	private void TogglePause()
	{
		_bench.TogglePause();
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private async void OpenDiskPicker()
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
		{
			return;
		}

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Insert Amiga disk image",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("Amiga disk images")
				{
					Patterns = new[] { "*.adf", "*.zip" }
				}
			}
		});
		var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
		if (path == null)
		{
			return;
		}

		_bench.InsertDisk(path);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private void RefreshCopperBenchUi()
	{
		_toolbar.IsVisible = _bench.IsToolbarVisible;
		_benchPanel.IsVisible = _bench.IsOverlayVisible;
		_benchToggleButton.Content = _bench.IsOverlayVisible ? "Hide" : "Bench";
		_pauseButton.Content = _bench.IsPaused ? "Run" : "Pause";
		_benchPath.Text = _bench.DisplayPath;
		_entryList.ItemsSource = null;
		_entryList.ItemsSource = _bench.Entries;
		_entryList.SelectedIndex = _bench.SelectedIndex;
		RefreshCopperBenchDetails();
		UpdateToolbarStatus();
	}

	private void RefreshCopperBenchDetails()
	{
		_benchDetails.Text = _bench.SelectedDetails;
	}

	private void UpdateToolbarStatus()
	{
		_toolbarStatus.Text = $"{_emulator.DiskName} | {_emulator.DriveStatusText} | {_emulator.ProgramCounterText} | {_emulator.StatusText}";
	}

	[Flags]
	private enum JoystickKeys
	{
		None = 0,
		NumPad1 = 1 << 0,
		NumPad2 = 1 << 1,
		NumPad3 = 1 << 2,
		NumPad4 = 1 << 3,
		NumPad5 = 1 << 4,
		NumPad6 = 1 << 5,
		NumPad7 = 1 << 6,
		NumPad8 = 1 << 7,
		NumPad9 = 1 << 8,
		Decimal = 1 << 9,
		Delete = 1 << 10
	}

	internal static int CalculateFramesToRender(int? queuedAudioBuffers, bool catchUpAudio)
	{
		if (!queuedAudioBuffers.HasValue)
		{
			return 1;
		}

		var queued = queuedAudioBuffers.Value;
		if (queued >= AudioOutputBufferCount)
		{
			return 0;
		}

		if (!catchUpAudio)
		{
			return 1;
		}

		if (queued >= TargetQueuedAudioBuffers)
		{
			return 0;
		}

		var availableBuffers = AudioOutputBufferCount - queued;
		return Math.Clamp(TargetQueuedAudioBuffers - queued, 1, Math.Min(MaxFramesPerTick, availableBuffers));
	}
}
