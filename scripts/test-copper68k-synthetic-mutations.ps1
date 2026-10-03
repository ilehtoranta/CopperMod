[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/synthetic-mutations', [ValidateSet('All','Move','Arithmetic')] [string]$Scope = 'All')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $output) { $output = Join-Path $output ([guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$advanced = 'Copper68k/M68kAdvancedTimingInterpreter.cs'
$mutations = @(
    @{name='absolute-decode'; file=$advanced; before='(opcode & 0xFFF8) == 0x13D0'; after='(opcode & 0xF1F8) == 0x11D0'; group='EveryLegalMoveOpcode'},
    @{name='extension-length'; file='Copper68k/M68kAdvancedTimingInterpreter.Move.cs'; before='case 0: return unchecked((uint)(int)(short)FetchWord());'; after='case 0: return FetchLong();'; group='EveryLegalMoveOpcode'},
    @{name='index-sign'; file='Copper68k/M68kIntegerSemantics.cs'; before=': unchecked((int)(short)(rawIndex & 0xFFFF));'; after=': unchecked((int)(ushort)(rawIndex & 0xFFFF));'; group='MoveExtensionsAndAliases'},
    @{name='alias-order'; file=$advanced; before=@'
            WriteGeneralRegister(true, sourceRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(sourceRegister, size)));
            // Resolve the destination after source EA side effects, including An aliasing.
            WriteSized(State.A[(opcode >> 9) & 7], value, size);
'@; after=@'
            WriteSized(State.A[(opcode >> 9) & 7], value, size);
            WriteGeneralRegister(true, sourceRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(sourceRegister, size)));
'@; group='EveryLegalMoveOpcode'},
    @{name='a7-stride'; file=$advanced; before='WriteGeneralRegister(true, source, State.A[source] + (source == 7 ? 2u : 1u));'; after='WriteGeneralRegister(true, source, State.A[source] + 1u);'; group='EveryLegalMoveOpcode'},
    @{name='move-flags'; file=$advanced; before=@'
        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
'@; after=@'
        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Zero, false);
'@; group='MoveValueAndConditionCodeBoundaries'}
)
$arithmetic = 'Copper68k/M68kAdvancedTimingInterpreter.Arithmetic.cs'
$mutations += @(
    @{name='arithmetic-overflow'; file=$advanced; before=@'
            var arithmetic = M68kIntegerSemantics.CalculateAddFlags(destination, source, result, size);
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
'@; after=@'
            var arithmetic = M68kIntegerSemantics.CalculateAddFlags(destination, source, result, size);
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, false);
'@; group='ArithmeticBoundariesAndAllConditionCodes'; milestone=3},
    @{name='extend-sticky-zero'; file=$arithmetic; before='        result &= M68kCpuState.Mask(size);'; after='        // Mutant: untruncated result before sticky-zero flags.'; group='ExtendComparisonAndAliases'; milestone=3},
    @{name='decimal-alias-order'; file=$advanced; before=@'
                source = ReadByte(sourceAddress);
                var address = State.A[destinationRegister] - (destinationRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, destinationRegister, address);
                destinationAddress = State.A[destinationRegister];
'@; after=@'
                var address = State.A[destinationRegister] - (destinationRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, destinationRegister, address);
                source = ReadByte(State.A[sourceRegister]);
                destinationAddress = State.A[destinationRegister];
'@; group='DecimalAndPacking'; milestone=3},
    @{name='pack-a7-stride'; file=$arithmetic; before=@'
    private byte ReadPredecrementByte(int register)
    {
        var address = unchecked(State.A[register] - (register == 7 ? 2u : 1u));
'@; after=@'
    private byte ReadPredecrementByte(int register)
    {
        var address = unchecked(State.A[register] - 1u);
'@; group='DecimalAndPacking'; milestone=3},
    @{name='060-divide-frame'; file='Copper68k/M68060Interpreter.cs'; before='            if (vector == 5) PushLong(State.LastInstructionProgramCounter);'; after='            // Mutant: missing format-2 instruction address.'; group='MultiplyDivideAddressingAndRegisterAliases'; milestone=3}
)
if ($Scope -eq 'Move') { $mutations = @($mutations | Where-Object { $_.milestone -ne 3 }) }
if ($Scope -eq 'Arithmetic') { $mutations = @($mutations | Where-Object { $_.milestone -eq 3 }) }
$saved = @{}
foreach ($mutation in $mutations) {
    $path = Join-Path $repo $mutation.file
    if (-not $saved.ContainsKey($path)) { $saved[$path] = [IO.File]::ReadAllBytes($path) }
}
$reportDirectory = [Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
$results = @()
Push-Location $repo
try {
    foreach ($mutation in $mutations) {
        $path = Join-Path $repo $mutation.file
        $text = [Text.Encoding]::UTF8.GetString($saved[$path])
        $newline = $(if ($text.Contains("`r`n")) { "`r`n" } else { "`n" })
        $before = $mutation.before.Replace("`r`n", "`n").Replace("`n", $newline)
        $after = $mutation.after.Replace("`r`n", "`n").Replace("`n", $newline)
        if (($text.Split($before, [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Mutation anchor is not unique: $($mutation.name)" }
        $directory = Join-Path $output $mutation.name
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR', $directory)
        try {
            [IO.File]::WriteAllText($path, $text.Replace($before, $after), [Text.UTF8Encoding]::new($false))
            $model = $(if ($mutation.name -eq '060-divide-frame') { '68060' } else { '68020' })
            & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~$($mutation.group)&DisplayName~$model" --logger "trx;LogFileName=mutation.trx" --results-directory $directory *> (Join-Path $directory 'run.log')
            $exitCode = $LASTEXITCODE
            $batches = @(Get-ChildItem -LiteralPath $directory -Filter '*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
            $failures = @($batches | ForEach-Object { $_.failures })
            if ($exitCode -eq 0 -or $batches.Count -eq 0 -or $failures.Count -eq 0) { throw "Mutation survived or failed without executable semantic evidence: $($mutation.name)" }
            $results += @{mutation=$mutation.name; file=$mutation.file; sourceSha256=(Get-FileHash -LiteralPath $path).Hash;
                detected=$true; replacementCase=$failures[0].id; diagnostic=$failures[0].reason}
            Write-Host "Detected $($mutation.name): $($failures[0].id)"
        }
        finally { [IO.File]::WriteAllBytes($path, $saved[$path]) }
    }
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'mutation-proof.json')
}
finally {
    foreach ($path in $saved.Keys) { [IO.File]::WriteAllBytes($path, $saved[$path]) }
    [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR', $reportDirectory)
    # Rebuild the restored implementation; never leave mutated binaries behind.
    & dotnet build Copper68k.Tests/Copper68k.Tests.csproj -c Release --no-restore *> (Join-Path $output 'restored-build.log')
    $restoredExit = $LASTEXITCODE
    Pop-Location
    if ($restoredExit -ne 0) { throw 'Restored implementation failed to rebuild' }
}
