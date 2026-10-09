using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticDebugInstructionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void DebugInstructionAliasesPrivilegeTraceAndHaltRecovery(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-debug-instructions");
        // MC68060UM 9.2.2 / 9-30: HALT is privileged; interrupts cannot
        // restart it. PULSE is user-accessible and preserves integer state.
        // Debug-port restart, pipeline toggling and PST pins are not exposed.
        foreach (var opcode in new ushort[] { 0x4ac8, 0x4acc })
        foreach (var supervisor in new[] { false, true })
        foreach (var trace in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            if (trace) machine.Core.State.StatusRegister |= 0x8000;
            var expected = SyntheticExecution.Prepare(machine, [opcode]);
            var halt = modelId == "68060" && opcode == 0x4ac8 && supervisor;
            if (modelId != "68060") SyntheticExecution.ExpectException(machine, expected, 4);
            else if (opcode == 0x4ac8 && !supervisor) SyntheticExecution.ExpectException(machine, expected, 8);
            else if (halt) expected.Halted = true;
            else if (trace) SyntheticExecution.ExpectException(machine, expected, 9, SyntheticMachine.Code + 2);
            var id = $"{modelId}/{(opcode == 0x4ac8 ? "HALT" : "PULSE")}/super={supervisor}/trace={trace}/ccr={ccr:X2}";
            SyntheticExecution.Run(machine, expected, report, id + $"/op={opcode:X4}", !halt);
            if (!halt) continue;
            if (!machine.Core.State.Halted || machine.Core.State.ProgramCounter != expected.Pc)
            {
                foreach (var edge in new[] { "idle", "interrupt", "host-entry", "reset" })
                    report.Record(id + $"/edge={edge}/op={opcode:X4}", "untested", "HALT prerequisite did not execute correctly");
                continue;
            }
            // Edge checks are additional named cases, not assertions counted as
            // independent instruction combinations. Reset is the available
            // public recovery operation; architectural debug restart is absent.
            foreach (var edge in new[] { "idle", "interrupt", "host-entry", "reset" })
            {
                try
                {
                    var accessCount = machine.Bus.Accesses.Count;
                    if (edge == "idle") machine.Core.ExecuteInstruction();
                    if (edge == "interrupt")
                    { machine.Core.RequestInterrupt(1, 100); machine.Core.RequestInterrupt(7, 124); }
                    if (edge == "host-entry") machine.Core.BeginSubroutine(0x1100, 0x7800, 0x1200);
                    string? mismatch;
                    if (edge == "reset")
                    {
                        machine.Reset(ccr, supervisor);
                        var recovered = SyntheticExecution.Prepare(machine, [0x4e71]);
                        machine.Core.ExecuteInstruction(); mismatch = recovered.Verify(machine);
                    }
                    else mismatch = expected.Verify(machine) ??
                        (machine.Bus.Accesses.Count != accessCount ? "Halted CPU performed bus accesses" : null);
                    report.Record(id + $"/edge={edge}/op={opcode:X4}", mismatch == null ? "passing" : "mismatching", mismatch);
                }
                catch (Exception ex) { report.Record(id + $"/edge={edge}/op={opcode:X4}", "mismatching", ex.ToString()); }
            }
        }
        report.Complete(output);
    }
}
