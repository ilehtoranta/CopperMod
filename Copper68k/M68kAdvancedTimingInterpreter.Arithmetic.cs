/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private static bool IsArithmeticIndexedInstruction(ushort opcode) => IsGeneralArithmetic(opcode) || (opcode & 0xffc0) is 0x4c00 or 0x4c40 or 0x4800 || (opcode & 0xf0c0) == 0xc0c0 || (opcode & 0xf0c0) == 0x80c0;

    private static bool IsGeneralArithmetic(ushort opcode)
    {
        var mode = (opcode >> 3) & 7; var reg = opcode & 7; var size = (opcode >> 6) & 3;
        var top = opcode >> 12; var opmode = (opcode >> 6) & 7;
        if (top is 9 or 11 or 13)
            return opmode is 3 or 7 ? mode != 7 || reg <= 4 :
                opmode < 3 ? !(size == 0 && mode == 1) && (mode != 7 || reg <= 4) :
                top != 11 && mode >= 2 && (mode != 7 || reg <= 1);
        if (top == 5) return size != 3 && !(size == 0 && mode == 1) && (mode != 7 || reg <= 1);
        return (opcode & 0xff00) is 0x0400 or 0x0600 or 0x0c00 && size != 3 && mode != 1 &&
            (mode != 7 || reg <= ((opcode & 0xff00) == 0x0c00 ? 3 : 1));
    }

    private bool TryExecuteGeneralArithmetic(ushort opcode)
    {
        if (!IsGeneralArithmetic(opcode)) return false;
        BeginInstruction(opcode); _ = FetchWord();
        var top = opcode >> 12; var mode = (opcode >> 3) & 7; var reg = opcode & 7;
        var dr = (opcode >> 9) & 7; var opmode = (opcode >> 6) & 7;
        var address = top is 9 or 11 or 13 && opmode is 3 or 7;
        var size = address ? (opmode == 3 ? M68kOperandSize.Word : M68kOperandSize.Long) :
            (M68kOperandSize)(1 << ((opcode >> 6) & 3));
        var immediate = top == 0; var quick = top == 5;
        var store = !address && (immediate || quick || opmode >= 4);
        var compare = top == 11 || immediate && (opcode & 0xff00) == 0x0c00;
        var subtract = top == 9 || compare || immediate && (opcode & 0xff00) == 0x0400 || quick && (opcode & 0x100) != 0;
        uint source = immediate ? (size == M68kOperandSize.Long ? FetchLong() : FetchWord()) :
            quick ? (uint)(dr == 0 ? 8 : dr) : store ? State.D[dr] : 0;
        uint operandAddress = 0;
        uint operand;
        if (mode < 2) operand = mode == 0 ? State.D[reg] : State.A[reg];
        else if (mode == 7 && reg == 4) operand = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
        else
        {
            operandAddress = ResolveMoveAddress(mode, reg, size, opcode);
            operand = ReadSized(operandAddress, size);
            if (mode == 3) WriteGeneralRegister(true, reg, unchecked(operandAddress + M68kIntegerSemantics.AddressIncrement(reg, size)));
        }
        if (!store) source = operand;
        var destination = store ? operand : address ? State.A[dr] : State.D[dr];
        if (address && size == M68kOperandSize.Word) source = unchecked((uint)(int)(short)source);
        var arithmeticSize = address || quick && mode == 1 ? M68kOperandSize.Long : size;
        source &= M68kCpuState.Mask(arithmeticSize);
        destination &= M68kCpuState.Mask(arithmeticSize);
        var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
        if (compare) SetCompareFlags(destination, source, arithmeticSize);
        else if (!address && !(quick && mode == 1))
        {
            if (subtract) SetSubtractFlags(destination, source, result, arithmeticSize);
            else SetAddFlags(destination, source, result, arithmeticSize);
        }
        if (!compare)
        {
            if (address || quick && mode == 1) WriteGeneralRegister(true, address ? dr : reg, result);
            else if (!store) WriteDataRegisterSized(dr, result, size);
            else if (mode == 0) WriteDataRegisterSized(reg, result, size);
            else WriteSized(operandAddress, result, size);
        }
        CompleteGeneralArithmeticTiming(mode, reg, store && mode >= 2 && !compare);
        return true;
    }

    private void CompleteGeneralArithmeticTiming(int mode, int reg, bool readModifyWrite)
    {
        var cycles = _profile.FixedInstructionNativeCycles ?? 4 + MoveEaPolicyCycles(mode, reg) + _indexedOperandExtraCycles;
        var barriers = readModifyWrite ? M68kTimingBarrier.ReadModifyWrite : M68kTimingBarrier.None;
        CompleteTimingPlan(_profile.Model == M68kAcceleratorModel.M68030
            ? M68kInstructionPlan.CreateHeadTail(M68kInstructionTimingKey.GeneralArithmetic, "integer arithmetic general EA", cycles, 2, 0, barriers)
            : M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralArithmetic, "integer arithmetic general EA", cycles, barriers));
    }

    private bool TryExecuteGeneralExtendMemory(ushort opcode)
    {
        if ((opcode & 0xf138) is not (0xd108 or 0x9108) || ((opcode >> 6) & 3) == 3) return false;
        BeginInstruction(opcode); _ = FetchWord();
        var size = (M68kOperandSize)(1 << ((opcode >> 6) & 3));
        var sourceReg = opcode & 7; var destinationReg = (opcode >> 9) & 7;
        var sourceAddress = unchecked(State.A[sourceReg] - M68kIntegerSemantics.AddressIncrement(sourceReg, size));
        WriteGeneralRegister(true, sourceReg, sourceAddress);
        var source = ReadSized(sourceAddress, size);
        var destinationAddress = unchecked(State.A[destinationReg] - M68kIntegerSemantics.AddressIncrement(destinationReg, size));
        WriteGeneralRegister(true, destinationReg, destinationAddress);
        var destination = ReadSized(destinationAddress, size);
        var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
        var subtract = (opcode >> 12) == 9;
        var result = subtract ? unchecked(destination - source - extend) : unchecked(destination + source + extend);
        result &= M68kCpuState.Mask(size);
        WriteSized(destinationAddress, result, size);
        if (subtract) SetSubxFlags(destination, source, result, size); else SetAddxFlags(destination, source, result, size);
        CompleteGeneralArithmeticTiming(4, destinationReg, true);
        return true;
    }

    private bool TryExecutePackUnpack(ushort opcode)
    {
        var encoding = opcode & 0xf1f0;
        if (encoding is not (0x8140 or 0x8180)) return false;
        BeginInstruction(opcode); _ = FetchWord(); var adjustment = FetchWord();
        var unpack = encoding == 0x8180; var source = opcode & 7; var destination = (opcode >> 9) & 7;
        var memory = (opcode & 8) != 0;
        uint value;
        if (!memory) value = State.D[source];
        else
        {
            // The unpacked operand is one contiguous word, including at A7.
            // Only the packed byte uses A7's two-byte stride. Preserve the
            // existing low-byte-first transfer ordering and timing policy.
            var sourceStride = unpack && source == 7 ? 2u : 1u;
            var low = ReadPredecrementByte(source, sourceStride);
            value = unpack ? low : (uint)(ReadPredecrementByte(source, sourceStride) << 8 | low);
        }
        var result = unpack ? (ushort)(((value & 0xf0) << 4 | (value & 15)) + adjustment) :
            (ushort)((ushort)(value + adjustment) & 0x0f0f);
        if (!unpack) result = (ushort)((result >> 4 & 0xf0) | (result & 15));
        if (!memory) WriteDataRegisterSized(destination, result, unpack ? M68kOperandSize.Word : M68kOperandSize.Byte);
        else
        {
            var destinationStride = !unpack && destination == 7 ? 2u : 1u;
            WritePredecrementByte(destination, (byte)result, destinationStride);
            if (unpack) WritePredecrementByte(destination, (byte)(result >> 8), destinationStride);
        }
        CompleteGeneralArithmeticTiming(memory ? 4 : 0, destination, memory);
        return true;
    }
    private byte ReadPredecrementByte(int register, uint stride)
    {
        var address = unchecked(State.A[register] - stride);
        WriteGeneralRegister(true, register, address); return ReadByte(address);
    }
    private void WritePredecrementByte(int register, byte value, uint stride)
    {
        var address = unchecked(State.A[register] - stride);
        WriteGeneralRegister(true, register, address); WriteByte(address, value);
    }
}
