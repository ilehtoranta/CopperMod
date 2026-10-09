using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Actual simple MOVE origins followed by a rejected stack/vector entry request.
// Fixed private C023 layout and logical register effects, not silicon timing.
public sealed class SyntheticM68020MoveOriginEntryFaultTests(ITestOutputHelper output)
{
    private sealed record Case(int Width, string Bank, int Location, int Source, int Destination, bool Post, int Value, int Ccr, int OperandByte, int Entry, int EntryByte);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_ORIGIN_ENTRY", "require private actual-MOVE entry-fault audit"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarMoveOriginEntryFaults() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_ORIGIN_ENTRY", "require private actual-MOVE entry-fault audit"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchMoveOriginEntryFaults() => Audit(true);

    private static IEnumerable<Case> Cases(string kind, int selectedWidth, string? selectedBank)
    {
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var location in new[] { 0, 1 })
        {
            if (selectedWidth != 0 && (width != selectedWidth || bank != selectedBank)) continue;
            if (kind == "opcodes")
            {
                for (var source = 0; source < 8; source++)
                for (var destination = 0; destination < 8; destination++)
                foreach (var post in new[] { false, true })
                    yield return new(width, bank, location, source, destination, post, 2, 31, 0, 0, 0);
            }
            else if (kind == "boundaries")
            {
                for (var value = 0; value < 4; value++)
                for (var ccr = 0; ccr < 32; ccr++)
                    yield return new(width, bank, location, 0, 1, true, value, ccr, 0, 0, 0);
            }
            else
            {
                foreach (var source in new[] { 0, 7 })
                foreach (var alias in new[] { false, true })
                foreach (var post in new[] { false, true })
                foreach (var ccr in new[] { 0, 31 })
                for (var operandByte = 0; operandByte < width; operandByte++)
                for (var entry = 0; entry < 17; entry++)
                for (var entryByte = 0; entryByte < (entry == 16 ? 4 : 2); entryByte++)
                    yield return new(width, bank, location, source, alias ? source : 1, post, 2, ccr, operandByte, entry, entryByte);
            }
        }
    }
    private static IEnumerable<(int Width, string? Bank)> Slices(string kind)
    {
        if (kind != "entry") { yield return (0, null); yield break; }
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" }) yield return (width, bank);
    }
    private static ushort Opcode(Case c) => (ushort)((c.Width == 1 ? 0x1090 : c.Width == 2 ? 0x3090 : 0x2090) | c.Source | c.Destination << 9 | (c.Post ? 8 : 0));
    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200"))
        foreach (var kind in new[] { "opcodes", "entry", "boundaries" })
        foreach (var slice in Slices(kind))
        {
            var suffix = kind == "entry" ? $"-{slice.Width}-{slice.Bank}" : "";
            var report = new CoverageBatch(model.Id, $"move-origin-entry-{kind}{suffix}-{(batch ? "batch" : "scalar")}");
            var bus = new EntryBus(); var m = new SyntheticMachine(model, bus);
            foreach (var c in Cases(kind, slice.Width, slice.Bank))
            {
                var id = $"{model.Id}/MOVE/origin-entry/{kind}/opcode={Opcode(c):X4}/width={c.Width}/bank={c.Bank}/location={c.Location}/source={c.Source}/destination={c.Destination}/post={c.Post}/value={c.Value}/ccr={c.Ccr}/operand-byte={c.OperandByte}/entry={c.Entry}/entry-byte={c.EntryByte}";
                try { Run(m, bus, batch, c); report.Record(id, "passing", null); }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }
    private static void Run(SyntheticMachine m, EntryBus bus, bool batch, Case c)
    {
        bus.Disarm(); m.Reset(); var opcode = Opcode(c); _ = SyntheticExecution.Prepare(m, [opcode, 0x7e2a, 0x4e71]);
        var origin = c.Location == 0 ? 0u : 0x12340000u; var vbr = c.Location == 0 ? 0u : 0x12345000u;
        var usp = origin + 0x6800; var isp = origin + 0x6000; var msp = origin + 0x7400;
        var sr = (ushort)(0x700 | c.Ccr | (c.Bank is "ISP" or "MSP" ? 0x2000 : 0) | (c.Bank is "user-M" or "MSP" ? 0x1000 : 0));
        var s = m.Core.State;
        for (var n = 0; n < 7; n++) s.A[n] = origin + 0x4200u + (uint)n * 0x100;
        s.SetUserStackPointer(usp); s.SetInterruptStackPointer(isp); s.SetMasterStackPointer(msp); s.StatusRegister = sr; s.VectorBaseRegister = vbr;
        var source = s.A[c.Source]; var stride = c.Width == 1 && c.Source == 7 ? 2u : (uint)c.Width;
        var destination = s.A[c.Destination] + (c.Post && c.Source == c.Destination ? stride : 0);
        var mask = c.Width == 4 ? uint.MaxValue : (1u << (c.Width * 8)) - 1;
        var value = c.Value switch { 0 => 0u, 1 => mask >> 1, 2 => 1u << (c.Width * 8 - 1), _ => mask };
        foreach (var at in new[] { source, destination, usp, isp, msp })
            for (var offset = -40; offset < 40; offset++) m.InitializePhysical(unchecked(at + (uint)offset), 0xa5, 1);
        m.InitializePhysical(source, value, c.Width); m.InitializePhysical(vbr + 8, 0x9020, 4);
        var e = ArchitecturalExpectation.Capture(m); var serial = s.ExceptionSequence;
        var saved = (ushort)((sr & 0xfff0) | (c.Value == 0 ? 4 : c.Value >= 2 ? 8 : 0));
        if (c.Post)
        {
            if (c.Source != 7) e.A[c.Source] += stride;
            else if (c.Bank == "ISP") isp += stride;
            else if (c.Bank == "MSP") msp += stride;
            else usp += stride;
        }
        var master = (sr & 0x1000) != 0; var stack = master ? msp : isp;
        var secondary = c.Entry == 16 ? vbr + 8 : stack - (uint)(c.Entry + 1) * 2;
        var words = new ushort[16];
        words[0] = saved; words[1] = 0; words[2] = 0x1002; words[3] = 0xa008; words[4] = 0xc023;
        words[5] = (ushort)(0x100 | (c.Width == 1 ? 0x10 : c.Width == 2 ? 0x20 : 0) | ((sr & 0x2000) == 0 ? 1 : 5));
        words[8] = (ushort)(destination >> 16); words[9] = (ushort)destination; words[10] = opcode; words[11] = 1;
        words[12] = (ushort)(value >> 16); words[13] = (ushort)value;
        var completed = c.Entry == 16 ? 16 : c.Entry;
        for (var n = 0; n < completed; n++) e.Write(stack - (uint)(n + 1) * 2, words[15 - n], 2, m.Model);
        var finalSp = c.Entry == 16 ? stack - 32 : secondary;
        if (master) msp = finalSp; else isp = finalSp;
        e.A[7] = finalSp; e.InactiveStackPointer = usp; e.MasterStackPointer = msp;
        e.Pc = 0x1002; e.Sr = (ushort)(saved | 0x2000); e.Halted = true; e.ExceptionVector = 2;
        e.ControlChecks["saved SR"] = (x => x.LastExceptionStatusRegister, saved);
        e.ControlChecks["saved PC"] = (x => x.LastExceptionStackedProgramCounter, 0x1002);
        e.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, serial + 1);
        e.ControlChecks["VBR"] = (x => x.VectorBaseRegister, vbr);
        e.ControlChecks["SFC"] = (x => x.SourceFunctionCode, 0); e.ControlChecks["DFC"] = (x => x.DestinationFunctionCode, 0);
        bus.Accesses.Clear(); bus.Arm(m.Model.Physical(destination + (uint)c.OperandByte), c.Width,
            m.Model.Physical(secondary + (uint)c.EntryByte), c.Entry == 16 ? 4 : 2,
            c.Entry == 16 ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuDataWrite);
        Step(m, batch); Check(m, e); CheckBanks(m, usp, isp, msp);
        if (!bus.Rejected.SequenceEqual(new[] { (m.Model.Physical(destination), c.Width, M68kBusAccessKind.CpuDataWrite),
            (m.Model.Physical(secondary), c.Entry == 16 ? 4 : 2, c.Entry == 16 ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuDataWrite) })) throw new InvalidOperationException("Wrong operand/entry fault requests");
        if (!bus.Accesses.Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead).Select(a => (a.Address, a.Width, a.Value)).SequenceEqual(new[] { (m.Model.Physical(source), c.Width, value) })) throw new InvalidOperationException("Origin entry fault reread or misplaced the source");
        if (!bus.Accesses.Where(a => a.Write).Select(a => (a.Address, a.Width, a.Value)).SequenceEqual(Enumerable.Range(0, completed).Select(n => (m.Model.Physical(stack - (uint)(n + 1) * 2), 2, (uint)words[15 - n])))) throw new InvalidOperationException("Origin entry fault repeated/reordered a stack or operand write");
        var accesses = bus.Accesses.Count;
        if (batch) { var b = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, b) != 0 || b.Before != 0 || b.After != 0) throw new InvalidOperationException("Halted origin executed a batch boundary"); }
        else m.Core.ExecuteInstruction();
        Check(m, e); CheckBanks(m, usp, isp, msp);
        if (bus.Accesses.Count != accesses || bus.Rejected.Count != 2) throw new InvalidOperationException("Halted origin retried a bus access");
    }
    private static void CheckBanks(SyntheticMachine m, uint usp, uint isp, uint msp)
    { if (m.Core.State.UserStackPointer != usp || m.Core.State.InterruptStackPointer != isp || m.Core.State.MasterStackPointer != msp) throw new InvalidOperationException("Origin entry fault changed an inactive stack bank"); }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { if (e.Verify(m) is { } error) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    { if (!batch) m.Core.ExecuteInstruction(); else { var b = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, b) != 1 || b.Before != 1 || b.After != 1) throw new InvalidOperationException("Origin entry batch boundaries differ"); } }
    private sealed class Boundary : IM68kInstructionBoundary { internal int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class EntryBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private uint operand, secondary; private int operandWidth, secondaryWidth, phase; private M68kBusAccessKind secondaryKind;
        internal readonly List<(uint Address, int Width, M68kBusAccessKind Kind)> Rejected = [];
        internal void Disarm() { phase = 0; Rejected.Clear(); }
        internal void Arm(uint first, int firstWidth, uint second, int secondWidth, M68kBusAccessKind kind)
        { Disarm(); operand = first; operandWidth = firstWidth; secondary = second; secondaryWidth = secondWidth; secondaryKind = kind; phase = 1; }
        public bool IsCpuPhysicalAddressMapped(uint at, int size, M68kBusAccessKind kind)
        {
            if (phase == 1 && kind == M68kBusAccessKind.CpuDataWrite && size == operandWidth && unchecked(operand - at) < size)
            { Rejected.Add((at, size, kind)); phase = 2; return false; }
            if (phase == 2 && kind == secondaryKind && size == secondaryWidth && unchecked(secondary - at) < size)
            { Rejected.Add((at, size, kind)); phase = 0; return false; }
            return true;
        }
    }
}
