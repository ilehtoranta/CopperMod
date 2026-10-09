namespace Copper68k.Tests.Synthetic;

internal static class InvalidOperandScenario
{
    public static void Run(SyntheticMachine machine, CoverageBatch report, ushort opcode, string description,
        bool supervisor, int ccr, ushort extensionWord = 0x0011, int vector = 4)
    {
        machine.Reset(ccr, supervisor);
        // Fixture initialization is separate from CPU accesses. Canaries detect
        // a mistaken operand read/write in addition to register/CCR changes.
        var forbidden = new List<uint>();
        for (var reg = 0; reg < 8; reg++)
        for (var offset = -8; offset < 8; offset++)
        {
            var address = machine.Model.Physical(unchecked(machine.Core.State.A[reg] + (uint)offset));
            machine.Bus.Initialize(address, 0xa5, 1);
            forbidden.Add(address);
        }
        var expected = SyntheticExecution.Prepare(machine, [opcode, extensionWord, 0x81a5, 0x4e71]);
        expected.ForbiddenOperandReads.UnionWith(forbidden);
        SyntheticExecution.ExpectException(machine, expected, vector);
        SyntheticExecution.Run(machine, expected, report,
            $"{machine.Model.Id}/{description}/op={opcode:X4}/super={supervisor}/ccr={ccr:X2}");
    }
}
