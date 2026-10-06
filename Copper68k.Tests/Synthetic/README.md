# Synthetic integer instruction suite

The accepted [implementation plan](../../docs/COPPER68K_SYNTHETIC_INSTRUCTION_SUITE_PLAN.md)
defines the six milestones. The suite uses the public factory and test-internal
architectural specifications, a sparse 32-bit recording bus, independent operand
fixtures and a common register/PC/SR/memory verifier. It never calls production
decoders, arithmetic, effective-address or timing helpers to compute expectations.

## Deterministic gate

From the CopperMod root, run:

```powershell
./scripts/test-copper68k-synthetic.ps1
```

Milestones 1–5 execute MOVE/MOVEA, transfer/address, arithmetic/comparison,
logical/bit/shift/atomic and control/system operations in
**8,761,672 logical cases** across **386 reporting xUnit batches**, across seven models and the A1200 profile.
Ordinary `dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release` includes
these batches. CI additionally validates every required report and exact count;
missing reports, mismatches, unsupported execution and empty groups fail the gate.
The following instruction sentinel verifies extension consumption.

The `logical-invalid-operands` group covers 207 assigned illegal opcode words
per profile: ORI/ANDI/EORI destinations and static/dynamic bit-operation
destinations, across both stacks and all 32 CCR inputs (13,248 cases per profile).
It checks preserved registers/CCR, saved fault PC/SR, exception frames and memory
canaries. CCR/SR immediate forms, dynamic BTST's legal immediate source and MOVEP
encodings remain in their legal groups. Unassigned mode-7 registers 5..7 are
outside this added matrix; their inclusion in a reference ILLEGAL directory does
not by itself establish complete illegal-opcode qualification.

The same test-internal `InvalidOperandScenario` now drives immediate-arithmetic
and CAS invalid forms. `arithmetic-invalid-operands` covers 99 words on 000/010
(6,336 cases per profile), or 93 on 020+ (5,952 cases), preserving legal
PC-relative CMPI on 020+. `logical-cas-invalid-operands` covers 54 words and
3,456 cases per profile. CAS2.W/.L have separate legal coverage; the unassigned
byte-CAS2 word and unassigned EA registers are outside these added matrices.

`system-status-invalid-operands` covers 38 assigned illegal status-transfer
words (2,432 cases per profile). `system-moves-invalid-operands` covers 57
illegal B/W/L operand words with valid load/store extensions (7,296 cases per
profile). Both reuse the canary fixture, both stacks and all 32 CCR inputs,
checking vector 4 before privilege or operand effects. Legal status/MOVES forms
remain in their existing groups; unassigned mode-7 registers and the CAS.L size
field are excluded from these matrices. Normal opcode prefetch is allowed.
The report summary derives its batch count from validated report identities
and counts instead of a hardcoded total. Nonreporting fixture/inventory checks
and opt-in audits are separate from these logical-case batches.

The additional 040-only `system-mmu-disabled` group qualifies single-word
PFLUSH/PTEST decoding, privilege, CCR preservation and trace behavior with
translation disabled. MMUSR is undefined for disabled-MMU PTEST and is not
asserted. Enabled MMU operation remains outside the integer-family gate;
see the [qualification boundaries](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md).

The 000-only `system-double-fault` group adds 640 cases for address-error
entry, bad supervisor stacks, odd handler addresses, trap/interrupt entry,
CCR/trace preservation, halted bus inactivity and reset recovery. It preserves
the first frame and verifies that a fault in a successfully entered handler is
a new exception. External BERR and reset-vector fault signaling are unavailable
through the current public bus API. The separate compiled-JIT warm-cache gate now covers 180 scenarios in 15
batches, including guards that dispatch odd accesses before operand effects.

The 010-only `system-format8-entry` (1,024 cases), `system-format8-rte`
(4,096) and `system-format8-double-fault` (256) groups qualify structural
prerequisites: the 58-byte address-error frame, 26 word writes with reserved
holes untouched, scoped read/write/fetch metadata, version rejection before
popping, tail probing before remaining reads, stack selection and halt/reset
recovery. Emulator version zero and internal images are private conventions.

Five 010 `system-move-word-restart-*` groups add 127,776 cases for generated
word-MOVE/MOVEA images: source faults 62,208; destination faults 64,512; copied/
nested/alias/prefetch/trace edges 640; user A7 192; malformed private images 224.
RR=0 retries only the stacked word cycle; RR=1 uses the input image or accepts
a software-completed write. Earlier operand reads and register effects are never
replayed. The private image contains continuation phase and completed prefetch
words, so copied frames need no side table. Long transfers, non-MOVE families,
foreign hardware internal images, RMW, external BERR and physical restart timing
remain unqualified; this is not full format-8 restart qualification.

Reports contain per-scenario identifiers and counts for passing, mismatching,
unsupported and untested outcomes. `integer-inventory.json` lists all integer
families and their model-specific architectural outcomes. The validated
`qualified-inventory.json` promotes only completed milestone families; later
families remain explicitly untested. Reserved/undefined full extensions and
excluded FPU/MMU/physical timing work are identified separately. Diagnostic
68010/68060 results do not qualify desktop readiness.

MOVE coverage is separated into opcode enumeration (9,726 legal words per
profile), boundaries/CCR, extensions/aliases, overlapping operands, external
address boundaries, invalid opwords and alignment. Full indexed fixtures cover
66 legal structural combinations. Transfer tests reuse the same fixtures for
LEA/PEA and MOVEM, and cover spaced MOVEP bytes, masks, aliases, model exceptions,
register encodings and preserved CCR bits.

## Seeded and external audits

```powershell
./scripts/test-copper68k-synthetic.ps1 -Deep -Seed 68020 -Samples 10000
./scripts/test-copper68k-synthetic.ps1 -Deep -Models 68000,68020 -Seed 123456 -Samples 20000
```

Recorded xorshift32 seeds add MOVE, arithmetic, logical and control samples without expanding the deterministic
Cartesian product. Zero seeds, empty/unknown model selections and nonpositive
sample counts are rejected. Each invocation uses a fresh report directory.

External audits reuse existing adapters; append the appropriate switches:

```powershell
-SingleStepPath <SingleStepTests-directory>
-MusashiPath <pinned-Musashi-checkout>
-WinUaePath <generated-cputest-directory> -WinUaeLibrary <native-tester.dll> -WinUaeSourceCommit <40-hex-generator-commit>
```

Requested references must exist, select inputs and pass. Input SHA-256 identities,
source revision and native library identities are recorded. Musashi is pinned to
`72c1d74800f3087b45a0c1a7342601bbed898881`. SingleStepTests is a MAME-generated
software oracle, not a hardware capture. The available WinUAE integer adapter is
68000-specific; its generator's broader model support does not establish adapter
or input qualification. Unrequested or unavailable references are not passing
coverage. Cross-model external qualification remains milestone 6 work.

