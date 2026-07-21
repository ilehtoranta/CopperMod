# Deferred CPU Corpus Matrix

This document records the local ADF/IPF corpus review for the deferred CPU
bus and dynamic-DMA work. It is a test-selection guide, not an emulation
assumption: a title is useful only after its counters show the relevant state.

## Corpus Inventory

The local `D:\TestData\TestImages` corpus currently contains:

- 59 outer files
- 54 ZIP images
- 4 standalone ADF files
- 40 ADF files inside ZIPs
- 34 IPF files inside ZIPs

The repository copy under `CopperScreen\TestImages` contains the benchmark
subset. Team17's ZIP collection adds IPF sets that are not all in the current
benchmark matrix.

## Priority Matrix

| Priority | Workload | Image type | Why it matters | Current evidence |
| --- | --- | --- | --- | --- |
| 1 | Hired Guns | ADF | Workbench takeover and expansion-RAM execution provide deferred-batch coverage. | Short audit: 4,231 deferred batches and 3,236 fixed-image uses. No active-blitter overlap yet. |
| 2 | Full Contact original | ADF | Confirmed deferred production coverage and useful intro timing. | 2,309 fixed-image uses; production median improved about 3%. No active-blitter overlap yet. |
| 3 | Desert Strike | ADF | Existing frame-1800 interaction reaches later disk/gameplay state and intro blitter activity. | Run through frame 1800 before judging coverage. |
| 4 | Worms / Worms: The Director's Cut | IPF | Likely sustained gameplay blitter use and CPU/chip-RAM interaction. | Not yet in benchmark matrix. |
| 5 | Apidya | IPF | Team17 action workload with likely frequent DMA and blitter activity. | Not yet in benchmark matrix. |
| 6 | Alien Breed / Alien Breed II | IPF | Team17 games with long-lived chip/expansion-RAM code paths. | Short boot audits did not reach active-blitter overlap. |
| 7 | Alien Breed 3D | IPF | Planar conversion and Copper/DMA interaction are useful dynamic-state coverage. | Resolves locally but currently hits the existing boot-program memory-fit fault. |
| 8 | F17 Challenge / Overdrive | IPF | Additional Team17 gameplay and DMA variety. | Added to the benchmark matrix; short smoke boots succeed. |
| 9 | Superfrog CSL | ADF | Existing cracktro tests cover line blits and display DMA. | Live shadow coverage exists, but short deferred audit had no active-blitter overlap. |
| 10 | Full Contact IPF / Operation Thunderbolt IPF / Shadow IPF | IPF | Important disk and display regression references. | Use after existing protected-loading/regression failures are separated from this work. |

## What To Measure

For every candidate, run interpreter-only Release audits and record:

- framebuffer and audio checksums
- boot/status text and scheduler cycle
- `cpubatch` attempts and uses
- `waitfast` attempts and uses
- `fixedprod` attempts, uses, and verification mismatches
- `slotshadow` matches/mismatches
- `slotblt` scratch counters
- `bltoverlap=attempts/supported/unsupported/nasty` production overlap counters
- blitter top-pattern and specialization counters
- scheduler and bus-access drain counts

`slotblt` is the decisive signal for the current dynamic-blitter work. A title
can be blitter-heavy and still be irrelevant if no deferred CPU exit occurs
while the blitter is busy.

## Synthetic ROM Workload

`DeferredCpuSyntheticRomOrExpansionWorkloadReportsActiveBlitterOverlap` in
`CopperMod.Amiga.Tests/AmigaBusTimingTests.cs` is the focused dynamic-DMA
workload. It executes from either a mapped Kickstart-style ROM address or
pseudo-fast expansion RAM, repeatedly writes `BLTSIZE` to restart a four-word
blit, and performs sixteen chip-RAM word reads between starts. The test
compares a baseline bus with deferred batching for retired instruction count,
PC, address-register state, cycles, and blitter overlap counters.

The test runs with shadow verification enabled. Supported non-live active
blits now use the same ordered pending-CPU slot execution in baseline and
deferred preparation, including the HRM three-miss rule for non-nasty blits
and queued `BLTSIZE` restarts. ROM and expansion-RAM variants require zero
shadow mismatches. Combined live-display plus blitter scratch remains
explicitly unsupported until both devices share one disposable DMA image.

