using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68010MovecTests(ITestOutputHelper output)
{
    // MC68000UM figure 2-3 and 6.3.6/7; M68000PM MOVEC 6-22/23.
    // Legal selectors, register widths and masks are independent specifications.
    private static readonly int[] Legal = [0, 1, 0x800, 0x801];
    private static readonly uint[] PairedValues = [0, 7, 0x400, 0x12345678, 0xfffffffd, 0xfffffffe, uint.MaxValue];
    private static SyntheticMachine Machine() => new(ModelSpec.All.Single(m => m.Id == "68010"));
    private static uint[] FunctionValues() => Enumerable.Range(0, 8).Select(v => (uint)v)
        .Concat(Enumerable.Range(3, 29).Select(bit => 1u << bit))
        .Concat([0xfffffff8u, uint.MaxValue, 0xaaaaaaabu, 0x55555554u]).Distinct().Order().ToArray();
    private static uint[] AddressValues() => Enumerable.Range(0, 32).Select(bit => 1u << bit)
        .Concat([0u, 7u, 0x400u, 0x4800u, 0x12345678u, uint.MaxValue, 0xaaaaaaaau, 0x55555555u]).Distinct().Order().ToArray();

    [Fact, Trait("Suite", "Synthetic")]
    public void Movec010EveryLegalControlRegisterPairPrivilegeAndCcr()
    {
        var m = Machine(); var report = new CoverageBatch("68010", "system-010-movec-pairs");
        foreach (var control in Legal) Fixture(control).WritesAndReadback(m, report, PairedValues,
            initial: Initial(control), readRegisters: Enumerable.Range(0, 16), groupConditionCodes: true);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void Movec010FunctionCodeMasksFromEveryInitialImage()
    {
        var m = Machine(); var report = new CoverageBatch("68010", "system-010-movec-masks");
        var values = FunctionValues(); Assert.Equal(41, values.Length);
        foreach (var control in new[] { 0, 1 })
        for (uint initial = 0; initial < 8; initial++) Fixture(control).WritesAndReadback(m, report, values,
            initial: initial, groupConditionCodes: true);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void Movec010RawControlImagesPreserveAllBitsAndActiveStack()
    {
        var m = Machine(); var report = new CoverageBatch("68010", "system-010-movec-reads");
        foreach (var control in Legal) Fixture(control).RawReads(m, report,
            control < 2 ? Enumerable.Range(0, 8).Select(v => (uint)v) : AddressValues(), groupConditionCodes: true);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void Movec010UndefinedSelectorsAndLegalUserFormsHaveExactFrames()
    {
        var m = Machine(); var report = new CoverageBatch("68010", "system-010-movec-encodings");
        foreach (var control in Enumerable.Range(0, 4096).Except(Legal))
        for (var general = 0; general < 16; general++)
        foreach (var store in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        foreach (var ccr in new[] { 0, 31 }) Rejected(m, report, control, general, store, supervisor, ccr);
        foreach (var control in Legal)
        for (var general = 0; general < 16; general++)
        foreach (var store in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++) Rejected(m, report, control, general, store, false, ccr);
        report.Complete(output);
    }

    private static void Rejected(SyntheticMachine m, CoverageBatch report, int control, int general, bool store, bool supervisor, int ccr)
    {
        m.Reset(ccr, supervisor); Vectors(m);
        var opcode = (ushort)(store ? 0x4e7b : 0x4e7a);
        SyntheticExecution.Prepare(m, [opcode, (ushort)(general << 12 | control)]);
        Controls(m.Core.State);
        var e = ArchitecturalExpectation.Capture(m); e.Pc = SyntheticMachine.Code + 4;
        // 010 incoming user privilege is checked before the selector is decoded.
        SyntheticExecution.ExpectException(m, e, supervisor ? 4 : 8);
        e.ControlChecks["SFC preserved"] = (s => s.SourceFunctionCode, 1);
        e.ControlChecks["DFC preserved"] = (s => s.DestinationFunctionCode, 5);
        e.ControlChecks["VBR preserved"] = (s => s.VectorBaseRegister, 0x400);
        e.ControlChecks["stack model preserved"] = (s => s.M68020StackModeEnabled ? 1u : 0, 0);
        SyntheticExecution.Run(m, e, report,
            $"68010/MOVEC/L/control={control:X3}/R{general}/store={store}/super={supervisor}/op={opcode:X4}/ccr={ccr:X2}", false);
    }

    private static uint Initial(int control) => control switch { 0 => 1, 1 => 5, 0x800 => 0x7800, _ => 0x400 };
    private static void Vectors(SyntheticMachine m)
    {
        m.InitializePhysical(0x410, 0x9040, 4);
        m.InitializePhysical(0x420, 0x9080, 4);
    }
    private static void Controls(M68kCpuState s)
    {
        s.SourceFunctionCode = 1; s.DestinationFunctionCode = 5; s.VectorBaseRegister = 0x400;
    }
    private static SyntheticMovecRegisterFixture Fixture(int control) => control switch
    {
        0 => new(0, "SFC", 7, s => s.SourceFunctionCode, (s, v) => s.SourceFunctionCode = v,
            PrepareMemory: Vectors, InitializeControls: Controls),
        1 => new(1, "DFC", 7, s => s.DestinationFunctionCode, (s, v) => s.DestinationFunctionCode = v,
            PrepareMemory: Vectors, InitializeControls: Controls),
        0x800 => new(0x800, "USP", uint.MaxValue, s => s.UserStackPointer, (s, v) => s.SetUserStackPointer(v),
            UserStackControl: true, PrepareMemory: Vectors, InitializeControls: Controls),
        0x801 => new(0x801, "VBR", uint.MaxValue, s => s.VectorBaseRegister, (s, v) => s.VectorBaseRegister = v,
            PrepareMemory: Vectors, InitializeControls: Controls),
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    };
}
