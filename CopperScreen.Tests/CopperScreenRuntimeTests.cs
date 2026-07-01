using System.Reflection;
using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenRuntimeTests
{
	[Fact]
	public void RuntimePresentationBuffersUseHighResolutionDimensions()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk());
		var lastSeenFrameNumber = 0L;

		using var lease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber, force: true);

		Assert.NotNull(lease);
		Assert.Equal(AmigaConstants.PalHighResWidth, runtime.Width);
		Assert.Equal(AmigaConstants.PalHighResHeight, runtime.Height);
		Assert.Equal(runtime.Width * runtime.Height, lease.Framebuffer.Length);
	}

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
			return audio.QueuedBufferCount >= 8;
		}, TimeSpan.FromSeconds(1)));
		Assert.InRange(audio.SubmitCount, 8, 16);
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

	[Theory]
	[InlineData(".adz")]
	[InlineData(".dms")]
	public void LoadedCompressedDiskDefaultsWriteProtectedAndReadOnly(string extension)
	{
		using var temp = new TempDiskFile(extension);
		var adf = new byte[AmigaDiskImage.StandardAdfSize];
		adf[0] = (byte)'D';
		adf[1] = (byte)'O';
		adf[2] = (byte)'S';
		File.WriteAllBytes(temp.Path, extension.Equals(".adz", StringComparison.OrdinalIgnoreCase)
			? Gzip(adf)
			: CreateDms(adf));
		var loaded = AmigaDiskImage.Load(temp.Path);
		var emulator = CopperScreenEmulator.CreateWithLoadedDisk([temp.Path], AppContext.BaseDirectory, loaded);
		emulator.RenderNextFrame();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator, new FakeAudioOutput());
		var state = runtime.CurrentState;

		Assert.False(loaded.CanWriteTracks);
		Assert.True(state.Drives[0].HasDisk);
		Assert.True(state.Drives[0].WriteProtected);
		Assert.EndsWith(extension, state.Drives[0].DiskName, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void LoadedExtendedAdfDefaultsWriteProtectedReadOnlyAndPreserved()
	{
		using var temp = new TempDiskFile(".adf");
		var adf = new byte[AmigaDiskImage.StandardAdfSize];
		adf[0] = (byte)'D';
		adf[1] = (byte)'O';
		adf[2] = (byte)'S';
		File.WriteAllBytes(temp.Path, CreateExtendedAdf(adf, [0x44, 0x89, 0xA0, 0x00], rawBitLength: 20));
		var loaded = AmigaDiskImage.Load(temp.Path);
		var emulator = CopperScreenEmulator.CreateWithLoadedDisk([temp.Path], AppContext.BaseDirectory, loaded);
		emulator.RenderNextFrame();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator, new FakeAudioOutput());
		var state = runtime.CurrentState;

		Assert.False(loaded.CanWriteTracks);
		Assert.True(loaded.HasPreservedTrackData);
		Assert.True(state.Drives[0].HasDisk);
		Assert.True(state.Drives[0].WriteProtected);
		Assert.EndsWith(".adf", state.Drives[0].DiskName, StringComparison.OrdinalIgnoreCase);
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
	public void RuntimeRendersFramesDirectlyIntoQueuedPresentationBuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);
		const int sentinel = unchecked((int)0x55667788);
		Array.Fill(emulator.Framebuffer, sentinel);

		runtime.Start();

		CopperScreenFrameLease? lease = null;
		try
		{
			Assert.True(SpinWait.SpinUntil(() =>
			{
				lease = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
				return lease != null;
			}, TimeSpan.FromSeconds(1)));

			Assert.NotNull(lease);
			Assert.NotEqual(sentinel, lease.Framebuffer[0]);
			Assert.Equal(0, lease.State.LastPublishCopyMilliseconds);
			Assert.True(lease.State.LastPresentationBufferReserveMilliseconds >= 0);
			Assert.True(lease.State.LastDisplayFrameMilliseconds >= 0);
		}
		finally
		{
			lease?.Dispose();
		}
	}

	[Fact]
	public void FullPresentationQueueReplacesOldestQueuedFrameWithNewestFrame()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);

		PublishFrame(emulator, runtime, unchecked((int)0xFF010203));
		PublishFrame(emulator, runtime, unchecked((int)0xFF040506));
		PublishFrame(emulator, runtime, unchecked((int)0xFF070809));

		Assert.Equal(2, runtime.CurrentState.PresentationQueueDepth);
		Assert.True(runtime.CurrentState.PresentationQueueFullThrottleCount > 0);
		using var first = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(first);
		Assert.Equal(unchecked((int)0xFF040506), first.Framebuffer[0]);
		using var second = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(second);
		Assert.Equal(unchecked((int)0xFF070809), second.Framebuffer[0]);
	}

	[Fact]
	public void HealthyAudioPublishingPreservesQueuedPresentationFrameOrder()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var audio = new FakeAudioOutput(initialQueued: 2);
		using var runtime = CopperScreenRuntime.CreateForTests(emulator, audio);
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);

		PublishFrame(emulator, runtime, unchecked((int)0xFF010203), queuedAudioBuffers: 2);
		PublishFrame(emulator, runtime, unchecked((int)0xFF040506), queuedAudioBuffers: 2);

		using var first = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(first);
		Assert.Equal(unchecked((int)0xFF010203), first.Framebuffer[0]);
		using var second = runtime.TryAcquireNextPresentationFrame(ref lastSeenFrameNumber);
		Assert.NotNull(second);
		Assert.Equal(unchecked((int)0xFF040506), second.Framebuffer[0]);
		Assert.Equal(0, second.State.PresentationSkippedFrames);
		Assert.Equal(0, second.State.DroppedFrames);
	}

	[Fact]
	public void MouseInputCommandsDoNotPublishPresentationFrames()
	{
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk());
		var lastSeenFrameNumber = 0L;
		DrainPresentationFrames(runtime, ref lastSeenFrameNumber);
		var initialFrameNumber = runtime.CurrentState.FrameNumber;

		runtime.MoveMousePort(8, -4);
		runtime.MoveMousePort(-3, 6);
		runtime.SetMouseButtons(primaryFirePressed: true, secondFirePressed: false);
		runtime.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
		InvokePrivateMethod(runtime, "ProcessCommands");

		Assert.Equal(initialFrameNumber, runtime.CurrentState.FrameNumber);
		Assert.False(runtime.HasPendingPresentationFrames);
		Assert.Equal(0, runtime.CurrentState.DroppedFrames);
	}

	[Fact]
	public void PresentationOptionsCommandUpdatesEmulatorLive()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		using var runtime = CopperScreenRuntime.CreateForTests(emulator);

		runtime.SetPresentationOptions(new CopperScreenPresentationOptions(CopperScreenLacedPresentationMode.CrtFlicker));
		InvokePrivateMethod(runtime, "ProcessCommands");

		Assert.Equal(
			CopperScreenLacedPresentationMode.CrtFlicker,
			GetPrivateField<CopperScreenPresentationOptions>(emulator, "_presentationOptions").LacedMode);
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
	public void HealthyAudioContinuesWhenTargetVideoQueueDepthIsReached()
	{
		using var audio = new FakeAudioOutput(initialQueued: 2);
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);
		Assert.Equal(1, runtime.CurrentState.PresentationQueueDepth);

		runtime.Start();

		Assert.True(SpinWait.SpinUntil(() => audio.SubmitCount > 0 && audio.QueuedBufferCount >= 8, TimeSpan.FromSeconds(1)));
		Assert.InRange(runtime.CurrentState.PresentationQueueDepth, 0, 2);
	}

	[Fact]
	public void CriticalAudioCatchUpCollapsesStaleVideoQueueAfterRefill()
	{
		using var audio = new FakeAudioOutput(initialQueued: 0);
		using var runtime = CopperScreenRuntime.CreateForTests(CopperScreenEmulator.CreateWithoutDisk(), audio);

		runtime.Start();

		Assert.True(SpinWait.SpinUntil(() => audio.QueuedBufferCount >= 8, TimeSpan.FromSeconds(1)));
		Assert.True(audio.SubmitCount >= 8);
		Assert.InRange(runtime.CurrentState.PresentationQueueDepth, 0, 2);
		Assert.True(runtime.CurrentState.PresentationQueueFullThrottleCount > 0);
	}

	private static T GetPrivateField<T>(CopperScreenRuntime runtime, string fieldName)
	{
		var field = typeof(CopperScreenRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsType<T>(field.GetValue(runtime));
	}

	private static T GetPrivateField<T>(CopperScreenEmulator emulator, string fieldName)
	{
		var field = typeof(CopperScreenEmulator).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsType<T>(field.GetValue(emulator));
	}

	private static void InvokePrivateMethod(CopperScreenRuntime runtime, string methodName, params object[] arguments)
	{
		var method = typeof(CopperScreenRuntime).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method.Invoke(runtime, arguments);
	}

	private static void PublishFrame(CopperScreenEmulator emulator, CopperScreenRuntime runtime, int pixel)
		=> PublishFrame(emulator, runtime, pixel, queuedAudioBuffers: 0);

	private static void PublishFrame(CopperScreenEmulator emulator, CopperScreenRuntime runtime, int pixel, int queuedAudioBuffers)
	{
		var publish = typeof(CopperScreenRuntime).GetMethod("PublishCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(publish);
		Array.Fill(emulator.Framebuffer, pixel);
		publish.Invoke(runtime, [1, queuedAudioBuffers]);
	}

	private static byte[] Gzip(byte[] data)
	{
		using var memory = new MemoryStream();
		using (var gzip = new System.IO.Compression.GZipStream(memory, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
		{
			gzip.Write(data);
		}

		return memory.ToArray();
	}

	private static byte[] CreateDms(byte[] adf)
	{
		const int cylinderBytes = AmigaDiskImage.HeadCount * AmigaDiskImage.SectorsPerTrack * AmigaDiskImage.SectorSize;
		var body = new List<byte>();
		for (var cylinder = 0; cylinder < AmigaDiskImage.CylinderCount; cylinder++)
		{
			var cylinderData = adf.AsSpan(cylinder * cylinderBytes, cylinderBytes).ToArray();
			var trackHeader = new byte[20];
			trackHeader[0] = (byte)'T';
			trackHeader[1] = (byte)'R';
			WriteUInt16(trackHeader, 2, cylinder);
			WriteUInt16(trackHeader, 6, cylinderData.Length);
			WriteUInt16(trackHeader, 8, cylinderData.Length);
			WriteUInt16(trackHeader, 10, cylinderData.Length);
			WriteUInt16(trackHeader, 14, Checksum(cylinderData));
			WriteUInt16(trackHeader, 16, Crc(cylinderData));
			WriteUInt16(trackHeader, 18, Crc(trackHeader.AsSpan(0, 18)));
			body.AddRange(trackHeader);
			body.AddRange(cylinderData);
		}

		var header = new byte[56];
		header[0] = (byte)'D';
		header[1] = (byte)'M';
		header[2] = (byte)'S';
		header[3] = (byte)'!';
		WriteUInt16(header, 16, 0);
		WriteUInt16(header, 18, AmigaDiskImage.CylinderCount - 1);
		WriteUInt24(header, 21, body.Count);
		WriteUInt24(header, 25, AmigaDiskImage.StandardAdfSize);
		WriteUInt16(header, 46, 111);
		WriteUInt16(header, 50, 0);
		WriteUInt16(header, 52, 0);
		WriteUInt16(header, 54, Crc(header.AsSpan(4, 50)));
		var image = new byte[header.Length + body.Count];
		header.CopyTo(image, 0);
		body.CopyTo(image, header.Length);
		return image;
	}

	private static byte[] CreateExtendedAdf(byte[] adf, byte[] rawTrack, int rawBitLength)
	{
		const int trackHeaderLength = 12;
		const int headerLength = 12;
		const int trackBytes = AmigaDiskImage.SectorsPerTrack * AmigaDiskImage.SectorSize;
		var tableLength = headerLength + (AmigaDiskImage.TrackCount * trackHeaderLength);
		var image = new byte[tableLength + rawTrack.Length + ((AmigaDiskImage.TrackCount - 1) * trackBytes)];
		"UAE-1ADF"u8.CopyTo(image);
		WriteUInt16(image, 10, AmigaDiskImage.TrackCount);
		var dataOffset = tableLength;
		for (var track = 0; track < AmigaDiskImage.TrackCount; track++)
		{
			var headerOffset = headerLength + (track * trackHeaderLength);
			if (track == 0)
			{
				WriteUInt16(image, headerOffset + 2, 1);
				WriteUInt32(image, headerOffset + 4, rawTrack.Length);
				WriteUInt32(image, headerOffset + 8, rawBitLength);
				rawTrack.CopyTo(image.AsSpan(dataOffset));
				dataOffset += rawTrack.Length;
			}
			else
			{
				WriteUInt16(image, headerOffset + 2, 0);
				WriteUInt32(image, headerOffset + 4, trackBytes);
				WriteUInt32(image, headerOffset + 8, 0);
				adf.AsSpan(track * trackBytes, trackBytes).CopyTo(image.AsSpan(dataOffset));
				dataOffset += trackBytes;
			}
		}

		return image;
	}

	private static ushort Crc(ReadOnlySpan<byte> data)
	{
		var crc = 0;
		foreach (var value in data)
		{
			crc ^= value;
			for (var bit = 0; bit < 8; bit++)
			{
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
			}
		}

		return (ushort)crc;
	}

	private static ushort Checksum(ReadOnlySpan<byte> data)
	{
		var checksum = 0;
		foreach (var value in data)
		{
			checksum = (checksum + value) & 0xFFFF;
		}

		return (ushort)checksum;
	}

	private static void WriteUInt16(Span<byte> data, int offset, int value)
	{
		data[offset] = (byte)(value >> 8);
		data[offset + 1] = (byte)value;
	}

	private static void WriteUInt24(Span<byte> data, int offset, int value)
	{
		data[offset] = (byte)(value >> 16);
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)value;
	}

	private static void WriteUInt32(Span<byte> data, int offset, int value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
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
