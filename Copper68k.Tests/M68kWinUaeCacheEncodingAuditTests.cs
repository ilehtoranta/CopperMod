using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_CACHE_ENCODINGS_AUDIT", "audit qualified WinUAE cache scope-zero exception encodings")]
    public void WinUaeCacheScopeZeroAcrossSelectedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "CacheEncodings", "CACHE_ENCODINGS", "cputest-cache-encodings.cpp", "cache-encodings.patch", "CacheEncodings",
        "5f6df41a9b6e5e96088c0c81c65f31d9363a1904e6a37c3001ebf4cf4105ab92", "ef393d99b50198f1c0139de12d890f1f2c3cc0675bee19d22259de698c68e32e", "winuae-cache-encodings-audit.json",
        ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060"],
        ModelSpec.All.Select(x => x.Id).ToArray(), _ => ["ILLEGAL"], (_, _) => (256, 256),
        "M68000PM 6-4/9 and MC68060UM D-12: every cache scope-00 word takes vector 4 on 040/060, before privilege effects; earlier profiles take vector 11. Copied input generator corrects that exception and selects only these encodings before reference execution. All 64 scope-00 words, CCR 0/31 and both privilege states; exact form distribution required. Original Basic inputs/failures retained. Legal cache operations remain upstream-excluded and synthetic coverage. No physical cache/bus effects, fault restart, incoming trace or timing qualification.",
        FormCounts: (_, _) => 256, ClassifyWords: (model, opcode, _, _, sr, family) => QualifyCacheScopeEncoding(model, opcode, sr, family),
        ExpectedForms: CacheScopeForms, SavedPcControls: true));

    internal static string QualifyCacheScopeEncoding(ModelSpec model, ushort opcode, ushort sr, string family)
    {
        if ((opcode & 0xff18) != 0xf400 || family != "ILLEGAL" || sr is not (0 or 0x1f or 0x2000 or 0x201f))
            throw new XunitException($"Unqualified cache scope encoding: {model.Id}/{family}/{opcode:X4}/{sr:X4}");
        return $"ILLEGAL/op={opcode:X4}/sr={sr:X4}/vector={(model.Id is "68040" or "68060" ? 4 : 11)}";
    }

    private static IReadOnlyDictionary<string, int> CacheScopeForms(ModelSpec model, string family)
    {
        var forms = new SortedDictionary<string, int>(StringComparer.Ordinal);
        // Fixed independent encoding expansion, not decoder/classifier output.
        foreach (var prefix in new[] { 0xf400, 0xf420, 0xf440, 0xf460, 0xf480, 0xf4a0, 0xf4c0, 0xf4e0 })
        for (var register = 0; register < 8; register++)
        foreach (var sr in new[] { 0, 0x1f, 0x2000, 0x201f })
            forms.Add($"ILLEGAL/op={prefix + register:X4}/sr={sr:X4}/vector={(model.Id is "68040" or "68060" ? 4 : 11)}", 1);
        return forms;
    }
}

public sealed class M68kWinUaeCacheScopeEncodingTests
{
    [Theory]
    [InlineData("68040", 0xf400, 0, "ILLEGAL/op=F400/sr=0000/vector=4")]
    [InlineData("68060", 0xf467, 0x201f, "ILLEGAL/op=F467/sr=201F/vector=4")]
    [InlineData("68060", 0xf4e7, 0x1f, "ILLEGAL/op=F4E7/sr=001F/vector=4")]
    [InlineData("68000", 0xf400, 0x2000, "ILLEGAL/op=F400/sr=2000/vector=11")]
    [InlineData("68030", 0xf4c0, 0x201f, "ILLEGAL/op=F4C0/sr=201F/vector=11")]
    [InlineData("A1200", 0xf420, 0, "ILLEGAL/op=F420/sr=0000/vector=11")]
    public void FixedManualExamplesDistinguishScopeAndModel(string modelId, int opcode, int sr, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyCacheScopeEncoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)sr, "ILLEGAL"));

    [Theory]
    [InlineData(0xf408, 0, "ILLEGAL")]
    [InlineData(0xf410, 0, "ILLEGAL")]
    [InlineData(0xf418, 0, "ILLEGAL")]
    [InlineData(0xf500, 0, "ILLEGAL")]
    [InlineData(0xf400, 0xa000, "ILLEGAL")]
    [InlineData(0xf400, 0, "CINVL")]
    public void LegalScopesForeignFamiliesAndUnrequestedStatusAreRejected(int opcode, int sr, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCacheScopeEncoding(ModelSpec.All.Single(x => x.Id == "68040"), (ushort)opcode, (ushort)sr, family));
}
