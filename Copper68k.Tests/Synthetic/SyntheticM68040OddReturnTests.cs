using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.2.2, 8.4, 8.4.3/6.7. The additional SR image ordering
// agrees with documentary WinUAE 5d22d336 (not an executed hardware oracle).
// Direct ISP/MSP frames only; chained user-frame SR provenance stays explicit.
public sealed class SyntheticM68040OddReturnTests(ITestOutputHelper output)
{
    private const uint Rte = 0x1000, PendingHandler = 0x9090, AddressHandler = 0x9190, Repaired = 0x6200;
    private static readonly string[] Banks = ["user", "ISP", "MSP"];

    [Fact, Trait("Suite", "Synthetic")]
    public void OddShortNormalAndMovemReturnsRaiseAddressErrorDuringRte() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void PendingExceptionsPrecedeTheOddReturnAddressError() => Audit(true);

    private void Audit(bool pending)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var report = new CoverageBatch("68040", pending ? "rte-odd-pending" : "rte-odd-normal");
        foreach (var form in pending ? new[] {"CT", "CU", "CP49", "CP50", "CP51", "CP52", "CP53", "CP54", "CP55"}
            : new[] {"format0", "format2", "format3", "normal", "CM"})
        foreach (var entry in new[] {"ISP", "MSP"})
        foreach (var result in Banks)
        foreach (var trace in new ushort[] {0, 0x8000, 0x4000})
        foreach (var target in new uint[] {0x6001, 0xff002003})
        foreach (var vbr in new uint[] {0, 0x10000})
        foreach (var alignment in new uint[] {0, 1})
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr);
            var frame = (entry == "ISP" ? 0x4700u : 0x7400u) + alignment;
            var sr = Status(result, trace, ccr ^ 31);
            var initialSr = Status(entry, 0, ccr);
            var format = form == "format0" ? 0 : form == "format2" ? 2 : form == "format3" ? 3 : 7;
            var vector = form == "CT" ? 9 : form == "CU" ? 11 : form.StartsWith("CP") ? int.Parse(form[2..]) : 0;
            var ssw = form == "CM" ? 0x1005 : form == "CT" ? 0x2005 : form == "CU" ? 0x4005 : vector >= 49 ? 0x8005 : 0x0105;
            for (var n = -20; n < 84; n++) m.InitializePhysical(unchecked(frame + (uint)n), (uint)(n ^ 0x5a), 1);
            m.InitializePhysical(frame, sr, 2); m.InitializePhysical(frame + 2, target, 4);
            m.InitializePhysical(frame + 6, (uint)(format << 12) | 8, 2);
            m.InitializePhysical(frame + 8, 0x4200, 4); m.InitializePhysical(frame + 12, (uint)ssw, 2);
            foreach (var offset in new[] {14u, 16u, 18u}) m.InitializePhysical(frame + offset, 0x85, 2);
            foreach (var offset in new[] {24u, 32u, 40u})
            {
                m.InitializePhysical(frame + offset, 0x3200 + offset, 4);
                m.InitializePhysical(frame + offset + 4, 0xc3a55a3c, 4);
                m.InitializePhysical(0x3200 + offset, 0x96abcdef, 4);
            }
            m.InitializePhysical(AddressHandler, 0x4e73, 2); m.InitializePhysical(AddressHandler + 2, 0x4e71, 2);
            m.InitializePhysical(PendingHandler, 0x4e73, 2); m.InitializePhysical(PendingHandler + 2, 0x4e71, 2);
            m.InitializePhysical(Repaired, 0x60fe, 2);
            m.InitializePhysical(vbr + 12, AddressHandler, 4);
            foreach (var v in new[] {9, 11, 49, 50, 51, 52, 53, 54, 55}) m.InitializePhysical(vbr + (uint)v * 4, PendingHandler, 4);
            _ = SyntheticExecution.Prepare(m, [0x4e73]);
            m.Core.State.SetMasterStackPointer(0x7400 + alignment);
            m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
            m.Core.State.StatusRegister = initialSr; m.Core.State.VectorBaseRegister = vbr;
            if (form == "CU") m.Core.State.M68040PendingFpuExceptions.Begin(2, vector, target);
            if (vector >= 49) m.Core.State.M68040PendingFpuExceptions.Begin(3, vector, target);
            var e = ArchitecturalExpectation.Capture(m);
            var pointers = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
            pointers[entry] += format == 7 ? 60u : format == 0 ? 8u : 12u;
            e.OperandAccessAddresses.UnionWith(new uint[] {0x3218, 0x3220, 0x3228, 0x4200});
            e.ExpectedOperandTransfers = [];
            e.ControlChecks["odd target never fetched"] = (_ => m.Bus.Accesses.Any(a => !a.Write && a.Address == target) ? 1u : 0u, 0);
            if (!pending) e.ControlChecks["no MOVEM continuation after odd target"] = (_ =>
                ((M68kAdvancedTimingInterpreter)m.Core).HasPendingM68040MovemContinuation ? 1u : 0u, 0);
            if (vector == 11 || vector >= 49) e.ControlChecks["pending delivery consumed"] = (s =>
                s.M68040PendingFpuExceptions.Find(vector == 11 ? 2 : 3) == null ? 0u : 1u, 0);
            var sequence = m.Core.State.ExceptionSequence;
            var id = $"68040/RTE/odd/{form}/entry={entry}/result={result}/T={trace:X4}/target={target:X8}/VBR={vbr:X8}/align={alignment}/op=4E73/ccr={ccr:X2}";
            if (pending) Exception(m, e, pointers, sr, sr, vector, target, 0x4200, PendingHandler, sequence + 1);
            else Exception(m, e, pointers, sr, initialSr, 3, Rte, target & ~1u, AddressHandler, sequence + 1);
            var phases = pending ? new[] {"pending-return", "repaired-return", "following-BRA"} : new[] {"repaired-return", "following-BRA"};
            if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/RTE"))
            { foreach (var phase in phases) report.Record(id + "/" + phase, "untested", "RTE prerequisite failed"); continue; }
            if (pending)
            {
                pointers[SupervisorBank(sr)] += 12;
                var handlerSr = (ushort)((sr | 0x2000) & ~0xc000);
                Exception(m, e, pointers, sr, handlerSr, 3, PendingHandler, target & ~1u, AddressHandler, sequence + 2);
                if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/pending-return"))
                { report.Record(id + "/repaired-return", "untested", "Pending return prerequisite failed"); report.Record(id + "/following-BRA", "untested", "Pending return prerequisite failed"); continue; }
            }
            // Address faults require software repair. Never retry the consumed RTE
            // or a partially completed operand. Return to a new, even instruction.
            var addressFrame = pointers[SupervisorBank(sr)];
            m.InitializePhysical(addressFrame, sr, 2); e.Write(addressFrame, sr, 2, m.Model);
            m.InitializePhysical(addressFrame + 2, Repaired, 4); e.Write(addressFrame + 2, Repaired, 4, m.Model);
            pointers[SupervisorBank(sr)] += 12;
            SetStacks(e, pointers, sr); e.Pc = Repaired; e.ExceptionVector = null;
            if (!SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/repaired-return"))
            { report.Record(id + "/following-BRA", "untested", "Repair return prerequisite failed"); continue; }
            if (trace != 0) Exception(m, e, pointers, sr, sr, 9, Repaired, Repaired, PendingHandler, sequence + (pending ? 3u : 2u));
            SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/following-BRA");
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void OddInstructionFetchUsesTheArchitecturalAddressErrorFrame()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var report = new CoverageBatch("68040", "address-error-fetch-040");
        foreach (var bank in Banks)
        foreach (var trace in new ushort[] {0, 0x8000, 0x4000})
        foreach (var target in new uint[] {0x6001, 0xff002003})
        foreach (var vbr in new uint[] {0, 0x10000})
        foreach (var alignment in new uint[] {0, 1})
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr);
            _ = SyntheticExecution.Prepare(m, [0x4e71]);
            m.InitializePhysical(vbr + 12, AddressHandler, 4);
            m.Core.State.SetMasterStackPointer(0x7400 + alignment);
            m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
            var sr = Status(bank, trace, ccr);
            m.Core.State.StatusRegister = sr; m.Core.State.ProgramCounter = target; m.Core.State.VectorBaseRegister = vbr;
            var pointers = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
            var e = ArchitecturalExpectation.Capture(m);
            e.ControlChecks["odd target never fetched"] = (_ => m.Bus.Accesses.Any(a => !a.Write && a.Address == target) ? 1u : 0u, 0);
            Exception(m, e, pointers, sr, sr, 3, target, target & ~1u, AddressHandler, m.Core.State.ExceptionSequence + 1);
            SyntheticM68040AccessFrameAuditTests.Step(m, e, report,
                $"68040/fetch/odd/bank={bank}/T={trace:X4}/target={target:X8}/VBR={vbr:X8}/align={alignment}/op=0000/ccr={ccr:X2}");
        }
        report.Complete(output);
    }

    private static ushort Status(string bank, ushort trace, int ccr) =>
        (ushort)((bank == "ISP" ? 0x2000 : bank == "MSP" ? 0x3000 : 0) | trace | ccr);
    private static string SupervisorBank(ushort sr) => (sr & 0x1000) != 0 ? "MSP" : "ISP";
    private static void SetStacks(ArchitecturalExpectation e, Dictionary<string, uint> pointers, ushort sr)
    {
        e.Sr = sr; e.A[7] = pointers[(sr & 0x2000) == 0 ? "user" : SupervisorBank(sr)];
        e.InactiveStackPointer = pointers[(sr & 0x2000) == 0 ? "ISP" : "user"]; e.MasterStackPointer = pointers["MSP"];
        e.ControlChecks["USP"] = (s => s.UserStackPointer, pointers["user"]);
        e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, pointers["ISP"]);
        e.ControlChecks["MSP"] = (s => s.MasterStackPointer, pointers["MSP"]);
    }
    private static void Exception(SyntheticMachine m, ArchitecturalExpectation e, Dictionary<string, uint> pointers,
        ushort restoredSr, ushort savedSr, int vector, uint pc, uint address, uint handler, uint sequence)
    {
        var bank = SupervisorBank(restoredSr); pointers[bank] -= 12;
        SetStacks(e, pointers, (ushort)((restoredSr | 0x2000) & ~0xc000));
        e.Pc = handler; e.ExceptionVector = vector;
        e.ControlChecks["exception entries"] = (s => s.ExceptionSequence, sequence);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, pc);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, savedSr);
        var stack = pointers[bank];
        e.Write(stack, savedSr, 2, m.Model); e.Write(stack + 2, pc, 4, m.Model);
        e.Write(stack + 6, (uint)((vector >= 49 ? 3 : 2) << 12 | vector * 4), 2, m.Model); e.Write(stack + 8, address, 4, m.Model);
    }
}
