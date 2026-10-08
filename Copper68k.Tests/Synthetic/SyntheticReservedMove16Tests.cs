using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticReservedMove16Tests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Theory, MemberData(nameof(Models)), Trait("Suite", "Synthetic")]
    public void UnassignedMove16FirstWordsTakeLineFBeforeOperandEffects(string modelId)
    {
        // Five assigned first-word forms occupy F600..F627. F628..F63F
        // are unrecognized F-line words, not a valid MOVE16 with an invalid
        // extension. MC68040UM 9.6.1 / MC68060UM 8.2.4 require vector 11,
        // format zero and the causing instruction PC on the advanced models.
        // Earlier models also take their documented Line-F exception.
        var words = Enumerable.Range(0xf628, 24).Select(x => (ushort)x).ToArray();
        Assert.Equal(0xf628, words[0]);
        Assert.Equal(0xf63f, words[^1]);
        Assert.All(words, word => Assert.InRange((word >> 3) & 7, 5, 7));
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "system-move16-reserved-first-words");
        foreach (var word in words)
        foreach (var followingWord in new ushort[] { 0, 0x8000, 0xffff, 0x4e71 })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            InvalidOperandScenario.Run(machine, report, word,
                $"MOVE16/unassigned-first-word/following={followingWord:X4}",
                supervisor, ccr, followingWord, vector: 11);
        report.Complete(output);
    }
}
