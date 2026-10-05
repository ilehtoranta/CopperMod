using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.2: a throwaway selects any of the three stacks and may
// lead to another throwaway. Stack locations and frame encodings are inputs;
// independent pointer bookkeeping below is not production RTE/EA logic.
public sealed class SyntheticM68040ThrowawayTests(ITestOutputHelper output)
{
    private static readonly string[] Banks = ["user", "ISP", "MSP"];
    private const uint Target = 0x6000, Handler = 0x9090, Operand = 0x4200;

    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedThrowawaysSelectEveryStackBeforeShortFrames() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedThrowawaysSelectEveryStackBeforeAccessContinuations() => Audit(true);

    private void Audit(bool access)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var report = new CoverageBatch("68040", access ? "rte-throwaway-access" : "rte-throwaway-controls");
        var forms = access ? new[] { "normal", "CM", "CT", "CU", "CP49", "CP50", "CP51", "CP52", "CP53", "CP54", "CP55" }
            : new[] { "format0", "format2", "format3" };
        foreach (var start in new[] { "ISP", "MSP" })
        foreach (var tail in Banks)
        foreach (var middle in new[] { "none", "user", "ISP", "MSP" })
        foreach (var result in Banks)
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var form in forms)
        for (var ccr = 0; ccr < 32; ccr++)
            Run(m, report, start, tail, middle, result, trace, alignment, form, ccr, access);
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, CoverageBatch report, string start, string tail, string middle,
        string result, ushort trace, uint alignment, string form, int ccr, bool access)
    {
        m.Reset(ccr);
        var pointers = new Dictionary<string, uint> { ["user"] = 0x7800 + alignment, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
        foreach (var location in pointers.Values)
            for (var n = -16; n < 112; n++) m.InitializePhysical(unchecked(location + (uint)n), (uint)(n ^ 0x5a), 1);
        var path = middle == "none" ? new[] { start, tail } : new[] { start, middle, tail };
        for (var n = 0; n < path.Length - 1; n++)
        {
            var at = pointers[path[n]];
            // The discarded PC is odd and must never become an instruction fetch.
            m.InitializePhysical(at, Status(path[n + 1], trace == 0x8000 ? (ushort)0x4000 : (ushort)0x8000, ccr), 2);
            m.InitializePhysical(at + 2, 0xdead0001u + (uint)n * 2, 4);
            m.InitializePhysical(at + 6, 0x1024, 2);
            pointers[path[n]] += 8;
        }
        var frame = pointers[tail];
        var savedSr = Status(result, trace, ccr ^ 31);
        var format = access ? 7 : form == "format0" ? 0 : form == "format2" ? 2 : 3;
        var vector = form == "CT" ? 9 : form == "CU" ? 11 : form.StartsWith("CP") ? int.Parse(form[2..]) : 0;
        var continuation = form == "CM" ? 0x1000 : form == "CT" ? 0x2000 : form == "CU" ? 0x4000 : vector >= 49 ? 0x8000 : 0;
        m.InitializePhysical(frame, savedSr, 2); m.InitializePhysical(frame + 2, Target, 4);
        m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
        m.InitializePhysical(frame + 8, Operand, 4); m.InitializePhysical(frame + 12, (uint)(continuation | 0x0105), 2);
        // Handler-owned writebacks remain untouched even through chained returns.
        foreach (var offset in new[] { 14u, 16u, 18u }) m.InitializePhysical(frame + offset, 0x85, 2);
        foreach (var offset in new[] { 24u, 32u, 40u })
        {
            m.InitializePhysical(frame + offset, 0x3200 + offset, 4);
            m.InitializePhysical(frame + offset + 4, 0xc3a55a3c, 4);
            m.InitializePhysical(0x3200 + offset, 0x96abcdef, 4);
        }
        if (form == "CM")
        {
            m.InitializePhysical(Target, 0x4cd0, 2); m.InitializePhysical(Target + 2, 3, 2); // MOVEM.L (A0),D0-D1
            m.InitializePhysical(Target + 4, 0x60fe, 2);
            m.InitializePhysical(Operand, 0x89abcdef, 4); m.InitializePhysical(Operand + 4, 0x12345678, 4);
            m.Core.State.A[0] = 0x4300; // saved EA must override the handler's base.
        }
        else m.InitializePhysical(Target, 0x60fe, 2);
        m.InitializePhysical(Handler, 0x4e73, 2); m.InitializePhysical(Handler + 2, 0x4e71, 2);
        const uint vbr = 0x10000;
        foreach (var v in new[] { 9, 11, 49, 50, 51, 52, 53, 54, 55 }) m.InitializePhysical(vbr + (uint)v * 4, Handler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetUserStackPointer(0x7800 + alignment);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = Status(start, 0, ccr);
        m.Core.State.VectorBaseRegister = vbr;
        if (continuation == 0x8000) m.Core.State.M68040PendingFpuExceptions.Begin(3, vector, Target);
        if (continuation == 0x4000) m.Core.State.M68040PendingFpuExceptions.Begin(2, 11, Target);
        var e = ArchitecturalExpectation.Capture(m);
        e.OperandAccessAddresses.UnionWith(new uint[] { 0x3218, 0x3220, 0x3228 });
        e.ExpectedOperandTransfers = [];
        foreach (var pc in new uint[] { 0xdead0001, 0xdead0003 }) e.ForbiddenOperandReads.Add(pc);
        e.ControlChecks["discarded PCs never fetched"] = (_ => m.Bus.Accesses.Any(a =>
            !a.Write && a.Address is 0xdead0001 or 0xdead0003) ? 1u : 0u, 0);
        if (vector != 0 && vector != 9)
            e.ControlChecks["pending delivery consumed"] = (s => s.M68040PendingFpuExceptions.Find(vector == 11 ? 2 : 3) == null ? 0u : 1u, 0);
        var sequence = m.Core.State.ExceptionSequence;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        pointers[tail] += access ? 60u : format == 0 ? 8u : 12u;
        SetStacks(e, pointers, savedSr); e.Pc = Target;
        var id = $"68040/RTE/throwaway/{form}/start={start}/middle={middle}/tail={tail}/result={result}/T={trace:X4}/align={alignment}/op=4E73/ccr={ccr:X2}";
        if (vector != 0) ExpectException(m, e, pointers, savedSr, vector, Target, Operand, sequence + 1);
        if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/RTE"))
        {
            if (vector != 0) report.Record(id + "/handler-return", "untested", "RTE prerequisite failed");
            report.Record(id + "/following", "untested", "RTE prerequisite failed"); return;
        }
        if (vector != 0)
        {
            pointers[ExceptionBank(savedSr)] += 12;
            SetStacks(e, pointers, savedSr); e.Pc = Target; e.ExceptionVector = null;
            if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/handler-return"))
            { report.Record(id + "/following", "untested", "Handler return prerequisite failed"); return; }
        }
        e.Pc = form == "CM" ? Target + 4 : Target;
        if (form == "CM") { e.D[0] = 0x89abcdef; e.D[1] = 0x12345678; }
        if (trace == 0x8000 || trace == 0x4000 && form != "CM")
            ExpectException(m, e, pointers, savedSr, 9, e.Pc, Target, sequence + (vector != 0 ? 2u : 1u));
        SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/following");
    }

    private static ushort Status(string bank, ushort trace, int ccr) =>
        (ushort)((bank == "ISP" ? 0x2000 : bank == "MSP" ? 0x3000 : 0) | trace | ccr);
    private static string ExceptionBank(ushort sr) => (sr & 0x3000) == 0x3000 ? "MSP" : "ISP";
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr)
    {
        e.Sr = sr;
        e.A[7] = pointers[(sr & 0x2000) == 0 ? "user" : ExceptionBank(sr)];
        e.InactiveStackPointer = pointers[(sr & 0x2000) == 0 ? "ISP" : "user"];
        e.MasterStackPointer = pointers["MSP"];
        e.ControlChecks["USP"] = (s => s.UserStackPointer, pointers["user"]);
        e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, pointers["ISP"]);
        e.ControlChecks["MSP"] = (s => s.MasterStackPointer, pointers["MSP"]);
    }
    private static void ExpectException(SyntheticMachine m, ArchitecturalExpectation e, Dictionary<string, uint> pointers,
        ushort sr, int vector, uint pc, uint address, uint sequence)
    {
        var bank = ExceptionBank(sr); pointers[bank] -= 12;
        SetStacks(e, pointers, (ushort)((sr | 0x2000) & ~0xc000));
        e.Pc = Handler; e.ExceptionVector = vector;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, pc);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
        var stack = pointers[bank];
        e.Write(stack, sr, 2, m.Model); e.Write(stack + 2, pc, 4, m.Model);
        e.Write(stack + 6, (uint)((vector >= 49 ? 3 : 2) << 12 | vector * 4), 2, m.Model);
        e.Write(stack + 8, address, 4, m.Model);
    }
}
