using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.2.6, 8.4.6.2/7: CM saves the original EA, RTE repeats
// preceding operand transfers, and a suspended instruction traces on completion.
// This is software architectural qualification, not a physical pipeline model.
public sealed class SyntheticM68040MovemReadRecoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_MOVEM_READ_RECOVERY";
    private const uint Handler = 0xa000, TraceHandler = 0xa100, Vbr = 0x900000;
    private static readonly uint[] LongValues = [0x11112222, 0x00000040, 0x66667777, 0x88889999];
    private static readonly uint[] WordValues = [0xffff8001, 0x00000040, 0x00007ffe, 0xffff8888];

    [Fact, Trait("Suite", "ReferenceDiscovery")]
    public void FixedPcSelfReferenceEncodings()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        m.Reset(31); m.Core.State.D[1] = 0xffffffe0;
        var words = new List<ushort> { 0x4cfb, 0x0303 };
        var direct = new AddressingFixture(m, 4, words, m.Core.State.D, m.Core.State.A, 0,
            new(SourceIndex: new(Full: true, IndexRegister: 1, SuppressIndex: true)));
        Assert.Equal(0x1004u, direct.Resolve(new(7, 3), true));
        Assert.Equal(new ushort[] { 0x4cfb, 0x0303, 0x1150 }, words);
        Assert.Equal(0x1006u, direct.NextPc);
        words = [0x4cfb, 0x0303];
        var indirect = new AddressingFixture(m, 4, words, m.Core.State.D, m.Core.State.A, 0,
            new(SourceIndex: new(Full: true, IndexRegister: 1, Indirect: 5)));
        Assert.Equal(0x001fffe0u, indirect.Resolve(new(7, 3), true));
        Assert.Equal(new ushort[] { 0x4cfb, 0x0303, 0x1115 }, words);
        Assert.Equal(0x1004u, indirect.SourcePointerAddress);
        m.InitializePhysical(0x1004, 0x11157e55, 4);
        Assert.Equal(0x11157e35u, indirect.SourcePointerTarget!());
        Assert.Equal(0x1006u, indirect.NextPc);
    }

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedLegalSourceRecoveryWitnesses(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-read-recovery-witness-" + Route(batch));
        foreach (var source in Sources())
        foreach (var width in new[] { 2, 4 })
            Case(report, batch, "witness", source, width, 3, width - 1, "ISP", 0, 31);
        report.Complete(output);
    }

    [EnvironmentFact(Enable, "qualify actual MOVEM read faults and software RTE recovery"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarRecoveryMatrix() => Audit(false);

    [EnvironmentFact(Enable, "qualify actual MOVEM read faults and software RTE recovery"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchRecoveryMatrix() => Audit(true);

    private void Audit(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-read-recovery-discovery-" + Route(batch));
        foreach (var source in Sources())
        foreach (var width in new[] { 2, 4 })
        for (var transfer = 0; transfer < 4; transfer++)
        for (var faultByte = 0; faultByte < width; faultByte++)
            Case(report, batch, "opcode", source, width, transfer, faultByte, "ISP", 0, 31);

        // Full structures are separate from the CCR/bank/trace product.
        foreach (var spec in IndexFixture.FullStructures())
        foreach (var index in new[] {
            spec with { IndexRegister = 1, LongIndex = false, Scale = 0 },
            spec with { IndexRegister = 1, LongIndex = true, Scale = 0 },
            spec with { IndexRegister = 1, LongIndex = false, Scale = 3 },
            spec with { AddressIndex = true, IndexRegister = 0, LongIndex = false, Scale = 2 } })
        foreach (var form in new[] { new OperandForm(6, 1), new OperandForm(7, 3) })
        foreach (var width in new[] { 2, 4 })
        for (var transfer = 0; transfer < 4; transfer++)
        for (var faultByte = 0; faultByte < width; faultByte++)
            Case(report, batch, "structure", new(form, index), width, transfer, faultByte, "ISP", 0, 31);

        Source[] canonical = [new(new(2, 0)), new(new(3, 0)), new(new(3, 7)),
            new(new(5, 0)), new(new(6, 0)), new(new(7, 0)), new(new(7, 1)),
            new(new(7, 2)), new(new(7, 3)),
            new(new(6, 0), new(Full: true, IndexRegister: 1, LongIndex: true, BaseSize: 2, Indirect: 2)),
            new(new(7, 3), new(Full: true, IndexRegister: 1, LongIndex: true, BaseSize: 3, Indirect: 7))];
        foreach (var source in canonical)
        foreach (var width in new[] { 2, 4 })
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        for (var ccr = 0; ccr < 32; ccr++)
        for (var transfer = 0; transfer < 4; transfer++)
        for (var faultByte = 0; faultByte < width; faultByte++)
            Case(report, batch, "status", source, width, transfer, faultByte, bank, trace, ccr);
        report.Complete(output);
    }

    private static string Route(bool batch) => batch ? "batch" : "scalar";
    private sealed record Source(OperandForm Form, IndexFixture? Index = null)
    {
        public string Id => $"mode={Form.Mode}/reg={Form.Register}/index={Index?.Id ?? "brief-default"}" +
            (Index is null ? "" : $"/ix={(Index.AddressIndex ? "A" : "D")}{Index.IndexRegister}/{(Index.LongIndex ? "L" : "W")}/scale={1 << Index.Scale}");
    }
    private static IEnumerable<Source> Sources()
    {
        foreach (var mode in new[] { 2, 3, 5, 6 })
        for (var register = 0; register < 8; register++) yield return new(new(mode, register));
        for (var register = 0; register < 4; register++) yield return new(new(7, register));
    }

    private static void Case(CoverageBatch report, bool batch, string cohort, Source source,
        int width, int transfer, int faultByte, string bank, ushort trace, int ccr)
    {
        var opcode = (ushort)(0x4c80 | (width == 4 ? 0x40 : 0) | source.Form.Mode << 3 | source.Form.Register);
        var id = $"68040/MOVEM/read-recovery/{cohort}/size={width}/{source.Id}/bank={bank}/T={trace:X4}/transfer={transfer}/byte={faultByte}/op={opcode:X4}/ccr={ccr:X2}";
        var phase = "fixture";
        try
        {
            var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
            m.Reset(ccr);
            var sr = (ushort)(0x0700 | M68040StackFixture.Status(bank, trace, ccr));
            var pointers = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
            SetInitialStacks(m, pointers, sr);
            m.Core.State.D[1] = 0xffffffe0; // Signed/scaled index, overwritten by transfer 1.
            if (source.Index?.AddressIndex == true) m.Core.State.A[0] = 0xffffffe0;
            foreach (var top in pointers.Values)
                for (var n = -64; n < 32; n++) m.InitializePhysical(unchecked(top + (uint)n), (uint)(n ^ 0x5a), 1);
            var words = new List<ushort> { opcode, 0x0303 }; // D0/D1/A0/A1, including bases/indexes.
            var index = source.Index ?? new(IndexRegister: 1, LongIndex: true, BriefDisplacement: 64);
            var fixture = new AddressingFixture(m, width, words, (uint[])m.Core.State.D.Clone(),
                (uint[])m.Core.State.A.Clone(), 0, new(SourceIndex: index,
                    SourceAbsoluteWord: 0xf000, SourceAbsoluteLong: 0x12346200));
            var ea = fixture.Resolve(source.Form, true);
            for (var n = 0; n < words.Count; n++) m.InitializePhysical(0x1000 + (uint)n * 2, words[n], 2);
            var next = fixture.NextPc;
            m.InitializePhysical(next, 0x7e55, 2); // MOVEQ #55,D7, no flow trace.
            m.InitializePhysical(next + 2, 0x6002, 2); // BRA.s next+6, must flow trace under T0.
            m.InitializePhysical(next + 4, 0x4afc, 2); m.InitializePhysical(next + 6, 0x4e71, 2);
            // With a null PC displacement, legal operands/pointers can be the
            // extension words themselves. Preserve the instruction bytes and
            // derive those operand values from the test-owned encoded stream.
            if (fixture.SourcePointerTarget is { } pointerTarget) ea = pointerTarget();
            var values = (uint[])(width == 2 ? WordValues : LongValues).Clone();
            for (var n = 0; n < 4; n++)
            {
                var at = unchecked(ea + (uint)(n * width));
                for (var lane = 0; lane < width; lane++)
                {
                    var address = unchecked(at + (uint)lane);
                    if (address >= 0x1000 && address < next + 8) continue;
                    m.InitializePhysical(address, (values[n] >> ((width - lane - 1) * 8)) & 255, 1);
                }
                var raw = m.PeekPhysical(at, width);
                values[n] = width == 2 ? unchecked((uint)(int)(short)raw) : raw;
            }
            m.InitializePhysical(Vbr + 8, Handler, 4); m.InitializePhysical(Vbr + 36, TraceHandler, 4);
            m.InitializePhysical(Handler, 0x4e73, 2); m.InitializePhysical(Handler + 2, 0x4e71, 2);
            // Real trace handler clears stacked T1/T0 and returns to the saved PC.
            m.InitializePhysical(TraceHandler, 0x0257, 2); m.InitializePhysical(TraceHandler + 2, 0x3fff, 2);
            m.InitializePhysical(TraceHandler + 4, 0x4e73, 2); m.InitializePhysical(TraceHandler + 6, 0x4e71, 2);
            m.Start(); SetInitialStacks(m, pointers, sr); m.Core.State.VectorBaseRegister = Vbr;
            var sequence = m.Core.State.ExceptionSequence;
            var e = ArchitecturalExpectation.Capture(m);
            for (var n = 0; n < transfer; n++) SetLoaded(e, n, values[n]);
            var exceptionBank = M68040StackFixture.ExceptionBank(sr);
            var frame = pointers[exceptionBank] - 60;
            pointers[exceptionBank] = frame;
            M68040StackFixture.SetStacks(e, pointers, (ushort)((sr | 0x2000) & ~0xc000));
            e.Pc = Handler; e.ExceptionVector = 2;
            e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 1);
            e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, 0x1000);
            e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
            e.ControlChecks["bypass cleared"] = (s => s.M68040Mmu.BypassTranslation ? 1u : 0u, 0);
            for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
            Define(m, e, frame, sr, 2); Define(m, e, frame + 2, 0x1000, 4);
            Define(m, e, frame + 6, 0x7008, 2); Define(m, e, frame + 8, ea, 4);
            Define(m, e, frame + 12, (uint)(0x1100 | (width == 2 ? 0x40 : 0) | ((sr & 0x2000) == 0 ? 1 : 5)), 2);
            e.MemoryMasks[frame + 13] = 0x7f;
            foreach (var offset in new uint[] { 14, 16, 18 })
            {
                e.Write(frame + offset, 0, 2, m.Model);
                e.MemoryMasks[frame + offset + 1] = 0x80;
            }
            Define(m, e, frame + 20, unchecked(ea + (uint)(transfer * width)), 4);
            bus.Accesses.Clear(); bus.Arm(unchecked(ea + (uint)(transfer * width + faultByte)), M68kBusAccessKind.CpuDataRead);
            phase = "entry"; Step(m, e, batch);
            if (bus.Rejected.Count != 1 || bus.Rejected[0].Width != width) throw new InvalidOperationException("Wrong original-width read rejection");
            pointers[exceptionBank] += 60; M68040StackFixture.SetStacks(e, pointers, sr);
            e.Pc = 0x1000;
            phase = "handler-RTE"; Step(m, e, batch);
            for (var n = 0; n < 4; n++) SetLoaded(e, n, values[n]);
            if (source.Form.Mode == 3)
            {
                var end = unchecked(ea + (uint)(4 * width)); e.A[source.Form.Register] = end;
                if (source.Form.Register == 7) pointers[M68040StackFixture.PhysicalBank(bank)] = end;
            }
            M68040StackFixture.SetStacks(e, pointers, sr); e.Pc = next;
            var resumedSr = sr;
            if (trace == 0x8000) ExpectTrace(m, e, pointers, next, 0x1000, sr, sequence + 2);
            phase = "resumed-MOVEM"; Step(m, e, batch);
            for (var n = 0; n < 4; n++)
            {
                var address = unchecked(ea + (uint)(n * width));
                var count = bus.Accesses.Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address == address && a.Width == width);
                if (count != (n < transfer ? 2 : 1)) throw new InvalidOperationException($"Transfer {n}: repeated-read count {count}");
            }
            if (fixture.SourcePointerAddress is { } pointer &&
                bus.Accesses.Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address == pointer && a.Width == 4) != 1)
                throw new InvalidOperationException("Indirect pointer repeated on CM recovery");
            if (bus.Rejected.Count != 1) throw new InvalidOperationException("Faulting read automatically retried");
            if (trace == 0x8000) { phase = "trace-handler"; ClearTraceAndReturn(m, e, pointers, sr, next, batch); resumedSr &= 0x3fff; }
            e.D[7] = 0x55; e.Sr = (ushort)(resumedSr & 0xfff0); e.Pc = next + 2;
            phase = "following-MOVEQ"; Step(m, e, batch);
            if (trace == 0x4000)
            {
                var branchSr = e.Sr; ExpectTrace(m, e, pointers, next + 6, next + 2, branchSr, sequence + 2);
                phase = "following-flow-trace"; Step(m, e, batch);
                phase = "flow-trace-handler"; ClearTraceAndReturn(m, e, pointers, branchSr, next + 6, batch);
                e.Pc = next + 8; phase = "following-NOP"; Step(m, e, batch);
            }
            report.Record(id, "passing", null);
        }
        catch (NotSupportedException ex) { report.Record(id, "unsupported", phase + ": " + ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", phase + ": " + ex.Message); }
    }

    private static void SetLoaded(ArchitecturalExpectation e, int transfer, uint value)
    { if (transfer < 2) e.D[transfer] = value; else e.A[transfer - 2] = value; }
    private static void SetInitialStacks(SyntheticMachine m, Dictionary<string, uint> pointers, ushort sr)
    {
        m.Core.State.SetUserStackPointer(pointers["user"]); m.Core.State.SetInterruptStackPointer(pointers["ISP"]);
        m.Core.State.SetMasterStackPointer(pointers["MSP"]); m.Core.State.StatusRegister = sr;
    }
    private static void Define(SyntheticMachine m, ArchitecturalExpectation e, uint at, uint value, int width)
    { e.Write(at, value, width, m.Model); for (uint n = 0; n < width; n++) e.MemoryMasks[at + n] = 255; }
    private static void ExpectTrace(SyntheticMachine m, ArchitecturalExpectation e, Dictionary<string, uint> pointers,
        uint next, uint origin, ushort sr, uint sequence)
    {
        var bank = M68040StackFixture.ExceptionBank(sr); var frame = pointers[bank] - 12; pointers[bank] = frame;
        M68040StackFixture.SetStacks(e, pointers, (ushort)((sr | 0x2000) & ~0xc000));
        e.Pc = TraceHandler; e.ExceptionVector = 9;
        e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, next);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
        Define(m, e, frame, sr, 2); Define(m, e, frame + 2, next, 4);
        Define(m, e, frame + 6, 0x2024, 2); Define(m, e, frame + 8, origin, 4);
    }
    private static void ClearTraceAndReturn(SyntheticMachine m, ArchitecturalExpectation e,
        Dictionary<string, uint> pointers, ushort sr, uint pc, bool batch)
    {
        var bank = M68040StackFixture.ExceptionBank(sr); var frame = pointers[bank];
        Define(m, e, frame, (uint)(sr & 0x3fff), 2); e.Sr &= 0xfff0; e.Pc = TraceHandler + 4;
        Step(m, e, batch);
        pointers[bank] += 12; M68040StackFixture.SetStacks(e, pointers, (ushort)(sr & 0x3fff)); e.Pc = pc;
        Step(m, e, batch);
    }
    private static void Step(SyntheticMachine m, ArchitecturalExpectation e, bool batch)
    {
        if (batch)
        {
            var boundary = new Boundary();
            var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 10000, boundary);
            if (count != 1 || boundary.Before != 1 || boundary.After != 1) throw new InvalidOperationException("Batch count/callbacks differ");
        }
        else m.Core.ExecuteInstruction();
        if (e.Verify(m) is { } mismatch) throw new InvalidOperationException(mismatch);
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
