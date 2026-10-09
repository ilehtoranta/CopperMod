using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6.2/7: CM restarts after EA calculation and repeats
// all selected operand accesses. The mask/order oracle is test-owned.
public sealed class SyntheticM68040MovemMaskFaultTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_MOVEM_MASK_FAULT_AUDIT";

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedMasksRecoverEveryTransferWithoutLosingRegisterOrder(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-mask-fault-controls-" + Route(batch));
        var masks = new SortedSet<ushort> { 0, 0xffff, 0x00ff, 0xff00, 0x5555, 0xaaaa, 0x0303, 0x8001 };
        for (var bit = 0; bit < 16; bit++)
        {
            masks.Add((ushort)(1 << bit));
            masks.Add((ushort)(0xffff ^ (1 << bit)));
            if (bit < 15) masks.Add((ushort)(3 << bit));
        }
        foreach (var mask in masks)
        foreach (var mode in new[] { 2, 4 })
        foreach (var width in new[] { 2, 4 })
        {
            var count = Enumerable.Range(0, 16).Count(bit => (mask & (1 << bit)) != 0);
            if (count == 0) Run(report, batch, "mask-controls", mode, width, mask, 0, reject: false);
            else for (var transfer = 0; transfer < count; transfer++)
                Run(report, batch, "mask-controls", mode, width, mask, transfer);
        }
        report.Complete(output);
    }

    [EnvironmentFact(Enable, "audit all 65,536 MOVEM mask words with real write faults"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryMaskWordScalar() => Audit(false);

    [EnvironmentFact(Enable, "audit all 65,536 MOVEM mask words with real write faults"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryMaskWordBatch() => Audit(true);

    private void Audit(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-mask-fault-all-" + Route(batch));
        for (var mask = 0; mask <= 0xffff; mask++)
        foreach (var mode in new[] { 2, 4 })
        foreach (var width in new[] { 2, 4 })
        {
            var count = Enumerable.Range(0, 16).Count(bit => (mask & (1 << bit)) != 0);
            Run(report, batch, "all-masks", mode, width, (ushort)mask, Math.Max(0, count - 1), reject: count != 0);
        }
        report.Complete(output);
    }

    private static void Run(CoverageBatch report, bool batch, string cohort, int mode, int width,
        ushort mask, int transfer, bool reject = true)
        => SyntheticM68040MovemWriteFaultDiscoveryTests.Case(report, batch, cohort, new(mode, 2),
            width, transfer, width - 1, "ISP", 0, 31, reject: reject, recover: reject, registerMask: mask);

    private static string Route(bool batch) => batch ? "batch" : "scalar";
}
