namespace Copper68k.Tests.Synthetic;

// Integer exception-handler program for supplied normal format-7 writebacks.
// MC68040UM 8.4.6.3/5/7: WB1 is memory aligned, WB2/3 register aligned;
// software completes WB1 -> WB2 -> WB3. This does not generate a data fault.
internal sealed class SyntheticM68040WritebackProgram
{
    internal sealed record Instruction(uint Pc, ushort[] Words, string Operation, int Slot = 0, string? Target = null);
    internal const uint Entry = SyntheticMachine.Code;
    internal readonly List<Instruction> Instructions = [];
    internal readonly Dictionary<string, uint> Labels = [];
    private uint pc;
    internal static int StatusOffset(int slot) => 20 - 2 * slot;
    internal static int AddressOffset(int slot) => 48 - 8 * slot;
    internal static int DataOffset(int slot) => AddressOffset(slot) + 4;

    internal SyntheticM68040WritebackProgram(uint entry = Entry)
    {
        pc = entry;
        Emit("save", 0, [0x48e7, 0xf0c0]); // MOVEM.L D0-D3/A0-A1,-(A7)
        Emit("frame", 0, [0x43ef, 24]); // LEA 24(A7),A1
        Emit("save-DFC", 0, [0x4e7a, 0x3001]);
        for (var slot = 1; slot <= 3; slot++)
        {
            Emit("zero-status", slot, [0x7400]);
            Emit("status", slot, [0x1429, (ushort)(StatusOffset(slot) + 1)]);
            Emit("valid", slot, [0x0802, 7]);
            Branch("invalid", slot, $"next{slot}", 0x6700);
            Emit("data", slot, [0x2029, (ushort)DataOffset(slot)]);
            Emit("address", slot, [0x2069, (ushort)AddressOffset(slot)]);
            Emit("zero-DFC", slot, [0x7200]);
            Emit("DFC-byte", slot, [0x1229, (ushort)(StatusOffset(slot) + 1)]);
            Emit("DFC-mask", slot, [0x0281, 0, 7]);
            Emit("DFC-write", slot, [0x4e7b, 0x1001]);
            if (slot == 1)
            {
                Emit("lane-address", slot, [0x2208]);
                Emit("lane-mask", slot, [0x0281, 0, 3]);
                Emit("lane-count", slot, [0xe789]); // LSL.L #3,D1
                Emit("lane-rotate", slot, [0xe3b8]); // ROL.L D1,D0
            }
            Emit("size", slot, [0x0282, 0, 0x60]);
            Branch("long-branch", slot, $"long{slot}", 0x6700);
            Emit("byte-compare", slot, [0x0c82, 0, 0x20]);
            Branch("byte-branch", slot, $"byte{slot}", 0x6700);
            if (slot == 1) Emit("word-align", slot, [0x4840]); // SWAP D0
            Emit("store-word", slot, [0x0e50, 0x0800]); // MOVES.W D0,(A0)
            Branch("stored", slot, $"clear{slot}", 0x6000);
            Mark($"byte{slot}");
            if (slot == 1) Emit("byte-align", slot, [0xe198]); // ROL.L #8,D0
            Emit("store-byte", slot, [0x0e10, 0x0800]);
            Branch("stored", slot, $"clear{slot}", 0x6000);
            Mark($"long{slot}");
            Emit("store-long", slot, [0x0e90, 0x0800]);
            Mark($"clear{slot}");
            Emit("clear-valid", slot, [0x08a9, 7, (ushort)(StatusOffset(slot) + 1)]);
            Mark($"next{slot}");
        }
        Emit("restore-DFC", 0, [0x4e7b, 0x3001]);
        Emit("restore", 0, [0x4cdf, 0x030f]);
        Emit("return", 0, [0x4e73]);
        foreach (var instruction in Instructions.Where(i => i.Target != null))
        {
            var displacement = checked((short)(Labels[instruction.Target!] - (long)(instruction.Pc + 2)));
            instruction.Words[1] = unchecked((ushort)displacement);
        }
    }
    private void Emit(string operation, int slot, ushort[] words, string? target = null)
    { Instructions.Add(new(pc, words, operation, slot, target)); pc += (uint)words.Length * 2; }
    private void Branch(string operation, int slot, string target, ushort opcode) => Emit(operation, slot, [opcode, 0], target);
    private void Mark(string label) => Labels.Add(label, pc);
    internal void Initialize(SyntheticMachine m)
    {
        foreach (var instruction in Instructions)
            for (var n = 0; n < instruction.Words.Length; n++)
                m.InitializePhysical(instruction.Pc + (uint)n * 2, instruction.Words[n], 2);
    }

