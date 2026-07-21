# vAmigaTS Stratified Corpus Status — post-DMA-refactor baseline, 2026-07-20

## Scope and configuration

This run supersedes the July 19 stratified status for the current working tree after the extensive DMA refactor.

- Selected directories: all VPOS, blitter `bbusy`, Copper WAIT, DDF, CIA TOD, attached sprites, and Paula basic interrupts.
- ADFs executed: **143**
- Raw-comparable cases: **112**
- Exact raw matches: **7**
- Raw mismatches: **105**
- Cases without a local raw reference: **31**
- Profile: A500 PAL OCS through `vanilla-kickstart13`
- ROM: `C:\D-drive\TestData\ROM\Kickstart_13.rom`
- Build: Release
- Hardware specialization: enabled
- Maximum requested frame depth: 180; reference capture frames and adjacent-frame probes extend applicable cases to frame 453
- Raw-offset scanning: disabled with `COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN=1`
- Timing/display compensation: none
- Per-case timeout: 120 seconds; **no timeouts occurred**
- Runtime: 12 minutes 54 seconds

Runner command:

```powershell
$env:COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN = "1"
.\scripts\run-vamigats.ps1 -Cases "Agnus/Registers/VPOS;Agnus/Blitter/bbusy;Agnus/Copper/Wait;Agnus/DDF/DDF;CIA/TOD/tod;Denise/Sprites/attached;Paula/Interrupts/basicint" -MaxFrames 180 -KickstartRom C:\D-drive\TestData\ROM\Kickstart_13.rom -CompareRaw -HardwareSpecialization -CaseTimeoutSeconds 120
```

Machine-readable results:

- `TestResults/vamigats-stratified-2026-07-20-after-dma-refactor-results.jsonl`
- `TestResults/vamigats-stratified-2026-07-20-after-dma-refactor-progress.log`

## Unit and conformance test status

- The complete `CopperMod.Amiga.Tests` project was attempted after the corpus run. It exceeded a 15-minute command window without producing its buffered summary. The test host remained responsive and CPU-active until the outer timeout, so this run is **incomplete**, not a reported test failure.
- A narrower set containing `AmigaBitplaneConformanceMatrixTests`, `AmigaBlitterConformanceMatrixTests`, `AmigaCopperConformanceMatrixTests`, `AmigaSpriteConformanceMatrixTests`, and `AmigaBusTimingTests` was then attempted. It likewise remained responsive and CPU-active but exceeded a 10-minute command window without a summary. This run is also **incomplete**.
- The orphaned test hosts created by the outer command timeouts were stopped explicitly.
- The most recent completed CPU-focused run passed 59/59 tests.
- The most recent completed `M68kInterpreterTests` run passed 121 tests, failed 0, and skipped 9 archived diagnostic probes.

The unit-test runner therefore needs the same sharding/progress treatment now used by the corpus. Until that is implemented, the post-DMA-refactor full-project unit-test status must not be described as green or red.

## Headline results

| Area | Comparable | Exact | Mismatch | Unavailable | Current signal |
|---|---:|---:|---:|---:|---|
| VPOS | 30 | 6 | 24 | 0 | Four formerly exact cycle cases now diverge near rows 185–186. |
| Blitter BUSY | 17 | 0 | 17 | 0 | No exact cases, but many mismatch counts improved. |
| Copper WAIT | 14 | 0 | 14 | 0 | `waitblt1` regressed; `waitblt2` improved greatly; `copwait3`, `copwait4`, and `copwait9` worsened greatly. |
| DDF | 38 | 0 | 38 | 4 | All eleven formerly exact cases regressed at the common far-right tail. Several shift/hardware-stop cases improved greatly. |
| CIA TOD | 0 | 0 | 0 | 27 | All cases booted; no local raw oracle. |
| Attached sprites | 6 | 1 | 5 | 0 | `attached5` remains exact; other results are effectively unchanged. |
| Paula basic interrupts | 7 | 0 | 7 | 0 | Several cases improved, but none are exact. |
| **Total** | **112** | **7** | **105** | **31** | Exact count changed from 23 to 7. |

