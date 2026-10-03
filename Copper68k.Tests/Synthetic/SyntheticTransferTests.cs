using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticTransferTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void RegisterTransferFamilies(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-registers");
        for (var reg = 0; reg < 8; reg++)
        {
            for (var imm = 0; imm < 256; imm++)
            foreach (var ccr in imm is 0 or 1 or 127 or 128 or 255 ? Enumerable.Range(0, 32) : new[] { 31 })
            {
                machine.Reset(ccr);
                var opcode = (ushort)(0x7000 | (reg << 9) | imm);
                var expected = SyntheticExecution.Prepare(machine, [opcode]);
                expected.D[reg] = unchecked((uint)(int)(sbyte)imm);
                expected.Sr = SyntheticExecution.MoveFlags(expected.Sr, expected.D[reg], 4);
                SyntheticExecution.Run(machine, expected, report, $"{modelId}/MOVEQ/L/imm8->D{reg}/all-encodings/op={opcode:X4}");
            }
            foreach (var value in new[] { 0u, 1u, 0x7fu, 0x80u, 0x7fffu, 0x8000u, 0x89abcdefu, uint.MaxValue })
            for (var ccr = 0; ccr < 32; ccr++)
            foreach (var family in new[] { "EXT.W", "EXT.L", "EXTB.L", "SWAP" })
            {
                machine.Reset(ccr);
                machine.Core.State.D[reg] = value;
                var opcode = (ushort)((family switch { "EXT.W" => 0x4880, "EXT.L" => 0x48c0, "EXTB.L" => 0x49c0, _ => 0x4840 }) | reg);
                var expected = SyntheticExecution.Prepare(machine, [opcode]);
                if (family == "EXTB.L" && !machine.Model.FullIndex) SyntheticExecution.ExpectException(machine, expected, 4);
                else
                {
                    var result = family switch
                    {
                        "EXT.W" => (value & 0xffff0000) | (ushort)(short)(sbyte)value,
                        "EXT.L" => unchecked((uint)(int)(short)value),
                        "EXTB.L" => unchecked((uint)(int)(sbyte)value),
                        _ => (value << 16) | (value >> 16)
                    };
                    expected.D[reg] = result;
                    expected.Sr = SyntheticExecution.MoveFlags(expected.Sr, result, family == "EXT.W" ? 2 : 4);
                }
                SyntheticExecution.Run(machine, expected, report, $"{modelId}/{family}/D{reg}/boundary-ccr/op={opcode:X4}/v={value:X8}/ccr={ccr:X2}");
            }
        }
        foreach (var kind in new[] { 0x40, 0x48, 0x88 })
        for (var source = 0; source < 8; source++)
        for (var destination = 0; destination < 8; destination++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr);
            var opcode = (ushort)(0xc100 | (destination << 9) | kind | source);
            var expected = SyntheticExecution.Prepare(machine, [opcode]);
            if (kind == 0x40) (expected.D[source], expected.D[destination]) = (expected.D[destination], expected.D[source]);
            else if (kind == 0x48) (expected.A[source], expected.A[destination]) = (expected.A[destination], expected.A[source]);
            else (expected.A[source], expected.D[destination]) = (expected.D[destination], expected.A[source]);
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/EXG/L/kind={kind:X2}/r{source}->r{destination}/preserve-flags/op={opcode:X4}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void LeaAndPeaUseSharedAddressFixtures(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-addresses");
        var forms = new List<OperandForm>();
        foreach (var mode in new[] { 2, 5, 6 }) for (var reg = 0; reg < 8; reg++) forms.Add(new(mode, reg));
        for (var reg = 0; reg < 4; reg++) forms.Add(new(7, reg));
        foreach (var form in forms)
        foreach (var destination in Enumerable.Range(0, 9)) // 0..7 LEA, 8 PEA
        for (var ccr = 0; ccr < 32; ccr++)
            AddressCase(machine, report, form, destination, ccr, null);
        if (machine.Model.FullIndex)
        foreach (var spec in IndexFixture.FullStructures())
        foreach (var form in new[] { new OperandForm(6, 0), new OperandForm(7, 3) })
        foreach (var destination in Enumerable.Range(0, 9))
            AddressCase(machine, report, form, destination, 31, spec);
        report.Complete(output);
    }

    private static void AddressCase(SyntheticMachine machine, CoverageBatch report, OperandForm form, int destination, int ccr, IndexFixture? index)
    {
        machine.Reset(ccr);
        var opcode = (ushort)((destination == 8 ? 0x4840 : 0x41c0 | destination << 9) | form.Mode << 3 | form.Register);
        var words = new List<ushort> { opcode };
        var d = (uint[])machine.Core.State.D.Clone(); var a = (uint[])machine.Core.State.A.Clone();
        var fixture = new AddressingFixture(machine, 4, words, d, a, 0, new(SourceIndex: index));
        var address = fixture.Resolve(form, true);
        var expected = SyntheticExecution.Prepare(machine, words);
        address = fixture.SourcePointerTarget?.Invoke() ?? address;
        if (destination == 8) { expected.A[7] -= 4; expected.Write(expected.A[7], address, 4, machine.Model); }
        else expected.A[destination] = address;
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{(destination == 8 ? "PEA" : "LEA")}/L/{form.Id}->r{destination}/{index?.Id ?? "brief-ccr"}/op={opcode:X4}/ccr={ccr:X2}");
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MovepPreservesSpacedBytesAnd060RaisesDocumentedException(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-movep");
        foreach (var width in new[] { 2, 4 })
        foreach (var load in new[] { false, true })
        for (var dr = 0; dr < 8; dr++)
        for (var ar = 0; ar < 8; ar++)
        foreach (var value in new[] { 0u, 1u, 0x7fffu, 0x8000u, 0xffffu, 0x89abcdefu, uint.MaxValue })
        foreach (var displacement in new short[] { -32, 32 })
        foreach (var ccr in dr == 0 && ar == 1 && value == 0x89abcdef && displacement == 32 ? Enumerable.Range(0, 32) : new[] { 31 })
        {
            machine.Reset(ccr);
            if (!load) machine.Core.State.D[dr] = value;
            var opcode = (ushort)(0x0108 | dr << 9 | ar | (width == 4 ? 0x40 : 0) | (!load ? 0x80 : 0));
            var address = unchecked(machine.Core.State.A[ar] + (uint)displacement);
            for (var i = -2; i <= width * 2 + 2; i++) machine.Bus.Initialize(machine.Model.Physical(unchecked(address + (uint)i)), 0x5a, 1);
            if (load) for (var i = 0; i < width; i++) machine.Bus.Initialize(machine.Model.Physical(address + (uint)i * 2), value >> (8 * (width - i - 1)), 1);
            var expected = SyntheticExecution.Prepare(machine, [opcode, (ushort)displacement]);
            if (modelId == "68060") SyntheticExecution.ExpectException(machine, expected, 61);
            else if (load) expected.D[dr] = (value & MoveSpecification.Mask(width)) | (width == 2 ? expected.D[dr] & 0xffff0000 : 0);
            else for (var i = 0; i < width; i++) expected.Write(address + (uint)i * 2, value >> (8 * (width - i - 1)), 1, machine.Model);
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/MOVEP/{width}/{(load ? "load" : "store")}/D{dr},d16(A{ar})/d={displacement}/op={opcode:X4}/v={value:X8}");
        }
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MovemMasksOrderAndBaseAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-movem");
        var masks = new[] { 0, 0xffff, 0xaaaa, 0x5555, 0x8181 }.Concat(Enumerable.Range(0, 16).Select(bit => 1 << bit)).ToArray();
        foreach (var width in new[] { 2, 4 })
        foreach (var load in new[] { false, true })
        foreach (var mode in new[] { 2, load ? 3 : 4, 5, 6, 7 })
        foreach (var reg in Enumerable.Range(0, mode == 7 ? (load ? 4 : 2) : 8))
        foreach (var mask in masks)
            MovemCase(machine, report, new(mode, reg), width, load, (ushort)mask, null);
        if (machine.Model.FullIndex)
        foreach (var width in new[] { 2, 4 })
        foreach (var load in new[] { false, true })
        foreach (var form in load ? new[] { new OperandForm(6, 0), new OperandForm(7, 3) } : new[] { new OperandForm(6, 0) })
        foreach (var index in IndexFixture.FullStructures())
            MovemCase(machine, report, form, width, load, 0xffff, index);
        foreach (var width in new[] { 2, 4 })
        foreach (var load in new[] { false, true })
        foreach (var form in new[] { new OperandForm(2, 0), new OperandForm(load ? 3 : 4, 7) })
        for (var ccr = 0; ccr < 32; ccr++)
            MovemCase(machine, report, form, width, load, 0xffff, null, ccr);
        report.Complete(output);
    }

    private static void MovemCase(SyntheticMachine machine, CoverageBatch report, OperandForm form, int width, bool load, ushort mask, IndexFixture? index, int ccr = 31)
    {
        machine.Reset(ccr);
        var opcode = (ushort)(0x4880 | (load ? 0x400 : 0) | (width == 4 ? 0x40 : 0) | form.Mode << 3 | form.Register);
        var words = new List<ushort> { opcode, mask };
        var initialD = (uint[])machine.Core.State.D.Clone(); var initialA = (uint[])machine.Core.State.A.Clone();
        var fixture = new AddressingFixture(machine, width, words, initialD, initialA, 0, new(SourceIndex: index, DestinationIndex: index));
        var address = form.Mode is 3 or 4 ? initialA[form.Register] : fixture.Resolve(form, true);
        _ = SyntheticExecution.Prepare(machine, words);
        address = fixture.SourcePointerTarget?.Invoke() ?? address;
        var count = System.Numerics.BitOperations.PopCount((uint)mask);
        if (load)
        {
            for (var i = 0; i < count * width + 4; i++)
            {
                var target = machine.Model.Physical(address + (uint)i);
                if (target >= SyntheticMachine.Code && target < SyntheticMachine.Code + (uint)words.Count * 2 + 4) continue;
                machine.Bus.Initialize(target, (uint)((i % 4) switch { 0 => 0x89, 1 => 0xab, 2 => 0xcd, _ => 0xef }), 1);
            }
        }
        var expected = ArchitecturalExpectation.Capture(machine);
        expected.Pc = SyntheticMachine.Code + (uint)words.Count * 2;
        var cursor = address;
        for (var bit = 0; bit < 16; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            var register = form.Mode == 4 ? 15 - bit : bit;
            if (form.Mode == 4) cursor -= (uint)width;
            if (load)
            {
                var data = machine.PeekPhysical(cursor, width);
                if (width == 2) data = unchecked((uint)(int)(short)data);
                if (register < 8) expected.D[register] = data; else expected.A[register - 8] = data;
            }
            else
            {
                var data = register < 8 ? initialD[register] : initialA[register - 8];
                if (form.Mode == 4 && register == 8 + form.Register && machine.Model.FullIndex) data -= (uint)width;
                expected.Write(cursor, data, width, machine.Model);
            }
            if (form.Mode != 4) cursor += (uint)width;
        }
        if (form.Mode is 3 or 4) expected.A[form.Register] = cursor;
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/MOVEM/{width}/{(load ? "load" : "store")}/{form.Id}/mask={mask:X4}/{index?.Id ?? "canonical"}/op={opcode:X4}/ccr={ccr:X2}");
    }
}
