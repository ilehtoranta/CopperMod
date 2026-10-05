using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.1 figure 8-1 / 7.6.3: handler-entry prefetch belongs
// to exception processing; a later fault in executing handler code does not.
// Discovery remains failing until the full required entry window is modeled.
public sealed class SyntheticM68040HandlerPrefetchTests(ITestOutputHelper output)
{
    private const string Enable = "COPPER68K_RUN_040_ACCESS_FRAME_AUDIT";
    private const uint Handler = 0x9020;

    [EnvironmentFact(Enable, "qualify the complete 040 handler-entry prefetch window"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryRequiredScalarEntryPrefetchByteHaltsBeforeHandlerExecution() => Audit("scalar", true);

    [EnvironmentFact(Enable, "qualify the complete 040 handler-entry prefetch window"), Trait("Suite", "ReferenceDiscovery")]
    public void EveryRequiredBatchEntryPrefetchByteHaltsBeforeHandlerExecution() => Audit("batch", true);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData("scalar")]
    [InlineData("batch")]
    public void FetchFaultAfterHandlerExecutionStartsANewException(string route) => Audit(route, false);

    private void Audit(string route, bool entry)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", $"handler-prefetch-{(entry ? "entry" : "executing")}-{route}");
        foreach (var form in new[] { "opcode", "extension", "RTE" })
        foreach (var bank in form == "RTE" ? new[] { "ISP", "MSP" } : ["user", "ISP", "MSP"])
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var handlerOffset in entry ? new uint[] { 0, 2, 4, 6 } : [0])
        for (var faultByte = 0; faultByte < (entry ? 16 : 4); faultByte++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68040/access-error/handler-prefetch/{form}/route={route}/bank={bank}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/handler={handlerOffset}/byte={faultByte}/op={(form == "RTE" ? 0x4e73 : form == "extension" ? 0x203c : 0x7002):X4}/ccr={ccr:X2}";
            try { Run(m, bus, route, entry, form, bank, trace, alignment, vbr, handlerOffset, faultByte, ccr); report.Record(id, "passing", null); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, SyntheticM68040AccessDoubleFaultTests.FaultBus bus,
        string route, bool entry, string form, string bank, ushort trace, uint alignment, uint vbr,
        uint handlerOffset, int faultByte, int ccr)
    {
        bus.Disarm(); m.Reset(ccr);
        var sr = (ushort)((bank == "MSP" ? 0x3000 : bank == "ISP" ? 0x2000 : 0) | trace | ccr);
        var stack = (bank == "MSP" ? 0x7400u : 0x4700u) + alignment;
        var frame = stack - 60;
        foreach (var at in new uint[] { 0x4700 + alignment, 0x7400 + alignment, 0x7800 + alignment })
            for (var n = -140; n < 100; n++) m.InitializePhysical(unchecked(at + (uint)n), (uint)(n ^ 0x5a), 1);
        m.InitializePhysical(stack, 0x201f, 2); m.InitializePhysical(stack + 2, 0x6000, 4);
        m.InitializePhysical(stack + 6, 8, 2);
        for (uint n = 0; n < 24; n += 2) m.InitializePhysical(Handler + n, 0x4e71, 2);
        m.InitializePhysical(Handler + 4, entry ? 0x4e71u : 0x7002u, 2);
        m.InitializePhysical(Handler + handlerOffset, 0x7201, 2); // MOVEQ #1,D1
        m.InitializePhysical(vbr + 8, Handler + handlerOffset, 4);
        _ = SyntheticExecution.Prepare(m, form == "RTE" ? [0x4e73] : form == "extension" ? [0x203c, 0x1234, 0x5678] : [0x7002]);
        var s = m.Core.State;
        s.SetUserStackPointer(0x7800 + alignment); s.SetInterruptStackPointer(0x4700 + alignment);
        s.SetMasterStackPointer(0x7400 + alignment); s.StatusRegister = sr; s.VectorBaseRegister = vbr;
        var before = ArchitecturalExpectation.Capture(m); var sequence = s.ExceptionSequence;
        var firstAddress = form == "RTE" ? stack : form == "extension" ? 0x1004u : 0x1000u;
        bus.Arm(firstAddress, form == "RTE" ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuInstructionFetch);
        if (entry) bus.Arm(Handler + (uint)faultByte, M68kBusAccessKind.CpuInstructionFetch);
        if (route == "batch")
        {
            var denied = new DeniedBoundary(); var accesses = bus.Accesses.Count;
            var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, s.Cycles + 1000, denied);
            if (count != 0 || denied.Before != 1 || denied.After != 0 || bus.Rejected.Count != 0 ||
                bus.Accesses.Count != accesses || before.Verify(m) is { })
                throw new InvalidOperationException("Denied cold batch performed a speculative CPU fetch before its boundary");
        }
        Execute(m.Core, route);
        if (bus.Rejected.Count == 0 || bus.Rejected[0].Address != firstAddress || s.ExceptionSequence != sequence + 1)
            throw new InvalidOperationException("Original access-error entry prerequisite differs");
        if (entry)
        {
            if (!s.Halted || s.Stopped || bus.Rejected.Count != 2)
                throw new InvalidOperationException($"Handler-entry prefetch must halt before execution: halted={s.Halted}, rejections={bus.Rejected.Count}");
            var rejected = bus.Rejected[1];
            if (rejected.Kind != M68kBusAccessKind.CpuInstructionFetch || rejected.Width != 4 ||
                rejected.Address != (Handler + (uint)faultByte & ~3u) || bus.Accesses.Count != rejected.AccessCount)
                throw new InvalidOperationException("Wrong entry prefetch or transfer after fatal rejection");
            if (!s.D.SequenceEqual(before.D) || !s.A.Take(7).SequenceEqual(before.A.Take(7)))
                throw new InvalidOperationException("Handler instruction or unrelated register changed during entry");
            var frozen = ArchitecturalExpectation.Capture(m); frozen.Halted = true;
            var accesses = bus.Accesses.Count;
            m.Core.RequestInterrupt(7, 31 * 4); m.Core.BeginSubroutine(0x6000, 0x8000, 0x7000);
            m.Core.SwitchTaskContext(new M68kCpuState()); Execute(m.Core, route, halted: true);
            var mismatch = frozen.Verify(m);
            if (mismatch != null || bus.Accesses.Count != accesses || s.ExceptionSequence != sequence + 1)
                throw new InvalidOperationException("Host entry resumed fatal state: " + mismatch);
            bus.Disarm(); m.InitializePhysical(0x6000, 0x747b, 2); m.Core.Reset(0x6000, 0x8000);
            Execute(m.Core, route);
            if (s.Halted || s.Stopped || s.D[2] != 123 || s.ProgramCounter != 0x6002)
                throw new InvalidOperationException("External reset did not resume the halted CPU");
            return;
        }
        if (s.Halted || s.ProgramCounter != Handler || s.A[7] != frame)
            throw new InvalidOperationException("Access-error entry did not reach the handler");
        if (!s.D.SequenceEqual(before.D) || !s.A.Take(7).SequenceEqual(before.A.Take(7)))
            throw new InvalidOperationException("Faulted instruction or handler prefix executed during entry");
        Execute(m.Core, route); Execute(m.Core, route); // MOVEQ; NOP before the next long.
        var handlerSr = (ushort)(((sr | 0x2000) & ~0xc000 & ~15) | 0);
        if (s.ProgramCounter != Handler + 4 || s.D[1] != 1 || s.StatusRegister != handlerSr || s.A[7] != frame)
            throw new InvalidOperationException("Executed handler prefix prerequisite differs");
        var e = ArchitecturalExpectation.Capture(m);
        var nested = frame - 60; e.A[7] = nested; if (bank == "MSP") e.MasterStackPointer = nested;
        e.Pc = Handler; e.ExceptionVector = 2;
        e.ControlChecks["nested exception"] = (x => x.ExceptionSequence, sequence + 2);
        e.ControlChecks["handler PC saved"] = (x => x.LastExceptionStackedProgramCounter, Handler + 4);
        e.ControlChecks["handler SR saved"] = (x => x.LastExceptionStatusRegister, handlerSr);
        e.ControlChecks["exactly two rejections"] = (_ => (uint)bus.Rejected.Count, 2);
        SyntheticM68040RteValidationFaultTests.ExpectAccessFrame(m, e, nested, handlerSr, Handler + 4, Handler + 4, 4);
        e.Write(nested + 12, 0x0106, 2, m.Model); // Instruction TM, not data TM.
        bus.Arm(Handler + 4 + (uint)faultByte, M68kBusAccessKind.CpuInstructionFetch);
        Execute(m.Core, route);
        var error = e.Verify(m);
        if (error != null) throw new InvalidOperationException(error);
    }

    private static void Execute(IM68kCore core, string route, bool halted = false)
    {
        if (route == "scalar") core.ExecuteInstruction();
        else if (((IM68kBatchCore)core).ExecuteInstructions(1, core.State.Cycles + 1000, new Boundary()) != (halted ? 0 : 1))
            throw new InvalidOperationException("Wrong batch instruction count");
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
    }

    private sealed class DeniedBoundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return false; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
