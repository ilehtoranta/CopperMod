#if IOS || MACCATALYST
using System.Globalization;
using GameController;

namespace CopperPad;

/// <summary>
/// Apple GameController provider for iOS and Mac Catalyst targets.
/// </summary>
public sealed class GameControllerControllerProvider : IControllerProvider
{
	private readonly Dictionary<string, CopperControllerSnapshot> _snapshots = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	/// <inheritdoc />
	public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	/// <inheritdoc />
	public void Start()
	{
		GCController.Notifications.ObserveDidConnect((_, _) => PublishControllers());
		GCController.Notifications.ObserveDidDisconnect((_, _) => PublishControllers());
		PublishControllers();
	}

	/// <inheritdoc />
	public void Stop()
	{
		_snapshots.Clear();
		PublishControllers();
	}

	/// <inheritdoc />
	public IReadOnlyList<CopperControllerInfo> GetControllers()
		=> GCController.Controllers.Select(ToInfo).ToArray();

	/// <inheritdoc />
	public bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot)
		=> _snapshots.TryGetValue(controllerId, out snapshot!);

	/// <inheritdoc />
	public void Dispose()
		=> Stop();

	private void PublishControllers()
		=> ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs(GetControllers()));

	private static CopperControllerInfo ToInfo(GCController controller)
	{
		var id = controller.VendorName ?? controller.GetHashCode().ToString("X", CultureInfo.InvariantCulture);
		var profiles = controller.ExtendedGamepad != null
			? new HashSet<ControllerProfileKind> { ControllerProfileKind.StandardGamepad, ControllerProfileKind.ExtendedGamepad }
			: new HashSet<ControllerProfileKind> { ControllerProfileKind.StandardGamepad };
		return new CopperControllerInfo(
			id,
			controller.VendorName ?? "GameController",
			0,
			0,
			ControllerTransport.Unknown,
			true,
			profiles,
			ControllerMappingSource.ProviderNative,
			"Apple GameController",
			null);
	}
}
#endif
