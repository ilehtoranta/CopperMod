using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class MainWindow : Window
{
	private const int DisplayTimerIntervalMilliseconds = 20;
	private const int StatusUpdateIntervalMilliseconds = 250;
	private long _presentedFrames;
	private readonly CopperScreenRuntime _runtime;
	private readonly CopperBenchViewModel _bench;
	private readonly FramebufferPresenter _presenter;
	private readonly int[] _presentationFramebuffer;
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
	private readonly Button _overscanButton;
	private readonly StackPanel _entryList;
	private readonly DispatcherTimer _timer;
	private long _lastSeenFrameNumber;
	private long _lastStatusUpdateTick;
	private CopperScreenState _latestState;
	private readonly HashSet<AmigaRawKey> _pressedAmigaKeys = new HashSet<AmigaRawKey>();
	private JoystickKeys _pressedJoystickKeys;
	private NumpadInputMode _numpadMode = NumpadInputMode.Joystick;
	private bool _showFullOverscan = true;
	private double? _lastMouseX;
	private double? _lastMouseY;

	public MainWindow(string[] args)
	{
		Title = "CopperScreen";
		Icon = LoadWindowIcon();
		Width = 960;
		Height = 768;
		Focusable = true;
		_runtime = CopperScreenRuntime.Create(args, AppContext.BaseDirectory);
		_latestState = _runtime.CurrentState;
		_bench = new CopperBenchViewModel();
		_presentationFramebuffer = new int[_runtime.Width * _runtime.Height];

		_presenter = new FramebufferPresenter(_runtime.Width, _runtime.Height)
		{
			Focusable = true,
			Cursor = new Cursor(StandardCursorType.None),
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
		};
		ApplyPresenterViewport();
		_toolbarStatus = new TextBlock();
		_benchPath = new TextBlock();
		_benchDetails = new TextBlock();
		_benchToggleButton = CreateToolbarButton("Bench", ToggleCopperBench);
		_pauseButton = CreateToolbarButton("Pause", TogglePause);
		_numpadModeButton = CreateToolbarButton("Numpad: Joy", ToggleNumpadMode);
		_fullscreenButton = CreateToolbarButton("Full", ToggleFullscreen);
		_overscanButton = CreateToolbarButton("Overscan", ToggleOverscan);
		_entryList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
		_root = new Grid();
		_benchPanel = CreateCopperBenchPanel();
		_toolbar = CreateToolbar();
		_root.Children.Add(_presenter);
		_root.Children.Add(_benchPanel);
		_root.Children.Add(_toolbar);
		Content = _root;
		RefreshCopperBenchUi();
		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DisplayTimerIntervalMilliseconds) };
		_timer.Tick += (_, _) => PresentLatestFrame();
		Opened += (_, _) =>
		{
			_presenter.Focus();
			_runtime.Start();
			PresentLatestFrame(forceStatus: true);
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
			_runtime.Dispose();
		};
	}

	private static WindowIcon LoadWindowIcon()
	{
		using var stream = AssetLoader.Open(new Uri("avares://CopperScreen/Assets/CopperScreen.ico"));
		return new WindowIcon(stream);
	}

	private void PresentFrame(bool catchUpAudio = true)
	{
		_ = catchUpAudio;
		PresentLatestFrame(forceStatus: true);
	}

	private void PresentLatestFrame(bool forceStatus = false)
	{
		if (!_runtime.TryCopyLatestFrame(_presentationFramebuffer, ref _lastSeenFrameNumber, out var state, forceStatus))
		{
			return;
		}

		_latestState = state;
		if (state.FrameNumber != _presentedFrames)
		{
			_presenter.Update(_presentationFramebuffer);
			_presentedFrames = state.FrameNumber;
		}

		if (state.CopperBenchRequestPending)
		{
			_runtime.ConsumeCopperBenchRequest();
			_ = ShowCopperBenchAsync();
			RefreshCopperBenchUi();
		}

		var now = Environment.TickCount64;
		if (forceStatus ||
			state.CopperBenchRequestPending ||
			now - _lastStatusUpdateTick >= StatusUpdateIntervalMilliseconds)
		{
			_lastStatusUpdateTick = now;
			UpdateToolbarStatus(state);
			Title = "CopperScreen - " + state.ProfileName + " - " + state.StatusText + " - F11 toolbar, Alt+Enter fullscreen, F12 next disk, Shift+F12 previous disk, NumLock numpad mode";
			CopperScreenCrashLog.Heartbeat(() => BuildCrashLogState(state));
		}
	}

	private string BuildCrashLogState(CopperScreenState state)
	{
		return $"frame={_presentedFrames}, rendered={state.FramesRendered}, paused={state.IsPaused}, profile=\"{state.ProfileName}\", disk=\"{state.DiskName}\", drive=\"{state.DriveStatusText}\", {state.ProgramCounterText}, status=\"{state.StatusText}\", queuedAudio={state.QueuedAudioBuffers}, dropped={state.DroppedFrames}, audioSubmitFailures={state.AudioSubmitFailures}, emuMs={state.LastEmulationFrameMilliseconds:F2}, framebuffer={_runtime.Width}x{_runtime.Height}";
	}

	private void UpdateMousePort(PointerEventArgs args)
	{
		var position = args.GetPosition(_presenter);
		if (_presenter.TryMapPointToFramebuffer(position, out var framebufferPoint))
		{
			if (_lastMouseX.HasValue && _lastMouseY.HasValue)
			{
				var deltaX = (int)Math.Round(framebufferPoint.X - _lastMouseX.Value);
				var deltaY = (int)Math.Round(framebufferPoint.Y - _lastMouseY.Value);
				if (deltaX != 0 || deltaY != 0)
				{
					_runtime.MoveMousePort(deltaX, deltaY);
				}
			}

			_lastMouseX = framebufferPoint.X;
			_lastMouseY = framebufferPoint.Y;
		}
		else
		{
			_lastMouseX = null;
			_lastMouseY = null;
		}

		var properties = args.GetCurrentPoint(_presenter).Properties;
		_runtime.SetMouseButtons(properties.IsLeftButtonPressed, properties.IsRightButtonPressed);
	}

	private async void OnKeyDown(object? sender, KeyEventArgs args)
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
				await InsertPreviousDiskAsync().ConfigureAwait(true);
			}
			else
			{
				await InsertNextDiskAsync().ConfigureAwait(true);
			}

			args.Handled = true;
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
			await ActivateCopperBenchSelectedAsync().ConfigureAwait(true);
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
				_runtime.KeyDown(rawKey);
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
				_runtime.KeyUp(rawKey);
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
		_runtime.SetJoystickPort(up, down, left, right, primaryFire, secondFire);
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
			_runtime.KeyUp(rawKey);
		}

		_pressedAmigaKeys.Clear();
		_pressedJoystickKeys = JoystickKeys.None;
		_lastMouseX = null;
		_lastMouseY = null;
		_runtime.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
		_runtime.SetJoystickPort(
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
		bar.Children.Add(CreateToolbarButton("Reset", async () =>
		{
			await ResetRuntimeAsync().ConfigureAwait(true);
		}));
		bar.Children.Add(_fullscreenButton);
		bar.Children.Add(_overscanButton);
		bar.Children.Add(_numpadModeButton);
		bar.Children.Add(CreateToolbarButton("Disk", OpenDiskPicker));
		bar.Children.Add(CreateToolbarButton("Prev", async () =>
		{
			await InsertPreviousDiskAsync().ConfigureAwait(true);
		}));
		bar.Children.Add(CreateToolbarButton("Next", async () =>
		{
			await InsertNextDiskAsync().ConfigureAwait(true);
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
		navigation.Children.Add(CreatePanelButton("Up", async () =>
		{
			await _bench.GoUpAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
			RefreshCopperBenchUi();
		}));
		navigation.Children.Add(CreatePanelButton("Refresh", async () =>
		{
			await _bench.RefreshAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
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
			_ = ActivateCopperBenchSelectedAsync();
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

	private async void ToggleCopperBench()
	{
		await _bench.ToggleOverlayAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
		RefreshCopperBenchUi();
	}

	private async void TogglePause()
	{
		await ToggleRuntimePauseAsync().ConfigureAwait(true);
	}

	private void ToggleFullscreen()
	{
		WindowState = WindowState == WindowState.FullScreen
			? WindowState.Normal
			: WindowState.FullScreen;
		RefreshCopperBenchUi();
	}

	private void ToggleOverscan()
	{
		_showFullOverscan = !_showFullOverscan;
		ApplyPresenterViewport();
		RefreshCopperBenchUi();
	}

	private void ApplyPresenterViewport()
	{
		if (_showFullOverscan)
		{
			_presenter.SetSourceViewport(0, 0, _runtime.Width, _runtime.Height);
		}
		else
		{
			_presenter.SetSourceViewport(
				AmigaConstants.PalLowResOverscanBorderX,
				AmigaConstants.PalLowResOverscanBorderY,
				AmigaConstants.PalLowResStandardWidth,
				AmigaConstants.PalLowResStandardHeight);
		}

		_lastMouseX = null;
		_lastMouseY = null;
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

		await InsertDiskAsync(path).ConfigureAwait(true);
	}

	private async Task ShowCopperBenchAsync()
	{
		await _bench.ShowOverlayAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
		RefreshCopperBenchUi();
	}

	private async Task ToggleRuntimePauseAsync()
	{
		var result = await _runtime.TogglePausedAsync().ConfigureAwait(true);
		_latestState = result.State;
		_bench.SetStatusMessage(result.Message);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private async Task ResetRuntimeAsync()
	{
		var result = await _runtime.ResetAsync().ConfigureAwait(true);
		_latestState = result.State;
		_bench.ResetPath();
		if (_bench.IsOverlayVisible)
		{
			await _bench.RefreshAsync(result.State.DiskPath).ConfigureAwait(true);
		}

		_bench.SetStatusMessage(result.Message);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private Task InsertDiskAsync(string path)
		=> CompleteDiskCommandAsync(_runtime.InsertDiskAsync(path));

	private Task InsertNextDiskAsync()
		=> CompleteDiskCommandAsync(_runtime.InsertNextDiskAsync());

	private Task InsertPreviousDiskAsync()
		=> CompleteDiskCommandAsync(_runtime.InsertPreviousDiskAsync());

	private async Task CompleteDiskCommandAsync(Task<CopperScreenCommandResult> command)
	{
		var result = await command.ConfigureAwait(true);
		_latestState = result.State;
		if (result.Success)
		{
			_bench.ResetPath();
		}

		if (_bench.IsOverlayVisible)
		{
			await _bench.RefreshAsync(result.State.DiskPath).ConfigureAwait(true);
		}

		_bench.SetStatusMessage(result.Message);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private async Task ActivateCopperBenchSelectedAsync()
	{
		await _bench.ActivateSelectedAsync(
			_runtime.CurrentState.DiskPath,
			path => _runtime.LaunchCopperBenchPathAsync(path)).ConfigureAwait(true);
		_latestState = _runtime.CurrentState;
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private void RefreshCopperBenchUi()
	{
		_toolbar.IsVisible = _bench.IsToolbarVisible;
		_benchPanel.IsVisible = _bench.IsOverlayVisible;
		_benchToggleButton.Content = _bench.IsOverlayVisible ? "Hide" : "Bench";
		_pauseButton.Content = _latestState.IsPaused ? "Run" : "Pause";
		_numpadModeButton.Content = _numpadMode == NumpadInputMode.Joystick ? "Numpad: Joy" : "Numpad: Keys";
		_fullscreenButton.Content = WindowState == WindowState.FullScreen ? "Window" : "Full";
		_overscanButton.Content = _showFullOverscan ? "Crop" : "Overscan";
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
			button.DoubleTapped += async (_, _) =>
			{
				_bench.SelectIndex(index);
				await ActivateCopperBenchSelectedAsync().ConfigureAwait(true);
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
		UpdateToolbarStatus(_latestState);
	}

	private void UpdateToolbarStatus(CopperScreenState state)
	{
		_fullscreenButton.Content = WindowState == WindowState.FullScreen ? "Window" : "Full";
		_overscanButton.Content = _showFullOverscan ? "Crop" : "Overscan";
		_toolbarStatus.Text = $"{state.ProfileName} | {state.DiskName} | {state.DriveStatusText} | {state.ProgramCounterText} | {state.StatusText}";
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
		=> CopperScreenRuntime.CalculateFramesToRender(queuedAudioBuffers, catchUpAudio);
}
