using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.6/8.4.6.2/8.4.6.7, not scalar/batch agreement.
// This focused entry gate remains separate from broader restoration coverage.
public sealed class SyntheticM68040OperandReadFaultDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_OPERAND_READ_DISCOVERY";
    private const uint Operand = 0x4200, Handler = 0xa000;
    private static readonly (string Family, int Width, ushort Opcode, bool Movem)[] Instructions =
    [
        ("MOVE", 1, 0x1010, false), ("MOVE", 2, 0x3010, false), ("MOVE", 4, 0x2010, false),
        ("MOVEA", 2, 0x3050, false), ("MOVEA", 4, 0x2050, false),
        ("ADD", 1, 0xd010, false), ("ADD", 2, 0xd050, false), ("ADD", 4, 0xd090, false),
        ("CMP", 1, 0xb010, false), ("CMP", 2, 0xb050, false), ("CMP", 4, 0xb090, false),
        ("TST", 1, 0x4a10, false), ("TST", 2, 0x4a50, false), ("TST", 4, 0x4a90, false),
        ("MOVEM", 2, 0x4c90, true), ("MOVEM", 4, 0x4cd0, true)
    ];

    [EnvironmentFact(Enable, "qualify actual 040 operand-read access frames"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarReadFaultsRequireFormat7() => Audit(false);

    [EnvironmentFact(Enable, "qualify actual 040 operand-read access frames"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchReadFaultsRequireFormat7() => Audit(true);

    [Theory, Trait("Suite", "Synthetic")]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualMovemFaultRetainsCalculatedEaAcrossLoadedBaseAndIndex(bool full)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        m.Reset(31); m.Core.State.A[0] = Operand; m.Core.State.D[1] = 0x20;
        m.InitializePhysical(0x1000, 0x4cf0, 2); // MOVEM.L indexed(A0),D0/D1/A0/A1
        m.InitializePhysical(0x1002, 0x0303, 2);
        m.InitializePhysical(0x1004, full ? 0x1922u : 0x1800u, 2);
        if (full)
        {
            m.InitializePhysical(0x1006, 0x0010, 2); m.InitializePhysical(0x1008, 0x0006, 2);
            m.InitializePhysical(0x4230, 0x5000, 4); // A0 + D1.L + BD, then outer displacement.
        }
        var ea = full ? 0x5006u : 0x4220u;
        uint[] values = [0x11112222, 0x00000040, 0x66667777, 0x88889999];
        for (var n = 0; n < 4; n++) m.InitializePhysical(ea + (uint)n * 4, values[n], 4);
        var next = full ? 0x100au : 0x1006u;
        m.InitializePhysical(next, 0x7e55, 2); m.InitializePhysical(next + 2, 0x4e71, 2);
        m.InitializePhysical(8, Handler, 4); m.InitializePhysical(Handler, 0x4e73, 2);
        m.InitializePhysical(Handler + 2, 0x4e71, 2); m.Start();
        var sequence = m.Core.State.ExceptionSequence;
        bus.Accesses.Clear(); bus.Arm(ea + 12, M68kBusAccessKind.CpuDataRead);
        m.Core.ExecuteInstruction();
        var frame = 0x4700u - 60;
        Assert.Equal(frame, m.Core.State.A[7]); Assert.Equal(Handler, m.Core.State.ProgramCounter);
        Assert.Equal(0x7008u, m.PeekPhysical(frame + 6, 2));
        Assert.Equal(0x1105u, m.PeekPhysical(frame + 12, 2) & 0xff7f);
        Assert.Equal(ea, m.PeekPhysical(frame + 8, 4)); Assert.Equal(ea + 12, m.PeekPhysical(frame + 20, 4));
        Assert.Equal(0x1000u, m.PeekPhysical(frame + 2, 4));
        Assert.Equal(values[0], m.Core.State.D[0]); Assert.Equal(values[1], m.Core.State.D[1]);
        Assert.Equal(values[2], m.Core.State.A[0]); Assert.Equal(0x4100u, m.Core.State.A[1]);
        Assert.Single(bus.Rejected);
        m.Core.ExecuteInstruction(); // Software handler explicitly returns via RTE.
        Assert.Equal(0x1000u, m.Core.State.ProgramCounter); Assert.Equal(0x4700u, m.Core.State.A[7]);
        m.Core.ExecuteInstruction(); // CM resumes after EA calculation; preceding MOVEM reads repeat by architecture.
        Assert.Equal(next, m.Core.State.ProgramCounter); Assert.Equal(values[3], m.Core.State.A[1]);
        Assert.Equal(values[0], m.Core.State.D[0]); Assert.Equal(values[1], m.Core.State.D[1]);
        Assert.Equal(values[2], m.Core.State.A[0]); Assert.Equal(0x271f, m.Core.State.StatusRegister);
        for (var n = 0; n < 3; n++) Assert.Equal(2, bus.Accesses.Count(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address == ea + (uint)n * 4));
        Assert.Single(bus.Accesses.Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address == ea + 12));
        if (full) Assert.Single(bus.Accesses.Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address == 0x4230));
        Assert.Single(bus.Rejected); Assert.Equal(sequence + 1, m.Core.State.ExceptionSequence);
        m.Core.ExecuteInstruction(); Assert.Equal(0x55u, m.Core.State.D[7]);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void FixedManualReadInstructionWitnessesValidateEncodings()
    {
        // Literal results for source bytes 80 81 82 83 and initial D0 A55A0022,
        // CCR 1F. These are independent of production decoder/arithmetic helpers.
        uint[] expectedData = [0xa55a0080, 0xa55a8081, 0x80818283, 0xa55a0022, 0xa55a0022,
            0xa55a00a2, 0xa55a80a3, 0x25db82a5, 0xa55a0022, 0xa55a0022, 0xa55a0022,
            0xa55a0022, 0xa55a0022, 0xa55a0022, 0xffff8081, 0x80818283];
        ushort[] expectedStatus = [0x2718, 0x2718, 0x2718, 0x271f, 0x271f,
            0x2708, 0x2708, 0x2713, 0x271b, 0x2711, 0x2710, 0x2718, 0x2718, 0x2718, 0x271f, 0x271f];
        for (var index = 0; index < Instructions.Length; index++)
        {
            var instruction = Instructions[index];
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
            m.Reset(31); m.Core.State.A[0] = Operand;
            m.InitializePhysical(Operand, 0x80818283, 4);
            m.InitializePhysical(SyntheticMachine.Code, instruction.Opcode, 2);
            m.InitializePhysical(SyntheticMachine.Code + 2, instruction.Movem ? 1u : 0x4e71u, 2);
            m.InitializePhysical(SyntheticMachine.Code + 4, 0x4e71, 2);
            m.Start();
            var e = ArchitecturalExpectation.Capture(m);
            e.D[0] = expectedData[index]; e.Sr = expectedStatus[index];
            e.Pc += instruction.Movem ? 4u : 2u;
            if (instruction.Family == "MOVEA") e.A[0] = instruction.Width == 2 ? 0xffff8081u : 0x80818283u;
            m.Core.ExecuteInstruction();
            Assert.True(e.Verify(m) is null, $"Manual read encoding {instruction.Opcode:X4}: {e.Verify(m)}");
        }
    }

    private void Audit(bool batch)
    {
        var report = new CoverageBatch("68040", "operand-read-fault-discovery-" + (batch ? "batch" : "scalar"));
        foreach (var instruction in Instructions)
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        for (var ccr = 0; ccr < 32; ccr++)
        for (var lane = 0; lane < 4; lane++)
        for (var faultByte = 0; faultByte < instruction.Width; faultByte++)
            Case(report, batch, instruction, bank, trace, ccr, lane, faultByte);
        report.Complete(output);
    }

    private static void Case(CoverageBatch report, bool batch,
        (string Family, int Width, ushort Opcode, bool Movem) instruction,
        string bank, ushort trace, int ccr, int lane, int faultByte)
    {
        var id = $"68040/{instruction.Family}/operand-read-fault/size={instruction.Width}/bank={bank}/T={trace:X4}/lane={lane}/byte={faultByte}/op={instruction.Opcode:X4}/ccr={ccr:X2}";
        try
        {
            var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
            var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
            m.Reset(ccr);
            m.Core.State.A[0] = Operand + (uint)lane;
            m.InitializePhysical(Operand - 4, 0xa55aa55a, 4);
            for (uint n = 0; n < 12; n++) m.InitializePhysical(Operand + n, 0x80 + n, 1);
            m.InitializePhysical(SyntheticMachine.Code, instruction.Opcode, 2);
            m.InitializePhysical(SyntheticMachine.Code + 2, instruction.Movem ? 0x0001u : 0x4e71u, 2);
            m.InitializePhysical(SyntheticMachine.Code + 4, 0x4e71, 2);
            m.InitializePhysical(8, Handler, 4);
            m.InitializePhysical(Handler, 0x4e73, 2);
            m.InitializePhysical(Handler + 2, 0x4e71, 2);
            foreach (var top in new uint[] { 0x4700, 0x7400 })
                for (var n = -64; n < 4; n++) m.InitializePhysical(unchecked(top + (uint)n), (uint)(n ^ 0x5a), 1);
            m.Start();
            var sr = (ushort)(0x0700 | M68040StackFixture.Status(bank, trace, ccr));
            m.Core.State.SetUserStackPointer(0x7800);
            m.Core.State.SetInterruptStackPointer(0x4700);
            m.Core.State.SetMasterStackPointer(0x7400);
            m.Core.State.StatusRegister = sr;
            var e = ArchitecturalExpectation.Capture(m);
            var pointers = new Dictionary<string, uint> { ["user"] = 0x7800, ["ISP"] = 0x4700, ["MSP"] = 0x7400 };
            var exceptionBank = M68040StackFixture.ExceptionBank(sr);
            var frame = pointers[exceptionBank] - 60;
            pointers[exceptionBank] = frame;
            M68040StackFixture.SetStacks(e, pointers, (ushort)((sr | 0x2000) & ~0xc000));
            e.Pc = Handler; e.ExceptionVector = 2;
            e.ControlChecks["one access exception"] = (s => s.ExceptionSequence, m.Core.State.ExceptionSequence + 1);
            e.ControlChecks["original instruction PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
            e.ControlChecks["original SR"] = (s => s.LastExceptionStatusRegister, sr);
            e.ControlChecks["single original-width rejected read"] = (_ => bus.Rejected.Count == 1 &&
                bus.Rejected[0].Address == Operand + (uint)lane && bus.Rejected[0].Width == instruction.Width &&
                bus.Rejected[0].Kind == M68kBusAccessKind.CpuDataRead ? 1u : 0u, 1);
            e.ControlChecks["no translation bypass leak"] = (s => s.M68040Mmu.BypassTranslation ? 1u : 0u, 0);
            e.Write(frame, sr, 2, m.Model); e.Write(frame + 2, SyntheticMachine.Code, 4, m.Model);
            e.Write(frame + 6, 0x7008, 2, m.Model);
            // Undefined EA/push/writeback data are not assigned architectural values.
            for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
            var size = instruction.Width == 1 ? 0x20 : instruction.Width == 2 ? 0x40 : 0;
            var modifier = (sr & 0x2000) != 0 ? 5 : 1;
            var ssw = (uint)(0x0100 | size | modifier | (instruction.Movem ? 0x1000 : 0));
            e.Write(frame + 12, ssw, 2, m.Model);
            e.MemoryMasks[frame + 12] = 255; e.MemoryMasks[frame + 13] = 0x7f; // X undefined.
            if (instruction.Movem)
            {
                e.Write(frame + 8, Operand + (uint)lane, 4, m.Model);
                for (uint n = 8; n < 12; n++) e.MemoryMasks[frame + n] = 255;
            }
            foreach (var offset in new uint[] { 14, 16, 18 })
            {
                e.Write(frame + offset, 0, 2, m.Model);
                e.MemoryMasks[frame + offset + 1] = 0x80; // No pending writes before first operand read.
            }
            e.Write(frame + 20, Operand + (uint)lane, 4, m.Model);
            for (uint n = 20; n < 24; n++) e.MemoryMasks[frame + n] = 255;
            bus.Arm(Operand + (uint)(lane + faultByte), M68kBusAccessKind.CpuDataRead);
            // Exactly one faulting attempt. Do not retry partially executed instructions.
            M68040StackFixture.Step(m, e, report, id, batch);
        }
        catch (NotSupportedException ex) { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
    }
}
