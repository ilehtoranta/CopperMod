using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68060UM D-19/20 fixes the second opcode word at 01C0; 8.2.4/5
// distinguishes unrecognized F-line encodings from privileged instructions.
// Broadcast/pin behavior and physical prefetch ordering are not qualified here.
public sealed class SyntheticLowPowerStopTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Fact, Trait("Suite", "Synthetic")]
    public void EveryM68060UnrecognizedSecondWordRaisesLineFBeforePrivilege()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68060"));
        var report = new CoverageBatch("68060", "system-lpstop-extensions");
        for (var extension = 0; extension <= ushort.MaxValue; extension++)
        {
            if (extension == 0x01c0) continue;
            foreach (var supervisor in new[] { false, true })
                Run(m, report, (ushort)extension, 0x271f, supervisor, 0, 0,
                    $"68060/LPSTOP/unrecognized/high={extension >> 8:X2}/super={supervisor}/op=F800/ext={extension:X4}/ccr=00");
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void EncodingPrivilegeStatusAndIncomingTraceHaveIndependentOutcomes(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "system-lpstop-values");
        var extensions = Enumerable.Range(0, 16).Select(bit => (ushort)(0x01c0 ^ (1 << bit)))
            .Concat(new ushort[] { 0, 0xffff, 0x01c0 }).ToArray();
        Assert.Equal(19, extensions.Distinct().Count());
        foreach (var extension in extensions)
        foreach (var immediate in new ushort[] { 0, 0x071f, 0x2000, 0x271f, 0xa01f })
        foreach (var supervisor in new[] { false, true })
        foreach (var trace in new ushort[] { 0, 0x8000 })
        for (var ccr = 0; ccr < 32; ccr++)
            Run(m, report, extension, immediate, supervisor, trace, ccr,
                $"{modelId}/LPSTOP/encoding={extension:X4}/imm={immediate:X4}/super={supervisor}/T={trace:X4}/op=F800/ccr={ccr:X2}");
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, CoverageBatch report, ushort extension, ushort immediate,
        bool supervisor, ushort trace, int ccr, string id)
    {
        m.Reset(ccr, supervisor);
        // Fixed encoding and following sentinel; no production decoder is used.
        var e = SyntheticExecution.Prepare(m, [0xf800, extension, immediate]);
        m.Core.State.StatusRegister |= trace; e.Sr |= trace;
        var vector = m.Model.Id != "68060" || extension != 0x01c0 ? 11
            : !supervisor || (immediate & 0x2000) == 0 ? 8 : 0;
        if (vector != 0) SyntheticExecution.ExpectException(m, e, vector);
        else
        {
            SyntheticSystemTests.ApplyStatus(m, e, immediate);
            if (trace != 0) SyntheticExecution.ExpectException(m, e, 9, e.Pc);
            else e.Stopped = true;
        }
        if (vector != 0)
        {
            var savedSr = (ushort)((supervisor ? 0x2700 : 0x0700) | trace | ccr);
            e.ControlChecks["saved instruction PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
            e.ControlChecks["saved original SR"] = (s => s.LastExceptionStatusRegister, savedSr);
        }
        // Frame/neighbor memory, registers, stacks and flags are compared by the
        // shared verifier. A stopped CPU must not retire the following sentinel.
        try
        {
            m.Core.ExecuteInstruction();
            var mismatch = e.Verify(m);
            if (mismatch == null && e.Stopped)
            {
                var sequence = m.Core.State.ExceptionSequence;
                m.Core.ExecuteInstruction();
                mismatch = e.Verify(m);
                if (mismatch == null && m.Core.State.ExceptionSequence != sequence) mismatch = "Stopped CPU entered an unexpected exception";
            }
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
        }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
        { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
    }
}
