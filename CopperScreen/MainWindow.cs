using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CopperMod.Amiga;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CopperScreen;

internal sealed class MainWindow : Window
{
	private const int StatusUpdateIntervalMilliseconds = 250;
	private const double DebuggerPanelWidth = 1120;
	private const double DebuggerLeftColumnWidth = 500;
	private long _presentedFrames;
	private CopperScreenRuntime? _runtime;
	private readonly CopperBenchViewModel _bench;
	private readonly FramebufferPresenter _presenter;
	private readonly Grid _root;
	private readonly Border _toolbar;
	private readonly Border _settingsPanel;
	private readonly Border _benchPanel;
	private readonly Border _debuggerPanel;
	private readonly TextBlock _diskStatus;
	private readonly TextBlock _ledFilterStatus;
	private readonly TextBlock _cpuPcStatus;
	private readonly TextBlock _lastPcStatus;
	private readonly TextBlock _frameStatus;
	private readonly TextBlock _perfStatus;
	private Border _perfStatusBox = null!;
	private readonly TextBlock[] _driveStatusTexts = new TextBlock[4];
	private readonly Border[] _driveStatusBoxes = new Border[4];
	private readonly Button[] _driveStatusButtons = new Button[4];
	private Border _ledFilterBox = null!;
	private readonly TextBlock _benchPath;
	private readonly TextBlock _benchDetails;
	private readonly TextBlock _debuggerTitle;
	private readonly TextBlock _debuggerMessage;
	private readonly TextBlock _debuggerRegisters;
	private readonly TextBlock _debuggerDisassembly;
	private readonly TextBlock _debuggerStack;
	private readonly TextBlock _debuggerDevices;
	private readonly Button _benchToggleButton;
	private readonly Button _pauseButton;
	private readonly Button _numpadModeButton;
	private readonly Button _fullscreenButton;
	private readonly Button _overscanButton;
	private readonly Button _writeProtectButton;
	private readonly Button _settingsButton;
	private readonly StackPanel _entryList;
	private readonly CopperScreenStartupOptions _initialStartupOptions;
	private CopperScreenSettingsDraft _settingsDraft;
	private CopperScreenInputOptions _inputOptions = CopperScreenInputOptions.Default;
	private readonly CopperScreenJoystickActions[] _pressedJoystickActionsByPort = new CopperScreenJoystickActions[2];
	private TextBlock _settingsStatus = null!;
	private Button _settingsCloseButton = null!;
	private Button _settingsStartButton = null!;
	private TabControl _settingsTabs = null!;
	private int _settingsPageIndex;
	private string? _settingsStartupError;
	private ListBox _profileList = null!;
	private TextBlock _profileDirectoryText = null!;
	private TextBox _profileIdBox = null!;
	private TextBox _profileNameBox = null!;
	private TextBox _profileDescriptionBox = null!;
	private ComboBox _kickstartSourceBox = null!;
	private TextBox _kickstartRomBox = null!;
	private ComboBox _cpuBackendBox = null!;
	private TextBox _chipRamBox = null!;
	private TextBox _pseudoFastRamBox = null!;
	private TextBox _pseudoFastBaseBox = null!;
	private TextBox _realFastRamBox = null!;
	private TextBox _realFastBaseBox = null!;
	private CheckBox _rtcEnabledBox = null!;
	private ComboBox _floppyDriveCountBox = null!;
	private readonly TextBox[] _drivePathBoxes = new TextBox[4];
	private readonly Button[] _driveBrowseButtons = new Button[4];
	private readonly CheckBox[] _driveWriteProtectBoxes = new CheckBox[4];
	private CheckBox _floppySoundsEnabledBox = null!;
	private ComboBox _floppySoundModeBox = null!;
	private TextBox _floppySoundPackBox = null!;
	private TextBox _floppySoundVolumeBox = null!;
	private ComboBox _lacedPresentationModeBox = null!;
	private ComboBox _port1ControllerBox = null!;
	private ComboBox _port2ControllerBox = null!;
	private ComboBox _controllerProfileChooser = null!;
	private TextBox _controllerProfileNameBox = null!;
	private ComboBox _controllerProfileKindBox = null!;
	private readonly TextBox[] _joystickKeyBoxes = new TextBox[6];
	private bool _settingsVisible;
	private bool _updatingSettingsUi;
	private long _lastSeenFrameNumber;
	private long _lastStatusUpdateTick;
	private CopperScreenState _latestState;
	private readonly HashSet<AmigaRawKey> _pressedAmigaKeys = new HashSet<AmigaRawKey>();
	private NumpadInputMode _numpadMode = NumpadInputMode.Joystick;
	private bool _showFullOverscan = true;
	private double? _lastMouseX;
	private double? _lastMouseY;
	private double _mouseDeltaRemainderX;
	private double _mouseDeltaRemainderY;
	private bool _mouseGrabActive;
	private bool _mouseGrabWaitingForCenter;
	private bool _mouseGrabRecenterPending;
	private IPointer? _mouseGrabPointer;
	private Point? _mouseGrabCenterFramebufferPoint;
	private PixelPoint? _mouseGrabCenterScreenPoint;
	private CopperScreenDebugSnapshot? _visibleDebugSnapshot;
	private int _presentationQueued;
	private double _lastPresenterUpdateMilliseconds;
	private double _lastPresentationFrameMilliseconds;
	private bool _isClosed;

