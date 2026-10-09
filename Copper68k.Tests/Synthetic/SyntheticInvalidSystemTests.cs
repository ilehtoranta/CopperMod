using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidSystemTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidStatusOperandsTrapBeforePrivilegeAndOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-status-invalid-operands");
        // M68000PM 4-122/124/125 and 6-18/20: from-SR/CCR require data
        // alterable destinations; to-SR/CCR allow data sources, never An.
        // Unassigned mode-7 registers 5..7 are outside this matrix.
        var opcodes = StatusOpcodes().ToArray();
        Assert.Equal(38, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x40c8);
        Assert.Contains(opcodes, x => x.Opcode == 0x42fc);
        Assert.Contains(opcodes, x => x.Opcode == 0x46cf);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x40c0 or 0x42d0 or 0x44fa or 0x46fc);

        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidMovesOperandsTrapBeforePrivilegeExtensionAndOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-moves-invalid-operands");
        // M68000PM 6-25: B/W/L MOVES requires memory-alterable EAs.
        // Size 3 encodes CAS.L separately; unassigned mode-7 registers are
        // excluded. Both extension words are valid D0 load/store encodings.
        var opcodes = MovesOpcodes().ToArray();
        Assert.Equal(57, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x0e00);
        Assert.Contains(opcodes, x => x.Opcode == 0x0ebc);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x0e10 or 0x0e90 or 0x0ec0);

        foreach (var (opcode, family, form) in opcodes)
        foreach (var store in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}/store={store}",
                supervisor, ccr, store ? (ushort)0x0800 : (ushort)0x0000);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> StatusOpcodes()
    {
        foreach (var (encoding, family) in new[] { (0x40c0, "MOVEfromSR"), (0x42c0, "MOVEfromCCR"),
            (0x44c0, "MOVEtoCCR"), (0x46c0, "MOVEtoSR") })
        foreach (var form in InvalidForms(false, family.StartsWith("MOVEfrom")))
            yield return ((ushort)(encoding | form.Mode << 3 | form.Register), family + ".W", form.Id);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> MovesOpcodes()
    {
        for (var size = 0; size < 3; size++)
        foreach (var form in InvalidForms(true, true))
            yield return ((ushort)(0x0e00 | size << 6 | form.Mode << 3 | form.Register),
                $"MOVES.{new[] { "B", "W", "L" }[size]}", form.Id);
    }

    private static IEnumerable<OperandForm> InvalidForms(bool dataRegisters, bool pcAndImmediate)
    {
        if (dataRegisters)
            for (var reg = 0; reg < 8; reg++) yield return new(0, reg);
        for (var reg = 0; reg < 8; reg++) yield return new(1, reg);
        if (pcAndImmediate)
            for (var reg = 2; reg <= 4; reg++) yield return new(7, reg);
    }
}
