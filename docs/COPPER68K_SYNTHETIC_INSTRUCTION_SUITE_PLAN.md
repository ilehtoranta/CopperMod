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
| 3. Arithmetic and comparison | Planned / untested | ADD/SUB variants, quick/immediate/address/extend forms, comparisons, multiply/divide, decimal/packing operations; overflow, borrow, carry, sticky zero, exceptional operands. |
| 4. Logical, bit and shift operations | Planned / untested | Logical/immediate/unary operations, bit manipulation, shifts/rotates, bitfields, atomic integer operations; preservation and memory effects. |
| 5. Control and system operations | Planned / untested | Branches, conditions, calls/returns, stack frames, traps, privilege-sensitive transfers, STOP/RESET, model-specific integer/system instructions; exception frames, saved PC/SR, stack selection, interrupt/trace. |
| 6. Reference qualification and consolidation | Planned | Independent reference audits across selected models, gap review, retire proven redundant tests; publish architectural combination coverage, not just xUnit counts. |

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
