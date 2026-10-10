using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_CAS2_AUDIT", "audit qualified WinUAE CAS2 compare aliases and 060 unimplemented frames")]
    public void WinUaeCas2AcrossAdvancedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "Cas2", "CAS2", "gencpu-cas2.cpp", "cas2-compare-alias.patch", "Cas2",
        "beeb1f112867f1f11aa18b172c4607fe572db3f7500c8ef509a657423d640917", "134b61047dfafb91f38374b5151b940ada98dee32bfec251b6a6855043092888",
        "winuae-cas2-audit.json", ["68EC020", "68020", "68030", "68040", "68060"],
        ["68EC020", "68020", "68030", "68040", "68060", "A1200"],
        _ => ["CAS2.W", "CAS2.L"], (model, family) => { var c = Cas2Counts(model, family); return (c.Cases, c.Frames); },
        "M68000PM 4-66/68: canonical W/L extension fields, compare/update semantics, preserved X, shared compare register receives operand 1 on failure; overlapping memory updates are undefined. MC68060UM C.2.2: CAS2 takes vector 61 at the causing instruction PC. Copied CPU generator corrects 040 compare-register alias order. Original Basic corpus/failures retained; no result normalization. CCR 0/31, both privilege modes, one seeded round. Not physical bus locks/order, cache, trace, fault restart, timing or exhaustive architectural-combination qualification. 000/010 absence remains synthetic.",
        FormCounts: (model, family) => Cas2Counts(model, family).Forms, AllowFrameFreeFamilies: true,
        SavedPcControls: true, ClassifyRegisters: QualifyCas2Encoding,
        CompareAliasControls: (model, _) => model.Id == "68040",
        InputIdentity: new("cputest-cas2.cpp", "cas2-overlap-inputs.patch",
            "4edf1108dd8aab27ea8f61177c8e2166fca5b54e374f759ed388e3c804d5b93e", "dbf0c78f5b88bf2f2ddc0d656a52bca190ec6b970b9e3c6b82cc48d5eac5fc0d")));

    private static (int Cases, uint Frames, int Forms) Cas2Counts(ModelSpec model, string family) =>
        (FixtureId(model.Id), family) switch
        {
            ("68EC020", "CAS2.W") => (174, 0, 174),
            ("68EC020", "CAS2.L") => (154, 0, 154),
            ("68020" or "68030" or "68040", "CAS2.W") => (42, 0, 42),
            ("68020" or "68030" or "68040", "CAS2.L") => (34, 0, 34),
            ("68060", "CAS2.W") => (1148, 1148, 1096),
            ("68060", "CAS2.L") => (1148, 1148, 1092),
            _ => throw new XunitException($"Unqualified CAS2 family: {model.Id}/{family}")
        };

    internal static string QualifyCas2Encoding(ModelSpec model, ushort opcode, ushort first, ushort second,
        ushort inputSr, IReadOnlyList<uint> registers, string family)
    {
        var width = opcode == 0x0cfc ? 2 : opcode == 0x0efc ? 4 : 0;
        if (!model.FullIndex || registers.Count != 16 || width == 0 || family != (width == 2 ? "CAS2.W" : "CAS2.L") ||
            ((first | second) & 0x0e38) != 0 || inputSr is not (0 or 0x001f or 0x2000 or 0x201f))
            throw new XunitException($"Unqualified CAS2 encoding/profile: {model.Id}/{family}/{opcode:X4}/{first:X4}/{second:X4}/{inputSr:X4}");
        var r1 = first >> 12; var r2 = second >> 12;
        var address1 = registers[r1]; var address2 = registers[r2];
        var overlap = false;
        for (var i = 0; i < width; i++)
        for (var j = 0; j < width; j++)
            overlap |= model.Physical(unchecked(address1 + (uint)i)) == model.Physical(unchecked(address2 + (uint)j));
        if (overlap && model.Id != "68060")
            throw new XunitException($"Unqualified overlapping CAS2 operands: {model.Id}/{family}/{address1:X8}/{address2:X8}");
        return $"{family}/Rn={r1}:{r2}/Dc={first & 7}:{second & 7}/Du={(first >> 6) & 7}:{(second >> 6) & 7}/alias={(first & 7) == (second & 7)}/align={address1 & (uint)(width - 1)}:{address2 & (uint)(width - 1)}/overlap={overlap}/sr={inputSr:X4}";
    }
}

public sealed class M68kWinUaeCas2EncodingTests
{
    private static uint[] Registers() => Enumerable.Range(0, 16).Select(x => (uint)(x * 0x100)).ToArray();
    [Theory]
    [InlineData("68040", 0x0cfc, 0x8083, 0xe043, 0, "CAS2.W", "CAS2.W/Rn=8:14/Dc=3:3/Du=2:1/alias=True/align=0:0/overlap=False/sr=0000")]
    [InlineData("A1200", 0x0efc, 0x11c7, 0xf000, 0x201f, "CAS2.L", "CAS2.L/Rn=1:15/Dc=7:0/Du=7:0/alias=False/align=0:0/overlap=False/sr=201F")]
    public void FixedManualExamplesDistinguishRegistersAliasesAndSize(string modelId, int opcode, int first, int second, int sr, string family, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)first, (ushort)second, (ushort)sr, Registers(), family));
    [Theory]
    [InlineData("68000", 0x0cfc, 0x8083, 0xe043, 0, "CAS2.W")]
    [InlineData("68010", 0x0cfc, 0x8083, 0xe043, 0, "CAS2.W")]
    [InlineData("68020", 0x0afc, 0x8083, 0xe043, 0, "CAS2.W")]
    [InlineData("68020", 0x0cfc, 0x8283, 0xe043, 0, "CAS2.W")]
    [InlineData("68020", 0x0cfc, 0x8083, 0xe04b, 0, "CAS2.W")]
    [InlineData("68020", 0x0cfc, 0x8083, 0xe043, 0, "CAS2.L")]
    [InlineData("68020", 0x0cfc, 0x8083, 0xe043, 0xa000, "CAS2.W")]
    public void ReservedUnavailableAndForeignFormsAreRejected(string modelId, int opcode, int first, int second, int sr, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)first, (ushort)second, (ushort)sr, Registers(), family));
    [Fact]
    public void OverlapIsExcludedOnlyWhenHardwareCanUpdateTheMemoryOperands()
    {
        var registers = Registers(); registers[14] = registers[8] + 1;
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(ModelSpec.All.Single(x => x.Id == "68040"), 0x0cfc, 0x8083, 0xe043, 0, registers, "CAS2.W"));
        Assert.Contains("overlap=True", M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(ModelSpec.All.Single(x => x.Id == "68060"), 0x0cfc, 0x8083, 0xe043, 0, registers, "CAS2.W"));
    }
    [Fact]
    public void OverlapChecksPhysicalWidthAndTransfersWrappingAtTheBoundary()
    {
        var ec = ModelSpec.All.Single(x => x.Id == "68EC020");
        var full = ModelSpec.All.Single(x => x.Id == "68020");
        var registers = Registers(); registers[14] = registers[8] + 0x01000000;
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(ec, 0x0cfc, 0x8083, 0xe043, 0, registers, "CAS2.W"));
        Assert.Contains("overlap=False", M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(full, 0x0cfc, 0x8083, 0xe043, 0, registers, "CAS2.W"));
        registers[8] = 0xffffffff; registers[14] = 0;
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(full, 0x0efc, 0x8083, 0xe043, 0, registers, "CAS2.L"));
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCas2Encoding(full, 0x0efc, 0x8083, 0xe043, 0, [], "CAS2.L"));
    }
}
