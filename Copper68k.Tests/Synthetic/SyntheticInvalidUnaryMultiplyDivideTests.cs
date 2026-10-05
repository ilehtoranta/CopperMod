using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidUnaryMultiplyDivideTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidUnaryOperandsTrapWithoutOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-unary-invalid-operands");
        // M68000PM: CLR/NEG/NEGX/NOT/TAS/NBCD require data-alterable EAs.
        // TST gained PC/immediate and word/long An sources on 020 (4-193).
        // 020+ NBCD mode-1 words encode LINK.L instead (4-111). Size 3 and
        // unassigned mode-7 registers are outside this matrix except TST.
        var opcodes = UnaryOpcodes(machine.Model.FullIndex, modelId == "68060").ToArray();
        Assert.Equal(modelId == "68060" ? 161 : machine.Model.FullIndex ? 163 : 196, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x4008);
        Assert.Contains(opcodes, x => x.Opcode == 0x483c);
        Assert.Equal(modelId != "68060", opcodes.Any(x => x.Opcode == 0x4ac8));
        Assert.Equal(modelId != "68060", opcodes.Any(x => x.Opcode == 0x4acc));
        Assert.Equal(!machine.Model.FullIndex, opcodes.Any(x => x.Opcode == 0x4a48));
        Assert.Equal(!machine.Model.FullIndex, opcodes.Any(x => x.Opcode == 0x4a3c));
        Assert.Equal(!machine.Model.FullIndex, opcodes.Any(x => x.Opcode == 0x4808));
        Assert.Contains(opcodes, x => x.Opcode == 0x4a3d);
        Assert.Contains(opcodes, x => x.Opcode == 0x4abf);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x4000 or 0x4810 or 0x4ad0 or 0x40c0);

        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidWordMultiplyDivideSourcesTrapWithoutRegisterEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-muldiv-word-invalid-operands");
        // M68000PM 4-93/97/136/139: data sources exclude all An encodings.
        var opcodes = WordOpcodes().ToArray();
        Assert.Equal(256, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x80c8);
        Assert.Contains(opcodes, x => x.Opcode == 0xcfcf);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x80c0 or 0x81fc or 0xc0fa or 0xc1fb);

        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidLongMultiplyDivideSourcesTrapBeforeExtensionSelectedOperations(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-muldiv-long-invalid-operands");
        // Long operations (020+) still exclude An (M68000PM 4-94/98/137/140).
        // Valid signed/unsigned, 32/64-bit extensions cannot admit an invalid
        // opcode EA, including 060's unavailable 64-bit operations. Reserved
        // extension bits are outside this assigned-operand matrix.
        var opcodes = LongOpcodes().ToArray();
        Assert.Equal(16, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x4c08);
        Assert.Contains(opcodes, x => x.Opcode == 0x4c4f);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x4c00 or 0x4c7c);

        foreach (var (opcode, family, form) in opcodes)
        foreach (var extension in new ushort[] { 0x0001, 0x0801, 0x1402, 0x1c02 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}/ext={extension:X4}",
                supervisor, ccr, extension);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> UnaryOpcodes(bool advancedTst, bool debug060)
    {
        foreach (var (encoding, family) in new[] { (0x4000, "NEGX"), (0x4200, "CLR"),
            (0x4400, "NEG"), (0x4600, "NOT"), (0x4a00, "TST"), (0x4800, "NBCD"), (0x4ac0, "TAS") })
        for (var size = 0; size < (family is "NBCD" or "TAS" ? 1 : 3); size++)
        {
            foreach (var form in InvalidDestinations())
            {
                if (family == "TST" && advancedTst && (form.Mode == 7 || size != 0)) continue;
                if (family == "NBCD" && advancedTst && form.Mode == 1) continue;
                // MC68060UM 9.2.2: HALT/PULSE reuse two TAS mode-1 words.
                if (family == "TAS" && debug060 && form.Mode == 1 && form.Register is 0 or 4) continue;
                yield return ((ushort)(encoding | size << 6 | form.Mode << 3 | form.Register),
                    $"{family}.{new[] { "B", "W", "L" }[size]}", form.Id);
            }
            if (family == "TST")
            for (var register = 5; register < 8; register++)
                yield return ((ushort)(encoding | size << 6 | 0x38 | register),
                    $"TST.{new[] { "B", "W", "L" }[size]}", $"unassigned-EA(7,{register})");
        }
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> WordOpcodes()
    {
        foreach (var (encoding, family) in new[] { (0x80c0, "DIVU"), (0x81c0, "DIVS"),
            (0xc0c0, "MULU"), (0xc1c0, "MULS") })
        for (var destination = 0; destination < 8; destination++)
        for (var source = 0; source < 8; source++)
            yield return ((ushort)(encoding | destination << 9 | 0x08 | source), family + ".W", $"A{source},D{destination}");
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> LongOpcodes()
    {
        foreach (var (encoding, family) in new[] { (0x4c00, "MULL"), (0x4c40, "DIVL") })
        for (var source = 0; source < 8; source++)
            yield return ((ushort)(encoding | 0x08 | source), family + ".L", $"A{source}");
    }

    private static IEnumerable<OperandForm> InvalidDestinations()
    {
        for (var reg = 0; reg < 8; reg++) yield return new(1, reg);
        for (var reg = 2; reg <= 4; reg++) yield return new(7, reg);
    }
}
