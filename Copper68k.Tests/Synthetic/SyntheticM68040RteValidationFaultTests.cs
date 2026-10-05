using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6.7: validation faults preserve the incomplete frame.
// Physical map rejection exercises the real logical bus with translation off.
// This does not inject a production exception or calculate an EA with CPU code.
public sealed class SyntheticM68040RteValidationFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020, Target = 0x6000;
    private static readonly string[] SupervisorBanks = ["ISP", "MSP"];
    private static readonly string[] Forms = ["format0", "format2", "format3", "invalid4", "invalid15",
        "normal", "CM", "CT", "CU", "CP"];

    [Fact, Trait("Suite", "Synthetic")]
    public void DirectValidationFaultsPreserveEveryCcrAndOriginalFrame() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void ValidationFaultsRetainCommittedThrowawaysAndStackSelection() => Audit(true);

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
        string[] path, int ccr, ushort trace, uint alignment, uint vbr, string form, uint offset, int width, int faultByte)
    {
        bus.Disarm(); m.Reset(ccr);
        var pointers = new Dictionary<string, uint> { ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
        foreach (var at in pointers.Values)
            for (var n = -76; n < 80; n++) m.InitializePhysical(unchecked(at + (uint)n), (uint)(n ^ 0x5a), 1);
        for (var n = 0; n < path.Length - 1; n++)
        {
            var at = pointers[path[n]];
            m.InitializePhysical(at, Status(path[n + 1], trace, ccr ^ 31), 2);
            m.InitializePhysical(at + 2, 0xdead0001u + (uint)n * 2, 4);
            m.InitializePhysical(at + 6, 0x1024, 2);
            pointers[path[n]] += 8;
        }
        var tail = path[^1]; var frame = pointers[tail];
        var format = form.StartsWith("format") ? int.Parse(form[6..]) : form.StartsWith("invalid") ? int.Parse(form[7..]) : 7;
        var continuation = form switch { "CM" => 0x1000, "CT" => 0x2000, "CU" => 0x4000, "CP" => 0x8000, _ => 0 };
        // Deliberately different S/M/CCR/trace bits: a fault must not install this SR.
        m.InitializePhysical(frame, 0x801fu ^ (uint)ccr, 2);
        m.InitializePhysical(frame + 2, Target, 4);
        m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
        m.InitializePhysical(frame + 8, 0x12345678, 4);
        m.InitializePhysical(frame + 12, (uint)continuation | 0x0105, 2);
        m.InitializePhysical(Handler, 0x4e73, 2); m.InitializePhysical(Handler + 2, 0x7001, 2);
        m.InitializePhysical(vbr + 8, Handler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = Status(path[0], trace, ccr);
        m.Core.State.VectorBaseRegister = vbr;
        if (form == "CU") m.Core.State.M68040PendingFpuExceptions.Begin(2, 11, Target);
        if (form == "CP") m.Core.State.M68040PendingFpuExceptions.Begin(3, 49, Target);
        var e = ArchitecturalExpectation.Capture(m);
        var priorSr = Status(tail, trace, path.Length > 1 ? ccr ^ 31 : ccr);
        var accessFrame = frame - 60;
        var sequence = m.Core.State.ExceptionSequence;
        pointers[tail] = accessFrame;
        SetStacks(e, pointers, tail, (ushort)(priorSr & ~0xc000));
        e.Pc = Handler; e.ExceptionVector = 2;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 1);
        e.ControlChecks["saved RTE PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
        e.ControlChecks["saved pre-validation SR"] = (s => s.LastExceptionStatusRegister, priorSr);
        e.ControlChecks["fault rejected once"] = (_ => (uint)bus.Rejected.Count, 1);
        e.ControlChecks["original access address"] = (_ => bus.Rejected.Count == 1 ? bus.Rejected[0].Address : uint.MaxValue, frame + offset);
        e.ControlChecks["original access width"] = (_ => bus.Rejected.Count == 1 ? (uint)bus.Rejected[0].Width : 0, (uint)width);
        e.ControlChecks["discarded PCs never fetched"] = (_ => bus.Accesses.Any(a => a.Address is 0xdead0001 or 0xdead0003) ? 1u : 0u, 0);
        e.ControlChecks["handler sentinel never executed"] = (s => s.D[0], e.D[0]);
        if (form is "CU" or "CP") e.ControlChecks["pending delivery retained"] =
            (s => s.M68040PendingFpuExceptions.Find(form == "CU" ? 2 : 3) != null ? 1u : 0u, 1);
        ExpectAccessFrame(m, e, accessFrame, priorSr, SyntheticMachine.Code, frame + offset, width);
        bus.Arm(frame + offset + (uint)faultByte);
        var id = $"68040/RTE/validation-physical/{form}/path={string.Join('-', path)}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/read={offset}:{width}/fault-byte={faultByte}/op=4E73/ccr={ccr:X2}";
        if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/fault-entry"))
        { report.Record(id + "/handler-return", "untested", "Validation fault entry prerequisite failed"); return; }
        pointers[tail] = frame;
        SetStacks(e, pointers, tail, priorSr); e.Pc = SyntheticMachine.Code; e.ExceptionVector = null;
        SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/handler-return");
    }

    internal static void ExpectAccessFrame(SyntheticMachine m, ArchitecturalExpectation e, uint accessFrame,
        ushort priorSr, uint instructionPc, uint faultAddress, int width)
    {
        e.Write(accessFrame, priorSr, 2, m.Model); e.Write(accessFrame + 2, instructionPc, 4, m.Model);
        e.Write(accessFrame + 6, 0x7008, 2, m.Model);
        // EA/invalid WB data are undefined. Verify defined status/address fields,
        // all untouched original-frame bytes and all neighboring memory instead.
        for (uint n = 8; n < 60; n++) e.MemoryMasks[accessFrame + n] = 0;
        e.Write(accessFrame + 12, width == 2 ? 0x0145u : 0x0105u, 2, m.Model);
        e.MemoryMasks[accessFrame + 12] = 0xff; e.MemoryMasks[accessFrame + 13] = 0x7f; // SSW X undefined
        foreach (var at in new uint[] { 14, 16, 18 })
        {
            e.Write(accessFrame + at, 0, 2, m.Model);
            e.MemoryMasks[accessFrame + at + 1] = 0x80; // WB valid bit; no pending write
        }
        e.Write(accessFrame + 20, faultAddress, 4, m.Model);
        for (uint n = 20; n < 24; n++) e.MemoryMasks[accessFrame + n] = 0xff;
    }

    private static ushort Status(string bank, ushort trace, int ccr) => (ushort)((bank == "MSP" ? 0x3000 : 0x2000) | trace | ccr);
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, string bank, ushort sr)
    {
        e.Sr = sr; e.A[7] = pointers[bank]; e.InactiveStackPointer = 0x7800; e.MasterStackPointer = pointers["MSP"];
        e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, pointers["ISP"]);
        e.ControlChecks["MSP"] = (s => s.MasterStackPointer, pointers["MSP"]);
    }

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
