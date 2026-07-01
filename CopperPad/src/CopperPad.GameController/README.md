# CopperPad.GameController

This package is the Apple GameController provider for CopperPad.

The desktop workspace builds the `net10.0` stub so normal Windows CI does not require Apple workloads. Build the real iOS target on macOS with:

```powershell
dotnet build CopperPad/src/CopperPad.GameController -p:EnableIOSProviderBuild=true -f net10.0-ios
```

Manual validation requires iOS hardware or an Apple-supported controller environment.

## Quick example

```csharp
using CopperPad;

using var host = new CopperControllerHost(new GameControllerControllerProvider());
host.Start();

foreach (var controller in host.GetControllers())
{
	var snapshot = controller.GetSnapshot();
	Console.WriteLine($"{snapshot.DisplayName}: {snapshot.MappingSource}");
}
```

The `net10.0` desktop build is a stub so shared code can compile in normal desktop CI. Use the `net10.0-ios` target for real Apple GameController input.
