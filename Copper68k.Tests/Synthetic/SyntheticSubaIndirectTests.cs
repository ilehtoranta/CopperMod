using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticSubaIndirectTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void IndirectSubaSignExtensionAliasesAndPreservedFlags(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "suba-indirect-consolidation");
        foreach (var width in new[] { 2, 4 })
        foreach (var destination in new[] { 0, 3, 7 })
        foreach (var supervisor in new[] { false, true })
        foreach (var source in width == 2
            ? new uint[] { 0xfffe, 0x8000, 0x7fff, 0 }
            : new uint[] { 2, 0x80000000, 0x7fffffff, 0 })
        for (var ccr = 0; ccr < 32; ccr++)
            SyntheticArithmeticTests.Case(machine, report, "SUBA", width, new(2, 3), source,
                destination == 3 ? 0x3000u : 0x1000u, ccr, reg: destination,
                supervisor: supervisor, scenario: "indirect-sign-alias-all-CCR");
        report.Complete(output);
    }
}
