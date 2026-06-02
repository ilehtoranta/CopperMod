using System.Collections.Concurrent;
using System.Diagnostics;
using CopperMod.Amiga;

namespace CopperScreen;

internal interface ICopperScreenAudioOutput : IDisposable
{
	int QueuedBufferCount { get; }

	bool Submit(ReadOnlySpan<float> samples);
}

internal readonly record struct CopperScreenCommandResult(bool Success, string Message, CopperScreenState State);

internal readonly record struct CopperScreenCpuState(
	uint ProgramCounter,
	uint LastInstructionProgramCounter,
	ushort StatusRegister);

internal readonly record struct CopperScreenDriveState(
	int Index,
	bool Connected,
	bool HasDisk,
	string DiskName,
	string? DiskPath,
	int Cylinder,
	int Head,
	bool MotorOn,
	bool Selected,
	bool ActiveDma);

internal readonly record struct CopperScreenState(
	string ProfileName,
	string DiskName,
	string? DiskPath,
	CopperScreenCpuState Cpu,
	CopperScreenDriveState[] Drives,
	string StatusText,
	bool IsPaused,
	bool IsWorkbenchHandoffPending,
	bool IsDiskSwapPending,
	bool IsPrimaryFirePressed,
	bool AudioFilterEnabled,
	bool CopperBenchRequestPending,
	int FramesRendered,
	int QueuedAudioBuffers,
	long FrameNumber,
	long DroppedFrames,
	long AudioSubmitFailures,
	double LastEmulationFrameMilliseconds);

internal sealed class CopperScreenRuntime : IDisposable
{
	private const int AudioSampleRate = 44_100;
	private const int AudioChannels = 2;
	private const int AudioOutputBufferCount = 8;
	private const int TargetQueuedAudioBuffers = 5;
	private const int MaxFramesPerTick = 5;
	private const int DisplayFrameMilliseconds = 20;
	private readonly CopperScreenEmulator _emulator;
	private readonly ICopperScreenAudioOutput? _audio;
	private readonly bool _disposeAudio;
	private readonly float[] _audioBuffer;
	private readonly CopperScreenDriveState[] _driveStates = new CopperScreenDriveState[4];
	private readonly ConcurrentQueue<CopperScreenCommand> _commands = new ConcurrentQueue<CopperScreenCommand>();
	private readonly AutoResetEvent _wake = new AutoResetEvent(false);
	private readonly object _presentationSync = new object();
	private readonly int[][] _frameBuffers;
	private Thread? _thread;
	private volatile bool _running;
	private int _writeBufferIndex;
	private int _latestBufferIndex;
	private long _latestFrameNumber;
	private long _droppedFrames;
	private long _audioSubmitFailures;
	private CopperScreenState _latestState;
	private bool _pendingCopperBenchRequest;
	private bool _pausedStatePublished;
	private double _lastEmulationFrameMilliseconds;
	private bool _disposed;

