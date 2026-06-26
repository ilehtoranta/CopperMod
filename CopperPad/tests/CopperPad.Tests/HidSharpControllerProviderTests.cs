using CopperPad;

public sealed class HidSharpControllerProviderTests
{
	[Fact]
	public async Task Provider_StartsStreamsAndPublishesLatestSnapshot()
	{
		var device = ControllerMapperTests.Device(0x1111, 0x2222, "Generic Gamepad", isGameControllerUsage: true);
		var stream = new FakeHidInputStream(device.MaxInputReportLength);
		var provider = new FakeHidDeviceProvider();
		provider.SetDevices(device);
		provider.SetStream(device.Id, stream);
		using var controllerProvider = new HidSharpControllerProvider(provider, new HidSharpControllerProviderOptions());
		var changedCount = 0;
		CopperControllerSnapshot? observedSnapshot = null;
		controllerProvider.ControllersChanged += (_, _) => changedCount++;
		controllerProvider.SnapshotChanged += (_, args) => observedSnapshot = args.Snapshot;

		controllerProvider.Start();
		stream.Enqueue([255, 128, 128, 128, 0, 255, 0x01, 0]);

		var snapshot = await WaitForSnapshotAsync(controllerProvider, device.Id);

		Assert.True(snapshot.IsPressed(ControllerElement.A));
		Assert.InRange(snapshot.GetAxis(ControllerElement.LeftStickX), 0.99, 1.0);
		Assert.True(snapshot.IsPressed(ControllerElement.DPadUp));
		Assert.Equal(ControllerMappingSource.Fallback, snapshot.MappingSource);
		Assert.Equal(1, changedCount);
		Assert.Equal(snapshot, observedSnapshot);
	}

	[Fact]
	public void Provider_RescansAttachAndDetachEvents()
	{
		var device = ControllerMapperTests.Device(0x1111, 0x2222, "Generic Gamepad", isGameControllerUsage: true);
		var provider = new FakeHidDeviceProvider();
		using var controllerProvider = new HidSharpControllerProvider(provider, new HidSharpControllerProviderOptions());
		controllerProvider.Start();

		provider.SetDevices(device);
		provider.SetStream(device.Id, new FakeHidInputStream(device.MaxInputReportLength));
		provider.RaiseChanged();

		Assert.Single(controllerProvider.GetControllers());

		provider.SetDevices();
		provider.RaiseChanged();

		Assert.Empty(controllerProvider.GetControllers());
		Assert.False(controllerProvider.TryGetSnapshot(device.Id, out _));
	}

	private static async Task<CopperControllerSnapshot> WaitForSnapshotAsync(HidSharpControllerProvider provider, string id)
	{
		for (var i = 0; i < 50; i++)
		{
			if (provider.TryGetSnapshot(id, out var snapshot) && snapshot.IsConnected)
			{
				return snapshot;
			}

			await Task.Delay(20).ConfigureAwait(false);
		}

		throw new TimeoutException("The fake controller snapshot was not published.");
	}

	private sealed class FakeHidDeviceProvider : IHidDeviceProvider
	{
		private readonly Dictionary<string, FakeHidInputStream> _streams = new(StringComparer.Ordinal);
		private IReadOnlyList<HidDeviceDescriptor> _devices = Array.Empty<HidDeviceDescriptor>();

		public event EventHandler? Changed;

		public IReadOnlyList<HidDeviceDescriptor> GetDevices()
			=> _devices;

		public IHidInputStream Open(HidDeviceDescriptor device, TimeSpan readTimeout)
			=> _streams.TryGetValue(device.Id, out var stream)
				? stream
				: throw new IOException("No fake stream registered.");

		public void SetDevices(params HidDeviceDescriptor[] devices)
			=> _devices = devices;

		public void SetStream(string id, FakeHidInputStream stream)
			=> _streams[id] = stream;

		public void RaiseChanged()
			=> Changed?.Invoke(this, EventArgs.Empty);
	}

	private sealed class FakeHidInputStream(int maxInputReportLength) : IHidInputStream
	{
		private readonly Queue<byte[]> _reports = new();
		private readonly SemaphoreSlim _available = new(0);
		private bool _disposed;

		public int MaxInputReportLength { get; } = maxInputReportLength;

		public void Enqueue(byte[] report)
		{
			lock (_reports)
			{
				_reports.Enqueue(report);
			}

			_available.Release();
		}

		public async ValueTask<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
		{
			await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
			lock (_reports)
			{
				if (_disposed || _reports.Count == 0)
				{
					return 0;
				}

				var report = _reports.Dequeue();
				var length = Math.Min(report.Length, buffer.Length);
				Array.Copy(report, buffer, length);
				return length;
			}
		}

		public void Dispose()
		{
			_disposed = true;
			_available.Release();
			_available.Dispose();
		}
	}
}
