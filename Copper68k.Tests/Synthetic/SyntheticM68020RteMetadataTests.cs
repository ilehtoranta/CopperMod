using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Interpreter-private C021 rejection controls, not foreign silicon frames.
public sealed class SyntheticM68020RteMetadataTests(ITestOutputHelper output)
{
    [EnvironmentFact("COPPER68K_RUN_020_RTE_METADATA", "qualify private C021 metadata rejection"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarPrivateMetadataControls() => Audit(false);
    [EnvironmentFact("COPPER68K_RUN_020_RTE_METADATA", "qualify private C021 metadata rejection"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchPrivateMetadataControls() => Audit(true);

    private static readonly string[] Forms = ["valid", "phase-range", "phase-version", "phase-end", "fault-address", "ssw-size", "ssw-fc", "ssw-extra", "returned-frame", "returned-bank"];
    private void Audit(bool batch)
    {
        var failures = new List<Exception>();
        foreach (var model in ModelSpec.All.Where(m => m.Id is "68EC020" or "68020" or "68030" or "A1200"))
        {
            var report = new CoverageBatch(model.Id, $"rte-private-metadata-{(batch ? "batch" : "scalar")}");
            var machine = new SyntheticMachine(model);
            foreach (var master in new[] { false, true })
            foreach (var high in new[] { false, true })
            foreach (var form in Forms)
            for (var ccr = 0; ccr < 32; ccr++)
            {
                var id = $"{model.Id}/RTE/private-C021/{form}/master={master}/high={high}/op=4E73/ccr={ccr:X2}";
                try { Run(machine, batch, master, high, ccr, form); report.Record(id, "passing", null); }
                catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(e => e.Message)));
    }

    private static void Run(SyntheticMachine m, bool batch, bool master, bool high, int ccr, string form)
    {
        m.Reset(); _ = SyntheticExecution.Prepare(m, [0x4e73, 0x7e2a, 0x4e71]);
        var inner = high ? 0x12346000u : 0x6000u; var outer = inner - 92;
        var sr = (ushort)((master ? 0x3700 : 0x2700) | ccr);
        for (var at = outer - 8; at < inner + 0x110; at++) m.InitializePhysical(at, 0xa5, 1);
        for (uint offset = 0; offset < 92; offset += 2) m.InitializePhysical(outer + offset, 0, 2);
        m.InitializePhysical(outer, sr, 2); m.InitializePhysical(outer + 2, 0x9000, 4);
        m.InitializePhysical(outer + 6, 0xb008, 2); m.InitializePhysical(outer + 8, 0xc021, 2);
        m.InitializePhysical(outer + 10, 0x65, 2); // supplied word / supervisor data
        m.InitializePhysical(outer + 16, inner, 4); m.InitializePhysical(outer + 20, inner, 4);
        m.InitializePhysical(outer + 28, (uint)sr << 16, 4); m.InitializePhysical(outer + 32, 0x3000, 4);
        m.InitializePhysical(outer + 44, sr, 4); // independent supplied initial SR
        m.InitializePhysical(inner, sr, 2); m.InitializePhysical(inner + 2, 0x3000, 4);
        m.InitializePhysical(inner + 6, 0, 2); m.InitializePhysical(0x3000, 0x7c2b, 2);
        m.InitializePhysical(0x3002, 0x4e71, 2);
        switch (form)
        {
            case "phase-range": m.InitializePhysical(outer + 40, 0xffff, 2); break;
            case "phase-version": m.InitializePhysical(outer + 40, 3, 2); break;
            case "phase-end": m.InitializePhysical(outer + 40, 4, 2); break;
            case "fault-address": m.InitializePhysical(outer + 16, inner + 1, 4); break;
            case "ssw-size": m.InitializePhysical(outer + 10, 0x45, 2); break;
            case "ssw-fc": m.InitializePhysical(outer + 10, 0x61, 2); break;
            case "ssw-extra": m.InitializePhysical(outer + 10, 0x265, 2); break;
            case "returned-frame":
                m.InitializePhysical(outer + 16, inner + 0x100, 4); m.InitializePhysical(outer + 20, inner + 0x100, 4); break;
            case "returned-bank": m.InitializePhysical(outer, (uint)(sr ^ 0x1000), 2); break;
        }
        var s = m.Core.State;
        s.SetUserStackPointer(0x7800); s.SetInterruptStackPointer(master ? 0x4700u : outer);
        s.SetMasterStackPointer(master ? outer : 0x7400u); s.StatusRegister = sr; s.SetActiveStackPointer(outer);
        var e = ArchitecturalExpectation.Capture(m); var serial = s.ExceptionSequence;
        var isp = s.InterruptStackPointer; var msp = s.MasterStackPointer;
        m.Bus.Accesses.Clear();
        var boundary = new Boundary();
        void Step()
        {
            if (batch) Assert.Equal(1, ((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, boundary));
            else m.Core.ExecuteInstruction();
        }
        if (form == "valid")
        {
            Step(); e.Pc = 0x3000; e.A[7] = inner + 8;
            if (master) { e.MasterStackPointer = inner + 8; msp = inner + 8; } else isp = inner + 8;
            if (batch) { Assert.Equal(1, boundary.Before); Assert.Equal(1, boundary.After); }
            Assert.Null(e.Verify(m));
            // A following instruction proves the restored PC is actually used.
            e.D[6] = 43; e.Pc = 0x3002; e.Sr = (ushort)(sr & 0xfff0); Step();
            if (batch) { Assert.Equal(2, boundary.Before); Assert.Equal(2, boundary.After); }
        }
        else
        {
            Assert.Throws<UnsupportedM68kTimingException>(Step);
            e.Pc = SyntheticMachine.Code + 2;
            if (batch) { Assert.Equal(1, boundary.Before); Assert.Equal(0, boundary.After); }
            Assert.DoesNotContain(m.Bus.Accesses, a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= m.Model.Physical(inner) && a.Address < m.Model.Physical(inner) + 0x110);
        }
        Assert.Null(e.Verify(m)); Assert.Equal(serial, s.ExceptionSequence);
        Assert.Equal(0x7800u, s.UserStackPointer); Assert.Equal(isp, s.InterruptStackPointer); Assert.Equal(msp, s.MasterStackPointer);
        Assert.DoesNotContain(m.Bus.Accesses, a => a.Write);
    }
    private sealed class Boundary : IM68kInstructionBoundary
    {
        internal int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long before, long after) => After++;
    }
}
