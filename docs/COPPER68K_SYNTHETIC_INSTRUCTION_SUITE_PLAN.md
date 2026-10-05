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
