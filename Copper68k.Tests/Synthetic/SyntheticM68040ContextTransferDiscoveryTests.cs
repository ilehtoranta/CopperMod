using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Diagnose the retained CP context-transferred-vector requirement. Pending
// delivery is a supplied input, as in SyntheticM68040FpuContinuationTests.
// CopyTaskContextFrom/SwitchTaskContext are emulator APIs, not guest opcodes.
// This does not qualify silicon/FSAVE images, FPU arithmetic or a migration ABI.
public sealed class SyntheticM68040ContextTransferDiscoveryTests(ITestOutputHelper output)
{
    private const string Enable = "COPPER68K_RUN_040_CONTEXT_TRANSFER_DISCOVERY";
    private const uint Target = 0x6000, Vbr = 0x10000, Ea = 0x12345678;

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalPendingDeliveryControls(bool batch) => Audit(batch, false);

    [EnvironmentFact(Enable, "diagnose lost or unrelated CP vectors after public task-context transfer"), Trait("Suite", "ReferenceDiscovery")]
    public void TransferredVectorRequirementScalar() => Audit(false, true);

    [EnvironmentFact(Enable, "diagnose lost or unrelated CP vectors after public task-context transfer"), Trait("Suite", "ReferenceDiscovery")]
    public void TransferredVectorRequirementBatch() => Audit(true, true);

    private void Audit(bool batch, bool transfer)
    {
        var route = batch ? "batch" : "scalar";
        var report = new CoverageBatch("68040", $"cp-context-{(transfer ? "transfer-discovery" : "local-controls")}-{route}");
        foreach (var vector in Enumerable.Range(49, 7))
        foreach (var bank in new[] { "user", "ISP", "MSP" })
        foreach (var destination in transfer ? new[] { "fresh", "unrelated-pending" } : ["local"])
        {
            var source = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
            var (_, frame, sr) = SyntheticM68040AccessFrameAuditTests.Prepare(source, 7, bank, 31, 0, Vbr, Ea, 0x8005);
            // This vector was selected before delivery was suspended. It must
            // not be inferred from mutable FPCR/FPSR or an unrelated event.
            source.Core.State.M68040PendingFpuExceptions.Begin(3, vector, Target);
            foreach (var n in Enumerable.Range(49, 7))
                source.InitializePhysical(Vbr + (uint)n * 4, 0xb000u + (uint)n * 32, 4);
            var machine = source;
            if (transfer)
            {
                var saved = new M68kCpuState();
                saved.CopyTaskContextFrom(source.Core.State);
                machine = new SyntheticMachine(source.Model, source.Bus);
                machine.Core.Reset(SyntheticMachine.Code, 0x4700);
                if (destination == "unrelated-pending")
                    machine.Core.State.M68040PendingFpuExceptions.Begin(3, vector == 55 ? 49 : 55, Target);
                machine.Core.SwitchTaskContext(saved);
            }
            var e = ArchitecturalExpectation.Capture(machine);
            e.ControlChecks["selected vector"] = (s => unchecked((uint)s.LastExceptionVector), (uint)vector);
            e.ControlChecks["one pending delivery"] = (s => s.ExceptionSequence, machine.Core.State.ExceptionSequence + 1);
            e.ControlChecks["original saved PC"] = (s => s.LastExceptionStackedProgramCounter, Target);
            e.ControlChecks["original saved SR"] = (s => s.LastExceptionStatusRegister, sr);
            e.Sr = (ushort)((sr | 0x2000) & ~0xc000);
            e.A[7] = frame + 48; e.InactiveStackPointer = 0x7800;
            if (bank == "MSP") e.MasterStackPointer = frame + 48;
            e.Write(frame + 48, sr, 2, machine.Model);
            e.Write(frame + 50, Target, 4, machine.Model);
            e.Write(frame + 54, (uint)(0x3000 | vector * 4), 2, machine.Model);
            e.Write(frame + 56, Ea, 4, machine.Model);
            e.Pc = 0xb000u + (uint)vector * 32; e.ExceptionVector = vector;
            var id = $"68040/RTE/CP-context/vector={vector}/bank={bank}/destination={destination}/ccr=1F/phase=converted-delivery/op=4E73";
            try
            {
                if (batch)
                {
                    var boundary = new Boundary();
                    var count = ((IM68kBatchCore)machine.Core).ExecuteInstructions(1, machine.Core.State.Cycles + 1000, boundary);
                    if (count != 1 || boundary.Before != 1 || boundary.After != 1)
                        throw new InvalidOperationException($"Batch count/callbacks differ: {count}/{boundary.Before}/{boundary.After}");
                }
                else machine.Core.ExecuteInstruction();
                var mismatch = e.Verify(machine);
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch == null ? null
                    : $"Selected vector expected {vector}, observed {machine.Core.State.LastExceptionVector}; {mismatch}");
            }
            catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
            { report.Record(id, "unsupported", ex.Message); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        // Every requested transfer failure remains a failed gate, not an
        // expected-exception pass or a reference exclusion.
        report.Complete(output);
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
