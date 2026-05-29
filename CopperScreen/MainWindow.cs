using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CopperMod.Amiga;

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
	private readonly Button _numpadModeButton;
	private readonly Button _fullscreenButton;
	private readonly StackPanel _entryList;
	private readonly DispatcherTimer _timer;
	private readonly WaveOutAudioOutput? _audio;
	private readonly float[] _audioBuffer;
	private readonly HashSet<AmigaRawKey> _pressedAmigaKeys = new HashSet<AmigaRawKey>();
	private JoystickKeys _pressedJoystickKeys;
	private NumpadInputMode _numpadMode = NumpadInputMode.Joystick;
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
		_numpadModeButton = CreateToolbarButton("Numpad: Joy", ToggleNumpadMode);
		_fullscreenButton = CreateToolbarButton("Full", ToggleFullscreen);
		_entryList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
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
		if (_emulator.ConsumeCopperBenchRequest())
		{
			_bench.ShowOverlay();
			RefreshCopperBenchUi();
		}

		UpdateToolbarStatus();
		Title = "CopperScreen - " + _emulator.ProfileName + " - " + _emulator.StatusText + " - F11 toolbar, Alt+Enter fullscreen, F12 next disk, Shift+F12 previous disk, NumLock numpad mode";
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
		if (args.Key == Key.F11 || args.PhysicalKey == PhysicalKey.F11)
		{
			_bench.ToggleToolbar();
			RefreshCopperBenchUi();
			args.Handled = true;
			return;
		}

		if ((args.Key == Key.Enter || args.Key == Key.Return || args.PhysicalKey == PhysicalKey.Enter || args.PhysicalKey == PhysicalKey.NumPadEnter) &&
			(args.KeyModifiers & KeyModifiers.Alt) != 0)
		{
			ToggleFullscreen();
			args.Handled = true;
			return;
		}

		if (args.Key == Key.F12 || args.PhysicalKey == PhysicalKey.F12)
		{
			if ((args.KeyModifiers & KeyModifiers.Shift) != 0)
			{
				_bench.InsertPreviousDisk();
			}
			else
			{
				_bench.InsertNextDisk();
			}

			RefreshCopperBenchUi();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
			return;
		}

		if (args.Key == Key.NumLock || args.PhysicalKey == PhysicalKey.NumLock)
		{
			ToggleNumpadMode();
			args.Handled = true;
			return;
		}

		if ((args.Key == Key.Enter || args.Key == Key.Return || args.PhysicalKey == PhysicalKey.Enter || args.PhysicalKey == PhysicalKey.NumPadEnter) &&
			_bench.IsOverlayVisible)
		{
			_bench.ActivateSelected();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
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

		if (_numpadMode == NumpadInputMode.Joystick && TryGetJoystickKey(args, out var joystickKey))
		{
			_pressedJoystickKeys |= joystickKey;
			UpdateJoystickPort();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
			return;
		}

		if (AmigaHostKeyMapper.TryMap(args.Key, args.PhysicalKey, _numpadMode, out var rawKey))
		{
			if (_pressedAmigaKeys.Add(rawKey))
			{
				_emulator.KeyDown(rawKey);
				PresentFrame(catchUpAudio: false);
			}

			args.Handled = true;
		}
	}

	private void OnKeyUp(object? sender, KeyEventArgs args)
	{
		if (args.Key == Key.NumLock || args.PhysicalKey == PhysicalKey.NumLock)
		{
			args.Handled = true;
			return;
		}

		if (_numpadMode == NumpadInputMode.Joystick && TryGetJoystickKey(args, out var joystickKey))
		{
			_pressedJoystickKeys &= ~joystickKey;
			UpdateJoystickPort();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
			return;
		}

		if (AmigaHostKeyMapper.TryMap(args.Key, args.PhysicalKey, _numpadMode, out var rawKey))
		{
			if (_pressedAmigaKeys.Remove(rawKey))
			{
				_emulator.KeyUp(rawKey);
				PresentFrame(catchUpAudio: false);
			}

			args.Handled = true;
		}
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

	private static bool TryGetJoystickKey(KeyEventArgs args, out JoystickKeys joystickKey)
	{
		joystickKey = GetJoystickKey(args.Key, args.PhysicalKey);
		return joystickKey != JoystickKeys.None;
	}

	internal static JoystickKeys GetJoystickKey(Key key, PhysicalKey physicalKey)
	{
		if (physicalKey != PhysicalKey.None)
		{
			return physicalKey switch
			{
				PhysicalKey.NumPad1 => JoystickKeys.NumPad1,
				PhysicalKey.NumPad2 => JoystickKeys.NumPad2,
				PhysicalKey.NumPad3 => JoystickKeys.NumPad3,
				PhysicalKey.NumPad4 => JoystickKeys.NumPad4,
				PhysicalKey.NumPad5 or PhysicalKey.NumPadClear => JoystickKeys.NumPad5,
				PhysicalKey.NumPad6 => JoystickKeys.NumPad6,
				PhysicalKey.NumPad7 => JoystickKeys.NumPad7,
				PhysicalKey.NumPad8 => JoystickKeys.NumPad8,
				PhysicalKey.NumPad9 => JoystickKeys.NumPad9,
				PhysicalKey.NumPadDecimal => JoystickKeys.Decimal,
				_ => JoystickKeys.None
			};
		}

		return GetJoystickKeyFromLogicalKey(key);
	}

	private static JoystickKeys GetJoystickKeyFromLogicalKey(Key key)
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
		foreach (var rawKey in _pressedAmigaKeys)
		{
			_emulator.KeyUp(rawKey);
		}

		_pressedAmigaKeys.Clear();
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
		_toolbarStatus.Foreground = Brushes.White;
		_toolbarStatus.FontSize = 12;
		_toolbarStatus.TextWrapping = TextWrapping.NoWrap;
		_toolbarStatus.TextTrimming = TextTrimming.CharacterEllipsis;

		var bar = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		bar.Children.Add(_benchToggleButton);
		bar.Children.Add(_pauseButton);
		bar.Children.Add(CreateToolbarButton("Reset", () =>
		{
			_bench.Reset();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));
		bar.Children.Add(_fullscreenButton);
		bar.Children.Add(_numpadModeButton);
		bar.Children.Add(CreateToolbarButton("Disk", OpenDiskPicker));
		bar.Children.Add(CreateToolbarButton("Prev", () =>
		{
			_bench.InsertPreviousDisk();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));
		bar.Children.Add(CreateToolbarButton("Next", () =>
		{
			_bench.InsertNextDisk();
			RefreshCopperBenchUi();
			PresentFrame(catchUpAudio: false);
		}));

		var layout = new StackPanel
		{
			Orientation = Orientation.Vertical,
			Spacing = 5
		};
		layout.Children.Add(_toolbarStatus);
		layout.Children.Add(bar);

		return new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(220, 18, 22, 28)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(70, 78, 92)),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(8, 5),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Top,
			Child = layout
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

		var browserAndPreview = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(new GridLength(350)),
				new ColumnDefinition(new GridLength(1, GridUnitType.Star))
			}
		};

		var entryScroller = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(230, 8, 10, 14)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(48, 58, 72)),
			BorderThickness = new Thickness(1),
			Margin = new Thickness(0, 0, 10, 0),
			Child = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Content = _entryList
			}
		};
		Grid.SetColumn(entryScroller, 0);
		browserAndPreview.Children.Add(entryScroller);

		_benchDetails.Foreground = new SolidColorBrush(Color.FromRgb(220, 226, 235));
		_benchDetails.TextWrapping = TextWrapping.Wrap;
		_benchDetails.Margin = new Thickness(10);
		var previewPane = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(215, 13, 18, 26)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(48, 58, 72)),
			BorderThickness = new Thickness(1),
			Child = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Content = _benchDetails
			}
		};
		Grid.SetColumn(previewPane, 1);
		browserAndPreview.Children.Add(previewPane);
		Grid.SetRow(browserAndPreview, 2);
		panel.Children.Add(browserAndPreview);

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
		Grid.SetRow(commands, 3);
		panel.Children.Add(commands);

		return new Border
		{
			Width = 820,
			Margin = new Thickness(12, 78, 0, 12),
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
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromRgb(34, 40, 50)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(78, 90, 108)),
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
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromRgb(34, 40, 50)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(78, 90, 108)),
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

	private void ToggleFullscreen()
	{
		WindowState = WindowState == WindowState.FullScreen
			? WindowState.Normal
			: WindowState.FullScreen;
		RefreshCopperBenchUi();
	}

	private void ToggleNumpadMode()
	{
		ReleaseInteractiveInput();
		_numpadMode = _numpadMode == NumpadInputMode.Joystick
			? NumpadInputMode.AmigaKeys
			: NumpadInputMode.Joystick;
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
					Patterns = new[] { "*.adf", "*.ipf", "*.zip" }
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
		_numpadModeButton.Content = _numpadMode == NumpadInputMode.Joystick ? "Numpad: Joy" : "Numpad: Keys";
		_fullscreenButton.Content = WindowState == WindowState.FullScreen ? "Window" : "Full";
		_benchPath.Text = _bench.DisplayPath;
		RefreshEntryList();
		RefreshCopperBenchDetails();
		UpdateToolbarStatus();
	}

	private void RefreshEntryList()
	{
		_entryList.Children.Clear();
		for (var i = 0; i < _bench.Entries.Count; i++)
		{
			var entry = _bench.Entries[i];
			var index = i;
			var selected = index == _bench.SelectedIndex;
			var button = new Button
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Background = selected
					? new SolidColorBrush(Color.FromRgb(54, 75, 105))
					: new SolidColorBrush(Color.FromRgb(18, 24, 33)),
				BorderBrush = selected
					? new SolidColorBrush(Color.FromRgb(110, 150, 205))
					: new SolidColorBrush(Color.FromRgb(42, 52, 66)),
				BorderThickness = new Thickness(1),
				Padding = new Thickness(8, 5),
				Content = new TextBlock
				{
					Text = entry.ToString(),
					Foreground = Brushes.White,
					TextTrimming = TextTrimming.CharacterEllipsis
				}
			};
			button.Click += (_, _) =>
			{
				_bench.SelectIndex(index);
				RefreshCopperBenchUi();
			};
			button.DoubleTapped += (_, _) =>
			{
				_bench.SelectIndex(index);
				_bench.ActivateSelected();
				RefreshCopperBenchUi();
				PresentFrame(catchUpAudio: false);
			};
			_entryList.Children.Add(button);
		}
	}

	private void RefreshCopperBenchDetails()
	{
		_benchDetails.Text = _bench.SelectedDetails;
	}

	private void UpdateToolbarStatus()
	{
		_fullscreenButton.Content = WindowState == WindowState.FullScreen ? "Window" : "Full";
		_toolbarStatus.Text = $"{_emulator.ProfileName} | {_emulator.DiskName} | {_emulator.DriveStatusText} | {_emulator.ProgramCounterText} | {_emulator.StatusText}";
	}

	[Flags]
	internal enum JoystickKeys
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
