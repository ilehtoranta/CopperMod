using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6.2/7: restart a rejected first indirect operand read after
// explicit handler RTE. No earlier operand effects, trace epoch, enabled MMU,
// cache/pipeline behavior or partially accepted physical transfer is qualified.
public sealed class SyntheticM68040OperandReadRecoveryTests(ITestOutputHelper output)
{
    private const string Enable = "COPPER68K_RUN_040_OPERAND_READ_RECOVERY";

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstReadFaultCompletesAfterExplicitHandlerReturn(bool batch) => Audit(batch, false);

    [EnvironmentFact(Enable, "require completed first-read fault recovery across CCRs, lanes and fault bytes"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryCcrLaneAndFaultByteScalar() => Audit(false, true);

    [EnvironmentFact(Enable, "require completed first-read fault recovery across CCRs, lanes and fault bytes"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryCcrLaneAndFaultByteBatch() => Audit(true, true);

    private void Audit(bool batch, bool generated)
    {
        var cohort = generated ? "matrix" : "reference";
        var report = new CoverageBatch("68040", $"operand-read-recovery-{cohort}-{(batch ? "batch" : "scalar")}");
        foreach (var instruction in SyntheticM68040OperandReadFaultDiscoveryTests.Instructions)
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var ccr in generated ? Enumerable.Range(0, 32) : [31])
        foreach (var lane in generated ? Enumerable.Range(0, 4) : [0])
        foreach (var faultByte in generated ? Enumerable.Range(0, instruction.Width) : [instruction.Width - 1])
            SyntheticM68040OperandReadFaultDiscoveryTests.Case(report, batch, instruction, bank, 0, ccr, lane, faultByte,
                recover: true, cohort: cohort);
        report.Complete(output);
    }
}
