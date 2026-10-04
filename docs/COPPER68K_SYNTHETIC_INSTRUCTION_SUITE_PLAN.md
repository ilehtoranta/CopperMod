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
