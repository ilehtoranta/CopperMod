# Lightweight A500 engine execution

**2026-09-16 host decoupling:** the user authorized deferring CopperStart
restoration. The native CopperScreen build no longer references the Legacy
host or CopperStart projects. Legacy/CopperBench report unavailable instead of
falling back. Use `CopperScreen.Lightweight.slnx` for the independent build;
see [restoration boundary](../CopperScreen/COPPERSTART_RESTORATION.md).
This supersedes earlier host availability statements, not hardware gate results.

Current bounded completion checklist and supported configuration:
[Supported-v1 readiness](LIGHTWEIGHT_A500_SUPPORTED_V1_READINESS.md).
**Subsequent default-switch authorization, 2026-09-15:** the user approved
Lightweight as the universal default, native A500 as the standard startup
profile, explicit-only Legacy selection, and clear rejection of unsupported
configurations without silent hardware changes. Host routing and Settings are
implemented and verified: 133 checks including both native replays passed;
the final settings-only rerun passed 133 checks with two external-asset native
tests skipped. Engine and CPU binaries are unchanged. Build identity and logs
are recorded in the readiness document above.
This current decision supersedes opt-in/default-routing statements in the
historical staged narrative below; historical gate outcomes are not relabelled.
**BUILD ACCEPTED WITH OWNER WAIVER, 2026-09-15:** the user stated “I accept
this build” after the one-off aggregate-host-load exception was proposed.
The supported-v1 frozen build and its final performance retention are accepted
by exception; no more reruns are required solely for this acceptance. This is
not a general policy change, hardware-edge closure or default-engine switch.
The latest 2026-09-15 final-build retention series measured 324.94 versus
329.01 FPS (98.76%), but is INVALID / RERUN because aggregate host interference
crossed the policy-v2 threshold for 10.117 seconds. This is the third complete
host-invalid series; none replaces the preceding valid performance evidence.
See the readiness document for all samples, fingerprints and immutable logs.
Final live Lemmings skill assignment
is user-confirmed ("Digger digs a hole"), disk replacement works, and a separate
same-build boot-path reset check passes. Hired Guns remains user-accepted.
The bounded live completion pass is complete. Underlying measurement labels and
logs remain unchanged; build acceptance is the separate owner decision above.
Host-load attribution is optional follow-up, not a remaining acceptance gate.
The build acceptance did not itself authorize a switch; the subsequent explicit
authorization and implementation are recorded above.

Status: **H0 through H5 accepted; H6a (DF0 media/control pins) is GO;
H6b1 ideal-ADF serial input and H6b2 read RAM DMA are GO for experimental
continuation within their documented models; H6b and H6c are accepted
with TOD retained and synchronized keyboard/digital-controller execution
implemented; input correctness and all replacement performance checks PASS;
the cycle-preserving CPU-wait simplification is retained with an 8.35%
same-work throughput gain; H6c is GO and H6d is accepted / experimental GO
by the user on 2026-09-14 within its documented scope;
OCS hires DMA/full-width output is implemented with 330 focused-suite tests passing;
scripted disk swapping reaches the native Lemmings menu, first-level briefing
and running level with audio diagnostically; the repeated control-panel
corruption is repaired by restoring Copper's mandatory vertical high-bit comparison;
hires visibility simplification retained experimentally: same-work diagnostic
385.80 versus 371.12 FPS (+3.96%) beyond paired shifting, identical output;
previous lowres retention was 99.37% diagnostically; STOP spread exceeds 10%,
subsequent direct wide-lowres output gains 1.58% in its confirmation series
(411.74 versus 405.33 FPS), with no material narrow/hires regression observed;
the repaired corruption predated opt4 (606 identical snapshots against opt3);
slow-mode ideal-ADF reception now executes through an experimental window model;
native level input assigns a digger and saves lemmings without unsupported bypass;
exact receiver rate-switch phases remain unverified (LWA-DISK-010);
latest retained native gameplay optimization measures 347.58 versus 310.03 FPS
(+12.11%), zero measured allocation, three samples per build with valid
host/spread checks; the earlier 271.05 FPS result is historical, not current speed;
the unchanged script completes a first 14,520-field Legacy replay with active
gameplay/audio over the proposed interval; the completed paired comparison now
measures 343.10 FPS Lightweight versus 28.41 FPS frozen Legacy (12.08x), with
valid host/spread checks, deterministic within-engine outputs and zero measured
allocation; native-workload reproducibility and the 200 FPS throughput check PASS;
H6d experimental GO is accepted; H7 performance-stage review is GO at 343.10 FPS
within that accepted scope; Stage 4 opt-in CopperScreen connection is implemented;
host pause/reset/fault audio-queue discard is repaired; new-build retention PASS
at 323.71 versus 322.92 FPS (100.24%) in the current paired run;
live native boot/menu, mouse loader input, disk swap and reset recovery work;
native Enter's pre-existing premature SDR-write stop (LWA-INPUT-004) is repaired;
native press/release plus gameplay replay PASS; actual clocked serial output
remains explicitly unsupported; Hired Guns interlace presentation is user-confirmed
visually working; subsequent interrupt-tail frame stall is repaired (LWA-EXEC-001),
its stale blank display is repaired by COPJMP read strobes (LWA-VIDEO-004),
exposing the HAM output gap, now implemented with loading artwork and credits
replay evidence (LWA-VIDEO-005; adversarial timing edges remain unverified);
Hired Guns loading-to-main-menu milestone ACCEPTED by the user on 2026-09-14:
native replay reaches MAIN MENU using Space after the credits start, without
further engine changes;
follow-on Hired Guns Exploration training loads through disk2/disk3 requests,
renders all four character views and responds to forward/turn mouse input;
live CopperScreen boot, launcher/takeover, credits and MAIN MENU are observed;
the user accepts the repaired live Hired Guns session on2026-09-15,
confirming correct music, sound effects and graphics; occasional interlace
instability is retained as a non-blocking follow-up, not a hardware verification;
manual F1 follow-up exposes a host starvation mechanism, repaired with a
dispatcher regression check and subsequent user live acceptance (LWA-HOST-001);
the resulting interlaced CRT-phosphor blackout under pointer-input pressure is
repaired by bounding host decay to one emulated field interval (LWA-HOST-002);
residual interlace steadiness remains OPEN / deferred;
the pre-Workbench garbage stripe is repaired by suppressing premature
early-blank sprite DMA, with368 tests and native boot/menu/training replays;
clean throughput retention PASS:327.40 versus330.43 FPS,99.08% (LWA-VIDEO-006),
independently of host input starvation;
subsequent direct sprite blank-end scheduling retains the repair with375 tests
and identical native checkpoints; latest same-work retention PASS at335.05
versus329.05 FPS (+1.82%), zero allocation and identical timing/output fingerprints;
Hired Guns missing melody is traced to a lost held CIA interrupt and repaired
with382 tests plus native four-channel progress evidence (LWA-CIA-003);
the repaired recording and rebuilt-host music/sound effects are user-confirmed
correct; current Lemmings replay retains the
previous complete-workload fingerprints, without a new formal speed claim;
Legacy/default routing is unchanged;
post-H6b2 storage/hot-path simplifications are retained, host retention is PASS,
and the recorded hardware uncertainties remain OPEN;
the post-H4a and post-H5a renderer optimization checkpoints retained;
post-H5c output/diagnostic simplifications retained without an active-FPS gain claim;
the original pre-project matched baseline remains unavailable; the new comparison
uses an explicitly prospective frozen Legacy reference.** The
separate pre-device CPU-throughput checkpoint and the formal H2 hardware-path
close are **PASS / GO**.
The new project is an independent A500 PAL OCS engine foundation with a direct
CPU/bus/clock boundary, PAL beam and refresh timing, CPU Chip-RAM phases,
interrupt visibility, essential CIA timers/ports, ROM vector/overlay handling,
raw register latches, Copper, low/high-resolution bitplane DMA, direct OCS raster
output, manual and DMA-fed Denise sprite output, compact Agnus sprite-DMA sequencing,
Paula audio DMA, real 48 kHz stereo PCM and ADKCON modulation,
ideal-ADF serial input and experimental fixed-slot disk read DMA,
reusable output buffers, and a headless runner. It is not yet a
complete Amiga emulator. CopperScreen now supports explicit experimental selection;
Legacy remains the default and no production-default cutover has occurred.

## Live audio acceptance update — 2026-09-15

The earlier inference that the report was merely normal stereo separation is
**withdrawn**. The user clarified that only background bass is heard and the
melody is missing. Both speakers being audible does not establish complete
music playback. Investigation reproduced a lost CIA-B timer request at field
6204: Paula DMA continues repeating old samples, but the music sequencer stops.
See **LWA-CIA-003** in the issue register for the exact event sequence.

The correction retains each CIA's asserted /IRQ latch until ICR is read, and
prevents a Paula acknowledgement from losing that still-held request. It adds
no per-cycle polling, diagnostic callbacks or managed allocations. Seven focused
checks cover both CIAs, masking, acknowledgement and reset; four lost-request
cases fail before the fix, and the final full Lightweight suite passes382/382.

Native Hired Guns replay completes11000 fields with no unsupported feature.
Over sampled fields6500-11000 all four channels now change sample locations,
periods and volumes; previously all three values were fixed on every channel
and channel3 looped zero. Raw stereo output from fields7001-8000 is saved as
`.codex-tmp/hiredguns-audio-fixed-20260915/hiredguns-menu.wav` for user listening.
The user subsequently confirmed this repaired recording: "It works correctly
now" (2026-09-15). The missing-melody listening check is ACCEPTED for this
recording, not proof of every instrument/timing detail or rebuilt-host playback.
The CIA/Paula synchronizer's sub-CCK
acknowledgement behavior remains unverified; the fix establishes the held-line
contract rather than inventing a new propagation-delay oracle.

Current Release engine SHA256:
`D97933A56E464C312FC60C1730E548F235B1206B924E1CA43288FE1837A84A4F`.
Copper68k is unchanged (`F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`).
A separate CopperScreen Release build is ready in
`.codex-tmp/hiredguns-audio-fixed-host-20260915/`; the already-open live process
was not replaced or restarted and still contains the preceding build.

Lemmings10920 warmup +3600 measured fields retain cycle2063321634,
CPU`6AE7090DA8AFB7E3`, hardware`334A819F6FDFB1CA`,
output`C65F87325946E5DA`, real48kHz stereo and zero measured allocation.
Evidence: `.codex-tmp/lightweight-cia-irq-lemmings-20260915.log`.
Its310.31 FPS is **diagnostic only**: unpinned, no formal host telemetry,
with the live emulator running and a build overlapping the replay. It neither
replaces the prior valid335.05 FPS disposition nor establishes new retention.
The repaired-recording listening check was ACCEPTED before the restart;
rebuilt-host acceptance is recorded in the subsequent live-session section.
No production/default switch occurred. The remaining broader training/disk
coverage and Lemmings live skill assignment are not closed by audio acceptance.

## Combined repaired live session — 2026-09-15

Following approval to restart and check live audio/F1 responsiveness, the
initial audio-only test launch was superseded by a fresh build including the
newer LWA-HOST-002 bounded CRT-decay repair. The audio-only window was closed;
only the combined session remains running.

- Build: `.codex-tmp/hiredguns-live-combined-fixes-20260915/`.
- CopperScreen SHA256:`0D3F3CBF57787C73B7D26D5F1344D5597EE5ED0402CCCE63A634D461F9B65B7B`.
- Engine SHA256:`D97933A56E464C312FC60C1730E548F235B1206B924E1CA43288FE1837A84A4F`.
- Process6036, started22:27:47 local time; explicit Lightweight/native
  Kickstart1.3 profile and the same read-only Hired Guns disk1 ZIP.
- Native Workbench, opened game drawer, loader and animated title sequence
  observed. User input occurred during launch; do not attribute the completed
  launcher steps to the interrupted automated clicks.
- Pause click is followed by`paused=True` at frame5252 and subsequent
  `paused=False` with advancing frames. Resume occurred during user interaction;
  it was not a second automated click. No audio-submit failures in the observed
  interval. These observations do not yet reproduce the post-F1 incident.
- Log: `C:/Users/vsys-admin/AppData/Local/CopperScreen/Logs/CopperScreen-20260915-222747-6036.log`.

Live full-music listening, the manual F1 sequence, post-F1 Pause response and
pointer-induced black flashes were requested from the user. The user responded:
"It works correctly. Live music is correct, sound effects are correct, graphics
are correct." The only reported remaining issue is that interlace is not always
steady, which the user considers tolerable. **This bounded Hired Guns live
acceptance check is ACCEPTED with that disclosed non-blocking caveat.**
The response supplies functional live confirmation in the context of the F1/
responsiveness check; it is not an instrumented latency measurement or exhaustive
game/disk coverage. LWA-CIA-003 live audio and LWA-HOST-001 functional confirmation
are closed for this session. Retain the residual interlace observation under
LWA-HOST-002: its cause is unclassified, and it is not established to be inherent
or unfixable. Do not reopen this accepted slice for general interlace research.
This is a live acceptance session, not a throughput sample (the running process
reports High priority; formal measurements require the separate Normal-priority
protocol). No engine code, default selection or benchmark disposition changed.

## Current contract

Performance clarification (2026-09-14): the [450 FPS same-work audit](LIGHTWEIGHT_A500_450_FPS_AUDIT.md)
reruns the original H6b1 workload on frozen/current engines with the original
runner: **447.43 / 445.31 FPS**, identical fingerprints, no demonstrated
material regression. On the current engine/runner, the measured cost ladder
is **449.28 serial-only, 432.51 with RAM DMA, 432.39 with TOD, 425.29 with
input, 402.49 full-width lowres, 393.89 hires**. Added display work accounts
for about 60% of the observed 0.313 ms/field increment. All measurements are
provisional engineering evidence, not gate acceptance or native gameplay FPS.
That audit made no engine change. Subsequent direct full-width output saved
1.58% in its confirmation series; the bounded RAM-DMA/generated-code follow-up
found no substantial safe simplification. Native display defect LWA-VIDEO-003
is now repaired by a one-line Copper comparison correction, not a renderer
workaround. Slow-disk reception is implemented experimentally; hardware phase
verification remains open, while H6d experimental acceptance was granted on
2026-09-14. See the audit and
dated follow-ups below for hashes, controls and measurement limitations.

Issue-handling clarification (2026-09-13): the follow-up register is
[Lightweight A500 issues to revisit](LIGHTWEIGHT_A500_ENGINE_ISSUES.md).
Confirmed defects, unverified hardware assumptions and unsupported features
must be distinguished. Unanswered hardware details are not automatic blockers
to further experimental implementation and must not be called verified.
The clarification alone did not grant a gate GO. The subsequent user-approved
H6b2 acceptance opened continuation to H6c; the subsequent explicit H6c
acceptance below opens continuation to H6d. The subsequent explicit H6d
acceptance opens H7 closure review. These acceptances do not authorize a
production switch or relabel historical results. Use the register's stable IDs
and revisit triggers for follow-up work.

- First machine: A500 PAL OCS, 68000, 512 KiB Chip RAM, 512 KiB slow RAM.
- Host workload: complete Lemmings frames with pixels and 48 kHz stereo audio.
- Final performance objective: 200 FPS, measured without host presentation or
  audio pacing.
- Early experimental switch threshold: 50 FPS and twice the frozen Legacy
  baseline, with timing checks passing and unresolved edges documented.
- Cycle model: one integer CPU-cycle clock with two CPU cycles per color clock
  and 454 CPU cycles per PAL line. Reset selects the 313-line OCS PAL long
  field (142,102 CPU cycles). H1 adds explicit 312/313-line field selection;
  automatic LACE-driven field alternation is part of the accepted H2 display
  path.
- CPU: reused Copper68k accurate M68000 interpreter through a direct IM68kBus.
- Production: Legacy remains unchanged. The early throughput threshold has been
  met against the prospective frozen reference, but host integration, session
  checks and a separate default-switch decision remain required.

## Current implementation

The new projects are:

- `CopperMod.Amiga.Lightweight`: engine boundary, memory map, clock, raw
  register owner and reusable framebuffer/audio buffers.
- `CopperMod.Amiga.Lightweight.Runner`: headless frame runner and checksum.
- `CopperMod.Amiga.Lightweight.Tests`: focused reset, clock, bus and boundary
  tests.

The project references Copper68k and CopperDisk (mount-time ADF encoding only).
It has no project reference to the old
  Amiga Bus, Scheduler, Display, requester or emulator host. The video buffer is
  a directly generated uncropped PAL field. H5c generates real 48 kHz stereo
  PCM into reusable buffers from the canonical-clock channel state. No
  successful benchmark may claim complete-machine coverage until H6 and
  native gameplay are connected.

The three projects are included in `CopperMod.sln`. The runner accepts an
extracted image or a ZIP containing one `.rom`/`.bin`/`.kick` entry and a ZIP
containing one `.adf` entry. The A500 256 KiB image is mapped in the upper ROM
window and is visible through reset overlay vectors.

## Frozen workload inputs

The intended Stage 1 fixture is the supplied native-ROM Lemmings workload:

| Input | SHA-256 |
| --- | --- |
| `D:\TestData\ROM\Kickstart_13.rom` | `EE05862D8102A08436AC4056DA7D549DB31625C7D47B24DFB7B3C9A5C113CA53` |
| `D:\TestData\TestImages\Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip` | `5FC56B436722688BA37836E273A3A1C84D724235A91B39272768FF3E4CAC0A59` |
| `D:\TestData\TestImages\Lemmings (1991)(Psygnosis)(Disk 2 of 2)[cr SR].zip` | `0C1CB9C49CBC29B56D2F974A90C510752E256148F8E8CC10CFBD026DB73DEA1A` |

The H6d opening script has since been extended to disk swapping, the menu,
briefing and running level. `Workloads/lemmings-native-level1-exploratory.json`
now reproducibly reaches active Lightweight gameplay; its separate digger
variant establishes input affecting terrain and saved lemmings. The 600-field
gameplay warmup ends at field 10,920 and the measured interval is
10,921–14,520. The completed 2026-09-14 paired series reproduces this same
unmodified script in both engines with stable within-engine fingerprints and
complete native pixels/48 kHz stereo. The shared complete-workload reproducibility
check is now **PASS**, superseding its earlier STOP. The reference is a newly
frozen Legacy build, not the missing original pre-project baseline; native output
geometry/audio algorithms and visible Legacy differences remain disclosed below.

## Stage 1 evidence (non-formal)

Commands run from `CopperMod.Amiga`:

```text
dotnet build ..\CopperMod.Amiga.Lightweight.Tests\CopperMod.Amiga.Lightweight.Tests.csproj -c Release --no-restore
dotnet test ..\CopperMod.Amiga.Lightweight.Tests\CopperMod.Amiga.Lightweight.Tests.csproj -c Release --no-build --logger "console;verbosity=minimal"
dotnet run --project ..\CopperMod.Amiga.Lightweight.Runner\CopperMod.Amiga.Lightweight.Runner.csproj -c Release --no-build -- --frames 10 --rom D:\TestData\ROM\Kickstart_13.rom --adf "D:\TestData\TestImages\Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip"
```

The original 100-frame ROM/ADF smoke observation of roughly **193--201 FPS**
used the superseded 312-line scaffold and predates the CPU-boundary work below.
It remains historical shell evidence only. It excludes real DMA, output
composition, audio generation and gameplay progress, and must not be compared
with G6/G7 or used as an early-switch measurement.

## Pre-device CPU-throughput checkpoint (2026-09-12)

The corrected 313-line shell was profiled before device implementation. The
unbatched NOP/BRA workload spent about 92% of host time in Copper68k instruction
execution, about 5% in the lightweight clock, and less than 2% in runner output
hashing. A large CPU publication-context copy represented about 7% inside the
CPU total. This established the CPU/host boundary, not output handling, as the
first bounded optimization target.

The retained optimization is deliberately narrow:

- Copper68k exposes its existing internal batch boundary to the Lightweight
  assembly without widening the public package API.
- Lightweight opts into a conservative batch containing only an exact
  NOP/short-BRA loop. Every instruction fetch still enters the ordinary bus;
  no Chip-RAM window, speculative replay, DMA batching or schedule cache is
  used.
- Admission peeks only at the current two-word loop shape and is repeated at a
  frame boundary. All other instruction streams immediately use the scalar
  interpreter path.
- STOP can advance device-only time directly to the next known boundary rather
  than executing one idle CPU cycle repeatedly.
- The clock owns incremental line/color-clock state and calls the machine
  directly, without a per-CCK delegate or duplicate video beam division.

An attempted general visible-bus batch was rejected during implementation:
Copper68k's broader tests exposed queue and delayed-prefetch parity failures for
other admitted instruction kinds. The retained opt-in cannot admit those kinds.

Focused tests now pass **11/11**, including scalar-versus-batched CPU state,
prefetch state, instruction-fetch address/cycle/kind sequence, 32 frame
boundaries, STOP fast-forward and 313-line wrap. The Amiga M68000 interpreter
suite passes **175/175** with seven optional diagnostics skipped. The complete
Copper68k test project has **1,470 passing, six skipped and six remaining
failures** in the already-present one-extension JIT descriptor work; none uses
the new conservative boundary. The twelve batch-specific failures exposed by
the rejected general implementation disappeared after the conservative scope
was applied.

Pinned diagnostic measurements used logical CPU 2 / mask 4, Normal priority,
600 warmup frames and 3,600 measured frames. They did not collect the complete
host-load-policy-v2 telemetry and therefore are not a formal production gate.

| CPU mode | Samples (FPS) | Median | Spread | Allocation |
| --- | --- | ---: | ---: | ---: |
| Scalar | 405.56, 375.79, 397.65 | 397.65 | 7.49% | 0 bytes |
| Conservative loop batch | 799.71, 853.20, 845.33 | 845.33 | 6.33% | 0 bytes |

Both modes produced cycle `596828400`, CPU fingerprint
`735A89DA3214B375`, and output fingerprint `3D49359399F6A383` after the
600+3,600 sequence. The conservative median is **2.13x** the scalar median and
passes the pre-device target of 650 FPS. This authorizes continuation of the
engine foundation; it does not establish complete-machine performance.

The native-ROM/ADF smoke stream at that pre-H1 checkpoint was a different,
incomplete scalar-heavy instruction workload. Its CPU and output fingerprints
matched with batching enabled or disabled. Because CIA, disk, DMA and output
devices were absent in that frozen build, it was neither native boot nor a
Lemmings benchmark and had no stage disposition.

### Simple-bus publication-context checkpoint (2026-09-12)

A sampling profile of that incomplete ROM stream attributed 6.4% of host time
to large structure copies below instruction-fetch callers. Copper68k was
constructing a complete `M68kInstructionFetchPublicationContext` for every
prefetch even when the bus did not implement `IM68kInstructionFetchWindowBus`.
Only that optional interface receives the context on the execution path.

The retained change makes context capture return an empty value for a plain
`IM68kBus`; instruction-window and deferred-timing consumers retain the full
snapshot path. It does not skip an instruction fetch, bus call, prefetch state
transition, cycle update or interrupt sample. An alternative that also changed
`TopUpPrefetchOne` to receive the context by readonly reference was rejected:
despite identical emulated fingerprints, its pinned median was 385.70 FPS
against the frozen baseline's 427.03 FPS. The original by-value hot-method
shape was restored.

For a plain bus, diagnostic snapshots can consequently report zero for the
otherwise unconsumed pending publication group/entry-cycle metadata. Those
fields do not drive CPU execution; the full diagnostics remain available on
the instruction-window path that uses them.

The retained candidate was measured against the frozen pre-change binaries in
interleaved order. Each process was confirmed at logical CPU 2 / mask 4 and
Normal priority on the Balanced power plan. Each run used 600 warmup and 3,600
measured frames and allocated zero steady-state bytes. Complete host-load-policy
v2 telemetry was not collected, so this remains a diagnostic optimization gate
rather than a formal production result.

| Build | Samples (FPS) | Median | Spread |
| --- | --- | ---: | ---: |
| Frozen pre-change | 435.58, 429.11, 420.72 | 429.11 | 3.46% |
| Plain-bus capture suppression | 448.42, 447.17, 444.06 | 447.17 | 0.98% |

The retained median improves by **4.21%**, clearing the predeclared 3% retention
threshold. Every run produced cycle `596828404`, CPU fingerprint
`80E90618BEADA095`, and output fingerprint `3D49359399F6A383`. Lightweight
tests pass 11/11 and the broad `M68k|M68020|M68040` filter passes 483/483 with
seven optional diagnostics skipped. The full Copper68k result remains 1,470
passing, six skipped and the same six one-extension descriptor expectation
failures recorded above.

## Current defect register and historical limitations

The maintained current register is [Lightweight A500 issues](LIGHTWEIGHT_A500_ENGINE_ISSUES.md).
At the 2026-09-14 readiness review, read-only RAM disk DMA, TOD, synchronized
input, hires output and native gameplay are implemented. Slow receiver phase
and other declared hardware uncertainties remain open; matched Legacy
acceptance and production integration remain unfinished.

**Historical snapshot below:** these pre-H6b2 limitations are retained as
earlier-stage evidence, not as the current implementation inventory. Later
dated gates supersede statements that RAM disk DMA, hires, TOD, input or native
gameplay are absent; they do not turn unresolved hardware edges into verified
behavior.

- No automatic ROM-directory discovery or legal-ROM selection policy yet; the
  runner accepts an explicit ROM path.
- H6a mounts an owned, read-only standard 880 KiB ADF and connects DF0 control
  and status pins. H6b1 adds encoded tracks, nominal-speed serial input,
  DSKBYTR/DSKSYNC and CIA-B index/FLAG. RAM disk DMA remains absent and
  enabling DSKLEN is still reported as unsupported. See the H6b1 checkpoint
  below for explicitly provisional serial/interrupt timing and media edges.
- Copper, OCS low-resolution bitplane DMA, direct normal/EHB output, manual
  Denise sprites, Agnus sprite DMA with delayed Denise presentation, and
  ascending/descending/fill area plus OCS line blits are implemented. H5 Paula
  register/manual timing, fixed-slot DMA, real stereo PCM and ADKCON modulation
  are active. Floppy RAM transfers remain absent. Canonical line BLTSIZE
  width two is supported; other widths are reported as unsupported instead of
  being assigned guessed behavior.
- CIA-A/B ports, data direction, CPU-clocked timers A/B and ICR are present.
  DF0 control/status pins and index-to-FLAG are connected in H6a/H6b1.
  TOD, CIA serial/CNT and general external FLAG behavior
  and keyboard input remain assigned to later H6 slices; other unconnected
  inputs currently pull up.
- Reset overlay currently exposes ROM vectors in the low address window; the
  complete A500 overlay/mirror map is still deferred with the full chipset.
- The framebuffer is a reusable 454x313 uncropped PAL raster. Hires, HAM and
  dual-playfield output are explicit unsupported modes and invalidate a runner
  result instead of falling through to Legacy.
- H2 supports the documented low-resolution OCS fetch window. Undocumented or
  illegal DDF values and the 227-CCK wrap-dummy bus value remain unverified.
  The fixed PAL blanking aperture follows the repository's current
  hardware-backed capture profile; external-blank behavior is deferred.
- The direct slot owner resolves mandatory refresh, Paula audio, bitplane,
  sprite, Copper and area/line-blitter outputs. Disk ownership enters at H6.
- H5b's digital timing cases use the scoped Paula oracle. CPU AUDxDAT writes
  during the two startup states, sub-CCK setup/hold windows, and repeated
  off/on transitions inside an already accepted startup transfer remain
  unverified. Ordinary queued holding-latch overwrite is covered; it does not
  establish all CPU/DMA write-signal races. Very-short-period underrun
  expectations are model-level checks pending a focused external hardware
  capture. H5c uses an ideal linear DAC mix with sample-area averaging;
  analog filter/LED-filter response, DAC nonlinearity and analog volume-PWM
  reconstruction remain unmodeled. ADKCON changes exactly coincident with
  channel transitions have no independent hardware capture in this gate.
- Sprite vertical-stop/previous-line-data edges, an isolated denied-DATAA but
  granted-DATB transfer, DDF/refresh conflict corruption, and the documented
  too-early BPL1DAT miss cases remain unverified. H4c implements and tests the
  hardware-backed complete control pair, denied-DATB reuse, attached-partner
  and ordinary BPL1DAT output-enable paths; it does not generalize those open
  edges into claimed behavior.
- BPLCON0 LACE alternation is implemented. VPOSW V8/VHPOSW counter
  synchronization and pathological late beam-counter writes remain unresolved.
  A late LOF shortening can no longer create a past frame deadline; the current
  line is completed conservatively until a hardware oracle establishes finer
  behavior.
- `RESET` clears the implemented external-device state and reasserts overlay,
  but the exact reset-pin phase and which beam state survives still need a
  hardware-backed boundary test before native-boot acceptance.
- No complete-machine throughput or compatibility gate is open for this
  engine. H2 measurements are hardware-slice results only.
- The native-ROM smoke run is not a gameplay result: missing devices currently
  allow the CPU to advance through an incomplete custom-chip map. This is an
  explicit unresolved defect, not evidence of boot success.

The prior G6/G7 results remain historical evidence for the old SlotKernel
route. They are not relabeled as results for this replacement engine.

## Hardware-path staged gates

The former single deferred hardware stage is split so a correctness or host
cost regression stops at the device that introduced it. The active gate is
the only hardware scope authorized for implementation; later gates may expose
small fixed fields or call boundaries needed by the active gate, but must not
silently acquire hardware behavior.

| Gate | Hardware slice | Status |
| --- | --- | --- |
| H0 | Independent CPU, memory, clock shell and frozen throughput controls | **GO** |
| H1 | PAL beam/field timing, refresh ownership, CPU Chip-RAM phases, interrupt visibility and essential CIA timing | **GO** |
| H2 | Copper, bitplanes and direct framebuffer composition | **GO** |
| H3 | Blitter, including nice/nasty BLTPRI and Copper BFD observation | **GO** |
| H4 | Sprite DMA and delayed Denise output | **GO / accepted** |
| H5a | Paula register owner, manual two-byte playback and audio IRQ phases | **GO / accepted** |
| H5b | Four fixed-slot audio-DMA channels, reload and delayed INTREQ2 | **GO / accepted** |
| H5c | Direct deterministic 48 kHz stereo output and ADKCON modulation | **GO / accepted** |
| H6 | ADF floppy path, remaining CIA drive/input lines and native Lemmings workload | **H6a, H6b, H6c and H6d accepted / experimental GO within their documented models** |
| H7 | Complete-engine profiling and optimization to 200 FPS / 5 ms | **GO / performance stage closed within accepted H6d scope: 343.10 FPS; not a production or full hardware-conformance claim** |

H1 is itself accepted in three bounded steps:

1. **H1a -- beam and physical bus spine:** monotonic CPU-cycle/CCK state,
   default long-field cadence, explicit short-field selection, four mandatory
   OCS refresh slots per rasterline, single-word grants, and separated 68000
   longword phases across line and frame boundaries.
2. **H1b -- interrupt path:** one INTENA/INTREQ owner, set/clear semantics,
   level encoding, CPU-visible transition cycles, vertical-blank latching and
   STOP wakeup/68000 autovector delivery.
3. **H1c -- essential CIA path:** CIA-A/B register decode, ports and data
   direction, CPU-clocked timers A/B, ICR read/clear and mask semantics, and
   CIA-A overlay output. TOD, serial keyboard protocol and disk control pins
   may remain declared omissions until their owning gates.

H2 is implemented and measured as three independent requester/output slices:

1. **H2a -- Copper:** one retained address-input/data-output word, MOVE at the
   second-word output phase, live WAIT/SKIP comparison, COPJMP, DMA gating and
   frame restart. At H2 acceptance BFD observed an explicitly idle blitter;
   H3a subsequently connected the active-blitter completion boundary.
2. **H2b -- bitplane input:** OCS DDF control, low-resolution HRM plane-slot
   order, retained addresses/data, pointer and modulo advancement, and direct
   contention with Copper and CPU. Hires and undocumented/illegal DDF modes
   remain explicit omissions unless added with their own focused evidence.
3. **H2c -- direct output:** a reusable uncropped PAL raster target, Denise
   data latches/shifters, DIW visibility, palette conversion and deterministic
   border/blanking output. Host cropping and scaling remain outside the engine.

H3 is split into three bounded implementation and regression gates. No H3
sub-gate authorizes H4 work:

1. **H3a -- area/control spine:** ascending area blits, retained DMA
   address/data phases, BUSY/BLTZERO/INTREQ publication, DMA pause/resume,
   nice-mode three-grant CPU yield, nasty BLTPRI, and Copper BFD observation
   of the live completion boundary. BLTPRI is a delayed control effect, not a
   second scheduler timeline.
2. **H3b -- descending and fill:** descending pointers and modulos, A/B shift
   carry, inclusive/exclusive fill carry, and the fill idle-C phase. H3a must
   retain its timing and throughput disposition before this slice advances.
3. **H3c -- line mode:** OCS line octants, accumulator/sign/single-dot rules,
   texture phase, the idle/C/idle/D cadence, and final physical completion.

Each slice requires focused physical-phase tests, zero steady-state managed
allocation, an inactive-path retention check, and an active synthetic blitter
measurement. H3 was held STOP until all three slices passed and the final H3
measurement was accepted explicitly.

#### H3a area/control gate (2026-09-12)

H3a adds one compact fixed-field area sequencer to the shared clock. It
retains each accepted channel address and write value through the following
physical output phase, gives refresh/bitplane/Copper their fixed priority, and
does not allocate event or requester objects. DMACONR now publishes BUSY and
BLTZERO. Destination-only startup retains the six-input OCS pipeline and can
publish main completion one CCK before its already-accepted final D output
drains. Copper BFD observes that drain, rather than the earlier CPU-visible
BUSY edge.

Nice mode yields the fourth otherwise-eligible blitter DMA output to a pending
CPU Chip-RAM/custom request after three committed blitter outputs. Nasty mode
keeps each eligible slot. BLTPRI changes become effective two CCKs after their
DMACON transfer and affect future admissions only. A stopped or disabled DMA
channel retains an already accepted output but does not advance later phases.

The focused Release suite passes **65/65**. Its H3a coverage includes all 256
area minterms and all 16 channel combinations, masks/shifts/pointers/modulos,
the D-only final pipeline, DMA pause/resume, refresh and bitplane priority,
nice/nasty CPU contention, delayed BLTPRI, INTREQ/DMACONR state, and active
Copper BFD. Descending/fill and line mode are explicit unsupported results,
not silent fallbacks.

The formal H3a comparison used the same runner in both directories, logical
CPU 2 / mask 4, verified efficiency class 1, physical core 2 with protected
logical processors 2 and 3 / mask 12, Normal priority, Balanced power plan,
600 warmup frames, 3,600 measured frames and interleaved C/R/R/C/C/R order.
Host-policy-v2 telemetry was valid. A 27.4% selected-core observation lasted
only 0.24 seconds, below the required continuous ten-second rejection window;
all other selected/sibling/aggregate observations were also below the
sustained threshold.

| Cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H3a candidate inactive | 13126.96, 12969.71, 13072.14 | 13072.14 | 1.20% | 0.076499 ms |
| Frozen H2 reference inactive | 13223.72, 13318.92, 13333.06 | 13318.92 | 0.82% | 0.075081 ms |
| H3a candidate full raster + DMA + blitter | 471.47, 472.32, 458.45 | 471.47 | 2.94% | 2.121026 ms |
| Frozen H2 reference with identical setup but no blitter owner | 553.33, 560.57, 552.51 | 553.33 | 1.46% | 1.807240 ms |

Inactive retention is **98.15%** and cumulative active retention is **85.21%**.
The active candidate remains **2.36x** the final 200 FPS target at this
incomplete-machine point. Every sample allocated zero managed bytes and
reported no unsupported behavior. Inactive runs shared cycle `596828400`, CPU
fingerprint `7A4ADB9B73247EED`, hardware fingerprint `6A26103B37A801F6`
and output fingerprint `BF2D06EE5CCE0B83`. Active H3a used hardware/output
fingerprints `98E946E20BFA5ABA` / `E1A4D8D8CCDF1919`; the frozen no-blitter
reference used `68EE0A28AF338A15` / `789774D3B050972E`.

Build identity was runner SHA-256
`8E825C90B4A941DB60CAC48164E695263596EAD7D35437AEEF775E635181BCE8`,
candidate engine
`1BBC84D440427B206FFD016B7B869FCB44A6E7915085A59DF55F8F4BBE334440`,
frozen H2 engine
`A91894CD025AA1C3226C6C7B52D5797845359C2C6EB24BC4334D830033E54F5B`,
and shared Copper68k
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The measured H3a build is frozen at
`.codex-tmp/lightweight-h3a-accepted-20260912`.

H3a dispositions are **Timing GO**, **Inactive host path GO**, **Active host
path GO**, and **Cumulative budget GO**. At the H3a close, H3 remained STOP
while H3b and H3c were incomplete.

#### H3b descending/fill gate (2026-09-12)

H3b extends the same area sequencer; it adds no requester layer or alternate
clock. Descending mode decrements retained channel pointers, subtracts the
even portion of each signed modulo at row boundaries, and uses the descending
A/B 32-bit shifter composition. Inclusive/exclusive fill consumes bits 0
through 15, carries across words, resets its initial carry per row, and adds
the physical idle-C control phase only when C DMA is absent.

The focused Release suite passes **71/71**. Six new checks cover all 16 shift
values in descending mode, backward pointers, odd-modulo low-bit masking,
inclusive/exclusive fill with both carry inputs, row carry reset, and the
extra idle-C timing. The full H3a set remains green.

A strict controlled regression rerun against frozen H3a produced identical
ascending hardware/output fingerprints. Inactive retention was **102.16%**
(candidate median 12090.49 FPS; reference 11834.71 FPS), and ascending active
retention was **99.13%** (458.09 versus 462.09 FPS). Spreads were 3.66%/4.68%
inactive and 4.40%/1.42% active. This is the H3a-retention GO gate.

The separate descending/exclusive-fill workload used the same host controls,
sample order and 600/3,600 frame protocol as H3a. The frozen H3a reference ran
the identical setup with its accepted ascending blitter; cross-engine
fingerprint equality was intentionally not required for the newly implemented
behavior.

| Cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H3b candidate inactive | 11805.98, 11474.78, 11701.39 | 11701.39 | 2.83% | 0.085460 ms |
| Frozen H3a reference inactive | 11681.47, 11817.76, 12143.81 | 11817.76 | 3.91% | 0.084618 ms |
| H3b descending/fill active | 460.43, 456.06, 444.34 | 456.06 | 3.53% | 2.192694 ms |
| Frozen H3a ascending active | 461.28, 459.39, 462.76 | 461.28 | 0.73% | 2.167881 ms |

Inactive retention is **99.02%**, active retention is **98.87%**, and the H3b
candidate remains **2.28x** the final 200 FPS target. All samples were
allocation-free, deterministic and supported. Inactive fingerprints remain
cycle `596828400`, CPU `7A4ADB9B73247EED`, hardware
`6A26103B37A801F6`, output `BF2D06EE5CCE0B83`. H3b active used
hardware/output `B1D2016D44E6C51A` / `75865E01A7B91238`; its H3a reference
used `98E946E20BFA5ABA` / `E1A4D8D8CCDF1919`.

Build identity was runner SHA-256
`D7B2BC45AAED824B43A8AE5A48F1E92AE0F3663694A7031AEC44AFFE78FAB301`,
candidate engine
`416EDB99B0040A2941342E14C728F9236BE4E7F9A575F15F37309A4213A194D3`,
frozen H3a engine
`1BBC84D440427B206FFD016B7B869FCB44A6E7915085A59DF55F8F4BBE334440`,
and shared Copper68k
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The measured H3b build is frozen at
`.codex-tmp/lightweight-h3b-accepted-20260912`.

H3b dispositions are **Timing GO**, **H3a regression GO**, **Inactive host
path GO**, **Active host path GO**, and **Cumulative budget GO**. At the H3b
close, H3 remained STOP pending H3c and an explicit overall disposition.

#### H3c line-mode gate (2026-09-12)

H3c extends the same fixed-field blitter sequencer rather than adding a line
requester or another clock. It retains the four non-reserving startup CCKs and
four physical phases per pixel: `idle/C/idle/D` without B, or `B/B/C/D` when
B DMA is enabled. Startup and idle phases overlap fixed DMA because they do
not reserve Chip RAM. An accepted B/C/D address/data phase survives fixed-slot
contention until its following output. The final D output owns BUSY/INTREQ
completion; a mandatory refresh collision therefore delays both the memory
effect and the completion edge.

The implementation covers all OCS octants, accumulator/sign stepping,
single-dot suppression, texture start phase and per-pixel B pattern reloads.
It also preserves the requester-scoped OCS evidence already captured by the
repository: C enable is required for drawing, D enable does not suppress the
write, BLTDPT supplies only the first pixel, later writes use BLTCPT, BLTCMOD
supplies the row stride, BLTDMOD and BLTALWM are ignored, and BLTDPT finishes
as a mirror of BLTCPT. BLTAPT is updated with the line error only when A is
enabled. A deferred BLTSIZE restart receives a fresh four-CCK startup instead
of consuming a startup phase on the preceding blit's final output.

An earlier measured candidate incorrectly blocked non-reserving line startup
and idle clocks behind fixed DMA. The post-implementation phase audit rejected
that candidate before overall H3 acceptance. The corrected build permits the
overlap, retains contention only for physical B/C/D outputs, adds a boundary
test for both cases, and is the only build represented by the evidence below.

The full focused Release suite passes **82/82**. Twelve H3c checks replace the
former explicit-unsupported row and cover startup/cadence/final completion,
all octant/sign combinations, all 16 texture phases, C/D enable behavior,
BLTALWM, first-D/later-C addressing, BLTCMOD versus BLTDMOD, disabled-A
writeback, double B reload and even pattern modulo, single-dot suppression,
deferred restart, noncanonical-width reporting, non-reserving refresh overlap,
and final-write contention at a line-crossing refresh slot. H3a and H3b tests
remain green.

Before measuring line mode, the current candidate was compared with frozen
H3b using the accepted ascending and descending/fill workloads. Both
regressions used logical CPU 2 / mask 4, verified efficiency class 1,
physical core 2 with protected logical processors 2 and 3 / mask 12, Normal
priority, Balanced power plan, 600 warmup frames, 3,600 measured frames,
interleaved C/R/R/C/C/R order, and valid host-policy-v2 telemetry.

| Regression cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H3c candidate inactive, area run | 12932.22, 12980.05, 13130.66 | 12980.05 | 1.53% | 0.077041 ms |
| Frozen H3b inactive, area run | 12994.49, 13103.72, 13283.91 | 13103.72 | 2.21% | 0.076314 ms |
| H3c candidate ascending active | 452.87, 460.31, 456.43 | 456.43 | 1.63% | 2.190916 ms |
| Frozen H3b ascending active | 456.08, 463.97, 467.66 | 463.97 | 2.50% | 2.155312 ms |
| H3c candidate inactive, fill run | 13187.17, 13113.53, 12973.91 | 13113.53 | 1.63% | 0.076257 ms |
| Frozen H3b inactive, fill run | 13212.31, 13258.12, 13170.52 | 13212.31 | 0.66% | 0.075687 ms |
| H3c candidate descending/fill active | 453.92, 453.74, 445.77 | 453.74 | 1.80% | 2.203905 ms |
| Frozen H3b descending/fill active | 460.82, 457.21, 447.14 | 457.21 | 2.99% | 2.187179 ms |

Ascending inactive/active retention is **99.06% / 98.37%**. Fill-run
inactive/active retention is **99.25% / 99.24%**. Candidate and reference
fingerprints are identical within each accepted regression workload, and all
samples allocate zero managed bytes and report no unsupported behavior.

The dedicated H3c workload executes two 1,024-pixel, B-enabled OCS line blits
per complete raster frame alongside the accepted Copper, bitplane and direct
output paths. The frozen H3b build receives the identical runner and setup but
uses its capability-detected ascending-area reference. Cross-engine
fingerprint equality is therefore neither expected nor required; each build
must be deterministic internally.

| H3c cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H3c candidate inactive | 13188.26, 13140.61, 13093.73 | 13140.61 | 0.72% | 0.076100 ms |
| Frozen H3b reference inactive | 13167.06, 13285.51, 13250.14 | 13250.14 | 0.89% | 0.075471 ms |
| H3c line active | 477.65, 488.18, 484.91 | 484.91 | 2.17% | 2.062238 ms |
| Frozen H3b ascending active reference | 461.88, 465.02, 466.14 | 465.02 | 0.92% | 2.150445 ms |

Inactive retention is **99.17%**, active-reference retention is **104.28%**,
and the line candidate is **2.42x** the final 200 FPS target at this
incomplete-machine point, consuming 2.062238 ms and leaving 2.937762 ms of the
5 ms frame budget. Every H3c sample is allocation-free, deterministic and
supported. Inactive fingerprints are cycle `596828400`, CPU
`7A4ADB9B73247EED`, hardware `6A26103B37A801F6`, output
`BF2D06EE5CCE0B83`. Line-active fingerprints are cycle `596828454`, CPU
`6BBCE8FE0EDB204B`, hardware `C7BABD1B0C895E54`, output
`A858D83B32C91B3E`; the frozen ascending reference retains cycle
`596828400`, CPU `7A4ADB9B73247EED`, hardware `98E946E20BFA5ABA`, output
`E1A4D8D8CCDF1919`.

Build identity for all three controlled H3c series was runner SHA-256
`E75E0F99CC0BC59E062BE60B71F839DE82F1DEFC0BB811B20D12FD87E75C4B26`,
candidate engine
`D246BB0BCEBCB37616E635809C045ECE136ED85778149401D79FA4A8510881D2`,
frozen H3b engine
`416EDB99B0040A2941342E14C728F9236BE4E7F9A575F15F37309A4213A194D3`,
and shared Copper68k
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The measured candidate is frozen at
`.codex-tmp/lightweight-h3c-accepted-20260912`.

H3c dispositions are **Timing GO**, **H3a regression GO**, **H3b regression
GO**, **Inactive host path GO**, **Active host path GO**, and **Cumulative
budget GO**. At this sub-gate close, overall H3 remained **STOP pending explicit
acceptance** and H4 was not open.

#### Post-H3 optimization checkpoint (2026-09-12)

Before requesting the overall H3 disposition, the corrected H3c build was
profiled again. The first release trace charged 87.86% inclusive time to the
single clock advance, while a no-inline diagnostic separated the recurring
cost into clock/dispatcher bookkeeping, Copper, bitplanes and direct output.
The retained changes simplify those paths without suppressing an emulated
cycle, pixel, audio-buffer result or DMA phase:

- the clock finishes an odd CPU-cycle phase separately and then uses a direct
  aligned-CCK loop;
- Release omits device-publication assertions and long-cycle overflow guards;
  Debug retains the publication assertions, and signed 64-bit time still spans
  more than forty thousand years at the A500 clock rate;
- repeated bitplane eligibility work is computed once per CCK, and DDFSTRT is
  not reread while a fetch run is already active;
- a STOPped CPU masking level 6 or 7 advances the same device clock directly
  to the frame boundary because this machine exposes no interrupt above IPL6;
  `AdvanceTo` requests optimized JIT code so the fixed 600-frame warmup does
  not depend on late tier promotion;
- after a complete device tick, active video is the known earliest dense CCK,
  so only fixed sparse interrupt/CIA deadlines can precede it; mid-device
  register writes retain the full minimum calculation; and
- when bitplane DMA is enabled but not in a fetch run, it publishes only the
  next DDF permit or live DDFSTRT comparator phase. Register writes still wake
  it on the following CCK, and active fetch/output remains per-CCK.

This is direct phase publication, not a schedule cache or range batch. Debug
testing caught and rejected an earlier over-broad next-cycle assumption made
inside a Copper register write; the accepted optimization applies that
invariant only after all devices finish the current tick. The renderer's
two-pixel fusion was rejected at 99.84% active retention, whole-dispatcher
inlining was rejected after expanding the clock method from 379 to 2,361
bytes and producing 98.63% active retention, and a Copper output-phase
specialization was rejected because it did not improve throughput.

The current focused suite is **83/83 in both Release and Debug**. The added
test proves that inactive bitplanes publish the next DDF comparator rather
than polling every CCK. All original H3 timing tests remain green.

The optimized engine was then compared directly with the corrected,
pre-optimization H3c engine for all three H3 workloads. Each formal series
used logical CPU 2 / mask 4, verified efficiency class 1, physical core 2 with
protected logical processors 2 and 3 / mask 12, Normal priority, Balanced
power plan, 600 warmup frames, 3,600 measured frames, three interleaved
samples, and valid host-policy-v2 telemetry.

| Workload/cohort | Optimized samples (FPS) | Optimized median / spread | Pre-optimization samples (FPS) | Pre-optimization median / spread | Gain |
| --- | --- | ---: | --- | ---: | ---: |
| H3a inactive | 28034.50, 27562.43, 27792.08 | 27792.08 / 1.70% | 12944.55, 13126.66, 12870.79 | 12944.55 / 1.98% | +114.70% |
| H3a area active | 540.64, 539.67, 545.11 | 540.64 / 1.01% | 455.56, 454.64, 464.58 | 455.56 / 2.18% | +18.68% |
| H3b inactive | 28239.95, 27994.80, 28216.86 | 28216.86 / 0.87% | 13051.91, 13151.89, 12966.08 | 13051.91 / 1.42% | +116.19% |
| H3b fill active | 532.10, 526.00, 534.42 | 532.10 / 1.58% | 459.15, 448.95, 455.13 | 455.13 / 2.24% | +16.91% |
| H3c inactive | 27942.13, 27635.51, 28257.90 | 27942.13 / 2.23% | 13030.46, 12696.57, 13080.15 | 13030.46 / 2.94% | +114.44% |
| H3c line active | 561.00, 562.87, 561.43 | 561.43 / 0.33% | 485.07, 487.67, 485.69 | 485.69 / 0.54% | +15.59% |

Optimized median frame times are 1.849660 ms for area, 1.879346 ms for fill
and 1.781166 ms for line mode. The line workload is **2.81x** the final 200 FPS
target at this incomplete-machine point and leaves **3.218834 ms** of the 5 ms
frame budget. Every result is allocation-free and reports no unsupported
feature. Candidate and reference fingerprints are identical within every
workload; H3c line retains cycle `596828454`, CPU `6BBCE8FE0EDB204B`, hardware
`C7BABD1B0C895E54` and output `A858D83B32C91B3E`.

The controlled comparison used runner SHA-256
`E75E0F99CC0BC59E062BE60B71F839DE82F1DEFC0BB811B20D12FD87E75C4B26`,
optimized engine
`3A22D5CF139979DC12A31C08C48A964BCE9E40DBD0C6B6A9DD7A25DFDBB2344F`,
pre-optimization engine
`D246BB0BCEBCB37616E635809C045ECE136ED85778149401D79FA4A8510881D2`,
and Copper68k
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The accepted optimized build is frozen at
`.codex-tmp/lightweight-h3-optimized-accepted-20260912`.

The post-H3 optimization dispositions are **Timing regression GO**, **H3a
throughput GO**, **H3b throughput GO**, **H3c throughput GO**, **Host policy
GO**, and **Allocation GO**. These results strengthened readiness but did not
implicitly approve overall H3.

#### H3 overall acceptance (2026-09-12)

The user explicitly declared **H3 GO** after reviewing the complete H3 evidence
and the post-H3 optimization checkpoint. H3 is accepted. This opens H4 for
sprite DMA and delayed Denise output; it does not accept H4, native gameplay,
the complete-machine gate, or the final 200 FPS objective.

H4 is split into three bounded implementation and regression gates:

1. **H4a -- manual Denise sprite path:** all eight `SPRxPOS/CTL/DATA/DATB`
   register sets, the following-input-stage latch, MSB-first two-bit output,
   OCS palette groups, attached pairs, fixed sprite priority, display-window
   clipping and normal-playfield `BPLCON2` priority. This gate does not claim
   sprite DMA.
2. **H4b -- Agnus sprite DMA:** all eight pointer pairs, master/SPREN gating,
   fixed `$18..$36` slots, retained address/data phases, causal pointer
   advancement, control/data state transitions, terminators, CPU/Copper/blitter
   contention and OCS DDF slot stealing. No fixed slot may be replayed after a
   late enable or pointer write.
3. **H4c -- delayed DMA presentation and close:** feed granted data into the
   same Denise latches, cover attached/missing-word behavior and the BPL1DAT
   output-enable edge, then run allocation, inactive-path and cumulative active
   performance comparisons against the frozen optimized H3 build.

Each sub-gate requires focused physical-phase tests and a measured regression
checkpoint. H4 remains open until all three pass and the final H4 evidence is
accepted explicitly.

#### H4a manual Denise sprite gate (2026-09-12)

H4a adds one compact eight-channel Denise owner for `SPRxPOS`, `SPRxCTL`,
`SPRxDATA` and `SPRxDATB`. CPU and Copper writes still enter the single custom
register implementation; the sprite owner consumes the resulting input one
CCK later. `DATA` arms manual output, `CTL` disarms it, and an accepted line
uses line-local two-bit shifters with MSB-first output. The compositor covers
the four OCS sprite palette groups, attached odd/even pairs, fixed pair and
within-pair priority, normal-playfield `BPLCON2` placement, transparency and
display-window clipping. This gate makes no sprite-DMA, pointer, arbitration
or BPL1DAT output-enable claim.

The first direct implementation scanned and decoded all eight sprite channels
at every visible pixel. Its approximately 367.5 FPS result against a roughly
553.5 FPS H3 reference was rejected before gating. The retained implementation
is the simpler physical shape: decode eight vertical comparators once per
line, latch a channel only at its horizontal comparator, then shift only live
channels. A tiny transparent-interval fast path stays in Denise's pixel loop;
register/comparator maintenance and live composition are bounded non-inlined
paths. The normal non-overlap case advances one compact per-channel state
directly, while the general pair path remains available for overlapping and
attached output. There is no schedule cache, replay, event allocation or
per-pixel eight-channel scan.

Eleven focused sprite cases bring both the Debug and Release lightweight
suites to **94/94**. They cover the following-CCK DATA latch and arming edge,
MSB-first output/transparency, all eight register sets and palette groups,
attached-pair color, the required armed even partner, fixed even/odd and pair
priority, CTL disarm and zero-height stale-data exclusion, both sides of the
normal-playfield BPLCON2 priority edge, a DATA write after the horizontal
comparator, and shifter advancement while the first four pixels are clipped
by DIW. Expectations use the repository's hardware-backed sprite conformance
matrix and documented OCS Denise/Agnus observations; Legacy equality is not
used as an oracle.

The formal comparison used an identical runner and Copper68k assembly in both
directories, logical CPU 2 / mask 4, verified efficiency class 1, physical
core 2 with protected logical processors 2 and 3 / mask 12, Normal priority,
Balanced power plan, 600 warmup and 3,600 measured fields in interleaved
C/R/R/C/C/R order. The active cohort is the accepted H3 line workload plus
eight manually armed sprites, including two attached odd channels. The frozen
H3 engine receives the identical register setup but has no sprite output.
Host-policy-v2 telemetry was valid: brief preflight readings above 25% did not
persist for the required ten seconds, and live intervals remained below the
sustained rejection limits.

The first Windows PowerShell 5.1 invocation stopped before its first sample
because that runtime exposes a null `ProcessStartInfo.ArgumentList`; it
produced no gate result. The harness now uses the structured argument list
when available and a quoted controlled-argument fallback on 5.1. A twelve-run
one-frame launch/telemetry stress traversed both builds and cohorts after the
fix; its intentionally non-warmed allocation/FPS values are not gate evidence.
The complete series below is the clean PowerShell 7 result.

| H4a cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H4a candidate inactive | 27508.74, 26193.31, 28158.39 | 27508.74 | 7.14% | 0.036352 ms |
| Frozen H3 inactive | 27237.20, 27789.53, 27982.66 | 27789.53 | 2.68% | 0.035985 ms |
| H4a manual sprites active | 508.93, 506.05, 501.31 | 506.05 | 1.51% | 1.976089 ms |
| Frozen H3 line active, identical setup | 559.64, 561.39, 560.00 | 560.00 | 0.31% | 1.785714 ms |

Inactive retention is **98.99%**, above the 97% requirement. Active retention
is **90.37%**, above the declared H4a 90% checkpoint, and active throughput is
**2.53x** the final 200 FPS target at this incomplete-machine point. Manual
sprite presentation adds approximately 0.190375 ms to this deliberately
sustained workload and leaves **3.023911 ms** of the 5 ms frame budget. Every
sample allocated zero steady-state managed bytes, reported no unsupported
behavior, and reproduced its within-build fingerprint.

Inactive samples completed at cycle `596828400` with CPU/hardware/output
fingerprints `7A4ADB9B73247EED` / `6A26103B37A801F6` /
`BF2D06EE5CCE0B83`. Active samples completed at cycle `596828454` with
CPU/hardware fingerprints `6BBCE8FE0EDB204B` / `C7BABD1B0C895E54`;
candidate output was `9A5752F9E2F342C4` and the no-sprite H3 reference was
`A858D83B32C91B3E`.

Build identity is identical runner SHA-256
`65395E06715D8327FC41559AAA5237E3AE9FFC6ECA1BD424974D58828D796A0F`,
H4a engine SHA-256
`8A33D0AF010B7727B68F4A2301BDC70E3D7263BADF5DB97804940EAB327B7C82`,
frozen H3 engine SHA-256
`3A22D5CF139979DC12A31C08C48A964BCE9E40DBD0C6B6A9DD7A25DFDBB2344F`,
and shared Copper68k SHA-256
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The measured candidate is frozen at
`.codex-tmp/lightweight-h4a-accepted-20260912`.

H4a dispositions are **Timing GO**, **Inactive host path GO**, **Active host
path GO**, **Cumulative budget GO**, **Host policy GO** and **Allocation GO**.
Overall **H4a result GO**. The user's subsequent instruction to proceed with
H4 records explicit acceptance of this stop/go boundary. H4 as a whole remains
open; DMA-fed presentation, missing-word behavior and the BPL1DAT output-enable
edge remain unimplemented.

#### Accepted H4a hot-path checkpoint (2026-09-12)

Before H4b, two measured H4a-only simplifications were retained. Custom
register writes now route directly by offset to their owning device instead of
notifying every device and rebuilding the device minimum for unrelated writes.
The physical video CCK now emits its two low-resolution pixels together while
still applying bitplane reload, DIW clipping and sprite priority independently
at each pixel. The overwhelmingly common single live sprite shifts two bits in
one bounded operation; comparator, dirty-register, overlap and attached-pair
edges retain the scalar/general path.

The first routing experiment reproduced cycle, CPU, hardware and output
fingerprints exactly and improved a short interleaved H4a cohort by about
3.8%. The paired-pixel experiment then passed a new discriminating adjacent-
pixel sprite case and the complete **95/95** Debug and Release suites. Its
three-sample candidate median was **523.92 FPS** versus **514.10 FPS** for the
direct-routing checkpoint, a further **1.91%**, with 1.25% and 1.46% spreads.
Every sample retained cycle `596828454`, CPU/hardware/output fingerprints
`6BBCE8FE0EDB204B` / `C7BABD1B0C895E54` / `9A5752F9E2F342C4` and zero
steady-state allocation. A no-inline pair variant was rejected at roughly
496--506 FPS versus 522--523 FPS for the retained shape.

These were short pinned diagnostic comparisons, not a replacement formal H4a
series. The retained checkpoint is frozen at
`.codex-tmp/lightweight-h4a-optimized-checkpoint-20260912`, with engine
SHA-256 `1E3A66141AC21C1760BCCFB7F3151EFC72F764FBAF65B1F630BBDE50CA467684`,
runner SHA-256
`26CA8EE9B2C4AF598CBF7C3E8BC5C75ED132B0398FF26D188CBE8AC0276BBD8E`,
and Copper68k SHA-256
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.

#### H4b Agnus sprite-DMA gate (2026-09-12)

H4b adds one fixed-field Agnus sprite sequencer, separate from Denise's H4a
shifters. Each of the eight channels owns a live 19-bit pointer and compact
control/data state. Address input occurs one CCK before the applicable fixed
OCS output at `$18..$36`; an accepted address survives later DMACON or pointer
writes, Chip RAM is sampled at output, and only a granted word advances its
pointer. Master DMA and SPREN are both required. A late enable or pointer write
publishes only the next physical input and never replays an elapsed slot.

The sequencer fetches POS/CTL while inactive, DATA/DATB on active vertical
rows, the next control pair at VSTOP, and stops at the zero terminator. It
resets control eligibility at the field boundary without replacing pointer or
data latches. Standard DDF leaves all eight channels available. Early
low-resolution DDF applies the OCS stolen-suffix rule, including the `$18`
special case in which sprite 0 POS remains available but its CTL word does not.
Bitplanes resolve first; a real pending sprite output then blocks incoming
Copper, blitter control and CPU ownership. Empty fixed sprite opportunities do
not reserve the bus.

Ten focused cases cover all eight pointer pairs and both exact slots,
master/SPREN gating, late enable, retained address versus subsequent pointer
input, word-by-word control/data/pointer advancement, VSTOP control restart,
the zero terminator, DDF starts `$18/$1C/$20/$30/$38`, and CPU/Copper/blitter
contention. Expectations follow the repository's hardware-backed OCS fixed-
owner and DDF/sprite conformance evidence; Legacy equality is not the oracle.
The complete lightweight suite passes **105/105** in both Debug and Release.

The in-gate comparison used the same runner and Copper68k assembly in both
directories, the verified logical CPU 2 / mask 4 P-core at Normal priority,
300 warmup and 1,800 measured fields, and interleaved C/R/R/C/C/R order. It was
a short pinned gate profile permitted by the per-gate contract; full
host-load-policy-v2 telemetry was not collected, so it is not a formal
production measurement.

| H4b cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H4b candidate, H4a path only | 529.02, 543.98, 539.03 | 539.03 | 2.78% | 1.855184 ms |
| Frozen optimized H4a, same inactive setup | 542.65, 540.53, 542.93 | 542.65 | 0.44% | 1.842808 ms |
| H4b sprite DMA active | 497.32, 486.16, 486.15 | 486.16 | 2.30% | 2.056936 ms |
| Frozen optimized H4a, identical DMA setup but no requester | 538.74, 545.83, 503.65 | 538.74 | 7.83% | 1.856183 ms |

Inactive retention is **99.33%**, above the required 97%. The new active
candidate freezes H4b's first active baseline at **486.16 FPS**, **2.43x** the
final target. Executing all eight active DMA channels costs approximately
**0.200753 ms/frame** against the identical no-requester setup and leaves
**2.943064 ms** of the 5 ms frame ledger. The 90.24% reference ratio is the
cost of the newly required physical work, not a later regression from an H4b
active baseline. No H4b presentation work has been omitted from this gate:
feeding granted words into Denise is explicitly H4c.

Every run allocated zero steady-state managed bytes and reproduced its
within-build fingerprint. Inactive candidate and reference samples were exact
at cycle `298414254` with CPU/hardware/output fingerprints
`864B9D3E37A4CF33` / `63348221B9BACDF4` / `1F069EB181E17741`.
Active candidate samples completed at cycle `298414382` with fingerprints
`22EAA107D99008B3` / `295844CEF3BADEE2` / `1F069EB181E17741`;
the no-requester reference completed at cycle `298414318` with
`3FFC1B5974473273` / `47468CF37156561C` / `1F069EB181E17741`.
The identical output is expected because H4c owns delayed Denise delivery;
the CPU/hardware difference proves active arbitration rather than skipped DMA.

Build identity is runner SHA-256
`2AD8C98BF5B4F3218404F47222FAD75D815AE85E95CF227F085179758C928480`,
candidate engine SHA-256
`5BA69744569C4D3CAE6898F8455F22F415B60183642E6D3173723F5C2546B9D1`,
reference engine SHA-256
`1E3A66141AC21C1760BCCFB7F3151EFC72F764FBAF65B1F630BBDE50CA467684`,
and Copper68k SHA-256
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The candidate is frozen at `.codex-tmp/lightweight-h4b-candidate-20260912`.

H4b dispositions are **Timing GO**, **Inactive host path GO**, **Active host
path baseline GO**, **Cumulative budget GO** and **Allocation GO**. Overall
**H4b result GO**. The user explicitly accepted this stop/go boundary on
2026-09-12, opening H4c to connect DMA output to Denise. H4 itself remains
open, and H4b makes no DMA-presentation, missing-word, BPL1DAT output-enable,
complete-machine or production claim.

#### H4c delayed DMA-presentation gate (2026-09-12)

H4c connects each granted Agnus sprite word to the existing H4a Denise owner;
there is no parallel DMA renderer or copied sprite state. The Agnus output
retains its value, and Denise consumes it one CCK later. A complete POS/CTL
pair enters together after the CTL output and disarms the previous data latch.
DATAA and DATB enter independently: DATAA arms the channel, while a denied
DATB leaves the prior DATB latch intact. The single raw custom-register owner
is updated at the same Denise input phase. A zero control pair therefore
removes stale manual output through the same path as any other DMA control
record.

The OCS presentation gate is line-local. When BPLCON0 requests bitplanes,
sprite shifters continue to consume physical pixels but their color is hidden
until that scanline's first BPL1DAT value reaches Denise. Pixels before the
input phase are not replayed, and the gate closes again on the next scanline.
The zero-bitplane manual/border case accepted by H4a remains available. This
adds only fixed fields and direct calls; there is no event allocation,
timeline, schedule cache or per-row command list.

Eight new focused cases bring the complete Lightweight suite to **113/113**
in both Debug and Release. They prove the complete-control-pair input phase,
DATAA arming one CCK after its granted output, visible DMA data, attached odd
output with and without its even partner, hardware-backed denied-DATB reuse,
terminator disarm of stale manual data, the exact within-line BPL1DAT enable
edge, and closure again without a load on the following line. Legacy output
is not used as an oracle; the undocumented edges use the repository's
hardware-backed OCS conformance register.

The H4c comparison is a short per-gate profile, not a formal production
measurement. Every process reported affinity mask 4 on the established
logical-CPU-2 P-core and Normal priority; the active power plan was Balanced.
Each sample used 300 warmup and 1,800 measured fields. Full host-load-policy-v2
telemetry was not collected. The true H4-off cohort used the accepted H3
bitplane/Copper/line-blitter workload in C/H/H/C/C/H order. The delivery-
dormant cohort retained H4a manual sprites but disabled sprite DMA and ran
C/B/B/C/C/B against frozen H4b. The active cohort used eight sustained
DMA-fed sprites and ran C/B/H/H/B/C/C/B/H against both frozen H4b and frozen
optimized H3.

| H4c cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H4c candidate, H4 entirely disabled | 546.04, 543.22, 548.96 | 546.04 | 1.05% | 1.831368 ms |
| Frozen optimized H3, same H4-off workload | 543.64, 545.01, 538.75 | 543.64 | 1.15% | 1.839453 ms |
| H4c candidate, manual sprites / DMA delivery dormant | 526.75, 513.14, 511.37 | 513.14 | 3.00% | 1.948786 ms |
| Frozen H4b, same manual-sprite workload | 527.97, 515.54, 523.71 | 523.71 | 2.37% | 1.909454 ms |
| H4c candidate, eight DMA-fed sprites | 458.27, 438.93, 452.57 | 452.57 | 4.27% | 2.209603 ms |
| Frozen H4b, DMA active but not delivered to Denise | 477.84, 461.99, 478.20 | 477.84 | 3.39% | 2.092751 ms |
| Frozen optimized H3, identical setup but no H4 devices | 546.30, 537.98, 539.23 | 539.23 | 1.54% | 1.854496 ms |

The H4-off path retains **100.44%** of H3, and the new delivery code while
dormant retains **97.98%** of H4b. Active DMA presentation retains **94.71%**
of H4b and adds approximately **0.116852 ms/frame** for the required Denise
input/latch work. All of H4 adds approximately **0.355107 ms/frame** against
the frozen optimized H3 workload. The complete H4 candidate still reaches
**452.57 FPS**, **2.263x** the final 200 FPS objective, and leaves
**2.790397 ms** of the 5 ms frame budget for H5--H7. These reference ratios
measure newly executed hardware work; they are not achieved by omitting DMA,
shifting, pixels or output checksums.

All samples allocated zero steady-state managed bytes, reported no unsupported
feature and reproduced their within-build fingerprints. The H4-off cohort was
exact at cycle `298414254` with CPU/hardware/output fingerprints
`864B9D3E37A4CF33` / `63348221B9BACDF4` / `CD1A1CFB652A4579`.
The delivery-dormant candidate and H4b reference retained the same cycle,
CPU and hardware values with output `1F069EB181E17741`. Active candidate and
H4b samples were exact at cycle `298414382`, CPU `22EAA107D99008B3` and
hardware `295844CEF3BADEE2`; H4c output changed to `E06176A07C751E7A`
while H4b, which intentionally lacks delivery, remained
`1F069EB181E17741`. The H3 active reference completed at cycle `298414318`
with CPU/hardware/output `3FFC1B5974473273` / `47468CF37156561C` /
`CD1A1CFB652A4579`.

Build identity is runner SHA-256
`D946FF26AE729C29B5C1C2426598DC0669A8770E5E2FD4E0D34A321D3858CAD7`,
H4c engine SHA-256
`DBD3E4D9D9551311F10EC1781538017D1C114575F6B9851EB3AF3EC8413D7A4B`,
frozen H4b engine SHA-256
`5BA69744569C4D3CAE6898F8455F22F415B60183642E6D3173723F5C2546B9D1`,
frozen optimized H3 engine SHA-256
`3A22D5CF139979DC12A31C08C48A964BCE9E40DBD0C6B6A9DD7A25DFDBB2344F`,
and shared Copper68k SHA-256
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The candidate is frozen at
`.codex-tmp/lightweight-h4c-candidate-20260912`.

H4c dispositions are **Timing GO**, **H4-off host path GO**, **Dormant
delivery path GO**, **Active DMA presentation GO**, **Cumulative budget GO**
and **Allocation GO**. Overall **H4c result GO**. The user explicitly accepted
this stop/go boundary on 2026-09-12, closing H4 and making H5 the next available
gate. This result makes no Paula, floppy, native-gameplay, complete-machine or
production claim.

H5 is split into three causally ordered stop/go gates:

1. **H5a -- register and manual-audio spine:** add one Paula owner for
   `ADKCON`, all four `AUDxLC/LEN/PER/VOL/DAT` groups, separate holding and
   output latches, high-byte/low-byte transitions, period-zero expansion and
   the manual completion decision sampled one CCK early. Hardware audio
   requests latch `INTREQ` at their source phase and reach the CPU through the
   existing eight-CPU-cycle hardware visibility path. H5a makes no DMA or host
   PCM claim.
2. **H5b -- fixed audio DMA:** add the four channel slots at horizontal
   `$10/$12/$14/$16`, retained Chip-RAM address/data phases, master/channel DMA
   gating, startup discard/prefetch, pointer and length reload, short-toggle
   continuity, underrun behavior and delayed `INTREQ2`. Paula enters the direct
   bus-priority decision; no generic requester or timeline object is permitted.
3. **H5c -- direct output and modulation:** move the reusable audio buffer to
   Paula, generate deterministic 48 kHz stereo PCM for every completed field,
   route channels 0/3 left and 1/2 right, and implement `ADKCON` volume/period
   attachment including source muting. Host chunking must not alter hardware
   state or PCM.

Each sub-gate requires focused source-phase tests, state that survives line and
field boundaries, an inactive-path comparison, an active synthetic measurement
and zero steady-state managed allocation. H5 remains open until H5c passes and
the final H5 evidence is explicitly accepted. The user authorized H5 work on
2026-09-12. H5a has now reached its automated stop/go boundary; H5b remains
held until H5a is explicitly accepted.

#### H5a register/manual-audio gate (2026-09-12)

H5a adds one compact four-channel Paula owner to the canonical clock. CPU and
Copper writes first update the shared custom-register owner, then enter Paula
at the accepted register-output cycle. The channel state consists only of
fixed pointer/length/period/volume fields, a holding latch, an output latch and
the next manual phase. There is no event object, callback list, requester,
secondary timeline, replay or allocation on the execution path.

Manual `AUDxDAT` output begins with the signed high byte, changes to the signed
low byte after `AUDxPER` color clocks, and makes the completion/restart decision
after sampling `INTREQ` one CCK before the final boundary. A period write before
a byte boundary affects the following interval, not the interval already in
flight. `AUDxPER=0` expands to the full 65,536-clock counter,
`AUDxLEN=0` latches 65,536 words, volume is clamped to 64, and the pending phase
survives a field boundary. A manual start latches the channel `INTREQ` bit at
the source cycle; CPU visibility uses the existing four-DMA-cycle/eight-CPU-
cycle hardware-interrupt path. Enabling an audio DMA channel or an ADKCON
audio-attach bit reports its owning H5b/H5c feature as unsupported instead of
silently running an incomplete path.

The executable expectations are the repository's previously accepted,
requester-scoped production-Paula oracle in `PaulaConformanceMatrixTests`,
`PaulaTests` and `AhxPaulaTimingTests`. They establish the digital register and
manual state-machine boundary used here; they do not establish analog DAC
reconstruction, write-only open-bus readback, audio DMA or whole-machine raw
external parity. Those remain unclaimed.

The complete lightweight Debug and Release suites pass **125/125**. The 12 new
test cases cover all four channels, the shared ADKCON/audio register latches,
length/period/volume rules, high/low ordering, separate holding/output latches,
queued manual data, the one-CCK-early IRQ decision, period changes, period zero,
field-boundary retention, hardware IPL visibility and explicit rejection of
premature audio DMA. The unchanged focused legacy oracle selection passes
**59/59**.

Short per-gate comparisons used one current runner in both directories,
logical CPU 2 / mask 4, the previously verified efficiency-class-1 P-core
mapping, Normal priority, Balanced power plan, 300 warmup and 1,800 measured
fields, and C/R/R/C/C/R sample order. Full host-load-policy-v2 telemetry was
not collected, so these are diagnostic implementation gates rather than a
formal production result. Every process reported affinity mask 4 and Normal
priority; all spreads are below 10%.

| H5a cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| Candidate, event-free stopped machine | 28023.55, 28236.45, 27657.16 | 28023.55 | 2.07% | 0.035684 ms |
| Frozen H4c, identical stopped setup | 28123.68, 27895.39, 27902.05 | 27902.05 | 0.82% | 0.035840 ms |
| Candidate, complete H4 workload with Paula dormant | 466.29, 458.28, 451.34 | 458.28 | 3.26% | 2.182072 ms |
| Frozen H4c, identical H4 workload | 452.06, 459.33, 450.53 | 452.06 | 1.95% | 2.212096 ms |
| Candidate, H4 workload plus four manual channels | 405.34, 402.91, 399.76 | 402.91 | 1.38% | 2.481944 ms |
| Frozen H4c, identical register/service traffic but no Paula owner | 401.95, 403.96, 396.76 | 401.95 | 1.79% | 2.487872 ms |

The event-free and full-H4 dormant retentions are **100.44%** and **101.38%**.
Active manual audio retains **100.24%** of the identical frozen-reference path,
allocates **zero steady-state managed bytes**, remains at **2.01x** the final
200 FPS target, and leaves **2.518056 ms** of the 5 ms frame budget for H5b,
H5c and H6. Dormant fingerprints are identical across builds. The full H4
dormant workload also retains identical cycle, CPU, hardware and output
fingerprints. The manual-audio candidate and reference retain identical cycle,
CPU and placeholder-output fingerprints; their hardware fingerprints differ
as expected because only the candidate executes and latches Paula requests.

Build identity is runner SHA-256
`E915825C6968A2564621D0FD70EA613197B30E422BDE9C9E7FAA949FB7FC79A5`,
H5a engine SHA-256
`3221911C78DDFA99F3DC0202A8986C159DA18A40F11B498A6AC4BBB4DE6F798D`,
frozen H4c engine SHA-256
`DBD3E4D9D9551311F10EC1781538017D1C114575F6B9851EB3AF3EC8413D7A4B`,
and shared Copper68k SHA-256
`CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.
The candidate is frozen at
`.codex-tmp/lightweight-h5a-candidate-20260912`; the same-runner H4c reference
is frozen at `.codex-tmp/lightweight-h5a-runner-on-h4c-20260912`.

H5a dispositions are **Register/manual timing GO**, **H4 dormant retention
GO**, **Active manual-audio cost GO** and **Allocation GO**. Overall automated
**H5a result GO / awaiting explicit acceptance**. H5b, H5c, native gameplay,
complete-machine measurement, production selection and the 200 FPS objective
remain unaccepted.

#### Post-H5a optimization checkpoint (2026-09-13)

H5b remained held while three measured simplifications were attempted against
the complete synthetic H5a path. Each experiment retained physical CPU and DMA
work, full raster output, the reusable audio buffer and zero-allocation output
consumption. The result is deliberately mixed: only the renderer change is
retained.

**O1 -- direct plain-bus prefetch split: rejected.** A scalar CPU profile showed
152-byte `M68kInstructionFetchPublicationContext` copies below the hot NOP and
short-branch paths. A separate plain-`IM68kBus` prefetch implementation removed
those copies and passed the 125 lightweight tests plus the unchanged Copper68k
baseline of 1,470 passes, six optional skips and six pre-existing typed-
descriptor expectation failures. It nevertheless reduced the scalar ROM-loop
median from **424.86 FPS** to **322.75 FPS** (75.97% retention). A no-inline/hot-
cold variant was slower again. Both direct-path variants were removed. The
earlier, accepted simple-bus capture suppression remains in place; this result
specifically rejects duplicating the full prefetch implementation to avoid the
remaining by-value copy.

**O2 -- incremental raster output: retained.** Denise now derives the raster
cursor once on activation and then advances compact line, x and framebuffer-
index fields by one two-pixel CCK. Per-line vertical blank/window and sprite-
enable state is refreshed at line boundaries and after the same delayed
register/data inputs as before. Fully blanked, visible-border and fully-inside-
DIW pairs use short direct paths; the rare odd DIW boundary retains the scalar
per-pixel compositor. This is incremental state, not cached output or batched
hardware time: both playfield pixels are still shifted and written at every
physical CCK.

The O2 comparison used the same current Copper68k and identical runner in both
directories, 300 warmup and 1,200 measured fields, and C/R/R/C/C/R order. It
was pinned to logical CPU 4 / mask 16, verified for this diagnostic as group 0,
core 4, sibling 5 and efficiency class 1. Full host-load-policy-v2 telemetry
was not collected, so this is a short engineering gate rather than a formal
milestone measurement.

| Complete synthetic H5a build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| O2 incremental renderer | 358.08, 385.04, 378.96 | 378.96 | 7.11% | 2.6388 ms |
| Pre-O2 H5a, same Copper68k | 349.87, 346.97, 356.65 | 349.87 | 2.77% | 2.8582 ms |

O2 improves the complete synthetic path by **8.31%** and saves approximately
**0.2194 ms/field**. Every sample produced cycle `213269266`, CPU fingerprint
`B8FC84B1201017A7`, hardware fingerprint `1F8FE24661D0728D`, output fingerprint
`0C8B659FB580D10E`, the full 454x313 raster, 1,920 audio samples and zero
steady-state allocation. Debug and Release both pass **125/125** focused tests,
including same-CCK palette visibility, delayed bitplane reload, DIW clipping,
field wrap and sprite presentation.

The same-Copper68k reference is frozen at
`.codex-tmp/lightweight-h5-opt-reference-currentcpu-20260913`. The retained O2
binary is frozen at `.codex-tmp/lightweight-h5-opt-o2-only-20260913`, with
engine SHA-256
`B749E9E2013098F8A42ECD3679F96FA7653A8D4C1D9D0F748DB550177E03BDAF`,
runner SHA-256
`E915825C6968A2564621D0FD70EA613197B30E422BDE9C9E7FAA949FB7FC79A5`,
and Copper68k SHA-256
`5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.

**O3 -- sparse Copper and sprite-DMA publication: rejected.** Publishing only
real Copper input/output phases improved the synthetic Copper-loop median from
399.53 to 408.92 FPS (**2.35%**), with exact fingerprints. However, two fixed-
slot sprite-DMA implementations both regressed the active eight-channel cohort:
the first retained 95.63% (414.50 versus 433.44 FPS), and the version carrying
horizontal slot state instead of recomputing it retained 96.31% (402.28 versus
417.71 FPS). The Copper-only build also showed an apparent regression in the
required sprite-DMA mix, although that follow-up series was host-contaminated.
Because O3 was optional optimization rather than required hardware work, no
active-path loss was accepted. All O3 source changes and their provisional
tests were removed; the rebuilt engine hash exactly matches the retained O2
checkpoint.

A formal logical-CPU-2 rerun was attempted and correctly returned **INVALID /
RERUN** during preflight before any benchmark sample: concurrent `dotnet test`
and `testhost` workloads were present, with one interval at 62.1% selected-core
other activity and 32.7% package-other activity. This does not replace or
invalidate the clean short O2 engineering comparison, and it does not advance
a formal complete-machine gate. H5b remains WAIT pending explicit H5a
acceptance.

#### Further post-H5a lightweight hot-path checkpoint (2026-09-13)

H5b remained held while the retained O2 machine was profiled and simplified in
small, independently frozen steps. These changes do not batch hardware time,
cache raster output, skip active devices or remove benchmark work. CPU, Copper,
bitplane, sprite, blitter and Paula state still advance through the same CCK
phases and every field still produces the full framebuffer and reusable audio
buffer.

The retained changes are:

- **O4 packed playfield shifters:** the six Denise shifters are held as four
  16-bit lanes in one `ulong` and two lanes in one `uint`. Lane masks preserve
  independent 16-bit shifts and delayed per-parity reloads.
- **O5 paired playfield shift:** one physical CCK extracts and shifts both
  low-resolution pixels together. A bounded slow path retains the original
  per-pixel reload ordering when a BPLCON1 reload is pending.
- **O6/O7 literal refresh phases:** current- and following-CCK refresh checks
  use the canonical beam horizontal directly for bitplanes and then all active
  requesters. A new exhaustive 227-horizontal test proves both direct forms
  equal the canonical slot calculation, including line wrap.
- **O8b/O9 compact bitplane state:** the effective plane count is decoded when
  the delayed BPLCON0 latch becomes visible, inactive publication is direct,
  and DDFSTOP has a direct field in the single register owner. This decoded
  latch is hardware state, not a speculative schedule or output cache. A new
  late-DDF test preserves fetch phase across the odd 227-CCK line boundary.
- **O10 packed-bit gather:** BMI2-capable x64 hosts gather the six output bits
  directly from the packed shifters. The scalar fallback remains present and
  produces identical results when hardware intrinsics are disabled.
- **O11 literal phase conversion:** the non-negative bitplane cycle delta uses
  the exact `>> 1` conversion for the fixed two-CPU-cycle CCK instead of a
  signed division correction sequence.
- **O15 lean Release diagnostics:** pure last-input/last-address evidence stores
  are compiled into Debug and explicit diagnostic builds, but not the normal
  Release engine. Output-cycle fields required by arbitration remain live.
  The test project explicitly requests the diagnostic engine, so Release cycle
  tests retain their observations. This change was throughput-neutral in the
  noisy paired cohort and is retained to enforce the plan's lean-Release rule,
  not counted as a performance gain.
- **O17 adjacent framebuffer writes:** each rendered pair takes one checked
  framebuffer reference and derives the known-adjacent second destination.
  Pixel production and store order are unchanged.

Short same-core A/B pairs showed approximate median gains of 4.37% for O4,
2.00% for O5, 0.35% for O6, 3.1% for O7, 1.60% for O8b, 1.84% for O9 and
1.53% for O10. The eight-sample O11 and O17 comparisons had approximate
medians of 1.05% each. These small-step percentages are engineering signals
and are not additive; later code layout and the deliberately shared CPU affect
each pair.

The cumulative comparison used the current runner for both engines, 300 warmup
and 3,600 measured fields per process, balanced C/R/R/C launch order, logical
CPU 4 / mask 16, and AboveNormal priority. Candidate and reference ran
concurrently on that same verified P-core to share frequency and host pressure.
This makes the ratio useful under the currently noisy host, but it deliberately
does not satisfy the formal Normal-priority, isolated-sample, host-policy-v2
protocol and therefore does not advance a formal milestone gate.

| Complete synthetic H5a engine | Paired FPS samples | Median FPS | Median frame time |
| --- | --- | ---: | ---: |
| Current lightweight survivor | 277.88, 272.10, 279.45, 284.51 | 278.67 | 3.5885 ms |
| Retained O2 reference | 248.57, 252.36, 255.85, 252.38 | 252.37 | 3.9624 ms |

The current median is **10.42% faster than O2**, saving approximately
**0.3739 ms/field** in this paired cohort. Every cumulative sample produced
cycle `554314066`, CPU fingerprint `14DFB2906058C4E7`, live-hardware
fingerprint `2CECAFF62FEE7DC4`, output fingerprint `80486A07CC61DF60`, the
full 454x313 raster, 1,920 audio samples and zero steady-state allocation.

Debug and diagnostic Release both pass **127/127** lightweight tests. With all
hardware intrinsics disabled, the 38 video/bitplane/sprite tests pass and the
1,200-frame workload retains cycle `213269266`, CPU fingerprint
`B8FC84B1201017A7`, live-hardware fingerprint `A59C6005CF925044` and output
fingerprint `0C8B659FB580D10E`. The current lean Release is frozen at
`.codex-tmp/lightweight-h5-opt-final-20260913`, with engine SHA-256
`7B456D7A48A1C9EA2050DD1C5ED1D112BB108CFBA96E072FC217408ADAC9EA0B`, runner
SHA-256 `A574E5E6BA8067E7F1819E81CF569307C0C479A10EEDBAE2D0E542ACE33A5EA4`, and
Copper68k SHA-256
`5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.

Rejected and fully reverted experiments include a bitplane fetch counter and
packed slot lookup; blank-store suppression; alternate render-window branch
layout; 64-bit paired framebuffer stores; unchecked palette lookup; packed
reload latches; packed bitplane-register pointers; unsafe Chip-RAM word access;
direct-next publication rewrites; state-oriented DDF control; forced bitplane
or Copper-completion inlining; forced blitter non-inlining; a `$CA`-specific
blitter minterm path; and a Copper protection short-circuit. Each either
regressed the complete active-DMA workload or remained directionally
indistinguishable after longer pairs. None remains in source.

The surviving profile is now dominated by required work rather than one
obvious removable abstraction: `LightweightBitplanes.Step` is approximately
15.2% exclusive, blitter stepping and completion approximately 7.7% combined,
and the runner's deliberate per-frame synthetic rearming approximately 30%.
The visible frame-boundary clear is retained because the final objective must
include real PCM production; deleting unfinished audio work would not be a
valid optimization. Further speculative micro-tuning is stopped here. The next
recommended action is H5b under its existing stop/go gate, followed by a fresh
profile after DMA audio changes the complete workload. H5a acceptance and all
later stage dispositions remain unchanged.

#### H5a acceptance and H5b opening (2026-09-13)

The user explicitly accepted H5a and authorized proceeding. This supersedes
the historical awaiting-acceptance statements above and opens H5b. H5c still
requires the H5b stop/go result. The retained H5a reference is
`.codex-tmp/lightweight-h5-opt-final-20260913` (engine SHA-256
`7B456D7A48A1C9EA2050DD1C5ED1D112BB108CFBA96E072FC217408ADAC9EA0B`).

The standalone H5a repeat used Normal priority, logical CPU 4 / mask 16,
Balanced power, 300 warmup and 1,200 measured manual-audio fields. The first
series (493.65, 498.40, 576.91 FPS) had 16.7% spread and is INVALID / RERUN.
The replacement was 473.19, 469.95, 488.20 FPS, median 473.19 and 3.86% spread.
Neither collected full policy-v2 telemetry. These are engineering observations;
the simultaneous same-core 278.67/252.37 comparison above is only a ratio
experiment and its absolute frame times cannot be used in the frame budget.

H5b's predeclared engineering checks are the focused physical-phase tests,
zero steady-state allocation, at least 97% retention of the H5a manual-audio
path, and a first sustained four-channel DMA baseline above 200 FPS. Compare
candidate versus reference serially in C/R/R/C/C/R order, using the same runner,
300 warmup and 1,800 measured fields. Also compare the new DMA workload with
an identical candidate configuration whose audio-DMA channel enables remain
off. Record that ratio as the cost of added hardware work, with no artificial
retention threshold against a machine lacking that work. Spreads above 10%
are INVALID / RERUN. These intermediate checks do not replace the complete
native-gameplay measurement, which still requires real PCM and disk execution.

#### H5b fixed-slot audio DMA implementation (2026-09-13)

The existing four compact channel structs now retain an address-input phase,
a Chip-RAM output phase and the following Paula data-load phase. Channel
outputs occupy horizontal `$10/$12/$14/$16`; addresses are accepted one CCK
earlier and read data is held until Paula consumes it one CCK later. Pointer
and length advance at RAM output; later register or memory writes cannot
rewrite an already accepted address or captured data. The common clock runs
Paula before lower-priority requesters publish their next output. CPU chip
accesses, Copper, bitplanes, sprites and the blitter observe the direct audio
ownership predicate, including the output CCK after its pending flag clears.

Startup discards the first DMA word, latches its interrupt at Paula load and
requests the first audible word for the next channel slot. High/low playback
uses the programmed period; one-word prefetch shares the same holding latch
as CPU/Copper AUDxDAT writes. Length exhaustion reloads the programmed pointer
and length; active reload grants arm INTREQ2. The latch is consumed when it
can set INTREQ and survives idle for an AUDxON retrigger. Disabling DMA stops
unaccepted requests while accepted transfers finish. A brief toggle preserves
the running byte period and queued word. An underrun holds the last low byte
until the modeled next data/period transition. No requester adapter, event
object, alternate timeline, phase batching or old-engine dependency was added.

The executable reference is the scoped digital subset of
`PaulaConformanceMatrixTests`, `AhxPaulaTimingTests` and the local
`References/UndocumentedChipsetFeatures.md` INTREQ2 row (EAB archive pages 17
and 19). These support the functional state ordering and channel slots;
the new discriminating tests exercise the explicit lightweight phase model.
They are not a claim that every newly modeled phase has an independent raw
hardware capture. The remaining uncertain races are listed in the defect
register above.

Debug and diagnostic Release pass **146/146** lightweight tests. The 19 added
DMA cases cover all four slots, accepted address/data visibility, register
writes around input, startup discard, one-word-per-line service, length-one
reload and delayed IRQ survival/consumption, pointer/length changes, channel
and master gating, accepted completion after disable, short toggles, underrun,
line/field transitions, CPU chip contention and longword phases, STOP wakeup,
reset, 19-bit pointer wrap, length zero and shared holding-latch overwrite.
Existing H5a manual-audio and prior requester tests remain passing. Copper68k
source was not changed in H5b; its measured assembly matches the H5a reference.

The runner adds `--synthetic-paula-dma` and optional `--audio-dma-off`. Both
execute the H4 raster/Copper/line-blitter/eight-sprite workload and identical
audio setup traffic. The active mode runs four continuous 64-word buffers at
periods 128/228/328/428 CCK; the control keeps audio channel DMA disabled.
Post-timing validation requires all four active channels to be in DMA output
and folds pointer, remaining length, output word, sample, next transition and
delayed IRQ into the hardware fingerprint. Full pixels and the existing
placeholder audio buffer are consumed in the timed loop. Real stereo output
remains explicitly absent until H5c, and native Lemmings remains H6 work.

The final standalone engineering series used verified group 0 logical CPU 4,
core 4, efficiency class 1, mask 16, SMT sibling 5, Normal priority and Balanced
power plan `381b4222-f694-41f0-9685-ff5bb260df2e`. Processes ran serially, with
300 warmup and 1,800 measured fields, in C/R/R/C/C/R order for each comparison.
The existing host-policy-v2 counters recorded selected-core, sibling and
aggregate activity with benchmark CPU subtraction and actual elapsed intervals.
No competing build/test workload or sustained policy-v2 contention was reported.
These are intermediate engineering checks with telemetry; they do not claim
the formal complete-machine protocol or its preflight/cooldown and 600/3,600
native-gameplay frame counts.

| H5b final cohort | Samples (FPS) | Median | Spread | Frame time |
| --- | --- | ---: | ---: | ---: |
| H5b candidate, unchanged H5a manual workload | 492.69, 496.58, 511.52 | 496.58 | 3.79% | 2.013774 ms |
| Frozen H5a, identical manual workload and runner | 501.42, 492.69, 485.67 | 492.69 | 3.20% | 2.029674 ms |
| H5b, four DMA channels active | 514.60, 531.09, 532.83 | 531.09 | 3.43% | 1.882920 ms |
| H5b, identical DMA setup with channel enables off | 596.30, 598.81, 609.37 | 598.81 | 2.18% | 1.669979 ms |

The unchanged H5a path retains **100.79%**, clearing the predeclared 97%
threshold. Sustained four-channel DMA adds **0.212941 ms/field** against its
own dormant control (88.69% FPS retention), leaving **3.117080 ms** of the
5 ms budget for this synthetic machine. This is a first active baseline, not
proof that a complete native-gameplay machine has reached the 200 FPS goal.
Manual-audio and DMA workload medians must not be compared as equivalent work:
the former includes repeated manual-service traffic while the latter streams
DMA and different CPU contention changes subsequent renderer/blitter traffic.

Every final sample allocated **zero** steady-state managed bytes, produced the
full 454x313 raster and 1,920 placeholder audio samples, and reported no active
unsupported feature. All within-cohort fingerprints were deterministic. The
manual candidate/reference match at cycle `298530466`, CPU
`E903B8D0A8BFDC97`, hardware `3A8AB4D91374ABE4`, output `E06176A07C751E7A`.
The active DMA candidate has cycle `298414394`, CPU `6B199162B5B2A3AF`, hardware
`C799138F6E6323F4`, output `2F3460A2E8C29A1C`; its dormant control has cycle
`298414382`, CPU `22EAA107D99008B3`, hardware `B259B96F6B55A46F`, output
`E06176A07C751E7A`. This cross-cohort difference is expected from new DMA work.

The final lean Release is frozen at `.codex-tmp/lightweight-h5b-final-20260913`:

- Engine SHA-256: `0DD417E15BFA0E4398F1DF154C4655E500475367231D809A28A0CF9285FBEA95`.
- Identical candidate/reference runner SHA-256: `F7A1DCA08A4CC189EDC3D3F0D604093BF691D8B977ED4887357AA0361E025DCE`.
- Unchanged Copper68k SHA-256: `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.

The same-runner H5a reference is `.codex-tmp/lightweight-h5b-reference-20260913`.
Raw final logs are `.codex-tmp/h5b-final-retention-identical-runner-20260913.log`
and `.codex-tmp/h5b-final-active-20260913.log`; the diagnostic driver is
`.codex-tmp/run-h5b-engineering.ps1`. Earlier H5b trial measurements preceded
the shared-holding-latch correction, and the first final-retention attempt
had differing runner build hashes. They are preserved as development evidence
only; the identical-runner final series above determines this disposition.

**H5b result: focused phase checks GO, H5a retention GO, active DMA baseline GO,
allocation GO, overall engineering GO / awaiting explicit acceptance.** The
uncertain hardware edges remain disclosed. H5c is next: real 48 kHz stereo PCM
and ADKCON modulation, with its own timing and cost checkpoint. H5b does not
authorize production selection or claim native Lemmings completion.

#### H5b acceptance and H5c opening (2026-09-13)

The user's instruction to proceed accepts the completed H5b boundary and opens
H5c. This supersedes H5b's historical awaiting-acceptance wording above.
H5c adds mandatory output work even when the CPU is stopped and the DAC levels
are silent. Its predeclared intermediate performance check is a standalone
same-runner H5c/H5b comparison with 300 warmup, 1,800 measured fields, three
samples per build in C/R/R/C/C/R order, verified P-core placement and Normal
priority. Measure the stopped machine and sustained four-channel audio-DMA
workloads separately. Require zero allocations, stable within-build fingerprints,
less than 10% spread, active throughput above 200 FPS, and report added frame
time explicitly. Require matching CPU/live-hardware fingerprints for the
unmodulated workload; PCM/output fingerprints and sample counts intentionally
change. A dormant 97% ratio is not applicable to replacing silent placeholder
output with real sample generation. The final 200 FPS native-gameplay protocol
and remaining H6 machine work are unchanged.

#### H5c real stereo output and modulation gate (2026-09-13)

Paula now owns two reusable interleaved int16 PCM buffers. Before a channel
transition, an audio register write, an ADKCON change or field completion, it
integrates the existing signed DAC levels up to the canonical CPU cycle.
Channels 0/3 feed left and 1/2 feed right. Each side uses the full int16 range
for the sum of its two full-volume channels. Integer sample-area accumulation
uses the existing PAL model's 7,093,790 CPU cycles/second and 48,000 sample
frames/second. Fractional duration and unfinished sample area carry across
fields, including 312/313-line alternation. Output accumulation observes
hardware time; it never advances channel state or publishes hardware deadlines.

The public AudioSamples view contains only a completed field's actual sample
count; it is empty after cold reset and valid until the next frame execution.
Long PAL fields ordinarily publish 961 or 962 stereo sample frames instead
of the old fixed 960-frame silence placeholder. The framebuffer owner no
longer owns or clears audio. A CPU RESET first advances devices to its source
cycle and resets Paula hardware while preserving emitted output and the
fractional output clock; a cold machine reset clears both. Configurations
other than 48 kHz stereo now fail explicitly at construction.

ADKCON volume attachment applies the source word to the next channel at the
high-byte transition; period attachment applies it at the low-byte transition.
The current target period deadline remains in force until its next reload.
Combined attachment applies the same word at both transitions. Normal/volume
DMA uses high-byte data requests; period-only DMA uses low-byte requests and
delayed interrupt consumption. Any attached source is muted, including channel
3, which has no following target. Set/clear semantics use the existing single
register owner; effective target period/volume remain hardware latches.

The digital modulation expectations follow the scoped existing
`PaulaConformanceMatrixTests` volume, period, combined and channel-3 attachment
rows; PCM arithmetic is checked independently with exact integer expectations.
Debug and diagnostic Release both pass **167/167** lightweight tests. The 21
new cases cover all stereo routes, negative full scale, cycle-weighted volume
writes, fractional counts over alternating fields, manual and DMA/modulated
partition invariance, reset, attachment transitions/muting, retained target
deadlines, and period-only DMA/INTREQ2 phases. The unmodified H5a/H5b hardware
tests remain passing. Analog and exact coincident-write uncertainties remain
explicit in the defect register rather than being described as verified.

The runner labels `pcm=real` versus the older `pcm=placeholder` and validates
changing nonzero left/right output in the four-channel DMA workload after the
timed interval. Every measured field generates and consumes its actual PCM
and full framebuffer. The same runner assembly is used with the frozen H5b
reference, which retains its old silent output for the cost comparison.

| H5c cohort/build | Samples (FPS) | Median | Spread | Frame time |
| --- | --- | ---: | ---: | ---: |
| H5c four-channel DMA plus real PCM | 515.95, 516.75, 521.01 | 516.75 | 0.98% | 1.935172 ms |
| H5b identical DMA workload, placeholder output | 526.48, 526.30, 525.96 | 526.30 | 0.10% | 1.900057 ms |
| H5c stopped machine, real silent PCM | 22653.73, 23314.28, 22665.00 | 22665.00 | 2.91% | 0.044121 ms |
| H5b stopped machine, placeholder output | 26996.71, 26884.97, 27090.12 | 26996.71 | 0.76% | 0.037042 ms |

Active throughput retains **98.19%** of H5b, adding **0.035115 ms/field** for
PCM and modulation-capable execution. The synthetic active budget is now
**1.935172 ms**, leaving **3.064828 ms** of the 5 ms objective. Silent PCM adds
**0.007079 ms/field** to the stopped-machine case. The higher stopped-path
percentage cost measures required sample production and does not imply an
active-gameplay regression. Native gameplay and floppy work are still absent;
516.75 FPS is not a complete-machine acceptance result.

Samples ran serially in C/R/R/C/C/R order per cohort, with 300 warmup and 1,800
measured fields, group 0 logical CPU 4/core 4, mask 16, efficiency class 1 and
SMT sibling 5, Normal priority, Balanced power plan
`381b4222-f694-41f0-9685-ff5bb260df2e`. Existing policy-v2 counters recorded
selected/sibling/aggregate activity, actual elapsed intervals and benchmark
CPU subtraction. No conflicting workload or sustained contention was reported.
These remain short engineering comparisons rather than the formal milestone
protocol. All samples have zero steady-state allocations, no reported active
unsupported feature and stable within-build fingerprints.

Active candidate/reference match cycle `298414394`, CPU `6B199162B5B2A3AF` and
hardware `C799138F6E6323F4`. Candidate output is `F2204A5C5A39055C` with 1,924
interleaved PCM values in the final field; reference output is
`2F3460A2E8C29A1C` with 1,920 placeholder values. The stopped cohort matches
cycle `298414200`, CPU `94D98FDB9BEE2DD5` and hardware `E758EFBB73D28F66`;
candidate/reference output is `E0DB4A83324AEB25` / `B280D41981F59783`.

Frozen candidate: `.codex-tmp/lightweight-h5c-candidate-20260913`.
Same-runner reference: `.codex-tmp/lightweight-h5c-reference-20260913`.

- H5c engine SHA-256: `A82E54269BCA754A3E44BEC2787D8FB7287ACCF1F2D09CE3095048BCAF5955A5`.
- H5b reference engine: `0DD417E15BFA0E4398F1DF154C4655E500475367231D809A28A0CF9285FBEA95`.
- Shared runner: `9DB39D8F28C031D698C9027C7E4C1ECF58A64502306A8F114D91E5A41206AA1E`.
- Unchanged Copper68k: `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.

Raw evidence: `.codex-tmp/h5c-active-20260913.log`,
`.codex-tmp/h5c-stopped-20260913.log` and the diagnostic driver
`.codex-tmp/run-h5c-engineering.ps1`.

**H5c: digital timing GO, PCM GO, partition invariance GO, allocation GO,
active budget GO. H5c and overall H5 are GO / awaiting acceptance.** H6 is
the next stage for ADF floppy execution, remaining CIA/input work and native
Lemmings boot. No production switch or full-workload 200 FPS acceptance is
implied by this synthetic result.

#### Post-H5c lightweight optimization investigation (2026-09-13)

The H5c frozen engine was sampled with `dotnet-sampled-thread-time` during
600 warmup and 6,000 synthetic Paula-DMA fields. This was a profiling run,
not a throughput sample. Exclusive stack attribution was 40.57% in the clock,
23.03% in device dispatch, 15.85% in bitplane stepping, 2.57% in Paula stepping,
2.28% in Paula next-phase publication and 1.01% in PCM emission. Inlining
means the clock/dispatcher percentages include device/output work; they do
not establish that this much time is removable arbitration overhead.

Most historical diagnostic stores were already absent from lean Release.
Paula's descriptive `State` label was another observation-only field, not the
state used to execute playback. It is now compiled only into diagnostic builds;
lean snapshots explicitly return `NotRecorded`. Accepted-output ownership,
physical phase deadlines and all functional latches remain live.

The PCM simplification completes the weighted partial sample, writes subsequent
whole samples directly from the unchanged DAC level, then retains the fractional
tail. It does not omit output samples, cache audio, change rounding, batch DMA
or alter the hardware clock. Register writes that cannot move an existing
deadline no longer rescan the four channel deadlines. Period and location/length
still take effect at their existing byte-transition and DMA-reload phases.

A fused channel-advance/deadline scan was tried and reverted: its small
initial positive signal did not hold in the longer combined comparisons.
Moving PCM visits from the common phase entry into individual sample mutations
was also tried; it supplied no dependable active-workload improvement, so the
simpler common entry ordering is retained. Neither experiment is counted as a
speedup. No old execution machinery, schedule cache or diagnostic hook was added.

The additional output tests use an independent piecewise DAC integral spanning
volume/mute writes, signed high/low bytes and two fields, plus a playing
modulation target's mid-sample volume change. They supplement the existing
contention, DMA, interrupt, reset and partition checks rather than replacing
hardware expectations with Legacy parity.

Final source passes **169/169 Debug and 169/169 diagnostic Release tests**.
Reflection verifies that the lean channel has no diagnostic `State` backing
field. The CPU and runner source are unchanged. The same frozen runner DLL is
used for both engines; adding the internal `NotRecorded` enum member changed
the rebuilt runner identity, so that newly rebuilt runner was replaced with
the frozen runner before comparison. The identity guard rejected the mismatched
setup before any sample was taken.

Engineering comparisons use serial C/R/R/C/C/R order, three samples per build,
300 warmup fields and 3,600 measured active fields (36,000 for the very short
stopped case). Both engines produce real PCM and the same complete raster.
Placement was reverified as group 0, logical CPU 4 / affinity 16, core 4,
efficiency class 1, sibling CPU 5, Normal priority, Balanced power plan
`381b4222-f694-41f0-9685-ff5bb260df2e`. Selected/sibling/aggregate policy-v2
telemetry was collected with benchmark CPU subtraction; no sustained rejection
threshold was reached. These are engineering comparisons, not the formal
native-Lemmings milestone protocol or a replacement for its 600-frame warmup.

| Final build/cohort | FPS samples | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Simplified, active A | 517.32, 528.51, 529.04 | 528.51 | 2.22% |
| Frozen H5c, active A | 513.13, 516.38, 521.25 | 516.38 | 1.57% |
| Simplified, active confirmation B | 496.42, 490.32, 496.17 | 496.17 | 1.23% |
| Frozen H5c, active confirmation B | 487.79, 500.61, 497.88 | 497.88 | 2.57% |
| Simplified, stopped | 24438.02, 23889.88, 23098.56 | 23889.88 | 5.61% |
| Frozen H5c, stopped | 22905.95, 22770.47, 20752.91 | 22770.47 | 9.46% |

The initial active +2.35% result did **not** reproduce: confirmation was
-0.34%. There is **no dependable active-FPS gain claim**. Both builds slowed
in the later cohort, but that alone does not invalidate an otherwise accepted
host-policy sample. The stopped median improved 4.92%, saving 2.06 microseconds
per field; its near-limit spread makes the exact percentage provisional.
The earlier 9.10% stopped result belonged to the discarded mutation-local
PCM-emission variant, not the final source. The final simplifications are
retained for reduced arithmetic/diagnostic work and the stopped-path saving,
not as a major gameplay-throughput improvement.

Every final active sample has cycle `554197982`, CPU `4EC69AC79128F103`,
hardware `B1A19216345854BB`, output `BF988BEFECE96765`, 142,102 pixels,
1,924 audio samples in the last field and zero steady-state allocation.
Stopped samples likewise match at cycle `5158302600`, CPU `64F23D54C111C445`,
hardware `5BC8B92EF3693D16`, output `BB4EB059374B18C3`, 1,922 last-field audio
samples and zero allocation. The additional 300/1,200 manual-audio smoke also
matches the frozen engine's CPU/hardware/output fingerprints, including output
`D99A4818DF818B27`; its unpinned FPS is not comparison evidence.

Frozen final engine: `.codex-tmp/lightweight-h5c-opt5-20260913`, SHA-256
`1A4296656E44FE80EF47A35C56AA9059F4C2CD64991CAD2AF1025AAA64DA1804`.
Reference: `.codex-tmp/lightweight-h5c-candidate-20260913`, SHA-256
`A82E54269BCA754A3E44BEC2787D8FB7287ACCF1F2D09CE3095048BCAF5955A5`.
Shared runner SHA-256:
`9DB39D8F28C031D698C9027C7E4C1ECF58A64502306A8F114D91E5A41206AA1E`;
unchanged Copper68k SHA-256:
`5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.
Raw final telemetry/results are `.codex-tmp/h5c-opt5-active-20260913.log`,
`.codex-tmp/h5c-opt5-active-confirm-20260913.log` and
`.codex-tmp/h5c-opt5-stopped-20260913.log`; driver:
`.codex-tmp/run-h5c-opt-engineering.ps1`. The sampled trace is
`.codex-tmp/h5c-opt-profile-20260913.nettrace`.

The next optimization evidence should come from the connected native workload,
particularly CPU execution once it is no longer mostly stopped. The existing
active raster/device path, including bitplanes, remains the largest measured
cost; stripping more logging is not an evidenced large opportunity. Do not
restart previously rejected schedule/cache experiments on the strength of this
stack attribution alone.

H5c/overall H5 acceptance, H6 and the complete-native-workload milestone remain
unchanged. Synthetic Paula-DMA FPS is not native Lemmings gameplay FPS.

#### H6 opening and bounded execution slices (2026-09-13)

The user's authorization to proceed to H6 accepts H5c/overall H5 and opens
H6. This supersedes earlier awaiting-acceptance statements without changing
their historical measurements. H6 is connected in the following slices,
with an explicit stop/go checkpoint after each:

1. **H6a -- media and drive pins:** owned read-only standard ADF, DF0
   selection/motor/side/step and CIA status. GO requires focused pin/media
   checks, the complete lightweight suite, zero steady execution allocations,
   and at least 97% retention against frozen post-H5c in active and stopped
   controls. This is not a disk-throughput or native-boot result.
2. **H6b -- disk serial and physical DMA:** standard ADF track encoding,
   live shift register/WORDSYNC/DSKBYTR, index/FLAG, explicit accepted RAM
   phases, FIFO/countdown/cancellation/interrupts including zero-length second
   DSKLEN strobe. GO requires focused serial/bus-phase checks and a measured
   active-disk workload plus unchanged dormant controls. State/byte effects
   must not be imported from Legacy requester classes.
   Split at the serial/RAM boundary: **H6b1** establishes live ideal-ADF input,
   byte/sync status and index; **H6b2** adds WORDSYNC DMA alignment, accepted
   RAM phases, FIFO, countdown and cancellation. H6b1 cannot close H6b or
   authorize native-workload acceptance without H6b2.
3. **H6c -- remaining CIA/input path:** TOD and the required keyboard/mouse/
   joystick protocol. GO requires cycle-causal pin/interrupt checks, deterministic
   input replay and a measured connected-machine run. List remaining omissions.
4. **H6d -- native workload:** native Kickstart 1.3 cold boot, disk loading and
   changing interactive Lemmings gameplay with sound. Any required implemented
   display/CPU corrections are isolated and measured. GO requires the complete
   workload and the original reproducible gameplay/performance protocol; no
   synthetic FPS is substituted. H7 remains held until this checkpoint.

H6a adds one compact drive owner, invoked only at media boundaries and CIA
port reads/writes. It adds no clock-loop poll, events, timeline, cache,
per-cycle diagnostics or steady-state allocation. CIA DDR transitions drive
the same pins as port-latch writes. MOTOR is latched on DF0 selection; head
steps use selected falling STEP edges. Media insertion/ejection sets change;
a step clears it only with media present. Reset turns off the motor and
releases selection without ejecting media or moving the head. Mount rejects
nonstandard geometry without discarding the previously mounted image.

Primary reference: Commodore HRM, [disk control and sensing, Table 8-5](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node018F.html).
The existing `AmigaDiskControllerConformanceMatrixTests` supplies supporting
selected falling-edge/side/pin checks, not blanket Legacy parity. Unlike the
old controller's unselected-DF0 fallback, the new unselected drive releases
its status lines; track-zero describes physical head position even without
media.

Unverified drive edges remain explicit: exact motor spin-up/spin-down and
insertion readiness, motor-off ID signaling, mechanical seek/settle delays,
simultaneous direction/select/STEP changes and real drive limits beyond the
80-cylinder ADF geometry. The selected model uses a fixed 500 ms ready delay
(3,546,895 PAL CPU cycles); that is a deterministic mechanical assumption,
not a measured universal transition. Inward seeks beyond cylinder 79 report
unsupported. Media are write protected; writeback is not silently discarded.
Disk serial/DMA and index/FLAG remain H6b omissions; enabling DSKLEN reports
unsupported rather than allowing a successful incomplete disk benchmark.

**H6a checkpoint: pin/model correctness GO; allocation GO; retained throughput
GO. H6b is next, and H6 as a whole is not yet complete.** The ten new tests
cover ownership/invalid geometry, selection-latched motor, exact modeled ready
observation, step edges/direction/bounds, media-change/track-zero/write-protect,
reset retention, CIA DDR versus pin state, unsupported disk execution and
allocation-free repeated drive control/observation. Complete lightweight tests:
**179/179 Debug and 179/179 diagnostic Release**. No shared Copper68k or Legacy
execution code changed, so historical whole-emulator suites were not required
for this isolated new-engine slice.

The controls compare against the frozen post-H5c optimization build, using its
identical runner and CPU DLLs. Each cohort runs serial C/R/R/C/C/R, three samples
per engine, 300 warmup fields and 3,600 active or 36,000 stopped measured fields.
Verified group 0 CPU 4 / mask 16, efficiency class 1, sibling 5, Normal priority,
Balanced plan `381b4222-f694-41f0-9685-ff5bb260df2e`; policy-v2 telemetry records
elapsed selected/sibling/aggregate activity after subtracting benchmark CPU.
No sustained rejection threshold was reached. This is an engineering retention
checkpoint, not the formal complete-native-gameplay performance protocol.

| H6a control/build | FPS samples | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Active, H6a | 535.86, 538.88, 517.16 | 535.86 | 4.05% |
| Active, post-H5c | 531.99, 510.43, 532.26 | 531.99 | 4.10% |
| Stopped, H6a | 25392.24, 25628.05, 25613.85 | 25613.85 | 0.92% |
| Stopped, post-H5c | 25637.58, 25564.09, 25556.18 | 25564.09 | 0.32% |

Retention is **100.73% active and 100.19% stopped**, above the 97% floor.
These near-equal results establish no detected regression, not a new speedup.
Both builds produce the same fingerprints and zero allocation in every sample:
active cycle `554197982`, CPU `4EC69AC79128F103`, hardware `B1A19216345854BB`,
output `BF988BEFECE96765`; stopped cycle `5158302600`, CPU `64F23D54C111C445`,
hardware `5BC8B92EF3693D16`, output `BB4EB059374B18C3`. Both retain the full
142,102-pixel field and real PCM (1,924 active / 1,922 stopped last-field samples).
Neither control executes disk transfers; H6b must add and measure that work.

Frozen H6a: `.codex-tmp/lightweight-h6a-candidate-20260913`, engine SHA-256
`F3F8A62AC59C1ED25095973BBD6F638970B78DC7407823CE3C64E2003FC3F418`.
Reference: `.codex-tmp/lightweight-h5c-opt5-20260913`, engine SHA-256
`1A4296656E44FE80EF47A35C56AA9059F4C2CD64991CAD2AF1025AAA64DA1804`.
Shared runner/CPU hashes remain those recorded in the post-H5c checkpoint.
Evidence: `.codex-tmp/h6a-active-20260913.log`,
`.codex-tmp/h6a-stopped-20260913.log`,
`.codex-tmp/run-h6a-engineering.ps1`. An output-directory build collision was
resolved with a serial build before freezing; a driver topology-type typo was
corrected before collecting any samples. Neither produced performance evidence.

#### H6b1 serial-input checkpoint (2026-09-13)

**Bounded model/tests GO; zero-allocation GO; idle-disk retention GO; first
active-input baseline established. H6b remains OPEN; H6b2 is next.** This
does not claim full Paula disk cycle correctness, RAM transfers, native boot,
gameplay, or achievement of the complete-engine 200 FPS objective.

Implementation is one compact serial state owner and one pending bit-input
cycle, stepped by the existing machine clock. No event objects, callbacks,
per-cycle tracing/counters, old requester adapters, schedule caches, batching
or independently advancing device clock were added. The allowed CopperDisk
dependency encodes all 160 standard tracks once at mount; immutable encoded
media are about 2.03 MB, separate from the owned 901,120-byte ADF. No encoder
or media-wrapper calls occur during steady reading, seek or side changes.
Raw DSKSYNC/DSKLEN remain in the single register owner; the receiver owns only
its shift/byte/comparison latches and spindle phase.

The model samples MSB-first MFM bits at nominal 300 RPM. Encoded tracks contain
101,344 bits. Arrival `n` is `origin + 2*ceil(n*7093790/(10*101344))` CPU cycles,
implemented with a small integer remainder, not floating point or a division
per bit. The remainder survives revolution/line/field boundaries. Selection
and side/head changes do not restart rotation; deselection freezes the input
receiver but the motor/medium continue turning. Eject and reset remove the
pending input. A new motor run or medium starts at bit zero after readiness.

Live DSKBYTR supplies the received byte, read-clear ready bit, comparison
state and documented DMA/direction bits. DSKSYNC compares the live shift
register and latches INTREQ bit 12 on a new match independently of WORDSYNC
or DMA enable. Index enters CIA-B FLAG, including pending-while-masked and
later mask-enable visibility. These functional expectations come from the
[Commodore HRM disk-controller chapter](https://bastya.net/AmigaDevDocs/hard_8.html)
(DSKBYTR, DSKSYNC, disk interrupts). The existing media encoder's output is
checked independently by decoding odd/even headers and every sector payload
on all 160 tracks, not by comparing against a second invocation of itself.

**Unverified boundaries, not timing proof:** the ADF supplies sectors rather
than captured flux. Nominal speed, ready-origin/bit-zero phase, coast-down,
insertion/seek-settle effects, receiver phase across head changes, byte phase
under WORDSYNC, reset's 0x4489 sync seed and precise comparator/write-edge
propagation are explicit model choices or unresolved edges. Disk-sync CPU
visibility provisionally uses the existing Paula 4-CCK delay; index uses the
existing CIA visibility path. These need discriminating hardware evidence,
not Legacy equality. Slow/GCR/MSBSYNC recovery reports unsupported when input
is active. DSKLEN enable still reports **H6b2 disk DMA execution**; no benchmark
can silently omit an attempted RAM transfer. Writable media remain deferred.

Validation: **198/198 Debug and 198/198 diagnostic Release** focused-project
tests. The 19 added cases cover all-track decoding, five revolutions of exact
model deadlines, byte arrival/read-clear including byte-bus access, all three
DMA status enables, sync matches without DMA/WORDSYNC, comparator changes,
deselection/side changes, masked index/ICR behavior, field crossing,
eject/reset, unsupported recovery and allocation-free serial/index execution.
No Copper68k or CopperDisk source changed; the historical Legacy corpus was
not required for this isolated new-engine slice.

Engineering measurements use a verified P-core, group 0 CPU 4 / mask 16,
efficiency class 1, SMT sibling 5, Normal priority and Balanced plan
`381b4222-f694-41f0-9685-ff5bb260df2e`. Each series uses C/R/R/C/C/R,
300 warmup fields and three samples per cohort, 3,600 measured fields for
active hardware or 36,000 for the stopped control. Policy-v2 telemetry was
valid; no sustained rejection threshold or competing workload was detected.
A single approximately one-second 25% sibling reading in the stopped series
does not meet the continuous ten-second rejection rule. No series spread
exceeded 10%. These are engineering comparisons, not the 600/3,600 native
gameplay acceptance protocol.

| Workload/build | FPS samples | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Active hardware, idle disk, H6b1 | 513.89, 527.10, 530.28 | 527.10 | 3.11% |
| Same control, H6a | 529.72, 531.00, 514.92 | 529.72 | 3.04% |
| Stopped, H6b1 | 25371.26, 25686.04, 25843.66 | 25686.04 | 1.84% |
| Stopped, H6a | 25183.62, 25190.34, 25511.24 | 25190.34 | 1.30% |
| Active hardware + serial input, H6b1 | 448.05, 459.54, 458.28 | 458.28 | 2.51% |
| Same H6b1, serial input off | 523.23, 529.26, 520.68 | 523.23 | 1.64% |

Idle-disk retention is **99.51% active / 101.97% stopped**, above 97%; no
speedup claim. Serial input adds approximately **0.271 ms/frame** in its
interleaved same-build control: **2.182 ms** with input versus **1.911 ms**
without. This freezes the first active-input baseline, not a regression
against an earlier implementation of the same work. The synthetic workload
has CPU predominantly stopped, active Copper/bitplanes/sprites/blitter/audio,
full 142,102-pixel fields and real 48 kHz stereo PCM. About **2.818 ms** remains
to the 5 ms budget on this fixture, but native CPU work, RAM disk DMA and
remaining H6 behavior are absent; costs cannot be assumed additive. No
per-component CPU/chipset profile or native frame-budget closure is claimed.

Every measured sample allocated **zero bytes**. Idle controls retained the H6a
fingerprints above. Active input retained cycle `554197982`, CPU
`4EC69AC79128F103`, output `BF988BEFECE96765` and 1,924 last-field PCM samples;
its hardware fingerprint is `EC881A45051BB113`. Added input changes disk
state/interrupts, not this fixture's pixels/audio. The runner verifies input
position progress, a future bit deadline and sync/index signals outside timing.

Frozen candidate: `.codex-tmp/lightweight-h6b1-candidate-20260913`.
The reference directory `.codex-tmp/lightweight-h6b1-reference-20260913` retains
the original H6a engine/CPU, with the identical new runner/media dependency
for comparison; the original H6a freeze was not overwritten.

- Engine SHA-256: `C99A6835CD9981BE50356EFBF8EA5DDB500801A43807352FDBA6FB7FD8A3246D`.
- Shared runner: `1F25C12791E8E68EB4C7C025DBE1A5C79ED229CA082FE45A73001A2F7F1AA68F`.
- Copper68k unchanged: `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.
- CopperDisk: `8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227`.

Evidence: `.codex-tmp/run-h6b1-engineering.ps1`,
`.codex-tmp/h6b1-active-20260913.log`,
`.codex-tmp/h6b1-stopped-20260913.log`,
`.codex-tmp/h6b1-disk-input-20260913.log`.
Reproduce active input with the frozen runner and
`--synthetic-paula-dma --disk-serial --warmup 300 --frames 3600`.
The default synthetic medium is an all-zero 880 KiB ADF, encoded normally.
The supplied disk-1 Lemmings ZIP also completed a 300-warmup/120-field serial
smoke run with sync/index progress, zero allocation and no active unsupported
feature. This used the synthetic hardware ROM, **not native Kickstart or
gameplay**; its unpinned FPS is not acceptance evidence.

**Next stop/go boundary: H6b2.** Add the three fixed disk slots, accepted
address/data retention through completion, CPU/lower-DMA exclusion, FIFO,
WORDSYNC word alignment, two-strobe/zero-second-length DSKLEN semantics,
countdown/completion and cancellation. Preserve accepted effects across
disable and line/field boundaries. Repeat focused phase checks, zero-allocation
checks, dormant controls and a genuinely RAM-active disk workload. H6c and
native Lemmings acceptance remain held until that checkpoint is recorded.

#### H6b1 access-cost experiments (2026-09-13)

The user requested optimization checks before H6b2, then authorized testing
the two small candidates independently. No disk phases, clock arithmetic,
WORDSYNC/DSKBYTR semantics, interrupt delays or media encoding are changed.
The H6b1 hardware-uncertainty register remains in force.

**Candidate 1: direct DSKSYNC field in the existing register owner — rejected
as an unproven speedup and reverted.** CPU/Copper/reset still used one owner;
the candidate removed the per-bit general-register array checks. However,
the interleaved active-input medians were only 448.66 versus 445.77 FPS
(+0.65%), smaller than the reference spread. This does not establish a useful
improvement. The added CPU/Copper/reset ownership test is retained, using
the original register access after the revert.

| Candidate 1 cohort | FPS samples | Median | Spread |
| --- | --- | ---: | ---: |
| Direct-field candidate | 449.11, 447.67, 448.66 | 448.66 | 0.32% |
| Frozen H6b1 | 445.77, 443.65, 460.12 | 445.77 | 3.69% |

Candidate freeze: `.codex-tmp/lightweight-h6b1-opt1-20260913`, engine SHA-256
`8E9FEC903AF4D290F0DFB96B4B21F3DB8BA48FF579D53F99495741BF60D367D7`.
Evidence: `.codex-tmp/h6b1-opt1-disk-20260913.log`.

**Candidate 2: direct track-bit read — rejected and reverted.** Tested alone,
after reverting candidate 1. It replaced the optional track span with a direct
managed array read; no pointer cache, unsafe indexing or skipped bit was
introduced. Focused tests additionally compared every encoded bit across all
160 tracks against the original span expression before measuring. The result
was 435.25 versus 443.70 FPS (candidate median **1.90% slower**, not a gain).
Candidate spread was 3.78%, so the exact regression size is not claimed as
precise. There is no evidence here to retain it as an optimization.

| Candidate 2 cohort | FPS samples | Median | Spread |
| --- | --- | ---: | ---: |
| Direct-bit candidate | 447.31, 430.86, 435.25 | 435.25 | 3.78% |
| Frozen H6b1 | 442.42, 445.81, 443.70 | 443.70 | 0.76% |

Generated-code inspection confirmed that `ReadBit` was already inlined in
optimized `Step`: its code shrank from 497 to 453 bytes, but throughput did
not improve. This is evidence against assuming that a shorter source/native
method necessarily makes the complete workload faster. The disassembly run
was separate from performance measurement.

Candidate freeze: `.codex-tmp/lightweight-h6b1-opt2-20260913`, engine SHA-256
`EFCAD58ABE23981A5A554D770C56694F03C2C7FFF8A4E007C942373F08500E31`.
Evidence: `.codex-tmp/h6b1-opt2-disk-20260913.log`,
`.codex-tmp/h6b1-opt2-jit.txt`; driver
`.codex-tmp/run-h6b1-optimization.ps1`.

Both experiments use the identical frozen H6b1 runner and Copper68k DLLs,
verified P-core group 0 CPU 4 / mask 16 / efficiency class 1 / sibling 5,
Normal priority, Balanced power plan, 300 warmup and 3,600 measured fields,
three samples per build in C/R/R/C/C/R order. The optimization driver requires
cross-build as well as within-build fingerprint equality even with serial
input active. Policy-v2 load telemetry and the 10% spread rejection rule are
unchanged. These remain engineering synthetic-workload comparisons, not
native gameplay acceptance or new hardware evidence.

Both measured series had valid telemetry, no policy-v2 rejection and spreads
below 10%. All samples matched the original H6b1 active-input fingerprints:
cycle `554197982`, CPU `4EC69AC79128F103`, hardware `EC881A45051BB113`, output
`BF988BEFECE96765`, 142,102 pixels, real stereo PCM (1,924 last-field samples),
zero allocations and no active unsupported feature. These fingerprints and
the existing focused timing checks support unchanged implemented behavior;
they do not resolve the previously documented uncertain hardware edges.

**Disposition: retain the original H6b1 engine, not either experimental
optimization.** The CPU/Copper/reset ownership test and this evidence are
retained. H6b2 has not begun. The preceding H6b1 gate and performance baseline
are unchanged; no new speedup is claimed. No shared Copper68k, CopperDisk or
Legacy execution source was modified.

Final restored-build verification: **199/199 Debug and 199/199 diagnostic
Release** lightweight tests pass. The non-diagnostic Release engine rebuilt
in `.codex-tmp/lightweight-h6b1-opt-restored-20260913` has SHA-256
`C99A6835CD9981BE50356EFBF8EA5DDB500801A43807352FDBA6FB7FD8A3246D`,
**byte-for-byte identical to frozen H6b1**. Additional dormant-path throughput
comparisons are unnecessary for the final result because no runtime change
survived these experiments.

#### H6b2 read-DMA implementation checkpoint (2026-09-13)

The user authorized H6b2. This connects the existing ideal-ADF receiver to
Chip RAM, without importing Legacy execution classes or changing Copper68k
or CopperDisk. **Implementation is experimental; physical disk-request timing
is not yet GO. H6c and native-gameplay acceptance remain held.** Historical
H6b1 evidence and its earlier "H6b2 next" statements above retain their dates
and meanings; they are not the current disposition.

`LightweightDiskDma` owns a packed three-word FIFO, one word-alignment counter,
the two-strobe/WORDSYNC state and one accepted address/data pair. The canonical
clock calls it directly after serial input and before lower-priority DMA.
No event objects, tracing callbacks, schedule cache, per-cycle counters or
steady-state allocation were added. CPU and Copper use the same register
implementation. The inactive receiver now checks DMA activation inline before
entering the FIFO code.

Implemented and discriminated by focused tests:

- Two enabled DSKLEN strobes start the second length, including a zero-length
  second strobe that requests DSKBLK without touching RAM. A clear DMAEN
  cancels rather than completing the programmed transfer.
- WORDSYNC discards the first matching sync word and starts with its following
  word. Subsequent matches re-establish word alignment; an aligned subsequent
  sync word can itself be stored.
- The bounded model chooses the next eligible fixed address input at internal
  H7/H9/HB, with RAM output one CCK later at H8/HA/HC. Data becoming ready at an
  input cannot retroactively occupy that input. Pointer increment occurs at
  acceptance; memory visibility and remaining-count decrement at output.
- Accepted address/data survive a pointer rewrite, DSKLEN cancellation or
  DMACON disable. Cancellation drops unaccepted words. A preceding accepted
  write cannot decrement or interrupt a newly started transfer.
- Disk slots exclude the CPU, Copper, blitter, sprites and bitplanes. The
  clock retains queued work across line/field boundaries; pointers wrap in
  the supported 512 KiB Chip RAM. External hardware reset clears pending work.
- Completion requests level-one DSKBLK and wakes an already STOPped 68000.
  CPU and Copper share the strobe latch. Both DMA master and disk enable
  control transfer eligibility.

Validation: **219/219 Debug and 219/219 diagnostic Release** lightweight tests
pass (20 more cases than H6b1). Actual encoded ADF tests compare every DMA word
for both a 32-word block and a complete 6,334-word revolution crossing index,
including nonzero sector data, with zero execution allocation. The existing
all-cylinder/head independent ADF decoding tests remain in the suite. No
shared CPU/Legacy implementation changed, so the historical emulator corpus
was not rerun. Builds retain the pre-existing NU1902 package warning.

Hardware evidence and unresolved edges:

| ID | Evidence / implemented model | Disposition |
| --- | --- | --- |
| H6b2-T1 | Commodore documents the double enable, length, WORDSYNC following-word/every-match behavior and DSKBLK. Repository zero-second-strobe conformance evidence is retained. | Documented semantics covered; not a full physical timing oracle. |
| H6b2-T2 | Fixed OUT8/A/C coordinates are established by the existing Agnus slot evidence. This implementation drains into the earliest eligible slot. WinUAE's `disk_dmal` maps one/two/three queued words to different request bits; this is not proof of the same request-sampling phase or slot order. | **OPEN:** discriminate physical DMAL sampling, FIFO ordering and request-to-slot delay before claiming exact disk arbitration. |
| H6b2-T3 | Remaining length and DSKBLK latch at the final RAM output here, with provisional immediate CPU visibility. Commodore also warns of a last-read-word hardware bug; other implementations differ on serial-versus-RAM countdown. | **OPEN:** hardware-backed final-word/count/interrupt and cancellation-edge expectations required. |
| H6b2-T4 | Non-WORDSYNC reception starts a fresh 16-bit counter at the second strobe; paused DMA retains alignment but does not enqueue incoming words. H6b1 DSKBYTR derives enable/direction from raw register bits. | **OPEN:** initial/free-running word alignment, pause/selection edges and post-completion DMAON status not verified. |

Primary documentation: [Commodore hardware manual, disk interface](https://bastya.net/AmigaDevDocs/hard_8.html).
Implementation comparison, **not a hardware oracle**:
[WinUAE disk execution](https://github.com/tonioni/WinUAE/blob/master/disk.cpp),
[Paula replacement FPGA](https://github.com/nonarkitten/amiga_replacement_project/blob/master/paula/Paula.v).
The Amiga cycle-exact skill required separating these sources from hardware
proof; passing these model tests does not close T2–T4. H6b1's digital-input
uncertainties remain in force. Write DMA, active DSKLEN reprogramming without
cancellation, active WORDSYNC enable changes and FIFO overflow explicitly
report unsupported behavior. There is no fallback to Legacy.

The runner adds `--disk-dma` to the active hardware fixture. It uses the same
encoded ADF serial path with WORDSYNC disabled, repeatedly reads 16,383-word blocks into
`$50000..$57FFD`, and rearms completed blocks at the next frame boundary inside
timing. Every measured frame must complete RAM words; a small RAM checksum
is consumed along with pixels/audio. A final destination-region fingerprint
is computed outside timing. The first active-DMA result is a **synthetic
hardware result**, with predominantly STOPped CPU, not Lemmings gameplay.

The complete-machine 200 FPS objective remains unfinished regardless of this
synthetic result. Native Kickstart boot and gameplay have not been accepted.

Implementation/performance audit trail (not silently discarded):

- Initial receiver integration retained 99.66% with the drive idle
  (517.48 versus 519.27 FPS) but only 95.83% with serial input active and RAM
  DMA disabled (439.86 versus 459.01). This triggered the >3% investigation.
- A 12-second sampled profile of that initial serial-only candidate placed
  roughly 36.5% of sampled execution in `Clock.AdvanceTo`, 27.7% in
  `TickDevices` and 16.1% in `Bitplanes.Step`. Inlined disk-input work is not
  separately attributed, so this is not proof of a particular DMA method's
  cost. Evidence: `.codex-tmp/h6b2-initial-profile-on.nettrace` and its
  `.speedscope.json` conversion.
- The inline inactive-DMA guard candidate retained 97.56% with serial input
  (444.31 versus 455.43 FPS), but its idle comparison retained only 96.51%
  (507.63 versus 525.97). This did not close the dormant-path gate. A first
  attempt was rejected before sampling because the runner hashes differed;
  a new reference clone with the identical runner supplied the actual series.
- The retained implementation also removes the redundant comparison with a
  *past* output in future-slot arbitration: lower requesters inspect the
  pending output; a CPU arriving after the clock has executed the current CCK
  inspects the completed output. Existing exact-phase tests cover both sides.
  Unused FIFO/address/data observation properties were removed.
- The initial long active fixture aborted, so it has **no accepted FPS**.
  Its detailed rerun identified a WORDSYNC wait at frame 984, with unchanged
  remaining length 16,383 and no unsupported feature. The gap plus distance
  from the last sector's sync can exceed a field. This was not evidence of
  lost DMA data. The final continuous-read fixture disables WORDSYNC; focused
  tests still cover WORDSYNC reads and compare every transferred track word.
  Its progress check counts completed words, rather than pointer acceptance.

Frozen final non-diagnostic Release build:
`.codex-tmp/lightweight-h6b2-final-20260913`. The original H6b1 freeze is intact;
`.codex-tmp/lightweight-h6b2-final-reference-20260913` contains that engine
with the identical final runner for comparisons.

- Engine: `403D55E3800F2FD23D7FFDCF3E4F898A1D6F120771F72A471FF63C6578481F04`.
- Runner: `C9CD19EB307AB8092BC4DEC16A7421599AC991E3EB4A4271077972869DA9CF4C`.
- Copper68k unchanged: `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.
- CopperDisk unchanged: `8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227`.

All final engineering comparisons use 300 warmup / 3,600 measured fields,
three samples per side in C/R/R/C/C/R order, Normal priority and a freshly
verified P-core: group 0, logical CPU 4, affinity 16, efficiency class 1,
SMT sibling 5. Balanced power GUID:
`381b4222-f694-41f0-9685-ff5bb260df2e`. The driver records full topology, build
identities and core/sibling/aggregate policy-v2 telemetry externally. These
are H6 slice measurements, not the 600/3,600 native-gameplay acceptance protocol.

Final active RAM-DMA samples: **431.60, 428.63, 425.85 FPS**, median
**428.63 FPS / 2.3330 ms per frame**, spread **1.34%**. The same engine with
serial input but RAM DMA disabled produced **453.09, 441.98, 439.51 FPS**,
median **441.98 FPS / 2.2625 ms**, spread **3.07%**. The incremental measured
RAM-DMA cost is approximately **0.0705 ms/frame**. This is a first active
baseline, not a same-work optimization or a complete-machine speed claim.
The synthetic fixture leaves **2.6670 ms** of the 5 ms target budget unspent;
native gameplay CPU/loader work is still missing from that ledger.

All three RAM-active samples agree on cycle `554197982`, CPU
`4EC69AC79128F103`, hardware `A209A1FB2AAD014E`, output `CC609FA86CAFD46B`,
142,102 pixels, real stereo PCM (1,924 samples in the last field), zero
allocation and no active unsupported feature. Serial-only controls retain
the H6b1 hardware/output fingerprints `EC881A45051BB113` /
`BF988BEFECE96765`. Disk contention changes raster output in the active
fixture; cross-workload output equality is not an acceptance condition.

The final drive-idle active-hardware comparison produced **508.83, 518.57,
515.87 FPS** versus H6b1 **514.09, 522.64, 494.04 FPS**: medians **515.87**
and **514.09**, retention **100.35%**, spreads **1.89% / 5.56%**. It passes
the 97% threshold without claiming a speedup beyond measurement variation.

Final serial-input/no-RAM-DMA comparison: **433.51, 441.21, 454.82 FPS**
versus frozen H6b1 **460.09, 449.83, 461.73 FPS**; medians **441.21 / 460.09**,
retention **95.90%**, spreads **4.83% / 2.59%**. This is approximately
**0.0930 ms/frame** of remaining added cost. It does **not** meet the 97%
unchanged-work threshold. The earlier guard candidate's passing serial-only
sample cannot waive this final build's result; no host contamination was
reported by policy v2. Both source simplifications are retained as an
experimental implementation, not as a demonstrated optimization win.

Final STOP control: **25109.01, 25383.74, 25373.75 FPS** versus H6b1
**25645.79, 25534.61, 25422.81 FPS**; medians **25373.75 / 25534.61**,
retention **99.37%**, spreads **1.08% / 0.87%**. Fingerprints agree:
cycle `554197800`, CPU `A3A40F49B8CFEC65`, hardware `5FA0BF87BC7833B6`,
output `0123DB1732B6D603`, zero allocation. All four final series had valid
placement/telemetry, no policy-v2 rejection and spread below 10%.

The supplied Lemmings disk-1 ZIP also passed a 300-warmup/120-field read-DMA
smoke run with RAM progress, real pixels/stereo audio, zero allocation and
no active unsupported feature. Fingerprints: cycle `59683034`, CPU
`ADC971FCB2A6DBCF`, hardware `B03AA632A52ED4F8`, output `DA9473FB73EC75EB`.
This used the synthetic ROM, **not native Kickstart/gameplay**; its unpinned
FPS is not gate evidence.

Initial H6b2 implementation dispositions (superseded only by later evidence below):

| Gate | Result |
| --- | --- |
| Implemented bounded-model checks | **PASS**, 219/219 in both test builds; no claim of complete physical timing verification. |
| Hardware timing | **OPEN**, especially T2 DMAL/request-to-slot mapping and T3 last-word/completion interrupt phases; T4 remains explicitly uncertain. |
| Dormant host path | Drive-idle and STOP controls **PASS**; serial-input/no-RAM-DMA retention **OPEN / below threshold (95.90%)**. |
| Active RAM-DMA host path | **Measured baseline established: 428.63 FPS**, deterministic and zero-allocation. This does not waive either open gate. |
| Cumulative budget | Synthetic hardware **2.3330 ms/frame**; complete native-gameplay ledger still **OPEN**. |
| Overall / next stage | **H6b2 OPEN, not GO. H6c remains held; production unchanged.** |

Next work is bounded to H6b2: isolate the remaining inactive-receiver/clock
cost against the frozen H6b1 binary (inspect generated code before another
source-shape experiment), and obtain discriminating request/FIFO/completion
phase evidence for T2/T3. Do not silently accept the 4.1% unchanged-work cost,
describe model tests as a hardware oracle, or start native-workload acceptance
under this disposition.

Final evidence files, all under `.codex-tmp/`:
`lightweight-h6b2-final-ram-dma-20260913.txt`,
`lightweight-h6b2-final-active-20260913.txt`,
`lightweight-h6b2-final-disk-input-20260913.txt`, and
`lightweight-h6b2-final-retention-20260913.txt`.
The retained driver is `run-h6b1-optimization.ps1` with explicit H6b2 build
names; `-RamDmaCost` enables DMA only on side C and requires deterministic
fingerprints within each side instead of cross-workload equality. Reproduce
active DMA directly with the final runner and
`--synthetic-paula-dma --disk-dma --warmup 300 --frames 3600`.

#### H6b2 storage and hot-path optimization (2026-09-13)

The user requested further optimization, allowing reduced testability and
observability but prioritizing correctness. This checkpoint changes storage
and unnecessary host work, **not the modeled disk hardware behavior**.
The Amiga cycle-exact skill guided retention of the phase checks and the
separation of measured equivalence from the unresolved T2–T4 hardware questions.

Generated-code audit before editing:

- Frozen H6b1 serial `Step` is 497 native bytes in the captured Tier1 build;
  initial H6b2 is 551. H6b2 prepares shift/equality arguments before testing
  DMA activation, and reads DMA state through another managed object.
- The canonical tick also follows the serial/DMA object references to their
  next phases. The measured issue is additional host execution and memory
  access, not a reason to omit incoming bits, slot ownership or accepted RAM
  effects.
- Native size alone is not a performance oracle: captured `TickDevices`
  is 3,104 bytes in H6b1 versus 2,716 in initial H6b2, despite H6b2's failed
  serial-only retention. Inlining/PGO and the executed path matter.

Retained implementation:

1. Embed the small, reference-free `LightweightDiskSerial` and
   `LightweightDiskDma` value states directly in the machine. They remain
   **mutable owner fields**, not readonly copies or externally shared state.
   This removes two tiny heap objects and the extra object lookup in clock
   checks. Mutation stays by reference; no callbacks, interfaces or copied
   requester state were added.
2. Check DMA activation in the serial receiver **before** preparing the call's
   data/equality arguments. The generated inactive path no longer loads those
   arguments. The machine's internal receive entry is an active-DMA entry;
   the existing diagnostic assertion remains outside the lean Release path.
3. Read the live WORDSYNC enable only when a sync match actually occurs.
   The previous generated DMA receiver loaded the register and constructed
   its boolean on every incoming bit, even while waiting without a match.
   No register write or emulated event can intervene inside this single-clock
   bit operation, so moving this pure read preserves modeled ordering. There
   is no cached register value or look-ahead.

No tests, phase checks or active-device work were removed. Release already
excludes diagnostic tracing; deleting test code would not make its execution
faster. The shared CPU, media encoder and Legacy source were not edited.

Experiments, all pinned and interleaved under the existing policy:

| Candidate / comparison | Candidate median FPS | Reference median FPS | Interpretation |
| --- | ---: | ---: | --- |
| DMA value state alone, serial-only vs H6b1 | 445.08 | 463.49 | Still below 97%; insufficient alone. |
| DMA value state alone, active DMA vs initial H6b2 | 431.64 | 434.30 | No demonstrated speedup; not retained as a standalone result. |
| Both value states + early guard, serial-only vs H6b1 | 458.39 | 452.43 | Retention 101.32%; former failing slice passes. |
| Both value states + early guard, active DMA vs initial H6b2 | 433.57 | 425.16 | 101.98% retention; small apparent gain is within reference variation. |
| Lazy WORDSYNC read, active DMA vs preceding candidate | 436.51 | 435.09 | 0.33% difference is below sample spreads; **no separate FPS gain claim**. Retained as simpler common-path execution with unchanged results. |

The generated lazy receiver executes its ordinary non-sync/non-word-boundary
path without reading ADKCON or the machine argument. Total native method size
actually grows from 286 to 336 bytes; the common path shortens. The completed
serial receiver is 553 bytes, and its tick is 3,242 in the captured build.
These measurements explain why this checkpoint does not optimize for source
line count or total native bytes alone.

Final candidate: `.codex-tmp/lightweight-h6b2-lazy-sync-20260913`.

- Engine SHA-256: `9FE7B6E1521E2BD831A055844E83AC0201B4E75E6522803FC7B712F1627AEBC5`.
- Runner SHA-256: `D5C53C536BD9947F87DBFAD968665DF4032F4EA24CE184B29D46A205C673A29D`.
- Copper68k and CopperDisk retain the unchanged hashes recorded above.
- Reference clones `lightweight-h6b2-disk-h6b1-ref-20260913` and
  `lightweight-h6b2-disk-h6b2-ref-20260913` contain the respective frozen
  engines with the identical runner; original freezes remain intact.

**219/219 Debug and 219/219 diagnostic Release tests pass.** This includes
all implemented disk phase, WORDSYNC, cancellation/rearm, complete-track
data, CPU contention, reset and STOP-wakeup checks. No new hardware oracle
is claimed by these unchanged results.

Final H6b1 retention controls (300 warmup / 3,600 measured frames,
C/R/R/C/C/R order, three samples per side):

| Workload | Candidate samples FPS | H6b1 samples FPS | Medians C / R | Spread C / R | Retention |
| --- | --- | --- | --- | --- | --- |
| Serial input, RAM DMA off | 436.94, 463.31, 457.72 | 463.05, 460.24, 465.27 | 457.72 / 463.05 | 5.76% / 1.09% | **98.85% — PASS** |
| Active hardware, drive idle | 517.49, 523.97, 510.38 | 524.48, 517.02, 521.61 | 517.49 / 521.61 | 2.63% / 1.43% | **99.21% — PASS** |
| STOP control | 24695.86, 25926.77, 25506.44 | 24705.42, 25695.71, 25782.44 | 25506.44 / 25695.71 | 4.83% / 4.19% | **99.26% — PASS** |

The serial-only result replaces the initial 95.90% retention failure for this
candidate; it does not assert that the new engine is faster than H6b1.
Clock/CPU/hardware/output fingerprints agree with each respective frozen
control, and every sample allocates zero steady-state bytes.

Final **active DMA versus the frozen initial H6b2**, using the same runner and
the same DMA-active workload on both sides:

- Candidate: **441.14, 444.09, 433.94 FPS**, median **441.14 FPS / 2.2669 ms**,
  spread **2.30%**.
- Initial H6b2: **417.05, 428.62, 433.60 FPS**, median **428.62 FPS / 2.3331 ms**,
  spread **3.86%**.
- Retention **102.92% — PASS** against the 95% active-path requirement.
  The approximately **0.0662 ms/frame** apparent saving is modest and within
  the reference series' spread; do not claim a precisely established 2.92%
  speedup. Removing the former retention failure is the main gate result.
- Both sides retain cycle `554197982`, CPU `4EC69AC79128F103`, hardware
  `A209A1FB2AAD014E`, output `CC609FA86CAFD46B`, full pixels/real stereo PCM,
  zero allocations and no active unsupported feature. No DMA, CPU, rendering
  or audio work was omitted to obtain the result.

All final comparisons reverified group 0 / logical CPU 4 / affinity 16,
efficiency class 1 and sibling 5, Normal priority, Balanced power GUID
`381b4222-f694-41f0-9685-ff5bb260df2e`, with policy-v2 telemetry and no rejection.
Sample spreads are below 10%. These remain engineering synthetic measurements,
not the complete native-gameplay acceptance protocol. The synthetic ledger
now uses **2.2669 ms/frame**, leaving **2.7331 ms** of the 5 ms budget for
still-unmeasured complete-machine work; this is not a performance forecast.

The final candidate also repeated the 300-warmup/120-field supplied Lemmings
disk-1 read-DMA smoke: exact prior fingerprints `ADC971FCB2A6DBCF` /
`B03AA632A52ED4F8` / `DA9473FB73EC75EB`, cycle `59683034`, zero allocations
and no active unsupported feature. It uses the synthetic ROM, not native
Kickstart/gameplay. Its unpinned FPS is excluded from acceptance evidence.

**Disposition: retain the three simplifications. H6b2 host-path checks PASS;
implemented-model tests PASS; physical timing remains OPEN (T2–T4). Overall
H6b2 remains OPEN, H6c held, and production unchanged.** No correctness or
hardware-evidence requirement was waived for this performance result.
Next is the bounded disk-request/FIFO/completion timing work, not another
round of unmeasured accessor changes. Larger future optimization decisions
should follow a complete-workload profile; 200 native-gameplay FPS remains
unfinished.

Evidence under `.codex-tmp/`:

- Generated code: `h6b2-opt-audit-current-jit.txt`,
  `h6b2-opt-audit-reference-jit.txt`, `h6b2-inline-state-jit.txt`,
  `h6b2-inline-disk-jit.txt`, `h6b2-inline-disk-receive-jit.txt`,
  `h6b2-lazy-sync-receive-jit.txt`. These diagnostic runs' FPS is not evidence.
- Experiments: `h6b2-inline-state-serial-retention.txt`,
  `h6b2-inline-state-active-dma.txt`, `h6b2-inline-disk-serial-retention.txt`,
  `h6b2-inline-disk-active-dma.txt`, `h6b2-lazy-sync-active-dma.txt`.
- Final controls: `h6b2-optimized-final-disk-input.txt`,
  `h6b2-optimized-final-active.txt`, `h6b2-optimized-final-retention.txt`,
  `h6b2-optimized-final-active-dma.txt`.
- Driver: `run-h6b1-optimization.ps1`, now with `-BothRamDma` for same-work
  active-DMA comparisons. It still requires cross-engine fingerprints to
  match in those comparisons; no threshold or rejection rule was weakened.

#### H6b2 gate acceptance and continuation (2026-09-13)

After recording the deferred-issue policy, the user stated: "We can proceed
with our gate." This accepts **H6b2 GO for experimental continuation under
the documented bounded model**, and closes H6b on that basis. It supersedes
the earlier overall OPEN/H6c-held dispositions, not their measurements or
the still-open hardware questions. It is not a claim of fully verified disk
cycle correctness.

Gate-close verification on the current source:

- Complete lightweight suite: **219/219 Debug and 219/219 diagnostic Release**,
  zero failures or skipped tests. Both were rebuilt with `-m:1`.
- Non-diagnostic Release runner rebuilt into
  `.codex-tmp/lightweight-h6b2-gate-close-20260913`. Engine, runner, Copper68k
  and CopperDisk DLL SHA-256 values exactly match the frozen
  `lightweight-h6b2-lazy-sync-20260913` build recorded above. No execution
  source changed for this gate decision.
- Supplied Lemmings disk-1 read-DMA smoke repeated with the synthetic ROM,
  300 warmup and 120 measured fields: cycle `59683034`, CPU
  `ADC971FCB2A6DBCF`, hardware `B03AA632A52ED4F8`, output
  `DA9473FB73EC75EB`, 142,102 pixels, 1,922 audio samples in the final field,
  real stereo PCM, **zero allocated bytes**, and `unsupported=none`.
  These exactly reproduce the prior smoke. Its unpinned FPS is not gate
  evidence, and it is not native-ROM gameplay.
- The existing `Microsoft.Build.Tasks.Git` NU1902 warning remains; no package
  or shared CPU change was made in this documentation/gate-close step.

| Gate | Accepted disposition |
| --- | --- |
| Implemented-model checks | **PASS**, refreshed as above. |
| Hardware verification | **OPEN** for LWA-DISK-001 through LWA-DISK-008; neither waived nor represented as proven. These recorded uncertainties alone do not block experimental continuation. |
| Dormant-path performance | **PASS**, retained valid same-build measurements: serial-only 98.85%, drive-idle 99.21%, STOP 99.26%, each above 97%. |
| Active read-DMA performance | **PASS**, retained valid same-work measurement: 441.14 FPS / 2.2669 ms, 102.92% retention against the initial H6b2 build, above 95%. Synthetic workload only. |
| Allocation/determinism | **PASS**, existing measured samples and refreshed disk smoke. |
| Overall | **H6b2 / H6b GO for experimental continuation; H6c next.** |

The performance rows reuse the prior policy-v2-valid interleaved series, whose
binary identity was checked above; they are not new throughput measurements.
No threshold, workload, sample protocol or host-contamination rule changed.

**Next bounded task: H6c, remaining CIA/input execution.** Connect TOD and the
required keyboard/mouse/joystick protocol, with focused cycle-causal checks,
deterministic input replay and a measured connected-machine run. Preserve the
single clock, direct compact state and zero-allocation release path. Record
omissions and revisit disk issues when their documented triggers arise; do not
reopen all disk research as an unconditional prerequisite.

Stop at the H6c acceptance boundary. H6c itself is not yet implemented/accepted
by this entry. H6d native-gameplay acceptance, H7, production selection and the
200 complete-gameplay FPS objective remain unfinished. The historical G6/G7
outcomes are unchanged.

#### H6c opening: first bounded TOD checkpoint (2026-09-13)

The user authorized **"Proceed to H6c"**. H6c is being connected in measured
pieces so clock-loop cost can be attributed before keyboard and controller
work is added. This first piece, **H6c1**, implements TOD; it does not close
the full CIA/input gate or claim native-workload acceptance. The remaining
H6c work stays authorized, subject to the checkpoint results below.

**Implemented:** a compact 24-bit binary counter, write-only alarm, coherent
MSB-to-LSB read latch, MSB-stop/LSB-start programming, reset, pending alarm
ICR bits and mask enable/clear behavior in the existing CIA owner. CIA-B
receives one pulse at the canonical line boundary; CIA-A at the field
boundary. The existing clock directly invokes these operations. There is no
new event framework, speculative replay, schedule cache, per-cycle log,
callback subscription or allocation in the execution path.

An enabled future alarm participates in the existing CPU/STOP wake boundary.
CIA-B uses the exact remaining line-pulse count; CIA-A predicts only the next
field, because VPOSW/LACE can change subsequent field lengths. VPOSW writes
and frame completion refresh that prediction. Alarm requests follow the
existing eight-CPU-cycle CIA-to-Paula IPL visibility model. A stopped CPU
therefore wakes for the alarm without waiting until the frame ends.

**Evidence boundary:** [Commodore HRM, 8520 TOD and interrupt registers](https://bastya.net/AmigaDevDocs/hard_f.html)
supports the binary counter, alarm ownership and read-latch protocol. Existing
Legacy tests establish useful regression scenarios, not a physical timing
oracle. Legacy lacks the implemented counter stop/restart behavior and its
conformance matrix explicitly leaves TOD debounce pending. [WinUAE CIA source](https://raw.githubusercontent.com/tonioni/WinUAE/master/cia.cpp)
is supporting evidence for additional qualification and MSB/LSB programming,
not hardware proof or imported execution code.

The boundary-based board pulse model, reset details and comparator-write
edges remain explicitly unverified in **LWA-CIA-001 / LWA-CIA-002** in the
[issue register](LIGHTWEIGHT_A500_ENGINE_ISSUES.md). Counter transitions and
interrupts are causal within this model; neither its boundary tests nor its
speed results establish exact physical TOD phases. Unsupported-feature
documentation retains this distinction.

Validation:

- `LightweightCiaTodTests`: **19/19**, covering binary carry/wrap, reset,
  stop/restart, repeated high-byte read latching, middle-byte reads/writes,
  alarm ownership/masking, prediction across wrap, accepted pulse versus later
  register write, CPU odd phases, line/field boundaries, VPOSW including late
  field selection, coarse/single-cycle partition equality, eight-cycle IPL
  visibility, STOP wake before field end, external reset and zero allocation.
- Complete lightweight suite: **238/238 Debug and 238/238 diagnostic Release**,
  zero skipped tests. No shared Copper68k or Legacy execution source changed;
  the historical emulator corpus was not rerun for this isolated-engine edit.
- One initial Release run failed the new allocation test with only ten warmup
  fields. It passed both an isolated repeat and a full repeat unchanged; the
  cause was not established. The test now uses the engineering runner's 300
  warmup fields and snapshots allocations before invoking assertions. Both
  final suites pass; retain the initial observation rather than claiming a
  diagnosed engine allocation repair. The non-diagnostic runner separately
  verifies zero steady-state allocations.
- `--cia-tod` in the runner explicitly starts both counters before warmup and
  checks their exact field/line progress outside the timed interval. It hashes
  that state and fails if TOD execution is unavailable. It composes with the
  existing complete-pixel/real-PCM synthetic disk-DMA workload. It does not
  pretend to provide the unfinished native gameplay/input replay.

Frozen candidate: `.codex-tmp/lightweight-h6c1-tod-20260913`.

| Binary | SHA-256 |
| --- | --- |
| Lightweight engine | `FB22516600D28731DD63E0DEE6BCAC6490391ACBF59F97D9761F8D35C7DAB0F1` |
| Runner | `C079FFDC8FD9BC533927DBF9A3DCA4D25EF85A716547DCDF14E775E35AA56252` |
| Copper68k (unchanged) | `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497` |
| CopperDisk (unchanged) | `8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227` |

The reference `.codex-tmp/lightweight-h6c1-h6b2-ref-20260913` pairs the same
runner with frozen accepted H6b2 engine `9FE7B6E1521E2BD831A055844E83AC0201B4E75E6522803FC7B712F1627AEBC5`;
the original accepted freeze is untouched. Dormant comparisons require exact
cross-build fingerprints. The explicitly TOD-active candidate versus old
TOD-absent reference is a cost comparison: fingerprints must be deterministic
within each side, but are not required to match across different work.

Engineering thresholds retained before sampling: **97%** for TOD-dormant
controls and **95%** for the newly active path. Protocol: 300 warmup / 3,600
measured fields, three samples per side, C/R/R/C/C/R order; verified group 0
logical CPU 4, affinity 16, efficiency class 1, sibling 5, Normal priority,
Balanced power GUID `381b4222-f694-41f0-9685-ff5bb260df2e`, host-load policy v2.
Placement is host-specific evidence, not a portable CPU selection rule.

Completed engineering samples:

| Workload | Candidate FPS samples | H6b2 FPS samples | Medians C / R | Spread C / R | Retention |
| --- | --- | --- | --- | --- | --- |
| Active disk DMA, TOD dormant | 424.08, 419.57, 420.89 | 415.73, 414.17, 418.76 | 420.89 / 415.73 | 1.07% / 1.10% | **101.24% PASS** (97%) |
| STOP, TOD dormant | 24769.98, 25358.29, 24524.56 | 23569.82, 24403.80, 24332.81 | 24769.98 / 24332.81 | 3.37% / 3.43% | **101.80% PASS** (97%) |
| Active disk DMA plus both TOD counters / H6b2 without TOD | 416.42, 418.25, 420.28 | 413.41, 420.38, 420.98 | 418.25 / 420.38 | 0.92% / 1.80% | **99.49% PASS** (95%) |

All three series passed placement, priority, policy-v2 telemetry and spread
checks. Every sample produced full pixels and real stereo PCM, allocated zero
steady-state bytes and reported no active unsupported feature. Dormant
fingerprints match H6b2 exactly. With TOD active, the candidate retains cycle
`554197982`, CPU `4EC69AC79128F103`, output `CC609FA86CAFD46B`, 142,102 pixels
and 1,924 final-field audio samples; its extended hardware fingerprint is
`EEC2C96B7D692162`, identical across its samples. The reference retains
`A209A1FB2AAD014E` because it neither executes nor hashes TOD. These values
must not be required to match across different work.

The connected synthetic budget is **2.3909 ms/frame**, leaving **2.6091 ms**
of 5 ms for still-unmeasured native CPU/input/compatibility work; this is not
a forecast. The approximately 0.0121 ms apparent TOD-active difference is
smaller than the reference spread; no precisely established slowdown or
speedup is claimed. Compare paired controls, not this session's absolute
FPS against the older 441.14-FPS result.

The supplied Lemmings disk-1 smoke, now with TOD running, also passes at
300 warmup / 120 measured fields: cycle `59683034`, CPU `ADC971FCB2A6DBCF`,
hardware `B23FC0F2A9CF3A14`, output `DA9473FB73EC75EB`, full pixels, real PCM,
zero allocations and no active unsupported feature. This is still a synthetic
ROM reading the supplied disk, not native Lemmings gameplay; its unpinned
throughput is excluded from acceptance.

**H6c1 checkpoint: implemented-model checks PASS, dormant retention PASS,
active-cost check PASS, allocation/determinism PASS. Retain the TOD path;
continue within authorized H6c scope.** Hardware phase questions stay OPEN
under the documented experimental policy. **H6c overall remains OPEN**:
keyboard serial delivery/handshake, mouse/joystick register/pin behavior and
deterministic input replay are not yet implemented. Production, H6d/H7 and
the 200 complete-gameplay FPS objective remain unchanged.

Evidence under `.codex-tmp/`: `h6c1-release-tests-20260913.txt`,
`h6c1-release-final-tests-20260913.txt`, `h6c1-dma-retention-20260913.txt`,
`h6c1-stop-retention-20260913.txt`, `h6c1-tod-active-cost-20260913.txt`.
Driver: `run-h6b1-optimization.ps1` with the explicit candidate/reference
directories above. `-Series active -BothRamDma` runs the dormant-TOD disk
control; add `-CandidateTod` for the new-work comparison. `-Series retention`
runs STOP. `-ReferenceTod` is also supported for future same-work comparisons;
cross-build fingerprints remain mandatory unless the two sides explicitly
request different work. No test/build ran concurrently with these series.

#### H6c2 synchronized keyboard and digital input (2026-09-13)

The user requested continuation after the passing TOD checkpoint. H6c2 adds
the remaining **bounded input path**: synchronized raw keyboard transmission,
the host handshake, mouse counters/buttons, digital joystick direction/fire,
JOYTEST, digital POTGO/POTGOR and deterministic input replay. This supersedes
the earlier store-only input description, not the historical test results.

Implementation:

- `LightweightKeyboard` is compact value state with an inline ten-byte
  type-ahead buffer, one retained byte, a bit/phase position and one pending
  cycle. Data setup, KCLK low and KCLK rising phases run on the same machine
  clock. CIA-A receives each bit; SDR/serial ICR become visible only on the
  eighth rising edge. The existing CIA wake boundary incorporates the pending
  keyboard phase, so no additional keyboard poll is added to each CCK.
- A byte remains pending until the host drives the data line low then high.
  **Reading SDR or ICR does not acknowledge a key.** The CIA serial input
  buffer and small output latch are separate, so receiving a byte does not
  overwrite the host's handshake latch. Cold reset clears keyboard state;
  the 68000 RESET device operation clears the CIA while retaining the
  independently clocked keyboard's pending physical transfer.
- Controller state is applied once at the current submission cycle. Mouse
  deltas wrap eight-bit counters; joystick directions use XOR-encoded JOYDAT;
  fire buttons reach CIA-A port inputs and additional buttons reach POTGOR.
  JOYTEST preserves low counter bits. There is no controller event queue or
  clock-loop input poll. Unsupported analog counter reads are reported.
- `SubmitKey(byte)` accepts raw Amiga make/break codes; bit 7 is release.
  `SubmitInput` takes joystick flags up/down/left/right/fire/second-fire as
  bits 0–5; port-0 bit 7 selects joystick instead of mouse. Mouse buttons
  are left/right/middle bits 0–2. `KeyCode=0` means no event; `0x100 | raw`
  submits a key, including raw key zero. Invalid flags and type-ahead overflow
  are explicit errors, not dropped input. Calls belong on the hardware-owner
  thread; keyboard layout, repeat and caps-lock translation belong to the host.

Protocol source: [Commodore HRM keyboard interface](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_g.html),
[8520 serial input](https://bastya.net/AmigaDevDocs/hard_f.html),
[CIA controller/fire pin assignments](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0117.html),
and [digital joystick encoding](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0183.html).
No Legacy input/requester execution was imported. The old SDR-read
acknowledgement tests are not an oracle for the keyboard's wire protocol.

**Declared limits:** this is an already-synchronized keyboard peripheral, not
an emulated keyboard MCU. Power-up/resynchronization, reset chords, general
CIA serial output/CNT timer modes and analog controller behavior remain
omitted. A handshake timeout, serial-output collision, active unsupported CNT
mode or analog counter use reports unsupported. Nominal 142-cycle bit phases,
minimum eight-cycle acknowledgement, CIA interrupt qualification and accepted
host mouse-counter transitions are not claims of complete pin-level hardware
verification. Follow-ups are **LWA-INPUT-001 through LWA-INPUT-003** in the
[issue register](LIGHTWEIGHT_A500_ENGINE_ISSUES.md). Native boot must revisit
startup/recovery if required; this synthetic fixture cannot waive that work.

Focused validation: **26 input checks**, including make/break/zero-byte wire
encoding; data/clock/sample phases; eighth-edge latch; mask/IPL delay;
SDR/ICR reads versus acknowledgement; short handshake rejection; buffering
and explicit overflow/timeout; frame crossing, odd-cycle scheduling and STOP
wake; CIA direction/reset during partial bytes; mouse wrap and button release;
joystick XOR directions, port selection and JOYTEST; POTGO output and analog
rejection; deterministic coarse/single-cycle replay; and zero allocations.
The complete lightweight suite passes **264/264 Debug and 264/264 diagnostic
Release**, with no skipped checks. No shared Copper68k or Legacy execution
source changed; unrelated historical suites were not rerun.

The runner adds `--input-replay` to the existing active hardware fixture. Each
field submits deterministic mouse/button/joystick state and alternating key
make/break, executes the normal field path, checks the received byte/ICR and
controller values, then supplies the actual low/high host handshake before
the next byte. Checks use CPU bus accesses, including their real contention
waits; their work is included in timing. Every field still generates full
pixels and real 48 kHz stereo PCM with active disk/audio/display/sprite/blitter
work. The fixture verifies the requested completed-field count and consumes
input/output fingerprints. Its host-driven register service is synthetic,
not native keyboard-driver or Lemmings gameplay evidence.

Frozen candidate: `.codex-tmp/lightweight-h6c2-input-final-20260913`.

| Binary | SHA-256 |
| --- | --- |
| Engine | `5EEC55EF77CB197E9467C1E6CAC5E5DA5D9D7541C363A3B0DA1F13F8B696811B` |
| Runner | `D7E84A98406FBF1B818613BDE0A58937B46EE3E0729F16B62B9ED87AE5ACF678` |
| Copper68k (unchanged) | `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497` |
| CopperDisk (unchanged) | `8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227` |

Reference `.codex-tmp/lightweight-h6c2-h6c1-ref-20260913` uses the identical
runner and frozen H6c1 engine `FB22516600D28731DD63E0DEE6BCAC6490391ACBF59F97D9761F8D35C7DAB0F1`.
Original freezes remain untouched. The input-dormant comparisons require
exact fingerprints across engines; the newly input-active cost comparison
requires deterministic fingerprints within each side, not equality between
different work. Its completed fields include different CPU bus-service phases
and therefore can have different final within-field offsets/fingerprints.

Retained engineering protocol: 300 warmup / 3,600 measured fields, three
samples per side in C/R/R/C/C/R order, 97% dormant and 95% active retention;
verified P-core group 0 CPU 4 / affinity 16 / efficiency class 1 / SMT sibling
5, Normal priority, Balanced power plan and host policy v2. These CPU numbers
are machine-specific evidence. Performance disposition follows below.

Initial H6c2 results (all three series valid under policy v2):

| Workload | Candidate samples / median FPS | H6c1 samples / median FPS | Retention / gate |
| --- | --- | --- | --- |
| Input dormant, active disk DMA + TOD | 433.32 / 433.72 / 435.57; **433.72** | 438.19 / 436.69 / 435.66; **436.69** | **99.32%, PASS** (97%) |
| Inactive ROM STOP | 26027.77 / 26317.42 / 26735.90; **26317.42** | 25850.52 / 26050.56 / 26179.63; **26050.56** | **101.02%, PASS** (97%) |
| New input replay + disk DMA + TOD versus disk DMA + TOD | 402.34 / 405.41 / 388.19; **402.34** | 438.30 / 421.90 / 440.45; **438.30** | **91.79%, FAIL** (95%) |

Candidate/reference spreads were respectively 0.52%/0.58%, 2.69%/1.26%
and 4.28%/4.23%. Every sample allocated zero bytes. Dormant fingerprints
matched across engines; input-active fingerprints were deterministic within
each engine. The active failure is a valid cost result, not invalid host
noise: **H6c remains OPEN until a passing replacement exists.** Evidence:
`.codex-tmp/h6c2-dormant-retention-20260913.txt`,
`h6c2-stop-retention-20260913.txt`, and `h6c2-input-active-cost-20260913.txt`.

##### H6c2 CPU bus-wait simplification

A short sampled profile (`.codex-tmp/h6c2-input-cost.nettrace`) attributes
88.77% inclusive time to `ReadWord`, including DMA execution while controller
reads wait for Chip-RAM bus access. `TickDevices` is 56.62% inclusive and
26.02% exclusive. These overlapping sampled percentages are not additive and
do not establish that keyboard execution itself is free. They identify a
concrete coordination cost: repeatedly entering arbitrary-target clock
advancement for every stolen CPU slot during a sustained nasty blit.

The candidate keeps the pending CPU request inside `LightweightClock` until
the first available slot. It publishes the next CPU candidate before each
CCK and calls the same `CompleteColorClock` routine used by device-only
advancement. It still executes and arbitrates every CCK; no range batching,
schedule cache, event objects, skipped DMA or separate device timeline is
introduced. CPU word completion remains grant + two CPU cycles, and longword
phases remain separate. Nice/nasty arbitration retains its candidate ordering.

All **264/264 Debug and Release** checks pass after this change. A 300-warmup /
120-measured synthetic input replay with the supplied Lemmings Disk 1 ZIP
passes with zero allocations, real PCM and no unsupported feature. Its exact
cycle/CPU/hardware/output fingerprints match the initial H6c2 build:
`59816386 / 63D50368CDEDE737 / 9E9507B7EBF13808 / F54D348B32C67C5B`.
This is disk/input regression evidence, **not native Lemmings gameplay**.

Frozen candidate `.codex-tmp/lightweight-h6c2-cpuwait-20260913`:

| Binary | SHA-256 |
| --- | --- |
| Engine | `285A6AD4D904CF3D33D1524D9EAA161F0CF0CD300678F4723F965F5BCF326BDD` |
| Runner | `9CE0215BC49F26CAF94A364B582492B21F2DC452E67E3EEF070EB915EB12164A` |

Copper68k and CopperDisk hashes are unchanged from the table above.
Same-runner references `lightweight-h6c2-cpuwait-input-ref-20260913` and
`lightweight-h6c2-cpuwait-tod-ref-20260913` copy this candidate's runner and
supporting files, replacing only the engine DLL with the initial H6c2 or H6c1
frozen engine respectively. Original freezes are untouched.

The first comparison was refused before sampling because runner identities
differed (`h6c2-cpuwait-samework-20260913.txt`). The same-runner retry was
**INVALID / RERUN** when a separate CopperStart.Intuition build started
(`h6c2-cpuwait-samework-rerun-20260913.txt`). Completed samples had identical
execution fingerprints; that does not waive the performance gate. Neither
attempt supersedes the preceding valid active-cost FAIL.

The second same-runner retry was also **INVALID / RERUN** when the independent
CopperStart.Intuition test run appeared (test/vstest/testhost processes;
`h6c2-cpuwait-samework-rerun2-20260913.txt`). Those workloads were not stopped.
Normal background applications were not rejected merely for being present.

**Disposition before the clean benchmark below: H6c2 implemented-model correctness PASS; initial
active-cost retention FAIL; CPU-wait optimization performance UNCONFIRMED.
H6c overall OPEN, not GO.** The clock-loop candidate remains in the working
tree for validation, not as an accepted performance improvement. Production
selection is unchanged. The uncertainty register remains applicable.

Continuation validation (2026-09-13): the independent
`NativeServiceReentryReapsRootBeforeHookCancelWrapperReturns` test was still
active on entry, so no new throughput-gate series was started. Instead, both
frozen CPU-wait and same-runner pre-optimization input engines completed a
full **300 warmup + 3,600 measured-field-length replay** with disk serial,
RAM DMA, TOD and input enabled. Exact fingerprints matched:

| Completed fields | Cycle | CPU | Hardware | Output |
| --- | --- | --- | --- | --- |
| 3,900 | `554331342` | `3051D82B2D7994D3` | `3B959D7FDCC85DA3` | `6C6B7906B62638AE` |

Both produced 142,102 final-field pixels, 1,924 interleaved stereo PCM samples, zero
steady-run allocated bytes and no unsupported feature. Evidence:
`.codex-tmp/h6c2-cpuwait-full-replay-correctness-20260913.txt`.
**This is a correctness-only comparison.** The runner's incidental FPS was
collected during competing tests without controlled placement/telemetry and
must not be used as optimization or gate evidence. The independent test was
still active afterward. No engine code changed in this continuation; the
performance disposition above remains unchanged.

At that point, resume with these bounded steps; do not expand native compatibility while
silently abandoning the outstanding checkpoint:

1. Run `run-h6b1-optimization.ps1` against the frozen CPU-wait candidate and
   its `cpuwait-input-ref` with both disk DMA, TOD and input replay enabled.
   Require identical fingerprints across both engines and valid policy-v2
   telemetry for all six samples. Retain the change only with useful measured
   evidence and no correctness regression; otherwise revise/revert this
   specific candidate, preserving unrelated work.
2. Compare the candidate with its same-runner `cpuwait-tod-ref`: disk DMA +
   TOD dormant-input control (97%), inactive ROM STOP (97%), and candidate
   input replay versus reference no-input active cost (95%). Preserve the
   300/3,600 lengths and three interleaved samples per side.
3. Publish those results before requesting H6c acceptance. H6d native
   Kickstart/Lemmings boot and complete-machine FPS remain separate future
   work; keyboard startup/recovery may require implementation there.

##### H6c2 clean benchmark and replacement disposition (2026-09-13)

The user requested the benchmark. Preflight and postflight found no competing
test/build workload. No build or test was launched during these measurements.
The four series below ran sequentially against the frozen CPU-wait candidate
and the same-runner references identified above; no engine code changed.

Protocol unchanged: **300 warmup / 3,600 measured fields, three samples per
side, C/R/R/C/C/R**, verified group 0 logical CPU 4, affinity mask 16,
efficiency class 1, SMT sibling 5, Normal priority. Fresh topology, binary
hashes, Balanced power plan `381b4222-f694-41f0-9685-ff5bb260df2e` and
selected/sibling/aggregate policy-v2 telemetry are recorded in each log.
All four series are **VALID**: no competing workload or sustained contention
violation, confirmed placement/priority, deterministic fingerprints and
sample spread below 10% of the median. No temperature/clock-speed claim is
inferred from the aggregate-pressure telemetry.

Series order and results:

| Comparison | Candidate FPS samples; median | Reference FPS samples; median | Result |
| --- | --- | --- | --- |
| Identical input-active work, CPU-wait versus initial H6c2 | 426.64 / 416.99 / 420.89; **420.89** | 396.36 / 388.46 / 387.94; **388.46** | **+8.35% throughput**, identical execution |
| Input-active candidate versus H6c1 disk DMA + TOD | 415.69 / 419.43 / 421.33; **419.43** | 428.15 / 433.26 / 436.29; **433.26** | **96.81% retention, PASS** (95%) |
| Input dormant, disk DMA + TOD on both | 431.54 / 424.03 / 431.68; **431.54** | 423.14 / 422.68 / 417.10; **422.68** | **102.10% retention, PASS** (97%) |
| Inactive ROM STOP | 24723.49 / 26057.35 / 26247.83; **26057.35** | 26535.91 / 25209.63 / 26280.61; **26280.61** | **99.15% retention, PASS** (97%) |

Candidate/reference spreads in that order: **2.29%/2.17%, 1.34%/1.88%,
1.77%/1.43%, 5.85%/5.05%**. The same-work candidate takes 2.376 ms/field;
the independently measured input-active retention candidate takes
2.384 ms/field. Do not compare non-interleaved medians from different series
to attribute another speed gain or loss.

Every sample completed 3,900 fields, produced full pixels and real 48 kHz
stereo PCM, allocated zero steady-run bytes and reported no unsupported
feature. Identical-work comparisons matched exact cycle/CPU/hardware/output
fingerprints across engines; different-work input-cost comparisons matched
within each engine. The active input tuple remains
`554331342 / 3051D82B2D7994D3 / 3B959D7FDCC85DA3 / 6C6B7906B62638AE`.
The disk/TOD control tuple remains
`554197982 / 4EC69AC79128F103 / EEC2C96B7D692162 / CC609FA86CAFD46B`.
The STOP tuple remains
`554197800 / A3A40F49B8CFEC65 / 5FA0BF87BC7833B6 / 0123DB1732B6D603`.

Evidence under `.codex-tmp/`, in series order:

- `h6c2-cpuwait-samework-rerun3-20260913.txt`
- `h6c2-cpuwait-active-retention-20260913.txt`
- `h6c2-cpuwait-dormant-retention-20260913.txt`
- `h6c2-cpuwait-stop-retention-20260913.txt`

**Replacement disposition: retain the CPU-wait simplification. H6c2
implemented-model correctness PASS, active-cost and dormant retention PASS.
LWA-PERF-001 is resolved for this checkpoint. H6c is ready for experimental
acceptance by the user; acceptance itself has not been granted here.** This
supersedes the earlier active-cost FAIL and unconfirmed performance
disposition, not the historical measurements or invalid attempts.

The 264/264 Debug and Release checks and supplied-disk smoke remain the
correctness evidence for the unchanged source. Hardware uncertainties,
keyboard startup/recovery and analog input limits remain OPEN in the issue
register; matching fingerprints is not hardware verification. H6d native
boot/gameplay and the complete-engine 200 FPS objective remain unfinished.
These **synthetic hardware FPS are not native Lemmings FPS**. No production
switch or native-compatibility expansion was performed.

##### H6c user acceptance (2026-09-13)

The user explicitly stated **"I accept H6c"** after the clean benchmark.
**H6c is accepted / GO for experimental continuation within its documented
TOD, synchronized-keyboard and digital-controller models.** This supersedes
the earlier awaiting-acceptance and overall OPEN dispositions, not their
historical results. The CPU-wait simplification is retained.

Acceptance rests on the 264/264 Debug and Release checks, deterministic
input replay and supplied-disk smoke, zero allocations, +8.35% same-work
throughput gain, 96.81% active-input retention and passing dormant controls.
These remain separate implemented-model correctness and host-performance
results, not proof of every physical hardware edge.

**Next stage: H6d**, native Kickstart 1.3 cold boot and interactive Lemmings
gameplay with sound, using the existing complete-workload validation and
measurement protocol. Keyboard startup/recovery and any other required
supported-machine corrections must be addressed there if exercised.
The issue register's unresolved timing/compatibility entries remain OPEN.
H6d itself is not accepted, H7 remains held, and production is unchanged.
The complete-engine **200 FPS objective remains unfinished**. This acceptance
record makes no source change and does not start H6d execution.

##### H6d opening: reproducible native boot and first blockers (2026-09-13)

The user authorized **"Proceed with H6d"**. Native Kickstart 1.3 now executes
against the supplied Lemmings Disk 1 on the accepted H6c engine. No Legacy
adapter, CopperStart path, engine source change or unsupported-feature bypass
was introduced. The engine DLL is still
`285A6AD4D904CF3D33D1524D9EAA161F0CF0CD300678F4723F965F5BCF326BDD`.

Added host-runner support:

- `--input-script <json>` reads a frame-ordered array once before execution.
  Frame zero precedes the first emulated field. Each entry submits the normal
  public input state on the hardware-owner thread; no CPU/register injection
  or game-specific engine path is used. The script spans warmup and measured
  fields using one frame index. Native ROM is required; synthetic fixtures
  and the synthetic input replay cannot be combined with it.
- `--boot-probe <directory>` records bounded field-boundary CPU/beam/custom/
  CIA/drive state, Chip RAM and uncropped BMP images outside engine execution.
  It captures the first field, every 60 fields, final field, and first reported
  unsupported conditions. Snapshots read state without CPU bus accesses.
  This mode explicitly marks FPS/allocation totals diagnostic, not acceptance.
- Default probing stops on unsupported behavior. The explicit diagnostic-only
  `--probe-continue-unsupported` allows bounded observation beyond it, but
  leaves the original warning latched and the process result unsuccessful.
  A separate video warning is captured so the earlier disk warning cannot
  hide the later unsupported display mode. No release per-cycle logging or
  new engine event/deadline mechanism was added.

Exploratory script:
`CopperMod.Amiga.Lightweight.Runner/Workloads/lemmings-native-exploratory.json`.
It presses left mouse before fields 600 and 3,600 and releases before 606 and
3,606. Its SHA-256 is
`132106D553CA4C98DAE6C96ADE2075596F820DF0D8B1A432CCB13F657284AF24`.
ROM and Disk 1 ZIP hashes were rechecked and match the frozen input table.
The final runner DLL hash is
`BF26F572079BAC16E5908566222DB79A2AD6C97E9084F7150E3816DDC29D3E53`.

Observed checkpoints, not hardware proof:

| Field | Observation |
| --- | --- |
| 1–300 | Native ROM initializes, disables overlay, performs disk DMA and transfers control into loaded Chip-RAM code. |
| 360–600 | The disk's Skid Row intro runs; sampled PCM is nonzero by field 420. Its full visual correctness is not established. |
| 600/606 | Scripted mouse press/release exits that intro and starts the game loader. |
| 662 | First disk warning: `ADKCON=0000`, `DMACON=0000`, `INTENA=0000`, `DSKLEN=4000`, no active RAM DMA, selected motor-on drive. Field-end cycle `94071530`, PC `000704E4`, cylinder 72/head 0. |
| 1,800 | After diagnostic continuation, FAST is restored (`ADKCON=1500`) and the Psygnosis presentation is visibly rendered while loading continues. |
| 1,949 | First separately recorded video warning: `OCS hires output`; `BPLCON0=C200`, four hires planes, `DDFSTRT=003C`, `DDFSTOP=00D4`. Field-end cycle `276956814`, PC `000794A4`. |
| 3,600 | Animated Lemmings balloon/mountain introduction is visible in a later low-resolution mode. |
| 4,800 | After the second scripted click the machine reaches another hires screen (`BPLCON0=C200`), but output is blank because hires fetch/decode is unimplemented. Interactive gameplay is not verified. |

The disk warning is triggered by selected-drive serial input during a
temporary slow-mode setting, before DMA is armed. Subsequent successful
loading does **not** prove that skipping receiver effects during this interval
is correct. The [Commodore HRM disk control description](https://bastya.net/AmigaDevDocs/hard_8.html)
distinguishes FAST bit-cell rates and states that sync interrupts are
independent of WORDSYNC. Do not suppress the warning merely because DMA is
disabled, or derive a hardware rule from this title. Record and resolve the
required transition behavior as **LWA-DISK-009**.

The hires omission is explicit in both `LightweightBitplanes` and
`LightweightVideo`: each currently selects zero supported planes in hires.
This is a concrete required feature gap, **LWA-VIDEO-001**, not a CPU or
disk-decoding diagnosis based on a black screen. Proper hires requires fetch
cadence, shifter phases and sufficient output resolution; dropping every
other pixel is not a complete-output implementation.

Validation:

- Release runner builds. **264/264 lightweight Release tests PASS**, none
  skipped. Shared CPU/Legacy source is unchanged, so historical suites were
  not rerun. Existing NU1902 package warning remains unrelated.
- Repeated 4,800-field script with and without snapshots gives identical
  cycle/CPU/hardware/output:
  `682089600 / 151A5A91B837072D / 15C370AEA2626F4E / F30A779F8BBAAC66`.
  Both terminate unsuccessfully for unsupported disk recovery; continuation
  did not clear the failure. The independent video failure is also recorded.
- No formal native FPS result exists. The no-probe cold-boot exploration
  printed 253.47 FPS and 602,208 allocated bytes without warmup; it includes
  unsupported/blank hires output, no verified gameplay and no placement/load
  protocol. **Neither that number nor diagnostic FPS is performance evidence
  for the complete-engine 200 FPS objective or a switch gate.**

Evidence under `.codex-tmp/`: `h6d-native-opening-20260913.txt`,
`h6d-native-probe1.txt` through `h6d-native-probe6.txt` and their snapshot
directories, `h6d-native-script-no-probe-20260913.txt`, and
`h6d-opening-release-tests-20260913.txt`. Probe 6 is the final diagnostic
format, including separate video failure and nonzero-PCM observation.

Reproduce from `CopperMod.Amiga` after building the Release runner:

```powershell
dotnet ../CopperMod.Amiga.Lightweight.Runner/bin/Release/net10.0/CopperMod.Amiga.Lightweight.Runner.dll --rom D:/TestData/ROM/Kickstart_13.rom --adf "D:/TestData/TestImages/Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip" --frames 4800 --input-script ../CopperMod.Amiga.Lightweight.Runner/Workloads/lemmings-native-exploratory.json --boot-probe ../.codex-tmp/h6d-native-repro --probe-continue-unsupported
```

**H6d remains OPEN, not GO.** Next bounded implementation is OCS hires
fetch/output with focused timing tests and measured lowres/hires work, then
the required disk-mode transition correction and native input/gameplay
continuation. Keep each correction isolated, preserve the H6c frozen control,
and measure its cost before connecting another slice. Do not accept native
gameplay or start H7 until all required work and the original full-workload
protocol pass. Keyboard startup/handshake and gameplay sound remain to be
verified in the native path; nonzero intro PCM does not establish them.

##### H6d hires implementation checkpoint (2026-09-13)

The next user **"Please proceed"** authorizes the bounded hires slice described
above. **Implementation/tests PASS for the recorded model; performance remains
INVALID / RERUN, not GO.** No disk recovery change, production selection or
H6d/gameplay acceptance is included. The accepted H6c result is preserved.

Implemented without another execution timeline, schedule cache or release
logging:

- Hires Agnus input order `4,2,3,1,4,2,3,1`, retaining accepted addresses until
  the following CCK. Normal `DDFSTRT=003C/DDFSTOP=00D4` fetches 40 words per
  plane. The existing eight-CCK DDF control unit contains two hires words;
  modulo is applied only to each plane's final word, not both words.
- Four independent hires pixels per CCK into reusable **908 x 313** buffers.
  Lowres sections in the same framebuffer duplicate each physical lowres
  pixel; shifters are not advanced twice. Blanking and DIW remain in their
  original physical coordinate system. Host scaling remains outside the engine.
- Sprites still shift once per lowres pixel; priority is resolved separately
  against both hires playfield samples, including attached sprite pairs.
- `FramebufferWidth/Height` expose host output geometry. Native runner ROM
  sessions select width 908 automatically. The existing width-454 default is
  an explicit **lowres-only** contract used by frozen performance controls;
  it still reports unsupported hires, never silently drops alternate pixels.
- Runner `--wide-output` requests full-width synthetic output; `--hires`
  additionally selects four planes/40 words with per-line modulo -80.
  It is restricted to the bitplane or connected Paula-DMA fixture. The latter
  retains Copper, blitter, sprites, real PCM and optional disk/input work.
  New workload labels distinguish these from the unchanged lowres control.

Evidence basis: the [Commodore HRM fetch-layout description](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0072.html)
specifies normal hires DDF positions and a 4.5-CCK display-start offset;
[HRM DMA allocation](https://www.amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node012B.html)
shows the four-plane order and 160 occupied slots.
The OCS hires scroll model uses lowres-unit delay modulo eight; the
[WinUAE renderer](https://raw.githubusercontent.com/tonioni/WinUAE/master/drawing.cpp)
is supporting evidence for this interpretation, **not a hardware oracle**.
Mid-fetch resolution changes and exact reload/write-edge behavior remain
unverified under **LWA-VIDEO-002**. Deterministic stepping does not verify them.

Validation:

- **290/290 Debug and Release lightweight tests PASS**, none skipped.
  Twenty-six new hires cases cover fetch order/completion, 40-word pointers
  and one modulo, full four-plane CPU contention, distinct pixels, all scroll
  nibble values, wide lowres output, ordinary/attached sprite priority and
  single sprite advancement, accepted address surviving pointer writes/DMA
  disable, coarse versus single-cycle mode changes, short-field tail clearing
  and zero steady-frame allocations. The sprite fixture initially encoded
  output x=130 instead of x=129; correcting its comparator resolved both
  failures without changing sprite timing to fit the test.
- Release runner builds; the active hires DMA/disk/TOD/input smoke
  (`30` warmup + `60` fields) produces real stereo PCM, `unsupported=none`
  and **0 allocated bytes**. This short unpinned run is not FPS evidence.
- Old H6c and new engine on the same runner, unchanged lowres smoke, give
  identical cycle/CPU/hardware/output/allocation results:
  `12922730 / 8D7DFF3D80A3D9BF / 134BF162DD157F5B / 98FB17E89B2671E1 / 0`.
- Repeated native 4,800-field probes show readable **"Please insert the
  Lemmings disk 2 into any drive"** and mouse-button instructions. Final
  `PC=000018E6`, `BPLCON0=C200`, `videoUnsupported=null`, 284,204 output pixels;
  cycle/CPU/hardware/output repeat as
  `682089600 / D77055BD478D353A / 88B804ABE5C7A79C / 0E39A1FC87CAAD58`.
  The slow-mode disk warning remains latched and both probes exit
  unsuccessfully. Their FPS/allocation totals include snapshots and are
  **not acceptance measurements**. This is not gameplay or proof of complete
  intro graphics, disk semantics, keyboard operation or gameplay sound.
- Shared Copper68k/Legacy execution was not edited; no historical corpus rerun.
  Existing NU1902 build warning is unchanged.

Frozen final checkpoint (source checkout HEAD
`e9a1ef47444c87da626ed01a62bad4d1670e9d97` plus existing uncommitted work):

| Artifact | SHA-256 |
| --- | --- |
| Engine | `9B6F93014CE7AAF582321B1D9546AC301D30E3687A1F206FE8C8734E9A1F0AA3` |
| Runner | `0D6C02290526CE333B62C41BBF89DCE76ADE8215896509E3D53BB454B379934D` |
| Copper68k (unchanged) | `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497` |
| CopperDisk (unchanged) | `8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227` |

Final pair: `.codex-tmp/lightweight-h6d-hires2-20260913` and
`.codex-tmp/lightweight-h6d-h6c-reference2-20260913`; reference contains the
same runner with only the engine replaced by the frozen H6c DLL. Earlier
`hires1` uses the same engine and runner `C0CEFE...34B80` before explicit
synthetic wide/hires workload labels; its snapshots remain valid observations.

Performance attempt: `.codex-tmp/h6d-hires1-retention.txt` recorded verified
group-0 P-core logical CPU 4 / mask 16 / efficiency class 1, SMT sibling 5,
Normal priority and Balanced power plan
`381b4222-f694-41f0-9685-ff5bb260df2e`. Policy v2 rejected the first sample
because a separate CopperStart.Intuition test process was running. That
workload was not stopped. **No median, retention PASS or performance regression
finding is available.** Normal desktop applications were not the rejection.

Next checkpoint, before modifying disk recovery:

1. Once no competing build/test/benchmark is active, use the existing
   preflight/cooldown and policy-v2 telemetry with the final frozen pair.
   Run 300 warmup + 3,600 measured fields, three each in `C/R/R/C/C/R` order.
   Unchanged active lowres and STOP retention require the existing 97% floor,
   exact same-work fingerprints, zero allocations and <=10% median spread.
2. Measure full-width lowres versus hires on the new engine, same remaining
   active work and output size, using `-BothWide -CandidateHires` in the
   engineering driver. Record added-work cost separately; do not require
   cross-mode fingerprints, but require within-mode determinism. Investigate
   cost before accepting the slice; no cost waiver or target change is granted.
3. Publish results, then correct **LWA-DISK-009** and extend native media/input
   execution through disk 2 into actual gameplay. Native performance still
   requires the original complete-workload protocol, not synthetic FPS.

Evidence under `.codex-tmp/`: `h6d-hires-release-tests.txt`,
`h6d-hires-debug-tests.txt`, `h6d-hires-native1/2.txt` and snapshot directories,
`h6d-hires1-retention.txt`. Final snapshots are in
`h6d-hires-native2/frame-004800.bmp` and `.json`. **H6d remains OPEN.**

##### H6d performance retry and disk research (2026-09-13)

The next **"Please proceed"** retry reverified the final frozen hires2/H6c
pair, topology and Balanced power plan. Preflight remains **INVALID / RERUN**:
the separate `CopperStart.Intuition.Tests` case
`NativeServiceReentryReapsRootBeforeHookCancelWrapperReturns` is still active
(`dotnet` 123984/23504, `testhost` 57492). No benchmark sample was launched,
and none of those external processes was stopped. This is not a new
performance failure and does not change accepted H6c evidence.

The host-only engineering driver now performs a monitored ten-second
preflight plus ten-second cooldown before samples, rejects existing competing
work before launching, rechecks affinity/Normal priority during live samples,
and terminates only its own benchmark child if a series aborts. It does not
weaken policy v2 or alter the frozen runner/engine. The competing-work rejection
path was exercised; a clean full series remains outstanding. Evidence:
`.codex-tmp/h6d-hires2-retention-preflight-20260913.txt` and
`h6d-hires-preflight-retry-20260913.json`.

Read-only disk investigation continued while measurement was unavailable.
[Commodore's ADKCON description](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_8.html)
specifies the 2/4-microsecond cell selection, but not transition recovery.
[WinUAE's `nextbit/getonebit`](https://raw.githubusercontent.com/tonioni/WinUAE/master/disk.cpp)
uses data-dependent consumption when reading a two-microsecond image in slow
mode, not simple alternate-bit sampling. This is supporting implementation
evidence only; it does not establish Paula edge timing or justify importing
its image-position advancement into our independent spindle clock. The
unresolved questions are added to LWA-DISK-009. No receiver change or warning
suppression was made. Next executable checkpoint remains the hires comparison.

##### H6d requested provisional benchmarks (2026-09-13/14)

The user explicitly requested **"Benchmark it anyway."** All three comparisons
were executed despite the separate test workload. These are **provisional
engineering observations, not acceptance evidence**. No emulation change,
gate waiver, production switch or H6d GO is implied.

The host driver adds an explicit `-Provisional` option: it records policy-v2
host rejection messages without aborting for contamination. Default formal
behavior is unchanged. Invalid telemetry or placement still aborts, and
fingerprint mismatches remain errors. Driver SHA-256:
`8D2BC2F077AFE6D090BE0E5D8931121099E286447D6B1E2BF0B48EADEBE306AA`.

Frozen hires2 engine and runner hashes remain those in the checkpoint above.
Each comparison used **300 warmup + 3,600 measured fields**, three samples per
side in **C/R/R/C/C/R** order, monitored 20-second preflight/cooldown, verified
group-0 P-core CPU 4 / affinity 16 / efficiency class 1 / SMT sibling 5,
Normal priority and Balanced power plan. Series ran serially: unchanged active
lowres, full-width hires cost, then STOP+TOD. No local build or test was started
alongside them; the external workload was left alone.

| Comparison / side | Three samples (FPS) | Median FPS | Spread / median |
| --- | --- | ---: | ---: |
| Unchanged active lowres, hires2 candidate | 397.97, 399.08, 400.10 | 399.08 | 0.53% |
| Unchanged active lowres, frozen H6c reference | 388.73, 408.96, 398.55 | 398.55 | 5.08% |
| Full-width hires, hires2 candidate | 344.99, 343.62, 347.62 | 344.99 | 1.16% |
| Full-width lowres, same hires2 engine | 373.23, 387.06, 358.87 | 373.23 | 7.55% |
| STOP+TOD, hires2 candidate | 24157.34, 24365.15, 23830.01 | 24157.34 | 2.22% |
| STOP+TOD, frozen H6c reference | 24811.13, 24708.44, 24845.87 | 24811.13 | 0.55% |

Interpretation: unchanged active throughput is **100.13%** of H6c, with no
observed active lowres regression in this series. STOP retention is **97.36%**.
Hires is **92.43%** of full-width lowres throughput: **7.57% fewer FPS**, or
**0.219 ms additional time per field** (2.899 versus 2.679 ms). This is a
different-work cost comparison (four hires planes/160 fetch slots versus six
lowres planes/120), not an identical-work regression. Full-width outputs both
contain 284,204 pixels; unchanged lowres uses 142,102. Do not treat separate
series' narrow/wide ratios as an interleaved same-work comparison.

All **18 samples** completed successfully with **zero measured allocations**,
real PCM and no unsupported behavior. Within-workload fingerprints repeated;
unchanged candidate/reference fingerprints match exactly:

| Workload | Cycle | CPU | Hardware | Output |
| --- | ---: | --- | --- | --- |
| Unchanged lowres | 554331342 | `3051D82B2D7994D3` | `3B959D7FDCC85DA3` | `6C6B7906B62638AE` |
| Wide lowres | 554331342 | `3051D82B2D7994D3` | `3B959D7FDCC85DA3` | `AF2F22691E456C79` |
| Wide hires | 554319136 | `E4D8C0149D9E61FD` | `3FC545F94DE12D7B` | `8A98C07297363F5D` |
| STOP+TOD | 554197800 | `A3A40F49B8CFEC65` | `050021CC5D2A53C6` | `0123DB1732B6D603` |

Host observation: the active series recorded competing tests throughout.
Sample-interval maxima for selected-core-other / sibling / aggregate-other
were **9.1 / 0 / 12.8%** for unchanged lowres and **12.7 / 2.9 / 20.2%** for
the hires comparison. These stay below the numeric contention limits, but
the independent competing-test rule still invalidates formal acceptance.
No workload rejection was recorded in the later STOP series. Its deliberately
provisional disposition is retained; it does not validate the other series.

Evidence under `.codex-tmp/`:
`h6d-hires2-provisional-active-20260913.txt`,
`h6d-hires2-provisional-hires-cost-20260913.txt`, and
`h6d-hires2-provisional-stop-20260913.txt`. Each contains builds, topology,
power plan, sample order, placement, telemetry, fingerprints and disposition.

The result gives a useful cost estimate and does not indicate a collapse of
the existing lowres path. It is **not native Lemmings gameplay FPS** and does
not complete the 200-FPS objective. H6d remains OPEN; formal acceptance and
the unresolved disk-mode/native gameplay work remain outstanding.

##### H6d optimization investigation (2026-09-14)

The user requested looking for optimizations after the provisional benchmark.
**Investigation only: no engine/CPU source change or claimed FPS gain.** The
frozen hires2 engine remains the comparison reference. The Amiga timing skill
keeps implemented phases and complete output as constraints; no device, pixel,
input or audio work is removed to improve a measurement.

Captured two 12-second sampled-thread profiles on the same frozen Release
build: full-width hires and full-width lowres, each connected to the existing
Paula/disk RAM DMA/TOD/input fixture, 300 warmup and 8,000 total measured
fields. Placement was CPU 4 / mask 16 / Normal on the previously verified
P-core. These instrumented exploratory runs are **not throughput acceptance**;
external tests were present. Both complete with zero allocations and no
unsupported feature.

Important profiling correction: the previous summary helper selected only
stacks under `ExecuteFrame`. Here that sees only **14.18%** of hires samples
and **11.13%** of lowres samples. The fixture's timed `ConsumeInputReplayFrame`
bus reads spend much of their time waiting while the engine advances live
hardware through `AdvanceToCpuGrant`. This is real engine work, not removable
test overhead. The summary helper now accepts a root-pattern parameter;
`Program\.<Main>` includes the entire captured timed loop without changing
the workload. Old summaries retain their original scope and are not silently
relabelled. This fixture is not representative native 68000 gameplay.

Whole-loop hires profile, exclusive sampled attribution:

| Attributed method | Share |
| --- | ---: |
| `TickDevices` | 27.49% |
| `RenderHiresCck` | 24.74% |
| `AdvanceToCpuGrant` | 20.07% |
| `LightweightBitplanes.Step` | 12.28% |

These are sampled JIT-method attributions, including inlined callees—not
independent hardware cost counters. In particular the lowres renderer is
inlined into calling methods; do not compare `TickDevices` percentages between
modes as pure scheduler overhead. The old ExecuteFrame-only hires summary
attributed 37.92% to the renderer; the whole-loop result above supersedes that
percentage for prioritization. A separate JIT listing shows the current
Tier1 `RenderHiresCck` body is **2,303 bytes**, containing expanded scalar
shifter/reload work twice within its two-iteration CCK loop.

Recommended bounded experiments, in order:

1. **Reuse pair shifting in hires.** Replace each pair of scalar
   `ShiftPlayfieldPixel` calls with the existing `ShiftPlayfieldPair` at hires
   coordinates. Its no-reload path advances two packed samples at once;
   its existing scalar fallback preserves reload boundaries. Keep all four
   output pixels and both sprite clocks. This is the smallest candidate and
   should reduce duplicated hot code without new state or scheduling.
2. **Keep inactive sprites out of the large hires compositor.** Apply the
   existing lowres transparent-interval pattern to hires, using existing
   line/dirty/comparator/shifter fields. Retain exact line setup, dirty writes,
   comparator entry and one sprite advance per lowres pixel. No sprite skip
   based merely on a black framebuffer or disabled visibility.
3. **Direct wide-lowres stores.** The current wide wrapper first writes a
   narrow pair, reloads the second color, then overwrites/expands it. Consider
   direct duplicated output stores with no extra shifter advance. Preserve
   the narrow path and all window/blanking boundary behavior. Only retain if
   it improves the full-width workload without unnecessary duplicated logic.

For each implemented experiment, run the focused hires/display/sprite tests,
then the compact complete lightweight suite. Compare the same hires workload
against frozen hires2 with 300/3,600 fields and three interleaved repeats; require
exact within/cross-build fingerprints and zero allocations. Check unchanged
lowres/STOP retention separately. Report provisional results when host policy
cannot pass; do not wait indefinitely or claim a gate. Reprofile before
expanding into clock/arbitration changes. An unchanged-fingerprint native
script check should follow any retained renderer change. No new batching,
cache, diagnostic counter, event framework or CPU-core change is proposed.

Evidence in `.codex-tmp/`: `profile-h6d-hires.ps1`,
`h6d-profile-hires-20260914.nettrace` and `.speedscope.json`, matching lowres
files, `h6d-profile-hires-complete-summary-20260914.txt`,
`h6d-profile-lowres-complete-summary-20260914.txt`, and
`h6d-hires-jit-20260914.txt`. H6d and the native performance objective remain
open; **experiment 1 is the recommended next implementation**.

##### H6d optimization 1: paired hires shifting (2026-09-14)

The user authorized **"Please proceed"** after the optimization investigation.
Implemented only experiment 1 in `LightweightVideo.RenderHiresCck`: use the
existing `ShiftPlayfieldPair` for each pair of hires pixels, extracting its two
color indices. When a reload is pending, its existing scalar fallback observes
both original pixel phases. Four hires pixels and two lowres sprite clocks
per CCK remain. No new fields, cache, event, logging, bus bypass or omitted work.
No shared CPU, disk, arbitration or lowres renderer implementation was changed.

**Disposition: experimental candidate retained; implemented-model correctness
PASS, clean hires measurement complete, formal retention still OPEN.** This
does not accept H6d, resolve hardware uncertainties or authorize a switch.
That checkpoint originally held optimization 2 and disk changes. The subsequent
user request to optimize authorizes the renderer experiments recorded below;
it does not authorize a gate transition or waive unresolved disk behavior.

Validation:

- **290/290 lightweight tests PASS in both Debug and Release**, none skipped.
  Existing hires cases include all scroll-nibble settings, reload/output
  boundaries, sprite/attached priority, mode transitions and frame boundaries.
  The Release runner builds; the pre-existing NU1902 warning is unchanged.
- The native 4,800-field script retains exactly the reference cycle/CPU/
  hardware/output fingerprints:
  `682089600 / D77055BD478D353A / 88B804ABE5C7A79C / 0E39A1FC87CAAD58`.
  All **246 reference snapshot files** (82 each of BMP, Chip RAM and state
  JSON) match byte for byte against `h6d-hires-native2`. The disk-2 prompt,
  unresolved slow-mode warning and unsuccessful diagnostic exit are unchanged.
  This is not gameplay acceptance or native FPS evidence.
- JIT Tier1 `RenderHiresCck` shrank from **2,303 to 1,369 bytes (-40.56%)**.
  This is the renderer body, not a claim that all generated engine code shrank
  by that amount. The listing run is diagnostic, not a throughput sample.
- All completed performance samples retain exact same-work fingerprints,
  `unsupported=none`, real PCM and **zero measured allocations**.

Clean hires comparison: 300 warmup + 3,600 measured fields, three per build,
`C/R/R/C/C/R`, full-width hires plus Paula/disk RAM DMA/TOD/input replay.
Verified P-core group 0 / CPU 4 / mask 16 / efficiency 1 / sibling 5, Normal
priority, Balanced power plan; monitored 20-second preflight/cooldown and
policy-v2 telemetry. No provisional override. Host checks and spread pass.

| Build | Samples FPS | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Paired hires candidate | 374.22, 367.70, 373.15 | 373.15 | 1.75% |
| Frozen scalar hires2 | 369.12, 367.77, 368.96 | 368.96 | 0.37% |

Observed gain is **1.14%**, about **0.0304 ms/field** (2.680 vs 2.710 ms).
This is modest, not a large optimization claim. In particular the earlier
provisional 344.99 FPS from another host-load session is **not** the baseline
for this improvement. Correctness fingerprint is the unchanged wide-hires row
in the preceding benchmark table.

Retention attempts remain distinct:

- Initial strict lowres series: INVALID / RERUN after aggregate competing
  CPU activity exceeded 25% continuously for 11.27 seconds.
- Strict retry: INVALID / RERUN when an external build appeared mid-series.
  Neither partial series is used for a median or regression disposition.
- Following the user's previously requested diagnostic approach, a complete
  provisional lowres series ran despite external builds. Candidate samples
  **354.87, 402.29, 344.48** (median 354.87, spread **16.29%**); reference
  **393.43, 416.19, 350.47** (median 393.43, spread **16.70%**). Both exceed
  the allowed spread. These do **not** demonstrate either retention or a
  regression, even though all execution/allocation fingerprints match.
- Provisional STOP+TOD: candidate **22952.25, 23865.51, 23314.33**, median
  **23314.33**; reference **24111.18, 23061.45, 23430.57**, median **23430.57**.
  Spreads 3.92%/4.48%; ratio 99.50%, but concurrent tests mean no formal PASS.

Frozen candidate `.codex-tmp/lightweight-h6d-opt1-20260914`, reference
`lightweight-h6d-opt1-reference-20260914`. Both use runner
`0D6C02290526CE333B62C41BBF89DCE76ADE8215896509E3D53BB454B379934D`.
Candidate engine SHA-256:
`994E6DD9D11B29A777FD25D595D1847A0238614F06A5612BFB8C914F9C2790CF`;
reference engine remains `9B6F9301...9A1F0AA3` from hires2. Copper68k/CopperDisk
hashes match the preceding checkpoint. Checkout HEAD remains
`e9a1ef47444c87da626ed01a62bad4d1670e9d97` plus uncommitted work.

Evidence `.codex-tmp/h6d-opt1-*-20260914.txt`: `release-tests`, `debug-tests`,
`hires`, `lowres`, `lowres-retry`, `lowres-provisional`, `stop-provisional`,
`native`, and `jit`; snapshots in `h6d-opt1-native-20260914/`.
Next: obtain a clean retention comparison for this small candidate. If retained,
the next planned code experiment is the inactive-sprite hires path. No gate
threshold has been relaxed and the 200-FPS native workload remains unfinished.

##### H6d optimization follow-up: direct hires visibility (2026-09-14)

User request: **"Please optimize. It must be faster"**. Retained the
single-CCK hires visibility simplification in `LightweightVideo`; rejected
the separate inactive-sprite shortcut. No CPU, clock, DMA, disk, lowres
renderer, logging or production-selection implementation changes.

The two existing paired shifts still produce all four physical hires samples,
including pending reloads in blanking and borders. There is no device input
between these samples. Whole-CCK blank/border decisions now fill four pixels
directly; fully visible CCKs use two explicit sprite-composition calls. Only
CCKs straddling a window edge use the per-half boundary path. No event search,
schedule cache, skipped pixel or phase approximation was introduced.

The proposed inactive-sprite wrapper passed the then-290 tests but lost about
3% in its 300/1,200-field interleaved screen: candidate median 365.87 FPS versus
377.15 reference. It was **reverted**, not stacked into the retained candidate.
Evidence: `.codex-tmp/h6d-opt2-screen2-20260914.txt`. Its reference used the
same rebuilt runner as that candidate and the frozen opt1 engine; an earlier
attempt correctly refused differing runner hashes before launching samples.

The retained visibility candidate first screened at **392.04 versus 376.31
FPS (+4.18%)**. The longer comparison below uses 300 warmup + 3,600 measured
fields, three samples per engine in `C/R/R/C/C/R` order. Both execute the same
full-width hires fixture with Paula audio, disk RAM DMA, TOD and input replay.

| Build | Samples FPS | Median FPS | Spread | ms/field |
| --- | --- | ---: | ---: | ---: |
| Visibility candidate (opt3) | 384.18, 390.01, 385.80 | 385.80 | 1.51% | 2.5920 |
| Paired-shift reference (opt1) | 376.06, 371.12, 370.61 | 371.12 | 1.47% | 2.6945 |

Observed improvement: **3.96%, 0.1025 ms/field saved**. These are provisional
engineering measurements, **not formal gate evidence**: the diagnostic
override was selected to keep implementation moving under the user's earlier
"benchmark anyway" direction. This completed hires series nevertheless
logged no host-policy rejection and remained below the spread limit. It does
not compare against the earlier noisy 344.99 FPS or establish native gameplay
speed. The fixture CPU is mostly stopped; it is not a complete gameplay CPU
profile.

Placement was verified from topology: group 0, logical CPU 4, affinity 16,
efficiency class 1, SMT sibling 5; Normal priority, Balanced power plan
`381b4222-f694-41f0-9685-ff5bb260df2e`, monitored 20-second preflight/cooldown
and policy-v2 selected/sibling/aggregate telemetry. Mapping is host-specific.

Correctness and allocation evidence:

- **298/298 tests pass in both Release and Debug**, none skipped. Eight new
  cases distinguish physical blanking from colored borders and exercise odd
  and even display-window boundaries. Existing scroll/reload, sprite, mode
  transition and field-boundary tests remain passing. The skill's phase
  constraints guided the change; these tests preserve the implemented model,
  not verification of every unresolved OCS edge.
- All six long hires samples retain cycle `554319136`, CPU
  `E4D8C0149D9E61FD`, hardware `3FC545F94DE12D7B`, output `8A98C07297363F5D`,
  284,204 pixels, real PCM, zero measured allocation and `unsupported=none`.
- Native exploratory 4,800-field run: all **246** BMP/Chip-RAM/state snapshots
  match `h6d-hires-native2` byte for byte. Final cycle/CPU/hardware/output remain
  `682089600 / D77055BD478D353A / 88B804ABE5C7A79C / 0E39A1FC87CAAD58`.
  Disk-2 prompt and unsupported slow-disk warning remain unchanged. The run
  still ends unsuccessfully as a diagnostic; its FPS is not acceptance data.

Control series used the same 300/3,600 lengths, repeats, interleaving and
placement, also provisionally:

- Lowres active candidate **416.63, 409.49, 415.99** versus reference
  **420.68, 418.63, 417.95**; medians **415.99 / 418.63 FPS**, retention
  **99.37%**, spreads **1.72% / 0.65%**, no host-policy rejection logged.
  Cycle `554331342`, CPU `3051D82B2D7994D3`, hardware `3B959D7FDCC85DA3`,
  output `6C6B7906B62638AE` match, with zero allocation. No material lowres
  regression is indicated; this remains diagnostic rather than formal PASS.
- STOP+TOD candidate **22827.84, 25032.30, 25501.01** versus reference
  **25237.73, 25120.61, 25711.51**; medians **25032.30 / 25237.73 FPS**.
  Candidate spread **10.68%** exceeds the rule (reference 2.34%):
  **INVALID / RERUN**, not retention acceptance despite the 99.19% median
  ratio. All fingerprints and zero allocations match. The hires renderer
  is inactive in this control; no code change or additional repeated series
  was made solely to chase this very short sample's outlier.

Frozen candidate `.codex-tmp/lightweight-h6d-opt3-20260914`, reference
`lightweight-h6d-opt3-reference-20260914`. Engine SHA-256:
`CEB7A3B1EF3768E9F302D5B3359E6C66E7D4A5B77D1A2487BE78C715806805C2`;
reference `994E6DD9D11B29A777FD25D595D1847A0238614F06A5612BFB8C914F9C2790CF`.
Both runners are `0D6C02290526CE333B62C41BBF89DCE76ADE8215896509E3D53BB454B379934D`;
Copper68k/CopperDisk and checkout HEAD are unchanged from optimization 1.
Evidence: `.codex-tmp/h6d-opt3-{screen,full,native,debug-tests}-20260914.txt`,
control logs `h6d-opt3-{lowres,stop}-20260914.txt`, native snapshots
`h6d-opt3-native-20260914/`. Tests compile the engine with diagnostics enabled;
all throughput and native comparisons use the frozen lean Release runner.

**Disposition:** retain the visibility change experimentally. H6d is not GO;
native gameplay, slow-disk recovery and the 200 FPS objective remain open.
No historical G6/G7 or earlier H-gate outcomes have been relabelled.

##### H6d optimization 4: direct wide-lowres output (2026-09-14)

The user authorized implementation after the 450 FPS cost audit. The only
engine edit is in `LightweightVideo`: `RenderLowResPair` now passes final
colors to a tiny inlined writer with a constant narrow/wide choice at the
two hot call sites. Wide output writes both copies of each pixel directly;
it no longer writes a narrow pair into the framebuffer and rereads/expands
that intermediate result. The uncommon window-boundary path uses the same
writer. No duplicated device state, cache, event, logging or timeline was
introduced. No CPU, disk or scheduler source was changed.

The Amiga timing skill constrained the implementation: retain the original
pair shifting/reload logic, sprite-composition calls, border/blanking decisions
and physical CCK boundaries. Six new tests compare every wide pixel to the
corresponding narrow pixel over two fields, with odd/even windows, scroll and
manual sprite data. They passed against the old implementation before editing
the engine and pass after the edit. The full suite passes **304/304 in both
Release and Debug**, none skipped. The native exploratory 4,800-field run
also matches all **246** reference BMP/Chip-RAM/state snapshots byte for byte.
Final cycle/CPU/hardware/output remain
`682089600 / D77055BD478D353A / 88B804ABE5C7A79C / 0E39A1FC87CAAD58`.
The disk-2 prompt, slow-disk warning and unsuccessful diagnostic exit remain
unchanged. Native diagnostic FPS/allocation totals are not benchmark evidence.

Frozen opt4 candidate versus opt3, same runner and workloads. Each series
uses 300 warmup / 3,600 measured fields, three samples per side, C/R/R/C/C/R.

| Series | Candidate samples; median FPS | Reference samples; median FPS | Spread C / R |
| --- | --- | --- | --- |
| Wide lowres initial | 415.09, 408.26, 417.19; **415.09** | 380.71, 399.73, 410.62; **399.73** | 2.15% / 7.48% |
| Wide lowres confirmation | 411.74, 411.90, 405.95; **411.74** | 405.81, 401.79, 405.33; **405.33** | 1.45% / 0.99% |
| Hires control | 388.07, 393.84, 391.89; **391.89** | 391.27, 384.24, 388.06; **388.06** | 1.47% / 1.81% |
| Narrow lowres control | 394.01, 419.77, 416.93; **416.93** | 422.87, 415.29, 415.23; **415.29** | 6.18% / 1.84% |

Use the conservative confirmation result: **+1.58%, 0.0384 ms/field saved**
(2.4287 versus 2.4671 ms), not the initial +3.84% whose reference varied more.
Hires/narrow controls show no material regression; their approximately
1.0%/0.4% higher medians are not separate optimization claims. All 24 samples
retain their exact same-work cycle/CPU/hardware/output fingerprints from the
450 FPS audit, zero measured allocation, real PCM and `unsupported=none`.

All four series ran provisionally under the existing diagnostic policy;
**not formal gate evidence**. Topology confirmed group 0, CPU 4, affinity 16,
efficiency class 1, sibling 5; Normal priority, Balanced power plan
`381b4222-f694-41f0-9685-ff5bb260df2e`; 20-second monitored preflight/cooldown
and policy-v2 telemetry. No host-policy rejection was logged and all spreads
were below 10%. Brief load bursts remain in the logs; they are not evidence
of sustained contention. No build/test/profile ran concurrently with samples.

Frozen directories `.codex-tmp/lightweight-h6d-opt4-20260914` and
`lightweight-h6d-opt4-reference-20260914`. Candidate engine SHA-256
`7CF1C1E80884C18166C48FF9EDB9530DA0BF77BCCEFF94A0ED3FC41FB143903A`;
reference engine `CEB7A3B1EF3768E9F302D5B3359E6C66E7D4A5B77D1A2487BE78C715806805C2`.
Both runners `0D6C02290526CE333B62C41BBF89DCE76ADE8215896509E3D53BB454B379934D`;
CPU and media-library hashes are unchanged from the 450 FPS audit. All tests
use diagnostic engine builds; benchmarks use frozen lean Release assemblies.
Logs `.codex-tmp/h6d-opt4-{wide,wide-confirm,hires,narrow}-20260914.txt`.
Validation logs `h6d-opt4-{native,debug-tests}-20260914.txt`, native snapshots
`h6d-opt4-native-20260914/`. Release runner build succeeds; only the existing
unrelated NU1902 package warning is emitted.

Disk RAM DMA was then inspected, without editing it. Two short sampled
profiles used the frozen opt4 engine, narrow serial-input workload, 300
warmup / 8,000 total measured fields, a 12-second capture after startup,
verified CPU 4 / affinity 16 / Normal priority. These are profiling runs,
not throughput acceptance samples. Whole-runner stacks were summarized,
including work outside `ExecuteFrame`.

| Exclusive sampled attribution | RAM DMA off | RAM DMA on |
| --- | ---: | ---: |
| Clock AdvanceTo | 36.05% | 30.36% |
| TickDevices (including inlined work) | 25.74% | 30.78% |
| Bitplanes.Step | 16.38% | 13.75% |
| Blitter.Step | 7.68% | 5.84% |

The receiver and disk transfer work are not isolated as large separate
frames in these summaries. That absence does not establish inlining: the
subsequent generated-code capture below contains explicit disk calls.
Percentages from different captures cannot be subtracted to infer
absolute component cost. In particular this does not prove that arbitration
alone costs 60%, or that audio became slower. The preceding controlled cost
ladder remains the evidence for the approximate RAM-DMA increment.

Source inspection confirms compact FIFO state, one retained address/data
phase and fixed input/output positions. No further measured disk-specific
shortcut was established in this pass. A subsequent disk optimization should
inspect generated code for the per-bit receiver versus word acceptance and
completion, preserving sync observation, accepted transfers and memory
visibility. Do not replace them with batching or change unsupported slow-mode
behavior on the strength of inclusive profile percentages.

Profile evidence: `.codex-tmp/profile-h6d-disk-cost.ps1`,
`h6d-opt4-disk-{off,on}-20260914.nettrace` and `.speedscope.json`, and
`h6d-opt4-disk-{off,on}-summary-20260914.txt`.

**Disposition:** direct wide output retained experimentally. This is a modest
saving, not recovery of the entire added display cost. H6d and native gameplay
acceptance remain open; previous invalid STOP evidence is not relabelled.

##### H6d bounded optimization stop and native level replay (2026-09-14)

The authorized bounded generated-code inspection found **no substantial safe
optimization**. Keep opt4; no engine or Copper68k source changed in this pass,
and no additional FPS gain is claimed. Normal TieredPGO captures show optimized
`AdvanceTo` at 588 bytes (FullOpts), final Tier1 `AdvanceToCpuGrant` at 906,
`TickDevices` at 4,487, `DiskSerial.Step` at 814, `DiskDma.Step` at 598,
`ReceiveBit` at 336 and `NextInputAfter` at 102 bytes. Instrumented Tier0 and
OSR bodies are not substituted for these figures.

The ordinary receiver path has a few state checks and returns before word
acceptance; DMACON/ADKCON loads are not unconditional per bit. The fixed disk
phase calculation already lowers modulus 454 to multiply/shifts and three
fixed comparisons, not division or an event search. The clock remains one
compact canonical CCK loop. `TickDevices` explicitly calls serial and RAM-DMA
steps in this capture, and the receiver also has a call site: missing disk
entries in the earlier top sampled stacks are **not evidence of inlining**.
Do not pursue speculative batching or a small cold-path split without measured
benefit. Evidence: `.codex-tmp/h6d-bounded-disk-loop-jit-20260914.txt` and
`h6d-bounded-disk-phases-jit-20260914.txt`. Their instrumented, unpinned run FPS
is not throughput evidence.

Native continuation required only a host-runner extension. `NativeInputScript`
now preloads scripted ADF images and applies mount/eject actions at field
boundaries. Relative media paths resolve against the script directory. ADF
encoding at mount still allocates, so the runner rejects media changes within
the measured interval unless this is an explicit boot probe. No old-engine
fallback, hardware phase change, warning suppression or production switch was
introduced. The timing skill's phase constraints kept this change outside
emulated hardware execution.

Validation: **308/308 tests pass in both Debug and Release**, none skipped;
Release runner builds. Four added cases cover replay, preloading, measured
interval boundaries and malformed actions. An actual CLI invocation confirms
that a timed media swap without `--boot-probe` fails with the intended error.
The existing unrelated NU1902 warning remains. The original exploratory script
is unchanged, and all **246** pre-swap reference snapshots still match.

New scripts under `CopperMod.Amiga.Lightweight.Runner/Workloads` are diagnostic
reproducers, not accepted benchmark fixtures:

| Script | Result | SHA-256 |
| --- | --- | --- |
| `lemmings-native-disk2-exploratory.json` | Eject 4800, mount disk 2 at 4860, click 4920; readable menu by 6000 | `2EC62826631E45511B97188B859958082084C1FED951B5683E82FC37FEC1DC38` |
| `lemmings-native-gameplay-exploratory.json` | F1 at 8000 does not leave menu; failed exploratory input, not a diagnosed keyboard bug | `1F2C7BB30E4913EB642F804CB670FDE77BE723B728FC87BDCD023CA1DFCE7E0D` |
| `lemmings-native-menu-mouse-exploratory.json` | Mouse positioning/click enters Level 1 “Just dig!” briefing | `2750BC5C306EF74C9CD46EDD3E78A11C4039FAD79B6DE90D2904BB7FC3DE9F2D` |
| `lemmings-native-level1-exploratory.json` | Additional click 10000; running level and nonzero PCM, corrupted display | `B2CFB175BAE3DFE6609DD6A3E8C3B320EC124F996913594ED48D85F2952B5C09` |

Inputs are native `D:/TestData/ROM/Kickstart_13.rom` and the user's SR-cracked
Lemmings disk ZIPs under `D:/TestData/TestImages`. The scripts' absolute disk-2
path is host-specific, not a portable default. Input SHA-256:

- ROM: `EE05862D8102A08436AC4056DA7D549DB31625C7D47B24DFB7B3C9A5C113CA53`.
- Disk 1 ZIP: `5FC56B436722688BA37836E273A3A1C84D724235A91B39272768FF3E4CAC0A59`.
- Disk 2 ZIP: `0C1CB9C49CBC29B56D2F974A90C510752E256148F8E8CC10CFBD026DB73DEA1A`.

Frozen runner `.codex-tmp/lightweight-h6d-native-swap-20260914` has SHA-256
`38B6EADFA11C934099623CF7E14E86C294688F9384CDD33E4224EBC233AD803A`;
engine remains opt4 `7CF1C1E80884C18166C48FF9EDB9530DA0BF77BCCEFF94A0ED3FC41FB143903A`.
CPU/media-library hashes and HEAD remain unchanged. The reference bridge
`lightweight-h6d-native-swap-opt3-20260914` uses this same new runner and the
previous opt3 engine `CEB7A3B1EF3768E9F302D5B3359E6C66E7D4A5B77D1A2487BE78C715806805C2`.
Original frozen directories are unchanged; future comparisons must account
for the new runner identity.

The 12,000-field level replay first records nonzero gameplay audio at sampled
field 10,260. Screenshots 10,980 and 12,000 show a terrain strip near the top
and a repeated/corrupted control panel down the raster; the timer progresses
from 4:42 to 4:21. This proves execution/input progress and audio activity,
**not usable or correct gameplay**. End cycle/CPU/hardware/output are
`1705224006 / BB553602CC7FD9D2 / 5B1CFE24E43F2BFE / E730A25D52677276`.
Both opt3 and opt4 produce identical results: **all 606 BMP, Chip-RAM and
state-JSON files match byte for byte**. The visual symptom therefore predates
opt4's direct wide-lowres writer; this does not clear every earlier change.

Logs and snapshot directories under `.codex-tmp`:
`h6d-native-disk2-20260914`, `h6d-native-one-player-20260914`,
`h6d-native-menu-mouse-20260914`, `h6d-native-level1-20260914` and
`h6d-native-level1-opt3-20260914` (logs add `.txt`). Every native run explicitly
uses `--boot-probe --probe-continue-unsupported` and ends unsuccessfully because
LWA-DISK-009 remains reported. Snapshot overhead, incomplete supported behavior
and corrupted output exclude these FPS/allocation totals from acceptance.

**Disposition:** bounded micro-optimization pass closed; opt4 retained without
another speed claim. Record confirmed visual symptom **LWA-VIDEO-003**, root
cause unknown. Next investigate the first incorrect scanline against register,
fetch, pointer/modulo and output ordering, checking source RAM as needed.
Frame-end BPLCON0=C200 alone does not establish the mode throughout the frame.
Keep LWA-DISK-009 independently open; loading later screens does not verify
receiver transitions. H6d is **not GO**, no production cutover is authorized,
and complete-workload 200 FPS remains unfinished. Historical gates are unchanged.

##### H6d native display repair: Copper VP7 comparison (2026-09-14)

**LWA-VIDEO-003 is resolved for the reported repeated-panel/short-terrain
symptom.** The fault was in Copper WAIT/SKIP comparison, not pixel generation.
The selected profile remains A500 PAL OCS Agnus/Denise, 68000, 512 KiB Chip RAM
and 512 KiB slow RAM, native Kickstart 1.3, read-only standard ADFs. No CPU,
renderer, disk, shared Legacy execution or production-selection code changed.

The saved field-12,000 Chip RAM contains this causal display sequence:

| Copper address | Words | Intended action |
| --- | --- | --- |
| `0084F0` | `0100 4200` | Four-plane lowres terrain |
| `0085CC` | `CC01 FF00` | WAIT for vertical position 204 |
| `0085D0` | `0100 C200` | Switch to four-plane hires panel |
| `0085D4` onward | DDF, modulo and BPL pointer MOVEs | Install panel fetch/layout state |
| `008624` | `DC01 FF00` | Panel palette change at line 220 |
| `008668` | `F401 FF00` | Wait for line 244 before Copper interrupt |

`ComparisonSatisfied` used `secondWord & 0x7FFE`, removing VP7 from both beam
and target. Thus `CC01/FF00` became a line-76 comparison: the panel state was
installed **128 scanlines early**, and subsequent waits/palette/interrupts were
also affected. This explains the terrain cut-off and panel data being fetched
over too many rows. The new mask is `0x8000 | (secondWord & 0x7FFE)`.
The separate BFD/live-blitter condition, input/output phases, clock, lower mask
bits and masked target comparison are unchanged. No additional state, cache,
logging, allocation or renderer branch was added.

Primary expectation: Commodore's [Hardware Reference Manual, chapter 2,
comparison-enable discussion](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_2.html)
states that the vertical high bit cannot be masked in WAIT or SKIP; IR2 bit 15
controls BFD instead. Its following loop example also requires an observed
VP7=1 to make a lower masked target already satisfied. The timing skill guided
the primary-source check and test-before-fix workflow; native appearance is
corroboration, not the source of the hardware rule.

Ten added OCS PAL synthetic cases cover WAIT at lines 128/204, both BFD forms,
zero lower compare enables, SKIP before/at the upper-half target, and the
observed-high-bit/lower-target case. **Eight fail before the engine edit**;
the two already-satisfied upper-half SKIP controls pass. After the one-line
fix, **318/318 pass in both Release and Debug**, none skipped, including the
existing live BFD/final-blitter-drain test. No shared Copper68k/Legacy code
changed, so validation uses the independent engine suite, not the historical
full-emulator corpus. Runner Release build succeeds with only the existing
NU1902 dependency warning. Logs under `.codex-tmp`:
`h6d-copper-vp7-{before,release,debug,build}-20260914.txt`.

Frozen lean Release build `.codex-tmp/lightweight-h6d-copper-vp7-20260914`:

- Engine SHA-256: `ADD706E08DB13FA58DD6088F4C25B98045CEB8CE76210B1355F2C7D037BE6FDE`.
- Runner unchanged: `38B6EADFA11C934099623CF7E14E86C294688F9384CDD33E4224EBC233AD803A`.
- Copper68k unchanged: `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.
- CopperDisk unchanged: `8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227`.

The unchanged `lemmings-native-level1-exploratory.json` and native ROM/media
hashes above reproduce menu, briefing and active level through 12,000 fields.
Screenshots now show full terrain, individual lemmings and a single panel at
the bottom. Timer advances from 4:42 at 10,980 to 4:21 at 12,000; PCM remains
nonzero and `videoUnsupported` is null. These observations resolve this visual
symptom, not every display boundary or complete game/input compatibility.
Final cycle/CPU/hardware/output:
`1705224016 / 689153DB37EEF0F4 / 0DFAA476C5271DA3 / D82BC9CF82366CD9`.
At the endpoint BPL1..4 are `34B00/35780/36400/37080`, rather than the old
`37300/37F80/38C00/39880`; 0x2800 fewer bytes per plane corresponds to the
removed 128 extra rows of 80-byte hires fetching. Endpoint PC=1972, SR=2104,
DMACON=07FF, INTENA=4018, INTREQ=17E2, BPLCON0=C200 and disk DMA inactive.

Evidence directories/logs: `.codex-tmp/h6d-native-level1-vp7-20260914/` and
`h6d-native-level1-vp7-repeat-20260914/`, each with matching `.txt` log.
The independent repeat matches **all 606 BMP/Chip-RAM/state files byte for
byte** and the complete endpoint fingerprints above. The runner's exit code
remains 2 for the known unsupported disk feature; this is not a successful
acceptance run. Before/after all 202
snapshots per type were compared: 59 BMPs differ (first sampled difference
8,460), 133 Chip-RAM dumps differ (first 4,080), and 130 state JSONs differ
(first 420). The earlier sampled state difference is not claimed to be the
first physical event divergence. Corrected WAIT/SKIP/interrupt timing changes
native execution, so old fingerprints are deliberately not equality gates.

**Disposition:** retain the targeted repair. No new performance result is
claimed: boot-probe FPS includes snapshots and unsupported disk behavior.
LWA-DISK-009, unresolved hires transition edges and H6d acceptance remain open;
neither production cutover nor final 200 FPS completion is authorized.

##### H6d experimental slow-mode reception and playable input (2026-09-14)

The user authorized continuation without physical Amiga hardware (interpreting
the message's unavailable “he” as hardware). This pass distinguishes implemented
behavior from hardware verification. **LWA-DISK-009's missing slow input now
executes under a declared ideal-ADF model; LWA-DISK-010 retains the unresolved
physical rate-switch/PLL questions.** No physical-hardware result is claimed.

The timing skill required a primary-evidence check and a bounded model instead
of deriving a hardware rule from Lemmings. The HRM ADKCON table specifies rate
selection; [Commodore's US4780844A patent](https://patents.google.com/patent/US4780844A/en)
describes pulse inspection windows, completion-driven shifting and separate
phase/frequency corrections. This supports separating recovery from spindle
motion, but does not verify our reduced transition rule. Supporting reference:
[WinUAE disk.cpp](https://github.com/tonioni/WinUAE/blob/62802fab227ceceed3e5d7020818046bc80b76ce/disk.cpp),
`nextbit/getonebit`, source commit `62802fab227ceceed3e5d7020818046bc80b76ce`.
This is reference-implementation evidence, not a physical capture.

Before implementation, a temporary diagnostic-only `LIGHTWEIGHT_DISK_TRANSITION_PROBE`
build recorded mode writes (at most 24 events). The probe was removed from
source afterward; no release logging/callback remains. Evidence:
`.codex-tmp/h6d-disk-transition-probe-20260914.txt` and the matching snapshot
directory, build log `h6d-disk-transition-probe-build-20260914.txt`.
The initial FAST clear is at cycle 89,695,016, PC 7008E, deselected; FAST is
restored at 96,698,112, PC 702DA, selected. Later selected-drive transitions
last **16 CPU cycles**, e.g. 102,202,088 to 102,202,104 (PC 702D4/702DA),
with one source cell arriving at 102,202,100. DMA is inactive and DSKLEN=4000.
These are real register transitions to model, not a reason to suppress input
because DMA is off. Source/media profiles remain A500 PAL OCS/68000/512+512 KiB
and read-only standard ADF; no production routing or shared CPU code changed.

`LightweightDiskSerial` adds two fixed fields and a small slow-path method:

- FAST input without a pending window uses the unchanged source-cell path.
- Slow input consumes two arrived source cells; if the second is a pulse,
  consume one settling cell before publishing a recovered one. Otherwise
  publish the first cell's pulse value after two arrivals. No future cell is
  inspected. Every physical source cell still advances at the original
  rational 300-RPM schedule, including index and deselected rotation.
- An accepted slow window completes across FAST writes and line/frame wraps;
  partial receiver byte/word state is retained. This is a chosen experimental
  boundary, **not a verified Paula mode-write phase**. A new spindle/media run
  clears an incomplete window; reset also clears receiver byte state.
- Completed recovered bits enter the existing sync, byte-ready, interrupt and
  RAM-DMA paths. MSBSYNC remains unsupported; no true GCR/flux/PLL support is
  claimed. No device timeline, cache, logging or steady-state allocation added.

**330/330 tests pass in Release and Debug**, none skipped. Twelve additional
cases cover causal early/late/no-pulse windows, FAST changes, partial bytes,
DMA-independent sync, word transfer, reset, line/frame crossings and slow-mode
allocation/index behavior. The final cases were also run with the frozen old
engine: **12 fail / 20 pass** in the disk-serial class. One initially uninitialized
DMA-test destination was corrected before that final baseline run. These new
cases check the declared receiver model, not unavailable hardware phases.
Logs: `.codex-tmp/h6d-slow-window-{before,focused,release,debug,build}-20260914.txt`
and `h6d-slow-window-baseline-final-tests-20260914.txt`. Only the existing
NU1902 package warning remains. No shared engine/CPU changes require the old
full-emulator corpus; the independent focused suite is the applicable scope.

Frozen lean Release `.codex-tmp/lightweight-h6d-slow-window-20260914`:

- Engine: `B63513DF87360DB4A714F1AEF2A1A219DC915DCB5421B7D890124ADC97F01893`.
- Runner: `CF3130EBC617F617D02183CFB18413AEB8EDC2980735C73A1C0865343426FF5E`.
- CopperDisk: `2A684F2261D1FB223FD84E72F71D90B4D3EED3B152A73A0C27285B8C64C89273`.
- Copper68k: `5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497`.

Runner/media-library source was not edited in this pass, but their rebuilt
binary identities differ and are recorded rather than assumed equal. The
FAST control bridge `lightweight-h6d-slow-window-reference-20260914` uses these
same runner/libraries and the frozen preceding VP7 engine
`ADD706E08DB13FA58DD6088F4C25B98045CEB8CE76210B1355F2C7D037BE6FDE`.
HEAD remains `e9a1ef47444c87da626ed01a62bad4d1670e9d97` plus the existing dirty tree.

The unchanged level-1 script completes **12,000 fields, exit 0, unsupported=none**,
without `--probe-continue-unsupported`. All **201 matching BMPs and 201 Chip-RAM
dumps** equal the preceding display-fixed run. The extra old first-warning
snapshot no longer occurs; JSON's unsupported field deliberately changes.
End cycle/CPU/hardware/output remain
`1705224016 / 689153DB37EEF0F4 / 0DFAA476C5271DA3 / D82BC9CF82366CD9`.
Evidence: `.codex-tmp/h6d-native-slow-window-20260914/` and `.txt`.

New `Workloads/lemmings-native-digger-exploratory.json` retains the boot/media
sequence, selects the digger at field 10,566 and assigns it at 10,620. By 12,000
the terrain contains the excavated shaft, the skill count falls from 10 to 9,
and the panel shows **80% saved / two lemmings still out**. Native input affects
game logic, blitter terrain and level progress; it is no longer animation-only
evidence. Exit 0, unsupported=none, active PCM; endpoint
`1705224016 / A62A6A6D1BC4ACC7 / 0DFAA476C5271DA3 / 2F16E51E96719F69`.
Evidence: `.codex-tmp/h6d-native-digger-20260914/` and `.txt`.
Script SHA-256: `C176AD48B91471C9C84101011C02DD21905013362991E03BF53457D0192AB2CD`.
These boot probes still include snapshots and are not throughput measurements.
The digger replay is separate from the forthcoming measured interval so a
quickly completed level cannot replace active gameplay with a static result screen.

**Disposition:** experimental receiver implementation and native gameplay
interaction established. No hardware-phase closure, H6d GO, Legacy comparison,
production switch or final 200 FPS completion is implied. Preliminary native
measurement follows separately; unresolved edges remain in LWA-DISK-010.

##### H6d preliminary native gameplay throughput (2026-09-14)

**Native-only preliminary median: 271.05 FPS / 3.689 ms per complete field.**
This is the active native-ROM game, not the earlier synthetic 400–450 FPS
fixture. It exceeds 200 FPS in these samples but is **not final target/gate
acceptance**: no interleaved Legacy comparison was performed and the declared
receiver/other hardware uncertainties remain open. Do not turn preliminary
throughput success into hardware correctness or a production-switch decision.

The frozen slow-window candidate above uses the unchanged level-1 script
(`B2CFB175BAE3DFE6609DD6A3E8C3B320EC124F996913594ED48D85F2952B5C09`), native
ROM and both disk hashes already recorded. Gameplay checkpoint is field 10,320;
execute 600 additional gameplay warmup fields, then measure **10,921–14,520**
(3,600 complete fields). CLI `--warmup 10920` includes the untimed native boot
and media swap as well as those final 600 gameplay fields. There are no media
changes, snapshots or unsupported-feature bypasses in the timed interval.
Each field generates the full 908×313 framebuffer and real 48 kHz stereo PCM;
the existing small output checksum consumes pixels/audio every field. Host
presentation, audio-device pacing and wall-clock throttling are absent.

| Sample order | FPS | Measured allocation | Outcome |
| --- | ---: | ---: | --- |
| N1 | 271.05 | 0 bytes | Complete, unsupported=none |
| N2 | 276.95 | 0 bytes | Complete, unsupported=none |
| N3 | 270.37 | 0 bytes | Complete, unsupported=none |

Spread `(max-min)/median` is **2.43%**. All three share cycle
`2063321040`, CPU `25B78C17DE5341FF`, hardware `886654CADB84C05F`, output
`D9AED52B4567F91B`, 284,204 pixels and 1,922 PCM samples at the endpoint.
Fingerprint matching is within this engine, not a cross-engine hardware oracle.

Fresh Windows CPU-set mapping confirms group 0, logical CPU **4**, efficiency
class **1**, physical-core index 4, SMT sibling **5**, affinity **16**; Normal
priority throughout each process. Efficiency classes 1/0 map to P/E groups
in the recorded topology. Balanced power plan
`381b4222-f694-41f0-9685-ff5bb260df2e`. These are host-specific observations,
not reusable logical-CPU policy. Ten-second preflight and inter-sample cooldowns
use host-load policy v2; selected-core/sibling/aggregate accounting subtracts
the pinned process. No competing workload or sustained-load rejection occurred;
host/spread checks are valid. Normal background apps were not excluded by name.

Before native N1–N3, a bounded FAST-only screening pair ran reference then
candidate, each 300 warmup / 3,600 measured fields, with synthetic Paula/disk
RAM DMA/TOD/input/full-width lowres all active. **409.77 / 412.42 FPS**,
identical cycle/CPU/hardware/output fingerprints, zero allocation and no
unsupported behavior. This single pair does not establish a precise gain or
replace a formal repeated retention series; it shows no obvious FAST-path
regression. The reference bridge uses the identical new runner/media libraries
with the preceding VP7 engine, not Legacy.

Evidence `.codex-tmp/measure-h6d-native-slow-window.ps1`, main log
`h6d-native-slow-window-measurement-20260914.txt`, and ordered host stream
`h6d-native-slow-window-host-telemetry-20260914.txt` (222 intervals). PowerShell
emitted the information-stream telemetry separately during this run; the
complete captured stream was saved alongside the main results. The wrapper
now routes that stream into its main log on future runs; sampling logic and
limits are unchanged. Native image/input validation is outside timing, using
the separate boot-probe paths recorded here, not the benchmark process.

After all measurements, a separate untimed boot-probe replay through field
14,520 validates the same endpoint cycle/CPU/hardware fingerprints. It shows
the running level, ten lemmings still out and timer 3:31, not a result screen.
All **61 sampled fields from 10,920 through 14,520 have distinct BMPs,
nonzero PCM and no unsupported feature**. The 10,320 checkpoint also has active
gameplay and nonzero audio. Evidence:
`.codex-tmp/h6d-native-measurement-validation-20260914/` and `.txt`, exit 0.
Its cumulative output checksum spans the entire boot, so it is not compared
to the benchmark's 3,600-field checksum. Its snapshot-including FPS is ignored.

**Remaining acceptance work:** review the declared experimental receiver scope,
then complete the required
matched Legacy/new-engine comparison. The later readiness review below completes
the first Legacy script preflight, not that formal comparison. No historical G6/G7 outcome is relabeled.
The initial engineering throughput target is exceeded here, but the overall
200 FPS objective and H6d are not declared complete by this unpaired series.

##### H6d native-gameplay CPU profile (2026-09-14)

Two post-warmup profiles of the same 3,600-field native interval identify
**CPU interpretation as the largest current cost**: 55.01–60.85% exclusive
managed samples in Copper68k, plus 5.91–7.07% machine scalar CPU stepping.
Display/bitplanes/sprites own 10.52–11.98%; clock methods 6.45–8.12%; the
device coordinator 5.60–5.80%, including inlined work. Inclusive CPU stacks
also contain chipset execution and must not be treated as pure CPU cost.

The wide synthetic control has a STOPped 68000, as did the earlier roughly
450 FPS fixture; it is not representative native-game CPU work. Both native
captures and the synthetic control preserve their uninstrumented fingerprints,
zero allocation and no unsupported features. Engine/CPU binaries are unchanged.

Prioritize instruction-entry/dispatch bookkeeping, prefetch/branch value
copies and the observed generic unary/EA path before another DMA redesign.
These are investigation candidates, not implemented or proven speedups.
Full evidence, caveats and reproduction identities:
[native gameplay profile](LIGHTWEIGHT_A500_NATIVE_GAMEPLAY_PROFILE.md).
Profiled rates are diagnostic only; the prior 271.05 FPS preliminary median
and every gate disposition remain unchanged.

##### H6d retained CPU byte-test optimization (2026-09-14)

**306.41 FPS native-gameplay median versus a freshly rerun 269.63 FPS
reference: +13.64%, saving 0.445 ms per complete field.** Three samples per
build in R/C/C/R/R/C order use the same 600-gameplay-warmup / 3,600-measured
interval, native ROM/disks/input script, full pixels and real stereo PCM.
Reference samples 266.89/269.63/272.51; candidate 299.70/306.41/312.40 FPS.
Spreads 2.08%/4.14%; verified P-core/Normal and host-policy-v2 checks pass.
All six runs preserve cycle/CPU/hardware/output fingerprints and allocate zero
bytes. This is a same-work CPU optimization comparison, not Legacy acceptance.

Instruction counts showed a byte-test/conditional-branch polling loop accounts
for 77.3% of retired instructions. The retained change directly executes the
eight general `TST.B d16(An)` encodings through existing planned dispatch,
removing generic decode/EA construction. Every fetch, operand read, timing
update and branch remains; the opcode is excluded from loop batching and the
JIT microsequence mapping. No title/address special case or new cache exists.
Two preceding state-layout/metadata-store experiments showed no gain and were
reverted. No release logging or tests were removed to obtain this improvement.

Lightweight Release 330/330 passes; broader Amiga CPU tests remain 1,116
passed/7 skipped. The full Copper68k suite has six existing descriptor-label
failures, reproduced against the frozen old CPU; no new failure was observed.
The focused dispatch/phase checks and full evidence are in the
[native gameplay profile and optimization follow-up](LIGHTWEIGHT_A500_NATIVE_GAMEPLAY_PROFILE.md).
Candidate `.codex-tmp/lightweight-native-cpu-tst-20260914`, Copper68k SHA-256
`EA0BE0E1089F980E3FD9DF652B53CAAA3A4A9AB0D6865925A60EF93955747332`;
engine, runner and media libraries match the frozen slow-window family.
H6d hardware uncertainties, matched Legacy acceptance, G6/G7 history and
production selection are unchanged. The 200 FPS objective is not relabeled
complete by this isolated optimization comparison.

##### H6d retained prefetch compiler-policy optimization (2026-09-14)

**347.58 FPS native-gameplay median versus today's 310.03 FPS byte-test
reference: +12.11%, saving 0.348 ms per complete field (3.225 to 2.877 ms).**
Three samples per build in R/C/C/R/R/C order retain the same native workload,
600 gameplay warmup / 3,600 measured fields, complete pixels and real stereo
PCM. Reference 304.48/310.03/311.44 FPS; candidate 345.73/347.58/351.64 FPS;
spreads 2.24%/1.70%. Verified P-core LP4/mask16/sibling5, Normal priority,
Balanced power plan and full host-policy-v2 checks pass. All six fingerprints
match, with zero measured allocations and no unsupported features.

Two updated profiles of the byte-test CPU confirm interpretation remains the
largest sampled cost (51.28–56.89% exclusive). The retained change deletes only
the forced-inlining attribute from the full `TopUpPrefetchOne` overload.
The JIT chooses its call-site policy; every emulated operation is unchanged.
No prefetch implementation, signature, bus phase, pending state, interrupt
ordering, logging or test infrastructure was removed. The synthetic generated-
code check does **not** show a smaller branch helper, so the exact native
hot-caller/code-layout mechanism remains unisolated; the repeat gameplay
comparison, not a code-size claim, supports retention.

Focused CPU tests pass 91/91 in Release and Debug; Lightweight Release
330/330; broader Amiga CPU Release 1,116 passed/7 skipped. Full CPU Release
remains 1,476 passed/6 existing descriptor-label failures/6 skipped, with no
new failure observed. The normal Release runner rebuild succeeds.

Frozen candidate `.codex-tmp/lightweight-branch-inline-20260914`, CPU SHA-256
`F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.
Runner/engine/media DLLs are unchanged from the preceding byte-test family.
Detailed profiles, sample identities and test evidence are in the
[native gameplay profile](LIGHTWEIGHT_A500_NATIVE_GAMEPLAY_PROFILE.md).

This completes the bounded optimization follow-up. **Next: return to H6d
readiness, review the declared experimental receiver scope, then the required
matched Legacy/new-engine complete-workload comparison.** H6d is not newly
declared GO; the 200 FPS objective, production selection, unresolved hardware
edges and historical G6/G7 dispositions are not relabeled by this result.

##### H6d readiness review and Legacy replay preflight (2026-09-14)

The user authorized proceeding after the retained 347.58 FPS optimization.
No hardware/CPU implementation or production selection changes in this pass.
The opening status and frozen-workload description are reconciled with the
later native-gameplay evidence; the former "Current defect register" snapshot
is explicitly historical where it still says disk RAM DMA, input or hires is
absent. The maintained issue register remains authoritative for open edges.

**Receiver scope review:** LWA-DISK-009's omitted slow input is implemented
under the declared causal ideal-ADF window model. LWA-DISK-010 remains OPEN
for actual PLL/history/mode-write phase. The implementation retains accepted
windows, no future-cell reads, nominal spindle/index timing and byte/word
state. Existing focused model checks and native input evidence support further
experimental work; they do not establish physical PLL correctness. No new
contradicting evidence was found in this review and no unverified edge is
silently closed. Lack of physical hardware is not made a new blanket blocker.

**Legacy workload preparation:** the historical CopperScreen benchmark cannot
be used unchanged: it has no shared JSON gameplay replay and renders internally
at 44.1 kHz before output conversion. A separate opt-in
`--native-gameplay-replay` mode now exists in `CopperScreen.Benchmarks`.
It directly uses the existing Legacy Machine/native ROM boot and renderer,
without starting CopperScreen, GPU/audio-device pacing, or CopperStart boot.
It configures PAL OCS/accurate 68000/512 KiB Chip + 512 KiB slow RAM, no RTC,
one read-only DF0, Legacy arbitration and normal existing deferred CPU defaults.
ROM, media and the input script are the identical supplied files; swaps and
relative mouse/button levels apply before the same indexed frame. Keyboard and
joystick script entries currently fail explicitly rather than being omitted.

The preflight generates stereo directly from Paula on a continuous rational
48 kHz grid, preserves audio phase across fields, produces Legacy's native
716x285 field presentation, and saves bounded checkpoint BMP/Chip-RAM evidence.
It rejects incomplete CPU fields and unhandled field-geometry changes.
It has **no FPS/PASS acceptance mode**. File I/O, snapshots and per-checkpoint
logging belong only to this external preflight, not either engine hot path.
Nine guard tests pass; the Release benchmark build succeeds. The unchanged
package warning and existing nested-build git-path warning remain visible.

Native output geometry differs from Lightweight's 908x313 complete raster.
This is a disclosure and workload-accounting requirement, **not a new demand
to rewrite Legacy's renderer or force cross-engine pixels to match**. A formal
comparison must preserve each engine's complete native output and clearly
record the smaller Legacy field output; it must not crop Lightweight or claim
equal pixel-generation work. Audio algorithms also differ (Legacy instantaneous
digital sampling versus Lightweight sample-area averaging); both must generate
real 48 kHz stereo throughout the same emulated interval. The relative speed
comparison must not be advertised as equal output algorithms or hardware parity.

The Legacy reference prepared here is a newly frozen replay build from the
current dirty tree, **not a retroactively invented pre-project Stage-1 build**.
The missing original matched-gameplay baseline remains a provenance limitation.
An explicit matched reference series is still needed before the 2x threshold
or final 200 FPS acceptance can be assessed. Existing synthetic/old-G6/G7 rates
must not be substituted for that reference.

Reproduction from the repository root:

```text
dotnet CopperScreen.Benchmarks/bin/Release/net10.0/CopperScreen.Benchmarks.dll --native-gameplay-replay --rom D:/TestData/ROM/Kickstart_13.rom --adf "D:/TestData/TestImages/Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip" --input-script CopperMod.Amiga.Lightweight.Runner/Workloads/lemmings-native-level1-exploratory.json --frames 14520 --output <new-evidence-directory>
```

Frozen preflight `.codex-tmp/h6d-legacy-replay-frozen-20260914`:

- Runner `0DABA47A35202383F06F87AA01FEA274E021111E9BECDA525F24DAC619F08299`.
- Legacy engine `16E69A18DC19CE7FEB29C401DA55EAA67753D108429E73864274E9808A0D9787`.
- Emulator host `2EA4A4530FE97F14E363A20200E8C293E2C049D8DA674B92D2D4CCC4294CD9DC`.
- Shared optimized CPU `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.
- Replay source `537573FE5DE88BCEE4FE55419B9132508E11765FE9AD28D2577A394B2AD925E8`.
- Script `B2CFB175BAE3DFE6609DD6A3E8C3B320EC124F996913594ED48D85F2952B5C09`.

Evidence: `.codex-tmp/h6d-legacy-replay-20260914.txt`,
`.codex-tmp/h6d-legacy-replay-evidence-20260914/`,
`.codex-tmp/h6d-legacy-replay-build-final-20260914.txt` and
`.codex-tmp/h6d-legacy-replay-tests-20260914.txt`.

**Completed replay:** exit 0 after **14,520 fields**. The unmodified script
passes the crack intro, game introduction, explicit disk swap, one-player menu
selection and briefing. At 10,320 the image visibly shows level-one lemmings,
two out, and timer 4:55; audio is nonzero. Gameplay persists at 14,520 rather
than ending on a result screen. All **eight sampled checkpoints from 10,920
through 14,520 have distinct images and nonzero audio**. This verifies those
checkpoints, not a claim to have inspected every intervening frame or audio
sample. The sampler generated audio throughout the entire run.

Endpoint: cycle `2063321040` (the same complete-field endpoint as Lightweight),
PC `000049AE`, DMACON `07FF`, BPLCON0 `C200`, DF0 cylinder 65;
last field 961 stereo audio frames; framebuffer SHA-256
`3279E4FABA6BE1CD891F25675B32E3A818A9ACFA4C1CBC6546AF9569C8A494DB`.
Twenty-seven bounded image/Chip-RAM checkpoints are retained. Menu/panel
rendering differences are visible in Legacy's captures, including a corrupt
final status-panel raster. Their origin (Legacy execution versus this new
replay/presentation path) has not been isolated. They are compatibility signals,
not evidence of a new Lightweight regression or verified hardware behavior;
the comparison must disclose them and not demand pixel equality.

**Disposition:** first shared-script native replay preflight complete;
**H6d remains OPEN / not GO**, formal throughput and repeated deterministic
Legacy fingerprints remain pending. This diagnostic was not pinned or supplied
with formal host telemetry; the small harness guard suite ran while it was
active. No FPS is reported or accepted, and no earlier valid performance
disposition is replaced. The 347.58 FPS retained Lightweight optimization and
all engine/CPU source are unchanged in this pass.

**Next bounded step:** prepare the non-diagnostic paired measurement mode using
this exact script and native configuration, reusable complete native outputs,
48 kHz stereo and checksums. Run 600 gameplay warmup + 3,600 measured fields,
three samples per engine interleaved, with verified P-core/Normal and full
policy-v2 telemetry. Freeze and record both builds before the series; verify
within-engine fingerprints and progress separately. Do not add new compatibility
work or close the uncertain receiver phases merely to conduct that comparison.

##### H6d paired measurement implementation (2026-09-14)

The user authorized the formal paired comparison after the Legacy replay
preflight. `NativeGameplayReplay` now has a separate
`--native-gameplay-measure` mode, fixed to 10,920 boot/warmup fields and 3,600
measured fields. The first 10,320 establish gameplay, followed by the required
600 gameplay warmup. It executes the same frame/audio/ROM path as the verified
preflight. Both engine source trees and the shared CPU are unchanged.

Changes are host-only: a reusable Legacy stereo buffer, 257 pixel / 61 audio
checksum samples per measured field (the same bounded counts as Lightweight),
allocation/time reporting, and start/end images written strictly outside the
timed interval. Snapshots, input logging, full RAM/image hashes and file I/O
are excluded from timing. Media changes in the measured interval are rejected.
Every frame still generates native pixels and all 48 kHz stereo samples;
checksumming fewer values does not skip production of the remaining output.
The endpoint includes CPU, RAM and image fingerprints, total audio frames and
nonzero-audio-field counts. Legacy lacks the new engine's unsupported-feature
reporter, so its result honestly says `unsupported=not-instrumented`, not `none`.
The preflight's native gameplay is its workload-coverage evidence. Existing
Legacy rendering differences remain disclosed, not patched by this harness.

Twelve Release guard tests pass. The benchmark Release build passes with the
existing package warning. The external wrapper's syntax check passes.
`scripts/run-lightweight-native-paired.ps1` reuses policy-v2 accounting, adds
recognition of competing Lightweight runner processes, checks actual CPU-set
topology/SMT relationships and Normal affinity throughout, verifies the frozen
input hashes, and runs R1/C1/C2/R2/R3/C3 with 10-second monitored cooldowns.
It requires each engine's fingerprints to repeat, not cross-engine equality.
The first Legacy result must reproduce the earlier preflight's start/end
image hashes before the series continues. Legacy allocation is reported;
Lightweight must preserve its zero-allocation validated fingerprint.

Series host: Intel Core i9-12950HX, freshly verified P-core LP4/mask16,
sibling5, efficiency class1 (E cores class0), Normal priority, Balanced power
plan. Full mapping, OS/runtime, power GUID, sample order, input and frozen
assembly hashes are logged externally. Placement is not isolation. Normal
desktop apps remain allowed; no temperature/effective-clock claim is made.

Frozen Legacy measurement runner:
`.codex-tmp/h6d-legacy-measure-frozen-20260914`, runner SHA-256
`B0FAE27D1D0C046037906747D0193E425B11BB602D5FF837711B18E5B45F0222`.
Legacy engine/host/CPU hashes equal the preceding replay build. Candidate:
`.codex-tmp/lightweight-branch-inline-20260914`, unchanged from the retained
347.58 FPS comparison. The reference is this prospective frozen Legacy build,
not an invented reconstruction of the project's missing original baseline.

Wrapper SHA-256 `0BE53784754B1043B6FB540C0D6EA284E8A0F032F6130D925ADEECE4F25B9547`;
Legacy runner source SHA-256
`5247C7C1C8564C7E78F875C2B155E13AA19EECDDFF671717E45EEC7FD1B86D83`.
Logs: `.codex-tmp/h6d-paired-runner-final-tests-20260914.txt`,
`.codex-tmp/h6d-paired-runner-build-20260914.txt`,
`.codex-tmp/h6d-native-paired-series-20260914.txt`; external snapshots:
`.codex-tmp/h6d-native-paired-evidence-20260914/`.
No historical G6/G7 result or production selection changes here.

##### H6d completed paired comparison and acceptance recommendation (2026-09-14)

The six-sample wrapper completed with **exit 0**, no host-load rejection,
confirmed placement/priority and valid policy-v2 telemetry. Same native ROM,
media, input script and measured fields 10,921–14,520 in both engines; 600
gameplay warmup fields precede every 3,600-field measurement. The fixed order
was **R1 / C1 / C2 / R2 / R3 / C3**. All builds and host settings are those
frozen in the preceding implementation entry; no engine/CPU edit, build, test
or competing benchmark was introduced during the series.

| Engine | Three samples (FPS) | Median FPS | ms/field | Spread / median |
| --- | --- | --- | --- | --- |
| Frozen Legacy reference | 28.41, 28.41, 28.44 | **28.41** | 35.199 | 0.106% |
| Lightweight candidate | 344.70, 343.10, 342.39 | **343.10** | **2.915** | 0.673% |

Lightweight is **12.0767x** this frozen reference and exceeds 200 FPS by 71.55%.
Both spreads are below 10%. This is complete native-gameplay throughput, not
an inactive/synthetic estimate. It includes CPU, active chipset/DMA, framebuffer
and real 48 kHz stereo production; presentation, device pacing and diagnostic
file output are excluded. Legacy generates 716x285 pixels and float32
instantaneous digital audio; Lightweight generates the larger 908x313 full
raster and int16 sample-area-averaged audio. The ratio compares their complete
native paths, not identical rendering/audio algorithms or hardware parity.

**Determinism and work checks:** all three repetitions within each engine
have identical non-FPS result fields, including CPU/output fingerprints and
zero measured managed allocation. Lightweight also preserves hardware fingerprint
`0x886654CADB84C05F`, CPU `0x25B78C17DE5341FF`, output `0xD9AED52B4567F91B`,
and reports no unsupported operation encountered. Legacy CPU
`0xF94558EF5C8F94D1`, output `0xBE7313D358EB2AF2`, start/end pixel hashes and
full Chip-RAM hash reproduce the verified preflight in every repeat. Each
Legacy measured interval generates 3,461,510 stereo audio frames with nonzero
audio in all 3,600 fields. Its unsupported-feature reporting remains
`not-instrumented`; absence of that instrumentation is not a verification claim.
Both end at field horizon cycle 2,063,321,040; Legacy reports its separate
CPU endpoint 2,063,321,668 rather than concealing instruction overhang.
Preflight checkpoints and the earlier Lightweight digger-input evidence
establish gameplay/progress outside timing; cross-engine fingerprints are
neither equal nor required to be equal.

| Independent disposition | Result |
| --- | --- |
| Same-script native gameplay reproducibility | **PASS**, replacing the earlier missing-Legacy-replay STOP |
| Host measurement validity and within-engine determinism | **PASS** |
| Early throughput threshold: 50 FPS and 2x reference | **PASS against the newly frozen Legacy reference**; original Stage-1 baseline provenance is still missing |
| Complete-workload 200 FPS / 5 ms throughput requirement | **PASS: 343.10 FPS / 2.915 ms** |
| Implemented timing model | Existing focused checks retained; no new timing changes in this pass. Unverified hardware edges remain OPEN, not claimed verified |
| H6d acceptance | **Experimental GO recommended; awaiting user acceptance** |
| Production switch / full project completion | **Not authorized or claimed** |

The prior 330 Lightweight checks and focused CPU evidence remain applicable
because neither implementation changed. The 12 host-harness guard tests pass;
the known six CPU descriptor-label failures are not relabeled as passing.
LWA-DISK-010's physical receiver/PLL/mode-write phases and the other issues in
`LIGHTWEIGHT_A500_ENGINE_ISSUES.md` remain disclosed. Legacy panel differences
remain unisolated. Neither Legacy matching nor game execution proves cycle
correctness, and neither open hardware uncertainty alone is made a new blanket
barrier to the already agreed experimental continuation.

**Next:** accept H6d within that declared experimental scope, then review H7
closure using this complete-workload evidence. There is no measured need for
another optimization loop merely to reach 200 FPS. Host integration/selection
and cold boot, gameplay, input, audio delivery, reset and eject/remount checks
remain separate work before any supported-configuration default switch. This
recommendation does not itself grant stage approval or alter production routing.

Evidence: `.codex-tmp/h6d-native-paired-series-20260914.txt` (six RESULT lines,
two SUMMARY lines, final valid COMPARISON/disposition and complete telemetry),
`.codex-tmp/h6d-native-paired-evidence-20260914/R1/`, `R2/`, `R3/` (untimed
start/end images and RAM evidence). The earlier G6/G7 history is unchanged.

##### H6d user acceptance (2026-09-14)

The user explicitly accepted the preceding recommendation: **H6d is accepted /
experimental GO** for the documented A500 PAL OCS native-ROM, ideal-ADF
Lemmings workload. This supersedes the preceding pending-acceptance disposition
and completes the H6 staged experimental checkpoint.

The accepted evidence is the paired series above: **343.10 FPS**, 2.915 ms/field,
12.08x the prospective frozen Legacy reference, valid host/spread checks,
deterministic within-engine results and zero measured managed allocation.
The missing original baseline provenance, different native output algorithms,
LWA-DISK-010 and all other disclosed unresolved hardware edges remain unchanged.
Experimental acceptance is not a claim that those edges are hardware-verified.

**Next: H7 closure review** using the existing complete-workload performance
evidence; no additional optimization loop is required just to reach 200 FPS.
This acceptance does not itself close H7, authorize a production/default switch,
or mark the full replacement project complete. Host integration and its
cold-boot/gameplay/input/audio/reset/eject-remount checks remain separate work.
No implementation or measurement changed for this documentation-only acceptance;
tests and benchmarks were not rerun. Historical G6/G7 outcomes are preserved.

##### H7 closure review and host-integration handoff (2026-09-14)

Following explicit H6d acceptance, the user authorized this review. **H7 is
GO / the complete-engine performance stage is closed within the accepted
experimental H6d scope.** The 200 FPS objective is met for the specified native
Lemmings workload, not deferred to another synthetic checkpoint. This closes
the speed milestone, not all hardware-conformance questions or the overall
replacement/integration project.

The review checked the recorded evidence rather than rerunning an unchanged
benchmark. The paired series has six successful results, valid host/spread
disposition, the prescribed warmup/measurement/order and deterministic
within-engine fingerprints. Median **343.10 FPS / 2.915 ms per complete field**
leaves **2.085 ms** below the 5 ms target. The 12.08x comparison remains against
the explicitly prospective 28.41 FPS Legacy reference; no original Stage-1
baseline is reconstructed and no equal-output-algorithm claim is introduced.

Frozen candidate identities were rehashed during this review and match the
paired-series log:

- Copper68k: `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.
- Lightweight engine: `B63513DF87360DB4A714F1AEF2A1A219DC915DCB5421B7D890124ADC97F01893`.
- Runner: `CF3130EBC617F617D02183CFB18413AEB8EDC2980735C73A1C0865343426FF5E`.

The supporting test logs were checked: Lightweight **330/330**; focused CPU
**91/91 Release and Debug**; broader Amiga CPU **1,116 passed / 7 skipped**.
Full CPU remains **1,476 passed / 6 existing descriptor-label failures / 6
skipped**, not all green. See `.codex-tmp/branch-inline-{lightweight,debug,amiga,core}-tests-20260914.txt`
and `.codex-tmp/branch-inline-focused-20260914.txt`. These are retained results,
not fresh executions. No implementation changed in this review. The normal
rebuilt runner's previously recorded smoke reproduces the workload fingerprints,
but its differing engine/runner assembly identities do **not** inherit the
formal 343.10 FPS result; future integration builds need their own validation.

The [native profile](LIGHTWEIGHT_A500_NATIVE_GAMEPLAY_PROFILE.md) supplies the
profile-driven optimization record: CPU execution was the dominant remaining
cost; the retained general byte-test dispatch and compiler-inlining-policy
changes improved the complete workload without removing emulated work. Another
optimization or profile run is not a prerequisite for integration merely because
more speed might be possible. Hardware uncertainties, especially LWA-DISK-010,
remain OPEN under the accepted experimental model. Game progress, deterministic
fingerprints and passing model tests are not promoted to physical hardware proof.

**Next bounded implementation: Stage 4, opt-in CopperScreen adapter.** A read-only
boundary check confirms the lightweight project still references only Copper68k
and CopperDisk, and CopperScreen has no Lightweight engine-selection path yet.
Proceed through these host milestones without moving hardware ownership out of
the engine:

1. **Opt-in connection:** add explicit Lightweight selection at session creation
   for the supported native-ROM A500 PAL OCS / 512 KiB Chip + 512 KiB slow /
   read-only ADF configuration. Preserve Legacy/default and unsupported-profile
   routing. Keep one engine for the whole session; reject unsupported explicit
   requests visibly, with no mid-session fallback or mixed chipset state.
   Adapt reusable pixels/audio and input at the host boundary, not through old
   Bus/Scheduler/requester execution. Review this boundary before widening scope.
2. **Integrated-session verification:** cold boot, gameplay, mouse/keyboard,
   actual audio delivery, reset, disk eject/remount and unsupported reporting.
   Recheck the new build's headless complete-workload output and throughput;
   keep host presentation/pacing costs separate from engine FPS. Any confirmed
   supported-path regression must be repaired before default-switch readiness.
   Keyboard startup/recovery limitations stay disclosed; synchronized transport
   tests are not a substitute for the native host-input check.
3. **Default-switch decision:** publish the supported configuration and unresolved
   issue list with the integration evidence. Stop for a separate explicit
   decision before changing the supported default. Preserve explicit Legacy and
   existing unsupported-configuration routing; no earlier historical G7 cutover
   authorization is reused for this replacement engine.

This review changes documents only. It grants no production switch, closes no
unverified hardware issue, and leaves historical G6/G7 outcomes intact.

##### Stage 4 opt-in CopperScreen connection (2026-09-14)

The user authorized implementation after H7 review. The first host milestone
is connected: `--engine Lightweight` selects one `CopperScreenLightweightSession`
at runtime creation. Omission and explicit `--engine Legacy` retain Legacy.
`ICopperScreenSession` is a host/frame/command boundary, not a device scheduler
or per-instruction interface. The Lightweight session creates only a
`LightweightA500Machine`; the old Machine/Bus/boot controller/requesters are
not instantiated. Existing mount-time ROM/archive/ADF decoding is reused;
only standard ADF sector bytes enter the engine, which owns encoded tracks.
Unsupported profiles, media and Legacy execution overrides fail explicitly.
The window shows construction failures in settings without creating a fallback
machine. Engine opt-in survives the current settings/restart/profile-load flow;
it is not saved as a new global or profile-file default.

Supported opt-in configuration: PAL OCS, accurate 68000, 512 KiB Chip + 512 KiB
slow at `$C00000`, native 256 KiB v34 Kickstart 1.3, one read-only standard-ADF
DF0 (including ADF-in-ZIP). RTC, hard drives, RTG, other CPUs/chipsets/ROM
versions, writable or preserved-track media, extra drives and mouse-on-port-2
are rejected. `Profiles/lightweight-a500-kickstart13.json` provides these
settings but still requires the explicit engine flag and a ROM path.

Run from the repository root after a Release build:

```powershell
dotnet CopperScreen/bin/Release/net10.0/CopperScreen.dll --engine Lightweight --profile lightweight-a500-kickstart13 --rom D:/TestData/ROM/Kickstart_13.rom "D:/TestData/TestImages/Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip"
```

The host retains all 908x313 engine pixels. Its reusable 908x626 presentation
buffer duplicates progressive rows or weaves retained interlace rows for the
existing presenter; no engine crop, fetch or pixel production changes. Interlace
host presentation is implemented but not yet verified in a live native session.
Runtime audio uses the selected session's rate: Legacy remains 44.1 kHz;
Lightweight converts every signed-16 stereo sample to float at **48 kHz** with
no resampling or additional emulation advance. Variable field sample counts
are preserved. The existing output queue and audio device remain outside the
hardware owner. No fictitious per-device timing split or debug snapshot is
generated for Lightweight.

Input/media/reset commands execute on the existing runtime owner thread.
Relative mouse deltas, raw key transitions and digital controller levels reach
the engine directly. Absolute cursor hints from MainWindow are deliberately
ignored for native ROM, as in the Legacy native path: the window separately
submits relative movement, so applying both would double it. Disk replacement
can preserve a 25-field host eject interval; explicit eject and immediate
remount are available at the runtime/session boundary. Rejected replacement
media leave the mounted disk intact. Reset clears host pending state and
retains the chosen disk. Unsupported emulated execution pauses the session
with its fault visible; it cannot resume by silently choosing Legacy.
Clipboard and CopperStart/CopperBench services are explicitly unavailable.

Engine changes are read-only host metadata accessors (drive pins/status,
interlace/field and audio-filter control pin). They add no clock/device work,
release diagnostics or managed allocation. The filter indicator is its control
pin, not a new analog-filter implementation. No CPU source or hardware-phase
implementation changed. Existing unrelated Legacy M68040 run-ahead edits in
the dirty worktree were preserved rather than attributed to this integration.

Validation:

- Host boundary suite: **115 passed / 1 optional native test skipped**, covering
  existing runtime/startup/presentation/archive/audio-queue behavior plus the
  new selection, rejection, media, input, fault and adapter tests. A synthetic
  steady frame/audio adapter loop allocates **zero managed bytes**.
- Lightweight Release: **330/330 passed** after the read-only API additions.
- Separately enabled native adapter replay: **14,520 fields**, same frozen
  script/media/ROM, CPU `25B78C17DE5341FF`, output `D9AED52B4567F91B`, cycle
  `2063321040`, real active PCM and no unsupported operation. The checksum
  consumes the adapter's doubled-row pixels and float PCM mapped back to native
  sample values, not an unused parallel engine output. This is a correctness
  replay, **not a formal throughput or physical audio-device test**.
- Release builds pass; the existing package advisory and nested-build git-path
  warning remain. No full historical hardware corpus or CPU suite was rerun
  for host adaptation/read-only metadata edits.

Final Release host SHA-256 `104F8D529880FE151985065F355EC24C7A927597D6745EAD43413205C5C7CC5D`;
engine `27706196E4E3EC03B0518824593E632107A39D3EA3D7B73D14706B61B38DB95F`;
CPU `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.
The prior 343.10 FPS acceptance belongs to its frozen build; these new assembly
identities are not silently assigned that measurement.

Evidence: `.codex-tmp/lightweight-host-{build,boundary-tests,engine-tests,native-replay,native-final}-20260914.txt`.
Native replay is enabled with `COPPERSCREEN_LIGHTWEIGHT_NATIVE_ROM`,
`COPPERSCREEN_LIGHTWEIGHT_NATIVE_ADF` and `COPPERSCREEN_LIGHTWEIGHT_NATIVE_SCRIPT`;
without these inputs it reports a skip rather than a native pass.

**Disposition:** opt-in connection and automated boundary checks complete;
Stage 4 integrated-session/default-switch readiness remains **OPEN**. Next:
verify the actual window and audio device, native keyboard/mouse interaction,
pause/reset audio-queue behavior, user-driven disk eject/remount and interlace
presentation. Recheck new-build headless throughput separately under the existing
host policy. No new default, historical G6/G7 relabeling, hardware-uncertainty
closure or overall-project completion is claimed.

##### Stage 4 host-session follow-up (2026-09-14)

The next integrated-session check found and repaired a host-output defect:
pausing, resetting or faulting a Lightweight session left up to eight old PCM
buffers queued for playback. These transitions now discard the old queue
generation. Only the audio consumer advances ring read indexes, including a
partially consumed buffer; new-generation samples survive the discard.
Already delivered audio in the device's current block cannot be recalled.
This changes neither Paula execution nor the emulation clock and adds no
per-sample/per-cycle callback, trace or allocation. Legacy transition behavior
is unchanged; the shared queue retains its ordinary no-discard behavior.

The missing runtime discard was reproduced by a failing assertion before the
repair. Afterward the focused host/session/runtime/presentation/audio-queue
selection passes **47 tests**, with the optional native replay explicitly
skipped in that invocation. A separate enabled **14,520-field native adapter
replay passes** on the repaired host, preserving CPU `25B78C17DE5341FF`, output
`D9AED52B4567F91B`, cycle `2063321040`, real PCM and no unsupported execution.
These are correctness checks, not live device or throughput acceptance.

A real CopperScreen process launched with the opt-in profile and native media.
Its accessibility state showed a running Lightweight session at field 1450
and an active audio queue. However, the screenshot was the Windows lock
screen; UI interaction was stopped and only this verification process was
closed. No visual gameplay, live keyboard/mouse, audible output, user-driven
media or interlace presentation pass is claimed. Desktop unlock is required
to complete those checks, not to continue engine development.

Repaired host SHA-256:
`EF4CB70F5CA11BFB223F522116F7382FE78A89015667913780A9C9E315EADEFD`.
Engine remains `27706196E4E3EC03B0518824593E632107A39D3EA3D7B73D14706B61B38DB95F`;
CPU is unchanged from the preceding entry. Evidence:
`.codex-tmp/lightweight-audio-flush-before-20260914.txt`,
`.codex-tmp/lightweight-host-session-tests-20260914.txt`,
`.codex-tmp/lightweight-host-native-audioflush-20260914.txt`.

New-build engine-throughput retention **PASS**, measured separately against
the accepted frozen Lightweight build:

| Build | Samples (FPS) | Median | Spread | ms/frame |
|---|---|---:|---:|---:|
| Accepted frozen Lightweight | 335.62, 322.92, 318.94 | 322.92 | 5.17% | 3.097 |
| Current integration engine | 321.57, 323.71, 325.63 | 323.71 | 1.25% | 3.089 |

Retention is **100.24%**, above the active-workload 95% floor and the 200 FPS
objective. This establishes retention, not a significant optimization gain.
The same accepted binary now measures 322.92 rather than its historical
343.10 FPS; comparing the current candidate only with that historical number
would incorrectly attribute the entire session difference to integration.
No particular thermal, power or background-process cause is established.

Protocol: native ROM/media/script hashes checked against the frozen identities;
10,320-field gameplay checkpoint, another 600 warmup fields and 3,600 measured
fields per sample; order **R1,C1,C2,R2,R3,C3**. Full CPU/DMA, 908x313 pixels and
48 kHz stereo PCM are produced and consumed. All six runs preserve CPU
`25B78C17DE5341FF`, hardware `886654CADB84C05F`, output `D9AED52B4567F91B`,
cycle `2063321040`, zero measured allocation and no unsupported execution.
P-core topology was freshly verified: group 0, logical CPU **4**, mask **16**,
SMT sibling **5**, efficiency class **1** versus E-core class **0**; Normal
priority and Balanced power scheme. Policy-v2 telemetry is valid, with no
qualifying sustained contention or competing workload and spreads below 10%.
Short load spikes, including during final cooldown, did not meet the
continuous ten-second rejection rule. This mapping is evidence, not policy.

Frozen candidate: `.codex-tmp/lightweight-host-retention-20260914`; engine/CPU
identities above; runner SHA-256
`F41D8AB438CAFF3B3F2DFA8C4B0AC3CA8173CABA5DDF3CF42D12174FEE6E8C79`.
Reference: `.codex-tmp/lightweight-branch-inline-20260914`, unchanged H7 build.
External harness: `scripts/run-lightweight-native-retention.ps1`; complete
hashes, topology, sample order and telemetry:
`.codex-tmp/lightweight-host-retention-series-20260914.txt`.

This series excludes the host adapter, GPU presentation, audio-device pacing
and wall-clock throttling; it is not a measurement of displayed FPS. It does
not rerun or relabel the historical Legacy comparison. **Stage 4 remains
opt-in / OPEN pending live verification; Legacy remains the default.**

##### Stage 4 unlocked-desktop verification (2026-09-14)

Desktop access resumed after the user's unlock confirmation. The unchanged
Release host (`EF4CB70F...EADEFD`) was launched explicitly as Lightweight with
the supported native Kickstart 1.3 profile and read-only Lemmings disk 1.
Observed in the real window: changing crack-intro/Psygnosis/DMA Design/title
screens, mouse clicks advancing native loader screens, pause holding a frame,
resume continuing, the native disk-2 prompt, F12 causing an eject/remount
interval, then disk-2 reads reaching the animated Lemmings menu. These are
live integration observations, not hardware-oracle or displayed-FPS claims.

**Keyboard readiness STOP:** pressing Enter at the menu produces
`Lightweight unsupported: general CIA serial output transmission`.
The fault is recorded by `CopperScreenRuntime.RenderFrames`; subsequent Run
does not bypass it. PC `$001972`, last PC `$00196E`, SR `$2104`. The toolbar
shows Run without exposing the cause, so fault visibility also needs attention.
This is not an ordinary pause or a failure to focus the game viewport.

Independent headless reproduction submits raw Return `$44` at field 7900
after the same disk handoff. **Both the current and accepted frozen H7
engines stop with the same report**, cycle `1122747910`, CPU
`C6B48640BAD80718`, hardware `15B7207A0B2D4B93`, output `438A3609594803C4`;
exit code 2. The mouse-only accepted gameplay script never submitted a key.
Thus this is a previously unexercised input gap, not a regression introduced
by the CopperScreen adapter or pause/reset audio fix. Synthetic CRA-toggle
handshakes did not cover SDR writes in output mode. See **LWA-INPUT-004** in
`LIGHTWEIGHT_A500_ENGINE_ISSUES.md` for the exact reproduction and next test.
No serial guard was bypassed and no hardware behavior changed in this check.

Recovery verified live: Shift+F12 selected disk 1 while faulted; Reset mounted
that pending disk, cleared the fault and rebooted into the native crack intro.
The actual device queue resumed; paused/faulted output later reported Q0 and
zero audio-submit failures. The paused toolbar can retain its last published
queue count, so that stale label alone is not a queue-drain measurement.
Speaker audibility remains unconfirmed (the user was asked); interlace and
live in-level play/input remain unverified. The test window is left paused
after reset, without changing engine defaults or user media.

Evidence: `.codex-tmp/lightweight-live-keyboard-failure-20260914.log`,
`.codex-tmp/lightweight-native-menu-enter-20260914.json`,
`.codex-tmp/lightweight-native-menu-enter-{current-replay,reference}-20260914.txt`.
The existing 323.71 FPS retention result remains valid for its unchanged
mouse-only workload; it does not establish keyboard compatibility. Historical
H6d/H7 acceptance and G6/G7 outcomes are not relabeled. **No Stage 4 acceptance
or default switch: next work is the bounded CIA keyboard acknowledgement /
serial-output correction and explicit host fault visibility.**

##### Stage 4 bounded keyboard acknowledgement repair (2026-09-14)

The user authorized correction of the live Enter failure and fault visibility.
A temporary one-field diagnostic build captured the actual CIA-A writes:

| CPU cycle | Handler PC | Write | Timer A counter afterward |
|---:|---|---|---:|
| 1122609340 | $001752 | SDR=$00 (input mode) | 36053 |
| 1122609370 | $00175A | CRA=$41 (output mode) | 36050 |
| 1122609380 | $001762 | SDR=$01 | 36049 |
| 1122610510 | $001774 | SDR=$00 | 35936 |
| 1122610540 | $00177C | CRA=$01 (input mode) | 35933 |

Output mode lasts **1,170 CPU cycles**, about 165 microseconds; there is no
Timer-A underflow during it. The original guard confused buffered writes with
an actual transmission. The [Commodore HRM, appendix F, SDR output mode](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_f.html)
describes buffered data and Timer-A-clocked output; the
[keyboard protocol](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_g.html)
requires a low/high acknowledgement pulse and 85 microseconds for compatibility.
These sources do not establish every internal CIA direction-change pipeline
phase; those remain documented uncertainty, not claimed new hardware proof.

Minimal repair: retain an SDR holding byte and one pending-output flag;
SDR writes do not change SP. Returning to input mode cancels pending output.
If Timer A reaches a potential output clock first, the engine still reports
`clocked CIA serial output transmission`. That boundary participates in the
existing single-clock CIA path even when interrupts are masked, and survives
timer stop/start, same-cycle writes and field boundaries. It covers CIA-A and
CIA-B. No new event framework, callback, cache, per-cycle trace or allocation
was added. The temporary diagnostic statements were removed. No Copper68k or
Legacy hardware code changed.

Scope is deliberate: the observed no-clock acknowledgement is now supported;
general clocked serial output is **not** implemented by this repair. A key
handshake that reaches an output clock can still stop explicitly. Serializer,
pin-pipeline and MCU resynchronization work remains in LWA-INPUT-001/002.

Validation: the first five new regression cases failed before correction.
Final lightweight Release suite: **338/338 passed**, including eight new cases
for buffer-versus-pin separation, native acknowledgement, masked-IRQ output
boundaries, cancellation, timer restart, reset and same-cycle ordering.
Host/session/runtime/queue/presentation selection: **48 passed / 2 optional
native checks skipped**. Separately enabled native host replays: **2/2 passed**,
each 14,520 fields. One retains the original CPU/output fingerprints; the other
submits Enter down/up at fields 7900/7906, verifies the keyboard is idle by
field 7916 and reaches the same full gameplay output checksum
`D9AED52B4567F91B`, with active PCM and no unsupported execution.

Fault visibility: a dedicated nullable fault reason is published through the
host session/state boundary. A wrapping on-screen banner displays it even
when the toolbar is hidden, releases mouse capture and tells the user to
reset. Reset clears it. No per-device execution logging was introduced.
Runtime fault-publication/reset tests pass. A synthetic v34-tagged ROM (no native
ROM bytes; host fault fixture only) reaches the genuine clocked-output guard.
The real CopperScreen accessibility tree exposes the complete banner text:
`Emulation stopped: Lightweight unsupported: clocked CIA serial output transmission`
and reset guidance. Visual inspection remains **OPEN**: capture was black and
activation failed twice with `failed to activate captured window`, including
one refreshed-window retry. No visual/fullscreen PASS is claimed. The fixture
process was closed afterward; no user emulator process was closed for this
fixture cleanup. Native live gameplay, speaker audibility and interlace checks
remain separate open host checks.

Formal engine retention after the repair: **VALID / retained**. Same frozen
native script and interval (10,320-field gameplay checkpoint, 600 warmup fields,
3,600 measured fields), three samples per build in R1/C1/C2/R2/R3/C3 order:

| Build | Samples (FPS) | Median FPS | Spread / median |
|---|---|---:|---:|
| Frozen prior integration | 348.58, 339.91, 335.96 | 339.91 | 3.71% |
| Keyboard repair | 341.76, 337.53, 348.56 | **341.76** | 3.23% |

Retention is **100.54%** (2.926 ms/frame candidate); this is no measured
regression, not a claim of a meaningful speedup. All six samples retain CPU
`25B78C17DE5341FF`, hardware `886654CADB84C05F`, output `D9AED52B4567F91B`,
cycle 2063321040, complete pixels/real PCM and **zero measured allocation**.
Placement was freshly verified P-core logical CPU 4 / mask 16, efficiency
class 1, SMT sibling 5, Normal priority, Balanced power plan. Host-load policy
v2 telemetry and spread checks pass. Windows build 26200 / .NET SDK 10.0.303;
dirty-tree frozen assembly hashes identify the builds, not HEAD alone. This
measures complete engine execution, excluding the host adapter, presentation
and device pacing; it is not a new production switch or live keyboard gate.
Full topology, build/media/script identities, ordering and telemetry:
`.codex-tmp/lightweight-keyboard-retention-series-20260914.txt` and its
same-name evidence directory.

Evidence: `.codex-tmp/lightweight-keyboard-{trace2,handshake-before,final-engine-tests,host-tests,native-host}-20260914.txt`.
Frozen candidate: `.codex-tmp/lightweight-keyboard-handshake-20260914`;
engine SHA-256 `459D6853711ADC94243481448190AAF2FE5682DD56D28590597303E770BE3DC3`;
runner `5E4B876B289B7D5B391691BAC1257A4A1FFB90B5A4A01015742007F08BF7300B`;
host `6F31EADEF4CEB2275072EE148FCC8D0DF7C7329701A88951EF6DC4ADA4696BD0`;
CPU remains `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.
Stage 4/default switch remains OPEN; no historical acceptance is relabeled.

##### Stage 4 visible fault-banner recheck (2026-09-14)

The next user-authorized live check used the unchanged repaired Release host
SHA-256 `6F31EADEF4CEB2275072EE148FCC8D0DF7C7329701A88951EF6DC4ADA4696BD0`.
Launching the synthetic clocked-serial-output fixture visibly allowed actual
image inspection. **Fault-banner visual PASS**: the reason and reset guidance
are readable in the normal window, fullscreen, and fullscreen with the toolbar
hidden using F11. This closes the earlier banner-only visual uncertainty;
it is not native boot or hardware timing evidence. No source changes were
needed. The synthetic fixture process was closed after the check.

The native Kickstart 1.3 / read-only Lemmings disk-1 session was then launched
with explicit Lightweight selection. Its process remained responsive, but
native-window capture failed with `foreground window did not report a process
id`, including one refreshed-window retry. Computer-use recovery policy stopped
further UI actions. The native window was left open (PID 82148 for this run).
No live native key, gameplay, audio-audibility or interlace PASS is claimed from
this retry. These remain the next host checks; the prior two successful native
replays and valid 341.76 FPS retention evidence remain unchanged. Legacy remains
the default and Stage 4 remains OPEN.

##### Stage 4 cracktro DMA and host-coordinate repair (2026-09-14)

The user reported a misplaced/incomplete cracktro during the live retry.
Capture now worked. The pre-repair native window at field 38450 showed two
rainbow lines and only fragments of text, even in fullscreen. Independent
600-field raw-engine snapshots reproduce the missing artwork, excluding host
scaling as its cause. Profile: PAL OCS / native Kickstart 1.3 / 68000 /
512 KiB Chip + 512 KiB slow / read-only standard ADF disk 1.

**Confirmed DMA defect, repaired:** DIWSTRT=$2C10, DIWSTOP=$3CF0,
DDFSTRT=$0030, DDFSTOP=$00D8. Bitplane DMA decoded vertical stop $3C as
line 60 because it only extended stops numerically below the start. OCS
actually supplies V8 as the complement of V7, independently of the start:
$3C means $13C (316). Denise's existing output decoding already did this.
The result was missing fetches after line 59, not a Copper/artwork or host
upload failure. The [Commodore HRM DIWSTOP description](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node002E.html)
establishes the high-bit rule. The repair adds that decoding to the existing
DMA check; no clock phases, scheduler framework, tracing or cache are added.
The existing wrapped-window model is otherwise unchanged, not newly certified.

Four focused decode cases cover $3C, $2C, $80 and $F4, fetched-word counts
and the stop edge where inside the PAL field. $3C fails before correction;
all four and the full **342-test Release engine suite pass** after correction.
Raw field-600 output now contains readable text, stars and scrolling artwork.
Evidence directories: `.codex-tmp/lightweight-cracktro-recheck-20260914` and
`.codex-tmp/lightweight-cracktro-fixed-20260914` (BMP, JSON and Chip-RAM snapshots).
Before: cycle 85261202, PC $0303E0, BPL1PT $04B1C4; after: cycle 85261208,
PC $0303EC, BPL1PT $04DD40. Additional DMA changes intro contention as expected.

**Host-coordinate defect, repaired separately:** Lightweight retains beam
coordinates in its 908x626 host buffer, unlike Legacy's already-offset capture.
Its full visible viewport is now (196,52,712,570), matching the engine's
lowres visible range x=98..453 / y=26..310. Standard PAL uses
(258,88,640,512), corresponding to DIW origin ($81,$2C). Optional geometry
viewports keep Legacy's existing behavior unchanged. Only already-blank
samples are excluded from the full host view; complete engine pixels/audio
remain generated and included in checksums. This is host coordinate mapping,
not hiding missing artwork or modifying emulation timing. Viewport and mouse
mapping regression coverage plus focused host checks: **44 passed / 2 optional
native tests skipped**. Separately enabled native host replays: **2/2 passed**
(14,520 fields each), including Enter down/up acknowledgement followed by
continued gameplay and the original mouse-only CPU/output assertions.

Live repaired window verified animated text/starfield/scrolling artwork and
both full-overscan and standard-crop placement. Left paused at field 1838 in
full overscan (PID 38168 for this run). No live in-level gameplay, speaker
audibility or interlace acceptance is inferred. Full cracktro hardware
correctness remains broader than this specific repair.

The original 10,920 warmup / 3,600 measured gameplay script completes with
unchanged CPU `25B78C17DE5341FF`, hardware `886654CADB84C05F`, output
`D9AED52B4567F91B`, cycle 2063321040 and zero measured allocation. Its single
**322.21 FPS is diagnostic only**: unpinned, no host-v2 telemetry, and a build
overlapped the run. No new throughput acceptance or regression conclusion is
drawn; 341.76 FPS remains evidence for the preceding build, not a measurement
of this one. Frozen engine candidate: `.codex-tmp/lightweight-diwstop-repair-20260914`;
engine SHA-256 `8C6FFCA97305BAE35CE065BB4BFC15E84E1B6B35F039214CBBD9B8C3C62A0569`;
host `BF5654FDDB3EED8B870989098BDF8D5A3C20D8C471E766DF68FC38D659A102E9`.
Stage 4/default switch remains OPEN; Legacy remains default.

##### Stage 4 display-repair formal retention (2026-09-14)

Fresh complete-workload comparison against the preceding keyboard repair:
**VALID; 200 FPS target PASS**. Candidate median **327.07 FPS** (3.05745
ms/frame), reference **332.80 FPS** (3.00481 ms/frame): **98.28% retention**,
a 1.72% lower observed median / +0.05264 ms per frame. Ranges overlap; this
small measured difference does not establish its causal source.

| Build | Samples in per-build order (FPS) | Median | Spread / median |
|---|---|---:|---:|
| Keyboard repair reference | 332.80, 323.63, 332.80 | 332.80 | 2.76% |
| DIWSTOP repair candidate | 325.82, 335.53, 327.07 | 327.07 | 2.97% |

Same native ROM/media/script and interval: checkpoint 10320 +600 gameplay
warmup, 3600 measured fields, R1/C1/C2/R2/R3/C3 order. All six samples:
cycle 2063321040, CPU `25B78C17DE5341FF`, hardware `886654CADB84C05F`,
output `D9AED52B4567F91B`, full 908x313 pixels / real 48 kHz stereo,
zero allocation and no unsupported execution. Runner, CPU and media-library
identities are unchanged; frozen engine hashes identify the two builds.

Fresh topology: P-core LP4 / mask16, efficiency class1, SMT sibling5;
Normal priority, Balanced plan, Windows 26200, SDK10.0.303. Host-load policy
v2 and both spread checks pass. The live CopperScreen session stayed paused;
no concurrent build, test or emulator workload was launched. Timing excludes
host upload/presentation/device pacing. Evidence (build/input identities,
full topology, ordering and telemetry):
`.codex-tmp/lightweight-diwstop-retention-series-20260914.txt` and same-name
evidence directory. Candidate throughput is no longer pending; this does not
relabel historical G6/G7 outcomes or change production selection. Stage 4
remains OPEN for remaining live checks and an explicit default-switch decision.

##### Stage 4 live repaired native gameplay (2026-09-14)

After the formal series ended, resumed the unchanged repaired host (PID38168,
host hash `BF5654FDDB3EED8B870989098BDF8D5A3C20D8C471E766DF68FC38D659A102E9`).
Observed real-window sequence: repaired cracktro -> DMA Design / animated
intro -> readable disk-2 prompt -> F12 eject/remount -> animated menu ->
one-player selection -> "Just dig!" briefing -> running level. Enter was
submitted at the menu around field11371; animation continued beyond field12681
without the previous unsupported fault. A further F1 key also did not fault;
neither key is claimed to select a menu item. Relative mouse movement plus
click selected one-player mode around field14961. Briefing mouse input started
the level; observed OUT5 / TIME4:50, then OUT10 / TIME4:26 and 4:22. These are
live integration observations, not new hardware-oracle or throughput evidence.

Live mouse navigation and button response are demonstrated. A successful
in-level skill assignment/rescue was **not** verified: the automated digger
attempt did not produce a confirmed terrain change or saved lemming. That
check remains open alongside speaker audibility (asked asynchronously; not
confirmed) and interlace presentation. Actual audio queues remained populated
while playing; this alone does not prove sound reached speakers. The mouse
was released with F10 and the game left open for user interaction. No source
changes or default switch occurred during this verification. General clocked
CIA output and previously documented timing uncertainties remain deferred.

##### Stage 4 Hired Guns interlace observation and loading stall (2026-09-14)

Used the unchanged repaired engine
`8C6FFCA97305BAE35CE065BB4BFC15E84E1B6B35F039214CBBD9B8C3C62A0569`
and host `BF5654FDDB3EED8B870989098BDF8D5A3C20D8C471E766DF68FC38D659A102E9`,
native Kickstart 1.3, PAL OCS / 68000 / 512 KiB Chip + 512 KiB slow,
read-only DF0. Media: `D:/TestData/TestImages/Hired Guns v1.08.39.25
(1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip`, containing a 901120-byte ADF;
ZIP SHA256 `3880F61D819113256FCDD887FAAD1FA0F29EA7B766B6A7642D1CB7BABD912716`.

The no-input 2400-field probe reached native Workbench, not the game. Reproduction
requires opening the disk icon, double-clicking the game icon, and accepting
SystemTakeover. The user performed the live launch and explicitly confirmed
that the interlaced loading screen worked, followed by a loading crash.
Disposition: **live interlace presentation PASS (user observation)**, not
hardware-phase/field-order certification and not Hired Guns compatibility PASS.
The historical CopperStart interlaced-HAM test is supporting context only;
this live run used the native ROM, with no CopperStart or Legacy fallback.

Post-failure inspection found a black viewport, last published field **4034**,
PC `$C1A17E`, last-PC `$C1A186`, DF0 **46.1 MSP**, and no visible fault banner.
Repeated observations were unchanged while the process remained responsive and
consumed CPU. A successful minidump captured the emulation worker inside
`ReadWordRaw -> ExecuteFrame -> RenderNextFrame -> RenderFrames`. This supports
an engine execution stall, not a process exit or an audio/presentation wait;
the published PC is not a proven faulting instruction. Exact cause is OPEN
as **LWA-EXEC-001**. A larger heap capture failed with PARTIAL_COPY and supplies
no accepted state evidence. The session was not reset or terminated.

Evidence under repository `.codex-tmp/`: `lightweight-hiredguns-interlace-20260914/`
and matching `.log` (no-input boot only), `lightweight-hiredguns-stall-20260914.dmp`,
and `lightweight-hiredguns-stall-20260914-stacks.txt`. Existing host log:
`C:/Users/vsys-admin/AppData/Local/CopperScreen/Logs/CopperScreen-20260914-201835-16508.log`.
The next diagnostic is a deterministic manual-launch replay and CPU/clock/frame
state at the first non-progress boundary. Do not infer a disk timing correction
or blame interlace solely from this title. No source changes, benchmark, default
switch or hardware-edge closure occurred. Live Lemmings skill assignment and
speaker audibility remain unconfirmed; the interlace presentation item is now
closed at the stated visual scope.

##### Stage 4 interrupt-tail progress repair (2026-09-14)

**LWA-EXEC-001 repaired, Hired Guns compatibility still OPEN.** Read-only CLR
inspection of the stalled live process obtained the missing state without the
failed heap dump: internal completed field 4041, hardware cycle 574314532,
short-field boundary 574314540, CPU cycle 574314544, PC `$C0288C`, SR `$2600`.
The UI's field4034/PC is an older publication, not the actual stall boundary.
`RequestInterrupt` retires a final internal tail after the last bus transfer;
the adapter returned without advancing hardware through it. With CPU cycles
already beyond the next field boundary, `ExecuteFrame` repeatedly did zero work.

The fix is one canonical-clock advance after accepted interrupt entry in
`LightweightA500Machine.DispatchPendingCpuInterrupt`. It does not change the CPU
interpreter, 44-cycle entry timing, bus accesses, device rules or host presentation.
No trace/counter/timeout is added to production execution. The Amiga and CPU
boundary skills guided a minimal integration repair rather than a disk or
title-specific workaround. Three new line/short-field/long-field tests reproduce
the 12-cycle discrepancy before editing and pass afterward; Release lightweight
suite **345/345 PASS**. Tests assert synchronization before attempting frame
execution, avoiding a hung worker on the pre-fix implementation.

Focused host/presentation/audio validation **41/41 PASS**, including both native
14520-field Lemmings adapter replays (with/without the keyboard press/release).
The initial build attempt hit the still-running old host's DLL lock; after
closing that captured stalled process, the rerun built and passed. Log:
`.codex-tmp/lightweight-interrupt-tail-host-tests-rerun-20260914.txt`.

Frozen candidate: `.codex-tmp/lightweight-interrupt-tail-repair-20260914/`;
engine SHA256 `A5CD099B1EF2275B712741B17EAC318676783DE140DB19389E4BC6A366B46A25`.
Copper68k remains `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`;
host remains `BF5654FDDB3EED8B870989098BDF8D5A3C20D8C471E766DF68FC38D659A102E9`.
The host build receives the repaired engine DLL; the old stalled live process
and the deliberately stalled old-build headless replay were closed after capture.

Added runner `Workloads/hiredguns-native-launch-exploratory.json`, SHA256
`14C399AC3942AFB26ACA9F70BDB77D1C61356F1890828F47F8C34240E5C63E68`:
open disk, launch game, accept takeover, Continue with English. Frozen old engine
reproduces the stall at internal field4046 (before LACE): hardware575086784,
boundary575086794, CPU575086796. Same repaired replay completes **6500 and 12000
fields**, real output/audio, no unsupported bypass. At common checkpoint6480,
state SHA256 is `AFAEFCA86E6CFC4A57962619CD3A394B42B079ECFD3E970EE0715CB8D559EC23`
and raw BMP SHA256 `5C73C5F5CDFFB929A5BA56FD6BE91AC8E6E5468F96068E578C0AECB7AB888F39`
in both runs. This confirms reproducible progress, not gameplay: later raw
captures are mostly blank (LWA-VIDEO-004), despite active interlaced mode/audio.

Native Lemmings replay with 10920 warmup / 3600 consumed fields completes at
cycle2063321040 with unchanged CPU `25B78C17DE5341FF`, hardware
`886654CADB84C05F`, output `D9AED52B4567F91B`, zero measured allocations and no
unsupported feature. Its **282.38 FPS is diagnostic only**: unpinned, no v2 host
telemetry, old spinning emulator still present. Do not compare it as a formal
regression result or replace the preceding valid 327.07 FPS series. Fresh formal
retention is not established by this check. Historical H/G gate outcomes and
Legacy/default routing are unchanged.

Evidence under `.codex-tmp/`: `lightweight-interrupt-tail-before-20260914.txt`,
`lightweight-interrupt-tail-after-20260914.txt`,
`lightweight-hiredguns-live-state2-20260914.txt`,
`lightweight-hiredguns-scripted-stall-20260914.txt`,
`hiredguns-launch-before2-20260914.log`, `hiredguns-launch-fixed2-20260914/`,
`hiredguns-launch-fixed-long-20260914/` and matching logs,
`lightweight-interrupt-tail-lemmings-20260914.txt`.

##### Stage 4 Hired Guns COPJMP read-strobe diagnosis and repair (2026-09-14)

**Blank/stale display cause repaired; true loading screen now stops at unsupported
HAM. No gameplay or throughput acceptance.** Consecutive raw fields prove this
was not merely a single-field capture artifact. The earlier credits-loop PC
sample was also not proof of a guest hang: a bounded instruction probe observes
one VERTB handler, `$C02D70` changing 00->FF at `$C02CF8`, then FF->00 at
`$C093DA` as the main routine advances its credits transitions.

The guest writes COP1LC at `$C1E8F2` and reads COPJMP1 at `$C1E8F6`.
Read strobes were absent from the lightweight bus, leaving the old list able
to overwrite the requested new list pointer. The Commodore HRM
[Starting the Copper After Reset](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0056.html)
uses this same `MOVE.W COPJMP1(a1),d0` sequence. Added one narrow physical-read
strobe path for COPJMP1/2, reusing accepted restart phases. No title special
case, CPU edit, per-cycle diagnostics, raw-peek side effect or fallback.
Read return data/open-bus behavior and undocumented restart edges are not newly
certified. Eight new read cases fail before and pass after; full lightweight
Release suite **353/353 PASS**.

Frozen candidate `.codex-tmp/lightweight-copjmp-read-repair-20260914/`, engine
SHA256 `71C378666D31BF8A4DF0DE0ACAE034D71CDF32EC1647004BBBB8B0B6D87BF4CF`.
Unchanged Hired Guns launch script reaches field5475, cycle777793752,
PC `$C02890`, BPLCON0 `$6804`, DIW `$5071/$DCD1`, DDF `$0030/$00D8`,
modulos44/44, then explicitly stops at **OCS HAM output**. This demonstrates
that LWA-VIDEO-004's omitted restart hid the real loading display requirement;
LWA-VIDEO-005 records that missing mode. Existing user interlace confirmation
is retained at its visual scope, not promoted to successful HAM rendering.
Next meaningful slice is HAM6 output, with focused hold/modify timing and
native replay/performance checks, not further cropping or loader workarounds.

Native Lemmings completes the unchanged 10920 warmup / 3600 consumed-field
replay with real pixels/audio, zero measured allocations and no unsupported
feature. The missing read strobe changes its execution/output fingerprints:
cycle2063321672, CPU `E9ED169188EABAE5`, hardware `A8DFE876EDDBBF09`, output
`514760AD9BC62BCE`. An independent selective-capture replay reaches the same
final cycle; visual inspection of field14520 confirms the terrain, lemmings,
toolbar and gameplay status (`OUT 10`, `TIME 3-31`). The native host replay
expectations are rebaselined to this repaired behavior, not relaxed or removed.
The headless result of **341.20 FPS is diagnostic only** (unpinned, no v2
telemetry); it does not replace the preceding valid 327.07 FPS series or
establish formal retention for this candidate.
Host validation after rebaseline: **18/18 session tests PASS**, including both
native 14520-field gameplay and keyboard press/release replays, with the
documented `COPPERSCREEN_LIGHTWEIGHT_NATIVE_ROM`, `_ADF`, `_SCRIPT` variables
set; **23/23 framebuffer/audio-queue tests PASS** separately. The non-keyboard
host replay independently matches the runner CPU/output fingerprints; both
host replays match the final cycle/output and retain active audio. CopperScreen's
copied engine DLL matches the frozen candidate SHA256 above.

Evidence: `.codex-tmp/hiredguns-consecutive-20260914/`,
`hiredguns-consecutive-late-20260914/`, `hiredguns-flagtrace-20260914.log`,
`hiredguns-copjmp-before-20260914.txt`, `hiredguns-copjmp-after-20260914.txt`,
`hiredguns-copjmp-fixed-20260914/` and matching log,
`hiredguns-copjmp-lemmings-20260914.txt`, `copjmp-lemmings-final-20260914/`,
`copjmp-host-before-rebaseline-20260914.txt`,
`copjmp-host-after-rebaseline-20260914.txt`,
`copjmp-host-output-checks-20260914.txt`. No G/H gate relabeling,
default switch or complete Hired Guns compatibility claim.

##### Stage 4 lightweight HAM output slice (2026-09-14)

Implemented a separate lores HAM decode path in `LightweightVideo`, with one
held ARGB color. It reuses the existing physical shifter samples, DMA, palette
and framebuffer; no generic decoder framework, lookup cache, extra buffers,
events, allocations or logging. The ordinary lores path gains one mode-bit
branch; the hires render path is unchanged. HAM5 and HAM6 share the same
masked six-bit decoder. Sprite colors overlay the held playfield result and
do not contaminate subsequent modify pixels. Unsupported HAM/hires/dual-playfield
combinations still stop explicitly. No changes to Copper68k or bus timing.

Eleven focused cases cover RGB ordering, direct palette codes, both framebuffer
widths, interlaced field boundaries, HAM5, window clipping, sprite overlay,
palette changes and unsupported combinations; full engine suite **364/364 PASS**.
Current COLOR00 initialization follows the Commodore manuals; exact undocumented
HBLANK/DIW, mid-line mode-switch and sprite priority edges remain explicitly
unverified in LWA-VIDEO-005. A synthetic pass does not certify those edges.

Frozen build `.codex-tmp/lightweight-ham6-20260914/`, engine SHA256
`010B40B66A2D114B0C99009DE20AD8EBE098CF926BA323D49268477C7EA39579`.
The unchanged native Hired Guns launch now completes 12000 fields, with audio
and no unsupported feature. Field5520 visibly renders the HAM loading logos;
fields6500 and12000 show different animated credits. Field5520 BMP SHA256
`A6996D9D87AFA9B5D2DD1DABF1806701CE70E57E8A1AE701BF45742C9292EA4A`
matches in two independent replays. This closes the demonstrated missing-mode
stop, not full Hired Guns gameplay or live woven-field acceptance.

Evidence: `.codex-tmp/ham-before-20260914.txt`, `ham-after-20260914.txt`,
`hiredguns-ham6-20260914/`, `hiredguns-ham6-long-20260914/` and matching logs.
Snapshot-probe FPS and allocations are diagnostic only. Legacy remains default;
no historical G/H results are relabeled.

Host regression suite **41/41 PASS**, including the native gameplay and keyboard
replays with the three `COPPERSCREEN_LIGHTWEIGHT_NATIVE_*` environment variables
set (`ham-host-20260914.txt`). Both still match the post-COPJMP cycle/output
fingerprints; no further golden-value changes were necessary. A separate
exploratory continuation adds a left-button press/release at fields6500/6503;
field9000 still shows credits, not a demonstrated main menu. Do not promote
this loading/credits result to gameplay acceptance. Evidence:
`hiredguns-ham-continue-20260914.json`, `hiredguns-ham6-continue-20260914/` and log.

The native retention script's strict expected cycle/CPU/hardware/output values
are updated to the independently verified 2026-09-14 COPJMP repair, leaving its
interval, warmup, sample count/order, topology checks and host-load policy
unchanged. Use only post-COPJMP builds with this revised fingerprint contract;
earlier recorded comparisons remain historical evidence.

**Controlled ordinary-gameplay retention: VALID, 98.12%.** Same native Lemmings
script and fields, 10920 boot/warmup fields (including 600 gameplay warmup),
3600 measured fields per sample, order R1/C1/C2/R2/R3/C3. Fresh topology verified
LP4/mask16 as P-core efficiency class1, SMT sibling LP5, class0 E-cores LP16–23;
Normal priority, Balanced power plan, .NET10.0.303, Windows10.0.26200.0.
Host-load policy v2 and spread checks PASS. Reference is the post-COPJMP frozen
engine `71C378...BF4CF`, not the older pre-COPJMP build.

| Build | Samples FPS | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Post-COPJMP reference | 340.49, 350.16, 351.36 | 350.16 | 3.10% |
| HAM candidate | 351.72, 338.94, 343.59 | 343.59 | 3.72% |

Candidate median is 1.88% lower, 2.910 ms/field. This is not a speedup claim.
All six samples retain cycle2063321672, CPU `E9ED169188EABAE5`, hardware
`A8DFE876EDDBBF09`, output `514760AD9BC62BCE`, complete pixels/48kHz stereo audio,
zero measured allocations and no unsupported feature. This measures ordinary
gameplay retention after adding HAM, not HAM throughput or full Hired Guns
acceptance. Evidence: `.codex-tmp/ham6-native-retention-20260914.log` contains
build/media hashes, topology, sample order, priority/affinity and core/sibling/
aggregate telemetry. Historical gate outcomes and default routing are unchanged.

Actual HAM loading work also completes without probe overhead: 5500 warmup /
500 measured fields (5501–6000), real pixels/audio, **zero measured allocations**,
no unsupported feature, cycle852277866, CPU `4C588ACB7A67D80C`, hardware
`3B9519964F5F34D0`, output `4BE6DC90D6132AD0`. This short unpinned sample reports
**263.13 FPS**, diagnostic only, not a formal gameplay or throughput gate.
Snapshots independently show BPLCON0 `$6804` during this loading interval.
Evidence: `.codex-tmp/hiredguns-ham6-allocation-20260914.txt`.

##### Stage 4 Hired Guns main-menu replay (2026-09-14)

**User acceptance: ACCEPTED, 2026-09-14.** The accepted slice is native Hired
Guns loading artwork, animated credits and the reproducible transition to
MAIN MENU. Gameplay, menu-action coverage, live host presentation and the
disclosed hardware timing edges remain separate follow-ups; they do not hold
this accepted slice open. This is not acceptance of all Stage 4 work or
authorization for a production-default engine switch. Existing performance
evidence and historical gate dispositions are unchanged.

**MAIN MENU reached without an engine change.** The credits are intentionally
cyclic (`$C096D0` returns to `$C095F8`). The bounded guest-code inspection shows
input polling at `$C02A92` during credits dwell intervals, testing the guest
keyboard latch `$C02A66` and CIAA mouse/joystick fire pins. The previous short
mouse pulse at fields6500–6503 occurs during the animation code (`$C09300`,
VERTB wait `$C093E0`); it need not survive until the next input poll. A Space
key event remains latched and exits the credits normally. This is input-script
coverage, not a newly discovered CPU/chipset defect or a title-specific fix.

New repository workload:
`CopperMod.Amiga.Lightweight.Runner/Workloads/hiredguns-native-main-menu-exploratory.json`,
SHA256 `38829FB5061530DA07291EEA016355D4BBEEB7429DF60FB71C2CFA3E1C72A812`.
It preserves the existing launch sequence and adds Space down/up at frame
indices6500/6560 (`0x140` / `0x1C0`, decimal320/448). The earlier launch and
mouse-only continuation evidence remain unchanged. Use the existing native
Kickstart1.3 and Hired Guns disk1 identities, 9000 total fields and no warmup
for a complete boot-to-menu replay. No save state or old-engine path is used.

Field6900 and9000 display MAIN MENU with F1 Start a new game, F2 Continue a
saved game, F3 Exit to Workbench. At field9000: cycle1277902862, PC `$C097A2`,
SR `$2009`, BPLCON0 `$C004`, DIW `$2A81/$FBC1`, DDF `$003C/$00D4`, modulos80/80,
nonzero audio, DF0 idle/deselected at cylinder76.0, no unsupported feature.
The displayed choices agree with the [original game manual's main-menu section](https://www.lemonamiga.com/doc/hired-guns/797).
This closes the requested native main-menu target, not gameplay, F1/F2/F3
action coverage, live host presentation or undocumented timing-edge acceptance.

Frozen HAM engine remains `010B40B66A2D114B0C99009DE20AD8EBE098CF926BA323D49268477C7EA39579`.
Only workload/evidence documents change; preceding 364 engine / 41 host checks
and the 343.59 FPS controlled HAM-candidate median remain historical evidence
for that unchanged binary. No new performance result or default switch.
Diagnostics remain outside production: `.codex-tmp/hiredguns-menu-probe-20260914/`
and log; `.codex-tmp/hiredguns-menu-space-20260914/` and log.

Independent replay using the saved repository workload repeats the result:
`.codex-tmp/hiredguns-main-menu-repeat-20260914/` and matching log. Both 9000-field
runs have CPU `BBCA91C3400F82EC`, hardware `25B7883508766135`, output
`D8A577C55DE0AADD`, and identical final menu BMP SHA256
`EF2B4C42053E2A0AD965AC3C4159FCA43596F26BE38208E5FD0407AF6FDB735B`.
The BMP is one raw interlaced field, not a captured live woven host window.
Snapshot-probe timings/allocations are not acceptance measurements.

##### Stage 4 Hired Guns initial Exploration gameplay (2026-09-14)

**Native loading and initial movement work; no engine change.** Continued the
accepted main-menu run using ordinary host key/mouse/media input. Selected
F1 New Game, F1 Training, F1 Single Player and F1 Exploration, then selected
four characters and confirmed the team. The game requested disk2 and disk3;
both were mounted in DF0 after an explicit eject interval and acknowledged
with Space. No extra drive, writable media, guest-memory patch, forced PC,
unsupported bypass or fallback was used.

| Completed field | Observed result |
| ---: | --- |
| 9306 | New Game menu |
| 9812 | Player-count menu |
| 10318 | Training mission menu |
| 11524 | Character selection |
| 13228 | Four selected portraits; team complete |
| 14788 | Disk2 request |
| 17308 | Disk3 request |
| 19828 | Exploration scene, four views and active audio |
| 20378 | First character's view changes after forward click |
| 20648 | First character's view/compass changes after turn click |

Menu F1 presses and character-selection cursor/Return inputs reach the game.
Some six-field Return taps during selection did not fill the next portrait;
the exploratory sequence retains those attempts and uses sixty-field presses
for reliable selection. This is not evidence of a diagnosed timing bug or a
reason to alter keyboard emulation. Single-player movement is mouse-controlled;
the preliminary cursor-up input is not counted as a movement success.

Saved workload: `CopperMod.Amiga.Lightweight.Runner/Workloads/hiredguns-native-training-exploratory.json`.
SHA256 `56E28164D2EB8A622E3E4D9B5A14AEF5AE6CBA933F6B5ECE8A9192620B034D64`.
It extends the accepted menu sequence, preserves the observed input timings,
and mounts the user-provided disk2/disk3 ZIPs. Media SHA256:
disk2 `F33E3E6987EE3457821F89670502708B4330574215B1894FB583D487192C6CAC`;
disk3 `3BCF2FF4434B2B02FC9DF6C988E8AFA34396EFC690528E9EED7D3BC6E22E1BC7`.
Native ROM/disk1 and frozen HAM engine `010B40B6...39579` remain unchanged.

Evidence: `.codex-tmp/hiredguns-training-interactive-20260914/` contains bounded
before/after raw field images, device/CPU states and a frame-indexed input
journal. Captures include consecutive field pairs; these are not a live host
presentation recording. No production instrumentation was added. Scope is
initial Exploration loading and movement, not mission completion, combat,
inventory/save/load, all controls or broad game compatibility. The accepted
main-menu milestone is unchanged. No new formal throughput measurement is
claimed and no default engine switch is authorized by this result.

An independent cold-boot replay of that saved workload completes all 20648
fields and matches the interactive run's final raw framebuffer exactly:
SHA256 `3FF83B47C7CCFCCF55A2F0235EB713EF5CEF7C9DBAC580FA14CB293614291539`.
Final state also matches: cycle2930514162, PC `$C05FCE`, SR `$2004`, Copper PC
`$14C46`, DMACON `$07D9`, no unsupported feature and nonzero audio. The frame
boundary BPLCON0 `$0000` is the Copper's border state, not absent playfield work;
the completed frame contains the four gameplay views. Repeat evidence:
`.codex-tmp/hiredguns-training-repeat-20260914/` and matching log. This was
workload/documentation-only work: no engine tests or formal performance suite
were rerun, and no previous result was replaced.

##### Stage 4 Hired Guns live host check (2026-09-14)

The existing CopperScreen Release build was launched explicitly with
`--engine Lightweight --profile lightweight-a500-kickstart13`, native
Kickstart 1.3 and the supplied read-only disk1 ZIP. No engine, host source,
profile file or default selection was changed. Engine SHA256 remains
`010B40B66A2D114B0C99009DE20AD8EBE098CF926BA323D49268477C7EA39579`;
host DLL SHA256 is
`BF5654FDDB3EED8B870989098BDF8D5A3C20D8C471E766DF68FC38D659A102E9`.
The test process was placed at Normal priority; this is not a formal FPS run.

Live window observations establish native Workbench boot, opening the disk and
game icons, the user-approved SystemTakeover confirmation, English Continue,
disk decompression, introductory graphics/animated credits and MAIN MENU after
Space. The host reports a 908x626 presentation buffer. Evidence is the desktop
captures in this task and `CopperScreen-20260914-230412-146788.log` under
`%LOCALAPPDATA%/CopperScreen/Logs`; no new production tracing was enabled.
At menu fields30017-30376 the existing log reports zero audio-submit failures,
zero dropped/skipped/buffer-dropped frames and a serviced audio queue. This
does not establish audibility, sound quality, sustained presentation rate or
a replacement for the controlled engine throughput result.

The app uses captured relative mouse motion, not absolute guest coordinates.
Small movements and a longer button press reached the dialogs; some very short
automated clicks did not register. Automated F1 taps at the settled main menu
also did not advance it. A manual half-second F1 press was requested to separate
tool input from host/guest behavior. This is an OPEN observation, not a proven
keyboard defect; do not add timing changes or title-specific input workarounds
without evidence. Live training, disk2/disk3 swapping and movement remain
unverified in this session; their independent headless replay remains valid.
No new tests or benchmarks were run for this live-check/documentation slice.

Follow-up, 2026-09-15: the user reports that manual F1 advances a menu, but
the next F1 appears to enter an infinite busy loop. The process had closed
before investigation. Its saved log continues publishing frames through
176861 inside `$C09940-$C0997A`, identified in the native program as the
ordinary player-count keyboard wait. A bounded unchanged-engine replay
reaches the same loop at field9625 and exits on the next F1, reaching the
Training menu at field9950 and Exploration loading at field10575. Even
same-cycle press/release input advances settled menus in that replay, so the
earlier short-tap explanation is not established. Live input/presentation
remains unresolved, not dismissed as user error or declared repaired.
See LWA-HOST-001 and `.codex-tmp/hiredguns-key-interval-20260915/` for evidence.
No production code, timing contract, default selection or performance gate
changed; only a temporary external probe and evidence documents were updated.

##### Stage 4 pre-Workbench graphics investigation (2026-09-15)

The user redirected the live menu retest to graphical glitches during boot,
before Workbench. The repaired-host session is paused at Workbench; the F1
and window-responsiveness retest remains open, not silently counted as passed.

**Finding:** the unchanged frozen HAM engine reproduces a narrow garbage
sprite in raw field750. An isolated bounded trace shows sprite channel2
accepting an old descriptor address on line0, then completing that request
after Copper writes the intended null-sprite pointer. The completion replaces
the newly written pointer; repeated fields drift into unrelated RAM, loading
`AAAA` as sprite position/control/data. This is a missing sprite-DMA vertical-
blank startup restriction, not a host rendering or HAM regression.

**Causal check:** the diagnostic source copy matches the original field750
framebuffer and Chip RAM before changing behavior. Suppressing only new sprite
requests on lines0-24 in that copy removes exactly2180 garbage pixels, with
every other field750 pixel unchanged. The later field2400 Workbench image is
byte-identical to the original. No production engine, CPU or host changes were
made for this investigation. This experiment is not a complete hardware-timing
fix, a performance measurement, or a suite/gate acceptance.

Record: [LWA-VIDEO-006](LIGHTWEIGHT_A500_ENGINE_ISSUES.md#lwa-video-006--garbage-sprite-during-native-hired-guns-pre-workbench-boot)
contains the physical sequence, source authority, hashes and local artifacts.
Next bounded implementation is sprite vertical-blank/reset-line handling,
with legal-slot tests and native boot regression. Preserve pending physical
transactions and avoid title-specific suppression or cached pointer resets.
Existing line0 descriptor tests encode the flawed startup assumption and need
corrected scenarios; they are not evidence that line0 fetching matches hardware.
All previous accepted milestones and unresolved host-input confirmation retain
their recorded dispositions.

##### Stage 4 sprite vertical-blank repair (2026-09-15)

The user authorized implementation with a performance-regression check.
The retained change is a direct PAL line0-24 address-input restriction in
`LightweightSpriteDma`; line25 and later retain their fixed physical slots.
The existing accepted-output path remains ahead of new-request arbitration.
No generic scheduler, cached pointers, tracing, additional state or allocations
were introduced. CPU, runner and media-decoder assemblies are byte-identical
to the frozen HAM reference; only the engine assembly changes:
`61DB639BE38769F2E85D90E89C649C5560B550EF8D21C0147AA6663DF04FA81B`.
Frozen candidate: `.codex-tmp/lightweight-sprite-vblank-20260915/`.
CopperScreen Release rebuild succeeds and contains this exact engine hash;
the next launch uses the repair. No live session was restarted or engine
selection changed. The existing NuGet vulnerability warning is unchanged.

**Correctness/replay:**368 Lightweight tests PASS, no skips, including the
previously failing early-blank rewrite and both PAL field-wrap regressions.
Late enable/accepted completion after disable is covered. The older fixed-slot
tests now start on legal line25. Native Hired Guns boot750 is stripe-free,
Workbench2400 is byte-identical to the original, menu9000 and training20648
still complete. Native Lemmings14520 reaches the same running-level scene
with active audio. See LWA-VIDEO-006 for hashes, changed-output limits and
unverified sprite-comparator edges; this is not exhaustive sprite correctness.

**Initial performance attempt: INVALID / RERUN, not PASS or a demonstrated
regression.** The paired same-work attempt used freshly verified P-core CPU2,
mask4, sibling3, Normal priority and Balanced power plan. Topology and build/
input hashes are in `.codex-tmp/sprite-vblank-performance-20260915.log`.
Other build/test workloads were running throughout the observed attempt;
early telemetry recorded roughly79-94% aggregate activity and heavy selected-
core/sibling activity. ReferenceR1 was deliberately stopped before completion
rather than spending the full six-sample series on invalid measurements. The
wrapper's workload-failed exit is from that stop, not an emulator failure.
No fresh FPS or measured native-allocation result is claimed.

Prepared retry: `.codex-tmp/measure-sprite-vblank-20260915.ps1`, derived from the
repository native-retention protocol without changing the historical script or
goldens. It preserves input hashes,600 gameplay-warmup fields,3600 measured
fields,three samples/build,interleaved order,full pixels/48kHz stereo,zero
measured allocation requirement,per-build determinism and host/spread checks.
It permits cross-build timing fingerprints to differ for this demonstrated DMA
correction; native replay images are checked separately. Reference directory
is `.codex-tmp/lightweight-ham6-20260914/`; use the candidate above and a fresh
evidence directory. Existing accepted performance results remain historical
evidence, not certification of this repaired build. Default/Legacy routing and
the separate pending live host-input confirmation remain unchanged.

##### Stage 4 sprite-repair performance retry (2026-09-15)

**PASS:99.083% active-workload retention;327.40 FPS /3.05437 ms per field.**
No engine/source/build changes were made during this verification. The exact
repaired engine already copied into CopperScreen was compared with frozen HAM.
The previous interrupted attempt remains INVALID / RERUN; this is a new,
complete clean series, not a relabeling or selection of its samples.

| Build | FPS samples | Median FPS | Spread | ms/field |
| --- | --- | ---: | ---: | ---: |
| Frozen HAM reference |333.73,330.43,323.71|330.43|3.03%|3.02636|
| Sprite vertical-blank repair |323.99,327.40,327.74|327.40|1.15%|3.05437|

The repaired median is0.917% lower (+0.02801 ms/field), within the observed
overlapping sample ranges. Do not call this proof of identical speed or a
measured improvement. It comfortably passes the existing active95% floor and
the200 FPS target, with1.94563 ms remaining to the5 ms budget. A further profile
is not required by this regression check. Rendering, CPU, DMA and48kHz stereo
audio all remain included; host presentation and device pacing remain excluded.

Protocol: same native Kickstart1.3/Lemmings script and media hashes; checkpoint
10320 followed by600 gameplay warmup fields; measured fields10921-14520,
3600 per sample. OrderR1,C1,C2,R2,R3,C3. All six allocate zero measured bytes,
complete with no unsupported feature, and repeat their within-build fingerprints.
Cross-build fingerprints differ as allowed for the demonstrated DMA correction;
the preceding native visual/gameplay replays remain the functional evidence.

| Build | Final cycle | CPU | Hardware | Output |
| --- | ---: | --- | --- | --- |
| Reference |2063321672|`E9ED169188EABAE5`|`A8DFE876EDDBBF09`|`514760AD9BC62BCE`|
| Repaired |2063321634|`6AE7090DA8AFB7E3`|`334A819F6FDFB1CA`|`C65F87325946E5DA`|

Host checks PASS throughout preflight, execution and cooldown. Fresh Windows
CPU-set topology identifies logical CPU2 as efficiency-class1 P-core, affinity
mask4, protected SMT pair2/3; E-cores16-23 have class0. Priority Normal, Balanced
plan GUID381b4222-f694-41f0-9685-ff5bb260df2e, Windows10.0.26200.0,
.NET SDK10.0.303. Placement, competing workloads and sustained core/sibling/
aggregate load were checked using policy v2, with no invalid interval reported.

Build identities: reference engine
`010B40B66A2D114B0C99009DE20AD8EBE098CF926BA323D49268477C7EA39579`,
candidate engine
`61DB639BE38769F2E85D90E89C649C5560B550EF8D21C0147AA6663DF04FA81B`.
The CPU/runner/decoder assemblies are unchanged between builds. Recorded checkout
HEAD`e9a1ef47444c87da626ed01a62bad4d1670e9d97` is context only; the frozen
assembly hashes identify these dirty-tree builds.

Evidence: `.codex-tmp/sprite-vblank-performance-20260915-r2.log`, SHA256
`66E88AD856849B7DCBC5305B149E603583177143436B16A9ED6B8987A02189FD`.
Runner script `.codex-tmp/measure-sprite-vblank-20260915.ps1`, SHA256
`1128CD04E6125E76FED58A4C08B3DFB1EAD18466FE32778FDC2473196D461318`.
No tests were rerun for this measurement/documentation-only turn. The368-test
and native replay results remain applicable to the unchanged candidate. This
closes the sprite repair's throughput-retention check, not the separate live
host-input confirmation, open hardware edges or any default-engine cutover.

##### Stage 4 sprite-repair cost investigation (2026-09-15)

The initial pair (333.73 ->323.99 FPS) was2.92% lower; the complete valid
six-sample comparison above was0.917% lower at the medians, or28.01 us/field.
These are not interchangeable results. The overlapping ranges do not prove
zero implementation cost, nor do three samples isolate a sub-percent cause.

Separate source-copy diagnostic builds counted work over the same native
Lemmings warmup10920/measurement3600 interval. Only the early-blank guard differs
between their old/repaired behaviors. Both link the frozen CPU/decoder DLLs.
Two diagnostic pairs, the second adding channel-level counts, reproduce the
respective frozen builds' final cycle/CPU/hardware/output fingerprints exactly.
No production source, CPU, runner binary, allocation golden or acceptance result
was changed. Diagnostic FPS/allocation figures are not acceptance evidence:
counters perturb execution, and their post-timer JSON reporting allocates before
the diagnostic runner reads its allocation total.

| Measured work over3600 fields | Old behavior | Repaired behavior |
| --- | ---: | ---: |
| CPU instructions (batch count zero in both) |32,524,707|32,512,379|
| Sprite input-slot probes |18,028,800|18,028,803|
| Early-blank returns |0|1,440,003|
| Exhausted-channel returns |14,788,800|10,886,400|
| Sprite POS words |36,000|43,200|
| Sprite CTL words |36,000|43,200|
| Sprite DATA words |158,400|316,800|
| Sprite DATB words |158,400|316,800|

**Real changed work:** sprite output transfers average108 ->200 words/field
(+85.19%); image-data transfers double88 ->176, while control transfers rise
20 ->24. DATA words by channel0..7, averaged per field, change
8/12/0/0/12/12/0/0 ->16/24/0/0/24/24/0/0; DATB counts are identical to DATA.
This is additional activity in the same four channels, not extra channels.
Every CTL/DATA/DATB completion also queues the existing Denise input and dirties
its sprite registers, so extra work extends beyond a RAM read. The removed
premature fetches therefore did not merely steal unnecessary cycles: the two
behaviors execute different later sprite transfers. Reading a stale terminator
before the game's blank-period pointer rewrite can exhaust a channel for the
field (pointer writes do not clear Exhausted); that is consistent with these
counts, but this investigation did not trace each Lemmings descriptor to prove
the exact field-by-field cause. CPU instruction count decreases0.0379%;
an increase in instruction count does not explain the slowdown.

**Small coordination overhead:** the repair checks BeamLine before even the
exhausted-channel exit, about5008 times/field. It still schedules about400
input probes/field on prohibited lines0-24 and then rejects them. This exposes
a bounded optimization candidate: arrange the next eligible input directly at
the legal blank-end slot, preserving DMA enable/pointer writes, variable field
length and any already accepted output. That is not permission to skip active
DMA, cache requester schedules or undo the correction; no such change was made.

Optimized JIT inspection of the original frozen binaries retains inlining in
both SpriteDma.Step bodies. Step is881 ->907 bytes; standalone TryAcceptInput
562 ->577; ScheduleInputAfter stays165. The new load/compare/branch is visible;
there is no lost inlining, new helper call or large code-size expansion in this
path. Register allocation/block layout also changes, so exact cost cannot be
assigned from assembly size.

Conclusion: the omitted consideration in the first performance explanation was
**more real sprite execution after the timing repair**, alongside the per-slot
guard. The28 us median difference cannot be numerically divided between those
costs and host/JIT variation from these probes. Keep the valid99.083% retention
PASS and the repair; consider the blank-period no-op scheduling separately.

Evidence: isolated projects/scripts under
`.codex-tmp/sprite-cost-work-20260915/` and
`.codex-tmp/sprite-cost-counts-20260915.ps1`; original-binary disassemblies
`.codex-tmp/sprite-cost-reference-jit-20260915.txt` and
`.codex-tmp/sprite-cost-repaired-jit-20260915.txt`.
Counters are confined to those temporary builds, not the release engine.

##### Stage 4 direct sprite blank-end scheduling (2026-09-15)

Implemented the bounded optimization authorized after the cost investigation.
`LightweightSpriteDma` retains one earliest-input cycle for the current field,
at PAL line25/h=$17. Enable/pointer writes clamp their scheduling input;
field restart assigns the first eligible cycle directly. Thus both go to the
first legal input instead of visiting400 prohibited slots per313-line field.
The ordinary `ScheduleInputAfter` body is unchanged: no new hot-path clamp.
The per-input blanking branch is removed, and BeamLine is read only after the
exhausted-channel exit. No active transfer, accepted address/output phase,
Denise latch, sprite comparator, CPU timing, rendering or audio is omitted.
No schedule cache, generic event framework, per-cycle diagnostics or allocation
is added. One8-byte scalar records the physical field eligibility boundary.

Reset review caught an edge in the initial candidate: CPU peripheral RESET
does not reset the beam. Both full and peripheral reset paths now pass the
live clock's FrameStartCycle into SpriteDma.Reset, so blank eligibility is
correct after a nonzero/short-field origin as well as cold reset. This changes
two reset call sites in the machine adapter, not Copper68k. The initial
comparison was deliberately stopped during referenceR1 before any FPS result;
`.codex-tmp/sprite-blank-schedule-performance-20260915.log` is an incomplete,
superseded attempt, not throughput acceptance or an emulator failure.
An intermediate reset-correct candidate still clamped every slot; its series
was stopped during referenceR2 to move that clamp to the cold rearm paths.
The incomplete `sprite-blank-schedule-performance-final-20260915.log` contains
R1=340.07,C1=335.31,C2=343.65 FPS with matching fingerprints/zero allocation;
it is not a completed comparison and contributes no accepted samples below.

Validation: **375 Lightweight tests PASS, no skips.** The new blank-scheduling
assertions failed in four cases before the optimization; the early-blank
peripheral-reset case failed against the first candidate and passes after the
reset correction. Coverage includes enable immediately before/on/after the
first input, mixed312/313/312 fields, full/peripheral reset, pointer rewrites,
accepted completion after disable, fixed channels/slots and stolen slots.
TRX: `CopperMod.Amiga.Lightweight.Tests/TestResults/sprite-blank-rearm-20260915.trx`.
The build retains the pre-existing NU1902 warning; no package was changed.

Native Hired Guns replay through20648 fields matches the repaired reference
exactly at750,2400,9000,20647 and20648: all15 BMP/JSON/Chip-RAM hashes match.
The last checkpoints include active sound and training state. This preserves
the pre-Workbench repair and menu/training behavior, without claiming a new
live host-input confirmation or resolving the existing hardware uncertainties.
Evidence: `.codex-tmp/hiredguns-sprite-scheduled-probe/rearm-training/`.

Final candidate engine SHA256
`139EDEA68A38B60E0430DDF64A9EDABCE9E1C9EE900FDD65C142D6207516A905`;
frozen directory `.codex-tmp/lightweight-sprite-blank-rearm-20260915/`.
Reference is the repaired sprite build `61DB639B...04FA81B`, not the older
HAM build that executes fewer sprite transfers. CPU, runner and decoder DLL
hashes remain identical. CopperScreen Release was rebuilt and contains that
same engine hash and the unchanged Copper68k hash. No host routing/default
selection changed, and no GUI session was launched for this optimization.

**Performance PASS: 335.05 FPS / 2.98463 ms per field; +1.823% median FPS.**
The completed fresh six-sample comparison preserves all corrected sprite work,
CPU execution, full pixels and48kHz stereo PCM. It saves0.05442 ms/field against
the repaired reference median; this is a modest measured gain, not a claim
that every future host sample will improve by exactly this percentage.

| Build | FPS samples | Median FPS | Spread | ms/field |
| --- | --- | ---: | ---: | ---: |
| Repaired sprite reference |330.25,329.05,321.52|329.05|2.65%|3.03905|
| Direct blank-end scheduling |332.87,340.17,335.05|335.05|2.18%|2.98463|

All six have zero measured allocation, unsupported=none, and the same final
cycle2063321634 / CPU`6AE7090DA8AFB7E3` / hardware`334A819F6FDFB1CA` /
output`C65F87325946E5DA`. The wrapper requires these exact repaired-reference
fingerprints in both builds; it does not permit a correctness trade for speed.
Same native ROM/media/script hashes, checkpoint10320,600 gameplay warmup,
3600 measured fields10921-14520; orderR1,C1,C2,R2,R3,C3. No partial sample from
either earlier implementation is reused. Active-workload retention101.823%
passes the existing95% floor and200 FPS target independently of correctness.

Host policy v2 and spread checks PASS throughout preflight, all six runs and
cooldowns. Fresh CPU-set topology verifies P-core logical CPU2/mask4 with
SMT sibling3; efficiency class1 on0-15, class0 on16-23. Priority Normal,
Balanced plan381b4222-f694-41f0-9685-ff5bb260df2e,24 logical CPUs,
Windows10.0.26200.0, .NET SDK10.0.303. No build/test/native replay was run
alongside this final series. Frozen assembly hashes identify the dirty-tree
builds; HEAD`e9a1ef47444c87da626ed01a62bad4d1670e9d97` is context only.

Evidence: `.codex-tmp/sprite-blank-rearm-performance-20260915.log`, SHA256
`068D28E62878D6707F2DF7373828E870CA0D04E0462E737F2D4EA11EE087C04E`.
Script `.codex-tmp/measure-sprite-blank-schedule-20260915.ps1`, SHA256
`CDE01EEAE74DA7C60B3CE4560E2A0774DF02B4FFDCD25C889C206A5DA2145C57`.
Retain this bounded optimization. Existing hardware uncertainties and live
host-input confirmation remain open; this is not a default-engine cutover.

##### Stage 4 host input starvation repair (2026-09-15)

The user clarified that the entire CopperScreen window became unresponsive,
not merely the guest menu. The previous headless replay does not clear that
host failure. Inspection found the coalesced presentation callback repeatedly
posted at Avalonia Render priority, above Input. Under sustained presentation
backlog this path bypasses the dispatcher's native-input yielding protection.
That explains how toolbar heartbeat logging can continue while Windows input
does not run; the incident itself has no preserved UI-thread dump.

Changed only the existing presentation callback priority to Background. Its
coalescing and frame ownership are retained. No CPU/chipset/device changes,
extra scheduling framework, timing workaround, omitted output or default switch.
The real-dispatcher regression test queues input during a bounded64-frame
replenished stream: old priority defers input until64 frames; new priority
handles it after1 and still executes all64. The production-priority case fails
before the correction; both it and the old-priority negative control pass after.
Focused host tests pass74/74; the2 opt-in native tests initially skip without
their environment, then both PASS separately with the supplied native Kickstart
ROM, Lemmings disk1 and accepted level1 script. These cover14520-field native
gameplay/output and keyboard press/release through the host adapter. Combined
selected coverage:76 PASS, no remaining skips. No formal FPS sample was taken.

Build completes with pre-existing NU1902 package vulnerability and unavailable
build-task `git` metadata warnings; dependencies were not changed as part of
this focused repair.

Rebuilt host DLL SHA256:
`A239A06515DFAAF0E5913D76472429041845E0EC629A587CF4A267C25841C531`.
Engine `010B40B6...39579` and Copper68k `F9C16D97...9FB3` hashes are unchanged.
This is a host responsiveness repair, not a new engine FPS result. Full live
Hired Guns confirmation remains OPEN. LWA-HOST-001 records source authority,
the user's clarification and the remaining check.

##### Stage 4 interlaced CRT-phosphor blackout repair (2026-09-15)

The user reported occasional pitch-black flicker while moving the Windows
pointer over an interlaced CopperScreen display. This is a host-presentation
interaction, not a raw Lightweight field or chipset-timing failure. The
input-starvation repair posts emulator-field presentation at Background
priority, while the independent CRT animation callback continued applying a
24 ms half-life from host wall time. Pointer input could defer fresh field
consumption long enough for that callback to decay the composed image to black.

`CrtPhosphorComposer` now limits decay from each submitted field to that
field's expected PAL/NTSC duration. At the boundary the image is held and the
animation callback chain stops; a fresh submitted field establishes the next
decay interval and restarts animation. Presentation remains below Input
priority, and framebuffer ownership, emulated field generation, CPU/chipset
timing and audio are unchanged. The repair adds no steady-state allocation and
removes redundant full-frame phosphor packing/uploads during a delayed field.

The discriminating test holds output bit-identical between the 20 ms PAL
boundary and a one-second host delay, then proves that a fresh opposite field
restarts decay. Focused presentation/dispatcher coverage passes 8/8. The full
CopperScreen suite passes 348 with 11 optional external-media/ROM tests skipped
(359 total, 7m53s). The skip set includes opt-in native Hired Guns and native
Lightweight gameplay checks, so live pointer-movement confirmation remains
open and is not represented as an automated pass. See LWA-HOST-002.

Active H2 uses one direct device step at each CCK while a requester or raster
output is active. This is a bounded 227-CCK-per-line loop, not an event search:
there is no requester scan, deadline minimum tree, replay, row schedule or
cache. When Copper, bitplanes and non-black raster output are inactive, the H1
event-free path remains unchanged. H2a, H2b and H2c each stop for focused timing
and an inactive/active throughput comparison before the next slice is accepted.

#### H2a Copper gate (2026-09-12)

H2a implements a compact OCS Copper decoder with one retained address input
and one following Chip-RAM output phase. The first and second words are sampled
at their accepted output cycles; MOVE uses the second-word output cycle and
enters the same custom-register owner as CPU writes. WAIT and SKIP compare the
live beam after their physical idle/control stages. COPJMP invalidates decoding
without cancelling an already accepted RAM output. Copper output slots directly
exclude CPU grants; refresh remains a concurrent internal Copper-control phase,
not a Copper output grant.

Five focused Copper tests cover the two word phases, CPU contention with a
simultaneous Copper input, sleeping WAIT wakeup, SKIP/MOVE versus WAIT behavior,
and COPJMP retention. The complete lightweight Release suite passes **37/37**.
The expected phase sequence is grounded in the repository's configured OCS PAL
physical-ledger evidence (`CopperDmaPhaseTests` and
`CopperWaitPhysicalPipelineTests`); Legacy equality is not the oracle.

The inactive comparison used the frozen accepted H1 runner followed by the H2a
candidate in interleaved order. Each process used logical CPU 2 / mask 4,
Normal priority, 600 warmup and 3,600 measured fields. Full host-load-policy-v2
telemetry was not collected, so this remains an intra-gate diagnostic series.

| Inactive build | Samples (FPS) | Median | Spread | Allocation |
| --- | --- | ---: | ---: | ---: |
| Frozen H1 | 6722.56, 6763.67, 6756.29 | 6756.29 | 0.61% | 0 bytes |
| H2a Copper inactive | 6694.07, 6761.82, 6729.62 | 6729.62 | 1.01% | 0 bytes |

H2a retains **99.61%** of H1 on the inactive path. Both builds produced cycle
`596828400`, CPU fingerprint `7A4ADB9B73247EED` and placeholder-output
fingerprint `3D49359399F6A383`.

The active loop repeatedly executes two palette MOVEs and COPJMP1. Its clean
fingerprinted samples were 1435.38, 1497.47 and 1509.64 FPS: median
**1497.47 FPS**, 4.96% spread, **0.6678 ms/field**, and zero allocation. Every
sample produced hardware fingerprint `2BDAE566083965B2` in addition to the
unchanged stopped-CPU and placeholder-output fingerprints. This deliberately
stressful loop costs about 0.519 ms/field over the H1 inactive control and
leaves approximately 4.332 ms of the final 5 ms budget before bitplanes,
pixels, audio, floppy and representative gameplay CPU work.

H2a dispositions are **Timing GO**, **Inactive host path GO**, **Active host
path GO / baseline frozen**, and **Cumulative budget GO**. H2a is accepted for
continuation to H2b; H2 as a whole remains in progress and production remains
unchanged.

#### H2b bitplane-input gate (2026-09-12)

H2b adds the OCS low-resolution DDF sequencer and six compact bitplane pointer
and data latches. DDF selects address-input opportunities in the HRM low-res
slot order `[BPL4, BPL6, BPL2, -, BPL3, BPL5, BPL1]` after the initial empty
slot; each accepted address samples Chip RAM on the following CCK. Pointer
writes after input cannot retarget the retained transfer, pointer advancement
occurs at output, and odd/even modulo is applied once in the terminal fetch
unit. Bitplane output wins a shared slot over Copper and CPU; the denied Copper
word remains local and retries at its next input phase. Disabling DMA retains
one accepted output and aborts later inputs. Enabling after DDFSTRT naturally
waits for the next rasterline comparator.

Seven focused H2b tests cover plane order/output phases, retained addresses,
terminal modulo, consecutive-output CPU contention, Copper priority/retry,
late enable and mid-run disable. The complete lightweight Release suite passes
**44/44**. Expected slot order and DDF behavior come from the repository's
hardware-backed OCS matrix and physical bitplane tests; no Legacy-equality
claim is used as proof.

The inactive diagnostic again interleaved the frozen H1 binary because the H2a
runner binary was not preserved before H2b editing. This is recorded as a gate
process defect, not hidden as an exact preceding-build comparison. The H2a
inactive median was 6729.62 FPS; the transitive frozen H1 control below is the
stricter available artifact. H2b itself was frozen immediately after this gate
for the H2c comparison.

| Inactive build | Samples (FPS) | Median | Spread | Allocation |
| --- | --- | ---: | ---: | ---: |
| Frozen H1 | 6713.52, 6739.63, 6754.20 | 6739.63 | 0.60% | 0 bytes |
| H2b bitplanes inactive | 6618.62, 6666.58, 6620.72 | 6620.72 | 0.72% | 0 bytes |

H2b retains **98.24%** of frozen H1 and 98.38% of the recorded H2a inactive
median. CPU and placeholder-output fingerprints remain
`7A4ADB9B73247EED` and `3D49359399F6A383`.

The prior active Copper cohort runs at 1566.08, 1452.36 and 1464.62 FPS:
median **1464.62 FPS**, 7.76% spread, zero allocation, retaining **97.81%** of
the accepted H2a active median. The new six-plane DMA-only cohort runs at
1267.67, 1271.13 and 1247.73 FPS: median **1267.67 FPS**, 1.85% spread,
**0.7888 ms/field**, zero allocation, hardware fingerprint
`517FEF643C2E0416`.

The first combined Copper+six-plane series was `INVALID / RERUN` because its
10.93% spread exceeded the fixed limit despite identical fingerprints. The
clean replacement is 942.26, 944.95 and 924.86 FPS: median **942.26 FPS**,
2.13% spread, **1.0613 ms/field**, zero allocation, hardware fingerprint
`2C8E7132DEA7B634`. It leaves approximately **3.9387 ms** of the final 5 ms
budget before pixel conversion, audio, floppy and representative gameplay CPU
work. Full host-load-policy-v2 telemetry remains deferred to the required H2
closing series.

H2b dispositions are **Timing GO**, **Inactive host path GO**, **Prior active
path GO**, **New active/combined path GO / baselines frozen**, and **Cumulative
budget GO**. At this checkpoint H2b was accepted for continuation to H2c and
H2 remained in progress; the H2c close below supersedes that interim status.

#### H2c direct-output gate (2026-09-12)

H2c adds two reusable 454x313 buffers for the uncropped PAL raster. One buffer
is rendered while the preceding completed field remains available to the host;
the arrays exchange ownership at the field boundary. The direct path consumes
the bitplane data outputs already established by H2b, retains six Denise data
latches/intermediate latches/shifters, applies BPLCON0/BPLCON1 and DIW inputs
one CCK after their bus writes, and consumes palette writes at their physical
output CCK. Low-resolution normal and six-plane EHB output are implemented.
Fixed blanking and the display window affect visibility without stopping the
serial shifters. LACE alternates 313/312-line fields. Hires, HAM,
dual-playfield and BPU=7 output latch an unsupported-result reason; returning
to a supported mode does not erase that evidence.

Eight focused output tests cover the uncropped/reusable host shape, fixed
blanking and COLOR00 border, low-resolution DMA reload and 16-pixel drain,
DIW clipping without shifter rephasing, same-CCK palette visibility, manual
BPL1DAT staging, LACE field cadence and sticky unsupported-mode reporting. The
complete lightweight Release suite passes **52/52**. The output counter origin
and palette-write phase follow the repository's hardware-backed
`Display.GetOcsPaletteOutputX` and physical-presentation tests; Legacy image
equality is not used as the oracle.

The first complete raster implementation produced a 435.46 FPS traced run.
The trace and source audit found avoidable work in the pixel loop: palette
conversion and DIW decoding were repeated for every pixel, plane extraction
used a generic loop, and beam coordinates were recomputed from a 64-bit cycle
division. H2c now holds direct converted palette latches, decoded DIW bounds,
unrolled six-plane shifter extraction and incremental beam coordinates. This
does not cache schedules or skip output. The same active output fingerprint
then measured 532.10 FPS in the immediate 600/3,600 diagnostic run.

The closing series used an identical runner binary in both directories so the
H2b and H2c builds received the same standard PAL six-plane pattern, palette,
Copper loop and small output checksum. It was a formal host-load-policy-v2
series: logical CPU 2 / mask 4, core 2 with protected sibling 3 / mask 12,
efficiency class 1, Normal priority, Balanced power plan, 600 warmup and 3,600
measured fields. Each inactive and active cohort used order
candidate/reference/reference/candidate/candidate/reference. The 10-second
preflight and every live interval were valid. Preflight package-other load
peaked at 8.9%; live selected-other, sibling and package-other readings peaked
at 15.5%, 0.7% and 3.6%, respectively, all below policy limits.

| Cohort/build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| H2c inactive | 13336.10, 13226.80, 13245.17 | 13245.17 | 0.83% | 0.075499 ms |
| Frozen H2b inactive | 12621.94, 12545.99, 12567.16 | 12567.16 | 0.60% | 0.079572 ms |
| H2c complete raster | 565.84, 517.07, 560.07 | 560.07 | 8.71% | 1.785491 ms |
| Frozen H2b requester-only | 1000.20, 1003.81, 903.88 | 1000.20 | 9.99% | 0.999800 ms |

H2c retains **105.40%** of H2b on the inactive path. The active comparison is
not a 95% retention test because H2c deliberately adds every output pixel to
the preceding requester-only build; 560.07 FPS is the first accepted H2c
active baseline. The completed H2 path is **2.80x** the final 200 FPS target at
this incomplete-machine point and consumes 1.785491 ms of the 5 ms budget,
leaving **3.214509 ms** for blitter, sprites, Paula/audio, floppy and
representative gameplay CPU work. This is headroom, not a forecast that those
costs are additive.

All samples completed at cycle `596828400`, used CPU fingerprint
`7A4ADB9B73247EED`, and allocated zero steady-state bytes. H2c active used
hardware/output fingerprints `976EDE65773CFA00` / `88D1006FB18B85D4`; H2c
inactive used `6A26103B37A801F6` / `BF2D06EE5CCE0B83`. The H2b active
reference used `976B7A65773A1A3D` / `CAD459C571858383`. Build identity was:

- identical runner SHA-256
  `0C9E78AF78FD043E49C3C33FDA1EB06CA246DC0A7903EE6C82E5B3C9ABBFA865`;
- H2c engine SHA-256
  `A91894CD025AA1C3226C6C7B52D5797845359C2C6EB24BC4334D830033E54F5B`;
- frozen H2b engine SHA-256
  `F58D99FA0C5EA7A64E6BBAD12316C6E4D4F21444A6C0C456C05E7C1C763E8CAC`;
- shared Copper68k SHA-256
  `CC033EE4E073BF2F1AC44739F1B291CA868D2D67661C52AD7352DFD469D6286F`.

The measured H2c family is frozen at
`.codex-tmp/lightweight-h2-accepted-20260912`; the directory contains the
exact engine and runner assemblies identified above.

H2c dispositions are **Timing GO**, **Inactive host path GO**, **Active host
path GO / baseline frozen**, and **Cumulative budget GO**. Overall **H2 GO**.
This authorizes opening H3 only; it does not claim a complete machine, native
Lemmings execution, 200 FPS completion or a production cutover.

The HRM-backed reset/default contract is a 313-line PAL long field. VPOSR LOF
reports the selected field and VPOSW may select a 312-line short field. H1 does
not invent unconditional 313/312 alternation: BPLCON0 LACE and its automatic
field relationship are verified with H2 display behavior.

### H1 evidence and disposition (2026-09-12)

The timing sources are recorded separately from compatibility comparisons:

- Commodore's [*Amiga Hardware Reference Manual*](https://www.ikod.se/wp-content/uploads/2020/08/Amiga_Hardware_Reference_Manual_3rd_Edition.pdf),
  beam-position table 7-5 and [Appendix F](https://www.amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node012E.html),
  define VPOSR/VPOSW LOF/V8, the CIA-A/CIAB byte-lane maps, CIA-A DDRA
  initialization, timer registers and INT2/INT6 routing.
- The [MOS/Commodore 8520 specification](https://studylib.net/doc/27757407/cia--318029-01-02-03--specification---commodore)
  defines DDR pin selection, timer A/B modes and ICR mask/read-clear behavior.
- Existing hardware-backed repository evidence supplies the internal refresh
  coordinates (`AgnusHrmOcsSlotTable`), OCS VERTB phase
  (`OcsVerticalBlankInterruptPhaseTests`) and 68000 two-word transfer phase
  contract. Legacy equality was not used as proof.

The final Release suite passes **32/32** focused tests. H1a covers monotonic
beam state, long/short PAL field boundaries, VPOSR/VHPOSR, refresh contention,
word completion and separated longword phases across line/frame boundaries.
H1b covers INTENA/INTREQ set/clear, delayed CPU visibility, VERTB at frame start
+ 2 CPU cycles, running/STOP interrupt recognition and autovectors. H1c covers
CIA byte lanes, the 10-CPU-cycle peripheral grid, reset PRA/DDRA, ports, timer
A/B continuous/one-shot and chained modes, ICR and CIA-to-custom interrupt
visibility.

The closing audit corrected five defects before acceptance: CIA-A now resets
to PRA `$FC` / DDRA `$03` while reset overlay remains asserted; timer B retains
its clock phase when changing from timer-A-underflow input to CPU ticks;
late VPOSW shortening cannot publish a frame boundary in the past; and
disconnected CIA byte lanes neither mutate the chip nor pay a connected-lane
E-clock wait; and SET/CLR registers no longer retain write-only/read-only or
reserved bits.

The inactive comparison was run in interleaved H0/H1 order with the frozen H0
binary. Each process used logical CPU 2 / mask 4 (the verified P-core mapping
for this host), Normal priority, Balanced power plan, 600 warmup frames and
3,600 measured frames. Both builds produced cycle `596828400`, CPU fingerprint
`7A4ADB9B73247EED`, output fingerprint `3D49359399F6A383`, and zero
steady-state allocation. Full host-load-policy-v2 telemetry was not collected,
so this is a diagnostic gate rather than a formal production measurement.

| Inactive STOP build | Samples (FPS) | Median | Spread | Median frame time |
| --- | --- | ---: | ---: | ---: |
| Frozen H0 | 6528.61, 6495.44, 6546.15 | 6528.61 | 0.78% | 0.1532 ms |
| H1 | 6648.55, 6709.17, 6695.57 | 6695.57 | 0.91% | 0.1494 ms |

H1 retains **102.56%** of the frozen median, above the 97% requirement.

The active CIA timer workload was interleaved with its inactive STOP control.
It produced 6020.18, 6211.64 and 6241.21 FPS: median **6211.64 FPS**,
3.56% spread, **0.1610 ms/frame**, zero allocation, cycle `596828412`, CPU
fingerprint `C279ABF64F46E389` and the same deterministic placeholder-output
fingerprint. Against that series' 6695.57 FPS STOP median, active CIA service
costs approximately **0.0116 ms/frame**. This freezes the H1 active baseline.

The synthetic H1 path therefore uses about 0.1610 ms of the 5 ms budget and
leaves 4.8390 ms unspent. That is a ledger entry, not a complete-machine
forecast: Copper, display composition, sprites, blitter, Paula, floppy and
native gameplay CPU work are absent.

H1 dispositions are **Timing GO**, **Inactive host path GO**, **Active host
path GO / baseline frozen**, and **Cumulative budget GO**. Overall **H1 GO**;
At this historical H1 close, H2 remained WAIT until its explicit gate
transition.

### Per-gate stop/go contract

Every H gate records independent dispositions for:

- **Timing:** focused synthetic tests must establish the implemented physical
  phases and profile. Legacy differences are investigation signals, not an
  oracle. Uncertain edges stay in the defect register.
- **Inactive host path:** an interleaved comparison against the preceding
  accepted build must retain at least 97% of its median and allocate zero
  steady-state bytes. A regression above 3% requires profiling; 5% or more is
  STOP until explicitly resolved or accepted with evidence.
- **Active host path:** the new slice receives a deterministic representative
  workload and fingerprint. Its first accepted implementation freezes the
  active baseline; later work must retain at least 95% unless a measured,
  required hardware behavior explains the cost and the 5 ms ledger still
  closes.
- **Cumulative frame budget:** report milliseconds per frame, not FPS alone,
  for CPU execution, clock/arbitration, the new device, framebuffer/audio work
  and the remaining distance to 5 ms. Device costs are not assumed additive
  because contention can reduce retired CPU work.

Timing GO never waives a host-path STOP, and host-path GO never waives a timing
failure. If either side stops, implementation remains at that gate while the
new slice is profiled and simplified; the next device is not connected.
Fingerprints are compared within the same stage and workload. A deliberate
hardware correction may change them, but the new value must remain
deterministic.

Short pinned Release profiles are sufficient inside a gate. Formal
host-load-policy-v2 series are required after H2, at the first complete native
Lemmings run, at an early production switch decision, and at the 200 FPS final
target. Invalid host samples neither advance nor reject a gate.
