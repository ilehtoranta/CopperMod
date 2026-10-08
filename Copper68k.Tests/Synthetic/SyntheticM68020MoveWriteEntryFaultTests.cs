using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Secondary entry faults of the bounded C021/C023 software continuation.
// Enumerate all entry bytes separately from the all-CCR canonical group.
public sealed class SyntheticM68020MoveWriteEntryFaultTests(ITestOutputHelper output)
{
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_ENTRY_FAULT", "require private validation entry-fault audit"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarWriteValidationEntryFaults() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_ENTRY_FAULT", "require private validation entry-fault audit"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchWriteValidationEntryFaults() => Audit(true);

    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200"))
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var allCcr in new[] { false, true })
        {
            var kind = allCcr ? "ccr" : "enumeration";
            var report = new CoverageBatch(model.Id, $"move-write-entry-{kind}-{bank}-{(batch ? "batch" : "scalar")}");
            var bus = new EntryBus(); var m = new SyntheticMachine(model, bus);
            foreach (var location in new[] { 0, 1 })
            foreach (var ccr in allCcr ? Enumerable.Range(0, 32) : new[] { 31 })
            for (var request = 0; request < 4; request++)
            for (var validationByte = 0; validationByte < (request == 1 ? 4 : 2); validationByte++)
            foreach (var entry in allCcr ? new[] { 0 } : Enumerable.Range(0, 47))
            for (var entryByte = 0; entryByte < (entry == 46 ? 4 : 2); entryByte++)
            {
                var id = $"{model.Id}/RTE/write-validation-entry/{kind}/bank={bank}/location={location}/ccr={ccr}/request={request}/byte={validationByte}/entry={entry}/entry-byte={entryByte}";
                try { Run(m, bus, batch, bank, location, ccr, request, validationByte, entry, entryByte); report.Record(id, "passing", null); }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void Run(SyntheticMachine m, EntryBus bus, bool batch, string bank, int location, int ccr, int request, int validationByte, int entry, int entryByte)
    {
        bus.Disarm(); m.Reset(); _ = SyntheticExecution.Prepare(m, [0x4e73, 0x4e71]);
        var frame = location == 0 ? 0x6000u : 0x12346000u;
        var vbr = location == 0 ? 0u : 0x12345000u;
        var saved = (ushort)(0x700 | ccr); var current = (ushort)((bank == "ISP" ? 0x2700 : 0x3700) | ccr);
        for (uint at = frame - 100; at < frame + 40; at++) m.InitializePhysical(at, 0xa5, 1);
        for (uint at = 0x43f8; at < 0x4510; at++) m.InitializePhysical(at, 0xa5, 1);
        m.InitializePhysical(vbr + 8, 0x9020, 4);
        m.InitializePhysical(frame, saved, 2); m.InitializePhysical(frame + 2, 0x2000, 4);
        m.InitializePhysical(frame + 6, 0xa008, 2); m.InitializePhysical(frame + 8, 0xc023, 2);
        m.InitializePhysical(frame + 10, 0x125, 2); m.InitializePhysical(frame + 12, 0x7e11, 2);
        m.InitializePhysical(frame + 14, 0x7c22, 2); m.InitializePhysical(frame + 16, 0x4400, 4);
        m.InitializePhysical(frame + 20, 0x329f, 2); m.InitializePhysical(frame + 22, 0x301, 2);
        m.InitializePhysical(frame + 24, 0xb2c3, 4); m.InitializePhysical(frame + 28, 0x7a33, 2); m.InitializePhysical(frame + 30, 0, 2);
        var s = m.Core.State; s.A[0] = 0x4204; s.A[1] = 0x4500; s.VectorBaseRegister = vbr;
        s.SetUserStackPointer(0x7800); s.SetInterruptStackPointer(bank == "ISP" ? frame : 0x4700); s.SetMasterStackPointer(bank == "MSP" ? frame : 0x7400);
        s.StatusRegister = current; s.SetActiveStackPointer(frame);
        var e = ArchitecturalExpectation.Capture(m); var serial = s.ExceptionSequence;
        var requests = new (uint Offset, int Width)[] { (0, 2), (2, 4), (6, 2), (30, 2) };
        var rejected = requests[request]; var vectorFault = entry == 46;
        var secondary = vectorFault ? vbr + 8 : frame - (uint)(entry + 1) * 2;
        bus.Accesses.Clear(); bus.Arm(m.Model.Physical(frame + rejected.Offset + (uint)validationByte), rejected.Width,
            m.Model.Physical(secondary + (uint)entryByte), vectorFault ? 4 : 2,
            vectorFault ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuDataWrite);
        Step(m, batch);
        if (!bus.Rejected.SequenceEqual(new[] { (m.Model.Physical(frame + rejected.Offset), rejected.Width, M68kBusAccessKind.CpuDataRead),
            (m.Model.Physical(secondary), vectorFault ? 4 : 2, vectorFault ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuDataWrite) }))
            throw new InvalidOperationException("Wrong validation/entry fault requests");
        // Fixed private layout, independent of the production frame builder.
        var words = new ushort[46];
        void Long(int offset, uint value) { words[offset / 2] = (ushort)(value >> 16); words[offset / 2 + 1] = (ushort)value; }
        words[0] = current; Long(2, 0x1000); words[3] = 0xb008; words[4] = 0xc021;
        words[5] = (ushort)(0x145 | (rejected.Width == 2 ? 0x20 : 0));
        Long(16, frame + rejected.Offset); Long(20, frame);
        Long(28, (uint)((request >= 1 ? saved : 0) << 16) | (request >= 3 ? 0xa008u : 0));
        Long(32, request >= 2 ? 0x2000u : 0); words[20] = (ushort)(request == 3 ? 4 : request);
        var completed = vectorFault ? 46 : entry;
        for (var n = 0; n < completed; n++) e.Write(frame - (uint)(n + 1) * 2, words[45 - n], 2, m.Model);
        var sp = vectorFault ? frame - 92 : secondary;
        e.A[7] = sp; if (bank == "MSP") e.MasterStackPointer = sp;
        e.Pc = 0x1002; e.Halted = true; e.ExceptionVector = 2;
        e.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, serial + 1);
        e.ControlChecks["saved PC"] = (x => x.LastExceptionStackedProgramCounter, 0x1000);
        e.ControlChecks["saved SR"] = (x => x.LastExceptionStatusRegister, current);
        Check(m, e);
        if (s.UserStackPointer != 0x7800 || s.InterruptStackPointer != (bank == "ISP" ? sp : 0x4700u) ||
            s.MasterStackPointer != (bank == "MSP" ? sp : 0x7400u) || s.VectorBaseRegister != vbr)
            throw new InvalidOperationException("Entry fault changed inactive stacks or VBR");
        var reads = bus.Accesses.Where(x => !x.Write && x.Kind == M68kBusAccessKind.CpuDataRead).Select(x => (x.Address, x.Width));
        if (!reads.SequenceEqual(requests.Take(request).Select(x => (m.Model.Physical(frame + x.Offset), x.Width))))
            throw new InvalidOperationException("Entry fault replayed validation or read the pending operand");
        var writes = bus.Accesses.Where(x => x.Write).Select(x => (x.Address, x.Width, x.Value));
        if (!writes.SequenceEqual(Enumerable.Range(0, completed).Select(n => (m.Model.Physical(frame - (uint)(n + 1) * 2), 2, (uint)words[45 - n]))))
            throw new InvalidOperationException("Entry fault lost/repeated a completed stack write");
        var accesses = bus.Accesses.Count;
        if (batch) { if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, new Boundary()) != 0) throw new InvalidOperationException("Halted entry ran a batch instruction"); }
        else m.Core.ExecuteInstruction();
        Check(m, e);
        if (bus.Accesses.Count != accesses || bus.Rejected.Count != 2) throw new InvalidOperationException("Halted entry retried a bus access");
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { if (e.Verify(m) is { } error) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) m.Core.ExecuteInstruction();
        else if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, new Boundary()) != 1) throw new InvalidOperationException("Empty entry-fault batch");
    }
    private sealed class Boundary : IM68kInstructionBoundary { public bool BeforeInstruction() => true; public void AfterInstruction(long a, long b) { } }
    private sealed class EntryBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private uint validation, secondary; private int validationWidth, secondaryWidth;
        private M68kBusAccessKind secondaryKind; private int phase;
        internal readonly List<(uint Address, int Width, M68kBusAccessKind Kind)> Rejected = [];
        internal void Disarm() { phase = 0; Rejected.Clear(); }
        internal void Arm(uint first, int firstWidth, uint second, int secondWidth, M68kBusAccessKind kind)
        { Disarm(); validation = first; validationWidth = firstWidth; secondary = second; secondaryWidth = secondWidth; secondaryKind = kind; phase = 1; }
        public bool IsCpuPhysicalAddressMapped(uint at, int size, M68kBusAccessKind kind)
        {
            if (phase == 1 && kind == M68kBusAccessKind.CpuDataRead && size == validationWidth && unchecked(validation - at) < size)
            { Rejected.Add((at, size, kind)); phase = 2; return false; }
            if (phase == 2 && kind == secondaryKind && size == secondaryWidth && unchecked(secondary - at) < size)
            { Rejected.Add((at, size, kind)); phase = 0; return false; }
            return true;
        }
    }
}
