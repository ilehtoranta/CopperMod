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
| 68000 | 55 | 23 |
| 68010 | 55 | 23 |
| 68EC020 | 72 | 6 |
| 68020 | 72 | 6 |
| 68030 | 72 | 6 |
| 68040 | 72 | 6 |
| 68060 | 66 | 12 |
| A1200 EC020 | 72 | 6 |
| Total | 536 | 88 |

Four mc68000 fixtures retain the previous invalid-BCD/undefined-DIV-flags caveats.
000/010 additionally exclude `mc68000/move.bin`: despite its directory, offset
0x160 encodes 020-only PC-relative CMPI.B (`0C3A`), with no compatible
unavailable-instruction handler. The current synthetic CMPI and unavailable
instruction coverage is retained. These are the current audit counts; earlier
dated checkpoint counts below remain historical evidence.
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

### SingleStepTests boundary and LINK A7

The existing binary adapter now audits the complete pinned
[`SingleStepTests/m68000`](https://github.com/SingleStepTests/m68000/tree/64b253116a3de04aaac4346c43680960dc9b67e5)
revision `64b253116a3de04aaac4346c43680960dc9b67e5`: 127 files, with TAS and
TRAPV explicitly excluded according to the upstream README. The remaining
125 files each contain 2,500 cases. This is a MAME microcoded software reference,
not hardware captures. All register/stack banks, full architectural SR, converted
PC and fixture final RAM are compared. Transaction/prefetch images and physical
timing are not qualified by this semantic adapter. Fixture memory is sparse,
with intentional 24-bit wrapping for the 68000, and cores use the public factory.

The first run executed 312,500 cases and reported 120,553 disagreements. Inspection
of NOP case 007 (`4E71`, initial/final SR `$8609`, unchanged SSP `$AE04C0`, only
one ordinary prefetch transaction) establishes that the corpus ends before
pending trace entry. Copper68k's ordinary `ExecuteInstruction` includes trace
entry. The adapter therefore selects the existing internal interpreter trace
switch without clearing/changing SR. This qualifies the instruction-body boundary;
the ordinary synthetic trace group retains full API trace qualification. The
script selects the interpreter explicitly; no JIT boundary audit is claimed.

After boundary alignment, 312,174 cases passed and **326 LINK A7 cases** disagreed
in the pushed value. The old synthetic expectation inferred alias sampling from
the SP/An assignment shorthand in
[M68000PM 4-111](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf).
That shorthand alone is insufficient to distinguish overlapping registers on
individual models. The prose describes pushing the specified register before
loading the updated stack pointer. Pinned
[WinUAE generator LINK code](https://github.com/tonioni/WinUAE/blob/5d22d33632646efc3f747f03e82d28353e52722e/gencpu.cpp#L7044)
explicitly samples An before stack decrement except on 68040. Its source comment
labels the sequence cycle-exact confirmed; this is corroborating software-source
evidence, not an independently executed hardware test. The Musashi special-case
code instead uses the decremented value on every model, so its self-checking
program audit did not expose this distinction.

The corrected synthetic stack expectation reproduced **3,840 mismatches** in
seven profile batches; 040 remained passing. 000/010 now push the original An.
Advanced word/long LINK does the same except for the preserved 040 early-decrement
rule. Displacement fetching, writes, register updates and timing keys remain in
their existing order. No instruction retry or public API change is introduced.
All **312,500 external cases** and the **48,640 synthetic stack cases** pass after
correction. No regression is retired by this slice.

```powershell
git clone https://github.com/SingleStepTests/m68000 artifacts/reference-singlestep
git -C artifacts/reference-singlestep checkout 64b253116a3de04aaac4346c43680960dc9b67e5
./scripts/test-copper68k-synthetic.ps1 -SingleStepPath artifacts/reference-singlestep
```

The command defaults to all upstream-verified files; `-SingleStepFilter MOVE`
requests a recorded subset. It requires the pinned revision and all 127 unchanged
inputs even for a subset, rejects an empty selection and checks 2,500 executed
cases plus matching SHA-256 per selected file. The adapter emits
`singlestep-model-audit.json`, including passing/mismatching case counts and up
to 20 precise diagnostics per file. A mismatch fails after collecting the report;
missing, malformed, empty or limited requested audits fail instead of passing.
The existing generic input manifest supplies hashes for excluded files too.
Other CPU models remain covered by the separate pinned Musashi program audit;
SingleStepTests here supplies only 68000 fixtures.

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
At this checkpoint the 010 model hook retained its separate behavior; the
subsequent 010 structural slice extends the same entry guard to that hook.
Advanced compatibility mode retains its separate behavior.

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

- SingleStepTests has the pinned 312,500-case 68000 instruction-body audit.
  The later checkpoints below add a pinned multi-model WinUAE bridge, the
  unchanged failing Basic discovery audit, and separately qualified trace,
  trap/bounds, breakpoint, legal long-arithmetic and word-division presets. Remaining Basic disagreements and
  unsupported integer execution still require per-case qualification; passing
  focused presets do not replace the failing broad audit.
- Generated 010 word-MOVE/MOVEA format-8 images now have the scoped continuation
  gate below. Long transfers, other instruction families, foreign silicon images,
  external bus faults, 020/030 formats 9/A/B and the remaining 040 format-7
  CP context transfer and detailed fault protocols remain unqualified. The advanced decoder does not implement all these
  legal restoration protocols; this is an implementation gap, not invalid encoding.
  The [040 access-frame discovery gate](#040-access-frame-restoration-discovery-2026-10-05)
  retains the original failed-before record. The latest checkpoint below promotes
  normal/CT/CM/CU/CP synthetic returns and chained throwaways, preserving the remaining fault/context
  prerequisites explicitly.
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

### 010 format-8 structural follow-up

[MC68000UM sections 6.3.9.2, 6.3.10 and 6.4, figures 6-8/6-9](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf)
specify a 58-byte address/bus-error frame, with 26 information words written and
three reserved words left unwritten. The prior 010 address-error path allocated
only eight bytes while labelling the frame format 8. RTE then skipped 58 bytes
without reading or validating its internal version. Two bounded repros failed
before this correction: incorrect frame allocation and an incompatible version
accepted without vector 14.

The 010 address-error path now allocates the complete frame, leaves offsets
14/18/22 untouched, and records the logical fault address, scoped SSW read/write,
instruction/data and function-code fields, and the faulting output word. The
ordinary word/long MOVE reads/writes, MOVEA read, predecrement forms and JMP target
fixtures distinguish logical 32-bit frame addresses from 24-bit physical transfers.
The existing saved-PC convention, partial MOVE side effects and exception cycle
policy are preserved; these are not new physical prefetch/timing qualification.
No instruction is retried after operand effects.

RTE validates version bits 10-13 of the first internal word at SP+26 before
changing SP/SR. An incompatible version raises a format-error frame below the
intact original. For accepted version zero it probes SP+56 before reading the
remaining information words, skipping reserved holes, then performs the existing
structural pop and stack selection. Version zero is an emulator-private convention,
not a universal claim about hardware revisions. Zeroed input buffers and internal
words are placeholders. RR-controlled cycle/instruction continuation, interrupted
RMW semantics and physical prefetch buffer contents remain **unqualified**. This
slice must not be interpreted as completion of format-8 restart support.

The shared active-entry guard now also encloses the 010 hook. An odd supervisor
stack or odd handler PC halts until the supplied-PC/SP reset API, preserving
completed writes and rejecting interrupt/task/subroutine wakeups. The public bus
API does not signal external BERR; tail-probe accessibility failures, double-BERR
loading behavior and bus-error restart cannot be qualified by this fixture.

The three new batches execute **5,376 cases**: `system-format8-entry` 1,024,
`system-format8-rte` 4,096 and `system-format8-double-fault` 256. They cover all
CCRs, both stacks and trace states for entry/halt, all 16 version fields, independent
non-version bits, restored user/supervisor stacks, exact PC and a following MOVEQ
sentinel. Common verification checks all registers, defined SR, execution state,
memory and surroundings; the recording bus checks 26 information writes, reserved
holes, rejected odd transfers and version/probe/tail read order. Placeholder values
are tested as implementation conventions only. No old regression is retired.

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

## Validation checkpoint: 010 format-8 structure

The full ordinary Release CPU suite passes **4,624 tests**, with eight optional/
opt-in skips and zero failures. The final focused run passes 43 tests/batches.
Deterministic report validation passes **8,374,056 logical cases in 343 batches**,
including all 5,376 new structural cases. Previous reports missing the new groups
are rejected. The requested pinned Musashi audit executes and passes 538 program/
profile combinations with 86 explicit exclusions and no mismatches, from revision
`72c1d74800f3087b45a0c1a7342601bbed898881`. AHX passes 18 tests. The two bounded
before-fix regressions fail, then pass after correction. No new seeded run or
regression retirement is claimed.

Private package `1.5.2-synthetic-dev.37` has SHA-256
`8c3fe8b434b5b9bef6ff57c5e8148739b560abcb6ebd93c49acb8dccf8de059a`.
It remains unpublished. The isolated CopperScreen baseline `d9beae8` resolves
this exact NuGet version in production and separate diagnostic outputs. Release
build passes with zero warnings/errors; host 149, disk 74 and engine diagnostics
1,080 pass. The ordinary host invocation has six optional/media skips; these are
unavailable coverage. Separate native Workbench 3.1 at 0/2 MiB Fast RAM and
A1200 eight-plane hard-disk boot/reopen persistence execute all three cases
without skips. These are correctness replays, not throughput or physical timing
qualification. Unrelated root working changes are preserved.

Evidence: CPU `artifacts/format8-before/`, `artifacts/format8-focused/`,
`artifacts/format8-final/`, `artifacts/format8-ahx/` and
`artifacts/synthetic-private-feed-37/`; isolated consumer
`artifacts/format8-validation/`, `artifacts/format8-diagnostic-tests/` and
`artifacts/format8-production.binlog`. Full 010 suspended-instruction restart,
external BERR and physical timing qualification remain open.

## 010 word-MOVE/MOVEA continuation

[MC68000UM sections 6.3.9.2, 6.3.10 and 6.4](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf)
describe resuming the suspended bus cycle/instruction after RTE. With RR clear,
the stacked fault address is used; correcting only the address register therefore
still causes an address error. With RR set, software has supplied the read buffer
image or completed the write. Re-decoding the instruction would incorrectly repeat
completed source reads, increments and other effects. Three bounded tests exposed
this distinction before the correction: both software-completed MOVE paths returned
to the original opcode, and register-only correction did not refault during RTE.

Generated word-MOVE/MOVEA address-error frames now serialize a private continuation
in the internal information words. RTE loads/validates it while preserving the
previous version/probe read order. It pops the complete frame, selects the restored
stack, restores completed prefetch words, then continues at the pending word access.
There is no instruction replay, transaction rollback or per-frame side table.
A source fault continues with the supplied/rerun value and resolves only the still
unresolved destination. A destination fault uses the saved output word and completes
only the pending access/increment. A subsequent destination fault produces a fresh
continuation without repeating the completed source. Restored T1 takes its trace
exception after the suspended instruction completes.

The following private image is **an emulator convention, not a silicon encoding**.
Its version word is zero and marker is `$C010`; other generated families keep their
previous placeholder internal words and remain unqualified for continuation.

| Frame byte offset | Private word-MOVE contents |
| --- | --- |
| 26 | Version word, zero |
| 28 | Marker `$C010` |
| 30 | Suspended opcode |
| 32 | Pending source read (0) or destination write (1) |
| 34–37 | Next execution/extension PC at suspension |
| 38–41 | Completed prefetch queue address |
| 42, 44 | Queue words 0 and 1 |
| 46 | Queue count (0–2) |
| 48–57 | Unused private words, zero |

Malformed marked images raise vector 14 below the intact old frame before popping.
This additional validation is a private-image convention. Unmarked accepted-version
images retain structural-only compatibility; they do not gain guessed silicon
restart semantics. The existing saved-PC convention and successful nonfaulting
instruction paths remain unchanged. RTE retains its existing 20-cycle policy;
physical resumed-cycle/pipeline timing, asynchronous prefetch and interrupt-entry
qualification are not established by this semantic gate. No public API changes.

The new deterministic groups execute **127,776 logical cases in five batches**:
source 62,208; destination 64,512; copied/nested/alias/prefetch/trace 640; user A7
192; invalid private images 224. Addressing fixtures and mathematical register/
flag expectations are independent of production helpers. Canonical selectors
cover every word operand form with boundaries and all CCRs, both privilege modes
and RR settings, source/destination extension ordering and 24-bit physical wrapping
of logical high addresses. Additional cases cover MOVEA sign extension/flags,
partial D-register preservation, A7 bank selection, aliased postincrements,
serialized frame copies, multiple operand faults, buffered following code, trace,
and rejection of malformed opcode/phase/PC/queue/version/private words.

Common verification checks registers, PC/SR, memory/surroundings and stack banks.
The recording bus checks that only the pending cycle reruns, that completed
source accesses never repeat, and that software-completed cycles stay off the bus.
Following MOVEQ sentinels expose extension/prefetch consumption errors. The older
structural group masks the now-private continuation words; their contents and
behavior have this dedicated gate. Disabling continuation reproduces all three
bounded failures again, with sources restored and rebuilt afterward. No existing
regression is retired.

This is a bounded word-MOVE/MOVEA continuation implementation for emulator-generated
images, not a full 010 restart milestone. Long transfers, non-MOVE families, real
hardware internal-state encodings, RMW, external BERR, physical function-code
spaces, MMU/FPU and physical timing remain outside this gate. Milestone 6 stays open.

### Validation checkpoint: word-MOVE continuation

The ordinary full Release CPU suite passes **4,632 tests**, with eight optional/
opt-in skips and zero failures. All 127,776 new continuation cases pass in the
full run. The three bounded regressions fail before correction and also fail
when the continuation hook is disabled. That mutation is restored/rebuilt before
final validation. Previous reports lacking the new groups are rejected. AHX
passes 18 tests. No new seeded audit or regression retirement is claimed.

CPU evidence is under `artifacts/move-restart-before/`,
`artifacts/move-restart-focused/`, `artifacts/move-restart-mutation/`,
`artifacts/move-restart-final/` and `artifacts/move-restart-ahx/`.

Report validation passes **8,501,832 deterministic cases in 348 batches**. The
requested pinned Musashi audit executes and passes 538 program/profile combinations
with 86 explicit exclusions and zero mismatches, from revision
`72c1d74800f3087b45a0c1a7342601bbed898881`. This software audit does not qualify
010 restart against hardware; the new continuation cases use the manual and
independent synthetic fixtures.

Private unpublished package `1.5.2-synthetic-dev.38` has SHA-256
`9b67e027a617dd66f2ba43bfcba959f3a5b09f7f6b430ad9af8efbc55cf3195c`.
The isolated CopperScreen baseline `d9beae8` resolves exactly that NuGet version
in production and separate diagnostic outputs. Release builds with zero warnings/
errors; host 149, disk 74 and engine diagnostics 1,080 pass. Six ordinary host/
media skips remain unavailable coverage. The separate native Workbench 3.1 at
0/2 MiB Fast RAM and A1200 eight-plane hard-disk boot/reopen persistence execute
all three cases without skips. These are correctness replays, not throughput
or physical timing qualification. Root working changes and dependency boundaries
are preserved; no package is published.

Consumer evidence: `artifacts/move-restart-validation/`,
`artifacts/move-restart-diagnostic-tests/` and
`artifacts/move-restart-production.binlog`; CPU package evidence:
`artifacts/synthetic-private-feed-38/`. Long/non-MOVE/foreign frame restart and
the other remaining qualification gaps above stay open.

## Validation checkpoint: SingleStepTests and LINK alias sampling

The full ordinary Release CPU suite passes **4,632 tests**, with eight optional/
opt-in skips and zero failures. Report validation passes the unchanged
**8,501,832 deterministic cases / 348 batches**. The corrected stack expectation
detects 3,840 old-implementation mismatches across seven profiles; after correction
all 48,640 stack scenarios pass. The pinned SingleStepTests audit executes all
312,500 selected cases without mismatches or skips, with TAS/TRAPV explicit
exclusions. The pinned Musashi audit executes 538 passing program/profile
combinations and 86 explicit exclusions, without mismatches. AHX passes 18 tests.
No new seeded run or regression retirement is claimed.

Requested missing fixtures, empty binary fixtures, unmatched selections and case
limits all execute a failing audit. Script requests with changed pinned inputs
and unmatched filters are rejected before execution; changed inputs are restored in
`finally`. The final input manifest matches the restored corpus. These negative
checks do not qualify omitted references or physical timing.

Private unpublished package `1.5.2-synthetic-dev.39` has SHA-256
`625f4d4f30fc05c78af253f596d5ccf6f82b3674c603db6b10d4860293133e33`.
Package/API validation passes. The isolated CopperScreen baseline `d9beae8`
resolves exactly this version in production and separate diagnostic outputs.
Release builds with zero warnings/errors; host 149, disk 74 and engine diagnostics
1,080 pass. Six ordinary host/media skips remain unavailable coverage. Separate
native Workbench 3.1 at 0/2 MiB Fast RAM and A1200 eight-plane hard-disk boot/reopen
persistence execute all three cases without skips. These are correctness replays,
not throughput or physical timing measurements. No package is published, and
root working changes and the NuGet dependency boundary are preserved.

CPU evidence: `artifacts/m6-singlestep-discovery/`,
`artifacts/m6-singlestep-boundary/`, `artifacts/m6-link-before/`,
`artifacts/m6-singlestep-fixed/`, `artifacts/m6-singlestep-rejections/`,
`artifacts/m6-singlestep-final/`, `artifacts/m6-singlestep-ahx/` and
`artifacts/synthetic-private-feed-39/`. Isolated consumer evidence:
`artifacts/singlestep-validation/`, `artifacts/singlestep-diagnostic-tests/` and
`artifacts/singlestep-production.binlog`. Milestone 6 remains open for the
executable WinUAE/multi-model reference and restoration-protocol gaps above.

## WinUAE multi-model discovery checkpoint

The opt-in integer audit now executes every selected profile using the public
factory, preflights pinned binary input identities and requires a native NOP
register-corruption probe to fail. The reproducible Windows preparation command
and source revisions are in
[WinUAE conformance](../Copper68k.Tests/M68kWinUaeCpuTesterConformanceTests.md#pinned-integer-audit-across-cpu-models-milestone-6-checkpoint).
Native bridge fixes enable all requested CCR inputs/unchanged-register assertions,
close leaked header streams and release per-directory allocations. No production
CPU semantics, timing policy, package version or public API changes in this slice.

The discovery run records 11,890,943 executed callbacks over 1,381 opcode/profile
directories: 1,295 passing, 86 mismatching, zero empty executions. All eight NOP
corruption probes are detected. Callback totals include partial executions in
failing directories; they are not a count of qualified passing architectural
combinations. Mismatches include instruction results, exception/undefined-flag
expectations and callback rejection of encodings, and require independent triage.
They are not yet classified as CPU defects. No mismatch is excluded to turn this
run green. Milestone 6 remains in progress; the ordinary deterministic gate and
previous consumer qualification remain separate evidence.

Evidence: `artifacts/m6-winuae-models-discovery/` (CCR-zero false-success probe),
`artifacts/m6-winuae-models-full/` (stdio exhaustion),
`artifacts/m6-winuae-models-fixed/` (complete failing discovery),
`artifacts/m6-winuae-prepared/` (tracked preparation script output),
`artifacts/m6-winuae-checkpoint-audit/` and
`artifacts/m6-winuae-checkpoint-tests/`. Inputs and native binaries are local
artifacts, never committed. No release is published or old regression retired.

The current ordinary Release CPU suite passes **4,638 tests**, with nine optional/
opt-in skips and zero failures. This includes all six new input-validation
regressions; the newly added external model audit is optional in ordinary CI.
The fresh tracked preparation output reproduces the same 1,295 passing / 86
mismatching groups and callback counts, with all eight corruption probes detected.
A wrong source revision is rejected before creating output. PowerShell syntax and
Git whitespace checks pass. Previous production/consumer evidence remains the
`1.5.2-synthetic-dev.39` checkpoint; no new consumer replay or package is claimed
for this test-only follow-up.

## WinUAE defined flags and exception-frame verification

The native wrapper's previous exception validator skipped modern frame records,
so earlier discovery counts did not independently qualify saved frame contents.
The new test-only parser checks normal six-byte 68000 frames, format/vector words,
saved SR/PC and format-2/3/4 extra addresses using independent fixture results.
Unsupported trace-extra, combined-fault or restart records fail explicitly.
Architectural SR masks now come from M68000PM rather than the pinned generator's
zero instruction-level undefined-mask field. CPU results stay unchanged; only
undefined comparisons are masked. Defined X/SR/register assertions remain active.

Twenty-four mutations are rejected across eight profiles: wrong D0, defined X,
and actual exception-frame memory. Eight additional controls toggle only undefined
CHK flags and pass. The actual-frame mutation occurs after copying returned
registers, so it proves a separate memory assertion. Older frame-skipping bridges
are rejected before execution. The report records schema 2, assembly/input/native
identities, executed frame checks and cases using partial SR masks.

The stronger audit records 1,304 passing and 77 mismatching opcode/profile groups,
11,133,876 callbacks, 1,371,000 exception-frame checks and 199,327 masked-SR cases.
Totals include partial failing groups; these are scoped software assertions, not
qualified hardware coverage. All 32 controls pass. No family is excluded to make
this run green. The initial stricter parser's trace-record rejection was corrected
to accept ordinary trace frames; unsupported extra records still fail.

Remaining saved-PC disagreements require source qualification: the pinned TRAPcc
generator raises before advancing PC, while newer WinUAE source at revision
`6ae6fb6b84bb9517e0245a80fc9bdca1a8580dde` synchronizes PC first. M68000PM 4-189
specifies the next instruction-word address. The old generated value cannot serve
as authority for changing the CPU. Other failing families still need independent
triage. Existing production behavior and timing policy remain untouched.

Evidence: `artifacts/m6-winuae-frame-focused/`,
`artifacts/m6-winuae-frame-cpu/`, `artifacts/m6-winuae-basic-trace-inputs/`,
`artifacts/m6-winuae-basic-trace-audit/`,
`artifacts/m6-winuae-old-bridge-rejected/` and
`artifacts/m6-winuae-frame-verified/`. No package or old-test retirement is included;
milestone 6 remains in progress.

The final schema-2 run distinguishes the 77 non-passing groups as **62 mismatching
and 15 emulator-unsupported**, with zero untested groups. Unsupported callbacks
are identified by their typed `UnsupportedM68kTimingException`; they remain gate
failures, including reserved/invalid encodings whose architectural classification
still needs review. No documented processor exception is treated as implementation
unsupported merely because its vector is raised.

Ordinary Release validation passes **4,652 tests**, with nine optional/opt-in
skips and zero failures. The final focused run passes 20 rule/preflight tests
without skips; its separate external audit intentionally fails on the 77 unresolved
groups. All 32 mutation/acceptance controls pass. The old bridge is rejected with
the explicit missing-validation-export diagnostic. The final classifier report is
`artifacts/m6-winuae-frame-classified/winuae-model-audit.json`. Its counts reproduce
the stronger audit above. Preparation, PowerShell syntax and Git whitespace checks
pass. This follow-up changes only test tooling/documentation; existing .39 consumer
results remain prior evidence, without a new package or replay claim.

## Assigned illegal logical operands — 2026-10-05

The pinned ILLEGAL fixtures exposed opcode `083C` (static BTST with an immediate
destination) and `0008` (ORI.B to A0). Independent authority is
[M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf), especially
BTST 4-62/63 and the data-alterable tables for ANDI, EORI and ORI. Static BTST
excludes an immediate destination; dynamic BTST permits it. The distinction also
shows that the old `ImmediateBtstCanTestImmediateOperand` expectation was wrong.
That regression is corrected and retained; it is not retired as redundant.

The shared `logical-invalid-operands` matrix independently enumerates 207
assigned illegal logical/bit opcode words per profile, covering both stacks and
all 32 CCR states. Fixed encoding controls protect legal dynamic BTST, MOVEP and
CCR/SR forms from accidental inclusion. Registers, CCR, memory canaries, operand
reads, handler PC and saved SR/PC/frame contents are checked using the existing
common verifier. Unassigned mode-7 registers 5..7 remain outside this new matrix.
The legal addressing/bit/transfer gates separately protect legal neighboring
encodings; the implementation preserves their execution and timing policy.

Before correction, 000/010/040 each fail 64 static-BTST cases, while 020/EC020/030/
060/A1200 each report 13,248 unsupported executions. The corrected matrix passes
all **105,984 cases**. Advanced dispatch now maps assigned illegal forms to its
existing vector-4 path before operand effects; the 000 decoder removes the
incorrect static-immediate special case. No instruction is caught and retried.
The 64-batch focused legal/invalid check also passes without skips. The expanded
deterministic gate has **8,607,816 logical cases in 356 batches**. Missing the new
report group is explicitly rejected.

Final ordinary Release validation passes **4,660 CPU tests**, with nine optional/
opt-in skips and zero failures. The first full run's only failure was the stale
static-BTST expectation corrected above; the final complete run passes it. The
package/source/assembly identity snapshot is
`artifacts/m6-invalid-logical-package.json` (built from this checkpoint's working
changes before commit); no published version is changed.

Fresh pinned references pass 312,500 SingleStepTests cases in 125 verified files
(TAS/TRAPV remain explicit exclusions) and 538 Musashi program/profile cases
(86 explicit exclusions). WinUAE still fails: **1,304 passing / 62 mismatching /
15 unsupported / zero untested groups**, over 11,136,661 callbacks, 1,373,785
frame checks and 199,327 masked-SR cases. All 32 comparator controls pass.
It now reaches later ILLEGAL fixtures: `0C3C` on 000/010, `0408` on advanced
profiles and `0AC0` on 040. These require separate immediate-arithmetic/atomic
encoding and adapter review. The unchanged group totals do not mean unchanged
case coverage; the earlier failures are now passed before these later stops.
The pinned saved-PC and stack-mapping disagreements remain unresolved.

Private **unpublished** NuGet `1.5.2-synthetic-dev.40` has SHA-256
`e83bfe8d1a87004b4ee671f8b43f82e4b0bd45cb30bf94eb574d264ffcefedd3`.
The isolated CopperScreen baseline `d9beae8` resolves that exact version in
production and separate diagnostic assets. Its loaded CPU assembly hashes match
the tested CPU assembly. Release builds with zero warnings/errors; host 149,
disk 74, diagnostics 1,080 and all three native Workbench/A1200 boot/persistence
cases pass. Six optional host/media skips remain unavailable coverage; the
three requested native cases execute without skips. AHX also passes 18 tests.
These are correctness replays, not throughput/physical-timing qualification.

Evidence: `artifacts/m6-invalid-logical-before/`,
`artifacts/m6-invalid-logical-after/`, `artifacts/m6-invalid-logical-cpu/`,
`artifacts/m6-invalid-logical-final/`, `artifacts/m6-invalid-logical-winuae/`,
`artifacts/m6-invalid-logical-singlestep/`, `artifacts/m6-invalid-logical-musashi/`,
`artifacts/m6-invalid-logical-missing-group/`,
`artifacts/m6-invalid-logical-ahx-results/` and
`artifacts/synthetic-private-feed-40/`; isolated consumer
`artifacts/invalid-logical-validation/`,
`artifacts/invalid-logical-diagnostic-tests/` and
`artifacts/invalid-logical-production.binlog`.
No package is published and no old test is retired. Milestone 6 remains in progress
for the unresolved reference and restoration qualification requirements above.

## Assigned illegal arithmetic and CAS operands — 2026-10-05

The next pinned ILLEGAL failures were `0C3C` (CMPI.B with an immediate destination)
on 000/010, `0408` (SUBI.B to A0) on the advanced profiles, and `0AC0` (CAS.B D0)
on 040. Independent authority is M68000PM 4-10, 4-80 and 4-180 for immediate
arithmetic and 4-67 for CAS. CMPI excludes An/immediate destinations and gains
PC-relative forms on 020. CAS requires a memory-alterable destination; CAS2.W/.L
use separate words. The pinned WinUAE decoder tables corroborate these rules.

The new immediate-arithmetic matrix covers 99 assigned illegal words on 000/010
and 93 on 020+, both stacks and all 32 CCR states (48,384 cases). The CAS matrix
covers 54 words per profile under the same states (27,648 cases). Unassigned
mode-7 registers and the byte-CAS2 word remain outside this added matrix, with
legal word/long CAS2 retained in its existing gate. Fixed encoding controls protect
legal neighbors. These families reuse `InvalidOperandScenario`, extracted from
the preceding logical tests without changing that matrix's counts/expectations.
It uses the common register/CCR/memory/exception verifier and canaries; none of
its expectations call production decoders, EA, arithmetic or timing helpers.

Before correction, immediate arithmetic reports 576 mismatches on each of 000/010
and 5,952 unsupported executions on each of 020/EC020/030/060/A1200. Its existing
040 fallback already passes. CAS reports 3,456 mismatches per advanced profile
including 040; 000/010 already raise vector 4. This reproduces 21,888 mismatches
and 29,760 unsupported executions in the 76,032 new cases.

The corrected 000/010 plan inventory applies the legal destination constraint
even when CMPI does not write memory. Advanced dispatch maps invalid immediate
arithmetic and CAS EAs to the existing vector-4 path before operand effects.
Legal 020+ PC-relative CMPI, memory CAS and CAS2 retain their existing execution
and timing policy. No instruction is caught and retried. All new cases pass,
alongside the 64-batch affected legal/invalid gate. The complete deterministic
gate now validates **8,683,848 logical cases in 372 batches**; missing either new
report group is explicitly rejected. Ordinary Release CPU validation passes
**4,676 tests**, with nine optional/opt-in skips and zero failures.

Fresh SingleStepTests retains 312,500 passing cases across 125 verified files,
with TAS/TRAPV explicit exclusions. Musashi initially exposes a fixture caveat:
`mc68000/move.bin` encodes 020-only PC-relative CMPI.B (`0C3A`) at offset `0x160`,
against a nearby code operand. The source's local-label compare assembled to a
PC-relative EA. M68000PM 4-80 and the pinned WinUAE table require vector 4 on
000/010, but the program supplies no compatible handler. Its SHA-256 remains
`6fd7762aabf3b57b54e9a7919ff1917dea1c52b6fc12723c0c468ae3fde6086e`.
Those two program/profile rows are now explicitly excluded; the unchanged input
remains required and runs on all six applicable profiles. Final Musashi coverage
is **536 passing / 88 excluded / zero mismatching**, in 624 rows. The synthetic
matrix qualifies the unavailable CMPI form's architectural trap instead of
changing the CPU to execute it. Other program exclusions remain unchanged.

The fresh WinUAE audit passes all **34,880** selected 000 ILLEGAL cases. Its total
is **1,305 passing / 61 mismatching / 15 unsupported / zero untested groups**,
over 11,180,443 callbacks, 1,417,565 frame checks and 199,327 masked-SR cases.
All 32 comparator controls pass. It reaches later failures: `40C8` on 010 and
`0E00` on advanced profiles, requiring status-transfer and MOVES legality/privilege
ordering review. No WinUAE family is excluded to make this run pass. Generator
saved-PC, adapter stack mapping and other reference/restore disagreements remain
unresolved. Callback totals include partial groups, not exhaustive coverage.

Private **unpublished** NuGet `1.5.2-synthetic-dev.41` has SHA-256
`47bcfbc205a4d8de43b2955ff83bf43c4df293c51e0bf3914dfc8f753f5e856a`.
The source/assembly snapshot is `artifacts/m6-invalid-arithmetic-package.json`;
it records these changes built before commit. The isolated CopperScreen baseline
`d9beae8` resolves the exact package through NuGet in production and separate
diagnostic assets; both loaded DLLs match the tested CPU assembly hash. Release
builds with zero warnings/errors; host 149, disk 74, diagnostics 1,080 and all
three requested native Workbench/A1200 boot/persistence cases pass. Six optional
host/media skips remain unavailable coverage; none of the three native cases
is skipped. AHX also passes 18 tests. No throughput or physical timing claim.

Evidence: `artifacts/m6-invalid-arithmetic-before/`,
`artifacts/m6-invalid-arithmetic-after/`, `artifacts/m6-invalid-arithmetic-cpu/`,
`artifacts/m6-invalid-arithmetic-winuae/`,
`artifacts/m6-invalid-arithmetic-references/`,
`artifacts/m6-invalid-arithmetic-reference-final/`,
`artifacts/m6-invalid-arithmetic-missing-arithmetic-invalid-operands/`,
`artifacts/m6-invalid-arithmetic-missing-logical-cas-invalid-operands/`,
`artifacts/m6-invalid-arithmetic-ahx-results/`,
`artifacts/synthetic-private-feed-41/`; isolated consumer
`artifacts/invalid-arithmetic-validation/`,
`artifacts/invalid-arithmetic-diagnostic-tests/` and
`artifacts/invalid-arithmetic-production.binlog`.
The full CPU run preceded the test-only Musashi exclusion adjustment; the final
17-batch targeted run validates that adjustment and all new matrix cases, with
the production assembly unchanged. No package publication, old-test retirement,
seeded run or hardware qualification is added. Milestone 6 remains in progress.

## Assigned illegal status-transfer and MOVES operands — 2026-10-05

The pinned ILLEGAL directory next exposed `40C8` (MOVE SR,A0) on 010 and
`0E00` (MOVES.B D0) on advanced profiles. M68000PM 4-122/124/125 and 6-18/20
exclude address-register operands for status transfers; from-SR/CCR also exclude
PC-relative/immediate destinations. Section 6-25 requires memory-alterable
MOVES operands. The pinned decoder's assigned opcode tables and generated cases
corroborate vector 4 for these illegal forms, even in user state. Legal privileged
forms still raise vector 8 in user state.

`system-status-invalid-operands` enumerates 38 assigned illegal words per profile:
11 each for MOVE from SR/CCR, eight each for MOVE to SR/CCR. Both stacks and all
32 CCR inputs give 19,456 cases across the eight profiles. The MOVES matrix
enumerates 57 B/W/L words with Dn, An and PC-relative/immediate operands, using
valid D0 load and store extensions, both stacks and all CCR inputs: 58,368 cases.
The shared canary fixture verifies registers, PC, defined SR, stack selection,
complete exception frames, preserved memory and absence of operand accesses.
Normal opcode prefetch is permitted. Unassigned mode-7 registers are outside
these matrices; MOVES size 3 is separate CAS.L encoding. Fixed reference examples
exclude legal status/MOVES neighbors, which remain in their existing gates.

Before correction, the 010 status matrix has 352 mismatches. The 040 status
matrix has 704 unsupported executions, with 3,648 MOVES user-state mismatches.
The five other advanced profiles each have 2,432 unsupported status cases and
7,296 unsupported MOVES cases. Together, the new matrices detect **4,000
mismatches and 49,344 unsupported executions** in 77,824 cases. Their corrected
results are all passing, alongside the 48-batch affected legal/invalid gate.

The 010 path now validates MOVE-from-SR destinations before checking privilege.
Advanced dispatch classifies illegal status/MOVES operands through the existing
vector-4 path before effects. The 040 model-specific MOVES privilege path admits
only legal memory operands, so it cannot intercept illegal forms. Successful
execution ordering and the existing timing policy are preserved. No instruction
is caught and retried, and no package API changes.

Ordinary Release CPU validation passes **4,692 tests**, with nine optional/opt-in
skips and zero failures. The deterministic report gate validates **8,761,672
logical cases in 386 reporting batches**. Missing either new group, incorrect
report model identity and incorrect batch count are independently rejected.
The summary now derives batch totals from validated reports instead of a
hardcoded constant. The prior .41 checkpoint's summary stated 372 batches; its
required logical-case reports actually number 370. This fresh run adds 16.
Historical reports are retained unchanged; nonreporting checks and opt-in audits
are separate from the logical-case batch count.

Fresh pinned SingleStepTests retains 312,500 passes in 125 files, with the same
TAS/TRAPV exclusions. Musashi retains 536 passes and 88 explicit exclusions
across eight profiles. AHX passes 18 tests. The WinUAE audit now passes all
**34,880** selected ILLEGAL callbacks on both 000 and 010. It still fails with
**1,306 passing / 60 mismatching / 15 unsupported / zero untested groups** over
11,242,795 callbacks, including 1,479,919 frame assertions and 199,327 masked-SR
cases. All 32 comparator controls pass. It reaches later `4008` (NEGX.B A0)
failures on EC020/020/030/060/A1200, and `4C08` (long multiply from A0) on 040.
These assigned invalid forms need broader unary/multiply operand qualification.
Reference saved-PC, adapter stack mapping and internal restoration gaps remain
open. No WinUAE family is excluded; totals include partial failing groups.

Private **unpublished** NuGet `1.5.2-synthetic-dev.42` has SHA-256
`553e3baa9d201e57b5bcbdacf469f051b359c74e23e096357944310e79258e08`.
`artifacts/m6-invalid-system-package.json` records source/package/assembly
identities built before commit. The isolated CopperScreen baseline `d9beae8`
resolves this exact NuGet package in production and separate diagnostics; both
loaded CPU DLLs match the tested assembly. Release build has zero warnings/errors;
host 149, disk 74, separate diagnostics 1,080 and all three requested native
Workbench/A1200 boot/persistence cases pass. Six optional host/media skips are
unavailable coverage; no requested native case is skipped. This is correctness
evidence, not throughput or physical timing qualification.

Evidence: `artifacts/m6-invalid-system-before/`,
`artifacts/m6-invalid-system-after/`, `artifacts/m6-invalid-system-cpu/`,
`artifacts/m6-invalid-system-references/`, `artifacts/m6-invalid-system-winuae/`,
`artifacts/m6-invalid-system-guard-missing-status/`,
`artifacts/m6-invalid-system-guard-missing-moves/`,
`artifacts/m6-invalid-system-guard-wrong-model/`,
`artifacts/m6-invalid-system-guard-wrong-batches/`,
`artifacts/m6-invalid-system-ahx-results/`, `artifacts/synthetic-private-feed-42/`;
isolated consumer `artifacts/invalid-system-validation/`,
`artifacts/invalid-system-diagnostic-tests/`,
`artifacts/invalid-system-production.binlog`. No old test is retired, no package
is published and no new seeded or physical audit is claimed. Milestone 6 remains
in progress with the accepted scope unchanged.

## Unary, multiply/divide, CHK, bitfield and debug instructions — 2026-10-05

The next pinned ILLEGAL cases expose NEGX.B An, long multiply/divide An,
CHK An and illegal bitfield operands. M68000PM 4-31..52, 4-70,
4-93..98, 4-111, 4-136..142 and 4-193 provide the independent EA rules.
The new matrices cover all register encodings of these assigned invalid forms,
both stacks and all 32 initial CCR values. Long multiply/divide uses four valid
signed/unsigned and 32/64-bit extensions, proving that an invalid EA enters
vector 4 before the 060 unavailable-operation decision. Bitfields distinguish
read-only PC-relative sources from mutating destinations. The shared fixture
checks complete frames, preserved registers/memory and absent operand effects.
Unassigned mode-7 registers and reserved extension bits remain outside this
assigned-operand slice; existing legal matrices remain required.

Two aliases require positive qualification. NBCD mode-1 words are LINK.L on
020+, and 060 TAS mode-1 words `4AC8`/`4ACC` are HALT/PULSE. Preliminary unary
fixtures incorrectly classified these aliases; the corrected baseline excludes
them and tests their legal behavior separately. These fixture errors are not
CPU defects. MC68060UM 9.2.2 / 9-30 defines privileged HALT, no interrupt restart,
and user-accessible PULSE. Both instructions now appear in the integer inventory.
HALT holds the next PC with no subsequent trace entry; PULSE preserves integer
state and follows ordinary T1 tracing. Reset recovers HALT; idle, interrupt and
host-entry operations cannot wake it. Physical PST signals, debug-port restart,
pipeline toggling and physical timing remain unavailable qualification.

The six new groups contain **421,120 cases in 48 reporting batches**:
unary 82,944; word multiply/divide 131,072; long multiply/divide 32,768;
CHK 65,536; bitfield 106,496; debug instructions/recovery 2,304. The corrected
baseline against production `424ad4e` records **226,304 passing, 5,120
mismatching, 189,440 unsupported and 256 untested cases**. The untested recovery
edges depend on HALT first executing successfully. Word multiply/divide already
passes; it needs no production correction. Advanced dispatch now rejects the
other assigned illegal operands before effects. The 060 handler intercepts its
legal debug aliases before TAS legality checks. Post-instruction tracing cannot
process a halted CPU; STOP retains its trace behavior. All 158 focused checks
pass, including all new cases, legal bitfields and affected trace/STOP checks.
Existing successful ordering and timing policy are preserved, with no partial
instruction retry and no public package API change.

Final ordinary Release validation passes **4,740 CPU tests**, with nine optional
skips and zero failures. The report gate validates **9,182,792 logical cases in
434 reporting batches**; deleting each of the six new 000 reports independently
fails the selected-model report gate. Fresh SingleStepTests retains 312,500
passes in 125 files and Musashi retains 536 passes / 88 explicit exclusions.
AHX passes 18 tests. Existing input pins and exclusion caveats apply unchanged.

The fresh WinUAE audit remains failing: **1,308 passing, 60 mismatching, 13
unsupported and zero untested groups**, with 11,263,752 callbacks, 1,500,873
frame assertions and 199,327 masked-SR cases. All 32 comparator controls pass.
HALT and PULSE each pass their two selected callbacks, which alone do not qualify
supervisor HALT/recovery or trace; the synthetic matrix supplies those cases.
000/010 still pass all 34,880 selected ILLEGAL callbacks. Other advanced profiles
now reach unassigned CHK word `413D`; 040 reaches `F300` FPU/ILLEGAL expectations.
These, saved-PC reference caveats, adapter stack conventions and internal RTE
restoration remain separate work. No family is excluded to make the audit pass.

Private **unpublished** NuGet `1.5.2-synthetic-dev.43` has SHA-256
`5f2e9252a598a9e2d803eacfae89597c566f916f11c9c62bc22d6c7d8645b2ad`.
`artifacts/m6-invalid-integer-package.json` records production source and assembly
identities against `424ad4e`. The isolated CopperScreen baseline `d9beae8`
resolves this exact package for production and separate diagnostics. All four
loaded consumer CPU DLLs match SHA-256
`72078b2730ddebd29106a6614cb9cbb082ba476c7d133519a6503a9d23aedca7`.
Release build has zero warnings/errors; host 149, disk 74 and separate engine
diagnostics 1,080 pass. All three native Workbench/A1200 boot and disk-persistence
cases pass without skips. Six optional host/media skips remain unavailable
coverage. These checks qualify correctness, not throughput or physical timing.

Evidence: `artifacts/m6-invalid-integer-qualified-baseline/`,
`artifacts/m6-invalid-integer-final-focused/`, `artifacts/m6-invalid-integer-cpu/`,
`artifacts/m6-invalid-integer-references/`, `artifacts/m6-invalid-integer-winuae/`,
`artifacts/m6-invalid-integer-guard-*/`, `artifacts/m6-invalid-integer-ahx-results/`
and `artifacts/synthetic-private-feed-43/`; isolated consumer
`artifacts/invalid-integer-validation/`,
`artifacts/invalid-integer-diagnostic-tests/` and
`artifacts/invalid-integer-production.binlog`. Earlier preliminary unary runs
retain their fixture mistakes as historical evidence, not corrected counts.
No package publication, old-test retirement, seeded or physical audit is added.
Milestone 6 remains in progress with its existing scope intact.

## PACK/UNPK word stride and terminal reference boundaries — 2026-10-05

PACK's unpacked source and UNPK's unpacked destination are contiguous words,
including when addressed through A7. Only the packed byte operand uses A7's
special two-byte stride. The original production code and synthetic fixture
both incorrectly applied that byte stride to each half of the word, consuming
four bytes and leaving a gap. Independent expectations now treat the word as
one two-byte operand. M68000PM 4-156..158 and 4-195..197 define the operand
diagrams and transformations; MC68020UM table 9-21 corroborates the word-side
operand access. Pinned WinUAE PACK/UNPK code agrees. The pinned Musashi
instruction source retains separate A7 byte decrements and is not corroborating
evidence for this correction; its passing self-checking program audit does not
contain a discriminating packing program.

The new `arithmetic-packing-memory` group executes **89,728 cases in eight
reporting batches**: 8,192 per 000/010 profile and 12,224 per advanced profile.
Canonical cases exercise every source/destination register encoding, all CCR
states and both stacks. Advanced cases add boundary values and adjustments,
aliased bases, overlapping operands, odd addresses, negative high addresses
and external-address wrapping. Surrounding canaries, complete register state,
exact next PC and unchanged flags remain checked. Unavailable 000/010 packing
instructions must enter vector 4. Fixed opcode examples independently check
the fixture encoding.

Against production `66d276e`, these revised expectations detect **14,208
mismatches**, 2,368 per advanced profile, with zero unsupported or untested new
cases. The corrected existing decimal fixture additionally detects 32 A7
packing failures per advanced profile. All 16 affected batches pass after the
production stride correction. Byte transfer order and the existing timing
policy are preserved; physical bus width, intermediate A7 visibility and
silicon timing are not qualified. No instruction is retried after operand
effects, no public API changes and no old regression is retired.

The WinUAE adapter now ends an integer callback when execution becomes stopped
or halted. It compares actual state immediately, without waking the CPU,
advancing PC to a sentinel or normalizing the result. Schema 2 adds
`TerminalCases` to rows and `terminalCases` to totals. A fresh all-profile audit
passes all **12 PACK/UNPK groups / 70,720 callbacks**. Its complete result is
**1,320 passing, 48 mismatching, 13 unsupported and zero untested groups** over
11,311,137 callbacks, 1,500,873 exception frames and 199,327 masked-SR cases;
all 32 comparator controls pass. There is one terminal callback. The 060 STOP
case `4E72 0000` now exposes a state disagreement rather than waiting 64 steps:
Copper68k clears S and stops, while the pinned reference expects vector 8 and
the old SR. The applicable model rule and saved-PC reference need qualification;
this adapter change does not fix or hide that disagreement. No family is
excluded and the requested discovery audit remains failing.

Evidence: `artifacts/m6-packing-before/`, `artifacts/m6-packing-after/`,
`artifacts/m6-packing-winuae/`, `artifacts/m6-terminal-winuae/` and
`artifacts/m6-packing-final-winuae/`. Earlier runs retain their identities and
counts. Milestone 6 remains in progress with its accepted scope intact.

Final ordinary Release validation passes **4,748 CPU tests**, with nine optional
skips and zero failures. Fresh SingleStepTests passes 312,500 cases in 125 files;
Musashi passes 536 programs with 88 explicit exclusions; AHX passes 18 tests.
Existing source pins and reference caveats apply unchanged. The deterministic
report gate validates **9,272,520 logical cases in 442 reporting batches**.
Omitting the new 000 packing report fails the selected-model gate.

Private **unpublished** NuGet `1.5.2-synthetic-dev.44` has SHA-256
`a4230a7efa4b1b8e0bd374862822483a9caea4e14e9925f3bfa2ab280b2771a2`.
`artifacts/m6-packing-package.json` records source and assembly identities
against `66d276e`. The isolated CopperScreen baseline `d9beae8` resolves the
exact package in production and separate diagnostics. All four loaded CPU DLLs
match SHA-256
`9404ef080ea6c1f6ba44b933df3683b42b6aff6e163100b4bff896074915b8a0`.
Release build has zero warnings/errors; host 149, disk 74 and engine diagnostics
1,080 pass. Two native Workbench floppy boot profiles and one native A1200
boot/disk-persistence replay pass. An additionally selected Workbench hard-disk
theory is skipped without its HDF environment input; it is unavailable coverage,
not a replay. Six optional host/media skips remain unavailable. No new seeded,
physical timing or host-throughput qualification is claimed.

Final evidence: `artifacts/m6-packing-cpu/`, `artifacts/m6-packing-references/`,
`artifacts/m6-packing-guard-missing/`, `artifacts/m6-packing-ahx-results/`,
`artifacts/synthetic-private-feed-44/`; isolated consumer
`artifacts/packing-validation/`, `artifacts/packing-diagnostic-tests/` and
`artifacts/packing-production.binlog`. No package is published.

## Synchronous trap and trace priority — 2026-10-05

The shared advanced core stacked a pending trace immediately after completed
instruction traps on every advanced model. This is wrong for 040 and 060.
[MC68040UM 8.3, 8-20](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
suppresses trace when a priority-3 synchronous exception wins;
[MC68060UM 8.2.6/8.3, 8-11/8-18](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
also suppresses it. After RTE restores T1, the next executed instruction is
traced. Earlier models retain their existing nested trace behavior. MC68020UM
6.1.11 explicitly describes trap processing followed immediately by trace
processing; its 6.1.4 wording about tracing after the trap handler's RTE is
inconsistent with that multiple-exception description. The explicit priority
example and WinUAE's model distinction corroborate the retained ordering.

The new `system-trap-trace` group contains **50,496 cases in eight reporting
batches**: 3,968 on 000/010, 5,952 on EC020/020/030/A1200, 10,368 on 040 and
8,384 on 060. It covers all TRAP vectors, taken/untaken TRAPV, word/long divide
by zero, word/long CHK negative/above-bound traps, and all three immediate
lengths of true/false TRAPcc. Canonical fixtures use all initial CCRs and both
user/supervisor stacks, with no trace, T1 and applicable T0. Unsupported old-model
forms require vector 4. Defined flags, preserved registers/memory, complete
frames and exact exception-entry counts are checked. 040/060 additionally
execute RTE from real format-0/2 trap frames and resume a traced self-branch.
M-mode, physical fault sequencing and advanced internal restart are not added
to this group.

Against `4a32a1f`, the revised group detects **8,224 mismatches**, with **5,888
dependent return/resume cases untested** until correct entry works. There are
zero emulator-unsupported cases. The corrected older trace fixture additionally
detects 192 failures on 040 and 96 on 060. The production fix suppresses the
second exception only on those models. It preserves the first frame, operand
effects and existing timing plans; there is no instruction retry or public API
change. All 16 synthetic batches pass after correction.

The pinned WinUAE generator independently contains the same overgeneralized
trace rule. Its unchanged Basic preset never enables incoming trace rounds,
so its passing TRAP groups did not expose this. A separate `TraceTraps` preset
applies the committed one-line [generator patch](../scripts/winuae/trace-priority.patch)
to a copied source file. Original tracked sources and all existing Basic
fixture identities remain unchanged. The corrected source, patch and compiler
output have explicit hashes; normalized-text authority handles checkout line
endings while exact manifested bytes must still match. This is a qualified
correction to an independent software reference, not unchanged upstream or
hardware evidence. The pinned newer local WinUAE `newcpu.cpp` revision
`6ae6fb6b84bb9517e0245a80fc9bdca1a8580dde`, `exception_check_trace`, also retains
pending instruction-trap trace only below 040.

The qualified preset passes **512 TRAP callbacks / 512 frame assertions**,
including **256 incoming-T1 callbacks**, across 040/060. It covers T1/S
combinations and CCR 0/31; it does not claim all CCRs externally. All six
register, defined-X and frame corruption controls pass. Temporarily removing
the CPU correction makes both models fail at their first incoming-T1 callback
(fifth callback per model), preserving eight earlier frame comparisons. Native
diagnostics report expected TRAP vector 32 versus actual trace vector 9 and
incorrect saved SR. Empty profile selection and changed fixture data are
rejected before native execution. The requested audit also requires exact
256/128/256 callback/trace/frame counts per model. Other traced families/models,
extra trace/fault records and M-mode remain untested by this focused preset.
Preparation and audit commands are in [WinUAE conformance](../Copper68k.Tests/M68kWinUaeCpuTesterConformanceTests.md#qualified-trap-trace-preset-for-040060).

The unchanged broad Basic audit still fails: **1,320 passing, 48 mismatching,
13 unsupported and zero untested groups** over 11,311,137 callbacks, with
1,500,873 frames, 199,327 masked-SR cases and one terminal callback. All 32
controls pass. The focused trace preset does not replace it or exclude families.
STOP, saved-PC reference disagreements, reserved words, advanced restoration
and the other previously recorded gaps remain open.

Full ordinary Release CPU validation passes **4,756 tests**, with ten optional
skips and zero failures. The final focused gate passes 23 tests, including the
new trace audit and retained input-preflight checks. The deterministic gate
validates **9,323,016 logical cases in 450 reporting batches** and rejects a
missing new trace report. Fresh SingleStepTests passes 312,500 cases / 125 files,
Musashi passes 536 programs with 88 exclusions and AHX passes 18 tests.
Source pins, exclusions and physical-timing caveats apply unchanged.

Private **unpublished** NuGet `1.5.2-synthetic-dev.45` has SHA-256
`f44f0f2f8b4566dc3fabd4749aca585bb84807e3202fb4a25b30c0b91e004d47`.
`artifacts/m6-trap-trace-package.json` records source/assembly identities against
`4a32a1f`. The isolated CopperScreen `d9beae8` baseline resolves the exact package
in production and separate diagnostics. All four loaded CPU DLLs match SHA-256
`37e71d96a4cb26e4c2309197d56c846ee6a81d73b290362598cf7f3e6b7f7614`.
Release build has zero warnings/errors; host 149, disk 74 and engine diagnostics
1,080 pass. Three native Workbench/A1200 boot and A1200 disk-persistence replays
pass with no skips. Six optional host/media skips remain unavailable coverage.

Evidence: `artifacts/m6-trap-trace-before/`, `artifacts/m6-trap-trace-after/`,
`artifacts/m6-trap-trace-qualified-inputs/`,
`artifacts/m6-trap-trace-reference-before/`,
`artifacts/m6-trap-trace-final-qualified/`, `artifacts/m6-trap-trace-cpu/`,
`artifacts/m6-trap-trace-references/`, `artifacts/m6-trap-trace-basic-winuae/`,
`artifacts/m6-trap-trace-guard-*/`, `artifacts/m6-trap-trace-ahx-results/` and
`artifacts/synthetic-private-feed-45/`; consumer
`artifacts/trap-trace-validation/`, `artifacts/trap-trace-diagnostic-tests/`
and `artifacts/trap-trace-production.binlog`. The initial preparation attempt
failed on a relative source-file path; the corrected generator preparation
completes and preserves that failed attempt as historical evidence. No package
publication, regression retirement, seeded, host-performance or physical audit
is added. Milestone 6 remains in progress with its accepted scope unchanged.

### Translation-control MOVEC register image — 2026-10-05

The broad audit stopped in `MOVEC2` at callback 31 on both 040 and 060.
040 wrote `FFFF7FFF` into TC without masking it, accidentally activating the
private MMU state's high-bit enable convention and faulting before readback.
060 returned `00007FFF`, including reserved bit zero, instead of `00007FFE`.
[MC68040UM 3.1.2 / figure 3-4](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
defines the implemented E/P bits and zero reads for the remaining bits.
[MC68060UM 4.1.2 / figure 4-4](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
defines zero reads for bits 31–16 and bit 0. Architectural MOVEC reads/writes
now use fixed masks `0000C000` and `0000FFFE`, respectively. The existing private
MMU state conventions and cache/ATC execution policy are retained; this does
not qualify enabled MMU translation, ATC flushing or physical timing.

The prior canonical MOVEC fixture also expected an unmasked 040 TC write.
Its expectation is corrected; the retained MMU register-transfer regression
now uses the implemented page-size bit with translation disabled. No test is
retired. New independent `system-translation-control` batches contain
**145,280 cases**, 72,640 per model. They exercise 38 deterministic write
values (boundaries and walking bits), D0–D7/A0–A6, all 32 CCR states, privilege
rejection without TC changes, dependent readback and a following sentinel.
Another 35 internally supplied read values cover every general register,
including A7, and verify reads have no register-state side effect. The retained
canonical inventory covers the A7 write. Architectural E remains clear in the
new inputs; the raw-state read cases also avoid the private enable bit.
Nonzero writes to reserved bits are robustness samples and emulator storage
canonicalization checks, not claims about a legal hardware programming
sequence: both manuals require reserved bits to be written as zero. The
independent read expectations enforce the documented zero-read behavior.

Against production `8ffc8df`, these batches record **80,768 passing,
45,312 mismatching, zero unsupported and 19,200 untested dependent phases**.
All 145,280 pass after the correction. A failed write never causes a partial
instruction retry or a claimed readback pass. The ordinary report gate requires
both new batches with their exact counts and rejects omission of the 040 report.

The current unchanged broad WinUAE inputs/bridge still fail, with **1,320
passing, 48 mismatching, 13 unsupported and zero untested groups**. They execute
11,311,153 callbacks, 1,500,881 frame assertions, 199,327 masked-SR cases and one
terminal callback; all 32 controls pass. Both `MOVEC2` failures advance eight
callbacks, from 31 to 39, and now stop at ITT0 (`FFFF6364` expected,
`FFFF7FFF` actual). Transparent-translation and root-pointer register masks
remain open; neither complete MOVEC-family qualification nor a green broad
audit is claimed. An initial run used the older frame-only bridge, producing
25 additional trace-record mismatches. That result is retained in
`artifacts/m6-tc-winuae/`; the comparable current result is
`artifacts/m6-tc-current-winuae/` using the earlier Basic manifest
`37cd8ddae8b61ac60e3a49ba835362fbf80d418f48c2539338e6541c9d85e31f`
and native library
`75f0c11352d4a8c1e84f32a49a5347ae4cb5bd9128f1501a33d843e6f99cb057`.
No inputs or comparison masks are altered to obtain this progress.

Ordinary Release CPU validation passes **4,758 tests**, with ten optional
skips and zero failures; the focused MOVEC/MMU gate passes 26 tests. The report
gate validates **9,468,296 cases in 452 batches**, with `roadmapComplete=false`.
Pinned SingleStepTests passes 312,500 cases in 125 files; pinned Musashi passes
536 programs with 88 explicit exclusions. Input hashes, source pins and complete
selection are rechecked by the report/reference script. The existing qualified
040/060 TRAP trace audit passes 512 callbacks / 256 incoming-T1 cases / 512
frames and all six controls against this CPU. AHX passes 18 tests.

Private **unpublished** package `1.5.2-synthetic-dev.46` has SHA-256
`d7ee51601e409f4f0663deda3d00667700d1bc5db881fb3ce47bd13e8972b110`;
`artifacts/m6-tc-package.json` records both changed production sources and the
tested CPU DLL identity
`b39bae5cfe09e45c124ae9a705d4e33b014936234dc9e558e548174d30103e4f`.
The isolated CopperScreen baseline `d9beae8` resolves that exact package in
production and separate diagnostics. All four loaded DLLs match. Release build
has zero warnings/errors; host 149, disk 74, engine 1,080 and three native
Workbench/A1200 boot and disk-persistence replays pass. Six optional host/media
skips remain unavailable coverage. The first host invocation used a nonexistent
project path and did not execute tests; its corrected invocation passes.

Evidence: `artifacts/m6-tc-before/`, `artifacts/m6-tc-focused/`,
`artifacts/m6-tc-cpu/`, `artifacts/m6-tc-current-winuae/`,
`artifacts/m6-tc-trace-reference/`, `artifacts/m6-tc-missing-report-all/`,
`artifacts/m6-tc-ahx-results/` and `artifacts/synthetic-private-feed-46/`;
consumer `artifacts/tc-validation/`, `artifacts/tc-diagnostic-tests/` and
`artifacts/tc-production.binlog`. The initial missing-report probe lacked other
models' reports and did not isolate the new gate; the final probe includes every
other required batch and fails specifically for the missing 040 TC report.

The 010 RTE investigation also distinguishes two unresolved observations.
The Basic corpus fails on format-error N/Z/V changes, not incoming trace;
the pinned generator explicitly derives these flags from the rejected format
word. Current WinUAE source (`6ae6fb6b84bb9517e0245a80fc9bdca1a8580dde`,
`newcpu.cpp`, `exception_check_trace`) and pinned generator `025b999` retain
trace for a 010 format error. MC68000UM 6.3.8 describes instruction-forced
exceptions preceding trace, while 6.4 calls version rejection an aborted RTE.
Those passages do not establish every partial CCR/trace effect at every
validation stage. No production or expectation change is made on this evidence
alone. Stage-specific qualified fixtures or verified hardware evidence remain
required. All earlier restoration, reference, adapter and consolidation gaps
remain open. No release, regression retirement or physical audit is added;
milestone 6 remains in progress with its full scope unchanged.

### Transparent-control and legal root-pointer MOVEC images — 2026-10-05

MOVEC now masks all four 040/060 transparent-translation registers to
`FFFFE364` on architectural reads and writes. [MC68040UM 3.1.3 / figure
3-5](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf) and
[MC68060UM 4.1.3 / figure
4-5](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf) define bits 12–10,
7, 4, 3, 1 and 0 as always reading zero. Existing enabled-TTR rejection on 060,
040 cache/ATC policy and instruction execution order remain unchanged. Reserved
nonzero writes exercise robustness and storage canonicalization; they do not
claim compliant hardware programming. Enabled MMU/TTR translation and physical
timing remain outside this semantic qualification.

A shared test-internal MOVEC fixture now serves TC, TTR and root-pointer tests.
It verifies registers, CCR, exact PC, privilege entry, untouched control state,
dependent readback and a following instruction sentinel. It computes expectations
from fixed encodings/masks without production decoder or MMU helpers. The two
existing TC batches retain their 145,280 logical cases and gain control-state
preservation checks; no old regression is retired. The older canonical MOVEC
fixture's unmasked TTR expectation is corrected independently.

Eight `system-transparent-control-{4,5,6,7}` batches add **622,592 cases**:
77,824 per register/model, using 38 disabled values, every general register
including A7, both stacks, every CCR, privileged writes/readback and raw reads.
Four `system-root-control-{806,807}` batches add **221,184 cases**: 55,296 per
register/model with 27 legal aligned values and the same transfer dimensions.
Both manuals require root-pointer bits 8–0 to be written zero. That rule alone
does not define nonaligned write/read behavior: the pinned reference preserves
those bits on 040 and masks them on 060. This checkpoint qualifies legal aligned
transfers without changing root-pointer production behavior or claiming either
nonaligned convention as hardware authority.

Before the TTR fix, against production `e4f8bb6`, the TTR groups record
**450,560 passing, 114,688 mismatching, zero unsupported and 57,344 untested
dependent readback cases**. All legal root and refactored TC groups already pass.
After correction, all **843,776 new cases** pass. A failed prerequisite write
never retries an instruction or claims a dependent readback pass. The focused
MOVEC/MMU gate passes 38 tests.

The unchanged broad WinUAE audit advances 040 `MOVEC2` from its first ITT0
failure to **16,384 passing callbacks**. The 060 sequence advances from callback
39 to callback 71 and now exposes BUSCR: after writing `FFFFFFFF`, the reference
expects `A0000000` while the CPU returns `F0000000`. Pinned reference code
preserves SL/SLE while MOVEC writes L/LE. BUSCR shadow-write and nested-exception
qualification remain open; no production correction is inferred solely from
that software agreement. The complete broad audit still fails, with **1,321
passing, 47 mismatching, 13 unsupported and zero untested groups**, 11,327,530
callbacks, 1,517,229 frame assertions, 199,327 masked-SR cases and one terminal
callback. All 32 comparator controls pass. Generator/runner pins, original
Basic manifest `37cd8ddae8b61ac60e3a49ba835362fbf80d418f48c2539338e6541c9d85e31f`
and library `75f0c11352d4a8c1e84f32a49a5347ae4cb5bd9128f1501a33d843e6f99cb057`
are unchanged. No family is excluded or comparison weakened to obtain progress.

Ordinary Release CPU validation passes **4,770 tests**, with ten optional
skips and zero failures. The deterministic report gate validates **10,312,072
logical cases in 464 reporting batches**, with `roadmapComplete=false`.
Omitting only the new 040 ITT0 report rejects the gate. Fresh pinned
SingleStepTests passes 312,500 cases in 125 files; pinned Musashi passes 536
programs with 88 exclusions. The script rechecks source revisions, input
identities and complete selections. AHX passes 18 tests. The qualified 040/060 TRAP trace
audit passes 512 callbacks, 256 incoming-T1 cases, 512 frame assertions and
six comparator controls against this CPU; its original qualification caveats
apply unchanged.

Private **unpublished** NuGet `1.5.2-synthetic-dev.47` has SHA-256
`fee96e4233a340c0564f3975bb278fe0a94882ddb92073aac20fb21b63f87c20`.
`artifacts/m6-ttr-package.json` records both changed production source hashes
against `e4f8bb6` and CPU DLL identity
`b6311bcc1b25d5d18ed5a92780d589c3d9251f1efb0a2884b259ef85883adde0`.
The isolated CopperScreen `d9beae8` baseline resolves the exact package in
production and separate diagnostics; all four loaded CPU DLLs match. Release
build has zero warnings/errors. Host 149, disk 74, engine diagnostics 1,080
and three native Workbench/A1200 boot and disk-persistence replays pass.
Six optional host/media skips remain unavailable coverage.

Evidence includes `artifacts/m6-ttr-before/`, `artifacts/m6-ttr-focused/`,
`artifacts/m6-ttr-discovery/`, `artifacts/m6-ttr-winuae/`,
`artifacts/m6-ttr-full/`, `artifacts/m6-ttr-trace-reference/`,
`artifacts/m6-ttr-missing-report/`, `artifacts/m6-ttr-ahx-results/`
and `artifacts/synthetic-private-feed-47/`;
consumer `artifacts/ttr-validation/`, `artifacts/ttr-diagnostic-tests/`
and `artifacts/ttr-production.binlog`. All earlier restoration, 010 format-error,
reference-adapter and consolidation gaps remain open. No package publication,
regression retirement, seeded audit or host/physical timing qualification is
added; milestone 6 remains in progress with its accepted scope.

### 060 BUSCR snapshots and MOVEC control-field legality — 2026-10-05

MOVEC BUSCR writes now update L/LE (`A0000000`) and preserve SL/SLE
(`50000000`). [MC68060UM 7.4](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
describes software lock commands and exception snapshots. Preserving shadows
across software writes is the interpretation of that snapshot model corroborated
by pinned WinUAE `newcpu_common.cpp`, rather than an explicit read-only sentence
or hardware measurement. Reserved nonzero writes are robustness samples; they
are canonicalized to implemented command bits. Raw reads cover all 16 legal
upper-nibble images, not arbitrary internally corrupted reserved bits.

Exception entry now accumulates active L/LE into SL/SLE and clears L/LE while
retaining existing shadows. The user manual's generic copy wording does not
spell out nested retention. The later Motorola [MC68060AR section 5, printed
page 6](https://www.nxp.com/docs/en/supporting-information/MC68060AR.pdf)
explicitly states that the processor does not clear SL and nested exceptions
must not lose the lock state, with equivalent LE/SLE behavior. That clarification
is the independent expectation. Reset clears BUSCR; RTE does not automatically
restore software lock commands. Current WinUAE `newcpu.cpp` has a different
exception update, so this nested-state expectation is not inferred from
software agreement. Physical LOCK/LOCKE timing, cache bypass, locked access
faults and actual CAS2 software emulation remain unqualified.

Three deterministic BUSCR groups add 315,408 cases: 247,808 command writes,
dependent readbacks, privilege and raw reads; 65,536 TRAP entries, nested TRAPs
and both RTE phases; and 2,064 privilege/trace/illegal/reset checks. They cover
all general registers including A7, all CCRs, both stacks and all four shadow
images. Against `964266e`, they detect respectively **119,808 / 15,360 / 896
mismatches**, with **59,904 / 37,888 / zero** dependent cases untested. All
315,408 pass after the fix. A fourth group adds 4,096 interrupt cases across
accepted/masked requests and running/STOP state. Replacing only the exception
update with its original expression detects 896 interrupt mismatches; 3,200
other cases pass. Production source is restored after each mutation.

With BUSCR corrected, the unchanged external 060 `MOVEC2` sequence advances
from callback 71 to 73: undefined control field `009` in user state expected
vector 4 but got vector 8. [MC68060UM
8.2.4](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf) explicitly identifies
undefined MOVEC register fields as illegal. The pinned generator corroborates
legality before privilege on 060. The interpreter now validates that field before
privilege, fetching the same opcode/extension words as before and performing
no transfer on rejection. Other models keep their prior privilege convention;
legal 060 transfers retain their successful execution order and timing policy.
The canonical fixture's two erroneous expectations are corrected; none is retired.

`system-movec-control-encodings` adds **536,832 cases**. Every one of the
4,082 undefined fields is tested in both directions, all 16 general registers,
both stacks and CCR 0/31; all 14 implemented fields are tested in user state
for every CCR and register. Assertions include saved PC/SR, frame/stack state,
untouched registers and control images. Disabling only the 060 legality check
detects **261,248 mismatches** with 275,584 passing and zero unsupported/untested
cases. All cases pass with the guard restored. BUSCR/encoding additions total
**856,336 cases in five reporting batches**. A failing prerequisite is never
retried or counted as a passing dependent phase.

The unchanged broad WinUAE audit now passes 060 `MOVEC2` (**8,228 callbacks /
8,192 frame assertions**) and the already-passing 040 sequence. This is the
generated group's scope, not exhaustive external qualification of every MOVEC
register and initial image. Overall the audit remains failing: **1,322 passing,
46 mismatching, 13 unsupported and zero untested groups**, over 11,335,687
callbacks, 1,525,385 frames, 199,327 masked-SR cases and one terminal callback.
All 32 controls pass. The original Basic manifest and library identities recorded
above are unchanged; no comparison mask or family exclusion is added. The
original PCR diagram uses EDEBUG bit 7 while pinned WinUAE writes bit 6; the
current addendum does not resolve that difference. No PCR production correction
is made from software agreement alone.

Evidence includes `artifacts/m6-buscr-before/`, `artifacts/m6-buscr-focused/`,
`artifacts/m6-buscr-control-focused/`, `artifacts/m6-movec-legality-before/`,
`artifacts/m6-buscr-interrupt-before/`, `artifacts/m6-buscr-discovery/`
and `artifacts/m6-buscr-winuae/`. Milestone 6 remains in progress: prior
restoration, stage-specific 010 format-error, reference and consolidation
requirements remain. No package publication, regression retirement or physical
qualification is added.

Final Release validation passes **4,775 CPU tests** with ten optional skips and
zero failures. The deterministic gate validates **11,168,408 logical cases in
469 reporting batches**, with `roadmapComplete=false`. Omitting only the new
060 control-encoding report rejects the gate. Fresh pinned SingleStepTests passes
312,500 selected cases in 125 files; Musashi passes 536 programs with 88
exclusions. The script rechecks source revisions, exact inputs and selection.
AHX passes 18 tests. The qualified TRAP trace preset passes 512 callbacks,
256 incoming-T1 cases, 512 frames and six controls against this CPU.

Private **unpublished** NuGet `1.5.2-synthetic-dev.48` has SHA-256
`de7ce3911783486a52077c0213684c1f722037e064eda1131e67c27b4d00b7c5`.
`artifacts/m6-buscr-package.json` records all three changed production source
hashes against `964266e` and CPU assembly identity
`624cd5631ea0e33e5eba2e5de0aa85e6dd8a4c55541b561df1cccbfa9812ba15`.
The isolated CopperScreen `d9beae8` baseline resolves the exact package in
production and separate diagnostics, with all four loaded DLLs matching that
identity. Release build has zero warnings/errors. Host 149, disk 74 and engine
diagnostics 1,080 pass. Three native Workbench/A1200 boot and disk-persistence
replays pass with no skips; six optional host/media skips remain unavailable
coverage. Evidence: `artifacts/m6-buscr-full/`,
`artifacts/m6-buscr-missing-report/`, `artifacts/m6-buscr-trace-reference/`,
`artifacts/m6-buscr-ahx-results/` and `artifacts/synthetic-private-feed-48/`;
consumer `artifacts/buscr-validation/`, `artifacts/buscr-diagnostic-tests/`
and `artifacts/buscr-production.binlog`. No seeded, physical timing or host
performance qualification is claimed.

## TRAPcc and CHK2 saved-PC reference qualification (2026-10-05)

The pinned Basic generator raises CHK2 and TRAPcc exceptions before committing
its pending PC offset. For example, `50FA 0095` at `0087FFA0` expects stacked
`0087FFA0` instead of `0087FFA4`; `00D0 0800` has the same discrepancy for CHK2.B.
These are reference defects, not evidence for changing the CPU. The authority is
[M68000PM 4-189](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf),
[MC68020UM 6.1.4](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf),
[MC68040UM 8.2.3](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
and [MC68060UM 8.2.3](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf):
instruction traps save the following instruction address and retain the causing
instruction address separately. PM 4-71 defines CHK2's X/Z/C and undefined N/V.

The new `TrapBounds` preset applies exactly two anchored additions of
`sync_m68k_pc()` in a copy of pinned `gencpu.cpp`, before the CHK2 and TRAPcc trap
conditions. It retains the generator/runner pins and native assertion bridge.
Original tracked reference sources, Basic inputs and trace preset stay intact.
The common profile preflight accepts an explicit required family set; its
existing Basic and TraceTraps defaults retain their strict original selections.
No production CPU code, timing policy, SR comparison mask or exception frame
expectation changes. A1200 uses the independently selected EC020 fixture profile.

The focused preset requires all five advanced fixture profiles and audits all
six CPU profiles including A1200. CHK2 is unavailable in 060 hardware (UM 8.2.4),
so that model's focused selection is TRAPcc only; the existing synthetic bounds
suite retains its required architectural exception coverage. This does not
exclude any directory from the unchanged broad audit.

| Profile | CHK2.B callbacks / frames | CHK2.W callbacks / frames | CHK2.L callbacks / frames | TRAPcc callbacks / frames |
| --- | --- | --- | --- | --- |
| EC020 and A1200, each | 1,074 / 628 | 1,048 / 534 | 1,130 / 546 | 156,160 / 78,080 |
| 020, 030 and 040, each | 912 / 542 | 900 / 533 | 874 / 406 | 156,160 / 78,080 |
| 060 | unavailable here | unavailable here | unavailable here | 156,160 / 78,080 |

All **21 groups pass**, totaling **951,522 executed callbacks, 476,339 exception
frames and 14,562 callbacks with documented undefined SR bits masked**. Each
group must meet these exact counts, not merely execute one passing sample.
All 63 register/defined-X/frame corruption controls are detected. An isolated
CPU mutation saving the opcode PC for both families fails all 21 groups, at
31 callbacks and 21 frames; the original CPU source is restored byte-for-byte.
Separate copied-input preflight controls reject a missing data file, changed
fixture byte, empty profile selection and modified generator source, before
native execution. Evidence: `artifacts/m6-trap-bounds-command/`,
`artifacts/m6-trap-bounds-pc-mutation/` and `artifacts/m6-trap-bounds-preflight/`.

Identities for `artifacts/m6-trap-bounds-qualified-inputs/`:

- Manifest: `6d3fd2de241776b7fb22fa70225924932cc1c6c487e4931311916adab6b210ef`.
- Normalized patched source: `3ef386033792e585b093e55a44242b32d2cee16be7a597aa687bae7191ca449d`.
- Normalized patch: `ca94d93ec49447e853769bb93bd4e823fe313854aba59601161b35ef8f7bcca4`.
- Generator executable: `6b350167e05a2fc27a6320251488383384a86e9aa2e59e21f691853123cb0f9d`.
- Native bridge: `05ee1b8f5e6fbe67526cd5a53e7768e38e207091f4cc5ac11850700000bbac2d`.
- CPU assembly: `423405f59e9e0706a96fd2bd8f9d67b479032692e831a4f6e31480df6954b785`.
- Adapter assembly: `13a0baa1f50b368f3f0df818ca4a0b331496f512f29fa5f5acd7cf024ee4fe76`.

Generator seeds initialize xorshift state to 1 per test set, one round. This
focused selection changes the random stream's family order relative to Basic;
its passing counts cannot be substituted into the old Basic report. Full-format
extensions are enabled, with CCR 0/31 and user/supervisor rounds; bus/address
faults, incoming trace/M rounds, physical timing and exhaustive architectural
combination coverage are not claimed. These are explicitly patched software
expectations, not unchanged upstream or hardware measurements.

Reproduce with fresh output directories (supply the local pinned checkouts and
MSVC environment script; no generated binary/media is committed):

```powershell
./scripts/prepare-copper68k-winuae.ps1 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -Preset TrapBounds -OutputDirectory artifacts/trap-bounds-inputs
./scripts/test-copper68k-winuae-trap-bounds.ps1 -InputDirectory artifacts/trap-bounds-inputs -OutputDirectory artifacts/trap-bounds-audit
```

The audit command restores its environment and fails on missing, changed, empty
or mismatching inputs, skipped execution or a missing report. Fresh unchanged
Basic validation still reports **1,322 passing, 46 mismatching, 13 unsupported
and zero untested groups**, with 11,335,687 callbacks, 1,525,385 frame assertions,
199,327 masked-SR callbacks and all 32 controls passing. The Basic manifest and
library retain their previous identities. The existing qualified TRAP trace
audit still passes 512 callbacks, 256 incoming-T1 cases, 512 frames and six
controls. Evidence: `artifacts/m6-trap-bounds-broad/` and
`artifacts/m6-trap-bounds-trace/`. Milestone 6 remains in progress with all earlier
restoration, reference and consolidation requirements retained. No regression
is retired and no package is published in this follow-up.

Final Release CPU validation passes **4,776 tests with ten optional skips** and
zero failures, including the enabled new reference audit. The deterministic gate
still validates **11,168,408 logical cases in 469 reporting batches** with
`roadmapComplete=false`. Fresh pinned SingleStepTests passes 312,500 selected
cases in 125 files; Musashi passes 536 programs with 88 exclusions. Source pins,
input hashes and selections are checked again. Evidence:
`artifacts/m6-trap-bounds-full/` and `artifacts/m6-trap-bounds-gate.log`.
Consumer package/replay qualification remains the earlier .48 evidence; this
follow-up changes only test adapters, scripts and documentation.

## Breakpoint fallback qualification and shared audit runner (2026-10-05)

The original Basic audit fails BKPT on all seven applicable CPU profiles at the
first callback: `4848` at `0087FFA0` expects stacked PC `0087FFA2`, while the CPU
saves `0087FFA0`. Pinned `gencpu.cpp` synchronizes its two-byte PC offset before
calling `op_illg`. The documented illegal-exception PC is the causing instruction,
so this is a reference defect. Authority:
[MC68000UM 5.1.4 / 6.3.6](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf),
[MC68020UM 6.1.5 / 6.1.10](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf),
[MC68040UM 8.2.4 / 8.2.8](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
and [MC68060UM 8.2.4 / 8.2.8](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf).
010 continues illegal-instruction processing after an acknowledge and does not
accept replacement data. 020/EC020 support an external replacement instruction,
or illegal processing on BERR. 040/060 enter illegal processing after TA or TEA.
The current public bus API provides no breakpoint acknowledge/replacement device;
only the integer-state illegal-exception fallback is qualified here.

The new separate `Breakpoints` preset replaces the generator's PC synchronization
with `m68k_pc_offset = 0` in a copied source, leaving the opcode address available
to the exception constructor. Original reference sources and Basic inputs remain
unchanged. All eight BKPT encodings, both ordinary privilege states and CCR 0/31
run on 010, EC020, A1200, 020, 030, 040 and 060: **32 callbacks and 32 frame checks
per profile, 224 total**, with zero masked SR bits. Every register/X/frame
corruption control is detected (**21 controls**). 000 does not implement BKPT;
its defined illegal-word handling remains covered by the synthetic suite.
No incoming trace/M/fault rounds, physical acknowledge cycles or external
instruction substitution are qualified by this preset.

The test-internal `QualifiedExceptionPreset` runner shares exact source/patch/
executable/library checks, complete profile/family and fixture preflight,
comparator controls, fixed callback/frame gates and coverage reporting between
Breakpoints and TrapBounds. Each preset has its own optional native-library
environment override, so both can run in one ordinary CPU-suite invocation.
The existing common library variable still works. The dedicated TrapBounds CLI
is retained as a forwarding command; the generic command executes either preset
with the same fresh-output, environment-restoration and non-skipped-test gates:

```powershell
./scripts/prepare-copper68k-winuae.ps1 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -Preset Breakpoints -OutputDirectory artifacts/breakpoint-inputs
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Breakpoints -InputDirectory artifacts/breakpoint-inputs -OutputDirectory artifacts/breakpoint-audit
```

An isolated CPU mutation saves opcode PC + 2 on BKPT, narrowly affecting the
advanced system path and BKPT-only illegal entry in the 000/010 core. All seven
reference groups detect it on their first callback. The existing synthetic
`system-model` batch detects **512 mismatches per profile, 4,096 total**, with
zero unsupported or untested cases; its other cases remain passing. Both source
files are restored byte-for-byte. This proves the saved-PC checks in the existing
synthetic coverage as well as the new reference preset; no old test is retired.
Six copied-input controls per preset (**12 total**) reject missing/changed data,
empty or duplicate profiles, an empty family set and modified reference source
before native execution. Evidence: `artifacts/m6-breakpoint-pc-mutation/` and
`artifacts/m6-breakpoint-preflight/`.

The initial preparer attempt stopped without a manifest because its new preset
did not enter the source-patching branch. That failed directory is not an input
qualification. The corrected preparer uses a fresh v2 directory. Initial
callback-cardinality discovery rejected a provisional 128-case contract while
all native comparisons passed; the frozen required count is the actually
executed 32 per profile. Neither preparer failure nor discovery is reported as
an emulator mismatch or a passing audit.

Qualified `artifacts/m6-breakpoint-qualified-inputs-v2/` identities:

- Manifest: `a2503a7bd97335f92d19b8f52257fcf0029d92a46bcc994390e5fc530020519d`.
- Normalized source: `d83606b597e5bd38efc289e0ecbd1d41843e67ecedaac72e4b9335312d2ead21`.
- Normalized patch: `27b0fb21a7fadbe0bf92ae42074ceaf665d7c19a83efdc841bc7730befc87a7b`.
- Generator executable: `f6185f3231feac952bf321ffa5c0c61ec8a8600cf49f361ecc84290a51ed742c`.
- Native bridge: `f1b17372c36560a4557016c29bf9b438d9744dd7f9f67b533afcea5d42585f20`.

Generator seeds initialize xorshift state to 1 per test set, one focused round.
Regenerating TrapBounds with the shared preparer retains its normalized source
and patch hashes and all 21 passing groups / 951,522 callbacks / 476,339 frames.
Both old and new CLIs execute their complete selection. Evidence:
`artifacts/m6-breakpoint-command/` and
`artifacts/m6-breakpoint-regenerated-trap-bounds/`. Qualification remains patched
software agreement, not unchanged upstream or hardware measurement.

A stale BKPT exclusion was found in the optional **m68k-rs extra** adapter, not
the pinned Musashi suite. Its exact fixture is unavailable locally, so the
exclusion is retained with a corrected reason: standalone fallback is now
qualified, while that program's handler/frame assumptions still require audit.
No Musashi exclusion is removed, and absent optional inputs remain unavailable
coverage. Milestone 6 retains its earlier reference, advanced-restoration and
consolidation requirements. No production CPU correction, package publication,
physical timing claim or public API change is made in this follow-up.

Final Release validation passes **4,777 CPU tests with ten optional skips** and
zero failures, including both enabled exception presets. The deterministic gate
validates **11,168,408 logical cases in 469 reporting batches** with
`roadmapComplete=false`. Fresh pinned SingleStepTests passes 312,500 selected
cases in 125 files; Musashi remains 536 passing programs and 88 exclusions.
The qualified 040/060 TRAP trace audit passes 512 callbacks, 256 incoming-T1
cases, 512 frames and six controls. The unchanged Basic audit still fails:
1,322 passing, 46 mismatching, 13 unsupported and zero untested groups over
11,335,687 callbacks and 1,525,385 frames, with all 32 controls passing. It is
not overwritten or narrowed by the new preset.

Evidence: `artifacts/m6-breakpoint-full/`, `artifacts/m6-breakpoint-gate.log`,
`artifacts/m6-breakpoint-final-command/`, `artifacts/m6-breakpoint-broad/` and
`artifacts/m6-breakpoint-trace/`. At baseline `f249fe9`, the unchanged production
source builds CPU assembly SHA-256
`1a4b59a07ddb13f6cce318c901cca6d025c468b25a65db1c89aa7b52c31f8012`;
the final adapter assembly is
`ce24a4016c02922a6a99ecc856d54497b85ebfd51dc40fb9ed93bb6ac2d9c91d`.
Earlier .48 consumer-package qualification is retained; no new consumer package
or native replay is needed for these adapter/script/documentation changes.

## Unassigned CHK effective-address words (2026-10-05)

The unchanged Basic ILLEGAL audit exposed `413D` (CHK.L, unassigned mode 7 /
register 5) at `0087FFA0` on EC020/A1200/020/030/060. The advanced classifier
rejected An but omitted unassigned mode-7 fields. The subsequent general CHK
handler correctly declined them, and execution threw an emulator timing exception
instead of entering architectural vector 4. The existing 000/010/040 paths already
handled those words correctly.

[M68000PM 4-69/70](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
defines CHK's data EAs, with mode-7 fields only 0..4. These added words are
explicitly labeled **unassigned EA encodings**, rather than legal operand forms.
[MC68020UM 6.1.5](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
defines illegal first-word patterns as vector 4 with the causing instruction
address saved. The correction classifies only the CHK mask's An or mode-7
registers 5..7 before operand execution. Immediate and PC-relative sources and
the adjacent LEA encoding remain admitted. There is no generic illegal fallback,
operand retry, timing-policy change or public API change.

The existing `system-chk-invalid-operands` batch now contains **176 opcode words**:
128 An-source words plus 48 unassigned words, covering both sizes, all destination
registers, both stacks and every initial CCR. Fixed reference examples include
`413D`, `41BD`, `4F3F` and `4FBF`; legal neighbors and LEA are excluded explicitly.
Independent expectations check saved PC/SR, frame/stack state, all registers,
surrounding memory and forbidden operand reads. The gate requires **11,264 cases
per profile**, an addition of **24,576 cases across eight profiles**. No existing
regression is retired.

Failed-before evidence: `artifacts/m6-chk-unassigned-before/` reports 8,192 passing
and 3,072 emulator-unsupported cases on each of EC020/A1200/020/030/060; the other
three profiles pass all 11,264. There are no mismatches or untested cases. The
restored classifier passes all **90,112 cases** with zero other statuses in
`artifacts/m6-chk-unassigned-focused/`.

The unchanged broad audit now completes six additional callbacks and frames per
affected profile before encountering the next unassigned integer word, `4140`.
This separately requires decoding/encoding qualification; it is not treated as a
legal CHK byte form. The 040 `F300` illegal FPU operand/privilege-priority mismatch
also remains open. Overall Basic still fails **1,322 passing, 46 mismatching,
13 unsupported, zero untested groups**, over **11,335,717 callbacks and 1,525,415
frames**, with 199,327 masked-SR cases and all 32 controls detected. The original
Basic manifest and native library are unchanged. No family exclusion or comparison
mask is added. Evidence: `artifacts/m6-chk-unassigned-broad-qualified/`.

An initial invocation pointed to the wrong DLL filename; preflight rejected it
before callbacks. The corresponding first full-suite invocation was interrupted
after the setup error was identified. These are not passing validation evidence.
The corrected invocations use the manifest-qualified `m68k_cpu_tester.dll`.

Private, **unpublished** package `1.5.2-synthetic-dev.49` has SHA-256
`817d9d166f64135aa894c1af42fce6bbf75b6e5e2e7d099eaefca77be984d5a6`;
the packaged CPU assembly is
`2e37452c73dd5ae46dac1e8bd2fb9ed7f8c44e2493bea3c203120d3798aef583`.
`artifacts/m6-chk-unassigned-package.json` records changed-source and assembly
identities against `eaddcac`. The default-version CPU tested by the reference
adapter is `c01e30c9ba41db44cb37b329af7c3a42a2047890e3462403214f1cc37574bc5f`;
its version differs from the private package, with the same production source.

Isolated CopperScreen baseline `d9beae8` resolves the exact .49 dependency in
production and separate diagnostic assets, and all four loaded DLLs match the
packaged assembly. Release build has zero warnings/errors. Host **149** pass with
six optional skips, disk **74**, engine **1,080**, and three native Workbench/A1200
boot and disk-persistence replays pass with no skips. AHX passes **18** tests.
Consumer evidence: `artifacts/chk-unassigned-validation/`,
`artifacts/chk-unassigned-diagnostic-tests/` and
`artifacts/chk-unassigned-production.binlog`; CPU AHX results are in
`artifacts/m6-chk-unassigned-ahx-results/`. Native media is not committed. No
package publication, physical timing or host-throughput qualification is claimed.
Milestone 6 remains in progress with all earlier requirements retained.

Final validation for this follow-up passes **4,777 CPU tests**, with ten optional
skips and zero failures. The deterministic gate verifies **11,192,984 logical
cases in 469 batches**, retaining `roadmapComplete=false`. It rejects an omitted
020 CHK report and the previous passing report's 8,192-case cardinality. Fresh
pinned SingleStepTests passes 312,500 selected cases in 125 files; Musashi passes
536 programs with 88 explicit exclusions. Both qualified exception presets run
in the full suite: BKPT 224 callbacks / 224 frames / 21 controls, and trap/bounds
951,522 callbacks / 476,339 frames / 63 controls. The separately enabled qualified
TRAP trace audit passes 512 callbacks / 512 frames / six controls.

Evidence: `artifacts/m6-chk-unassigned-final-full/`,
`artifacts/m6-chk-unassigned-gate.log`, `artifacts/m6-chk-unassigned-gate-controls/`
and `artifacts/m6-chk-unassigned-trace/`. Ordinary CI uses the expanded existing
CHK matrix and updated strict cardinality. The full-suite skips remain unavailable
coverage; the independently run Basic audit remains failing. No seeded audit,
test consolidation, package publication or hardware qualification is added.

## Line-4 illegal and unassigned words (2026-10-05)

The unchanged Basic ILLEGAL audit next exposed `4140`, invalid LEA `41C0`,
invalid MOVEM `4888`, unassigned TST `4A3D`, and unassigned system word `4E00`.
On EC020/A1200/020/030/060 these reached an emulator unsupported-timing exception
instead of architectural vector 4. The 000/010/040 paths already handled them.
The advanced classifier now rejects these narrowly identified encodings before
operand effects, using the existing illegal-exception path and timing policy.
There is no generic fallback, partial-instruction retry or public API change.

[M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf) defines CHK
at 4-69/70 (bit 6 zero), LEA at 4-110 (bits 8..6 = 111), control EAs for
JMP/JSR/LEA/PEA at 4-108/109/110/159, MOVEM direction-specific EAs at 4-128..130,
and TST sizes/EAs at 4-192/193. Its instruction-format inventory leaves the
selected `4140` and `4E` words unassigned on these processors.
[MC68020UM 6.1.5](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf) specifies
vector 4 and the causing instruction PC for unassigned first words. Tests label
unassigned first words and EA fields explicitly; these are selected-model rules,
not claims about future architectures. Assigned invalid operand forms remain
distinct from legal instruction coverage.

The new and expanded mandatory matrices use independent vector-4 expectations,
both user/supervisor stacks and all 32 initial CCR images. They check saved PC/SR,
frame and bank selection, every register, surrounding memory and forbidden
operand reads. Fixed opcode examples and distinct-word counts audit encoding.

| Batch | Opcode words | Cases per profile | Cases across eight profiles |
| --- | ---: | ---: | ---: |
| `system-unassigned-4140` | 512 | 32,768 | 262,144 |
| `control-invalid-addresses` | 372 | 23,808 | 190,464 |
| `transfer-movem-invalid-operands` | 100 | 6,400 | 51,200 |
| Added unassigned TST words in `logical-unary-invalid-operands` | 9 | 576 additional | 4,608 additional |
| `system-unassigned-4e` | 70 | 4,480 | 35,840 |

The net addition is **544,256 cases and 32 reporting batches**. Legal aliases
remain covered by the existing transfer/control/system matrices: `49C0..49C7`
EXTB.L, PEA's SWAP/BKPT, MOVEM's EXT.W/L, and neighboring TRAP/LINK/UNLK/USP,
return and MOVEC words. TST size 3 remains TAS, including 060 HALT/PULSE rules.

The initial control-EA fixture incorrectly included EXTB.L aliases as illegal
LEA. That was a test expectation defect: each advanced profile reported 512
mismatches from correct EXTB.L execution. The corrected failed-before fixture
excludes those eight words, before the production control-EA guard is added.
It reports 23,808 unsupported cases on each affected profile. This is recorded
separately from the CPU defects, rather than used to alter correct EXTB behavior.

Failed-before directories retain the evidence for each correction:

- `artifacts/m6-unassigned-4140-before/`: 32,768 unsupported per affected profile.
- `artifacts/m6-invalid-control-qualified-before/`: 23,808 unsupported per affected
  profile; earlier erroneous fixture is in `m6-invalid-control-before/`.
- `artifacts/m6-invalid-movem-before/`: 3,328 passing and 3,072 unsupported per
  affected profile.
- `artifacts/m6-unassigned-tst-before/`: 576 additional unsupported per affected
  profile, with previous unary cases passing.
- `artifacts/m6-unassigned-4e-before/`: 4,480 unsupported per affected profile.

All three unaffected profiles pass these failed-before matrices. The corrected
final focused run passes **128 tests with zero skips/failures** in
`artifacts/m6-invalid-address-focused/`, including legal neighbors. No existing
regression is retired.

The unchanged Basic audit now reaches the next illegal integer word `5008`,
after 9,574 callbacks per affected advanced profile. The 040 `F300` exception
priority mismatch remains. Basic still fails **1,322 passing, 46 mismatching,
13 unsupported and zero untested groups**, over **11,350,097 callbacks and
1,539,795 frames**. All 32 controls pass; 199,327 masked-SR cases and the
manifest/native-library identities remain unchanged. No family exclusion or
comparison-mask change is added. Evidence: `artifacts/m6-invalid-address-broad/`.
The intermediate broad runs retain each earlier failing word and progression.

Private **unpublished** package `1.5.2-synthetic-dev.50` has SHA-256
`3ed5f13504404f751f8d7aa563965a49fd137fe4da940e077aba294ae597994e`;
the packaged CPU DLL is
`8743b5ba04f7bcb416b63cda9a63329017613235a2927755c95234dff852438d`.
`artifacts/m6-invalid-address-package.json` records source and assembly identities
against `0041aec`. The default-version tested CPU DLL is
`da4bbd49e4a4083ac8a6da2ef19eea3941bc2132d9e4137e5c13ceeb3c866aea`;
the version differs from the private package with identical production source.
Milestone 6 remains in progress; its earlier reference, advanced restoration
and consolidation requirements remain open. No new seeded audit, physical
timing/host-throughput qualification or package publication is claimed.

Final Release CPU validation passes **4,809 tests with ten optional skips** and
zero failures, with both qualified exception presets enabled. BKPT retains
224 callbacks / 224 frames / 21 controls; trap/bounds retains 951,522 callbacks /
476,339 frames / 63 controls. The separate qualified TRAP trace audit passes
512 callbacks / 512 frames / six controls. AHX passes 18 tests. Evidence:
`artifacts/m6-invalid-address-full/`, `artifacts/m6-invalid-address-trace/` and
`artifacts/m6-invalid-address-ahx-results/`. Optional skips are unavailable
coverage; the unchanged broad Basic audit remains failing.

Isolated CopperScreen baseline `d9beae8` builds in Release with zero warnings or
errors against the exact .50 dependency. Host tests pass **149** with six optional
skips, disk **74**, separate engine diagnostics **1,080**, and three native
Workbench/A1200 boot and disk-persistence replays pass with no skips. The package
ZIP's CPU DLL, all four consumer DLLs and four resolved dependency assets match
the recorded package/version. Consumer evidence is retained in
`artifacts/invalid-address-validation/`, `artifacts/invalid-address-diagnostic-tests/`,
`artifacts/invalid-address-production.binlog` and
`artifacts/invalid-address-identities.json`. Native media and build artifacts
are not committed; the root CopperScreen user's changes are preserved.

The strict deterministic gate verifies **11,737,240 logical cases in 501 batches**
and retains `roadmapComplete=false`. Fresh pinned SingleStepTests passes 312,500
selected 68000 cases in 125 files; Musashi passes 536 programs with 88 exclusions.
The positive copied-report gate passes, while omission of each of the four new
020 reports and substitution of the old 9,856-case unary report are rejected.
Controls use separate directories and preserve the original evidence. See
`artifacts/m6-invalid-address-gate.log` and
`artifacts/m6-invalid-address-gate-controls-v2/controls.json`. Ordinary CI requires
the expanded matrices and exact per-profile cardinalities. The complete milestone
6 goal remains open despite these passing scoped gates.

## Quick, binary and memory-shift illegal decoding (2026-10-05)

The unchanged Basic ILLEGAL run exposed `5008`, `7100`, `8008`, `C180` and
`E0C0` in sequence. These words reached unsupported-timing exceptions on
EC020/A1200/020/030/060. Narrow classifier guards now enter the existing
architectural vector-4 path before operand effects. The correction preserves
legal execution ordering, timing policy and public API; no generic fallback or
retry after partial effects is introduced.

Authority is [M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf):
ADDQ/SUBQ 4-11/12 and 4-181/182 permit only alterable destinations and forbid byte
An; Scc 4-173 and the separate DBcc/TRAPcc formats distinguish the unassigned
condition words. MOVEQ 4-134 fixes bit 8 to zero. ADD/SUB/CMP source rules admit
word/long An, unlike AND/OR; destination tables exclude PC/immediate. The binary
matrix uses their explicit instruction/opmode tables, including address variants
and word multiply/divide source limits. EXG 4-105 admits opmodes 01000, 01001 and
10001, excluding the unassigned `C180` words. Memory shifts/rotates at 4-24,
4-115, 4-162 and 4-166 require memory-alterable operands. Unassigned first words
use the causing opcode PC per
[MC68020UM 6.1.5](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf).

Six required batches cover every selected illegal/unassigned opcode word, both
stacks and every initial CCR. Fixed examples, exact distinct-word counts and
legal exclusions audit the independent encoding fixtures. The common verifier
checks saved PC/SR, frame/bank selection, all registers, surrounding memory and
forbidden operand reads. Unassigned words/EA fields are labeled explicitly;
they are not included as legal instruction combinations.

| Batch | Distinct words | Cases per profile | Added cases across eight profiles |
| --- | ---: | ---: | ---: |
| `arithmetic-quick-invalid-operands` | 416 | 26,624 | 212,992 |
| `control-scc-unassigned-operands` | 48 | 3,072 | 24,576 |
| `transfer-moveq-unassigned-words` | 2,048 | 131,072 | 1,048,576 |
| `integer-binary-invalid-operands` | 1,896 | 121,344 | 970,752 |
| `logical-unassigned-c180` | 64 | 4,096 | 32,768 |
| `logical-memory-shift-invalid-operands` | 176 | 11,264 | 90,112 |

Total addition: **2,379,776 cases in 48 batches**. Quick size 3, DBcc/TRAPcc,
legal MOVEQ, ADDX/SUBX, CMPM, SBCD/PACK/UNPK, ABCD/EXG, and register shifts /
bitfields retain their separate legal or architectural-unavailability coverage.
Word-multiply/divide An invalid-source coverage is retained in its prior matrix,
rather than duplicated in the new binary batch.

Failed-before runs have zero mismatches or untested cases. The 000/010/040
profiles already pass all new matrices. Each affected advanced profile reports:

- `artifacts/m6-invalid-quick-before/`: 26,624 unsupported quick cases and 3,072
  unsupported unassigned-condition cases.
- `artifacts/m6-unassigned-moveq-before/`: 131,072 unsupported cases.
- `artifacts/m6-invalid-binary-before/`: 17,408 passing / 103,936 unsupported.
- `artifacts/m6-unassigned-c180-before/`: 4,096 unsupported cases.
- `artifacts/m6-invalid-memory-shift-before/`: 11,264 unsupported cases.

Restored focused runs retain the corresponding `*-focused/` reports. Quick and
legal arithmetic/control tests pass 80 xUnit tests; MOVEQ and legal transfers
pass 40; binary and legal arithmetic/logical/multiply/divide/decimal/transfer
tests pass 120; C180 and legal register transfers pass 16. These are scoped
checks, separate from the final full CPU suite. Invalid memory operands and
legal shifts/bitfields pass 40 tests with no skips or failures.

The unchanged broad audit reaches line-F words on every advanced profile:
EC020/A1200/020 `F110` at callback 28,467; 030 `F520` at 28,551; 040 `F300` at
28,959; 060 `F23D` at 28,946. There is no remaining integer unsupported stop in
the ILLEGAL group, but these line-F exception disagreements are still failing
evidence requiring per-case source/manual/CPU qualification. The complete Basic
result is **1,322 passing / 51 mismatching / eight unsupported / zero untested
groups**, over **11,445,125 callbacks and 1,634,819 frames**. Five groups have
moved from unsupported execution to later architectural mismatches; they have
not become passing groups. All 32 controls pass. Masked-SR count 199,327 and
the original Basic manifest/native bridge remain unchanged. No family exclusion,
comparison mask or fixture alteration is added. See
`artifacts/m6-invalid-memory-shift-broad/`; intermediate failing audits are retained.

Consolidation review retains `MoveqSignExtendsImmediateAndSetsFlags`,
`AddqWordDataRegisterAddsImmediateUpdatesFlagsAndPreservesUpperWord`,
`OrByteDataToDataRegisterUpdatesLowByteAndFlags` and the three EXG register tests
in `M68020InterpreterTests`: they assert native and elapsed timing policy in
addition to semantic state. The shared semantic matrices do not replace that
timing evidence. No regression is retired in this follow-up. The earlier proven
ASL retirement and all specialized bus/prefetch/cache/JIT/native regressions
remain retained as documented. The stale initial gap summary above is corrected
to describe the now-implemented multi-model bridge and its remaining failures.

Milestone 6 remains in progress, including earlier reference disagreements,
advanced exception restoration and consolidation requirements. New seeded audits,
physical timing/host-throughput qualification and package publication are not
claimed by this checkpoint.

Private **unpublished** package `1.5.2-synthetic-dev.51` has SHA-256
`3967602830b825abe065684737788da47ea7109ba5e290c5838a97ad81853a45`;
the packaged CPU assembly is
`277818b4de5bb7638613b7cb26301ef6b14d691b711279cf91487ba20428bb27`.
`artifacts/m6-integer-illegal-package.json` records source and assembly identities
against `31a6221`; the default-version CPU used by the reference adapters is
`e73b8e1f89922399fad793a8f37025864b1bd4af3390dcf171d66de019572374`.
The assembly version differs from the private package; production source is
identical.

Isolated CopperScreen baseline `d9beae8` builds in Release with zero warnings or
errors against the exact .51 dependency. Host **149** pass with six optional
skips, disk **74**, separate engine diagnostics **1,080**, and all three native
Workbench/A1200 boot and disk-persistence replays pass without skips. All four
loaded CPU DLLs match the package ZIP entry, and all four dependency assets
resolve exactly .51. Evidence: `artifacts/integer-illegal-validation/`,
`artifacts/integer-illegal-diagnostic-tests/`,
`artifacts/integer-illegal-production.binlog` and
`artifacts/integer-illegal-identities.json`. No native media/build artifacts are
committed, and unrelated root CopperScreen changes remain untouched. AHX passes
18 tests in `artifacts/m6-integer-illegal-ahx-results/`; the qualified TRAP trace
audit retains 512 callbacks / 512 frames / six controls in
`artifacts/m6-integer-illegal-trace/`.

Final Release CPU validation passes **4,857 tests with ten optional skips** and
zero failures in `artifacts/m6-integer-illegal-full/`. Both qualified exception
presets execute: BKPT 224 callbacks / 224 frames / 21 controls, trap/bounds
951,522 callbacks / 476,339 frames / 63 controls. Optional skips remain
unavailable coverage, and the separately run Basic audit remains failing.

The strict gate verifies **14,117,016 logical cases in 549 batches**, retaining
`roadmapComplete=false`. Fresh pinned SingleStepTests passes 312,500 selected
68000 cases in 125 files; Musashi passes 536 programs with 88 exclusions.
The positive copied-report gate passes, and omitting each of the six new 020
reports is rejected. Original evidence is preserved in separate control folders.
See `artifacts/m6-integer-illegal-gate.log` and
`artifacts/m6-integer-illegal-gate-controls/controls.json`. Ordinary CI requires
every new per-profile report at its exact cardinality.

Remaining Basic ILLEGAL diagnostics record expected/actual vectors: `F110`
EC020/A1200/020 and `F520` 030 expect 8 versus actual 11; 040 `F300` expects 11
versus actual 8; 060 `F23D` expects 11 versus actual no exception. These require
instruction-format and exception-priority qualification, not a blanket line-F
exclusion. The eight remaining unsupported groups are the DIVL/MULL families
on EC020/A1200/020/030, first stopping at extension words `0031`, `0084` or
`5B2B`. Their reserved extension fields still need per-case qualification;
entire multiply/divide families are not excluded. Earlier restoration and
reference requirements remain open, so this checkpoint does not complete the
milestone 6 goal.

### Line-F state and operand qualification (2026-10-05)

The unchanged Basic ILLEGAL diagnostics exposed incorrect privilege ordering on
EC020/020/030, invalid FSAVE/FRESTORE operands on 040/060, and unassigned FPU
operand fields on 060. The corrections use architectural exceptions before
operand effects, retaining the existing exception timing policy, host gateway
`FF00` and public API. No instruction is retried after partial effects.

Authority is [MC68020UM 7.2.3.3/4 and 7.5.2.2/3](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
and [MC68030UM 10.1, 10.2.3.3/4 and 10.5.2.2/3](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf):
legal cpSAVE/cpRESTORE instructions check supervisor privilege before contacting
an absent coprocessor. Invalid first-word operands instead take vector 11.
The 030's CpID 0 denotes its internal MMU, so these words do not acquire the
external-coprocessor privilege rule. Existing supervisor absent-coprocessor
fallback behavior is retained; these tests do not qualify physical CIR/bus-fault
sequencing or implement an external coprocessor responder.

[M68000PM 6-13/16](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
and [MC68060UM D-15/18 and 8.2.4](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
define the state-transfer EAs and distinguish unrecognized first words from
unimplemented floating-point operations. Invalid 040/060 FSAVE/FRESTORE words
now take format-zero line-F before a privilege check. FPU command/conditional
mode-7 register fields 5..7 are unassigned and take line-F before command or
operand execution. Legal FDBcc/FTRAPcc neighbors retain their separate paths;
060 floating-point arithmetic and legal unimplemented FPU operations remain
outside this integer profile's qualification.

The shared state-frame legality predicate admits legal PC-relative FRESTORE
sources. The 040 now executes those sources with the extension-word PC base.
Their independent fixtures also detected reversed preindexed/postindexed pointer
ordering in the existing 040 FPU EA helper, corrected according to M68000PM
table 2-2. This correction applies to that shared helper; legal timing-bearing
FPU, integer, JIT and consumer regressions remain required.

| Required batch | Cases per profile | Scope |
| --- | ---: | --- |
| `system-linef-state-encodings` | 65,536 (000/010); 47,616 (EC020/A1200/020); 49,856 (030); 60,224 (040); 61,248 (060) | Save/restore first words, all CpID/EA encodings, both privilege states and all CCR values for required exceptions |
| `system-linef-unassigned-fpu-ea` | 1,536 | Six unassigned command/conditional words, four following extension patterns, both stacks and all CCR values |
| `system-linef-pc-restore` | 6,464 (040 only) | Displacement, brief index and all 66 legal full-format structures; NULL/IDLE/invalid frames and all CCR values |

The addition is **464,000 logical cases in 17 batches**. Legal supervisor
coprocessor/FPU state protocols are explicitly outside the first-word exception
batch, not counted as passing exceptions. 040/060 PFLUSH and 040 PTEST overlap
these bit patterns but are integer MMU instructions; their existing separate
matrices own them. The first fixture version failed to distinguish those
overlaps; `artifacts/m6-linef-before/` is retained as fixture debugging, not CPU
failed-before evidence. The qualified baseline is
`artifacts/m6-linef-qualified-before/`: each EC020/A1200/020 has 17,920 privilege
mismatches, 030 has 15,680, 040 has 1,856 invalid-state priority mismatches, and
060 has 3,712 invalid-state mismatches. The original three unassigned FPU command
words have 384 unsupported 040 cases and 768 060 emulator-error mismatches.

After command corrections, the additional conditional words are proved failing
in `artifacts/m6-linef-conditional-before/`: 040 has 576 unsupported cases and
060 has 768 emulator-error mismatches. Corrected PC fixtures preserve instruction
bytes when a null displacement and suppressed index makes the instruction's own
extension a header or pointer. Such direct self-reference can only test an
invalid frame, not simultaneously encode a NULL/IDLE header. The original PC
fixture debugging run is retained separately; qualified index-order failed-before
evidence has 3,456 mismatches in `artifacts/m6-linef-qualified-index-before/`.
Removing only the new PC address paths produces 6,464 unsupported cases in
`artifacts/m6-linef-pc-mutation/`; source is restored before final validation.

The earlier generic line-F fixture `F123` was a legal privileged cpSAVE word on
EC020/020. Its common unassigned-word example is corrected to `F1C0`; the new
matrix retains the actual `F123` exception outcomes. This is an expectation
correction, not regression retirement. Existing specialized FPU state/timing,
bus/prefetch/cache, JIT and native tests remain retained. No additional old test
is retired in this follow-up.

The unchanged final Basic audit now passes ILLEGAL on EC020, A1200 and 020
(35,828 callbacks each), and 030 (34,780). The 000/010 groups retain 34,880 each.
040 advances to `F400` at callback 29,331; 060 advances past `F27D` to `F380`
at 29,074. Overall it still fails: **1,326 passing / 47 mismatching / eight
unsupported / zero untested groups**, over **11,473,937 callbacks and 1,663,635
frames**. All 32 controls pass; masked-SR count 199,327, one terminal callback,
original manifest and native bridge remain unchanged. No exclusion, mask change
or fixture alteration makes the failing audit green. Evidence:
`artifacts/m6-linef-final-broad/`.

`F400` needs further per-case qualification: WinUAE expects vector 11 whereas
M68000PM 6-3/6-9 explicitly assigns scope 00 an illegal-instruction trap. The
current CPU raises vector 4; the native diagnostic reports a subsequent trap as
"no exception", so the adapter boundary also needs inspection. No CPU change
is made merely to match this software disagreement. `F380` is a further
unassigned 060 F-line category still caught by its broad unsupported floating
execution path. The eight DIVL/MULL unsupported groups and all earlier advanced
restoration/reference/consolidation requirements remain open. Milestone 6 stays
in progress; these passing scopes do not establish roadmap completion.

Final Release CPU validation passes **4,874 tests with ten optional skips** and
zero failures in `artifacts/m6-linef-full/`. Both qualified exception presets
execute: BKPT 224 callbacks / 224 frames / 21 controls, trap/bounds 951,522
callbacks / 476,339 frames / 63 controls. The strict gate checks **14,581,016
logical cases in 566 batches**, retains `roadmapComplete=false`, and freshly
passes pinned SingleStepTests (312,500 cases / 125 files) and Musashi (536
programs / 88 exclusions). Evidence: `artifacts/m6-linef-gate.log` and the full
report directory. Additional seeded audits are not claimed. The separate
qualified TRAP trace run passes 512 callbacks / 512 frames / six controls in
`artifacts/m6-linef-qualified-trace/`; an initial invocation omitted the native
library environment variable, failed preflight and is retained separately.
AHX passes 18 tests in `artifacts/m6-linef-ahx-results/`.

Private **unpublished** package `1.5.2-synthetic-dev.52` has SHA-256
`16eab48210a68daf11e118d5fa55aa52cf93296dd455d0686a31f2cdc33352a6`;
the packaged CPU assembly is
`5453e81863793e8155a8c1505aa867f10f3c7e872fb34172cbe80988a5d667b8`.
`artifacts/m6-linef-package.json` records source/assembly identities against
`281b91b`; the default-version CPU used by the reference adapters is
`a3f298936ca3ea57eefbb63b2c491105ee210c66138f96c7b66631b48fd10e79`.
Packing uses separate artifact outputs, so it cannot replace the assembly used
by the full CPU run. Final source and default CPU hashes remain identical after
the reference gate.

The positive copied-report gate passes. Omitting each of the three new report
types, retaining the old 768-case unassigned-FPU count, or retaining the
unqualified 6,528-case PC fixture count is rejected. Original evidence remains
intact in its source directory; controls use separate copies. Evidence:
`artifacts/m6-linef-gate-controls/controls.json`. Ordinary CI requires every
new per-profile report at its exact cardinality.

Isolated CopperScreen baseline `d9beae8` builds in Release with zero warnings or
errors against the exact .52 dependency. Host **149** pass with six optional
skips, disk **74**, separate engine diagnostics **1,080**, and all three native
Workbench/A1200 boot and disk-persistence replays pass without skips. All four
loaded CPU DLLs match the package ZIP entry, and all four dependency assets
resolve exactly .52. Evidence: `artifacts/linef-validation/`,
`artifacts/linef-diagnostic-tests/`, `artifacts/linef-production.binlog` and
`artifacts/linef-identities.json`. No media/build artifacts are committed,
unrelated root CopperScreen changes remain untouched, and package publication
is not authorized or performed.

### Unassigned FPU category follow-up (2026-10-05)

MC68020UM 7.5.2.2 identifies coprocessor instruction types 110/111 as
unassigned. MC68060UM 8.2.4 assigns unrecognized F-line words vector 11 with
a format-zero frame and the causing instruction address. The 060 unavailable
floating-point guard incorrectly intercepted these words. Its narrow correction
allows the existing architectural line-F path to handle them before operand
effects. Actual floating-point operations retain their existing handling; no
FPU arithmetic, enabled-MMU, physical timing or instruction retry is added.

The new required `system-linef-unassigned-fpu-types` batch covers every
`F380..F3FF` word (CpID 1), both stacks and all 32 CCR states: 8,192 cases per
profile, 65,536 across all eight profiles. Independent exception verification
checks saved PC/SR, stack selection, registers and memory. Fixed encoding
examples distinguish state-transfer words and CpID 2 PLPA aliases, which must
not be classified by a blanket category guard. Failed-before evidence in
`artifacts/m6-fpu-types-before/` has seven passing profiles and all 8,192 060
cases mismatching because of emulator exceptions. The corrected focused run
passes all 107 selected tests without skips in
`artifacts/m6-fpu-types-focused/`. No specialized regression is retired.

The unchanged Basic reference now reaches `F400` on both 040 and 060 at callback
29,331. This retains the previously documented illegal-instruction versus
line-F reference/manual disagreement and native adapter diagnostic caveat.
Overall Basic still fails: 1,326 passing, 47 mismatching, eight unsupported and
zero untested groups, with 11,474,194 callbacks. Input manifest, native bridge,
comparison masks and family selections remain unchanged. Evidence:
`artifacts/m6-fpu-types-broad/`. Reserved long multiply/divide extensions and
all earlier restoration/reference/consolidation requirements remain open.
Milestone 6 remains in progress; the passing bounded scope does not establish
roadmap completion.

Final Release CPU validation passes **4,882 tests with ten optional skips** and
zero failures in `artifacts/m6-fpu-types-full/`. Qualified BKPT and trap/bounds
retain their exact 224 / 951,522 callbacks and 224 / 476,339 frame assertions.
The strict gate verifies **14,646,552 logical cases in 574 batches**, with
`roadmapComplete=false`, fresh pinned SingleStepTests (312,500 cases in 125
files) and Musashi (536 programs, 88 exclusions). Separate qualified TRAP trace
passes 512 callbacks and 512 frames; AHX passes 18 tests. Evidence:
`artifacts/m6-fpu-types-gate.log`, `artifacts/m6-fpu-types-trace/` and
`artifacts/m6-fpu-types-ahx-results/`. The copied-report positive control passes;
omitting the new 060 report or reducing its count to 8,191 fails the gate.
Original reports remain intact. Evidence:
`artifacts/m6-fpu-types-gate-controls/controls.json`.

Private **unpublished** package `1.5.2-synthetic-dev.53` has SHA-256
`6157c9f4585cee92c15efc1173dfb33a15413eed8164d843c87439553f81b496`;
its CPU assembly is
`4118e59f278507443e8d763f0d75d800b6cc45d9b13082179528d13b08ddb7a0`.
`artifacts/m6-fpu-types-package.json` records source/package/assembly identities
against `903f45c`. The default-version CPU used by the reference adapters is
`e2cc6606d8b2ae05604f6413ec2fb45e3d30535cb5acd8a2c72590bc32e53a10`.
Packing uses separate outputs; final source and CPU hashes remain unchanged.

Isolated CopperScreen baseline `d9beae8` builds in Release with zero warnings or
errors against .53. Host 149 pass with six optional skips, disk 74, separate
engine diagnostics 1,080, and all three native Workbench/A1200 boot and
disk-persistence replays pass without skips. All four loaded CPU DLLs match the
package ZIP entry, and all four assets resolve exactly .53. Evidence:
`artifacts/fpu-types-validation/`, `artifacts/fpu-types-diagnostic-tests/`,
`artifacts/fpu-types-production.binlog` and `artifacts/fpu-types-identities.json`.
Unrelated CopperScreen changes remain untouched; no media/artifacts or package
publication is included in this source checkpoint.

### Long arithmetic reference qualification (2026-10-05)

The eight Basic DIVL.L/MULL.L unsupported groups on EC020/A1200/020/030
first fail on extensions such as `0031`, `0084` and `5B2B`. These contain
reserved fields, not legal long-arithmetic combinations. The pinned WinUAE
`cputest.cpp`, `handle_specials_extra`, already clears mask `83F8` on 040/060
but leaves it random on 020/030. [M68000PM 4-94/98/136/140](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
assigns those fields zero; 4-136/140 explicitly labels Dh == Dl with a 64-bit
multiply result undefined. Matching that software output would not establish
documented legal execution. The original inputs and their failing results remain
retained; no CPU guard, comparison mask or Basic family selection is relaxed.

The separate `LongArithmetic` preset patches copies of the pinned sources. Its
encoding-selection patch clears the reserved fields on all applicable models
and selects a distinct Dh for the undefined 64-bit multiply alias, before
computing the expected reference result. Divide-register aliases remain legal
and selected. Fixed examples validate both families' encodings; each of the
eight reserved fields, undefined multiply aliases, invalid EAs and wrong-family
words is independently rejected. All 18 new encoding checks pass in ordinary
CI. Every native callback is additionally checked without input normalization.
000/010 long arithmetic remains architecturally unavailable and covered by the
synthetic suite, not this advanced-model reference preset.

The first generated run passes ten groups but fails both 060 groups: vector 61
saves opcode PC + 4 in WinUAE, while [MC68060UM 8.2.4/C.2.2](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
requires the causing instruction PC. The source emits operand reads and PC
synchronization before `m68k_mull`/`m68k_divl` detect the unavailable operation.
Evidence: `artifacts/m6-long-arithmetic-first/`; ten passing, two mismatching,
zero unsupported groups. This exploratory run predates the final two-source
identity gate and is failed-before diagnostic evidence, not qualified success.

After early unimplemented detection, 060 MULL.L passes 3,226 callbacks / 2,018
frames; DIVL.L reaches callback 2,319. `4C5F 0002`, a 32-bit divide through
`(A7)+` by zero, retains its four-byte increment in Copper68k but the generator
undoes it. `needmmufixup` is commented as undoing unavailable-instruction effects;
the generated false-result branch also applies it to a divide-by-zero exception.
The second failed run is retained in
`artifacts/m6-long-arithmetic-qualified-first/`, with eleven passing groups.

The final CPU-generation patch separates these paths. Unavailable 060 64-bit
operations enter vector 61 before EA reads/effects, with the causing PC.
Ordinary divide-by-zero retains EA effects. The latter is an explicitly stated
interpretation of MC68060UM 8.3, which places divide-by-zero in group 3 after
instruction execution, together with PRM 2.2.4/5 increment/decrement semantics.
It is also consistent with the source's stated rollback purpose. Hardware
corroboration is not claimed. No production CPU, timing, arithmetic or flag-mask
change is required. Additional 060 candidates become valid reference exceptions
when the generator no longer attempts unavailable operands; they are counted,
not silently dropped.

Final pinned identities:

| Artifact | SHA-256 |
| --- | --- |
| Normalized `cputest-long-arithmetic.cpp` | `0012b9bd9cd2a172c4c321a8cf8e9f1095d32f824286518e61c9d4e75bc5eb43` |
| `long-arithmetic-encodings.patch` | `44e8dacde5a11e9cbd2e15119362a09b35c2e109fbbdbecbefa92929e89b32ea` |
| Normalized `gencpu-long-arithmetic.cpp` | `f1ec43c3658de9ae99e6e60f8376979f562c3b55d338a92e5d4f4849434b0799` |
| `long-arithmetic-unimplemented.patch` | `75a9e8d7ce2f534d2b16910dffe436e9d8396b3f470628bc59de9852898e0733` |
| Final input manifest | `f3340970bb5f014c672ddba27e26f93d434f74270c4efd1ec38ca5b1ab330247` |
| Final native DLL | `4ae1547993a1c358582bea4ed529bc7455977cb508d66a46a7e9580ca5a679cb` |
| Final generator executable | `78c8790ba4c9e168320f030d67724fec45c2756f5adcf416dae2cd8616fca47a` |

Generator/runner commits retain `025b999239800357e95065fe5b9a15ea5b300fa7`
and `7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Source copies, actual
source hashes, patches, compiler identity, all input hashes and executable/DLL
identities are in `artifacts/m6-long-arithmetic-final-inputs/manifest.json`.
Generation uses the pinned xorshift seed initialized to 1 and one Basic-style
CCR 0/31 round; full EA extensions are enabled. Other seeds, incoming trace,
fault rounds, physical MMU/cache/pipeline and timing remain unqualified.

Required per-profile callback/frame/masked-SR/form counts:

| Profile | DIVL.L | MULL.L |
| --- | --- | --- |
| EC020 and A1200, each | 2,422 / 132 / 686 / 1,054 | 2,456 / 0 / 0 / 1,077 |
| 020, 030 and 040, each | 2,040 / 214 / 584 / 881 | 2,046 / 0 / 0 / 894 |
| 060 | 3,178 / 2,068 / 94 / 1,422 | 3,226 / 2,018 / 0 / 1,411 |
| Total | 14,142 / 2,974 / 3,218 / 6,173 | 14,276 / 2,018 / 0 / 6,247 |

All twelve groups pass, with **28,418 callbacks, 4,992 frames and 12,420
architectural forms**. Each form identifies model/family, sign, 32/64-bit
selection, source EA fields and both destination register fields. All four
sign/width categories are mandatory; this sample does not imply exhaustive
external coverage of every addressing extension/value. Register and defined-X
corruption controls are required on all groups, frame corruption on seven
exception-bearing groups, and undefined-flag-only acceptance on all six DIVL
groups: **37 controls**. They pass in
`artifacts/m6-long-arithmetic-final-controls/`.

Eight copied-input preflight controls reject missing manifest/memory,
empty families/profiles, changed data, duplicate profiles and unqualified
source/patch identities before native execution. Evidence:
`artifacts/m6-long-arithmetic-preflight-controls/controls.json`. A production
saved-PC mutation independently fails both 060 reference groups and 8,972
boundary / 2,904 addressing synthetic cases. A 2,045-versus-2,046 cardinality
mutation fails all three 020/030/040 MULL groups, despite the native comparison
passing. Evidence: `artifacts/m6-long-arithmetic-mutation-controls/`. All source
mutations are restored before final validation. No specialized regression is
retired.

The reproducible preparation/audit commands and environment contract are in
[the adapter guide](../Copper68k.Tests/M68kWinUaeCpuTesterConformanceTests.md#qualified-long-multiplydivide-preset).
The new audit fails missing/empty/unqualified selections, unsupported legal
execution, mismatches and incomplete exact counts. The original Basic audit's
eight reserved-extension unsupported groups remain visible; the new separate
scope qualifies documented long-arithmetic inputs without making that broad
audit green. All earlier advanced restoration, other reference disagreements
and consolidation requirements remain open. Milestone 6 remains in progress.

Final Release validation passes **4,901 tests with ten optional skips**, zero
failures, in `artifacts/m6-long-arithmetic-full/`, including BKPT, trap/bounds
and LongArithmetic. Their exact counts and comparator controls pass. The strict
gate retains **14,646,552 logical cases / 574 batches**, `roadmapComplete=false`,
and freshly passes pinned SingleStepTests (312,500 cases / 125 files) and Musashi
(536 programs / 88 exclusions). The opening Musashi table is reconciled to those
current counts; earlier dated records remain unchanged. Separate qualified TRAP
trace passes 512 callbacks / 512 frames / six controls in
`artifacts/m6-long-arithmetic-trace/`. The gate log is
`artifacts/m6-long-arithmetic-gate.log`.

The unchanged broad Basic audit still fails at exactly 1,326 passing, 47
mismatching, eight unsupported and zero untested groups: 11,474,194 callbacks,
1,663,891 frames, 199,327 masked-SR cases, one terminal callback and all 32
controls. The original manifest/native identities remain unchanged. Evidence:
`artifacts/m6-long-arithmetic-broad/`. This preserves the 040/060 F400 disagreement
and the earlier advanced restoration/system/reference requirements.

`artifacts/m6-long-arithmetic-identities.json` records test/script/assembly hashes
against baseline `2787ebf`. The CPU source tree is unchanged:
`12753a6d2bb677f36cae03f3193f370828f50a66`. The default CPU assembly is
`73af4f3eb4ef2e218b89f88b55b8a56f2d759f4d567cdb1bbd9a99165a6c8fce`;
the adapter is
`281ac75e9761ea5d23899d8ffb2a41eab2f11c2c78f96e71e222316f5b8b804e`.
The CPU's informational version now includes `2787ebf`, so its binary hash
differs from the prior checkpoint's pre-commit build despite unchanged CPU
source. All final source/assembly identities remain stable after the gate.
Pinned tracked WinUAE/runner sources are clean. No production CPU, package or
consumer change is made in this follow-up; the prior isolated .53 consumer
evidence remains separate. No new package publication or host-performance
measurement is claimed. Unrelated root CopperScreen changes remain untouched.

## Word division reference qualification (2026-10-05)

This follow-up qualifies a remaining defined-flag disagreement without changing
production CPU behavior. In the original Basic report, `DIVU.W` on
EC020/A1200/020/030 first fails at `84C0`: D2=`FFFFFFFF`, D0=`00000010`,
initial CCR=`1F`. The unsigned quotient overflows. The reference leaves C=1;
Copper68k returns C=0. Evidence remains in
`artifacts/m6-long-arithmetic-broad/winuae-model-audit.json` (EC020/A1200 callback
1,480; 020/030 callback 1,344). The unsigned 000/010/040/060 groups and every
signed group pass their original Basic inputs.

[M68000PM 4-92/96](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
defines X preservation and cleared carry for signed/unsigned division. Overflow
sets V and leaves the destination unchanged; N/Z are undefined. Divide-by-zero
traps with undefined N/Z/V and cleared carry. The pinned generator's
`newcpu_common.cpp:setdivuflags` documents C=0 for 020, but its 020/030 branch
only sets V and conditionally N. It omits the carry clear. The separate
[`WordDivision` patch](../scripts/winuae/word-division-carry.patch) adds an
explicit C clear after that helper in a copied `gencpu.cpp`. Signed division,
normal unsigned results, divide-by-zero and other model behavior retain their
existing paths. N/Z values and timing are not changed. This is a manual-qualified
software reference correction, not hardware observation or unchanged upstream.

The preparation command retains pinned generator
`025b999239800357e95065fe5b9a15ea5b300fa7` and runner
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`, untouched tracked sources and all
original Basic inputs. The copied normalized source is
`254873bb8a1b59b081e98defa72fda572d5a2c93c33bb644c8ac07c4257e5c5e`;
the patch is
`7eb90f09aaad56d164d0eafb9f66ddfc31becde9951a832820d5fdcc7973a83f`.
Prepared evidence: `artifacts/m6-word-division-inputs/`. Its manifest is
`a9aa908a2367fe46189c98e3778f34f6504d7d0a3784705116ecc030cc40abde`,
generator executable
`b0983c76a403017ad4e4a4480c4c571ace760f0ee07af01478ac3f8f829ab615`,
and native bridge
`dee99883158721c1166cd510977973667165f7ffc8d5c9641621b6a60eb4fa8b`.
All `.dat` input hashes are retained and checked before native loading.

The complete profile/family selection, fixed callbacks, frames, masked-SR
counts and operand/profile forms are required. A reusable test-internal native
callback classifier observes immutable opcode/SR fields, rejects illegal data
sources, incorrect families and SR states outside this preset, and records
EA/register/input-SR combinations without production decoders or EA helpers.
Fourteen fixed encoding/profile examples join ordinary CI. These forms count
distinct first-word fields and initial SR; they do not imply exhaustive external
indexed structures or values. Basic generation uses CCR 0/31, user/supervisor
states and enabled full extensions, without incoming trace or bus faults.

| Profile | DIVS callbacks / frames / masked / forms | DIVU callbacks / frames / masked / forms |
| --- | ---: | ---: |
| 68000 | 5,738 / 1,590 / 3,530 / 736 | 6,166 / 1,838 / 4,158 / 736 |
| 68010 | 5,782 / 1,632 / 3,836 / 736 | 6,058 / 1,792 / 4,032 / 736 |
| EC020 and A1200, each | 9,690 / 2,734 / 6,074 / 784 | 9,670 / 2,670 / 7,132 / 752 |
| 020, 030, 040 and 060, each | 9,246 / 2,570 / 5,816 / 688 | 8,870 / 2,466 / 5,676 / 656 |

All sixteen groups pass: **134,928 callbacks, 37,804 frames, 87,936 masked-SR
cases and 11,392 architectural combinations**. Per-group register, defined-X,
defined-C and exception-frame corruption must fail; changes confined to
documented undefined flags must pass. All **80 controls** pass. The existing
independent flag masks remain unchanged, including defined C in overflow/trap
comparisons. No actual CPU result or fixture operand is modified to match.
The standalone command executes fifteen tests with zero skips, and writes
`winuae-word-division-audit.json` with distinct passing/mismatching/unsupported/
untested counts. Evidence: `artifacts/m6-word-division-final/`.

```powershell
./scripts/prepare-copper68k-winuae.ps1 `
  -GeneratorSource artifacts/reference-winuae-api `
  -RunnerSource artifacts/reference-copperline `
  -VcVars64 '<Visual Studio>/VC/Auxiliary/Build/vcvars64.bat' `
  -Preset WordDivision -OutputDirectory artifacts/winuae-word-division-inputs
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 `
  -Preset WordDivision -InputDirectory artifacts/winuae-word-division-inputs `
  -OutputDirectory artifacts/winuae-word-division-report
```

Eight copied-input controls reject missing manifest/memory, empty families,
changed data, empty/duplicate profiles, unqualified source and unqualified patch
before native execution. The original qualified inputs remain intact. Evidence:
`artifacts/m6-word-division-preflight-controls/controls.json`.

A temporary production mutation preserves incoming C only on unsigned word
overflow. The reference audit detects all six advanced DIVU groups; every other
group passes. The existing synthetic boundary matrix detects **448 mismatches
per advanced profile / 2,688 total**, while all sixteen addressing/boundary
reports contain zero unsupported or untested cases. Classic profiles and the
addressing groups pass; this mutation does not exercise overflow in their
chosen addressing samples. A separate expected-count mutation (000 DIVU
6,166 to 6,165) fails only that group despite native arithmetic passing.
Evidence: `artifacts/m6-word-division-mutation-controls-final/controls.json`.
Both source files are restored byte-for-byte before final validation. The first
control script incorrectly expected addressing mismatches too; its assertion
failed, its `finally` restored sources, and the corrected control run verifies
the measured boundary-only scope. Exploratory count/form discovery reports are
retained separately and never labeled passing gates.

This slice does not retire a regression, produce/publish a package, change the
CPU/public API/timing policy or broaden physical qualification. The prior private
.53 consumer evidence remains separate because production source is unchanged.
Milestone 6 remains in progress, including advanced restoration, other reference
disagreements, broader independent coverage and consolidation review.

Final Release CPU validation passes **4,916 tests / ten optional skips / zero
failures**, including WordDivision, LongArithmetic, TrapBounds and Breakpoints.
The strict gate passes **14,646,552 logical cases / 574 batches**, with
`roadmapComplete=false`; fresh pinned SingleStepTests passes 312,500 cases in
125 files, and Musashi passes 536 programs with 88 explicit exclusions. Evidence:
`artifacts/m6-word-division-full/`, `artifacts/m6-word-division-full.log` and
`artifacts/m6-word-division-gate.log`.

The fresh original Basic audit retains exactly **1,326 passing / 47 mismatching /
eight unsupported / zero untested** groups: 11,474,194 callbacks, 1,663,891
frames, 199,327 masked-SR cases and one terminal callback. All 32 controls pass.
Evidence: `artifacts/m6-word-division-broad/`. The unchanged original manifest
`37cd8ddae8b61ac60e3a49ba835362fbf80d418f48c2539338e6541c9d85e31f`
and native library
`75f0c11352d4a8c1e84f32a49a5347ae4cb5bd9128f1501a33d843e6f99cb057`
remain intact. These failures are not relabeled by the qualified preset.

`artifacts/m6-word-division-identities.json` records exact source/script/assembly
hashes against baseline `e8140401e9ea37e2a05583eb3cea9f12c9b4a0a5`.
The unchanged production source tree is
`12753a6d2bb677f36cae03f3193f370828f50a66`; its default assembly is
`b56545b9695b2b18f2c906e330152885fd52b28d6166f544166ea1910d836102`,
and the adapter assembly is
`2a858963843343689c0637de0843f1d61ccdd9e48f6862d102adbb15dfb7bc7e`.
The CPU informational version now embeds baseline `e814040`; the different
assembly hash does not imply a CPU source change. Validated source/assembly
identities remain stable after the strict gate. PowerShell parsing and local
documentation link checks pass. No additional seeded audit is claimed.

## 040 access-frame restoration discovery — 2026-10-05

This checkpoint adds an independent, deliberately failing restoration audit;
**no production CPU correction is committed**. The existing Basic WinUAE RTE
successes do not establish advanced-frame continuation correctness. The ordinary
synthetic RTE matrix skips the relevant legal formats, and its former comment
incorrectly suggested that retained specialist tests supplied the missing proof.
That comment now identifies the implementation/qualification gap.

Authority is [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf),
sections 8.4.6.2/7. A normal format-7 return consumes 60 bytes. CT creates the
trace frame at old SP+48 using the saved EA. CM replays MOVEM operands with the
saved address where required. Pending writebacks belong to the handler.
Simultaneous continuation bits are undefined and excluded. The trace vector
offset 0x24 follows the normal trace-frame interpretation; it is not a silicon
observation. Short-frame controls use sections 8.4.1/3/4.

The pinned [WinUAE MMU helper](https://raw.githubusercontent.com/tonioni/WinUAE/6ae6fb6b84bb9517e0245a80fc9bdca1a8580dde/cpummu.cpp)
was inspected, not executed as an oracle. Its CT staging and CM saved-EA state
are distinct from the pinned non-MMU generator's skip-only format-7 path
(`025b999239800357e95065fe5b9a15ea5b300fa7`, `gencpu/gencpu.cpp`). Neither path
certifies all continuation protocols. The primary PDF was inspected through the
web reader; direct NXP download was unavailable, so no local PDF hash is claimed.

Run the complete discovery command:

```powershell
./scripts/test-copper68k-040-access-frames.ps1 -OutputDirectory artifacts/040-access-frame-audit
```

The command rejects reused outputs, missing identities/reports, empty or partial
test selection, changed source/assembly inputs, foreign combinations, negative
counts and stale group/combination cardinalities. `-ValidateReportsOnly` verifies
existing reports against the current inputs; it does not turn historical failures
into success. Each request executes four xUnit batches and six fixed saved-SR
examples. Reports separate actual execution phases from dependent untested phases.

Canonical fixtures cover user, ISP and MSP returns, all 32 CCRs, separate T0/T1
and no-trace SR states, default/relocated VBR, two saved EAs and four SSW access
states. The sparse recording bus guards the complete frame, its neighbors and
pending writeback destinations, including absence of their operand transfers.
Normal return executes a following self-BRA; CT additionally returns through the
new trace frame before the following BRA. This checks restored trace retirement,
exact PC/SR/stack banks, registers, memory, saved exception diagnostics and entry
count. Expectations use independent constants and never production EA helpers.

| Group | Passing phases | Mismatching phases | Unsupported | Untested phases |
| --- | ---: | ---: | ---: | ---: |
| Existing formats 0/2/3 controls | 6,912 | 0 | 0 | 0 |
| Format-7 normal return | 0 | 4,608 | 0 | 4,608 |
| Format-7 pending trace | 0 | 4,608 | 0 | 9,216 |
| CM/CU/CP prerequisite inventory | 0 | 0 | 0 | 864 |
| Total | 6,912 | 9,216 | 0 | 14,688 |

These are **30,816 logical phases**, not 30,816 executed instructions: 864 are
explicit inventory gaps and 13,824 depend on failed RTE prerequisites. The
continuation inventory names seven CM addressing categories plus CU and CP for
each bank/CCR; it is not a completed size/opcode/full-index/alias matrix. Those
more detailed combinations still require fixtures and qualification.

Failed-before evidence is `artifacts/m6-rte-access-before/before.trx` and the
fresh command output recorded below. All attempted format-7 returns enter the
incorrect format-error path; their dependent phases are not retried. The current
CPU source tree remains `12753a6d2bb677f36cae03f3193f370828f50a66` at baseline
`7845c5ee06db83885c3f69cd1f0951c9e4e77090`.

Temporary, uncommitted probes qualify the downstream audit phases. A normal/CT
prototype passes all 29,952 executable phases while the complete gate still
fails on 864 continuation gaps. A skip-only prototype and a wrong traced-address
prototype each preserve the 6,912 short-frame controls and 9,216 normal phases
but fail all 4,608 pending-trace prerequisites. The latter distinguishes saved
EA from the RTE opcode address. These probes establish detection and fixture
reachability, not a shipped fix or independent hardware qualification. Source
is restored byte-for-byte and rebuilt after probing. No instruction fallback,
retry, changed timing policy or package publication is introduced.

The next implementation must qualify MOVEM saved-EA/replay and pending CU/CP
protocols, alongside normal/CT return. Detailed frame-validation faults, odd
return PCs, throwaway-to-access frames, real access-fault entry and other-model
advanced restoration remain open. FPU arithmetic, enabled MMU and physical
pipeline/cache/timing remain outside the roadmap. No specialized regression is
retired. Milestone 6 and the full goal remain **in progress**;
`roadmapComplete=false` remains unchanged.

Final unchanged-CPU evidence is `artifacts/m6-rte-access-restored/`: the command
exits 1 with the exact table above and writes a failing `audit-summary.json`.
`identities.json` records the actual tracked CPU-file hashes (separate from the
committed tree), fixture/command hashes and loaded build assemblies. The restored
RTE source SHA-256 is
`31ca43359abf7aff20a54e8507e5876a7c980fc73732556e2c320d70c7f3407f`.
Probe evidence is `artifacts/m6-rte-access-probes-final/controls.json`; all three
probe runs leave the complete gate failing. Earlier command-development failures
and the initial compile typo remain historical failures, not successful audits.

All thirteen specific report/input corruption controls fail for their intended
reason (`artifacts/m6-rte-access-preflight-controls/controls.json`): missing report,
missing identity, empty selection, stale cardinality, negative status, foreign
model/combination, duplicate fixture input, changed/empty CPU source identity,
changed fixture/command identity, changed assembly identity and missing status.
The restored Release focused selection passes 22 tests with four discovery skips
and zero failures (`artifacts/m6-rte-access-focused/focused.trx`). Those skips are
unavailable ordinary-CI restoration coverage; the explicit discovery command
executes them and fails as recorded above. Production CPU sources remain
unchanged, so no new NuGet consumer validation or release is claimed or required
for this audit-only checkpoint. Earlier full-suite, strict deterministic and
external-reference records are retained unchanged; they do not certify format 7.

## 040 normal, CT and MOVEM restoration checkpoint — 2026-10-05

This checkpoint implements normal, CT and CM format-7 RTE restoration in the
advanced 68040 interpreter. Normal return consumes the 60-byte frame. CT creates
its format-2 trace frame at old SP+48, using the saved EA as the instruction
address. CM preserves a one-shot PC/address pair and restarts MOVEM after EA
calculation: extension words are consumed without calculating an index or reading
an indirect pointer chain. Reset clears the pair; an interrupt handler at another
PC preserves it. Interpreter hot blocks and warmed V1/V2 JIT traces defer to the
interpreter until the continuation is consumed. Ordinary MOVEM calculation,
operand ordering and the existing timing policy remain unchanged.

Authority is MC68040UM sections 8.4.6.2 and 8.4.6.7, as cited in the discovery
record. Applying the saved EA to every legal MOVEM addressing mode interprets
8.4.6.7's restart-after-EA rule; 8.4.6.2 specifically discusses indexed and
PC-relative calculation. The pinned WinUAE MMU generator is documentary agreement,
not an executed oracle or silicon qualification. Physical restart timing remains
unqualified. CU/CP still need independently qualified pending-FPU state; they are
implementation gaps, not invalid encodings or exclusions caused by the FPU
arithmetic boundary. Multiple continuation bits remain undefined/excluded.

| Promoted batch | Passing logical phases | Architectural combinations |
| --- | ---: | ---: |
| Formats 0/2/3 controls | 6,912 | 108 |
| Format-7 normal return | 9,216 | 144 |
| Format-7 CT trace conversion/return | 13,824 | 144 |
| Every legal MOVEM opcode word, W/L, three masks | 120,960 | 1,260 |
| All 66 full-index structures, W/L, load/store/PC source | 114,048 | 1,188 |
| Total promoted | 264,960 | 2,844 |

The two MOVEM matrices contain 40,320 and 38,016 scenarios respectively; each
scenario checks RTE, MOVEM and a following NOP. Opcode enumeration covers all
140 legal MOVEM first words. All 32 CCRs and user/ISP/MSP banks are included.
Expectations use the shared independent addressing fixture, check registers,
memory guards, operand transfer widths/order, exact extension consumption and
absence of pointer-chain reads. Five additional ordinary xUnit scenarios prove
reset/interrupt/one-shot lifetime and warmed V1/V2 compiled entry before and after
the fallback. V2 uses its bus-batch boundary and explicit cycle target; the test
requires a compiled execution witness, not interpreter/JIT agreement alone.

Fresh discovery evidence is `artifacts/m6-rte-movem-discovery-qualified/`:
12 executed xUnit cases, 11 passing and one intentional CU/CP inventory failure.
The command exits 1 and records 265,152 logical phases: 264,960 passing, zero
mismatching/unsupported and 192 explicitly untested CU/CP requirements in six
combinations. `roadmapComplete=false`. The five promoted batches run in ordinary
CI; the remaining inventory executes only when explicitly requested. Detailed
validation faults, odd return PCs, throwaway-to-access frames, real access-fault
entry/writeback handling, other-model restoration and the broader reference gaps
remain required. No specialized regression is retired.

Repaired failed-before evidence is `artifacts/m6-rte-current-probes/before/`:
the original CPU fails all 40,320/38,016 RTE prerequisites and leaves
80,640/76,032 dependent phases untested. Removing the pending-continuation JIT
guard fails both warmed compiler tests. Restoring source byte-for-byte passes
all seven focused matrix/state tests. Evidence is
`artifacts/m6-rte-current-probes/controls.json`. Earlier fixture/command development
failures remain recorded and are not relabeled as successful qualification.
All thirteen specific malformed-input/report controls still detect their intended
defect (`artifacts/m6-rte-continuation-preflight-controls/controls.json`).

Full Release CPU validation passes 4,932 tests with eleven optional/opt-in skips
and zero failures, including all four qualified WinUAE presets
(`artifacts/m6-rte-continuation-full/full.trx`). The broad Basic discovery record
above remains unresolved; this checkpoint does not certify its disagreements.

Fresh private, unpublished NuGet `1.5.2-synthetic-dev.54` validates the production
fix through isolated CopperScreen baseline
`d9beae8b88be24032221e3482942a249c03c27d3`: Release solution build has zero
warnings/errors, host 149 passed/six optional skips, disk 74 passed, separate
engine diagnostics 1,080 passed, native Workbench boot two passed and A1200
AGA boot/persistence one passed without skips. Four restored assets select the
exact package and all four loaded CPU DLLs match its SHA-256
`57570f04cc1bd2cbff2103ca5dfc0912e9f1e74de86ef1ce02a14d1c95b3e861`.
Package SHA-256 is
`7f0ef9ce3c2a645b5b9202bb3f54bd1273a2e5724ea639ec83cee95fa0103884`.
Consumer identities/results are under the isolated worktree's
`artifacts/rte-continuation-identities.json` and
`artifacts/rte-continuation-validation/`. Published packages remain immutable;
no package publication or root CopperScreen dependency change is included.
Milestone 6 and the full reference/consolidation goal remain **in progress**.

The strict report gate verifies **14,911,576 passing logical cases in 579 reporting
batches**, with fresh pinned SingleStepTests (312,500 cases / 125 files) and
Musashi (536 programs / 88 explicit exclusions across all eight profiles).
Evidence is `artifacts/m6-rte-continuation-gate-final.log` and the full-run
`summary.json`; `roadmapComplete=false`. An initial verifier invocation incorrectly
requested zero-count 040-only reports for other models and failed; the corrected
model-scoped selection is the successful invocation recorded here.

## 040 CU/CP pending delivery checkpoint — 2026-10-05

RTE now converts CU frames to format 2/vector 11 and CP frames to format 3/the
original pending FPU vector, at old SP+48. Saved PC/SR/EA and the supervisor stack
bank are preserved; saved trace bits reach the new handler, which is responsible
for servicing the original trace. Authority is MC68040UM sections 8.3 and
8.4.6.2/7. The selected vector is integer exception-delivery state, distinct from
handler-visible FPCR/FPSR/FPIAR and FSAVE data. The interpreter and JIT retain it
until vector fetching succeeds. Nested completed deliveries preserve a suspended
outer event, including equal-valued entries. CPU reset clears delivery state;
FPU context/register reset does not. A redirected saved PC does not reselect or
lose the pending vector. Ordinary exception frame and timing policies remain.

The new CU matrix passes 13,824 logical phases/144 combinations. The CP matrix
passes 96,768 phases/1,008 combinations for vectors 49–55. Both use all 32 CCRs,
user/ISP/MSP banks, T0/T1/no trace, two VBRs/EAs and four lower SSW patterns.
Each scenario checks conversion, handler RTE and a following self-BRA; common
verification checks registers, exact PC/SR, guarded memory and writeback
non-replay. Fixed pending vectors are fixture inputs, not arithmetic expectations.
Deliberately inconsistent handler FPU control registers prove vector ownership.
BSUN is not a post-instruction FMOVE exception and is outside this CP selection.

Sixteen additional ordinary xUnit scenarios exercise actual FSIN unimplemented
and FMOVE unsupported/overflow exception delivery. A one-shot injected internal
fault at the vector fetch suspends delivery. Accurate, warmed V1 and warmed V2
entries preserve the event; compiled entry is mandatory in JIT scenarios. A
completed single-precision operand store is not fetched or written again during
CP return. Reset, nested delivery and redirected return PCs are covered. Foreign
CP frames without their original selected vector fail explicitly as unsupported,
without a fabricated architectural format error or operand retry. These tests
use the existing approximate internal fault entry and independently construct
the returning format-7 frame; they do not qualify real access-error frame entry,
FPU arithmetic, enabled MMU, silicon timing or foreign/context-transfer recovery.

The complete discovery selection is still failing:
`artifacts/m6-fpu-continuation-discovery-final/` executes fourteen xUnit cases,
with thirteen passing and one inventory failure. Its summary reports 376,128
logical phases: 375,552 passing, zero mismatching/unsupported and 576 explicitly
untested requirements. The inventory expands the previously documented gaps
into six named categories across three banks/all CCRs: frame-validation faults,
odd user trace PCs, throwaway-to-access return, real access-fault entry,
writeback-handler qualification and CP context-transferred vector recovery.
The latter remains necessary; preserving an original local pending event does
not solve migrating that event to another CPU/context. No requirement is removed
from the milestone or relabeled as invalid.

Failed-before CU/CP evidence is `artifacts/m6-fpu-continuation-before/`: 4,608
and 32,256 mismatching RTE prerequisites, with 9,216/64,512 dependent phases
untested. A constant-49 CP-vector mutation passes only that vector's scope and
fails exactly 27,648 prerequisites, leaving 55,296 dependent phases untested.
Premature delivery-state consumption fails all fifteen actual-delivery scenarios;
the explicit missing-state gap test still passes. Source is restored byte-for-byte
and all 73 focused FPU/matrix/lifetime/MOVEM tests pass. Evidence is
`artifacts/m6-fpu-continuation-probes/controls.json`. All thirteen specific
malformed input/report controls still detect their intended defects in
`artifacts/m6-fpu-continuation-preflight-controls/controls.json`. Earlier failed
V2 fixture attempts remain recorded; the qualified FSIN route proves a compiled
entry rather than accepting a fallback as V2 coverage.

Final Release CPU validation passes 4,950 tests, with eleven optional/opt-in
skips and zero failures; all four qualified WinUAE presets are enabled.
The strict report gate verifies 15,022,168 passing logical cases in 581 reporting
batches, with fresh pinned SingleStepTests (312,500 cases / 125 files) and
Musashi (536 programs / 88 explicit exclusions across eight profiles).
Evidence is `artifacts/m6-fpu-continuation-full/` and
`artifacts/m6-fpu-continuation-gate.log`; `roadmapComplete=false`.

Isolated unpublished NuGet `1.5.2-synthetic-dev.55` validates CopperScreen at
baseline `d9beae8b88be24032221e3482942a249c03c27d3`: Release build has zero
warnings/errors; host 149 passing/six optional skips, disk 74, separate engine
diagnostics 1,080, native Workbench boots two and A1200 AGA boot/persistence one,
without native skips. Four restored assets and four loaded CPU assemblies match
the package. Package SHA-256 is
`e804107087da31910386ca8d5ea46b622c71482bd46a4dc927751f2c07312f11`;
CPU assembly SHA-256 is
`3f09d72f14ae9dd9242845ecd745421a461b35dc1019825dc9edb763a46c69a8`.
Consumer evidence is its `artifacts/fpu-continuation-identities.json`,
`artifacts/fpu-continuation-validation/` and separate diagnostic outputs.
Published package versions and root CopperScreen dependencies are unchanged.

The fresh broad Basic audit in `artifacts/m6-fpu-continuation-broad-final/`
still fails: 1,326 passing directories, 47 mismatching and eight unsupported,
with 11,474,194 executed callbacks, 1,663,891 exception frames and 199,327
masked-SR cases. All 32 comparator controls detect their intended defects.
The pinned generator, runner, native library and input manifest are unchanged.
An initial invocation used the wrong native-library path and was rejected before
execution; its failure remains in `artifacts/m6-fpu-continuation-broad/`.
The corrected invocation verifies the manifest's exact library identity.

Other-model advanced restoration, the broad reference disagreements, independent
combination qualification and consolidation remain required. Existing specialized
regressions are retained. Milestone 6 and the goal remain **in progress** with
`roadmapComplete=false`; publication remains a separate release step.

## 040 chained throwaway qualification — 2026-10-05

MC68040UM section 8.4.2 permits a throwaway frame to select any of the three
stacks, including another throwaway before the final frame. The previous
ordinary-CI fixture covered only ISP-to-MSP followed by format 0. New matrices
qualify every one/two-throwaway bank path from an initially privileged ISP/MSP,
all three final restored banks, all 32 initial CCRs, T0/T1/no final trace and
even/odd data-stack addresses. Discarded PCs are odd and never fetched; discarded
SRs deliberately differ in trace state, without simultaneous T0/T1. There is
no privilege recheck after a throwaway selects USP. Independent pointer tracking
checks USP/ISP/MSP together, including repeated frames on the same stack.

| New ordinary-CI group | Passing phases | Combinations |
| --- | ---: | ---: |
| Chained short-frame controls (formats 0/2/3) | 82,944 | 1,296 |
| Chained format-7 normal/CM/CT/CU/CP (vectors 49–55) | 428,544 | 4,752 |
| Total new | 511,488 | 6,048 |

Each scenario checks RTE and the following BRA or CM MOVEM. Pending CT/CU/CP
scenarios also return through the converted handler frame. Exact PC/SR/registers,
every stack bank, guarded frame/neighbor memory, discarded-PC nonfetch,
handler-owned writeback non-replay and pending-vector consumption are checked.
CM executes MOVEM.L (A0),D0–D1 using the saved EA despite a changed A0. Restored
trace applies to the following instruction. These are synthetic return frames,
not captures or qualification of real fault entry, physical bus transfer order,
enabled MMU or timing. Production CPU source is unchanged from `9fa9f84`.

The maintained command
`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Consolidation`
now includes both chain termination and missing stack selection. Early
termination fails 193,536 RTE prerequisites, leaving 317,952 dependent phases
untested. Missing selection passes 85,248 phases on unchanged-stack paths and
fails 161,280 RTE prerequisites, leaving 264,960 dependent phases untested.
Neither mutation is accepted on compiler failure or a missing batch: both
complete new reporting selections must run and fail. Reports preserve exact
failing case IDs and status totals. All four consolidation mutations detect
their defects; `finally` restores source byte-for-byte and rebuilds it.
Evidence is `artifacts/m6-throwaway-mutations-final/mutation-proof.json`.
An earlier fixture used simultaneous T0/T1 in a discarded SR; that input was
removed and the proofs rerun rather than included as required defined coverage.

The complete discovery command still exits 1 with one inventory failure:
`artifacts/m6-throwaway-discovery-final/` reports 887,520 logical phases,
887,040 passing, zero mismatching/unsupported and 480 explicitly untested.
Sixteen xUnit cases execute: fifteen pass and the remaining inventory fails.
The promoted throwaway requirement is replaced by these executable matrices;
validation faults, odd final user trace PCs, real access-fault entry,
writeback-handler qualification and CP context transfer retain their named
requirements across all banks/CCRs. Exact report-combination selection and
fixture/source/assembly identities are verified. No regression is retired.
Milestone 6 and the full goal remain **in progress**, with all other-model and
broad independent qualification/consolidation requirements preserved.

The next odd-PC requirement has an executable exploratory reproduction at
`artifacts/m6-rte-odd-probe/`, with output
`artifacts/m6-rte-odd-probe-output-with-baseline.log`. Twenty-four public-factory
cases cover short formats 0/2/3 and normal format 7, ISP/MSP entry and user return
with no trace/T1/T0. RTE returns to odd PC `$6001` without changing the exception
sequence; the following execution takes vector 3 with a format-0 frame. In the
sixteen traced cases, the stacked SR still has S clear. MC68040UM sections 8.4,
8.2.2 and 8.4.6.7 instead require an address error during user traced restoration,
S set in the saved SR and a format-2 address-error frame. This is a reproduced
defect, not passing qualification. Exact saved PC, fault timing, stack bank/pop
ordering and pending-continuation priorities need independent qualification in
the fixing matrix; its named inventory requirement remains. Probe CPU assembly
SHA-256 is `c03904a1e9fb60ebe542450c50c2929b9e5865389041aad688ced528f9217ed0`.
Initial probe dependency/setup failures are retained separately and not counted
as execution evidence. The current checkpoint does not change CPU behavior.

Final Release CPU validation passes 4,952 tests, eleven optional/opt-in skips
and zero failures with all four qualified WinUAE presets enabled. The strict
report gate passes 15,533,656 logical cases in 583 reporting batches, including
fresh pinned SingleStepTests (312,500 cases / 125 files) and Musashi (536
programs / 88 explicit exclusions across eight profiles). Evidence is
`artifacts/m6-throwaway-full/` and `artifacts/m6-throwaway-gate.log`.
All thirteen malformed-input/report controls reject their intended defects in
`artifacts/m6-throwaway-preflight-controls/controls.json`. The latest CPU source
tree remains `e7de8cba19a6b182b8cb329db8f79194eed51f35`, unchanged from the
preceding private `.55` consumer-validated production checkpoint. Consumer
replays and package publication are not repeated for this test/reporting-only
change. All previously recorded native results and broad discovery disagreements
remain in force, with their original scope and identities. No package is published.

## 040 odd-PC return correction — 2026-10-05

The advanced 040 interpreter now takes an address error during a direct RTE
return to an odd instruction address. Short formats 0/2/3 and normal/CM format 7
pop their original frame and restore the selected stack before entering vector 3.
The address-error frame is format 2 (`$200c`), saves the RTE instruction PC and
stores the failed prefetch address with A0 cleared. Standalone odd instruction
fetch uses the same architectural frame, saving the current instruction PC.
Neither route reads the odd target. A rejected CM return installs no MOVEM
continuation and never retries completed operands. Existing timing keys are
preserved; this is not physical timing qualification.

The primary authority is [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
sections 8.2.2, 8.4, 8.4.3 and 8.4.6.7. The manual explicitly requires S set
in the stacked SR when restoring a traced user PC, and gives CT/CU/CP precedence
over an odd returned PC. Those pending exceptions therefore convert/deliver
first; their handler's later RTE takes the address error. The original saved EA
and pending vector remain intact until that delivery completes.

Additional pre-restoration SR-image ordering is documentary software evidence:
[WinUAE gencpu.cpp](https://github.com/tonioni/WinUAE/blob/5d22d33632646efc3f747f03e82d28353e52722e/gencpu.cpp)
passes `oldsr` to `exception3_read_prefetch_68040bug`, and
[newcpu.cpp](https://github.com/tonioni/WinUAE/blob/5d22d33632646efc3f747f03e82d28353e52722e/newcpu.cpp)
uses that SR for the stacked image while retaining restored live SR. The source
pin is `5d22d33632646efc3f747f03e82d28353e52722e`; local identities under
`artifacts/m6-rte-address-winuae/` are:

| Documentary file | SHA-256 |
| --- | --- |
| newcpu.cpp | `eb538884af9c14012b083db9b1e18106f1b351be7e3aca59767d915911beb6f5` |
| gencpu.cpp | `e993c18b19c68a98ec5404c2ee2983dba0f17252cff42a1ba932c92d8c13f094` |
| cpummu.cpp | `a833e9adcddbe68483bad603b1a8b8554393f21f1d319b2b138bb3fbabd94a08` |

These source files were inspected, not executed as a new reference oracle or
observed on hardware. They are distinct from the executed emoon tester/generator
pin `025b999…` already recorded above. Chained throwaways selecting a user tail
leave a saved-SR provenance question between that software path and the manual's
S requirement. The direct ISP/MSP matrices do not qualify it. Its original
inventory scope remains as `odd-PC-chained-SR-provenance` (three banks/all CCRs),
alongside frame-validation faults, real access-fault entry, writeback handlers
and CP context transfer. No requirement is removed or relabeled invalid.

| New ordinary-CI group | Passing phases | Combinations |
| --- | ---: | ---: |
| Direct short/normal/CM odd returns | 69,120 | 720 |
| CT/CU/CP odd-return priority and handler returns | 165,888 | 1,296 |
| Standalone odd instruction fetch | 2,304 | 72 |
| Total new | 237,312 | 2,088 |

The independent fixtures cover all CCRs, distinct restored CCRs, ISP/MSP entry,
all restored banks, T0/T1/no trace, two odd target addresses including a full
32-bit address, two VBRs and even/odd stack data addresses. They check exact
registers, every stack pointer, frame and neighbor memory, saved PC/SR, exception
sequence, pending delivery consumption and operand/writeback non-replay. A
handler explicitly repairs the frame to a new even PC, then returns and executes
the following BRA/trace; the consumed faulty RTE is never retried.

Before the production fix, `artifacts/m6-rte-odd-before/` records 66,816
mismatching prerequisites, 41,472 passing pending-delivery phases and 129,024
dependent phases untested. After the fix all new phases pass. Separate
`M68040OddReturnStateTests` execute 540 scenarios across accurate/V1/V2 dispatch;
both JIT engines must witness compiled warm dispatch and actual RTE fallback.
This does not claim a compiled RTE implementation. Together with retained
FPU/MOVEM continuation tests, the qualified focused selection passes 24 cases
in `artifacts/m6-rte-odd-state-qualified/state.trx`.

The maintained `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Rte040`
requires all three complete reports to execute for each mutation. Wrong frame
format and uncleared A0 each fail 66,816 phases, pass 41,472 pending prerequisites
and leave 129,024 downstream phases untested. Substituting restored SR for the
saved image fails 50,688, passes 85,248 and leaves 101,376 untested. Taking the
address error ahead of CT/CU/CP fails 41,472, passes 71,424 and leaves 124,416
untested. All four mutants fail semantically, not on compilation or missing
selection; exact IDs/counts are retained in
`artifacts/m6-rte-odd-mutations/mutation-proof.json`. Source is restored
byte-for-byte and rebuilt. No specialized regression is retired.

Milestone 6 and the goal remain **in progress** with `roadmapComplete=false`.
Other-model restoration, broader independent qualification, the recorded Basic
disagreements and consolidation remain required. Enabled MMU, FPU arithmetic,
physical timing and OS compatibility are outside this roadmap.

Isolated unpublished NuGet `1.5.2-synthetic-dev.56` validates CopperScreen at
baseline `d9beae8b88be24032221e3482942a249c03c27d3`: Release build has zero
warnings/errors; host 149 passing/six optional skips, disk 74, separate engine
diagnostics 1,080 and native Workbench boots two/A1200 AGA persistence one pass
without native skips. Four restored assets and four loaded CPU assemblies match
the package. Package SHA-256 is
`8377ea7330a5a4f22b944735e4ea611bdb43bfe0d1ba218da37f69994d5dc540`;
CPU assembly SHA-256 is
`1c0a788adf1443bf4cf34049115080083dcab7c2f568065335e5994944ffe26c`.
Consumer evidence is its `artifacts/rte-odd-identities.json`,
`artifacts/rte-odd-validation/` and separate diagnostic outputs. Published
package versions, the root CopperScreen dependencies and unrelated changes are
preserved. No package is published.

Final Release CPU validation passes 4,958 tests, eleven optional/opt-in skips
and zero failures with all four qualified WinUAE presets enabled. The strict
gate verifies 15,770,968 passing logical cases in 586 reporting batches, plus
fresh pinned SingleStepTests (312,500 cases / 125 files) and Musashi (536
programs / 88 explicit exclusions across eight profiles). Evidence is
`artifacts/m6-rte-odd-full/` and `artifacts/m6-rte-odd-gate.log`.

Complete 040 discovery remains failing in `artifacts/m6-rte-odd-discovery/`:
1,124,832 logical phases, 1,124,352 passing, zero mismatching/unsupported and
480 explicitly untested. Nineteen xUnit cases execute: eighteen pass and the
remaining inventory fails. Exact combination, cardinality, input/source and
assembly selections are checked; passing direct odd returns does not complete
the broader return/fault gate.

All thirteen malformed-input/report controls detect their specific defects in
`artifacts/m6-rte-odd-preflight-controls/controls.json`. The fresh broad Basic
audit still fails in `artifacts/m6-rte-odd-broad/`: 1,326 passing directories,
47 mismatching and eight unsupported, with 11,474,194 callbacks, 1,663,891
exception frames and 199,327 masked-SR cases. All 32 comparator controls detect
their defects. Generator/runner/native-library/manifest pins match the preceding
record; each model/opcode row retains its status and callback/frame/mask counts.
This checkpoint neither hides those disagreements nor treats discovery as a
passing qualification gate.

## LPSTOP encoding and exception-priority qualification — 2026-10-05

The 68060 executor recognized LPSTOP's first word but checked privilege before
its fixed second opcode word, and then classified malformed second words as
vector 4. It now recognizes the complete encoding before privilege handling;
an unrecognized second word takes line-F vector 11 without changing SR or
entering STOP. Legal LPSTOP's S-clear rejection, trace and stop timing policy
are retained. No instruction is retried and the public package API is unchanged.

[MC68060UM D-19/20](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
defines `F800 01C0 immediate` and explicitly makes an attempt to clear S a
privilege violation. Sections 8.2.4/5 distinguish unrecognized F-line encodings
from recognized privileged instructions, save the original SR, and save the
instruction's opcode PC. Section 8.2.6 specifies incoming trace and no stopped
state on a traced LPSTOP. Those manual rules determine the expectations.
The inspected pinned generators also validate `01C0` before privilege and use
vector 11 for an invalid word, but retain the previously recorded saved-PC
disagreement for legal user-mode LPSTOP. They do not override the manual.

| New ordinary-CI group | Passing cases | Reporting batches |
| --- | ---: | ---: |
| Every unrecognized 060 second opcode word, both privilege modes | 131,070 | 1 |
| Encoding/status/CCR/trace cases across eight profiles | 97,280 | 8 |
| Total new | 228,350 | 9 |

The exhaustive group enumerates all 65,535 non-`01C0` words independently of
production decoding, with canonical immediate/CCR inputs. The second group
uses the legal word, each of its sixteen individual bit changes, zero/all ones,
five SR boundary images, both privilege states, incoming T1/no trace and all
32 CCRs. It covers model-specific absence, correct vector/frame/PC/SR, all
registers and stacks, untouched memory and a sentinel which cannot retire while
stopped. A legal traced LPSTOP takes vector 9 with format 2 and next PC. These
groups qualify semantic outcomes, not physical prefetch ordering, broadcast bus
cycles, PST pins, clock quiescence or silicon timing.

Against production `a194c12`, `artifacts/m6-lpstop-before/` records 142,590
mismatches, 85,760 passing cases and no unsupported/untested new cases. Both
affected 060 batches fail and the seven other profiles pass. After the correction
all 228,350 pass; the affected system/model/trace selection passes 81 tests in
`artifacts/m6-lpstop-after/`.

`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope LowPowerStop`
requires both complete 060 batches to execute for each control. Vector-4
substitution fails 142,590 cases and passes 640. An early privilege check fails
71,295 and passes 71,935. Removing the existing S-clear check fails 128 and
passes 143,102. All fail semantically; no compiler/selection failure counts as
proof. Source is restored byte-for-byte and rebuilt. Evidence is
`artifacts/m6-lpstop-mutations-qualified/mutation-proof.json`; the initial
mixed-line-ending anchor failure is retained separately and is not execution
evidence. No old regression is retired.

The 040 chained user-tail SR question remains unresolved: the manual's traced
user S requirement and documentary WinUAE's prior user SR differ. That finding
does not turn the required inventory item into passing coverage. Ordinary 060
STOP's S-clear software disagreement also remains open: the generator labels
the behavior undocumented, while LPSTOP's S-clear rule is explicit. Existing
Basic saved-PC, reserved-word and other reference disagreements are retained.
Milestone 6 and the full goal remain **in progress** with `roadmapComplete=false`.

Private unpublished NuGet `1.5.2-synthetic-dev.57` validates isolated CopperScreen
at baseline `d9beae8b88be24032221e3482942a249c03c27d3`: Release build has zero
warnings/errors; host 149 passing/six optional skips, disk 74, separate engine
diagnostics 1,080, native Workbench boots two and A1200 persistence one pass with
zero native skips. Four restored assets and four loaded CPU assemblies match
the package. Package SHA-256 is
`d6d90ced1bf74824ab4e73a4918d0833689d11c17c55c5ea03c8f4597c60c059`;
CPU assembly SHA-256 is
`de37702d5301d7dfddd792b7c2d776434b048c5969537c32115d09c1a143a0a1`.
Evidence is the consumer's `artifacts/lpstop-identities.json`,
`artifacts/lpstop-validation/` and separate diagnostic outputs. Root CopperScreen
dependencies/unrelated changes and published package versions are preserved.
No package is published.

Final Release CPU validation passes 4,967 tests, eleven optional/opt-in skips and
zero failures with all four qualified WinUAE presets enabled. The strict gate
verifies 15,999,318 passing logical cases in 595 reporting batches, plus fresh
pinned SingleStepTests (312,500 cases / 125 files) and Musashi (536 programs /
88 explicit exclusions across eight profiles). Evidence is
`artifacts/m6-lpstop-full/` and `artifacts/m6-lpstop-gate.log`.

Five negative report controls reject their specific defect: missing 060 extension
enumeration, missing 000 absence profile, empty enumeration, stale value count
and foreign model. Evidence is `artifacts/m6-lpstop-report-controls/controls.json`.
The fresh 040 discovery in `artifacts/m6-lpstop-040-discovery/` still executes all
nineteen cases, passes eighteen and fails its remaining inventory: 1,124,352
passing phases, zero mismatching/unsupported and 480 explicitly untested. Its
input/source/assembly identities describe this production checkpoint. Earlier
thirteen 040 report-corruption proofs remain retained with their original source
identities; they are not relabeled as freshly rerun here.

Fresh Basic discovery still fails in `artifacts/m6-lpstop-broad/`: 1,326 passing
directories, 47 mismatching and eight unsupported, with 11,474,194 callbacks,
1,663,891 frames and 199,327 masked-SR cases. All 32 comparator controls detect
their defects. Every model/opcode row retains its preceding status and callback/
frame/mask counts; the generator, runner, native-library and input-manifest pins
are unchanged. In particular, the first legal user-mode LPSTOP case still exposes
the reference's opcode-PC-plus-two disagreement. Correcting malformed encodings
does not normalize that result or make the broad audit passing.

## LPSTOP independent exception qualification and consolidation, 2026-10-05

[MC68060UM](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf) D-19/20
defines the fixed `F800 01C0` encoding and S-clear privilege violation;
8.2.4/5 defines Line-F recognition and original-SR/opcode-PC exception frames.
Inspection of the executed generator pin explains its saved-PC disagreement:
`get_wordi_test(offset)` fetches at the given offset, then advances `regs.pc`
by two. LPSTOP calls it for fixed offsets two and four, so the first fetch
changes the saved exception PC and the second fetch address. The separate
`LowPowerStop` preset patches a copy of `gencpu.cpp` to use its existing
nonadvancing `get_word_test_prefetch` helper for both words. It does not change
CPU results, comparison masks, the original Basic corpus or tracked upstream
source. Physical fetch ordering/cycles are not qualified by this correction.

Generator `025b999239800357e95065fe5b9a15ea5b300fa7` and assertion-runner
`7a83745d6c6159bc74ab0471578ffc8bc244e66e` remain pinned. The normalized copied
generator SHA-256 is
`69ceec2d63bf35e27990142ca2e9e72f9c36dd2f4fba5113d17552c1ccbc35ca`;
`lpstop-fetch-pc.patch` is
`64cb8ff58d5a3a4fb4f217e3327fb1748d73f85521e3c9be98af7fd08f3a47bf`.
The fresh `artifacts/m6-lpstop-qualified-inputs-v2/manifest.json` SHA-256 is
`2cff30fc342e5c3953193eb2e85b08b89cab3d15bb87a992fc41a7a7ca30278e`;
it records every input file, configuration, compiler, executable and bridge
identity. Generator executable SHA-256 is
`2e57b5685af5f18dc2c7856d44c9c38e7bd2c0ebd89dd0b0babb9fcc26409b5b`;
native bridge SHA-256 is
`17e8b5c7ae0edc6fa4b79c4ee52d797b1d65a1e7423a80770197603cd2d22b3f`.

The first generated selection had 147,456 passing exception callbacks, but
malformed encodings did not request automatic supervisor rounds. It is retained
as discovery, not the final qualified scope. The maintained preset explicitly
requests `feature_sr_mask=0x2000` for both privilege states. The pinned generator
uses a recognized second word when immediate bits 6/7 are zero, otherwise a
seeded unrecognized word. It skips legal supervisor stopped outcomes. Thus this
corpus does not cover every immediate/extension pair or stopped/trace behavior.
The final exception corpus passes **245,760 callbacks and frames**, zero masked
SR cases, mismatches, unsupported or untested selected directories. Its input
classifier reads immutable opcode/extension/immediate/SR bytes before CPU
execution, requires the canonical first word and CCR 0/31 with no incoming
trace, and rejects stopped or foreign profile inputs. Exact form distributions
are checked, not just their total:

| Encoding / immediate S | Incoming SR combinations | Cases per combination | Total |
| --- | --- | ---: | ---: |
| Recognized / S=0 | `0000`, `001F`, `2000`, `201F` | 8,192 | 32,768 |
| Recognized / S=1 | `0000`, `001F` | 8,192 | 16,384 |
| Unrecognized / S=0 | `0000`, `001F`, `2000`, `201F` | 24,576 | 98,304 |
| Unrecognized / S=1 | `0000`, `001F`, `2000`, `201F` | 24,576 | 98,304 |
| Total | 14 combinations | | 245,760 |

Register, defined-SR and frame-byte corruptions each fail on an executed case.
The maintained command requires the opt-in audit plus ten encoding/distribution
tests to execute and pass, with a nonempty coverage report:

```powershell
./scripts/prepare-copper68k-winuae.ps1 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -Preset LowPowerStop -OutputDirectory <fresh-inputs>
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset LowPowerStop -InputDirectory <fresh-inputs> -OutputDirectory <fresh-output>
```

Evidence is `artifacts/m6-lpstop-reference-final/` and the full-suite
`winuae-lpstop-audit.json`. Preliminary reports intentionally fail while exact
corpus counts are being discovered; they are not relabeled as qualified gates.
Seven isolated input/source controls fail for their specific defects: empty
profile/family/input selections, missing/changed data, changed generator and
changed patch. See `artifacts/m6-lpstop-reference-controls-v2/controls.json`.
The first control attempt retains partial evidence: removing a memory image
returns a different diagnostic from the helper's expected missing-data selection
message. It is not counted as the complete seven-control proof.

### LPSTOP duplicate retirement proof

The former LPSTOP loop in
`SyntheticModelSystemTests.Move16CacheInstructionsBreakpointsAndLowPowerStop`
tested the legal extension, immediates `0000/0700/2000/2700/271F`, all 32
initial CCRs and both privilege states: 320 cases per profile, 2,560 total.
The replacement
`SyntheticLowPowerStopTests.EncodingPrivilegeStatusAndIncomingTraceHaveIndependentOutcomes`
now includes those exact five values plus `071F/A01F`, with the legal extension,
eighteen malformed encoding controls, both privilege states, incoming T1/no
trace and all CCRs. Its former-compatible cases use the same public factory,
stack/neighbor fixtures and full architectural checks. For stopped outcomes it
also verifies a second execution cannot retire the sentinel or add an exception.
The exhaustive malformed-word group remains separate.

Before removing the old loop, the maintained S-clear mutation ran both tests:
the old batch detects 64 mismatches and the replacement detects 192. The
first replacement witness is
`68060/LPSTOP/encoding=01C0/imm=0000/super=True/T=0000/op=F800/ccr=00`.
The exhaustive group still passes under this mutation, demonstrating a distinct
status-rule defect rather than encoding failure. Wrong-vector and early-
privilege mutations detect 147,198 and 73,599 mismatches respectively. Source
restores byte-for-byte and rebuilds. Proof is
`artifacts/m6-lpstop-consolidation-proof/mutation-proof.json`.

Only the duplicate LPSTOP loop is retired. Its remaining test is renamed
`Move16CacheInstructionsAndBreakpoints`, and its MOVE16 mutation selection is
updated. No cache/prefetch/bus/fault/JIT/native test is retired. Ordinary-CI
LPSTOP coverage is now 267,262 cases in nine batches: 131,070 exhaustive cases
and 17,024 boundary/status cases per profile. The residual `system-model`
batch has 12,800 cases per profile. Historical counts above describe their
original checkpoints and are not rewritten.

Final validation in `artifacts/m6-lpstop-consolidated-full/` passes **4,978 CPU
tests**, eleven optional skips and zero failures, with all five qualified WinUAE
presets enabled. The strict gate in `artifacts/m6-lpstop-consolidated-gate.log`
verifies **16,035,670 passing logical cases / 595 reporting batches**; fresh
SingleStepTests and Musashi audits pass 312,500 cases / 125 files and 536
programs / 88 explicit exclusions. CPU production source remains `cb9679d`;
this checkpoint changes test/reference infrastructure only. Its full-suite CPU
assembly SHA-256 is
`4e718d58cd6c24df668b498943e5e38a770c14ec04cfbf3bbd756cbe06d14f9e`;
adapter SHA-256 is
`290709cd4f3a447c106015f3ecc4881e1d8647888490e1b89d68709945d037d7`.
Six fresh report controls reject missing enumeration/absence profiles, empty
enumeration, stale boundary counts, foreign model and the old duplicate loop's
13,120-case count. See
`artifacts/m6-lpstop-consolidated-report-controls/controls.json`.
Consumer/package validation remains the preceding unpublished `.57` evidence
with its original binary identities; it is not claimed as a new consumer replay.
No package is published or dependency boundary changed.

The original Basic LPSTOP failure remains preserved. This qualified exception
preset resolves its documented reference-fetch issue for a separate selection;
it does not make Basic passing or close ordinary STOP's S-clear disagreement.
Advanced restoration/fault, chained SR provenance, broader reference and
consolidation gaps remain required. Milestone 6 and the active goal remain
**in progress**, with `roadmapComplete=false`.

## MOVES legal-input reference qualification, 2026-10-05

The retained Basic 68010 `MOVES.L` failure at callback 783 has opcode `0E9D`,
extension `DA5E`, SR `2000`: it stores A5 through `(A5)+`. The native reference
expects the original value and Copper68k stores the incremented value.
[M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf) 6-26 marks
same-An postincrement/predecrement stored values undefined; 6-24/25 fixes the
extension's lower eleven fields at zero. This input violates both qualifications.
The synthetic MOVES matrix already excludes undefined stores and uses canonical
extension words. No production change is warranted by this disagreement.

The separate `Moves` preset qualifies legal sampled inputs for 010, EC020,
020, 030, 040 and 060, with A1200 executing the EC020 fixture through its own
public factory. The generator does not supply MOVES on 000; its documented
unavailable outcome remains synthetic coverage. A copied `cputest.cpp` masks
reserved fields and rejects undefined same-An store candidates before reference
execution. Random consumption remains intact. The generator's direction check
now reads the fixed instruction extension at `opcode_memory_start+2`, rather
than the final EA extension at `pc-2`. Neither CPU results nor comparator masks
are normalized. Original Basic inputs, sources and mismatching rows remain.

Pins remain generator `025b999239800357e95065fe5b9a15ea5b300fa7` and runner
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Normalized copied source SHA-256 is
`8a7f2930a6f28a48170834481fcb449eee07a92dd7d6df96f8b6675d082692a7`;
`moves-encodings.patch` is
`e79367079bbbbe4fda3b326cf7c6a5cf15e08f021cc3137b1fc3b3aeee756368`.
The fresh `artifacts/m6-moves-qualified-inputs-v2/manifest.json` SHA-256 is
`8b4d4c819af6d7b011384253cb20b06e9be42502a7981bc7e4e8939ed8f61125`;
each of its six profiles has three families and twelve pinned input files.
Generator executable SHA-256 is
`987f3d22f6529541ccbbe9508a32a03bd2f161d6af2b83a8c0799ac15a99d0fc`;
native bridge SHA-256 is
`efec6cced943fcbea7312e380b102d2c483d0eb56ac2eb1aceee47481aabb5a0`.

The input classifier checks immutable words before stack copying or CPU
execution. It rejects wrong family/size, non-memory-alterable EAs, reserved
extension bits, undefined stores, foreign S/CCR profiles and reserved full-
format structural fields. Ordinary 010 indexing is labeled unscaled brief,
including ignored format bits. Advanced full-index forms report base/index
suppression, base displacement length and pre/post/no memory indirection.
This records actual sampled combinations rather than inferring them from a test
count. Exact callback/frame/form counts and complete model/family selections
are required:

| Profile(s), each | Family | Callbacks | Privilege frames | Recorded forms |
| --- | --- | ---: | ---: | ---: |
| 010 | B | 5,410 | 3,844 | 2,760 |
| 010 | W | 4,748 | 3,770 | 2,460 |
| 010 | L | 4,810 | 3,778 | 2,468 |
| EC020 / A1200 | B | 5,368 | 3,936 | 3,032 |
| EC020 / A1200 | W | 5,346 | 3,980 | 3,022 |
| EC020 / A1200 | L | 5,476 | 3,892 | 3,030 |
| 020 / 030 / 040 / 060 | B | 5,238 | 4,268 | 2,846 |
| 020 / 030 / 040 / 060 | W | 5,214 | 4,300 | 2,752 |
| 020 / 030 / 040 / 060 | L | 5,334 | 4,198 | 2,918 |
| All seven profiles | B/W/L | 110,492 | 86,072 | 59,920 |

All 21 directories pass, with zero mismatching, unsupported or untested selected
directories and no masked SR cases. Each directory's independent register,
defined-SR and frame-byte corruptions fail, for 63 comparator controls. The
command also requires sixteen fixed encoding/profile tests to pass. Evidence
is `artifacts/m6-moves-reference-qualified/`:

```powershell
./scripts/prepare-copper68k-winuae.ps1 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -Preset Moves -OutputDirectory <fresh-inputs>
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Moves -InputDirectory <fresh-inputs> -OutputDirectory <fresh-output>
```

The initial patch affected only the generator's exact-target EA path. The
callback classifier rejected its still-reserved random inputs before execution:
all 21 selected directories are explicitly untested, with zero callbacks. This
failed discovery is retained in `artifacts/m6-moves-reference-discovery/`; it
is not counted as CPU failure or passing coverage. The corrected selection's
first count-discovery report intentionally fails while exact count expectations
are established; subsequent qualified evidence uses the fixed expectations.
Seven isolated input/source controls reject empty profile/family/input
selections, missing/changed data, changed generator and changed patch. Their
specific failures and identities are recorded in
`artifacts/m6-moves-reference-controls/controls.json`.

Qualification is limited to the one-round flat-address-space corpus, incoming
CCR 0/31 and user/supervisor mode. It does not establish physical SFC/DFC spaces,
cache coherency, bus ordering, trace/fault restart, timing or exhaustive
indexed-value combinations. The existing 040 privilege-before-extension bus
regression is retained; no regression is retired here. Required broader
restoration and reference gaps remain, and milestone 6 stays in progress.

The full Release CPU suite in `artifacts/m6-moves-full/` passes 4,995 tests,
eleven optional skips and zero failures, with all six qualified WinUAE presets
enabled. Every preset reports zero mismatching, unsupported or untested selected
directories with the same CPU/adapter identities. CPU assembly SHA-256 is
`bf8abb3a4603d55e19ad38ef64293569b2f6cea8a9ec251ccfe08e70f42f6a35`;
adapter SHA-256 is
`5d7d21a851072f49812bdc27164024e2b50ac060a1add15d96424b4751a80bf3`.
The strict gate in `artifacts/m6-moves-gate.log` validates 16,035,670 passing
logical cases in 595 reporting batches, with `roadmapComplete=false`. Fresh
SingleStepTests passes 312,500 cases in 125 files; Musashi passes 536 programs,
with 88 explicit exclusions. Their new evidence retains the pinned inputs and
does not broaden the documented software-reference qualification.
Production CPU source remains unchanged from `cb9679d`; prior unpublished `.57`
consumer evidence retains its original package/assembly identities, without a
new consumer replay or package publication claim. Unrelated CopperScreen working
changes and its NuGet boundary remain preserved.

### CAS legal-input and unimplemented-frame reference qualification — 2026-10-05

The retained Basic audit stops at callback 273 for both 060 CAS.W and CAS.L.
Its first misaligned predecrement operand is `-(A3)` with initial A3 `00007FFF`:
opcodes `0CE3` / `0EE3`, extensions `0043` / `00C1`, initial SR `0000`,
instruction PC `0087FFA0`. The CPU takes vector 61 and saves `0087FFA0`;
the reference expects `0087FFA4`. [MC68060UM C.2.2 and figure
8-3](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf) require a format-0
frame pointing to the unimplemented instruction. Section 7.7.6 identifies
misaligned CAS as an unimplemented-integer case. Copper68k already has the
documented PC; no production change is made. The pinned generator's CAS path
calls `sync_m68k_pc_noreset()` before its exception helper, which stacks that
advanced PC. `cas-unimplemented-pc.patch` changes only this path in a copied
generator to restore `regs.instruction_pc` before vector 61. The original Basic
corpus, generator and failed evidence remain unchanged.

The separate `Cas` preset selects B/W/L on EC020/020/030/040/060, with A1200
reusing EC020 inputs. [M68000PM 4-66/67](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
defines memory-alterable operands, fixed-zero reserved extension fields and
compare/update registers. `cas-encodings.patch` clears all fields except Du/Dc
in the copied input generator before reference execution. The bridge rejects
noncanonical inputs; it never normalizes an input, CPU result or expected frame.
The immutable-word classifier records size, EA mode/register, Dc/Du, initial
S/CCR and brief/full indexed structure. Reserved full-format fields, unsupported
profiles, foreign families and incoming SR values outside `0000`, `001F`,
`2000`, `201F` are rejected.

The exact passing selections are:

| Profile(s), each | Family | Callbacks | Exception frames | Recorded forms |
| --- | --- | ---: | ---: | ---: |
| EC020 / A1200 | B | 3,358 | 0 | 2,620 |
| EC020 / A1200 | W | 2,924 | 0 | 2,340 |
| EC020 / A1200 | L | 3,476 | 0 | 2,736 |
| 020 / 030 / 040 / 060 | B | 2,348 | 0 | 1,826 |
| 020 / 030 / 040 | W | 2,442 | 0 | 1,904 |
| 020 / 030 / 040 | L | 2,652 | 0 | 2,034 |
| 060 | W | 2,442 | 894 | 1,904 |
| 060 | L | 2,652 | 1,572 | 2,034 |
| All six profiles | B/W/L | 49,284 | 2,466 | 38,448 |

All eighteen directories pass with zero mismatching, unsupported or untested
selected directories and zero masked SR cases. Fifteen fixed encoding/profile
tests also pass. Independent register and defined-SR corruptions fail for all
directories. Frame-format/vector-word and saved-PC corruptions fail in the two
060 W/L directories, giving forty applicable comparator controls. Adding four
to the saved PC recreates the original discrepancy; both controls fail after
517 callbacks with frame byte 5 expected `A0`, actual `A4`. Zero-frame directories
require exactly zero frames and explicitly label their frame controls inapplicable;
they are not evidence of exception-frame coverage. The first count-discovery
report intentionally fails against placeholder zero counts and remains in
`artifacts/m6-cas-reference-discovery/`. Subsequent passing evidence with fixed
counts and saved-PC controls is `artifacts/m6-cas-reference-qualified-v2/`.

Preparation and audit commands:

```powershell
./scripts/prepare-copper68k-winuae.ps1 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -Preset Cas -OutputDirectory <fresh-inputs>
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Cas -InputDirectory <fresh-inputs> -OutputDirectory <fresh-output>
```

The generator remains pinned to `025b999239800357e95065fe5b9a15ea5b300fa7`
and runner to `7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Required normalized
source/patch SHA-256 identities are:

| Input | SHA-256 |
| --- | --- |
| Copied CPU generator | `40e65f21b503437993d1a704c7552b8b6f8ab17493c109583c13e00c93d77b10` |
| CPU-generator patch | `a27200bdbac2441b1b63651f02590d2894eadc3257df54097fd8a50a5c0d8646` |
| Copied input generator | `6b24d70455aa39ed6894ad2a2253d60bf4b8b487be4e2dada194a0589eb53b5b` |
| Input-generator patch | `cb45000b18f7d4a11dcb0fec7130202c7918ac7fd42c9940effa248b1991fba2` |

The manifest in `artifacts/m6-cas-qualified-inputs/` has SHA-256
`a1cf7a6d6af743c2f01a51a92d4270a149a692aaee3235f2b241a379a9d3bb76`;
generator executable SHA-256 is
`610d0815532612eb6485cd2cab86ab655e5f2180501dae2d9c4a999668790cc8`;
native library SHA-256 is
`0aff0778debe3b56ca5cee818f32f835afa46015eb1efc83641f39711272b7fd`.
Complete profiles/families, data and memory images, input hashes, both copied
sources and both patches are validated before execution. Nine isolated controls
reject empty profiles/families/input selections, missing or changed input data,
changed CPU generator/patch and changed input generator/patch. Each fails for
its intended reason in `artifacts/m6-cas-reference-controls/controls.json`.

This is one seeded round with CCR 0/31 and user/supervisor states, with full
addressing extensions enabled. It is a corrected software reference, not an
unchanged upstream or silicon oracle. The reference reads its operand before
checking 060 alignment; this audit qualifies architectural state and frames,
not bus accesses or fault sequencing. Physical locks/bus ordering, cache,
incoming trace, operand faults/restart, timing and exhaustive signed/scaled
indexed-value combinations remain unqualified. CAS2 (including the separate
040 alias-order disagreement) is not promoted by this preset. Unavailable
000/010 CAS remains synthetic coverage. No regression is retired, no production
CPU change or package release is made, and all required RTE/restart/reference/
consolidation gaps remain. Milestone 6 stays in progress.

The next CAS2 reference follow-up has a distinct manual rule. M68000PM 4-68
states that if Dc1 and Dc2 name the same register and comparison fails, memory
operand 1 is stored there. The retained 040 CAS2.W witness is `0CFC 8083 E043`
(both compare fields D3), with different operand addresses A0/A6. Copper68k
returns operand 1 (`FFFF0001`); the reference expects operand 2 (`FFFFC700`).
The pinned generator explicitly reverses the compare-register write order on
040. This identifies a separate reference correction to qualify, not a CPU fix
or completed CAS2 audit. CAS2 inputs, applicable frame controls and independent
coverage still need their own validation; none are silently included above.

The full Release CPU suite in `artifacts/m6-cas-full/` passes 5,011 tests,
eleven optional skips and zero failures, with all seven qualified WinUAE presets
enabled. All seven reports have zero mismatching, unsupported or untested
selected directories and matching CPU/adapter identities. CPU assembly SHA-256
is `dd9fab1d6aebb4c35ad48e6c8443fc19dd6d52dc0ede9f5d7a49f11013d188c7`;
adapter SHA-256 is
`1a427ea255554770f5891b82be5e29a159ff6984b722c7b0be8e6e29f325fa17`.
Production source remains unchanged since `cb9679d`; this test/reference-only
checkpoint retains the preceding unpublished `.57` consumer evidence with its
original package/assembly identities. It does not claim a fresh consumer replay,
binary equality with `.57`, or package publication. Unrelated CopperScreen
changes and its pinned NuGet boundary remain preserved.

The strict gate in `artifacts/m6-cas-gate.log` validates 16,035,670 passing
logical cases in 595 reporting batches, with `roadmapComplete=false` and the
complete required-gap inventory retained. Fresh pinned SingleStepTests passes
312,500 cases in 125 files; Musashi passes 536 programs with 88 explicit
exclusions. Their input identities and complete selections are rechecked; this
does not broaden the references' documented qualification or close other gaps.

### CAS2 compare-alias and unimplemented-frame reference qualification — 2026-10-05

The preceding CAS investigation identified the unchanged Basic 040 CAS2.W
witness `0CFC 8083 E043`: both compare fields name D3, with different operand
addresses A0/A6. Copper68k returns operand 1 (`FFFF0001`); the reference expects
operand 2 (`FFFFC700`). [M68000PM 4-68](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
explicitly assigns operand 1 to a shared compare register on comparison failure.
The pinned generator reverses the register-update order on 040. A copied CPU
generator now writes operand 2 first, then operand 1, for both W/L, preserving
the final operand-1 value when Dc1 == Dc2. Nonaliased results and CPU production
behavior remain unchanged. The original Basic inputs, source and disagreement
remain retained.

The first `Cas2` discovery inputs also contain overlapping memory operands.
The immutable input classifier rejects them before CPU execution, including
`FFFFFFFF` and zero long transfers whose physical bytes overlap after wrapping.
M68000PM 4-68 marks overlapping memory-update results undefined. A separate
copied input generator now excludes overlapping operand ranges before reference
execution, independently of the comparison outcome. It checks every physical
byte using the profile's external address width and selects A7's actual initial
USP/ISP according to S. It writes the generator's normal skip marker, undoes
fixture write history and resets frame compression state. Existing upstream
canonicalization already clears reserved CAS2 extension fields; the bridge
verifies them rather than changing inputs. Each of EC020/020/030/040 excludes
372 generated CCR/S candidates, recorded in its generation log. On 060 every
CAS2 is an unimplemented-integer exception, so overlap candidates are retained:
there is no ambiguous memory update. [MC68060UM C.2.2](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
requires vector 61 and an instruction-PC format-0 frame.

The classifier uses immutable instruction words and initial registers, with
explicit stack selection rather than production decoder/EA helpers. It records
W/L, both general-register address selectors, both compare/update registers,
shared-compare aliases, alignment residues, overlap and incoming S/CCR. Fixed
tests distinguish 24/32-bit physical aliasing and transfers wrapping at the
address boundary. Required selected counts are:

| Profile(s), each | Family | Callbacks | Exception frames | Recorded forms |
| --- | --- | ---: | ---: | ---: |
| EC020 / A1200 | W | 174 | 0 | 174 |
| EC020 / A1200 | L | 154 | 0 | 154 |
| 020 / 030 / 040 | W | 42 | 0 | 42 |
| 020 / 030 / 040 | L | 34 | 0 | 34 |
| 060 | W | 1,148 | 1,148 | 1,096 |
| 060 | L | 1,148 | 1,148 | 1,092 |
| All six profiles | W/L | 3,180 | 2,296 | 3,072 |

All twelve selected directories pass with zero mismatching, unsupported or
untested directories and no masked SR cases. Eleven fixed encoding/profile/
overlap tests also pass. Every directory detects register and defined-SR
corruptions; the two 060 directories also detect frame-word and saved-PC
corruptions. The two 040 directories detect a separate alias-result corruption
that substitutes the independently captured original operand 2 on a failed
shared-register comparison. The W control fails at callback 29 on the original
`0CFC 8083 E043` witness, expected D3 `FFFF0001`, actual `FFFFC700`. The L control
fails on its first callback (`0EFC E047 1147`), expected D7 `C700FD02`, actual
`0001004F`. These thirty applicable controls prove the comparisons detect the
old reference defect; inapplicable frame controls are labeled, not counted as
coverage. Compare-alias forms occur in every selected family/profile; these
counts do not establish exhaustive combinations or success/failure distributions.

The initial overlapping-input failures remain in
`artifacts/m6-cas2-reference-discovery/`; the subsequent count-discovery run
intentionally fails against placeholder counts in
`artifacts/m6-cas2-reference-counts/`. Neither is passing qualified evidence.
The final exact-count run in `artifacts/m6-cas2-reference-qualified/` passes
twelve xUnit tests (eleven fixed cases and the generated audit). Nine isolated
controls reject empty profile/family/input selections, missing/changed data,
changed CPU-generator source/patch and changed input-generator source/patch;
`artifacts/m6-cas2-reference-controls/controls.json` records specific failures.

```powershell
./scripts/prepare-copper68k-winuae.ps1 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -Preset Cas2 -OutputDirectory <fresh-inputs>
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Cas2 -InputDirectory <fresh-inputs> -OutputDirectory <fresh-output>
```

The generator pin remains `025b999239800357e95065fe5b9a15ea5b300fa7`, runner
pin `7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Required source/patch SHA-256:

| Input | SHA-256 |
| --- | --- |
| Copied CPU generator | `beeb1f112867f1f11aa18b172c4607fe572db3f7500c8ef509a657423d640917` |
| CPU-generator patch | `134b61047dfafb91f38374b5151b940ada98dee32bfec251b6a6855043092888` |
| Copied input generator | `4edf1108dd8aab27ea8f61177c8e2166fca5b54e374f759ed388e3c804d5b93e` |
| Input-generator patch | `dbf0c78f5b88bf2f2ddc0d656a52bca190ec6b970b9e3c6b82cc48d5eac5fc0d` |

The complete fixture manifest has SHA-256
`3d819a0373d698496e1db0d440745128eddf227ba066a11d70d35f7e124fccf4`;
generator executable SHA-256
`9bc0646013661b2e2a1882a321ee1d177fe34eb78298abcc19ef36a8eda9202f`;
native library SHA-256
`32178feb2c58e1c483fa23507400353efa7a2c06c7bc76cb6227b41818654003`.
All profiles/families, data and memory images, input hashes, both copied sources
and both patches are required before execution. Inputs are in
`artifacts/m6-cas2-qualified-inputs/`, with one seeded round, CCR 0/31 and both
privilege states. The small implemented-model selections are reported as actual
software-reference samples, not comprehensive architectural qualification.

Physical lock/bus order, cache, trace, operand-fault restart, physical timing
and exhaustive register/value/alias combinations remain unqualified. Unavailable
000/010 outcomes remain synthetic. No production fix, regression retirement,
consumer replay or package publication is added here. This promotes only the
documented sampled reference scope; the full required restoration/reference/
consolidation roadmap remains in progress.

Full Release CPU validation in `artifacts/m6-cas2-full/` passes 5,023 tests,
eleven optional skips and zero failures, with all eight qualified WinUAE presets
enabled. Every preset passes its exact selection/count controls with matching
CPU/adapter identities. CPU assembly SHA-256 is
`df73a9759389296aaa6300ec682d27fe67d37f66033bda8d78836044edc60132`;
adapter SHA-256 is
`c1d3cca379061aff66c065b2fb5b61e24eb3ae8e88e4e834ab698e07ec4df0f5`.
Production CPU source remains unchanged since `cb9679d`; preceding private
unpublished `.57` consumer evidence retains its original package/binary
identities, without a new consumer replay or cross-checkpoint binary-equality
claim. CopperScreen's unrelated changes and pinned NuGet boundary are preserved.

The strict report gate passes 16,035,670 deterministic logical cases in 595
xUnit batches. Pinned SingleStepTests passes 312,500 cases across 125 files;
Musashi passes 536 programs with 88 explicit exclusions. Input identities and
complete selections are rechecked. `roadmapComplete=false` remains explicit;
these passing scoped gates do not close the remaining milestone-6 requirements.

### Cache encoding reference qualification and consolidation — 2026-10-05

The retained Basic 040/060 ILLEGAL failures stop at callback 29,331 with `F400`,
initial SR `0000` and opcode PC `0087FFA0`. Copper68k takes vector 4; the pinned
generator expects line-F vector 11. [M68000PM 6-4/9](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
assigns illegal-instruction traps to cache scope `00` and makes cache `00` a
no-operation for other scopes. [MC68060UM D-12](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
repeats the scope-zero rule. The production CPU already follows these rules;
no production correction or timing change is needed.

The old native diagnostic's “got no exception” is not an absent CPU exception.
The adapter trace records `exc=4`, and the pinned native tester special-cases
vector 4 because its completion sentinel also uses ILLEGAL. At vendor
`m68k_cpu_tester.c` lines 1289–1307 it describes a vector-4 mismatch at the
matching PC as no exception. No result/frame/mask normalization is introduced
to make that wording disappear. The new preset compares vector 4 and validates
its real frame; frame and saved-PC corruption are independently rejected.
Original Basic inputs, manifest, bridge and failures remain unchanged.

`SyntheticCacheEncodingTests.EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache`
enumerates all 256 F4xx words, all 32 CCR states and both privilege states on
every profile. The independent expectation checks all registers, stack banks,
defined SR, exact PC, exception frames, terminal state and surrounding memory;
successful instructions also execute a following sentinel. Scope-zero on
040/060 takes vector 4 before privilege; valid scopes in user mode take vector
8, including cache-zero forms. Older profiles take vector 11. All **131,072**
cases pass in eight `system-cache-encodings` batches. These are architectural
semantic cases; empty fixture caches do not establish physical invalidation,
dirty-line writeback, enabled-MMU translation, bus faults or pipeline timing.

The separate `CacheEncodings` reference preset copies pinned `cputest.cpp`.
Its two-hunk patch corrects `op_illg_1` only for `F4xx` scope zero on 040/060,
and selects only those words in the ILLEGAL family before reference execution.
The CPU generator and native comparator remain unchanged. Upstream's
`isunsupported` excludes actual CINV/CPUSH operations, so they are explicitly
not promoted as independent reference coverage. Initial exploratory generation
requested those families and produced only ILLEGAL; it is retained in
`artifacts/m6-cache-encodings-inputs/`. The final configuration explicitly
requests only ILLEGAL in `artifacts/m6-cache-encodings-qualified-inputs/`.

Every selected profile, including A1200's EC020 fixture reuse, executes all 64
scope-zero words with CCR 0/31 and both privilege states: **256 callbacks,
frames and distinct forms per profile; 2,048 total**. Expectations require the
complete fixed opcode/status distribution with multiplicity one, not just a
snapshot distinct count. All eight directories pass with zero mismatching,
unsupported, untested or masked-SR cases. Twelve fixed examples protect model
distinctions, legal neighboring scopes and rejected foreign/status inputs.
The focused command passes thirteen xUnit tests. All **32** applicable
register/SR/frame/saved-PC comparator corruptions are detected. Evidence:
`artifacts/m6-cache-encodings-reference/`.

Seven isolated negative controls reject empty profiles, families or input
selections; missing or changed fixtures; and changed copied source or patch.
Each executes exactly one failing audit for its intended reason. The original
inputs remain intact. Evidence:
`artifacts/m6-cache-encodings-reference-controls/controls.json`. An initial
control-helper invocation had a trailing-comma parser error before execution;
it is not counted as reference evidence.

Pinned generator remains `025b999239800357e95065fe5b9a15ea5b300fa7`; native
runner remains `7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Exact final identities:

| Input | SHA-256 |
| --- | --- |
| Copied input/reference source | `5f6df41a9b6e5e96088c0c81c65f31d9363a1904e6a37c3001ebf4cf4105ab92` |
| Two-hunk patch | `ef393d99b50198f1c0139de12d890f1f2c3cc0675bee19d22259de698c68e32e` |
| Manifest | `c1e7170e4272e6fcb54d816ecc335016529511320b5d056c151023cd2a3bd41f` |
| Generator executable | `0d7f05c598c611403ba541842967fda9d8b6c87eb62cf39e9ea946ef67da325c` |
| Native library | `d531d1725958be836a90051dc645a6caf3ebccac63c59144282e921a6d342bd5` |

Preparation uses the existing pinned source/compiler arguments with
`-Preset CacheEncodings`; validation uses
`./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset CacheEncodings
-InputDirectory artifacts/m6-cache-encodings-qualified-inputs
-OutputDirectory <fresh-output>`. Requested missing or incomplete audits fail.

`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope CacheEncodings`
requires the complete 16,384-case 040 batch for each mutation, and both batches
when the original regression is present. Evidence in
`artifacts/m6-cache-encodings-mutations-qualified/mutation-proof.json`:

| Mutation | Replacement mismatches | Original loop mismatches |
| --- | ---: | ---: |
| Scope-zero vector 11 | 4,096 | Inapplicable: absent old cases |
| Privilege before scope recognition | 2,048 | Inapplicable: absent old cases |
| Neither-cache user privilege bypass | 1,536 | Inapplicable: absent old cases |
| X set instead of preserved | 3,072 | 2,304 |
| Extra extension word consumed | 6,144 | 4,608 |

The flag and extension controls fail both original and replacement matrices;
source is restored byte-for-byte and rebuilt after each run. An initial harness
attempt placed its TRX check before execution and failed before testing; it is
retained separately in `artifacts/m6-cache-encodings-mutations/`, not counted as
mutation proof. The new enumeration includes every old cache case: push 0/1,
cache 1/2/3, scope 1/2/3, An 0..7, user/supervisor and CCR 0..31, exactly 9,216
cases per profile. For example the old `68040/CINV/1/cache=1/A0/super=True/
op=F448/ccr=00` maps to `68040/CINV/scope=1/cache=1/A0/super=True/ccr=00/op=F448`
with the same machine initialization, preserved-state expectation and sentinel.
The complete ordinary matrix additionally covers scope-zero and neither-cache.

This permits removal of only the duplicate cache loop from
`SyntheticModelSystemTests`. The retained method is named
`Move16TransfersAndBreakpoints`, still exercising 512 breakpoint and 3,072
MOVE16 scenarios per profile. Its `system-model` gate changes from 12,800 to
3,584, with the separate 16,384 cache gate required for each profile. The focused
consolidated selection passes sixteen tests, 131,072 cache plus 28,672 retained
MOVE16/breakpoint cases. Specialized cache, prefetch, bus-ordering, fault, JIT
and native regressions remain. No production CPU or public API change, package
publication or fresh consumer replay is introduced. The full remaining
restoration/reference/consolidation requirements and `roadmapComplete=false`
remain explicit.

Copied-report gate controls pass a complete prior-report baseline merged with
the final sixteen cache/MOVE16 reports, then reject a missing 040 cache report,
a reduced 16,383-case cache count and the stale 12,800-case retained-loop count.
Original reports are preserved. This copied baseline proves gate detection,
not a fresh full-suite run. Evidence:
`artifacts/m6-cache-encodings-gate-controls/controls.json`.

Final full Release CPU validation in `artifacts/m6-cache-encodings-full/`
passes **5,044 tests, eleven optional skips and zero failures**. All nine
qualified WinUAE presets pass their exact selections/counts against matching
CPU/adapter assemblies: CPU SHA-256
`1926da25083ab44ac860da0d40b14df043c6e5a78356f47922d4e50c19854c0a`,
adapter SHA-256
`ac2f9767ab33f266998db37e23f6f57eeea9840c845481eb8641d91f9e45a2db`.
The strict gate passes **16,093,014 logical cases in 603 batches**. Fresh pinned
SingleStepTests passes 312,500 cases across 125 files; Musashi passes 536 programs
with 88 explicit exclusions. Binary identities remain unchanged through the
strict gate. Production CPU source remains unchanged since `cb9679d`; the
preceding unpublished `.57` consumer evidence keeps its own original source,
package and binary identities. No new consumer replay or cross-checkpoint binary
equality is claimed. All remaining required roadmap gaps remain in progress.

### 040 physical RTE validation fault correction — 2026-10-05

[MC68040UM 8.4.6.7](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
requires a format-7 access frame for a fault during RTE frame validation,
preserving the incomplete original frame. Sections 8.4.6.2/4 and table 7-1
define transfer metadata and size encodings. The expectation source is the
processor manual; no hardware oracle or WinUAE fault execution is claimed.

The existing internal physical-address map can reject a read even with the MMU
disabled. Previously that path lost the original access width and built an
eight-byte format-0 vector-2 frame. The new fixtures reproduce **81,408 direct
and 30,528 chained entry mismatches**, with the corresponding handler returns
explicitly untested after the failed prerequisite. Evidence:
`artifacts/m6-rte-validation-fault-before/`. No production exception is injected
by the fixture: a one-shot map rejection reaches the real logical bus through
the public CPU factory.

The correction retains physical access width in internal fault metadata and
marks only pre-commit RTE validation reads. Fault entry stacks 60 bytes, with
the original RTE PC, live pre-validation SR, read/size/data-space attributes,
the original transfer address and no pending writebacks. Undefined EA and
writeback/push data use zero as an implementation convention. Successfully
consumed throwaways retain their stack-pointer and SR effects; no instruction
is automatically retried after partial side effects. Enabled-MMU faults and
other instructions' existing fault paths are unchanged and remain unqualified.
No public package API changes are introduced. The existing exception timing
key remains `IllegalInstruction`; physical timing is not qualified.

`SyntheticM68040RteValidationFaultTests` provides two ordinary-CI batches:

| Group | Passing phases | Architectural combinations | CCRs per combination |
| --- | ---: | ---: | --- |
| `rte-validation-physical-direct` | 162,816 | 2,544 | All 32, entry and handler return |
| `rte-validation-physical-chained` | 61,056 | 15,264 | 0/31, entry and handler return |

The matrix rejects every byte of the selected SR/PC/format/SSW/continuation-EA
read. It covers two direct and twelve one/two-throwaway supervisor-bank paths,
T0/T1/no trace, even/odd data-stack addresses, and zero/nonzero VBR. Forms are
short formats 0/2/3, unsupported 040 formats 4/15, and normal/CM/CT/CU/CP access
frames. Different candidate restoration SRs expose premature installation.
Common verification checks all registers, stack banks, exact PC/SR, exception
entry count, saved PC/SR, guarded original-frame and neighboring memory, and
pending CU/CP retention. The handler's following sentinel and discarded odd
PCs must not execute. SSW X, EA and invalid writeback/push data are masked;
writeback valid bits and the fault address are checked. Evidence:
`artifacts/m6-rte-validation-fault-fixed/` and the final full-suite reports.

The maintained `-Scope RteValidationFault` mutation command requires both
complete batches and restores/rebuilds production source after testing:

| Mutation | Direct entry mismatches | Chained entry mismatches |
| --- | ---: | ---: |
| Wrong format word | 81,408 | 30,528 |
| Wrong word/long size | 81,408 | 30,528 |
| Fault address incremented | 81,408 | 30,528 |
| Saved RTE PC incremented | 81,408 | 30,528 |
| Continuation EA loses validation marker | 12,288 | 4,608 |

Failed prerequisites retain untested handler-return phases instead of reporting
them as passing. Evidence:
`artifacts/m6-rte-validation-fault-mutations/mutation-proof.json`.
No historical regression is retired by this checkpoint.

The complete discovery command validates the exact independent byte-range
distribution, seven fixture/command identities and CPU/assembly identities.
It passes **1,348,224 phases**, with zero mismatching/unsupported promoted
phases, and still fails on **480 named untested fault/context requirements**.
Supervisor physical validation is not a replacement for user-tail validation,
internal restoration/double faults, other real instruction fault entry,
writeback-handler execution or CP context transfer. Re-execution of a repaired
original RTE, warmed JIT fault cases, public BERR signaling, physical bus beats
and enabled-MMU operation are not qualified by these two phases. Evidence:
`artifacts/m6-rte-validation-fault-discovery/`.

Five copied-report/source controls reject a missing validation report, shortened
case count, foreign byte-range combination, missing fixture identity and changed
CPU identity for their specific reasons. The baseline retains the 480 inventory
failures and is not called a passing roadmap. Evidence:
`artifacts/m6-rte-validation-fault-report-controls/controls.json`.

Full Release CPU validation in `artifacts/m6-rte-validation-fault-full/` passes
**5,046 tests, eleven optional skips and zero failures**. All nine qualified
WinUAE presets pass their existing exact callback/frame selections against the
same CPU and adapter assemblies. CPU SHA-256:
`91e634457a2736f4c98a19abeac75bab3562601caabbce967e30a3a564af7efb`;
adapter SHA-256:
`19359ce94782ea27c4226691c97e4abab6b939b905343043aa626f9883b2027e`.
The strict gate passes **16,316,886 logical cases in 605 batches**, with fresh
pinned SingleStepTests (312,500 cases / 125 files) and Musashi (536 programs /
88 explicit exclusions). Identities remain unchanged through the strict gate.

Isolated CopperScreen at baseline `d9beae8b88be24032221e3482942a249c03c27d3`
validates private unpublished NuGet `1.5.2-synthetic-dev.58`: Release build has
zero warnings/errors; host 149 passing/six optional skips, disk 74, separate
engine diagnostics 1,080, and native Workbench boots two/A1200 persistence one
pass without native skips. All four assets and loaded assemblies match the
package and the CPU binary above. Package SHA-256:
`e6b829df02b2eb449d68149832c4c569ae6a3f53e3c9774135a867fe490a52e9`.
Evidence: the consumer's `artifacts/rte-validation-fault-identities.json`,
`artifacts/rte-validation-fault-validation-v2/` and separate diagnostic outputs.
The first consumer helper failed during restore because its URI argument was
interpreted as a local source; the validated rerun uses an explicit isolated
NuGet configuration. A preliminary ROM extraction identity check rejected raw
bytes because the native test pins the supplied ZIP; no media was overwritten.
Neither attempt is counted as semantic replay evidence.

Root CopperScreen changes, its pinned dependency boundary and published package
versions remain untouched. No package is published. The complete restoration,
reference and consolidation requirements remain in progress, with
`roadmapComplete=false`.

### 040 access-fault entry halt qualification — 2026-10-05

Authority: [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
7.6.3, 8.2.1 and exception-entry figure 8-1. A further access fault during
stacking or vector fetch halts until external reset; a fault during an executing
handler starts a new exception. This audit uses real translation-disabled
physical-map rejections, independent addresses and public factory execution.
It does not inject a production exception or call CPU frame/EA helpers.

The interpreter previously allowed the second fault to escape to its host.
Its new private fatal latch persists through host subroutine/task entry and
interrupt requests. Compiled entry shares that latch, without retrying partial
stacking or recording another exception. Existing interpreter timing keys and
the compiled 34-cycle exception policy are retained; neither is a physical
timing qualification. A separate classic-emitter defect bypassed the
model-aware B/W/L helpers. Those six paths now use 040 address-map checks and
unaligned data access; the 000 pipeline paths retain their existing behavior.
Compiled exception entry also preserves M and clears trace like the interpreter.

| Ordinary group | Logical scenarios | Architectural combinations |
| --- | ---: | ---: |
| `rte-access-entry-double-fault` | 98,304 | 3,072 |
| `rte-access-handler-refault` | 3,840 | 120 |
| `access-double-fault-dispatch-accurate` | 1,088 | 544 |
| `access-double-fault-dispatch-v1` | 1,088 | 544 |
| `access-double-fault-dispatch-v2` | 1,088 | 544 |

Each fatal scenario verifies no transfer after rejection, unrelated register/
memory preservation, inactivity through host entry and batches, then external
reset and a MOVEQ sentinel. Partial frame contents and exact SP/write ordering
are opaque. The main matrix includes every byte of the 60-byte frame and
four-byte vector, both supervisor banks, all CCRs, three defined trace states,
odd/even stacks and two VBRs. T1+T0 is undefined and excluded. The handler control
faults all five validation reads after the first entry has completed and checks
the new defined frame while preserving both older frames.

Dispatch cases require actual warmed compiled code before selecting RTE fallback
or compiled MOVE B/W/L read/write side exits. They check access widths, odd/even
data and stacks, both supervisor banks and CCR 0/31. Generic operand faults still
use the existing approximate short frame; these cases qualify fatal entry only,
not general architectural format-7 restart. Accurate active-batch fault delivery,
handler-entry prefetch, internal-state restoration, repaired-original RTE retry,
user-tail validation, writeback handlers and CP context transfer remain required.
The 480-case untested inventory remains in the complete audit. No old regression
is retired and milestone 6 remains in progress.

Failed-before evidence: `artifacts/m6-access-entry-double-fault-before/` records
98,304 escaped second faults, with the 3,840 handler controls passing. Warmed
dispatch failures in `artifacts/m6-access-entry-double-fault-dispatch-before/`
also exposed compiled exception and odd-stack faults; later classic-dispatch
diagnostics identified the missing model-aware memory routing. These discovery
runs are not acceptance evidence. Validation records below bind the final
selection and binaries; no public package release is authorized by this work.

Final complete 040 discovery in
`artifacts/m6-access-entry-double-fault-discovery/` executes 26 xUnit tests:
25 pass and the retained inventory fails on 480 untested cases. All 20 reports,
six fixed examples, 1,453,632 passing scenarios, fixture/source/assembly identities
and independent combination distributions validate. Five missing-report, shortened
count, foreign-combination and fixture/CPU identity controls reject their intended
defects in `artifacts/m6-access-entry-double-fault-report-controls/controls.json`.

Ten maintained mutations in
`artifacts/m6-access-entry-double-fault-mutations-v3/mutation-proof.json`
detect fatal-latch, reset, compiled halt, master-stack and all six classic
memory-routing defects. Each executes all three 1,088-case dispatch batches.
The latch and reset mutations fail 1,088 per engine; compiled halt fails 576
per JIT engine, master-stack selection 192 per JIT engine, and each specialized
memory route fails 96 classic cases. Source is restored and rebuilt afterward.
A preliminary reset mutation survived, exposing a missing host-entry check after
external reset; the final fixtures verify both restart and restored host entry.
An intermediate mutation run rejected a mixed-newline anchor before execution;
the final run normalizes source line endings and detects all ten mutations.
Neither preliminary run is final mutation acceptance.

Full Release CPU validation in `artifacts/m6-access-entry-double-fault-full/`
passes **5,051 tests, eleven optional skips and zero failures**. All nine
qualified WinUAE presets retain their exact callback/frame counts, checked in
`qualified-preset-identities.json`. The strict gate passes **16,422,294 logical
cases in 610 reporting batches**, with fresh pinned SingleStepTests (312,500 /
125 files) and Musashi (536 programs / 88 explicit exclusions). CPU SHA-256:
`216d59e53e3190a9f9faffa0d7790554cbf2528c1d7d41c48b6f3da22bd4751c`;
test/reference adapter SHA-256:
`d343fb7cd582a6e881153280e781e3b84b93d3bd352014d239b67beb4eb3780f`.
Both identities remain unchanged through the strict gate.

Isolated CopperScreen baseline `d9beae8b88be24032221e3482942a249c03c27d3`
passes through unpublished private NuGet `1.5.2-synthetic-dev.59`: Release build
zero warnings/errors, host 149/six optional skips, disk 74, separate engine
diagnostics 1,080 and three native Workbench/A1200 boot/persistence replays with
no native skips. Four assets and loaded CPU assemblies match the package and
the CPU binary above. Package SHA-256:
`1b8622efefe5364447020a4f3ce1d4bb9c0766b86bcfa50bc4a559ed53bcbe1d`.
Consumer evidence is under `artifacts/access-entry-double-fault-validation-v2/`,
separate diagnostic outputs and `artifacts/access-entry-double-fault-identities.json`.
Root CopperScreen edits and published packages remain untouched. All remaining
reference/consolidation requirements persist with `roadmapComplete=false`.

## 040 accurate batch-fault checkpoint — 2026-10-05

Physical faults previously escaped accurate batch execution, including cached
blocks, although scalar execution delivered them. Shared instruction execution
now offers the 040 fault handler; normal/model-specific cached blocks and cached
self-branches also deliver faults and count the failed instruction exactly once.
Boundary callbacks remain outside fault handling. Completed prefixes and partial
MOVE address-register updates are preserved without retry.

`SyntheticM68040BatchFaultTests` contributes `rte-validation-batch` (129,024
scenarios, 4,032 combinations) and `access-fault-batch-dispatch` (10,368 scenarios,
5,184 combinations). The former combines cold/warmed execution, prefixes 0/1/3,
ISP/MSP, all CCRs, defined incoming trace modes, alignment, VBR, instruction-cap/
boundary limits and each byte of the five validation reads. The latter covers
warmed load/store, mixed model-specific prefixes, partial source/destination
updates and self-branch fetch faults; instruction caps, boundary denial and cycle
deadlines; successful entry and fatal stack/vector faults. Counts, callbacks,
handler sentinels, surrounding state and exact scalar/batch transfer sequences
are checked. Host code peeks are separate from CPU accesses.

Architectural validation-frame expectations follow MC68040UM 8.4.6.7, and fatal
entry follows 7.6.3/8.2.1. Existing generic short operand/fetch frames remain
approximate and unqualified as architectural format-7 restart. Scalar/batch
machine/native cycle equality and bus-order equality qualify execution policy,
not physical timing. The FMOVE prefix selects model-specific block dispatch;
it does not qualify FPU arithmetic. Enabled MMU operation remains outside scope.

Failed-before evidence in `artifacts/m6-batch-fault-before/before.trx` records
two failed batches and all 139,392 scenarios mismatching due to escaped faults.
`artifacts/m6-batch-fault-fixed/fixed.trx` records two passing batches, with zero
mismatches, unsupported execution or untested promoted scenarios. The strict
ordinary gate requires both exact report counts. No old regression is retired.
Dedicated discovery distribution and maintained per-path mutation integration
are pending; the existing 480-case untested inventory and broader qualification
requirements remain. This checkpoint does not complete milestone 6 or authorize
public package publication.

Final validation in `artifacts/m6-batch-fault-full/` passes **5,053 Release CPU
tests, eleven optional skips and zero failures**. All nine qualified WinUAE
presets retain their exact directory/callback/frame selections and matching
CPU/adapter identities in `qualified-preset-identities.json`. The strict gate
passes **16,561,686 logical cases in 612 reporting batches**, with fresh pinned
SingleStepTests (312,500 cases / 125 files) and Musashi (536 programs / 88 explicit
exclusions). Four controls in `artifacts/m6-batch-fault-report-controls/controls.json`
reject missing and shortened reports for each new batch, then restore them.
Audits and package validation use the same compiled snapshot with `--no-build`.
CPU SHA-256: `178df423e5c24ff107d41ba49dd5789d432626260b67cc3cb95dce5c5ca4d1f0`;
test/reference adapter SHA-256:
`30535c74aa5af87576df8835d52bc7755d6f8cd4d2999e325781a93de90f5e80`.

Isolated CopperScreen baseline `d9beae8b88be24032221e3482942a249c03c27d3`
passes through unpublished private NuGet `1.5.2-synthetic-dev.60`: Release build
zero warnings/errors, host 149/six optional skips, disk 74, separate engine 1,080
and three native Workbench/A1200 boot/persistence replays without native skips.
Four assets and loaded CPU assemblies match the package and CPU above. Package
SHA-256: `e83f68bf8572cdf0b00a63eda6abd5b3a7b4be5a21d80dcbadb2f29fe6f8b001`.
Consumer evidence is under `artifacts/batch-fault-validation/`, separate
diagnostic outputs and `artifacts/batch-fault-identities.json`. Root CopperScreen
changes and public packages remain untouched. Dedicated 040 discovery was not
rerun or extended in this checkpoint; its previously recorded 480 untested
requirements remain, with `roadmapComplete=false`.

## 040 batch-fault gate and mutation qualification — 2026-10-05

The dedicated `test-copper68k-040-access-frames.ps1` selection now includes both
ordinary batch-fault groups. Its independent enumeration specifies cached/cold
execution, prefix lengths, stack banks, valid trace modes, alignment, VBR, limits,
fault outcomes and bytes. Validation read ranges are fixed in the command rather
than imported from the C# fixture. Each of 4,032 RTE combinations must contain
32 CCR cases; each of 5,184 dispatch combinations contains CCR 0/31. Independent
enumeration cardinalities are themselves checked. The command requires nine
distinct fixture/command identities, exact CPU sources and both compiled binaries.

Fresh `artifacts/m6-batch-fault-discovery/` executes 28 tests: 27 pass and the
retained inventory fails on 480 untested cases. All 22 reports and six fixed
examples are present, with **1,593,024 passing scenarios, zero mismatches and
zero unsupported execution**. Including the inventory gives 1,593,504 logical
cases. The command validates these distributions and identities before reporting
the expected incomplete gate. Its inventory reason now acknowledges selected
active-batch coverage without removing any user-tail or internal-restoration gap.

Seven maintained mutations execute all 139,392 cases in both batch-fault groups
on every run. Detection requires mismatches in the intended combination, in
addition to complete xUnit/report selection. Thus a compiler failure, empty
selection or unrelated failure cannot satisfy qualification. Evidence:
`artifacts/m6-batch-fault-mutations/mutation-proof.json` and per-mutation reports.

| Mutation | RTE mismatches | Dispatch mismatches | Intended detection |
| --- | ---: | ---: | --- |
| Cold delivery removed | 129,024 | 1,728 | Cold RTE and slow partial MOVE path |
| Normal cached delivery removed | 0 | 4,608 | Warm load/store and mixed forms without a model-specific prefix |
| Model-specific cached delivery removed | 0 | 2,304 | Mixed blocks with completed FPU register-transfer prefix |
| Self-branch delivery removed | 0 | 1,728 | Warm self-branch physical fetch |
| Self-branch count doubled | 0 | 1,728 | Instruction-count contract |
| Cached callback omitted on fault | 0 | 4,608 | Boundary callback contract |
| Failed instruction retried | 129,024 | 3,456 | Successful-entry cases, including partial MOVE effects |

All mutation cases execute with zero unsupported or untested outcomes. The retry
mutation explicitly rewinds the failed PC after delivery and executes again;
partial-store combinations detect repeated operand effects independently of
scalar/batch agreement. Production source is restored byte-for-byte and rebuilt
with zero warnings/errors. No regression is retired and no CPU behavior changes
are added by this qualification checkpoint. Generic short frames, callback/count
and bus/cycle policies retain their previously documented scope; this does not
qualify general architectural restart, enabled MMU, FPU arithmetic or physical
timing.

Eleven copied-input controls in
`artifacts/m6-batch-fault-dedicated-report-controls/controls.json` reject missing,
shortened, foreign and redistributed combinations for both new groups, plus a
missing fixture identity and altered CPU-source/assembly identities. Distribution
controls preserve total cases and combination counts, proving that fixed counts
per architectural combination are enforced independently of aggregate totals.
Every control fails for its specific intended reason.

Qualification starts from `c46999824c6ab62b6220543e26c791e07e12e961`, CPU tree
`6bb8bd68a93ba23bf79d1d077d0b34c48e5e6679`. Current discovery identities record
CPU SHA-256 `fc36b2ce0d0f4a816d08e322836d7d59feb333d71d7909fe3b747f3b44e247c9`
and test/reference adapter SHA-256
`c1163ad174e5c900dda725b6726427ad0bf1a72aef54e0cfe71a6381aa79b87c`.
These rebuilt identities belong to this discovery run; the preceding full CPU,
external-reference and private-consumer evidence remains separately bound to its
recorded binaries. Ordinary report validation retains 16,561,686 logical cases
in 612 batches; this checkpoint revalidates those reports rather than claiming
another full ordinary execution or external audit. No new production change,
consumer package or public release is introduced.

Dedicated batch-fault integration and its mutation requirement are now satisfied.
The 480-case inventory, user-tail/internal-restoration faults, handler-entry
prefetch, repaired-original-RTE retry, general format-7 restart, writeback/context
transfer and the earlier model/reference/consolidation requirements remain open.
Milestone 6 remains **in progress**, with `roadmapComplete=false`.

## 040 executed RTE repair and retry — 2026-10-05

The authority is [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
8.4.2, 8.4.6.7 and 8.2.6: throwaways commit their stack/SR changes,
validation faults preserve the attempted frame for software repair, and trace
depends on instruction entry versus restored trace state. These are independent
synthetic architectural expectations, not hardware captures or software-reference
agreement. Enabled MMU, physical timing and opaque internal fault phases are
outside this execution fixture.

`SyntheticM68040RteRepairTests` adds two ordinary CI batches:

| Report | Logical instruction phases | Architectural combinations |
| --- | ---: | ---: |
| `rte-repair-boundaries` | 143,424 | 540 |
| `rte-repair-chained` | 768,960 | 45,792 |

The first covers all 32 CCRs, both entry supervisor banks, all incoming/restored
trace states and all three restored stack banks using a canonical SR validation
fault. The second faults each byte of the selected SR/PC/format/SSW/continuation
EA reads after every one/two-throwaway supervisor path, with CCR 0/31, incoming
T1, all restored trace states, both VBRs, odd/even stacks and all result banks.
Both cover formats 0/2/3, invalid 4/15 repaired to 0, normal format 7 and CM,
CT, CU and original-vector CP49. CP50–55 are separately qualified by existing
positive continuation fixtures, not by these repair cases.

Repair is executed code: fixed MOVE.W/MOVE.L immediate instruction encodings
write the original SR/PC/format, then MOVE.W explicitly clears incoming trace in
the fault frame. Each store checks its flags, exact PC, unchanged registers and
surrounding memory; four exact bus writes are verified before handler return.
Handler RTE returns to the original RTE with the current supervisor tail still
selected. Retry may not reread consumed throwaways. It restores the repaired
frame, returns/converts pending delivery, resumes MOVEM using saved EA rather
than the handler's live base, and applies repaired trace to the following
instruction. Untouched incoming-trace retry is explicitly not claimed.

The initial fixture run failed because `3EFC` encoded postincrement. Correcting
it to indirect `3EBC` fixed the fixture; this was not a production CPU defect.
The final complete 040 audit in `artifacts/m6-rte-repair-audit/` executes 30 tests:
29 pass and the retained inventory test fails, with 2,505,408 passing phases,
zero mismatches/unsupported execution and 480 untested requirements. Independent
PowerShell enumeration requires 24 reporting batches, six fixed examples and
ten distinct fixture/command identities. Nine copied-input controls in
`artifacts/m6-rte-repair-controls/controls.json` reject missing, shortened,
foreign and redistributed repair combinations for both new reports and an
omitted repair fixture identity. Redistribution preserves aggregate counts and
combination cardinality, proving per-combination counts are enforced.

`-Scope RteRepair` in the maintained mutation command executes both complete
batches for every mutation. Required diagnostic phases are enforced; a compile
failure, empty selection or mismatch only at an unrelated prerequisite cannot
satisfy qualification.

| Mutation | Boundary mismatches | Chained mismatches | Required phase |
| --- | ---: | ---: | --- |
| Short-frame return PC +2 | 13,824 | 70,848 | Retry RTE |
| Saved MOVEM EA +4 | 1,728 | 12,096 | Following MOVEM |
| Pending exception stacks prior SR | 5,184 | 32,256 | Retry RTE conversion |

All three are detected in `artifacts/m6-rte-repair-mutations/mutation-proof.json`;
prerequisite-dependent later phases are labeled untested during mutations, with
no unsupported execution. Production sources are restored byte-for-byte and
rebuilt with zero warnings/errors. No existing regression is retired.

Qualification begins at `9ab8d56030898e520c42142a59ce1191e5965d83`, CPU tree
`6bb8bd68a93ba23bf79d1d077d0b34c48e5e6679`. Discovery records CPU SHA-256
`e93cb1cdd30a88f9c6fceb829d80e5e2442b240a6751b977c390a2d1c9872af3`
and test/reference adapter SHA-256
`61f468ab7e75aebdaeea466f5315b0ceba2649e66b35bac6c3c79ad8e1d1686f`.
These are this rebuilt test snapshot's identities; the earlier full CPU,
external-reference and private `.60` consumer evidence retains its own binary
identities. No production CPU change, new consumer package or public release is
introduced in this checkpoint.

The fresh ordinary synthetic command in `artifacts/m6-rte-repair-ordinary/`
passes 616 tests with no failures or skips and validates 17,474,070 logical
cases in 614 reporting batches across all eight profiles. Its CPU and adapter
binaries match the dedicated audit identities above. External reference audits
were not requested/executed in this run; their preceding pinned evidence remains
separately identified. The full goal is not implied by the passing semantic gate.

User-tail validation, internal restoration/double faults, handler-entry prefetch,
untouched incoming-trace retry, chained odd-PC provenance, general real format-7
restart, writeback/context transfer and earlier model/reference/consolidation
requirements remain open. The 480-case inventory and `roadmapComplete=false`
are preserved. Milestone 6 and the full goal remain **in progress**.

## 040 instruction-fetch access faults — 2026-10-05

The authority is [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
8.2.1 and 8.4.6, including 8.4.6.2/7. An instruction access error uses a
60-byte format-7 frame: PC identifies the executing instruction, FA identifies
the faulted prefetch, TM selects instruction space, and no writeback is valid.
These are manual-derived synthetic expectations, not hardware captures. The
fixture selects the existing cache-disabled accurate long-prefetch policy;
its four-byte access width is not physical pipeline/bus qualification.

Production fault entry now uses that layout for MMU-disabled instruction fetch.
Accurate scalar, normal hot-block and self-branch boundaries record the executing
PC before fetching; an extension or aligned long's other half must not become
the restart PC. Existing exception timing policy and partial operand effects
are preserved. Data errors retain their explicitly unqualified short-frame
policy: writebacks/restart cannot be implemented by changing a tag and replaying
an instruction after partial effects. Compiled fetch-PC provenance and enabled
cache/speculative prefetch deferral remain unqualified.

`SyntheticM68040InstructionFaultTests` adds two ordinary CI batches:

| Report | Logical instruction phases | Architectural combinations |
| --- | ---: | ---: |
| `instruction-fault-frame` | 36,864 | 576 |
| `instruction-fault-restart` | 79,872 | 624 |

Fixed opcode, extension-low, following-opcode and self-branch encodings cover
all CCRs, user/ISP/MSP stacks, odd/even stacks, both VBRs and rejection at every
byte of the selected long prefetch. Frame/handler-return coverage includes
incoming T0/T1; deterministic restart values execute handler RTE, the original
instruction and a following branch. Expectations check defined frame fields,
inactive stacks, unchanged registers, surrounding memory, single fault delivery
and restored translation bypass. Undefined frame fields are masked explicitly.

The first following-opcode fixture remained in an already fetched long;
adding a fixed NOP prefix makes the rejected fetch observable. This was a
fixture correction. With that corrected fixture, historical production source
at `36a8edf266dc682036d638fc9a5bb3cd0985439a` fails all 38,400 fault-entry
cases: 18,432 frame and 19,968 restart mismatches, with later prerequisite
phases explicitly untested. Evidence is in
`artifacts/m6-instruction-fault-before-v2/`; historical CPU SHA-256 is
`f4756485a7945d5fa49e01be6af0c0332d279d60be364c48a435ba7dccc4d313`
and historical adapter SHA-256 is
`c6aab37470dada0f9ceb25aa7899437d09c81019e31c16e200d8a7a44dee3c18`.

The maintained `-Scope InstructionFault` command executes both complete batches
for each mutation and requires an intended fault-entry mismatch. Short-frame
and data-TM substitutions each produce 18,432/19,968 mismatches; prefetch-PC
substitution produces 9,216/7,680 mismatches. Prerequisite-dependent phases are
untested during mutations, not successful coverage. All three mutations detect
their intended defect in `artifacts/m6-instruction-fault-mutations/`.
The full seven-mutation `BatchFault` campaign also passes with updated
architectural self-fetch expectations in
`artifacts/m6-instruction-fault-batch-mutations/`. Sources are restored
byte-for-byte and rebuilt. No regression is retired.

The complete dedicated command in `artifacts/m6-instruction-fault-audit/`
executes 32 tests: 26 reporting batches and six fixed examples. It records
2,622,144 passing phases, zero mismatches/unsupported execution and the retained
480 untested requirements. Its intentional inventory failure remains a failed
full discovery gate. Independent enumeration requires eleven fixture/command
identities and fixed per-combination counts. Nine copied-input controls in
`artifacts/m6-instruction-fault-controls/` reject missing, shortened, foreign and
redistributed reports for both new groups, plus omitted fixture identity.
Redistribution preserves aggregate counts and combination cardinality.

Qualification starts from HEAD `36a8edf266dc682036d638fc9a5bb3cd0985439a`.
The audit's committed-tree identity describes that starting point; recorded
working-source hashes describe the correction. Qualified CPU SHA-256 is
`3cc890bd66d7c5cc403a00a6d2aed556983a74a42595be90c60babb799eeac0d`
and test/reference adapter SHA-256 is
`5540e61568601aa580a039f18cde12e312680ed79a6fab6a56d2476e40034be1`.
Dedicated, full-suite, external-reference and private-consumer executions below
all match these binaries.

Fresh full Release CPU validation in `artifacts/m6-instruction-fault-full/`
passes 5,057 tests with zero failures and eleven optional skips. All nine pinned
WinUAE presets match their exact directory/callback/frame counts and binaries;
`qualified-preset-identities.json` records each selection. Generator source is
`025b999239800357e95065fe5b9a15ea5b300fa7` and runner source is
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Strict report validation checks
17,590,806 ordinary logical cases in 616 reporting batches across all eight
profiles. Fresh external audits pass SingleStepTests' 312,500 cases in 125
selected files at `64b253116a3de04aaac4346c43680960dc9b67e5` and Musashi's
536 programs with 88 explicit exclusions at
`72c1d74800f3087b45a0c1a7342601bbed898881`. These are scoped software-reference
results, not exhaustive architectural combinations or silicon evidence.

Isolated CopperScreen baseline `d9beae8b88be24032221e3482942a249c03c27d3`
consumes unpublished private `1.5.2-synthetic-dev.61`, package SHA-256
`5422e6153e28380b9898fdb58d97fb992d982bd94642f911b31573997420a020`.
The package's embedded CPU, four asset manifests and four loaded CPU DLLs match.
Release build has zero warnings/errors; host tests pass 149 with six optional
skips, disk tests pass 74, separate engine diagnostics pass 1,080, and all three
native Workbench/A1200 boot/persistence replays pass without native skips.
Evidence and identities are under the isolated consumer's
`artifacts/instruction-fault-validation/` and
`artifacts/instruction-fault-identities.json`. Root consumer changes and its
published dependency pin remain preserved. No public package/API is released.

Data writebacks/restart, compiled fetch-PC provenance, enabled-cache/speculative
prefetch, handler-entry prefetch, user-tail/internal-restoration faults,
untouched incoming-trace retry, context transfer and all earlier model/reference
and consolidation requirements remain open. Original Basic's 47 mismatching and
eight unsupported groups are not reclassified. The 480-case inventory and
`roadmapComplete=false` remain explicit; milestone 6 is **in progress**.

## 040 host-reader boundary and handler-prefetch discovery — 2026-10-05

The authority for the handler boundary is
[MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
8.1, figure 8-1, and 7.6.3. Entry prefetch precedes handler execution and a
fault during entry halts the processor; a later fault in executing handler code
starts another exception. Four required entry longs must be distinguished from
the demand-driven frontend's current fetch policy. Software-reference agreement
or a successful later handler instruction cannot qualify this entry window.

Investigation also found that the logical 040 bus advertises `IM68kCodeReader`
even when its physical bus has no host code reader. Its fallback host read then
performs a real instruction fetch during speculative hot-block construction,
before `BeforeInstruction`, and can leak the physical fault to the host. The
accurate interpreter now admits hot-block construction only when the physical
bus supplies a host code reader. Other buses execute through the ordinary
instruction boundary and fault handler. This preserves the existing timing
keys and available host-reader fast path; no throughput or physical timing
qualification is inferred.

`SyntheticM68040HandlerPrefetchTests` supplies fixed opcode, extension and RTE
validation faults. Instruction faults cover user/ISP/MSP; RTE begins on ISP/MSP.
All CCRs, incoming trace states, odd/even stacks, VBRs and rejected bytes are
included. A denied cold batch must make one before callback, no after callback,
no fetch/rejection, and no state/memory change. After a valid handler MOVEQ/NOP
prefix, a new instruction-fetch fault must preserve the prefix, stack an
independently checked format-7 frame and retain precise handler PC/SR.

| Report | Logical cases | Architectural combinations | Status |
| --- | ---: | ---: | --- |
| `handler-prefetch-executing-scalar` | 12,288 | 384 | Passing ordinary CI |
| `handler-prefetch-executing-batch` | 12,288 | 384 | Passing ordinary CI after correction |
| `handler-prefetch-entry-scalar` | 196,608 | 6,144 | Failing reference discovery |
| `handler-prefetch-entry-batch` | 196,608 | 6,144 | Failing reference discovery |

The entry reports reject each byte of the four-long window with four handler
word offsets. They require fatal halt before handler execution, no second
exception or subsequent bus transfer, frozen host-entry behavior and external
reset recovery. Current source fails at the entry prerequisite: no handler
prefetch is attempted during exception processing. Subsequent halt/reset checks
are therefore not yet qualified by this fixture. These failures are actual
executed mismatches, not exclusions or renamed passing policy coverage.

Before correction, the focused executing-handler audit passes its scalar batch
but fails all 12,288 batch cases; evidence is in
`artifacts/m6-handler-prefetch-positive-v2/`. The maintained mutation command
`-Scope HandlerPrefetch` reinstates the old host-reader admission rule, executes
both full ordinary reports and requires a denied-boundary mismatch. It detects
the defect in `artifacts/m6-handler-prefetch-mutations-v2/`; production sources are
restored byte-for-byte and rebuilt. No historical regression is retired.

The complete run in `artifacts/m6-handler-prefetch-audit/` executes 36 tests:
33 pass and three discovery tests fail. It checks 3,040,416 logical cases:
2,646,720 passing, 393,216 entry-prefetch mismatches, zero unsupported execution
and 480 untested inventory cases. Seventeen copied-input controls in
`artifacts/m6-handler-prefetch-controls/` reject omitted, shortened, foreign and
redistributed combinations in all four new reports, plus missing fixture
identity. Controls require their specific diagnostic; a pre-existing discovery
failure cannot satisfy corruption detection. Redistribution preserves aggregate
counts and combination cardinality.

The source baseline is `d99f7c80efd42399e2c367fccdf14ff853f695f2`; the dedicated
manifest distinguishes that committed tree from corrected working-source
hashes. Qualified CPU SHA-256 is
`adae3e327e83199dbc804346b7eb1814c05b2f05b89a799d42cfb8e6043b0146`
and test/reference adapter SHA-256 is
`4dabd59a3453a4dd1320224a04dcefa9dfa5045066da3f83facffedaea0f80d7`.
The isolated consumer at `d9beae8b88be24032221e3482942a249c03c27d3`
consumes unpublished private `.62`, package SHA-256
`da4a2dd6c57d2248809d0b873dd4f96f96040bcce98416a4543e665598abd955`.
Release build passes with zero warnings/errors; host 149/six optional skips,
disk 74, separate engine diagnostics 1,080 and all three native Workbench/A1200
boot/persistence replays pass, with no native skips. The embedded CPU, four
assets and four loaded DLLs match. Evidence is under the isolated consumer's
`artifacts/handler-prefetch-validation/` and
`artifacts/handler-prefetch-identities.json`; root CopperScreen edits and its
published dependency pin are preserved. No public package is released.

Fresh full Release CPU execution in `artifacts/m6-handler-prefetch-full/`
passes 5,059 tests with zero failures and thirteen opt-in skips. Two skips are
the required handler-entry discovery tests executed separately above; their
failures are not successful full-goal coverage. All nine pinned WinUAE presets
match exact directory/callback/frame counts and the same CPU/adapter binaries;
`qualified-preset-identities.json` records each selection. Generator and runner
pins remain `025b999239800357e95065fe5b9a15ea5b300fa7` and
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Strict ordinary report validation
checks 17,615,382 cases in 618 batches across all eight profiles. Fresh pinned
SingleStepTests passes 312,500 cases / 125 files at
`64b253116a3de04aaac4346c43680960dc9b67e5`; Musashi passes 536 programs with
88 explicit exclusions at `72c1d74800f3087b45a0c1a7342601bbed898881`.
The restored mutation sources/binaries match this qualified snapshot. These
software-reference selections do not qualify the missing handler-entry window,
silicon behavior, physical timing or the full roadmap.

The dedicated gate independently enumerates all four new reports and requires
36 tests, 30 reporting batches, six fixed examples and twelve fixture/command
identities. The new entry mismatches must fail it; the earlier 480-case required
inventory remains intact. Completing entry prefetch requires a real buffered
entry/fault protocol that handles the complete window and distinguishes faults
after execution starts. Suppressing the discovery test, fetching only a safe
first word or retrying an instruction after operand side effects is insufficient.
All earlier model, restart, writeback, reference and consolidation requirements
remain open. Milestone 6 remains **in progress**, `roadmapComplete=false`.
## 040 access-error handler entry window — 2026-10-05

This checkpoint addresses the preceding handler-entry discovery rather than
discarding its failed evidence. The authoritative scope is cache-disabled,
MMU-disabled accurate 040 scalar and batch execution after instruction-fetch
faults and pre-commit supervisor RTE validation faults. Other entry routes,
enabled caches/MMU, compiled fetch provenance and physical pipeline timing remain
unqualified.

The documentary reading was checked against the image of MC68040UM figure 8-1
(printed 8-3), not just extracted text: four longwords precede instruction
execution, and an entry bus/address error leads to HALT. Section 8.2.1 also
distinguishes errors during access-error processing from later handler execution.
This is the exception-entry interpretation; it does not override the ordinary
speculative-fetch deferral rules in 7.6.1/8.2.1. Suppressed unused prefetches and
enabled-cache line fills still require separate qualification.

An independently archived Motorola 1993 manual was inspected at PDF page 224
(one based): [scan](https://ftpmirror.your.org/pub/misc/bitsavers/components/motorola/68000/68040/MC68040_Users_Manual_1993.pdf),
17,233,053 bytes, SHA-256
`93741393f70656941e413beb232c060a5e4e0218ea2adc2f5b392f9165454a7f`.
The [NXP reference](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
remains the primary citation. The scan is documentary evidence, not hardware or
an executed reference CPU. Neither manual nor ROM/media files are committed.

The timed bus now acquires and retains four aligned longwords beginning at the
uncached half-line boundary. Entry is published only after all four transfers
succeed. An entry rejection halts before handler execution; an odd handler
address halts without another fetch or frame. Original format-7 fields,
registers, neighboring memory and translation-bypass restoration are checked.
HALT remains sticky across interrupts and host entry until external reset.
Sequential consumption uses retained data; selected branch, subroutine, task
and host-map changes discard stale data. Host map changes during a rejected
access cannot discard newly fetched entry data on the next scalar step.
Existing timing-plan keys are preserved; newly required bus accesses do not
establish physical timing accuracy.

The later-handler fault fixture now executes eight retained words before
rejecting the demand fetch at handler+16. Its previous handler+4 fault would
target data already acquired during entry and could not prove later demand
fault delivery. Historical report counts and failures remain unchanged in the
preceding record; fresh results describe the revised fixture explicitly.

| Group, each scalar/batch | Cases | Combinations |
| --- | ---: | ---: |
| Entry rejection | 196,608 | 6,144 |
| Later handler demand fault | 12,288 | 384 |
| Entry retention/context change | 7,680 | 240 |
| Odd handler address | 12,288 | 384 |

All eight groups are ordinary CI requirements: **457,728 cases**. The two
formerly optional entry discovery tests are promoted; their existing fault-byte
matrix remains intact. Retention fixtures replace handler code after entry to
prove that fetched values are retained rather than read again. The stable-map
fixture rejects page probes without consuming one-shot CPU faults, advances its
generation on actual map changes, and records real accesses separately.

Before host-entry/map invalidation was corrected, the final retention fixture in
`artifacts/m6-entry-retention-before-v3/` reports 4,608 passing and 3,072
mismatching cases per route. Intermediate fixture-development outputs are not
qualification evidence. Five maintained `EntryPrefetch` mutations separately
remove the entry window, its fourth long, retained data, subroutine invalidation
and task invalidation. Each requires complete scalar/batch selections and its
specific mismatch, not merely a failing process.

The complete 040 gate requires **40 executed tests, 34 reporting batches, six
fixed examples and twelve input identities**. Its independent iterator checks
all eight groups' exact combinations. The preserved 480-case protocol inventory
still fails completion; this checkpoint does not remove it. No old regression is
retired. Milestone 6 remains **in progress**, `roadmapComplete=false`.

Fresh full Release CPU validation in `artifacts/m6-entry-window-full/` passes
**5,065 tests with eleven optional skips and zero failures**. The required
inventory discovery is executed separately; its ordinary skip is not successful
coverage. All nine qualified WinUAE presets match their exact directory,
callback and exception-frame counts and current CPU/adapter identities in
`qualified-preset-identities.json`. Their generator/runner pins, original Basic
failures and documented reference caveats remain unchanged; this does not
promote the original Basic selection.

The fresh strict report gate checks **18,048,534 deterministic cases in 624
reporting batches** across all eight profiles. Fresh pinned SingleStepTests
passes **312,500 cases in 125 files**, source
`64b253116a3de04aaac4346c43680960dc9b67e5`. Fresh Musashi passes **536 programs
with 88 explicit exclusions**, source
`72c1d74800f3087b45a0c1a7342601bbed898881`. These independent software audits
retain their original architectural and timing limits.

The complete 040 run in `artifacts/m6-entry-window-audit/` executes 40 tests:
39 pass and the retained inventory test fails. All **3,079,872 executed cases
pass**, with zero mismatches/unsupported execution and **480 untested** cases.
Source, assembly, input selection and independently enumerated combinations
validate before the gate rejects the inventory. All **33** report/identity
controls in `artifacts/m6-entry-window-controls/` detect their specific missing,
shortened, foreign, redistributed or omitted-fixture corruption.

All five new mutations are detected in `artifacts/m6-entry-window-mutations/`:
per route, omitted entry mismatches 196,608 cases, omitted fourth long 49,152,
discarded data 3,072, stale subroutine context 1,536 and stale task context
1,536. The refreshed host-reader mutation in
`artifacts/m6-entry-window-host-mutation/` passes all 12,288 scalar cases and
mismatches all 12,288 batch cases at the denied boundary. Mutations restore
source bytes and rebuild successfully; no mutated binary is used for acceptance.

Verified CPU SHA-256 is
`8b0a29e30ecafa1a0acdd2bb3c9e53e9026c9bff5ed61f899edd90ab2ce01131`;
test/reference adapter is
`9452ec20f94b35e0ef081adb050dca67223e6a261226f548aee72ad270f7c970`.
The manifest identifies starting commit `92922a7` and separately hashes the
working source actually built; its starting committed CPU tree is not relabeled
as the correction's tree.

Isolated CopperScreen baseline
`d9beae8b88be24032221e3482942a249c03c27d3` consumes unpublished private
`1.5.2-synthetic-dev.63`, package SHA-256
`68d5e7f3158c682df451e3c156a4387bbb6a87e0a65c6589ccac2d7c4c260133`.
Release builds with zero warnings/errors; host tests pass 149 with six optional
skips, disk tests pass 74, separate engine diagnostics pass 1,080, and all three
native Workbench/A1200 boot/persistence replays pass without native skips.
Embedded CPU, four package assets and four loaded assemblies match the audited
CPU. Evidence is in the isolated consumer's `artifacts/entry-window-validation/`
and `entry-window-identities.json`. Root CopperScreen changes and its 1.5.1 pin
remain untouched. No package is published and no throughput claim is made.

### 040 user-tail validation and software repair — 2026-10-05

`SyntheticM68040UserRteFaultTests` qualifies the MMU/cache-disabled accurate
factory and one-instruction batching against independently composed manual
expectations. [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
2.2.2.1 defines stack selection; 8.4.2/figure 8-6 install live SR when consuming
throwaways, including USP; 8.1/8.2.1 preserve current SR and enter supervisor
mode for an access exception; 8.2.5 defines privilege faults; 8.4.6.7 preserves
an incompletely validated frame. The previously inspected 1993 scan additionally
provides figure 8-6 at PDF page 243 (one based), printed 8-22, with the same
SHA-256 recorded above. No new reference input or media is committed.

This expectation is an **inference combining those documented rules**. The
manual's usual description of stacking below the incomplete frame assumes a
supervisor validation stack; it does not explicitly show the combined USP fault
path. These tests instead require the general exception's supervisor stack,
chosen by retained M, and preserve the user frame. No hardware measurement or
executed WinUAE oracle for this combination is claimed. The historical WinUAE
source snapshot in `artifacts/m6-rte-address-winuae/` uses live SR after a
throwaway and changes from USP to the M-selected supervisor stack on exception;
that is documentary corroboration only, not an independent execution audit.
Hardware provenance of the combined case stays unqualified and cannot be
inferred from interpreter/batch agreement.

Both user M values are exercised, with each initial supervisor stack and optional
middle throwaway on user/ISP/MSP. The fault saves live intermediate SR and the
original RTE PC in format 7, reports user-data TM=1 and the rejected read's FA/
SIZE, and leaves the unvalidated user SR/PC/frame and memory outside the new
access frame intact. Consumed throwaway memory can be reused by exception
stacking; its committed stack-pointer effects survive and it is not replayed.
Incoming trace is cleared for handler execution. A bare RTE return restores
user mode; the following RTE raises privilege violation on the selected
supervisor stack. Pending CU/CP objects stay pending on this path.

The repair path executes three stores to the original user frame, three to a
fresh supervisor throwaway bridge and one to the access frame's saved SR.
It explicitly sets S and clears incoming trace for the handler return. The
retried supervisor RTE consumes the fresh bridge, selects USP and completes
the repaired frame without rereading any consumed throwaway or fetching its
discarded PC. Repair-store address/width/value/count, exact next PC, preserved
registers/stacks/memory, following traces, CM saved-EA accesses and CT/CU/CP49
delivery are independently checked. FPU arithmetic is not in this scope.

| Group, each scalar/batch | Cases | Combinations |
| --- | ---: | ---: |
| User validation / bare return / privilege | 168,192 | 20,832 |
| Executed repair / bridge / retry / following | 675,328 | 8,224 |

The four ordinary groups total **1,687,040 cases**. Canonical SR-read cases use
all 32 CCRs; structural cases use 0/31 while rejecting each byte of every
validation read. Every admitted frame form is retained: 0/2/3, invalid 4/15,
normal format 7 and CM/CT/CU/CP49. Invalid formats are repaired to format 0.
All three incoming trace states are covered by fault groups; repair explicitly
clears saved incoming T1 and checks all three restored traces and stacks in its
canonical cases. Untouched incoming-trace retry is still required separately.

The first focused run in `artifacts/m6-user-rte-first/` passes all four tests
with no skips, mismatches, unsupported execution or untested phases. The
production CPU source is unchanged. Three maintained `UserRteFault` mutations
in `artifacts/m6-user-rte-mutations/` each execute both complete fault batches:
saved-S corruption and supervisor-data TM mismatch 56,064 fault-entry cases per
route (112,128 dependent phases untested); forced ISP mismatches 28,032 per route,
leaves 56,064 dependent phases untested and passes the 84,096 M-clear phases.
Proof requires the intended SR/SSW diagnostic on each route. Source bytes are
restored and rebuilt before acceptance validation. No old test is retired.

The full 040 command now requires **44 executed tests, 38 reporting batches,
six fixed examples and thirteen input identities**, with independent
combination enumeration for all four new groups. Its preserved 480-case
inventory still fails the complete gate; internal-restoration faults, chained
odd-PC SR provenance, general data/writeback/context-transfer protocols and
all broader reference/consolidation requirements remain open. Selected software
qualification does not replace physical bus sequencing/timing or enabled-MMU/
cache coverage. Milestone 6 stays **in progress**, `roadmapComplete=false`.

Fresh full Release validation in `artifacts/m6-user-rte-full/` passes **5,069
tests with eleven optional skips and zero failures**. All nine qualified WinUAE
presets match the previously pinned exact directory/callback/frame counts and
the current CPU/adapter identities in `qualified-preset-identities.json`.
The strict report gate passes **19,735,574 cases / 628 reporting batches**.
Fresh pinned SingleStepTests passes **312,500 cases / 125 files**; fresh Musashi
passes **536 programs with 88 explicit exclusions**. Their source pins and
software-reference limits remain unchanged. Both fresh reference tests execute
without skips; they are separate from the optional skips in the full run.

The complete 040 audit in `artifacts/m6-user-rte-audit/` executes 44 tests:
43 pass and the required inventory fails. All **4,766,912 executable cases
pass**, with zero mismatches/unsupported execution; **480 remain untested**.
All source/input/binary and independent combination checks validate before the
inventory rejection. All **17** new malformed-report/identity controls in
`artifacts/m6-user-rte-controls/` reject their specific missing, shortened,
foreign, redistributed or omitted-fixture corruption. A generic inventory
failure is never accepted as proof that a negative control worked.

CPU assembly SHA-256 is
`dccd468b1a26090501e310acc6cafd1ef3cc3fb6b3fb63fc48f0f614869f889e`;
test/reference adapter is
`ae19b94ecd01f7c4ce908e1779f511ef411371e8c8f6e4a4e963bf97d85cab86`.
The manifest records starting commit `dc27536`, the committed CPU tree and
separate current source/fixture hashes. Production CPU source has no changes
from that commit. This rebuild's informational version includes `dc27536`;
its binary identity is not relabeled as the preceding checkpoint's binary.
No new consumer validation or private/public package is generated for this
test-only checkpoint; preceding unpublished `.63` evidence remains scoped to
its own unchanged package and binary. Root CopperScreen work is untouched.

## 040 preserved-trace RTE repair — 2026-10-05

This test-only checkpoint extends executed supervisor-tail repair without
changing production CPU code. Expectations follow
[MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf) sections
8.2.6, 8.4.2 and 8.4.6.7: an access fault defers tracing until the suspended
instruction completes, incoming trace controls that completion, RTE is a T0
flow change, and the trace frame saves the resulting SR and next PC. Synthetic
repair/continuation frames provide manual-derived software qualification;
there is no executed hardware reference for these combined scenarios.

The real handler performs exactly three original-frame SR/PC/format stores.
It leaves the access frame's saved SR untouched. Its RTE restores the original
incoming trace state; the retried original RTE then completes the repaired
frame. T1/T0 produce a trace with the repaired SR/PC even when the repaired
SR clears trace. The trace-handler RTE returns to the target, whose following
BRA or MOVEM verifies restored trace, stacks and CM continuation delivery.
CM must retain its saved operand address across the intervening trace-handler
RTE, instead of consuming it there or using the live address register.

| Group, each scalar/batch | Cases | Combinations |
| --- | ---: | ---: |
| Canonical CCR boundaries | 92,736 | 378 |
| Chained validation read bytes | 1,271,808 | 82,944 |

The four groups total **2,729,088 phases**. Canonical direct SR-read faults
cover all 32 CCRs. Structural cases use CCR 0/31 and reject every byte of each
SR/PC/format/SSW/continuation-address validation read, with both alignments,
both VBRs and twelve one/two-throwaway supervisor paths. Each group crosses
incoming 0/T1/T0, restored 0/T1/T0 and restored user/ISP/MSP. Forms are 0/2/3,
invalid 4/15 repaired to 0 and normal/CM format 7. Consumed throwaway PCs are
never fetched; committed stack effects are preserved and retry cannot reread
their frames. Both public scalar and one-instruction batch execution verify
architectural state and unchanged-memory guards; batch count/callbacks are
also checked.

The initial focused run in `artifacts/m6-rte-retry-trace-first/` executes all
four tests with zero skips, mismatches, unsupported execution or untested
phases. The earlier explicit-trace-clear repair groups keep their original
case identifiers/cardinalities and separate mutation selection.

Pending CT/CU/CP interactions, the user-tail trace bridge and mixed trace
values across consumed throwaways remain required separately. This checkpoint
uses the same incoming trace value throughout each throwaway chain. Internal
restoration, chained odd-PC saved-SR provenance, general data/writeback/context
transfer and all broader model/reference/consolidation requirements remain
open. The 480-case remaining-protocol inventory continues to fail the complete
gate. Physical pipeline/cache timing, enabled MMU, FPU arithmetic and OS
compatibility remain outside this roadmap. No old regression is retired,
public API changed or package released; milestone 6 remains **in progress**
with `roadmapComplete=false`.

The three maintained `RteRetryTrace` mutations in
`artifacts/m6-rte-retry-trace-mutations/` each execute all four complete reports
and require their intended phase and architectural diagnostic on both routes.
Counts below are per route, with boundary/chained values respectively:

| Mutation | Mismatching | Dependent phases untested | Required diagnostic |
| --- | ---: | ---: | --- |
| Require restored trace bits | 2,688 / 36,864 | 5,376 / 73,728 | Incoming T1 with restored trace clear, retry PC |
| Omit RTE from T0 flow changes | 4,032 / 55,296 | 8,064 / 110,592 | Incoming T0 with restored trace clear, retry PC |
| Consume CM in trace-handler RTE | 1,152 / 24,192 | 0 / 0 | CM following instruction, D0 from saved EA |

Mutations are restored byte-for-byte and the original source rebuilt before
acceptance. These failures prove the new checks discriminate the target defects;
they are not relabeled as production mismatches.

The refreshed legacy `RteRepair` campaign in
`artifacts/m6-rte-retry-trace-legacy-mutations/` still executes exactly its two
original reports (143,424 and 768,960 cases). All three retry-PC, saved MOVEM EA
and pending saved-SR mutations are detected at the required retry/following
phase. Its explicit method filter preserves that selection after adding the
four new facts to the shared fixture class.

Acceptance validation finished on 2026-10-06. Fresh full Release execution in
`artifacts/m6-rte-retry-trace-full/` passes **5,073 tests, zero failures and
eleven optional skips**. All nine qualified WinUAE presets match their pinned
directory/callback/frame counts and current CPU/adapter identities, recorded
in `qualified-preset-identities.json`. Optional unavailable coverage remains
separate from these executed reference selections.

The dedicated `artifacts/m6-rte-retry-trace-audit/` run executes **48 tests:
47 passing and the required inventory failing**, with no skips. All
**7,496,000 executable cases pass**; there are zero mismatches/unsupported
cases and **480 untested requirements**. All 42 reports, six fixed examples,
thirteen fixture/command identities and independently enumerated combinations
validate before the inventory rejection. All **17** malformed-report/identity
controls in `artifacts/m6-rte-retry-trace-controls/` reject their specific
missing, shortened, foreign, redistributed or omitted-fixture corruption;
the generic inventory failure cannot satisfy a negative control.

CPU assembly SHA-256 is
`21a9f11e4bdcad46880db2643a04e6997e2adf172e6d3edbc799b68b18956dd2`;
test/reference adapter is
`e800f3f49757dbc39bb3b972d63f11eb92a1b0839a9d0d04e5ee4fbde59b4445`.
The audit manifest records starting commit `32761cc`, its committed CPU tree
and actual current source/fixture hashes. Production source is unchanged from
that commit; the newly built binary is recorded under its own identity.
No consumer replay or private/public package is generated for this test-only
checkpoint. Prior unpublished `.63` consumer evidence remains scoped to its
own package and binary. Unrelated root CopperScreen changes remain untouched.

The final strict gate in `artifacts/m6-rte-retry-trace-strict.log` passes
**22,464,662 logical cases / 632 reporting batches**. Fresh pinned
SingleStepTests passes **312,500 cases / 125 files** and Musashi passes
**536 programs with 88 explicit exclusions**. Both reference tests execute
without skips; their source pins and software-reference limits remain
unchanged. The full-run optional skips are not credited as reference coverage.
The ordinary summary retains `roadmapComplete=false`.

## 040 pending delivery with preserved trace — 2026-10-06

This checkpoint extends the real three-store supervisor-tail repair fixture
to CT/CU/CP49 with the access frame's saved SR untouched. Expectations use
[MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf) 8.3,
8.4.6.2 and 8.4.6.7: RTE immediately delivers the indicated pending exception;
CU/CP take precedence over tracing, leaving software to inspect saved trace
bits. CT already delivers its pending trace. An extra automatic trace of the
converting RTE must not obscure that frame. These are manual-derived synthetic
contexts, not observed hardware faults or qualification of FPU arithmetic.

The handler changes the original frame's SR/PC/format with exactly three real
MOVE stores. Its return restores incoming 0/T1/T0. The retried original RTE
converts the repaired pending frame, preserving repaired SR/PC/EA and using
format 2 for CT/CU or format 3 for CP49. Its handler sees the original pending
vector and CU/CP context consumption. Incoming trace cannot cause a second
automatic exception at this boundary. A bare pending-handler RTE then returns;
the following self-BRA traces according to the repaired saved SR.

| Group, each scalar/batch | Cases | Combinations |
| --- | ---: | ---: |
| Canonical CCR boundaries | 41,472 | 162 |
| Chained validation read bytes | 870,912 | 54,432 |

All four reports total **1,824,768 phases**. The canonical SR-read case covers
all 32 CCRs. Structural cases use CCR 0/31, every rejected validation-read byte,
twelve supervisor paths, both alignments/VBRs and all incoming/restored trace
and restored user/ISP/MSP combinations. No consumed throwaway may be replayed
or its discarded PC fetched. Exact repair writes, preserved architectural
state/memory and public batch counts/callbacks remain checked. The initial
focused run in `artifacts/m6-pending-trace-first/` passes all four tests with
zero skips, mismatches, unsupported execution or untested phases.

The pending handler here deliberately performs a bare RTE. Its required
software service of the original trace is not claimed as executed coverage;
the saved trace bits needed by that service are checked independently. Other
CP vectors retain their earlier distinct continuation coverage but are not
qualified by this new preserved-trace repair matrix. User-tail trace bridges,
mixed-epoch trace provenance, internal restoration, chained odd-PC SR,
general data/writeback/context-transfer and all earlier model/reference/
consolidation requirements remain open. The 480-case inventory continues to
fail the complete gate. Enabled MMU, FPU arithmetic, physical pipeline/cache
timing and OS compatibility remain outside this roadmap. No regression is
retired, API changed or package released; milestone 6 remains **in progress**
with `roadmapComplete=false`.

Three maintained `RtePendingTrace` mutations in
`artifacts/m6-pending-trace-mutations/` each execute all four complete reports.
The proof requires the intended retry/following phase and architectural
diagnostic in both routes and both matrices. Counts below are per route,
with canonical/chained values respectively:

| Mutation | Mismatching | Dependent phases untested | Required diagnostic |
| --- | ---: | ---: | --- |
| Extra automatic RTE trace | 3,456 / 72,576 | 6,912 / 145,152 | CT retry, extra frame changes A7 |
| Stack incoming instead of repaired SR | 5,184 / 96,768 | 10,368 / 193,536 | CT retry, saved-frame memory |
| Suppress following self-BRA trace | 3,456 / 72,576 | 0 / 0 | Incoming/restored T1, following PC |

Production source bytes are restored and rebuilt before acceptance. The new
pending scope shares the validator with the existing normal/CM trace scope;
each retains its own four-report cardinalities and intended proof identifiers.
No historical failing mutation result is presented as a production mismatch.

The dedicated run in `artifacts/m6-pending-trace-audit/` executes **52 tests:
51 pass and the required inventory fails**, with no skips. All **9,320,768
executable cases pass**, with zero mismatches/unsupported execution and **480
untested requirements**. All 46 reports, six fixed examples, thirteen input
identities and independently enumerated combinations validate before the
inventory rejection. This gate remains failed; the new software qualification
does not complete milestone 6.

The manifest records starting commit `b884aee`, committed CPU tree
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1` and current source/fixture hashes.
CPU assembly SHA-256 is
`82bc2ed7607ab5c99006a65dc5f32a310730b8abd458a0acf7189c8dc6451e5d`;
test/reference adapter is
`2b72c0dc66b33c3f79e26aceebf384b2e40f7c3ea81be496dce183df69de34ce`.
Production source is unchanged from that commit; this newly built binary has
its own recorded identity. No new consumer replay or private/public package
is generated for this test-only checkpoint. Prior unpublished `.63` consumer
evidence remains scoped to its own package/binary; root CopperScreen work is
untouched.

Fresh full Release validation in `artifacts/m6-pending-trace-full/` passes
**5,077 tests with zero failures and eleven optional skips**. All nine
qualified WinUAE presets match the pinned exact selections and current
CPU/adapter identities in `qualified-preset-identities.json`. Their executed
coverage is separate from optional unavailable tests. Historical Basic
mismatches/unsupported groups retain their original status.

The strict ordinary gate in `artifacts/m6-pending-trace-strict.log` passes
**24,289,430 logical cases / 636 batches**. Fresh pinned SingleStepTests passes
**312,500 cases / 125 files** and Musashi passes **536 programs with 88 explicit
exclusions**. Both fresh reference tests execute without skips; their pinned
sources and software-reference caveats remain unchanged. All **17** controls
in `artifacts/m6-pending-trace-controls/` reject their specific missing,
shortened, foreign, redistributed or omitted-fixture corruption. The ordinary
summary retains `roadmapComplete=false`.

The current result selector specifies user mode with M=0, ISP with M=0 and
MSP with M=1. Restored user mode with M=1 is **not covered** by this matrix;
its subsequent exception-bank selection needs separate qualification. This
is distinct from the already qualified initial user-tail fault M values and
from a user-tail trace bridge. It remains a required stack-state combination,
along with the other named unqualified protocols above.

### 040 restored user M=1 — 2026-10-06

`SyntheticM68040RteRepairTests` now includes a separate restored user S=0,M=1
matrix through accurate scalar and one-instruction batch execution. MC68040UM
2.2.2.1 makes USP active in user mode regardless of M; 8.1 forces S and clears
trace for a synchronous exception, preserving M and selecting MSP. The shared
independent exception expectation must therefore use M alone to select that
future supervisor bank. Existing user M=0 / ISP / MSP reports retain their
original case identities and cardinalities. No production CPU correction was
required by the new focused run.

The real handler repairs SR/PC/format with exactly three MOVE stores while
preserving the access frame's saved incoming trace. The original RTE restores
S=0,M=1, then normal/CM completion trace or CT/CU/CP49 pending conversion must use
MSP. Handler return restores USP without clearing M, and the following BRA or
CM MOVEM obeys the restored trace. Every phase checks architectural registers,
all three stack pointers, saved SR/PC/EA, exception sequence, format/vector,
pending context consumption, guarded memory and batch counts/callbacks. CM
still uses the saved operand address across intervening trace handlers.

| Group, each scalar/batch | Cases | Combinations |
| --- | ---: | ---: |
| Canonical CCR boundaries | 44,736 | 180 |
| Chained validation-read bytes | 714,240 | 45,792 |

The four reports total **1,517,952 checked phases**. Canonical cases cover all
32 CCRs. Chained cases use CCR 0/31 and every byte of each documented validation
read, twelve supervisor throwaway paths, both alignments/VBRs and all incoming/
restored 0/T1/T0. Forms are 0/2/3, invalid 4/15 repaired to 0, normal/CM and
CT/CU/CP49. The initial run at `artifacts/m6-user-master-focused/` passes all four
tests without skips, mismatches, unsupported execution or untested phases.

The complete 040 command requires 56 executed tests, 50 reports, six fixed
examples and thirteen input identities. Its independent iterator checks every
new path/result/trace/form/read/fault-byte combination and expected cardinality.
The ordinary strict command also requires the four new reports. Maintained
mutation scope `RteUserMaster` requires the intended semantic diagnostic from
all four reports for incorrect user stack selection, pending M clearing and
trace M clearing. It restores exact production source bytes before rebuilding.

These are manually derived software expectations, composing the documented
stack-selection and exception-entry rules with RTE continuation rules; they
are not observed hardware faults or independent FPU arithmetic qualification.
The pending handler performs a bare RTE rather than the remaining software
trace-service protocol. This closes the restored user M=1 combination in these
supervisor-tail repair programs, separately from initial user-tail M and
user-tail trace bridges. Other CP vectors, software trace service, mixed-epoch
provenance, internal restoration, chained odd-PC SR, general data/writeback/
context-transfer and the broader selected-model/reference/consolidation work
remain required. The 480-case inventory still fails completion. Milestone 6
remains **in progress**, `roadmapComplete=false`. Enabled MMU, FPU arithmetic,
physical pipeline/cache timing and OS compatibility remain outside the roadmap.
No regression is retired, public API changed or package published.

The maintained `RteUserMaster` proof in `artifacts/m6-user-master-mutations-v2/`
detects all three mutations. Each executes all four complete reports and
requires the intended retry/following phase and diagnostic in both routes and
both matrices. Counts below are per route, canonical/chained respectively:

| Mutation | Mismatching | Dependent phases untested | Required diagnostic |
| --- | ---: | ---: | --- |
| Select MSP during user execution | 5,760 / 91,584 | 10,176 / 164,736 | Format-0 retry, A7 differs |
| Clear M during pending conversion | 1,728 / 36,288 | 3,456 / 72,576 | CT retry, SR loses M |
| Clear M during trace entry | 4,672 / 72,000 | 5,376 / 73,728 | Format-0 following instruction, SR loses M |

The first proof attempt expected an A7 diagnostic for pending M clearing, but
the common verifier reports the SR mismatch first. The proof checker now
requires that precise missing-M SR diagnostic; all three mutations were rerun
in a fresh directory. The initial failed proof attempt remains at
`artifacts/m6-user-master-mutations/`; it is not acceptance evidence. Exact
production source bytes match the preceding checkpoint after restoration, and
the restored Release rebuild succeeds with zero warnings/errors.

The first complete run executes all 56 tests: 55 pass and the required inventory
fails. Its report validator then rejects `rte-user-master-boundaries-scalar`
because the older `rte-user-` prefix selector also matched the new group. The
selector now names `rte-user-fault-` and `rte-user-repair-` explicitly, preserving
both iterators' independent scopes. The original run remains at
`artifacts/m6-user-master-audit/`; acceptance uses a fresh complete run rather
than rewriting that run's command identity or results.

Fresh full Release validation in `artifacts/m6-user-master-full/` passes **5,081
tests with zero failures and eleven optional skips**. All nine qualified WinUAE
presets match their exact selections and current CPU/adapter identities in
`qualified-preset-identities.json`. CPU assembly SHA-256 is
`9765973d30625e2e15c1f76159391fe8a1c7c39c7b0cce1a80187748f382700e`;
test/reference adapter is
`70a3f5a04b5e47a872cfdc12036747bf30ca95b8829e65865338b43d216be4b8`.
The build starts from commit `b59300c`; production CPU source is unchanged,
while the tested fixture source has its own recorded hashes. These binaries
are not relabeled as built from the subsequent checkpoint commit.

No new consumer validation or private/public package is produced for this
test-only checkpoint. Prior unpublished `.63` evidence retains its own
package/binary scope. Root CopperScreen changes and the pinned dependency
boundary are untouched. Original Basic reference mismatches/unsupported groups
remain historical failures, separate from the qualified selections above.

The strict ordinary gate in `artifacts/m6-user-master-strict.log` passes
**25,807,382 logical cases / 640 batches**. Fresh pinned SingleStepTests passes
**312,500 cases / 125 files**, and Musashi passes **536 programs with 88 explicit
exclusions**. Both reference tests execute once without skips, with fixture
identities and source pins checked by the maintained command. These are scoped
software references rather than hardware qualification. The ordinary summary
retains `roadmapComplete=false` and the new restored-user scope separately.

The fresh corrected run in `artifacts/m6-user-master-audit-v2/` executes **56
tests: 55 pass and the required inventory fails**, with no skips. All
**10,838,720 executable phases pass**, with zero mismatches or unsupported
execution and **480 untested requirements**. The verifier validates all 50
reports, six fixed examples, thirteen fixture/command identities and every
independently enumerated combination before rejecting the inventory. Logical
coverage totals 10,839,200 including the required untested rows. The manifest
records starting commit `b59300c`, CPU tree
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1` and current input/source/binary hashes.
This gate remains failed and does not establish milestone-6 completion.

All **17** controls in `artifacts/m6-user-master-controls/` reject their intended
missing, shortened, foreign, redistributed or omitted-fixture corruption with
the specific expected diagnostic, rather than the generic inventory rejection.
Both scalar/batch canonical and chained restored-user reports are covered. All
current fixture/source/binary identities still match after reference audits.
Production CPU source remains unchanged; milestone 6 remains **in progress**.

### 040 preserved-trace CP vectors 50–55 — 2026-10-06

`SyntheticM68040RteRepairTests` adds four `OtherCpVectorsPreserveTrace` reports
for the remaining original CP vectors 50–55 after a physical validation fault
and real three-store software repair. Existing CP49, CT/CU, normal/CM and restored
user-M matrices retain their own report identities and cardinalities. Expectations
compose MC68040UM 8.3, 8.4.6.2 and 8.4.6.7 with 9.6 table 9-9. Section 9.6.2
explicitly permits vector 55 as a post-instruction exception for opclass 011
unsupported data types. BSUN48 is outside this CP selection. These are synthetic
suspended contexts, not FPU arithmetic or observed hardware faults.

The pending vector is an input selected before suspension. Handler-visible
FPCR=0, FPSR=08008198 and FPIAR=1234ABCD deliberately suggest another event;
RTE must preserve them and deliver the original vector. After three executed
stores repair SR/PC/format without changing the access frame's SR, handler return
restores incoming 0/T1/T0. The original RTE converts to format 3, with repaired
saved SR/PC/EA and no extra automatic RTE trace. Pending consumption, exception
sequence, all three stack pointers, guarded memory and batch counts/callbacks
are checked. A bare pending-handler RTE returns, and the following self-BRA
obeys the repaired trace bits.

| Group, each scalar/batch | Phases | Combinations |
| --- | ---: | ---: |
| Canonical CCR boundaries | 110,592 | 432 |
| Chained validation-read bytes | 2,322,432 | 145,152 |

All four reports total **4,866,048 checked phases**. Canonical cases cover all
32 CCRs. Structural cases reject every documented validation-read byte, use
CCR 0/31 and all twelve supervisor throwaway paths, both alignments/VBRs and
all incoming/restored traces. All four restored states (user M=0/M=1, ISP/MSP)
and all six vectors have separate identifiers. Consumed throwaways cannot be
replayed or their odd discarded PCs fetched. The initial focused run at
`artifacts/m6-cp-vectors-focused/` passes all four tests, with zero skips,
mismatches, unsupported execution or untested phases.

The maintained `RteCpVectors` mutation scope requires the intended wrong-vector,
extra-RTE-trace or unconsumed-context diagnostic in each complete route/matrix.
The complete 040 command independently enumerates every CP50–55 combination;
it requires 60 tests, 54 reports, six fixed examples and thirteen input
identities. Ordinary reporting also requires all four groups, with their scope
recorded separately from the earlier CP49 matrices.

Pending software trace service remains unqualified: the pending handler here
performs a bare RTE. User-tail trace bridges, mixed-epoch provenance, internal
restoration, chained odd-PC SR, general data/writeback/context transfer and the
broader model/reference/consolidation work remain required. The 480-case
inventory still fails completion. Milestone 6 remains **in progress**,
`roadmapComplete=false`. Enabled MMU, FPU arithmetic, physical pipeline/cache
timing and OS compatibility remain outside the roadmap. No old regression is
retired, public API changed or package published.

The maintained `RteCpVectors` run in `artifacts/m6-cp-vectors-mutations/` detects
all three mutations. Each executes all four complete reports and requires the
intended retry-RTE diagnostic in both routes and matrices. Counts below are per
route, canonical/chained respectively:

| Mutation | Mismatching | Dependent phases untested | Required diagnostic |
| --- | ---: | ---: | --- |
| Force original vector to 49 | 13,824 / 290,304 | 27,648 / 580,608 | CP50 retry delivers 49 |
| Add automatic RTE trace after CP | 9,216 / 193,536 | 18,432 / 387,072 | CP50 retry delivers 9 |
| Leave pending context unconsumed | 13,824 / 290,304 | 27,648 / 580,608 | CP50 retry retains pending delivery |

Exact production source bytes match the preceding checkpoint after restoration;
the restored Release rebuild succeeds with zero warnings/errors. Deliberate
mutation failures and their dependent untested phases are proof evidence, not
production mismatches. The shared proof validator retains each earlier trace/
restored-user scope's separate four-report cardinalities and identifiers.

The complete current 040 run in `artifacts/m6-cp-vectors-audit/` executes all
**60 tests: 59 pass, one required inventory failure, zero skips**. All 54 reports,
six fixed examples and thirteen fixture/command identities are present. The
independent combination verifier checks **15,704,768 passing phases**, zero
mismatches/unsupported execution and the retained **480 untested requirements**
(15,705,248 total). The command's final rejection is exactly that inventory;
it is not a passing completion gate.

The fresh full Release run in `artifacts/m6-cp-vectors-full/` passes **5,085 tests**
with eleven optional skips and zero failures. All nine qualified WinUAE presets
match their exact directory/callback/frame selections and CPU/adapter identities.
The acceptance build starts from `e26a8cc3cfd3caf27e0e525bf0eba9f239134e59` plus
the recorded fixture changes, with unchanged committed CPU tree
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`. CPU SHA-256 is
`614599a2840b2fa5860e27038873259cf74fea625144eebdd6a9a1a0a294b1d1`;
test/adapter SHA-256 is
`1a42b10c1bb128323b8254e9e655eadd4b73cb18202a2fe4550797c8939168ba`.
These identities describe the validation build, not a rebuild of the subsequent
checkpoint commit. The complete manifest's source, input and binary hashes all
match current bytes.

Strict ordinary validation checks **30,673,430 logical cases in 644 batches**.
Fresh pinned SingleStepTests passes **312,500 cases in 125 files**; Musashi
passes **536 programs with 88 explicit exclusions** across all eight profiles.
Both requested adapters execute exactly one passing test with zero skips, and
all current acceptance binary/source/fixture identities still match afterward.
The original Basic reference failures remain separate historical evidence.

All **17 fresh controls** in `artifacts/m6-cp-vectors-controls/` reject their
specific missing, shortened, foreign, redistributed or omitted-fixture
corruption. Both canonical/chained scalar/batch CP-vector reports are covered;
the controls require the intended diagnostic rather than accepting the generic
inventory rejection. Current source, fixture and assembly identities still
match after all checks. No production CPU fix, consumer rerun, package release
or old-test retirement is needed for this test-only addition. Milestone 6
remains **in progress**.

### 040 executed pending software trace service — 2026-10-06

The four `SoftwareTraceService` reports extend the real validation-fault and
three-store repair fixture with executed integer CU/CP trace-service programs.
Expectations compose MC68040UM 8.2.6, 8.3 and 8.4.6.2/7: the pending condition
has priority, and software must inspect the saved trace condition and adjust
the frame before directly calling the trace handler. These are software-handler
protocol tests with supplied instruction-completion metadata, not an FPU
arithmetic emulator or observed hardware fault.

CU cases supply linear or taken-flow completion, adjusting saved PC to target+2
or target+4. CP cases supply a completed two-word FMOVE starting at target−4,
with its next PC already saved. T1 requests trace service; T0 requests it only
for taken-flow CU, since post-instruction CP is FMOVE-to-memory. Live handler
trace bits are clear, so the executed BTSTs must inspect the stacked SR byte.
The handler converts the format/vector word to $2024, supplies the original
instruction address, reads vector 9 through VBR, pushes the target and uses RTS
for a direct software call. This temporary call target is not an exception
frame. Every step preserves all registers and checks all three stack banks.

The trace handler writes one marker, optionally clears stacked trace bits and
executes RTE. The software path does not change the hardware exception counter,
vector or saved-provenance registers. A following self-BRA checks hardware
tracing from the returned SR separately. Canonical cases cross all 32 CCRs,
incoming/restored 0/T1/T0, all four restored banks and CU/CP49–55. Structural
cases use CCR 0/31, incoming T1, every validation-read byte, all twelve supervisor
paths, both alignments/VBRs and all restored traces/vectors/service policies.

| Group, each scalar/batch | Phases | Combinations |
| --- | ---: | ---: |
| Canonical service programs | 514,560 | 1,296 |
| Chained service programs | 3,601,920 | 145,152 |

The maintained `RteSoftwareTrace` scope requires intended saved-SR-bit-test,
short-format-2-return and original-vector diagnostics in all four reports.
Golden fixed program words audit instruction encodings separately. The complete
040 selection requires 65 tests, 58 reports, seven fixed examples and fourteen
fixture/command identities. User-tail trace bridges, mixed-epoch provenance,
internal restoration and the other retained model/reference/consolidation gaps
remain required. The 480-case inventory is retained and still prevents milestone
completion. No production CPU fix, public API change, package release or old-test
retirement is included at this implementation stage.

All three maintained mutations in `artifacts/m6-software-trace-mutations/` are
detected with the intended diagnostic in all four complete reports. Counts below
are per route, canonical/chained respectively; unsupported execution is zero:

| Mutation | Mismatching | Dependent phases untested | Required diagnostic |
| --- | ---: | ---: | --- |
| Invert the saved-SR BTST result | 41,472 / 290,304 | 215,040 / 1,505,280 | CU-linear test-T1 SR mismatch |
| Pop only eight bytes for format 2 | 19,968 / 139,776 | 19,968 / 139,776 | CU-linear return has wrong inactive stack pointer |
| Replace original CP vector with 49 | 27,648 / 193,536 | 161,280 / 1,128,960 | CP50 retry delivers 49 |

The script restores exact production bytes and rebuilds successfully with zero
warnings/errors. Every production source hash matches the preceding CP-vector
manifest. Deliberate mutation failures and dependent untested phases are proof
evidence, not production gaps. The restored acceptance build starts from
`635ecde475695a1744cde826c8ad5bc20c2ea497` plus the recorded fixture changes:
CPU SHA-256 is
`60748e5e34c12f541b1dcaa16928459c58211b041f33bffe029e38ed15d39139`;
test/adapter SHA-256 is
`5d4a79638b0b266e82234c305dbeef28c449b8a3c0bd256ac06542ed224d5384`.
The complete 040 run in `artifacts/m6-software-trace-audit/` executes all
**65 tests: 64 pass, one required inventory failure, zero skips**. All 58 reports,
seven fixed examples and fourteen input identities are present. Independent
combination enumeration verifies **23,937,728 passing phases**, zero mismatches
or unsupported execution and exactly **480 untested requirements** (23,938,208
total). The command rejects precisely that inventory after all selection and
identity checks; it is not a passing completion gate. All current source,
fixture and binary hashes match the acceptance manifest.

The fresh full Release selection in `artifacts/m6-software-trace-full/` passes
**5,090 tests**, with eleven optional skips and zero failures. All nine qualified
WinUAE presets match their exact directory/callback/frame selections and the
CPU/adapter identities above. Strict reporting verifies **38,906,390 logical
cases in 648 batches** across all eight profiles. Fresh pinned SingleStepTests
passes **312,500 cases in 125 files**; Musashi passes **536 programs with 88
explicit exclusions**. Each requested adapter executes one passing test with
zero skips. All current acceptance source/input/binary identities match after
those references. All **17 fresh controls** in
`artifacts/m6-software-trace-controls/` reject their specific missing, shortened,
foreign, redistributed or omitted-fixture corruption. All four software-trace
reports and the new fixture identity are covered; each control requires its
intended diagnostic rather than accepting the generic inventory rejection.
Current source, fixture and assembly identities still match after all checks.
This qualifies the test-only software-trace checkpoint; no production CPU fix,
consumer rerun, package release or old-test retirement is included. Milestone 6
remains **in progress** with the full remaining scope retained.

### 040 user-tail bridge with preserved trace — 2026-10-06

The four `UserTailBridgePreservesTrace` groups compose MC68040UM 2.2.2.1,
8.1/8.2.1/8.2.6, 8.3, 8.4.2 and 8.4.6.7. The existing fault fixture consumes
throwaways into USP, rejects one validation read and enters the M-selected
supervisor stack. Seven real repair stores patch the user frame, build a new
throwaway bridge and add S to the access return. The new groups retain incoming
T1/T0 in both the bridge and access return instead of clearing them. The live
fault handler remains untraced.

The handler's RTE returns to supervisor code with the original trace condition;
the retried RTE consumes the new bridge and completes the repaired user frame.
Normal/CM completion traces even if repaired SR clears trace. CT/CU/CP49–55
instead deliver the pending frame with no extra automatic RTE trace. The
trace/pending handler return and following BRA/MOVEM independently verify the
restored trace condition, all stack banks, saved SR/PC/address, exception
provenance, original pending consumption and CM continuation lifetime.

This is an inference combining documented rules for a synthetic software
repair program. It is not a hardware measurement or independently executed
WinUAE oracle for the combined user-tail path. Physical timing, enabled MMU,
FPU arithmetic and opaque internal restoration are not qualified here.

| Group, each scalar/batch | Phases | Combinations |
| --- | ---: | ---: |
| Canonical preserved-trace bridge | 873,984 | 2,304 |
| Structural validation read bytes | 3,502,080 | 145,920 |

Canonical cases use all 32 CCRs and incoming 0/T1/T0 with alignment zero and
VBR $10000. Structural cases use CCR 0/31 and incoming T1, reject each byte of
every validation read, and cross both alignments/VBRs and all initial/middle
paths. All groups retain both original user M values, every restored
user/user-M/ISP/MSP state, every restored trace condition, formats 0/2/3,
invalid 4/15 repaired to 0 and normal/CM/CT/CU/CP49–55. These four ordinary
groups add 8,752,128 phases. No production CPU or public API change is included.

`artifacts/m6-user-trace-first/` and `artifacts/m6-user-trace-structure/` pass
all four focused reports with no skips, mismatches, unsupported execution or
untested phases. All three maintained `UserRteTrace` mutations now detect their
intended diagnostic in every complete report. Complete 040 acceptance executes
69 tests (62 reports, seven fixed examples) with all fourteen input identities.
It reports 32,689,856 passing phases, zero mismatches/unsupported execution and
480 untested requirements. Its sole failing test rejects that incomplete
inventory; all 68 other tests pass without skips. Mixed-epoch
trace provenance, user-tail software trace-service programs and every other
retained model/reference/consolidation requirement remain open; milestone 6
stays **in progress**.

The first maintained `UserRteTrace` mutation, `040-user-trace-restored-bits`,
executes all four complete groups with zero skips or unsupported execution.
Each canonical route detects 7,168 mismatches, leaves 14,336 dependent phases
untested and passes 852,480 phases. Each structural route detects 32,768,
leaves 65,536 dependent phases untested and passes 3,403,776. Every report
contains the intended retry-RTE PC diagnostic with incoming T1 and repaired
trace clear. This proves that restored trace bits cannot replace the incoming
instruction's tracing decision. These deliberate mutation failures are proof
evidence, not production failures.

The original-CP-vector mutation also executes all four complete groups without
skips or unsupported execution. Each canonical route detects 27,648 mismatches,
leaves 55,296 dependent phases untested and passes 791,040 phases. Each
structural route detects 129,024, leaves 258,048 dependent phases untested and
passes 3,115,008. All reports include CP50 delivered as vector 49 at retry-RTE;
the other-vector cases retain their full cardinalities.

The MOVEM handler-consumption mutation completes all four reports with zero
skips, unsupported execution or dependent untested phases. Each canonical route
detects 3,072 mismatches and passes 870,912 phases; each structural route detects
21,504 and passes 3,480,576. The intended following-instruction D0 diagnostic
shows that the saved EA must survive the trace-handler RTE until MOVEM resumes
at its recorded PC. No old regression is retired by these proofs.

The script restores production sources byte-for-byte and rebuilds with zero
warnings/errors. Every current production source hash matches the preceding
software-trace manifest; the committed CPU tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`. The restored acceptance build starts
from `e52243255c7dd0d1f608954399915810eaeafd78` plus the recorded fixture changes.
CPU SHA-256 is
`51e5454dddc852170e8d44d33ee10834d92f2533c00170892b0356dfd2673963`;
test/adapter SHA-256 is
`4eacd6fe96c9918e481bb764dd97ff59eb615880ae9fff6875fca295dbd91902`.
The fresh full Release CPU selection passes 5,094 tests with eleven optional
skips and zero failures. All nine qualified WinUAE presets match exact
selections and current CPU/adapter identities. Strict ordinary reporting
verifies 47,658,518 logical cases in 652 batches across all eight profiles,
with `roadmapComplete=false`. Fresh pinned SingleStepTests passes 312,500 cases
in 125 files; Musashi passes 536 programs with 88 explicit exclusions. Both
requested adapter tests execute once and pass without skips. Source, fixture
and assembly identities still match after the reference audits.

All seventeen fresh report/fixture integrity controls reject their specific
missing, shortened, foreign, redistributed or omitted-fixture corruption.
Every canonical/structural scalar/batch report is covered; generic inventory
rejection alone cannot satisfy a control. Current source, fixture and assembly
identities still match after all checks. This test-only checkpoint is qualified;
milestone 6 remains **in progress** with all required remaining scope retained.
No CPU production fix, consumer rerun, package release or old-test retirement
is included.

### 040 user-tail executed software trace service — 2026-10-06

The new four `UserTailSoftwareTraceService` groups reuse the fixed-reference
integer program from supervisor-tail qualification after the seven-store
user-tail repair bridge. Expectations compose MC68040UM 2.2.2.1, 8.1/8.2.1,
8.2.6, 8.3 and 8.4.2/8.4.6.7. Pending CU/CP suppresses automatic RTE tracing;
the software inspects repaired saved T1/T0, adjusts the supplied CU completion
PC, converts the frame and fetches/calls vector 9 through a real stack/RTS
sequence. Its marker is written exactly once when eligible. Trace-retaining
and trace-clearing returns execute real RTE, followed by BRA to distinguish
software service from subsequent hardware tracing.

The fixture verifies registers, all three stack banks, guarded memory,
exception sequence and saved PC/SR at each instruction. Original pending CP
vectors 49–55 must survive deliberately conflicting FPCR/FPSR/FPIAR values.
The temporary direct-call stack word is not an exception frame. No FPU opcode
or arithmetic is executed, and no combined user-tail hardware oracle is claimed.

| Group, each scalar/batch | Phases | Combinations |
| --- | ---: | ---: |
| Canonical user-tail software service | 1,360,896 | 2,592 |
| Structural validation read bytes | 6,350,848 | 193,536 |

Canonical cases cross every CCR and incoming 0/T1/T0. Structural cases use
CCR 0/31 and incoming T1, every validation-read byte, both alignments/VBRs and
all eight initial/middle paths. Both original user M values, every restored
user/user-M/ISP/MSP state and all three restored trace conditions are retained.
Each combination independently selects a trace-retaining/clearing service.
The maintained PowerShell command independently enumerates the literal program
transitions and matches all new counts; the earlier bridge counts are unchanged.

Initial canonical execution passes both tests without a production CPU fix.
Fresh report-producing canonical and structural runs now pass all four reports
with zero skips, mismatches, unsupported execution or untested phases. Their
15,423,488 total phases and exact combination counts match the independent
command enumeration. These are focused results from the initial build, not
final restored-build acceptance. The maintained
`UserRteSoftwareTrace` scope adds saved-SR BTST inversion, short format-2 return
and original CP-vector loss, requiring intended diagnostics in each of four
complete reports. All three probes are detected in all four complete reports;
every production source byte matches the preceding qualified manifest after
restoration. Fresh acceptance below qualifies this test-only checkpoint. No package
release or test retirement is included. Mixed-epoch trace provenance, internal
restoration and all other required model/reference/consolidation scope remain
open; milestone 6 stays **in progress**.

The first maintained probe, `040-user-software-trace-bit-test`, executes all
four complete groups with zero skips or unsupported execution. Each canonical
route detects 82,944 mismatches, leaves 430,080 dependent phases untested and
passes 847,872 phases. Each structural route detects 387,072, leaves 2,007,040
dependent phases untested and passes 3,956,736. Every report contains the
intended CU-linear `test-T1` SR diagnostic. This qualifies the probe's ability
to detect a wrong saved-SR bit test; these deliberate failures are not
production failures.

The short-format-2-return probe also executes all four complete reports with
zero skips or unsupported execution. Each canonical route detects 39,936
mismatches, leaves 39,936 dependent phases untested and passes 1,281,024 phases.
Each structural route detects 186,368, leaves 186,368 dependent phases untested
and passes 5,978,112. Every report contains the intended CU-linear
`pending-return` inactive-stack-pointer diagnostic. This distinguishes the
required 12-byte pop from a wrong 8-byte return, including inactive stack banks.
The original-vector probe executes all four complete reports with zero skips
or unsupported execution. Each canonical route detects 55,296 mismatches,
leaves 322,560 dependent phases untested and passes 983,040 phases. Each
structural route detects 258,048 mismatches, leaves 1,505,280 dependent phases
untested and passes 4,587,520. Every report contains the intended CP50
`retry-RTE` diagnostic, expected exception 50 versus actual 49.

The restored acceptance build records CPU SHA256
`c427185573ad05ef6feca134d067ce41951f06c18002c429f73589ac306298cc`
and adapter SHA256
`459397c204b6b221b1dad6dbb5aba28ea91ca13713f4b4ee29a7fd00b3930111`.
Its source checkpoint is `47d6400a29002f86b96dc73afa6d10f82c0d8c62`
plus the recorded fixture/command changes; these binaries are not relabeled as
the later commit containing this record.

Fresh full Release validation in `artifacts/m6-user-service-full/` passes
**5,098 tests**, with eleven optional skips and zero failures. All nine qualified
WinUAE presets match their exact selections and both assembly identities.
Strict ordinary reporting verifies **63,082,006 logical cases / 656 batches**,
with `roadmapComplete=false`. Fresh pinned SingleStepTests passes 312,500 cases
from 125 files; Musashi passes 536 programs with 88 explicit exclusions and
624 total rows. Both explicit reference selections execute one test with no skip.

The complete 040 command in `artifacts/m6-user-service-audit/` executes all
**73 tests: 72 pass and the sole failure is the required 480-case inventory**.
Its 66 reports contain **48,113,344 passing phases**, zero mismatches or
unsupported execution, and 480 named untested requirements. Seven fixed
examples and fourteen fixture/command identities are retained. Seventeen fresh
controls reject their specific missing, shortened, foreign, redistributed or
omitted-fixture corruption. All production source, fixture/input and assembly
identities still match after reference and integrity checks. Milestone 6 remains
**in progress**; mixed-epoch provenance, internal restoration and all retained
model/reference/consolidation scope remain required. No CPU production fix,
package release, consumer rerun or old-test retirement is included.

### 040 successful mixed-epoch throwaway chains — focused qualification

The four `SyntheticM68040ThrowawayTests.MixedEpochTrace*` groups independently
select incoming, first/second throwaway and final restored trace states 0/T1/T0.
Expectations compose MC68040UM 8.2.6, 8.3, 8.4.2 and 8.4.6.7; no combined
hardware capture or FPU arithmetic oracle is claimed. User M=1 and aliases of
USP with distinct M values are explicit. Pending CT/CU/CP49–55 retains priority
and the original vector despite conflicting FPU registers. The CM route checks
the saved EA through an RTE-completion trace, MOVEM, its optional trace return
and a following BRA. Registers, all stack banks, guarded memory, discarded PCs,
saved PC/SR and exception sequence are checked at each scalar/batch phase.

| Group, each scalar/batch | Phases | Combinations |
| --- | ---: | ---: |
| Canonical one-throwaway, every CCR | 1,152,000 | 12,096 |
| Structural one/two-throwaway, CCR 0/31 | 3,744,000 | 628,992 |

After mutation restoration, fresh focused Release execution passes ten tests
with zero skips: these four complete reports and six independent fixed SR
encoding examples. All 9,792,000 phases pass with zero mismatches, unsupported
execution or untested phases. Every combination and its weight is checked
against the maintained command's independent literal PowerShell enumeration.
The original throwaway groups retain their identifiers and 82,944 / 428,544
phases; both passed the initial focused selection.

Four complete `MixedEpochRte` mutation probes detect intended restored-bits,
T0, MOVEM-lifetime and intermediate-SR defects in every report. Both structural
reports additionally retain the second-throwaway SR diagnostic. The T1-leak
probe targets trace-cleared final SR to keep both one/two-throwaway diagnostics
within bounded failure recording; the initial broader probe exhausted that
recording and was rejected, not credited. The rerun qualifies all four probes.
Every production source byte matches the preceding qualified manifest afterward.

Frozen build starts from `53251330d7cb04fe7cb0e5d970da30002dbadfc5` plus these
test changes; production tree remains `795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `1061e146cbf1507df3fb83ce9aa3fdb9157234f4e470bb172ff396cef79be710`.
Adapter SHA256: `592ffd91de0848bc6e49b13f87734eaec66fefc7c5c938483761634573fd48f3`.
`artifacts/m6-mixed-epoch-restored/qualification.json` records all seven current
fixture/command/assembly identities. These identities are not relabeled after
commit. The fresh inventory selection executes one failing test, with no skips,
retaining exactly 480 untested cases / fifteen combinations.

The expanded complete 040 command requires 83 tests, 70 reports, thirteen fixed
examples and fourteen input identities: 57,905,824 phases including that
480-case inventory. Ordinary reporting requires 72,874,006 cases / 660 batches.
Those are expanded selection requirements, not a fresh complete-suite result.
The preceding full-suite, complete 040, external-reference and report-integrity
qualification remains separate historical evidence; it was not rerun or
relabeled for this test-only checkpoint.

Mixed epochs during validation faults, repair/retry and internal restoration
remain required, alongside every retained data/context/model/reference and
consolidation requirement. Milestone 6 stays **in progress**. No production
CPU fix, package release, consumer rerun or regression retirement is included.

### Mixed-epoch validation fault capture — qualified checkpoint

`SyntheticM68040RteValidationFaultTests.MixedEpochFaultCapture*` extends physical
read rejection to independent incoming/first/second trace states and all stack
banks, including aliases of user/user-M. Canonical cases cross final trace/CCR
and reject the PC read; structural cases hold final T1 and reject every transfer
byte. Source SR/PC/format and continuation reads are real bus operations.
Expectations compose MC68040UM 8.1, 8.2.5/6, 8.3, 8.4.2 and 8.4.6.7; they are
software qualification, not a hardware capture of combined fault behavior.

Every phase checks register/stack/memory preservation, saved live SR, fault PC,
exact access width, original pending CP vector and preserved FPU registers. A
bare return is followed by a privilege failure on user tails; supervisor retry
is not executed and original trace deferral is not inferred. Structural tests
caught a test-fixture bug: USP had been initialized after pointer construction,
skipping an aliased throwaway. The input base now remains unconsumed; failed
initial evidence is retained separately and not credited.

| Group, each scalar/batch | Phases | Combinations |
| --- | ---: | ---: |
| Canonical PC-read boundaries | 276,480 | 3,456 |
| Structural validation bytes | 3,556,800 | 711,360 |

Corrected initial focused reports pass all 7,666,560 phases. Three complete
`MixedEpochFault` probes qualify lost live T1, premature CCR installation and
the aliased-USP fixture error. CPU probes fail all four groups; the fixture
probe preserves both canonical groups and fails both structural groups. Intended
saved-SR diagnostics, including second-throwaway provenance in both structural
reports, are required. Every production source byte is restored against the
preceding qualified manifest.

Fresh restored Release build succeeds with zero warnings/errors. Focused
execution passes ten tests with zero skips: four complete reports contain
7,666,560 passing phases, with zero mismatches, unsupported execution or
untested phases, plus six fixed SR encodings. Every new combination and its
weight matches independent literal PowerShell enumeration. Fourteen retained
tests passed before the fixture correction, which only affects new mixed-epoch
cases; that evidence remains separate. A fresh inventory selection executes
one expected failing test with zero skips, retaining 480 untested requirements
/ fifteen combinations.

Frozen build starts from `5e90768279da33092d2da3f14e870a6a7881da44` plus these
test changes; production tree remains `795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `e553a838521fb957c377ecfa65cab36c31f0d651e8d3ec705fb68963465ed41a`.
Adapter SHA256: `da2b8dd9d5e1263563a64486703b89ccba3e203d1d1a81de0dfc99141c880425`.
`artifacts/m6-mixed-fault-restored/qualification.json` records all eight current
fixture/command/assembly identities. Build identities are not relabeled after
commit. Broader full-suite, complete 040 and external-reference qualification
retains its original checkpoint; it was not rerun for this test-only slice.
Expanded 040/ordinary requirements are recorded in the plan. Trace
deferral/resumption, repair/retry, internal restoration and every remaining
model/reference/consolidation requirement stay open. No CPU fix, consumer
rerun, package release or additional regression retirement is included.

### Mixed-epoch RTE retry discovery — unresolved trace composition

The previous repaired-chain fixtures reused incoming trace in every throwaway,
so they could not distinguish original-instruction trace deferral from tracing
a fresh retry with the last committed SR. The new `M68040MixedEpochRetryDiscoveryTests`
passes independent first/second trace values to the shared test-only repair
fixture. Physical PC-read rejection, saved live SR/PC, committed stack pointers,
three real repair stores, handler return and the original RTE retry are executed.
The matrix includes one/two throwaways, all intermediate aliases including
user M=1, supervisor fault tails, all restored banks, CCR 0/31 and format0/normal/CM.

| Each scalar/batch report | Phases |
| --- | ---: |
| Passing | 220,896 |
| Mismatching | 14,976 |
| Unsupported | 0 |
| Untested after a failed prerequisite | 22,464 |
| Total / combinations | 258,336 / 16,848 |

Both tests execute and fail with zero skips. The current core traces the retry
using its entry SR, which contains the last committed throwaway trace. Applying
MC68040UM 8.2.6's original-instruction deferral rule instead predicts the opposite
outcome when the trace classifications differ: original T1/T0 with a cleared
throwaway loses the predicted trace; original zero with a traced throwaway gains
one. Both directions and the second-throwaway diagnostic are retained in both
reports. Equal classifications pass and mismatch counts localize the divergence
to retry. The interaction of the general deferral rule with RTE validation
restart remains an unqualified composition, not confirmed hardware behavior.
MC68040UM 8.4.2/8.4.6.7 and the addendum do not establish an extra private trace
latch here. Documentary WinUAE source was inspected, not executed as a fault
oracle. No CPU fix or guessed CT/latch behavior is introduced.

The explicit `test-copper68k-040-mixed-retry-discovery.ps1` command validates every
combination and phase weight using literal independent enumeration. It retains
the failed summary with `architecturallyQualified=false`, `passed=false` and
`roadmapComplete=false`. Five negative integrity controls reject missing
identities/fixture identity, empty selection, missing report and missing
combination for their intended reasons. Three retained boundary/encoding tests
pass with zero skips. Release build has zero warnings/errors.

Frozen build starts from `78d6dceb5d3577f5f6cc9ca7a0c268916f7d507d` plus these
test changes; production tree remains `795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `084d3c734d22aa40271c5700a67a385041dd5c27448832777f9b3161a0c14cec`.
Adapter SHA256: `978a24ef1123392a681b9eff9d12ff77fbe00dd293396c7b2f24df95de12d87f`.
`artifacts/m6-mixed-retry-discovery-final/identities.json` records production and
fixture/command/assembly identities; the corresponding summary retains all
counts. These build identities are not relabeled after commit. No previous
broader qualification is rerun or relabeled. Ordinary/complete-040 coverage
requirements remain those of the preceding checkpoint; these separately gated
discovery cases do not replace its 480-case inventory. Independent trace
qualification, complete executed mixed-epoch repair/retry, internal restoration,
all other model/reference gaps and consolidation remain required.

### Chained odd-PC short-frame SR handoff — executed software reference

The required odd-PC provenance work now has an executed reference for chained
format0/2/3 tails. `SyntheticM68040ChainedOddReturnTests` prepares independent
frame addresses and raw words, one/two throwaways, every stack and user M=1
alias, independent incoming/first/second/final trace states, all canonical CCRs
and structural CCR 0/31, alignment 0/1, VBR 0/0x10000 and low/high odd PCs.
Full architectural register/memory checks retain surrounding frame guards,
consumed USP/ISP/MSP, validation reads in order, non-fetch of discarded/odd PCs,
the saved RTE PC/SR and format-2 fault address. Scalar and one-instruction batch
execution share the public factory and retained batch boundary checks.

| Fresh report | Passing cases | Combinations | Per-combination CCR weight |
| --- | ---: | ---: | ---: |
| Canonical scalar | 82,944 | 2,592 | 32 |
| Canonical batch | 82,944 | 2,592 | 32 |
| Structural scalar | 539,136 | 269,568 | 2 |
| Structural batch | 539,136 | 269,568 | 2 |
| Total | 1,244,160 | 544,320 | — |

Fresh Release build succeeds with zero .NET warnings/errors. Four tests execute
and pass with zero skips; every report has zero mismatches, unsupported or
untested cases. The native reference independently compares every row and
agrees in all 1,244,160 cases. Literal PowerShell enumeration verifies every
architectural combination and weight, separately from xUnit counts.

`scripts/test-copper68k-040-rte-handoff.ps1` requires a pristine official WinUAE
checkout at `5d22d33632646efc3f747f03e82d28353e52722e`. It compiles the original
opcode builder and generator, with the single CPU_TESTER configuration change,
then extracts the untouched generated `op_4e73_94_test_ff` and original
`MakeFromSR_x`, `MakeFromSR_T0`, `MakeFromSR` from `cputest.cpp`. The transport
implements sparse initialized memory, read recording, flags and the odd-PC
callback. Unexpected reads/exceptions, out-of-scope frames, missing/malformed
rows and empty selections fail. No Copper68k decoder, EA, arithmetic or timing
helper produces reference outcomes. Two literal native controls discriminate
one/two-throwaway SR provenance and consumed stacks independently of exported
CPU fixtures.

The generated function passes its last committed throwaway SR as the secondary
image to odd-PC entry, while the restored SR governs live flags/stack selection.
The observer stops at that callback. Its tuple is composed with architectural
format-2 entry and the traced-user saved-S correction described in the
[MC68040 addendum](https://www.nxp.com/docs/en/reference-manual/MC68040UMAD.pdf),
general-operation item 3 (page 2). This validates software handoff/read/stack
agreement and the explicitly composed frame expectation. It does **not** execute
WinUAE's full exception engine or establish hardware, cache, MMU, bus fault,
trace-retry or physical timing behavior. The pinned
[tester limitations](https://raw.githubusercontent.com/tonioni/WinUAE/5d22d33632646efc3f747f03e82d28353e52722e/cputest/readme.txt)
remain relevant. In particular, the generated 040 RTE lacks validation-read
bus-fault handling and cannot qualify the unresolved original-trace retry
discovery. No latch/CT workaround or CPU correction is inferred from this audit.

The native generator uses the upstream Unicode configuration and MSVC
19.51.36260 x64. One unused `oldpc` warning arises in untouched generated RTE;
it is retained in the native build log. Initial generator attempts used the
wrong table input or missing Unicode/link configuration and are not accepted
reference results. Initial C# fixture compilation used the wrong batch API/helper
name; those errors were corrected before any successful execution. The initial
maintained reference invocation also inherited MSVC's `Platform=x64` in its
.NET build; that preliminary comparison is not the acceptance record. The final
command restores compiler environment before building/testing the regular
Release assemblies. It records exact pristine source, extracted/generated
inputs, compiler, assemblies, fixture rows, native outputs, reports and TRX hashes.

`ChainedOddRte` mutation proofs execute both complete canonical routes per probe:

| Probe | Mismatches per scalar/batch report | Required diagnostic |
| --- | ---: | --- |
| Substitute final restored SR | 82,944 | Saved SR expected 0000, actual 001F |
| Lose committed throwaway trace | 55,296 | Saved SR expected 8000, actual 0000 |
| Omit traced-user saved S | 13,824 | Saved SR expected 2000, actual 0000 |

Each probe executes two failing tests, zero skips and 82,944 complete cases /
2,592 combinations per route. The runner requires the intended provenance
diagnostic and restores every production byte; the restored build succeeds.
The seven retained direct-return/fixed-SR tests pass without skips. The required
format7 protocol inventory is separately rerun: one expected failing test,
zero skips, exactly 480 untested cases / fifteen combinations. Its chained
odd-PC entry now identifies short-frame reference coverage while retaining
normal/CM and pending/foreign-context work.

Seven integrity controls reject missing identities/fixture identity, an empty
xUnit selection, a missing combination, a wrong combination weight, and native
missing/empty fixture inputs for their intended reasons. Output checksums are
updated only in the isolated semantic-corruption controls so checksum rejection
cannot stand in for the deeper combination/selection checks. The complete-040
gate's new branches are separately exercised against all four accepted reports;
its 78-report / 66,816,544-case requirement sum is checked without claiming a
fresh execution of the older complete selection.

Frozen acceptance build begins at `72102e90c3a78dd6538a4c6eca2d564e5bcad116`
plus these test/command changes. Production tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`; no production CPU byte changes.
CPU SHA256: `ac077d65570a3fc72972b0e3521a456f90838f925b1f9a3314e16d3977eda545`.
Adapter SHA256: `6f01ae15524f2d3cee820a632d91e146b9317e1efbc0b03dffedafe5f7c8b1db`.
`artifacts/m6-chained-odd-reference-final/identities.json` and
`qualification-summary.json` retain exact input/evidence hashes and counts;
build identities are not relabeled after commit. Mutation and retained/inventory
evidence stays in separate `m6-chained-odd-*` directories. Older broad CPU,
complete-040, external adapter and consumer qualifications are not rerun or
relabeled by this test-only change. Expanded requirement counts are in the plan.

Format7 chained odd-PC continuation/foreign-context provenance, original-trace
repair/retry, internal restoration, real data/writeback restart, earlier model
reference gaps and consolidation remain required. Milestone 6 stays **in
progress**, `roadmapComplete=false`. No old regression is retired, public API
changed, consumer dependency advanced or package published.

### Chained odd-PC normal/CM access-frame handoff

The same fixture now covers normal and CM format7 tails. Source/destination
stack aliases and all independent trace epochs retain the short-frame matrix;
the access frame additionally populates guarded WB status/data, checks SSW and
CM saved-EA validation order, consumes sixty bytes and takes the composed
format-2 address error without replaying software-owned writebacks. The existing
Copper68k policy of not retaining a MOVEM continuation after an odd return is
checked separately; the native observer cannot qualify that continuation lifetime.

| Fresh access report | Passing cases | Combinations |
| --- | ---: | ---: |
| Canonical scalar | 55,296 | 1,728 |
| Canonical batch | 55,296 | 1,728 |
| Structural scalar | 359,424 | 179,712 |
| Structural batch | 359,424 | 179,712 |
| Total | 829,440 | 362,880 |

Release build succeeds with zero .NET warnings/errors. Four access tests execute
and pass, with zero skips; every report has zero mismatches, unsupported or
untested cases. Canonical combinations have all 32 initial CCRs, structural
combinations have CCR 0/31. Every combination and weight is independently
enumerated. All 829,440 observed header/SR/stack handoffs agree with pinned
generated WinUAE. The current short profile is also rerun after the fixture/
observer changes: four tests pass without skips and all 1,244,160 cases agree.
Together the two current profiles contain 2,073,600 passing software cases.
Native compilation retains the unused `oldpc` warning from untouched source.

The explicit `-AccessFrames` profile records its identity and accepts only
declared normal/CM SSW values. The generated test RTE skips the SSW/EA protocol,
so the observer compares only its header reads and SR/stack handoff. Synthetic
validation/no-replay checks and documented frame/S-bit composition remain
separate. No SSW helper or production continuation calculation is injected into
the reference. Three literal native controls include one/two-throwaway short
frames and the sixty-byte access tail. CT/CU/CP and undefined continuation
inputs are rejected; wrong-profile report revalidation is also rejected.

This boundary matters for CM lifetime. The pinned MMU RTE generator calls
`m68k_do_rte_mmu040` before checking the odd return; that helper arms its MOVEM
restart state when CM is set. The non-MMU test generator used by this observer
does not call it. This documentary difference is retained as required
continuation/fault-lifetime research, not hidden by a passing header comparison
or classified as a proven non-MMU CPU defect. Full external restoration,
CM state across software repair, hardware behavior and physical timing are not
qualified by this observer. Enabled-MMU operation remains outside the roadmap.

Five maintained `ChainedOddAccessRte` probes each execute both complete
canonical groups (55,296 cases / 1,728 combinations per route) with two failing
tests, zero skips and the intended diagnostic:

| Probe | Mismatches per route | Diagnostic |
| --- | ---: | --- |
| Substitute final restored SR | 55,296 | Saved SR 0000 versus 001F |
| Lose committed trace | 36,864 | Saved SR 8000 versus 0000 |
| Omit traced-user saved S | 9,216 | Saved SR 2000 versus 0000 |
| Skip CM saved-EA validation | 27,648 | Validation read order |
| Arm MOVEM before odd-PC delivery | 27,648 | Retained continuation policy |

Production source bytes are restored and rebuilt. Twelve intended negative
controls reject missing identity/fixture identity, empty xUnit selection,
missing combination, wrong weight, native missing/empty fixtures, CT/CU/CP/
undefined SSWs and mismatched profile. The required inventory executes one
expected failing test, zero skips, retaining 480 untested cases / fifteen
combinations. Its odd-PC entry distinguishes qualified header/SR coverage from
the pending/foreign-context and full continuation behavior still required.

Frozen acceptance source starts at `2f66bcaa03d9e34efa82df80634003f2bfbf8c96`
plus these test/command changes; production tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `141b6fc84c140873d46e4f6bf49f909ac8d6b86ba506b3efcbffac92c0fb4db8`.
Adapter SHA256: `e84d20e2e3c49a73bc976091c415f3327363e1780fa76ef9174c3f2bf19e126e`.
`artifacts/m6-chained-access-reference-final/identities.json` and its summary
record current fixture/source/extracted-code/binary/row/evidence identities and
the explicit access profile. Current short-profile evidence is separately
`artifacts/m6-chained-access-short-retained/`; earlier snapshots are not relabeled.
Expanded ordinary/complete-040 requirements are recorded in the plan; older
broad CPU, complete selection and consumer results are not newly rerun here.

Pending/foreign odd-return context, CM fault/repair lifetime, original-trace
repair/retry, internal/data/writeback restoration, broader model reference
qualification and consolidation remain required. Milestone 6 stays **in
progress**, `roadmapComplete=false`. No CPU source change, package publication
or additional regression retirement is included.

### Executed 68040 CM lifetime discovery across odd-PC repair

The documentary CM difference above is now exercised rather than inferred from
source alone. `scripts/test-copper68k-040-cm-lifetime-discovery.ps1` builds the
unchanged full WinUAE generator at `5d22d33632646efc3f747f03e82d28353e52722e`.
Strict extraction retains original generated `op_4e73_31_ff`, MOVE.W immediate
through A7, MOVE.L immediate to 2(A7), PC-relative MOVEM.L and NOP. The original
`m68k_do_rte_mmu040` from `cpummu.cpp` and cputest SR helpers are also unchanged.
No function body is rewritten or replaced with Copper68k arithmetic/EA helpers.

Four literal normal/CM × even/odd controls execute a fixed program. The return
target is 6000/6001; the format7 CM EA is 4200; MOVEM's encoded PC base 6004 plus
0FFC selects ordinary 7000. Those addresses contain different D0/D1 values.
An odd return reaches an address-error boundary, writes the intended SR and
even PC into the frame with real instructions, returns, then executes MOVEM.
In this software model the helper arms CM before the odd-PC callback, both
repair stores and handler RTE leave it armed, and MOVEM uses 4200 and clears CM.
Every selected generated instruction is checked against a literal opcode at
its PC; sparse physical transport rejects uninitialized reads.

The crucial boundary remains explicit: the adapter composes documented
format-2 address-error entry after the callback. It does **not** execute full
WinUAE `Exception_mmu` or the run loop, enabled translation, IRQ/trace delivery,
cache/pipeline behavior or physical timing. The pinned MMU and non-MMU exception
paths have different SR handling; this harness must not certify their full
equivalence or establish hardware continuation lifetime. `newcpu.cpp` is pinned
as source context, not claimed as executed exception processing.

The CPU discovery varies entry ISP/MSP, restored user/user-M/ISP/MSP, normal/CM,
even/odd PC, stack alignment 0/1, VBR 0/10000 and every initial CCR. Each scalar
and batch route has 4,096 scenarios, 576 phase combinations and 18,432 logical
phases. All registers, physical stack pointers, PC/SR, exception metadata,
guarded memory and selected validation/operand read order are checked. Batched
steps require exactly one instruction and both boundary callbacks. Prerequisite
failure is recorded as untested later execution; no instruction is retried.

| Current discovery route | Passing | Mismatching | Unsupported | Untested |
| --- | ---: | ---: | ---: | ---: |
| Scalar | 16,384 | 1,024 | 0 | 1,024 |
| Batch | 16,384 | 1,024 | 0 | 1,024 |
| Total | 32,768 | 2,048 | 0 | 2,048 |

Both tests execute without skips and correctly fail. Every mismatch is the
odd-PC CM MOVEM phase: D0 expected 89ABCDEF from saved 4200, actual DEADBEEF from
recomputed 7000. Only those scenarios' following NOPs are untested. Normal and
even-PC CM controls, address-error entry composition, repair stores and handler
returns agree at their observed boundaries. Native comparison of all public
register/PC/SR/stack snapshots gives exactly the same phase classifications.
This is a localized executed software-reference discrepancy, not a proven
architectural production defect. No guessed continuation latch is added.

Fresh final evidence is `artifacts/m6-cm-lifetime-final/`; command exit 1 is the
expected current discovery failure. Release build has zero .NET warnings/errors;
native warnings are retained only in untouched generated source. The command
records complete current CPU/fixture, pinned source, generated/extracted code,
binary and row/evidence hashes. It independently enumerates all keys, 32-CCR
weights and 4,096 ordered fixture rows per route. Report-only revalidation also
executes the frozen native observer and requires byte-identical reference
results. Thirteen integrity controls reject missing identity/fixture identity,
empty selection, missing combination, wrong weight, duplicated fixture,
missing reference row, altered reference summary, native missing/empty inputs,
empty required selection, out-of-scope fixture and reordered index. They are
recorded in `artifacts/m6-cm-lifetime-integrity-final/controls.json`.

Eight retained tests pass without skips: six fixed saved-SR examples and both
existing CM MOVEM matrices, totalling 235,008 passing generated cases. The fresh
required inventory has one expected failing test without skips and retains
480 untested cases / fifteen combinations. Its explanation now names the
unqualified lifetime discrepancy. Ordinary synthetic/complete-040 selections
exclude the two gated discovery tests and their previous counts remain
requirements; older broad suites/consumer results are not relabeled as rerun.

Frozen build starts at `826351ff0709d2e990b6b62692f068038e06af75` plus these
test/command inputs. Production tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `89cefdfbcc27b2d64e0d5fc7dc764f02402406883ac2b21a5b88978553bd1c78`.
Adapter SHA256: `d31a06c3ca60d1644433acef41ffe826509f8bac376494064bffad3da5f00315`.
The frozen identities, rather than current post-commit build output, identify
this run. Full CM fault/repair lifetime, pending/foreign contexts, original-trace
retry, internal/data/writeback restoration, broader model reference coverage
and consolidation remain required. Milestone 6 stays **in progress**,
`roadmapComplete=false`; no CPU fix, package release or regression retirement.

### Scoped MMU exception entry executed during CM lifetime discovery

The CM command adds `-MmuExceptionEntry` to replace its composed native boundary
with unchanged exception code from the same WinUAE pin. In addition to generated
RTE/repair/MOVEM/NOP and the original CM helper, this profile extracts native
`newcpu.cpp` MakeSR/MakeFromSR, exception3 handling, `Exception`/`ExceptionX`,
`Exception_mmu`, trace clearing, and `newcpu_common.cpp`'s frame builder. The
original `fill_prefetch` executes its `cpu_compatible=0` early return. No function
body is edited. The selected frame's physical writes and vector fetch are real
reference operations; the adapter supplies memory, cycle and disabled-feature
transport. Other exception, interrupt and compatible-cache paths fail explicitly.
It is scoped software entry coverage, not enabled translation, complete run-loop,
trace-delivery, cache/pipeline, timing or hardware qualification.

Four literal controls still distinguish normal/CM and even/odd returns. The
native CM state survives actual exception entry, both repair stores and handler
RTE, then selects saved 4200 for MOVEM and clears. The fixture now additionally
exports raw frame SP/SR/PC/format/fault address immediately after the initial
return, before the repair stores erase evidence. Native execution compares these
bytes independently of the existing CPU fixture's composed frame expectation.

Both scalar/batch tests execute without skips, with 4,096 scenarios / 18,432
instruction phases / 576 combinations per route. Their phase classifications
remain 16,384 passing, 1,024 mismatching, zero unsupported and 1,024 untested;
only odd-PC CM MOVEM's D0 disagrees and its following sentinel is untested.
The new raw-frame comparison additionally checks 2,048 actual odd frames per
route. All differ only in saved SR: the MMU reference stacks restored SR;
Copper68k stacks the incoming secondary SR. All SP/PC/format/fault-address fields
agree, including both alignments and VBRs. Even scenarios export zero placeholders
and are not counted as actual frame comparisons. Total instruction phases remain
36,864; 4,096 frame comparisons/mismatches are reported separately.

The source paths therefore disagree in a way a register-only comparison cannot
reveal. The previous composed reference and the retained non-MMU handoff path
must not be relabeled as this MMU exception execution. Nor should a software-path
vote override processor documentation. The [MC68040 addendum](https://www.nxp.com/docs/en/reference-manual/MC68040UMAD.pdf),
general-operation item 3, describes the traced-user-return saved-S correction;
this no-trace matrix does not qualify its complete interaction with CM. The
[MC68040 manual](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf),
8.4.6.2/7, specifies saved-EA MOVEM restart but does not settle every intervening
odd-return fault/repair lifetime in this composition. The saved-SR difference
and continuation persistence remain software discovery, not confirmed CPU defects.

Final executed-entry evidence is
`artifacts/m6-cm-exception-comparison-final/`; the shared default profile is
freshly rerun in `artifacts/m6-cm-exception-default-final/`. Both requested
commands correctly exit 1. The default's unchanged instruction classifications
have zero raw frame comparisons, explicitly distinguished from agreement on
frames. Release builds have zero .NET warnings/errors; native warnings are
retained at untouched source sites. Source/input/generated/extracted/binary/
evidence identities and the profile are mandatory. Report-only revalidation
executes the frozen observer and requires identical reference outputs. Supplying
the wrong profile fails before qualification can be inferred.

Thirteen retained default controls and eight new executed-entry controls reject
invalid evidence. The latter cover wrong profile, missing frame identity,
truncated exported frame rows, altered reference frame bytes, and native missing,
empty, reordered or extra frame inputs. Fresh final controls are
`artifacts/m6-cm-exception-default-controls/controls.json` and
`artifacts/m6-cm-exception-entry-controls/controls.json`. Preliminary failed
harness builds/controls are not acceptance evidence. No current full CPU or
consumer rerun is claimed for these test/adapter-only changes.

Frozen source starts at `b88f95016bab947eaee92b04282d0dee2bd399b4` plus the
recorded fixture/adapter/command changes. Production tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `5bada3aedb413d5c9df20214fb7112e0edd8b4bc5d2977c89b61dc5b1070f20b`.
Adapter SHA256: `cdd945f93cb27493982fd5164834b10dc0b309b2307b53ba47156f22b3672201`.
The 480-case required inventory, ordinary/complete-040 requirements, other model
audits and consolidation scope remain intact. Full CM lifetime, pending/foreign
contexts, original-trace retry and internal/data/writeback restoration remain
required. Milestone 6 stays **in progress**, `roadmapComplete=false`; no production
CPU fix, package publication or regression retirement is included.

### 68060 PCR qualification and consolidation (2026-10-06)

`SyntheticPcrTests` adds four ordinary-CI batches and **305,152 logical cases**.
Expectations come from [MC68060UM revision 1](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf),
3.2.2.5 / figure 3-5, 11.1.2.1.1 and D-22. Identification is 0430, the selected
first-revision byte is zero, EDEBUG is bit 7, DFP bit 1 and ESS bit 0. The
identification/revision fields ignore writes; the defined controls clear on
reset. The [1998 addendum](https://www.nxp.com/docs/en/reference-manual/MC68060UMAD.pdf)
does not amend this register diagram.

| Group | Logical cases | Architectural combinations | Qualification |
| --- | ---: | ---: | --- |
| `system-pcr-defined` | 102,400 | 102,400 | All eight initial/control images, sixteen general registers, all CCRs, supervisor writes/readback and user privilege rejection; all defined raw read images |
| `system-pcr-identification` | 172,032 | 172,032 | Every read-only bit, all-set/alternating high patterns, all eight control images, registers and both stacks, CCR 0/31 |
| `system-pcr-reserved-policy` | 6,144 | 6,144 | Existing repository mask policy for reserved writes; explicitly excluded from architectural qualification |
| `system-pcr-reset` | 24,576 | 768 | Actual write, external API reset and instruction readback, all defined control images, incoming stacks, registers and CCRs |

The reusable MOVEC register fixture gains optional CCR selections and guards the
PCR during other control transfers. Tests check all architectural registers,
exact PC, SR/CCR, stack state, execution state, unchanged controls and memory.
Successful readbacks include a following NOP. Failed writes/reset prerequisites
leave dependent phases explicitly untested; no partially executed instruction
is retried. Initial control images are fixture setup, not decoder-derived
expectations. Reset register clearing and the saved MSP initialization are
public API conventions, distinguished from the processor's PCR/SR reset rules.

The manual requires reserved bits 6..2 to remain zero. Moreover,
[MC68060DE rev 4.0](https://www.nxp.com/docs/en/errata/MC68060DE.pdf), I14/I15,
assign bit 5 to a bypass workaround on specified masksets. Reserved writes
therefore remain a named repository-policy batch, never promoted as proof that
every physical maskset ignores these bits. Physical debug output, superscalar
timing, pending-FPU synchronization and FPU arithmetic are not qualified here.
The fixtures start with no outstanding floating-point work.

Pinned WinUAE `5d22d33632646efc3f747f03e82d28353e52722e` still writes EDEBUG using
bit 6 in `newcpu_common.cpp` lines 172-173. Its unchanged source SHA256 is
`ef87202a97c0135128e7367a0fc09c627d29fc606d5de7fc06ad5f49d8bb9cbc`.
This software/manual disagreement remains open. No reference mask, production
CPU behavior or original Basic discrepancy is changed to obtain agreement.
Ordinary STOP's S-clear disagreement also remains required.

The dedicated command independently enumerates every combination and its
weight, verifies exactly four executed tests, rejects failed/unsupported/untested
cases, and records exact production/fixture/command, binary and evidence input
identities. Report revalidation checks the same source/binary identities and
selection. Eight integrity controls reject missing manifests, wrong profiles,
missing/duplicate source identities, missing reports, empty execution, incorrect
weights and substituted combinations. None changes the original accepted files.

```powershell
./scripts/test-copper68k-060-pcr.ps1 -OutputDirectory artifacts/m6-pcr-qualified-final
./scripts/test-copper68k-060-pcr.ps1 -ValidateReportsOnly -OutputDirectory artifacts/m6-pcr-qualified-final
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope ProcessorConfiguration
```

Retirement: `M68060InterpreterTests.ProcessorIdentificationIsReadOnlyAndResetClearsControls`
is replaced only after all three mutations fail both the original fact and the
complete replacement matrix. Before retirement each selection executes two
failed tests without skips; afterward each executes the replacement without skips.

| Mutation | Replacement witness | Mismatching / dependent untested |
| --- | --- | ---: |
| EDEBUG moved to bit 6 | `PCR/R0/initial=00000000/value=00000080/super=True/ccr=00/write` | 16,384 / 16,384 |
| Identification writable | `PCR/R0/initial=00000000/value=00000000/super=True/ccr=00/write` | 57,344 / 57,344 |
| Reset retains controls | `PCR/reset/R0/super=False/image=00000001/reset/op=4E7B/ccr=00` | 7,168 / 7,168 |

The old all-ones write is retained explicitly in the reserved-policy matrix
(`R0/initial=00000000/value=FFFFFFFF/super=True/ccr=00/write` and `/read`);
reset/readback from its resulting image 83 is covered for every destination,
including R1. No specialized instruction, cache, bus, JIT or native regression
is retired. Mutation sources are restored byte-for-byte and rebuilt successfully.

Fresh acceptance: four PCR tests pass with no skips, **305,152 cases** and no
mismatches/unsupported/untested cases. Retained shared-fixture/control coverage
passes **46 tests**, including **1,236,864 cases / fifteen reports** plus 31
fixed 68060 checks, with no skips. Evidence is in `artifacts/m6-pcr-qualified-final`,
`artifacts/m6-pcr-retained`, `artifacts/m6-pcr-mutations-before-retirement`,
`artifacts/m6-pcr-mutations-after-retirement` and `artifacts/m6-pcr-integrity`.
The first reset attempt had an incorrect fixture MSP expectation and is excluded
from acceptance; its trace remains under `artifacts/m6-pcr-first` in test outputs.

Source starts at `11124e9a24fb13c832ed569626ec2b76f6242696` plus recorded test/command
changes. Production tree remains `795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `a44b20aed377d1789607415ed50fde48a8ebea38cbc49cc50a08cc0ad4a0aff8`.
Test assembly SHA256: `0ebdff1cff679ada0488fd29735e99e10a705ef33ca3f8798ecfe838706a554f`.
The ordinary CI requirement grows to **82,919,318 cases / 676 reports**; this
is an expanded requirement, not a new broad-suite execution claim. Complete-040
requirements and the 480-case remaining inventory are unchanged. Full CM
lifetime, advanced exception/restoration and broader model audits/consolidation
remain required. Milestone 6 stays **in progress**, `roadmapComplete=false`.
No production CPU fix, consumer replay or package publication is included.

### 68060 ordinary STOP software-reference discovery (2026-10-06)

`M68060StopDiscoveryTests` compares scalar and batch execution with untouched
pinned WinUAE `5d22d33632646efc3f747f03e82d28353e52722e`. The original
`gencpu.cpp` STOP section labels the target-S-clear behavior undocumented:
68060 rejects it immediately, before transferring the new SR. Copper68k
currently transfers that SR and either stops or delivers incoming trace.
This is a software-reference discrepancy, not a qualified architectural fix.

[M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf), STOP,
6-85, describes SR transfer, advancing PC and stopping execution when the
incoming processor state is supervisor. [MC68060UM revision 1](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf),
8.2.5 and 8.2.6, supplies incoming privilege and trace rules and format-zero/
format-two boundaries. Its LPSTOP entry D-19 explicitly rejects a new S-clear
status image; that statement is not an explicit ordinary-STOP rule. These
manual sections do not settle the undocumented ordinary-STOP discrepancy.
No guessed production restriction or timing change is introduced.

Each route enumerates 163,840 STOP scenarios: 32,768 target-status image cases
and 131,072 canonical CCR cases. Target images use defined mask B71F; both
incoming stacks, initial/new M and T1, all target IPLs in the image group and
IPL 0/7 in the CCR group are covered. Incoming IPL is fixed at 7. The image
group uses input CCR 0/31; the CCR group covers all 32 by 32 combinations.
Successful nontraced STOP adds 20,480 inert attempts, giving **184,320 logical
cases / 720 combinations / one xUnit batch per route**.

All registers, stack pointers, PC, SR/CCR, stopped state, exception boundaries,
guarded memory and preserved PCR are checked independently. Privilege saves
original SR/opcode PC; trace saves loaded SR/next PC. The batch API counts an
idle attempt as one logical step, with one before/after callback. This is an
existing API convention, not physical instruction retirement. Inert attempts
must leave architectural state unchanged and never execute the following NOP.
There is no instruction retry after partial effects.

The observer executes unchanged generated `op_4e72_33_ff` from CPU_TESTER=0,
and original `MakeSR`, `MakeFromSR_x`, `MakeFromSR_STOP`, `m68k_set_stop` and
`do_cycles_stop` functions. Four literal accepted, incoming-user, target-S-clear
and incoming-trace controls guard transport. The original native stopped PC
remains at the opcode until resume; comparison explicitly normalizes the
architectural next PC to 1004. Incoming trace is a composed boundary, not
native trace/run-loop execution. Privilege is an observed native callback;
native exception entry and frame writes are not executed. The native helper's
stopped reentry is checked separately from Copper68k's inert architectural
state. No IRQ, enabled MMU, cache, bus-cycle or hardware oracle is claimed.

| Route | Passing | Mismatching | Unsupported | Untested |
| --- | ---: | ---: | ---: | ---: |
| Scalar | 143,360 | 40,960 | 0 | 0 |
| Batch | 143,360 | 40,960 | 0 | 0 |

Both tests execute and fail without skips. Every mismatch has incoming
supervisor state and a target S-clear image; incoming trace does not override
the reference's privilege rejection. Per route the native observer executes
163,840 rows, reporting 122,880 matching and 40,960 mismatching boundaries;
the extra 20,480 passing logical cases are Copper68k inert attempts. These
different denominators are intentional. Full frame checks belong to the
synthetic manual expectation, not to the native boundary-only comparison.

```powershell
./scripts/test-copper68k-060-stop-discovery.ps1 -OutputDirectory artifacts/m6-stop-discovery-final
./scripts/test-copper68k-060-stop-discovery.ps1 -ValidateReportsOnly -OutputDirectory artifacts/m6-stop-discovery-final
```

Both commands terminate with the explicit **81,920 software-reference
mismatches** failure after validating and replaying both routes. They record
pristine pinned sources, exact fixture/CPU sources, generated/extracted code,
binaries and evidence identities. Validation independently enumerates keys,
weights, fixture ordering, defined SR selections and canonical registers/
stacks; it requires both named tests with no skips. Frozen report validation
executes the observer again and requires byte-identical outputs. Missing,
empty or malformed native selections fail rather than count as coverage.

Thirteen integrity controls reject missing manifests, wrong profiles, missing
source identities/fixtures/reports, empty execution, substituted keys, altered
weights, wrong registers and reordered rows, plus missing/empty/wrong-register
native fixtures. Semantic controls refresh the altered evidence hash to test
content validation as well as identity validation. Originals remain unchanged.
Evidence is under `artifacts/m6-stop-discovery-final`,
`artifacts/m6-stop-discovery-revalidate.log` and `artifacts/m6-stop-integrity`.
Preliminary `m6-stop-discovery-first` had a fixture build error; `initial` and
`debug` incorrectly expected zero batch idle steps. These attempts are excluded
from the final classifications; no production defect is inferred from them.

Fresh Release build has zero .NET warnings/errors. The observer has two
warnings in unchanged extracted reference code (SR narrowing and unused
opcode). Retained STOP/LPSTOP, system privilege, trace, idle-batch and 68060
coverage passes **75 tests without skips**, containing **331,582 cases / 25
reports** and fifty fixed examples, under `artifacts/m6-stop-retained-final`.
Source starts at `a71460dcecd219a751556f8070b26ee44beca7aa` plus recorded fixture/
observer/command changes; production tree stays
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `424896b6ca007b4f6294c25e6672b6a560ceee4ef5b2cf6afa17a5e8e4cdc1cd`.
Test SHA256: `4f34672737725504a906666ad6abef23653b25e976a12036e7415bb510cb9043`.

The discovery remains outside ordinary synthetic CI and promoted complete-040
coverage. Their requirements stay **82,919,318 cases / 676 reports** and
**95 tests / 82 reports / thirteen fixed examples / 67,645,984 phases including
inventory** respectively. The 480 untested inventory cases and all broader
qualification/consolidation requirements remain. Ordinary STOP's architectural
S-clear rule, full external trace/exception/IRQ qualification and PCR's software/
manual disagreement are still open. Milestone 6 stays **in progress**,
`architecturallyQualified=false`, `roadmapComplete=false`. No CPU fix, regression
retirement, consumer replay or package release is included.

### 68010 MOVEC register qualification and consolidation (2026-10-06)

`SyntheticM68010MovecTests` adds **2,271,744 passing logical cases / four
ordinary-CI batches**. Independent specifications come from
[MC68000UM ninth edition](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf),
figure 2-3 and 6.3.6/7, and
[M68000PM MOVEC](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf),
6-22/23. MC68010 legal selectors are SFC 000, DFC 001, USP 800 and VBR 801.
Function codes have three implemented bits; USP/VBR retain all 32 register
bits despite the external 24-bit address bus. Transfers preserve CCR. Incoming
user privilege takes vector 8; undefined selectors in supervisor state take
vector 4, both saving opcode PC in an eight-byte format-zero frame.

| Group | Logical cases | Combinations | Selection |
| --- | ---: | ---: | --- |
| `system-010-movec-pairs` | 688,128 | 21,504 | Four controls, every source/readback register pair, seven values, both privilege states and all CCRs |
| `system-010-movec-masks` | 1,007,616 | 31,488 | Both function codes, all eight initial images, 41 low-bit/high-bit/pattern samples, every register, privilege and CCR |
| `system-010-movec-reads` | 48,128 | 1,504 | All eight function-code images and 39 full-width USP/VBR images, every register and CCR |
| `system-010-movec-encodings` | 527,872 | 262,016 | All 4,092 undefined selectors, register encodings and directions in both privilege states at CCR 0/31; all legal user forms at every CCR |

The shared MOVEC fixture supports distinct write/read registers, USP effects,
independent vector-memory/control initialization and grouped CCR weights. It
now also preserves SFC, DFC and VBR when another control is transferred. Source
setup follows control initialization, so a user-mode A7 source has the intended
USP alias. Expectations capture setup before execution; result masks and legal
selectors never use production decoders, arithmetic or timing helpers. Every
general register, active/inactive stack, defined SR bit, exact PC, unchanged
control, initialized memory and new memory write is checked. Successful
readbacks include a following NOP; a failed write leaves readback untested and
is never retried. Read phases identify their actual 4E7A opcode separately.
The 010 stack-model check expects false independently of its observed value.

These are nontraced IPL7 register/encoding semantics, not qualification of
physical function-code spaces, exception bus ordering, prefetch, format-8
restart, interrupts or timing. Diagnostic 010 coverage does not establish
desktop readiness. Existing specialized tests for these protocols remain.
No newly executed external CPU or hardware oracle is claimed by this manual
qualification. Existing independent model audits remain separately recorded.

The dedicated command checks exact fixture/CPU/command sources, binaries and
evidence identities, exactly four named passing tests with no skips, and
independently enumerated combination keys and weights. Requested execution
and report revalidation reject missing inputs, empty selections, mismatches,
unsupported execution and untested cases. Ten integrity controls reject missing
manifests, wrong profiles, missing/duplicate sources, missing reports, empty
execution, wrong keys/weights/models and substituted test names. Content
controls refresh the changed evidence hash; originals remain unchanged.

```powershell
./scripts/test-copper68k-010-movec.ps1 -OutputDirectory artifacts/m6-010-movec-qualified-final
./scripts/test-copper68k-010-movec.ps1 -ValidateReportsOnly -OutputDirectory artifacts/m6-010-movec-qualified-final
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Movec010
```

Retirement is limited to three methods in `M68010InterpreterTests`:
`MovecTransfersVectorBaseRegister`, `MovecTransfersSupportedControlRegisters`
(three theory rows), and `MovecVectorBaseToA7UpdatesActiveSupervisorStackPointer`.
These five xUnit cases have explicit replacements:

- D0→VBR→D1, value 00000400: the pairs matrix's VBR/R0/read-R1 write/read.
- D0→SFC→D1, FFFFFFFE→6; D0→DFC→D1, FFFFFFFD→5; D0→VBR→D1,
  12345678: corresponding pairs cases, plus the complete mask/read matrices.
- VBR→A7, 00004800: reads matrix VBR/read-R15/internal=00004800.
- The old no-020-stack assertion: the independent 010 stack-model check in
  every shared transfer, plus the retained public factory regression.

The fixed old encodings 4E7B/0801, 4E7A/1801 and 4E7A/F801 correspond to those
generated cases. Six maintained mutations fail both the original regression
and its complete replacement before retirement, and still fail afterward:

| Mutation | Original witness | Replacement mismatching / dependent untested |
| --- | --- | ---: |
| SFC ignores its three-bit mask | Supported-control SFC theory row | 135,168 / 135,168 |
| DFC ignores its three-bit mask | Supported-control DFC theory row | 135,168 / 135,168 |
| VBR truncated to 24 bits | Supported-control VBR theory row | 32,768 / 32,768 |
| VBR loses bit 10 | Vector-base transfer fact | 40,960 / 40,960 |
| A7 loses saved SSP synchronization | VBR-to-A7 fact | 3,008 / 0 |
| Wrong 020 stack model enabled | Vector-base transfer fact | 458,752 / 229,376 |

Before retirement each theory probe executes four tests: two fail, two pass;
each fact probe executes two failing tests. After retirement each executes
one failing replacement test. No probe skips execution or counts compilation
failure as detection. Production sources are restored byte-for-byte and the
restored Release build has zero warnings/errors. Preliminary proof attempts
had an overbroad legacy-name selection and an inaccessible setter in the
stack-model mutation; those failed commands are excluded. Final proof is in
`artifacts/m6-010-movec-mutations-before-final` and
`artifacts/m6-010-movec-mutations-after-final`.

Fresh command execution and frozen report revalidation both pass all four
matrices, with zero mismatches/unsupported/untested cases and no skips, in
`artifacts/m6-010-movec-qualified-final`. All **69 retained tests pass without
skips**, including **1,633,168 cases / thirty reports** and 39 fixed 010/060
examples, in `artifacts/m6-010-movec-retained`. This covers every other shared
MOVEC fixture family and the old model-control inventory. Integrity evidence
is in `artifacts/m6-010-movec-integrity`.

Source starts at `e2c456bcf140196497807baa3a7f44569eca64a3` plus recorded test/
command changes. Production tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `d151cb2e5d3ab78e5b118cfe7e8f69f01db38a345b1fd2baabc61c995a013fe5`.
Test SHA256: `a36059542a07a006749d14b98f1aea479c5698e7c50a419f09bbaac115ea6396`.
Ordinary CI now requires **85,191,062 cases / 680 reports**; this is an expanded
requirement, not a fresh full-suite execution result. Complete-040 requirements
remain unchanged. Its updated inventory wording recognizes both composed and
executed MMU-entry CM discoveries; neither becomes hardware qualification.
Fresh inventory execution still fails with exactly **480 untested cases /
fifteen combinations**, without skips, in `artifacts/m6-010-movec-inventory-final`.
Advanced 010/020/030 restoration, full CM/fault lifetime, STOP/PCR disagreement,
broader model audits and consolidation remain required. Milestone 6 stays **in
progress**, `roadmapComplete=false`; no production fix, consumer replay or
package publication is included.

### Rejected 68010 RTE format software-reference discovery (2026-10-06)

The remaining invalid-RTE CCR/trace work now has an executed discovery rather
than an inferred decoder expectation. `M68010RteFormatDiscoveryTests` exercises
scalar and one-instruction batch execution through the public 68010 factory.
`scripts/test-copper68k-010-rte-format-discovery.ps1` generates, extracts and
executes untouched WinUAE generic `op_4e73_4_ff` and compatible
`op_4e73_11_ff`, together with original MakeSR / MakeFromSR helpers, at pin
`5d22d33632646efc3f747f03e82d28353e52722e`.

The [pinned generator](https://github.com/tonioni/WinUAE/blob/5d22d33632646efc3f747f03e82d28353e52722e/gencpu.cpp#L7012)
sets N from the sign of an invalid format word, clears Z/V and preserves X/C
before its vector-14 callback. Copper68k instead preserves all incoming CCR
bits. Generic and compatible functions agree on these flags, privilege precedence,
callback PC, unconsumed stack and unchanged registers. They share generator
provenance, so agreement between them is not two independent hardware oracles.

[MC68000UM section 6.4](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf)
defines format rejection before the stack is consumed. Section 6.3.7 defines
privilege rejection and section 6.3.8 describes trace/aborted-instruction rules.
These passages do not specifically establish N/Z/V on an invalid-format failure.
The executed software result is therefore an unresolved architectural qualification
question; no CPU fix is selected solely from this discrepancy.

Each route executes the following separate deterministic groups within one batch:

| Matrix | Selection | Cases / combinations |
| --- | --- | ---: |
| Words | All 57,344 words with format nibble 1–7 or 9–F, user/supervisor, CCR 0/31, fixed stacked A71F and odd FFFF6001 target | 229,376 / 114,688 |
| CCR/header | Fourteen invalid formats, offsets 000/004/024/3FC/7FC/FFF, user/supervisor, incoming T clear/set, eight stacked SRs, four even/odd/high-address target PCs and every incoming CCR | 344,064 / 10,752 |
| Total, each route | Both matrices | **573,440 / 125,440** |

Incoming IPL is 7. The CCR/header matrix's stacked SRs are 0000/001F,
2000/201F, 8000/801F and A000/A01F; targets are 00006000/00006001 and
FFFF6000/FFFF6001. Entire invalid words include reserved low fields, but this
does not assert behavior for otherwise valid reserved-format encodings. Valid
format0/8 words and version/internal format8 restoration are outside this matrix.

Each route reports **336,896 passing / 236,544 mismatching / zero unsupported /
zero untested cases**. Both named tests execute and fail without skips. Every
user-mode row rejects with vector8 and unchanged CCR. Supervisor rows reject
with vector14 and expose only the N/Z/V disagreement. For example incoming
271F, format1000, stacked A71F and targetFFFF6001 yields software saved SR2711
versus Copper68k271F, with saved PC1000, handler90E0 and SP46F8 on both sides.
Changing stacked S/T/CCR or the target never bypasses rejection.

All other SR bits, D/A registers, stack banks, execution state, control-stack
configuration, original frame bytes and surroundings are compared even when
N/Z/V mismatch. Raw stacked SR must equal the CPU's recorded saved SR, and
the current SR must follow exception-entry clearing. Repository header read sets
are checked separately, and the rejected target must never be fetched. There is
no retry, dependent sentinel or partially abandoned operand phase.

Every row exports a separate surrounding-state result before the CCR comparison;
all **573,440 results per route pass**. The native observer and report validator
require that result independently, so a CCR mismatch cannot conceal a register,
frame, callback, stack-bank or access invariant failure.

The native observer checks unconsumed stack/registers at the exception callback.
Generic physical transport reads SR, both PC words and format; compatible source
reads SR, format and PC high before rejection. This difference is retained and
explicitly checked, not normalized into a physical bus-order claim. Native
exception-frame entry, trace/IRQ run-loop delivery and timing are not executed;
the format0 frame and vector fixtures are compositions in the full CPU comparison.
Long-frame version checks, restart, prefetch fault sequencing and bus errors are
not qualified by these generated functions. Eight literal controls per observer
invocation check low/high format rejection, user privilege and valid short returns
on both implementations.

Fresh execution in `artifacts/m6-010-rte-format-qualified` completes all synthetic
and native rows and returns exit1 explicitly for **473,088 combined discrepancies**.
Report-only revalidation also executes the frozen observer and requires identical
outputs. The command verifies the exact two named executions, all pinned source /
generated / fixture / CPU/test binary / evidence identities, every independently
enumerated key and weight, all fixture row ordering and all per-combination
classifications. Missing or empty fixtures/selections cannot pass. The preliminary
`artifacts/m6-010-rte-format-first` observer transport failed its literal controls
because wrapper long-read evaluation order was unspecified; it is excluded.
Sequential transport reads fixed that wrapper, leaving extracted functions intact.

The second preliminary audit recorded the same counts but initialized the native
inactive SSP to 4700 in user mode, while the CPU fixture uses 8000. This bank was
not consumed before callback, but the final audit aligns and exports both initial
stack banks and independently verifies them on every row. The extracted RTE
hash remains unchanged. Preliminary reports are not the accepted final identities.

The retained ordinary `system-rte` group's invalid-010 CCR preservation assertion
remains a repository behavior check; it is not independent flag qualification.
This separate gated discovery keeps the software disagreement visible and failing.

Twenty-three integrity controls in `artifacts/m6-010-rte-format-integrity-proof`
reject missing manifests/inputs/fixtures/reports, duplicate identities, wrong
profiles/models/named or empty selections, changed keys/weights/classifications,
reordered rows, altered native SR, wrong initial stack banks and failed
surrounding-state outcomes. Native controls additionally reject missing/empty
inputs, valid-format substitution, wrong declared SR/saved PC, wrong stack-bank
images and hidden surrounding-state failure. Content changes refresh evidence
hashes before validation, so these exercise semantic gates rather than merely
detecting changed hashes. All subprocess statuses and expected reasons are checked;
the control harness itself completes with exit0. Original evidence stays intact.

Retained evidence is `artifacts/m6-010-rte-format-retained-qualified`: **25 passing tests /
zero failures / zero skips**, with **2,404,896 passing cases / twelve reports**
and thirteen fixed examples. Selection includes all current MOVEC, format8,
word-MOVE restart and `M68010InterpreterTests` cases. No full CPU or consumer
replay is claimed for this test/observer-only change. Fresh Release build has
zero .NET warnings/errors; native extracted code emits conversion, unused-variable
and unreachable-branch warnings on the bounded throw-on-outside-profile transport.

Frozen source starts at `90e3afa55bb56a5db3c580133b30606a84249b3c` plus the
recorded test/observer inputs. Committed production CPU tree remains
`795fd12d6c0237a22a5f92f4a96cc4823e0364c1`.
CPU SHA256: `d3ce6efdd0d54539e788d3eb8b3bc5ca092732352e067470d4ba0e55eccad157`.
Test SHA256: `54dca077898ab6f7e75bced71b4b01f409cf5cee7ae8c70612494766139304c8`.
Extracted instruction SHA256: `23ddb060c814ba5f4c6ee9d79e26ba0a036f0787229c8fdf1f6efa36f473da4f`.
Observer SHA256: `40c73a34417c8238d3472f5e09957cbeba6b13db02734fb89869e232f10caa20`.

```powershell
./scripts/test-copper68k-010-rte-format-discovery.ps1 -OutputDirectory artifacts/010-rte-format-discovery
./scripts/test-copper68k-010-rte-format-discovery.ps1 -ValidateReportsOnly -OutputDirectory artifacts/010-rte-format-discovery
```

Both requested commands fail for the preserved mismatch. This discovery stays
outside ordinary synthetic CI and complete-040 selection. Ordinary requirements
remain 85,191,062 cases / 680 reports, not a fresh broad-suite result. Complete-040
and its 480-case untested inventory remain unchanged. No regression is retired,
production CPU/timing/API changed, private consumer package refreshed or public
package published. Invalid-RTE flag architecture, long/RMW/foreign 010 restoration,
020/030 advanced frames, full CM/fault lifetime, STOP/PCR discrepancies, broader
independent model audits and consolidation remain required. Milestone 6 stays
**in progress**, `roadmapComplete=false`.

### 68010 private long MOVE/MOVEA continuation — 2026-10-06

[MC68000UM](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf)
6.3.9.2, 6.3.10 and 6.4 require continuation from restored internal state rather
than decoding the instruction again. The transfer buffers in figure 6-8 are
16 bits. RR chooses a processor rerun or software-completed transfer, with
DF/IF selecting the read buffer. This supports per-word continuation of a long
operand. It does not specify the silicon's internal word encoding. Locked RMW
has separate whole-cycle semantics; TAS is byte sized and cannot be qualified
using alignment faults. External non-MMU BERR signaling remains unavailable
through the current public bus contract.

The new `M68010LongMoveResumeFrame` image is explicitly **private**, alongside
the existing `C010` word image. Its `C110` marker identifies these internal words:

| Internal words | Meaning |
| --- | --- |
| 0–3 | Version zero, marker, original opcode, read/write direction |
| 4–10 | Extension PC, prefetch address, two prefetched words, queue count |
| 11 | Phase 0/1 source high/low; 2/3 ascending write high/low; 4/5 descending write low/high |
| 12–13 | Accumulated source input or complete source output |
| 14–15 | Original logical operand base |

The stacked fault address identifies only the pending word. A handler redirect
does not change the subsequent word's original address. Mixed RR choices can
therefore produce overlapping destinations; the later word wins byte by byte.
This address convention is a repository choice, not inferred silicon behavior.
Only generated odd-alignment MOVE/MOVEA images are promoted. Unsupported private
images reject before frame consumption; foreign unmarked images retain existing
structural compatibility and remain unqualified for continuation.

The core records a long read fault before the first word and an ascending or
descending write fault before its first word. RTE loads the marked image and
continues word cycles directly. It never repeats source reads, operand decoding
or already committed EA effects. A successful source transfer increments once;
a successful destination transfer commits postincrement or delayed predecrement
once. MOVEA preserves flags; MOVE uses the completed 32-bit source. New capture
hooks run only on odd long accesses. Successful long paths retain their original
access widths/order and timing policy. The pending context clears before further
prefetch/operand work or the next instruction. No public API changes.

`SyntheticM68010LongMoveRestartTests` adds five ordinary reports:

| Group | Scenarios | Architectural combinations |
| --- | ---: | ---: |
| Source faults | 165,888 | 648 |
| Destination faults | 172,032 | 672 |
| Copied/nested/alias/prefetch/trace | 5,120 | 160 |
| User A7 | 512 | 16 |
| Invalid private images | 640 | 20 |
| Total | **344,192** | **1,516** |

Counts describe multi-phase scenarios, not individual bus cycles or xUnit test
counts. Two fixed regressions additionally demonstrate the old missing source
continuation and descending-write continuation. Both failed at the first RTE
before the fix (`artifacts/m6-010-long-restart-before/before.trx`). Seven named
tests now pass without skips. Canonical matrices use nine memory sources, seven
memory destinations, legal register/immediate alternatives, eight long values,
32 CCRs, both privilege states and four independent two-word RR combinations.
They check initial register effects, the next complete frame/register/memory
state, completed instruction state and a following MOVEQ sentinel. Completed
sources are changed by the handler to expose accidental re-reads. Existing
initial fault-flag/PC policy is retained; no new independent silicon oracle is
claimed for initial fault flags or instruction-space bus signaling.

```powershell
./scripts/test-copper68k-010-long-move-restart.ps1 -OutputDirectory artifacts/010-long-move-restart
./scripts/test-copper68k-010-long-move-restart.ps1 -ValidateReportsOnly -OutputDirectory artifacts/010-long-move-restart
```

Accepted scoped evidence is `artifacts/m6-010-long-restart-qualified-current/`.
Source identity is frozen before execution and rechecked afterward. CPU, test
assembly and every report/TRX identity must match. Validation independently
enumerates all combinations and weights and requires the exact seven methods.
Fresh and frozen validations pass. All fourteen controls in
`artifacts/m6-010-long-restart-controls-current/controls.json` reject their intended
identity, selection or report error; altered report hashes are refreshed so
semantic errors cannot hide behind hash rejection. Original evidence is intact.

Register destinations start with distinct values when the source is memory;
the fixture cannot conceal an omitted register write. Preserved SR bits and
MOVEA flags are checked against the independent initial SR. The retained
format-8 structural test now checks fixed `C110` image examples instead of
expecting obsolete zero internal words, while retaining all header, reserved-hole,
surrounding-memory and bus-write checks.

The retained selection passes 21 tests without skips: 133,152 scenarios / eight
reports plus thirteen fixed examples (`artifacts/m6-010-long-restart-retained-final/`).
Six isolated production mutations detect disabled capture, lost source high word,
repeated source increment, wrong remaining address, premature/wrong descending
base commit and lost completed output. Both fixed regressions remain unchanged
after fixture strengthening; each mutation restores source byte-for-byte.
Evidence: `artifacts/m6-010-long-restart-mutations/mutations.json`.

A seventh targeted mutation removes only the memory-source continuation's D2
write. All 18,432 D2-destination scenarios detect it; the other 147,456 source
scenarios remain passing, with every combination independently classified.
This proves that the distinct initial destination values expose the omitted
write instead of allowing a coincidentally correct register value. Source is
restored byte-for-byte and normal CPU/test assemblies remain unchanged.
Evidence: `artifacts/m6-010-long-restart-register-mutation/mutation-identity.json`.

The maintained command below repeats all seven proofs using isolated build
outputs and temporarily mutates the CPU source in the selected checkout.
Do not edit that source or build from it concurrently. The command refuses to
overwrite a concurrently changed source. It checks exact test names and failed
methods, captured mutation source, source/input/binary/evidence identities, and
all 648 register-mutation combinations. Frozen validation fails for incomplete
or altered selections. Its checks are targeted defect proofs, not additional
passing emulator semantics or permission to retire unrelated regressions.

```powershell
./scripts/test-copper68k-010-long-move-mutations.ps1 -OutputDirectory artifacts/010-long-move-mutations
./scripts/test-copper68k-010-long-move-mutations.ps1 -ValidateReportsOnly -OutputDirectory artifacts/010-long-move-mutations
```

Fresh execution and frozen validation pass at
`artifacts/m6-010-long-restart-maintained-mutations-final/`: thirteen exact
executions, comprising twelve fixed regression executions and the complete
165,888-case source matrix under the register-write mutation. Eight altered
profile/selection/source/execution/classification controls reject their intended
error after changed evidence hashes are refreshed. The restoration guard also
restores the expected mutation and rejects a concurrent edit without overwriting
it, using an isolated text fixture. Evidence is
`artifacts/m6-010-long-restart-mutation-controls/controls.json` and
`artifacts/m6-010-long-restart-mutation-controls/concurrency-guard.json`.
The preliminary maintained-command output precedes the exact failed-method
and concurrent-edit guards; it is superseded by this final accepted output.

Pinned external semantic audits pass: SingleStepTests 312,500 cases / 125 files
at `64b253116a3de04aaac4346c43680960dc9b67e5`, and Musashi 536 passing /
88 excluded combinations at `72c1d74800f3087b45a0c1a7342601bbed898881`.
Accepted evidence is `artifacts/m6-010-long-restart-external-qualified/`; it uses
the same production CPU and the earlier test adapter, recorded separately.
These audits do not supply an external long-continuation oracle. AHX retains
18 passing tests without skips (`artifacts/m6-010-long-restart-ahx/`).

The first full CPU run records 5,128 passing, one failing and nineteen skipped
tests (`artifacts/m6-010-long-restart-full/`). Its sole failure was the structural
test's obsolete zero-internal-image expectation described above. This failed
run remains intact and is not accepted as a passing semantic gate. The corrected
full rerun passes 5,129 tests with zero failures and nineteen unavailable tests
(`artifacts/m6-010-long-restart-full-final/`). Exact method/theory/skip selection,
all nine pinned WinUAE input/CPU/adapter identities and the ordinary semantic
gate pass strict verification (`full-identity.json`). Discovery lists 5,116 test
descriptors; one theory expands into 33 runtime rows, giving 5,148 executions.
This run validates 85,535,254 scenarios / 685 reports. It precedes the later EXG
fixture and retirement, which receive separate scoped execution below; it is
not a broad run of that subsequently expanded test source. The source snapshot
in `artifacts/m6-010-long-restart-full-final-inputs.json` was captured while the
run was active, not before execution. Its recorded normal assemblies remained
unchanged throughout the run and subsequent isolated EXG builds.

An isolated CopperScreen consumer at `d9beae8b88be24032221e3482942a249c03c27d3`
passes its Release build, host tests (149 passing / six optional skips), disk
tests (74), separate engine diagnostics (1,080), and three native boot/persistence
replays without native skips. Private unpublished package
`1.5.2-synthetic-dev.64` SHA256 is
`b84e9328c6a74618aaa9d0ed54c13e89019b970496029cd5029bbf9439453bb7`.
Its CPU and all four loaded consumer assemblies match
`0145566d23551d1dd8793784d2d83b5fdbe9a9ad2db6c9199012b6f9af80308a`;
the final scoped test adapter is
`180db7219a99097c9c24d7cae0cff17f5b9aa27798d241861ece75d59f1e8367`.
Consumer evidence is under the isolated checkout's
`artifacts/010-long-restart-validation/` and
`artifacts/010-long-restart-identities.json`. Root CopperScreen changes and its
dependency pin remain untouched. This is correctness evidence, not throughput
or physical timing qualification.

No regression is retired, public package published, hardware timing qualified,
or full format-8 restart claimed. Non-MOVE/foreign/RMW/external-BERR restoration,
rejected-RTE CCR architecture and all other reference/consolidation gaps remain
required. Milestone 6 stays **in progress**, `roadmapComplete=false`.

### EXG wide-register qualification and consolidation — 2026-10-06

M68000PM EXG 4-105 specifies long-register DD/AA/DA exchanges with unchanged
CCR ([manual](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)).
Architectural address registers retain all 32 bits independently of external
bus width. The existing shared register-transfer matrix used An values below
64 KiB, so it did not prove replacement coverage for the old full-long fact.
The added `SyntheticExgRegisterTests` fixture fills that gap without changing
production CPU behavior or consulting production expectation helpers.

| Scope | Scenarios per profile | Combinations per profile |
| --- | ---: | ---: |
| 68000, 68010, 68060: user and ISP | 8,448 | 1,008 |
| 68EC020, 68020, 68030, 68040, A1200: user, ISP and MSP | 12,672 | 1,512 |
| All eight profiles | **88,704** | **10,584** |

Each bank separately enumerates every DD/AA/AD register binding with two wide
value pairs (384 scenarios) and five canonical/alias/A7 bindings with eight
pairs and every CCR (3,840). Register snapshots independently determine results,
including self aliases. Checks cover full architectural values, unchanged
SR/CCR, exact PC, every untouched register/inactive stack bank, guarded memory,
no operand bus transfers or exception entry, and a following NOP. Counts are
scenarios, not twice-counted sentinel phases. Three fixed encodings include
`C54E`, the historical exchange word.

```powershell
./scripts/test-copper68k-exg.ps1 -OutputDirectory artifacts/exg-wide
./scripts/test-copper68k-exg.ps1 -ValidateReportsOnly -OutputDirectory artifacts/exg-wide
./scripts/test-copper68k-exg-mutations.ps1 -OutputDirectory artifacts/exg-mutations
./scripts/test-copper68k-exg-mutations.ps1 -ValidateReportsOnly -OutputDirectory artifacts/exg-mutations
```

Fresh and frozen positive qualification pass eleven exact executions without
skips at `artifacts/m6-exg-wide-qualified-final/`. Source inputs are captured
before execution and checked afterward. CPU, test, report/TRX/log identities,
model/group selection and every independently enumerated key/weight must match.
Isolated CPU SHA256 is
`f71d94abe6be78af6eb966c605fdde04fac79f5fdfa745714f1d68a4a3b139c9`;
test adapter is
`3e8779eda6a8740c5e75278652e0e4c2b3474506afbd2456f600d6f5c26b99bd`.
Isolated build identities differ from the recorded normal/consumer outputs;
they do not imply different production source behavior.

Only `M68kInterpreterCoreBehaviorTests.ExgAddressRegistersSwapsFullLongValues`
is retired. Its exact replacement is
`68000/EXG.L/AA/bank=ISP/r6->r2/boundaries/pair=0/op=C54E/ccr=00`,
with the same opcode and values, supervisor SR `2700` and stronger preservation
checks. Both full 68000 matrix and historical fact detect both targeted mutations:

| Mutation in AA exchange | Mismatching / passing replacement scenarios |
| --- | ---: |
| Truncate both results to a word | 2,096 / 6,352 |
| Read the already-overwritten register instead of latched input | 1,568 / 6,880 |

Pre-retirement positive evidence passes twelve tests at
`artifacts/m6-exg-wide-before-retirement-final/`. Pre-retirement proofs at
`artifacts/m6-exg-wide-mutations-before/` execute two failing tests per mutation;
post-retirement proofs at `artifacts/m6-exg-wide-mutations-after/` execute one.
Each command requires every one of 1,008 keys with the expected classification,
all failure identifiers and the exact historical witness. Fresh and frozen
validation pass before and after retirement. The command captures mutation
source, restores original bytes and rejects concurrent edits without overwriting
them. Normal CPU/test assemblies retain the identities recorded above for the
full run. No unrelated regression is retired.

Twelve controls in `artifacts/m6-exg-wide-controls-final/controls.json` reject
wrong profiles, empty input/mutation selections, missing reports, foreign test
selection, wrong keys/weights, altered mutation source, wrong classifications
and a missing historical witness. Altered evidence hashes are refreshed so
semantic checks must reject the content itself. Original evidence is retained.
The preliminary scripted positive run failed a test-name array-construction
check despite passing emulator tests; the preliminary controls stopped on a
single-result XML indexing error. Both are excluded and superseded by the final
outputs. Frozen positive validation attempted while mutation source was active
correctly rejected the changed input; it passes after source restoration.

The ordinary gate now requires **85,623,958 scenarios / 693 reports**. Combined
report validation in `artifacts/m6-exg-wide-combined-reports/` uses the prior
strictly verified 685-report full run plus eight freshly qualified EXG reports;
it is not a fresh full-suite run of the changed test source. The historical
full run, long-continuation audits and pre-retirement proofs retain their original
input identities; they are not rehashed to pretend to execute later source.
No new consumer execution or package publication is needed for this test-only
slice. The complete-040 inventory's 480 untested cases, all broader restoration
and software disagreements, independent coverage and further consolidation
remain required. Milestone 6 stays **in progress**, `roadmapComplete=false`.

### 040 supplied write-back handler qualification — 2026-10-06

[MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
8.4.6.3/5/7, tables 8-5/6, define the frame's write-back validity, transfer size,
function code, alignment and handler obligations. The audited local manual
(`artifacts/mc68040um-1993-entry-audit.pdf`) SHA256 is
`93741393f70656941e413beb232c060a5e4e0218ea2adc2f5b392f9165454a7f`.
WB1 bytes occupy memory lanes; WB2/WB3 byte/word values occupy low register bits.
Software must complete pending stores in WB1→WB2→WB3 order before returning.
RTE does not perform these stores for the handler.

`SyntheticM68040WritebackProgram` executes a generic integer handler: save
D0–D3/A0–A1, retain the supplied frame pointer and original DFC, inspect each
valid bit, load its data/address, select DFC, normalize WB1 lanes, select B/W/L
MOVES, clear that valid bit, restore DFC/registers and RTE. It branches on actual
frame bytes. Instruction transitions, flags, registers, all stack banks, memory
and bus writes are checked independently after every step. Following BRA and
trace-handler RTE check restored T1/T0, SR, exception provenance and stacks.
Invalid slots must not read their pointer/data fields. Overlaps expose store
ordering; final address/size/data/order expectations come from intended operands,
independently of the program's normalization calculations.

| Group, each scalar/batch route | Program scenarios | Combinations | Weight |
| --- | ---: | ---: | ---: |
| Canonical | 36,864 | 1,152 | 32 CCRs |
| Structural | 36,864 | 18,432 | CCR 0/31 |
| Both groups, both routes | **147,456** | **39,168** | |

Canonical cases cover both handler banks, four restored banks, all three slots,
B/W/L sizes, four address lanes and four value images. Structural cases cover
all eight valid masks, four uniform/mixed-size patterns, distinct/equal/overlapping
destinations, four address lanes, both frame alignments, two value images and
restored trace 0/T1/T0. Handler trace is clear. Initial/restored CCRs differ;
scratch registers and original DFC must survive. FPCR/FPSR/FPIAR are preserved,
but no floating-point operation or context transfer is performed. Counts describe
whole programs; checked instructions and following sentinels are not counted again.

Supplied headers follow table 8-6: valid WB1 implies a normal write with
FA=WB1A; WB2 without WB1 uses a supplied write-page-fault image; reads have
neither WB1 nor WB2. Invalid statuses are zero. The earlier test-only draft used
read status for all masks and is excluded from architectural qualification.
These frames are initialization, not CPU-generated data faults. Translation
and caches are disabled during handler execution; the test does not execute an
enabled-MMU page fault or qualify its construction.

```powershell
./scripts/test-copper68k-040-writebacks.ps1 -OutputDirectory artifacts/040-writebacks
./scripts/test-copper68k-040-writebacks.ps1 -ValidateReportsOnly -OutputDirectory artifacts/040-writebacks
./scripts/test-copper68k-040-writeback-mutations.ps1 -OutputDirectory artifacts/040-writeback-mutations
./scripts/test-copper68k-040-writeback-mutations.ps1 -ValidateReportsOnly -OutputDirectory artifacts/040-writeback-mutations
```

Accepted fresh/frozen qualification at `artifacts/m6-writeback-qualified-final/`
passes all fifteen exact executions without skips: four report batches, nine
fixed encoding/alignment/header examples and two bounded program witnesses.
Source inputs are captured before execution and checked afterward; source,
binary, report/TRX/log identities and exact names/keys/weights must match.
Isolated CPU SHA256 is
`bd11ad1ad39668a80582d4458fe707be63c44880d15b2a380950b2c3edb17caf`;
test adapter is
`e17c25b909f390cfd718796eaae4a9eeb338ae3f60a004581f7684a09df234e1`.
These isolated build identities differ from previous normal/consumer assemblies;
production CPU source and normal assemblies are unchanged.

Two maintained test-handler mutations qualify discrimination at
`artifacts/m6-writeback-mutations-final/`. A two-test baseline passes. Replacing
WB1's lane rotation with NOP fails both witnesses at the checked rotation
transition; reversing stage order fails only the overlapping mixed-width witness,
whose independent final store-order check catches the defect. Exact failures,
methods and reasons are required, not merely a nonzero test exit. Mutation source,
input, CPU/test and TRX/log identities are captured; original program bytes and
normal assemblies are restored/preserved. This mutates the test handler, not the
CPU, and supplies no historical CPU-defect or regression-retirement claim.

Eight controls at `artifacts/m6-writeback-controls-final/controls.json` reject
wrong profile, empty/duplicate inputs, missing reports, foreign test selection,
substituted keys, redistributed weights and hidden unsupported execution.
Seven mutation controls at `artifacts/m6-writeback-mutation-controls/controls.json`
reject wrong profile, empty inputs/mutations, altered source, wrong failed method,
wrong failure reason and wrong execution status. Changed evidence hashes are
refreshed, requiring semantic rejection. The isolated `concurrency-guard.json`
proves expected restoration and refusal to overwrite a concurrent edit.
The first run's absolute exception-counter expectation was a fixture error
(Reset retains that counter); its failed evidence is preserved and excluded.

Ordinary requirements become **85,771,414 scenarios / 697 reports**. Combined
validation at `artifacts/m6-writeback-combined-reports/` uses the preceding
strictly verified full run, separately qualified EXG reports and these four new
reports. It is not a fresh broad run of the expanded source. Complete-040
requirements now contain **67,793,440 cases / 86 reports and 24 fixed
controls/witnesses**, with exactly 110 discovered executions. Static requirement
verification is recorded in `artifacts/m6-writeback-complete-audit-requirements.json`;
the new complete-audit enumeration branch separately matches every qualified key
and weight (`artifacts/m6-writeback-complete-audit-key-check.json`). No passing
fresh complete-040 execution is claimed.

Fresh inventory execution at `artifacts/m6-writeback-remaining-inventory/`
fails its one exact method without skips, retaining **480 untested cases /
fifteen combinations**, independently verified. Its write-back entry now states
this partial coverage accurately. Nested WB2/3 faults, cache pushes/MOVE16 line
cleanup, actual data-fault creation/restart, physical function-code spaces and
full handler/reference/hardware qualification remain required. Other advanced
restoration and software disagreements are unchanged. No production fix,
regression retirement, new consumer replay or package publication is included.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

## 040 actual MOVE write-fault qualification — 2026-10-06

The 040 interpreter previously stacked a short generic bus-error frame when a
normal MOVE destination write failed. Its source/extensions were already consumed,
but the frame lacked the pending data needed to finish the store. Production now
captures the actual store address/value/width, completes the destination base and
MOVE flags, and stacks the consumed following PC in a 60-byte format-7 frame.
WB1 contains valid SIZE/TM, FA=WB1A and memory-aligned WB1D. WB2/WB3 are invalid;
undefined fields use the existing zero convention. Incoming T1 sets CT and the
instruction EA; MOVE does not trigger T0. Expectations follow
[MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf), 8.4.6,
8.4.6.2, 8.4.6.5/table 8-5 and 8.4.6.7. Retained local manual SHA256:
`93741393f70656941e413beb232c060a5e4e0218ea2adc2f5b392f9165454a7f`.

Eligibility is limited to legal ordinary MOVE operand writes rejected by the
physical address map, with translation disabled and the same instruction and
exception boundary. The fallback's descending word transfers retain their
existing order/widths; an internal context preserves the full original long
operand in the fault. No operand is decoded or executed again. The boundary
guard prevents a trace-frame store fault from completing an already-retired
MOVE a second time. Handler entry, double-fault routing and existing timing
policy are retained. No public API changes are made.

Independent MOVE fixtures choose final registers, operand address/value and
following PC. Actual physical-map rejection enters the handler; no supplied
fault exception substitutes for the instruction. The shared integer handler
normalizes WB1, executes MOVES, clears validity, restores registers/DFC and RTEs.
Every instruction checks state and memory. The final store is compared with the
independently intended operand, not with production frame data. Defined frame
fields/data lanes are independently checked first; undefined bytes then become
opaque handler inputs. A completed fallback low-word store is checked separately
and preserved. T1 cases exercise format-2 trace conversion, trace-handler RTE and
the following traced sentinel; T0 cases verify normal MOVE continuation.

| Group, each scalar/batch route | Programs | Combinations | Weight |
| --- | ---: | ---: | ---: |
| All legal memory-destination MOVE opcodes | 7,350 | 7,350 | 1 |
| Lanes/sizes/values/CCR/stack banks | 9,216 | 288 | 32 CCRs |
| Full-index structures/aliases/trace | 14,256 | 14,256 | 1 |
| Both routes | **61,644** | **43,788** | |

Boundary cases cover B/W/L, four lanes, six values, all CCRs, user/user-M/ISP/MSP
and A7 byte stride. Indexed cases cover all 66 legal full-format structures,
sizes, register 0/7, four banks, trace 0/T1/T0 and source/destination/dual forms.
Opcode cases include source/destination register aliases. Six bounded executions
protect postincrement widths, aliased predecrement, indexed CT and trace-frame
fault routing. The trace-frame witness qualifies preservation/routing only;
its full nested exception-frame/recovery protocol remains open.

Maintained commands:

```powershell
./scripts/test-copper68k-040-operand-writes.ps1 -OutputDirectory artifacts/040-operand-writes
./scripts/test-copper68k-040-operand-writes.ps1 -ValidateReportsOnly -OutputDirectory artifacts/040-operand-writes
./scripts/test-copper68k-040-operand-write-mutations.ps1 -OutputDirectory artifacts/040-operand-write-mutations
./scripts/test-copper68k-040-operand-write-mutations.ps1 -ValidateReportsOnly -OutputDirectory artifacts/040-operand-write-mutations
```

Fresh/frozen qualification at `artifacts/m6-real-write-qualified-final-v3/` passes
all twelve exact executions without skips. Inputs are captured before execution;
source, binary, evidence, exact selection and independent keys/weights must match.
Seven maintained CPU mutations at `artifacts/m6-real-write-mutations-final/` detect
lost original width, omitted predecrement, wrong bus lanes, lost data, missing CT,
missing N and missing exception-boundary qualification. The six-test baseline
and seven six-test mutants produce 48 exact executions; expected methods/reasons
are checked. Original source bytes and normal assemblies are restored/preserved.
No old regression is retired.

Eight positive-evidence and seven mutation-evidence controls are recorded in
`artifacts/m6-real-write-controls-final/controls.json` and
`artifacts/m6-real-write-mutation-controls-final/controls.json`. They reject
wrong profiles, empty/duplicate inputs, missing reports, foreign executions,
substituted keys, redistributed weights, hidden unsupported results and altered
mutation source/method/reason/status. Evidence hashes are refreshed before
semantic rejection. The isolated `concurrency-guard.json` verifies restoration
and refusal to overwrite a concurrent edit.

The fresh full run at `artifacts/m6-real-write-full-qualified/` passes **5,166
tests / zero failures / nineteen explicitly unavailable tests**, total 5,185,
in 48 minutes 37 seconds. Independent verification matches the 5,153 discovered
methods, the retained 33-row theory expansion, exact nineteen skip names and all
nine pinned WinUAE presets. The ordinary gate validates **85,834,306 scenarios /
703 reports**. Source/fixture/script identities are captured before execution and
checked afterward; normal CPU/test binaries remain unchanged. `before.json`,
`after.json`, `full-identity.json` and `summary.json` preserve the evidence.
Final isolated CPU SHA256:
`00dece901073a2918cb370410bd049f8f39bddbbb27890a133fd2d3f72a14604`;
test adapter:
`ac030d21cbf4fd2820723ff7b81f9c43f1beb734e5bf08ca369abe8b3d2ad941`.
WinUAE generator pin `025b999239800357e95065fe5b9a15ea5b300fa7` and runner pin
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`, native/input/source fingerprints and
callback/frame counts match the retained qualification presets. These selected
software comparisons do not qualify a physical write-fault pipeline. The records
below are documentation added after that verified execution; CPU/test/script
source is unchanged.

Final software-semantic audits at `artifacts/m6-real-write-external-final/` use
the same CPU/test assemblies as the final full run. SingleStepTests pin
`64b253116a3de04aaac4346c43680960dc9b67e5` passes 312,500 cases / 125 files.
Musashi pin `72c1d74800f3087b45a0c1a7342601bbed898881` passes 536 combinations,
with 88 explicitly excluded from 624 rows. These references do not provide an
external physical 040 write-fault/frame or timing oracle.

The immutable unpublished `1.5.2-synthetic-dev.66` package has SHA256
`d33a5b2c55a44acfa3370666351f6eb5ec6a5b76e04f29d5b1ceec8e4e22d24f`.
The isolated CopperScreen consumer builds Release with zero warnings/errors;
host tests pass 149 with six optional unavailable tests, disk 74, separate engine
diagnostics 1,080, and native boot/persistence replays all three without skips.
Four restore assets select `.66`; all four loaded CPU DLLs match packed CPU SHA256
`1ded1424442ebc6f58145ddfc86440d6949bd97e2872731505c630aee177d33e`.
`artifacts/m6-real-write-consumer-final-identity.json` records package/assets/DLLs,
TRX identities and native inputs. Pinned native SHA256 values:

- Kickstart 3.1 A500: `8c8a0cf04f91b88eaf0c4f1126041987067e2286a8ee590bdbae447a8000c5ee`.
- Workbench 3.1 ADF: `a8f167bad2897e8c7f2cfe73de7a9f8dad10c7a5b1f78ca17f818ab17278b985`.
- Kickstart 3.0 A1200 ZIP: `eb63ba9ceff1ac12bb025389434946101ae71dd064b8a830e8ae8fe111c1be39`.
- Pristine AGA probe HDF: `3bbab58c135844aad802ce937d49ec5da26d6407303c9d9fc543fc6cc63e9c85`.

The two Workbench rows use 0/2 MiB Fast RAM; the A1200 row checks eight-plane
display and persistence through desktop reopen. Media stays local. Earlier
private `.64`/`.65` packages remain immutable. Root CopperScreen changes and its
published dependency pin are untouched; no public release is performed.

Ordinary requirements are **85,834,306 scenarios / 703 reports**. Complete-040
requirements are **67,856,332 scenarios / 92 reports and 30 fixed witnesses**,
with exactly 122 discovered executions. Static requirements and new key-branch
checks are recorded in `artifacts/m6-real-write-complete-audit-requirements.json`,
`artifacts/m6-real-write-complete-audit-key-check.json` and
`artifacts/m6-real-write-complete-discovery.json`. No passing complete-040 run is
claimed. Fresh remaining-inventory execution intentionally fails one exact
method for **480 untested cases / fifteen combinations**, retaining every prior
category (`artifacts/m6-real-write-remaining-inventory/verified-inventory.json`).
Actual normal MOVE writes are now partial coverage within that inventory;
other reads/integer/MOVEM/MOVE16, nested writebacks, mixed-epoch repair, CP/CM and
trace-entry recovery remain required. Enabled MMU, FPU arithmetic, physical
pipeline/cache timing and OS compatibility remain outside this roadmap.

Initial bounded failures, incomplete matrix drafts, failed mutation expectations
and two stopped full attempts remain separate historical evidence under
`artifacts/m6-real-write-*`. One stopped full attempt exposed the obsolete short
MOVE-frame expectation; the next was superseded by the trace-entry boundary fix.
Neither is a complete-suite success. Final manifests are not applied retroactively
to them. Milestone 6 remains **in progress**, `roadmapComplete=false`.

## 68030 integrated-MMU user privilege qualification — 2026-10-06

The unchanged pinned WinUAE Basic corpus exposed a real 68030 exception-priority
defect: user-mode CpID-0 words raised Line-F instead of privilege violation.
[MC68030UM part 2](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf),
8.1.5/6 and 9.8, requires privilege violation in user mode even for undefined
integrated-MMU patterns. The CPU now checks this before extension/operand handling
and Line-F delivery. Other models, supervisor-mode invalid words and external
coprocessor handling retain their existing behavior and timing policy.

`SyntheticM68030PmmuPrivilegeTests` passes **263,168 whole-program scenarios /
9,216 independently checked report keys** in four scalar/batch reports, with **47 fixed
controls**, all 51 exact executions passing without skips. The opcode group covers
every first word F000–F1FF. Separate status groups combine eight primary formats,
eight secondary-word boundaries, every defined user SR image (trace, M, IPL and
CCR) and both execution routes. The fixture checks complete format-0 frames,
vector provenance, registers, SFC/DFC, stack banks and surrounding memory. Entry
must not read operand data. A literal integer handler edits the stacked PC to
skip both fixture words, then RTE restores state and a MOVEQ sentinel verifies
continuation and applicable trace delivery. This software skip does not define
PMMU instruction length or retry a partially executed instruction.

The retained Line-F matrix had 4,096 obsolete 030 expectations; its expectation
now includes the documented integrated-MMU priority. All 25 retained tests pass. The general system matrix also corrects 32 user-mode F1C0 expectations; its eight-profile selection passes.
No old regression is retired. Four maintained production mutations prove that
the controls detect missing priority and incorrect CpID/model/mode boundaries;
baseline plus mutations execute **235 tests** with exact failing names/reasons.
Original CPU bytes and protected normal assemblies are preserved. Eight report
integrity controls reject profile/input/report/key/weight/status corruption. Seven mutation-evidence controls reject their intended errors; an isolated restoration guard preserves concurrent edits.

```powershell
./scripts/test-copper68k-030-pmmu-privilege.ps1 -OutputDirectory artifacts/030-pmmu-privilege
./scripts/test-copper68k-030-pmmu-privilege.ps1 -ValidateReportsOnly -OutputDirectory artifacts/030-pmmu-privilege
./scripts/test-copper68k-030-pmmu-privilege-mutations.ps1 -OutputDirectory artifacts/030-pmmu-privilege-mutations
./scripts/test-copper68k-030-pmmu-privilege-mutations.ps1 -ValidateReportsOnly -OutputDirectory artifacts/030-pmmu-privilege-mutations
```

The commands require exact source/binary/evidence selections and independently
enumerated architectural keys and weights. Original focused evidence was written under
`artifacts/m6-pmmu-privilege-qualified-final`,
`artifacts/m6-pmmu-privilege-mutations-final-v2` and
`artifacts/m6-pmmu-privilege-controls` and `artifacts/m6-pmmu-mutation-controls-v3`. See the final validation note for the subsequent cleanup and regenerated records. Earlier incomplete matrix identifiers and
incorrect mutation expectations remain excluded historical evidence.

The unchanged broad Basic audit improves from **1,326 passing / 47 mismatching /
8 unsupported directories** to **1,327 / 46 / 8**, with no untested directories;
the 030 MMUOP030 directory passes 4,178 callbacks. Both broad runs intentionally
fail their remaining selections. Total callbacks (11,474,194 before and
11,478,371 after) include partially executed failing directories and are not
all-passing architectural coverage. The before/after records are
`artifacts/m6-basic-current-audit` and `artifacts/m6-basic-pmmu-after-audit`.
Both use generator `025b999239800357e95065fe5b9a15ea5b300fa7`, runner
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`, manifest SHA256
`37cd8ddae8b61ac60e3a49ba835362fbf80d418f48c2539338e6541c9d85e31f`
and native library SHA256
`75f0c11352d4a8c1e84f32a49a5347ae4cb5bd9128f1501a33d843e6f99cb057`.
No failing Basic family is silently excluded to make this audit pass.

Ordinary requirements become **86,097,474 scenarios / 707 reports**.
Complete-040 requirements remain 67,856,332 scenarios / 92 reports plus 30 fixed
witnesses, with the required 480 untested cases / fifteen combinations retained.
No passing complete-040 audit is claimed. Legal supervisor PMMU semantics,
advanced exception restoration and remaining milestone-6 protocols stay open;
enabled MMU translation, FPU arithmetic and physical timing remain outside the
roadmap. Nonzero CpID controls here cover general coprocessor words, not every
external state-transfer privilege rule. Milestone 6 remains **in progress**,
`roadmapComplete=false`.


#### Final validation and concurrent artifact cleanup

A concurrent cleanup removed ignored reports and pinned external inputs during
the full run. The retained TRX records **5,208 passing / nine failed / nineteen
optional skipped tests**, total 5,236. All nine failures are
`DirectoryNotFoundException` for deleted WinUAE preset manifests, not semantic
mismatches. This is **not a green full run** and those audits are unavailable
until their pinned inputs are restored. No missing audit is relabeled passing.

The actual retained TRX contains all **707 deterministic batch summaries /
86,097,474 scenarios**. Independent verification matches every model/group,
expected case count, passing outcome and zero mismatch/unsupported/untested
counts against the maintained gate requirements. This recovers execution/count
evidence; it does not recreate deleted JSON key details or claim the original
JSON report gate completed. The preserved record is
`C:/Users/ilkle/AppData/Local/Temp/copper68k-030-pmmu-20261006/full-verification.json`,
with the original TRX and extracted summaries beside it.

After cleanup stopped, both maintained PMMU commands were rerun under that
temporary root: `qualification` passes all 51 executions / 263,168 scenarios /
9,216 independently checked report keys; `mutations` proves all four defects in
235 executions, preserving original CPU bytes and normal assemblies. Their
complete fresh source/binary/evidence manifests supersede the deleted local
focused records. Earlier report/mutation integrity controls and the independent
Basic audit were observed before cleanup; their original local records are no
longer available and are not retrospectively recreated.

Before cleanup, pinned SingleStepTests passed 312,500 cases / 125 files and
Musashi 536 combinations with 88 explicit exclusions against final full-build
CPU SHA256 `43fffbd564c89c1ec98d4d2e1e1793c47a2435a157a4a0809a9bd6a9f8369514`
and test SHA256 `2a9af80b3c770db7fb93d53d228dc4a66e55a0bd65dbccbab66f55f6a2ef8a92`.
These remain observed execution results; their deleted local manifests are not
claimed to be present.

The immutable private `1.5.2-synthetic-dev.67` package remains present with SHA256
`fed1a90ced4a7a11429bbfc1a0ddfe33fd38cf4aded33332c9d1dc31e759e6ea`.
Before cleanup, its CopperScreen Release build completed without warnings/errors;
host 149 passing / six optional skips, disk 74, separate engine diagnostics 1,080
and all three native replays passed. Four restore assets and loaded DLLs matched
packed CPU SHA256 `ebc62e354bd95cb85ad7d8809775bd7db8dba66739b371b0eb6cb51e8d776006`.
Cleanup subsequently removed the consumer checkout and local replay inputs;
these results are not described as fresh post-cleanup replays. Source and the
published dependency pin remain untouched. No public package is published.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

#### Restored scoped WinUAE qualification — 2026-10-06

After cleanup stopped, fresh source checkouts at generator
`025b999239800357e95065fe5b9a15ea5b300fa7` and runner
`7a83745d6c6159bc74ab0471578ffc8bc244e66e` regenerated all nine deleted
qualified presets through `prepare-copper68k-winuae.ps1`. Tracked reference
sources remain unchanged. The maintained audit commands pass their complete
selections, including required corruption controls and architectural form
distributions. Their existing Motorola-qualified patches and reference caveats
remain applicable; these are software reference results, not silicon timing
qualification or an unchanged upstream oracle.

| Preset | Passing xUnit executions | Model/family directories | Callbacks | Exception frames |
| --- | ---: | ---: | ---: | ---: |
| TrapBounds | 1 | 21 | 951,522 | 476,339 |
| Breakpoints | 1 | 7 | 224 | 224 |
| LongArithmetic | 19 | 12 | 28,418 | 4,992 |
| WordDivision | 15 | 16 | 134,928 | 37,804 |
| LowPowerStop | 11 | 1 | 245,760 | 245,760 |
| Moves | 17 | 21 | 110,492 | 86,072 |
| Cas | 16 | 18 | 49,284 | 2,466 |
| Cas2 | 12 | 12 | 3,180 | 2,296 |
| CacheEncodings | 13 | 8 | 2,048 | 2,048 |
| Total | 105 | 116 | 1,525,856 | 858,001 |

All nine commands have zero failures or skips; all reports have zero
mismatching, unsupported or untested selected directories. The 105 executions
include nine native audit facts and 96 encoding controls. Callbacks include
distinct model/profile executions and are not a count of unique instruction
encodings. The fresh evidence is under
`C:/Users/ilkle/AppData/Local/Temp/copper68k-reference-restoration-20261006`:
`inputs/<Preset>` contains each pinned input manifest, native bridge and source
identities; `audits/<Preset>` contains TRX, report and isolated assemblies.
`verify-restoration.ps1` independently checks exact totals, passing outcomes,
pins, input bytes/hashes, report/manifest/native/assembly identities, row controls
and unchanged normal assemblies. `restoration-verification.json` records those
identities. New native binaries and manifests have their own hashes; no deleted
evidence is relabeled or reconstructed as identical.

Both maintained audit wrappers now pass `--artifacts-path`, defaulting to
`<OutputDirectory>/build`; optional `-ArtifactsPath` selects another isolated
build directory. CPU/test normal assembly hashes remain
`0145566d23551d1dd8793784d2d83b5fdbe9a9ad2db6c9199012b6f9af80308a` and
`180db7219a99097c9c24d7cae0cff17f5b9aa27798d241861ece75d59f1e8367`.
This slice changes audit output placement and documentation only, with no
production CPU or package changes.

These fresh runs resolve the nine missing-input failures independently. The
earlier full run remains failed, its deleted deterministic JSON details remain
unavailable, and no new green full-suite run is claimed. SingleStepTests,
Musashi, broad Basic and consumer/native evidence retain the preceding cleanup
limitations. Remaining architectural gaps and consolidation work are unchanged;
milestone 6 remains **in progress**, `roadmapComplete=false`.

## Reference-only audit command

`scripts/test-copper68k-reference-audits.ps1` runs the existing independent
adapters without rerunning the deterministic synthetic matrix. It requires at
least one requested reference, complete pinned inputs and a fresh output
directory; every requested test must execute and pass. Builds default to
`<OutputDirectory>/build`, with optional `-ArtifactsPath` for another isolated
directory. Source revisions, input bytes/SHA256, command identity, report/TRX
and isolated CPU/test assembly hashes are retained. The command checks complete
per-file SingleStepTests coverage and per-model/program Musashi coverage,
including their documented exclusions, then verifies inputs stayed unchanged.
It rejects empty selections, wrong pins, missing inputs and orphan WinUAE
source arguments. It never publishes a package or alters comparator masks.

```powershell
./scripts/test-copper68k-reference-audits.ps1 `
  -SingleStepPath <pinned-SingleStepTests-checkout> `
  -MusashiPath <pinned-Musashi-checkout> `
  -OutputDirectory <fresh-output>

./scripts/test-copper68k-reference-audits.ps1 `
  -WinUaePath <prepared-Basic-inputs> `
  -WinUaeGeneratorSource <pinned-generator-checkout> `
  -WinUaeRunnerSource <pinned-runner-checkout> `
  -OutputDirectory <fresh-output>
```

The second command deliberately fails while the Basic discrepancies remain.
Its full diagnostics are retained; no passing verification manifest is emitted
for a failed selection. The native adapter still performs its complete profile,
directory, binary header, input identity and corruption-probe checks. Basic
requires the explicit `Basic` preset manifest from the maintained preparation
command; other scoped presets cannot substitute for broad qualification.

### Restored broad reference evidence — 2026-10-06

Fresh pinned SingleStepTests and Musashi checkouts pass the reference-only
command's complete selections: **312,500 cases / 125 files** and **536 passing
model/program combinations / 88 explicit exclusions**, respectively. Both
facts execute without skips. `audits/IndependentFinal/reference-inputs.json`
and `reference-verification.json` under the restoration temporary root above
record all input bytes/hashes, pins, command and report/TRX/assembly identities.
Each SingleStepTests file passes exactly 2,500 cases; every Musashi profile has
the documented passing/excluded program distribution. Existing trace-boundary,
invalid-input, unavailable-instruction and software-reference caveats remain.
Four preflight controls reject empty selection, wrong source revision, absent
inputs and orphan WinUAE source arguments in
`reference-command-controls-final.json`.

The broad Basic corpus is also freshly regenerated at the same generator/runner
pins. Its manifest SHA256 is
`b5741ca90fa75d3510ef25bac33d2d36d6a92ab2283f0666e762a5f1853a38ed`,
native bridge SHA256
`135d8c7d01137bbef2467e34207444984fd47a9076041a0c251848d2518b32ce`.
The audit reproduces **1,327 passing / 46 mismatching / eight unsupported /
zero untested directories**, 11,478,371 callbacks and 1,668,069 exception-frame
checks. These include partial failing directories, not all-passing coverage.
All eight profiles execute their complete directory selection and register,
defined-SR, exception-frame and ignored-undefined-SR controls. The requested
command fails, retaining original diagnostics. No comparator mask, CPU behavior,
Basic input or family selection is changed to obtain agreement.

The 54 non-passing model/family rows remain explicit:

| Family | Non-passing rows | Current disposition |
| --- | ---: | --- |
| BKPT | 7 | Saved-PC software disagreement; separate qualified Breakpoints preset passes. |
| CHK2 B/W/L | 15 | Saved-PC software disagreement; separate qualified TrapBounds preset passes. |
| TRAPcc | 6 | Saved-PC software disagreement; separate qualified TrapBounds preset passes. |
| DIVL.L / MULL.L | 10 | Eight emulator-unsupported Basic inputs plus two 060 saved-PC disagreements; canonical LongArithmetic qualification passes separately. |
| DIVU.W | 4 | Overflow flag software disagreement; WordDivision qualification passes separately. |
| MOVES.B / MOVES.L | 3 | Reserved extension fields/undefined same-An stores; legal-input Moves qualification passes separately. |
| CAS.W / CAS.L | 2 | 060 saved-PC disagreement; separate Cas qualification passes. |
| CAS2.W | 1 | 040 compare alias software disagreement; separate Cas2 qualification passes. |
| LPSTOP | 1 | Privilege-fetch/saved-PC software disagreement; separate LowPowerStop qualification passes. |
| ILLEGAL | 2 | 040/060 F400 cache encoding software disagreement; separate CacheEncodings qualification passes. |
| RTE | 1 | 010 format-error CCR disagreement remains required. |
| STOP | 1 | 060 supervisor-bit-cleared disagreement remains required. |
| MOVE16 | 1 | 060 Basic failure uses noncanonical extension 6000; legal-input independent qualification remains required. |

The recovered MOVE16 diagnostic is `F626 6000`, user SR 0000, source/destination
A6 `0087FE9F`. The reference increments A6 while Copper68k raises vector 4.
[M68000PRM MOVE16](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf)
4-127 requires extension bit 15 set and bits 11..0 clear; the canonical same-A6
form is `F626 E000`. The pinned generator instead reads only extension bits
14..12 and does not enforce bit 15. This establishes a noncanonical input,
not authority for a CPU change or a hardware-defined outcome for every reserved
extension. A canonical-input MOVE16 audit is the next reference qualification
slice; original Basic failure remains retained.

This closes the local availability gap for SingleStepTests, Musashi and Basic
diagnostics with fresh identities. It does not recreate deleted deterministic
JSON details or consumer/media inputs, reclassify the earlier failed full run,
or close advanced frame/recovery and consolidation requirements. Production CPU,
normal assemblies and packages are unchanged. Milestone 6 remains **in
progress**, `roadmapComplete=false`.

### Canonical MOVE16 reference qualification — 2026-10-06

The maintained `Move16` preset independently executes all five documented
MOVE16 forms on 68040 and 68060. Preparation patches a copy of the pinned
WinUAE generator: set the canonical post-post extension before effective-address
construction, serialize valid address-register inputs before reference execution,
and enumerate all eight destination registers in sixteen low-nibble rounds.
Production decoding/execution and the original Basic corpus remain unchanged.
The architectural basis is [M68000PRM 4-125..127](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf).

```powershell
./scripts/prepare-copper68k-winuae.ps1 -Preset Move16 -GeneratorSource <pinned-generator> -RunnerSource <pinned-runner> -VcVars64 <vcvars64.bat> -OutputDirectory <fresh-inputs>
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Move16 -InputDirectory <fresh-inputs> -OutputDirectory <fresh-audit>
```

The pins remain generator `025b999239800357e95065fe5b9a15ea5b300fa7` and runner
`7a83745d6c6159bc74ab0471578ffc8bc244e66e`. Normalized copied generator SHA256 is
`b201915e70fa2c7cc7c87cbfef588eb910bf3e655ea02bbff0955e42bc4e2e55`;
patch SHA256 `c23761b715a1eb8abc01f4293710ffda73bb48f9a4867f1d0087c462eaa7ed12`.
The final input manifest SHA256 is
`244611bbd2dd6afcc81c192e8c9478211651a251da106887c73c2692da5b5846` and native bridge
SHA256 `819bbafbeba54f065a8eed396502dd98094cd90e7ea5aac6672cd5ae2a7bc1e0`.

Fresh acceptance passes **22 executions / zero skips**, with **83,712 callbacks**
(41,856 per model), no exception frames or masked SR bits, and zero selected
mismatches/unsupported/untested rows. There are **25,260 weighted form keys per
model**, 50,520 summed across profiles. Each model's ordinal `key=weight\n`
distribution hashes to
`143e6c025a7451664853b10bfe94651dae28860263dad370a98ad6a33c77d236`.
Keys distinguish five forms, registers, aliases, line overlap, low address bits
and initial SR. Register, defined-SR and actual destination-memory corruption
are detected independently on both models. Fixed manual encoding examples,
rejected foreign/reserved encodings, weight redistribution and missing-reference
classification execute without optional inputs.

The native selection covers all 64 post-post register pairs in user mode and
49 non-A7 pairs in supervisor mode, with CCR 0/31. Absolute forms cover all eight
address registers in user mode and seven non-A7 registers in supervisor mode.
An independent 384-combination form/register/SR inventory reports **38 absent
supervisor-A7 combinations per model / 76 total**: thirty post-post and eight
absolute combinations per model. Full missing keys remain in `ReferenceGaps`;
`unavailableReferenceCombinations` is distinct from passing selected callbacks.
These combinations require independent reference follow-up. Ordinary synthetic
MOVE16 coverage retains both stacks; it does not substitute for missing native
reference evidence. No incoming trace, faults, physical burst/bus ordering,
cache allocation or physical timing qualification is claimed.

Evidence is under the restoration temporary root in
`audits/Move16GapQualifiedFinal` and `move16-cohort-verification.json`. Copied-input
controls in `Move16IntegrityFinal/controls.json` reject missing profiles, changed
source, foreign families and changed fixture bytes. An isolated copied test
changes only the expected weighted-map hash: all 83,712 callbacks and their
counts pass, while both profile gates fail `formDistributionMatches=False`.
All five controls execute exactly one failing native fact without skips and
retain the intended diagnostic. Earlier exploratory preparations and the first
incorrect integrity invocation are preserved separately and excluded from this
acceptance.

The shared adapter also passes all nine existing qualified presets again:
105 executions, 116 directories, 1,525,856 callbacks and 858,001 frames, with no
skips. Synthetic MOVE16/breakpoint, input-validation and architectural-flag
controls pass 28 executions. Normal CPU/test assemblies retain their protected
identities. This test-only slice requires no CPU fix, consumer replay, package
release or old-test retirement. The original Basic MOVE16 failure and its full
54 non-passing rows remain retained. The supervisor-A7 reference gaps, complete
480-case advanced-040 inventory and all other retained milestone-6 requirements
remain open; milestone 6 stays **in progress**, `roadmapComplete=false`.

### Supervisor-A7 MOVE16 reference completion — 2026-10-06

The previous 76 missing form/register/SR combinations are now independently
executed. The pinned generator excluded writes to its reserved supervisor-stack
area and discarded instructions that changed supervisor A7. Two additional
input-generator patch hunks permit selected MOVE16 stack-line accesses,
including subsequent write-history validation, and retain MOVE16 A7 changes.
No production CPU or original Basic/reference source is modified.

The native output format restores USP into register slot A7 and cannot compare
the completed active SSP. The adapter therefore independently derives all eight
expected architectural address registers from documented MOVE16 fields and the
initial active stack, then asserts them before converting output registers.
Same-register post-post increments once; non-postincrement forms preserve their
address registers. Fixed controls cover A7 aliasing, wraparound and low-bit
preservation. A copied adapter deliberately flips active supervisor A7 before
conversion; both models fail the new assertion at position seven. No reference
expected result is changed or masked to obtain agreement.

The maintained `Move16` command now requires **24 executions without skips**,
**43,856 callbacks / 25,674 weighted form keys per model**: **87,712 callbacks /
51,348 summed profile keys**. All five forms, all 64 post-post register pairs,
all eight registers in the four absolute forms, CCR 0/31 and both user/supervisor
modes execute. An independent inventory requires **all 384 form/register/SR
combinations per model with zero missing combinations**; missing required
reference coverage now fails the selected gate. Sixteen user-register low-bit
rounds remain; the native supervisor A7 starts at its fixed aligned SSP. This
closes the recorded form/register/SR gap, without claiming every supervisor
low-bit pairing, trace/fault, cache or physical timing behavior is qualified.

The final normalized copied source SHA256 is
`04ecd41c994ccdcb06a6dc08607f54d2fa5b219e8b821be8760c2022aea990a4`;
patch SHA256 `fe407db40dca22a0258a0a689133fa581588117d22ca631cb2c8e52a15932caa`.
Input manifest SHA256 is
`7797be5f1621a24e1e8e27cd655bfc44c27f78ad78c7360f6366ad34610f3eac`;
native SHA256 `1877ea62f605efd2701f13653bfc862d84632e1009849507ee9b857958164eae`.
Each ordinal weighted form distribution hashes to
`560bf286786ac671980a481728c812f977cd08c376ced187da599fd1238487c3`.
Generator/runner pins and the existing preparation/audit commands are unchanged.
Native register, SR and actual destination-memory corruption are detected on
both models. Five copied-input/weighted-map integrity controls and the active
supervisor-stack corruption control reject their precise intended errors.

Fresh evidence under the restoration temporary root is
`inputs/Move16SupervisorSerialization`, `audits/Move16SupervisorQualifiedFinal`,
`Move16SupervisorIntegrity/controls.json`,
`Move16SupervisorStackControl/control.json` and
`move16-supervisor-cohort-verification.json`. The initial stack-generator
attempt failed because inactive write-history validation still rejected its
stack region; its outputs remain excluded. The exploratory successful execution
retained the old count/hash expectations and correctly failed the gate before
final coverage identities were fixed. Neither failed run is reclassified green.
All nine existing qualified presets pass again (105 executions / 116 directories /
1,525,856 callbacks / 858,001 frames), together with 28 shared/synthetic controls.
Normal CPU/test outputs remain unchanged. No consumer replay, package release
or old-regression retirement is performed in this test-only slice.

The original Basic corpus and its 54 non-passing rows remain unchanged; its
noncanonical MOVE16 stop is preserved separately from this legal-input audit.
The advanced 480-case 040 inventory, other-model restoration, software reference
disagreements and all remaining consolidation requirements stay open.
Milestone 6 remains **in progress**, `roadmapComplete=false`.

### Actual 040 operand-read fault discovery — 2026-10-06

The remaining real data-fault requirement now has a self-contained, strict
failing gate. `SyntheticM68040OperandReadFaultDiscoveryTests` executes sixteen
fixed legal read encodings: MOVE B/W/L, MOVEA W/L, ADD/CMP/TST B/W/L and MOVEM W/L.
The memory operand is `(A0)`; MOVEM selects D0 and rejects its first transfer.
The expectation source is [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
8.4.6, 8.4.6.2 and 8.4.6.7: format-7 data access frames, fault address, read/size/
modifier fields, MOVEM CM/calculated EA and absent pending writes before the
first operand read. Undefined EA/push/writeback data and SSW X are masked only
where the architecture leaves them undefined. No CPU decoding/arithmetic/EA
helper supplies expected results.

Each scalar/batch route executes **61,440 cases / 1,920 combinations** covering
all 32 initial CCR values, user/user-M/ISP/MSP banks, no/T1/T0 incoming trace,
four operand address lanes and every byte of the rejected original-width read.
The gate checks original PC/SR, complete defined frame fields, one exception,
stack selection/consumption, registers, surrounding memory and translation
bypass restoration. Each case attempts its faulting instruction exactly once;
there is no retry after a mismatch or partial effects. No hardware timing,
enabled cache/MMU, arbitrary addressing, later MOVEM transfers, handler/RTE
recovery or physical split-transfer ordering is qualified by this entry fixture.

Fresh execution passes the single fixed witness fact's **sixteen literal
normal-execution examples**, but both fault-entry batches fail without skips:
**zero passing / 122,880 mismatching / zero unsupported / zero untested**
selected cases. Captured detailed failures first differ at A7: an eight-byte
frame is consumed instead of sixty, leaving the stack 52 bytes above the required
boundary. Current `RaiseMmuFault` explicitly falls back to format 0 for these
operand reads. Further defined frame checks remain unmet prerequisites; these
failed cases are not described as qualified passing coverage.

```powershell
./scripts/test-copper68k-040-operand-read-discovery.ps1 -OutputDirectory <fresh-output>
./scripts/test-copper68k-040-operand-read-discovery.ps1 -ValidateReportsOnly -OutputDirectory <existing-output>
```

The maintained command requires the three exact executions (one passing witness,
two complete entry selections), with no skips, complete CPU/test source identities,
report/TRX/isolated-assembly hashes and an independently expanded 1,920-key /
32-weight inventory for each route. It records all classifications before failing
for any mismatch/unsupported/untested case. Frozen validation checks identities
and complete report/TRX content; it does not rerun the CPU. Missing fixtures,
empty selections and changed evidence cannot become passing audit coverage.
Six copied-evidence controls reject missing source/report identities, changed
weights, foreign keys, empty combinations and empty execution. Content controls
refresh altered report hashes so they exercise semantic checks as well as hashing.

Fresh evidence is under the restoration temporary root in
`audits/OperandReadDiscoveryFinal` (three executions: one pass, two fail),
`OperandReadIntegrityFinal/controls.json` and `operand-read-discovery-final.log`.
Input manifest SHA256:
`5102bcba25b93795b8266de1c0c6efa37619edb795d4c6ac3b126cce01a7b6a1`.
Preliminary wrapper output failed an incorrect PowerShell property-count check;
its captured CPU failures remain separate from final verified acceptance.
The first integrity invocation used an overly specific missing-file diagnostic
string; final six controls require the observed intended errors. These attempts
are preserved and excluded from final validation claims.

A separate documentary recheck finds no ordinary STOP S-clear clarification in
[NXP M68000PRMER revision 1](https://www.nxp.com/docs/en/reference-manual/M68000PRMER.pdf)
or [MC68060UMAD revision 0.1](https://www.nxp.com/docs/en/reference-manual/MC68060UMAD.pdf).
The existing 060 STOP software-reference disagreement remains unresolved; no
CPU restriction is inferred from absence of an erratum.

No production CPU edit, regression retirement, consumer replay or package release
is included. Ordinary scenario/report requirements remain 86,097,474 / 707;
the sixteen-example fixed witness is separate from those batch totals. The
480-case advanced-040 inventory remains intact: this is a failing concrete
qualification prerequisite, not closure of its broad real-access-fault row.
The next fix must correct architectural operand-read frame delivery and preserve
actual latched MOVEM EA/continuation rules, then qualify executed recovery and
affected consumers. All other model/reference/consolidation requirements remain
required. Milestone 6 stays **in progress**, `roadmapComplete=false`.

### Physical 040 operand-read frame correction — 2026-10-06

The failing operand-read discovery above is retained as before-fix evidence.
Normal, translation-disabled 040 operand reads now enter the architectural
60-byte format-7 frame, preserving the instruction PC and live SR and recording
the actual read size, data modifier and fault address. Exception-entry reads
are excluded by the instruction boundary/exception sequence checks. Enabled
MMU delivery and compiled operand side exits retain their separate scope.

MOVEM read faults carry the EA calculated before any transfer. Both advanced
paths and the shared integer fallback attach it at the failing read; neither
recalculates an overwritten base/index nor rereads an indirect pointer during
exception delivery. The frame sets CM and saves this EA. An explicit software
RTE then resumes through the existing CM path, repeating preceding MOVEM reads
as required by MC68040UM 8.4.6.7. There is no automatic instruction retry.

The focused command passes **122,880 selected entry cases**, with zero
mismatches, unsupported cases or missing selected combinations. Its five
executions include sixteen literal encoding witnesses and two new brief/full
indexed MOVEM recovery controls. These fault the fourth transfer after D1 and
A0 have been overwritten, verify the saved EA/CM/PC, execute RTE and complete
the transfer, and check that the full-format pointer was read exactly once.
Dropping calculated-EA metadata in an isolated source copy makes both controls
fail at the CM assertion. Six copied-evidence corruption controls still reject
their intended errors; exact theory inputs are required by the command.

Cached-batch expectations now require the architectural read frame while
retaining count, callback, partial-effect and timing-policy checks. Accurate
fatal-entry coverage expands from eight to all sixty frame bytes for B/W/L
reads: **1,248 additional scenarios**, making the ordinary required inventory
**86,098,722 scenarios / 707 reports**. This does not promote the focused entry
matrix into those ordinary totals or reduce the broad 480-case remaining gate.

Consumer validation uses a clean archive of CopperScreen commit
`aa1dad5dcc0fb7c970e8e3443a160487add846af` and the isolated, unpublished
`1.5.2-synthetic-dev.68` NuGet package. Release production build, 171 host tests,
74 disk tests and 1,080 separately built engine tests pass. Six optional host
replays are unavailable in that ordinary run. Two explicit native Workbench
3.1 floppy boots (zero/2 MiB Fast RAM) pass their existing ROM/media, PC, cycle
and framebuffer fingerprints. The Workbench input was restored from its local
archive; the previously deleted HDF was not recreated or claimed as replayed.
Pinned SingleStepTests and Musashi audits also pass independently.

Evidence is retained under the restoration temporary root in
`audits/OperandReadAcceptance`, `OperandReadAcceptanceIntegrity`,
`mutations/DropMovemEa`, `audits/OperandReadIndependent`, `consumer-68` and
`native-68`. The private package is immutable and has SHA256
`c4d2e75502d9304d249760e9c57356b20b6ad569c45f4fe2786ecdbd35022e28`.

The fresh full CPU run completed on **2026-10-07**: **5,244 passing / zero
failed / 21 optional or discovery skips / 5,265 total**. All ten enabled
qualified WinUAE presets pass in this run. The maintained report validator
accepts all **707 required batches / 86,098,722 scenarios**. Actual passing TRX
batch summaries are cross-checked against the report contents; the complete
result roster and report/assembly hashes are retained in
`audits/OperandReadFull/full-verification.json`. The older cleanup-affected full
run remains failed historical evidence. These fresh results do not turn skipped
discovery gates or the original Basic reference disagreements into coverage.

General operand addressing/recovery/trace deferral, MOVEM writes, enabled
MMU/cache and physical transfer timing remain outside this selected fix.
Other models' restoration gaps, unresolved software-reference disagreements,
the original Basic non-passing rows and regression consolidation remain
required. No public release or regression retirement is included. Milestone 6
remains **in progress**, `roadmapComplete=false`.

## Executed 040 MOVEM read recovery — 2026-10-07

The selected software recovery qualification now executes a complete fault,
handler RTE, resumed MOVEM and following-instruction program. Expectations use
MC68040UM 8.2.6 and 8.4.6.2/7: CM retains the calculated EA; resumed MOVEM repeats
preceding operand transfers without recalculating that EA; an aborted
instruction's trace is deferred until completion. This is architectural software
qualification with translation and instruction caching disabled, not physical
pipeline or cache timing qualification.

Run the complete selected inventory with:

```powershell
./scripts/test-copper68k-040-movem-read-recovery.ps1 -OutputDirectory artifacts/040-movem-recovery-audit
```

`-ValidateReportsOnly` verifies frozen source/report/assembly/TRX identities,
the exact five-execution roster and independently expanded combination keys and
weights. Both routes pass **114,912 matrix programs plus 72 fixed source programs**:
**229,968 whole programs**, zero mismatches, unsupported executions or missing
selected combinations. Logical counts represent complete programs, not individual
instructions or xUnit tests. Per route the matrix comprises:

- 864 source-encoding programs: all 36 legal memory-source encodings, W/L,
  four rejected transfer positions and every byte of the rejected access.
- 12,672 structure programs: 66 legal full-format structures, four signed/scaled
  data/address-index variants, address-register/PC bases, W/L and every rejection
  position/byte.
- 101,376 status programs: eleven canonical source forms, four stack/SR profiles,
  T0/T1/off, all 32 CCR states, W/L and every rejection position/byte.

The transfer list is deliberately D0/D1/A0/A1, exercising overwritten bases and
indexes. Registers, stack banks, saved SR/PC, defined format-7 fields, partial
effects, exact next PC, surrounding memory, operand-read repetition, pointer-read
count and following MOVEQ/branch trace sentinels are checked. Two literal fixture
examples additionally verify PC self-reference encodings. Null PC displacement
can place an operand or pointer in the instruction stream; fixture setup preserves
those bytes and derives their values from the test-owned encoded stream.

Evidence is retained in `audits/MovemReadRecoveryAcceptance` under the restoration
temporary root. Six copied-evidence controls reject missing source/report,
changed weight, foreign key and empty selection/execution. Discarding calculated
EA metadata in an isolated shared-fallback source copy produces **31 expected
frame-EA mismatches per route**, while 41 programs per route still pass through
unmutated execution paths. The earlier selected 122,880-case read-entry gate also
passes again against the current sources. Failed provisional fixture runs remain
separate historical evidence.

The existing MOVE extension/alias, MOVEM mask/base-alias and LEA/PEA fixture
checks pass across all eight model/profile selections: 25 executions, 24 reports
and 145,816 logical scenarios, retained in `audits/MovemRecoverySharedFixtures`.

No production CPU code changes or new package are part of this extension.
The prior full CPU/consumer results above qualify the unchanged production
implementation; they are not claimed as a fresh full-suite run of these new tests.
The ordinary required 707-report inventory remains separate from this focused
reference command. Other MOVEM masks, MOVEM writes, pointer-fetch faults, nested
recovery, enabled MMU/cache and physical timing still need their applicable
qualification. The broad 480-case remaining gate and the other model/reference/
consolidation work remain open. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

### Pre-EA MOVEM indirect-pointer faults — 2026-10-07

The same maintained recovery command now also requires the pre-EA fault path.
A full-format indirect-pointer read faults before any MOVEM operand transfer.
The selected expectation is format 7 with CM clear, a long read's attributes,
the pointer address in FA, no valid pending writebacks and the original PC/SR.
EA is undefined with no continuation bits and is deliberately not compared.
RTE returns to the original instruction; address calculation resolves the pointer
once successfully, then all four operand transfers complete. T1 is deferred to
resumed completion; T0 is checked by the following branch sentinel. Partial
register/stack/memory state, exact instruction length, original-width rejection
and absence of operand reads before pointer recovery are verified.

This distinguishes a fault during address calculation from a fault after the
calculated EA has been retained for MOVEM. The expectation derives from
MC68040UM 8.4.6.2/7. Source-level corroboration uses pinned generator
`025b999239800357e95065fe5b9a15ea5b300fa7`: `genmovemel` emits `genamode` before
`movem_mmu040`, which emits the runtime continuation flag and saved EA after
address calculation. This inspection is not an executed native fault oracle or
hardware observation; its source hash and caveat are recorded separately.

Each route adds **15,744 matrix programs plus eight fixed programs**:

- 3,456 structure programs: 54 legal indirect full-format structures, four
  signed/scaled index variants, address-register/PC bases, W/L MOVEM and each
  byte of the four-byte pointer read.
- 12,288 status programs: pre/post indirection with address-register/PC bases,
  W/L, four stack/SR profiles, T0/T1/off, all 32 CCR states and every pointer byte.

Together with the unchanged selected operand-fault coverage, the command requires
**261,472 passing whole programs / eight reports / nine executions**. It rejects
missing pointer reports or test selections rather than silently omitting them.
Evidence is retained in `audits/MovemPointerRecoveryAcceptance`; six corruption
controls pass in `MovemPointerRecoveryIntegrity`. An isolated mutation which sets
CM before EA calculation makes all **16 fixed pointer programs** fail at the SSW
continuation assertion, retained in `mutations/PointerFaultPrematureCm`.

These are transient physical read failures with the existing one-shot bus fixture;
the handler executes RTE, without editing the pointer or completing a writeback.
The transfer list remains D0/D1/A0/A1. Other transfer masks, pointer-editing handlers,
nested recovery, MOVEM writes, enabled MMU/cache and physical timing are not
qualified by this slice. Production CPU/package bytes are unchanged. The previous
full-suite and consumer results remain historical evidence for that production
implementation, not a fresh run of these new tests. The broad remaining gate,
other model/reference disagreements and consolidation remain open. Milestone 6
remains **in progress**, `roadmapComplete=false`.

## EXTB semantic regression consolidation — 2026-10-07

`M68020InterpreterTests.M68020ExecutesM68020OnlyExtbLong` is retired only after
proving replacement coverage. Its critical inputs are opcode `49C0`, D0 `$80`,
CCR zero and supervisor SR `$2700`. It checks the sign-extended D0, next PC and
N/Z/V/C, with no timing, bus-order, prefetch, cache or integration assertions.
The exact existing replacement is
`68020/EXTB.L/D0/boundary-ccr/op=49C0/v=00000080/ccr=00` in
`SyntheticTransferTests.RegisterTransferFamilies`. Shared expectations also
check X/other SR bits, all untouched registers/stacks/memory, execution state
and a following NOP sentinel. Other CCR/value/register selections and all eight
profiles remain covered by that matrix, including the unavailable instruction's
architectural outcome on 000/010.

[M68000PRM EXT/EXTB, 4-106](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf)
defines EXTB as copying bit 7 into bits 31..8. The targeted production mutation
replaces that sign extension with zero extension in an isolated source copy.
Before retirement, both the original fact and the replacement fail in the same
run. The original reports expected `$FFFFFF80`, actual `$80`; the exact
replacement reports expected SR `$2708`, actual `$2700`, catching the resulting
incorrect N flag first. There are **1,024 intended EXTB mismatches on each of
six implementing profiles / 6,144 total**. The 000/010 unavailable-instruction
checks and every other transfer combination still pass.

The maintained command makes this proof repeatable after retirement:

```powershell
./scripts/test-copper68k-extb-consolidation.ps1 -OutputDirectory artifacts/extb-consolidation-proof
```

It requires the exact original fact from pinned commit
`4863f449816115b536b21f9faa7c73539ecaabcb`, inserts it only into owned source
copies, and runs clean/mutated comparisons. Missing historical fixtures or empty
selections fail. The gate checks source, fixture, assembly, report and TRX
identities, the exact nine-test roster in each run, the original diagnostic,
the exact replacement case, and all **232 independently enumerated weighted
combination keys per model**. Both pre- and post-retirement clean comparisons
pass **140,992 logical scenarios / eight reports plus the historical fact**.
The mutated run has precisely seven failed executions and two passing ones.
`-ValidateReportsOnly` verifies the frozen proof without rebuilding.

Evidence is retained under the restoration temporary root in
`audits/ExtbConsolidationBeforeFinal`, `audits/ExtbConsolidationAfter` and
`ExtbConsolidationIntegrity`. Six copied-evidence controls reject missing source,
altered pinned fixture, empty execution, wrong weight, lost exact replacement
case and wrong historical diagnostic. Initial verifier defects remain retained
as provisional evidence, not accepted proof.

All **462 remaining M68020InterpreterTests executions** pass with no failures or
skips, retained in `audits/ExtbRetained020Tests`. Nearby LEA/MOVEQ and other
timing-policy tests remain: their cycle assertions are not replaced by the
semantic matrix. Specialized exception, bus, cache/prefetch, JIT and native
regressions are not retired. The ordinary 707-report scenario inventory does not
change. Production source, package bytes and protected normal assemblies remain
unchanged; no new full CPU/consumer run or package release is claimed. Other
consolidation/reference/restoration requirements remain open. Milestone 6 is
still **in progress**, `roadmapComplete=false`.

## Actual 040 MOVEM write-fault discovery — 2026-10-07

This slice exposes a production gap; it does **not** close it. MC68040UM
8.4.6.2/5/7 requires a normal physical write's format-7 frame, valid
memory-aligned WB1, FA/WB1A and the original MOVEM calculated EA with CM set.
The saved PC identifies the interrupted MOVEM. A handler completes WB1 before
RTE; CM continuation repeats preceding MOVEM operand accesses. Exception entry
must not automatically retry a partly executed instruction. These are software
architectural requirements, separate from physical pipeline/cache qualification.
Primary source: [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf).

`SyntheticM68040MovemWriteFaultDiscoveryTests` uses the public CPU factory,
sparse recording memory, existing independent addressing/stack fixtures and a
one-shot physical-map rejection. Selected instructions store D0/D1/A0/A1 with
mask `0303`, or reverse mask `C0C0` for predecrement. It separately executes:

- All 34 legal store EA/register encodings, word/long sizes, each of four
  transfers and every rejected byte: **816 cases per execution route**.
- Six canonical forms across four stack states, T1/T0/clear trace, all 32 CCRs,
  both sizes and every transfer/rejected byte: **55,296 cases per route**.

Scalar and batch execution each contain **56,112 cases / 2,544 weighted keys**.
All **112,224 fault-entry cases mismatch**, with zero unsupported or untested
cases in this *selected* inventory. Retained failure diagnostics show
`format/vector expected 7008, actual 0008`, with an eight-byte frame rather than
60 bytes (ISP example `A7 expected 000046C4, actual 000046F8`). Production routing
handles completed normal MOVE writes specially, but MOVEM writes fall through
to the generic format-0 bus exception. MOVEM store paths also lack calculated-EA
fault metadata. This identifies the required correction without claiming a
validated fix.

The literal opcode/brief-extension witness passes. Independent fault-free
controls execute all 34 forms at both sizes in scalar and batch routes:
**136 passing MOVEM/following-MOVEQ programs**, checking register preservation,
flags, memory, exact next PC, predecrement bases (including base/list aliases),
A7 and stack state. The discovery run has **three passing executions and two
failing executions**, no skips. Successful controls establish the selected
fixture encodings; they do not validate fault frames or recovery.

Reproduce with the explicit strict command:

```powershell
./scripts/test-copper68k-040-movem-write-discovery.ps1 -OutputDirectory artifacts/movem-write-discovery
```

It currently exits nonzero for the genuine frame mismatches. Use a fresh output
directory. `-ValidateReportsOnly` checks the frozen evidence without rebuilding
and must also reject this failing acceptance cohort. The verifier pins source,
script, assembly, report and TRX identities; requires the exact five-execution
roster, both independently enumerated weighted fault rosters and both 68-key
passing fixture rosters; and checks protected normal DLL hashes. Missing inputs,
empty selections and mismatches cannot be accepted. Seven copied-evidence
controls reject missing/changed source, missing report, empty execution, changed
weight, missing key and a false fixture-control weight at the intended checks.
An initial control harness expected different missing-file wording; that
provisional run is retained separately from the completed seven-control proof.

Evidence under the restoration temporary root:
`audits/MovemWriteDiscoveryFinalV2` (manifest SHA-256
`dcf627ceea945aed7326637427a88f96a843aae43ac54baaedd28744e4f5df8e`),
`MovemWriteDiscoveryIntegrityFinal`, and `audits/MovemWriteOrdinaryControlsFinal`.
The latter runs the new controls and existing fixed MOVEM operand/pointer-read
recovery witnesses: **eight passing executions, zero failures, six explicit
opt-in matrix skips**. Those skips are unavailable coverage in this invocation;
they do not override the explicit write-discovery failures. Earlier discovery
iterations remain historical, with their own frozen source identities.

Next required work is latched MOVEM write metadata across fast/general/shared
fallback routes, correct normal-write WB1/CM entry, and executed handler/RTE/
resumption/sentinel verification, including trace and partial-write ordering.
Full indexed structures, arbitrary masks, alignment, nested write faults and
other physical write families remain untested by this slice. No promoted gate,
480-case broad inventory or ordinary 707-report requirement is reduced. No
production source change, regression retirement, new full CPU/consumer run or
package publication is claimed. Normal CPU/test assemblies and unrelated
CopperScreen edits remain untouched. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

## Physical 040 MOVEM write-frame correction — 2026-10-07

The working correction addresses the preceding actual-write discovery. Fast,
general and shared-fallback MOVEM store paths retain the original calculated EA
only when an operand write faults. Pre-EA pointer reads and exception-frame
writes cannot acquire that MOVEM write metadata. Existing operand ordering,
predecrement register-list semantics and timing-plan placement remain unchanged;
the shared fallback keeps its descending split-word long stores and the logical
bus's original operand address/size/data latch.

Normal physical MOVEM write faults now use format 7, original instruction PC,
CM/original EA and a valid memory-aligned WB1. The shared normal-write builder
retains completed MOVE's distinct following-PC/CT behavior. MOVEM remains
suspended: exception delivery never retries it, software completes WB1, then
RTE explicitly resumes the MOVEM. Enabled MMU/cache/pipeline behavior and other
physical-write families are not promoted by this correction.

Fresh focused evidence:

- The complete original selected entry gate passes **112,224 cases**, zero
  mismatches/unsupported/untested selections, plus its literal witness and
  **136 fault-free controls**. Five executions pass with no skips. Manifest
  `audits/MovemWriteEntryAcceptance/inputs.json` SHA-256:
  `6bef57d174ba6e083f3fc32b056ee427d2ec62c8d5c40c959fb5dd985f9d494f`.
- Executed writeback-handler/RTE/resumption qualification passes **143,512
  whole programs / six reports / six executions**, zero selected gaps. Each
  route has 816 store-opcode cases, 64,512 canonical stack/CCR/trace cases and
  6,336 full-format structural cases, plus 68 canonical witnesses and 24
  self-overwriting-pointer witnesses. The full-format cohort independently
  covers all 66 legal structures with signed W/L D1 and signed/scaled A0/D1
  index variants. Status cases include predecrement A7 in all four stack states.
- Actual handler instructions preserve/restore scratch registers and DFC,
  complete WB1 with MOVES, clear its valid flag, and execute RTE. The resumed
  MOVEM repeats all four operand writes in order. A pointer overwritten by an
  earlier store is not resolved again. Exact following-instruction boundaries,
  T1 completion trace, deferred T0 flow trace, registers, defined frame/CCR
  fields, surrounding memory and partial pre-fault stores are checked.
- Discarding shared-fallback write-EA metadata in an owned copy produces **40
  intended frame mismatches / 96 unchanged passing witnesses**. Recalculating
  the address instead of consuming CM's saved EA produces **48 intended
  resumed-MOVEM mismatches**, exposing the overwritten pointer. Both routes
  fail, with exact phase diagnostics. Proof: `mutations/MovemWriteCorrection`.
- Seven copied-evidence corruption controls pass for each current entry and
  recovery gate: missing/changed source, missing report, empty execution,
  changed weight, lost key and false witness weight. Source, binary, report,
  exact TRX roster and independent weighted combination inventories are frozen.

Reproduce whole-program recovery with:

```powershell
./scripts/test-copper68k-040-movem-write-recovery.ps1 -OutputDirectory artifacts/movem-write-recovery
```

A fresh output is required. `-ValidateReportsOnly` checks frozen identities and
coverage without rebuilding. Missing fixtures, empty selections, mismatches or
unsupported execution fail. The entry command from the preceding section now
passes its unchanged architectural requirements. Recovery evidence is retained
in `audits/MovemWriteRecoveryAcceptance` (manifest SHA-256
`2a5717f542d817e2b39d59520335252046266af575bcc90abe6d4dad61f518af`),
`MovemWriteRecoveryIntegrity` and `MovemWriteEntryAcceptanceIntegrity`.
The first recovery witness run revealed a fixture initialization error: reset
cleared DFC before handler execution. DFC/SFC are now set after reset and checked
explicitly. That provisional failing run is retained separately.

Fresh independent audits under `audits/MovemWriteIndependent` pass **312,500
SingleStepTests cases / 125 files**, with the established TAS/TRAPV exclusions,
and **536 Musashi model/program combinations / 88 explicit exclusions**. Pinned
sources and caveats remain unchanged. These software references do not qualify
physical bus timing or the new fault protocol by themselves.

Clean CopperScreen commit `aa1dad5dcc0fb7c970e8e3443a160487add846af` builds with
zero warnings/errors using private **1.5.2-synthetic-dev.69** through its existing
NuGet boundary. **171 host**, **74 disk**, and separate **1,080 engine** tests pass;
six optional host replays remain unavailable in that ordinary invocation.
Two explicitly supplied native Workbench 3.1 floppy replays, zero/2 MiB Fast RAM,
pass the retained ROM/media, PC, cycle and framebuffer fingerprints. Actual app,
host-test and engine-test CPU DLLs match the exact package. Evidence:
`consumer-69-final/consumer-verification.json`; package SHA-256
`eed6baf7d1c2dfae99f83825904cc1e729ee2f4ad985a45fd43c3ef65f9c70e1`;
packaged CPU DLL SHA-256
`5528cb66670a6afc92fe4ddadb2c20425146995a4de63098b27845b174dc1c65`.
The package was created once and is immutable. A lock-file property error and
an external helper's command-name collision affected provisional owned validation
attempts; the completed consumer uses a fresh clean archive and the original
package. No source repository or previously published package was overwritten.
No HD replay is claimed and no public package is published.

The fresh full CPU run in `audits/MovemWriteFull` passes **5,255 tests**, zero
failures, with **29 explicit optional/discovery skips / 5,284 total** in
**56 minutes 43 seconds**. All ten previously qualified WinUAE presets execute
and pass. The maintained ordinary inventory and a separate actual-TRX audit
verify **707 required reports / 86,098,722 passing deterministic scenarios**,
including complete combination weights and passing execution identities.
The full run's opt-in write-entry/recovery skips are qualified separately by
the fresh focused commands above; remaining skipped coverage is not promoted.
Exact skip names are retained in `full-verification.json`. Source manifest
SHA-256: `7acedd9c5c632e10b9e7ee7ba6130a18d88b01106147d5bff3f785d56df840ce`;
TRX SHA-256: `c596190315220d8cb7ebeea51b93505b8f82b88c3cb2d8ee2c3b8ef92d541078`.
`ordinary-trx-verification.json` records every required report's hash, actual
test identity, scenario count and combination count. This is fresh passing
evidence for the correction, distinct from the prior read-only full run.
Normal CPU/test assemblies and unrelated CopperScreen changes are preserved.
The ordinary 707-report inventory is unchanged. Arbitrary MOVEM masks, broader
write-fault/nested/writeback protocols, the 480-case broad advanced inventory,
original Basic failures, other-model restoration and remaining consolidation
requirements stay open. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

## Transfer timing-regression retention audit — 2026-10-07

Six neighboring `M68020InterpreterTests` transfer facts were reviewed after the
pure EXTB semantic duplicate was retired. They remain required. The shared
`SyntheticTransferTests.RegisterTransferFamilies` matrix checks EXT.W/EXT.L,
SWAP and all EXG register forms semantically, but does not assert the named
accelerator profiles' native/machine-cycle policy. Semantic overlap alone is
insufficient replacement coverage.

| Retained fact | Selected profile | Additional timing assertion |
| --- | --- | --- |
| `ExtLongDataRegisterSignExtendsWordToLong` | `OcsAccelerator14Mhz` | 4 native / 2 machine cycles |
| `ExtWordDataRegisterSignExtendsByteToWord` | `A1200Ec02014Mhz` | 2 native / 1 machine cycle |
| `SwapDataRegisterSwapsWordsAndSetsFlags` | `OcsAccelerator14Mhz` | 4 native / 2 machine cycles |
| `ExgDataAddressSwapsRegistersAndPreservesFlags` | `OcsAccelerator14Mhz` | 4 native / 2 machine cycles |
| `ExgDataDataSwapsRegistersAndPreservesFlags` | `A1200Ec02014Mhz` | 4 native / 2 machine cycles |
| `ExgAddressAddressSwapsRegistersAndPreservesFlags` | `A1200Ec02014Mhz` | 4 native / 2 machine cycles |

The EXT/SWAP facts also use specific boundary/upper-register values, and the EXG
facts use specific register values. No exact-case replacement or original-defect
mutation proof is claimed for these six facts. They are retained on the concrete
cycle-coverage difference, without weakening or moving their assertions. Those
cycle counts describe the existing execution policy; they do not establish
physical MC68020 bus/pipeline timing. This audit changes documentation only,
retires no further regression and leaves the wider consolidation scope open.

## 020/030 address-frame entry discovery — 2026-10-07

The next restoration audit starts with real exception entry rather than invented
internal-state images. MC68020UM 6.1.3 and MC68030UM 8.1.3 require a short or long
bus-fault frame for an odd instruction prefetch, with vector offset 12 and no bus
access to the odd instruction address. The short/long choice depends on saved
execution context; the discovery therefore accepts either format A or B, without
forcing one physical pipeline implementation.

`SyntheticM68020AddressFrameDiscoveryTests` uses the public factory and sparse
recording bus for EC020, A1200, 020 and 030. Both execution routes cover four
stack states, all 32 CCRs, incoming T0/T1/no trace and low/high odd addresses.
The strict command executes **6,144 cases / eight discovery reports**, with
**zero passing / 6,144 mismatching / zero unsupported / zero untested** in this
selected entry matrix. Every case currently delivers format `000C`, a normal
eight-byte frame. The first EC020 user witness reaches handler `9030` with
SP `46F8`, instead of delivering a bus-fault frame. Accepted cases must also
pass the common header/vector and no-odd-fetch checks; failure at the format
check does not verify those subsequent requirements.

All **512 fault-free controls / four reports** pass shared register, stack,
PC and surrounding-memory verification. The complete strict roster has four
passing control executions and two failing matrix executions, zero skips.
The ordinary invocation passes the four controls and explicitly skips the two
opt-in discovery matrices. They are not promoted architectural coverage.

```powershell
./scripts/test-copper68k-020-address-frame-discovery.ps1 -OutputDirectory artifacts/020-address-frames
```

A fresh directory is required. The command currently fails acceptance.
`-ValidateReportsOnly` freezes source/binary/report identities and verifies the
exact roster, all twelve actual TRX summaries and independently enumerated
combination weights. Four copied-evidence controls reject a missing report,
changed weight, empty execution and changed source identity at their intended
checks. Evidence: `audits/AddressFrameDiscoveryFinalV2` under the restoration
temporary root; manifest SHA-256
`c308c348ccd55a98196f8b9a0b1a8a69d355651c4de7bd91349c97e490d1b68e`.
Integrity evidence is in `AddressFrameDiscoveryIntegrityFinal`. Provisional
compiler/helper failures remain separate from this verified discovery.

The pinned generator `025b999239800357e95065fe5b9a15ea5b300fa7` also exposes a
reference limitation: its non-MMU RTE branch only pops 20/32/92 bytes for
formats 9/A/B. Its 030 MMU branch calls separate restoration helpers. Agreement
with the simple pop branch cannot qualify opaque saved state or recovery.
Inspected `gencpu/gencpu.cpp` SHA-256:
`8215f4fee33b1410a7476dec2bce1fc40c4272fdcd2ef5dbca3262ba706e011d`.

The required implementation/qualification sequence remains:

1. Produce real A/B frames with defined SSW, saved PC/SR and resumable integer
   context; retain partial operand effects without implicit retries.
2. Validate both ends before loading state, including long-frame version at
   SP+36 hex. Distinguish validation exceptions from fatal state-load faults.
3. Execute repair handlers and RTE for software-completed and rerun data cycles,
   instruction stages and read-modify-write operations, preserving stack,
   trace, boundary and existing timing policy. Check exact following execution.
4. Qualify format-9 coprocessor context transport separately; frame deallocation
   alone does not restore it. Enabled MMU and physical pipeline qualification
   remain outside the roadmap.

The existing legal 9/A/B exclusions are not relabeled invalid or removed.
This slice adds tests, a strict command and documentation only. It fixes no
production behavior, retires no regression and creates no package. The preceding
full CPU/consumer evidence applies to the MOVEM correction, not a fresh full run
of this new fixture. Milestone 6 remains **in progress**, `roadmapComplete=false`.

## 020/030 instruction-prefetch recovery correction — 2026-10-07

The subsequent correction delivers a 92-byte format-B frame for the synchronous
interpreter's initial odd instruction prefetch on EC020/A1200/020/030. The
defined header, SSW RC/RB bits and stage-B address follow MC68020UM 6.1.3/6.2/6.4
and MC68030UM 8.1.3/8.2/8.4. FB/FC/DF remain clear because an odd prefetch does
not issue a bus cycle. The opaque internal image identifies this interpreter's
pending instruction context; it is not a silicon, MMU or physical-pipeline
serialization format. No address-keyed side table or automatic instruction
retry is introduced.

RTE checks the long-frame version before loading state, preserves the original
frame on version rejection, and restores software-repaired C/B instruction
words after the existing RTE timing barrier. Both supplied words survive until
consumed, including a supplied NOP followed by a supplied branch. Uncleared
RC/RB bits cause explicit RTE to enter the address-error handler again without
fetching an odd address. Foreign opaque images, format-A data continuation and
format-9 context transport remain unsupported required work; they are not
relabeled architectural format errors or promoted by this correction.

The original discovery command now passes **6,144 entry cases and 512 controls**
in six executions, zero skips. Evidence: `audits/AddressFrameEntryAcceptance`,
manifest SHA-256
`5ba39907279db53982dbd61162df2849b457bce9f8621cea5c40ef0b278942d2`.
The retained failing discovery remains historical evidence of the original defect.

`SyntheticM68020PrefetchRecoveryTests` executes real repair handlers and RTE,
with shared register/PC/status/stack/memory checks at instruction boundaries,
two pipeline-word sequences, low/high addresses, four stack states, all CCRs,
T0/T1 trace returns, fifteen incompatible versions and uncleared rerun bits.
The strict eight-execution gate passes **8,960 complete programs / 32 reports**,
with zero mismatches, unsupported or untested cases in this selected matrix.

```powershell
./scripts/test-copper68k-020-prefetch-recovery.ps1 -OutputDirectory artifacts/020-prefetch-recovery
```

Fresh output is required. `-ValidateReportsOnly` checks frozen source/binary/
report identities, exact execution roster, all actual TRX summaries and
independently enumerated keys and weights. Evidence:
`audits/PrefetchRecoveryAcceptanceFinal`, manifest SHA-256
`491ad179157b10a3b6c9ccb1f7d6bd8aea5f1267d5804eaacdb974739e26925a`.
Four copied-evidence integrity controls reject missing reports, changed weights,
empty execution and changed source identity. Two isolated production mutations
detect loss of the restored pipe (256 mismatches) and loss of its second word
(64 mismatches, 192 unchanged passing witnesses). Evidence is retained under
`PrefetchRecoveryIntegrity` and `mutations/PrefetchRecovery`. The 486 selected
existing 020/030 interpreter and synthetic exception controls also pass.

Fresh pinned SingleStepTests and Musashi audits pass: 312,500 cases / 125 files
with documented TAS/TRAPV exclusions, and 536 model/program combinations / 88
explicit exclusions respectively. A clean CopperScreen archive at
`aa1dad5dcc0fb7c970e8e3443a160487add846af` builds with zero warnings/errors through
isolated immutable private package `1.5.2-synthetic-dev.70`. Host 171, disk 74 and
separate engine 1,080 tests pass, with six unavailable optional host replays.
Two explicit native Workbench 3.1 floppy boots pass; runtime app and test CPU
assemblies match the package DLL. No HD replay or public release is claimed.
Evidence: `audits/PrefetchIndependent` and `consumer-70-final`.

Fresh full CPU validation passes **5,265 tests, zero failures and 33 explicit
optional/discovery skips (5,298 total)**, including all ten qualified WinUAE
presets. The ordinary gate and separate actual-TRX audit verify all **707 required
reports / 86,098,722 scenarios**, with complete combination weights. The opt-in
entry/recovery matrices pass separately above; remaining skipped coverage stays
unpromoted. Evidence: `audits/PrefetchFull`; source manifest SHA-256
`3a03e18a3cd8b7210e078b26a56100cd13df6f5ac9b5ff18b407ebb3256db2ab`, TRX SHA-256
`a131a12c38502a8347f9578df463d1c63ba45c7c03e71c41e2798d9f4b611e2f`.
Normal CPU/test assemblies and unrelated CopperScreen changes are preserved.
The immutable .70 package SHA-256 is
`15d6af210c9487b66b1bfd1c25ff92c694fda296b7eb0d703fae35e6a38aec84`;
validated app/test CPU DLL SHA-256 is
`cddef7efa5803a2f2b32345cb7330e4b4d10192da92a55600da5f4e86bed18f4`.

Broader A/B data recovery, foreign
images, format-9 transport, nested fault/load-failure qualification and remaining
reference/consolidation work stay required. Milestone 6 remains **in progress**,
`roadmapComplete=false`. No regression is retired or public package published.

## 020/030 RTE validation and state-load faults — 2026-10-07

The next correction distinguishes a rejected frame-validation read from a
rejected internal-state load on EC020, A1200, 020 and 030. The architectural
expectations come from [MC68020UM](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
6.1.12 and [MC68030UM](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
8.1.13. A validation fault creates a format-B bus-error frame below the intact
original frame. A failure once state loading starts halts without writing a
second exception image. An inaccessible write or vector read during validation
exception entry also halts after the already-completed prefix.

The interpreter serializes its pending validation phase and previously read
values in a private opaque format-B continuation. An explicit handler RTE
resumes the rejected phase, retaining completed reads. Clearing DF selects the
right-justified software input buffer; leaving DF set retries only the pending
read. Repeated rejection delivers a fresh bus-error image after deallocating
the old one. An incompatible software-supplied version produces format error
before further state loading. No implicit replay or frame-address side table is
used, and no public API changes are introduced.

`SyntheticM68020RteFaultTests` passes **36,096 complete programs / 104 reports /
22 executions**, zero mismatches, unsupported forms or skips in this selected
matrix. Tests cover scalar/batch dispatch, each selected validation/load request
and byte lane, all CCRs, four stack states, T0/T1 returns, software-supplied
header/version reads, preservation of an already-read SR after handler writes,
explicit repeated faults, exception-entry failures, and normal/throwaway-chain
headers. All registers, PC, status, stacks and surrounding memory are checked
at instruction boundaries. Opaque bytes are checked for preservation, never
presented as independently known silicon state.

```powershell
./scripts/test-copper68k-020-rte-faults.ps1 -OutputDirectory artifacts/020-rte-faults
```

The command requires fresh output and rejects missing fixtures, empty/partial
execution and changed source/binary/report identities. `-ValidateReportsOnly`
checks the exact 22-test roster, all 104 actual TRX summaries and independently
enumerated case identifiers and weights. Evidence: `audits/Rte020FaultAcceptanceV3`,
source/evidence manifest SHA-256
`8a5f585c884dff3783add8f1c3a65d8e87e2c0a5b1b3d04ccfaa52bf46906ad1`.
Earlier verifier attempts are retained as failed provisional evidence, not
relabeled accepted runs. Four copied-evidence integrity controls reject missing
reports, changed weights, empty execution and changed source identity under
`Rte020FaultIntegrity`.

The pinned pre-fix archive at `4837ce900acaea77467e310c50e4e725abdc6a56`
fails all **64 original witnesses / 16 reports / four executions** at the
intended missing-read-rejection check. The baseline's source, binaries, exact
case roster, diagnostics and actual TRX summaries are independently recorded
under `audits/Rte020BaselineReplay`. The current strict prefetch gate still
passes 8,960 programs and frame-entry qualification passes 6,144 cases plus
512 controls (`audits/Rte020Prefetch`, `audits/Rte020Entry`). Fresh pinned
SingleStepTests and Musashi audits also pass (`audits/Rte020Independent`).

Three isolated production mutations detect replayed validation phases (448
mismatches), discarded software input (320 mismatches, 128 unaffected passing
cases), and ignored state-load rejection (32 mismatches). The proof checks
source snapshots, binaries, exact passing/failing execution rosters, case
identities/weights, diagnostic phases and actual TRX summaries. Evidence:
`mutations/Rte020FaultsVerified`, verification SHA-256
`dec7b05946c9206042c3b605abe13447a52167ae30a31578ec714e06aa76df41`.
Earlier failed proof-verifier attempts remain provisional evidence.

A clean CopperScreen archive at `aa1dad5dcc0fb7c970e8e3443a160487add846af`
builds with zero warnings/errors through isolated private package
`1.5.2-synthetic-dev.71`. Host 171, disk 74 and separate engine 1,080 tests pass,
with six unavailable optional host replays. Two explicit native Workbench 3.1
floppy boots pass; the app and test CPU assemblies match the exact package DLL.
No HD replay or public release is claimed. Evidence: `consumer-71-final`;
immutable package SHA-256
`ca953178b2a5b29f3a5fbf933db368765079723ce4fe63bc530f45a7f1aa99a8`.

The initial snapshot's unfinished full run was cancelled when the subsequent
wrapping correction was identified. Final corrected-source full-suite and
actual-TRX qualification is recorded below; earlier full-suite evidence is not
substituted. Milestone 6 remains **in progress**, `roadmapComplete=false`.

The host's internal physical-address map rejects logical requests before the
ordinary bus access. This is not a physical port-width/BERR-cycle oracle.
Physical pipeline timing and halted internal register images are not qualified
here. High-address/wrapping validation-fault transport, migrated/foreign private
images, general A/B operand fault generation and data continuation, format-9
coprocessor context transport, wider nested-fault protocols and remaining
reference/consolidation work stay required. Existing timing policy is retained;
no regression is retired or public package published.

### High and wrapped RTE validation transport

A subsequent isolated discovery checks seven low/high/24-bit/32-bit boundary
stack addresses, ISP/MSP, all CCRs, scalar/batch dispatch, each normal-frame
header read and byte lane, and a high VBR. Fault-free frames and a real handler
RTE followed by MOVEQ are verified against independent register/status/stack/PC
and surrounding-memory expectations. On EC020/A1200, 1,024 selected cases
initially failed because the wrapper masked only the start of a map query and
missed a denied physical byte after the 24-bit wrap. The baseline passes the
other 31,232 programs, including all selected 020/030 cases. Evidence:
`audits/Rte020AddressDiscovery`; exact failures and source/TRX/report identities
are independently verified.

The correction queries each physical span at the existing wrap, retaining
optional-interface fallbacks and the original timed byte/word/long accesses.
The isolated corrected tree passes all **32,256 programs / 16 reports / four
executions**, plus 25 retained address-map, optional-interface and cache tests.
Only `M68EC020Interpreter.cs` differs between those production snapshots;
all fixture and other CPU source identities match. Evidence:
`audits/Rte020AddressFix/proof.json`, SHA-256
`c81fc9d0dba3a04fe3b8a54950ec99b345e3567a6c2fdfee1c9056ba274b4c92`.

The correction and fixture are now imported. The maintained mainline command
passes all 32,256 programs with exact source/binary/report identities, execution
roster, independently enumerated cases/weights and actual TRX summaries:

```powershell
./scripts/test-copper68k-020-rte-address-transport.ps1 -OutputDirectory artifacts/020-rte-address-transport
```

Evidence: `audits/Rte020AddressAcceptance`; manifest SHA-256
`b3e02251d176e67bbea201f76536bcb96a1f8eaffcc194948540bb58d65103f7`.
Four copied-evidence controls reject missing reports, changed weights, empty
execution and changed source identity (`Rte020AddressIntegrity`). The original
36,096-case RTE gate also passes against this corrected source, including its
four fresh integrity controls (`audits/Rte020FinalFaultAcceptance`,
`Rte020FinalFaultIntegrity`); manifest SHA-256
`c2912e8877678ca52d883a4d862e13419bea1f2db4fc97db55eeb46568661b81`.

The older `audits/Rte020Full` run was deliberately stopped after this necessary
production correction was identified. Its partial outputs and cancellation
reason remain preserved; it is not accepted full-suite evidence. Fresh final
CPU validation under `audits/Rte020FullFinal` passes **5,291 tests**, zero
failures and **33 explicit optional/discovery skips** (5,324 total), in
46 minutes 19 seconds. Both new RTE matrices and all ten qualified WinUAE
presets are explicitly enabled. The maintained inventory and separate actual-TRX
audit verify all **707 ordinary reports / 86,098,722 deterministic scenarios**
and complete combination weights. Actual-TRX verification SHA-256:
`85f48a39cfc90485fb85876f44d6dfaf006cce4d41a4e54ee2dc64ee1e5eb279`.
Source manifest SHA-256:
`2db5aa39d79ac190a21182238c10744468f40c2056187acd9d8fd797e3d458f1`;
TRX SHA-256:
`c73a6e4e1e54de0a3ea5ed4c8c8fe449278fff74326c6dbe89285ccc8fc9c7e2`.
Clean private .72 consumer qualification passes the
production build with zero warnings/errors, 171 host, 74 disk and 1,080 separate
engine tests, and two explicit native Workbench 3.1 floppy boots. Six optional
host replays remain unavailable, with no HD replay claimed. Runtime app/test
CPU DLLs match the exact package. Evidence: `consumer-72-final`; immutable
package SHA-256
`aab23ec350f487e7baaaa93b943f16c98457e1af815a80a4f10a1b923d12a8f3`;
validated CPU DLL SHA-256
`8ef14985fe1393d4e42ae7faebc1b45ab218533674aa4ebef22050643b439e43`.
.71 is not repacked or substituted for the corrected source. Fresh final
prefetch (8,960 programs), entry (6,144 cases plus 512 controls), SingleStepTests
and Musashi gates also pass against this source (`audits/Rte020FinalPrefetch`,
`audits/Rte020FinalEntry`, `audits/Rte020FinalIndependent`). All three original
RTE production mutations are re-proved after the mapping correction: 448,
320 and 32 intended mismatches respectively, with the same 128 unaffected
software-input controls. Evidence: `mutations/Rte020FinalFaults`, verification
SHA-256 `7bb9c19b6537053e80fe3002ab8d84292e7821bd8e68838d4d1a3db9733d5fd4`.
The final shipping validation commands are cleaned before commit: the fault
command's extra trailing blank line is removed and the address command's
baseline-only branches are removed. Both commands rerun successfully against
the unchanged CPU source, with four fresh corruption controls each. Current
command evidence: `audits/Rte020FinalFaultCleanCommand`, manifest SHA-256
`0641c7d138b165e47edbee3bf87a0c9397403ba39abbeebe410770fdcd65e88b`;
`audits/Rte020AddressCleanCommand`, manifest SHA-256
`1f470977dd122d647297470d8185bd705d3bc3cb8b90b46c6e05ab3a3dedd849`.
Their integrity controls are retained under `Rte020FinalFaultCleanCommandIntegrity`
and `Rte020AddressCleanCommandIntegrity`. Earlier command runs retain their own
producer identities; they are not substituted for these final command gates.
No public package is published. Wider high-address private-frame load/entry faults,
transferred/foreign images and general A/B continuation remain required;
milestone 6 remains **in progress**, `roadmapComplete=false`.

### Next required data-read discovery (isolated source)

While the RTE correction's full suite runs against frozen source, an isolated
archive plus that exact correction reproduces the remaining ordinary operand
read gap. Six literal MOVE source forms (indirect, postincrement, predecrement,
displacement, absolute word and absolute long), byte/word/long sizes, all CCRs,
four stack states, scalar/batch routes and all four 020/030 profiles pass
**18,432 fault-free control programs**, including exact next PC, a following
MOVEQ sentinel, all registers/status/stacks and surrounding memory.
The same fixtures produce **43,008 intended mismatches** when the internal map
rejects an operand request: the ordinary read route never queries/rejects it.
No case is labeled passing or an architectural exception merely because the
instruction silently completes. The selected data-read expectation is format B,
with DF/read/size/function-code information and the operand fault address,
per MC68020UM 6.2 and MC68030UM 8.2.

Evidence: `audits/Operand020Discovery` (16 reports, four executions: two passing
controls and two failing discovery batches). Its independent verifier checks
frozen source/binary identities, exact case keys and weights, all diagnostic
reasons and actual TRX summaries. The fixture remains in that isolated archive
until the current full-run source freeze ends; this is discovery evidence, not
a promoted mainline gate or a completed data-continuation implementation.
The next correction must capture pending operation state and evaluated EAs,
preserve partial effects, and resume only the rejected operation through
explicit RTE; replaying the instruction is not an acceptable replacement.
Full-format pointer stages, writes, other integer families and broader A/B
transport remain required coverage. Milestone 6 remains **in progress**.

#### Isolated read-entry and RTE-rerun development

The next isolated source prototype now rejects the selected operand requests
before ordinary bus access and serializes the pending logical address, consumed
extension PC, opcode, stack and instruction-pipe words in an interpreter-private
format-B context. Both shared scalar reads and the direct fast long
postincrement read are covered. The initial prototype's missed fast route is
retained as failed provisional evidence (`audits/Operand020ReadFixV2`).

Read-entry qualification passes all **43,008 selected fault cases** plus
**18,432 fault-free controls**, with exact sources, four executions, all 16
reports, independently enumerated keys/weights and actual TRX summaries
(`audits/Operand020ReadFixV3/verification.json`, SHA-256
`a7a9bfaa855c9db5ee62a82af6fb41acd3f2dcaccaa65795f171ddd490c268f6`).

A further isolated prototype implements explicit RTE rerun for these simple
memory-source/register-destination MOVE forms. It restores the pending pipe,
reads the saved operand address and finishes the register/flag suffix without
beginning the instruction, consuming extensions or calculating the source EA
again. All **43,008 selected whole recovery programs** and **18,432 controls**
pass, checking the final architectural state, surrounding memory and a
following MOVEQ sentinel. Strict evidence:
`audits/Operand020ReadFixV4/verification.json`, SHA-256
`4c57ba682ae147a5b06bdb68c7115c2ca6da4eda2123619e43c08f0e0075c795`.

Neither prototype is imported or a promoted mainline gate. Software-buffer
repair, persistent refaults, handler-modified operands/extensions, saved-pipe
mutations, A7/alias/index combinations and retained controls still require
qualification before promotion. Full-format pointer stages, memory destinations,
other instruction families and wider A/B transport remain required. The
existing full-suite source remains frozen while this development proceeds;
milestone 6 remains **in progress**, `roadmapComplete=false`.

The next frozen prototype (`audits/Operand020ReadFixV5`) passes **129,024
whole handler-recovery programs / 24 reports / six executions**. Real handlers
supply a different value through DIB and clear DF, persistently reject the
pending read before allowing it, or modify the source base, original opcode and
consumed extension words. Every CCR, selected size/form, stack state and byte
lane runs through scalar/batch dispatch. Complete architectural state and
surrounding memory are checked after each handler instruction, resumption and
following sentinel. Software input causes no ordinary source read; explicit
rerun completes exactly one successful read. Strict verification SHA-256:
`439c7e3dd73b4b877e066af7a37ab23afdff2ae156853a9d46c6b007b4a41913`.

Three isolated mutations produce exactly **43,008** software-input mismatches,
**50,176** saved-address mismatches and **21,504** repeated-predecrement
mismatches, with the remaining 86,016, 78,848 and 107,520 programs respectively
still passing. The proof checks the sole mutated CPU source, unchanged fixtures,
exact execution outcomes, every failing CCR identifier and diagnostic phase,
coverage weights and actual TRX summaries. Evidence:
`mutations/Operand020ReadHandlers/verification.json`, SHA-256
`71f5d52b125c3df5bddc2312f678035791beb373045d3296ac80d34cc253d250`.
All **51 retained executions** pass: the 68,352 previously qualified RTE
programs produce byte-identical reports with matching actual TRX summaries,
and all 25 mapping/cache/optional-interface controls pass. Evidence:
`audits/Operand020ReadFixV5/retained/verification.json`, SHA-256
`6eb70887a60d448fde5d20966ea5a412693fc24a87cf0f9eaeaba99d682f2ad0`.

The prototype remains unimported and unpromoted. Before promotion, pending-read
origin must be distinguished from a later exception/vector read rather than
inferred solely from the retained opcode; that distinction is not yet qualified.
Saved-pipe mutations, A7/alias/index combinations, trace/interrupt and changed
return-SR/stack behavior, high-address transport and evidence-integrity controls
also remain required. Broader pointer, memory-destination and other-family
continuation stays open. No private consumer package is built for this prototype;
the next such version remains .73. Milestone 6 remains **in progress**.

#### Typed trace-vector read continuation — 2026-10-07

The next isolated discovery distinguishes a pending exception read from a
completed MOVE source read. With T1 set, a repaired MOVE completes its register
and flag effects, creates the format-2 trace frame, then encounters a rejected
vector-9 read. The V5 opcode-only recovery path incorrectly treats that vector
as another source operand. The independent fixture retains **18,432 passing
controls**, **24,576 long-size mismatches** and **49,152 byte/word unsupported
programs**; these failed discovery batches are not promoted coverage.
The documented ordering is in [MC68020UM](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
6.1.11 and [MC68030UM](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
8.1.12: a bus fault during trace processing must be handled before that
processing finishes.

The frozen corrected prototype serializes a typed pending-read kind and the
pending indexed timing-policy state. A trace-vector continuation retries only
its saved read (or accepts DF-cleared DIB input), then finishes the existing
exception timing policy. It neither reexecutes MOVE nor pushes another trace
frame. Unknown read origins remain explicitly unsupported on return. All
**92,160 selected programs / 16 reports / four executions** pass across
EC020/A1200/020/030, scalar/batch routes, six source forms, three sizes, four
stack states, every CCR and all vector-read byte lanes. Removing the trace
continuation produces exactly **73,728 mismatches**, with **18,432 unchanged
controls** passing. The strict proof checks both changed CPU files, unchanged
fixtures, precise failure identifiers/reasons, combination weights and actual
TRX summaries. Evidence:
`audits/Operand020TraceVectorFixV2/proof.json`, SHA-256 `c5168b47442dcd246c206b0d784596d603c463e9504a0fd3bd109d4d67f6e5d2`;
fixed verification SHA-256 `f4cd4ac39c04d200a9d73137d2e1ac2183f15e1332199e87ba6c1728c17392d8`.

An additional canonical-indirect fixture passes **73,728 complete programs /
48 reports / 12 executions**, with and without the earlier source fault.
Real handlers supply a different trace target through DIB, persistently refault
before allowing the pending read, or change VBR through MOVEC. All code is
prepared before execution; the source handler installs the subsequent bus-error
handler with a real memory write. Per-instruction architectural state, all
stacks, trace-frame fields, surrounding memory, exception counts, source/vector
read counts and the following sentinel are checked. A VBR change preserves
the already-evaluated pending vector address. Ignoring DIB and recalculating
that address from VBR each produce **24,576 precise mismatches**, with **49,152
unaffected programs** passing. Evidence:
`audits/Operand020TraceVectorHandlersV2/verification.json`, SHA-256 `6277bbd11424c486d6e0c2e1ad12d451fa196f41e7cb72e3d6a3840175931bf6`;
mutation verification SHA-256 `dc882d0f264639ffa4049c0546f1f48e46fe6fa2eedaff2a7729aa5aa18b283d` and `85982c02016c89b78c3da8cc300ffd6cb6fa5741a15d716967553ffb131c82b0`.
The strict verifier also rejects a missing report, an empty execution selection
and an incorrect per-combination weight; its separate controls do not count as
CPU programs.

Fresh corrected-source retention passes all **129,024 earlier handler programs**
and all **51 retained executions**: **68,352 RTE programs** have byte-identical
reports and matching actual TRX summaries, plus **25 mapping/cache/optional
interface controls**. These source-specific gates validate the typed prototype;
the delivered production full-suite/private .72 results apply to the earlier
RTE/map correction, not this prototype. The failed initial handler fixture
compile attempt is retained separately and supplies no passing coverage.

This development remains isolated, unimported and unpromoted. Independent
nested vector-frame PC provenance, T0/interrupt interactions, other exception
read origins, saved-pipe mutation and warm dispatch, A7/aliases/indexing,
changed return SR/stacks, wider fault transport, memory destinations and
other-family continuation remain required. Existing reference and consolidation
gaps remain open. No package is built or published; the next private consumer
version remains .73. Milestone 6 remains **in progress**, `roadmapComplete=false`.

#### A7/MOVEA and captured private-pipe qualification — 2026-10-07

Two new isolated fixtures qualify the unchanged typed-read CPU source above.
The A7 fixture passes **196,608 programs / 32 reports / eight executions**:
**21,504 fault-free controls** and **175,104 explicit repair, software-input
and persistent-refault programs**. EC020/A1200/020/030 run scalar/batch routes,
indirect/postincrement/predecrement sources, MOVE byte/word/long, MOVEA word/long
to A0 and aliased A7, four stack states, all CCRs and all operand fault byte
lanes. Literal expected strides include two bytes for A7 byte operations;
MOVEA.W sign extension, flag preservation and destination-after-source ordering
are independent of production helpers. All architectural registers/status,
active and inactive stacks, defined bus-error fields, surrounding memory,
exception/read counts and a following MOVEQ sentinel are checked after each
handler and recovery instruction. Changing the resumed byte stride to one
causes exactly **3,072 mismatches / 193,536 unaffected passes**. Moving the
source update after an aliased MOVEA destination write causes exactly
**18,432 mismatches / 178,176 unaffected passes**.

The captured-pipe fixture passes **86,016 programs / 32 reports / eight
executions**. A real instruction-prefetch error and handler/RTE repair supply
the opcode and following word; the subsequent ordinary MOVE fault genuinely
captures that remaining word. Real handlers write a different following opcode
to memory, and separately replace the saved private pipe word. This covers
three extension-free source forms, three operand sizes, four stack states,
all CCRs/operand byte lanes, both routes and cold/warmed dispatch on all four
profiles. Warming uses batch dispatch and requires host-code-reader use;
recovery then uses the selected scalar or batch route. The original saved word
and software-edited word both override different memory/cached candidates.
Discarding the pipe causes **86,016 mismatches**; ignoring the edited pipe word
causes **43,008 mismatches / 43,008 unaffected passes**. These checks qualify
interpreter-private continuation and existing execution policy, not physical
cache/pipeline contents or timing.

Each strict case verifier checks exact executions, every combination/CCR weight,
precise mutation failure identifiers/reasons and matching actual TRX summaries.
A separate complete-input audit independently enumerates all CPU/test C# and
project sources, checks every file hash, pins the execution helper/base inputs
and links the selected-case proof to its TRX. **Ten separate integrity controls**
(five per gate) reject omitted fixtures, changed source, altered producer
identities, empty execution selections and wrong combination weights. Mutation
fixtures are byte-identical to their accepted parent; only the intended private
operand-continuation CPU file changes.

Evidence: `audits/Operand020StackRead`, `audits/Operand020PipeReadV4`,
`mutations/Operand020StackRead`, `mutations/Operand020PipeRead`; aggregate proof
`operand020-stack-pipe-proof.json`, SHA-256 `2d6b57c4a244c8efea96902886e2889d10b2e5c586b08351dc631da706d7b432`. Its six entries pin
all case and input-integrity verifiers; it also pins the ten corruption controls.
The earlier pipe fixture's assumption of an initially non-empty pipe, a tuple
compile error and incorrect scalar cache priming remain separate failed
fixture evidence. None is relabeled as a production CPU defect or passing run.

No production CPU source, timing policy, package or regression retirement changes.
The isolated prototype remains unimported and unpromoted. Broader register/index
aliases, extension-bearing and multiword captured pipes, nested-PC provenance,
other read origins, trace/interrupt and changed return-SR/stack combinations,
wider fault transport, memory destinations and other-family continuation remain
required alongside all existing reference/consolidation gaps. Milestone 6 stays
**in progress**, `roadmapComplete=false`.

#### PC-relative operand function codes — 2026-10-07

The next literal fixture demonstrates a function-code defect in the isolated
read prototype. [M68000PM section 2](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
and [MC68030UM-P1 section 2/Table 4-1](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P1.pdf)
classify PC-relative operands as program references: FC 2 in user mode and
FC 6 in supervisor mode. Part 1 section 6.1.2 nevertheless places their operand
data in the data cache, and section 4.3 places non-reset exception vectors in
supervisor data space. Program function codes therefore do not justify changing
the existing operand bus access kind to instruction fetch.

The frozen discovery passes **9,216 fault-free controls** and retains **64,512
precise SSW mismatches**: source faults incorrectly carry FC 1/5 instead of 2/6.
The fixtures use independently selected positive, negative and unaligned operand
locations, literal displacements, byte/word/long MOVE, every CCR and fault byte
lane, four stack states, EC020/A1200/020/030 and scalar/batch dispatch with a
host code-reader adapter. These discovery failures remain failed evidence.

The isolated correction captures the function code for the typed pending read
and validates it on explicit RTE. A MOVE PC-relative source uses program space;
a trace-vector read uses supervisor data space even when the retained opcode
is PC-relative. Existing bus access kinds, operand evaluation, extension order,
partial effects and timing policy are unchanged. All **73,728 selected programs /
32 reports / eight executions** pass, including controls, different DIB input,
persistent refault and handlers that modify the source opcode/displacement.
Per-instruction architectural state, stacks, defined frame fields, surrounding
memory, exception/read counts and a following sentinel are checked.

A separate PC-relative trace fixture passes **73,728 programs / 48 reports /
12 executions**, with and without an earlier source fault, including software
vector input, refault and changed VBR. It independently requires vector FC 5;
the operand's program-space classification cannot leak into this later read.
Discarding program-space capture produces exactly **64,512 SSW mismatches /
9,216 unchanged control passes**. Deriving the vector code from the stale opcode
produces exactly **73,728 mismatches**, each FC 6 where FC 5 is required.

Fresh corrected-source retention passes **645,888 previously qualified programs**,
with **272 byte-identical reports** and matching actual TRX summaries, plus all
**25 mapping/cache/optional-interface controls** (**89 executions total**).
These include the previous handler, A7/MOVEA, captured-pipe, trace, RTE fault
and RTE transport gates. Strict verifiers check exact executions, independently
enumerated keys/CCR weights, precise mutation identifiers/reasons, all source
and producer/base-input identities and actual TRX summaries. Ten separate
corruption controls reject omitted fixtures, changed source, altered producers,
empty execution selections and wrong weights.

Evidence: `audits/Operand020PcRelativeDiscovery`, `audits/Operand020PcRelativeFix`,
`audits/Operand020PcRelativeTrace`, `audits/Operand020PcRelativeRetained` and
`mutations/Operand020PcRelative`. Aggregate proof `operand020-pcrel-proof.json`,
SHA-256 `c19704ef4687311f61f14aa19e204e93509303453be7a26cf8f28edc8a40f527`, pins all six case/input-integrity proofs and corruption
controls. It confirms that the correction changes only the private operand
continuation CPU file, with unchanged witness fixtures; each mutation likewise
changes only that file. The production CPU source and public API remain unchanged.

This correction remains isolated and unpromoted. Indexed PC-relative/pointer
stages, other integer-family address-space handling, wider fault transport,
more pipe/alias combinations, nested-PC provenance, trace/interrupt and changed
return-SR/stack behavior remain required with all earlier reference/consolidation
gaps. No package is built or published. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

#### Indexed MOVE source and pointer stages — 2026-10-07

The next frozen discovery passes **11,520 fault-free literal controls** and
retains **105,984 PC-indexed SSW mismatches / 105,984 unsupported An-indexed
RTE continuations**. Its 18 independent witnesses cover brief signed/scaled
word and long indexes, an aliased address-register index, full null/word/long
base displacements, base/index suppression, unaligned and high/negative
addresses, and pre/post memory indirection with null/word/long outer
displacements. An and PC sources, MOVE B/W/L to an aliased index register,
MOVEA W/L to the source base, four stack states, CCR 00/1F, every rejected byte
lane, EC020/A1200/020/030 and scalar/batch routes are selected. This is a bounded
extension/stage matrix; it does not claim every full-format structural or
register combination. The earlier all-CCR gates remain separately retained.

An isolated correction explicitly distinguishes the indirect-pointer read
from the final operand read. It serializes the consumed extension, selected
timing policy and already-evaluated outer/post-index component in versioned
interpreter-private context. RTE performs only the suspended read and its
remaining suffix. A pointer DIB supplies a different pointer and therefore
selects a different operand address; an operand DIB supplies a different value
without rereading memory. Persistent faults remain observable. Real handlers
alter base/index registers and consumed instruction words, and change already
read pointers. Recovery preserves the saved calculation and never retries the
instruction or repeats completed pointer reads.

[M68000PM 2.2.7–2.2.15](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
provides the addressing rules. [MC68030UM-P1 section 2/Table 4-1](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P1.pdf)
provides program-space classification for PC-relative references, including
suppressed-PC forms. [Part 2 section 8.2.2](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
defines DF/DIB software completion. Operand bus access kinds remain unchanged;
the private context is not a claim about physical pipeline/cache contents.

All **223,488 selected programs / 40 reports / ten executions** pass, with
independent architectural register/SR/stack/memory/frame expectations, exact
next PC, ordered pointer/operand read widths and counts, exception counts and
a following sentinel. A separate **223,488-program** policy gate checks literal
retained timing keys/cycle policies and full-indexed 030 head/tail shapes after
both ordinary and recovered execution. These are existing approximate policies,
not physical timing qualification. Fresh retention passes **793,344 previously
qualified programs / 352 byte-identical reports / 109 executions**, including
all **25 mapping/cache/optional-interface controls**.

Discarding the saved pointer offset produces exactly **81,920 mismatches /
141,568 unaffected passes**. Repeating the recovered operand read produces
exactly **89,856 mismatches / 133,632 unaffected passes**. Replacing the captured
indexed timing policy with the general MOVE policy produces exactly **211,968
policy mismatches / 11,520 unchanged control passes**. Each mutation changes
only the private continuation CPU file; its fixtures are unchanged. Strict
verifiers independently enumerate all keys/weights and precise failure
identifiers/reasons, match actual TRX execution summaries, and verify complete
source and producer/base-input inventories. Ten deliberate corruption controls
reject omitted/changed fixtures, wrong producers, empty executions and wrong
weights for the read and policy gates.

Evidence: `audits/Operand020IndexedDiscovery`, `audits/Operand020IndexedFixV2`,
`audits/Operand020IndexedPolicy`, `audits/Operand020IndexedRetained`,
`mutations/Operand020Indexed`; aggregate proof `operand020-indexed-proof.json`,
SHA-256 `55c6d6caccb956d708a48d78f3c2f78cb4e4169329f0d904bbae4026db771d99`. The first correction snapshot's missing
narrowing casts remain a distinct failed compile, not passing qualification.
The accepted correction changes three isolated CPU files; production source,
public API, packages and regression retirement remain unchanged.

The prototype stays isolated and unpromoted. All-structural indexed/address
alias coverage, chained pointer/operand faults, extension-bearing/multiword
captured pipes, nested-PC provenance, other read origins, trace/interrupt,
changed return-SR/stack, wider transport, memory destinations and other-family
continuation remain required with the earlier reference/consolidation gaps.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

#### Chained indexed pointer and operand faults — 2026-10-07

The unchanged isolated indexed prototype now passes **365,184 programs / 56
reports / 14 executions**: **5,760 controls** and **359,424 chained-fault
programs**. Nine literal full-format witnesses select pre/post indirection,
null/word/long displacements and suppression, An/PC sources, MOVE B/W/L and
MOVEA W/L, four stack states, CCR 00/1F, every pointer/operand fault byte lane,
EC020/A1200/020/030 and scalar/batch dispatch. Both faults are armed before
execution; guest state changes occur through real handler instructions.

The first handler redirects the bus-error vector to a separate second handler.
Its RTE completes the pointer stage and then faults on the operand. Each frame
must retain the original resumed MOVE's PC/SR, consumed instruction context,
correct access address/width/function code and stack bank. The second frame
must identify the operand stage, without restoring a stale pointer stage.
Checks cover different pointer DIB input (and therefore a different operand
address), different operand DIB input, both inputs together, an additional
persistent operand refault, and handlers changing base/index registers,
consumed opcode/extensions and an already-read pointer. Architectural state,
all stacks, surrounding memory, exact next PC, ordered rejected/completed
access widths/counts, exception counts and a following sentinel are checked.
Literal retained full-indexed timing policies and 030 head/tail shapes also pass.

[MC68030UM-P2 sections 8.1.2 and 8.2.2–8.2.3](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
provide bus-error status capture and DF/DIB/RTE completion rules. The saved
stage and resumed-instruction provenance checks qualify the interpreter's
serialized continuation model. They do not qualify physical overlapped
instruction/pipeline state or all exception-PC origins.

Three isolated CPU mutations detect exactly **359,424 stale-stage failures**,
**359,424 RTE-PC provenance failures**, and **239,616 repeated-pointer-read
failures**, with **5,760**, **5,760**, and **125,568** unaffected passes respectively.
Each changes only the private operand-continuation file; witness fixtures are
byte-identical to their accepted parent. Strict verifiers independently
enumerate the full selection and weights, verify every precise mutation
identifier/reason, match actual TRX summaries and pin complete source,
producer and base-input identities. Five deliberate corruption controls reject
omitted/changed fixtures, wrong producers, empty executions and wrong weights.

All CPU/project and preexisting fixture sources remain byte-identical to the
previous indexed correction. Its accepted retention evidence (**793,344
programs / 352 reports / 109 executions**, including 25 map/cache controls)
is reused after verifying unchanged CPU identities; no fresh retention replay
is claimed. The first fixture placed its longest alias handler over the second
handler: **26,624 failures** remain distinct invalid-fixture evidence, with
338,560 other cases passing. A fresh fixture moves the second handler to a
disjoint region and checks their extents. The repair changes only that fixture;
no CPU defect is inferred from the overlap.

Evidence: `audits/Operand020IndexedChain`, `audits/Operand020IndexedChainV2`,
`mutations/Operand020IndexedChain`; aggregate `operand020-indexed-chain-proof.json`,
SHA-256 `7531f558fe7391aa96b0c69fd557105fb6e740d7b64841172ac4f51bacf3825a`. It links all four selected/input proofs,
three mutations, five corruption controls, invalid-fixture repair and the
previous unchanged-source retention proof. Production source, public API,
packages and regression retirement remain unchanged. The prototype remains
isolated and unpromoted. All-structural indexed/register aliases, broader
chained faults, captured multiword pipes, other exception-PC origins,
trace/interrupt, changed return-SR/stack, wider transport, memory destinations,
other families and all existing reference/consolidation gaps remain required.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

#### All legal full-indexed read structures — 2026-10-07

The unchanged isolated CPU prototype passes **786,432 logical programs / 32
reports / nine executions**, with zero mismatching, unsupported or untested
cases in this selection. The ninth execution checks **20 fixed An/PC fixture
examples**. Shared test-internal `IndexFixture.FullStructures()` and
`AddressingFixture` prepare all **66 legal full-format structures**: both
suppression bits, null/word/long base displacements, direct addressing and
all legal pre/post-indirect outer-displacement forms. Reserved combinations
remain excluded according to [M68000PM table 2-2](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf).

The separate deterministic matrix crosses An/PC bases, MOVE byte/word/long,
MOVEA word/long, four 020/030 models/profiles, scalar/batch routes, four stack
states, CCR 00/1F, and every byte lane of pointer/operand reads. It includes
**42,240 fault-free controls** and **744,192 recovery programs** with repair,
data-input-buffer and persistent refault handlers. Index D7.W=-8 and
register destinations D2/A0 are canonical; this does not qualify all register,
index-width, scale or alias combinations. The generator never calls production
EA helpers. Fixed literal encodings and addresses anchor the shared fixture.

Legal operands/pointers overlapping instruction extensions, including
suppressed PC bases and address zero, stay in the matrix. Bytewise setup
preserves code, expectations capture its real operand/pointer bytes, and the
evaluated outer/index offset is frozen before handlers execute. Checks cover
architectural registers, defined SR, PC, stack selection, memory guards,
exception delivery, ordered read widths/counts and a following sentinel.
This matrix does not add physical timing qualification or an all-structure
timing-policy gate; the preceding literal timing-policy evidence is retained.

A sole CPU mutation duplicates resumed **word** operand reads. With all
fixtures unchanged it causes exactly **67,584 identified mismatches**, each
for pointer/operand access ordering or count, while **718,848 unaffected
programs pass**. Five deliberate integrity controls reject omitted/changed
fixtures, wrong producer identity, empty execution selection and incorrect
combination weights. Independent verifiers enumerate the complete source
inventory and architectural keys, check exact logical weights against actual
TRX output, and match every intended mutation failure identifier and reason.

Evidence: `audits/Operand020IndexedStructures`,
`mutations/Operand020IndexedStructures/RepeatWordRead`; aggregate
`operand020-indexed-structures-proof.json`, SHA-256
`e62a7da9ca29637cc5ce46ad943784051a28cebdc113c5eab7fd81bcfe9f353f`.
It pins selected inputs/TRX, verifier identities, the mutation, five integrity
controls, and the preceding chained proof. All CPU/project and preexisting
fixture sources remain byte-identical; accepted retention is reused, with no
fresh retention replay claimed. Protected normal assemblies remain unchanged.
No production import, API/package change, publication or regression retirement
is included. Register/index/alias permutations, all-structure chained faults,
wider captured pipes and provenance/transport, other read origins,
trace/interrupt, return-SR/stack, memory destinations, other families and
remaining reference/consolidation gates stay required. Milestone 6 remains
**in progress**, `roadmapComplete=false`.

#### Indexed register, index-size, scale and alias groups — 2026-10-07

The unchanged isolated prototype passes **768,000 programs / 256 reports / 65
executions** with zero mismatching, unsupported or untested cases in this
selection. Six fixed opcode/extension examples validate the shared
`MoveSpecification`/`IndexFixture` encodings. Two separate deterministic groups
complement the preceding 66-structure matrix:

- **460,800 register programs:** all eight An bases plus PC, all eight
  destinations, all eight D/A indexes, at canonical word-index/scale-one
  settings. This includes source/destination/index register aliases.
- **307,200 index programs:** word/long indexes and scales 1/2/4/8 across
  distinct, base/destination, destination/index, base/index and all-register
  aliases, plus PC-relative destination/index aliases, rotated through all
  register numbers.

Both groups cover brief addressing, full direct, full preindexed and full
postindexed canonical structures, MOVE byte/word/long and MOVEA word/long,
four 020/030 models/profiles and scalar/batch routes. The selected state is ISP,
CCR 1F, with one canonical fault lane; other stack/CCR/read-lane combinations
remain covered by their preceding matrices. There are **307,200 controls** and
**460,800 real alias-handler programs**. D-index input `1000FFF8` distinguishes
negative word indexes from high long indexes; An inputs retain the fixture's
positive address/stack values. Negative address-index values and wider numeric
boundaries remain a separate required group.

Real handlers change participating D and A0-A6 registers, rewrite consumed
opcode/extensions, and change already-completed pointers. They preserve A7
while it addresses the active handler frame. A7 source/index cases and MOVEA
A7 destinations remain included; architectural checks verify ISP changes along
with the resulting A7 value. Expected EAs/index offsets are established before
CPU execution without production helpers. A resumed source reads current
expected memory after real handler writes, including a self-code operand.
All registers, defined flags, PC, stacks, memory guards, exception fields,
ordered read widths/counts and the following sentinel are checked.

A sole CPU mutation drops the saved outer/post-index offset during pointer
recovery. Every one of the **153,600 intended failures** has its exact
identifier and register/flag mismatch verified; **614,400 unaffected programs
pass**. Five corruption controls reject omitted/changed fixtures, wrong
producers, empty executed selections and wrong weights. Independent source,
execution-roster, architectural-key and logical-weight checks pass.

The first two snapshots remain distinct invalid-fixture evidence: the first
has **744,656 passes / 23,344 expectation failures** from self-code data changes
and stale ISP expectations; the second has **744,960 passes / 23,040 stale-ISP
expectation failures**. Their complete failure identifiers/reasons are audited.
Fresh repairs modify only the fixture, never the CPU or an executed snapshot.
No CPU defect is inferred from those failures.

Evidence: `audits/Operand020IndexedRegisters`,
`audits/Operand020IndexedRegistersV2`, `audits/Operand020IndexedRegistersV3`,
`mutations/Operand020IndexedRegisters/DiscardOffset`; aggregate
`operand020-indexed-registers-proof.json`, SHA-256
`ba6093c39caf7695c6933f7c3de537304c3fbf781b5a2d086b26b4c07ca04fd5`.
It pins all four selected executions, complete 217-file source inventories,
verifier/binary identities, fixture-only repairs, the sole CPU mutation, five
corruption controls and the previous unchanged-source proof. Existing
source-specific retention/timing evidence is reused; no fresh retention or
all-structure timing gate is claimed. Protected normal assemblies, production
source, API/packages and regression retirement remain unchanged; the prototype
stays isolated and unpromoted. Signed address-index/value boundaries,
all-structure chained faults, broader pipes/provenance/transport, other read
origins, trace/interrupt, return-SR/stack, memory destinations, other families
and existing reference/consolidation work remain required. Milestone 6 stays
**in progress**, `roadmapComplete=false`.

#### Signed index boundaries and user-stack A7 — 2026-10-07

The unchanged isolated prototype passes **1,638,400 programs / 1,024 reports /
257 executions**, with zero mismatching, unsupported or untested cases in the
selected matrix. **64 fixed address examples** independently anchor D/A word
and long indexes at scales one/eight; execution covers all four scales. Word
sign extension and scale encodings follow [M68000PM table 2-1](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf).
The eight fixed raw register values are `80000000`, `80007FFF`, `80008000`,
`8000FFFF`, `7FFFFFFF`, `FFFFFFFF`, `1000FFF8` and zero. They distinguish low-word
sign boundaries from complete long values and exercise scaling/32-bit wrapping.

The separate deterministic group rotates destination/index, base/index,
all-register and PC-relative aliases through every D/A index register. It
crosses word/long indexes, scales 1/2/4/8, brief/full-direct/pre/post canonical
structures, MOVE byte/word/long and MOVEA word/long, four 020/030 models/profiles
and both execution routes. There are **655,360 controls** and **983,040 real
alias-handler programs**, at user CCR 1F and a canonical fault lane. Other
stack/CCR/lane combinations retain their preceding evidence; this is not an
all-structure/value Cartesian product.

A7 address-index inputs use the selected raw value as the user stack pointer,
including zero, high/negative and odd values, with a separate supervisor handler
frame. MOVEA A7 updates USP and the architectural register together. Handlers
alter participating D and A0-A6 registers, consumed opcode/extensions and
completed pointers while preserving their active frame pointer. Operand/pointer
access spans are asserted disjoint from handler and exception-frame regions;
setup protects code through the model's physical address mapping. Expected
addresses and evaluated offsets use the shared test fixture, never production
EA helpers. Architectural state, logical fault addresses/function codes,
ordered read widths/counts, memory guards and the following sentinel pass.

A full preindexed PC case deliberately reads its own extension words as a
pointer. Recovery reads the real pointer bytes after handler writes at the
**saved pointer address**, retaining the already-evaluated outer/index offset.
Final-operand recovery similarly retains its saved EA when the handler changes
a completed pointer. The first snapshot incorrectly expected an unchanged
pointer value: **1,637,760 other programs pass / 640 fixture-expectation failures**
remain distinct audited invalid evidence. A fresh repair changes only that
fixture, not CPU source or an executed snapshot.

A sole CPU mutation discards the saved offset at pointer recovery. Exact
identifiers/reasons verify all **327,680 intended failures**, with **1,310,720
unaffected passes**. Of those failures, **640** still produce the same zero
register/flag result and are detected by the access-order/count checks; the
other **327,040** produce the expected register/flag mismatches. Five deliberate
integrity controls reject omitted/changed fixtures, wrong producer identities,
empty executed selections and incorrect weights. Complete source inventories,
actual test rosters/TRX summaries and independently enumerated architectural
keys/logical weights pass.

Evidence: `audits/Operand020IndexedBoundaries`,
`audits/Operand020IndexedBoundariesV2`,
`mutations/Operand020IndexedBoundaries/DiscardOffset`; aggregate
`operand020-indexed-boundaries-proof.json`, SHA-256
`9252f8f0c37250b07224ad2e2ef90defedd19e238fbb4faba77c293acad982a7`.
It pins three complete selected executions, 218-file source inventories,
verifier/binary identities, fixture-only repair, the sole CPU mutation, five
corruption controls and the preceding unchanged-source proof. Accepted
source-specific retention/timing evidence is reused; no fresh retention replay
or physical/all-structure timing qualification is claimed. Protected normal
assemblies, production CPU source, API/packages and regression retirement
remain unchanged. The prototype stays isolated and unpromoted. All-structure
chained faults, broader captured pipes/provenance/transport, other read origins,
trace/interrupt, changed return-SR/stack, memory destinations, other families
and remaining reference/consolidation work stay required. Milestone 6 remains
**in progress**, `roadmapComplete=false`.

#### All-structure chained indexed faults — 2026-10-07

The unchanged isolated prototype passes **2,198,784 programs / 336 reports /
85 executions**, with zero mismatching, unsupported or untested cases in the
selected matrix. All **66 legal full-format structures** retain **42,240
controls**. The **54 pointer-bearing structures** execute **2,156,544 chained
programs**; the **12 direct structures** have no pointer access and are
explicitly excluded from chains with that architectural reason. Four fixed
encoding/pointer examples anchor the shared fixture.

The matrix crosses An/PC, MOVE byte/word/long and MOVEA word/long, four stack
states, CCR 00/1F, every pointer and operand fault lane, scalar/batch execution
and EC020/A1200/020/030 profiles. Canonical D2.W is -8; previous register,
size/scale and signed-boundary groups retain their separate evidence. Groups
partition displacement length and base suppression to preserve every failure
identifier without report truncation.

Real handlers repair accesses, supply pointer/operand/both input buffers,
refault, redirect the second exception vector and alter registers, consumed
code and completed pointers. Checks cover original logical instruction PC/SR,
private stage advance, saved addresses/function codes, architectural registers
and stacks, ordered access widths/counts, memory guards and the following
sentinel. Full-index native cycles, head/tail and barrier expectations pass
across selected structures. These are the existing approximate timing policy,
not physical pipeline/bus qualification; private frame fields are not claims
about silicon frame contents.

The compile-only initial snapshot has a constant-to-ushort narrowing error
and no executed coverage. Its fresh fixture-only repair masks the low vector
address word. The next snapshot has **2,192,128 passes / 6,656 invalid-fixture
failures**: a completed-pointer write overlapping PC extension storage replaces
the following sentinel with BRA.W, which the CPU correctly executes. A fresh
fixture-only repair changes the pointer while preserving that sentinel's low
word. Neither invalid snapshot counts toward the passing gate; no CPU bug is
inferred from either failure.

A sole CPU mutation leaves recovery at the pointer stage instead of advancing
to the operand stage. Against the repaired fixture it produces exactly
**2,156,544 identified stage mismatches**, preserving **42,240 controls**.
An earlier mutation against the invalid-sentinel fixture remains a separate
prefix probe. Complete 219-file source inventories, actual TRX rosters,
independently enumerated keys/weights and five corruption controls pass. The
controls reject omitted/changed fixtures, wrong producer identity, empty
execution selections and incorrect weights.

Evidence: `audits/Operand020IndexedFullChain`,
`audits/Operand020IndexedFullChainV2`, `audits/Operand020IndexedFullChainV3`,
`mutations/Operand020IndexedFullChain/KeepPointerStage` and
`mutations/Operand020IndexedFullChain/KeepPointerStageV3`; aggregate
`operand020-indexed-full-chain-proof.json`, SHA-256
`eb64a95e6b2ac90d073bb073f7446721b23d7d4662f9acd66001744b303cf52d`.
It pins complete inventories, producer/verifier/binary identities, exact
fixture-only repairs, the sole CPU mutation, integrity rejections and the
preceding signed-boundary proof. Existing source-specific retention is reused;
no fresh retention replay is claimed. Protected normal assemblies, production
source/packages and regression retirement remain unchanged. The prototype
stays isolated, unimported and unpromoted. Wider captured multiword/extension
pipes, provenance/transport, other read origins, trace/interrupt, changed
return-SR/stack, memory destinations, other families and remaining reference/
consolidation gates stay required. Milestone 6 remains **in progress**,
`roadmapComplete=false`.


#### Three-word private pipe transport — 2026-10-07

The unchanged isolated prototype passes **1,003,520 programs / 160 reports /
40 executions** for a following three-word MOVE.L. Real handlers replace its
backing opcode and both immediate words, then supply a private saved pipe of
length **0, 1, 2 or 3**, with unchanged words or an edit at each retained
position. The expected instruction combines the supplied prefix with the
changed memory suffix; its destination, complete long value, flags, next PC
and following NOP distinguish each position and fetch boundary. MOVE encoding
and flag expectations use the test-internal specification and literal cases;
architectural semantics follow the [programmer reference's MOVE description](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf).

The matrix crosses indirect/postincrement/predecrement, displacement,
absolute-word/long and PC-displacement sources; byte/word/long operand reads;
all four stack states, all 32 initial CCR states, every read lane, scalar/batch
execution, cold/warm host-reader preparation and EC020/A1200/020/030 profiles.
An actual odd-prefetch exception and software RTE prepare the stream before
the operand fault. Originally captured entries are checked for address/content;
the handler-supplied pipe is checked through subsequent execution. Completed
operand effects, stack selection, original PC/SR, operand read count/width,
architectural registers and memory remain verified. This qualifies interpreter-
private software frame transport, not three-word silicon capture or physical
pipeline/cache timing. Wider indexed/chained pipes remain required.

The first three-source-form matrix separately passes **430,080 programs**.
Its extended fixture incorrectly expected a zero extension-word write to clear
CCR.Z. The original extended snapshot retains **860,160 passing programs /
143,360 precisely identified invalid-fixture failures**; a fresh repair changes
only that expectation, preserving the failed snapshot and unchanged CPU source.
No production defect is inferred from that fixture error.

Two sole CPU mutations corrupt the saved middle and final words during RTE.
The repaired fixture detects exactly **702,464 middle-word failures / 301,056
unaffected passes** and **401,408 final-word failures / 602,112 unaffected
passes**. Every failure identifier and wrong-register-value reason is audited;
the mutations do not change fixtures. Complete 220-file source inventories,
actual TRX rosters/summaries, independently enumerated architectural keys and
weights, and five integrity controls pass. Those controls reject omitted or
changed fixtures, wrong producers, empty executions and incorrect weights.

Evidence: `audits/Operand020MultiwordPipe`,
`audits/Operand020MultiwordPipeExtended`,
`audits/Operand020MultiwordPipeExtendedV2`,
`mutations/Operand020MultiwordPipeExtended/Middle` and
`mutations/Operand020MultiwordPipeExtended/Last`; aggregate
`operand020-multiword-pipe-proof.json`, SHA-256 `43f3034390925f06d8cd2eceefb18c74ee29187ee82ef3218a08493dc70b0a0f`.
The proof pins all five executions, sources, binaries, producer/verifier
identities, fixture-only repair, sole mutations, integrity rejections and the
preceding chained-structure proof. Existing source-specific retention is reused;
no fresh replay is claimed. Protected normal assemblies, production source,
API/packages and regression retirement remain unchanged. The prototype stays
isolated, unimported and unpromoted. Wider pipes, nested PC/SR provenance,
transport, other read origins, trace/interrupt, changed return-SR/stack, memory
destinations, other families and remaining reference/consolidation gates stay
required. Milestone 6 remains **in progress**, `roadmapComplete=false`.


#### Nested operand and trace-vector provenance — 2026-10-07

The unchanged isolated prototype passes **516,096 programs / 336 reports /
84 executions** for direct traces and traces after operand-read recovery.
Seven MOVE source forms cover indirect/postincrement/predecrement,
displacement, absolute-word/long and PC displacement, byte/word/long sizes,
four stack states, all 32 initial CCR states, all four vector fault lanes,
scalar/batch execution and EC020/A1200/020/030 profiles. The operand fault uses
a canonical lane; earlier lane matrices retain their separate evidence.

Real operand handlers replace consumed opcode/extensions with NOPs and install
the later vector handler. Trace-vector handlers supply different vector input,
refault or change VBR. Both initial and repeated vector faults retain the
original instruction PC/opcode, consumed next PC, long vector-read width and
typed pending-vector state. Trace frames preserve the post-MOVE SR and next
instruction PC, together with the serialized original-instruction address.
Register/stack/memory checks at each handler instruction, exact exception
counts, completed operand-read counts and the following sentinel pass.
[MC68030UM section 8.1.7](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
defers tracing until a suspended instruction completes and saves its resulting
SR and next PC. The private nested vector-fault fields qualify the interpreter's
serialized policy; they do not establish physical pipeline-overlap provenance
or silicon-private frame contents. T0/interrupt and wider origins remain open.

The first fixture left relocated bus-error/trace handlers' initial vectors at
the old addresses. Its **86,016 passes / 430,080 precisely identified invalid-
fixture failures** remain separate evidence. A fresh repair changes only the
fixture's two vector initializations; no CPU defect is inferred.

A sole PC mutation loses the original instruction address after operand RTE:
**258,048 intended failures / 258,048 unaffected passes**. A sole SR mutation
discards completed MOVE flags: **241,920 intended failures / 274,176 unaffected
passes**. The latter preserves cases whose initial flags already equal the
MOVE result. Every failure identifier/reason is verified; both mutations leave
fixtures unchanged. Complete 221-file source inventories, exact TRX rosters,
independently enumerated keys/weights and five corruption controls pass.

Evidence: `audits/Operand020TraceProvenance`,
`audits/Operand020TraceProvenanceV2`,
`mutations/Operand020TraceProvenance/PC` and
`mutations/Operand020TraceProvenance/SR`; aggregate
`operand020-trace-provenance-proof.json`, SHA-256
`1bc7701a3525f6d181f011ada153a000e37c1fe87374e282852a4a7adb357d61`.
The proof pins four executions, complete source/producer/verifier/binary
identities, the fixture-only repair, sole mutations, integrity rejections and
the preceding multiword-pipe proof. Existing source-specific retention is
reused; no fresh replay is claimed. Protected normal assemblies, production
CPU source, API/packages and regression retirement remain unchanged. The
prototype stays isolated, unimported and unpromoted. Wider indexed/chained
pipes and provenance, frame transport, other read origins, trace/interrupt,
changed return-SR/stack, memory destinations, other families and remaining
reference/consolidation gates stay required. Milestone 6 remains **in progress**,
`roadmapComplete=false`.


#### High and wrapped private operand frames — 2026-10-07

The unchanged isolated prototype passes **94,080 programs / 1,680 reports /
420 executions** for nominal private operand-frame entry and RTE transport.
Frame starts are `00004700`, `10004700`, `00FFFFA4`, `00FFFFD0`, `FFFFFFA4`,
`FFFFFFD0`, `FFFFFFFF`, `01000001`, `00100001` and `10004701`. They include
high/odd addresses, exact boundary ends and crossings of the 24-/32-bit address
boundaries. Seven source forms, MOVE byte/word/long, four stack states, all
operand fault lanes, CCR 00/1F, input/refault/alias handlers, both routes and
EC020/A1200/020/030 profiles remain represented. Earlier canonical groups
retain all-CCR evidence; this address group avoids repeating that Cartesian
product. [MC68020UM](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
distinguishes the MC68020's 32-bit and EC020's 24-bit external address spaces;
logical register addresses retain their full width.

A relocated VBR keeps vector entries clear of wrapping frames. Full-address
models poison the low 24-bit mirror wherever it is disjoint from the real
frame/guards. Defined/private fields independently verify original PC/SR,
consumed next PC, opcode, operand width, source address, guest stack and pending
kind. Entry writes match the descending logical word sequence mapped through
the selected model, including EC020 split bytes at its address-space boundary.
Map predicates reject requests outside that physical domain. Guards, untouched
mirrors, architectural registers/stacks and memory, exact exception/read counts,
real handler changes and the following sentinel pass.

A sole mutation truncates the logical stack on RTE: **47,040 intended failures /
47,040 unaffected passes**. Repeated faults additionally expose the resulting
wrong logical frame, with precise A7 failure reasons. A sole mutation truncates
frame-entry write addresses: **32,928 intended failures / 61,152 unaffected
passes**; the EC020 profiles already apply that external mapping and remain
unaffected. Both mutations preserve fixtures. Complete 222-file inventories,
actual TRX rosters, independently enumerated case keys/weights and five
corruption controls pass.

Evidence: `audits/Operand020FrameTransport`,
`mutations/Operand020FrameTransport/StackMask` and
`mutations/Operand020FrameTransport/EntryMask`; aggregate
`operand020-frame-transport-proof.json`, SHA-256
`72fa89495653d9f02af0babffb958bd27388e79db19cbdde62003b253ba93c74`.
The proof pins three executions, complete source/producer/verifier/binary
identities, sole mutations, integrity rejections and the preceding trace proof.
Existing source-specific retention is reused; no fresh replay is claimed.
This qualifies nominal private transport, not physical port widths/timing or
silicon-private frame contents. Faults during frame entry/state loading,
wider indexed/chained pipes/provenance, other origins, changed return-SR/stack,
memory destinations, other families and remaining reference/consolidation stay
required. Production source/packages and protected normal assemblies remain
unchanged; no regression is retired and the prototype stays isolated/unpromoted.
Milestone 6 remains **in progress**, `roadmapComplete=false`.


#### Private frame-entry and internal state-load HALT — 2026-10-07

The unchanged isolated prototype passes **138,240 programs / 160 reports /
40 executions** for private frame-entry and internal RTE state-load faults.
The ten preceding high/wrapped/odd frame starts remain selected. MOVE.L
`(A0),D0` provides a canonical source fault; four stack states, CCR 00/1F,
scalar/batch routes and EC020/A1200/020/030 profiles are covered. Entry faults
exercise both bytes of all 46 descending word writes: **58,880 programs**.
State-load faults exercise every byte of all 54 read occurrences, including
repeated reads: **78,080 programs**. The remaining **1,280 controls** complete
RTE, the pending operand read and the following MOVEQ sentinel.

[MC68020UM section 6.1.2](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
and [MC68030UM section 8.1.2](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
specify HALT for a bus error during bus/address/reset exception processing or
internal RTE state loading, with no further attempt to alter memory. This gate
checks selected bus-error entry and internal state-load paths; it does not
claim reset/address-error entry coverage. Earlier recoverable RTE validation
faults remain distinct from internal state-load faults.

The complete 92-byte private image is independently constructed from literals,
including the selected cold pipe state. Guards and disjoint poisoned low
mirrors verify memory outside completed writes. Entry failures preserve exactly
the completed descending prefix and the failed logical stack position, without
fetching the handler or building another image. Load failures preserve the
completed validation/load read prefix, uncommitted original frame and live
handler state, without an operand retry or another exception. Exact PC/SR,
registers/stacks, exception counts, access widths/order and physical map domain
pass, including EC020 boundary splits. Subsequent HALT calls perform no memory
access or architectural work. Scalar calls retain the existing two-native-cycle
Idle policy with a bus-synchronization barrier; batch calls retire zero
instructions without advancing those counters. This is approximate policy
preservation, not physical timing qualification.

The first fixture did not compile because two batch calls omitted required
arguments. It remains compile-only evidence with no TRX or executed coverage.
An API-only fixture repair executed the matrix but incorrectly required scalar
HALT calls to freeze timing counters: **69,760 passes / 68,480 precisely
identified invalid-fixture failures**. A second fixture-only repair checks the
existing Idle policy while preserving architectural and bus invariants. No CPU
defect is inferred from either fixture error.

A sole entry mutation continues after a failed frame write: **58,880 intended
failures / 79,360 unaffected passes**. A sole state-load mutation fails to set
HALT: **78,080 intended failures / 60,160 unaffected passes**. Every failure
identifier/reason is checked; both mutations retain the repaired fixture.
Complete 223-file source inventories, exact TRX rosters, independently
enumerated case keys/weights and five evidence-corruption controls pass.

Evidence: `audits/Operand020FrameFaults`, `audits/Operand020FrameFaultsV2`,
`audits/Operand020FrameFaultsV3`,
`mutations/Operand020FrameFaults/ContinueEntry` and
`mutations/Operand020FrameFaults/ContinueLoad`; aggregate
`operand020-frame-faults-proof.json`, SHA-256
`69b5baebce61d0284fc132a3840c2c18c05d1ca422d1fb760e1337fbc2d823b3`.
The proof pins four executed snapshots plus the compile-only attempt, complete
source/producer/verifier/binary identities, exact fixture-only repairs, sole
mutations, integrity rejections and the preceding nominal transport proof.
Existing source-specific retention is reused; no fresh full replay is claimed.
Production CPU source/packages and protected normal assemblies stay unchanged;
the prototype remains isolated, unimported and unpromoted. Wider indexed/chained
pipes/provenance, foreign frames, other origins, reset/address-error entry,
trace/interrupt, changed return-SR/stack, memory destinations, other families
and remaining reference/consolidation gates stay required. No regression is
retired. Milestone 6 remains **in progress**, `roadmapComplete=false`.


#### CMPM alias and A7 semantic consolidation — 2026-10-07

Two pure semantic methods in `M68020CmpmTests` are retired after proving their
shared replacements: three aliased-A0 size cases and one aliased-A7 byte case.
The existing 36 timing/width/flags/memory cases remain. The shared
`SyntheticExtendDecimalTests.ExtendComparisonAndAliases` matrix checks CMPM
source updates before destination reads, all register pairs, sizes, both stack
privilege modes and complete architectural/memory state. Its A7 byte fixtures
retain the two-byte stride specified by
[M68000PM section 2.2.4](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf).

| Retired original witness | Representative shared replacement |
| --- | --- |
| `AliasedAddressRegisterUsesTheNextOperandAfterSourceIncrement`, B/W/L | `A1200/CMPM/{1,2,4}/r0-r0/memory=True/super=True`, opcodes `B108/B148/B188`, source `1`, destination all-ones, CCR `14` |
| `ByteStackRegisterUsesTwoByteStrideForBothAliasedOperands` | `A1200/CMPM/1/r7-r7/memory=True/super=True/op=BF0F/s=00000001/d=000000FF/ccr=14` |

The representative replacement inputs differ from the originals' `0/1`, CCR
`1F`; both inputs distinguish the same ordering/stride defect. The shared group
also covers all other registers and user stacks. No equivalence is inferred
from passing tests alone: the original class is pinned to commit
`1e1ab44489b68982a1bbc981c45cddf01fec09c5` and reinstated unchanged in owned
copies for co-execution under each targeted mutation.

The clean command passes **203,520 shared programs / eight reports**, plus all
40 pinned original cases. A sole CMPM-function mutation samples the destination
base before source increment: **288 precisely identified synthetic failures**
and all four original alias/stack witnesses fail, while 203,232 shared programs
and all 36 timing cases pass. A second sole-function mutation gives byte A7 a
one-byte stride: **180 precisely identified synthetic failures** and the original
A7 witness fail, while 203,340 shared programs and all 36 timing cases pass.
The untouched 000/010 implementations remain controls. Fixtures are unchanged
across mutations; defined flags, register updates and failure reasons are
independently enumerated.

The maintained command audits complete source/helper identities, links owned
baseline sources to the current repository and pinned original class, verifies
the exact 48-test TRX roster, all 1,920 weighted keys per profile and every
failure identifier/reason. Eight corruption controls reject missing fixtures,
wrong producers, empty selections, wrong weights, changed pins/witnesses,
unrelated failure reasons and a copied CPU alteration even when its metadata is
re-signed. A fresh direct run against the reduced worktree passes **44 tests**:
all 36 retained timing cases and eight shared batches / 203,520 programs, with
zero failures or skips.

Reproduce with `scripts/test-copper68k-cmpm-consolidation.ps1 -PythonPath
<python3-executable> -OutputDirectory <fresh-output>`; Python 3 requires only
its standard library. `-ValidateReportsOnly` checks retained evidence without
executing the CPU. Ordinary CI coverage and integer inventory counts are
unchanged. Evidence: `audits/CmpmConsolidationFinal/proof.json`, SHA-256
`e6360daec2c2de102da162e448c347bf63ecbc84f8378b28cd902f686ae27f9a`,
and `audits/CmpmRetainedActual/verification.json`. Earlier probe/command
snapshots remain distinct. Production CPU source/packages and protected normal
assemblies are unchanged; no fresh full CPU/consumer replay is claimed.
Indexed/chained private pipe execution remains a separate pending gate.
All remaining reference/continuation/consolidation requirements stay open;
milestone 6 remains **in progress**, `roadmapComplete=false`.

#### Full-format indexed and chained private pipe transport — 2026-10-07

The isolated operand-read continuation prototype now passes **4,692,480
programs / 1,920 reports / 480 executions**: 1,098,240 source-fault programs
across all 66 full-format structural combinations, and 3,594,240
pointer-to-operand chains across the 54 pointer-bearing combinations. The 12
direct forms are explicitly excluded from pointer chains and remain covered
by source faults. An/PC bases, MOVE byte/word/long and MOVEA word/long, four
stack states, CCR `00/1F`, every selected pointer/operand byte lane and
scalar/batch execution are included. Canonical indexes use signed `D2.W=-8`;
the separate retained register and signed-boundary gates provide their own
broader index coverage.

Real handlers supply software pipe prefixes of zero through three words,
change all three backing words and optionally edit each retained word. Chained
faults must preserve the prefix through pointer recovery and the subsequent
operand fault. Independent literal checks verify each saved word, original
instruction provenance, completed access order/width and the following
MOVE.L/NOP result and exact next PC. Existing full-indexed timing policy is
checked separately. This qualifies interpreter-private software transport;
it does not establish physical three-word capture or cache/pipeline timing.

Two sole CPU-file mutations retain every fixture unchanged. Corrupting the
third word during capture detects exactly **1,437,696 failures**, leaving
3,254,784 programs passing. Corrupting it during restoration detects exactly
**1,876,992 failures**, leaving 2,815,488 passing. Every failure identifier and
reason is independently enumerated, including chained saved-word failures
and source-only following-register differences. No unrelated failures count
as mutation detection.

The strict verifier checks all 224 source/project inputs against the frozen
parent, exact mutation text, all 480 actual TRX results and their per-test
output, every weighted report key, logical totals and all failure reasons.
Five integrity controls precisely reject omitted or changed fixtures, a wrong
producer, an empty selection and a changed logical weight. Aggregate proof:
`operand020-indexed-pipe-proof.json`, SHA-256
`0cf31520cc9b5e21cb4093f42baa42a5a19e7bcb689b63f82dd805dc172aafe0`.
Evidence roots are `audits/Operand020IndexedPipe` and
`mutations/Operand020IndexedPipe/{CaptureThird,RestoreThird}` under the
retained reference-restoration evidence directory. The parent frame-fault
proof and all 223 preexisting inputs remain unchanged.

Production CPU source/packages and protected normal assemblies are unchanged;
the prototype remains isolated and unimported. No fresh full CPU/consumer
replay or regression retirement is claimed. Future import must preserve the
already-delivered CMPM retirements instead of replacing tests from an older
snapshot. Indexed/chained nested trace-vector provenance, foreign frames,
other origins and the remaining reference/continuation/consolidation gates
remain required. Milestone 6 remains **in progress**, `roadmapComplete=false`.

#### Copied private frame and selected-stack relocation — 2026-10-07

A bounded discovery exposes an original-stack-address restriction in the
isolated MOVE operand-read prototype. A real handler copies every byte of the
92-byte private frame using 23 MOVE.L instructions, selects the relocated
frame with LEA A7, and executes RTE. Across EC020/A1200/020/030, MOVE B/W/L,
four stack states, CCR `00/1F`, all operand byte lanes and scalar/batch,
**672 programs pass and 224 supervisor-stack programs are unsupported**.
All unmoved controls and relocated user-stack cases pass. Every unsupported
identifier/reason points to the original RTE stack-equality guard.

[MC68030UM section 8.1.13](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf)
describes RTE validating and consuming the frame on the selected active
supervisor stack. For this interpreter-private image, fault-time A7 provenance
does not impose an original-address restriction: the completed source address
is serialized independently. A separate isolated correction removes that
restriction while retaining the provenance read, all other frame reads,
validation, saved effective address and suffix execution. The unchanged
fixture then passes **896 programs / 16 reports / four executions**, with zero
mismatches or unsupported cases and exactly one completed operand read.
The original guard remains a retained discriminating regression witness.

Fresh affected retention passes **138,240 frame-entry/internal-load fault
programs / 160 reports / 40 executions**. Complete keys and weights match the
previous independently qualified frame-fault gate; actual per-test TRX output
and complete source identities agree. Defined HALT behavior, completed effects
and existing scalar/batch policy remain intact. Six integrity controls reject
omitted/changed fixtures, a wrong producer, an empty selection, changed weights
and unrelated failure reasons. Aggregate proof:
`operand020-moved-frame-proof.json`, SHA-256
`719240a5ab6fa273f1402062249ddeb613e40a7b7fc826f81964a111ca7be4b3`.
Evidence roots are `audits/Operand020MovedFrameDiscovery` and
`audits/Operand020MovedFrameFix`; the sole CPU-file change and unchanged fixture
are independently verified against the frozen indexed-pipe parent.

This qualifies selected private-image relocation within each interpreter
profile. It does not qualify foreign silicon images, cross-profile migration,
changed return SR/banks, all A7 aliases or general A/B continuations. The
indexed/chained nested-trace matrix continues separately on unchanged parent
CPU source. Broader qualification, fresh full CPU validation and affected
consumers remain required before importing the correction. Production CPU
source/packages, protected normal assemblies and CMPM retirements are
unchanged. Milestone 6 remains **in progress**, `roadmapComplete=false`.

#### Private recovery with A7 aliases and edited CCR — 2026-10-07

The separately corrected private-frame relocation prototype passes
**139,776 programs / 192 reports / 48 executions** for A0/A7 indirect,
postincrement and predecrement sources, MOVE byte/word/long to D0/D7 and
MOVEA word/long to A0/A7. Four stack states, unmoved/copied frames, real
keep/CCR-edit handlers, every operand byte lane and scalar/batch are included.
CCR `00/1F` covers all forms; the canonical A7 postincrement selection covers
all 32 CCR values separately. The handlers copy the complete private image,
select A7 with LEA and optionally invert all five saved CCR bits. Privilege,
bank and trace bits remain unchanged in this gate.

Independently chosen operand locations remain fixed through relocation and
source/destination aliasing. The test checks completed predecrement effects,
the pending postincrement suffix, destination A7 overrides, word MOVEA sign
extension, partial data registers, untouched state/memory, returned CCR
preservation and the following MOVEQ sentinel. A7 byte strides and MOVE/MOVEA
flag expectations follow
[M68000PM sections 2.2.4/2.2.5 and the instruction definitions](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf).
The exact literal RTE validation/load prefix already qualified by the frame
fault gate precedes exactly one pending operand read.

An initial fixture records **139,520 passing / 256 mismatching cases**. The
relocated MSP frame overlaps the user-M predecrement word operand: legitimate
frame validation/load word reads share its address and width. Counting all
address matches as operand reads incorrectly labels those cases as replay.
A fresh fixture-only repair compares the entire independently qualified
59-read frame prefix followed by one operand read. Every original failure
identifier/reason and the exact fixture repair remain pinned; no CPU change
or acceptance-scope reduction resolves the invalid counting assumption.

Sole CPU-file mutations retain the repaired fixture unchanged. Giving byte A7
a one-byte postincrement detects exactly **8,192 failures**, retaining 131,584
passing programs. Clearing the returned X flag detects exactly **69,888
failures**, retaining 69,888 passing programs. Independent expectations check
each failing A7 or SR value, including overlapping-frame zero operands and
MOVEA's unchanged CCR. All 226 source/project inputs, actual TRX results/output,
weighted keys and precise reasons are audited. Six corruption controls reject
missing/changed fixtures, wrong producers, empty selections, changed weights
and unrelated failure reasons. Aggregate proof:
`operand020-stack-alias-proof.json`, SHA-256
`1b1888711fe5f23ab9c8cd47b38da00093f5ada1dade7fad4de7a7b2e1686716`.

Evidence: `audits/Operand020StackAliasV2` and
`mutations/Operand020StackAlias/{Stride,Extend}`. The earlier invalid fixture
remains `audits/Operand020StackAlias`. CPU and all preexisting sources are
unchanged from the separately qualified relocation correction; its fresh
frame-fault retention remains applicable without claiming a new full replay.
The indexed/chained nested-trace run remains separate on unchanged older CPU
source. Returned privilege/bank/trace changes, foreign/cross-profile frames,
other origins and remaining roadmap gates still require qualification before
production import and fresh full CPU/consumer validation. Production CPU
source/packages, protected normal assemblies and CMPM retirements remain
unchanged. Milestone 6 remains **in progress**, `roadmapComplete=false`.

#### Full-format indexed and chained nested trace provenance — 2026-10-07

The isolated unchanged-CPU parent passes **6,137,856 programs / 3,456 reports /
864 executions** with zero mismatches, unsupported cases or skips in the
selected nested-trace matrix. Direct completion contributes 506,880 programs,
source-fault recovery 1,317,888 and pointer-to-operand chains 4,313,088. All 66
full-format structural forms have direct/source coverage; all 54
pointer-bearing forms have chained coverage, with 12 direct chain exclusions.
An/PC bases, MOVE byte/word/long and MOVEA word/long, four stack states, CCR
`00/1F`, all selected pointer/operand/vector byte lanes and scalar/batch are
included. Canonical indexes are signed `D2.W=-8`; retained register/boundary
and simple-address trace gates provide their separately qualified breadth.

Real source handlers overwrite the original opcode before return. The resumed
instruction completes its saved operand and register/flag suffix once, then
enters trace processing. Vector handlers exercise a software input buffer,
persistent refault and changed VBR. The test independently verifies the
original instruction PC/opcode and exact next PC, post-instruction SR, format-2
trace frame, nested private bus-error context, stack selection, completed
pointer/operand reads, the saved original vector address and following
instruction. The architectural trace expectations follow
[MC68030UM section 8.1.7](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf).
Private serialized provenance is distinct from physical pipeline overlap.

Two sole CPU-file mutations retain every fixture unchanged. Losing original
PC provenance during resumed completion produces exactly **5,630,976
failures**, retaining 506,880 direct controls. Corrupting the saved vector-read
width produces exactly **6,137,856 failures**. Every identifier and precise
frame-field reason is independently enumerated. Complete source inventories,
all 864 actual TRX results and per-test output, weighted report keys and logical
totals agree. Six corruption controls precisely reject missing/changed fixtures,
wrong producers, empty selections, changed weights and unrelated failure
reasons. Aggregate proof: `operand020-indexed-trace-proof.json`, SHA-256
`3d67385d5fb57b1f0a702bf1ec9ed887262668ba3b1a70b86c3d16ba0fbc06ca`.

Evidence: `audits/Operand020IndexedTraceV2` and
`mutations/Operand020IndexedTrace/{PC,Width}`. The original CS0136 local-name
collision is compile-only evidence; its repair changes only the fixture local
name. An initial planned aggregate omitted MOVEA widths when adding the five
sizes. Per-case catalogs/weights already included them; the aggregate was
corrected to 6,137,856 before any complete gate was accepted, without changing
fixtures, selections or acceptance scope. All 224 preexisting parent inputs
remain unchanged; the new fixture is the 225th source/project input.

This proof applies to the unchanged parent CPU, separately from the relocated
frame correction and its stack-alias gate. They must be combined in a fresh
source snapshot and checked against their complete qualified selections.
Foreign/cross-profile frames, returned privilege/bank/trace changes, other
origins and remaining reference/continuation/consolidation gates remain open.
Production CPU source/packages, protected normal assemblies and CMPM retirements
are unchanged. No fresh full CPU/consumer replay or physical timing
qualification is claimed. Milestone 6 remains **in progress**,
`roadmapComplete=false`.
