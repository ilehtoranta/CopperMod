/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
using System;

namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private bool TryExecuteGeneralMovem(ushort opcode)
    {
        if ((opcode & 0xfb80) != 0x4880) return false;
        var mode = (opcode >> 3) & 7;
        var baseRegister = opcode & 7;
        if (mode < 2) return false; // EXT shares the upper opcode bits.
        var load = (opcode & 0x400) != 0;
        var legal = mode == 2 || mode == 5 || mode == 6 ||
            mode == (load ? 3 : 4) || (mode == 7 && baseRegister <= (load ? 3 : 1));
        BeginInstruction(opcode);
        _ = FetchWord();
        if (!legal)
        {
            RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
            return true;
        }
        var mask = FetchWord();
        var size = (opcode & 0x40) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
        Span<uint> registers = stackalloc uint[16];
        State.D.CopyTo(registers);
        State.A.CopyTo(registers[8..]);
        var address = mode is 3 or 4 ? State.A[baseRegister] : ResolveMoveAddress(mode, baseRegister, size, opcode);
        for (var bit = 0; bit < 16; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            var register = mode == 4 ? 15 - bit : bit;
            if (mode == 4) address = unchecked(address - (uint)size);
            if (load)
            {
                var value = ReadSized(address, size);
                if (size == M68kOperandSize.Word) value = unchecked((uint)(int)(short)value);
                WriteGeneralRegister(register >= 8, register & 7, value);
            }
            else
            {
                var value = registers[register];
                // M68000PM MOVEM: 020+ stores the initial base minus ONE
                // operand size when the predecrement base is in the list.
                if (mode == 4 && register == baseRegister + 8) value = unchecked(value - (uint)size);
                WriteSized(address, value, size);
            }
            if (mode != 4) address = unchecked(address + (uint)size);
        }
        if (mode is 3 or 4) WriteGeneralRegister(true, baseRegister, address);
        var cycles = _profile.FixedInstructionNativeCycles ??
            8 + (load ? 4 : 0) + 2 * (int)((State.ProgramCounter - State.LastInstructionProgramCounter - 4) / 2) +
            CountSetBits(mask) * (size == M68kOperandSize.Word ? 2 : 4) + _indexedOperandExtraCycles;
        CompleteTimingPlan(_profile.Model == M68kAcceleratorModel.M68030
            ? M68kInstructionPlan.CreateHeadTail(M68kInstructionTimingKey.GeneralMovem, "MOVEM general EA", cycles, 2, 0)
            : M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralMovem, "MOVEM general EA", cycles));
        return true;
    }
}
