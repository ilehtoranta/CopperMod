using System.Text.Json;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

internal sealed record InstructionSpec(string Family, int Milestone, string Sizes, string OperandForms, string Models = "all", string Removed060 = "")
{
    public string ArchitecturalOutcome(ModelSpec model)
    {
        if (Family is "MOVE16" or "CINV" or "CPUSH" && model.Id is not ("68040" or "68060")) return "line-F-vector-11";
        if (Family == "LPSTOP" && model.Id != "68060") return "line-F-vector-11";
        if (Models == "010+" && model.Id == "68000") return "illegal-vector-4";
        if (Models == "020+" && !model.FullIndex) return "illegal-vector-4";
        if (Models == "020-only" && model.Id is not ("68020" or "68EC020" or "A1200")) return "illegal-vector-4";
        if (Models == "040+" && model.Id is not ("68040" or "68060")) return "illegal-vector-4";
        if (Models == "060" && model.Id != "68060") return "illegal-vector-4";
        if (model.Id == "68060" && Removed060.Length != 0) return Removed060;
        return "execute";
    }
}

internal static class IntegerInventory
{
    // Integer unit instruction inventory: M68000PM sections 3–6 and MC68060UM.
    // A family being listed never implies its scenarios have been implemented.
    public static readonly InstructionSpec[] All = Build().ToArray();
    private static IEnumerable<InstructionSpec> Build()
    {
        yield return new("MOVE", 1, "B,W,L", "legal-source -> data-alterable; all register fields");
        yield return new("MOVEA", 1, "W,L", "legal-source -> An; all register fields");
        yield return new("MOVEQ", 2, "L", "signed-imm8 -> Dn");
        yield return new("MOVEM", 2, "W,L", "register-mask <-> control-memory; predecrement store; postincrement load");
        yield return new("MOVEP", 2, "W,L", "Dn <-> d16(An), spaced bytes", Removed060: "unimplemented-integer-vector-61");
        yield return new("LEA", 2, "L", "control-EA -> An");
        yield return new("PEA", 2, "L", "control-EA -> stack");
        yield return new("EXG", 2, "L", "Dn/Dm; An/Am; Dn/Am");
        yield return new("EXT", 2, "W,L", "Dn: B->W; W->L");
        yield return new("EXTB", 2, "L", "Dn: B->L", "020+");
        yield return new("SWAP", 2, "L", "Dn word halves");
        foreach (var name in new[] { "ADD", "SUB" }) yield return new(name, 3, "B,W,L", "data-EA -> Dn; Dn -> memory-alterable");
        foreach (var name in new[] { "ADDA", "SUBA", "CMPA" }) yield return new(name, 3, "W,L", "all-source -> An");
        foreach (var name in new[] { "ADDI", "SUBI", "CMPI" }) yield return new(name, 3, "B,W,L", "immediate -> data-alterable; CMPI PC-relative on 020+");
        foreach (var name in new[] { "ADDQ", "SUBQ" }) yield return new(name, 3, "B,W,L", "1..8 -> data-alterable; An W,L");
        foreach (var name in new[] { "ADDX", "SUBX" }) yield return new(name, 3, "B,W,L", "Dn -> Dm; -(An) -> -(Am); sticky Z");
        yield return new("CMP", 3, "B,W,L", "data-EA -> Dn; An source W,L");
        yield return new("CMPM", 3, "B,W,L", "(An)+ -> (Am)+");
        foreach (var name in new[] { "MULS", "MULU", "DIVS", "DIVU" })
        {
            yield return new(name + ".word", 3, "W", "data-EA -> Dn");
            yield return new(name + ".long32", 3, "L", "data-EA -> Dn; optional remainder register", "020+");
            yield return new(name + ".long64", 3, "L", "data-EA -> register pair", "020+", "unimplemented-integer-vector-61");
        }
        foreach (var name in new[] { "ABCD", "SBCD" }) yield return new(name, 3, "B", "Dn -> Dm; -(An) -> -(Am); valid BCD; mask undefined flags");
        yield return new("NBCD", 3, "B", "data-alterable; valid BCD; mask undefined flags");
        foreach (var name in new[] { "PACK", "UNPK" }) yield return new(name, 3, "packed", "Dn -> Dm; -(An) -> -(Am); adjustment word", "020+");
        foreach (var name in new[] { "AND", "OR" }) yield return new(name, 4, "B,W,L", "data-EA -> Dn; Dn -> memory-alterable");
        yield return new("EOR", 4, "B,W,L", "Dn -> data-alterable");
        foreach (var name in new[] { "ANDI", "ORI", "EORI" }) yield return new(name, 4, "B,W,L", "immediate -> data-alterable");
        foreach (var name in new[] { "CLR", "NEG", "NEGX", "NOT", "TST" }) yield return new(name, 4, "B,W,L", "data-alterable; TST extra source forms on 020+");
        foreach (var name in new[] { "BTST", "BCHG", "BCLR", "BSET" }) yield return new(name, 4, "B,L", "immediate/Dn bit -> byte-memory or long-Dn; BTST extra source forms");
        foreach (var name in new[] { "ASL", "ASR", "LSL", "LSR", "ROL", "ROR", "ROXL", "ROXR" }) yield return new(name, 4, "B,W,L", "immediate/register count -> Dn; single-bit W memory");
        foreach (var name in new[] { "BFCHG", "BFCLR", "BFEXTS", "BFEXTU", "BFFFO", "BFINS", "BFSET", "BFTST" }) yield return new(name, 4, "bitfield", "Dn/control-EA; signed register offsets; immediate/register width", "020+");
        yield return new("TAS", 4, "B", "data-alterable; atomic memory read/write");
        yield return new("CAS", 4, "B,W,L", "compare/update registers, memory-alterable", "020+");
        yield return new("CAS2", 4, "W,L", "two compare/update/address register tuples", "020+", "unimplemented-integer-vector-61");
        foreach (var name in new[] { "BRA", "BSR", "Bcc" }) yield return new(name, 5, "B,W,L(020+)", "relative displacement; all conditions");
        yield return new("DBcc", 5, "W", "Dn low-word counter, displacement; all conditions");
        yield return new("Scc", 5, "B", "data-alterable; all conditions");
        yield return new("TRAPcc", 5, "none,W,L", "optional immediate; all conditions", "020+");
        foreach (var name in new[] { "JMP", "JSR" }) yield return new(name, 5, "L", "control-EA");
        yield return new("LINK", 5, "W,L(020+)", "An, signed displacement");
        yield return new("UNLK", 5, "L", "An, stack frame");
        foreach (var name in new[] { "RTS", "RTR", "RTE", "RESET", "NOP", "TRAPV", "ILLEGAL", "Line-A", "Line-F" }) yield return new(name, 5, "none", "implicit; documented privilege/exception behavior");
        yield return new("RTD", 5, "W", "stack, signed adjustment", "010+");
        yield return new("STOP", 5, "W", "immediate SR, privileged");
        yield return new("TRAP", 5, "none", "vector 0..15");
        yield return new("CHK", 5, "W,L(020+)", "data-EA, Dn bounds");
        foreach (var name in new[] { "CHK2", "CMP2" }) yield return new(name, 5, "B,W,L", "control-EA bounds pair, general register", "020+", "unimplemented-integer-vector-61");
        yield return new("BKPT", 5, "none", "breakpoint 0..7; acknowledge-dependent exception", "010+");
        yield return new("CALLM", 5, "B", "immediate count, control-EA", "020-only");
        yield return new("RTM", 5, "none", "Dn/An module frame", "020-only");
        yield return new("MOVES", 5, "B,W,L", "general-register <-> memory-alterable, SFC/DFC, privileged", "010+");
        yield return new("MOVEC", 5, "L", "general-register <-> model-specific control-register, privileged", "010+");
        foreach (var name in new[] { "MOVE from SR", "MOVE to SR", "MOVE to CCR" }) yield return new(name, 5, "W", "documented source/destination EA, model-dependent privilege");
        yield return new("MOVE from CCR", 5, "W", "CCR -> data-alterable", "010+");
        yield return new("MOVE USP", 5, "L", "An <-> USP, privileged");
        foreach (var name in new[] { "ANDI", "ORI", "EORI" })
        foreach (var target in new[] { "CCR", "SR" }) yield return new(name + " to " + target, 5, "W", "immediate; SR privileged");
        yield return new("MOVE16", 5, "16 bytes", "aligned line; model-defined register/absolute forms", "040+");
        foreach (var name in new[] { "CINV", "CPUSH" }) yield return new(name, 5, "line,page,all", "instruction/data/both caches, privileged", "040+");
        yield return new("LPSTOP", 5, "W", "immediate SR, privileged", "060");
        yield return new("HALT", 5, "none", "privileged debug halt; interrupts cannot restart", "060");
        yield return new("PULSE", 5, "none", "user/supervisor; integer state preserved; physical PST/debug commands unqualified", "060");
    }
}

