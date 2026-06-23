namespace CopperPad;

public sealed record HidDeviceInfo(
	string Id,
	string ProductName,
	int VendorId,
	int ProductId,
	ControllerTransport Transport,
	int MaxInputReportLength,
	byte[] ReportDescriptor,
	bool IsGameControllerUsage,
	bool ReportsUseId,
	string? Diagnostic);

public sealed class HidDevicesChangedEventArgs(IReadOnlyList<HidDeviceInfo> devices) : EventArgs
{
	public IReadOnlyList<HidDeviceInfo> Devices { get; } = devices;
}

public sealed class ControllerRawReportReceivedEventArgs(HidDeviceInfo device, byte[] report, int length, DateTimeOffset timestamp) : EventArgs
{
	public HidDeviceInfo Device { get; } = device;
	public byte[] Report { get; } = report;
	public int Length { get; } = length;
	public DateTimeOffset Timestamp { get; } = timestamp;
}

public sealed class ControllerStateChangedEventArgs(VirtualXboxControllerState state) : EventArgs
{
	public VirtualXboxControllerState State { get; } = state;
}

public sealed class ControllerDiagnosticsHost : IDisposable
{
	private readonly IHidDeviceProvider _provider;
	private readonly TimeSpan _readTimeout;
	private readonly object _gate = new();
	private IReadOnlyList<HidDeviceDescriptor> _descriptors = Array.Empty<HidDeviceDescriptor>();
	private IReadOnlyList<HidDeviceInfo> _devices = Array.Empty<HidDeviceInfo>();
	private ControllerProfileSet _profiles;
	private string? _selectedDeviceId;
	private CancellationTokenSource? _readerCancellation;
	private Task? _readerTask;
	private bool _started;
	private bool _disposed;

	public ControllerDiagnosticsHost(ControllerHostOptions? options = null)
		: this(new HidSharpDeviceProvider(), options ?? new ControllerHostOptions())
	{
	}

	internal ControllerDiagnosticsHost(IHidDeviceProvider provider, ControllerHostOptions options)
	{
		_provider = provider;
		_profiles = options.Profiles;
		_readTimeout = options.ReadTimeout;
		_provider.Changed += OnProviderChanged;
	}

	public event EventHandler<HidDevicesChangedEventArgs>? DevicesChanged;
	public event EventHandler<ControllerRawReportReceivedEventArgs>? RawReportReceived;
	public event EventHandler<ControllerStateChangedEventArgs>? StateChanged;

	public string? SelectedDeviceId
	{
		get
		{
			lock (_gate)
			{
				return _selectedDeviceId;
			}
		}
	}

	public void Start()
	{
		ThrowIfDisposed();
		lock (_gate)
		{
			if (_started)
			{
				return;
			}

			_started = true;
			RescanLocked(restartReader: true);
		}
	}

	public void Stop()
	{
		lock (_gate)
		{
			if (!_started)
			{
				return;
			}

			_started = false;
			StopReaderLocked();
			_descriptors = Array.Empty<HidDeviceDescriptor>();
			_devices = Array.Empty<HidDeviceInfo>();
			RaiseDevicesChangedLocked();
		}
	}

	public IReadOnlyList<HidDeviceInfo> GetDevices()
	{
		lock (_gate)
		{
			return _devices;
		}
	}

	public void SelectDevice(string? deviceId)
	{
		ThrowIfDisposed();
		lock (_gate)
		{
			if (string.Equals(_selectedDeviceId, deviceId, StringComparison.Ordinal))
			{
				return;
			}

			_selectedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
			if (_started)
			{
				StartSelectedReaderLocked();
			}
		}
	}

