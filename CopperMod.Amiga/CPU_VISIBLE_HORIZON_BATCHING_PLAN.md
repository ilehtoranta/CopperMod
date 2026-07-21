# CPU-Visible Horizon Batching Plan

Status: **Planning complete; implementation not started**  
Created: 2026-07-21  
Primary target: A500 PAL OCS, `AccurateM68000`, no JIT  
Secondary targets: OCS NTSC, ECS PAL, ECS NTSC  

## Purpose

Restore the original performance property where chipset emulation advances in
large batches to the next CPU-visible event horizon, without reintroducing
retrospective Chip RAM reconstruction, future-value reservations, or
out-of-order bus commits.

This is intentionally a multi-stage project. Every stage must be independently
measurable, testable, reversible, and safe to leave enabled while the following
stage is developed.

## Current position

The causal executor now preserves chronological sampling and presentation, but
ordinary CPU activity can fragment chipset execution into very small scheduler
drains. Fixed Agnus work and sole Paula/disk work can already advance between
maintained causal barriers. The remaining opportunity is to allow the CPU to
run through intervals where no unresolved chipset effect can change anything
the CPU observes.

Recent reference measurements from the current working tree:

- Lemmings SR improved from approximately 83.4 FPS to 109.4 FPS after safe
  fixed/dynamic range batching.
- Hired Guns remains workload- and phase-sensitive and has shown roughly
  87–145 FPS in repeated headless runs.
- Historical performance before the causal refactor exceeded 500 FPS in some
  Workbench-style phases.
- The focused physical-bus, timing, display, disk, and Paula gate is an
  explicit eight-class matrix. Test counts may grow as theory rows are added;
  the gate must not use broad substring matching.

Measurements are not comparable unless the same build, profile, warmup, frame
range, presentation mode, disk image, and host conditions are used.

## Non-negotiable invariants

1. Chip RAM and custom-bus commits occur in nondecreasing granted-cycle order.
2. A DMA fetch observes the memory value present at its granted cycle.
3. A CPU write before a DMA fetch affects it; a write after the fetch does not.
4. CPU longword accesses remain two causal word operations.
5. A requester created by a slot cannot consume that same slot.
6. No access may commit behind `ExecutedThroughCycle`.
7. CPU execution never passes an unresolved read whose value can be changed by
   earlier DMA or custom-chip work.
8. Interrupts and CPU-readable custom state become visible at their documented
   cycles.
9. Presentation remains causal and writes directly to the bound framebuffer.
10. No frame-wide Chip RAM history, retrospective sampling, or storage
    proportional to Chip RAM size is permitted.
11. All journals, traces, and batch state are preallocated and allocation-free
    in steady state.
12. OCS/ECS behavior stays limited to six active planes while storage remains
    eight-plane capable. Unsupported AGA execution remains fail-fast.

## Terminology

### Executed bus frontier

The latest cycle through which all relevant bus owners and causal effects have
been committed. This is the existing authoritative execution horizon.

### CPU speculative frontier

The CPU cycle reached while executing operations that are proven not to depend
on uncommitted chipset state. It may be ahead of the executed bus frontier.

### CPU visibility horizon

The earliest cycle at which an uncommitted event could change CPU-observable
state or invalidate a speculative CPU result.

### Hard barrier

An event that requires the bus executor to catch up before CPU execution can
continue. Examples include an unresolved Chip RAM/custom read, an interrupt
becoming observable, a write-journal dependency, or an unsupported operation.

### Soft event

An event that can be recorded chronologically without immediately blocking CPU
execution, such as an eligible write whose later dependencies are tracked.

### Batch fallback

Immediate termination of the current batch followed by the existing scalar
causal path. Fallback is always valid and is the recovery mechanism for unknown
or unsupported cases.

## Target architecture

The AccurateM68000 interpreter asks the bus executor for a CPU visibility
horizon. It then executes only operations admitted by the active batching
stage. CPU events are either committed immediately, appended to a bounded
chronological journal, or treated as hard barriers.

At a barrier or horizon:

1. Stop CPU speculation.
2. Merge journaled CPU events with persistent DMA intents and fixed line plans.
3. Execute chronologically through the required cycle.
4. Resolve pending reads and CPU-visible state.
5. Dispatch interrupts.
6. Clear or compact the consumed journal prefix.
7. Resume the CPU from the new committed frontier.

The executor remains the sole owner of arbitration and Chip RAM sampling.

## Stage 0 — Freeze baselines and add a repeatable gate

Goal: make later performance and correctness decisions reproducible.

### Work

- [x] Record the exact git status and identify unrelated user changes that must
      not be modified.