public sealed class SyntheticInventoryTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Suite", "Synthetic")]
    public void InventoryKeepsEveryPlannedIntegerFamilyVisible()
    {
        Assert.Equal(IntegerInventory.All.Length, IntegerInventory.All.Select(x => x.Family).Distinct().Count());
        Assert.Contains(IntegerInventory.All, s => s.Family == "MOVEA");
        Assert.Contains(IntegerInventory.All, s => s.Family == "CALLM");
        Assert.Contains(IntegerInventory.All, s => s.Family == "LPSTOP");
        Assert.Contains(IntegerInventory.All, s => s.Family == "HALT");
        Assert.Contains(IntegerInventory.All, s => s.Family == "PULSE");
        var rows = from model in ModelSpec.All from spec in IntegerInventory.All
                   select new { model = model.Id, model.Diagnostic, instruction = spec.Family, spec.Sizes, spec.OperandForms, spec.Milestone,
                       outcome = spec.ArchitecturalOutcome(model), status = "untested", note = "Execution evidence is in named scenario batches; inventory membership is not a passing result." };
        var report = JsonSerializer.Serialize(new { schema = 1, families = IntegerInventory.All.Length, combinations = rows,
            models = ModelSpec.All.Select(m => new { m.Id, m.AddressBits, m.DataAlignment, m.IndexRules, m.StackRules, m.Diagnostic }),
            excluded = new[] { "FPU arithmetic", "enabled MMU translation", "CPU32-only integer instructions", "physical pipeline/cache qualification", "OS compatibility", "Decimal arithmetic results for non-BCD operands", "64-bit MUL with identical high/low registers (undefined result)" },
            reserved = "Full indexed BD=00, bit3=1, IIS=100, suppressed-index IIS=101..111: undefined/reserved; no architectural execution expectation." }, new JsonSerializerOptions { WriteIndented = true });
        var directory = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (!string.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "integer-inventory.json"), report); }
        output.WriteLine($"{IntegerInventory.All.Length} integer families, {rows.Count()} model/family specifications. Later milestone coverage remains explicitly untested.");
    }
}
