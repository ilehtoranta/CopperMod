# CopperMod

CopperMod is a terminal music player for classic tracker and chip music, with
reusable C# playback libraries and a command-line audio exporter.

[![CopperMod.Med](https://img.shields.io/nuget/v/CopperMod.Med?label=CopperMod.Med)](https://www.nuget.org/packages/CopperMod.Med)
[![CopperMod.ProTracker](https://img.shields.io/nuget/v/CopperMod.ProTracker?label=CopperMod.ProTracker)](https://www.nuget.org/packages/CopperMod.ProTracker)
[![CopperMod.Sid](https://img.shields.io/nuget/v/CopperMod.Sid?label=CopperMod.Sid)](https://www.nuget.org/packages/CopperMod.Sid)
[![CopperMod.Cust](https://img.shields.io/nuget/v/CopperMod.Cust?label=CopperMod.Cust)](https://www.nuget.org/packages/CopperMod.Cust)

## Formats

- **MED / OctaMED:** MMD0–MMD3 parsing and Amiga-style playback.
- **ProTracker MOD:** four-channel playback with Amiga-style sample output.
- **AHX0 / AHX1:** playback through the original 68000 replay routine; requires
  a locally supplied, hash-verified AHX 2.3d replay binary.
- **SID / RSID:** C64/SID emulation with cycle-counted register scheduling.
- **Amiga CUST:** custom-player execution in an Amiga/Paula playback sandbox.

Replay accuracy is a work in progress. Advanced effects, difficult RSID tunes
and SID analog behavior remain areas of ongoing development. Music files and
third-party replay binaries are not distributed with the player.

## Build and run

Requires the .NET 10 SDK. The terminal player uses Terminal.Gui and NAudio.

```powershell
dotnet build CopperMod/CopperMod.csproj -c Release
dotnet run --project CopperMod -c Release -- "path/to/tune.sid"
```

Without a filename, the player tries the default MED test tune if it is available
locally.

**Current source-build limitation:** the Cust backend has a known
`KickstartTrapTable` constructor mismatch. The player build is blocked until
that shared API mismatch is repaired.

## Export audio

`CopperMod.Tools` renders supported modules without opening the player:

```powershell
dotnet run --project CopperMod.Tools -- render "path/to/tune.mod" --out tune.wav --seconds 30
dotnet run --project CopperMod.Tools -- render "path/to/tune.sid" --out tune.pcm --seconds 30
dotnet run --project CopperMod.Tools -- render "path/to/tune.sid" --out tune.mp3 --seconds 30 --mp3-bitrate 192
```

WAV output is 32-bit float; raw PCM is interleaved little-endian Float32.
MP3 export uses the Windows Media Foundation encoder. See the
[export tool reference](CopperMod.Tools/README.md) for output shaping,
voice selection, duration detection and other options.

## Reusable libraries

Playback backends render audio in small slices and can be used independently
of the terminal application.

| Component | Purpose |
| --- | --- |
| [CopperMod.Abstractions](https://www.nuget.org/packages/CopperMod.Abstractions) | Module loading and audio-rendering interfaces. |
| [CopperMod.Med](https://www.nuget.org/packages/CopperMod.Med) | MED / OctaMED parser and renderer. |
| [CopperMod.ProTracker](https://www.nuget.org/packages/CopperMod.ProTracker) | ProTracker MOD parser and renderer. |
| CopperMod.Ahx | AHX loading and original 68000 replay integration. |
| [CopperMod.Sid](https://www.nuget.org/packages/CopperMod.Sid) | PSID / RSID parsing and emulation. |
| [CopperMod.Cust](https://www.nuget.org/packages/CopperMod.Cust) | Amiga custom-player and Paula playback sandbox. |
| [Copper68k](Copper68k/README.md) | Motorola 68000-family CPU emulation. |
| [CopperFloat](CopperFloat/README.md) | Deterministic, allocation-free extended 80-bit arithmetic. |
| [Copper6510](Copper6510/README.md) | MOS 6510 CPU emulation. |
| CopperMod.Amiga | Shared Amiga emulation used by the Cust and AHX backends. |
| CopperMod.Rendering | Format registration and offline-rendering helpers. |

For example:

```powershell
dotnet add package CopperMod.Med
dotnet add package CopperMod.ProTracker
dotnet add package CopperMod.Sid
```

## Development

Tests live alongside their components. Run the relevant project when changing
a backend or shared library, for example:

```powershell
dotnet test CopperMod.Med.Tests/CopperMod.Med.Tests.csproj -c Release
dotnet test CopperMod.ProTracker.Tests/CopperMod.ProTracker.Tests.csproj -c Release
dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release
```

Some integration tests require local reference programs or media. Their absence
is not evidence that the corresponding emulation behavior has been verified.

Package build and publication helpers are under `scripts/nuget`. Windows
self-contained and framework-dependent player archives are produced by
`scripts/release/publish-coppermod.ps1`; the corresponding release helper uses
GitHub CLI. Review artifacts before publishing.

The static project website is under `docs`.

## Related project

[CopperScreen](https://github.com/ilehtoranta/CopperScreen) is the native Amiga
emulator application, including the Lightweight A500 engine and CopperDisk.

## License

MIT; see [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md).
