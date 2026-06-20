# Copper68k

Copper68k is a reusable C# Motorola 68000-family CPU emulation core extracted
from CopperScreen and CopperMod. It provides interpreter backends for MC68000,
MC68020, MC68030, and MC68040-style execution behind a small bus/core API.

The package is intended for emulator projects that want to supply their own
memory map, devices, interrupt sources, and host integration.

## Install

```powershell
dotnet add package Copper68k
```

Copper68k currently targets `.NET 10`.

## Quick Start

Implement `IM68kBus`, create a core through `M68kCoreFactory`, reset it with an
initial PC and stack pointer, then execute instructions.

```csharp
using Copper68k;

var bus = new RamBus(64 * 1024);
bus.WriteWord(0x1000, 0x7042); // MOVEQ #$42,D0

using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
cpu.Reset(programCounter: 0x1000, stackPointer: 0x2000);
cpu.ExecuteInstruction();

Console.WriteLine(cpu.State.D[0]); // 66

sealed class RamBus : IM68kBus
{
    private readonly byte[] memory;

    public RamBus(int size) => memory = new byte[size];

    public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
        => memory[address % memory.Length];

    public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
        => (ushort)((ReadByte(address, ref cycle, accessKind) << 8) |
            ReadByte(address + 1, ref cycle, accessKind));

    public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
        => ((uint)ReadWord(address, ref cycle, accessKind) << 16) |
            ReadWord(address + 2, ref cycle, accessKind);

    public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
        => memory[address % memory.Length] = value;

    public void WriteWord(uint address, ushort value)
    {
        long cycle = 0;
        WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }

    public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
    {
        WriteByte(address, (byte)(value >> 8), ref cycle, accessKind);
        WriteByte(address + 1, (byte)value, ref cycle, accessKind);
    }

    public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
    {
        WriteWord(address, (ushort)(value >> 16), ref cycle, accessKind);
        WriteWord(address + 2, (ushort)value, ref cycle, accessKind);
    }

    public bool HasHostTrapStub(uint address) => false;

    public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
        => false;

    public void ResetExternalDevices(long cycle)
    {
    }
}
```

## CPU Models

Use `M68kCpuModel` to select the interpreter backend:

- `M68000`: base 68000 interpreter with 68000-style exception frames.
- `M68020`: 68020-style core with VBR, format-zero exception frames, and native-cycle timing state.
- `M68030`: 68030-oriented interpreter profile.
- `M68040`: 68040-oriented interpreter with the current integer/FPU/MMU support used by CopperScreen.

Host-specific JIT backends are not part of the Copper68k NuGet package.

## Bus Contract

`IM68kBus` receives every CPU byte, word, and long access with an address, a
mutable cycle counter, and an access kind. Implementations may advance the
cycle counter to model memory wait states.

`M68kBusAccessKind` distinguishes instruction fetches, data reads, and data
writes. This matters for bus errors, address errors, MMU translation, and
device side effects.

`HasHostTrapStub` and `TryInvokeHostTrap` are optional host integration hooks.
Return `false` from both if your emulator does not use CopperMod host traps.

## Reset and Interrupts

`Reset(programCounter, stackPointer)` clears the general registers, sets the PC
and supervisor stack pointer, and initializes SR to `0x2700` (`Supervisor | IPL
7`), matching 68k reset behavior.

`RequestInterrupt(level, vectorAddress)` ignores levels that are masked by SR.
Pass a vector-table byte offset such as `24u * 4` for level 6 autovector, or a
device-specific vector offset. On 68020+ cores the offset is relative to the
current vector base register.

## State

`M68kCpuState` exposes data registers `D[0..7]`, address registers `A[0..7]`,
the program counter, status register, stack pointers, cycle counters, STOP/HALT
state, and 68020+ control registers. Setting `StatusRegister` also updates the
active stack pointer when supervisor/user or master/interrupt stack mode
changes.

## Status

Copper68k is an accuracy-oriented emulator core extracted for reuse. The public
API is intentionally small and may continue to tighten before a stable 1.0
release.
