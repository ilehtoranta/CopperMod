# SID timing and fidelity correction contract

Implemented against the 2026-09-26 audit of commit `8fb826eacf8e9dc32948b482c878079448bd6e61`. This contract describes the software convention; it is not a claim of measured physical equivalence.

## Clock and bus convention

The fixture identifier is `cpu-bus-end-write-next-clock-v1`.

1. An access at cycle N observes the chip after N complete SID clocks. A write at N drives the CPU data bus immediately and forwards to the digital registers at the start of clock N+1.
2. A digital clock commits forwarded registers, latches ENV3 from the envelope before its step, advances envelope events, advances oscillators, advances pulse/noise pipelines, evaluates waveform output/readback and applies hard sync. Hard sync uses the pre-reset MSB edges; combined-waveform MSB pulldown can suppress an edge.
3. A 6581 OSC3 read returns this clock's selected waveform. The 8580 triangle/saw read stage retains the preceding triangle/saw value, gated by this clock's pulse and noise. Floating/pulse/noise readback does not acquire that extra stage. Repeated reads do not clock a voice.
4. CPU bus state belongs to the register timeline in both traced and normal operation. Digital replay does not drive old writes onto that bus. Audio state synchronization preserves that bus and its decay deadline.
5. POTX/POTY, OSC3 and ENV3 reads drive and recharge the bus. Write-only reads return the floating value and halve its remaining lifetime. The existing 8,192-cycle retention constant is provisional; it is not a calibrated leakage model.
6. Sampling does not clock additional SID cycles. Nearest integer targets retain the previous frame-count convention, while a signed rational remainder supplies the actual sample position. The causal FIR's fixed group delay is preserved. No fitted time offset is permitted in digital evidence comparisons.

The initialization API intentionally provides repeatable player state (including a zero, disabled envelope). It does not emulate a specified physical power-on or RESET-pin experiment. Hardware fixtures must supply an initialization sequence that establishes comparable state.

## Digital evidence

Behavioral reference: [libresidfp at commit 02566f917539b552e2e0a4f28f3dfa0d8b64b3a0](https://github.com/libsidplayfp/libresidfp/tree/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0). This is a source comparison, not a run of the separately pinned sidplayfp binary and not a new hardware measurement.

The envelope uses the 15-bit rate LFSR and comparison codes, explicit comparison/reset, state, divider and envelope-step stages, and a history-dependent exponential divider. Rate-register writes only select a comparator. GATE does not reset the rate clock. Tests cover all 16 attack rates, passed-comparator delay, threshold/retrigger history, sustain changes, genuine GATE-induced wrap/freeze, read latching and copying pending events. Under the deterministic reset convention, rate 0 compares on clock 9, schedules the step through reset and two pipeline clocks, and first steps on clock 12; ENV3 exposes it on clock 13. Subsequent attack steps are nine clocks apart. The inspected reference itself notes an unmodeled attack/divider reset subphase; physical boundary traces remain necessary.

RING substitutes the complemented source MSB into the triangle inversion path and is disabled when saw is selected. Saw+pulse is not silenced by RING. The former RING/SYNC amplitude multipliers and width-only pulse bias have been removed. Tests exercise both public profiles. Combined-waveform calibration remains provisional and differs between those profiles.

TEST assertion retains noise history, cancels the pending shift and latches that history. Release completes the shift with TEST forcing the feedback input high. Long TEST holds use gradual charge recovery with provisional reference-source defaults: initial 50,000 / subsequent 15,000 cycles for 6581 and 986,000 / 314,300 for 8580. These are not universal silicon constants. Short pulses around both shift phases are covered. Full combined-noise selector-transition and writeback behavior, including output tap/phase conventions, still needs cycle-indexed hardware qualification; the existing combined-noise approximations are not certified.

The left-shifting 23-bit noise register uses reset seed `0x7FFFFE` and DAC taps `20,18,14,11,9,5,2,0` mapped to DAC bits 11 through 4. Combined-waveform writeback uses the same taps. The previous seed/taps represented a state two ordinary shifts ahead, which cannot be combined directly with the history-preserving TEST-release operation. The [noise failure investigation](SID-noise-investigation-2026-09-26.md) records the source convention, direct regression vector and passing external comparisons.

## Audio sampling and numerical checks

`SidSampleClock` retains exact integer numerators until the final fractional conversion, including at long playback positions. `SidSong` carries these fractions through `C64Machine` to `SidSystem` and the resampler. The FIR uses 65 fractional kernels spanning -0.5 to +0.5 input cycles, interpolates between adjacent kernels and normalizes each for unity DC gain. Kernel construction is a configuration operation. The sample loop allocates no memory.

The audit's 10 kHz/PAL test fits only constant gain and phase after startup. Its engineering acceptance threshold is -80 dB relative residual, not a SID distortion specification. The observed residual improved from approximately -34.70 dB to -145.31 dB at 44.1 kHz and -148.21 dB at 48 kHz. The integer-ratio control remains approximately -247 dB. End-to-end song tests verify that playback actually supplies the fractional positions.

The VCR lookup now refines intervals against the existing continuous circuit equation, including its steep subthreshold region, instead of rounding to 16 bins. Refinement checks midpoint and quarter-point errors against 1e-5; a separate sweep checks absolute lookup error below 1e-4. Stable evaluation retains subthreshold current when `1 + exp(x)` would round to one. These are numerical corrections. They do not establish that the underlying circuit parameters match a physical specimen.

`VoiceDcVoltage` still cancels in the existing normalized input mapping. Reintroducing physical bias requires a consistent operating-point model and measurements; no unmeasured DC shift has been added.
