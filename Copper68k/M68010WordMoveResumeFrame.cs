/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace Copper68k;

// Private format-8 internal image. This is not a Motorola silicon encoding.
// All continuation state is in the frame; copied/nested frames need no side table.
internal static class M68010WordMoveResumeFrame
{
    internal const ushort Marker = 0xc010;
    internal static bool IsOpcodeSupported(ushort opcode, bool write)
    {
        if ((opcode & 0xf000) != 0x3000) return false;
        var sourceMode = (opcode >> 3) & 7; var sourceReg = opcode & 7;
        var destMode = (opcode >> 6) & 7; var destReg = (opcode >> 9) & 7;
        var legalSource = sourceMode < 7 || sourceReg <= 4;
        var legalDestination = destMode < 7 || destReg <= 1;
        return legalSource && legalDestination && (write ? destMode >= 2 : sourceMode >= 2 && !(sourceMode == 7 && sourceReg == 4));
    }

    internal static bool IsValid(ushort[] words)
    {
        if (words.Length != 16 || words[0] != 0 || words[1] != Marker || words[3] > 1 ||
            !IsOpcodeSupported(words[2], words[3] == 1) || words[10] > 2 ||
            (Long(words, 4) & 1) != 0 || (Long(words, 6) & 1) != 0 ||
            (words[10] != 0 && Long(words, 6) != Long(words, 4))) return false;
        for (var i = 11; i < 16; i++) if (words[i] != 0) return false;
        return true;
    }

    internal static uint Long(ushort[] words, int index) => ((uint)words[index] << 16) | words[index + 1];
    internal static void Long(ushort[] words, int index, uint value)
    { words[index] = (ushort)(value >> 16); words[index + 1] = (ushort)value; }
}
