using System;

namespace CopperMod.Amiga
{
    internal sealed class AmigaCopper
    {
        private const ushort CopconCopperDanger = 0x0002;

        public void ExecuteList(AmigaBus bus, uint listAddress, int maxInstructions = 1024, Action<ushort, ushort>? onMove = null)
        {
            ArgumentNullException.ThrowIfNull(bus);
            var pc = listAddress;
            ushort copcon = 0;
            var suppressNextMove = false;
            for (var i = 0; i < maxInstructions; i++)
            {
                var first = bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc, 0);
                var second = bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc + 2, 0);
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
                    if (IsCopperDangerStopRegister(register, copcon))
                    {
                        return;
                    }

                    if (suppressMove)
                    {
                        continue;
                    }

                    if (!CanCopperWriteRegister(register, copcon))
                    {
                        continue;
                    }

                    if (register == 0x02E)
                    {
                        copcon = second;
                    }

                    onMove?.Invoke(register, second);
                    bus.WriteDeviceWord(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, 0x00DFF000u + register, second, 0);
                    continue;
                }

                if ((second & 1) != 0)
                {
                    suppressNextMove = IsCopperComparisonSatisfiedAtResetBeam(first, second);
                    continue;
                }
            }
        }

        private static bool CanCopperWriteRegister(ushort offset, ushort copcon)
        {
            if (offset < 0x010)
            {
                return false;
            }

            return offset >= 0x020 || (copcon & CopconCopperDanger) != 0;
        }

        private static bool IsCopperDangerStopRegister(ushort offset, ushort copcon)
        {
            if (offset < 0x010)
            {
                return true;
            }

            return offset < 0x020 && (copcon & CopconCopperDanger) == 0;
        }

        private static bool IsCopperComparisonSatisfiedAtResetBeam(ushort first, ushort second)
        {
            var mask = (ushort)(0x8000 | (second & 0x7FFE));
            var target = (ushort)(first & 0xFFFE);
            return (0 & mask) >= (target & mask);
        }
    }

}
