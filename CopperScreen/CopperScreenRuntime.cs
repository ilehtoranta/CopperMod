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
	bool WriteProtected,
	bool ActiveDma);

internal readonly record struct CopperScreenState(
	string ProfileName,
	string DiskName,
	string? DiskPath,
	CopperScreenCpuState Cpu,
	CopperScreenDriveState[] Drives,
	CopperScreenDebugSnapshot? DebugSnapshot,
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
	long PresentationSkippedFrames,
	long PresentationBufferDroppedFrames,
	long AudioSubmitFailures,
	int PresentationQueueDepth,
	int PresentationQueueCapacity,
	long PresentationQueueFullThrottleCount,
	double LastPublishFrameMilliseconds,
	double LastPublishCopyMilliseconds,
	double LastPresentationBufferReserveMilliseconds,
	double LastEmulationFrameMilliseconds,
	double LastCpuFrameMilliseconds,
	double LastHardwareFrameMilliseconds,
	double LastDisplayFrameMilliseconds,
	double LastAudioFrameMilliseconds);

internal sealed class CopperScreenFrameLease : IDisposable
{
	private CopperScreenRuntime? _owner;

	internal CopperScreenFrameLease(CopperScreenRuntime owner, int bufferIndex, int[] framebuffer, CopperScreenState state)
	{
		_owner = owner;
		BufferIndex = bufferIndex;
		Framebuffer = framebuffer;
		State = state;
	}

	public int[] Framebuffer { get; }

	public CopperScreenState State { get; }

	private int BufferIndex { get; }

	public void Dispose()
	{
		var owner = Interlocked.Exchange(ref _owner, null);
		owner?.ReleaseFrameLease(BufferIndex);
	}
}

internal sealed class CopperScreenRuntime : IDisposable
{
	private const int AudioSampleRate = 44_100;
	private const int AudioChannels = 2;
	private const int AudioOutputBufferCount = 8;
	private const int TargetQueuedAudioBuffers = 8;
	private const int CriticalQueuedAudioBuffers = 2;
	private const int MaxFramesPerTick = 5;
	private const int TargetQueuedPresentationFrames = 1;
	private const int MaxQueuedPresentationFrames = 2;
	private const int DisplayFrameMilliseconds = 20;
	private const int MaxSteadyAudioWaitMilliseconds = 5;
	private static readonly long DisplayFrameStopwatchTicks = Math.Max(
		1,
		(long)Math.Round(Stopwatch.Frequency / AmigaConstants.A500PalVBlankHz));
	private readonly CopperScreenEmulator _emulator;
	private readonly ICopperScreenAudioOutput? _audio;
	private readonly bool _disposeAudio;
	private readonly FloppyDriveAudio? _floppyDriveAudio;
	private readonly float[] _audioBuffer;
	private readonly CopperScreenDriveState[] _driveStates = new CopperScreenDriveState[4];
	private readonly CopperScreenDriveState[] _floppyDriveAudioStates = new CopperScreenDriveState[4];
	private readonly ConcurrentQueue<CopperScreenCommand> _commands = new ConcurrentQueue<CopperScreenCommand>();
	private readonly AutoResetEvent _wake = new AutoResetEvent(false);
	private readonly object _presentationSync = new object();
	private readonly int[][] _frameBuffers;
	private readonly CopperScreenDriveState[][] _frameDriveStates;
	private readonly int[] _frameBufferLeaseCounts;
	private readonly PresentationFrameEntry[] _presentationQueue = new PresentationFrameEntry[MaxQueuedPresentationFrames];
	private Thread? _thread;
	private volatile bool _running;
	private int _writeBufferIndex;
	private int _latestBufferIndex;
	private int _presentationQueueHead;
	private int _presentationQueueCount;
	private long _latestFrameNumber;
	private long _droppedFrames;
	private long _presentationSkippedFrames;
	private long _presentationBufferDroppedFrames;
	private long _presentationQueueFullThrottleCount;
	private long _audioSubmitFailures;
	private CopperScreenState _latestState;
	private bool _pendingCopperBenchRequest;
	private bool _pausedStatePublished;
	private long _nextSteadyAudioFrameTimestamp;
	private double _lastPublishFrameMilliseconds;
	private double _lastPublishCopyMilliseconds;
	private double _lastPresentationBufferReserveMilliseconds;
	private double _lastEmulationFrameMilliseconds;
	private double _lastAudioFrameMilliseconds;
	private bool _disposed;

