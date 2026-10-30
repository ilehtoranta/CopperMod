param(
    [Parameter(Mandatory)][string]$InputPath,
    [string]$Version = '1.1.0',
    [string]$PrivateRomPath = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$root = Join-Path $repo "artifacts\release\CopperMod\$Version"
$fixture = (Resolve-Path -LiteralPath $InputPath).Path
$smoke = Join-Path $root ('smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smoke | Out-Null
$oldCache = $env:COPPERMOD_REPLAYER_CACHE
$env:COPPERMOD_REPLAYER_CACHE = Join-Path $smoke 'replayer-cache'
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class CopperModReleaseWave {
    public static bool ContainsRomSignature(string path, byte[] signature) =>
        File.ReadAllBytes(path).AsSpan().IndexOf(signature) >= 0;
    public static double Peak(string path) {
        using var r = new BinaryReader(File.OpenRead(path));
        if (new string(r.ReadChars(4)) != "RIFF") throw new Exception("Missing RIFF header");
        r.ReadInt32(); if (new string(r.ReadChars(4)) != "WAVE") throw new Exception("Missing WAVE header");
        bool format = false;
        while (r.BaseStream.Position + 8 <= r.BaseStream.Length) {
            var id = new string(r.ReadChars(4)); var size = r.ReadUInt32(); var end = r.BaseStream.Position + size;
            if (id == "fmt ") { format = r.ReadUInt16() == 3 && r.ReadUInt16() == 2 && r.ReadInt32() == 44100; }
            if (id == "data") {
                if (!format || size != 6 * 44100 * 2 * 4) throw new Exception("Unexpected audio format or duration");
                double peak = 0;
                while (r.BaseStream.Position < end) { var sample = r.ReadSingle(); if (!float.IsFinite(sample)) throw new Exception("Non-finite sample"); peak = Math.Max(peak, Math.Abs(sample)); }
                if (peak < 0.001 || peak > 1) throw new Exception("Silent or out-of-range output");
                return peak;
            }
            r.BaseStream.Position = end + (size & 1);
        }
        throw new Exception("Missing audio data");
    }
}
'@
$results = @()
try {
    foreach ($flavor in 'win-x64-self-contained', 'dotnet') {
        $archive = Join-Path $root "CopperMod-$Version-$flavor.zip"
        $hash = (Get-FileHash -LiteralPath $archive).Hash
        if (-not (Select-String -LiteralPath (Join-Path $root 'SHA256SUMS.txt') -SimpleMatch "$hash  $([IO.Path]::GetFileName($archive))")) {
            throw "Archive checksum does not match: $archive"
        }
        $destination = Join-Path $smoke $flavor
        Expand-Archive -LiteralPath $archive -DestinationPath $destination
        $files = Get-ChildItem -LiteralPath $destination -Recurse -File
        if ($PrivateRomPath) {
            $rom = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $PrivateRomPath).Path)
            if ($rom.Length -lt 256) { throw 'ROM signature input is too short.' }
            $signature = [byte[]]$rom[0..255]
            foreach ($file in $files) {
                if ([CopperModReleaseWave]::ContainsRomSignature($file.FullName, $signature)) {
                    throw "Private ROM signature found in $($file.Name)."
                }
            }
        }
        if (@($files | Where-Object { $_.Extension -in '.rom', '.bin', '.sam', '.tfx', '.wav', '.adf', '.hdf' -or $_.Name -match 'CopperStart|SystemRom|DiskBoot' }).Count) {
            throw 'Private ROM or media found in archive.'
        }
        if ($flavor -eq 'dotnet') {
            $executable = 'dotnet'; $prefix = @((Join-Path $destination 'CopperMod.dll'))
            # Inspect managed release resources without instantiating emulator code.
            foreach ($dll in $files | Where-Object Name -like 'Copper*.dll') {
                $assembly = [Reflection.Assembly]::LoadFile($dll.FullName)
                $resources = $assembly.GetManifestResourceNames()
                if (@($resources | Where-Object { $_ -match '(?i)\.rom$|SystemRom|DiskBoot|CopperStart' }).Count) {
                    throw "Private ROM resource found in $($dll.Name)."
                }
            }
        }
        else { $executable = Join-Path $destination 'CopperMod.exe'; $prefix = @() }
        $versionText = & $executable @prefix --version
        if ($LASTEXITCODE -ne 0 -or $versionText -ne "CopperMod $Version") { throw 'Packaged version command failed.' }
        $wave = Join-Path $smoke "$flavor.wav"
        & $executable @prefix --render $fixture --out $wave --seconds 6
        if ($LASTEXITCODE -ne 0) { throw "Packaged render failed: $flavor" }
        $peak = [CopperModReleaseWave]::Peak($wave)
        $results += [pscustomobject]@{ flavor = $flavor; version = $versionText; peak = $peak; waveSha256 = (Get-FileHash $wave).Hash; archiveSha256 = $hash }
    }
    if ($results[0].waveSha256 -ne $results[1].waveSha256) { throw 'The release flavors rendered different audio.' }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $smoke 'results.json')
    $results | Format-Table
    Write-Host "Package checks passed. Cold-cache acquisition and both renders are recorded in $smoke"
}
finally { $env:COPPERMOD_REPLAYER_CACHE = $oldCache }
