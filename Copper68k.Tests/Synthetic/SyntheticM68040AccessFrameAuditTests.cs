using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Qualified ordinary-CI paths plus the remaining explicit discovery gate.
// MC68040UM 8.4.6.2/7 is the expectation source, not WinUAE's non-MMU RTE.
public sealed class SyntheticM68040AccessFrameAuditTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_ACCESS_FRAME_AUDIT";
    private static readonly ushort[] TraceStates = [0, 0x8000, 0x4000];
    private static readonly ushort[] AccessStates = [0, 0x0105, 0x0505, 0x0905];
    private static readonly string[] Banks = ["user", "ISP", "MSP"];
    private const uint Target = 0x6000, TraceHandler = 0x9090;

    [Fact, Trait("Suite", "Synthetic")]
    public void ExistingShortFramesValidateTheRestorationFixture() => Audit("rte-access-controls", [0, 2, 3], false);

    [Fact, Trait("Suite", "Synthetic")]
    public void AccessFrameWithoutContinuationRestoresAndTracesFollowingInstruction() => Audit("rte-access-normal", [7], false);

    [Fact, Trait("Suite", "Synthetic")]
    public void PendingTraceConvertsTheFrameBeforeReturningAndResuming() => Audit("rte-access-trace", [7], true);

    [EnvironmentFact(Enable, "audit complete 040 format-7 restoration prerequisites"), Trait("Suite", "ReferenceDiscovery")]
    public void RemainingFaultProtocolsCannotDisappearFromTheGate()
    {
        var report = new CoverageBatch("68040", "rte-access-remaining-protocols");
        // Preserve the unqualified protocols explicitly after promoting CU/CP.
        foreach (var bank in Banks)
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var form in new[] { "frame-validation-fault", "odd-PC-chained-SR-provenance",
            "real-access-fault-entry", "writeback-handler", "CP-context-transferred-vector" })
            report.Record($"68040/RTE/format7/{form}/bank={bank}/op=4E73/ccr={ccr:X2}", "untested",
                form == "frame-validation-fault"
                    ? "Supervisor/user-tail validation, normal/CM and pending CT/CU/CP preserved-trace retry, and executed supervisor/user-tail pending software trace service have separate software coverage; internal restoration and mixed-epoch trace protocols remain required. Physical/hardware behavior is separately unqualified"
                    : form == "real-access-fault-entry"
                        ? "Cache-disabled accurate instruction-fetch frames/restart are covered separately; data fault/writeback restart, enabled-cache deferral and compiled instruction-PC provenance remain required"
                    : "Required fault/context protocol has no independently qualified execution fixture yet");
        report.Complete(output);
    }

    private void Audit(string group, int[] formats, bool pendingTrace)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var report = new CoverageBatch("68040", group);
        foreach (var format in formats)
        foreach (var bank in Banks)
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var trace in TraceStates)
        foreach (var vbr in new[] { 0u, 0x10000u })
        foreach (var ea in new[] { 0x5ffau, 0x12345678u })
        foreach (var access in format == 7 ? AccessStates : new ushort[] { 0 })
        {
            var (e, frame, savedSr) = Prepare(m, format, bank, ccr, trace, vbr, ea,
                (ushort)(access | (pendingTrace ? 0x2000 : 0)));
            var id = $"68040/RTE/format{format}/{(pendingTrace ? "CT" : "normal")}/bank={bank}/T={trace:X4}/VBR={vbr:X8}/EA={ea:X8}/SSW={access:X4}/op=4E73/ccr={ccr:X2}";
            var sequence = m.Core.State.ExceptionSequence;
            var end = frame + (format == 7 ? 60u : format == 0 ? 8u : 12u);
            Restore(e, bank, end, savedSr);
            e.Pc = Target;
            if (pendingTrace) ExpectTrace(m, e, bank, frame + 48, savedSr, ea, sequence + 1);
            else e.ControlChecks["no exception during RTE"] = (s => s.ExceptionSequence, sequence);
            if (!Step(m, e, report, id + "/RTE"))
            {
                if (pendingTrace) report.Record(id + "/return", "untested", "RTE prerequisite failed");
                report.Record(id + "/following-BRA", "untested", "RTE prerequisite failed");
                continue;
            }
            if (pendingTrace)
            {
                // Return from the pending trace, using the newly converted frame.
                Restore(e, bank, end, savedSr);
                e.Pc = Target; e.ExceptionVector = null;
                e.ControlChecks["no exception during RTE"] = (s => s.ExceptionSequence, sequence + 1);
                if (!Step(m, e, report, id + "/return"))
                {
                    report.Record(id + "/following-BRA", "untested", "Pending trace return prerequisite failed");
                    continue;
                }
            }
            e.ControlChecks.Remove("no exception during RTE");
            if (trace != 0) ExpectTrace(m, e, bank, end - 12, savedSr, Target, sequence + (pendingTrace ? 2u : 1u));
            else e.ControlChecks["no following exception"] = (s => s.ExceptionSequence, sequence + (pendingTrace ? 1u : 0u));
            Step(m, e, report, id + "/following-BRA");
        }
        report.Complete(output);
    }

    internal static (ArchitecturalExpectation Expected, uint Frame, ushort SavedSr) Prepare(
        SyntheticMachine m, int format, string bank, int ccr, ushort trace, uint vbr, uint ea, ushort ssw)
    {
        m.Reset(ccr);
        var frame = bank == "MSP" ? 0x7400u : 0x4700u;
        // Guard the whole frame and both neighbors. WB status/data are populated
        // even when not used: RTE must not replay the handler's pending writebacks.
        for (var i = -16; i < 80; i++) m.InitializePhysical(unchecked(frame + (uint)i), (uint)(0x5a ^ i), 1);
        var savedSr = SavedStatus(bank, trace, ccr);
        m.InitializePhysical(frame, savedSr, 2);
        m.InitializePhysical(frame + 2, Target, 4);
        m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
        m.InitializePhysical(frame + 8, ea, 4);
        m.InitializePhysical(frame + 12, ssw, 2);
        foreach (var offset in new[] { 14u, 16u, 18u }) m.InitializePhysical(frame + offset, 0x85, 2);
        foreach (var offset in new[] { 24u, 32u, 40u })
        {
            m.InitializePhysical(frame + offset, 0x4200 + offset, 4);
            m.InitializePhysical(frame + offset + 4, 0xc3a55a3c, 4);
            m.InitializePhysical(0x4200 + offset, 0x96abcdef, 4);
        }
        m.InitializePhysical(Target, 0x60fe, 2); // self BRA; discriminates restored T0/T1.
        m.InitializePhysical(Target + 2, 0x4e71, 2);
        m.InitializePhysical(TraceHandler, 0x4e73, 2);
        m.InitializePhysical(TraceHandler + 2, 0x4e71, 2);
        m.InitializePhysical(vbr + 9 * 4, TraceHandler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.VectorBaseRegister = vbr;
        m.Core.State.SetMasterStackPointer(0x7400);
        if (bank == "MSP") m.Core.State.StatusRegister |= 0x1000;
        var e = ArchitecturalExpectation.Capture(m);
        e.OperandAccessAddresses.UnionWith(new uint[] { 0x4218, 0x4220, 0x4228 });
        e.ExpectedOperandTransfers = []; // Pending writebacks belong to the handler.
        return (e, frame, savedSr);
    }

    internal static ushort SavedStatus(string bank, ushort trace, int ccr) =>
        (ushort)((bank switch { "user" => 0, "ISP" => 0x2000, "MSP" => 0x3000, _ => throw new ArgumentException("Unknown stack bank") }) | trace | (ccr ^ 31));

    internal static void Restore(ArchitecturalExpectation e, string bank, uint stack, ushort sr)
    {
        e.Sr = sr;
        e.A[7] = bank == "user" ? 0x7800u : stack;
        e.InactiveStackPointer = bank == "user" ? stack : 0x7800u;
        if (bank == "MSP") e.MasterStackPointer = stack;
    }

    private static void ExpectTrace(SyntheticMachine m, ArchitecturalExpectation e, string bank,
        uint stack, ushort sr, uint tracedInstruction, uint sequence)
    {
        e.ControlChecks.Remove("no exception during RTE");
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, Target);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
        e.Sr = (ushort)((sr | 0x2000) & ~0xc000);
        e.A[7] = stack;
        e.InactiveStackPointer = 0x7800;
        if (bank == "MSP") e.MasterStackPointer = stack;
        e.Write(stack, sr, 2, m.Model);
        e.Write(stack + 2, Target, 4, m.Model);
        e.Write(stack + 6, 0x2024, 2, m.Model);
        e.Write(stack + 8, tracedInstruction, 4, m.Model);
        e.Pc = TraceHandler; e.ExceptionVector = 9;
    }

    internal static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try
        {
            m.Core.ExecuteInstruction();
            var mismatch = e.Verify(m);
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            return mismatch == null;
        }
        catch (Exception ex)
        {
            report.Record(id, ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException
                ? "unsupported" : "mismatching", ex.Message);
            return false;
        }
    }
}

public sealed class M68040AccessFrameFixtureTests
{
    [Theory]
    [InlineData("user", 0, 0, 0x001f)]
    [InlineData("user", 0x8000, 31, 0x8000)]
    [InlineData("ISP", 0, 31, 0x2000)]
    [InlineData("ISP", 0x4000, 0, 0x601f)]
    [InlineData("MSP", 0x8000, 0, 0xb01f)]
    [InlineData("MSP", 0x4000, 31, 0x7000)]
    public void SavedStatusHasIndependentFixedReferenceExamples(string bank, int trace, int ccr, int expected) =>
        Assert.Equal((ushort)expected, SyntheticM68040AccessFrameAuditTests.SavedStatus(bank, (ushort)trace, ccr));
}
