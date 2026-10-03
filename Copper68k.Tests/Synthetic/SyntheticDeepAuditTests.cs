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
            var arithmetic = new CoverageBatch(id, $"arithmetic-seeded-{seed}");
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
                var width = new[] { 1, 2, 4 }[Next() % 3];
                var families = new[] { "ADD", "SUB", "CMP", "ADDI", "SUBI", "CMPI", "ADDQ", "SUBQ" };
                var family = families[Next() % (uint)families.Length];
                var forms = ArithmeticSpecification.Alterable().ToArray();
                var form = forms[Next() % (uint)forms.Length];
                // Every seeded indexed target remains aligned on early models.
                var source = family.EndsWith('Q') ? 1 + Next() % 8 : Next() & ~1u;
                SyntheticArithmeticTests.Case(machine, arithmetic, family, width, form, source, Next() & ~1u, (int)(Next() & 31),
                    reg: (int)(Next() % 8), supervisor: (Next() & 1) != 0, scenario: $"seed={seed}/sample={i}");
            }
            report.Complete(output);
            arithmetic.Complete(output);
        }
    }
}
