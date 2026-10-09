using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Pinned WinUAE changes N/Z/V before format rejection. The manual does not
// establish those failure-path flags. This software disagreement is discovery,
// not a promoted architectural expectation or a full native exception oracle.
public sealed class M68010RteFormatDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_010_RTE_FORMAT_DISCOVERY";
    [EnvironmentFact(Enable, "compare 68010 rejected RTE formats with pinned WinUAE"), Trait("Suite", "ReferenceDiscovery")]
    public void InvalidFormatWordsAndConditionCodesScalar() => Audit(false);
    [EnvironmentFact(Enable, "compare 68010 rejected RTE formats with pinned WinUAE"), Trait("Suite", "ReferenceDiscovery")]
    public void InvalidFormatWordsAndConditionCodesBatch() => Audit(true);

    private void Audit(bool batch)
    {
        var group = "rte-format-discovery-" + (batch ? "batch" : "scalar");
        var report = new CoverageBatch("68010", group);
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        var export = Environment.GetEnvironmentVariable("COPPER68K_010_RTE_FORMAT_EXPORT");
        if (string.IsNullOrWhiteSpace(export)) throw new InvalidOperationException("RTE format discovery requires an export directory");
        using var rows = new StreamWriter(Path.Combine(export, group + ".rows"));
        uint index = 0;
        foreach (var matrix in new[] { "words", "ccr" })
        foreach (var format in Enumerable.Range(0, 65536).Where(f => f >> 12 is not (0 or 8)))
        {
            if (matrix == "ccr" && (format & 4095) is not (0 or 4 or 0x24 or 0x3fc or 0x7fc or 0xfff)) continue;
            foreach (var supervisor in new[] { false, true })
            foreach (var trace in matrix == "words" ? new[] { 0 } : new[] { 0, 0x8000 })
            foreach (var stackedSr in matrix == "words" ? new[] { 0xa71f } : new[] { 0, 31, 0x2000, 0x201f, 0x8000, 0x801f, 0xa000, 0xa01f })
            foreach (var target in matrix == "words" ? new[] { 0xffff6001u } : new[] { 0x6000u, 0x6001u, 0xffff6000u, 0xffff6001u })
            foreach (var ccr in matrix == "words" ? new[] { 0, 31 } : Enumerable.Range(0, 32))
            {
                m.Reset(ccr, supervisor);
                m.Core.State.StatusRegister |= (ushort)trace;
                var fp = m.Core.State.A[7];
                foreach (var stack in new[] { 0x4700u, 0x7800u, 0x8000u })
                    for (var offset = -20; offset < 64; offset++) m.InitializePhysical(unchecked(stack + (uint)offset), (uint)(offset ^ 0x5a), 1);
                m.InitializePhysical(fp, (uint)stackedSr, 2);
                m.InitializePhysical(fp + 2, target, 4);
                m.InitializePhysical(fp + 6, (uint)format, 2);
                var e = SyntheticExecution.Prepare(m, [0x4e73]);
                var incoming = e.Sr;
                var initialUsp = m.Core.State.UserStackPointer;
                var initialSsp = m.Core.State.SupervisorStackPointer;
                var vector = supervisor ? 14 : 8;
                // These are independently encoded software-source expectations.
                // X/C are preserved, N is the format sign, Z/V are cleared.
                var boundarySr = supervisor ? (ushort)((incoming & ~14) | ((format & 0x8000) != 0 ? 8 : 0)) : incoming;
                e.Sr = boundarySr;
                // Check every other bit/register/memory location even when N/Z/V
                // disagree. Compare these three bits explicitly after verification.
                e.DefinedSrMask = 0xfff1;
                SyntheticExecution.ExpectException(m, e, vector);
                e.ControlChecks["saved SR other bits"] = (s => (uint)(s.LastExceptionStatusRegister & 0xfff1), (uint)(boundarySr & 0xfff1));
                e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
                e.ControlChecks["stack model"] = (s => s.M68020StackModeEnabled ? 1u : 0u, 0);
                var id = $"68010/RTE/{matrix}/format={format:X4}/super={supervisor}/T={trace:X4}/stackedSR={stackedSr:X4}/target={target:X8}/op=4E73/ccr={ccr:X2}";
                string? mismatch;
                var status = "passing";
                var invariantsMatch = false;
                try
                {
                    if (batch)
                    {
                        var boundary = new Boundary();
                        var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, boundary);
                        mismatch = count != 1 || boundary.Before != 1 || boundary.After != 1 ? "RTE batch count/boundaries differ" : e.Verify(m);
                    }
                    else { m.Core.ExecuteInstruction(); mismatch = e.Verify(m); }
                    if (mismatch == null)
                    {
                        var reads = m.Bus.Accesses.Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= fp && a.Address < fp + 58).ToArray();
                        var expected = supervisor ? new[] { (fp, 2), (fp + 2, 4), (fp + 6, 2) } : [];
                        if (!reads.Select(a => (a.Address, a.Width)).SequenceEqual(expected)) mismatch = "Rejected frame read set/order differs from repository transport policy";
                        if (m.Bus.Accesses.Any(a => a.Address == m.Model.Physical(target))) mismatch = "Rejected RTE fetched stacked target";
                        var actualSaved = m.Core.State.LastExceptionStatusRegister;
                        if (m.PeekPhysical(e.A[7], 2) != actualSaved || m.Core.State.StatusRegister != ((actualSaved | 0x2000) & ~0x8000))
                            mismatch = "Rejected RTE saved/current SR consistency differs";
                        invariantsMatch = mismatch == null;
                        if (mismatch == null && actualSaved != boundarySr)
                            mismatch = $"Rejected RTE software-reference saved SR expected {boundarySr:X4}, actual {actualSaved:X4}";
                    }
                    if (mismatch != null) status = "mismatching";
                }
                catch (Exception ex) when (ex is UnsupportedM68kOpcodeException or UnsupportedM68kTimingException)
                { status = "unsupported"; mismatch = ex.Message; }
                catch (Exception ex) { status = "mismatching"; mismatch = ex.ToString(); }
                report.Record(id, status, mismatch);
                var state = m.Core.State;
                var actualVector = Math.Max(0, state.LastExceptionVector);
                var saved = actualVector == 0 ? state.StatusRegister : state.LastExceptionStatusRegister;
                var fields = new uint[] { index++, incoming, (uint)stackedSr, target, (uint)format, (uint)vector, boundarySr,
                    (uint)actualVector, saved, state.LastExceptionStackedProgramCounter, state.A[7], state.ProgramCounter,
                    status == "passing" ? 1u : 0u, initialUsp, initialSsp, invariantsMatch ? 1u : 0u };
                rows.WriteLine(string.Join(' ', fields.Select(v => v.ToString("X8"))));
            }
        }
        Assert.Equal(573440u, index);
        report.Complete(output);
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
