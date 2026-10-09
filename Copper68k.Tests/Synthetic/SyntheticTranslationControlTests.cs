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
        var fixture = new SyntheticMovecRegisterFixture(3, "TC", modelId == "68040" ? 0xc000u : 0xfffeu,
            s => s.M68040Mmu.TranslationControl, (s, value) => s.M68040Mmu.TranslationControl = value);
        fixture.WritesAndReadback(m, report, Values(), storeRegisters: 15);
        // Raw reads avoid the legacy private MMU enable bit (31).
        fixture.RawReads(m, report, Values().Where(x => (x & 0x80000000) == 0));
        report.Complete(output);
    }

    private static uint[] Values() => new uint[] { 0, 1, 0x4000, 0x7fff, 0xffff7fff, 0x1234, 0x7100, 0x55555555, 0xaaaa2aaa }
        .Concat(Enumerable.Range(0, 32).Where(bit => bit != 15).Select(bit => 1u << bit)).Distinct().Order().ToArray();
}
