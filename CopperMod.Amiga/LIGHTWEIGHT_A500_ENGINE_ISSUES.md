# Lightweight A500 engine: issues to revisit

## Deferred host feature: CopperStart / Legacy restoration

2026-09-16: explicitly deferred by the user to unblock independent native host
publication. Legacy selection and CopperBench launch are unavailable in the
standard build. Their source remains retained. Restore via an optional adapter
as described in [the host boundary notes](../CopperScreen/COPPERSTART_RESTORATION.md),
without reintroducing CopperStart types into the common session interface.
This is a disclosed host feature limitation, not a Lightweight timing defect.

Final-build completion tracking is in
[Supported-v1 readiness](LIGHTWEIGHT_A500_SUPPORTED_V1_READINESS.md).
The 2026-09-15 live Lemmings session rechecks disk-2 replacement successfully;
the user confirms “Digger digs a hole,” closing live skill assignment. A separate
same-build session passes toolbar reset / native ROM / cracktro recovery.
All three final-build formal comparisons are INVALID / RERUN due to sustained
host interference, not an emulation defect or accepted performance regression.
The latest retry measured 324.94 versus 329.01 FPS (98.76%), with unchanged fingerprints
and zero allocations. The user subsequently stated “I accept this build” after
the one-off aggregate-host-load waiver was proposed (2026-09-15). Bounded live
and supported-v1 build acceptance are CLOSED, with final retention accepted by
owner exception. The three measurement labels remain INVALID / RERUN; policy
v2 and future gates are unchanged. No further rerun or host-load attribution is
required solely for this build's acceptance. Hardware uncertainties below remain
open. The subsequent separate authorization makes Lightweight the universal
default, with Legacy explicitly selectable and unsupported hardware rejected.
This does not close any missing feature or hardware uncertainty below.

Default-switch host repair (2026-09-15): Settings previously dropped chipset
and ROM-version fields when reconstructing a profile. Loading/restarting/saving
now preserve them, so unsupported configurations cannot silently become an
OCS / 1.3 machine. Focused round-trip/rejection checks pass; this host defect is
closed. The engine and CPU binaries are unchanged. Cutover validation and build
identity are recorded in the readiness document.

Updated: 2026-09-15 (Hired Guns pre-Workbench sprite-DMA corruption repaired;
subsequent blank-end scheduling retention PASS at335.05 FPS /101.82%;
held CIA interrupt/music sequencer stall repaired with382 tests;
rebuilt live music, sound effects and graphics user-confirmed correct;
bounded live acceptance closes with residual interlace instability deferred).

Scope: the independent lightweight A500 PAL OCS engine, Copper68k 68000,
512 KiB Chip RAM, 512 KiB slow RAM, and the read-only standard-ADF path.
This is a follow-up register covering the disk questions discussed after H6b2
and the CIA/TOD/input questions encountered in H6c. It is not an exhaustive audit
of the emulator.

## Decision: uncertainty is not automatically a blocker

Legacy implemented particular answers to these questions, but did not establish
every hardware edge conclusively. It also had useful conformance tests and
specific corrections. Neither "Legacy did it" nor a passing game proves that
a rule matches hardware; conversely, missing hardware proof does not by itself
demonstrate a bug in the new engine.

The earlier blanket requirement to answer every outstanding hardware question
before continuing was too strong. Apply the original project's distinction:

- **Confirmed bug:** reproducible behavior contradicts an established contract
  or supported hardware expectation. Fix it in the relevant milestone; do not
  excuse incorrect behavior as a performance optimization.
- **Unverified hardware behavior:** record the implemented assumption, its
  possible impact and the evidence needed. It may remain open during
  experimental development; it is not automatically a stop condition and must
  not be described as verified cycle correctness.
- **Unsupported feature:** an explicit implementation gap, not a proven timing
  defect. Prioritize it when the supported workload needs it. Keep unsupported
  execution visible; never silently fall back or omit requested benchmark work.
- **Performance issue:** assess separately with the documented workload and
  measurement policy. Correctness fingerprints cannot waive a failed speed
  gate, and faster execution cannot waive a demonstrated correctness failure.

Promote an uncertainty to a bug when evidence establishes a contradiction.
Revisit it when a supported workload exercises the edge, a focused test fails,
or useful primary/hardware evidence becomes available. Do not require broad
compatibility or exhaustive hardware research merely to continue building the
experimental machine.

Gate dispositions belong in the [execution plan](LIGHTWEIGHT_A500_ENGINE_PLAN.md),
not this register. H6b and H6c were subsequently accepted; on 2026-09-14 the
user accepted **H6d / experimental GO**. The H7 review closes the performance
stage at **343.10 FPS** on the complete native-gameplay workload. This is not
synthetic disk throughput, general hardware verification or a production switch.
The original eight disk uncertainties and the additional open edges below
remain open; acceptance does not turn assumptions into verified behavior.
The opt-in CopperScreen connection is implemented with automated native replay.
Host queued-audio discard on pause/reset/fault is repaired and focused tests
plus the native adapter replay pass. After desktop unlock, live native boot,
animated menu, mouse loader input, disk-2 replacement and reset recovery work.
Native Enter exposed **LWA-INPUT-004**, also reproduced in the accepted frozen
build; its premature SDR-write rejection is now repaired, with native
press/release and subsequent full gameplay replay passing. The subsequent
repaired live run reaches "Just dig!" gameplay, with moving lemmings and a
decreasing timer; Enter no longer stops the live menu. Live skill assignment
was subsequently user-confirmed ("Digger digs a hole") on the combined final
build; desktop access works. On 2026-09-15 the user confirmed Hired Guns
music is audible on both left and right outputs, but subsequently clarified
that the melody is missing and only background bass is heard. The initial
normal-stereo inference is withdrawn: **LWA-CIA-003** reproduces and repairs
a lost timer interrupt that stalls the music sequencer. The user subsequently
confirmed the repaired recording and then rebuilt live music/sound effects sound
correct. The bounded Hired Guns live session is accepted, with occasional
interlace instability retained as a non-blocking follow-up. The user confirmed
Hired Guns interlaced loading-screen presentation after manually opening the
disk and game icons. Its subsequent loading stall, LWA-EXEC-001, is now repaired;
the later blank output was traced to missing COPJMP read strobes, now repaired
(LWA-VIDEO-004). The subsequent HAM output gap is implemented: Hired Guns now
renders its loading artwork and animated credits, and a Space key event reaches
the main menu (LWA-VIDEO-005). No additional emulation fix was needed for that
transition. The subsequent bounded live presentation check is user-accepted;
this does not certify every game path, disk transition or interlace timing edge.
Visual presentation success does not establish interlace timing correctness.
This does not close any hardware edge below. Historical G6/G7 results are unchanged.

## Open follow-ups

### LWA-CIA-003 — Hired Guns music stalls after a lost held CIA interrupt

- **Type/status:** confirmed interrupt-line ownership defect; REPAIRED with
  focused tests and native replay. Repaired-recording listening ACCEPTED by the
  user on2026-09-15 ("It works correctly now"). After the combined-build restart,
  the user also confirmed correct live music and sound effects. The missing-melody
  defect's functional live confirmation is CLOSED; timing uncertainties remain.
- **Reproduction:** experimental PAL OCS,512KiB Chip +512KiB slow RAM,
  native Kickstart1.3 and Hired Guns disk1, unchanged native-main-menu script.
  Frozen engine`139EDEA68A38B60E0430DDF64A9EDABCE9E1C9EE900FDD65C142D6207516A905`.
  ROM SHA256`EE05862D8102A08436AC4056DA7D549DB31625C7D47B24DFB7B3C9A5C113CA53`;
  disk ZIP`3880F61D819113256FCDD887FAAD1FA0F29EA7B766B6A7642D1CB7BABD912716`;
  script`38829FB5061530DA07291EEA016355D4BBEEB7429DF60FB71C2CFA3E1C72A812`.
- **First traced stall:** during field6204, CIA-B ICR read at cycle881250340
  (PC`C02AE6`) returns`82` and clears the previous timer-B cause. Timer B
  underflows again at881272080 while the handler is still executing. At
  881305244 (PC`C0289E`) the handler writes`2000` to INTREQ. The old engine
  clears EXTER even though CIA-B has pending`02`, mask`07` and an asserted
  interrupt source. Further underflows cannot generate a fresh pending-bit edge,
  so the music sequencer never resumes. CPU/graphics/DMA continue running.
- **Observed audio:** all four DMA enable bits remain on. During sampled
  fields6500-11000 every channel's location, period and volume remain fixed;
  channel3 reads zero from the silent loop at`$100`. Stereo PCM stays nonzero,
  explaining why the previous nonzero-audio checks missed the defect.
- **Repair:** each CIA stores its asserted /IRQ latch, cleared by ICR read or
  reset, not by masking a cause after assertion. On a targeted INTREQ clear,
  retain requests from still-asserted CIA lines. The CPU/Copper register owner
  remains shared. No periodic polling, new event mechanism, per-cycle logging
  or CPU-core change. Seven focused checks; four lost-request cases fail on
  the preceding implementation. Full Lightweight suite382/382 PASS.
- **Evidence/authority:** [Commodore HRM appendix F, ICR](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0142.html)
  describes enabled interrupt assertion and release on ICR read. Supporting
  implementation evidence is WinUAE`cia.cpp`'s`rethink_cias` and the custom
  INTREQ re-evaluation path, not a hardware oracle. Exact sub-CCK synchronization
  and transient INTREQR/IPL behavior around acknowledgement remain unverified;
  tests establish the held-source contract, not a newly verified delay.
