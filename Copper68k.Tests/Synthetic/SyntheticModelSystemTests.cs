using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticModelSystemTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void MovesAllSizesRegistersMemoryFormsAndPrivilege(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-moves");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var form in ArithmeticSpecification.Alterable().Where(f => f.Memory))
        for (var g = 0; g < 16; g++)
        foreach (var store in new[] { false, true })
        foreach (var supervisor in new[] { false, true }) Moves(m, report, width, form, g, store, supervisor, 31, 0x80000080);
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var value in ArithmeticSpecification.Boundaries(width))
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var g in new[] { 1, 9 })
        foreach (var store in new[] { false, true }) Moves(m, report, width, new(2, 0), g, store, true, ccr, value);
        if (m.Model.FullIndex)
        foreach (var index in IndexFixture.FullStructures())
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var store in new[] { false, true }) Moves(m, report, width, new(6, 7), 0, store, true, 31, 0x80, index);
        report.Complete(output);
    }
    private static void Moves(SyntheticMachine m, CoverageBatch report, int width, OperandForm form, int general, bool store, bool supervisor, int ccr, uint value, IndexFixture? index = null)
    {
        // MOVES An,(An)+ / -(An) store value is architecturally undefined.
        if (store && general >= 8 && general - 8 == form.Register && form.Mode is 3 or 4) return;
        m.Reset(ccr, supervisor);
        if (store) { if (general < 8) m.Core.State.D[general] = value; else if (general == 15) m.Core.State.SetActiveStackPointer(value); else m.Core.State.A[general - 8] = value; }
        var opcode = (ushort)(0x0e00 | ArithmeticSpecification.SizeField(width) << 6 | form.Mode << 3 | form.Register);
        var fixture = new OperandFixture(m, [opcode, (ushort)(general << 12 | (store ? 0x800 : 0))], form, width, value, index: index ?? new(IndexRegister: general == 7 ? 6 : 7));
        var e = fixture.Expected;
        m.Core.State.SourceFunctionCode = 1; m.Core.State.DestinationFunctionCode = 5;
        e.ControlChecks["SFC"] = (s => s.SourceFunctionCode, 1); e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, 5);
        if (modelIdOf(m) == "68000" || !supervisor)
        {
            m.Core.State.A.CopyTo(e.A, 0); SyntheticExecution.ExpectException(m, e, modelIdOf(m) == "68000" ? 4 : 8);
            for (var i = 0; i < width; i++) e.ForbiddenOperandReads.Add(m.Model.Physical(fixture.Address + (uint)i));
        }
        else if (store) e.Write(fixture.Address, general < 8 ? e.D[general] : e.A[general - 8], width, m.Model);
        else if (general < 8) e.D[general] = (e.D[general] & ~MoveSpecification.Mask(width)) | fixture.Value;
        else e.A[general - 8] = unchecked((uint)ArithmeticSpecification.Signed(fixture.Value, width));
        SyntheticExecution.Run(m, e, report, $"{m.Model.Id}/MOVES/{width}/{form.Id}/R{general}/store={store}/super={supervisor}/{index?.Id ?? "brief"}/op={opcode:X4}/value={value:X8}/ccr={ccr:X2}");
    }
    private static string modelIdOf(SyntheticMachine m) => m.Model.Id;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void MovecControlInventoryMasksPrivilegeAndAllGeneralRegisters(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-movec");
        foreach (var control in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 0x800, 0x801, 0x802, 0x803, 0x804, 0x805, 0x806, 0x807, 0x808, 0xfff })
        for (var general = 0; general < 16; general++)
        foreach (var store in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        foreach (var ccr in new[] { 0, 31 })
        {
            m.Reset(ccr, supervisor);
            var value = control switch { 2 => modelId == "68060" ? 0xf880e000u : modelId == "68040" ? 0x80008000u : modelId == "68030" ? 0x3f1fu : 0xfu, 8 => 0xf0000000u, 0x808 => 0x83u, 0x802 => 0xfcu, _ => 0x7100u };
            if (store) { if (general < 8) m.Core.State.D[general] = value; else if (general == 15) m.Core.State.SetActiveStackPointer(0x7100); else m.Core.State.A[general - 8] = value; }
            var op = (ushort)(store ? 0x4e7b : 0x4e7a); var e = SyntheticExecution.Prepare(m, [op, (ushort)(general << 12 | control)]);
            var valid = modelId != "68000" && (control is 0 or 1 or 0x800 or 0x801 || m.Model.FullIndex && (control == 2 || control == 0x802 && modelId is not ("68040" or "68060") || control is 0x803 or 0x804 && modelId != "68060") || modelId is "68040" or "68060" && (control is >= 3 and <= 7 or 0x806 or 0x807 || control == 0x805 && modelId == "68040" || control is 8 or 0x808 && modelId == "68060"));
            if (modelId == "68000" || !supervisor || !valid) SyntheticExecution.ExpectException(m, e, modelId == "68000" || supervisor || modelId == "68060" && !valid ? 4 : 8);
            else
            {
                var read = ControlReader(control); var actualValue = read(m.Core.State);
                if (store)
                {
                    var source = general < 8 ? e.D[general] : e.A[general - 8];
                    // Both 040 and 060 root pointers reserve the low nine bits.
                    var masked = control switch { 0 or 1 => source & 7, 2 when modelId == "68060" => source & 0xf880e000, 2 when modelId == "68040" => source & 0x80008000, 2 => source & (modelId == "68030" ? 0x3313u : 3u), 3 when modelId == "68040" => source & 0xc000, 3 when modelId == "68060" => source & 0xfffe, >= 4 and <= 7 when modelId is "68040" or "68060" => source & 0xffffe364, 8 => source & 0xa0000000, 0x808 => 0x04300000 | source & 0x83, 0x806 or 0x807 when modelId is "68040" or "68060" => source & 0xfffffe00, _ => source };
                    e.ControlChecks[$"control {control:X3}"] = (read, masked);
                    if (control == 0x800) e.InactiveStackPointer = masked;
                    if (control == 0x803) e.MasterStackPointer = masked;
                    if (control == 0x804) e.A[7] = masked;
                }
                else if (general < 8) e.D[general] = actualValue;
                else e.A[general - 8] = actualValue;
            }
            SyntheticExecution.Run(m, e, report, $"{modelId}/MOVEC/L/R{general}/control={control:X3}/store={store}/super={supervisor}/op={op:X4}/ccr={ccr:X2}");
        }
        report.Complete(output);
    }
    private static Func<M68kCpuState, uint> ControlReader(int control) => control switch
    {
        0 => s => s.SourceFunctionCode, 1 => s => s.DestinationFunctionCode, 2 => s => s.CacheControlRegister,
        3 => s => s.M68040Mmu.TranslationControl, 4 => s => s.M68040Mmu.InstructionTransparentTranslation0,
        5 => s => s.M68040Mmu.InstructionTransparentTranslation1, 6 => s => s.M68040Mmu.DataTransparentTranslation0,
        7 => s => s.M68040Mmu.DataTransparentTranslation1, 8 => s => s.M68060BusControl,
        0x800 => s => s.UserStackPointer, 0x801 => s => s.VectorBaseRegister, 0x802 => s => s.CacheAddressRegister,
        0x803 => s => s.MasterStackPointer, 0x804 => s => s.InterruptStackPointer, 0x805 => s => s.M68040Mmu.Status,
        0x806 => s => s.M68040Mmu.UserRootPointer, 0x807 => s => s.M68040Mmu.SupervisorRootPointer,
        0x808 => s => s.M68060ProcessorConfiguration, _ => throw new InvalidOperationException()
    };

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void Move16TransfersAndBreakpoints(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-model");
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var supervisor in new[] { false, true })
        {
            for (var breakpoint = 0; breakpoint < 8; breakpoint++)
            {
                m.Reset(ccr, supervisor); var op = (ushort)(0x4848 | breakpoint); var e = SyntheticExecution.Prepare(m, [op]);
                SyntheticExecution.ExpectException(m, e, 4); SyntheticExecution.Run(m, e, report, $"{modelId}/BKPT/none/{breakpoint}/super={supervisor}/op={op:X4}/ccr={ccr:X2}");
            }
            // Cache semantics moved to the exhaustive F4xx matrix after
            // shared X-flag and extension-length mutation proof. Specialized
            // physical cache/prefetch regressions are retained separately.
        }
        for (var form = 0; form < 5; form++)
        for (var r = 0; r < 8; r++)
        foreach (var other in form == 4 ? Enumerable.Range(0, 8) : new[] { 0 })
        for (var offset = 0; offset < 16; offset++)
        foreach (var supervisor in new[] { false, true })
        {
            var fixtureOffset = modelId is "68040" or "68060" ? offset : offset & ~1;
            m.Reset(31, supervisor); if (r == 7) m.Core.State.SetActiveStackPointer(0x7000u + (uint)fixtureOffset); else m.Core.State.A[r] = 0x7000u + (uint)fixtureOffset;
            if (form == 4 && other != r) { if (other == 7) m.Core.State.SetActiveStackPointer(0x7100u + (uint)(14 - fixtureOffset)); else m.Core.State.A[other] = 0x7100u + (uint)(14 - fixtureOffset); }
            var source = form == 4 || (form & 1) == 0 ? m.Core.State.A[r] & ~15u : 0x7200;
            var destination = form == 4 ? m.Core.State.A[other] & ~15u : (form & 1) == 0 ? 0x7200 : m.Core.State.A[r] & ~15u;
            for (var i = -8; i < 24; i++) { m.InitializePhysical(source + (uint)i, (uint)(byte)(i * 7), 1); if (source != destination) m.InitializePhysical(destination + (uint)i, 0xa5, 1); }
            var op = (ushort)(0xf600 | form << 3 | r); var e = SyntheticExecution.Prepare(m, form == 4 ? [op, (ushort)(0x8000 | other << 12)] : [op, 0, 0x720f]);
            if (modelId is not ("68040" or "68060")) SyntheticExecution.ExpectException(m, e, 11);
            else
            {
                for (var i = 0; i < 16; i++) e.Write(destination + (uint)i, m.PeekPhysical(source + (uint)i, 1), 1, m.Model);
                if (form < 2 || form == 4) e.A[r] += 16;
                if (form == 4 && other != r) e.A[other] += 16;
            }
            SyntheticExecution.Run(m, e, report, $"{modelId}/MOVE16/line/form={form}/A{r},A{other}/offset={offset}/super={supervisor}/op={op:X4}");
        }
        report.Complete(output);
    }
}
