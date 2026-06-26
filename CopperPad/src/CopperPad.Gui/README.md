# CopperPad.Gui

`CopperPad.Gui` is the end-user Avalonia desktop app for testing, mapping, and calibrating controllers through CopperPad.

Publish and smoke-test the Windows release artifact with:

```powershell
CopperPad\scripts\SmokeTest-Gui.ps1 -Configuration Release -Runtime win-x64
```

The executable also supports a release smoke mode:

```powershell
CopperPad.Gui.exe --smoke-test
```

Smoke mode opens the normal main-window startup path and exits automatically. A non-zero exit code means startup failed; details are written to the per-user CopperPad crash log.