## Baseline Commands

Use the benchmark project from `CopperScreen`:

```powershell
dotnet build ..\CopperMod.Amiga\CopperMod.Amiga.csproj -c Release --no-restore --no-dependencies
dotnet build ..\CopperScreen.Benchmarks\CopperScreen.Benchmarks.csproj -c Release --no-restore --no-dependencies

dotnet ..\CopperScreen.Benchmarks\bin\Release\net10.0\CopperScreen.Benchmarks.dll `
  --only "Hired Guns" --cpu interpreter --warmup 600 --frames 360 --repeat 5 `
  --hardware-profile --cpu-deferred-bus-batch --cpu-deferred-bus-batch-verify

dotnet ..\CopperScreen.Benchmarks\bin\Release\net10.0\CopperScreen.Benchmarks.dll `
  --only "Desert Strike intro" --cpu interpreter --warmup 1800 --frames 360 --repeat 3 `
  --hardware-profile --cpu-deferred-bus-batch --cpu-deferred-bus-batch-verify
```

For a new IPF candidate, first add a temporary benchmark workload entry with
the exact ZIP name from `D:\TestData\TestImages\Team17`, then run the same
paired baseline and verified variants. Do not treat a boot-only 30/60-frame
run as gameplay coverage.

## Current Conclusions

### 2026-07-12 paired results

- Conservative batch admission now requires at least a 16-cycle cached event
  horizon and skips the next three probes after a measured one-instruction
  chip-visible exit. The synthetic loop drops from about 1.89 million to
  444 thousand batches. Its median remains slower than baseline (ROM about
  +35%, expansion about +13%), showing that admission reduces but does not
  eliminate per-exit cost. Full Contact original retains useful coverage at
  about 517 thousand batches and measured 107.6 FPS versus a 105.6 FPS paired
  baseline median (+1.9%).

- The dedicated ROM/expansion synthetic active-blitter loop preserves CPU
  cycles and checksums, but is adversarial to the current batch boundary:
  about 1.89 million batches are entered for 2 million instructions. Median
  time regressed from 467.6 ms to 674.3 ms in ROM and from 879.1 ms to
  1048.4 ms in expansion RAM. Only about 122 thousand exits overlap an active
  blitter. This points to per-batch enter/exit and pre-request replay cost,
  not incorrect CPU or DMA timing.
- Full Contact original, interpreter repeat 5, improved from a 101.1 FPS
  baseline median to 108.2 FPS deferred (+7.0%). Framebuffer
  `0x23B5F535`, audio `0xF745C11D`, status, and scheduler cycle matched.
  It used 1,105,253 batches and 150,215 wait-fast attempts, but recorded no
  active-blitter overlap in this boundary.
- Follow-up audio auditing found that this Full Contact gain was not valid:
  the runtime batch boundary skipped an opaque incremental-audio callback.
  The resulting CPU polling phase delayed the AUD0/AUD1 DMACON enable write
  from cycle 44,156,008 to 44,156,398 and shifted Paula by one sample byte.
  Runtime boundaries with `beforeDeviceAdvance` callbacks now reject deferred
  batching before bus setup. Paired framebuffer and audio checksums match
  again; production runtime coverage is intentionally zero until that callback
  exposes a usable event horizon.
- The incremental audio callback now exposes that horizon explicitly as its
  next output-sample cycle. Runtime batching clamps the bus wake target to the
  earlier of the hardware event and audio sample, then invokes the existing
  callback at the instruction boundary. Full Contact again has 1,820,771
  admitted batches with exact framebuffer `0x23B5F535` and audio
  `0x2F65C11D`. Repeat-3 median improved from 100.2 to 106.9 FPS (+6.7%).
  Opaque callbacks without a horizon remain rejected before bus setup.
