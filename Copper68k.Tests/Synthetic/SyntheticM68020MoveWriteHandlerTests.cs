using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Native 030 final-write observation: short format A, completed source/flags,
// following PC, one pending write. Other models remain software discovery.
public sealed class SyntheticM68020MoveWriteHandlerTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200").ToArray();
    // Private frame software protocol. This does not qualify hardware trace,
    // partial bus cycles or arbitrary handler stack/model changes.
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_HANDLERS", "require private pending-write handler protocols"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarPendingWriteHandlers() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_HANDLERS", "require private pending-write handler protocols"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchPendingWriteHandlers() => Audit(true);

    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        {
            var bus = new FaultBus(); var m = new SyntheticMachine(model, bus);
            foreach (var width in new[] { 1, 2, 4 })
            foreach (var lane in new[] { "original", "output", "software", "refault", "edited-refault" })
            {
            // Keep every possible failure below the shared report's witness cap.
            var report = new CoverageBatch(model.Id, $"move-write-handlers-{width}-{lane}-{(batch ? "batch" : "scalar")}");
            foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
            foreach (var source in new[] { 0, 7 })
            foreach (var post in new[] { false, true })
            foreach (var alias in new[] { false, true })
            foreach (var vi in Enumerable.Range(0, 4))
            foreach (var ccr in Enumerable.Range(0, 32))
            {
                var id = $"{model.Id}/MOVE/write-handler/width={width}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}/lane={lane}";
                try
                {
                    var mask = width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue;
                    var value = vi switch { 0 => 0u, 1 => 0x80818283u & mask, 2 => mask >> 1, _ => mask };
                    var stride = width == 1 && source == 7 ? 2u : (uint)width;
                    var destination = alias ? 0x4200u + (post ? stride : 0) : 0x4400u;
                    var opcode = (ushort)((width == 1 ? 0x1090 : width == 2 ? 0x3090 : 0x2090) | source | (post ? 8 : 0) | ((alias ? source : 1) << 9));
                    bus.Disarm(); m.Reset(ccr); _ = SyntheticExecution.Prepare(m, [opcode, 0x7e2a, 0x4e71]);
                    for (uint address = 0x41f8; address < 0x4210; address++) m.InitializePhysical(address, 0xa5, 1);
                    for (uint address = 0x43f8; address < 0x4410; address++) m.InitializePhysical(address, 0xa5, 1);
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
                    bus.Accesses.Clear(); bus.Observe(destination, width); bus.Arm(destination, width); Step(m, batch);
                    {
                        if (bus.Rejected != 1 || m.Core.State.LastExceptionVector != 2 || m.Core.State.ExceptionSequence != serial + 1 ||
                            m.Core.State.ProgramCounter != 0x9020 || m.Core.State.Halted || m.Core.State.Stopped)
                            throw new InvalidOperationException("Final-write request did not enter vector 2 exactly once");
                        var frame = m.Core.State.A[7];
                        var expectedFrame = (bank is "ISP" or "MSP" ? e.A[7] : bank == "user-M" ? 0x7400u : 0x4700u) - 32;
                        if (frame != expectedFrame || bus.SuccessfulSourceReads != 1 || bus.SuccessfulDestinationWrites != 0 ||
                            m.Core.State.D[7] != e.D[7] || m.PeekPhysical(frame + 6, 2) != 0xa008 || m.PeekPhysical(frame, 2) != e.Sr || m.PeekPhysical(frame + 2, 4) != 0x1002 ||
                            (m.PeekPhysical(frame + 10, 2) & 0x1f7) != (0x100u | (width == 1 ? 0x10u : width == 2 ? 0x20u : 0) | ((sr & 0x2000) != 0 ? 5u : 1u)) ||
                            m.PeekPhysical(frame + 16, 4) != destination || (m.PeekPhysical(frame + 24, 4) & mask) != value)
                            throw new InvalidOperationException("Final-write format-A PC/SR/SSW/address/output differs");
                        if (bus.Accesses.Any(a => a.Write && (a.Address < frame || a.Address >= frame + 32)))
                            throw new InvalidOperationException("Rejected final write reached ordinary memory");
                        for (uint n = 0; n < 32; n++) e.Memory[model.Physical(frame + n)] = bus.Peek(model.Physical(frame + n));
                        bus.ExcludeFrameReads(frame); e.ExceptionVector = 2;
                        var pending = value;
                        if (lane == "output") { pending = value ^ mask; m.InitializePhysical(frame + 24, pending, 4); }
                        if (lane == "software")
                        {
                            // Software completed the write; fixture initialization
                            // is deliberately separate from CPU bus transactions.
                            m.InitializePhysical(destination, value, width);
                            m.InitializePhysical(frame + 10, m.PeekPhysical(frame + 10, 2) & ~0x100u, 2);
                        }
                        if (lane == "edited-refault")
                        {
                            e.Sr = (ushort)((e.Sr & 0xffe0) | ((ccr + 7) & 31));
                            m.InitializePhysical(frame, e.Sr, 2);
                            // Original SSW FC, independently of returned SR.
                            m.InitializePhysical(frame + 10, m.PeekPhysical(frame + 10, 2) ^ 4, 2);
                        }
                        var pendingSsw = m.PeekPhysical(frame + 10, 2);
                        if (lane is "refault" or "edited-refault")
                        {
                            bus.Arm(destination, width); Step(m, batch);
                            if (m.Core.State.ExceptionSequence != serial + 2 || m.Core.State.LastExceptionVector != 2 ||
                                m.Core.State.ProgramCounter != 0x9020 || m.Core.State.A[7] != frame || m.Core.State.Halted || m.Core.State.Stopped ||
                                m.PeekPhysical(frame, 2) != e.Sr || m.PeekPhysical(frame + 2, 4) != 0x1002 ||
                                m.PeekPhysical(frame + 10, 2) != pendingSsw || m.PeekPhysical(frame + 16, 4) != destination ||
                                (m.PeekPhysical(frame + 24, 4) & mask) != pending || bus.SuccessfulSourceReads != 1 || bus.SuccessfulDestinationWrites != 0)
                                throw new InvalidOperationException("Pending-write refault lost returned SR/FC/output or repeated source effects");
                            // Check every register at the second fault boundary,
                            // including source updates and inactive stack banks.
                            for (var n = 0; n < 8; n++)
                                if (m.Core.State.D[n] != e.D[n] || (n != 7 && m.Core.State.A[n] != e.A[n]))
                                    throw new InvalidOperationException("Pending-write refault changed completed registers");
                            if (m.Core.State.UserStackPointer != (bank is "user" or "user-M" ? e.A[7] : 0x7800u) ||
                                m.Core.State.InterruptStackPointer != (bank is "user" or "ISP" ? frame : 0x4700u) ||
                                m.Core.State.MasterStackPointer != (bank is "user-M" or "MSP" ? frame : 0x7400u) ||
                                m.Core.State.StatusRegister != (ushort)((e.Sr | 0x2000) & ~0xc000))
                                throw new InvalidOperationException("Pending-write refault changed stack banks or entry SR");
                        }
                        // Only opaque/private frame bytes may differ. Handler edits
                        // are included explicitly; unrelated memory stays guarded.
                        for (uint n = 0; n < 32; n++) e.Memory[model.Physical(frame + n)] = bus.Peek(model.Physical(frame + n));
                        Step(m, batch);
                        if (m.Core.State.ExceptionSequence != serial + (lane is "refault" or "edited-refault" ? 2 : 1))
                            throw new InvalidOperationException("Pending-write recovery created an extra exception");
                        value = pending;
                    }
                    e.Write(destination, value, width, model); e.Pc = 0x1002; Check(m, e);
                    // Existing approximate execution policy, not physical cycles.
                    var key = (post, width) switch
                    {
                        (false, 1) => M68kInstructionTimingKey.MoveByteAddressIndirectToAddressIndirect,
                        (false, 2) => M68kInstructionTimingKey.MoveWordAddressIndirectToAddressIndirect,
                        (false, _) => M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect,
                        (true, 1) => M68kInstructionTimingKey.MoveBytePostIncrementToAddressIndirect,
                        (true, 2) => M68kInstructionTimingKey.MoveWordPostIncrementToAddressIndirect,
                        _ => M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect
                    };
                    var plan = ((M68kAdvancedTimingInterpreter)m.Core).Timing.LastInstructionTiming.Plan;
                    if (plan.Key != key || plan.NativeCycles != 8 || plan.Barriers != M68kTimingBarrier.None ||
                        plan.UsesHeadTail != (model.Id == "68030") || plan.HeadCycles != (model.Id == "68030" ? 1 : 0) || plan.TailCycles != (model.Id == "68030" ? 1 : 0))
                        throw new InvalidOperationException($"Final-write timing policy differs: {plan.Key}/{plan.NativeCycles}/{plan.HeadCycles}/{plan.TailCycles}");
                    // Frame traffic can overlap A7 source guards; distinguish the
                    // intended operand widths/value from frame word transfers.
                    if (bus.SuccessfulSourceReads != 1 || bus.SuccessfulDestinationWrites != (lane == "software" ? 0 : 1) || bus.Rejected != (lane is "refault" or "edited-refault" ? 2 : 1))
                        throw new InvalidOperationException("Pending-write handler repeated a completed source/update or write");
                    e.D[7] = 42; e.Pc = 0x1004; e.Sr = (ushort)(e.Sr & 0xfff0); Step(m, batch); Check(m, e);
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
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e)
    { var error = e.Verify(m); if (error != null) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) { m.Core.ExecuteInstruction(); return; }
        var boundary = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, boundary) != 1 || boundary.Before != 1 || boundary.After != 1)
            throw new InvalidOperationException("Batch boundary differs");
    }
    private sealed class Boundary : IM68kInstructionBoundary
    { public int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class FaultBus : SparseRecordingBus, IM68kPhysicalAddressMap, IM68kBus
    {
        private uint? denied; private int width;
        internal int Rejected, SuccessfulSourceReads, SuccessfulDestinationWrites;
        private uint destination; private int operandWidth; private uint? frameReadStart;
        internal void Disarm() { denied = frameReadStart = null; Rejected = SuccessfulSourceReads = SuccessfulDestinationWrites = 0; }
        internal void Arm(uint address, int size) { denied = address; width = size; }
        public bool IsCpuPhysicalAddressMapped(uint address, int size, M68kBusAccessKind kind)
        {
            if (denied is not { } at || kind != M68kBusAccessKind.CpuDataWrite || size != width || address != at) return true;
            denied = null; Rejected++; return false;
        }
        // Record actual transport, independently of the mapping query. A denied
        // destination must never be dispatched to the ordinary bus.
        private void BeforeWrite(uint address, int size, M68kBusAccessKind kind)
        {
            if (kind != M68kBusAccessKind.CpuDataWrite) return;
            if (denied == address && size == width) throw new InvalidOperationException("Denied destination write bypassed physical map");
            if (address == destination && size == operandWidth) SuccessfulDestinationWrites++;
        }
        internal void Observe(uint address, int size) { destination = address; operandWidth = size; }
        internal void ExcludeFrameReads(uint frame) => frameReadStart = frame;
        private void BeforeRead(uint address, int size, M68kBusAccessKind kind)
        {
            if (address is >= 0x41f8 and < 0x4210 && size == operandWidth && kind == M68kBusAccessKind.CpuDataRead &&
                !(frameReadStart is { } frame && address >= frame && address + (uint)size <= frame + 32)) SuccessfulSourceReads++;
        }
        byte IM68kBus.ReadByte(uint a, ref long c, M68kBusAccessKind k) { BeforeRead(a, 1, k); return base.ReadByte(a, ref c, k); }
        ushort IM68kBus.ReadWord(uint a, ref long c, M68kBusAccessKind k) { BeforeRead(a, 2, k); return base.ReadWord(a, ref c, k); }
        uint IM68kBus.ReadLong(uint a, ref long c, M68kBusAccessKind k) { BeforeRead(a, 4, k); return base.ReadLong(a, ref c, k); }
        void IM68kBus.WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) { BeforeWrite(a, 1, k); base.WriteByte(a, v, ref c, k); }
        void IM68kBus.WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) { BeforeWrite(a, 2, k); base.WriteWord(a, v, ref c, k); }
        void IM68kBus.WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) { BeforeWrite(a, 4, k); base.WriteLong(a, v, ref c, k); }
    }
}
