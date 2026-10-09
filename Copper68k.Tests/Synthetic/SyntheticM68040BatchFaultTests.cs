using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// Architectural validation frames: MC68040UM 8.4.6.7. Cached-block batching,
// callbacks and cycle equality are emulator execution/timing-policy contracts.
// MOVE writes and instruction fetch/validation use architectural format 7.
// Normal physical operand reads also use format 7; restart phases are separate.
public sealed class SyntheticM68040BatchFaultTests(ITestOutputHelper output)
{
    private const uint Handler = 0x9020;
    private static readonly ModelSpec Model = ModelSpec.All.Single(x => x.Id == "68040");

    [Fact, Trait("Suite", "Synthetic")]
    public void RteValidationFaultsDeliverOnceAcrossColdAndCachedBatchPrefixes()
    {
        var scalarBus = new CodeBus(); var batchBus = new CodeBus();
        var scalar = new SyntheticMachine(Model, scalarBus); var batch = new SyntheticMachine(Model, batchBus);
        var report = new CoverageBatch("68040", "rte-validation-batch");
        foreach (var cached in new[] { false, true })
        foreach (var prefix in new[] { 0, 1, 3 })
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        for (var ccr = 0; ccr < 32; ccr++)
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var stop in new[] { "cap", "before" })
        foreach (var (offset, width) in SyntheticM68040RteValidationFaultTests.Reads("CM"))
        for (var faultByte = 0; faultByte < width; faultByte++)
        {
            var id = $"68040/RTE/validation-batch/cached={cached}/prefix={prefix}/bank={bank}/T={trace:X4}/align={alignment}/VBR={vbr:X8}/stop={stop}/read={offset}:{width}/byte={faultByte}/op=4E73/ccr={ccr:X2}";
            Run(scalar, scalarBus, batch, batchBus, report, id, "RTE", cached, prefix, bank, trace, ccr,
                alignment, vbr, stop, "entry", offset, width, faultByte);
        }
        report.Complete(output);
    }

    [Fact, Trait("Suite", "Synthetic")]
    public void CachedOperandMixedAndSelfBranchFaultsPreserveCountsAndPartialEffects()
    {
        var scalarBus = new CodeBus(); var batchBus = new CodeBus();
        var scalar = new SyntheticMachine(Model, scalarBus); var batch = new SyntheticMachine(Model, batchBus);
        var report = new CoverageBatch("68040", "access-fault-batch-dispatch");
        foreach (var form in new[] { "load", "store", "mixed-load", "mixed-store", "partial-store", "self-fetch" })
        foreach (var prefix in new[] { 0, 1, 3 })
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var ccr in new[] { 0, 31 })
        foreach (var alignment in new uint[] { 0, 1 })
        foreach (var vbr in new uint[] { 0, 0x10000 })
        foreach (var stop in new[] { "cap", "before", "cycle" })
        foreach (var outcome in new[] { "entry", "fatal-stack", "fatal-vector" })
        for (var faultByte = 0; faultByte < 4; faultByte++)
        {
            var opcode = Opcode(form);
            var id = $"68040/access-fault/batch/{form}/prefix={prefix}/bank={bank}/align={alignment}/VBR={vbr:X8}/stop={stop}/outcome={outcome}/byte={faultByte}/op={opcode:X4}/ccr={ccr:X2}";
            Run(scalar, scalarBus, batch, batchBus, report, id, form, true, prefix, bank, 0, ccr,
                alignment, vbr, stop, outcome, 0, 4, faultByte);
        }
        report.Complete(output);
    }

    private static void Run(SyntheticMachine scalar, CodeBus scalarBus, SyntheticMachine batch, CodeBus batchBus,
        CoverageBatch report, string id, string form, bool cached, int prefix, string bank, ushort trace, int ccr,
        uint alignment, uint vbr, string stop, string outcome, uint offset, int width, int faultByte)
    {
        try
        {
            var first = Prepare(scalar, scalarBus, form, cached, prefix, bank, trace, ccr, alignment, vbr, outcome, offset, width, faultByte);
            var second = Prepare(batch, batchBus, form, cached, prefix, bank, trace, ccr, alignment, vbr, outcome, offset, width, faultByte);
            var startCycle = scalar.Core.State.Cycles; var startNative = scalar.Core.State.NativeCycles;
            for (var n = 0; n <= prefix; n++)
            {
                if (!first.Boundary.BeforeInstruction()) throw new InvalidOperationException("Scalar prefix boundary unexpectedly denied");
                var priorCycle = scalar.Core.State.Cycles;
                scalar.Core.ExecuteInstruction(); first.Boundary.AfterInstruction(priorCycle, scalar.Core.State.Cycles);
            }
            var elapsed = scalar.Core.State.Cycles - startCycle;
            var nativeElapsed = scalar.Core.State.NativeCycles - startNative;
            var batchStart = batch.Core.State.Cycles; var batchNative = batch.Core.State.NativeCycles;
            var count = ((IM68kBatchCore)batch.Core).ExecuteInstructions(stop == "cap" ? prefix + 1 : prefix + 8,
                stop == "cycle" ? batchStart + elapsed : null, second.Boundary);
            string? mismatch = null;
            if (count != prefix + 1 || second.Boundary.Completed != prefix + 1 ||
                second.Boundary.BeforeCalls != prefix + 1 + (stop == "before" && outcome == "entry" ? 1 : 0))
                mismatch = $"Batch count/callbacks differ: count={count}, before={second.Boundary.BeforeCalls}, after={second.Boundary.Completed}";
            else if (elapsed <= 0 || batch.Core.State.Cycles - batchStart != elapsed || batch.Core.State.NativeCycles - batchNative != nativeElapsed)
                mismatch = "Batch changed the scalar machine/native timing policy";
            else if (!scalarBus.Accesses.SequenceEqual(batchBus.Accesses))
                mismatch = "Batch repeated, omitted or reordered a CPU bus transfer";
            else if (scalarBus.Rejected.Count != (outcome == "entry" ? 1 : 2) || batchBus.Rejected.Count != scalarBus.Rejected.Count ||
                batchBus.Rejected[0].Address != second.FaultAddress || batchBus.Rejected[0].Width != width)
                mismatch = "Fault width/address/count differs";
            else if (cached && form is "load" or "store" or "mixed-load" or "mixed-store" or "self-fetch" &&
                (batchBus.RootPeeksAtArm == 0 || batchBus.RootPeeks != batchBus.RootPeeksAtArm))
                mismatch = "Cached dispatch fixture did not retain its warmed root block";
            if (mismatch == null)
            {
                mismatch = outcome == "entry" ? first.Expected.Verify(scalar) : VerifyFatal(scalar, scalarBus, first);
                mismatch ??= outcome == "entry" ? second.Expected.Verify(batch) : VerifyFatal(batch, batchBus, second);
            }
            if (mismatch == null && outcome == "entry")
            {
                // Ensure exception delivery did not consume the handler sentinel,
                // leave a stale direct opcode, or retain incoming pending trace.
                foreach (var (machine, fixture) in new[] { (scalar, first), (batch, second) })
                {
                    fixture.Expected.D[2] = 123; fixture.Expected.Pc = Handler + 2;
                    fixture.Expected.Sr = SyntheticExecution.MoveFlags(fixture.Expected.Sr, 123, 4);
                    machine.Core.ExecuteInstruction(); mismatch ??= fixture.Expected.Verify(machine);
                }
            }
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
        }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
        { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
    }

    private sealed record Fixture(ArchitecturalExpectation Expected, FaultBoundary Boundary, uint Frame, uint FaultAddress, uint Sequence);

    private static Fixture Prepare(SyntheticMachine m, CodeBus bus, string form, bool cached, int prefix,
        string bank, ushort trace, int ccr, uint alignment, uint vbr, string outcome, uint offset, int width, int faultByte)
    {
        bus.Disarm(); bus.RootPeeks = 0; bus.RootPeeksAtArm = 0; bus.AllowPeek = cached; m.Reset(ccr);
        var frame = (bank == "MSP" ? 0x7400u : 0x4700u) + alignment;
        for (var n = -76; n < 80; n++) m.InitializePhysical(unchecked(frame + (uint)n), (uint)(n ^ 0x5a), 1);
        m.InitializePhysical(frame, 0x801fu ^ (uint)ccr, 2); m.InitializePhysical(frame + 2, 0x6000, 4);
        m.InitializePhysical(frame + 6, 0x7008, 2); m.InitializePhysical(frame + 8, 0x12345678, 4);
        m.InitializePhysical(frame + 12, 0x1105, 2);
        m.InitializePhysical(0x4200 + alignment, 0x12345678, 4); m.InitializePhysical(0x4300 + alignment, 0x9abcdef0, 4);
        m.InitializePhysical(vbr + 8, Handler, 4); m.InitializePhysical(Handler, 0x747b, 2);
        m.InitializePhysical(Handler + 2, 0x4e71, 2);
        var words = new List<ushort>();
        var mixed = form.StartsWith("mixed");
        for (var n = 0; n < prefix; n++)
        {
            if (mixed && n == 0) { words.Add(0xf200); words.Add(0x0080); } // FMOVE FP0,FP1, no arithmetic.
            else words.Add(0x5287); // ADDQ.L #1,D7: completed prefix survives the fault.
        }
        var faultPc = SyntheticMachine.Code + (uint)words.Count * 2;
        if (form == "self-fetch") { words = [0x4e71, 0x4e71, 0x4e71]; faultPc = 0x1006; }
        words.Add(Opcode(form));
        if (form.EndsWith("store") && form != "partial-store")
        { words.Add(0); words.Add((ushort)(0x4300 + alignment)); }
        for (var n = 0; n < words.Count; n++) m.InitializePhysical(SyntheticMachine.Code + (uint)n * 2, words[n], 2);
        m.InitializePhysical(SyntheticMachine.Code + (uint)words.Count * 2, 0x4e71, 2);
        m.Start();
        m.Core.State.SetInterruptStackPointer(0x4700 + alignment); m.Core.State.SetMasterStackPointer(0x7400 + alignment);
        m.Core.State.StatusRegister = (ushort)((bank == "MSP" ? 0x3000 : 0x2000) | ccr);
        m.Core.State.A[0] = 0x4200 + alignment; m.Core.State.A[1] = 0x4304 + alignment;
        m.Core.State.VectorBaseRegister = vbr;
        if (form == "self-fetch") m.Core.State.ProgramCounter = faultPc;
        var beforeWarm = ArchitecturalExpectation.Capture(m);
        if (cached)
        {
            var warmCount = form == "RTE" ? prefix : form == "self-fetch" ? 4 : prefix + 1;
            if (warmCount > 0 && ((IM68kBatchCore)m.Core).ExecuteInstructions(warmCount, null, new WarmBoundary()) != warmCount)
                throw new InvalidOperationException("Warm prefix did not execute completely");
            beforeWarm.D.CopyTo(m.Core.State.D, 0);
            for (var n = 0; n < 7; n++) m.Core.State.A[n] = beforeWarm.A[n];
            m.Core.State.SetInterruptStackPointer(0x4700 + alignment); m.Core.State.SetMasterStackPointer(0x7400 + alignment);
            m.Core.State.StatusRegister = beforeWarm.Sr; m.Core.State.ProgramCounter = beforeWarm.Pc;
            m.Core.State.M68040Fpu.Reset();
            bus.Memory.Clear(); foreach (var item in beforeWarm.Memory) bus.Memory.Add(item.Key, item.Value);
        }
        bus.Accesses.Clear();
        var e = ArchitecturalExpectation.Capture(m);
        var savedSr = (ushort)((bank == "MSP" ? 0x3000 : 0x2000) | trace | ccr);
        var incomingSr = savedSr;
        var write = form.EndsWith("store");
        var storeValue = form == "partial-store" ? 0x12345678u : m.Core.State.D[0];
        if (write) savedSr = (ushort)((savedSr & 0xfff0) | (storeValue == 0 ? 4 : 0) | ((storeValue & 0x80000000) != 0 ? 8 : 0));
        e.Sr = (ushort)(savedSr & ~0xc000); e.Pc = Handler; e.ExceptionVector = 2;
        e.D[7] += form == "self-fetch" ? 0u : (uint)(prefix - (mixed && prefix > 0 ? 1 : 0));
        if (form == "partial-store") { e.A[0] += 4; e.A[1] -= 4; }
        var kind = form == "self-fetch" ? M68kBusAccessKind.CpuInstructionFetch : form.EndsWith("store") ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead;
        var faultAddress = form == "RTE" ? frame + offset : form == "self-fetch" ? 0x1004u : form.EndsWith("store") ? 0x4300 + alignment : 0x4200 + alignment;
        var stackedPc = write ? faultPc + (form == "partial-store" ? 2u : 6u) : faultPc;
        var sequence = m.Core.State.ExceptionSequence;
        e.ControlChecks["single exception"] = (s => s.ExceptionSequence, sequence + 1);
        e.ControlChecks["saved SR"] = (s => s.LastExceptionStatusRegister, savedSr);
        e.ControlChecks["saved PC"] = (s => s.LastExceptionStackedProgramCounter, stackedPc);
        e.ControlChecks["bypass cleared"] = (s => s.M68040Mmu.BypassTranslation ? 1u : 0u, 0);
        var accessFrame = frame - 60u;
        e.A[7] = accessFrame; if (bank == "MSP") e.MasterStackPointer = accessFrame;
        {
            SyntheticM68040RteValidationFaultTests.ExpectAccessFrame(m, e, accessFrame, savedSr, stackedPc, faultAddress, width);
            if (form == "self-fetch") e.Write(accessFrame + 12, 0x0106, 2, m.Model);
            if (write)
            {
                var modifier = bank == "MSP" || bank == "ISP" ? 5u : 1u;
                e.Write(accessFrame + 12, modifier, 2, m.Model);
                e.Write(accessFrame + 18, 0x80 | modifier, 2, m.Model);
                e.Write(accessFrame + 40, faultAddress, 4, m.Model);
                for (uint n = 18; n < 20; n++) e.MemoryMasks[accessFrame + n] = 255;
                for (uint n = 40; n < 48; n++) e.MemoryMasks[accessFrame + n] = 255;
                for (var n = 0; n < 4; n++)
                    e.Memory[accessFrame + 44 + ((faultAddress + (uint)n) & 3)] = (byte)(storeValue >> (8 * (3 - n)));
            }
        }
        var boundary = new FaultBoundary(m.Core, bus, prefix, incomingSr, faultAddress + (uint)faultByte, kind,
            outcome == "fatal-stack" ? accessFrame : outcome == "fatal-vector" ? vbr + 8 : null,
            outcome == "fatal-stack" ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead);
        return new(e, boundary, frame, faultAddress, sequence);
    }

    private static string? VerifyFatal(SyntheticMachine m, CodeBus bus, Fixture fixture)
    {
        var s = m.Core.State; var e = fixture.Expected;
        if (!s.Halted || s.Stopped || s.StatusRegister != e.Sr || s.LastExceptionVector != 2 ||
            s.LastExceptionStatusRegister != e.ControlChecks["saved SR"].Value || s.ExceptionSequence != fixture.Sequence + 1 ||
            s.M68040Mmu.BypassTranslation) return "Batch did not retain fatal exception state/provenance";
        if (!s.D.SequenceEqual(e.D) || !s.A.Take(7).SequenceEqual(e.A.Take(7))) return "Fatal batch changed unrelated or partially committed registers";
        if (s.A[7] < e.A[7] || s.A[7] > fixture.Frame || bus.Accesses.Count != bus.Rejected[^1].AccessCount)
            return "Fatal batch changed memory after rejection or escaped its partial frame";
        if (bus.Accesses.Where(a => a.Write).Any(a => a.Address < e.A[7] || (ulong)a.Address + (uint)a.Width > fixture.Frame))
            return "Fatal batch wrote outside its partial frame";
        // Original operand accesses are reads only before the rejected write.
        // Compare outside the opaque partial frame against the initial memory.
        if (e.Memory.Keys.Concat(bus.Memory.Keys).Distinct().Any(at => (at < e.A[7] || at >= fixture.Frame) &&
            e.Memory.GetValueOrDefault(at) != bus.Peek(at))) return "Fatal batch changed original operands/frame/neighbors";
        return null;
    }

    private static ushort Opcode(string form) => form switch
    {
        "RTE" => 0x4e73, "load" or "mixed-load" => 0x2018, "store" or "mixed-store" => 0x23c0,
        "partial-store" => 0x2318, "self-fetch" => 0x60fe, _ => throw new ArgumentException("Unknown form")
    };

    private sealed class CodeBus : SyntheticM68040AccessDoubleFaultTests.FaultBus, IM68kCodeReader
    {
        public bool AllowPeek { get; set; }
        public int RootPeeks { get; set; }
        public int RootPeeksAtArm { get; set; }
        public ushort ReadHostWord(uint address)
        {
            if (!AllowPeek) throw M68kCodeReadException.Instance;
            if (address >= SyntheticMachine.Code && address < 0x1100) RootPeeks++;
            return (ushort)Peek(address, 2);
        }
    }

    private sealed class WarmBoundary : IM68kInstructionBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previous, long current) { }
    }

    private sealed class FaultBoundary(IM68kCore core, CodeBus bus, int prefix, ushort incomingSr,
        uint firstByte, M68kBusAccessKind firstKind, uint? secondByte, M68kBusAccessKind secondKind) : IM68kInstructionBoundary
    {
        public int BeforeCalls { get; private set; }
        public int Completed { get; private set; }
        public bool BeforeInstruction()
        {
            BeforeCalls++;
            if (Completed > prefix) return false;
            if (Completed == prefix)
            {
                core.State.StatusRegister = incomingSr;
                bus.RootPeeksAtArm = bus.RootPeeks;
                bus.Arm(firstByte, firstKind); if (secondByte.HasValue) bus.Arm(secondByte.Value, secondKind);
            }
            return true;
        }
        public void AfterInstruction(long previous, long current) => Completed++;
    }
}
