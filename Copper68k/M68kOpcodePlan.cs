using System;

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
        Immediate,
        ImmediateBtst,
        RegisterArithmetic
    }

    internal readonly struct M68kOpcodePlan
    {
        public M68kOpcodePlan(
            M68kOpcodePlanKind kind,
            M68kOperandSize size = 0,
            byte register = 0,
            byte sourceMode = 0,
            byte sourceRegister = 0,
            byte destinationMode = 0,
            byte destinationRegister = 0,
            byte condition = 0,
            byte quickValue = 0,
            byte variant = 0,
            short displacement = 0,
            bool extensionDisplacement = false)
        {
            Kind = kind;
            Size = size;
            Register = register;
            SourceMode = sourceMode;
            SourceRegister = sourceRegister;
            DestinationMode = destinationMode;
            DestinationRegister = destinationRegister;
            Condition = condition;
            QuickValue = quickValue;
            Variant = variant;
            Displacement = displacement;
            ExtensionDisplacement = extensionDisplacement;
        }

        public readonly M68kOpcodePlanKind Kind;

        public readonly M68kOperandSize Size;

        public readonly byte Register;

        public readonly byte SourceMode;

        public readonly byte SourceRegister;

        public readonly byte DestinationMode;

        public readonly byte DestinationRegister;

        public readonly byte Condition;

        public readonly byte QuickValue;

        public readonly byte Variant;

        public readonly short Displacement;

        public readonly bool ExtensionDisplacement;
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
        public static readonly M68kOpcodePlan[] Plans = CreatePlans();

        private static M68kOpcodePlan[] CreatePlans()
        {
            var plans = new M68kOpcodePlan[0x1_0000];
            for (var opcode = 0; opcode <= 0xFFFF; opcode++)
            {
                plans[opcode] = CreatePlan((ushort)opcode);
            }

            return plans;
        }

        private static M68kOpcodePlan CreatePlan(ushort opcode)
        {
            if (opcode == 0x4E71)
            {
                return new M68kOpcodePlan(M68kOpcodePlanKind.Nop);
            }

            if ((opcode & 0xF100) == 0x7000)
            {
                return new M68kOpcodePlan(
                    M68kOpcodePlanKind.Moveq,
                    M68kOperandSize.Long,
                    register: (byte)((opcode >> 9) & 7),
                    displacement: unchecked((sbyte)(opcode & 0xFF)));
            }

            if ((opcode & 0xF000) == 0x6000)
            {
                var condition = (opcode >> 8) & 0x0F;
                if (condition == 1)
                {
                    return default;
                }

                var displacement = opcode & 0xFF;
                return new M68kOpcodePlan(
                    M68kOpcodePlanKind.Branch,
                    condition: (byte)condition,
                    displacement: displacement == 0 ? (short)0 : unchecked((sbyte)displacement),
                    extensionDisplacement: displacement == 0);
            }

            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return new M68kOpcodePlan(
                    M68kOpcodePlanKind.Dbcc,
                    register: (byte)(opcode & 7),
                    condition: (byte)((opcode >> 8) & 0x0F),
                    extensionDisplacement: true);
            }

            if (TryCreateQuickRegisterPlan(opcode, out var quickPlan))
            {
                return quickPlan;
            }

            if (TryCreateMovePlan(opcode, out var movePlan))
            {
                return movePlan;
            }

            if (TryCreateImmediateBtstPlan(opcode, out var btstPlan))
            {
                return btstPlan;
            }

            if (TryCreateImmediatePlan(opcode, out var immediatePlan))
            {
                return immediatePlan;
            }

            if (TryCreateRegisterArithmeticPlan(opcode, out var arithmeticPlan))
            {
                return arithmeticPlan;
            }

            return default;
        }

        private static bool TryCreateQuickRegisterPlan(ushort opcode, out M68kOpcodePlan plan)
        {
            plan = default;
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

            var size = sizeCode switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            if (mode == 1 && size == M68kOperandSize.Byte)
            {
                return false;
            }

            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            plan = new M68kOpcodePlan(
                M68kOpcodePlanKind.QuickRegister,
                size,
                destinationMode: (byte)mode,
                destinationRegister: (byte)(opcode & 7),
                quickValue: (byte)count,
                variant: (byte)(((opcode & 0x0100) != 0) ? 1 : 0));
            return true;
        }

        private static bool TryCreateMovePlan(ushort opcode, out M68kOpcodePlan plan)
        {
            plan = default;
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

            plan = new M68kOpcodePlan(
                M68kOpcodePlanKind.Move,
                size,
                sourceMode: (byte)sourceMode,
                sourceRegister: (byte)sourceRegister,
                destinationMode: (byte)destinationMode,
                destinationRegister: (byte)destinationRegister);
            return true;
        }

        private static bool TryCreateImmediateBtstPlan(ushort opcode, out M68kOpcodePlan plan)
        {
            plan = default;
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

            plan = new M68kOpcodePlan(
                M68kOpcodePlanKind.ImmediateBtst,
                mode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte,
                destinationMode: (byte)mode,
                destinationRegister: (byte)register);
            return true;
        }

        private static bool TryCreateImmediatePlan(ushort opcode, out M68kOpcodePlan plan)
        {
            plan = default;
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

            plan = new M68kOpcodePlan(
                M68kOpcodePlanKind.Immediate,
                size,
                destinationMode: (byte)mode,
                destinationRegister: (byte)register,
                variant: high switch
                {
                    0x0000 => 0,
                    0x0200 => 1,
                    0x0400 => 2,
                    0x0600 => 3,
                    0x0A00 => 4,
                    _ => 5
                });
            return true;
        }

        private static bool TryCreateRegisterArithmeticPlan(ushort opcode, out M68kOpcodePlan plan)
        {
            plan = default;
            var line = opcode >> 12;
            if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD))
            {
                return false;
            }

            var reg = (opcode >> 9) & 7;
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            var eaReg = opcode & 7;
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

            plan = new M68kOpcodePlan(
                M68kOpcodePlanKind.RegisterArithmetic,
                size,
                register: (byte)reg,
                sourceRegister: (byte)eaReg,
                variant: (byte)(((line & 0x0F) << 4) | opmode));
            return true;
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
