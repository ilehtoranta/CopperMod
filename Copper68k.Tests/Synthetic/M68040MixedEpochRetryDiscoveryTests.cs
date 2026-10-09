using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Discovery, not a promoted architectural qualification. MC68040UM 8.2.6
// gives the original-instruction trace rule, but its interaction with committed
// throwaways and a fresh validation retry still needs independent qualification.
// Keep both differing directions visible; never use current CPU behavior as
// the expected answer or add a private continuation latch to make this pass.
public sealed class M68040MixedEpochRetryDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_MIXED_RETRY_DISCOVERY";
    private static readonly ushort[] Traces = [0, 0x8000, 0x4000];

    [EnvironmentFact(Enable, "qualify original trace across mixed-epoch RTE repair/retry"), Trait("Suite", "ReferenceDiscovery")]
    public void OriginalTraceDeferralScalar() => Audit(false);

    [EnvironmentFact(Enable, "qualify original trace across mixed-epoch RTE repair/retry"), Trait("Suite", "ReferenceDiscovery")]
    public void OriginalTraceDeferralBatch() => Audit(true);

    private void Audit(bool batch)
    {
        var bus = new SyntheticM68040RteValidationFaultTests.ValidationFaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", $"rte-mixed-retry-discovery-{(batch ? "batch" : "scalar")}");
        foreach (var start in new[] { "ISP", "MSP" })
        foreach (var tail in new[] { "ISP", "MSP" })
        foreach (var middle in new[] { "none", "user", "user-M", "ISP", "MSP" })
        foreach (var incoming in Traces)
        foreach (var first in Traces)
        foreach (var second in middle == "none" ? new ushort[] { 0 } : Traces)
        foreach (var result in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var restored in Traces)
        foreach (var ccr in new[] { 0, 31 })
        foreach (var form in new[] { "format0", "normal", "CM" })
        {
            var path = middle == "none" ? new[] { start, tail } : [start, middle, tail];
            SyntheticM68040RteRepairTests.Run(m, bus, report, path, result, incoming, restored,
                0, 0x10000, ccr, form, 2, 4, 0, keepTrace: true, batch: batch,
                pendingTrace: false, userMaster: false, cpVectors: false, traceService: false,
                clearServiceTrace: false, throwawayTraces: (first, second));
        }
        report.Complete(output);
    }
}
