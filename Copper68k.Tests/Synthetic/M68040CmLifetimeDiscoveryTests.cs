using System.Globalization;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Software discovery: pinned WinUAE's MMU RTE arms CM before its odd-PC
// callback. Its generated MOVEM consumes that global state after repair.
// The native adapter composes exception entry; neither that composition nor
// continuation lifetime is a full external/hardware architectural oracle.
public sealed class M68040CmLifetimeDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_CM_LIFETIME_DISCOVERY";
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];
    private const uint Handler = 0x9190;

    [EnvironmentFact(Enable, "compare CM lifetime across odd-PC RTE repair"), Trait("Suite", "ReferenceDiscovery")]
    public void MovemContinuationLifetimeScalar() => Audit(false);

    [EnvironmentFact(Enable, "compare CM lifetime across odd-PC RTE repair"), Trait("Suite", "ReferenceDiscovery")]
    public void MovemContinuationLifetimeBatch() => Audit(true);

    private void Audit(bool batch)
    {
        var group = $"rte-cm-lifetime-discovery-{(batch ? "batch" : "scalar")}";
        var report = new CoverageBatch("68040", group);
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var export = Environment.GetEnvironmentVariable("COPPER68K_040_CM_LIFETIME_EXPORT");
        using var rows = string.IsNullOrWhiteSpace(export) ? null : new StreamWriter(Path.Combine(export, group + ".rows"));
        using var frameRows = string.IsNullOrWhiteSpace(export) ? null : new StreamWriter(Path.Combine(export, group + ".frames"));
        uint index = 0;
        foreach (var start in new[] { "ISP", "MSP" })
        foreach (var result in Banks)
        foreach (var odd in new[] { false, true })
        foreach (var cm in new[] { false, true })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var ccr in Enumerable.Range(0, 32))
        {
            m.Reset(ccr);
            var pointers = new Dictionary<string, uint> { ["user"] = 0x7800 + alignment, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
            foreach (var p in pointers.Values)
                for (var n = -20; n < 112; n++) m.InitializePhysical(unchecked(p + (uint)n), (uint)(n ^ 0x5a), 1);
            var initialSr = M68040StackFixture.Status(start, 0, ccr);
            var resultSr = M68040StackFixture.Status(result, 0, ccr ^ 31);
            var frame = pointers[start];
            m.InitializePhysical(frame, resultSr, 2); m.InitializePhysical(frame + 2, odd ? 0x6001u : 0x6000u, 4);
            m.InitializePhysical(frame + 6, 0x7008, 2); m.InitializePhysical(frame + 8, 0x4200, 4);
            m.InitializePhysical(frame + 12, cm ? 0x1005u : 0x0105u, 2);
            Install(m, 0x6000, [0x4cfa, 3, 0x0ffc, 0x4e71]); // PC base 6004 + 4092 = 7000.
            Install(m, Handler, [0x3ebc, resultSr, 0x2f7c, 0, 0x6000, 2, 0x4e73]);
            m.InitializePhysical(vbr + 12, Handler, 4);
            m.InitializePhysical(0x4200, 0x89abcdef, 4); m.InitializePhysical(0x4204, 0x12345678, 4);
            m.InitializePhysical(0x7000, 0xdeadbeef, 4); m.InitializePhysical(0x7004, 0x789abcde, 4);
            _ = SyntheticExecution.Prepare(m, [0x4e73]);
            m.Core.State.SetUserStackPointer(pointers["user"]);
            m.Core.State.SetInterruptStackPointer(pointers["ISP"]);
            m.Core.State.SetMasterStackPointer(pointers["MSP"]);
            m.Core.State.StatusRegister = initialSr; m.Core.State.VectorBaseRegister = vbr;
            var e = ArchitecturalExpectation.Capture(m);
            var id = $"68040/RTE/cm-lifetime/{(cm ? "CM" : "normal")}/entry={start}/result={result}/odd={(odd ? 1 : 0)}/align={alignment}/VBR={vbr:X8}";
            var actual = new List<uint>();
            var valid = true; var executed = 0u;
            void Phase(string name, ushort opcode)
            {
                var phaseId = $"{id}/phase={name}/op={opcode:X4}/ccr={ccr:X2}";
                if (!valid) { report.Record(phaseId, "untested", "Previous phase mismatched the software discovery expectation"); return; }
                valid = M68040StackFixture.Step(m, e, report, phaseId, batch);
                actual.AddRange(Snapshot(m)); executed++;
            }
            pointers[start] += 60;
            var validation = new List<(uint, int)> { (frame, 2), (frame + 2, 4), (frame + 6, 2), (frame + 12, 2) };
            if (cm) validation.Add((frame + 8, 4));
            var validationAddresses = validation.Select(r => r.Item1).ToHashSet();
            e.ControlChecks["format7 validation order"] = (_ => m.Bus.Accesses.Where(a => !a.Write && validationAddresses.Contains(a.Address))
                .Select(a => (a.Address, a.Width)).SequenceEqual(validation) ? 0u : 1u, 0);
            if (odd)
            {
                var bank = M68040StackFixture.ExceptionBank(resultSr); pointers[bank] -= 12;
                var sp = pointers[bank];
                M68040StackFixture.SetStacks(e, pointers, (ushort)(resultSr | 0x2000));
                e.Pc = Handler; e.ExceptionVector = 3;
                e.ControlChecks["one address error"] = (s => s.ExceptionSequence, m.Core.State.ExceptionSequence + 1);
                e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, initialSr);
                e.ControlChecks["saved RTE PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
                e.Write(sp, initialSr, 2, m.Model); e.Write(sp + 2, SyntheticMachine.Code, 4, m.Model);
                e.Write(sp + 6, 0x200c, 2, m.Model); e.Write(sp + 8, 0x6000, 4, m.Model);
            }
            else { M68040StackFixture.SetStacks(e, pointers, resultSr); e.Pc = 0x6000; }
            Phase("initial-RTE", 0x4e73);
            if (frameRows != null)
            {
                var sp = m.Core.State.A[7];
                uint[] frameActual = odd ? [index, sp, m.PeekPhysical(sp, 2), m.PeekPhysical(sp + 2, 4), m.PeekPhysical(sp + 6, 2), m.PeekPhysical(sp + 8, 4)] : [index, 0, 0, 0, 0, 0];
                frameRows.WriteLine(string.Join(' ', frameActual.Select(Hex)));
            }
            e.ControlChecks.Remove("format7 validation order");
            if (odd)
            {
                var sp = e.A[7];
                e.Write(sp, resultSr, 2, m.Model); e.Pc = Handler + 4;
                e.Sr = (ushort)((e.Sr & 0xfff0) | (resultSr == 0 ? 4 : 0));
                Phase("repair-SR", 0x3ebc);
                e.Write(sp + 2, 0x6000, 4, m.Model); e.Pc = Handler + 12;
                e.Sr &= 0xfff0;
                Phase("repair-PC", 0x2f7c);
                pointers[M68040StackFixture.ExceptionBank(resultSr)] += 12;
                M68040StackFixture.SetStacks(e, pointers, resultSr); e.Pc = 0x6000;
                Phase("handler-RTE", 0x4e73);
            }
            e.Pc = 0x6006;
            e.D[0] = cm ? 0x89abcdefu : 0xdeadbeefu; e.D[1] = cm ? 0x12345678u : 0x789abcdeu;
            var source = cm ? 0x4200u : 0x7000u;
            var accessStart = m.Bus.Accesses.Count;
            e.ControlChecks["MOVEM operand address/order"] = (_ => m.Bus.Accesses.Skip(accessStart).Where(a => !a.Write && a.Address is 0x4200 or 0x4204 or 0x7000 or 0x7004)
                .Select(a => (a.Address, a.Width)).SequenceEqual(new[] { (source, 4), (source + 4, 4) }) ? 0u : 1u, 0);
            Phase("MOVEM", 0x4cfa);
            e.ControlChecks.Remove("MOVEM operand address/order");
            e.Pc = 0x6008; Phase("sentinel", 0x4e71);
            if (rows != null)
            {
                uint[] input = [index, start == "MSP" ? 1u : 0u, (uint)Array.IndexOf(Banks, result), odd ? 1u : 0u, cm ? 1u : 0u, alignment, vbr, (uint)ccr];
                rows.WriteLine(string.Join(' ', input.Select(Hex)) + " | " + Hex(executed) + " " + string.Join(' ', actual.Select(Hex)));
            }
            index++;
        }
        report.Complete(output);
    }

    private static void Install(SyntheticMachine m, uint address, ushort[] words)
    { for (var n = 0; n < words.Length; n++) m.InitializePhysical(address + (uint)n * 2, words[n], 2); }
    private static uint[] Snapshot(SyntheticMachine m)
    {
        var s = m.Core.State;
        return [s.ProgramCounter, s.StatusRegister, s.UserStackPointer, s.InterruptStackPointer, s.MasterStackPointer, .. s.D, .. s.A];
    }
    private static string Hex(uint value) => value.ToString("X", CultureInfo.InvariantCulture);
}
