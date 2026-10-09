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

    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochTraceCanonicalScalar() => AuditMixedEpoch(false, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochTraceCanonicalBatch() => AuditMixedEpoch(false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochTraceStructuralScalar() => AuditMixedEpoch(true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void MixedEpochTraceStructuralBatch() => AuditMixedEpoch(true, true);

    private readonly record struct Epochs(ushort Incoming, ushort First, ushort Second, string Matrix, uint Vbr);
    private static readonly ushort[] TraceStates = [0, 0x8000, 0x4000];
    private static readonly string[] MixedBanks = ["user", "user-M", "ISP", "MSP"];

    [Theory]
    [InlineData("user", 0, 0, 0x0000)]
    [InlineData("user", 0x8000, 31, 0x801f)]
    [InlineData("user-M", 0, 0, 0x1000)]
    [InlineData("user-M", 0x8000, 31, 0x901f)]
    [InlineData("ISP", 0x4000, 0, 0x6000)]
    [InlineData("MSP", 0x4000, 31, 0x701f)]
    public void FixtureStatusHasIndependentReferenceExamples(string bank, int trace, int ccr, int expected) =>
        Assert.Equal((ushort)expected, Status(bank, (ushort)trace, ccr));

    private void AuditMixedEpoch(bool structural, bool batch)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var matrix = structural ? "structure" : "canonical";
        var report = new CoverageBatch("68040", $"rte-mixed-epoch-{matrix}-{(batch ? "batch" : "scalar")}");
        foreach (var start in new[] { "ISP", "MSP" })
        foreach (var tail in MixedBanks)
        foreach (var middle in structural ? new[] { "none", "user", "user-M", "ISP", "MSP" } : ["none"])
        foreach (var result in MixedBanks)
        foreach (var incoming in TraceStates)
        foreach (var first in TraceStates)
        foreach (var second in middle == "none" ? new ushort[] { 0 } : TraceStates)
        foreach (var trace in TraceStates)
        foreach (var alignment in structural ? new uint[] { 0, 1 } : [0])
        foreach (var vbr in structural ? new uint[] { 0, 0x10000 } : [0x10000])
        foreach (var form in new[] { "format0", "format2", "format3", "normal", "CM", "CT", "CU",
            "CP49", "CP50", "CP51", "CP52", "CP53", "CP54", "CP55" })
        foreach (var ccr in structural ? new[] { 0, 31 } : Enumerable.Range(0, 32))
            Run(m, report, start, tail, middle, result, trace, alignment, form, ccr,
                !form.StartsWith("format", StringComparison.Ordinal), new Epochs(incoming, first, second, matrix, vbr), batch);
        report.Complete(output);
    }

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
        string result, ushort trace, uint alignment, string form, int ccr, bool access, Epochs? epochs = null, bool batch = false)
    {
        m.Reset(ccr);
        var pointers = new Dictionary<string, uint> { ["user"] = 0x7800 + alignment, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
        foreach (var location in pointers.Values)
            for (var n = -16; n < 112; n++) m.InitializePhysical(unchecked(location + (uint)n), (uint)(n ^ 0x5a), 1);
        var path = middle == "none" ? new[] { start, tail } : new[] { start, middle, tail };
        for (var n = 0; n < path.Length - 1; n++)
        {
            var at = pointers[PhysicalBank(path[n])];
            // The discarded PC is odd and must never become an instruction fetch.
            var throwawayTrace = epochs is { } mixed ? n == 0 ? mixed.First : mixed.Second
                : trace == 0x8000 ? (ushort)0x4000 : (ushort)0x8000;
            m.InitializePhysical(at, Status(path[n + 1], throwawayTrace, ccr), 2);
            m.InitializePhysical(at + 2, 0xdead0001u + (uint)n * 2, 4);
            m.InitializePhysical(at + 6, 0x1024, 2);
            pointers[PhysicalBank(path[n])] += 8;
        }
        var frame = pointers[PhysicalBank(tail)];
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
        var vbr = epochs?.Vbr ?? 0x10000;
        foreach (var v in new[] { 9, 11, 49, 50, 51, 52, 53, 54, 55 }) m.InitializePhysical(vbr + (uint)v * 4, Handler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetUserStackPointer(0x7800 + alignment);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = Status(start, epochs?.Incoming ?? 0, ccr);
        m.Core.State.VectorBaseRegister = vbr;
        if (continuation == 0x8000) m.Core.State.M68040PendingFpuExceptions.Begin(3, vector, Target);
        if (continuation == 0x4000) m.Core.State.M68040PendingFpuExceptions.Begin(2, 11, Target);
        if (epochs != null)
        {
            // Context inputs intentionally disagree with the original event;
            // no FPU instruction or arithmetic is executed by these fixtures.
            m.Core.State.M68040Fpu.Fpcr = 0;
            m.Core.State.M68040Fpu.Fpsr = 0x08008198;
            m.Core.State.M68040Fpu.Fpiar = 0x1234abcd;
        }
        var e = ArchitecturalExpectation.Capture(m);
        if (epochs != null)
        {
            e.ControlChecks["FPCR preserved"] = (s => s.M68040Fpu.Fpcr, 0);
            e.ControlChecks["FPSR preserved"] = (s => s.M68040Fpu.Fpsr, 0x08008198);
            e.ControlChecks["FPIAR preserved"] = (s => s.M68040Fpu.Fpiar, 0x1234abcd);
        }
        e.OperandAccessAddresses.UnionWith(new uint[] { 0x3218, 0x3220, 0x3228 });
        e.ExpectedOperandTransfers = [];
        foreach (var pc in new uint[] { 0xdead0001, 0xdead0003 }) e.ForbiddenOperandReads.Add(pc);
        e.ControlChecks["discarded PCs never fetched"] = (_ => m.Bus.Accesses.Any(a =>
            !a.Write && a.Address is 0xdead0001 or 0xdead0003) ? 1u : 0u, 0);
        if (vector != 0 && vector != 9)
            e.ControlChecks["pending delivery consumed"] = (s => s.M68040PendingFpuExceptions.Find(vector == 11 ? 2 : 3) == null ? 0u : 1u, 0);
        var sequence = m.Core.State.ExceptionSequence;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        pointers[PhysicalBank(tail)] += access ? 60u : format == 0 ? 8u : 12u;
        SetStacks(e, pointers, savedSr); e.Pc = Target;
        var id = $"68040/RTE/throwaway/{form}/start={start}/middle={middle}/tail={tail}/result={result}/T={trace:X4}/align={alignment}/op=4E73/ccr={ccr:X2}";
        if (epochs is { } epoch)
            id = $"68040/RTE/mixed-epoch/{epoch.Matrix}/{form}/start={start}/middle={middle}/tail={tail}/result={result}/incoming={epoch.Incoming:X4}/first={epoch.First:X4}/second={(middle == "none" ? "none" : epoch.Second.ToString("X4"))}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/op=4E73/ccr={ccr:X2}";
        // MC68040UM 8.2.6: the incoming instruction trace condition is latched
        // before any throwaway installs SR. Pending delivery wins (8.3/8.4.6.7).
        var rteTrace = vector == 0 && epochs is { Incoming: not 0 };
        var phases = new List<string> { "RTE" };
        if (vector != 0 || rteTrace) phases.Add("handler-return");
        phases.Add("following");
        if (epochs != null && form == "CM")
        {
            if (trace == 0x8000) phases.Add("movem-trace-return");
            phases.Add("following-BRA");
        }
        var phase = 0;
        bool Step()
        {
            var current = phases[phase++];
            if (ExecuteStep(m, e, report, id + "/" + current, batch)) return true;
            foreach (var remaining in phases.Skip(phase))
                report.Record(id + "/" + remaining, "untested", "Earlier mixed-epoch/throwaway phase failed");
            return false;
        }
        if (vector != 0) ExpectException(m, e, pointers, savedSr, vector, Target, Operand, sequence + 1);
        else if (rteTrace) ExpectException(m, e, pointers, savedSr, 9, Target, SyntheticMachine.Code, sequence + 1);
        if (!Step()) return;
        if (vector != 0 || rteTrace)
        {
            pointers[ExceptionBank(savedSr)] += 12;
            SetStacks(e, pointers, savedSr); e.Pc = Target; e.ExceptionVector = null;
            if (!Step()) return;
        }
        e.Pc = form == "CM" ? Target + 4 : Target;
        if (form == "CM") { e.D[0] = 0x89abcdef; e.D[1] = 0x12345678; }
        if (trace == 0x8000 || trace == 0x4000 && form != "CM")
            ExpectException(m, e, pointers, savedSr, 9, e.Pc, Target, sequence + (vector != 0 || rteTrace ? 2u : 1u));
        if (!Step() || epochs == null || form != "CM") return;
        var exceptions = sequence + (rteTrace ? 1u : 0u);
        if (trace == 0x8000)
        {
            exceptions++;
            pointers[ExceptionBank(savedSr)] += 12;
            SetStacks(e, pointers, savedSr); e.Pc = Target + 4; e.ExceptionVector = null;
            if (!Step()) return;
        }
        e.Pc = Target + 4;
        if (trace != 0) ExpectException(m, e, pointers, savedSr, 9, Target + 4, Target + 4, exceptions + 1);
        Step();
    }

    private static ushort Status(string bank, ushort trace, int ccr) =>
        M68040StackFixture.Status(bank, trace, ccr);
    private static string PhysicalBank(string bank) => M68040StackFixture.PhysicalBank(bank);
    private static string ExceptionBank(ushort sr) => M68040StackFixture.ExceptionBank(sr);
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr) =>
        M68040StackFixture.SetStacks(e, pointers, sr);
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

    private static bool ExecuteStep(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id, bool batch)
        => M68040StackFixture.Step(m, e, report, id, batch);
}

// Shared test-only stack/status fixtures. Fixed literal examples above validate
// the encoding independently; no production stack or exception helper is used.
internal static class M68040StackFixture
{
    internal static ushort Status(string bank, ushort trace, int ccr) =>
        (ushort)((bank == "ISP" ? 0x2000 : bank == "MSP" ? 0x3000 : bank == "user-M" ? 0x1000 : 0) | trace | ccr);
    internal static string PhysicalBank(string bank) => bank == "user-M" ? "user" : bank;
    internal static string ExceptionBank(ushort sr) => (sr & 0x1000) != 0 ? "MSP" : "ISP";
    internal static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr)
    {
        e.Sr = sr;
        e.A[7] = pointers[(sr & 0x2000) == 0 ? "user" : ExceptionBank(sr)];
        e.InactiveStackPointer = pointers[(sr & 0x2000) == 0 ? "ISP" : "user"];
        e.MasterStackPointer = pointers["MSP"];
        e.ControlChecks["USP"] = (s => s.UserStackPointer, pointers["user"]);
        e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, pointers["ISP"]);
        e.ControlChecks["MSP"] = (s => s.MasterStackPointer, pointers["MSP"]);
    }
    internal static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id, bool batch)
    {
        if (!batch) return SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id);
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
