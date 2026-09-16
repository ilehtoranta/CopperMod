# Lightweight commit isolation — 2026-09-16

## Current disposition after authorized host decoupling

The user subsequently authorized decoupling CopperStart and restoring it later.
The native host now builds in the isolated checkout without the uncommitted
CopperStart migration. `CopperScreen.Lightweight.Tests` passed **58/58** with
both native 14,520-field Lemmings replays enabled. Cycle/CPU/output expectations
remain the accepted fingerprints below. Log:
`.codex-tmp/lightweight-decoupled-isolated-native-tests-20260916.log`.
Legacy/CopperBench are explicitly unavailable; their source is retained.
See [restoration notes](../CopperScreen/COPPERSTART_RESTORATION.md).

The publication candidate includes the independent engine, runner, CPU
dependencies, native CopperScreen host, focused host test project and scoped
documentation/harnesses. Historical Legacy host/tests and native-comparison
helper edits are excluded from the commit, as are all CopperStart and graphics
migration edits. The existing core/CyberGraphics configuration and presentation
types remain build dependencies, but no Legacy execution instance is created.

No chipset execution change was made for decoupling. The build reports an
existing Microsoft.Build.Tasks.Git NU1902 package warning and a non-fatal Git
metadata-command warning; these are not hidden by the successful build/tests.

## Initial request and blocker (historical)

The user requested commit/push of Lightweight and its required dependencies,
excluding unrelated work. Initially no changes were staged, committed or pushed
while the existing CopperScreen build prerequisite failure was investigated.

Base: `e9a1ef47` (`Bus scheduler`), local `main`, matching fetched `origin/main`.
Remote: `https://github.com/ilehtoranta/CopperMod.git`.

Candidate checkout: `.codex-tmp/lightweight-commit-20260916/` (repository-root
relative). Untouched base check: `.codex-tmp/lightweight-base-check-20260916/`.
Both are detached worktrees. The original working tree and its index are
unchanged; excluded work has not been discarded.

## Prepared candidate scope

- Independent Lightweight engine, tests, headless runner and input scripts.
- Copper68k internal visibility, conservative bus-boundary batching,
  TST.B displacement plan and unobservable prefetch-context optimization,
  with the associated CPU tests. JIT repairs are excluded, including the
  JIT-only additions in the shared opcode-plan file.
- CopperScreen session adapter, default routing/settings, presentation/audio
  changes and focused tests; native comparison helper and benchmark entry.
  The unrelated Legacy 68040 run-ahead change and other Legacy test changes
  remain excluded.
- Lightweight solution entries, execution/readiness/issue documents and
  performance harnesses. No ROMs, game disks, binaries or local logs selected.

CopperStart, native graphics-library work, Legacy DMA changes, unrelated
replayers/SID work and broad ignore-file changes remain excluded. The candidate
is prepared for inspection, not yet a final staged manifest.

## Independent verification

All logs below are repository-root `.codex-tmp/` files.

- `lightweight-isolated-engine-tests-20260916.log`: 382 passed, no failures or
  skips, Release.
- `lightweight-isolated-cpu-tests-20260916.log`: 1,482 passed, no failures,
  six optional external-corpus skips, Release.
- `lightweight-isolated-runner-build-20260916.log`: Release runner build passes.
- `lightweight-isolated-native-replay-20260916.log`: native Kickstart 1.3,
  Lemmings level-1 script, 10,920 warmup fields and 3,600 measured fields;
  14,520 completed. Cycle `2063321634`, CPU `6AE7090DA8AFB7E3`, hardware
  `334A819F6FDFB1CA`, output `C65F87325946E5DA`, real PCM, zero allocations,
  unsupported `none`. All fingerprints match previously accepted evidence.
  The reported 269.32 FPS is an uncontrolled diagnostic, not a formal
  performance comparison or regression finding.

## Host prerequisite blocker

`lightweight-isolated-host-tests-20260916.log` fails while compiling
`CopperMod.Amiga.Emulator`, before host tests execute. An untouched checkout of
the base commit fails too (`lightweight-base-host-build-20260916.log`). Their
26 distinct compiler error code/message pairs are identical: missing
CopperStart/Icon, Amiga SDK and graphics-related types. This is not an engine
dependency: the standalone Lightweight engine/runner build and execute without
the old host machinery.

The current working tree has substantial CopperStart migration source and
project-reference changes that are absent from the base commit. Importing that
entire migration would violate the chosen isolation boundary. Passing the
previous full-working-tree host tests does not establish a buildable isolated
host commit. No host PASS is claimed here.

Options presented to the user: publish the verified standalone Lightweight
engine/runner/CPU portion first and leave host integration pending, or hold
publication until the separate CopperStart prerequisite is resolved.
