using System.Text.Json;
using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticMoveTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => ModelSpec.All.Select(m => new object[] { m.Id });

    [Fact]
    [Trait("Suite", "Synthetic")]
    public void IndependentEncodingsMatchMotorolaExamples()
    {
        Assert.Equal(0x123c, MoveSpecification.Encode(1, new(7, 4), new(0, 1))); // MOVE.B #imm,D1
        Assert.Equal(0x307c, MoveSpecification.Encode(2, new(7, 4), new(1, 0))); // MOVEA.W #imm,A0
        Assert.Equal(0x23fc, MoveSpecification.Encode(4, new(7, 4), new(7, 1))); // MOVE.L #imm,abs.l
        Assert.Equal(9726, MoveSpecification.Opcodes().Count());
        Assert.False(MoveSpecification.Legal(0x1048)); // MOVE.B An,An
        Assert.False(MoveSpecification.Legal(0x35c0)); // PC-relative destination
        Assert.Equal(66, IndexFixture.FullStructures().Count());
        Assert.Equal(0x0923, new IndexFixture(Full: true, IndexRegister: 0, LongIndex: true, BaseSize: 2, Indirect: 3).Extension);
        Assert.Equal(new ushort[] { 0xffe0, 2, 0 }, new IndexFixture(Full: true, BaseSize: 2, Indirect: 3).Displacements);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void EveryLegalMoveOpcode(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-opcodes");
        foreach (var opcode in MoveSpecification.Opcodes()) Run(machine, report, new(machine, opcode, 0x89abcdee));
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MoveRegisterSignExtensionAndOverlappingMemory(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-register-overlap");
        foreach (var width in new[] { 2, 4 })
        foreach (var source in Enumerable.Range(0, 8))
        foreach (var destination in Enumerable.Range(0, 8))
        foreach (var destinationMode in new[] { 0, 1 })
        foreach (var value in new[] { 0u, 0x7fffu, 0x8000u, 0xffffu, 0x12348000u, 0x80000000u, uint.MaxValue })
            Run(machine, report, new(machine, MoveSpecification.Encode(width, new(1, source), new(destinationMode, destination)), ccr: 31,
                scenario: "address-register-bits", customize: m => { if (source == 7) m.Core.State.SetActiveStackPointer(value); else m.Core.State.A[source] = value; }));
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var delta in width == 1 ? new[] { -1, 0, 1 } : new[] { -2, 0, 2 })
            Run(machine, report, new(machine, MoveSpecification.Encode(width, new(2, 0), new(2, 1)), scenario: $"overlap/delta={delta}",
                customize: m => m.Core.State.A[1] = unchecked(m.Core.State.A[0] + (uint)delta)));
        if (machine.Model.FullIndex)
        foreach (var spec in IndexFixture.FullStructures())
        foreach (var reg in new[] { 0, 7 })
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var supervisor in new[] { false, true })
            Run(machine, report, new(machine, MoveSpecification.Encode(width, new(3, reg), new(6, reg)), supervisor: supervisor,
                scenario: spec.Id + "/full-index-base-alias", options: new(DestinationIndex: spec with { AddressIndex = true, IndexRegister = reg, LongIndex = true, Scale = 1 })));
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MoveValueAndConditionCodeBoundaries(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-values-ccr");
        OperandForm[] sources = [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0), new(5, 0), new(6, 0), new(7, 0), new(7, 1), new(7, 2), new(7, 3), new(7, 4)];
        OperandForm[] destinations = [new(0, 1), new(1, 1), new(2, 1), new(3, 1), new(4, 1), new(5, 1), new(6, 1), new(7, 0), new(7, 1)];
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var source in sources)
        foreach (var destination in destinations)
        {
            var opcode = MoveSpecification.Encode(width, source, destination);
            if (!MoveSpecification.Legal(opcode)) continue;
            var sign = 1u << (width * 8 - 1);
            foreach (var value in new[] { 0u, 1u, sign - 1, sign, sign + 1, MoveSpecification.Mask(width) })
            for (var ccr = 0; ccr < 32; ccr++)
                Run(machine, report, new(machine, opcode, value, ccr, scenario: "boundary-ccr"));
        }
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MoveExtensionsAndAliases(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-extensions-aliases");
        OperandForm[] sources = [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0), new(5, 0), new(6, 0), new(7, 0), new(7, 1), new(7, 2), new(7, 3), new(7, 4)];
        OperandForm[] destinations = [new(0, 1), new(1, 1), new(2, 1), new(3, 1), new(4, 1), new(5, 1), new(6, 1), new(7, 0), new(7, 1)];
        foreach (var width in new[] { 1, 2, 4 })
        {
            foreach (var source in sources)
            foreach (var destination in destinations)
            {
                var opcode = MoveSpecification.Encode(width, source, destination);
                if (!MoveSpecification.Legal(opcode)) continue;
                Run(machine, report, new(machine, opcode, scenario: "negative-and-wide", options: new(
                    SourceAbsoluteWord: 0xf000, DestinationAbsoluteWord: 0xf100,
                    SourceAbsoluteLong: 0x12346200, DestinationAbsoluteLong: 0x23456300,
                    SourceDisplacement: -64, DestinationDisplacement: -96),
                    customize: m => { m.Core.State.A[0] |= 0x12000000; m.Core.State.A[1] |= 0x23000000; }));
            }
            foreach (var index in new[] { 0, 1, 6, 7 })
            foreach (var addressIndex in new[] { false, true })
            foreach (var longIndex in new[] { false, true })
            foreach (var scale in new[] { 0, 1, 2, 3 })
            foreach (var earlyFormatBit in machine.Model.FullIndex ? new[] { false } : new[] { false, true })
            {
                var spec = new IndexFixture(AddressIndex: addressIndex, IndexRegister: index, LongIndex: longIndex, Scale: scale, EarlyFormatBit: earlyFormatBit);
                var opt = new AddressOptions(SourceIndex: spec, DestinationIndex: spec);
                foreach (var opcode in new[] { MoveSpecification.Encode(width, new(6, 0), new(0, 1)), MoveSpecification.Encode(width, new(7, 3), new(5, 1)), MoveSpecification.Encode(width, new(3, 0), new(6, 0)) })
                    Run(machine, report, new(machine, opcode, scenario: spec.Id, options: opt,
                        customize: m => {
                            var signedIndex = longIndex ? 0xfffffff0u : 0x1234fff0u;
                            if (!addressIndex) m.Core.State.D[index] = signedIndex;
                            else if (index == 7) m.Core.State.SetActiveStackPointer(signedIndex);
                            else m.Core.State.A[index] = signedIndex;
                        }));
            }
            foreach (var sourceMode in new[] { 2, 3, 4, 5, 6 })
            foreach (var destinationMode in new[] { 2, 3, 4, 5, 6 })
            foreach (var reg in new[] { 0, 7 })
            foreach (var supervisor in new[] { false, true })
                Run(machine, report, new(machine, MoveSpecification.Encode(width, new(sourceMode, reg), new(destinationMode, reg)), supervisor: supervisor, scenario: "base-alias-stack"));
            if (!machine.Model.FullIndex) continue;
            foreach (var spec in IndexFixture.FullStructures())
            {
                foreach (var destination in destinations)
                foreach (var source in new[] { new OperandForm(6, 0), new OperandForm(7, 3) })
                {
                    var opcode = MoveSpecification.Encode(width, source, destination);
                    if (MoveSpecification.Legal(opcode)) Run(machine, report, new(machine, opcode, scenario: spec.Id + "/source", options: new(SourceIndex: spec)));
                }
                foreach (var source in sources)
                {
                    var opcode = MoveSpecification.Encode(width, source, new(6, 1));
                    if (MoveSpecification.Legal(opcode)) Run(machine, report, new(machine, opcode, scenario: spec.Id + "/destination", options: new(DestinationIndex: spec)));
                }
                // Independently exercise source/destination extension ordering with two full EAs.
                // Complementary lengths ensure one extension cannot accidentally stand in for the other.
                var other = spec with { BaseSize = 4 - spec.BaseSize, SuppressBase = !spec.SuppressBase };
                Run(machine, report, new(machine, MoveSpecification.Encode(width, new(6, 0), new(6, 1)), scenario: spec.Id + "/dual", options: new(spec, other)));
            }
        }
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MoveAcrossExternalAddressBoundary(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-address-boundary");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var address in new[] { 0x00fffffcu, 0x00fffffeu, 0x00ffffffu, 0xfffffffeu, uint.MaxValue })
        foreach (var absolute in new[] { false, true })
        foreach (var load in new[] { false, true })
        {
            if (width > 1 && (address & 1) != 0 && !machine.Model.FullIndex) continue;
            var form = absolute ? new OperandForm(7, 1) : new OperandForm(2, 0);
            var opcode = MoveSpecification.Encode(width, load ? form : new(0, 0), load ? new(0, 1) : form);
            Run(machine, report, new(machine, opcode, 0x89abcdef, 31, scenario: $"external-boundary/address={address:X8}",
                options: new(SourceAbsoluteLong: address, DestinationAbsoluteLong: address), customize: m => m.Core.State.A[0] = address));
        }
        report.Complete(output);
    }

    internal static void Run(SyntheticMachine machine, CoverageBatch report, MoveFixture fixture)
    {
        try
        {
            fixture.Prepare();
            machine.Core.ExecuteInstruction();
            var mismatch = fixture.Verify();
            if (mismatch == null)
            {
                machine.Core.ExecuteInstruction();
                mismatch = fixture.Verify(sentinel: true);
            }
            report.Record(fixture.Id, mismatch == null ? "passing" : "mismatching", mismatch);
        }
        catch (UnsupportedM68kTimingException ex) { report.Record(fixture.Id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(fixture.Id, "mismatching", ex.ToString()); }
    }
}

