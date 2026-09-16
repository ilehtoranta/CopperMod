# CopperScreen standalone repository boundary

**2026-09-16 migration update:** the scoped boundary is committed and pushed as
`713ad6c1bc1bc31efc038996c50faccec31208d6`. The user selected the public
[`ilehtoranta/CopperScreen`](https://github.com/ilehtoranta/CopperScreen) repository.
Its standalone checkout preserves 90 app commits, owns its focused tests and
uses unconditional package references. All 66 standalone checks passed, including
both native replays; its local Windows package also built. Source checkout:
`C:/D-drive/Koodit/GIT/CopperScreen`. The original app remains intact here while
package publication and public-feed-only verification are pending feed selection.
See the new repository's `docs/MIGRATION.md` for package hashes and evidence.
The preparation record below is retained as historical context.

## Decision and scope — 2026-09-16

Prepare CopperScreen to move into its own repository without moving or copying
emulator implementation code. Keep Copper68k, CopperDisk and
CopperMod.Amiga.Lightweight in the library repository and consume explicit,
versioned packages from the application. No repository or package has been
published by this preparation step.

The application owns profiles, settings, raw-key names, presentation geometry,
windowing, input mapping, pacing and audio-device delivery. The engine owns all
emulated hardware. The split adds no per-cycle interfaces, dispatch, copying,
logging or diagnostics. Existing native-session pixel and audio paths are unchanged.

## Dependencies

The active host no longer references CopperMod.Amiga, CyberGraphics,
CopperMod.Amiga.Emulator, CopperStart or its SDK. The old Legacy emulator,
presentation-frame helper and CopperBench implementation remain excluded source
for a future optional adapter; their old type bindings must not be re-enabled
unchanged. Unsupported profiles retain their settings and fail explicitly.

CopperScreen uses public Copper68k and Lightweight APIs. Its CPU friend grants
were removed; internal engine-to-CPU timing hooks remain internal. Only profile
metadata and protocol constants were moved into the host, not execution classes.

The local proof uses these unpublished package versions:

| Package | Version |
| --- | --- |
| Copper68k | 1.4.1-boundary.1 |
| CopperDisk | 2.1.1-boundary.1 |
| CopperMod.Amiga.Lightweight | 0.1.0-preview.1 |

Engine and host package references use exact version ranges. These are local
verification versions, not claims that a public feed contains them. Do not
publish different binaries under an already published version.

## Build modes

Monorepo development retains source references by default:

```powershell
dotnet build CopperScreen.Lightweight.slnx -c Release
dotnet test CopperScreen.Lightweight.Tests/CopperScreen.Lightweight.Tests.csproj -c Release
```

The standalone consumer selects `UseEmulatorProjectReferences=false`:

```powershell
dotnet build CopperScreen/CopperScreen.csproj -c Release -p:UseEmulatorProjectReferences=false -p:RestoreAdditionalProjectSources=PATH_TO_LOCAL_FEED
```

Pack the engine with `UseEmulatorPackageDependencies=true`, Release configuration
and `LightweightDiagnostics=false`. Packing diagnostics, Debug configuration or
unversioned source-project dependencies is rejected before package generation.
The guard does not affect ordinary source builds or diagnostic tests.

## Repeatable isolated verification

From the library repository root:

```powershell
./scripts/verify-copperscreen-package-boundary.ps1
```

For native replay, also supply all of `-NativeRom`, `-NativeAdf`, `-NativeScript`
with local paths to the native Kickstart 1.3 ROM, frozen Lemmings disk and
`CopperMod.Amiga.Lightweight.Runner/Workloads/lemmings-native-level1-exploratory.json`.
Without these paths the two native tests are explicitly skipped.

The script creates a fresh directory, packs the three libraries to a local feed,
uses a fresh package cache, copies only the app and focused host tests, and builds
and tests the copied consumer. There are no CPU, disk or engine source projects
beside it. It checks restored dependencies are packages and rejects forbidden
Legacy/CopperStart DLLs in host output. Logs and test results remain in the printed
evidence directory. It never publishes, commits, deletes evidence or touches a remote.

Use `-SourceRoot` to select an isolated source snapshot and `-WorkDirectory` to
choose a new evidence directory. Source builds may update bin/obj in that snapshot;
the script does not isolate unrelated uncommitted library changes automatically.

## Verification evidence

The 2026-09-16 proof uses accepted commit
`a7f30465f8e4a5021b3790f9259313cbac9dc793` plus this boundary change, in a detached
snapshot. Unrelated uncommitted CPU JIT, Legacy and CopperStart work was not packed.
Evidence is retained under `.codex-tmp/copperscreen-package-proof-20260916`.

- Package-only focused suite: **58 passed, 0 skipped, 0 failed**, including both
  14,520-field native Lemmings replays (with and without keyboard input).
- Accepted native fingerprints retained: cycle `2063321634`, ordinary replay CPU
  `6AE7090DA8AFB7E3`, and both replays' pixel/audio output `C65F87325946E5DA`.
  Audio was active and no unsupported feature was reported. The existing
  steady-execution allocation check also passed.
- After adding eight host-owned RAM metadata checks, package-only fast suite:
  **64 passed, 2 native tests skipped, 0 failed**. The native tests above were not
  repeated for this test-only addition and source whitespace cleanup.
- Consumer assets list all three emulator libraries as packages, not projects;
  host assembly and output checks find none of the forbidden dependencies.
- Package metadata pins exact CPU/Disk versions. Negative diagnostic and
  source-dependency pack attempts fail without emitting a package. A guarded
  Release package succeeds.

The pack logs retain the existing CPU build-tool dependency NU1902 warning for
Microsoft.Build.Tasks.Git 10.0.300. The host's optional Git metadata command also
reports Git unavailable in its build-command environment. Neither is hidden or
treated as an execution defect; build-tool dependency maintenance remains separate.

This is dependency/correctness verification, not a new throughput gate; no new FPS
claim or historical gate disposition follows from it.

## Repository migration still to do

1. Commit the scoped boundary change without unrelated work.
2. Choose the new repository owner/name and visibility, then create it with
   authorization. Preserve the app's history where practical.
3. Move the app, profiles/assets, focused host tests and app documentation. The
   focused tests currently link four files from CopperScreen.Tests; include those
   in the move or place them directly in the new test project. Do not move the
   historical Legacy test project and its execution dependency graph.
4. Publish the tested library package versions to the chosen feed; select final
   versions and change standalone default to package mode. Include license,
   notices, solution, CI and application packaging in the new repository.
5. Verify a clean checkout using packages, then repeat native boot/gameplay,
   input, sound, reset and media smoke checks before releasing that checkout.

CopperStart restoration remains separate. Do not add automatic engine fallback
or restore Legacy dependencies to the common host to make that work compile.