- [x] Repair or wait for any unrelated build break before recording full-suite
      baselines.
- [x] Add a checked-in PowerShell gate script dedicated to this project.
- [x] Make the script build Release binaries once and run benchmarks with
      `--no-build` afterward.
- [x] Pin interpreter, hardware specialization, presentation mode, profile,
      warmup, measured frames, and repeat count.
- [x] Record machine/runtime information with every result.
- [x] Establish baseline commands for:
  - Lemmings SR, including the established 360-frame checksum run.
  - Hired Guns after enough warmup to reach the intended Workbench/game phase.
  - Shadow of the Beast using the reported host-exec profile.
  - Workbench 1.3 as a chipset-light control workload.
- [x] Store baseline results in a small append-only Markdown or TSV file.

### Required diagnostics

- CPU instructions and cycles.
- Scheduler drains and bus-access drains.
- Agenda reads and updates.
- Batch attempts, accepted batches, instructions, cycles, and fallbacks.
- Average and percentile batch length.
- Barrier counts by reason.
- Fixed and dynamic DMA grant mix.
- Framebuffer, audio, disk, and workload checksums.
- Allocated bytes after warmup.

### Exit gate

- [x] Three consecutive benchmark runs have stable checksums.
- [x] Median FPS variance is understood and documented.
- [x] The focused regression filter and full build result are recorded.

### Rollback

No production behavior changes are allowed in this stage.

## Stage 1 — Executor-owned visibility-horizon diagnostics

Goal: measure batching opportunities without changing execution.

### Work

- [x] Add `GetNextCpuVisibilityHorizon(...)` as a read-only executor query.
- [x] Compute the minimum of CPU-visible interrupt, CIA, Paula, disk, Copper,
      blitter, raster, control-event, and externally supplied boundaries.
- [x] Keep source deadlines persistent and versioned; do not rescan every
      device after each CPU instruction.
- [x] Add an O(1) agenda result containing both cycle and reason.
- [x] Add counters for potential cycles and instructions available before each
      horizon.
- [ ] Record why a candidate batch would be rejected under each later stage's
      admission rules.
- [x] Do not execute speculative CPU instructions yet.

### Tests

- [x] Horizon returns the target cycle when no earlier event exists.
- [x] Every interrupt source shortens the horizon correctly.
- [ ] Copper WAIT/BFD and pending MOVE boundaries are represented.
- [x] Disk WORDSYNC, index, active transfer, and status-read boundaries are
      represented without passive-byte false wakes.
- [ ] Paula reload and interrupt boundaries are represented.
- [ ] Blitter completion and nasty-mode boundaries are represented.
- [ ] Mid-line register writes invalidate only affected deadline leaves.

### Exit gate

- [x] Shadow diagnostics do not mutate execution or checksums.
- [x] Horizon queries are materially cheaper than the scheduler work they are
      intended to replace.
- [x] Hired Guns and Workbench show useful horizon lengths.

### Rollback

Diagnostics can be disabled independently; no production path is changed.

## Stage 2 — Pure ROM/Fast-RAM interpreter batches

Goal: recover the safest and highest-value portion first.

### Admission

A batch may continue only while:

- Instruction fetches come from ROM or genuine non-chip Fast RAM.
- Data accesses are ROM/Fast RAM and side-effect-free.
- No CIA, custom-register, autoconfig, RTC, host trap, or mapped-device access
  occurs.
- No exception, STOP, RESET, trace, privilege transition, or interrupt entry
  occurs.
- No CPU Chip RAM access occurs.
- The CPU cycle remains below the visibility horizon.

### Work

- [x] Make the existing deferred interpreter batcher executor-owned rather than
      scheduler-owned.
- [x] Request one visibility horizon at batch entry.
- [x] Execute scalar AccurateM68000 instructions normally inside the batch; no
      JIT or alternate CPU semantics.
- [x] Avoid per-instruction `AdvanceHardwareEventsTo` calls inside an admitted
      batch.
- [x] Advance the executor once at batch termination.
- [x] Dispatch pending interrupts before the next instruction.
- [x] Fall back before the first unsupported instruction or access.
- [x] Enable behind an internal option initially.

### Tests

- [x] Multiple ROM instructions produce one hardware boundary call.
- [x] Multiple Fast-RAM instructions produce one hardware boundary call.
- [x] Interrupts stop the batch at the exact CPU-visible boundary.
- [x] Exceptions and traps terminate before architectural exception handling.
- [x] A transition into Chip RAM terminates before the first Chip fetch.
- [x] Self-modifying Fast RAM invalidates any relevant decode cache correctly.
- [x] Scalar-on and batching-on states, cycles, checksums, and bus traces match.