Specifications use [M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf),
[MC68000UM](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf),
[MC68020UM](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf),
[MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf), and
[MC68060UM](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf).
Arithmetic expectations use mathematical ranges and BigInteger multiplication/division,
not production semantics. The matrix covers all sizes and legal EA forms, all 32
CCR states, all quick counts, signed/scaled/full indexes, register/stack aliases,
alignment and address boundaries. ADDX/SUBX verify sticky zero in register and
predecrement memory forms; CMPM verifies source effects before destination reads.
Decimal tests exhaust all valid 00..99 input pairs and mask undefined N/V flags.
PACK/UNPK cover adjustment-word wrapping, memory byte ordering and A7 strides.
Divide overflow masks undefined N/Z; zero divide masks undefined N/Z/V while
requiring preserved X and cleared C. Early illegal long forms and 060 removed
64-bit operations trap before EA effects. Zero divide verifies saved next PC,
format-2 instruction address on 020+ and RTE in both stack modes.

## Replacement proofs and retained tests

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Arithmetic
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Logical
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Control
```

Twenty-two isolated mutations prove detection of absolute-address decoding, extension
length, signed indexes, source/destination alias order, A7 byte stride and MOVE
flags, arithmetic overflow, extend sticky zero, decimal alias ordering, PACK A7
stride and the 060 divide frame. Additional proofs cover bitfield V/C clearing,
negative offsets, CAS2 alias precedence and 040 failed writeback, CHK2 boundary Z,
CMP2 address width, RTE throwaway frames, MOVE16 postincrement and CACR command
readback. The command restores source in `finally`, rebuilds it and records each
mutation, precise failing replacement case and source hash. A compilation failure
does not count as detection. Do not run it concurrently with a build or edit of
the same production file.

No specialized regressions have been retired. Existing focused MOVE tests whose
unsupported boundary became legal execution were updated to assert the result.
Cache, prefetch, detailed fault ordering, bus timing, JIT and native media tests
remain separate. These semantic results preserve existing timing policy; they do
not certify physical timing or OS compatibility.

## Control and system qualification boundary

Normal, throwaway and postinstruction RTE frames verify saved SR/PC, active and
inactive stack banks, format errors and privilege ordering. Trace tests cover
T1, T0 on applicable models, taken/untaken control flow, completed traps,
aborting exceptions and trace changes at batch boundaries. Interrupt tests cover
IPL, level 7, STOP wakeup, VBR and paired MSP/ISP frames with RTE restoration.
MOVEC verifies model selectors and masks; MOVES covers memory and register effects.
All five MOVE16 forms, cache-instruction privilege and LPSTOP are included.
CALLM/RTM cover 020 module frames, both argument options and an isolated internal
CPU-space access-control responder. An ordinary bus has no such responder and
produces a format error for type-1 modules; ordinary RAM is never used as one.

Detailed RTE bus-fault restart/internal-state restoration on 010/020/030,
020/030 coprocessor midinstruction restoration and detailed 040 access-fault
entry/validation, writeback handlers and CP context transfer remain **untested**. They are
listed separately in report qualification boundaries rather than reported as
invalid frame formats or passing coverage. External BKPT instruction replacement,
physical MOVES function-code buses and LPSTOP CPU-space broadcast are also
unavailable qualification. Cache/prefetch/fault/JIT/native suites remain retained;
no physical pipeline/cache, FPU arithmetic, enabled MMU or OS qualification is
claimed. Undefined CAS2 overlapping-memory results, MOVES storing its own updated
address base, reserved module fields and simultaneous T1/T0 are excluded.

## Milestone 6 reference work

The pinned Musashi command now audits all selected profiles across both integer
program directories. It records passing/excluded programs and input identities;
missing/incomplete fixtures and mismatches fail. See
[reference qualification](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md)
for the 538 passing programs, 86 explicit exclusions, 040 T0 correction and
ASL regression replacement proof. Run the consolidation mutations with
`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Consolidation`.
Milestone 6 remains in progress; remaining implementation/reference gaps are
listed explicitly in that document.

The pinned SingleStepTests command now executes 312,500 68000 instruction-body
cases in 125 files, with TAS/TRAPV explicit upstream exclusions. It records
per-file hashes and case counts and rejects incomplete/mismatching requests:
`./scripts/test-copper68k-synthetic.ps1 -SingleStepPath artifacts/reference-singlestep`.
See the reference document for the pin, trace-boundary adaptation and the LINK A7
disagreement that corrected both production behavior and a synthetic expectation.
The existing stack gate distinguishes 040 early decrement from the other models;
its logical count is unchanged. No new test retirement or hardware qualification
is claimed.

The 000 compiled-JIT alignment follow-up is retained in
`M68000JitDirectZeroWaitTests`: 180 logical warm-cache scenarios in 15 ordinary
xUnit batches. Instruction-boundary guards transfer word/long, stack and
JMP/JSR alignment faults to the accurate interpreter before operand effects.
These JIT regressions are reported separately from the synthetic inventory;
no cache/prefetch/bus-ordering regression is retired. See the reference
qualification document for reproduced failures, guard-removal proofs and scope.

The additional pinned Windows WinUAE model audit and preparation command are
documented in [WinUAE conformance](../M68kWinUaeCpuTesterConformanceTests.md#pinned-integer-audit-across-cpu-models-milestone-6-checkpoint).
It is an opt-in discovery gate that currently reports unresolved mismatches;
it does not replace the deterministic synthetic gate or complete milestone 6.

The independent 040 access-frame discovery command is
`./scripts/test-copper68k-040-access-frames.ps1 -OutputDirectory artifacts/040-access-frame-audit`.
It currently **fails** on 480 explicit fault/context requirements. Normal,
CT, CM, CU and CP restoration, chained throwaways and direct odd-PC returns now
pass 1,124,352 logical phases, including short-frame and odd-fetch controls.
Twelve promoted batches run in ordinary CI. CP uses the
original suspended delivery vector; context-transferred frames without that
state remain an implementation gap. The MOVEM
matrices cover every legal opcode word and all 66 full-index structures;
separate state/JIT regressions check reset, interrupts, nested FPU delivery,
completed operand-store preservation and warmed V1/V2 traces. These fixtures do
not certify hardware access-fault generation or enabled-MMU operation.
Failed prerequisites leave later phases untested.
The throwaway matrices independently track all three stack pointers through one
or two discarded frames, every start/intermediate/tail/result bank selection,
even/odd data-stack addresses, all CCRs and T0/T1/no trace. Discarded odd PCs are
never fetched; terminal short/access frames preserve guarded memory and pending
writebacks. CM's following MOVEM uses the saved EA. The maintained consolidation
mutation command proves early chain termination and missing stack selection are
detected, then restores and rebuilds source. Detailed validation/access faults,
chained odd-PC saved-SR provenance, real access-fault entry, writeback handlers and CP context
transfer remain required; no regression is retired by this checkpoint.
Direct short/normal/CM odd returns now raise a format-2 address error during RTE,
with the causing instruction PC and fault address A0 cleared. CT/CU/CP delivery
takes priority; its handler return then produces the address error. The matrices
check both return phases, explicit software repair and the following instruction.
Saved-SR image ordering follows documentary pinned WinUAE source rather than an
executed hardware oracle; chained user-tail provenance remains an explicit gap.
Separate accurate/V1/V2 tests run 540 state scenarios and witness warmed compiled
dispatch followed by RTE fallback. Run the four discriminating mutations with
`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Rte040`.
The command checks exact selections, combinations, counts and source/assembly
identities. Existing outputs must be validated with `-ValidateReportsOnly` or a
fresh output directory used. It cannot pass merely by fixing normal/trace return.
Two ordinary-CI physical-map batches additionally cover supervisor-stack RTE
validation faults and handler returns (223,872 phases). The direct matrix uses
all CCRs; one/two-throwaway chains use CCR 0/31. Every byte of each SR, PC, format,
SSW and required continuation-EA read is rejected in turn, with T0/T1/no trace,
even/odd data-stack addresses and zero/nonzero VBR. The format-7 frame preserves
the incomplete original frame and committed stack selection. Expectations mask
only undefined SSW X, EA and invalid writeback/push data. The recording bus
rejects the original operand range through the existing internal physical-map
interface with translation disabled; it does not synthesize a CPU exception.
Run the five fault-frame/width/address/PC/continuation-read mutations with
`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RteValidationFault`.
User-tail validation, faults during internal restoration or fault entry, other
instructions' fault frames and software writeback handlers remain required.
These two phases do not qualify re-execution of the repaired original RTE,
enabled MMU operation, a public BERR interface or physical timing.
See the [reference record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-access-frame-restoration-discovery-2026-10-05)
for failed-before evidence, mutation controls and the remaining scope.

The LPSTOP qualification runs 267,262 ordinary-CI cases in nine batches.
On 060, every second opcode word except the fixed `$01c0` encoding is checked
in both privilege modes (131,070 cases). These unrecognized F-line forms must
take vector 11 before privilege handling. Eight profile batches add 17,024
cases each for individual encoding-bit changes, immediate SR boundaries, all
initial CCRs, user/supervisor mode and incoming trace. Legal 060 LPSTOP retains
its S-clear privilege rule, trace behavior and stopped-state nonretirement.
Exception frames preserve the original SR and opcode PC according to the manual.
Run its three discriminating controls with
`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope LowPowerStop`.
Physical broadcast/pins and the separate ordinary STOP S-clear disagreement
remain unqualified. The former 320-case-per-profile LPSTOP loop is replaced by
the encoding/status matrix after shared S-clear mutation proof; MOVE16, cache,
breakpoint and specialized regressions remain.

The independent LPSTOP exception preset uses a copied pinned WinUAE generator
with nonadvancing fixed-offset fetches. Its 245,760 callbacks validate actual
CPU results, original SR and opcode-PC frames in fourteen encoding/immediate-S/
incoming-S-and-CCR combinations. The generator skips stopped outcomes and this
preset has no incoming trace: these remain synthetic coverage. It requires exact
source/patch/input identities, all combinations and their recorded distributions;
missing fixtures or empty selections fail. Register/SR/frame corruption probes
must fail too. Prepare with `./scripts/prepare-copper68k-winuae.ps1 -Preset
LowPowerStop` and the same pinned-source/compiler arguments as other presets;
run with `./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset
LowPowerStop -InputDirectory <qualified-inputs> -OutputDirectory <fresh-output>`.
The [reference record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#lpstop-independent-exception-qualification-and-consolidation-2026-10-05)
records the source correction, corpus limitations and retirement proof. The
original Basic disagreement remains retained; this is a separate qualified preset.

Legal MOVES B/W/L reference inputs have a separate `Moves` preset. It clears the
eleven reserved extension bits and excludes same-An postincrement/predecrement
stores before reference execution, as M68000PM 6-24/25/26 requires. Original
Basic inputs and undefined-store disagreements remain preserved. All three
families on 010/EC020/A1200/020/030/040/060 pass 110,492 callbacks, with 86,072
validated privilege frames and 59,920 recorded architectural forms. The report
identifies operand mode/register, general register, direction, incoming S/CCR,
brief indexing and full-format suppression/displacement/indirection structure.
It is a seeded flat-address-space sample, not physical function-code, bus,
cache, trace/fault-restart or exhaustive indexed-value qualification.
Prepare with `./scripts/prepare-copper68k-winuae.ps1 -Preset Moves` and the pinned
source/compiler arguments; run
`./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Moves
-InputDirectory <qualified-inputs> -OutputDirectory <fresh-output>`.
Missing/incompatible fixtures or empty selections fail. Fixed encoding examples
and register/SR/frame corruption controls run alongside actual CPU comparisons.
The 68000 unavailable outcome remains in synthetic coverage. The existing
MOVES privilege-before-extension bus-ordering regression is retained.
The [MOVES qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#moves-legal-input-reference-qualification-2026-10-05)
documents identities, complete selections, failed discovery and limitations.

Legal CAS B/W/L reference inputs use the `Cas` preset on EC020/A1200/020/030/
040/060. A copied input generator clears reserved extension fields; a separate
copied CPU generator fixes the 060 misalignment vector-61 saved PC to the
causing instruction, as MC68060UM C.2.2 requires. The original Basic saved-PC
disagreement is retained. All eighteen selected directories pass 49,284
callbacks, 2,466 exception frames and 38,448 recorded architectural forms.
The report records size, EA register/mode, compare/update register, incoming
S/CCR and indexed structure. Fixed encodings, exact counts and complete
selections are required. Register/SR controls apply to every directory; frame
and saved-PC corruption controls apply to the two 060 W/L directories with
exceptions. Zero-frame directories must report exactly zero frames; their
inapplicable frame controls are labeled explicitly.
Prepare with `./scripts/prepare-copper68k-winuae.ps1 -Preset Cas` and the pinned
source/compiler arguments; run
`./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Cas
-InputDirectory <qualified-inputs> -OutputDirectory <fresh-output>`.
Both source/patch identities and all fixture hashes are checked. This is a
seeded software-reference qualification with CCR 0/31 and both privilege states,
not physical lock/bus, cache, trace/fault restart, timing or exhaustive indexed-
value qualification. CAS2 remains separate; 000/010 absence remains synthetic.
The [CAS qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#cas-legal-input-and-unimplemented-frame-reference-qualification-2026-10-05)
records the original disagreement, controls and remaining scope.

CAS2 W/L has a separate `Cas2` preset for EC020/A1200/020/030/040/060. It corrects
the pinned 040 reference's compare-alias result to operand 1 (M68000PM 4-68),
and excludes undefined overlapping memory-update candidates before reference
execution. On 060 overlapping candidates remain selected because CAS2 takes
vector 61 without memory updates. The input classifier independently checks
physical overlap, including boundary wrap, and records register/address selectors,
compare/update aliases, alignment and incoming S/CCR. Twelve directories pass
3,180 callbacks, 2,296 exception frames and 3,072 recorded forms. Both source/
patch identities, all fixture hashes, complete selections and exact counts are
required. Comparator controls recreate both W/L alias defects and corrupt 060
saved PCs; eleven fixed encoding/overlap tests also run. This is a small seeded
software-reference sample, not exhaustive combinations or physical qualification.
Prepare with `./scripts/prepare-copper68k-winuae.ps1 -Preset Cas2` and the pinned
source/compiler arguments; run
`./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset Cas2
-InputDirectory <qualified-inputs> -OutputDirectory <fresh-output>`.
See the [CAS2 qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#cas2-compare-alias-and-unimplemented-frame-reference-qualification-2026-10-05)
for retained failed discovery, controls, exclusions and remaining scope.

The `CacheEncodings` preset qualifies every scope-zero cache word on all eight
profiles: 64 opcode words, CCR 0/31 and both privilege states, totaling 2,048
callbacks and exception frames. M68000PM 6-4/9 and MC68060UM D-12 specify vector
4 on 040/060; earlier profiles take line-F vector 11. A copied input generator
corrects that reference exception and selects these inputs before execution.
Exact opcode/status distributions, source/patch/input identities, complete
selections and register/SR/frame/saved-PC controls are required. Twelve fixed
encoding cases run with the audit. The original Basic disagreement is retained.
Prepare with `./scripts/prepare-copper68k-winuae.ps1 -Preset CacheEncodings`
and the pinned source/compiler arguments; run
`./scripts/test-copper68k-winuae-qualified-exceptions.ps1 -Preset CacheEncodings
-InputDirectory <qualified-inputs> -OutputDirectory <fresh-output>`.
Upstream excludes actual cache operations; they are not external-reference
coverage. The ordinary `system-cache-encodings` matrix enumerates every F4xx word,
all CCRs and both privilege states (131,072 cases), including neither-cache
no-operations and illegal-scope priority. `-Scope CacheEncodings` in the mutation
script proves vector, priority, privilege, flag and extension-length detection.
The duplicated cache loop is retired after shared mutation proof; MOVE16,
breakpoints, specialized cache/prefetch/bus/JIT/native regressions remain.
See the [cache encoding record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#cache-encoding-reference-qualification-and-consolidation-2026-10-05)
for exact identities, replacement cases and qualification limits.

040 access-fault entry has five additional ordinary CI batches:
`rte-access-entry-double-fault` (98,304 complete halt/host-entry/reset scenarios),
`rte-access-handler-refault` (3,840 nested handler exceptions), and
`access-double-fault-dispatch-accurate`, `-v1`, `-v2` (1,088 each). The first
matrix rejects every byte of the format-7 stack and vector access across both
supervisor banks, valid incoming trace states, all CCRs, odd/even stacks and VBRs.
The dispatch groups require actual compiled warm execution, RTE fallback or a
compiled operand-fault side exit, and B/W/L read/write access widths. Halted
state cannot be resumed by interrupt, task or subroutine entry; external reset
restores those host operations and instruction execution. Partial frame state
and stack ordering remain opaque. The approximate short generic operand-fault
frame is not promoted as architectural format-7 coverage.

Run `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope AccessDoubleFault`
for maintained fatal-latch, reset, compiled-entry, master-stack and six classic
memory-routing mutations. The complete 040 audit includes these combinations
and fixture identities while retaining its failing untested protocol inventory.
Handler-entry prefetch and internal-restoration faults remain required work;
executed repair/retry coverage is described below.

`SyntheticM68040BatchFaultTests` adds two ordinary CI batches:
`rte-validation-batch` (129,024 scenarios) and `access-fault-batch-dispatch`
(10,368). They cover cold execution, warmed cached blocks, model-specific mixed
blocks and the self-branch path; completed prefixes, instruction caps, boundary
denial and cycle deadlines; both supervisor stacks and partial MOVE side effects.
The fixtures check instruction counts, callback counts, handler sentinels and
each byte of selected physical reads/writes. RTE validation uses independent
architectural format-7 expectations. Generic operand/fetch frames and scalar/
batch cycle and bus-order equality verify existing execution policy, not general
architectural restart or physical timing. The dedicated 040 discovery command
requires these reports, independently enumerates their 4,032 and 5,184
combinations and binds their fixture and CPU identities. With repair coverage it
executes 30 tests (24 reporting batches and six fixed examples), retaining the failing
480-case untested protocol inventory.

Run `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope BatchFault` to
remove handling separately from cold, normal cached, model-specific cached and
self-branch execution, change the self-branch count, omit the cached callback,
or retry after partial operand effects. Every mutation must execute both full
batches and detect mismatches in its intended path; a build failure, empty
selection or failure only in another path is insufficient. Sources are restored
and rebuilt even when qualification fails. General format-7 restart, user-tail
and internal-restoration faults and handler-entry prefetch remain required work.

`SyntheticM68040RteRepairTests` executes the access-error handler's stores,
handler RTE, original RTE retry and following instruction in two ordinary CI
batches: `rte-repair-boundaries` (143,424 phases, 540 combinations) and
`rte-repair-chained` (768,960 phases, 45,792 combinations). The first covers all
32 CCRs and incoming/restored trace states; the second rejects each byte of
every selected validation read after one/two consumed supervisor throwaways,
with odd/even stacks, VBR and boundary CCRs. Both restore user/ISP/MSP results
and cover formats 0/2/3, invalid 4/15 repaired to 0, and normal/CM/CT/CU/CP49.
The handler repairs SR, PC and format using fixed MOVE encodings, then explicitly
clears saved incoming trace. Expectations check each store, exact PC, untouched
registers/memory, stack banks, no repeated throwaway read, saved MOVEM EA,
pending conversion/return and trace on the following instruction. This does not
qualify untouched incoming-trace retry, user-tail validation, enabled MMU,
internal-restoration faults, arbitrary CP context transfer or physical timing.

The dedicated 040 audit requires these complete reports and independently
enumerates their combinations. Its 30 tests comprise 24 reporting batches and
six fixed examples; the 480-case untested inventory still fails the full gate.
Run `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RteRepair` to corrupt
the short-frame return PC, MOVEM continuation EA or pending exception's stacked
SR. Each mutation must execute both full batches and mismatch in its intended
retry/following phase, rather than failing at an unrelated prerequisite. The
[qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-executed-rte-repair-and-retry--2026-10-05)
records current evidence and remaining scope. No existing regression is retired.

Physical instruction-fetch faults now use a 60-byte format-7 frame with separate
executing-instruction PC and prefetch fault address, instruction TM and no valid
writebacks. Accurate scalar, normal cached and self-branch boundaries retain the
executing PC even when the rejected long covers an extension or the other half
of a fetched long. Data operand/writeback restart remains a separate required
protocol; its existing short-frame policy is not promoted by this correction.

`SyntheticM68040InstructionFaultTests` adds `instruction-fault-frame` (36,864
phases / 576 combinations) and `instruction-fault-restart` (79,872 / 624) to
ordinary CI. Fixed opcode/extension/following-opcode/self-branch fixtures check
each byte of the required prefetch, all CCRs, all stack banks, odd/even stacks,
both VBRs, frame trace states and restart values. The handler really executes
RTE, followed by the original instruction and a branch sentinel. These fixtures
use the cache-disabled accurate factory with MMU off; speculative prefetch
deferral, enabled-cache behavior, compiled instruction-PC provenance, data
writebacks/restart and physical timing remain unqualified.

The complete 040 command now requires 32 tests (26 reports, six fixed examples),
eleven fixture/command identities and both new independent combination matrices,
while retaining the failing 480-case inventory. `-Scope InstructionFault` in
the maintained mutation command detects a short frame, data TM and prefetch PC
substitution, requiring complete executable selection and intended fault-entry
diagnostics. `-Scope BatchFault` retains the complete seven-mutation campaign
including updated architectural self-fetch frames. No regression is retired.
The [qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-instruction-fetch-access-faults--2026-10-05)
records current execution, historical proof, mutations, identities and remaining
scope.

`SyntheticM68040HandlerPrefetchTests` adds two ordinary reports with 24,576
cases. They check that a denied cold batch makes no CPU fetch/state change, and
that an instruction-fetch fault after an executed handler MOVEQ/NOP prefix starts
a new format-7 exception with precise handler PC/SR. The accurate 040 engine
requires a physical host code reader for speculative hot-block construction;
the logical bus's real-fetch fallback cannot run before an instruction boundary.
`-Scope HandlerPrefetch` restores that defect and requires intended denial proof.

At historical checkpoint `92922a7`, the two entry-prefetch reports were enabled
only by the complete 040 discovery command. They rejected each byte of the
four-long window and failed all 393,216 cases: deferred demand fetch did not
perform architectural handler-entry prefetch. That command required 36 tests
(30 reports, six fixed examples), twelve fixture identities and four independent
new combination matrices, preserving 480 untested requirements. Those results
remain failed historical discovery, not current ordinary qualification. See the
[qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-host-reader-boundary-and-handler-prefetch-discovery--2026-10-05)
for authorities and historical evidence.

### Current 040 access-error handler entry

The 040 access-error handler-entry window is now an ordinary eight-batch gate
(457,728 cases): four aligned longwords are acquired before handler execution,
entry rejections and odd handler PCs halt, retained words survive memory changes,
and selected branch/host/task/map transitions invalidate stale entry data. The
later-handler fault is at handler+16, beyond the retained entry window. Coverage
uses cache/MMU-disabled accurate scalar/batch execution with physical-map faults;
other exception paths, enabled-cache speculation, compiled fetch provenance and
physical timing remain unqualified. Historical failed discovery is preserved.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope EntryPrefetch -OutputDirectory artifacts/entry-prefetch-mutations
```

