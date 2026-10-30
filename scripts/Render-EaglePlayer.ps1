param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$Player = 'tfmx-pro',
    [ValidateRange(1, 3600)][int]$Seconds = 30,
    [ValidateRange(0, 255)][int]$SubSong = 0
)
$ErrorActionPreference = 'Stop'
$inputFile = (Resolve-Path -LiteralPath $InputPath).Path
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($outputFile) -ine '.wav' -or $outputFile -ieq $inputFile) {
    throw 'OutputPath must be a .wav file different from InputPath.'
}
$repo = Split-Path $PSScriptRoot -Parent
$projectReference = [Security.SecurityElement]::Escape((Join-Path $repo 'CopperMod.EaglePlayer\CopperMod.EaglePlayer.csproj'))
$scratch = Join-Path ([IO.Path]::GetTempPath()) 'coppermod-eagleplayer-render'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup><Compile Include="Program.cs" /><ProjectReference Include="$projectReference" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $scratch 'Render.csproj')
@'
using CopperMod.Abstractions;
using CopperMod.EaglePlayer;
var input = args[0]; var output = args[1]; var player = args[2];
var seconds = int.Parse(args[3]); var subsong = int.Parse(args[4]);
var data = File.ReadAllBytes(input);
using var session = EaglePlayerSession.LoadConfigured(EaglePlayerReplayerCatalog.Get(player), data, new ModuleLoadContext(data, input));
if (subsong != 0) session.SelectSubSong(subsong);
const int rate = 44100, channels = 2;
var target = seconds * rate;
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var writer = new BinaryWriter(File.Create(output));
writer.Write("RIFF"u8); writer.Write(36 + target * channels * 4); writer.Write("WAVEfmt "u8);
writer.Write(16); writer.Write((short)3); writer.Write((short)channels); writer.Write(rate);
writer.Write(rate * channels * 4); writer.Write((short)(channels * 4)); writer.Write((short)32);
writer.Write("data"u8); writer.Write(target * channels * 4);
int written = 0; double energy = 0; float peak = 0;
while (written < target) {
    var frames = session.GetCurrentTickFrameCount(rate);
    var samples = new float[frames * channels];
    session.RenderQuantum(samples, frames, channels, rate);
    var count = Math.Min(frames, target - written) * channels;
    for (int i = 0; i < count; i++) {
        if (!float.IsFinite(samples[i])) throw new InvalidDataException("Non-finite audio output.");
        writer.Write(samples[i]); energy += samples[i] * samples[i]; peak = Math.Max(peak, Math.Abs(samples[i]));
    }
    written += count / channels;
}
Console.WriteLine($"Rendered {seconds}s, subsong {subsong + 1}/{session.SubSongCount}, peak={peak:F6}, RMS={Math.Sqrt(energy / (target * channels)):F6}: {output}");
foreach (var diagnostic in session.Diagnostics) Console.WriteLine(diagnostic);
if (session.Diagnostics.Any(d => d.Code == "CUST_CPU_OVERRUN" || d.Severity == ModuleDiagnosticSeverity.Error)) Environment.ExitCode = 1;
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
dotnet run --project (Join-Path $scratch 'Render.csproj') -c Release -- $inputFile $outputFile $Player $Seconds $SubSong
if ($LASTEXITCODE -ne 0) { throw "EaglePlayer render failed with exit code $LASTEXITCODE." }
