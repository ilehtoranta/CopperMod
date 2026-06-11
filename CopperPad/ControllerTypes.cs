namespace CopperPad;

public enum ControllerTransport
{
	Unknown = 0,
	Usb,
	Bluetooth
}

public sealed record ControllerInfo(
	string Id,
	string ProductName,
	int VendorId,
	int ProductId,
	ControllerTransport Transport,
	bool IsConnected,
	string? Diagnostic);

public sealed class ControllersChangedEventArgs(IReadOnlyList<ControllerInfo> controllers) : EventArgs
{
	public IReadOnlyList<ControllerInfo> Controllers { get; } = controllers;
}

public sealed record VirtualXboxControllerState(
	string ControllerId,
	DateTimeOffset Timestamp,
	bool IsConnected,
	string ProductName,
	int VendorId,
	int ProductId,
	ControllerTransport Transport,
	bool A,
	bool B,
	bool X,
	bool Y,
	bool LeftShoulder,
	bool RightShoulder,
	bool Back,
	bool Start,
	bool Guide,
	bool LeftStick,
	bool RightStick,
	bool DPadUp,
	bool DPadDown,
	bool DPadLeft,
	bool DPadRight,
	double LeftX,
	double LeftY,
	double RightX,
	double RightY,
	double LeftTrigger,
	double RightTrigger,
	string? Diagnostic)
{
	public static VirtualXboxControllerState Disconnected(ControllerInfo info, DateTimeOffset timestamp)
		=> new(
			info.Id,
			timestamp,
			false,
			info.ProductName,
			info.VendorId,
			info.ProductId,
			info.Transport,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			false,
			0,
			0,
			0,
			0,
			0,
			0,
			info.Diagnostic);
}

public sealed record ControllerHostOptions
{
	public ControllerProfileSet Profiles { get; init; } = ControllerProfileSet.Empty;
	public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
}

public enum VirtualXboxControl
{
	A,
	B,
	X,
	Y,
	LeftShoulder,
	RightShoulder,
	Back,
	Start,
	Guide,
	LeftStick,
	RightStick,
	DPadUp,
	DPadDown,
	DPadLeft,
	DPadRight,
	LeftX,
	LeftY,
	RightX,
	RightY,
	LeftTrigger,
	RightTrigger
}
