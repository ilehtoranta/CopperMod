using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticMoveaDisplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void CapturedWordMoveaReadsAliasedDisplacementBeforeSignExtending(string modelId)
    {
        // M68000PM MOVEA: sign extend W to the complete address register,
        // preserve CCR, and evaluate the source before replacing its base.
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "movea-displacement-captured");
        foreach (var value in new uint[] { 0x8001, 0x7fff, 0 })
        foreach (short displacement in new short[] { -8, 8 })
        for (var register = 0; register < 8; register++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var opcode = (ushort)(0x3068 | register << 9 | register);
            if (register == 1) Assert.Equal(0x3269, opcode);
            var fixture = new MoveFixture(machine, opcode, value, ccr, supervisor,
                scenario: $"captured-displacement/super={supervisor}/disp={displacement}",
                options: new(SourceDisplacement: displacement),
                customize: m => {
                    if (register == 7) m.Core.State.SetActiveStackPointer(0x3000);
                    else m.Core.State.A[register] = 0x3000;
                });
            SyntheticMoveTests.Run(machine, report, fixture);
            Assert.Equal(0x1004u, fixture.NextPc);
            Assert.Equal(value, fixture.OperandValue);
        }
        report.Complete(output);
    }
}
