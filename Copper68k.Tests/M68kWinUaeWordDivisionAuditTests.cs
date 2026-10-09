using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_WORD_DIVISION_AUDIT", "audit qualified WinUAE word division flags and exceptions")]
    public void WinUaeWordDivisionAcrossSelectedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "WordDivision", "WORD_DIVISION", "gencpu-word-division.cpp", "word-division-carry.patch", "WordDivision",
        "254873bb8a1b59b081e98defa72fda572d5a2c93c33bb644c8ac07c4257e5c5e", "7eb90f09aaad56d164d0eafb9f66ddfc31becde9951a832820d5fdcc7973a83f", "winuae-word-division-audit.json",
        ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060"],
        ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"],
        _ => ["DIVS.W", "DIVU.W"], (model, family) => { var counts = WordDivisionCounts(model, family); return (counts.Cases, counts.Frames); },
        "DIVS.W/DIVU.W across all selected profiles. M68000PM 4-92/96: carry is cleared, including overflow and divide-by-zero; X is preserved; N/Z undefined on overflow, N/Z/V undefined on divide-by-zero. A copied generator explicitly clears carry after DIVU overflow; the pinned helper omitted this on 020/030. Existing independently defined flag masks and CPU results are unchanged. Basic CCR 0/31, both stacks, full extensions enabled; no incoming trace/bus faults or physical timing. Patched software reference, not unchanged Basic or silicon qualification.",
        ArithmeticFlagControls: true, MaskedCounts: (model, family) => WordDivisionCounts(model, family).Masked,
        ClassifyForm: QualifyWordDivisionEncoding, FormCounts: (model, family) => WordDivisionCounts(model, family).Forms));

    private static (int Cases, uint Frames, uint Masked, int Forms) WordDivisionCounts(ModelSpec model, string family) =>
        (FixtureId(model.Id), family) switch
        {
            ("68000", "DIVS.W") => (5738, 1590, 3530, 736),
            ("68000", "DIVU.W") => (6166, 1838, 4158, 736),
            ("68010", "DIVS.W") => (5782, 1632, 3836, 736),
            ("68010", "DIVU.W") => (6058, 1792, 4032, 736),
            ("68EC020", "DIVS.W") => (9690, 2734, 6074, 784),
            ("68EC020", "DIVU.W") => (9670, 2670, 7132, 752),
            ("68020" or "68030" or "68040" or "68060", "DIVS.W") => (9246, 2570, 5816, 688),
            ("68020" or "68030" or "68040" or "68060", "DIVU.W") => (8870, 2466, 5676, 656),
            _ => throw new XunitException($"Unqualified word-division family: {model.Id}/{family}")
        };
    // M68000PM 4-93/97: fixed first-word encoding and legal data sources.
    // This classifies immutable input; it never decodes production EAs or
    // calculates an expected arithmetic/flag result from production helpers.
    internal static string QualifyWordDivisionEncoding(ushort opcode, ushort inputSr, string family)
    {
        var expected = family switch { "DIVU.W" => 0x80c0, "DIVS.W" => 0x81c0, _ => -1 };
        var mode = (opcode >> 3) & 7;
        var register = opcode & 7;
        if (expected < 0 || (opcode & 0xf1c0) != expected || mode == 1 || (mode == 7 && register > 4) ||
            (inputSr & ~0x201f) != 0 || (inputSr & 31) is not (0 or 31))
            throw new XunitException($"Unqualified word-division encoding/profile: {family}/{opcode:X4}/{inputSr:X4}");
        return $"{family}/ea={mode}:{register}/destination={(opcode >> 9) & 7}/sr={inputSr:X4}";
    }
}

public sealed class M68kWinUaeWordDivisionEncodingTests
{
    [Theory]
    [InlineData(0x80c0, 0x0000, "DIVU.W", "DIVU.W/ea=0:0/destination=0/sr=0000")]
    [InlineData(0x84df, 0x001f, "DIVU.W", "DIVU.W/ea=3:7/destination=2/sr=001F")]
    [InlineData(0x8ee6, 0x2000, "DIVU.W", "DIVU.W/ea=4:6/destination=7/sr=2000")]
    [InlineData(0x8efc, 0x201f, "DIVU.W", "DIVU.W/ea=7:4/destination=7/sr=201F")]
    [InlineData(0x81c0, 0x0000, "DIVS.W", "DIVS.W/ea=0:0/destination=0/sr=0000")]
    [InlineData(0x85d7, 0x001f, "DIVS.W", "DIVS.W/ea=2:7/destination=2/sr=001F")]
    [InlineData(0x8ffb, 0x2000, "DIVS.W", "DIVS.W/ea=7:3/destination=7/sr=2000")]
    [InlineData(0x83f8, 0x201f, "DIVS.W", "DIVS.W/ea=7:0/destination=1/sr=201F")]
    public void FixedReferenceExamplesRemainDistinct(int opcode, int sr, string family, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyWordDivisionEncoding((ushort)opcode, (ushort)sr, family));

    [Theory]
    [InlineData(0x80c8, 0x0000, "DIVU.W")]
    [InlineData(0x81fd, 0x0000, "DIVS.W")]
    [InlineData(0x80c0, 0x0000, "DIVS.W")]
    [InlineData(0x4c40, 0x0000, "DIVU.W")]
    [InlineData(0x80c0, 0xa000, "DIVU.W")]
    [InlineData(0x81c0, 0x0012, "DIVS.W")]
    public void InvalidFormsAndUnqualifiedProfileStatesAreRejected(int opcode, int sr, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyWordDivisionEncoding((ushort)opcode, (ushort)sr, family));
}
