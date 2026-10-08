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
| Coverage by architectural combination, with passing/mismatching/unsupported/untested distinct | Actual per-model report keys, weights and statuses; exact named TRX selection; no empty or substituted reports | Frozen 86469f7 full execution is qualified: 86,856,010 passing cases / 983 reports. All combination keys, weights and statuses match retained or focused reference reports. The ordinary gate passes 86,663,530 cases / 783 batches. |
| Deterministic coverage in ordinary CI | Current `test-copper68k-synthetic.ps1` and CI invocation; all required reports and positive case counts | Implemented, including 4,096 EOR postincrement and 3,072 ANDI / MOVEA displacement cases per profile. Frozen full execution and the separately qualified duplicate-selection guard are explicitly linked. |
| Explicit deep-audit command, deterministic seeds and strict missing/empty/mismatch rejection | Deep command/settings, recorded seed and selected inputs; rejected negative controls | Frozen 86469f7 seed 68020 runs 10,000 samples per family/profile: 320,000 passing cases. Eight same-build invalid/missing/empty/duplicate requests fail. Full and deep execution now share verified assembly identities. Mismatch rejection retains separate mutation evidence. |
| Reuse SingleStepTests, Musashi and WinUAE with pinned identities and caveats | Adapter source, reference manifests, source/input hashes, actual execution and documented exclusions | Implemented. SingleStepTests supplies 000 semantic fixtures; Musashi runs independent self-checking programs, not its CPU as an oracle. WinUAE remains a software reference. |
| Replacement detects absolute decoding, extension length, index sign, alias order, A7 stride and flags defects | Six distinct isolated mutation proofs with precise replacement IDs and current source linkage | The maintained mutation command defines all six. Fresh isolated 51444ea executions detect all six against a 75,214-case clean baseline. Exact source/roster/report linkage and every mismatch ID are verified. |
| Retire an old regression only after a mapped replacement detects its defect | Exact pinned old method/rows, before-removal proof, mutation witness and after-removal sibling verification | NOT displacement, EOR postincrement, ANDI displacement and aliased MOVEA word retirements have exact proofs. EOR replaces one fact with 32,768 passing cases and retains four siblings. Schema-2 retirement replay now requires execution DLL hashes and exact loaded definition/method linkage; fresh MOVEA/EOR executions, 20 corruption controls each and strict replay are qualified. No blanket regression deletion is authorized. |
| Retain cache, prefetch, bus ordering, fault sequencing, JIT and native regressions unless separately proven redundant | Exact test-source diff and full named roster, with every removal accounted for | Specialized tests remain. The exact 5,408-test frozen roster is compared with its predecessor: eight retired ANDI/MOVEA rows and sixteen replacement batches are accounted for. All 63 unavailable rows remain explicit. |
| Production CPU fixes pass full CPU and affected consumers | Same-source full CPU proof; isolated NuGet package; CopperScreen production, host/disk/engine and applicable native results | MOVE16 recovery has full-suite and local .77 consumer evidence. Subsequent consolidation leaves all 37 CPU source inputs unchanged. No new consumer execution is claimed. |
| Preserve successful ordering and timing policy; no automatic retry after partial effects | Relevant sequence/fault tests and change review, separate from timing qualification | Existing checks and scoped explicit-RTE continuation proofs remain. Private candidate results are not production behavior or physical timing qualification. |
| Test-internal framework; public factory; NuGet consumer boundary; immutable published versions | Source/project/package diffs and consumer dependency inspection | Preserved. Private 39-input CPU candidates remain separate from the production 37-input CPU graph. No publication is authorized by this audit. |

## Completion gates and evidence status

