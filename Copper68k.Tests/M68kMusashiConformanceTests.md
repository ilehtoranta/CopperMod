# Musashi conformance

`M68kMusashiConformanceTests` can run the MIT-compatible Musashi `test/mc68000`
and `test/mc68040` program corpora from the pinned upstream commit used during
integration:
`72c1d74800f3087b45a0c1a7342601bbed898881`.

Fetch the corpus outside normal tracked source files or at the default local
path:

```powershell
git clone https://github.com/kstenerud/Musashi third_party/Musashi
git -C third_party/Musashi checkout 72c1d74800f3087b45a0c1a7342601bbed898881
```

Run the MC68000 conformance test explicitly:

```powershell
$env:COPPER68K_RUN_MUSASHI_M68000 = "1"
dotnet test Copper68k.Tests/Copper68k.Tests.csproj --filter "MusashiM68000ProgramsPassInterpreterWhenEnabled"
```

Useful environment variables:

- `COPPER68K_MUSASHI_M68000_PATH`: Musashi repo root or `test/mc68000` folder.
- `COPPER68K_MUSASHI_M68000_FILTER`: file-name substring such as `move` or `div`.
- `COPPER68K_MUSASHI_M68000_LIMIT`: maximum number of `.bin` programs to run.
- `COPPER68K_MUSASHI_M68000_MAX_INSTRUCTIONS`: per-program execution cap; default is `1000000`.
- `COPPER68K_MUSASHI_M68000_CPU_MODEL`: `m68000` by default, or `m68040` to mirror Musashi's own test driver.
- `COPPER68K_MUSASHI_M68000_BACKEND`: `interpreter` by default, or `jit` for the MC68000 JIT backend.
- `COPPER68K_MUSASHI_M68000_INCLUDE_KNOWN_INCOMPATIBLE`: include programs whose expectations conflict with SingleStepTests.

The runner mirrors Musashi's dummy machine: stack/vector RAM at `0x0`, ROM from
`0x10000`, special pass/fail/output registers at `0x100000`, and extra RAM at
`0x300000`.

SingleStepTests/m68000 remains the authority for Copper68k CPU semantics. The
default Musashi run excludes four programs:

- `abcd.bin` and `sbcd.bin`: loop over byte values from `0x99` down to `0x00`,
  including invalid packed-BCD digits. Copper68k follows SingleStepTests for
  those invalid-digit cases.
- `divs.bin` and `divu.bin`: the data accumulators match Musashi, but the ROMs
  assert overflow `N/Z` flag behavior that conflicts with SingleStepTests.

Motorola documentation uses "undefined" differently from "not affected". An
undefined condition code is not architecturally guaranteed to preserve its prior
state; it may be a deterministic side effect of the internal ALU or microcode path
for a particular CPU model or mask revision. These leftovers can be useful when
matching one concrete chip, but they are not portable architectural behavior. For
Copper68k's MC68000 target, SingleStepTests/m68000 defines the chosen behavior
when Musashi asserts undefined flags or invalid packed-BCD results.

Run the MC68040 conformance test explicitly:

```powershell
$env:COPPER68K_RUN_MUSASHI_M68040 = "1"
dotnet test Copper68k.Tests/Copper68k.Tests.csproj --filter "MusashiM68040ProgramsPassInterpreterWhenEnabled"
```

Useful MC68040 environment variables:

- `COPPER68K_MUSASHI_M68040_PATH`: Musashi repo root or `test/mc68040` folder.
- `COPPER68K_MUSASHI_M68040_FILTER`: file-name substring such as `bf` or `trap`.
- `COPPER68K_MUSASHI_M68040_LIMIT`: maximum number of `.bin` programs to run.
- `COPPER68K_MUSASHI_M68040_MAX_INSTRUCTIONS`: per-program execution cap; default is `1000000`.
- `COPPER68K_MUSASHI_M68040_INCLUDE_KNOWN_FAILING`: include MC68040 programs that currently expose unsupported or divergent behavior.

The MC68040 runner uses the interpreter backend. The default run excludes one
known divergent program so the test remains useful in regular explicit audit
passes:

- `cmp2.bin`: the byte-size CMP2 carry expectation follows Musashi but conflicts
  with the test source author's inline note about the expected branch condition;
  SingleStepTests remain the authority when an instruction is covered there.

With that exclusion, the current MC68040 Musashi baseline covers the bitfield
programs, `cas.bin`, `chk2.bin`, long multiply/divide, `interrupt.bin`,
`jmp.bin`, `rtd.bin`, `shifts3.bin`, and `trapcc.bin`. Use
`COPPER68K_MUSASHI_M68040_INCLUDE_KNOWN_FAILING=1` as an audit mode when working
on the excluded program.

## m68k-rs extra fixtures

`M68kMusashiConformanceTests` can also run the m68k-rs extra fixture binaries.
These are useful as a broad secondary corpus across privilege, exception,
coverage, MC68010, MC68020, MC68030, and MC68040-focused programs. They are not
an authority over SingleStepTests; they are a regression and discovery net for
unsupported instruction families, addressing modes, and exception flows.

Fetch the corpus outside tracked source files or at the default local path:

```powershell
git clone https://github.com/benletchford/m68k-rs third_party/m68k-rs
```

The local baseline was inspected with m68k-rs commit
`9d75ddcc219ae615a2a188d7d46b148b652296a9`.

Run the m68k-rs extra fixture test explicitly:

```powershell
$env:COPPER68K_RUN_M68KRS_EXTRA = "1"
dotnet test Copper68k.Tests/Copper68k.Tests.csproj --filter "M68kRsExtraFixturesPassInterpreterWhenEnabled"
```

Useful m68k-rs environment variables:

- `COPPER68K_M68KRS_EXTRA_PATH`: m68k-rs repo root or `tests/fixtures/extra` folder.
- `COPPER68K_M68KRS_EXTRA_FILTER`: relative-path or file-name substring such as `m68020` or `fpu`.
- `COPPER68K_M68KRS_EXTRA_LIMIT`: maximum number of `.bin` programs to run.
- `COPPER68K_M68KRS_EXTRA_MAX_INSTRUCTIONS`: per-program execution cap; default is `1000000`.
- `COPPER68K_M68KRS_EXTRA_INCLUDE_KNOWN_FAILING`: include programs that currently expose unsupported or divergent behavior.

The m68k-rs runner uses the same dummy-machine shape as the Musashi runner, with
ROM at `0x10000`, the pass/fail/output device at `0x100000`, and the extra RAM
slots `0x30` through `0x3f` mirrored to the same 64 KB scratch RAM.

By default, the current m68k-rs baseline executes 77 of 127 programs and excludes
50 known failing programs. The excluded programs are still intentionally useful:
they currently map to missing MC68020+ CALLM/RTM/MOVES/MOVE16/MMU/FPU support,
full-extension indexed and memory-indirect addressing, trace/interrupt/privilege
exception differences, and a few exact instruction forms found by the fixture
prologues. Use
`COPPER68K_M68KRS_EXTRA_INCLUDE_KNOWN_FAILING=1` as audit mode when working down
those buckets.