This mutation command requires both routes and the intended semantic mismatch
for each of five defects. The complete 040 audit still fails its retained
480-case protocol inventory; passing ordinary handler tests do not complete
milestone 6. See the
[reference record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-access-error-handler-entry-window--2026-10-05).

### User-stack RTE validation and software repair

`SyntheticM68040UserRteFaultTests` adds four ordinary scalar/batch reports with
1,687,040 cases. It combines documented throwaway live-SR behavior with general
supervisor exception entry; the combined USP fault path has no executed hardware
oracle. Both user M values select the expected exception stack, with TM=1 and
preserved user frame/consumed throwaways. Bare handler return is followed by a
privilege fault at RTE. A seven-store software handler repairs the frame, builds
a fresh throwaway bridge and explicitly sets saved S before retry. Canonical
SR reads use all CCRs; structural cases reject every validation read byte. Repair
checks following traces, saved MOVEM EA and pending CT/CU/CP49 delivery.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope UserRteFault -OutputDirectory artifacts/user-rte-mutations
```

The command requires complete scalar/batch fault reports and each mutation's
intended saved-S, TM or stack-selection mismatch. The complete 040 command now
checks 44 tests / 38 reports / six fixed examples / thirteen input identities,
while keeping the failing 480-case remaining-protocol inventory. Internal
restoration, untouched trace retry, chained odd-PC provenance and broader
reference/consolidation work remain required. Production CPU code and public
packages are unchanged; no old regression is retired. See the
[qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-user-tail-validation-and-software-repair--2026-10-05)
for the distinction between this manual-derived software qualification and
unobserved hardware behavior.

### Preserved incoming trace after RTE repair

Four additional `SyntheticM68040RteRepairTests` reports cover 2,729,088 phases
through accurate scalar and one-instruction batch execution. The real handler
repairs the original SR/PC/format with three stores, leaving the access frame's
saved SR untouched. Handler return restores incoming T1/T0. Successful original
RTE completion traces using the repaired SR/PC even when its restored trace
bits are clear. The trace-handler RTE and following instruction also check that
the CM saved MOVEM address survives the intervening handler.

Canonical cases cover all 32 CCRs. Chained cases reject every validation read
byte, use CCR 0/31, both alignments/VBRs and all twelve supervisor throwaway
paths, including middle stacks. Both groups cross incoming/restored trace and
restored user/ISP/MSP stacks. Forms are 0/2/3, invalid 4/15 repaired to 0 and
normal/CM format 7. Each throwaway in a case uses the same incoming trace value;
pending CT/CU/CP, a user-tail trace bridge and mixed-epoch trace provenance remain
separate required gaps.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RteRetryTrace -OutputDirectory artifacts/rte-retry-trace-mutations
```

