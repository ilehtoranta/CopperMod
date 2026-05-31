# Undocumented OCS Chipset Features

Date: 2026-05-31

Scope: A500 PAL OCS behavior collected from the local EAB thread archive in
`CopperMod.Amiga/References/Undocumented features/`. The local archive contains
19 pages and 369 posts with no missing pages or post numbers.

These items are not official Commodore Hardware Reference Manual behavior. Treat
them as thread-derived compatibility notes and keep all implementation inside
`CopperMod.Amiga`; do not add a dependency from `CopperMod.Amiga` to the
CopperMod player or `CopperMod.Cust`.

## Implemented OCS Targets

| Feature | Source | Chipset scope | Confidence | HRM status | Implementation status | Test status |
| --- | --- | --- | --- | --- | --- | --- |
| Copper `SKIP` suppresses only the next `MOVE` write; following `WAIT`/`SKIP` still executes. | page 1, post 1 | OCS | High | Undocumented | Implemented | Executable Copper conformance rows |
| Copper dangerous-register `MOVE` with `COPCON` danger disabled stops Copper until next vblank, including suppressed `MOVE`. | page 1, post 1 | OCS | High | Undocumented | Implemented | Executable Copper conformance rows |
| Blitter line mode requires C channel for drawing; D enable does not control drawing or speed. | page 1, posts 6, 12 | OCS | High | Undocumented | Implemented | Executable Blitter conformance rows |
| Blitter line mode uses `BLTCMOD` for both C and D stepping; `BLTDMOD` is ignored. | page 1, post 6 | OCS | High | Undocumented | Implemented | Executable Blitter conformance rows |
| Blitter line mode writes first pixel through `BLTDPT`, following pixels through `BLTCPT`, and ends with `BLTDPT == BLTCPT`. | page 1, posts 6, 12; page 9, post 161 | OCS | High | Undocumented | Implemented | Executable Blitter conformance rows |
| `BLTALWM` is unused in line mode. | page 1, post 14 | OCS | Medium | Contradicts HRM line-mode wording | Implemented | Executable Blitter conformance row |
| Blitter hidden old-BDAT value is cleared at blit start only when B DMA is enabled. | page 12, posts 225, 229 | OCS | High | Undocumented | Implemented | Executable Blitter conformance row |
| Blitter B-channel line-pattern reload reads B twice per line pixel from the same address, then applies `BLTBMOD`. | page 13, post 254 | OCS | High | Undocumented | Implemented | Executable Blitter conformance row |
| `BPLCON2` priority values 5-7 trigger the SWIV-style normal/EHB and dual-playfield behavior. | page 1, post 5; page 10, post 184 | OCS/ECS, not AGA | High | Undocumented | Implemented for A500 PAL OCS | Executable Bitplane conformance rows |
| `CLXDAT` bit 15 is always set. | page 1, post 10 | OCS | High | HRM marks bit unused | Implemented | Executable custom-register row |
| Enabling bitplane DMA during the active DDF window does not restart OCS fetches until the next scanline. | page 1, post 13 | OCS | High | Undocumented | Implemented for A500 PAL OCS timing | Executable Bitplane conformance row |

## Documented And Deferred

| Feature | Source | Chipset scope | Confidence | HRM status | Implementation status | Test status |
| --- | --- | --- | --- | --- | --- | --- |
| COPJMP extra cycles, wait wakeup quirks, and the later 3-stage Copper model. | pages 6-8, 19; posts 118, 122, 156, 364, 366 | OCS/ECS/AGA details vary | High but complex | Undocumented | Deferred | Pending Copper rows |
| CIA TICK/TOD alarm interrupt debounce of roughly 14-16 E-clock cycles. | page 8, post 142 | OCS-era CIA | Medium | Undocumented | Deferred until exact phase behavior is modeled | Pending CIA row |
| FMODE CPU-write issue. | page 8, posts 144-150 | AGA | High false alarm | Not a hardware bug | Not implemented | Documented false alarm |
| FMODE SSCAN2 sprite duplication. | page 1, post 15; page 8, posts 151-155 | AGA/CD32-oriented, documented but easy to miss | High | Documented but misunderstood | Out of OCS scope | Not tested here |
| BBUSY/Copper-heavy-DMA reports without a binary repro. | page 8, posts 157-160 | Reported AGA-heavy DMA | Low | Unverified | Unactionable pending repro | Pending note only |
| BPLxDAT Denise latch behavior, sprites outside bitplane area via early `BPLxDAT`, 7-plane mode, HAM+dual-playfield interaction, sprite/border timing, and too-early `BPL1DAT` limits. | pages 1, 2, 4, 10, 11, 13, 14, 18 | OCS/ECS details vary | Medium to high | Undocumented | Deferred until a latch-level Denise model exists | Pending Bitplane/Sprite rows |
| DDFSTRT sprite-slot stealing, DDFSTRT `< $18` every-other-line blanking, DMA conflict corruption, refresh pointer conflicts, and VHPOSW/strobe/blanking behavior. | pages 2, 9, 14-18 | OCS/ECS details vary | Medium to high | Undocumented | Deferred until a fuller Agnus/Denise bus-state model exists | Pending rows |
| Paula delayed audio `INTREQ2`, disk WORDSYNC write/read quirks, CIA TOD/timer edge bugs, serial quirks, read-strobe behavior, and bus noise. | pages 6, 7, 16-18 | OCS-era peripherals | Medium to high | Undocumented | Deferred to peripheral-specific passes | Pending Paula/Disk/CIA notes |

## Compatibility Rule

Run `CopperMod.Amiga.Tests` and `CopperMod.Cust.Tests` after changes. The
existing architecture tests must continue to prove that `CopperMod.Amiga` does
not reference `CopperMod`, `CopperMod.Abstractions`, or `CopperMod.Cust`.