1. **The broad software-reference gate is failed.** The retained
   `QualifiedBasicMove16RecoveryV1` audit reports 1,379 passing directories and
   two mismatching directories, with actual test exit 1. The first failures are
   010 RTE invalid-format CCR and 060 ordinary STOP with a new S-clear SR. The original replay stops at those first
   failures. A separate isolated discovery now visits 6,248 RTE and 196,608 STOP
   callbacks, retaining 2,233 / 65,536 mismatching cases. A later isolated
   observer retains every passing/failing adapter image and proves per-case
   SR/frame disagreement for RTE and vector/SR disagreement for STOP, with
   unchanged comparison code and counts. Hardware expectations remain
   unresolved; these images are diagnostic evidence. Passing qualified presets
   do not turn this broad audit green.
2. **Private continuation work is not production qualification.** The 020/030
   read/write recovery candidates and their literal private formats have scoped
   software evidence but are not imported into the public CPU. The detailed
   record still lists wider restoration, operand, repair/refault, trace/interrupt
   and foreign-frame gaps. Preserve those distinctions when reviewing the
   promoted integer/system families.
   The isolated 258-input predecessor preserves the current production
   MOVE16 correction. Its 38-test / 1,641,472-case preflight is independently
   qualified, and exact discovery proves 8,124 complete-suite names. That full
   execution completes with 8,087 passed / 37 unavailable / zero failed.
   Independent full verification binds 13,737 reports / 110,928,842 cases,
   ten qualified native presets and the ordinary 86,663,530-case gate.
   This is candidate evidence, not production import or hardware qualification.
   The same candidate's local .78 consumer checks and same-build seeded,
   SingleStepTests/Musashi and eight rejection controls are independently
   verified. Source/package linkage is complete for these scopes. The full
   completed evidence is linked by
   `d9c5948d380cf3acb9ebe277b349a2c1a56b74d8546dbb9fcee27356a1a1b898`.
   A newer 259-input private predecrement final-write correction has selected
   1,204,224-case evidence and a reviewable two-file patch. Its same-build deep/API,
   eight invalid-request guards and local .79 consumers are independently verified.
   Exact source/assembly/package linkage is recorded in the qualification log.
   Its full replay now completes with 8,091 passed / 37 unavailable / zero
   failed. Independent verification binds all 8,128 rows, 13,753 reports /
   111,322,058 cases, ten qualified native presets and the ordinary gate.
   Complete linkage to its same-build deep/API/guards and same-source .79
   consumers is
   `3097ce893cfc9581dcb597b3f4c15232f7f42edc85e7c3f6d4a8c9584806924b`.
   This qualifies the frozen 259-input graph; it cannot qualify the later
   260-input read change or the current test extensions. No private production
   import occurs.
   The 040 access-frame discovery inventory retains all 480 required untested
   combinations (three banks, 32 CCR images, five protocol labels). Its diagnostic
   notes now acknowledge the separately qualified MOVES, nested supplied-slot
   writeback and MOVE16 fixtures without treating their bounded coverage as
   completion of those broader protocols. Only descriptions change; no case,
   status, enable flag or failure condition is removed.
   A subsequent 260-input private candidate also closes selected whole-request
   predecrement source-read recovery in focused execution: 3,244,032 passing
   cases / 520 reports, including 1,671,168 byte-identical retained cases.
   The new fixture and one-file review patch preserve the accepted decrement
   across explicit RTE, including read/write/refault lanes. Same-build deep,
   eight actual rejection guards, compiled API and clean local .80 consumers are
   independently qualified. Its own full suite has started with 8,132 names and
   a frozen 13,945-report / 112,894,922-case expected catalog. Full execution and
   independent verification remain pending; the running 259-input audit cannot
   qualify this newer change. Explicit linkage:
   `2e092d99a6116d1ebd0b95605b71c61d71c095d1f76c7b4ec511c95eb1db0915`.
   Two later 040 diagnostic strings are explicitly separated from the frozen
   inputs, with the same 480-case failing inventory retained. Production import
   and wider protocols remain open.
   A portable selected audit now reconstructs that private candidate from the
   current 233-input checkout in an isolated 235-input snapshot. The combined
   six-file review patch reproduces all 39 qualified CPU inputs; all 196 test
   inputs match the current checkout. Its fresh execution passes all 14 named
   tests and 3,244,032 cases in 520 byte-identical pinned reports, and strict
   replay passes. Independent linkage proof:
   `836e17c0f43e3565076aa8381320b704a5e21c106e6687544dbf1624d3dff263`.
   This supplies maintained reproduction without the temporary predecessor.
   It does not qualify the whole current suite or import the private CPU.
   A relocated valid evidence copy passes and all eight copied corruption
   controls reject the intended report/selection/method/assembly/source defects.
   The original evidence remains unchanged; no new CPU execution is claimed
   from those validator controls. Their proof is
   `8d9cfea6562c99b9b7662b443fa0a57f004318291d8f525837fb842800da92ae`.
   A later production-CPU fixture extension qualifies supplied mixed-width
   nested writebacks: all 24 B/W/L width triples with differing slot widths,
   all 32 CCR values, four lanes, common FC1/5, ISP/MSP outer frames, every
   rejected byte/slot, and scalar/batch execution. Four named tests pass with
   172,032 new cases and 672 byte-identical retained cases. Independent proof:
   `d41d476f7ef765c49c94fd7f6c180774ef84bbd32bd489cad6f3e9ffde56ac30`.
   All 37 production CPU inputs are unchanged. The later inventory description
   update changes no case generation or failing gate; all 480 required untested
   protocol IDs remain. Original slot construction, other same-width CCRs,
   heterogeneous FCs, user outer returns, deeper refaults and trace/interrupt
   interruption remain open. This focused result is not a whole-suite result.
   A further selected slice adds 20,832 same-width CCR=0..30 cases and 32,256
   supplied user-return cases (three same-width triples and six distinct-width
   permutations, all CCRs). Four named tests pass with those 53,088 new cases
   and 672 unchanged controls. Independent proof:
   `7decf4a860ff66b4f3911b2126b0760d7f042018090cbda8d1afac64242d0136`.
   It separates the supplied user SR from the supervisor handler SR, checks
   nested ISP service and the final USP switch. The earlier mixed-width matrix
   retains its own frozen evidence; it is not reexecuted by this slice. All
   37 production CPU inputs remain unchanged. A later diagnostic-only update
   retains all 480 cases and the failing gate. Other user-width combinations,
   user-M returns, heterogeneous FCs, deeper refaults, trace/interrupts and
   original slot construction remain open.