### Performance gate

- [x] Hired Guns and Workbench improve without harming Lemmings SR by more than
      2% outside normal variance.
- [x] Boundary calls saved approximately equal batched instructions minus
      batches.
- [x] Zero steady-state allocations.

### Rollback

Disable the internal option; scalar execution remains authoritative.

## Stage 3 — Preallocated chronological CPU event journal

Goal: let the CPU cross selected non-read bus events without immediate chipset
catch-up.

### Data structure

Add a fixed-size ring of compact entries containing:

- Requested cycle and instruction phase.
- Target and address.
- Access kind and size.
- Read/write classification.
- Write value where applicable.
- Ordering sequence.
- Dependency flags.
- Completion/grant fields populated by the executor.

The ring must be preallocated, bounded, allocation-free, and reset without
clearing unnecessary storage. A full ring is a hard barrier, not an error.

### Initial scope

- [x] Journal only operations proven not to require an immediate return value.
- [x] Start with a narrow, explicitly enumerated class of CPU writes.
- [x] Do not journal custom-register writes initially.
- [x] Do not journal Chip RAM reads initially.
- [x] Flush before exception entry, interrupt entry, reset, STOP, host calls,
      tracing, or externally visible callbacks.

### Execution

- [x] Insert CPU events into the executor agenda without granting future slots.
- [x] At flush, arbitrate journal entries chronologically with fixed and dynamic
      intents.
- [x] Commit memory only when the executor reaches the granted slot.
- [x] Preserve both word phases of longword writes independently.
- [x] Make a later overlapping CPU read or write a dependency barrier.
- [x] Make a later instruction fetch from a modified page a dependency barrier.

### Tests

- [x] DMA fetch before journaled CPU write sees the old word.
- [x] DMA fetch after journaled CPU write sees the new word.
- [x] Two CPU writes preserve program order after arbitration delays.
- [x] Longword halves remain separately observable by intervening DMA.
- [x] Overlapping byte/word/long accesses flush correctly.
- [x] Ring-full fallback is deterministic and allocation-free.
- [x] Exceptions do not leave journaled writes uncommitted or duplicated.

### Exit gate

- [x] Journaled and scalar traces match owner, cycle, address, kind, value,
      pointer state, and interrupt state.
- [x] No historical reads or replay of archived memory values is introduced.

### Rollback

Disable journal admission; the journal implementation may remain dormant.

## Stage 4 — Read-only DMA segments

Goal: defer chipset work across intervals where all pending DMA effects are
read-only with respect to CPU-observed memory.

### Classification

Classify each active requester conservatively:

- Display and sprite fetches: memory readers, presentation/state writers.
- Paula playback DMA: memory reader, audio/interrupt state writer.
- Copper fetch: memory reader; decoded MOVE/WAIT is a later control barrier.
- Disk read-from-memory mode: memory reader.
- Disk write-to-memory mode: memory writer.
- Blitter source-only phase: memory reader.
- Blitter destination phase: memory writer.
- CPU write journal: memory writer.

Any unknown state is classified as a writer/barrier.

### Work

- [x] Add a maintained `MayWriteChipRamBefore(cycle)` summary to the executor.
- [x] Derive it from persistent intents and micro-operation phase, not repeated
      whole-device scans.
- [x] Allow safe CPU Chip RAM reads to sample current memory only when no earlier
      writer can exist and ownership timing is known.
- [x] Preserve arbitration timing from fixed line plans and persistent intents.
- [x] Flush all deferred read DMA before a CPU or DMA memory write that could
      change a previously scheduled fetch value.
- [x] Capture presentation/audio/Copper latch values while executing the batch,
      never retrospectively after a later write.

### Tests

- [x] CPU reads cross display-only DMA without extra scalar drains.
- [x] A following CPU write flushes earlier display fetches first.
- [x] Active disk-to-memory and blitter destination phases block read deferral.
- [x] Paula playback and sprite fetches retain exact values and pointer timing.
- [x] Copper self-modifying-list cases retain fetch-before-write behavior.

### Exit gate

- [ ] Chip-RAM-heavy workloads show longer batches.
- [ ] Lemmings SR checksum and visual output remain unchanged.
- [ ] Presentation allocations remain zero.

### Rollback

Disable read-DMA deferral independently of ROM/Fast batching and the write
journal.

## Stage 5 — Selected custom-register and CIA interactions

Goal: expand batching only where CPU-visible semantics are fully modeled.

### Work

- [ ] Create an explicit access table: immediate barrier, journalable write,
      side-effect-free read, or unsupported.
