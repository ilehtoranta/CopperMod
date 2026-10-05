using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticUnassignedMoveQuickTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void UnassignedMoveQuickWordsTrapBeforeRegisterOrFlagEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-moveq-unassigned-words");
        // M68000PM 4-134: MOVEQ's bit 8 is zero. The 0111 rrr 1 iiiiiiii
        // first words are unassigned on the selected models, not MOVEQ aliases.
        // Legal MOVEQ sign extension and CCR behavior remain in transfer-registers.
        var opcodes = (from register in Enumerable.Range(0, 8)
                       from immediate in Enumerable.Range(0, 256)
                       select (ushort)(0x7100 | register << 9 | immediate)).ToArray();
        Assert.Equal(2048, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Distinct().Count());
        Assert.Contains((ushort)0x7100, opcodes);
        Assert.Contains((ushort)0x7180, opcodes);
        Assert.Contains((ushort)0x7fff, opcodes);
        Assert.DoesNotContain(opcodes, x => x is 0x7000 or 0x7080 or 0x7eff);
        foreach (var opcode in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode,
                $"UNASSIGNED/MOVEQ/register-field={(opcode >> 9) & 7}/immediate-field={opcode & 255:X2}", supervisor, ccr);
        report.Complete(output);
    }
}
