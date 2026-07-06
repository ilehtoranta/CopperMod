/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper68k
{
    internal sealed class M68030Interpreter : M68020Interpreter
    {
        public M68030Interpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz)
        {
        }

        internal M68030Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : base(bus, profile, new M68kCpuState(), opcodeKinds: M68020OpcodeDispatchTable.M68030Kinds)
        {
            if (profile.Model != M68kAcceleratorModel.M68030)
            {
                throw new ArgumentException("The MC68030 interpreter requires an MC68030 CPU profile.", nameof(profile));
            }
        }

        internal M68030Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(bus, profile, state, instructionFrequency, opcodeKinds: M68020OpcodeDispatchTable.M68030Kinds)
        {
            if (profile.Model != M68kAcceleratorModel.M68030)
            {
                throw new ArgumentException("The MC68030 interpreter requires an MC68030 CPU profile.", nameof(profile));
            }
        }
    }
}
