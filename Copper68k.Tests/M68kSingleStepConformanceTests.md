# M68000 single-step conformance

`M68kSingleStepConformanceTests` can run the MIT-licensed
`SingleStepTests/m68000` corpus directly from its official `.json.bin` files.

Fetch the corpus outside the normal source tree or at the default local path:

```powershell
git clone https://github.com/SingleStepTests/m68000 third_party/SingleStepTests.m68000
```

Run the conformance test explicitly:

```powershell
$env:COPPER68K_RUN_M68000_SINGLESTEP = "1"
dotnet test Copper68k.Tests/Copper68k.Tests.csproj --filter "OfficialSingleStepCorpusMatchesInterpreterWhenEnabled"
```

Useful environment variables:

- `COPPER68K_M68000_SINGLESTEP_PATH`: repo root or `v1` fixture directory.
- `COPPER68K_M68000_SINGLESTEP_FILTER`: file-name substring such as `SWAP` or `ADD.w`.
- `COPPER68K_M68000_SINGLESTEP_LIMIT`: maximum number of cases to run.
- `COPPER68K_M68000_SINGLESTEP_INCLUDE_UNVERIFIED`: include corpus files the upstream README marks as caveated.
- `COPPER68K_M68000_SINGLESTEP_VALIDATE_CYCLES`: assert fixture total cycle counts in addition to final CPU state and RAM.
- `COPPER68K_M68000_SINGLESTEP_BACKEND`: `interpreter` by default, or `jit` to run the MC68000 JIT backend.

The interpreter adapter uses the public CPU factory and the existing internal
trace switch to stop before pending trace exception entry, matching the corpus
boundary without changing SR. Synthetic trace tests separately cover the ordinary
`ExecuteInstruction` boundary. JIT trace-boundary alignment is not qualified here.
Sparse fixture memory retains the 68000's intentional 24-bit wrapping.

For a pinned, recorded audit use:

```powershell
git -C third_party/SingleStepTests.m68000 checkout 64b253116a3de04aaac4346c43680960dc9b67e5
./scripts/test-copper68k-synthetic.ps1 -SingleStepPath third_party/SingleStepTests.m68000
```

This executes 312,500 cases across all 125 upstream-verified files by default.
TAS and TRAPV remain explicit upstream exclusions. `-SingleStepFilter MOVE`
selects a recorded subset. All 127 pinned files must be present and unchanged;
each selected file must execute all 2,500 cases. Missing/empty/mismatching audits
fail. `singlestep-model-audit.json` records per-file hashes, counts and bounded
failure diagnostics; `singlestep-inputs.json` identifies all inputs. Direct adapter
collection can be enabled with `COPPER68K_M68000_SINGLESTEP_AUDIT=1` and
`COPPER68K_SYNTHETIC_REPORT_DIR`; a case limit is rejected in that mode.
Only the script enforces the pinned source and full fixture selection.

These are software-reference architectural-state/fixture-RAM checks. Bus
transactions, prefetch images and physical timing are not qualified by this audit.
