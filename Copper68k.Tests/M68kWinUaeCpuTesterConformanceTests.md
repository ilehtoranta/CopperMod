# WinUAE cputest conformance

`M68kWinUaeCpuTesterConformanceTests` can run WinUAE's `cputest/gencpu`
MC68000 data through the Copper68k interpreter by using the host-side runner
shape from Copperline's `crates/cputest-runner`.

This runner is disabled by default and is a secondary discovery net.
`SingleStepTests/m68000` remains the authority for Copper68k MC68000 semantics,
especially for undefined CCR/SR bits and other model-specific leftovers.

Fetch Copperline and generate the external data outside tracked source files or
at the default local path:

```powershell
git clone https://github.com/LinuxJedi/Copperline third_party/Copperline
bash third_party/Copperline/crates/cputest-runner/tools/cputest-gen.sh third_party/winuae-cputest 68000
```

`cputest-gen.sh` pins `emoon/m68k_cpu_tester_api` at the commit Copperline's
vendored runner came from, builds the WinUAE gencpu chain with `c++`, writes the
generated data under `third_party/winuae-cputest/68000`, and uses
`feature_flags_mode=1` so officially undefined flag bits are not verified.

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
