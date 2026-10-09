using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticNotDisplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void LongNotDisplacementPreservesExtendAndSurroundingMemory(string modelId)
    {
        // M68000PM NOT: complement the selected operand, preserve X,
        // derive N/Z from the result, and clear V/C. Captured old operands
        // and signed displacements are extended across registers and CCRs.
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-not-displacement-captured");
        foreach (var value in new uint[] { 0, 0xffffffff, 0x92345678 })
        foreach (short displacement in new short[] { -8, 8 })
        for (var register = 0; register < 8; register++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            if (register == 7) machine.Core.State.SetActiveStackPointer(0x3000);
            else machine.Core.State.A[register] = 0x3000;
            var opcode = (ushort)(0x46a8 | register);
            if (register == 5) Assert.Equal(0x46ad, opcode);
            var operand = new OperandFixture(machine, [opcode], new(5, register), 4, value,
                source: false, options: new(DestinationDisplacement: displacement));
            Assert.Equal(unchecked((uint)(0x3000 + displacement)), operand.Address);
            Assert.Equal(0x1004u, operand.NextPc);
            var expected = operand.Expected;
            var result = ~value;
            operand.Write(result);
            expected.Sr = (ushort)((expected.Sr & 0xfff0) |
                (result == 0 ? 4 : 0) | ((result & 0x80000000) != 0 ? 8 : 0));
            SyntheticExecution.Run(machine, expected, report,
                $"{modelId}/NOT/4/d16(A{register})/captured/super={supervisor}/op={opcode:X4}/v={value:X8}/disp={displacement}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }
}
