using Avalonia.Input;

namespace CopperScreen;

internal enum CopperScreenControllerKind
{
	None,
	Mouse,
	KeyboardJoystick,
	Gamepad
}

internal sealed record CopperScreenControllerProfile(
	string Id,
	string DisplayName,
	CopperScreenControllerKind Kind,
	CopperScreenJoystickKeyMap JoystickKeys)
{
	public static CopperScreenControllerProfile None { get; } = new("none", "None", CopperScreenControllerKind.None, CopperScreenJoystickKeyMap.Empty);

	public static CopperScreenControllerProfile Mouse { get; } = new("mouse", "Mouse", CopperScreenControllerKind.Mouse, CopperScreenJoystickKeyMap.Empty);

	public static CopperScreenControllerProfile NumpadJoystick { get; } = new("numpad-joystick", "Numpad Joystick", CopperScreenControllerKind.KeyboardJoystick, CopperScreenJoystickKeyMap.Numpad);

	public static CopperScreenControllerProfile WasdJoystick { get; } = new("wasd-joystick", "WASD Joystick", CopperScreenControllerKind.KeyboardJoystick, CopperScreenJoystickKeyMap.Wasd);

	public override string ToString()
		=> DisplayName;
}

internal sealed record CopperScreenInputOptions(
	string Port1ProfileId,
	string Port2ProfileId,
	IReadOnlyList<CopperScreenControllerProfile> ControllerProfiles)
{
	public const int DefaultMousePort = 1;

	public const string Port1Name = "port1";

	public const string Port2Name = "port2";

	public static IReadOnlyList<CopperScreenControllerProfile> DefaultControllerProfiles { get; } =
	[
		CopperScreenControllerProfile.None,
		CopperScreenControllerProfile.Mouse,
		CopperScreenControllerProfile.NumpadJoystick,
		CopperScreenControllerProfile.WasdJoystick
	];

	public static CopperScreenInputOptions Default { get; } = Create(
		CopperScreenControllerProfile.Mouse.Id,
		CopperScreenControllerProfile.NumpadJoystick.Id,
		DefaultControllerProfiles);

	public int MousePort => GetProfileForPort(1).Kind == CopperScreenControllerKind.Mouse ? 1 : 2;

	public int MousePortIndex => MousePort - 1;

	public int JoystickPort => GetProfileForPort(1).Kind == CopperScreenControllerKind.KeyboardJoystick ? 1 : 2;

	public int JoystickPortIndex => JoystickPort - 1;

	public CopperScreenJoystickKeyMap JoystickKeys => GetProfileForPort(JoystickPort).JoystickKeys;

	public static CopperScreenInputOptions Create(
		string? port1ProfileId,
		string? port2ProfileId,
		IEnumerable<CopperScreenControllerProfile>? controllerProfiles = null)
	{
		var profiles = NormalizeProfiles(controllerProfiles);
		var port1 = ResolveProfileId(port1ProfileId, profiles, CopperScreenControllerProfile.Mouse.Id);
		var port2 = ResolveProfileId(port2ProfileId, profiles, CopperScreenControllerProfile.NumpadJoystick.Id);
		return new CopperScreenInputOptions(port1, port2, profiles);
	}

	public static CopperScreenInputOptions Create(int mousePort, CopperScreenJoystickKeyMap? joystickKeys = null)
	{
		if (mousePort is < 1 or > 2)
		{
			throw new InvalidOperationException("input.mousePort must be 1 or 2.");
		}

		var joystick = new CopperScreenControllerProfile(
			CopperScreenControllerProfile.NumpadJoystick.Id,
			CopperScreenControllerProfile.NumpadJoystick.DisplayName,
			CopperScreenControllerKind.KeyboardJoystick,
			joystickKeys ?? CopperScreenJoystickKeyMap.Numpad);
		var profiles = new[]
		{
			CopperScreenControllerProfile.None,
			CopperScreenControllerProfile.Mouse,
			joystick,
			CopperScreenControllerProfile.WasdJoystick
		};
		return Create(
			mousePort == 1 ? CopperScreenControllerProfile.Mouse.Id : joystick.Id,
			mousePort == 1 ? joystick.Id : CopperScreenControllerProfile.Mouse.Id,
			profiles);
	}

	public CopperScreenControllerProfile GetProfileForPort(int port)
	{
		var profileId = port == 1 ? Port1ProfileId : Port2ProfileId;
		return ControllerProfiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase)) ??
			CopperScreenControllerProfile.None;
	}

	public bool IsMousePort(int portIndex)
		=> GetProfileForPort(portIndex + 1).Kind == CopperScreenControllerKind.Mouse;

	public bool TryGetKeyboardJoystickMap(int portIndex, out CopperScreenJoystickKeyMap keyMap)
	{
		var profile = GetProfileForPort(portIndex + 1);
		if (profile.Kind == CopperScreenControllerKind.KeyboardJoystick)
		{
			keyMap = profile.JoystickKeys;
			return true;
		}

		keyMap = CopperScreenJoystickKeyMap.Empty;
		return false;
	}

	public CopperScreenInputOptions WithPortAssignment(int port, string profileId)
		=> port == 1
			? this with { Port1ProfileId = ResolveProfileId(profileId, ControllerProfiles, CopperScreenControllerProfile.None.Id) }
			: this with { Port2ProfileId = ResolveProfileId(profileId, ControllerProfiles, CopperScreenControllerProfile.None.Id) };

	public CopperScreenInputOptions WithControllerProfiles(IEnumerable<CopperScreenControllerProfile> profiles)
		=> Create(Port1ProfileId, Port2ProfileId, profiles);

	private static IReadOnlyList<CopperScreenControllerProfile> NormalizeProfiles(IEnumerable<CopperScreenControllerProfile>? profiles)
	{
		var byId = new Dictionary<string, CopperScreenControllerProfile>(StringComparer.OrdinalIgnoreCase);
		foreach (var profile in DefaultControllerProfiles)
		{
			byId[profile.Id] = profile;
		}

		if (profiles != null)
		{
			foreach (var profile in profiles)
			{
				if (!string.IsNullOrWhiteSpace(profile.Id))
				{
					var id = NormalizeProfileId(profile.Id);
					byId[id] = profile with { Id = id, DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? id : profile.DisplayName.Trim() };
				}
			}
		}

		return byId.Values.OrderBy(profile => profile.Kind).ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string ResolveProfileId(string? profileId, IReadOnlyList<CopperScreenControllerProfile> profiles, string fallback)
	{
		var normalized = NormalizeProfileId(profileId);
		return profiles.Any(profile => string.Equals(profile.Id, normalized, StringComparison.OrdinalIgnoreCase))
			? normalized
			: fallback;
	}

	internal static string NormalizeProfileId(string? profileId)
	{
		if (string.IsNullOrWhiteSpace(profileId))
		{
			return string.Empty;
		}

		return profileId.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
	}
}

