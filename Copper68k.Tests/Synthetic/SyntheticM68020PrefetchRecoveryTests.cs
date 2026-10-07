using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68020PrefetchRecoveryTests(ITestOutputHelper output)
{
    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void RepairedPipelineRunsWithoutOddBusFetch(bool batch) => Audit(batch, false);

    [EnvironmentFact("COPPER68K_RUN_020_PREFETCH_RECOVERY", "qualify executed 020/030 prefetch repair and RTE"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarRecoveryMatrix() => Audit(false, true);

    [EnvironmentFact("COPPER68K_RUN_020_PREFETCH_RECOVERY", "qualify executed 020/030 prefetch repair and RTE"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchRecoveryMatrix() => Audit(true, true);

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleVersionPreservesOriginalFrame(bool batch) => Refusal(batch, true);

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void UnclearedRerunReentersAddressHandlerWithoutInstructionReplay(bool batch) => Refusal(batch, false);

    private void Refusal(bool batch, bool version)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(x => x.Id is "68020" or "68030" or "68EC020" or "A1200"))
        {
            var m = new SyntheticMachine(model);
            var report = new CoverageBatch(model.Id, "prefetch-" + (version ? "version-" : "rerun-") + (batch ? "batch" : "scalar"));
            foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
            foreach (var revision in version ? Enumerable.Range(1, 15) : new[] { 0 })
            {
                var id = $"{model.Id}/prefetch-{(version ? "version" : "rerun")}/bank={bank}/revision={revision:X}/op=4E73/ccr=1F";
                var phase = "entry";
                try
                {
                    m.Reset(31); m.InitializePhysical(0x9030, 0x4e73, 2);
                    _ = SyntheticExecution.Prepare(m, [0x4e71]);
                    m.Core.State.SetUserStackPointer(0x7800); m.Core.State.SetInterruptStackPointer(0x4700); m.Core.State.SetMasterStackPointer(0x7400);
                    m.Core.State.StatusRegister = (ushort)(0x71f | (bank is "ISP" or "MSP" ? 0x2000 : 0) | (bank is "user-M" or "MSP" ? 0x1000 : 0));
                    m.Core.State.SetActiveStackPointer(bank == "MSP" ? 0x7400u : bank == "ISP" ? 0x4700u : 0x7800u);
                    m.Core.State.ProgramCounter = 0x6001;
                    Step(m, batch);
                    var frame = m.Core.State.A[7];
                    if (m.PeekPhysical(frame+6,2) != 0xb00c || m.PeekPhysical(frame+10,2) != 0x3000 || m.Core.State.ProgramCounter != 0x9030)
                        throw new InvalidOperationException("required prefetch frame was not delivered");
                    if (version) m.InitializePhysical(frame+0x36, (uint)revision << 12, 2);
                    var expected = ArchitecturalExpectation.Capture(m);
                    var serial = m.Core.State.ExceptionSequence;
                    if (version)
                    {
                        SyntheticExecution.ExpectException(m, expected, 14, 0x9030);
                        if ((expected.Sr & 0x1000) != 0) expected.MasterStackPointer = expected.A[7];
                    }
                    m.Bus.Accesses.Clear();
                    phase = "RTE";
                    Step(m, batch); Check(m, expected, phase);
                    if (m.Core.State.ExceptionSequence != serial+1 || m.Core.State.LastExceptionVector != (version ? 14 : 3))
                        throw new InvalidOperationException("refusal exception sequence/vector differs");
                    if (version)
                    {
                        var reads = m.Bus.Accesses.Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= frame && a.Address < frame+92)
                            .Select(a => (a.Address,a.Width)).ToArray();
                        (uint,int)[] required = [(frame,2),(frame+2,4),(frame+6,2),(frame+0x36,2)];
                        if (!reads.SequenceEqual(required)) throw new InvalidOperationException("version validation transport order differs");
                    }
                    if (m.Bus.Accesses.Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && (a.Address & 1) != 0))
                        throw new InvalidOperationException("refusal issued an odd instruction bus read");
                    report.Record(id, "passing", null);
                }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
                { report.Record(id, "unsupported", phase+": "+ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", phase+": "+ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private void Audit(bool batch, bool matrix)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(x => x.Id is "68020" or "68030" or "68EC020" or "A1200"))
        {
            var m = new SyntheticMachine(model);
            var report = new CoverageBatch(model.Id, "prefetch-recovery-" + (matrix ? "matrix-" : "witness-") + (batch ? "batch" : "scalar"));
            foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
            foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
            foreach (var target in new[] { 0x6001u, 0x10006001u })
            foreach (var ccr in matrix ? Enumerable.Range(0, 32) : new[] { 31 })
            {
                Case(m, report, batch, bank, trace, target, ccr, false);
                if (trace == 0) Case(m, report, batch, bank, trace, target, ccr, true);
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void Case(SyntheticMachine m, CoverageBatch report, bool batch, string bank, ushort trace, uint target, int ccr, bool twoStage)
    {
        var phase = "entry";
        var id = $"{m.Model.Id}/prefetch-recovery/bank={bank}/T={trace:X4}/target={target:X8}/sequence={(twoStage ? "nop-branch" : "single-branch")}/op=4E73/ccr={ccr:X2}";
        try
        {
            m.Reset(ccr);
            // MOVE.W #BRA,12(A7); MOVE.W #NOP,14(A7); ANDI.W #CFFF,10(A7); RTE.
            ushort[] handler = [0x3f7c,0x6001,0x000c,0x3f7c,0x4e71,0x000e,0x026f,0xcfff,0x000a,0x4e73];
            if (twoStage) { handler[1] = 0x4e71; handler[4] = 0x6001; }
            for (var n = 0; n < handler.Length; n++) m.InitializePhysical(0x9030u + (uint)n * 2, handler[n], 2);
            // Clear trace in the saved SR, then return from the real trace frame.
            m.InitializePhysical(0x9090, 0x0257, 2); m.InitializePhysical(0x9092, 0x3fff, 2); m.InitializePhysical(0x9094, 0x4e73, 2);
            var branchTarget = unchecked(target + (twoStage ? 5u : 3u));
            m.InitializePhysical(branchTarget, 0x7c2a, 2); m.InitializePhysical(branchTarget + 2, 0x4e71, 2);
            _ = SyntheticExecution.Prepare(m, [0x4e71]);
            m.Core.State.SetUserStackPointer(0x7800); m.Core.State.SetInterruptStackPointer(0x4700); m.Core.State.SetMasterStackPointer(0x7400);
            m.Core.State.StatusRegister = (ushort)(trace | 0x700 | ccr | (bank is "ISP" or "MSP" ? 0x2000 : 0) |
                (bank is "user-M" or "MSP" ? 0x1000 : 0));
            m.Core.State.SetActiveStackPointer(bank == "MSP" ? 0x7400u : bank == "ISP" ? 0x4700u : 0x7800u);
            m.Core.State.ProgramCounter = target;
            m.Bus.Accesses.Clear();
            var expected = ArchitecturalExpectation.Capture(m);
            var initialSr = expected.Sr;
            var master = (initialSr & 0x1000) != 0;
            var frame = (master ? 0x7400u : 0x4700u) - 92;
            SyntheticSystemTests.ApplyStatus(m, expected, (ushort)((initialSr | 0x2000) & ~0xc000));
            expected.A[7] = frame;
            if (master) expected.MasterStackPointer = frame;
            expected.Pc = 0x9030;
            // Opaque internal values are not silicon expectations. Defined
            // fields are checked independently, then opaque bytes are frozen
            // solely to detect unintended handler/RTE memory modifications.
            for (uint offset = 0; offset < 92; offset++) expected.MemoryMasks[m.Model.Physical(frame + offset)] = 0;
            foreach (var (offset, value, width) in new[] {
                (0u,(uint)initialSr,2),(2u,target,4),(6u,0xb00cu,2),(10u,0x3000u,2),(0x24u,unchecked(target+2),4) })
            {
                expected.Write(frame + offset, value, width, m.Model);
                for (uint n = 0; n < width; n++) expected.MemoryMasks[m.Model.Physical(frame + offset + n)] = 255;
            }
            Step(m, batch); Check(m, expected, phase);
            for (uint offset = 0; offset < 92; offset++)
            {
                var address = m.Model.Physical(frame + offset);
                expected.Memory[address] = m.Bus.Peek(address);
                expected.MemoryMasks.Remove(address);
            }
            phase = "repair C";
            expected.Write(frame + 12, twoStage ? 0x4e71u : 0x6001u, 2, m.Model); expected.Pc += 6;
            expected.Sr = (ushort)(expected.Sr & 0xfff0);
            Step(m, batch); Check(m, expected, phase);
            phase = "repair B";
            expected.Write(frame + 14, twoStage ? 0x6001u : 0x4e71u, 2, m.Model); expected.Pc += 6;
            Step(m, batch); Check(m, expected, phase);
            phase = "clear rerun";
            expected.Write(frame + 10, 0, 2, m.Model); expected.Pc += 6; expected.Sr |= 4;
            Step(m, batch); Check(m, expected, phase);
            phase = "RTE";
            expected.A[7] = frame + 92;
            if (master) expected.MasterStackPointer = frame + 92;
            SyntheticSystemTests.ApplyStatus(m, expected, initialSr); expected.Pc = target;
            if ((initialSr & 0x2000) == 0) expected.InactiveStackPointer = 0x4700;
            Step(m, batch); Check(m, expected, phase);
            if (twoStage)
            {
                phase = "supplied NOP";
                expected.Pc += 2;
                Step(m, batch); Check(m, expected, phase);
            }
            phase = "supplied BRA";
            expected.Pc = branchTarget;
            if (trace != 0)
            {
                SyntheticSystemTests.ApplyStatus(m, expected, (ushort)((initialSr | 0x2000) & ~0xc000));
                expected.A[7] -= 12;
                if (master) expected.MasterStackPointer = expected.A[7];
                expected.Write(expected.A[7], initialSr, 2, m.Model);
                expected.Write(expected.A[7]+2, branchTarget, 4, m.Model);
                expected.Write(expected.A[7]+6, 0x2024, 2, m.Model);
                expected.Write(expected.A[7]+8, target, 4, m.Model);
                expected.Pc = 0x9090;
            }
            Step(m, batch); Check(m, expected, phase);
            if (trace != 0)
            {
                phase = "trace repair";
                expected.Write(expected.A[7], (uint)(initialSr & ~0xc000), 2, m.Model); expected.Pc += 4;
                expected.Sr = (ushort)((expected.Sr & 0xfff0) | ((initialSr & 0x3fff) == 0 ? 4 : 0));
                Step(m, batch); Check(m, expected, phase);
                phase = "trace RTE";
                expected.A[7] += 12; if (master) expected.MasterStackPointer = expected.A[7];
                SyntheticSystemTests.ApplyStatus(m, expected, (ushort)(initialSr & ~0xc000)); expected.Pc = branchTarget;
                if ((initialSr & 0x2000) == 0) expected.InactiveStackPointer = 0x4700;
                Step(m, batch); Check(m, expected, phase);
            }
            phase = "sentinel";
            expected.D[6] = 42; expected.Pc += 2; expected.Sr = (ushort)(expected.Sr & 0xfff0);
            Step(m, batch); Check(m, expected, phase);
            if (m.Bus.Accesses.Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && (a.Address & 1) != 0))
                throw new InvalidOperationException("repaired instruction stream was fetched at an odd bus address");
            report.Record(id, "passing", null);
        }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
        { report.Record(id, "unsupported", phase + ": " + ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", phase + ": " + ex.Message); }
    }

    private static void Check(SyntheticMachine m, ArchitecturalExpectation expected, string phase)
    {
        var mismatch = expected.Verify(m);
        if (mismatch != null) throw new InvalidOperationException(phase + ": " + mismatch);
    }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) { m.Core.ExecuteInstruction(); return; }
        var boundary = new Boundary();
        var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, boundary);
        if (count != 1 || boundary.Before != 1 || boundary.After != 1) throw new InvalidOperationException("instruction boundary count differs");
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
