# Synthetic suite reference qualification

Milestone 6 is in progress. This is scoped software-reference evidence, not
exhaustive external coverage, physical CPU qualification or desktop readiness.
68010 and 68060 remain diagnostic profiles.

## Sources and reproducibility

The [Motorola programmer reference](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
and applicable processor manuals establish architectural expectations. Independent
software corpora are useful for finding disagreements; their assertions require
review when they disagree with documented behavior.

The reused Musashi adapter executes self-checking programs from
[`kstenerud/Musashi`](https://github.com/kstenerud/Musashi/tree/72c1d74800f3087b45a0c1a7342601bbed898881/test),
pinned to `72c1d74800f3087b45a0c1a7342601bbed898881`. It does not execute the
Musashi CPU as a differential oracle. Each requested profile uses the public CPU
factory, including the A1200 EC020 factory. Both complete fixture directories
(60 mc68000 programs and 18 mc68040 programs) are required. Reports distinguish
passing, mismatching and explicitly excluded programs, record input SHA-256
identities and retired instruction counts, and retain recent retirement diagnostics.
Program counts do not imply coverage of every instruction/address combination.

```powershell
git clone https://github.com/kstenerud/Musashi artifacts/reference-musashi
git -C artifacts/reference-musashi checkout 72c1d74800f3087b45a0c1a7342601bbed898881
./scripts/test-copper68k-synthetic.ps1 -Deep -Seed 68020 -Samples 10000 -MusashiPath artifacts/reference-musashi
```

Requested audits fail for missing fixture directories, incomplete selections,
unknown/duplicate model IDs or any mismatch. The command rejects modified pinned
inputs. SHA-256 identities are written to `musashi-inputs.json` and the per-program
`musashi-model-audit.json`; selected profiles must each execute programs.

| Profile | Passing programs | Explicit exclusions |
| --- | ---: | ---: |
| 68000 | 56 | 22 |
| 68010 | 56 | 22 |
| 68EC020 | 72 | 6 |
| 68020 | 72 | 6 |
| 68030 | 72 | 6 |
| 68040 | 72 | 6 |
| 68060 | 66 | 12 |
| A1200 EC020 | 72 | 6 |
| Total | 538 | 86 |

Four mc68000 fixtures retain the previous invalid-BCD/undefined-DIV-flags caveats.
000/010 exclude the advanced directory because its programs assume 020+
instructions without unavailable-instruction handlers. All advanced profiles
exclude `cmp2.bin`'s conflicting carry assertion and `chk2.bin`: its final
`CHK2.W (A7),A0` at binary offset 0xEC (`02D7 8800`) uses long bounds
`10000000/70000000` but expects a long-sized out-of-range trap. Word-sized bounds
are instead `1000/0000`, sign-extended and compared against all 32 bits of A0;
the wrapped interval contains `70000001`. The binary agrees with its unsuffixed
source instruction, not the intended long operation. Inputs remain unchanged.
060 additionally excludes MOVEP, CHK2, CAS2 and 64-bit multiply/divide programs
without software handlers, and the interrupt program's required MSP/format-1 pair.

## Disagreement found and corrected

[MC68040UM section 8.2.6](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
lists NOP, MOVES, CAS/CAS2, MOVE USP, MOVEC and cache/MMU serializers among T0
events. The earlier suite used 020/030 flow rules for 040. New scenarios reproduced
481 mismatches on 040 and none on the other profiles. Correcting the 040 classifier
passes all selected profiles. Tests include all initial CCRs, both privilege modes,
aborting exceptions and batch-boundary trace changes. TAS remains a negative case:
the external branch-status list differs from the architectural T0 list.
FPU tracing and enabled-MMU execution remain outside this roadmap.

### 040 MMU instruction decoding follow-up

PRM sections 6-35/6-36 and 6-70/6-71 specify single-word PFLUSH and PTEST
encodings. The old PFLUSH consumed the following word, and the PTEST mask could
never match. Neither decoder checked privilege. The new disabled-MMU matrix
reproduced **34,850 mismatches** before the correction.

The correction consumes only the opcode, checks privilege before ATC/probe
effects, and decodes PTEST's read/write bit and DFC space from the documented
fields. PTEST refreshes the selected cached translation before a table search.
JIT fallback invalidation recognizes the same instruction masks, including PTEST.
The existing instruction timing key/policy is preserved.

The `68040/system-mmu-disabled` gate adds 34,850 cases in one batch: every
address register for page flush/probe forms, canonical PFLUSHA/PFLUSHAN words,
four defined DFC values, all 32 CCR values, user/supervisor execution and all
T0/T1 combinations. Exact next PC, exception frames, untouched registers and
memory, absence of operand reads and following MOVEQ sentinels are checked.
[MC68040UM section 3.1.3](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
specifies no table search with TC.E clear and undefined PTEST results in this
state. The matrix deliberately makes no MMUSR-value assertion.

Focused enabled-MMU routing/ATC tests exercise the existing **flat-table
approximation**, separately from architectural qualification. PFLUSH still
conservatively flushes the entire ATC; selective page/global preservation,
real table formats, descriptor updates and architectural MMUSR contents remain
unqualified. Undefined DFC values and noncanonical global register fields are
excluded from this scoped gate. No enabled-MMU family is promoted in the integer
inventory, and no specialized regression is retired by this follow-up.

### 000 address-error double-fault follow-up

[MC68000UM sections 6.3.9.1 and 6.3.10](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf)
require halting when another bus/address error occurs while processing a group-0
exception. Entry to its handler is part of that processing. A bounded odd-handler
test reproduced `Halted=false` before the fix, without invoking the recursive
odd-stack path in the old process.

The 000 core now guards active address-error entry. A nested alignment fault
halts instead of recursively stacking another frame. An odd handler target also
halts before another frame or instruction fetch. Completed frame writes and
operand effects are preserved; the guard neither rolls back nor retries an
instruction. Halt abandons pending prefetch and trace work. Interrupts are ignored
while halted, and an aborted interrupt stack sequence unwinds without continuing
its writes or exposing the internal control-flow exception to the host.

A private double-fault latch prevents host subroutine/task entry from waking this
architectural halt. The supplied-PC/SP `Reset` API clears it. The existing host
convention of explicitly setting `Halted` and subsequently entering a subroutine
continues to work; that convention is separately tested. No public API was added.
The 010 model hook and advanced compatibility mode retain their separate behavior.

`68000/system-double-fault` adds 640 public-factory cases across all CCRs,
user/supervisor stacks and trace states: odd exception stacks, odd handler PCs,
trap/interrupt stacking, halted register/memory/bus inactivity and reset recovery.
Positive scenarios check that an operand fault after a valid handler instruction
begins creates a new exception normally. The first 14-byte frame is checked word
by word. Partial stack-pointer changes on failed entry follow the retained
execution ordering; no physical timing claim is made for the halted edge.

Warm classic/V2 JIT tests compare all registers, PC/SR, exception metadata,
machine/native cycle policy and writes against scalar, kind-table and packed-plan
interpreters through the **architectural-trace-forced fallback** at this first
checkpoint. Direct compiled odd-access execution exposed a separate host-exception
gap; the subsequent compiled-JIT alignment follow-up below corrects that path.
The public bus API has no explicit external BERR/reset-vector fault signal;
this gate qualifies address-error double faults, not general bus-error handling.
All specialized regressions are retained.

### 000 compiled JIT alignment follow-up

The warm `3010 60FC` repro failed with both classic and V2 JIT before this
correction: changing A0 from `$2000` to `$2001` with SR `$201F` exposed a host
exception instead of vector 3. The new instruction-boundary IL guards inspect
only address parity, before consuming instruction fetches, updating registers,
changing flags or accessing the bus. A failing guard returns completed trace
instructions and writes back their state; the accurate interpreter executes the
faulting instruction once. No compiled instruction is caught and retried.

The guards apply only to the 68000 plan. They cover word/long memory operand
forms, displacement and unscaled brief indexes, absolute and PC-relative
addresses, stack accesses for PEA/BSR/JSR/RTS, and JMP/JSR target alignment.
Even word/long address increments preserve parity across operand aliases. Byte
operands, MOVEP's byte transfers and LEA's computed address remain legal at odd
addresses; PEA checks its stack rather than its pushed address value. The
low-level alignment assertions remain as defensive checks.

`M68000JitDirectZeroWaitTests` executes **180 logical warm-cache scenarios in
15 xUnit batches**: 132 operand/prefix/dispatch-mode combinations, ten cold
absolute/PC-relative graph exits, eight odd-stack cases, four odd JMP/JSR target
cases, two legal odd-byte/address loops and 24 double-fault/trace/dispatch
combinations. This includes the earlier trace-only comparisons, now extended to
trace-disabled compiled execution. Fixed instruction encodings and fault
addresses supply independent assertions; architectural registers, exception
metadata, all frame words, completed writes and memory are compared with the
interpreter. Retained pipeline tests own IR/prefetch ordering, including write
faults whose IR already contains a successor opcode.

Removing all guards reproduces seven failing batches, including a cold V2
absolute access and odd V2 stack write. Removing MOVE destination guards
separately reproduces three failing batches for compiled writes. These mutations
are restored before the successful full run. No old regression is retired.
This gate preserves existing machine/native cycle policies; it is not physical
pipeline/timing qualification or exhaustive JIT exception-path qualification.
External BERR/reset-vector signaling remains unavailable through the public bus
API. Independent reference and advanced restart-frame gaps remain open.

## Consolidation proof

`M68kShiftTests.AslByteSetsOverflowWhenSignChanges` is replaced by
`SyntheticBitShiftTests.ShiftsCountsValuesAndFlags`. The named
`<model>/ASL/1/D2/historical-sign-change/op=E302/value=6891C884/count=1/ccr=00`
scenario preserves its input and assertions through shared independent expectations.
It also checks exact PC, untouched registers/memory and a following sentinel on
all eight profiles. The larger shift matrix covers all CCRs and counts.

Before retirement, the `000-asl-overflow` mutation disabled the production
68000 overflow update. Both the original regression and the synthetic replacement
failed in the same run. `040-t0-serializers` separately removes the new classifier
and reproduces the trace failure. Evidence is under
`artifacts/m6-mutation-proof-verified/`; `mutation-proof.json` records
`originalRegressionDetected=true` for the former and exact failing case IDs.
Sources are restored and rebuilt by the mutation command.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Consolidation
```

Only this pure semantic duplicate is retired. Timing-policy, cache, prefetch,
bus ordering, detailed fault sequencing, JIT and native ROM/media tests remain.

## Remaining qualification and implementation gaps

- Existing SingleStepTests and WinUAE adapters remain available; no new external
  corpus execution is claimed for them in this slice. WinUAE integer integration
  currently selects 68000; multi-model native bridge/fixtures still need qualification.
- RTE internal restart remains untested: 010 format 8, 020/030 formats 9/A/B,
  and 040 format 7. The current advanced decoder does not implement all these
  legal restoration protocols; this is an implementation gap, not invalid encoding.
- Nested 000 address errors during exception stacking previously recursed on an
  odd SSP. This is corrected by the address-error double-fault slice above. External
  BERR and reset-vector fault signaling remain unavailable through the current
  public bus API; no general bus-error qualification is claimed.
- Compiled 000 JIT word/long and stack alignment faults are corrected by the
  instruction-boundary guards above. The original host-exception repro remains
  in `artifacts/double-fault-focused/focused-jit.trx`; final warm-cache and
  guard-removal evidence is under `artifacts/jit-address-error-focused/` and
  `artifacts/jit-address-error-before/`. Other compiled exception/fetch paths
  are not exhaustively qualified by this scoped gate.
- 040 enabled-MMU selective/global flushing, real translation-table formats,
  descriptor updates and architectural MMUSR contents remain unqualified;
  the single-word decoder and disabled-MMU instruction gate are now corrected.
- External BKPT replacement, physical MOVES function-code spaces, LPSTOP
  CPU-space broadcast and real CALLM/RTM access-control responses remain unqualified.
- Exhaustive independent addressing references, hardware captures, physical
  cache/pipeline timing, enabled MMU and FPU arithmetic are not claimed here.

Milestone 6 remains open until the planned remaining reference work and review
are completed. Published packages are immutable; publication is a separate release.

## Validation checkpoint: initial reference audit

The full CPU suite passes 4,593 tests with six optional external-reference skips.
The report gate passes all 8,333,190 deterministic cases in 338 batches, plus
320,000 seeded cases (seed 68020, 10,000 per profile/group). The pinned Musashi
audit passes 538 programs; 86 exclusions remain explicit. A requested missing
fixture audit fails as expected. AHX passes 18 consumer tests.

Private package `1.5.2-synthetic-dev.33` has SHA-256
`817c0bc8a27e9ec0ad768eeb9b3c39c93f2f487a6fcf978716ab14ee47c127e6`.
The isolated CopperScreen consumer at baseline `d9beae8` resolves that package
through NuGet, builds Release with zero warnings/errors, and passes 149 host,
74 disk and 1,080 separately built diagnostic tests. Six optional host/media
skips remain unavailable coverage. Native replay separately passes Workbench 3.1
at 0/2 MiB Fast RAM and the A1200 eight-plane hard-disk boot with persistence
across a desktop reopen (three executed cases, no skips). These are correctness
results, not throughput or physical-timing qualification. Root consumer working
changes remain outside this validation checkout; the package is not published.

Evidence: `artifacts/m6-final/`, `artifacts/m6-ahx/`,
`artifacts/m6-mutation-proof-verified/`, `artifacts/m6-negative-inputs/`,
`artifacts/synthetic-private-feed-33/`, and the isolated consumer's
`artifacts/m6-validation/`.

## Validation checkpoint: 040 MMU decoder follow-up

The ordinary full CPU suite passes **4,601 tests**, with eight optional/opt-in
checks skipped and no failures. The strengthened final focused run passes all
17 batches/tests. Deterministic report validation passes **8,368,040 logical
cases in 339 batches**, including all 34,850 new MMU cases. The pinned Musashi
audit separately executes and passes 538 program/profile combinations, with
86 explicit exclusions. AHX passes 18 tests. Previous reports lacking the new
MMU group are rejected. The prior 320,000 seeded cases are historical evidence;
this bounded follow-up does not claim a new seeded run.

Private package `1.5.2-synthetic-dev.34` has SHA-256
`b81870fadf4036d97ec5c2297990fa76e26bfc22add2473e9b381961bbf26bdf`.
The isolated CopperScreen baseline `d9beae8` resolves that exact NuGet version
and builds Release with zero warnings/errors. Host tests pass 149 cases with
six optional/media skips; disk passes 74 and separate engine diagnostics pass
1,080 without skips. Native Workbench 3.1 at 0/2 MiB Fast RAM and A1200
eight-plane hard-disk boot/reopen persistence separately pass all three cases
without skips. These are correctness replays, not performance qualification.
The package remains unpublished; root CopperScreen working changes are preserved.

Evidence: `artifacts/mmu-decode-before/` (including the failing matrix and
missing-report rejection), `artifacts/mmu-decode-focused-final/`,
`artifacts/mmu-decode-final/`, `artifacts/mmu-decode-ahx/`,
`artifacts/synthetic-private-feed-34/`, and the isolated consumer's
`artifacts/mmu-decode-validation/` and `artifacts/mmu-decode-diagnostic-tests/`.

## Validation checkpoint: 000 address-error double faults

The full ordinary CPU suite passes **4,606 tests**, with eight optional/opt-in
skips and no failures. The final focused run passes eight tests/batches, including
640 double-fault scenarios and warm classic/V2 trace-fallback comparisons against
all three interpreter dispatch modes. Deterministic report validation passes
**8,368,680 logical cases in 340 batches**. Reports missing the new fault group
are rejected. The pinned Musashi audit separately passes 538 program/profile
combinations with 86 explicit exclusions. AHX passes 18 tests. The earlier
320,000 seeded cases remain historical evidence; no new seeded audit is claimed.

Private package `1.5.2-synthetic-dev.35` has SHA-256
`20d13f6435f78dbd04691ef8e12da600d644363cc9cd79b6a1eee39665b2a815`.
The isolated CopperScreen baseline `d9beae8` resolves that exact NuGet version
and builds Release with zero warnings/errors. Host tests pass 149 cases with six
optional/media skips; disk passes 74 and separately built engine diagnostics pass
1,080 without skips. Native Workbench 3.1 at 0/2 MiB Fast RAM and A1200
eight-plane hard-disk boot/reopen persistence separately pass all three cases
without skips. The package remains unpublished; root consumer changes are
preserved. This is correctness and retained timing-policy evidence, not physical
timing or throughput qualification.

Evidence: `artifacts/double-fault-before/` (bounded failing handler test and
missing-report rejection), `artifacts/double-fault-focused/` (including the
separate compiled-JIT failure), `artifacts/double-fault-final/`,
`artifacts/double-fault-ahx/`, `artifacts/synthetic-private-feed-35/`, and the
isolated consumer's `artifacts/double-fault-validation/` and
`artifacts/double-fault-diagnostic-tests/`.

## Validation checkpoint: 000 compiled JIT alignment

The full ordinary CPU suite passes **4,619 tests**, with eight optional/opt-in
skips and zero failures. All **8,368,680 synthetic cases in 340 batches** pass;
the JIT-specific warm-cache gate separately passes **180 logical scenarios in
15 batches**. The requested pinned Musashi audit executes without skips: 538
program/profile combinations pass, with 86 explicit exclusions and zero
mismatches (source `72c1d74800f3087b45a0c1a7342601bbed898881`). AHX passes 18 tests.
The bounded warm-loop repro fails before correction; removing all guards detects
seven failing batches and removing MOVE destination guards detects three.
No new seeded audit or retirement of an existing regression is claimed.

Private package `1.5.2-synthetic-dev.36` has SHA-256
`96b16bd8bd2b9c865fe482501289456bcb8ca69a332b99552a80a649b995b6c5`.
It is not published. The isolated CopperScreen consumer retains its pinned NuGet
boundary and resolves this exact private version in both production and separate
diagnostic outputs. Its Release production build has zero warnings/errors; host
149, disk 74 and engine diagnostics 1,080 pass. The ordinary host invocation has
six optional/media skips. The separate native Workbench 3.1 0/2 MiB Fast RAM and
A1200 eight-plane hard-disk boot/reopen persistence invocation executes all three
cases without skips. Native replay results are correctness evidence, not host
throughput or physical timing qualification. Unrelated root changes are preserved.

Evidence is in the CPU checkout's `artifacts/jit-address-error-before/`,
`artifacts/jit-address-error-focused/`, `artifacts/jit-address-error-final/`,
`artifacts/jit-address-error-ahx/` and `artifacts/synthetic-private-feed-36/`, and
the isolated consumer's `artifacts/jit-address-error-validation/`,
`artifacts/jit-address-error-diagnostic-tests/` and
`artifacts/jit-address-error-production.binlog`.
