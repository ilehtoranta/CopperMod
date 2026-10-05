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
