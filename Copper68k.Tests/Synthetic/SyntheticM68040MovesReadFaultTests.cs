using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM Table 3-2, 8.4.6: only the MOVES operand uses alternate TT/TM.
// Physical-map rejection, caches/MMU disabled. Saved opcode PC and retained
// auto-address effects describe the existing synchronous execution policy;
// handler return, store completion and physical pipeline timing are not qualified.
public sealed class SyntheticM68040MovesReadFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020;
    private static readonly ushort[] Attributes = [0x10, 1, 1, 0x13, 0x14, 5, 5, 0x17];
    private static readonly string[] Forms = ["indirect", "post", "pre", "displacement", "brief", "absolute-word", "absolute-long", "full-pre", "full-post"];

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalReadFaultAttributesBelongOnlyToTheMovesOperand(bool batch)
    {
        var report = new CoverageBatch("68040", "moves-read-fault-provenance-" + (batch ? "batch" : "scalar"));
        foreach (var form in Forms) RunCohort("operand", form, "operand", [0, 31], false);
        RunCohort("ccr", "indirect", "operand", Enumerable.Range(0, 32), false);
        foreach (var form in new[] { "full-pre", "full-post" })
        foreach (var store in new[] { false, true }) RunCohort("pointer", form, "pointer", [0, 31], store);
        foreach (var form in Forms.Skip(3))
        foreach (var store in new[] { false, true }) RunCohort("extension", form, "extension", [0, 31], store);
        report.Complete(output);

        void RunCohort(string cohort, string form, string stage, IEnumerable<int> ccrs, bool store)
        {
            foreach (var width in new[] { 1, 2, 4 })
            foreach (var lane in new uint[] { 0, 1, 2, 3 })
            for (var fc = 0; fc < 8; fc++)
            foreach (var bank in new[] { "ISP", "MSP" })
            foreach (var ccr in ccrs)
            for (var rejectedByte = 0; rejectedByte < (stage == "operand" ? width : 4); rejectedByte++)
                Case(report, batch, cohort, form, stage, width, lane, fc, bank, ccr, rejectedByte, store);
        }
    }

    private static void Case(CoverageBatch report, bool batch, string cohort, string form, string stage,
        int width, uint lane, int fc, string bank, int ccr, int rejectedByte, bool store)
    {
        var id = $"68040/MOVES/read-fault/{cohort}/route={(batch ? "batch" : "scalar")}/form={form}/stage={stage}/size={width}/lane={lane}/FC={fc}/bank={bank}/ccr={ccr:X2}/byte={rejectedByte}/store={store}";
        try
        {
            var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
            m.Reset(ccr); var s = m.Core.State;
            var target = (form == "absolute-long" ? 0x12344200u : 0x4200u) + lane;
            s.A[0] = form switch { "pre" => target + (uint)width, "displacement" => target - 16,
                "brief" => target - 32, "full-pre" or "full-post" => 0x4000 + lane, _ => target };
            s.D[1] = 32;
            // Fixed legal opcode and extension shapes. Literal full-format
            // pre/post indexed examples are 1921/1925 with word BD=0010.
            var ea = form switch { "indirect" => 0x10, "post" => 0x18, "pre" => 0x20,
                "displacement" => 0x28, "brief" or "full-pre" or "full-post" => 0x30,
                "absolute-word" => 0x38, _ => 0x39 };
            var opcode = (ushort)((width == 1 ? 0x0e00 : width == 2 ? 0x0e40 : 0x0e80) | ea);
            var words = new List<ushort> { opcode, (ushort)(store ? 0x0800 : 0) };
            if (form == "displacement") words.Add(0x0010);
            else if (form == "brief") words.Add(0x1800);
            else if (form == "absolute-word") words.Add((ushort)target);
            else if (form == "absolute-long") { words.Add((ushort)(target >> 16)); words.Add((ushort)target); }
            else if (form is "full-pre" or "full-post") { words.Add((ushort)(form == "full-pre" ? 0x1921 : 0x1925)); words.Add(0x0010); }
            var pointer = (form == "full-pre" ? 0x4030u : 0x4010u) + lane;
            if (form is "full-pre" or "full-post") m.InitializePhysical(pointer, form == "full-pre" ? target : target - 32, 4);
            m.InitializePhysical(target, 0x12345678, width);
            m.InitializePhysical(Handler, 0x4e71, 2); m.InitializePhysical(Handler + 2, 0x4e71, 2);
            m.InitializePhysical(8, Handler, 4);
            _ = SyntheticExecution.Prepare(m, words);
            s.SetInterruptStackPointer(0x4700); s.SetMasterStackPointer(0x7400);
            s.StatusRegister = (ushort)((bank == "MSP" ? 0x3700 : 0x2700) | ccr);
            s.SourceFunctionCode = (uint)fc; s.DestinationFunctionCode = (uint)(7 - fc);
            var e = ArchitecturalExpectation.Capture(m); var sequence = s.ExceptionSequence;
            var stack = bank == "MSP" ? 0x7400u : 0x4700u; var frame = stack - 60;
            var faultAddress = stage == "extension" ? 0x1004u : stage == "pointer" ? pointer : target;
            var faultWidth = stage == "operand" ? width : 4;
            if (stage == "operand" && form is "post" or "pre") e.A[0] = form == "post" ? target + (uint)width : target;
            e.A[7] = frame; if (bank == "MSP") e.MasterStackPointer = frame;
            e.Pc = Handler; e.ExceptionVector = 2;
            e.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, sequence + 1);
            e.ControlChecks["SFC"] = (x => x.SourceFunctionCode, (uint)fc);
            e.ControlChecks["DFC"] = (x => x.DestinationFunctionCode, (uint)(7 - fc));
            e.ControlChecks["synchronous saved opcode PC"] = (x => x.LastExceptionStackedProgramCounter, 0x1000);
            e.Write(frame, e.Sr, 2, m.Model); e.Write(frame + 2, 0x1000, 4, m.Model); e.Write(frame + 6, 0x7008, 2, m.Model);
            for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
            var attribute = stage == "operand" ? Attributes[fc] : stage == "extension" ? 6 : 5;
            var size = faultWidth == 1 ? 0x20 : faultWidth == 2 ? 0x40 : 0;
            e.Write(frame + 12, (uint)(0x0100 | size | attribute), 2, m.Model);
            e.MemoryMasks[frame + 12] = 255; e.MemoryMasks[frame + 13] = 0x7f; // X undefined.
            foreach (var at in new uint[] { 14, 16, 18 }) { e.Write(frame + at, 0, 2, m.Model); e.MemoryMasks[frame + at + 1] = 0x80; }
            e.Write(frame + 20, faultAddress, 4, m.Model);
            for (uint n = 20; n < 24; n++) e.MemoryMasks[frame + n] = 255;
            var kind = stage == "extension" ? M68kBusAccessKind.CpuInstructionFetch : M68kBusAccessKind.CpuDataRead;
            bus.Arm(faultAddress + (uint)rejectedByte, kind);
            if (batch)
            {
                if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, s.Cycles + 1000, new Boundary()) != 1)
                    throw new InvalidOperationException("Expected one batch instruction boundary");
            }
            else m.Core.ExecuteInstruction();
            if (e.Verify(m) is { } mismatch) throw new InvalidOperationException(mismatch);
            if (s.M68040Mmu.BypassTranslation || bus.Rejected.Count != 1 || bus.Rejected[0].Address != faultAddress ||
                bus.Rejected[0].Width != faultWidth || bus.Rejected[0].Kind != kind)
                throw new InvalidOperationException("Rejection provenance/width or bypass state differs");
            if (bus.Accesses.Any(x => x.Address == target && x.Kind is M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite))
                throw new InvalidOperationException("MOVES operand completed or was retried after rejection");
            if (bus.Accesses.Count(x => x.Address == pointer && x.Kind == M68kBusAccessKind.CpuDataRead) !=
                (stage == "operand" && form is "full-pre" or "full-post" ? 1 : 0))
                throw new InvalidOperationException("Pointer was repeated or accessed before its extension fault");
            report.Record(id + $"/op={opcode:X4}", "passing", null);
        }
        catch (Exception ex) { report.Record(id + "/op=fixture", "mismatching", ex.Message); }
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
    }
}
