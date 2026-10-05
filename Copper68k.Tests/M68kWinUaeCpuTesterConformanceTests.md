# WinUAE cputest conformance

`M68kWinUaeCpuTesterConformanceTests` can run WinUAE's `cputest/gencpu`
MC68000 data through the Copper68k interpreter by using the host-side runner
shape from Copperline's `crates/cputest-runner`.

This runner is disabled by default and is a secondary discovery net.
Processor documentation and verified hardware establish architectural expectations.
SingleStepTests and WinUAE are independent software references; disagreements,
especially involving undefined flags, need investigation before changing the CPU.

Fetch Copperline and generate the external data outside tracked source files or
at the default local path:

```powershell
git clone https://github.com/LinuxJedi/Copperline third_party/Copperline
bash third_party/Copperline/crates/cputest-runner/tools/cputest-gen.sh third_party/winuae-cputest 68000
```

`cputest-gen.sh` pins `emoon/m68k_cpu_tester_api` at the commit Copperline's
vendored runner came from, builds the WinUAE gencpu chain with `c++`, writes the
generated data under `third_party/winuae-cputest/68000`, and uses
`feature_flags_mode=1` to reduce CCR input combinations for instructions which
do not consume flags. Undefined output bits are masked separately by the native
runner's `check_undefined_sr` setting and each fixture header.

For MC68040 FPU fixtures, use current WinUAE generator sources or apply the
upstream FScc effective-address fix before generating the corpus. Older pinned
snapshots omit the address-register update for `FScc (An)+` and `FScc -(An)`;
their expected results therefore disagree with the processor manuals and newer
WinUAE releases. Regenerate FScc after updating the generator rather than
adding a Copper68k compatibility exception.

Build a native library from Copperline's vendored
`crates/cputest-runner/vendor/m68k_cpu_tester.c` plus its capstone stub. The C
wrapper needs two small local fixes when used directly from .NET:
`M68KTester_run_tests` should return `1` only when every selected opcode passed
(including every directory for `all`), and `M68KTester_last_output` should
export the runner's mismatch diagnostic.
Some vendored snapshots return `0` unconditionally even though their header
documents `1` as success.

Run one opcode explicitly:

```powershell
$env:COPPER68K_RUN_WINUAE_CPUTEST_M68000 = "1"
$env:COPPER68K_WINUAE_CPUTEST_LIBRARY = "C:\path\to\m68k_cpu_tester.dll"
$env:COPPER68K_WINUAE_CPUTEST_M68000_PATH = "third_party\winuae-cputest"
$env:COPPER68K_WINUAE_CPUTEST_OPCODE = "ADD.W"
dotnet test Copper68k.Tests/Copper68k.Tests.csproj --filter "WinUaeM68000CpuTesterPassesInterpreterWhenEnabled"
```

Audit every opcode directory and write a TSV matrix. This reuses one native
runner process, keeps undefined SR bits unchecked, and fails the test if any
directory fails:

```powershell
$env:COPPER68K_RUN_WINUAE_CPUTEST_M68000 = "1"
$env:COPPER68K_WINUAE_CPUTEST_LIBRARY = "C:\path\to\m68k_cpu_tester.dll"
$env:COPPER68K_WINUAE_CPUTEST_M68000_PATH = "third_party\winuae-cputest"
$env:COPPER68K_WINUAE_CPUTEST_AUDIT = "1"
$env:COPPER68K_WINUAE_CPUTEST_AUDIT_OUTPUT = "third_party\winuae-cputest\winuae-m68000-opcode-audit.tsv"
$env:COPPER68K_WINUAE_CPUTEST_CONTINUE_ON_ERROR = "1"
dotnet test Copper68k.Tests/Copper68k.Tests.csproj --filter "WinUaeM68000CpuTesterPassesInterpreterWhenEnabled"
```

The TSV has one row per directory with `pass`, `fail`, or `error` status,
executed callback cases, unmapped bus accesses, duration, and sanitized native
diagnostics. The audit is deliberately separate from `OPCODE=all`: the
vendored runner resets its error flag for each directory and may otherwise
return only the status of the final directory.

Useful environment variables:

- `COPPER68K_WINUAE_CPUTEST_M68000_PATH`: generated output folder containing a
  `68000` link/folder, such as `third_party\winuae-cputest`.
- `COPPER68K_WINUAE_CPUTEST_LIBRARY`: native Copperline cputest runner library
  path.
