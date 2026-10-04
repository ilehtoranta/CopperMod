using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticAtomicTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void CasBoundariesAddressingAndAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-cas");
        foreach (var width in new[] { 1, 2, 4 })
        {
            foreach (var memory in ArithmeticSpecification.Boundaries(width))
            foreach (var compare in ArithmeticSpecification.Boundaries(width))
            for (var ccr = 0; ccr < 32; ccr++)
                Cas(machine, report, width, new(2, 0), memory, compare, 1, 2, ccr);
            foreach (var form in ArithmeticSpecification.Alterable().Where(f => f.Memory))
            for (var compare = 0; compare < 8; compare++)
            for (var update = 0; update < 8; update++)
            foreach (var equal in new[] { false, true })
            foreach (var supervisor in new[] { false, true })
                Cas(machine, report, width, form, equal ? 0x80u : 0x7fu, 0x80, compare, update, 31, supervisor: supervisor);
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures())
            foreach (var equal in new[] { false, true })
                Cas(machine, report, width, new(6, 0), equal ? 0x80u : 0x7fu, 0x80, 1, 2, 31, index: index);
            foreach (var form in new[] { new OperandForm(7, 0), new OperandForm(7, 1) })
                Cas(machine, report, width, form, 0x80, 0x80, 1, 2, 31,
                    options: new(SourceAbsoluteWord: 0xff80, SourceAbsoluteLong: 0x10006000));
            foreach (var address in new[] { 0x4001u, 0x4002u, 0x4003u })
                Cas(machine, report, width, new(2, 0), 0x80, 0x80, 1, 2, 31, oddAddress: address);
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void Cas2ComparisonsRegisterFieldsAndAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-cas2");
        foreach (var width in new[] { 2, 4 })
        {
            foreach (var first in ArithmeticSpecification.Boundaries(width))
            foreach (var second in ArithmeticSpecification.Boundaries(width))
            for (var ccr = 0; ccr < 32; ccr++)
            foreach (var outcome in new[] { 0, 1, 2 })
                Cas2(machine, report, width, 8, 9, 2, 3, 4, 5, first, second, outcome, ccr);
            for (var compare1 = 0; compare1 < 8; compare1++)
            for (var compare2 = 0; compare2 < 8; compare2++)
            for (var update1 = 0; update1 < 8; update1++)
            for (var update2 = 0; update2 < 8; update2++)
            foreach (var outcome in new[] { 0, 1, 2 })
                Cas2(machine, report, width, 8, 9, compare1, compare2, update1, update2, 0x8000, 0x7fff, outcome, 31);
            for (var address1 = 0; address1 < 16; address1++)
            for (var address2 = 0; address2 < 16; address2++)
            {
                if (address1 == address2) continue; // Overlapping memory updates are architecturally undefined.
                foreach (var outcome in new[] { 0, 1, 2 })
                foreach (var supervisor in new[] { false, true })
                    Cas2(machine, report, width, address1, address2, 2, 3, 4, 5, 0x8000, 0x7fff, outcome, 31, supervisor);
            }
        }
        report.Complete(output);
    }

    internal static void Cas(SyntheticMachine machine, CoverageBatch report, int width, OperandForm form, uint memory,
        uint compare, int compareReg, int updateReg, int ccr, bool supervisor = true, IndexFixture? index = null,
        AddressOptions? options = null, uint? oddAddress = null)
    {
        machine.Reset(ccr, supervisor);
        machine.Core.State.D[updateReg] = ArithmeticSpecification.RegisterBits(0x96c3815a, width);
        machine.Core.State.D[compareReg] = ArithmeticSpecification.RegisterBits(compare, width);
        if (oddAddress.HasValue) machine.Core.State.A[0] = oddAddress.Value;
        var opcode = (ushort)((width == 1 ? 0xac0 : width == 2 ? 0xcc0 : 0xec0) | form.Mode << 3 | form.Register);
        var fixture = new OperandFixture(machine, [opcode, (ushort)(updateReg << 6 | compareReg)], form, width, memory, index: index, options: options);
        var expected = fixture.Expected;
        var unavailable = !machine.Model.FullIndex || machine.Model.Id == "68060" && (fixture.Address & (uint)(width - 1)) != 0;
        if (unavailable)
        {
            // Unimplemented instructions are restarted by their software handler.
            machine.Core.State.A.CopyTo(expected.A, 0);
            SyntheticExecution.ExpectException(machine, expected, machine.Model.Id == "68060" ? 61 : 4);
            for (var i = 0; i < width; i++) expected.ForbiddenOperandReads.Add(machine.Model.Physical(unchecked(fixture.Address + (uint)i)));
        }
        else
        {
            var actualCompare = expected.D[compareReg] & MoveSpecification.Mask(width);
            expected.Sr = ArithmeticSpecification.Binary(fixture.Value, actualCompare, width, expected.Sr, true, compare: true).Sr;
            var equal = fixture.Value == actualCompare;
            if (equal) fixture.Write(expected.D[updateReg]);
            else expected.D[compareReg] = (expected.D[compareReg] & ~MoveSpecification.Mask(width)) | fixture.Value;
            expected.OperandAccessAddresses.Add(machine.Model.Physical(fixture.Address));
            expected.ExpectedOperandTransfers = [(false, width)];
            if (equal || machine.Model.Id is "68040" or "68060") expected.ExpectedOperandTransfers.Add((true, width));
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/CAS/{width}/{form.Id}/Dc{compareReg}/Du{updateReg}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/memory={memory:X8}/compare={compare:X8}/ccr={ccr:X2}/address={fixture.Address:X8}");
    }

    internal static void Cas2(SyntheticMachine machine, CoverageBatch report, int width, int address1Reg, int address2Reg,
        int compare1, int compare2, int update1, int update2, uint value1, uint value2, int outcome, int ccr, bool supervisor = true)
    {
        machine.Reset(ccr, supervisor);
        machine.Core.State.D[compare1] = ArithmeticSpecification.RegisterBits(value1, width);
        machine.Core.State.D[compare2] = ArithmeticSpecification.RegisterBits(value2, width);
        // Address selectors can alias compare/update registers. Choose all addresses independently before taking snapshots.
        if (address1Reg < 8) machine.Core.State.D[address1Reg] = 0x5200;
        if (address2Reg < 8) machine.Core.State.D[address2Reg] = 0x6200;
        var address1 = address1Reg < 8 ? machine.Core.State.D[address1Reg] : machine.Core.State.A[address1Reg - 8];
        var address2 = address2Reg < 8 ? machine.Core.State.D[address2Reg] : machine.Core.State.A[address2Reg - 8];
        var mask = MoveSpecification.Mask(width);
        var memory1 = (machine.Core.State.D[compare1] & mask) ^ (outcome == 1 ? 1u : 0);
        var memory2 = (machine.Core.State.D[compare2] & mask) ^ (outcome == 2 ? 1u : 0);
        for (var i = -8; i < width + 8; i++)
        {
            machine.InitializePhysical(unchecked(address1 + (uint)i), 0xa5, 1);
            machine.InitializePhysical(unchecked(address2 + (uint)i), 0xa5, 1);
        }
        machine.InitializePhysical(address1, memory1, width); machine.InitializePhysical(address2, memory2, width);
        var opcode = (ushort)(width == 2 ? 0xcfc : 0xefc);
        var expected = SyntheticExecution.Prepare(machine, [opcode, (ushort)(address1Reg << 12 | update1 << 6 | compare1), (ushort)(address2Reg << 12 | update2 << 6 | compare2)]);
        if (!machine.Model.FullIndex || machine.Model.Id == "68060")
        {
            SyntheticExecution.ExpectException(machine, expected, machine.Model.Id == "68060" ? 61 : 4);
            for (var i = 0; i < width; i++)
            {
                expected.ForbiddenOperandReads.Add(machine.Model.Physical(address1 + (uint)i));
                expected.ForbiddenOperandReads.Add(machine.Model.Physical(address2 + (uint)i));
            }
        }
        else
        {
            var firstEqual = memory1 == (expected.D[compare1] & mask);
            var secondEqual = memory2 == (expected.D[compare2] & mask);
            expected.Sr = ArithmeticSpecification.Binary(firstEqual ? memory2 : memory1, expected.D[firstEqual ? compare2 : compare1], width, expected.Sr, true, compare: true).Sr;
            if (firstEqual && secondEqual)
            {
                expected.Write(address1, expected.D[update1], width, machine.Model);
                expected.Write(address2, expected.D[update2], width, machine.Model);
            }
            else
            {
                expected.D[compare2] = (expected.D[compare2] & ~mask) | memory2;
                // M68000PM 4-68: Dc1 takes precedence when the compare register is aliased.
                expected.D[compare1] = (expected.D[compare1] & ~mask) | memory1;
            }
            expected.OperandAccessAddresses.UnionWith([machine.Model.Physical(address1), machine.Model.Physical(address2)]);
            expected.ExpectedOperandTransfers = [(false, width), (false, width)];
            if (firstEqual && secondEqual) expected.ExpectedOperandTransfers.AddRange([(true, width), (true, width)]);
            else if (machine.Model.Id == "68040") expected.ExpectedOperandTransfers.Add((true, width));
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/CAS2/{width}/R{address1Reg}:R{address2Reg}/Dc{compare1}:Dc{compare2}/Du{update1}:Du{update2}/super={supervisor}/op={opcode:X4}/value1={value1:X8}/value2={value2:X8}/outcome={outcome}/ccr={ccr:X2}");
    }
}
