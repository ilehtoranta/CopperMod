# CopperScreen Performance Backlog

This backlog tracks compatibility-first performance work for the CopperScreen A500
runtime. The current priority is Full Contact original single-drive playback with
the accurate interpreter path:

```powershell
CopperScreen.exe --profile "CopperScreen\Profiles\expanded-kickstart13 - singledrive.json" "CopperScreen\TestImages\Full Contact (1991)(Team 17)(Disk 1 of 2).zip"
```

## Current Position

- Full Contact still has audio stutter in longer interpreter runs.
- Recent hardware-poll and scheduler work reduced expensive custom-register read
  cost, but did not fully remove long-run underruns.
- The first real PC/block hot-loop profiler is now available through the existing
  benchmark `--instruction-matrix` option.
- Hot-loop counts show where emulated CPU instruction volume is concentrated, but
  they do not by themselves prove that interpreter dispatch is the host-time
  bottleneck.

## Hot-Loop Profiler Findings

Short Release probe:

```powershell
dotnet run --project ..\CopperScreen.Benchmarks\CopperScreen.Benchmarks.csproj -c Release --no-build -- --only "Full Contact original single-drive" --cpu interpreter --instruction-matrix --top-opcodes 20 --warmup 120 --frames 120
```

Measured result:

- `1164709` measured instructions.
- Benchmark bucket reported about `94.0%` CPU time, but this bucket includes
  CPU execution and CPU-paid bus/scheduler work.
- Fake audio queue had `0` submit failures in this short window, but longer
  windows have still shown underruns.

Top measured PC/block findings:

| Block | Kind | Count | Share |
| --- | --- | ---: | ---: |
| `$FC3130..$FC3134` | `DBcc` self-loop | `598859` | `51.42%` |
| `$07B38E..$07B392` | `DBcc` self-loop | `199584` | `17.14%` |
| `$07B2D4..$07B2D8` | `DBcc` self-loop | `49152` | `4.22%` |
| `$07B334..$07B338` | `DBcc` self-loop | `49152` | `4.22%` |
| `$07B294..$07B298` | `DBcc` self-loop | `45056` | `3.87%` |
| `$07B240..$07B24E` | branch poll block | `24954` | `2.14%` |

Interpretation:

- The largest blocks are pure counted delay loops, not necessarily hardware
  polling loops.
- Optimizing hardware register reads will not materially affect the top `DBcc`
  self-loops.
- The next profiler step should attribute host time, not just instruction count:
  CPU dispatch, branch handlers, scheduler drains, bus accesses, chip DMA, display
  replay, disk, Paula, CIA, and blitter.

## Rasterline Cache Status

Current implemented direction:

- Cache rasterline schedules/descriptors rather than rendered pixels.
- Replay cached descriptors by performing live chip-RAM DMA reads at the original
  grant cycles.
- Keep exact fallback for copper activity, pending display writes, unsupported
  state, descriptor mismatches, and invalidation.
- Sprite DMA must remain stateful during replay: control words, terminators,
  denied DATB latch reuse, missed slots, timeline records, and priority behavior
  are observable.

Observed status:

- The current scheduler line cache reports high hit counts in short Full Contact
  runs, but rebuilds and invalidations remain significant.
- Rasterline-cache gains so far have been shallow compared with the remaining
  stutter problem.
- The likely missing piece is not cached pixels or stale memory pointers. It is a
  more exact and cheaper schedule strategy bounded by copper/display-write
  behavior, plus host-time evidence showing whether display DMA replay is still a
  wall-clock hot path.

Open rasterline-cache work:

- Separate descriptor-cache counters from broader scheduler line-cache counters
  in benchmark output.
- Add host-time attribution around exact path versus descriptor replay path.
- Track why rows fall back: copper active, pending display write, unsupported
  state, overflow, mismatch, sprite state, bitplane state, frame reset.
- Consider copper-bounded row schedule reuse only after fallback reasons show it
  would reduce real host time without risking same-line register semantics.

## Compatibility Risks

Do not trade accuracy for speed in these areas:

- CPU/custom/chip/CIA reads and writes must retain identical granted and completed
  cycles with HRM chip-slot arbitration enabled.
- Reads must sample current state and perform only documented read side effects.
- Same-cycle writes must be visible to later same-cycle reads.
- Interrupt visibility must not change for VBLANK, DSKBLK, Paula, CIA, blitter,
  and copper-triggered interrupt paths.
- `DSKBYTR` passive input advancement and byte-ready clear must remain exact.
- CIA ICR reads must clear pending bits without hiding same-cycle timer or flag
  events.
- Blitter busy polling and CPU stall release cycles must not move.
- Display/copper/bitplane/sprite DMA slot reservations must match exact behavior.
- Sprite/playfield priority and attach/control-word behavior must not regress.
- No title-specific Full Contact hacks.
- No public profile schema, disk format, audio buffering policy, or JIT behavior
  changes for this workstream unless explicitly split into a separate plan.

## Required Tests

Focused Amiga hardware/runtime suites:

```powershell
dotnet test ..\CopperMod.Amiga.Tests\CopperMod.Amiga.Tests.csproj --filter "BusTiming|Disk|Paula|CIA|Blitter|Copper|Display|Sprite|Raster" -c Release --no-restore -m:1
dotnet test ..\CopperScreen.Tests\CopperScreen.Tests.csproj --filter "Runtime|Full Contact|Shadow|Superfrog|OperationThunderbolt" -c Release --no-restore -m:1
```

Profiler and benchmark build checks:

```powershell
dotnet test ..\CopperMod.Amiga.Tests\CopperMod.Amiga.Tests.csproj --filter "InstructionFrequency" -c Release --no-restore -m:1
dotnet build ..\CopperScreen.Benchmarks\CopperScreen.Benchmarks.csproj -c Release --no-restore -m:1
```

Full Contact performance probe:

```powershell
dotnet run --project ..\CopperScreen.Benchmarks\CopperScreen.Benchmarks.csproj -c Release --no-build -- --only "Full Contact original single-drive" --cpu interpreter --instruction-matrix --top-opcodes 20 --warmup 240 --frames 900
```

Acceptance evidence to record:

- average and max frame milliseconds
- slow frames over 20/33/40 ms
- fake-audio submit failures
- fake-audio min/max queued milliseconds
- active Paula audio frames
- disk transfers and active DMA state
- scheduler drain counts and max frame drains
- line-cache and descriptor-cache counters
- hot `instruction-pc` and `hot-loop-block` lines
- host-time attribution counters once available

## Next Work Items

1. Add host-time attribution around CPU, scheduler drains, bus access, and display
   descriptor replay/exact fallback.
2. Use the attribution data to decide whether the next optimization belongs in
   interpreter branch dispatch, CPU boundary scheduling, bus access paths, display
   DMA replay, or disk/Paula/CIA event handling.
3. If `DBcc` self-loops are confirmed as host-time hot, design a proof-heavy
   exact countdown-loop fast path bounded by next CPU-visible event, pending
   interrupt, bus-visible write, and cycle budget.
4. If rasterline work remains host-time hot, make fallback reasons and descriptor
   replay costs visible before changing the cache strategy again.
