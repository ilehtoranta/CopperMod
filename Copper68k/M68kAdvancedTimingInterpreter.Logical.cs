/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private static bool IsAdvancedBitIndexedInstruction(ushort opcode) => IsStatusTransfer(opcode) || (opcode & 0xffc0) == 0x06c0 || (opcode & 0xff00) == 0x0e00 || (opcode & 0xf9c0) is 0x00c0 or 0x02c0 or 0x04c0 || (opcode & 0xf140) == 0x4100 || (opcode & 0xf0c0) == 0x50c0 || (opcode & 0xf8c0) == 0xe8c0 || (opcode & 0xffc0) is 0x0ac0 or 0x0cc0 or 0x0ec0;

    private static bool IsGeneralLogical(ushort opcode)
    {
        var mode = (opcode >> 3) & 7; var reg = opcode & 7; var size = (opcode >> 6) & 3;
        var alterable = mode != 1 && (mode != 7 || reg <= 1);
        var data = mode != 1 && (mode != 7 || reg <= 4);
        var top = opcode >> 12; var opmode = (opcode >> 6) & 7;
        if (top is 8 or 12) return opmode < 3 ? data : opmode is >= 4 and <= 6 && mode >= 2 && alterable;
        if (top == 11) return opmode is >= 4 and <= 6 && alterable;
        if ((opcode & 0xff00) is 0x0000 or 0x0200 or 0x0a00) return size < 3 && alterable;
        if ((opcode & 0xffc0) == 0x4ac0) return alterable;
        if ((opcode & 0xff00) is 0x4000 or 0x4200 or 0x4400 or 0x4600 or 0x4a00)
            return size < 3 && ((opcode & 0xff00) == 0x4a00 ? (mode != 1 || size != 0) && (mode != 7 || reg <= 4) : alterable);
        if ((opcode & 0xf100) == 0x0100 || (opcode & 0xff00) == 0x0800)
            return ((opcode >> 6) & 3) == 0 ? data && ((opcode & 0x100) != 0 || mode != 7 || reg != 4) : alterable;
        return top == 14 && (size != 3 || (opcode & 0x0800) == 0 && mode >= 2 && alterable);
    }

    private bool TryExecuteGeneralLogical(ushort opcode)
    {
        if (!IsGeneralLogical(opcode)) return false;
        BeginInstruction(opcode); _ = FetchWord();
        var mode = (opcode >> 3) & 7; var reg = opcode & 7; var dr = (opcode >> 9) & 7;
        var top = opcode >> 12; var opmode = (opcode >> 6) & 7;
        var bit = (opcode & 0xf100) == 0x0100 || (opcode & 0xff00) == 0x0800;
        var shift = top == 14; var memoryShift = shift && (opcode & 0xc0) == 0xc0;
        var tas = (opcode & 0xffc0) == 0x4ac0;
        var size = bit ? (mode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte) :
            tas ? M68kOperandSize.Byte : memoryShift ? M68kOperandSize.Word : (M68kOperandSize)(1 << ((opcode >> 6) & 3));
        var unary = top == 4;
        var immediate = !bit && top == 0;
        var store = unary || immediate || bit || memoryShift || !shift && opmode >= 4;
        uint argument = bit ? ((opcode & 0x100) != 0 ? State.D[dr] : (uint)FetchWord()) :
            immediate ? (size == M68kOperandSize.Long ? FetchLong() : FetchWord()) : State.D[dr];
        var address = 0u;
        uint value;
        if (mode < 2 || shift && !memoryShift) value = mode == 1 && !shift ? State.A[reg] : State.D[reg];
        else if (mode == 7 && reg == 4) value = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
        else
        {
            address = ResolveMoveAddress(mode, reg, size, opcode);
            // CLR on 020+ performs a write without the 000's destination read.
            value = (opcode & 0xff00) == 0x4200 ? 0 : ReadSized(address, size);
            if (mode == 3) WriteGeneralRegister(true, reg, unchecked(address + M68kIntegerSemantics.AddressIncrement(reg, size)));
        }
        uint result; var write = true;
        if (bit)
        {
            var mask = 1u << (int)(argument & (mode == 0 ? 31u : 7u));
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            result = ((opcode >> 6) & 3) switch { 1 => value ^ mask, 2 => value & ~mask, 3 => value | mask, _ => value };
            write = ((opcode >> 6) & 3) != 0;
        }
        else if (shift)
        {
            var count = memoryShift ? 1 : (opcode & 0x20) != 0 ? (int)(State.D[dr] & 63) : dr == 0 ? 8 : dr;
            var type = memoryShift ? (opcode >> 9) & 3 : (opcode >> 3) & 3;
            var shifted = M68kIntegerSemantics.Shift(value, count, size, type, (opcode & 0x100) != 0, State.GetFlag(M68kCpuState.Extend));
            result = shifted.Value;
            SetMoveFlags(result, size);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged) State.SetFlag(M68kCpuState.Extend, shifted.Extend);
        }
        else if (unary)
        {
            var family = opcode & 0xff00;
            result = family switch { 0x4200 => 0, 0x4600 => ~value, 0x4400 => unchecked(0u - value),
                0x4000 => unchecked(0u - value - (State.GetFlag(M68kCpuState.Extend) ? 1u : 0u)), _ => tas ? value | 128u : value };
            result &= M68kCpuState.Mask(size);
            if (family == 0x4400) SetSubtractFlags(0, value, result, size);
            else if (family == 0x4000) SetSubxFlags(0, value, result, size);
            else SetMoveFlags(tas ? value : result, size);
            write = tas || family != 0x4a00;
        }
        else
        {
            var destination = store ? value : State.D[dr]; var source = store ? argument : value;
            var operation = immediate ? opcode & 0xff00 : top == 12 ? 0x0200 : top == 11 ? 0x0a00 : 0;
            result = operation switch { 0x0200 => destination & source, 0x0a00 => destination ^ source, _ => destination | source };
            SetMoveFlags(result, size);
        }
        if (write)
        {
            if (shift && !memoryShift || store && mode == 0) WriteDataRegisterSized(reg, result, size);
            else if (!store) WriteDataRegisterSized(dr, result, size);
            else WriteSized(address, result, size);
        }
        CompleteGeneralLogicalTiming(mode, reg, write && store && mode >= 2 && !(shift && !memoryShift));
        return true;
    }

    private void CompleteGeneralLogicalTiming(int mode, int reg, bool readModifyWrite)
    {
        var cycles = _profile.FixedInstructionNativeCycles ?? 4 + MoveEaPolicyCycles(mode, reg) + _indexedOperandExtraCycles;
        var barriers = readModifyWrite ? M68kTimingBarrier.ReadModifyWrite : M68kTimingBarrier.None;
        CompleteTimingPlan(_profile.Model == M68kAcceleratorModel.M68030
            ? M68kInstructionPlan.CreateHeadTail(M68kInstructionTimingKey.GeneralLogical, "integer logical general EA", cycles, 2, 0, barriers)
            : M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralLogical, "integer logical general EA", cycles, barriers));
    }

    private void CompleteAtomicTiming()
    {
        var plan = _timing.GetPlan(M68kInstructionTimingKey.Nop);
        plan = plan with { NativeCycles = _profile.FixedInstructionNativeCycles ?? plan.NativeCycles + _indexedOperandExtraCycles,
            Barriers = plan.Barriers | M68kTimingBarrier.ReadModifyWrite };
        CompleteTimingPlan(plan);
    }
}
