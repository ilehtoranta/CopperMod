using System.Reflection;
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
	public async Task LatestFramePublishingDropsStaleFramesWithoutBlockingRuntime()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk());
		var framebuffer = new int[runtime.Width * runtime.Height];
		var lastSeen = 0L;
		runtime.Start();

		await Task.Delay(40);
		Assert.True(runtime.TryCopyLatestFrame(framebuffer, ref lastSeen, out _, force: false));
		await Task.Delay(140);
		Assert.True(runtime.TryCopyLatestFrame(framebuffer, ref lastSeen, out var state, force: false));

		Assert.True(state.DroppedFrames > 0, $"Expected stale frames to be dropped, saw {state.DroppedFrames}.");
	}

	[Fact]
	public void AudioRefillKeepsTargetQueueDepth()
	{
		using var audio = new FakeAudioOutput();
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);
		runtime.Start();

		Assert.True(SpinWait.SpinUntil(() => audio.QueuedBufferCount >= 5, TimeSpan.FromSeconds(1)));
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
		var lastSeenFrameNumber = 0L;

		Assert.True(runtime.TryCopyLatestFrame(destination, ref lastSeenFrameNumber, out _, force: true));
		Assert.Equal(sentinel, destination[0]);
	}

	[Fact]
	public void FrameLeaseKeepsRuntimeFromOverwritingReadableBuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var publish = typeof(CopperScreenRuntime).GetMethod("PublishCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(publish);
		const int leasedPixel = unchecked((int)0xFF112233);
		Array.Fill(emulator.Framebuffer, leasedPixel);
		publish.Invoke(runtime, [1, 0]);
		var lastSeenFrameNumber = 0L;
		using var lease = runtime.TryAcquireLatestFrame(ref lastSeenFrameNumber);
		Assert.NotNull(lease);
		var leasedBuffer = lease.Framebuffer;

		for (var i = 0; i < 4; i++)
		{
			Array.Fill(emulator.Framebuffer, unchecked((int)(0xFF445500u + (uint)i)));
			publish.Invoke(runtime, [1, 0]);
		}

		Assert.Equal(leasedPixel, leasedBuffer[0]);
	}

	private static T GetPrivateField<T>(CopperScreenRuntime runtime, string fieldName)
	{
		var field = typeof(CopperScreenRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsType<T>(field.GetValue(runtime));
	}

	private sealed class FakeAudioOutput : ICopperScreenAudioOutput
	{
		private int _queued;

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
