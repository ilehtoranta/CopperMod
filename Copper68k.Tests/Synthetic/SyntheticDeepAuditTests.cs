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
        Assert.True(selected.Distinct(StringComparer.Ordinal).Count() == selected.Length, "Duplicate audit model IDs are not allowed");
        Assert.All(selected, id => Assert.Contains(ModelSpec.All, m => m.Id == id));
        var opcodes = MoveSpecification.Opcodes().ToArray();
        foreach (var id in selected)
        {
            var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == id));
            var report = new CoverageBatch(id, $"move-seeded-{seed}");
            var arithmetic = new CoverageBatch(id, $"arithmetic-seeded-{seed}");
            var logical = new CoverageBatch(id, $"logical-seeded-{seed}");
            var control = new CoverageBatch(id, $"control-seeded-{seed}");
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
                if (Next() % 4 == 0)
                    SyntheticBitFieldTests.Case(machine, logical, (int)(Next() % 8), new(2, 0), unchecked((int)Next()), (int)(1 + Next() % 32), Next(), (int)(Next() & 31), offsetReg: 2, widthReg: 3);
                else
                {
                    var logicalFamily = LogicalSpecification.BinaryFamilies.Concat(LogicalSpecification.UnaryFamilies).ToArray();
                    var operation = logicalFamily[Next() % (uint)logicalFamily.Length];
                    SyntheticLogicalTests.Case(machine, logical, operation, operation == "TAS" ? 1 : width, form, Next() & ~1u, Next() & ~1u, (int)(Next() & 31), reg: (int)(Next() % 8), supervisor: (Next() & 1) != 0);
                }
                if ((Next() & 1) == 0)
                    SyntheticControlTests.Dbcc(machine, control, (int)(Next() % 16), (int)(Next() % 8), Next(), (int)(Next() & 31), unchecked((short)(Next() & 0xfffe)));
                else
                    SyntheticControlTests.Branch(machine, control, (int)(Next() % 16), machine.Model.FullIndex && Next() % 3 == 0 ? 4 : 2, unchecked((short)(Next() & 0xfffe)), (int)(Next() & 31), (Next() & 1) != 0);
            }
            report.Complete(output);
            arithmetic.Complete(output);
            logical.Complete(output);
            control.Complete(output);
        }
    }
}