The exact cases are:

- `lof1`, `lof2`
- `vhpos1`, `vhpos3`, `vhpos4`, `vhpos5`
- `attached5`

## Regressions from the July 19 baseline

Sixteen formerly exact raw comparisons now fail.

| Case group | New mismatch | First divergence |
|---|---:|---|
| `cycle01v`, `cycle01vh` | 4,396 each | `(24,186)`, expected black, actual magenta |
| `cycleD9v` | 3,556 | `(688,185)`, expected black, actual magenta |
| `cycleD9vh` | 252 | `(20,186)`, expected black, actual magenta |
| `waitblt1` | 88 | `(4,152)`, expected black, actual blue |
| `ddf1`, `ddf2`, `ddf5`, `ddf6`, `ddf9`, `ddf10` | 51 each | `(678,106)`, expected fetched color, actual black |
| `ddf3`, `ddf4`, `ddf7`, `ddf8` | 60 each | `(678,94)`, expected fetched color, actual black |
| `farright1` | 228 | `(678,58)`, expected fetched color, actual black |

The DDF regressions share the same x=678 far-right boundary and small 51/60-pixel signatures. This strongly suggests one shared post-refactor fetch-tail, line-tail, or presentation-tail rule rather than eleven independent geometry errors.

The four cycle cases now diverge much later in the image than the original DBRA/prefetch failure. Their common row 185/186 signature should be investigated first as a DMA/presentation-state regression; it is not evidence by itself that the CPU prefetch fix is wrong.

`waitblt1` has a separate narrow row-152 left-edge signature. Keep it separate from the shared DDF tail until a physical transfer trace proves a common cause.

## Significant improvements that remain non-exact

The DMA refactor also corrected or greatly reduced several broad failures:

| Case | Previous mismatch | Current mismatch | Change |
|---|---:|---:|---:|
| `shift1` | 30,347 | 1,241 | -29,106 |
| `shift2` | 31,289 | 609 | -30,680 |
| `shift3` | 31,335 | 1,484 | -29,851 |
| `shift4` | 34,279 | 1,484 | -32,795 |
| `shift5` | 34,279 | 1,484 | -32,795 |
| `lupo1` | 68,309 | 35,508 | -32,801 |
| `waitblt2` | 15,924 | 216 | -15,708 |
| `basicint4` | 42,364 | 30,952 | -11,412 |
| `vhpos2` | 10,104 | 5,052 | -5,052 |

Several blitter BUSY cases also improved by thousands of pixels, but the first busy-result transition still differs and no `bbusy` case is exact.

## Large new non-exact regressions

Three Copper WAIT cases changed from narrow edge mismatches to broad result differences:

| Case | Previous mismatch | Current mismatch | Change |
|---|---:|---:|---:|
| `copwait3` | 376 | 24,176 | +23,800 |
| `copwait4` | 436 | 40,584 | +40,148 |
| `copwait9` | 240 | 24,040 | +23,800 |

These should be grouped as a shared Copper/DMA result regression unless their first divergent physical transfers differ.

## Recommended investigation order

1. Restore the shared DDF far-right tail while keeping the large `shift1`–`shift5` improvements.
2. Trace the row-185/186 transition in the four cycle cases and identify the first divergent DMA or presentation event.
3. Compare `copwait3`, `copwait4`, and `copwait9` at their first divergent Copper fetch/grant/MOVE application.
4. Recheck `waitblt1` independently at row 152.
5. Preserve `attached5`, the six exact VPOS cases, and the large shift/waitblt2 improvements as regression gates.

The acceptance rule remains physical-timeline correctness: no offset scanning, global delay, crop, or case-specific compensation.
