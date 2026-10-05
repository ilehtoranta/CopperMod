using System.Text.Json;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    internal const string LongArithmeticSourceHash = "0012b9bd9cd2a172c4c321a8cf8e9f1095d32f824286518e61c9d4e75bc5eb43";
    internal const string LongArithmeticPatchHash = "44e8dacde5a11e9cbd2e15119362a09b35c2e109fbbdbecbefa92929e89b32ea";
    internal const string LongArithmeticCpuSourceHash = "f1ec43c3658de9ae99e6e60f8376979f562c3b55d338a92e5d4f4849434b0799";
    internal const string LongArithmeticCpuPatchHash = "75a9e8d7ce2f534d2b16910dffe436e9d8396b3f470628bc59de9852898e0733";
    private static readonly string[] LongArithmeticFamilies = ["DIVL.L", "MULL.L"];
    private static readonly string[] LongArithmeticProfiles = ["68EC020", "68020", "68030", "68040", "68060"];

    [EnvironmentFact("COPPER68K_RUN_WINUAE_LONG_ARITHMETIC_AUDIT", "audit qualified legal WinUAE long multiply/divide encodings")]
    public void WinUaeLongArithmeticAcrossAdvancedModelsWhenEnabled()
    {
        var root = Environment.GetEnvironmentVariable("COPPER68K_WINUAE_LONG_ARITHMETIC_PATH");
        var library = Environment.GetEnvironmentVariable("COPPER68K_WINUAE_LONG_ARITHMETIC_LIBRARY");
        var output = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(output))
            throw new XunitException("LongArithmetic audit requires qualified fixtures, native library and report directory.");
        var manifest = ValidateLongArithmeticIdentity(root, library);
        var models = LongArithmeticProfiles.Append("A1200").Select(id => ModelSpec.All.Single(m => m.Id == id)).ToArray();
        foreach (var model in models) ValidateWinUaeProfile(root, manifest, model, requiredOpcodes: LongArithmeticFamilies);
        var rows = new List<LongArithmeticRow>();
        var failures = new List<string>();
        using var tester = NativeTester.Load(library);
        foreach (var model in models)
        foreach (var family in LongArithmeticFamilies)
        {
            var fixture = manifest.Profiles.Single(p => p.Id == FixtureId(model.Id));
            var path = Path.Combine(root, fixture.Id);
            var register = tester.Run(path, family, fixture.CpuLevel, false, false, model,
                corruptResult: true, qualifyLongArithmetic: true);
            var sr = tester.Run(path, family, fixture.CpuLevel, false, false, model,
                corruptSr: 0x10, qualifyLongArithmetic: true);
            var frameRequired = family == "DIVL.L" || model.Id == "68060";
            var frame = frameRequired ? tester.Run(path, family, fixture.CpuLevel, false, false, model,
                corruptFrame: true, qualifyLongArithmetic: true) : default;
            var frameDetected = !frameRequired || (!frame.Passed && tester.FrameChecks > 0 &&
                frame.Detail.Contains("frame byte", StringComparison.Ordinal));
            var ignoredRequired = family == "DIVL.L";
            var ignored = ignoredRequired ? tester.Run(path, family, fixture.CpuLevel, false, false, model,
                corruptIgnoredSr: true, qualifyLongArithmetic: true) : default;
            var ignoredAccepted = !ignoredRequired || (ignored.Passed && ignored.ExecutedCases > 0 && tester.MaskedCases > 0);
            var controls = !register.Passed && register.ExecutedCases > 0 &&
                !sr.Passed && sr.ExecutedCases > 0 && sr.Detail.Contains("SR:", StringComparison.Ordinal) &&
                frameDetected && ignoredAccepted;
            var result = tester.Run(path, family, fixture.CpuLevel, false, false, model, qualifyLongArithmetic: true);
            var forms = new SortedDictionary<string, int>(tester.LongArithmeticForms.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal);
            var expected = LongArithmeticCounts(model.Id, family);
            var passing = controls && result.Passed && result.ExecutedCases == expected.Cases &&
                tester.FrameChecks == expected.Frames && tester.MaskedCases == expected.Masked &&
                forms.Count == expected.Forms && forms.Values.Sum() == result.ExecutedCases &&
                new[] { "signed/32/", "signed/64/", "unsigned/32/", "unsigned/64/" }.All(prefix => forms.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)));
            var status = passing ? "passing" : tester.UnsupportedExecution ? "unsupported" : result.ExecutedCases == 0 ? "untested" : "mismatching";
            var detail = $"{result.Detail} Expected/actual callbacks={expected.Cases}/{result.ExecutedCases}, frames={expected.Frames}/{tester.FrameChecks}, masked={expected.Masked}/{tester.MaskedCases}, forms={expected.Forms}/{forms.Count}.";
            rows.Add(new(model.Id, family, status, result.ExecutedCases, tester.FrameChecks,
                tester.MaskedCases, controls, register.ExecutedCases, sr.ExecutedCases, frameRequired,
                frameDetected, frame.ExecutedCases, ignoredRequired, ignoredAccepted, ignored.ExecutedCases, forms, detail));
            if (!passing) failures.Add($"{model.Id}/{family}: controls={controls}; {detail}");
            _output.WriteLine($"{model.Id}/{family}: {result.ExecutedCases} callbacks, {tester.FrameChecks} frames; {status}; controls={controls}.");
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "winuae-long-arithmetic-audit.json"), JsonSerializer.Serialize(new
        {
            schema = 1, reference = "WinUAE LongArithmetic preset with Motorola-qualified legal extension selection",
            manifest.GeneratorCommit, manifest.RunnerCommit, manifest.NativeLibrarySha256,
            manifestSha256 = Hash(Path.Combine(root, "manifest.json")), generatorNormalizedSourceSha256 = LongArithmeticSourceHash,
            generatorPatchSha256 = LongArithmeticPatchHash,
            cpuGeneratorNormalizedSourceSha256 = LongArithmeticCpuSourceHash, cpuGeneratorPatchSha256 = LongArithmeticCpuPatchHash,
            adapterAssemblySha256 = Hash(typeof(M68kWinUaeCpuTesterConformanceTests).Assembly.Location),
            cpuAssemblySha256 = Hash(typeof(M68kCoreFactory).Assembly.Location),
            selectedModels = models.Select(m => m.Id),
            excludedProfiles = new[] { new { model = "68000", reason = "Long multiply/divide is architecturally unavailable; synthetic instruction/exception coverage is retained." }, new { model = "68010", reason = "Long multiply/divide is architecturally unavailable; synthetic instruction/exception coverage is retained." } },
            excludedEncodings = new[] { "Nonzero reserved extension fields (bits 15 and 9..3): not qualified as architectural arithmetic by this preset.", "64-bit multiply with Dh == Dl: result architecturally undefined; the generator selects distinct registers before reference execution." },
            passing = rows.Count(r => r.Status == "passing"), mismatching = rows.Count(r => r.Status == "mismatching"),
            unsupported = rows.Count(r => r.Status == "unsupported"), untested = rows.Count(r => r.Status == "untested"),
            executedCases = rows.Sum(r => r.ExecutedCases), exceptionFrames = rows.Sum(r => r.ExceptionFrames),
            maskedSrCases = rows.Sum(r => r.MaskedSrCases), architecturalForms = rows.Sum(r => r.Forms.Count),
            qualification = "MULL.L/DIVL.L on EC020/A1200/020/030/040/060; extension reserved fields zeroed before reference execution; undefined 64-bit multiply Dh==Dl replaced by a distinct Dh. DIVL aliases retained. Both signs and 32/64-bit forms remain selected, including 060 architectural unimplemented-integer exceptions. MC68060UM C.2.2/8.2.4 qualifies a separate 060 early vector-61 path with the causing instruction PC, before operand EA effects; 8.3 group-3 traps occur after instruction execution, interpreted with PRM 2.2.4/5 to preserve divide-by-zero postincrement/predecrement. No bridge input normalization, family exclusion or changed flag mask. Patched software reference, not unchanged Basic or hardware/timing qualification. Basic CCR 0/31; full addressing extensions enabled, no incoming trace/bus faults.",
            rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static (int Cases, uint Frames, uint Masked, int Forms) LongArithmeticCounts(string model, string family) =>
        (FixtureId(model), family) switch
        {
            ("68EC020", "DIVL.L") => (2422, 132, 686, 1054),
            ("68EC020", "MULL.L") => (2456, 0, 0, 1077),
            ("68060", "DIVL.L") => (3178, 2068, 94, 1422),
            ("68060", "MULL.L") => (3226, 2018, 0, 1411),
            ("68020" or "68030" or "68040", "DIVL.L") => (2040, 214, 584, 881),
            ("68020" or "68030" or "68040", "MULL.L") => (2046, 0, 0, 894),
            _ => throw new XunitException($"Unqualified LongArithmetic family: {model}/{family}")
        };

    private sealed record LongArithmeticRow(string Model, string Family, string Status, int ExecutedCases,
        uint ExceptionFrames, uint MaskedSrCases, bool Controls, int RegisterControlCases, int SrControlCases,
        bool FrameControlRequired, bool FrameDetected, int FrameControlCases, bool IgnoredControlRequired,
        bool IgnoredAccepted, int IgnoredControlCases, SortedDictionary<string, int> Forms, string Detail);

    internal static WinUaeManifest ValidateLongArithmeticIdentity(string root, string library)
    {
        var json = File.ReadAllText(Path.Combine(root, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<WinUaeManifest>(json) ?? throw new XunitException("Missing LongArithmetic manifest.");
        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement;
        var source = Path.Combine(root, "cputest-long-arithmetic.cpp");
        var normalizedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(source).Replace("\r\n", "\n")))).ToLowerInvariant();
        var cpuSource = Path.Combine(root, "gencpu-long-arithmetic.cpp");
        var cpuNormalizedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(cpuSource).Replace("\r\n", "\n")))).ToLowerInvariant();
        if (manifest.Schema != 1 || manifest.GeneratorCommit != GeneratorPin || manifest.RunnerCommit != RunnerPin ||
            fields.GetProperty("Preset").GetString() != "LongArithmetic" || normalizedHash != LongArithmeticSourceHash ||
            Hash(source) != fields.GetProperty("LongArithmeticSourceSha256").GetString() ||
            fields.GetProperty("LongArithmeticPatchSha256").GetString() != LongArithmeticPatchHash ||
            Hash(Path.Combine(root, "long-arithmetic-encodings.patch")) != LongArithmeticPatchHash ||
            cpuNormalizedHash != LongArithmeticCpuSourceHash ||
            Hash(cpuSource) != fields.GetProperty("LongArithmeticCpuSourceSha256").GetString() ||
            fields.GetProperty("LongArithmeticCpuPatchSha256").GetString() != LongArithmeticCpuPatchHash ||
            Hash(Path.Combine(root, "long-arithmetic-unimplemented.patch")) != LongArithmeticCpuPatchHash ||
            Hash(Path.Combine(root, "cputester.exe")) != fields.GetProperty("GeneratorExecutableSha256").GetString() ||
            Hash(library) != manifest.NativeLibrarySha256)
            throw new XunitException("LongArithmetic source, patch, generator or native identity is unqualified.");
        if (manifest.Profiles.Length != LongArithmeticProfiles.Length ||
            !manifest.Profiles.Select(p => p.Id).Order().SequenceEqual(LongArithmeticProfiles.Order()))
            throw new XunitException("LongArithmetic requires its complete profile selection without empty or duplicate selections.");
        return manifest;
    }

    // Independent fixture encoding check: M68000PM 4-94/98/136/140. It must
    // reject unqualified input rather than reinterpret it or calculate CPU results.
    internal static string QualifyLongArithmeticEncoding(ushort opcode, ushort extension, string family)
    {
        var expected = family switch { "MULL.L" => 0x4c00, "DIVL.L" => 0x4c40, _ => -1 };
        var mode = (opcode >> 3) & 7;
        var register = opcode & 7;
        if (expected < 0 || (opcode & 0xffc0) != expected || mode == 1 || (mode == 7 && register > 4) ||
            (extension & 0x83f8) != 0 ||
            (family == "MULL.L" && (extension & 0x0400) != 0 && ((extension >> 12) & 7) == (extension & 7)))
            throw new XunitException($"Unqualified long-arithmetic encoding: {family}/{opcode:X4}/{extension:X4}");
        return $"{((extension & 0x0800) != 0 ? "signed" : "unsigned")}/{((extension & 0x0400) != 0 ? "64" : "32")}/ea={mode}:{register}/primary={(extension >> 12) & 7}/secondary={extension & 7}";
    }
}

