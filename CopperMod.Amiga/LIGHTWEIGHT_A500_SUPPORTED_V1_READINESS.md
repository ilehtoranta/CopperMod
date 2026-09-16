# Lightweight A500: supported-v1 readiness

Updated: 2026-09-15. This is the current bounded completion checklist; historical
gate results remain in [the execution plan](LIGHTWEIGHT_A500_ENGINE_PLAN.md).

## Decision and scope

**2026-09-16 superseding host decision:** CopperStart restoration is deferred
by the user. CopperScreen now builds independently of the Legacy host and
CopperStart; explicit Legacy selection reports unavailable, without fallback.
The independent build/test entry point is `CopperScreen.Lightweight.slnx`.
See [host restoration notes](../CopperScreen/COPPERSTART_RESTORATION.md).
The earlier explicit-Legacy availability statements below are historical.
Decoupled isolated-host checks: **58/58 PASS**, including both native gameplay
replays (keyboard and non-keyboard), with unchanged accepted cycle/CPU/output
fingerprints. See [commit-isolation evidence](LIGHTWEIGHT_COMMIT_ISOLATION_2026-09-16.md).

**Universal Lightweight default authorized and implemented, 2026-09-15.** After
accepting the build, the user approved making Lightweight the default for all
configurations, with Legacy available only by explicit selection. This
supersedes the earlier proposal to route unsupported configurations to Legacy
automatically. It does not add support for missing hardware.

New startup uses `lightweight-a500-kickstart13`: PAL OCS / 68000 / 512 KiB Chip
+ 512 KiB slow / one read-only DF0. No ROM is bundled or machine-specific ROM
path hard-coded: supply `--kickstart <path>` or configure Settings > Machine.
Explicit profiles retain their configured hardware and ROM version. Unsupported
requests fail visibly, without an automatic fallback or silent hardware downgrade.
Use `--engine Legacy` or Settings > Machine > Engine > Legacy to select the old
engine. Selection applies to a new/restarted session, not a mid-session fallback;
it is not persisted inside hardware profile JSON.

**Current disposition: BUILD ACCEPTED WITH EXPLICIT OWNER WAIVER (2026-09-15).**
After the one-off aggregate-host-load exception was proposed, the user stated:
“I accept this build.” This closes the bounded supported-v1 build acceptance,
including final performance-retention acceptance by exception. Live acceptance
is complete. No further rerun or host-load investigation is required solely to
accept this build. The subsequent default-switch authorization is recorded above;
it does not retroactively change the measurement results or acceptance scope.

The waiver applies only to the aggregate-host-load condition for the frozen
build identified below and these three recorded series. It does not change
host policy v2, future measurement requirements, correctness requirements,
the 95% retention threshold or the 200 FPS objective. The original series remain
**INVALID / RERUN under the measurement policy**, not retrospectively valid PASS
results. Earlier valid evidence and all original logs are preserved.

Acceptance rationale: the three candidate medians are 317.02–324.94 FPS with
97.61–99.23% matched-reference retention, below-10% spreads, unchanged
fingerprints and zero allocations, alongside accepted live Lemmings and Hired
Guns behavior. The telemetry did record aggregate activity, but it does not
establish material benchmark interference, a responsible application or thermal
throttling. The waiver accepts that measurement uncertainty; it does not assert
that telemetry was false or that all hardware timing has been verified.

Supported experimental configuration:

- PAL OCS A500, Copper68k accurate 68000, 512 KiB Chip RAM and 512 KiB slow RAM
  at `$C00000`; no real fast RAM.
- Native 256 KiB Kickstart 1.3 (v34).
- One read-only DF0, standard complete 880 KiB ADF, including ADF entries in ZIP.
- Mouse on port 1, keyboard and implemented digital-controller input.
- Reusable full raster output and 48 kHz stereo audio; host presentation and
  audio pacing remain outside the emulated hardware owner.

