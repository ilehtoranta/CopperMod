using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticBitShiftTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void ShiftsCountsValuesAndFlags(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-shifts");
        foreach (var family in LogicalSpecification.Shifts)
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var value in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++)
        {
            for (var count = 1; count <= 8; count++) Shift(machine, report, family, width, new(0, 0), value, (uint)count, ccr, false);
            foreach (var count in new uint[] { 0, 1, 7, 8, 15, 16, 31, 32, 33, 63, 64, 65, 127, 0xffffffff })
                Shift(machine, report, family, width, new(0, 0), value, count, ccr, true);
        }
        foreach (var family in LogicalSpecification.Shifts)
        for (var src = 0; src < 8; src++)
        for (var dst = 0; dst < 8; dst++)
        foreach (var width in new[] { 1, 2, 4 }) Shift(machine, report, family, width, new(0, dst), 0x80008001, 33, 31, true, src);
        foreach (var family in LogicalSpecification.Shifts)
        foreach (var form in ArithmeticSpecification.Alterable().Where(f => f.Memory))
        for (var ccr = 0; ccr < 32; ccr++) Shift(machine, report, family, 2, form, 0x8001, 1, ccr, false);
        if (machine.Model.FullIndex)
        foreach (var family in LogicalSpecification.Shifts)
        foreach (var index in IndexFixture.FullStructures()) Shift(machine, report, family, 2, new(6, 0), 0x8001, 1, 31, false, index: index);
        report.Complete(output);
    }

    private static void Shift(SyntheticMachine machine, CoverageBatch report, string family, int width, OperandForm form, uint value, uint count, int ccr, bool registerCount, int countReg = 1, IndexFixture? index = null)
    {
        machine.Reset(ccr); var operation = Array.IndexOf(LogicalSpecification.Shifts, family);
        var opcode = form.Mode == 0 ? (ushort)(0xe000 | ((registerCount ? countReg : (int)count & 7) << 9) | (operation & 1) << 8 |
            ArithmeticSpecification.SizeField(width) << 6 | (registerCount ? 32 : 0) | (operation >> 1) << 3 | form.Register) :
            (ushort)(0xe0c0 | operation << 8 | form.Mode << 3 | form.Register);
        if (registerCount) machine.Core.State.D[countReg] = count;
        var fixture = new OperandFixture(machine, [opcode], form, width, value, source: false, index: index);
        var actualCount = form.Mode == 0 && registerCount ? (int)(fixture.Expected.D[countReg] & 63) : (int)count;
        var result = LogicalSpecification.Shift(family, fixture.Value, actualCount, width, fixture.Expected.Sr);
        fixture.Write(result.Value); fixture.Expected.Sr = result.Sr;
        SyntheticExecution.Run(machine, fixture.Expected, report, $"{machine.Model.Id}/{family}/{width}/{form.Id}/count={(registerCount ? "Dn" : "immediate")}/r{countReg}/{index?.Id ?? "brief"}/op={opcode:X4}/value={value:X8}/count={count:X8}/ccr={ccr:X2}");
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void BitOperationsModesCountsAndAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-bits");
        foreach (var family in new[] { "BTST", "BCHG", "BCLR", "BSET" })
        foreach (var immediate in new[] { false, true })
        {
            foreach (var form in ArithmeticSpecification.Sources(1, false))
            {
                if (family != "BTST" && form.Mode == 7 && form.Register > 1 || immediate && form.Mode == 7 && form.Register == 4) continue;
                foreach (var bit in new uint[] { 0, 7, 8, 31, 32, 63, 255, 0xffffffff })
                for (var ccr = 0; ccr < 32; ccr++) Bit(machine, report, family, form, 0x80000081, bit, ccr, immediate);
            }
            for (var src = 0; src < 8; src++)
            for (var dst = 0; dst < 8; dst++) Bit(machine, report, family, new(0, dst), 0x80000081, 31, 31, immediate, src);
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures())
            foreach (var form in family == "BTST" ? new[] { new OperandForm(6, 0), new OperandForm(7, 3) } : new[] { new OperandForm(6, 0) })
                Bit(machine, report, family, form, 0x81, 7, 31, immediate, index: index);
        }
        report.Complete(output);
    }
    private static void Bit(SyntheticMachine machine, CoverageBatch report, string family, OperandForm form, uint value, uint bit, int ccr, bool immediate, int reg = 1, IndexFixture? index = null)
    {
        machine.Reset(ccr); var operation = family == "BTST" ? 0 : family == "BCHG" ? 1 : family == "BCLR" ? 2 : 3;
        var opcode = (ushort)((immediate ? 0x800 : 0x100 | reg << 9) | operation << 6 | form.Mode << 3 | form.Register);
        machine.Core.State.D[reg] = bit;
        var fixture = new OperandFixture(machine, immediate ? [opcode, (ushort)(bit & 255)] : [opcode], form, form.Mode == 0 ? 4 : 1, value,
            source: family == "BTST", index: index);
        var actualBit = immediate ? bit & 255 : fixture.Expected.D[reg];
        var mask = 1u << (int)(actualBit % (form.Mode == 0 ? 32u : 8u));
        fixture.Expected.Sr = (ushort)((fixture.Expected.Sr & ~4) | ((fixture.Value & mask) == 0 ? 4 : 0));
        if (operation != 0) fixture.Write(operation == 1 ? fixture.Value ^ mask : operation == 2 ? fixture.Value & ~mask : fixture.Value | mask);
        SyntheticExecution.Run(machine, fixture.Expected, report, $"{machine.Model.Id}/{family}/{(form.Mode == 0 ? 4 : 1)}/{form.Id}/{(immediate ? "immediate" : "dynamic")}/r{reg}/{index?.Id ?? "brief"}/op={opcode:X4}/bit={bit:X8}/ccr={ccr:X2}");
    }
}
