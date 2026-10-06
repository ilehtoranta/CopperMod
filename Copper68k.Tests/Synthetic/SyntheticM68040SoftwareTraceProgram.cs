using Copper68k;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.2.6 / 8.3: software inspects the saved trace condition,
// converts the pending frame and directly calls the trace handler. This is
// an integer protocol fixture; no floating-point operation is emulated here.
internal sealed class SyntheticM68040SoftwareTraceProgram(string form, bool clearTrace,
    uint handler, uint target, uint vbr)
{
    internal const uint TraceHandler = 0x9180, Marker = 0x8000, MarkerValue = 0x13579bdf;
    private bool Cu => form.StartsWith("CU", StringComparison.Ordinal);
    private bool Flow => form == "CU-flow";
    internal uint ResumePc => Cu ? target + (Flow ? 4u : 2u) : target;
    // Synthetic instruction metadata: CU starts at target, while CP supplies
    // the start of a completed two-word FMOVE. No FPU opcode is decoded here.
    private uint TraceAddress => Cu ? target : target - 4;
    private uint TestPc => handler + (Cu ? 8u : 0u);
    private uint ServicePc => TestPc + (Flow ? 14u : 8u);
    private bool Eligible(ushort sr) => (sr & 0x8000) != 0 || Flow && (sr & 0x4000) != 0;
    internal ushort ReturnedSr(ushort sr) => clearTrace && Eligible(sr) ? (ushort)(sr & 0x3fff) : sr;

    internal void Initialize(SyntheticMachine m)
    {
        var words = new List<ushort>();
        // Fixed reference encodings: MOVE.L #imm,2(A7); BTST #7/#6,(A7);
        // BNE.s; RTE; MOVE.W #$2024,6(A7); MOVE.L #address,8(A7);
        // MOVE.L (vector).L,-(A7); RTS. The temporary RTS target is not an
        // exception frame. The frame itself remains on the original bank.
        if (Cu) words.AddRange([0x2f7c, (ushort)(ResumePc >> 16), (ushort)ResumePc, 2]);
        words.AddRange([0x0817, 7, (ushort)(Flow ? 0x6608 : 0x6602)]);
        if (Flow) words.AddRange([0x0817, 6, 0x6602]);
        words.Add(0x4e73);
        words.AddRange([0x3f7c, 0x2024, 6,
            0x2f7c, (ushort)(TraceAddress >> 16), (ushort)TraceAddress, 8,
            0x2f39, (ushort)((vbr + 36) >> 16), (ushort)(vbr + 36), 0x4e75, 0x7e7e]);
        for (var n = 0; n < words.Count; n++) m.InitializePhysical(handler + (uint)n * 2, words[n], 2);
        m.InitializePhysical(vbr + 36, TraceHandler, 4);
        ushort[] trace = clearTrace
            ? [0x23fc, (ushort)(MarkerValue >> 16), (ushort)(MarkerValue & 0xffff), 0, (ushort)Marker, 0x0257, 0x3fff, 0x4e73, 0x7e7e]
            : [0x23fc, (ushort)(MarkerValue >> 16), (ushort)(MarkerValue & 0xffff), 0, (ushort)Marker, 0x4e73, 0x7e7e];
        for (var n = 0; n < trace.Length; n++) m.InitializePhysical(TraceHandler + (uint)n * 2, trace[n], 2);
        for (var n = -4; n < 8; n++) m.InitializePhysical(unchecked(Marker + (uint)n), 0xa5, 1);
        m.InitializePhysical(Marker, 0, 4);
        m.InitializePhysical(ResumePc, 0x60fe, 2);
        m.InitializePhysical(ResumePc + 2, 0x7e7e, 2);
    }

    internal IEnumerable<string> Phases(ushort sr)
    {
        if (Cu) yield return "emulate-PC";
        yield return "test-T1"; yield return "branch-T1";
        if (Flow && (sr & 0x8000) == 0) { yield return "test-T0"; yield return "branch-T0"; }
        if (Eligible(sr))
        {
            yield return "trace-format"; yield return "trace-address";
            yield return "trace-vector-push"; yield return "trace-direct-call";
            yield return "trace-marker";
            if (clearTrace) yield return "trace-clear";
        }
        yield return "pending-return";
    }

    // Each expected transition is independent of production decode/EA/flags.
    // All registers, all three stack pointers, frame/guard memory and exception
    // provenance remain verified by the caller at every executed instruction.
    internal bool Run(SyntheticMachine m, ArchitecturalExpectation e, uint frame,
        ushort sr, Func<bool> step)
    {
        var writes = m.Bus.Accesses.Count;
        e.ControlChecks["software marker writes"] = (_ => (uint)m.Bus.Accesses.Skip(writes)
            .Count(a => a.Write && a.Address == Marker), 0);
        if (Cu)
        {
            e.Write(frame + 2, ResumePc, 4, m.Model);
            e.Sr = SyntheticExecution.MoveFlags(e.Sr, ResumePc, 4); e.Pc = TestPc;
            if (!step()) return false;
        }
        e.Sr = BitFlags(e.Sr, (sr & 0x8000) != 0); e.Pc = TestPc + 4;
        if (!step()) return false;
        e.Pc = (sr & 0x8000) != 0 ? ServicePc : TestPc + 6;
        if (!step()) return false;
        if (Flow && (sr & 0x8000) == 0)
        {
            e.Sr = BitFlags(e.Sr, (sr & 0x4000) != 0); e.Pc = TestPc + 10;
            if (!step()) return false;
            e.Pc = (sr & 0x4000) != 0 ? ServicePc : TestPc + 12;
            if (!step()) return false;
        }
        if (!Eligible(sr)) return true; // Caller verifies the untraced RTE.
        e.Write(frame + 6, 0x2024, 2, m.Model);
        e.Sr = SyntheticExecution.MoveFlags(e.Sr, 0x2024, 2); e.Pc = ServicePc + 6;
        if (!step()) return false;
        e.Write(frame + 8, TraceAddress, 4, m.Model);
        e.Sr = SyntheticExecution.MoveFlags(e.Sr, TraceAddress, 4); e.Pc = ServicePc + 14;
        if (!step()) return false;
        SetActiveStack(e, frame - 4);
        e.Write(frame - 4, TraceHandler, 4, m.Model);
        e.Sr = SyntheticExecution.MoveFlags(e.Sr, TraceHandler, 4); e.Pc = ServicePc + 20;
        if (!step()) return false;
        SetActiveStack(e, frame); e.Pc = TraceHandler;
        if (!step()) return false;
        e.Write(Marker, MarkerValue, 4, m.Model);
        e.Sr = SyntheticExecution.MoveFlags(e.Sr, MarkerValue, 4); e.Pc = TraceHandler + 10;
        e.ControlChecks["software marker writes"] = (_ => (uint)m.Bus.Accesses.Skip(writes)
            .Count(a => a.Write && a.Address == Marker), 1);
        if (!step()) return false;
        if (clearTrace)
        {
            var value = (ushort)(sr & 0x3fff);
            e.Write(frame, value, 2, m.Model);
            e.Sr = (ushort)((e.Sr & 0xfff0) | (value == 0 ? 4 : 0)); e.Pc = TraceHandler + 14;
            if (!step()) return false;
        }
        return true; // Caller verifies real RTE and restored stack selection.
    }

    private static ushort BitFlags(ushort sr, bool set) => (ushort)((sr & ~4) | (set ? 0 : 4));
    private static void SetActiveStack(ArchitecturalExpectation e, uint sp)
    {
        e.A[7] = sp;
        var bank = (e.Sr & 0x1000) != 0 ? "MSP" : "ISP";
        e.ControlChecks[bank] = (e.ControlChecks[bank].Read, sp);
        if (bank == "MSP") e.MasterStackPointer = sp;
    }
}
