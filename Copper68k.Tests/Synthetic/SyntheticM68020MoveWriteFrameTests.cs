using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Cold, relocated private C023 images. This is a serialized software contract,
// not a claim about silicon internal register images or physical fault timing.
public sealed class SyntheticM68020MoveWriteFrameTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models = ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200").ToArray();
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];
    private static readonly uint[] Frames = [0x6000, 0x7000, 0x12346000];
    private static readonly string[] Invalid = ["foreign-zero", "foreign-read", "version-zero", "version-two", "count-four", "reserved", "read-cycle", "wrong-size", "bad-fc", "trace-t1", "trace-t0", "odd-pc", "bad-source", "bad-destination"];

    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_FRAME", "require private pending-write frame provenance"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarColdWriteFrames() => Audit(false, false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_FRAME", "require private pending-write frame provenance"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchColdWriteFrames() => Audit(true, false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_FRAME", "require private pending-write frame provenance"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarInvalidWriteFrames() => Audit(false, true);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_FRAME", "require private pending-write frame provenance"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchInvalidWriteFrames() => Audit(true, true);

    private void Audit(bool batch, bool invalid)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var current in new[] { "ISP", "MSP" })
        foreach (var lane in invalid ? Invalid : new[] { "pending", "completed" })
        {
            var report = new CoverageBatch(model.Id, $"move-write-frame-{width}-{current}-{lane}-{(batch ? "batch" : "scalar")}");
            var m = new SyntheticMachine(model, new FrameBus());
            foreach (var returned in Banks)
            foreach (var location in Enumerable.Range(0, Frames.Length))
            foreach (var count in invalid ? new[] { 3 } : Enumerable.Range(0, 4))
            foreach (var ccr in invalid ? new[] { 0, 31 } : Enumerable.Range(0, 32))
            {
                var id = $"{model.Id}/MOVE/write-frame/width={width}/current={current}/returned={returned}/location={location}/count={count}/ccr={ccr}/lane={lane}";
                try
                {
                    m.Reset(); _ = SyntheticExecution.Prepare(m, [0x4e73, 0x4e71]);
                    var frame = Frames[location];
                    var savedSr = (ushort)(0x700 | ccr | (returned is "ISP" or "MSP" ? 0x2000 : 0) | (returned is "user-M" or "MSP" ? 0x1000 : 0));
                    var value = 0x80a1b2c3u & (width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue);
                    // MOVE.B/W/L (A7)+,(A1), with completed source registers deliberately
                    // unrelated to the saved destination. No original MOVE is executed.
                    var opcode = (ushort)(width == 1 ? 0x129f : width == 2 ? 0x329f : 0x229f);
                    var ssw = (ushort)((lane == "completed" ? 0 : 0x100) | (width == 1 ? 0x10 : width == 2 ? 0x20 : 0) | (current == "ISP" ? 1 : 5));
                    for (uint a = frame - 8; a < frame + 40; a++) m.InitializePhysical(a, 0xa5, 1);
                    for (uint a = 0x43f8; a < 0x4510; a++) m.InitializePhysical(a, 0xa5, 1);
                    m.InitializePhysical(0x2000, 0x7e2a, 2); m.InitializePhysical(0x2002, 0x7c2b, 2); m.InitializePhysical(0x2004, 0x7a2c, 2); m.InitializePhysical(0x2006, 0x4e71, 2);
                    // Literal documented private layout; no production frame/EA helper.
                    m.InitializePhysical(frame, savedSr, 2); m.InitializePhysical(frame + 2, 0x2000, 4);
                    m.InitializePhysical(frame + 6, 0xa008, 2); m.InitializePhysical(frame + 8, 0xc023, 2);
                    m.InitializePhysical(frame + 10, ssw, 2); m.InitializePhysical(frame + 12, 0x7e11, 2); m.InitializePhysical(frame + 14, 0x7c22, 2);
                    m.InitializePhysical(frame + 16, 0x4400, 4); m.InitializePhysical(frame + 20, opcode, 2);
                    m.InitializePhysical(frame + 22, (uint)(1 | count << 8), 2); m.InitializePhysical(frame + 24, value, 4);
                    m.InitializePhysical(frame + 28, 0x7a33, 2); m.InitializePhysical(frame + 30, 0, 2);
                    if (lane == "completed") m.InitializePhysical(0x4400, value, width);
                    switch (lane)
                    {
                        case "foreign-zero": m.InitializePhysical(frame + 8, 0, 2); break;
                        case "foreign-read": m.InitializePhysical(frame + 8, 0xc022, 2); break;
                        case "version-zero": m.InitializePhysical(frame + 22, 0x300, 2); break;
                        case "version-two": m.InitializePhysical(frame + 22, 0x302, 2); break;
                        case "count-four": m.InitializePhysical(frame + 22, 0x401, 2); break;
                        case "reserved": m.InitializePhysical(frame + 30, 1, 2); break;
                        case "read-cycle": m.InitializePhysical(frame + 10, (uint)(ssw | 0x40), 2); break;
                        case "wrong-size": m.InitializePhysical(frame + 10, (uint)(ssw ^ 0x20), 2); break;
                        case "bad-fc": m.InitializePhysical(frame + 10, (uint)(ssw & ~7), 2); break;
                        case "trace-t1": m.InitializePhysical(frame, (uint)(savedSr | 0x8000), 2); break;
                        case "trace-t0": m.InitializePhysical(frame, (uint)(savedSr | 0x4000), 2); break;
                        case "odd-pc": m.InitializePhysical(frame + 2, 0x2001, 4); break;
                        case "bad-source": m.InitializePhysical(frame + 20, (uint)(opcode & ~0x38), 2); break;
                        case "bad-destination": m.InitializePhysical(frame + 20, (uint)(opcode & ~0x1c0), 2); break;
                    }
                    m.Core.State.A[0] = 0x4204; m.Core.State.A[1] = 0x4500;
                    m.Core.State.SetUserStackPointer(0x7800);
                    m.Core.State.SetInterruptStackPointer(current == "ISP" ? frame : 0x4700);
                    m.Core.State.SetMasterStackPointer(current == "MSP" ? frame : 0x7400);
                    m.Core.State.StatusRegister = current == "ISP" ? (ushort)0x2700 : (ushort)0x3700;
                    m.Core.State.SetActiveStackPointer(frame);
                    var e = ArchitecturalExpectation.Capture(m); var serial = m.Core.State.ExceptionSequence;
                    m.Bus.Accesses.Clear();
                    if (invalid)
                    {
                        var rejected = false;
                        try { Step(m, batch); } catch (UnsupportedM68kTimingException) { rejected = true; }
                        if (!rejected) throw new InvalidOperationException("Malformed or foreign private write frame was accepted");
                        e.Pc = 0x1002; Check(m, e);
                        CheckBanks(m, 0x7800, current == "ISP" ? frame : 0x4700, current == "MSP" ? frame : 0x7400);
                        if (m.Bus.Accesses.Any(a => a.Write)) throw new InvalidOperationException("Rejected write frame committed a memory effect");
                    }
                    else
                    {
                        var isp = current == "ISP" ? frame + 32 : 0x4700;
                        var msp = current == "MSP" ? frame + 32 : 0x7400;
                        e.Sr = savedSr; e.Pc = 0x2000;
                        e.A[7] = returned == "ISP" ? isp : returned == "MSP" ? msp : 0x7800;
                        e.InactiveStackPointer = returned is "ISP" or "MSP" ? 0x7800 : isp; e.MasterStackPointer = msp;
                        e.Write(0x4400, value, width, model);
                        Step(m, batch); Check(m, e); CheckBanks(m, 0x7800, isp, msp);
                        if (!m.Bus.Accesses.Where(a => a.Write).Select(a => (a.Address, a.Width)).SequenceEqual(lane == "completed" ? Array.Empty<(uint, int)>() : new[] { (0x4400u, width) }))
                            throw new InvalidOperationException("Cold write frame repeated or misplaced the pending write");
                        for (var n = 0; n < 3; n++)
                        {
                            e.D[7 - n] = (uint)(n < count ? new[] { 0x11, 0x22, 0x33 }[n] : 0x2a + n);
                            e.Sr = (ushort)(e.Sr & 0xfff0); e.Pc += 2;
                            var start = m.Bus.Accesses.Count; Step(m, batch); Check(m, e); CheckBanks(m, 0x7800, isp, msp);
                            if (m.Bus.Accesses.Skip(start).Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == 0x2000u + (uint)n * 2) != (n < count ? 0 : 1))
                                throw new InvalidOperationException("Cold write frame discarded or refetched a saved instruction word");
                        }
                    }
                    if (m.Core.State.ExceptionSequence != serial || m.Bus.Accesses.Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && !(a.Address >= model.Physical(frame) && a.Address + (uint)a.Width <= model.Physical(frame) + 32)))
                        throw new InvalidOperationException("Cold write frame replayed source or changed exception provenance");
                    report.Record(id, "passing", null);
                }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void CheckBanks(SyntheticMachine m, uint usp, uint isp, uint msp)
    {
        if (m.Core.State.UserStackPointer != usp || m.Core.State.InterruptStackPointer != isp || m.Core.State.MasterStackPointer != msp)
            throw new InvalidOperationException("Cold write frame changed a completed or inactive stack bank");
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { var error = e.Verify(m); if (error != null) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) { m.Core.ExecuteInstruction(); return; }
        var b = new Boundary(); if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, b) != 1 || b.Before != 1 || b.After != 1) throw new InvalidOperationException("Cold write frame batch boundaries differ");
    }
    private sealed class Boundary : IM68kInstructionBoundary { public int Before, After; public bool BeforeInstruction() { Before++; return true; } public void AfterInstruction(long a, long b) => After++; }
    private sealed class FrameBus : SparseRecordingBus, IM68kPhysicalAddressMap
    { public bool IsCpuPhysicalAddressMapped(uint address, int size, M68kBusAccessKind kind) => true; }
}
