using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticAndiDisplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void CapturedLongAndiDisplacementConsumesImmediateAndPreservesExtend(string modelId)
    {
        // M68000PM ANDI: preserve X, set N/Z from the selected-width result,
        // and clear V/C. Literal captured masks and displacements are extended
        // across every address register, both stack banks and all CCR images.
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-andi-displacement-captured");
        foreach (var mask in new uint[] { 0, 0x80123456, 0x7fffffff })
        foreach (short displacement in new short[] { -8, 8 })
        for (var register = 0; register < 8; register++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            if (register == 7) machine.Core.State.SetActiveStackPointer(0x3000);
            else machine.Core.State.A[register] = 0x3000;
            var opcode = (ushort)(0x02a8 | register);
            if (register == 1) Assert.Equal(0x02a9, opcode);
            var operand = new OperandFixture(machine, [opcode, (ushort)(mask >> 16), (ushort)mask],
                new(5, register), 4, uint.MaxValue, source: false,
                options: new(DestinationDisplacement: displacement));
            Assert.Equal(unchecked((uint)(0x3000 + displacement)), operand.Address);
            Assert.Equal(0x1008u, operand.NextPc);
            var expected = operand.Expected;
            operand.Write(mask); // FFFFFFFF AND the captured mask.
            expected.Sr = (ushort)((expected.Sr & 0xfff0) |
                (mask == 0 ? 4 : 0) | ((mask & 0x80000000) != 0 ? 8 : 0));
            SyntheticExecution.Run(machine, expected, report,
                $"{modelId}/ANDI/4/d16(A{register})/captured/super={supervisor}/op={opcode:X4}/mask={mask:X8}/disp={displacement}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }
}
