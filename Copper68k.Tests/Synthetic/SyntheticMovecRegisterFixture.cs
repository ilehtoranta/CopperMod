using Copper68k;

namespace Copper68k.Tests.Synthetic;

// Shared register-transfer fixture; masks and encodings are independent
// specifications, not results computed with production decoder/MMU helpers.
internal sealed record SyntheticMovecRegisterFixture(int Control, string Name, uint Mask,
    Func<M68kCpuState, uint> Read, Action<M68kCpuState, uint> Initialize,
    Func<uint, uint, uint>? StoredValue = null, bool UserStackControl = false,
    Action<SyntheticMachine>? PrepareMemory = null, Action<M68kCpuState>? InitializeControls = null)
{
    private static readonly (string Name, Func<M68kCpuState, uint> Read)[] Preserved =
    [
        ("SFC", s => s.SourceFunctionCode),
        ("DFC", s => s.DestinationFunctionCode),
        ("VBR", s => s.VectorBaseRegister),
        ("TC", s => s.M68040Mmu.TranslationControl),
        ("ITT0", s => s.M68040Mmu.InstructionTransparentTranslation0),
        ("ITT1", s => s.M68040Mmu.InstructionTransparentTranslation1),
        ("DTT0", s => s.M68040Mmu.DataTransparentTranslation0),
        ("DTT1", s => s.M68040Mmu.DataTransparentTranslation1),
        ("URP", s => s.M68040Mmu.UserRootPointer),
        ("SRP", s => s.M68040Mmu.SupervisorRootPointer),
        ("BUSCR", s => s.M68060BusControl),
        ("PCR", s => s.M68060ProcessorConfiguration)
    ];

    internal void WritesAndReadback(SyntheticMachine m, CoverageBatch report, IEnumerable<uint> values, int storeRegisters = 16,
        uint initial = 0, IEnumerable<int>? conditionCodes = null, IEnumerable<int>? readRegisters = null, bool groupConditionCodes = false)
    {
        foreach (var value in values)
        for (var general = 0; general < storeRegisters; general++)
        foreach (var destination in readRegisters ?? [general])
        foreach (var supervisor in new[] { false, true })
        foreach (var ccr in conditionCodes ?? Enumerable.Range(0, 32))
        {
            m.Reset(ccr, supervisor);
            SetGeneral(m.Core.State, general, value);
            PrepareMemory?.Invoke(m);
            SyntheticExecution.Prepare(m, [0x4e7b, (ushort)(general << 12 | Control), 0x4e7a, (ushort)(destination << 12 | Control)]);
            InitializeControls?.Invoke(m.Core.State);
            Initialize(m.Core.State, initial);
            // USP initialization may alias a user-mode A7 source. Source setup
            // is last, and the expectation captures that intended initial state.
            SetGeneral(m.Core.State, general, value);
            var e = ArchitecturalExpectation.Capture(m);
            e.Pc = SyntheticMachine.Code + 4;
            Preserve(m, e);
            var id = $"{m.Model.Id}/MOVEC/L/{Name}/R{general}/initial={initial:X8}/value={value:X8}/super={supervisor}";
            if (readRegisters != null) id += $"/read-R{destination}";
            if (!groupConditionCodes) id += $"/ccr={ccr:X2}";
            var sample = groupConditionCodes ? $"/op=4E7B/ccr={ccr:X2}" : "";
            var readSample = groupConditionCodes ? $"/op=4E7A/ccr={ccr:X2}" : "";
            if (!supervisor)
            {
                SyntheticExecution.ExpectException(m, e, 8);
                SyntheticExecution.Run(m, e, report, id + "/privilege" + sample, false);
                continue;
            }
            var stored = StoredValue?.Invoke(initial, value) ?? value & Mask;
            e.ControlChecks[Name] = (Read, stored);
            if (UserStackControl) e.InactiveStackPointer = stored;
            if (!Step(m, e, report, id + "/write" + sample))
            {
                report.Record(id + "/read" + readSample, "untested", $"{Name} write failed; dependent readback not executed.");
                continue;
            }
            e.Pc += 4;
            if (destination < 8) e.D[destination] = stored;
            else e.A[destination - 8] = stored;
            SyntheticExecution.Run(m, e, report, id + "/read" + readSample);
        }
    }

    internal void RawReads(SyntheticMachine m, CoverageBatch report, IEnumerable<uint> values, bool groupConditionCodes = false)
    {
        foreach (var value in values)
        for (var general = 0; general < 16; general++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr);
            PrepareMemory?.Invoke(m);
            SyntheticExecution.Prepare(m, [0x4e7a, (ushort)(general << 12 | Control)]);
            InitializeControls?.Invoke(m.Core.State);
            Initialize(m.Core.State, value);
            var e = ArchitecturalExpectation.Capture(m);
            e.Pc = SyntheticMachine.Code + 4;
            Preserve(m, e);
            if (general < 8) e.D[general] = value & Mask;
            else e.A[general - 8] = value & Mask;
            SyntheticExecution.Run(m, e, report, $"{m.Model.Id}/MOVEC/L/{Name}/read-R{general}/internal={value:X8}{(groupConditionCodes ? "/op=4E7A" : "")}/ccr={ccr:X2}");
        }
    }

    private static void SetGeneral(M68kCpuState state, int general, uint value)
    {
        if (general < 8) state.D[general] = value;
        else if (general == 15) state.SetActiveStackPointer(value);
        else state.A[general - 8] = value;
    }

    private static void Preserve(SyntheticMachine m, ArchitecturalExpectation e)
    {
        foreach (var (name, read) in Preserved) e.ControlChecks[name] = (read, read(m.Core.State));
        if (m.Model.Id == "68010") e.ControlChecks["010 stack model"] = (s => s.M68020StackModeEnabled ? 1u : 0, 0);
    }

    private static bool Step(SyntheticMachine m, ArchitecturalExpectation e, CoverageBatch report, string id)
    {
        try
        {
            m.Core.ExecuteInstruction();
            var mismatch = e.Verify(m);
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
            return mismatch == null;
        }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
        { report.Record(id, "unsupported", ex.Message); return false; }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); return false; }
    }
}
