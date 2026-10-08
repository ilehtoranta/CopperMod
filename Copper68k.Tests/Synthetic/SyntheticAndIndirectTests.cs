using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticAndIndirectTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void AndIndirectRetainsSelectedWidthRegistersAndFlags(string modelId)
    {
        // Captured C012/C052/C092 operands from the old EC020 regression,
        // extended to every source/address register, CCR and stack state.
        // M68000PM AND: sized bitwise conjunction, X unchanged, N/Z from the
        // selected result, V/C clear. No production arithmetic/EA helpers.
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-and-indirect-captured");
        foreach (var width in new[] { 1, 2, 4 })
        for (var addressReg = 0; addressReg < 8; addressReg++)
        for (var dataReg = 0; dataReg < 8; dataReg++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            var initial = width == 1 ? 0xffff00ffu : uint.MaxValue;
            machine.Core.State.D[dataReg] = initial;
            var opcode = (ushort)(0xc010 | dataReg << 9 | ArithmeticSpecification.SizeField(width) << 6 | addressReg);
            if (dataReg == 0 && addressReg == 2)
                Assert.Equal(width == 1 ? 0xc012 : width == 2 ? 0xc052 : 0xc092, opcode);
            var value = width == 1 ? 0xf0u : width == 2 ? 0xf0a5u : 0xf0a55a0fu;
            var operand = new OperandFixture(machine, [opcode], new(2, addressReg), width, value);
            var expected = operand.Expected;
            var result = width == 1 ? 0xf0u : width == 2 ? 0xf0a5u : 0xf0a55a0fu;
            expected.D[dataReg] = (initial & ~MoveSpecification.Mask(width)) | result;
            expected.Sr = (ushort)((expected.Sr & 0xfff0) | 8); // X preserved; N=1, Z/V/C=0.
            SyntheticExecution.Run(machine, expected, report,
                $"{modelId}/AND/{width}/(A{addressReg})/D{dataReg}/super={supervisor}/ccr={ccr:X2}/op={opcode:X4}");
        }
        report.Complete(output);
    }
}
