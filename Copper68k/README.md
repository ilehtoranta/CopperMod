# Copper68k

Development `1.4.2-ocs020.56` corrects uncached instruction fetching in the OCS
040 interpreter. Aligned longword reads start at the eight-byte half-line base,
including when execution starts in the second longword. The holding register
retains loops in the first six bytes even with CACR.IE clear. Integer fallback
execution now uses the same frontend rather than a separate 68000 prefetch queue.
Reset, instruction-cache maintenance, privilege/translation changes and host
mapping generations prevent reuse of stale context. Guest data writes alone do
not invalidate held instructions. JIT max-speed fetching is unchanged.

This is a **demand-driven approximation**, not a complete 040 pipeline: consuming
the fourth word retires the half-line; speculative next-half-line transfers,
deferred prefetch faults, pipeline overlap and accurate instruction costs remain
unimplemented. These limits are not a Lotus-specific delay or compatibility rule.
The architectural fetch/retention expectations come from MC68040UM section 4.2
and the paragraph after table 7-3. Timings in chapter 10 require a separate BCLK
pipeline model; the current fixed one-native-cycle policy does not implement it.

Preceding development `1.4.2-ocs020.55` prepares experimental OCS 030/040 integration and
adds a distinct, integer-focused MC68060 diagnostic core. The 060 has its own
PCR identity, single supervisor stack with software M bit, removed-control-register
exceptions, vector-61 removed-integer traps, aligned CAS boundary, and eight-native-
clock approximate fixed timing policy. It is **not a complete 68060 implementation**:
FPU arithmetic, enabled MMU/transparent translation, BUSCR operation, full indexed
addressing and full exception/trace conformance remain unavailable or incomplete.
FPU control-register transfers and NULL/IDLE 12-byte state frames are implemented;
unsupported FPU forms stop explicitly. No JIT is provided for the 060.

The 040 now uses its documented CACR enable bits (15/31), with CINV/CPUSH
line/page/all maintenance and privilege/encoding checks. Chip RAM permits
instruction caching on 030/040 as well as 020. Cache capacity/associativity,
copyback data, physical cache-fill timing and superscalar dispatch remain bounded
policies, not hardware certification. The 000/020 execution profiles are preserved.

**Native Kickstart 1.3 cannot boot this 060 profile.** Its initial task FPU frame
is four bytes; MC68060UM requires twelve even for NULL. The correctly sized restore
consumes the following saved context and sends the task to address zero. A newer
OS and 060-aware task/FPU support are required before desktop availability. The
030 and 040 native paths are validated separately by the CopperScreen consumer.
This is an unpublished development package; source commits do not authorize release.

