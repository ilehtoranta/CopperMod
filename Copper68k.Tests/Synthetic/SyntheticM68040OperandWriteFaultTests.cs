using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6, 8.4.6.5 table 8-5 and 8.4.6.7:
// a normal physical write fault is completed by the handler, without replaying
// the MOVE or its operand reads/address-register updates.
public sealed class SyntheticM68040OperandWriteFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0xa000, TraceHandler = 0xb000;
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];
    [Fact, Trait("Suite", "Synthetic")]
    public void TraceFrameWriteFaultCannotChangeCompletedMoveDestination()
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        m.Reset(31); m.Core.State.D[0] = 0x12345678; m.Core.State.A[0] = 0x4200;
        m.InitializePhysical(0x1000, 0x30c0, 2); m.InitializePhysical(0x1002, 0x4e71, 2);
        m.InitializePhysical(0x9020, 0x4e71, 2); m.InitializePhysical(0x9022, 0x4e71, 2);
        m.Start(); m.Core.State.StatusRegister |= 0x8000;
        var sequence = m.Core.State.ExceptionSequence;
        // MOVE completes, then the trace format-word store is rejected. That
        // exception write must not be classified as another MOVE operand.
        // Only preservation/routing is qualified here; the trace-entry fault's
        // complete format/recovery protocol remains a separate gap.
        bus.Arm(0x4700 - 12 + 6, M68kBusAccessKind.CpuDataWrite);
        m.Core.ExecuteInstruction();
        Assert.Equal(0x4202u, m.Core.State.A[0]);
        Assert.Equal(0x5678u, m.PeekPhysical(0x4200, 2));
        Assert.Single(bus.Accesses.Where(a => a.Write && a.Address == 0x4200));
        Assert.Single(bus.Rejected);
        Assert.Equal(sequence + 2, m.Core.State.ExceptionSequence);
        Assert.Equal(2, m.Core.State.LastExceptionVector);
        Assert.False(m.Core.State.Halted);
    }
    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryMemoryDestinationMoveOpcodeFaultsAndReturns(bool batch)
    {
        var report = new CoverageBatch("68040", "move-write-fault-opcodes-" + (batch ? "batch" : "scalar"));
        foreach (var opcode in MoveSpecification.Opcodes().Where(op => ((op >> 6) & 7) >= 2))
            Case(report, batch, opcode, 0x89abcdee, 31, "ISP", 0, "opcode");
        report.Complete(output);
    }

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryLaneSizeValueCcrAndStackBank(bool batch)
    {
        var report = new CoverageBatch("68040", "move-write-fault-boundaries-" + (batch ? "batch" : "scalar"));
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var bank in Banks)
        foreach (var lane in Enumerable.Range(0, 4))
        foreach (var pair in Enumerable.Range(0, 6))
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var sign = 1u << (width * 8 - 1);
            uint[] values = [0, 1, sign - 1, sign, sign + 1, MoveSpecification.Mask(width)];
            var opcode = MoveSpecification.Encode(width, new(0, 0), new(3, 7));
            Case(report, batch, opcode, values[pair], ccr, bank, 0, $"boundary/lane={lane}/pair={pair}",
                customize: m => m.Core.State.SetActiveStackPointer((bank == "MSP" ? 0x7400u : bank.StartsWith("user") ? 0x7800u : 0x4700u) + (uint)lane));
        }
        report.Complete(output);
    }

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void FullIndexedAliasesAndTraceContinuation(bool batch)
    {
        var report = new CoverageBatch("68040", "move-write-fault-indexed-" + (batch ? "batch" : "scalar"));
        foreach (var spec in IndexFixture.FullStructures())
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var register in new[] { 0, 7 })
        foreach (var bank in Banks)
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var form in new[] { "source", "destination", "dual" })
        {
            var source = new OperandForm(form == "destination" ? 3 : 6, register);
            var destination = new OperandForm(form == "source" ? 3 : 6, register);
            var opcode = MoveSpecification.Encode(width, source, destination);
            Case(report, batch, opcode, 0x89abcdee, 31, bank, trace, $"indexed/{spec.Id}/reg={register}/form={form}",
                new(SourceIndex: spec, DestinationIndex: spec with { AddressIndex = true, IndexRegister = register, LongIndex = true, Scale = 1 }));
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void PredecrementFaultCommitsAliasedBaseOnce()
    {
        var result = Case(new("68040", "bounded-control"), false, 0x2120, 0x89abcdee, 31, "MSP", 0, "predecrement-alias");
        Assert.True(result.Status == "passing", result.Reason);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void FullIndexedFaultKeepsLatchedOperandAndPendingTrace()
    {
        var index = new IndexFixture(Full: true, BaseSize: 3, Indirect: 7);
        var result = Case(new("68040", "bounded-control"), true, 0x21b0, 0x89abcdee, 31, "user-M", 0x8000, "full-index-trace",
            new(index, index with { AddressIndex = true, IndexRegister = 0, LongIndex = true, Scale = 1 }));
        Assert.True(result.Status == "passing", result.Reason);
    }

    private static (string Status, string? Reason) Case(CoverageBatch report, bool batch, ushort opcode, uint value, int ccr,
        string bank, ushort trace, string scenario, AddressOptions? options = null, Action<SyntheticMachine>? customize = null)
    {
        var source = new OperandForm((opcode >> 3) & 7, opcode & 7);
        var destination = new OperandForm((opcode >> 6) & 7, (opcode >> 9) & 7);
        var id = $"68040/MOVE/write-fault/{scenario}/size={MoveSpecification.Width(opcode)}/source={source.Id}/destination={destination.Id}/bank={bank}/T={trace:X4}/op={opcode:X4}/v={value:X8}/ccr={ccr:X2}";
        string? mismatch = null; var status = "passing";
        try
        {
            var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
            var fixture = new MoveFixture(m, opcode, value, ccr, !bank.StartsWith("user"), options: options, customize: x =>
            {
                if (bank == "MSP") { x.Core.State.StatusRegister |= 0x1000; x.Core.State.SetActiveStackPointer(0x7400); }
                customize?.Invoke(x);
            });
            fixture.Prepare();
            var originalA7 = m.Core.State.A[7];
            var stacks = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
            stacks[M68040StackFixture.PhysicalBank(bank)] = originalA7;
            m.Core.State.SetUserStackPointer(stacks["user"]); m.Core.State.SetInterruptStackPointer(stacks["ISP"]);
            m.Core.State.SetMasterStackPointer(stacks["MSP"]);
            m.Core.State.StatusRegister = (ushort)(0x0700 | M68040StackFixture.Status(bank, trace, ccr));
            m.Core.State.DestinationFunctionCode = 3; m.Core.State.SourceFunctionCode = 6;
            var program = new SyntheticM68040WritebackProgram(Handler); program.Initialize(m);
            m.InitializePhysical(8, Handler, 4); m.InitializePhysical(36, TraceHandler, 4);
            m.InitializePhysical(TraceHandler, 0x4e73, 2); m.InitializePhysical(TraceHandler + 2, 0x4e71, 2);
            m.InitializePhysical(fixture.NextPc, 0x7e55, 2); m.InitializePhysical(fixture.NextPc + 2, 0x4e71, 2);
            var e = ArchitecturalExpectation.Capture(m);
            Array.Copy(fixture.D, e.D, 8); Array.Copy(fixture.A, e.A, 8);
            var savedSr = (ushort)((fixture.ExpectedSr & 0x071f) | M68040StackFixture.Status(bank, trace, 0));
            stacks[M68040StackFixture.PhysicalBank(bank)] = fixture.A[7];
            var exceptionBank = M68040StackFixture.ExceptionBank(savedSr);
            var frame = stacks[exceptionBank] - 60; stacks[exceptionBank] = frame;
            var sequence = m.Core.State.ExceptionSequence;
            M68040StackFixture.SetStacks(e, stacks, (ushort)((savedSr | 0x2000) & 0x3fff));
            e.Pc = Handler; e.ExceptionVector = 2;
            e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 1);
            e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, savedSr);
            e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, fixture.NextPc);
            e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, 3);
            e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, 6);
            e.Write(frame, savedSr, 2, m.Model); e.Write(frame + 2, fixture.NextPc, 4, m.Model); e.Write(frame + 6, 0x7008, 2, m.Model);
            for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
            var size = fixture.Width == 1 ? 0x20 : fixture.Width == 2 ? 0x40 : 0;
            var modifier = bank.StartsWith("user") ? 1 : 5;
            var ssw = (ushort)(size | modifier | (trace == 0x8000 ? 0x2000 : 0));
            ushort[] statuses = [(ushort)(size | modifier | 0x80), 0, 0];
            uint[] addresses = [fixture.DestinationAddress, 0, 0];
            WriteDefined(12, ssw, 2); e.MemoryMasks[frame + 13] = 0x7f;
            foreach (uint n in new uint[] { 14, 16 })
            { WriteDefined(n, 0, 2); e.MemoryMasks[frame + n + 1] = 0x80; }
            WriteDefined(18, statuses[0], 2); WriteDefined(20, addresses[0], 4); WriteDefined(40, addresses[0], 4);
            if (trace == 0x8000) WriteDefined(8, SyntheticMachine.Code, 4);
            for (var n = 0; n < fixture.Width; n++)
            {
                var at = frame + 44 + ((addresses[0] + (uint)n) & 3);
                e.Memory[at] = (byte)(fixture.OperandValue >> (8 * (fixture.Width - n - 1))); e.MemoryMasks[at] = 255;
            }
            bus.Arm(addresses[0], M68kBusAccessKind.CpuDataWrite);
            ExecuteCore(m, batch);
            if (bus.Rejected.Count != 1 || bus.Rejected[0].Address != addresses[0] ||
                bus.Rejected[0].Width != fixture.Width && !(fixture.Width == 4 && bus.Rejected[0].Width == 2))
                throw new InvalidOperationException("Rejected operand width/address changed");
            // Some existing fallback routes retain descending word transfers.
            // A completed low word before rejection is an independent partial
            // store, not a whole-instruction retry or an atomic bus guarantee.
            foreach (var access in bus.Accesses.Take(bus.Rejected[0].AccessCount).Where(a => a.Write))
            {
                var offset = unchecked(access.Address - addresses[0]);
                if (fixture.Width != 4 || access.Width != 2 || offset != 2 || access.Value != (fixture.OperandValue & 0xffff))
                    throw new InvalidOperationException("Unexpected store before operand rejection");
                if (access.Address < frame || access.Address >= frame + 60)
                    e.Write(access.Address, fixture.OperandValue & 0xffff, 2, m.Model);
            }
            mismatch = e.Verify(m);
            if (mismatch == null && bus.Accesses.Skip(bus.Rejected[0].AccessCount).Any(a => a.Write &&
                (a.Address < frame || a.Address >= frame + 60))) mismatch = "Rejected operand was repeated during entry";
            if (mismatch != null) throw new InvalidOperationException("fault entry: " + mismatch);
            // Undefined frame bytes are opaque inputs to the software handler.
            // Their values qualify neither the CPU's defined lanes nor the final
            // intended store, which are checked independently from OperandValue.
            for (uint n = 8; n < 60; n++) e.Memory[frame + n] = bus.Peek(frame + n);
            e.MemoryMasks.Clear();
            uint[] encoded = [m.PeekPhysical(frame + 44, 4), 0, 0];
            var originalD = (uint[])e.D.Clone(); var originalA = (uint[])e.A.Clone();
            var stores = new List<(uint Address, int Width, uint Value)>();
            var steps = 0;
            while (e.Pc != fixture.NextPc && e.Pc != TraceHandler)
            {
                if (++steps > 120) throw new InvalidOperationException("Handler failed to terminate");
                var instruction = program.Instructions.Single(i => i.Pc == e.Pc);
                program.Expect(instruction, m, e, frame, statuses, addresses, encoded, originalD, originalA, 3, stacks, stores);
                if (instruction.Operation == "return")
                {
                    if (trace != 0x8000)
                    { stacks[exceptionBank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = fixture.NextPc; }
                    else
                    {
                        stacks[exceptionBank] = frame + 48; M68040StackFixture.SetStacks(e, stacks, (ushort)((savedSr | 0x2000) & 0x3fff));
                        e.Write(frame + 48, savedSr, 2, m.Model); e.Write(frame + 50, fixture.NextPc, 4, m.Model);
                        e.Write(frame + 54, 0x2024, 2, m.Model); e.Write(frame + 56, SyntheticMachine.Code, 4, m.Model);
                        e.Pc = TraceHandler; e.ExceptionVector = 9;
                        e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 2);
                        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, fixture.NextPc);
                    }
                }
                mismatch = Execute(m, e, batch);
                if (mismatch != null) throw new InvalidOperationException($"handler {instruction.Operation}/WB{instruction.Slot}: {mismatch}");
            }
            if (!stores.SequenceEqual(new[] { (addresses[0], fixture.Width, fixture.OperandValue) }))
                throw new InvalidOperationException("Handler store differs from independently intended operand");
            if (trace == 0x8000)
            {
                stacks[exceptionBank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = fixture.NextPc;
                mismatch = Execute(m, e, batch); if (mismatch != null) throw new InvalidOperationException("trace RTE: " + mismatch);
            }
            e.D[7] = 0x55; e.Sr = (ushort)(savedSr & 0xfff0); e.Pc = fixture.NextPc + 2;
            if (trace == 0x8000)
            {
                stacks[exceptionBank] -= 12; var sp = stacks[exceptionBank];
                var tracedSr = e.Sr;
                M68040StackFixture.SetStacks(e, stacks, (ushort)((tracedSr | 0x2000) & 0x3fff));
                e.Write(sp, tracedSr, 2, m.Model); e.Write(sp + 2, fixture.NextPc + 2, 4, m.Model);
                e.Write(sp + 6, 0x2024, 2, m.Model); e.Write(sp + 8, fixture.NextPc, 4, m.Model);
                e.Pc = TraceHandler; e.ExceptionVector = 9;
                e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 3);
                e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, tracedSr);
                e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, fixture.NextPc + 2);
            }
            mismatch = Execute(m, e, batch); if (mismatch != null) throw new InvalidOperationException("following MOVEQ: " + mismatch);

            void WriteDefined(uint offset, uint data, int width)
            { e.Write(frame + offset, data, width, m.Model); for (var n = 0; n < width; n++) e.MemoryMasks[frame + offset + (uint)n] = 255; }
        }
        catch (Exception ex)
        { mismatch = ex.ToString(); status = ex is UnsupportedM68040InstructionException or UnsupportedM68kOpcodeException or UnsupportedM68kTimingException ? "unsupported" : "mismatching"; }
        report.Record(id, status, mismatch);
        return (status, mismatch);
    }

    private static string? Execute(SyntheticMachine m, ArchitecturalExpectation e, bool batch)
    {
        ExecuteCore(m, batch);
        return e.Verify(m);
    }
    private static void ExecuteCore(SyntheticMachine m, bool batch)
    {
        if (!batch) m.Core.ExecuteInstruction();
        else if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 1000, new Boundary()) != 1)
            throw new InvalidOperationException("Expected exactly one batch instruction");
    }
    private sealed class Boundary : IM68kInstructionBoundary
    { public bool BeforeInstruction() => true; public void AfterInstruction(long previousCycle, long currentCycle) { } }

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(1, 0x10c0)]
    [InlineData(2, 0x30c0)]
    [InlineData(4, 0x20c0)]
    public void MovePostincrementWriteFaultCreatesPendingWriteback(int width, int opcode)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        m.Reset(31);
        m.Core.State.D[0] = 0x12345678;
        m.Core.State.A[0] = 0x4201;
        m.InitializePhysical(0x1000, (uint)opcode, 2);
        m.InitializePhysical(0x1002, 0x7e55, 2);
        m.InitializePhysical(0x9020, 0x4e73, 2);
        m.InitializePhysical(0x9022, 0x4e71, 2);
        m.InitializePhysical(0x4200, 0xaabbccdd, 4);
        m.Start();
        var sequence = m.Core.State.ExceptionSequence;
        bus.Arm(0x4201, M68kBusAccessKind.CpuDataWrite);
        m.Core.ExecuteInstruction();
        Assert.Equal(0x9020u, m.Core.State.ProgramCounter);
        Assert.Equal(sequence + 1, m.Core.State.ExceptionSequence);
        Assert.False(m.Core.State.Halted);
        Assert.Equal(0x4700u - 60, m.Core.State.A[7]);
        var frame = m.Core.State.A[7];
        Assert.Equal(0x7008u, m.PeekPhysical(frame + 6, 2));
        Assert.Equal(0x1002u, m.PeekPhysical(frame + 2, 4));
        Assert.Equal(0x4201u + (uint)width, m.Core.State.A[0]);
        Assert.Equal(0x2710u, m.PeekPhysical(frame, 2));
        var size = width == 1 ? 0x20u : width == 2 ? 0x40u : 0;
        Assert.Equal(size | 5, m.PeekPhysical(frame + 12, 2) & 0xff7f);
        Assert.Equal(size | 0x85, m.PeekPhysical(frame + 18, 2));
        Assert.Equal(0u, m.PeekPhysical(frame + 14, 2));
        Assert.Equal(0u, m.PeekPhysical(frame + 16, 2));
        Assert.Equal(0x4201u, m.PeekPhysical(frame + 20, 4));
        Assert.Equal(0x4201u, m.PeekPhysical(frame + 40, 4));
        // Check only the defined WB1 bus lanes, independently from CPU alignment.
        for (var n = 0; n < width; n++)
            Assert.Equal((byte)(0x12345678u >> (8 * (width - n - 1))),
                bus.Peek(frame + 44 + (uint)((1 + n) & 3)));
        Assert.Equal(0xaabbccddu, m.PeekPhysical(0x4200, 4));
        Assert.Single(bus.Rejected);
        Assert.DoesNotContain(bus.Accesses, a => a.Write && a.Address == 0x4201);
    }
}
