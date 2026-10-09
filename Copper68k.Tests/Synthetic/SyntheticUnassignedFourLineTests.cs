using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticUnassignedFourLineTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void Unassigned4140WordsTrapBeforeOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-unassigned-4140");
        // M68000PM 4-69/70, 4-110: CHK uses bit 6 = 0, and LEA
        // requires bits 8..6 = 111. The 0100 rrr 101 mmm rrr pattern
        // is unassigned, not a byte CHK or a legal LEA operand form.
        // MC68020UM 6.1.5: illegal first words enter vector 4 at opcode PC.
        var opcodes = Unassigned4140Words().ToArray();
        Assert.Equal(512, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Distinct().Count());
        Assert.Contains((ushort)0x4140, opcodes);
        Assert.Contains((ushort)0x417c, opcodes);
        Assert.Contains((ushort)0x4f7f, opcodes);
        Assert.DoesNotContain((ushort)0x4100, opcodes);
        Assert.DoesNotContain((ushort)0x4180, opcodes);
        Assert.DoesNotContain((ushort)0x41c0, opcodes);
        foreach (var opcode in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode,
                $"UNASSIGNED/4140/field={(opcode >> 9) & 7}/ea=({(opcode >> 3) & 7},{opcode & 7})",
                supervisor, ccr);
        report.Complete(output);
    }

    private static IEnumerable<ushort> Unassigned4140Words()
    {
        for (var registerField = 0; registerField < 8; registerField++)
        for (var mode = 0; mode < 8; mode++)
        for (var register = 0; register < 8; register++)
            yield return (ushort)(0x4140 | registerField << 9 | mode << 3 | register);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void Unassigned4eSystemWordsTrapBeforeExtensionOrStackEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-unassigned-4e");
        // M68000PM instruction-format summary and MC68020UM 6.1.5:
        // these first words are unassigned on the selected processors. The
        // adjacent TRAP/LINK/UNLK/USP/return/system and MOVEC words are not
        // part of this matrix; their availability is tested separately.
        var opcodes = Enumerable.Range(0x4e00, 64).Select(x => (ushort)x)
            .Concat(new ushort[] { 0x4e78, 0x4e79, 0x4e7c, 0x4e7d, 0x4e7e, 0x4e7f }).ToArray();
        Assert.Equal(70, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Distinct().Count());
        Assert.Contains((ushort)0x4e00, opcodes);
        Assert.Contains((ushort)0x4e3f, opcodes);
        Assert.Contains((ushort)0x4e78, opcodes);
        Assert.Contains((ushort)0x4e7f, opcodes);
        Assert.DoesNotContain(opcodes, x => x is 0x4e40 or 0x4e50 or 0x4e58 or 0x4e60 or 0x4e70 or 0x4e74 or 0x4e7a or 0x4e7b);
        foreach (var opcode in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"UNASSIGNED/4E/word={opcode:X4}", supervisor, ccr);
        report.Complete(output);
    }
}
