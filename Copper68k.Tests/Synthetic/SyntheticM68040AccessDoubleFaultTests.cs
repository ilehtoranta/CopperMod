using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 7.6.3, 8.2.1: faults while stacking or fetching the access-error
// vector halt until external reset. The partially constructed frame is opaque;
// its write ordering/SP are not hardware-qualified by this semantic fixture.
public sealed class SyntheticM68040AccessDoubleFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020;
    private static readonly ushort[] Traces = [0, 0x8000, 0x4000]; // C000 is undefined.

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData("accurate")]
    [InlineData("v1")]
    [InlineData("v2")]
    public void WarmDispatchHaltsBothRteFallbackAndOperandFaultEntry(string engine)
    {
        var report = new CoverageBatch("68040", "access-double-fault-dispatch-" + engine);
        foreach (var form in new[] { "RTE", "MOVE.B.read", "MOVE.W.read", "MOVE.L.read", "MOVE.B.write", "MOVE.W.write", "MOVE.L.write" })
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var ccr in new[] { 0, 31 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var stage in new[] { "stack", "vector" })
        for (var faultByte = 0; faultByte < (stage == "vector" ? 4 : form == "RTE" || engine == "accurate" ? 60 : 8); faultByte++)
        {
            var opcode = form switch { "RTE" => 0x4e73, "MOVE.B.read" => 0x1010, "MOVE.W.read" => 0x3010,
                "MOVE.L.read" => 0x2010, "MOVE.B.write" => 0x1080, "MOVE.W.write" => 0x3080, _ => 0x2080 };
            var write = form.EndsWith("write");
            var id = $"68040/access-double-fault/dispatch={engine}/form={form}/bank={bank}/align={alignment}/stage={stage}/byte={faultByte}/op={opcode:X4}/ccr={ccr:X2}";
            try
            {
                var bus = new FaultBus(); var executionBus = new ImmutableCodeBus(bus);
                bus.Initialize(0x2000, (uint)(form == "RTE" ? 0x3010 : opcode), 2); bus.Initialize(0x2002, 0x60fc, 2); // MOVE; BRA
                bus.Initialize(0x1000, 0x4e73, 2); bus.Initialize(0x1002, 0x4e71, 2);
                bus.Initialize(Handler, 0x4e73, 2); bus.Initialize(Handler + 2, 0x4e71, 2);
                bus.Initialize(0x5000, 0x747b, 2); bus.Initialize(0x5002, 0x4e71, 2);
                bus.Initialize(8, Handler, 4); bus.Initialize(0x4200, 0x1234, 2);
                IM68kCore core = engine == "accurate" ? M68kCoreFactory.Default.Create(M68kCpuModel.M68040, executionBus)
                    : M68kJitCore.CreateM68040ForTesting(executionBus, engine == "v2");
                using var disposable = core as IDisposable;
                core.Reset(0x2000, 0x4700); core.State.A[0] = 0x4200; core.State.CacheControlRegister = 0x8000;
                for (var n = 0; n < 300; n++) Execute(core);
                if (core is M68kJitCore warm && warm.Counters.TraceHits + warm.Counters.V2TraceHits == 0)
                    throw new InvalidOperationException("No actual compiled warm dispatch");
                // Keep compiled code, clear dispatch continuation from the warm loop.
                if (core is M68kJitCore prepared) prepared.ResetForBenchmark(form == "RTE" ? 0x1000u : 0x2000u, 0x4700);
                else core.Reset(form == "RTE" ? 0x1000u : 0x2000u, 0x4700);
                core.State.A[0] = 0x4200 + alignment; core.State.CacheControlRegister = 0x8000;
                var frame = (bank == "MSP" ? 0x7400u : 0x4700u) + alignment;
                core.State.SetInterruptStackPointer(0x4700 + alignment); core.State.SetMasterStackPointer(0x7400 + alignment);
                core.State.StatusRegister = (ushort)((bank == "MSP" ? 0x3000 : 0x2000) | ccr);
                core.State.ProgramCounter = form == "RTE" ? 0x1000u : 0x2000u;
                bus.Initialize(frame, 0x2000, 2); bus.Initialize(frame + 2, 0x6000, 4);
                bus.Initialize(frame + 6, 0x7008, 2); bus.Initialize(frame + 8, 0x12345678, 4);
                bus.Initialize(frame + 12, 0x1105, 2);
                var sequence = core.State.ExceptionSequence;
                var fallbacks = core is M68kJitCore j ? j.Counters.FallbackInstructions : 0;
                var sideExits = core is M68kJitCore j2 ? j2.Counters.SideExits : 0;
                bus.Arm(form == "RTE" ? frame : 0x4200 + alignment,
                    write ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead);
                // Accurate physical reads and MOVE writes create format 7.
                // Compiled operand delivery remains separately qualified here
                // only for fatal entry, with its existing short-frame policy.
                var stackStart = frame - (form == "RTE" || engine == "accurate" ? 60u : 8u);
                bus.Arm(stage == "vector" ? 8 + (uint)faultByte : stackStart + (uint)faultByte,
                    stage == "vector" ? M68kBusAccessKind.CpuDataRead : M68kBusAccessKind.CpuDataWrite);
                Execute(core);
                if (!core.State.Halted || core.State.Stopped || bus.Rejected.Count != 2 ||
                    bus.Accesses.Count != bus.Rejected[^1].AccessCount || core.State.ExceptionSequence != sequence + 1)
                    throw new InvalidOperationException($"Fatal entry: halted={core.State.Halted}, stopped={core.State.Stopped}, rejections={bus.Rejected.Count}, accesses={bus.Accesses.Count}, fatalAt={(bus.Rejected.Count > 0 ? bus.Rejected[^1].AccessCount : -1)}, sequence={core.State.ExceptionSequence - sequence}");
                var operandWidth = form == "RTE" || form.Contains(".W.") ? 2 : form.Contains(".B.") ? 1 : 4;
                if (bus.Rejected[0].Width != operandWidth || bus.Rejected[0].Kind !=
                    (write ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead))
                    throw new InvalidOperationException("Original operand access width/direction differs");
                if (core is M68kJitCore jit)
                {
                    if (form == "RTE" && jit.Counters.FallbackInstructions <= fallbacks)
                        throw new InvalidOperationException("RTE did not reach interpreter fallback");
                    if (form != "RTE" && (jit.Counters.FallbackInstructions != fallbacks || jit.Counters.SideExits <= sideExits))
                        throw new InvalidOperationException("Operand fault did not reach the compiled fault side exit");
                }
                var s = core.State; var d = (uint[])s.D.Clone(); var a = (uint[])s.A.Clone();
                var pc = s.ProgramCounter; var sr = s.StatusRegister; var accesses = bus.Accesses.Count;
                var memory = new Dictionary<uint, byte>(bus.Memory);
                core.RequestInterrupt(7, 31 * 4); core.BeginSubroutine(0x5000, 0x6000, 0x7000);
                core.SwitchTaskContext(new M68kCpuState());
                var batch = ((IM68kBatchCore)core).ExecuteInstructions(10, s.Cycles + 100, new Boundary());
                if (batch != 0 || !s.Halted || s.Stopped || !s.D.SequenceEqual(d) || !s.A.SequenceEqual(a) ||
                    s.ProgramCounter != pc || s.StatusRegister != sr || s.ExceptionSequence != sequence + 1 ||
                    bus.Accesses.Count != accesses || !memory.OrderBy(x => x.Key).SequenceEqual(bus.Memory.OrderBy(x => x.Key)))
                    throw new InvalidOperationException("Host entry resumed or changed fatal CPU state");
                bus.Disarm(); core.Reset(0x6000, 0x8000);
                core.BeginSubroutine(0x5000, 0x8000, 0x7000); Execute(core);
                if (s.Halted || s.Stopped || s.D[2] != 123 || s.ProgramCounter != 0x5002)
                    throw new InvalidOperationException("External reset did not restart execution");
                report.Record(id, "passing", null);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void EveryStackAndVectorByteCanHaltAccessFaultEntryUntilReset()
    {
        var bus = new FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", "rte-access-entry-double-fault");
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var trace in Traces)
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var firstOffset in new uint[] { 0, 8 })
        for (var secondByte = 0; secondByte < 64; secondByte++)
        {
            var id = $"68040/RTE/access-entry-double-fault/bank={bank}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/first={firstOffset}/second={secondByte}/op=4E73/ccr={ccr:X2}";
            try
            {
                var frame = Prepare(m, bus, bank, trace, ccr, alignment, vbr);
                var before = ArchitecturalExpectation.Capture(m);
                var sequence = m.Core.State.ExceptionSequence;
                bus.Arm(frame + firstOffset, M68kBusAccessKind.CpuDataRead);
                bus.Arm(secondByte < 60 ? frame - 60 + (uint)secondByte : vbr + 8 + (uint)secondByte - 60,
                    secondByte < 60 ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead);
                m.Core.ExecuteInstruction();
                var s = m.Core.State;
                string? mismatch = null;
                if (!s.Halted || s.Stopped) mismatch = "Second access fault must halt, not STOP";
                else if (bus.Rejected.Count != 2 || bus.Accesses.Count != bus.Rejected[^1].AccessCount)
                    mismatch = "Expected two rejections and no bus transfer after the fatal rejection";
                else if (s.StatusRegister != (before.Sr & ~0xc000) || s.LastExceptionStatusRegister != before.Sr ||
                    s.LastExceptionStackedProgramCounter != SyntheticMachine.Code || s.LastExceptionVector != 2 ||
                    s.ExceptionSequence != sequence + 1 || s.M68040Mmu.BypassTranslation)
                    mismatch = "Fault entry SR/provenance/sequence or translation bypass differs";
                else if (!s.D.SequenceEqual(before.D) || !s.A.Take(7).SequenceEqual(before.A.Take(7)) ||
                    s.UserStackPointer != before.InactiveStackPointer ||
                    (bank == "ISP" ? s.MasterStackPointer != 0x7400 + alignment : s.InterruptStackPointer != 0x4700 + alignment))
                    mismatch = "Fault entry changed unrelated registers or inactive stacks";
                else if (s.A[7] < frame - 60 || s.A[7] > frame)
                    mismatch = "Partial frame escaped its bounded stack region";
                else if (bus.Accesses.Where(a => a.Write).Any(a => a.Kind != M68kBusAccessKind.CpuDataWrite ||
                    a.Address < frame - 60 || (ulong)a.Address + (uint)a.Width > frame))
                    mismatch = "Fault entry wrote outside the new frame";
                else if (before.Memory.Keys.Concat(bus.Memory.Keys).Distinct().Any(at =>
                    (at < frame - 60 || at >= frame) && before.Memory.GetValueOrDefault(at) != bus.Peek(at)))
                    mismatch = "Fault entry changed original frame or surrounding memory";
                if (mismatch == null)
                {
                    // Opaque partial state is frozen after HALT, not used to infer
                    // hardware stacking order. Ordinary host entry cannot restart it.
                    var frozen = ArchitecturalExpectation.Capture(m); frozen.Halted = true;
                    frozen.ExceptionVector = 2;
                    frozen.ControlChecks["exception sequence"] = (x => x.ExceptionSequence, sequence + 1);
                    frozen.ControlChecks["last opcode"] = (x => x.LastOpcode, s.LastOpcode);
                    var accesses = bus.Accesses.Count;
                    m.Core.ExecuteInstruction();
                    m.Core.RequestInterrupt(7, 31 * 4);
                    m.Core.BeginSubroutine(0x5000, 0x6000, 0x7000);
                    m.Core.SwitchTaskContext(new M68kCpuState());
                    var batch = ((IM68kBatchCore)m.Core).ExecuteInstructions(10, s.Cycles + 100, new Boundary());
                    mismatch = frozen.Verify(m);
                    if (mismatch == null && (batch != 0 || bus.Accesses.Count != accesses || bus.Rejected.Count != 2))
                        mismatch = "Halted CPU performed a transfer or executed a batch";
                }
                if (mismatch == null)
                {
                    bus.Disarm(); m.InitializePhysical(0x5000, 0x747b, 2); // MOVEQ #123,D2
                    m.InitializePhysical(0x5002, 0x4e71, 2);
                    m.Core.Reset(0x6000, 0x8000);
                    m.Core.BeginSubroutine(0x5000, 0x8000, 0x7000);
                    var reset = ArchitecturalExpectation.Capture(m); reset.Pc = 0x5002; reset.D[2] = 123;
                    m.Core.ExecuteInstruction(); mismatch = reset.Verify(m);
                }
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void HandlerValidationRefaultStartsANewException()
    {
        var bus = new FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", "rte-access-handler-refault");
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var trace in Traces)
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var offset in new uint[] { 0, 2, 6, 12, 8 })
        {
            var id = $"68040/RTE/access-handler-refault/bank={bank}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/read={offset}/op=4E73/ccr={ccr:X2}";
            try
            {
                var frame = Prepare(m, bus, bank, trace, ccr, alignment, vbr);
                var before = ArchitecturalExpectation.Capture(m);
                var sequence = m.Core.State.ExceptionSequence;
                bus.Arm(frame, M68kBusAccessKind.CpuDataRead);
                m.Core.ExecuteInstruction();
                if (m.Core.State.Halted || m.Core.State.ProgramCounter != Handler || m.Core.State.A[7] != frame - 60)
                    throw new InvalidOperationException("First access-fault entry prerequisite failed");
                // Set CM to require the EA continuation read as well as header/SSW.
                // This is handler preparation, not an expectation derived from CPU output.
                m.InitializePhysical(frame - 60 + 12, 0x1145, 2);
                var e = ArchitecturalExpectation.Capture(m);
                bus.Arm(frame - 60 + offset, M68kBusAccessKind.CpuDataRead);
                var nested = frame - 120;
                e.A[7] = nested; if (bank == "MSP") e.MasterStackPointer = nested;
                e.Pc = Handler; e.ExceptionVector = 2;
                e.ControlChecks["new exception"] = (s => s.ExceptionSequence, sequence + 2);
                e.ControlChecks["handler PC saved"] = (s => s.LastExceptionStackedProgramCounter, Handler);
                e.ControlChecks["new fault rejected once"] = (_ => (uint)bus.Rejected.Count, 2);
                e.Write(nested, e.Sr, 2, m.Model); e.Write(nested + 2, Handler, 4, m.Model);
                e.Write(nested + 6, 0x7008, 2, m.Model);
                for (uint n = 8; n < 60; n++) e.MemoryMasks[nested + n] = 0;
                e.Write(nested + 12, offset is 2 or 8 ? 0x0105u : 0x0145u, 2, m.Model);
                e.MemoryMasks[nested + 12] = 0xff; e.MemoryMasks[nested + 13] = 0x7f;
                foreach (var at in new uint[] { 14, 16, 18 })
                { e.Write(nested + at, 0, 2, m.Model); e.MemoryMasks[nested + at + 1] = 0x80; }
                e.Write(nested + 20, frame - 60 + offset, 4, m.Model);
                for (uint n = 20; n < 24; n++) e.MemoryMasks[nested + n] = 0xff;
                m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m);
                if (mismatch == null && (m.Core.State.LastExceptionStatusRegister != (before.Sr & ~0xc000) ||
                    bus.Rejected[^1].Width != (offset is 2 or 8 ? 4 : 2)))
                    mismatch = "Nested validation fault saved the wrong SR or used the wrong width";
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    private static uint Prepare(SyntheticMachine m, FaultBus bus, string bank, ushort trace, int ccr, uint alignment, uint vbr)
    {
        bus.Disarm(); m.Reset(ccr);
        var frame = (bank == "MSP" ? 0x7400u : 0x4700u) + alignment;
        for (var n = -136; n < 80; n++) m.InitializePhysical(unchecked(frame + (uint)n), (uint)(n ^ 0x5a), 1);
        m.InitializePhysical(frame, 0x801fu ^ (uint)ccr, 2);
        m.InitializePhysical(frame + 2, 0x6000, 4); m.InitializePhysical(frame + 6, 0x7008, 2);
        m.InitializePhysical(frame + 8, 0x12345678, 4); m.InitializePhysical(frame + 12, 0x1105, 2);
        m.InitializePhysical(Handler, 0x4e73, 2); m.InitializePhysical(Handler + 2, 0x7001, 2);
        m.InitializePhysical(vbr + 8, Handler, 4);
        _ = SyntheticExecution.Prepare(m, [0x4e73]);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
        m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = (ushort)((bank == "MSP" ? 0x3000 : 0x2000) | trace | ccr);
        m.Core.State.VectorBaseRegister = vbr;
        return frame;
    }

    internal class FaultBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private readonly List<(uint At, M68kBusAccessKind Kind)> holes = [];
        public List<(uint Address, int Width, M68kBusAccessKind Kind, int AccessCount)> Rejected { get; } = [];
        public void Disarm() { holes.Clear(); Rejected.Clear(); }
        public void Arm(uint at, M68kBusAccessKind kind) => holes.Add((at, kind));
        public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind kind)
        {
            var i = holes.FindIndex(h => h.Kind == kind && unchecked(h.At - address) < byteCount);
            if (i < 0) return true;
            holes.RemoveAt(i); Rejected.Add((address, byteCount, kind, Accesses.Count)); return false;
        }
    }

    private static void Execute(IM68kCore core)
    {
        if (core is M68kJitCore jit)
        {
            if (jit.ExecuteInstructions(1, core.State.Cycles + 1000, new Boundary()) != 1)
                throw new InvalidOperationException("Expected one instruction boundary");
        }
        else core.ExecuteInstruction();
    }

    private sealed class ImmutableCodeBus(FaultBus memory) : IM68kBus, IM68kJitBus, IM68kPhysicalAddressMap
    {
        public bool IsCpuPhysicalAddressMapped(uint address, int count, M68kBusAccessKind kind) => memory.IsCpuPhysicalAddressMapped(address, count, kind);
        public event Action<uint, int>? JitCodeRangeWritten { add { } remove { } }
        public bool IsJitCodeAddress(uint address, int count, M68kBusAccessKind kind) =>
            address >= 0x1000 && address + count <= 0x1004 || address >= 0x2000 && address + count <= 0x2004 ||
            address >= 0x5000 && address + count <= 0x5004 || address >= Handler && address + count <= Handler + 4;
        public bool IsJitReadOnlyCodeAddress(uint address, int count, M68kBusAccessKind kind) => IsJitCodeAddress(address, count, kind);
        public ushort ReadJitCodeWord(uint address) => (ushort)memory.Peek(address, 2);
        public uint GetJitCodePageGeneration(uint address) => 0;
        public bool JitCodeRangeGenerationMatches(uint address, int count, uint first, uint last) => first == 0 && last == 0;
        public bool TryCaptureJitCodeSnapshot(uint address, int count, out M68kJitCodeSnapshot snapshot) { snapshot = default; return false; }
        public void ResetExternalDevices(long cycle) => memory.ResetExternalDevices(cycle);
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => memory.ReadByte(address, ref cycle, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => memory.ReadWord(address, ref cycle, kind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) => memory.ReadLong(address, ref cycle, kind);
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => memory.WriteByte(address, value, ref cycle, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => memory.WriteWord(address, value, ref cycle, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => memory.WriteLong(address, value, ref cycle, kind);
    }

    private sealed class Boundary : IM68kBusAccessTraceBatchBoundary, IM68kPureCpuTraceBatchBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
        public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long target, out long batchTarget) { batchTarget = target; return true; }
        public void AfterBusAccessTraceBatch(long previous, long current, int count) { }
        public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long target, out long batchTarget) { batchTarget = target; return true; }
        public void AfterPureCpuTraceBatch(long previous, long current, int count) { }
    }
}
