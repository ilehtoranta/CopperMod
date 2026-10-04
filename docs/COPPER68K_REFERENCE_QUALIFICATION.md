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

- SingleStepTests now has the pinned 312,500-case 68000 instruction-body audit
  described above. WinUAE's executable adapter has no new fixture run in this
  slice; the LINK source review is separate evidence. WinUAE integer integration
  currently selects 68000; multi-model native bridge/fixtures still need qualification.
- Generated 010 word-MOVE/MOVEA format-8 images now have the scoped continuation
  gate below. Long transfers, other instruction families, foreign silicon images,
  external bus faults, 020/030 formats 9/A/B and 040 format 7 remain unqualified. The current advanced decoder does not implement all these
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
