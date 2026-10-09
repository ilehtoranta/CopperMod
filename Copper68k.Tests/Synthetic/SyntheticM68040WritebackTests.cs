using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68040WritebackTests(ITestOutputHelper output)
{
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];
    private static readonly uint[] Values = [0, uint.MaxValue, 0x89abcdef, 0x12345678];
    private static readonly int[][] Patterns = [[1, 1, 1], [2, 2, 2], [4, 4, 4], [1, 2, 4]];
    private const uint Target = 0x6000, TraceHandler = 0x9090;

    [Fact, Trait("Suite", "Synthetic")]
    public void CanonicalWritebacksAllLanesSizesValuesAndCcrScalar() => Audit(false, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void CanonicalWritebacksAllLanesSizesValuesAndCcrBatch() => Audit(false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void WritebackValidityOrderOverlapAndRestoredTraceScalar() => Audit(true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void WritebackValidityOrderOverlapAndRestoredTraceBatch() => Audit(true, true);

    private void Audit(bool structural, bool batch)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var program = new SyntheticM68040WritebackProgram();
        var report = new CoverageBatch("68040", $"rte-writeback-{(structural ? "structure" : "canonical")}-{(batch ? "batch" : "scalar")}");
        foreach (var handlerBank in new[] { "ISP", "MSP" })
        foreach (var restoredBank in Banks)
        foreach (var lane in Enumerable.Range(0, 4))
        {
            if (!structural)
            {
                for (var slot = 1; slot <= 3; slot++)
                foreach (var width in new[] { 1, 2, 4 })
                for (var pair = 0; pair < Values.Length; pair++)
                for (var ccr = 0; ccr < 32; ccr++)
                    Case(m, program, report, batch, false, handlerBank, restoredBank, lane, 1 << (slot - 1),
                        [width, width, width], $"slot{slot}-size{width}", pair, ccr, 0, "distinct");
            }
            else
            {
                for (var mask = 0; mask < 8; mask++)
                for (var pattern = 0; pattern < Patterns.Length; pattern++)
                foreach (var layout in new[] { "distinct", "same", "overlap" })
                foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
                foreach (var pair in new[] { 2, 3 })
                foreach (var ccr in new[] { 0, 31 })
                    Case(m, program, report, batch, true, handlerBank, restoredBank, lane, mask,
                        Patterns[pattern], $"pattern{pattern}", pair, ccr, trace, layout);
            }
        }
        report.Complete(output);
    }

    private static (string Status, string? Reason) Case(SyntheticMachine m, SyntheticM68040WritebackProgram program, CoverageBatch report,
        bool batch, bool structural, string handlerBank, string restoredBank, int lane, int mask, int[] widths,
        string sizes, int pair, int ccr, ushort trace, string layout)
    {
        var id = $"68040/RTE/writebacks/{(structural ? "structure" : "canonical")}/handler={handlerBank}/restore={restoredBank}" +
            $"/lane={lane}/valid={mask}/{sizes}/layout={layout}/T={trace:X4}/pair={pair}/op=4E73/ccr={ccr:X2}";
        m.Reset(ccr);
        var frame = (handlerBank == "ISP" ? 0x4700u : 0x7400u) + (uint)(structural ? lane & 1 : 0);
        var stacks = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
        stacks[handlerBank] = frame;
        for (var n = -32; n < 80; n++) m.InitializePhysical(unchecked(frame + (uint)n), (uint)(n ^ 0xa5), 1);
        var savedSr = (ushort)(0x0700 | M68040StackFixture.Status(restoredBank, trace, ccr ^ 31));
        m.InitializePhysical(frame, savedSr, 2); m.InitializePhysical(frame + 2, Target, 4);
        m.InitializePhysical(frame + 6, 0x7008, 2); m.InitializePhysical(frame + 8, 0x12345678, 4);
        for (var n = -4; n < 84; n++) m.InitializePhysical(unchecked(0x4200u + (uint)n), (uint)(n ^ 0x6a), 1);
        var statuses = new ushort[3]; var addresses = new uint[3]; var encoded = new uint[3];
        var intended = new List<(uint Address, int Width, uint Value)>();
        for (var slot = 1; slot <= 3; slot++)
        {
            var index = slot - 1;
            addresses[index] = 0x4200u + (uint)lane + (uint)(layout == "distinct" ? index * 24 : layout == "overlap" ? index : 0);
            var value = Values[pair] ^ (uint)(index * 0x11111111);
            var width = widths[index];
            var transfer = value & (width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue);
            statuses[index] = (ushort)((mask & (1 << index)) != 0
                ? 0x80 | (width == 1 ? 0x20 : width == 2 ? 0x40 : 0) | (slot == 2 ? 1 : 5) : 0);
            encoded[index] = slot == 1 ? MemoryAligned(transfer, width, (int)(addresses[index] & 3)) : value;
            m.InitializePhysical(frame + (uint)SyntheticM68040WritebackProgram.StatusOffset(slot), statuses[index], 2);
            m.InitializePhysical(frame + (uint)SyntheticM68040WritebackProgram.AddressOffset(slot), addresses[index], 4);
            m.InitializePhysical(frame + (uint)SyntheticM68040WritebackProgram.DataOffset(slot), encoded[index], 4);
            if ((mask & (1 << index)) != 0) intended.Add((addresses[index], width, transfer));
        }
        // Table 8-6: normal physical write has valid WB1 and FA=WB1A;
        // supplied write-page-fault images have WB2, while read images can
        // have only WB3. No actual page fault/MMU operation is executed here.
        var header = FaultHeader(statuses, addresses);
        m.InitializePhysical(frame + 12, header.Ssw, 2); m.InitializePhysical(frame + 20, header.FaultAddress, 4);
        program.Initialize(m);
        m.InitializePhysical(Target, 0x60fe, 2); m.InitializePhysical(Target + 2, 0x7e7e, 2);
        m.InitializePhysical(TraceHandler, 0x4e73, 2); m.InitializePhysical(TraceHandler + 2, 0x7e7e, 2);
        m.InitializePhysical(36, TraceHandler, 4);
        m.Start();
        m.Core.State.SetUserStackPointer(stacks["user"]); m.Core.State.SetInterruptStackPointer(stacks["ISP"]);
        m.Core.State.SetMasterStackPointer(stacks["MSP"]);
        m.Core.State.StatusRegister = (ushort)(0x0700 | M68040StackFixture.Status(handlerBank, 0, ccr));
        m.Core.State.DestinationFunctionCode = (uint)(ccr & 7);
        m.Core.State.M68040Fpu.Fpcr = 0x60; m.Core.State.M68040Fpu.Fpsr = 0x12340000; m.Core.State.M68040Fpu.Fpiar = 0x87654321;
        var e = ArchitecturalExpectation.Capture(m);
        var sequence = m.Core.State.ExceptionSequence;
        var originalD = (uint[])e.D.Clone(); var originalA = (uint[])e.A.Clone(); var originalDfc = m.Core.State.DestinationFunctionCode;
        var stores = new List<(uint Address, int Width, uint Value)>();
        M68040StackFixture.SetStacks(e, stacks, e.Sr);
        e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, m.Core.State.SourceFunctionCode);
        e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, originalDfc);
        e.ControlChecks["FPCR preserved"] = (s => s.M68040Fpu.Fpcr, 0x60);
        e.ControlChecks["FPSR preserved"] = (s => s.M68040Fpu.Fpsr, 0x12340000);
        e.ControlChecks["FPIAR preserved"] = (s => s.M68040Fpu.Fpiar, 0x87654321);
        e.ControlChecks["no handler exception"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["writeback order/width/value"] = (_ => m.Bus.Accesses
            .Where(a => a.Write && a.Address is >= 0x4200 and < 0x4254)
            .Select(a => (a.Address, a.Width, a.Value)).SequenceEqual(stores) ? 0u : 1u, 0);
        for (var slot = 1; slot <= 3; slot++)
            if ((mask & (1 << (slot - 1))) == 0)
                for (var n = 0; n < 8; n++) e.ForbiddenOperandReads.Add(frame + (uint)SyntheticM68040WritebackProgram.AddressOffset(slot) + (uint)n);
        var steps = 0;
        string? failure = null; var status = "passing";
        try
        {
            while (e.Pc != Target)
            {
                if (++steps > 120) throw new InvalidOperationException("Handler did not terminate");
                var instruction = program.Instructions.Single(i => i.Pc == e.Pc);
                program.Expect(instruction, m, e, frame, statuses, addresses, encoded, originalD, originalA, originalDfc, stacks, stores);
                if (instruction.Operation == "return")
                { stacks[handlerBank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = Target; }
                failure = Execute(m, e, batch);
                if (failure != null) { failure = $"{instruction.Operation}/WB{instruction.Slot}/PC={instruction.Pc:X8}: {failure}"; break; }
            }
            if (failure == null && !stores.SequenceEqual(intended)) failure = "Writeback results differ from independently intended operands";
            if (failure == null)
            {
                if (trace != 0)
                {
                    var bank = M68040StackFixture.ExceptionBank(savedSr); stacks[bank] -= 12; var sp = stacks[bank];
                    M68040StackFixture.SetStacks(e, stacks, (ushort)((savedSr | 0x2000) & 0x3fff));
                    e.Write(sp, savedSr, 2, m.Model); e.Write(sp + 2, Target, 4, m.Model);
                    e.Write(sp + 6, 0x2024, 2, m.Model); e.Write(sp + 8, Target, 4, m.Model);
                    e.Pc = TraceHandler; e.ExceptionVector = 9;
                    e.ControlChecks["no handler exception"] = (s => s.ExceptionSequence, sequence + 1);
                    e.ControlChecks["trace saved SR"] = (s => s.LastExceptionStatusRegister, savedSr);
                }
                failure = Execute(m, e, batch);
                if (failure != null) failure = "following BRA: " + failure;
                if (failure == null && trace != 0)
                {
                    stacks[M68040StackFixture.ExceptionBank(savedSr)] += 12;
                    M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = Target; e.ExceptionVector = null;
                    failure = Execute(m, e, batch); if (failure != null) failure = "trace RTE: " + failure;
                }
            }
            if (failure != null) status = "mismatching";
        }
        catch (Exception ex) { failure = ex.ToString(); status = ex is UnsupportedM68kOpcodeException or UnsupportedM68kTimingException or UnsupportedM68040InstructionException ? "unsupported" : "mismatching"; }
        report.Record(id, status, failure);
        return (status, failure);
    }

    private static (ushort Ssw, uint FaultAddress) FaultHeader(ushort[] statuses, uint[] addresses)
        => (statuses[0] & 0x80) != 0 ? ((ushort)(statuses[0] & 0x67), addresses[0])
         : (statuses[1] & 0x80) != 0 ? ((ushort)(0x400 | (statuses[1] & 0x67)), addresses[1])
         : ((ushort)0x0105, 0x4300);

    [Theory]
    [InlineData(1, 0x25, 0x4200u)]
    [InlineData(2, 0x441, 0x4218u)]
    [InlineData(4, 0x105, 0x4300u)]
    [InlineData(0, 0x105, 0x4300u)]
    public void SuppliedHeaderMatchesManualFrameCombinations(int mask, int ssw, uint faultAddress)
    {
        ushort[] statuses = [(ushort)((mask & 1) != 0 ? 0xa5 : 0), (ushort)((mask & 2) != 0 ? 0xc1 : 0), (ushort)((mask & 4) != 0 ? 0x85 : 0)];
        Assert.Equal(((ushort)ssw, faultAddress), FaultHeader(statuses, [0x4200, 0x4218, 0x4230]));
    }

    [Fact]
    public void HandlerCompletesMemoryAlignedByteAtLaneOne()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var result = Case(m, new(), new("68040", "bounded-control"), false, false, "MSP", "user-M", 1, 1,
            [1, 1, 1], "slot1-size1", 2, 31, 0, "distinct");
        Assert.True(result.Status == "passing", result.Reason);
    }

    [Fact]
    public void HandlerCompletesOverlappingMixedWritebacksInOrder()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var result = Case(m, new(), new("68040", "bounded-control"), false, true, "MSP", "ISP", 3, 7,
            [1, 2, 4], "pattern3", 3, 0, 0x4000, "overlap");
        Assert.True(result.Status == "passing", result.Reason);
    }

    private static string? Execute(SyntheticMachine m, ArchitecturalExpectation e, bool batch)
    {
        if (!batch) m.Core.ExecuteInstruction();
        else
        {
            var boundary = new Boundary();
            var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 1000, boundary);
            if (count != 1 || boundary.Before != 1 || boundary.After != 1) return "Batch count/callbacks differ";
        }
        return e.Verify(m);
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        internal int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }

    // Deposit independently chosen operand bytes in their bus lanes. Poison
    // unused lanes, so reading WB1 as register aligned cannot accidentally pass.
    private static uint MemoryAligned(uint value, int width, int lane)
    {
        byte[] bytes = [0xd1, 0xe2, 0xf3, 0x04];
        for (var n = 0; n < width; n++) bytes[(lane + n) & 3] = (byte)(value >> (8 * (width - n - 1)));
        uint result = 0; foreach (var b in bytes) result = result << 8 | b; return result;
    }

    [Theory]
    [InlineData(0x78u, 1, 0, 0x78e2f304u)]
    [InlineData(0x78u, 1, 3, 0xd1e2f378u)]
    [InlineData(0x1234u, 2, 3, 0x34e2f312u)]
    [InlineData(0x12345678u, 4, 1, 0x78123456u)]
    public void MemoryAlignedFixtureMatchesManualExamples(uint value, int width, int lane, uint expected)
        => Assert.Equal(expected, MemoryAligned(value, width, lane));

    [Fact]
    public void HandlerUsesFixedReferenceEncodings()
    {
        var program = new SyntheticM68040WritebackProgram();
        var prefix = program.Instructions.Take(6).SelectMany(i => i.Words).ToArray();
        Assert.Equal(new ushort[] { 0x48e7, 0xf0c0, 0x43ef, 24, 0x4e7a, 0x3001, 0x7400, 0x1429, 19, 0x0802, 7 }, prefix);
        Assert.Equal(new ushort[] { 0xe3b8 }, program.Instructions.Single(i => i.Operation == "lane-rotate").Words);
        Assert.Equal(new ushort[] { 0x4cdf, 0x030f }, program.Instructions.Single(i => i.Operation == "restore").Words);
        Assert.Equal(new ushort[] { 0x4e73 }, program.Instructions.Last().Words);
    }
}