3. **Frozen snapshot coherence is verified.** The frozen `86469f7` graph has
   complete full-suite, ordinary-gate and same-assembly deep/reference/guard
   evidence. Schema-2 EOR/MOVEA retirement Current graphs match all 230 inputs;
   their reports match the full run. The six MOVE mutation baseline reports
   match full execution, and all 37 CPU inputs match the retained .77 consumer
   package. Proof: `fea989f4167448e1c35576e4269416a67d54b8b0a8f9f440a9f2b00d5f0368f4`.
   A later test-only C021 metadata fixture has separate focused evidence below;
   the full run still qualifies the original 230-input snapshot. Future source
   changes require renewed linkage. Gates 1 and 2 still prevent milestone completion.

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

## Later focused validation — 2026-10-08 (private metadata)

The later private C021 metadata checkpoint adds one opt-in fixture with 10,240
passing controls and 6,144 exact isolated mutant failure witnesses. It closes
the covered phase/SSW/returned-stack software guard gap; it does not qualify
foreign frames, private C023 production recovery or disputed hardware trace
boundaries. See the latest qualification record for scope and proof identities.
No production CPU input changes. Milestone 6 remains in progress.

The maintained `test-copper68k-rte-metadata.py` command reproduces the baseline
and all three isolated mutants, then strictly verifies sources, loaded methods,
assemblies, result counters and exact reports. Replay passes. The companion
integrity command rejects 17 corrupted copied validator inputs at their intended
guards and preserves original evidence. Commands and proof identities are in
the latest qualification record. This closes reproducibility for this focused
private software audit, without qualifying the broader unresolved behavior.

