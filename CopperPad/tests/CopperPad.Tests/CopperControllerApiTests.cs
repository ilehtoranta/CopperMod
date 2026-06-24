using CopperPad;

public sealed class CopperControllerApiTests
{
	[Fact]
	public void Snapshot_ExposesCanonicalFaceButtonsAndXboxAliases()
	{
		var snapshot = new CopperControllerSnapshot(
			"pad",
			DateTimeOffset.UtcNow,
			true,
			"Pad",
			1,
			2,
			ControllerTransport.Usb,
			new Dictionary<ControllerElement, ControllerElementValue>
			{
				[ControllerElement.South] = ControllerElementValue.Button(true),
				[ControllerElement.East] = ControllerElementValue.Button(false),
				[ControllerElement.LeftStickX] = ControllerElementValue.Axis(0.5)
			},
			new HashSet<ControllerProfileKind> { ControllerProfileKind.StandardGamepad, ControllerProfileKind.ExtendedGamepad },
			ControllerMappingSource.ProviderNative,
			"Native",
			null);

		Assert.True(snapshot.South);
		Assert.True(snapshot.A);
		Assert.False(snapshot.B);
		Assert.Equal(0.5, snapshot.GetAxis(ControllerElement.LeftStickX), precision: 3);
		Assert.NotNull(snapshot.StandardGamepad);
		Assert.NotNull(snapshot.ExtendedGamepad);
	}

	[Fact]
	public void Host_PublishesProfileAndElementChanges()
	{
		using var provider = new FakeControllerProvider();
		using var host = new CopperControllerHost(provider);
		var info = new CopperControllerInfo(
			"pad",
			"Pad",
			1,
			2,
			ControllerTransport.Usb,
			true,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.RawInput },
			ControllerMappingSource.None,
			null,
			null);
		provider.SetControllers(info);

		host.Start();
		var controller = Assert.Single(host.GetControllers());
		var profileChanges = 0;
		var elementChanges = 0;
		controller.ProfileChanged += (_, _) => profileChanges++;
		controller.ElementChanged += (_, args) =>
		{
			if (args.Element == ControllerElement.South)
			{
				elementChanges++;
			}
		};

		provider.Publish(new CopperControllerSnapshot(
			"pad",
			DateTimeOffset.UtcNow,
			true,
			"Pad",
			1,
			2,
			ControllerTransport.Usb,
			new Dictionary<ControllerElement, ControllerElementValue>
			{
				[ControllerElement.South] = ControllerElementValue.Button(true)
			},
			new HashSet<ControllerProfileKind> { ControllerProfileKind.StandardGamepad },
			ControllerMappingSource.UserProfile,
			"Override",
			null));

		Assert.Equal(1, profileChanges);
		Assert.Equal(1, elementChanges);
		Assert.True(controller.GetSnapshot().A);
	}

	private sealed class FakeControllerProvider : IControllerProvider
	{
		private IReadOnlyList<CopperControllerInfo> _controllers = Array.Empty<CopperControllerInfo>();

		public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
		public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

		public void Start()
			=> ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs(_controllers));

		public void Stop()
		{
		}

		public IReadOnlyList<CopperControllerInfo> GetControllers()
			=> _controllers;

		public bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot)
		{
			snapshot = null!;
			return false;
		}

		public void SetControllers(params CopperControllerInfo[] controllers)
			=> _controllers = controllers;

		public void Publish(CopperControllerSnapshot snapshot)
			=> SnapshotChanged?.Invoke(this, new CopperControllerSnapshotChangedEventArgs(snapshot));

		public void Dispose()
		{
		}
	}
}
