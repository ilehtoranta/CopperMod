using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Cold private C023 frames, two explicit pending-store faults and actual
// handler RTEs. No original MOVE is retried and no silicon frame is asserted.
public sealed class SyntheticM68020MoveWriteRefaultTests(ITestOutputHelper output)
{
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_REFAULT", "require private returned-bank pending-store refault audit"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarWriteRefaultContinuation() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_REFAULT", "require private returned-bank pending-store refault audit"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchWriteRefaultContinuation() => Audit(true);

    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200"))
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var current in new[] { "ISP", "MSP" })
        foreach (var mode in new[] { "mapped", "software" })
        {
            var report = new CoverageBatch(model.Id, $"move-write-refault-{width}-{current}-{mode}-{(batch ? "batch" : "scalar")}");
            var bus = new RefaultBus(); var m = new SyntheticMachine(model, bus);
            foreach (var returned in new[] { "user", "user-M", "ISP", "MSP" })
            foreach (var location in new[] { 0, 1 })
            foreach (var ccr in Enumerable.Range(0, 32))
            foreach (var fc in new[] { 1, 5 })
            foreach (var count in Enumerable.Range(0, 4))
            for (var lane = 0; lane < width; lane++)
            {
                var id = $"{model.Id}/MOVE/write-refault/width={width}/current={current}/returned={returned}/location={location}/ccr={ccr}/fc={fc}/count={count}/mode={mode}/byte={lane}";
                try { Run(m, bus, batch, width, current, returned, location, ccr, fc, count, mode, lane); report.Record(id, "passing", null); }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void Run(SyntheticMachine m, RefaultBus bus, bool batch, int width, string current, string returned, int location, int ccr, int fc, int count, string mode, int lane)
    {
        bus.Disarm(); m.Reset(); _ = SyntheticExecution.Prepare(m, [0x4e73, 0x4e71]);
        var frame = location == 0 ? 0x6000u : 0x12346000u;
        var saved = (ushort)(0x700 | ccr | (returned is "ISP" or "MSP" ? 0x2000 : 0) | (returned is "user-M" or "MSP" ? 0x1000 : 0));
        var currentSr = (ushort)((current == "ISP" ? 0x2700 : 0x3700) | ccr);
        var value = 0x80a1b2c3u & (width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue);
        var opcode = (ushort)(width == 1 ? 0x129f : width == 2 ? 0x329f : 0x229f);
        var ssw = (ushort)(0x100 | (width == 1 ? 0x10 : width == 2 ? 0x20 : 0) | fc);
        var isp = current == "ISP" ? frame + 32 : 0x4700u;
        var msp = current == "MSP" ? frame + 32 : 0x7400u;
        var faultBank = (saved & 0x1000) == 0 ? "ISP" : "MSP";
        var faultFrame = (faultBank == "ISP" ? isp : msp) - 32;
        foreach (var origin in new[] { frame, faultFrame })
            for (uint at = origin - 8; at < origin + 40; at++) m.InitializePhysical(at, 0xa5, 1);
        for (uint at = 0x43f8; at < 0x4510; at++) m.InitializePhysical(at, 0xa5, 1);
        m.InitializePhysical(8, 0x9020, 4);
        m.InitializePhysical(0x9020, 0x4e73, 2); m.InitializePhysical(0x9022, 0x4e71, 2);
        for (var n = 0; n < 4; n++) m.InitializePhysical(0x2000u + (uint)n * 2, (uint)(n == 3 ? 0x4e71 : new[] { 0x7e2a, 0x7c2b, 0x7a2c }[n]), 2);
        m.InitializePhysical(frame, saved, 2); m.InitializePhysical(frame + 2, 0x2000, 4);
        m.InitializePhysical(frame + 6, 0xa008, 2); m.InitializePhysical(frame + 8, 0xc023, 2);
        m.InitializePhysical(frame + 10, ssw, 2); m.InitializePhysical(frame + 12, 0x7e11, 2); m.InitializePhysical(frame + 14, 0x7c22, 2);
        m.InitializePhysical(frame + 16, 0x4400, 4); m.InitializePhysical(frame + 20, opcode, 2);
        m.InitializePhysical(frame + 22, (uint)(1 | count << 8), 2); m.InitializePhysical(frame + 24, value, 4);
        m.InitializePhysical(frame + 28, 0x7a33, 2); m.InitializePhysical(frame + 30, 0, 2);
        var s = m.Core.State; s.A[0] = 0x4204; s.A[1] = 0x4500;
        s.SetUserStackPointer(0x7800); s.SetInterruptStackPointer(current == "ISP" ? frame : 0x4700); s.SetMasterStackPointer(current == "MSP" ? frame : 0x7400);
        s.StatusRegister = currentSr; s.SetActiveStackPointer(frame);
        var e = ArchitecturalExpectation.Capture(m); var serial = s.ExceptionSequence;
        bus.Accesses.Clear(); bus.Arm(m.Model.Physical(0x4400u + (uint)lane), width);
        var words = new ushort[16]; words[0] = saved; words[1] = 0; words[2] = 0x2000; words[3] = 0xa008;
        words[4] = 0xc023; words[5] = ssw; words[8] = 0; words[9] = 0x4400; words[10] = opcode;
        words[11] = (ushort)(1 | count << 8); words[12] = (ushort)(value >> 16); words[13] = (ushort)value;
        for (var n = 0; n < count; n++) words[n == 0 ? 6 : n == 1 ? 7 : 14] = (ushort)new[] { 0x7e11, 0x7c22, 0x7a33 }[n];
        for (var n = 0; n < 16; n++) e.Write(faultFrame + (uint)n * 2, words[n], 2, m.Model);
        e.A[7] = faultFrame; e.InactiveStackPointer = 0x7800; e.MasterStackPointer = faultBank == "MSP" ? faultFrame : msp;
        e.Sr = (ushort)(saved | 0x2000); e.Pc = 0x9020; e.ExceptionVector = 2;
        e.ControlChecks["saved SR"] = (x => x.LastExceptionStatusRegister, saved);
        e.ControlChecks["saved PC"] = (x => x.LastExceptionStackedProgramCounter, 0x2000);
        Step(m, batch); Check(m, e); CheckBanks(m, faultBank == "ISP" ? faultFrame : isp, faultBank == "MSP" ? faultFrame : msp);
        CheckEntry(m, 0, faultFrame, words);
        if (bus.Rejected.Count != 1 || s.ExceptionSequence != serial + 1) throw new InvalidOperationException("Pending-store fault did not enter exactly once");
        var second = bus.Accesses.Count;
        Step(m, batch); Check(m, e); CheckBanks(m, faultBank == "ISP" ? faultFrame : isp, faultBank == "MSP" ? faultFrame : msp);
        CheckEntry(m, second, faultFrame, words);
        if (bus.Rejected.Count != 2 || s.ExceptionSequence != serial + 2) throw new InvalidOperationException("Explicit pending-store RTE did not refault exactly once");

        if (mode == "software")
        {
            var handler = new List<ushort> { (ushort)(width == 1 ? 0x13fc : width == 2 ? 0x33fc : 0x23fc) };
            if (width == 4) handler.Add((ushort)(value >> 16));
            handler.Add((ushort)value); handler.Add(0); handler.Add(0x4400);
            handler.AddRange([0x026f, 0xfeff, 0x000a, 0x4e73]);
            for (var n = 0; n < handler.Count; n++) m.InitializePhysical(0x9020u + (uint)n * 2, handler[n], 2);
            // Fixture initialization is separate from CPU stores and expectations.
            for (var n = 0; n < handler.Count; n++) e.Write(0x9020u + (uint)n * 2, handler[n], 2, m.Model);
            e.Write(0x4400, value, width, m.Model); e.Pc += width == 4 ? 10u : 8u; e.Sr = (ushort)((e.Sr & 0xfff0) | 8);
            Step(m, batch); Check(m, e);
            e.Write(faultFrame + 10, (uint)(ssw & ~0x100), 2, m.Model); e.Pc += 6; e.Sr = (ushort)(e.Sr & 0xfff0);
            Step(m, batch); Check(m, e);
        }
        var final = bus.Accesses.Count;
        e.A[7] = returned is "user" or "user-M" ? 0x7800u : returned == "ISP" ? isp : msp;
        e.InactiveStackPointer = returned is "user" or "user-M" ? isp : 0x7800u; e.MasterStackPointer = msp;
        e.Sr = saved; e.Pc = 0x2000; e.Write(0x4400, value, width, m.Model);
        Step(m, batch); Check(m, e); CheckBanks(m, isp, msp);
        if (!bus.Accesses.Skip(final).Where(a => a.Write).Select(a => (a.Address, a.Width, a.Value)).SequenceEqual(mode == "software" ? Array.Empty<(uint, int, uint)>() : new[] { (0x4400u, width, value) })) throw new InvalidOperationException("Pending-store completion lost or repeated its write");
        for (var n = 0; n < 3; n++)
        {
            e.D[7 - n] = (uint)(n < count ? new[] { 0x11, 0x22, 0x33 }[n] : 0x2a + n); e.Sr = (ushort)(e.Sr & 0xfff0); e.Pc += 2;
            var before = bus.Accesses.Count; Step(m, batch); Check(m, e); CheckBanks(m, isp, msp);
            if (bus.Accesses.Skip(before).Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == 0x2000u + (uint)n * 2) != (n < count ? 0 : 1)) throw new InvalidOperationException("Refault changed retained instruction words");
        }
        if (s.ExceptionSequence != serial + 2 || !bus.Rejected.SequenceEqual(new[] { (0x4400u, width), (0x4400u, width) })) throw new InvalidOperationException("Refault changed fault provenance");
        if (bus.Accesses.Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address != 8 &&
            !(a.Address >= m.Model.Physical(frame) && a.Address + (uint)a.Width <= m.Model.Physical(frame) + 32) &&
            !(a.Address >= m.Model.Physical(faultFrame) && a.Address + (uint)a.Width <= m.Model.Physical(faultFrame) + 32))) throw new InvalidOperationException("Refault reread the original operand");
        if (bus.Accesses.Count(a => a.Write && a.Address == 0x4400) != 1) throw new InvalidOperationException("Pending operand was not written exactly once");
    }
    private static void CheckEntry(SyntheticMachine m, int start, uint frame, ushort[] words)
    {
        if (!m.Bus.Accesses.Skip(start).Where(a => a.Write).Select(a => (a.Address, a.Width, a.Value)).SequenceEqual(Enumerable.Range(0, 16).Select(n => (m.Model.Physical(frame + 30 - (uint)n * 2), 2, (uint)words[15 - n])))) throw new InvalidOperationException("Refault lost/reordered completed stack writes");
    }
    private static void CheckBanks(SyntheticMachine m, uint isp, uint msp)
    { if (m.Core.State.UserStackPointer != 0x7800 || m.Core.State.InterruptStackPointer != isp || m.Core.State.MasterStackPointer != msp) throw new InvalidOperationException("Refault changed an inactive or completed stack bank"); }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { if (e.Verify(m) is { } error) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    { if (!batch) m.Core.ExecuteInstruction(); else if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, new Boundary()) != 1) throw new InvalidOperationException("Empty refault batch"); }
    private sealed class Boundary : IM68kInstructionBoundary { public bool BeforeInstruction() => true; public void AfterInstruction(long a, long b) { } }
    private sealed class RefaultBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private uint address; private int width, remaining;
        internal readonly List<(uint Address, int Width)> Rejected = [];
        internal void Disarm() { remaining = 0; Rejected.Clear(); }
        internal void Arm(uint at, int size) { Disarm(); address = at; width = size; remaining = 2; }
        public bool IsCpuPhysicalAddressMapped(uint at, int size, M68kBusAccessKind kind)
        { if (remaining == 0 || kind != M68kBusAccessKind.CpuDataWrite || size != width || unchecked(address - at) >= size) return true; remaining--; Rejected.Add((at, size)); return false; }
    }
}
