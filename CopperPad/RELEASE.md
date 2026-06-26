# CopperPad Release Checklist

## Required Checks

- Run core tests:

```powershell
dotnet test CopperPad\tests\CopperPad.Tests\CopperPad.Tests.csproj -c Release
dotnet test CopperPad\tests\CopperPad.Gui.Tests\CopperPad.Gui.Tests.csproj -c Release
```

- Pack public libraries:

```powershell
dotnet pack CopperPad\src\CopperPad\CopperPad.csproj -c Release -o CopperPad\artifacts\packages
dotnet pack CopperPad\src\CopperPad.HidSharp\CopperPad.HidSharp.csproj -c Release -o CopperPad\artifacts\packages
dotnet pack CopperPad\src\CopperPad.GameController\CopperPad.GameController.csproj -c Release -o CopperPad\artifacts\packages
```

- Publish and smoke-test the end-user GUI:

```powershell
CopperPad\scripts\SmokeTest-Gui.ps1 -Configuration Release -Runtime win-x64
```

- Build CopperScreen against the release candidate:

```powershell
dotnet build CopperScreen\CopperScreen.csproj -c Release
```

## Manual GUI Smoke Tests

- Start CopperPad.Gui with no controller attached; it must remain open and show an empty/clear device state.
- Attach one known controller; it must appear without enabling "Show all HID".
- Enable "Show all HID"; non-game HID devices may appear only in this mode.
- Open the selected controller summary, then test, create/edit profile, save, reload, import, and export.
- Disconnect and reconnect the controller while the GUI is open; the app must show status instead of exiting.
- Check `%APPDATA%\CopperMod\CopperPad\CopperPad.Gui.crash.log` after failures.

## Release Notes

- Mention that `CopperPad.Gui` is a shipped end-user controller tester, mapper, and calibrator.
- Mention the breaking profile-first API.
- Mention that SDL_GameControllerDB data is bundled with `CopperPad.HidSharp` under the zlib license.
- If publishing `CopperPad.GameController`, pack the real `net10.0-ios` target on macOS with the iOS workload.
