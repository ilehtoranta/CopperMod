using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticBoundsTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void CheckBoundsValuesFlagsFormsAndRegisterAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-bounds");
        foreach (var width in new[] { 2, 4 })
        foreach (var bound in ArithmeticSpecification.Boundaries(width))
        foreach (var value in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++) Chk(machine, report, width, new(0, 1), 0, bound, value, ccr);
        foreach (var width in new[] { 2, 4 })
        foreach (var form in ArithmeticSpecification.Sources(width, false))
        for (var reg = 0; reg < 8; reg++)
        foreach (var supervisor in new[] { false, true }) Chk(machine, report, width, form, reg, 7, 3, 31, supervisor);
        if (machine.Model.FullIndex)
        foreach (var index in IndexFixture.FullStructures())
        foreach (var width in new[] { 2, 4 })
        foreach (var form in new[] { new OperandForm(6, 7), new OperandForm(7, 3) }) Chk(machine, report, width, form, 7, 7, 8, 31, index: index);
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var lower in ArithmeticSpecification.Boundaries(width))
        foreach (var upper in ArithmeticSpecification.Boundaries(width))
        foreach (var value in ArithmeticSpecification.Boundaries(width))
        foreach (var address in new[] { false, true })
        foreach (var trap in new[] { false, true })
            Pair(machine, report, width, new(2, 0), 1, address, trap, lower, upper, value, 31);
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var form in ArithmeticSpecification.Sources(4).Where(f => f.Mode is 2 or 5 or 6 || f.Mode == 7 && f.Register < 4))
        for (var reg = 0; reg < 8; reg++)
        foreach (var address in new[] { false, true })
        foreach (var trap in new[] { false, true }) Pair(machine, report, width, form, reg, address, trap, 1, 8, 3, 31);
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var trap in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++) Pair(machine, report, width, new(2, 0), 1, false, trap, 1, 8, 8, ccr);
        if (machine.Model.FullIndex)
        foreach (var index in IndexFixture.FullStructures())
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var form in new[] { new OperandForm(6, 7), new OperandForm(7, 3) }) Pair(machine, report, width, form, 7, false, true, 1, 8, 3, 31, index);
        report.Complete(output);
    }

    private static void Chk(SyntheticMachine machine, CoverageBatch report, int width, OperandForm form, int reg, uint bound, uint value, int ccr, bool supervisor = true, IndexFixture? index = null)
    {
        machine.Reset(ccr, supervisor); machine.Core.State.D[reg] = ArithmeticSpecification.RegisterBits(value, width);
        var opcode = (ushort)((width == 2 ? 0x4180 : 0x4100) | reg << 9 | form.Mode << 3 | form.Register);
        var fixture = new OperandFixture(machine, [opcode], form, width, bound, index: index ?? new IndexFixture(IndexRegister: reg == 7 ? 6 : 7)); var e = fixture.Expected;
        if (width == 4 && !machine.Model.FullIndex)
        {
            machine.Core.State.A.CopyTo(e.A, 0); SyntheticExecution.ExpectException(machine, e, 4);
        }
        else
        {
            var number = ArithmeticSpecification.Signed(e.D[reg], width); var limit = ArithmeticSpecification.Signed(fixture.Value, width);
            // Only X and the documented trapping N result are defined.
            e.DefinedSrMask = (ushort)(number < 0 || number > limit ? 0xfff8 : 0xfff0);
            if (number < 0 || number > limit)
            {
                e.Sr = (ushort)((e.Sr & ~8) | (number < 0 ? 8 : 0));
                SyntheticExecution.ExpectException(machine, e, 6, e.Pc);
            }
        }
        SyntheticExecution.Run(machine, e, report, $"{machine.Model.Id}/CHK/{width}/{form.Id}/D{reg}/{index?.Id ?? "brief"}/op={opcode:X4}/bound={bound:X8}/value={value:X8}/ccr={ccr:X2}/super={supervisor}");
    }
    private static void Pair(SyntheticMachine machine, CoverageBatch report, int width, OperandForm form, int reg, bool address, bool trap, uint lower, uint upper, uint value, int ccr, IndexFixture? index = null)
    {
        machine.Reset(ccr);
        if (address) { if (reg == 7) machine.Core.State.SetActiveStackPointer(0x7800); else machine.Core.State.A[reg] = value; }
        else machine.Core.State.D[reg] = ArithmeticSpecification.RegisterBits(value, width);
        var opcode = (ushort)(0x00c0 | ArithmeticSpecification.SizeField(width) << 9 | form.Mode << 3 | form.Register);
        var extension = (ushort)((address ? 0x8000 : 0) | reg << 12 | (trap ? 0x800 : 0));
        var fixture = new OperandFixture(machine, [opcode, extension], form, width, lower, index: index); var e = fixture.Expected;
        for (var i = 0; i < width; i++)
        {
            var p = unchecked(fixture.Address + (uint)(width + i));
            if (machine.Model.Physical(p) >= SyntheticMachine.Code && machine.Model.Physical(p) < fixture.NextPc + 4) continue;
            machine.InitializePhysical(p, (upper >> (8 * (width - i - 1))) & 255, 1); e.Write(p, (upper >> (8 * (width - i - 1))) & 255, 1, machine.Model);
        }
        var unsupported = !machine.Model.FullIndex || machine.Model.Id == "68060";
        if (unsupported) SyntheticExecution.ExpectException(machine, e, machine.Model.Id == "68060" ? 61 : 4);
        else
        {
            var lo = ArithmeticSpecification.Signed(machine.PeekPhysical(fixture.Address, width), width);
            var hi = ArithmeticSpecification.Signed(machine.PeekPhysical(fixture.Address + (uint)width, width), width);
            var number = ArithmeticSpecification.Signed(address ? e.A[reg] : e.D[reg], address ? 4 : width);
            var outside = lo <= hi ? number < lo || number > hi : number > hi && number < lo;
            e.DefinedSrMask = 0xfff5;
            e.Sr = (ushort)((e.Sr & ~5) | (outside ? 1 : 0) | (number == lo || number == hi ? 4 : 0));
            if (trap && outside) SyntheticExecution.ExpectException(machine, e, 6, e.Pc);
        }
        SyntheticExecution.Run(machine, e, report, $"{machine.Model.Id}/{(trap ? "CHK2" : "CMP2")}/{width}/{form.Id}/{(address ? "A" : "D")}{reg}/{index?.Id ?? "brief"}/op={opcode:X4}/bounds={lower:X8},{upper:X8}/value={value:X8}/ccr={ccr:X2}");
    }
}
