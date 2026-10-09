using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Literal MOVE.L encodings, independently chosen addresses and CCR expectations.
// Native 030 observer: scripts/reference/m68030-memory-destination-recovery.cpp.
// The physical map rejects a whole logical request, not a partial silicon cycle.
public sealed class SyntheticM68020MemoryDestinationRecoveryTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "A1200" or "68020" or "68030").ToArray();
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];
    private static readonly uint[] Values = [0, 0x80818283, 0x7fffffff, 0xffffffff];
    private static readonly (ushort Opcode, bool Post, bool Alias)[] Forms =
        [(0x2290, false, false), (0x2298, true, false), (0x2090, false, true), (0x2098, true, true)];

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectMemoryDestinationControls(bool batch) => Audit(batch, false);

    [EnvironmentFact("COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY", "require isolated source-read recovery candidate"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarMemoryDestinationRecovery() => Audit(false, true);

    [EnvironmentFact("COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY", "require isolated source-read recovery candidate"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchMemoryDestinationRecovery() => Audit(true, true);

    private void Audit(bool batch, bool fault)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        {
            var bus = new FaultBus();
            var m = new SyntheticMachine(model, bus);
            var report = new CoverageBatch(model.Id, $"memory-destination-{(fault ? "recovery" : "controls")}-{(batch ? "batch" : "scalar")}");
            foreach (var form in Forms)
            foreach (var bank in Banks)
            foreach (var value in Values)
            for (var ccr = 0; ccr < 32; ccr++)
            foreach (var lane in fault ? Enumerable.Range(0, 4) : new[] { 0 })
            {
                var id = $"{model.Id}/MOVE.L/source={(form.Post ? "post" : "indirect")}/dest={(form.Alias ? "alias-A0" : "A1")}/bank={bank}/value={value:X8}/ccr={ccr:X2}/lane={lane}";
                try
                {
                    bus.Disarm(); m.Reset(ccr);
                    _ = SyntheticExecution.Prepare(m, [form.Opcode, 0x7e2a, 0x4e71]);
                    // Guards include overlapping source/destination bytes.
                    for (uint address = 0x41f8; address < 0x4210; address++) m.InitializePhysical(address, 0xa5, 1);
                    for (uint address = 0x43f8; address < 0x4410; address++) m.InitializePhysical(address, 0xa5, 1);
                    m.InitializePhysical(0x4200, value, 4);
                    m.InitializePhysical(0x9020, 0x4e73, 2); m.InitializePhysical(0x9022, 0x4e71, 2);
                    m.Core.State.A[0] = 0x4200; m.Core.State.A[1] = 0x4400;
                    m.Core.State.SetUserStackPointer(0x7800); m.Core.State.SetInterruptStackPointer(0x4700); m.Core.State.SetMasterStackPointer(0x7400);
                    var sr = (ushort)(0x700 | ccr | (bank is "ISP" or "MSP" ? 0x2000 : 0) | (bank is "user-M" or "MSP" ? 0x1000 : 0));
                    m.Core.State.StatusRegister = sr;
                    m.Core.State.SetActiveStackPointer(bank == "MSP" ? 0x7400u : bank == "ISP" ? 0x4700u : 0x7800u);
                    var e = ArchitecturalExpectation.Capture(m);
                    var serial = m.Core.State.ExceptionSequence;
                    bus.Accesses.Clear();
                    if (fault) bus.Arm(0x4200u + (uint)lane);
                    Step(m, batch);
                    if (fault)
                    {
                        var frame = (bank is "user-M" or "MSP" ? 0x7400u : 0x4700u) - 92;
                        var ssw = 0x140u | ((sr & 0x2000) != 0 ? 5u : 1u);
                        if (bus.Rejected != 1 || m.Core.State.ExceptionSequence != serial + 1 || m.Core.State.LastExceptionVector != 2 ||
                            m.Core.State.ProgramCounter != 0x9020 || m.Core.State.A[7] != frame || m.Core.State.Halted || m.Core.State.Stopped ||
                            m.PeekPhysical(frame + 6, 2) != 0xb008 || (m.PeekPhysical(frame + 10, 2) & 0x1f7) != ssw || m.PeekPhysical(frame + 16, 4) != 0x4200)
                            throw new InvalidOperationException("Source-fault frame/SSW/vector differs");
                        if (bus.Accesses.Any(a => a.Address is >= 0x41f8 and < 0x4410 && (a.Write || a.Kind == M68kBusAccessKind.CpuDataRead)) ||
                            m.Core.State.A[0] != 0x4200 || m.Core.State.A[1] != 0x4400 || m.Core.State.D[7] != e.D[7])
                            throw new InvalidOperationException("Source fault prematurely completed an operand effect");
                        // Opaque private frame preservation; no invented silicon fields.
                        for (uint n = 0; n < 92; n++) e.Memory[model.Physical(frame + n)] = bus.Peek(model.Physical(frame + n));
                        e.ExceptionVector = 2;
                        Step(m, batch);
                    }
                    var destination = form.Alias ? (form.Post ? 0x4204u : 0x4200u) : 0x4400u;
                    if (form.Post) e.A[0] = 0x4204;
                    e.Write(destination, value, 4, model);
                    e.Pc = 0x1002;
                    e.Sr = (ushort)((sr & 0xfff0) | (value == 0 ? 4 : (value & 0x80000000) != 0 ? 8 : 0));
                    Check(m, e);
                    // Existing approximate execution policy, not physical cycle accuracy.
                    var plan = ((M68kAdvancedTimingInterpreter)m.Core).Timing.LastInstructionTiming.Plan;
                    var key = form.Post ? M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect : M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect;
                    if (plan.Key != key || plan.NativeCycles != 8 ||
                        plan.Barriers != M68kTimingBarrier.None || plan.UsesHeadTail != (model.Id == "68030") ||
                        plan.HeadCycles != (model.Id == "68030" ? 1 : 0) || plan.TailCycles != (model.Id == "68030" ? 1 : 0))
                        throw new InvalidOperationException($"Memory destination changed the existing MOVE timing policy: {plan.Key}/{plan.NativeCycles}/{plan.UsesHeadTail}/{plan.HeadCycles}/{plan.TailCycles}/{plan.Barriers}");
                    if (bus.Rejected != (fault ? 1 : 0) || m.Core.State.ExceptionSequence != serial + (fault ? 1 : 0))
                        throw new InvalidOperationException("Source was rejected again during recovery");
                    var transfers = bus.Accesses.Where(a => a.Address is >= 0x41f8 and < 0x4410 &&
                        a.Kind is M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite).ToArray();
                    if (transfers.Length != 2 || transfers[0].Write || transfers[0].Address != 0x4200 || transfers[0].Width != 4 ||
                        !transfers[1].Write || transfers[1].Address != destination || transfers[1].Width != 4 || transfers[1].Value != value)
                        throw new InvalidOperationException("Source read/destination write address, order, width or count differs");
                    if (bus.Accesses.Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == 0x1000) > 1)
                        throw new InvalidOperationException("Recovery replayed the MOVE opcode fetch");
                    e.D[7] = 42; e.Pc = 0x1004; e.Sr = (ushort)(e.Sr & 0xfff0);
                    Step(m, batch); Check(m, e);
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
        var boundary = new Boundary();
        if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, boundary) != 1 || boundary.Before != 1 || boundary.After != 1)
            throw new InvalidOperationException("Batch boundary count differs");
    }
    private sealed class Boundary : IM68kInstructionBoundary
    { public int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class FaultBus : SparseRecordingBus, IM68kPhysicalAddressMap
    {
        private uint? denied;
        internal int Rejected { get; private set; }
        internal void Disarm() { denied = null; Rejected = 0; }
        internal void Arm(uint address) => denied = address;
        public bool IsCpuPhysicalAddressMapped(uint address, int size, M68kBusAccessKind kind)
        {
            if (denied is not { } at || kind != M68kBusAccessKind.CpuDataRead || size != 4 || unchecked(at - address) >= 4) return true;
            denied = null; Rejected++; return false;
        }
    }
}
