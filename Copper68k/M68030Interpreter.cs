/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper68k
{
    internal sealed class M68030Interpreter : M68kAdvancedTimingInterpreter
    {
        private uint _translationControl;

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

        public override void Reset(uint programCounter, uint stackPointer)
        {
            base.Reset(programCounter, stackPointer);
            _translationControl = 0;
        }

        protected override bool TryReadControlRegister(int register, uint instructionPc, out uint value)
        {
            if (register == 0x003)
            {
                value = _translationControl;
                return true;
            }

            return base.TryReadControlRegister(register, instructionPc, out value);
        }

        protected override bool TryWriteControlRegister(int register, uint value, uint instructionPc)
        {
            if (register == 0x003)
            {
                _translationControl = value;
                return true;
            }

            return base.TryWriteControlRegister(register, value, instructionPc);
        }
    }
}
