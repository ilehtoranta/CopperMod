using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_MOVE16_AUDIT", "audit canonical WinUAE MOVE16 encodings and line transfers")]
    public void WinUaeMove16AcrossSupportedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "Move16", "MOVE16", "cputest-move16.cpp", "move16-encodings.patch", "Move16",
        "b201915e70fa2c7cc7c87cbfef588eb910bf3e655ea02bbff0955e42bc4e2e55", "c23761b715a1eb8abc01f4293710ffda73bb48f9a4867f1d0087c462eaa7ed12",
        "winuae-move16-audit.json", ["68040", "68060"], ["68040", "68060"],
        _ => ["MOVE16"], (_, _) => (41856, 0),
        "M68000PRM 4-125..127: five canonical forms, aligned 16-byte line copy, low address bits retained in postincrements, same register increments once, CCR unchanged. Copied input generator fixes extension bits before EA construction, serializes explicit valid An addresses, and enumerates every Ay in sixteen low-nibble rounds. Original Basic inputs/failures remain unchanged. CCR 0/31; all 64 post-post register pairs in user mode and 49 non-A7 pairs in supervisor mode are independently executed. Thirty supervisor A7 post-post pair/CCR and eight absolute-form A7/CCR combinations per model are absent from this reference selection and remain required reference follow-up; their ordinary synthetic semantics are retained. Four absolute forms include user/supervisor. No incoming trace/faults, cache allocation, physical burst/bus order or timing qualification. Unavailable models retain synthetic Line-F coverage.",
        FormCounts: (_, _) => 25260, ClassifyRegisters: QualifyMove16Encoding,
        ExpectedFormHash: (_, _) => "143e6c025a7451664853b10bfe94651dae28860263dad370a98ad6a33c77d236",
        AllowFrameFreeFamilies: true, MemoryControls: true, ReferenceGaps: Move16ReferenceGaps));

    internal static IReadOnlyList<string> Move16ReferenceGaps(IReadOnlyDictionary<string, int> forms)
    {
        var seen = forms.Keys.Select(x => { var fields = x.Split('/'); return fields[1] + "/" + fields[2] + "/" + fields[^1]; })
            .ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        for (var source = 0; source < 8; source++)
        for (var destination = 0; destination < 8; destination++)
        foreach (var sr in new[] { 0, 0x1f, 0x2000, 0x201f })
        {
            var key = $"form=post-post/A={source}:{destination}/sr={sr:X4}";
            if (!seen.Contains(key)) missing.Add("MOVE16/" + key);
        }
        foreach (var form in new[] { "post-abs", "abs-post", "indirect-abs", "abs-indirect" })
        for (var register = 0; register < 8; register++)
        foreach (var sr in new[] { 0, 0x1f, 0x2000, 0x201f })
        {
            var key = $"form={form}/A={register}:abs/sr={sr:X4}";
            if (!seen.Contains(key)) missing.Add("MOVE16/" + key);
        }
        return missing;
    }

    // Decode only documented immutable fields and input registers; never call
    // production instruction decoding or effective-address helpers.
    internal static (uint Source, uint Destination) Move16Operands(ModelSpec model, ushort opcode,
        ushort first, ushort second, ushort sr, IReadOnlyList<uint> registers, string family)
    {
        var form = (opcode >> 3) & 7;
        if (model.Id is not ("68040" or "68060") || registers.Count != 16 || family != "MOVE16" ||
            (opcode & 0xffc0) != 0xf600 || form > 4 || sr is not (0 or 0x1f or 0x2000 or 0x201f) ||
            (form == 4 && (first & 0x8fff) != 0x8000))
            throw new XunitException($"Unqualified MOVE16 encoding/profile: {model.Id}/{family}/{opcode:X4}/{first:X4}/{second:X4}/{sr:X4}");
        var register = registers[8 + (opcode & 7)];
        var absolute = ((uint)first << 16) | second;
        return form switch
        {
            0 or 2 => (register, absolute),
            1 or 3 => (absolute, register),
            _ => (register, registers[8 + ((first >> 12) & 7)])
        };
    }

    internal static string QualifyMove16Encoding(ModelSpec model, ushort opcode, ushort first, ushort second,
        ushort sr, IReadOnlyList<uint> registers, string family)
    {
        var (source, destination) = Move16Operands(model, opcode, first, second, sr, registers, family);
        var form = (opcode >> 3) & 7;
        var name = form switch { 0 => "post-abs", 1 => "abs-post", 2 => "indirect-abs", 3 => "abs-indirect", _ => "post-post" };
        var other = form == 4 ? ((first >> 12) & 7).ToString() : "abs";
        return $"MOVE16/form={name}/A={opcode & 7}:{other}/sameRegister={form == 4 && (opcode & 7) == ((first >> 12) & 7)}/offset={source & 15}:{destination & 15}/sameLine={(source & ~15u) == (destination & ~15u)}/sr={sr:X4}";
    }
}

public sealed class M68kWinUaeMove16EncodingTests
{
    private static uint[] Registers() => Enumerable.Range(0, 16).Select(x => 0x1000u + (uint)(x * 0x101)).ToArray();

