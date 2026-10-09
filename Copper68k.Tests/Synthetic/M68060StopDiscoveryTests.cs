using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Discovery of pinned WinUAE's undocumented ordinary STOP S-clear rule.
// Privilege/trace frames use manual rules; no full native exception or IRQ oracle.
public sealed class M68060StopDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_060_STOP_DISCOVERY";
    [EnvironmentFact(Enable, "compare 68060 ordinary STOP software-reference outcomes"), Trait("Suite", "ReferenceDiscovery")]
    public void DefinedStatusImagesScalar() => Audit(false);
    [EnvironmentFact(Enable, "compare 68060 ordinary STOP software-reference outcomes"), Trait("Suite", "ReferenceDiscovery")]
    public void DefinedStatusImagesBatch() => Audit(true);

    private void Audit(bool batch)
    {
        var group = "stop-discovery-" + (batch ? "batch" : "scalar");
        var report = new CoverageBatch("68060", group);
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68060"));
        var export = Environment.GetEnvironmentVariable("COPPER68K_060_STOP_EXPORT");
        if (string.IsNullOrWhiteSpace(export)) throw new InvalidOperationException("STOP discovery requires an export directory");
        using var rows = new StreamWriter(Path.Combine(export, group + ".rows"));
        uint index = 0;
        foreach (var matrix in new[] { "images", "ccr" })
        foreach (var supervisor in new[] { false, true })
        foreach (var master in new[] { 0, 0x1000 })
        foreach (var trace in new[] { 0, 0x8000 })
        foreach (var ccr in matrix == "images" ? new[] { 0, 31 } : Enumerable.Range(0, 32))
        foreach (var resultS in new[] { 0, 0x2000 })
        foreach (var resultM in new[] { 0, 0x1000 })
        foreach (var resultT in new[] { 0, 0x8000 })
        foreach (var ipl in matrix == "images" ? Enumerable.Range(0, 8) : new[] { 0, 7 })
        foreach (var resultCcr in Enumerable.Range(0, 32))
        {
            m.Reset(ccr, supervisor);
            var sr = (ushort)((supervisor ? 0x2700 : 0x0700) | master | trace | ccr);
            var immediate = (ushort)(resultS | resultM | resultT | ipl << 8 | resultCcr);
            m.Core.State.StatusRegister = sr;
            foreach (var stack in new[] { 0x4700u, 0x7800u })
                for (var offset = -20; offset < 16; offset++) m.InitializePhysical(unchecked(stack + (uint)offset), (uint)(offset ^ 0x5a), 1);
            var e = SyntheticExecution.Prepare(m, [0x4e72, immediate]);
            var initialD = (uint[])e.D.Clone(); var initialA = (uint[])e.A.Clone();
            var vector = !supervisor || resultS == 0 ? 8 : trace != 0 ? 9 : 0;
            var boundarySr = vector == 8 ? sr : immediate;
            var boundaryPc = vector == 8 ? SyntheticMachine.Code : SyntheticMachine.Code + 4;
            e.ControlChecks["PCR preserved"] = (s => s.M68060ProcessorConfiguration, 0x04300000);
            if (vector == 8) SyntheticExecution.ExpectException(m, e, 8);
            else
            {
                SyntheticSystemTests.ApplyStatus(m, e, immediate);
                if (vector == 9) SyntheticExecution.ExpectException(m, e, 9, e.Pc);
                else e.Stopped = true;
            }
            if (vector != 0)
            {
                e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, boundarySr);
                e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, boundaryPc);
            }
            var id = $"68060/STOP/{matrix}/super={supervisor}/M={master:X4}/T={trace:X4}/S={resultS:X4}/newM={resultM:X4}/newT={resultT:X4}/IPL={ipl}";
            var sample = $"/op=4E72/ccr={ccr:X2}/newCcr={resultCcr:X2}";
            var valid = M68040StackFixture.Step(m, e, report, id + "/execute" + sample, batch);
            var actualVector = Math.Max(0, m.Core.State.LastExceptionVector);
            var actualSr = actualVector == 0 ? m.Core.State.StatusRegister : m.Core.State.LastExceptionStatusRegister;
            var actualPc = actualVector == 0 ? m.Core.State.ProgramCounter : m.Core.State.LastExceptionStackedProgramCounter;
            var fields = new uint[] { index++, sr, immediate, (uint)vector, boundarySr, boundaryPc,
                (uint)actualVector, actualSr, actualPc, 0x7800, 0x4700 }.Concat(initialD).Concat(initialA);
            rows.WriteLine(string.Join(' ', fields.Select(v => v.ToString("X8"))));
            if (vector == 0)
            {
                if (!valid) report.Record(id + "/inert" + sample, "untested", "STOP prerequisite failed; no retry");
                else
                {
                    // Architectural stopped state, independent of idle timing.
                    if (batch)
                    {
                        var boundary = new Boundary();
                        var count = ((Copper68k.IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 1000, boundary);
                        // The existing batch API counts an idle attempt as one
                        // logical step. It must preserve architectural state;
                        // this count is not physical instruction retirement.
                        var mismatch = count != 1 || boundary.Before != 1 || boundary.After != 1
                            ? $"Stopped batch count/callbacks differ: {count}/{boundary.Before}/{boundary.After}"
                            : e.Verify(m);
                        report.Record(id + "/inert" + sample, mismatch == null ? "passing" : "mismatching", mismatch);
                    }
                    else
                    {
                        m.Core.ExecuteInstruction();
                        var mismatch = e.Verify(m);
                        report.Record(id + "/inert" + sample, mismatch == null ? "passing" : "mismatching", mismatch);
                    }
                }
            }
        }
        Assert.Equal(163840u, index);
        report.Complete(output);
    }
    private sealed class Boundary : Copper68k.IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
