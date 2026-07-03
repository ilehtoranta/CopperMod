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
