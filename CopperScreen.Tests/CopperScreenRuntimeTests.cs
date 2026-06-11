using System.Reflection;
using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenRuntimeTests
{
	[Fact]
	public async Task CommandQueuePreservesToggleOrder()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), new FakeAudioOutput());
		runtime.Start();

		var first = runtime.TogglePausedAsync();
		var second = runtime.TogglePausedAsync();
		var third = runtime.TogglePausedAsync();
		var results = await Task.WhenAll(first, second, third);

		Assert.True(results[0].State.IsPaused);
		Assert.False(results[1].State.IsPaused);
		Assert.True(results[2].State.IsPaused);
		Assert.True(runtime.CurrentState.IsPaused);
	}

	[Fact]
	public async Task PauseDoesNotContinuouslyPublishFrames()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), new FakeAudioOutput());
		runtime.Start();

		await runtime.TogglePausedAsync();
		await Task.Delay(50);
		var firstPausedFrame = runtime.CurrentState.FrameNumber;
		await Task.Delay(100);

		Assert.Equal(firstPausedFrame, runtime.CurrentState.FrameNumber);
	}

	[Fact]
	public void QueuedPresentationFramesAreAcquiredInPublishOrderWithoutPresenterSkips()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var lastSeen = 0L;
		DrainPresentationFrames(runtime, ref lastSeen);

		PublishFrame(emulator, runtime, unchecked((int)0xFF112233));
		PublishFrame(emulator, runtime, unchecked((int)0xFF445566));

		using var first = runtime.TryAcquireNextPresentationFrame(ref lastSeen);
		Assert.NotNull(first);
		Assert.Equal(unchecked((int)0xFF112233), first.Framebuffer[0]);
		using var second = runtime.TryAcquireNextPresentationFrame(ref lastSeen);
		Assert.NotNull(second);
		Assert.Equal(unchecked((int)0xFF445566), second.Framebuffer[0]);
		Assert.Equal(0, second.State.PresentationSkippedFrames);
		Assert.Equal(0, second.State.DroppedFrames);
	}

	[Fact]
	public void AudioRefillKeepsTargetQueueDepth()
	{
		using var audio = new FakeAudioOutput();
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);
		var lastSeen = 0L;
		runtime.Start();

		Assert.True(SpinWait.SpinUntil(() =>
		{
			DrainPresentationFrames(runtime, ref lastSeen);
			return audio.QueuedBufferCount >= 5;
		}, TimeSpan.FromSeconds(1)));
		Assert.InRange(audio.SubmitCount, 5, 8);
	}

	[Fact]
	public void StateIncludesCompactToolbarIndicatorsForAllDrives()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk());

		var state = runtime.CurrentState;

		Assert.Equal(4, state.Drives.Length);
		Assert.Equal(new[] { 0, 1, 2, 3 }, state.Drives.Select(drive => drive.Index).ToArray());
		Assert.True(state.Drives[0].Connected);
		Assert.True(state.Drives[1].Connected);
		Assert.False(state.Drives[2].Connected);
		Assert.False(state.Drives[3].Connected);
		_ = state.AudioFilterEnabled;
		_ = state.Cpu.ProgramCounter;
		_ = state.Cpu.LastInstructionProgramCounter;
	}

	[Fact]
	public async Task InsertedAdfDefaultsWriteProtectedAndRuntimeCanToggleIt()
	{
		using var temp = new TempDiskFile(".adf");
		using var nextTemp = new TempDiskFile(".adf");
		var adf = new byte[AmigaDiskImage.StandardAdfSize];
		File.WriteAllBytes(temp.Path, adf);
		File.WriteAllBytes(nextTemp.Path, adf);
		var emulator = CopperScreenEmulator.CreateWithLoadedDisk(
			[temp.Path],
			AppContext.BaseDirectory,
			AmigaDiskImage.FromAdfBytes(adf, Path.GetFileName(temp.Path)));
		emulator.RenderNextFrame();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		runtime.Start();

		Assert.True(runtime.CurrentState.Drives[0].HasDisk);
		Assert.True(runtime.CurrentState.Drives[0].WriteProtected);

		var unprotected = await runtime.SetDriveWriteProtectedAsync(0, false);
		Assert.True(unprotected.Success);
		Assert.False(unprotected.State.Drives[0].WriteProtected);

		var insertedNext = await runtime.InsertDiskAsync(nextTemp.Path, markChanged: false);
		Assert.True(insertedNext.Success);
		Assert.True(insertedNext.State.Drives[0].WriteProtected);

		var protectedAgain = await runtime.SetDriveWriteProtectedAsync(0, true);
		Assert.True(protectedAgain.Success);
		Assert.True(protectedAgain.State.Drives[0].WriteProtected);
	}

	[Fact]
	public async Task FramePublishedNotifiesPresentationLoop()
	{
		using var audio = new FakeAudioOutput();
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);
		var framePublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		runtime.FramePublished += () => framePublished.TrySetResult();

		runtime.Start();

		await framePublished.Task.WaitAsync(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task PublishingDoesNotOverwriteReadableFrameOutsidePresentationLock()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var sync = GetPrivateField<object>(runtime, "_presentationSync");
		var frameBuffers = GetPrivateField<int[][]>(runtime, "_frameBuffers");
		var publish = typeof(CopperScreenRuntime).GetMethod("PublishCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(publish);
		var lastSeenFrameNumber = 0L;
		using (var initial = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: true))
		{
			Assert.NotNull(initial);
		}

		var latestBufferIndex = GetPrivateField<int>(runtime, "_latestBufferIndex");
		var originalReadablePixel = frameBuffers[latestBufferIndex][0];
		const int sentinel = unchecked((int)0x55AA33CC);
		Array.Fill(emulator.Framebuffer, sentinel);
		using var started = new ManualResetEventSlim();
		Task publishTask;
		lock (sync)
		{
			publishTask = Task.Run(() =>
			{
				started.Set();
				publish.Invoke(runtime, [1, 0]);
			});
			Assert.True(started.Wait(TimeSpan.FromSeconds(1)));
			Thread.Sleep(50);

			Assert.Equal(originalReadablePixel, frameBuffers[latestBufferIndex][0]);
		}

		await publishTask.WaitAsync(TimeSpan.FromSeconds(1));
		var destination = new int[runtime.Width * runtime.Height];

		Assert.True(runtime.TryCopyNextPresentationFrame(destination, ref lastSeenFrameNumber, out _, force: true));
		Assert.Equal(sentinel, destination[0]);
	}

	[Fact]
	public void FrameLeaseKeepsRuntimeFromOverwritingReadableBuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var publish = typeof(CopperScreenRuntime).GetMethod("PublishCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(publish);
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);
		const int leasedPixel = unchecked((int)0xFF112233);
		Array.Fill(emulator.Framebuffer, leasedPixel);
		publish.Invoke(runtime, [1, 0]);
		using var lease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(lease);
		var leasedBuffer = lease.Framebuffer;

		for (var i = 0; i < 4; i++)
		{
			Array.Fill(emulator.Framebuffer, unchecked((int)(0xFF445500u + (uint)i)));
			publish.Invoke(runtime, [1, 0]);
		}

		Assert.Equal(leasedPixel, leasedBuffer[0]);
	}

	[Fact]
	public void FullPresentationQueuePreventsPublishingWithoutReplacingOldestFrame()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);

		PublishFrame(emulator, runtime, unchecked((int)0xFF010203));
		PublishFrame(emulator, runtime, unchecked((int)0xFF040506));
		var frameBeforeRejectedPublish = runtime.CurrentState.FrameNumber;
		PublishFrame(emulator, runtime, unchecked((int)0xFF070809));

		Assert.Equal(frameBeforeRejectedPublish, runtime.CurrentState.FrameNumber);
		Assert.Equal(2, runtime.CurrentState.PresentationQueueDepth);
		Assert.True(runtime.CurrentState.PresentationQueueFullThrottleCount > 0);
		using var first = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(first);
		Assert.Equal(unchecked((int)0xFF010203), first.Framebuffer[0]);
		using var second = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(second);
		Assert.Equal(unchecked((int)0xFF040506), second.Framebuffer[0]);
	}

	[Fact]
	public void ForceStatusReturnsCurrentFrameWhenNoQueuedFrameExists()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk());
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);

		Assert.Null(runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber));
		using var forced = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: true);

		Assert.NotNull(forced);
		Assert.Equal(runtime.CurrentState.FrameNumber, forced.State.FrameNumber);
		Assert.Equal(0, forced.State.PresentationQueueDepth);
	}

	[Fact]
	public void PublishingTracksNoFreePresentationBufferDropsSeparately()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var publish = typeof(CopperScreenRuntime).GetMethod("PublishCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(publish);
		var lastSeenFrameNumber = 0L;

		var firstLease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: true);
		Assert.NotNull(firstLease);
		publish.Invoke(runtime, [1, 0]);
		var secondLease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: false);
		Assert.NotNull(secondLease);
		publish.Invoke(runtime, [1, 0]);
		var thirdLease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: false);
		Assert.NotNull(thirdLease);

		publish.Invoke(runtime, [1, 0]);
		var stateLease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: true);
		Assert.NotNull(stateLease);

		Assert.Equal(1, stateLease.State.DroppedFrames);
		Assert.Equal(0, stateLease.State.PresentationSkippedFrames);
		Assert.Equal(1, stateLease.State.PresentationBufferDroppedFrames);

		stateLease.Dispose();
		thirdLease.Dispose();
		secondLease.Dispose();
		firstLease.Dispose();
	}

	[Fact]
	public void HealthyAudioThrottlesWhenTargetVideoQueueDepthIsReached()
	{
		using var audio = new FakeAudioOutput(initialQueued: 2);
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);
		Assert.Equal(1, runtime.CurrentState.PresentationQueueDepth);

		runtime.Start();
		Thread.Sleep(50);

		Assert.Equal(0, audio.SubmitCount);
		Assert.True(runtime.CurrentState.PresentationQueueFullThrottleCount > 0);
		Assert.Equal(1, runtime.CurrentState.PresentationQueueDepth);
	}

	[Fact]
	public void CriticalAudioMayFillSecondVideoQueueSlotButDoesNotOverwriteIt()
	{
		using var audio = new FakeAudioOutput(initialQueued: 0);
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);

		runtime.Start();

		Assert.True(SpinWait.SpinUntil(() => runtime.CurrentState.PresentationQueueDepth == 2, TimeSpan.FromSeconds(1)));
		var submitCountAfterQueueFilled = audio.SubmitCount;
		Thread.Sleep(50);

		Assert.Equal(2, runtime.CurrentState.PresentationQueueDepth);
		Assert.Equal(submitCountAfterQueueFilled, audio.SubmitCount);
		Assert.True(runtime.CurrentState.PresentationQueueFullThrottleCount > 0);
	}

	private static T GetPrivateField<T>(CopperScreenRuntime runtime, string fieldName)
	{
		var field = typeof(CopperScreenRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsType<T>(field.GetValue(runtime));
	}

	private static void PublishFrame(CopperScreenEmulator emulator, CopperScreenRuntime runtime, int pixel)
	{
		var publish = typeof(CopperScreenRuntime).GetMethod("PublishCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(publish);
		Array.Fill(emulator.Framebuffer, pixel);
		publish.Invoke(runtime, [1, 0]);
	}

	private static void DrainPresentationFrames(CopperScreenRuntime runtime, ref long lastSeenFrameNumber)
	{
		while (true)
		{
			using var lease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
			if (lease == null)
			{
				return;
			}
		}
	}

	private sealed class TempDiskFile : IDisposable
	{
		public TempDiskFile(string extension)
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
		}

		public string Path { get; }

		public void Dispose()
		{
			try
			{
				File.Delete(Path);
			}
			catch (IOException)
			{
			}
		}
	}

	private sealed class FakeAudioOutput : ICopperScreenAudioOutput
	{
		private int _queued;

		public FakeAudioOutput(int initialQueued = 0)
		{
			_queued = initialQueued;
		}

		public int SubmitCount { get; private set; }

		public int QueuedBufferCount => Volatile.Read(ref _queued);

		public bool Submit(ReadOnlySpan<float> samples)
		{
			_ = samples;
			SubmitCount++;
			while (true)
			{
				var queued = Volatile.Read(ref _queued);
				if (queued >= 8)
				{
					return false;
				}

				if (Interlocked.CompareExchange(ref _queued, queued + 1, queued) == queued)
				{
					return true;
				}
			}
		}

		public void Dispose()
		{
		}
	}
}
