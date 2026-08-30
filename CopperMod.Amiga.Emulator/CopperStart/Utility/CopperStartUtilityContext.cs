using System;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Utility;

/// <summary>Concrete reset-scoped guest-memory and allocation bridge for utility.library.</summary>
internal sealed class CopperStartUtilityContext
{
    public CopperStartUtilityContext(
        AmigaBus bus,
        HostGuestMemory memory,
        Func<int, uint, uint> allocate,
        Action<uint, int> free,
        Action<Copper68k.M68kCpuState, uint, uint>? startGuestSubroutine = null,
        Action<uint>? replyMessage = null)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));
        Free = free ?? throw new ArgumentNullException(nameof(free));
        StartGuestSubroutine = startGuestSubroutine;
        ReplyMessage = replyMessage;
    }

    public AmigaBus Bus { get; }
    public HostGuestMemory Memory { get; }
    public Func<int, uint, uint> Allocate { get; }
    public Action<uint, int> Free { get; }
    /// <summary>Enters guest code only through the outer emulator-loop continuation path.</summary>
    public Action<Copper68k.M68kCpuState, uint, uint>? StartGuestSubroutine { get; }
    public Action<uint>? ReplyMessage { get; }
}
