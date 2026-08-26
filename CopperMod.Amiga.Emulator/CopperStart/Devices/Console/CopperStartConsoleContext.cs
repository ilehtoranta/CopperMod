using System;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Input;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart.Devices.Console;

/// <summary>
/// Concrete, reset-scoped integration surface for console.device.  It keeps
/// ordinary guest memory access separate from guest-code entry: the latter is
/// always resumed by the outer emulator loop through <see cref="StartGuestSubroutine"/>.
/// </summary>
internal sealed class CopperStartConsoleContext
{
    public CopperStartConsoleContext(
        AmigaBus bus,
        ExecMemoryOperations memory,
        InputDeviceServices input,
        Action<uint> reply,
        Action<M68kCpuState, uint, uint, uint> drawText,
        Action<M68kCpuState, uint, uint> startGuestSubroutine)
    {
        Bus = bus;
        Memory = memory;
        Input = input;
        Reply = reply;
        DrawText = drawText;
        StartGuestSubroutine = startGuestSubroutine;
    }

    public AmigaBus Bus { get; }
    public ExecMemoryOperations Memory { get; }
    public InputDeviceServices Input { get; }
    public Action<uint> Reply { get; }
    public Action<M68kCpuState, uint, uint, uint> DrawText { get; }
    public Action<M68kCpuState, uint, uint> StartGuestSubroutine { get; }
}
