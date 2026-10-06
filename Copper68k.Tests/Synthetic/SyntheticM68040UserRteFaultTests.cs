using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 2.2.2.1, 8.1, 8.2.1/5, 8.4.2 and 8.4.6.7 composed:
// throwaways install live SR; an access fault saves it before selecting a
// supervisor stack. This is a manual-derived software qualification, not a
// hardware observation of this unusual user-tail combination. Read ordering
// and synchronous rejection widths retain the emulator's existing policy.
public sealed class SyntheticM68040UserRteFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020, PendingHandler = 0x9090, PrivilegeHandler = 0x9070, Target = 0x6000, Operand = 0x4200;
    private static readonly string[] Forms = ["format0", "format2", "format3", "invalid4", "invalid15", "normal", "CM", "CT", "CU", "CP"];

    [Fact, Trait("Suite", "Synthetic")]
    public void UserTailFaultsAndBareReturnsUseLiveStatusScalar() => Audit(false, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void UserTailFaultsAndBareReturnsUseLiveStatusBatch() => Audit(false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void ExecutedUserTailRepairRebuildsAThrowawayBridgeScalar() => Audit(true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void ExecutedUserTailRepairRebuildsAThrowawayBridgeBatch() => Audit(true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void UserTailBridgePreservesTraceBoundariesScalar() => Audit(true, false, true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void UserTailBridgePreservesTraceBoundariesBatch() => Audit(true, true, true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void UserTailBridgePreservesTraceChainedScalar() => Audit(true, false, true, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void UserTailBridgePreservesTraceChainedBatch() => Audit(true, true, true, true);

    private void Audit(bool repair, bool batch, bool keepTrace = false, bool chained = false)
    {
        var bus = new SyntheticM68040RteValidationFaultTests.ValidationFaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", keepTrace ? $"rte-user-trace-{(chained ? "chained" : "boundaries")}-{(batch ? "batch" : "scalar")}" : $"rte-user-{(repair ? "repair" : "fault")}-{(batch ? "batch" : "scalar")}");
        foreach (var matrix in keepTrace ? new[] { chained ? "structure" : "boundaries" } : ["boundaries", "structure"])
        foreach (var start in new[] { "ISP", "MSP" })
        foreach (var middle in matrix == "structure" ? new[] { "none", "user", "ISP", "MSP" } : ["none"])
        foreach (var userMaster in new[] { false, true })
        foreach (var ccr in matrix == "boundaries" ? Enumerable.Range(0, 32) : new[] { 0, 31 })
        // All incoming trace states have canonical CCR coverage. Structural
        // rejection cases use T1 and independently cross all restored traces.
        foreach (var incoming in keepTrace && !chained || !repair ? new ushort[] { 0, 0x8000, 0x4000 } : [0x8000])
        foreach (var alignment in keepTrace && !chained ? new uint[] { 0 } : [0, 1])
        foreach (var vbr in keepTrace && !chained ? new uint[] { 0x10000 } : [0, 0x10000])
        foreach (var result in keepTrace ? new[] { "user", "user-M", "ISP", "MSP" } : repair && matrix == "boundaries" ? ["user", "ISP", "MSP"] : ["ISP"])
        foreach (var restoredTrace in keepTrace || repair && matrix == "boundaries" ? new ushort[] { 0, 0x8000, 0x4000 } : [0])
        foreach (var form in keepTrace ? Forms.Concat(["CP50", "CP51", "CP52", "CP53", "CP54", "CP55"]) : Forms)
        foreach (var (offset, width) in matrix == "structure" ? SyntheticM68040RteValidationFaultTests.Reads(form) : [(0u, 2)])
        for (var faultByte = 0; faultByte < (matrix == "structure" ? width : 1); faultByte++)
        {
            var path = middle == "none" ? new[] { start, "user" } : [start, middle, "user"];
            Run(m, bus, report, repair, batch, matrix, path, userMaster, ccr, incoming, alignment, vbr,
                result, restoredTrace, form, offset, width, faultByte, keepTrace);
        }
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, SyntheticM68040RteValidationFaultTests.ValidationFaultBus bus,
        CoverageBatch report, bool repair, bool batch, string matrix, string[] path, bool userMaster, int ccr,
        ushort incoming, uint alignment, uint vbr, string result, ushort restoredTrace, string form, uint offset, int width, int faultByte, bool keepTrace)
    {
        bus.Disarm(); m.Reset(ccr);
        var pointers = new Dictionary<string, uint> { ["user"] = 0x7800 + alignment, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
        foreach (var at in pointers.Values)
            for (var n = -76; n < 112; n++) m.InitializePhysical(unchecked(at + (uint)n), (uint)(n ^ 0x5a), 1);
        var discarded = new List<uint>();
        for (var n = 0; n < path.Length - 1; n++)
        {
            var at = pointers[path[n]]; discarded.Add(at);
            m.InitializePhysical(at, Status(path[n + 1], incoming, ccr ^ 31, userMaster), 2);
            m.InitializePhysical(at + 2, 0xdead0001u + (uint)n * 2, 4);
            m.InitializePhysical(at + 6, 0x1024, 2);
            pointers[path[n]] += 8;
        }
        var frame = pointers["user"];
        var priorSr = Status("user", incoming, ccr ^ 31, userMaster);
        var exceptionBank = userMaster ? "MSP" : "ISP";
        var bridge = pointers[exceptionBank]; var accessFrame = bridge - 60;
        var format = form.StartsWith("format") ? int.Parse(form[6..]) : form.StartsWith("invalid") ? int.Parse(form[7..]) : 7;
        var repairedFormat = form.StartsWith("invalid") ? 0 : format;
        var post = form.StartsWith("CP", StringComparison.Ordinal);
        var continuation = post ? 0x8000 : form switch { "CM" => 0x1000, "CT" => 0x2000, "CU" => 0x4000, _ => 0 };
        var vector = post ? form == "CP" ? 49 : int.Parse(form[2..]) : form switch { "CT" => 9, "CU" => 11, _ => 0 };
        var savedSr = Status(result, restoredTrace, ccr ^ 31);
        var handlerSr = (ushort)((priorSr | 0x2000) & ~0xc000);
        var retrySr = keepTrace ? (ushort)(priorSr | 0x2000) : handlerSr;
        m.InitializePhysical(frame, 0x801fu ^ (uint)ccr, 2);
        m.InitializePhysical(frame + 2, Target + 2, 4);
        m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
        m.InitializePhysical(frame + 8, Operand, 4);
        m.InitializePhysical(frame + 12, (uint)continuation | 0x0105, 2);
        m.InitializePhysical(Target, form == "CM" ? 0x4cd0u : 0x60feu, 2);
        m.InitializePhysical(Target + 2, form == "CM" ? 3u : 0x7e7eu, 2);
        m.InitializePhysical(Target + 4, 0x60fe, 2);
        m.InitializePhysical(Operand, 0x89abcdef, 4); m.InitializePhysical(Operand + 4, 0x12345678, 4);
        m.Core.State.A[0] = 0x4300;
        var stores = new List<(uint At, uint Value, int Width)>();
        var handler = new List<ushort>();
        if (repair)
        {
            // Repair the untouched user frame, then create a fresh throwaway on
            // the selected supervisor stack. Patch saved SR explicitly so the
            // handler returns in supervisor mode, able to retry privileged RTE.
            // The preserved-trace route retains incoming trace in both the
            // access return and new bridge. The bridge reselects USP; consumed
            // frames are never replayed and its discarded PC is never fetched.
            stores.AddRange([(frame, savedSr, 2), (frame + 2, Target, 4), (frame + 6, (uint)(repairedFormat << 12 | 8), 2),
                (bridge, keepTrace ? priorSr : (uint)(priorSr & ~0xc000), 2), (bridge + 2, 0xdead0005, 4), (bridge + 6, 0x1024, 2), (accessFrame, retrySr, 2)]);
            foreach (var (at, value, bytes) in stores)
            {
                // Fixed encodings MOVE.W/L #imm,(xxx).L, independent of decoder.
                handler.Add(bytes == 2 ? (ushort)0x33fc : (ushort)0x23fc);
                if (bytes == 4) handler.Add((ushort)(value >> 16));
                handler.Add((ushort)value); handler.Add((ushort)(at >> 16)); handler.Add((ushort)at);
            }
        }
        handler.Add(0x4e73); handler.Add(0x7e7e);
        for (var n = 0; n < handler.Count; n++) m.InitializePhysical(Handler + (uint)n * 2, handler[n], 2);
        m.InitializePhysical(PendingHandler, 0x4e73, 2); m.InitializePhysical(PendingHandler + 2, 0x7e7e, 2);
        m.InitializePhysical(PrivilegeHandler, 0x7e7e, 2);
        m.InitializePhysical(vbr + 8, Handler, 4); m.InitializePhysical(vbr + 32, PrivilegeHandler, 4);
        foreach (var v in new[] { 9, 11, 49 }) m.InitializePhysical(vbr + (uint)v * 4, PendingHandler, 4);
        if (post) m.InitializePhysical(vbr + (uint)vector * 4, PendingHandler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetUserStackPointer(0x7800 + alignment); m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment); m.Core.State.StatusRegister = Status(path[0], incoming, ccr);
        m.Core.State.VectorBaseRegister = vbr;
        if (form == "CU") m.Core.State.M68040PendingFpuExceptions.Begin(2, 11, Target);
        if (post) m.Core.State.M68040PendingFpuExceptions.Begin(3, vector, Target);
        var e = ArchitecturalExpectation.Capture(m); var sequence = m.Core.State.ExceptionSequence;
        e.ControlChecks["one rejection"] = (_ => (uint)bus.Rejected.Count, 1);
        e.ControlChecks["original read address"] = (_ => bus.Rejected.Count == 1 ? bus.Rejected[0].Address : uint.MaxValue, frame + offset);
        e.ControlChecks["original read width"] = (_ => bus.Rejected.Count == 1 ? (uint)bus.Rejected[0].Width : 0, (uint)width);
        e.ControlChecks["discarded PCs not fetched"] = (_ => bus.Accesses.Any(a => a.Address is 0xdead0001 or 0xdead0003 or 0xdead0005) ? 1u : 0u, 0);
        e.ControlChecks["sentinel not executed"] = (s => s.D[7], e.D[7]);
        if (form == "CU" || post) e.ControlChecks["pending delivery"] = (s => s.M68040PendingFpuExceptions.Find(form == "CU" ? 2 : 3) == null ? 0u : 1u, 1);
        pointers[exceptionBank] = accessFrame; SetStacks(e, pointers, handlerSr); e.Pc = Handler; e.ExceptionVector = 2;
        Provenance(e, sequence + 1, SyntheticMachine.Code, priorSr);
        SyntheticM68040RteValidationFaultTests.ExpectAccessFrame(m, e, accessFrame, priorSr, SyntheticMachine.Code, frame + offset, width);
        bus.Arm(frame + offset + (uint)faultByte);
        var phases = repair ? new List<string> { "fault-entry", "repair-SR", "repair-PC", "repair-format", "bridge-SR", "bridge-PC", "bridge-format", "repair-return-SR", "handler-return", "retry-RTE" }
            : new List<string> { "fault-entry", "bare-return", "privilege" };
        var retryTrace = keepTrace && incoming != 0 && vector == 0;
        if (repair) { if (retryTrace) phases.Add("retry-trace-return"); if (vector != 0) phases.Add("pending-return"); phases.Add("following"); }
        var id = $"68040/RTE/user-{(keepTrace ? "trace" : repair ? "repair" : "fault")}/{matrix}/{form}/path={string.Join('-', path)}/M={userMaster}/incoming={incoming:X4}/align={alignment}/VBR={vbr:X8}/result={result}/T={restoredTrace:X4}/read={offset}:{width}/byte={faultByte}/op=4E73/ccr={ccr:X2}";
        var phase = 0;
        bool Step()
        {
            string? mismatch;
            try
            {
                Execute(m.Core, batch); mismatch = e.Verify(m);
                if (mismatch != null) mismatch += $"; savedSR={m.Core.State.LastExceptionStatusRegister:X4}, USP={m.Core.State.UserStackPointer:X8}, ISP={m.Core.State.InterruptStackPointer:X8}, MSP={m.Core.State.MasterStackPointer:X8}";
            }
            catch (NotSupportedException ex) { report.Record(id + "/" + phases[phase++], "unsupported", ex.Message); return Skip(); }
            catch (Exception ex) { mismatch = ex.Message; }
            report.Record(id + "/" + phases[phase++], mismatch == null ? "passing" : "mismatching", mismatch);
            return mismatch == null || Skip();
        }
        bool Skip() { while (phase < phases.Count) report.Record(id + "/" + phases[phase++], "untested", "User-tail prerequisite failed"); return false; }
        if (!Step()) return;
        var repairStart = bus.Accesses.Count; uint handlerPc = Handler;
        foreach (var (at, value, bytes) in stores)
        {
            e.Write(at, value, bytes, m.Model); e.Sr = SyntheticExecution.MoveFlags(e.Sr, value, bytes);
            handlerPc += bytes == 2 ? 8u : 10u; e.Pc = handlerPc;
            if (!Step()) return;
        }
        if (repair)
            e.ControlChecks["seven exact repair stores"] = (_ => bus.Accesses.Skip(repairStart).Where(a => a.Write).SequenceEqual(
                stores.Select(s => new BusAccess(s.At, s.Width, true, s.Value, M68kBusAccessKind.CpuDataWrite))) ? 1u : 0u, 1);
        pointers[exceptionBank] = bridge; SetStacks(e, pointers, repair ? retrySr : priorSr);
        e.Write(accessFrame, repair ? retrySr : priorSr, 2, m.Model); e.Pc = SyntheticMachine.Code; e.ExceptionVector = null;
        if (!Step()) return;
        if (!repair)
        {
            pointers[exceptionBank] -= 8; SetStacks(e, pointers, handlerSr); e.Pc = PrivilegeHandler; e.ExceptionVector = 8;
            Provenance(e, sequence + 2, SyntheticMachine.Code, priorSr);
            e.Write(bridge - 8, priorSr, 2, m.Model); e.Write(bridge - 6, SyntheticMachine.Code, 4, m.Model); e.Write(bridge - 2, 0x0020, 2, m.Model);
            for (uint n = 0; n < 8; n++) e.MemoryMasks[bridge - 8 + n] = 255;
            Step(); return;
        }
        e.ControlChecks.Remove("seven exact repair stores");
        var retryStart = bus.Accesses.Count;
        e.ControlChecks["old throwaways not reread"] = (_ => bus.Accesses.Skip(retryStart).Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead &&
            discarded.Any(at => unchecked(a.Address - at) < 8)) ? 1u : 0u, 0);
        pointers[exceptionBank] += 8; pointers["user"] += repairedFormat == 7 ? 60u : repairedFormat == 0 ? 8u : 12u;
        SetStacks(e, pointers, savedSr); e.Pc = Target;
        if (vector != 0)
        {
            Pending(m, e, pointers, savedSr, vector, Target, Operand, sequence + 2);
            if (form == "CU" || post) e.ControlChecks["pending delivery"] = (s => s.M68040PendingFpuExceptions.Find(form == "CU" ? 2 : 3) == null ? 0u : 1u, 0);
        }
        // The retried instruction begins with incoming trace, even if the
        // repaired SR clears it. CT/CU/CP conversion wins over RTE tracing;
        // no automatic trace may overwrite the converted pending frame.
        if (retryTrace) Pending(m, e, pointers, savedSr, 9, Target, SyntheticMachine.Code, sequence + 2);
        if (!Step()) return;
        e.ControlChecks.Remove("old throwaways not reread");
        if (vector != 0 || retryTrace)
        {
            pointers[ExceptionBank(savedSr)] += 12; SetStacks(e, pointers, savedSr); e.Pc = Target; e.ExceptionVector = null;
            if (!Step()) return;
        }
        e.Pc = form == "CM" ? Target + 4 : Target;
        if (form == "CM") { e.D[0] = 0x89abcdef; e.D[1] = 0x12345678; }
        if (restoredTrace == 0x8000 || restoredTrace == 0x4000 && form != "CM")
            Pending(m, e, pointers, savedSr, 9, e.Pc, Target, sequence + (vector != 0 || retryTrace ? 3u : 2u));
        Step();
    }

    private static ushort Status(string bank, ushort trace, int ccr, bool userMaster = false) =>
        (ushort)((bank == "MSP" ? 0x3000 : bank == "ISP" ? 0x2000 : bank == "user-M" || userMaster ? 0x1000 : 0) | trace | ccr);
    private static string ExceptionBank(ushort sr) => (sr & 0x1000) != 0 ? "MSP" : "ISP";
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr)
    {
        e.Sr = sr; e.A[7] = pointers[(sr & 0x2000) == 0 ? "user" : ExceptionBank(sr)];
        e.InactiveStackPointer = pointers[(sr & 0x2000) == 0 ? "ISP" : "user"]; e.MasterStackPointer = pointers["MSP"];
        foreach (var bank in new[] { "user", "ISP", "MSP" })
        {
            var expected = pointers[bank];
            e.ControlChecks[bank] = (s => bank == "user" ? s.UserStackPointer : bank == "ISP" ? s.InterruptStackPointer : s.MasterStackPointer, expected);
        }
    }
    private static void Provenance(ArchitecturalExpectation e, uint sequence, uint pc, ushort sr)
    {
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, pc);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
    }
    private static void Pending(SyntheticMachine m, ArchitecturalExpectation e, Dictionary<string, uint> pointers,
        ushort sr, int vector, uint pc, uint address, uint sequence)
    {
        var bank = ExceptionBank(sr); pointers[bank] -= 12;
        SetStacks(e, pointers, (ushort)((sr | 0x2000) & ~0xc000)); e.Pc = PendingHandler; e.ExceptionVector = vector;
        Provenance(e, sequence, pc, sr);
        var at = pointers[bank]; e.Write(at, sr, 2, m.Model); e.Write(at + 2, pc, 4, m.Model);
        e.Write(at + 6, (uint)((vector >= 49 ? 3 : 2) << 12 | vector * 4), 2, m.Model); e.Write(at + 8, address, 4, m.Model);
        for (uint n = 0; n < 12; n++) e.MemoryMasks[at + n] = 255;
    }
    private static void Execute(IM68kCore core, bool batch)
    {
        if (!batch) { core.ExecuteInstruction(); return; }
        var boundary = new Boundary();
        var count = ((IM68kBatchCore)core).ExecuteInstructions(1, core.State.Cycles + 1000, boundary);
        if (count != 1 || boundary.Before != 1 || boundary.After != 1)
            throw new InvalidOperationException($"Batch count/callbacks differ: {count}/{boundary.Before}/{boundary.After}");
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