	public void UpdateProfiles(ControllerProfileSet profiles)
	{
		ThrowIfDisposed();
		lock (_gate)
		{
			_profiles = profiles;
			if (_started)
			{
				StartSelectedReaderLocked();
			}
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_provider.Changed -= OnProviderChanged;
		Stop();
	}

	private void OnProviderChanged(object? sender, EventArgs args)
	{
		lock (_gate)
		{
			if (_started)
			{
				RescanLocked(restartReader: true);
			}
		}
	}

	private void RescanLocked(bool restartReader)
	{
		_descriptors = _provider.GetDevices()
			.GroupBy(device => device.Id, StringComparer.Ordinal)
			.Select(group => group.First())
			.OrderBy(device => device.ProductName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(device => device.Id, StringComparer.Ordinal)
			.ToArray();
		_devices = _descriptors.Select(ToInfo).ToArray();
		if (_selectedDeviceId != null && !_descriptors.Any(device => string.Equals(device.Id, _selectedDeviceId, StringComparison.Ordinal)))
		{
			_selectedDeviceId = null;
		}

		if (restartReader)
		{
			StartSelectedReaderLocked();
		}

		RaiseDevicesChangedLocked();
	}

	private void StartSelectedReaderLocked()
	{
		StopReaderLocked();
		if (_selectedDeviceId == null)
		{
			return;
		}

		var device = _descriptors.FirstOrDefault(candidate => string.Equals(candidate.Id, _selectedDeviceId, StringComparison.Ordinal));
		if (device == null)
		{
			return;
		}

		var mapper = ControllerMapperFactory.Create(device, _profiles);
		var cancellation = new CancellationTokenSource();
		_readerCancellation = cancellation;
		_readerTask = Task.Run(() => ReadLoopAsync(device, mapper, cancellation.Token));
	}

	private void StopReaderLocked()
	{
		var cancellation = _readerCancellation;
		var task = _readerTask;
		_readerCancellation = null;
		_readerTask = null;
		if (cancellation == null)
		{
			return;
		}

		cancellation.Cancel();
		try
		{
			task?.Wait(TimeSpan.FromSeconds(1));
		}
		catch (AggregateException)
		{
		}
		catch (OperationCanceledException)
		{
		}

		cancellation.Dispose();
	}

	private async Task ReadLoopAsync(HidDeviceDescriptor device, IControllerMapper mapper, CancellationToken cancellationToken)
	{
		var info = ToInfo(device);
		try
		{
			using var stream = _provider.Open(device, _readTimeout);
			var buffer = new byte[Math.Max(1, stream.MaxInputReportLength)];
			while (!cancellationToken.IsCancellationRequested)
			{
				var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
				if (read <= 0)
				{
					continue;
				}

				var snapshot = new byte[read];
				Array.Copy(buffer, snapshot, read);
				var timestamp = DateTimeOffset.UtcNow;
				RawReportReceived?.Invoke(this, new ControllerRawReportReceivedEventArgs(info, snapshot, read, timestamp));
				var input = new RawControllerInput(device, snapshot, read, timestamp);
				StateChanged?.Invoke(this, new ControllerStateChangedEventArgs(mapper.Map(input)));
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
		{
			var failed = info with { Diagnostic = "HID read failed: " + ex.Message };
			var controller = new ControllerInfo(
				failed.Id,
				failed.ProductName,
				failed.VendorId,
				failed.ProductId,
				failed.Transport,
				false,
				failed.Diagnostic);
			StateChanged?.Invoke(this, new ControllerStateChangedEventArgs(VirtualXboxControllerState.Disconnected(controller, DateTimeOffset.UtcNow)));
		}
	}

	private void RaiseDevicesChangedLocked()
		=> DevicesChanged?.Invoke(this, new HidDevicesChangedEventArgs(_devices));

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(ControllerDiagnosticsHost));
		}
	}

	internal static HidDeviceInfo ToInfo(HidDeviceDescriptor descriptor)
		=> new(
			descriptor.Id,
			descriptor.ProductName,
			descriptor.VendorId,
			descriptor.ProductId,
			descriptor.Transport,
			descriptor.MaxInputReportLength,
			descriptor.ReportDescriptor.ToArray(),
			descriptor.IsGameControllerUsage,
			descriptor.ReportsUseId,
			descriptor.Diagnostic);
}
