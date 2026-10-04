using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68010MoveRestartTests(ITestOutputHelper output)
{
    [Fact]
    public void SoftwareCompletedReadUsesBufferWithoutRepeatingPostincrement()
    {
        var m = NewMachine(0x3018); // MOVE.W (A0)+,D0
        m.Core.State.A[0] = 0x4001;
        m.Start();
        m.Core.ExecuteInstruction();
        var fp = m.Core.State.A[7];
        Assert.Equal(0x4003u, m.Core.State.A[0]);
        m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
        m.InitializePhysical(fp + 20, 0x8001, 2);
        m.Bus.Accesses.Clear();
        m.Core.ExecuteInstruction(); // RTE completes the suspended MOVE, without decoding it again.
        Assert.Equal(0x1002u, m.Core.State.ProgramCounter);
        Assert.Equal(0xa55a8001u, m.Core.State.D[0]);
        Assert.Equal(0x4003u, m.Core.State.A[0]);
        Assert.Equal(0x2718, m.Core.State.StatusRegister);
        Assert.DoesNotContain(m.Bus.Accesses, a => a.Address == 0x4001);
    }

    [Fact]
    public void SoftwareCompletedWriteDoesNotReadSourceOrRepeatItsIncrement()
    {
        var m = NewMachine(0x32d8); // MOVE.W (A0)+,(A1)+
        m.Core.State.A[0] = 0x4000; m.Core.State.A[1] = 0x4101;
        m.InitializePhysical(0x4000, 0x8001, 2);
        m.Start(); m.Core.ExecuteInstruction();
        var fp = m.Core.State.A[7];
        Assert.Equal(0x4002u, m.Core.State.A[0]);
        m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
        m.InitializePhysical(0x4101, 0x8001, 2); // exception handler completes the write
        m.InitializePhysical(0x4000, 0x1234, 2);
        m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
        Assert.Equal(0x1002u, m.Core.State.ProgramCounter);
        Assert.Equal(0x4002u, m.Core.State.A[0]);
        Assert.Equal(0x4103u, m.Core.State.A[1]);
        Assert.DoesNotContain(m.Bus.Accesses, a => a.Address is 0x4000 or 0x4101);
    }

    [Fact]
    public void ProcessorRerunUsesStackedAddressDespiteRegisterCorrection()
    {
        var m = NewMachine(0x3010); // MOVE.W (A0),D0
        m.Core.State.A[0] = 0x4001;
        m.Start(); m.Core.ExecuteInstruction();
        var fp = m.Core.State.A[7];
        m.Core.State.A[0] = 0x4000; // correcting only the register does not correct the stacked cycle
        m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
        Assert.Equal(0x9030u, m.Core.State.ProgramCounter);
        Assert.Equal(3, m.Core.State.LastExceptionVector);
        Assert.Equal(fp, m.Core.State.A[7]);
        Assert.Equal(0x4001u, m.PeekPhysical(fp + 10, 4));
        Assert.Equal(0xa55a0022u, m.Core.State.D[0]);
        Assert.Equal(0x4000u, m.Core.State.A[0]);
        Assert.DoesNotContain(m.Bus.Accesses, a => a.Address == 0x4001);
    }


    private static readonly uint[] Values = [0, 1, 0x7fff, 0x8000, 0x8001, 0xffff];
    private static readonly OperandForm[] MemorySources = [new(2,0), new(3,0), new(4,0), new(5,0), new(6,0), new(7,0), new(7,1), new(7,2), new(7,3)];
    private static readonly OperandForm[] MemoryDestinations = [new(2,1), new(3,1), new(4,1), new(5,1), new(6,1), new(7,0), new(7,1)];

    [Fact, Trait("Suite", "Synthetic")]
    public void SourceFaultOperandFormsValuesAndCcr()
    {
        var report = new CoverageBatch("68010", "system-move-word-restart-source");
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        foreach (var source in MemorySources)
        foreach (var destination in MemoryDestinations.Concat([new OperandForm(0,2), new OperandForm(1,2)]))
        foreach (var value in Values)
        foreach (var software in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            Transfer(m, report, source, destination, value, ccr, supervisor, software, writeFault: false);
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void DestinationFaultOperandFormsValuesAndCcr()
    {
        var report = new CoverageBatch("68010", "system-move-word-restart-destination");
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        foreach (var source in MemorySources.Concat([new OperandForm(0,2), new OperandForm(1,2), new OperandForm(7,4)]))
        foreach (var destination in MemoryDestinations)
        foreach (var value in Values)
        foreach (var software in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
            Transfer(m, report, source, destination, value, ccr, supervisor, software, writeFault: true);
        report.Complete(output);
    }

    private static void Transfer(SyntheticMachine m, CoverageBatch report, OperandForm source, OperandForm destination,
        uint value, int ccr, bool supervisor, bool software, bool writeFault)
    {
        var opcode = MoveSpecification.Encode(2, source, destination);
        var id = $"68010/MOVE.W/restart/{(writeFault ? "destination" : "source")}/{source}/{destination}/super={supervisor}/RR={(software ? 1 : 0)}/op={opcode:X4}/value={value:X4}/ccr={ccr:X2}";
        Record(report, id, () =>
        {
            m.Reset(ccr, supervisor);
            m.Core.State.D[2] = 0xa55a0000 | value;
            m.Core.State.A[2] = 0x89ab0000 | value;
            if (!writeFault && source.Mode is 2 or 3 or 4) m.Core.State.A[0] = source.Mode == 4 ? 0x4003u : 0x4001u;
            if (writeFault && destination.Mode is 2 or 3 or 4) m.Core.State.A[1] = destination.Mode == 4 ? 0x4103u : 0x4101u;
            var words = new List<ushort> { opcode };
            var finalD = (uint[])m.Core.State.D.Clone(); var finalA = (uint[])m.Core.State.A.Clone();
            var options = new AddressOptions(
                SourceDisplacement: (short)(writeFault ? 0x40 : 0x41), DestinationDisplacement: (short)(writeFault ? 0x61 : 0x60),
                SourceAbsoluteWord: (ushort)(writeFault ? 0x6000 : 0x6001), DestinationAbsoluteWord: (ushort)(writeFault ? 0x6101 : 0x6100),
                SourceAbsoluteLong: writeFault ? 0xab006200u : 0xab006201u, DestinationAbsoluteLong: writeFault ? 0xab006301u : 0xab006300u,
                SourceIndex: new(BriefDisplacement: (sbyte)(writeFault ? 0x40 : 0x41)),
                DestinationIndex: new(BriefDisplacement: (sbyte)(writeFault ? 0x61 : 0x60)));
            var fixture = new AddressingFixture(m, 2, words, finalD, finalA, value, options);
            var sourceAddress = source.Mode >= 2 ? fixture.Resolve(source, true) : 0;
            var destinationAddress = destination.Mode >= 2 ? fixture.Resolve(destination, false) : 0;
            if (source.Mode >= 2 && source != new OperandForm(7,4)) m.InitializePhysical(sourceAddress, value, 2);
            var nextPc = fixture.NextPc;
            m.InitializePhysical(nextPc, 0x7c7b, 2); m.InitializePhysical(nextPc + 2, 0x4e71, 2);
            m.InitializePhysical(0x9030, 0x4e73, 2); m.InitializePhysical(0x9032, 0x4e71, 2);
            for (var i = 0; i < words.Count; i++) m.InitializePhysical(0x1000 + (uint)i * 2, words[i], 2);
            m.Start(); m.Core.ExecuteInstruction();
            if (m.Core.State.LastExceptionVector != 3) return "Fixture did not produce the required address error";
            var fp = m.Core.State.A[7]; var savedSr = (ushort)m.PeekPhysical(fp, 2);
            if (m.PeekPhysical(fp + 28, 2) != 0xc010 || m.PeekPhysical(fp + 30, 2) != opcode || m.PeekPhysical(fp + 32, 2) != (writeFault ? 1u : 0u))
                return "Generated private image did not identify the suspended word-MOVE phase";
            var fault = writeFault ? destinationAddress : sourceAddress;
            if (m.PeekPhysical(fp + 10, 4) != fault) return "Incorrect logical fault address";
            var rerunAddress = software ? fault : unchecked(fault + 1);
            if (software) m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
            else m.InitializePhysical(fp + 10, rerunAddress, 4);
            if (!writeFault)
            {
                // PC-relative reads use the established program-space/input-buffer convention.
                var instructionBuffer = source.Mode == 7 && source.Register is 2 or 3;
                m.InitializePhysical(fp + 20, instructionBuffer ? value ^ 0xffff : value, 2);
                m.InitializePhysical(fp + 24, instructionBuffer ? value : value ^ 0xffff, 2);
                if (!software) m.InitializePhysical(rerunAddress, value, 2);
            }
            else
            {
                if (source.Mode is 0 or 1)
                {
                    if (source.Mode == 0) m.Core.State.D[2] ^= 0xffff;
                    else m.Core.State.A[2] ^= 0xffff;
                }
                else if (source != new OperandForm(7,4)) m.InitializePhysical(sourceAddress, value ^ 0xffff, 2);
                if (software) m.InitializePhysical(rerunAddress, value, 2); // software performs the faulting write
            }
            var e = ArchitecturalExpectation.Capture(m);
            e.A[7] = fp + 58; SyntheticSystemTests.ApplyStatus(m, e, savedSr);
            for (var r = 0; r < 7; r++)
                if (!(writeFault && source.Mode == 1 && source.Register == r)) e.A[r] = finalA[r];
            if (!writeFault && destination.Mode == 0) e.D[destination.Register] = (e.D[destination.Register] & 0xffff0000) | value;
            if (!writeFault && destination.Mode == 1) e.A[destination.Register] = unchecked((uint)(int)(short)value);
            if (destination.Mode != 1) e.Sr = SyntheticExecution.MoveFlags(e.Sr, value, 2);
            e.Pc = nextPc;
            if (destination.Mode >= 2) e.Write(writeFault ? rerunAddress : destinationAddress, value, 2, m.Model);
            m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
            var mismatch = e.Verify(m); if (mismatch != null) return mismatch;
            var physicalFault = m.Model.Physical(fault); var physicalRerun = m.Model.Physical(rerunAddress);
            if (m.Bus.Accesses.Any(a => a.Address == physicalFault)) return "Original odd bus transfer was repeated";
            if (writeFault && source.Mode >= 2 && source != new OperandForm(7,4) && m.Bus.Accesses.Any(a => a.Address == m.Model.Physical(sourceAddress)))
                return "Completed source was read again";
            if (m.Bus.Accesses.Count(a => a.Address == physicalRerun && a.Write == writeFault && a.Width == 2) != (software ? 0 : 1))
                return "Faulted word cycle must rerun exactly once, or be supplied by software";
            m.Core.ExecuteInstruction(); e.D[6] = 123; e.Pc += 2; e.Sr = (ushort)(e.Sr & 0xfff0);
            return e.Verify(m);
        });
    }


    [Fact, Trait("Suite", "Synthetic")]
    public void CopiedNestedAliasedPrefetchedAndTracedContinuations()
    {
        var report = new CoverageBatch("68010", "system-move-word-restart-edges");
        foreach (var kind in new[] { "copied", "nested", "alias", "prefetched", "trace" })
        foreach (var software in new[] { false, true })
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/MOVE.W/restart/{kind}/super={supervisor}/RR={(software ? 1 : 0)}/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                var opcode = kind == "alias" ? (ushort)0x30d8 : kind == "nested" ? (ushort)0x32d8 : (ushort)0x3418;
                var m = NewMachine(opcode);
                m.Core.State.StatusRegister = (ushort)((supervisor ? 0x2700 : 0x0700) | ccr | (kind == "trace" ? 0x8000 : 0));
                m.Core.State.SetActiveStackPointer(supervisor ? 0x4700u : 0x7800u);
                m.Core.State.A[0] = 0x4001; m.Core.State.A[1] = 0x4101;
                m.InitializePhysical(0x1002, 0x7c7b, 2);
                m.Start(); m.Core.ExecuteInstruction();
                var fp = m.Core.State.A[7];
                if (kind == "copied")
                {
                    for (uint offset = 0; offset < 58; offset += 2) m.InitializePhysical(0x6800 + offset, m.PeekPhysical(fp + offset, 2), 2);
                    fp = 0x6800; m.Core.State.SetActiveStackPointer(fp);
                }
                var sourceRerun = software ? 0x4001u : 0x4002u;
                if (software)
                { m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2); m.InitializePhysical(fp + 20, 0x8001, 2); }
                else { m.InitializePhysical(fp + 10, sourceRerun, 4); m.InitializePhysical(sourceRerun, 0x8001, 2); }
                if (kind == "prefetched") m.InitializePhysical(0x1002, 0x7cff, 2); // buffer must retain MOVEQ #123,D6
                var e = ArchitecturalExpectation.Capture(m);
                var savedSr = (ushort)m.PeekPhysical(fp, 2);
                e.A[7] = fp + 58; SyntheticSystemTests.ApplyStatus(m, e, savedSr);
                e.Sr = SyntheticExecution.MoveFlags(e.Sr, 0x8001, 2); e.Pc = 0x1002;
                m.Bus.Accesses.Clear();
                if (kind is "nested" or "alias")
                {
                    // Source is completed once, then an independent destination fault suspends the same instruction.
                    m.Core.ExecuteInstruction();
                    if (m.Core.State.ProgramCounter != 0x9030 || m.Core.State.LastExceptionVector != 3 || m.Core.State.A[0] != 0x4003)
                        return "Second operand fault did not preserve completed source state";
                    var second = m.Core.State.A[7]; var destination = kind == "alias" ? 0x4003u : 0x4101u;
                    if (m.PeekPhysical(second + 16, 2) != 0x8001 || m.PeekPhysical(second + 32, 2) != 1)
                        return "Destination continuation lost the supplied source value or write phase";
                    if (m.Bus.Accesses.Count(a => a.Address == sourceRerun) != (software ? 0 : 1)) return "Source cycle count differs before second fault";
                    var writeRerun = software ? destination : destination + 1;
                    if (software)
                    { m.InitializePhysical(second + 8, m.PeekPhysical(second + 8, 2) | 0x8000, 2); m.InitializePhysical(destination, 0x8001, 2); }
                    else m.InitializePhysical(second + 10, writeRerun, 4);
                    e = ArchitecturalExpectation.Capture(m);
                    savedSr = (ushort)m.PeekPhysical(second, 2);
                    e.A[7] = second + 58; SyntheticSystemTests.ApplyStatus(m, e, savedSr);
                    e.A[kind == "alias" ? 0 : 1] += 2;
                    e.Pc = 0x1002; e.Write(writeRerun, 0x8001, 2, m.Model);
                    m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
                    if (m.Bus.Accesses.Any(a => a.Address == sourceRerun)) return "Source was retried after second fault";
                    if (m.Bus.Accesses.Count(a => a.Write && a.Address == writeRerun) != (software ? 0 : 1)) return "Second fault write cycle count differs";
                }
                else
                {
                    e.D[2] = (e.D[2] & 0xffff0000) | 0x8001;
                    if (kind == "trace") SyntheticExecution.ExpectException(m, e, 9, 0x1002);
                    m.Core.ExecuteInstruction();
                    if (m.Bus.Accesses.Count(a => a.Address == sourceRerun) != (software ? 0 : 1)) return "Source cycle repeated on continuation";
                }
                var mismatch = e.Verify(m); if (mismatch != null) return mismatch;
                if (kind != "trace")
                {
                    m.Core.ExecuteInstruction(); e.Pc += 2; e.D[6] = 123; e.Sr = (ushort)(e.Sr & 0xfff0);
                    mismatch = e.Verify(m);
                }
                return mismatch;
            });
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void UserA7StridesAndMoveaStackSelection()
    {
        var report = new CoverageBatch("68010", "system-move-word-restart-a7");
        foreach (var kind in new[] { "post-read", "post-write", "movea" })
        foreach (var software in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/MOVE.W/restart/A7/{kind}/RR={(software ? 1 : 0)}/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                var opcode = kind == "post-write" ? (ushort)0x3ec0 : kind == "movea" ? (ushort)0x3e5f : (ushort)0x301f;
                var m = NewMachine(opcode);
                m.Core.State.StatusRegister = (ushort)(0x0700 | ccr); m.Core.State.SetActiveStackPointer(0x7801);
                m.Core.State.D[0] = 0xa55a8001;
                m.Start(); m.Core.ExecuteInstruction();
                var fp = m.Core.State.A[7]; var savedSr = (ushort)m.PeekPhysical(fp, 2);
                var rerun = software ? 0x7801u : 0x7802u;
                if (software) m.InitializePhysical(fp + 8, m.PeekPhysical(fp + 8, 2) | 0x8000, 2);
                else m.InitializePhysical(fp + 10, rerun, 4);
                if (kind == "post-write") { if (software) m.InitializePhysical(rerun, 0x8001, 2); }
                else { m.InitializePhysical(fp + 20, 0x8001, 2); if (!software) m.InitializePhysical(rerun, 0x8001, 2); }
                var e = ArchitecturalExpectation.Capture(m);
                e.A[7] = fp + 58; SyntheticSystemTests.ApplyStatus(m, e, savedSr);
                e.A[7] = kind == "movea" ? 0xffff8001u : 0x7803u;
                if (kind != "movea") e.Sr = SyntheticExecution.MoveFlags(e.Sr, 0x8001, 2);
                e.Pc = 0x1002;
                if (kind == "post-write") e.Write(rerun, 0x8001, 2, m.Model);
                else if (kind == "post-read") e.D[0] = (e.D[0] & 0xffff0000) | 0x8001;
                m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
                var mismatch = e.Verify(m); if (mismatch != null) return mismatch;
                if (m.Bus.Accesses.Count(a => a.Address == rerun && a.Write == (kind == "post-write")) != (software ? 0 : 1)) return "A7 cycle repeated or software buffer ignored";
                return null;
            });
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void InvalidPrivateContinuationPreservesFrameBeforeFormatError()
    {
        var report = new CoverageBatch("68010", "system-move-word-restart-invalid");
        foreach (var corruption in new[] { "version", "opcode", "phase", "pc", "queue-address", "queue-count", "reserved" })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            var id = $"68010/MOVE.W/restart/invalid/{corruption}/ccr={ccr:X2}";
            Record(report, id, () =>
            {
                var m = NewMachine(0x3010); m.Core.State.StatusRegister = (ushort)(0x2700 | ccr); m.Core.State.A[0] = 0x4001;
                m.Start(); m.Core.ExecuteInstruction(); var fp = m.Core.State.A[7];
                var (offset, value) = corruption switch
                {
                    "version" => (26u, 0x0400u), "opcode" => (30u, 0x4e71u), "phase" => (32u, 2u),
                    "pc" => (36u, 0x1003u), "queue-address" => (40u, 0x1003u), "queue-count" => (46u, 3u), _ => (48u, 1u)
                };
                m.InitializePhysical(fp + offset, value, 2);
                var e = ArchitecturalExpectation.Capture(m); SyntheticExecution.ExpectException(m, e, 14, 0x9030);
                m.Bus.Accesses.Clear(); m.Core.ExecuteInstruction();
                if (m.Bus.Accesses.Any(a => a.Address is 0x4000 or 0x4001)) return "Malformed continuation executed an operand cycle";
                return e.Verify(m);
            });
        }
        report.Complete(output);
    }

    private static void Record(CoverageBatch report, string id, Func<string?> run)
    {
        try { var mismatch = run(); report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch); }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException)
        { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
    }

    private static SyntheticMachine NewMachine(ushort opcode)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68010"));
        m.Reset(31);
        m.InitializePhysical(0x1000, opcode, 2);
        m.InitializePhysical(0x1002, 0x747b, 2);
        m.InitializePhysical(0x1004, 0x4e71, 2);
        m.InitializePhysical(0x9030, 0x4e73, 2);
        m.InitializePhysical(0x9032, 0x4e71, 2);
        return m;
    }
}
