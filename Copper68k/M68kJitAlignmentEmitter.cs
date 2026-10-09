/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Reflection.Emit;

namespace Copper68k
{
    // Guard at the instruction boundary, before fetch consumption, register updates,
    // flags or bus accesses. The interpreter then executes the instruction once and
    // owns its precise address-error frame and any operand effects before the fault.
    internal static class M68kJitAlignmentEmitter
    {
        public static void Emit(
            ILGenerator il,
            M68kDecodedInstruction instruction,
            Action<int> loadDataRegister,
            Action<int> loadAddressRegister,
            Label exit)
        {
            switch (instruction.Operation)
            {
                case M68kJitOperation.Lea:
                case M68kJitOperation.Movep: // Byte bus transfers permit odd addresses.
                    return;
                case M68kJitOperation.Pea:
                case M68kJitOperation.Bsr:
                case M68kJitOperation.Rts:
                    loadAddressRegister(7);
                    EmitOddExit(il, exit);
                    return;
                case M68kJitOperation.Jsr:
                    loadAddressRegister(7);
                    EmitOddExit(il, exit);
                    goto case M68kJitOperation.Jmp;
                case M68kJitOperation.Jmp:
                    EmitEa(il, instruction.Source, loadDataRegister, loadAddressRegister, exit);
                    return;
            }

            if (instruction.Size == M68kOperandSize.Byte)
            {
                return;
            }

            EmitEa(il, instruction.Source, loadDataRegister, loadAddressRegister, exit);
            EmitEa(il, instruction.Destination, loadDataRegister, loadAddressRegister, exit);
        }

        private static void EmitEa(
            ILGenerator il,
            M68kDecodedEa ea,
            Action<int> loadDataRegister,
            Action<int> loadAddressRegister,
            Label exit)
        {
            if (!ea.IsMemory)
            {
                return;
            }

            switch (ea.Kind)
            {
                case M68kJitEaKind.AddressIndirect:
                case M68kJitEaKind.AddressPostincrement:
                case M68kJitEaKind.AddressPredecrement:
                    // Word/long increments and decrements are even, including aliases.
                    loadAddressRegister(ea.Register);
                    break;
                case M68kJitEaKind.AddressDisplacement:
                    loadAddressRegister(ea.Register);
                    il.Emit(OpCodes.Ldc_I4, ea.Extension0 & 1);
                    il.Emit(OpCodes.Xor);
                    break;
                case M68kJitEaKind.AddressIndex:
                case M68kJitEaKind.PcIndex:
                    if (ea.Kind == M68kJitEaKind.AddressIndex)
                    {
                        loadAddressRegister(ea.Register);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4, (int)(ea.ExtensionAddress & 1));
                    }
                    var indexRegister = (ea.Extension0 >> 12) & 7;
                    if ((ea.Extension0 & 0x8000) != 0)
                    {
                        loadAddressRegister(indexRegister);
                    }
                    else
                    {
                        loadDataRegister(indexRegister);
                    }
                    // 68000 brief indexes are unscaled; sign extension preserves parity.
                    il.Emit(OpCodes.Xor);
                    il.Emit(OpCodes.Ldc_I4, ea.Extension0 & 1);
                    il.Emit(OpCodes.Xor);
                    break;
                case M68kJitEaKind.AbsoluteWord:
                    il.Emit(OpCodes.Ldc_I4, ea.Extension0 & 1);
                    break;
                case M68kJitEaKind.AbsoluteLong:
                    il.Emit(OpCodes.Ldc_I4, ea.Extension1 & 1);
                    break;
                case M68kJitEaKind.PcDisplacement:
                    il.Emit(OpCodes.Ldc_I4, (int)((ea.ExtensionAddress ^ ea.Extension0) & 1));
                    break;
                default:
                    throw new InvalidOperationException($"Unqualified 68000 alignment form: {ea.Kind}.");
            }

            EmitOddExit(il, exit);
        }

        private static void EmitOddExit(ILGenerator il, Label exit)
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, exit);
        }
    }
}
