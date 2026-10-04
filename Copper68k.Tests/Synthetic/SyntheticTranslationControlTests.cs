using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticTranslationControlTests(ITestOutputHelper output)
{
    [Theory, InlineData("68040"), InlineData("68060"), Trait("Suite", "Synthetic")]
    public void MovecTranslationControlDisabledWritesReadbackAndReservedBits(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "system-translation-control");
        // MC68040UM 3.1.2 / figure 3-4; MC68060UM 4.1.2 / figure 4-4.
        // Expectations are fixed architectural masks, not production MMU helpers.
        var mask = modelId == "68040" ? 0xc000u : 0xfffeu;
        foreach (var value in Values())
        for (var general = 0; general < 15; general++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr, supervisor);
            if (general < 8) m.Core.State.D[general] = value;
            else m.Core.State.A[general - 8] = value;
            var e = SyntheticExecution.Prepare(m, [0x4e7b, (ushort)(general << 12 | 3), 0x4e7a, (ushort)(general << 12 | 3)]);
            e.Pc = SyntheticMachine.Code + 4;
            var id = $"{modelId}/MOVEC/TC/R{general}/value={value:X8}/super={supervisor}/ccr={ccr:X2}";
            if (!supervisor)
            {
                SyntheticExecution.ExpectException(m, e, 8);
                e.ControlChecks["TC untouched"] = (s => s.M68040Mmu.TranslationControl, 0);
                SyntheticExecution.Run(m, e, report, id + "/privilege", false);
                continue;
            }
            e.ControlChecks["TC stored"] = (s => s.M68040Mmu.TranslationControl, value & mask);
            if (!Step(m, e, report, id + "/write"))
            {
                report.Record(id + "/read", "untested", "TC write failed; dependent readback not executed.");
                continue;
            }
            e.Pc += 4;
            if (general < 8) e.D[general] = value & mask;
            else e.A[general - 8] = value & mask;
            SyntheticExecution.Run(m, e, report, id + "/read");
        }
        // Reading unimplemented bits must return zero even for an internally
        // supplied state. Avoid the legacy private MMU enable bit (31) here.
        foreach (var value in Values().Where(x => (x & 0x80000000) == 0))
        for (var general = 0; general < 16; general++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr);
            var e = SyntheticExecution.Prepare(m, [0x4e7a, (ushort)(general << 12 | 3)]);
            m.Core.State.M68040Mmu.TranslationControl = value;
            if (general < 8) e.D[general] = value & mask;
            else e.A[general - 8] = value & mask;
            e.ControlChecks["read has no TC side effect"] = (s => s.M68040Mmu.TranslationControl, value);
            SyntheticExecution.Run(m, e, report, $"{modelId}/MOVEC/TC/read-R{general}/internal={value:X8}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    private static uint[] Values() => new uint[] { 0, 1, 0x4000, 0x7fff, 0xffff7fff, 0x1234, 0x7100, 0x55555555, 0xaaaa2aaa }
        .Concat(Enumerable.Range(0, 32).Where(bit => bit != 15).Select(bit => 1u << bit)).Distinct().Order().ToArray();

    private static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try
        {
            m.Core.ExecuteInstruction();
            var mismatch = e.Verify(m);
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            return mismatch == null;
        }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
        { report.Record(id, "unsupported", ex.Message); return false; }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); return false; }
    }
}
