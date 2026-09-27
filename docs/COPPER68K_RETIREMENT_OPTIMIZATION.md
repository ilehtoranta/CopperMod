# MC68000 interrupt-sample retirement optimization — 2026-09-22

This first experiment produced only small, mixed changes in the normal batch
benchmarks: -0.41% branch, -1.27% register, +2.45% memory. Its larger single-step
figures below should not be presented as a general interpreter speedup. Subsequent
work targets the batch path directly; see the
[batch prefetch report](COPPER68K_BATCH_PREFETCH_OPTIMIZATION.md).

The interpreter now checks for a deferred-timing bus before resolving interrupt
samples, and keeps the resolution implementation in a non-inlined helper.
This removes redundant sample copies on ordinary buses and keeps the resolver's
struct temporaries out of frequently executed caller frames.

Baseline: `986ca2b0306c52bd782333847eb0059a2b9f03e8`. The candidate measured in this report changes only
`ResolveDeferredInterruptSamples()` in `Copper68k/M68kCore.cs` by adding the guard
and extracting its original body into `ResolveDeferredInterruptSamplesCore()`.

## Timing preservation

When `IM68kDeferredCpuInstructionTiming` is absent, the previous implementation
returned each sample unchanged and its registration call immediately returned.
Skipping those operations preserves both samples, including any stored token.
When the interface is present, the original resolution and registration sequence
still executes at the same point, even when the bus is not currently batching.
The overload that resolves a fixed-batch context is unchanged.

No instruction cycle formula, memory transfer, prefetch operation, bus callback,
IPL sampling point, exception sequence or batch admission rule is changed.
This is a preservation optimization, not a claim of complete hardware conformance.

## Measurements

Windows x64, AMD Ryzen 5 5600X, .NET SDK 10.0.400, Release, default tiered JIT/PGO.
Baseline and candidate were built into separate directories. Six pairs alternate
baseline/candidate order, with a fresh process per variant, 5 million warmup and
20 million measured instructions per workload. No concurrent test or build was
started during these measurements. Host scheduling/frequency were not controlled.

The following are **medians of paired throughput changes**
(`baseline milliseconds / candidate milliseconds - 1`), not application FPS:

| Single-instruction workload | Median baseline ms | Median candidate ms | Paired throughput gain |
| --- | ---: | ---: | ---: |
| branch-self-loop | 1253.377 | 1086.505 | 14.95% |
| register-hot-loop | 1245.673 | 1042.108 | 19.54% |
| memory-transform-loop | 1662.556 | 1513.531 | 9.83% |

Every pair improved, but individual results varied considerably. All measured
cycle totals and state/memory checksums matched; measured allocations were zero.
The benchmark bus does not implement deferred timing. These gains are not an
estimate for CopperScreen or a host using deferred CPU bus batches.

Three additional pairs used the default batch API with 5 million warmup and
50 million measured instructions. Paired medians were -0.41% for branch,
-1.27% for register and +2.45% for memory. Branch/register runs were short and
mixed in direction; no batch speedup is claimed. Their cycles and checksums also
matched. Raw tab-separated measurements: [single-step](assets/copper68k-retirement-single-step.txt)
and [batch](assets/copper68k-retirement-batch.txt).

Tier-1 disassembly of the register workload provides supporting evidence:

| Method | Baseline stack reservation | Candidate stack reservation | Baseline / candidate code bytes |
| --- | ---: | ---: | ---: |
| ExecuteInstructionBody | 232 bytes | 128 bytes | 2951 / 2534 |
| CompleteInstruction | 168 bytes | 80 bytes | 1068 / 698 |

These are observed JIT outputs for this host/profile, not portable guarantees.
Measured `Copper68k.dll` SHA-256 values:

- Baseline: `6507C88FC557BF2AFAE97EA5EA7CF8BDF2E46719436D44B4731ABF154565BCB3`
- Candidate: `3D55F1AD919561808EB84BA793D363E797B546D39C587C169EDABE6B5339FC33`

Reproduce against separately built baseline and candidate benchmark directories:

```powershell
dotnet <directory>/Copper68k.Benchmarks.dll --backend InterpreterM68000 --single-step --warmup 5000000 --instructions 20000000 --repeats 1
```

Alternate execution order for six pairs. For the batch check omit `--single-step`
and use `--instructions 50000000` for three pairs.

## Validation and limits

- Full Release `Copper68k.Tests`: **1,499 passed, 6 skipped, 0 failed**.
  Existing tests cover requested/completed bus phases, serialized prefetch,
  delayed fetch/data accesses, self-modifying code, branch/DBcc transitions,
  interrupt sampling, trace exceptions, write ordering and dispatch-tier parity.
- Release `CopperMod.Amiga.Lightweight.Tests`, filtered to
  `LightweightA500MachineTests|LightweightInterruptProgressTests`:
  **35 passed, 0 failed**.
- The larger `CopperMod.Amiga.Tests` project could not compile: its dependencies
  have a `KickstartTrapTable` constructor mismatch and missing CopperStart types.
  An isolated attempt to compile its unchanged CPU/bus test files also encountered
  a missing `AmigaDiskImage` type. No results are claimed for that suite.
- The six optional external conformance tests (SingleStepTests, Musashi/m68k-rs,
  WinUAE) were skipped. No external hardware corpus or native application replay
  was run. Matching benchmark cycle totals alone does not verify bus timing;
  the existing phase-level tests provide that regression coverage.

No package version, public API or consumer pin was changed.
