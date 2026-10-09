using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68020UM 6.1.3 and MC68030UM 8.1.3 permit A or B for an odd
// instruction prefetch. Neither permits the normal format-0 frame.
// This discovers real exception entry; it does not fabricate opaque RTE state.
public sealed class SyntheticM68020AddressFrameDiscoveryTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Models => ModelSpec.All
        .Where(m => m.Id is "68EC020" or "A1200" or "68020" or "68030")
        .Select(m => new object[] { m.Id });

    [Theory, MemberData(nameof(Models)), Trait("Suite", "ReferenceDiscovery")]
    public void FaultFreeFixturesPreserveBanksRegistersAndMemory(string modelId)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "address-frame-controls");
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            Initialize(m, bank, ccr, 0);
            var expected = ArchitecturalExpectation.Capture(m);
            expected.Pc += 2;
            SyntheticExecution.Run(m, expected, report,
                $"{modelId}/NOP/address-frame-control/bank={bank}/op=4E71/ccr={ccr:X2}", false);
        }
        report.Complete(output);
    }

    [EnvironmentFact("COPPER68K_RUN_020_ADDRESS_FRAME_DISCOVERY", "require real 020/030 address-error A/B frames"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarOddInstructionFetchRequiresBusFaultFrame() => Audit(false);

    [EnvironmentFact("COPPER68K_RUN_020_ADDRESS_FRAME_DISCOVERY", "require real 020/030 address-error A/B frames"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchOddInstructionFetchRequiresBusFaultFrame() => Audit(true);

    private void Audit(bool batch)
    {
        // Complete all models before the aggregate assertion, retaining each
        // report even when an earlier model exposes the implementation gap.
        var failures = new List<Exception>();
        foreach (var row in Models)
            try { Audit((string)row[0], batch); } catch (Exception ex) { failures.Add(ex); }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(e => e.Message)));
    }

    private void Audit(string modelId, bool batch)
    {
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == modelId));
        var report = new CoverageBatch(modelId, "address-frame-discovery-" + (batch ? "batch" : "scalar"));
        foreach (var bank in new[] { "user", "user-M", "ISP", "MSP" })
        foreach (var trace in new[] { 0, 0x8000, 0x4000 })
        foreach (var target in new[] { 0x6001u, 0x10006001u })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            Initialize(m, bank, ccr, trace);
            m.Core.State.ProgramCounter = target;
            var before = ArchitecturalExpectation.Capture(m);
            var id = $"{modelId}/address-error/odd-prefetch/bank={bank}/T={trace:X4}/target={target:X8}/op=0000/ccr={ccr:X2}";
            string? mismatch = null;
            try
            {
                if (batch)
                {
                    var boundary = new Boundary();
                    var count = ((IM68kBatchCore)m.Core).ExecuteInstructions(1, null, boundary);
                    if (count != 1 || boundary.Before != 1 || boundary.After != 1)
                        mismatch = $"boundary count expected 1/1/1 actual {count}/{boundary.Before}/{boundary.After}";
                }
                else m.Core.ExecuteInstruction();
                var state = m.Core.State;
                var frame = m.PeekPhysical(state.A[7] + 6, 2);
                if (mismatch == null && frame >> 12 is not (10 or 11))
                    mismatch = $"address-error format expected A or B actual {frame:X4}; PC={state.ProgramCounter:X8}; SP={state.A[7]:X8}";
                if (mismatch == null && (frame & 0xfff) != 12) mismatch = "address-error vector offset differs";
                if (mismatch == null && (state.LastExceptionVector != 3 || state.ProgramCounter != 0x9030 ||
                    m.PeekPhysical(state.A[7], 2) != before.Sr || m.PeekPhysical(state.A[7] + 2, 4) != target))
                    mismatch = "address-error common header/vector differs";
                if (mismatch == null && m.Bus.Accesses.Any(a => !a.Write &&
                    a.Kind == M68kBusAccessKind.CpuInstructionFetch && a.Address == m.Model.Physical(target)))
                    mismatch = "odd instruction address was accessed on the bus";
                report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            }
            catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
            { report.Record(id, "unsupported", ex.Message); }
            catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
        }
        report.Complete(output);
    }

    private static void Initialize(SyntheticMachine m, string bank, int ccr, int trace)
    {
        m.Reset(ccr);
        _ = SyntheticExecution.Prepare(m, [0x4e71]);
        m.Core.State.SetUserStackPointer(0x7800);
        m.Core.State.SetInterruptStackPointer(0x4700);
        m.Core.State.SetMasterStackPointer(0x7400);
        m.Core.State.StatusRegister = (ushort)(trace | 0x700 | ccr | (bank is "ISP" or "MSP" ? 0x2000 : 0) |
            (bank is "user-M" or "MSP" ? 0x1000 : 0));
        m.Core.State.SetActiveStackPointer(bank == "MSP" ? 0x7400u : bank == "ISP" ? 0x4700u : 0x7800u);
        m.Bus.Accesses.Clear();
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public int Before, After;
        public bool BeforeInstruction() { Before++; return true; }
        public void AfterInstruction(long previousCycle, long currentCycle) => After++;
    }
}
