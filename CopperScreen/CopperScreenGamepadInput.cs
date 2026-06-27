using CopperPad;

namespace CopperScreen;

internal static class CopperScreenGamepadInput
{
	private const double StickThreshold = 0.5;

	internal static string CreateProfileId(string controllerId)
	{
		var hash = unchecked((uint)StringComparer.Ordinal.GetHashCode(controllerId));
		return "gamepad-" + hash.ToString("x8");
	}

	internal static CopperScreenControllerProfile CreateProfile(CopperControllerInfo info)
		=> new(
			CreateProfileId(info.Id),
			string.IsNullOrWhiteSpace(info.DisplayName) ? "Gamepad" : info.DisplayName,
			CopperScreenControllerKind.Gamepad,
			CopperScreenJoystickKeyMap.Empty);

	internal static bool TryFindControllerIdForProfile(
		string profileId,
		IReadOnlyDictionary<string, string> profileIdsByControllerId,
		out string controllerId)
	{
		foreach (var item in profileIdsByControllerId)
		{
			if (string.Equals(item.Value, profileId, StringComparison.OrdinalIgnoreCase))
			{
				controllerId = item.Key;
				return true;
			}
		}

		controllerId = string.Empty;
		return false;
	}

	internal static CopperScreenJoystickActions GetActions(CopperControllerSnapshot snapshot)
	{
		var actions = CopperScreenJoystickActions.None;
		var x = snapshot.GetAxis(ControllerElement.LeftStickX);
		var y = snapshot.GetAxis(ControllerElement.LeftStickY);
		if (snapshot.IsPressed(ControllerElement.DPadUp) || y > StickThreshold)
		{
			actions |= CopperScreenJoystickActions.Up;
		}

		if (snapshot.IsPressed(ControllerElement.DPadDown) || y < -StickThreshold)
		{
			actions |= CopperScreenJoystickActions.Down;
		}

		if (snapshot.IsPressed(ControllerElement.DPadLeft) || x < -StickThreshold)
		{
			actions |= CopperScreenJoystickActions.Left;
		}

		if (snapshot.IsPressed(ControllerElement.DPadRight) || x > StickThreshold)
		{
			actions |= CopperScreenJoystickActions.Right;
		}

		if (snapshot.IsPressed(ControllerElement.South) || snapshot.GetAxis(ControllerElement.RightTrigger) > StickThreshold)
		{
			actions |= CopperScreenJoystickActions.Fire;
		}

		if (snapshot.IsPressed(ControllerElement.East) || snapshot.GetAxis(ControllerElement.LeftTrigger) > StickThreshold)
		{
			actions |= CopperScreenJoystickActions.SecondFire;
		}

		return actions;
	}
}
