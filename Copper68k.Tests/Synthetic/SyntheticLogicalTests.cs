using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticLogicalTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void LogicalAndUnaryBoundaries(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-boundaries");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var family in LogicalSpecification.BinaryFamilies)
        foreach (var source in ArithmeticSpecification.Boundaries(width))
        foreach (var destination in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++) Case(machine, report, family, width, new(0, 0), source, destination, ccr);
        foreach (var family in LogicalSpecification.UnaryFamilies)
        foreach (var width in family == "TAS" ? new[] { 1 } : new[] { 1, 2, 4 })
        foreach (var value in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++) Case(machine, report, family, width, new(0, 0), 0, value, ccr);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void LogicalAndUnaryAddressing(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-addressing");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var family in LogicalSpecification.BinaryFamilies.Concat(new[] { "AND.store", "OR.store" }).Concat(LogicalSpecification.UnaryFamilies))
        {
            if (family == "TAS" && width != 1) continue;
            var sourceEa = family is "AND" or "OR" || family == "TST" && machine.Model.FullIndex;
            var forms = sourceEa ? ArithmeticSpecification.Sources(width, family == "TST") : ArithmeticSpecification.Alterable();
            foreach (var form in forms)
            {
                if (family.EndsWith(".store") && form.Mode == 0) continue;
                foreach (var supervisor in new[] { false, true })
                    Case(machine, report, family, width, form, 0x8002, 0x7ffffffe, 31, supervisor: supervisor);
            }
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures())
            foreach (var form in sourceEa ? new[] { new OperandForm(6, 0), new OperandForm(7, 3) } : new[] { new OperandForm(6, 0) })
                Case(machine, report, family, width, form, 0x8002, 0x7ffffffe, 31, index: index);
            for (var src = 0; src < 8; src++)
            for (var dst = 0; dst < 8; dst++)
                Case(machine, report, family.Replace(".store", ""), width, new(0, src), 0x8002, 0x7ffffffe, 31, reg: dst);
            foreach (var index in new[] { new IndexFixture(Scale: 3), new IndexFixture(AddressIndex: true, LongIndex: true, Scale: 2) })
                Case(machine, report, family, width, new(6, 0), 0x8002, 0x7ffffffe, 31, index: index, negativeIndex: true);
            foreach (var form in new[] { new OperandForm(7, 0), new OperandForm(7, 1) })
                Case(machine, report, family, width, form, 0x8002, 0x7ffffffe, 31,
                    options: new(SourceAbsoluteWord: 0xff80, DestinationAbsoluteWord: 0xff80, SourceAbsoluteLong: 0x10006000, DestinationAbsoluteLong: 0x10006000));
        }
        report.Complete(output);
    }

    internal static void Case(SyntheticMachine machine, CoverageBatch report, string family, int width, OperandForm form, uint source, uint destination, int ccr,
        int reg = 1, bool supervisor = true, IndexFixture? index = null, AddressOptions? options = null, bool negativeIndex = false)
    {
        machine.Reset(ccr, supervisor);
        if (negativeIndex) { machine.Core.State.D[7] = 0xffff8002; machine.Core.State.SetActiveStackPointer(0xffff8002); }
        var unary = LogicalSpecification.UnaryFamilies.Contains(family); var immediate = family.EndsWith('I');
        var store = family.EndsWith(".store") || family == "EOR";
        var size = ArithmeticSpecification.SizeField(width); var words = new List<ushort>();
        ushort opcode;
        if (unary) opcode = (ushort)((family switch { "CLR" => 0x4200, "NEG" => 0x4400, "NEGX" => 0x4000, "NOT" => 0x4600, "TST" => 0x4a00, _ => 0x4ac0 }) | (family == "TAS" ? 0 : size << 6) | form.Mode << 3 | form.Register);
        else if (immediate) opcode = (ushort)((family == "ORI" ? 0 : family == "ANDI" ? 0x200 : 0xa00) | size << 6 | form.Mode << 3 | form.Register);
        else opcode = (ushort)((family.StartsWith("AND") ? 0xc000 : family.StartsWith("OR") ? 0x8000 : 0xb000) | reg << 9 | (size + (store ? 4 : 0)) << 6 | form.Mode << 3 | form.Register);
        words.Add(opcode);
        if (immediate) { if (width == 4) words.Add((ushort)(source >> 16)); words.Add((ushort)(source & MoveSpecification.Mask(width))); }
        machine.Core.State.D[reg] = ArithmeticSpecification.RegisterBits(store ? source : destination, width);
        var fixture = new OperandFixture(machine, words, form, width, unary || immediate || store ? destination : source, source: !store && !immediate && !unary, index: index, options: options);
        var expected = fixture.Expected;
        if (unary)
        {
            var result = LogicalSpecification.Unary(family, fixture.Value, width, expected.Sr);
            expected.Sr = result.Sr; if (family != "TST") fixture.Write(result.Value);
        }
        else
        {
            var lhs = immediate || store ? fixture.Value : expected.D[reg];
            var rhs = immediate ? source : store ? expected.D[reg] : fixture.Value;
            var result = LogicalSpecification.Binary(family, lhs, rhs) & MoveSpecification.Mask(width);
            expected.Sr = SyntheticExecution.MoveFlags(expected.Sr, result, width);
            if (immediate || store) fixture.Write(result); else expected.D[reg] = (expected.D[reg] & ~MoveSpecification.Mask(width)) | result;
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{family}/{width}/{form.Id}/r{reg}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/s={source:X8}/d={destination:X8}/ccr={ccr:X2}");
    }
}
