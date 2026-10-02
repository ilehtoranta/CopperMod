# MC68000 batch prefetch optimization — 2026-09-23

The earlier retirement-only change had small, mixed batch results. This change
targets the interpreter's cached batch execution and compares against that earlier
candidate, using the same benchmark executable for both versions.

## Change and timing contract

`ReadFixedBatchPrefetchWord` retains its timing setup and admitted-window fetch
path. Its general bus-fetch tail is extracted into the non-inlined
`ReadFixedBatchBusPrefetchWord`. That tail still performs the same instruction
read, bus timing query, ready/next-transfer updates, deferred-token capture and
phase-trace callback in the same order with the same requested cycle.

The fixed-batch interrupt-sample resolver now uses the same null-interface guard
and non-inlined implementation as the earlier single-instruction optimization.
Without a deferred-timing interface its old body was a no-op. With the interface
present it still resolves and registers both samples, including outside an active
deferred batch.

This changes generated code placement, not the emulated bus schedule. It does not
add batching eligibility, replace bus reads, change timing formulas or elide
prefetch. Existing admitted fetch-window rules remain the host's contract.

## Measurement method

Windows x64, Ryzen 5 5600X, .NET SDK 10.0.400, Release, default tiered JIT/PGO.
Six pairs alternate control/candidate order. Each process executes 5 million
warmup and 30 million measured instructions per workload through
`ExecuteInstructions`; these are **batch measurements**, not `--single-step`.
No test/build work ran concurrently with measurement. Scheduling, affinity and
clock frequency were not controlled.

Both directories contain the identical benchmark executable. The control uses
the earlier retirement-optimized `Copper68k.dll`; the candidate adds the two
batch changes above. The new opt-in `InterpreterM68000BusPrefetch` backend disables
instruction fetch windows to exercise the general bus path. Default backends and
workloads are unchanged.

Results use the median of six paired throughput changes:
`100 * (control milliseconds / candidate milliseconds - 1)`. These synthetic
results do not establish CopperScreen application FPS or speed on contended chip
memory. The benchmark bus does not implement deferred CPU timing.

| Fetch path | Workload | Control median ms | Candidate median ms | Paired throughput change | Pair range |
| --- | --- | ---: | ---: | ---: | ---: |
| Cached window | branch-self-loop | 39.424 | 39.884 | -0.67% | -4.28% to +0.74% |
| Cached window | register-hot-loop | 117.106 | 115.613 | +0.32% | -6.74% to +6.52% |
| Cached window | memory-transform-loop | 1945.170 | 1314.262 | **+46.77%** | +46.02% to +53.71% |
| Cached window | cfg-bcc-register-loop | 1540.631 | 1014.711 | **+56.11%** | +51.19% to +59.08% |
| Cached window | cfg-dbra-load-loop | 1596.010 | 1014.543 | **+57.97%** | +53.31% to +59.06% |
| Cached window | cfg-bcc-memory-loop | 1674.939 | 1083.916 | **+55.09%** | +48.83% to +61.45% |
| Ordinary bus | branch-self-loop | 1645.342 | 1661.857 | -0.35% | -11.10% to +15.22% |
| Ordinary bus | register-hot-loop | 1567.221 | 1534.263 | +1.90% | -0.36% to +8.91% |
| Ordinary bus | memory-transform-loop | 2229.951 | 2132.739 | +3.67% | +1.02% to +8.27% |

The substantial gains are confined to cached memory/control-flow execution.
The simple branch/register loops and ordinary bus path remain small or mixed.
The ranges are observed extrema of six pairs, not confidence intervals. In
particular, ordinary-bus branch timings were noisy; no gain is claimed there.

All **108 measurements** matched their paired cycle totals and state/memory
checksums, with zero measured allocations. [Raw tab-separated measurements](assets/copper68k-batch-prefetch-pairs.txt)
include every pair and all benchmark counters.

JIT inspection of `ExecuteCachedFixedPlanGraphWalk` on the memory workload showed
its stack reservation shrinking from **4,952 to 1,704 bytes**, and generated code
from **24,775 to 21,659 bytes**. The fast-memory instruction helper, previously a
separate call, was incorporated into the optimized graph loop. These are observed
JIT outputs for this profile; they are supporting evidence, not portable promises.

## Reproduction

Build the benchmark once, copy its output into two separate directories, and
replace only `Copper68k.dll` in the control directory with the previous version.
Run the following in each directory, alternating order for six pairs:

```powershell
dotnet Copper68k.Benchmarks.dll --backend InterpreterM68000 --warmup 5000000 --instructions 30000000 --repeats 1
dotnet Copper68k.Benchmarks.dll --backend InterpreterM68000 --workload cfg- --warmup 5000000 --instructions 30000000 --repeats 1
dotnet Copper68k.Benchmarks.dll --backend InterpreterM68000BusPrefetch --warmup 5000000 --instructions 30000000 --repeats 1
```

SHA-256 of measured binaries:

- Control core: `3D55F1AD919561808EB84BA793D363E797B546D39C587C169EDABE6B5339FC33`
- Candidate core: `881982995B5FBA962E53BBDC83DD64E4893C7B5699802E0C3D31DC33C022DE13`
- Shared benchmark: `6DF3474CCC1AA42B8BE8C5C31D12F5A295ACA17867E9A56DAD77BBA40082E124`

## Correctness validation

- Full Release `Copper68k.Tests`: **1,499 passed, 6 skipped, 0 failed**.
- Release lightweight machine and interrupt tests: **35 passed, 0 failed**.
- Relevant existing regressions include delayed retirement prefetch, exact bus
  phases, partial context commit on a thrown fetch, cached-run exit at unsafe
  fetches, self-modifying code, prefetch state and interrupt-sample parity.

The six external conformance cases remain skipped. The larger Amiga test project
has the compilation blockers recorded in the earlier report; it was not rerun.
No native application replay or external hardware corpus was run. These checks
preserve tested bus/prefetch behavior and do not certify every hardware edge case.
