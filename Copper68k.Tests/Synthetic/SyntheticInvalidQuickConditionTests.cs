using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidQuickConditionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidQuickDestinationsTrapBeforeRegisterOrOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-quick-invalid-operands");
        // M68000PM 4-11/12, 4-181/182: alterable destinations only; An
        // permits word/long, never byte. Mode-7 fields 5..7 are unassigned.
        // Size 3 selects Scc/DBcc/TRAPcc, not a quick arithmetic size.
        var opcodes = InvalidQuickWords().ToArray();
        Assert.Equal(416, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x5008); // ADDQ.B #8,A0
        Assert.Contains(opcodes, x => x.Opcode == 0x5f0f); // SUBQ.B #7,A7
        Assert.Contains(opcodes, x => x.Opcode == 0x50ba); // ADDQ.L #8,PC-relative
        Assert.Contains(opcodes, x => x.Opcode == 0x5fbf); // SUBQ.L, unassigned EA
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x5000 or 0x5048 or 0x508f or 0x51b9 or 0x50c8 or 0x50fa);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> InvalidQuickWords()
    {
        foreach (var subtract in new[] { false, true })
        for (var dataField = 0; dataField < 8; dataField++)
        for (var size = 0; size < 3; size++)
        {
            if (size == 0)
            for (var register = 0; register < 8; register++) yield return Case(new(1, register));
            for (var register = 2; register < 8; register++) yield return Case(new(7, register));

            (ushort, string, string) Case(OperandForm form) =>
                ((ushort)(0x5000 | dataField << 9 | (subtract ? 0x0100 : 0) | size << 6 | form.Mode << 3 | form.Register),
                    $"{(subtract ? "SUBQ" : "ADDQ")}.{new[] { "B", "W", "L" }[size]}/count={(dataField == 0 ? 8 : dataField)}",
                    form.Mode == 7 && form.Register > 4 ? $"unassigned-EA(7,{form.Register})" : form.Id);
        }
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void UnassignedConditionWordsTrapWithoutTestingOrUpdatingOperands(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "control-scc-unassigned-operands");
        // M68000PM 4-173: Scc admits mode-7 fields 0/1 only. Mode 1 is
        // DBcc, and fields 2/3/4 are separate TRAPcc words on 020+.
        // Only the remaining unassigned fields 5..7 enter this matrix.
        var opcodes = (from condition in Enumerable.Range(0, 16)
                       from register in Enumerable.Range(5, 3)
                       select (ushort)(0x50f8 | condition << 8 | register)).ToArray();
        Assert.Equal(48, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Distinct().Count());
        Assert.Contains((ushort)0x50fd, opcodes);
        Assert.Contains((ushort)0x5fff, opcodes);
        Assert.DoesNotContain(opcodes, x => x is 0x50c0 or 0x50c8 or 0x50f8 or 0x50f9 or 0x50fa or 0x50fb or 0x50fc);
        foreach (var opcode in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode,
                $"UNASSIGNED/Scc/condition={(opcode >> 8) & 15}/EA(7,{opcode & 7})", supervisor, ccr);
        report.Complete(output);
    }
}
