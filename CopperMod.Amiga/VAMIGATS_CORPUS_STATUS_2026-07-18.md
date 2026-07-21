# vAmigaTS Stratified Corpus Status — updated 2026-07-19

## Scope and configuration

This is a broader, subsystem-stratified vAmigaTS run, not yet the complete 1,929-ADF corpus.

- Selected directories: all VPOS, blitter `bbusy`, Copper WAIT, DDF, CIA TOD, attached sprites, and Paula basic interrupts.
- ADFs executed: **143**
- Raw-comparable cases: **112**
- Exact raw matches: **23**
- Raw mismatches: **89**
- Cases without a local raw reference: **31**
- Profile: A500 PAL OCS through `vanilla-kickstart13`
- ROM: `C:\D-drive\TestData\ROM\Kickstart_13.rom`
- Build: Release
- Hardware specialization: enabled
- Primary raw frame: reference `.retrosh` frame or frame 180
- Adjacent-frame probe: primary frame ±3
- Raw-offset scanning: disabled with `COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN=1`
- Timing/display compensation: none
- Runner: `scripts/run-vamigats.ps1 -Cases "Agnus/Registers/VPOS;Agnus/Blitter/bbusy;Agnus/Copper/Wait;Agnus/DDF/DDF;CIA/TOD/tod;Denise/Sprites/attached;Paula/Interrupts/basicint" -MaxFrames 180 -KickstartRom C:\D-drive\TestData\ROM\Kickstart_13.rom -CompareRaw -HardwareSpecialization`

A missing raw file is classified as **UNAVAILABLE**, not as an emulator failure. Every such ADF booted and continued with status `boot program running:`.

## Headline results

| Area | Comparable | Exact | Mismatch | Unavailable | Primary classification |
|---|---:|---:|---:|---:|---|
| VPOS | 30 | 10 | 20 | 0 | CPU-visible beam-register, IRQ, and color-write timing |
| Blitter BUSY | 17 | 0 | 17 | 0 | Agnus blitter completion/BUSY readback |
| Copper WAIT | 14 | 1 | 13 | 0 | `waitblt1` exact; remaining WAIT/blitter synchronization and narrow edge/result transitions |
| DDF | 38 | 11 | 27 | 4 | Core matrix and `farright1` exact; remaining hardware-stop and re-enable behavior |
| CIA TOD | 0 | 0 | 0 | 27 | No local raw oracle |
| Attached sprites | 6 | 1 | 5 | 0 | Coordinate fixed; remaining Denise attachment/composition behavior |
| Paula basic interrupts | 7 | 0 | 7 | 0 | INTREQ-to-IPL/handler-visible timing |
| **Total** | **112** | **23** | **89** | **31** |

Exact VPOS successes are `cycle01v`, `cycle01vh`, `cycleD9v`, `cycleD9vh`, `lof1`, `lof2`, `vhpos1`, `vhpos3`, `vhpos4`, and `vhpos5`. The four cycle cases pass together, and `cycleD9v` is exact across capture frames 450–456.

## Failure groups and first divergence

### 1. VPOS CPU/register boundary

The DBRA/prefetch-to-interrupt correction now makes `cycle01v`, `cycle01vh`, `cycleD9v`, and `cycleD9vh` exact. The committed target-extension transfer constrains ordinary retirement, while accepted exception entry retains a four-cycle internal tail after that transfer completes. The previous alternating `cycleD9v` sample expectation was a compensation artifact; the stable hardware invariant is `$8000` across IRQ/RTE.

The large `probe8`–`probe13` failures extend rendered probe output to row 188 where the reference stops near row 24. Those require a CPU custom-register-read and interrupt trace before assigning a production fix.

### 2. Agnus blitter BUSY

All 17 `bbusy` cases still fail at the first busy-result color transition, usually on raw row 6 at x=416 or x=480, but their mismatch counts are substantially lower. `bbusy0` is now 9,100 pixels from exact, down from 34,620. The likely boundary remains the physical transfer that makes blitter completion visible through BUSY/DMACONR. Capture the final blitter DMA slot, BUSY clear, CPU read grant, and following color write for `bbusy0` first.

### 3. Copper WAIT after raw capture quantization

The raw comparator now permits only a one-step quantization difference per RGB channel. This matches the local vAmiga capture palette, where a 4-bit channel may appear at `n × 16` or one lower. It leaves emulated COLOR registers and framebuffer colors unchanged and still rejects real palette differences.

