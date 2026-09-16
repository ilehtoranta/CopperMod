# Where did the 450 FPS go?

Date: 2026-09-14. User requested a frozen/current same-work comparison,
followed by separate measurements of the added features. Measurement-only
investigation: no engine, CPU, device or production-selection code changed.

## Finding

**The current engine still runs the old workload at approximately 450 FPS.**
Using the original H6b1 runner on both engines, the current engine achieved
445.31 FPS versus the frozen engine's 447.43 FPS: 99.53% retention. The
0.47% difference is smaller than the observed sample spreads; this does not
establish a material same-work regression.

Using the current engine and current runner throughout the subsequent cost
ladder, the old workload achieved 449.28 FPS and the fully enabled hires
fixture 393.89 FPS. The extra work adds about **0.313 ms/field**. Approximately
60% of that observed increment belongs to the two wider-display steps,
28% to enabling disk RAM DMA, and 12% to input replay. TOD's measured
increment is too small to distinguish confidently from noise.

These are conditional workload differences, not independently additive
device execution times or proof that the cost is irreducible. DMA contention
and the input fixture's bus accesses also change how other devices execute.

## Original workload on frozen and current engines

The original 458.28 FPS observation came from the H6b1 active serial-input
fixture: synthetic STOP-based CPU, Copper, bitplanes, sprites, blitter, Paula
audio and ideal-ADF serial input; 454 x 313 pixels and real stereo PCM.
Disk RAM DMA, active TOD counters, input replay and hires were not enabled.

For this rerun, copied the original frozen directory to a new bridge directory
and replaced only its engine DLL with the current frozen opt3 DLL. The old
runner, CPU and media library are byte-identical across this comparison.
Original frozen directories were not overwritten.

| Engine with original runner | Samples FPS | Median | Spread | ms/field |
| --- | --- | ---: | ---: | ---: |
| Current opt3 | 449.54, 445.26, 445.31 | 445.31 | 0.96% | 2.2456 |
| Frozen H6b1 | 447.43, 434.86, 458.66 | 447.43 | 5.32% | 2.2350 |

All six samples exactly match cycle `554197982`, CPU `4EC69AC79128F103`,
hardware `EC881A45051BB113`, output `BF988BEFECE96765`, 142,102 pixels,
real PCM and zero allocation. No unsupported feature is reported. This also
matches the original recorded H6b1 execution fingerprints.

## Current-engine cost ladder

Each row adds to the preceding current-runner row. The separate original-runner
bridge checks that updating the runner did not change the base workload.

| Cohort | Workload | Samples FPS | Median FPS | Spread | ms/field | Increment ms |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| B | Original runner, serial-only | 447.48, 434.55, 446.53 | 446.53 | 2.90% | 2.2395 | — |
| S | Current runner, same serial-only work | 449.81, 449.28, 443.96 | 449.28 | 1.30% | 2.2258 | baseline |
| D | Add disk RAM DMA | 432.51, 432.43, 433.96 | 432.51 | 0.35% | 2.3121 | +0.0863 |
| T | Add CIA TOD counters | 430.54, 432.39, 438.67 | 432.39 | 1.88% | 2.3127 | +0.0006, unresolved |
| I | Add input replay | 425.29, 417.12, 427.28 | 425.29 | 2.39% | 2.3513 | +0.0386 |
| W | Expand lowres output to 908 x 313 | 402.49, 400.76, 404.19 | 402.49 | 0.85% | 2.4845 | +0.1332 |
| H | Switch to full-width hires | 394.45, 392.22, 393.89 | 393.89 | 0.57% | 2.5388 | +0.0542 |

B/S fingerprints match exactly. Their 0.62% median difference is smaller than
sample variation; there is no demonstrated runner regression or speedup.
All cohorts are deterministic within themselves, allocate zero steady-state
bytes, emit real PCM and report `unsupported=none`.

The W row doubles emitted pixels without changing lowres bitplane DMA or the
CPU/hardware fingerprints. H changes six lowres planes / 120 fetch slots to
four hires planes / 160 fetch slots and decodes four distinct hires pixels
per CCK. H-W therefore includes changed DMA/shifting/composition work, not
just framebuffer stores. Narrow output is not a substitute for correct hires.

| Cohort | Cycle | CPU | Hardware | Output |
| --- | ---: | --- | --- | --- |
| B/S | 554197982 | 4EC69AC79128F103 | EC881A45051BB113 | BF988BEFECE96765 |
| D | 554197982 | 4EC69AC79128F103 | A209A1FB2AAD014E | CC609FA86CAFD46B |
| T | 554197982 | 4EC69AC79128F103 | EEC2C96B7D692162 | CC609FA86CAFD46B |
| I | 554331342 | 3051D82B2D7994D3 | 3B959D7FDCC85DA3 | 6C6B7906B62638AE |
| W | 554331342 | 3051D82B2D7994D3 | 3B959D7FDCC85DA3 | AF2F22691E456C79 |
| H | 554319136 | E4D8C0149D9E61FD | 3FC545F94DE12D7B | 8A98C07297363F5D |

