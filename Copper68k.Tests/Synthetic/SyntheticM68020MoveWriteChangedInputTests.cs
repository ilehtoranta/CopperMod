using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Cold serialized C021/C023 contexts. No original MOVE or validation fault
// is executed here; the handler supplies changed validation data explicitly.
public sealed class SyntheticM68020MoveWriteChangedInputTests(ITestOutputHelper output)
{
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_CHANGED_INPUT", "require private changed validation-input audit"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarChangedWriteValidationInput() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_MOVE_WRITE_CHANGED_INPUT", "require private changed validation-input audit"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchChangedWriteValidationInput() => Audit(true);

    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(x => x.Id is "68EC020" or "68020" or "68030" or "A1200"))
        foreach (var bank in new[] { "ISP", "MSP" })
        {
            var report = new CoverageBatch(model.Id, $"move-write-changed-input-{bank}-{(batch ? "batch" : "scalar")}");
            var m = new SyntheticMachine(model);
            foreach (var location in new[] { 0, 1 })
            foreach (var ccr in Enumerable.Range(0, 32))
            foreach (var returned in new[] { "user", "user-M", "ISP", "MSP" })
            for (var request = 0; request < 4; request++)
            {
                var id = $"{model.Id}/RTE/write-changed-input/bank={bank}/location={location}/ccr={ccr}/returned={returned}/request={request}";
                try { Run(m, batch, bank, location, ccr, returned, request); report.Record(id, "passing", null); }
                catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException) { report.Record(id, "unsupported", ex.Message); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void Run(SyntheticMachine m, bool batch, string bank, int location, int ccr, string returned, int request)
    {
        m.Reset();
        var inner = location == 0 ? 0x6000u : 0x12346000u; var outer = inner - 92;
        var saved = (ushort)(0x700 | ccr | (returned is "ISP" or "MSP" ? 0x2000 : 0) | (returned is "user-M" or "MSP" ? 0x1000 : 0));
        var current = (ushort)((bank == "ISP" ? 0x2700 : 0x3700) | ccr);
        var supplied = request switch { 0 => 0xcafe0000u | (uint)(saved ^ 0x101f), 1 => 0x12342400u, 2 => 0xcafea024u, _ => 0xcafe9876u };
        var resultSr = request == 0 ? (ushort)(saved ^ 0x101f) : saved;
        var resultPc = request == 1 ? 0x12342400u : 0x2000u;
        _ = SyntheticExecution.Prepare(m, [0x2f7c, (ushort)(supplied >> 16), (ushort)supplied, 0x002c, 0x026f, 0xfeff, 0x000a, 0x4e73, 0x4e71]);
        for (uint at = outer - 8; at < inner + 40; at++) m.InitializePhysical(at, 0xa5, 1);
        for (uint at = 0x43f8; at < 0x4410; at++) m.InitializePhysical(at, 0xa5, 1);
        for (uint at = 0; at < 92; at += 2) m.InitializePhysical(outer + at, 0, 2);
        m.InitializePhysical(outer, current, 2); m.InitializePhysical(outer + 2, 0x9000, 4);
        m.InitializePhysical(outer + 6, 0xb008, 2); m.InitializePhysical(outer + 8, 0xc021, 2);
        m.InitializePhysical(outer + 10, (uint)(request == 1 ? 0x145 : 0x165), 2);
        m.InitializePhysical(outer + 16, inner + (request == 0 ? 0u : request == 1 ? 2u : request == 2 ? 6u : 30u), 4);
        m.InitializePhysical(outer + 20, inner, 4);
        m.InitializePhysical(outer + 28, (uint)((request >= 1 ? saved : 0) << 16) | (request >= 3 ? 0xa008u : 0), 4);
        m.InitializePhysical(outer + 32, request >= 2 ? 0x2000u : 0, 4);
        m.InitializePhysical(outer + 40, (uint)(request == 3 ? 4 : request), 2);
        m.InitializePhysical(inner, saved, 2); m.InitializePhysical(inner + 2, 0x2000, 4);
        m.InitializePhysical(inner + 6, 0xa008, 2); m.InitializePhysical(inner + 8, 0xc023, 2);
        m.InitializePhysical(inner + 10, 0x125, 2); m.InitializePhysical(inner + 12, 0x7e11, 2); m.InitializePhysical(inner + 14, 0x7c22, 2);
        m.InitializePhysical(inner + 16, 0x4400, 4); m.InitializePhysical(inner + 20, 0x329f, 2);
        m.InitializePhysical(inner + 22, 0x301, 2); m.InitializePhysical(inner + 24, 0xb2c3, 4);
        m.InitializePhysical(inner + 28, 0x7a33, 2); m.InitializePhysical(inner + 30, 0, 2);
        for (var n = 0; n < 4; n++) m.InitializePhysical(resultPc + (uint)n * 2, (uint)(n == 3 ? 0x4e71 : new[] { 0x7e2a, 0x7c2b, 0x7a2c }[n]), 2);
        var s = m.Core.State; s.A[0] = 0x4204; s.A[1] = 0x4500;
        s.SetUserStackPointer(0x7800); s.SetInterruptStackPointer(bank == "ISP" ? outer : 0x4700); s.SetMasterStackPointer(bank == "MSP" ? outer : 0x7400);
        s.StatusRegister = current; s.SetActiveStackPointer(outer);
        var e = ArchitecturalExpectation.Capture(m); var serial = s.ExceptionSequence; m.Bus.Accesses.Clear();
        e.Write(outer + 44, supplied, 4, m.Model); e.Pc += 8;
        e.Sr = (ushort)((current & 0xfff0) | (request == 1 ? 0 : 8));
        Step(m, batch); Check(m, e);
        e.Write(outer + 10, (uint)(request == 1 ? 0x45 : 0x65), 2, m.Model); e.Pc += 6; e.Sr = (ushort)(e.Sr & 0xfff0);
        Step(m, batch); Check(m, e);
        var start = m.Bus.Accesses.Count;
        var isp = bank == "ISP" ? inner + 32 : 0x4700u; var msp = bank == "MSP" ? inner + 32 : 0x7400u;
        e.A[7] = (resultSr & 0x2000) == 0 ? 0x7800u : (resultSr & 0x1000) == 0 ? isp : msp;
        e.InactiveStackPointer = (resultSr & 0x2000) == 0 ? isp : 0x7800u; e.MasterStackPointer = msp;
        e.Sr = resultSr; e.Pc = resultPc; e.Write(0x4400, 0xb2c3, 2, m.Model);
        Step(m, batch); Check(m, e);
        var reads = new (uint Offset, int Width)[] { (0, 2), (2, 4), (6, 2), (30, 2), (8, 2), (10, 2), (16, 4), (20, 2), (22, 2), (24, 4), (30, 2), (12, 2), (14, 2), (28, 2) }
            .Skip(request + 1).Concat(Enumerable.Range(0, 10).Select(n => (12u + (uint)n * 2, 2)));
        var actual = m.Bus.Accesses.Skip(start).Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= m.Model.Physical(inner) && a.Address < m.Model.Physical(inner) + 32).Select(a => (a.Address, a.Width));
        if (!actual.SequenceEqual(reads.Select(a => (m.Model.Physical(inner + a.Item1), a.Item2)))) throw new InvalidOperationException("Changed input replayed/skipped a validation or frame read");
        if (!m.Bus.Accesses.Skip(start).Where(a => a.Write).Select(a => (a.Address, a.Width, a.Value)).SequenceEqual(new[] { (0x4400u, 2, 0xb2c3u) })) throw new InvalidOperationException("Changed input repeated or misplaced the pending write");
        for (var n = 0; n < 3; n++)
        {
            e.D[7 - n] = (uint)new[] { 0x11, 0x22, 0x33 }[n]; e.Sr = (ushort)(e.Sr & 0xfff0); e.Pc += 2;
            var before = m.Bus.Accesses.Count; Step(m, batch); Check(m, e);
            if (m.Bus.Accesses.Skip(before).Any(a => !a.Write && a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == m.Model.Physical(resultPc + (uint)n * 2))) throw new InvalidOperationException("Changed input discarded a retained instruction word");
        }
        if (s.ExceptionSequence != serial || s.UserStackPointer != 0x7800 || s.InterruptStackPointer != isp || s.MasterStackPointer != msp) throw new InvalidOperationException("Changed input altered exception count or stack bank");
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e) { if (e.Verify(m) is { } error) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m, bool batch)
    {
        if (!batch) m.Core.ExecuteInstruction();
        else if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, new Boundary()) != 1) throw new InvalidOperationException("Empty changed-input batch");
    }
    private sealed class Boundary : IM68kInstructionBoundary { public bool BeforeInstruction() => true; public void AfterInstruction(long a, long b) { } }
}
