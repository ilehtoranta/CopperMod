using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidAddressOperationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidControlAddressesTrapBeforeRegisterStackOrBranchEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "control-invalid-addresses");
        // M68000PM 4-108/109/110/159: control EAs only. PEA's mode 0
        // and mode 1 words are SWAP/BKPT, so they are not illegal PEA.
        // 49C0..49C7 are EXTB.L aliases, covered by transfer-registers.
        // Mode-7 registers 5..7 are explicitly unassigned EA encodings.
        var opcodes = InvalidOpcodes().ToArray();
        Assert.Equal(372, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x41c0);
        Assert.Contains(opcodes, x => x.Opcode == 0x4fff);
        Assert.Contains(opcodes, x => x.Opcode == 0x4858);
        Assert.Contains(opcodes, x => x.Opcode == 0x487c);
        Assert.Contains(opcodes, x => x.Opcode == 0x4e80);
        Assert.Contains(opcodes, x => x.Opcode == 0x4eff);
        Assert.DoesNotContain(opcodes, x => x.Opcode is >= 0x49c0 and <= 0x49c7);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x4840 or 0x4848 or 0x41d0 or 0x41fb or 0x487b or 0x4e90 or 0x4ed0);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> InvalidOpcodes()
    {
        foreach (var form in InvalidForms())
        {
            for (var destination = 0; destination < 8; destination++)
            {
                if (destination == 4 && form.Mode == 0) continue; // EXTB.L Dn
                yield return ((ushort)(0x41c0 | destination << 9 | form.Mode << 3 | form.Register),
                    "LEA.L", $"{FormName(form)}->A{destination}");
            }
            foreach (var (encoding, family) in new[] { (0x4e80, "JSR"), (0x4ec0, "JMP") })
                yield return ((ushort)(encoding | form.Mode << 3 | form.Register), family, FormName(form));
            if (form.Mode is not (0 or 1))
                yield return ((ushort)(0x4840 | form.Mode << 3 | form.Register), "PEA.L", FormName(form));
        }
    }

    private static IEnumerable<OperandForm> InvalidForms()
    {
        foreach (var mode in new[] { 0, 1, 3, 4 })
        for (var register = 0; register < 8; register++) yield return new(mode, register);
        for (var register = 4; register < 8; register++) yield return new(7, register);
    }

    private static string FormName(OperandForm form) => form.Mode == 7 && form.Register > 4
        ? $"unassigned-EA(7,{form.Register})" : form.Id;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidMovemAddressesTrapBeforeMaskOrTransferEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-movem-invalid-operands");
        // M68000PM 4-128..130: store admits control-alterable/predecrement;
        // load admits control/postincrement. Store mode 0 is EXT, not MOVEM.
        var opcodes = InvalidMovemOpcodes().ToArray();
        Assert.Equal(100, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x4888);
        Assert.Contains(opcodes, x => x.Opcode == 0x48ff);
        Assert.Contains(opcodes, x => x.Opcode == 0x4c80);
        Assert.Contains(opcodes, x => x.Opcode == 0x4cff);
        Assert.DoesNotContain(opcodes, x => x.Opcode is 0x4880 or 0x48c0 or 0x48a0 or 0x4c98 or 0x4cba);
        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            // Nonempty mask selects D0 and A0; a mistaken dispatch must not
            // update either register, any operand canary, or its base.
            InvalidOperandScenario.Run(machine, report, opcode, $"{family}/invalid/{form}", supervisor, ccr, 0x0101);
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> InvalidMovemOpcodes()
    {
        foreach (var load in new[] { false, true })
        foreach (var size in new[] { 2, 4 })
        {
            foreach (var mode in load ? new[] { 0, 1, 4 } : new[] { 1, 3 })
            for (var register = 0; register < 8; register++) yield return Case(new(mode, register));
            for (var register = load ? 4 : 2; register < 8; register++) yield return Case(new(7, register));
            (ushort, string, string) Case(OperandForm form) =>
                ((ushort)(0x4880 | (load ? 0x0400 : 0) | (size == 4 ? 0x0040 : 0) | form.Mode << 3 | form.Register),
                    $"MOVEM.{(size == 4 ? "L" : "W")}", $"{(load ? "load" : "store")}/{FormName(form)}");
        }
    }
}
