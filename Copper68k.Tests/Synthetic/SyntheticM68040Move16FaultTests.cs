using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6/8.4.6.7: format 7; normal read or MOVE16 write
// write transfer attributes. WB1 V is deliberately unqualified: 8.4.6.3
// contradicts example 4 and Table 8-6. Read TT also remains unqualified:
// example 1 specifies TT=0, while Table 5-2 and the software reference
// describe MOVE16 TT=1. Following-PC/address updates qualify the synchronous
// execution policy. Physical pipeline/timing remain unqualified.
public sealed class SyntheticM68040Move16FaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0xa000, TraceHandler = 0xb000;

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralMove16FixturesComplete(bool batch) => Audit(batch, false);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLineOperandFaultsRequireFormat7(bool batch) => Audit(batch, true);

    private void Audit(bool batch, bool fault)
    {
        var report = new CoverageBatch("68040", "move16-physical-" + (fault ? "fault-" : "fixture-") + (batch ? "batch" : "scalar"));
        for (var form = 0; form < 5; form++)
        for (uint lane = 0; lane < 16; lane++)
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var ccr in new[] { 0, 31 })
        foreach (var write in fault ? new[] { false, true } : new[] { false })
        foreach (var trace in fault && write ? new ushort[] { 0, 0x8000, 0x4000 } : new ushort[] { 0 })
        for (var byteIndex = 0; byteIndex < (fault ? 16 : 1); byteIndex++)
        {
            var id = $"68040/MOVE16/physical-{(fault ? "fault" : "fixture")}/form={form}/source-low={lane}/destination-low={15 - lane}/bank={bank}/ccr={ccr:X2}/direction={(write ? "write" : "read")}/T={trace:X4}/byte={byteIndex}";
            try { Run(batch, fault, form, lane, bank, ccr, write, byteIndex, trace); report.Record(id, "passing", null); }
            catch (Exception ex) { report.Record(id, ex is UnsupportedM68040InstructionException or UnsupportedM68kOpcodeException or UnsupportedM68kTimingException ? "unsupported" : "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    private static void Run(bool batch, bool fault, int form, uint lane, string bank, int ccr, bool write, int byteIndex, ushort trace)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        m.Reset(ccr);
        var source = 0x5200u + lane; var destination = 0x6200u + 15 - lane;
        ushort[] words;
        // Fixed manual first words and fixed destination-register extension.
        // Fixture locations are chosen before encoding; no decoder/EA helper.
        switch (form)
        {
            case 0: m.Core.State.A[0] = source; words = [0xf600, (ushort)(destination >> 16), (ushort)destination]; break;
            case 1: m.Core.State.A[0] = destination; words = [0xf608, (ushort)(source >> 16), (ushort)source]; break;
            case 2: m.Core.State.A[0] = source; words = [0xf610, (ushort)(destination >> 16), (ushort)destination]; break;
            case 3: m.Core.State.A[0] = destination; words = [0xf618, (ushort)(source >> 16), (ushort)source]; break;
            default: m.Core.State.A[0] = source; m.Core.State.A[1] = destination; words = [0xf620, 0x9000]; break;
        }
        uint[] line = [0x89abcdef, 0x10203040, 0xfedcba98, 0x76543210];
        foreach (var at in new uint[] { 0x5200, 0x6200 })
            for (uint n = 0; n < 24; n++) m.InitializePhysical(at - 4 + n, 0x5a, 1);
        for (var n = 0; n < 4; n++) m.InitializePhysical(0x5200 + (uint)n * 4, line[n], 4);
        SyntheticExecution.Prepare(m, words);
        var next = 0x1000u + (uint)words.Length * 2;
        m.InitializePhysical(next, 0x7e55, 2); m.InitializePhysical(next + 2, 0x4e71, 2);
        m.InitializePhysical(8, Handler, 4); m.InitializePhysical(Handler, 0x4e71, 2);
        m.InitializePhysical(Handler + 2, 0x4e71, 2);
        m.InitializePhysical(36, TraceHandler, 4); m.InitializePhysical(TraceHandler, 0x4e73, 2);
        if (fault && write)
        {
            uint hp = Handler;
            foreach (var wordsAt in LineHandler())
                foreach (var word in wordsAt) { m.InitializePhysical(hp, word, 2); hp += 2; }
        }
        var s = m.Core.State;
        s.SetInterruptStackPointer(0x4700); s.SetMasterStackPointer(0x7400);
        s.StatusRegister = (ushort)((bank == "MSP" ? 0x3700 : 0x2700) | ccr | trace);
        s.SourceFunctionCode = 2; s.DestinationFunctionCode = 6;
        var e = ArchitecturalExpectation.Capture(m); var sequence = s.ExceptionSequence;
        bus.Accesses.Clear();
        if (fault) bus.Arm((write ? 0x6200u : 0x5200u) + (uint)byteIndex,
            write ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead);
        Execute();
        if (!fault)
        {
            for (var n = 0; n < 4; n++) e.Write(0x6200 + (uint)n * 4, line[n], 4, m.Model);
            if (form is 0 or 1 or 4) e.A[0] += 16;
            if (form == 4) e.A[1] += 16;
            e.Pc = next;
            if (e.Verify(m) is { } mismatch) throw new InvalidOperationException("fixture: " + mismatch);
            if (s.ExceptionSequence != sequence || bus.Rejected.Count != 0) throw new InvalidOperationException("Unexpected fault-free exception");
            var accesses = bus.Accesses.Where(a => a.Address >= 0x5200 && a.Address < 0x5210 || a.Address >= 0x6200 && a.Address < 0x6210)
                .Select(a => (a.Address, a.Width, a.Write));
            var expected = Enumerable.Range(0, 4).Select(n => (0x5200u + (uint)n * 4, 4, false))
                .Concat(Enumerable.Range(0, 4).Select(n => (0x6200u + (uint)n * 4, 4, true)));
            if (!accesses.SequenceEqual(expected)) throw new InvalidOperationException("Synchronous line transport differs");
            e.Pc += 2; e.D[7] = 0x55; e.Sr &= 0xfff0; Execute();
            if (e.Verify(m) is { } sentinel) throw new InvalidOperationException("sentinel: " + sentinel);
        }
        else
        {
            var stack = bank == "MSP" ? 0x7400u : 0x4700u; var frame = stack - 60;
            if (bus.Rejected.Count != 1 || bus.Rejected[0].Address != (write ? 0x6200u : 0x5200u) + (uint)(byteIndex / 4) * 4 || bus.Rejected[0].Width != 4)
                throw new InvalidOperationException("Wrong physical operand rejection");
            if (s.ProgramCounter != Handler || s.LastExceptionVector != 2 || s.ExceptionSequence != sequence + 1 || s.Halted || s.Stopped)
                throw new InvalidOperationException("Rejected transfer did not enter exactly one access error");
            var format = m.PeekPhysical(s.A[7] + 6, 2);
            if (s.A[7] != frame || format != 0x7008)
                throw new InvalidOperationException($"format 7 required: SP={s.A[7]:X8}, frame={format:X4}");
            if (m.PeekPhysical(frame, 2) != e.Sr || m.PeekPhysical(frame + 20, 4) != bus.Rejected[0].Address)
                throw new InvalidOperationException("Saved SR or logical fault address differs");
            // Size/MA, read TT and WB1 V remain explicitly excluded. Write
            // TT=1 and both normal supervisor data TM=5 are required.
            // SFC/DFC do not select MOVE16.
            var attributes = write ? 0x000du : 0x0105u;
            if ((m.PeekPhysical(frame + 12, 2) & (write ? 0x011fu : 0x0107u)) != attributes)
                throw new InvalidOperationException("MOVE16 direction/TM or write TT differ");
            // Reads must not modify D registers or CCR. Fault-time A-register
            // completion/PC policy remains an explicitly separate investigation.
            if (!s.D.SequenceEqual(e.D) || s.StatusRegister != (e.Sr & 0x3fff)) throw new InvalidOperationException("Fault changed data registers/flags");
            var partials = write ? byteIndex / 4 : 0;
            for (var n = 0; n < partials; n++) e.Write(0x6200 + (uint)n * 4, line[n], 4, m.Model);
            foreach (var at in e.Memory.Keys.Concat(bus.Memory.Keys).Distinct())
                if ((at < frame || at >= stack) && e.Memory.GetValueOrDefault(at) != bus.Peek(at))
                    throw new InvalidOperationException($"Unexpected operand/canary change at {at:X8}");
            var after = bus.Accesses.Skip(bus.Rejected[0].AccessCount);
            if (after.Any(a => a.Address >= 0x5200 && a.Address < 0x5210 || a.Address >= 0x6200 && a.Address < 0x6210))
                throw new InvalidOperationException("Line operand access repeated during exception delivery");
            if (write)
                CompleteLine(m, bus, e, batch, frame, bank, form, next, line, partials, sequence);
        }
        if (s.SourceFunctionCode != 2 || s.DestinationFunctionCode != 6) throw new InvalidOperationException("Instruction changed FC registers");

        void Execute()
        {
            if (batch)
            { if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, s.Cycles + 10000, new Boundary()) != 1) throw new InvalidOperationException("Empty batch"); }
            else m.Core.ExecuteInstruction();
        }
    }
    // Fixed independent program: preserve D0/A0/A1, align FA, write PD0..PD3
    // using ordinary MOVE.L, restore registers, then RTE. WB1 validity is not
    // consulted, because the manual contradicts itself on that field.
    private static ushort[][] LineHandler() =>
    [
        [0x48e7, 0x80c0], [0x43ef, 12], [0x2029, 20],
        [0x0280, 0xffff, 0xfff0], [0x2040],
        [0x2029, 44], [0x20c0], [0x2029, 48], [0x20c0],
        [0x2029, 52], [0x20c0], [0x2029, 56], [0x20c0],
        [0x4cdf, 0x0301], [0x4e73]
    ];

    private static void CompleteLine(SyntheticMachine m,
        SyntheticM68040AccessDoubleFaultTests.FaultBus bus, ArchitecturalExpectation e,
        bool batch, uint frame, string bank, int form, uint next, uint[] line, int partials, uint sequence)
    {
        var s = m.Core.State;
        if (m.PeekPhysical(frame + 2, 4) != next || s.LastExceptionStackedProgramCounter != next)
            throw new InvalidOperationException("Synchronous MOVE16 write must resume after consumed extensions");
        if ((m.PeekPhysical(frame + 12, 2) & 0x60) != 0x60)
            throw new InvalidOperationException("MOVE16 line transfer size missing");
        var traced = (e.Sr & 0x8000) != 0;
        if ((m.PeekPhysical(frame + 12, 2) & 0xf000) != (traced ? 0x2000u : 0))
            throw new InvalidOperationException("MOVE16 pending trace classification differs");
        if (traced && m.PeekPhysical(frame + 8, 4) != SyntheticMachine.Code)
            throw new InvalidOperationException("Pending trace origin differs");
        for (var n = 0; n < 4; n++)
            if (m.PeekPhysical(frame + 44 + (uint)n * 4, 4) != line[n])
                throw new InvalidOperationException($"Latched PD{n} data differs");
        if (form is 0 or 1 or 4) e.A[0] += 16;
        if (form == 4) e.A[1] += 16;
        var originalD = (uint[])e.D.Clone(); var originalA = (uint[])e.A.Clone();
        var savedSr = e.Sr;
        var stacks = new Dictionary<string, uint>
        { ["user"] = e.InactiveStackPointer!.Value, ["ISP"] = bank == "ISP" ? frame : 0x4700,
          ["MSP"] = bank == "MSP" ? frame : 0x7400 };
        M68040StackFixture.SetStacks(e, stacks, (ushort)(savedSr & 0x3fff));
        e.Pc = Handler; e.ExceptionVector = 2;
        e.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, sequence + 1);
        e.ControlChecks["saved PC"] = (x => x.LastExceptionStackedProgramCounter, next);
        e.ControlChecks["saved SR"] = (x => x.LastExceptionStatusRegister, savedSr);
        // Defined fields were checked independently above. Carry the remaining
        // unqualified/undefined frame bytes only as preservation canaries.
        for (uint at = frame; at < frame + 60; at++) e.Memory[at] = bus.Peek(at);
        Verify("fault entry");
        var program = LineHandler();
        for (var n = 0; n < program.Length; n++)
        {
            e.Pc += (uint)program[n].Length * 2;
            if (n == 0)
            {
                stacks[bank] = frame - 12; M68040StackFixture.SetStacks(e, stacks, e.Sr);
                e.Write(frame - 12, originalD[0], 4, m.Model);
                e.Write(frame - 8, originalA[0], 4, m.Model);
                e.Write(frame - 4, originalA[1], 4, m.Model);
            }
            else if (n == 1) e.A[1] = frame;
            else if (n == 2) { e.D[0] = 0x6200u + (uint)partials * 4; MoveFlags(); }
            else if (n == 3) { e.D[0] &= 0xfffffff0; MoveFlags(); }
            else if (n == 4) e.A[0] = e.D[0];
            else if (n < 13)
            {
                var index = (n - 5) / 2;
                if ((n & 1) != 0) { e.D[0] = line[index]; MoveFlags(); }
                else { e.Write(e.A[0], e.D[0], 4, m.Model); e.A[0] += 4; MoveFlags(); }
            }
            else if (n == 13)
            {
                e.D[0] = originalD[0]; e.A[0] = originalA[0]; e.A[1] = originalA[1];
                stacks[bank] = frame; M68040StackFixture.SetStacks(e, stacks, e.Sr);
            }
            else if (!traced)
            { stacks[bank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = next; }
            else
            {
                stacks[bank] = frame + 48; M68040StackFixture.SetStacks(e, stacks, (ushort)(savedSr & 0x3fff));
                e.Write(frame + 48, savedSr, 2, m.Model); e.Write(frame + 50, next, 4, m.Model);
                e.Write(frame + 54, 0x2024, 2, m.Model); e.Write(frame + 56, SyntheticMachine.Code, 4, m.Model);
                e.Pc = TraceHandler; e.ExceptionVector = 9;
                e.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, sequence + 2);
                e.ControlChecks["saved PC"] = (x => x.LastExceptionStackedProgramCounter, next);
            }
            Step(); Verify($"line handler step {n}");
        }
        if (traced)
        {
            stacks[bank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = next;
            Step(); Verify("pending trace RTE");
        }
        var reads = bus.Accesses.Where(a => !a.Write && a.Address >= 0x5200 && a.Address < 0x5210)
            .Select(a => (a.Address, a.Width));
        if (!reads.SequenceEqual(Enumerable.Range(0, 4).Select(n => (0x5200u + (uint)n * 4, 4))))
            throw new InvalidOperationException("MOVE16 source was reread during recovery");
        var writes = bus.Accesses.Where(a => a.Write && a.Address >= 0x6200 && a.Address < 0x6210)
            .Select(a => (a.Address, a.Width, a.Value));
        var expected = Enumerable.Range(0, partials).Concat(Enumerable.Range(0, 4))
            .Select(n => (0x6200u + (uint)n * 4, 4, line[n]));
        if (!writes.SequenceEqual(expected))
            throw new InvalidOperationException("Original partial writes or explicit line completion differ");
        e.Pc = next + 2; e.D[7] = 0x55; e.Sr &= 0xfff0;
        if (traced)
        {
            var tracedSr = e.Sr; stacks[bank] -= 12; var sp = stacks[bank];
            M68040StackFixture.SetStacks(e, stacks, (ushort)(tracedSr & 0x3fff));
            e.Write(sp, tracedSr, 2, m.Model); e.Write(sp + 2, next + 2, 4, m.Model);
            e.Write(sp + 6, 0x2024, 2, m.Model); e.Write(sp + 8, next, 4, m.Model);
            e.Pc = TraceHandler; e.ExceptionVector = 9;
            e.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, sequence + 3);
            e.ControlChecks["saved PC"] = (x => x.LastExceptionStackedProgramCounter, next + 2);
            e.ControlChecks["saved SR"] = (x => x.LastExceptionStatusRegister, tracedSr);
        }
        Step(); Verify("following MOVEQ");

        void MoveFlags() => e.Sr = (ushort)((e.Sr & 0xfff0) |
            (e.D[0] == 0 ? 4 : 0) | ((e.D[0] & 0x80000000) != 0 ? 8 : 0));
        void Verify(string stage)
        { if (e.Verify(m) is { } mismatch) throw new InvalidOperationException(stage + ": " + mismatch); }
        void Step()
        {
            if (batch)
            { if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, s.Cycles + 10000, new Boundary()) != 1) throw new InvalidOperationException("Empty batch"); }
            else m.Core.ExecuteInstruction();
        }
    }

    private sealed class Boundary : IM68kInstructionBoundary
    { public bool BeforeInstruction() => true; public void AfterInstruction(long previousCycle, long currentCycle) { } }
}
