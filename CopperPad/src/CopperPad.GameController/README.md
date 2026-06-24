# CopperPad.GameController

This package is the Apple GameController provider for CopperPad.

The desktop workspace builds the `net10.0` stub so normal Windows CI does not require Apple workloads. Build the real iOS target on macOS with:

```powershell
dotnet build CopperPad/src/CopperPad.GameController -p:EnableIOSProviderBuild=true -f net10.0-ios
```

Manual validation requires iOS hardware or an Apple-supported controller environment.
