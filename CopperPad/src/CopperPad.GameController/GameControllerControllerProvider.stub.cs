namespace CopperPad;

#pragma warning disable CS0067
public sealed class GameControllerControllerProvider : IControllerProvider
{
	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	public void Start() => ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs([]));

	public void Stop()
	{
	}

	public IReadOnlyList<CopperControllerInfo> GetControllers() => [];

	public bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot)
	{
		snapshot = null!;
		return false;
	}

	public void Dispose()
	{
	}
}
#pragma warning restore CS0067
