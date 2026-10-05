using Copper68k;

namespace Copper68k.Tests.Synthetic;

// Shared register-transfer fixture; masks and encodings are independent
// specifications, not results computed with production decoder/MMU helpers.
internal sealed record SyntheticMovecRegisterFixture(int Control, string Name, uint Mask,
    Func<M68kCpuState, uint> Read, Action<M68kCpuState, uint> Initialize,
    Func<uint, uint, uint>? StoredValue = null)
{
    private static readonly (string Name, Func<M68kCpuState, uint> Read)[] Preserved =
    [
        ("TC", s => s.M68040Mmu.TranslationControl),
        ("ITT0", s => s.M68040Mmu.InstructionTransparentTranslation0),
        ("ITT1", s => s.M68040Mmu.InstructionTransparentTranslation1),
        ("DTT0", s => s.M68040Mmu.DataTransparentTranslation0),
        ("DTT1", s => s.M68040Mmu.DataTransparentTranslation1),
        ("URP", s => s.M68040Mmu.UserRootPointer),
        ("SRP", s => s.M68040Mmu.SupervisorRootPointer),
        ("BUSCR", s => s.M68060BusControl)
    ];

    internal void WritesAndReadback(SyntheticMachine m, CoverageBatch report, IEnumerable<uint> values, int storeRegisters = 16, uint initial = 0)
    {
        foreach (var value in values)
        for (var general = 0; general < storeRegisters; general++)
        foreach (var supervisor in new[] { false, true })
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr, supervisor);
            if (general < 8) m.Core.State.D[general] = value;
            else if (general == 15) m.Core.State.SetActiveStackPointer(value);
            else m.Core.State.A[general - 8] = value;
            var e = SyntheticExecution.Prepare(m, [0x4e7b, (ushort)(general << 12 | Control), 0x4e7a, (ushort)(general << 12 | Control)]);
            Initialize(m.Core.State, initial);
            e.Pc = SyntheticMachine.Code + 4;
            Preserve(m, e);
            var id = $"{m.Model.Id}/MOVEC/L/{Name}/R{general}/initial={initial:X8}/value={value:X8}/super={supervisor}/ccr={ccr:X2}";
            if (!supervisor)
            {
                SyntheticExecution.ExpectException(m, e, 8);
                SyntheticExecution.Run(m, e, report, id + "/privilege", false);
                continue;
            }
            var stored = StoredValue?.Invoke(initial, value) ?? value & Mask;
            e.ControlChecks[Name] = (Read, stored);
            if (!Step(m, e, report, id + "/write"))
            {
                report.Record(id + "/read", "untested", $"{Name} write failed; dependent readback not executed.");
                continue;
            }
            e.Pc += 4;
            if (general < 8) e.D[general] = stored;
            else e.A[general - 8] = stored;
            SyntheticExecution.Run(m, e, report, id + "/read");
        }
    }

    internal void RawReads(SyntheticMachine m, CoverageBatch report, IEnumerable<uint> values)
    {
        foreach (var value in values)
        for (var general = 0; general < 16; general++)
        for (var ccr = 0; ccr < 32; ccr++)
        {
            m.Reset(ccr);
            var e = SyntheticExecution.Prepare(m, [0x4e7a, (ushort)(general << 12 | Control)]);
            Initialize(m.Core.State, value);
            Preserve(m, e);
            if (general < 8) e.D[general] = value & Mask;
            else e.A[general - 8] = value & Mask;
            SyntheticExecution.Run(m, e, report, $"{m.Model.Id}/MOVEC/L/{Name}/read-R{general}/internal={value:X8}/ccr={ccr:X2}");
        }
    }

    private static void Preserve(SyntheticMachine m, ArchitecturalExpectation e)
    {
        foreach (var (name, read) in Preserved) e.ControlChecks[name] = (read, read(m.Core.State));
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
