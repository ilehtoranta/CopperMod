using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68040MovemWriteRecoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_MOVEM_WRITE_RECOVERY";

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCanonicalStoreFormRunsHandlerAndResumes(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-write-recovery-witness-" + Route(batch));
        foreach (var mode in new[] { 2, 4, 5, 6, 7 })
        for (var register = 0; register < (mode == 7 ? 2 : 8); register++)
        foreach (var width in new[] { 2, 4 })
            SyntheticM68040MovemWriteFaultDiscoveryTests.Case(report, batch, "recovery-witness", new(mode, register),
                width, 3, width - 1, "ISP", 0, 31, recover: true);
        report.Complete(output);
    }

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfOverwrittenIndirectPointerIsNotResolvedAgain(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-write-pointer-alias-" + Route(batch));
        foreach (var width in new[] { 2, 4 })
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
            SyntheticM68040MovemWriteFaultDiscoveryTests.Case(report, batch, "pointer-alias", new(6, 0),
                width, 3, width - 1, bank, trace, 31, recover: true,
                index: new(Full: true, IndexRegister: 1, LongIndex: true, BaseSize: 2, Indirect: 1), overwritePointer: true);
        report.Complete(output);
    }

    [EnvironmentFact(Enable, "qualify actual MOVEM write faults, WB1 handler and RTE resumption"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarRecoveryMatrix() => Audit(false);

    [EnvironmentFact(Enable, "qualify actual MOVEM write faults, WB1 handler and RTE resumption"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchRecoveryMatrix() => Audit(true);

    private static string Route(bool batch) => batch ? "batch" : "scalar";
    private void Audit(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-write-recovery-" + Route(batch));
        foreach (var mode in new[] { 2, 4, 5, 6, 7 })
        for (var register = 0; register < (mode == 7 ? 2 : 8); register++)
            Cases("recovery-opcode", new(mode, register), "ISP", 0, 31);
        foreach (var form in new[] { new OperandForm(2, 0), new(4, 0), new(4, 7), new(5, 0), new(6, 0), new(7, 0), new(7, 1) })
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        for (var ccr = 0; ccr < 32; ccr++) Cases("recovery-status", form, bank, trace, ccr);
        foreach (var spec in IndexFixture.FullStructures())
        foreach (var index in new[] {
            spec with { IndexRegister = 1, LongIndex = false, Scale = 0 },
            spec with { IndexRegister = 1, LongIndex = true, Scale = 0 },
            spec with { IndexRegister = 1, LongIndex = false, Scale = 3 },
            spec with { AddressIndex = true, IndexRegister = 0, LongIndex = false, Scale = 2 } })
        foreach (var width in new[] { 2, 4 })
        for (var transfer = 0; transfer < 4; transfer++)
        for (var faultByte = 0; faultByte < width; faultByte++)
            SyntheticM68040MovemWriteFaultDiscoveryTests.Case(report, batch, "recovery-structure", new(6, 1),
                width, transfer, faultByte, "ISP", 0, 31, recover: true, index: index);
        report.Complete(output);

        void Cases(string cohort, OperandForm form, string bank, ushort trace, int ccr)
        {
            foreach (var width in new[] { 2, 4 })
            for (var transfer = 0; transfer < 4; transfer++)
            for (var faultByte = 0; faultByte < width; faultByte++)
                SyntheticM68040MovemWriteFaultDiscoveryTests.Case(report, batch, cohort, form,
                    width, transfer, faultByte, bank, trace, ccr, recover: true);
        }
    }
}
