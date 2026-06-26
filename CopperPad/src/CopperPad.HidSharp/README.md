# CopperPad.HidSharp

`CopperPad.HidSharp` is the desktop HID provider for the platform-neutral CopperPad core API. It contains HidSharp enumeration/reading, raw report diagnostics, profile-backed mapping, SDL_GameControllerDB lookup, and fallback mappers.

Mapping precedence is:

1. user/app profile JSON
2. provider-native mappings where available
3. bundled SDL_GameControllerDB data
4. built-in fallback mappers
5. raw diagnostics only

The bundled SDL_GameControllerDB snapshot is third-party mapping data under the zlib license. See the package `THIRD-PARTY-NOTICES.md` and `ThirdParty/SDL_GameControllerDB/LICENSE`.

## Quick example

```csharp
using CopperPad;

using var host = new CopperControllerHost(new HidSharpControllerProvider());
host.ControllersChanged += (_, args) =>
{
	foreach (var info in args.Controllers)
	{
		Console.WriteLine($"{info.DisplayName} ({info.MappingSource}: {info.MappingName})");
	}
};

host.Start();
```

Read the latest normalized state from a selected controller:

```csharp
var controller = host.GetControllers().FirstOrDefault();
var snapshot = controller?.GetSnapshot();

if (snapshot?.ExtendedGamepad is { } gamepad)
{
	Console.WriteLine($"Left stick: {gamepad.LeftStickX:0.00}, {gamepad.LeftStickY:0.00}");
	Console.WriteLine($"South/A pressed: {gamepad.South}");
}
```

Pass user/app profiles when an override should take precedence over SDL_GameControllerDB:

```csharp
var provider = new HidSharpControllerProvider(new HidSharpControllerProviderOptions
{
	Profiles = profiles,
	ReadTimeout = TimeSpan.FromMilliseconds(100)
});
```