- [ ] Start with benign write-only pointer/data registers whose effects are
      already represented as causal control events.
- [ ] Keep beam, interrupt, disk-status, audio-status, collision, and open-bus
      reads as hard barriers until proven otherwise.
- [ ] Treat DMACON, INTENA, INTREQ, ADKCON, DDF, DIW, BPLCON, sprite control,
      Copper jumps, blitter start, and disk start as schedule-changing events.
- [ ] Apply schedule-changing writes to the unexecuted suffix only.
- [ ] Keep CIA timer/TOD/ICR semantics as CPU-visible barriers unless a dedicated
      latched-read model proves safe.

### Tests

- [ ] Same-line DDF/DIW/BPLCON/palette behavior.
- [ ] Copper jump and WAIT/BFD behavior.
- [ ] Blitter final-slot completion and nasty mode.
- [ ] Disk WORDSYNC and DSKBYTR visibility.
- [ ] Paula reload and interrupt timing.
- [ ] Beam-position polling loops and IRQ entry probes.

### Exit gate

- [ ] Every admitted register has focused before/after-barrier tests.
- [ ] No default-admitted catch-all register class exists.

## Stage 6 — Default enablement and cleanup

Goal: make horizon batching the normal AccurateM68000 path.

### Work

- [ ] Run shadow comparison for all benchmark workloads and timing corpora.
- [ ] Enable Stage 2 by default first.
- [ ] Enable later stages independently after their gates pass.
- [ ] Retain per-stage kill switches during stabilization.
- [ ] Remove duplicate scheduler wake scans made obsolete by executor horizons.
- [ ] Remove experimental verification paths only after production parity.
- [ ] Keep the compact diagnostic ring and counters.
- [ ] Update architecture documentation and benchmark expectations.

### Final test matrix

- [ ] Focused physical bus ledger tests.
- [ ] Display/bitplane/sprite conformance matrices.
- [ ] Copper conformance and WAIT/BFD tests.
- [ ] Blitter conformance, nasty mode, and final-slot tests.
- [ ] Paula audio and interrupt tests.
- [ ] Disk controller and WORDSYNC tests.
- [ ] CPU bus timing, longword, exception, and interrupt-entry tests.
- [ ] OCS PAL, OCS NTSC, ECS PAL, ECS NTSC.
- [ ] Complete `CopperMod.Amiga.Tests` suite.
- [ ] Complete CopperScreen test suite.
- [ ] CopperScreen visual regression filters.
- [ ] VAmigaTS timing corpus gates used by the project.

### Final performance matrix

- [ ] Lemmings SR: established checksum and no material regression from the best
      accepted causal baseline.
- [ ] Hired Guns: stable phase-specific measurement and material improvement.
- [ ] Shadow of the Beast: sustained real-time audio without stutter.
- [ ] Workbench: substantial recovery toward historical chipset-light speed.
- [ ] Zero steady-state presentation and batching allocations.
- [ ] Scheduler/agenda time reduced by at least 50% from the pre-batching causal
      baseline in the targeted chipset-light workloads.

## Per-stage working procedure

Every implementation session should follow this order:

1. Select exactly one unchecked item or tightly coupled group.
2. Record the pre-change benchmark and relevant counters.
3. Add or identify a focused correctness test before enabling production use.
4. Implement behind the narrowest possible admission rule.
5. Run the focused test immediately.
6. Run the explicit causal/disk/Paula/display class matrix.
7. Run Lemmings SR and the workload targeted by the change.
8. Compare checksums, grants, drains, agenda operations, allocations, and FPS.
9. Revert the attempted optimization if it is slower or broadens behavior
   without proof.
10. Mark completed checkboxes and append a dated progress entry below.

Do not combine semantic expansion, performance restructuring, and cleanup in
one stage.

## Progress log

Append entries; do not rewrite history.

### 2026-07-21 — Plan created

- Documented the staged CPU-visible horizon design.
- Recorded the current causal invariants and reference performance range.
- Implementation remains intentionally unstarted under this plan.

### 2026-07-21 — Stage 0 gate scaffolded; causal regression found

- Added `scripts/run-cpu-visible-horizon-gate.ps1` and the append-only
  `CPU_VISIBLE_HORIZON_BASELINES.md` result log.
- Added the exact Shadow of the Beast PNA host-exec workload and made unknown
  `--only` selectors fail instead of silently running the full catalog.
- The quick four-workload wiring gate and Release benchmark build pass.
- The first full gate deterministically failed during Lemmings SR warmup after
  frame 105: a CPU DMACON write granted at cycle 14924806 reached Denise after
  display capture had already committed through cycle 14924808. Stage 0 remains
  open until this pre-existing causal-ordering regression is corrected and the
  stable three-run gate completes.

