namespace CopperPad;

#pragma warning disable CS0067
/// <summary>
/// Apple GameController provider. The desktop stub exposes no controllers.
/// </summary>
public sealed class GameControllerControllerProvider : IControllerProvider
{
	/// <inheritdoc />
	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	/// <inheritdoc />
	public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	/// <inheritdoc />
	public void Start() => ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs([]));

	/// <inheritdoc />
	public void Stop()
	{
	}

	/// <inheritdoc />
	public IReadOnlyList<CopperControllerInfo> GetControllers() => [];

	/// <inheritdoc />
	public bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot)
	{
		snapshot = null!;
		return false;
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}
}
#pragma warning restore CS0067
