using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Private serialized pipe protocol; not physical prefetch or silicon frame state.
public sealed class SyntheticM68020MoveWritePipeTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200").ToArray();
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_PIPE", "require private pending-write saved pipes"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarPendingWritePipes() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_PIPE", "require private pending-write saved pipes"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchPendingWritePipes() => Audit(true);

    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        {
            var bus = new FaultBus(); var m = new SyntheticMachine(model, bus);
            foreach (var width in new[] { 1, 2, 4 })
            foreach (var lane in new[] { "write", "software", "refault" })
            foreach (var count in Enumerable.Range(0, 4))
            foreach (var edit in Enumerable.Range(-1, count + 1))
            {
                var report = new CoverageBatch(model.Id, $"move-write-pipe-{width}-{lane}-count{count}-edit{edit}-{(batch ? "batch" : "scalar")}");
                foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
                foreach (var source in new[] { 0, 7 })
                foreach (var post in new[] { false, true })
                foreach (var alias in new[] { false, true })
                foreach (var vi in Enumerable.Range(0, 4))
                foreach (var ccr in new[] { 0, 31 })
                {
                    var id = $"{model.Id}/MOVE/write-pipe/width={width}/lane={lane}/count={count}/edit={edit}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}";
                    try
                    {
                        var mask = width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue;
                        var value = vi switch { 0 => 0u, 1 => 0x80818283u & mask, 2 => mask >> 1, _ => mask };
                        var stride = width == 1 && source == 7 ? 2u : (uint)width;
                        var destination = alias ? 0x4200u + (post ? stride : 0) : 0x4400u;
                        var opcode = (ushort)((width == 1 ? 0x1090 : width == 2 ? 0x3090 : 0x2090) | source | (post ? 8 : 0) | ((alias ? source : 1) << 9));
                        bus.ResetObservation(); m.Reset(ccr); _ = SyntheticExecution.Prepare(m, [opcode, 0x2c3c, 0x9abc, 0xdef0, 0x4e71]);
                        for (uint a = 0x41f8; a < 0x4210; a++) m.InitializePhysical(a, 0xa5, 1);
                        for (uint a = 0x43f8; a < 0x4410; a++) m.InitializePhysical(a, 0xa5, 1);
                        m.InitializePhysical(0x4200, value, width); m.InitializePhysical(0x9020, 0x4e73, 2); m.InitializePhysical(0x9022, 0x4e71, 2);
                        m.Core.State.A[0] = source == 0 ? 0x4200u : 0x4000u; m.Core.State.A[1] = 0x4400;
                        m.Core.State.SetUserStackPointer(source == 7 && bank is "user" or "user-M" ? 0x4200u : 0x7800u);
                        m.Core.State.SetInterruptStackPointer(source == 7 && bank == "ISP" ? 0x4200u : 0x4700u);
                        m.Core.State.SetMasterStackPointer(source == 7 && bank == "MSP" ? 0x4200u : 0x7400u);
                        var sr = (ushort)(0x700 | ccr | (bank is "ISP" or "MSP" ? 0x2000 : 0) | (bank is "user-M" or "MSP" ? 0x1000 : 0));
                        m.Core.State.StatusRegister = sr; m.Core.State.SetActiveStackPointer(source == 7 ? 0x4200u : bank == "MSP" ? 0x7400u : bank == "ISP" ? 0x4700u : 0x7800u);
                        var e = ArchitecturalExpectation.Capture(m); var serial = m.Core.State.ExceptionSequence;
                        e.Sr = (ushort)((sr & 0xfff0) | (value == 0 ? 4 : (value & (1u << (width * 8 - 1))) != 0 ? 8 : 0));
                        if (post) { e.A[source] += stride; if (source == 7 && bank == "MSP") e.MasterStackPointer = e.A[7]; }
                        var frame = (bank is "ISP" or "MSP" ? e.A[7] : bank == "user-M" ? 0x7400u : 0x4700u) - 32;
                        bus.Accesses.Clear(); bus.Observe(destination, width); bus.Arm(destination); Step(m, batch);
                        CheckFault(m, e, bus, frame, destination, value, width, serial + 1, 0, opcode);
                        bus.Frames.Add(frame); e.ExceptionVector = 2;
                        ushort[] words = [0x2e3c, 0x1234, 0x5678];
                        if (edit >= 0) words[edit] = edit == 0 ? (ushort)0x2a3c : edit == 1 ? (ushort)0x8765 : (ushort)0x4321;
                        m.InitializePhysical(frame + 22, (uint)(1 | count << 8), 2);
                        for (var n = 0; n < 3; n++) m.InitializePhysical(frame + (n == 0 ? 12u : n == 1 ? 14u : 28u), words[n], 2);
                        if (lane == "software")
                        { m.InitializePhysical(frame + 10, m.PeekPhysical(frame + 10, 2) & ~0x100u, 2); m.InitializePhysical(destination, value, width); }
                        CaptureFrame(m, e, bus, frame);
                        if (lane == "refault")
                        {
                            bus.Arm(destination); var start = bus.Accesses.Count; Step(m, batch); CheckFrameReads(bus, frame, count, start);
                            CheckFault(m, e, bus, frame, destination, value, width, serial + 2, count, opcode);
                            for (var n = 0; n < count; n++) if (m.PeekPhysical(frame + (n == 0 ? 12u : n == 1 ? 14u : 28u), 2) != words[n])
                                throw new InvalidOperationException("Refault changed retained pending-write words");
                            CaptureFrame(m, e, bus, frame);
                        }
                        var before = bus.Accesses.Count; Step(m, batch); CheckFrameReads(bus, frame, count, before);
                        e.Write(destination, value, width, model); e.Pc = 0x1002; Check(m, e);
                        var plan = ((M68kAdvancedTimingInterpreter)m.Core).Timing.LastInstructionTiming.Plan;
                        var key = (post, width) switch
                        {
                            (false, 1) => M68kInstructionTimingKey.MoveByteAddressIndirectToAddressIndirect,
                            (false, 2) => M68kInstructionTimingKey.MoveWordAddressIndirectToAddressIndirect,
                            (false, _) => M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect,
                            (true, 1) => M68kInstructionTimingKey.MoveBytePostIncrementToAddressIndirect,
                            (true, 2) => M68kInstructionTimingKey.MoveWordPostIncrementToAddressIndirect,
                            _ => M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect
                        };
                        if (plan.Key != key || plan.NativeCycles != 8 || plan.Barriers != M68kTimingBarrier.None || plan.UsesHeadTail != (model.Id == "68030") ||
                            plan.HeadCycles != (model.Id == "68030" ? 1 : 0) || plan.TailCycles != (model.Id == "68030" ? 1 : 0))
                            throw new InvalidOperationException("Pending-write pipe changed timing policy");
                        var writes = lane == "software" ? 0 : 1;
                        if (bus.Reads != 1 || bus.Writes != writes || bus.Rejected != (lane == "refault" ? 2 : 1) ||
                            m.Core.State.ExceptionSequence != serial + (lane == "refault" ? 2 : 1))
                            throw new InvalidOperationException("Pending-write pipe repeated source or write effects");
                        var first = count > 0 ? words[0] : (ushort)0x2c3c;
                        var immediate = ((uint)(count > 1 ? words[1] : (ushort)0x9abc) << 16) | (count > 2 ? words[2] : (ushort)0xdef0);
                        e.D[(first >> 9) & 7] = immediate; e.Pc = 0x1008;
                        e.Sr = (ushort)((e.Sr & 0xfff0) | (immediate == 0 ? 4 : (immediate & 0x80000000) != 0 ? 8 : 0));
                        before = bus.Accesses.Count; Step(m, batch); Check(m, e);
                        for (var n = 0; n < 3; n++)
                            if (bus.Accesses.Skip(before).Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == 0x1002u + (uint)n * 2) != (n < count ? 0 : 1))
                                throw new InvalidOperationException("Pending-write pipe refetched a retained word or missed backing data");
                        report.Record(id, "passing", null);
                    }
                    catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                    catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
                }
                try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }
    private static void CheckFault(SyntheticMachine m, ArchitecturalExpectation e, FaultBus bus, uint frame, uint address, uint value, int width, long serial, int count, ushort opcode)
    {
        if (m.Core.State.ProgramCounter != 0x9020 || m.Core.State.A[7] != frame || m.Core.State.ExceptionSequence != serial || m.Core.State.LastExceptionVector != 2 || m.Core.State.Halted || m.Core.State.Stopped ||
            m.PeekPhysical(frame, 2) != e.Sr || m.PeekPhysical(frame + 2, 4) != 0x1002 || m.PeekPhysical(frame + 6, 2) != 0xa008 || m.PeekPhysical(frame + 8, 2) != 0xc023 ||
            m.PeekPhysical(frame + 20, 2) != opcode || m.PeekPhysical(frame + 22, 2) != (uint)(1 | count << 8) || m.PeekPhysical(frame + 30, 2) != 0 ||
            (m.PeekPhysical(frame + 10, 2) & 0x1f7) != (0x100u | (width == 1 ? 0x10u : width == 2 ? 0x20u : 0) | ((e.Sr & 0x2000) != 0 ? 5u : 1u)) ||
            m.PeekPhysical(frame + 16, 4) != address || m.PeekPhysical(frame + 24, 4) != value || bus.Reads != 1 || bus.Writes != 0)
            throw new InvalidOperationException("Pending-write pipe fault changed completed effects or frame state");
        for (var n = 0; n < 8; n++) if (m.Core.State.D[n] != e.D[n] || n != 7 && m.Core.State.A[n] != e.A[n])
            throw new InvalidOperationException("Pending-write pipe fault repeated register effects");
    }
    private static void CaptureFrame(SyntheticMachine m, ArchitecturalExpectation e, FaultBus bus, uint frame)
    { for (uint n = 0; n < 32; n++) e.Memory[m.Model.Physical(frame + n)] = bus.Peek(m.Model.Physical(frame + n)); }
    private static void CheckFrameReads(FaultBus bus, uint frame, int count, int start)
    {
        // Fixed private-frame request protocol. Counting these requests also
        // detects extra source reads when A7 postincrement overlaps the frame.
        (uint Offset, int Width)[] header = [(0, 2), (2, 4), (6, 2), (30, 2), (8, 2), (10, 2), (16, 4), (20, 2), (22, 2), (24, 4), (30, 2)];
        var expected = header.Select(x => (frame + x.Offset, x.Width))
            .Concat(Enumerable.Range(0, count).Select(n => (frame + (n == 0 ? 12u : n == 1 ? 14u : 28u), 2)))
            .Concat(Enumerable.Range(0, 10).Select(n => (frame + 12u + (uint)n * 2, 2)));
        var actual = bus.Accesses.Skip(start).Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= frame && a.Address < frame + 32).Select(a => (a.Address, a.Width));
        if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Pending-write pipe frame reads differ from fixed transport protocol");
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { var error = e.Verify(m); if (error != null) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    { if (!batch) { m.Core.ExecuteInstruction(); return; } var b = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, b) != 1 || b.Before != 1 || b.After != 1) throw new InvalidOperationException("Batch boundaries differ"); }
    private sealed class Boundary : IM68kInstructionBoundary { public int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class FaultBus : SparseRecordingBus, IM68kPhysicalAddressMap, IM68kBus
    {
        private uint? denied; private uint destination; private int width;
        internal int Reads, Writes, Rejected;
        internal readonly List<uint> Frames = [];
        internal void ResetObservation() { denied = null; Reads = Writes = Rejected = 0; Frames.Clear(); }
        internal void Observe(uint target, int size) { destination = target; width = size; }
        internal void Arm(uint at) => denied = at;
        public bool IsCpuPhysicalAddressMapped(uint a, int size, M68kBusAccessKind kind)
        { if (kind == M68kBusAccessKind.CpuDataWrite && size == width && denied == a) { denied = null; Rejected++; return false; } return true; }
        private void Read(uint a, int size, M68kBusAccessKind k)
        { if (k == M68kBusAccessKind.CpuDataRead && size == width && a is >= 0x41f8 and < 0x4210 && !Frames.Any(f => a >= f && a + (uint)size <= f + 32)) Reads++; }
        private void Write(uint a, int size, M68kBusAccessKind k)
        { if (k != M68kBusAccessKind.CpuDataWrite) return; if (denied == a && size == width) throw new InvalidOperationException("Pending write bypassed physical map"); if (a == destination && size == width) Writes++; }
        byte IM68kBus.ReadByte(uint a, ref long c, M68kBusAccessKind k) { Read(a, 1, k); return base.ReadByte(a, ref c, k); }
        ushort IM68kBus.ReadWord(uint a, ref long c, M68kBusAccessKind k) { Read(a, 2, k); return base.ReadWord(a, ref c, k); }
        uint IM68kBus.ReadLong(uint a, ref long c, M68kBusAccessKind k) { Read(a, 4, k); return base.ReadLong(a, ref c, k); }
        void IM68kBus.WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) { Write(a, 1, k); base.WriteByte(a, v, ref c, k); }
        void IM68kBus.WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) { Write(a, 2, k); base.WriteWord(a, v, ref c, k); }
        void IM68kBus.WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) { Write(a, 4, k); base.WriteLong(a, v, ref c, k); }
    }
}
