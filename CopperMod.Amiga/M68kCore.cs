using System;

namespace CopperMod.Amiga
{
    internal enum M68kOperandSize
    {
        Byte = 1,
        Word = 2,
        Long = 4
    }

    internal interface IM68kBus
    {
        byte ReadByte(uint address, ref long cycle, AmigaBusAccessKind accessKind);

        ushort ReadWord(uint address, ref long cycle, AmigaBusAccessKind accessKind);

        uint ReadLong(uint address, ref long cycle, AmigaBusAccessKind accessKind);

        void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind);

        void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind);

        void WriteLong(uint address, uint value, ref long cycle, AmigaBusAccessKind accessKind);

        bool TryInvokeHost(uint address, M68kCpuState state);
    }

    internal interface IM68kCore
    {
        M68kCpuState State { get; }

        int ExecuteInstruction();

        void Reset(uint programCounter, uint stackPointer);

        void BeginSubroutine(uint address, uint stackPointer, uint returnAddress);

        void RequestInterrupt(int level, uint vectorAddress);
    }

    internal enum M68kBackendKind
    {
        AccurateM68000,
        FastM68000,
        JitM68000,
        Cpu32
    }

    internal interface IM68kCoreFactory
    {
        IM68kCore Create(M68kBackendKind backend, IM68kBus bus);
    }

    internal sealed class M68kCoreFactory : IM68kCoreFactory
    {
        public static M68kCoreFactory Default { get; } = new M68kCoreFactory();

        public IM68kCore Create(M68kBackendKind backend, IM68kBus bus)
        {
            if (backend == M68kBackendKind.AccurateM68000)
            {
                return new M68kInterpreter(bus);
            }

            throw new AmigaEmulationException($"The requested MC68000 backend is not implemented: {backend}.");
        }
    }

    internal sealed class M68kCpuState
    {
        public const ushort Carry = 0x0001;
        public const ushort Overflow = 0x0002;
        public const ushort Zero = 0x0004;
        public const ushort Negative = 0x0008;
        public const ushort Extend = 0x0010;
        public const ushort Supervisor = 0x2000;

        public uint[] D { get; } = new uint[8];

        public uint[] A { get; } = new uint[8];

        public uint ProgramCounter { get; set; }

        public ushort StatusRegister { get; set; } = Supervisor;

        public long Cycles { get; set; }

        public bool Halted { get; set; }

        public ushort LastOpcode { get; set; }

        public uint LastInstructionProgramCounter { get; set; }

        public bool GetFlag(ushort flag)
        {
            return (StatusRegister & flag) != 0;
        }

        public void SetFlag(ushort flag, bool value)
        {
            StatusRegister = value
                ? (ushort)(StatusRegister | flag)
                : (ushort)(StatusRegister & ~flag);
        }

        public void SetNegativeZero(uint value, M68kOperandSize size)
        {
            var mask = Mask(size);
            var sign = SignBit(size);
            value &= mask;
            SetFlag(Zero, value == 0);
            SetFlag(Negative, (value & sign) != 0);
        }

        public static uint Mask(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => 0xFF,
                M68kOperandSize.Word => 0xFFFF,
                _ => 0xFFFF_FFFF
            };
        }

        public static uint SignBit(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => 0x80,
                M68kOperandSize.Word => 0x8000,
                _ => 0x8000_0000
            };
        }

        public static uint SignExtend(uint value, M68kOperandSize size)
        {
            value &= Mask(size);
            return size switch
            {
                M68kOperandSize.Byte => (value & 0x80) != 0 ? value | 0xFFFF_FF00 : value,
                M68kOperandSize.Word => (value & 0x8000) != 0 ? value | 0xFFFF_0000 : value,
                _ => value
            };
        }
    }

    internal sealed class UnsupportedM68kOpcodeException : AmigaEmulationException
    {
        public UnsupportedM68kOpcodeException(ushort opcode, uint programCounter)
            : base($"Unsupported MC68000 opcode 0x{opcode:X4} at 0x{programCounter:X8}.")
        {
            Opcode = opcode;
            ProgramCounter = programCounter;
        }

        public ushort Opcode { get; }

        public uint ProgramCounter { get; }
    }

    internal sealed class M68kInterpreter : IM68kCore
    {
        private const uint SubroutineSentinel = 0xFFFF_FFFC;
        private readonly IM68kBus _bus;

        public M68kInterpreter(IM68kBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public M68kCpuState State { get; } = new M68kCpuState();

        public int ExecuteInstruction()
        {
            if (State.Halted)
            {
                State.Cycles++;
                return 1;
            }

            var startCycles = State.Cycles;
            var directHostAddress = State.ProgramCounter;
            if (_bus.TryInvokeHost(directHostAddress, State))
            {
                AddCycles(16);
                if (State.ProgramCounter == directHostAddress)
                {
                    State.ProgramCounter = PullLong();
                }

                return (int)(State.Cycles - startCycles);
            }

            var instructionPc = State.ProgramCounter;
            var opcode = FetchWord();
            State.LastOpcode = opcode;
            State.LastInstructionProgramCounter = instructionPc;

            var opcodeLine = opcode & 0xF000;
            if ((opcodeLine == 0x1000 || opcodeLine == 0x2000 || opcodeLine == 0x3000) && DecodeMove(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            if (DecodeLine0(opcode) ||
                DecodeLine4(opcode, instructionPc) ||
                DecodeLine5(opcode) ||
                DecodeBranch(opcode, instructionPc) ||
                DecodeMoveq(opcode) ||
                DecodeArithmetic(opcode) ||
                DecodeShiftRotate(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            throw new UnsupportedM68kOpcodeException(opcode, instructionPc);
        }

        public void Reset(uint programCounter, uint stackPointer)
        {
            Array.Clear(State.D);
            Array.Clear(State.A);
            State.ProgramCounter = programCounter;
            State.A[7] = stackPointer;
            State.StatusRegister = M68kCpuState.Supervisor;
            State.Cycles = 0;
            State.Halted = false;
            State.LastOpcode = 0;
        }

        public void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            State.A[7] = stackPointer;
            PushLong(returnAddress);
            State.ProgramCounter = address;
            State.Halted = false;
        }

        public void RequestInterrupt(int level, uint vectorAddress)
        {
            if (level <= 0)
            {
                return;
            }

            var mask = (State.StatusRegister >> 8) & 0x07;
            if (level <= mask)
            {
                return;
            }

            PushLong(State.ProgramCounter);
            PushWord(State.StatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister & 0xF8FF) | ((level & 7) << 8) | M68kCpuState.Supervisor);
            State.ProgramCounter = ReadLong(vectorAddress);
            AddCycles(44);
        }

        private bool DecodeMove(ushort opcode)
        {
            var size = ((opcode >> 12) & 0x03) switch
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

            var src = ResolveEa((opcode >> 3) & 7, opcode & 7, size);
            var value = src.Read();
            var destMode = (opcode >> 6) & 7;
            var destReg = (opcode >> 9) & 7;
            var dest = ResolveEa(destMode, destReg, size, write: true);
            dest.Write(value);
            if (destMode == 1)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }
            else
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddCycles(EstimateEaCycles(src, dest, size, write: true));
            return true;
        }

        private bool DecodeMoveq(ushort opcode)
        {
            if ((opcode & 0xF100) != 0x7000)
            {
                return false;
            }

            var reg = (opcode >> 9) & 7;
            State.D[reg] = (uint)unchecked((int)(sbyte)(opcode & 0xFF));
            State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(4);
            return true;
        }

        private bool DecodeBranch(ushort opcode, uint instructionPc)
        {
            if ((opcode & 0xF000) != 0x6000)
            {
                return false;
            }

            var condition = (opcode >> 8) & 0x0F;
            var displacement = opcode & 0xFF;
            var branchBase = State.ProgramCounter;
            int offset;
            if (displacement == 0)
            {
                offset = unchecked((short)FetchWord());
            }
            else
            {
                offset = unchecked((sbyte)displacement);
            }

            if (condition == 1)
            {
                PushLong(State.ProgramCounter);
                State.ProgramCounter = (uint)(branchBase + offset);
                AddCycles(displacement == 0 ? 18 : 18);
                return true;
            }

            if (CheckCondition(condition))
            {
                State.ProgramCounter = (uint)(branchBase + offset);
                AddCycles(displacement == 0 ? 10 : 10);
            }
            else
            {
                _ = instructionPc;
                AddCycles(8);
            }

            return true;
        }

        private bool DecodeLine0(ushort opcode)
        {
            if ((opcode & 0xFF00) is 0x0800 or 0x0840 or 0x0880 or 0x08C0)
            {
                var bit = FetchWord() & 31;
                var operation = (opcode >> 6) & 3;
                var bitMode = (opcode >> 3) & 7;
                var bitReg = opcode & 7;
                if (bitMode == 1 || (bitMode == 7 && bitReg == 4))
                {
                    return false;
                }

                var bitSize = bitMode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                var bitEa = ResolveEa(bitMode, bitReg, bitSize, write: operation != 0);
                var value = bitEa.Read();
                var mask = 1u << (int)(bitMode == 0 ? bit : bit & 7);
                State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
                if (operation != 0)
                {
                    value = operation switch
                    {
                        1 => value ^ mask,
                        2 => value & ~mask,
                        _ => value | mask
                    };
                    bitEa.Write(value);
                }

                AddCycles(bitMode == 0 ? 10 : 14);
                return true;
            }

            if ((opcode & 0xF100) == 0x0100)
            {
                var bitRegister = (opcode >> 9) & 7;
                var operation = (opcode >> 6) & 3;
                var bitMode = (opcode >> 3) & 7;
                var bitReg = opcode & 7;
                if (bitMode == 1 || (bitMode == 7 && bitReg == 4))
                {
                    return false;
                }

                var bitSize = bitMode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                var bitEa = ResolveEa(bitMode, bitReg, bitSize, write: operation != 0);
                var value = bitEa.Read();
                var bit = State.D[bitRegister] & (bitMode == 0 ? 31u : 7u);
                var mask = 1u << (int)bit;
                State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
                if (operation != 0)
                {
                    value = operation switch
                    {
                        1 => value ^ mask,
                        2 => value & ~mask,
                        _ => value | mask
                    };
                    bitEa.Write(value);
                }

                AddCycles(bitMode == 0 ? 8 : 14);
                return true;
            }

            var high = opcode & 0xFF00;
            if (high != 0x0000 && high != 0x0200 && high != 0x0400 && high != 0x0600 && high != 0x0A00 && high != 0x0C00)
            {
                return false;
            }

            var size = DecodeImmediateSize(opcode);
            var immediate = FetchImmediate(size);
            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;
            var ea = ResolveEa(mode, reg, size, write: high != 0x0C00);
            var destination = ea.Read();
            switch (high)
            {
                case 0x0000:
                    destination |= immediate;
                    ea.Write(destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0200:
                    destination &= immediate;
                    ea.Write(destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0400:
                    destination = Subtract(destination, immediate, size, setExtend: true);
                    ea.Write(destination);
                    break;
                case 0x0600:
                    destination = Add(destination, immediate, size, setExtend: true);
                    ea.Write(destination);
                    break;
                case 0x0A00:
                    destination ^= immediate;
                    ea.Write(destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0C00:
                    _ = Subtract(destination, immediate, size, setExtend: false, storeResult: false);
                    break;
            }

            AddCycles(size == M68kOperandSize.Long ? 16 : 8);
            return true;
        }

        private bool DecodeLine4(ushort opcode, uint instructionPc)
        {
            switch (opcode)
            {
                case 0x4E71:
                    AddCycles(4);
                    return true;
                case 0x4E72:
                    State.StatusRegister = FetchWord();
                    AddCycles(20);
                    return true;
                case 0x4E73:
                    State.StatusRegister = PullWord();
                    State.ProgramCounter = PullLong();
                    AddCycles(20);
                    return true;
                case 0x4E75:
                    State.ProgramCounter = PullLong();
                    AddCycles(16);
                    return true;
                case 0x4E76:
                    State.Halted = true;
                    AddCycles(4);
                    return true;
                case 0x4E77:
                    State.StatusRegister = PullWord();
                    AddCycles(12);
                    return true;
            }

            if ((opcode & 0xFFF8) == 0x4E50)
            {
                var reg = opcode & 7;
                PushLong(State.A[reg]);
                var displacement = unchecked((short)FetchWord());
                State.A[reg] = State.A[7];
                State.A[7] = (uint)(State.A[7] + displacement);
                AddCycles(16);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E58)
            {
                var reg = opcode & 7;
                State.A[7] = State.A[reg];
                State.A[reg] = PullLong();
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFB80) == 0x4880 && ((opcode >> 3) & 7) != 0)
            {
                DecodeMovem(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4840)
            {
                var reg = opcode & 7;
                var value = State.D[reg];
                State.D[reg] = (value << 16) | ((value >> 16) & 0xFFFF);
                State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4840)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                PushLong(ea.Address);
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4880)
            {
                var reg = opcode & 7;
                var value = M68kCpuState.SignExtend(State.D[reg] & 0xFF, M68kOperandSize.Byte) & 0xFFFF;
                State.D[reg] = (State.D[reg] & 0xFFFF_0000) | value;
                State.SetNegativeZero(value, M68kOperandSize.Word);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x48C0)
            {
                var reg = opcode & 7;
                var value = M68kCpuState.SignExtend(State.D[reg] & 0xFFFF, M68kOperandSize.Word);
                State.D[reg] = value;
                State.SetNegativeZero(value, M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(4);
                return true;
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                var addressRegister = (opcode >> 9) & 7;
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                State.A[addressRegister] = ea.Address;
                AddCycles(8);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E90)
            {
                var target = State.A[opcode & 7];
                if (_bus.TryInvokeHost(target, State))
                {
                    AddCycles(16);
                    return true;
                }

                PushLong(State.ProgramCounter);
                State.ProgramCounter = target;
                AddCycles(16);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4E80)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                PushLong(State.ProgramCounter);
                State.ProgramCounter = ea.Address;
                AddCycles(18);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4EC0)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                State.ProgramCounter = ea.Address;
                AddCycles(12);
                return true;
            }

            var unary = opcode & 0xFF00;
            if (unary is 0x4200 or 0x4400 or 0x4600 or 0x4A00)
            {
                var size = DecodeImmediateSize(opcode);
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, size, write: unary != 0x4A00);
                var value = ea.Read();
                switch (unary)
                {
                    case 0x4200:
                        value = 0;
                        ea.Write(value);
                        State.SetNegativeZero(0, size);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                        break;
                    case 0x4400:
                        value = Subtract(0, value, size, setExtend: true);
                        ea.Write(value);
                        break;
                    case 0x4600:
                        value = (~value) & M68kCpuState.Mask(size);
                        ea.Write(value);
                        SetLogicFlags(value, size);
                        break;
                    case 0x4A00:
                        State.SetNegativeZero(value, size);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                        break;
                }

                AddCycles(size == M68kOperandSize.Long ? 12 : 8);
                return true;
            }

            _ = instructionPc;
            return false;
        }

        private bool DecodeLine5(ushort opcode)
        {
            if ((opcode & 0xF0F8) == 0x50C8)
            {
                var condition = (opcode >> 8) & 0x0F;
                var reg = opcode & 7;
                var branchBase = State.ProgramCounter;
                var displacement = unchecked((short)FetchWord());
                if (!CheckCondition(condition))
                {
                    var counter = (ushort)((State.D[reg] & 0xFFFF) - 1);
                    State.D[reg] = (State.D[reg] & 0xFFFF_0000) | counter;
                    if (counter != 0xFFFF)
                    {
                        State.ProgramCounter = (uint)(branchBase + displacement);
                        AddCycles(10);
                    }
                    else
                    {
                        AddCycles(14);
                    }
                }
                else
                {
                    AddCycles(12);
                }

                return true;
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                var condition = (opcode >> 8) & 0x0F;
                var conditionEa = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Byte, write: true);
                conditionEa.Write(CheckCondition(condition) ? 0xFFu : 0u);
                AddCycles(8);
                return true;
            }

            if ((opcode & 0xF000) != 0x5000)
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
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var subtract = (opcode & 0x0100) != 0;
            var mode = (opcode >> 3) & 7;
            var ea = ResolveEa(mode, opcode & 7, mode == 1 && size == M68kOperandSize.Byte ? M68kOperandSize.Word : size, write: true);
            var old = ea.Read();
            var result = subtract
                ? Subtract(old, (uint)count, size, setExtend: mode != 1)
                : Add(old, (uint)count, size, setExtend: mode != 1);
            ea.Write(result);
            AddCycles(size == M68kOperandSize.Long ? 8 : 4);
            return true;
        }

        private bool DecodeArithmetic(ushort opcode)
        {
            var line = opcode >> 12;
            if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD))
            {
                return false;
            }

            var reg = (opcode >> 9) & 7;
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            var eaReg = opcode & 7;

            if (line == 0xC && opmode == 3)
            {
                var source = ResolveEa(mode, eaReg, M68kOperandSize.Word).Read();
                State.D[reg] = (uint)((ushort)State.D[reg] * (ushort)source);
                State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(70);
                return true;
            }

            if (line == 0xC && opmode == 7)
            {
                var source = unchecked((short)ResolveEa(mode, eaReg, M68kOperandSize.Word).Read());
                State.D[reg] = (uint)(unchecked((short)State.D[reg]) * source);
                State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(70);
                return true;
            }

            if (line == 0x8 && (opmode == 3 || opmode == 7))
            {
                var divisor = ResolveEa(mode, eaReg, M68kOperandSize.Word).Read() & 0xFFFF;
                if (divisor == 0)
                {
                    State.Halted = true;
                    AddCycles(38);
                    return true;
                }

                var dividend = State.D[reg];
                uint quotient;
                uint remainder;
                if (opmode == 3)
                {
                    quotient = dividend / divisor;
                    remainder = dividend % divisor;
                }
                else
                {
                    var signedDivisor = unchecked((short)divisor);
                    var signedDividend = unchecked((int)dividend);
                    quotient = (uint)(signedDividend / signedDivisor);
                    remainder = (uint)(signedDividend % signedDivisor);
                }

                if ((quotient & 0xFFFF_0000) != 0)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                }
                else
                {
                    State.D[reg] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                    State.SetNegativeZero(quotient, M68kOperandSize.Word);
                    State.SetFlag(M68kCpuState.Overflow, false);
                    State.SetFlag(M68kCpuState.Carry, false);
                }

                AddCycles(140);
                return true;
            }

            if ((line == 0x9 || line == 0xD || line == 0xB) && (opmode == 3 || opmode == 7))
            {
                var size = opmode == 3 ? M68kOperandSize.Word : M68kOperandSize.Long;
                var ea = ResolveEa(mode, eaReg, size);
                var value = ea.Read();
                if (line == 0xB)
                {
                    _ = Subtract(State.A[reg], value, size, setExtend: false, storeResult: false);
                }
                else if (line == 0x9)
                {
                    State.A[reg] = State.A[reg] - M68kCpuState.SignExtend(value, size);
                }
                else
                {
                    State.A[reg] = State.A[reg] + M68kCpuState.SignExtend(value, size);
                }

                AddCycles(size == M68kOperandSize.Long ? 8 : 6);
                return true;
            }

            var operandSize = opmode switch
            {
                0 or 4 => M68kOperandSize.Byte,
                1 or 5 => M68kOperandSize.Word,
                2 or 6 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (operandSize == 0)
            {
                return false;
            }

            var registerToEa = opmode >= 4;
            if (line == 0xB)
            {
                registerToEa = true;
            }

            var eaOperand = ResolveEa(mode, eaReg, operandSize, write: registerToEa && line != 0xB);
            var eaValue = eaOperand.Read();
            var regValue = State.D[reg] & M68kCpuState.Mask(operandSize);
            uint result;
            switch (line)
            {
                case 0x8:
                    result = registerToEa ? eaValue | regValue : regValue | eaValue;
                    if (registerToEa)
                    {
                        eaOperand.Write(result);
                    }
                    else
                    {
                        WriteDataRegister(reg, result, operandSize);
                    }

                    SetLogicFlags(result, operandSize);
                    break;
                case 0x9:
                    if (registerToEa)
                    {
                        result = Subtract(eaValue, regValue, operandSize, setExtend: true);
                        eaOperand.Write(result);
                    }
                    else
                    {
                        result = Subtract(regValue, eaValue, operandSize, setExtend: true);
                        WriteDataRegister(reg, result, operandSize);
                    }

                    break;
                case 0xB:
                    if (opmode >= 4)
                    {
                        result = eaValue ^ regValue;
                        eaOperand.Write(result);
                        SetLogicFlags(result, operandSize);
                    }
                    else
                    {
                        _ = Subtract(regValue, eaValue, operandSize, setExtend: false, storeResult: false);
                    }

                    break;
                case 0xC:
                    result = registerToEa ? eaValue & regValue : regValue & eaValue;
                    if (registerToEa)
                    {
                        eaOperand.Write(result);
                    }
                    else
                    {
                        WriteDataRegister(reg, result, operandSize);
                    }

                    SetLogicFlags(result, operandSize);
                    break;
                default:
                    if (registerToEa)
                    {
                        result = Add(eaValue, regValue, operandSize, setExtend: true);
                        eaOperand.Write(result);
                    }
                    else
                    {
                        result = Add(regValue, eaValue, operandSize, setExtend: true);
                        WriteDataRegister(reg, result, operandSize);
                    }

                    break;
            }

            AddCycles(operandSize == M68kOperandSize.Long ? 12 : 8);
            return true;
        }

        private bool DecodeShiftRotate(ushort opcode)
        {
            if ((opcode & 0xF000) != 0xE000)
            {
                return false;
            }

            if ((opcode & 0x00C0) == 0x00C0)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Word, write: true);
                var value = ea.Read() & 0xFFFF;
                var type = (opcode >> 9) & 3;
                var left = (opcode & 0x0100) != 0;
                var result = Shift(value, 1, M68kOperandSize.Word, type, left);
                ea.Write(result);
                AddCycles(8);
                return true;
            }

            var reg = opcode & 7;
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            var count = (opcode >> 9) & 7;
            if ((opcode & 0x0020) != 0)
            {
                count = (int)(State.D[count] & 63);
            }
            else if (count == 0)
            {
                count = 8;
            }

            var typeRegister = (opcode >> 3) & 3;
            var leftRegister = (opcode & 0x0100) != 0;
            var valueRegister = State.D[reg] & M68kCpuState.Mask(size);
            var shifted = Shift(valueRegister, count, size, typeRegister, leftRegister);
            WriteDataRegister(reg, shifted, size);
            AddCycles(6 + (count * 2));
            return true;
        }

        private void DecodeMovem(ushort opcode)
        {
            var size = (opcode & 0x0040) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            var registerMask = FetchWord();
            var directionMemoryToRegisters = (opcode & 0x0400) != 0;
            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;

            if (!directionMemoryToRegisters && mode == 4)
            {
                var address = State.A[reg];
                for (var bit = 0; bit < 16; bit++)
                {
                    if ((registerMask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    var register = 15 - bit;
                    address -= (uint)size;
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    if (size == M68kOperandSize.Word)
                    {
                        WriteWord(address, (ushort)value);
                    }
                    else
                    {
                        WriteLong(address, value);
                    }
                }

                State.A[reg] = address;
                AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
                return;
            }

            var ea = ResolveEa(mode, reg, size, write: !directionMemoryToRegisters, addressOnly: true);
            var current = ea.Address;
            for (var register = 0; register < 16; register++)
            {
                if ((registerMask & (1 << register)) == 0)
                {
                    continue;
                }

                if (directionMemoryToRegisters)
                {
                    var value = size == M68kOperandSize.Word
                        ? M68kCpuState.SignExtend(ReadWord(current), M68kOperandSize.Word)
                        : ReadLong(current);
                    if (register < 8)
                    {
                        State.D[register] = value;
                    }
                    else
                    {
                        State.A[register - 8] = value;
                    }
                }
                else
                {
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    if (size == M68kOperandSize.Word)
                    {
                        WriteWord(current, (ushort)value);
                    }
                    else
                    {
                        WriteLong(current, value);
                    }
                }

                current += (uint)size;
            }

            if (directionMemoryToRegisters && mode == 3)
            {
                State.A[reg] = current;
            }

            AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
        }

        private EaOperand ResolveEa(int mode, int reg, M68kOperandSize size, bool write = false, bool addressOnly = false)
        {
            switch (mode)
            {
                case 0:
                    return EaOperand.DataRegister(this, reg, size);
                case 1:
                    return EaOperand.AddressRegister(this, reg, size);
                case 2:
                    return EaOperand.Memory(this, State.A[reg], size);
                case 3:
                {
                    var address = State.A[reg];
                    if (!addressOnly)
                    {
                        State.A[reg] += AddressIncrement(reg, size);
                    }

                    return EaOperand.Memory(this, address, size);
                }
                case 4:
                {
                    State.A[reg] -= AddressIncrement(reg, size);
                    return EaOperand.Memory(this, State.A[reg], size);
                }
                case 5:
                {
                    var displacement = unchecked((short)FetchWord());
                    return EaOperand.Memory(this, (uint)(State.A[reg] + displacement), size);
                }
                case 6:
                {
                    var extension = FetchWord();
                    var displacement = unchecked((sbyte)(extension & 0xFF));
                    var indexReg = (extension >> 12) & 7;
                    var indexValue = ((extension & 0x8000) != 0 ? State.A[indexReg] : State.D[indexReg]);
                    if ((extension & 0x0800) == 0)
                    {
                        indexValue = M68kCpuState.SignExtend(indexValue, M68kOperandSize.Word);
                    }

                    return EaOperand.Memory(this, (uint)(State.A[reg] + indexValue + displacement), size);
                }
                case 7:
                    return ResolveMode7(reg, size);
                default:
                    throw new InvalidOperationException("Invalid effective address mode.");
            }
        }

        private EaOperand ResolveMode7(int reg, M68kOperandSize size)
        {
            return reg switch
            {
                0 => EaOperand.Memory(this, (uint)(short)FetchWord(), size),
                1 => EaOperand.Memory(this, FetchLong(), size),
                2 => ResolvePcRelative(size),
                3 => ResolvePcIndexed(size),
                4 => EaOperand.Immediate(this, FetchImmediate(size), size),
                _ => throw new UnsupportedM68kOpcodeException(State.LastOpcode, State.ProgramCounter - 2)
            };
        }

        private EaOperand ResolvePcRelative(M68kOperandSize size)
        {
            var extensionAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            return EaOperand.Memory(this, (uint)(extensionAddress + displacement), size);
        }

        private EaOperand ResolvePcIndexed(M68kOperandSize size)
        {
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var displacement = unchecked((sbyte)(extension & 0xFF));
            var indexReg = (extension >> 12) & 7;
            var indexValue = ((extension & 0x8000) != 0 ? State.A[indexReg] : State.D[indexReg]);
            if ((extension & 0x0800) == 0)
            {
                indexValue = M68kCpuState.SignExtend(indexValue, M68kOperandSize.Word);
            }

            return EaOperand.Memory(this, (uint)(extensionAddress + indexValue + displacement), size);
        }

        private uint ReadEaValue(EaOperand operand)
        {
            return operand.Read();
        }

        private void WriteDataRegister(int reg, uint value, M68kOperandSize size)
        {
            var mask = M68kCpuState.Mask(size);
            State.D[reg] = size == M68kOperandSize.Long
                ? value
                : (State.D[reg] & ~mask) | (value & mask);
        }

        private uint Add(uint destination, uint source, M68kOperandSize size, bool setExtend)
        {
            var mask = M68kCpuState.Mask(size);
            var sign = M68kCpuState.SignBit(size);
            destination &= mask;
            source &= mask;
            var full = (ulong)destination + source;
            var result = (uint)full & mask;
            var carry = full > mask;
            var overflow = (~(destination ^ source) & (destination ^ result) & sign) != 0;
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, carry);
            if (setExtend)
            {
                State.SetFlag(M68kCpuState.Extend, carry);
            }

            return result;
        }

        private uint Subtract(uint destination, uint source, M68kOperandSize size, bool setExtend, bool storeResult = true)
        {
            var mask = M68kCpuState.Mask(size);
            var sign = M68kCpuState.SignBit(size);
            destination &= mask;
            source &= mask;
            var result = (destination - source) & mask;
            var borrow = source > destination;
            var overflow = ((destination ^ source) & (destination ^ result) & sign) != 0;
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, borrow);
            if (setExtend)
            {
                State.SetFlag(M68kCpuState.Extend, borrow);
            }

            _ = storeResult;
            return result;
        }

        private uint Shift(uint value, int count, M68kOperandSize size, int type, bool left)
        {
            value &= M68kCpuState.Mask(size);
            if (count == 0)
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Carry, false);
                State.SetFlag(M68kCpuState.Overflow, false);
                return value;
            }

            var bits = size == M68kOperandSize.Long ? 32 : size == M68kOperandSize.Word ? 16 : 8;
            var mask = M68kCpuState.Mask(size);
            var carry = false;
            for (var i = 0; i < count; i++)
            {
                if (left)
                {
                    carry = (value & (1u << (bits - 1))) != 0;
                    value = ((value << 1) & mask) | (type == 3 && carry ? 1u : 0u);
                }
                else
                {
                    carry = (value & 1) != 0;
                    if (type == 0)
                    {
                        var sign = value & (1u << (bits - 1));
                        value = (value >> 1) | sign;
                    }
                    else if (type == 3)
                    {
                        value = (value >> 1) | (carry ? 1u << (bits - 1) : 0u);
                    }
                    else
                    {
                        value >>= 1;
                    }
                }
            }

            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            State.SetFlag(M68kCpuState.Overflow, false);
            return value & mask;
        }

        private void SetLogicFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        private bool CheckCondition(int condition)
        {
            var c = State.GetFlag(M68kCpuState.Carry);
            var v = State.GetFlag(M68kCpuState.Overflow);
            var z = State.GetFlag(M68kCpuState.Zero);
            var n = State.GetFlag(M68kCpuState.Negative);
            return condition switch
            {
                0x0 => true,
                0x1 => false,
                0x2 => !c && !z,
                0x3 => c || z,
                0x4 => !c,
                0x5 => c,
                0x6 => !z,
                0x7 => z,
                0x8 => !v,
                0x9 => v,
                0xA => !n,
                0xB => n,
                0xC => n == v,
                0xD => n != v,
                0xE => !z && n == v,
                0xF => z || n != v,
                _ => false
            };
        }

        private static M68kOperandSize DecodeImmediateSize(ushort opcode)
        {
            return ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => throw new UnsupportedM68kOpcodeException(opcode, 0)
            };
        }

        private uint FetchImmediate(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => (uint)(FetchWord() & 0xFF),
                M68kOperandSize.Word => FetchWord(),
                _ => FetchLong()
            };
        }

        private ushort FetchWord()
        {
            var value = ReadWord(State.ProgramCounter, AmigaBusAccessKind.CpuInstructionFetch);
            State.ProgramCounter += 2;
            return value;
        }

        private uint FetchLong()
        {
            var high = FetchWord();
            var low = FetchWord();
            return ((uint)high << 16) | low;
        }

        private byte ReadByte(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            var cycle = State.Cycles;
            var value = _bus.ReadByte(address, ref cycle, accessKind);
            State.Cycles = cycle;
            return value;
        }

        private ushort ReadWord(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 word read at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            var value = _bus.ReadWord(address, ref cycle, accessKind);
            State.Cycles = cycle;
            return value;
        }

        private uint ReadLong(uint address)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 long read at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            var value = _bus.ReadLong(address, ref cycle, AmigaBusAccessKind.CpuDataRead);
            State.Cycles = cycle;
            return value;
        }

        private void WriteByte(uint address, byte value)
        {
            var cycle = State.Cycles;
            _bus.WriteByte(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
        }

        private void WriteWord(uint address, ushort value)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 word write at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            _bus.WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
        }

        private void WriteLong(uint address, uint value)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 long write at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            _bus.WriteLong(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
        }

        private void PushWord(ushort value)
        {
            State.A[7] -= 2;
            WriteWord(State.A[7], value);
        }

        private void PushLong(uint value)
        {
            State.A[7] -= 4;
            WriteLong(State.A[7], value);
        }

        private ushort PullWord()
        {
            var value = ReadWord(State.A[7]);
            State.A[7] += 2;
            return value;
        }

        private uint PullLong()
        {
            var value = ReadLong(State.A[7]);
            State.A[7] += 4;
            return value;
        }

        private void AddCycles(int cycles)
        {
            State.Cycles += Math.Max(1, cycles);
        }

        private static uint AddressIncrement(int reg, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte && reg == 7)
            {
                return 2;
            }

            return (uint)size;
        }

        private static int EstimateEaCycles(EaOperand source, EaOperand destination, M68kOperandSize size, bool write)
        {
            _ = source;
            _ = destination;
            _ = write;
            return size == M68kOperandSize.Long ? 12 : 8;
        }

        private static int CountBits(int value)
        {
            var count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private readonly struct EaOperand
        {
            private readonly M68kInterpreter _cpu;
            private readonly int _kind;
            private readonly int _reg;
            private readonly uint _immediate;

            private EaOperand(M68kInterpreter cpu, int kind, int reg, uint address, uint immediate, M68kOperandSize size)
            {
                _cpu = cpu;
                _kind = kind;
                _reg = reg;
                Address = address;
                _immediate = immediate;
                Size = size;
            }

            public uint Address { get; }

            public M68kOperandSize Size { get; }

            public static EaOperand DataRegister(M68kInterpreter cpu, int reg, M68kOperandSize size)
            {
                return new EaOperand(cpu, 0, reg, 0, 0, size);
            }

            public static EaOperand AddressRegister(M68kInterpreter cpu, int reg, M68kOperandSize size)
            {
                return new EaOperand(cpu, 1, reg, 0, 0, size);
            }

            public static EaOperand Memory(M68kInterpreter cpu, uint address, M68kOperandSize size)
            {
                return new EaOperand(cpu, 2, 0, address, 0, size);
            }

            public static EaOperand Immediate(M68kInterpreter cpu, uint value, M68kOperandSize size)
            {
                return new EaOperand(cpu, 3, 0, 0, value, size);
            }

            public uint Read()
            {
                return _kind switch
                {
                    0 => _cpu.State.D[_reg] & M68kCpuState.Mask(Size),
                    1 => Size == M68kOperandSize.Word ? _cpu.State.A[_reg] & 0xFFFF : _cpu.State.A[_reg],
                    2 => Size switch
                    {
                        M68kOperandSize.Byte => _cpu.ReadByte(Address),
                        M68kOperandSize.Word => _cpu.ReadWord(Address),
                        _ => _cpu.ReadLong(Address)
                    },
                    3 => _immediate & M68kCpuState.Mask(Size),
                    _ => 0
                };
            }

            public void Write(uint value)
            {
                value &= M68kCpuState.Mask(Size);
                switch (_kind)
                {
                    case 0:
                        _cpu.WriteDataRegister(_reg, value, Size);
                        break;
                    case 1:
                        _cpu.State.A[_reg] = Size == M68kOperandSize.Word
                            ? M68kCpuState.SignExtend(value, M68kOperandSize.Word)
                            : value;
                        break;
                    case 2:
                        if (Size == M68kOperandSize.Byte)
                        {
                            _cpu.WriteByte(Address, (byte)value);
                        }
                        else if (Size == M68kOperandSize.Word)
                        {
                            _cpu.WriteWord(Address, (ushort)value);
                        }
                        else
                        {
                            _cpu.WriteLong(Address, value);
                        }

                        break;
                    default:
                        throw new AmigaEmulationException("Cannot write to an immediate MC68000 operand.");
                }
            }
        }
    }
}
