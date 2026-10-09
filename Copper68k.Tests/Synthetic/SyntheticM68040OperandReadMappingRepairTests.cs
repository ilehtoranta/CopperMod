using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Persistent physical-map fixture; software must execute the controller store
// before RTE can restart the first indirect read. This is not an enabled MMU.
public sealed class SyntheticM68040OperandReadMappingRepairTests(ITestOutputHelper output)
{
    private const string Enable = "COPPER68K_RUN_040_OPERAND_READ_MAPPING_REPAIR";

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void HandlerReallyRepairsPersistentMappingBeforeReturning(bool batch) => Audit(batch, false);

    [EnvironmentFact(Enable, "require executed persistent mapping repair for all first-read CCR/lane/byte cases"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryCcrLaneAndFaultByteScalar() => Audit(false, true);

    [EnvironmentFact(Enable, "require executed persistent mapping repair for all first-read CCR/lane/byte cases"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryCcrLaneAndFaultByteBatch() => Audit(true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void PersistentMappingAndLiteralRepairProgramHaveFixedReferenceExamples()
    {
        var bus = new SyntheticM68040OperandReadFaultDiscoveryTests.MappingRepairBus(0x4201);
        IM68kPhysicalAddressMap mapping = bus;
        Assert.False(mapping.IsCpuPhysicalAddressMapped(0x4200, 2, M68kBusAccessKind.CpuDataRead));
        Assert.False(mapping.IsCpuPhysicalAddressMapped(0x4200, 2, M68kBusAccessKind.CpuDataRead));
        Assert.Equal(2, bus.Rejected.Count); // A second request must remain denied.
        Assert.True(mapping.IsCpuPhysicalAddressMapped(0x41fe, 2, M68kBusAccessKind.CpuDataRead));
        Assert.True(mapping.IsCpuPhysicalAddressMapped(0x4200, 2, M68kBusAccessKind.CpuInstructionFetch));
        bus.Initialize(0x4500, 1, 2);
        Assert.True(mapping.IsCpuPhysicalAddressMapped(0x4200, 2, M68kBusAccessKind.CpuDataRead));
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040")); m.Reset();
        SyntheticM68040OperandReadFaultDiscoveryTests.InitializeMappingRepair(m, 0xa000);
        ushort[] expected = [0x33fc, 1, 0, 0x4500, 0x4e73, 0x4e71];
        for (var n = 0; n < expected.Length; n++) Assert.Equal((uint)expected[n], m.PeekPhysical(0xa000 + (uint)n * 2, 2));
    }

    private void Audit(bool batch, bool generated)
    {
        var cohort = generated ? "mapping-matrix" : "mapping-reference";
        var report = new CoverageBatch("68040", $"operand-read-{cohort}-{(batch ? "batch" : "scalar")}");
        foreach (var instruction in SyntheticM68040OperandReadFaultDiscoveryTests.Instructions)
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var ccr in generated ? Enumerable.Range(0, 32) : [31])
        foreach (var lane in generated ? Enumerable.Range(0, 4) : [0])
        foreach (var faultByte in generated ? Enumerable.Range(0, instruction.Width) : [instruction.Width - 1])
            SyntheticM68040OperandReadFaultDiscoveryTests.Case(report, batch, instruction, bank, 0, ccr, lane, faultByte,
                recover: true, cohort: cohort, repairMapping: true);
        report.Complete(output);
    }
}
