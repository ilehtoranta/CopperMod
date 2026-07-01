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
	public void JoystickControllerDetectionAcceptsMappedGamepadProfiles()
	{
		var info = Info(
			ControllerMappingSource.SdlGameControllerDb,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.StandardGamepad, ControllerProfileKind.ExtendedGamepad });

		Assert.True(CopperScreenGamepadInput.IsJoystickController(info));
	}

	[Fact]
	public void JoystickControllerDetectionRejectsUnknownRawHidDevices()
	{
		var rawOnly = Info(
			ControllerMappingSource.None,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.RawInput });
		var diagnosticWithGamepadShape = Info(
			ControllerMappingSource.None,
			new HashSet<ControllerProfileKind>
			{
				ControllerProfileKind.StandardGamepad,
				ControllerProfileKind.ExtendedGamepad,
				ControllerProfileKind.RawInput
			});
		var unknownFallback = Info(
			ControllerMappingSource.Fallback,
			new HashSet<ControllerProfileKind>
			{
				ControllerProfileKind.StandardGamepad,
				ControllerProfileKind.ExtendedGamepad,
				ControllerProfileKind.RawInput
			},
			displayName: "Unknown HID controller");

		Assert.False(CopperScreenGamepadInput.IsJoystickController(rawOnly));
		Assert.False(CopperScreenGamepadInput.IsJoystickController(diagnosticWithGamepadShape));
		Assert.False(CopperScreenGamepadInput.IsJoystickController(unknownFallback));
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

	[Fact]
	public void GamepadAssignmentUsesDpadLeftForPortOne()
	{
		var snapshot = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.DPadLeft] = ControllerElementValue.Button(true)
		});

		var assigned = CopperScreenGamepadInput.TryGetAssignmentPort(snapshot, out var port);

		Assert.True(assigned);
		Assert.Equal(1, port);
	}

	[Fact]
	public void GamepadAssignmentUsesDpadRightForPortTwo()
	{
		var snapshot = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.DPadRight] = ControllerElementValue.Button(true)
		});

		var assigned = CopperScreenGamepadInput.TryGetAssignmentPort(snapshot, out var port);

		Assert.True(assigned);
		Assert.Equal(2, port);
	}

	[Fact]
	public void GamepadAssignmentUsesLeftStickHorizontalAxis()
	{
		var left = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.LeftStickX] = ControllerElementValue.Axis(-0.8)
		});
		var right = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.LeftStickX] = ControllerElementValue.Axis(0.8)
		});

		Assert.True(CopperScreenGamepadInput.TryGetAssignmentPort(left, out var leftPort));
		Assert.True(CopperScreenGamepadInput.TryGetAssignmentPort(right, out var rightPort));
		Assert.Equal(1, leftPort);
		Assert.Equal(2, rightPort);
	}

	[Fact]
	public void GamepadAssignmentIgnoresNeutralVerticalAmbiguousAndDisconnectedInput()
	{
		var neutral = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>());
		var vertical = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.DPadUp] = ControllerElementValue.Button(true),
			[ControllerElement.LeftStickY] = ControllerElementValue.Axis(0.8)
		});
		var ambiguous = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.DPadLeft] = ControllerElementValue.Button(true),
			[ControllerElement.DPadRight] = ControllerElementValue.Button(true)
		});
		var disconnected = Snapshot(new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.DPadLeft] = ControllerElementValue.Button(true)
		}, connected: false);

		Assert.False(CopperScreenGamepadInput.TryGetAssignmentPort(neutral, out _));
		Assert.False(CopperScreenGamepadInput.TryGetAssignmentPort(vertical, out _));
		Assert.False(CopperScreenGamepadInput.TryGetAssignmentPort(ambiguous, out _));
		Assert.False(CopperScreenGamepadInput.TryGetAssignmentPort(disconnected, out _));
	}

	[Fact]
	public void GamepadAssignmentIgnoresUnknownRawHidSnapshots()
	{
		var snapshot = Snapshot(
			new Dictionary<ControllerElement, ControllerElementValue>
			{
				[ControllerElement.DPadLeft] = ControllerElementValue.Button(true)
			},
			mappingSource: ControllerMappingSource.None,
			supportedProfiles: new HashSet<ControllerProfileKind>
			{
				ControllerProfileKind.StandardGamepad,
				ControllerProfileKind.ExtendedGamepad,
				ControllerProfileKind.RawInput
			});
		var unknownFallback = Snapshot(
			new Dictionary<ControllerElement, ControllerElementValue>
			{
				[ControllerElement.DPadRight] = ControllerElementValue.Button(true)
			},
			displayName: "Unknown HID controller",
			mappingSource: ControllerMappingSource.Fallback);

		Assert.False(CopperScreenGamepadInput.TryGetAssignmentPort(snapshot, out _));
		Assert.False(CopperScreenGamepadInput.TryGetAssignmentPort(unknownFallback, out _));
	}

	private static CopperControllerInfo Info(
		ControllerMappingSource mappingSource,
		IReadOnlySet<ControllerProfileKind> supportedProfiles,
		bool connected = true,
		string displayName = "Test HID")
		=> new(
			"hid://pad",
			displayName,
			1,
			2,
			ControllerTransport.Usb,
			connected,
			supportedProfiles,
			mappingSource,
			null,
			null);

	private static CopperControllerSnapshot Snapshot(
		IReadOnlyDictionary<ControllerElement, ControllerElementValue> elements,
		bool connected = true,
		ControllerMappingSource mappingSource = ControllerMappingSource.UserProfile,
		IReadOnlySet<ControllerProfileKind>? supportedProfiles = null,
		string displayName = "Pad")
		=> new(
			"pad",
			DateTimeOffset.UtcNow,
			connected,
			displayName,
			1,
			2,
			ControllerTransport.Usb,
			elements,
			supportedProfiles ?? new HashSet<ControllerProfileKind> { ControllerProfileKind.ExtendedGamepad },
			mappingSource,
			"Test",
			null);
}
