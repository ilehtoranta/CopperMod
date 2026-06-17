using System;

namespace CopperMod.Amiga
{
    internal sealed class M68030Interpreter : M68020Interpreter
    {
        public M68030Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : base(bus, profile)
        {
            if (profile.Model != M68kAcceleratorModel.M68030)
            {
                throw new ArgumentException("The MC68030 interpreter requires an MC68030 CPU profile.", nameof(profile));
            }
        }

        public M68030Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(bus, profile, state, instructionFrequency)
        {
            if (profile.Model != M68kAcceleratorModel.M68030)
            {
                throw new ArgumentException("The MC68030 interpreter requires an MC68030 CPU profile.", nameof(profile));
            }
        }
    }
}