After the color-packing and Copper/display corrections, `copwait1`–`copwait9` remain only 128–560 pixels from exact. `copwait5` is closest at 128, followed by `copwait9` at 240. `waitblt1` is now an exact 0/204060 match across adjacent frames. Its final four pixels were a Denise presentation issue: palette latency was lost when a Copper write occurred in the eight-CCK display-start preload window. Preserving the two-lowres-pixel Copper latency there also reduced `waitblt2` to 15,924 and `waitblt3` to 11,612.

The `waitblt4`/`waitblt5` transfer-start palette hypothesis was falsified by the four-case VPOS gate. The slot engine's recorded CPU grant is in its internal phase, and a CPU COLOR write reaches Denise six CCKs after that grant. Replacing this conversion with `grant - 2 CCK` advanced every CPU-driven color edge by eight CCKs, producing the exact `cycle01v` regression signature: 11,940 mismatches and a 32-pixel-early first stripe. The restored `grant + 6 CCK` conversion returns all four VPOS cases to exact output, with `cycle01v` and `cycleD9v` exact at every capture frame 450–456.

The regression guard is now a direct presentation-phase invariant: CPU COLOR writes use the six-CCK Denise-visible phase, while Copper COLOR writes and non-color CPU writes retain their recorded cycles. The DBRA interrupt ledger remains separate and authoritative: a committed target-extension transfer is preserved, followed by a four-cycle internal tail and a ten-cycle completion-to-first-stack-request gap.

Earlier `waitblt4`/`waitblt5` interrupt-aperture experiments were measured with the invalid palette conversion and are diagnostic only. On the restored VPOS-safe baseline, software INTREQ visibility and the short-branch IPL poll have now been separated from transfer completion. The deterministic results are 28/204060 for `waitblt4` and 24/204060 for `waitblt5`. Their first shared divergence is the level-1 color edge at row 214: expected x=224, actual x=240. The later level-3 clear is three CCKs early in `waitblt4` and two CCKs late in `waitblt5`. This opposite sign rejects a shared compensating delay; the next comparison must separate B-only completion/drain phase from exception and handler-prefetch timing while keeping the four-case VPOS gate green.

The short-BRA boundary has now been isolated further. Its target-opcode transfer is already physical when IRQ1 becomes visible, and the following target-extension transfer is the microcode operation that polls IPL. Cancelling that extension moved the shared handler edge one CCK later and broke scalar/batched parity, so it is not cancellable. Allowing exception-entry internal cycles to overlap the committed branch tail also worsened the shared edge. Both candidates were reverted. Separately, moving the IRQ1/BLTSIZE sequence by four CCKs made the later `waitblt4` clear exact while making the first handler edge worse. This proves the remaining handler-entry error and the B-only completion/WAIT ordering are independent timing contracts.

### 4. DDF fetch-window geometry

The repeated expected x=699 versus actual x=675 boundary was traced to one shared low-resolution origin rule, not DDFSTOP rounding. `GetDataFetchStartX` incorrectly retargeted the DDF stream when DIWSTRT moved left. On OCS lowres, DDF retains its physical phase independently of DIW: left overscan exposes the comparator `$80` pre-roll pixel, while a standard-or-later DIW clips it at comparator `$81`.

After separating DDF phase from DIW clipping, the remaining `ddf1` divergence mapped to Copper row `$A0`, exactly where the test switches to hires. The archived DMA timeline proved that Agnus fetched all requested words, but Denise clipped the first word because hires still derived its origin from DIW. Preserving the same physical pre-roll phase in hires moved the first pixel from raw x=32 to the reference x=46. Hires fetch counts are also completed to an even two-word fetch unit; this corrects `$38→$B4` from 33 to 34 words without changing the valid 40-word `$3C→$D4` stride.

The remaining `ddf3`/`ddf4` and `ddf7`/`ddf8` phase pairs exposed the corresponding lowres start rule. On OCS, H1 is ignored, while a DDFSTRT match in the second half of an eight-CCK fetch block advances the physical first slot to the next block. It does not shorten the fetch-unit span when DDFSTOP is in the first half; when both start and stop are in their second halves, they refer to the same boundary and must not double-extend it. Encoding that two-bit phase matrix makes all `ddf1` through `ddf10` exact 0/204060 matches.

