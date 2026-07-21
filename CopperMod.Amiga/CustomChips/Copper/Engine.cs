using System;

namespace CopperMod.Amiga.CustomChips.Copper
{
    internal sealed class Copper
    {
        public void ExecuteList(AmigaBus bus, uint listAddress, int maxInstructions = 1024, Action<ushort, ushort>? onMove = null)
        {
            ArgumentNullException.ThrowIfNull(bus);
            var pc = listAddress;
            var cycle = 0L;
            ushort copcon = 0;
            var suppressNextMove = false;
            for (var i = 0; i < maxInstructions; i++)
            {
                var firstRead = bus.ReadChipWordForDeviceWithResult(
                    AmigaBusRequester.Copper,
                    AmigaBusAccessKind.Copper,
                    pc,
                    cycle);
                cycle = firstRead.BusAccess.CompletedCycle;
                var secondRead = bus.ReadChipWordForDeviceWithResult(
                    AmigaBusRequester.Copper,
                    AmigaBusAccessKind.Copper,
                    pc + 2,
                    cycle);
                cycle = secondRead.BusAccess.CompletedCycle;
                var first = firstRead.Value;
                var second = secondRead.Value;
                pc += 4;
                if (first == 0xFFFF && second == 0xFFFE)
                {
                    return;
                }

                if ((first & 1) == 0)
                {
                    var register = (ushort)(first & 0x01FE);
                    var suppressMove = suppressNextMove;
                    suppressNextMove = false;
                    if (bus.StopsCopperAtCustomRegister(register, copcon))
                    {
                        return;
                    }

                    if (suppressMove)
                    {
                        continue;
                    }

                    if (!bus.CanCopperWriteCustomRegister(register, copcon))
                    {
                        continue;
                    }

                    if (register == 0x02E)
                    {
                        copcon = second;
                    }

                    onMove?.Invoke(register, second);
                    bus.WriteDeviceWord(
                        AmigaBusRequester.Copper,
                        AmigaBusAccessKind.Copper,
                        0x00DFF000u + register,
                        second,
                        cycle);
                    continue;
                }

                if ((second & 1) != 0)
                {
                    suppressNextMove = IsCopperComparisonSatisfiedAtResetBeam(first, second);
                    continue;
                }
            }
        }

        private static bool IsCopperComparisonSatisfiedAtResetBeam(ushort first, ushort second)
        {
            var mask = (ushort)(0x8000 | (second & 0x7FFE));
            var target = (ushort)(first & 0xFFFE);
            return (0 & mask) >= (target & mask);
        }
    }

}
