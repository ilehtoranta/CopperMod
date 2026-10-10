using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticBitFieldTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    internal static readonly string[] Families = ["BFTST", "BFEXTU", "BFCHG", "BFEXTS", "BFCLR", "BFFFO", "BFSET", "BFINS"];

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void BitFieldWidthsOffsetsFlagsAndRegisterAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-bitfield-values");
        for (var operation = 0; operation < 8; operation++)
        {
            for (var width = 1; width <= 32; width++)
            for (var offset = 0; offset < 32; offset++)
            foreach (var pattern in new[] { 0u, 0x81234567u })
            foreach (var form in new[] { new OperandForm(0, 0), new OperandForm(2, 0) })
                Case(machine, report, operation, form, offset, width, pattern, 31);
            foreach (var width in new[] { 1, 7, 8, 15, 16, 31, 32 })
            foreach (var offset in new[] { 0, 7, 8, 15, 31 })
            foreach (var pattern in new[] { 0u, 1u, 0x80000000u, 0xffffffffu, 0x81234567u })
            for (var ccr = 0; ccr < 32; ccr++)
            foreach (var form in new[] { new OperandForm(0, 0), new OperandForm(2, 0) })
                Case(machine, report, operation, form, offset, width, pattern, ccr);
            for (var target = 0; target < 8; target++)
            for (var operand = 0; operand < 8; operand++)
            for (var offsetReg = 0; offsetReg < 8; offsetReg++)
            for (var widthReg = 0; widthReg < 8; widthReg++)
                Case(machine, report, operation, new(0, target), -9, 37, 0x81234567, 31, operand, offsetReg, widthReg);
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void BitFieldAddressingSignedOffsetsAndFullExtensions(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "logical-bitfield-addressing");
        for (var operation = 0; operation < 8; operation++)
        {
            var modifying = operation is 2 or 4 or 6 or 7;
            foreach (var form in Forms(modifying))
            foreach (var supervisor in new[] { false, true })
            foreach (var offsetReg in new[] { -1, 2 })
            foreach (var widthReg in new[] { -1, 3 })
                Case(machine, report, operation, form, offsetReg < 0 ? 7 : -9, 32, 0x81234567, 31, 1, offsetReg, widthReg, supervisor: supervisor);
            foreach (var offset in new[] { int.MinValue, -33, -8, -1, 0, 7, 31, 32, int.MaxValue })
            foreach (var width in new[] { 1, 8, 31, 32, 64, -1 })
                Case(machine, report, operation, new(2, 7), offset, width, 0x81234567, 31, 1, 2, 3, supervisor: false);
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures())
            foreach (var form in modifying ? new[] { new OperandForm(6, 0) } : new[] { new OperandForm(6, 0), new OperandForm(7, 3) })
                Case(machine, report, operation, form, -9, 32, 0x81234567, 31, 1, 2, 3, index);
            foreach (var form in new[] { new OperandForm(7, 0), new OperandForm(7, 1) })
                Case(machine, report, operation, form, -1, 32, 0x81234567, 31, 1, 2, 3,
                    options: new(SourceAbsoluteWord: 0xff80, SourceAbsoluteLong: 0x10006000));
        }
        report.Complete(output);
    }

    internal static IEnumerable<OperandForm> Forms(bool modifying)
    {
        foreach (var mode in new[] { 0, 2, 5, 6 })
        for (var reg = 0; reg < 8; reg++) yield return new(mode, reg);
        for (var reg = 0; reg < (modifying ? 2 : 4); reg++) yield return new(7, reg);
    }

    internal static void Case(SyntheticMachine machine, CoverageBatch report, int operation, OperandForm form, int offset, int width,
        uint pattern, int ccr, int operand = 1, int offsetReg = -1, int widthReg = -1, IndexFixture? index = null,
        bool supervisor = true, AddressOptions? options = null)
    {
        machine.Reset(ccr, supervisor);
        machine.Core.State.D[operand] = 0x5a96c381;
        if (offsetReg >= 0) machine.Core.State.D[offsetReg] = unchecked((uint)offset);
        if (widthReg >= 0) machine.Core.State.D[widthReg] = unchecked((uint)width);
        var opcode = (ushort)(0xe8c0 | operation << 8 | form.Mode << 3 | form.Register);
        var extension = (ushort)(operand << 12 | (offsetReg >= 0 ? 0x800 | offsetReg << 6 : (offset & 31) << 6) |
            (widthReg >= 0 ? 0x20 | widthReg : width & 31));
        var fixture = new OperandFixture(machine, [opcode, extension], form, form.Mode == 0 ? 4 : 1, pattern, index: index, options: options);
        var expected = fixture.Expected;
        var actualOffset = offsetReg >= 0 ? unchecked((int)expected.D[offsetReg]) : offset & 31;
        var actualWidth = (int)(widthReg >= 0 ? expected.D[widthReg] & 31 : (uint)width & 31);
        if (actualWidth == 0) actualWidth = 32;
        var byteOffset = (long)Math.Floor(actualOffset / 8.0);
        var firstAddress = unchecked(fixture.Address + (uint)byteOffset);
        var firstBit = actualOffset - byteOffset * 8;
        if (form.Memory)
        {
            // Independent patterned operand, with guards on both sides of the affected bytes.
            for (var i = -4; i < 10; i++)
            {
                var address = machine.Model.Physical(unchecked(firstAddress + (uint)i));
                if (address >= SyntheticMachine.Code && address < fixture.NextPc + 4) continue;
                var value = i >= 0 && i < 5 ? (byte)(pattern >> (24 - (i % 4) * 8)) : (byte)0xa5;
                machine.Bus.Initialize(address, value, 1); expected.Memory[address] = value;
            }
        }
        if (!machine.Model.FullIndex)
        {
            expected.A[7] = machine.Core.State.A[7];
            SyntheticExecution.ExpectException(machine, expected, 4);
            for (var i = 0; i < 5; i++) expected.ForbiddenOperandReads.Add(machine.Model.Physical(unchecked(firstAddress + (uint)i)));
        }
        else
        {
            var originalRegister = expected.D[form.Register];
            uint field = 0;
            for (var i = 0; i < actualWidth; i++)
            {
                var bit = form.Mode == 0 ? (originalRegister >> (31 - ((actualOffset + i) & 31))) & 1 :
                    (uint)(machine.Bus.Peek(machine.Model.Physical(unchecked(firstAddress + (uint)((firstBit + i) / 8)))) >> (7 - (int)((firstBit + i) % 8))) & 1;
                field = (field << 1) | bit;
            }
            var mask = actualWidth == 32 ? uint.MaxValue : (1u << actualWidth) - 1;
            var inserted = expected.D[operand] & mask;
            var flagsValue = operation == 7 ? inserted : field;
            expected.Sr = (ushort)((expected.Sr & 0xfff0) | (flagsValue == 0 ? 4 : 0) | ((flagsValue & (1u << (actualWidth - 1))) != 0 ? 8 : 0));
            if (operation is 1 or 3) expected.D[operand] = operation == 3 && (field & (1u << (actualWidth - 1))) != 0 ? field | ~mask : field;
            if (operation == 5)
            {
                var first = 0;
                while (first < actualWidth && (field & (1u << (actualWidth - 1 - first))) == 0) first++;
                expected.D[operand] = unchecked((uint)(actualOffset + first));
            }
            if (operation is 2 or 4 or 6 or 7)
            {
                var result = operation switch { 2 => field ^ mask, 4 => 0u, 6 => mask, _ => inserted };
                for (var i = 0; i < actualWidth; i++)
                {
                    var set = (result & (1u << (actualWidth - 1 - i))) != 0;
                    if (form.Mode == 0)
                    {
                        var bit = 1u << (31 - ((actualOffset + i) & 31));
                        expected.D[form.Register] = set ? expected.D[form.Register] | bit : expected.D[form.Register] & ~bit;
                    }
                    else
                    {
                        var address = machine.Model.Physical(unchecked(firstAddress + (uint)((firstBit + i) / 8)));
                        var bit = (byte)(1 << (7 - (int)((firstBit + i) % 8)));
                        expected.Memory[address] = set ? (byte)(expected.Memory[address] | bit) : (byte)(expected.Memory[address] & ~bit);
                    }
                }
            }
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{Families[operation]}/{form.Id}/operand=D{operand}/offset={(offsetReg >= 0 ? $"D{offsetReg}" : "immediate")}/width={(widthReg >= 0 ? $"D{widthReg}" : "immediate")}/{index?.Id ?? "brief"}/op={opcode:X4}/ext={extension:X4}/offset={actualOffset}/width={actualWidth}/pattern={pattern:X8}/ccr={ccr:X2}/super={supervisor}");
    }
}
