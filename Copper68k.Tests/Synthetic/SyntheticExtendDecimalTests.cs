using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticExtendDecimalTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void ExtendComparisonAndAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-extend");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var family in new[] { "ADDX", "SUBX", "CMPM" })
        foreach (var s in ArithmeticSpecification.Boundaries(width))
        foreach (var d in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var memory in family == "CMPM" ? new[] { true } : new[] { false, true })
            Pair(machine, report, family, width, s, d, ccr, 0, 1, memory, true);
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var family in new[] { "ADDX", "SUBX", "CMPM" })
        for (var src = 0; src < 8; src++)
        for (var dst = 0; dst < 8; dst++)
        foreach (var supervisor in new[] { false, true })
        foreach (var memory in family == "CMPM" ? new[] { true } : new[] { false, true })
            Pair(machine, report, family, width, 1, MoveSpecification.Mask(width), 20, src, dst, memory, supervisor);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void PackingMemoryWordStrideAliasesAndAddressBoundaries(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-packing-memory");
        // Fixed M68000PM 4-157/196 examples: the unpacked operand is a
        // contiguous two-byte value. A7's special stride applies to the
        // packed byte operand, not independently to both halves of the word.
        Assert.Equal(0x814f, 0x8140 | 8 | 7); // PACK -(A7),-(A0)
        Assert.Equal(0x8f89, 0x8180 | 7 << 9 | 8 | 1); // UNPK -(A1),-(A7)
        foreach (var family in new[] { "PACK", "UNPK" })
        for (var src = 0; src < 8; src++)
        for (var dst = 0; dst < 8; dst++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            Pack(machine, report, family, family == "UNPK" ? 0x85u : 0x1234u,
                0xffff, ccr, src, dst, true, supervisor);
        // 000/010 lack both instructions; canonical cases above verify their
        // vector-4 outcomes. Unaligned/wrapping operands are legal on 020+.
        if (machine.Model.FullIndex)
        foreach (var family in new[] { "PACK", "UNPK" })
        foreach (var value in new uint[] { 0, 0x1234, 0x9abc, 0x8000, 0xffff, 0x85, 0x99 })
        foreach (var adjustment in new ushort[] { 0, 1, 0xffff, 0xff, 0xff00, 0x8888 })
        foreach (var (src, dst) in new[] { (7, 0), (0, 7), (7, 7), (0, 0), (0, 1), (1, 0) })
        foreach (var supervisor in new[] { false, true })
        foreach (var (sourceBase, destinationBase) in new[]
        { (0x4103u, 0x4705u), (0xffff8002u, 0xffff9001u), (1u, 0x01000001u), (0x4103u, 0x4103u) })
            Pack(machine, report, family, value, adjustment, 31, src, dst, true,
                supervisor, sourceBase, destinationBase);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void DecimalAndPacking(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-decimal");
        // All valid two-digit decimal inputs. Non-BCD operands have undefined
        // arithmetic results and are explicitly excluded from this semantic gate.
        foreach (var family in new[] { "ABCD", "SBCD" })
        for (var source = 0; source < 100; source++)
        for (var destination = 0; destination < 100; destination++)
        foreach (var ccr in new[] { 0, 4, 16, 20 })
            Pair(machine, report, family, 1, Bcd(source), Bcd(destination), ccr, 0, 1, false, true);
        foreach (var family in new[] { "ABCD", "SBCD" })
        for (var src = 0; src < 8; src++)
        for (var dst = 0; dst < 8; dst++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            Pair(machine, report, family, 1, 0x01, 0x99, ccr, src, dst, true, supervisor);
        foreach (var form in ArithmeticSpecification.Alterable())
        for (var value = 0; value < 100; value++)
        foreach (var ccr in new[] { 0, 4, 16, 20 })
            Nbcd(machine, report, form, Bcd(value), ccr);
        if (machine.Model.FullIndex)
        foreach (var index in IndexFixture.FullStructures()) Nbcd(machine, report, new(6, 0), 0x99, 20, index);
        foreach (var family in new[] { "PACK", "UNPK" })
        foreach (var value in new uint[] { 0, 1, 0x09, 0x99, 0x1234, 0xffff, 0x8000 })
        foreach (var adjustment in new ushort[] { 0, 1, 0xffff, 0xff, 0xff00, 0x8888 })
        for (var ccr = 0; ccr < 32; ccr++) Pack(machine, report, family, value, adjustment, ccr, 0, 1, false, true);
        foreach (var family in new[] { "PACK", "UNPK" })
        for (var src = 0; src < 8; src++)
        for (var dst = 0; dst < 8; dst++)
        foreach (var supervisor in new[] { false, true })
        foreach (var memory in new[] { false, true })
            // UNPK 85 with adjustment -1 produces 0804: distinct memory bytes
            // make a swapped write order observable for every register alias.
            Pack(machine, report, family, family == "UNPK" && memory ? 0x1285u : 0x1234u, 0xffff, 31, src, dst, memory, supervisor);
        report.Complete(output);
    }

    private static uint Bcd(int value) => (uint)((value / 10 << 4) | value % 10);
    private static ushort DecimalFlags(ushort sr, uint result, bool carry) =>
        (ushort)((sr & 0xffe0) | (carry ? 17 : 0) | (result == 0 && (sr & 4) != 0 ? 4 : 0));
    private static (uint Value, bool Carry) Decimal(uint d, uint s, int x, bool subtract)
    {
        var destination = (int)(d >> 4) * 10 + (int)(d & 15);
        var source = (int)(s >> 4) * 10 + (int)(s & 15);
        var result = subtract ? destination - source - x : destination + source + x;
        return (Bcd((result + 100) % 100), result < 0 || result >= 100);
    }

    private static void Pair(SyntheticMachine machine, CoverageBatch report, string family, int width, uint source, uint destination, int ccr,
        int src, int dst, bool memory, bool supervisor)
    {
        machine.Reset(ccr, supervisor);
        var compare = family == "CMPM"; var decimalOp = family is "ABCD" or "SBCD";
        var subtract = family is "SUBX" or "CMPM" or "SBCD";
        var opcode = (ushort)((compare ? 0xb108 : decimalOp ? subtract ? 0x8100 : 0xc100 : subtract ? 0x9100 : 0xd100) |
            dst << 9 | (decimalOp ? 0 : ArithmeticSpecification.SizeField(width) << 6) | (memory ? 8 : 0) | src);
        uint sourceAddress = 0, destinationAddress = 0;
        var a = (uint[])machine.Core.State.A.Clone();
        if (memory)
        {
            var sourceStride = (uint)(width == 1 && src == 7 ? 2 : width);
            var destinationStride = (uint)(width == 1 && dst == 7 ? 2 : width);
            sourceAddress = compare ? a[src] : a[src] - sourceStride;
            a[src] = compare ? a[src] + sourceStride : sourceAddress;
            destinationAddress = compare ? a[dst] : a[dst] - destinationStride;
            a[dst] = compare ? a[dst] + destinationStride : destinationAddress;
            machine.InitializePhysical(sourceAddress, source, width); machine.InitializePhysical(destinationAddress, destination, width);
        }
        else
        {
            machine.Core.State.D[src] = ArithmeticSpecification.RegisterBits(source, width);
            machine.Core.State.D[dst] = ArithmeticSpecification.RegisterBits(destination, width);
        }
        var expected = SyntheticExecution.Prepare(machine, [opcode]);
        if (memory) { a.CopyTo(expected.A, 0); source = machine.PeekPhysical(sourceAddress, width); destination = machine.PeekPhysical(destinationAddress, width); }
        else { source = expected.D[src]; destination = expected.D[dst]; }
        uint result;
        if (decimalOp)
        {
            var decimalResult = Decimal(destination & 255, source & 255, (ccr >> 4) & 1, subtract);
            result = decimalResult.Value; expected.Sr = DecimalFlags(expected.Sr, result, decimalResult.Carry); expected.DefinedSrMask = 0xfff5;
        }
        else
        {
            var arithmetic = ArithmeticSpecification.Binary(destination, source, width, expected.Sr, subtract, compare, !compare);
            result = arithmetic.Value; expected.Sr = arithmetic.Sr;
        }
        if (!compare)
        {
            if (memory) expected.Write(destinationAddress, result, width, machine.Model);
            else expected.D[dst] = (expected.D[dst] & ~MoveSpecification.Mask(width)) | result;
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{family}/{width}/r{src}-r{dst}/memory={memory}/super={supervisor}/op={opcode:X4}/s={source:X8}/d={destination:X8}/ccr={ccr:X2}");
    }

    private static void Nbcd(SyntheticMachine machine, CoverageBatch report, OperandForm form, uint value, int ccr, IndexFixture? index = null)
    {
        machine.Reset(ccr); var opcode = (ushort)(0x4800 | form.Mode << 3 | form.Register);
        var fixture = new OperandFixture(machine, [opcode], form, 1, value, source: false, index: index);
        var result = Decimal(0, fixture.Value, (ccr >> 4) & 1, true);
        fixture.Write(result.Value); fixture.Expected.Sr = DecimalFlags(fixture.Expected.Sr, result.Value, result.Carry); fixture.Expected.DefinedSrMask = 0xfff5;
        SyntheticExecution.Run(machine, fixture.Expected, report, $"{machine.Model.Id}/NBCD/1/{form.Id}/{index?.Id ?? "brief"}/op={opcode:X4}/value={value:X2}/ccr={ccr:X2}");
    }

    private static void Pack(SyntheticMachine machine, CoverageBatch report, string family, uint value, ushort adjustment, int ccr, int src, int dst, bool memory, bool supervisor,
        uint? sourceBase = null, uint? destinationBase = null)
    {
        machine.Reset(ccr, supervisor); var unpack = family == "UNPK";
        var opcode = (ushort)((unpack ? 0x8180 : 0x8140) | dst << 9 | (memory ? 8 : 0) | src);
        var sourceWidth = unpack ? 1 : 2; var destinationWidth = unpack ? 2 : 1;
        if (sourceBase.HasValue)
        {
            if (src == 7) machine.Core.State.SetActiveStackPointer(sourceBase.Value);
            else machine.Core.State.A[src] = sourceBase.Value;
            if (dst != src)
            {
                if (dst == 7) machine.Core.State.SetActiveStackPointer(destinationBase!.Value);
                else machine.Core.State.A[dst] = destinationBase!.Value;
            }
        }
        var a = (uint[])machine.Core.State.A.Clone(); var writes = new List<uint>();
        uint source = 0;
        if (!memory) machine.Core.State.D[src] = ArithmeticSpecification.RegisterBits(value, sourceWidth);
        else
        {
            foreach (var operandBase in new[] { a[src], a[dst] })
                for (var offset = -8; offset <= 4; offset++)
                    machine.InitializePhysical(unchecked(operandBase + (uint)offset), (uint)(0xd0 + offset), 1);
            a[src] = unchecked(a[src] - (sourceWidth == 2 ? 2u : src == 7 ? 2u : 1u));
            machine.InitializePhysical(a[src], value, sourceWidth);
            source = value & MoveSpecification.Mask(sourceWidth);
            a[dst] = unchecked(a[dst] - (destinationWidth == 2 ? 2u : dst == 7 ? 2u : 1u));
            for (var i = 0; i < destinationWidth; i++) writes.Add(unchecked(a[dst] + (uint)(destinationWidth - 1 - i)));
        }
        var expected = SyntheticExecution.Prepare(machine, [opcode, adjustment]);
        if (!memory) source = expected.D[src];
        if (!machine.Model.FullIndex) SyntheticExecution.ExpectException(machine, expected, 4);
        else
        {
            var adjusted = unpack ? (ushort)(((source >> 4 & 15) << 8 | source & 15) + adjustment) : (ushort)(source + adjustment);
            uint result = unpack ? adjusted : (uint)((adjusted >> 8 & 15) << 4 | adjusted & 15);
            if (!memory) expected.D[dst] = (expected.D[dst] & ~MoveSpecification.Mask(destinationWidth)) | result;
            else
            {
                a.CopyTo(expected.A, 0); expected.ExpectedByteOperandAccesses = sourceWidth + destinationWidth;
                for (var i = 0; i < writes.Count; i++) expected.Write(writes[i], result >> (8 * i), 1, machine.Model);
            }
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{family}/packed/r{src}-r{dst}/memory={memory}/super={supervisor}/source-base={sourceBase:X8}/destination-base={destinationBase:X8}/op={opcode:X4}/value={value:X4}/adjust={adjustment:X4}/ccr={ccr:X2}");
    }
}
