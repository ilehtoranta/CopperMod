using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68040MovemContinuationTests(ITestOutputHelper output)
{
    private static readonly string[] Banks = ["user", "ISP", "MSP"];
    private static readonly ushort[] Masks = [0x0001, 0x0101, 0xffff];

    [Fact, Trait("Suite", "Synthetic")]
    public void EveryLegalMovemWordResumesFromItsCalculatedAddress()
    {
        var report = new CoverageBatch("68040", "rte-access-movem-opcodes");
        var m = NewMachine();
        foreach (var load in new[] { false, true })
        foreach (var width in new[] { 2, 4 })
        for (var mode = 2; mode < 8; mode++)
        for (var register = 0; register < 8; register++)
        {
            var legal = mode is 2 or 5 or 6 || mode == (load ? 3 : 4) || mode == 7 && register <= (load ? 3 : 1);
            if (!legal) continue;
            foreach (var bank in Banks)
            for (var ccr = 0; ccr < 32; ccr++)
            foreach (var mask in Masks) Run(m, report, load, width, mode, register, mask, bank, ccr, null);
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void FullIndexContinuationConsumesExtensionsWithoutReadingPointerChains()
    {
        var report = new CoverageBatch("68040", "rte-access-movem-full-index");
        var m = NewMachine();
        foreach (var (load, mode, register) in new[] { (false, 6, 0), (true, 6, 0), (true, 7, 3) })
        foreach (var width in new[] { 2, 4 })
        foreach (var index in IndexFixture.FullStructures())
        foreach (var bank in Banks)
        for (var ccr = 0; ccr < 32; ccr++) Run(m, report, load, width, mode, register, 0x0101, bank, ccr, index);
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, CoverageBatch report, bool load, int width, int mode, int register,
        ushort mask, string bank, int ccr, IndexFixture? index)
    {
        m.Reset(ccr);
        m.Core.State.D[0] = 0x200;
        var opcode = (ushort)((load ? 0x4c80 : 0x4880) | (width == 4 ? 0x40 : 0) | mode << 3 | register);
        var words = new List<ushort> { opcode, mask };
        var d = (uint[])m.Core.State.D.Clone(); var a = (uint[])m.Core.State.A.Clone();
        var options = new AddressOptions(SourceIndex: index ?? new(IndexRegister: 0, BriefDisplacement: 0x40));
        var fixture = new AddressingFixture(m, width, words, d, a, 0, options);
        var address = fixture.Resolve(new OperandForm(mode, register), true);
        if (mode == 4) address += (uint)width; // MOVEM's calculated predecrement EA precedes the first decrement.
        var transfers = Enumerable.Range(0, 16).Where(bit => (mask & 1 << bit) != 0).ToArray();
        var targets = transfers.Select((_, n) => mode == 4 ? address - (uint)((n + 1) * width) : address + (uint)(n * width)).ToArray();
        for (var n = 0; n < targets.Length; n++)
        {
            m.InitializePhysical(targets[n], width == 2 ? 0x8001u + (uint)n : 0x89abcdefu + (uint)n, width);
            m.InitializePhysical(targets[n] - 1, 0x96, 1);
            m.InitializePhysical(targets[n] + (uint)width, 0x69, 1);
        }
        var frame = bank == "MSP" ? 0x7400u : 0x4700u;
        for (var n = -16; n < 80; n++) m.InitializePhysical(unchecked(frame + (uint)n), (uint)(n ^ 0x5a), 1);
        var savedSr = SyntheticM68040AccessFrameAuditTests.SavedStatus(bank, 0, ccr);
        m.InitializePhysical(frame, savedSr, 2); m.InitializePhysical(frame + 2, SyntheticMachine.Code, 4);
        m.InitializePhysical(frame + 6, 0x7008, 2); m.InitializePhysical(frame + 8, address, 4);
        m.InitializePhysical(frame + 12, 0x1005, 2);
        m.InitializePhysical(0x9000, 0x4e73, 2); m.InitializePhysical(0x9002, 0x4e71, 2);
        _ = SyntheticExecution.Prepare(m, words);
        m.Core.State.SetMasterStackPointer(0x7400);
        if (bank == "MSP") m.Core.State.StatusRegister |= 0x1000;
        // A partial load can overwrite its own base or index. Pointer memory may
        // change in the handler. Restart uses the calculated EA, not these inputs.
        if (load && mode is 2 or 6 && register != 7) m.Core.State.A[register] = 0x12348000;
        if (mode == 6 || mode == 7 && register == 3) m.Core.State.D[index?.IndexRegister ?? 0] = 0x12345000;
        m.Core.State.ProgramCounter = 0x9000;
        var e = ArchitecturalExpectation.Capture(m);
        var sequence = m.Core.State.ExceptionSequence;
        e.Sr = savedSr; e.Pc = SyntheticMachine.Code;
        e.A[7] = bank == "user" ? 0x7800u : frame + 60;
        e.InactiveStackPointer = bank == "user" ? frame + 60 : 0x7800u;
        if (bank == "MSP") e.MasterStackPointer = frame + 60;
        e.ControlChecks["no exception"] = (s => s.ExceptionSequence, sequence);
        var id = $"68040/MOVEM-CM/{(load ? "load" : "store")}/{width}/ea={mode}:{register}/{index?.Id ?? "brief"}/bank={bank}/mask={mask:X4}/op={opcode:X4}/ccr={ccr:X2}";
        if (!Step(m, e, report, id + "/RTE"))
        { report.Record(id + "/MOVEM", "untested", "RTE prerequisite failed"); report.Record(id + "/NOP", "untested", "RTE prerequisite failed"); return; }
        e.Pc = SyntheticMachine.Code + (uint)words.Count * 2;
        var registers = e.D.Concat(e.A).ToArray();
        for (var n = 0; n < transfers.Length; n++)
        {
            var destination = mode == 4 ? 15 - transfers[n] : transfers[n];
            if (load)
            {
                var value = Read(e.Memory, targets[n], width);
                if (width == 2) value = unchecked((uint)(int)(short)value);
                if (destination < 8) e.D[destination] = value;
                else e.A[destination - 8] = value;
            }
            else
            {
                var value = registers[destination];
                if (mode == 4 && destination == register + 8) value -= (uint)width;
                e.Write(targets[n], value, width, m.Model);
            }
        }
        if (mode is 3 or 4) e.A[register] = unchecked(address + (mode == 4 ? 0u - (uint)(transfers.Length * width) : (uint)(transfers.Length * width)));
        if (bank == "MSP") e.MasterStackPointer = e.A[7];
        e.OperandAccessAddresses.UnionWith(targets);
        e.ExpectedOperandTransfers = Enumerable.Repeat((!load, width), transfers.Length).ToList();
        if (index is { Full: true, Indirect: not 0 })
        {
            // Only reads at the saved operand addresses are allowed during MOVEM;
            // any reconstructed pointer read would add another data transfer.
            e.ControlChecks["operand read count"] = (_ => (uint)m.Bus.Accesses.Count(x => !x.Write && x.Kind == M68kBusAccessKind.CpuDataRead), load ? (uint)transfers.Length : 0u);
        }
        m.Bus.Accesses.Clear();
        if (!Step(m, e, report, id + "/MOVEM"))
        { report.Record(id + "/NOP", "untested", "MOVEM prerequisite failed"); return; }
        e.Pc += 2;
        Step(m, e, report, id + "/NOP");
    }

    private static uint Read(Dictionary<uint, byte> memory, uint address, int width)
    {
        uint value = 0;
        for (var n = 0; n < width; n++) value = (value << 8) | memory.GetValueOrDefault(address + (uint)n);
        return value;
    }
    private static SyntheticMachine NewMachine() => new(ModelSpec.All.Single(x => x.Id == "68040"));
    private static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try
        {
            m.Core.ExecuteInstruction();
            var mismatch = e.Verify(m);
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            return mismatch == null;
        }
        catch (Exception ex)
        { report.Record(id, ex is UnsupportedM68kTimingException or UnsupportedM68040InstructionException ? "unsupported" : "mismatching", ex.Message); return false; }
    }
}