Architectural sources: [MC68060UM](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf)
sections 3.2.2, 8.2.4, 11.1.2, C.2 and appendix D; [MC68040UM](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
section 2.2.2.5; [M68000PRM](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf)
CINV/CPUSH. Remaining features must be implemented from these contracts, without
substituting 040 FPU frames or 68881 NULL frames to make old software boot.

## Preceding prerelease 1.4.2-ocs020.54

Prerelease `1.4.2-ocs020.54` fixes indexed `JSR` and 13 further operand forms
reached by Kickstart 1.3 CopperHDF startup and native Fast RAM allocation/file I/O.
The added forms cover `JMP`, `MOVE`, `MOVEA`, `ADD`, `ADDA`, `SUBA`, `ANDI`, `NOT`,
`CMPI` and `MOVEM`. Focused cases check signed/scaled indexes, extension-word PC
bases (including MOVEM's preceding register mask), stack aliasing/alignment,
widths, flags and preserved surrounding memory. The shared 030 executor receives
these forms; 040 retains its fallback. Full index extensions remain explicitly
unsupported on these paths. The operand-shape timing policy remains approximate.
This prerelease is approved for publication; no additional host CPU is enabled.

The suite passes 3,395 cases, including 189 new HDF/indexed-control cases. Against
the preceding `.53` binary, 162 of those new cases reproduce the missing forms;
27 fallback/full-extension controls pass. Six optional external conformance
cases are unavailable. Architectural expectations follow the instruction and
effective-address definitions in [M68000PRM](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf).

The preceding `1.4.2-ocs020.53` adds PC-relative brief-indexed `LEA` for the
020/EC020 Autoconfig boot path. The shared 030 executor receives the same operand
form; this does not enable another host CPU. Signed indexes, scale, PC base,
register aliasing and unchanged flags have focused coverage. Full index
extensions on this path remain unsupported; 040 keeps its existing fallback.
Instruction timing uses the existing bounded indexed-LEA policy.

Copper68k is a reusable C# Motorola 68000-family CPU emulation core extracted
from CopperScreen and CopperMod. It provides interpreter backends for MC68000,
MC68010, 68EC020, MC68020, MC68030, and MC68040-style execution behind a small
bus/core API, plus an opt-in MC68040 JIT backend for hosts that expose stable
code snapshots and write invalidation.

The package is intended for emulator projects that want to supply their own
memory map, devices, interrupt sources, and host integration.

## Install

```powershell
dotnet add package Copper68k
```

Copper68k currently targets `.NET 10`.

## Development version 1.4.2-ocs020.52

The advanced interpreter now distinguishes absolute and PC-relative memory
sources from immediate values for `MOVE.L <ea>,(An)`. The old decoder mask could
copy an address as instruction bytes, corrupting later execution. Its 30-case
regression includes 24 memory-source failures under the old mask and six passing
immediate controls. Additional memory moves, arithmetic, logical/bit operations,
shifts and PC-relative jumps cover the newly reached Lotus III continuation path.
Tests check width, flags, signed/scaled addresses and ordered side effects when
source and destination address registers alias.

The 020 profiles now permit Chip RAM instruction caching. A Chip RAM code write
retains the cached instruction until guest invalidation, as documented by
[Commodore's CacheClearE autodoc](https://www.theflatnet.de/pub/cbm/amiga/amigadev.elowar.com/read/ADCD_2.1/Includes_and_Autodocs_2._guide/node059D.html).
Custom/CIA instruction fetches remain uncached; 030/040 profile policies are
unchanged. Four enabled/disabled-cache regression cases cover both 020 variants.
A standalone Chip RAM self-modification ROM agrees with WinUAE's stale-code/flush
behavior. Cache-line fetch timing remains approximate and is not certified here.

This candidate extends the shared advanced interpreter for experimental OCS
68EC020/68020 integration. EC020 now advertises host code reads only when the
underlying bus supplies that optional capability. Added operand forms cover
MOVE, arithmetic/logical, condition-code, bit, MOVEM and rotate-through-extend
operations exercised by Kickstart 1.3 and Lotus III startup. Regression cases
cover operand width, flags, signed/scaled addressing, register aliasing, stack
updates and optional code-reader behavior.

The 68000 implementation is unchanged; new advanced forms retain the 68040's
existing fallback. The 68030 shares advanced execution code, but this work does
not establish native 68030 host support. Missing advanced forms still fail
explicitly. Operand-shape timing remains a bounded execution policy; it does not
certify a particular accelerator card or the sequence-dependent timings in
[MC68020UM section 8](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf).
This is an unpublished development version, not a stable release. Native
Kickstart boot and bounded Lotus III driving-demo/menu progression are covered;
complete game compatibility, player-controlled racing, FPU/MMU behavior and
physical timing conformance remain outside this slice.

The candidate passes 3,107 regular CPU tests, including 1,419 continuation cases
across 020, EC020 and the shared 030 executor. The earlier 184 expansion cases
and Chip RAM cache checks remain included. Six optional external conformance
tests were unavailable and skipped.

## Stable version 1.4.1

Copper68k 1.4.1 is the stable NuGet release. It retains the 68000 trace-exception
correction and prefetch-locality improvements from the `1.4.1-locality.1`
development build. It further reduces interpreter stack initialization in cached
fixed-batch execution and avoids deferred interrupt-sample resolution when no
deferred-timing provider is present. The public API and emulated bus ordering are
unchanged.

The combined candidate passed all 1,499 regular Copper68k tests and all three
retained CopperScreen performance workloads on the recorded Ryzen 5 5600X host.
The one-sided 95% upper regression bound is below 1% for each workload; native
Lemmings gains 2.76% paired FPS. Six optional external conformance suites were not
run because their external datasets or generators are opt-in. See the
[complete benchmark and validation record](https://github.com/ilehtoranta/CopperScreen/blob/main/docs/engine/CPU_BATCH_PREFETCH_2026-09-23.md)
for exact binaries, confidence bounds and coverage details.

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
- `M68010`: 68010-style core with VBR and a 24-bit external address bus.
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

### MC68000 fast-path coverage

The MC68000 interpreter retires instructions through one of several paths: a
cycle-accurate scalar decoder, a planned dispatch that skips decoding, and
cached fixed-plan runs that hoist the prefetch and bus-timing machinery out of
hot loops entirely. All paths are held to byte-identical retired cycle counts by
a differential test harness.

Register-direct forms of the following are admitted to the cached fixed-plan-run
path, so loops built from them stay on the fastest tier: `NOP`, `MOVEQ`, short
unconditional branches, `ADDQ`/`SUBQ`, long register `OR`/`AND`/`EOR`/`ADD`,
`CLR`/`NEG`/`NEGX`/`NOT`/`TST`, and shifts and rotates with an immediate count.
Shifts with a register-sourced count stay on the planned dispatch because their
cycle cost depends on register state.