	public MainWindow(string[] args)
	{
		Title = "CopperScreen";
		Icon = LoadWindowIcon();
		SizeToContent = SizeToContent.WidthAndHeight;
		Focusable = true;
		_initialStartupOptions = CopperScreenStartupOptions.Parse(args, AppContext.BaseDirectory);
		_settingsDraft = CopperScreenSettingsDraft.FromStartupOptions(_initialStartupOptions);
		_settingsStartupError = _initialStartupOptions.Error;
		_inputOptions = _settingsDraft.Input;
		if (_initialStartupOptions.HasExplicitProfile)
		{
			_runtime = CopperScreenRuntime.Create(_initialStartupOptions);
			_latestState = _runtime.CurrentState;
		}
		else
		{
			_latestState = CreateIdleState(_settingsDraft);
			_settingsVisible = true;
		}

		_bench = new CopperBenchViewModel();

		_presenter = new FramebufferPresenter(AmigaConstants.PalHighResWidth, AmigaConstants.PalHighResHeight)
		{
			Focusable = true,
			Cursor = new Cursor(StandardCursorType.None),
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
		};
		ApplyPresenterViewport();
		_diskStatus = CreateToolbarTextBlock(fontSize: 11);
		_ledFilterStatus = CreateToolbarTextBlock(fontSize: 10, textAlignment: TextAlignment.Center);
		_cpuPcStatus = CreateToolbarTextBlock(fontSize: 10, textAlignment: TextAlignment.Center);
		_lastPcStatus = CreateToolbarTextBlock(fontSize: 10, textAlignment: TextAlignment.Center);
		_frameStatus = CreateToolbarTextBlock(fontSize: 10, textAlignment: TextAlignment.Center);
		_perfStatus = CreateToolbarTextBlock(fontSize: 10, textAlignment: TextAlignment.Center);
		_benchPath = new TextBlock();
		_benchDetails = new TextBlock();
		_debuggerTitle = new TextBlock();
		_debuggerMessage = new TextBlock();
		_debuggerRegisters = new TextBlock();
		_debuggerDisassembly = new TextBlock();
		_debuggerStack = new TextBlock();
		_debuggerDevices = new TextBlock();
		_benchToggleButton = CreateToolbarButton("Bench", ToggleCopperBench, "Show or hide the CopperBench overlay");
		_pauseButton = CreateToolbarButton("Pause", TogglePause, "Pause or resume emulation");
		_numpadModeButton = CreateToolbarButton("N:Joy", ToggleNumpadMode, "Toggle numpad between joystick emulation and Amiga numpad keys");
		_fullscreenButton = CreateToolbarButton("Full", ToggleFullscreen, "Toggle fullscreen mode");
		_overscanButton = CreateToolbarButton("Crop", ToggleOverscan, "Toggle between full overscan and cropped display");
		_settingsButton = CreateToolbarButton("Settings", ToggleSettings, "Show emulator settings");
		_writeProtectButton = CreateToolbarButton("WP ON", async () =>
		{
			await ToggleWriteProtectAsync().ConfigureAwait(true);
		}, "Toggle DF0: write protection");
		_entryList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
		_root = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(new GridLength(1, GridUnitType.Star))
			}
		};
		_benchPanel = CreateCopperBenchPanel();
		_debuggerPanel = CreateDebuggerPanel();
		_toolbar = CreateToolbar();
		_settingsPanel = CreateSettingsPanel();
		_root.Children.Add(_presenter);
		_root.Children.Add(_benchPanel);
		_root.Children.Add(_debuggerPanel);
		_root.Children.Add(_toolbar);
		_root.Children.Add(_settingsPanel);
		Content = _root;
		ApplyWindowPresentationMode();
		RefreshCopperBenchUi();
		RefreshDebuggerUi(_latestState.DebugSnapshot);
		RefreshSettingsUi();
		if (_runtime != null)
		{
			_runtime.FramePublished += QueueFramePresentation;
		}

		Opened += (_, _) =>
		{
			ApplyWindowPresentationMode();
			if (_settingsVisible)
			{
				_settingsPanel.Focus();
			}
			else
			{
			_presenter.Focus();
			}

			_runtime?.Start();
			PresentNextFrame(forceStatus: true);
		};
		_presenter.PointerMoved += (_, args) => UpdateMousePort(args);
		_presenter.PointerPressed += (_, args) =>
		{
			_presenter.Focus();
			if (BeginMouseGrab(args))
			{
				UpdateMouseButtons(args);
			}
			else
			{
				UpdateMousePort(args);
			}

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
			if (!_mouseGrabActive)
			{
				ResetMouseTracking();
			}
		};
		KeyDown += OnKeyDown;
		KeyUp += OnKeyUp;
		PropertyChanged += (_, args) =>
		{
			if (args.Property == WindowStateProperty)
			{
				RefreshCopperBenchUi();
			}
		};
		Deactivated += (_, _) => ReleaseInteractiveInput();
		Closed += (_, _) =>
		{
			_isClosed = true;
			if (_runtime != null)
			{
				_runtime.FramePublished -= QueueFramePresentation;
				_runtime.Dispose();
			}
		};
	}

	private static CopperScreenState CreateIdleState(CopperScreenSettingsDraft draft)
	{
		var drives = new CopperScreenDriveState[4];
		for (var i = 0; i < drives.Length; i++)
		{
			var connected = i < draft.FloppyDriveCount;
			var path = draft.DriveDiskPaths[i];
			drives[i] = new CopperScreenDriveState(
				i,
				connected,
				connected && !string.IsNullOrWhiteSpace(path),
				string.IsNullOrWhiteSpace(path) ? "No disk" : Path.GetFileName(path),
				string.IsNullOrWhiteSpace(path) ? null : path,
				0,
				0,
				false,
				false,
				draft.DriveWriteProtected[i] ?? false,
				false);
		}

		return new CopperScreenState(
			draft.DisplayName,
			string.IsNullOrWhiteSpace(draft.DriveDiskPaths[0]) ? "No disk" : Path.GetFileName(draft.DriveDiskPaths[0]) ?? "No disk",
			draft.DriveDiskPaths[0],
			new CopperScreenCpuState(0, 0, 0),
			drives,
			null,
			"settings",
			false,
			false,
			false,
			false,
			false,
			false,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			2,
			0,
			0,
			0,
			0);
	}

	private static WindowIcon LoadWindowIcon()
	{
		using var stream = AssetLoader.Open(new Uri("avares://CopperScreen/Assets/CopperScreen.ico"));
		return new WindowIcon(stream);
	}

	private void PresentFrame(bool catchUpAudio = true)
	{
		_ = catchUpAudio;
		PresentNextFrame(forceStatus: true);
	}

	private void QueueFramePresentation()
	{
		if (_isClosed || Interlocked.Exchange(ref _presentationQueued, 1) != 0)
		{
			return;
		}

		Dispatcher.UIThread.Post(PresentQueuedFrame, DispatcherPriority.Render);
	}

	private void PresentQueuedFrame()
	{
		Interlocked.Exchange(ref _presentationQueued, 0);
		if (_isClosed)
		{
			return;
		}

		PresentNextFrame();
		if (_runtime?.HasPendingPresentationFrames == true)
		{
			QueueFramePresentation();
		}
	}

	private void PresentNextFrame(bool forceStatus = false)
	{
		if (_runtime == null)
		{
			return;
		}

		var presentationStartTimestamp = Stopwatch.GetTimestamp();
		using var frameLease = _runtime.TryAcquireNextPresentationFrame(ref _lastSeenFrameNumber, forceStatus);
		if (frameLease == null)
		{
			return;
		}

		var state = frameLease.State;
		_latestState = state;
		RefreshDebuggerUi(state.DebugSnapshot);
		if (state.FrameNumber != _presentedFrames)
		{
			_presenter.Update(frameLease.Framebuffer);
			_lastPresenterUpdateMilliseconds = _presenter.LastUpdateMilliseconds;
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
			Title = "CopperScreen - " + state.ProfileName + " - Alt+Enter fullscreen, F10 release mouse, F11 toolbar in fullscreen, F12 next disk, Shift+F12 previous disk, NumLock numpad mode";
			CopperScreenCrashLog.Heartbeat(() => BuildCrashLogState(state));
		}

		_lastPresentationFrameMilliseconds = Stopwatch.GetElapsedTime(presentationStartTimestamp).TotalMilliseconds;
	}

	private string BuildCrashLogState(CopperScreenState state)
	{
		var drive = state.Drives.Length > 0 ? FormatDriveStatus(state.Drives[0]) : "DF0 unavailable";
		var debugger = state.DebugSnapshot == null ? "none" : state.DebugSnapshot.ReasonCode;
		return $"frame={_presentedFrames}, rendered={state.FramesRendered}, paused={state.IsPaused}, profile=\"{state.ProfileName}\", disk=\"{state.DiskName}\", drive=\"{drive}\", pc=0x{state.Cpu.ProgramCounter & 0x00FF_FFFF:X6}, lastPc=0x{state.Cpu.LastInstructionProgramCounter & 0x00FF_FFFF:X6}, sr=0x{state.Cpu.StatusRegister:X4}, filter={state.AudioFilterEnabled}, debugger={debugger}, status=\"{state.StatusText}\", queuedAudio={state.QueuedAudioBuffers}, dropped={state.DroppedFrames}, skipped={state.PresentationSkippedFrames}, bufferDropped={state.PresentationBufferDroppedFrames}, audioSubmitFailures={state.AudioSubmitFailures}, emuMs={state.LastEmulationFrameMilliseconds:F2}, publishMs={state.LastPublishFrameMilliseconds:F2}, presentMs={_lastPresentationFrameMilliseconds:F2}, uploadMs={_lastPresenterUpdateMilliseconds:F2}, renderMs={_presenter.LastRenderMilliseconds:F2}, framebuffer={_runtime?.Width ?? AmigaConstants.PalHighResWidth}x{_runtime?.Height ?? AmigaConstants.PalHighResHeight}";
	}

	private static string FormatDriveStatus(CopperScreenDriveState drive)
	{
		if (!drive.Connected)
		{
			return $"DF{drive.Index} disconnected";
		}

		if (!drive.HasDisk)
		{
			return $"DF{drive.Index} empty";
		}

		var flags = string.Concat(drive.ActiveDma ? 'D' : drive.MotorOn ? 'M' : '-', drive.Selected ? 'S' : '-');
		return $"DF{drive.Index} cyl {drive.Cylinder:00}.{drive.Head} {flags}";
	}

	private void UpdateMousePort(PointerEventArgs args)
	{
		var runtime = _runtime;
		if (runtime == null || _settingsVisible)
		{
			return;
		}

		if (_mouseGrabActive)
		{
			UpdateGrabbedMousePort(args);
			UpdateMouseButtons(args);
			return;
		}

		var position = args.GetPosition(_presenter);
		if (_presenter.TryMapPointToFramebuffer(position, out var framebufferPoint))
		{
			var mousePoint = MapPresentationPointToAmigaMousePoint(framebufferPoint);
			if (_lastMouseX.HasValue && _lastMouseY.HasValue)
			{
				var deltaX = ConsumeWholeMouseDelta(ref _mouseDeltaRemainderX, mousePoint.X - _lastMouseX.Value);
				var deltaY = ConsumeWholeMouseDelta(ref _mouseDeltaRemainderY, mousePoint.Y - _lastMouseY.Value);
				if (deltaX != 0 || deltaY != 0)
				{
					runtime.MoveMousePort(deltaX, deltaY);
				}
			}

			_lastMouseX = mousePoint.X;
			_lastMouseY = mousePoint.Y;
		}
		else
		{
			ResetMouseTracking();
		}

		UpdateMouseButtons(args);
	}

	private void UpdateGrabbedMousePort(PointerEventArgs args)
	{
		var runtime = _runtime;
		if (runtime == null)
		{
			return;
		}

		if (!_mouseGrabCenterFramebufferPoint.HasValue)
		{
			ReleaseMouseGrab();
			return;
		}

		if (TryGetGrabbedMouseFramebufferPoint(args, out var framebufferPoint, out var screenPoint))
		{
			var center = _mouseGrabCenterFramebufferPoint.Value;
			if (_mouseGrabWaitingForCenter)
			{
				if (!IsMouseGrabCenterPoint(framebufferPoint, center) ||
					(_mouseGrabCenterScreenPoint.HasValue &&
					screenPoint.HasValue &&
					!IsNearScreenPoint(screenPoint.Value, _mouseGrabCenterScreenPoint.Value)))
				{
					_ = RecenterMouseGrabPointer();
					return;
				}

				_mouseGrabWaitingForCenter = false;
				_mouseGrabRecenterPending = false;
				_mouseGrabCenterFramebufferPoint = center;
				_ = RecenterMouseGrabPointer();
				return;
			}

			if (_mouseGrabRecenterPending &&
				IsMouseGrabRecenterEcho(framebufferPoint, center, screenPoint, _mouseGrabCenterScreenPoint))
			{
				_mouseGrabRecenterPending = false;
				return;
			}

			_mouseGrabRecenterPending = false;
			var mousePoint = MapPresentationPointToAmigaMousePoint(framebufferPoint);
			var centerMousePoint = MapPresentationPointToAmigaMousePoint(center);
			var deltaX = ConsumeWholeMouseDelta(ref _mouseDeltaRemainderX, mousePoint.X - centerMousePoint.X);
			var deltaY = ConsumeWholeMouseDelta(ref _mouseDeltaRemainderY, mousePoint.Y - centerMousePoint.Y);
			if (deltaX != 0 || deltaY != 0)
			{
				runtime.MoveMousePort(deltaX, deltaY);
			}
		}

		_ = RecenterMouseGrabPointer();
	}

	private void UpdateMouseButtons(PointerEventArgs args)
	{
		if (_runtime == null)
		{
			return;
		}

		var properties = args.GetCurrentPoint(_presenter).Properties;
		_runtime.SetMouseButtons(properties.IsLeftButtonPressed, properties.IsRightButtonPressed);
	}

	internal static int ConsumeWholeMouseDelta(ref double remainder, double delta)
	{
		var accumulated = remainder + delta;
		var whole = (int)Math.Truncate(accumulated);
		remainder = accumulated - whole;
		return whole;
	}

	internal static Point MapPresentationPointToAmigaMousePoint(Point framebufferPoint)
		=> new(framebufferPoint.X * 0.5, framebufferPoint.Y * 0.5);

	private async void OnKeyDown(object? sender, KeyEventArgs args)
	{
		if (_settingsVisible)
		{
			return;
		}

		if (_runtime == null)
		{
			return;
		}

		if (_mouseGrabActive && (args.Key == Key.F10 || args.PhysicalKey == PhysicalKey.F10))
		{
			ReleaseMouseGrab();
			args.Handled = true;
			return;
		}

		if (args.Key == Key.F11 || args.PhysicalKey == PhysicalKey.F11)
		{
			ReleaseMouseGrab();
			if (WindowState == WindowState.FullScreen)
			{
				_bench.ToggleToolbar();
			}

			RefreshCopperBenchUi();
			args.Handled = true;
			return;
		}

		if ((args.Key == Key.Enter || args.Key == Key.Return || args.PhysicalKey == PhysicalKey.Enter || args.PhysicalKey == PhysicalKey.NumPadEnter) &&
			(args.KeyModifiers & KeyModifiers.Alt) != 0)
		{
			ReleaseMouseGrab();
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

		if (_numpadMode == NumpadInputMode.Joystick && TryGetJoystickActions(args, out var joystickPortIndex, out var joystickActions))
		{
			_pressedJoystickActionsByPort[joystickPortIndex] |= joystickActions;
			UpdateJoystickPort(joystickPortIndex);
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
		if (_settingsVisible || _runtime == null)
		{
			return;
		}

		if (args.Key == Key.NumLock || args.PhysicalKey == PhysicalKey.NumLock)
		{
			args.Handled = true;
			return;
		}

		if (_numpadMode == NumpadInputMode.Joystick && TryGetJoystickActions(args, out var joystickPortIndex, out var joystickActions))
		{
			_pressedJoystickActionsByPort[joystickPortIndex] &= ~joystickActions;
			UpdateJoystickPort(joystickPortIndex);
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

	private void UpdateJoystickPort(int portIndex)
	{
		if (_runtime == null)
		{
			return;
		}

		var actions = _pressedJoystickActionsByPort[portIndex];
		_runtime.SetJoystickPort(
			portIndex,
			IsPressed(actions, CopperScreenJoystickActions.Up),
			IsPressed(actions, CopperScreenJoystickActions.Down),
			IsPressed(actions, CopperScreenJoystickActions.Left),
			IsPressed(actions, CopperScreenJoystickActions.Right),
			IsPressed(actions, CopperScreenJoystickActions.Fire),
			IsPressed(actions, CopperScreenJoystickActions.SecondFire));
	}

	private static bool IsPressed(CopperScreenJoystickActions pressed, CopperScreenJoystickActions keys)
	{
		return (pressed & keys) != 0;
	}

	private bool TryGetJoystickActions(KeyEventArgs args, out int portIndex, out CopperScreenJoystickActions joystickActions)
	{
		for (portIndex = 0; portIndex < _pressedJoystickActionsByPort.Length; portIndex++)
		{
			if (!_inputOptions.TryGetKeyboardJoystickMap(portIndex, out var keyMap))
			{
				continue;
			}

			joystickActions = keyMap.GetActions(args.Key, args.PhysicalKey);
			if (joystickActions != CopperScreenJoystickActions.None)
			{
				return true;
			}
		}

		portIndex = -1;
		joystickActions = CopperScreenJoystickActions.None;
		return false;
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
		if (_runtime == null)
		{
			_pressedAmigaKeys.Clear();
			Array.Clear(_pressedJoystickActionsByPort);
			ReleaseMouseGrab();
			return;
		}

		foreach (var rawKey in _pressedAmigaKeys)
		{
			_runtime.KeyUp(rawKey);
		}

		_pressedAmigaKeys.Clear();
		Array.Clear(_pressedJoystickActionsByPort);
		ReleaseMouseGrab();
		_runtime.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
		for (var portIndex = 0; portIndex < _pressedJoystickActionsByPort.Length; portIndex++)
		{
			_runtime.SetJoystickPort(
				portIndex,
				up: false,
				down: false,
				left: false,
				right: false,
				primaryFirePressed: false,
				secondFirePressed: false);
		}
	}

	private Border CreateToolbar()
	{
		var controls = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		controls.Children.Add(_benchToggleButton);
		controls.Children.Add(_pauseButton);
		controls.Children.Add(CreateToolbarButton("Reset", async () =>
		{
			await ResetRuntimeAsync().ConfigureAwait(true);
		}, "Reset the emulated Amiga"));
		controls.Children.Add(_fullscreenButton);
		controls.Children.Add(_overscanButton);
		controls.Children.Add(_numpadModeButton);
		controls.Children.Add(_settingsButton);
		controls.Children.Add(CreateToolbarButton("Prev", async () =>
		{
			await InsertPreviousDiskAsync().ConfigureAwait(true);
		}, "Insert the previous disk image in the set"));
		controls.Children.Add(CreateToolbarButton("Next", async () =>
		{
			await InsertNextDiskAsync().ConfigureAwait(true);
		}, "Insert the next disk image in the set"));

		_ledFilterBox = CreateIndicatorBox(_ledFilterStatus, 64, "Power LED and audio filter state");
		var controlRow = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 5,
			VerticalAlignment = VerticalAlignment.Center
		};
		controlRow.Children.Add(controls);

		var statusRow = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 5,
			VerticalAlignment = VerticalAlignment.Center
		};
		statusRow.Children.Add(CreateIndicatorBox(_diskStatus, 180, "Current disk image"));
		statusRow.Children.Add(CreateIndicatorBox(_cpuPcStatus, 76, "Current 68000 program counter"));
		statusRow.Children.Add(CreateIndicatorBox(_lastPcStatus, 76, "Previous 68000 program counter"));
		statusRow.Children.Add(CreateIndicatorBox(_frameStatus, 92, "Published emulator frame counter"));
		_perfStatusBox = CreateIndicatorBox(_perfStatus, 76, "Emulation speed and frame timing");
		statusRow.Children.Add(_perfStatusBox);

		var drives = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4,
			VerticalAlignment = VerticalAlignment.Center
		};
		for (var driveIndex = 0; driveIndex < _driveStatusTexts.Length; driveIndex++)
		{
			var text = CreateToolbarTextBlock(fontSize: 10, textAlignment: TextAlignment.Center);
			var box = CreateIndicatorBox(text, 66, $"DF{driveIndex}: drive status");
			var button = CreateDriveStatusButton(box, driveIndex);
			_driveStatusTexts[driveIndex] = text;
			_driveStatusBoxes[driveIndex] = box;
			_driveStatusButtons[driveIndex] = button;
			drives.Children.Add(button);
		}

		var bottomRow = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 5,
			VerticalAlignment = VerticalAlignment.Center
		};
		bottomRow.Children.Add(drives);
		bottomRow.Children.Add(_writeProtectButton);
		bottomRow.Children.Add(_ledFilterBox);

		var layout = new StackPanel
		{
			Orientation = Orientation.Vertical,
			Spacing = 4
		};
		layout.Children.Add(controlRow);
		layout.Children.Add(statusRow);
		layout.Children.Add(bottomRow);

		return new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(220, 18, 22, 28)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(70, 78, 92)),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(6, 4),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Top,
			Child = layout
		};
	}

	private Border CreateSettingsPanel()
	{
		var panel = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(new GridLength(1, GridUnitType.Star)),
				new RowDefinition(GridLength.Auto)
			}
		};

		var header = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
				new ColumnDefinition(GridLength.Auto)
			},
			Margin = new Thickness(0, 0, 0, 10)
		};
		header.Children.Add(new TextBlock
		{
			Text = "CopperScreen Settings",
			FontSize = 22,
			FontWeight = FontWeight.SemiBold,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center
		});
		_settingsCloseButton = CreatePanelButton(_runtime == null ? "Exit" : "Close", HideSettings);
		Grid.SetColumn(_settingsCloseButton, 1);
		header.Children.Add(_settingsCloseButton);
		Grid.SetRow(header, 0);
		panel.Children.Add(header);

		_settingsTabs = new TabControl
		{
			Background = Brushes.Transparent,
			Foreground = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		_settingsTabs.Items.Add(CreateSettingsTab("General", CreateGeneralSettingsPage()));
		_settingsTabs.Items.Add(CreateSettingsTab("Advanced", CreateAdvancedSettingsPage()));
		_settingsTabs.SelectedIndex = _settingsPageIndex;
		_settingsTabs.SelectionChanged += (_, _) =>
		{
			if (_settingsTabs.SelectedIndex >= 0)
			{
				_settingsPageIndex = _settingsTabs.SelectedIndex;
			}
		};
		Grid.SetRow(_settingsTabs, 1);
		panel.Children.Add(_settingsTabs);

		var commands = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
				new ColumnDefinition(GridLength.Auto)
			},
			Margin = new Thickness(0, 10, 0, 0)
		};
		_settingsStatus = new TextBlock
		{
			Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap
		};
		commands.Children.Add(_settingsStatus);
		var buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		buttons.Children.Add(CreatePanelButton("Load", LoadSelectedProfile));
		buttons.Children.Add(CreatePanelButton("Save", SaveCurrentProfile));
		buttons.Children.Add(CreatePanelButton("Save As", SaveCurrentProfileAs));
		_settingsStartButton = CreatePanelButton("Start / Restart", () => _ = StartRuntimeFromSettingsAsync());
		buttons.Children.Add(_settingsStartButton);
		Grid.SetColumn(buttons, 1);
		commands.Children.Add(buttons);
		Grid.SetRow(commands, 2);
		panel.Children.Add(commands);

		return new Border
		{
			CornerRadius = new CornerRadius(6),
			Background = new SolidColorBrush(Color.FromArgb(246, 18, 22, 28)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(92, 108, 132)),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(12),
			Margin = new Thickness(12, 78, 12, 12),
			MaxWidth = 1120,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			IsVisible = _settingsVisible,
			Child = panel
		};
	}

	private static TabItem CreateSettingsTab(string text, Control content)
	{
		return new TabItem
		{
			Header = new TextBlock
			{
				Text = text,
				Foreground = new SolidColorBrush(Color.FromRgb(232, 242, 255)),
				FontWeight = FontWeight.SemiBold,
				Margin = new Thickness(8, 3)
			},
			Content = content,
			Background = new SolidColorBrush(Color.FromRgb(34, 40, 50)),
			Foreground = new SolidColorBrush(Color.FromRgb(232, 242, 255)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(78, 90, 108)),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(4, 2),
			Margin = new Thickness(0, 0, 6, 0)
		};
	}

	private Control CreateGeneralSettingsPage()
	{
		var layout = CreateSettingsForm();
		layout.Children.Add(CreateSettingsSection("Profiles"));
		_profileDirectoryText = new TextBlock
		{
			Foreground = new SolidColorBrush(Color.FromRgb(170, 184, 202)),
			TextWrapping = TextWrapping.Wrap
		};
		layout.Children.Add(CreateSettingsRow("Folder", _profileDirectoryText));
		_profileList = new ListBox
		{
			MinHeight = 140,
			MaxHeight = 180,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		layout.Children.Add(CreateSettingsRow("Choose", _profileList));
		layout.Children.Add(CreateSettingsSection("Profile"));
		_profileIdBox = AddTextSetting(layout, "Profile id");
		_profileNameBox = AddTextSetting(layout, "Display name");
		_profileDescriptionBox = AddTextSetting(layout, "Description");
		_kickstartSourceBox = AddComboSetting(layout, "Kickstart", ["CopperStart", "KickstartRom", "Kickstart13Rom", "DiagRom"]);
		_kickstartRomBox = AddTextSetting(layout, "Kickstart ROM");
		_cpuBackendBox = AddComboSetting(layout, "CPU", ["AccurateM68000", "AccurateM68020", "AccurateM68030", "AccurateM68040", "JitM68000", "JitM68040"]);
		layout.Children.Add(CreateSettingsSection("Memory"));
		_chipRamBox = AddTextSetting(layout, "Chip RAM KB");
		_pseudoFastRamBox = AddTextSetting(layout, "Pseudo-fast RAM KB");
		_pseudoFastBaseBox = AddTextSetting(layout, "Pseudo-fast base");
		_realFastRamBox = AddTextSetting(layout, "Real fast RAM KB");
		_realFastBaseBox = AddTextSetting(layout, "Real fast base");
		_rtcEnabledBox = new CheckBox { Content = "Enabled" };
		_rtcEnabledBox.IsCheckedChanged += (_, _) => ApplyRtcEnabledSetting();
		layout.Children.Add(CreateSettingsRow("RTC clock", _rtcEnabledBox));
		layout.Children.Add(CreateSettingsSection("Floppy drives"));
		_floppyDriveCountBox = AddComboSetting(layout, "Floppy drives", ["1", "2", "3", "4"], markRestart: false);
		_floppyDriveCountBox.SelectionChanged += (_, _) => ApplyFloppyDriveCountSetting();
		for (var driveIndex = 0; driveIndex < _drivePathBoxes.Length; driveIndex++)
		{
			var row = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6
			};
			var box = new TextBox
			{
				MinWidth = 420,
				PlaceholderText = $"DF{driveIndex} disk image"
			};
			var index = driveIndex;
			var browse = CreatePanelButton("Browse", () => _ = PickSettingsDiskAsync(index));
			box.TextChanged += (_, _) => ApplyDrivePathTextSetting(index);
			var writeProtect = new CheckBox
			{
				Content = "WP",
				VerticalAlignment = VerticalAlignment.Center
			};
			writeProtect.IsCheckedChanged += async (_, _) =>
			{
				if (_updatingSettingsUi)
				{
					return;
				}

				ClearSettingsStartupError();
				_settingsDraft.DriveWriteProtected[index] = writeProtect.IsChecked;
				if (_runtime != null && index < _latestState.Drives.Length && _latestState.Drives[index].Connected && _latestState.Drives[index].HasDisk)
				{
					var result = await _runtime.SetDriveWriteProtectedAsync(index, writeProtect.IsChecked == true).ConfigureAwait(true);
					_latestState = result.State;
					UpdateToolbarStatus();
				}
				else if (_runtime == null)
				{
					_latestState = CreateIdleState(_settingsDraft);
					UpdateToolbarStatus();
				}

				UpdateSettingsStatus();
			};
			_drivePathBoxes[driveIndex] = box;
			_driveBrowseButtons[driveIndex] = browse;
			_driveWriteProtectBoxes[driveIndex] = writeProtect;
			row.Children.Add(box);
			row.Children.Add(browse);
			row.Children.Add(writeProtect);
			layout.Children.Add(CreateSettingsRow($"DF{driveIndex}", row));
		}

		return new ScrollViewer { Content = layout };
	}

	private Control CreateAdvancedSettingsPage()
	{
		var layout = CreateSettingsForm();
		layout.Children.Add(CreateSettingsSection("Display"));
		_lacedPresentationModeBox = AddComboSetting(layout, "Laced display", ["CRT flicker", "Stable weave"], markRestart: false);
		_lacedPresentationModeBox.SelectionChanged += (_, _) => ApplyPresentationSettingsLive();
		layout.Children.Add(CreateSettingsSection("Audio"));
		_floppySoundsEnabledBox = new CheckBox { Content = "Enabled" };
		_floppySoundsEnabledBox.IsCheckedChanged += (_, _) => ApplyFloppySoundsEnabledSetting();
		layout.Children.Add(CreateSettingsRow("Floppy sounds", _floppySoundsEnabledBox));
		_floppySoundModeBox = AddComboSetting(layout, "Sound mode", ["Synthetic", "Samples"]);
		_floppySoundPackBox = AddTextSetting(layout, "Sound pack");
		_floppySoundVolumeBox = AddTextSetting(layout, "Sound volume");
		layout.Children.Add(CreateSettingsSection("Input"));
		_port1ControllerBox = AddComboSetting(layout, "Port 1 controller", [], markRestart: false);
		_port1ControllerBox.SelectionChanged += (_, _) => ApplyInputSettingsLive();
		_port2ControllerBox = AddComboSetting(layout, "Port 2 controller", [], markRestart: false);
		_port2ControllerBox.SelectionChanged += (_, _) => ApplyInputSettingsLive();
		_controllerProfileChooser = AddComboSetting(layout, "Edit profile", [], markRestart: false);
		_controllerProfileChooser.SelectionChanged += (_, _) => RefreshSelectedControllerProfileEditor();
		_controllerProfileNameBox = AddTextSetting(layout, "Profile name", markRestart: false);
		_controllerProfileNameBox.LostFocus += (_, _) => ApplyControllerProfileEditor();
		_controllerProfileKindBox = AddComboSetting(layout, "Profile kind", ["None", "Mouse", "KeyboardJoystick", "Gamepad"], markRestart: false);
		_controllerProfileKindBox.SelectionChanged += (_, _) => ApplyControllerProfileEditor();
		var labels = new[] { "Joy up", "Joy down", "Joy left", "Joy right", "Joy fire", "Joy second" };
		for (var i = 0; i < _joystickKeyBoxes.Length; i++)
		{
			var box = AddTextSetting(layout, labels[i], markRestart: false);
			var index = i;
			box.LostFocus += (_, _) => ApplyControllerProfileEditor();
			_joystickKeyBoxes[index] = box;
		}

		return new ScrollViewer { Content = layout };
	}

	private static StackPanel CreateSettingsForm()
	{
		return new StackPanel
		{
			Orientation = Orientation.Vertical,
			Spacing = 7,
			Margin = new Thickness(0, 2, 12, 2)
		};
	}

	private static TextBlock CreateSettingsSection(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = 15,
			FontWeight = FontWeight.SemiBold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 240, 248)),
			Margin = new Thickness(0, 8, 0, 2)
		};
	}

	private TextBox AddTextSetting(StackPanel layout, string label, bool markRestart = true)
	{
		var box = new TextBox { MinWidth = 320 };
		if (markRestart)
		{
			box.TextChanged += (_, _) => MarkSettingsRestartRequired();
		}

		layout.Children.Add(CreateSettingsRow(label, box));
		return box;
	}

	private ComboBox AddComboSetting(StackPanel layout, string label, string[] items, bool markRestart = true)
	{
		var combo = new ComboBox
		{
			ItemsSource = items,
			MinWidth = 180
		};
		if (markRestart)
		{
			combo.SelectionChanged += (_, _) => MarkSettingsRestartRequired();
		}

		layout.Children.Add(CreateSettingsRow(label, combo));
		return combo;
	}

	private static Control CreateSettingsRow(string label, Control control)
	{
		var row = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(new GridLength(150)),
				new ColumnDefinition(new GridLength(1, GridUnitType.Star))
			}
		};
		row.Children.Add(new TextBlock
		{
			Text = label,
			Foreground = new SolidColorBrush(Color.FromRgb(198, 208, 220)),
			VerticalAlignment = VerticalAlignment.Center
		});
		Grid.SetColumn(control, 1);
		row.Children.Add(control);
		return row;
	}

	private void ToggleSettings()
	{
		_settingsVisible = !_settingsVisible;
		if (_settingsVisible)
		{
			RefreshSettingsUi();
			ReleaseInteractiveInput();
		}

		RefreshCopperBenchUi();
	}

	private void HideSettings()
	{
		if (_runtime == null)
		{
			Close();
			return;
		}

		_settingsVisible = false;
		RefreshCopperBenchUi();
		_presenter.Focus();
	}

	private void MarkSettingsRestartRequired()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		ClearSettingsStartupError();
		_settingsDraft.MarkRequiresRestart();
		UpdateSettingsStatus();
	}

	private void ClearSettingsStartupError()
	{
		_settingsStartupError = null;
	}

	private void ApplyFloppyDriveCountSetting()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		ClearSettingsStartupError();
		_settingsDraft.FloppyDriveCount = ParseDriveCount(_floppyDriveCountBox.SelectedItem);
		_settingsDraft.MarkRequiresRestart();
		UpdateDriveSettingsEnabled();
		if (_runtime == null)
		{
			_latestState = CreateIdleState(_settingsDraft);
			UpdateToolbarStatus();
		}

		UpdateSettingsStatus();
	}

	private void ApplyRtcEnabledSetting()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		_settingsDraft.RtcEnabled = _rtcEnabledBox.IsChecked == true;
		MarkSettingsRestartRequired();
	}

	private void ApplyDrivePathTextSetting(int driveIndex)
	{
		if ((uint)driveIndex >= (uint)_settingsDraft.DriveDiskPaths.Length)
		{
			return;
		}

		var text = _drivePathBoxes[driveIndex].Text;
		_settingsDraft.DriveDiskPaths[driveIndex] = string.IsNullOrWhiteSpace(text)
			? null
			: text.Trim();
		if (_updatingSettingsUi)
		{
			return;
		}

		ClearSettingsStartupError();
		_settingsDraft.MarkRequiresRestart();
		if (_runtime == null)
		{
			_latestState = CreateIdleState(_settingsDraft);
			UpdateToolbarStatus();
		}

		UpdateSettingsStatus();
	}

	private void ApplyFloppySoundsEnabledSetting()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		_settingsDraft.FloppyDriveAudio = _settingsDraft.FloppyDriveAudio with
		{
			Enabled = _floppySoundsEnabledBox.IsChecked == true
		};
		MarkSettingsRestartRequired();
	}

	private void UpdateDriveSettingsEnabled()
	{
		for (var driveIndex = 0; driveIndex < _drivePathBoxes.Length; driveIndex++)
		{
			var connected = driveIndex < _settingsDraft.FloppyDriveCount;
			_drivePathBoxes[driveIndex].IsEnabled = connected;
			_driveBrowseButtons[driveIndex].IsEnabled = connected;
			_driveWriteProtectBoxes[driveIndex].IsEnabled = connected;
		}
	}

	private void SetDrivePathTextSilently(int driveIndex, string path)
	{
		if ((uint)driveIndex >= (uint)_drivePathBoxes.Length)
		{
			return;
		}

		var wasUpdating = _updatingSettingsUi;
		_updatingSettingsUi = true;
		try
		{
			_drivePathBoxes[driveIndex].Text = path;
		}
		finally
		{
			_updatingSettingsUi = wasUpdating;
		}
	}

	private void RefreshSettingsUi()
	{
		_updatingSettingsUi = true;
		try
		{
			_settingsPanel.IsVisible = _settingsVisible;
			_settingsCloseButton.Content = _runtime == null ? "Exit" : "Close";
			var profilesDirectory = CopperScreenProfileStore.FindProfilesDirectory(AppContext.BaseDirectory);
			var profiles = CopperScreenProfileStore.ListProfiles(AppContext.BaseDirectory);
			_profileDirectoryText.Text = profilesDirectory;
			_profileList.ItemsSource = profiles;
			_profileList.SelectedItem = profiles.FirstOrDefault(profile => string.Equals(profile.Id, _settingsDraft.Id, StringComparison.OrdinalIgnoreCase));
			_profileIdBox.Text = _settingsDraft.Id;
			_profileNameBox.Text = _settingsDraft.DisplayName;
			_profileDescriptionBox.Text = _settingsDraft.Description;
			_kickstartSourceBox.SelectedItem = _settingsDraft.KickstartSource.ToString();
			_kickstartRomBox.Text = _settingsDraft.KickstartRomPath ?? string.Empty;
			_cpuBackendBox.SelectedItem = _settingsDraft.CpuBackend.ToString();
			_chipRamBox.Text = _settingsDraft.ChipRamKb.ToString(System.Globalization.CultureInfo.InvariantCulture);
			_pseudoFastRamBox.Text = _settingsDraft.PseudoFastRamKb.ToString(System.Globalization.CultureInfo.InvariantCulture);
			_pseudoFastBaseBox.Text = _settingsDraft.PseudoFastBase;
			_realFastRamBox.Text = _settingsDraft.RealFastRamKb.ToString(System.Globalization.CultureInfo.InvariantCulture);
			_realFastBaseBox.Text = _settingsDraft.RealFastBase;
			_rtcEnabledBox.IsChecked = _settingsDraft.RtcEnabled;
			_floppyDriveCountBox.SelectedItem = _settingsDraft.FloppyDriveCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
			for (var driveIndex = 0; driveIndex < _drivePathBoxes.Length; driveIndex++)
			{
				_drivePathBoxes[driveIndex].Text = _settingsDraft.DriveDiskPaths[driveIndex] ?? string.Empty;
				_driveWriteProtectBoxes[driveIndex].IsChecked = _settingsDraft.DriveWriteProtected[driveIndex];
			}
			UpdateDriveSettingsEnabled();

			_floppySoundsEnabledBox.IsChecked = _settingsDraft.FloppyDriveAudio.Enabled;
			_floppySoundModeBox.SelectedItem = _settingsDraft.FloppyDriveAudio.Mode.ToString();
			_floppySoundPackBox.Text = _settingsDraft.FloppyDriveAudio.SoundPack;
			_floppySoundVolumeBox.Text = _settingsDraft.FloppyDriveAudio.Volume.ToString(System.Globalization.CultureInfo.InvariantCulture);
			_lacedPresentationModeBox.SelectedItem = FormatLacedPresentationMode(_settingsDraft.PresentationOptions.LacedMode);
			RefreshControllerSelectors();
			_settingsTabs.SelectedIndex = Math.Clamp(_settingsPageIndex, 0, _settingsTabs.ItemCount - 1);
		}
		finally
		{
			_updatingSettingsUi = false;
		}

		UpdateSettingsStatus();
	}

	private void RefreshControllerSelectors()
	{
		var wasUpdating = _updatingSettingsUi;
		_updatingSettingsUi = true;
		try
		{
			var profiles = _settingsDraft.Input.ControllerProfiles.ToArray();
			_port1ControllerBox.ItemsSource = profiles;
			_port2ControllerBox.ItemsSource = profiles;
			_controllerProfileChooser.ItemsSource = profiles;
			SelectControllerProfile(_port1ControllerBox, _settingsDraft.Input.Port1ProfileId);
			SelectControllerProfile(_port2ControllerBox, _settingsDraft.Input.Port2ProfileId);
			if (_controllerProfileChooser.SelectedItem is not CopperScreenControllerProfile selected ||
				!profiles.Any(profile => string.Equals(profile.Id, selected.Id, StringComparison.OrdinalIgnoreCase)))
			{
				_controllerProfileChooser.SelectedItem = profiles.FirstOrDefault(profile => profile.Kind == CopperScreenControllerKind.KeyboardJoystick) ??
					profiles.FirstOrDefault();
			}

			RefreshSelectedControllerProfileEditor();
		}
		finally
		{
			_updatingSettingsUi = wasUpdating;
		}
	}

	private static void SelectControllerProfile(ComboBox combo, string profileId)
	{
		if (combo.ItemsSource is not IEnumerable<CopperScreenControllerProfile> profiles)
		{
			return;
		}

		combo.SelectedItem = profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
	}

	private void UpdateSettingsStatus()
	{
		if (_settingsStatus == null)
		{
			return;
		}

		_settingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
		var valid = CanStartFromSettings(out var validationError);
		if (_settingsStartButton != null)
		{
			_settingsStartButton.IsEnabled = valid;
		}

		if (!valid)
		{
			_settingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 194, 194));
			_settingsStatus.Text = validationError;
			return;
		}

		if (!string.IsNullOrWhiteSpace(_settingsStartupError))
		{
			_settingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 194, 194));
			_settingsStatus.Text = _settingsStartupError;
			return;
		}

		_settingsStatus.Text = _runtime == null
			? "Choose a profile or edit settings, then start the emulator."
			: _settingsDraft.RequiresRestart
				? "Machine settings changed. Restart to apply them; disk and input changes can apply live."
				: "Settings are current.";
	}

	private bool CanStartFromSettings(out string error)
	{
		error = string.Empty;
		if (_profileIdBox == null)
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(_profileIdBox.Text))
		{
			error = "Profile id is required.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(_profileNameBox.Text))
		{
			error = "Display name is required.";
			return false;
		}

		try
		{
			_ = ParsePositiveInt(_chipRamBox.Text, "Chip RAM KB");
			_ = ParseNonNegativeInt(_pseudoFastRamBox.Text, "Pseudo-fast RAM KB");
			_ = ParseNonNegativeInt(_realFastRamBox.Text, "Real fast RAM KB");
			_ = ParseFloat(_floppySoundVolumeBox.Text, "Sound volume");
		}
		catch (InvalidOperationException ex)
		{
			error = ex.Message;
			return false;
		}

		return true;
	}

	private void LoadSelectedProfile()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		if (_profileList.SelectedItem is not CopperScreenProfileSummary summary)
		{
			SetSettingsError("Select a profile to load.");
			return;
		}

		if (!CopperScreenProfile.TryLoad(summary.Path, AppContext.BaseDirectory, out var profile, out var error))
		{
			SetSettingsError(error ?? "Could not load profile.");
			return;
		}

		ClearSettingsStartupError();
		_settingsDraft = CopperScreenSettingsDraft.FromProfile(profile);
		_settingsDraft.MarkRequiresRestart();
		_inputOptions = _settingsDraft.Input;
		_runtime?.SetPresentationOptions(_settingsDraft.PresentationOptions);
		RefreshSettingsUi();
	}

	private void SaveCurrentProfile()
	{
		if (!TryApplySettingsFromUi(out var error))
		{
			SetSettingsError(error);
			return;
		}

		try
		{
			var path = CopperScreenProfileStore.Save(_settingsDraft, AppContext.BaseDirectory);
			RefreshSettingsUi();
			_settingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(206, 248, 213));
			_settingsStatus.Text = "Saved " + path;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
		{
			SetSettingsError(ex.Message);
		}
	}

	private void SaveCurrentProfileAs()
	{
		if (!TryApplySettingsFromUi(out var error))
		{
			SetSettingsError(error);
			return;
		}

		try
		{
			var path = CopperScreenProfileStore.SaveAs(_settingsDraft, AppContext.BaseDirectory);
			RefreshSettingsUi();
			_settingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(206, 248, 213));
			_settingsStatus.Text = "Saved copy " + path;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
		{
			SetSettingsError(ex.Message);
		}
	}

	private async Task StartRuntimeFromSettingsAsync()
	{
		if (!TryApplySettingsFromUi(out var error))
		{
			SetSettingsError(error);
			return;
		}

		CopperScreenStartupOptions options;
		try
		{
			options = _settingsDraft.ToStartupOptions(AppContext.BaseDirectory);
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException or FormatException)
		{
			SetSettingsError(ex.Message);
			return;
		}

		ReleaseInteractiveInput();
		if (_runtime != null)
		{
			_runtime.FramePublished -= QueueFramePresentation;
			_runtime.Dispose();
		}

		_runtime = CopperScreenRuntime.Create(options);
		_runtime.FramePublished += QueueFramePresentation;
		_latestState = _runtime.CurrentState;
		_lastSeenFrameNumber = 0;
		_presentedFrames = 0;
		_settingsDraft.ClearRestartRequired();
		_settingsVisible = false;
		_runtime.Start();
		await _bench.RefreshAsync(_latestState.DiskPath).ConfigureAwait(true);
		RefreshDebuggerUi(_latestState.DebugSnapshot);
		RefreshCopperBenchUi();
		PresentNextFrame(forceStatus: true);
		_presenter.Focus();
	}

	private bool TryApplySettingsFromUi(out string error)
	{
		error = string.Empty;
		try
		{
			_settingsDraft.Id = _profileIdBox.Text?.Trim() ?? string.Empty;
			_settingsDraft.DisplayName = _profileNameBox.Text?.Trim() ?? string.Empty;
			_settingsDraft.Description = _profileDescriptionBox.Text?.Trim() ?? string.Empty;
			_settingsDraft.KickstartSource = ParseKickstartSourceSelection(_kickstartSourceBox.SelectedItem);
			_settingsDraft.KickstartRomPath = string.IsNullOrWhiteSpace(_kickstartRomBox.Text) ? null : _kickstartRomBox.Text.Trim();
			_settingsDraft.CpuBackend = ParseCpuSelection(_cpuBackendBox.SelectedItem);
			_settingsDraft.ChipRamKb = ParsePositiveInt(_chipRamBox.Text, "Chip RAM KB");
			_settingsDraft.PseudoFastRamKb = ParseNonNegativeInt(_pseudoFastRamBox.Text, "Pseudo-fast RAM KB");
			_settingsDraft.PseudoFastBase = _pseudoFastBaseBox.Text?.Trim() ?? "$C00000";
			_settingsDraft.RealFastRamKb = ParseNonNegativeInt(_realFastRamBox.Text, "Real fast RAM KB");
			_settingsDraft.RealFastBase = _realFastBaseBox.Text?.Trim() ?? "$200000";
			_settingsDraft.RtcEnabled = _rtcEnabledBox.IsChecked == true;
			_settingsDraft.FloppyDriveCount = ParseDriveCount(_floppyDriveCountBox.SelectedItem);
			for (var driveIndex = 0; driveIndex < _drivePathBoxes.Length; driveIndex++)
			{
				var drivePathText = _drivePathBoxes[driveIndex].Text;
				_settingsDraft.DriveDiskPaths[driveIndex] = string.IsNullOrWhiteSpace(drivePathText)
					? null
					: drivePathText.Trim();
				_settingsDraft.DriveWriteProtected[driveIndex] = _driveWriteProtectBoxes[driveIndex].IsChecked;
			}

			_settingsDraft.FloppyDriveAudio = new FloppyDriveAudioOptions(
				_floppySoundsEnabledBox.IsChecked == true,
				ParseFloppyAudioModeSelection(_floppySoundModeBox.SelectedItem),
				string.IsNullOrWhiteSpace(_floppySoundPackBox.Text)
					? FloppyDriveAudioOptions.DefaultSoundPack
					: _floppySoundPackBox.Text.Trim(),
				FloppyDriveAudioOptions.ClampVolume(ParseFloat(_floppySoundVolumeBox.Text, "Sound volume")));
			ApplyControllerProfileEditor();
			_settingsDraft.Input = ReadInputOptionsFromUi();
			_settingsDraft.PresentationOptions = ReadPresentationOptionsFromUi();
			_inputOptions = _settingsDraft.Input;
			_runtime?.SetInputOptions(_inputOptions);
			_runtime?.SetPresentationOptions(_settingsDraft.PresentationOptions);
			ClearSettingsStartupError();
			return true;
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or OverflowException)
		{
			error = ex.Message;
			return false;
		}
	}

	private CopperScreenInputOptions ReadInputOptionsFromUi()
	{
		var port1Profile = _port1ControllerBox.SelectedItem as CopperScreenControllerProfile;
		var port2Profile = _port2ControllerBox.SelectedItem as CopperScreenControllerProfile;
		return CopperScreenInputOptions.Create(
			port1Profile?.Id,
			port2Profile?.Id,
			_settingsDraft.Input.ControllerProfiles);
	}

	private CopperScreenPresentationOptions ReadPresentationOptionsFromUi()
	{
		return new CopperScreenPresentationOptions(
			ParseLacedPresentationModeSelection(_lacedPresentationModeBox.SelectedItem));
	}

	private void RefreshSelectedControllerProfileEditor()
	{
		if (_controllerProfileChooser.SelectedItem is not CopperScreenControllerProfile profile)
		{
			return;
		}

		_updatingSettingsUi = true;
		try
		{
			_controllerProfileNameBox.Text = profile.DisplayName;
			_controllerProfileKindBox.SelectedItem = profile.Kind.ToString();
			var keyboardProfile = profile.Kind == CopperScreenControllerKind.KeyboardJoystick;
			for (var i = 0; i < _joystickKeyBoxes.Length; i++)
			{
				_joystickKeyBoxes[i].IsEnabled = keyboardProfile;
			}

			_joystickKeyBoxes[0].Text = string.Join(", ", profile.JoystickKeys.Up);
			_joystickKeyBoxes[1].Text = string.Join(", ", profile.JoystickKeys.Down);
			_joystickKeyBoxes[2].Text = string.Join(", ", profile.JoystickKeys.Left);
			_joystickKeyBoxes[3].Text = string.Join(", ", profile.JoystickKeys.Right);
			_joystickKeyBoxes[4].Text = string.Join(", ", profile.JoystickKeys.Fire);
			_joystickKeyBoxes[5].Text = string.Join(", ", profile.JoystickKeys.SecondFire);
		}
		finally
		{
			_updatingSettingsUi = false;
		}
	}

	private void ApplyControllerProfileEditor()
	{
		if (_updatingSettingsUi || _controllerProfileChooser.SelectedItem is not CopperScreenControllerProfile profile)
		{
			return;
		}

		try
		{
			ClearSettingsStartupError();
			var updated = profile with
			{
				DisplayName = string.IsNullOrWhiteSpace(_controllerProfileNameBox.Text)
					? profile.DisplayName
					: _controllerProfileNameBox.Text.Trim(),
				Kind = ParseControllerKindSelection(_controllerProfileKindBox.SelectedItem),
				JoystickKeys = CopperScreenJoystickKeyMap.Create(
					SplitKeyList(_joystickKeyBoxes[0].Text),
					SplitKeyList(_joystickKeyBoxes[1].Text),
					SplitKeyList(_joystickKeyBoxes[2].Text),
					SplitKeyList(_joystickKeyBoxes[3].Text),
					SplitKeyList(_joystickKeyBoxes[4].Text),
					SplitKeyList(_joystickKeyBoxes[5].Text))
			};
			var profiles = _settingsDraft.Input.ControllerProfiles
				.Select(existing => string.Equals(existing.Id, updated.Id, StringComparison.OrdinalIgnoreCase) ? updated : existing)
				.ToArray();
			_settingsDraft.Input = _settingsDraft.Input.WithControllerProfiles(profiles);
			_inputOptions = _settingsDraft.Input;
			_runtime?.SetInputOptions(_inputOptions);
			RefreshControllerSelectors();
			_updatingSettingsUi = true;
			try
			{
				_controllerProfileChooser.SelectedItem = _settingsDraft.Input.ControllerProfiles.FirstOrDefault(item =>
					string.Equals(item.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
			}
			finally
			{
				_updatingSettingsUi = false;
			}

			RefreshSelectedControllerProfileEditor();
			UpdateSettingsStatus();
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
		{
			SetSettingsError(ex.Message);
		}
	}

	private void ApplyInputSettingsLive()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		try
		{
			ClearSettingsStartupError();
			_settingsDraft.Input = ReadInputOptionsFromUi();
			_inputOptions = _settingsDraft.Input;
			_runtime?.SetInputOptions(_inputOptions);
			UpdateSettingsStatus();
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
		{
			SetSettingsError(ex.Message);
		}
	}

	private void ApplyPresentationSettingsLive()
	{
		if (_updatingSettingsUi)
		{
			return;
		}

		try
		{
			ClearSettingsStartupError();
			_settingsDraft.PresentationOptions = ReadPresentationOptionsFromUi();
			_runtime?.SetPresentationOptions(_settingsDraft.PresentationOptions);
			UpdateSettingsStatus();
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
		{
			SetSettingsError(ex.Message);
		}
	}

	private static CopperScreenControllerKind ParseControllerKindSelection(object? value)
		=> Enum.TryParse<CopperScreenControllerKind>(value?.ToString(), ignoreCase: true, out var kind)
			? kind
			: CopperScreenControllerKind.None;

	private async Task PickSettingsDiskAsync(int driveIndex)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
		{
			return;
		}

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = $"Select DF{driveIndex} disk image",
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

		ClearSettingsStartupError();
		_settingsDraft.DriveDiskPaths[driveIndex] = path;
		SetDrivePathTextSilently(driveIndex, path);
		if (_runtime != null && driveIndex < _latestState.Drives.Length && _latestState.Drives[driveIndex].Connected)
		{
			await InsertDriveDiskAsync(driveIndex, path).ConfigureAwait(true);
		}
		else
		{
			_latestState = CreateIdleState(_settingsDraft);
			UpdateToolbarStatus();
		}
		UpdateSettingsStatus();
	}

	private void SetSettingsError(string error)
	{
		_settingsStatus.Text = error;
		_settingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 194, 194));
	}

	private static CopperScreenKickstartSource ParseKickstartSourceSelection(object? value)
	{
		return value?.ToString() switch
		{
			string source when string.Equals(source, "KickstartRom", StringComparison.OrdinalIgnoreCase) => CopperScreenKickstartSource.KickstartRom,
			string source when string.Equals(source, "Kickstart13Rom", StringComparison.OrdinalIgnoreCase) => CopperScreenKickstartSource.Kickstart13Rom,
			string source when string.Equals(source, "DiagRom", StringComparison.OrdinalIgnoreCase) => CopperScreenKickstartSource.DiagRom,
			_ => CopperScreenKickstartSource.CopperStart
		};
	}

	private static M68kBackendKind ParseCpuSelection(object? value)
		=> value?.ToString() switch
		{
			string backend when string.Equals(backend, "JitM68000", StringComparison.OrdinalIgnoreCase) => M68kBackendKind.JitM68000,
			string backend when string.Equals(backend, "JitM68040", StringComparison.OrdinalIgnoreCase) => M68kBackendKind.JitM68040,
			string backend when string.Equals(backend, "AccurateM68020", StringComparison.OrdinalIgnoreCase) => M68kBackendKind.AccurateM68020,
			string backend when string.Equals(backend, "AccurateM68030", StringComparison.OrdinalIgnoreCase) => M68kBackendKind.AccurateM68030,
			string backend when string.Equals(backend, "AccurateM68040", StringComparison.OrdinalIgnoreCase) => M68kBackendKind.AccurateM68040,
			_ => M68kBackendKind.AccurateM68000
		};

	private static FloppyDriveAudioMode ParseFloppyAudioModeSelection(object? value)
		=> string.Equals(value?.ToString(), "Samples", StringComparison.OrdinalIgnoreCase)
			? FloppyDriveAudioMode.Samples
			: FloppyDriveAudioMode.Synthetic;

	private static CopperScreenLacedPresentationMode ParseLacedPresentationModeSelection(object? value)
		=> string.Equals(value?.ToString(), "CRT flicker", StringComparison.OrdinalIgnoreCase)
			? CopperScreenLacedPresentationMode.CrtFlicker
			: CopperScreenLacedPresentationMode.StableWeave;

	private static string FormatLacedPresentationMode(CopperScreenLacedPresentationMode mode)
		=> mode == CopperScreenLacedPresentationMode.CrtFlicker
			? "CRT flicker"
			: "Stable weave";

	private static int ParseDriveCount(object? value)
		=> int.TryParse(value?.ToString(), out var count) ? Math.Clamp(count, 1, 4) : 1;

	private static int ParsePositiveInt(string? value, string label)
	{
		if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result) ||
			result <= 0)
		{
			throw new InvalidOperationException(label + " must be positive.");
		}

		return result;
	}

	private static int ParseNonNegativeInt(string? value, string label)
	{
		if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result) ||
			result < 0)
		{
			throw new InvalidOperationException(label + " cannot be negative.");
		}

		return result;
	}

	private static float ParseFloat(string? value, string label)
	{
		if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
		{
			throw new InvalidOperationException(label + " must be a number.");
		}

		return result;
	}

	private static IEnumerable<string> SplitKeyList(string? value)
		=> string.IsNullOrWhiteSpace(value)
			? Array.Empty<string>()
			: value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
			if (_runtime == null)
			{
				return;
			}

			await _bench.GoUpAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
			RefreshCopperBenchUi();
		}));
		navigation.Children.Add(CreatePanelButton("Refresh", async () =>
		{
			if (_runtime == null)
			{
				return;
			}

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

	private Border CreateDebuggerPanel()
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

		_debuggerTitle.Text = "Debugger";
		_debuggerTitle.FontSize = 18;
		_debuggerTitle.FontWeight = FontWeight.SemiBold;
		_debuggerTitle.Foreground = Brushes.White;
		_debuggerTitle.Margin = new Thickness(0, 0, 0, 6);
		Grid.SetRow(_debuggerTitle, 0);
		panel.Children.Add(_debuggerTitle);

		_debuggerMessage.Foreground = new SolidColorBrush(Color.FromRgb(242, 208, 161));
		_debuggerMessage.TextWrapping = TextWrapping.Wrap;
		_debuggerMessage.Margin = new Thickness(0, 0, 0, 10);
		Grid.SetRow(_debuggerMessage, 1);
		panel.Children.Add(_debuggerMessage);

		var details = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(new GridLength(DebuggerLeftColumnWidth)),
				new ColumnDefinition(new GridLength(1, GridUnitType.Star))
			},
			RowDefinitions =
			{
				new RowDefinition(new GridLength(1, GridUnitType.Star)),
				new RowDefinition(new GridLength(160))
			}
		};

		_debuggerRegisters.FontFamily = FontFamily.Parse("Consolas");
		_debuggerRegisters.FontSize = 12;
		_debuggerRegisters.Foreground = new SolidColorBrush(Color.FromRgb(224, 231, 241));
		_debuggerRegisters.TextWrapping = TextWrapping.NoWrap;
		var registerPane = CreateDebuggerPane(_debuggerRegisters);
		Grid.SetColumn(registerPane, 0);
		Grid.SetRow(registerPane, 0);
		details.Children.Add(registerPane);

		_debuggerDisassembly.FontFamily = FontFamily.Parse("Consolas");
		_debuggerDisassembly.FontSize = 12;
		_debuggerDisassembly.Foreground = new SolidColorBrush(Color.FromRgb(226, 238, 216));
		_debuggerDisassembly.TextWrapping = TextWrapping.NoWrap;
		var disassemblyPane = CreateDebuggerPane(_debuggerDisassembly);
		disassemblyPane.Margin = new Thickness(10, 0, 0, 0);
		Grid.SetColumn(disassemblyPane, 1);
		Grid.SetRow(disassemblyPane, 0);
		details.Children.Add(disassemblyPane);

		_debuggerDevices.FontFamily = FontFamily.Parse("Consolas");
		_debuggerDevices.FontSize = 12;
		_debuggerDevices.Foreground = new SolidColorBrush(Color.FromRgb(214, 224, 238));
		_debuggerDevices.TextWrapping = TextWrapping.Wrap;
		var devicePane = CreateDebuggerPane(_debuggerDevices);
		devicePane.Margin = new Thickness(0, 10, 0, 0);
		Grid.SetColumn(devicePane, 0);
		Grid.SetRow(devicePane, 1);
		details.Children.Add(devicePane);

		_debuggerStack.FontFamily = FontFamily.Parse("Consolas");
		_debuggerStack.FontSize = 12;
		_debuggerStack.Foreground = new SolidColorBrush(Color.FromRgb(218, 223, 232));
		_debuggerStack.TextWrapping = TextWrapping.NoWrap;
		var stackPane = CreateDebuggerPane(_debuggerStack);
		stackPane.Margin = new Thickness(10, 10, 0, 0);
		Grid.SetColumn(stackPane, 1);
		Grid.SetRow(stackPane, 1);
		details.Children.Add(stackPane);

		Grid.SetRow(details, 2);
		panel.Children.Add(details);

		var commands = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			Margin = new Thickness(0, 10, 0, 0)
		};
		commands.Children.Add(CreatePanelButton("Copy report", CopyDebuggerReport));
		commands.Children.Add(CreatePanelButton("Reset", async () =>
		{
			await ResetRuntimeAsync().ConfigureAwait(true);
		}));
		Grid.SetRow(commands, 3);
		panel.Children.Add(commands);

		return new Border
		{
			MaxWidth = DebuggerPanelWidth,
			Margin = new Thickness(12, 78, 12, 12),
			Padding = new Thickness(12),
			CornerRadius = new CornerRadius(6),
			Background = new SolidColorBrush(Color.FromArgb(242, 18, 20, 25)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(188, 103, 72)),
			BorderThickness = new Thickness(1),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			IsVisible = false,
			Child = panel
		};
	}

	private static Border CreateDebuggerPane(TextBlock content)
	{
		content.Margin = new Thickness(8);
		return new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(230, 8, 10, 14)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(56, 66, 82)),
			BorderThickness = new Thickness(1),
			Child = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				Content = content
			}
		};
	}

	private void CopyDebuggerReport()
	{
		var snapshot = _visibleDebugSnapshot;
		if (snapshot == null)
		{
			return;
		}

		var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard != null)
		{
			_ = clipboard.SetTextAsync(snapshot.ToReport());
		}
	}

	private static Button CreateToolbarButton(string text, Action action, string tooltip)
	{
		var button = new Button
		{
			Content = text,
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromRgb(34, 40, 50)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(78, 90, 108)),
			FontSize = 11,
			Padding = new Thickness(6, 2),
			MinWidth = 0
		};
		ToolTip.SetTip(button, tooltip);
		button.Click += (_, _) => action();
		return button;
	}

	private static TextBlock CreateToolbarTextBlock(double fontSize, TextAlignment textAlignment = TextAlignment.Left)
	{
		return new TextBlock
		{
			Foreground = Brushes.White,
			FontFamily = FontFamily.Parse("Consolas"),
			FontSize = fontSize,
			TextAlignment = textAlignment,
			TextWrapping = TextWrapping.NoWrap,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	private static Border CreateIndicatorBox(TextBlock text, double width, string tooltip)
	{
		var border = new Border
		{
			Width = width,
			Height = 21,
			Background = new SolidColorBrush(Color.FromRgb(24, 29, 36)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(58, 67, 80)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Padding = new Thickness(4, 1),
			Child = text
		};
		ToolTip.SetTip(border, tooltip);
		return border;
	}

	private Button CreateDriveStatusButton(Border indicator, int driveIndex)
	{
		var button = new Button
		{
			Content = indicator,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(0),
			MinWidth = 0,
			Cursor = new Cursor(StandardCursorType.Hand)
		};
		ToolTip.SetTip(button, $"DF{driveIndex}: click to insert or change disk image");
		button.Click += async (_, _) =>
		{
			await OpenDiskPickerAsync(driveIndex).ConfigureAwait(true);
		};
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
		if (_runtime == null)
		{
			_settingsVisible = true;
			RefreshCopperBenchUi();
			return;
		}

		await _bench.ToggleOverlayAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
		RefreshCopperBenchUi();
	}

	private async void TogglePause()
	{
		if (_runtime == null)
		{
			return;
		}

		await ToggleRuntimePauseAsync().ConfigureAwait(true);
	}

	private void ToggleFullscreen()
	{
		ReleaseMouseGrab();
		WindowState = WindowState == WindowState.FullScreen
			? WindowState.Normal
			: WindowState.FullScreen;
		RefreshCopperBenchUi();
	}

	private void ApplyWindowPresentationMode()
	{
		var fullscreen = WindowState == WindowState.FullScreen;
		SizeToContent = fullscreen ? SizeToContent.Manual : SizeToContent.WidthAndHeight;
		_toolbar.IsVisible = !fullscreen || _bench.IsToolbarVisible;
		_settingsPanel.IsVisible = _settingsVisible;
		_root.RowDefinitions[1].Height = fullscreen
			? new GridLength(1, GridUnitType.Star)
			: GridLength.Auto;
		_presenter.DevicePixelExactLayout = !fullscreen;
		_presenter.HorizontalAlignment = fullscreen ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
		_presenter.VerticalAlignment = fullscreen ? VerticalAlignment.Stretch : VerticalAlignment.Center;
		_presenter.InvalidateMeasure();

		Grid.SetRow(_toolbar, 0);
		Grid.SetRowSpan(_toolbar, 1);
		Grid.SetRow(_settingsPanel, 0);
		Grid.SetRowSpan(_settingsPanel, 2);

		if (fullscreen)
		{
			Grid.SetRow(_presenter, 0);
			Grid.SetRowSpan(_presenter, 2);
			Grid.SetRow(_benchPanel, 0);
			Grid.SetRowSpan(_benchPanel, 2);
			_benchPanel.Margin = new Thickness(12, 78, 0, 12);
			Grid.SetRow(_debuggerPanel, 0);
			Grid.SetRowSpan(_debuggerPanel, 2);
			_debuggerPanel.Margin = new Thickness(12, 78, 12, 12);
			return;
		}

		Grid.SetRow(_presenter, 1);
		Grid.SetRowSpan(_presenter, 1);
		Grid.SetRow(_benchPanel, 1);
		Grid.SetRowSpan(_benchPanel, 1);
		_benchPanel.Margin = new Thickness(12, 12, 0, 12);
		Grid.SetRow(_debuggerPanel, 1);
		Grid.SetRowSpan(_debuggerPanel, 1);
		_debuggerPanel.Margin = new Thickness(12, 12, 12, 12);
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
			var viewport = FullOverscanPresentationViewport;
			_presenter.SetSourceViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
		}
		else
		{
			var viewport = CroppedPresentationViewport;
			_presenter.SetSourceViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
		}

		ReleaseMouseGrab();
	}

	internal static PixelRect FullOverscanPresentationViewport { get; } =
		new(0, 0, AmigaConstants.PalHighResWidth, AmigaConstants.PalHighResHeight);

	internal static PixelRect CroppedPresentationViewport { get; } =
		new(
			AmigaConstants.PalLowResOverscanBorderX * 2,
			AmigaConstants.PalLowResOverscanBorderY * 2,
			AmigaConstants.PalLowResStandardWidth * 2,
			AmigaConstants.PalLowResStandardHeight * 2);

	private bool BeginMouseGrab(PointerEventArgs args)
	{
		if (_mouseGrabActive)
		{
			return true;
		}

		if (!TryGetPresenterCenterFramebufferPoint(out var center))
		{
			return false;
		}

		_mouseGrabActive = true;
		_mouseGrabWaitingForCenter = true;
		_mouseGrabPointer = args.Pointer;
		_mouseGrabCenterFramebufferPoint = center;
		ResetMouseTracking();
		args.Pointer.Capture(_presenter);
		if (!RecenterMouseGrabPointer())
		{
			ReleaseMouseGrab();
			return false;
		}

		return true;
	}

	private void ReleaseMouseGrab()
	{
		if (_mouseGrabPointer != null)
		{
			_mouseGrabPointer.Capture(null!);
			_mouseGrabPointer = null;
		}

		_mouseGrabActive = false;
		_mouseGrabWaitingForCenter = false;
		_mouseGrabRecenterPending = false;
		_mouseGrabCenterFramebufferPoint = null;
		_mouseGrabCenterScreenPoint = null;
		ResetMouseTracking();
	}

	private static bool IsNearFramebufferPoint(Point left, Point right)
	{
		return Math.Abs(left.X - right.X) < 0.75 &&
			Math.Abs(left.Y - right.Y) < 0.75;
	}

	internal static bool IsMouseGrabCenterPoint(Point framebufferPoint, Point centerFramebufferPoint)
		=> IsNearFramebufferPoint(framebufferPoint, centerFramebufferPoint);

	internal static bool IsNearScreenPoint(PixelPoint left, PixelPoint right)
		=> Math.Abs(left.X - right.X) <= 1 &&
			Math.Abs(left.Y - right.Y) <= 1;

	internal static bool IsMouseGrabRecenterEcho(
		Point framebufferPoint,
		Point centerFramebufferPoint,
		PixelPoint? screenPoint,
		PixelPoint? centerScreenPoint)
	{
		if (!IsMouseGrabCenterPoint(framebufferPoint, centerFramebufferPoint))
		{
			return false;
		}

		return !centerScreenPoint.HasValue ||
			(screenPoint.HasValue &&
			screenPoint.Value.X == centerScreenPoint.Value.X &&
			screenPoint.Value.Y == centerScreenPoint.Value.Y);
	}

	private bool TryGetPresenterCenterFramebufferPoint(out Point framebufferPoint)
	{
		if (!_presenter.TryGetRenderedImageCenter(out var center))
		{
			framebufferPoint = default;
			return false;
		}

		return _presenter.TryMapPointToFramebufferUnclamped(center, out framebufferPoint);
	}

	private bool TryGetGrabbedMouseFramebufferPoint(PointerEventArgs args, out Point framebufferPoint, out PixelPoint? screenPoint)
	{
		if (OperatingSystem.IsWindows() && TryGetCursorScreenPoint(out var currentScreenPoint))
		{
			screenPoint = currentScreenPoint;
			var presenterPoint = _presenter.PointToClient(currentScreenPoint);
			return _presenter.TryMapPointToFramebufferUnclamped(presenterPoint, out framebufferPoint);
		}

		screenPoint = null;
		return _presenter.TryMapPointToFramebufferUnclamped(args.GetPosition(_presenter), out framebufferPoint);
	}

	private bool TryRecenterMousePointer()
	{
		if (!OperatingSystem.IsWindows() || _presenter.Bounds.Width <= 0 || _presenter.Bounds.Height <= 0)
		{
			return false;
		}

		try
		{
			if (!_presenter.TryGetRenderedImageCenter(out var center))
			{
				return false;
			}

			var screenPoint = _presenter.PointToScreen(center);
			if (!SetCursorPos(screenPoint.X, screenPoint.Y))
			{
				return false;
			}

			_mouseGrabCenterScreenPoint = screenPoint;
			if (_presenter.TryMapPointToFramebufferUnclamped(_presenter.PointToClient(screenPoint), out var framebufferPoint))
			{
				_mouseGrabCenterFramebufferPoint = framebufferPoint;
			}

			return true;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	private bool RecenterMouseGrabPointer()
	{
		if (!TryRecenterMousePointer())
		{
			return false;
		}

		_mouseGrabRecenterPending = true;
		return true;
	}

	private void ResetMouseTracking()
	{
		_lastMouseX = null;
		_lastMouseY = null;
		_mouseDeltaRemainderX = 0;
		_mouseDeltaRemainderY = 0;
	}

	[DllImport("user32.dll")]
	private static extern bool SetCursorPos(int x, int y);

	private static bool TryGetCursorScreenPoint(out PixelPoint point)
	{
		if (GetCursorPos(out var nativePoint))
		{
			point = new PixelPoint(nativePoint.X, nativePoint.Y);
			return true;
		}

		point = default;
		return false;
	}

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out NativePoint point);

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		public int X;
		public int Y;
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

	private async Task OpenDiskPickerAsync(int driveIndex)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
		{
			return;
		}

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = $"Insert Amiga disk image in DF{driveIndex}",
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

		if ((uint)driveIndex < (uint)_settingsDraft.DriveDiskPaths.Length)
		{
			_settingsDraft.DriveDiskPaths[driveIndex] = path;
			if (_drivePathBoxes[driveIndex] != null)
			{
				SetDrivePathTextSilently(driveIndex, path);
			}
		}

		await InsertDriveDiskAsync(driveIndex, path).ConfigureAwait(true);
	}

	private async Task ShowCopperBenchAsync()
	{
		if (_runtime == null)
		{
			return;
		}

		await _bench.ShowOverlayAsync(_runtime.CurrentState.DiskPath).ConfigureAwait(true);
		RefreshCopperBenchUi();
	}

	private async Task ToggleRuntimePauseAsync()
	{
		if (_runtime == null)
		{
			return;
		}

		var result = await _runtime.TogglePausedAsync().ConfigureAwait(true);
		_latestState = result.State;
		_bench.SetStatusMessage(result.Message);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private async Task ResetRuntimeAsync()
	{
		if (_runtime == null)
		{
			return;
		}

		var result = await _runtime.ResetAsync().ConfigureAwait(true);
		_latestState = result.State;
		_bench.ResetPath();
		if (_bench.IsOverlayVisible)
		{
			await _bench.RefreshAsync(result.State.DiskPath).ConfigureAwait(true);
		}

		_bench.SetStatusMessage(result.Message);
		RefreshDebuggerUi(result.State.DebugSnapshot);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private Task InsertDiskAsync(string path)
		=> _runtime == null ? Task.CompletedTask : CompleteDiskCommandAsync(_runtime.InsertDiskAsync(path));

	private Task InsertDriveDiskAsync(int driveIndex, string path)
		=> _runtime == null ? Task.CompletedTask : CompleteDiskCommandAsync(_runtime.InsertDriveDiskAsync(driveIndex, path));

	private Task InsertNextDiskAsync()
		=> _runtime == null ? Task.CompletedTask : CompleteDiskCommandAsync(_runtime.InsertNextDiskAsync());

	private Task InsertPreviousDiskAsync()
		=> _runtime == null ? Task.CompletedTask : CompleteDiskCommandAsync(_runtime.InsertPreviousDiskAsync());

	private async Task ToggleWriteProtectAsync()
	{
		if (_runtime == null)
		{
			return;
		}

		var drive = _latestState.Drives.Length > 0
			? _latestState.Drives[0]
			: new CopperScreenDriveState(0, false, false, "No disk", null, 0, 0, false, false, false, false);
		if (!drive.Connected || !drive.HasDisk)
		{
			return;
		}

		var result = await _runtime.SetDriveWriteProtectedAsync(0, !drive.WriteProtected).ConfigureAwait(true);
		_latestState = result.State;
		_bench.SetStatusMessage(result.Message);
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private async Task CompleteDiskCommandAsync(Task<CopperScreenCommandResult> command)
	{
		if (_runtime == null)
		{
			return;
		}

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
		if (_runtime == null)
		{
			return;
		}

		await _bench.ActivateSelectedAsync(
			_runtime.CurrentState.DiskPath,
			path => _runtime.LaunchCopperBenchPathAsync(path)).ConfigureAwait(true);
		_latestState = _runtime.CurrentState;
		RefreshCopperBenchUi();
		PresentFrame(catchUpAudio: false);
	}

	private void RefreshCopperBenchUi()
	{
		ApplyWindowPresentationMode();
		_benchPanel.IsVisible = _bench.IsOverlayVisible && _visibleDebugSnapshot == null;
		_benchToggleButton.Content = _bench.IsOverlayVisible ? "Hide" : "Bench";
		_pauseButton.Content = _latestState.IsPaused ? "Run" : "Pause";
		_numpadModeButton.Content = _numpadMode == NumpadInputMode.Joystick ? "N:Joy" : "N:Key";
		_fullscreenButton.Content = WindowState == WindowState.FullScreen ? "Win" : "Full";
		_overscanButton.Content = _showFullOverscan ? "Crop" : "Scan";
		_benchPath.Text = _bench.DisplayPath;
		RefreshEntryList();
		RefreshCopperBenchDetails();
		UpdateToolbarStatus();
	}

	private void RefreshDebuggerUi(CopperScreenDebugSnapshot? snapshot)
	{
		ApplyWindowPresentationMode();
		if (ReferenceEquals(_visibleDebugSnapshot, snapshot))
		{
			_debuggerPanel.IsVisible = snapshot != null;
			_benchPanel.IsVisible = _bench.IsOverlayVisible && snapshot == null;
			return;
		}

		_visibleDebugSnapshot = snapshot;
		_debuggerPanel.IsVisible = snapshot != null;
		_benchPanel.IsVisible = _bench.IsOverlayVisible && snapshot == null;
		if (snapshot == null)
		{
			_debuggerTitle.Text = "Debugger";
			_debuggerMessage.Text = string.Empty;
			_debuggerRegisters.Text = string.Empty;
			_debuggerDisassembly.Text = string.Empty;
			_debuggerStack.Text = string.Empty;
			_debuggerDevices.Text = string.Empty;
			return;
		}

		_debuggerTitle.Text = "Debugger - " + snapshot.ReasonCode;
		_debuggerMessage.Text = snapshot.Message;
		_debuggerRegisters.Text = snapshot.Cpu.FormatRegisters();
		_debuggerDisassembly.Text = string.Join(Environment.NewLine, snapshot.Disassembly);
		_debuggerStack.Text = string.Join(Environment.NewLine, snapshot.StackWords);
		_debuggerDevices.Text = FormatDebuggerDevices(snapshot);
	}

	private static string FormatDebuggerDevices(CopperScreenDebugSnapshot snapshot)
	{
		var builder = new StringBuilder();
		builder.AppendLine($"Profile: {snapshot.ProfileName}");
		builder.AppendLine($"CPU: {snapshot.CpuBackendName}");
		builder.AppendLine($"Frame: {snapshot.FrameNumber}");
		builder.AppendLine($"Disk: {snapshot.DiskName}");
		if (!string.IsNullOrWhiteSpace(snapshot.DiskPath))
		{
			builder.AppendLine(snapshot.DiskPath);
		}

		builder.AppendLine();
		for (var i = 0; i < snapshot.Drives.Length; i++)
		{
			builder.AppendLine(CopperScreenDebugSnapshot.FormatDrive(snapshot.Drives[i]));
		}

		if (snapshot.Diagnostics.Length > 0)
		{
			builder.AppendLine();
			for (var i = 0; i < snapshot.Diagnostics.Length; i++)
			{
				builder.AppendLine(snapshot.Diagnostics[i]);
			}
		}

		return builder.ToString();
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
		_pauseButton.Content = state.IsPaused ? "Run" : "Pause";
		_numpadModeButton.Content = _numpadMode == NumpadInputMode.Joystick ? "N:Joy" : "N:Key";
		_fullscreenButton.Content = WindowState == WindowState.FullScreen ? "Win" : "Full";
		_overscanButton.Content = _showFullOverscan ? "Crop" : "Scan";
		SetText(_diskStatus, state.DiskName);
		SetText(_ledFilterStatus, state.AudioFilterEnabled ? "LED/F ON" : "LED/F OFF");
		StyleIndicator(
			_ledFilterBox,
			state.AudioFilterEnabled ? Color.FromRgb(28, 72, 45) : Color.FromRgb(34, 36, 40),
			state.AudioFilterEnabled ? Color.FromRgb(91, 160, 103) : Color.FromRgb(66, 70, 78),
			state.AudioFilterEnabled ? Color.FromRgb(210, 255, 218) : Color.FromRgb(148, 154, 164));
		SetText(_cpuPcStatus, $"PC {state.Cpu.ProgramCounter & 0x00FF_FFFF:X6}");
		SetText(_lastPcStatus, $"LP {state.Cpu.LastInstructionProgramCounter & 0x00FF_FFFF:X6}");
		SetText(_frameStatus, $"F {state.FrameNumber}");
		SetText(_perfStatus, $"Q{state.QueuedAudioBuffers} D{state.DroppedFrames}");
		SetPerformanceToolTip(state);
		var primaryDrive = state.Drives.Length > 0
			? state.Drives[0]
			: new CopperScreenDriveState(0, false, false, "No disk", null, 0, 0, false, false, false, false);
		_writeProtectButton.IsEnabled = primaryDrive.Connected && primaryDrive.HasDisk;
		_writeProtectButton.Content = primaryDrive.WriteProtected ? "WP ON" : "WP OFF";
		_writeProtectButton.Background = new SolidColorBrush(primaryDrive.WriteProtected
			? Color.FromRgb(56, 42, 24)
			: Color.FromRgb(29, 58, 40));
		_writeProtectButton.BorderBrush = new SolidColorBrush(primaryDrive.WriteProtected
			? Color.FromRgb(148, 101, 48)
			: Color.FromRgb(76, 142, 92));
		_writeProtectButton.Foreground = new SolidColorBrush(primaryDrive.WriteProtected
			? Color.FromRgb(255, 226, 170)
			: Color.FromRgb(206, 248, 213));
		SetWriteProtectToolTip(primaryDrive.HasDisk
			? $"DF0: write protection {(primaryDrive.WriteProtected ? "enabled" : "disabled")}"
			: "DF0: insert a disk before changing write protection");

		for (var driveIndex = 0; driveIndex < _driveStatusTexts.Length; driveIndex++)
		{
			var drive = driveIndex < state.Drives.Length
				? state.Drives[driveIndex]
				: new CopperScreenDriveState(driveIndex, false, false, "No disk", null, 0, 0, false, false, false, false);
			UpdateDriveStatus(drive, state.IsDiskSwapPending && driveIndex == 0);
		}

	}

	private void SetPerformanceToolTip(CopperScreenState state)
	{
		var text =
			$"Queued audio buffers: {state.QueuedAudioBuffers}\n" +
			$"Dropped frames: {state.DroppedFrames}\n" +
			$"Skipped by presenter: {state.PresentationSkippedFrames}\n" +
			$"Dropped, no free buffer: {state.PresentationBufferDroppedFrames}\n" +
			$"Video queue: {state.PresentationQueueDepth}/{state.PresentationQueueCapacity}\n" +
			$"Video queue throttles: {state.PresentationQueueFullThrottleCount}\n" +
			$"Emulator frame: {state.LastEmulationFrameMilliseconds:F2} ms\n" +
			$"Runtime publish: {state.LastPublishFrameMilliseconds:F2} ms\n" +
			$"Presentation callback: {_lastPresentationFrameMilliseconds:F2} ms\n" +
			$"Framebuffer upload: {_lastPresenterUpdateMilliseconds:F2} ms\n" +
			$"Framebuffer render: {_presenter.LastRenderMilliseconds:F2} ms";
		ToolTip.SetTip(_perfStatusBox, text);
		ToolTip.SetTip(_perfStatus, text);
	}

	private void UpdateDriveStatus(CopperScreenDriveState drive, bool swapping)
	{
		var text = _driveStatusTexts[drive.Index];
		var box = _driveStatusBoxes[drive.Index];
		var button = _driveStatusButtons[drive.Index];
		button.IsEnabled = drive.Connected;
		button.Cursor = drive.Connected
			? new Cursor(StandardCursorType.Hand)
			: new Cursor(StandardCursorType.Arrow);
		var tooltip = BuildDriveTooltip(drive, swapping);
		ToolTip.SetTip(button, tooltip);
		ToolTip.SetTip(box, tooltip);

		if (swapping)
		{
			SetText(text, $"DF{drive.Index} swap");
			StyleIndicator(box, Color.FromRgb(74, 58, 26), Color.FromRgb(152, 118, 52), Color.FromRgb(255, 226, 162));
			return;
		}

		if (!drive.Connected)
		{
			SetText(text, $"DF{drive.Index} --.- NC");
			StyleIndicator(box, Color.FromRgb(24, 25, 27), Color.FromRgb(48, 50, 54), Color.FromRgb(106, 112, 120));
			return;
		}

		if (!drive.HasDisk)
		{
			SetText(text, $"DF{drive.Index} --.- --");
			StyleIndicator(box, Color.FromRgb(29, 33, 38), Color.FromRgb(58, 64, 72), Color.FromRgb(145, 152, 162));
			return;
		}

		var flags = string.Concat(drive.ActiveDma ? 'D' : drive.MotorOn ? 'M' : '-', drive.Selected ? 'S' : '-');
		SetText(text, $"DF{drive.Index} {drive.Cylinder:00}.{drive.Head} {flags}{(drive.WriteProtected ? "P" : "W")}");
		if (drive.ActiveDma)
		{
			StyleIndicator(box, Color.FromRgb(76, 35, 28), Color.FromRgb(182, 93, 66), Color.FromRgb(255, 220, 202));
		}
		else if (drive.MotorOn)
		{
			StyleIndicator(box, Color.FromRgb(70, 58, 25), Color.FromRgb(160, 128, 48), Color.FromRgb(255, 234, 170));
		}
		else if (drive.Selected)
		{
			StyleIndicator(box, Color.FromRgb(28, 57, 75), Color.FromRgb(72, 139, 178), Color.FromRgb(207, 239, 255));
		}
		else
		{
			StyleIndicator(box, Color.FromRgb(28, 39, 35), Color.FromRgb(62, 88, 77), Color.FromRgb(184, 218, 202));
		}
	}

	private static string BuildDriveTooltip(CopperScreenDriveState drive, bool swapping)
	{
		if (!drive.Connected)
		{
			return $"DF{drive.Index}: drive not connected";
		}

		if (swapping)
		{
			var pendingDisk = string.IsNullOrWhiteSpace(drive.DiskPath) ? drive.DiskName : drive.DiskPath;
			return $"DF{drive.Index}: changing disk\n{pendingDisk}";
		}

		if (!drive.HasDisk)
		{
			return $"DF{drive.Index}: empty\nClick to insert a disk image";
		}

		var insertedDisk = string.IsNullOrWhiteSpace(drive.DiskPath) ? drive.DiskName : drive.DiskPath;
		return $"DF{drive.Index}: {drive.DiskName}\n{insertedDisk}\nWrite protect: {(drive.WriteProtected ? "on" : "off")}\nClick to change disk image";
	}

	private void SetWriteProtectToolTip(string text)
	{
		ToolTip.SetTip(_writeProtectButton, new TextBlock
		{
			Text = text,
			Foreground = Brushes.White
		});
	}

	private static void SetText(TextBlock text, string value)
	{
		if (!string.Equals(text.Text, value, StringComparison.Ordinal))
		{
			text.Text = value;
		}
	}

	private static void StyleIndicator(Border box, Color background, Color border, Color foreground)
	{
		box.Background = new SolidColorBrush(background);
		box.BorderBrush = new SolidColorBrush(border);
		if (box.Child is TextBlock text)
		{
			text.Foreground = new SolidColorBrush(foreground);
		}
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
