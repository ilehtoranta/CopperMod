using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Supplied suspended integer-delivery context and emulator task APIs.
// Saved CCR is the complement of the incoming CCR, so a stale current SR
// cannot satisfy restoration checks. No incoming trace or silicon ABI claim.
public sealed class SyntheticM68040ContextTransferCcrTests(ITestOutputHelper output)
{
    private const string Enable = "COPPER68K_RUN_040_CONTEXT_TRANSFER_CCR";

    [EnvironmentFact(Enable, "require all CCR images through local and transferred suspended delivery"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarAllCcrControlsAndTransfer() => Audit(false);

    [EnvironmentFact(Enable, "require all CCR images through local and transferred suspended delivery"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchAllCcrControlsAndTransfer() => Audit(true);

    private void Audit(bool batch)
    {
        var fixture = new SyntheticM68040ContextTransferDiscoveryTests(output);
        var failures = new List<Exception>();
        foreach (var transfer in new[] { false, true })
        {
            try { fixture.Audit(batch, transfer, allCcr: true); }
            catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(ex => ex.Message)));
    }
}