Every cohort finishes 3,900 total fields and emits 1,924 samples in its final
audio buffer. B through I emit 142,102 pixels; W/H emit 284,204. Fingerprint
equality across deliberately different workloads is not required.

## Protocol and evidence

A500 PAL OCS, 68000, 512 KiB Chip RAM + 512 KiB slow RAM. Frozen Release
assemblies, synthetic ROM and generated ideal ADF; no native ROM or gameplay
script is involved in these measurements. All emulated hardware is single
threaded. The Amiga timing skill kept the investigation observational: no
phase changes, logging hooks or omitted output were added to the engine.

- 300 warmup + 3,600 measured fields per process, three samples per cohort.
- Same-work series: C/R/R/C/C/R. Cost ladder:
  `B/S/D/T/I/W/H/H/W/I/T/D/S/B/T/I/W/H/B/S/D`.
- Verified topology: group 0, logical CPU 4, affinity 16, efficiency class 1,
  SMT sibling 5; Normal priority. This placement is host-specific evidence.
- Balanced power plan `381b4222-f694-41f0-9685-ff5bb260df2e`;
  20-second monitored preflight/cooldown and host-load-policy-v2 telemetry.
- No host-policy rejection was logged in either completed series; all
  spreads are below 10%. No effective-clock or temperature claim is made.
- Both series were explicitly run provisionally to honor the earlier
  diagnostic "benchmark anyway" direction. They are **engineering evidence,
  not a formal gate PASS**; the multi-cohort order is not the pairwise formal
  acceptance protocol. Neither these runs nor their lower spreads relabel
  previous invalid measurements.

Frozen H6b1 directory: `.codex-tmp/lightweight-h6b1-candidate-20260913`.
Current directory: `.codex-tmp/lightweight-h6d-opt3-20260914`.
Original-runner/current-engine bridge:
`.codex-tmp/lightweight-h6b1-current-bridge-20260914`.

| Assembly | SHA-256 |
| --- | --- |
| Frozen H6b1 engine | C99A6835CD9981BE50356EFBF8EA5DDB500801A43807352FDBA6FB7FD8A3246D |
| Current opt3 engine | CEB7A3B1EF3768E9F302D5B3359E6C66E7D4A5B77D1A2487BE78C715806805C2 |
| Original runner | 1F25C12791E8E68EB4C7C025DBE1A5C79ED229CA082FE45A73001A2F7F1AA68F |
| Current runner | 0D6C02290526CE333B62C41BBF89DCE76ADE8215896509E3D53BB454B379934D |
| Copper68k (all runs) | 5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497 |
| CopperDisk (all runs) | 8F2AA0D1AD74A1A05D96449A1385D6F9A672FE8FEAE68548B3D92BD2EDDCE227 |

Checkout HEAD `e9a1ef47444c87da626ed01a62bad4d1670e9d97` plus existing
uncommitted work. Logs are repository-root-relative:

- `.codex-tmp/h6d-450-samework-20260914.txt`
- `.codex-tmp/h6d-450-cost-ladder-20260914.txt`

Harnesses: existing `.codex-tmp/run-h6b1-optimization.ps1` and new
`.codex-tmp/run-h6d-cost-ladder.ps1` (SHA-256
`9C6EF76B56D25DB91EA60112343D8E8C2028CD3CD54F769182D4CC5DC3B92254`).
The new harness checks within-cohort determinism, B/S equality, completed
processes, allocations, unsupported behavior, placement and telemetry. No
builds/tests were needed or launched during this measurement-only audit.

## Recommended next optimization

First inspect **direct full-width output**, then the **disk RAM DMA hot path**.
The full-width lowres branch currently renders two intermediate pixels and
then rereads/expands them into four stores. Direct emission is a concrete
simplification candidate; its gain is not established and the full measured
0.133 ms is not assumed recoverable. Retain all pixels, sprite advancement,
bitplane reload phases and mixed-resolution behavior when testing it.

Do not start another arbitration redesign from these numbers, optimize TOD
from an unresolved sub-microsecond difference, or treat input's inclusive
profile stacks as pure keyboard overhead: those stacks include DMA and
rendering performed during CPU bus waits.

The 393.89 versus the preceding session's 385.80 FPS is **not another code
optimization**: the same opt3 binary was used. H6d, the slow-disk warning,
native gameplay and the 200 complete-gameplay FPS objective remain open.

## Implementation follow-up (2026-09-14)

The user subsequently authorized the recommended direct-output experiment.
Opt4 now emits wide lowres pixels directly, sharing the existing shifter and
composition logic. The confirmation comparison measured **411.74 versus
405.33 FPS (+1.58%, 0.0384 ms/field saved)** on identical wide-lowres work.
Narrow and hires controls showed no material regression; all allocations and
fingerprints match. All 304 Debug/Release tests pass and 246 native snapshots
are byte-identical. These remain provisional engineering measurements.

Disk RAM DMA was inspected and profiled on/off, but no additional isolated
shortcut was established: much of its work is inlined into the shared loop.
Disk execution remains unchanged. See the execution plan's **H6d optimization
4** entry for full sample lists, build hashes, profile limitations and the
next bounded investigation. This follow-up does not rewrite the audit's
historical opt3 workload ladder above.