- The audio-only horizon has now been replaced by an allocation-free composite
  execution-boundary schedule. It covers output samples and synthetic VBlank
  cycle boundaries while retaining disk insertion at frame start and input
  pulse advancement at frame completion. Opaque legacy callbacks are rejected
  before bus batch setup. Batch counters are now committed only when the first
  instruction starts, so a used batch can no longer contain zero instructions.
  The corrected Full Contact interpreter repeat-5 result is 101.2 FPS baseline
  versus 104.0 FPS deferred (+2.8%), with framebuffer `0x23B5F535`, audio
  `0x2F65C11D`, and scheduler timing identical in every run. Deferred execution
  recorded 462,726 attempts, 316,622 used batches, 1,058,623 instructions, and
  316,622 flushes. This supersedes the earlier +6.7% result, whose admission
  counters did not prove that each reported batch executed an instruction.
- A follow-up sampled profile and interleaved paired run showed that the
  generalized schedule itself is not a measurable hotspot. The dominant new
  costs are CPU wake-horizon admission (`GetNextCpuBatchWakeCandidateCycle`,
  about 2% exclusive in the sampled deferred run) and batch enter/exit work.
  Full Contact executes only 3.34 instructions per used batch and reduces
  scheduler drains by about 8.1%. Cooling admission after chip-visible batches
  of three instructions or fewer improved throughput, but did not make the
  path neutral: the interleaved repeat-5 medians were 124.1 FPS baseline and
  118.5 FPS deferred (-4.5%). Every pair retained framebuffer `0x23B5F535`
  and audio `0x39C5C11D` at the 300/180-frame boundary. Dynamic-DMA expansion
  should remain paused until enter/exit cost can be amortized more effectively.
- The benchmark phase profiler still reflected the removed audio callback and
  crashed after measured frames. It now drives the generalized boundary
  schedule directly, including frame begin, execution begin/end, and frame
  completion.
- Batch lifetime now survives ordinary ROM, real-fast, and expansion-RAM
  reads/writes after committing any queued timing. Chip RAM, custom registers,
  CIA/RTC, and chipset-visible writes remain hard exits. A trial that also
  retained batches across chip-RAM reads preserved the framebuffer but changed
  Full Contact audio (`0x39C5C11D` to `0x96B5C11D`), proving that such reads can
  advance observable chipset state before the scheduled host sample; that
  broader variant was discarded.
- The conservative persistence path reduces Full Contact used batches from
  313,084 to 272,705 while increasing executed batched instructions from
  1,043,591 to 1,194,146. Average batch length rises from 3.33 to 4.38
  instructions. An interleaved interpreter repeat-5 run measured 120.3 FPS
  baseline versus 123.0 FPS deferred (+2.2%), with framebuffer `0x23B5F535`
  and audio `0x39C5C11D` identical in every pair. Lemmings remains checksum
  stable with zero batch coverage.
- Exact cycle-based retry admission was tested and rejected. Most short
  horizons were `current+1` pending-interrupt barriers, so literal retrying
  increased rather than reduced admission attempts. The existing
  three-instruction productivity cooldown remains unchanged.
- The scheduler now returns immediately when a pending CIA or unmasked Paula
  interrupt can enter at `current+1`; no later VBlank, CIA timer, disk, Paula,
  Copper, or blitter source can beat that cycle. This preserves batch cadence
  and counters while avoiding a full wake-source scan on the dominant rejected
  admission shape. A three-pair Full Contact production run kept identical
  framebuffer `0x23B5F535`, audio `0x909DC11D`, and batch counters
  (`418804/272704/1194167/982684/272704`). Deferred execution was faster in
  every pair; noisy medians were 110.3 FPS baseline and 123.4 FPS deferred.
- Deferred instruction bookkeeping is now batch-level. The interpreter marks
  use once before the first instruction, accumulates completed instructions and
  skipped flushes in locals, and publishes both totals once before batch close.
  This removes two timing-interface calls per batched instruction without
  changing grants or exits. Full Contact retained matching framebuffer/audio
  checksums and stable per-run counters; a noisy three-pair run measured 121.5
  FPS baseline versus 123.5 FPS deferred (+1.6%). Sampling confirms the old
  per-instruction callback methods are absent from the hot stack.
- Hired Guns improved from 629.7 FPS to 922.6 FPS and reduced scheduler drains
  from 550,855 to 22,099 with matching checksums and scheduler cycle. The
  selected boundary is still a blank boot/loading phase with no blitter work,
  so this is static-path evidence only and not a dynamic-DMA validation.