CopperStart, ECS/AGA, other CPUs/ROMs, RTC, RTG, hard drives, extra floppy drives,
writable or preserved-track media, and save-state compatibility are outside
this scope. Legacy execution override options do not apply to Lightweight.
The current adapter rejects unsupported configurations explicitly.

## Completion checklist

| Check | Disposition |
| --- | --- |
| Focused engine regression suite after held-CIA interrupt repair | PASS, 382/382, previously recorded |
| Hired Guns live music, sound effects and graphics | ACCEPTED by user, 2026-09-15 |
| Hired Guns bounded live responsiveness | ACCEPTED in that same session; not a latency measurement |
| Lemmings native deterministic active-gameplay replay | PASS; live skill assignment remains a separate check |
| Lemmings live skill assignment on combined final build | ACCEPTED: user confirms “Digger digs a hole.” |
| Disk replacement on combined final build | PASS: disk-1 prompt, F12 disk-2 replacement, menu loads |
| Reset recovery on combined final build | PASS: toolbar reset, native ROM re-entry, animated cracktro reload |
| Final matched-harness performance retention | ACCEPTED BY OWNER WAIVER; three underlying series remain INVALID / RERUN |
| Supported-v1 frozen build acceptance | ACCEPTED by user, 2026-09-15 |
| Universal Lightweight default switch | AUTHORIZED / IMPLEMENTED; focused host checks and native replay PASS |

## Default-switch implementation and verification (2026-09-15)

Application startup and Settings now default to Lightweight for every profile.
The standard startup profile is the native A500 configuration above. Explicit
Legacy selection remains available in Settings and on the command line; typed
Legacy test/benchmark entry points retain their historical defaults. Unsupported
profiles do not fall back. Loading, restarting and saving a profile now preserve
its chipset and ROM version, repairing a Settings round-trip defect that could
otherwise disguise unsupported hardware as OCS / Kickstart 1.3.

Release host: `CopperScreen/bin/Release/net10.0/CopperScreen.exe` (repository-root
relative). CopperScreen DLL SHA256:
`13E64B05D2B2109E30E608A4BE9A4AA22FBEF43714DC97C8B1A762A21B6EB04F`.
The Lightweight engine and Copper68k DLLs are byte-identical to the accepted
pre-switch build listed below. This is a host startup/settings change, not a
new engine optimization or a new formal performance result.

- `lightweight-default-routing-final-tests-20260915.log`: **133 passed, zero
  failed/skipped**, including both native 14,520-field Lemmings replays through
  application-default routing, with and without keyboard input.
- Following final ROM-version preservation changes,
  `lightweight-default-routing-settings-final-tests-20260915.log`: **133 passed,
  zero failed, two native tests skipped** because external asset variables were
  not set for this short rerun. This adds two unsupported-ROM-version cases;
  native replay evidence is the preceding completed run, not the skipped tests.
- Coverage includes explicit Legacy selection, unsupported profiles and missing
  ROM reporting, settings/save round trips, startup arguments, presentation,
  disk archives and existing Legacy integration routes. Logs are under the
  repository-root `.codex-tmp/` directory. No new live UI smoke is claimed.

The first native run exposed stale pre-sprite-repair expected fingerprints in
the host tests. The unchanged accepted engine produced the already recorded
cycle `2063321634`; expectations were reconciled with the existing accepted
evidence (CPU `6AE7090DA8AFB7E3`, output `C65F87325946E5DA`), and both replays
then passed. The initial failing log
`lightweight-default-routing-native-tests-20260915.log` is preserved.
The build still emits the existing non-fatal Git metadata command warning;
binary hashes above identify this build independently.

## Accepted pre-switch build identity

Final live host: `.codex-tmp/hiredguns-live-combined-fixes-20260915/`.