The final 15 `farright1` pixels were not a global hard-stop cap. The first divergent event was the post-`$D8` bitplane RGA request at v144 h223 coinciding with the Copper's second-word request. Quiet lines retain the late latched word, but the following `BPLCON0` plane-disable commits that collision as empty. Preserving the collision candidate until the disable transition makes `farright1` exact without shortening ordinary `$DE` lines.

### 5. Denise attached sprites

The common +2 raw-pixel displacement was a single coordinate conversion error: OCS comparator `$81`, not `$80`, aligns with the standard visible left edge. Correcting that conversion makes `attached5` an exact 0/204060 match across frames 447–453 and aligns its bounds at x=174–615. The other five cases now expose separate attachment/composition behavior rather than a global position error. In particular, `attached3`, `attached4`, and `attached6` have aligned outer bounds but select or combine the wrong attached-sprite colors; `attached1` still lacks only the leftmost two raw pixels on some rows.

### 6. Paula basic interrupt timing

All seven cases boot and draw the expected geometry, but IRQ-driven color boundaries differ at x=96, x=288, or x=480. The physical event chain to trace is Paula INTREQ assertion → IPL visibility → CPU interrupt acceptance/IACK → handler prefetch → first result `COLOR00` write. Do not treat these as a global CPU delay until that first divergent transfer is identified.

### 7. CIA TOD coverage unavailable

All 27 selected CIA TOD ADFs booted, but no matching `.raw`, `_OCS.raw`, or `_ECS.raw` files exist locally. Their raw-comparison failures are harness/oracle availability failures, not CIA correctness evidence.

## Recommended investigation order

1. Preserve the four-case cycle timing gate and `cycleD9v` frame 450–456 determinism check.
2. Compare the B-only final transfer, internal drain, BUSY clear/INTREQ, and level-3 CPU boundary in `waitblt4`/`waitblt5`; also resolve the shared four-CCK-late level-1 handler color edge.
3. Investigate DDF `single3` (384 pixels) and `reenable1` (1,028) using the exact `ddf1`–`ddf10` and `farright1` boundaries.
4. Trace `bbusy0` from the final blitter DMA slot through BUSY readback and first color write.
5. Trace `basicint1` through INTREQ, IPL, IACK, handler entry, and first color write.
6. Trace the remaining attached-sprite pair data selection and priority rules; the global coordinate error is fixed.
7. Add non-raw semantic CIA TOD oracles before judging those 27 cases.

## Per-case status

