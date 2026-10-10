namespace Copper68k.Tests.Synthetic;

// M68000PM table 3-19. These Boolean predicates are independent of production condition decoding.
internal static class ControlSpecification
{
    public static bool Condition(int condition, int ccr)
    {
        var c = (ccr & 1) != 0; var v = (ccr & 2) != 0; var z = (ccr & 4) != 0; var n = (ccr & 8) != 0;
        return condition switch
        {
            0 => true, 1 => false, 2 => !c && !z, 3 => c || z, 4 => !c, 5 => c, 6 => !z, 7 => z,
            8 => !v, 9 => v, 10 => !n, 11 => n, 12 => n == v, 13 => n != v, 14 => !z && n == v, _ => z || n != v
        };
    }

    public static bool InstallTargetSentinel(SyntheticMachine machine, uint target, uint nextPc)
    {
        var physical = machine.Model.Physical(target);
        if (physical >= SyntheticMachine.Code && physical < nextPc) return false;
        machine.InitializePhysical(target, 0x4e71, 2);
        machine.InitializePhysical(unchecked(target + 2), 0x4e71, 2);
        return true;
    }
}
