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
		_presenter.PointerPressed += (_, args) =>
		{
			_emulator.PulsePrimaryFire();
			args.Handled = true;
			PresentFrame(catchUpAudio: false);
		};
		KeyDown += (_, args) =>
		{
			if (args.Key == Key.F12)
			{
				_emulator.InsertNextDisk();
				args.Handled = true;
				PresentFrame(catchUpAudio: false);
			}
			else if (args.Key is Key.Space or Key.Enter)
			{
				_emulator.PulsePrimaryFire();
				args.Handled = true;
				PresentFrame(catchUpAudio: false);
			}
		};
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
		Title = "CopperScreen - " + _emulator.StatusText + " - Space/Enter/click fire, F12 next disk";
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
