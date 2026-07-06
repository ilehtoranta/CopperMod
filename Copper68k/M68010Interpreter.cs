/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace Copper68k
{
    internal sealed class M68010Interpreter : M68020Interpreter
    {
        public M68010Interpreter(IM68kBus bus)
            : base(
                bus,
                M68020CpuProfile.OcsAccelerator14Mhz,
                new M68kCpuState(),
                enableM68020StackMode: false,
                opcodeKinds: M68020OpcodeDispatchTable.M68010Kinds)
        {
        }

        internal M68010Interpreter(
            IM68kBus bus,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(
                bus,
                M68020CpuProfile.OcsAccelerator14Mhz,
                state,
                instructionFrequency,
                enableM68020StackMode: false,
                opcodeKinds: M68020OpcodeDispatchTable.M68010Kinds)
        {
        }

        protected override bool TryReadControlRegister(int register, uint instructionPc, out uint value)
        {
            switch (register)
            {
                case 0x000:
                    value = State.SourceFunctionCode;
                    return true;
                case 0x001:
                    value = State.DestinationFunctionCode;
                    return true;
                case 0x801:
                    value = State.VectorBaseRegister;
                    return true;
                default:
                    value = RaiseIllegalControlRegister(instructionPc);
                    return false;
            }
        }

        protected override bool TryWriteControlRegister(int register, uint value, uint instructionPc)
        {
            switch (register)
            {
                case 0x000:
                    State.SourceFunctionCode = value & 0x7;
                    return true;
                case 0x001:
                    State.DestinationFunctionCode = value & 0x7;
                    return true;
                case 0x801:
                    State.VectorBaseRegister = value;
                    return true;
                default:
                    _ = RaiseIllegalControlRegister(instructionPc);
                    return false;
            }
        }

        protected override bool TryRaiseMisalignedWordDataRead(uint address, uint instructionPc)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(3, instructionPc, savedStatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushWord(0x800C);
            PushLong(instructionPc);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + 0x0C);
            CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
            _ = address;
            return true;
        }
    }
}
