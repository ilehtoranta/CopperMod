using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68030UM 8.1.5/6 and 9.8: CpID-0 F-line words are privileged even
// when their subsequent encoding is undefined. This is not MMU translation
// or successful supervisor PMMU execution qualification.
public sealed class SyntheticM68030PmmuPrivilegeTests(ITestOutputHelper output)
{
    private static readonly ushort[] Payloads = [0, 1, 0x00be, 0x2000, 0x4000, 0x8000, 0xa000, 0xffff];

    public static IEnumerable<object[]> ModelControls() =>
        from model in ModelSpec.All from supervisor in new[] { false, true } from batch in new[] { false, true }
        select new object[] { model.Id, supervisor, batch };

    public static IEnumerable<object[]> ExternalControls() =>
        from id in Enumerable.Range(1, 7) from batch in new[] { false, true }
        select new object[] { id, batch };

    [Theory, MemberData(nameof(ModelControls)), Trait("Suite", "Synthetic")]
    public void RuleIsSpecificToUserModeOn68030(string modelId, bool supervisor, bool batch)
        => Control(new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)), 0xf000,
            supervisor, batch, modelId == "68030" && !supervisor ? 8 : 11);

    [Theory, MemberData(nameof(ExternalControls)), Trait("Suite", "Synthetic")]
    public void NonzeroCpIdsKeepTheAbsentCoprocessorLineFRoute(int id, bool batch)
        => Control(new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68030")),
            (ushort)(0xf000 | id << 9), false, batch, 11);

    private static void Control(SyntheticMachine m, ushort opcode, bool supervisor, bool batch, int vector)
    {
        m.Reset(31, supervisor);
        var e = SyntheticExecution.Prepare(m, [opcode, 0x00be, 0x4e71]);
        SyntheticExecution.ExpectException(m, e, vector);
        var sequence = m.Core.State.ExceptionSequence;
        e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 1);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
        Assert.Null(Step(m, e, batch));
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void UndefinedPmmuWordInUserModeRaisesPrivilegeBeforeLineF()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68030"));
        var result = Case(m, false, 0xf000, 0x00be, 0);
        Assert.True(result is null, result);
    }

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCpIdZeroPrimaryWordInUserMode(bool batch)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68030"));
        var report = new CoverageBatch("68030", "system-pmmu-privilege-opcodes-" + (batch ? "batch" : "scalar"));
        for (var opcode = 0xf000; opcode <= 0xf1ff; opcode++)
            Record(report, m, batch, (ushort)opcode, 0x00be, 0x071f);
        report.Complete(output);
    }

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryDefinedUserStatusAndSecondaryWordBoundaries(bool batch)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68030"));
        var report = new CoverageBatch("68030", "system-pmmu-privilege-status-" + (batch ? "batch" : "scalar"));
        for (var form = 0; form < 8; form++)
        foreach (var payload in Payloads)
        for (var trace = 0; trace < 4; trace++)
        for (var master = 0; master < 2; master++)
        for (var ipl = 0; ipl < 8; ipl++)
        for (var ccr = 0; ccr < 32; ccr++)
            Record(report, m, batch, (ushort)(0xf000 | form << 6), payload,
                (ushort)(trace << 14 | master << 12 | ipl << 8 | ccr));
        report.Complete(output);
    }

    private static void Record(CoverageBatch report, SyntheticMachine m, bool batch, ushort opcode, ushort payload, ushort sr)
    {
        var id = $"68030/PMMU/privilege/primary={opcode:X4}/extension={payload:X4}/status={sr & 0xffe0:X4}/op={opcode:X4}/ccr={sr & 31:X2}";
        try
        {
            var mismatch = Case(m, batch, opcode, payload, sr);
            report.Record(id, mismatch is null ? "passing" : "mismatching", mismatch);
        }
        catch (Exception ex)
        {
            report.Record(id, ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException
                ? "unsupported" : "mismatching", ex.ToString());
        }
    }

    private static string? Case(SyntheticMachine m, bool batch, ushort opcode, ushort payload, ushort sr)
    {
        const uint handler = 0x9080, traceHandler = 0x9090;
        m.Reset(sr & 31, false);
        m.Core.State.StatusRegister = sr;
        m.InitializePhysical(0x420, handler, 4); // VBR + privilege vector.
        m.InitializePhysical(0x42c, 0x90b0, 4); // Distinct Line-F route for failure diagnostics.
        m.InitializePhysical(0x90b0, 0x4e71, 2);
        m.InitializePhysical(SyntheticMachine.Code, opcode, 2);
        m.InitializePhysical(SyntheticMachine.Code + 2, payload, 2);
        m.InitializePhysical(SyntheticMachine.Code + 4, 0x7e55, 2); // MOVEQ #$55,D7 sentinel.
        m.InitializePhysical(SyntheticMachine.Code + 6, 0x4e71, 2);
        // Software deliberately skips both fixture words; this is not a PMMU
        // instruction-length expectation or retry of the privileged operation.
        m.InitializePhysical(handler, 0x58af, 2); // ADDQ.L #4,2(A7)
        m.InitializePhysical(handler + 2, 2, 2);
        m.InitializePhysical(handler + 4, 0x4e73, 2);
        m.InitializePhysical(handler + 6, 0x4e71, 2);
        m.InitializePhysical(0x424, traceHandler, 4);
        m.InitializePhysical(traceHandler, 0x4e73, 2);
        m.Start();
        m.Core.State.VectorBaseRegister = 0x400;
        m.Core.State.SetUserStackPointer(0x7800);
        m.Core.State.SetInterruptStackPointer(0x4700);
        m.Core.State.SetMasterStackPointer(0x7400);
        m.Core.State.SourceFunctionCode = 1;
        m.Core.State.DestinationFunctionCode = 5;
        var sequence = m.Core.State.ExceptionSequence;
        var e = ArchitecturalExpectation.Capture(m);
        var master = (sr & 0x1000) != 0;
        var top = master ? 0x7400u : 0x4700u;
        var frame = top - 8;
        e.Pc = handler; e.Sr = (ushort)((sr | 0x2000) & 0x3fff); e.ExceptionVector = 8;
        e.A[7] = frame; e.InactiveStackPointer = 0x7800;
        e.MasterStackPointer = master ? frame : 0x7400;
        e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, master ? 0x4700u : frame);
        e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 1);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
        e.ControlChecks["VBR"] = (s => s.VectorBaseRegister, 0x400);
        e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, 1);
        e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, 5);
        e.Write(frame, sr, 2, m.Model); e.Write(frame + 2, SyntheticMachine.Code, 4, m.Model);
        e.Write(frame + 6, 0x20, 2, m.Model);
        var mismatch = Step(m, e, batch);
        if (m.Core.State.LastExceptionVector != 8)
            return $"entry: expected privilege vector 8, actual {m.Core.State.LastExceptionVector}";
        if (m.Bus.Accesses.Any(a => a.Kind == M68kBusAccessKind.CpuDataRead &&
            Enumerable.Range(0, a.Width).Any(n => a.Address + (uint)n < 0x420 || a.Address + (uint)n >= 0x424)))
            return "entry: privileged operation read operand memory before trapping";
        if (mismatch != null) return "entry: " + mismatch;
        e.Write(frame + 2, SyntheticMachine.Code + 4, 4, m.Model);
        e.Pc = handler + 4; e.Sr &= 0xffe0; // Positive nonzero ADDQ result, no carry/overflow.
        mismatch = Step(m, e, batch); if (mismatch != null) return "software skip: " + mismatch;
        e.Pc = SyntheticMachine.Code + 4; e.Sr = sr; e.A[7] = 0x7800;
        e.InactiveStackPointer = 0x4700; e.MasterStackPointer = 0x7400;
        e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, 0x4700);
        mismatch = Step(m, e, batch); if (mismatch != null) return "RTE: " + mismatch;
        e.D[7] = 0x55; e.Sr = (ushort)(sr & 0xfff0); e.Pc = SyntheticMachine.Code + 6;
        if ((sr & 0x8000) != 0)
        {
            var tracedSr = e.Sr; var traceFrame = top - 12;
            e.A[7] = traceFrame; e.InactiveStackPointer = 0x7800;
            e.MasterStackPointer = master ? traceFrame : 0x7400;
            e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, master ? 0x4700u : traceFrame);
            e.Sr = (ushort)((tracedSr | 0x2000) & 0x3fff); e.Pc = traceHandler; e.ExceptionVector = 9;
            e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 2);
            e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code + 6);
            e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, tracedSr);
            e.Write(traceFrame, tracedSr, 2, m.Model); e.Write(traceFrame + 2, SyntheticMachine.Code + 6, 4, m.Model);
            e.Write(traceFrame + 6, 0x2024, 2, m.Model); e.Write(traceFrame + 8, SyntheticMachine.Code + 4, 4, m.Model);
        }
        return Step(m, e, batch);
    }

    private static string? Step(SyntheticMachine m, ArchitecturalExpectation e, bool batch)
    {
        if (!batch) m.Core.ExecuteInstruction();
        else if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 1000, new Boundary()) != 1)
            return "Expected exactly one batch instruction";
        return e.Verify(m);
    }
    private sealed class Boundary : IM68kInstructionBoundary
    { public bool BeforeInstruction() => true; public void AfterInstruction(long previousCycle, long currentCycle) { } }
}