- **Repaired native result:** through11000 fields, no unsupported stop. For
  channels0/1/2/3 the sampled distinct location counts are5/7/5/6, period
  counts9/16/14/11, volume counts10/30/19/18; every channel has nonzero samples.
  This restores sequencer progress; the user has also confirmed the repaired
  stereo recording sounds correct, followed by explicit live music/sound-effects
  confirmation after restart. Exact timing edges are not established by listening.
  `.codex-tmp/hiredguns-audio-channels-20260915/` and
  `hiredguns-audio-irq-20260915/` contain baseline snapshots;
  `hiredguns-audio-trace-20260915.log` contains the bounded source-copy trace;
  `hiredguns-audio-fixed-20260915/` contains repaired snapshots and a20-second
  stereo WAV. Test logs are`hiredguns-audio-tests-before-20260915.log` and
  `hiredguns-audio-tests-final-20260915.log`, under`.codex-tmp/`.
- **Other workload/performance:** complete Lemmings replay retains previous
  CPU/hardware/output fingerprints and zero allocation. No new formal speed
  acceptance was attempted during the live emulator/build workload. Historical
  valid throughput evidence is unchanged; do not call the diagnostic FPS a
  paired regression result. Legacy/default routing is unchanged.

### LWA-VIDEO-006 — Garbage sprite during native Hired Guns pre-Workbench boot

- **Type/status:** confirmed early-blank sprite-DMA defect; REPAIRED with
  discriminating tests and native replays. Throughput retention PASS at99.08%
  in the clean retry; the broader sprite-comparator edges below are not verified.
- **Reproduction:** native Kickstart 1.3, PAL OCS, 512 KiB Chip + 512 KiB slow,
  Hired Guns disk1 and the existing native-main-menu script. No script input
  occurs before field2400. Frozen HAM engine SHA256
  `010B40B66A2D114B0C99009DE20AD8EBE098CF926BA323D49268477C7EA39579`.
  Raw field750 contains a garbage stripe at x682-713, y170-299, before Workbench.
  The raw engine reproduces it without CopperScreen, excluding host presentation
  as the cause of this particular artifact. The user has not yet confirmed
  whether this accounts for every visual glitch they observed.
- **Physical cause:** `LightweightSpriteDma.OnFrameStart` reopens descriptor
  fetching on line0. The native Copper list at `$420` correctly writes unused
  SPR2PT to the null descriptor at `$478` (`FE00 FF00 0000 0000`). Premature
  line0 sprite slots overlap that rewrite. In the traced field starting with
  completed-frame counter719, SPR2PTH is written at h=$1D; POS output at h=$20
  reads old `$1930`; CTL address `$1932` is already accepted when Copper writes
  SPR2PTL=`$0478` at h=$21. CTL completion at h=$22 overwrites that new pointer
  with `$1934`. The pointer drifts again each field instead of following the
  intended null descriptor. It eventually loads unrelated RAM as sprite control
  and pixels: POS/CTL/DATA/DATB=`$AAAA` on channel2, producing the stripe.
- **Evidence:** `.codex-tmp/hiredguns-pre-workbench-20260915/` captures boot
  through field2400; `hiredguns-pre-workbench-sprites-20260915/` adds every-field
  captures and channel state for720-780. The isolated source-copy probe at
  `.codex-tmp/hiredguns-sprite-diagnostic/` logs only the bounded pointer/control
  transitions. Its unmodified-behavior field750 BMP and Chip RAM hashes equal
  the frozen engine's (`C32EF163EFFC05F6C3DF54CC2BA195EC22C614686DF5BF758A33BF19F456B6C2`
  and `2AAFA482BB1A74A666A4DE996CBDFE44820AEA2EFE244BCCF5823D7017CBA023`).
- **Counterfactual:** only the temporary copied sequencer suppresses new sprite
  requests on lines0-24. Field750 then differs by exactly2180 pixels, all inside
  the stripe rectangle; channel2 retains null control throughout captured
  fields720-780. Native boot still reaches Workbench at field2400, with the same
  final BMP hash as the original run:
  `4424B13B0E8EC844DAA1D7AC054383D860923B88B5FFB3A8D88B5BFBD002AEC9`.
  No production engine/CPU/host source is changed for this investigation, and
  there is no throughput claim or regression-suite acceptance for this probe.
