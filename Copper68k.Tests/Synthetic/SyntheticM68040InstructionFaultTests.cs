using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.2.1, 8.4.6/7: instruction faults use format 7 without
// writebacks; FA describes the prefetch, while PC restarts its instruction.
// Cache disabled, physical-map rejection, scalar accurate public factory.
// This does not qualify speculative prefetch deferral or physical bus timing.
public sealed class SyntheticM68040InstructionFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020;
    private static readonly string[] Forms = ["opcode", "extension-low", "next-opcode", "self-branch"];

    [Fact, Trait("Suite", "Synthetic")]
    public void PhysicalInstructionFaultsStackDefinedFormatSevenFields() => Audit(false);

    [Fact, Trait("Suite", "Synthetic")]
    public void InstructionFaultHandlerReturnRestartsTheOriginalInstruction() => Audit(true);

    private void Audit(bool restart)
    {
        var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
        var report = new CoverageBatch("68040", restart ? "instruction-fault-restart" : "instruction-fault-frame");
        foreach (var form in Forms)
        foreach (var bank in new[] { "user", "ISP", "MSP" })
        foreach (var trace in restart ? new ushort[] { 0 } : [0, 0x8000, 0x4000])
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var value in !restart || form == "self-branch" ? new uint[] { 2 } : form == "extension-low"
            ? [0, 0x7fffffff, 0x80000000, 0xffffffff] : [0, 0x7f, 0xffffff80, 0xffffffff])
        for (var ccr = 0; ccr < 32; ccr++)
        for (var faultByte = 0; faultByte < 4; faultByte++)
            Run(m, bus, report, form, bank, trace, alignment, vbr, value, ccr, faultByte, restart);
        report.Complete(output);
    }

    private static void Run(SyntheticMachine m, SyntheticM68040AccessDoubleFaultTests.FaultBus bus, CoverageBatch report,
        string form, string bank, ushort trace, uint alignment, uint vbr, uint value, int ccr, int faultByte, bool restart)
    {
        bus.Disarm(); m.Reset(ccr);
        foreach (var stack in new uint[] { 0x4700 + alignment, 0x7400 + alignment, 0x7800 + alignment })
            for (var n = -76; n < 80; n++) m.InitializePhysical(unchecked(stack + (uint)n), (uint)(n ^ 0x5a), 1);
        var moveq = (ushort)(0x7000 | (value & 255));
        ushort[] words = form switch {
            "extension-low" => [0x203c, (ushort)(value >> 16), (ushort)value, 0x60fe],
            "next-opcode" => [0x7201, 0x4e71, moveq, 0x60fe],
            "self-branch" => [0x4e71, 0x4e71, 0x4e71, 0x60fe],
            _ => [moveq, 0x60fe]
        };
        m.InitializePhysical(Handler, 0x4e73, 2); m.InitializePhysical(Handler + 2, 0x7e7e, 2);
        m.InitializePhysical(vbr + 8, Handler, 4);
        _ = SyntheticExecution.Prepare(m, words);
        if (form == "next-opcode") { m.Core.ExecuteInstruction(); m.Core.ExecuteInstruction(); }
        var instructionPc = form == "next-opcode" ? 0x1004u : form == "self-branch" ? 0x1006u : 0x1000u;
        var fetchAddress = form is "self-branch" or "extension-low" or "next-opcode" ? 0x1004u : 0x1000u;
        var nextPc = form is "extension-low" or "next-opcode" or "self-branch" ? 0x1006u : 0x1002u;
        var sr = (ushort)((bank == "MSP" ? 0x3000 : bank == "ISP" ? 0x2000 : 0) | trace | ccr);
        m.Core.State.SetUserStackPointer(0x7800 + alignment);
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment); m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = sr; m.Core.State.ProgramCounter = instructionPc; m.Core.State.VectorBaseRegister = vbr;
        var e = ArchitecturalExpectation.Capture(m);
        var sequence = m.Core.State.ExceptionSequence;
        var superStack = bank == "MSP" ? 0x7400u + alignment : 0x4700u + alignment;
        var frame = superStack - 60;
        e.Sr = (ushort)((sr | 0x2000) & ~0xc000); e.A[7] = frame;
        e.InactiveStackPointer = 0x7800 + alignment; if (bank == "MSP") e.MasterStackPointer = frame;
        e.Pc = Handler; e.ExceptionVector = 2;
        e.ControlChecks["saved instruction PC"] = (s => s.LastExceptionStackedProgramCounter, instructionPc);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, sr);
        e.ControlChecks["single access error"] = (s => s.ExceptionSequence, sequence + 1);
        e.ControlChecks["rejected one long prefetch"] = (_ => bus.Rejected.Count == 1 &&
            bus.Rejected[0].Address == fetchAddress && bus.Rejected[0].Width == 4 &&
            bus.Rejected[0].Kind == M68kBusAccessKind.CpuInstructionFetch ? 1u : 0u, 1);
        e.ControlChecks["no translation bypass leak"] = (s => s.M68040Mmu.BypassTranslation ? 1u : 0u, 0);
        e.Write(frame, sr, 2, m.Model); e.Write(frame + 2, instructionPc, 4, m.Model); e.Write(frame + 6, 0x7008, 2, m.Model);
        for (uint n = 8; n < 60; n++) e.MemoryMasks[frame + n] = 0;
        e.Write(frame + 12, bank == "user" ? 0x0102u : 0x0106u, 2, m.Model);
        e.MemoryMasks[frame + 12] = 255; e.MemoryMasks[frame + 13] = 0x7f; // X undefined.
        foreach (var offset in new uint[] { 14, 16, 18 }) {
            e.Write(frame + offset, 0, 2, m.Model); e.MemoryMasks[frame + offset + 1] = 0x80;
        }
        e.Write(frame + 20, fetchAddress, 4, m.Model);
        for (uint n = 20; n < 24; n++) e.MemoryMasks[frame + n] = 255;
        bus.Arm(fetchAddress + (uint)faultByte, M68kBusAccessKind.CpuInstructionFetch);
        var id = $"68040/access-error/instruction/{form}/bank={bank}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/value={value:X8}/byte={faultByte}/op={words[form == "next-opcode" ? 2 : form == "self-branch" ? 3 : 0]:X4}/ccr={ccr:X2}";
        var phases = restart ? new[] { "fault-entry", "handler-return", "restart", "following" } : ["fault-entry", "handler-return"];
        var phase = 0;
        bool Step() {
            var ok = SyntheticM68040AccessFrameAuditTests.Step(m, e, report, id + "/" + phases[phase++]);
            if (!ok) while (phase < phases.Length) report.Record(id + "/" + phases[phase++], "untested", "Instruction fault prerequisite failed");
            return ok;
        }
        if (!Step()) return;
        e.Sr = sr; e.A[7] = bank == "user" ? 0x7800 + alignment : superStack;
        e.InactiveStackPointer = bank == "user" ? superStack : 0x7800 + alignment;
        if (bank == "MSP") e.MasterStackPointer = superStack;
        e.Pc = instructionPc; e.ExceptionVector = null;
        if (!Step() || !restart) return;
        e.Pc = nextPc;
        if (form != "self-branch") { e.D[0] = value; e.Sr = SyntheticExecution.MoveFlags(sr, value, 4); }
        if (!Step()) return;
        Step();
    }
}
