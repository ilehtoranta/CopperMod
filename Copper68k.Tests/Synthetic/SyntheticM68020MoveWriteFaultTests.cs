using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Native 030 final-write observation: short format A, completed source/flags,
// following PC, one pending write. Other models remain software discovery.
public sealed class SyntheticM68020MoveWriteFaultTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200").ToArray();
    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)] [InlineData(true)]
    public void DirectFinalWriteControls(bool batch) => Audit(batch, false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY", "require actual final-write fault recovery"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarFinalWriteFaults() => Audit(false, true);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY", "require actual final-write fault recovery"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchFinalWriteFaults() => Audit(true, true);

    private void Audit(bool batch, bool fault)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        {
            var bus = new FaultBus(); var m = new SyntheticMachine(model, bus);
            var report = new CoverageBatch(model.Id, $"move-final-write-{(fault ? "fault" : "controls")}-{(batch ? "batch" : "scalar")}");
            foreach (var width in new[] { 1, 2, 4 })
            foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
            foreach (var source in new[] { 0, 7 })
            foreach (var post in new[] { false, true })
            foreach (var alias in new[] { false, true })
            foreach (var vi in Enumerable.Range(0, 4))
            foreach (var ccr in new[] { 0, 31 })
            {
                var id = $"{model.Id}/MOVE/write/width={width}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}";
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
                    bus.Accesses.Clear(); bus.Observe(destination, width); if (fault) bus.Arm(destination, width); Step(m, batch);
                    if (fault)
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
                        bus.ExcludeFrameReads(frame); e.ExceptionVector = 2; Step(m, batch);
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
                    if (bus.SuccessfulSourceReads != 1 || bus.SuccessfulDestinationWrites != 1 || bus.Rejected != (fault ? 1 : 0))
                        throw new InvalidOperationException("Final-write recovery repeated a completed source/update or write");
                    e.D[7] = 42; e.Pc = 0x1004; e.Sr = (ushort)(e.Sr & 0xfff0); Step(m, batch); Check(m, e);
                    report.Record(id, "passing", null);
                }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
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
