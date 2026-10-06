using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6.2, 8.4.6.5 and 8.4.6.7: normal physical
// MOVEM writes require format 7, CM/original EA and memory-aligned WB1.
// This discovery does not qualify handler/RTE recovery or pipeline timing.
public sealed class SyntheticM68040MovemWriteFaultDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_MOVEM_WRITE_DISCOVERY";
    private const uint Handler = 0xa000, TraceHandler = 0xb000, Vbr = 0x900000;

    [Fact, Trait("Suite", "ReferenceDiscovery")]
    public void FixedManualStoreEncodings()
    {
        Assert.Equal(0x4890, Opcode(new(2, 0), 2));
        Assert.Equal(0x48d0, Opcode(new(2, 0), 4));
        Assert.Equal(0x48a7, Opcode(new(4, 7), 2));
        Assert.Equal(0x48e7, Opcode(new(4, 7), 4));
        Assert.Equal(0x48b0, Opcode(new(6, 0), 2));
        Assert.Equal(0x48f9, Opcode(new(7, 1), 4));
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        m.Reset(); m.Core.State.D[1] = 0xffffffe0;
        var words = new List<ushort> { 0x48f0, 0x0303 };
        var fixture = new AddressingFixture(m, 4, words, m.Core.State.D, m.Core.State.A, 0,
            new(DestinationIndex: new(IndexRegister: 1, LongIndex: true, BriefDisplacement: 64)));
        Assert.Equal(0x4020u, fixture.Resolve(new(6, 0), false));
        Assert.Equal(new ushort[] { 0x48f0, 0x0303, 0x1840 }, words);
        Assert.Equal(0x1006u, fixture.NextPc);
    }

    [EnvironmentFact(Enable, "discover actual MOVEM write-fault frame construction"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarStoresRequireFormat7() => Audit(false);

    [EnvironmentFact(Enable, "discover actual MOVEM write-fault frame construction"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchStoresRequireFormat7() => Audit(true);

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalStoreFixturesExecuteWithoutFault(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-write-fixture-control-" + (batch ? "batch" : "scalar"));
        foreach (var mode in new[] { 2, 4, 5, 6, 7 })
        for (var register = 0; register < (mode == 7 ? 2 : 8); register++)
        foreach (var width in new[] { 2, 4 })
            Case(report, batch, "fixture", new(mode, register), width, 3, 0, "ISP", 0, 31, reject: false);
        report.Complete(output);
    }

    private static ushort Opcode(OperandForm form, int width)
        => (ushort)(0x4880 | (width == 4 ? 0x40 : 0) | form.Mode << 3 | form.Register);

    private void Audit(bool batch)
    {
        var report = new CoverageBatch("68040", "movem-write-fault-discovery-" + (batch ? "batch" : "scalar"));
        foreach (var mode in new[] { 2, 4, 5, 6, 7 })
        for (var register = 0; register < (mode == 7 ? 2 : 8); register++)
            Cases("opcode", new(mode, register), "ISP", 0, 31);
        foreach (var form in new[] { new OperandForm(2, 0), new(4, 0), new(5, 0), new(6, 0), new(7, 0), new(7, 1) })
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        for (var ccr = 0; ccr < 32; ccr++) Cases("status", form, bank, trace, ccr);
        report.Complete(output);

        void Cases(string cohort, OperandForm form, string bank, ushort trace, int ccr)
        {
            foreach (var width in new[] { 2, 4 })
            for (var transfer = 0; transfer < 4; transfer++)
            for (var faultByte = 0; faultByte < width; faultByte++)
                Case(report, batch, cohort, form, width, transfer, faultByte, bank, trace, ccr);
        }
    }

    internal static void Case(CoverageBatch report, bool batch, string cohort, OperandForm form,
        int width, int transfer, int faultByte, string bank, ushort trace, int ccr, bool reject = true,
        bool recover = false, IndexFixture? index = null, bool overwritePointer = false)
    {
        var opcode = Opcode(form, width);
        var indexedId = index is null ? "" : $"/index={index.Id}/ix={(index.AddressIndex ? "A" : "D")}{index.IndexRegister}/{(index.LongIndex ? "L" : "W")}/scale={1 << index.Scale}/pointer-alias={overwritePointer}";
        var id = $"68040/MOVEM/write-fault/{cohort}/size={width}/mode={form.Mode}/reg={form.Register}{indexedId}/bank={bank}/T={trace:X4}/transfer={transfer}/byte={faultByte}/op={opcode:X4}/ccr={ccr:X2}";
        try
        {
            var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
            m.Reset(ccr); m.Core.State.D[0] = 0x11112222; m.Core.State.D[1] = 0xffffffe0;
            if (index?.AddressIndex == true) m.Core.State.A[0] = 0xffffffe0;
            var sr = (ushort)(0x0700 | M68040StackFixture.Status(bank, trace, ccr));
            var stacks = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
            SetStacks();
            var registers = m.Core.State.D.Concat(m.Core.State.A).ToArray();
            var words = new List<ushort> { opcode, (ushort)(form.Mode == 4 ? 0xc0c0 : 0x0303) };
            var fixture = new AddressingFixture(m, width, words, (uint[])m.Core.State.D.Clone(),
                (uint[])m.Core.State.A.Clone(), 0, new(DestinationIndex:
                    index ?? new(IndexRegister: 1, LongIndex: true, BriefDisplacement: 64),
                    DestinationAbsoluteWord: 0xf000, DestinationAbsoluteLong: 0x12346200));
            var first = fixture.Resolve(form, false);
            if (overwritePointer)
            {
                if (index is not { Full: true, BaseSize: 2, Indirect: 1, SuppressBase: false, SuppressIndex: false } || form != new OperandForm(6, 0))
                    throw new InvalidOperationException("Unsupported self-overwriting pointer fixture");
                first = unchecked(registers[8] - 32 + registers[1]);
                if (fixture.DestinationPointerAddress != first) throw new InvalidOperationException("Wrong independently intended pointer location");
            }
            var ea = form.Mode == 4 ? registers[8 + form.Register] : first;
            var addresses = Enumerable.Range(0, 4).Select(n => unchecked(first + (uint)(form.Mode == 4 ? -n * width : n * width))).ToArray();
            int[] order = form.Mode == 4 ? [9, 8, 1, 0] : [0, 1, 8, 9];
            var values = order.Select(r => registers[r] - (form.Mode == 4 && r == 8 + form.Register ? (uint)width : 0u))
                .Select(v => width == 2 ? v & 0xffff : v).ToArray();
            foreach (var at in addresses) m.InitializePhysical(at, 0x5a5aa5a5, width);
            if (overwritePointer) m.InitializePhysical(first, first, 4);
            for (var n = 0; n < words.Count; n++) m.InitializePhysical(0x1000 + (uint)n * 2, words[n], 2);
            m.InitializePhysical(fixture.NextPc, 0x7e55, 2);
            m.InitializePhysical(fixture.NextPc + 2, 0x6002, 2); m.InitializePhysical(fixture.NextPc + 6, 0x4e71, 2);
            m.InitializePhysical(Vbr + 8, Handler, 4); m.InitializePhysical(Handler, 0x4e71, 2); m.InitializePhysical(Handler + 2, 0x4e71, 2);
            var program = recover ? new SyntheticM68040WritebackProgram(Handler) : null;
            program?.Initialize(m);
            m.InitializePhysical(Vbr + 36, TraceHandler, 4);
            m.InitializePhysical(TraceHandler, 0x0257, 2); m.InitializePhysical(TraceHandler + 2, 0x3fff, 2);
            m.InitializePhysical(TraceHandler + 4, 0x4e73, 2);
            m.Start(); SetStacks(); m.Core.State.VectorBaseRegister = Vbr;
            m.Core.State.DestinationFunctionCode = 3; m.Core.State.SourceFunctionCode = 6;
            var e = ArchitecturalExpectation.Capture(m); var sequence = m.Core.State.ExceptionSequence;
            e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, 3);
            e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, 6);
            if (!reject)
            {
                for (var n = 0; n < 4; n++) e.Write(addresses[n], values[n], width, m.Model);
                if (form.Mode == 4)
                {
                    e.A[form.Register] = unchecked(ea - (uint)(4 * width));
                    if (form.Register == 7) stacks[M68040StackFixture.PhysicalBank(bank)] = e.A[7];
                }
                M68040StackFixture.SetStacks(e, stacks, sr); e.Pc = fixture.NextPc;
                e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence);
                Execute();
                if (e.Verify(m) is { } controlMismatch) throw new InvalidOperationException("fault-free MOVEM: " + controlMismatch);
                e.Pc += 2; e.D[7] = 0x55; e.Sr &= 0xfff0;
                Execute();
                if (e.Verify(m) is { } sentinelMismatch) throw new InvalidOperationException("following MOVEQ: " + sentinelMismatch);
                report.Record(id, "passing", null); return;
            }
            for (var n = 0; n < transfer; n++) e.Write(addresses[n], values[n], width, m.Model);
            var exceptionBank = M68040StackFixture.ExceptionBank(sr); var frame = stacks[exceptionBank] - 60;
            stacks[exceptionBank] = frame;
            M68040StackFixture.SetStacks(e, stacks, (ushort)((sr | 0x2000) & ~0xc000));
            e.Pc = Handler; e.ExceptionVector = 2;
            e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 1);
            e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, 0x1000);
            e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
            e.ControlChecks["bypass cleared"] = (s => s.M68040Mmu.BypassTranslation ? 1u : 0u, 0);
            for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
            Define(0, sr, 2); Define(2, 0x1000, 4); Define(6, 0x7008, 2); Define(8, ea, 4);
            var size = width == 2 ? 0x40u : 0u; var tm = bank.StartsWith("user") ? 1u : 5u;
            Define(12, 0x1000 | size | tm, 2); e.MemoryMasks[frame + 13] = 0x7f; // X undefined.
            foreach (uint offset in new uint[] { 14, 16 }) { Define(offset, 0, 2); e.MemoryMasks[frame + offset + 1] = 0x80; }
            Define(18, 0x80 | size | tm, 2); Define(20, addresses[transfer], 4); Define(40, addresses[transfer], 4);
            for (var n = 0; n < width; n++)
            {
                var at = frame + 44 + ((addresses[transfer] + (uint)n) & 3);
                e.Memory[at] = (byte)(values[transfer] >> (8 * (width - n - 1))); e.MemoryMasks[at] = 255;
            }
            bus.Accesses.Clear(); bus.Arm(addresses[transfer] + (uint)faultByte, M68kBusAccessKind.CpuDataWrite);
            Execute();
            if (bus.Rejected.Count != 1) throw new InvalidOperationException("Missing original physical write rejection");
            var rejected = bus.Rejected[0];
            if (rejected.Kind != M68kBusAccessKind.CpuDataWrite || unchecked(addresses[transfer] + (uint)faultByte - rejected.Address) >= rejected.Width)
                throw new InvalidOperationException("Wrong physical write rejected");
            var actualFormat = m.PeekPhysical(m.Core.State.A[7] + 6, 2);
            if (actualFormat != 0x7008) throw new InvalidOperationException($"entry: format/vector expected 7008, actual {actualFormat:X4}; A7 expected {frame:X8}, actual {m.Core.State.A[7]:X8}");
            // Preserve the existing fallback's descending split-word transport.
            // A completed low half is permissible; a whole operand retry is not.
            var preceding = bus.Accesses.Take(rejected.AccessCount).Where(a => a.Write && a.Address >= addresses[transfer] && a.Address < addresses[transfer] + (uint)width).ToArray();
            foreach (var access in preceding)
            {
                if (width != 4 || access.Address != addresses[transfer] + 2 || access.Width != 2 || access.Value != (values[transfer] & 0xffff))
                    throw new InvalidOperationException("Unexpected partial faulting store");
                if (access.Address < frame || access.Address >= frame + 60) e.Write(access.Address, access.Value, 2, m.Model);
            }
            if (bus.Accesses.Skip(rejected.AccessCount).Any(a => a.Write && (a.Address < frame || a.Address >= frame + 60)))
                throw new InvalidOperationException("Operand store repeated during exception entry");
            if (e.Verify(m) is { } mismatch) throw new InvalidOperationException("entry: " + mismatch);
            if (recover)
            {
                // Undefined lanes/slots are opaque handler inputs, not expected
                // hardware values. Every defined WB1 lane was checked above.
                for (uint n = 8; n < 60; n++) e.Memory[frame + n] = bus.Peek(frame + n);
                e.MemoryMasks.Clear();
                ushort[] statuses = [(ushort)(0x80 | size | tm), 0, 0];
                uint[] wbAddresses = [addresses[transfer], 0, 0];
                uint[] encoded = [m.PeekPhysical(frame + 44, 4), 0, 0];
                var originalD = (uint[])e.D.Clone(); var originalA = (uint[])e.A.Clone();
                var handlerStores = new List<(uint Address, int Width, uint Value)>();
                var steps = 0;
                while (e.Pc != 0x1000)
                {
                    if (++steps > 120) throw new InvalidOperationException("Handler failed to terminate");
                    var instruction = program!.Instructions.Single(i => i.Pc == e.Pc);
                    program.Expect(instruction, m, e, frame, statuses, wbAddresses, encoded, originalD, originalA, 3, stacks, handlerStores);
                    if (instruction.Operation == "return")
                    { stacks[exceptionBank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, sr); e.Pc = 0x1000; }
                    Execute();
                    if (e.Verify(m) is { } handlerMismatch) throw new InvalidOperationException($"handler {instruction.Operation}/WB{instruction.Slot}: {handlerMismatch}");
                }
                if (!handlerStores.SequenceEqual(new[] { (addresses[transfer], width, values[transfer]) }))
                    throw new InvalidOperationException("WB1 handler did not complete the independently intended store");
                for (var n = 0; n < 4; n++) e.Write(addresses[n], values[n], width, m.Model);
                if (form.Mode == 4)
                {
                    e.A[form.Register] = unchecked(ea - (uint)(4 * width));
                    if (form.Register == 7) stacks[M68040StackFixture.PhysicalBank(bank)] = e.A[7];
                }
                M68040StackFixture.SetStacks(e, stacks, sr); e.Pc = fixture.NextPc;
                if (trace == 0x8000) ExpectTrace(fixture.NextPc, 0x1000, sr);
                var resumedStart = bus.Accesses.Count;
                Execute();
                if (e.Verify(m) is { } resumedMismatch) throw new InvalidOperationException("resumed MOVEM: " + resumedMismatch);
                var resumedWrites = bus.Accesses.Skip(resumedStart).Where(a => a.Write).Take(4)
                    .Select(a => (a.Address, a.Width, a.Value));
                if (!resumedWrites.SequenceEqual(addresses.Select((at, n) => (at, width, values[n]))))
                    throw new InvalidOperationException("CM did not repeat the original four operand writes in order");
                if (fixture.DestinationPointerAddress is { } pointer &&
                    bus.Accesses.Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address == pointer && a.Width == 4) != 1)
                    throw new InvalidOperationException("CM repeated indirect-pointer resolution");
                if (bus.Rejected.Count != 1) throw new InvalidOperationException("Unexpected repeated physical rejection");
                var resumedSr = sr;
                if (trace == 0x8000) { ClearTrace(sr, fixture.NextPc); resumedSr &= 0x3fff; }
                e.D[7] = 0x55; e.Sr = (ushort)(resumedSr & 0xfff0); e.Pc = fixture.NextPc + 2;
                Execute();
                if (e.Verify(m) is { } sentinelMismatch) throw new InvalidOperationException("following MOVEQ: " + sentinelMismatch);
                if (trace == 0x4000)
                {
                    var branchSr = e.Sr; ExpectTrace(fixture.NextPc + 6, fixture.NextPc + 2, branchSr);
                    Execute();
                    if (e.Verify(m) is { } branchMismatch) throw new InvalidOperationException("following flow trace: " + branchMismatch);
                    ClearTrace(branchSr, fixture.NextPc + 6);
                    e.Pc = fixture.NextPc + 8; Execute();
                    if (e.Verify(m) is { } nopMismatch) throw new InvalidOperationException("following NOP: " + nopMismatch);
                }
            }
            report.Record(id, "passing", null);

            void Define(uint offset, uint value, int count)
            { e.Write(frame + offset, value, count, m.Model); for (uint n = 0; n < count; n++) e.MemoryMasks[frame + offset + n] = 255; }
            void SetStacks()
            { m.Core.State.SetUserStackPointer(stacks["user"]); m.Core.State.SetInterruptStackPointer(stacks["ISP"]); m.Core.State.SetMasterStackPointer(stacks["MSP"]); m.Core.State.StatusRegister = sr; }
            void Execute()
            {
                if (!batch) { m.Core.ExecuteInstruction(); return; }
                var boundary = new Boundary();
                var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 10000, boundary);
                if (count != 1 || boundary.Before != 1 || boundary.After != 1) throw new InvalidOperationException("Batch count/callbacks differ");
            }
            void ExpectTrace(uint next, uint origin, ushort savedSr)
            {
                var traceBank = M68040StackFixture.ExceptionBank(savedSr); var traceFrame = stacks[traceBank] - 12;
                stacks[traceBank] = traceFrame;
                M68040StackFixture.SetStacks(e, stacks, (ushort)((savedSr | 0x2000) & ~0xc000));
                e.Pc = TraceHandler; e.ExceptionVector = 9;
                e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 2);
                e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, next);
                e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, savedSr);
                e.Write(traceFrame, savedSr, 2, m.Model); e.Write(traceFrame + 2, next, 4, m.Model);
                e.Write(traceFrame + 6, 0x2024, 2, m.Model); e.Write(traceFrame + 8, origin, 4, m.Model);
            }
            void ClearTrace(ushort savedSr, uint next)
            {
                var traceBank = M68040StackFixture.ExceptionBank(savedSr); var traceFrame = stacks[traceBank];
                e.Write(traceFrame, (uint)(savedSr & 0x3fff), 2, m.Model); e.Sr &= 0xfff0; e.Pc = TraceHandler + 4;
                Execute(); if (e.Verify(m) is { } clearMismatch) throw new InvalidOperationException("trace-handler ANDI: " + clearMismatch);
                stacks[traceBank] += 12; M68040StackFixture.SetStacks(e, stacks, (ushort)(savedSr & 0x3fff)); e.Pc = next;
                Execute(); if (e.Verify(m) is { } rteMismatch) throw new InvalidOperationException("trace-handler RTE: " + rteMismatch);
            }
        }
        catch (Exception ex)
        {
            var unsupported = ex is NotSupportedException or UnsupportedM68040InstructionException or
                UnsupportedM68kOpcodeException or UnsupportedM68kTimingException;
            report.Record(id, unsupported ? "unsupported" : "mismatching", ex.Message);
        }
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
