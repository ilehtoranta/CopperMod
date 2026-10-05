using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.2 and 8.4.6.7: software may repair the preserved frame.
// The handler really executes its stores and RTE; initialization is not repair.
// Supervisor validation tails only. User-tail repair is covered separately by
// SyntheticM68040UserRteFaultTests; internal restoration remains unqualified.
public sealed class SyntheticM68040RteRepairTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020, PendingHandler = 0x9090, Target = 0x6000, Operand = 0x4200;
    private static readonly string[] SupervisorBanks = ["ISP", "MSP"];
    private static readonly string[] Forms = ["format0", "format2", "format3", "invalid4", "invalid15",
        "normal", "CM", "CT", "CU", "CP"];

    [Fact, Trait("Suite", "Synthetic")]
    public void ExecutedRepairRestoresAllCcrAndTraceStates() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void ExecutedRepairRetriesEveryValidationReadAfterCommittedThrowaways() => Audit(true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RepairWithUntouchedIncomingTraceBoundariesScalar() => Audit(false, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RepairWithUntouchedIncomingTraceBoundariesBatch() => Audit(false, true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RepairWithUntouchedIncomingTraceChainedScalar() => Audit(true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RepairWithUntouchedIncomingTraceChainedBatch() => Audit(true, true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void PendingRepairWithPreservedIncomingTraceBoundariesScalar() => Audit(false, true, false, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void PendingRepairWithPreservedIncomingTraceBoundariesBatch() => Audit(false, true, true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void PendingRepairWithPreservedIncomingTraceChainedScalar() => Audit(true, true, false, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void PendingRepairWithPreservedIncomingTraceChainedBatch() => Audit(true, true, true, true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RestoredUserMasterBoundariesScalar() => Audit(false, true, userMaster: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RestoredUserMasterBoundariesBatch() => Audit(false, true, true, userMaster: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RestoredUserMasterChainedScalar() => Audit(true, true, userMaster: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void RestoredUserMasterChainedBatch() => Audit(true, true, true, userMaster: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void OtherCpVectorsPreserveTraceBoundariesScalar() => Audit(false, true, cpVectors: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void OtherCpVectorsPreserveTraceBoundariesBatch() => Audit(false, true, true, cpVectors: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void OtherCpVectorsPreserveTraceChainedScalar() => Audit(true, true, cpVectors: true);

    [Fact, Trait("Suite", "Synthetic")]
    public void OtherCpVectorsPreserveTraceChainedBatch() => Audit(true, true, true, cpVectors: true);

    private void Audit(bool chained, bool keepTrace = false, bool batch = false, bool pendingTrace = false, bool userMaster = false, bool cpVectors = false)
    {
        var bus = new SyntheticM68040RteValidationFaultTests.ValidationFaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var traceGroup = cpVectors ? "rte-cp-vectors" : userMaster ? "rte-user-master" : pendingTrace ? "rte-pending-trace" : "rte-retry-trace";
        var report = new CoverageBatch("68040", keepTrace ? $"{traceGroup}-{(chained ? "chained" : "boundaries")}-{(batch ? "batch" : "scalar")}"
            : chained ? "rte-repair-chained" : "rte-repair-boundaries");
        foreach (var start in SupervisorBanks)
        foreach (var tail in chained ? SupervisorBanks : [start])
        foreach (var middle in chained ? new[] { "none", "ISP", "MSP" } : ["none"])
        foreach (var result in cpVectors ? new[] { "user", "user-M", "ISP", "MSP" } : userMaster ? ["user-M"] : ["user", "ISP", "MSP"])
        foreach (var incoming in chained && !keepTrace ? new ushort[] { 0x8000 } : [0, 0x8000, 0x4000])
        foreach (var restoredTrace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var alignment in chained ? new uint[] { 0, 1 } : [0])
        foreach (var vbr in chained ? new uint[] { 0, 0x10000 } : [0x10000])
        foreach (var ccr in chained ? new[] { 0, 31 } : Enumerable.Range(0, 32))
        foreach (var form in cpVectors ? new[] { "CP50", "CP51", "CP52", "CP53", "CP54", "CP55" }
            : Forms.Where(f => userMaster || (pendingTrace ? f is "CT" or "CU" or "CP" : !keepTrace || f is not ("CT" or "CU" or "CP"))))
        foreach (var (offset, width) in chained ? SyntheticM68040RteValidationFaultTests.Reads(form) : [(0u, 2)])
        for (var faultByte = 0; faultByte < (chained ? width : 1); faultByte++)
        {
            var path = !chained ? new[] { start } : middle == "none" ? [start, tail] : new[] { start, middle, tail };
            Run(m, bus, report, path, result, incoming, restoredTrace, alignment, vbr, ccr, form, offset, width, faultByte, keepTrace, batch, pendingTrace, userMaster, cpVectors);
        }
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, SyntheticM68040RteValidationFaultTests.ValidationFaultBus bus,
        CoverageBatch report, string[] path, string result, ushort incoming, ushort restoredTrace,
        uint alignment, uint vbr, int ccr, string form, uint offset, int width, int faultByte, bool keepTrace, bool batch, bool pendingTrace, bool userMaster, bool cpVectors)
    {
        bus.Disarm(); m.Reset(ccr);
        var pointers = new Dictionary<string, uint> { ["user"] = 0x7800 + alignment, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
        foreach (var at in pointers.Values)
            for (var n = -76; n < 112; n++) m.InitializePhysical(unchecked(at + (uint)n), (uint)(n ^ 0x5a), 1);
        var discarded = new List<uint>();
        for (var n = 0; n < path.Length - 1; n++)
        {
            var at = pointers[path[n]]; discarded.Add(at);
            m.InitializePhysical(at, Status(path[n + 1], incoming, ccr ^ 31), 2);
            m.InitializePhysical(at + 2, 0xdead0001u + (uint)n * 2, 4);
            m.InitializePhysical(at + 6, 0x1024, 2);
            pointers[path[n]] += 8;
        }
        var tail = path[^1]; var frame = pointers[tail]; var accessFrame = frame - 60;
        var format = form.StartsWith("format") ? int.Parse(form[6..]) : form.StartsWith("invalid") ? int.Parse(form[7..]) : 7;
        var repairedFormat = form.StartsWith("invalid") ? 0 : format;
        var post = form.StartsWith("CP", StringComparison.Ordinal);
        var continuation = post ? 0x8000 : form switch { "CM" => 0x1000, "CT" => 0x2000, "CU" => 0x4000, _ => 0 };
        var vector = post ? form == "CP" ? 49 : int.Parse(form[2..]) : form switch { "CT" => 9, "CU" => 11, _ => 0 };
        var savedSr = Status(result, restoredTrace, ccr ^ 31);
        var priorSr = Status(tail, incoming, path.Length > 1 ? ccr ^ 31 : ccr);
        // Every repair changes SR and PC. Invalid formats are repaired to format 0.
        m.InitializePhysical(frame, 0x801fu ^ (uint)ccr, 2);
        m.InitializePhysical(frame + 2, Target + 2, 4);
        m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
        m.InitializePhysical(frame + 8, Operand, 4);
        m.InitializePhysical(frame + 12, (uint)continuation | 0x0105, 2);
        m.InitializePhysical(Target, form == "CM" ? 0x4cd0u : 0x60feu, 2);
        m.InitializePhysical(Target + 2, form == "CM" ? 3u : 0x7e7eu, 2);
        m.InitializePhysical(Target + 4, 0x60fe, 2);
        m.InitializePhysical(Operand, 0x89abcdef, 4); m.InitializePhysical(Operand + 4, 0x12345678, 4);
        m.Core.State.A[0] = 0x4300; // CM must use the saved EA, not this live base.
        // Fixed reference encodings: MOVE.W #imm,(xxx).L; MOVE.L #imm,(xxx).L;
        // MOVE.W #imm,(A7); RTE. Legacy repair explicitly clears saved incoming
        // trace. The retry-trace fixture does not store to the access frame SR.
        ushort[] handler = [0x33fc, savedSr, (ushort)(frame >> 16), (ushort)frame,
            0x23fc, 0, (ushort)Target, (ushort)((frame + 2) >> 16), (ushort)(frame + 2),
            0x33fc, (ushort)((repairedFormat << 12) | 8), (ushort)((frame + 6) >> 16), (ushort)(frame + 6),
            0x3ebc, (ushort)(priorSr & ~0xc000), 0x4e73, 0x7e7e];
        if (keepTrace) handler = [.. handler.Take(13), 0x4e73, 0x7e7e];
        for (var n = 0; n < handler.Length; n++) m.InitializePhysical(Handler + (uint)n * 2, handler[n], 2);
        m.InitializePhysical(PendingHandler, 0x4e73, 2); m.InitializePhysical(PendingHandler + 2, 0x7e7e, 2);
        m.InitializePhysical(vbr + 8, Handler, 4);
        foreach (var v in new[] { 9, 11, 49 }) m.InitializePhysical(vbr + (uint)v * 4, PendingHandler, 4);
        if (cpVectors) m.InitializePhysical(vbr + (uint)vector * 4, PendingHandler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetUserStackPointer(0x7800 + alignment);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = Status(path[0], incoming, ccr); m.Core.State.VectorBaseRegister = vbr;
        if (form == "CU") m.Core.State.M68040PendingFpuExceptions.Begin(2, 11, Target);
        if (post) m.Core.State.M68040PendingFpuExceptions.Begin(3, vector, Target);
        if (cpVectors)
        {
            // The pending vector is an input selected before suspension. These
            // deliberately conflicting registers must not reselect its event.
            m.Core.State.M68040Fpu.Fpcr = 0;
            m.Core.State.M68040Fpu.Fpsr = 0x08008198;
            m.Core.State.M68040Fpu.Fpiar = 0x1234abcd;
        }
        var e = ArchitecturalExpectation.Capture(m); var sequence = m.Core.State.ExceptionSequence;
        if (cpVectors)
        {
            e.ControlChecks["FPCR preserved"] = (s => s.M68040Fpu.Fpcr, 0);
            e.ControlChecks["FPSR preserved"] = (s => s.M68040Fpu.Fpsr, 0x08008198);
            e.ControlChecks["FPIAR preserved"] = (s => s.M68040Fpu.Fpiar, 0x1234abcd);
        }
        e.ControlChecks["fault rejected once"] = (_ => (uint)bus.Rejected.Count, 1);
        e.ControlChecks["rejected original address"] = (_ => bus.Rejected.Count == 1 ? bus.Rejected[0].Address : uint.MaxValue, frame + offset);
        e.ControlChecks["rejected original width"] = (_ => bus.Rejected.Count == 1 ? (uint)bus.Rejected[0].Width : 0, (uint)width);
        e.ControlChecks["discarded PCs never fetched"] = (_ => bus.Accesses.Any(a => a.Address is 0xdead0001 or 0xdead0003) ? 1u : 0u, 0);
        e.ControlChecks["sentinel never executed"] = (s => s.D[7], e.D[7]);
        if (form == "CU" || post) e.ControlChecks["pending delivery"] = (s => s.M68040PendingFpuExceptions.Find(form == "CU" ? 2 : 3) == null ? 0u : 1u, 1);
        pointers[tail] = accessFrame; SetStacks(e, pointers, (ushort)(priorSr & ~0xc000)); e.Pc = Handler; e.ExceptionVector = 2;
        ExpectProvenance(e, sequence + 1, SyntheticMachine.Code, priorSr);
        SyntheticM68040RteValidationFaultTests.ExpectAccessFrame(m, e, accessFrame, priorSr, SyntheticMachine.Code, frame + offset, width);
        bus.Arm(frame + offset + (uint)faultByte);
        var phases = new List<string> { "fault-entry", "repair-SR", "repair-PC", "repair-format" };
        if (!keepTrace) phases.Add("clear-incoming-trace");
        phases.AddRange(["handler-return", "retry-RTE"]);
        if (keepTrace && incoming != 0 && vector == 0) phases.Add("retry-trace-return");
        if (vector != 0) phases.Add("pending-return"); phases.Add("following");
        var id = $"68040/RTE/{(cpVectors ? "cp-vectors" : userMaster ? "user-master" : pendingTrace ? "pending-trace" : keepTrace ? "retry-trace" : "repair")}/{form}/path={string.Join('-', path)}/result={result}/incoming={incoming:X4}/T={restoredTrace:X4}/align={alignment}/VBR={vbr:X8}/read={offset}:{width}/fault-byte={faultByte}/op=4E73/ccr={ccr:X2}";
        var phase = 0;
        bool Step()
        {
            var caseId = id + "/" + phases[phase++];
            var passed = batch ? BatchStep(m, e, report, caseId) : SyntheticM68040AccessFrameAuditTests.Step(m, e, report, caseId);
            if (!passed) while (phase < phases.Count) report.Record(id + "/" + phases[phase++], "untested", "Repair prerequisite failed");
            return passed;
        }
        if (!Step()) return;
        var repairStart = bus.Accesses.Count;
        // Each store has independent flag and memory expectations, exact next PC,
        // unchanged stack/register guards and no new exception.
        foreach (var (at, value, bytes, next) in new[] {
            (frame, (uint)savedSr, 2, Handler + 8), (frame + 2, Target, 4, Handler + 18),
            (frame + 6, (uint)((repairedFormat << 12) | 8), 2, Handler + 26),
            (accessFrame, (uint)(priorSr & ~0xc000), 2, Handler + 30) }.Where(s => !keepTrace || s.Item1 != accessFrame))
        {
            e.Write(at, value, bytes, m.Model); e.Sr = SyntheticExecution.MoveFlags(e.Sr, value, bytes); e.Pc = next;
            if (!Step()) return;
        }
        var repairWrites = bus.Accesses.Skip(repairStart).Where(a => a.Write).ToArray();
        e.ControlChecks["exact repair writes"] = (_ => repairWrites.Length == (keepTrace ? 3 : 4) &&
            repairWrites[0] == new BusAccess(frame, 2, true, savedSr, M68kBusAccessKind.CpuDataWrite) &&
            repairWrites[1] == new BusAccess(frame + 2, 4, true, Target, M68kBusAccessKind.CpuDataWrite) &&
            repairWrites[2] == new BusAccess(frame + 6, 2, true, (uint)((repairedFormat << 12) | 8), M68kBusAccessKind.CpuDataWrite) &&
            (keepTrace || repairWrites[3] == new BusAccess(accessFrame, 2, true, (uint)(priorSr & ~0xc000), M68kBusAccessKind.CpuDataWrite)) ? 1u : 0u, 1);
        var resumeSr = keepTrace ? priorSr : (ushort)(priorSr & ~0xc000);
        pointers[tail] = frame; SetStacks(e, pointers, resumeSr); e.Pc = SyntheticMachine.Code; e.ExceptionVector = null;
        if (!Step()) return;
        var retryStart = bus.Accesses.Count;
        // Only retry itself is constrained here. Later pending frames may occupy
        // memory formerly used by consumed throwaways and may be read legitimately.
        e.ControlChecks["consumed throwaways not read by retry"] = (_ => bus.Accesses.Skip(retryStart).Any(a =>
            !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && discarded.Any(at => unchecked(a.Address - at) < 8)) ? 1u : 0u, 0);
        pointers[tail] += repairedFormat == 7 ? 60u : repairedFormat == 0 ? 8u : 12u;
        SetStacks(e, pointers, savedSr); e.Pc = Target;
        if (vector != 0)
        {
            ExpectException(m, e, pointers, savedSr, vector, Target, Operand, sequence + 2);
            if (form == "CU" || post) e.ControlChecks["pending delivery"] = (s => s.M68040PendingFpuExceptions.Find(form == "CU" ? 2 : 3) == null ? 0u : 1u, 0);
        }
        // MC68040UM 8.2.6: the suspended RTE is traced only once it completes.
        // Both T1 and T0 trace RTE; the repaired frame's SR is saved in that
        // trace frame, independently of the trace bits which caused entry.
        // MC68040UM 8.3: pending CT/CU/CP processing wins; no extra automatic
        // trace of RTE may obscure the converted exception frame. CU/CP trace
        // emulation belongs to software, which sees the repaired saved SR.
        var retryTrace = keepTrace && incoming != 0 && vector == 0;
        if (retryTrace) ExpectException(m, e, pointers, savedSr, 9, Target, SyntheticMachine.Code, sequence + 2);
        if (!Step()) return;
        e.ControlChecks.Remove("consumed throwaways not read by retry");
        e.ControlChecks.Remove("exact repair writes");
        if (retryTrace)
        {
            pointers[ExceptionBank(savedSr)] += 12; SetStacks(e, pointers, savedSr); e.Pc = Target; e.ExceptionVector = null;
            if (!Step()) return;
        }
        if (vector != 0)
        {
            pointers[ExceptionBank(savedSr)] += 12; SetStacks(e, pointers, savedSr); e.Pc = Target; e.ExceptionVector = null;
            if (!Step()) return;
        }
        e.Pc = form == "CM" ? Target + 4 : Target;
        if (form == "CM") { e.D[0] = 0x89abcdef; e.D[1] = 0x12345678; }
        if (restoredTrace == 0x8000 || restoredTrace == 0x4000 && form != "CM")
            ExpectException(m, e, pointers, savedSr, 9, e.Pc, Target, sequence + (vector != 0 ? 3u : retryTrace ? 3u : 2u));
        Step();
    }

    private static ushort Status(string bank, ushort trace, int ccr) => (ushort)((bank == "MSP" ? 0x3000 : bank == "ISP" ? 0x2000 : bank == "user-M" ? 0x1000 : 0) | trace | ccr);
    // MC68040UM 2.2.2.1 / 8.1: user execution uses USP regardless of M.
    // A synchronous exception sets S, preserves M and therefore selects MSP
    // whenever M was set, including the restored S=0,M=1 state.
    private static string ExceptionBank(ushort sr) => (sr & 0x1000) != 0 ? "MSP" : "ISP";
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr)
    {
        e.Sr = sr; e.A[7] = pointers[(sr & 0x2000) == 0 ? "user" : ExceptionBank(sr)];
        e.InactiveStackPointer = pointers[(sr & 0x2000) == 0 ? "ISP" : "user"]; e.MasterStackPointer = pointers["MSP"];
        foreach (var bank in new[] { "user", "ISP", "MSP" })
        {
            var expected = pointers[bank];
            e.ControlChecks[bank] = (s => bank == "user" ? s.UserStackPointer : bank == "MSP" ? s.MasterStackPointer : s.InterruptStackPointer, expected);
        }
    }
    private static void ExpectProvenance(ArchitecturalExpectation e, uint sequence, uint pc, ushort sr)
    {
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, pc);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
    }
    private static void ExpectException(SyntheticMachine m, ArchitecturalExpectation e, Dictionary<string, uint> pointers,
        ushort sr, int vector, uint pc, uint address, uint sequence)
    {
        var bank = ExceptionBank(sr); pointers[bank] -= 12;
        SetStacks(e, pointers, (ushort)((sr | 0x2000) & ~0xc000)); e.Pc = PendingHandler; e.ExceptionVector = vector;
        ExpectProvenance(e, sequence, pc, sr);
        var at = pointers[bank]; e.Write(at, sr, 2, m.Model); e.Write(at + 2, pc, 4, m.Model);
        e.Write(at + 6, (uint)((vector >= 49 ? 3 : 2) << 12 | vector * 4), 2, m.Model); e.Write(at + 8, address, 4, m.Model);
        for (uint n = 0; n < 12; n++) e.MemoryMasks[at + n] = 255;
    }

    private static bool BatchStep(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try
        {
            var boundary = new Boundary();
            var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 1000, boundary);
            var mismatch = count != 1 || boundary.Before != 1 || boundary.After != 1
                ? $"Batch count/callbacks differ: {count}/{boundary.Before}/{boundary.After}" : e.Verify(m);
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            return mismatch == null;
        }
        catch (NotSupportedException ex) { report.Record(id, "unsupported", ex.Message); return false; }
        catch (Exception ex) { report.Record(id, "mismatching", ex.Message); return false; }
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
