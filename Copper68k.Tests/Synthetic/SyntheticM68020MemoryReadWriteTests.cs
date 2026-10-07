using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Whole logical requests; no physical partial-cycle or trace qualification.
public sealed class SyntheticM68020MemoryReadWriteTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200").ToArray();
    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)] [InlineData(true)]
    public void DirectWidthControls(bool batch) => Audit(batch, false);
    [EnvironmentFact("COPPER68K_RUN_020_MEMORY_READ_WRITE", "require private all-width read/write continuations"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarReadWriteContinuations() => Audit(false, true);
    [EnvironmentFact("COPPER68K_RUN_020_MEMORY_READ_WRITE", "require private all-width read/write continuations"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchReadWriteContinuations() => Audit(true, true);

    private void Audit(bool batch, bool fault)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        {
            var bus = new FaultBus(); var m = new SyntheticMachine(model, bus);
            foreach (var width in new[] { 1, 2, 4 })
            foreach (var lane in fault ? new[] { "read", "read-write", "read-write-refault" } : new[] { "control" })
            foreach (var offset in fault ? Enumerable.Range(0, width) : new[] { 0 })
            {
                var report = new CoverageBatch(model.Id, $"memory-read-write-{width}-{lane}-{offset}-{(batch ? "batch" : "scalar")}");
                foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
                foreach (var source in new[] { 0, 7 })
                foreach (var post in new[] { false, true })
                foreach (var alias in new[] { false, true })
                foreach (var vi in Enumerable.Range(0, 4))
                foreach (var ccr in Enumerable.Range(0, 32))
                {
                    var id = $"{model.Id}/MOVE/read-write/width={width}/lane={lane}/offset={offset}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}";
                    try
                    {
                        var mask = width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue;
                        var value = vi switch { 0 => 0u, 1 => 0x80818283u & mask, 2 => mask >> 1, _ => mask };
                        var stride = width == 1 && source == 7 ? 2u : (uint)width;
                        var destination = alias ? 0x4200u + (post ? stride : 0) : 0x4400u;
                        var opcode = (ushort)((width == 1 ? 0x1090 : width == 2 ? 0x3090 : 0x2090) | source | (post ? 8 : 0) | ((alias ? source : 1) << 9));
                        bus.ResetObservation(); m.Reset(ccr); _ = SyntheticExecution.Prepare(m, [opcode, 0x7e2a, 0x4e71]);
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
                        var readFrame = (bank is "ISP" or "MSP" ? e.A[7] : bank == "user-M" ? 0x7400u : 0x4700u) - 92;
                        bus.Accesses.Clear(); bus.Observe(destination, width);
                        if (fault) bus.ArmRead(0x4200u + (uint)offset);
                        if (lane != "read" && fault) bus.ArmWrite(destination);
                        Step(m, batch);
                        if (fault)
                        {
                            if (m.Core.State.A[7] != readFrame || m.Core.State.ProgramCounter != 0x9020 || m.Core.State.ExceptionSequence != serial + 1 ||
                                m.Core.State.LastExceptionVector != 2 || m.Core.State.Halted || m.Core.State.Stopped ||
                                m.PeekPhysical(readFrame, 2) != sr || m.PeekPhysical(readFrame + 2, 4) != 0x1000 || m.PeekPhysical(readFrame + 6, 2) != 0xb008 ||
                                (m.PeekPhysical(readFrame + 10, 2) & 0x1f7) != (0x140u | (width == 1 ? 0x10u : width == 2 ? 0x20u : 0) | ((sr & 0x2000) != 0 ? 5u : 1u)) ||
                                m.PeekPhysical(readFrame + 16, 4) != 0x4200 || bus.Reads != 0 || bus.Writes != 0 || bus.ReadDenied != 1 || bus.WriteDenied != 0)
                                throw new InvalidOperationException("All-width read fault changed prefix/frame/PC/SR/SSW");
                            for (var n = 0; n < 8; n++) if (m.Core.State.D[n] != e.D[n] || n != 7 && m.Core.State.A[n] != e.A[n])
                                throw new InvalidOperationException("Read denial performed an early register effect");
                            CaptureFrame(m, e, bus, readFrame, 92); e.ExceptionVector = 2;
                            Step(m, batch);
                        }
                        e.Sr = (ushort)((sr & 0xfff0) | (value == 0 ? 4 : (value & (1u << (width * 8 - 1))) != 0 ? 8 : 0));
                        if (post) { e.A[source] += stride; if (source == 7 && bank == "MSP") e.MasterStackPointer = e.A[7]; }
                        if (fault && lane != "read")
                        {
                            var frame = (bank is "ISP" or "MSP" ? e.A[7] : bank == "user-M" ? 0x7400u : 0x4700u) - 32;
                            var count = 2;
                            CheckWriteBoundary(m, e, bus, frame, destination, value, mask, width, serial + count);
                            CaptureFrame(m, e, bus, frame, 32);
                            if (lane == "read-write-refault")
                            {
                                bus.ArmWrite(destination); Step(m, batch); count++;
                                CheckWriteBoundary(m, e, bus, frame, destination, value, mask, width, serial + count);
                                CaptureFrame(m, e, bus, frame, 32);
                            }
                            Step(m, batch);
                        }
                        e.Write(destination, value, width, model); e.Pc = 0x1002; Check(m, e);
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
                            throw new InvalidOperationException("All-width continuation changed timing policy");
                        var exceptions = fault ? lane == "read" ? 1 : lane == "read-write" ? 2 : 3 : 0;
                        if (bus.Reads != 1 || bus.Writes != 1 || bus.ReadDenied != (fault ? 1 : 0) || bus.WriteDenied != (fault ? exceptions - 1 : 0) ||
                            m.Core.State.ExceptionSequence != serial + exceptions ||
                            bus.Accesses.Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == 0x1000) > 1)
                            throw new InvalidOperationException("All-width continuation repeated a source/update/write or opcode fetch");
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
    private static void CaptureFrame(SyntheticMachine m, ArchitecturalExpectation e, FaultBus bus, uint frame, int length)
    { for (uint n = 0; n < length; n++) e.Memory[m.Model.Physical(frame + n)] = bus.Peek(m.Model.Physical(frame + n)); bus.Frames.Add((frame, length)); }
    private static void CheckWriteBoundary(SyntheticMachine m, ArchitecturalExpectation e, FaultBus bus, uint frame, uint address, uint value, uint mask, int width, long serial)
    {
        var sr = e.Sr;
        if (m.Core.State.ProgramCounter != 0x9020 || m.Core.State.A[7] != frame || m.Core.State.ExceptionSequence != serial || m.Core.State.LastExceptionVector != 2 || m.Core.State.Halted || m.Core.State.Stopped ||
            m.PeekPhysical(frame, 2) != sr || m.PeekPhysical(frame + 2, 4) != 0x1002 || m.PeekPhysical(frame + 6, 2) != 0xa008 ||
            (m.PeekPhysical(frame + 10, 2) & 0x1f7) != (0x100u | (width == 1 ? 0x10u : width == 2 ? 0x20u : 0) | ((sr & 0x2000) != 0 ? 5u : 1u)) ||
            m.PeekPhysical(frame + 16, 4) != address || (m.PeekPhysical(frame + 24, 4) & mask) != value || bus.Reads != 1 || bus.Writes != 0)
            throw new InvalidOperationException("Read-to-write fault lost completed source/flags or pending write/frame");
        for (var n = 0; n < 8; n++) if (m.Core.State.D[n] != e.D[n] || n != 7 && m.Core.State.A[n] != e.A[n])
            throw new InvalidOperationException("Read-to-write fault repeated a register update");
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { var error = e.Verify(m); if (error != null) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) { m.Core.ExecuteInstruction(); return; }
        var b = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, b) != 1 || b.Before != 1 || b.After != 1) throw new InvalidOperationException("Batch boundaries differ");
    }
    private sealed class Boundary : IM68kInstructionBoundary { public int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class FaultBus : SparseRecordingBus, IM68kPhysicalAddressMap, IM68kBus
    {
        private uint? deniedRead, deniedWrite; private uint destination; private int width;
        internal int Reads, Writes, ReadDenied, WriteDenied;
        internal readonly List<(uint Start, int Length)> Frames = [];
        internal void ResetObservation() { deniedRead = deniedWrite = null; Reads = Writes = ReadDenied = WriteDenied = 0; Frames.Clear(); }
        internal void Observe(uint target, int size) { destination = target; width = size; }
        internal void ArmRead(uint at) => deniedRead = at;
        internal void ArmWrite(uint at) => deniedWrite = at;
        public bool IsCpuPhysicalAddressMapped(uint a, int size, M68kBusAccessKind kind)
        {
            if (kind == M68kBusAccessKind.CpuDataRead && size == width && deniedRead is { } r && unchecked(r - a) < size) { deniedRead = null; ReadDenied++; return false; }
            if (kind == M68kBusAccessKind.CpuDataWrite && size == width && deniedWrite == a) { deniedWrite = null; WriteDenied++; return false; }
            return true;
        }
        private void Read(uint a, int size, M68kBusAccessKind kind)
        {
            if (kind != M68kBusAccessKind.CpuDataRead) return;
            if (deniedRead is { } r && size == width && unchecked(r - a) < size) throw new InvalidOperationException("Source read bypassed physical map");
            if (size == width && a is >= 0x41f8 and < 0x4210 && !Frames.Any(f => a >= f.Start && a + (uint)size <= f.Start + f.Length)) Reads++;
        }
        private void Write(uint a, int size, M68kBusAccessKind kind)
        {
            if (kind != M68kBusAccessKind.CpuDataWrite) return;
            if (deniedWrite == a && size == width) throw new InvalidOperationException("Destination write bypassed physical map");
            if (a == destination && size == width) Writes++;
        }
        byte IM68kBus.ReadByte(uint a, ref long c, M68kBusAccessKind k) { Read(a, 1, k); return base.ReadByte(a, ref c, k); }
        ushort IM68kBus.ReadWord(uint a, ref long c, M68kBusAccessKind k) { Read(a, 2, k); return base.ReadWord(a, ref c, k); }
        uint IM68kBus.ReadLong(uint a, ref long c, M68kBusAccessKind k) { Read(a, 4, k); return base.ReadLong(a, ref c, k); }
        void IM68kBus.WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) { Write(a, 1, k); base.WriteByte(a, v, ref c, k); }
        void IM68kBus.WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) { Write(a, 2, k); base.WriteWord(a, v, ref c, k); }
        void IM68kBus.WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) { Write(a, 4, k); base.WriteLong(a, v, ref c, k); }
    }
}
