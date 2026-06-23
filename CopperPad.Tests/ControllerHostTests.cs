using CopperPad;

public sealed class ControllerHostTests
{
	[Fact]
	public async Task Host_StartsStreamsAndPublishesLatestState()
	{
		var device = ControllerMapperTests.Device(0x1111, 0x2222, "Generic Gamepad", isGameControllerUsage: true);
		var stream = new FakeHidInputStream(device.MaxInputReportLength);
		var provider = new FakeHidDeviceProvider();
		provider.SetDevices(device);
		provider.SetStream(device.Id, stream);
		using var host = new ControllerHost(provider, new ControllerHostOptions());
		var changedCount = 0;
		VirtualXboxControllerState? observedState = null;
		host.ControllersChanged += (_, _) => changedCount++;
		host.ControllerStateChanged += (_, args) => observedState = args.State;

		host.Start();
		stream.Enqueue([255, 128, 128, 128, 0, 255, 0x01, 0]);

		var state = await WaitForStateAsync(host, device.Id);

		Assert.True(state.A);
		Assert.InRange(state.LeftX, 0.99, 1.0);
		Assert.True(state.DPadUp);
		Assert.Equal(1, changedCount);
		Assert.Equal(state, observedState);
	}

	[Fact]
	public void Host_RescansAttachAndDetachEvents()
	{
		var device = ControllerMapperTests.Device(0x1111, 0x2222, "Generic Gamepad", isGameControllerUsage: true);
		var provider = new FakeHidDeviceProvider();
		using var host = new ControllerHost(provider, new ControllerHostOptions());
		host.Start();

		provider.SetDevices(device);
		provider.SetStream(device.Id, new FakeHidInputStream(device.MaxInputReportLength));
		provider.RaiseChanged();

		Assert.Single(host.GetControllers());

		provider.SetDevices();
		provider.RaiseChanged();

		Assert.Empty(host.GetControllers());
		Assert.False(host.TryGetState(device.Id, out _));
	}

	private static async Task<VirtualXboxControllerState> WaitForStateAsync(ControllerHost host, string id)
	{
		for (var i = 0; i < 50; i++)
		{
			if (host.TryGetState(id, out var state) && state.IsConnected)
			{
				return state;
			}

			await Task.Delay(20).ConfigureAwait(false);
		}

		throw new TimeoutException("The fake controller state was not published.");
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
