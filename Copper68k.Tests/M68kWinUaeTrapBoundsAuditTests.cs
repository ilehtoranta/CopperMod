using System.Text.Json;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    private const string TrapBoundsSourceHash = "3ef386033792e585b093e55a44242b32d2cee16be7a597aa687bae7191ca449d";
    private const string TrapBoundsPatchHash = "ca94d93ec49447e853769bb93bd4e823fe313854aba59601161b35ef8f7bcca4";

    [EnvironmentFact("COPPER68K_RUN_WINUAE_TRAP_BOUNDS_AUDIT", "audit qualified WinUAE TRAPcc/CHK2 saved PCs")]
    public void WinUaeTrapAndBoundsAcrossAdvancedModelsWhenEnabled()
    {
        var root = Environment.GetEnvironmentVariable("COPPER68K_WINUAE_TRAP_BOUNDS_PATH");
        var library = Environment.GetEnvironmentVariable(LibraryVariable);
        var output = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(output))
            throw new XunitException("Trap/bounds audit requires qualified fixtures, native library and report directory.");
        var manifestFile = Path.Combine(root, "manifest.json");
        var json = File.ReadAllText(manifestFile);
        var manifest = JsonSerializer.Deserialize<WinUaeManifest>(json) ?? throw new XunitException("Missing trap/bounds manifest.");
        using var identity = JsonDocument.Parse(json);
        var fields = identity.RootElement;
        var sourcePath = Path.Combine(root, "gencpu-trap-bounds.cpp");
        var normalizedSourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath).Replace("\r\n", "\n")))).ToLowerInvariant();
        if (manifest.Schema != 1 || manifest.GeneratorCommit != GeneratorPin || manifest.RunnerCommit != RunnerPin ||
            fields.GetProperty("Preset").GetString() != "TrapBounds" || normalizedSourceHash != TrapBoundsSourceHash ||
            fields.GetProperty("TrapBoundsPatchSha256").GetString() != TrapBoundsPatchHash ||
            Hash(sourcePath) != fields.GetProperty("TrapBoundsSourceSha256").GetString() ||
            Hash(Path.Combine(root, "trap-bounds-pc.patch")) != TrapBoundsPatchHash ||
            Hash(Path.Combine(root, "cputester.exe")) != fields.GetProperty("GeneratorExecutableSha256").GetString() ||
            Hash(library) != manifest.NativeLibrarySha256)
            throw new XunitException("Trap/bounds source, patch, generator or native identity is unqualified.");
        var profileIds = new[] { "68020", "68030", "68040", "68060", "68EC020" };
        if (manifest.Profiles.Length != profileIds.Length || !manifest.Profiles.Select(x => x.Id).Order().SequenceEqual(profileIds.Order()))
            throw new XunitException("Trap/bounds audit requires all five advanced profiles, without empty or duplicate selections.");
        var models = ModelSpec.All.Where(x => x.Id is not ("68000" or "68010")).ToArray();
        foreach (var model in models) ValidateWinUaeProfile(root, manifest, model, requiredOpcodes: TrapBoundsFamilies(model));
        var rows = new List<WinUaeTrapBoundsRow>();
        var probes = new List<WinUaeTrapBoundsProbe>();
        using var tester = NativeTester.Load(library);
        foreach (var model in models)
        {
            var fixture = manifest.Profiles.Single(x => x.Id == FixtureId(model.Id));
            var path = Path.Combine(root, fixture.Id);
            foreach (var family in TrapBoundsFamilies(model))
            {
                var register = tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptResult: true);
                var sr = tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptSr: 0x10);
                var frame = tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptFrame: true);
                var detected = !register.Passed && register.ExecutedCases > 0 && !sr.Passed && sr.ExecutedCases > 0 &&
                    sr.Detail.Contains("SR:", StringComparison.Ordinal) && !frame.Passed && tester.FrameChecks > 0 &&
                    frame.Detail.Contains("frame byte", StringComparison.Ordinal);
                probes.Add(new(model.Id, family, detected, register.ExecutedCases, sr.ExecutedCases, frame.ExecutedCases));
                var result = tester.Run(path, family, fixture.CpuLevel, false, false, model);
                var expected = TrapBoundsCounts(model, family);
                var passing = detected && result.Passed && result.ExecutedCases == expected.Cases &&
                    tester.FrameChecks == expected.Frames && tester.MaskedCases == (family == "TRAPcc" ? 0 : expected.Cases);
                rows.Add(new(model.Id, family, passing ? "passing" : tester.UnsupportedExecution ? "unsupported" : "mismatching",
                    result.ExecutedCases, tester.FrameChecks, tester.MaskedCases, detected, result.Detail));
                _output.WriteLine($"{model.Id}/{family}: {result.ExecutedCases} callbacks, {tester.FrameChecks} frames; controls={detected}.");
            }
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "winuae-trap-bounds-audit.json"), JsonSerializer.Serialize(new
        {
            schema = 1, reference = "WinUAE trap/bounds preset with explicit Motorola-qualified saved-PC correction",
            manifest.GeneratorCommit, manifest.RunnerCommit, manifest.NativeLibrarySha256,
            manifestSha256 = Hash(manifestFile), generatorSourceSha256 = Hash(sourcePath),
            generatorNormalizedSourceSha256 = TrapBoundsSourceHash, generatorPatchSha256 = TrapBoundsPatchHash,
            adapterAssemblySha256 = Hash(typeof(M68kWinUaeCpuTesterConformanceTests).Assembly.Location),
            cpuAssemblySha256 = Hash(typeof(Copper68k.M68kCoreFactory).Assembly.Location),
            qualification = "TRAPcc on EC020/A1200/020/030/040/060; CHK2 B/W/L on EC020/A1200/020/030/040. Basic CCR 0/31 and user/supervisor, full extensions enabled, no incoming trace/bus faults. CHK2 is unavailable on 060 and is covered architecturally by synthetic tests, not this generated preset. Patched software reference, not unchanged upstream or silicon qualification.",
            passing = rows.Count(x => x.Status == "passing"), mismatching = rows.Count(x => x.Status == "mismatching"),
            unsupported = rows.Count(x => x.Status == "unsupported"), executedCases = rows.Sum(x => (long)x.ExecutedCases),
            exceptionFrames = rows.Sum(x => (long)x.ExceptionFrames), maskedSrCases = rows.Sum(x => (long)x.MaskedSrCases),
            probes, rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(rows.All(x => x.Status == "passing"), string.Join(Environment.NewLine,
            rows.Where(x => x.Status != "passing").Select(x => $"{x.Model}/{x.Family}: controls={x.Controls}; {x.Detail}")));
    }

    private static string[] TrapBoundsFamilies(ModelSpec model) => model.Id == "68060"
        ? ["TRAPcc"] : ["CHK2.B", "CHK2.W", "CHK2.L", "TRAPcc"];
    private static (int Cases, uint Frames) TrapBoundsCounts(ModelSpec model, string family) =>
        (FixtureId(model.Id), family) switch
        {
            (_, "TRAPcc") => (156160, 78080),
            ("68EC020", "CHK2.B") => (1074, 628),
            ("68EC020", "CHK2.W") => (1048, 534),
            ("68EC020", "CHK2.L") => (1130, 546),
            (_, "CHK2.B") => (912, 542),
            (_, "CHK2.W") => (900, 533),
            (_, "CHK2.L") => (874, 406),
            _ => throw new XunitException($"Unqualified trap/bounds family: {model.Id}/{family}")
        };
    private sealed record WinUaeTrapBoundsRow(string Model, string Family, string Status, int ExecutedCases,
        uint ExceptionFrames, uint MaskedSrCases, bool Controls, string Detail);
    private sealed record WinUaeTrapBoundsProbe(string Model, string Family, bool Detected,
        int RegisterCases, int SrCases, int FrameCases);
}
