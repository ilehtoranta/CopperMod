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
| 1. Framework and MOVE | In progress | Execute the legal data MOVE/MOVEA matrix on all seven models and A1200 EC020. Fix implementation and decode defects until every required case passes. |
| 2. Data transfer and address operations | Planned / untested | MOVEQ, MOVEM, MOVEP, LEA, PEA, EXG, EXT, SWAP using shared fixtures; unavailable instructions produce documented architectural outcomes. |
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
