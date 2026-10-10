using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticMovecControlEncodingTests(ITestOutputHelper output)
{
    // MC68060UM programming models (3,4,5,7): availability is independent
    // of the production decoder. 8.2.4 defines undefined MOVEC fields as illegal.
    private static readonly int[] Legal060 = [0, 1, 2, 3, 4, 5, 6, 7, 8, 0x800, 0x801, 0x806, 0x807, 0x808];

    [Fact, Trait("Suite", "Synthetic")]
    public void Movec060UndefinedControlFieldsPrecedePrivilegeAndValidUserFormsTrap()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68060"));
        var report = new CoverageBatch("68060", "system-movec-control-encodings");
        foreach (var control in Enumerable.Range(0, 4096).Except(Legal060))
        for (var general = 0; general < 16; general++)
        foreach (var store in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        foreach (var ccr in new[] { 0, 31 }) Run(m, report, control, general, store, supervisor, ccr, 4);
        foreach (var control in Legal060)
        for (var general = 0; general < 16; general++)
        foreach (var store in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++) Run(m, report, control, general, store, false, ccr, 8);
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, CoverageBatch report, int control, int general, bool store, bool supervisor, int ccr, int vector)
    {
        m.Reset(ccr, supervisor);
        var op = (ushort)(store ? 0x4e7b : 0x4e7a);
        var e = SyntheticExecution.Prepare(m, [op, (ushort)(general << 12 | control)]);
        SyntheticExecution.ExpectException(m, e, vector);
        e.ControlChecks["TC untouched"] = (s => s.M68040Mmu.TranslationControl, 0);
        e.ControlChecks["ITT0 untouched"] = (s => s.M68040Mmu.InstructionTransparentTranslation0, 0);
        e.ControlChecks["ITT1 untouched"] = (s => s.M68040Mmu.InstructionTransparentTranslation1, 0);
        e.ControlChecks["DTT0 untouched"] = (s => s.M68040Mmu.DataTransparentTranslation0, 0);
        e.ControlChecks["DTT1 untouched"] = (s => s.M68040Mmu.DataTransparentTranslation1, 0);
        e.ControlChecks["BUSCR untouched"] = (s => s.M68060BusControl, 0);
        e.ControlChecks["PCR untouched"] = (s => s.M68060ProcessorConfiguration, 0x04300000);
        e.ControlChecks["CACR untouched"] = (s => s.CacheControlRegister, 0);
        e.ControlChecks["SFC untouched"] = (s => s.SourceFunctionCode, 0);
        e.ControlChecks["DFC untouched"] = (s => s.DestinationFunctionCode, 0);
        e.ControlChecks["VBR untouched"] = (s => s.VectorBaseRegister, 0);
        e.ControlChecks["URP untouched"] = (s => s.M68040Mmu.UserRootPointer, 0);
        e.ControlChecks["SRP untouched"] = (s => s.M68040Mmu.SupervisorRootPointer, 0);
        SyntheticExecution.Run(m, e, report, $"68060/MOVEC/L/control={control:X3}/R{general}/store={store}/super={supervisor}/ccr={ccr:X2}/vector={vector}", false);
    }
}