- `COPPER68K_WINUAE_CPUTEST_OPCODE`: opcode directory name such as `ADD.W`; the
  default is `all`.
- `COPPER68K_WINUAE_CPUTEST_CHECK_UNDEFINED_SR`: set to `1` only when auditing
  WinUAE/UAE undefined SR expectations. Leave unset for the default
  SingleStepTests-compatible mode.
- `COPPER68K_WINUAE_CPUTEST_CONTINUE_ON_ERROR`: pass through to the native
  wrapper when supported by the local build.
- `COPPER68K_WINUAE_CPUTEST_AUDIT`: set to `1` to run each `68000` opcode
  directory and write the matrix.
- `COPPER68K_WINUAE_CPUTEST_AUDIT_OUTPUT`: optional TSV output path; by
  default the report is written under the corpus root.

The managed runner targets the native layout used by Copperline's vendored
`m68k_cpu_tester.c`, not the older generic public header. That vendored file
adds `exc010`, `endpc`, `branchtarget`, `cycles`, and
`m68k_tester_addressing_mask()`, all of which are useful for the generated
WinUAE sets.

## Pinned integer audit across CPU models (milestone 6 checkpoint)

The new opt-in `WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled` uses the
public CPU factory for all seven models and the A1200 profile. A1200 executes
its own core against the EC020 input set. The native runner is serialized and
releases its per-directory allocations. The managed callback requires an actual
instruction/exception boundary within 64 steps; it does not synthesize trace
exceptions or apply the legacy FPU adapter's result adjustments.

On Windows, prepare external fixtures with PowerShell 7 and an x64 MSVC installation. The script
requires exact source revisions, rejects tracked source modifications and existing
output directories, records compiler/native/generator identities and each binary
input hash, and restores the compiler environment afterward. Generated files stay
outside tracked source. The pinned generator initializes its xorshift state to 1
per test set; the preset uses one Basic round and full extension addressing.

```powershell
git clone https://github.com/emoon/m68k_cpu_tester_api artifacts/reference-winuae-api
git -C artifacts/reference-winuae-api checkout 025b999239800357e95065fe5b9a15ea5b300fa7
git clone https://github.com/LinuxJedi/Copperline artifacts/reference-copperline
git -C artifacts/reference-copperline checkout 7a83745d6c6159bc74ab0471578ffc8bc244e66e
./scripts/prepare-copper68k-winuae.ps1 `
  -GeneratorSource artifacts/reference-winuae-api `
  -RunnerSource artifacts/reference-copperline `
  -VcVars64 '<Visual Studio>/VC/Auxiliary/Build/vcvars64.bat' `
  -OutputDirectory artifacts/winuae-model-inputs

$env:COPPER68K_RUN_WINUAE_MODEL_AUDIT = '1'
$env:COPPER68K_WINUAE_MODEL_PATH = (Resolve-Path artifacts/winuae-model-inputs).Path
$env:COPPER68K_WINUAE_CPUTEST_LIBRARY = (Resolve-Path artifacts/winuae-model-inputs/m68k_cpu_tester.dll).Path
$env:COPPER68K_SYNTHETIC_REPORT_DIR = Join-Path (Get-Location).Path 'artifacts/winuae-model-report'
dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release -p:Platform=AnyCPU `
  --filter FullyQualifiedName~WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled
