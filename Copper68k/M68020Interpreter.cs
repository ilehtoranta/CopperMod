/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace Copper68k
{
    internal sealed class M68020Interpreter : M68kAdvancedTimingInterpreter
    {
        public M68020Interpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.OcsAccelerator14Mhz)
        {
        }

        internal M68020Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : base(bus, profile, new M68kCpuState())
        {
        }

        internal M68020Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(
                bus,
                profile,
                state,
                instructionFrequency,
                opcodeKinds: M68020OpcodeDispatchTable.M68020Kinds)
        {
        }
    }
}
