# CopperScreen product migration — 2026-09-16

The canonical emulator product is now
[ilehtoranta/CopperScreen](https://github.com/ilehtoranta/CopperScreen), including
the desktop app, Lightweight engine, CopperDisk, tests, headless runner, workloads
and performance/issue records. This supersedes the earlier UI-only package split.

The engine/disk source was exported from this repository's committed snapshot
`0978025c4786cd924ef99fb82bd6508c50888494`; all 76 imported C# Git blobs are
unchanged. The destination import is `257fdf4db904a8ae7649c2544dadcc5a2b0880b7`.
See its [current ownership and verification record](https://github.com/ilehtoranta/CopperScreen/blob/main/docs/PRODUCT_OWNERSHIP.md)
for subsequent public-package lock correction and CI evidence.

The corrected destination commit is `a5e0e22ffa0f9b9a22a6efb8eeaf050753399981`.
Both its production and diagnostic [CI jobs passed](https://github.com/ilehtoranta/CopperScreen/actions/runs/35142346789)
before source removal. All 66 native-enabled host tests were rerun locally using
the public packages after the lock correction and passed.

## What moved

- `CopperMod.Amiga.Lightweight`, its tests and runner/workloads.
- `CopperDisk`, its tests and package scripts. The player has no direct or
  transitive source dependency on this disk library.
- Focused native-host tests and the Lightweight solution are replaced by the
  standalone product's solution and tests.
- Lightweight performance harnesses and six execution/issue/evidence documents.
  Short forwarding notes preserve existing document links here.
- Emulator builds/tests no longer belong to this repository's active solution
  or CI. Obsolete CopperScreen release and boundary-verification scripts are
  removed; release from the canonical repository, not the old compatibility host.

All removed source remains recoverable from Git history and the destination.
No histories were rewritten. No package or binary release was made by this move.

## What stays

- Copper68k, shared with the player; CopperScreen uses its exact published
  `1.4.1-boundary.1` package.
- Older `CopperMod.Amiga`, required by Cust/AHX, and the music player/backends.
- Shared host-load policy/support used by historical Legacy G6/G7 evidence.
  Their results have not been relabeled or invalidated by this ownership change.
- The unfinished Legacy/CopperStart workspace and historical host/headless/test
  projects. Many contain uncommitted user work; this migration does not discard,
  export or commit that work. They are removed from the active solution, not
  presented as the active CopperScreen implementation.

Retained old host/Legacy consumers now reference the already published
`CopperDisk 2.1.1-boundary.1` / `CopperMod.Amiga.Lightweight 0.1.0-preview.1`
packages where necessary. This is a compatibility snapshot, not a source fallback
or a second owner of engine development. Further archival/restoration of the
unfinished workspace is a separate task.

## Verification and existing limitations

CopperScreen Release build, 382 engine tests and 74 disk tests passed. Its 66
host tests include the native Lemmings replays with unchanged accepted
fingerprints; the short synthetic runner also completed. Diagnostic and lean
production outputs are isolated. No formal FPS gate was rerun.

CopperMod validation used a clean detached worktree at the source snapshot,
before and after applying only this cleanup:

- Copper68k: **1,482 passed**, six optional external-corpus tests skipped.
- AHX shared-Amiga tests: **18 passed**, no skips.
- Retained compatibility host: Release build passed using published packages;
  the inherited optional Git-metadata lookup warning is still visible.
- Player build: **same pre-existing failure before and after**,
  `CopperMod.Cust/CustMachine.cs:488`, CS1729: `KickstartTrapTable` has no
  17-argument constructor. No new missing-project or missing-type errors.
  Existing Microsoft.Build.Tasks.Git vulnerability warnings also remain.

The player is therefore **not claimed to build successfully**. Repair its
existing Cust/API mismatch separately; do not conflate it with this migration.
The old CI step referencing the already absent performance-analyzer test project
was replaced with the actual AHX shared-Amiga test project.
The retained CPU packaging script also invokes Legacy Amiga tests; therefore its
existing Cust dependency can still block this repository's full CI. No CI pass
is claimed by the focused checks above, and this move does not disable those
existing checks to conceal the failure.

Local logs are under ignored `.codex-tmp/`:
`copperscreen-player-baseline-20260916.log`,
`copperscreen-player-after-20260916.log`,
`copperscreen-cpu-after-20260916.log`,
`copperscreen-ahx-after-20260916.log`, and
`copperscreen-compat-after-20260916.log`.
