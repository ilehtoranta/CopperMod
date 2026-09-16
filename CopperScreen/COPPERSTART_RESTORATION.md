# CopperScreen native host / CopperStart restoration

2026-09-16: the user authorized decoupling now and restoring CopperStart later.
The standard host builds without `CopperMod.Amiga.Emulator` or any CopperStart
project. Legacy selection remains recognizable in settings and profiles, but
fails explicitly: it is not installed in this build. CopperBench shows an
unavailable message; use native Workbench to launch disk applications.

## Build and test independently

From the repository root:

```powershell
dotnet build CopperScreen.Lightweight.slnx -c Release
dotnet test CopperScreen.Lightweight.Tests/CopperScreen.Lightweight.Tests.csproj -c Release
dotnet test CopperMod.Amiga.Lightweight.Tests/CopperMod.Amiga.Lightweight.Tests.csproj -c Release
dotnet run --project CopperScreen -c Release -- --kickstart "path/to/Kickstart_13.rom" "path/to/disk.adf"
```

The historical `CopperMod.sln`, `CopperScreen.Tests` and Legacy benchmark
projects include the unfinished CopperStart migration. They are not the native
host build/test entry points. Their source is retained, not silently passed or
deleted. The native host suite links the focused existing session, presentation
and audio tests and adds build-boundary/media checks.

## Boundary retained for restoration

- `CopperScreenEmulator.cs` and the original `CopperBenchViewModel.cs` are
  excluded from compilation, not erased. Do not simply re-enable them: their
  old host types need explicit conversion to the neutral session boundary.
- `ICopperScreenSession` retains host-level input/frame/audio/control calls.
  No CPU/device execution dispatch was added. `CopperScreenAdfImage` owns
  standard 880 KiB ADF mount bytes; ZIP loading is bounded and does not extract
  arbitrary media to temporary files. Other disk formats are unsupported.
- Clipboard BGRA payload and allocator profile metadata are host-owned types.
  CopperStart must convert these in a future adapter, not own the common types.
- Filename-based disk navigation was extracted unchanged from Legacy.
- `CopperMod.Amiga` / CyberGraphics remain build dependencies for existing
  configuration and presentation types. No Legacy machine is constructed by
  Lightweight. Removing those remaining type dependencies is optional future
  cleanup, not required to remove CopperStart.

Restore CopperStart through a separately buildable optional Legacy adapter,
with explicit factory registration and unavailable handling when absent.
Keep its SDK/firmware dependencies and host-service tests outside the default
native build. Verify profile round trips, Legacy media conversion, clipboard,
boot/reset and CopperBench launch before enabling its selection again.
Do not add automatic fallback or change a requested machine to make it start.

The original host failed to build from untouched `e9a1ef47` due to 26 distinct
missing CopperStart/SDK compiler errors. That migration remains separate; the
native host no longer requires its completion. Historical build/performance
acceptance and hardware uncertainty records are not relabelled by this split.
