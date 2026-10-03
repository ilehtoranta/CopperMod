using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

internal sealed class SyntheticDeepAuditAttribute : FactAttribute
{
    public SyntheticDeepAuditAttribute()
    {
        if (Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_DEEP") != "1")
            Skip = "Additional seeded audit requires scripts/test-copper68k-synthetic.ps1 -Deep";
    }
}

public sealed class SyntheticDeepAuditTests(ITestOutputHelper output)
{
    [SyntheticDeepAudit]
    [Trait("Suite", "SyntheticDeep")]
    public void RecordedSeedMoveSamples()
    {
        Assert.True(uint.TryParse(Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_SEED"), out var seed) && seed != 0, "A nonzero uint seed is required");
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_SAMPLES"), out var samples) && samples > 0, "An audit must select positive samples");
        var selected = (Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_MODELS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(selected);
        Assert.All(selected, id => Assert.Contains(ModelSpec.All, m => m.Id == id));
        var opcodes = MoveSpecification.Opcodes().ToArray();
        foreach (var id in selected)
        {
            var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == id));
            var report = new CoverageBatch(id, $"move-seeded-{seed}");
            var random = seed; // xorshift32; stable across framework versions.
            uint Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random; }
            for (var i = 0; i < samples; i++)
            {
                var opcode = opcodes[Next() % (uint)opcodes.Length];
                // Keep word-indexed canonical targets aligned on 000/010.
                var value = Next() & ~1u;
                var ccr = (int)(Next() & 31);
                var fixture = new MoveFixture(machine, opcode, value, ccr, (Next() & 1) != 0, $"seed={seed}/sample={i}");
                SyntheticMoveTests.Run(machine, report, fixture);
            }
            report.Complete(output);
        }
    }
}