	private CopperScreenRuntime(CopperScreenEmulator emulator, ICopperScreenAudioOutput? audio, bool disposeAudio)
	{
		_emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
		_audio = audio;
		_disposeAudio = disposeAudio;
		_audioBuffer = new float[_emulator.AudioFramesPerAppFrame(AudioSampleRate) * AudioChannels];
		_floppyDriveAudio = FloppyDriveAudio.TryCreate(
			_emulator.FloppyDriveAudioOptions,
			_emulator.BaseDirectory,
			AudioSampleRate,
			out var floppyDriveAudioStatus);
		if (floppyDriveAudioStatus != null)
		{
			_emulator.SetStatusText(floppyDriveAudioStatus);
		}

		_frameBuffers =
		[
			new int[_emulator.Framebuffer.Length],
			new int[_emulator.Framebuffer.Length],
			new int[_emulator.Framebuffer.Length]
		];
		_frameDriveStates =
		[
			new CopperScreenDriveState[4],
			new CopperScreenDriveState[4],
			new CopperScreenDriveState[4]
		];
		_frameBufferLeaseCounts = new int[_frameBuffers.Length];
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
				return WithCurrentPresentationDiagnostics(_latestState);
			}
		}
	}

	public bool HasPendingPresentationFrames
	{
		get
		{
			lock (_presentationSync)
			{
				return _presentationQueueCount > 0;
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

	public static CopperScreenRuntime Create(CopperScreenStartupOptions startupOptions)
	{
		var emulator = CopperScreenEmulator.Create(startupOptions);
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

		if (queued >= CriticalQueuedAudioBuffers)
		{
			return 1;
		}

		var availableBuffers = AudioOutputBufferCount - queued;
		return Math.Clamp(TargetQueuedAudioBuffers - queued, 1, Math.Min(MaxFramesPerTick, availableBuffers));
	}

	internal static int CalculatePresentationQueueCapacity(int? queuedAudioBuffers)
		=> queuedAudioBuffers.HasValue && queuedAudioBuffers.Value < CriticalQueuedAudioBuffers
			? MaxQueuedPresentationFrames
			: TargetQueuedPresentationFrames;

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

	public bool TryCopyNextPresentationFrame(Span<int> destination, ref long lastSeenFrameNumber, out CopperScreenState state, bool force = false)
	{
		lock (_presentationSync)
		{
			if (destination.Length < _emulator.Framebuffer.Length)
			{
				throw new ArgumentException("Destination framebuffer is too small.", nameof(destination));
			}

			if (_presentationQueueCount > 0)
			{
				var entry = DequeuePresentationFrameNoLock();
				RecordSkippedPresentationFrames(lastSeenFrameNumber, entry.FrameNumber);

				_frameBuffers[entry.BufferIndex].AsSpan().CopyTo(destination);
				state = WithCurrentPresentationDiagnostics(entry.State);
				lastSeenFrameNumber = entry.FrameNumber;
				_wake.Set();
				return true;
			}

			if (!force)
			{
				state = WithCurrentPresentationDiagnostics(_latestState);
				return false;
			}

			if (_latestFrameNumber != lastSeenFrameNumber)
			{
				RecordSkippedPresentationFrames(lastSeenFrameNumber, _latestFrameNumber);
			}

			_frameBuffers[_latestBufferIndex].AsSpan().CopyTo(destination);
			state = WithCurrentPresentationDiagnostics(_latestState);
			lastSeenFrameNumber = _latestFrameNumber;
			return true;
		}
	}

	public CopperScreenFrameLease? TryAcquireNextPresentationFrame(ref long lastSeenFrameNumber, bool force = false)
	{
		lock (_presentationSync)
		{
			if (_presentationQueueCount > 0)
			{
				var entry = DequeuePresentationFrameNoLock();
				RecordSkippedPresentationFrames(lastSeenFrameNumber, entry.FrameNumber);

				_frameBufferLeaseCounts[entry.BufferIndex]++;
				lastSeenFrameNumber = entry.FrameNumber;
				_wake.Set();
				return new CopperScreenFrameLease(
					this,
					entry.BufferIndex,
					_frameBuffers[entry.BufferIndex],
					WithCurrentPresentationDiagnostics(entry.State));
			}

			if (!force)
			{
				return null;
			}

			if (_latestFrameNumber != lastSeenFrameNumber)
			{
				RecordSkippedPresentationFrames(lastSeenFrameNumber, _latestFrameNumber);
			}

			var bufferIndex = _latestBufferIndex;
			_frameBufferLeaseCounts[bufferIndex]++;
			lastSeenFrameNumber = _latestFrameNumber;
			return new CopperScreenFrameLease(
				this,
				bufferIndex,
				_frameBuffers[bufferIndex],
				WithCurrentPresentationDiagnostics(_latestState));
		}
	}

	public void KeyDown(AmigaRawKey key)
		=> Post(emulator => emulator.KeyDown(key));

	public void KeyUp(AmigaRawKey key)
		=> Post(emulator => emulator.KeyUp(key));

	public void MoveMousePort(int deltaX, int deltaY)
		=> Post(emulator => emulator.MoveMousePort(deltaX, deltaY));

	public void SetMousePortPosition(int x, int y)
		=> Post(emulator => emulator.SetMousePortPosition(x, y));

	public void SetMousePresentationPosition(int x, int y)
		=> Post(emulator => emulator.SetMousePresentationPosition(x, y));

	public void SetMouseButtons(bool primaryFirePressed, bool secondFirePressed)
		=> Post(emulator => emulator.SetMouseButtons(primaryFirePressed, secondFirePressed));

	public void SetJoystickPort(bool up, bool down, bool left, bool right, bool primaryFirePressed, bool secondFirePressed)
		=> Post(emulator => emulator.SetJoystickPort(up, down, left, right, primaryFirePressed, secondFirePressed));

	public void SetJoystickPort(int portIndex, bool up, bool down, bool left, bool right, bool primaryFirePressed, bool secondFirePressed)
		=> Post(emulator => emulator.SetJoystickPort(portIndex, up, down, left, right, primaryFirePressed, secondFirePressed));

	public void SetInputOptions(CopperScreenInputOptions inputOptions)
		=> Post(emulator => emulator.SetInputOptions(inputOptions));

	public void SetPresentationOptions(CopperScreenPresentationOptions options)
		=> Post(emulator => emulator.SetPresentationOptions(options), publishAfterExecute: true);

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
		if (!CopperScreenDiskImageArchive.DiskPathExists(diskPath))
		{
			return await SetStatusAsync("disk image not found").ConfigureAwait(false);
		}

		var fullPath = CopperScreenDiskImageArchive.NormalizeDiskPath(diskPath);
		AmigaDiskImage disk;
		try
		{
			disk = await Task.Run(() => CopperScreenDiskImageArchive.LoadDiskImage(fullPath)).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
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

		if (!CopperScreenDiskImageArchive.DiskPathExists(diskPath))
		{
			return await SetStatusAsync("disk image not found").ConfigureAwait(false);
		}

		var fullPath = CopperScreenDiskImageArchive.NormalizeDiskPath(diskPath);
		AmigaDiskImage disk;
		try
		{
			disk = await Task.Run(() => CopperScreenDiskImageArchive.LoadDiskImage(fullPath)).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
		{
			return await SetStatusAsync(ex.Message).ConfigureAwait(false);
		}

		return await EnqueueAsync(emulator =>
		{
			var inserted = emulator.InsertLoadedDisk(driveIndex, fullPath, disk, markChanged);
			var message = inserted ? $"Inserted DF{driveIndex}: {CopperScreenDiskImageArchive.GetDisplayName(fullPath)}" : emulator.StatusText;
			return new CopperScreenCommandResult(inserted, message, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		}).ConfigureAwait(false);
	}

	public Task<CopperScreenCommandResult> SetDriveWriteProtectedAsync(int driveIndex, bool writeProtected)
		=> EnqueueAsync(emulator =>
		{
			var updated = emulator.SetDriveWriteProtected(driveIndex, writeProtected);
			return new CopperScreenCommandResult(updated, emulator.StatusText, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

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
		}, publishAfterExecute: true);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Stop();
		_emulator.Dispose();
		_wake.Dispose();
		if (_disposeAudio)
		{
			_audio?.Dispose();
		}

		_floppyDriveAudio?.Dispose();
	}

	private Task<CopperScreenCommandResult> SetStatusAsync(string message)
		=> EnqueueAsync(emulator =>
		{
			emulator.SetStatusText(message);
			return new CopperScreenCommandResult(false, message, CaptureState(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0));
		});

	private void Post(Action<CopperScreenEmulator> action, bool publishAfterExecute = false)
	{
		_commands.Enqueue(new CopperScreenCommand(action, null, publishAfterExecute));
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
			completion,
			publishAfterExecute: true));
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
				if (WaitForPresentationQueueCapacity(null))
				{
					continue;
				}

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

			if (ShouldThrottleSteadyAudioRefill(queued, framesToRender))
			{
				var now = Stopwatch.GetTimestamp();
				if (_nextSteadyAudioFrameTimestamp > 0 && now < _nextSteadyAudioFrameTimestamp)
				{
					var waitMilliseconds = CalculateSteadyAudioWaitMilliseconds(_nextSteadyAudioFrameTimestamp - now);
					if (waitMilliseconds > 0)
					{
						_wake.WaitOne(waitMilliseconds);
					}
					else
					{
						Thread.Yield();
					}

					continue;
				}
			}
			else
			{
				_nextSteadyAudioFrameTimestamp = Stopwatch.GetTimestamp();
			}

			RenderFrames(framesToRender, queued);
			if (ShouldThrottleSteadyAudioRefill(queued, framesToRender))
			{
				ScheduleNextSteadyAudioFrame();
			}
		}
	}

	private void ProcessCommands()
	{
		while (_commands.TryDequeue(out var command))
		{
			try
			{
				command.Execute(_emulator);
				if (command.PublishAfterExecute)
				{
					PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? 0);
				}
			}
			catch (Exception ex)
			{
				var result = SetEmulatorError(ex.Message);
				command.Completion?.TrySetResult(result);
			}
		}
	}

	internal static bool ShouldThrottleSteadyAudioRefill(int queuedAudioBuffers, int framesToRender)
		=> framesToRender == 1 && queuedAudioBuffers >= CriticalQueuedAudioBuffers;

	internal static long SteadyAudioFrameStopwatchTicks => DisplayFrameStopwatchTicks;

	internal static int CalculateSteadyAudioWaitMilliseconds(long remainingStopwatchTicks)
	{
		if (remainingStopwatchTicks <= 0)
		{
			return 0;
		}

		var milliseconds = (remainingStopwatchTicks * 1000) / Stopwatch.Frequency;
		if (milliseconds <= 0)
		{
			return 0;
		}

		return (int)Math.Min(MaxSteadyAudioWaitMilliseconds, milliseconds);
	}

	private void ScheduleNextSteadyAudioFrame()
	{
		var now = Stopwatch.GetTimestamp();
		if (_nextSteadyAudioFrameTimestamp <= 0 ||
			now - _nextSteadyAudioFrameTimestamp > DisplayFrameStopwatchTicks)
		{
			_nextSteadyAudioFrameTimestamp = now + DisplayFrameStopwatchTicks;
			return;
		}

		_nextSteadyAudioFrameTimestamp += DisplayFrameStopwatchTicks;
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

			if (_audio == null && WaitForPresentationQueueCapacity(null))
			{
				return;
			}

			var presentationQueuedAudioBuffers = _audio?.QueuedBufferCount ?? queuedAudioBuffers;
			var reservedBufferIndex = TryReservePresentationFrameBuffer(
				presentationQueuedAudioBuffers,
				out var reservedFrameNumber);
			var presentationFramebuffer = reservedBufferIndex >= 0
				? _frameBuffers[reservedBufferIndex]
				: _emulator.Framebuffer;
			var startTimestamp = Stopwatch.GetTimestamp();
			int audioFrames;
			try
			{
				_emulator.RenderNextFrame(presentationFramebuffer);
				var audioStartTimestamp = Stopwatch.GetTimestamp();
				audioFrames = _emulator.RenderAudio(_audioBuffer, AudioSampleRate, AudioChannels);
				if (_audio != null && _floppyDriveAudio != null && audioFrames > 0)
				{
					_emulator.CaptureDriveStates(_floppyDriveAudioStates);
					_floppyDriveAudio.Mix(_audioBuffer.AsSpan(0, audioFrames * AudioChannels), audioFrames, AudioChannels, _floppyDriveAudioStates);
				}

				_lastAudioFrameMilliseconds = Stopwatch.GetElapsedTime(audioStartTimestamp).TotalMilliseconds;
			}
			catch (Exception ex)
			{
				CopperScreenCrashLog.WriteException("CopperScreenRuntime.RenderFrames", ex, ex);
				_emulator.CaptureFatalException(ex);
				_audioBuffer.AsSpan().Clear();
				_lastAudioFrameMilliseconds = 0;
				_lastEmulationFrameMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
				PublishCurrentFrame(framesRendered: 0, queuedAudioBuffers: _audio?.QueuedBufferCount ?? queuedAudioBuffers);
				return;
			}

			_lastEmulationFrameMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
			if (_audio != null && !_audio.Submit(_audioBuffer.AsSpan(0, audioFrames * AudioChannels)))
			{
				_audioSubmitFailures++;
			}

			if (reservedBufferIndex >= 0)
			{
				PublishRenderedFrame(
					reservedBufferIndex,
					reservedFrameNumber,
					framesRendered: 1,
					queuedAudioBuffers: _audio?.QueuedBufferCount ?? queuedAudioBuffers);
			}
		}
	}

	private int TryReservePresentationFrameBuffer(int queuedAudioBuffers, out long frameNumber)
	{
		var reserveStartTimestamp = Stopwatch.GetTimestamp();
		lock (_presentationSync)
		{
			if (ShouldReplaceQueuedPresentationFrames(queuedAudioBuffers))
			{
				var queueCapacity = GetPublishPresentationQueueCapacity(queuedAudioBuffers);
				while (_presentationQueueCount >= queueCapacity && TryDropOldestPresentationFrameNoLock())
				{
					_presentationQueueFullThrottleCount++;
				}
			}

			if (_presentationQueueCount >= MaxQueuedPresentationFrames)
			{
				_presentationQueueFullThrottleCount++;
				frameNumber = 0;
				_lastPresentationBufferReserveMilliseconds = Stopwatch.GetElapsedTime(reserveStartTimestamp).TotalMilliseconds;
				return -1;
			}

			var bufferIndex = SelectWritableFrameBufferNoLock();
			if (bufferIndex < 0)
			{
				_droppedFrames++;
				_presentationBufferDroppedFrames++;
				frameNumber = 0;
				_lastPresentationBufferReserveMilliseconds = Stopwatch.GetElapsedTime(reserveStartTimestamp).TotalMilliseconds;
				return -1;
			}

			frameNumber = _latestFrameNumber + 1;
			_lastPresentationBufferReserveMilliseconds = Stopwatch.GetElapsedTime(reserveStartTimestamp).TotalMilliseconds;
			return bufferIndex;
		}
	}

	private void PublishRenderedFrame(int bufferIndex, long frameNumber, int framesRendered, int queuedAudioBuffers)
	{
		var publishStartTimestamp = Stopwatch.GetTimestamp();
		_pendingCopperBenchRequest |= _emulator.ConsumeCopperBenchRequest();
		_lastPublishCopyMilliseconds = 0;
		var state = CaptureState(framesRendered, queuedAudioBuffers, _frameDriveStates[bufferIndex]);
		lock (_presentationSync)
		{
			_writeBufferIndex = bufferIndex;
			_latestBufferIndex = bufferIndex;
			_latestFrameNumber = frameNumber;
			_latestState = WithCurrentPresentationDiagnostics(state with { FrameNumber = frameNumber });
			EnqueuePresentationFrameNoLock(new PresentationFrameEntry(bufferIndex, frameNumber, _latestState));
		}

		_lastPublishFrameMilliseconds = Stopwatch.GetElapsedTime(publishStartTimestamp).TotalMilliseconds;
		FramePublished?.Invoke();
	}

	private void PublishCurrentFrame(int framesRendered, int queuedAudioBuffers)
	{
		var publishStartTimestamp = Stopwatch.GetTimestamp();
		_pendingCopperBenchRequest |= _emulator.ConsumeCopperBenchRequest();
		int nextBuffer;
		long frameNumber;
		lock (_presentationSync)
		{
			if (ShouldReplaceQueuedPresentationFrames(queuedAudioBuffers))
			{
				var queueCapacity = GetPublishPresentationQueueCapacity(queuedAudioBuffers);
				while (_presentationQueueCount >= queueCapacity && TryDropOldestPresentationFrameNoLock())
				{
					_presentationQueueFullThrottleCount++;
				}
			}

			if (_presentationQueueCount >= MaxQueuedPresentationFrames)
			{
				_presentationQueueFullThrottleCount++;
				_lastPublishCopyMilliseconds = 0;
				_lastPublishFrameMilliseconds = Stopwatch.GetElapsedTime(publishStartTimestamp).TotalMilliseconds;
				return;
			}

			nextBuffer = SelectWritableFrameBufferNoLock();
			if (nextBuffer < 0)
			{
				_droppedFrames++;
				_presentationBufferDroppedFrames++;
				_lastPublishCopyMilliseconds = 0;
				_lastPublishFrameMilliseconds = Stopwatch.GetElapsedTime(publishStartTimestamp).TotalMilliseconds;
				return;
			}

			frameNumber = _latestFrameNumber + 1;
		}

		var copyStartTimestamp = Stopwatch.GetTimestamp();
		_emulator.Framebuffer.AsSpan().CopyTo(_frameBuffers[nextBuffer]);
		_lastPublishCopyMilliseconds = Stopwatch.GetElapsedTime(copyStartTimestamp).TotalMilliseconds;
		_lastPublishFrameMilliseconds = Stopwatch.GetElapsedTime(publishStartTimestamp).TotalMilliseconds;
		var state = CaptureState(framesRendered, queuedAudioBuffers, _frameDriveStates[nextBuffer]);
		lock (_presentationSync)
		{
			_writeBufferIndex = nextBuffer;
			_latestBufferIndex = nextBuffer;
			_latestFrameNumber = frameNumber;
			_latestState = WithCurrentPresentationDiagnostics(state with { FrameNumber = frameNumber });
			EnqueuePresentationFrameNoLock(new PresentationFrameEntry(nextBuffer, frameNumber, _latestState));
		}

		FramePublished?.Invoke();
	}

	private bool WaitForPresentationQueueCapacity(int? queuedAudioBuffers)
	{
		lock (_presentationSync)
		{
			var capacity = CalculatePresentationQueueCapacity(queuedAudioBuffers);
			if (_presentationQueueCount < capacity)
			{
				return false;
			}

			_presentationQueueFullThrottleCount++;
		}

		_wake.WaitOne(1);
		return true;
	}

	private void RecordSkippedPresentationFrames(long lastSeenFrameNumber, long frameNumber)
	{
		if (lastSeenFrameNumber == 0 || frameNumber <= lastSeenFrameNumber + 1)
		{
			return;
		}

		var skipped = frameNumber - lastSeenFrameNumber - 1;
		_droppedFrames += skipped;
		_presentationSkippedFrames += skipped;
	}

	private CopperScreenState WithCurrentPresentationDiagnostics(CopperScreenState state)
		=> state with
		{
			DroppedFrames = _droppedFrames,
			PresentationSkippedFrames = _presentationSkippedFrames,
			PresentationBufferDroppedFrames = _presentationBufferDroppedFrames,
			PresentationQueueDepth = _presentationQueueCount,
			PresentationQueueCapacity = GetPublishPresentationQueueCapacity(state.QueuedAudioBuffers),
			PresentationQueueFullThrottleCount = _presentationQueueFullThrottleCount,
			LastPublishFrameMilliseconds = _lastPublishFrameMilliseconds,
			LastPublishCopyMilliseconds = _lastPublishCopyMilliseconds,
			LastPresentationBufferReserveMilliseconds = _lastPresentationBufferReserveMilliseconds
		};

	private int GetPublishPresentationQueueCapacity(int queuedAudioBuffers)
		=> _audio == null ? MaxQueuedPresentationFrames : CalculatePresentationQueueCapacity(queuedAudioBuffers);

	private bool ShouldReplaceQueuedPresentationFrames(int queuedAudioBuffers)
		=> _audio == null || queuedAudioBuffers < CriticalQueuedAudioBuffers;

	private int SelectWritableFrameBufferNoLock()
	{
		for (var offset = 1; offset <= _frameBuffers.Length; offset++)
		{
			var candidate = (_writeBufferIndex + offset) % _frameBuffers.Length;
			if (candidate != _latestBufferIndex &&
				_frameBufferLeaseCounts[candidate] == 0 &&
				!IsPresentationBufferQueuedNoLock(candidate))
			{
				return candidate;
			}
		}

		return -1;
	}

	internal void ReleaseFrameLease(int bufferIndex)
	{
		lock (_presentationSync)
		{
			if ((uint)bufferIndex >= (uint)_frameBufferLeaseCounts.Length || _frameBufferLeaseCounts[bufferIndex] <= 0)
			{
				return;
			}

			_frameBufferLeaseCounts[bufferIndex]--;
		}

		_wake.Set();
	}

	private CopperScreenState CaptureState(int framesRendered, int queuedAudioBuffers)
		=> CaptureState(framesRendered, queuedAudioBuffers, _driveStates);

	private CopperScreenState CaptureState(int framesRendered, int queuedAudioBuffers, CopperScreenDriveState[] driveStates)
	{
		return new CopperScreenState(
			_emulator.ProfileName,
			_emulator.DiskName,
			_emulator.DiskPath,
			_emulator.CpuState,
			CaptureDriveStates(driveStates),
			_emulator.DebugSnapshot,
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
			_presentationSkippedFrames,
			_presentationBufferDroppedFrames,
			_audioSubmitFailures,
			_presentationQueueCount,
			MaxQueuedPresentationFrames,
			_presentationQueueFullThrottleCount,
			_lastPublishFrameMilliseconds,
			_lastPublishCopyMilliseconds,
			_lastPresentationBufferReserveMilliseconds,
			_lastEmulationFrameMilliseconds,
			_emulator.LastFrameTiming.CpuMilliseconds,
			_emulator.LastFrameTiming.HardwareMilliseconds,
			_emulator.LastFrameTiming.DisplayMilliseconds,
			_lastAudioFrameMilliseconds);
	}

	private CopperScreenDriveState[] CaptureDriveStates()
		=> CaptureDriveStates(_driveStates);

	private CopperScreenDriveState[] CaptureDriveStates(CopperScreenDriveState[] destination)
	{
		_emulator.CaptureDriveStates(destination);
		return destination;
	}

	private void EnqueuePresentationFrameNoLock(PresentationFrameEntry entry)
	{
		var tail = (_presentationQueueHead + _presentationQueueCount) % _presentationQueue.Length;
		_presentationQueue[tail] = entry;
		_presentationQueueCount++;
	}

	private PresentationFrameEntry DequeuePresentationFrameNoLock()
	{
		var entry = _presentationQueue[_presentationQueueHead];
		_presentationQueue[_presentationQueueHead] = default;
		_presentationQueueHead = (_presentationQueueHead + 1) % _presentationQueue.Length;
		_presentationQueueCount--;
		return entry;
	}

	private bool TryDropOldestPresentationFrameNoLock()
	{
		if (_presentationQueueCount <= 0)
		{
			return false;
		}

		_presentationQueue[_presentationQueueHead] = default;
		_presentationQueueHead = (_presentationQueueHead + 1) % _presentationQueue.Length;
		_presentationQueueCount--;
		return true;
	}

	private bool IsPresentationBufferQueuedNoLock(int bufferIndex)
	{
		for (var offset = 0; offset < _presentationQueueCount; offset++)
		{
			var queueIndex = (_presentationQueueHead + offset) % _presentationQueue.Length;
			if (_presentationQueue[queueIndex].BufferIndex == bufferIndex)
			{
				return true;
			}
		}

		return false;
	}

	private sealed class CopperScreenCommand
	{
		public CopperScreenCommand(
			Action<CopperScreenEmulator> execute,
			TaskCompletionSource<CopperScreenCommandResult>? completion,
			bool publishAfterExecute)
		{
			Execute = execute;
			Completion = completion;
			PublishAfterExecute = publishAfterExecute;
		}

		public Action<CopperScreenEmulator> Execute { get; }

		public TaskCompletionSource<CopperScreenCommandResult>? Completion { get; }

		public bool PublishAfterExecute { get; }
	}

	private readonly record struct PresentationFrameEntry(int BufferIndex, long FrameNumber, CopperScreenState State);
}