internal sealed record CopperScreenJoystickKeyMap(
	string[] Up,
	string[] Down,
	string[] Left,
	string[] Right,
	string[] Fire,
	string[] SecondFire)
{
	public static CopperScreenJoystickKeyMap Empty { get; } = new([], [], [], [], [], []);

	public static CopperScreenJoystickKeyMap Numpad { get; } = new(
		["NumPad8", "NumPad7", "NumPad9"],
		["NumPad2", "NumPad1", "NumPad3"],
		["NumPad4", "NumPad7", "NumPad1"],
		["NumPad6", "NumPad9", "NumPad3"],
		["NumPad5", "NumPadClear"],
		["NumPadDecimal", "Delete"]);

	public static CopperScreenJoystickKeyMap Wasd { get; } = new(
		["W"],
		["S"],
		["A"],
		["D"],
		["Space"],
		["LeftCtrl"]);

	public static CopperScreenJoystickKeyMap Default => Numpad;

	public static CopperScreenJoystickKeyMap Create(
		IEnumerable<string>? up,
		IEnumerable<string>? down,
		IEnumerable<string>? left,
		IEnumerable<string>? right,
		IEnumerable<string>? fire,
		IEnumerable<string>? secondFire)
	{
		return new CopperScreenJoystickKeyMap(
			Normalize(up, Numpad.Up),
			Normalize(down, Numpad.Down),
			Normalize(left, Numpad.Left),
			Normalize(right, Numpad.Right),
			Normalize(fire, Numpad.Fire),
			Normalize(secondFire, Numpad.SecondFire));
	}

	public CopperScreenJoystickActions GetActions(Key key, PhysicalKey physicalKey)
	{
		var actions = CopperScreenJoystickActions.None;
		if (Matches(Up, key, physicalKey))
		{
			actions |= CopperScreenJoystickActions.Up;
		}

		if (Matches(Down, key, physicalKey))
		{
			actions |= CopperScreenJoystickActions.Down;
		}

		if (Matches(Left, key, physicalKey))
		{
			actions |= CopperScreenJoystickActions.Left;
		}

		if (Matches(Right, key, physicalKey))
		{
			actions |= CopperScreenJoystickActions.Right;
		}

		if (Matches(Fire, key, physicalKey))
		{
			actions |= CopperScreenJoystickActions.Fire;
		}

		if (Matches(SecondFire, key, physicalKey))
		{
			actions |= CopperScreenJoystickActions.SecondFire;
		}

		return actions;
	}

	public static bool IsReservedHostKeyName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var normalized = NormalizeKeyName(value);
		return normalized is "F10" or "F11" or "F12" or "NumLock" or "Enter" or "Return" or "NumPadEnter" or
			"Alt+Enter" or "Alt+Return" or "Alt+NumPadEnter" or
			"LeftAlt+Enter" or "LeftAlt+Return" or "RightAlt+Enter" or "RightAlt+Return";
	}

	private static bool Matches(IEnumerable<string> names, Key key, PhysicalKey physicalKey)
	{
		foreach (var name in names)
		{
			var normalized = NormalizeKeyName(name);
			if (physicalKey != PhysicalKey.None &&
				Enum.TryParse<PhysicalKey>(normalized, ignoreCase: true, out var parsedPhysical) &&
				parsedPhysical == physicalKey)
			{
				return true;
			}

			if (Enum.TryParse<Key>(normalized, ignoreCase: true, out var parsedKey) && parsedKey == key)
			{
				return true;
			}
		}

		return false;
	}

	private static string[] Normalize(IEnumerable<string>? values, string[] fallback)
	{
		if (values == null)
		{
			return fallback;
		}

		var normalized = values
			.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			.Select(NormalizeKeyName)
			.Where(value => value.Length > 0 && !IsReservedHostKeyName(value))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return normalized.Length == 0 ? fallback : normalized;
	}

	private static string NormalizeKeyName(string value)
	{
		var trimmed = value.Trim();
		return trimmed.StartsWith("Key.", StringComparison.OrdinalIgnoreCase) ||
			trimmed.StartsWith("PhysicalKey.", StringComparison.OrdinalIgnoreCase)
			? trimmed[(trimmed.IndexOf('.') + 1)..]
			: trimmed;
	}
}

[Flags]
internal enum CopperScreenJoystickActions
{
	None = 0,
	Up = 1 << 0,
	Down = 1 << 1,
	Left = 1 << 2,
	Right = 1 << 3,
	Fire = 1 << 4,
	SecondFire = 1 << 5
}
