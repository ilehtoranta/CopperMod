using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticExceptionControlTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void RteFramesPrivilegeStackSelectionAndInvalidFormats(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-rte");
        foreach (var supervisor in new[] { false, true })
        foreach (var restoredSupervisor in new[] { false, true })
        for (var format = 0; format < 16; format++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr, supervisor); var sp = m.Core.State.A[7]; var sr = (ushort)((restoredSupervisor ? 0x2000 : 0) | (ccr ^ 31));
            m.InitializePhysical(sp, sr, 2); m.InitializePhysical(sp + 2, 0x6000, 4); m.InitializePhysical(sp + 6, (uint)(format << 12 | 0x24), 2);
            if (m.Model.FullIndex && modelId != "68060" && format == 1)
            {
                var second = restoredSupervisor ? sp + 8 : 0x7800u;
                m.InitializePhysical(second, sr, 2); m.InitializePhysical(second + 2, 0x6000, 4); m.InitializePhysical(second + 6, 0x24, 2);
            }
            m.InitializePhysical(0x6000, 0x4e71, 2); m.InitializePhysical(0x6002, 0x4e71, 2);
            var e = SyntheticExecution.Prepare(m, [0x4e73]);
            // Fault restart and coprocessor internal-state restoration remain in retained specialist suites.
            var outside = modelId == "68040" ? format == 7 :
                m.Model.FullIndex && modelId != "68060" && format is 9 or 10 or 11;
            if (outside && supervisor) continue;
            var size = modelId == "68000" ? 6u : format == 0 ? 8u : modelId == "68010" && format == 8 ? 58u :
                m.Model.FullIndex && modelId != "68060" && format == 1 ? 8u : m.Model.FullIndex && format == 2 ? 12u : modelId is "68040" or "68060" && format == 3 ? 12u : modelId == "68060" && format == 4 ? 16u : 0;
            if (!supervisor) SyntheticExecution.ExpectException(m, e, 8);
            else if (size == 0) SyntheticExecution.ExpectException(m, e, 14);
            else
            {
                e.A[7] = sp + size; SyntheticSystemTests.ApplyStatus(m, e, sr);
                if (m.Model.FullIndex && modelId != "68060" && format == 1) e.A[7] += 8;
                e.Pc = 0x6000;
            }
            SyntheticExecution.Run(m, e, report, $"{modelId}/RTE/frame{format:X}/super={supervisor}/restoreSuper={restoredSupervisor}/op=4E73/ccr={ccr:X2}");
        }
        if (m.Model.FullIndex && modelId != "68060")
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr); var isp = m.Core.State.A[7];
            m.Core.State.SetMasterStackPointer(0x7400);
            m.InitializePhysical(isp, 0x3000u | (uint)ccr, 2); m.InitializePhysical(isp + 2, 0x6100, 4); m.InitializePhysical(isp + 6, 0x1024, 2);
            m.InitializePhysical(0x7400, 0x3000u | (uint)(ccr ^ 31), 2); m.InitializePhysical(0x7402, 0x6000, 4); m.InitializePhysical(0x7406, 0x24, 2);
            m.InitializePhysical(0x6000, 0x4e71, 2); m.InitializePhysical(0x6002, 0x4e71, 2);
            var e = SyntheticExecution.Prepare(m, [0x4e73]); m.Core.State.SetMasterStackPointer(0x7400);
            e.Sr = (ushort)(0x3000 | (ccr ^ 31)); e.A[7] = 0x7408; e.MasterStackPointer = 0x7408; e.Pc = 0x6000;
            e.ControlChecks["ISP after throwaway"] = (s => s.InterruptStackPointer, isp + 8);
            SyntheticExecution.Run(m, e, report, $"{modelId}/RTE/frame1-to-frame0/MSP/op=4E73/ccr={ccr:X2}");
        }
        report.Complete(output);
    }
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void TraceRetirementTakenAndUntakenFlowStopsAndAbortingFaults(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-trace");
        foreach (var trace in m.Model.FullIndex && modelId != "68060" ? new[] { 0x8000, 0x4000 } : new[] { 0x8000 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var family in new[] { "NOP", "BRA", "BNE", "DBF", "STOP", "MOVEtoSR", "TRAP", "TRAPV", "ILLEGAL" })
        {
            m.Reset(ccr, supervisor); m.Core.State.D[0] = 1;
            var words = family switch { "NOP" => new ushort[] { 0x4e71 }, "BRA" => [0x6010], "BNE" => [0x6610], "DBF" => [0x51c8, 0x0010],
                "STOP" => [0x4e72, 0x2700], "MOVEtoSR" => [0x46fc, 0x2700], "TRAP" => [0x4e43], "TRAPV" => [0x4e76], _ => [0x4afc] };
            m.InitializePhysical(0x1012, 0x4e71, 2); m.InitializePhysical(0x1014, 0x4e71, 2);
            var e = SyntheticExecution.Prepare(m, words); m.Core.State.StatusRegister |= (ushort)trace; e.Sr |= (ushort)trace;
            var abort = family == "ILLEGAL" || family is "STOP" or "MOVEtoSR" && !supervisor;
            var flow = family == "BRA" || family == "BNE" && (ccr & 4) == 0 || family == "DBF";
            if (flow) { e.Pc = 0x1012; if (family == "DBF") e.D[0] = 0; }
            if (family is "STOP" or "MOVEtoSR" && supervisor) { SyntheticSystemTests.ApplyStatus(m, e, 0x2700); flow = true; }
            if (abort) SyntheticExecution.ExpectException(m, e, family == "ILLEGAL" ? 4 : 8);
            else
            {
                if (family == "TRAP" || family == "TRAPV" && (ccr & 2) != 0)
                { SyntheticExecution.ExpectException(m, e, family == "TRAP" ? 35 : 7, e.Pc); flow = true; }
                if (trace == 0x8000 || flow) SyntheticExecution.ExpectException(m, e, 9, e.Pc);
            }
            SyntheticExecution.Run(m, e, report, $"{modelId}/trace-{family}/none/T={trace:X4}/super={supervisor}/op={words[0]:X4}/ccr={ccr:X2}", false);
        }
        foreach (var trace in m.Model.FullIndex && modelId != "68060" ? new[] { 0x8000, 0x4000 } : new[] { 0x8000 })
        foreach (var afterFirst in new[] { false, true })
        foreach (var selfBranch in new[] { false, true })
        {
            m.Reset(31); var e = SyntheticExecution.Prepare(m, selfBranch ? [0x60fe] : new ushort[] { 0x4e71, 0x60fc });
            e.Sr |= (ushort)trace;
            var tracedAddress = selfBranch ? SyntheticMachine.Code : afterFirst || trace == 0x4000 ? SyntheticMachine.Code + 2 : SyntheticMachine.Code;
            var savedPc = tracedAddress == SyntheticMachine.Code && !selfBranch ? SyntheticMachine.Code + 2 : SyntheticMachine.Code;
            SyntheticExecution.ExpectException(m, e, 9, savedPc);
            if (m.Model.FullIndex) e.Write(e.A[7] + 8, tracedAddress, 4, m.Model);
            var boundary = new TraceBoundary(m.Core.State, (ushort)trace, afterFirst);
            ((IM68kBatchCore)m.Core).ExecuteInstructions(3, null, boundary);
            var mismatch = e.Verify(m);
            report.Record($"{modelId}/trace-batch/none/T={trace:X4}/afterFirst={afterFirst}/selfBranch={selfBranch}", mismatch == null ? "passing" : "mismatching", mismatch);
        }
        report.Complete(output);
    }
    private sealed class TraceBoundary(M68kCpuState state, ushort trace, bool afterFirst) : IM68kInstructionBoundary
    {
        private int retired;
        public bool BeforeInstruction()
        {
            if (state.LastExceptionVector == 9) return false;
            if (!afterFirst && retired == 0) state.StatusRegister |= trace;
            return true;
        }
        public void AfterInstruction(long previousCycle, long currentCycle)
        {
            retired++;
            if (afterFirst && retired == 1) state.StatusRegister |= trace;
        }
    }
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void InterruptMasksStopWakeVectorBaseAndMasterFrames(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-interrupt");
        for (var mask = 0; mask < 8; mask++)
        for (var level = 1; level < 8; level++)
        foreach (var supervisor in new[] { false, true })
        foreach (var stopped in new[] { false, true })
        {
            m.Reset(31, supervisor); var e = SyntheticExecution.Prepare(m, [0x4e71]); e.Pc = SyntheticMachine.Code;
            m.Core.State.StatusRegister = (ushort)((supervisor ? 0x2000 : 0) | mask << 8 | 31); e.Sr = m.Core.State.StatusRegister;
            m.Core.State.Stopped = stopped; e.Stopped = stopped;
            var accepted = level > mask || level == 7;
            if (accepted)
            {
                SyntheticExecution.ExpectException(m, e, 28, SyntheticMachine.Code); e.Sr = (ushort)((e.Sr & 0xf8ff) | level << 8); e.Stopped = false;
            }
            try { m.Core.RequestInterrupt(level, 28 * 4); var mismatch = e.Verify(m); report.Record($"{modelId}/interrupt/none/mask={mask}/level={level}/super={supervisor}/stop={stopped}", mismatch == null ? "passing" : "mismatching", mismatch); }
            catch (Exception ex) { report.Record($"{modelId}/interrupt/mask={mask}/level={level}", "mismatching", ex.Message); }
        }
        // The interrupt API takes a vector-table offset. VBR must relocate it.
        if (modelId != "68000")
        foreach (var supervisor in new[] { false, true })
        {
            m.Reset(31, supervisor); _ = SyntheticExecution.Prepare(m, [0x4e71]);
            m.Core.State.VectorBaseRegister = 0x20000; m.Core.State.StatusRegister = (ushort)((supervisor ? 0x2000 : 0) | 31);
            m.InitializePhysical(0x20000 + 28 * 4, 0x6000, 4);
            var e = ArchitecturalExpectation.Capture(m);
            SyntheticExecution.ExpectException(m, e, 28, SyntheticMachine.Code); e.Sr = (ushort)((e.Sr & 0xf8ff) | 0x300);
            m.Core.RequestInterrupt(3, 28 * 4); var mismatch = e.Verify(m);
            report.Record($"{modelId}/interrupt/none/VBR/super={supervisor}", mismatch == null ? "passing" : "mismatching", mismatch);
        }
        if (m.Model.FullIndex && modelId != "68060")
        foreach (var trace in new[] { 0, 0x8000, 0x4000 })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr); _ = SyntheticExecution.Prepare(m, [0x4e71]); var isp = m.Core.State.A[7];
            m.Core.State.SetMasterStackPointer(0x7400); m.Core.State.StatusRegister = (ushort)(trace | 0x3000 | ccr);
            var e = ArchitecturalExpectation.Capture(m); var saved = e.Sr;
            e.MasterStackPointer = 0x73f8; e.A[7] = isp - 8; e.Sr = (ushort)(0x2300 | ccr); e.Pc = 0x91c0; e.ExceptionVector = 28;
            foreach (var (frame, format) in new[] { (0x73f8u, 0x70u), (isp - 8, 0x1070u) })
            { e.Write(frame, saved, 2, m.Model); e.Write(frame + 2, SyntheticMachine.Code, 4, m.Model); e.Write(frame + 6, format, 2, m.Model); }
            m.Core.RequestInterrupt(3, 28 * 4); var mismatch = e.Verify(m);
            report.Record($"{modelId}/interrupt/none/master/T={trace:X4}/ccr={ccr:X2}", mismatch == null ? "passing" : "mismatching", mismatch);
            if (mismatch == null)
            {
                // Return from the handler through ISP throwaway and MSP frame.
                m.InitializePhysical(e.Pc, 0x4e73, 2); e.Write(e.Pc, 0x4e73, 2, m.Model);
                e.A[7] = 0x7400; e.MasterStackPointer = 0x7400; e.Sr = saved; e.Pc = SyntheticMachine.Code; e.ExceptionVector = null;
                e.ControlChecks["ISP restored"] = (state => state.InterruptStackPointer, isp);
                SyntheticExecution.Run(m, e, report, $"{modelId}/RTE/interrupt-master/T={trace:X4}/op=4E73/ccr={ccr:X2}", false);
            }
        }
        report.Complete(output);
    }
}
