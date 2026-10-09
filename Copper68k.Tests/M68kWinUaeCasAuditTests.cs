using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_CAS_AUDIT", "audit qualified WinUAE CAS encodings and 060 unimplemented-integer frames")]
    public void WinUaeCasAcrossAdvancedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "Cas", "CAS", "gencpu-cas.cpp", "cas-unimplemented-pc.patch", "Cas",
        "40e65f21b503437993d1a704c7552b8b6f8ab17493c109583c13e00c93d77b10", "a27200bdbac2441b1b63651f02590d2894eadc3257df54097fd8a50a5c0d8646",
        "winuae-cas-audit.json", ["68EC020", "68020", "68030", "68040", "68060"],
        ["68EC020", "68020", "68030", "68040", "68060", "A1200"],
        _ => ["CAS.B", "CAS.W", "CAS.L"], (model, family) => { var counts = CasCounts(model, family); return (counts.Cases, counts.Frames); },
        "M68000PM 4-66/67: B/W/L memory-alterable operands, reserved extension fields zero, compare/update semantics and preserved X. MC68060UM C.2.2 and figure 8-3: misaligned W/L operands take vector 61 with the causing instruction PC. A copied generator clears reserved fields before execution and corrects its advanced exception PC; the original Basic corpus and failures remain. EC020/A1200/020/030/040/060, CCR 0/31 and user/supervisor, one seeded round and full extensions enabled. 000/010 absence is covered synthetically. CAS2, incoming trace, operand faults/restart, external lock cycles, physical bus ordering, cache and timing remain unqualified. The software generator can read an operand before checking alignment; this preset compares architectural state and frames, not bus activity or silicon.",
        FormCounts: (model, family) => CasCounts(model, family).Forms, ClassifyWords: QualifyCasEncoding,
        InputIdentity: new("cputest-cas.cpp", "cas-encodings.patch",
            "6b24d70455aa39ed6894ad2a2253d60bf4b8b487be4e2dada194a0589eb53b5b", "cb45000b18f7d4a11dcb0fec7130202c7918ac7fd42c9940effa248b1991fba2"),
        AllowFrameFreeFamilies: true, SavedPcControls: true));

    private static (int Cases, uint Frames, int Forms) CasCounts(ModelSpec model, string family) =>
        (FixtureId(model.Id), family) switch
        {
            ("68EC020", "CAS.B") => (3358, 0, 2620),
            ("68EC020", "CAS.W") => (2924, 0, 2340),
            ("68EC020", "CAS.L") => (3476, 0, 2736),
            ("68020" or "68030" or "68040" or "68060", "CAS.B") => (2348, 0, 1826),
            ("68020" or "68030" or "68040", "CAS.W") => (2442, 0, 1904),
            ("68020" or "68030" or "68040", "CAS.L") => (2652, 0, 2034),
            ("68060", "CAS.W") => (2442, 894, 1904),
            ("68060", "CAS.L") => (2652, 1572, 2034),
            _ => throw new XunitException($"Unqualified CAS family: {model.Id}/{family}")
        };

    // Classify immutable input with fixed manual fields, not production EA/decoder helpers.
    internal static string QualifyCasEncoding(ModelSpec model, ushort opcode, ushort extension, ushort eaExtension, ushort inputSr, string family)
    {
        var size = (opcode >> 9) & 3;
        var mode = (opcode >> 3) & 7;
        var reg = opcode & 7;
        var expected = size switch { 1 => "CAS.B", 2 => "CAS.W", 3 => "CAS.L", _ => "invalid" };
        if (!model.FullIndex || (opcode & 0xf9c0) != 0x08c0 || family != expected || mode < 2 ||
            (mode == 7 && reg > 1) || (extension & 0xfe38) != 0 || inputSr is not (0 or 0x001f or 0x2000 or 0x201f))
            throw new XunitException($"Unqualified CAS encoding/profile: {model.Id}/{family}/{opcode:X4}/{extension:X4}/{inputSr:X4}");
        var index = "none";
        if (mode == 6)
        {
            index = "brief";
            if ((eaExtension & 0x0100) != 0)
            {
                var displacement = (eaExtension >> 4) & 3;
                var indirect = eaExtension & 7;
                if ((eaExtension & 8) != 0 || displacement == 0 || indirect == 4)
                    throw new XunitException($"Unqualified CAS full extension: {eaExtension:X4}");
                index = $"full/BS={(eaExtension >> 7) & 1}/IS={(eaExtension >> 6) & 1}/BD={displacement}/IIS={indirect}";
            }
        }
        return $"{family}/ea={mode}:{reg}/Dc={extension & 7}/Du={(extension >> 6) & 7}/index={index}/sr={inputSr:X4}";
    }
}

public sealed class M68kWinUaeCasEncodingTests
{
    [Theory]
    [InlineData("68020", 0x0ad0, 0x01c7, 0, 0x2000, "CAS.B", "CAS.B/ea=2:0/Dc=7/Du=7/index=none/sr=2000")]
    [InlineData("68060", 0x0ce3, 0x0043, 0, 0, "CAS.W", "CAS.W/ea=4:3/Dc=3/Du=1/index=none/sr=0000")]
    [InlineData("68060", 0x0ee3, 0x00c1, 0, 0x001f, "CAS.L", "CAS.L/ea=4:3/Dc=1/Du=3/index=none/sr=001F")]
    [InlineData("A1200", 0x0cf2, 0x0102, 0x7912, 0x201f, "CAS.W", "CAS.W/ea=6:2/Dc=2/Du=4/index=full/BS=0/IS=0/BD=1/IIS=2/sr=201F")]
    public void FixedManualExamplesDistinguishSizeRegistersAddressingAndIndex(string modelId, int opcode, int extension, int eaExtension, int sr, string family, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyCasEncoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)extension, (ushort)eaExtension, (ushort)sr, family));

    [Theory]
    [InlineData("68000", 0x0cd0, 0x0040, 0, 0, "CAS.W")]
    [InlineData("68010", 0x0cd0, 0x0040, 0, 0, "CAS.W")]
    [InlineData("68020", 0x0ac0, 0, 0, 0, "CAS.B")]
    [InlineData("68020", 0x0cfa, 0, 0, 0, "CAS.W")]
    [InlineData("68020", 0x0efc, 0, 0, 0, "CAS.L")]
    [InlineData("68020", 0x0cd0, 0x0200, 0, 0, "CAS.W")]
    [InlineData("68020", 0x0cd0, 8, 0, 0, "CAS.W")]
    [InlineData("68020", 0x0cd0, 0, 0, 0, "CAS.L")]
    [InlineData("68020", 0x0cd0, 0, 0, 0xa000, "CAS.W")]
    [InlineData("68020", 0x0cf0, 0, 0x7900, 0, "CAS.W")]
    [InlineData("68020", 0x0cf0, 0, 0x7914, 0, "CAS.W")]
    public void ReservedUnavailableAndForeignFormsAreRejected(string modelId, int opcode, int extension, int eaExtension, int sr, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyCasEncoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)extension, (ushort)eaExtension, (ushort)sr, family));
}
