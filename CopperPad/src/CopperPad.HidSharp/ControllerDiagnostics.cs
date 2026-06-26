/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperPad;

/// <summary>
/// Describes one HID device discovered by diagnostics enumeration.
/// </summary>
/// <param name="Id">Stable provider-specific HID device identifier.</param>
/// <param name="ProductName">Human-readable HID product name.</param>
/// <param name="VendorId">USB/HID vendor identifier.</param>
/// <param name="ProductId">USB/HID product identifier.</param>
/// <param name="Transport">Best-known device transport.</param>
/// <param name="MaxInputReportLength">Maximum input report length reported by the device.</param>
/// <param name="ReportDescriptor">Raw HID report descriptor bytes.</param>
/// <param name="IsGameControllerUsage">Whether the descriptor advertises a game-controller usage.</param>
/// <param name="ReportsUseId">Whether input reports include report IDs.</param>
/// <param name="Diagnostic">Descriptor or enumeration diagnostic message, when available.</param>
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

/// <summary>
/// Event data for HID diagnostics device-list changes.
/// </summary>
/// <param name="devices">Current HID device list.</param>
/// <param name="diagnostic">Enumeration diagnostic message, when available.</param>
public sealed class HidDevicesChangedEventArgs(IReadOnlyList<HidDeviceInfo> devices, string? diagnostic = null) : EventArgs
{
	/// <summary>Gets the current HID device list.</summary>
	public IReadOnlyList<HidDeviceInfo> Devices { get; } = devices;
	/// <summary>Gets an enumeration diagnostic message, when available.</summary>
	public string? Diagnostic { get; } = diagnostic;
}

/// <summary>
/// Event data for a raw HID input report.
/// </summary>
/// <param name="device">The selected HID device.</param>
/// <param name="report">A copy of the raw report bytes.</param>
/// <param name="length">The number of valid bytes in <paramref name="report"/>.</param>
/// <param name="timestamp">The time the report was read.</param>
public sealed class ControllerRawReportReceivedEventArgs(HidDeviceInfo device, byte[] report, int length, DateTimeOffset timestamp) : EventArgs
{
	/// <summary>Gets the selected HID device.</summary>
	public HidDeviceInfo Device { get; } = device;
	/// <summary>Gets a copy of the raw report bytes.</summary>
	public byte[] Report { get; } = report;
	/// <summary>Gets the number of valid bytes in <see cref="Report"/>.</summary>
	public int Length { get; } = length;
	/// <summary>Gets the time the report was read.</summary>
	public DateTimeOffset Timestamp { get; } = timestamp;
}

/// <summary>
/// Hosts HID diagnostics enumeration, selected-device raw reports, and mapped snapshots.
/// </summary>
public sealed class ControllerDiagnosticsHost : IDisposable
{
	private readonly IHidDeviceProvider _provider;
	private readonly TimeSpan _readTimeout;
	private readonly object _gate = new();
	private IReadOnlyList<HidDeviceDescriptor> _descriptors = [];
	private IReadOnlyList<HidDeviceInfo> _devices = [];
	private ControllerProfileSet _profiles;
	private string? _diagnostic;
	private string? _selectedDeviceId;
	private CancellationTokenSource? _readerCancellation;
	private Task? _readerTask;
	private bool _started;
	private bool _disposed;

	/// <summary>
	/// Creates a diagnostics host using the local HidSharp device list.
	/// </summary>
	/// <param name="options">Provider options, or <see langword="null"/> for defaults.</param>
	public ControllerDiagnosticsHost(HidSharpControllerProviderOptions? options = null) : this(new HidSharpDeviceProvider(), options ?? new HidSharpControllerProviderOptions())
	{
	}

	internal ControllerDiagnosticsHost(IHidDeviceProvider provider, HidSharpControllerProviderOptions options)
	{
		_provider = provider;
		_profiles = options.Profiles;
		_readTimeout = options.ReadTimeout;
		_provider.Changed += OnProviderChanged;
	}

	/// <summary>Raised when the HID device list changes.</summary>
	public event EventHandler<HidDevicesChangedEventArgs>? DevicesChanged;
	/// <summary>Raised when a raw input report is read from the selected device.</summary>
	public event EventHandler<ControllerRawReportReceivedEventArgs>? RawReportReceived;
	/// <summary>Raised when a selected-device report is mapped to a CopperPad snapshot.</summary>
	public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	/// <summary>Gets the selected HID device identifier, or <see langword="null"/> when none is selected.</summary>
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

	/// <summary>Starts HID enumeration and selected-device reading.</summary>
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

	/// <summary>Stops HID enumeration and selected-device reading.</summary>
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
			_descriptors = [];
			_devices = [];
			RaiseDevicesChangedLocked();
		}
	}

	/// <summary>
	/// Gets the most recently enumerated HID devices.
	/// </summary>
	/// <returns>The current HID device list.</returns>
	public IReadOnlyList<HidDeviceInfo> GetDevices()
	{
		lock (_gate)
		{
			return _devices;
		}
	}

	/// <summary>
	/// Gets mapping information for a HID device.
	/// </summary>
	/// <param name="deviceId">The HID device identifier.</param>
	/// <returns>The selected mapping, or <see langword="null"/> when the device is unknown.</returns>
	public ControllerMappingInfo? GetMappingInfo(string deviceId)
	{
		lock (_gate)
		{
			var descriptor = _descriptors.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.Ordinal));
			return descriptor == null ? null : ControllerMapperFactory.Describe(descriptor, _profiles);
		}
	}

	/// <summary>
	/// Selects the HID device whose raw reports and mapped snapshots should be read.
	/// </summary>
	/// <param name="deviceId">The HID device identifier, or <see langword="null"/> to clear the selection.</param>
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

	/// <summary>
	/// Updates the user profiles used by mapping diagnostics.
	/// </summary>
	/// <param name="profiles">Profiles to use for subsequent selected-device reads.</param>
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

	/// <summary>Stops readers and releases provider subscriptions.</summary>
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
		try
		{
			_descriptors = _provider.GetDevices()
				.GroupBy(device => device.Id, StringComparer.Ordinal)
				.Select(group => group.First())
				.OrderBy(device => device.ProductName, StringComparer.OrdinalIgnoreCase)
				.ThenBy(device => device.Id, StringComparer.Ordinal)
				.ToArray();
			_diagnostic = null;
		}
		catch (Exception ex) when (IsRecoverableHidException(ex))
		{
			StopReaderLocked();
			_descriptors = [];
			_devices = [];
			_selectedDeviceId = null;
			_diagnostic = "HID scan failed: " + ex.Message;
			RaiseDevicesChangedLocked();
			return;
		}

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
				SnapshotChanged?.Invoke(this, new CopperControllerSnapshotChangedEventArgs(mapper.Map(input)));
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (IsRecoverableHidException(ex))
		{
			var diagnostic = "HID read failed: " + ex.Message;
			SnapshotChanged?.Invoke(
				this,
				new CopperControllerSnapshotChangedEventArgs(
					CopperControllerSnapshotBuilder.Disconnected(device, DateTimeOffset.UtcNow, mapper.MappingInfo, diagnostic)));
		}
	}

	private void RaiseDevicesChangedLocked()
		=> DevicesChanged?.Invoke(this, new HidDevicesChangedEventArgs(_devices, _diagnostic));

	private static bool IsRecoverableHidException(Exception ex)
		=> ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or NotSupportedException;

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(ControllerDiagnosticsHost));
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