# Remove these opt-in variables before running the ordinary suite.
```

`COPPER68K_SYNTHETIC_MODELS` optionally selects exact comma-separated profile IDs;
missing, empty, duplicate or unknown selections fail. Preflight requires all
expected opcode directories, memory images, sequential data files, matching model/
address-width headers and unchanged manifested inputs. Six ordinary structural
regressions cover changed input, missing memory, empty selection, missing sequence,
incorrect address width and empty data. Structural placeholders never reach the
native runner. Every selected profile must also fail a deliberately corrupted NOP
register result before its audit proceeds.

The bridge initializes the native CCR mask to `0xff` and honors undefined-SR
checking. The upstream wrapper left the mask at zero, silently reducing execution
to CCR-zero inputs and bypassing unchanged-register/SR assertions. The NOP corruption
probe exposed that false-success path. The bridge also closes each opcode header;
without that correction a long audit exhausted Windows stdio handles and aborted.
The wrapper inherits Copperline's aggregate failure return and version-16 format
support; the preparation script verifies unique patch anchors.

`winuae-model-audit.json` records per-model/opcode directory results, callback
counts, diagnostics, input identities and corruption probes. A mismatch or empty
execution fails the requested audit. This checkpoint is discovery evidence: the
Basic preset omits bus/address faults, trace/M rounds and advanced restart protocols.
The pinned generator disables the high memory range in the 32-bit fixture headers;
this run does not independently qualify high 32-bit operand addresses. Physical
timing, cache, enabled MMU and FPU arithmetic remain outside this integer audit.
Generator candidate counts and executed callback counts are separate quantities.
The audit remains failing until reference/adapter/CPU disagreements are resolved;
its presence does not mark milestone 6 complete.

## Architectural SR masks and exception-frame assertions

The integer model audit requires the updated bridge exports. Older bridges which
skip exception-frame records are rejected before callbacks execute. Report schema
2 adds executed frame assertions, cases using architectural SR masks, probe kinds
and adapter/CPU assembly SHA-256 identities. The input manifest also records the
native integer-validator header hash.

`WinUaeArchitecturalFlags` defines comparison masks independently of the production
CPU. Its source is [M68000PM](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
sections 4-2, 4-69/71, 4-92/96, 4-141 and 4-170. CHK retains its defined trap N
bit and X; CHK2/CMP2 and decimal operations retain their defined flags. Division
masks depend on zero/overflow outcome, while defined C/V/X remain checked as
applicable. Exception identity and those outcome bits are independently asserted.
Normal execution results are never normalized to the reference. The bridge applies
these masks to both explicit SR updates and preservation checks. Fourteen ordinary
rule regressions retain every upper SR bit and X.

The old Copperline `validate_exception` returned immediately after consuming each
length-prefixed record. It therefore verified exception numbers without frame
contents; the preceding report's generic frame-effects wording was too broad.
The replacement parser follows the pinned generator's `save_exception` layout.
It reconstructs expected saved SR/PC from independent fixture result records,
parses format/vector words and format-2/3/4 additional addresses, and compares
actual memory at the reported exception stack pointer. Normal 68000 six-byte
frames are also checked. Repeated-record markers retain the reference fields.
Standard trace frames produced by SR-changing instructions are included.
Unimplemented extra-trace/group-2/fault and restart-frame records fail explicitly.
Nothing silently skips those records to claim coverage.

Every model must reject three distinct mutations: NOP D0, NOP's defined X, and
TRAP's actual frame memory (saved-PC word on 68000, format/vector word otherwise).
The frame mutation occurs after copying result registers, isolating memory
validation from the ordinary register assertions. A fourth control toggles only
CHK's documented undefined bits in returned SR; all those cases must still pass.
This proves both that defined assertions remain active and that undefined bits
are excluded from architectural comparisons. Probe callbacks are separate from
normal coverage totals. Native diagnostics now terminate appended byte/line data
so stale previous-case text cannot contaminate the reported failure.

The stronger comparator exposes additional saved-PC disagreements, including
BKPT, CHK2 and TRAPcc. The pinned TRAPcc generator raises the exception before
synchronizing PC; local newer WinUAE revision
`6ae6fb6b84bb9517e0245a80fc9bdca1a8580dde`, `gencpu.cpp`'s `i_TRAPcc` case,
synchronizes PC before raising it. M68000PM 4-189 also specifies the next
instruction-word address. This is a reference disagreement requiring generator
qualification, not permission to change Copper68k to the old expected value.
Other saved-PC and result disagreements remain unclassified pending equivalent
manual/source audits. No family is excluded and the requested audit remains red.

Schema 2 separates emulator-unsupported callbacks from value/frame mismatches.
Both fail the gate. The initial schema-2 checkpoint recorded 62 mismatching and
15 unsupported groups (77 total), with zero untested. The current Basic audit
records 1,326 passing, 47 mismatching and eight reserved-encoding unsupported
groups, with zero untested. Frame/masked-case counts and all 32 controls are
recorded separately from normal callbacks. Older bridges lacking these
assertions are rejected. Source/adapter/CPU disagreements remain open.

Integer callbacks also stop at an actual STOP/HALT boundary. The adapter does
not wake execution or advance PC to a following sentinel. Additive schema-2
`TerminalCases` row counts and the `terminalCases` total record these comparisons.
The packing checkpoint observes one such callback: 060 STOP `4E72 0000` exposes
a privilege/state disagreement against the pinned reference. It remains a
failing result requiring model/reference qualification. FPU adapter behavior
is unchanged. Current results and pinned-input caveats are recorded in
[reference qualification](../docs/COPPER68K_REFERENCE_QUALIFICATION.md#packunpk-word-stride-and-terminal-reference-boundaries--2026-10-05).

## Qualified TRAP trace preset for 040/060

The separate `TraceTraps` preset adds incoming T1/S combinations and CCR 0/31
for TRAP. It expects 256 callbacks, 128 with incoming T1 and 256 frame assertions
per model. Register, defined-X and frame corruption controls must all fail.
Missing/changed inputs, wrong model sets, unqualified source/patch/generator
identities and incomplete counts fail the requested audit.

The pinned generator incorrectly reports a pending trace after a synchronous
trap on 040/060. [MC68040UM 8.3](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
and [MC68060UM 8.2.6/8.3](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
require suppression. The preparation script applies the one-line
[trace-priority patch](../scripts/winuae/trace-priority.patch) to a separate
source file, records source/patch/executable hashes and preserves the original
checkout and all Basic inputs. The test verifies both exact manifested source
bytes and its qualified normalized-text hash, so checkout line endings do not
change the authority. This is an explicitly corrected software reference;
unchanged upstream or physical hardware qualification is not claimed.

```powershell
./scripts/prepare-copper68k-winuae.ps1 `
  -GeneratorSource artifacts/reference-winuae-api `
  -RunnerSource artifacts/reference-copperline `
  -VcVars64 '<Visual Studio>/VC/Auxiliary/Build/vcvars64.bat' `
  -Preset TraceTraps -OutputDirectory artifacts/winuae-trap-trace-inputs
