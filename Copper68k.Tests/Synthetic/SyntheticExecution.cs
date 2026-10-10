using Copper68k;

namespace Copper68k.Tests.Synthetic;

internal static class SyntheticExecution
{
    public static ArchitecturalExpectation Prepare(SyntheticMachine machine, IReadOnlyList<ushort> words)
    {
        for (var i = 0; i < words.Count; i++) machine.Bus.Initialize(SyntheticMachine.Code + (uint)i * 2, words[i], 2);
        var nextPc = SyntheticMachine.Code + (uint)words.Count * 2;
        machine.Bus.Initialize(nextPc, 0x4e71, 2);
        machine.Bus.Initialize(nextPc + 2, 0x4e71, 2);
        machine.Start();
        var expected = ArchitecturalExpectation.Capture(machine);
        expected.Pc = nextPc;
        return expected;
    }

    public static void ExpectException(SyntheticMachine machine, ArchitecturalExpectation expected, int vector, uint? savedProgramCounter = null)
    {
        var format2 = vector is 5 or 6 or 7 or 9 && machine.Model.FullIndex;
        var frameSize = format2 ? 12u : machine.Model.Model == M68kCpuModel.M68000 ? 6u : 8u;
        var savedSr = expected.Sr;
        var savedPc = savedProgramCounter ?? SyntheticMachine.Code;
        if ((savedSr & 0x2000) == 0)
        {
            var userStack = expected.A[7];
            expected.A[7] = expected.InactiveStackPointer!.Value;
            expected.InactiveStackPointer = userStack;
        }
        expected.A[7] -= frameSize;
        expected.Write(expected.A[7], savedSr, 2, machine.Model);
        expected.Write(expected.A[7] + 2, savedPc, 4, machine.Model);
        expected.MemoryMasks[machine.Model.Physical(expected.A[7])] = (byte)(expected.DefinedSrMask >> 8);
        expected.MemoryMasks[machine.Model.Physical(expected.A[7] + 1)] = (byte)expected.DefinedSrMask;
        if (frameSize >= 8) expected.Write(expected.A[7] + 6, (format2 ? 0x2000u : 0) | (uint)vector * 4, 2, machine.Model);
        if (format2) expected.Write(expected.A[7] + 8, SyntheticMachine.Code, 4, machine.Model);
        expected.Sr = (ushort)((savedSr | 0x2000) & ~0xc000);
        uint handler = 0;
        for (var i = 0; i < 4; i++) handler = (handler << 8) | expected.Memory.GetValueOrDefault(machine.Model.Physical(machine.Core.State.VectorBaseRegister + (uint)vector * 4 + (uint)i));
        expected.Pc = handler;
        expected.ExceptionVector = vector;
    }

    public static void Run(SyntheticMachine machine, ArchitecturalExpectation expected, CoverageBatch report, string id, bool followingInstruction = true)
    {
        try
        {
            machine.Core.ExecuteInstruction();
            var mismatch = expected.Verify(machine);
            if (mismatch == null && expected.ExceptionVector == null && followingInstruction)
            {
                machine.Core.ExecuteInstruction();
                expected.Pc += 2;
                mismatch = expected.Verify(machine);
            }
            report.Record(id, mismatch == null ? "passing" : "mismatching", mismatch);
        }
        catch (Exception ex) when (ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException or UnsupportedM68040InstructionException)
        { report.Record(id, "unsupported", ex.Message); }
        catch (Exception ex) { report.Record(id, "mismatching", ex.ToString()); }
    }

    public static ushort MoveFlags(ushort sr, uint value, int width)
    {
        value &= MoveSpecification.Mask(width);
        return (ushort)((sr & 0xfff0) | (value == 0 ? 4 : 0) | ((value & (1u << (width * 8 - 1))) != 0 ? 8 : 0));
    }
}
