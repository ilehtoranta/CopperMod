using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticCacheEncodingTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "system-cache-encodings");
        // M68000PM 6-4/9 and MC68060UM D-12: scope 00 is illegal,
        // cache 00 is a legal no-operation for the other scopes. Enumerate
        // every F4xx word, including ignored An fields in all-cache forms.
        for (var bits = 0; bits < 256; bits++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            machine.Reset(ccr, supervisor);
            var opcode = (ushort)(0xf400 | bits);
            var scope = (bits >> 3) & 3;
            var expected = SyntheticExecution.Prepare(machine, [opcode]);
            if (modelId is not ("68040" or "68060")) SyntheticExecution.ExpectException(machine, expected, 11);
            else if (scope == 0) SyntheticExecution.ExpectException(machine, expected, 4);
            else if (!supervisor) SyntheticExecution.ExpectException(machine, expected, 8);
            SyntheticExecution.Run(machine, expected, report,
                $"{modelId}/{((bits & 32) == 0 ? "CINV" : "CPUSH")}/scope={scope}/cache={bits >> 6}/A{bits & 7}/super={supervisor}/ccr={ccr:X2}/op={opcode:X4}");
        }
        report.Complete(output);
    }
}
