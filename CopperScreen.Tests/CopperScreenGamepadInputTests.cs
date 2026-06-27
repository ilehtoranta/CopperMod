using CopperPad;

namespace CopperScreen.Tests;

public sealed class CopperScreenGamepadInputTests
{
	[Fact]
	public void GamepadSnapshotMapsDpadFaceButtonsAndTriggersToJoystickActions()
	{
		var snapshot = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.DPadUp] = ControllerElementValue.Button(true),
			[ControllerElement.DPadLeft] = ControllerElementValue.Button(true),
			[ControllerElement.South] = ControllerElementValue.Button(true),
			[ControllerElement.LeftTrigger] = ControllerElementValue.Trigger(0.75)
		});

		var actions = CopperScreenGamepadInput.GetActions(snapshot);

		Assert.True(actions.HasFlag(CopperScreenJoystickActions.Up));
		Assert.True(actions.HasFlag(CopperScreenJoystickActions.Left));
		Assert.True(actions.HasFlag(CopperScreenJoystickActions.Fire));
		Assert.True(actions.HasFlag(CopperScreenJoystickActions.SecondFire));
		Assert.False(actions.HasFlag(CopperScreenJoystickActions.Down));
		Assert.False(actions.HasFlag(CopperScreenJoystickActions.Right));
	}

	[Fact]
	public void GamepadSnapshotMapsLeftStickToJoystickDirections()
	{
		var snapshot = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.LeftStickX] = ControllerElementValue.Axis(0.8),
			[ControllerElement.LeftStickY] = ControllerElementValue.Axis(-0.8)
		});

		var actions = CopperScreenGamepadInput.GetActions(snapshot);

		Assert.True(actions.HasFlag(CopperScreenJoystickActions.Right));
		Assert.True(actions.HasFlag(CopperScreenJoystickActions.Down));
		Assert.False(actions.HasFlag(CopperScreenJoystickActions.Left));
		Assert.False(actions.HasFlag(CopperScreenJoystickActions.Up));
	}

	[Fact]
	public void GamepadProfileUsesStableCopperPadControllerIdentity()
	{
		var info = new CopperControllerInfo(
			"hid://pad-1",
			"Esperanza EG102",
			0x0079,
			0x0006,
			ControllerTransport.Usb,
			true,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.ExtendedGamepad },
			ControllerMappingSource.SdlGameControllerDb,
			"PC Controller",
			null);

		var profile = CopperScreenGamepadInput.CreateProfile(info);

		Assert.Equal(CopperScreenControllerKind.Gamepad, profile.Kind);
		Assert.Equal("Esperanza EG102", profile.DisplayName);
		Assert.Equal(CopperScreenGamepadInput.CreateProfileId(info.Id), profile.Id);
	}

	[Fact]
	public void GamepadProfileLookupFindsControllerWithoutLinq()
	{
		var profileIds = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["hid://keyboard"] = "keyboard",
			["hid://pad-1"] = "gamepad-a1b2c3d4"
		};

		var found = CopperScreenGamepadInput.TryFindControllerIdForProfile(
			"GAMEPAD-A1B2C3D4",
			profileIds,
			out var controllerId);

		Assert.True(found);
		Assert.Equal("hid://pad-1", controllerId);
	}

	private static CopperControllerSnapshot Snapshot(IReadOnlyDictionary<ControllerElement, ControllerElementValue> elements)
		=> new(
			"pad",
			DateTimeOffset.UtcNow,
			true,
			"Pad",
			1,
			2,
			ControllerTransport.Usb,
			elements,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.ExtendedGamepad },
			ControllerMappingSource.UserProfile,
			"Test",
			null);
}