$env:COPPER68K_RUN_WINUAE_TRAP_TRACE_AUDIT = '1'
$env:COPPER68K_WINUAE_TRAP_TRACE_PATH = (Resolve-Path artifacts/winuae-trap-trace-inputs).Path
$env:COPPER68K_WINUAE_CPUTEST_LIBRARY = (Resolve-Path artifacts/winuae-trap-trace-inputs/m68k_cpu_tester.dll).Path
$env:COPPER68K_SYNTHETIC_REPORT_DIR = Join-Path (Get-Location).Path 'artifacts/winuae-trap-trace-report'
dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release -p:Platform=AnyCPU `
  --filter FullyQualifiedName~WinUaeTrapTracePriorityAcross040And060WhenEnabled
# Remove these opt-in variables before running the ordinary suite.
```

`winuae-trap-trace-audit.json` records source/input/assembly identities and per-model
results, traced callback counts, frames and controls. Other traced families and
models, M-mode, fault combinations and advanced restart remain untested by this
preset. The broad Basic discovery audit retains its separate failures; this
focused preset cannot make it pass.

## Qualified long multiply/divide preset

`LongArithmetic` independently qualifies MULL.L and DIVL.L on EC020/A1200,
020, 030, 040 and 060. The original Basic corpus is preserved. Its 020/030
long-arithmetic cases contain nonzero reserved extension fields, so their
unsupported execution cannot qualify documented arithmetic semantics.

