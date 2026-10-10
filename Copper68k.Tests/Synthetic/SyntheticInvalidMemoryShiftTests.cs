using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidMemoryShiftTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidMemoryShiftOperandsTrapWithoutReadingOrUpdatingOperands(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-memory-shift-invalid-operands");
        // M68000PM 4-24, 4-115, 4-162, 4-166: word-sized, count-one
        // memory shifts/rotates admit memory-alterable EAs only. Register
        // shifts and 020+ bitfields have separate first-word encodings.
        var opcodes = InvalidWords().ToArray();
        Assert.Equal(176, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0xe0c0);
        Assert.Contains(opcodes, x => x.Opcode == 0xe3cf);
        Assert.Contains(opcodes, x => x.Opcode == 0xe7ff);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0xe040 or 0xe0d0 or 0xe7f9 or 0xe8c0 or 0xefc7);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}.W/memory/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> InvalidWords()
    {
        foreach (var (encoding, family) in new[] { (0xe0c0, "ASR"), (0xe1c0, "ASL"),
            (0xe2c0, "LSR"), (0xe3c0, "LSL"), (0xe4c0, "ROXR"), (0xe5c0, "ROXL"),
            (0xe6c0, "ROR"), (0xe7c0, "ROL") })
        {
            foreach (var mode in new[] { 0, 1 })
            for (var register = 0; register < 8; register++) yield return Case(new(mode, register));
            for (var register = 2; register < 8; register++) yield return Case(new(7, register));

            (ushort, string, string) Case(OperandForm form) =>
                ((ushort)(encoding | form.Mode << 3 | form.Register), family,
                    form.Mode == 7 && form.Register > 4 ? $"unassigned-EA(7,{form.Register})" : form.Id);
        }
    }
}
