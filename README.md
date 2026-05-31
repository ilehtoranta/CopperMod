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

## CopperScreen

`CopperScreen` is the Amiga 500 PAL emulator front-end in this workspace. It can
boot ADF and IPF disk images directly, or a ZIP containing exactly one ADF or
IPF image:

```powershell
dotnet run --project .\CopperScreen -- "path\to\disk.adf"
dotnet run --project .\CopperScreen -- "path\to\disk.ipf"
dotnet run --project .\CopperScreen -- "path\to\disk.zip"
```

IPF support is provided by the native `CopperMod.Ipf` decoder. It decodes SPS /
CAPS IPF images into raw Amiga track streams for CopperScreen's floppy path, so
protected or non-standard disks can be tested without converting them to sector
ADF images first. This is still part of emulator bring-up, so tricky protection
schemes may continue to expose missing floppy-controller or disk-DMA behavior.

By default CopperScreen starts from the `expanded-copperstart` profile config in
`CopperScreen\Profiles`. Profiles are JSON files that describe the machine
memory layout and Kickstart source. Select a bundled profile by id, or pass a
path to a custom profile JSON file with `--profile`.

A local real Kickstart 1.3 ROM can also be used for testing. Place it at
`CopperScreen\ROM\Kickstart_13.rom`, or pass a path with `--kickstart-rom`.
ROM files are local-only and should not be committed.

Available startup profiles:

| Profile config | Memory | Kickstart source |
| --- | --- | --- |
| `vanilla-copperstart` | 512 KB chip RAM | CopperStart 1.3 |
| `expanded-copperstart` | 512 KB chip RAM + 512 KB pseudo-fast at `$C00000` | CopperStart 1.3 |
| `vanilla-kickstart13` | 512 KB chip RAM | real Kickstart 1.3 ROM |
| `expanded-kickstart13` | 512 KB chip RAM + 512 KB pseudo-fast at `$C00000` | real Kickstart 1.3 ROM |

Examples:

```powershell
dotnet run --project .\CopperScreen -- --profile vanilla-copperstart "path\to\disk.adf"
dotnet run --project .\CopperScreen -- --profile expanded-kickstart13 --kickstart-rom ".\CopperScreen\ROM\Kickstart_13.rom" "path\to\disk.zip"
dotnet run --project .\CopperScreen -- --profile ".\CopperScreen\Profiles\expanded-copperstart.json" "path\to\disk.adf"
```

The ROM-backed profiles are still an emulator bring-up path; CopperStart remains
the default for day-to-day disk testing.

## Website

The static project website lives in `docs`. GitHub Pages deploys it through
`.github/workflows/pages.yml` on pushes to `main` and manual workflow runs.

## Export

`CopperMod.Tools` renders supported modules to files without opening the player:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.mod" --out tune.wav --seconds 30
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.sid" --out tune.pcm --seconds 30
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.sid" --out tune.mp3 --seconds 30 --mp3-bitrate 192
```

WAV output is 32-bit float. PCM output is raw interleaved little-endian Float32.
MP3 output uses the Windows Media Foundation encoder through NAudio.Wasapi.

## Binary Releases

CopperMod and CopperScreen are released separately. Each release script publishes
a Windows self-contained zip and a portable .NET zip that can be run with
`dotnet CopperMod.dll` or `dotnet CopperScreen.dll`.

```powershell
.\publish-coppermod.ps1 -Version 1.0.0
.\release-coppermod.ps1 -Version 1.0.0

.\publish-copperscreen.ps1 -Version 1.0.0
.\release-copperscreen.ps1 -Version 1.0.0
```

The publish scripts strip `.pdb` and `.xml` files before zipping. CopperMod
releases use tags like `coppermod-v1.0.0`; CopperScreen releases use tags like
`copperscreen-v1.0.0`.

The release scripts use GitHub CLI (`gh`) when it is installed. Without `gh`,
run the release script with `-TagOnly`, then create the GitHub release manually
and upload the generated zip files plus `SHA256SUMS.txt`.

## Projects

- `CopperMod` - terminal player application.
- `CopperMod.Abstractions` - shared playback interfaces.
- `CopperMod.Rendering` - shared format registration and offline rendering helpers.
- `CopperMod.Tools` - offline render/export utility.
- `CopperMod.Med` - MED / OctaMED backend.
- `CopperMod.ProTracker` - ProTracker MOD backend.
- `CopperMod.Sid` - PSID / RSID backend.
- `CopperMod.Amiga` - shared Amiga 500 emulation core.
- `CopperMod.Ipf` - native SPS / CAPS IPF disk image decoder.
- `CopperScreen` - Avalonia Amiga 500 emulator front-end.
