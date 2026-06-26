/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Collections.Concurrent;

namespace CopperPad;

/// <summary>
/// Desktop HID controller provider backed by HidSharp and SDL_GameControllerDB mappings.
/// </summary>
public sealed class HidSharpControllerProvider : IControllerProvider
{
	private readonly IHidDeviceProvider _provider;
	private readonly HidSharpControllerProviderOptions _options;
	private readonly object _gate = new();
	private readonly ConcurrentDictionary<string, CopperControllerSnapshot> _snapshots = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ControllerSession> _sessions = new(StringComparer.Ordinal);
	private bool _started;
	private bool _disposed;

	/// <summary>
	/// Creates a HidSharp controller provider using the local device list.
	/// </summary>
	/// <param name="options">Provider options, or <see langword="null"/> for defaults.</param>
	public HidSharpControllerProvider(HidSharpControllerProviderOptions? options = null)
		: this(new HidSharpDeviceProvider(), options ?? new HidSharpControllerProviderOptions())
	{
	}

	internal HidSharpControllerProvider(IHidDeviceProvider provider, HidSharpControllerProviderOptions options)
	{
		_provider = provider;
		_options = options;
		_provider.Changed += OnProviderChanged;
	}

	/// <inheritdoc />
	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	/// <inheritdoc />
	public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	/// <inheritdoc />
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

	/// <inheritdoc />
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
			_snapshots.Clear();
			RaiseControllersChangedLocked();
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<CopperControllerInfo> GetControllers()
	{
		lock (_gate)
		{
			return _sessions.Values
				.Select(session => session.Info)
				.OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
	}

	/// <inheritdoc />
	public bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot)
		=> _snapshots.TryGetValue(controllerId, out snapshot!);

	/// <inheritdoc />
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
			_snapshots.TryRemove(stale, out _);
		}

		foreach (var device in devices)
		{
			if (_sessions.ContainsKey(device.Id))
			{
				continue;
			}

			var mapper = ControllerMapperFactory.Create(device, _options.Profiles);
			var session = new ControllerSession(device, mapper, _provider, _options.ReadTimeout, PublishSnapshot);
			_sessions.Add(device.Id, session);
			_snapshots[device.Id] = CopperControllerSnapshotBuilder.Disconnected(device, DateTimeOffset.UtcNow, mapper.MappingInfo, device.Diagnostic);
			session.Start();
		}

		RaiseControllersChangedLocked();
	}

	private void PublishSnapshot(CopperControllerSnapshot snapshot)
	{
		_snapshots[snapshot.ControllerId] = snapshot;
		SnapshotChanged?.Invoke(this, new CopperControllerSnapshotChangedEventArgs(snapshot));
	}

	private void RaiseControllersChangedLocked()
		=> ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs(GetControllers()));

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(HidSharpControllerProvider));
	}

	private sealed class ControllerSession : IDisposable
	{
		private readonly HidDeviceDescriptor _device;
		private readonly IControllerMapper _mapper;
		private readonly IHidDeviceProvider _provider;
		private readonly TimeSpan _readTimeout;
		private readonly Action<CopperControllerSnapshot> _publish;
		private readonly CancellationTokenSource _cancellation = new();
		private Task? _task;

		public ControllerSession(
			HidDeviceDescriptor device,
			IControllerMapper mapper,
			IHidDeviceProvider provider,
			TimeSpan readTimeout,
			Action<CopperControllerSnapshot> publish)
		{
			_device = device;
			_mapper = mapper;
			_provider = provider;
			_readTimeout = readTimeout;
			_publish = publish;
			Info = CopperControllerSnapshotBuilder.ToInfo(device, true, mapper.MappingInfo, device.Diagnostic);
		}

		public CopperControllerInfo Info { get; private set; }

		public void Start() => _task = Task.Run(ReadLoopAsync);

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
					var mapped = _mapper.Map(input);
					Info = CopperControllerSnapshotBuilder.ToInfo(_device, true, _mapper.MappingInfo, mapped.Diagnostic);
					_publish(mapped);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or NotSupportedException)
			{
				var diagnostic = "HID read failed: " + ex.Message;
				Info = CopperControllerSnapshotBuilder.ToInfo(_device, false, _mapper.MappingInfo, diagnostic);
				_publish(CopperControllerSnapshotBuilder.Disconnected(_device, DateTimeOffset.UtcNow, _mapper.MappingInfo, diagnostic));
			}
		}
	}
}
