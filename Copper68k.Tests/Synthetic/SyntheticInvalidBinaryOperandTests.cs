using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidBinaryOperandTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidBinaryOperandsTrapBeforeArithmeticLogicalOrTransferEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "integer-binary-invalid-operands");
        // M68000PM ADD/SUB/CMP admit An sources for word/long; AND/OR never
        // admit An. Their memory destinations and EOR's destinations exclude
        // PC/#data. Every data-source EA table ends at mode-7 field 4.
        // Opmodes 3/7 encode address arithmetic or word multiply/divide.
        // Mode 0/1 destination aliases (extend/decimal/packing/CMPM/EXG)
        // are excluded, and existing word-muldiv An tests remain separate.
        var opcodes = InvalidWords().ToArray();
        Assert.Equal(1896, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x8008); // OR.B A0,D0
        Assert.Contains(opcodes, x => x.Opcode == 0xc08f); // AND.L A7,D0
        Assert.Contains(opcodes, x => x.Opcode == 0xd008); // ADD.B A0,D0
        Assert.Contains(opcodes, x => x.Opcode == 0x90ff); // SUBA.W unassigned EA
        Assert.Contains(opcodes, x => x.Opcode == 0xbfbc); // EOR.L D7,#data
        Assert.Contains(opcodes, x => x.Opcode == 0x80ff); // DIVU.W unassigned EA
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x9048 or 0xb08f or 0xd0cc or
            0x803c or 0xc0fb or 0x80c8 or 0xc1c8 or 0x8100 or 0x8148 or
            0x8180 or 0xc140 or 0xc148 or 0xc188 or 0xb108 or 0xd100 or 0x9108);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void UnassignedLongAndRegisterDestinationWordsTrapWithoutExchangeEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-unassigned-c180");
        // M68000PM 4-17: AND's destination-EA form requires memory.
        // 4-105: EXG uses opmode fields 01000, 01001, 10001, excluding
        // the C180..C187 register-EA words. These are unassigned, not EXG.
        var opcodes = (from registerField in Enumerable.Range(0, 8)
                       from eaRegister in Enumerable.Range(0, 8)
                       select (ushort)(0xc180 | registerField << 9 | eaRegister)).ToArray();
        Assert.Equal(64, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Distinct().Count());
        Assert.Contains((ushort)0xc180, opcodes);
        Assert.Contains((ushort)0xcf87, opcodes);
        Assert.DoesNotContain(opcodes, x => x is 0xc140 or 0xc148 or 0xc188 or 0xc190);
        foreach (var opcode in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode,
                $"UNASSIGNED/C180/register-fields={(opcode >> 9) & 7},{opcode & 7}", supervisor, ccr);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> InvalidWords()
    {
        // Declarative instruction fields from the manual, independent of the
        // production classifier. A non-data source permits word/long An.
        foreach (var (line, family, dataOnly, operation3, operation7) in new[]
        {
            (0x8000, "OR", true, "DIVU.W", "DIVS.W"),
            (0x9000, "SUB", false, "SUBA.W", "SUBA.L"),
            (0xb000, "CMP", false, "CMPA.W", "CMPA.L"),
            (0xc000, "AND", true, "MULU.W", "MULS.W"),
            (0xd000, "ADD", false, "ADDA.W", "ADDA.L")
        })
        for (var registerField = 0; registerField < 8; registerField++)
        {
            for (var size = 0; size < 3; size++)
            {
                var sized = $"{family}.{new[] { "B", "W", "L" }[size]}";
                if (dataOnly || size == 0)
                for (var register = 0; register < 8; register++)
                    yield return Case(size, new(1, register), $"{sized}/EA->D{registerField}");
                for (var register = 5; register < 8; register++)
                    yield return Case(size, new(7, register), $"{sized}/EA->D{registerField}");
                var destinationFamily = family == "CMP" ? "EOR" : family;
                for (var register = 2; register < 8; register++)
                    yield return Case(size + 4, new(7, register),
                        $"{destinationFamily}.{new[] { "B", "W", "L" }[size]}/D{registerField}->EA");
            }
            foreach (var (opmode, operation) in new[] { (3, operation3), (7, operation7) })
            for (var register = 5; register < 8; register++)
                yield return Case(opmode, new(7, register), $"{operation}/register-field={registerField}");

            (ushort, string, string) Case(int opmode, OperandForm form, string operation) =>
                ((ushort)(line | registerField << 9 | opmode << 6 | form.Mode << 3 | form.Register),
                    operation, form.Mode == 7 && form.Register > 4
                        ? $"unassigned-EA(7,{form.Register})" : form.Id);
        }
    }
}