- **Authority/limits:** Commodore's HRM, Sprite Hardware, requires sprite-pointer
  rewriting during vertical blank and describes comparator-driven start/stop
  [source](https://bastya.net/AmigaDevDocs/hard_4.html). Supporting implementation
  evidence: [WinUAE custom.cpp, commit5db5720](https://github.com/tonioni/WinUAE/blob/5db572091715327ec6bcf784d43b6021495d25a9/custom.cpp)
  suppresses sprite requests while Agnus vertical blank is active, reloads
  control at its final line and ends PAL blanking at line25 (`sprstartstop`,
  `generate_sprites`, `generate_dmal`, vertical-counter logic). This is not a
  physical A500 trace proving every boundary or pointer-write collision rule.
- **Production repair (2026-09-15):** `TryAcceptInput` rejects new requests on
  PAL lines0-24. The existing field-start state preparation therefore cannot
  fetch controls until the line25 slots. Accepted output still completes first,
  with its original address, even after DMA disable. No pointer cache/reset,
  presentation suppression, new scheduling state, logging or allocations.
  CPU/Copper pointer-write precedence and all later sprite sequencing remain
  unchanged. Engine SHA256
  `61DB639BE38769F2E85D90E89C649C5560B550EF8D21C0147AA6663DF04FA81B`;
  frozen runner directory `.codex-tmp/lightweight-sprite-vblank-20260915/`.
- **Validation:**368 Lightweight tests PASS, zero skips. Three new cases
  (early-blank pointer rewrite and both312/313-line field wraps) failed before
  the repair and pass afterward; another checks late blank-period enable and
  accepted completion after disable. Existing slot/presentation checks now use
  legal line25 startup instead of incorrectly asserting line0 DMA. Native
  Hired Guns field750 matches the clean diagnostic BMP above; Workbench2400
  remains identical; menu9000 and training20648 complete without unsupported
  bypass and training has audio. Training's final recorded machine state equals
  the earlier replay;552 pixels differ within x510-589,y71-152, so output is
  not described as globally identical. Lemmings14520 still renders the same
  running-level scene/status with audio; timing/output fingerprints can change
  when invalid DMA contention is removed. Replays are in
  `.codex-tmp/hiredguns-sprite-repaired-probe/`.
- **Initial performance attempt (historical INVALID / RERUN):** unchanged native interval (600 gameplay warmup,
  3600 measured fields, intended three interleaved samples/build) with verified
  logical CPU2, mask4, SMT sibling3, Normal priority, Balanced plan and policy-v2
  telemetry. Concurrent external builds/tests and high measured contention
  invalidate the attempt. Reference sampleR1 was deliberately stopped; no
  completed candidate comparison or new FPS/allocation acceptance exists.
  `.codex-tmp/sprite-vblank-performance-20260915.log` preserves the evidence;
  the wrapper's final workload-failed exception reflects that intentional stop,
  not an emulation crash. Retry using the bounded comparison script
  `.codex-tmp/measure-sprite-vblank-20260915.ps1` and a fresh evidence directory.
- **Clean performance retry (2026-09-15): PASS.** Three interleaved samples per
  unchanged frozen build, same full native gameplay interval and policy-v2
  telemetry. Reference333.73/330.43/323.71 FPS, median330.43;
  repaired323.99/327.40/327.74 FPS, median327.40. Retention99.083%, above the
  active95% floor and200 FPS target;3.05437 versus3.02636 ms/field (+0.02801 ms).
  The0.917% lower median lies within the observed overlapping sample ranges;
  this does not prove zero cost, but shows no material regression under the
  prescribed gate. Spreads3.03%/1.15%, all six samples allocate zero bytes and
  repeat their own build's fingerprints. Host/placement checks valid throughout:
  verified P-core CPU2/mask4, sibling3, Normal priority, Balanced power plan.
  Evidence `.codex-tmp/sprite-vblank-performance-20260915-r2.log`; full protocol,
  hashes and fingerprints are in the plan's dated retry section. The earlier
  interrupted attempt remains invalid; no production routing or hardware
  correctness gate was changed by this throughput result.
- **Blank-end scheduling optimization (2026-09-15):** replaces the per-slot
  blanking guard with one field-relative first-legal-input cycle, applied only
  at DMA enable/pointer writes and field restart. It removes400 prohibited
  input visits per enabled PAL field without adding a check to normal slot
  advancement. Full/peripheral reset use the actual live field origin.
  All375 Lightweight tests pass, and all15 native Hired Guns BMP/state/Chip-RAM
  checkpoint hashes match the repaired reference. Fresh six-sample native
  Lemmings comparison:335.05 versus329.05 FPS, +1.823%, zero allocation,
  identical CPU/hardware/output fingerprints, valid host/spread checks.
  Engine`139EDEA6...7516A905` is also in rebuilt CopperScreen Release.
  The plan's direct sprite blank-end scheduling section records the complete
  evidence, including discarded development attempts. No hardware uncertainty
  or live host-input issue is closed by the performance result.
- **Remaining scope:** this repairs premature vertical-blank requests, not all
  sprite state-machine assumptions. Exact VSTART/VSTOP equality/reuse edges,
  starts on the reset line, manual POS/CTL interaction with Agnus and partial
  control-pair handling under stolen slots still need hardware-backed review.
  The current range-based later sequencing was not expanded or certified here.
  Live host-input acceptance is recorded separately under LWA-HOST-001.

### LWA-HOST-001 — Live Hired Guns appears stuck after F1 menu navigation

- **Type/status:** host dispatcher starvation defect reproduced and repaired;
  bounded live functional confirmation ACCEPTED by the user on2026-09-15 in
  response to the F1/responsiveness check. Not an engine deadlock.
- **Report (2026-09-15):** manual F1 advances the menu; the following F1
  appears to cause an infinite busy loop. The user clarified that the entire
  Windows window stopped responding, including keys; this is not merely a game
  waiting for a selection. CopperScreen was no longer running
  when investigated, so a live thread/input-state capture was unavailable.
- **Saved evidence:** `CopperScreen-20260914-230412-146788.log` shows entry
  into the `$C09870` menu loop at 00:15:37 and the `$C09940` player-count loop
  at 00:19:53. From 00:19:53 to 00:20:53, published frames advance from174659
  to176861 with zero audio-submit failures and no debugger/fault reported.
  Thus the saved interval is not the LWA-EXEC-001 stuck-frame mechanism.
  Published-state logging alone does not prove that Windows input remained
  responsive or that the user saw updated frames.
- **Guest loop:** verified against the native loaded-program image, `$C09944`
  through `$C0996A` compare the keyboard byte at `$C0A6C2` with raw F1-F4
  (`$50-$53`); the no-key branch returns to `$C09940`. `$C0F058` is an RTS.
  This is the game's ordinary player-count input wait, not by itself a CPU bug.
- **Bounded replay:** unchanged frozen HAM engine, same ROM/disk1/menu script.
  F1 press/release submitted at the same emulated cycle advances to New Game
  (field9300); a 25-field press reaches player count (field9625, PC `$C0994E`);
  another 25-field press leaves that loop and reaches Training (field9950).
  Further input starts Exploration loading (field10575, active disk DMA,
  no unsupported feature). Evidence: `.codex-tmp/hiredguns-key-interval-20260915/`,
  including input journal, raw images and machine snapshots. This does not
  reproduce Windows event delivery. Short input duration alone is not a proven
  explanation for the failed automated live taps.
- **Host mechanism:** `MainWindow.QueueFramePresentation` posted at Render
  priority, and `PresentQueuedFrame` requeued while frames remained pending.
  Avalonia 12.0.3 processes priorities above Input without its native-input
  yielding checks. With continuously available frames, the UI thread can keep
  presenting and logging while starving keyboard/mouse, paint and window messages.
  The old live log's roughly14-15ms presentation callbacks plus additional CRT
  animation work support this mechanism, but no incident thread dump exists.
  Source: [Avalonia 12.0.3 dispatcher queue](https://github.com/AvaloniaUI/Avalonia/blob/12.0.3/src/Avalonia.Base/Threading/Dispatcher.Queue.cs)
  (`ExecuteJobsCore`) and its `DispatcherPriority` definitions.
- **Repair:** post the existing coalesced presentation callback at Background
  priority, below Input. No new event queue, timer, sleeps, per-frame logging,
  engine work suppression or input stretching. Frame ownership, audio, CRT
  composition, CPU and chipset execution are unchanged.
- **Discriminating check:** real Avalonia dispatcher with a bounded chain of64
  frame callbacks and input queued during the first. Old Render priority serves
  input only after64; the repaired production priority serves input after1,
  with all64 frame callbacks still executed. The new test failed before the
  change; its repaired case and explicit old-priority control pass afterward.
  Focused host checks:74 PASS. Both initially skipped opt-in native checks
  subsequently PASS with the supplied native ROM, Lemmings disk1 and accepted
  level1 script configured: gameplay/output and keyboard press/release replay,
  14520 fields each. Combined selected coverage:76 PASS, no remaining skips.
- **Live follow-up:** combined repaired build/process6036, described in the
  execution plan. Pause and subsequent resume were observed. Asked to repeat
  F1 navigation and check responsiveness, the user reported the session works
  correctly, with only residual interlace instability. Accept the functional
  check without claiming instrumented post-F1 input latency. If a new hang is
  reported, capture host stacks rather than infer CPU failure from a guest PC.

### LWA-HOST-002 — Interlaced CRT mode flashes black during host pointer movement

- **Type/status:** host presentation scheduling/decay defect REPAIRED;
  general live graphics accepted. Residual intermittent interlace instability
  remains OPEN / non-blocking deferred follow-up. No chipset timing change.
- **Report (2026-09-15):** an interlaced display occasionally flickers to pitch
  black while the Windows pointer moves over the CopperScreen window.
  Progressive and StableWeave presentation do not execute this phosphor path.
- **Cause:** the LWA-HOST-001 repair correctly moved queued emulator-field
  presentation below Input priority. Pointer events can therefore defer a fresh
  field. `CrtPhosphorComposer` independently continued applying its 24 ms
  half-life on animation ticks using host wall time; without a new field the
  visible contribution falls to 5.6% after 100 ms and 0.31% after 200 ms.
  This made host scheduling delay look like loss of the emulated video signal.
- **Repair:** every submitted field records its expected PAL/NTSC end timestamp.
  Decay clamps at that boundary, holds the composed output and stops requesting
  animation frames. A fresh field resets the timestamp and resumes decay. The
  Background presentation priority and input-responsiveness repair remain intact.
- **Validation:** the new bounded-decay/resume test fails under unbounded decay
  and passes with the repair. Focused presentation and real-dispatcher checks
  pass 8/8. Full `CopperScreen.Tests` result: 348 PASS, 11 optional fixture tests
  SKIP, 0 FAIL (359 total, 7m53s). The skipped native-media tests are unavailable
  coverage, not PASS. The user subsequently reports correct live graphics but
  interlace that is not always steady. This does not distinguish the remaining
  symptom from the original pointer-induced blackout or establish its cause.
- **User disposition (2026-09-15):** accepts the functioning session with this
  caveat. Do not block the bounded acceptance or assume no improvement is
  possible. A future focused investigation should first distinguish inherent
  interlace flicker, host presentation cadence and incorrect field sequencing;
  no new diagnosis, workaround or hardware-timing claim is made here.
- **Performance scope:** no engine hot path, framebuffer ownership, audio or
  emulated timing changed. Holding the boundary also avoids repeated phosphor
  packing and framebuffer uploads until the next field; no FPS claim was made.

### LWA-EXEC-001 — Native Hired Guns stalls after its interlaced loading screen

- **Type/status:** confirmed lightweight CPU/clock integration defect; REPAIRED
  with focused tests and native replay. Full Hired Guns compatibility remains open.
- **Profile/reproduction:** native Kickstart 1.3, PAL OCS / 68000, 512 KiB Chip
  + 512 KiB slow, standard read-only DF0 ADF. Hired Guns v1.08.39.25 disk 1 ZIP
  SHA256 `3880F61D819113256FCDD887FAAD1FA0F29EA7B766B6A7642D1CB7BABD912716`.
  Open the Workbench disk icon, double-click the game icon, accept SystemTakeover.
  The user performed these steps and confirmed working interlace before the
  loading failure. This is native-ROM evidence, not CopperStart behavior.
- **Observed state (2026-09-14):** black screen, last published field 4034,
  PC `$C1A17E`, last-PC `$C1A186`, DF0 46.1 MSP. No visible fault banner.
  Process 16508 remained responsive and consumed CPU; repeated published state
  did not advance. Minidump worker stack: `ReadWordRaw -> ExecuteFrame ->
  RenderNextFrame -> RenderFrames`. Last displayed PC is not necessarily the
  faulting instruction. An unsupported-feature stop was not demonstrated.
- **Build/evidence:** engine SHA256
  `8C6FFCA97305BAE35CE065BB4BFC15E84E1B6B35F039214CBBD9B8C3C62A0569`;
  `.codex-tmp/lightweight-hiredguns-stall-20260914.dmp` and matching `-stacks.txt`.
  Larger heap capture failed with PARTIAL_COPY; do not treat it as valid evidence.
- **Cause proved:** read-only live-state inspection found completed field 4041,
  canonical cycle 574314532, next boundary 574314540, CPU cycle 574314544,
  PC `$C0288C`, SR `$2600`. Interrupt entry had retired 12 internal cycles after
  its last bus access, but the adapter had not advanced hardware through them.
  The frame loop could neither complete the field nor execute an instruction
  because CPU cycles were already beyond its target. The deterministic scripted
  old-build replay stalls at field 4046 with the same condition: hardware
  575086784, boundary 575086794, CPU 575086796. This second occurrence precedes
  interlace, excluding interlace as a necessary trigger.
- **Repair:** `DispatchPendingCpuInterrupt` advances the existing canonical clock
  to CPU retirement after accepted interrupt entry. No CPU timing, bus-phase,
  requester, media or presentation rules are changed; no hot-path diagnostics
  added. Copper68k binary hash is unchanged. Three synthetic line/short-field/
  long-field cases fail before the fix and pass after; all 345 lightweight tests
  pass. Fixed native runs complete 6500 and 12000 fields, without unsupported
  bypass, and have identical state/image hashes at common checkpoint 6480.
  Focused host/presentation/audio tests also pass 41/41, including both native
  Lemmings adapter replays; its CPU/hardware/output fingerprints remain unchanged.
- **Reproducer:** runner workload `Workloads/hiredguns-native-launch-exploratory.json`
  (SHA256 `14C399AC3942AFB26ACA9F70BDB77D1C61356F1890828F47F8C34240E5C63E68`)
  opens the disk/game icons, confirms takeover and selects Continue with English.
  Fixed engine SHA256 `A5CD099B1EF2275B712741B17EAC318676783DE140DB19389E4BC6A366B46A25`.
  Evidence: `.codex-tmp/lightweight-hiredguns-live-state2-20260914.txt`,
  `lightweight-hiredguns-scripted-stall-20260914.txt`,
  `hiredguns-launch-before2-20260914.log`, `hiredguns-launch-fixed2-20260914/`,
  and `hiredguns-launch-fixed-long-20260914/` with matching logs.
- **Disposition:** retain the user-confirmed visual interlace result separately;
  the engine freeze is repaired, not a Hired Guns gameplay pass or a closure of
  uncertain hardware edges. No title-specific workaround or default switch.

### LWA-VIDEO-004 — Hired Guns later output mostly blank after progress repair

- **Type/status:** confirmed missing COPJMP read-strobe behavior; REPAIRED.
- **Evidence:** the repaired deterministic launch completes 12000 fields. Captured
  field 6500 shows a blue band; field 12000 is black. Both sample native PC
  `$C093E0`, BPLCON0 `$B004`, DIW `$6E81/$B6C1`, DDF `$003C/$00D4`, modulos
  80/80, nonzero audio, no unsupported feature, DF0 deselected at cylinder 76.0.
  Emulated time advances; this is distinct from LWA-EXEC-001's host spin.
- **Diagnosis (2026-09-14):** consecutive fields 6000–6004 and 12000–12004 show
  the stale loading-list background, not a missing opposite field. A bounded
  instruction probe confirms VERTB is serviced and the `$C02D70` flag is set
  and subsequently cleared by the main loop; it is not an interrupt hang.
  The guest advances through the credits sequence. At `$C1E8F2` it writes the
  new COP1LC and at `$C1E8F6` executes `MOVE.W $88(A6),D0` to restart Copper.
  CPU reads previously omitted this side effect; the still-running old list
  can restore its own COP1LC, leaving the new content undisplayed.
- **Expectation:** Commodore HRM chapter 2
  [Starting the Copper After Reset](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0056.html)
  demonstrates the same read strobe. This is not a game-specific rule.
- **Repair:** physical word/byte reads of COPJMP1/2 use the existing restart
  implementation and refresh the next device phase. Long reads retain separate
  ordered word phases. Raw peeks and byte-write read/merge do not trigger it.
  No register mirror write or CPU-core edit. Eight new read cases fail before
  repair (including retained accepted output) and pass afterward; 353/353
  lightweight tests pass. Exact undocumented restart edges remain unverified.
- **Regression validation:** native Lemmings still reaches visibly active gameplay,
  with real audio and zero measured allocations. Host fingerprints were updated
  only after an independent headless replay and visual capture; 18/18 session
  tests (including native gameplay and keyboard replay) and 23/23 framebuffer/
  audio-queue tests pass. New fingerprints and diagnostic-only FPS are in the
  execution plan; no formal throughput result is replaced.
- **Result/limits:** the read-strobe-only repair reached `$6804` at field5475
  and stopped explicitly at unsupported OCS HAM output. That subsequent gap
  is now implemented in LWA-VIDEO-005 below. The restart repair alone is
  not full Hired Guns compatibility or a successful benchmark. Earlier user
  confirmation remains visual evidence of the prior displayed interlace, not
  proof that the true HAM loading screen had been rendered.
- **Evidence:** `.codex-tmp/hiredguns-consecutive-20260914/`,
  `hiredguns-consecutive-late-20260914/`, `hiredguns-flagtrace-20260914.log`,
  `hiredguns-copjmp-before-20260914.txt`, `hiredguns-copjmp-after-20260914.txt`,
  `hiredguns-copjmp-fixed-20260914/` and matching log. Fixed engine SHA256
  `71C378666D31BF8A4DF0DE0ACAE034D71CDF32EC1647004BBBB8B0B6D87BF4CF`.

### LWA-VIDEO-005 — Native Hired Guns loading requires OCS HAM output

- **Type/status:** missing mode IMPLEMENTED for PAL OCS five/six-plane lores,
  including interlaced fields; timing-edge follow-ups remain OPEN.
- **Reproduction:** native-ROM disk1 launch script with COPJMP read repair reaches
  field5475, cycle777793752, PC `$C02890`, BPLCON0 `$6804` (six planes, HAM,
  interlace), DIW `$5071/$DCD1`, DDF `$0030/$00D8`, modulos44/44,
  pointers `$3720A/$3A22A/$3D24A/$4026A/$4328A/$462AA`. Existing unsupported
  reporting correctly stopped with `OCS HAM output` before this slice.
- **Implementation (2026-09-14):** one held ARGB color in the existing Denise
  output stage. Physical lores codes select COLOR00–15 or modify blue/red/green
  in order; sprites overlay without changing that hold. The existing shifters,
  delayed inputs, DMA and framebuffer ownership are unchanged. Ordinary lores
  pays one mode-bit branch; hires is untouched. No new tables, buffers, events,
  allocations or diagnostics. HAM5 uses the same masked shifter codes. Hires
  HAM, HAM with fewer planes, BPU=7 and dual playfield remain unsupported.
- **Validation:** 11 focused cases cover direct/modify ordering, both widths,
  interlace, HAM5, clipping, sprite overlay, palette writes and unsupported
  combinations; 364/364 engine tests PASS. HRM chapter 3
  [Hold-And-Modify Mode](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node008F.html)
  specifies the component codes; RKM Libraries chapter 27 specifies COLOR00
  at the left edge. Tests are not hardware-trace certification of edge phases.
  Unchanged native launch completes 12000 fields with audio and no unsupported
  feature; field5520 shows HAM logos/loading artwork and later fields show
  changing credits. The field5520 BMP repeats exactly in two replays (SHA256
  `A6996D9D87AFA9B5D2DD1DABF1806701CE70E57E8A1AE701BF45742C9292EA4A`).
- **Unverified edges:** exact hold reset versus HBLANK/DIW transitions and
  off-window fetch/scroll combinations; enabling/disabling HAM mid-line without
  a direct-color pixel; undocumented sprite priority/transparency combinations.
  Current model restores COLOR00 outside the window and holds only playfield
  colors. Do not claim these adversarial transitions are hardware-verified.
- **Evidence:** `.codex-tmp/lightweight-ham6-20260914/`, engine SHA256
  `010B40B66A2D114B0C99009DE20AD8EBE098CF926BA323D49268477C7EA39579`;
  `ham-before-20260914.txt`, `ham-after-20260914.txt`,
  `hiredguns-ham6-20260914/`, `hiredguns-ham6-long-20260914/` and matching logs.
  Probe allocations/FPS include snapshots and are not throughput acceptance.
- **Host/performance regression:** 41/41 host checks PASS, with native gameplay
  fingerprints unchanged. Controlled Lemmings retention is valid: 343.59 FPS
  candidate versus 350.16 FPS post-COPJMP reference (98.12% retained); all six
  samples allocate zero measured bytes. Full protocol and telemetry are in the
  execution plan and `.codex-tmp/ham6-native-retention-20260914.log`.
- **Main-menu transition (2026-09-14):** the credits routine loops at `$C096D0`.
  It polls `$C02A92` between animation segments, testing the guest keyboard
  latch `$C02A66` and live CIAA fire pins. The old three-field mouse pulse at
  6500–6503 falls during the animation wait, not the input polling interval.
  The new `hiredguns-native-main-menu-exploratory.json` preserves native boot
  and adds Space down/up at fields6500/6560 (`KeyCode` 320/448). Native keyboard
  delivery latches the event for the next poll. Field6900 and9000 show MAIN MENU
  with F1 new game, F2 saved game, F3 Workbench. PC `$C097A2`, audio active,
  no unsupported feature. No engine change, forced PC, guest-memory patch,
  fallback, timing adjustment or diagnostic bypass.
  Two independent 9000-field replays match CPU/hardware/output fingerprints and
  final menu BMP SHA256 `EF2B4C42053E2A0AD965AC3C4159FCA43596F26BE38208E5FD0407AF6FDB735B`.
  The saved menu workload SHA256 is
  `38829FB5061530DA07291EEA016355D4BBEEB7429DF60FB71C2CFA3E1C72A812`.
  See `.codex-tmp/hiredguns-menu-space-20260914/` and
  `hiredguns-main-menu-repeat-20260914/` plus matching logs.
- **Disposition:** loading-to-main-menu milestone ACCEPTED by the user on
  2026-09-14. Live gameplay presentation, menu-action coverage, full Hired Guns gameplay
  and the disclosed timing edges remain separate OPEN follow-ups, not blockers
  to this accepted slice. Do not
  revert the supported COPJMP behavior to evade the unsupported mode or fall back
  to Legacy. Full game compatibility and default switch are not claimed.
- **Follow-on initial gameplay (2026-09-14):** native F1 menu navigation,
  single-player Exploration selection and a four-character team work. DF0
  disk2/disk3 requests are serviced using the supplied ADF archives. Field19828
  shows all four gameplay views with audio; forward and turn clicks change the
  first character's view at fields20378/20648. No unsupported feature or engine
  change. `hiredguns-native-training-exploratory.json` records the exact route;
  bounded evidence is in `.codex-tmp/hiredguns-training-interactive-20260914/`.
  This supports initial playability, not mission completion, all controls,
  combat/save-load coverage or live presentation acceptance. Some short Return
  taps during selection were not registered; longer presses worked. Their
  sampling behavior is not newly certified or diagnosed as an engine defect.
  An independent cold-boot replay reaches the same cycle2930514162 and final
  state at field20648, with identical gameplay BMP SHA256
  `3FF83B47C7CCFCCF55A2F0235EB713EF5CEF7C9DBAC580FA14CB293614291539`.
  See `.codex-tmp/hiredguns-training-repeat-20260914/` and matching log.
- **Live host follow-up (2026-09-14):** unchanged Release engine in explicit
  Lightweight CopperScreen reaches Workbench, launcher/takeover, English
  Continue, animated credits and MAIN MENU using Space. The existing host log
  `CopperScreen-20260914-230412-146788.log` records serviced audio output with
  zero submit failures in the inspected menu interval; audibility is unverified.
  Short automated F1 taps did not advance the menu. Manual longer-key input was
  requested before attributing this to the host or emulated keyboard. Live
  training/disk swaps remain OPEN; no timing workaround or source change was
  made. See the plan's live-host-check section for scope and build identity.

### LWA-DISK-001 — Disk request sampling and slot ordering

- **Type/status:** unverified hardware behavior; OPEN. Maps to H6b2-T2.
- **Current model:** a ready FIFO word uses the next eligible address input at
  internal H7/H9/HB, with RAM output one CCK later at H8/HA/HC.
- **Question:** when does hardware sample the disk request, and how do one,
  two or three queued words select/order those slots? Fixed slot coordinates
  alone do not prove the request-to-slot delay. The recorded WinUAE comparison
  suggests a distinction worth investigating, not an established oracle.
- **Potential impact:** contention and memory visibility relative to CPU,
  Copper, blitter and display DMA.
- **Revisit:** first when investigating disk-related contention or loader/raster
  timing, or when a discriminating hardware capture is available.
- **Close with:** documented expectations for words arriving before, on and
  after request-sampling boundaries, covering FIFO occupancies 1–3, and focused
  phase checks against those expectations.

### LWA-DISK-002 — Final word, length countdown and completion interrupt

- **Type/status:** unverified hardware behavior; OPEN. Maps to H6b2-T3.
- **Current model:** decrement the remaining count at RAM output; latch DSKBLK
  on the final output, with provisional immediate CPU interrupt visibility.
- **Question:** which effects belong to serial reception, FIFO acceptance or
  RAM completion? What is the CPU-visible interrupt delay? The recorded manual
  warning about a last-read-word hardware bug also needs bounded expectations.
- **Potential impact:** software may observe completion before or after the
  data it expects, or respond to the interrupt at the wrong cycle.
- **Revisit:** first for final-word corruption, completion polling or interrupt
  timing failures; do not infer the answer from Legacy equality alone.
- **Close with:** short-transfer expectations tying the final serial bit,
  accepted bus phase, RAM visibility, countdown and interrupt visibility to
  the same clock. Document the conditions of any modeled hardware quirk.

### LWA-DISK-003 — Cancellation and rearming at an accepted transfer boundary

- **Type/status:** unverified hardware edge; OPEN. Maps to H6b2-T3.
- **Current model:** cancellation drops unaccepted words but preserves an
  accepted address/data pair through RAM output. That old write must not
  decrement or interrupt a newly armed transfer. Reset clears pending work.
- **Question:** do DSKLEN cancellation and DMACON disable behave this way at
  every relevant physical phase, including the last word?
- **Potential impact:** a stray/missing RAM write, stale completion interrupt,
  or incorrect count in a replacement transfer.
- **Revisit:** when cancellation/restart is implicated in a supported loader,
  or alongside LWA-DISK-002 boundary evidence.
- **Close with:** phase-specific cancel/disable/rearm expectations and checks.
  Existing model tests already protect accepted-write isolation; retain them.

### LWA-DISK-004 — Initial word alignment without WORDSYNC

- **Type/status:** unverified hardware behavior; OPEN. Maps to H6b2-T4.
- **Current model:** begin a fresh 16-bit word counter at the second enabled
  DSKLEN strobe when WORDSYNC is disabled.
- **Question:** does hardware instead retain a free-running receiver phase,
  or use another boundary to establish the first DMA word?
- **Potential impact:** the first word and subsequent buffer alignment may
  differ even when the serial bitstream itself is correct.
- **Revisit:** when a supported loader starts unsynchronized reads, or focused
  hardware evidence can vary the start strobe over all 16 bit positions.
- **Close with:** expected first-word data and timing for those positions.
  The continuous-DMA benchmark exercises the current model, not its hardware
  validity.

### LWA-DISK-005 — Partial words across pause and drive-selection changes

- **Type/status:** unverified hardware behavior; OPEN. Maps to H6b2-T4 and H6b1.
- **Current model:** disabling DMA preserves word alignment but does not enqueue
  incoming words; deselecting the drive freezes receiver input while the medium
  continues rotating. Selection/head changes do not restart the spindle.
- **Question:** which bit, byte, word and sync phases survive disable/re-enable,
  deselection/reselection, head changes and seek transitions?
- **Potential impact:** shifted data or sync acquisition differences after a
  pause or drive transition.
- **Revisit:** for read failures specifically following those transitions.
- **Close with:** representative partial-word transition expectations, then
  focused checks preserving the common clock and accepted RAM effects.

### LWA-DISK-006 — DMA-active status after completion

- **Type/status:** unverified readback behavior; OPEN, not yet a confirmed bug.
  Maps to H6b2-T4.
- **Current model:** DSKBYTR enable/direction bits derive from raw DSKLEN and
  DMACON bits; internal transfer activity is tracked separately.
- **Question:** what should DMAON report after the final word, after the first
  start strobe, and during cancellation or pause? In particular, raw enable
  bits remaining set may not describe actual transfer activity.
- **Potential impact:** software polling DMAON may observe a misleading state.
- **Revisit:** when boot/loader code polls this status, or as a small targeted
  register-semantics investigation.
- **Close with:** externally supported readback expectations for each state,
  not simply the current raw-bit formula.

### LWA-DISK-007 — Sync comparator, byte status and reset edges

- **Type/status:** unverified hardware details; OPEN. Inherited H6b1 register.
- **Current model:** compare the live shift register on incoming bits and
  DSKSYNC writes, request a sync interrupt on a new match, use a provisional
  four-CCK CPU-visibility delay, and reset DSKSYNC to `$4489`.
- **Question:** exact comparator/write-edge propagation, the reset seed, byte
  phase under WORDSYNC, and interrupt visibility need further evidence.
- **Potential impact:** polling, sync interrupts and precisely timed register
  writes may differ from hardware.
- **Revisit:** for sync/status-related loader failures or focused write-edge
  evidence. Basic documented WORDSYNC following-word behavior is already tested.
- **Close with:** narrowly scoped reset, status and coincident-write checks
  backed by the relevant hardware expectations.

### LWA-DISK-008 — Ideal-ADF spindle and receiver assumptions

- **Type/status:** declared media-model limitation; OPEN, not a demonstrated bug.
- **Current model:** standard encoded ADF tracks, nominal 300 RPM and a new
  motor/media run beginning at track bit zero after readiness. ADF provides
  sectors, not a recording of physical flux or mechanical transitions.
- **Question:** insertion phase, coast-down, seek settling, speed variation
  and detailed receiver recovery are not fully modeled or verified.
- **Potential impact:** phase-sensitive software may differ from a physical
  drive despite correct sector data.
- **Revisit:** if the supported standard-ADF workload demonstrably depends on
  one of these effects. Broader flux/protection support is outside this scope.
- **Close with:** a clearly bounded supported model and discriminating evidence
  for any added effect; do not introduce speculative delays or title hacks.

### LWA-DISK-009 — Native loader transient non-FAST receiver mode

- **Type/status:** native-path implementation gap addressed experimentally on
  2026-09-14 with a bounded ideal-ADF receiver window. Hardware rate-switch
  phase remains unverified under LWA-DISK-010; this is not a hardware-verification
  closure or an H6d GO.
- **Reproducer:** native Kickstart 1.3, Lemmings SR Disk 1 and the repository
  exploratory input script. The first warning occurs within field 662;
  field-end cycle 94071530, PC 000704E4, ADKCON=0000, DMACON=0000,
  INTENA=0000, DSKLEN=4000, selected motor-on DF0, no RAM DMA.
- **Original behavior:** the ideal FAST-MFM receiver reports unsupported and
  skips shifting bits in this mode while spindle position still advances.
  The loader subsequently restores FAST and loads later screens. Diagnostic
  continuation is explicitly unsuccessful and does not waive the omission.
- **Question:** required slow/FAST transition receiver phase, byte/sync state
  and later visibility must be established. DMA being off does not by itself
  make receiver effects unobservable; successful loading is not hardware proof.
- **Close with:** a bounded primary-evidence-backed transition model and
  focused phase/readback/interrupt tests, followed by this native loader and
  unchanged active-FAST retention checks. Do not special-case the title,
  suppress the warning or change spindle RPM to obtain a passing result.
- **Research update (2026-09-13):** the
  [Commodore ADKCON table](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_8.html)
  specifies 2/4-microsecond cell selection but does not settle recovery on
  switching modes. [WinUAE `disk.cpp`](https://raw.githubusercontent.com/tonioni/WinUAE/master/disk.cpp),
  inspected at `nextbit/getonebit`, recovers slow-mode bits from a nominal
  two-microsecond stream with pattern-dependent consumption (two or three
  source cells). Do not substitute simple every-other-bit sampling or copy
  source-position changes into the physical spindle timeline. This is an
  emulator model, not hardware proof. Still needed: causally bounded recovery
  phase, treatment of an in-flight cell during FAST writes, byte/word state
  continuity, and sync/readback visibility with DMA off. Engine unchanged.

- **Implementation update (2026-09-14):** physical hardware is unavailable.
  A two/three-source-cell recovery window now produces slow-mode receiver
  bits while preserving spindle/index timing, partial byte/word state and
  existing sync/interrupt/RAM-DMA paths. It does not read future media cells.
  FAST writes leave an accepted slow window to complete; that chosen
  transition rule is explicitly experimental, not a measured Paula phase.
  MSBSYNC and unsupported media formats remain rejected. Native boot through
  12,000 fields now completes without a bypass and with `unsupported=none`;
  all 201 matching BMPs and Chip-RAM dumps equal the preceding VP7-fixed run.
  The warning is removed because slow input now executes, not because DMA is
  off or because the title is recognized. See the plan for tests and evidence.

### LWA-DISK-010 — Ideal-ADF recovery-window phase versus physical Paula PLL

- **Type/status:** declared approximation / pending hardware conformance; OPEN.
  Cycle-stamped model execution is not a claim of verified hardware phases.
- **Primary evidence:** the [Commodore HRM ADKCON table](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_8.html)
  defines 2/4-microsecond cell selection. Commodore's [US4780844A receiver
  patent](https://patents.google.com/patent/US4780844A/en) describes pulse-detection
  windows and shifting at window completion, with phase/frequency correction.
  Neither establishes our reduced model's exact mode-write transition phase.
- **Supporting model:** [WinUAE disk.cpp, pinned source](https://github.com/tonioni/WinUAE/blob/62802fab227ceceed3e5d7020818046bc80b76ce/disk.cpp)
  groups nominal two-microsecond source cells in pairs, consuming a third when
  the second contains a pulse. Our receiver consumes arrivals sequentially and
  publishes only after that group is complete; it does not copy WinUAE's
  look-ahead or advance the physical spindle by the group size in one step.
- **Chosen boundaries:** a pending group survives FAST changes and line/field
  wraps. Selection-off freezes receiver state while rotation continues, as in
  the existing ideal model; a new spindle/media run discards an incomplete
  recovery window, while hardware reset also clears partial bytes. FAST input
  with no pending window follows the unchanged one-source-cell path.
- **Unverified:** actual PLL accumulator/history, pulse aperture, phase during
  FAST writes, selection/motor/media gaps, malformed/high-density/flux media
  and true GCR/MSBSYNC operation. This is not a general analog/digital PLL.
- **Pending conformance:** apply FAST writes at successive clock phases around
  a pulse/window boundary and measure recovered bits, DSKBYTR ready/data,
  DSKSYNC/interrupt and active-DMA alignment. Until trustworthy physical traces
  or equivalent circuit evidence exist, keep the current model tests labeled
  as model checks. Do not silently describe them as hardware-backed tests.
- **Revisit:** supported loader/readback differences, or new primary timing
  evidence. No real Amiga availability is required for continued experimental
  work, but implementation and performance results do not close this uncertainty.

### LWA-VIDEO-001 — Native Lemmings requires OCS hires DMA/output

- **Type/status:** implementation gap addressed and accepted experimentally
  with H6d on 2026-09-14. Full native pixels and gameplay are included in the
  343.10 FPS paired result; performance/gameplay are no longer pending here.
  Hardware-transition and crack-intro uncertainties remain in LWA-VIDEO-002.
- **Reproducer:** first video warning at field 1949 of the native exploratory
  script; field-end cycle 276956814, PC 000794A4, BPLCON0=C200 (four hires
  planes), DDFSTRT=003C, DDFSTOP=00D4. Another hires screen is reached after
  skipping the animated introduction and is blank at field 4800.
- **Current behavior (2026-09-13 update):** four-plane hires DMA and full
  908-pixel output are implemented. Normal 3C/D4 fetches 40 words per plane,
  with one modulo update and retained physical word phases. Native field
  4800 now shows a readable disk-2 prompt with no video warning. All 290
  lightweight tests pass in Debug/Release; the active hires smoke allocates
  zero bytes. Width 454 remains explicitly lowres-only and rejects hires.
  Performance retention was INVALID / RERUN because another test was active;
  no cost/retention acceptance is claimed. The independent disk warning remains.
  The subsequent user-requested provisional benchmark completed all repeats:
  unchanged lowres 399.08 versus H6c 398.55 FPS; full-width hires 344.99 versus
  full-width lowres 373.23 FPS (7.57% lower throughput, +0.219 ms/field).
  All samples allocate zero bytes with deterministic fingerprints. These are
  synthetic observations under competing tests, not a formal gate or gameplay
  result; see the execution plan's provisional benchmark entry.
- **Optimization follow-up (2026-09-14):** paired shifting plus direct
  per-CCK hires visibility is retained experimentally. The latest same-work
  300/3,600-field diagnostic comparison is 385.80 versus paired-shift reference
  371.12 FPS (+3.96%), with identical fingerprints and zero allocation.
  All 298 Debug/Release tests pass, and all 246 native snapshots remain byte
  identical. The separate inactive-sprite experiment was slower and reverted.
  This does not verify the edges in LWA-VIDEO-002, fix the disk warning or
  establish complete gameplay speed; full evidence is in the execution plan.
- **Close with:** hires fetch cadence, accepted word phases, pointer/modulo
  behavior, shifter reload/scroll and mixed lowres/hires output at sufficient
  resolution; focused tests and measured regression/cost controls, then native
  screenshots and input. Do not downsample away required pixels for an FPS
  result. Full crack-intro visual correctness remains unverified as well.

### LWA-VIDEO-003 — Cracktro DMA stops early and host view uses the wrong origin

- **Type/status:** two confirmed defects repaired on 2026-09-14; broader
  cracktro hardware correctness remains unverified under LWA-VIDEO-002.
- **DMA cause:** native DIWSTRT=$2C10 / DIWSTOP=$3CF0 was interpreted by
  bitplane DMA as stopping at line 60 instead of 316. OCS supplies V8=!V7;
  the output path already decoded this correctly. Missing artwork exists in
  raw engine snapshots, not only the host window. Corrected DMA decoding;
  no title workaround, extra clock or logging. Four focused cases plus all
  342 Release engine tests pass.
- **Host cause:** the beam-coordinate buffer includes leading blanking, but
  the host used Legacy's capture-relative crop. Explicit Lightweight viewports
  now map the existing visible raster and standard PAL DIW origin. Legacy
  defaults and complete engine buffers are unchanged. The corrected native
  cracktro and both viewports were visually inspected in the real window.
- **Evidence:** execution-plan subsection "Stage 4 cracktro DMA and
  host-coordinate repair"; `.codex-tmp/lightweight-cracktro-{recheck,fixed}-20260914`.
  Original gameplay script still finishes with unchanged gameplay fingerprints
  and zero measured allocation. Formal throughput retention for this new build
  is now valid: **327.07 versus 332.80 FPS**, 98.28% retention, 200 FPS target
  PASS, unchanged fingerprints and zero allocation. Both spreads and host-v2
  checks pass. The earlier unpinned 322.21 FPS is not acceptance evidence.

### LWA-VIDEO-002 — Hires reload and mid-fetch resolution-change edges

- **Type/status:** bounded-model limitation / unverified hardware edges; OPEN.
- **Current model:** normal hires uses the documented 4,2,3,1 fetch cadence
  and 40 words per plane; scroll uses lowres units modulo eight, supported by
  WinUAE's interpretation. The existing delayed register/data stages and
  eight-CCK DDF stop control are retained. Scroll reloads are resolved per
  physical hires pixel; sprites still advance at lowres rate.
- **Not verified:** exact OCS reload behavior for writes adjacent to hires
  fetch boundaries; resolution/BPU/scroll changes during the terminal fetch
  unit, including modulo selection; full crack-intro visual correctness.
  Coarse/single-cycle equality is an internal ordering test, not a hardware
  oracle. Normal HRM examples do not settle these transition edges.
- **Revisit when:** native supported content exposes a boundary/mode-change
  discrepancy, or a primary hardware test becomes available. Obtain phase
  expectations and add a small discriminating case before changing the model.
  Do not introduce a title-specific delay or a schedule cache.

### LWA-VIDEO-003 — Native level-1 terrain and control-panel corruption

- **Type/status:** confirmed Copper comparison defect; RESOLVED on 2026-09-14
  for the reported repeated-panel/short-terrain symptom. H6d was subsequently
  accepted experimentally; this resolution does not verify all rendering behavior.
- **Reproducer (2026-09-14):** native Kickstart 1.3, SR Lemmings disk 1/2,
  runner `Workloads/lemmings-native-level1-exploratory.json`, 12,000 fields
  with `--boot-probe --probe-continue-unsupported`. The script reaches a
  readable menu and “Just dig!” briefing before starting the level.
- **Observed:** snapshots 10,980 and 12,000 show terrain near the top and a
  corrupted/repeated control panel down the screen. The timer changes from
  4:42 to 4:21; nonzero gameplay PCM is sampled from field 10,260. This is
  active execution, not usable gameplay or verified rendering. Evidence is
  `.codex-tmp/h6d-native-level1-20260914/` with matching `.txt` log.
- **Regression boundary:** the same new runner/script on pre-opt4 opt3 matches
  all **606** BMP/Chip-RAM/state snapshots byte for byte. Thus this symptom
  predates the direct wide-lowres writer; earlier changes are not ruled out.
  Reference: `.codex-tmp/h6d-native-level1-opt3-20260914/`. Full hashes and
  endpoint fingerprints are recorded in the execution plan.
- **Cause:** `LightweightCopper.ComparisonSatisfied` removed vertical bit 7
  from both operands. The saved list's `CC01/FF00` WAIT at `0085CC` therefore
  matched line 76 instead of 204, installing hires panel pointers/mode 128
  lines early. Later palette/interrupt waits were also affected. End-frame
  BPLCON0 alone had hidden the intended lowres/hires split.
- **Resolution:** force bit 15 of the comparison mask on; keep IR2 BFD's
  separate live-blitter condition unchanged. Commodore's [HRM chapter 2,
  comparison-enable discussion](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_2.html)
  explicitly makes VP7 unmaskable in WAIT/SKIP. Ten added synthetic cases
  include eight that fail before repair; all **318** tests pass in Debug and
  Release afterward. Renderer, CPU and disk code are unchanged.
- **Native verification:** the same 12,000-field script now displays full
  terrain and one bottom panel, with lemmings, advancing timer and nonzero
  audio. Evidence `.codex-tmp/h6d-native-level1-vp7-20260914/` and repeat run
  `h6d-native-level1-vp7-repeat-20260914/`; complete fingerprints/build identity
  in the execution plan. Known slow-disk warning persists, so boot probes
  still end unsuccessfully and their FPS is not acceptance evidence.
- **Revisit:** a repeatable remaining display symptom or contrary hardware
  evidence. Preserve the focused VP7 cases; LWA-VIDEO-002 and LWA-DISK-009 are
  not closed by this repair. No title-specific handling or omitted pixels.

### LWA-CIA-001 — Board TOD pulse phase and input qualification

- **Type/status:** bounded-model limitation / unverified hardware phases; OPEN.
- **Current model:** CIA-B accepts one TOD pulse at each completed PAL raster
  line, CIA-A at each completed field. Pulses enter the single execution clock;
  enabled alarm requests use the existing eight-CPU-cycle CIA-to-Paula IPL
  visibility model. No separate device timeline or TOD debounce is claimed.
- **Question:** actual board sync/TICK edge placement, PAL/interlace pulse
  details, E-clock qualification and the reported 14–16 E-clock debounce
  need a discriminating physical oracle. Legacy's boundary-based TOD tests
  do not establish those phases; its conformance matrix also leaves debounce
  pending. WinUAE models additional qualification, as supporting evidence.
- **Potential impact:** reads near a pulse and alarm interrupts can differ
  from hardware even when the long-term counter rate is right.
- **Revisit:** supported native boot/gameplay depends on sub-line TOD timing,
  a focused hardware check fails, or suitable captured edge evidence is found.
- **Close with:** pin-to-counter-to-ICR/IPL evidence for the declared PAL OCS
  board profile, including a pulse crossing a line/field boundary and reset.
  Keep the counter/register tests; do not label raster-boundary assertions as
  proof of the physical pulse phase.

### LWA-CIA-002 — TOD comparator-write and reset/read-latch edges

- **Type/status:** unverified hardware edges; OPEN.
- **Current model:** binary 24-bit counter and separate write-only alarm;
  high-byte read holds the read latch until low-byte read. High-byte counter
  write stops counting, low-byte write starts it; middle-byte write preserves
  running state. Alarm writes do not affect running state. Reset clears state
  and leaves TOD stopped until its low byte is written. Alarm comparison is
  performed on an accepted counter pulse, not on register writes.
- **Question:** comparator triggering on counter/alarm writes, intermediate
  carry/glitch behavior, power-on details and writes during a latched read
  are not fully verified. The HRM prose about stopping on writes is less
  specific than the MSB-stop behavior modeled here and in WinUAE.
- **Potential impact:** an alarm programmed equal to the current count or
  unusual programming order may have different interrupt/readback behavior.
- **Revisit:** a supported workload uses one of these edges, or primary/hardware
  evidence discriminates it. Do not copy Legacy's always-running counter as
  a correctness oracle; that implementation lacks the stop/restart behavior.
- **Close with:** a minimal register/pulse sequence and established expected
  counter, latch, ICR and visible interrupt cycles for each changed edge.

### LWA-INPUT-001 — Keyboard MCU startup, loss of sync and reset sequences

- **Type/status:** explicit implementation gap; OPEN, not verified behavior.
- **Current model:** synchronized raw-key transport with data setup, low/high
  KCLK phases, CIA serial receive, a ten-code type-ahead buffer and a required
  host data-line acknowledgement. Reading SDR/ICR is not acknowledgement.
- **Missing:** startup resync pulses, FD/FE power-up stream, timeout resync /
  F9 retransmission, MCU matrix scanning, power-up held-key stream and reset
  chord behavior. Host translation owns raw key transitions and caps-lock/
  repeat policy. The engine does not automatically run a keyboard MCU.
- **Visibility:** a 143 ms handshake timeout reports unsupported and parks the
  transfer; switching the CIA into output mode during transmission also
  reports unsupported. Neither can silently produce a successful benchmark.
  The synthetic replay explicitly starts from a synchronized peripheral.
- **Revisit:** native cold boot or recovery requires startup/resync, or a
  supported program changes direction during a pending transfer. Do not
  present the synchronized fixture as cold-keyboard boot evidence.
- **Close with:** primary-protocol-backed startup/recovery cases, held keys,
  interrupted bytes and reset, then a native-ROM input smoke. Keep byte-read
  versus handshake and accepted-bit ordering tests as regressions.

### LWA-INPUT-002 — Keyboard timing and CIA serial/output pin details

- **Type/status:** unverified physical edges plus unsupported modes; OPEN.
- **Current model:** three 142-CPU-cycle phases per bit (~20 us each); the
  first data phase is the next CCK after host submission. The CIA latches SDR
  and its serial ICR bit on the eighth rising KCLK edge; enabled requests use
  the existing eight-cycle CIA/Paula IPL path. A host low/high pulse lasting
  at least eight CPU cycles acknowledges; fixtures use >=604 cycles (85 us).
- **Question:** precise keyboard oscillator, SP output latch/direction-switch
  behavior, CIA input qualification and interrupt propagation need physical
  evidence. The HRM describes approximate 20/20/20 us phases, not an exact
  142-cycle oscillator. Do not describe this choice as measured hardware.
- **Unsupported:** general timer-driven serial output and keyboard CNT timer
  input modes. Active output-register transmission or receiving CNT in a
  selected unsupported timer mode is reported. The simple output latch is
  sufficient only for the declared bit-banged handshake sequence.
- **Revisit/close:** supported software exercises those modes, or a pin/IRQ
  trace contradicts the chosen model; isolate the earliest differing edge and
  add a focused serial/handshake check before changing execution.

### LWA-INPUT-003 — Controller pin sampling and analog behavior

- **Type/status:** bounded digital-controller model and implementation gaps; OPEN.
- **Current model:** SubmitInput accepts relative mouse counter transitions
  once at the current machine cycle, with 8-bit wrap. Joystick directions use
  JOYDAT XOR encoding; CIA fire inputs and digital POTGOR buttons use stored
  host levels. JOYTEST preserves the two low counter bits; POTGO low outputs
  and pressed buttons pull their modeled digital lines low.
- **Missing/unverified:** electrical quadrature transitions and sampling phase,
  movement while joystick pins change, analog RC/paddle conversion, light-pen
  behavior, adapters and hardware-reset details. Analog POT counter reads
  report unsupported; extra host controller flag bits are rejected.
- **Revisit:** native gameplay exhibits input differences at those boundaries
  or requires a deferred controller mode.
- **Close with:** explicit host-event/pin/counter timing expectations and a
  focused hardware-backed case, without replacing direct state ownership by
  generic event objects or per-cycle input polling.

### LWA-INPUT-004 — Native Lemmings keyboard event stops on CIA SDR output write

- **Type/status:** premature SDR-write rejection REPAIRED for the no-output-clock
  acknowledgement path; native press/release replay PASS. General clocked output
  remains unsupported and visible; this is not full serial/keyboard acceptance.
- **Reproduction (2026-09-14):** native Kickstart 1.3 / PAL OCS / 68000 /
  512 KiB Chip + 512 KiB slow / read-only ADF DF0. Boot disk 1, advance the
  title sequence, swap to disk 2, reach the menu, then press Enter. The live
  session reports `general CIA serial output transmission` and parks with
  PC `$001972`, last PC `$00196E`, SR `$2104`. Host reset recovers and boots
  the selected disk again; repeated Run cannot bypass the fault.
- **Independent replay:** fixed loader events through field 4926, raw Return
  `$44` submitted at field 7900 (`keyCode: 324`). Both current integration
  engine and accepted frozen H7 engine finish field 7901 with the same
  unsupported report and cycle `1122747910`, CPU `C6B48640BAD80718`, hardware
  `15B7207A0B2D4B93`, output `438A3609594803C4`. Both runners exit 2.
  This predates the host adapter and the queue-discard repair. The diagnostic
  one-field FPS values are not performance evidence.
- **Original boundary:** `LightweightA500Machine.WriteCiaRegister` reported unsupported
  for *every* CIA-A SDR (`$BFEC01`) write while CRA serial-output mode is set.
  The original pin model immediately used the written byte's high bit as SP;
  it did not implement a Timer-A-clocked serializer. Existing synthetic
  keyboard handshakes only toggle CRA, so they did not exercise this path.
- **Captured cause:** the native handler writes SDR=0 in input mode, CRA=$41,
  SDR=1, waits, writes SDR=0, then CRA=1. Output mode lasts 1,170 CPU cycles;
  Timer A counter falls from 36,050 to 35,933 without underflow. No serial byte
  is clocked out in this acknowledgement. See the execution plan's trace table.
- **Repair:** SDR is a holding buffer, not SP. A pending output byte is canceled
  on return to input mode. Actual Timer-A underflow while output is pending
  still reports `clocked CIA serial output transmission`, with an unmasked
  scheduling boundary regardless of IRQ masks; stopping/restarting/reloading
  Timer A cannot hide it. This guard covers both CIA chips. The existing
  direction-toggle low/high handshake model is retained, not newly certified.
- **Verification:** eight focused cases plus the rest of the 338-test engine
  suite pass; the host's native Enter press/release at fields 7900/7906 reaches
  idle keyboard state by field 7916 and continues through field 14520 with the
  original gameplay output checksum `D9AED52B4567F91B`. Original mouse-only
  replay also passes. Temporary one-field tracing was removed from source.
- **Still deferred:** a handshake overlapping an actual output-clock boundary
  still stops. A Timer-A-clocked serializer, detailed CIA direction/pin pipeline
  phases and keyboard resynchronization require their own implementation and
  evidence (LWA-INPUT-001/002); do not characterize them as resolved here.
- **Host visibility:** fault reason now travels explicitly in runtime state and
  is shown in a wrapping banner independent of the toolbar/hidden overlay.
  Mouse capture is released at a fault; reset clears the banner state. Focused
  runtime publication/reset checks pass. A real-window synthetic fault fixture
  exposes the complete banner text in the accessibility tree. The initial
  visual attempt was blocked by activation/capture failure. A subsequent visible
  launch on 2026-09-14 verifies readable reason/reset guidance in windowed mode,
  fullscreen and toolbar-hidden fullscreen: **banner visual PASS**. The fixture
  was closed. Native live recheck remains OPEN: capture of the separately
  launched native session failed twice with `foreground window did not report
  a process id`. Details are in the execution plan; no native gameplay PASS is
  inferred from the synthetic banner fixture.
- **Performance:** valid interleaved retention comparison: repaired median
  **341.76 FPS**, frozen prior integration **339.91 FPS** (100.54% retention),
  unchanged complete-workload fingerprints and zero measured allocation.
  Host-load policy v2 and both sample-spread checks pass. Evidence:
  `.codex-tmp/lightweight-keyboard-retention-series-20260914.txt`.
- **Evidence:** `.codex-tmp/lightweight-live-keyboard-failure-20260914.log`;
  `.codex-tmp/lightweight-native-menu-enter-20260914.json` (SHA-256
  `3E0B29D729CEAF7A611ECA2CA19C887B0644E1490E929BE92D7DF80E077B9352`);
  `.codex-tmp/lightweight-native-menu-enter-{current-replay,reference}-20260914.txt`.
  The failed initial boot-probe invocation rejected incompatible warmup
  arguments before executing; it is not emulation evidence.
  Repair evidence: `.codex-tmp/lightweight-keyboard-trace2-20260914.txt`,
  `.codex-tmp/lightweight-keyboard-handshake-before-20260914.txt`,
  `.codex-tmp/lightweight-keyboard-final-engine-tests-20260914.txt`,
  `.codex-tmp/lightweight-keyboard-host-tests-20260914.txt`,
  `.codex-tmp/lightweight-keyboard-native-host-20260914.txt`.

### LWA-PERF-001 — H6c2 input-active retention

- **Type/status:** measured performance shortfall; RESOLVED for the H6c2
  checkpoint on 2026-09-13, not a hardware defect.
- **Evidence:** initial input-active replay median 402.34 FPS versus H6c1
  no-input 438.30 FPS, **91.79% retention** against the unchanged 95% cost
  gate. Both series are valid; dormant controls pass. These are synthetic
  hardware FPS, not native Lemmings gameplay.
- **Investigation:** CPU bus reads spend substantial time advancing DMA while
  waiting for slots. The candidate keeps the pending CPU request inside the
  clock loop, preserving every CCK and grant/completion order. All 264 tests
  pass; supplied-disk smoke fingerprints match exactly with zero allocations.
  A subsequent full 300-warmup / 3,600-field input replay also matches the
  frozen pre-optimization engine exactly, including CPU/hardware/output
  fingerprints and zero allocations. That run was correctness-only because
  the independent test workload remained active.
- **Resolution:** after the independent tests finished, a valid same-work
  comparison measured 420.89 versus 388.46 FPS, **+8.35%**, with identical
  fingerprints. The input-active replacement gate measured 419.43 versus
  433.26 FPS, **96.81% retention**, passing the unchanged 95% threshold.
  Disk/TOD input-dormant and STOP controls passed at **102.10% and 99.15%**
  retention against 97%. All four series passed policy-v2 telemetry and
  spread checks, with zero allocations. The clock-loop simplification is
  retained; the earlier valid failure and invalid attempts remain historical
  evidence. See the H6c2 clean benchmark section in the execution plan.
- **Revisit:** a repeatable same-work regression, different execution
  fingerprints or a supported input path that exceeds the cost budget.
  Hardware uncertainties remain separate. The user explicitly accepted H6c
  on 2026-09-13; H6d is next. That acceptance does not close the unresolved
  hardware entries or authorize a production switch.

## Explicit implementation gaps, separate from suspected bugs

- H6c now delivers synchronized keyboard bytes/handshakes and digital mouse/
  joystick state, with deterministic replay. The earlier store-only input gap
  is replaced by the bounded implementation; startup/recovery and physical
  limitations remain LWA-INPUT-001 through LWA-INPUT-003 above. Native gameplay
  is still a separate unfinished gate.

- Active DSKLEN reprogramming without cancellation, changing WORDSYNC enable
  during active DMA, and FIFO-overrun behavior currently report unsupported.
  Revisit the applicable behavior if the supported boot/gameplay path uses it.
- Disk write DMA/writable ADF and true GCR/MSBSYNC recovery are unsupported.
  Slow-mode reception from standard ADF now has the explicit experimental
  window model described in LWA-DISK-009/010, not full PLL support.
  Writable media and broader disk formats remain deferred scope, not an
  automatic requirement for the read-only standard-ADF milestone.
- Native Kickstart/Lemmings gameplay has not yet been accepted. Treat missing
  functionality found there as concrete implementation work. Reading the
  Lemmings ADF under a synthetic ROM is not evidence of native gameplay.

## Resolved items to retain as regression coverage

- **Zero-length second DSKLEN strobe:** the inherited Legacy defect was
  investigated and corrected; the lightweight path has focused coverage.
  A zero-length second enabled strobe must not be conflated with cancellation.
  Do not reopen this as an unanswered question without contrary evidence.
- **H6b2 serial-only performance retention:** the initial 95.90% failure was
  replaced by a valid 98.85% result after the storage/hot-path changes. Keep
  normal performance checks; this is not currently an open performance defect.

Incomplete hardware verification alone does not establish an emulator bug.
LWA-VIDEO-003 separately records a confirmed Copper defect, now repaired with
primary-documentation and synthetic evidence. Other entries retain their
stated evidence categories.

## Evidence and maintenance

- [Engine plan: H6b/H6c assumptions, tests and performance](LIGHTWEIGHT_A500_ENGINE_PLAN.md).
- [Historical arbitration execution plan: G5L disk evidence and inherited fixes](LIGHTWEIGHT_AGNUS_BUS_ARBITRATION_EXECUTION_PLAN.md).
- Implementation: `../CopperMod.Amiga.Lightweight/LightweightDiskSerial.cs`,
  `LightweightDiskDma.cs`, `LightweightCia.cs`, `LightweightClock.cs`, and
  `LightweightA500Machine.cs`, plus `LightweightKeyboard.cs` and
  `LightweightControllers.cs` in that project.
- Focused checks: `../CopperMod.Amiga.Lightweight.Tests/LightweightDiskSerialTests.cs`
  and `LightweightDiskDmaTests.cs` / `LightweightCiaTodTests.cs` /
  `LightweightInputTests.cs` in that project.

For each revisit, record the build/profile, smallest reproducer, observed and
expected cycles/state, evidence source, resolution and affected checks. Keep
IDs stable and mark resolved entries with evidence rather than deleting them.
Legacy/emulator comparisons are investigation signals; do not relabel them as
hardware captures. Do not add per-cycle release logging merely to maintain
this document.