### 2026-07-22 — CPU custom-write commit boundary investigated

- Added a focused consecutive-DMACON/refresh regression; it and the two existing
  bitplane-DMACON causal tests pass with the protective production path.
- Confirmed that carrying the word value into the granted-slot scheduler and
  dispatching register side effects before it returns is not yet safe: Denise
  repeatedly reconciles the same-cycle pending write in
  `RefreshStartedLiveLineDmaState`. Hang dumps prove this is active
  same-cycle convergence, not a test-host deadlock.
- Restored the passing protective path after the experiment. The next change
  must separate executor slot commitment from Denise event publication, then
  perform post-slot control effects exactly once after the scheduler leaves its
  pending-CPU scope.
- The full Lemmings run remains deterministic without the horizon exception at
  framebuffer/audio checksums `0x5BC9B11D`/`0xE3B5811D`, but these are not
  accepted baselines because the established `0x6C7AC40D`/`0xCF07D11D` gate
  still fails.

### 2026-07-22 — Denise coverage horizon proven distinct from causal state

- A clean detached build of committed `HEAD` (`fc9d3779`) reproduces the same
  frame-105 DMACON/display-horizon exception, proving it is present in the
  committed code rather than introduced by unrelated dirty worktree files.
- `_liveCycle` and `_liveCapturedThroughCycle` were audited as scan/coverage
  cursors. They advance across refresh, plan preparation, and target
  fast-forwards and therefore cannot serve as a causal Denise-state horizon.
- Two narrower experiments were rejected and reverted: forcing every CPU write
  beyond coverage produced `0x2B3286DD` at 11.5 FPS; admitting/applying the
  refresh-only late event produced `0x99EF9705` at 9.5 FPS. Both broadened
  scheduling work and failed the established checksum gate.
- The next implementation must add a separate causal Denise commit horizon and
  mark only actual register/Copper effects, granted display DMA captures,
  immutable line snapshots, and finalized rows. Coverage/wake logic remains on
  the existing cursor. CPU custom writes must then publish before any later
  causal display commit.

### 2026-07-22 — Denise causal and finalized horizons separated

- Added distinct Denise horizons for speculative display coverage, committed
  display state, and finalized presentation pixels. Refresh-only coverage no
  longer rejects an earlier register event, while committed bitplane DMA and a
  finalized rasterline remain irreversible.
- Added focused regressions for refresh-only coverage, committed display DMA,
  finalized rows, and consecutive CPU DMACON writes around a refresh slot. All
  five focused cases pass with the original pre/post CPU-access drains restored;
  the temporary custom-write drain bypasses are no longer present.
- The 360-frame Lemmings workload now completes without the frame-105 horizon
  exception. It remains outside the acceptance gate at framebuffer/audio
  checksums `0x9B2A3039`/`0x2EBC091D` versus the established
  `0x6C7AC40D`/`0xCF07D11D`. The same hashes occur with and without the former
  executor-committed post-grant drain bypass, so that bypass is not the source
  of the remaining semantic divergence.
- The current gate script's broad substring filter selects 2,421 tests rather
  than the intended focused matrix and reports 103 failures, including unrelated
  JIT tests. Stage 0 remains open; next work is to isolate the first divergent
  CPU custom-register/display event and correct the gate's explicit test set.

### 2026-07-22 — First causal CPU/Copper divergence isolated and fixed

- Replaced the gate's namespace-wide substring expression with an explicit
  eight-class physical-bus/conformance matrix. It selects 1,208 theory cases;
  1,141 pass, two skip, and the remaining 65 failures are all localized to
  `AmigaBusTimingTests` rather than unrelated JIT tests.
- Added allocation-free per-frame framebuffer checksums to benchmark progress
  diagnostics. A legacy/causal comparison found the first differing output at
  frame 95 and the first differing bus decision at cycle 13470208.
- Fixed the chronological CPU-wait retry loop so a Copper-owned HRM half-pair
  also consumes its adjacent CPU opportunity. The prior causal fast path granted
  a `VPOSR` read in that adjacent slot, contradicting the existing physical-pair
  test and the HRM allocator. A focused custom-register collision regression
  now covers this path.
- Frames 1 through 120 are pixel-identical between legacy and causal execution
  after the fix. The old legacy trace later grants a CPU read and then replaces
  that same physical slot with Copper, so its checksum is not by itself proof of
  correct ownership. The 360-frame causal run remains outside the old checksum
  gate (`0x03E3F1D1`/`0xFA40F11D`); further single-owner divergences must be
  removed or adjudicated against hardware tests before freezing a new baseline.

