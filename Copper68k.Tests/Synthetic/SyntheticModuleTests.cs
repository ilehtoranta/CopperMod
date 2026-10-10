using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticModuleTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void CallmDescriptorsFramesArgumentsAndAccessControl(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-callm");
        foreach (var form in ArithmeticSpecification.Sources(4).Where(f => f.Mode is 2 or 5 or 6 || f.Mode == 7 && f.Register < 4))
        for (var general = 0; general < 16; general++)
        foreach (var option in new[] { 0, 4 })
        foreach (var count in new[] { 0, 1, 2, 15, 16, 255 })
        foreach (var supervisor in new[] { false, true }) Call(m, report, form, general, option, 0, count, supervisor, 31);
        for (var ccr = 0; ccr < 32; ccr++) Call(m, report, new(2, 0), 9, 0, 0, 2, true, ccr);
        if (m.Model.FullIndex)
        foreach (var index in IndexFixture.FullStructures())
        foreach (var form in new[] { new OperandForm(6, 7), new OperandForm(7, 3) }) Call(m, report, form, 0, 0, 0, 2, true, 31, index: index);
        foreach (var option in new[] { 0, 4 })
        foreach (var supervisor in new[] { false, true })
        for (var status = 0; status < 9; status++) Call(m, report, new(2, 0), 15, option, 1, 15, supervisor, 31, status);
        foreach (var option in new[] { 1, 2, 3, 5, 6, 7 }) Call(m, report, new(2, 0), 0, option, 0, 0, true, 31);
        foreach (var type in new[] { 2, 15, 16, 31 }) Call(m, report, new(2, 0), 0, 0, type, 0, true, 31);
        foreach (var supervisor in new[] { false, true }) Call(m, report, new(2, 0), 0, 0, 1, 2, supervisor, 31);
        report.Complete(output);
    }
    private static bool Supported(SyntheticMachine m) => m.Model.Id is "68020" or "68EC020" or "A1200";
    private static void Call(SyntheticMachine m, CoverageBatch report, OperandForm form, int general, int option, int type, int count, bool supervisor, int ccr, int status = -1, IndexFixture? index = null)
    {
        m.Reset(ccr, supervisor);
        var argumentSp = m.Core.State.A[7];
        for (var i = 0; i < count; i++) m.InitializePhysical(argumentSp + (uint)i, (uint)(byte)(i * 11), 1);
        var op = (ushort)(0x06c0 | form.Mode << 3 | form.Register);
        var f = new OperandFixture(m, [op, (ushort)count], form, 4, (uint)option << 29 | (uint)type << 24, index: index); var e = f.Expected;
        void Initialize(uint address, uint value, int width)
        {
            for (var i = 0; i < width; i++)
            {
                var p = m.Model.Physical(address + (uint)i);
                if (p >= SyntheticMachine.Code && p < f.NextPc + 4) continue;
                var b = value >> (8 * (width - 1 - i)) & 255; m.InitializePhysical(address + (uint)i, b, 1); e.Write(address + (uint)i, b, 1, m.Model);
            }
        }
        Initialize(f.Address + 4, 0x6800, 4); Initialize(f.Address + 8, 0x7200, 4); Initialize(f.Address + 12, 0x7600, 4);
        var control = m.PeekPhysical(f.Address, 4); var entry = m.PeekPhysical(f.Address + 4, 4); var data = m.PeekPhysical(f.Address + 8, 4);
        Initialize(entry, (uint)general << 12, 2); Initialize(entry + 2, 0x4e71, 2); Initialize(entry + 4, 0x4e71, 2);
        var oldSp = e.A[7];
        m.Start();
        if (status >= 0) { m.Bus.ModuleResponder = true; m.Bus.ModuleRegisters[0x10000] = 0x37; m.Bus.ModuleRegisters[0x1000c] = (uint)status; }
        var actualOption = control >> 29; var actualType = control >> 24 & 31;
        var error = actualOption is not (0 or 4) || actualType > 1 || (control & 0xffff) != 0 || actualType == 1 && status is <= 0 or > 7;
        if (!Supported(m)) SyntheticExecution.ExpectException(m, e, 4);
        else if (error) SyntheticExecution.ExpectException(m, e, 14);
        else
        {
            var entryWord = m.PeekPhysical(entry, 2); var selected = (int)(entryWord >> 12); var savedData = selected < 8 ? e.D[selected] : e.A[selected - 8];
            var top = oldSp;
            if (actualType == 1 && status >= 4)
            {
                top = m.PeekPhysical(f.Address + 12, 4);
                if (actualOption == 0) { top -= (uint)count; for (var i = 0; i < count; i++) e.Write(top + (uint)i, m.PeekPhysical(oldSp + (uint)i, 1), 1, m.Model); }
            }
            var frame = top - 24;
            e.Write(frame, (control >> 16 & 0xff00) | (actualType == 1 ? 0x37u : 0), 2, m.Model);
            e.Write(frame + 2, (uint)ccr, 2, m.Model); e.Write(frame + 4, (uint)count, 2, m.Model); e.Write(frame + 6, 0, 2, m.Model);
            e.Write(frame + 8, f.Address, 4, m.Model); e.Write(frame + 12, f.NextPc, 4, m.Model); e.Write(frame + 16, savedData, 4, m.Model);
            e.Write(frame + 20, actualType == 1 || actualOption == 4 ? oldSp : 0, 4, m.Model);
            // Reserved frame word and unused saved-SP slot are architecturally undefined.
            for (var i = 0; i < 2; i++) e.MemoryMasks[m.Model.Physical(frame + 6 + (uint)i)] = 0;
            if (actualType == 0 && actualOption == 0)
                for (var i = 0; i < 4; i++) e.MemoryMasks[m.Model.Physical(frame + 20 + (uint)i)] = 0;
            if (selected < 8) e.D[selected] = data; else e.A[selected - 8] = data;
            e.A[7] = frame; e.Pc = entry + 2;
        }
        var id = $"{m.Model.Id}/CALLM/byte/{form.Id}/R{general}/opt={option}/type={type}/count={count}/super={supervisor}/status={status}/{index?.Id ?? "brief"}/op={op:X4}/ccr={ccr:X2}";
        SyntheticExecution.Run(m, e, report, id);
        if (Supported(m) && actualType == 1 && status >= 0)
        {
            var check = m.Bus.ModuleRegisters.GetValueOrDefault(0x10004u) == (control >> 16 & 255) && m.Bus.ModuleRegisters.GetValueOrDefault(supervisor ? 0x10054u : 0x10044u) == f.Address;
            report.Record(id + "/external-access-request", check ? "passing" : "mismatching", check ? null : "Descriptor/access-level request differs");
        }
    }
    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void RtmFramesRestoreAllGeneralRegistersConditionsAndArguments(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId)); var report = new CoverageBatch(modelId, "system-rtm");
        for (var general = 0; general < 16; general++)
        foreach (var option in new[] { 0, 4 })
        foreach (var type in new[] { 0, 1, 2 })
        foreach (var count in new[] { 0, 1, 15, 255 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr, supervisor); var sp = m.Core.State.A[7];
            m.InitializePhysical(sp, (uint)(option << 13 | type << 8 | 0x37), 2); m.InitializePhysical(sp + 2, (uint)(ccr ^ 31), 2);
            m.InitializePhysical(sp + 4, (uint)count, 2); m.InitializePhysical(sp + 6, 0, 2); m.InitializePhysical(sp + 8, 0x6200, 4);
            m.InitializePhysical(sp + 12, 0x6000, 4); m.InitializePhysical(sp + 16, 0x7200, 4); m.InitializePhysical(sp + 20, 0x7300, 4);
            m.InitializePhysical(0x6000, 0x4e71, 2); m.InitializePhysical(0x6002, 0x4e71, 2);
            var op = (ushort)(0x06c0 | general); var e = SyntheticExecution.Prepare(m, [op]);
            m.Bus.ModuleResponder = true; m.Bus.ModuleRegisters[0x1000c] = 1;
            if (!Supported(m)) SyntheticExecution.ExpectException(m, e, 4);
            else if (type > 1) SyntheticExecution.ExpectException(m, e, 14);
            else
            {
                if (general < 8) e.D[general] = 0x7200; else e.A[general - 8] = 0x7200;
                e.A[7] = (type == 1 || option == 4 ? 0x7300u : sp + 24) + (uint)count;
                e.Sr = (ushort)((e.Sr & 0xffe0) | (ccr ^ 31)); e.Pc = 0x6000;
            }
            SyntheticExecution.Run(m, e, report, $"{modelId}/RTM/none/R{general}/opt={option}/type={type}/count={count}/super={supervisor}/op={op:X4}/ccr={ccr:X2}");
        }
        foreach (var supervisor in new[] { false, true })
        foreach (var scenario in new[] { 1, 2, 3, 5, 6, 7, 100, 108, 200 })
        {
            m.Reset(31, supervisor); var sp = m.Core.State.A[7];
            var invalidOption = scenario < 8;
            var header = invalidOption ? scenario << 13 : 0x0137;
            m.InitializePhysical(sp, (uint)header, 2);
            m.InitializePhysical(sp + 2, 0, 2); m.InitializePhysical(sp + 4, 2, 2);
            m.InitializePhysical(sp + 12, 0x6000, 4); m.InitializePhysical(sp + 16, 0x7200, 4); m.InitializePhysical(sp + 20, 0x7300, 4);
            var e = SyntheticExecution.Prepare(m, [0x06c0]);
            m.Bus.ModuleResponder = scenario != 200; m.Bus.ModuleRegisters[0x1000c] = scenario == 108 ? 8u : 0;
            SyntheticExecution.ExpectException(m, e, Supported(m) ? 14 : 4);
            SyntheticExecution.Run(m, e, report, $"{modelId}/RTM/invalid-frame-or-denied/scenario={scenario}/super={supervisor}/op=06C0/ccr=1F");
        }
        report.Complete(output);
    }
}
