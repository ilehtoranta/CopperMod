using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Bounded private C023 validation continuations. Literal integer handlers
// supply a rejected validation value or explicitly refault before completion.
// Completed operand writes and earlier validation reads never repeat.
public sealed class SyntheticM68020MoveWriteValidationTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200").ToArray();
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_VALIDATION", "require private pending-write validation continuation"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarWriteValidationContinuation() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_VALIDATION", "require private pending-write validation continuation"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchWriteValidationContinuation() => Audit(true);

    private static (uint Offset, int Width)[] Requests(int count) =>
        new (uint, int)[] { (0, 2), (2, 4), (6, 2), (30, 2), (8, 2), (10, 2), (16, 4), (20, 2), (22, 2), (24, 4), (30, 2) }
        .Concat(Enumerable.Range(0, count).Select(n => (n == 0 ? 12u : n == 1 ? 14u : 28u, 2)))
        .Concat(Enumerable.Range(0, 10).Select(n => (12u + (uint)n * 2, 2))).ToArray();

    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var current in new[] { "ISP", "MSP" })
        foreach (var count in Enumerable.Range(0, 4))
        {
            var report = new CoverageBatch(model.Id, $"move-write-validation-{width}-{current}-count{count}-{(batch ? "batch" : "scalar")}");
            var bus = new FaultBus(); var m = new SyntheticMachine(model, bus); var requests = Requests(count);
            foreach (var returned in new[] { "user", "user-M", "ISP", "MSP" })
            foreach (var location in Enumerable.Range(0, 2))
            foreach (var ccr in new[] { 0, 31 })
            foreach (var software in new[] { false, true })
            foreach (var repairMode in new[] { "buffer", "refault" })
            foreach (var ri in Enumerable.Range(0, 4))
            foreach (var lane in Enumerable.Range(0, requests[ri].Width))
            {
                var id = $"{model.Id}/MOVE/write-frame-fault/width={width}/current={current}/returned={returned}/location={location}/count={count}/ccr={ccr}/software={software}/mode={repairMode}/request={ri}/byte={lane}";
                try { Run(m, bus, batch, width, current, returned, location, count, ccr, software, requests, ri, lane, repairMode); report.Record(id, "passing", null); }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void Run(SyntheticMachine m, FaultBus bus, bool batch, int width, string current, string returned, int location, int count, int ccr, bool software, (uint Offset, int Width)[] requests, int ri, int lane, string repairMode)
    {
        bus.Disarm(); m.Reset(); _ = SyntheticExecution.Prepare(m, [0x4e73, 0x4e71]);
        var frame = location == 0 ? 0x6000u : 0x12346000u;
        var saved = (ushort)(0x700 | ccr | (returned is "ISP" or "MSP" ? 0x2000 : 0) | (returned is "user-M" or "MSP" ? 0x1000 : 0));
        var currentSr = (ushort)((current == "ISP" ? 0x2700 : 0x3700) | ccr);
        var value = 0x80a1b2c3u & (width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue);
        for (uint a = frame - 100; a < frame + 40; a++) m.InitializePhysical(a, 0xa5, 1);
        for (uint a = 0x43f8; a < 0x4510; a++) m.InitializePhysical(a, 0xa5, 1);
        m.InitializePhysical(0x9020, 0x4e73, 2); m.InitializePhysical(0x9022, 0x4e71, 2);
        m.InitializePhysical(0x2000, 0x7e2a, 2); m.InitializePhysical(0x2002, 0x7c2b, 2); m.InitializePhysical(0x2004, 0x7a2c, 2); m.InitializePhysical(0x2006, 0x4e71, 2);
        m.InitializePhysical(frame, saved, 2); m.InitializePhysical(frame + 2, 0x2000, 4); m.InitializePhysical(frame + 6, 0xa008, 2); m.InitializePhysical(frame + 8, 0xc023, 2);
        m.InitializePhysical(frame + 10, (uint)((software ? 0 : 0x100) | (width == 1 ? 0x10 : width == 2 ? 0x20 : 0) | 5), 2);
        m.InitializePhysical(frame + 12, 0x7e11, 2); m.InitializePhysical(frame + 14, 0x7c22, 2); m.InitializePhysical(frame + 16, 0x4400, 4);
        m.InitializePhysical(frame + 20, (uint)(width == 1 ? 0x129f : width == 2 ? 0x329f : 0x229f), 2);
        m.InitializePhysical(frame + 22, (uint)(1 | count << 8), 2); m.InitializePhysical(frame + 24, value, 4); m.InitializePhysical(frame + 28, 0x7a33, 2); m.InitializePhysical(frame + 30, 0, 2);
        if (software) m.InitializePhysical(0x4400, value, width);
        m.Core.State.A[0] = 0x4204; m.Core.State.A[1] = 0x4500;
        m.Core.State.SetUserStackPointer(0x7800); m.Core.State.SetInterruptStackPointer(current == "ISP" ? frame : 0x4700); m.Core.State.SetMasterStackPointer(current == "MSP" ? frame : 0x7400);
        m.Core.State.StatusRegister = currentSr; m.Core.State.SetActiveStackPointer(frame);
        var supplied = ri switch { 0 => (uint)saved, 1 => 0x2000u, 2 => 0xa008u, _ => 0u };
        ushort[] handler = repairMode == "buffer"
            ? [0x2f7c, (ushort)(supplied >> 16), (ushort)supplied, 0x002c, 0x026f, 0xfeff, 0x000a, 0x4e73]
            : [0x4e73];
        for (var n = 0; n < handler.Length; n++) m.InitializePhysical(0x9020u + (uint)n * 2, handler[n], 2);
        var e = ArchitecturalExpectation.Capture(m); var serial = m.Core.State.ExceptionSequence;
        var request = requests[ri]; var byteOffset = request.Offset + (uint)lane;
        var occurrence = requests.Take(ri + 1).Count(x => x.Width == request.Width && byteOffset >= x.Offset && byteOffset < x.Offset + (uint)x.Width);
        m.Bus.Accesses.Clear(); bus.Arm(m.Model.Physical(frame + byteOffset), request.Width, occurrence);
        Step(m, batch);
        if (bus.Rejected.Count != 1 || bus.Rejected[0] != (m.Model.Physical(frame + request.Offset), request.Width)) throw new InvalidOperationException("Wrong selected frame fault request");
        CheckReads(m, frame, requests.Take(ri));
        var outer = frame - 92;
        for (uint n = 0; n < 92; n += 2) e.Write(outer + n, 0, 2, m.Model);
        e.Write(outer, currentSr, 2, m.Model); e.Write(outer + 2, 0x1000, 4, m.Model); e.Write(outer + 6, 0xb008, 2, m.Model); e.Write(outer + 8, 0xc021, 2, m.Model);
        e.Write(outer + 10, (uint)(0x145 | (request.Width == 2 ? 0x20 : 0)), 2, m.Model); e.Write(outer + 16, frame + request.Offset, 4, m.Model); e.Write(outer + 20, frame, 4, m.Model);
        e.Write(outer + 28, (uint)((ri >= 1 ? saved : 0) << 16) | (ri >= 3 ? 0xa008u : 0), 4, m.Model); e.Write(outer + 32, ri >= 2 ? 0x2000u : 0, 4, m.Model);
        e.Write(outer + 40, (uint)(ri == 3 ? 4 : ri), 2, m.Model);
        e.A[7] = outer; if (current == "MSP") e.MasterStackPointer = outer;
        e.Pc = 0x9020; e.ExceptionVector = 2; Check(m, e); CheckBanks(m, current, outer);
        if (m.Core.State.ExceptionSequence != serial + 1 || !m.Bus.Accesses.Where(a => a.Write).Select(a => (a.Address, a.Width)).SequenceEqual(Enumerable.Range(0, 46).Select(n => (m.Model.Physical(frame - 2u - (uint)n * 2), 2))))
            throw new InvalidOperationException("Validation fault changed exception count or entry writes");

        if (repairMode == "buffer")
        {
            e.Write(outer + 44, supplied, 4, m.Model); e.Pc += 8;
            e.Sr = (ushort)((e.Sr & 0xfff0) | (supplied == 0 ? 4 : 0) | (supplied >= 0x80000000 ? 8 : 0));
            Step(m, batch); Check(m, e); CheckBanks(m, current, outer);
            e.Write(outer + 10, (uint)(0x45 | (request.Width == 2 ? 0x20 : 0)), 2, m.Model);
            e.Pc += 6; e.Sr = (ushort)(e.Sr & 0xfff0);
            Step(m, batch); Check(m, e); CheckBanks(m, current, outer);
        }
        else
        {
            bus.Repeat(); var beforeRefault = m.Bus.Accesses.Count;
            Step(m, batch); Check(m, e); CheckBanks(m, current, outer);
            if (bus.Rejected.Count != 2 || m.Core.State.ExceptionSequence != serial + 2)
                throw new InvalidOperationException("Explicit validation RTE did not refault exactly once");
            CheckReads(m, frame, Array.Empty<(uint Offset, int Width)>(), beforeRefault);
        }
        var start = m.Bus.Accesses.Count;
        var isp = current == "ISP" ? frame + 32 : 0x4700u; var msp = current == "MSP" ? frame + 32 : 0x7400u;
        e.A[7] = returned == "ISP" ? isp : returned == "MSP" ? msp : 0x7800u;
        e.InactiveStackPointer = returned is "ISP" or "MSP" ? 0x7800u : isp; e.MasterStackPointer = msp;
        e.Sr = saved; e.Pc = 0x2000; e.Write(0x4400, value, width, m.Model);
        Step(m, batch); Check(m, e); CheckBanks(m, current, frame + 32);
        CheckReads(m, frame, requests.Skip(ri + (repairMode == "buffer" ? 1 : 0)), start);
        if (!m.Bus.Accesses.Skip(start).Where(a => a.Write).Select(a => (a.Address, a.Width)).SequenceEqual(software ? Array.Empty<(uint, int)>() : new[] { (0x4400u, width) }))
            throw new InvalidOperationException("Recovered validation fault repeated or misplaced the pending write");
        for (var n = 0; n < 3; n++)
        {
            e.D[7 - n] = (uint)(n < count ? new[] { 0x11, 0x22, 0x33 }[n] : 0x2a + n); e.Sr = (ushort)(e.Sr & 0xfff0); e.Pc += 2;
            var before = m.Bus.Accesses.Count; Step(m, batch); Check(m, e); CheckBanks(m, current, frame + 32);
            if (m.Bus.Accesses.Skip(before).Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == 0x2000u + (uint)n * 2) != (n < count ? 0 : 1))
                throw new InvalidOperationException("Recovered frame fault changed retained instruction words");
        }
        if (m.Core.State.ExceptionSequence != serial + (repairMode == "refault" ? 2u : 1u) || bus.Rejected.Count != (repairMode == "refault" ? 2 : 1) || m.Bus.Accesses.Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead &&
            !(a.Address >= m.Model.Physical(outer) && a.Address + (uint)a.Width <= m.Model.Physical(frame) + 32) && a.Address != 8))
            throw new InvalidOperationException("Recovered frame fault replayed source or changed exception provenance");
    }

    private static void CheckReads(SyntheticMachine m, uint frame, IEnumerable<(uint Offset, int Width)> expected, int start = 0)
    {
        var reads = m.Bus.Accesses.Skip(start).Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= m.Model.Physical(frame) && a.Address < m.Model.Physical(frame) + 32).Select(a => (a.Address, a.Width));
        if (!reads.SequenceEqual(expected.Select(x => (m.Model.Physical(frame + x.Offset), x.Width)))) throw new InvalidOperationException("Frame fault replayed or skipped completed validation/load reads");
    }
    private static void CheckBanks(SyntheticMachine m, string current, uint stack)
    {
        var isp = current == "ISP" ? stack : 0x4700u; var msp = current == "MSP" ? stack : 0x7400u;
        if (m.Core.State.UserStackPointer != 0x7800 || m.Core.State.InterruptStackPointer != isp || m.Core.State.MasterStackPointer != msp)
            throw new InvalidOperationException("Frame fault changed an inactive or completed stack bank");
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { var error = e.Verify(m); if (error != null) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) { m.Core.ExecuteInstruction(); return; }
        var b = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, b) != 1 || b.Before != 1 || b.After != 1) throw new InvalidOperationException("Frame fault batch boundaries differ");
    }
    private sealed class Boundary : IM68kInstructionBoundary { public int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class FaultBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private uint address; private int width, occurrence;
        internal readonly List<(uint Address, int Width)> Rejected = [];
        internal void Disarm() { occurrence = 0; Rejected.Clear(); }
        internal void Arm(uint at, int size, int nth) { Disarm(); address = at; width = size; occurrence = nth; }
        internal void Repeat() => occurrence = 1;
        public bool IsCpuPhysicalAddressMapped(uint at, int size, M68kBusAccessKind kind)
        {
            if (occurrence == 0 || kind != M68kBusAccessKind.CpuDataRead || size != width || unchecked(address - at) >= size) return true;
            if (--occurrence != 0) return true;
            Rejected.Add((at, size)); return false;
        }
    }
}
