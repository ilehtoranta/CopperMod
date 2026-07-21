# Vertical-position CPU timing gate

The `cycle01v`, `cycle01vh`, `cycleD9v`, and `cycleD9vh` images form one timing
gate. A CPU timing change is accepted only when all four raw images match
exactly with hardware specialization enabled and raw-offset scanning disabled.

Run the gate from the repository root:

```powershell
.\scripts\run-cycle-vpos-timing-gate.ps1 `
  -CorpusRoot .\third_party\vAmigaTS `
  -KickstartRom C:\D-drive\TestData\ROM\Kickstart_13.rom
```

The gate also checks `cycleD9v` capture frames 450 through 456 independently.
This detects frame-to-frame phase oscillation that a single exact image could
miss.

The focused CPU contract is:

- A taken DBRA retains its committed target opcode and extension transfers.
- Normal retirement waits for the extension transfer's frozen completion.
- An accepted interrupt waits four additional CPU cycles before exception
  stacking; two internal cycles overlap the committed transfer.
- IRQ/RTE must preserve the sampled `cycleD9v` value `$8000` across frames.
- Interpreter, hardware-specialized fallback, and exact-JIT paths must transfer
  the same queue, bus-tail, and interrupt-boundary state.

Run the focused regression set before the raw gate:

```powershell
dotnet test .\CopperMod.Amiga.Tests\CopperMod.Amiga.Tests.csproj `
  --filter "Prefetch|DBRA|Dbcc|BusPhase|VerticalSyncLoop|CycleD9v|Cycle01v"
```
