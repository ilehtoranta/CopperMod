# Copper6510

Copper6510 is a reusable C# MOS 6510 CPU emulation core extracted from
CopperMod.Sid. It provides a cycle-aware interpreter with official and common
undocumented opcode support behind a small bus/core API.

The package is intended for emulator projects that want to supply their own
memory map, devices, interrupt sources, and host integration.

## Install

```powershell
dotnet add package Copper6510
```

Copper6510 currently targets `.NET 10`.

## Quick Start

Implement `IMos6510Bus`, create a `Mos6510`, reset it with an initial program
counter, then execute instructions.

```csharp
using Copper6510;

var bus = new RamBus();
bus.Memory[0x1000] = 0xA9; // LDA #$42
bus.Memory[0x1001] = 0x42;

var cpu = new Mos6510(bus);
cpu.Reset(0x1000);
cpu.ExecuteInstruction();

Console.WriteLine(cpu.A); // 66

sealed class RamBus : IMos6510Bus
{
    public byte[] Memory { get; } = new byte[65536];

    public byte Read(ushort address, int cycleOffset = 0, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Read)
        => Memory[address];

    public void Write(ushort address, byte value, int cycleOffset, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Write)
        => Memory[address] = value;

    public void Idle(ushort address, int cycleOffset, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Idle)
    {
    }
}
```

## Bus Contract

`IMos6510Bus` receives every CPU byte read, byte write, and idle cycle with the
16-bit address, the cycle offset within the current instruction, and the bus
access kind.

`Mos6510BusAccessKind` distinguishes opcode fetches, operand fetches, data
reads and writes, stack accesses, vector reads, dummy cycles, and idle cycles.
Hosts can use this to model devices whose side effects depend on precise CPU
cycle timing.

## State and Interrupts

`Mos6510` exposes the accumulator, index registers, stack pointer, program
counter, status register, cycle counter, halt state, and last fetched opcode.

Use `TryRequestIrq` or `RequestIrq` for maskable interrupts and `TryRequestNmi`
or `RequestNmi` for non-maskable interrupts. `BeginSubroutine` is provided for
hosts that need to call machine-code routines directly and detect sentinel
returns.

## Status

Copper6510 1.1 is an accuracy-oriented emulator core with a stable,
intentionally small public API. Applications should depend on `Mos6510`,
`IMos6510Bus`, and `Mos6510BusAccessKind`.
