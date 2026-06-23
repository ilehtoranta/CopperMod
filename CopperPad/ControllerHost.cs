using System.Collections.Concurrent;

namespace CopperPad;

public sealed class ControllerHost : IDisposable
{
	private readonly IHidDeviceProvider _provider;
	private readonly ControllerHostOptions _options;
	private readonly object _gate = new();
	private readonly ConcurrentDictionary<string, VirtualXboxControllerState> _states = new();
	private readonly Dictionary<string, ControllerSession> _sessions = new(StringComparer.Ordinal);
	private bool _started;
	private bool _disposed;

	public ControllerHost(ControllerHostOptions? options = null)
		: this(new HidSharpDeviceProvider(), options ?? new ControllerHostOptions())
	{
	}

	internal ControllerHost(IHidDeviceProvider provider, ControllerHostOptions options)
	{
		_provider = provider;
		_options = options;
		_provider.Changed += OnProviderChanged;
	}

	public event EventHandler<ControllersChangedEventArgs>? ControllersChanged;
	public event EventHandler<ControllerStateChangedEventArgs>? ControllerStateChanged;

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
			RescanLocked();
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
			foreach (var session in _sessions.Values)
			{
				session.Dispose();
			}

			_sessions.Clear();
			_states.Clear();
			RaiseChangedLocked();
		}
	}

	public IReadOnlyList<ControllerInfo> GetControllers()
	{
		lock (_gate)
		{
			return _sessions.Values.Select(session => session.Info).OrderBy(info => info.ProductName, StringComparer.Ordinal).ToArray();
		}
	}

	public bool TryGetState(string controllerId, out VirtualXboxControllerState state)
		=> _states.TryGetValue(controllerId, out state!);

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
				RescanLocked();
			}
		}
	}

	private void RescanLocked()
	{
		var devices = _provider.GetDevices()
			.Where(device => ControllerMapperFactory.IsCandidate(device, _options.Profiles))
			.GroupBy(device => device.Id, StringComparer.Ordinal)
			.Select(group => group.First())
			.ToArray();
		var presentIds = devices.Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var stale in _sessions.Keys.Where(id => !presentIds.Contains(id)).ToArray())
		{
			_sessions[stale].Dispose();
			_sessions.Remove(stale);
			_states.TryRemove(stale, out _);
		}

		foreach (var device in devices)
		{
			if (_sessions.ContainsKey(device.Id))
			{
				continue;
			}

			var mapper = ControllerMapperFactory.Create(device, _options.Profiles);
			var session = new ControllerSession(device, mapper, _provider, _options.ReadTimeout, UpdateState);
			_sessions.Add(device.Id, session);
			_states[device.Id] = VirtualXboxControllerState.Disconnected(session.Info, DateTimeOffset.UtcNow);
			session.Start();
		}

		RaiseChangedLocked();
	}

	private void UpdateState(VirtualXboxControllerState state)
	{
		_states[state.ControllerId] = state;
		ControllerStateChanged?.Invoke(this, new ControllerStateChangedEventArgs(state));
	}

	private void RaiseChangedLocked()
	{
		var controllers = _sessions.Values.Select(session => session.Info).OrderBy(info => info.ProductName, StringComparer.Ordinal).ToArray();
		ControllersChanged?.Invoke(this, new ControllersChangedEventArgs(controllers));
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(ControllerHost));
		}
	}

	private sealed class ControllerSession : IDisposable
	{
		private readonly HidDeviceDescriptor _device;
		private readonly IControllerMapper _mapper;
		private readonly IHidDeviceProvider _provider;
		private readonly TimeSpan _readTimeout;
		private readonly Action<VirtualXboxControllerState> _publish;
		private readonly CancellationTokenSource _cancellation = new();
		private Task? _task;

		public ControllerSession(
			HidDeviceDescriptor device,
			IControllerMapper mapper,
			IHidDeviceProvider provider,
			TimeSpan readTimeout,
			Action<VirtualXboxControllerState> publish)
		{
			_device = device;
			_mapper = mapper;
			_provider = provider;
			_readTimeout = readTimeout;
			_publish = publish;
			Info = new ControllerInfo(
				device.Id,
				device.ProductName,
				device.VendorId,
				device.ProductId,
				device.Transport,
				true,
				device.Diagnostic);
		}

		public ControllerInfo Info { get; private set; }

		public void Start()
			=> _task = Task.Run(ReadLoopAsync);

		public void Dispose()
		{
			_cancellation.Cancel();
			try
			{
				_task?.Wait(TimeSpan.FromSeconds(1));
			}
			catch (AggregateException)
			{
			}
			catch (OperationCanceledException)
			{
			}

			_cancellation.Dispose();
		}

		private async Task ReadLoopAsync()
		{
			try
			{
				using var stream = _provider.Open(_device, _readTimeout);
				var buffer = new byte[Math.Max(1, stream.MaxInputReportLength)];
				while (!_cancellation.IsCancellationRequested)
				{
					var read = await stream.ReadAsync(buffer, _cancellation.Token).ConfigureAwait(false);
					if (read <= 0)
					{
						continue;
					}

					var snapshot = new byte[read];
					Array.Copy(buffer, snapshot, read);
					var input = new RawControllerInput(_device, snapshot, read, DateTimeOffset.UtcNow);
					_publish(_mapper.Map(input));
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or NotSupportedException)
			{
				var diagnostic = "HID read failed: " + ex.Message;
				Info = Info with { IsConnected = false, Diagnostic = diagnostic };
				_publish(VirtualXboxControllerState.Disconnected(Info, DateTimeOffset.UtcNow));
			}
		}
	}
}
