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
**8,501,832 logical cases** across **348 xUnit batches**, across seven models and the A1200 profile.
Ordinary `dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release` includes
these batches. CI additionally validates every required report and exact count;
missing reports, mismatches, unsupported execution and empty groups fail the gate.
The following instruction sentinel verifies extension consumption.

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
020/030 coprocessor midinstruction restoration and 040 access-fault pending
exceptions/writebacks remain **untested by this synthetic matrix**. They are
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
