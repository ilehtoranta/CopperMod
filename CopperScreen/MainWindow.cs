using Avalonia.Controls;
using Avalonia.Input;
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
	private readonly FramebufferPresenter _presenter;
	private readonly DispatcherTimer _timer;
	private readonly WaveOutAudioOutput? _audio;
	private readonly float[] _audioBuffer;
	private readonly HashSet<Key> _pressedKeys = new HashSet<Key>();
	private double? _lastMouseX;
	private double? _lastMouseY;

	public MainWindow(string[] args)
	{
		Title = "CopperScreen";
		Width = 960;
		Height = 768;
		Focusable = true;
		_emulator = CopperScreenEmulator.Create(args, AppContext.BaseDirectory);
		_audioBuffer = new float[_emulator.AudioFramesPerAppFrame(AudioSampleRate) * AudioChannels];
		_audio = WaveOutAudioOutput.TryCreate(AudioSampleRate, AudioChannels, _audioBuffer.Length / AudioChannels, AudioOutputBufferCount);
		_presenter = new FramebufferPresenter(_emulator.Width, _emulator.Height)
		{
			Focusable = true,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
		};
		Content = _presenter;
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
		var framesToRender = CalculateFramesToRender(_audio?.QueuedBufferCount, catchUpAudio);
		for (var frame = 0; frame < framesToRender; frame++)
		{
			_emulator.RenderNextFrame();
			var audioFrames = _emulator.RenderAudio(_audioBuffer, AudioSampleRate, AudioChannels);
			_audio?.Submit(_audioBuffer.AsSpan(0, audioFrames * AudioChannels));
		}

		_presenter.Update(_emulator.Framebuffer);
		Title = "CopperScreen - " + _emulator.StatusText + " - mouse port 1, numpad port 2, F12 next disk";
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
		if (args.Key == Key.F12)
		{
			_emulator.InsertNextDisk();
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
			_pressedKeys.Add(args.Key);
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

		_pressedKeys.Remove(args.Key);
		UpdateJoystickPort();
		args.Handled = true;
		PresentFrame(catchUpAudio: false);
	}

	private void UpdateJoystickPort()
	{
		var up = IsPressed(Key.NumPad8) || IsPressed(Key.NumPad7) || IsPressed(Key.NumPad9);
		var down = IsPressed(Key.NumPad2) || IsPressed(Key.NumPad1) || IsPressed(Key.NumPad3);
		var left = IsPressed(Key.NumPad4) || IsPressed(Key.NumPad7) || IsPressed(Key.NumPad1);
		var right = IsPressed(Key.NumPad6) || IsPressed(Key.NumPad9) || IsPressed(Key.NumPad3);
		var primaryFire = IsPressed(Key.NumPad5);
		var secondFire = IsPressed(Key.Decimal) || IsPressed(Key.Delete);
		_emulator.SetJoystickPort(up, down, left, right, primaryFire, secondFire);
	}

	private bool IsPressed(Key key)
	{
		return _pressedKeys.Contains(key);
	}

	private static bool IsJoystickKey(Key key)
	{
		return key is Key.NumPad1 or Key.NumPad2 or Key.NumPad3 or Key.NumPad4 or Key.NumPad5 or
			Key.NumPad6 or Key.NumPad7 or Key.NumPad8 or Key.NumPad9 or Key.Decimal or Key.Delete;
	}

	private void ReleaseInteractiveInput()
	{
		_pressedKeys.Clear();
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
