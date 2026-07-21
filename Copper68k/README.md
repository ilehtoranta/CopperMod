# Copper68k

Copper68k is a reusable C# Motorola 68000-family CPU emulation core extracted
from CopperScreen and CopperMod. It provides interpreter backends for MC68000,
MC68020, MC68030, and MC68040-style execution behind a small bus/core API, plus
an opt-in MC68040 JIT backend for hosts that expose stable code snapshots and
write invalidation.

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

    public bool HasHostGateway(uint address) => false;

    public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
        => false;

    public void ResetExternalDevices(long cycle)
    {
    }
}
```

## CPU Models

Use `M68kCpuModel` to select the default interpreter backend:

- `M68000`: base 68000 interpreter with 68000-style exception frames.
- `M68020`: 68020-style core with VBR, format-zero exception frames, and native-cycle timing state.
- `M68EC020`: 68020-style core with a 24-bit external address bus and full 32-bit registers.
- `M68030`: 68030-oriented interpreter profile.
- `M68040`: 68040-oriented interpreter with the current integer/FPU/MMU support used by CopperScreen.

`M68kCoreFactory.Create(model, bus)` always creates the interpreter path. Use
the options overload only when you want a non-default execution mode.

## MC68040 JIT

The MC68040 JIT is included in the package as an opt-in backend. The concrete
implementation remains internal; package consumers select it through
`M68kCoreOptions`.

```csharp
using var cpu = M68kCoreFactory.Default.Create(
    M68kCpuModel.M68040,
    bus,
    new M68kCoreOptions { ExecutionMode = M68kExecutionMode.Jit });
```

JIT mode is supported only for `M68kCpuModel.M68040`. Requesting it for another
model throws `M68kEmulationException`.

The bus must implement `IM68kJitBus`. That capability tells Copper68k which
physical code ranges are eligible for compilation, lets the JIT capture immutable
code snapshots for background compilation, and raises invalidation events when
writable code changes.

Hosts may also implement `IM68kJitFastMemoryBus` and
`IM68kJitTimedMemoryBus` to expose direct fast-memory paths or host-specific
timed device shortcuts. If those optional interfaces are absent, compiled traces
fall back to normal `IM68kBus` memory access.

## Bus Contract

`IM68kBus` receives every CPU byte, word, and long access with an address, a
mutable cycle counter, and an access kind. Implementations may advance the
cycle counter to model memory wait states.

`M68kBusAccessKind` distinguishes instruction fetches, data reads, and data
writes. This matters for bus errors, address errors, MMU translation, and
device side effects.

`HasHostGateway` and `TryInvokeHostGateway` are optional host integration hooks.
The private gateway instruction is `FF00` followed by a big-endian 32-bit opaque token.
Return `false` from both if your emulator does not use CopperMod host gateways.

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

Copper68k 1.1 is an accuracy-oriented emulator core with a stable,
intentionally small public API. Applications should create cores through
`M68kCoreFactory` and depend on `IM68kBus`, `IM68kCore`, `M68kCpuModel`,
`M68kCpuState`, and the optional JIT capability interfaces rather than
implementation-specific interpreter or JIT classes.