- Long verified synthetic runs still show owner-timeline-only shadow
  mismatches near refresh/blitter boundaries even when grant, completion,
  CPU cycles, and checksums match. The audit scratch model is therefore not
  ready to certify combined dynamic ownership timelines.

### 2026-07-13 consolidation checkpoint

- Deferred batch admission, execution accounting, and exit now form one
  exception-safe lifecycle. Boundary target clamping is inside the guarded
  region, so a schedule failure cannot leave the bus batch active. The
  three-instruction admission cooldown uses named constants, and a host
  exception after entering the first instruction can no longer produce a
  used batch with zero recorded instructions.
- The focused `DeferredCpu|CpuBatchWakeCandidate|ExecAddIntServer` Release
  suite passes 48/48. The separate Copper68k `Prefetch|SingleStep` filter
  retains two unrelated dirty-tree failures in
  `PlannedMoveLongDataToAbsoluteLongPreservesPrefetchedSequentialOpcode`.
  The Full Contact IPF protected-loader regression also remains unchanged.
- Interleaved interpreter repeat-5 verification pairs confirmed that the
  selected Lemmings SR and Full Contact FLT windows have no production batch
  coverage. Lemmings measured 52.1 FPS baseline versus 50.7 verified (-2.7%);
  Full Contact FLT measured 61.5 versus 59.6 (-3.1%). Both retained exact
  framebuffer/audio checksums and scheduler drain counts. Lemmings reported
  `cpubatch=0/0/0/0/0`, `waitfast=0/0/0/0/0/0/0`, and
  `slotshadow=23/23/0/0`; Full Contact FLT reported zero CPU batches/fast
  grants and `slotshadow=4913/105/0/4808`. The measured cost is verification
  overhead without a production opportunity, so these windows should remain
  correctness checks rather than justify broader scratch work.

### 2026-07-13 production validation

- Interleaved interpreter repeat-5 production pairs, without verification
  overhead, measured Full Contact original single-drive at 116.3 FPS baseline
  and 116.3 FPS deferred. Checksums remained framebuffer `0x23B5F535` and
  audio `0x2F65C11D`. Deferred execution used 272,705 of 418,805 attempts for
  1,194,146 instructions (4.38 instructions per used batch), reducing scheduler
  drains from 10,873,470 to 9,944,030 (-8.5%).
- Hired Guns measured 413.4 FPS baseline and 600.5 FPS deferred (+45.3%).
  Checksums remained framebuffer `0x0B7A1325` and audio `0x30A5C11D`.
  Deferred execution used 31,089 of 31,126 attempts for 537,262 instructions
  (17.28 instructions per used batch), reducing scheduler drains from 551,243
  to 48,997 (-91.1%).
- Separate verified runs retained the same checksums and drain counts. Full
  Contact reported `slotshadow=2031/2024/0/7` and zero verification mismatches;
  Hired Guns reported no shadow attempts and zero verification mismatches.
  The production path is therefore worth retaining: it is neutral for short
  batches and highly effective when ROM/expansion execution amortizes entry
  and exit costs.

- Lemmings SR and Full Contact FLT are checksum-stable regression workloads,
  but their short deferred audits showed zero `cpubatch` and zero `waitfast`
  coverage.
- Full Contact original is the current production deferred-path benchmark:
  it reduces scheduler drains and improves production-only median throughput,
  while verified mode adds measurable audit overhead.
- Existing real-workload runs have not yet produced nonzero `slotblt`.
  Active blitter support therefore remains synthetically validated but not
  corpus-validated.
- `bltoverlap` now distinguishes a deferred CPU exit during an active blitter
  from ordinary blitter activity. It is the first counter to inspect when a
  workload has real deferred-batch coverage.
- The next corpus task is to reach actual gameplay/intro blitter activity in
  Hired Guns, Desert Strike, Worms, Apidya, or Alien Breed 3D and capture a
  nonzero `slotblt` sample before expanding Paula or disk support.
- The new IPF smoke matrix resolves Apidya, F17 Challenge, Overdrive, Worms,
  and Worms: The Director's Cut. Alien Breed 3D still needs a memory/profile
  fix before it can serve as a runtime test.