| Case | Status | Raw evidence | Classification |
|---|---|---|---|
| `Agnus/Registers/VPOS/cycle01v/cycle01v.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/cycle01vh/cycle01vh.adf` | PASS | 0/204060 | Exact after preserving the recognized DBRA decrement while draining its committed exception-entry bus tail. |
| `Agnus/Registers/VPOS/cycleD9v/cycleD9v.adf` | PASS | 0/204060 | Exact with the four-cycle DBRA exception tail; deterministic across capture frames 450–456. |
| `Agnus/Registers/VPOS/cycleD9vh/cycleD9vh.adf` | PASS | 0/204060 | Exact with the committed DBRA exception tail; deterministic across adjacent frames. |
| `Agnus/Registers/VPOS/ersy1/ersy1.adf` | FAIL | 14050/204060; first (288,6) #F000EF→#60005F | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/ersy2/ersy2.adf` | FAIL | 14610/204060; first (62,18) #F0F0F0→#000000 | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/lof1/lof1.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/lof2/lof2.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/probe1/probe1.adf` | FAIL | 16564/204060; first (480,6) #60005F→#F000EF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe10/probe10.adf` | FAIL | 113380/204060; first (480,6) #60005F→#F000EF | Probe output continues far beyond reference bounds; VPOS/VHPOSR sampling or IRQ/loop termination. |
| `Agnus/Registers/VPOS/probe11/probe11.adf` | FAIL | 111348/204060; first (480,6) #60005F→#F000EF | Probe output continues far beyond reference bounds; VPOS/VHPOSR sampling or IRQ/loop termination. |
| `Agnus/Registers/VPOS/probe12/probe12.adf` | FAIL | 111360/204060; first (480,6) #60005F→#F000EF | Probe output continues far beyond reference bounds; VPOS/VHPOSR sampling or IRQ/loop termination. |
| `Agnus/Registers/VPOS/probe13/probe13.adf` | FAIL | 111360/204060; first (480,6) #60005F→#F000EF | Probe output continues far beyond reference bounds; VPOS/VHPOSR sampling or IRQ/loop termination. |
| `Agnus/Registers/VPOS/probe2/probe2.adf` | FAIL | 13614/204060; first (288,6) #F000EF→#60005F | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe3/probe3.adf` | FAIL | 14130/204060; first (608,6) #F000EF→#60005F | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe4/probe4.adf` | FAIL | 15526/204060; first (480,6) #60005F→#F000EF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe5/probe5.adf` | FAIL | 10726/204060; first (288,6) #F000EF→#60005F | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe6/probe6.adf` | FAIL | 14134/204060; first (480,6) #60005F→#F000EF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe7/probe7.adf` | FAIL | 13998/204060; first (480,6) #60005F→#F000EF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/probe8/probe8.adf` | FAIL | 111410/204060; first (480,6) #60005F→#F000EF | Probe output continues far beyond reference bounds; VPOS/VHPOSR sampling or IRQ/loop termination. |
| `Agnus/Registers/VPOS/probe9/probe9.adf` | FAIL | 111408/204060; first (480,6) #60005F→#F000EF | Probe output continues far beyond reference bounds; VPOS/VHPOSR sampling or IRQ/loop termination. |
| `Agnus/Registers/VPOS/vhpos1/vhpos1.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/vhpos2/vhpos2.adf` | FAIL | 10104/204060; first (676,134) #303030→#C0C0C0 | IRQ/VHPOSR-derived result bit differs at the first result transition. |
| `Agnus/Registers/VPOS/vhpos3/vhpos3.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/vhpos4/vhpos4.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/vhpos5/vhpos5.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/Registers/VPOS/vprobe1/vprobe1.adf` | FAIL | 8850/204060; first (32,32) #0000EF→#0000DF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/vprobe2/vprobe2.adf` | FAIL | 9302/204060; first (288,6) #F000EF→#60005F | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/vprobe3/vprobe3.adf` | FAIL | 12208/204060; first (480,6) #60005F→#F000EF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Registers/VPOS/vprobe4/vprobe4.adf` | FAIL | 12224/204060; first (480,6) #60005F→#F000EF | Stable beam-register/CPU-visible color transition mismatch; trace CPU read/write boundary next. |
| `Agnus/Blitter/bbusy/bbusy0/bbusy0.adf` | FAIL | 9100/204060; first (416,6) #60005F→#F000EF | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy1/bbusy1.adf` | FAIL | 8244/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy11l/bbusy11l.adf` | FAIL | 8276/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy13f/bbusy13f.adf` | FAIL | 11036/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy13l/bbusy13l.adf` | FAIL | 8888/204060; first (416,6) #60005F→#F000EF | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy15/bbusy15.adf` | FAIL | 18944/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy1f/bbusy1f.adf` | FAIL | 10668/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy2/bbusy2.adf` | FAIL | 9476/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy3/bbusy3.adf` | FAIL | 10680/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy4/bbusy4.adf` | FAIL | 9096/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy5/bbusy5.adf` | FAIL | 10680/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy5f/bbusy5f.adf` | FAIL | 10528/204060; first (416,6) #60005F→#F000EF | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy6/bbusy6.adf` | FAIL | 13176/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy7/bbusy7.adf` | FAIL | 10824/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy7l/bbusy7l.adf` | FAIL | 7856/204060; first (416,6) #60005F→#F000EF | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy9f/bbusy9f.adf` | FAIL | 11424/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Blitter/bbusy/bbusy9l/bbusy9l.adf` | FAIL | 9076/204060; first (480,6) #F000EF→#60005F | First busy-result color transition differs; Agnus blitter BUSY completion/readback timing. |
| `Agnus/Copper/Wait/copwait1/copwait1.adf` | FAIL | 456/204060; first (712,22) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait2/copwait2.adf` | FAIL | 472/204060; first (712,22) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait3/copwait3.adf` | FAIL | 376/204060; first (68,72) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait4/copwait4.adf` | FAIL | 436/204060; first (84,72) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait5/copwait5.adf` | FAIL | 128/204060; first (712,22) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait6/copwait6.adf` | FAIL | 560/204060; first (712,22) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait7/copwait7.adf` | FAIL | 464/204060; first (0,72) #00F0EF→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait8/copwait8.adf` | FAIL | 544/204060; first (704,72) #EFF000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/copwait9/copwait9.adf` | FAIL | 240/204060; first (68,72) #000000→#F00000 | Copper WAIT/result layout after capture quantization was removed. |
| `Agnus/Copper/Wait/waitblt1/waitblt1.adf` | PASS | 0/204060; exact across adjacent frames | Denise preserves palette latency for Copper writes in the display-start preload window. |
| `Agnus/Copper/Wait/waitblt2/waitblt2.adf` | FAIL | 15924/204060; first (228,22) #3F3FEF→#0000EF | Copper WAIT-for-blitter/result geometry; improved after preload-window correction. |
| `Agnus/Copper/Wait/waitblt3/waitblt3.adf` | FAIL | 11612/204060; first (0,22) #000000→#F000EF | Copper WAIT-for-blitter/result transition differs; improved after preload-window correction. |
| `Agnus/Copper/Wait/waitblt4/waitblt4.adf` | FAIL | 28/204060; first (224,214) #807FEF→#0000EF | Shared level-1 edge is four CCKs late (x240); level-3 clear is three CCKs early (expected x188, actual x176). |
| `Agnus/Copper/Wait/waitblt5/waitblt5.adf` | FAIL | 24/204060; first (224,214) #807FEF→#0000EF | Shared level-1 edge is four CCKs late (x240); level-3 clear is two CCKs late (expected x660, actual x668). |
| `Agnus/DDF/DDF/arosddf1/arosddf1.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `Agnus/DDF/DDF/arosddf2/arosddf2.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `Agnus/DDF/DDF/arosddf3/arosddf3.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `Agnus/DDF/DDF/arosddf4/arosddf4.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `Agnus/DDF/DDF/ddf1/ddf1.adf` | PASS | 0/204060 | Exact after preserving hires physical phase and completing two-word fetch units. |
| `Agnus/DDF/DDF/ddf10/ddf10.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/DDF/DDF/ddf2/ddf2.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/DDF/DDF/ddf3/ddf3.adf` | PASS | 0/204060 | Exact after second-half lowres DDFSTRT phase correction. |
| `Agnus/DDF/DDF/ddf4/ddf4.adf` | PASS | 0/204060 | Exact; OCS ignores DDFSTRT H1. |
| `Agnus/DDF/DDF/ddf5/ddf5.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/DDF/DDF/ddf6/ddf6.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/DDF/DDF/ddf7/ddf7.adf` | PASS | 0/204060 | Exact after second-half lowres DDFSTRT phase correction. |
| `Agnus/DDF/DDF/ddf8/ddf8.adf` | PASS | 0/204060 | Exact; OCS ignores DDFSTRT H1. |
| `Agnus/DDF/DDF/ddf9/ddf9.adf` | PASS | 0/204060 | Exact raw match. |
| `Agnus/DDF/DDF/dmaslots/dmaslots.adf` | FAIL | 7040/204060; first (288,6) #60005F→#F000EF | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/doublematch1/doublematch1.adf` | FAIL | 3328/204060; first (190,41) #3F3FEF→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/farright1/farright1.adf` | PASS | 0/204060 | Exact after preserving the late RGA collision until the following bitplane-disable transition. |
| `Agnus/DDF/DDF/farright3/farright3.adf` | FAIL | 29022/204060; first (126,107) #000000→#F060AF | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop1/hwstop1.adf` | FAIL | 9688/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop2/hwstop2.adf` | FAIL | 18192/204060; first (542,22) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop3/hwstop3.adf` | FAIL | 12266/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop4/hwstop4.adf` | FAIL | 19614/204060; first (542,22) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop5/hwstop5.adf` | FAIL | 19614/204060; first (542,22) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop6/hwstop6.adf` | FAIL | 17952/204060; first (542,22) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop7/hwstop7.adf` | FAIL | 13654/204060; first (2,22) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/hwstop8/hwstop8.adf` | FAIL | 10833/204060; first (676,22) #000000→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/lupo1/lupo1.adf` | FAIL | 68309/204060; first (46,22) #F000EF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/oldhwstop1/oldhwstop1.adf` | FAIL | 105838/204060; first (672,22) #F00000→#50602F | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/oldhwstop2/oldhwstop2.adf` | FAIL | 102846/204060; first (672,22) #F00000→#50708F | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/oldhwstop3/oldhwstop3.adf` | FAIL | 120570/204060; first (672,22) #F00000→#50602F | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/oldhwstop4/oldhwstop4.adf` | FAIL | 106790/204060; first (672,22) #F00000→#50708F | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/reenable1/reenable1.adf` | FAIL | 1028/204060; first (478,22) #F00000→#6060EF | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/shift1/shift1.adf` | FAIL | 30347/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/shift2/shift2.adf` | FAIL | 31289/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/shift3/shift3.adf` | FAIL | 31335/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/shift4/shift4.adf` | FAIL | 34279/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/shift5/shift5.adf` | FAIL | 34279/204060; first (672,22) #F00000→#000000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/single1/single1.adf` | FAIL | 31444/204060; first (2,28) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/single2/single2.adf` | FAIL | 12924/204060; first (350,28) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/single3/single3.adf` | FAIL | 384/204060; first (2,40) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/single4/single4.adf` | FAIL | 32370/204060; first (2,28) #605FEF→#F00000 | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `Agnus/DDF/DDF/single5/single5.adf` | FAIL | 16143/204060; first (34,28) #F00000→#6060EF | First display-fetch/result transition differs; Agnus DDF start/stop and Denise fetch-window geometry. |
| `CIA/TOD/tod/latch1/latch1.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch2/latch2.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch3/latch3.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch4/latch4.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch5/latch5.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch6/latch6.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch7/latch7.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch8/latch8.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/latch9/latch9.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/tod1/tod1.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/tod2/tod2.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/tod3/tod3.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/tod4/tod4.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todbug1/todbug1.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todbug2/todbug2.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todbug3/todbug3.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todbug4/todbug4.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/toddelay1/toddelay1.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/toddelay2/toddelay2.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/toddelay3/toddelay3.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todint1/todint1.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todint2/todint2.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todint3/todint3.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todint4/todint4.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todpulse1a/todpulse1a.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todpulse1b/todpulse1b.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `CIA/TOD/tod/todpulse2b/todpulse2b.adf` | UNAVAILABLE | raw oracle missing | No local raw reference; boot/run completed. |
| `Denise/Sprites/attached/attached1/attached1.adf` | FAIL | 2628/204060; first (62,25) #301F20→#000000 | Global right edge is aligned; some rows omit the leftmost low-resolution sprite pixel. |
| `Denise/Sprites/attached/attached2/attached2.adf` | FAIL | 352/204060; first (72,19) #000000→#30201F | Small vertical/activation-boundary difference; no longer a global horizontal displacement. |
| `Denise/Sprites/attached/attached3/attached3.adf` | FAIL | 1068/204060; first (202,19) #E0A020→#101010 | Outer bounds align; attached pair data/color selection differs. |
| `Denise/Sprites/attached/attached4/attached4.adf` | FAIL | 2232/204060; first (202,19) #E0A020→#101010 | Outer bounds align; attached pair data/color selection differs. |
| `Denise/Sprites/attached/attached5/attached5.adf` | PASS | 0/204060 | Exact after correcting `$81` sprite comparator origin; deterministic across frames 447–453. |
| `Denise/Sprites/attached/attached6/attached6.adf` | FAIL | 6640/204060; first (202,20) #F000EF→#00F000 | Outer bounds align; attached four-bit color composition differs. |
| `Paula/Interrupts/basicint/basicint1/basicint1.adf` | FAIL | 5664/204060; first (96,22) #F000EF→#60005F | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
| `Paula/Interrupts/basicint/basicint2/basicint2.adf` | FAIL | 5520/204060; first (96,22) #F000EF→#60005F | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
| `Paula/Interrupts/basicint/basicint3/basicint3.adf` | FAIL | 7120/204060; first (96,22) #F000EF→#60005F | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
| `Paula/Interrupts/basicint/basicint4/basicint4.adf` | FAIL | 42364/204060; first (480,22) #F000EF→#60005F | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
| `Paula/Interrupts/basicint/basicint5/basicint5.adf` | FAIL | 3060/204060; first (288,22) #60005F→#F000EF | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
| `Paula/Interrupts/basicint/basicint6/basicint6.adf` | FAIL | 7624/204060; first (288,22) #60005F→#F000EF | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
| `Paula/Interrupts/basicint/basicint7/basicint7.adf` | FAIL | 6136/204060; first (288,22) #60005F→#F000EF | IRQ-driven result color transition differs; Paula INTREQ visibility, CPU interrupt acceptance, or handler bus timing. |