The command requires all four complete reports and the intended semantic
failure for restored-bit trace gating, missing T0 RTE classification and early
CM continuation consumption. The complete 040 gate now checks 48 tests, 42
reports, six fixed examples and thirteen input identities. Its retained
480-case remaining-protocol inventory still fails completion. No production
CPU change, package publication or regression retirement is included. See the
[qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-preserved-trace-rte-repair--2026-10-05).

### Pending-exception delivery with preserved incoming trace

Four `PendingRepairWithPreservedIncomingTrace` reports add 1,824,768 phases
for CT/CU/CP49 after supervisor-tail repair. The handler preserves the access
frame's SR. The original RTE must deliver the pending exception without an
extra automatic trace of that RTE; repaired SR/PC/EA, format/vector and pending
consumption remain visible to its handler. A bare pending-handler RTE returns,
and the following BRA obeys the repaired trace bits. This fixture does not
emulate the pending handler's software service of the original trace.

Canonical cases cover all CCRs; chained cases reject every validation-read byte
with CCR 0/31. Both routes cross all incoming/restored traces and restored
stacks, twelve supervisor throwaway paths and both alignments/VBRs. The three
continuation forms have separate case identifiers. CP vectors other than 49,
software trace service, user-tail trace bridges and mixed-epoch trace provenance
are not qualified by these new groups. Existing untraced CP-vector coverage
remains separate.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RtePendingTrace -OutputDirectory artifacts/rte-pending-trace-mutations
```

The complete 040 gate now requires 52 tests, 46 reports, six fixed examples and
thirteen input identities. Its independent iterator checks every combination
and cardinality. The required 480-case inventory still prevents completion.
See the [qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-pending-delivery-with-preserved-trace--2026-10-06).

The three result states are user M=0, ISP M=0 and MSP M=1. Restored user M=1
is not covered here; its exception-bank selection remains a required separate
qualification, alongside the pending software trace-service and other gaps.

### Restored user mode with M=1

Four `RestoredUserMaster` reports check 1,517,952 phases after a supervisor-tail
validation fault and real software repair. The original frame restores S=0,M=1.
User execution uses USP regardless of M; synchronous completion trace, pending
CT/CU/CP49 conversion and a following traced instruction select MSP. Expectations
compose MC68040UM 2.2.2.1 and 8.1 with 8.2.6, 8.3 and 8.4.6.7. The shared
exception-bank helper now tests M independently of pre-exception S.

Canonical cases cover all 32 CCRs. Chained cases reject every validation-read
byte, with CCR 0/31, all twelve supervisor throwaway paths and both alignments/
VBRs. Both routes cross incoming/restored 0/T1/T0 and forms 0/2/3, invalid 4/15
repaired to 0, normal/CM and pending CT/CU/CP49. All three stack pointers, saved
SR/PC/EA, exact repair stores, pending consumption, handler returns and following
instruction state are checked. The original result-state reports retain their
own cardinalities.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RteUserMaster -OutputDirectory artifacts/rte-user-master-mutations
```

