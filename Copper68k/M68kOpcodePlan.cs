namespace Copper68k
{
    internal enum M68kOpcodePlanKind : byte
    {
        Unsupported = 0,
        Nop,
        Moveq,
        Branch,
        Dbcc,
        QuickRegister,
        Move,
        MoveLongPostincrementToData,
        MoveLongDataToPostincrement,
        Immediate,
        ImmediateBtst,
        RegisterArithmetic,
        DataRegisterLongOrToRegister,
        DataRegisterLongEorToDestination,
        DataRegisterLongAndToRegister,
        DataRegisterLongAddToRegister
    }

    internal readonly record struct M68kPlannedInterpreterCounters(
        long FastInstructions,
        long ScalarFallbackInstructions,
        long NopInstructions,
        long MoveqInstructions,
        long BranchInstructions,
        long DbccInstructions,
        long QuickRegisterInstructions,
        long MoveInstructions,
        long ImmediateInstructions,
        long ImmediateBtstInstructions,
        long RegisterArithmeticInstructions)
    {
        public static M68kPlannedInterpreterCounters Empty { get; } = new();
    }

    internal static class M68kOpcodePlanTable
    {
        public static readonly M68kOpcodePlanKind[] Kinds = CreateKinds();

        private static M68kOpcodePlanKind[] CreateKinds()
        {
            var kinds = new M68kOpcodePlanKind[0x1_0000];
            for (var opcode = 0; opcode <= 0xFFFF; opcode++)
            {
                kinds[opcode] = CreateKind((ushort)opcode);
            }

            return kinds;
        }

        private static M68kOpcodePlanKind CreateKind(ushort opcode)
        {
            if (opcode == 0x4E71)
            {
                return M68kOpcodePlanKind.Nop;
            }

            if ((opcode & 0xF100) == 0x7000)
            {
                return M68kOpcodePlanKind.Moveq;
            }

            if ((opcode & 0xF000) == 0x6000)
            {
                var condition = (opcode >> 8) & 0x0F;
                return condition == 1
                    ? M68kOpcodePlanKind.Unsupported
                    : M68kOpcodePlanKind.Branch;
            }

            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return M68kOpcodePlanKind.Dbcc;
            }

            if (TryCreateQuickRegisterKind(opcode, out var quickKind))
            {
                return quickKind;
            }

            if (TryCreateMoveKind(opcode, out var moveKind))
            {
                return moveKind;
            }

            if (TryCreateImmediateBtstKind(opcode, out var btstKind))
            {
                return btstKind;
            }

            if (TryCreateImmediateKind(opcode, out var immediateKind))
            {
                return immediateKind;
            }

            if (TryCreateRegisterArithmeticKind(opcode, out var arithmeticKind))
            {
                return arithmeticKind;
            }

            return M68kOpcodePlanKind.Unsupported;
        }

        private static bool TryCreateQuickRegisterKind(ushort opcode, out M68kOpcodePlanKind kind)
        {
            kind = M68kOpcodePlanKind.Unsupported;
            if ((opcode & 0xF000) != 0x5000 ||
                (opcode & 0xF0C0) == 0x50C0)
            {
                return false;
            }

            var sizeCode = (opcode >> 6) & 3;
            var mode = (opcode >> 3) & 7;
            if (sizeCode == 3 || mode is not (0 or 1))
            {
                return false;
            }

            if (mode == 1 && sizeCode == 0)
            {
                return false;
            }

            kind = M68kOpcodePlanKind.QuickRegister;
            return true;
        }

        private static bool TryCreateMoveKind(ushort opcode, out M68kOpcodePlanKind kind)
        {
            kind = M68kOpcodePlanKind.Unsupported;
            var line = (opcode >> 12) & 0x0F;
            var size = line switch
            {
                1 => M68kOperandSize.Byte,
                2 => M68kOperandSize.Long,
                3 => M68kOperandSize.Word,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            var sourceMode = (opcode >> 3) & 7;
            var sourceRegister = opcode & 7;
            var destinationMode = (opcode >> 6) & 7;
            var destinationRegister = (opcode >> 9) & 7;
            if (!IsSupportedMoveSource(sourceMode, sourceRegister, size) ||
                !IsSupportedMoveDestination(destinationMode, destinationRegister, size))
            {
                return false;
            }

            if (size == M68kOperandSize.Long &&
                sourceMode == 3 &&
                destinationMode == 0)
            {
                kind = M68kOpcodePlanKind.MoveLongPostincrementToData;
                return true;
            }

            if (size == M68kOperandSize.Long &&
                sourceMode == 0 &&
                destinationMode == 3)
            {
                kind = M68kOpcodePlanKind.MoveLongDataToPostincrement;
                return true;
            }

            kind = M68kOpcodePlanKind.Move;
            return true;
        }

        private static bool TryCreateImmediateBtstKind(ushort opcode, out M68kOpcodePlanKind kind)
        {
            kind = M68kOpcodePlanKind.Unsupported;
            if ((opcode & 0xFFC0) != 0x0800)
            {
                return false;
            }

            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (!IsValidImmediateBtstEa(mode, register) ||
                !IsSupportedReadableEa(mode, register, mode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte))
            {
                return false;
            }

            kind = M68kOpcodePlanKind.ImmediateBtst;
            return true;
        }

        private static bool TryCreateImmediateKind(ushort opcode, out M68kOpcodePlanKind kind)
        {
            kind = M68kOpcodePlanKind.Unsupported;
            var high = opcode & 0xFF00;
            if (high != 0x0000 &&
                high != 0x0200 &&
                high != 0x0400 &&
                high != 0x0600 &&
                high != 0x0A00 &&
                high != 0x0C00)
            {
                return false;
            }

            var sizeCode = (opcode >> 6) & 3;
            if (sizeCode == 3)
            {
                return false;
            }

            var size = sizeCode switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var compareOnly = high == 0x0C00;
            if (mode == 1 ||
                !IsSupportedReadableEa(mode, register, size) ||
                (!compareOnly && !IsSupportedWritableEa(mode, register, size)))
            {
                return false;
            }

            kind = M68kOpcodePlanKind.Immediate;
            return true;
        }

        private static bool TryCreateRegisterArithmeticKind(ushort opcode, out M68kOpcodePlanKind kind)
        {
            kind = M68kOpcodePlanKind.Unsupported;
            var line = opcode >> 12;
            if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD))
            {
                return false;
            }

            var reg = (opcode >> 9) & 7;
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            var eaReg = opcode & 7;
            _ = reg;
            _ = eaReg;
            if (mode != 0 ||
                (line == 0xC && (opmode == 3 || opmode == 7)) ||
                (line == 0x8 && (opmode == 3 || opmode == 7)) ||
                ((line == 0x9 || line == 0xD) && opmode is 4 or 5 or 6))
            {
                return false;
            }

            if ((line == 0x8 || line == 0xC) &&
                (opcode & 0xF1F0) is 0x8100 or 0xC100)
            {
                return false;
            }

            if (line == 0xC &&
                ((opcode & 0xF1F8) is 0xC140 or 0xC148 or 0xC188))
            {
                return false;
            }

            if (line == 0xB && (opcode & 0xF138) == 0xB108)
            {
                return false;
            }

            var size = opmode switch
            {
                0 or 4 => M68kOperandSize.Byte,
                1 or 5 => M68kOperandSize.Word,
                2 or 6 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            if (size == M68kOperandSize.Long &&
                TryCreateDataRegisterLongArithmeticKind(line, opmode, out kind))
            {
                return true;
            }

            kind = M68kOpcodePlanKind.RegisterArithmetic;
            return true;
        }

        private static bool TryCreateDataRegisterLongArithmeticKind(
            int line,
            int opmode,
            out M68kOpcodePlanKind kind)
        {
            if (line == 0x8 && opmode == 2)
            {
                kind = M68kOpcodePlanKind.DataRegisterLongOrToRegister;
                return true;
            }

            if (line == 0xB && opmode == 6)
            {
                kind = M68kOpcodePlanKind.DataRegisterLongEorToDestination;
                return true;
            }

            if (line == 0xC && opmode == 2)
            {
                kind = M68kOpcodePlanKind.DataRegisterLongAndToRegister;
                return true;
            }

            if (line == 0xD && opmode == 2)
            {
                kind = M68kOpcodePlanKind.DataRegisterLongAddToRegister;
                return true;
            }

            kind = M68kOpcodePlanKind.Unsupported;
            return false;
        }

        private static bool IsSupportedMoveSource(int mode, int register, M68kOperandSize size)
        {
            if (mode == 1 && size == M68kOperandSize.Byte)
            {
                return false;
            }

            return IsSupportedReadableEa(mode, register, size);
        }

        private static bool IsSupportedMoveDestination(int mode, int register, M68kOperandSize size)
        {
            if (mode == 1 && size == M68kOperandSize.Byte)
            {
                return false;
            }

            return IsSupportedWritableEa(mode, register, size);
        }

        private static bool IsSupportedReadableEa(int mode, int register, M68kOperandSize size)
        {
            _ = size;
            return mode switch
            {
                0 or 1 or 2 or 3 or 4 or 5 or 6 => true,
                7 => register <= 4,
                _ => false
            };
        }

        private static bool IsSupportedWritableEa(int mode, int register, M68kOperandSize size)
        {
            _ = size;
            return mode switch
            {
                0 or 1 or 2 or 3 or 4 or 5 or 6 => true,
                7 => register <= 1,
                _ => false
            };
        }

        private static bool IsValidImmediateBtstEa(int mode, int register)
        {
            return mode switch
            {
                0 => true,
                2 or 3 or 4 or 5 or 6 => true,
                7 => register <= 3,
                _ => false
            };
        }
    }
}
