# Copper68k synthetic instruction suite

## Scope and status

Accepted implementation plan, 2026-10-03. Source baseline: PR #19, commit
`e7bce76667d2768e042ff4967c695220b4b78f16`. Implementation belongs in CopperMod,
with CopperScreen consuming isolated NuGet packages for validation.

Build a reusable, self-contained instruction suite in `Copper68k.Tests` for
68000, 68010, 68EC020, 68020, 68030, 68040 and 68060, plus the A1200 EC020
profile. Diagnostic 010/060 qualification is separate from desktop readiness.
The suite progressively replaces duplicated instruction tests while retaining
specialized hardware and integration regressions.

**Milestone 1 requires completing legal MOVE/MOVEA execution on every selected
model. A gap report alone does not complete it.**

| Milestone | Status | Deliverable and completion gate |
| --- | --- | --- |
| 1. Framework and MOVE | Complete: semantic gate, 2026-10-04 | 631,184 deterministic cases across all eight profiles; zero mismatches, unsupported legal forms or untested required combinations. |
| 2. Data transfer and address operations | Complete: semantic gate, 2026-10-04 | 270,240 deterministic cases for MOVEQ, MOVEM, MOVEP, LEA, PEA, EXG, EXT/EXTB and SWAP; unavailable forms raise their documented exceptions. |
| 3. Arithmetic and comparison | Complete | ADD/SUB variants, quick/immediate/address/extend forms, comparisons, multiply/divide, decimal/packing operations; overflow, borrow, carry, sticky zero, exceptional operands. |
| 4. Logical, bit and shift operations | Complete: semantic gate, 2026-10-04 | Logical/immediate/unary operations, bit manipulation, shifts/rotates, bitfields, atomic integer operations; preservation and memory effects. |
| 5. Control and system operations | Complete: scoped semantic gate, 2026-10-04 | Branches, conditions, calls/returns, stack frames, traps, privilege-sensitive transfers, STOP/RESET, model-specific integer/system instructions; exception frames, saved PC/SR, stack selection, interrupt/trace. |
| 6. Reference qualification and consolidation | In progress | Independent reference audits across selected models, gap review, retire proven redundant tests; publish architectural combination coverage, not just xUnit counts. |

For every promoted family, mismatches and emulator-level unsupported execution
fail its gate. Documented processor exceptions (for example unimplemented integer
instructions on 060 hardware) are expected outcomes rather than implementation
gaps. Later families remain explicitly untested in a complete integer-family
inventory created in milestone 1.

## Shared test architecture

Extend the existing xUnit project and public CPU factory. All new framework types
remain test-internal; no public package API changes are planned.

- Model specifications define availability, external address width, indexing,
  alignment, stack behavior and architectural exceptions.