	private CopperScreenRuntime(CopperScreenEmulator emulator, ICopperScreenAudioOutput? audio, bool disposeAudio)
	{
		_emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
		_audio = audio;
		_disposeAudio = disposeAudio;
		_audioBuffer = new float[_emulator.AudioFramesPerAppFrame(AudioSampleRate) * AudioChannels];
		_frameBuffers =
		[
			new int[_emulator.Framebuffer.Length],
			new int[_emulator.Framebuffer.Length],
			new int[_emulator.Framebuffer.Length]
		];
		_latestBufferIndex = 0;
		PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0);
	}

	public int Width => _emulator.Width;

	public int Height => _emulator.Height;

	public event Action? FramePublished;

	public CopperScreenState CurrentState
	{
		get
		{
			lock (_presentationSync)
			{
				return _latestState;
			}
		}
	}

	public static CopperScreenRuntime Create(string[] args, string baseDirectory)
	{
		var emulator = CopperScreenEmulator.Create(args, baseDirectory);
		var audio = WaveOutAudioOutput.TryCreate(
			AudioSampleRate,
			AudioChannels,
			emulator.AudioFramesPerAppFrame(AudioSampleRate),
			AudioOutputBufferCount);
		return new CopperScreenRuntime(emulator, audio, disposeAudio: true);
	}

	internal static CopperScreenRuntime CreateForTests(CopperScreenEmulator emulator, ICopperScreenAudioOutput? audio = null)
		=> new CopperScreenRuntime(emulator, audio, disposeAudio: false);

	public static int CalculateFramesToRender(int? queuedAudioBuffers, bool catchUpAudio)
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

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_running)
		{
			return;
		}

		_running = true;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "CopperScreen runtime"
		};
		_thread.Start();
	}

	public void Stop()
	{
		_running = false;
		_wake.Set();
		var thread = _thread;
		if (thread != null && thread.IsAlive)
		{
			thread.Join(TimeSpan.FromSeconds(2));
		}
	}

	public bool TryCopyLatestFrame(Span<int> destination, ref long lastSeenFrameNumber, out CopperScreenState state, bool force = false)
	{
		lock (_presentationSync)
		{
			var frameNumber = _latestFrameNumber;
			if (!force && frameNumber == lastSeenFrameNumber)
			{
				state = _latestState;
				return false;
			}

			if (destination.Length < _emulator.Framebuffer.Length)
			{
				throw new ArgumentException("Destination framebuffer is too small.", nameof(destination));
			}

			if (lastSeenFrameNumber != 0 && frameNumber > lastSeenFrameNumber + 1)
			{
				_droppedFrames += frameNumber - lastSeenFrameNumber - 1;
			}

			_frameBuffers[_latestBufferIndex].AsSpan().CopyTo(destination);
			state = _latestState with { DroppedFrames = _droppedFrames };
			lastSeenFrameNumber = frameNumber;
			return true;
		}
	}

	public void KeyDown(AmigaRawKey key)
		=> Post(emulator => emulator.KeyDown(key));

	public void KeyUp(AmigaRawKey key)
		=> Post(emulator => emulator.KeyUp(key));

	public void MoveMousePort(int deltaX, int deltaY)
		=> Post(emulator => emulator.MoveMousePort(deltaX, deltaY));

	public void SetMouseButtons(bool primaryFirePressed, bool secondFirePressed)
		=> Post(emulator => emulator.SetMouseButtons(primaryFirePressed, secondFirePressed));

	public void SetJoystickPort(bool up, bool down, bool left, bool right, bool primaryFirePressed, bool secondFirePressed)
		=> Post(emulator => emulator.SetJoystickPort(up, down, left, right, primaryFirePressed, secondFirePressed));

	public Task<CopperScreenCommandResult> TogglePausedAsync()
		=> EnqueueAsync(emulator =>
		{
			var paused = emulator.TogglePaused();
			return new CopperScreenCommandResult(true, paused ? "Paused" : "Running", CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

	public Task<CopperScreenCommandResult> ResetAsync()
		=> EnqueueAsync(emulator =>
		{
			emulator.Reset();
			return new CopperScreenCommandResult(true, "Reset", CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

	public Task<CopperScreenCommandResult> PulsePrimaryFireAsync(int frames = 30)
		=> EnqueueAsync(emulator =>
		{
			emulator.PulsePrimaryFire(frames);
			return new CopperScreenCommandResult(true, "Fire", CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

	public Task<CopperScreenCommandResult> InsertNextDiskAsync()
	{
		var nextDiskPath = CopperScreenEmulator.ResolveNextDiskPath(CurrentState.DiskPath);
		return nextDiskPath == null
			? SetStatusAsync("no next disk image")
			: InsertDiskAsync(nextDiskPath, markChanged: true);
	}

	public Task<CopperScreenCommandResult> InsertPreviousDiskAsync()
	{
		var previousDiskPath = CopperScreenEmulator.ResolvePreviousDiskPath(CurrentState.DiskPath);
		return previousDiskPath == null
			? SetStatusAsync("no previous disk image")
			: InsertDiskAsync(previousDiskPath, markChanged: true);
	}

	public async Task<CopperScreenCommandResult> InsertDiskAsync(string diskPath, bool markChanged = true)
	{
		if (!File.Exists(diskPath))
		{
			return await SetStatusAsync("disk image not found").ConfigureAwait(false);
		}

		var fullPath = Path.GetFullPath(diskPath);
		AmigaDiskImage disk;
		try
		{
			disk = await Task.Run(() => AmigaDiskImage.Load(fullPath)).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException)
		{
			return await SetStatusAsync(ex.Message).ConfigureAwait(false);
		}

		return await EnqueueAsync(emulator =>
		{
			var inserted = emulator.InsertLoadedDisk(fullPath, disk, markChanged);
			var message = inserted ? "Inserted " + emulator.DiskName : emulator.StatusText;
			return new CopperScreenCommandResult(inserted, message, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		}).ConfigureAwait(false);
	}

	public async Task<CopperScreenCommandResult> InsertDriveDiskAsync(int driveIndex, string diskPath, bool markChanged = true)
	{
		if (driveIndex == 0)
		{
			return await InsertDiskAsync(diskPath, markChanged).ConfigureAwait(false);
		}

		if (!File.Exists(diskPath))
		{
			return await SetStatusAsync("disk image not found").ConfigureAwait(false);
		}

		var fullPath = Path.GetFullPath(diskPath);
		AmigaDiskImage disk;
		try
		{
			disk = await Task.Run(() => AmigaDiskImage.Load(fullPath)).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException)
		{
			return await SetStatusAsync(ex.Message).ConfigureAwait(false);
		}

		return await EnqueueAsync(emulator =>
		{
			var inserted = emulator.InsertLoadedDisk(driveIndex, fullPath, disk, markChanged);
			var message = inserted ? $"Inserted DF{driveIndex}: {Path.GetFileName(fullPath)}" : emulator.StatusText;
			return new CopperScreenCommandResult(inserted, message, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		}).ConfigureAwait(false);
	}

	public Task<CopperScreenCommandResult> LaunchCopperBenchPathAsync(string amigaPath)
		=> EnqueueAsync(emulator =>
		{
			var launched = emulator.LaunchCopperBenchPath(amigaPath, out var message);
			return new CopperScreenCommandResult(launched, message, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

	public void ConsumeCopperBenchRequest()
	{
		Post(_ =>
		{
			_pendingCopperBenchRequest = false;
			PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0);
		});
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Stop();
		_wake.Dispose();
		if (_disposeAudio)
		{
			_audio?.Dispose();
		}
	}

	private Task<CopperScreenCommandResult> SetStatusAsync(string message)
		=> EnqueueAsync(emulator =>
		{
			emulator.SetStatusText(message);
			return new CopperScreenCommandResult(false, message, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

	private void Post(Action<CopperScreenEmulator> action)
	{
		_commands.Enqueue(new CopperScreenCommand(action, null));
		_wake.Set();
	}

	private Task<CopperScreenCommandResult> EnqueueAsync(Func<CopperScreenEmulator, CopperScreenCommandResult> action)
	{
		var completion = new TaskCompletionSource<CopperScreenCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_commands.Enqueue(new CopperScreenCommand(
			emulator =>
			{
				var result = action(emulator);
				completion.TrySetResult(result);
			},
			completion));
		_wake.Set();
		return completion.Task;
	}

	private void Run()
	{
		var nextSilentFrameTime = Environment.TickCount64;
		while (_running)
		{
			ProcessCommands();

			if (_emulator.IsPaused)
			{
				if (!_pausedStatePublished)
				{
					PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0);
					_pausedStatePublished = true;
				}

				_wake.WaitOne(10);
				continue;
			}

			_pausedStatePublished = false;
			if (_audio == null)
			{
				var now = Environment.TickCount64;
				if (now < nextSilentFrameTime)
				{
					_wake.WaitOne((int)Math.Min(5, nextSilentFrameTime - now));
					continue;
				}

				RenderFrames(1, queuedAudioBuffers: 0);
				nextSilentFrameTime += DisplayFrameMilliseconds;
				if (nextSilentFrameTime < now - DisplayFrameMilliseconds)
				{
					nextSilentFrameTime = now + DisplayFrameMilliseconds;
				}

				continue;
			}

			var queued = _audio.QueuedBufferCount;
			var framesToRender = CalculateFramesToRender(queued, catchUpAudio: true);
			if (framesToRender <= 0)
			{
				_wake.WaitOne(1);
				continue;
			}

			RenderFrames(framesToRender, queued);
		}
	}

	private void ProcessCommands()
	{
		while (_commands.TryDequeue(out var command))
		{
			try
			{
				command.Execute(_emulator);
				PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0);
			}
			catch (Exception ex)
			{
				var result = SetEmulatorError(ex.Message);
				command.Completion?.TrySetResult(result);
			}
		}
	}

	private CopperScreenCommandResult SetEmulatorError(string message)
	{
		_emulator.SetStatusText(message);
		PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0);
		return new CopperScreenCommandResult(false, message, CurrentState);
	}

	private void RenderFrames(int framesToRender, int queuedAudioBuffers)
	{
		for (var frame = 0; frame < framesToRender && _running; frame++)
		{
			ProcessCommands();
				if (_emulator.IsPaused)
				{
					PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers);
					_pausedStatePublished = true;
					return;
				}

			var startTimestamp = Stopwatch.GetTimestamp();
			_emulator.RenderNextFrame();
			var audioFrames = _emulator.RenderAudio(_audioBuffer, AudioSampleRate, AudioChannels);
			_lastEmulationFrameMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
			if (_audio != null && !_audio.Submit(_audioBuffer.AsSpan(0, audioFrames * AudioChannels)))
			{
				_audioSubmitFailures++;
			}

			PublishCurrentFrame(framesRendered: 1, queuedAudioBuffers: _audio?.QueuedBufferCount ?? queuedAudioBuffers);
		}
	}

	private void PublishCurrentFrame(int framesRendered, int queuedAudioBuffers)
	{
		_pendingCopperBenchRequest |= _emulator.ConsumeCopperBenchRequest();
		var state = CaptureState(framesRendered, queuedAudioBuffers);
		var nextBuffer = (_writeBufferIndex + 1) % _frameBuffers.Length;
		if (nextBuffer == Volatile.Read(ref _latestBufferIndex))
		{
			nextBuffer = (nextBuffer + 1) % _frameBuffers.Length;
		}

		_emulator.Framebuffer.AsSpan().CopyTo(_frameBuffers[nextBuffer]);
		lock (_presentationSync)
		{
			_writeBufferIndex = nextBuffer;
			_latestBufferIndex = nextBuffer;
			_latestFrameNumber++;
			_latestState = state with { FrameNumber = _latestFrameNumber };
		}

		FramePublished?.Invoke();
	}

	private CopperScreenState CaptureState(int framesRendered, int queuedAudioBuffers)
	{
		return new CopperScreenState(
			_emulator.ProfileName,
			_emulator.DiskName,
			_emulator.DiskPath,
			_emulator.CpuState,
			CaptureDriveStates(),
			_emulator.StatusText,
			_emulator.IsPaused,
			_emulator.IsWorkbenchHandoffPending,
			_emulator.IsDiskSwapPending,
			_emulator.IsPrimaryFirePressed,
			_emulator.AudioFilterEnabled,
			_pendingCopperBenchRequest,
			framesRendered,
			queuedAudioBuffers,
			_latestFrameNumber,
			_droppedFrames,
			_audioSubmitFailures,
			_lastEmulationFrameMilliseconds);
	}

	private CopperScreenDriveState[] CaptureDriveStates()
	{
		_emulator.CaptureDriveStates(_driveStates);
		return _driveStates;
	}

	private sealed class CopperScreenCommand
	{
		public CopperScreenCommand(Action<CopperScreenEmulator> execute, TaskCompletionSource<CopperScreenCommandResult>? completion)
		{
			Execute = execute;
			Completion = completion;
		}

		public Action<CopperScreenEmulator> Execute { get; }

		public TaskCompletionSource<CopperScreenCommandResult>? Completion { get; }
	}
}
