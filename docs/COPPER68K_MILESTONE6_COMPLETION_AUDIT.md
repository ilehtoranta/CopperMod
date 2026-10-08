# Milestone 6 completion audit

This is the current completion checklist for **Reference qualification and
consolidation**, derived from the accepted
[synthetic-suite plan](COPPER68K_SYNTHETIC_INSTRUCTION_SUITE_PLAN.md).
Milestone 6 is **in progress**. A passing focused audit, inventory entry or
software-reference correction does not complete the milestone.

The detailed [qualification record](COPPER68K_REFERENCE_QUALIFICATION.md)
retains historical checkpoints and rejected attempts. This checklist separates
completion requirements from the many bounded experiments recorded there;
it does not replace or discard their remaining gaps.

## Required evidence

| Requirement from the accepted plan | Authoritative evidence needed | Current assessment |
| --- | --- | --- |
| Independent audits across 000, 010, EC020, 020, 030, 040, 060 and A1200 | Pinned reference inputs and adapters; executed selections for every profile; explicit exclusions and comparator checks | Fresh SingleStepTests 000 and Musashi eight-profile audits pass on the frozen current assemblies. The broad WinUAE audit still has two unresolved mismatching directories. |
| Complete integer-family inventory; later families cannot disappear | `IntegerInventory.cs`, generated model/family inventory and named execution reports | Inventory and reporting are implemented. Inventory membership alone does not prove execution or internal restoration coverage. |
| Coverage by architectural combination, with passing/mismatching/unsupported/untested distinct | Actual per-model report keys, weights and statuses; exact named TRX selection; no empty or substituted reports | The ordinary gate validates required groups and counts. A fresh current-source full run is underway; its result is not yet qualified. |
| Deterministic coverage in ordinary CI | Current `test-copper68k-synthetic.ps1` and CI invocation; all required reports and positive case counts | Implemented, including 4,096 EOR postincrement cases per profile. Final current-source execution linkage remains pending. |
| Explicit deep-audit command, deterministic seeds and strict missing/empty/mismatch rejection | Deep command/settings, recorded seed and selected inputs; rejected negative controls | Fresh seed 68020 runs 10,000 samples per family/profile: 320,000 passing cases. Six actual invalid/missing/empty requests fail. Mismatch rejection has separate mutation evidence; retain final source/evidence linkage. |
| Reuse SingleStepTests, Musashi and WinUAE with pinned identities and caveats | Adapter source, reference manifests, source/input hashes, actual execution and documented exclusions | Implemented. SingleStepTests supplies 000 semantic fixtures; Musashi runs independent self-checking programs, not its CPU as an oracle. WinUAE remains a software reference. |
| Replacement detects absolute decoding, extension length, index sign, alias order, A7 stride and flags defects | Six distinct isolated mutation proofs with precise replacement IDs and current source linkage | The maintained mutation command defines all six. Historical proofs need final source/evidence linkage; a mutation definition is not execution evidence. |
| Retire an old regression only after a mapped replacement detects its defect | Exact pinned old method/rows, before-removal proof, mutation witness and after-removal sibling verification | Recent NOT displacement and EOR postincrement retirements have exact proofs. EOR replaces one fact with 32,768 passing cases and retains four siblings. No blanket regression deletion is authorized. |
| Retain cache, prefetch, bus ordering, fault sequencing, JIT and native regressions unless separately proven redundant | Exact test-source diff and full named roster, with every removal accounted for | Specialized tests remain. Refresh the complete current roster and compare it with the previous frozen full run. |
| Production CPU fixes pass full CPU and affected consumers | Same-source full CPU proof; isolated NuGet package; CopperScreen production, host/disk/engine and applicable native results | MOVE16 recovery has full-suite and local .77 consumer evidence. Subsequent consolidation leaves all 37 CPU source inputs unchanged. No new consumer execution is claimed. |
| Preserve successful ordering and timing policy; no automatic retry after partial effects | Relevant sequence/fault tests and change review, separate from timing qualification | Existing checks and scoped explicit-RTE continuation proofs remain. Private candidate results are not production behavior or physical timing qualification. |
| Test-internal framework; public factory; NuGet consumer boundary; immutable published versions | Source/project/package diffs and consumer dependency inspection | Preserved. Private 39-input CPU candidates remain separate from the production 37-input CPU graph. No publication is authorized by this audit. |

