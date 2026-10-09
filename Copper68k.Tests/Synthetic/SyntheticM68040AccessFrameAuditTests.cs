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
                    ? "Validation, preserved-trace repair/software service, successful mixed-epoch chains and mixed-epoch validation fault capture have separate software coverage; mixed-epoch repair/retry, original trace suspension/resumption and internal restoration remain required. Physical/hardware behavior is separately unqualified"
                    : form == "real-access-fault-entry"
                        ? "Cache-disabled accurate instruction-fetch restart, actual legal MOVE memory-destination faults, scoped normal-space MOVES read/store faults and five legal MOVE16 operand-fault forms have separate software coverage. MOVE16 write recovery uses an explicit saved-line handler/RTE; its read TT and WB1 validity remain disputed. Other integer/read/MOVEM faults, wider operand/repair/refault combinations, enabled-cache deferral and compiled fault pipelines remain required"
                    : form == "odd-PC-chained-SR-provenance"
                        ? "Chained format0/2/3 and format7 normal/CM SR handoff, stack consumption and header reads have separate generated WinUAE software coverage; access-frame validation/no-replay checks are synthetic. Separate composed-entry and executed-MMU-entry CM lifetime discovery profiles disagree after odd-PC fault/repair; the executed profile also exposes stacked-SR disagreement. Neither qualifies the full run loop or hardware. CT/CU/CP pending/foreign-context chains, full reference exception qualification and hardware behavior remain unqualified"
                    : form == "writeback-handler"
                        ? "Executed supplied normal B/W/L handler covers validity, WB1 lane alignment, WB1/2/3 order, overlap, DFC and restored trace/stacks. Actual nested MOVES-store fault/handler/RTE coverage includes all 27 B/W/L width triples at all 32 CCRs, common FC1/5 and supplied user/user-M/ISP/MSP outer frames, with separate frozen execution evidence for each promoted slice. Differing per-slot FC1/5 has separate structural-width and canonical-CCR coverage for all four banks. Repeated faults of the same pending store at depths two/three have separate structural-width and canonical-CCR coverage with common FC1/5 and explicit frame unwinds. Original construction of all slots, wider FC/refault combinations and trace/interrupt interruption remain required. Five legal MOVE16 forms have explicit saved-line completion coverage; cache-push lines and other data-fault construction remain required. Physical function-code spaces, MOVE16 WB1 validity and hardware timing are unqualified"
                        : "Public task-context copy/switch has separately qualified failed production discovery: fresh destinations lose the suspended vector and unrelated destinations reuse a stale event. A private two-file snapshot candidate passes selected vectors49-55 across all32 initial CCRs, user/ISP/MSP, local/fresh/unrelated destinations and scalar/batch routes with zero incoming trace, including full-suite/reference/API and retained standard-consumer source linkage. The candidate is not imported; native consumer boot, foreign frames, wider trace/fault/context protocols and independent hardware expectations remain unqualified. These bounded fixtures do not complete this broader requirement");
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
