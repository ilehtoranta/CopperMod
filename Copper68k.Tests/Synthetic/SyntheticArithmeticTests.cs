using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticArithmeticTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void ArithmeticBoundariesAndAllConditionCodes(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-boundaries");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var source in ArithmeticSpecification.Boundaries(width))
        foreach (var destination in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++)
        {
            foreach (var family in new[] { "ADD", "SUB", "CMP", "ADDI", "SUBI", "CMPI" })
                Case(machine, report, family, width, new(0, 0), source, destination, ccr, scenario: "boundary-ccr");
            foreach (var family in new[] { "ADDQ", "SUBQ" })
            foreach (var count in new[] { 1u, 8u })
                Case(machine, report, family, width, new(0, 0), count, destination, ccr, scenario: "quick-boundary-ccr");
        }
        foreach (var width in new[] { 2, 4 })
        foreach (var family in new[] { "ADDA", "SUBA", "CMPA" })
        foreach (var source in ArithmeticSpecification.Boundaries(width))
        foreach (var destination in ArithmeticSpecification.Boundaries(4))
        for (var ccr = 0; ccr < 32; ccr++)
            Case(machine, report, family, width, new(0, 0), source, destination, ccr, scenario: "address-boundary-ccr");
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void ArithmeticAddressingModesAndFullExtensions(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-addressing");
        foreach (var width in new[] { 1, 2, 4 })
        {
            foreach (var family in new[] { "ADD", "SUB", "CMP", "ADDA", "SUBA", "CMPA" })
            {
                if (width == 1 && family.EndsWith('A')) continue;
                foreach (var form in ArithmeticSpecification.Sources(width))
                foreach (var reg in new[] { 0, 1, 7 })
                    Case(machine, report, family, width, form, 0x8002, 0x7ffffffe, 31, reg: reg);
                if (machine.Model.FullIndex)
                foreach (var index in IndexFixture.FullStructures())
                foreach (var form in new[] { new OperandForm(6, 0), new OperandForm(7, 3) })
                    Case(machine, report, family, width, form, 0x8002, 0x7ffffffe, 31, index: index);
            }
            foreach (var family in new[] { "ADD.store", "SUB.store", "ADDI", "SUBI", "CMPI", "ADDQ", "SUBQ" })
            {
                foreach (var form in ArithmeticSpecification.Alterable(family.EndsWith('Q') && width != 1))
                {
                    if (family.EndsWith(".store") && form.Mode == 0) continue;
                    Case(machine, report, family, width, form, family.EndsWith('Q') ? 8u : 0x8002u, 0x7ffffffe, 31);
                }
                if (family == "CMPI" && machine.Model.FullIndex)
                foreach (var form in new[] { new OperandForm(7, 2), new OperandForm(7, 3) })
                    Case(machine, report, family, width, form, 0x8002, 0x7ffffffe, 31);
                if (machine.Model.FullIndex)
                foreach (var index in IndexFixture.FullStructures())
                foreach (var form in family == "CMPI" ? new[] { new OperandForm(6, 0), new OperandForm(7, 3) } : new[] { new OperandForm(6, 0) })
                    Case(machine, report, family, width, form, family.EndsWith('Q') ? 8u : 0x8002u, 0x7ffffffe, 31, index: index);
            }
        }
        // All D/An field selections, including destination/source and A7 aliases.
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var family in new[] { "ADD", "SUB", "CMP", "ADDA", "SUBA", "CMPA" })
        {
            if (width == 1 && family.EndsWith('A')) continue;
            for (var src = 0; src < 8; src++)
            for (var dst = 0; dst < 8; dst++)
                Case(machine, report, family, width, new(0, src), 0x8002, 0x7ffffffe, 31, reg: dst, scenario: "all-register-fields");
        }
        // Address-register sources have separate encodings. Include every
        // source/destination field and A7 bank, with alias CCR preservation
        // and nonzero upper source words that expose missing sign extension.
        foreach (var width in new[] { 2, 4 })
        foreach (var family in new[] { "ADDA", "SUBA", "CMPA" })
        foreach (var source in width == 2
            ? new[] { 0u, 1u, 0x12348000u, 0xffff7fffu }
            : new[] { 0u, 1u, 0x80000000u, uint.MaxValue })
        for (var src = 0; src < 8; src++)
        for (var dst = 0; dst < 8; dst++)
        foreach (var supervisor in new[] { false, true })
        foreach (var ccr in src == dst ? Enumerable.Range(0, 32) : new[] { 0, 31 })
            Case(machine, report, family, width, new(1, src), source, 0x7ffffffe, ccr,
                reg: dst, supervisor: supervisor, scenario: "all-address-register-fields");
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void ArithmeticStackIndexAddressBoundariesAndQuickCounts(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-scenarios");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var family in new[] { "ADD", "SUB", "CMP", "ADDA", "SUBA", "CMPA", "ADD.store", "SUB.store", "ADDI", "SUBI", "CMPI", "ADDQ", "SUBQ" })
        {
            if (width == 1 && family.EndsWith('A')) continue;
            foreach (var form in new[] { new OperandForm(2, 0), new OperandForm(3, 7), new OperandForm(4, 7), new OperandForm(5, 0), new OperandForm(6, 0), new OperandForm(7, 0), new OperandForm(7, 1) })
            for (var ccr = 0; ccr < 32; ccr++)
                Case(machine, report, family, width, form, family.EndsWith('Q') ? 8u : 0x8002u, 0x7ffffffe, ccr, supervisor: false, scenario: "EA-all-CCR-user-stack");
            foreach (var form in new[] { new OperandForm(7, 0), new OperandForm(7, 1) })
                Case(machine, report, family, width, form, family.EndsWith('Q') ? 1u : 0x8002u, 0x7ffffffe, 31,
                    options: new(SourceAbsoluteWord: 0xff80, DestinationAbsoluteWord: 0xff80, SourceAbsoluteLong: 0x10006000, DestinationAbsoluteLong: 0x10006000), scenario: "negative-abs-and-external-width");
            foreach (var addressIndex in new[] { false, true })
            foreach (var longIndex in new[] { false, true })
            for (var scale = 0; scale < 4; scale++)
                Case(machine, report, family, width, new(6, 0), family.EndsWith('Q') ? 1u : 0x8002u, 0x7ffffffe, 31,
                    index: new(AddressIndex: addressIndex, LongIndex: longIndex, Scale: scale, EarlyFormatBit: !machine.Model.FullIndex), scenario: "signed-index-scale", negativeIndex: true);
            foreach (var supervisor in new[] { false, true })
                Case(machine, report, family, width, new(2, 0), family.EndsWith('Q') ? 1u : 0x8002u, 0x7ffffffe, 31,
                    supervisor: supervisor, scenario: "odd-data", oddAddress: true);
        }
        foreach (var family in new[] { "ADDQ", "SUBQ" })
        foreach (var width in new[] { 1, 2, 4 })
        for (uint count = 1; count <= 8; count++)
        for (var reg = 0; reg < 8; reg++)
        for (var ccr = 0; ccr < 32; ccr++)
            Case(machine, report, family, width, new(0, reg), count, 0x7ffffffe, ccr, scenario: "all-quick-fields");
        report.Complete(output);
    }

    internal static void Case(SyntheticMachine machine, CoverageBatch report, string family, int width, OperandForm form, uint source, uint destination, int ccr,
        int reg = 1, IndexFixture? index = null, string scenario = "canonical", bool supervisor = true, AddressOptions? options = null, bool negativeIndex = false, bool oddAddress = false)
    {
        machine.Reset(ccr, supervisor);
        if (negativeIndex) { machine.Core.State.D[7] = 0xffff8002; machine.Core.State.SetActiveStackPointer(0xffff8002); }
        if (oddAddress) machine.Core.State.A[0] = 0x4001;
        var size = ArithmeticSpecification.SizeField(width);
        var address = family.EndsWith('A'); var immediate = family.EndsWith('I'); var quick = family.EndsWith('Q'); var store = family.EndsWith(".store");
        var subtract = family.StartsWith("SUB") || family.StartsWith("CMP"); var compare = family.StartsWith("CMP");
        ushort opcode;
        var words = new List<ushort>();
        if (immediate)
        {
            opcode = (ushort)((family == "ADDI" ? 0x600 : family == "SUBI" ? 0x400 : 0xc00) | size << 6 | form.Mode << 3 | form.Register);
            words.Add(opcode); if (width == 4) words.Add((ushort)(source >> 16)); words.Add((ushort)(source & MoveSpecification.Mask(width)));
        }
        else if (quick)
        {
            opcode = (ushort)(0x5000 | (family == "SUBQ" ? 0x100 : 0) | ((int)source & 7) << 9 | size << 6 | form.Mode << 3 | form.Register);
            words.Add(opcode);
        }
        else
        {
            opcode = (ushort)((compare ? 0xb000 : subtract ? 0x9000 : 0xd000) | reg << 9 | (address ? (width == 2 ? 3 : 7) : store ? 4 + size : size) << 6 | form.Mode << 3 | form.Register);
            words.Add(opcode);
        }
        if (address)
        {
            if (reg == 7) machine.Core.State.SetActiveStackPointer(destination); else machine.Core.State.A[reg] = destination;
        }
        else machine.Core.State.D[reg] = ArithmeticSpecification.RegisterBits(store ? source : destination, width);
        var fixture = new OperandFixture(machine, words, form, width, immediate || quick || store ? destination : source, source: !store, index: index, options: options);
        var expected = fixture.Expected;
        var rhs = immediate || quick ? source & MoveSpecification.Mask(width) : store ? expected.D[reg] : fixture.Value;
        var lhs = address ? expected.A[reg] : immediate || quick || store ? fixture.Value : expected.D[reg];
        if (address || quick && form.Mode == 1)
        {
            var addressReg = address ? reg : form.Register;
            lhs = expected.A[addressReg];
            if (address && width == 2) rhs = unchecked((uint)ArithmeticSpecification.Signed(rhs, 2));
            if (compare) expected.Sr = ArithmeticSpecification.Binary(lhs, rhs, 4, expected.Sr, true, true).Sr;
            else expected.A[addressReg] = subtract ? unchecked(lhs - rhs) : unchecked(lhs + rhs);
        }
        else
        {
            var result = ArithmeticSpecification.Binary(lhs, rhs, width, expected.Sr, subtract, compare);
            expected.Sr = result.Sr;
            if (!compare)
            {
                if (immediate || quick || store) fixture.Write(result.Value);
                else expected.D[reg] = (expected.D[reg] & ~MoveSpecification.Mask(width)) | result.Value;
            }
        }
        var id = $"{machine.Model.Id}/{family}/{width}/{form.Id}/r{reg}/{scenario}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/s={source:X8}/d={destination:X8}/ccr={ccr:X2}";
        if (oddAddress && width != 1 && !machine.Model.FullIndex)
        {
            try
            {
                machine.Core.ExecuteInstruction();
                var mismatch = machine.Core.State.ProgramCounter != 0x9030 || machine.Core.State.LastExceptionVector != 3 ? "Expected architectural address error" :
                    machine.Bus.Accesses.Any(a => a.Address == 0x4001 && a.Kind is Copper68k.M68kBusAccessKind.CpuDataRead or Copper68k.M68kBusAccessKind.CpuDataWrite) ? "Faulting operand reached bus" : null;
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        else SyntheticExecution.Run(machine, expected, report, id);
    }
}