[M68000PM 4-94/98/136/140](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
marks extension bits 15 and 9..3 as zero. It also declares a 64-bit multiply
with Dh == Dl undefined. The separate
[encoding-selection patch](../scripts/winuae/long-arithmetic-encodings.patch)
clears reserved fields on every applicable model and selects a distinct Dh
for that undefined multiply form **before reference execution**. Legal divide
register aliases remain selected. The bridge checks every callback's raw words
and rejects unqualified inputs; it never changes fixture operands or returned
CPU results to satisfy the reference.

The pinned generator also synchronizes PC and reads the operand before detecting
060 unavailable 64-bit operations. Its later rollback path incorrectly restores
postincrement/predecrement for a completed divide-by-zero trap. The separate
[060 exception patch](../scripts/winuae/long-arithmetic-unimplemented.patch)
detects vector 61 before EA effects, saves the causing instruction PC as required
by [MC68060UM 8.2.4/C.2.2](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf),
and leaves ordinary divide-by-zero EA effects intact. The latter expectation
interprets the manual's 8.3 group-3 completion rule with PRM 2.2.4/5 addressing
semantics; hardware corroboration is not claimed. Copper68k already implements
these expectations. No production CPU, timing or flag-mask change is required.

```powershell
./scripts/prepare-copper68k-winuae.ps1 `
  -GeneratorSource artifacts/reference-winuae-api `
  -RunnerSource artifacts/reference-copperline `
  -VcVars64 '<Visual Studio>/VC/Auxiliary/Build/vcvars64.bat' `
  -Preset LongArithmetic -OutputDirectory artifacts/winuae-long-arithmetic-inputs
./scripts/test-copper68k-winuae-long-arithmetic.ps1 `
  -InputDirectory artifacts/winuae-long-arithmetic-inputs `
  -OutputDirectory artifacts/winuae-long-arithmetic-report
```

The audit requires both qualified source/patch identities, the recorded generator
executable/native bridge, complete profile/family selections, all exact input
hashes, and pinned per-family callback/frame/masked-CCR/architectural-form counts.
It executes 28,418 callbacks and 4,992 frames in twelve groups. Every group must
reject register and defined-X corruption; exception-bearing groups must reject
frame corruption. DIVL groups must accept changes confined to documented
undefined flags. All four signed/unsigned and 32/64-bit categories are required,
including 060 vector-61 outcomes. Coverage reports record 12,420 distinct
model/family/sign/width/EA/register combinations; this does not imply exhaustive
external addressing or operand coverage.

`winuae-long-arithmetic-audit.json` keeps passing, mismatching, unsupported,
untested and explicitly excluded encodings/profiles separate. The standalone
command restores its environment and fails on missing fixtures, empty/duplicate
selection, mismatches or incomplete scope. Ordinary CI runs the independent
encoding checks; this external audit remains opt-in. No new seeded audit,
trace/bus-fault/MMU/cache/pipeline qualification or old regression retirement is
claimed. Full current evidence and remaining milestone requirements are in
[reference qualification](../docs/COPPER68K_REFERENCE_QUALIFICATION.md#long-arithmetic-reference-qualification-2026-10-05).

## Qualified word division preset

`WordDivision` qualifies DIVS.W and DIVU.W on all seven CPU models and the
A1200 profile. [M68000PM 4-92/96](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
defines carry as cleared, including overflow and divide-by-zero, while X is
preserved. N/Z are undefined on overflow; N/Z/V are undefined on divide-by-zero.
The pinned generator's `setdivuflags` helper omits the carry clear on 020/030,
despite its own comment specifying C=0. A separate
[generator patch](../scripts/winuae/word-division-carry.patch) explicitly clears
carry after unsigned word overflow. Copper68k already implements this behavior.
No CPU result normalization, flag-mask change or physical hardware claim is made.

```powershell
./scripts/prepare-copper68k-winuae.ps1 `
  -GeneratorSource artifacts/reference-winuae-api `
  -RunnerSource artifacts/reference-copperline `
  -VcVars64 '<Visual Studio>/VC/Auxiliary/Build/vcvars64.bat' `
  -Preset WordDivision -OutputDirectory artifacts/winuae-word-division-inputs
./scripts/test-copper68k-winuae-qualified-exceptions.ps1 `
  -Preset WordDivision -InputDirectory artifacts/winuae-word-division-inputs `
  -OutputDirectory artifacts/winuae-word-division-report
```

The command requires the qualified source/patch, generator/native identities,
complete profiles/families and exact input hashes. All 134,928 callbacks, 37,804
frames, 87,936 masked-CCR cases and 11,392 model/family/EA/register/input-SR
combinations are pinned. Immutable raw opcode/SR classification rejects invalid
words or profile states and never calls production helpers. Fourteen fixed
encoding/profile checks run in ordinary CI. Every external group must reject
register, X, carry and frame corruption and accept changes confined to undefined
flags: 80 controls in total. A wrong callback count also fails the gate.

`winuae-word-division-audit.json` reports passing, mismatching, unsupported and
untested scope separately, and records architectural forms, source/input/assembly
identities and controls. The command requires all fifteen selected xUnit tests
to execute successfully. The shared qualified audit runner retains the existing
TrapBounds and Breakpoints scopes. Full indexing is enabled in generation, but
this preset does not prove every indexed structural combination or operand
boundary; the synthetic matrices retain that coverage. Incoming trace, bus
faults, physical timing and enabled MMU remain outside this preset. Original
Basic inputs and their failing discovery report remain separate. See the
[qualification record](../docs/COPPER68K_REFERENCE_QUALIFICATION.md#word-division-reference-qualification-2026-10-05)
for mutation/rejection evidence and remaining milestone requirements.
