using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM Table 3-2 and 8.4.6: actual normal-space MOVES writes,
// WB1 software completion and RTE. Following-PC and retained EA effects
// qualify the existing synchronous execution policy, not physical pipelines.
public sealed class SyntheticM68040MovesWriteFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0xa000, TraceHandler = 0xb000;
    private sealed record StoreFixture(int Width, uint DestinationAddress, uint OperandValue,
        uint NextPc, uint[] D, uint[] A, ushort ExpectedSr);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalSpaceStoresCompleteThroughActualHandler(bool batch)
    {
        var report = new CoverageBatch("68040", "moves-write-recovery-" + (batch ? "batch" : "scalar"));
        foreach (var mode in new[] { 2, 3, 4 })
        foreach (var width in new[] { 1, 2, 4 })
        for (uint lane = 0; lane < 4; lane++)
        foreach (var fc in new[] { 1, 2, 5, 6 })
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var ccr in new[] { 0, 31 })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        for (var faultByte = 0; faultByte < width; faultByte++)
        {
            ushort opcode = (ushort)((width == 1 ? 0x0e00 : width == 2 ? 0x0e40 : 0x0e80) | mode << 3);
            Case(report, batch, opcode, lane, ccr, bank, trace, $"lane={lane}/DFC={fc}/byte={faultByte}", fc, faultByte);
        }
        report.Complete(output);
    }

    private static (string Status, string? Reason) Case(CoverageBatch report, bool batch, ushort opcode, uint value, int ccr,
        string bank, ushort trace, string scenario, int fc = 5, int faultByte = 0)
    {
        var width = opcode >> 6 & 3;
        width = 1 << width;
        var mode = opcode >> 3 & 7;
        var id = $"68040/MOVES/write-recovery/{scenario}/size={width}/mode={mode}/bank={bank}/T={trace:X4}/ccr={ccr:X2}/op={opcode:X4}";
        string? mismatch = null; var status = "passing";
        try
        {
            var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
            m.Reset(ccr);
            var operand = 0x4200u + (uint)(value & 3);
            m.Core.State.D[0] = 0x89abcdef;
            m.Core.State.A[0] = mode == 4 ? operand + (uint)width : operand;
            for (uint at = 0x41fc; at < 0x420b; at++) m.InitializePhysical(at, 0x5a, 1);
            SyntheticExecution.Prepare(m, [opcode, 0x0800, 0x7e55, 0x4e71]);
            m.Core.State.SetInterruptStackPointer(0x4700);
            m.Core.State.SetMasterStackPointer(0x7400);
            m.Core.State.StatusRegister = (ushort)(0x0700 | M68040StackFixture.Status(bank, trace, ccr));
            var fixture = new StoreFixture(width, operand, 0x89abcdefu & (width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue),
                0x1004, (uint[])m.Core.State.D.Clone(), (uint[])m.Core.State.A.Clone(), (ushort)(0x0700 | ccr));
            if (mode == 3) fixture.A[0] = operand + (uint)width;
            if (mode == 4) fixture.A[0] = operand;
            var originalA7 = m.Core.State.A[7];
            var stacks = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
            stacks[M68040StackFixture.PhysicalBank(bank)] = originalA7;
            m.Core.State.SetUserStackPointer(stacks["user"]); m.Core.State.SetInterruptStackPointer(stacks["ISP"]);
            m.Core.State.SetMasterStackPointer(stacks["MSP"]);
            m.Core.State.StatusRegister = (ushort)(0x0700 | M68040StackFixture.Status(bank, trace, ccr));
            m.Core.State.DestinationFunctionCode = (uint)fc; m.Core.State.SourceFunctionCode = 6;
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
            e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, (uint)fc);
            e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, 6);
            e.Write(frame, savedSr, 2, m.Model); e.Write(frame + 2, fixture.NextPc, 4, m.Model); e.Write(frame + 6, 0x7008, 2, m.Model);
            for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
            var size = fixture.Width == 1 ? 0x20 : fixture.Width == 2 ? 0x40 : 0;
            var modifier = fc is 1 or 2 ? 1 : 5;
            var ssw = (ushort)(size | modifier | (trace != 0 ? 0x2000 : 0));
            ushort[] statuses = [(ushort)(size | modifier | 0x80), 0, 0];
            uint[] addresses = [fixture.DestinationAddress, 0, 0];
            WriteDefined(12, ssw, 2); e.MemoryMasks[frame + 13] = 0x7f;
            foreach (uint n in new uint[] { 14, 16 })
            { WriteDefined(n, 0, 2); e.MemoryMasks[frame + n + 1] = 0x80; }
            WriteDefined(18, statuses[0], 2); WriteDefined(20, addresses[0], 4); WriteDefined(40, addresses[0], 4);
            if (trace != 0) WriteDefined(8, SyntheticMachine.Code, 4);
            for (var n = 0; n < fixture.Width; n++)
            {
                var at = frame + 44 + ((addresses[0] + (uint)n) & 3);
                e.Memory[at] = (byte)(fixture.OperandValue >> (8 * (fixture.Width - n - 1))); e.MemoryMasks[at] = 255;
            }
            bus.Arm(addresses[0] + (uint)faultByte, M68kBusAccessKind.CpuDataWrite);
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
                program.Expect(instruction, m, e, frame, statuses, addresses, encoded, originalD, originalA, (uint)fc, stacks, stores);
                if (instruction.Operation == "return")
                {
                    if (trace == 0)
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
            if (trace != 0)
            {
                stacks[exceptionBank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = fixture.NextPc;
                mismatch = Execute(m, e, batch); if (mismatch != null) throw new InvalidOperationException("trace RTE: " + mismatch);
            }
            var completedStores = bus.Accesses.Count(a => a.Write && a.Address == fixture.DestinationAddress && a.Kind == M68kBusAccessKind.CpuDataWrite);
            if (completedStores != 1 || bus.Rejected.Count != 1) throw new InvalidOperationException("Original MOVES was repeated or pending write not completed exactly once");
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
            if (bus.Accesses.Count(a => a.Write && a.Address == fixture.DestinationAddress && a.Kind == M68kBusAccessKind.CpuDataWrite) != 1)
                throw new InvalidOperationException("Following sentinel repeated the original store");

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

}