The dedicated gate requires 56 tests, 50 reports, six fixed examples and thirteen
input identities, independently checking each combination. The 480-case required
inventory still prevents completion. Other pending vectors, software trace
service, user-tail trace bridges, mixed-epoch provenance and all earlier
internal/data/writeback/reference gaps remain required. These synthetic results
are software qualification, not hardware observations or physical timing.

See the [restored-user qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md#040-restored-user-m1--2026-10-06).

### Preserved-trace repair for CP vectors 50–55

Four `OtherCpVectorsPreserveTrace` reports add 4,866,048 checked phases through
accurate scalar and one-instruction batch execution. They restore user M=0,
user M=1, ISP or MSP after a physical validation fault and three executed repair
stores. The CP event must retain its originally selected vector despite
conflicting handler-visible FPCR/FPSR/FPIAR. Format-3 saved SR/PC/EA, pending
context consumption, stack selection, handler return and following trace are
checked. Pending delivery cannot generate an extra automatic RTE trace.

Canonical cases cover all CCRs; chained cases use CCR 0/31, every validation-read
byte, twelve supervisor paths and both alignments/VBRs. Both groups cross
incoming/restored 0/T1/T0 and all six vectors. Existing CP49 reports remain
separate. MC68040UM 9.6.2 explicitly permits vector 55 as a post-instruction
exception for register-to-memory unsupported data types; BSUN vector 48 is
outside this CP selection. FPU arithmetic is outside this suite.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RteCpVectors -OutputDirectory artifacts/rte-cp-vectors-mutations
```

The gate requires the intended wrong-vector, extra-RTE-trace or unconsumed-context
diagnostic in all four complete reports. The complete 040 audit requires 60 tests,
54 reports, six fixed examples and thirteen input identities. The retained
480-case inventory still prevents completion. Pending software trace service,
user-tail trace bridges, mixed-epoch provenance and the earlier context/internal/
data/writeback/reference gaps remain required.

See the [qualification record](../../docs/COPPER68K_REFERENCE_QUALIFICATION.md) for identities, proofs and remaining scope.

### Executed pending-exception software trace service

`SoftwareTraceService` adds separate scalar/batch canonical and chained reports.
After the real validation fault and three-store repair, a CU/CP handler executes
BTST/BNE on the saved SR. Synthetic CU metadata selects linear or taken-flow PC
completion; post-instruction CP metadata selects a completed two-word FMOVE.
T1 always requests service, while T0 requests service only for CU taken flow.
The handler adjusts the PC when required, converts format 3 to format 2, supplies
the original instruction address and reads vector 9 from the relocated table.
It pushes that software call target and executes RTS, preserving all registers
and leaving the trace frame on the selected exception stack.

The trace handler writes one marker, optionally clears stacked trace bits and
executes RTE. Every instruction checks state, guarded memory and all stack banks;
software calls preserve the hardware exception counter and provenance. The
following BRA independently checks resumed hardware tracing. Canonical cases
cross all CCRs, incoming/restored traces, four restored banks and CU/CP49–55.
Structural cases cover every validation-read byte, all twelve supervisor paths,
both alignments/VBRs and CCR 0/31, with incoming T1 and all restored traces.

The nominal CU/CP instruction completion metadata is an input to this integer
handler protocol. Actual FPU emulation/arithmetic and hardware fault observations
remain outside the qualified scope. User-tail and mixed-epoch trace bridges,
internal restoration and all other retained restoration/reference gaps remain
required. Fixed golden program words audit the fixture encodings separately.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope RteSoftwareTrace -OutputDirectory artifacts/rte-software-trace-mutations
```

The four reports require 514,560 canonical / 3,601,920 chained phases per route,
8,232,960 total. The complete 040 audit requires 65 tests, 58 reporting batches,
seven fixed examples and fourteen input identities. The retained 480-case
inventory continues to fail completion; milestone 6 remains in progress.

### User-tail bridge with preserved incoming trace

`SyntheticM68040UserRteFaultTests.UserTailBridgePreservesTrace*` extends the
seven-store repair program without replacing its original trace-clearing groups.
The new bridge and access return retain incoming trace; only the access return
adds S so privileged RTE can retry. Completion tracing uses incoming 0/T1/T0
independently of repaired trace. CT/CU/CP49–55 take priority over an automatic
RTE trace. Every return and following BRA/MOVEM checks all stack banks,
exception provenance, guarded memory and consumed throwaway effects.

Canonical scalar/batch groups each contain 873,984 phases / 2,304 combinations
with every CCR. Structural groups each contain 3,502,080 phases / 145,920
combinations, using CCR 0/31 and incoming T1 at every validation-read byte, both
alignments/VBRs and every initial/middle user/ISP/MSP path. Both original user M
values and all restored user/user-M/ISP/MSP states are covered. The four groups
add 8,752,128 phases to ordinary CI and the complete 040 audit.

Run the focused selection with
`dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --filter FullyQualifiedName~UserTailBridgePreservesTrace`.
Use `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope UserRteTrace` for
restored-bits substitution, original CP-vector loss and premature MOVEM
continuation consumption. Proof requires the intended diagnostic in all four
complete reports. Expectations compose MC68040UM trace, throwaway and pending
exception rules; no hardware execution is claimed. Mixed-epoch provenance,
user-tail software trace-service programs and internal restoration remain open.

### Executed software trace service after user-tail repair

`SyntheticM68040UserRteFaultTests.UserTailSoftwareTraceService*` combines the
seven-store bridge with the shared integer service program. CU linear/flow and
CP49–55 cases test repaired saved trace, supplied completion-PC adjustment,
frame conversion, vector-9 lookup, direct call, marker once, optional saved-trace
clear, real RTE and the following BRA. Both original user M values and every
restored user/user-M/ISP/MSP bank are covered. This composes documented rules;
no hardware or FPU arithmetic qualification is claimed.

Canonical scalar/batch reports each require 1,360,896 phases / 2,592 combinations;
structural reports each require 6,350,848 phases / 193,536 combinations. These
add 15,423,488 phases. The complete 040 selection requires 73 tests, 66 reports,
seven fixed examples and fourteen input identities; its retained 480-case
inventory still fails completion. This test-only checkpoint is qualified by
fresh full Release, strict reporting, reference audits and seventeen report
integrity controls; milestone 6 remains in progress. Production CPU source is
unchanged. See the plan and qualification record for exact evidence and limits.

Use `--filter FullyQualifiedName~UserTailSoftwareTraceService` for focused
execution and `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope UserRteSoftwareTrace`
for all four complete groups under each mutation. Intended BTST, return-length
and CP-vector diagnostics are required; unrelated failures do not qualify a
proof. Mixed-epoch trace provenance and internal restoration remain open.

### Successful mixed-epoch throwaway chains

`SyntheticM68040ThrowawayTests.MixedEpochTrace*` independently crosses incoming,
first/second throwaway and final trace states 0/T1/T0. It includes user M=1,
all stack aliases, short frames and every defined access continuation. Incoming
trace controls completion of the original RTE; pending CT/CU/CP has priority.
CM also executes MOVEM, its optional trace return and a following BRA.

Canonical scalar/batch reports each require 1,152,000 phases / 12,096 combinations;
structural reports each require 3,744,000 phases / 628,992 combinations. The
`MixedEpochRte` mutation scope requires intended provenance, T0 and MOVEM-lifetime
diagnostics in all four complete reports, including a second-throwaway SR
diagnostic in both structural reports. Focused restored-build qualification passes
all four reports and six independent SR encoding examples, with zero skips.
The separate current inventory still fails its 480 required cases. Previous
broader qualification remains a separate checkpoint. Mixed epochs
during validation faults, repair/retry and internal restoration remain required.

### Mixed-epoch validation fault capture

`SyntheticM68040RteValidationFaultTests.MixedEpochFaultCapture*` crosses
independent incoming and committed throwaway trace states, all stack banks and
user M values. Canonical cases cross final trace/CCR values at the PC read;
structural cases reject every validation-transfer byte in one/two-throwaway
chains, including aliases. Each phase checks the last committed SR, saved PC,
registers, guarded memory, stack banks, exact rejected access and retained
pending CP vector. Bare handler return and user-tail privilege failure are
executed; supervisor retry and original trace resumption remain unqualified.

Canonical scalar/batch reports each require 276,480 phases / 3,456 combinations;
structural reports each require 3,556,800 phases / 711,360 combinations. The
complete 040 selection now requires 87 tests / 74 reports / thirteen fixed
examples, including its retained 480-case failing inventory. Expectations
compose MC68040UM exception and RTE rules; no combined hardware capture is
claimed. See the qualification record for the executed validation scope.

Use `--filter FullyQualifiedName~MixedEpochFaultCapture` for focused execution
and `./scripts/test-copper68k-synthetic-mutations.ps1 -Scope MixedEpochFault`
for lost live T1, premature CCR installation and skipped aliased-USP fixture
probes. CPU probes must fail all four complete groups with the intended saved-SR
diagnostic; the fixture probe must preserve both canonical groups and fail both
structural groups. The lost-T1 probe additionally requires second-throwaway
trace diagnostics in both structural reports. Shared test-only stack/status
fixtures retain independent fixed SR examples. Failed initial fixture evidence
is not qualification evidence.

### Mixed-epoch repair/retry discovery

`M68040MixedEpochRetryDiscoveryTests` executes physical PC-read rejection,
three real repair stores, handler RTE, retry and following instruction. Initial,
first/second throwaway and repaired trace values vary independently; supervisor
tails include one/two throwaways, user/user-M intermediate aliases, every
restored stack bank, CCR 0/31 and format0/normal/CM. Its expectation applies
MC68040UM 8.2.6's original-instruction trace deferral to validation retry. That
composition still needs independent qualification. This discovery is not a
promoted family or a confirmed production defect; the existing required
inventory remains intact.

Run `./scripts/test-copper68k-040-mixed-retry-discovery.ps1` explicitly. It
requires two executed tests without skips, records current source/binary
identities, independently validates all 16,848 combinations and 258,336 phases
per route, and fails on mismatches, unsupported execution, untested phases or
missing inputs/reports/selections. Use `-ValidateReportsOnly -OutputDirectory`
with recorded outputs to validate them again. Ordinary synthetic CI excludes
these `ReferenceDiscovery` cases; an optional skip is unavailable coverage.

The current scalar/batch selections each contain 220,896 passing, 14,976
mismatching, zero unsupported and 22,464 untested phases. Retry uses the last
committed trace state where the hypothesis requires the original state. Both
lost and newly introduced trace directions, including second throwaways, are
retained. No private trace latch, guessed CT flag or production change is
introduced to force agreement. See the qualification record for scope and
independent-reference caveats.

### Chained odd-PC short-frame SR handoff

`SyntheticM68040ChainedOddReturnTests` adds four ordinary synthetic batches for
format0/2/3 tails following one/two throwaways. Canonical scalar/batch groups
each execute 82,944 cases / 2,592 combinations with every initial CCR; structural
groups each execute 539,136 cases / 269,568 combinations with CCR 0/31, both
alignments, both VBRs, low/high odd PCs, independent incoming/first/second/final
trace states, every stack and the user M=1 alias. Full register/memory checks,
consumed pointers, discarded/odd PC non-fetch, validation-read order and the
format-2 address-error image are checked.

Run `./scripts/test-copper68k-040-rte-handoff.ps1` with a pristine WinUAE checkout
at `5d22d33632646efc3f747f03e82d28353e52722e` passed as `-ReferenceDirectory`.
The default location is `artifacts/reference-winuae-rte-modern`; the command
requires PowerShell 7 and MSVC (override `-VcVars` for a different installation).
It builds the pinned generator with CPU_TESTER enabled, extracts the untouched
generated 040 RTE and cputest SR helpers, and executes their secondary-SR
handoff in a transport harness. Three literal controls distinguish the original,
last-throwaway and final SR. The handoff/live state and frame reads are compared
with CPU results; documented format-2 entry and the traced-user S-bit correction
are composed separately. This is an executed software handoff reference, not
a complete WinUAE exception engine or a hardware/timing qualification.

The command requires four executed tests without skips, exact independently
enumerated combinations, every reference row, current source/binary identities
and zero mismatches/unsupported/untested cases. Missing inputs, empty selections
and out-of-scope reference operations fail. Recheck frozen outputs using
`-ValidateReportsOnly -OutputDirectory`. Never substitute this observer for a
validation bus-fault oracle: the generated 040 test RTE does not handle that
fault protocol. Format7 pending/foreign context and the original-trace
retry discovery remain explicit requirements.

`./scripts/test-copper68k-synthetic-mutations.ps1 -Scope ChainedOddRte` proves
that both complete canonical batches detect substitution of the final SR,
loss of the committed trace bits and omission of the traced-user S correction.
It requires the intended saved-SR diagnostics and restores/rebuilds production
source. No old hardware/integration regression is retired by this qualification.

Use `-AccessFrames` to execute the four normal/CM format7 groups: canonical
scalar/batch each 55,296 cases / 1,728 combinations, structural scalar/batch
each 359,424 / 179,712. The observer qualifies SR handoff, 60-byte consumption
and header reads. It receives declared SSW/EA inputs and rejects CT/CU/CP or
undefined continuation flags, because its generated RTE skips their protocol.
Separate synthetic checks cover SSW/CM-EA validation order, writeback guards,
the address-error image and absence of an armed MOVEM continuation after the
odd return. These checks do not acquire a full external continuation oracle
merely by agreeing on the header handoff.

Revalidation of access-profile outputs also requires `-AccessFrames`.
`-Scope ChainedOddAccessRte` adds five maintained complete canonical probes:
final-SR substitution, lost committed trace, omitted saved S, skipped CM EA
read and premature MOVEM continuation before address-error delivery. Pending
and foreign-context odd-return chains remain in the 480-case required inventory.
The no-continuation assertion preserves Copper68k's existing policy; the MMU
WinUAE helper arms CM before the odd-PC check, whereas this non-MMU observer
skips that helper. Its passing header comparison does not settle CM lifetime
across address-error delivery/software repair. That protocol remains required
reference research, as recorded in the qualification document and plan.

### CM continuation lifetime discovery

`M68040CmLifetimeDiscoveryTests` follows a normal/CM format7 return through an
odd-PC address error, two executed repair stores, handler RTE, PC-relative
MOVEM.L and a following NOP. Even-PC controls omit the repair path. Every CCR,
entry ISP/MSP, restored user/user-M/ISP/MSP, both stack alignments and both VBRs
produce 4,096 scenarios / 18,432 phases / 576 phase combinations per route.
All registers, stack pointers, guarded memory and selected operand read order
are checked. A failed prerequisite leaves later phases explicitly untested.

Run `./scripts/test-copper68k-040-cm-lifetime-discovery.ps1` with the same pristine
WinUAE pin and MSVC options as the handoff command. This builds the original
full generator with CPU_TESTER=0, then extracts unchanged `_31` RTE, repair
MOVE, MOVEM and NOP functions, the original `m68k_do_rte_mmu040` helper and
cputest SR helpers. Four literal normal/CM, even/odd controls validate the
program and saved-versus-recomputed addresses. Sparse physical transport
rejects uninitialized reads. The adapter composes address-error entry; it
does **not** execute full WinUAE `Exception_mmu`, enabled MMU, trace/interrupt
delivery, physical timing or hardware behavior.

The current scalar/batch routes each contain 16,384 passing, 1,024 mismatching,
zero unsupported and 1,024 untested phases. Only odd-PC CM MOVEM disagrees:
Copper68k loads DEADBEEF from recomputed 7000; the reference retains CM through
repair and loads 89ABCDEF from saved 4200. Following sentinels in those scenarios
remain untested. All earlier repair phases, even-PC CM and normal controls agree.
This is an executed software discrepancy, not a promoted architectural defect.
No continuation latch or production edit is introduced to force agreement.

The command records pinned source, fixture, generated-code, binary and evidence
identities; independently checks all phase keys, CCR weights and fixture rows;
and fails on mismatches, unsupported/untested execution or missing/empty inputs.
`-ValidateReportsOnly -OutputDirectory` also executes the frozen native observer
and compares its regenerated results. Ordinary synthetic and complete-040
selections exclude these gated `ReferenceDiscovery` tests. The 480-case required
inventory and broader milestone 6 scope remain intact.

Use `-MmuExceptionEntry` for the additional executed-entry profile. It replaces
the composed native boundary with unchanged `newcpu.cpp` SR helpers, the
odd-PC callback and exception dispatch, `Exception_mmu`, trace clearing and
`newcpu_common.cpp` frame construction. The original `fill_prefetch` executes
its compatibility-disabled early return. Physical transport records the actual
frame; unavailable exception, interrupt and compatible-cache paths fail.
Translation, run-loop behavior, trace delivery and hardware remain unqualified.
This profile requires the matching switch during report revalidation.

The fixture exports raw frame SP/SR/PC/format/fault address immediately after
the initial return, before repair can overwrite them. In the current executed
MMU profile all 2,048 odd frames per route differ only in saved SR: the reference
stacks the restored SR, while Copper68k stacks the secondary incoming SR. All
other frame fields agree. CM still survives the native entry and repair, so
the MOVEM phase classifications remain unchanged. Both discoveries fail their
requested audit; neither software path's answer is promoted to hardware truth.
The passing handoff qualification and current continuation policy remain
separate from this raw-frame/CM lifetime research.

68060 PCR fields use `SyntheticPcrTests` and the shared MOVEC register fixture.
All defined EDEBUG/DFP/ESS combinations, read-only identification/revision probes,
privilege, general registers, CCR preservation and actual reset/readback are
covered. `system-pcr-reserved-policy` separately retains the old invalid-reserved
write mask convention; it is not architectural maskset qualification. MC68060DE
I14/I15's bit-5 workarounds and the pinned WinUAE bit-6 EDEBUG disagreement remain
explicit. Physical debug output, superscalar timing and pending-FPU behavior
are outside these register-field checks.

`./scripts/test-copper68k-060-pcr.ps1` runs four matrices (305,152 cases) and checks
source/binary/evidence identities, exact nonempty test selection, independently
enumerated combination keys and weights. `-ValidateReportsOnly` requires the
same output directory and identities. Three `-Scope ProcessorConfiguration`
mutations prove the replacements detect the retired PCR identification/reset
fact's defects. The qualification document records before/after proof, scope and
remaining requirements; the whole roadmap remains incomplete.

`M68060StopDiscoveryTests` is a gated `ReferenceDiscovery` selection for ordinary
68060 STOP. Run `./scripts/test-copper68k-060-stop-discovery.ps1` with the same
pristine WinUAE pin and MSVC setup as the CM command. Scalar/batch each cover
163,840 defined-status/CCR scenarios plus 20,480 inert attempts, with exact
register, stack, PC, SR, memory and exception-boundary checks. Incoming IPL is
fixed at 7; target status images cover all defined bits. Batch idle steps use
the existing API count of one, without retiring the following instruction.

The observer executes unchanged generated `_33` STOP and native SR/stop helpers.
It observes privilege callbacks, normalizes stopped PC to the architectural
next PC and composes incoming trace; native exception frames, run-loop trace,
IRQ and hardware timing are outside scope. Four literal controls protect the
transport. The pinned source calls its S-clear privilege rule undocumented;
it disagrees with Copper68k on 40,960 cases per route. Each route otherwise has
143,360 passing cases, zero unsupported and zero untested cases. No CPU fix is
selected from this software disagreement.

`-ValidateReportsOnly -OutputDirectory` checks the same exact identities,
independently enumerated keys/weights/fixture rows and both named test executions,
then reruns the frozen observer. Both commands currently fail explicitly on
81,920 combined discrepancies. Missing/empty/malformed inputs must also fail.
The qualification document records thirteen integrity controls and retained
test evidence. Discovery does not promote coverage into ordinary CI or
complete-040, retire regressions or complete milestone 6.

68010 MOVEC uses `SyntheticM68010MovecTests` and the shared control-register
fixture. Four ordinary groups cover all legal source/readback register pairs,
function-code masks from every initial image, all raw register bits, every
undefined selector and legal user-mode transfers. All registers, active/inactive
stacks, SR/CCR, PC, preserved controls and memory are checked. USP initialization
and A7 source aliases are explicit. Failed writes leave readbacks untested;
successful reads include a following NOP. This is nontraced IPL7 register
qualification, separate from physical function-code spaces, restart and timing.

`./scripts/test-copper68k-010-movec.ps1` runs four matrices (2,271,744 cases) and
checks exact input/binary/evidence identities, named test selection and
independently enumerated combination keys/weights. `-ValidateReportsOnly`
requires the same complete output and inputs. Ten report integrity controls
reject malformed selections. `-Scope Movec010` adds six maintained mutations,
with original/replacement detection before and after the demonstrated retirement
of three duplicate methods. The qualification record maps each original to its
replacement; factory, exception, interrupt and specialized regressions remain.
The broader roadmap is still incomplete.
