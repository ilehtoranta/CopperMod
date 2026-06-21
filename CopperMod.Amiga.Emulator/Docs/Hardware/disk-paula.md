# Disk And Paula DMA Behavior

## Scope

This page is the landing point for the disk/IPF fidelity pass. The target remains
A500 PAL OCS Paula disk DMA behavior, not title-specific loader patches.

Relevant areas:

- `DSKLEN` start/stop semantics.
- `DSKBYTR` and `DSKDATR` readback timing.
- `ADKCON` and WORDSYNC behavior.
- Index pulse and track phase.
- MFM gap, sync, weak-data, and variable-length track handling.
- ADF synthetic track behavior.
- IPF/SCP decoder orientation, flux/bitcell timing, and protection metadata.
- Drive select, motor, side, step, and ready behavior through CIA ports.

## Current Policy

Disk traces are evidence, not fixes by themselves. If a title stalls, capture:

- Frame and cycle.
- PC and interrupt state.
- `DMACON`, `INTENA`, `INTREQ`, `ADKCON`, `DSKLEN`, `DSKBYTR`.
- Selected drive, side, cylinder, motor, ready state.
- Last transfer address, cylinder/head, word count, and trace kind.
- CIA port state that controls drive selection and stepping.

Then reduce the issue to a synthetic disk-controller test where possible.

## Known Regression Signals

These titles are useful smoke tests for disk and display interaction:

- Shadow of the Beast.
- Major Motion.
- Operation Thunderbolt.
- Superfrog.

Do not make title-specific disk behavior. Use these titles to decide which
hardware trace to reduce next.

## Implemented Fidelity Topics

| Topic | Status | Notes |
| --- | --- | --- |
| Standard ADF synthetic revolution | Implemented | Preserved at 6334 MFM words / 12668 bytes. |
| IPF stored gap streams | Implemented | `GapOffset`, `GapBits`, and `Gap0/Gap1` stream elements are decoded; malformed offsets and stream endings are rejected. |
| Weak-data metadata | Implemented | Weak stream elements use deterministic materialization and produce `AmigaTrackRegion` spans with `WeakData` and `ApproximateWeakData`. |
| SCP read-only flux loading | Implemented | Normal floppy SCP headers, selected heads, 1-5 revolutions, 16-bit flux entries, checksum, ZIP wrapping, overflow/no-flux regions, and Amiga DD defaults are covered. |
| Track-region propagation | Implemented | CopperDisk region/features map through the emulator adapter into the core encoded-track model. |
| Bus-slot disk DMA completion | Implemented | Recovered disk words wait for legal Agnus disk DMA slots before chip RAM transfer and DSKBLK completion. |
| Non-WORDSYNC mid-word arming | Implemented | Read DMA drains the continuous recovered 16-bit phase; WORDSYNC remains the explicit realignment path. |
| `DSKBYTR`/`DSKDATR` readback timing | Implemented | Passive byte/word recovery remains observable independently from delayed DMA bus grants. |
| Per-drive index phase | Implemented | CIA-B index pulses are scheduled from each motor-on drive's own phase instead of one shared global timer. |

## Pending Fidelity Topics

| Topic | Status | Notes |
| --- | --- | --- |
| Exact disk write splice behavior | Pending | Needs hardware proof for last-bit and splice-edge corruption. |
| Paula write precompensation analog effects | Pending | Register bits are tracked, but analog write-shape behavior is out of scope. |
| IPF/SCP weak-data randomness policy | Pending | Regions are preserved; dynamic random/noise materialization should stay deterministic until tests specify otherwise. |
| Flux-capture index-duration metadata | Pending | Current core tracks preserve approximate-index/no-flux regions but do not expose a separate physical revolution duration. |
| Non-Amiga/HD SCP decoding | Pending | Rejected by default for the A500 DD target. |

## Tests

Use focused disk filters before title smoke tests:

```powershell
dotnet test ..\CopperMod.Amiga.Tests\CopperMod.Amiga.Tests.csproj --filter "Disk|Paula|CIA|Input"
dotnet test ..\CopperScreen.Tests\CopperScreen.Tests.csproj --filter "Shadow|MajorMotion|OperationThunderbolt|Superfrog|FullContact"
```
