using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// PRM 6-35/6-36, 6-70/6-71; MC68040UM 3.1.3: PTEST results are
// undefined with TC.E clear, and no table search occurs. This is decoder,
// privilege and trace qualification, not enabled-MMU qualification.
public sealed class SyntheticM68040MmuInstructionTests(ITestOutputHelper output)
{
    private static IEnumerable<ushort> Opcodes()
    {
        for (var reg = 0; reg < 8; reg++)
        {
            yield return (ushort)(0xf500 | reg); // PFLUSHN (An)
            yield return (ushort)(0xf508 | reg); // PFLUSH (An)
            yield return (ushort)(0xf548 | reg); // PTESTW (An)
            yield return (ushort)(0xf568 | reg); // PTESTR (An)
        }
        yield return 0xf510; // PFLUSHAN; canonical register field
        yield return 0xf518; // PFLUSHA; canonical register field
    }

    private static string Name(ushort opcode) => opcode switch
    {
        0xf510 => "PFLUSHAN", 0xf518 => "PFLUSHA",
        _ when (opcode & 0xfff8) == 0xf500 => "PFLUSHN",
        _ when (opcode & 0xfff8) == 0xf508 => "PFLUSH",
        _ when (opcode & 0xfff8) == 0xf548 => "PTESTW",
        _ => "PTESTR"
    };

    [Fact, Trait("Suite", "Synthetic")]
    public void SingleWordMmuInstructionsWithTranslationDisabled()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var report = new CoverageBatch("68040", "system-mmu-disabled");
        foreach (var opcode in Opcodes())
        foreach (var dfc in new uint[] { 1, 2, 5, 6 })
        foreach (var supervisor in new[] { false, true })
        foreach (var trace in new[] { 0, 0x4000, 0x8000, 0xc000 })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr, supervisor);
            m.Core.State.StatusRegister |= (ushort)trace;
            for (var reg = 0; reg < 7; reg++) m.Core.State.A[reg] = 0x80002003u + (uint)reg * 0x1000;
            var e = SyntheticExecution.Prepare(m, [opcode]);
            m.Core.State.DestinationFunctionCode = dfc;
            m.Core.State.M68040Mmu.UserRootPointer = 0x40004000;
            m.Core.State.M68040Mmu.SupervisorRootPointer = 0x40008000;
            e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, dfc);
            e.ControlChecks["TC"] = (s => s.M68040Mmu.TranslationControl, 0);
            e.ControlChecks["URP"] = (s => s.M68040Mmu.UserRootPointer, 0x40004000);
            e.ControlChecks["SRP"] = (s => s.M68040Mmu.SupervisorRootPointer, 0x40008000);
            // An is an address to probe/flush, never a memory operand to read.
            e.ForbiddenOperandReads.Add(e.A[opcode & 7]);
            if (!supervisor) SyntheticExecution.ExpectException(m, e, 8);
            else if (trace != 0) SyntheticExecution.ExpectException(m, e, 9, SyntheticMachine.Code + 2);
            SyntheticExecution.Run(m, e, report, $"68040/{Name(opcode)}/none/A{opcode & 7}/DFC={dfc}/super={supervisor}/trace={trace:X4}/ccr={ccr:X2}/op={opcode:X4}");
        }
        foreach (var opcode in Opcodes())
        {
            m.Reset(31);
            var e = SyntheticExecution.Prepare(m, [opcode, 0x747b]); // MOVEQ #123,D2 follows immediately
            e.Pc = SyntheticMachine.Code + 2;
            m.Core.State.DestinationFunctionCode = 5;
            var id = $"68040/{Name(opcode)}/none/A{opcode & 7}/following-MOVEQ/op={opcode:X4}";
            try
            {
                m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m);
                if (mismatch == null)
                {
                    m.Core.ExecuteInstruction();
                    e.Pc += 2; e.D[2] = 123; e.Sr &= 0xfff0;
                    mismatch = e.Verify(m);
                }
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
            { report.Record(id, "unsupported", ex.Message); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }
}