    internal void Expect(Instruction i, SyntheticMachine m, ArchitecturalExpectation e,
        uint frame, ushort[] statuses, uint[] addresses, uint[] data, uint[] originalD, uint[] originalA, uint originalDfc,
        Dictionary<string, uint> stacks, List<(uint Address, int Width, uint Value)> stores)
    {
        e.Pc = i.Pc + (uint)i.Words.Length * 2;
        var slot = i.Slot - 1;
        uint value;
        switch (i.Operation)
        {
            case "save":
                M68040StackFixture.SetStacks(e, stacks, e.Sr);
                stacks[M68040StackFixture.ExceptionBank(e.Sr)] = frame - 24;
                M68040StackFixture.SetStacks(e, stacks, e.Sr);
                uint[] saved = [originalD[0], originalD[1], originalD[2], originalD[3], originalA[0], originalA[1]];
                for (var n = 0; n < saved.Length; n++) e.Write(frame - 24 + (uint)n * 4, saved[n], 4, m.Model);
                break;
            case "frame": e.A[1] = frame; break;
            case "save-DFC": e.D[3] = originalDfc; break;
            case "zero-status": e.D[2] = 0; Flags(e, 0, 4); break;
            case "status": e.D[2] = statuses[slot]; Flags(e, e.D[2], 1); break;
            case "valid": e.Sr = (ushort)((e.Sr & ~4) | ((statuses[slot] & 0x80) == 0 ? 4 : 0)); break;
            case "invalid": if ((statuses[slot] & 0x80) == 0) e.Pc = Labels[i.Target!]; break;
            case "data": e.D[0] = data[slot]; Flags(e, e.D[0], 4); break;
            case "address": e.A[0] = addresses[slot]; break;
            case "zero-DFC": e.D[1] = 0; Flags(e, 0, 4); break;
            case "DFC-byte": e.D[1] = statuses[slot]; Flags(e, e.D[1], 1); break;
            case "DFC-mask": e.D[1] &= 7; Flags(e, e.D[1], 4); break;
            case "DFC-write": e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, e.D[1]); break;
            case "lane-address": e.D[1] = addresses[slot]; Flags(e, e.D[1], 4); break;
            case "lane-mask": e.D[1] &= 3; Flags(e, e.D[1], 4); break;
            case "lane-count": e.D[1] <<= 3; e.Sr &= 0xffef; Flags(e, e.D[1], 4); break;
            case "lane-rotate":
                var count = (int)e.D[1]; e.D[0] = RotateBytes(e.D[0], count / 8);
                Flags(e, e.D[0], 4); if (count != 0 && (e.D[0] & 1) != 0) e.Sr |= 1;
                break;
            case "size": e.D[2] &= 0x60; Flags(e, e.D[2], 4); break;
            case "long-branch": if ((statuses[slot] & 0x60) == 0) e.Pc = Labels[i.Target!]; break;
            case "byte-compare": Flags(e, e.D[2] - 0x20, 4); break;
            case "byte-branch": if ((statuses[slot] & 0x60) == 0x20) e.Pc = Labels[i.Target!]; break;
            case "word-align": e.D[0] = (e.D[0] << 16) | (e.D[0] >> 16); Flags(e, e.D[0], 4); break;
            case "byte-align": e.D[0] = RotateBytes(e.D[0], 1); Flags(e, e.D[0], 4); e.Sr |= (ushort)(e.D[0] & 1); break;
            case "store-byte": case "store-word": case "store-long":
                var width = i.Operation == "store-byte" ? 1 : i.Operation == "store-word" ? 2 : 4;
                value = e.D[0] & (width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue);
                e.Write(addresses[slot], value, width, m.Model); stores.Add((addresses[slot], width, value));
                break;
            case "stored": e.Pc = Labels[i.Target!]; break;
            case "clear-valid": e.Write(frame + (uint)StatusOffset(i.Slot) + 1, (uint)(statuses[slot] & 0x7f), 1, m.Model); e.Sr &= 0xfffb; break;
            case "restore-DFC": e.ControlChecks["DFC"] = (s => s.DestinationFunctionCode, originalDfc); break;
            case "restore":
                Array.Copy(originalD, e.D, 4); Array.Copy(originalA, e.A, 2);
                stacks[M68040StackFixture.ExceptionBank(e.Sr)] = frame;
                M68040StackFixture.SetStacks(e, stacks, e.Sr); break;
            // RTE/trace are checked by the caller against the independently supplied header.
            case "return": break;
            default: throw new InvalidOperationException(i.Operation);
        }
    }
    private static void Flags(ArchitecturalExpectation e, uint value, int width)
    {
        var mask = width == 1 ? 0xffu : width == 2 ? 0xffffu : uint.MaxValue;
        var sign = width == 1 ? 0x80u : width == 2 ? 0x8000u : 0x80000000u;
        value &= mask;
        e.Sr = (ushort)((e.Sr & 0xfff0) | (value == 0 ? 4 : 0) | ((value & sign) != 0 ? 8 : 0));
    }
    // Byte-array permutation, independent of the production rotate implementation.
    private static uint RotateBytes(uint value, int lanes)
    {
        byte[] bytes = [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        uint result = 0; for (var n = 0; n < 4; n++) result = result << 8 | bytes[(n + lanes) & 3];
        return result;
    }
}
