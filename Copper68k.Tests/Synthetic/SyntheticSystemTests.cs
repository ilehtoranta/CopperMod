using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticSystemTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void TrapsPrivilegeStopsAndResetPreserveArchitecturalState(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-basic");
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var supervisor in new[] { false, true })
        {
            for (var trap = 0; trap < 16; trap++) Basic(machine, report, "TRAP", (ushort)(0x4e40 | trap), ccr, supervisor);
            // Type 110 is unassigned, but MC68030UM 8.1.5/6 still gives
            // user-mode integrated-MMU CpID 0 privilege exception priority.
            // F123 is instead a legal privileged cpSAVE on EC020/020.
            foreach (var (family, opcode) in new[] { ("NOP", 0x4e71), ("RESET", 0x4e70), ("ILLEGAL", 0x4afc), ("LineA", 0xa123), ("LineF", 0xf1c0), ("TRAPV", 0x4e76) })
                Basic(machine, report, family, (ushort)opcode, ccr, supervisor);
            foreach (var sr in new ushort[] { 0, 0x0700, 0x2000, 0x2700, 0x271f }) Basic(machine, report, "STOP", 0x4e72, ccr, supervisor, sr);
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void StatusTransfersAllAddressingFlagsAndPrivilegeStates(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-status");
        foreach (var family in new[] { "MOVEfromSR", "MOVEfromCCR", "MOVEtoSR", "MOVEtoCCR" })
        {
            var from = family.StartsWith("MOVEfrom");
            var forms = from ? ArithmeticSpecification.Alterable() : ArithmeticSpecification.Sources(2, false);
            foreach (var form in forms)
            foreach (var supervisor in new[] { false, true })
            for (var ccr = 0; ccr < 32; ccr++) Status(machine, report, family, form, ccr, supervisor, 0x0700);
            if (machine.Model.FullIndex)
            foreach (var index in IndexFixture.FullStructures())
            foreach (var form in from ? new[] { new OperandForm(6, 7) } : new[] { new OperandForm(6, 7), new OperandForm(7, 3) })
                Status(machine, report, family, form, 31, true, 0x200f, index);
            foreach (var sr in new uint[] { 0, 1, 0x1f, 0x2000, 0x2700, 0x071f, 0xffff })
                Status(machine, report, family, new(0, 7), 31, true, sr);
        }
        foreach (var family in new[] { "ORI", "ANDI", "EORI" })
        foreach (var fullSr in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var value in new ushort[] { 0, 1, 0x1f, 0x0700, 0x2000, 0x271f })
            ImmediateStatus(machine, report, family, fullSr, value, ccr, supervisor);
        for (var reg = 0; reg < 8; reg++)
        foreach (var toUsp in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            var opcode = (ushort)(0x4e60 | (toUsp ? 0 : 8) | reg);
            var expected = SyntheticExecution.Prepare(machine, [opcode]);
            if (!supervisor) SyntheticExecution.ExpectException(machine, expected, 8);
            else if (toUsp) expected.InactiveStackPointer = expected.A[reg];
            else expected.A[reg] = expected.InactiveStackPointer!.Value;
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/MOVEUSP/L/A{reg}/toUSP={toUsp}/super={supervisor}/op={opcode:X4}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void LinkUnlinkAndOrdinaryReturnsWithStackAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-stack");
        for (var reg = 0; reg < 8; reg++)
        foreach (var width in new[] { 2, 4 })
        foreach (var displacement in new[] { -32768, -16, 0, 16, 32766 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            var opcode = (ushort)((width == 2 ? 0x4e50 : 0x4808) | reg);
            var words = new List<ushort> { opcode };
            if (width == 4) words.Add((ushort)(displacement >> 16)); words.Add((ushort)displacement);
            var expected = SyntheticExecution.Prepare(machine, words);
            if (width == 4 && !machine.Model.FullIndex) SyntheticExecution.ExpectException(machine, expected, 4);
            else
            {
                var saved = expected.A[reg];
                expected.A[7] -= 4;
                // The prose describes pushing the original An; the shorthand SP/An
                // assignments do not specify alias sampling across processors.
                // Pinned MAME fixtures and WinUAE distinguish 040's early decrement.
                expected.Write(expected.A[7], reg == 7 && modelId == "68040" ? expected.A[7] : saved, 4, machine.Model);
                expected.A[reg] = expected.A[7]; expected.A[7] = unchecked(expected.A[7] + (uint)displacement);
            }
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/LINK/{width}/A{reg}/super={supervisor}/op={opcode:X4}/disp={displacement}/ccr={ccr:X2}");
        }
        for (var reg = 0; reg < 8; reg++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            var frame = machine.Core.State.A[reg]; machine.InitializePhysical(frame, 0x6540, 4);
            var opcode = (ushort)(0x4e58 | reg); var expected = SyntheticExecution.Prepare(machine, [opcode]);
            expected.A[7] = frame + 4; expected.A[reg] = 0x6540;
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/UNLK/none/A{reg}/super={supervisor}/op={opcode:X4}/ccr={ccr:X2}");
        }
        foreach (var family in new[] { "RTS", "RTR", "RTD" })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var displacement in family == "RTD" ? new[] { -32768, -16, 0, 16, 32766 } : new[] { 0 })
        {
            machine.Reset(ccr, supervisor);
            var sp = machine.Core.State.A[7];
            machine.InitializePhysical(sp, family == "RTR" ? (uint)(0xa700 | (ccr ^ 31)) : 0x6000, family == "RTR" ? 2 : 4);
            if (family == "RTR") machine.InitializePhysical(sp + 2, 0x6000, 4);
            machine.InitializePhysical(0x6000, 0x4e71, 2); machine.InitializePhysical(0x6002, 0x4e71, 2);
            var opcode = (ushort)(family == "RTS" ? 0x4e75 : family == "RTR" ? 0x4e77 : 0x4e74);
            var expected = SyntheticExecution.Prepare(machine, family == "RTD" ? [opcode, (ushort)displacement] : [opcode]);
            if (family == "RTD" && modelId == "68000") SyntheticExecution.ExpectException(machine, expected, 4);
            else
            {
                expected.Pc = 0x6000;
                expected.A[7] = unchecked(sp + (family == "RTR" ? 6u : 4u) + (uint)displacement);
                if (family == "RTR") expected.Sr = (ushort)((expected.Sr & 0xffe0) | (ccr ^ 31));
            }
            SyntheticExecution.Run(machine, expected, report, $"{modelId}/{family}/none/super={supervisor}/op={opcode:X4}/disp={displacement}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    internal static void Basic(SyntheticMachine machine, CoverageBatch report, string family, ushort opcode, int ccr, bool supervisor, ushort immediate = 0)
    {
        machine.Reset(ccr, supervisor);
        var expected = SyntheticExecution.Prepare(machine, family == "STOP" ? [opcode, immediate] : [opcode]);
        if (family is "STOP" or "RESET" && !supervisor) SyntheticExecution.ExpectException(machine, expected, 8);
        else if (family == "STOP") { ApplyStatus(machine, expected, immediate); expected.Stopped = true; }
        else if (family == "RESET") expected.ExpectedDeviceResets = 1;
        else if (family == "TRAP") SyntheticExecution.ExpectException(machine, expected, 32 + (opcode & 15), expected.Pc);
        else if (family == "TRAPV" && (ccr & 2) != 0) SyntheticExecution.ExpectException(machine, expected, 7, expected.Pc);
        else if (family is "ILLEGAL" or "LineA" or "LineF")
        {
            var vector = family == "ILLEGAL" ? 4 : family == "LineA" ? 10
                : machine.Model.Id == "68030" && !supervisor && (opcode & 0x0e00) == 0 ? 8 : 11;
            SyntheticExecution.ExpectException(machine, expected, vector);
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{family}/none/super={supervisor}/op={opcode:X4}/imm={immediate:X4}/ccr={ccr:X2}", !expected.Stopped);
    }

    internal static void Status(SyntheticMachine machine, CoverageBatch report, string family, OperandForm form, int ccr, bool supervisor, uint value, IndexFixture? index = null)
    {
        machine.Reset(ccr, supervisor);
        var from = family.StartsWith("MOVEfrom");
        var opcode = (ushort)((family switch { "MOVEfromSR" => 0x40c0, "MOVEfromCCR" => 0x42c0, "MOVEtoCCR" => 0x44c0, _ => 0x46c0 }) | form.Mode << 3 | form.Register);
        var fixture = new OperandFixture(machine, [opcode], form, 2, value, source: !from, index: index);
        var expected = fixture.Expected;
        var illegal = family == "MOVEfromCCR" && machine.Model.Id == "68000";
        var privileged = family == "MOVEtoSR" || family == "MOVEfromSR" && machine.Model.Id != "68000";
        if (illegal || privileged && !supervisor)
        {
            machine.Core.State.A.CopyTo(expected.A, 0);
            SyntheticExecution.ExpectException(machine, expected, illegal ? 4 : 8);
            if (form.Memory) for (var i = 0; i < 2; i++) expected.ForbiddenOperandReads.Add(machine.Model.Physical(fixture.Address + (uint)i));
        }
        else if (from) fixture.Write(family == "MOVEfromSR" ? expected.Sr : (uint)ccr);
        else if (family == "MOVEtoCCR") expected.Sr = (ushort)((expected.Sr & 0xffe0) | (int)(fixture.Value & 31));
        else ApplyStatus(machine, expected, (ushort)fixture.Value);
        // Trace bits are covered by the explicit trace group; this register mask probe stops at the transfer boundary.
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{family}/W/{form.Id}/{index?.Id ?? "brief"}/super={supervisor}/op={opcode:X4}/value={value:X4}/ccr={ccr:X2}", (expected.Sr & 0xc000) == 0);
    }

    internal static void ImmediateStatus(SyntheticMachine machine, CoverageBatch report, string family, bool fullSr, ushort immediate, int ccr, bool supervisor)
    {
        machine.Reset(ccr, supervisor);
        var opcode = (ushort)((family == "ORI" ? 0 : family == "ANDI" ? 0x200 : 0xa00) | (fullSr ? 0x7c : 0x3c));
        var expected = SyntheticExecution.Prepare(machine, [opcode, immediate]);
        if (fullSr && !supervisor) SyntheticExecution.ExpectException(machine, expected, 8);
        else
        {
            var result = (ushort)(family == "ORI" ? expected.Sr | immediate : family == "ANDI" ? expected.Sr & immediate : expected.Sr ^ immediate);
            if (fullSr) ApplyStatus(machine, expected, result); else expected.Sr = (ushort)((expected.Sr & 0xffe0) | (result & 31));
        }
        SyntheticExecution.Run(machine, expected, report, $"{machine.Model.Id}/{family}to{(fullSr ? "SR" : "CCR")}/W/immediate/super={supervisor}/op={opcode:X4}/imm={immediate:X4}/ccr={ccr:X2}");
    }

    internal static void ApplyStatus(SyntheticMachine machine, ArchitecturalExpectation expected, ushort value)
    {
        var mask = machine.Model.FullIndex ? machine.Model.Id == "68060" ? 0xb71f : 0xf71f : 0xa71f;
        var previous = expected.Sr;
        value &= (ushort)mask;
        var previousSupervisor = (previous & 0x2000) != 0;
        var nextSupervisor = (value & 0x2000) != 0;
        if (previousSupervisor != nextSupervisor)
        {
            var old = expected.A[7]; expected.A[7] = expected.InactiveStackPointer!.Value; expected.InactiveStackPointer = old;
        }
        if (machine.Model.FullIndex && machine.Model.Id != "68060" && nextSupervisor && (value & 0x1000) != 0)
            expected.A[7] = expected.MasterStackPointer!.Value;
        expected.Sr = value;
    }
}
