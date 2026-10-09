using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticBusControlTests(ITestOutputHelper output)
{
    private static readonly uint[] Shadows = [0, 0x10000000, 0x40000000, 0x50000000];

    [Fact, Trait("Suite", "Synthetic")]
    public void MovecBusControlWritesCommandsPreservesShadowsAndReadsAllBits()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-bus-control");
        // MC68060UM 7.4: L/LE commands, SL/SLE exception snapshots.
        // Preserve shadow writes as independently corroborated by pinned WinUAE
        // newcpu_common.cpp, not by a production-derived expectation.
        var fixture = new SyntheticMovecRegisterFixture(8, "BUSCR", 0xf0000000,
            s => s.M68060BusControl, (s, v) => s.M68060BusControl = v,
            (old, value) => (old & 0x50000000) | (value & 0xa0000000));
        foreach (var shadow in Shadows) fixture.WritesAndReadback(m, report, Values(), initial: shadow);
        fixture.RawReads(m, report, Enumerable.Range(0, 16).Select(n => (uint)n << 28));
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void BusControlTrapNestedTrapAndReturnsRetainLockSnapshots()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-bus-control-exceptions");
        foreach (var image in Enumerable.Range(0, 16).Select(n => (uint)n << 28))
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        for (var trap = 0; trap < 16; trap++)
        {
            m.Reset(ccr, supervisor);
            var firstHandler = 0x9000u + (uint)(32 + trap) * 16;
            // Use TRAP #14 as nested vector, except when it is the first vector.
            var nestedVector = trap == 14 ? 45 : 46;
            var nestedPc = 0xd000u;
            m.Bus.Initialize((uint)nestedVector * 4, nestedPc, 4);
            m.InitializePhysical(firstHandler, (uint)(0x4e40 | nestedVector - 32), 2);
            m.InitializePhysical(firstHandler + 2, 0x4e73, 2);
            m.InitializePhysical(nestedPc, 0x4e73, 2);
            var e = SyntheticExecution.Prepare(m, [(ushort)(0x4e40 | trap)]);
            m.Core.State.M68060BusControl = image;
            var originalSr = e.Sr;
            var originalStack = e.A[7];
            var shadow = (image & 0x50000000) | ((image & 0xa0000000) >> 1);
            var id = $"68060/BUSCR/trap={trap}/image={image:X8}/super={supervisor}/ccr={ccr:X2}";
            e.ControlChecks["BUSCR snapshot"] = (s => s.M68060BusControl, shadow);
            SyntheticExecution.ExpectException(m, e, 32 + trap, SyntheticMachine.Code + 2);
            if (!Step(m, e, report, id + "/entry")) { Untested(report, id, "nested", "nested-RTE", "outer-RTE"); continue; }
            // MC68060AR section 5: processor never clears SL/SLE on nested entry.
            var handlerSr = e.Sr;
            SyntheticExecution.ExpectException(m, e, nestedVector, firstHandler + 2);
            if (!Step(m, e, report, id + "/nested")) { Untested(report, id, "nested-RTE", "outer-RTE"); continue; }
            e.A[7] += 8; e.Sr = handlerSr; e.Pc = firstHandler + 2; e.ExceptionVector = null;
            if (!Step(m, e, report, id + "/nested-RTE")) { Untested(report, id, "outer-RTE"); continue; }
            e.A[7] += 8;
            SyntheticSystemTests.ApplyStatus(m, e, originalSr);
            e.Pc = SyntheticMachine.Code + 2;
            if (e.A[7] != originalStack) throw new InvalidOperationException("Independent return fixture stack imbalance");
            SyntheticExecution.Run(m, e, report, id + "/outer-RTE");
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void BusControlPrivilegeTraceAndResetState()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-bus-control-state");
        foreach (var image in Enumerable.Range(0, 16).Select(n => (uint)n << 28))
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var kind in new[] { "privilege-read", "privilege-write", "trace", "illegal" })
        {
            var supervisor = kind is "trace" or "illegal";
            m.Reset(ccr, supervisor);
            ushort[] words = kind switch { "privilege-read" => [0x4e7a, 8], "privilege-write" => [0x4e7b, 8], "illegal" => [0x4afc], _ => [0x4e71] };
            var e = SyntheticExecution.Prepare(m, words);
            m.Core.State.M68060BusControl = image;
            if (kind == "trace") { m.Core.State.StatusRegister |= 0x8000; e.Sr |= 0x8000; }
            var vector = kind == "trace" ? 9 : kind == "illegal" ? 4 : 8;
            SyntheticExecution.ExpectException(m, e, vector, kind == "trace" ? SyntheticMachine.Code + 2 : SyntheticMachine.Code);
            e.ControlChecks["BUSCR snapshot"] = (s => s.M68060BusControl, (image & 0x50000000) | ((image & 0xa0000000) >> 1));
            SyntheticExecution.Run(m, e, report, $"68060/BUSCR/{kind}/image={image:X8}/ccr={ccr:X2}", false);
        }
        foreach (var image in Enumerable.Range(0, 16).Select(n => (uint)n << 28))
        {
            m.Reset(); m.Core.State.M68060BusControl = image;
            m.Core.Reset(SyntheticMachine.Code, 0x8000);
            var e = ArchitecturalExpectation.Capture(m);
            e.ControlChecks["BUSCR reset"] = (s => s.M68060BusControl, 0);
            var mismatch = e.Verify(m);
            report.Record($"68060/BUSCR/reset/image={image:X8}", mismatch == null ? "passing" : "mismatching", mismatch);
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void BusControlInterruptSnapshotsAndMaskedRequests()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-bus-control-interrupts");
        foreach (var image in Enumerable.Range(0, 16).Select(n => (uint)n << 28))
        foreach (var supervisor in new[] { false, true })
        foreach (var stopped in new[] { false, true })
        foreach (var accepted in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr, supervisor);
            var e = SyntheticExecution.Prepare(m, [0x4e71]); e.Pc = SyntheticMachine.Code;
            m.Core.State.StatusRegister = (ushort)((supervisor ? 0x2000 : 0) | (accepted ? 0 : 0x700) | ccr);
            e.Sr = m.Core.State.StatusRegister;
            m.Core.State.Stopped = stopped; e.Stopped = stopped;
            m.Core.State.M68060BusControl = image;
            e.ControlChecks["BUSCR"] = (s => s.M68060BusControl, accepted ? (image & 0x50000000) | ((image & 0xa0000000) >> 1) : image);
            if (accepted)
            {
                SyntheticExecution.ExpectException(m, e, 28, SyntheticMachine.Code);
                e.Sr = (ushort)((e.Sr & 0xf8ff) | 0x300); e.Stopped = false;
            }
            var id = $"68060/BUSCR/interrupt/image={image:X8}/super={supervisor}/stop={stopped}/accepted={accepted}/ccr={ccr:X2}";
            try
            {
                m.Core.RequestInterrupt(3, 28 * 4);
                var mismatch = e.Verify(m);
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }

    private static SyntheticMachine Machine() => new(ModelSpec.All.Single(m => m.Id == "68060"));
    private static uint[] Values() => new uint[] { 0, uint.MaxValue, 0x50000000, 0xf0000000, 0x0fffffff, 0x55555555, 0xaaaaaaaa }
        .Concat(Enumerable.Range(0, 32).Select(bit => 1u << bit)).Distinct().Order().ToArray();
    private static void Untested(CoverageBatch report, string id, params string[] phases)
    { foreach (var phase in phases) report.Record(id + "/" + phase, "untested", "Prerequisite instruction failed; no partial instruction retry."); }
    private static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try { m.Core.ExecuteInstruction(); var mismatch = e.Verify(m); report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch); return mismatch == null; }
        catch (Exception ex) { report.Record(id, ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException ? "unsupported" : "mismatching", ex.ToString()); return false; }
    }
}
