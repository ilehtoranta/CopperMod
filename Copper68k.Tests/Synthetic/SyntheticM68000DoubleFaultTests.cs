using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68000UM 6.3.9.1 / 6.3.10: an address error during group-0
// exception processing halts the processor until external reset.
public sealed class SyntheticM68000DoubleFaultTests(ITestOutputHelper output)
{
    [Fact, Trait("Suite", "Synthetic")]
    public void AddressErrorEntryHaltingAndResetRecovery()
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68000"));
        var report = new CoverageBatch("68000", "system-double-fault");
        foreach (var kind in new[] { "odd-stack", "odd-handler", "trap-stack", "interrupt-stack", "handler-refault" })
        foreach (var supervisor in new[] { false, true })
        foreach (var trace in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68000/address-error/double-fault/{kind}/super={supervisor}/trace={trace}/ccr={ccr:X2}";
            try
            {
                m.Reset(ccr, supervisor);
                if (trace) m.Core.State.StatusRegister |= 0x8000;
                m.Core.State.A[0] = 0x2001;
                var handler = kind == "odd-handler" ? 0x9001u : 0x9000u;
                m.Bus.Initialize(3 * 4, handler, 4);
                m.Bus.Initialize(0x9000, 0x3010, 2); // valid handler instruction may fault again normally
                m.Bus.Initialize(0x9002, 0x4e71, 2);
                _ = SyntheticExecution.Prepare(m, [(ushort)(kind == "trap-stack" ? 0x4e40 : 0x3010)]);
                var oddStack = kind is "odd-stack" or "trap-stack" or "interrupt-stack";
                if (oddStack)
                {
                    if (supervisor) m.Core.State.SetActiveStackPointer(0x4701);
                    else m.Core.State.SetInterruptStackPointer(0x8001);
                }
                var e = ArchitecturalExpectation.Capture(m);
                var savedSr = e.Sr;
                if (!supervisor)
                {
                    var usp = e.A[7]; e.A[7] = e.InactiveStackPointer!.Value; e.InactiveStackPointer = usp;
                }
                e.Sr = (ushort)((savedSr | 0x2000) & ~0x8000);
                e.ExceptionVector = 3;
                e.ForbiddenOperandReads.Add(0x2001);
                e.Pc = kind == "interrupt-stack" ? SyntheticMachine.Code : SyntheticMachine.Code + 2;
                if (oddStack)
                    e.A[7] -= kind == "trap-stack" ? 8u : kind == "interrupt-stack" ? 10u : 4u;
                else
                {
                    AddressFrame(m, e, savedSr, SyntheticMachine.Code + 2);
                    e.Pc = handler;
                }
                e.Halted = kind != "handler-refault";
                if (kind == "interrupt-stack") m.Core.RequestInterrupt(7, 31 * 4);
                else m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m);
                if (mismatch == null && kind == "handler-refault")
                {
                    // Once a valid handler instruction begins, its operand fault
                    // starts a new exception; it is not a double fault.
                    AddressFrame(m, e, e.Sr, 0x9002);
                    m.Core.ExecuteInstruction();
                    mismatch = e.Verify(m);
                }
                if (mismatch == null && e.Halted)
                {
                    var accesses = m.Bus.Accesses.Count;
                    var lastOpcode = m.Core.State.LastOpcode;
                    m.Core.ExecuteInstruction();
                    m.Core.RequestInterrupt(7, 31 * 4);
                    m.Core.BeginSubroutine(0x5000, 0x6000, 0x7000);
                    m.Core.SwitchTaskContext(new M68kCpuState());
                    var batched = ((IM68kBatchCore)m.Core).ExecuteInstructions(10, m.Core.State.Cycles + 100, new Boundary());
                    mismatch = e.Verify(m);
                    if (mismatch == null && (batched != 0 || m.Bus.Accesses.Count != accesses || m.Core.State.LastOpcode != lastOpcode))
                        mismatch = "Halted CPU executed, accessed the bus or changed its opcode";
                }
                if (mismatch == null)
                {
                    m.Bus.Initialize(0x5000, 0x747b, 2); // MOVEQ #123,D2 after external reset
                    m.Bus.Initialize(0x5002, 0x4e71, 2);
                    m.Core.Reset(0x5000, 0x8000);
                    var reset = ArchitecturalExpectation.Capture(m);
                    reset.Pc = 0x5002; reset.D[2] = 123;
                    m.Core.ExecuteInstruction();
                    mismatch = reset.Verify(m);
                }
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException)
            { report.Record(id, "unsupported", ex.Message); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }

    private static void AddressFrame(SyntheticMachine m, ArchitecturalExpectation e, ushort savedSr, uint savedPc)
    {
        // Fixed reference fixture for MOVE.W (A0),D0 with A0=$2001.
        // Preserve every word of the first frame when handler entry halts.
        e.A[7] -= 14;
        e.Write(e.A[7], (savedSr & 0x2000) != 0 ? 0x3015u : 0x3011u, 2, m.Model);
        e.Write(e.A[7] + 2, 0x2001, 4, m.Model);
        e.Write(e.A[7] + 6, 0x3010, 2, m.Model);
        e.Write(e.A[7] + 8, savedSr, 2, m.Model);
        e.Write(e.A[7] + 10, savedPc, 4, m.Model);
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
    }
}
