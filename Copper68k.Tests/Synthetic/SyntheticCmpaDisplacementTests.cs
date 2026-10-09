using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticCmpaDisplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void WordSourceComparesAgainstFullAddressAndPreservesExtend(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "cmpa-displacement-consolidation");
        foreach (var supervisor in new[] { false, true })
        foreach (var destination in new uint[] { 0xffff, uint.MaxValue, 0, 0x7fffffff, 0x80000000 })
        foreach (var source in new uint[] { 0xffff, 1, 0x8000, 0x7fff })
        for (var ccr = 0; ccr < 32; ccr++)
            SyntheticArithmeticTests.Case(machine, report, "CMPA", 2, new(5, 0), source,
                destination, ccr, reg: 1, supervisor: supervisor,
                scenario: "negative-d16-word-source-full-destination-all-CCR",
                options: new(SourceDisplacement: -2));
        report.Complete(output);
    }
}
