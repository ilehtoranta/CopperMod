param(
    [Parameter(Mandatory)][int]$LogicalProcessor,
    [Parameter(Mandatory)][string]$ReferenceDirectory,
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$RomPath,
    [Parameter(Mandatory)][string]$AdfPath,
    [Parameter(Mandatory)][string]$InputScriptPath,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/agnus-g6-host-load.ps1"
$reference = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$rom = (Resolve-Path -LiteralPath $RomPath).Path
$adf = (Resolve-Path -LiteralPath $AdfPath).Path
$inputScript = (Resolve-Path -LiteralPath $InputScriptPath).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence directory; previous evidence is immutable.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
# Windows CPU-set topology, not a hard-coded logical-CPU policy.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativePairTopology {
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetSystemCpuSetInformation(IntPtr p,uint n,out uint size,IntPtr process,uint flags);
 public static string[] Read() {
  uint size; GetSystemCpuSetInformation(IntPtr.Zero,0,out size,IntPtr.Zero,0);
  if(size==0) throw new Exception("Missing topology");
  var p=Marshal.AllocHGlobal(checked((int)size));
  try {
   if(!GetSystemCpuSetInformation(p,size,out size,IntPtr.Zero,0)) throw new Exception("CPU topology unavailable");
   var rows=new System.Collections.Generic.List<string>();
   for(int offset=0;offset<size;) {
    var q=IntPtr.Add(p,offset); var length=Marshal.ReadInt32(q);
    if(length<8 || offset+length>size) throw new Exception("Invalid topology entry");
    if(Marshal.ReadInt32(q,4)==0) {
     if(length<20) throw new Exception("Truncated CPU set");
     rows.Add($"{Marshal.ReadInt16(q,12)},{Marshal.ReadByte(q,14)},{Marshal.ReadByte(q,15)},{Marshal.ReadByte(q,18)}");
    }
    offset+=length;
   }
   return rows.ToArray();
  } finally { Marshal.FreeHGlobal(p); }
 }
}
'@
$topology = @([NativePairTopology]::Read() | ForEach-Object {
    $v=$_.Split(','); [pscustomobject]@{Group=[int]$v[0];Cpu=[int]$v[1];Core=[int]$v[2];Efficiency=[int]$v[3]}
})
if (@($topology.Group | Sort-Object -Unique).Count -ne 1 -or $topology[0].Group -ne 0) { throw 'Multi-group telemetry is unsupported.' }
$selected = @($topology | Where-Object Cpu -eq $LogicalProcessor)
$maxClass = ($topology.Efficiency | Measure-Object -Maximum).Maximum
$minClass = ($topology.Efficiency | Measure-Object -Minimum).Minimum
if ($selected.Count -ne 1 -or $LogicalProcessor -ge 63 -or $selected[0].Efficiency -ne $maxClass -or $minClass -eq $maxClass) { throw 'Verified hybrid-host P-core required.' }
$protected = @($topology | Where-Object Core -eq $selected[0].Core | ForEach-Object Cpu)
$mask = [int64]1 -shl $LogicalProcessor
'LIGHTWEIGHT NATIVE RETENTION: candidate=current frozen build; reference=correctness-validated frozen Lightweight; NOT a production cutover'
"Topology=$($topology | ConvertTo-Json -Compress)"
"Environment OS=$([Environment]::OSVersion) processors=$([Environment]::ProcessorCount)"
dotnet --version
powercfg /GETACTIVESCHEME
git -C (Split-Path $PSScriptRoot -Parent) rev-parse HEAD
'Build identity includes dirty-tree frozen assembly hashes; HEAD alone is not the build identity.'
foreach ($directory in @($reference,$candidate)) {
    foreach ($file in Get-ChildItem -LiteralPath $directory -File | Where-Object Extension -in '.dll','.json') {
        "BUILD $($file.FullName) SHA256=$((Get-FileHash -LiteralPath $file.FullName).Hash)"
    }
}
$expectedInputs=@{
    $rom='EE05862D8102A08436AC4056DA7D549DB31625C7D47B24DFB7B3C9A5C113CA53'
    $adf='5FC56B436722688BA37836E273A3A1C84D724235A91B39272768FF3E4CAC0A59'
    $inputScript='B2CFB175BAE3DFE6609DD6A3E8C3B320EC124F996913594ED48D85F2952B5C09'
}
foreach ($path in $expectedInputs.Keys) {
    $hash=(Get-FileHash -LiteralPath $path).Hash
    "INPUT $path SHA256=$hash"
    if ($hash -ne $expectedInputs[$path]) { throw 'This fixed H6d protocol requires the frozen input identities.' }
}
$entries = Get-Content -LiteralPath $inputScript -Raw | ConvertFrom-Json
foreach ($entry in $entries | Where-Object adfPath) {
    $path=[IO.Path]::GetFullPath($entry.adfPath,(Split-Path $inputScript -Parent))
    $hash=(Get-FileHash -LiteralPath $path).Hash
    "INPUT $path SHA256=$hash"
    if($hash -ne '0C1CB9C49CBC29B56D2F974A90C510752E256148F8E8CC10CFBD026DB73DEA1A') { throw 'Disk 2 differs from the frozen workload.' }
}
$script:valid=$true
$state=@{}
function Observe($before,$after,$benchmarkId=0) {
    try {
        $load=Get-G6HostLoad $before $after $LogicalProcessor $protected
        Write-G6HostLoad $load 6>&1 | ForEach-Object { "$_" }
        # The shared historical detector predates the Lightweight runner.
        $otherRunners=@(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object {
            $_.ProcessId -ne $benchmarkId -and $_.CommandLine -match 'CopperMod\.Amiga\.Lightweight\.Runner\.dll'
        })
        if($otherRunners.Count) { throw 'Competing Lightweight runner.' }
        Update-G6HostLoadState $state $load 25 25 10
    } catch { $script:valid=$false; "INVALID / RERUN: $($_.Exception.Message)" }
}
function Cooldown {
    $before=Get-G6HostSnapshot
    $start=$before.TimeSeconds
    do {
        Start-Sleep -Seconds 1
        $after=Get-G6HostSnapshot
        Observe $before $after
        $before=$after
    } while ($after.TimeSeconds-$start -lt 10)
}
$results=@()
$order=@('R1','C1','C2','R2','R3','C3')
'PROTOCOL same script; checkpoint=10320; gameplay-warmup=600; measured-fields=10921..14520; order=R1,C1,C2,R2,R3,C3'
'OUTPUT both=908x313,int16-stereo,48000Hz; full CPU/DMA/pixels/audio; fixed validated fingerprints required.'
Cooldown
foreach ($label in $order) {
    "SAMPLE=$label utc=$([DateTime]::UtcNow.ToString('o'))"
    $directory=if($label.StartsWith('R')){$reference}else{$candidate}
    $si=[Diagnostics.ProcessStartInfo]::new('dotnet')
    $si.UseShellExecute=$false; $si.CreateNoWindow=$true
    $si.RedirectStandardOutput=$true; $si.RedirectStandardError=$true
    $flags=@('--rom',$rom,'--adf',$adf,'--input-script',$inputScript,'--warmup','10920','--frames','3600')
    $flags=@("$directory/CopperMod.Amiga.Lightweight.Runner.dll")+$flags
    foreach ($arg in $flags) { $si.ArgumentList.Add($arg) }
    $p=[Diagnostics.Process]::Start($si)
    try {
        $p.ProcessorAffinity=[IntPtr]$mask; $p.PriorityClass='Normal'
        if($p.ProcessorAffinity.ToInt64() -ne $mask -or $p.PriorityClass -ne 'Normal') { throw 'Unconfirmed placement' }
        "PLACEMENT cpu=$LogicalProcessor mask=$mask protected=$($protected -join ',') priority=Normal"
        $stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync()
        $before=Get-G6HostSnapshot $p
        do {
            $done=$p.WaitForExit(1000); $p.Refresh()
            if(!$done -and ($p.ProcessorAffinity.ToInt64() -ne $mask -or $p.PriorityClass -ne 'Normal')) { throw 'Placement changed' }
            $after=Get-G6HostSnapshot $p
            Observe $before $after $p.Id
            $before=$after
        } while (!$done)
        $output=$stdout.Result.Trim()
        "STDOUT $label $output"
        if ($p.ExitCode -ne 0) { throw "Workload failed: $($stderr.Result)" }
        $line=@($output -split '\r?\n' | Where-Object { $_ -match '^engine=' })
        if($line.Count -ne 1) { throw 'Missing/ambiguous measurement result.' }
        $line=$line[0]
        # 2026-09-14 COPJMP read-strobe repair; independent runner/host replay
        # and gameplay image verified. Earlier frozen fingerprints remain in
        # historical evidence; do not use pre-repair builds with this protocol.
        if($line -notmatch 'warmup=10920 frames=3600 completed=14520 ' -or $line -notmatch ' cycle=2063321672 ') { throw 'Different emulated interval.' }
        if ($line -notmatch 'cpu=0xE9ED169188EABAE5 hardware=0xA8DFE876EDDBBF09 output=0x514760AD9BC62BCE pixels=284204 audioSamples=1922 pcm=real allocated=0 adf=True unsupported=none$') {
            throw 'Lightweight workload differs from validated native gameplay.'
        }
        "RESULT $label $line"
        $fps=[double]::Parse(([regex]::Match($line,'fps=([\d,.]+)').Groups[1].Value.Replace(',','.')),[Globalization.CultureInfo]::InvariantCulture)
        $results += [pscustomobject]@{Label=$label;Fps=$fps;Fingerprint=($line -replace 'fps=[\d,.]+ ','')}
    } finally {
        if(!$p.HasExited) { $p.Kill(); $p.WaitForExit() }
        $p.Dispose()
    }
    Cooldown
}
$medians=@{}
foreach($kind in @('R','C')) {
    $rows=@($results | Where-Object Label -Like "$kind*")
    if($rows.Count -ne 3 -or @($rows.Fingerprint | Select-Object -Unique).Count -ne 1) { throw "Within-engine fingerprints differ: $kind" }
    $values=@($rows.Fps | Sort-Object)
    $median=$values[1]
    $spread=100*($values[-1]-$values[0])/$median
    if($spread -gt 10) { $script:valid=$false }
    $medians[$kind]=$median
    "SUMMARY $kind samples=$($rows.Fps -join ',') median=$median spread=$spread msPerFrame=$(1000/$median)"
}
"COMPARISON retentionPercent=$(100*$medians.C/$medians.R) hostAndSpreadValid=$script:valid target200=$($medians.C -ge 200)"
if(!$script:valid) { 'DISPOSITION INVALID / RERUN; no gate result replaced'; exit 2 }
'DISPOSITION valid engine-throughput retention evidence only; excludes host adapter/presentation/device pacing; no production switch.'