- Instruction specifications declare legal sizes and operand combinations,
  expected results, affected/preserved flags and model differences, derived from
  the [Motorola programmer reference](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
  and applicable processor manuals.
- Addressing fixtures prepare registers, operands, instruction extensions and
  pointer chains, with intended locations chosen independently of production EA
  calculations. Reuse fixtures across instructions.
- Scenario generation combines legal forms with boundaries, all CCR states,
  register selections, aliases, stack operands and model-specific cases. Ordering
  is deterministic; additional generated samples record their seeds.
- Common verification compares architectural registers, exact PC, defined SR/CCR
  bits, execution state, memory changes and expected exceptions, including
  untouched registers and surrounding memory.
- A sparse recording bus supports full 32-bit addresses without unconditional
  24-bit truncation, records access widths, and separates initialization from CPU
  accesses.
- Expectations never call production decoder, arithmetic, EA or timing helpers.
  Fixed reference examples validate fixture encodings.
- Coverage identifies model/profile, instruction, size, operand forms and
  scenario. Passing, mismatching, unsupported and untested are distinct from
  architecturally invalid, reserved/undefined or excluded forms.

## MOVE coverage and acceptance

Run opcode enumeration, value/CCR boundaries and extension/alias scenarios as
separate deterministic groups rather than one enormous Cartesian product.

- Every legal MOVE/MOVEA opcode word, including register encodings, gets a
  canonical valid fixture.
- Byte/word/long; register, indirect, postincrement, predecrement, displacement,
  absolute, immediate and legal PC-relative sources.
- Brief indexed addressing and every documented full-format structural
  combination on applicable models: suppression, displacement lengths and
  pre/post memory indirection.
- Source/destination extension ordering, extension-word PC bases, negative
  absolute-word addresses, signed/scaled indexes and external address widths.
- Boundaries, all 32 initial CCR combinations for canonical operand forms,
  partial-register preservation, MOVEA word sign extension and unchanged flags.
- Aliased source/destination bases and indexes, overlapping operands, A7 byte
  strides, user/supervisor stacks and model-specific alignment outcomes.
- Invalid operand combinations against documented behavior; reserved or
  undefined encodings labeled explicitly.
- Exact next PC and following-instruction sentinels exposing overconsumed words.

Acceptance: **zero MOVE/MOVEA mismatches, unsupported legal forms or untested
required combinations** for all selected models/profiles. Fixes preserve correct
execution ordering and existing timing policy. Never retry after partial operand
side effects.

## Validation, migration and constraints

Deterministic coverage runs in ordinary Copper68k CI. Batch generated cases with
precise failure identifiers; logical case counts are reported separately from
xUnit batch counts. An explicit deep-audit command adds seeded cases and external
references. Requested audits fail on missing fixtures, empty selections or
mismatches.

Reuse existing SingleStepTests, Musashi and WinUAE adapters. Record pinned
source/input identities and caveats. [WinUAE's tester](https://raw.githubusercontent.com/tonioni/WinUAE/master/cputest/readme.txt)
provides broad model and addressing coverage; adapter capability and actual
available inputs must be reported separately.

Prove replacement coverage against isolated historical defects or targeted
mutations: absolute-address decoding, extension length, index sign extension,
alias ordering, A7 stride and flags. Retire an old regression only after mapping
its replacement and demonstrating detection of the original defect. Retain
cache, prefetch, bus ordering, detailed fault sequencing, JIT and native ROM/media
regressions unless separately proven redundant.

Production fixes require the full CPU suite and affected consumers. Validate
CopperScreen through isolated NuGet packages, preserving its dependency boundary
and unrelated changes. Published packages are immutable; publication remains a
separate explicitly authorized release step.

Semantic correctness, timing policy preservation and physical timing
qualification are separate. FPU arithmetic, enabled MMU operation, physical
pipeline/cache qualification and OS compatibility are outside this roadmap.

## Implementation evidence

The [milestone 6 completion audit](COPPER68K_MILESTONE6_COMPLETION_AUDIT.md)
maps the accepted requirements to evidence and remaining gates. Dated records
below remain historical; focused proofs do not establish whole-roadmap completion.

Fresh frozen `c284b94` seeded/reference validation (2026-10-08) passes 320,000
seeded cases, 312,500 SingleStepTests cases and 536 Musashi programs across the
selected profiles, with 88 explicit program exclusions. Six actual invalid/
missing/empty adapter requests fail. Sources, input sets, loaded assemblies,
commands, exact roster and every report outcome are independently linked.
The current full CPU run is still active; its result is not yet claimed.
Milestone 6 remains in progress, `roadmapComplete=false`.

Record commands, logical case counts, failure discoveries, fixes, reference
identities, replacement proofs and remaining required coverage here as work
progresses. No milestone is complete merely because an inventory exists or a
partial matrix passes.

Earlier milestone-6 checkpoint, 2026-10-05: the exhaustive cache-encoding matrix
passes 131,072 cases across eight profiles, including illegal scope-zero and
neither-cache forms. Independent scope-zero reference qualification passes
2,048 callbacks/frames with exact opcode/status distributions and 32 comparator
controls. A separate copied reference follows the manual's 040/060 vector-4
rule; the original Basic line-F disagreement remains retained. Five mutations
are detected; shared X-flag and extension-length evidence permits retirement
of the duplicated 9,216-case-per-profile cache loop. MOVE16, breakpoint and
specialized cache/prefetch/bus/JIT/native regressions remain. The
[cache encoding record](COPPER68K_REFERENCE_QUALIFICATION.md#cache-encoding-reference-qualification-and-consolidation-2026-10-05)
maintains exact identities, replacement mapping and qualification limits.
Milestone 6 remains in progress; production CPU source is unchanged.
Full Release CPU validation passes 5,044 tests with eleven optional skips,
all nine qualified WinUAE presets and matching CPU/adapter identities. The strict
gate passes 16,093,014 logical cases in 603 batches; pinned SingleStepTests passes
312,500 cases in 125 files, and Musashi passes 536 programs with 88 exclusions.
Seven specific input/source controls and three copied-report controls reject
their intended defects. `roadmapComplete=false` and the full required-gap
inventory remain explicit.

Preceding milestone-6 reference checkpoint, 2026-10-05: sampled CAS2 W/L passes
3,180 callbacks, 2,296 unimplemented-integer frames and 3,072 recorded forms
across EC020/A1200/020/030/040/060. The CPU already implements the manual's
shared-compare operand-1 rule; a copied reference corrects 040 alias ordering.
A copied input generator excludes overlapping memory-update candidates before
execution, with independent physical-width/wrap checks in the bridge; 060
unimplemented cases retain overlapping inputs. Twelve directories and eleven
fixed cases pass, with thirty comparator controls and nine input/source controls.
Both source/patch identities, complete selections and exact counts are required.
The [CAS2 qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#cas2-compare-alias-and-unimplemented-frame-reference-qualification-2026-10-05)
retains failed discovery and reports the limited seeded scope honestly. No
production fix, regression retirement or package publication is added. The full
milestone-6 restoration/reference/consolidation requirements remain in progress.
Full Release CPU validation passes 5,023 tests with eleven optional skips and
all eight qualified WinUAE presets enabled against the same CPU/adapter binaries.
The strict gate passes 16,035,670 logical cases in 595 batches; pinned
SingleStepTests passes 312,500 cases in 125 files, and Musashi passes 536 programs
with 88 explicit exclusions. `roadmapComplete=false` remains explicit.

Preceding milestone-6 reference checkpoint, 2026-10-05: legal sampled CAS B/W/L
passes 49,284 independent callbacks, 2,466 unimplemented-integer frames and
38,448 recorded architectural combinations across EC020/A1200/020/030/040/060.
The CPU already saves the documented 060 instruction PC; a separate copied
reference generator corrects its advanced PC and selects canonical extensions.
All eighteen directories and fifteen fixed encoding/profile tests pass, with
forty applicable comparator controls (including two saved-PC controls) and nine
input/source rejection controls. Both copied source/patch identities are required.
See the [CAS qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#cas-legal-input-and-unimplemented-frame-reference-qualification-2026-10-05).
No CPU behavior changes, regression retirement or package publication are added.
The original Basic results and all broader restoration/reference/consolidation
requirements remain retained; milestone 6 is still **in progress**.
The full Release CPU suite passes 5,011 tests, with eleven optional skips and
all seven qualified WinUAE presets enabled against the same CPU/adapter binaries.
The strict gate passes 16,035,670 logical cases in 595 reporting batches, with
fresh pinned SingleStepTests (312,500 cases / 125 files) and Musashi (536 programs /
88 exclusions). `roadmapComplete=false` and all required gaps remain explicit.

Preceding milestone-6 reference checkpoint, 2026-10-05: legal sampled MOVES B/W/L
passes 110,492 independent callbacks, 86,072 privilege frames and 59,920
recorded architectural combinations across seven profiles. The retained Basic
010 MOVES.L failure uses an undefined same-An store and noncanonical extension;
no CPU change is required. A copied generator selects legal inputs before
execution; exact identities/selections/counts and corruption controls are required.
The full CPU suite passes 4,995 tests, with eleven optional skips and all six
qualified WinUAE presets enabled. See the
[MOVES qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#moves-legal-input-reference-qualification-2026-10-05).
The strict gate remains passing at 16,035,670 logical cases in 595 reporting
batches, with fresh pinned SingleStepTests and Musashi audits and the complete
required-gap inventory retained.

Preceding milestone-6 checkpoint, 2026-10-05: independent LPSTOP exception
qualification passes 245,760 pinned WinUAE callbacks/frames in fourteen
architectural combinations. The new synthetic matrix includes all former SR
values and passes 267,262 cases; shared mutation proof permits removal of the
duplicate 320-case-per-profile LPSTOP loop. MOVE16/cache/BKPT and specialized
regressions remain. The full CPU suite passes 4,978 tests (eleven optional skips),
and the strict gate passes 16,035,670 logical cases in 595 reporting batches.
See the [qualification and retirement record](COPPER68K_REFERENCE_QUALIFICATION.md#lpstop-independent-exception-qualification-and-consolidation-2026-10-05).
Milestone 6 remains in progress: this checkpoint does not close the advanced
restoration, reference disagreement or broader consolidation requirements.

### First implementation checkpoint — 2026-10-04

The [suite guide](../Copper68k.Tests/Synthetic/README.md) describes fixtures,
coverage reports, the CI gate, seeded audits and mutation commands. The complete
integer-family inventory includes later families as untested. All implementation
helpers are internal to the test assembly; the package public API is unchanged.

| Deterministic group | 68000 / 68010, each | Other six profiles, each |
| --- | ---: | ---: |
| Every legal MOVE/MOVEA opcode word | 9,726 | 9,726 |
| MOVE value boundaries and all 32 CCR states | 58,368 | 58,368 |
| MOVE extension/alias cases | 1,756 | 7,120 |
| MOVE address-register signs, overlap and full stack/index aliases | 1,801 | 2,593 |
| MOVE external address boundaries | 44 | 60 |
| Invalid MOVE operand opwords | 2,562 | 2,562 |
| MOVE alignment outcomes | 12 | 12 |
| MOVEQ/EXT/EXTB/SWAP/EXG | 17,624 | 17,624 |
| LEA/PEA | 8,064 | 9,252 |
| MOVEP | 3,708 | 3,708 |
| MOVEM | 3,196 | 3,592 |

Total: **901,424 logical cases in 90 xUnit batches**, independently checked by
`scripts/test-copper68k-synthetic.ps1 -ValidateReportsOnly`. All pass. A separate
recorded seed 68020 adds **10,000 samples per profile / 80,000 total**, all passing.
Full-format fixtures cover all 66 legal structural combinations; brief index
cases include signed/scaled data/address indexes. Early processors ignore their
reserved scale/format bits; reserved full encodings are explicitly excluded from
architectural expectations. Alignment checks establish the vector outcome;
detailed restart/fault-frame qualification remains specialized and milestone 5
work.

Discovered implementation defects and completed fixes:

- An overly broad byte indirect-to-absolute-long decoder admitted absolute-word
  opcodes and consumed the following instruction. Narrowed the decode mask.
- Remaining legal advanced MOVE/MOVEA and MOVEM EA combinations lacked dispatch.
  Added general routes only after an existing route declines before execution.
- Existing MOVE routes needed full indexed resolution while preserving the order
  of source effects and destination extensions. Extended those routes and retained
  their existing brief timing plans.
- A7 updates through MOVE, EXG and MOVEM could leave the active stack bank stale.
  Writes now use the existing stack-aware register helper.
- 020+ predecrement MOVEM stores the initial base minus one operand size when that
  base is in the list; the prior implementation and one old test expected the
  000/010 initial-base value. Corrected the snapshot and assertion.
- The 040 integer fallback truncated addresses and rejected odd data accesses,
  used a brief decoder for full LEA/MOVEM and lacked its format-0 exception header.
  Corrected the fallback's already-selected 020 addressing mode.
- Transfers spanning the 24-bit external boundary leaked into higher addresses.
  Added wrapped slow transfers and rejected crossing fast spans before host access.
- Restoring a 040 fallback register checkpoint could retry after source bus effects.
  Rejection now propagates, with a dedicated one-read/one-increment regression.

New shapes use bounded approximate timing plans. Existing admitted brief shapes,
fixed-cycle profiles and bus ownership remain unchanged. This is semantic
qualification, not physical timing certification.

Validation at this checkpoint:

- Full Copper68k Release suite: **4,344 passed, 7 skipped, 0 failed**. Six optional
  external-reference tests and the opt-in seeded test are skipped in this ordinary
  run; the seeded test was separately enabled and passed. External references were
  not executed and remain unavailable coverage.
- Retained AHX consumer: **18 passed**.
- Private validated package `1.5.2-synthetic-dev.30`, SHA-256
  `46efb356b81ca0d1566b8e544dce3aac5b1dab01096f2cd39f6a2ecc219ef51f`.
  Not published. CopperScreen consumes this package through its boundary-version
  override in a clean detached checkout of `d9beae8`; no sibling source reference
  or root working-tree changes were introduced.
- CopperScreen production Release build: **0 warnings/errors**; host **149 passed,
  6 optional media entries skipped**; disk **74 passed**; isolated engine diagnostics
  **1,080 passed**, no skipped tests.
- Native 68000 Workbench 3.1 floppy replay: both 0 and 2 MiB Fast RAM cases pass,
  retaining pinned final cycles, PC and framebuffer hash. Native A1200 EC020
  eight-plane boot and cold reopen: passes with pinned RGB24/DOS proof checks.
  An initial AGA selection was rejected by its input hash; the correct final
  pristine fixture was selected and the independent AGA rerun passed.
- All six targeted mutations were detected by executable synthetic cases; source
  was restored and rebuilt. Proofs identify `11D0` absolute decode, `10B8` extension
  length, `1230` signed index, `1098` alias order, `101F` A7 stride and `1200` flags.
  No old regression has been retired. Two former full-MOVE unsupported assertions
  now assert legal execution; the MOVEM snapshot assertion was corrected.
- The audit command rejects a requested missing corpus and a valid selected
  SingleStep file containing zero cases. WinUAE audit rows with zero executions
  are untested and fail qualification. Reports distinguish
  unrequested references from passing coverage and record input/source identities
  when references are explicitly supplied.

Local evidence is retained under `artifacts/full-m1-m2-final/`,
`artifacts/synthetic-mutations-m1/`, `artifacts/synthetic-private-feed-30/` and the
isolated CopperScreen `artifacts/synthetic-consumer-m1-m2/` checkout. Generated
artifacts, package files, licensed ROMs/media and unrelated working changes are
not committed. Milestones 3–6 remain open; this checkpoint does not claim the
entire roadmap is complete.

### Arithmetic and comparison checkpoint — 2026-10-04

Milestone 3 adds **2,043,744 deterministic logical cases** in 56 model batches.
The cumulative milestones 1–3 gate has **2,945,168 cases in 146 xUnit batches**.
All seven models and the A1200 EC020 profile use the public CPU factory. The
coverage command checks exact counts and promotes only milestones 1–3;
milestones 4–6 remain explicitly untested.

| Arithmetic group | 68000 / 68010, each | Other profiles, each |
| --- | ---: | ---: |
| ADD/SUB/CMP and quick/immediate/address boundaries, all CCR states | 56,448 | 56,448 |
| Legal addressing forms, register fields and full indexed structures | 4,667 | 8,237 |
| All quick counts, EA CCR states, user stacks, signed/scaled indexes, address boundaries and alignment | 21,072 | 21,072 |
| ADDX/SUBX/CMPM, sticky zero and memory/register aliases | 25,440 | 25,440 |
| ABCD/SBCD/NBCD and PACK/UNPK | 111,392 | 111,458 |
| Word/long32/long64 signed and unsigned multiply/divide boundaries | 26,904 | 26,904 |
| Multiply/divide addressing, register aliases, zero-divide frames and returns | 5,608 | 7,224; 68060: 7,208 |

`ArithmeticSpecification` computes unsigned carry/borrow and signed overflow
from mathematical ranges. Multiply/divide use arbitrary-precision expectations,
including minimum signed dividends divided by -1, quotient overflow and signed
remainders. Shared single-operand fixtures reuse the independent MOVE addressing
fixtures, including extension-word PC bases and all 66 full-format structures.
They guard surrounding memory and check exact next PC with a following NOP.

Decimal tests exhaust every valid 00..99 source/destination pair with X/Z states.
They mask architecturally undefined N/V, verify sticky zero, preserve partial
registers and exercise every aliased register pair in memory. PACK/UNPK cover
adjustment wrapping, early illegal-instruction outcomes, separate byte accesses
and A7's two-byte stride. Non-BCD arithmetic results and identical high/low
registers for 64-bit MUL remain explicitly undefined/excluded. Divide overflow
masks undefined N/Z; divide-by-zero masks undefined N/Z/V but requires preserved X
and cleared C. Undefined stacked SR bits are masked individually too.

000/010 long instructions raise vector 4; 060 64-bit forms raise vector 61.
Both preserve operand registers and avoid operand reads. Zero divide verifies
the saved next PC, original instruction address in the 020+ format-2 frame,
user/supervisor stack selection and a real RTE back to the following instruction.
Alignment scenarios retain specialized detailed fault sequencing tests.

Completed production corrections:

- Add missing advanced arithmetic and predecrement-memory ADDX/SUBX routes only
  after the existing routes decline before execution. Preserve existing 040
  fallback plans; admit legal 020+ PC-relative CMPI before that fallback can
  incorrectly raise a 000 illegal-instruction exception.
- Resolve full indexed arithmetic, comparisons, multiplication/division and NBCD
  in their already-selected routes. New indexed shapes add bounded approximate
  policy costs; existing brief and fixed-cycle policies remain unchanged.
- Use the architectural address-register writer for A7 arithmetic, comparisons,
  source increments/decrements and CMPM aliases so the active stack bank agrees.
- Read the ABCD/SBCD source before decrementing an aliased destination base.
- Implement PACK/UNPK for 020+ with byte-by-byte memory operations and stack
  strides, wrapping adjustment words and unchanged flags.
- Truncate new memory extend results before computing sticky-zero flags.
- Compute signed word division with a wider dividend so INT_MIN/-1 produces
  architectural overflow instead of a host exception; clear defined divide C
  on overflow/zero-divide paths, including 000/010.
- Emit the format-2 zero-divide frame on 020/030/040/060 and consume it in RTE.
  The long zero-divide path completes its exception timing plan once.

Validation commands and evidence:

- Full Release CPU suite: `dotnet test Copper68k.Tests/Copper68k.Tests.csproj
  -c Release`; **4,400 passed, 7 skipped, 0 failed**. Optional external references
  remain unavailable. The new seeded audit was separately enabled: seed 68020,
  10,000 MOVE plus 10,000 arithmetic samples per profile, **160,000 total passing**.
- `scripts/test-copper68k-synthetic.ps1 -ValidateReportsOnly -OutputDirectory
  artifacts/m3-final` confirms every required count and zero mismatching,
  unsupported or untested cases in the promoted gates.
- The added separate-byte and forbidden-operand-read assertions pass in all
  16 affected model batches. UNPK memory scenarios use distinct high/low bytes
  to expose reversed write order, including every aliased register pair.
- `scripts/test-copper68k-synthetic-mutations.ps1 -Scope Arithmetic` detects all
  five new mutations: ADDQ.B overflow at 7F+1, memory ADDX.B sticky zero at FF+X,
  ABCD with an aliased predecrement base, PACK using A7 and the 060 divide frame.
  Source is restored and rebuilt. The six earlier MOVE proofs remain retained;
  no old regression has been retired.
- Retained AHX consumer: **18 passed**. Private package
  `1.5.2-synthetic-dev.31`, SHA-256
  `68e722e761cbf09ddc2394df139386ef9dbf81562240b808c44549c0b1a24d88`,
  passes package/content validation and is **not published**.
- CopperScreen consumes the private package in the detached `d9beae8` checkout
  through its existing boundary-version override. Production Release build:
  **0 warnings/errors**; host **149 passed, 6 optional media cases skipped**;
  disk **74 passed**; isolated engine diagnostics **1,080 passed**, none skipped.
  Root working changes and the pinned published dependency remain untouched.
- Native 68000 Workbench 3.1 floppy replays at 0 and 2 MiB Fast RAM both pass
  their pinned cycle, PC and framebuffer checks. Native A1200 EC020 eight-plane
  boot and cold reopen passes its pinned RGB24/DOS proof checks. Input identities
  remain the same as the first checkpoint; no ROM/media files are committed.

Evidence directories: `artifacts/m3-final/`, `artifacts/m3-seeded/`,
`artifacts/m3-bus-checks/`, `artifacts/m3-mutations-proof/`,
`artifacts/m3-pack-byte-order-all/`, `artifacts/synthetic-private-feed-31/` and the isolated CopperScreen checkout's
`artifacts/m3-validation/`. Semantic correctness, approximate timing policy and
physical timing qualification remain separate; no new physical timing or OS
compatibility qualification is claimed.

## Milestones 4 and 5 implementation checkpoint — 2026-10-04

The logical/bit/shift/atomic matrix adds **4,073,024 deterministic cases** in
64 batches. Control/system adds **1,305,838 cases** in 128 batches. Together
with milestones 1–3, the required semantic gate is **8,324,030 cases** in
**338 xUnit batches** across all seven models and the A1200 EC020 profile.
Every required named batch must exist with its exact count and zero mismatching,
unsupported or untested cases before promotion. Architectural unavailable
instructions execute their documented illegal, line-F or unimplemented-integer
exceptions, rather than being counted as emulator gaps.

| Milestone 4 group | 000/010 per profile | 020+ per profile |
| --- | ---: | ---: |
| Logical/unary value and CCR boundaries | 31,808 | 31,808 |
| Logical/unary addressing | 6,660 | 9,944 |
| Register/memory shifts and rotates | 130,560 | 131,088 |
| Static/dynamic bit operations | 104,192 | 104,852 |
| Bitfield widths, offsets, values and aliases | 155,136 | 155,136 |
| Bitfield addressing and full extensions | 2,688 | 3,480 |
| CAS sizes, comparisons, addressing and aliases | 36,975 | 37,371 |
| CAS2 comparisons, register fields and aliases | 36,864 | 36,864 |

Boolean and bit-by-bit shift expectations are independent of production helpers.
Bitfields independently extract/insert every selected bit, including signed
memory offsets, INT_MIN, wrapping register fields, width 32, dynamic offset/width
aliases and original-offset BFFFO results. CAS/CAS2 check comparison precedence,
partial registers, X preservation and selected read/write order and widths.
040/060 failed CAS writeback and the 060 misalignment/removed-CAS2 outcomes are
explicit. Overlapping CAS2 memory results are architecturally undefined/excluded.

| Milestone 5 group | 000/010 per profile | 020+ per profile |
| --- | ---: | ---: |
| Branch conditions, displacements and subroutine stacks | 8,192 | 12,288 |
| DBcc/Scc registers, addressing and CCR | 42,048 | 43,104 |
| Conditional traps | 3,072 | 3,072 |
| JMP/JSR addressing and stack aliases | 3,592 | 3,856 |
| Traps, privilege, STOP/RESET and basic instructions | 1,728 | 1,728 |
| SR/CCR/USP transfers and immediate status operations | 16,540 | 16,936 |
| LINK/UNLK and ordinary returns | 6,080 | 6,080 |
| CHK/CHK2/CMP2 bounds, registers and addressing | 11,828 | 12,488 |
| MOVES register/memory transfers and privilege | 10,656 | 11,052 |
| BKPT, cache instructions, five MOVE16 forms, LPSTOP | 13,120 | 13,120 |
| MOVEC selectors, masks, general registers and privilege | 2,432 | 2,432 |
| RTE frames and stack selection | 2,048 | 020/030/EC020/A1200: 1,888; 040: 2,016; 060: 2,048 |
| Trace retirement and batch boundaries | 580 | 020/030/040/EC020/A1200: 1,160; 060: 580 |
| Interrupt masks, STOP wakeup, VBR and master frames | 000: 224; 010: 226 | 020/030/040/EC020/A1200: 418; 060: 226 |
| CALLM descriptors, frames, arguments and access requests | 10,832 | 020/EC020/A1200: 11,000; 030/040/060: 10,964 |
| RTM frames, arguments, denied access and invalid options | 24,594 | 24,594 |

System scenarios verify exact stacked PC/SR, privilege before operand effects,
model-specific format-2 frames, inactive stack preservation, master interrupts
with paired MSP/ISP frames and real RTE restoration. T1 traces completed
instructions, T0 traces documented control flow on applicable models; completed
traps and aborting exceptions are distinguished. Batch-boundary trace changes
cannot bypass retirement through cached hot blocks. MOVE16 covers all five
register/absolute forms, line alignment, aliases and one postincrement for a
shared source/destination register. MOVEC exercises each model's selectors and
reserved/read-as-zero masks while keeping MMU translation disabled.

CALLM/RTM are implemented for 020/EC020/A1200, including inline/indirect argument
options, all general-register selections, descriptor validation and type-1
access-level requests. CPU-space access control uses an optional **internal**
responder separate from ordinary RAM. Public flat buses have no responder and
type-1 modules take a format error. The synthetic responder qualifies request
and architectural state behavior; it does not introduce a public package API or
qualify external module hardware timing. Reserved/unused frame fields are masked.

Production corrections discovered by these matrices include:

- Complete residual logical, bit, shift and system EA routes before approximate
  fallback; admit legal full indexed forms in their selected execution paths.
  Never retry an instruction after partial operand effects.
- Synchronize A7 operand changes with its architectural stack bank; retain
  source-before-destination effects and existing brief/fixed-cycle timing policy.
- Clear bitfield V/C, avoid signed-offset overflow, preserve BFFFO's original
  signed offset, implement CAS2 Dc1 failure precedence and failed atomic writeback.
- Implement missing 010 status/RTD/MOVES behavior; apply advanced SR privilege and
  alignment rules before 040's early-integer fallback.
- Store decremented SP for LINK A7 and preserve the pulled value for UNLK A7.
  CHK2 sets Z on either boundary; CMP2 compares an address register at full width
  against sign-extended byte/word bounds.
- Correct model-specific MOVEC availability, USP transfers, CACR reserved and
  clear-command masks, 060 BUSCR state and synchronous exception handling.
  Normal disabled-MMU memory transfers no longer erase 040 MMUSR.
- Implement MOVE16's five forms, 060 LPSTOP and 020 CALLM/RTM. Replace permissive
  historical MOVE16 extensions with the documented mandatory extension bit.
- Preserve synchronous master state, clear master state for interrupts, recognize
  directly requested level 7, emit documented trap/trace frames and consume
  throwaway RTE frames across stack switches. Keep 060's absent T0 bit masked.

### Qualification boundaries

The promoted gate covers the named semantic scenarios above. It **does not**
qualify faulted-instruction restart/internal-state restoration on 010 frame 8,
020/030 frames 9/A/B, or 040 frame 7 pending exceptions/writebacks. These remain
explicitly untested execution combinations in the qualified report; they are
not classified as architecturally invalid or as passing RTE coverage. Existing
cache, prefetch, bus ordering, detailed faults, JIT and native regressions remain
retained. A nested early-model address fault on an invalid odd supervisor stack
was observed during fixture development and remains an unresolved detailed-fault
edge; valid instruction fixtures do not hide it as a passing fault qualification.

BKPT tests the documented outcome without an external replacement device.
Physical MOVES function-code bus spaces and LPSTOP CPU-space broadcast are
unqualified by the current public bus boundary. Undefined MOVES self-base stored
values, simultaneous T1/T0, reserved module frame fields and undefined atomic overlap
results are excluded explicitly. FPU arithmetic, enabled MMU translation,
physical cache/pipeline behavior and OS compatibility remain outside the roadmap.
Diagnostic 010/060 results continue to be separate from desktop readiness.

### Validation evidence

- Full restored-source Release CPU suite: **4,592 passed, 7 optional skips,
  0 failed**. Deep audit separately enabled: seed 68020, 10,000 cases per profile
  in each of MOVE, arithmetic, logical and control, **320,000 passing cases**.
  Both deterministic and deep report validation require every exact count and
  zero mismatching/unsupported/untested required cases.
- All **20 targeted mutations** are detected by executed semantic scenarios,
  including all eleven retained MOVE/arithmetic proofs and nine new logical/system
  proofs. Mutated source is restored in `finally` and rebuilt with zero warnings.
  The CACR readback proof required adding clear-command bits to the input values;
  its replacement case is `68020/MOVEC/L/R0/control=002/store=True/super=True`.
  Proof SHA-256: `787210877e40827c651cbcaf077e0a5ccbc4b62a5d6211dea278ae82a1aef56d`.
- Retained AHX tests: **18 passed**. Private package
  `1.5.2-synthetic-dev.32` passes package/content validation; SHA-256
  `1147b920e313219bc5d95ddb87735d7767d58a89fb146a28a060d38cbfb6fca9`.
  It is **not published**; existing published versions remain immutable.
- Isolated CopperScreen `d9beae8` checkout resolves this exact package through
  `Copper68kBoundaryVersion`, preserving the existing NuGet dependency boundary.
  Production Release build: **0 warnings/errors**; host **149 passed, 6 optional
  media skips**; disk **74 passed**; separate engine diagnostics **1,080 passed**.
- Native 68000 Workbench 3.1 floppy boot at 0 and 2 MiB Fast RAM: **2 passed**.
  Native A1200 EC020 eight-plane boot and cold reopen: **1 passed**. The retained
  cycle, PC, framebuffer, RGB24/DOS proof checks and pinned inputs are unchanged.
  No ROM/media files are committed. These are deterministic integration replays,
  not throughput or physical-timing acceptance measurements.
- Specialized regressions are retained. Historical unsupported logical tests now
  assert the completed instruction result; historical MOVE16 fixtures use the
  documented extension bit, and the 040 invalid-MOVEC regression expects vector 4.
  No regression has been retired without replacement proof.

Evidence: `artifacts/m4-m5-final/`, `artifacts/m4-m5-seed-68020-fixed/`,
`artifacts/m4-m5-mutation-proof-final/`, `artifacts/synthetic-private-feed-32/`,
and the isolated consumer's `artifacts/m4-m5-validation/`. Reproduce with:

```powershell
./scripts/test-copper68k-synthetic.ps1
./scripts/test-copper68k-synthetic.ps1 -Deep -Seed 68020 -Samples 10000
./scripts/test-copper68k-synthetic-mutations.ps1
```

Cross-model independent external reference qualification and consolidation remain
milestone 6. Package publication remains a separately authorized release action.

## Milestone 6 progress: independent program audit and first consolidation

The reused Musashi adapter now executes both pinned integer directories across
all eight profiles: 538 program/profile combinations pass; 86 are explicitly
excluded for processor availability or reviewed fixture assertions. Input
SHA-256 identities, exclusions and execution counts are reported separately
from architectural combination coverage. Missing or incomplete requested inputs
fail the audit. The synthetic deterministic gate now has 8,333,190 logical
cases in the same 338 batches, including additional trace scenarios and an
original ASL defect input preserved across every profile.

Motorola manual review corrected the 040 T0 serializer rule after reproducing
481 mismatches. Two new isolated mutations detect the trace defect and 000 ASL
overflow loss. The original ASL regression and its synthetic replacement both
failed under the same mutation before retiring that pure semantic duplicate.
All specialized timing, prefetch, cache, fault, JIT and native tests remain.

See [reference qualification and consolidation evidence](COPPER68K_REFERENCE_QUALIFICATION.md)
for pins, exclusions, replacement identifiers and remaining implementation gaps.
Milestone 6 is still in progress: internal RTE restart and broader external
references remain open. The follow-up single-word 040
PFLUSH/PTEST decoder and privilege correction adds 34,850 disabled-MMU cases,
bringing the current deterministic gate to 8,368,040 cases in 339 batches.
PTEST MMUSR is undefined with translation disabled; enabled-MMU operation and
selective/global flushing remain outside this qualification.
Package publication remains a separately authorized release action.

Validation passes the full CPU suite (4,593 tests; six optional skips), the
deterministic/seeded report gates, AHX and isolated CopperScreen consumers using
unpublished private package `1.5.2-synthetic-dev.33`. Native Workbench and A1200
boot/persistence replays execute three cases without skips. Counts, package hash,
evidence locations and qualification boundaries are in the reference document.

The subsequent 040 decoder slice passes 4,601 ordinary CPU tests (eight opt-in
or optional skips), all 8,368,040 deterministic cases, the pinned cross-model
program audit and AHX. Isolated CopperScreen validation with unpublished
`1.5.2-synthetic-dev.34` passes the Release build, host/disk tests, separate
diagnostics and all three native boot/persistence cases. The reference document
records the package hash, reproduced failures, scoped MMU limits and evidence.

The subsequent 000 address-error double-fault slice adds 640 cases in one batch
for a current deterministic gate of 8,368,680 cases in 340 batches. Nested
address-error entry and odd error-handler addresses halt without recursive
stacking or retries. Interrupts and host task/subroutine entry do not wake the
latched double fault; the supplied-PC/SP reset API restores execution. Frame
contents, failed-entry ordering and normal re-faulting handlers are checked.
Warm classic/V2 JIT parity is scoped to the architectural-trace-forced interpreter
bridge. That slice exposed a separate compiled odd-access host exception; the
subsequent JIT alignment correction below resolves it before operand effects. External BERR/reset-vector
fault signaling is unavailable through the current public bus API. No general
bus-error or physical timing qualification is claimed, and milestone 6 stays open.

Validation of this slice passes 4,606 ordinary CPU tests (eight opt-in/optional
skips), all 8,368,680 deterministic cases, the pinned cross-model program audit
and AHX. Isolated CopperScreen Release, host/disk and separate diagnostic checks
pass with unpublished private package `1.5.2-synthetic-dev.35`; all three native
boot/persistence replays execute without skips. The reference document records
the package hash, qualified paths, separate JIT failure and evidence locations.

The subsequent 000 compiled-JIT alignment slice adds instruction-boundary parity
guards to classic, V2 bus/graph and V2 fast-read emission. Faulting instructions
enter the accurate interpreter before any compiled operand effects; completed
trace instructions write back normally. No partial instruction is retried.
Word/long operands, brief indexed and constant/PC-relative forms, stack accesses,
and JMP/JSR targets are covered; legal odd byte, MOVEP, LEA and PEA address values
are retained. The warm-cache suite executes 180 logical scenarios in 15 batches,
separate from the unchanged 8,368,680-case / 340-batch synthetic inventory.
Removing the guards and separately removing MOVE destination guards both
reproduce failures. No regression is retired, and milestone 6 remains open for
the reference/restart-frame qualification gaps recorded in the reference document.

Validation of the compiled-JIT slice passes 4,619 ordinary CPU tests (eight
optional/opt-in skips), the unchanged deterministic gate, 538 pinned reference
program/profile combinations (86 explicit exclusions) and 18 AHX tests. Private
unpublished `1.5.2-synthetic-dev.36` passes the isolated CopperScreen production
build, host 149/disk 74/diagnostics 1,080 and all three native boot/persistence
cases without native skips. The reference document records package identity,
mutation evidence, retained timing policy and remaining qualification limits.

The subsequent 010 format-8 structural slice adds 5,376 deterministic cases in
three batches, bringing the semantic gate to 8,374,056 cases in 343 batches.
Address errors now allocate 58 bytes, write the 26 information words and preserve
the three reserved holes. RTE validates its private version before popping and
probes the final word before loading the tail. The shared address-error guard
also protects 010 frame construction and handler entry from recursive faults,
with reset-only halt recovery. Existing MOVE side effects, saved-PC convention
and exception timing policy are retained.

Milestone 6 remains in progress: the structural frame uses placeholder input and
internal state, and does not resume a suspended instruction or implement RR
continuation. Version-zero acceptance is an emulator convention. RMW, physical
prefetch/stack-cycle sequencing, external BERR and the other advanced restart
formats remain unqualified. No regression is retired. The reference document
records the bounded before-fix failures, scoped gate and validation evidence.

Validation of the structural slice passes 4,624 ordinary CPU tests (eight optional/
opt-in skips), the 8,374,056-case gate, 538 pinned independent program/profile
combinations (86 explicit exclusions) and 18 AHX tests. Unpublished private package
`1.5.2-synthetic-dev.37` passes isolated CopperScreen Release, host 149, disk 74 and
separate diagnostics 1,080; all three native boot/persistence replays execute
without skips. The reference document records its hash and evidence.

The subsequent 010 word-MOVE/MOVEA continuation slice adds 127,776 cases in five
batches, for a deterministic gate of 8,501,832 cases in 348 batches. Generated
private format-8 images now retain the pending source/write phase, next PC and
completed prefetch words. RTE resumes the stacked word cycle for RR=0, or uses
software-supplied buffers/completed writes for RR=1. It never re-decodes the
instruction or repeats earlier operand effects. Source/destination faults, copied
and nested frames, aliases, A7, trace and malformed private images are checked.
Disabling the continuation reproduces the three original bounded failures.

The image is a private emulator encoding, not a hardware-internal layout.
Long/non-MOVE transfers, foreign silicon frames, RMW, external BERR and physical
restart timing remain unqualified. All normal successful paths retain their
existing ordering/timing policy; no public API changes or test retirement.
Milestone 6 remains in progress. The reference document records the private
layout, exact coverage, architectural source and qualification boundaries.

Validation passes 4,632 ordinary CPU tests (eight optional/opt-in skips), all
8,501,832 deterministic cases, the requested 538 pinned program/profile audits
(86 explicit exclusions) and 18 AHX tests. Private unpublished
`1.5.2-synthetic-dev.38` passes isolated CopperScreen Release, host 149, disk 74,
separate diagnostics 1,080 and all three native boot/persistence cases without
native skips. The reference document records package identity, mutations, exact
scope and remaining gaps; milestone 6 is not marked complete.

The subsequent SingleStepTests qualification slice adds a pinned independent
68000 instruction-body audit: 312,500 cases across 125 upstream-verified files,
with TAS/TRAPV explicit exclusions. The adapter preserves SR while selecting the
corpus's pre-trace boundary. Per-file counts/hashes and precise mismatches are
recorded; incomplete, changed, empty or limited requests fail. The script defaults
to all verified files and records any explicitly filtered subset.

That audit exposed LINK A7 alias sampling, which the prior synthetic expectation
also got wrong. Pinned WinUAE source corroborates sampling the original An on
000/010/020/030/060 and the existing early-decrement behavior on 040. Corrected
independent expectations reproduce 3,840 pre-fix failures in seven profiles;
the corrected implementation passes all 312,500 reference cases and 48,640 stack
scenarios. Existing extension/write/update order and timing keys are preserved.
The deterministic logical count remains 8,501,832 in 348 batches. No regression
is retired; WinUAE executable multi-model qualification and advanced restoration
protocols remain open. Milestone 6 stays in progress; this is scoped software
evidence, not exhaustive external coverage or physical hardware qualification.

Validation of the LINK/reference slice passes 4,632 ordinary CPU tests (eight
optional/opt-in skips), the unchanged 8,501,832-case gate, 312,500 pinned 68000
reference cases, 538 pinned program/profile combinations (86 exclusions) and
18 AHX tests. Private unpublished `1.5.2-synthetic-dev.39` passes isolated
CopperScreen Release, host 149, disk 74, diagnostics 1,080 and all three native
boot/persistence cases without native skips. The reference document records
package identity, negative input checks, reproduced failures and scope.

The subsequent WinUAE discovery checkpoint adds a reproducible pinned Windows
fixture/bridge preparation command and an opt-in audit across all eight profiles.
Preflight rejects missing, changed, empty or incompatible inputs; every model must
fail a deliberate NOP register-corruption probe. The native bridge now enables
CCR/unchanged-register assertions and closes leaked opcode-header streams.

The complete discovery records 1,295 passing and 86 mismatching opcode/profile
directories, with 11,890,943 callbacks and all eight corruption probes detected.
These are discovery results, not completed qualification. Failing directories
include partial executions; reference/adapter/CPU causes remain to be separated.
The requested audit fails on any mismatch. No production CPU fix, package release
or regression retirement is included. Milestone 6 remains in progress, including
resolution of these disagreements and the previously recorded restoration gaps.

Validation of this test-tooling checkpoint passes 4,638 ordinary CPU tests, with
nine optional/opt-in skips and zero failures. Fresh fixture generation reproduces
the failing audit counts above and all eight corruption probes. Source revision
rejection, script syntax and whitespace checks pass. Earlier production/consumer
validation remains separate; no new package or consumer replay is claimed.

The next reference-comparator checkpoint corrects an audit blind spot: Copperline
skipped modern exception-frame records. A test-only parser now validates saved
SR/PC, format/vector words and format-2/3/4 addresses, including normal 68000 and
trace frames. Unsupported extra/restart records fail explicitly. Independent
M68000PM masks limit SR comparisons to defined bits without changing CPU results.
Twenty-four register/SR/frame mutations are rejected and eight undefined-flag
acceptance controls pass across the eight profiles. Old frame-skipping bridges
are rejected before callbacks. Report schema 2 records assembly/native/input
identities and actual frame/masked-case counts.

The stronger audit records 1,304 passing / 77 mismatching groups, 11,133,876
callbacks and 1,371,000 frame checks; failing groups may be partial. It remains
red without excluding any family. The pinned TRAPcc generator disagrees with newer
WinUAE source and the documented PC rule, so remaining mismatches require source/
adapter/CPU triage rather than blindly changing the CPU. Milestone 6 remains open.

Final classification splits the 77 non-passing groups into 62 mismatching and
15 emulator-unsupported, with zero untested; all still fail the requested gate.
The ordinary Release CPU suite passes 4,652 tests with nine optional skips and
zero failures. Twenty focused rule/preflight tests and all 32 native controls pass.
Old frame-skipping bridges are rejected. Test-only changes preserve the production
CPU, timing policy, package/API and prior consumer evidence. No release or regression
retirement is included. The reference document records the scoped evidence and
remaining generator/adapter/CPU classification work.

The assigned-invalid logical follow-up adds 105,984 public-factory cases
(13,248 per profile): 207 independently encoded illegal ORI/ANDI/EORI and bit
destination words, both stacks and all 32 CCR states. It distinguishes static
BTST's illegal immediate destination from legal dynamic BTST, MOVEP and CCR/SR
forms. Unassigned EA register encodings remain outside this added matrix.
The expanded deterministic gate is 8,607,816 logical cases in 356 batches, with
missing new reports rejected. Before correction, 000/010/040 each fail 64 cases;
the other five profiles each report 13,248 unsupported executions. All new cases
pass after correcting static BTST and advanced illegal-form dispatch. A stale
regression expectation is corrected from M68000PM and retained.

The fresh pinned references retain 312,500 passing SingleStepTests cases and
538 Musashi program/profile passes with their explicit exclusions. WinUAE now
gets past the original invalid encodings and reaches later immediate-arithmetic/
atomic fixtures; 62 mismatching and 15 unsupported groups remain gate failures.
All 32 comparator controls pass. Private unpublished .40 validates the isolated
CopperScreen NuGet boundary, Release build, host/disk/engine diagnostics and all
three native boot/persistence cases. No package release or test retirement is
included. The reference document records pins, before/after counts, exact scope
and remaining work. Milestone 6 remains in progress.

Final validation of this slice passes 4,660 ordinary CPU tests with nine optional
skips and zero failures, all 8,607,816 deterministic cases, 18 AHX tests and the
scoped external/consumer replays above. The private .40 package identity and source/
assembly hashes are recorded. No seeded run or physical qualification is added.

The next assigned-invalid follow-up adds 76,032 immediate-arithmetic/CAS cases
across all eight profiles, using a shared test-internal invalid-operand fixture.
CMPI's 020+ PC-relative forms and legal CAS2 words remain separate legal cases;
unassigned encodings are labeled outside the new matrices. The matrix reproduces
21,888 mismatches and 29,760 unsupported executions before correcting the 000/010
CMPI plan constraint and advanced immediate/CAS legality dispatch. All new cases
pass, with successful operand ordering/timing policy unchanged and no partial retry.
The complete deterministic gate now has 8,683,848 logical cases in 372 batches,
and missing either new report group fails. Ordinary CPU validation passes 4,676
tests, with nine optional skips and zero failures.

Fresh SingleStepTests passes all 312,500 selected cases. Musashi discovers that
the `mc68000/move.bin` fixture itself contains a 020-only PC-relative CMPI and
has no compatible vector-4 handler. Those 000/010 rows are explicitly excluded
while the identical input executes on all six applicable profiles: 536 passing /
88 excluded, with the synthetic matrix qualifying the architectural trap. The
reference document records the source, exact word/offset/hash and manual rule.
WinUAE passes 34,880 selected 000 ILLEGAL cases, but still reports 61 mismatching
and 15 unsupported groups, now reaching status-transfer/MOVES invalid forms.
All 32 comparator controls pass. The private unpublished .41 package validates
isolated CopperScreen Release, host/disk/diagnostics and all three native replays;
AHX passes 18. A final targeted run validates the test-only exclusion adjustment
with the production assembly unchanged. No package release or test retirement
is included; milestone 6 remains in progress with its existing scope intact.

The status-transfer/MOVES follow-up adds 77,824 assigned-invalid cases across all
eight profiles, both stacks and all CCR inputs. The shared fixture proves vector
4, exact frames, preserved state/memory and absence of operand effects, including
valid MOVES load/store extensions. Unassigned encodings and legal CAS.L neighbors
remain separate. These cases detect 4,000 mismatches and 49,344 unsupported
executions before correcting 010 MOVE-from-SR privilege priority and advanced
status/MOVES legality dispatch. All new cases and the 48-batch affected gate pass.
Successful execution ordering/timing policy are unchanged; no partial retry.

The fresh full CPU run passes 4,692 tests with nine optional skips and zero
failures. The deterministic gate validates 8,761,672 logical cases in 386
reporting batches. Missing new reports and corrupt model/batch metadata fail.
Batch counts are now derived from validated reports; the prior hardcoded .41
summary overstated its 370 required reporting batches by two. Historical evidence
remains unchanged, with this correction recorded in the reference document.

WinUAE passes all 34,880 selected ILLEGAL callbacks on both 000 and 010. The
complete external audit still fails with 60 mismatching and 15 unsupported groups,
reaching later unary/multiply invalid operands. All 32 comparator controls pass.
Fresh SingleStepTests retains 312,500 passes and Musashi retains 536 passes / 88
explicit exclusions; AHX passes 18. The private unpublished .42 package validates
the isolated CopperScreen NuGet boundary, Release build, host/disk/diagnostics and
all three native boot/persistence replays. Source/package/DLL identities, exact
before/after cases, exclusions and limitations are recorded in the reference
document. No release, regression retirement, seeded or physical qualification is
added. Milestone 6 remains in progress for the existing integer/reference and
restoration requirements.

The next reference follow-up adds 421,120 cases in 48 reporting batches for
assigned illegal unary, word/long multiply/divide, CHK and bitfield operands,
plus 060 HALT/PULSE privilege, trace and recovery behavior. The corrected
baseline detects 5,120 mismatches and 189,440 unsupported executions, with
256 HALT recovery edges explicitly untested until their prerequisite works.
Legal LINK.L and HALT/PULSE aliases are separated from illegal NBCD/TAS forms;
both debug instructions are added to the complete integer inventory. Advanced
legality checks run before operand effects and unavailable-operation decisions.
HALT blocks subsequent tracing and cannot be restarted by interrupts or host
entry; STOP keeps its trace semantics. All 158 focused checks pass, including
the new cases, legal bitfields and affected trace/STOP behavior. Physical debug
signals, debug-port restart and pipeline commands remain unqualified.

Final validation passes 4,740 CPU tests with nine optional skips and zero failures,
and 9,182,792 deterministic logical cases in 434 reporting batches. Missing each
new report fails the gate. Fresh SingleStepTests passes 312,500 selected cases,
Musashi passes 536 programs with 88 exclusions and AHX passes 18 tests. WinUAE
passes the selected HALT/PULSE cases but still fails with 60 mismatching and 13
unsupported groups; all 32 comparator controls pass. The private unpublished
.43 package validates the isolated CopperScreen Release build, host/disk/engine
diagnostics and all three native boot/persistence replays. The reference document
records exact cases, source/package/assembly identities, preliminary fixture
corrections and remaining gaps. No release or regression retirement is included;
milestone 6 remains in progress.

The packing checkpoint corrects the A7 stride of PACK's word source and UNPK's
word destination. These are contiguous words; only the packed byte has A7's
special stride. Revised independent expectations expose the original error in
both production and the earlier fixture. The new `arithmetic-packing-memory`
group adds 89,728 cases in eight reporting batches across all profiles, covering
every memory register pair, both stacks, all CCR values, aliases, overlapping
operands and advanced address/value boundaries. Against `66d276e` it detects
14,208 mismatches; all new cases and existing decimal cases now pass. The report
gate requires this group and rejects its omission. Successful byte transfer
order and timing policy are preserved; physical bus qualification is separate.

The pinned WinUAE audit now passes all 12 PACK/UNPK groups (70,720 callbacks).
An integer callback ends at a real STOP/HALT boundary and compares actual state,
without advancing PC to the sentinel. The resulting 060 STOP privilege/state
disagreement remains open. The complete audit still fails with 48 mismatching
and 13 unsupported groups; all 32 comparator controls pass. No family is
excluded to make it green. The reference qualification document records the
manual/source evidence, old fixture error, terminal counts and remaining gaps.
No regression retirement or package publication is included. Milestone 6
remains in progress with its accepted scope intact.

Final validation passes 4,748 CPU tests with nine optional skips and zero
failures, plus 9,272,520 deterministic logical cases in 442 reporting batches.
Fresh SingleStepTests/Musashi and AHX checks pass. The private unpublished .44
package validates the isolated CopperScreen Release build, host/disk/engine
tests and three native Workbench/A1200 replays. An additionally selected
Workbench hard-disk theory without its HDF input is unavailable coverage. Exact
package, source, assembly and replay evidence is in the reference document.

The next checkpoint qualifies synchronous trap/trace priority. 040/060 suppress
the pending trace after a trap; RTE restores T1 and the following instruction
is traced. Earlier models keep their nested trace behavior. The new
`system-trap-trace` group adds 50,496 cases in eight batches, covering every
TRAP vector, TRAPV, word/long divide-by-zero and CHK, all immediate TRAPcc forms,
all CCRs, both stacks and applicable T1/T0. The corrected baseline detects
8,224 mismatches with 5,888 dependent return cases untested until entry works;
all cases now pass. Existing trace expectations are corrected on 040/060.

A separately manifested WinUAE `TraceTraps` preset applies an explicit manual-
qualified generator correction and passes 512 callbacks / frames, including
256 incoming-T1 cases on 040/060. Removing the CPU fix makes both profiles fail
their first T1 callback. Six corruption controls pass; empty profiles, changed
inputs and omitted deterministic reports fail. Other traced reference families
and models remain explicitly untested by this preset. The broad Basic audit
still has 48 mismatching and 13 unsupported groups and remains required.

Full CPU validation passes 4,756 tests with ten optional skips; the deterministic
gate validates 9,323,016 logical cases in 450 reporting batches. Fresh external
SingleStepTests/Musashi and AHX checks pass. The private unpublished .45 package
validates the isolated CopperScreen Release build, host/disk/engine tests and
three native replays without skips. The reference document records exact
manual/source caveats, pins, package/assembly identities, failed-before evidence
and remaining requirements. No old test is retired or package published;
milestone 6 remains in progress with the accepted scope intact.

The translation-control follow-up corrects 040/060 MOVEC TC register images
against their processor manuals. Reserved bits read zero; disabled writes are
canonicalized to `0000C000` / `0000FFFE`. A 040 reserved high bit no longer
activates the private MMU enable convention. This preserves private state and
timing policies and does not qualify enabled MMU behavior. Corrected canonical
expectations and the retained register-transfer regression accompany 145,280
new cases in two `system-translation-control` batches. They cover boundaries,
walking bits, general registers, every CCR, privilege, dependent readback,
unchanged state and a following sentinel. Nonzero reserved-bit writes are
explicit robustness/storage-policy cases; the defined zero-read rule is the
architectural expectation. The new cases detect 45,312 mismatches before the
fix, with 19,200 dependent phases untested until the prerequisite works; all
pass afterward. Missing the new 040 report fails the complete gate.

Full CPU validation passes 4,758 tests with ten optional skips. The report gate
validates 9,468,296 logical cases in 452 reporting batches. Pinned external
SingleStepTests/Musashi, AHX and the qualified 040/060 TRAP trace audit pass.
The broad WinUAE audit still fails 48 mismatching and 13 unsupported groups;
both MOVEC2 failures advance eight callbacks to the next ITT0 mask disagreement.
Transparent-translation/root-pointer masks and stage-specific 010 format-error
CCR/trace qualification remain open. The private unpublished .46 package
validates the isolated CopperScreen Release build, host/disk/engine tests and
three native replays. Exact source/input/package identities, failed-before
proof, initial invalid invocations and remaining scope are recorded in the
reference document. No package publication or regression retirement is added;
milestone 6 remains in progress with its full accepted scope unchanged.

The transparent-control checkpoint masks 040/060 ITT0/ITT1/DTT0/DTT1 MOVEC
images to `FFFFE364`, the processor manuals' defined zero-read bits. A shared
test-internal register fixture serves TC, TTR and root-pointer tests and checks
untouched control state as well as registers, CCR, PC, privilege, dependent
readback and sentinels. Eight new TTR batches add 622,592 cases; four legal
aligned root-pointer batches add 221,184. All 843,776 pass after the correction.
The TTR batches detect 114,688 mismatches before correction, with 57,344
dependent cases explicitly untested. Refactored TC and legal root transfers
already pass against that baseline. Reserved writes are robustness samples;
nonaligned root-pointer behavior and enabled translation remain unqualified.

The original broad WinUAE audit now passes the 040 MOVEC2 sequence and reaches
a later 060 BUSCR shadow-bit disagreement. Its overall result remains failing:
1,321 passing, 47 mismatching and 13 unsupported groups. No family is excluded
or expected state weakened. BUSCR, stage-specific 010 format-error behavior and
all previous restoration/reference/consolidation requirements remain open.
Milestone 6 stays in progress; no package publication or regression retirement
is included. Exact evidence and final validation are recorded in the reference
qualification document.

Final validation passes 4,770 CPU tests with ten optional skips, and the gate
validates 10,312,072 logical cases in 464 reporting batches. Missing the new
040 ITT0 report fails the gate. Fresh pinned SingleStepTests/Musashi, AHX and
qualified TRAP trace checks pass. The private unpublished .47 package validates
the isolated CopperScreen Release build, host/disk/engine tests and three native
Workbench/A1200 replays. Exact input/source/package identities and qualification
limits remain in the reference document.

The 060 BUSCR/control-field follow-up preserves exception shadow bits across
MOVEC writes and nested exception entry, while clearing active lock commands.
The later Motorola porting guide supplies the explicit nested-retention rule;
software-write preservation is a documented interpretation corroborated by the
pinned reference. Physical pin/cache/locked-access effects remain unqualified.
Undefined 060 MOVEC control fields now enter vector 4 before privilege checking,
with the same instruction fetch order and no transfer effects or retry.

Five required batches add 856,336 cases for BUSCR writes/readback, all shadow
images, nested TRAP/RTE, privilege/trace/reset, accepted/masked interrupts and
every 060 control-field encoding. The original BUSCR code and isolated legality
and interrupt mutations are detected; all new cases pass with fixes restored.
The canonical expectations are corrected and the reusable fixture extended;
no old regression is retired. The unchanged WinUAE 060 MOVEC2 group now passes
8,228 callbacks and 8,192 frames. Its broader audit still fails 46 mismatching
and 13 unsupported groups. PCR reference/manual disagreement and all earlier
restoration, reference and consolidation requirements remain open. Milestone 6
stays in progress; no package is published.

Final validation passes 4,775 CPU tests with ten optional skips. The report gate
validates 11,168,408 cases in 469 batches and rejects the missing new 060
control-encoding report. Fresh pinned SingleStepTests/Musashi, AHX and qualified
TRAP trace checks pass. The private unpublished .48 package passes the isolated
CopperScreen Release build, host/disk/engine checks and three native replays.
The reference document records exact identities, failed-before proofs and limits.

The TRAPcc/CHK2 reference follow-up qualifies a separate pinned `TrapBounds`
preset against Motorola's following-instruction saved-PC rules. Two missing PC
synchronizations are corrected in a copied generator source; CPU execution and
the unchanged Basic audit retain their behavior. Six advanced CPU profiles pass
21 groups, 951,522 callbacks and 476,339 frame checks. All 63 comparator controls
pass; an isolated wrong-saved-PC CPU mutation fails every group. Missing, changed,
empty and unqualified-source inputs are rejected before native execution. The
new dedicated audit command requires exact profile/family and callback/frame
coverage; it cannot pass from a skipped or empty selection.

CHK2's 060 architectural unavailability remains covered synthetically; the
focused generated 060 profile contains only TRAPcc. This is patched software
qualification with CCR 0/31 and ordinary stacks, not physical qualification or
exhaustive trace/fault coverage. The unchanged broad audit still fails 46
mismatching and 13 unsupported groups; its figures are not replaced by the new
preset. Milestone 6 remains in progress. No CPU source/package change or test
retirement is needed. Exact identities, scope and reproduction commands are in
`COPPER68K_REFERENCE_QUALIFICATION.md`.

Final follow-up validation passes 4,776 CPU tests with ten optional skips,
including the enabled trap/bounds audit. The deterministic gate retains
11,168,408 cases in 469 batches and `roadmapComplete=false`; fresh pinned
SingleStepTests/Musashi and the earlier qualified trace preset pass.

The breakpoint follow-up qualifies a separate corrected WinUAE `Breakpoints`
preset against Motorola's illegal-exception saved-PC rule. All eight encodings
pass on 010 and the six advanced profiles, at 224 callbacks / 224 frames with
all 21 comparator controls detected. A narrowly scoped wrong-saved-PC mutation
fails every reference group and 512 existing synthetic cases on each of all
eight profiles. Sources are restored exactly. Twelve preflight controls across
both exception presets reject missing, changed, empty, duplicate and unqualified
inputs. The TrapBounds and breakpoint adapters and CLIs now share their strict
runner, while preserving the old TrapBounds command and source qualification.
Regenerating TrapBounds retains its exact callback/frame coverage.

External breakpoint replacement and physical acknowledge timing remain
unqualified. The optional m68k-rs extra BKPT exclusion is retained with a corrected
reason because its handler fixture is unavailable; it is not a Musashi exclusion
and not evidence of missing standalone BKPT execution. No regression is retired
and no CPU/package behavior changes. The original broad failures and all prior
milestone 6 requirements remain open; exact identities and scope are recorded in
`COPPER68K_REFERENCE_QUALIFICATION.md`.

Final validation passes 4,777 CPU tests with ten optional skips, with both
exception reference presets enabled. The deterministic gate retains 11,168,408
cases in 469 batches and `roadmapComplete=false`; fresh pinned SingleStepTests,
Musashi and the existing trace preset pass. The unchanged broad audit retains
46 mismatching and 13 unsupported groups, so milestone 6 remains in progress.

The unassigned-CHK-EA follow-up corrects advanced decoding of mode-7 registers
5..7 to enter documented illegal-instruction vector 4 before operand effects.
The existing CHK invalid-source matrix adds 48 opcode words, both stacks and
every CCR: 24,576 new cases across all eight profiles. Failed-before runs detect
3,072 unsupported cases per affected advanced profile; the restored guard passes
all 90,112 invalid-CHK cases, with legal neighbors retained. No regression is
retired. The unchanged broad audit advances to the next unassigned word `4140`
and retains its failing status; its remaining families are not excluded.

The private unpublished .49 package passes the isolated CopperScreen Release
build, host/disk/engine checks and all three native Workbench/A1200 replays.
Source, package, loaded-DLL identities and precise scope are recorded in
`COPPER68K_REFERENCE_QUALIFICATION.md`. All earlier reference, advanced exception
restoration and consolidation requirements remain open; milestone 6 stays in
progress. No physical timing or new release is claimed.

Final validation passes 4,777 CPU tests with ten optional skips; both qualified
exception presets execute. The deterministic gate verifies 11,192,984 cases in
469 batches, rejects missing/old-cardinality CHK reports and retains
`roadmapComplete=false`. Fresh pinned SingleStepTests/Musashi and the separately
qualified TRAP trace audit pass. The broad audit still fails 46 mismatching and
13 unsupported groups, so the full milestone 6 objective remains open.

The line-4 illegal-encoding follow-up fixes narrowly defined advanced decoder
gaps for unassigned `4140`/`4E` words, invalid control EAs and MOVEM EAs, and
unassigned TST EAs. Independent matrices add 544,256 cases across eight profiles,
using both stacks and every CCR. Failed-before runs distinguish emulator
unsupported execution from architectural vector 4. An initial fixture wrongly
included EXTB.L aliases as illegal LEA; its expectation is corrected before the
CPU guard, with legal EXTB/SWAP/BKPT/EXT and system neighbors retained.

The unchanged broad audit advances to illegal integer word `5008` but retains
46 mismatching and 13 unsupported groups. No family exclusion, comparison-mask
change or regression retirement is made. The private .50 package is unpublished.
Exact source/package identities, failed-before proofs and qualification limits
are in `COPPER68K_REFERENCE_QUALIFICATION.md`. All earlier reference, advanced
exception restoration and consolidation requirements remain open; milestone 6
stays in progress.

Final follow-up validation passes 4,809 CPU tests with ten optional skips and both
qualified exception presets enabled. The strict gate verifies 11,737,240 logical
cases in 501 batches, rejects missing/new-group and stale-unary reports, and
retains `roadmapComplete=false`. Fresh pinned SingleStepTests/Musashi, qualified
TRAP trace and AHX pass. The private .50 package passes the isolated CopperScreen
Release build, host/disk/engine tests and all three native Workbench/A1200 replays,
with exact package/version and loaded-DLL identity checks.

The next illegal-integer follow-up adds six required matrices for ADDQ/SUBQ,
unassigned Scc-neighbor and MOVEQ words, invalid binary operands, unassigned
C180 words and invalid memory shifts. Their 2,379,776 cases across eight profiles
exercise every selected word, both stacks and all CCR states. Failed-before
coverage detects unsupported execution on five advanced profiles; narrow static
classification now enters vector 4 before operand effects. Legal data/address,
extend/decimal/packing, EXG/CMPM, DBcc/TRAPcc, register-shift and bitfield aliases
are preserved. No generic fallback, instruction retry or timing-policy change
is introduced.

The unchanged broad ILLEGAL group reaches line-F exception disagreements on all
advanced profiles. Overall Basic remains failing: 1,322 passing, 51 mismatching
and eight unsupported groups. Five groups now reach later architectural
mismatches instead of stopping at unsupported integer execution. The original
inputs and masks remain intact. Older MOVEQ/ADDQ/OR/EXG tests retain distinct
timing-policy assertions and are not retired. Exact authorities, failed-before
evidence and remaining gaps are in `COPPER68K_REFERENCE_QUALIFICATION.md`.
Milestone 6 remains in progress with its full earlier requirements retained.

Final follow-up validation passes 4,857 CPU tests with ten optional skips and
both qualified exception presets enabled. The strict gate verifies 14,117,016
logical cases in 549 batches and rejects each missing new 020 report. Fresh
pinned SingleStepTests/Musashi, qualified TRAP trace and AHX pass. The private
unpublished .51 package passes the isolated CopperScreen Release build,
host/disk/engine tests and all three native Workbench/A1200 replays, with exact
package/version and loaded-DLL identity checks. `roadmapComplete=false` is
retained; line-F priority, reserved multiply/divide extensions and earlier
advanced restoration/reference/consolidation requirements remain open.

The line-F follow-up qualifies cpSAVE/cpRESTORE first words, illegal 040/060
state-transfer operands and unassigned FPU command/conditional EAs. It corrects
020/030 privilege-before-absent-coprocessor behavior and 040/060 invalid-word
priority. Independent PC-relative 040 FRESTORE fixtures also expose and correct
reversed preindexed/postindexed pointer ordering. The new required batches add
464,000 cases across eight profiles. MMU opcode overlaps, legal supervisor
coprocessor/FPU protocols and self-referential frame/extension encodings are
explicitly distinguished; no FPU arithmetic qualification is claimed. The old
generic F123 example is corrected to an unassigned F1C0 word because F123 is a
legal privileged cpSAVE on EC020/020. No specialized regression is retired.

The unchanged WinUAE ILLEGAL groups now pass on EC020/A1200/020/030 and retain
000/010 passing coverage. Basic still fails overall: 1,326 passing, 47
mismatching and eight unsupported groups. 040 F400 has a reference/manual
exception disagreement, and 060 reaches further unassigned F380 decoding.
Reserved long multiply/divide extension qualification and all earlier advanced
restoration/reference/consolidation requirements remain open. Exact authorities,
failed-before evidence, mutation controls and caveats are maintained in
`COPPER68K_REFERENCE_QUALIFICATION.md`; milestone 6 remains **in progress**.

Final validation passes 4,874 CPU tests with ten optional skips and both qualified
exception presets enabled. The strict gate verifies 14,581,016 logical cases in
566 batches, with fresh pinned SingleStepTests/Musashi audits. Qualified TRAP
trace and AHX pass. Private unpublished .52 passes the isolated CopperScreen
Release build, host/disk/engine tests and all three native Workbench/A1200
replays, with exact package-resolution and loaded-DLL identity checks. Timing
policy is retained; physical timing, host performance and package publication
are not claimed. `roadmapComplete=false` remains unchanged.

The unassigned FPU category follow-up corrects the 060 unsupported-operation
guard for CpID 1 types 110/111. Every F380..F3FF word now enters the existing
architectural vector-11 path without operand effects. One new required batch
per profile adds 65,536 cases across eight profiles, both stacks and all CCR
states. Failed-before evidence detects all 8,192 060 cases; the other seven
profiles already pass. No FPU arithmetic, timing-policy change, instruction
retry or specialized-test retirement is introduced.

The unchanged broad Basic reference advances 060 to F400, matching the existing
040 reference/manual disagreement. Its overall 1,326 passing, 47 mismatching
and eight unsupported groups remain explicitly failing. Reserved long
multiply/divide extensions and all earlier advanced restoration, reference and
consolidation requirements remain open. Milestone 6 stays **in progress**.

Final validation passes 4,882 CPU tests with ten optional skips and both
qualified exception presets enabled. The strict gate verifies 14,646,552
logical cases in 574 batches, with fresh pinned SingleStepTests/Musashi audits;
missing and stale-cardinality category reports fail its controls. Qualified
TRAP trace and AHX pass. Private unpublished .53 passes the isolated CopperScreen
Release build, host/disk/engine tests and all three native Workbench/A1200
replays, with exact package and loaded-assembly identity checks. Timing policy
is retained; `roadmapComplete=false` remains unchanged.

The long-arithmetic follow-up qualifies the reserved-extension failures in the
Basic reference. A separate pinned `LongArithmetic` preset selects documented
MULL.L/DIVL.L encodings before generating expected results; undefined 64-bit
multiply register aliases are explicitly excluded while legal divide aliases
remain. Two copied source patches also qualify 060 vector-61 opcode PCs before
EA effects and preserve completed divide-by-zero EA updates. The latter is a
documented interpretation of the manual's group-3 completion rule, not hardware
qualification. No production CPU or comparison-mask change is made.

The complete advanced-model preset passes twelve groups: 28,418 callbacks,
4,992 frames and 12,420 model/family/sign/width/EA/register forms. Both signs
and 32/64-bit selections, including 060 architectural exceptions, are required.
All 37 corruption/undefined-flag controls pass; eight preflight defects fail.
Wrong-saved-PC and stale-cardinality mutations detect their intended defects;
sources are restored. Eighteen independent encoding checks join ordinary CI.
The original Basic corpus and its reserved-field failures remain intact. No
specialized test is retired; broader reference and restoration/consolidation
requirements keep milestone 6 **in progress**.

Final validation passes 4,901 CPU tests with ten optional skips, including the
three qualified presets. The strict gate retains 14,646,552 logical cases in
574 batches with fresh pinned SingleStepTests/Musashi. Qualified TRAP trace
passes separately. The original Basic audit retains exactly its 1,326 passing,
47 mismatching and eight reserved-encoding unsupported groups; those failures
remain visible. CPU sources and timing policy are unchanged; no new package or
consumer modification is needed. Source/input/assembly identities, historical
failures and limitations are maintained in the reference record. The goal stays
active with `roadmapComplete=false`.

### Milestone 6 word-division qualification — 2026-10-05

The pinned WinUAE unsigned-word-overflow helper omitted carry clearing on
020/030 despite the programmer reference and its own C=0 comment. A separate
`WordDivision` preset qualifies this defined-flag behavior through a copied CPU
generator. Existing CPU behavior and independent flag masks remain unchanged;
the original Basic corpus and its disagreements remain visible.

All eight profiles pass DIVS.W/DIVU.W: 134,928 callbacks, 37,804 exception frames,
87,936 masked-SR cases and 11,392 model/family/EA/register/input-SR combinations.
The shared qualified audit requires exact complete selections, pinned identities,
callback/frame/mask/form counts and raw input classification. Fourteen fixed
encoding/profile checks join ordinary CI. All 80 register/X/C/frame/undefined-flag
controls pass; eight missing/changed/empty/duplicate/unqualified inputs fail
preflight. A carry mutation causes six reference disagreements and 2,688 synthetic
boundary mismatches; a stale expected count fails its exact group. Sources are
restored before final validation. The addressing groups pass that mutation and
are explicitly not claimed as overflow detection. No old regression is retired.

The [reference record](COPPER68K_REFERENCE_QUALIFICATION.md#word-division-reference-qualification-2026-10-05)
maintains commands, exact inputs, corrections, failure evidence and scope limits.
Advanced restoration, other reference disagreements, independent coverage and
consolidation remain required. Milestone 6 stays **in progress**; no package
publication, consumer change, physical timing or enabled-MMU/FPU qualification
is included.

Final validation passes 4,916 Release CPU tests with ten optional skips, including
all four qualified presets. The strict gate retains 14,646,552 logical cases in
574 batches with fresh pinned SingleStepTests/Musashi audits. The fresh original
Basic audit retains 1,326 passing, 47 mismatching, eight unsupported and zero
untested groups; all 32 controls pass. It remains failing. Source/input/assembly
identities stay stable; the goal remains active with `roadmapComplete=false`.

### Milestone 6 advanced-restoration discovery — 2026-10-05

The new independent 040 access-frame audit reproduces 9,216 format-7 RTE
mismatches, retains 14,688 prerequisite/continuation phases as untested and
passes 6,912 short-frame control phases. Its complete 30,816-phase selection is
explicitly failing. Six fixed saved-SR examples join ordinary CI; the four
restoration batches remain an opt-in discovery gate until implementation and
reference qualification are complete.

A temporary normal/CT prototype reaches all downstream phases, but CM/CU/CP
gaps still fail the command. Skip-only and wrong-traced-address probes detect
all CT prerequisites. Production sources are restored; no CPU fix, package,
consumer change, timing-policy change or regression retirement is included.
The [reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-access-frame-restoration-discovery-2026-10-05)
maintains the full scope, authorities, failure evidence and limitations. Legal
advanced restoration on all applicable models, other reference disagreements,
independent combination coverage and consolidation remain required. Milestone 6
stays **in progress** with `roadmapComplete=false`.

Final Release focused validation passes 22 tests with four explicit discovery
skips. The complete requested discovery executes all four batches and six fixed
examples, exits 1 and records exactly the required failures/gaps. All thirteen
specific malformed-input/report controls are detected. No earlier green gate
is relabeled as advanced-restoration coverage.

### Milestone 6 normal/CT/CM implementation — 2026-10-05

The advanced 040 interpreter now restores normal and CT format-7 frames and
restarts CM MOVEM from its saved EA without recomputing indexes or reading pointer
chains. Five ordinary-CI batches pass 264,960 logical phases, including every
legal MOVEM word and all full-index structures. Separate reset/interrupt/one-shot
and warmed V1/V2 JIT tests prove continuation ownership. Original-CPU and omitted
JIT-guard probes detect the defects; restored focused tests pass. The exact
selection/report corruption controls remain effective.

The explicit discovery command still fails: 192 required CU/CP cases remain
untested. Detailed frame faults, real access-fault entry/writeback handling,
other-model advanced restoration, independent combination qualification and
consolidation remain required. Milestone 6 is **in progress**, with
`roadmapComplete=false`; this does not narrow the accepted completion gate.

Full Release CPU validation passes 4,932 tests, eleven optional/opt-in skips and
zero failures with all four qualified presets enabled. Isolated unpublished
`1.5.2-synthetic-dev.54` validates CopperScreen through NuGet: clean Release build,
host 149/six skips, disk 74, engine 1,080, native Workbench two and A1200 one
without skips. Package/assets/loaded DLL identities match. Published versions and
the root CopperScreen checkout are untouched. See the
[reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-normal-ct-and-movem-restoration-checkpoint--2026-10-05)
for architectural authority, exact coverage, evidence and remaining scope.

The strict gate verifies 14,911,576 passing logical cases in 579 reporting batches,
with fresh pinned SingleStepTests (312,500 cases / 125 files) and Musashi
(536 programs / 88 exclusions across eight profiles); roadmapComplete=false.

### Milestone 6 CU/CP delivery implementation — 2026-10-05

CU/CP RTE now converts the access frame to the pending exception and retains its
selected vector across interrupted delivery, nested handlers and FPU context
changes. Sixteen state/JIT scenarios prove actual delivery ownership, reset,
redirected PCs and no repeated completed operand store. Independent CU and CP
matrices add 110,592 passing phases in two ordinary-CI reporting batches. Original
return code and vector/lifetime mutations reproduce failures; restored focused
tests and corruption controls pass.

The complete discovery command remains failing: 375,552 passing phases and 576
explicitly untested fault/context requirements. Real access-frame entry,
validation faults, odd user trace PCs, throwaway frames, writeback-handler
qualification and CP context-transferred vector recovery remain required, as do
other-model restoration, broader independent coverage and consolidation. The
[reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-cucp-pending-delivery-checkpoint--2026-10-05)
records the exact scope, manual authority and evidence. Milestone 6 and the full
goal remain **in progress**; `roadmapComplete=false`. No FPU arithmetic, enabled
MMU, physical timing or OS qualification is inferred.

Final validation passes 4,950 Release CPU tests (eleven optional/opt-in skips),
15,022,168 logical cases in 581 strict reporting batches, fresh pinned
SingleStepTests and Musashi audits. Private unpublished `.55` validates the
isolated CopperScreen Release build, host/disk/engine tests and all three native
boot replays through NuGet. Package/assets/loaded DLL identities match; see the
reference record for counts and hashes. No package publication is included.

### Milestone 6 throwaway chaining qualification — 2026-10-05

Independent short/access matrices add 511,488 passing phases for every one/two
throwaway bank path, all restored banks/CCRs/trace states and even/odd stack data
addresses. Normal/CM/CT/CU/CP conversion, exact stack/PC/SR, following instruction,
discarded-PC nonfetch and handler writeback non-replay are checked. Production
source is unchanged from `9fa9f84`. Maintained consolidation mutations detect
premature termination and missing stack selection, then restore/rebuild source.

The complete 040 discovery remains failing: 887,040 passing phases, zero
mismatching/unsupported and 480 untested validation/fault/context requirements.
The throwaway inventory item is promoted to executable coverage; the remaining
five categories and all broader milestone requirements are retained. No old
regression is retired. See the
[reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-chained-throwaway-qualification--2026-10-05).
Milestone 6 and the goal remain **in progress**; `roadmapComplete=false`.

Validation passes 4,952 Release CPU tests (eleven optional/opt-in skips),
15,533,656 logical cases in 583 strict reporting batches, and fresh pinned
SingleStepTests/Musashi audits. All four maintained consolidation mutations
detect their defects and all thirteen report/input controls remain effective.
Production source and the preceding private `.55` consumer evidence are
unchanged; no new package is published. An exploratory odd-user-trace-PC probe
reproduces the next restoration defect; it remains required fixing work rather
than passing qualification. See the reference record for exact evidence.

### Milestone 6 direct odd-PC return implementation — 2026-10-05

040 direct odd-PC returns now take a format-2 address error during RTE, saving
the causing instruction PC and the aligned fault address without fetching the
odd target. CT/CU/CP delivery has priority; its handler return then takes the
address error. CM retains no unusable continuation. Three independent ordinary
CI batches add 237,312 passing phases; 540 accurate/V1/V2 state scenarios require
warmed compiled dispatch and RTE fallback. Four maintained `-Scope Rte040`
mutations detect frame, address, saved-SR and pending-priority defects, then
restore/rebuild production source.

The direct saved-SR image follows documentary pinned WinUAE source; it is not
an executed hardware oracle. The chained user-tail SR question retains its
explicit inventory requirement. Complete 040 discovery still requires 480
fault/context cases; other-model restoration, broader independent coverage and
consolidation remain required. No regression is retired. The
[reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-odd-pc-return-correction--2026-10-05)
records primary manual authority, source identities and failed-before/mutation
evidence. Milestone 6 and the full goal remain **in progress** with
`roadmapComplete=false`; public release authorization remains separate.

Validation passes 4,958 Release CPU tests (eleven optional/opt-in skips),
15,770,968 logical cases in 586 strict reporting batches and fresh pinned
SingleStepTests/Musashi audits. Complete 040 discovery passes 1,124,352 phases,
has no mismatches/unsupported promoted phases and still fails on 480 named
untested requirements. Isolated unpublished `.56` validates CopperScreen through
NuGet: clean Release build, host 149/six skips, disk 74, separate engine 1,080,
and all three native Workbench/A1200 replays pass without skips. Package/assets/
loaded DLL identities match. No public package is published.

All thirteen input/report controls detect their defects. Fresh broad Basic
discovery retains 1,326 passing directories, 47 mismatching and eight unsupported,
with the same per-model/opcode statuses and callback/frame/mask counts; its 32
comparator controls remain effective. These gaps remain required work.

### Milestone 6 LPSTOP encoding qualification — 2026-10-05

060 LPSTOP now validates its fixed second opcode word before privilege handling
and raises line-F for unrecognized encodings. Nine independent CI batches add
228,350 cases: every malformed second word in both privilege modes, plus
encoding/status/CCR/trace boundaries across all eight profiles. The manual's
original SR/opcode-PC frame rules, trace behavior, S-clear rejection and stopped
sentinel nonretirement are checked. Three maintained mutations detect wrong
vector, early privilege handling and an omitted S-clear check, then restore and
rebuild source. No regression is retired.

See the [reference record](COPPER68K_REFERENCE_QUALIFICATION.md#lpstop-encoding-and-exception-priority-qualification--2026-10-05)
for authority, failed-before counts and mutation evidence. Chained odd-PC SR,
ordinary STOP's undocumented software rule, legal LPSTOP saved-PC reference
disagreement, physical broadcast and all earlier milestone requirements remain
open. Milestone 6 and the full goal remain **in progress**; `roadmapComplete=false`.

Validation passes 4,967 Release CPU tests (eleven optional/opt-in skips),
15,999,318 logical cases in 595 strict reporting batches and fresh pinned
SingleStepTests/Musashi audits. Private unpublished `.57` validates CopperScreen
through NuGet: clean Release build, host 149/six optional skips, disk 74, separate
engine 1,080 and all three native Workbench/A1200 replays pass without skips.
Package/assets/loaded DLL identities match. No public package is published.

Five specific missing/empty/stale/foreign-model report controls reject their
defects. Fresh complete 040 discovery still fails on 480 named untested cases,
with 1,124,352 passing phases and no promoted mismatches. Fresh broad Basic
discovery retains 1,326 passing directories, 47 mismatching and eight unsupported;
its per-model/opcode statuses and counts are unchanged, and all 32 controls pass.
These unresolved requirements remain part of the goal.

### Milestone 6 physical RTE validation faults — 2026-10-05

040 physical map faults during pre-commit RTE validation now build a format-7
frame with the original access width/address, RTE PC and live SR, preserving the
incomplete frame and committed throwaway stack effects. Two ordinary-CI batches
add 223,872 passing entry/handler-return phases across direct and chained
supervisor stacks. Five maintained mutations detect format, size, address, PC
and continuation-read marker defects, then restore/rebuild source. Five report/
identity controls reject their intended defects. No regression is retired.

Full Release CPU validation passes 5,046 tests (eleven optional skips), all nine
qualified WinUAE presets, 16,316,886 strict logical cases in 605 batches and fresh
pinned SingleStepTests/Musashi audits. Isolated unpublished `.58` validates the
CopperScreen Release build, host/disk/engine suites and three native boot/
persistence replays through NuGet, with matching package/assets/loaded binaries.
Published versions and the root CopperScreen checkout remain untouched.

Complete 040 discovery still fails on 480 named untested fault/context cases,
with 1,348,224 passing phases and no promoted mismatch/unsupported execution.
User-tail validation, internal-restoration/double faults, re-execution after
software repair, warmed JIT fault cases, other instruction fault entry,
writeback handlers, CP context transfer and all broader remaining reference/
consolidation requirements remain open. See the
[reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-physical-rte-validation-fault-correction--2026-10-05)
for exact scope, failed-before evidence, authorities and identities. Milestone 6
and the full goal remain **in progress** with `roadmapComplete=false`; publication
requires separate release authorization.

### Milestone 6 access-fault entry halt qualification — 2026-10-05

040 interpreter and compiled access-fault entry now latch HALT on a second
stacking/vector fault, with no retry of partial side effects. External reset
clears the latch; interrupt, host task/subroutine and batch execution cannot
restart it. Classic B/W/L memory emitters now use the model-aware 040 helpers,
including physical-map rejection and unaligned accesses. Compiled fault entry
preserves master-stack selection and clears trace. Existing timing keys and
the compiled exception-cycle policy remain intact; physical timing is separate.

Five ordinary batches add 105,408 passing scenarios: every byte of format-7
stack/vector rejection, later handler refaults and warmed accurate/classic/V2
dispatch with B/W/L read/write faults. Ten maintained mutations detect latch,
reset, compiled halt, stack and six memory-routing defects. Five report/source
controls reject missing, shortened, foreign and stale identities. No old test
is retired. Partial stacking order and generic short operand-fault frames are
not promoted as architectural format-7 restart qualification.

Full CPU validation passes 5,051 tests (eleven optional skips), all nine
qualified WinUAE presets, 16,422,294 strict logical cases in 610 batches and
fresh pinned SingleStepTests/Musashi audits. Private unpublished `.59` validates
the isolated CopperScreen Release build, host/disk/separate engine suites and
all three native Workbench/A1200 replays through NuGet; package/assets/loaded
CPU binaries match. No public package is published.

Complete 040 discovery has 1,453,632 passing scenarios, no promoted mismatch/
unsupported cases and 480 retained untested requirements. Active accurate-batch
fault delivery, internal-restoration faults, handler-entry prefetch, repaired-RTE
retry, user-tail validation, chained SR provenance, real general access-fault
frames, writeback/context transfer and earlier broader qualification/
consolidation requirements remain open. The
[reference record](COPPER68K_REFERENCE_QUALIFICATION.md#040-access-fault-entry-halt-qualification--2026-10-05)
records authorities, exact identities, failed discovery, mutation evidence and
scope. Milestone 6 and the full goal remain **in progress**, `roadmapComplete=false`.

### Milestone 6 accurate 040 batch-fault checkpoint — 2026-10-05

Accurate 040 batch execution now delivers physical faults through the same
exception handler as scalar execution. Cold, normal cached, model-specific
cached and self-branch paths preserve completed instruction counts and boundary
callbacks. A failed instruction is not retried after partial operand effects.
Other models retain their existing fault behavior and the existing timing policy.

Two ordinary-CI reporting batches add 139,392 scenarios: 129,024 supervisor RTE
validation faults and 10,368 operand/fetch dispatch cases. They check warmed
block use, completed prefixes, both supervisor stacks, all CCRs for RTE,
selected physical byte faults, trace, odd/even addresses, VBR, batch limits,
fatal second faults and handler sentinels. Both batches failed completely
before correction and pass after it. Architectural RTE expectations are
independent; generic short frames and scalar/batch bus and cycle equality are
existing execution-policy checks, not architectural restart or physical timing.

No regression is retired. Dedicated 040 discovery combination integration and
maintained per-path mutation integration remain pending. The 480-case discovery
inventory is retained, along with user-tail/internal-restoration faults, entry
prefetch, repaired-original-RTE retry, general architectural format-7 restart,
writeback/context transfer and earlier reference/consolidation gaps. Milestone 6
and the full goal remain **in progress** with `roadmapComplete=false`.
See the [checkpoint record](COPPER68K_REFERENCE_QUALIFICATION.md#040-accurate-batch-fault-checkpoint--2026-10-05).

Validation passes 5,053 Release CPU tests with eleven optional skips, all nine
qualified WinUAE presets and 16,561,686 strict logical cases in 612 batches.
Fresh pinned SingleStepTests/Musashi audits pass. Four report controls reject
missing/shortened new batches. Isolated unpublished `.60` passes the CopperScreen
Release build, host/disk/separate engine suites and all three native boot replays;
the package, assets and loaded CPU binaries match. No public release is made.

### Milestone 6 batch-fault qualification gate — 2026-10-05

The dedicated 040 audit now includes both batch-fault groups and independently
enumerates every required combination, using fixed validation read ranges rather
than the C# fixture's read plan. It requires nine distinct fixture/command inputs,
CPU source and assembly identities, and 28 executed tests: 22 reporting batches
and six fixed examples. It verifies 1,593,024 passing scenarios, zero mismatches
or unsupported execution, and the retained 480 named untested requirements.
The complete gate therefore still fails; milestone 6 is not complete.

`-Scope BatchFault` adds seven maintained mutations: four distinct delivery
paths, self-branch instruction count, cached callbacks and retry after partial
effects. Every mutation executes both complete batches and must produce semantic
mismatches in its intended path. All seven are detected, with no unsupported or
untested mutation cases; production sources are restored and rebuilt. No new
production behavior, public API, package release or regression retirement is
included. The [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-batch-fault-gate-and-mutation-qualification--2026-10-05)
preserves detailed counts, identities, controls and remaining work.

Eleven copied-input controls reject missing/short/foreign/distributed new reports
and missing fixture or changed CPU/binary identities. Ordinary report validation
retains 16,561,686 cases in 612 batches; full CPU/external/consumer evidence is
retained from the preceding production checkpoint, rather than rerun for this
test/gate-only change. The complete required-gap inventory remains explicit and
`roadmapComplete=false`.

### Milestone 6 executed RTE repair and retry — 2026-10-05

Two ordinary-CI batches execute a real access-error repair handler, its RTE,
the original RTE again, optional pending exception return and the following
instruction. They add 912,384 logical phases across 46,332 combinations. Direct
boundaries cover all CCRs and incoming/restored trace states; chained fixtures
fault each byte of selected validation reads after committed supervisor
throwaways, with both VBRs and odd/even stacks. Repaired results select any of
the three stack banks. Formats 0/2/3, invalid 4/15 repaired to 0, and normal,
CM, CT, CU and original-vector CP49 returns are covered.

The handler's three original-frame stores and saved incoming-trace clear have
fixed instruction encodings and independent state/memory expectations. Retry
must not read consumed throwaways; MOVEM uses the restored EA, pending delivery
uses the repaired SR/PC, and following trace uses the repaired trace bits.
`-Scope RteRepair` detects wrong return PC, saved MOVEM EA and pending stacked
SR in the intended retry/following phase. Every mutation executes both complete
batches; sources are restored and rebuilt.

The dedicated 040 gate includes these independently enumerated reports and
fixture identities. The 480-case inventory stays explicit: user-tail validation,
internal restoration, handler-entry prefetch, untouched incoming-trace retry,
general real format-7 restart, writeback/context transfer and all earlier
model/reference/consolidation requirements remain open. No regression is
retired, CPU production behavior changed or package published. Milestone 6
and the goal remain **in progress**, with `roadmapComplete=false`.
See the [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-executed-rte-repair-and-retry--2026-10-05).

Fresh ordinary validation passes 616 tests without skips/failures and checks
17,474,070 logical cases in 614 reporting batches across all eight profiles.
Complete 040 discovery has 2,505,408 passing phases, zero mismatches or
unsupported execution and the retained 480 untested requirements. All three
repair mutations and nine report/identity controls detect their intended defects.
Previous full CPU/external/private-consumer evidence is retained separately;
no new external or consumer execution is claimed for this test/gate checkpoint.

### Milestone 6 instruction-fetch access faults — 2026-10-05

Cache-disabled accurate instruction-fetch faults now stack format 7 rather than
the generic 8-byte frame. The executing instruction boundary supplies the saved
PC; the independent FA field retains the aligned prefetch address. This covers
opcode, extension and following-opcode faults, including the cached self-branch
path. Instruction function code is distinguished from data and all writebacks
are invalid. Data fault/writeback restart is still required and is not implemented
by changing a frame tag or replaying partial operand effects.

Two ordinary batches add 116,736 passing phases across 1,200 combinations. They
check all CCRs, all three stack banks, incoming trace for frame/handler return,
odd/even stacks, VBRs, each byte of the selected long prefetch and deterministic
restart values. Handler RTE, original instruction execution and following branch
sentinels are verified. Historical source fails every fault-entry fixture.
Three maintained `InstructionFault` mutations detect short frame, wrong TM and
prefetch PC substitution; all seven `BatchFault` mutations are retained and
detected with architectural self-fetch expectations. Nine report controls reject
missing/short/foreign/distributed reports and omitted fixture identity.

The complete 040 discovery selection is 32 tests: 26 reporting batches and six
fixed examples. It records 2,622,144 passing phases, zero mismatches/unsupported
execution and the preserved 480-case untested inventory. Data writebacks/restart,
compiled instruction-fault PC provenance, enabled-cache/speculative prefetch,
handler-entry prefetch, user-tail/internal-restoration faults, untouched incoming
trace retry, context transfer and all earlier model/reference/consolidation
requirements remain explicit. No regression is retired or public package/API
released. Milestone 6 remains **in progress**, `roadmapComplete=false`.

Fresh full Release CPU validation passes 5,057 tests with eleven optional skips
and no failures; all nine pinned WinUAE selections match their exact counts and
binaries. Strict ordinary validation checks 17,590,806 cases in 616 reporting
batches. Fresh pinned SingleStepTests passes 312,500 cases in 125 files and
Musashi passes 536 programs with 88 explicit exclusions. Isolated CopperScreen
through private unpublished `.61` passes Release build, host/disk tests, separate
engine diagnostics and all three native Workbench/A1200 replays; package,
assets and loaded CPU identities match. Software references and existing timing
policy remain separate from hardware/physical timing qualification. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-instruction-fetch-access-faults--2026-10-05).

### Milestone 6 host-reader boundary and handler-entry discovery — 2026-10-05

The accurate 040 engine no longer builds speculative hot blocks through a
logical host-read fallback that performs real CPU fetches before the instruction
boundary. Buses without a physical host code reader use ordinary execution;
buses with a host reader retain the fast path. Two ordinary batches add 24,576
passing cases for denied cold-batch boundaries and faults after an executed
handler prefix. A maintained mutation restores the old admission rule and must
fail at the denied boundary.

Two reference-discovery batches execute the complete four-long handler-entry
prefetch requirement: 393,216 mismatching cases across scalar/batch routes.
They do not promote the existing deferred demand-fetch policy as architectural
entry qualification. The complete 040 gate now requires 36 tests, 30 reports,
six fixed examples and twelve fixture identities, while preserving its 480-case
untested inventory. The next correction must implement buffered entry prefetch,
fatal entry faults and the boundary to later handler execution; all earlier
requirements remain open. No regression or public API/package is retired or
released. Milestone 6 remains **in progress**, `roadmapComplete=false`.
See the [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-host-reader-boundary-and-handler-prefetch-discovery--2026-10-05).

Validation passes 5,059 Release CPU tests with thirteen opt-in skips and all nine
pinned WinUAE selections. Strict ordinary reporting checks 17,615,382 cases in
618 batches; fresh SingleStepTests passes 312,500 cases / 125 files and Musashi
passes 536 programs with 88 exclusions. All seventeen report/identity controls
detect their intended corruptions. Isolated CopperScreen through unpublished
private `.62` passes Release build, host/disk suites, separate engine diagnostics
and all three native replays with matching package/assets/CPU binaries. The
complete 040 audit intentionally fails: 2,646,720 passing cases, 393,216
handler-entry mismatches and 480 untested requirements. Ordinary skips and
software references do not imply completion of that required discovery scope.
### Milestone 6 access-error handler entry window — 2026-10-05

The accurate, cache/MMU-disabled 040 instruction-fault and supervisor RTE
validation paths now acquire and retain the four-long handler-entry window.
Entry bus faults and odd handler addresses halt before instruction execution;
later demand faults preserve the executed prefix and start another format-7
exception. Retention and selected flow/host/task/map invalidation are tested
independently of production fetch calculations. The former handler+4 demand
fixture moves beyond the retained window to handler+16.

The complete 393,216-case entry-byte matrix is promoted into ordinary CI.
Retention/context and odd-address groups add 39,936 cases; all eight handler
groups total 457,728 cases. Five maintained mutations target missing entry,
missing fourth long, discarded data and stale subroutine/task contexts. The
complete 040 audit requires 40 tests, 34 reports, six fixed examples and twelve
input identities, with independently enumerated combinations.

Historical failed discovery and intermediate fixture-development results remain
separate from fresh qualification. Other exception entry routes, enabled-cache
deferral, compiled fetch provenance, data writeback/restart and every earlier
model/reference/consolidation requirement remain required. The existing
480-case untested inventory is retained and continues to fail the complete
gate. No regression is retired or public API/package released. Milestone 6
remains **in progress**, `roadmapComplete=false`. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-access-error-handler-entry-window--2026-10-05).

Fresh validation passes 5,065 Release CPU tests with eleven optional skips,
all nine pinned WinUAE presets, and the strict 18,048,534-case / 624-batch gate.
Fresh SingleStepTests passes 312,500 cases in 125 files; Musashi passes 536
programs with 88 exclusions. All five entry mutations, the refreshed host-reader
mutation and 33 report/identity controls detect their intended failures.
The complete 040 audit has 3,079,872 passing cases, zero mismatches/unsupported
execution and the retained 480 untested requirements; its gate remains failed.
Isolated CopperScreen through unpublished private `.63` passes Release build,
host/disk suites, 1,080 separate engine diagnostics and all three native replays,
with matching package/assets/CPU binaries. Evidence and limits are recorded in
the qualification record; this checkpoint does not complete milestone 6.

### Milestone 6 user-tail RTE validation and executed repair — 2026-10-05

Four ordinary-CI batches add **1,687,040 passing cases** for throwaways selecting
USP, including both values of the live user M bit. Fault expectations compose
MC68040UM's documented live-SR throwaway behavior with general access-exception
entry; this is manual-derived software qualification, not an observed hardware
reference for the unusual combination. The selected supervisor stack receives
the access frame, while the incomplete user frame stays intact and consumed
throwaway stack-pointer effects survive. A bare return restores user mode and
the next RTE raises privilege violation. A real seven-store handler instead repairs the user frame, constructs
a new throwaway bridge and explicitly sets saved S before retrying RTE.

Canonical SR-read cases cover all 32 CCRs. Structural cases reject every byte of
SR/PC/format/SSW/continuation-EA reads, one/two-throwaway paths through any stack,
both alignments/VBRs and all supported frame/continuation forms. Repair covers
all restored stacks/traces in its canonical group, continuation delivery and
the following instruction. Three maintained `UserRteFault` mutations detect
saved-S corruption, supervisor-data TM and forced ISP selection on both routes.
Production CPU source is unchanged; the shared test frame checker now derives
TM from the independently specified fault SR.

The complete 040 gate requires 44 tests, 38 reporting batches, six fixed examples
and thirteen input identities. Its independent iterator checks each new
combination and cardinality. The 480-case inventory remains failing for internal
restoration, chained odd-PC provenance, general data/writeback/context transfer
and other pending requirements. Untouched incoming-trace retry and all earlier
model/reference/consolidation work remain required. No regression is retired,
public API changed or package released. Milestone 6 remains **in progress**;
`roadmapComplete=false`. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-user-tail-validation-and-software-repair--2026-10-05).

Fresh full Release validation passes 5,069 CPU tests with eleven optional skips
and zero failures. All nine pinned WinUAE presets retain their exact counts and
matching CPU/adapter identities. The strict ordinary gate passes 19,735,574
cases / 628 batches; fresh SingleStepTests passes 312,500 cases / 125 files and
Musashi passes 536 programs with 88 exclusions. The dedicated 040 run passes
4,766,912 cases with zero mismatches/unsupported execution, retaining its 480
untested requirements and failing completion. All three mutations and seventeen
report/identity controls detect their intended failures. No consumer or package
change is needed for this test-only checkpoint; preceding `.63` consumer
evidence remains historical evidence of its own qualified binary.

### Milestone 6 preserved incoming trace after RTE repair — 2026-10-05

Four ordinary scalar/batch reports add **2,729,088 phases** for supervisor-tail
repair that leaves the access frame's saved SR untouched. The real handler
executes exactly three original-frame stores. Its return restores incoming
T1/T0, and the original RTE traces on completion using repaired SR/PC even
when its restored SR clears trace. An intervening trace-handler return and
the following instruction verify that CM retains its saved MOVEM address.
Expectations follow MC68040UM trace and RTE rules, with software fixtures rather
than an executed hardware oracle.

Canonical faults cover all CCRs. Structural faults cover every validation-read
byte, twelve supervisor throwaway paths, both alignments/VBRs and all incoming/
restored trace and restored stack combinations. Forms are 0/2/3, invalid 4/15
repaired to 0 and normal/CM format 7. Three maintained `RteRetryTrace` mutations
target restored-bit trace gating, omitted T0 RTE classification and CM consumed
by the trace handler. The complete 040 gate requires 48 tests, 42 reporting
batches, six fixed examples and thirteen input identities, checking each
combination independently.

Pending CT/CU/CP, user-tail trace bridge and mixed trace values within throwaway
chains remain required separately, along with the earlier internal-restoration,
chained odd-PC, general data/writeback/context-transfer and broader model/
reference/consolidation work. The 480-case inventory remains required and
fails completion. Production CPU source is unchanged; no public API/package
change or regression retirement is included. Milestone 6 stays **in progress**,
`roadmapComplete=false`. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-preserved-trace-rte-repair--2026-10-05).

Fresh validation completed on 2026-10-06: **5,073** full Release CPU tests pass,
with eleven optional skips and zero failures. All nine pinned WinUAE selections
match their exact counts and CPU/adapter identities. The strict ordinary gate
passes **22,464,662 cases / 632 batches**; fresh SingleStepTests passes 312,500
cases / 125 files and Musashi passes 536 programs with 88 exclusions. All three
new mutations, three refreshed legacy repair mutations and seventeen new
report/identity controls detect their intended defects. The complete 040 run
has 7,496,000 passing cases, zero mismatches/unsupported execution and the
retained 480 untested requirements; it continues to fail completion. No new
consumer or package validation is needed for this test-only checkpoint.

### Milestone 6 pending delivery with preserved trace — 2026-10-06

Four scalar/batch groups add **1,824,768 phases** for repaired CT/CU/CP49
frames with untouched incoming trace. They verify pending conversion instead
of an extra automatic RTE trace, repaired saved SR/PC/EA, format/vector,
pending consumption, handler return and the following traced BRA. Canonical
cases use all CCRs; structural cases use CCR 0/31 and every validation-read
byte across twelve supervisor paths, both alignments/VBRs and all incoming/
restored traces and restored stacks. Expectations compose MC68040UM 8.3 and
8.4.6.2/7 with the existing repair/exception-entry fixtures.

The first focused run passes all four groups without skips or missing phases.
Three maintained `RtePendingTrace` mutations target extra RTE trace delivery,
incorrect saved SR and suppressed following BRA trace. The complete 040 gate
now requires 52 tests, 46 reports, six fixed examples and thirteen input
identities; it independently enumerates the new combinations. Production CPU
source is unchanged outside temporary mutation checks.

These fixtures use a bare pending-handler RTE. The handler's software trace
service, preserved-trace repair with other CP vectors, user-tail trace bridges
and mixed-epoch trace provenance remain required separately. All earlier
internal-restoration, chained odd-PC, data/writeback/context-transfer and broader
model/reference/consolidation work remains open. The 480-case protocol inventory
continues to fail completion. No regression is retired, public API changed or
package released. Milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-pending-delivery-with-preserved-trace--2026-10-06).

Fresh full Release validation passes **5,077 tests**, with eleven optional skips
and zero failures. All nine pinned WinUAE selections match their exact counts
and CPU/adapter identities. Strict reporting passes **24,289,430 cases / 636
batches**; fresh SingleStepTests passes 312,500 cases / 125 files and Musashi
passes 536 programs with 88 explicit exclusions. All three mutations and
seventeen new report/identity controls detect their intended defects. The
complete 040 audit executes 52 tests (51 pass, inventory fails), with 9,320,768
passing cases, zero mismatches/unsupported execution and 480 untested
requirements. No new consumer/package validation is needed for this test-only
checkpoint; prior `.63` evidence remains scoped to its own binary.

The repaired result selector covers user M=0, ISP M=0 and MSP M=1. Restored user
M=1 and its exception-bank selection remain **untested** separately from the
initial user-tail M qualification and user-tail trace bridge. This required
stack-state combination remains part of milestone 6.

### Milestone 6 restored user M=1 — 2026-10-06

The required restored S=0,M=1 state now has four separate scalar/batch reports,
adding **1,517,952 phases**. The initial focused run passes all four without skips
or missing phases. User execution retains USP; completion trace, pending
CT/CU/CP49 delivery and following trace select MSP while preserving M. Independent
expectations compose MC68040UM 2.2.2.1 and 8.1 with the existing trace/repair rules.
The test expectation helper selects the synchronous exception bank from M rather
than requiring pre-exception S as well. No production CPU correction was needed.

Canonical cases cover all CCRs. Structural cases cover every validation-read
byte, CCR 0/31, twelve supervisor paths, both alignments/VBRs and all incoming/
restored 0/T1/T0. Forms are 0/2/3, repaired invalid 4/15, normal/CM and CT/CU/CP49.
The maintained `RteUserMaster` mutation scope targets selecting MSP during user
execution and clearing M during pending conversion or trace entry. The complete
040 gate now requires 56 tests, 50 reports, six fixed examples and thirteen input
identities, with an independent combination iterator.

This closes the restored user M=1 gap for these supervisor-tail repair programs;
initial user-tail M and user-tail trace bridges are distinct protocols. Other CP
vectors, software trace service, mixed-epoch provenance, internal restoration,
chained odd-PC, general data/writeback/context transfer and the broader model/
reference/consolidation requirements remain open. The required 480-case inventory
continues to fail completion. Milestone 6 remains **in progress** and
`roadmapComplete=false`. No package/API change or regression retirement is made.

See the [restored-user qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-restored-user-m1--2026-10-06).

Fresh full Release validation passes **5,081 tests**, with eleven optional skips
and zero failures. All nine pinned qualified WinUAE selections match their exact
counts and current CPU/adapter identities. Strict reporting passes **25,807,382
cases / 640 batches**; fresh SingleStepTests passes 312,500 cases / 125 files and
Musashi passes 536 programs with 88 explicit exclusions. All three new mutations
are detected, and exact production source bytes are restored. The first audit's
report selector collision is corrected with explicit user-tail prefixes; that
failed verifier attempt remains distinct from the new complete audit run.

The corrected complete 040 audit executes all 56 tests (55 pass, inventory
fails), checks all 50 reports and independently enumerates their combinations.
It records **10,838,720 passing phases**, zero mismatches/unsupported execution
and the retained **480 untested requirements**. The gate remains failed, with
no skips or omitted scope. No new consumer/package validation is needed for
this test-only checkpoint; prior `.63` evidence remains scoped to its own binary.

Seventeen fresh missing/short/foreign/redistributed-report and missing-fixture
identity controls reject their specific corruption diagnostics. Source/fixture/
binary identities still match after all acceptance checks. Milestone 6 remains
**in progress**; the restored user M=1 software slice is qualified, while the
full required restoration/reference/consolidation scope remains retained.

### Milestone 6 preserved-trace CP vectors 50–55 — 2026-10-06

Four separate scalar/batch reports add **4,866,048 phases** for repaired CP50–55
frames while preserving incoming trace. The initial focused run passes all four
without skips or missing phases. They cross restored user M=0/M=1, ISP/MSP,
incoming/restored 0/T1/T0 and all six original vectors. Canonical cases cover all
CCRs; structural cases reject every validation-read byte, use CCR 0/31 and all
twelve supervisor throwaway paths, with both alignments/VBRs.

The pending vector is selected before suspension; conflicting FPCR/FPSR/FPIAR
values must neither reselect it nor change during the integer repair protocol.
Saved format-3 SR/PC/EA, pending consumption, all stack banks, handler return and
following traced BRA are checked. The independent expectations compose
MC68040UM 8.3, 8.4.6.2/7 and 9.6 table 9-9; 9.6.2 confirms vector-55 post-instruction
support for register-to-memory unsupported data types. BSUN48 is excluded from
this CP selection; arithmetic and actual FPU event generation remain outside
scope.

The maintained `RteCpVectors` scope probes forced vector 49, extra automatic RTE
trace and an unconsumed context, requiring intended diagnostics in all four
reports. The complete 040 gate now requires 60 tests, 54 reports, six fixed
examples and thirteen input identities. CP49 retains its separate earlier
qualification. No production CPU correction was needed by the focused run.

This closes preserved-trace supervisor-tail repair for the remaining CP vectors.
Software trace service, user-tail trace bridges, mixed-epoch provenance, internal
restoration, chained odd-PC, general data/writeback/context transfer and the
broader model/reference/consolidation requirements remain open. The required
480-case inventory continues to fail completion. Milestone 6 remains **in progress**
and `roadmapComplete=false`; no API/package change or regression retirement is
included.

See the [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md) for identities, proofs and remaining scope.

Current acceptance passes **5,085 full Release CPU tests**, with eleven optional
skips and zero failures. All nine qualified WinUAE selections and identities
match; strict reporting checks **30,673,430 cases in 644 batches**. Fresh
SingleStepTests passes 312,500 cases / 125 files, and Musashi passes 536 programs
with 88 explicit exclusions. Both requested reference adapters execute without
skips. The complete 040 command executes 60 tests (59 passing, one required
inventory failure), with 15,704,768 passing phases, zero mismatches/unsupported
execution and the retained 480 untested requirements. Every independently
enumerated combination and all thirteen fixture/command identities match.

All three maintained CP-vector mutations are detected with their intended
diagnostics in all four reports. Seventeen fresh report/fixture controls reject
their specific corruptions. Restored production source bytes and current
acceptance identities match; this is a test-only checkpoint. Milestone 6 remains
**in progress**, with the full required scope retained above.

### Milestone 6 pending software trace-service programs — 2026-10-06

Four `SoftwareTraceService` reports add executed CU/CP integer trace-service
programs after validation fault, repair and pending-frame conversion. All four
restored banks, CU linear/taken-flow completion metadata and original CP49–55
vectors are explicit. The handler checks the stacked T1/T0 condition, advances
the CU PC when required, sets trace format/address and directly calls vector 9
through a real table read and stack/RTS sequence. The trace handler writes one
marker, preserves or clears saved trace and returns using RTE. A software call
must not masquerade as a hardware exception entry.

Canonical cases cover all CCRs and incoming/restored traces. Structural cases
use CCR 0/31 and incoming T1, retaining every validation-read byte, all twelve
supervisor paths, both alignments/VBRs and all restored traces/vectors/service
policies. Separate grouping avoids repeating the canonical incoming-trace
cross product at every structural read. The expected counts are 514,560 /
3,601,920 phases and 1,296 / 145,152 combinations per scalar/batch route.

Golden instruction words audit the program fixture separately. Maintained
mutation probes target the saved-SR bit test, format-2 return size and original
pending vector, requiring intended diagnostics in each of the four reports.
The complete 040 command now requires 65 tests, 58 reports, seven fixed examples
and fourteen input identities, including the new test-internal program helper.
The 480-case remaining-protocol inventory is retained unchanged in cardinality.

CU/CP completion metadata is synthetic input; actual FPU emulation/arithmetic
and hardware observations are not claimed. User-tail trace bridges, mixed-epoch
provenance, internal restoration, data/writeback/context transfer, other models'
restoration and broader independent qualification/consolidation remain required.
Milestone 6 remains **in progress**, `roadmapComplete=false`.

### Milestone 6 MOVES physical-fault discovery — 2026-10-08

No 030/040 hardware is available; the manual/software trace disagreement remains
unqualified. Continue independent software qualification of defined behavior.
The bounded 040 MOVES discovery uses fixed B/W/L D0/(A0) encodings, all eight
SFC/DFC choices and independently literal MC68040UM Table 3-2 attributes.
Scalar/batch routes, four address lanes, ISP/MSP, CCR 0/31 and rejection at each
operand byte execute **5,120 cases** including **1,536 passing fault-free controls**.
Of the **3,584 fault cases**, **3,136 mismatch**: writes produce format 0 and most
reads use supervisor-data attributes. Each route passes only FC 5/6 reads.
The discovery gate remains **failed**, not a completed fault/recovery promotion.

Two maintained snapshots reproduce the first frozen discovery reports exactly;
an independent verifier binds the full input/execution/architectural inventories.
Seven evidence corruptions reject, while a complete copied fixture retains all
3,136 mismatches. Existing MOVES semantics and invalid-operand tests pass separately.
Production CPU sources are unchanged; no wide full-suite/consumer rerun is claimed.
See [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-moves-physical-fault-discovery--2026-10-08).
Next work must qualify alternate-space operand provenance and store completion,
saved PC and actual writeback recovery; it must not retry a partially executed
instruction. EA pointer faults, alias/auto modes, trace, nested handlers, enabled
MMU/cache and physical timing remain unqualified. No package or retirement changes
occur. Milestone 6 remains **in progress**, `roadmapComplete=false`.

### Milestone 6 MOVES read-fault provenance — 2026-10-08

Fix the documented read attribute defect at the actual MOVES operand boundary.
Physical reads latch SFC in an internal fault record, converted by the frame
builder according to MC68040UM Table 3-2. Extension and EA pointer reads retain
ordinary attributes. No access is retried, no public API or enabled MMU behavior
changes, and saved-PC/auto-address effects preserve the existing execution policy.
The separate nine-form operand, all-CCR indirect, pointer and extension cohorts
execute **93,952 cases**, all passing. The unchanged CPU fails **33,600** exact
operand combinations; the 1,536 fault-free discovery controls remain identical.
Three private mutations qualify conversion, SFC selection and operand provenance
against exact affected memberships. Ordinary CI requires 46,976 cases per route.
The composed report-only CI fixture passes 86,506,698 cases / 743 batches;
missing/incomplete scalar read reports reject, with original evidence unchanged.
This report validation executes no instructions and is separate from CPU testing.

The original maintained discovery now has only **1,792 write-frame mismatches**,
so its gate stays **failed**. Store completion/saved PC, actual handler recovery,
trace, additional EA/alias/register combinations and physical pipeline/timing
remain unqualified. The full 218-input CPU audit passes **5,318 tests / 53
unavailable / zero failures**, with an independently verified **86,699,178-case /
943-report / ten-native-preset** inventory. All retained reports match the earlier
accepted full audit; the current ordinary gate passes 86,506,698 cases / 743 batches.
Local `.75` package consumers pass the clean production build, host, disk, engine
and two native Workbench 3.1 floppy replays. Completed linkage binds all 37 CPU
inputs across focused/full execution and the package; immutable earlier pending
records are preserved. The local package is not published and the private C023 CPU is
not imported. See [the read-fault record](COPPER68K_REFERENCE_QUALIFICATION.md#040-moves-read-fault-provenance--2026-10-08).
No regression is retired. Milestone 6 remains **in progress**, `roadmapComplete=false`.



On 2026-10-07 the executed 040 MOVEM read-recovery gate passes 229,968 complete
fault/RTE/resumption/sentinel programs across scalar and batch execution. It
covers every legal memory-source encoding, all legal full-format structures in
selected index variants, transfer-byte faults and canonical stack/CCR/trace
profiles, using the D0/D1/A0/A1 list to expose base/index aliases. Six evidence
integrity controls reject their intended corruptions; dropping saved-EA metadata
in an isolated shared fallback causes 62 expected frame-EA mismatches. The prior
read-entry gate passes again with current test sources. This test-only extension
does not close the broad remaining gate, other models or consolidation and does
not authorize a release. See [executed MOVEM recovery](COPPER68K_REFERENCE_QUALIFICATION.md#executed-040-movem-read-recovery--2026-10-07).

The same gate subsequently adds pre-EA full-format pointer faults: 31,504 new
whole programs verify CM-clear format-7 entry, no preceding operand transfers,
normal RTE restart, pointer resolution and restored trace behavior. All 54 legal
indirect structures and canonical stack/CCR/trace profiles are covered in the
selected variants. The combined command requires 261,472 passing programs,
eight reports and nine executions. Six integrity controls pass; premature CM
in an isolated source copy makes all sixteen fixed pointer programs fail.
No production or package change is needed. The full milestone remains open;
see [pre-EA pointer faults](COPPER68K_REFERENCE_QUALIFICATION.md#pre-ea-movem-indirect-pointer-faults--2026-10-07).

EXTB consolidation on 2026-10-07 retires the pure semantic
`M68020ExecutesM68020OnlyExtbLong` fact after the existing exact synthetic case
and original both detect a targeted zero-extension mutation. A maintained command
restores only the pinned historical fact in isolated source copies and verifies
the same proof after retirement: 140,992 clean scenarios across eight profiles,
6,144 intended EXTB mismatches across six implementing profiles, complete weighted
keys and exact diagnostics. Six corrupted-evidence controls are rejected. All
462 retained 68020 class tests pass; timing and specialized regressions remain.
The ordinary scenario inventory and production package are unchanged. See the
[EXTB retirement proof](COPPER68K_REFERENCE_QUALIFICATION.md#extb-semantic-regression-consolidation--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The three maintained software-trace mutations are detected in each complete
scalar/batch canonical/chained report with their intended semantic diagnostic.
Production bytes are restored exactly and the clean implementation rebuilds
without warnings/errors. Fresh complete 040 acceptance executes 65 tests (64
passing, one required inventory failure), checks 58 reports/seven fixed examples
and all fourteen fixture/command identities. It verifies 23,937,728 passing phases,
zero mismatches/unsupported execution and exactly 480 untested requirements.
Full Release CPU acceptance passes 5,090 tests with eleven optional skips and
zero failures. All nine qualified WinUAE selections and identities match; strict
reporting checks 38,906,390 cases in 648 batches. Fresh requested SingleStepTests
and Musashi adapters execute without skips, passing 312,500 cases / 125 files
and 536 programs / 88 explicit exclusions respectively. All seventeen fresh
report/fixture integrity controls reject their specific corruptions. Current
production source, fixture and assembly identities still match. This test-only
checkpoint is qualified; milestone 6 remains **in progress** with its required
scope retained. See the qualification record for the acceptance build identity
and remaining scope.

### Milestone 6 user-tail bridge preserves trace — 2026-10-06

The next four scalar/batch groups retain incoming trace through the executed
seven-store user-tail repair. The access return adds S while retaining M/T1/T0;
the fresh supervisor throwaway restores the original user SR and selects USP.
The original instruction then completes with its incoming trace condition,
independently of the repaired SR. Pending CT/CU/CP49–55 conversion suppresses
an extra automatic RTE trace. Returns and the following BRA/MOVEM verify saved
SR/PC/address, all stack banks and continuation lifetime without replaying
consumed throwaways or their discarded PCs.

Canonical groups each require 873,984 phases / 2,304 combinations across all
CCRs and incoming 0/T1/T0. Structural groups each require 3,502,080 phases /
145,920 combinations with CCR 0/31, incoming T1, every validation-read byte,
both alignments/VBRs and all initial/middle paths. Both original user M states
and all restored user/user-M/ISP/MSP states are retained. These add 8,752,128
phases; the complete 040 command must execute 69 tests, 62 reports, seven fixed
examples and fourteen input identities, still retaining its 480-case inventory.

Initial canonical and structural execution passes all four reports without a
CPU production change. All three maintained mutations now detect their intended
diagnostic in every complete report; production bytes are restored exactly and
the implementation rebuilds without warnings/errors. Complete 040 acceptance
reports 32,689,856 passing phases, zero mismatches/unsupported execution and
480 untested requirements. All 69 tests execute without skips; its sole failure
is the retained inventory. Fresh full Release CPU acceptance passes 5,094 tests
with eleven optional skips and zero failures; all nine qualified WinUAE
selections match the frozen source/binary identities. Strict ordinary reporting
verifies 47,658,518 cases in 652 batches. Fresh pinned SingleStepTests and Musashi
audits pass 312,500 cases / 125 files and 536 programs / 88 explicit exclusions;
both adapter tests execute without skips. All current identities still match.
All seventeen fresh report/fixture integrity controls reject their specific
corruptions; all current identities still match afterward. This test-only
checkpoint is qualified. Mixed-epoch trace provenance,
user-tail software trace-service programs, internal restoration and all other
retained model/reference/consolidation requirements remain required.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

### Milestone 6 user-tail software trace service — 2026-10-06

Four new `UserTailSoftwareTraceService` scalar/batch groups compose the existing
seven-store user-tail repair bridge with the shared executed integer trace
service. CU linear/flow cases and CP49–55 inspect repaired saved T1/T0, adjust
the supplied CU completion PC, convert the pending frame, fetch vector 9 and
call the trace handler through a temporary stack/RTS target. The handler writes
its marker once and either retains or clears saved trace before a real RTE.
The following BRA checks resumed hardware tracing independently of the software
call; hardware exception sequence and provenance must remain unchanged during
the integer software service. No FPU opcode or arithmetic is executed.

Canonical groups each require 1,360,896 phases / 2,592 combinations; structural
groups each require 6,350,848 phases / 193,536 combinations. Canonical cases
cross all CCRs and incoming 0/T1/T0; structural cases use CCR 0/31, incoming T1,
all fourteen validation-read bytes, both alignments/VBRs and all initial/middle
paths. Both original user M values, all restored user/user-M/ISP/MSP states,
restored trace conditions and trace-service return choices are covered.

These four groups add 15,423,488 phases. The maintained complete 040 command
must execute 73 tests, 66 reports, seven fixed examples and fourteen input
identities, with 48,113,344 passing phases plus the retained 480-case inventory.
Independent PowerShell command enumeration matches every new combination count
and preserves the earlier user-tail bridge counts. Initial canonical execution
passes two tests without a production CPU change. Fresh report-producing
canonical and structural runs pass all four groups, 15,423,488 total phases,
with no skips, mismatches, unsupported execution or untested phases. These
initial-build focused results are not final restored-build acceptance. The maintained
`UserRteSoftwareTrace` mutation scope requires inverted saved-SR BTST, a short
format-2 return and original CP-vector loss to produce their intended diagnostic
in all four complete reports, then restores production bytes exactly. The
three-probe command detects every intended defect in all four complete reports
and restores production source bytes exactly. Fresh full Release acceptance
passes 5,098 tests with eleven optional skips and zero failures; all nine
qualified WinUAE presets match exact selections and assembly identities.
Strict reporting verifies 63,082,006 logical cases / 656 batches. Fresh
SingleStepTests passes 312,500 cases / 125 files; Musashi passes 536 programs
with 88 explicit exclusions. The complete 040 command executes all 73 tests,
passes 72 and fails only its retained 480-case inventory, with 48,113,344 passing
phases and zero mismatches or unsupported execution. Seventeen integrity
controls reject their specific corruption. Current source, fixture/input and
assembly identities still match after all checks.

This test-only checkpoint is qualified. Mixed-epoch provenance,
internal restoration, all earlier model/reference/consolidation requirements
and the complete roadmap remain required; milestone 6 stays **in progress**.

### Milestone 6 successful mixed-epoch throwaway chains — focused qualification

Four `SyntheticM68040ThrowawayTests.MixedEpochTrace*` groups independently vary
the instruction's incoming trace, the first and (where present) second
throwaway trace, and the final restored trace. Each uses states 0/T1/T0.
Short formats 0/2/3 and format-7 normal/CM/CT/CU/CP49–55 cover every initial
ISP/MSP, intermediate/tail user/user-M/ISP/MSP and restored bank. Canonical
one-throwaway cases use every CCR; structural cases use CCR 0/31, one/two
throwaways, both alignments and VBRs, including aliases of USP with different
user M values. Scalar and one-instruction batch routes check every phase,
registers/stack banks, guarded memory and exception provenance. The CM program
returns a MOVEM trace and executes BRA, separating the incoming RTE trace from
restored-state instruction tracing and saved-EA lifetime. Pending vectors retain
the original event despite conflicting FPU registers; no FPU opcode is executed.

After mutation restoration, fresh focused Release execution passes ten tests
without skips: four complete reports plus six independent SR encoding examples.
Canonical reports each pass 1,152,000 phases / 12,096 combinations; structural
reports each pass 3,744,000 phases / 628,992 combinations. All 9,792,000 phases
pass without mismatches, unsupported execution or untested phases. Independent
command enumeration checks every combination and weight. All four complete
mutation probes detect their intended semantic defect, including second-throwaway
SR provenance in both structural reports. Exact production bytes are restored.
The original two throwaway reports retain their identifiers/cardinalities and
passed the initial focused selection.

The complete 040 command now requires 83 tests, 70 reports, thirteen fixed
examples and 57,905,824 phases including the retained 480-case inventory.
A fresh inventory selection still fails its one executed test with exactly
480 untested cases / fifteen combinations and no skips. Expanded ordinary
coverage requires 72,874,006 cases / 660 batches. These are complete-selection
requirements; prior broader qualification is retained as separate evidence,
not represented as a freshly rerun complete suite. Frozen source/binary
identities and scope are recorded in `COPPER68K_REFERENCE_QUALIFICATION.md`.

Expectations compose MC68040UM 8.2.6, 8.3, 8.4.2 and 8.4.6.7. Successful chains
do not qualify mixed epochs during validation faults, repair/retry, internal
restoration or chained odd-PC saved-SR provenance. All earlier data/restart,
context-transfer, model/reference/consolidation requirements remain required.
Milestone 6 stays **in progress**, with `roadmapComplete=false`. This is a
test-only checkpoint; no package release or old-test retirement is included.

### Milestone 6 mixed-epoch validation fault capture — qualified checkpoint

Four `MixedEpochFaultCapture*` groups reuse the real physical rejection bus,
frame expectations and a shared test-only stack/status/batch fixture. Initial
instruction, first and second throwaway trace states are independent. Canonical
one-throwaway cases cross every final trace and CCR while rejecting the PC read;
structural one/two-throwaway cases hold the uncommitted final SR at T1 and reject
every byte of every validation transfer at both alignments/VBRs. Every stack
bank, user M value and alias participates. Short/invalid formats and all defined
format-7 continuations, including CP49–55, preserve original pending vectors
and conflicting FPU registers. Fault entry, bare handler return and user-tail
privilege failure check the last committed SR, source-frame preservation, exact
PC, stack banks, rejected access/width and exception sequence. These cases do
not execute supervisor retry or infer a private original-instruction trace latch.

Canonical scalar/batch reports each contain 276,480 phases / 3,456 combinations;
structural reports each contain 3,556,800 phases / 711,360 combinations. All
7,666,560 initial phases pass after correcting an aliased-USP initialization
error in the test fixture; the earlier failed structural reports are not credited.
Independent maintained-command enumeration matches the counts. Fourteen retained
tests, including six fixed SR encodings, passed before that fixture correction;
the correction only affects new mixed-epoch cases. Three complete probes qualify
lost live T1, premature CCR installation and the aliased-USP fixture error, with
the intended diagnostics and exact production source byte restoration. Fresh
restored Release build succeeds with zero warnings/errors; focused execution
passes ten tests with zero skips, comprising four complete new reports and six
fixed SR encodings. All 7,666,560 phases pass with zero mismatches, unsupported
execution or untested phases. Every combination and weight matches independent
literal enumeration. The fresh inventory selection executes one expected
failing test with zero skips and retains 480 untested / fifteen combinations.
Frozen identities and exact scope are recorded in the qualification document;
previous broad CPU/reference results remain separate historical evidence.
Expanded complete 040 requirements are 87 tests / 74 reports / thirteen fixed
examples and 65,572,384 phases including the retained 480-case inventory.
Expanded ordinary requirements are 80,540,566 phases / 664 batches. These are
requirements, not claims of fresh complete-suite execution.

Expectations compose MC68040UM 8.1, 8.2.5/6, 8.3, 8.4.2 and 8.4.6.7. Trace
suspension/resumption across differing epochs, executed repair/retry, internal
restoration, all earlier data/context/model/reference and consolidation scope
remain required. Milestone 6 stays in progress; no CPU defect or release is claimed.

### Milestone 6 original-trace retry discovery — unresolved

A dedicated discovery command now distinguishes original incoming tracing from
the trace state installed by consumed throwaways. It executes physical PC-read
faults, three repair stores, access-handler return, retry and following
instruction. One/two-throwaway paths cover supervisor tails, all intermediate
stack aliases and all restored banks; format0/normal/CM, CCR 0/31 and independent
initial/first/second/final trace values are explicit.

Both scalar/batch selections execute without skips and each report 258,336
phases / 16,848 independently enumerated combinations: 220,896 passing,
14,976 mismatching, zero unsupported and 22,464 untested phases. Current retry
tracing follows the last committed state, disagreeing in both directions with
the original-instruction deferral hypothesis composed from MC68040UM 8.2.6.
Equal trace classifications pass; failure is localized to the retry boundary.
The composition is still independently unqualified. No architectural CPU defect
or physical trace result is claimed, and no trace latch/CT workaround is added.

`scripts/test-copper68k-040-mixed-retry-discovery.ps1` records source/binary
identities, verifies complete selections and every combination/phase weight,
and correctly fails this current result. Five integrity controls reject missing
identities, a missing fixture identity, an empty selection, a missing report and
a missing combination. Three retained boundary/encoding tests pass. This is
discovery evidence rather than promoted ordinary-CI coverage; complete 040 and
ordinary counts from the preceding checkpoint remain unchanged. The existing
480-case inventory and every unresolved data/context/model/reference and
consolidation requirement remain required. Milestone 6 stays **in progress**.

### Milestone 6 chained odd-PC short-frame software handoff

Four ordinary scalar/batch matrices cover one/two throwaways before format0/2/3
odd-PC returns. Independent trace epochs, all stack/user-M aliases, every CCR
in canonical groups and structural CCR boundaries/alignments/VBRs/high PCs
produce 1,244,160 cases: 82,944 / 2,592 combinations per canonical route and
539,136 / 269,568 combinations per structural route.

An explicit native reference command executes untouched generated WinUAE 040
RTE and cputest SR helpers from pin `5d22d33632646efc3f747f03e82d28353e52722e`.
The observed secondary SR comes from the last committed throwaway; restored SR
selects live flags and stacks. Architectural format-2 entry and the addendum's
traced-user saved-S correction are composed independently of CPU helpers.
Two literal native controls, three canonical mutation probes and strict
source/binary/row/combination checks protect this scoped software qualification.
The harness stops at the address-error callback: full external exception entry,
validation-fault behavior and hardware/timing are not qualified by it.

Expanded ordinary requirements are 81,784,726 cases / 668 batches. The complete
040 selection now requires 91 tests / 78 reports / thirteen fixed examples,
66,816,544 phases including the unchanged 480-case remaining-protocol inventory,
and fifteen fixture/command input identities. These expanded requirements do
not relabel older broad suite results as current execution.

The inventory retains format7 chained odd-PC normal/CM/pending/foreign-context
requirements, mixed-epoch repair/retry and original-instruction trace deferral,
internal restoration, data/writeback restart, broader model reference audits
and consolidation. Milestone 6 stays **in progress**, `roadmapComplete=false`.
No production CPU fix, package publication or regression retirement is included.

### Milestone 6 chained odd-PC normal/CM access frames

Four additional ordinary matrices extend one/two-throwaway odd returns to
normal and CM format7 tails: canonical scalar/batch each require 55,296 cases /
1,728 combinations; structural scalar/batch each require 359,424 / 179,712.
All banks/user-M aliases, independent trace epochs, canonical CCRs, structural
CCR boundaries/alignments/VBRs/low-high odd PCs remain covered. The added
829,440 cases check 60-byte consumption, SSW/CM-EA validation ordering, guarded
writebacks, last committed saved-SR provenance and no MOVEM continuation after
odd-PC delivery.

The native handoff command gains an explicit `-AccessFrames` profile. It observes
only generated WinUAE SR/stack/header behavior and composes address-error entry
with documented rules. The generated test RTE skips SSW/EA continuation work;
therefore it rejects CT/CU/CP and undefined forms, and never certifies full
format7 restoration or hardware behavior. Three literal native controls and
five complete canonical mutation probes protect the qualified and synthetic
portions separately. Profile identity is mandatory on report revalidation.

Ordinary coverage now requires 82,614,166 cases / 672 batches. The complete 040
gate requires 95 tests / 82 reports / thirteen fixed examples and 67,645,984
phases including the unchanged 480-case inventory; fifteen fixture/command
identities remain mandatory. Expanded counts are requirements, not a relabeling
of earlier broad executions. CM continuation lifetime across odd-return fault/
software repair, pending/foreign-context odd-return chains,
original-trace fault/retry, internal/data/writeback restoration, broader model
reference audits and consolidation remain required. Milestone 6 stays **in
progress**, `roadmapComplete=false`; no CPU fix, release or regression retirement.

### Milestone 6 CM continuation lifetime software discovery

The previous documentary MMU/non-MMU difference now has executed evidence.
A dedicated gated scalar/batch matrix runs normal/CM format7 returns, even/odd
PCs, actual SR/PC repair stores, handler RTE, PC-relative MOVEM.L and a following
sentinel. Each route covers 4,096 scenarios / 18,432 phases / 576 combinations:
every CCR, entry ISP/MSP, all restored banks including user-M, alignments 0/1
and VBR 0/10000. Full architectural/guarded-memory checks accompany snapshots.

`scripts/test-copper68k-040-cm-lifetime-discovery.ps1` executes untouched full
generated WinUAE MMU RTE/MOVE/MOVEM/NOP, the original CM helper and SR helpers
from pin `5d22d33632646efc3f747f03e82d28353e52722e`. Four literal controls
distinguish saved and recomputed addresses. The adapter composes address-error
entry, so this is not a complete external exception or hardware oracle.
Per route: 16,384 passing, 1,024 mismatching, zero unsupported and 1,024 untested
phases. Every mismatch is the odd-PC CM MOVEM's D0: saved 89ABCDEF versus
recomputed DEADBEEF; its following sentinel remains untested. Earlier repair
phases, normal controls and even-PC CM agree. This localizes a software-reference
discrepancy without proving which continuation lifetime hardware requires.

The audit records pinned source/input/generated/binary/evidence identities,
independently enumerates keys/weights/rows, executes frozen native replay during
report revalidation and correctly fails the discrepancy. Required coverage is
not reduced: ordinary/complete-040 counts remain unchanged, the 480-case
inventory remains explicitly untested, and broader reference/consolidation work
remains required. This discovery is excluded from ordinary synthetic CI and
the complete-040 promoted selection. Milestone 6 stays **in progress**,
`roadmapComplete=false`; no CPU fix, package publication or regression retirement.

### Milestone 6 executed MMU exception entry and CM lifetime

The CM command adds an explicit `-MmuExceptionEntry` profile. It executes
untouched pinned WinUAE native SR helpers, exception3 callback/dispatch,
`Exception_mmu`, frame builder and trace clearing. Original `fill_prefetch`
executes its compatibility-disabled return; physical transport does not enable
translation, caches or a run-loop/IRQ/trace/hardware oracle. Normal/CM, even/odd,
CCR/bank/alignment/VBR selections remain complete at their stated scope.

Raw frames are captured before either repair store. Per route, all 2,048 odd
frames disagree only in stacked SR: the MMU path stacks the restored image;
Copper68k stacks the incoming secondary image. SP, stacked PC, format and fault
address agree. The MMU path still retains CM through exception entry and repair,
and the 18,432 instruction-phase classifications per route remain 16,384 passing,
1,024 mismatching, zero unsupported and 1,024 untested. These raw-frame
comparisons are reported separately from logical instruction counts.

The default composed profile is rerun with unchanged classifications. Both
requested audits fail, and report revalidation executes the frozen native code.
The new profile is explicit in source/binary/evidence identities; eight dedicated
frame/profile integrity controls and thirteen retained default controls reject
invalid inputs. No software path is selected as hardware truth by majority or
compatibility. Full CM lifetime, pending/foreign contexts and every broader
reference/consolidation requirement remain required. Milestone 6 stays **in
progress**; ordinary and complete-040 coverage requirements are unchanged.

### Milestone 6 68060 PCR fields and demonstrated consolidation

Four new ordinary matrices add 305,152 cases: 102,400 defined-field transfers,
172,032 identification/revision writes, 6,144 explicitly nonarchitectural
reserved-write policy cases, and 24,576 actual write/reset/read phases. The
selected first-revision PCR and writable bit positions come from MC68060UM
3.2.2.5 / figure 3-5. Registers, stacks, all canonical CCRs, privilege, exact PC,
readback/sentinels, untouched controls and memory remain checked. Read-only
bit probes and reserved-policy samples use CCR 0/31 as a separate group.

The shared MOVEC fixture now preserves PCR during other control transfers. All
four new tests and 46 retained tests pass without skips; retained shared-fixture
coverage adds 1,236,864 passing cases in fifteen reporting batches. The new
audit command checks identities, precise architectural keys/weights and exactly
four executed tests. Eight negative report controls reject incomplete inputs.

Three maintained PCR mutations fail both the original identification/reset fact
and its complete replacements before retirement, and still fail afterward.
Only that demonstrated duplicate is removed; its all-ones write remains in an
explicit repository-policy batch. The original manual requires reserved bits
zero; maskset errata give bit 5 a physical workaround role. This policy batch
does not establish maskset behavior or debug/FPU/superscalar timing correctness.

The pinned WinUAE bit-6 EDEBUG/software disagreement and ordinary STOP S-clear
rule remain open. Ordinary coverage now requires 82,919,318 cases / 676 reports;
complete-040 requirements and the 480-case untested inventory stay intact. No
new broad-suite result, production CPU fix, consumer replay or package release
is claimed. Full CM lifetime, advanced restoration, broader reference audits
and remaining consolidation are still required; milestone 6 stays **in progress**.

### Milestone 6 68060 ordinary STOP discovery

Two gated discovery matrices cover every defined target SR image and all
canonical input/target CCR combinations, incoming user/supervisor stacks,
initial/new M and T1, target IPLs and inert stopped attempts. Incoming IPL is
fixed at 7. Scalar and batch each execute 184,320 logical cases / 720
combinations: 143,360 passing, 40,960 mismatching, zero unsupported/untested.
Both named tests execute without skips. All discrepancies are supervisor STOP
with a new S-clear image.

Untouched pinned WinUAE generated STOP and native SR/stop helpers reject these
operands before SR transfer. That source calls the behavior undocumented;
MC68060UM explicitly states it for LPSTOP but does not settle ordinary STOP.
The observer executes the native privilege callback, composes incoming trace
and normalizes stopped PC; full external exception/frame/IRQ/run-loop/hardware
qualification is not claimed. No production CPU behavior is changed.

The command validates exact identities, independently generated keys/weights,
fixture ordering, SR selection and canonical initial registers/stacks, then
replays frozen native code. Both fresh execution and report revalidation fail
with the same 81,920 combined software-reference mismatches. Thirteen integrity
controls reject malformed/missing/empty selections. Seventy-five retained tests
pass without skips, covering 331,582 cases / 25 reports and fifty fixed checks.
The corrected fixture preserves the existing batch idle-step convention;
preliminary fixture failures are excluded from final evidence.

This remains an explicitly unqualified discovery outside ordinary CI and the
promoted complete-040 selection. Their counts and the 480-case required untested
inventory remain unchanged. Ordinary STOP's S-clear architectural rule, full
CM lifetime, advanced restoration, PCR disagreement, broader independent model
qualification and consolidation all remain required. Milestone 6 stays **in
progress**, `roadmapComplete=false`; no regression retirement or release.

### Milestone 6 68010 MOVEC qualification and consolidation

Four ordinary matrices add 2,271,744 cases: every legal control/source/readback
register pair, complete function-code masks/initial images, raw full-width
USP/VBR images, every undefined selector and all legal user forms. All canonical
CCRs are covered; undefined selectors use CCR 0/31. Incoming trace is clear and
IPL is 7. Expectations use MC68000UM figure 2-3 / 6.3.6/7 and M68000PM MOVEC
6-22/23 independently of production helpers. All registers, stack banks, SR,
exact PC, controls and memory are compared, including following sentinels and
dependent untested phases after a failed prerequisite.

The shared MOVEC fixture gains distinct readback registers, USP handling and
independent control/vector initialization. It now preserves SFC/DFC/VBR during
other transfers and explicitly expects the 010 stack model. Every new case and
independently enumerated combination passes the identity/selection/weight gate;
frozen report validation also passes. Ten malformed-report controls are rejected.
Sixty-nine retained tests pass without skips, comprising 1,633,168 cases /
thirty reports and 39 fixed examples.

Six maintained mutations detect masks, VBR width/bit 10, saved A7-bank state
and wrong stack-model configuration. Each fails both the original and complete
replacement before retirement, and the replacement afterward. Only three
documented duplicate MOVEC methods (five xUnit cases) are retired. Factory,
privilege, exception-frame, interrupt, restart, prefetch and other specialized
regressions remain. Mutation sources are restored byte-for-byte; no CPU behavior,
timing policy, public API or package release changes.

Ordinary coverage requirements become 85,191,062 cases / 680 reports; this is
not a new broad-suite execution claim. Complete-040 requirements stay intact.
Its remaining inventory now accurately names both composed and executed MMU
entry discoveries, and still fails with 480 untested cases / fifteen combinations.
Physical function-code spaces, bus ordering and advanced 010 restoration are
not qualified by MOVEC register tests. All remaining CM/restoration, STOP/PCR,
independent-model and consolidation requirements remain required. Milestone 6
stays **in progress**, `roadmapComplete=false`.

### Milestone 6 rejected 68010 RTE format discovery

Two gated scalar/batch audits execute 573,440 cases / 125,440 combinations each.
Every invalid format word is enumerated with CCR 0/31 and both privilege states;
a separate canonical-offset matrix covers all CCRs, incoming trace, both privilege
states, eight stacked SR images and four even/odd/high-address PC images.

Untouched generic and compatible WinUAE 68010 RTE functions at the existing
modern pin both change N/Z/V before format rejection. Copper68k preserves these
bits. Each route records 336,896 passing and 236,544 mismatching cases, with zero
unsupported or untested cases. Every other register/defined bit, stack bank,
original frame byte and exception-header field is checked even on CCR mismatch;
both named tests fail without skips. The command independently verifies all keys,
weights, rows and classifications and executes frozen observer replay. Separate
surrounding-state outcomes must pass on every row, even on CCR disagreement. Native
exception entry is composed, and the manual does not settle the failure-path
flags; this is software discovery, not an architectural CPU-fix gate.

Fresh execution and frozen revalidation explicitly fail the 473,088 combined
CCR discrepancies. Twenty-three integrity controls reject incomplete or altered
fixtures, identities, selections, classifications, initial stack banks and hidden
surrounding-state failures. Original evidence and production source remain intact.

The ordinary 68010 retained selection passes 25 tests without skips, comprising
2,404,896 cases / twelve reports plus thirteen fixed examples. Production CPU
source, timing policy, public API, ordinary CI requirements, complete-040 counts
and packages remain unchanged. No regression is retired. Rejected RTE CCR
architecture, long/RMW/foreign format8 continuation, full exception/trace entry,
and all other reference/consolidation requirements remain required. Milestone 6
stays **in progress**, `roadmapComplete=false`.

### 68010 long MOVE/MOVEA continuation — 2026-10-06

Generated private format-8 images now continue long transfers as two pending
word cycles. A completed source read is not repeated; postincrement commits
after both source words, and descending destination predecrement commits after
both writes. Input/output buffers and independent RR choices apply per word.
The frame retains accumulated input, full output, original operand address,
extension PC and completed prefetch. Redirecting one pending word leaves the
other word's original address intact; this is an explicit private convention.
Copied/nested images need no side table. Normal successful transfer ordering,
public API and existing timing policy remain unchanged.

The two bounded failures reproduce before the fix. Five ordinary groups add
344,192 scenarios / 1,516 combinations: source 165,888; destination 172,032;
copied/nested/alias/prefetch/trace 5,120; user A7 512; malformed images 640.
All seven named tests pass without skips. Source and destination fixtures are
independent of production decoding, including extension words, 24-bit physical
addresses, final flags, saved values, untouched registers and surrounding memory.
Each canonical scenario checks the second fault, completed transfer and sentinel.
The dedicated command independently checks identities, exact selections, keys
and weights; fresh and frozen validations pass. Fourteen integrity controls
reject changed identities, empty/wrong selections and redistributed coverage.

The ordinary semantic gate now requires **85,535,254 scenarios / 685 reports**.
The corrected full run validates these counts; its scope precedes the later EXG addition.
The retained 68010 selection passes 21 tests without skips; seven production
mutations detect the targeted continuation defects, including all 18,432 cases
affected by an omitted destination-register write. A maintained mutation command
reproduces the proofs with isolated builds, exact execution identities and
independent combination checks. Pinned SingleStepTests and
Musashi semantic audits pass. The isolated unpublished `.64` consumer passes
its Release build, host/disk/engine tests and all three native replays. The first
full CPU run failed only an obsolete structural-image expectation; that test
now checks explicit private-image examples and passes its retained selection.
The corrected full rerun passes 5,129 tests, with zero failures and nineteen
unavailable tests, including nine pinned WinUAE selections. See the reference document
for identities, exclusions and preserved failed evidence. No regression is
retired or public package published. Long non-MOVE, foreign hardware images, RMW and external bus errors
remain required, along with all other milestone-6 reference/consolidation gaps.
This private continuation is not hardware-internal or physical timing
qualification. Milestone 6 stays **in progress**, `roadmapComplete=false`.

### Milestone 6 EXG wide-register qualification and consolidation — 2026-10-06

The original shared register-transfer matrix used small An values and could
not detect word truncation. A new ordinary EXG matrix adds 88,704 scenarios /
10,584 independently checked combinations across eight profiles. It separates
all DD/AA/AD register bindings from eight value boundaries/all CCRs, including
self aliases, A7 and every supported active stack bank. Full register values,
unchanged flags, exact PC, untouched registers/banks/memory, absence of operand
bus transfers and a following NOP are checked. Three fixed examples validate
fixture encoding against M68000PM EXG 4-105.

Fresh and frozen qualification pass all eleven named tests without skips.
Word-truncation and lost-latched-input mutations fail both the old fact and
complete 68000 replacement before retirement, and the replacement afterward.
Only `ExgAddressRegistersSwapsFullLongValues` is retired, mapped to
`68000/EXG.L/AA/bank=ISP/r6->r2/boundaries/pair=0/op=C54E/ccr=00`.
The replacement uses the same opcode/values and stronger preservation checks.
Twelve integrity controls reject their intended errors. CPU source restores
byte-for-byte, and normal CPU/test assemblies remain unchanged.

Ordinary requirements become 85,623,958 scenarios / 693 reports. The preceding
full run passes 5,129 tests / zero failures / nineteen unavailable tests and
validates its 85,535,254 scenarios / 685 reports, including nine pinned WinUAE
selections. Later EXG tests execute separately in isolated outputs. Combined
report validation is not a fresh broad run of the expanded test source.
See the [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#exg-wide-register-qualification-and-consolidation--2026-10-06)
for scoped identities, exact retirement proof and rejected preliminary evidence.
Specialized hardware/timing/prefetch/bus/JIT/native regressions remain. No CPU
behavior, timing policy, public API or package changes are made in this slice.
The complete-040 inventory still retains 480 untested cases, and all broader
restoration, reference disagreements and consolidation requirements remain.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

### Milestone 6 supplied 040 write-back handler — 2026-10-06

Four ordinary matrices add 147,456 whole-program scenarios / 39,168 combinations
for scalar and one-instruction batch routes. The handler executes register saves,
validity/size/DFC decoding, WB1 memory-lane normalization, ordered WB1→WB2→WB3
MOVES stores, valid-bit clearing, register/DFC restoration and RTE. Canonical
cases cover every slot, B/W/L size, address lane, four values, all CCRs and
handler/restored banks. Structural cases cover all valid masks, uniform/mixed
sizes, distinct/equal/overlapping operands, frame alignment and restored trace.
Every instruction checks state and memory; final operands/order are independently
chosen. Header examples distinguish normal writes, supplied write-page-fault
images and reads according to MC68040UM table 8-6.

Fresh and frozen qualification pass fifteen exact executions without skips,
including nine fixed examples and two bounded witnesses. Lost WB1 rotation and
reversed stage order are detected by independently chosen results/order; baseline
and mutated execution identities, exact failing methods/reasons and restoration
are checked. Eight positive and seven mutation integrity controls reject their
intended errors; an isolated restoration-guard check preserves concurrent edits.
No CPU source or normal assemblies change, and no regression is retired.

Ordinary requirements become 85,771,414 scenarios / 697 reports. Complete-040
requirements become 67,793,440 cases / 86 reports and 24 fixed controls/witnesses;
its discovery selection lists exactly 110 executions. These are expanded
requirements, not a passing fresh complete-040 run. Fresh remaining-inventory
execution fails exactly 480 untested cases / fifteen combinations. Nested WB2/3
faults, push/MOVE16 line cleanup and actual data-fault construction remain required,
along with all other advanced restoration/reference/consolidation gaps. Supplied
frames do not qualify enabled-MMU operation, physical function-code spaces,
hardware or timing. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-supplied-write-back-handler-qualification--2026-10-06).
Milestone 6 stays **in progress**, `roadmapComplete=false`; no package publication.

### Milestone 6 actual 040 MOVE write faults — 2026-10-06

The production 040 path now constructs a documented format-7 pending-writeback
frame for ordinary MOVE destination writes rejected by the host physical map,
with translation disabled. It preserves consumed source/extensions, latched
store address/data, completed destination base/flags and following PC. The
integer handler completes WB1 and returns without replaying the MOVE. Retained
descending long transfers describe the original operand in the fault while
preserving successful transfer order/widths and existing timing policy. An
instruction/exception boundary guard prevents a rejected trace-frame store from
being mistaken for another MOVE operand. No public API changes are made.

Six ordinary scalar/batch matrices pass **61,644 whole-program scenarios /
43,788 combinations**: every legal memory-destination MOVE opcode, independent
lane/size/value/CCR/stack boundaries, and full-index structures/aliases/trace.
All twelve exact executions pass without skips, including six bounded witnesses.
Every handler instruction, defined frame field/data lane, preserved state,
ordered store, RTE and following instruction is checked. Seven maintained CPU
mutations detect operand width, predecrement, lanes, data, CT, N and exception
boundary defects. Baseline plus mutants execute 48 tests; source restores
byte-for-byte. Eight report and seven mutation integrity controls reject their
intended errors, and the isolated restoration guard preserves concurrent edits.

Ordinary requirements become **85,834,306 scenarios / 703 reports**. Complete-040
requirements become **67,856,332 scenarios / 92 reports and 30 fixed witnesses**,
with exactly 122 discovered executions. Static requirements/key validation is
separate from a passing complete audit. Its remaining inventory still fails
exactly **480 untested cases / fifteen combinations**, retaining all five
protocol categories. Read/other integer/MOVEM/MOVE16 fault construction,
trace-entry recovery, nested writebacks and advanced restoration remain open.

A fresh full CPU run passes **5,166 tests / zero failures / nineteen explicitly
unavailable tests**, total 5,185, with the exact roster/theory expansion/skips and
nine pinned WinUAE selections verified. Its ordinary semantic gate validates all
85,834,306 scenarios / 703 reports. Source and normal assemblies are checked
before/after; documentation follows that execution.

The final pinned SingleStepTests and Musashi audits pass their selected semantic
cases. The isolated unpublished `.66` package passes the CopperScreen Release
build, host/disk/engine checks and all three native replays. No regression is
retired and no public package is published. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-actual-move-write-fault-qualification--2026-10-06)
for exact identities and reference limitations. Milestone 6 remains
**in progress**, `roadmapComplete=false`.

### 68030 integrated-MMU user privilege qualification — 2026-10-06

The pinned unchanged WinUAE Basic audit exposed user-mode CpID-0 words raising
Line-F instead of privilege violation. The documented 030 priority now precedes
extension/operand handling, including undefined MMU patterns. Other models and
external coprocessor rules are preserved. A shared scalar/batch fixture checks
all primary words and separate status/secondary-word boundaries, full exception
frames, preserved registers/memory/stacks/SFC/DFC, software frame edit/RTE and
following sentinel/trace behavior without replaying the privileged instruction.

The focused gate passes **263,168 programs / 9,216 report keys / four reports**,
with 47 fixed controls and all 51 exact executions passing without skips. Four
maintained production mutations detect missing priority and incorrect
CpID/model/mode boundaries in 235 executions. Eight report and seven mutation
integrity controls reject corruption; an isolated restoration guard preserves
concurrent edits. The retained Line-F matrix corrects 4,096 obsolete expectations
and passes all 25 tests; the general system matrix corrects 32 F1C0 expectations
and passes its eight-profile selection. No old regression is retired.

Ordinary requirements become **86,097,474 scenarios / 707 reports**. Complete-040
requirements and its required 480 untested cases / fifteen combinations remain.
The unchanged broad Basic audit still fails 46 mismatching and eight unsupported
directories; the 030 MMUOP030 directory now passes 4,178 callbacks. These findings
are recorded separately from passing scoped reference presets. Legal supervisor
PMMU semantics and other milestone-6 requirements remain open; enabled MMU
translation and physical timing remain outside this roadmap. See the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#68030-integrated-mmu-user-privilege-qualification--2026-10-06).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The final full execution records 5,208 passing tests, nineteen optional skips and
nine WinUAE failures for pinned input manifests removed by concurrent cleanup;
it is not a green full run. The preserved TRX independently verifies all 707
ordinary batch/count summaries and 86,097,474 scenarios, while deleted JSON
report details remain unavailable. After cleanup stopped, the focused PMMU and
mutation commands pass again with complete fresh identities outside the cleaned
directory. The private `.67` consumer and external reference checks passed before
cleanup; their removed local records and replay inputs are not described as
present or rerun. See the qualification record for this distinction. No public
package release is performed.

After cleanup stopped, all nine scoped WinUAE presets were regenerated from
their pinned sources and passed fresh maintained audits: 105 executions without
failures/skips, 116 model/family directories, 1,525,856 callbacks and 858,001
exception frames. Exact input/report/binary identities and unchanged normal
assemblies are independently verified. Audit wrappers now isolate build outputs.
These runs resolve the missing-input failures separately; the cleanup-affected
full run is not reclassified green. See the
[restoration record](COPPER68K_REFERENCE_QUALIFICATION.md#restored-scoped-winuae-qualification--2026-10-06).
Other unavailable external evidence and all remaining milestone-6 gaps stay
explicit. Milestone 6 remains **in progress**, `roadmapComplete=false`.

Pinned SingleStepTests, Musashi and the broad Basic corpus are now restored and
rerun through a maintained isolated reference-only command. SingleStepTests
passes 312,500 cases / 125 files; Musashi passes 536 model/program combinations
with 88 documented exclusions. Basic intentionally fails its preserved 46
mismatching and eight unsupported rows; fresh diagnostics reproduce the prior
counts and expose a noncanonical 060 MOVE16 extension for the next legal-input
qualification. Four command preflight controls reject invalid requests. See the
[fresh evidence and explicit gap table](COPPER68K_REFERENCE_QUALIFICATION.md#restored-broad-reference-evidence--2026-10-06).
No production CPU change, regression retirement or package release is included.
Advanced restoration and remaining consolidation requirements remain open;
milestone 6 is **in progress**, `roadmapComplete=false`.

Canonical MOVE16 reference qualification now passes 83,712 callbacks on 040/060
in 22 executions without skips. All five forms, all user-mode register pairs,
sixteen low-nibble rounds and explicit weighted architectural keys are checked.
Native register/SR/memory corruption and five input/weighted-map controls detect
their intended errors. The native reference explicitly lacks 76 supervisor-A7
form/register/SR combinations; those remain required independent qualification.
The existing nine presets and 28 shared/synthetic controls pass again. Original
Basic failures, advanced restoration and all consolidation requirements remain
open. No production CPU change, regression retirement or package release is
included. See the [MOVE16 qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#canonical-move16-reference-qualification--2026-10-06).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The subsequent supervisor-A7 MOVE16 qualification closes all 76 previously
missing form/register/SR combinations. Copied generator restrictions are fixed;
independent active A-register assertions cover the native format's final SSP
blind spot. The maintained gate now passes 24 executions, 87,712 callbacks and
51,348 summed profile form keys, requiring all 384 form/register/SR combinations
on each of 040/060 with zero gaps. Six corruption/integrity controls reject their
intended errors. All nine existing presets and 28 shared controls pass again.
No production CPU fix, regression retirement or package release is included.
Original Basic failures and the full advanced restoration/consolidation scope
remain required. See the [supervisor-A7 record](COPPER68K_REFERENCE_QUALIFICATION.md#supervisor-a7-move16-reference-completion--2026-10-06).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The real 040 operand-read gap gained a concrete strict discovery gate: sixteen
legal MOVE/MOVEA/ADD/CMP/TST/MOVEM encodings, all CCRs, four stack banks, three
trace states, lanes and rejected read bytes, independently verified in scalar
and batch execution. Sixteen literal normal-execution witnesses pass. All
122,880 selected fault-entry cases fail the required format-7 stack boundary;
current delivery uses an eight-byte format-0 frame. Six evidence-integrity
controls reject intended corruption. These failing cases remain discovery,
without promotion or reducing the 480-case broad inventory. The next required
work is architectural read-fault delivery/latched MOVEM continuation, executed
recovery and affected-consumer qualification. No production edit, package release
or regression retirement is included. See the [operand-read discovery](COPPER68K_REFERENCE_QUALIFICATION.md#actual-040-operand-read-fault-discovery--2026-10-06).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The subsequent physical-read fix passes that complete selected entry inventory
and two brief/full indexed MOVEM fourth-transfer recovery controls. Calculated
EA metadata survives overwritten base/index registers, including the shared
integer fallback, without repeating pointer resolution. An isolated mutation
that drops the metadata fails both controls. The strict five-execution gate and
six evidence-integrity controls pass. Accurate fatal-entry checks now cover all
60 frame bytes for B/W/L reads, adding 1,248 cases; ordinary requirements become
86,098,722 scenarios / 707 reports. The fresh full CPU run passes 5,244 tests
with zero failures and 21 explicit optional/discovery skips; report validation
accepts every required ordinary batch. Clean CopperScreen consumer build, host/disk/
engine tests and two native Workbench floppy replays pass via a private NuGet
package, as do independent pinned SingleStepTests/Musashi audits. Broader
addressing/recovery/trace and remaining model/reference/consolidation coverage
stay required. See the [read-frame correction](COPPER68K_REFERENCE_QUALIFICATION.md#physical-040-operand-read-frame-correction--2026-10-06).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

On 2026-10-07, actual 040 MOVEM write-fault discovery executes every legal
store EA opcode with a selected D0/D1/A0/A1 list, word/long sizes, every rejected
transfer/byte, and separate canonical CCR/stack-bank/trace combinations. All
112,224 selected entries fail the required format-7 gate; retained diagnostics
show the current short format-0 delivery. The independent literal encoding
witness and 136 fault-free MOVEM/sentinel programs pass. Seven evidence-integrity
controls reject their intended corruptions. This is preserved failing discovery,
not promoted coverage or a production correction. Calculated-EA/CM write metadata,
normal WB1 construction and executed handler/RTE/resumption remain required;
full indexed/mask, alignment and nested write-fault protocols also remain open.
The broad 480-case inventory is not reduced, no regression is retired, and no
new package or consumer qualification is claimed. See the
[MOVEM write discovery](COPPER68K_REFERENCE_QUALIFICATION.md#actual-040-movem-write-fault-discovery--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The 2026-10-07 MOVEM physical-write correction now passes the selected 112,224
entry cases and 143,512 executed handler/RTE/resumption programs, including all
66 full-format structures, signed/scaled indexes, four stack states, CCRs,
predecrement A7, trace and self-overwritten pointers. Two owned mutations detect
lost shared write metadata and inappropriate EA recalculation. Seven integrity
controls pass for each current gate. Fresh pinned SingleStepTests/Musashi audits
and clean CopperScreen private-package .69 build, host/disk/engine tests and two
native floppy replays pass. Fresh full CPU validation passes 5,255 tests with
zero failures and 29 explicit optional/discovery skips (5,284 total), including
all ten qualified WinUAE presets. All 707 required ordinary reports and actual
passing TRX summaries verify 86,098,722 deterministic scenarios and complete
combination weights. Opt-in write-entry/recovery gates pass separately; other
skipped coverage remains unpromoted. Broader masks/fault/restoration/reference/consolidation requirements and
the broad 480-case inventory remain required. See the
[write-frame correction](COPPER68K_REFERENCE_QUALIFICATION.md#physical-040-movem-write-frame-correction--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

A follow-up transfer retention audit reviews six EXT.W/EXT.L/SWAP/EXG facts in
`M68020InterpreterTests`. All retain native/machine-cycle assertions for named
OCS/A1200 profiles that the semantic matrix does not replace. Their retention
reasons and exact asserted counts are recorded; no further regression is retired
and no replacement/mutation proof is claimed. See the
[retention audit](COPPER68K_REFERENCE_QUALIFICATION.md#transfer-timing-regression-retention-audit--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The subsequent 020/030 restoration audit establishes a real exception-entry
defect: all 6,144 selected odd-prefetch cases on EC020/A1200/020/030 deliver
normal format 0 rather than the documented A/B bus-fault frame. All 512
fault-free controls pass; four evidence-integrity controls reject intended
corruption. The strict maintained command fails acceptance and preserves all
model/stack/CCR/trace/address combinations. Pinned reference inspection shows
that its non-MMU RTE path merely pops advanced frame sizes and cannot establish
internal recovery correctness. Real fault-context generation, validation,
executed repair/RTE continuation and separate format-9 transport remain required.
No CPU fix, package or regression retirement is claimed. See the
[address-frame discovery](COPPER68K_REFERENCE_QUALIFICATION.md#020030-address-frame-entry-discovery--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The subsequent scoped 020/030 correction passes all 6,144 selected frame-entry
cases and 512 controls, plus 8,960 executed instruction-prefetch recovery/refusal
programs. Real handlers repair C/B words and clear rerun bits before RTE;
two-word retention, CCRs, stack states, trace, incompatible versions and explicit
uncleared-bit reentry are checked without odd instruction bus accesses. The
interpreter-specific opaque image is not claimed as a physical pipeline oracle.
Two isolated mutations detect lost restored words and four integrity controls
reject corrupted evidence. The 486 retained affected controls and fresh pinned
SingleStepTests/Musashi audits pass. Clean CopperScreen private-package .70
production build, host/disk/engine tests and two native floppy boots pass.
Fresh full CPU validation passes 5,265 tests, zero failures and 33 explicit
optional/discovery skips (5,298 total), including all ten qualified WinUAE
presets. All 707 ordinary reports and actual passing TRX summaries verify
86,098,722 deterministic scenarios and complete combination weights. Opt-in
entry/recovery matrices pass separately; remaining skipped coverage is unpromoted.
A/B data continuation, foreign images, format-9
transport and wider restoration/consolidation remain required. See the
[prefetch correction](COPPER68K_REFERENCE_QUALIFICATION.md#020030-instruction-prefetch-recovery-correction--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The subsequent RTE validation/load-fault slice passes 36,096 selected complete
programs across EC020/A1200/020/030. It preserves completed validation reads,
resumes only through explicit handler RTE, supports DF-cleared software input,
and halts on state-load or exception-entry failures. The maintained command
checks all 104 reports against their actual 22 executions and independently
enumerated cases. Mutation proofs, the archived failing baseline, independent
audits and clean private-package .71 consumer/native floppy checks pass. That
snapshot's unfinished full run was cancelled when the subsequent wrapping
correction was identified; final corrected-source results are recorded below.
Broader data continuation, foreign images, format-9 transport
and consolidation remain required. See the
[RTE fault correction](COPPER68K_REFERENCE_QUALIFICATION.md#020030-rte-validation-and-state-load-faults--2026-10-07).
Milestone 6 remains **in progress**, `roadmapComplete=false`.

The next address-transport qualification catches and fixes an EC020 wrapper
map-query bug at the 24-bit wrap. The maintained gate passes 32,256 normal-frame
RTE programs at seven low/high/boundary stack addresses, including real
validation-fault handlers and high VBR, with independently verified coverage
and four integrity controls. The original 36,096-case RTE gate also passes
against the corrected source. The isolated baseline retains 1,024 intended
wrap failures; 25 affected mapping/cache tests pass with the correction. The
older unfinished full run is preserved as cancelled evidence. Private .72 clean
consumer/native floppy qualification passes; fresh final full CPU validation
passes 5,291 tests with zero failures and 33 optional/discovery skips (5,324
total). The maintained inventory and separate actual-TRX audit verify all 707
ordinary reports, 86,098,722 deterministic scenarios and complete combination
weights. Wider private
load/entry fault transport, foreign images, data continuation and consolidation
remain required. Milestone 6 remains **in progress**, `roadmapComplete=false`.

Isolated development now passes 43,008 selected ordinary MOVE read-entry
cases and, in a subsequent snapshot, 43,008 explicit RTE-rerun programs;
each also passes 18,432 fault-free controls. Saved addresses, extension PC and
pipe words allow the selected register-destination suffix to finish without
instruction replay. These prototypes remain unimported and unpromoted pending
software-input/refault, handler-modification, alias/A7/index, mutation and
retained-control qualification. Broader instruction/operand continuations and
consolidation remain required. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

The subsequent frozen prototype passes 129,024 software-input, persistent
refault and handler-modification recovery programs. Three source mutations
detect 43,008, 50,176 and 21,504 intended mismatches, preserving the unaffected
passing cohorts. All 68,352 retained RTE programs and 25 mapping/cache/optional
controls pass. This remains unimported and unpromoted pending operation-origin
discrimination, saved-pipe, A7/alias/index, trace/interrupt, return-SR/stack,
high-address and integrity qualification. Broader continuation and consolidation
remain required. Milestone 6 remains **in progress**, `roadmapComplete=false`.

Typed pending-read development now distinguishes trace-vector suspension from
an already-completed MOVE source. The isolated corrected matrix passes 92,160
trace/control programs; removing its trace continuation causes exactly 73,728
failures while preserving 18,432 controls. Another 73,728 canonical-indirect
programs cover software-supplied vector targets, persistent refault and changed
VBR, both with and without an earlier source fault. Two mutations each detect
24,576 intended mismatches with 49,152 unaffected passes. Three evidence-integrity
controls reject missing reports, empty executions and incorrect weights.
All 129,024 previous handler programs and 68,352 RTE programs plus 25 retained
controls pass against the typed source. The prototype remains isolated and
unpromoted pending nested-PC provenance, other read origins, pipeline/A7/index,
trace/interrupt, changed return-SR/stack and wider transport qualification.
Broader continuation, reference gaps and consolidation remain required.
Milestone 6 remains **in progress**, `roadmapComplete=false`. See the
[typed trace-vector continuation](COPPER68K_REFERENCE_QUALIFICATION.md#typed-trace-vector-read-continuation--2026-10-07).

The unchanged isolated typed-read source now passes 196,608 A7/MOVEA programs
(21,504 controls; 175,104 repair/software-input/refault programs) and 86,016
captured private-pipe programs. A7 byte stride and aliased MOVEA ordering mutations
detect exactly 3,072 and 18,432 failures, preserving unaffected cases. A real
prefetch-handler/RTE sequence supplies the pipe words; cold and explicitly
batch-warmed dispatch respect saved/software-edited words. Pipe-loss and
edited-word mutations detect 86,016 and 43,008 failures respectively. Complete
source/producer/input inventories and ten deliberate integrity controls pass.
This remains unimported and unpromoted. Wider aliases/indexing, extension-bearing
and multiword pipes, other read origins, nested-PC provenance, trace/interrupt,
changed return-SR/stack, transport and general continuation stay required.
No CPU source/package change or regression retirement is included; milestone 6
stays **in progress**, `roadmapComplete=false`. See the
[A7 and private-pipe qualification](COPPER68K_REFERENCE_QUALIFICATION.md#a7movea-and-captured-private-pipe-qualification--2026-10-07).

PC-relative function-code qualification reproduces 64,512 precise SSW failures
with 9,216 fault-free controls in the isolated prototype. A typed-read correction
now passes all 73,728 selected operand/control programs and another 73,728
PC-relative trace-vector programs. Program-space operand codes remain separate
from supervisor data-space vector codes, without changing bus access kinds or
timing policy. Two mutations detect exactly 64,512 and 73,728 failures. Fresh
corrected-source retention passes 645,888 programs with 272 byte-identical
reports, plus 25 map/cache controls (89 executions total); ten integrity controls
and complete source/producer/input inventories pass. The correction remains
isolated and unpromoted. Indexed/pointer stages, other-family function codes,
wider transport, pipeline/aliases, nested-PC provenance, trace/interrupt,
changed return-SR/stack and all remaining reference/consolidation work stay
required. No package is built or published; milestone 6 stays **in progress**,
`roadmapComplete=false`. See the
[PC-relative function-code correction](COPPER68K_REFERENCE_QUALIFICATION.md#pc-relative-operand-function-codes--2026-10-07).

Indexed source/pointer qualification now distinguishes the suspended read stage
and preserves consumed extensions, evaluated index/outer components and the
selected timing policy. Eighteen literal witnesses expose 105,984 PC-indexed
SSW failures and 105,984 unsupported An-indexed RTE continuations, with 11,520
controls. The isolated correction passes all 223,488 selected programs and a
separate 223,488-program retained timing-policy gate. Fresh retention passes
793,344 prior programs with 352 byte-identical reports, plus all 25 map/cache
controls (109 executions total). Saved-offset, repeated-read and timing-policy
mutations detect exactly 81,920, 89,856 and 211,968 intended failures, preserving
unaffected cohorts. Complete source/producer inventories and ten deliberate
integrity controls pass. Production CPU source, packages and regression
retirement remain unchanged. The prototype remains isolated and unpromoted;
all-structural indexed/alias and chained-fault coverage, wider pipe/provenance,
transport, other read origins, trace/interrupt, changed return-SR/stack and
broader continuation/reference/consolidation work stay required. Milestone 6
stays **in progress**, `roadmapComplete=false`. See the
[indexed source/pointer qualification](COPPER68K_REFERENCE_QUALIFICATION.md#indexed-move-source-and-pointer-stages--2026-10-07).

The unchanged indexed prototype now passes 365,184 chained pointer/operand
programs (5,760 controls; 359,424 fault programs). Real handlers redirect the
second fault, supply different pointer/operand input, refault and alter
registers, consumed code and completed pointers. Stage advance, original
resumed-instruction PC/SR, ordered accesses and retained timing policies pass.
Three mutations detect exactly 359,424 stale-stage failures, 359,424 wrong-PC
failures and 239,616 repeated-pointer failures, preserving unaffected cases.
Complete source/producer inventories and five corruption controls pass. The
first fixture's 26,624 overlap failures remain invalid evidence; only the
fixture changes in its repair. Existing retained evidence is reused after
verifying unchanged CPU sources, with no new retention replay claimed.
Production source/packages remain unchanged and the prototype stays isolated
and unpromoted. All-structural indexed/register and wider chained/pipe,
provenance/transport, trace/interrupt, return-SR/stack, other-family and
remaining reference/consolidation work stay required. Milestone 6 remains
**in progress**, `roadmapComplete=false`. See the
[chained indexed fault qualification](COPPER68K_REFERENCE_QUALIFICATION.md#chained-indexed-pointer-and-operand-faults--2026-10-07).

All 66 legal full-indexed structures now pass a separate 786,432-program
read-recovery matrix on the unchanged isolated CPU prototype. Twenty fixed
An/PC examples anchor the shared addressing fixtures; self-code operands,
suppression, displacement lengths, indirection, stack states, CCR 00/1F and
all fault lanes remain represented. A repeated-word-read mutation detects
exactly 67,584 intended failures while preserving 718,848 unaffected programs;
complete source/key/weight checks and five integrity controls pass. Canonical
register/index selections do not replace the remaining permutation/alias
matrix. Existing retention/timing evidence is reused, without claiming a fresh
replay or all-structure timing gate. The prototype remains unimported and
unpromoted, with no package change or test retirement. Register/index aliases,
all-structure chained faults and broader continuation/reference/consolidation
gates stay required. Milestone 6 remains **in progress**, `roadmapComplete=false`.
See the [all-structure read qualification](COPPER68K_REFERENCE_QUALIFICATION.md#all-legal-full-indexed-read-structures--2026-10-07).

Separate indexed register and index-size/scale/alias groups now pass 768,000
programs on the unchanged isolated prototype. All An/PC base, destination and
D/A-index register selections use canonical index settings; rotated alias
layouts additionally cover word/long indexes and all four scales. A7 sources,
indexes and MOVEA destinations remain included, with ISP updates verified.
Real handlers alter participating registers, consumed code and completed
pointers. A saved-offset mutation detects exactly 153,600 intended failures
while preserving 614,400 unaffected programs; six fixed encoding examples,
complete source/key/weight/TRX checks and five corruption controls pass. Two
invalid-fixture snapshots remain audited separately; their repairs change only
the test file. Negative address-index/value boundaries, all-structure chained
faults and broader continuation/reference/consolidation remain required. The
prototype stays unimported and unpromoted, with no package change or test
retirement; milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [indexed register qualification](COPPER68K_REFERENCE_QUALIFICATION.md#indexed-register-index-size-scale-and-alias-groups--2026-10-07).

Signed D/A-index boundary qualification now passes 1,638,400 programs on the
unchanged isolated prototype. Eight fixed raw values, all register numbers,
word/long indexes, all scales and four An/PC alias layouts complement the prior
structure/register matrices. A7 uses user-stack values with a separate handler
frame; MOVEA A7 updates USP and its register expectation together. Sixty-four
fixed address examples anchor the shared fixture. A suspended self-code pointer
reads handler-updated bytes at its saved pointer address with its saved offset;
a fixture-only repair preserves 640 earlier expectation failures as invalid
evidence. The offset mutation detects exactly 327,680 intended failures,
including 640 same-value cases exposed by access checks, while 1,310,720 cases
remain passing. Complete source/key/weight/TRX audits and five integrity controls
pass. No production import, package change or regression retirement occurs.
All-structure chained faults and wider continuation/reference/consolidation
remain required; milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [signed index boundary qualification](COPPER68K_REFERENCE_QUALIFICATION.md#signed-index-boundaries-and-user-stack-a7--2026-10-07).

All-structure chained indexed faults now pass 2,198,784 programs on the unchanged
isolated prototype. All 66 full-format structures retain 42,240 controls; 54
pointer-bearing structures execute 2,156,544 chained programs, with 12 direct
structures explicitly excluded from pointer chains. Real handlers cover repair,
input, refault and aliases; original PC/SR, ordered effects, private stage
advance, sentinel and existing timing policy pass. A mutation against the
repaired fixture detects exactly 2,156,544 intended failures while preserving
all controls. Complete source/key/weight/TRX audits and five integrity controls
pass. A compile-only snapshot and 6,656 earlier sentinel fixture failures remain
invalid evidence, with fixture-only repairs. No production import, package
change or regression retirement occurs. Wider pipes, provenance/transport and
remaining continuation/reference/consolidation gates remain required;
milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [all-structure chained qualification](COPPER68K_REFERENCE_QUALIFICATION.md#all-structure-chained-indexed-faults--2026-10-07).


Three-word private pipe transport now passes 1,003,520 programs across seven
source forms, operand sizes, stack states, all CCR/read lanes, scalar/batch and
cold/warm host-reader preparation. Real handlers supply pipe lengths 0..3 and
edit each retained position against different backing opcode/extensions; the
following MOVE.L and NOP verify values and fetch boundaries. Middle/final word
mutations detect exactly 702,464 / 401,408 intended failures while unaffected
cases pass. Complete source/key/weight/TRX audits and five integrity controls
pass. The earlier extended fixture's 143,360 zero-word CCR expectation failures
remain separate invalid evidence, with a fixture-only repair. This qualifies
private software frame transport; broader indexed/chained pipes, provenance,
transport and remaining continuation/reference/consolidation stay required.
No production import, package change or regression retirement occurs;
milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [three-word pipe qualification](COPPER68K_REFERENCE_QUALIFICATION.md#three-word-private-pipe-transport--2026-10-07).


Nested operand/trace-vector provenance now passes 516,096 programs across seven
MOVE source forms, operand sizes, stack states, all CCR/vector lanes and both
execution routes. Real handlers overwrite consumed code, supply vector input,
refault and change VBR. Original instruction PC/opcode, next PC, post-MOVE trace
SR, typed pending state and completed effects pass. Sole PC/SR mutations detect
exactly 258,048 / 241,920 intended failures, preserving unaffected cases.
Complete source/key/weight/TRX audits and five integrity controls pass. Earlier
vector-initialization failures remain invalid fixture evidence with a fixture-
only repair. This qualifies serialized private provenance, not physical overlap.
Wider indexed/chained pipes/provenance, transport and remaining continuation/
reference/consolidation gates remain required. No production import, package
change or regression retirement occurs; milestone 6 stays **in progress**,
`roadmapComplete=false`.
See the [nested trace qualification](COPPER68K_REFERENCE_QUALIFICATION.md#nested-operand-and-trace-vector-provenance--2026-10-07).


Nominal high/wrapped private operand frames now pass 94,080 programs at ten
frame addresses, including odd and 24-/32-bit crossings. Seven source forms,
sizes, stack states, all operand lanes, CCR 00/1F and real input/refault/alias
handlers verify full logical stacks, physical entry ordering/domain, guards,
poisoned mirrors and continued effects. Stack/entry truncation mutations detect
exactly 47,040 / 32,928 intended failures, preserving unaffected cases.
Complete source/key/weight/TRX audits and five integrity controls pass. Frame
entry/state-load faults and remaining wider continuation/reference/consolidation
gates remain required; no production import, package change or test retirement
occurs. Milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [private frame transport qualification](COPPER68K_REFERENCE_QUALIFICATION.md#high-and-wrapped-private-operand-frames--2026-10-07).

Private frame-entry and internal state-load faults now pass 138,240 programs
on the unchanged isolated prototype: 58,880 entry faults, 78,080 load faults
and 1,280 recovery controls. Ten high/wrapped/odd frame starts, all 92 entry
byte positions and 122 load-byte occurrences, stack states, CCR 00/1F and both
routes verify the complete literal image, completed access prefixes, exact
HALT state and absence of subsequent architectural/bus work. Scalar Idle timing
and zero-retirement batch policy remain intact. Sole entry/load mutations detect
exactly 58,880 / 78,080 intended failures, preserving unaffected cases.
Complete source/key/weight/TRX audits and five integrity controls pass. The
compile-only attempt and 68,480 earlier scalar timing-expectation failures stay
invalid fixture evidence; both repairs change only the fixture. No production
import, package change or regression retirement occurs. Wider indexed/chained
pipes/provenance, foreign frames, other origins, reset/address-error entry and
remaining continuation/reference/consolidation gates stay required. Milestone
6 remains **in progress**, `roadmapComplete=false`.
See the [frame fault HALT qualification](COPPER68K_REFERENCE_QUALIFICATION.md#private-frame-entry-and-internal-state-load-halt--2026-10-07).

Two pure CMPM alias/A7 methods (four original cases) are now retired with a
pinned-original/shared-replacement proof. The maintained command passes 203,520
shared programs; alias-order and byte-stack-stride mutations detect exactly
288 / 180 intended failures and the corresponding original witnesses. All
36 specialized timing/width/flags/memory cases remain and pass under both
mutations. Complete source linkage, exact TRX/key/weight/reason audits and eight
integrity controls pass. A direct reduced-worktree run passes all 44 selected
tests, including the 36 retained cases and eight shared batches. Production CPU
source, packages and protected normal assemblies are unchanged; no fresh full
CPU/consumer replay is claimed. The indexed/chained pipe matrix remains a
separate pending gate. All remaining reference/continuation/consolidation
requirements stay open; milestone 6 remains **in progress**,
`roadmapComplete=false`.
See the [CMPM replacement mapping](COPPER68K_REFERENCE_QUALIFICATION.md#cmpm-alias-and-a7-semantic-consolidation--2026-10-07).

The full-format indexed/chained private pipe gate now passes 4,692,480 programs
across 1,920 reports and 480 executions. All 66 structures are covered by source
faults and all 54 pointer-bearing structures by pointer-to-operand chains;
the 12 direct forms remain explicit chain exclusions. Software prefixes of
zero through three words and each retained-word edit survive real handler
changes and selected scalar/batch recovery. Sole capture/restore mutations
detect exactly 1,437,696 / 1,876,992 intended failures, with unchanged fixtures.
Complete source/key/weight/TRX/reason audits and five integrity controls pass.
This is private transport qualification, not physical pipe/cache timing.
Production source/packages and CMPM retirements remain unchanged; the prototype
is unimported. Wider indexed/chained nested provenance, foreign frames, other
origins and remaining roadmap gates stay required. Milestone 6 remains
**in progress**, `roadmapComplete=false`.
See the [indexed/chained pipe qualification](COPPER68K_REFERENCE_QUALIFICATION.md#full-format-indexed-and-chained-private-pipe-transport--2026-10-07).

Selected private-frame relocation now has a retained failing witness: 224
supervisor-stack cases are unsupported while 672 controls/user-stack cases
pass. A separate isolated correction removes the original-SP restriction and
passes all 896 programs, retaining every frame read and the saved operand
address. Fresh affected retention passes 138,240 frame-entry/internal-load
fault programs; complete TRX/key/weight/source audits and six integrity
controls pass. The correction remains unimported. Indexed/chained nested trace
continues separately; foreign/cross-profile frames, broader return-SR/stack
and alias behavior, full CPU/consumer validation and remaining roadmap gates
stay required. Milestone 6 remains **in progress**, `roadmapComplete=false`.
See the [private-frame relocation evidence](COPPER68K_REFERENCE_QUALIFICATION.md#copied-private-frame-and-selected-stack-relocation--2026-10-07).

The isolated relocation correction now passes 139,776 stack/CCR alias programs
across 192 reports and 48 executions: A0/A7 indirect/postincrement/predecrement,
MOVE to D0/D7, MOVEA to A0/A7, four stack states, copied/unmoved frames, real
CCR edits, all operand lanes and scalar/batch. Canonical A7 postincrement
covers all 32 CCR values. Stride/X mutations detect exactly 8,192 / 69,888
intended failures; complete source/TRX/key/weight/reason and six corruption
controls pass. The 256 earlier overlap/read-count fixture failures and exact
fixture-only repair remain retained. CPU source is unchanged from the isolated
relocation correction and is unimported. Nested indexed trace continues on its
separate parent; privilege/bank/trace edits, foreign frames, other origins,
full CPU/consumers and remaining roadmap gates stay required. Milestone 6
remains **in progress**, `roadmapComplete=false`.
See the [A7 alias and CCR record](COPPER68K_REFERENCE_QUALIFICATION.md#private-recovery-with-a7-aliases-and-edited-ccr--2026-10-07).

Full-format indexed/chained nested-trace qualification now passes 6,137,856
programs, 3,456 reports and 864 executions. All 66 direct/source structures
and 54 pointer chains are covered; the 12 direct chain exclusions remain
explicit. Real opcode-overwrite and buffer/refault/VBR handlers preserve
original provenance, post-instruction SR, completed reads and trace frames.
PC/width mutations detect exactly 5,630,976 / 6,137,856 intended failures;
complete source/TRX/key/weight/reason audits and six corruption controls pass.
Compile-only fixture repair and the corrected pre-acceptance planned total are
retained. This unchanged-CPU parent remains separate from the qualified
relocation/stack-alias candidate until a fresh combined snapshot and complete
selected replay pass. Foreign frames, privilege/bank/trace edits, other origins,
full CPU/consumer validation and remaining gates stay required. Milestone 6
remains **in progress**, `roadmapComplete=false`.
See the [indexed nested-trace record](COPPER68K_REFERENCE_QUALIFICATION.md#full-format-indexed-and-chained-nested-trace-provenance--2026-10-07).

Returned privilege/stack-mode discovery now records 3,328 passing controls and
3,328 precise unsupported S-change cases. A sole private validator correction
separates saved cycle FC from returned SR and passes all 6,656 programs across
64 reports and 16 executions, with the fixture unchanged and six corruption
controls passed. All sixteen S/M bank transitions, copied/control frames,
MOVE/MOVEA `(A0)`, CCR00/1F, operand lanes and scalar/batch are covered.
Persistent refault FC, returned trace edits, wider aliases/indexed cases and
foreign origins remain required. The combined run uses the preceding CPU
snapshot and cannot qualify this additional correction. No production import,
publication or full-replay claim is made. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[returned privilege and stack-mode record](COPPER68K_REFERENCE_QUALIFICATION.md#private-recovery-with-returned-privilege-and-stack-modes--2026-10-07).

Repeated-fault coverage after saved privilege edits now passes 13,312 programs
(128 reports / 32 executions). It retains 6,656 exact original FC mismatches
and detects 6,656 scope-leak mutation failures with the fixture unchanged.
All sixteen S/M pairs, one/two refaults, copied/control frames, MOVE/MOVEA
`(A0)`, CCR00/1F, operand lanes and scalar/batch are covered. A scoped private
saved-cycle FC correction also passes fresh affected retention: 800,768
programs / 752 reports / 188 executions against four pinned parent proofs.
Both complete audits and twelve evidence corruption controls pass. The older
large combined run does not qualify this newer CPU. Changed-return trace,
wider aliases/indexed chains, foreign origins and full CPU/consumer gates
remain required; production source/packages remain unchanged. Milestone 6
remains **in progress**, `roadmapComplete=false`. See the
[changed-privilege repeated-fault record](COPPER68K_REFERENCE_QUALIFICATION.md#private-recovery-with-changed-privilege-and-repeated-faults--2026-10-07).

Returned-trace private-policy coverage passes 188,416 programs across 512
reports and 128 executions on unchanged refault-corrected CPU source.
Suppression/user-vector-FC mutations each detect 175,104 failures, retaining
13,312 trace-off controls; complete audits and six corruption controls pass.
This does **not** complete architectural changed-T1 qualification: 86,528
cases preserve T1 and 101,888 edit it, with the latter explicitly reference-
unresolved. The manual selects tracing at instruction start; the prototype
uses the returned SR for its resumed suffix. A genuine reference-generated
fault/frame with only stacked T1 edited must settle the four canonical trace
combinations before promoting that behavior. The proof requires the hardware
qualification flag to remain false. Hardware T0, wider indexed/alias/foreign
cases, full CPU/consumers and remaining gates stay required. No production
import or publication is claimed. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[returned-trace policy and reference-gap record](COPPER68K_REFERENCE_QUALIFICATION.md#private-returned-trace-policy-and-unresolved-architectural-outcomes--2026-10-07).

The fresh combined private recovery replay passes 11,109,248 programs across
5,744 reports and 1,436 executions, with exact complete parent selections,
227 source identities and seven corruption controls verified. This closes
the combined replay gate for the relocation-corrected snapshot only; later
returned-bank/refault corrections and changed-T1 architectural uncertainty
remain separately identified. Full CPU/consumer validation, wider origins,
foreign frames and remaining roadmap gates are still required. No production
CPU import or publication occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[combined recovery record](COPPER68K_REFERENCE_QUALIFICATION.md#complete-combined-private-recovery-replay--2026-10-07).

A maintained native 030 observer now executes reference-generated operand
fault/frame/RTE recovery for all four initial/returned T1 pairs, with two
direct controls, native replay and seven corruption controls. Returned T1=1
traces after the following MOVEQ in the previous WinUAE MMU030 loop, while
the direct trace-on control traces immediately after MOVE. This disagrees
with immediate resumed-suffix tracing, including unchanged T1=1; it does not
qualify that private policy or establish hardware behavior. Reconcile the
software/manual trace point before promotion or wider trace qualification.
No production CPU import/publication occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[executed reference disagreement](COPPER68K_REFERENCE_QUALIFICATION.md#executed-030-returned-trace-reference-disagreement--2026-10-07).

Compatible 030 reference qualification now executes the intact software
pipeline, generated `_35` instructions and compatible RTE helper. Fresh
previous/compatible profiles reproduce eight recoveries and four direct
controls, with nine evidence corruption checks and two native observer
guards. Both fail the explicitly requested architectural-agreement gate
for unchanged T1=1: their trace occurs after following MOVEQ rather than
before it. The manual-defined unchanged-T1 requirement is retained;
software reference agreement cannot override it. Handler-edited T1 remains
unresolved, and no production CPU behavior is changed. Observer word-width
repairs and failed exploratory runs are recorded separately. Milestone 6
remains **in progress**, `roadmapComplete=false`. See the
[compatible pipeline/reference gate record](COPPER68K_REFERENCE_QUALIFICATION.md#compatible-030-pipeline-reference-and-failing-agreement-gate--2026-10-07).

The pure semantic 68010 EXTB rejection regression is now replaced by the
shared register-transfer suite after an isolated proof restores its exact
original fixture. All 140,992 shared scenarios pass across eight profiles;
an intentional 68010 acceptance defect fails both the original witness and
exactly 2,048 shared EXTB scenarios. Seven other 68010 tests pass unchanged,
six evidence corruption checks reject altered inputs, and all 15 executions
in the reduced repository selection pass without skips. Timing and hardware
regressions remain. This completes only the EXTB consolidation slice;
remaining reference gaps, including the 030 trace disagreement, stay open.
Milestone 6 remains **in progress**, `roadmapComplete=false`. See the
[EXTB consolidation record](COPPER68K_REFERENCE_QUALIFICATION.md#extb-availability-regression-consolidation--2026-10-07).

Indexed returned-bank qualification now has a completed focused control of
51,584 programs and an unchanged-fixture cycle-FC mutation detecting 19,968
precise failures, plus six evidence corruption checks. The CPU source is
unchanged from the refault-corrected private snapshot. Its separate full
all-sixteen-bank / full-index source-and-chain selection is still running:
1,876,992 programs, 768 reports and 192 executions are required before its
gate can pass. Focused evidence cannot replace the complete selection.
Trace remains disabled in this slice; its disagreement and other required
reference/foreign-frame/general fault gates stay open. No production import
or publication occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[indexed returned-bank progress record](COPPER68K_REFERENCE_QUALIFICATION.md#indexed-returned-bank-recovery-qualification-in-progress--2026-10-07).

The complete indexed returned-bank run above now passes all 1,876,992 programs,
768 reports and 192 executions, with seven complete-gate corruption controls.
All sixteen bank pairs and all 66 source/54 indirect structures are audited
on the unchanged private refault-corrected CPU snapshot. The focused mutation
retains its separate scope. This closes that selected private gate only;
trace, broader/foreign origins, combined/full CPU/consumer validation and
remaining qualification requirements stay open. See the
[complete indexed returned-bank record](COPPER68K_REFERENCE_QUALIFICATION.md#complete-indexed-returned-bank-recovery-gate--2026-10-07).

The shared arithmetic matrix now covers every An source/destination field
for ADDA/SUBA/CMPA W/L, both stack modes and all alias CCRs, adding 141,312
scenarios. Its 200,068-scenario addressing selection and 79 original HDF-boot
regressions pass. A two-path ADDA sign-extension mutation fails all eight
profiles and all four original ADDA witnesses; six corruption checks pass.
Only that pure semantic method is retired, leaving 75 other HDF-boot
executions, including timing checks. The reduced 83-execution selection passes
without skips. The ordinary requirement becomes 86,240,034 scenarios in
707 reports; a fresh complete-suite run is not claimed by this test-only
change. No production CPU import or publication occurs. Milestone 6 remains
**in progress**, `roadmapComplete=false`. See the
[address-register matrix and consolidation record](COPPER68K_REFERENCE_QUALIFICATION.md#address-register-arithmetic-matrix-and-adda-consolidation--2026-10-07).

A fresh complete five-selection replay is now running on the latest scoped-FC
and returned-bank corrected private CPU, with all 230 parent inputs unchanged.
The required 11,109,248 programs / 5,744 reports / 1,436 executions remain
pending. A provisional 721,984-scenario / 599-report comparison and
four integrity controls pass; they do not replace the terminal full gate.
No production import/publication or architectural trace qualification occurs.
Milestone 6 remains **in progress**, `roadmapComplete=false`. See the
[latest combined replay progress record](COPPER68K_REFERENCE_QUALIFICATION.md#latest-refault-corrected-combined-replay-in-progress--2026-10-07).

ADDA consolidation now also has a maintained, isolated reproduction command:
`python scripts/test-copper68k-address-arithmetic-consolidation.py --output artifacts/address-arithmetic-audit`
and the same command with `--validate-only` replays the complete proof.
Fresh pinned historical, expanded clean, two-path mutation and current reduced
selections pass their required gates, together with seven integrity controls.
The historical reports are regenerated without temporary reference inputs;
all eight profiles and four original witnesses detect the intended defect.
No further test retirement, CPU change or publication occurs. The latest
combined private replay and broader milestone-6 requirements stay pending.
See the [maintained consolidation record](COPPER68K_REFERENCE_QUALIFICATION.md#maintained-address-arithmetic-consolidation-audit--2026-10-07).

The full CPU integration replay now runs on an isolated 205-input snapshot:
38 exact latest private CPU inputs and 167 current test/project inputs, with
all three documented retirements and expanded arithmetic coverage preserved.
Its positive preflight and eight corruption controls pass. Required terminal
coverage remains 5,315 executions (5,282 passing / 33 unavailable), 865 profile
reports, 707 ordinary reports / 86,240,034 scenarios and ten pinned native
presets. Both full CPU and combined private recovery executions are pending;
input qualification is not execution completion. No production import or
publication occurs. Milestone 6 stays **in progress**, `roadmapComplete=false`.
See the [current-tests/private-CPU full replay record](COPPER68K_REFERENCE_QUALIFICATION.md#current-tests-on-latest-private-cpu--full-replay-started-2026-10-07).

The latest returned-bank/scoped-FC private CPU now completes the unchanged
five-selection combined gate: 11,109,248 scenarios / 5,744 reports / 1,436
passing executions, zero skips, with exact original coverage and seven full
integrity controls. This supersedes provisional evidence for that selection.
Full current CPU/consumer qualification and broader roadmap gates remain
required. Hardware is unavailable and the trace boundary stays unresolved;
no production import/publication or architectural trace promotion occurs.
Milestone 6 stays **in progress**, `roadmapComplete=false`. See the
[complete corrected combined record](COPPER68K_REFERENCE_QUALIFICATION.md#latest-refault-corrected-combined-replay-complete--2026-10-07).

The current full CPU project on the latest private source completes its exact
integration gate: 5,282 passes / 33 unavailable / 5,315 executions, zero failures;
865 profile reports, 707 ordinary reports / 86,240,034 scenarios, and ten pinned
native preset audits. Eight complete-evidence corruption controls pass. The
separate combined recovery gate remains qualified on identical CPU source.
Consumer qualification and broader reference/fault requirements stay open;
architectural trace remains unresolved. No production import/publication occurs.
Milestone 6 stays **in progress**, `roadmapComplete=false`. See the
[full current-tests/private-CPU record](COPPER68K_REFERENCE_QUALIFICATION.md#current-tests-on-latest-private-cpu--full-replay-complete-2026-10-07).

Changed-bank A7 discovery now distinguishes two postincrement hypotheses in
60,928 scenarios each. Updating the original physical bank yields 35,840 A7
disagreements; updating the selected returned bank passes all cases on identical
CPU source. All predecrement and same-bank controls pass. Exact identities,
weights, failure IDs and test outcomes are verified. This remains unqualified
architectural behavior; changing an expectation is not a CPU fix or completion.
Milestone 6 stays **in progress**. See the
[returned A7 discovery record](COPPER68K_REFERENCE_QUALIFICATION.md#returned-a7-bank-selection-discovery--2026-10-07).

The native 030 A7 observer now executes sixteen fault bank pairs and four
direct controls using unchanged postincrement/fixup/frame/RTE/run-loop source.
Its same-bank user fault cases disagree with the ordinary four-byte update;
the architectural question remains open. Two observer provenance/replay guards
pass, and failed exploratory adapters remain retained. No CPU change or
architectural promotion occurs. See the
[native A7 discovery record](COPPER68K_REFERENCE_QUALIFICATION.md#native-030-returned-a7-observer-discovery--2026-10-07).

The native A7 discrepancy is isolated to inverse-fixup ordering: the unmodified
reference undoes the original increment on the handler's stack before restoring
SR. A sole native-helper ordering experiment removes both unchanged-bank user
discrepancies and retains all direct controls. This is causal software evidence,
not a qualified reference correction or changed-S/M architectural expectation.
No CPU change or promotion occurs. See the
[native fixup-ordering record](COPPER68K_REFERENCE_QUALIFICATION.md#native-a7-fixup-ordering-cause-isolated--2026-10-07).

The passive native A7 observer now has a maintained repository command and
fixture, regenerating pinned reference fragments without temporary producers.
Fresh generation and deterministic replay reproduce sixteen fault cases and
four direct controls. Eight corrupted-evidence controls fail precisely.
Discovery-only mode records unqualified observations; ordinary validation fails
the two unchanged-bank user recovery disagreements. Twelve changed-bank
architectural outcomes remain explicitly untested. Hardware is unavailable;
no CPU change, promotion or publication occurs. Milestone 6 remains
**in progress**, `roadmapComplete=false`. See the
[maintained A7 command and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#maintained-native-a7-discovery-command--2026-10-07).

Shared SUBA indirect coverage now passes 12,288 word/long, alias, stack and
all-CCR scenarios across all eight profiles. An isolated two-path word
zero-extension mutation produces exactly 3,072 shared mismatches and detects
the original word witness, retaining 9,216 controls and the original long case.
Eight evidence-corruption controls are precisely rejected; the maintained
command supports fresh generation and strict validate-only replay. The old
method remains: its separate long-width witness and remaining class need
replacement/retention proof before retirement. No production CPU change or
publication occurs. Milestone 6 remains **in progress**, `roadmapComplete=false`.
See the [SUBA indirect proof and remaining gate](COPPER68K_REFERENCE_QUALIFICATION.md#suba-indirect-shared-replacement-proof-retirement-pending--2026-10-07).

The pending SUBA indirect retirement gate is complete: independent word
zero-extension and long-as-word mutations fail their respective original rows
and produce exactly 3,072 / 4,608 shared failures, with the other width retained
as a control. Original/reduced class runs pass 47 / 45 cases plus eight shared
batches, each covering 12,288 scenarios. Only the one two-row pure semantic
method is removed; exact source-removal scope is checked. Fresh end-to-end
reproduction and validate-only replay pass, including ten evidence controls.
All specialized timing, factory, continuation and memory regressions remain.
No production CPU change/import/publication occurs. Milestone 6 remains
**in progress**, `roadmapComplete=false`. See the
[completed SUBA word/long replacement proof](COPPER68K_REFERENCE_QUALIFICATION.md#suba-indirect-wordlong-retirement-completed--2026-10-07).

CMPA's three-row pure displacement/sign-extension method is retired after
10,240 shared scenarios across eight profiles pass with the original negative
displacement, full 32-bit destinations, all CCRs and both stack modes. A scoped
base/advanced source-sign mutation produces exactly 5,120 shared failures and
detects both original negative-source witnesses, retaining 5,120 controls and
the original positive-source case. Original/reduced class runs pass 45 / 42
cases plus eight shared batches. Exact removal scope and all rosters/results
are verified; timing and integration checks remain. A reusable internal audit
runner checks producer/source/command identities and nine corruption controls.
Fresh end-to-end reproduction and strict replay pass. No production CPU change
or publication occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[CMPA replacement proof](COPPER68K_REFERENCE_QUALIFICATION.md#cmpa-displacement-sign-extension-consolidation--2026-10-07).

Independent native 030 source-fault recovery now covers a memory destination:
4,096 programs pass (2,048 recoveries / 2,048 controls), indirect/postincrement
A0 source, separate A1 or aliased A0 destination, four stack states, four values
and all CCRs. Exact source attempts, one successful read/write, postincrement
alias address, pre-sentinel MOVE flags, untouched state/memory and frame
preservation are checked. Frozen native replay matches; three sole observer
mutations and seven corrupted-evidence controls are rejected precisely.
This is scoped software reference evidence, not hardware/trace/translation or
architectural promotion. The private 020/030 candidate still excludes memory
destinations; implementation and broader model/EA/fault coverage stay required.
No production CPU/package change occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the
[native memory-destination evidence and scope](COPPER68K_REFERENCE_QUALIFICATION.md#native-source-read-recovery-before-a-memory-destination--2026-10-07).

The observed indirect-memory MOVE.L destination now has a bounded private
source-read recovery implementation. Original private source rejects 65,536
recoveries while retaining 16,384 direct controls; the isolated suffix passes
all 81,920 new scenarios and 61,440 existing register-destination scenarios.
Read replay, alias address, flag and timing mutations fail precisely; eleven
evidence-corruption controls are rejected and original evidence revalidates. Production
direct coverage passes separately; two optional private recovery batches remain
unavailable there. The maintained command pins the complete private parent and
verifies exact inputs, rosters, coverage keys/weights and outcomes. Existing
specialized timing policies and operand order are preserved. Broader fault/EA,
trace/hardware, full integration and consumer gates remain open before any CPU
import. No production CPU/package change occurs. Milestone 6 remains **in
progress**, `roadmapComplete=false`. See the
[private memory-destination suffix and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-memory-destination-read-recovery-suffix--2026-10-07).

The native MOVE fault reference now covers byte/word/long source-read and final
destination-write recovery with A0/A7, aliases, four stack states/values and all
CCRs: 33,792 programs pass, with 3,072 A7-postincrement source-read combinations
explicitly untested by this command. Final writes use short format A with the
following PC, completed flags and pending output; source reads use format B.
Four sole observer/input defects and nine evidence corruptions are detected;
frozen native replay matches. The current private CPU separately reproduces
6,144 denied-write mapping bypasses while 6,144 direct controls pass. Its
maintained default audit correctly fails the recovery gate; discovery-only
records the failures without promotion. Write recovery and byte/word source
continuation still require implementation, mutation/retention and integration
proof. No production CPU/package change occurs. Milestone 6 remains **in
progress**, `roadmapComplete=false`. See the
[native widths/final-write evidence and failing private probe](COPPER68K_REFERENCE_QUALIFICATION.md#native-move-widths-and-final-write-fault-discovery--2026-10-08).

The selected pending final-write path now has an isolated private implementation:
all 12,288 new scenarios pass, including 6,144 formerly failing recoveries, with
143,360 exact previous read-recovery results retained. Five deliberate defects
are detected while direct controls remain passing. The strengthened fixture
checks source guard reads and existing specialized timing policy; fresh unchanged
parent discovery preserves the original 6,144 mapping failures. Production
direct controls pass separately; its private recovery batches remain unavailable.
Short-frame software edits/refaults, saved pipe variants, foreign/frame faults,
trace/changed stacks, byte/word source-read memory destinations and read-to-write
chains remain unqualified. Full integration and consumer gates are still required
before importing this private candidate. Hardware is unavailable, so unresolved
reference disagreements remain open. No production CPU/package change or test
retirement occurs. Milestone 6 remains **in progress**, `roadmapComplete=false`.
See the [private final-write implementation and bounded evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-final-move-write-continuation--2026-10-08).

Pending-write handler protocols now pass 491,520 all-CCR cases for unchanged/
edited output, software DF-clear completion, another write denial and refault
after CCR/FC edits, with 155,648 previous recovery cases exactly retained.
Four sole defects are detected, including repeated postincrement, flag
recomputation and lost refault FC. Reports are split below the shared failure
witness cap; a capped V1 mutation audit was rejected rather than accepted with
incomplete detail. Second-fault stack-bank/SR, register/memory, timing, exception
count and sentinel checks pass. Production controls remain passing and optional
private recovery tests remain unavailable there. These checks qualify the
private software protocol; new handler edits/refaults still need independent
reference evidence, alongside the remaining short-frame, source-read/chained,
trace and integration/consumer gates. No production CPU/package change or test
retirement occurs. Milestone 6 remains **in progress**, `roadmapComplete=false`.
See [pending-write handler scope and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-pending-write-handler-protocols--2026-10-08).

The pinned native 030 observer now independently agrees with unchanged frames,
edited output and DF-clear completion: 36,864 programs pass. All 24,576 refault
comparisons remain mismatching because the native short frame saves the RTE
handler PC and subsequent recovery does not reach the original continuation.
One adapter-state intervention isolates the fault-PC origin and makes all 61,440
software comparisons pass without changing native fragments; this diagnostic
does not establish hardware behavior or an authoritative CPU correction. Negative
DF/output defects are detected, eight evidence corruptions are rejected, and
frozen rows/trace replay exactly. The ordinary agreement gate correctly fails;
discovery-only records the failures without promotion. Manual recovery/deallocation
rules do not by themselves settle the saved-PC/internal-state distinction.
Hardware is unavailable. New external evidence qualifies only the stated 030
non-refault software scope; the private refault protocol remains unqualified
architecturally. Broader source-read/chained, short-frame, trace, integration and
consumer requirements remain open. No CPU/package change or test retirement
occurs. Milestone 6 remains **in progress**, `roadmapComplete=false`. See the
[native handler agreement, disagreement and causal diagnostic](COPPER68K_REFERENCE_QUALIFICATION.md#native-pending-write-handler-comparison--2026-10-08).

The private all-width source-read suffix and read-to-write/refault chain gate
now passes 786,432 new cases, exactly retaining 647,168 earlier cases. Byte/word
recovery gaps in the old private baseline are reproduced; four sole defects
are detected with complete failure witnesses, and evidence integrity controls
reject incomplete or altered results. Current production direct controls pass
120,832 cases, with eight private recovery batches unavailable. The pinned
33,792-case native transfer evidence replays exactly in its stated scope.
A7 source-postincrement and repeated-fault saved-PC disagreements remain open;
the new chains qualify the private software contract only. Broader short-frame,
trace, frame/fault/origin and full integration/isolated-consumer gates remain
required. Hardware is unavailable. No production CPU import, package publication
or regression retirement occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See [all-width source and chained-write scope](COPPER68K_REFERENCE_QUALIFICATION.md#private-all-width-source-read-and-chained-write-continuation--2026-10-08).

The private short-frame saved-pipe gate now passes 184,320 cases for every
zero-to-three word length/edit, normal/DF-clear/refault completion, all transfer
widths, A0/A7 and aliases/banks, while retaining all 1,433,600 previous cases.
Four sole defects are detected, including data-word reordering without altered
reads. Seven evidence corruptions are rejected; the initial fixture's omitted
far-end validation request is independently traced and its failed evidence
preserved. No CPU correction occurs. The complete frozen production/private CPU
run remains active and its final result is unavailable; the new focused fixture
is a later test-only addition with byte-identical private CPU inputs. Integration,
isolated consumers, broader frame/provenance/fault and architectural A7/trace/
native refault-PC gates remain required. Hardware is unavailable. No production
import, publication or test retirement occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See [saved-pipe scope and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-pending-write-saved-pipes--2026-10-08).

The frozen complete production suite passes 5,299 tests with 41 explicitly
unavailable (5,340 total), and the private suite passes 8,021 with 33 unavailable
(8,054 total), both with zero failures. Strict verification binds respectively
86,453,506 / 109,079,170 logical cases, 921 / 12,065 complete profile reports,
ten native presets each, exact selections and source/assembly identities.
The initial 8,022-name discovery used the wrong text encoding and included a
deferred theory placeholder; its verifier rejection is preserved. Explicit UTF-8
discovery plus independent enumeration of 33 declared fixture rows reconstructs
exactly all 8,054 execution names. Missing names and changed data are rejected.
The later focused pipe fixture remains separate, with identical private CPU bytes.

Immutable local `.73` passes a clean committed CopperScreen archive build,
171 host tests (six unavailable), 74 disk tests, 1,080 engine tests and two
Workbench 3.1 floppy replays. These validate the private software contract,
not the outstanding architectural disagreements. No CPU candidate import or
package publication occurs. See the [full integration and consumer evidence](COPPER68K_REFERENCE_QUALIFICATION.md#current-full-production-baseline--2026-10-08).

The ordinary validator now requires the already executed SUBA/CMPA replacement
groups on every profile: 22,528 cases / 16 reports. Its total is 86,262,562 cases
in 723 batches. Missing SUBA and incomplete CMPA controls fail for their precise
reasons; unchanged frozen reports and the integer inventory pass. A first copied
fixture missing that inventory correctly failed and remains preserved. This
changes acceptance membership only, with no CPU fix or further test retirement.
See the [mandatory consolidation gate](COPPER68K_REFERENCE_QUALIFICATION.md#mandatory-arithmetic-consolidation-reports--2026-10-08).
Hardware is unavailable. Broader frame provenance, fault origins, trace and
reference requirements remain open. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

### Milestone 6 private cold pending-write images — 2026-10-08

Four optional reference-discovery batches now pass 163,584 private-contract
cases / 768 reports across EC020/A1200/020/030. Literal C023 frames after reset
exercise three locations, B/W/L, pending/DF-clear cycles, current ISP/MSP,
all four return banks, all CCRs and every zero-to-three saved instruction length.
Fourteen malformed/foreign/unsupported private image classes must reject before
stack/status/data commitment. Registers, all stack banks, guarded memory, exact
write/exception counts and three following instruction words are checked.

Wrong reserved-state acceptance, stack pop and flag recomputation are independently
detected; eight missing/changed evidence controls fail precisely. Frozen
revalidation passes. All 39 private CPU inputs are unchanged from the full suite
and `.73` consumer checkpoint; this adds only a fixture and maintained audit helper.
No CPU import, publication or old-test retirement occurs. These are bounded
software-protocol results, not hardware frame or trace qualification. General
foreign images, validation/load/entry faults, changed-bank refaults and broader
continuations remain required. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the [scope, command and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-cold-and-relocated-pending-write-frames--2026-10-08).

### Milestone 6 private pending-write frame faults — 2026-10-08

Two optional batches add 313,344 passing cases covering every byte and occurrence
of the selected C023 validation/load requests across all four 020/030 profiles.
They retain 163,584 cold-frame cases exactly: 476,928 cases / six executions /
1,152 reports. Validation faults preserve the original frame, serialize the
completed phase and recover through explicit RTE without repeating prior reads;
internal-load faults halt without committed stack/data effects or a second image.
Both outcomes check PC/SR, D/A registers, memory, banks, exception counts,
request order and resumed sentinels. CCR scope is 0/31 for this structural group.

Four mutations detect missing halt, early stack commitment, completed-read replay
and wrong saved PC with precise causes; ten corrupted evidence fixtures fail.
Frozen revalidation passes. An isolated production build confirms compilation
and two optional unavailable cases, not passing instruction coverage. All 39
private CPU inputs remain unchanged; no CPU correction, import, publication or
test retirement occurs. Supplied input, validation refaults/entry failures,
returned-bank pending-write refaults and broader architectural requirements remain
open. Milestone 6 remains **in progress**, `roadmapComplete=false`. See the
[frame-fault scope and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-pending-write-frame-validation-and-load-faults--2026-10-08).

### Milestone 6 broad-reference first-failure review — 2026-10-08

Fresh unfiltered Basic execution confirms 1,327 passing, 46 mismatching and eight
unsupported directories, zero untested, across 1,381 model/family rows. The audit
fails as required. A maintained [qualification ledger](COPPER68K_BASIC_REFERENCE_QUALIFICATION_LEDGER.json)
maps all 54 observed first failures: 40 reference-correction observations,
12 reserved/undefined input observations and two unresolved architectural questions
(010 RTE N/Z/V and 060 STOP S-clear). Qualified counterpart rows have exact
model/family, report identity, source and comparator-control bindings.

No raw directory is relabelled passing, and all later failures remain explicitly
unobserved. The validator rejects missing/unrelated mappings and fabricated
resolution; ten copied-ledger controls fail precisely. This completes the mapping
of the currently observed failures, not the entire independent-reference goal.
Combined reference execution, later-failure discovery, outstanding fault protocols
and architectural questions remain required. No CPU change, publication or
regression retirement occurs. Milestone 6 remains **in progress**,
`roadmapComplete=false`. See the [review scope and command](COPPER68K_REFERENCE_QUALIFICATION.md#basic-first-failure-qualification-ledger--2026-10-08).

### Milestone 6 composed broad reference execution — 2026-10-08

A separate `QualifiedBasic` corpus composes reviewed reference/input corrections
while preserving all Basic families and all eight execution profiles. Its fresh
run has 1,377 passing / four mismatching / zero unsupported / zero untested
directories. New 040/060 `F520` first failures are exposed behind the earlier
`F400` discrepancy; 010 RTE and 060 STOP remain unresolved. This is a new corpus,
not resolution of the old raw directories, and the broad gate still fails.
Exact composition, source/input/result bindings and ten corruption controls are
maintained alongside [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#composed-broad-reference-execution--2026-10-08).
No CPU source changes, candidate import, publication or retirement occur.
Milestone 6 remains **in progress**, `roadmapComplete=false`; neighboring
encoding/priority qualification, later-failure discovery and the existing
architectural/protocol requirements remain outstanding.

### Milestone 6 coprocessor-ID reference priority — 2026-10-08

The 040/060 `F520` discrepancy is qualified as a native-reference priority error:
only ID 1 identifies FPU state instructions on these models. Revision 2 limits
that reference check without changing Copper68k or excluding families; revision 1
remains reproducible. The fresh independent first-word matrix passes 445,248
cases in eight batches. The broad run still fails four directories, now exposing
`F628` reserved MOVE16 words behind `F520`; 010 RTE and 060 STOP remain. Twelve
corruption controls pass, and exact retained revision-1 evidence revalidates.
See [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040060-coprocessor-id-priority-qualification--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`. The new encoding and
adapter discrepancy, later failures and prior architectural/protocol gaps must
be addressed; no source candidate import or package publication occurs.

### Milestone 6 reserved MOVE16 first words — 2026-10-08

Unassigned `F628..F63F` words incorrectly raised vector 4 on 040/060. Their
documented Line-F classification is fixed without changing assigned transfers,
extension decoding or the fixed-cycle policy. The new ordinary gate executes
49,152 cases across eight profiles; it detects 12,288 failures before the fix
and passes completely afterward. The unchanged revision-2 broad corpus now
passes 1,379 directories, retaining the 010 RTE and 060 STOP mismatches. Eight
evidence-corruption controls reject invalid acceptance records. See
[the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#reserved-move16-first-word-qualification--2026-10-08).
The frozen preconsolidation full CPU suite passes 5,307 / 49 unavailable / zero
failures; independent verification binds 86,502,658 cases / 929 reports / ten native
presets, and the ordinary gate passes 86,311,714 cases / 731 batches. Fresh local
`.74` consumers pass Release build, 171 host / six unavailable, 74 disk, 1,080
engine and two supplied Workbench floppy replays, with independent source/package
bindings. The later consolidated full audit passes separately: 5,312 / 49
unavailable / zero failures, 86,600,962 cases / 937 reports / ten native presets.
Milestone 6 stays
**in progress**, `roadmapComplete=false`; hardware is unavailable for the separate
030/040 trace disagreement. No private CPU candidate import or publication occurs.

### Milestone 6 AND indirect consolidation — 2026-10-08

The three captured EC020 AND-indirect semantic rows are replaced by 98,304 shared
fixture cases across all eight profiles. Five causal mutations cover both executed
handlers, logical results, Extend preservation and partial-register preservation;
each fails the corresponding original row and exact predicted replacement cases.
An isolated retirement run passes all 39 sibling rows and eight replacement batches.
See [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#and-indirect-semantic-regression-consolidation--2026-10-08).
The ordinary CI gate now requires 12,288 `logical-and-indirect-captured` cases per
profile; the current full audit passes separately from the accepted
preconsolidation snapshot (5,312 passing / 49 unavailable / zero failures). A complete composed report-only fixture passes 86,410,018 cases / 739
batches; missing/incomplete AND controls reject. That check executes no instructions
and does not qualify the separate current full run. Its independent proof binds
86,600,962 cases / 937 reports / ten native presets; its current ordinary gate
passes 86,410,018 cases / 739 batches. Production CPU behavior,
specialized regressions and the
remaining reference requirements are unchanged. Milestone 6 stays **in progress**.

### Milestone 6 MOVEM mask access-fault qualification — 2026-10-08

Extend the previously qualified 040 physical MOVEM store/CM recovery fixture from
its captured four-register mask to independent register-list masks. The new
ordinary controls execute 55 masks, including empty/full masks, each singleton,
each omitted register, adjacent pairs and alternating data/address selections.
Every selected transfer is faulted for word/long indirect and predecrement stores
using A2, both scalar/batch routes, ISP and CCR 31. Empty masks execute without a
fault and must leave operands unchanged. All 2,728 new programs pass; nine bounded
tests also preserve the six earlier witness reports exactly.

Three private mutations qualify detection after CM return: lost initial-base
decrement (524 mismatches per route), omitted A7 replay (1,048), and wrong data
register replay (1,240). Independent enumeration checks every passing/failing
combination and its diagnostic category. The initial diagnostic verifier omitted
normal-mode missing-memory cases; its failure is retained and a successor verifies
the distinct memory, final-base and transfer-order outcomes.

The deep audit must execute all 65,536 mask words per route, both sizes and both
addressing forms. Each nonempty mask faults its last selected transfer; empty
masks are fault-free. This adds 524,288 whole programs. The unchanged earlier
143,512-program recovery matrix and 136 fault-free programs remain separate
required cohorts. The frozen complete audit passes **670,664 programs / thirteen
tests / twelve reports** with exact independently verified inventories and retained
report comparison. Eight altered-evidence controls reject; the ordinary report-only
gate passes 86,412,746 cases / 741 batches and rejects missing/incomplete controls.
The maintained command also passes the same complete gate from its separate
frozen 216-input snapshot; all twelve reports match the first audit. Complete
source/evidence linkage is recorded in the
[qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-movem-register-mask-access-fault-qualification--2026-10-08).
The prior wide full suite remains a separate 215-input execution; production
CPU sources remain identical, and no new wide full-suite run is claimed.

No production CPU source or package changes occur. This slice does not qualify
all mask/fault-position Cartesian combinations, other CCR/stack/EA combinations,
enabled MMU/cache, physical timing, or the unresolved hardware boundaries.
Milestone 6 remains **in progress**, `roadmapComplete=false`.

### Milestone 6 MOVES normal-space write recovery — 2026-10-08

Hardware is unavailable for the disputed 030/040 trace boundary; that
qualification gap remains open. The current software slice extends the existing
synchronous completed-MOVE write policy to actual MOVES operand writes. It
retains the bus-latched data/address and selected DFC, reports format 7/WB1,
and resumes after the store once an actual integer handler completes WB1.
No implicit instruction retry is permitted. Following-PC and pre/postincrement
effects qualify that execution policy, not physical pipeline behavior.

The bounded independent matrix executes byte/word/long D0 stores through (A0),
(A0)+ and -(A0), all four byte lanes, DFC 1/2/5/6, ISP/MSP, CCR 0/31, every
rejected operand byte and trace states 0/T1/T0. Each of **8,064 programs** checks
defined frame fields, every handler instruction, DFC/register restoration, RTE,
pending trace and a following MOVEQ sentinel. Every program fails against the
preceding CPU's format-zero path and passes with the correction.
All **93,952 read-fault programs** and **1,536 fault-free fixtures** retain their
previous complete reports. The maintained MOVES fault command separately passes
**5,120 cases**, closing its 1,792 remaining write-frame mismatches.

Three isolated mutations fail at their independently enumerated combinations:
raw DFC conversion (2,016 per route), lost T0 (1,344), and opcode return PC
(4,032). Original sources remain unchanged. These are new qualification cases;
no existing regression has been retired.

The complete frozen 219-input CPU audit passes **5,320 tests / 53 unavailable /
zero failures** (5,373 total), with 86,707,242 logical cases, 945 profile reports
and ten native presets. All 943 retained reports match the accepted predecessor.
The ordinary semantic gate passes 86,514,762 cases / 745 batches. Missing and
incomplete write reports reject; seven altered-source/input/assembly controls
reject, and the complete relocated fault evidence passes. A fresh local-only
`1.5.2-synthetic-dev.76` package preserves the NuGet consumer boundary. The clean
consumer archive passes the Release build, 171 host tests (six optional tests
unavailable), 74 disk tests, 1,080 engine tests and two native floppy replays;
loaded CPU DLLs match the package. It is not published. Special-space write
recovery, nested handler write faults, broader operand forms, enabled MMU/cache
and physical timing remain outside this
bounded gate. Complete source/evidence linkage binds the focused, full, mutation,
fault-frame, ordinary-control and consumer results without rewriting records that
were created while the full gate was pending. Milestone 6 remains **in progress**,
`roadmapComplete=false`.

### Milestone 6 nested integer writeback handler faults — 2026-10-08

Promote the captured normal-space nested-handler fixture with unchanged logic.
All **672 programs** pass and fail against the preceding MOVES write CPU; all
**103,552 accompanying MOVES programs** retain their architectural results.
The supplied three-slot outer format-7 frame drives actual integer WB1/WB2/WB3
handler faults, nested completion, restoration, RTE, remaining outer writes and
a following sentinel. B/W/L, four lanes, TM 1/5, ISP/MSP and each rejected byte
are covered; every instruction is checked and the three writes occur in order
exactly once. Three mutations detect missing register restoration, wrong word
data and wrong following PC without changing expectations.

Both new 336-case reports are mandatory in ordinary coverage. Missing/incomplete
controls reject; the composed report-only gate passes **86,515,434 cases / 747
batches**. The new bounded 220-input execution retains all 37 production CPU
inputs. The accepted 219-input full suite and local `.76` NuGet consumer gates
remain separate unchanged evidence; this slice claims no new wide full execution,
consumer replay or package release. Evidence identities and exact limits are in
[the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-nested-integer-writeback-handler-faults--2026-10-08).

Outer frame creation, mixed slot widths, all CCR states, trace/interrupt
interruption, other transfer spaces and physical pipeline behavior remain outside
this gate. MOVE16 write faults remain a failed private discovery. No old test is
retired. Hardware is unavailable at disputed boundaries. Milestone 6 remains
**in progress**, `roadmapComplete=false`.

### Milestone 6 MOVE16 physical line-write recovery — 2026-10-08

The physical MOVE16 write path now
captures the four values already read, constructs format 7 with MOVE16 transfer
attributes and pending line data, and retains synchronous following-PC/address
updates. Actual integer software completion writes all four saved long words,
restores registers and executes RTE without rereading the source or implicitly
retrying the instruction. Already completed operand stores remain intact; the
handler's explicit full-line writes are separately checked.

The final bounded 221-input fixture passes **41,600 MOVE16 programs** and retains
**104,224 MOVES/nested-handler programs**. The preceding CPU fails all **30,720
write-fault combinations**. Captured diagnostic messages are limited to 10,000
per route; all case IDs and statuses are independently verified. Four mutations
detect wrong line data, transfer type, return PC and lost address updates.
Review also exposed a test expectation sampled from live post-fault USP. The
corrected fixture uses the captured pre-fault expectation; a fifth corruption
control reproduces the former loophole and detects every affected write case.

The composed ordinary report-only gate passes **86,557,034 cases / 751 batches**
and rejects missing/incomplete MOVE16 reports. A fresh local-only `.77` NuGet
package passes the clean consumer Release build, 171 host tests (six unavailable),
74 disk tests, 1,080 engine diagnostics and two native floppy replays. Exact
source/package/loaded-DLL and archive/input/result linkage is independently
verified. No public package is released.

The complete frozen 221-input CPU audit passes **5,326 tests / 53 unavailable /
zero failures** (5,379 total), with **86,749,514 logical cases / 951 profile
reports / ten native presets**. All 945 retained reports match the accepted
predecessor; six added reports match focused evidence. The full audit uses V3;
the final V4 focused audit strengthens one test-only USP expectation and all
twelve focused reports remain byte-identical. Exact linkage proves the single
test-line difference and identical 37 CPU inputs across both revisions and `.77`.
This is not relabeled as a full execution of V4.

A fresh broad Basic replay with the final V4 fixture and current CPU reproduces
**1,379 passing / two mismatching directories**, 12,660,659 callbacks and
2,327,142 exception frames. The unresolved 010 invalid-format RTE flags and 060
S-clearing STOP behavior keep that separate gate failed. No family is excluded;
later failures within the two failing groups remain unobserved. See
[the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#040-move16-physical-line-write-recovery--2026-10-08).

MC68040UM 8.4.6.3 contradicts example 4 in 8.4.6.7 on WB1 validity; the bit remains
unqualified. Read transfer type also remains unqualified. Saved following PC and
address updates qualify the synchronous policy, not physical pipelines. Broader
register/alias/user-mode combinations, enabled MMU/cache and physical timing
remain outside this gate. No old regression is retired. Hardware is unavailable
at disputed boundaries. Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 NOT displacement consolidation — 2026-10-08

The shared captured NOT.L displacement fixture passes **24,576 cases** across
eight profiles, every address register, user/supervisor stacks and all CCRs.
The isolated lost-X mutation fails exactly **12,288 cases** and all four pinned
legacy rows. Exact removal preserves 71 sibling HDF rows and eight replacement
batches; nine altered-evidence controls reject. All eight 3,072-case reports are
mandatory. Missing/incomplete report controls reject; the complete composed
report-only gate passes **86,581,610 cases / 759 batches / 959 reports**.

This test-only 222-input snapshot retains all 37 production CPU inputs and the
separately recorded full/.77 consumer evidence. No new wide full-suite execution,
consumer replay, package release or specialized-regression retirement is claimed.
The [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#not-displacement-semantic-regression-consolidation--2026-10-08)
maps the exact replacement, defect and pinned evidence. Milestone 6 remains
**in progress**, `roadmapComplete=false`.


### Milestone 6 private pending-write validation continuation — 2026-10-08

The selected C023/C021 software contract adds **122,880 passing programs** for
original-value validation input and one explicit refault, with integer handlers,
exact read ordering, pending-store completion and following sentinels. All
**476,928 parent cases** retain their reports. Three isolated defects produce
**61,440 / 49,152 / 122,880** exact mismatches; all 233,472 diagnostic reasons are
independently checked. Eleven evidence controls reject and frozen strict replay
passes. The private 239-input graph preserves all 39 CPU inputs; the current
223-input production graph compiles the fixture with both new tests unavailable.

No production candidate is imported, package published or regression retired.
Changed validation values, entry failures, pending-write refaults, trace/interrupt
continuation and general hardware frames remain open. Exact graph/evidence limits
are in [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#private-pending-write-validation-input-and-refault--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 private validation exception entry — 2026-10-08

The selected C023/C021 software contract adds **51,200 passing cases** for
secondary stack/vector faults after validation failure. All **599,808 parent
cases** retain identical reports. Three isolated defects produce exactly
**51,200 / 51,200 / 1,280** mismatches; all **103,680** diagnostic messages are
independently checked. Eleven evidence controls reject and strict frozen replay
passes. The private 240-input graph preserves all 39 CPU inputs; current
production's 224-input graph compiles the fixture with two tests unavailable.

No production candidate is imported, package published or regression retired.
Changed validation values/protocol repairs, returned-bank pending-write refaults,
trace/interrupt continuation, wider origins/physical partial transfers and general
hardware frames remain open. Hardware is unavailable, so disputed native 030/040
boundaries remain unqualified. Exact scope and evidence are in [the qualification
record](COPPER68K_REFERENCE_QUALIFICATION.md#private-pending-write-validation-exception-entry--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 private changed validation input — 2026-10-08

The cold C021/C023 software contract adds **16,384 passing cases** for changed
SR/PC/same-format/end input via actual handlers, exact remaining read order,
pending-store completion and retained sentinels. All **651,008 parent cases**
retain their reports. Ignored input, replayed phases and lost M state are detected;
all **30,720** mismatch/unsupported diagnostics are independently verified.
Eleven evidence controls reject and strict frozen replay passes. All 39 private
CPU inputs remain unchanged; current production compiles both tests as
unavailable. No production candidate is imported or package published.

Malformed-protocol repair, wider pending/pipe forms, returned-bank write refaults,
trace/interrupts, physical partial transfers and general hardware frames remain
required. Exact scope and evidence are in [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#private-pending-write-changed-validation-input--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 private returned-bank write refaults — 2026-10-08

The cold C023 software contract adds **458,752 passing programs** for two
successive pending-store faults and explicit mapped/software completion, covering
B/W/L, all returned banks, saved FC 1/5, all CCR states, zero-to-three pipe words
and every rejected request byte. All **667,392 parent cases** retain their
reports. Four isolated defects produce exactly predicted mismatches; all
**1,462,272** failure messages are independently checked. Eleven evidence
controls reject and strict frozen replay passes. All 39 private CPU inputs stay
unchanged; production compiles two unavailable tests. No private candidate is
imported, package published or regression retired.

Actual original MOVE origins, physical partial transfers, refault entry faults,
trace/interrupts, malformed protocol repair and general hardware frames remain
required. Exact scope and evidence are in [the qualification record](COPPER68K_REFERENCE_QUALIFICATION.md#private-returned-bank-pending-write-refaults--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 private actual MOVE entry faults — 2026-10-08

Actual simple MOVE origins add **307,200 passing cases**, retaining **1,126,144
parent cases**. Separate opcode, value/CCR and entry groups cover all 384
supported words and rejected operand/stack/vector bytes. All four deliberate
defects are detected; **921,600** exact failure reasons, eleven evidence controls
and strict replay are verified. Sharding preserves every logical case and
failure witness. All 39 private CPU inputs remain unchanged; production compiles
two unavailable tests. No private CPU import, publication or regression retirement.

Wider operand forms, physical partial transfers, refault entry failures,
trace/interrupts, malformed protocols and hardware frames remain open. See
[scope and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#private-actual-move-secondary-entry-faults--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 EOR postincrement consolidation — 2026-10-08

The captured pure EOR byte postincrement fact is replaced by **32,768 passing
cases** across all eight profiles, every Dn/An, user/supervisor and all CCR states.
Before retirement, an extra-increment mutation failed both the original fact
and all replacement cases with exactly checked diagnostics. Nine evidence
controls passed. After exact one-fact removal, eight replacement batches and
all four retained address-error/trace siblings pass; strict replay and source/
assembly linkage pass. The ordinary gate requires 4,096 cases per profile.
All 37 production CPU inputs remain unchanged; no private CPU import or release.
See [the retirement mapping and evidence](COPPER68K_REFERENCE_QUALIFICATION.md#eor-byte-postincrement-consolidation--2026-10-08).
Milestone 6 remains **in progress**, `roadmapComplete=false`.


### Milestone 6 checkpoint — duplicate model-selection rejection, 2026-10-08

The seeded adapter and command now reject duplicate profile IDs. The old
`68020,68020` request passed while overwriting half its report paths. The guarded
fixture passes 3,200 valid cases across all eight profiles; two actual adapter
requests and both command modes reject duplicates before emulated execution or
output creation. Independent linkage verifies all 228 source inputs and the
unchanged 37 production CPU inputs. Full/deep reference runs retain their
separately identified pre-guard snapshot; no current full-suite success is
claimed. See the completion audit for exact evidence. Milestone 6 remains
**in progress**, `roadmapComplete=false`.


### Milestone 6 checkpoint — fresh six-defect MOVE proof, 2026-10-08

Fresh isolated 51444ea executions detect absolute decoding, extension length,
index sign, alias order, A7 stride and flags defects against a clean 75,214-case
68020 baseline. Each mutant changes one isolated CPU input; pushed CPU sources
stay unchanged. Independent source, loaded-assembly, exact-roster and report
verification is supplemented by six literal witnesses and all 8,712 diagnostic
ID-to-combination mappings. The current full run retains its pre-guard frozen
fixture and remains pending. See the completion audit for identities and scope;
milestone 6 stays **in progress**, `roadmapComplete=false`.


### Milestone 6 checkpoint — full run and six current MOVE mutants, 2026-10-08

The frozen c284b94 full run passes 5,337 tests, with 63 unavailable and zero
failures; 967 profile reports contain 86,806,858 passing logical cases. All ten
requested qualified WinUAE presets and the frozen ordinary report gate pass.
The exact roster and retained/new reports are independently linked. The newer
duplicate-selection guard retains separate focused evidence; no snapshot is
relabeled. Fresh isolated 51444ea mutation runs detect all six required MOVE
defects against a clean 75,214-case baseline, with exact source, assembly,
roster and report/witness linkage. All 37 CPU inputs remain unchanged; no new
consumer execution or package publication is claimed. See the completion audit
for identities and remaining gates. Milestone 6 stays **in progress**,
`roadmapComplete=false`.


### Milestone 6 checkpoint — later failed-reference cases, 2026-10-08

An isolated continuation bridge visits 6,248 010 RTE and 196,608 060 STOP cases
without changing the original fixture inputs or 37 CPU inputs. It retains
2,233 / 65,536 failing cases, with saved-frame/SR and exception/SR comparison
events respectively. Four clean/corrupted NOP controls validate the observer;
the discovery request still exits 1 and the original broad gate remains failed.
The complete native diff, pinned inputs, source graph, loaded assemblies,
named test and aggregate report are independently verified. V1/V2 failures are
rejected evidence; only V3 is qualified. Counts do not settle undocumented
hardware behavior, and no CPU change or reference exclusion follows. See the
completion audit for exact scope; milestone 6 stays **in progress**,
`roadmapComplete=false`.


### Milestone 6 checkpoint — ANDI displacement retirement, 2026-10-08

The captured long-immediate/displacement ANDI witness is replaced by 24,576
shared-fixture cases across eight profiles. A lost-X mutation fails all four
pinned original rows and exactly 12,288 replacement cases; every diagnostic,
key/weight and named selection is verified. Exact removal leaves all 67 HDF
siblings and eight replacement batches passing. Nine evidence controls and
strict replay pass. Ordinary CI requires 3,072 cases per profile, with actual
missing/wrong-weight controls rejecting. Its composed report-only gate passes
86,638,954 cases / 775 batches / 975 reports; this is not a new full-suite run.
The current graph has 229 source inputs and unchanged 37 CPU inputs. Original
full/deep/consumer identities and broader failed/private gates remain retained;
no publication follows. Milestone 6 stays **in progress**, `roadmapComplete=false`.


### Milestone 6 checkpoint — per-case reference witnesses, 2026-10-08

An isolated observer now retains all 202,856 disputed RTE/STOP cases and eight
clean/corrupted NOP control cases, with contiguous ordinals and initial,
reference and returned adapter images. Every 010 RTE mismatch has SR and saved
frame disagreement; every 060 STOP mismatch has vector and SR disagreement.
The 2,233 / 65,536 failure counts are unchanged. Reversing the observer additions
proves exact V3 comparison-code equality; complete input/source/native/assembly,
named failing test and per-case linkage are verified. The request still exits 1.
Source 3a5314f has 229 mainline inputs plus one isolated test, with all 37 CPU
inputs unchanged. No hardware qualification, guessed fix, exclusion, import or
publication follows. Milestone 6 stays **in progress**, `roadmapComplete=false`.


### Milestone 6 checkpoint — captured MOVEA displacement retirement, 2026-10-08

Shared MOVE fixtures now cover all captured aliased word MOVEA values with
every address register, user/supervisor state and CCR: 24,576 passing cases.
Zero extension fails all four pinned old rows and exactly 8,192 replacements;
every diagnostic, key/weight and named selection is verified. Exact removal
leaves all 63 HDF siblings and eight replacement batches passing. Nine evidence
controls and strict replay pass. Ordinary CI requires 3,072 cases per profile,
with actual missing/wrong-count rejection. The composed report-only gate passes
86,663,530 cases / 783 batches / 983 reports, without claiming a new full run.
All 37 CPU inputs remain unchanged in the 230-input graph; earlier full,
reference and consumer identities remain retained. Broader failed/private gates
remain open. Milestone 6 stays **in progress**, `roadmapComplete=false`.


### Milestone 6 checkpoint — retirement execution identity, 2026-10-08

The maintained retirement runner now requires schema-2 manifests with both CPU
DLLs and the test DLL, matching hashes and exact definition/method/path linkage.
Missing binaries and foreign paths were reproduced as accepted by the old
runner; a storage-only candidate's method gap was also reproduced. Fresh V2
MOVEA and EOR Clean/Mutation/Current executions preserve every prior case
report and named outcome, with twenty actual corruption controls per adapter
and strict replay passing. Old-schema replay rejects without altering retained
evidence. All 230 source inputs and 37 CPU inputs remain unchanged; no new
retirement, semantic scope, full-suite or consumer run is claimed. Broader
failed/private gates remain open. Milestone 6 stays **in progress**,
`roadmapComplete=false`.

### Milestone 6 checkpoint — current-build deep references, 2026-10-08

Fresh seeded and pinned software-reference execution against frozen `86469f7`
assemblies passes: 320,000 seeded cases / 32 reports, 312,500 SingleStepTests
cases / 125 files and 536 Musashi programs with 88 documented exclusions.
Independent verification binds all 230 source/project inputs, 37 unchanged CPU
inputs, loaded assemblies, exact named methods, corpus inputs and reports.
Proof: `293c7675e4f9e5f70b6b5fa602eab9ea0bc4b26724b553e6a477f5063883b64e`.
Eight fresh invalid requests against the same assemblies each fail exactly one
named test with the intended diagnostic: six missing/empty/invalid input checks
plus adjacent and separated duplicate models. Exact command, source, binary,
method, report absence and unchanged corpus linkage is independently verified:
`f344233c7317e1957dc436d0be12a4bca093bb167130ff8fc70741a2510e0140`.
The separate full CPU run is still pending. With no hardware available, disputed
030/040 boundaries remain unqualified; broad failed and private-restoration
requirements also remain open. Milestone 6 stays **in progress**,
`roadmapComplete=false`. See the reference qualification checkpoint for scope.

### Milestone 6 checkpoint — complete current evidence, 2026-10-08

The frozen `86469f7` full suite and independent verification finish successfully:
5,345 passing / 63 unavailable / zero failed tests, 86,856,010 logical cases /
983 reports and ten qualified WinUAE presets. The ordinary gate passes
86,663,530 semantic cases / 783 batches. Exact source, loaded methods, all named
results, report keys/weights/statuses and reference input sets are verified.
Full proof: `ee6dcf069e3747ea536bd3650ce66f0e727e51bb184b662d4df25cb4ac006980`.

Same-build deep/reference and eight rejection controls, unchanged-CPU .77
consumer results, six-defect MOVE baseline reports and schema-2 EOR/MOVEA
retirement graphs are linked without relabeling earlier execution identities.
All 230 source/project inputs and 37 CPU inputs remain unchanged. Linkage proof:
`fea989f4167448e1c35576e4269416a67d54b8b0a8f9f440a9f2b00d5f0368f4`.
This closes current snapshot coherence. The failed broad reference gate and
private-production restoration requirements remain open; milestone 6 stays
**in progress**, `roadmapComplete=false`, and PR #22 remains draft.

### Milestone 6 checkpoint — private metadata guards, 2026-10-08

A test-only C021 fixture adds 10,240 passing private return-frame software
controls across EC020, 020, 030 and A1200, with scalar/batch execution, both
supervisor stack banks, low/high addresses and all CCR values. Valid returns
execute a following-instruction sentinel. Three isolated guard removals produce
6,144 independently checked case IDs and diagnostics. Baseline and mutation
proofs are recorded in the reference qualification log, including the rejected
V1 label-format attempt and fresh corrected V2 execution.

The new 231-input focused graph is the earlier fully qualified 230-input graph
plus one test fixture; all 37 production CPU inputs remain unchanged. Earlier
full/deep/consumer results are retained under their original identities. This
does not qualify foreign hardware frames or close the broad reference and
restoration gaps. With no 030/040 hardware available, disputed trace boundaries
remain unqualified. Milestone 6 stays **in progress**, `roadmapComplete=false`.

### Milestone 6 checkpoint — maintained private audit, 2026-10-08

The maintained private C021 command snapshots and builds isolated sources,
reproduces 10,240 passing baseline cases and 6,144 exact mutation witnesses,
and supports strict replay. Seventeen corruption controls reject missing/empty
or coherently altered validator inputs at their intended guards, with positive
relocation controls and unchanged original evidence. Independent comparison
matches all focused source graphs and reports to the earlier qualified runs.
No CPU/test input changes accompany the tooling. The qualification record gives
commands, scope and proof identities. Milestone 6 remains **in progress**;
failed broad references, private recovery and hardware questions remain open.

### Milestone 6 checkpoint — combined candidate validation started, 2026-10-08

The isolated private candidate now preserves the current production MOVE16
correction and all current test inputs. Independent source reconstruction and a
38-test preflight pass: 1,641,472 cases / 1,628 reports. Compiled discovery proves
8,124 named cases, including deferred theory rows. The complete suite has started
on the same frozen assemblies with ten qualified reference presets. Completion,
independent full verification and affected-consumer qualification remain pending;
no private CPU code is imported. See the qualification record for identities,
preliminary attempts and scope. Milestone 6 stays **in progress**.

### Milestone 6 checkpoint — combined candidate consumers and references, 2026-10-08

The local-only .78 package preserves the NuGet consumer boundary and passes the
clean CopperScreen build, 171 host tests / six unavailable, 74 disk tests,
1,080 engine tests and two native floppy replays. Same-build deep audits pass
320,000 seeded cases, 312,500 SingleStepTests cases and 536 Musashi programs /
88 exclusions. Eight actual invalid requests reject before CPU execution.
Independent linkage binds the 258-input graph, loaded assemblies and 39 packaged
CPU inputs. Proofs and reference caveats are in the qualification record.

The full expected report catalog is frozen at 13,737 reports / 110,928,842 cases;
full execution remains running and its independent verification is pending.
No publication or private production import occurs. Milestone 6 remains
**in progress**, with broad reference, restoration and hardware gaps explicit.

### Milestone 6 checkpoint — combined candidate API review, 2026-10-08

Separate-process compiled API comparison and independent evidence verification
find zero managed public/protected signature changes: 20 types / 211 records in
both production and the combined candidate. A normalized six-file CPU source
patch is prepared for review without production import. The qualification record
states the exact scope and proof identities. Full-suite execution remains running;
unavailable hardware leaves disputed 030/040 boundaries unqualified. Milestone 6
stays **in progress**, `roadmapComplete=false`.

### Milestone 6 checkpoint — predecrement final-write correction, 2026-10-08

A new selected `MOVE -(An),(Am)` fixture passes 196,608 normal controls and
exposes 196,608 denied writes bypassing the physical map in the isolated private
candidate. A two-file private correction preserves the accepted source decrement,
captured value and existing general-EA timing policy across explicit RTE. Exact
independent verification qualifies 1,204,224 passing cases / 264 reports, including
811,008 unchanged retained cases. The test and reviewable candidate patch are
saved; production CPU code is unchanged. See the qualification record for scope,
evidence identities and patch replay caveats. Newer full/deep/API and consumer
qualification is pending; the earlier full run remains on its frozen inputs.
Milestone 6 stays **in progress**, `roadmapComplete=false`.

### Milestone 6 checkpoint — newer candidate integration, 2026-10-08

The 259-input predecrement candidate has exact 8,128-row compiled discovery and
a frozen 13,753-report / 111,322,058-case expected catalog. Its full run has
started on the selected-run assemblies; the earlier 258-input run continues
separately. Same-build deep references and eight actual invalid-request guards
are independently verified, as are zero managed public/protected API changes.
The fresh local-only .79 package passes clean CopperScreen build, 171 host /
six unavailable, 74 disk, 1,080 engine and two native floppy checks. Explicit
source/assembly/package linkage and caveats are in the qualification record.
Full execution and its independent verification remain pending; no private CPU
import or publication occurs. Milestone 6 stays **in progress**.

### Milestone 6 checkpoint — predecessor full qualification, 2026-10-08

The 258-input combined predecessor now completes independent full verification:
8,087 tests pass / 37 remain unavailable, with zero failures, 13,737 reports and
110,928,842 logical cases. The ordinary gate and ten qualified native presets
pass. Fresh linkage binds this result to its deep/guard, API and local .78
consumer evidence. The qualification record supplies proof identities and scope.
The newer 259-input predecrement full run is still pending and cannot inherit
the predecessor's result. Hardware is unavailable; disputed trace boundaries
remain unqualified. Broad reference and wider restoration gaps keep milestone 6
**in progress**, `roadmapComplete=false`; no private CPU import or publication.

### Milestone 6 checkpoint — predecrement read recovery, 2026-10-08

The next isolated private gap is reproduced and corrected: selected
`MOVE -(An),(Am)` read faults previously recorded an unknown continuation and
failed at RTE. The one-file candidate admits the accepted decrement and completes
only the pending suffix, preserving the mapped general-EA timing policy.
Independent verification qualifies 3,244,032 passing cases / 520 reports,
including 1,671,168 byte-identical retained cases. The new fixture covers all
source address registers, aliases, A7 stride, stack modes, CCRs, scalar/batch
routes and read/write/refault lanes. Its patch and replay are reviewable;
production CPU code is unchanged. Full/deep/API and consumer qualification of
this newer 260-input graph remain required. The earlier full audit continues on
its own frozen inputs. Broad reference, wider restoration and hardware gaps keep
milestone 6 **in progress**, `roadmapComplete=false`.

### Milestone 6 checkpoint — read candidate integration, 2026-10-08

The 260-input read candidate has exact 8,132-row discovery and a frozen
13,945-report / 112,894,922-case expected catalog. Full execution has started on
the focused-run assemblies. Independent deep/reference and eight rejection
controls pass, with zero compiled managed API changes. The local-only .80
package passes the clean CopperScreen build, 171 host / six unavailable,
74 disk, 1,080 engine and two native floppy checks. Source/assembly/package
linkage and the two later diagnostic-string differences are explicit in the
qualification record. Both running full candidates retain separate identities
and still require independent full verification. Broad reference, restoration
and hardware gaps keep milestone 6 **in progress**, `roadmapComplete=false`;
no private CPU import or package publication occurs.

### Milestone 6 checkpoint — portable private recovery audit, 2026-10-08

The selected private recovery audit can now be reproduced from the current
233-input checkout using a pinned combined patch, without a temporary
predecessor checkout. It runs an isolated 235-input snapshot with 39 private
CPU inputs and all 196 current test inputs. Fresh execution passes 14 named
tests / 3,244,032 cases / 520 reports; strict replay passes and independent
checks establish source equality and byte-identical pinned reports. Production
CPU inputs remain unchanged. The qualification record contains the maintained
commands and source/evidence identities. Broader full runs retain their own
frozen inputs and pending verification; the selected result cannot close
milestone 6, the broad failed reference gate or hardware gaps. Milestone 6 stays
**in progress**, `roadmapComplete=false`; no private import or publication.

### Milestone 6 checkpoint — mixed nested 040 writebacks, 2026-10-09

The shared supplied-frame handler fixture now varies each WB1/WB2/WB3 width
independently and initial CCR. All 24 mixed B/W/L triples pass across all 32
CCR values, four lanes, common FC1/5, ISP/MSP outer frames, each rejected byte
and pending slot, and scalar/batch routes: 172,032 new cases. Its two existing
same-width rows retain 672 cases and byte-identical reports. Four named tests
pass; independent source/assembly/method/report checks are recorded in the
qualification log. Production CPU code is unchanged. The 480-case broader
040 protocol gate remains untested and failing when requested; only its
coverage description is updated. Original construction, other CCRs for
same-width slots, heterogeneous FCs, user outer returns, deeper refaults and
trace/interrupt interruption remain required. This focused result does not
qualify the running private full suites or close milestone 6. Status remains
**in progress**, `roadmapComplete=false`; no import or publication.

### Milestone 6 checkpoint — complete private write-candidate evidence, 2026-10-09

The frozen 259-input / 39-CPU write-recovery candidate completes its full run:
8,091 passed / 37 unavailable / zero failed. Independent verification binds
all 8,128 rows, 13,753 reports / 111,322,058 logical cases, ten qualified native
presets and the ordinary 86,663,530-case / 783-batch semantic gate. Complete
linkage connects that full proof to the candidate's already qualified deep
references, eight request guards, compiled API and same-source local .79
consumers. These results qualify that frozen graph, not the newer read candidate
or later production-test extensions. Broad reference and restoration gaps keep
milestone 6 **in progress**, `roadmapComplete=false`; no private import or
package publication occurs.

### Milestone 6 checkpoint — supplied user returns and same-width CCRs, 2026-10-09

The shared nested-writeback fixture now distinguishes the supplied outer SR
from the running supervisor handler SR. A selected four-test slice passes
53,088 new cases: 20,832 remaining same-width CCR cases and 32,256 canonical
supplied user returns. Both scalar/batch routes cover all CCRs, lanes, common
FC1/5, each rejected byte/slot, nested supervisor service and final USP return.
Its 672 original controls remain byte-identical. Independent verification binds
all identifiers, exact named methods and source/assembly/report identities.
All 37 production CPU inputs are unchanged; earlier mixed-width evidence is
not relabeled as this later source execution. The qualification log records
scope, commands, identities and wider user-width/user-M/FC/refault/trace gaps.
All 480 broader protocol cases remain untested and fail their requested gate.
Milestone 6 remains **in progress**, `roadmapComplete=false`; no import or release.

### Milestone 6 checkpoint — remaining supplied user widths, 2026-10-09

Four selected tests pass 64,512 new cases for the eighteen remaining mixed-width
user triples and 672 retained controls with identical reports. Independent
verification checks exact case identifiers, source/assembly/method identities
and the unchanged shared execution fixture. All 37 CPU inputs match the retained
baseline after CRLF-to-LF conversion only; two raw identities changed during
checkout restoration. Earlier user and supervisor slices retain separate frozen
evidence. Their combined supplied-frame coverage includes all 27 B/W/L triples,
all CCRs, lanes, common FC1/5 and rejected bytes/slots on scalar/batch routes.
Original frame construction, user-M, heterogeneous FCs, deeper refaults and
trace/interrupt qualification remain open. The 480-case broader gate is retained.
Milestone 6 remains **in progress**, `roadmapComplete=false`; no import or release.