    [Theory]
    [InlineData("68040", 0xf600, 0, 0x221f, 0, "MOVE16/form=post-abs/A=0:abs/sameRegister=False/offset=8:15/sameLine=False/sr=0000")]
    [InlineData("68060", 0xf609, 0, 0x221f, 0x201f, "MOVE16/form=abs-post/A=1:abs/sameRegister=False/offset=15:9/sameLine=False/sr=201F")]
    [InlineData("68040", 0xf612, 0, 0x221f, 0x1f, "MOVE16/form=indirect-abs/A=2:abs/sameRegister=False/offset=10:15/sameLine=False/sr=001F")]
    [InlineData("68060", 0xf61b, 0, 0x221f, 0x2000, "MOVE16/form=abs-indirect/A=3:abs/sameRegister=False/offset=15:11/sameLine=False/sr=2000")]
    [InlineData("68040", 0xf626, 0xe000, 0x4e71, 0, "MOVE16/form=post-post/A=6:6/sameRegister=True/offset=14:14/sameLine=True/sr=0000")]
    [InlineData("68060", 0xf627, 0x8000, 0x4e71, 0x201f, "MOVE16/form=post-post/A=7:0/sameRegister=False/offset=15:8/sameLine=False/sr=201F")]
    public void FixedManualExamplesDistinguishFormsAliasesAndOffsets(string model, int opcode, int first, int second, int sr, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyMove16Encoding(ModelSpec.All.Single(x => x.Id == model),
            (ushort)opcode, (ushort)first, (ushort)second, (ushort)sr, Registers(), "MOVE16"));

    [Theory]
    [InlineData("68000", 0xf600, 0, 0x221f, 0, "MOVE16")]
    [InlineData("68010", 0xf600, 0, 0x221f, 0, "MOVE16")]
    [InlineData("68EC020", 0xf600, 0, 0x221f, 0, "MOVE16")]
    [InlineData("68020", 0xf600, 0, 0x221f, 0, "MOVE16")]
    [InlineData("68030", 0xf600, 0, 0x221f, 0, "MOVE16")]
    [InlineData("A1200", 0xf600, 0, 0x221f, 0, "MOVE16")]
    [InlineData("68060", 0xf626, 0x6000, 0x4e71, 0, "MOVE16")]
    [InlineData("68040", 0xf626, 0xe001, 0x4e71, 0, "MOVE16")]
    [InlineData("68060", 0xf626, 0xe800, 0x4e71, 0, "MOVE16")]
    [InlineData("68040", 0xf628, 0, 0, 0, "MOVE16")]
    [InlineData("68040", 0xf600, 0, 0x221f, 0xa000, "MOVE16")]
    [InlineData("68060", 0xf600, 0, 0x221f, 0, "MOVE")]
    public void ReservedUnavailableAndForeignFormsAreRejected(string model, int opcode, int first, int second, int sr, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyMove16Encoding(ModelSpec.All.Single(x => x.Id == model),
            (ushort)opcode, (ushort)first, (ushort)second, (ushort)sr, Registers(), family));

    [Fact]
    public void AddressWordsAreDataForAbsoluteFormsAndRegistersMustBeComplete()
    {
        var model = ModelSpec.All.Single(x => x.Id == "68060");
        Assert.Equal((0x1808u, 0x6000e001u), M68kWinUaeCpuTesterConformanceTests.Move16Operands(model, 0xf600, 0x6000, 0xe001, 0, Registers(), "MOVE16"));
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.Move16Operands(model, 0xf600, 0, 0, 0, [], "MOVE16"));
    }

    [Fact]
    public void CoverageFingerprintUsesOrdinalKeysAndPreservesWeights()
    {
        var forms = new Dictionary<string, int> { ["b"] = 1, ["a"] = 2 };
        Assert.Equal("b31f1b9cb2e96c4635034c21830f25be40592cee8c1ddc5c9cf83430501ecce0",
            M68kWinUaeCpuTesterConformanceTests.WinUaeFormDistributionSha256(forms));
        var redistributed = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        Assert.NotEqual(M68kWinUaeCpuTesterConformanceTests.WinUaeFormDistributionSha256(forms),
            M68kWinUaeCpuTesterConformanceTests.WinUaeFormDistributionSha256(redistributed));
    }

    [Fact]
    public void MissingReferencePairsRemainExplicitEvenWhenSelectedCallbacksPass()
    {
        var forms = new Dictionary<string, int>();
        foreach (var form in new[] { "post-abs", "abs-post", "indirect-abs", "abs-indirect" })
        for (var register = 0; register < 8; register++)
        foreach (var sr in new[] { 0, 0x1f, 0x2000, 0x201f })
        {
            if (sr >= 0x2000 && register == 7) continue;
            forms[$"MOVE16/form={form}/A={register}:abs/sameRegister=False/offset=0:0/sameLine=False/sr={sr:X4}"] = 1;
        }
        for (var source = 0; source < 8; source++)
        for (var destination = 0; destination < 8; destination++)
        foreach (var sr in new[] { 0, 0x1f, 0x2000, 0x201f })
        {
            if (sr >= 0x2000 && (source == 7 || destination == 7)) continue;
            forms[$"MOVE16/form=post-post/A={source}:{destination}/sameRegister=False/offset=0:0/sameLine=False/sr={sr:X4}"] = 1;
        }
        var gaps = M68kWinUaeCpuTesterConformanceTests.Move16ReferenceGaps(forms);
        Assert.Equal(38, gaps.Count);
        Assert.Contains("MOVE16/form=post-post/A=7:7/sr=201F", gaps);
        Assert.DoesNotContain("MOVE16/form=post-post/A=0:1/sr=2000", gaps);
        Assert.Contains("MOVE16/form=post-abs/A=7:abs/sr=2000", gaps);
        Assert.Equal(384, M68kWinUaeCpuTesterConformanceTests.Move16ReferenceGaps(new Dictionary<string, int>()).Count);
    }
}
