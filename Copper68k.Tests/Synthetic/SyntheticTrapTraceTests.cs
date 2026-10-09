using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticTrapTraceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void SynchronousTrapTracePriorityFramesAndReturn(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "system-trap-trace");
        foreach (var trace in m.Model.FullIndex && modelId != "68060" ? new[] { 0, 0x8000, 0x4000 } : new[] { 0, 0x8000 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var instruction in Instructions())
        {
            var (e, vector, nextPc) = Prepare(m, instruction, trace, supervisor, ccr);
            var sequence = m.Core.State.ExceptionSequence;
            if (vector != 0) SyntheticExecution.ExpectException(m, e, vector, vector == 4 ? SyntheticMachine.Code : nextPc);
            // MC68040UM 8.3 and MC68060UM 8.2.6/8.3 suppress a pending
            // trace when a synchronous trap wins. Earlier models retain it.
            var pendingTrace = trace != 0 && vector != 4 && vector != 0 && modelId is not ("68040" or "68060");
            if (pendingTrace || vector == 0 && trace == 0x8000) SyntheticExecution.ExpectException(m, e, 9, e.Pc);
            var entries = (vector == 0 ? 0u : 1u) + (pendingTrace || vector == 0 && trace == 0x8000 ? 1u : 0u);
            e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + entries);
            SyntheticExecution.Run(m, e, report, Id(m, instruction, trace, supervisor, ccr) + "/entry", false);
        }
        if (modelId is "68040" or "68060") ReturnAndResume(m, report);
        report.Complete(output);
    }

    private sealed record Instruction(string Id, ushort[] Words, uint Dividend = 0x12345678, uint Source = 0);

    private static IEnumerable<Instruction> Instructions()
    {
        for (var n = 0; n < 16; n++) yield return new($"TRAP-{n}", [(ushort)(0x4e40 | n)]);
        yield return new("TRAPV", [0x4e76]);
        yield return new("DIVU.W-zero", [0x80c1]);
        yield return new("DIVS.W-zero", [0x81c1]);
        yield return new("DIVU.L-zero", [0x4c41, 0x0001]);
        yield return new("DIVS.L-zero", [0x4c41, 0x0801]);
        yield return new("CHK.W-negative", [0x4181], 0xffff, 8);
        yield return new("CHK.W-above", [0x4181], 9, 8);
        yield return new("CHK.L-negative", [0x4101], 0xffffffff, 8);
        yield return new("CHK.L-above", [0x4101], 9, 8);
        foreach (var condition in new[] { "T", "F" })
        {
            var prefix = condition == "T" ? 0x5000 : 0x5100;
            yield return new($"TRAP{condition}-none", [(ushort)(prefix | 0xfc)]);
            yield return new($"TRAP{condition}-word", [(ushort)(prefix | 0xfa), 0x1357]);
            yield return new($"TRAP{condition}-long", [(ushort)(prefix | 0xfb), 0x1357, 0x9bdf]);
        }
    }

    private static (ArchitecturalExpectation Expected, int Vector, uint NextPc) Prepare(
        SyntheticMachine m, Instruction instruction, int trace, bool supervisor, int ccr, bool returnHandler = false)
    {
        m.Reset(ccr, supervisor);
        m.Core.State.D[0] = instruction.Dividend; m.Core.State.D[1] = instruction.Source;
        var vector = instruction.Id.StartsWith("TRAP-") ? 32 + (instruction.Words[0] & 15) :
            instruction.Id == "TRAPV" ? (ccr & 2) != 0 ? 7 : 0 :
            instruction.Id.StartsWith("DIV") ? 5 : instruction.Id.StartsWith("CHK") ? 6 :
            instruction.Id.StartsWith("TRAPT") ? 7 : 0;
        var unavailable = !m.Model.FullIndex && (instruction.Id.Contains(".L") || instruction.Id.StartsWith("TRAPT") || instruction.Id.StartsWith("TRAPF"));
        if (unavailable) vector = 4;
        if (returnHandler) m.InitializePhysical((uint)(0x9000 + vector * 0x10), 0x4e73, 2);
        var words = returnHandler ? instruction.Words.Concat(new ushort[] { 0x60fe }).ToArray() : instruction.Words;
        var e = SyntheticExecution.Prepare(m, words);
        var nextPc = SyntheticMachine.Code + (uint)instruction.Words.Length * 2;
        e.Pc = nextPc;
        m.Core.State.StatusRegister |= (ushort)trace; e.Sr |= (ushort)trace;
        if (!unavailable && instruction.Id.StartsWith("DIV"))
        {
            e.Sr &= 0xfffe; e.DefinedSrMask = 0xfff1; // N/Z/V undefined; X preserved, C clear.
        }
        if (!unavailable && instruction.Id.StartsWith("CHK"))
        {
            e.Sr = (ushort)((e.Sr & ~8) | (instruction.Id.EndsWith("negative") ? 8 : 0));
            e.DefinedSrMask = 0xfff8; // X preserved, trapping N defined; Z/V/C undefined.
        }
        return (e, vector, nextPc);
    }

    private static void ReturnAndResume(SyntheticMachine m, CoverageBatch report)
    {
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var instruction in Instructions().Where(x => x.Id.StartsWith("TRAP-") || x.Id.StartsWith("DIV") && x.Id.Contains(".W") || x.Id.StartsWith("CHK.W") || x.Id.StartsWith("TRAPT")))
        {
            var (e, vector, nextPc) = Prepare(m, instruction, 0x8000, supervisor, ccr, true);
            var restoredSr = e.Sr;
            var sequence = m.Core.State.ExceptionSequence;
            SyntheticExecution.ExpectException(m, e, vector, nextPc);
            e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 1);
            var id = Id(m, instruction, 0x8000, supervisor, ccr) + "/return";
            if (!Step(m, e, report, id + "/entry"))
            {
                report.Record(id + "/RTE", "untested", "Trap entry prerequisite failed");
                report.Record(id + "/resumed-BRA", "untested", "Trap entry prerequisite failed");
                continue;
            }
            e.A[7] += vector >= 32 ? 8u : 12u;
            SyntheticSystemTests.ApplyStatus(m, e, restoredSr);
            e.Pc = nextPc; e.ExceptionVector = null;
            if (!Step(m, e, report, id + "/RTE"))
            {
                report.Record(id + "/resumed-BRA", "untested", "RTE prerequisite failed");
                continue;
            }
            // A restored T1 traces the following instruction, never RTE itself.
            // The self-branch makes its traced instruction address unambiguous.
            SyntheticExecution.ExpectException(m, e, 9, nextPc);
            e.Write(e.A[7] + 8, nextPc, 4, m.Model);
            e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 2);
            Step(m, e, report, id + "/resumed-BRA");
        }
    }

    private static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
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
            report.Record(id, ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException ? "unsupported" : "mismatching", ex.Message);
            return false;
        }
    }

    private static string Id(SyntheticMachine m, Instruction instruction, int trace, bool supervisor, int ccr) =>
        $"{m.Model.Id}/trap-trace/{instruction.Id}/op={instruction.Words[0]:X4}/T={trace:X4}/super={supervisor}/ccr={ccr:X2}";
}
