# Copper6510

Copper6510 is a cycle-stepped NMOS 6510 CPU core for C64-class emulators. It
supports official and common undocumented opcodes, observable bus cycles,
hardware IRQ/NMI/RESET inputs, and RDY/AEC bus arbitration.

The package targets .NET 10.

## Quick start

```csharp
using Copper6510;

var bus = new RamBus();
bus.Memory[0x1000] = 0xA9; // LDA #$42
bus.Memory[0x1001] = 0x42;

var cpu = new Mos6510(bus);
cpu.InitializeState(0x1000); // deterministic host state injection
cpu.ExecuteInstruction();

Console.WriteLine(cpu.A); // 66

sealed class RamBus : IMos6510Bus
{
    public byte[] Memory { get; } = new byte[65536];

    public byte Read(ushort address, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Read)
        => Memory[address];

    public void Write(
        ushort address,
        byte value,
        Mos6510BusAccessKind kind = Mos6510BusAccessKind.Write)
        => Memory[address] = value;
}
```

`ExecuteInstruction()` is a convenience wrapper. Cycle-driven hosts should
call `StepCycle()` once per PHI2 cycle and inspect its allocation-free
`Mos6510CycleResult`.

Opcode fetch selects a static microprogram descriptor. Addressing operands,
effective addresses, page-cross state, and intermediate data remain in CPU
microstate between calls; instructions are not executed atomically and replayed.

## Pins and bus arbitration

Drive hardware inputs before the cycle on which they are sampled:

```csharp
cpu.SetIrqLine(ciaIrq || vicIrq); // level-sensitive
cpu.SetNmiLine(cia2Nmi);          // rising-edge latched
cpu.SetReadyLine(baHigh);         // low stalls reads; writes complete
cpu.SetBusAvailable(aecHigh);     // low freezes the CPU without a bus callback
var result = cpu.StepCycle();
```

Drive `SetResetLine(true)` for at least two cycles, then release it. The core
performs the seven-cycle NMOS reset sequence through `$FFFC/$FFFD`. Use
`InitializeState()` only when a host intentionally injects CPU state without
emulating RESET.

Every CPU-owned cycle calls exactly one `Read` or `Write`. Access kinds identify
executed and discarded opcode fetches, operands, dummy reads/writes, stack
accesses, and vector reads. RDY-low reads repeat; AEC-low cycles make no bus
callback.

`DummyRead`, `StackRead`, and `DiscardedOpcodeFetch` are real electrical reads,
not notifications. A bus implementation must return its normal value and apply
memory-mapped side effects on every callback, including every RDY-stalled repeat.
The callback address is the address observed on the NMOS 6510 bus, which can be
a sequential PC, current stack address, or partially corrected indexed/branch
address whose value the CPU ultimately discards.

## Migrating from 1.1

| Copper6510 1.1 | Copper6510 2.0 |
| --- | --- |
| Bus callbacks receive `cycleOffset` | Host owns the clock; one callback represents the current cycle |
| `IMos6510Bus.Idle` | Removed; unused CPU cycles are real dummy reads |
| `RequestIrq` / `TryRequestIrq` | `SetIrqLine(bool)` |
| `RequestNmi` / `TryRequestNmi` | `SetNmiLine(bool)` |
| `Reset(pc)` | `InitializeState(pc)` for injection or `SetResetLine` for hardware reset |
| Instruction-only execution | `StepCycle()` plus the `ExecuteInstruction()` convenience wrapper |

`BeginSubroutine` remains a synthetic, zero-time host helper. It is not a
hardware JSR and should not be used when every setup bus cycle must be modeled.