## Earlier frozen validation — 2026-10-08 (c284b94)

Current source checkpoint: `c284b948263501183136656307cab2ecce6e0479`.
The completed isolated `CurrentReferenceFullV1` run snapshots **228 test/CPU inputs**,
including **37 production CPU inputs** identical to the previous full-run CPU
sources, and enables the same ten qualified WinUAE presets. Its outputs are
local under `%TEMP%/copper68k-reference-restoration-20261006/audits/`.
It finishes with 5,337 passing / 63 unavailable / zero failed tests and
86,806,858 passing logical cases; exact evidence is recorded below. It does not include the separately failing broad Basic audit or
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

## Duplicate model-selection guards — 2026-10-08

The seeded MOVE adapter and command preflight now reject duplicate model IDs.
The frozen baseline request `68020,68020` incorrectly passed: it executed eight
groups but overwrote repeated report paths, leaving four reports. A request must
not silently substitute repeated execution for distinct profile coverage.

The isolated guarded build passes **3,200 cases in 32 reports**, using all eight
profiles, seed 68020 and 100 samples per family/profile. Adjacent and separated
duplicates each fail exactly one named adapter test before CPU execution, with
no reports. Both `-Deep` and `-ValidateReportsOnly` reject duplicates before
creating output directories. Exact source, loaded assembly, command, TRX roster,
report keys/counts and rejection diagnostics are independently verified. Only
one test source and the command preflight change; all **37 production CPU inputs**
remain unchanged. No package or consumer rerun is needed for this test-only fix.

The running full audit and the earlier 320,000-case deep/reference audit retain
their frozen `c284b94` fixture and gate. They are not relabeled as execution of
the guarded fixture. The focused guarded build supplies the separate evidence
for this sole test-source difference. At that guard checkpoint, the full audit
was pending; its completed result is recorded below.

| Guard evidence | SHA-256 |
| --- | --- |
| Reproduced unguarded baseline | `d3d3ba54eee5ae08a1259bd4ae5fd41e48ed703335f8f801dffd91ca24dd9c66` |
| Guarded valid and rejection executions | `f01b927d108dc7191ef16212f3d6ad0f2e606a271e3be05b92ec72c92a573751` |
| Independent complete source/evidence linkage | `788a9af242c2c0c07233d752bdb4d001b769deef8ee9b59b325d281f66936eaa` |

This closes the observed duplicate-selection gap, not the remaining milestone-6
reference disagreements or broader qualification requirements.

## Six current MOVE mutation witnesses — 2026-10-08

`CurrentMoveMutationsV1` refreshes the six required MOVE mutation checks using
isolated `51444ea` sources. Three clean 68020 batches pass **75,214 cases**.
Each deliberate defect changes exactly one CPU source in a separate copy;
the pushed production sources remain unchanged. Every intended named test runs
and fails with a concrete replacement witness. Independent verification checks
all 228 source inputs, exact mutation text, loaded assemblies, commands, rosters,
report statuses and complete combination weights. A separate literal-witness
check validates all six intended cases and maps **all 8,712 mismatch IDs** to
their report combinations, preserving the clean baseline's keys and weights.

| Mutation | Executed cases | Mismatches | Diagnostic at the pinned witness |
| --- | ---: | ---: | --- |
| absolute-decode | 9,726 | 8 | `PC expected 00001004, actual 00001006` |
| extension-length | 9,726 | 160 | `PC expected 00001004, actual 00001006` |
| index-sign | 7,120 | 288 | `SR expected 2708, actual 2704, mask=FFFF` |
| alias-order | 9,726 | 24 | `Memory 00004001: expected EE, actual 5A` |
| a7-stride | 9,726 | 8 | `A7 expected 00004702, actual 00004701` |
| move-flags | 58,368 | 8,224 | `SR expected 2704, actual 2700, mask=FFFF` |

