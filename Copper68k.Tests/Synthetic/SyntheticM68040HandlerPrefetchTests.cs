using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.1 figure 8-1 / 7.6.3: handler-entry prefetch belongs
// to exception processing; a later fault in executing handler code does not.
public sealed class SyntheticM68040HandlerPrefetchTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020;

    [Fact, Trait("Suite", "Synthetic")]
    public void EveryRequiredScalarEntryPrefetchByteHaltsBeforeHandlerExecution() => Audit("scalar", true);

    [Fact, Trait("Suite", "Synthetic")]
    public void EveryRequiredBatchEntryPrefetchByteHaltsBeforeHandlerExecution() => Audit("batch", true);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData("scalar")]
    [InlineData("batch")]
    public void FetchFaultAfterHandlerExecutionStartsANewException(string route) => Audit(route, false);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData("scalar")]
    [InlineData("batch")]
    public void OddHandlerAddressHaltsEntryWithoutFetchingOrStackingAgain(string route) => Audit(route, true, odd: true);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData("scalar")]
    [InlineData("batch")]
    public void EntryFetchDataIsRetainedUntilConsumedOrItsContextChanges(string route)
    {
        var bus = new StableFaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", $"handler-prefetch-retention-{route}");
        foreach (var mode in new[] { "linear", "branch", "subroutine", "task", "map" })
        foreach (var bank in new[] { "user", "ISP", "MSP" })
        foreach (var offset in new uint[] { 0, 2, 4, 6 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68040/access-error/handler-retention/{mode}/route={route}/bank={bank}/handler={offset}/align={alignment}/VBR={vbr:X8}/op=7002/ccr={ccr:X2}";
            try
            {
                bus.Disarm(); m.Reset(ccr);
                for (uint n = 0; n < 16; n += 2) m.InitializePhysical(Handler + n, 0x7401 + n / 2, 2);
                if (mode == "branch") m.InitializePhysical(Handler + offset, 0x6002, 2);
                m.InitializePhysical(Handler + 16, 0x7455, 2);
                m.InitializePhysical(vbr + 8, Handler + offset, 4);
                _ = SyntheticExecution.Prepare(m, [0x7002]);
                var s = m.Core.State;
                s.SetUserStackPointer(0x7800 + alignment); s.SetInterruptStackPointer(0x4700 + alignment);
                s.SetMasterStackPointer(0x7400 + alignment);
                s.StatusRegister = (ushort)((bank == "MSP" ? 0x3000 : bank == "ISP" ? 0x2000 : 0) | ccr);
                s.VectorBaseRegister = vbr;
                bus.Arm(0x1000, M68kBusAccessKind.CpuInstructionFetch);
                Execute(m.Core, route);
                if (s.Halted || s.ProgramCounter != Handler + offset)
                    throw new InvalidOperationException($"Retention entry prerequisite differs: PC={s.ProgramCounter:X8}, halted={s.Halted}, rejected={bus.Rejected.Count}");
                RequireCompleteWindow(bus);
                // Deliberate guest-visible code replacement proves the CPU
                // retains fetched values, rather than re-reading the window.
                for (uint n = 0; n < 16; n += 2) m.InitializePhysical(Handler + n, 0x747f, 2);
                var e = ArchitecturalExpectation.Capture(m);
                var accesses = bus.Accesses.Count;
                if (mode == "linear")
                {
                    for (var n = offset; n < 16; n += 2)
                    {
                        e.Pc = Handler + n + 2; e.D[2] = 1 + n / 2;
                        e.Sr = (ushort)(s.StatusRegister & ~15);
                        Execute(m.Core, route);
                        if (e.Verify(m) is { } error) throw new InvalidOperationException("Retained entry value differs: " + error);
                    }
                    if (bus.Accesses.Count != accesses)
                        throw new InvalidOperationException("Consumed entry window was fetched twice");
                    e.Pc = Handler + 18; e.D[2] = 0x55;
                }
                else if (mode == "branch")
                {
                    Execute(m.Core, route);
                    if (s.ProgramCounter != Handler + offset + 4 || bus.Accesses.Count != accesses)
                        throw new InvalidOperationException("Retained branch prerequisite differs");
                    e.Pc = Handler + offset + 6; e.D[2] = 127; e.Sr = (ushort)(e.Sr & ~15);
                }
                else
                {
                    if (mode == "subroutine") m.Core.BeginSubroutine(Handler + offset, s.A[7], 0x6000);
                    if (mode == "task")
                    {
                        var task = new M68kCpuState(); task.CopyTaskContextFrom(s);
                        m.Core.SwitchTaskContext(task);
                    }
                    if (mode == "map") bus.ChangeMap();
                    e = ArchitecturalExpectation.Capture(m);
                    e.Pc = Handler + offset + 2; e.D[2] = 127; e.Sr = (ushort)(e.Sr & ~15);
                }
                Execute(m.Core, route);
                if (e.Verify(m) is { } mismatch) throw new InvalidOperationException("Entry context invalidation differs: " + mismatch);
                report.Record(id, "passing", null);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    private static void RequireCompleteWindow(SyntheticM68040AccessDoubleFaultTests.FaultBus bus)
    {
        var fetches = bus.Accesses.Where(a => a.Kind == M68kBusAccessKind.CpuInstructionFetch &&
            unchecked(a.Address - Handler) < 16).ToArray();
        if (fetches.Length != 4 || fetches.Any(a => a.Write || a.Width != 4) ||
            !fetches.Select(a => a.Address).SequenceEqual(new uint[] { Handler, Handler + 4, Handler + 8, Handler + 12 }))
            throw new InvalidOperationException("Entry did not fetch exactly four aligned longs before execution: " +
                string.Join(",", fetches.Select(a => $"{a.Address:X8}:{a.Width}")));
    }

    private sealed class StableFaultBus : SyntheticM68040AccessDoubleFaultTests.FaultBus, IM68kStablePhysicalAddressMap
    {
        public uint CpuPhysicalAddressMapGeneration { get; private set; }
        public void ChangeMap() => CpuPhysicalAddressMapGeneration++;
        public new void Disarm() { base.Disarm(); ChangeMap(); }
        public new void Arm(uint at, M68kBusAccessKind kind) { base.Arm(at, kind); ChangeMap(); }
        public new bool IsCpuPhysicalAddressMapped(uint address, int count, M68kBusAccessKind kind)
        {
            if (count > 4) return false; // Page probes are queries, not CPU accesses.
            var mapped = base.IsCpuPhysicalAddressMapped(address, count, kind);
            if (!mapped) ChangeMap(); // The one-shot hole has just disappeared.
            return mapped;
        }
    }

    private void Audit(string route, bool entry, bool odd = false)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", $"handler-prefetch-{(odd ? "odd" : entry ? "entry" : "executing")}-{route}");
        foreach (var form in new[] { "opcode", "extension", "RTE" })
        foreach (var bank in form == "RTE" ? new[] { "ISP", "MSP" } : ["user", "ISP", "MSP"])
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var handlerOffset in odd ? new uint[] { 1, 3, 5, 7 } : entry ? [0, 2, 4, 6] : [0])
        for (var faultByte = odd ? -1 : 0; faultByte < (odd ? 0 : entry ? 16 : 4); faultByte++)
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
        for (uint n = 0; n < 32; n += 2) m.InitializePhysical(Handler + n, 0x4e71, 2);
        m.InitializePhysical(Handler + 16, entry ? 0x4e71u : 0x7002u, 2);
        m.InitializePhysical(Handler + handlerOffset, 0x7201, 2); // MOVEQ #1,D1
        m.InitializePhysical(vbr + 8, Handler + handlerOffset, 4);
        _ = SyntheticExecution.Prepare(m, form == "RTE" ? [0x4e73] : form == "extension" ? [0x203c, 0x1234, 0x5678] : [0x7002]);
        var s = m.Core.State;
        s.SetUserStackPointer(0x7800 + alignment); s.SetInterruptStackPointer(0x4700 + alignment);
        s.SetMasterStackPointer(0x7400 + alignment); s.StatusRegister = sr; s.VectorBaseRegister = vbr;
        var before = ArchitecturalExpectation.Capture(m); var sequence = s.ExceptionSequence;
        var firstAddress = form == "RTE" ? stack : form == "extension" ? 0x1004u : 0x1000u;
        bus.Arm(firstAddress, form == "RTE" ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuInstructionFetch);
        if (entry && faultByte >= 0) bus.Arm(Handler + (uint)faultByte, M68kBusAccessKind.CpuInstructionFetch);
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
            var odd = faultByte < 0;
            if (!s.Halted || s.Stopped || bus.Rejected.Count != (odd ? 1 : 2))
                throw new InvalidOperationException($"Handler-entry prefetch must halt before execution: halted={s.Halted}, rejections={bus.Rejected.Count}");
            if (odd)
            {
                if (bus.Accesses.Any(a => a.Kind == M68kBusAccessKind.CpuInstructionFetch && unchecked(a.Address - Handler) < 16))
                    throw new InvalidOperationException("Odd entry performed a handler fetch");
            }
            else
            {
                var rejected = bus.Rejected[1];
                if (rejected.Kind != M68kBusAccessKind.CpuInstructionFetch || rejected.Width != 4 ||
                    rejected.Address != (Handler + (uint)faultByte & ~3u) || bus.Accesses.Count != rejected.AccessCount)
                    throw new InvalidOperationException("Wrong entry prefetch or transfer after fatal rejection");
            }
            if (!s.D.SequenceEqual(before.D) || !s.A.Take(7).SequenceEqual(before.A.Take(7)))
                throw new InvalidOperationException("Handler instruction or unrelated register changed during entry");
            before.Sr = (ushort)((sr | 0x2000) & ~0xc000); before.A[7] = frame;
            before.InactiveStackPointer = 0x7800 + alignment;
            if (bank == "MSP") before.MasterStackPointer = frame;
            before.Pc = Handler + handlerOffset; before.Halted = true; before.ExceptionVector = 2;
            before.ControlChecks["saved instruction PC"] = (x => x.LastExceptionStackedProgramCounter, 0x1000);
            before.ControlChecks["saved SR"] = (x => x.LastExceptionStatusRegister, sr);
            before.ControlChecks["single exception"] = (x => x.ExceptionSequence, sequence + 1);
            before.ControlChecks["translation bypass restored"] = (x => x.M68040Mmu.BypassTranslation ? 1u : 0u, 0);
            SyntheticM68040RteValidationFaultTests.ExpectAccessFrame(m, before, frame, sr, 0x1000, firstAddress, form == "RTE" ? 2 : 4);
            if (form != "RTE") before.Write(frame + 12, bank == "user" ? 0x0102u : 0x0106u, 2, m.Model);
            if (before.Verify(m) is { } frameError) throw new InvalidOperationException("Fatal entry frame or untouched memory differs: " + frameError);
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
        RequireCompleteWindow(bus);
        if (!s.D.SequenceEqual(before.D) || !s.A.Take(7).SequenceEqual(before.A.Take(7)))
            throw new InvalidOperationException("Faulted instruction or handler prefix executed during entry");
        // These eight words were fetched during entry. A later demand fetch
        // is outside that window and belongs to handler execution.
        for (var n = 0; n < 8; n++) Execute(m.Core, route);
        var handlerSr = (ushort)(((sr | 0x2000) & ~0xc000 & ~15) | 0);
        if (s.ProgramCounter != Handler + 16 || s.D[1] != 1 || s.StatusRegister != handlerSr || s.A[7] != frame)
            throw new InvalidOperationException("Executed handler prefix prerequisite differs");
        var e = ArchitecturalExpectation.Capture(m);
        var nested = frame - 60; e.A[7] = nested; if (bank == "MSP") e.MasterStackPointer = nested;
        e.Pc = Handler; e.ExceptionVector = 2;
        e.ControlChecks["nested exception"] = (x => x.ExceptionSequence, sequence + 2);
        e.ControlChecks["handler PC saved"] = (x => x.LastExceptionStackedProgramCounter, Handler + 16);
        e.ControlChecks["handler SR saved"] = (x => x.LastExceptionStatusRegister, handlerSr);
        e.ControlChecks["exactly two rejections"] = (_ => (uint)bus.Rejected.Count, 2);
        SyntheticM68040RteValidationFaultTests.ExpectAccessFrame(m, e, nested, handlerSr, Handler + 16, Handler + 16, 4);
        e.Write(nested + 12, 0x0106, 2, m.Model); // Instruction TM, not data TM.
        bus.Arm(Handler + 16 + (uint)faultByte, M68kBusAccessKind.CpuInstructionFetch);
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
