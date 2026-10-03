using System.Numerics;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticMultiplyDivideTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    private static readonly string[] Families = ["MULU", "MULS", "DIVU", "DIVS"];

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void MultiplyDivideBoundariesAndFlags(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-muldiv-boundaries");
        foreach (var family in Families)
        foreach (var width in new[] { 2, 4 })
        foreach (var wide in width == 2 ? new[] { false } : new[] { false, true })
        foreach (var source in ArithmeticSpecification.Boundaries(width))
        foreach (var destination in ArithmeticSpecification.Boundaries(4).Concat(new uint[] { 0x10000, 0xffff0000, 0x12345678 }))
        for (var ccr = 0; ccr < 32; ccr++)
            Case(machine, report, family, width, wide, new(0, 0), source, destination, destination, ccr);
        foreach (var family in Families)
        foreach (var wide in new[] { false, true })
        foreach (var tuple in new[] { (Source: 0xffffffffu, Low: 0u, High: 0x80000000u), (Source: 1u, Low: 0u, High: 1u), (Source: 3u, Low: 0xfffffff9u, High: 0xffffffffu) })
            Case(machine, report, family, 4, wide, new(0, 0), tuple.Source, tuple.Low, tuple.High, 31);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void MultiplyDivideAddressingAndRegisterAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "arithmetic-muldiv-addressing");
        foreach (var family in Families)
        foreach (var width in new[] { 2, 4 })
        foreach (var wide in width == 2 ? new[] { false } : new[] { false, true })
        {
            foreach (var form in ArithmeticSpecification.Sources(width, address: false))
            foreach (var supervisor in new[] { false, true })
                Case(machine, report, family, width, wide, form, 0xfffe, 0x12345, 0, 31, supervisor: supervisor);
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures())
            foreach (var form in new[] { new OperandForm(6, 0), new OperandForm(7, 3) })
                Case(machine, report, family, width, wide, form, 2, 0x12345, 0, 31, index: index);
            for (var src = 0; src < 8; src++)
            for (var q = 0; q < 8; q++)
            foreach (var r in width == 2 ? new[] { q } : Enumerable.Range(0, 8))
            {
                // M68000PM MULx Dh=Dl with a 64-bit product is undefined.
                if (wide && family.StartsWith("MUL") && q == r) continue;
                Case(machine, report, family, width, wide, new(0, src), 0xfffffffe, 0x80000000, 0xffffffff, 31, q: q, r: r);
            }
            foreach (var supervisor in new[] { false, true })
            foreach (var form in new[] { new OperandForm(0, 0), new OperandForm(3, 7), new OperandForm(4, 7), new OperandForm(7, 4) })
                Case(machine, report, family, width, wide, form, 0, 0x80000000, 0x80000000, 31, supervisor: supervisor, returnFromTrap: true);
        }
        report.Complete(output);
    }

    private static void Case(SyntheticMachine machine, CoverageBatch report, string family, int width, bool wide, OperandForm form,
        uint source, uint destination, uint high, int ccr, int q = 1, int r = 2, IndexFixture? index = null, bool supervisor = true, bool returnFromTrap = false)
    {
        machine.Reset(ccr, supervisor);
        if (returnFromTrap) machine.Bus.Initialize(0x9050, 0x4e73, 2);
        var multiply = family.StartsWith("MUL"); var signed = family.EndsWith('S');
        var opcode = width == 2 ? (ushort)((multiply ? 0xc0c0 : 0x80c0) | (signed ? 0x100 : 0) | q << 9 | form.Mode << 3 | form.Register) :
            (ushort)((multiply ? 0x4c00 : 0x4c40) | form.Mode << 3 | form.Register);
        var extension = (ushort)(q << 12 | (signed ? 0x800 : 0) | (wide ? 0x400 : 0) | r);
        machine.Core.State.D[r] = high; machine.Core.State.D[q] = destination;
        var fixture = new OperandFixture(machine, width == 2 ? [opcode] : [opcode, extension], form, width, source, index: index);
        var expected = fixture.Expected;
        var nextPc = expected.Pc;
        var unavailable = width == 4 && !machine.Model.FullIndex;
        var removed = wide && machine.Model.Id == "68060";
        if (unavailable || removed)
        {
            // These exceptions precede any EA effects or operand read.
            machine.Core.State.A.CopyTo(expected.A, 0);
            if (form.Memory)
                for (var offset = 0; offset < width; offset++) expected.ForbiddenOperandReads.Add(machine.Model.Physical(unchecked(fixture.Address + (uint)offset)));
            SyntheticExecution.ExpectException(machine, expected, unavailable ? 4 : 61);
        }
        else
        {
            var s = signed ? new BigInteger(ArithmeticSpecification.Signed(fixture.Value, width)) : new BigInteger(fixture.Value);
            var low = expected.D[q]; var upper = expected.D[r];
            if (multiply)
            {
                var d = signed ? new BigInteger(ArithmeticSpecification.Signed(low, width)) : new BigInteger(low & MoveSpecification.Mask(width));
                var product = s * d;
                var result = (uint)(product & uint.MaxValue);
                var overflow = width == 4 && !wide && (signed ? product < int.MinValue || product > int.MaxValue : product > uint.MaxValue);
                if (wide) expected.D[r] = (uint)((product >> 32) & uint.MaxValue);
                expected.D[q] = result;
                // Explicit full-product flags for 64-bit MUL, truncated result for 32-bit MUL.
                expected.Sr = (ushort)((expected.Sr & 0xfff0) | (wide ? (product & (BigInteger.One << 63)) != 0 ? 8 : 0 : (result & 0x80000000) != 0 ? 8 : 0) |
                    ((wide ? product.IsZero : result == 0) ? 4 : 0) | (overflow ? 2 : 0));
            }
            else
            {
                expected.Sr &= 0xfffe; // C is defined clear, including overflow and divide-by-zero.
                if (s.IsZero)
                {
                    expected.DefinedSrMask = 0xfff1;
                    SyntheticExecution.ExpectException(machine, expected, 5, nextPc);
                }
                else
                {
                    BigInteger dividend = wide ? ((BigInteger)upper << 32) | low : low;
                    if (signed) dividend = wide && (upper & 0x80000000) != 0 ? dividend - (BigInteger.One << 64) : !wide ? ArithmeticSpecification.Signed(low, 4) : dividend;
                    var quotient = dividend / s; var remainder = dividend % s;
                    var bits = width == 2 ? 16 : 32;
                    var min = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
                    var max = signed ? (BigInteger.One << (bits - 1)) - 1 : (BigInteger.One << bits) - 1;
                    if (quotient < min || quotient > max) { expected.Sr |= 2; expected.DefinedSrMask = 0xfff3; }
                    else
                    {
                        var quotientBits = (uint)(quotient & uint.MaxValue);
                        var remainderBits = (uint)(remainder & uint.MaxValue);
                        if (width == 2) expected.D[q] = (remainderBits & 0xffff) << 16 | quotientBits & 0xffff;
                        else { if (r != q) expected.D[r] = remainderBits; expected.D[q] = quotientBits; }
                        expected.Sr = SyntheticExecution.MoveFlags(expected.Sr, quotientBits, width);
                    }
                }
            }
        }
        var id = $"{machine.Model.Id}/{family}.{(width == 2 ? "word" : wide ? "long64" : "long32")}/{form.Id}/q{q}-r{r}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/s={source:X8}/d={destination:X8}/high={high:X8}/ccr={ccr:X2}";
        SyntheticExecution.Run(machine, expected, report, id);
        if (returnFromTrap && expected.ExceptionVector == 5)
        {
            // Test a real return from the defined divide frame in both stack modes.
            var userStack = expected.InactiveStackPointer;
            expected.A[7] += machine.Model.FullIndex ? 12u : machine.Model.Id == "68000" ? 6u : 8u;
            expected.Sr = (ushort)((supervisor ? 0x2700 : 0x0700) | (ccr & ~1));
            if (!supervisor) { expected.InactiveStackPointer = expected.A[7]; expected.A[7] = userStack!.Value; }
            expected.Pc = nextPc; expected.ExceptionVector = null;
            SyntheticExecution.Run(machine, expected, report, id + "/RTE");
        }
    }
}