## Gates that still prevent completion

1. **The broad software-reference gate is failed.** The retained
   `QualifiedBasicMove16RecoveryV1` audit reports 1,379 passing directories and
   two mismatching directories, with actual test exit 1. The first failures are
   010 RTE invalid-format CCR and 060 ordinary STOP with a new S-clear SR. Later
   failures in those directories remain unobserved. Passing qualified presets
   do not turn this broad audit green.
2. **Private continuation work is not production qualification.** The 020/030
   read/write recovery candidates and their literal private formats have scoped
   software evidence but are not imported into the public CPU. The detailed
   record still lists wider restoration, operand, repair/refault, trace/interrupt
   and foreign-frame gaps. Preserve those distinctions when reviewing the
   promoted integer/system families.
3. **The final evidence set must be coherent.** Link the current complete
   source graph, roster, ordinary reports, references, seeded audits and
   consolidation witnesses. Do not substitute the latest focused test for a
   full suite, relabel an unavailable test as coverage, or combine different
   fixture revisions as one executed run.

For ordinary STOP, [M68000PM 6-85](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
describes the incoming supervisor check followed by SR transfer and stopping.
[MC68060UM 8.2.5/8.2.6](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
defines incoming privilege and trace processing. LPSTOP has an explicit new
S-clear rejection rule; these sections do not explicitly establish that rule
for ordinary STOP. The pinned WinUAE implementation labels its STOP rule
undocumented. The disagreement therefore remains unresolved; no guessed CPU
restriction or reference exclusion is justified by this review.

No hardware is available for the disputed native 030/040 trace boundaries.
Those observations remain unqualified. This does not make other software work
blocked, and software agreement must not be described as hardware evidence.

## Scope boundaries

The accepted roadmap excludes FPU arithmetic, enabled-MMU operation, physical
pipeline/cache qualification and OS compatibility. Physical bus/pin/timing
claims remain separate from semantic and timing-policy results. This checklist
does not add those excluded topics as milestone completion requirements.
Conversely, documented architectural exceptions, legal instruction semantics,
saved PC/SR, stack selection and promoted system-operation behavior remain
within the plan; a software-reference caveat cannot hide an implementation gap.

Diagnostic 010/060 qualification remains separate from desktop readiness.
Publication is a separate release step. PR #22 remains a draft, and
`roadmapComplete=false` until a requirement-by-requirement final audit proves
completion.

## Current validation work — 2026-10-08

Current source checkpoint: `c284b948263501183136656307cab2ecce6e0479`.
The fresh isolated `CurrentReferenceFullV1` run snapshots **228 test/CPU inputs**,
including **37 production CPU inputs** identical to the previous full-run CPU
sources, and enables the same ten qualified WinUAE presets. Its outputs are
local under `%TEMP%/copper68k-reference-restoration-20261006/audits/`.
The run is still in progress; neither success nor final logical counts are
claimed here. It does not include the separately failing broad Basic audit or
private CPU imports.

`CurrentDeepReferencesV1` has completed against those **same frozen assemblies**:
three named tests pass, with 320,000 seeded cases in 32 reports, 312,500
SingleStepTests cases in 125 files and 536 passing Musashi programs across all
eight profiles. Musashi retains 88 explicit exclusions. Complete pinned input
sets (127 SingleStepTests files including exclusions and 78 Musashi binaries),
source and binary hashes, command/settings, loaded test paths, exact TRX roster,
report keys/weights/statuses and per-file/per-program outcomes were independently
verified. This does not qualify physical timing or every instruction combination.

`CurrentDeepRejectionControlsV1` executes six actual failing adapter requests:
zero seed, zero samples, empty model selection, missing SingleStepTests corpus,
empty SingleStepTests filter and missing Musashi root. Each executes exactly one
named failing test with its expected diagnostic before emulated CPU execution;
original input and assembly hashes remain unchanged. These are adapter guards,
not a claim to have tested every CLI guard or every possible malformed input.

| Current deep evidence | SHA-256 |
| --- | --- |
| Complete seeded/reference proof | `6bf1ae0d33a92445ea1387580c4d09bff3f944627e51569fd94f49a0cca55f6b` |
| Six actual rejection controls | `4e80db7425c2b65ca3dcb9dba14ce45786d1a1a30d3527f784c8c3ab245a0c8a` |
