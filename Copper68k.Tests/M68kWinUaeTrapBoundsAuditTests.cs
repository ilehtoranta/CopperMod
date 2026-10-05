using System.Text.Json;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    private const string TrapBoundsSourceHash = "3ef386033792e585b093e55a44242b32d2cee16be7a597aa687bae7191ca449d";
    private const string TrapBoundsPatchHash = "ca94d93ec49447e853769bb93bd4e823fe313854aba59601161b35ef8f7bcca4";

    [EnvironmentFact("COPPER68K_RUN_WINUAE_TRAP_BOUNDS_AUDIT", "audit qualified WinUAE TRAPcc/CHK2 saved PCs")]
    public void WinUaeTrapAndBoundsAcrossAdvancedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "TrapBounds", "TRAP_BOUNDS", "gencpu-trap-bounds.cpp", "trap-bounds-pc.patch", "TrapBounds",
        TrapBoundsSourceHash, TrapBoundsPatchHash, "winuae-trap-bounds-audit.json",
        ["68020", "68030", "68040", "68060", "68EC020"],
        ["68EC020", "68020", "68030", "68040", "68060", "A1200"], TrapBoundsFamilies, TrapBoundsCounts,
        "TRAPcc on EC020/A1200/020/030/040/060; CHK2 B/W/L on EC020/A1200/020/030/040. Basic CCR 0/31 and user/supervisor, full extensions enabled, no incoming trace/bus faults. CHK2 is unavailable on 060 and is covered architecturally by synthetic tests, not this generated preset. Patched software reference, not unchanged upstream or silicon qualification."));

    [EnvironmentFact("COPPER68K_RUN_WINUAE_BREAKPOINT_AUDIT", "audit qualified WinUAE BKPT illegal-exception saved PCs")]
    public void WinUaeBreakpointExceptionsAcrossSelectedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "Breakpoints", "BREAKPOINT", "gencpu-breakpoints.cpp", "breakpoint-pc.patch", "Breakpoint",
        "d83606b597e5bd38efc289e0ecbd1d41843e67ecedaac72e4b9335312d2ead21", "27b0fb21a7fadbe0bf92ae42074ceaf665d7c19a83efdc841bc7730befc87a7b", "winuae-breakpoint-audit.json",
        ["68010", "68020", "68030", "68040", "68060", "68EC020"],
        ["68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"],
        _ => ["BKPT"], (_, _) => (32, 32),
        "BKPT illegal-exception fallback on 010/EC020/A1200/020/030/040/060. Basic CCR 0/31 and user/supervisor. No incoming trace/bus faults or external breakpoint instruction replacement; acknowledge pins and physical cycles are unqualified. Patched software reference, not unchanged upstream or silicon qualification."));

    private void RunQualifiedExceptionPreset(QualifiedExceptionPreset preset)
    {
        var root = Environment.GetEnvironmentVariable($"COPPER68K_WINUAE_{preset.EnvironmentKey}_PATH");
        var library = Environment.GetEnvironmentVariable($"COPPER68K_WINUAE_{preset.EnvironmentKey}_LIBRARY")
            ?? Environment.GetEnvironmentVariable(LibraryVariable);
        var output = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(output))
            throw new XunitException($"{preset.Name} audit requires qualified fixtures, native library and report directory.");
        var manifestFile = Path.Combine(root, "manifest.json");
        var json = File.ReadAllText(manifestFile);
        var manifest = JsonSerializer.Deserialize<WinUaeManifest>(json) ?? throw new XunitException($"Missing {preset.Name} manifest.");
        using var identity = JsonDocument.Parse(json);
        var fields = identity.RootElement;
        var sourcePath = Path.Combine(root, preset.SourceName);
        var normalizedSourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath).Replace("\r\n", "\n")))).ToLowerInvariant();
        if (manifest.Schema != 1 || manifest.GeneratorCommit != GeneratorPin || manifest.RunnerCommit != RunnerPin ||
            fields.GetProperty("Preset").GetString() != preset.Name || normalizedSourceHash != preset.SourceHash ||
            fields.GetProperty(preset.ManifestPrefix + "PatchSha256").GetString() != preset.PatchHash ||
            Hash(sourcePath) != fields.GetProperty(preset.ManifestPrefix + "SourceSha256").GetString() ||
            Hash(Path.Combine(root, preset.PatchName)) != preset.PatchHash ||
            Hash(Path.Combine(root, "cputester.exe")) != fields.GetProperty("GeneratorExecutableSha256").GetString() ||
            Hash(library) != manifest.NativeLibrarySha256)
            throw new XunitException($"{preset.Name} source, patch, generator or native identity is unqualified.");
        var profileIds = preset.ProfileIds;
        if (manifest.Profiles.Length != profileIds.Length || !manifest.Profiles.Select(x => x.Id).Order().SequenceEqual(profileIds.Order()))
            throw new XunitException($"{preset.Name} audit requires its complete profile selection, without empty or duplicate selections.");
        var models = preset.ModelIds.Select(id => ModelSpec.All.Single(x => x.Id == id)).ToArray();
        foreach (var model in models) ValidateWinUaeProfile(root, manifest, model, requiredOpcodes: preset.Families(model));
        var rows = new List<WinUaeQualifiedExceptionRow>();
        var probes = new List<WinUaeQualifiedExceptionProbe>();
        using var tester = NativeTester.Load(library);
        foreach (var model in models)
        {
            var fixture = manifest.Profiles.Single(x => x.Id == FixtureId(model.Id));
            var path = Path.Combine(root, fixture.Id);
            foreach (var family in preset.Families(model))
            {
                var register = tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptResult: true);
                var sr = tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptSr: 0x10);
                var frame = tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptFrame: true);
                var detected = !register.Passed && register.ExecutedCases > 0 && !sr.Passed && sr.ExecutedCases > 0 &&
                    sr.Detail.Contains("SR:", StringComparison.Ordinal) && !frame.Passed && tester.FrameChecks > 0 &&
                    frame.Detail.Contains("frame byte", StringComparison.Ordinal);
                var carry = preset.ArithmeticFlagControls ? tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptSr: 1) : default;
                var carryDetected = !preset.ArithmeticFlagControls || (!carry.Passed && carry.ExecutedCases > 0 && carry.Detail.Contains("SR:", StringComparison.Ordinal));
                var ignored = preset.ArithmeticFlagControls ? tester.Run(path, family, fixture.CpuLevel, false, false, model, corruptIgnoredSr: true) : default;
                var ignoredAccepted = !preset.ArithmeticFlagControls || (ignored.Passed && ignored.ExecutedCases > 0 && tester.MaskedCases > 0);
                detected &= carryDetected && ignoredAccepted;
                probes.Add(new(model.Id, family, detected, register.ExecutedCases, sr.ExecutedCases, frame.ExecutedCases,
                    preset.ArithmeticFlagControls, carryDetected, carry.ExecutedCases, ignoredAccepted, ignored.ExecutedCases));
                var result = tester.Run(path, family, fixture.CpuLevel, false, false, model,
                    fixtureClassifier: preset.ClassifyForm is null ? null : (opcode, inputSr) => preset.ClassifyForm(opcode, inputSr, family));
                var expected = preset.Counts(model, family);
                var forms = new SortedDictionary<string, int>(tester.FixtureForms.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal);
                var expectedForms = preset.FormCounts?.Invoke(model, family) ?? 0;
                var expectedMasked = preset.MaskedCounts?.Invoke(model, family) ?? (family.StartsWith("CHK2.", StringComparison.Ordinal) ? (uint)expected.Cases : 0);
                var passing = detected && result.Passed && result.ExecutedCases == expected.Cases &&
                    tester.FrameChecks == expected.Frames && tester.MaskedCases == expectedMasked &&
                    forms.Count == expectedForms && (preset.ClassifyForm is null || forms.Values.Sum() == result.ExecutedCases);
                var detail = $"{result.Detail} Expected/actual callbacks={expected.Cases}/{result.ExecutedCases}, frames={expected.Frames}/{tester.FrameChecks}, masked={expectedMasked}/{tester.MaskedCases}, forms={expectedForms}/{forms.Count}.";
                rows.Add(new(model.Id, family, passing ? "passing" : tester.UnsupportedExecution ? "unsupported" : result.ExecutedCases == 0 ? "untested" : "mismatching",
                    result.ExecutedCases, tester.FrameChecks, tester.MaskedCases, detected, forms, detail));
                _output.WriteLine($"{model.Id}/{family}: {result.ExecutedCases} callbacks, {tester.FrameChecks} frames; controls={detected}.");
            }
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, preset.ReportName), JsonSerializer.Serialize(new
        {
            schema = 1, reference = $"WinUAE {preset.Name} preset with explicit Motorola-qualified reference correction",
            manifest.GeneratorCommit, manifest.RunnerCommit, manifest.NativeLibrarySha256,
            manifestSha256 = Hash(manifestFile), generatorSourceSha256 = Hash(sourcePath),
            generatorNormalizedSourceSha256 = preset.SourceHash, generatorPatchSha256 = preset.PatchHash,
            adapterAssemblySha256 = Hash(typeof(M68kWinUaeCpuTesterConformanceTests).Assembly.Location),
            cpuAssemblySha256 = Hash(typeof(Copper68k.M68kCoreFactory).Assembly.Location),
            qualification = preset.Qualification,
            passing = rows.Count(x => x.Status == "passing"), mismatching = rows.Count(x => x.Status == "mismatching"),
            unsupported = rows.Count(x => x.Status == "unsupported"), executedCases = rows.Sum(x => (long)x.ExecutedCases),
            untested = rows.Count(x => x.Status == "untested"),
            exceptionFrames = rows.Sum(x => (long)x.ExceptionFrames), maskedSrCases = rows.Sum(x => (long)x.MaskedSrCases),
            architecturalForms = rows.Sum(x => x.Forms.Count),
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
    private sealed record QualifiedExceptionPreset(string Name, string EnvironmentKey, string SourceName, string PatchName,
        string ManifestPrefix, string SourceHash, string PatchHash, string ReportName, string[] ProfileIds, string[] ModelIds,
        Func<ModelSpec, string[]> Families, Func<ModelSpec, string, (int Cases, uint Frames)> Counts, string Qualification,
        bool ArithmeticFlagControls = false, Func<ModelSpec, string, uint>? MaskedCounts = null,
        Func<ushort, ushort, string, string>? ClassifyForm = null, Func<ModelSpec, string, int>? FormCounts = null);
    private sealed record WinUaeQualifiedExceptionRow(string Model, string Family, string Status, int ExecutedCases,
        uint ExceptionFrames, uint MaskedSrCases, bool Controls, SortedDictionary<string, int> Forms, string Detail);
    private sealed record WinUaeQualifiedExceptionProbe(string Model, string Family, bool Detected,
        int RegisterCases, int SrCases, int FrameCases, bool ArithmeticFlagControls,
        bool CarryDetected, int CarryCases, bool IgnoredAccepted, int IgnoredCases);
}
