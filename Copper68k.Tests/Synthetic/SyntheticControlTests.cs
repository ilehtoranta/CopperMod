using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticControlTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void BranchConditionsDisplacementsAndSubroutineStacks(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "control-branches");
        for (var condition = 0; condition < 16; condition++)
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var supervisor in new[] { false, true })
        foreach (var width in machine.Model.FullIndex ? new[] { 1, 2, 4 } : new[] { 1, 2 })
        foreach (var displacement in width == 1 ? new[] { -128, -2, 2, 126 } : width == 2 ? new[] { -32768, -2, 2, 32766 } : new[] { -0x10000002, -2, 2, 0x10000002 })
            Branch(machine, report, condition, width, displacement, ccr, supervisor);
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void DecrementAndSetConditionsAllRegistersModesAndFlags(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "control-conditions");
        for (var condition = 0; condition < 16; condition++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            for (var reg = 0; reg < 8; reg++)
            foreach (var count in new[] { 0u, 1u, 0x7fffu, 0xffffu })
                Dbcc(machine, report, condition, reg, count, ccr, -16);
            foreach (var form in ArithmeticSpecification.Alterable())
                Scc(machine, report, condition, form, ccr, supervisor: false);
        }
        for (var condition = 0; condition < 16; condition++)
        {
            foreach (var displacement in new[] { -32768, -2, 2, 32766 })
                Dbcc(machine, report, condition, 7, 1, 31, displacement);
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures()) Scc(machine, report, condition, new(6, 7), 31, index);
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void ConditionalTrapsAllConditionsOperandsAndStackModes(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "control-trapcc");
        for (var condition = 0; condition < 16; condition++)
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var width in new[] { 0, 2, 4 })
        foreach (var supervisor in new[] { false, true })
        {
            machine.Reset(ccr, supervisor);
            var words = new List<ushort> { (ushort)(0x50f8 | condition << 8 | (width == 0 ? 4 : width == 2 ? 2 : 3)) };
            if (width > 0) words.Add(0x1234);
            if (width == 4) words.Add(0x5678);
            var expected = SyntheticExecution.Prepare(machine, words);
            if (!machine.Model.FullIndex) SyntheticExecution.ExpectException(machine, expected, 4);
            else if (ControlSpecification.Condition(condition, ccr)) SyntheticExecution.ExpectException(machine, expected, 7, expected.Pc);
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/TRAPcc/{width}/condition={condition}/super={supervisor}/op={words[0]:X4}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void JumpCallControlAddressingAndStackAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "control-jumps");
        foreach (var subroutine in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var form in SyntheticBitFieldTests.Forms(false).Where(f => f.Mode != 0))
            Jump(machine, report, subroutine, form, ccr, supervisor);
        if (machine.Model.FullIndex)
        foreach (var subroutine in new[] { false, true })
        foreach (var index in IndexFixture.FullStructures())
        foreach (var form in new[] { new OperandForm(6, 7), new OperandForm(7, 3) })
            Jump(machine, report, subroutine, form, 31, true, index);
        foreach (var subroutine in new[] { false, true })
        foreach (var form in new[] { new OperandForm(6, 7), new OperandForm(7, 3) })
            Jump(machine, report, subroutine, form, 31, false, new(AddressIndex: true, IndexRegister: 7, LongIndex: true, Scale: 2));
        foreach (var subroutine in new[] { false, true })
        foreach (var form in new[] { new OperandForm(7, 0), new OperandForm(7, 1) })
            Jump(machine, report, subroutine, form, 31, true,
                options: new(SourceAbsoluteWord: 0xff80, SourceAbsoluteLong: 0x10006000));
        report.Complete(output);
    }

    internal static void Branch(SyntheticMachine machine, CoverageBatch report, int condition, int width, int displacement, int ccr, bool supervisor)
    {
        machine.Reset(ccr, supervisor);
        var words = new List<ushort> { (ushort)(0x6000 | condition << 8 | (width == 1 ? (byte)displacement : width == 4 ? 255 : 0)) };
        if (width == 4) words.Add((ushort)(displacement >> 16));
        if (width > 1) words.Add((ushort)displacement);
        var nextPc = SyntheticMachine.Code + (uint)words.Count * 2;
        var target = unchecked(SyntheticMachine.Code + 2 + (uint)displacement);
        var taken = condition == 1 || ControlSpecification.Condition(condition, ccr);
        var sentinel = ControlSpecification.InstallTargetSentinel(machine, target, nextPc);
        var expected = SyntheticExecution.Prepare(machine, words);
        if (taken)
        {
            expected.Pc = target;
            if (condition == 1) { expected.A[7] -= 4; expected.Write(expected.A[7], nextPc, 4, machine.Model); }
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{(condition == 0 ? "BRA" : condition == 1 ? "BSR" : "Bcc")}/{width}/condition={condition}/super={supervisor}/op={words[0]:X4}/disp={displacement}/ccr={ccr:X2}", !taken || sentinel);
    }

    internal static void Dbcc(SyntheticMachine machine, CoverageBatch report, int condition, int reg, uint count, int ccr, int displacement)
    {
        machine.Reset(ccr); machine.Core.State.D[reg] = 0xa55a0000 | count;
        var target = unchecked(SyntheticMachine.Code + 2 + (uint)displacement);
        var sentinel = ControlSpecification.InstallTargetSentinel(machine, target, SyntheticMachine.Code + 4);
        var opcode = (ushort)(0x50c8 | condition << 8 | reg);
        var expected = SyntheticExecution.Prepare(machine, [opcode, (ushort)displacement]);
        var taken = false;
        if (!ControlSpecification.Condition(condition, ccr))
        {
            var result = (count - 1) & 65535;
            expected.D[reg] = (expected.D[reg] & 0xffff0000) | result;
            if (result != 65535) { expected.Pc = target; taken = true; }
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/DBcc/W/D{reg}/condition={condition}/op={opcode:X4}/count={count:X4}/disp={displacement}/ccr={ccr:X2}", !taken || sentinel);
    }

    internal static void Scc(SyntheticMachine machine, CoverageBatch report, int condition, OperandForm form, int ccr, IndexFixture? index = null, bool supervisor = true)
    {
        machine.Reset(ccr, supervisor);
        var opcode = (ushort)(0x50c0 | condition << 8 | form.Mode << 3 | form.Register);
        var fixture = new OperandFixture(machine, [opcode], form, 1, 0xa5, source: false, index: index);
        fixture.Write(ControlSpecification.Condition(condition, ccr) ? 255u : 0);
        SyntheticExecution.Run(machine, fixture.Expected, report, $"{machine.Model.Id}/Scc/B/{form.Id}/condition={condition}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/ccr={ccr:X2}");
    }

    internal static void Jump(SyntheticMachine machine, CoverageBatch report, bool subroutine, OperandForm form, int ccr, bool supervisor,
        IndexFixture? index = null, AddressOptions? options = null)
    {
        machine.Reset(ccr, supervisor);
        var opcode = (ushort)((subroutine ? 0x4e80 : 0x4ec0) | form.Mode << 3 | form.Register);
        var words = new List<ushort> { opcode };
        var d = (uint[])machine.Core.State.D.Clone(); var a = (uint[])machine.Core.State.A.Clone();
        var addressing = new AddressingFixture(machine, 2, words, d, a, 0, options ?? new(SourceIndex: index));
        var address = addressing.Resolve(form, true);
        var expected = SyntheticExecution.Prepare(machine, words);
        var nextPc = expected.Pc;
        // A null-displacement PC pointer may straddle the following opcode. Use
        // an even reference word so this legal jump fixture selects aligned code.
        machine.InitializePhysical(nextPc, 0x4e70, 2); machine.InitializePhysical(nextPc + 2, 0x4e70, 2);
        expected.Write(nextPc, 0x4e70, 2, machine.Model); expected.Write(nextPc + 2, 0x4e70, 2, machine.Model);
        address = addressing.SourcePointerTarget?.Invoke() ?? address;
        var sentinel = ControlSpecification.InstallTargetSentinel(machine, address, nextPc);
        if (sentinel) { expected.Write(address, 0x4e71, 2, machine.Model); expected.Write(address + 2, 0x4e71, 2, machine.Model); }
        machine.Start();
        if (subroutine) { expected.A[7] -= 4; expected.Write(expected.A[7], nextPc, 4, machine.Model); }
        expected.Pc = address;
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{(subroutine ? "JSR" : "JMP")}/none/{form.Id}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/ccr={ccr:X2}", sentinel);
    }
}