- CopperScreen SHA256: `0D3F3CBF57787C73B7D26D5F1344D5597EE5ED0402CCCE63A634D461F9B65B7B`.
- Engine SHA256: `D97933A56E464C312FC60C1730E548F235B1206B924E1CA43288FE1837A84A4F`.
- Unchanged Copper68k SHA256: `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.

The performance candidate uses that engine in a copy of the preceding frozen
reference's runner directory. Only the engine DLL differs between the two DLL
sets; the runner is identical, SHA256
`03C85638C7A4BCBA85C85482754A5B0F8AA0B73A63C7B2F1929CBA58CE71EB36`.
Reference engine: `139EDEA68A38B60E0430DDF64A9EDABCE9E1C9EE900FDD65C142D6207516A905`.

## Final-build live Lemmings session

Process 13404 was launched from the combined host build at 22:53:29 local time
on 2026-09-15 with explicit Lightweight selection, the native profile/ROM and
Lemmings disk 1. Cracktro, publisher/developer screens and animated introduction
rendered. At the disk-2 prompt, F12 changed DF0 to disk 2; a display click resumed
loading and the animated menu appeared (observed around field 10111).
The subsequent one-player coordinate click did not establish successful menu
selection; a manual skill-assignment check was requested, rather than claiming
an automated pass. The user subsequently confirmed: “Digger digs a hole.”
This closes the final-build live skill-assignment check. This is functional
host evidence, not formal throughput or a general hardware timing proof.

Log: `C:/Users/vsys-admin/AppData/Local/CopperScreen/Logs/CopperScreen-20260915-225329-13404.log`.
The observed interval has advancing frames and zero audio-submit failures.

The gameplay window was already closed when resuming after the user's digger
confirmation. A separate session using the identical combined build and disk 1
(process 11776, 23:13:17 local) rechecked reset recovery. The toolbar reset was
followed by native ROM execution (`FC3130`, `FC0F94`), renewed disk activity,
then the animated cracktro (`0303EC`, observed field 3673). No audio-submit
failure was recorded. The first reset input was rejected because window bounds
changed; a fresh observation and single retry succeeded. This is a boot-path
reset smoke check, not a claim of reset from active level execution.
Log: `C:/Users/vsys-admin/AppData/Local/CopperScreen/Logs/CopperScreen-20260915-231317-11776.log`.
The window and process were confirmed closed before the performance retry.

## Performance evidence

The final series uses the unchanged native Lemmings level-1 script, 600 gameplay
warmup fields after checkpoint 10320, and 3600 measured fields (10921–14520).
Three samples per engine are interleaved R1, C1, C2, R2, R3, C3. Complete CPU,
DMA, pixels and real stereo audio remain enabled. Require Normal priority,
verified P-core placement, valid host-policy-v2 telemetry, spread at most 10%
of the median, at least 95% matched-reference retention, and at least 200 FPS.

Evidence: `.codex-tmp/lightweight-final-readiness-same-runner-20260915.log` and
its same-named evidence directory. Log SHA256:
`933486F9476F305EE906A5B747E17E2F4F03935444F865E9734ACED3A4048340`.
First complete series:

| Engine | Samples (FPS) | Median FPS | Spread |
| --- | --- | --- | --- |
| Pre-CIA-fix reference | 335.53, 322.68, 309.02 | 322.68 | 8.22% |
| Final repaired engine | 318.46, 321.13, 320.21 | 320.21 | 0.83% |

Measured retention is 99.23%, but **INVALID / RERUN**: aggregate competing CPU
activity exceeded 25% continuously for 10.381 seconds during C2. No sample is
cherry-picked and no previous valid gate is replaced. All six samples retained
cycle `2063321634`, CPU `6AE7090DA8AFB7E3`, hardware `334A819F6FDFB1CA`, output
`C65F87325946E5DA`, real audio, zero allocation and no unsupported feature.
Placement was verified P-core logical CPU 2 / mask 4 / SMT sibling 3, Normal
priority, Balanced power plan, 24 logical CPUs, .NET SDK 10.0.303. Topology,
build/media/script hashes and elapsed-interval telemetry are in the evidence.

An earlier attempt (`lightweight-final-readiness-performance-20260915.log`)
was stopped before a completed result to normalize differing runner binaries.
It is incomplete/superseded, not a performance STOP; no samples are reused.
The unpinned 310.31 FPS diagnostic is not formal retention evidence.

### Complete retry after live acceptance

Evidence: `.codex-tmp/lightweight-final-readiness-rerun-20260915.log` and its
same-named evidence directory. Log SHA256:
`CE1EC19B207092692D4CC07415AB9095930E01E218589C5CA7FF1CB93D58FFCD`.
Same frozen DLLs, inputs, interval, sample order and placement protocol;
the live emulator was closed before preflight, with no concurrent build/test.

| Engine | Samples (FPS) | Median FPS | Spread |
| --- | --- | --- | --- |
| Pre-CIA-fix reference | 325.37, 324.77, 310.62 | 324.77 | 4.54% |
| Final repaired engine | 317.02, 315.34, 322.27 | 317.02 | 2.19% |

Measured retention is 97.61% (3.154 ms/field), above the numerical 95% retention
and 200 FPS thresholds, but **INVALID / RERUN**: aggregate competing CPU
activity remained at least 25% for 10.314 seconds during R2. All six samples
retained the exact fingerprints and zero allocations listed above; both spreads
were below 10%. Those checks do not waive the invalid host result. No valid
performance gate is replaced, no selected samples are salvaged, and no further
automatic retry is initiated in this completion pass.

### User-requested second retry

Following the user's further “Please proceed,” the same complete protocol ran
again with no engine, harness, input or threshold changes. No CopperScreen,
dotnet or testhost process was present at the initial process check. Fresh
topology and placement verification selected P-core CPU 2 / mask 4 / sibling 3,
Normal priority, Balanced power, 24 logical CPUs and SDK 10.0.303.

Evidence: `.codex-tmp/lightweight-final-readiness-rerun2-20260915.log` and its
same-named evidence directory. Log SHA256:
`DC67D2BC20E2BBD37C23B2B49CE1D797FBF5F44359916B58D52F5C91A75F7705`.

| Engine | Samples (FPS) | Median FPS | Spread |
| --- | --- | --- | --- |
| Pre-CIA-fix reference | 330.39, 329.01, 326.22 | 329.01 | 1.27% |
| Final repaired engine | 328.78, 322.26, 324.94 | 324.94 | 2.01% |

Measured retention is 98.76% (3.077 ms/field). All six samples retain the exact
CPU/hardware/output fingerprints, zero allocations, complete real audio and
no unsupported feature. The numerical retention, absolute FPS and spread
checks pass, but the **series remains INVALID / RERUN**: aggregate competing
CPU activity reached the 25% threshold continuously for 10.117 seconds, with
rejection reported during R3. Prior invalid series are not reclassified and
prior valid evidence is preserved. No default switch or further retry occurred.

Before the owner's acceptance, bounded external host-load attribution was
recommended instead of another unchanged rerun. The subsequent one-off waiver
removes that investigation as a requirement for this build's acceptance. It
remains optional future measurement work, not an emulator defect or a new gate.

## Disclosed limitations

The [issue register](LIGHTWEIGHT_A500_ENGINE_ISSUES.md) remains authoritative for
hardware uncertainties and defects. Experimental acceptance does not certify
all cycle behavior. In particular, disk receiver/rate-change edges, CIA/Paula
sub-CCK interrupt synchronization and adversarial display timing remain subject
to the recorded limitations. Passing either game is not a hardware oracle.

Occasional interlace instability remains OPEN / deferred under LWA-HOST-002.
The user accepted current live Hired Guns behavior with this non-blocking
caveat. Its cause is unclassified, not established as inherent or unfixable.
Do not reopen the accepted session as an exhaustive compatibility exercise.

The original complete-workload comparison (343.10 FPS versus 28.41 FPS Legacy,
12.08×) already satisfied the performance objective in the accepted experimental
scope. This completion pass checks retention after repairs; it does not restart
historical G6/G7 or require broad compatibility before a scoped switch decision.
