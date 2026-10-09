using System.Globalization;
using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// MC68040UM 8.4.2/3, MC68040UMAD general-operation item 3. The last
// committed throwaway SR is the secondary image handed to address-error entry
// by pinned WinUAE's generated 040 RTE. The separate native audit executes that
// handoff, without claiming a complete WinUAE exception-frame or fault oracle.
public sealed class SyntheticM68040ChainedOddReturnTests(ITestOutputHelper output)
{
    private static readonly string[] Banks = ["user", "user-M", "ISP", "MSP"];
    private static readonly ushort[] Traces = [0, 0x8000, 0x4000];
    private const uint Handler = 0x9190;

    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddShortFrameCanonicalScalar() => Audit(false, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddShortFrameCanonicalBatch() => Audit(false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddShortFrameStructuralScalar() => Audit(true, false);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddShortFrameStructuralBatch() => Audit(true, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddAccessFrameCanonicalScalar() => Audit(false, false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddAccessFrameCanonicalBatch() => Audit(false, true, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddAccessFrameStructuralScalar() => Audit(true, false, true);
    [Fact, Trait("Suite", "Synthetic")]
    public void ChainedOddAccessFrameStructuralBatch() => Audit(true, true, true);

    private void Audit(bool structural, bool batch, bool access = false)
    {
        var group = $"rte-chained-odd-{(access ? "access-" : "")}{(structural ? "structure" : "canonical")}-{(batch ? "batch" : "scalar")}";
        var report = new CoverageBatch("68040", group);
        var m = new SyntheticMachine(ModelSpec.All.Single(x => x.Id == "68040"));
        var export = Environment.GetEnvironmentVariable("COPPER68K_040_RTE_HANDOFF_EXPORT");
        using var rows = string.IsNullOrWhiteSpace(export) ? null : new StreamWriter(Path.Combine(export, group + ".rows"));
        var index = 0;
        foreach (var start in new[] { "ISP", "MSP" })
        foreach (var tail in Banks)
        foreach (var middle in structural ? new[] { "none", "user", "user-M", "ISP", "MSP" } : ["none"])
        foreach (var result in Banks)
        foreach (var incoming in Traces)
        foreach (var first in Traces)
        foreach (var second in middle == "none" ? new ushort[] { 0 } : Traces)
        foreach (var trace in Traces)
        foreach (var target in structural ? new uint[] { 0x6001, 0xff002003 } : [0x6001])
        foreach (var alignment in structural ? new uint[] { 0, 1 } : [0])
        foreach (var vbr in structural ? new uint[] { 0, 0x10000 } : [0x10000])
        foreach (var form in access ? new[] { "normal", "CM" } : ["format0", "format2", "format3"])
        foreach (var ccr in structural ? new[] { 0, 31 } : Enumerable.Range(0, 32))
        {
            m.Reset(ccr);
            var pointers = new Dictionary<string, uint> { ["user"] = 0x7800 + alignment, ["ISP"] = 0x4700 + alignment, ["MSP"] = 0x7400 + alignment };
            foreach (var location in pointers.Values)
                for (var n = -20; n < 112; n++) m.InitializePhysical(unchecked(location + (uint)n), (uint)(n ^ 0x5a), 1);
            var path = middle == "none" ? new[] { start, tail } : new[] { start, middle, tail };
            var frames = new List<(uint Address, ushort Sr, uint Pc, ushort Format)>();
            for (var n = 0; n < path.Length - 1; n++)
            {
                var at = pointers[Physical(path[n])];
                frames.Add((at, Status(path[n + 1], n == 0 ? first : second, ccr), 0xdead0001u + (uint)n * 2, 0x1024));
                pointers[Physical(path[n])] += 8;
            }
            var restoredSr = Status(result, trace, ccr ^ 31);
            var format = access ? 7 : form == "format0" ? 0 : form == "format2" ? 2 : 3;
            frames.Add((pointers[Physical(tail)], restoredSr, target, (ushort)(format << 12 | 8)));
            foreach (var f in frames)
            {
                m.InitializePhysical(f.Address, f.Sr, 2); m.InitializePhysical(f.Address + 2, f.Pc, 4);
                m.InitializePhysical(f.Address + 6, f.Format, 2); m.InitializePhysical(f.Address + 8, 0x4200, 4);
            }
            var tailFrame = frames[^1].Address;
            var ssw = form == "CM" ? 0x1005u : 0x0105u;
            if (access)
            {
                m.InitializePhysical(tailFrame + 12, ssw, 2);
                foreach (var offset in new[] { 14u, 16u, 18u }) m.InitializePhysical(tailFrame + offset, 0x85, 2);
                foreach (var offset in new[] { 24u, 32u, 40u })
                {
                    m.InitializePhysical(tailFrame + offset, 0x3200 + offset, 4);
                    m.InitializePhysical(tailFrame + offset + 4, 0xc3a55a3c, 4);
                    m.InitializePhysical(0x3200 + offset, 0x96abcdef, 4);
                }
            }
            pointers[Physical(tail)] += access ? 60u : format == 0 ? 8u : 12u;
            _ = SyntheticExecution.Prepare(m, [0x4e73]);
            m.InitializePhysical(vbr + 12, Handler, 4); m.InitializePhysical(Handler, 0x60fe, 2);
            m.Core.State.SetUserStackPointer(0x7800 + alignment);
            m.Core.State.SetInterruptStackPointer(0x4700 + alignment);
            m.Core.State.SetMasterStackPointer(0x7400 + alignment);
            var initialSr = Status(start, incoming, ccr);
            m.Core.State.StatusRegister = initialSr; m.Core.State.VectorBaseRegister = vbr;
            var e = ArchitecturalExpectation.Capture(m);
            var priorSr = frames[^2].Sr;
            // Documented 040 traced-user-return correction is applied to the
            // secondary SR image, not substituted with the final restored SR.
            var savedSr = (ushort)(priorSr | ((restoredSr & 0x2000) == 0 && trace != 0 ? 0x2000 : 0));
            var bank = (restoredSr & 0x1000) == 0 ? "ISP" : "MSP";
            pointers[bank] -= 12;
            var sp = pointers[bank];
            e.Sr = (ushort)((restoredSr | 0x2000) & ~0xc000); e.Pc = Handler; e.ExceptionVector = 3;
            e.A[7] = sp; e.InactiveStackPointer = pointers["user"]; e.MasterStackPointer = pointers["MSP"];
            e.ControlChecks["USP"] = (s => s.UserStackPointer, pointers["user"]);
            e.ControlChecks["ISP"] = (s => s.InterruptStackPointer, pointers["ISP"]);
            e.ControlChecks["MSP"] = (s => s.MasterStackPointer, pointers["MSP"]);
            e.ControlChecks["saved SR provenance"] = (s => s.LastExceptionStatusRegister, savedSr);
            e.ControlChecks["saved RTE PC"] = (s => s.LastExceptionStackedProgramCounter, SyntheticMachine.Code);
            e.ControlChecks["single address error"] = (s => s.ExceptionSequence, m.Core.State.ExceptionSequence + 1);
            e.ControlChecks["odd targets never fetched"] = (_ => m.Bus.Accesses.Any(a => !a.Write &&
                (a.Address == target || frames.Take(frames.Count - 1).Any(f => f.Pc == a.Address))) ? 1u : 0u, 0);
            var expectedReads = frames.SelectMany(f => new[] { (f.Address, 2), (f.Address + 2, 4), (f.Address + 6, 2) }).ToArray();
            var validationReads = expectedReads.Concat(access ? form == "CM" ? new[] { (tailFrame + 12, 2), (tailFrame + 8, 4) }
                : [(tailFrame + 12, 2)] : []).ToArray();
            var addresses = expectedReads.Select(r => r.Item1).ToHashSet();
            var validationAddresses = validationReads.Select(r => r.Item1).ToHashSet();
            e.ControlChecks["frame validation read order"] = (_ => m.Bus.Accesses.Where(a => !a.Write && validationAddresses.Contains(a.Address))
                .Select(a => (a.Address, a.Width)).SequenceEqual(validationReads) ? 0u : 1u, 0);
            if (access)
            {
                e.ControlChecks["no MOVEM continuation after odd return"] = (_ => ((M68kAdvancedTimingInterpreter)m.Core).HasPendingM68040MovemContinuation ? 1u : 0u, 0);
                e.ControlChecks["no writeback replay"] = (_ => m.Bus.Accesses.Any(a => a.Address is 0x3218 or 0x3220 or 0x3228) ? 1u : 0u, 0);
            }
            e.Write(sp, savedSr, 2, m.Model); e.Write(sp + 2, SyntheticMachine.Code, 4, m.Model);
            e.Write(sp + 6, 0x200c, 2, m.Model); e.Write(sp + 8, target & ~1u, 4, m.Model);
            var secondName = middle == "none" ? "none" : $"{second:X4}";
            var id = $"68040/RTE/chained-odd/{form}/path={string.Join('-', path)}/result={result}/incoming={incoming:X4}/first={first:X4}/second={secondName}/T={trace:X4}/target={target:X8}/align={alignment}/VBR={vbr:X8}/op=4E73/ccr={ccr:X2}";
            M68040StackFixture.Step(m, e, report, id, batch);
            if (rows != null)
            {
                var s = m.Core.State;
                var input = new List<uint> { (uint)index, initialSr, 0x7800 + alignment, 0x4700 + alignment, 0x7400 + alignment, SyntheticMachine.Code, (uint)frames.Count };
                foreach (var f in frames) input.AddRange([f.Address, f.Sr, f.Pc, f.Format]);
                // Reference observes header/SR handoff only. Declare the SSW/EA
                // so it rejects pending/undefined forms rather than silently
                // treating them as normal. Extra validation reads above are
                // checked independently by the architectural fixture.
                if (access) input.AddRange([ssw, 0x4200]);
                var actual = new List<uint> { s.LastExceptionStatusRegister, s.StatusRegister, s.ProgramCounter, s.LastExceptionStackedProgramCounter,
                    m.PeekPhysical(s.A[7] + 8, 4), s.UserStackPointer, s.InterruptStackPointer, s.MasterStackPointer, (uint)expectedReads.Length };
                foreach (var a in m.Bus.Accesses.Where(a => !a.Write && addresses.Contains(a.Address))) actual.AddRange([a.Address, (uint)a.Width]);
                rows.WriteLine(string.Join(' ', input.Select(Hex)) + " | " + string.Join(' ', actual.Select(Hex)));
            }
            index++;
        }
        report.Complete(output);
    }

    private static string Hex(uint value) => value.ToString("X", CultureInfo.InvariantCulture);
    private static string Physical(string bank) => bank == "user-M" ? "user" : bank;
    private static ushort Status(string bank, ushort trace, int ccr) =>
        (ushort)((bank == "ISP" ? 0x2000 : bank == "MSP" ? 0x3000 : bank == "user-M" ? 0x1000 : 0) | trace | ccr);
}
