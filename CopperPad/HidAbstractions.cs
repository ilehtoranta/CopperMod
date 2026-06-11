namespace CopperPad;

internal sealed record HidDeviceDescriptor(
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

internal interface IHidDeviceProvider
{
	event EventHandler? Changed;

	IReadOnlyList<HidDeviceDescriptor> GetDevices();

	IHidInputStream Open(HidDeviceDescriptor device, TimeSpan readTimeout);
}

internal interface IHidInputStream : IDisposable
{
	int MaxInputReportLength { get; }

	ValueTask<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken);
}

internal sealed record RawControllerInput(
	HidDeviceDescriptor Device,
	byte[] Report,
	int Length,
	DateTimeOffset Timestamp);
