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

- SingleStepTests has the pinned 312,500-case 68000 instruction-body audit.
  The later checkpoints below add a pinned multi-model WinUAE bridge, the
  unchanged failing Basic discovery audit, and separately qualified trace,
  trap/bounds and breakpoint presets. Remaining Basic disagreements and
  unsupported integer execution still require per-case qualification; passing
  focused presets do not replace the failing broad audit.
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
