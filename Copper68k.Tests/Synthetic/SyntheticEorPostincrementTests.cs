using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticEorPostincrementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void CapturedEorBytePostincrementPreservesSourceAndAdvancesOnce(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-eor-postincrement-captured");
        for (var addressReg = 0; addressReg < 8; addressReg++)
        for (var dataReg = 0; dataReg < 8; dataReg++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            // M68000PM EOR: Dn XOR destination at byte width; preserve X and
            // the full source register, set N/Z, clear V/C. Captured BB XOR
            // 6D = D6 from the historical BF1B regression; A7 byte stride is 2.
            machine.Reset(ccr, supervisor);
            machine.Core.State.D[dataReg] = 0x6a34196d;
            if (addressReg != 7) machine.Core.State.A[addressReg] = 0x2000;
            var address = machine.Core.State.A[addressReg];
            var opcode = (ushort)(0xb118 | dataReg << 9 | addressReg);
            if (dataReg == 7 && addressReg == 3) Assert.Equal(0xbf1b, opcode);
            var operand = new OperandFixture(machine, [opcode], new(3, addressReg), 1, 0xbb, source: false);
            Assert.Equal(address, operand.Address);
            var expected = operand.Expected;
            expected.A[addressReg] = address + (addressReg == 7 ? 2u : 1u);
            expected.Sr = (ushort)((expected.Sr & 0xfff0) | 8);
            operand.Write(0xd6);
            SyntheticExecution.Run(machine, expected, report,
                $"{modelId}/EOR/1/D{dataReg}->(A{addressReg})+/captured/super={supervisor}/op={opcode:X4}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }
}
