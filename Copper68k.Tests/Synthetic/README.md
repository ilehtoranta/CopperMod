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

MOVE/MOVEA, transfer/address and arithmetic/comparison operations execute
**2,945,168 logical cases** in **146 xUnit batches**, across seven models and the A1200 profile.
Ordinary `dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release` includes
these batches. CI additionally validates every required report and exact count;
missing reports, mismatches, unsupported execution and empty groups fail the gate.
The following instruction sentinel verifies extension consumption.

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

Recorded xorshift32 seeds add MOVE and arithmetic samples without expanding the deterministic
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
[MC68020UM](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf) and
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
```

Eleven isolated mutations prove detection of absolute-address decoding, extension
length, signed indexes, source/destination alias order, A7 byte stride and MOVE
flags, arithmetic overflow, extend sticky zero, decimal alias ordering, PACK A7
stride and the 060 divide frame. The command restores source in `finally`, rebuilds it and records each
mutation, precise failing replacement case and source hash. A compilation failure
does not count as detection. Do not run it concurrently with a build or edit of
the same production file.

No specialized regressions have been retired. Existing focused MOVE tests whose
unsupported boundary became legal execution were updated to assert the result.
Cache, prefetch, detailed fault ordering, bus timing, JIT and native media tests
remain separate. These semantic results preserve existing timing policy; they do
not certify physical timing or OS compatibility.
