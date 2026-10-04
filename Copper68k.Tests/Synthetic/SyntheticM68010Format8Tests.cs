using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68010Format8Tests(ITestOutputHelper output)
{
    [Fact]
    public void GeneratedAddressErrorAllocatesLongFrame()
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        machine.Reset(31);
        machine.InitializePhysical(0x1000, 0x3010, 2); // MOVE.W (A0),D0
        machine.Core.State.A[0] = 0x4001;
        machine.Start();
        var stack = machine.Core.State.A[7];
        machine.Core.ExecuteInstruction();
        Assert.Equal(stack - 58, machine.Core.State.A[7]);
        Assert.Equal(0x800cu, machine.PeekPhysical(stack - 52, 2));
    }

    [Fact]
    public void Format8RteRejectsDifferentProcessorVersionBeforePoppingFrame()
    {
        var machine = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        machine.Reset(16);
        var stack = machine.Core.State.A[7];
        machine.InitializePhysical(0x1000, 0x4e73, 2);
        machine.InitializePhysical(stack, 0x001f, 2);
        machine.InitializePhysical(stack + 2, 0x6000, 4);
        machine.InitializePhysical(stack + 6, 0x800c, 2);
        machine.InitializePhysical(stack + 26, 0x0400, 2); // Version 1; emulated version is 0.
        machine.InitializePhysical(0x6000, 0x4e71, 2);
        machine.Start();
        machine.Core.ExecuteInstruction();
        Assert.Equal(14, machine.Core.State.LastExceptionVector);
        Assert.Equal(0x90e0u, machine.Core.State.ProgramCounter);
        Assert.Equal(stack - 8, machine.Core.State.A[7]);
        Assert.Equal(0x001fu, machine.PeekPhysical(stack, 2));
        Assert.Equal(0x800cu, machine.PeekPhysical(stack + 6, 2));
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void AddressErrorInformationWordsAndReservedHoles()
    {
        var m = NewMachine();
        var report = new CoverageBatch("68010", "system-format8-entry");
        foreach (var kind in new[] { "read-word", "read-long", "write-word", "write-long", "movea-word", "pre-read", "pre-write", "fetch" })
        foreach (var supervisor in new[] { false, true })
        foreach (var trace in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/format8/entry/{kind}/super={supervisor}/trace={trace}/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                m.Reset(ccr, supervisor);
                if (trace) m.Core.State.StatusRegister |= 0x8000;
                // Logical fault address retains its high byte; physical transfers wrap to 24 bits.
                m.Core.State.A[0] = kind == "pre-read" ? 0xab004003u : kind == "pre-write" ? 0xab004005u : 0xab004001u;
                m.Core.State.D[0] = 0x80011234;
                var opcode = kind switch
                {
                    "read-word" => 0x3010, "read-long" => 0x2010, "write-word" => 0x3080,
                    "write-long" => 0x2080, "movea-word" => 0x3250, "pre-read" => 0x3020,
                    "pre-write" => 0x2100, _ => 0x4ed0
                };
                m.InitializePhysical(0x4000, 0x5aa55aa5, 4);
                // Mark the complete frame and its surroundings, including the three reserved holes.
                var stack = supervisor ? 0x4700u : 0x8000u;
                for (uint offset = 0; offset < 66; offset += 2) m.InitializePhysical(stack - 62 + offset, 0xbeef, 2);
                var e = SyntheticExecution.Prepare(m, [(ushort)opcode]);
                var savedSr = e.Sr;
                // Preserve the existing MOVE fault-side-effect policy, separately from physical qualification.
                if (kind == "write-word") savedSr = (ushort)((savedSr & 0xfff0) | 0);
                if (kind == "pre-write") savedSr = (ushort)((savedSr & 0xfff0) | 8);
                // Retained descending MOVE.L write policy commits its base only after a successful write.
                if (kind == "pre-read") e.A[0] = 0xab004001;
                SwitchToSupervisorStack(e);
                e.Sr = (ushort)((savedSr | 0x2000) & ~0x8000);
                e.ExceptionVector = 3; e.Pc = 0x9030; e.A[7] -= 58;
                var fp = e.A[7];
                e.Write(fp, savedSr, 2, m.Model); e.Write(fp + 2, SyntheticMachine.Code, 4, m.Model);
                e.Write(fp + 6, 0x800c, 2, m.Model);
                var write = kind is "write-word" or "write-long" or "pre-write";
                var faultAddress = kind == "pre-write" ? 0xab004003u : 0xab004001u;
                var ssw = (uint)((kind == "fetch" ? 0x2002 : 0x1001) | (write ? 0 : 0x0100) | (supervisor ? 4 : 0));
                e.Write(fp + 8, ssw, 2, m.Model); e.Write(fp + 10, faultAddress, 4, m.Model);
                // Input/internal words are implementation placeholders, not qualified restart state.
                e.Write(fp + 16, write ? kind == "write-long" ? 0x8001u : 0x1234u : 0, 2, m.Model);
                e.Write(fp + 20, 0, 2, m.Model); e.Write(fp + 24, 0, 2, m.Model);
                for (uint offset = 26; offset < 58; offset += 2)
                {
                    if (kind is "read-word" or "write-word" or "movea-word" or "pre-read")
                    {
                        // Private continuation contents have their own restart gate.
                        e.MemoryMasks[m.Model.Physical(fp + offset)] = 0;
                        e.MemoryMasks[m.Model.Physical(fp + offset + 1)] = 0;
                    }
                    else e.Write(fp + offset, 0, 2, m.Model);
                }
                m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m);
                if (mismatch != null) return mismatch;
                var writes = m.Bus.Accesses.Where(a => a.Write).ToArray();
                if (writes.Length != 26 || writes.Any(a => a.Width != 2 || a.Address < fp || a.Address >= fp + 58))
                    return "Frame must write exactly 26 information words";
                if (writes.Any(a => a.Address == fp + 14 || a.Address == fp + 18 || a.Address == fp + 22))
                    return "Reserved frame word was written";
                if (m.Bus.Accesses.Any(a => a.Address is 0x4001 or 0x4003)) return "Faulting transfer reached the external bus";
                return null;
            });
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void RteVersionValidationTailProbeAndStackSelection()
    {
        var m = NewMachine();
        var report = new CoverageBatch("68010", "system-format8-rte");
        for (var version = 0; version < 16; version++)
        foreach (var otherBits in new[] { 0, 0x03ff, 0xc000, 0xc3ff })
        foreach (var restoredSupervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/format8/RTE/version={version}/other={otherBits:X4}/restoreSuper={restoredSupervisor}/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                m.Reset(ccr);
                var fp = m.Core.State.A[7];
                for (uint offset = 0; offset < 62; offset += 2) m.InitializePhysical(fp + offset, 0x5a5a, 2);
                var restoredSr = (ushort)((restoredSupervisor ? 0x2700 : 0x0700) | (ccr ^ 31));
                m.InitializePhysical(fp, restoredSr, 2); m.InitializePhysical(fp + 2, 0x6000, 4);
                m.InitializePhysical(fp + 6, 0x800c, 2);
                m.InitializePhysical(fp + 26, (uint)((version << 10) | otherBits), 2);
                m.InitializePhysical(0x6000, 0x747b, 2); m.InitializePhysical(0x6002, 0x4e71, 2);
                var e = SyntheticExecution.Prepare(m, [0x4e73]);
                if (version == 0)
                {
                    e.A[7] += 58; SyntheticSystemTests.ApplyStatus(m, e, restoredSr); e.Pc = 0x6000;
                }
                else SyntheticExecution.ExpectException(m, e, 14);
                m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m);
                if (mismatch != null) return mismatch;
                var reads = m.Bus.Accesses.Where(a => !a.Write && a.Kind == M68kBusAccessKind.CpuDataRead && a.Address >= fp && a.Address < fp + 58).ToArray();
                // Header first, version next, then last word before the rest. Reserved holes are not information words.
                var offsets = new List<(uint Address, int Width)> { (fp, 2), (fp + 2, 4), (fp + 6, 2), (fp + 26, 2) };
                if (version == 0)
                {
                    offsets.Add((fp + 56, 2));
                    for (uint offset = 8; offset < 56; offset += 2)
                        if (offset is not (14 or 18 or 22 or 26)) offsets.Add((fp + offset, 2));
                }
                if (!reads.Select(a => (a.Address, a.Width)).SequenceEqual(offsets)) return "RTE frame validation/probe read order differs";
                if (version == 0)
                {
                    m.Core.ExecuteInstruction(); e.D[2] = 123; e.Pc += 2;
                    e.Sr = (ushort)((e.Sr & 0xfff0) | 0); // MOVEQ #123,D2
                    mismatch = e.Verify(m);
                }
                return mismatch;
            });
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void DoubleFaultHaltsUntilReset()
    {
        var m = NewMachine();
        var report = new CoverageBatch("68010", "system-format8-double-fault");
        foreach (var oddStack in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        foreach (var trace in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/format8/double-fault/oddStack={oddStack}/super={supervisor}/trace={trace}/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                m.Reset(ccr, supervisor);
                if (trace) m.Core.State.StatusRegister |= 0x8000;
                m.Core.State.A[0] = 0x4001;
                if (!oddStack) m.InitializePhysical(12, 0x9031, 4);
                _ = SyntheticExecution.Prepare(m, [0x3010]);
                if (oddStack)
                {
                    if (supervisor) m.Core.State.SetActiveStackPointer(0x4701);
                    else m.Core.State.SetInterruptStackPointer(0x8001);
                }
                var e = ArchitecturalExpectation.Capture(m);
                SwitchToSupervisorStack(e);
                e.A[7] -= oddStack ? 2u : 58u;
                e.Sr = (ushort)((e.Sr | 0x2000) & ~0x8000);
                e.Pc = oddStack ? 0x1002u : 0x9031u; e.Halted = true; e.ExceptionVector = 3;
                if (!oddStack)
                {
                    var savedSr = m.Core.State.StatusRegister;
                    var fp = e.A[7];
                    for (uint offset = 0; offset < 58; offset += 2)
                        if (offset is not (14 or 18 or 22)) e.Write(fp + offset, 0, 2, m.Model);
                    e.Write(fp, savedSr, 2, m.Model); e.Write(fp + 2, 0x1000, 4, m.Model);
                    e.Write(fp + 6, 0x800c, 2, m.Model);
                    e.Write(fp + 8, supervisor ? 0x1105u : 0x1101u, 2, m.Model);
                    e.Write(fp + 10, 0x4001, 4, m.Model);
                    for (uint offset = 26; offset < 58; offset++) e.MemoryMasks[m.Model.Physical(fp + offset)] = 0;
                }
                m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m);
                if (mismatch != null) return mismatch;
                var accesses = m.Bus.Accesses.Count;
                m.Core.ExecuteInstruction(); m.Core.RequestInterrupt(7, 31 * 4);
                m.Core.BeginSubroutine(0x5000, 0x6000, 0x7000); m.Core.SwitchTaskContext(new M68kCpuState());
                if (m.Bus.Accesses.Count != accesses) return "Halted CPU accessed the bus";
                mismatch = e.Verify(m); if (mismatch != null) return mismatch;
                m.InitializePhysical(0x5000, 0x747b, 2); m.InitializePhysical(0x5002, 0x4e71, 2);
                m.Core.Reset(0x5000, 0x8000);
                var reset = ArchitecturalExpectation.Capture(m); reset.Pc = 0x5002; reset.D[2] = 123;
                m.Core.ExecuteInstruction(); return reset.Verify(m);
            });
        }
        report.Complete(output);
    }

    private static SyntheticMachine NewMachine() => new(ModelSpec.All.Single(x => x.Id == "68010"));

    private static void SwitchToSupervisorStack(ArchitecturalExpectation e)
    {
        if ((e.Sr & 0x2000) != 0) return;
        var usp = e.A[7]; e.A[7] = e.InactiveStackPointer!.Value; e.InactiveStackPointer = usp;
    }

    private static void Record(CoverageBatch report, string id, Func<string?> run)
    {
        try { var mismatch = run(); report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch); }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException)
        { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
    }
}
