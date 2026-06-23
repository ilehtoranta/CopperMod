using HidSharp;
using HidSharp.Reports;

namespace CopperPad;

internal sealed class HidSharpDeviceProvider : IHidDeviceProvider
{
	private readonly DeviceList _deviceList;

	public HidSharpDeviceProvider()
	{
		_deviceList = DeviceList.Local;
		_deviceList.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? Changed;

	public IReadOnlyList<HidDeviceDescriptor> GetDevices()
	{
		var devices = new List<HidDeviceDescriptor>();
		foreach (var device in _deviceList.GetHidDevices())
		{
			devices.Add(CreateDescriptor(device));
		}

		return devices;
	}

	public IHidInputStream Open(HidDeviceDescriptor device, TimeSpan readTimeout)
	{
		var hidDevice = _deviceList.GetHidDevices(device.VendorId, device.ProductId)
			.FirstOrDefault(candidate => string.Equals(candidate.DevicePath, device.Id, StringComparison.Ordinal));
		if (hidDevice == null)
		{
			throw new IOException("The HID device is no longer available.");
		}

		return new HidSharpInputStream(hidDevice.Open(), device.MaxInputReportLength, readTimeout);
	}

	private static HidDeviceDescriptor CreateDescriptor(HidDevice device)
	{
		var productName = SafeString(device.GetProductName, fallback: null, "Unknown HID controller");
		var reportDescriptor = Array.Empty<byte>();
		var diagnostic = default(string);
		var isGameController = false;
		var reportsUseId = false;
		try
		{
			reportDescriptor = device.GetRawReportDescriptor();
			var parsed = new ReportDescriptor(reportDescriptor);
			reportsUseId = parsed.ReportsUseID;
			isGameController = parsed.DeviceItems
				.SelectMany(item => item.Reports)
				.SelectMany(report => report.GetAllUsages())
				.Any(IsGameControllerUsage);
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
		{
			diagnostic = "Unable to parse HID report descriptor: " + ex.Message;
		}

		return new HidDeviceDescriptor(
			device.DevicePath,
			productName,
			device.VendorID,
			device.ProductID,
			GuessTransport(device.DevicePath, productName),
			Math.Max(1, device.GetMaxInputReportLength()),
			reportDescriptor,
			isGameController,
			reportsUseId,
			diagnostic);
	}

	private static bool IsGameControllerUsage(uint usage)
		=> usage == (uint)Usage.GenericDesktopGamepad ||
			usage == (uint)Usage.GenericDesktopJoystick ||
			usage == (uint)Usage.GenericDesktopMultiaxisController;

	private static ControllerTransport GuessTransport(string devicePath, string productName)
	{
		var text = devicePath + " " + productName;
		if (text.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) ||
			text.Contains("bth", StringComparison.OrdinalIgnoreCase))
		{
			return ControllerTransport.Bluetooth;
		}

		return devicePath.Contains("usb", StringComparison.OrdinalIgnoreCase)
			? ControllerTransport.Usb
			: ControllerTransport.Unknown;
	}

	private static string SafeString(Func<string> getter, string? fallback, string defaultValue)
	{
		try
		{
			var value = getter();
			return string.IsNullOrWhiteSpace(value) ? fallback ?? defaultValue : value;
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
		{
			return string.IsNullOrWhiteSpace(fallback) ? defaultValue : fallback;
		}
	}

	private sealed class HidSharpInputStream : IHidInputStream
	{
		private readonly HidStream _stream;

		public HidSharpInputStream(HidStream stream, int maxInputReportLength, TimeSpan readTimeout)
		{
			_stream = stream;
			MaxInputReportLength = Math.Max(1, maxInputReportLength);
			if (_stream.CanTimeout)
			{
				_stream.ReadTimeout = Math.Max(1, (int)readTimeout.TotalMilliseconds);
			}
		}

		public int MaxInputReportLength { get; }

		public async ValueTask<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
		{
			try
			{
				return await _stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, MaxInputReportLength), cancellationToken)
					.ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				return 0;
			}
		}

		public void Dispose()
			=> _stream.Dispose();
	}
}
