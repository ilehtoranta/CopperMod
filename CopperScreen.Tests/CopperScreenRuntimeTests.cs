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