public sealed class M68kWinUaeLongArithmeticEncodingTests
{
    [Theory]
    [InlineData(0x4c00, 0x0000, "MULL.L", "unsigned/32/ea=0:0/primary=0/secondary=0")]
    [InlineData(0x4c39, 0x7c06, "MULL.L", "signed/64/ea=7:1/primary=7/secondary=6")]
    [InlineData(0x4c7b, 0x4404, "DIVL.L", "unsigned/64/ea=7:3/primary=4/secondary=4")]
    [InlineData(0x4c7c, 0x3803, "DIVL.L", "signed/32/ea=7:4/primary=3/secondary=3")]
    public void FixedLegalEncodingsRemainDistinct(int opcode, int extension, string family, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyLongArithmeticEncoding((ushort)opcode, (ushort)extension, family));

    [Theory]
    [InlineData(0x8000)] [InlineData(0x0200)] [InlineData(0x0100)] [InlineData(0x0080)]
    [InlineData(0x0040)] [InlineData(0x0020)] [InlineData(0x0010)] [InlineData(0x0008)]
    public void EveryReservedExtensionFieldIsRejected(int bit)
    {
        foreach (var (opcode, family) in new[] { (0x4c00, "MULL.L"), (0x4c40, "DIVL.L") })
            Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyLongArithmeticEncoding((ushort)opcode, (ushort)bit, family));
    }

    [Theory]
    [InlineData(0x4c00, 0x0400, "MULL.L")]
    [InlineData(0x4c39, 0x7c07, "MULL.L")]
    [InlineData(0x4c08, 0x0000, "MULL.L")]
    [InlineData(0x4c7d, 0x0000, "DIVL.L")]
    [InlineData(0x4c00, 0x0000, "DIVL.L")]
    [InlineData(0x4c40, 0x0000, "MULL.L")]
    public void UndefinedAliasesAndInvalidWordsAreRejected(int opcode, int extension, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyLongArithmeticEncoding((ushort)opcode, (ushort)extension, family));
}
