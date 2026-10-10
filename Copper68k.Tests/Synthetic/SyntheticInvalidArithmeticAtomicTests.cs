using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidArithmeticAtomicTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidImmediateArithmeticDestinationsTrapBeforeOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-invalid-operands");
        // M68000PM 4-10, 4-80, 4-180: ADDI/SUBI destinations are data
        // alterable. CMPI excludes An/#data, but gained PC-relative EAs on 020.
        // These are assigned operand encodings, not unassigned mode-7 registers.
        var opcodes = ArithmeticOpcodes(machine.Model.FullIndex).ToArray();
        Assert.Equal(machine.Model.FullIndex ? 93 : 99, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x0408); // SUBI.B to A0
        Assert.Contains(opcodes, x => x.Opcode == 0x0c3c); // CMPI.B #data,#data
        Assert.DoesNotContain(opcodes, x => x.Opcode == 0x0c00); // CMPI.B to D0
        Assert.Equal(!machine.Model.FullIndex, opcodes.Any(x => x.Opcode == 0x0c3a));
        Assert.Equal(!machine.Model.FullIndex, opcodes.Any(x => x.Opcode == 0x0c3b));

        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidCasDestinationsTrapBeforeExtensionOrOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-cas-invalid-operands");
        // M68000PM 4-67: CAS requires memory-alterable EAs. Mode 7/register
        // 4 words encode CAS2.W/.L separately; its byte word is unassigned.
        // All three are outside this assigned CAS-operand matrix.
        var opcodes = CasOpcodes().ToArray();
        Assert.Equal(54, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x0ac0); // CAS.B D0
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x0afc or 0x0cfc or 0x0efc);
        Assert.DoesNotContain(opcodes, x => x.Opcode == 0x0ad0); // CAS.B (A0)

        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> ArithmeticOpcodes(bool pcRelativeCmpi)
    {
        foreach (var (encoding, family) in new[] { (0x0400, "SUBI"), (0x0600, "ADDI"), (0x0c00, "CMPI") })
        for (var size = 0; size < 3; size++)
        foreach (var form in NonAlterableDestinations())
        {
            if (family == "CMPI" && pcRelativeCmpi && form.Mode == 7 && form.Register is 2 or 3) continue;
            yield return ((ushort)(encoding | size << 6 | form.Mode << 3 | form.Register),
                $"{family}.{new[] { "B", "W", "L" }[size]}", form.Id);
        }
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> CasOpcodes()
    {
        for (var size = 0; size < 3; size++)
        foreach (var form in Enumerable.Range(0, 8).Select(r => new OperandForm(0, r))
            .Concat(NonAlterableDestinations().Where(f => f.Mode != 7 || f.Register != 4)))
            yield return ((ushort)((size == 0 ? 0x0ac0 : size == 1 ? 0x0cc0 : 0x0ec0) | form.Mode << 3 | form.Register),
                $"CAS.{new[] { "B", "W", "L" }[size]}", form.Id);
    }

    private static IEnumerable<OperandForm> NonAlterableDestinations()
    {
        for (var reg = 0; reg < 8; reg++) yield return new(1, reg);
        for (var reg = 2; reg <= 4; reg++) yield return new(7, reg);
    }
}