internal sealed class CoverageBatch(string model, string group)
{
    private readonly Dictionary<string, int> counts = new() { ["passing"] = 0, ["mismatching"] = 0, ["unsupported"] = 0, ["untested"] = 0 };
    private readonly List<object> failures = [];
    private readonly SortedDictionary<string, Dictionary<string, int>> combinations = new(StringComparer.Ordinal);
    public void Record(string id, string status, string? reason)
    {
        counts[status]++;
        var combination = id.Split("/op=", StringSplitOptions.None)[0];
        if (!combinations.TryGetValue(combination, out var results)) combinations[combination] = results = [];
        results[status] = results.GetValueOrDefault(status) + 1;
        if (status != "passing" && failures.Count < 10000) failures.Add(new { id, status, reason });
    }
    public void Complete(ITestOutputHelper output)
    {
        var report = JsonSerializer.Serialize(new { schema = 1, model, group, counts, logicalCases = counts.Values.Sum(), xunitBatches = 1, combinations, failures }, new JsonSerializerOptions { WriteIndented = true });
        var directory = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{model}-{group}.json"), report);
        }
        output.WriteLine($"{model}/{group}: {JsonSerializer.Serialize(counts)}; logical cases={counts.Values.Sum()}, xUnit batches=1, combinations={combinations.Count}");
        Assert.True(counts.Values.Sum() > 0, "Empty synthetic selection");
        Assert.True(counts["mismatching"] + counts["unsupported"] + counts["untested"] == 0,
            $"{model}/{group}: {JsonSerializer.Serialize(counts)}\n{JsonSerializer.Serialize(failures.Take(20))}");
    }
}