| Evidence | SHA-256 |
| --- | --- |
| Complete source/execution/report linkage | `129af1d38941789f70c1cb845ea64d8e7a2de95364cf9dd5cc584400414a5995` |
| Six literal witnesses and complete diagnostic mapping | `6730dac567a7cae1cfb606337ccca0ec0805150416627af1c77a6a2665e1ebe9` |

This refresh proves detection of the six named defects on 68020. It does not
claim a new all-model mutation audit or physical qualification. The full CPU
run remains active on its separate frozen `c284b94` fixture. No production CPU
source, consumer package or retired regression changes in this checkpoint;
milestone 6 stays **in progress**, `roadmapComplete=false`.

## Frozen full-suite and refreshed MOVE mutation qualification — 2026-10-08

The complete `CurrentReferenceFullV1` run has finished: **5,337 passing tests,
63 unavailable tests, zero failures**, 5,400 total. Its 967 profile reports contain
**86,806,858 passing logical cases**, with zero mismatching, unsupported or
untested cases in those executed reports. All ten requested qualified WinUAE
presets pass. The exact roster accounts for the five retired NOT/EOR rows,
16 replacement batches and ten unavailable private-candidate tests added since
the preceding full snapshot. All 951 retained profile reports match that
snapshot; the 16 added reports match their focused consolidation evidence.

Independent verification binds every frozen source, command/settings, loaded
assembly, TRX outcome, report key/weight/status, native input and result to this
execution. The frozen ordinary report gate also passes. **Unavailable tests do
not supply coverage**, and the separately failed broad 010 RTE / 060 STOP audit
is not part of this passing selection.

The full and 320,000-case deep/reference runs use frozen `c284b94` sources.
At that full-run checkpoint, the sole subsequent test-source change was the duplicate-selection guard,
qualified by its separate 3,200-case build and four actual rejection requests.
The full audit's original gate is preserved and executed explicitly; it is not
relabeled as a run of the guarded fixture. All 37 production CPU source inputs
are identical across these snapshots and the retained local .77 consumer proof.

`CurrentMoveMutationsV1` independently refreshes the six required MOVE witnesses
on isolated `51444ea` sources. Its three clean 68020 batches pass **75,214 cases**.
Each deliberately changed production source is confined to one isolated copy;
the exact intended test runs and detects that defect. Source hashes, mutation
text, loaded assemblies, commands, named rosters, complete report weights and
recorded diagnostic witnesses are verified. A separate literal-witness check
also binds every one of the 8,712 mismatch IDs to its report combination and
proves unchanged baseline combination keys and weights. This demonstrates causal detection
for the six defects, not hardware qualification or a new all-model mutation run.

| Mutation | Logical cases | Mismatches | Replacement witness |
| --- | ---: | ---: | --- |
| absolute-decode | 9,726 | 8 | `68020/MOVE/1/(A0)->abs.w/canonical/op=11D0/v=89ABCDEE/ccr=00` |
| extension-length | 9,726 | 160 | `68020/MOVE/1/abs.w->(A0)/canonical/op=10B8/v=89ABCDEE/ccr=00` |
| index-sign | 7,120 | 288 | `68020/MOVE/1/index(A0)->D1/brief/D0/W/scale=1/d=-32/ignored-format=False/op=1230/v=89ABCDEE/ccr=00` |
| alias-order | 9,726 | 24 | `68020/MOVE/1/(A0)+->(A0)/canonical/op=1098/v=89ABCDEE/ccr=00` |
| a7-stride | 9,726 | 8 | `68020/MOVE/1/(A7)+->D0/canonical/op=101F/v=89ABCDEE/ccr=00` |
| move-flags | 58,368 | 8,224 | `68020/MOVE/1/D0->D1/boundary-ccr/op=1200/v=00000000/ccr=00` |

