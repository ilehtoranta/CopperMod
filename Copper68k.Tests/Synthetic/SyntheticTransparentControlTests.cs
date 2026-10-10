using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticTransparentControlTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> TransparentRegisters =>
        from model in new[] { "68040", "68060" } from control in new[] { 4, 5, 6, 7 } select new object[] { model, control };
    public static IEnumerable<object[]> RootRegisters =>
        from model in new[] { "68040", "68060" } from control in new[] { 0x806, 0x807 } select new object[] { model, control };

    [Theory, MemberData(nameof(TransparentRegisters)), Trait("Suite", "Synthetic")]
    public void MovecTransparentRegistersDisabledWritesAndZeroReadBits(string modelId, int control)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, $"system-transparent-control-{control}");
        // MC68040UM 3.1.3 / figure 3-5; MC68060UM 4.1.3 / figure 4-5:
        // bits 12-10, 7, 4, 3, 1 and 0 always read zero. E is clear.
        var fixture = Fixture(control, 0xffffe364);
        fixture.WritesAndReadback(m, report, TransparentValues());
        fixture.RawReads(m, report, TransparentValues());
        report.Complete(output);
    }

    [Theory, MemberData(nameof(RootRegisters)), Trait("Suite", "Synthetic")]
    public void MovecRootPointersPreserveLegalAlignedAddresses(string modelId, int control)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, $"system-root-control-{control:X3}");
        // Both processor manuals 3.1.1/4.1.1 require bits 8-0 zero.
        // Nonaligned writes are outside the architectural programming rule;
        // do not impose 060's masking convention on 040 from that rule alone.
        var fixture = Fixture(control, uint.MaxValue);
        fixture.WritesAndReadback(m, report, RootValues());
        fixture.RawReads(m, report, RootValues());
        report.Complete(output);
    }

    private static SyntheticMovecRegisterFixture Fixture(int control, uint mask) => control switch
    {
        4 => new(control, "ITT0", mask, s => s.M68040Mmu.InstructionTransparentTranslation0, (s, v) => s.M68040Mmu.InstructionTransparentTranslation0 = v),
        5 => new(control, "ITT1", mask, s => s.M68040Mmu.InstructionTransparentTranslation1, (s, v) => s.M68040Mmu.InstructionTransparentTranslation1 = v),
        6 => new(control, "DTT0", mask, s => s.M68040Mmu.DataTransparentTranslation0, (s, v) => s.M68040Mmu.DataTransparentTranslation0 = v),
        7 => new(control, "DTT1", mask, s => s.M68040Mmu.DataTransparentTranslation1, (s, v) => s.M68040Mmu.DataTransparentTranslation1 = v),
        0x806 => new(control, "URP", mask, s => s.M68040Mmu.UserRootPointer, (s, v) => s.M68040Mmu.UserRootPointer = v),
        0x807 => new(control, "SRP", mask, s => s.M68040Mmu.SupervisorRootPointer, (s, v) => s.M68040Mmu.SupervisorRootPointer = v),
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    };

    private static uint[] TransparentValues() => new uint[] { 0, 1, 0x4000, 0x7fff, 0xffff7fff, 0x1234, 0x7100, 0x55555555, 0xaaaa2aaa }
        .Concat(Enumerable.Range(0, 32).Where(bit => bit != 15).Select(bit => 1u << bit)).Distinct().Order().ToArray();

    private static uint[] RootValues() => new uint[] { 0, 0x200, 0x400, 0x800, 0x7000, 0x12345600, 0xfffffe00, 0x80000000 }
        .Concat(Enumerable.Range(9, 23).Select(bit => 1u << bit)).Distinct().Order().ToArray();
}
