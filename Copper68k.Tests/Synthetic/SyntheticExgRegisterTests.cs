using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// M68000PM EXG: full long-register exchange; CCR unaffected. External address
// width does not truncate architectural An values, including active A7 banks.
public sealed class SyntheticExgRegisterTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;
    private static readonly (uint Left, uint Right)[] Values =
    [
        (0x00c00276, 0x00000040), // Exact historical-regression input, A6/A2.
        (0x89abcdef, 0x12345678),
        (0, uint.MaxValue), (uint.MaxValue, 0),
        (0x7fffffff, 0x80000000), (0xffff0000, 0x0000ffff),
        (0x5555aaaa, 0xaaaa5555), (1, 1)
    ];

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void WideValuesAliasesAndActiveStackBanks(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "transfer-exg-wide");
        var banks = m.Model.FullIndex && modelId != "68060" ? new[] { "user", "ISP", "MSP" } : ["user", "ISP"];
        foreach (var bank in banks)
        foreach (var kind in new[] { 0x40, 0x48, 0x88 })
        {
            // Separate register enumeration and value/CCR boundaries avoid an
            // unnecessary Cartesian product while every binding sees wide data.
            for (var source = 0; source < 8; source++)
            for (var destination = 0; destination < 8; destination++)
            for (var pair = 0; pair < 2; pair++)
                Case(m, report, kind, source, destination, bank, pair, 0, "registers");
            foreach (var (source, destination) in new[] { (6, 2), (2, 2), (7, 3), (2, 7), (7, 7) })
            for (var pair = 0; pair < Values.Length; pair++)
            for (var ccr = 0; ccr < 32; ccr++)
                Case(m, report, kind, source, destination, bank, pair, ccr, "boundaries");
        }
        report.Complete(output);
    }

    private static ushort Encode(int kind, int source, int destination)
        => (ushort)(0xc100 | destination << 9 | kind | source);

    private static void Case(SyntheticMachine m, CoverageBatch report, int kind, int source, int destination,
        string bank, int pair, int ccr, string scenario)
    {
        m.Reset(ccr, bank != "user");
        if (bank == "MSP")
        {
            m.Core.State.StatusRegister |= 0x1000;
            m.Core.State.SetActiveStackPointer(0x7400);
        }
        // Alias cases have a single input register. The second assignment
        // establishes its initial value before the independent snapshot.
        if (kind == 0x40) m.Core.State.D[source] = Values[pair].Left;
        else m.Core.State.A[source] = Values[pair].Left;
        if (kind == 0x48) m.Core.State.A[destination] = Values[pair].Right;
        else m.Core.State.D[destination] = Values[pair].Right;
        var opcode = Encode(kind, source, destination);
        var e = SyntheticExecution.Prepare(m, [opcode]);
        if (kind == 0x40) (e.D[source], e.D[destination]) = (e.D[destination], e.D[source]);
        else if (kind == 0x48) (e.A[source], e.A[destination]) = (e.A[destination], e.A[source]);
        else (e.A[source], e.D[destination]) = (e.D[destination], e.A[source]);
        if (bank == "MSP") e.MasterStackPointer = e.A[7];
        e.ExpectedOperandTransfers = [];
        e.ControlChecks["no exception entry"] = (s => s.ExceptionSequence, m.Core.State.ExceptionSequence);
        var family = kind == 0x40 ? "DD" : kind == 0x48 ? "AA" : "AD";
        SyntheticExecution.Run(m, e, report,
            $"{m.Model.Id}/EXG.L/{family}/bank={bank}/r{source}->r{destination}/{scenario}/pair={pair}/op={opcode:X4}/ccr={ccr:X2}");
    }

    [Theory]
    [InlineData(0x48, 6, 2, 0xc54e)]
    [InlineData(0x40, 0, 0, 0xc140)]
    [InlineData(0x88, 7, 3, 0xc78f)]
    public void FixtureEncodingMatchesFixedExamples(int kind, int source, int destination, int expected)
        => Assert.Equal((ushort)expected, Encode(kind, source, destination));
}
