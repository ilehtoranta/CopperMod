using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.3, 8.4.6.2/7. Synthetic suspended delivery is an input,
// not an arithmetic oracle or qualification of real access-fault frame entry.
public sealed class SyntheticM68040FpuContinuationTests(ITestOutputHelper output)
{
    [Fact, Trait("Suite", "Synthetic")]
    public void UnimplementedContinuationConvertsToFormatTwo() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void PostInstructionContinuationUsesTheOriginalVector() => Audit(true);

    private void Audit(bool post)
    {
        var report = new CoverageBatch("68040", post ? "rte-access-fpu-post" : "rte-access-fpu-unimplemented");
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        foreach (var vector in post ? new[] {49, 50, 51, 52, 53, 54, 55} : new[] {11})
        foreach (var bank in new[] {"user", "ISP", "MSP"})
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var trace in new ushort[] {0, 0x8000, 0x4000})
        foreach (var vbr in new[] {0u, 0x10000u})
        foreach (var ea in new[] {0x5ffau, 0x12345678u})
        foreach (var access in new ushort[] {0, 0x0105, 0x0505, 0x0905})
        {
            var (e, frame, sr) = SyntheticM68040AccessFrameAuditTests.Prepare(m, 7, bank, ccr, trace,
                vbr, ea, (ushort)(access | (post ? 0x8000 : 0x4000)));
            const uint target = 0x6000, handler = 0x9090;
            var format = post ? 3 : 2;
            // A prior event selected this vector before delivery was suspended.
            // Handler-visible FPU registers deliberately suggest another event.
            m.Core.State.M68040PendingFpuExceptions.Begin(format, vector, target);
            m.Core.State.M68040Fpu.Fpcr = 0;
            m.Core.State.M68040Fpu.Fpsr = 0x08008198;
            m.Core.State.M68040Fpu.Fpiar = 0x1234abcd;
            m.InitializePhysical(vbr + (uint)vector * 4, handler, 4);
            e.Write(vbr + (uint)vector * 4, handler, 4, m.Model);
            e.ControlChecks["FPCR preserved"] = (s => s.M68040Fpu.Fpcr, 0);
            e.ControlChecks["FPSR preserved"] = (s => s.M68040Fpu.Fpsr, 0x08008198);
            e.ControlChecks["FPIAR preserved"] = (s => s.M68040Fpu.Fpiar, 0x1234abcd);
            e.ControlChecks["pending delivery consumed"] = (s => s.M68040PendingFpuExceptions.Find(format, target) == null ? 0u : 1u, 0);
            var sequence = m.Core.State.ExceptionSequence;
            e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 1);
            e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, target);
            e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
            e.Sr = (ushort)((sr | 0x2000) & ~0xc000);
            e.A[7] = frame + 48; e.InactiveStackPointer = 0x7800;
            if (bank == "MSP") e.MasterStackPointer = frame + 48;
            e.Write(frame + 48, sr, 2, m.Model);
            e.Write(frame + 50, target, 4, m.Model);
            e.Write(frame + 54, (uint)(format << 12 | vector * 4), 2, m.Model);
            e.Write(frame + 56, ea, 4, m.Model);
            e.Pc = handler; e.ExceptionVector = vector;
            var id = $"68040/RTE/format7/{(post ? "CP" : "CU")}/vector={vector}/bank={bank}/T={trace:X4}/VBR={vbr:X8}/EA={ea:X8}/SSW={access:X4}/op=4E73/ccr={ccr:X2}";
            if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/RTE"))
            { report.Record(id + "/return", "untested", "RTE prerequisite failed"); report.Record(id + "/following-BRA", "untested", "RTE prerequisite failed"); continue; }
            SyntheticM68040AccessFrameAuditTests.Restore(e, bank, frame + 60, sr);
            e.Pc = target; e.ExceptionVector = null;
            if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/return"))
            { report.Record(id + "/following-BRA", "untested", "Handler return prerequisite failed"); continue; }
            // A handler may separately service the original trace. Here it simply
            // returns; the restored trace bits apply to the following self-BRA.
            if (trace != 0)
            {
                e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence + 2);
                e.Sr = (ushort)((sr | 0x2000) & ~0xc000);
                e.A[7] = frame + 48; e.InactiveStackPointer = 0x7800;
                if (bank == "MSP") e.MasterStackPointer = frame + 48;
                e.Write(frame + 48, sr, 2, m.Model); e.Write(frame + 50, target, 4, m.Model);
                e.Write(frame + 54, 0x2024, 2, m.Model); e.Write(frame + 56, target, 4, m.Model);
                e.Pc = handler; e.ExceptionVector = 9;
            }
            SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/following-BRA");
        }
        report.Complete(output);
    }
}
