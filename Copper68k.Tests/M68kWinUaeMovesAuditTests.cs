using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_MOVES_AUDIT", "audit qualified WinUAE MOVES legal encodings, operands and privilege frames")]
    public void WinUaeMovesAcrossSelectedModelsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "Moves", "MOVES", "cputest-moves.cpp", "moves-encodings.patch", "Moves",
        "8a7f2930a6f28a48170834481fcb449eee07a92dd7d6df96f8b6675d082692a7", "e79367079bbbbe4fda3b326cf7c6a5cf15e08f021cc3137b1fc3b3aeee756368",
        "winuae-moves-audit.json", ["68010", "68EC020", "68020", "68030", "68040", "68060"],
        ["68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"],
        _ => ["MOVES.B", "MOVES.W", "MOVES.L"], (model, family) => { var counts = MovesCounts(model, family); return (counts.Cases, counts.Frames); },
        "M68000PM 6-24/25/26: B/W/L memory-alterable operands, reserved extension fields zero, preserved CCR, partial Dn loads and sign-extended An loads; same-An postincrement/predecrement store values are explicitly undefined. A copied input generator clears reserved fields and excludes those store candidates before execution, leaving random consumption and independent expected CPU results intact. Original Basic inputs and failures are retained. All supported selected profiles, with 68000 absence covered synthetically; CCR 0/31, user/supervisor, one seeded round and full extensions enabled. The pinned generator and public bus use a flat address space; physical SFC/DFC spaces, cache coherency, bus ordering, trace/fault restart and timing are unqualified.",
        FormCounts: (model, family) => MovesCounts(model, family).Forms, ClassifyWords: QualifyMovesEncoding));

    private static (int Cases, uint Frames, int Forms) MovesCounts(ModelSpec model, string family) =>
        (FixtureId(model.Id), family) switch
        {
            ("68010", "MOVES.B") => (5410, 3844, 2760),
            ("68010", "MOVES.W") => (4748, 3770, 2460),
            ("68010", "MOVES.L") => (4810, 3778, 2468),
            ("68EC020", "MOVES.B") => (5368, 3936, 3032),
            ("68EC020", "MOVES.W") => (5346, 3980, 3022),
            ("68EC020", "MOVES.L") => (5476, 3892, 3030),
            ("68020" or "68030" or "68040" or "68060", "MOVES.B") => (5238, 4268, 2846),
            ("68020" or "68030" or "68040" or "68060", "MOVES.W") => (5214, 4300, 2752),
            ("68020" or "68030" or "68040" or "68060", "MOVES.L") => (5334, 4198, 2918),
            _ => throw new XunitException($"Unqualified MOVES family: {model.Id}/{family}")
        };

    // Manual fields and immutable input only, never the production decoder/EA.
    internal static string QualifyMovesEncoding(ModelSpec model, ushort opcode, ushort extension, ushort eaExtension, ushort inputSr, string family)
    {
        var size = (opcode >> 6) & 3;
        var mode = (opcode >> 3) & 7;
        var eaRegister = opcode & 7;
        var general = extension >> 12;
        var store = (extension & 0x0800) != 0;
        var expected = size switch { 0 => "MOVES.B", 1 => "MOVES.W", 2 => "MOVES.L", _ => "invalid" };
        if (model.Id == "68000" || (opcode & 0xff00) != 0x0e00 || family != expected || mode < 2 ||
            (mode == 7 && eaRegister > 1) || (extension & 0x07ff) != 0 ||
            inputSr is not (0x0000 or 0x001f or 0x2000 or 0x201f) ||
            (store && general >= 8 && general - 8 == eaRegister && mode is 3 or 4))
            throw new XunitException($"Unqualified MOVES encoding/profile: {model.Id}/{family}/{opcode:X4}/{extension:X4}/{inputSr:X4}");
        var index = "none";
        if (mode == 6)
        {
            if (!model.FullIndex || (eaExtension & 0x0100) == 0)
                index = model.FullIndex ? "brief" : "brief-unscaled";
            else
            {
                var displacement = (eaExtension >> 4) & 3;
                var indirect = eaExtension & 7;
                if ((eaExtension & 8) != 0 || displacement == 0 || indirect == 4)
                    throw new XunitException($"Unqualified MOVES full extension: {eaExtension:X4}");
                index = $"full/BS={(eaExtension >> 7) & 1}/IS={(eaExtension >> 6) & 1}/BD={displacement}/IIS={indirect}";
            }
        }
        return $"{family}/ea={mode}:{eaRegister}/R={general}/store={store}/index={index}/sr={inputSr:X4}";
    }
}

public sealed class M68kWinUaeMovesEncodingTests
{
    [Theory]
    [InlineData("68010", 0x0e90, 0x5800, 0, 0x2000, "MOVES.L", "MOVES.L/ea=2:0/R=5/store=True/index=none/sr=2000")]
    [InlineData("68010", 0x0e1f, 0xf000, 0, 0x001f, "MOVES.B", "MOVES.B/ea=3:7/R=15/store=False/index=none/sr=001F")]
    [InlineData("68020", 0x0e72, 0x9800, 0x7912, 0x201f, "MOVES.W", "MOVES.W/ea=6:2/R=9/store=True/index=full/BS=0/IS=0/BD=1/IIS=2/sr=201F")]
    [InlineData("68010", 0x0e72, 0x9800, 0x7912, 0x201f, "MOVES.W", "MOVES.W/ea=6:2/R=9/store=True/index=brief-unscaled/sr=201F")]
    [InlineData("A1200", 0x0eb9, 0xb000, 0, 0, "MOVES.L", "MOVES.L/ea=7:1/R=11/store=False/index=none/sr=0000")]
    public void FixedManualExamplesDistinguishSizeRegistersDirectionAndIndex(string modelId, int opcode, int extension, int eaExtension, int sr, string family, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyMovesEncoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)extension, (ushort)eaExtension, (ushort)sr, family));

    [Theory]
    [InlineData("68000", 0x0e90, 0x5800, 0, 0x2000, "MOVES.L")]
    [InlineData("68010", 0x0e9d, 0xda5e, 0, 0x2000, "MOVES.L")]
    [InlineData("68010", 0x0e9d, 0xd800, 0, 0x2000, "MOVES.L")]
    [InlineData("68020", 0x0ea0, 0x8800, 0, 0x2000, "MOVES.L")]
    [InlineData("68020", 0x0eba, 0x1000, 0, 0, "MOVES.L")]
    [InlineData("68020", 0x0ef0, 0x1000, 0, 0, "MOVES.L")]
    [InlineData("68020", 0x0e90, 0x5801, 0, 0x2000, "MOVES.L")]
    [InlineData("68020", 0x0e90, 0x5800, 0, 0x2000, "MOVES.W")]
    [InlineData("68020", 0x0e90, 0x5800, 0, 0xa000, "MOVES.L")]
    [InlineData("68020", 0x0eb0, 0x5800, 0x7900, 0x2000, "MOVES.L")]
    [InlineData("68020", 0x0eb0, 0x5800, 0x7914, 0x2000, "MOVES.L")]
    public void ReservedUndefinedUnavailableAndForeignFormsAreRejected(string modelId, int opcode, int extension, int eaExtension, int sr, string family) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyMovesEncoding(ModelSpec.All.Single(x => x.Id == modelId), (ushort)opcode, (ushort)extension, (ushort)eaExtension, (ushort)sr, family));
}
