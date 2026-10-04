using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticInvalidLogicalTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InvalidImmediateAndBitDestinationsTrapWithoutOperandEffects(string modelId)
    {
        // M68000PM: ANDI/EORI/ORI use data-alterable destinations, except their
        // separately encoded CCR/SR forms. Static BTST excludes #data whereas
        // dynamic BTST permits it; BCHG/BCLR/BSET require data-alterable EAs.
        // Unassigned mode-7 register encodings (5..7) are outside this matrix.
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-invalid-operands");
        var opcodes = InvalidOpcodes().ToArray();
        Assert.Equal(207, opcodes.Length);
        Assert.Equal(opcodes.Length, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0x0008);
        Assert.Contains(opcodes, x => x.Opcode == 0x083c);
        Assert.DoesNotContain(opcodes, x => x.Opcode == 0x013c); // BTST D0,#data
        Assert.DoesNotContain(opcodes, x => x.Opcode == 0x0108); // MOVEP
        Assert.DoesNotContain(opcodes, x => x.Opcode == 0x003c); // ORI to CCR
        Assert.DoesNotContain(opcodes, x => x.Opcode == 0x007c); // ORI to SR

        foreach (var (opcode, family, form) in opcodes)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            // Detect a mistaken operand read/write as well as register changes.
            var forbidden = new List<uint>();
            for (var reg = 0; reg < 8; reg++)
            for (var offset = -8; offset < 8; offset++)
            {
                var address = machine.Model.Physical(unchecked(machine.Core.State.A[reg] + (uint)offset));
                machine.Bus.Initialize(address, 0xa5, 1);
                forbidden.Add(address);
            }
            var expected = SyntheticExecution.Prepare(machine, [opcode, 0x0011, 0x81a5, 0x4e71]);
            expected.ForbiddenOperandReads.UnionWith(forbidden);
            SyntheticExecution.ExpectException(machine, expected, 4);
            SyntheticExecution.Run(machine, expected, report,
                $"{modelId}/{family}/invalid/{form}/op={opcode:X4}/super={supervisor}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    private static IEnumerable<(ushort Opcode, string Family, string Form)> InvalidOpcodes()
    {
        foreach (var (encoding, family) in new[] { (0x0000, "ORI"), (0x0200, "ANDI"), (0x0a00, "EORI") })
        for (var size = 0; size < 3; size++)
        foreach (var form in NonAlterableDestinations())
        {
            if (size < 2 && form.Mode == 7 && form.Register == 4) continue; // CCR/SR
            yield return ((ushort)(encoding | size << 6 | form.Mode << 3 | form.Register),
                $"{family}.{new[] { "B", "W", "L" }[size]}", form.Id);
        }

        foreach (var (operation, family) in new[] { (0, "BTST"), (1, "BCHG"), (2, "BCLR"), (3, "BSET") })
        foreach (var form in NonAlterableDestinations())
        {
            if (operation == 0 && form.Mode == 7 && form.Register is 2 or 3) continue; // Static PC-relative BTST is legal.
            yield return ((ushort)(0x0800 | operation << 6 | form.Mode << 3 | form.Register), $"{family}.static", form.Id);
        }

        // Dynamic mode-1 encodings belong to MOVEP; do not reinterpret them as
        // illegal bit operations. Dynamic BTST has no invalid assigned mode-7 EA.
        foreach (var (operation, family) in new[] { (1, "BCHG"), (2, "BCLR"), (3, "BSET") })
        for (var source = 0; source < 8; source++)
        for (var destination = 2; destination <= 4; destination++)
            yield return ((ushort)(0x0100 | source << 9 | operation << 6 | 7 << 3 | destination),
                $"{family}.dynamic.D{source}", new OperandForm(7, destination).Id);
    }

    private static IEnumerable<OperandForm> NonAlterableDestinations()
    {
        for (var reg = 0; reg < 8; reg++) yield return new(1, reg);
        for (var reg = 2; reg <= 4; reg++) yield return new(7, reg);
    }
}
