using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Supplied normal format-7 outer frame; actual physical fault in the integer
// WB1/WB2/WB3 completion handler. MC68040UM 8.4.6.3/5/7. No physical pipeline,
// original outer-frame construction or enabled MMU qualification is claimed.
public sealed class SyntheticM68040NestedWritebackFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0xa000, Next = 0x8000;

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualHandlerStoresFaultCompleteAndResume(bool batch)
    {
        var report = new CoverageBatch("68040", "nested-writeback-fault-" + (batch ? "batch" : "scalar"));
        foreach (var width in new[] { 1, 2, 4 })
        for (uint lane = 0; lane < 4; lane++)
        foreach (var fc in new[] { 1, 5 })
        foreach (var bank in new[] { "ISP", "MSP" })
        for (var slot = 1; slot <= 3; slot++)
        for (var faultByte = 0; faultByte < width; faultByte++)
        {
            var id = $"68040/writeback/nested-physical-fault/size={width}/lane={lane}/FC={fc}/bank={bank}/WB={slot}/byte={faultByte}";
            try { Run(batch, [width, width, width], 31, lane, fc, bank, slot, faultByte); report.Record(id, "passing", null); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }

    [EnvironmentFact("COPPER68K_RUN_040_MIXED_NESTED_WRITEBACK", "require mixed-width nested writebacks across all CCR values"), Trait("Suite", "ReferenceDiscovery")]
    public void MixedWidthsAndAllCcrScalar() => MixedWidthsAndAllCcr(false);

    [EnvironmentFact("COPPER68K_RUN_040_MIXED_NESTED_WRITEBACK", "require mixed-width nested writebacks across all CCR values"), Trait("Suite", "ReferenceDiscovery")]
    public void MixedWidthsAndAllCcrBatch() => MixedWidthsAndAllCcr(true);

    private void MixedWidthsAndAllCcr(bool batch)
    {
        var report = new CoverageBatch("68040", "mixed-nested-writeback-fault-" + (batch ? "batch" : "scalar"));
        foreach (var w1 in new[] { 1, 2, 4 })
        foreach (var w2 in new[] { 1, 2, 4 })
        foreach (var w3 in new[] { 1, 2, 4 })
        {
            if (w1 == w2 && w2 == w3) continue; // Existing same-width group stays separate.
            int[] widths = [w1, w2, w3];
            for (var ccr = 0; ccr < 32; ccr++)
            for (uint lane = 0; lane < 4; lane++)
            foreach (var fc in new[] { 1, 5 })
            foreach (var bank in new[] { "ISP", "MSP" })
            for (var slot = 1; slot <= 3; slot++)
            for (var faultByte = 0; faultByte < widths[slot - 1]; faultByte++)
            {
                var id = $"68040/writeback/mixed-nested-physical-fault/sizes={w1}-{w2}-{w3}/ccr={ccr:X2}/lane={lane}/FC={fc}/bank={bank}/WB={slot}/byte={faultByte}";
                try { Run(batch, widths, ccr, lane, fc, bank, slot, faultByte); report.Record(id, "passing", null); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
            }
        }
        report.Complete(output);
    }

    [EnvironmentFact("COPPER68K_RUN_040_NESTED_RETURN_BANKS", "require other same-width CCRs and supplied user-frame returns"), Trait("Suite", "ReferenceDiscovery")]
    public void OtherCcrAndUserReturnsScalar() => OtherCcrAndUserReturns(false);

    [EnvironmentFact("COPPER68K_RUN_040_NESTED_RETURN_BANKS", "require other same-width CCRs and supplied user-frame returns"), Trait("Suite", "ReferenceDiscovery")]
    public void OtherCcrAndUserReturnsBatch() => OtherCcrAndUserReturns(true);

    private void OtherCcrAndUserReturns(bool batch)
    {
        var report = new CoverageBatch("68040", "nested-writeback-return-banks-" + (batch ? "batch" : "scalar"));
        foreach (var bank in new[] { "user", "ISP", "MSP" })
        foreach (var w1 in new[] { 1, 2, 4 })
        foreach (var w2 in new[] { 1, 2, 4 })
        foreach (var w3 in new[] { 1, 2, 4 })
        {
            var same = w1 == w2 && w2 == w3;
            var allDifferent = w1 != w2 && w2 != w3 && w1 != w3;
            // Separate dimensions: complete the same-width CCR matrix and use
            // fixed width permutations for user returns, not a larger product.
            if (!same && (bank != "user" || !allDifferent)) continue;
            int[] widths = [w1, w2, w3];
            for (var ccr = 0; ccr < 32; ccr++)
            {
                if (bank != "user" && ccr == 31) continue; // Retained original group.
                for (uint lane = 0; lane < 4; lane++)
                foreach (var fc in new[] { 1, 5 })
                for (var slot = 1; slot <= 3; slot++)
                for (var faultByte = 0; faultByte < widths[slot - 1]; faultByte++)
                {
                    var id = $"68040/writeback/nested-return-bank/sizes={w1}-{w2}-{w3}/ccr={ccr:X2}/lane={lane}/FC={fc}/bank={bank}/WB={slot}/byte={faultByte}";
                    try { Run(batch, widths, ccr, lane, fc, bank, slot, faultByte); report.Record(id, "passing", null); }
                    catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
                }
            }
        }
        report.Complete(output);
    }

    [EnvironmentFact("COPPER68K_RUN_040_USER_MIXED_NESTED_WRITEBACK", "require remaining mixed-width supplied user-frame returns"), Trait("Suite", "ReferenceDiscovery")]
    public void RemainingUserMixedWidthsScalar() => RemainingUserMixedWidths(false);

    [EnvironmentFact("COPPER68K_RUN_040_USER_MIXED_NESTED_WRITEBACK", "require remaining mixed-width supplied user-frame returns"), Trait("Suite", "ReferenceDiscovery")]
    public void RemainingUserMixedWidthsBatch() => RemainingUserMixedWidths(true);

    private void RemainingUserMixedWidths(bool batch)
    {
        var report = new CoverageBatch("68040", "user-mixed-nested-writeback-fault-" + (batch ? "batch" : "scalar"));
        foreach (var w1 in new[] { 1, 2, 4 })
        foreach (var w2 in new[] { 1, 2, 4 })
        foreach (var w3 in new[] { 1, 2, 4 })
        {
            // Exactly two distinct widths: the preceding user-return slice
            // separately qualifies same-width and three-distinct-width triples.
            if ((w1 == w2 && w2 == w3) || (w1 != w2 && w2 != w3 && w1 != w3)) continue;
            int[] widths = [w1, w2, w3];
            for (var ccr = 0; ccr < 32; ccr++)
            for (uint lane = 0; lane < 4; lane++)
            foreach (var fc in new[] { 1, 5 })
            for (var slot = 1; slot <= 3; slot++)
            for (var faultByte = 0; faultByte < widths[slot - 1]; faultByte++)
            {
                var id = $"68040/writeback/user-mixed-nested-physical-fault/sizes={w1}-{w2}-{w3}/ccr={ccr:X2}/lane={lane}/FC={fc}/bank=user/WB={slot}/byte={faultByte}";
                try { Run(batch, widths, ccr, lane, fc, "user", slot, faultByte); report.Record(id, "passing", null); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
            }
        }
        report.Complete(output);
    }

    private static void Run(bool batch, int[] widths, int ccr, uint lane, int fc, string bank, int faultSlot, int faultByte)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        m.Reset(ccr);
        var sr = (ushort)((bank == "user" ? 0x0700 : bank == "MSP" ? 0x3700 : 0x2700) | ccr);
        // User-frame writebacks are serviced by supervisor code on ISP. The
        // supplied outer SR changes mode only when the outer RTE completes.
        var handlerBank = bank == "user" ? "ISP" : bank;
        var handlerSr = (ushort)((handlerBank == "MSP" ? 0x3700 : 0x2700) | ccr);
        var stacks = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
        var frame = stacks[handlerBank] - 60; stacks[handlerBank] = frame;
        var width = widths[faultSlot - 1];
        var size = width == 1 ? 0x20 : width == 2 ? 0x40 : 0;
        var wbStatus = (ushort)(0x80 | size | fc);
        var statuses = widths.Select(w => (ushort)(0x80 | (w == 1 ? 0x20 : w == 2 ? 0x40 : 0) | fc)).ToArray();
        uint[] addresses = [0x4200 + lane, 0x4300 + lane, 0x4400 + lane];
        uint[] values = [0x89abcdef, 0x10203040, 0xfedcba98];
        values = values.Select((x, n) => x & (widths[n] == 1 ? 0xffu : widths[n] == 2 ? 0xffffu : uint.MaxValue)).ToArray();
        for (uint at = frame; at < frame + 60; at++) m.InitializePhysical(at, 0, 1);
        m.InitializePhysical(frame, sr, 2); m.InitializePhysical(frame + 2, Next, 4);
        m.InitializePhysical(frame + 6, 0x7008, 2);
        m.InitializePhysical(frame + 12, (uint)((statuses[0] & 0x60) | fc), 2);
        m.InitializePhysical(frame + 20, addresses[0], 4);
        for (var n = 0; n < 3; n++)
        {
            m.InitializePhysical(frame + (uint)SyntheticM68040WritebackProgram.StatusOffset(n + 1), statuses[n], 2);
            m.InitializePhysical(frame + (uint)SyntheticM68040WritebackProgram.AddressOffset(n + 1), addresses[n], 4);
            if (n == 0)
                for (var b = 0; b < widths[0]; b++)
                    m.InitializePhysical(frame + 44 + ((addresses[0] + (uint)b) & 3), values[0] >> (8 * (widths[0] - 1 - b)), 1);
            else m.InitializePhysical(frame + (uint)SyntheticM68040WritebackProgram.DataOffset(n + 1), values[n], 4);
            for (uint b = 0; b < 12; b++) m.InitializePhysical(addresses[n] - 4 + b, 0x5a, 1);
        }
        uint[] data = [m.PeekPhysical(frame + 44, 4), values[1], values[2]];
        var program = new SyntheticM68040WritebackProgram(Handler); program.Initialize(m);
        m.InitializePhysical(8, Handler, 4); m.InitializePhysical(Next, 0x7e55, 2);
        m.InitializePhysical(Next + 2, 0x4e71, 2);
        m.Start(); m.Core.State.SetUserStackPointer(stacks["user"]);
        m.Core.State.SetInterruptStackPointer(stacks["ISP"]); m.Core.State.SetMasterStackPointer(stacks["MSP"]);
        m.Core.State.StatusRegister = handlerSr; m.Core.State.ProgramCounter = Handler;
        m.Core.State.DestinationFunctionCode = 7; m.Core.State.SourceFunctionCode = 6;
        var e = ArchitecturalExpectation.Capture(m); var sequence = m.Core.State.ExceptionSequence;
        e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, 7);
        e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, 6);
        e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence);
        var originalD = (uint[])e.D.Clone(); var originalA = (uint[])e.A.Clone();
        var stores = new List<(uint Address, int Width, uint Value)>(); var faulted = false; var steps = 0;
        bus.Accesses.Clear();
        while (e.Pc != Next)
        {
            if (++steps > 240) throw new InvalidOperationException("Outer handler did not terminate");
            var i = program.Instructions.Single(x => x.Pc == e.Pc);
            if (!faulted && i.Slot == faultSlot && i.Operation.StartsWith("store-"))
            {
                faulted = true;
                var savedSr = e.Sr; var resume = i.Pc + 4;
                var nested = stacks[handlerBank] - 60; stacks[handlerBank] = nested;
                M68040StackFixture.SetStacks(e, stacks, (ushort)(savedSr & 0x3fff));
                e.Pc = Handler; e.ExceptionVector = 2;
                e.ControlChecks["exception sequence"] = (s => s.ExceptionSequence, sequence + 1);
                e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, resume);
                e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, savedSr);
                for (uint b = 8; b < 60; b++) e.MemoryMasks[nested + b] = 0;
                Define(0, savedSr, 2); Define(2, resume, 4); Define(6, 0x7008, 2);
                Define(12, (uint)(size | fc), 2); e.MemoryMasks[nested + 13] = 0x7f;
                foreach (uint at in new uint[] { 14, 16 }) { Define(at, 0, 2); e.MemoryMasks[nested + at + 1] = 0x80; }
                Define(18, wbStatus, 2); Define(20, addresses[faultSlot - 1], 4); Define(40, addresses[faultSlot - 1], 4);
                for (var b = 0; b < width; b++)
                {
                    var at = nested + 44 + ((addresses[faultSlot - 1] + (uint)b) & 3);
                    e.Memory[at] = (byte)(values[faultSlot - 1] >> (8 * (width - 1 - b))); e.MemoryMasks[at] = 255;
                }
                bus.Arm(addresses[faultSlot - 1] + (uint)faultByte, M68kBusAccessKind.CpuDataWrite);
                Execute("nested fault entry");
                if (bus.Rejected.Count != 1) throw new InvalidOperationException("Missing unique handler-store rejection");
                for (uint b = 8; b < 60; b++) e.Memory[nested + b] = bus.Peek(nested + b);
                e.MemoryMasks.Clear();
                ushort[] ns = [wbStatus, 0, 0]; uint[] na = [addresses[faultSlot - 1], 0, 0];
                uint[] nd = [m.PeekPhysical(nested + 44, 4), 0, 0];
                var nestedD = (uint[])e.D.Clone(); var nestedA = (uint[])e.A.Clone();
                var nestedStores = new List<(uint Address, int Width, uint Value)>(); var nsteps = 0;
                // The reentrant handler shares code addresses with its caller.
                // Seeing the resume PC inside the nested handler is not a return.
                var returned = false;
                while (!returned)
                {
                    if (++nsteps > 120) throw new InvalidOperationException("Nested handler did not terminate");
                    var ni = program.Instructions.Single(x => x.Pc == e.Pc);
                    program.Expect(ni, m, e, nested, ns, na, nd, nestedD, nestedA, (uint)fc, stacks, nestedStores);
                    if (ni.Operation == "return")
                    { stacks[handlerBank] = nested + 60; M68040StackFixture.SetStacks(e, stacks, savedSr); e.Pc = resume; returned = true; }
                    Execute("nested " + ni.Operation);
                }
                if (!nestedStores.SequenceEqual(new[] { (addresses[faultSlot - 1], width, values[faultSlot - 1]) }))
                    throw new InvalidOperationException("Nested handler completed wrong pending store");
                stores.Add((addresses[faultSlot - 1], width, values[faultSlot - 1]));

                void Define(uint at, uint value, int count)
                { e.Write(nested + at, value, count, m.Model); for (uint b = 0; b < count; b++) e.MemoryMasks[nested + at + b] = 255; }
            }
            else
            {
                program.Expect(i, m, e, frame, statuses, addresses, data, originalD, originalA, 7, stacks, stores);
                if (i.Operation == "return")
                { stacks[handlerBank] = frame + 60; M68040StackFixture.SetStacks(e, stacks, sr); e.Pc = Next; }
                Execute("outer " + i.Operation);
            }
        }
        if (!faulted || !stores.SequenceEqual(addresses.Select((a, n) => (a, widths[n], values[n]))))
            throw new InvalidOperationException("WB1/WB2/WB3 completion order differs");
        var operandWrites = bus.Accesses.Where(a => a.Write && addresses.Contains(a.Address))
            .Select(a => (a.Address, a.Width, a.Value));
        if (!operandWrites.SequenceEqual(stores)) throw new InvalidOperationException("A handler store was repeated or omitted");
        e.Pc = Next + 2; e.D[7] = 0x55; e.Sr &= 0xfff0; Execute("following MOVEQ");

        void Execute(string stage)
        {
            if (batch)
            { if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, m.Core.State.Cycles + 10000, new Boundary()) != 1) throw new InvalidOperationException("Empty batch"); }
            else m.Core.ExecuteInstruction();
            if (e.Verify(m) is { } mismatch) throw new InvalidOperationException(stage + ": " + mismatch);
        }
    }
    private sealed class Boundary : IM68kInstructionBoundary
    { public bool BeforeInstruction() => true; public void AfterInstruction(long previousCycle, long currentCycle) { } }
}