### 2026-07-22 — Focused regression gate made explicit

- Replaced the broad substring filter with an explicit fully-qualified
  eight-class matrix covering the physical bus ledger, CPU bus timing, and the
  bitplane, sprite, Copper, blitter, disk, and Paula conformance matrices.
- The generated filter is written to the result directory so each gate run is
  reproducible. In particular, the bare `Copper` token can no longer match the
  `CopperMod` namespace and select unrelated JIT tests.
- The first explicit run selected 1,207 current theory cases: 1,141 passed, two
  were skipped, and 64 failed. All failures are in `AmigaBusTimingTests`; no JIT
  test was selected. These are now a bounded timing-regression set rather than
  contamination from an assembly-wide filter.

### 2026-07-22 — Copper retry parity and deterministic causal baseline

- Audited every causal CPU retry path against the HRM allocator. All byte,
  word, longword, read, write, fast, and fallback paths now advance through the
  shared owner-aware bus horizon, including Copper's adjacent half-slot. The
  core Release build and four focused CPU/Copper ordering tests pass.
- Three consecutive 600-warmup/360-measured interpreter runs produced identical
  framebuffer/audio checksums `0x03E3F1D1`/`0xFA40F11D`, identical grant mix,
  and 968 measured allocated bytes. This freezes deterministic evidence, but
  does not yet adjudicate the old legacy checksum: the legacy trace permits a
  same-slot CPU grant to be replaced by Copper and violates the single-owner
  invariant.
- Observed throughput was 26.2, 19.3, and 15.8 FPS in one serial run. The large
  monotonic drift makes that run unsuitable as an absolute performance
  baseline. Every emulated-work counter (drains, grants, device events, display
  rows, and allocations) is identical across repeats, so the variance is
  host-level throughput drift rather than different emulator work or state.
  Later stage comparisons must use interleaved or fresh-process medians.
- The remaining 65-test failure set was classified. Sixty-three failures are
  shared with the legacy executor. A high-confidence passive-disk publication
  defect was isolated and corrected in the scheduler: custom-register barriers
  may now publish passive disk latch progress without inventing a chronological
  disk deadline. All five focused inclusion/exclusion tests pass.
- The post-fix explicit matrix passes 1,146 of 1,208 cases with two skips and 60
  failures; the legacy executor passes 1,144 with 62 failures. There are no
  causal-only failures. Legacy alone fails the focused blitter completion-slot
  ownership test and the Agnus display-control mirror test. The full Release
  solution build succeeds with zero warnings and zero errors.
- The deterministic `0x03E3F1D1`/`0xFA40F11D` pair is now the causal gate. The
  retired `0x6C7AC40D`/`0xCF07D11D` trace violates physical single ownership by
  replacing a committed CPU slot with Copper, while the causal executor has no
  unique matrix regression.

### 2026-07-22 — Stage 1 visibility agenda established

- Added an executor-owned `CpuVisibilityHorizon` carrying the absolute cycle,
  winning reason, and disk subreason. It includes pending/future interrupts,
  VBlank, CIA-B TOD, CIA timers, Paula, disk, Copper, pending Agnus control,
  blitter, target, and an externally supplied boundary with strict target-tie
  semantics.
- Added a separate fixed 16-leaf tournament for each 68000 interrupt mask.
  CPU-visible deadlines are deliberately separate from raw bus-eligibility
  leaves. Source versions suppress unchanged device scans; a warmed query is
  one root read, performs no leaf updates, and allocates zero bytes across
  1,000 queries.
- Added a dedicated Paula CPU-interrupt-visibility version after Hired Guns
  exposed release-cycle changes that do not alter the register wake version.
  The corrected 20-frame Hired Guns run records 12,152/0 shadow
  matches/mismatches. Workbench records 21,153/0 and approximately 1.506
  billion candidate cycles, proving large ROM execution opportunities.
- Shadow diagnostics remain opt-in through
  `COPPERMOD_AMIGA_CPU_VISIBILITY_SHADOW=1`; the legacy scheduler still selects
  every production horizon. Quick Lemmings, Hired Guns, and Workbench output
  and grant counters remain unchanged. Executor counters are included in the
  scheduler snapshot and benchmark status line.
- Focused coverage now includes quiescent target, VBlank, CIA timer and pending
  IRQ, Paula interrupt masks and writes, Copper WAIT, pending control, nasty
  blitter transfer, external boundary, affected-leaf updates, allocation-free
  warmed roots, disk WORDSYNC/index/active-transfer visibility, and
  architectural non-mutation.
