using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68010LongMoveRestartTests(ITestOutputHelper output)
{
    [Fact]
    public void SourceWordsContinueWithoutRepeatingPostincrement()
    {
        var m = Machine(0x2018); // MOVE.L (A0)+,D0
        m.Core.State.A[0] = 0x4001;
        m.Start(); m.Core.ExecuteInstruction();
        Assert.Equal(0x4001u, m.Core.State.A[0]);
        SupplyRead(m, 0x8001);
        m.Core.ExecuteInstruction();
        Assert.Equal(0x9030u, m.Core.State.ProgramCounter);
        Assert.Equal(0x4003u, m.PeekPhysical(m.Core.State.A[7] + 10, 4));
        Assert.Equal(0x4001u, m.Core.State.A[0]);
        Assert.Equal(0xa55a0022u, m.Core.State.D[0]);
        SupplyRead(m, 0x7fff);
        m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
        Assert.Equal(0x80017fffu, m.Core.State.D[0]);
        Assert.Equal(0x4005u, m.Core.State.A[0]);
        Assert.Equal(0x1002u, m.Core.State.ProgramCounter);
        Assert.Equal(0x2718, m.Core.State.StatusRegister);
        Assert.DoesNotContain(m.Bus.Accesses, a => a.Address is 0x4001 or 0x4003);
    }

    [Fact]
    public void DescendingWriteRetainsSourceAndCommitsBaseOnce()
    {
        var m = Machine(0x2300); // MOVE.L D0,-(A1)
        m.Core.State.D[0] = 0x80017fff; m.Core.State.A[1] = 0x4105;
        m.Start(); m.Core.ExecuteInstruction();
        Assert.Equal(0x4105u, m.Core.State.A[1]);
        Assert.Equal(0x4103u, m.PeekPhysical(m.Core.State.A[7] + 10, 4));
        CompleteWrite(m, 0x7fff);
        m.Core.State.D[0] = 0x12345678;
        m.Core.ExecuteInstruction();
        Assert.Equal(0x9030u, m.Core.State.ProgramCounter);
        Assert.Equal(0x4101u, m.PeekPhysical(m.Core.State.A[7] + 10, 4));
        Assert.Equal(0x8001u, m.PeekPhysical(m.Core.State.A[7] + 16, 2));
        Assert.Equal(0x4105u, m.Core.State.A[1]);
        CompleteWrite(m, 0x8001);
        m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
        Assert.Equal(0x4101u, m.Core.State.A[1]);
        Assert.Equal(0x80017fffu, m.PeekPhysical(0x4101, 4));
        Assert.Equal(0x12345678u, m.Core.State.D[0]);
        Assert.Equal(0x1002u, m.Core.State.ProgramCounter);
        Assert.DoesNotContain(m.Bus.Accesses, a => a.Address is 0x4101 or 0x4103);
    }

    private static SyntheticMachine Machine(ushort opcode)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        m.Reset(0x10); m.InitializePhysical(0x1000, opcode, 2);
        m.InitializePhysical(0x1002, 0x7c7b, 2); m.InitializePhysical(0x1004, 0x4e71, 2);
        m.InitializePhysical(0x9030, 0x4e73, 2); m.InitializePhysical(0x9032, 0x4e71, 2);
        return m;
    }

    private static void SupplyRead(SyntheticMachine m, ushort value)
    {
        var fp = m.Core.State.A[7];
        m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
        m.InitializePhysical(fp + 20, value, 2);
    }

    private static void CompleteWrite(SyntheticMachine m, ushort value)
    {
        var fp = m.Core.State.A[7];
        m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
        m.InitializePhysical(m.PeekPhysical(fp + 10, 4), value, 2);
    }

    private static readonly uint[] Values = [0, 1, 0x7fffffff, 0x80000000, 0x80017fff, 0xffffffff, 0xffff0000, 0x0000ffff];
    private static readonly OperandForm[] MemorySources = [new(2,0), new(3,0), new(4,0), new(5,0), new(6,0), new(7,0), new(7,1), new(7,2), new(7,3)];
    private static readonly OperandForm[] MemoryDestinations = [new(2,1), new(3,1), new(4,1), new(5,1), new(6,1), new(7,0), new(7,1)];

    [Fact, Trait("Suite", "Synthetic")]
    public void SourceFaultOperandFormsValuesAndCcr()
    {
        var report = new CoverageBatch("68010", "system-move-long-restart-source");
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        foreach (var source in MemorySources)
        foreach (var destination in MemoryDestinations.Concat([new OperandForm(0,2), new OperandForm(1,2)]))
        foreach (var value in Values)
        for (var rr = 0; rr < 4; rr++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            Transfer(m, report, source, destination, value, ccr, supervisor, rr, false);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void DestinationFaultOperandFormsValuesAndCcr()
    {
        var report = new CoverageBatch("68010", "system-move-long-restart-destination");
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        foreach (var source in MemorySources.Concat([new OperandForm(0,2), new OperandForm(1,2), new OperandForm(7,4)]))
        foreach (var destination in MemoryDestinations)
        foreach (var value in Values)
        for (var rr = 0; rr < 4; rr++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            Transfer(m, report, source, destination, value, ccr, supervisor, rr, true);
        report.Complete(output);
    }

    private static void Transfer(SyntheticMachine m, CoverageBatch report, OperandForm source, OperandForm destination,
        uint value, int ccr, bool supervisor, int rr, bool writeFault)
    {
        var opcode = MoveSpecification.Encode(4, source, destination);
        var id = $"68010/MOVE.L/restart/{(writeFault ? "destination" : "source")}/{source.Id}/{destination.Id}/super={supervisor}/RR={rr}/op={opcode:X4}/value={value:X8}/ccr={ccr:X2}";
        Record(report, id, () =>
        {
            m.Reset(ccr, supervisor);
            if (source.Mode == 0) m.Core.State.D[2] = value;
            if (source.Mode == 1) m.Core.State.A[2] = value;
            if (!writeFault && source.Mode is 2 or 3 or 4) m.Core.State.A[0] = source.Mode == 4 ? 0x4005u : 0x4001u;
            if (writeFault && destination.Mode is 2 or 3 or 4) m.Core.State.A[1] = destination.Mode == 4 ? 0x4105u : 0x4101u;
            var words = new List<ushort> { opcode };
            var finalD = (uint[])m.Core.State.D.Clone(); var finalA = (uint[])m.Core.State.A.Clone();
            var options = new AddressOptions(
                SourceDisplacement: (short)(writeFault ? 0x40 : 0x41), DestinationDisplacement: (short)(writeFault ? 0x61 : 0x60),
                SourceAbsoluteWord: (ushort)(writeFault ? 0x6000 : 0x6001), DestinationAbsoluteWord: (ushort)(writeFault ? 0x6101 : 0x6100),
                SourceAbsoluteLong: writeFault ? 0xab006200u : 0xab006201u, DestinationAbsoluteLong: writeFault ? 0xab006301u : 0xab006300u,
                SourceIndex: new(BriefDisplacement: (sbyte)(writeFault ? 0x40 : 0x41)),
                DestinationIndex: new(BriefDisplacement: (sbyte)(writeFault ? 0x61 : 0x60)));
            var fixture = new AddressingFixture(m, 4, words, finalD, finalA, value, options);
            var sourceAddress = source.Mode >= 2 ? fixture.Resolve(source, true) : 0;
            var destinationAddress = destination.Mode >= 2 ? fixture.Resolve(destination, false) : 0;
            if (source.Mode >= 2 && source != new OperandForm(7,4)) m.InitializePhysical(sourceAddress, value, 4);
            var nextPc = fixture.NextPc;
            m.InitializePhysical(nextPc, 0x7c7b, 2); m.InitializePhysical(nextPc + 2, 0x4e71, 2);
            m.InitializePhysical(0x9030, 0x4e73, 2); m.InitializePhysical(0x9032, 0x4e71, 2);
            for (var i = 0; i < words.Count; i++) m.InitializePhysical(0x1000 + (uint)i * 2, words[i], 2);
            m.Start(); m.Core.ExecuteInstruction();
            if (m.Core.State.LastExceptionVector != 3 || m.Core.State.ProgramCounter != 0x9030) return "Missing initial address error";
            var fp = m.Core.State.A[7]; var savedSr = (ushort)m.PeekPhysical(fp, 2);
            var originalSr = (ushort)((supervisor ? 0x2700 : 0x0700) | ccr);
            if ((savedSr & 0xfff0) != (originalSr & 0xfff0) || (destination.Mode == 1 && savedSr != originalSr))
                return "Initial fault changed preserved SR bits or MOVEA flags";
            var descending = writeFault && destination.Mode == 4;
            var operand = writeFault ? destinationAddress : sourceAddress;
            var firstFault = descending ? operand + 2 : operand;
            var secondFault = descending ? operand : operand + 2;
            var phase = writeFault ? descending ? 4u : 2u : 0u;
            var firstValue = (ushort)(descending ? value : value >> 16);
            var secondValue = (ushort)(descending ? value >> 16 : value);
            if (m.PeekPhysical(fp + 28, 2) != 0xc110 || m.PeekPhysical(fp + 30, 2) != opcode ||
                m.PeekPhysical(fp + 32, 2) != (writeFault ? 1u : 0u) || m.PeekPhysical(fp + 48, 2) != phase ||
                m.PeekPhysical(fp + 50, 4) != (writeFault ? value : 0) || m.PeekPhysical(fp + 54, 4) != operand ||
                m.PeekPhysical(fp + 10, 4) != firstFault) return "Incorrect first pending cycle image";
            for (var r = 0; r < 8; r++)
            {
                if (m.Core.State.D[r] != finalD[r]) return $"D{r} changed before first fault";
                if (r == 7) continue;
                var expected = finalA[r];
                if (!writeFault && source.Mode == 3 && source.Register == r) expected -= 4;
                if (destination.Mode == 3 && destination.Register == r) expected -= 4;
                if (destination.Mode == 4 && destination.Register == r) expected += 4;
                if (m.Core.State.A[r] != expected) return $"A{r} changed incorrectly before first fault";
            }
            // Once the source has completed, handler changes cannot cause a re-read.
            if (writeFault)
            {
                if (source.Mode == 0) { m.Core.State.D[2] ^= 0xffffffff; finalD[2] ^= 0xffffffff; }
                else if (source.Mode == 1) { m.Core.State.A[2] ^= 0xffffffff; finalA[2] ^= 0xffffffff; }
                else if (source != new OperandForm(7,4)) m.InitializePhysical(sourceAddress, value ^ 0xffffffff, 4);
            }
            var firstSoftware = (rr & 1) != 0; var secondSoftware = (rr & 2) != 0;
            var firstAddress = PrepareCycle(m, fp, firstFault, firstValue, firstSoftware, writeFault, source);
            var intermediate = ArchitecturalExpectation.Capture(m);
            intermediate.Write(fp + 8, m.PeekPhysical(fp + 8, 2) & 0x7fff, 2, m.Model);
            intermediate.Write(fp + 10, secondFault, 4, m.Model);
            intermediate.Write(fp + 16, writeFault ? secondValue : 0u, 2, m.Model);
            intermediate.Write(fp + 20, 0, 2, m.Model); intermediate.Write(fp + 24, 0, 2, m.Model);
            intermediate.Write(fp + 48, phase + 1, 2, m.Model);
            intermediate.Write(fp + 50, writeFault ? value : value & 0xffff0000, 4, m.Model);
            if (writeFault && !firstSoftware) intermediate.Write(firstAddress, firstValue, 2, m.Model);
            m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
            var mismatch = intermediate.Verify(m); if (mismatch != null) return "second-fault: " + mismatch;
            if (m.Core.State.LastExceptionVector != 3 || m.PeekPhysical(fp + 2, 4) != 0x1000) return "Second cycle did not suspend original instruction";
            if (!CycleCount(m, firstAddress, firstFault, firstSoftware, writeFault)) return "First word repeated or missing";
            var secondAddress = PrepareCycle(m, fp, secondFault, secondValue, secondSoftware, writeFault, source);
            var e = ArchitecturalExpectation.Capture(m);
            e.A[7] = fp + 58; SyntheticSystemTests.ApplyStatus(m, e, savedSr);
            for (var r = 0; r < 7; r++) e.A[r] = finalA[r];
            finalD.CopyTo(e.D, 0);
            if (!writeFault && destination.Mode == 0) e.D[destination.Register] = value;
            if (!writeFault && destination.Mode == 1) e.A[destination.Register] = value;
            if (destination.Mode != 1) e.Sr = SyntheticExecution.MoveFlags(e.Sr, value, 4);
            e.Pc = nextPc;
            if (writeFault && !secondSoftware) e.Write(secondAddress, secondValue, 2, m.Model);
            if (!writeFault && destination.Mode >= 2) e.Write(destinationAddress, value, 4, m.Model);
            m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
            mismatch = e.Verify(m); if (mismatch != null) return "completion: " + mismatch;
            if (!CycleCount(m, secondAddress, secondFault, secondSoftware, writeFault)) return "Second word repeated or missing";
            if (writeFault && source.Mode >= 2 && source != new OperandForm(7,4) && m.Bus.Accesses.Any(a => a.Address == m.Model.Physical(sourceAddress)))
                return "Completed source was re-read";
            m.Core.ExecuteInstruction(); e.D[6] = 123; e.Pc += 2; e.Sr = (ushort)(e.Sr & 0xfff0);
            return e.Verify(m);
        });
    }

    private static uint PrepareCycle(SyntheticMachine m, uint fp, uint fault, ushort value, bool software, bool write, OperandForm source)
    {
        if (software) m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
        else m.InitializePhysical(fp + 10, fault + 1, 4);
        var address = software ? fault : fault + 1;
        if (write) { if (software) m.InitializePhysical(address, value, 2); }
        else
        {
            var instruction = source.Mode == 7 && source.Register is 2 or 3;
            m.InitializePhysical(fp + 20, instruction ? (uint)(value ^ 0xffff) : value, 2);
            m.InitializePhysical(fp + 24, instruction ? value : (uint)(value ^ 0xffff), 2);
            if (!software) m.InitializePhysical(address, value, 2);
        }
        return address;
    }

    private static bool CycleCount(SyntheticMachine m, uint address, uint fault, bool software, bool write)
        => !m.Bus.Accesses.Any(a => a.Address == m.Model.Physical(fault)) &&
           m.Bus.Accesses.Count(a => a.Address == m.Model.Physical(address) && a.Width == 2 && a.Write == write) == (software ? 0 : 1);

    [Fact, Trait("Suite", "Synthetic")]
    public void CopiedNestedAliasedPrefetchedAndTracedContinuations()
    {
        var report = new CoverageBatch("68010", "system-move-long-restart-edges");
        foreach (var kind in new[] { "copied", "nested", "alias", "prefetched", "trace" })
        for (var rr = 0; rr < 16; rr++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/MOVE.L/restart/{kind}/super={supervisor}/RR={rr}/op=20/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                var memory = kind is "nested" or "alias";
                var opcode = kind == "alias" ? (ushort)0x20d8 : kind == "nested" ? (ushort)0x22d8 : (ushort)0x2418;
                var m = Machine(opcode);
                m.Core.State.StatusRegister = (ushort)((supervisor ? 0x2700 : 0x0700) | ccr | (kind == "trace" ? 0x8000 : 0));
                m.Core.State.SetActiveStackPointer(supervisor ? 0x4700u : 0x7800u);
                m.Core.State.A[0] = 0x4001; m.Core.State.A[1] = 0x4101;
                m.Start(); m.Core.ExecuteInstruction();
                var fp = m.Core.State.A[7];
                if (kind == "copied")
                {
                    for (uint offset = 0; offset < 58; offset += 2) m.InitializePhysical(0x6800 + offset, m.PeekPhysical(fp + offset, 2), 2);
                    // Execute an unrelated fault in the handler before returning
                    // to the copied image. Its continuation cannot overwrite this one.
                    m.InitializePhysical(0x5000, 0x2011, 2); m.InitializePhysical(0x5002, 0x4e71, 2);
                    m.Core.SwitchTaskContext(m.Core.State); // discard current prefetch through public API
                    m.Core.State.ProgramCounter = 0x5000;
                    m.Core.ExecuteInstruction();
                    if (m.Core.State.LastExceptionVector != 3 || m.PeekPhysical(m.Core.State.A[7] + 10, 4) != 0x4101) return "Nested handler fault fixture failed";
                    fp = 0x6800; m.Core.State.SetActiveStackPointer(fp);
                }
                var pending = memory ? 4 : 2;
                var destination = kind == "alias" ? 0x4005u : 0x4101u;
                var writtenBytes = new Dictionary<uint, byte>();
                for (var phase = 0; phase < pending; phase++)
                {
                    var writing = phase >= 2; var low = (phase & 1) != 0;
                    var fault = (writing ? destination : 0x4001u) + (low ? 2u : 0);
                    if (m.Core.State.ProgramCounter != 0x9030 || m.PeekPhysical(fp + 10, 4) != fault) return $"Pending cycle {phase} address differs";
                    var value = low ? (ushort)0x7fff : (ushort)0x8001;
                    var software = (rr & (1 << phase)) != 0;
                    var address = PrepareCycle(m, fp, fault, value, software, writing, new(3,0));
                    if (writing)
                    {
                        writtenBytes[m.Model.Physical(address)] = (byte)(value >> 8);
                        writtenBytes[m.Model.Physical(address + 1)] = (byte)value;
                    }
                    if (kind == "prefetched") m.InitializePhysical(0x1002, 0x7cff, 2);
                    var e = ArchitecturalExpectation.Capture(m);
                    var sr = (ushort)m.PeekPhysical(fp, 2);
                    if (phase == pending - 1)
                    {
                        e.A[7] = fp + 58; SyntheticSystemTests.ApplyStatus(m, e, sr);
                        e.A[0] = kind == "alias" ? 0x4009u : 0x4005u;
                        if (kind == "nested") e.A[1] = 0x4105;
                        if (!memory) e.D[2] = 0x80017fff;
                        e.Sr = SyntheticExecution.MoveFlags(e.Sr, 0x80017fff, 4); e.Pc = 0x1002;
                        if (writing && !software) e.Write(address, value, 2, m.Model);
                        if (kind == "trace") SyntheticExecution.ExpectException(m, e, 9, 0x1002);
                        m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
                        var mismatch = e.Verify(m); if (mismatch != null) return "completion: " + mismatch;
                        if (!CycleCount(m, address, fault, software, writing)) return "Final cycle count differs";
                        if (kind != "trace")
                        {
                            m.Core.ExecuteInstruction(); e.D[6] = 123; e.Pc += 2; e.Sr = (ushort)(e.Sr & 0xfff0);
                            mismatch = e.Verify(m); if (mismatch != null) return "sentinel: " + mismatch;
                        }
                    }
                    else
                    {
                        m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction(); fp = m.Core.State.A[7];
                        if (!CycleCount(m, address, fault, software, writing)) return $"Cycle {phase} repeated or missing";
                        if (m.Core.State.A[0] != (phase < 1 ? 0x4001u : 0x4005u)) return "Source postincrement repeated or committed early";
                        if (m.Core.State.A[1] != 0x4101 || m.Core.State.D[2] != 0xa55a0222) return "Destination changed before its complete transfer";
                        if (phase == 1 && memory && m.PeekPhysical(fp + 50, 4) != 0x80017fff) return "Completed source lost before destination fault";
                    }
                }
                // Redirecting just one word can overlap the other word. The later
                // transfer must win byte by byte, in the declared transfer order.
                if (writtenBytes.Any(pair => m.Bus.Peek(pair.Key) != pair.Value)) return "Destination byte contents differ";
                return null;
            });
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void UserStackLongOperandsPreserveSupervisorBank()
    {
        var report = new CoverageBatch("68010", "system-move-long-restart-a7");
        foreach (var kind in new[] { "source-post", "source-pre", "dest-post", "dest-pre" })
        for (var rr = 0; rr < 4; rr++)
        for (var ccr = 0; ccr < 32; ccr++)
        Record(report, $"68010/MOVE.L/restart/A7/{kind}/RR={rr}/op=20/ccr={ccr:X2}", () =>
        {
            var writing = kind.StartsWith("dest"); var pre = kind.EndsWith("pre");
            var source = writing ? new OperandForm(0,0) : new OperandForm(pre ? 4 : 3,7);
            var destination = writing ? new OperandForm(pre ? 4 : 3,7) : new OperandForm(0,0);
            var m = Machine(MoveSpecification.Encode(4, source, destination));
            m.Core.State.StatusRegister = (ushort)(0x0700 | ccr);
            m.Core.State.SetActiveStackPointer(pre ? 0x7805u : 0x7801u); m.Core.State.D[0] = 0x80017fff;
            m.Start(); m.Core.ExecuteInstruction();
            var fp = m.Core.State.A[7];
            if (fp != 0x7fc6 || m.Core.State.UserStackPointer != (pre && !writing ? 0x7801u : pre ? 0x7805u : 0x7801u)) return "Wrong stack bank or early A7 update";
            for (var phase = 0; phase < 2; phase++)
            {
                var low = writing && pre ? phase == 0 : phase == 1;
                var fault = 0x7801u + (low ? 2u : 0);
                if (m.PeekPhysical(fp + 10, 4) != fault) return "Wrong A7 word address";
                PrepareCycle(m, fp, fault, low ? (ushort)0x7fff : (ushort)0x8001, (rr & (1 << phase)) != 0, writing, source);
                var e = ArchitecturalExpectation.Capture(m);
                if (phase == 1)
                {
                    e.A[7] = fp + 58; SyntheticSystemTests.ApplyStatus(m, e, (ushort)m.PeekPhysical(fp, 2));
                    e.A[7] = pre ? 0x7801u : 0x7805u;
                    e.D[0] = 0x80017fff; e.Sr = SyntheticExecution.MoveFlags(e.Sr, 0x80017fff, 4); e.Pc = 0x1002;
                    if (writing && (rr & 2) == 0) e.Write(fault + 1, low ? 0x7fffu : 0x8001u, 2, m.Model);
                }
                m.Core.ExecuteInstruction();
                if (phase == 1) return e.Verify(m);
                if (m.Core.State.A[7] != fp || m.Core.State.ProgramCounter != 0x9030) return "A7 first word did not suspend";
            }
            return "Empty A7 continuation";
        });
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void InvalidPrivateContinuationPreservesFrameBeforeFormatError()
    {
        var report = new CoverageBatch("68010", "system-move-long-restart-invalid");
        foreach (var writing in new[] { false, true })
        foreach (var corruption in new[] { "version", "opcode", "direction", "phase", "pc", "queue-address", "queue-count", "operand-parity", "value", "descending" })
        for (var ccr = 0; ccr < 32; ccr++)
        Record(report, $"68010/MOVE.L/restart/invalid/{corruption}/write={writing}/op=20/ccr={ccr:X2}", () =>
        {
            var m = Machine(writing ? (ushort)0x2280 : (ushort)0x2010);
            m.Core.State.StatusRegister = (ushort)(0x2700 | ccr); m.Core.State.A[0] = 0x4001; m.Core.State.A[1] = 0x4101;
            m.Start(); m.Core.ExecuteInstruction(); var fp = m.Core.State.A[7];
            var (offset, value) = corruption switch
            {
                "version" => (26u, 0x0400u), "opcode" => (30u, 0x4e71u), "direction" => (32u, writing ? 0u : 1u),
                "phase" => (48u, 6u), "pc" => (36u, 0x1003u), "queue-address" => (40u, 0x1003u),
                "queue-count" => (46u, 3u), "operand-parity" => (56u, 0x4000u),
                "value" => writing ? (48u, 0u) : (52u, 1u), _ => (48u, writing ? 4u : 2u)
            };
            m.InitializePhysical(fp + offset, value, 2);
            var e = ArchitecturalExpectation.Capture(m); SyntheticExecution.ExpectException(m, e, 14, 0x9030);
            m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
            if (m.Bus.Accesses.Any(a => a.Address is 0x4000 or 0x4001 or 0x4101)) return "Invalid image executed operand cycle";
            return e.Verify(m);
        });
        report.Complete(output);
    }

    private static void Record(CoverageBatch report, string id, Func<string?> run)
    {
        try { var mismatch = run(); report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch); }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException)
        { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
    }
}
