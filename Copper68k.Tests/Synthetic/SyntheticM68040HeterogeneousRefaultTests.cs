using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Reuse the independently specified handler/frame fixture. Combining differing
// slot FCs with repeated faults must preserve the faulted slot's code through
// every nested service and restore the caller's DFC on each explicit RTE.
public sealed class SyntheticM68040HeterogeneousRefaultTests(ITestOutputHelper output)
{
    private const string Enable = "COPPER68K_RUN_040_HETEROGENEOUS_REFAULT";
    private static readonly int[][] Codes = [[1, 1, 5], [1, 5, 1], [1, 5, 5], [5, 1, 1], [5, 1, 5], [5, 5, 1]];
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferingCodesSurviveRepeatedNestedReferenceExamples(bool batch)
    {
        var report = new CoverageBatch("68040", "heterogeneous-refault-reference-" + (batch ? "batch" : "scalar"));
        foreach (var codes in Codes)
        foreach (var bank in Banks)
        foreach (var depth in new[] { 2, 3 })
        for (var slot = 1; slot <= 3; slot++)
            Case(report, batch, [1, 2, 4], 5, 3, codes, bank, slot, (slot == 1 ? 1 : slot == 2 ? 2 : 4) - 1, depth, true);
        report.Complete(output);
    }

    [EnvironmentFact(Enable, "require differing per-slot FCs through repeated nested service"), Trait("Suite", "ReferenceDiscovery")]
    public void DifferingCodesThroughRepeatedNestedFaultsScalar() => Generated(false);

    [EnvironmentFact(Enable, "require differing per-slot FCs through repeated nested service"), Trait("Suite", "ReferenceDiscovery")]
    public void DifferingCodesThroughRepeatedNestedFaultsBatch() => Generated(true);

    private void Generated(bool batch)
    {
        var report = new CoverageBatch("68040", "heterogeneous-refault-" + (batch ? "batch" : "scalar"));
        foreach (var codes in Codes)
        foreach (var bank in Banks)
        foreach (var depth in new[] { 2, 3 })
        foreach (var w1 in new[] { 1, 2, 4 })
        foreach (var w2 in new[] { 1, 2, 4 })
        foreach (var w3 in new[] { 1, 2, 4 })
        {
            int[] widths = [w1, w2, w3];
            for (var ccr = 0; ccr < 32; ccr++)
            {
                if (ccr != 31 && (w1 != 1 || w2 != 2 || w3 != 4)) continue;
                for (uint lane = 0; lane < 4; lane++)
                for (var slot = 1; slot <= 3; slot++)
                for (var faultByte = 0; faultByte < widths[slot - 1]; faultByte++)
                    Case(report, batch, widths, ccr, lane, codes, bank, slot, faultByte, depth, false);
            }
        }
        report.Complete(output);
    }

    private static void Case(CoverageBatch report, bool batch, int[] widths, int ccr, uint lane,
        int[] codes, string bank, int slot, int faultByte, int depth, bool reference)
    {
        var id = $"68040/writeback/heterogeneous-refault{(reference ? "-reference" : "")}/depth={depth}/sizes={string.Join('-', widths)}/FCs={string.Join('-', codes)}/ccr={ccr:X2}/lane={lane}/bank={bank}/WB={slot}/byte={faultByte}";
        try
        {
            SyntheticM68040NestedWritebackFaultTests.RunFunctionCodes(batch, widths, ccr, lane, codes, bank, slot, faultByte, depth);
            report.Record(id, "passing", null);
        }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
    }
}