- Added potential-cycle/instruction and short-horizon counters. In the final
  20-frame shadow gate, Workbench exposes 376,401,911 potential interpreter
  instructions and Hired Guns exposes 213,997,831, with only one and five
  short-horizon candidates respectively.
- Replaced broad diagnostic invalidation with mutation-driven source refresh.
  Workbench now performs 244 source refreshes for 21,153 root reads; Hired Guns
  performs 135 for 12,152. Both workloads retain zero shadow mismatches.
- Corrected query-cost instrumentation so executor timing excludes shadow
  comparison bookkeeping. The executor query costs 78,178 ticks versus
  144,940 for the legacy Workbench query (46% lower), and 35,884 versus 71,726
  for Hired Guns (50% lower). Stage 1's direct-query cost gate is therefore
  satisfied. Remaining detailed coverage (Copper BFD/pending MOVE, Paula
  reload, blitter completion, and additional targeted invalidation sources)
  can be added alongside the consuming stages without blocking Stage 2.

### 2026-07-22 — Stage 2 pure ROM/Fast-RAM batching completed

- The existing AccurateM68000 deferred interpreter path now consumes the
  executor-owned `CpuVisibilityHorizon` directly. The legacy scheduler query no
  longer selects a production batch horizon. The feature remains opt-in via
  `--cpu-deferred-bus-batch`.
- Restored admission after the CPU-core change that made `ExecuteInstruction()`
  strictly scalar. The explicit `ExecuteInstructions(...)` path may now admit a
  batch with a cancellable successor prefetch pending. While a batch is active,
  an already-scheduled ROM/Fast-RAM successor is materialized without touching
  Chip RAM; an ineligible window terminates the batch before that fetch.
- Separated the older Chip-RAM MUL/DIV internal-no-bus optimization from the
  pure batching flag. It now has its own internal opt-in, so Lemmings does not
  execute unrelated Stage 3+ behavior when Stage 2 is measured.
- Fifteen focused Stage 2 tests pass: ROM and Fast-RAM boundary reduction,
  pending-interrupt rejection, scheduled external boundaries, exception
  cleanup, trap and Chip-RAM transitions, passive-disk exclusion, scalar
  expansion-RAM parity, self-modifying Fast RAM, and zero steady-state
  allocation. The complete Copper68k suite passes 859/860; the sole failure is
  the pre-existing `PushLongWritesLowWordFirst` stack-address expectation.
- Stable Workbench measurements are host-noisy, but the interleaved
  off/on/off/on 600-warmup/360-frame run averages 31.95 FPS off and 38.2 FPS on.
  Hired Guns' quick median improves from 30.6 to 41.9 FPS. CPU sampling shows
  scheduler-drain exclusive time falling from 5.38% to 2.15%.
- Workbench records 2,096,318 instructions in 240,762 batches, with 2,003,745
  per-instruction boundary flushes skipped. Hired Guns records 303,640
  instructions in 17,649 batches. The allocation-focused synthetic test
  reports zero bytes.
- The 600-warmup/360-frame Lemmings Stage 2 run and its scalar control are
  exactly identical (`0x69B4F675` framebuffer, `0x20FF111D` audio) and execute
  zero batches. This current-tree scalar checksum differs from Stage 0's
  documented `0x03E3F1D1`/`0xFA40F11D`; because both Stage 2 modes agree and
  Stage 2 is inactive for this workload, the global baseline divergence is
  retained as a mandatory regression investigation before default-on rollout.

### 2026-07-22 — Stage 3 preallocated chronological CPU event journal completed

- Added a fixed 256-entry `CpuEventJournal` with chronological sequence,
  requested/granted/completed cycles, instruction phase, access metadata, and
  dependency flags. Reset and steady-state enqueue/flush reuse storage; the
  focused allocation test reports zero bytes.
- The executor currently admits only CPU Chip RAM word writes. Reads, byte
  writes, and custom-register writes remain barriers. Longword writes are
  represented by two separate word entries and never reserve future values.
- Journal flush replays requested cycles through the causal CPU word executor,
  accumulates arbitration delay into the CPU cycle, and commits memory only at
  the actual granted slot. A recursion guard keeps the replay on the production
  arbitration path without re-admission.
- The AccurateM68000 ROM batch path now exercises the journal: a word write and
  a longword write remain pending until a later dependent Chip RAM read ends
  the batch; the read observes both committed values. Direct FIFO, longword,
  out-of-order rejection, overlap-barrier, ring-full, reset-reuse, exception,
  STOP-boundary, host-gateway, modified-code-fetch, integrated ring-full, and
  allocation tests pass. Display-DMA tests also prove writes before
  a fetch supply the new word, denied writes after it preserve the old fetched
  word, and a fetch between longword halves observes the intervening value.
