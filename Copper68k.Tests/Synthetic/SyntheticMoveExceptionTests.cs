using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticMoveExceptionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => SyntheticMoveTests.Models;

    [Fact]
    public void Rejected040DestinationExtensionNeverRetriesAnAlreadyReadSource()
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == "68040"));
        machine.Reset();
        machine.Bus.Initialize(0x4000, 0x89abcdef, 4);
        machine.Bus.Initialize(SyntheticMachine.Code, 0x2398, 2); // MOVE.L (A0)+,(full,A1)
        machine.Bus.Initialize(SyntheticMachine.Code + 2, 0x0100, 2); // reserved BD=00
        machine.Start();
        Assert.Throws<UnsupportedM68040InstructionException>(() => machine.Core.ExecuteInstruction());
        Assert.Equal(0x4004u, machine.Core.State.A[0]);
        Assert.Single(machine.Bus.Accesses.Where(a => !a.Write && a.Address == 0x4000 && a.Kind == M68kBusAccessKind.CpuDataRead));
        Assert.DoesNotContain(machine.Bus.Accesses, a => a.Write);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void IllegalMoveOperandEncodingsRaiseIllegalInstruction(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-invalid-operands");
        for (var op = 0x1000; op < 0x4000; op++)
        {
            if (MoveSpecification.Legal((ushort)op)) continue;
            machine.Reset(31);
            machine.Bus.Initialize(SyntheticMachine.Code, (uint)op, 2);
            machine.Bus.Initialize(SyntheticMachine.Code + 2, 0, 4);
            machine.Start();
            var d = (uint[])machine.Core.State.D.Clone();
            var a = (uint[])machine.Core.State.A.Clone();
            var id = $"{modelId}/MOVE/invalid/op={op:X4}";
            try
            {
                machine.Core.ExecuteInstruction();
                var state = machine.Core.State;
                var mismatch = state.ProgramCounter != 0x9040 ? $"Expected vector 4, PC={state.ProgramCounter:X8}" :
                    !d.SequenceEqual(state.D) || !a.Take(7).SequenceEqual(state.A.Take(7)) ? "Illegal opcode modified registers" : null;
                if (mismatch == null)
                {
                    var frame = a[7] - (machine.Model.Model == M68kCpuModel.M68000 ? 6u : 8u);
                    if (state.A[7] != frame || machine.Bus.Peek(frame, 2) != 0x271f || machine.Bus.Peek(frame + 2, 4) != SyntheticMachine.Code)
                        mismatch = "Illegal instruction frame PC/SR/stack mismatch";
                }
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (UnsupportedM68kTimingException ex) { report.Record(id, "unsupported", ex.Message); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }

    [Theory]
    [MemberData(nameof(Models))]
    [Trait("Suite", "Synthetic")]
    public void MisalignedMoveDataUsesModelAlignmentRules(string modelId)
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(m => m.Id == modelId));
        var report = new CoverageBatch(modelId, "move-alignment");
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var write in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        {
            var opcode = MoveSpecification.Encode(width, write ? new(0, 0) : new(2, 0), write ? new(2, 0) : new(0, 1));
            var fixture = new MoveFixture(machine, opcode, 0x89abcdef, 31, supervisor, "odd-data", customize: m => m.Core.State.A[0] = 0x4001);
            fixture.Prepare();
            var id = fixture.Id;
            try
            {
                machine.Core.ExecuteInstruction();
                string? mismatch;
                if (width == 1 || machine.Model.FullIndex) mismatch = fixture.Verify();
                else
                {
                    var state = machine.Core.State;
                    mismatch = state.ProgramCounter != 0x9030 ? "Expected address error vector 3" :
                        !state.GetFlag(M68kCpuState.Supervisor) ? "Exception did not select supervisor stack" :
                        machine.Bus.Accesses.Any(a => a.Address == 0x4001 && a.Kind is M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite) ? "Faulting data access reached bus" : null;
                    // Detailed restart state and physical fault ordering stay in their
                    // specialized suites; this gate verifies the architectural outcome.
                    if (state.LastExceptionVector != 3) mismatch = "Wrong recorded architectural exception";
                }
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }
}
