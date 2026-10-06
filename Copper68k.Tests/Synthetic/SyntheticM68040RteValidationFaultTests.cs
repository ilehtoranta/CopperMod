using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6.7: validation faults preserve the incomplete frame.
// Physical map rejection exercises the real logical bus with translation off.
// This does not inject a production exception or calculate an EA with CPU code.
public sealed class SyntheticM68040RteValidationFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020, Target = 0x6000, PrivilegeHandler = 0x9070;
    private static readonly string[] SupervisorBanks = ["ISP", "MSP"];
    private static readonly string[] Forms = ["format0", "format2", "format3", "invalid4", "invalid15",
        "normal", "CM", "CT", "CU", "CP"];

    [Fact, Trait("Suite", "Synthetic")]
    public void DirectValidationFaultsPreserveEveryCcrAndOriginalFrame() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void ValidationFaultsRetainCommittedThrowawaysAndStackSelection() => Audit(true);

    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochFaultCaptureCanonicalScalar() => AuditMixed(false, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochFaultCaptureCanonicalBatch() => AuditMixed(false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochFaultCaptureStructuralScalar() => AuditMixed(true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochFaultCaptureStructuralBatch() => AuditMixed(true, true);

    private readonly record struct Epochs(ushort Incoming, ushort First, ushort Second, ushort Final, string Matrix);
    private static readonly ushort[] Traces = [0, 0x8000, 0x4000];
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];

    private void AuditMixed(bool structural, bool batch)
    {
        var bus = new ValidationFaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var matrix = structural ? "structure" : "canonical";
        var report = new CoverageBatch("68040", $"rte-mixed-fault-{matrix}-{(batch ? "batch" : "scalar")}");
        foreach (var start in SupervisorBanks)
        foreach (var tail in Banks)
        foreach (var middle in structural ? new[] { "none", "user", "user-M", "ISP", "MSP" } : ["none"])
        foreach (var incoming in Traces)
        foreach (var first in Traces)
        foreach (var second in middle == "none" ? new ushort[] { 0 } : Traces)
        // Canonical cases cross every final trace independently. Structural
        // cases hold the uncommitted final SR at T1 and cross every read byte.
        foreach (var final in structural ? new ushort[] { 0x8000 } : Traces)
        foreach (var alignment in structural ? new uint[] { 0, 1 } : [0])
        foreach (var vbr in structural ? new uint[] { 0, 0x10000 } : [0x10000])
        foreach (var form in new[] { "format0", "format2", "format3", "invalid4", "invalid15", "normal", "CM", "CT", "CU",
            "CP49", "CP50", "CP51", "CP52", "CP53", "CP54", "CP55" })
        foreach (var ccr in structural ? new[] { 0, 31 } : Enumerable.Range(0, 32))
        foreach (var (offset, width) in structural ? Reads(form) : [(2u, 4)])
        for (var faultByte = 0; faultByte < (structural ? width : 1); faultByte++)
        {
            var path = middle == "none" ? new[] { start, tail } : [start, middle, tail];
            Run(m, bus, report, path, ccr, incoming, alignment, vbr, form, offset, width, faultByte,
                new Epochs(incoming, first, second, final, matrix), batch);
        }
        report.Complete(output);
    }

    private void Audit(bool chained)
    {
        var bus = new ValidationFaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", chained ? "rte-validation-physical-chained" : "rte-validation-physical-direct");
        var paths = new List<string[]>();
        foreach (var start in SupervisorBanks)
        {
            if (!chained) paths.Add([start]);
            else foreach (var tail in SupervisorBanks)
            {
                paths.Add([start, tail]);
                foreach (var middle in SupervisorBanks) paths.Add([start, middle, tail]);
            }
        }
        foreach (var path in paths)
        foreach (var ccr in chained ? new[] { 0, 31 } : Enumerable.Range(0, 32))
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var form in Forms)
        foreach (var (offset, width) in Reads(form))
        for (var faultByte = 0; faultByte < width; faultByte++)
            Run(m, bus, report, path, ccr, trace, alignment, vbr, form, offset, width, faultByte);
        report.Complete(output);
    }

    internal static IEnumerable<(uint Offset, int Width)> Reads(string form)
    {
        yield return (0, 2); yield return (2, 4); yield return (6, 2);
        if (form.StartsWith("format") || form.StartsWith("invalid")) yield break;
        yield return (12, 2);
        if (form != "normal") yield return (8, 4);
    }

    private static void Run(SyntheticMachine m, ValidationFaultBus bus, CoverageBatch report,
        string[] path, int ccr, ushort trace, uint alignment, uint vbr, string form, uint offset, int width, int faultByte,
        Epochs? epochs = null, bool batch = false)
    {
        bus.Disarm(); m.Reset(ccr);
        var pointers = new Dictionary<string, uint> { ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment,
            ["user"] = 0x7800 + (epochs != null ? alignment : 0) };
        foreach (var at in epochs != null ? pointers.Values : pointers.Where(p => p.Key != "user").Select(p => p.Value))
            for (var n = -76; n < 80; n++) m.InitializePhysical(unchecked(at + (uint)n), (uint)(n ^ 0x5a), 1);
        for (var n = 0; n < path.Length - 1; n++)
        {
            var at = pointers[M68040StackFixture.PhysicalBank(path[n])];
            var throwawayTrace = epochs is { } ep ? n == 0 ? ep.First : ep.Second : trace;
            m.InitializePhysical(at, Status(path[n + 1], throwawayTrace, ccr ^ 31), 2);
            m.InitializePhysical(at + 2, 0xdead0001u + (uint)n * 2, 4);
            m.InitializePhysical(at + 6, 0x1024, 2);
            pointers[M68040StackFixture.PhysicalBank(path[n])] += 8;
        }
        var tail = path[^1]; var frame = pointers[M68040StackFixture.PhysicalBank(tail)];
        var format = form.StartsWith("format") ? int.Parse(form[6..]) : form.StartsWith("invalid") ? int.Parse(form[7..]) : 7;
        var post = form.StartsWith("CP", StringComparison.Ordinal);
        var vector = post ? form == "CP" ? 49 : int.Parse(form[2..]) : 11;
        var continuation = post ? 0x8000 : form switch { "CM" => 0x1000, "CT" => 0x2000, "CU" => 0x4000, _ => 0 };
        // Deliberately different S/M/CCR/trace bits: a fault must not install this SR.
        m.InitializePhysical(frame, epochs is { } final ? Status("user", final.Final, ccr) : 0x801fu ^ (uint)ccr, 2);
        m.InitializePhysical(frame + 2, Target, 4);
        m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
        m.InitializePhysical(frame + 8, 0x12345678, 4);
        m.InitializePhysical(frame + 12, (uint)continuation | 0x0105, 2);
        m.InitializePhysical(Handler, 0x4e73, 2); m.InitializePhysical(Handler + 2, 0x7001, 2);
        m.InitializePhysical(vbr + 8, Handler, 4);
        if (epochs != null) { m.InitializePhysical(vbr + 32, PrivilegeHandler, 4); m.InitializePhysical(PrivilegeHandler, 0x7e7e, 2); }
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        // Start at the input base, not the pointer advanced while constructing
        // aliased throwaways. The processor must consume every user frame.
        if (epochs != null) m.Core.State.SetUserStackPointer(0x7800 + alignment);
        m.Core.State.StatusRegister = Status(path[0], trace, ccr);
        m.Core.State.VectorBaseRegister = vbr;
        if (form == "CU") m.Core.State.M68040PendingFpuExceptions.Begin(2, 11, Target);
        if (post) m.Core.State.M68040PendingFpuExceptions.Begin(3, vector, Target);
        if (epochs != null)
        {
            m.Core.State.M68040Fpu.Fpcr = 0;
            m.Core.State.M68040Fpu.Fpsr = 0x08008198;
            m.Core.State.M68040Fpu.Fpiar = 0x1234abcd;
        }
        var e = ArchitecturalExpectation.Capture(m);
        var liveTrace = epochs is { } live ? path.Length == 2 ? live.First : live.Second : trace;
        var priorSr = Status(tail, liveTrace, path.Length > 1 ? ccr ^ 31 : ccr);
        var exceptionBank = M68040StackFixture.ExceptionBank(priorSr);
        var bridge = pointers[exceptionBank]; var accessFrame = bridge - 60;
        var sequence = m.Core.State.ExceptionSequence;
        pointers[exceptionBank] = accessFrame;
        var handlerSr = (ushort)((priorSr | 0x2000) & ~0xc000);
        SetStacks(e, pointers, handlerSr);
        e.Pc = Handler; e.ExceptionVector = 2;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 1);
        e.ControlChecks["saved RTE PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
        e.ControlChecks["saved pre-validation SR"] = (s => s.LastExceptionStatusRegister, priorSr);
        e.ControlChecks["fault rejected once"] = (_ => (uint)bus.Rejected.Count, 1);
        e.ControlChecks["original access address"] = (_ => bus.Rejected.Count == 1 ? bus.Rejected[0].Address : uint.MaxValue, frame + offset);
        e.ControlChecks["original access width"] = (_ => bus.Rejected.Count == 1 ? (uint)bus.Rejected[0].Width : 0, (uint)width);
        e.ControlChecks["discarded PCs never fetched"] = (_ => bus.Accesses.Any(a => a.Address is 0xdead0001 or 0xdead0003) ? 1u : 0u, 0);
        e.ControlChecks["handler sentinel never executed"] = (s => s.D[0], e.D[0]);
        if (form == "CU" || post) e.ControlChecks["pending delivery retained"] =
            (s => s.M68040PendingFpuExceptions.Find(form == "CU" ? 2 : 3) != null ? 1u : 0u, 1);
        if (epochs != null)
        {
            e.ControlChecks["FPCR preserved"] = (s => s.M68040Fpu.Fpcr, 0);
            e.ControlChecks["FPSR preserved"] = (s => s.M68040Fpu.Fpsr, 0x08008198);
            e.ControlChecks["FPIAR preserved"] = (s => s.M68040Fpu.Fpiar, 0x1234abcd);
            if (post) e.ControlChecks["pending original vector retained"] =
                (s => s.M68040PendingFpuExceptions.Find(3)?.Vector is { } v ? (uint)v : uint.MaxValue, (uint)vector);
        }
        ExpectAccessFrame(m, e, accessFrame, priorSr, SyntheticMachine.Code, frame + offset, width);
        bus.Arm(frame + offset + (uint)faultByte);
        var id = $"68040/RTE/validation-physical/{form}/path={string.Join('-', path)}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/read={offset}:{width}/fault-byte={faultByte}/op=4E73/ccr={ccr:X2}";
        if (epochs is { } mixed)
            id = $"68040/RTE/mixed-epoch-validation/{mixed.Matrix}/{form}/path={string.Join('-', path)}/incoming={mixed.Incoming:X4}/first={mixed.First:X4}/second={(path.Length == 2 ? "none" : mixed.Second.ToString("X4"))}/final={mixed.Final:X4}/align={alignment}/VBR={vbr:X8}/read={offset}:{width}/fault-byte={faultByte}/op=4E73/ccr={ccr:X2}";
        var userTail = epochs != null && (priorSr & 0x2000) == 0;
        var phases = userTail ? new[] { "fault-entry", "handler-return", "privilege" } : ["fault-entry", "handler-return"];
        var phase = 0;
        bool Step()
        {
            if (M68040StackFixture.Step(m, e, report, id + "/" + phases[phase++], batch)) return true;
            while (phase < phases.Length) report.Record(id + "/" + phases[phase++], "untested", "Validation prerequisite failed");
            return false;
        }
        if (!Step()) return;
        pointers[exceptionBank] = bridge;
        SetStacks(e, pointers, priorSr); e.Pc = SyntheticMachine.Code; e.ExceptionVector = null;
        if (!Step() || !userTail) return;
        pointers[exceptionBank] -= 8;
        SetStacks(e, pointers, handlerSr); e.Pc = PrivilegeHandler; e.ExceptionVector = 8;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 2);
        e.Write(bridge - 8, priorSr, 2, m.Model); e.Write(bridge - 6, SyntheticMachine.Code, 4, m.Model);
        e.Write(bridge - 2, 0x0020, 2, m.Model);
        for (uint n = 0; n < 8; n++) e.MemoryMasks[bridge - 8 + n] = 255;
        Step();
    }

    internal static void ExpectAccessFrame(SyntheticMachine m, ArchitecturalExpectation e, uint accessFrame,
        ushort priorSr, uint instructionPc, uint faultAddress, int width)
    {
        e.Write(accessFrame, priorSr, 2, m.Model); e.Write(accessFrame + 2, instructionPc, 4, m.Model);
        e.Write(accessFrame + 6, 0x7008, 2, m.Model);
        // EA/invalid WB data are undefined. Verify defined status/address fields,
        // all untouched original-frame bytes and all neighboring memory instead.
        for (uint n = 8; n < 60; n++) e.MemoryMasks[accessFrame + n] = 0;
        var transferModifier = (priorSr & 0x2000) != 0 ? 5u : 1u;
        e.Write(accessFrame + 12, (width == 2 ? 0x0140u : 0x0100u) | transferModifier, 2, m.Model);
        e.MemoryMasks[accessFrame + 12] = 0xff; e.MemoryMasks[accessFrame + 13] = 0x7f; // SSW X undefined
        foreach (var at in new uint[] { 14, 16, 18 })
        {
            e.Write(accessFrame + at, 0, 2, m.Model);
            e.MemoryMasks[accessFrame + at + 1] = 0x80; // WB valid bit; no pending write
        }
        e.Write(accessFrame + 20, faultAddress, 4, m.Model);
        for (uint n = 20; n < 24; n++) e.MemoryMasks[accessFrame + n] = 0xff;
    }

    private static ushort Status(string bank, ushort trace, int ccr) => M68040StackFixture.Status(bank, trace, ccr);
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr) =>
        M68040StackFixture.SetStacks(e, pointers, sr);

    internal sealed class ValidationFaultBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private uint? faultByte;
        public List<(uint Address, int Width)> Rejected { get; } = [];
        public void Disarm() { faultByte = null; Rejected.Clear(); }
        public void Arm(uint at) { faultByte = at; Rejected.Clear(); }
        public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind kind)
        {
            if (kind != M68kBusAccessKind.CpuDataRead || faultByte is not { } at || unchecked(at - address) >= byteCount) return true;
            faultByte = null; Rejected.Add((address, byteCount)); return false;
        }
    }
}