- Architectural exception entry now flushes deferred CPU timing before building
  its stack frame. This prevents exception-frame writes from joining an operand
  journal and leaves the original write committed exactly once. Copper68k still
  passes 860/861; only the pre-existing
  `PushLongWritesLowWordFirst` expectation fails.
- CPU reset, the RESET instruction's external-device callback, and STOP now
  establish an explicit deferred-timing boundary before publishing their side
  effects. The focused Copper68k exception/reset/STOP group passes 61/61.
- The executor agenda now carries the journal head deadline while admission is
  active, but that leaf cannot grant a future slot. Flush advances the leaf as
  each entry commits and removes it when the ring becomes empty.
- A scalar-versus-journal display-contention trace with two ordered CPU writes
  matches requester, kind, address, write classification, grant/completion
  cycles, fetched value, bitplane pointer state, final memory, and interrupt
  state. The second write inherits the first write's arbitration delay.
- The broader deferred-batch filter currently passes 42/50. Its eight failures
  are the already-open later-stage Chip-read/blitter/CIA expectations; the new
  Stage 3 journal-focused suite adds no failure. Stage 3's implementation and
  exit-gate checkboxes are complete; the remaining failures belong to Stage 4
  read-DMA execution and its existing experimental fast path.

### 2026-07-22 — Stage 4 write-hazard foundation

- Added executor-owned `MayWriteChipRamBefore(cycle)`. It combines the CPU
  journal head, persistent CPU/disk/blitter intents, disk transfer direction,
  and the blitter destination-enable phase.
- Disk and blitter summaries are refreshed only when their source wake version
  or DMACON version changes. Repeated quiescent queries perform no device-state
  refresh, avoiding a new whole-device scan in the CPU read path.
- Eleven focused tests cover quiescence, journal deadlines, persistent read and
  write intents, mutation-driven refresh, destination and source-only blits,
  disk-to-memory writes, and memory-to-disk reads. No CPU Chip RAM read is
  admitted yet; the summary is live but behavior remains scalar pending the
  read-segment implementation and parity gate.

### 2026-07-22 — Stage 4 CPU Chip-read segment and prefetch parity

- Enabled the narrow CPU-data-read segment only when the maintained executor
  summary proves no Chip RAM writer can occur through the batch horizon. Fixed
  display ownership and persistent dynamic intents continue to arbitrate each
  CPU word causally; a following CPU write drains the earlier display fetch
  before committing memory.
- Fixed a batch-entry pipeline discrepancy exposed by active Paula playback. A
  batch can begin with one ready opcode word and its sequential prefetch still
  cancellable. Batch readiness now materializes that pending sequential word for
  either a zero- or one-word queue, matching scalar retirement instead of moving
  the transfer to batch exit and adding four cycles.
- Focused Stage 3/4 coverage passes 32 tests. CPU Chip reads retain scalar cycle,
  register, display-fetch, Paula latch, pointer, address, and value parity;
  disk-to-memory and destination-blitter phases conservatively block admission.
  The complete Copper68k suite remains at its established 860/861 baseline, with
  only `PushLongWritesLowWordFirst` failing independently of this stage.
- Added a live sprite crossing test at the physical OCS sprite slot. Scalar and
  segmented execution match CPU cycles/data, sprite grant count and cycle,
  fetched address, and post-fetch pointer while the CPU batch remains active.
- Added a self-modifying Copper-list regression. The Copper fetch and MOVE latch
  the original value before the later CPU write changes list memory; scalar and
  segmented execution match cycles, grants, final Copper address/value, and MOVE
  value. This completes the focused Stage 4 correctness checklist.

## Definition of done

This project is complete only when all of the following are true:

- The AccurateM68000 interpreter advances chipset emulation in executor-owned
  batches to maintained CPU visibility horizons.
- Common ROM/Fast-RAM execution avoids per-instruction chipset updates.
- Admitted Chip RAM and control events preserve exact causal ordering.
- No retrospective memory reconstruction or future-value reservation exists.
- All enabled stages pass their focused tests and the complete regression
  matrix.
- Lemmings SR retains its established checksum.
- Hired Guns, Shadow of the Beast, and Workbench meet documented performance
  targets with stable audio behavior.
- Steady-state batching and presentation allocate no memory per frame.
- The legacy redundant wake/drain paths superseded by the executor are removed.
- This document's implementation checkboxes and final gates are marked complete.
