using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticPcrTests(ITestOutputHelper output)
{
    // MC68060UM 3.2.2.5 / figure 3-5 / 11.1.2.1.1 / D-22:
    // first MC68060 revision, EDEBUG=7, DFP=1, ESS=0; ID/revision read-only.
    // Physical debug output, superscalar timing and FPU arithmetic are not tested.
    private const uint Identification = 0x04300000;
    private static readonly uint[] Controls = [0, 1, 2, 3, 0x80, 0x81, 0x82, 0x83];
    private static SyntheticMachine Machine() => new(ModelSpec.All.Single(m => m.Id == "68060"));
    private static SyntheticMovecRegisterFixture Fixture() => new(0x808, "PCR", uint.MaxValue,
        s => s.M68060ProcessorConfiguration, (s, v) => s.M68060ProcessorConfiguration = Identification | v,
        (_, value) => Identification | (value & 0x83));

    [Fact, Trait("Suite", "Synthetic")]
    public void PcrDefinedControlsAllRegistersPrivilegeAndCcr()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-pcr-defined");
        var fixture = Fixture();
        foreach (var initial in Controls) fixture.WritesAndReadback(m, report, Controls, initial: initial);
        fixture.RawReads(m, report, Controls.Select(v => Identification | v));
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void PcrIdentificationAndRevisionWritesAreIgnored()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-pcr-identification");
        // Bits 8..31 are explicitly ignored on write. Never set reserved 6..2 here.
        var high = new uint[] { 0, 0xffffff00, 0x55555500, 0xaaaaaa00 }
            .Concat(Enumerable.Range(8, 24).Select(bit => 1u << bit)).Distinct().Order().ToArray();
        Assert.Equal(28, high.Length);
        var values = high.SelectMany(v => Controls.Select(c => v | c)).ToArray();
        foreach (var initial in Controls) Fixture().WritesAndReadback(m, report, values, initial: initial, conditionCodes: [0, 31]);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void PcrReservedWritesRetainRepositoryProfilePolicy()
    {
        // These writes violate the manual's requirement to keep bits 6..2 zero.
        // Preserve the existing exact-mask regression as repository policy only.
        // MC68060DE I14/I15 assign bit 5 on specific masksets; no hardware claim.
        var m = Machine(); var report = new CoverageBatch("68060", "system-pcr-reserved-policy");
        uint[] values = [4, 8, 16, 32, 64, 0x7c, 0xffffff7c, uint.MaxValue];
        foreach (var initial in Controls) Fixture().WritesAndReadback(m, report, values, initial: initial, conditionCodes: [0, 31]);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void PcrResetClearsDefinedControlsFromBothStacks()
    {
        var m = Machine(); var report = new CoverageBatch("68060", "system-pcr-reset");
        foreach (var image in Controls)
        for (var general = 0; general < 16; general++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr);
            m.Core.State.D[0] = image;
            var e = SyntheticExecution.Prepare(m, [0x4e7b, 0x0808, 0x4e7a, (ushort)(general << 12 | 0x808)]);
            e.Pc = SyntheticMachine.Code + 4;
            e.ControlChecks["PCR"] = (s => s.M68060ProcessorConfiguration, Identification | image);
            var id = $"68060/MOVEC/L/PCR/reset/R{general}/super={supervisor}/image={image:X8}";
            var sample = $"/op=4E7B/ccr={ccr:X2}";
            if (!Step(m, e, report, id + "/write" + sample)) { Untested(report, id, sample, "reset", "read"); continue; }
            m.Core.State.StatusRegister = (ushort)((supervisor ? 0x2700 : 0x0700) | ccr);
            e = ArchitecturalExpectation.Capture(m);
            Array.Clear(e.D); Array.Clear(e.A); e.A[7] = 0x8000;
            e.Sr = 0x2700; e.Pc = SyntheticMachine.Code + 4;
            e.InactiveStackPointer = 0; e.MasterStackPointer = 0x8000;
            e.ControlChecks["PCR"] = (s => s.M68060ProcessorConfiguration, Identification);
            // Reset's register clearing is a public API convention, while PCR
            // control clearing and supervisor/IPL state are architectural rules.
            m.Core.Reset(e.Pc, 0x8000);
            var mismatch = e.Verify(m);
            report.Record(id + "/reset" + sample, mismatch == null ? "passing" : "mismatching", mismatch);
            if (mismatch != null) { Untested(report, id, sample, "read"); continue; }
            if (general < 8) e.D[general] = Identification; else e.A[general - 8] = Identification;
            e.Pc += 4;
            SyntheticExecution.Run(m, e, report, id + "/read" + sample);
        }
        report.Complete(output);
    }

    private static void Untested(CoverageBatch report, string id, string sample, params string[] phases)
    { foreach (var phase in phases) report.Record(id + "/" + phase + sample, "untested", "Prerequisite failed; no instruction retry."); }
    private static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try { m.Core.ExecuteInstruction(); var mismatch = e.Verify(m); report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch); return mismatch == null; }
        catch (Exception ex) { report.Record(id, ex is UnsupportedM68kOpcodeException or UnsupportedM68kTimingException or UnsupportedM68040InstructionException ? "unsupported" : "mismatching", ex.ToString()); return false; }
    }
}
