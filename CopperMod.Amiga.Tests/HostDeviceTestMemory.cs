using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

internal static class HostDeviceTestMemory
{
    public static ExecMemoryContext Create(AmigaBus bus, uint execBase, Func<uint>? currentTask = null)
    {
        uint next = 0x4000;
        return new ExecMemoryContext(bus, (bytes, flags) =>
        {
            uint address = next;
            next = checked((address + (uint)bytes + 3) & ~3u);
            if ((flags & (uint)global::Amiga.Exec.MemoryFlags.Clear) != 0)
                bus.ClearMemory(address, bytes);
            return address;
        }, (_, _) => 0, (_, _) => { }, _ => 0, (_, _, _) => 0,
            (_, _, _) => { }, _ => 0, (_, _, _) => { }, (_, _, _) => { }, (_, _) => { },
            getExecBase: () => execBase, getCurrentTask: currentTask ?? (() => 0));
    }
}
