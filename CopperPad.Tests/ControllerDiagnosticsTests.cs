using CopperPad;

public sealed class ControllerDiagnosticsTests
{
	[Fact]
	public void DiagnosticsHost_EnumeratesAllHidDevices()
	{
		var device = ControllerMapperTests.Device(0x1234, 0x5678, "Diagnostic Device", maxInputLength: 12);
		var provider = new FakeHidDeviceProvider();
		provider.SetDevices(device);
		using var host = new ControllerDiagnosticsHost(provider, new ControllerHostOptions());
		IReadOnlyList<HidDeviceInfo>? observed = null;
		host.DevicesChanged += (_, args) => observed = args.Devices;

		host.Start();

		var info = Assert.Single(host.GetDevices());
		Assert.Equal(device.Id, info.Id);
		Assert.Equal(12, info.MaxInputReportLength);
		Assert.Equal(observed, host.GetDevices());
	}

	[Fact]
	public async Task DiagnosticsHost_PublishesRawReportsAndProfileMappedState()
	{
		var device = ControllerMapperTests.Device(0x1111, 0x2222, "Profiled Gamepad", isGameControllerUsage: true);
		var stream = new FakeHidInputStream(device.MaxInputReportLength);
		var provider = new FakeHidDeviceProvider();
		provider.SetDevices(device);
		provider.SetStream(device.Id, stream);
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "diagnostics-test",
					VendorId = 0x1111,
					ProductId = 0x2222,
					Bindings =
					[
						new ControllerBinding
						{
							Target = VirtualXboxControl.A,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 0 }
						},
						new ControllerBinding
						{
							Target = VirtualXboxControl.LeftTrigger,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportByte, Offset = 1 },
							Axis = new AxisCalibration { Minimum = 0, Maximum = 255 }
						}
					]
				}
			]
		};
		using var host = new ControllerDiagnosticsHost(provider, new ControllerHostOptions { Profiles = profiles });
		var raw = new TaskCompletionSource<ControllerRawReportReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
		var state = new TaskCompletionSource<VirtualXboxControllerState>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RawReportReceived += (_, args) => raw.TrySetResult(args);
		host.StateChanged += (_, args) => state.TrySetResult(args.State);

		host.Start();
		host.SelectDevice(device.Id);
		stream.Enqueue([0x01, 255]);

		var rawReport = await WaitAsync(raw.Task);
		var mapped = await WaitAsync(state.Task);

		Assert.Equal(device.Id, rawReport.Device.Id);
		Assert.Equal([0x01, 255], rawReport.Report);
		Assert.True(mapped.A);
		Assert.InRange(mapped.LeftTrigger, 0.99, 1.0);
	}

	private static async Task<T> WaitAsync<T>(Task<T> task)
	{
		var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
		if (completed != task)
		{
			throw new TimeoutException("The diagnostics event was not published.");
		}

		return await task.ConfigureAwait(false);
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
