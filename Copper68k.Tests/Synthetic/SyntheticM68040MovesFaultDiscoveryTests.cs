using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 3.2.5/Table 3-2 and 8.4.6: physical MOVES operand faults
// require format 7 and converted TT/TM. No saved-PC, instruction completion,
// special-transfer writeback, handler restart or physical timing claim is made.
public sealed class SyntheticM68040MovesFaultDiscoveryTests(ITestOutputHelper output)
{
    internal const string Enable = "COPPER68K_RUN_040_MOVES_FAULT_DISCOVERY";
    private const uint Handler = 0x9020;
    // Literal table rows, independent of any production FC conversion helper.
    private static readonly ushort[] Attributes = [0x0010, 0x0001, 0x0001, 0x0013, 0x0014, 0x0005, 0x0005, 0x0017];

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalMovesFixturesPreserveFlagsAndOtherFunctionCode(bool batch) => Audit(batch, false);

    [EnvironmentFact(Enable, "discover physical MOVES operand fault frames"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarMovesFaultsRequireFormat7AndConvertedFunctionCodes() => Audit(false, true);

    [EnvironmentFact(Enable, "discover physical MOVES operand fault frames"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchMovesFaultsRequireFormat7AndConvertedFunctionCodes() => Audit(true, true);

    private void Audit(bool batch, bool fault)
    {
        var report = new CoverageBatch("68040", "moves-physical-fault-" + (fault ? "discovery-" : "fixture-") + (batch ? "batch" : "scalar"));
        foreach (var width in new[] { 1, 2, 4 })
        foreach (var lane in new uint[] { 0, 1, 2, 3 })
        for (var fc = 0; fc < 8; fc++)
        foreach (var write in new[] { false, true })
        foreach (var bank in new[] { "ISP", "MSP" })
        foreach (var ccr in new[] { 0, 31 })
        for (var faultByte = 0; faultByte < (fault ? width : 1); faultByte++)
        {
            // Fixed reference encodings: MOVES.B/W/L D0,(A0) or (A0),D0.
            ushort opcode = width switch { 1 => 0x0e10, 2 => 0x0e50, _ => 0x0e90 };
            var id = $"68040/MOVES/physical-{(fault ? "fault" : "fixture")}/route={(batch ? "batch" : "scalar")}/size={width}/lane={lane}/FC={fc}/direction={(write ? "write" : "read")}/bank={bank}/ccr={ccr:X2}/byte={faultByte}/op={opcode:X4}";
            try
            {
                var bus = new SyntheticM68040AccessDoubleFaultTests.FaultBus();
                var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"), bus);
                m.Reset(ccr); m.Core.State.D[0] = 0x89abcdef; m.Core.State.A[0] = 0x4200 + lane;
                for (uint n = 0x41fc; n < 0x420b; n++) m.InitializePhysical(n, 0x5a, 1);
                m.InitializePhysical(0x4200 + lane, 0x12345678, width);
                m.InitializePhysical(Handler, 0x4e71, 2); m.InitializePhysical(Handler + 2, 0x4e71, 2);
                m.InitializePhysical(8, Handler, 4);
                SyntheticExecution.Prepare(m, [opcode, (ushort)(write ? 0x0800 : 0x0000), 0x7e55, 0x60fe]);
                var s = m.Core.State;
                s.SetInterruptStackPointer(0x4700); s.SetMasterStackPointer(0x7400);
                s.StatusRegister = (ushort)((bank == "MSP" ? 0x3700 : 0x2700) | ccr);
                s.SourceFunctionCode = (uint)(write ? 7 - fc : fc);
                s.DestinationFunctionCode = (uint)(write ? fc : 7 - fc);
                var before = ArchitecturalExpectation.Capture(m); var sequence = s.ExceptionSequence;
                if (fault) bus.Arm(0x4200 + lane + (uint)faultByte,
                    write ? M68kBusAccessKind.CpuDataWrite : M68kBusAccessKind.CpuDataRead);
                if (batch)
                {
                    if (((IM68kBatchCore)m.Core).ExecuteInstructions(1, s.Cycles + 1000, new Boundary()) != 1)
                        throw new InvalidOperationException("Expected exactly one batch instruction boundary");
                }
                else m.Core.ExecuteInstruction();
                if (!fault)
                {
                    before.Pc = 0x1004;
                    if (write) before.Write(0x4200 + lane, 0x89abcdef, width, m.Model);
                    else before.D[0] = width switch { 1 => 0x89abcd78, 2 => 0x89ab5678, _ => 0x12345678 };
                    if (before.Verify(m) is { } mismatch) throw new InvalidOperationException(mismatch);
                    if (s.ExceptionSequence != sequence || bus.Rejected.Count != 0)
                        throw new InvalidOperationException("Fault-free fixture generated an exception");
                    var operand = bus.Accesses.Where(a => a.Address == 0x4200 + lane && a.Kind is
                        M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite).ToArray();
                    if (operand.Length != 1 || operand[0].Write != write || operand[0].Width != width)
                        throw new InvalidOperationException("Fixture operand width/direction/count differs");
                }
                else
                {
                    var stack = bank == "MSP" ? 0x7400u : 0x4700u;
                    var frame = stack - 60;
                    if (s.LastExceptionVector != 2 || s.ExceptionSequence != sequence + 1 ||
                        s.ProgramCounter != Handler || s.Halted || s.Stopped || bus.Rejected.Count != 1 ||
                        bus.Rejected[0].Address != 0x4200 + lane || bus.Rejected[0].Width != width)
                        throw new InvalidOperationException("Physical operand rejection did not enter exactly one access error");
                    var actualFormat = bus.Peek(s.A[7] + 6, 2);
                    if (s.A[7] != frame || actualFormat != 0x7008)
                        throw new InvalidOperationException($"format 7 required: SP={s.A[7]:X8}, frame={actualFormat:X4}");
                    var size = width switch { 1 => 0x20, 2 => 0x40, _ => 0 };
                    var expectedSsw = Attributes[fc] | size | (write ? 0 : 0x0100);
                    if ((bus.Peek(frame + 12, 2) & 0xff7f) != expectedSsw)
                        throw new InvalidOperationException($"SSW TT/TM/size/direction expected {expectedSsw:X4}, actual {bus.Peek(frame + 12, 2):X4}");
                    if (bus.Peek(frame, 2) != before.Sr || bus.Peek(frame + 20, 4) != 0x4200 + lane)
                        throw new InvalidOperationException("Saved SR or initial logical fault address differs");
                    if (!s.D.SequenceEqual(before.D) || !s.A.Take(7).SequenceEqual(before.A.Take(7)) ||
                        s.StatusRegister != before.Sr || s.UserStackPointer != before.InactiveStackPointer ||
                        (bank == "MSP" ? s.InterruptStackPointer != 0x4700 : s.MasterStackPointer != 0x7400) ||
                        s.M68040Mmu.BypassTranslation)
                        throw new InvalidOperationException("Fault changed registers, flags, inactive stacks or translation state");
                    // For ordinary data-space writes only, 8.4.6.7 example 3
                    // requires WB1 valid, FA=WB1A. Special-space writes are not
                    // promoted to a writeback expectation by this discovery.
                    if (write && fc is 1 or 2 or 5 or 6)
                    {
                        if (bus.Peek(frame + 18, 2) != (uint)(0x80 | size | Attributes[fc]) ||
                            bus.Peek(frame + 40, 4) != 0x4200 + lane)
                            throw new InvalidOperationException("Normal physical write must preserve WB1 status/address");
                        for (var n = 0; n < width; n++)
                            if (bus.Peek(frame + 44 + ((lane + (uint)n) & 3)) != (byte)(0x89abcdefu >> (8 * (width - 1 - n))))
                                throw new InvalidOperationException("Memory-aligned WB1 data lane differs");
                    }
                    if (bus.Accesses.Any(a => a.Address == 0x4200 + lane && a.Kind is
                        M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite))
                        throw new InvalidOperationException("Rejected operand was retried or completed implicitly");
                    foreach (var at in before.Memory.Keys.Concat(bus.Memory.Keys).Distinct())
                        if ((at < frame || at >= stack) && before.Memory.GetValueOrDefault(at) != bus.Peek(at))
                            throw new InvalidOperationException($"Unrelated memory changed at {at:X8}");
                }
                if (s.SourceFunctionCode != (uint)(write ? 7 - fc : fc) ||
                    s.DestinationFunctionCode != (uint)(write ? fc : 7 - fc))
                    throw new InvalidOperationException("MOVES changed SFC/DFC registers");
                report.Record(id, "passing", null);
            }
            catch (Exception ex) { report.Record(id, "mismatching", ex.Message); }
        }
        report.Complete(output);
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
    }
}
