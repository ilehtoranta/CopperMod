# CopperMod

CopperMod is a terminal music player for classic tracker and chip music formats.
It started as a portable MED renderer, but is growing into a small playback stack
with reusable C# backends and a Terminal.Gui based player application.

The project focuses on accurate replay behavior rather than format conversion.
Backends render audio in small time slices, making them useful both for the
CopperMod player and for other applications that need tracker playback.

## Current Status

- MED / OctaMED: MMD0-MMD3 parsing and Amiga-style playback work is underway.
- ProTracker MOD: 4-channel ProTracker playback with Amiga-style sample output.
- SID / RSID: native C# C64/SID emulation with cycle-counted register scheduling.
- CopperMod: terminal UI player with NAudio output and optional output shaping.

This is still an accuracy project in progress. Some advanced replay details,
especially for SID analog behavior and difficult RSID tunes, are expected to keep
improving over time.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet build .\CopperMod.sln
dotnet test .\CopperMod.sln
```

## Packages

The reusable playback backends are intended to be published separately:

```powershell
dotnet add package CopperMod.Med
dotnet add package CopperMod.ProTracker
dotnet add package CopperMod.Sid
```

All backend packages depend on `CopperMod.Abstractions`, which contains the
shared module loading and rendering interfaces.

## Run

```powershell
dotnet run --project .\CopperMod -- "path\to\tune.sid"
```

If no file is provided, CopperMod tries to open the default MED test tune when it
is available in the workspace.

## Projects

- `CopperMod` - terminal player application.
- `CopperMod.Abstractions` - shared playback interfaces.
- `CopperMod.Med` - MED / OctaMED backend.
- `CopperMod.ProTracker` - ProTracker MOD backend.
- `CopperMod.Sid` - PSID / RSID backend.
