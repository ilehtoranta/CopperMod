/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace Copper68k;

// Private format-8 image, not a Motorola silicon encoding. The original
// operand address remains fixed when a handler redirects one pending word.
internal static class M68010LongMoveResumeFrame
{
    internal const ushort Marker = 0xc110;
    internal static bool IsOpcodeSupported(ushort opcode, bool write)
        => (opcode & 0xf000) == 0x2000 &&
           M68010WordMoveResumeFrame.IsOpcodeSupported((ushort)((opcode & 0x0fff) | 0x3000), write);

    internal static bool IsValid(ushort[] words)
    {
        if (words.Length != 16 || words[0] != 0 || words[1] != Marker || words[3] > 1 ||
            !IsOpcodeSupported(words[2], words[3] != 0) || words[10] > 2 || words[11] > 5 ||
            (words[3] == 0) != (words[11] < 2) ||
            (M68010WordMoveResumeFrame.Long(words, 4) & 1) != 0 ||
            (M68010WordMoveResumeFrame.Long(words, 6) & 1) != 0 ||
            (words[10] != 0 && M68010WordMoveResumeFrame.Long(words, 6) != M68010WordMoveResumeFrame.Long(words, 4)) ||
            (M68010WordMoveResumeFrame.Long(words, 14) & 1) == 0) return false;
        if (words[11] == 0 && (words[12] != 0 || words[13] != 0)) return false;
        if (words[11] == 1 && words[13] != 0) return false;
        var predecrement = ((words[2] >> 6) & 7) == 4;
        return words[11] < 2 || predecrement == (words[11] >= 4);
    }
}
