using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticLineFEncodingTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void SaveRestoreFirstWordsSelectLineFOrPrivilegeBeforeOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-linef-state-encodings");
        // MC68020UM 7.2.3.3/4, 7.5.2.2/3; MC68030UM 10.2.3.3/4,
        // 10.5.2.2/3 (CpID 0 belongs to the internal MMU, not external CIRs).
        // M68000PM 6-13/16; MC68060UM D-15/18 and 8.3:
        // invalid first words are line-F, not privileged state transfers.
        var opcodes = (from cpId in Enumerable.Range(0, 8)
                       from restore in new[] { false, true }
                       from ea in Enumerable.Range(0, 64)
                       select (Opcode: (ushort)(0xf100 | cpId << 9 | (restore ? 0x40 : 0) | ea),
                           CpId: cpId, Restore: restore, Mode: ea >> 3, Register: ea & 7)).ToArray();
        Assert.Equal(1024, opcodes.Select(x => x.Opcode).Distinct().Count());
        Assert.Contains(opcodes, x => x.Opcode == 0xf110 && x.CpId == 0 && !x.Restore && x.Mode == 2);
        Assert.Contains(opcodes, x => x.Opcode == 0xf520 && x.CpId == 2 && !x.Restore && x.Mode == 4);
        Assert.Contains(opcodes, x => x.Opcode == 0xf300 && x.CpId == 1 && x.Mode == 0);
        Assert.Contains(opcodes, x => x.Opcode == 0xf37b && x.CpId == 1 && x.Restore && x.Mode == 7 && x.Register == 3);
        foreach (var item in opcodes)
        foreach (var supervisor in new[] { false, true })
        {
            // These bit patterns name integer MMU instructions on 040/060,
            // not external cpSAVE/cpRESTORE. Their separate MMU matrices own
            // PFLUSH (F500..F51F), and 040 PTEST (F548..F54F/F568..F56F).
            if (modelId is "68040" or "68060" && item.Opcode is >= 0xf500 and <= 0xf51f) continue;
            if (modelId == "68040" && (item.Opcode is >= 0xf548 and <= 0xf54f or >= 0xf568 and <= 0xf56f)) continue;
            var recognized = machine.Model.FullIndex &&
                (modelId == "68030" ? item.CpId != 0 :
                 modelId is "68040" or "68060" ? item.CpId == 1 : true);
            var legalEa = item.Mode is 2 or 5 or 6 ||
                item.Mode == (item.Restore ? 3 : 4) ||
                (item.Mode == 7 && item.Register <= (item.Restore ? 3 : 1));
            // This batch qualifies first-word and privilege exceptions. Legal
            // supervisor coprocessor/FPU state execution is a separate protocol,
            // not counted as passing exception coverage or silently simulated.
            if (supervisor && recognized && legalEa) continue;
            var vector = recognized && legalEa ? 8 : 11;
            var form = item.Mode == 7 && item.Register > 4 ? $"unassigned-EA(7,{item.Register})" : $"EA({item.Mode},{item.Register})";
            for (var ccr = 0; ccr < 32; ccr++)
                InvalidOperandScenario.Run(machine, report, item.Opcode,
                    $"LineF/{(item.Restore ? "cpRESTORE" : "cpSAVE")}/CpID={item.CpId}/{form}/vector={vector}",
                    supervisor, ccr, vector: vector);
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void UnassignedFpuOperandFieldsTakeLineFWithoutOperandEffects(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-linef-unassigned-fpu-ea");
        // M68000PM 2-1 and floating-point operand tables: mode 7 registers
        // 5..7 are unassigned. MC68060UM 8.2.4: an unrecognized F-line
        // word uses vector 11, format 0 and the causing instruction PC.
        // F27A/B/C are FTRAPcc, and F248..F24F are FDBcc; neither
        // aliases the unassigned F27D/E/F conditional operand fields.
        ushort[] words = [0xf23d, 0xf23e, 0xf23f, 0xf27d, 0xf27e, 0xf27f];
        Assert.Equal(0b111101, words[0] & 0x3f);
        foreach (var opcode in words)
        foreach (var command in new ushort[] { 0x4000, 0x8000, 0xc000, 0x4e71 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, opcode,
                $"LineF/FPU/unassigned-EA(7,{opcode & 7})/command={command:X4}",
                supervisor, ccr, command, vector: 11);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void M68040PcRelativeRestoreUsesExtensionBaseAndFullIndexPointerOrder()
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == "68040"));
        var report = new CoverageBatch("68040", "system-linef-pc-restore");
        var forms = new[] { (Form: new OperandForm(7, 2), Index: (IndexFixture?)null),
                            (Form: new OperandForm(7, 3), Index: (IndexFixture?)new()) }
            .Concat(IndexFixture.FullStructures().Select(index => (new OperandForm(7, 3), (IndexFixture?)index))).ToArray();
        Assert.Equal(68, forms.Length);
        foreach (var (form, index) in forms)
        foreach (var header in new ushort[] { 0, 0x4100, 0x1234 })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr);
            machine.Core.State.D[7] = 0x100;
            var words = new List<ushort> { (ushort)(0xf340 | form.Mode << 3 | form.Register) };
            var d = (uint[])machine.Core.State.D.Clone();
            var a = (uint[])machine.Core.State.A.Clone();
            var fixture = new AddressingFixture(machine, 4, words, d, a, 0,
                new AddressOptions(SourceIndex: index));
            var address = fixture.Resolve(form, source: true);
            // Null BD + suppressed index can read the instruction's own
            // extension as a frame header or an indirect pointer. Preserve
            // the encoding, and build expectations from the fixture bytes.
            for (var i = 0; i < words.Count; i++) machine.InitializePhysical(SyntheticMachine.Code + (uint)i * 2, words[i], 2);
            machine.InitializePhysical(fixture.NextPc, 0x4e71, 2);
            machine.InitializePhysical(fixture.NextPc + 2, 0x4e71, 2);
            if (fixture.SourcePointerTarget != null) address = fixture.SourcePointerTarget();
            var selfHeader = address == SyntheticMachine.Code + 2;
            if (selfHeader && header != 0x1234) continue; // This encoding cannot also contain a NULL/IDLE header.
            if (!selfHeader)
            {
                for (var offset = -4; offset < 8; offset++)
                    machine.InitializePhysical(unchecked(address + (uint)offset), 0xa5, 1);
                machine.InitializePhysical(address, (uint)header << 16, 4);
            }
            var expected = SyntheticExecution.Prepare(machine, words);
            // Independent constants: PRM 6-12/13 NULL clears the programmer's
            // model; IDLE preserves it. Invalid headers use format error 14.
            machine.Core.State.M68040Fpu.Fpcr = 0x00001000;
            machine.Core.State.M68040Fpu.Fpsr = 0x08000000;
            machine.Core.State.M68040Fpu.Fpiar = 0x12345678;
            expected.ControlChecks["FPCR"] = (s => s.M68040Fpu.Fpcr, header == 0 ? 0u : 0x00001000);
            expected.ControlChecks["FPSR"] = (s => s.M68040Fpu.Fpsr, header == 0 ? 0u : 0x08000000);
            expected.ControlChecks["FPIAR"] = (s => s.M68040Fpu.Fpiar, header == 0 ? 0u : 0x12345678);
            if (header == 0x1234) SyntheticExecution.ExpectException(machine, expected, 14);
            else
            {
                expected.ControlChecks["frame address"] = (s => s.M68040Fpu.LastStateFrameAddress, address);
                expected.ControlChecks["frame header"] = (s => s.M68040Fpu.LastStateFrameHeader, header);
                expected.ControlChecks["frame size"] = (s => s.M68040Fpu.LastStateFrameSize, 4);
            }
            SyntheticExecution.Run(machine, expected, report,
                $"68040/FRESTORE/{form.Id}/{index?.Id ?? "displacement"}/header={(selfHeader ? "instruction-extension" : header.ToString("X4"))}/op={words[0]:X4}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }
}