| Complete evidence | SHA-256 |
| --- | --- |
| Frozen full-suite, references and ordinary gate | `03b60c8348a83e75f7581cf663a4c6629ec5687a02e15c746c134472960885a8` |
| Fresh six-mutation source/execution/witness linkage | `129af1d38941789f70c1cb845ea64d8e7a2de95364cf9dd5cc584400414a5995` |
| Six literal witnesses and all 8,712 diagnostic mappings | `6730dac567a7cae1cfb606337ccca0ec0805150416627af1c77a6a2665e1ebe9` |

No CPU source changes, public package publication or additional consumer replays
are included. The unresolved broad-reference disagreements, private production
qualification and remaining consolidation requirements keep milestone 6
**in progress**, `roadmapComplete=false`.

## Later-case observation of the failed Basic groups — 2026-10-08

The original broad audit still fails at its first 010 RTE and 060 STOP
disagreements. Its native `continue_on_error` option only continues across
instruction directories; a separate compile-time switch stops each directory
at its first failure. Changing that option alone would not observe later cases.

The isolated `BasicContinuationDiscoveryV3` bridge visits the remaining cases
without altering input fixtures, CPU execution or reference comparisons. It
removes the two early exits, preserves aggregate failure across input files,
counts comparison events at 21 existing error sites and resets only diagnostic
buffer bookkeeping before each comparison. Observer calls retain the original
error statement's scope. An independent reconstruction verifies the complete
native/header diff; all 3,546 pinned input files, original DLL and 37 production
CPU source inputs remain unchanged. Its test-only graph has 229 source inputs,
including one local discovery fixture that is not imported into production.

| Profile / selection | Callbacks and validations | Failed cases | Comparison error events |
| --- | ---: | ---: | ---: |
| 010 RTE | 6,248 | 2,233 | 4,466 |
| 060 STOP | 196,608 | 65,536 | 131,072 |
| Each profile's clean NOP control | 2 | 0 | 0 |
| Each profile's corrupted NOP control | 2 | 2 | 2 |

The RTE aggregate records 2,233 SR and 2,233 saved-frame byte comparisons.
The STOP aggregate records 65,536 exception-vector and 65,536 SR comparisons. No other comparator sites fire in these two selections, and
there is no emulator-level unsupported execution. These are aggregate site
distributions, not retained independent architectural expectations or a complete
per-case register/frame witness archive. The controls establish that clean
execution remains successful and deliberately wrong results remain failures.

The actual discovery request exits 1 and its exact named xUnit test fails after
writing the report. It does not turn either disputed family or the original
broad gate green. The first compiler-environment failure and a subsequent native
crash with no usable result remain rejected attempts; only the repaired V3
execution is qualified. The crash motivated resetting diagnostic output per
comparison, and observer statements were also made explicitly scoped.

Complete source/native/input/assembly/TRX/report linkage is recorded in the
local `BasicContinuationDiscoveryV3/proof.json`, SHA-256
`64b51aec11c3568ad0c941d50d667ec4ceb287813e640146f5abba1bd360acaa`.
Software-reference disagreement still does not settle the undocumented flags
or STOP behavior. No CPU fix, reference exclusion, production bridge change,
consumer replay or package publication is selected from these counts.
Milestone 6 remains **in progress**, `roadmapComplete=false`.

## ANDI long-displacement consolidation — 2026-10-08

The four rows of `M68020HdfBootTests.AndiLongDisplacementConsumesLongImmediateAndPreservesExtend`
at source pin `ea18c2c0d2d5e2db69421091b7fc83d7b1fc7596` are replaced by
`SyntheticAndiDisplacementTests.CapturedLongAndiDisplacementConsumesImmediateAndPreservesExtend`.
The replacement executes **24,576 cases / eight profiles**, 3,072 per profile:
three original masks, both signed displacements, every address register,
user/supervisor stacks and all 32 initial CCR images. It uses the shared operand
fixture, public CPU factory and full architectural/memory verification. Literal
`02A9` encoding, independent operand address and exact next PC `1008` check the
long immediate followed by displacement. Expectations preserve X, derive N/Z
from the result and clear V/C without production arithmetic or decoding helpers.
The original A1 / supervisor / CCR-31 operand combinations are included;
surrounding-memory canaries use the shared fixture rather than the old AAAA/BBBB
literal bytes. This retires semantic rows, not native HDF boot coverage.

