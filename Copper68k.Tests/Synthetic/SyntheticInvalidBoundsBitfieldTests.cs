using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidBoundsBitfieldTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidChkSourcesTrapWithoutBoundsOrFlagEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-chk-invalid-operands");
        // M68000PM 4-70: CHK.W/.L data sources exclude An; mode 7
        // registers 5..7 have no assigned EA. MC68020UM 6.1.5 specifies
        // vector 4 and the causing instruction PC for illegal encodings.
        var opcodes = ChkOpcodes().ToArray();
        Assert.Equal(176, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x4108);
        Assert.Contains(opcodes, x => x.Opcode == 0x4f8f);
        Assert.Contains(opcodes, x => x.Opcode == 0x413d);
        Assert.Contains(opcodes, x => x.Opcode == 0x41bd);
        Assert.Contains(opcodes, x => x.Opcode == 0x4f3f);
        Assert.Contains(opcodes, x => x.Opcode == 0x4fbf);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x4180 or 0x413b or 0x413c or 0x41bc or 0x41c8);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidBitfieldOperandsTrapBeforeExtensionOrOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-bitfield-invalid-operands");
        // M68000PM 4-31..52: Dn or control memory; no An/post/pre/#data.
        // Read-only fields also allow PC-relative control EAs; mutating ones
        // require alterable memory. Unassigned mode-7 registers are excluded.
        var opcodes = BitfieldOpcodes().ToArray();
        Assert.Equal(208, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0xe8c8);
        Assert.Contains(opcodes, x => x.Opcode == 0xeafa);
        Assert.Contains(opcodes, x => x.Opcode == 0xefdf);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0xe8c0 or 0xe8d0 or 0xe8fa or 0xedfb);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            // Immediate offset zero, width 32, source/destination D0 where used.
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr, 0);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> ChkOpcodes()
    {
        foreach (var word in new[] { false, true })
        for (var destination = 0; destination < 8; destination++)
        {
            for (var source = 0; source < 8; source++)
                yield return ((ushort)(0x4108 | (word ? 0x80 : 0) | destination << 9 | source),
                    word ? "CHK.W" : "CHK.L", $"A{source},D{destination}");
            for (var source = 5; source < 8; source++)
                yield return ((ushort)(0x4138 | (word ? 0x80 : 0) | destination << 9 | source),
                    word ? "CHK.W" : "CHK.L", $"unassigned-EA(7,{source}),D{destination}");
        }
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> BitfieldOpcodes()
    {
        var names = new[] { "BFTST", "BFEXTU", "BFCHG", "BFEXTS", "BFCLR", "BFFFO", "BFSET", "BFINS" };
        for (var operation = 0; operation < names.Length; operation++)
        {
            for (var mode = 1; mode <= 4; mode++)
            {
                if (mode == 2) continue;
                for (var reg = 0; reg < 8; reg++) yield return Case(new(mode, reg));
            }
            yield return Case(new(7, 4));
            if (operation is 2 or 4 or 6 or 7)
            { yield return Case(new(7, 2)); yield return Case(new(7, 3)); }
            (ushort, string, string) Case(OperandForm form) =>
                ((ushort)(0xe8c0 | operation << 8 | form.Mode << 3 | form.Register), names[operation], form.Id);
        }
    }
}