Before removal, all **71 original HDF rows plus eight new batches** pass.
A deliberate lost-X defect in the base and advanced ANDI execution paths fails
all four original rows and exactly **12,288 replacement cases**. Every mismatch
ID and diagnostic, combination key/weight and full named TRX selection is
checked against independently enumerated expectations. After exact removal,
all **67 retained HDF rows plus eight replacement batches** pass. Nine copied
evidence corruptions reject, and strict replay reproduces the complete proof.

The ordinary command requires all eight new reports, each with 3,072 passing
cases. Its composed **report-only** gate passes **86,638,954 semantic cases /
775 batches / 975 profile reports**, using the previously qualified full run
plus the focused new reports and unchanged integer inventory. Two actual
requests with a missing 000 report or wrong 000 case count reject. This is not
a newly executed whole CPU suite. All **37 production CPU inputs** remain
unchanged; the current test/CPU graph has **229 inputs**. Earlier full-suite,
deep/reference and local .77 consumer results retain their original identities.
No package or new consumer execution is claimed.

The first mutation attempt rejected CRLF-sensitive anchors before mutant
execution. A first composed gate also rejected an omitted inventory. These
attempts remain separate; the accepted V2 audit and complete V2 gate correct
the tooling inputs without changing expectations or weakening acceptance.

| Evidence | SHA-256 |
| --- | --- |
| Before-removal preparation | `86aafb2afc40d73d75b9fe95925cd15ca2d94885fb3ec04aeaee5df14aa0b4ec` |
| Complete Clean / Mutation / Current proof | `f16ce319c088dd400da0bbebbc3a986a28994857544bd2382702e7da818f122a` |
| Complete report-only ordinary gate | `a75ad1fec362627f69bf0040681f151b5e8163a3766392071707c0e970a29cb5` |
| Two actual required-report controls | `1c7b586342ecbed43f302ce4e3cd782920bb43ac1efb3307ea2c5e558e7fb9f3` |
| Independent source / retirement / binary / report linkage | `a412f3851eb369d0738b5f1e6fd191f0b19837984e21d30d6f60a4cdb91c1a08` |

```powershell
python scripts/test-copper68k-andi-displacement-retirement.py --output artifacts/andi-displacement-retirement
python scripts/test-copper68k-andi-displacement-retirement.py --validate-only --output artifacts/andi-displacement-retirement
```

The helper pins the original witness/class inventory and checks exact source
scope; use this checkpoint for reproduction if later retirements change that
class. The 010 RTE / 060 STOP broad gate and private-production restoration
requirements remain open. Milestone 6 stays **in progress**,
`roadmapComplete=false`.

### Current-build reference evidence, 2026-10-08

The frozen `86469f7` build now has independently verified fresh deep/reference
evidence: 320,000 seeded cases, 312,500 SingleStepTests cases and 536 Musashi
programs pass. Its 88 Musashi exclusions remain explicit. Proof SHA-256:
`293c7675e4f9e5f70b6b5fa602eab9ea0bc4b26724b553e6a477f5063883b64e`.
All 230 source/project inputs and 37 CPU inputs are bound to the executed
assemblies and named test methods. Eight fresh same-build invalid requests
reject with exact named failures and guard diagnostics, including duplicate
model IDs. Independent command/source/assembly/method/corpus linkage proof:
`f344233c7317e1957dc436d0be12a4bca093bb167130ff8fc70741a2510e0140`.
These controls generate no coverage reports. The same-build full-suite execution is
still pending; this checkpoint does not close the coherent final-evidence
requirement. No hardware is available for disputed 030/040 boundaries. The
failed broad gate and private-production restoration work remain required;
milestone 6 remains **in progress**, `roadmapComplete=false`.
