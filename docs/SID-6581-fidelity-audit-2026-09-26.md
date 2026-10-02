# MOS6581 fidelity audit — 2026-09-26

**Verdict: the implementation does not yet meet cycle-exact digital behavior or 1:1 MOS6581 audio fidelity.** Eight issues were reproduced with focused probes; two additional findings concern envelope architecture and accuracy validation. All chip probes used `ReferenceMeasured`, so selecting that profile does not resolve these findings.

Audited checkout: `8fb826eacf8e9dc32948b482c878079448bd6e61`. Production code was not changed. The audit covers the SID digital core, register scheduling/readback, analog model and calibration, output sampling, and related tests. It is not a complete CPU/VIC/CIA or transistor-level certification.

Evidence distinctions matter: a reproduction confirms what CopperMod does. Expected behavior below comes from documented circuitry, the MOS datasheet, and inspected reSIDfp source; no fresh physical-chip capture or external emulator render was performed. reSIDfp is a comparison implementation, not a substitute for hardware evidence.

| Priority | Finding | Evidence |
| --- | --- | --- |
| P1 | Output sampling discards fractional SID-cycle position | Two failing tone probes; integer-ratio control passes |
| P1 | Release-rate writes unlock a zero envelope without GATE | Failing register-only probe |
| P1 | TEST destroys noise history immediately | Failing register-only probe; reference-source mismatch |
| P1 | RING forces saw+pulse to zero despite triangle being absent | Failing waveform probe; datasheet/reference-source mismatch |
| P1 | Pulse-width correction changes static DAC polarity | Failing equal-DAC probe |
| P1 | Audio catch-up overwrites CPU-visible bus state | Failing scheduling-invariance probe |
| P1 | MOS6581 OSC3 is delayed; ENV3 lacks its separate latch | OSC3 probe plus source inspection |
| P1 | Envelope state/step/divider pipelines are not modeled | Source inspection against documented reference timing |
| P2 | Write-only reads renew open-bus retention indefinitely | Failing polling probe; reference-source mismatch |
| P2 | Accuracy gates cannot substantiate cycle or waveform equivalence | Test and conformance validator inspection |

P1 means a direct blocker for the requested fidelity target. P2 means a narrower behavioral issue or an evidence gap that still needs resolution before certification.

**1. Preserve fractional position when producing output samples (P1).**

Locations: `CopperMod.Sid/SidSampleClock.cs:106–113`, `SidWindowedSincResampler.cs:99–113`, and `SidSystem.cs:275–287`.

The sample clock rounds each target to an integer SID cycle. The resampler applies one fixed FIR kernel at the newest integer sample; its `Read()` has no fractional-phase input. Correct average sample count and a good low-pass filter do not remove the resulting timing modulation.

Reproduction: feed a pure 10 kHz sine at the 985,248 Hz PAL clock, read at the production sample-clock targets, discard 50 ms of startup, and fit gain and phase over 100 ms. Residual RMS relative to the fitted tone is **1.84109% at 44.1 kHz** and **1.84054% at 48 kHz**, approximately **−34.70 dB**. A 960,000 Hz / 48,000 Hz integer-ratio control gives approximately `4.39e-13` relative residual. This isolates the sampling schedule from filter startup, constant latency and gain.

Use a fractional-delay/polyphase resampler driven by the exact rational sample position. Keep SID state updates on the SID clock; interpolate the filtered output at the host sample time. Add end-to-end in-band spectral-error tests. Existing resampler tests examine FIR coefficients, DC and frequency response, which do not catch this issue. The probe's −80 dB acceptance target is an audit engineering target, not a SID specification. The measured residual is not a measurement of SID harmonic distortion.

**2. Release-rate writes can create a full envelope from silence (P1).**

Location: `CopperMod.Sid/SidVoice.cs:329–340`.

`HandleEnvelopeRateWrite` explicitly clears zero hold and primes the exponential counter when a faster release rate is written after the counter passed its new comparison value. Reproduction without private-state injection: reset; write SR=`$0F`; clock 20 cycles with GATE continuously low; write SR=`$00`; clock 32,757 cycles. The envelope becomes **`$FF`**, although no gate event occurred. This can resurrect a silent voice merely because a player updates ADSR parameters.

The reference release-register write changes the release comparison value, not the counter-enable latch. Remove this artificial unlock and validate actual gate-induced wrap cases separately. The existing `ReleaseRateWriteCanClearZeroHoldAndWrapAfterAdsrDelay` test enshrines the suspect behavior. [Reference register handling](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/EnvelopeGenerator.cpp#L131-L144).

**3. Model TEST as a noise-latch operation rather than immediate reseeding (P1).**

Locations: `SidVoice.cs:812–870`; related combined-noise handling at `873–912`.

Asserting TEST calls `BeginNoiseReset`, replacing both the live noise register and shift latch with `$7FFFF8`. Each held cycle keeps this seed until a universal 16,384-cycle threshold replaces it with all ones. Clearing TEST copies the same latch back rather than completing the test-controlled shift.

Reproduction: run pure noise at frequency `$FFFF` for 100 cycles after reset. Its register is `$7FFE00`; assert TEST for one cycle and it becomes **`$7FFFF8`**. The previous noise history has disappeared immediately. This affects short TEST pulses, noise sequences and sample techniques based on them.

The inspected reference latches existing history at TEST assertion, completes the shift on release, and treats slow charge recovery separately. Its recovery constants are explicitly chip/temperature dependent. Repair assertion, hold and release as distinct operations, then test short pulses across both noise-shift phases and selector changes. The current phase-1 early return in combined-noise writeback also requires targeted hardware traces. [Reference noise/TEST implementation](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/WaveformGenerator.cpp#L393-L418).

**4. Remove the RING-dependent saw+pulse override (P1).**

Locations: `SidVoice.cs:604–609`; related amplitude override at `755–765`.

For a 6581, selector `$60` plus RING unconditionally sets the DAC code to zero. Two otherwise identical voices using `$60` and `$64` therefore differ even though neither selects triangle. In the probe, the first mismatch is cycle 2: **`$001` versus `$000`**. In `ReferenceMeasured`, this forced zero can also change accumulator-MSB pulldown and subsequent sync behavior.

RING belongs to the triangle path. The reference disables its MSB substitution when saw is selected; it does not mute saw+pulse. Remove the forced zero and test all selectors with RING toggled. Also investigate the independent `0.86` / `1.29` triangle gain multipliers selected by RING/SYNC: those are amplitude corrections outside the digital waveform mechanism and need independent hardware justification. [MOS control-register description](https://www.waitingforfriday.com/?p=661), [reference selector wiring](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/WaveformGenerator.cpp#L354-L364).

**5. Pulse-width shaping corrupts even a settled static waveform (P1).**

Locations: `CopperMod.Sid/SidAnalog.cs:41–65`; callers in `SidVoice.cs:407–433`.

`ScalePulseWidthEdgeOutput` depends on pulse width alone and adds a large positive bias for narrow pulses. It does not know frequency, time since an edge, or whether an edge occurred.

Reproduction: use TEST to establish phase zero, clear TEST with FREQ=0, and allow 100 cycles to settle. Widths `$001` and `$800` both produce DAC code `$000`, but their waveform outputs are **`+0.7844` and `−0.54`** respectively. This is a polarity change for an identical indefinitely held DAC code. It occurs in both profiles because this function has no profile parameter.

Remove the width-only bias. If hardware establishes pulse-edge settling, model its state and timing, and test a frequency/width sweep including FREQ=0. A static comparator result must not acquire this large width-dependent polarity change. [Reference pulse comparator description](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/WaveformGenerator.h#L59-L65).

**6. Preserve read-driven bus state when synchronizing the shadow SID (P1).**

Locations: `CopperMod.Sid/SidSystem.cs:483–506`, `SidChip.cs:195–196`.

The fast CPU-read path uses `_registerChips`, while audio uses `Chips`. Queue compaction copies the entire audio chip state back to the register chip, including the bus value and timestamp. Reads that drove the register-side bus are absent from the audio-side copy.

Reproduction: queue 64 writes of `$33` at cycles 1–64; read POTX at cycle 65 and obtain `$FF`; render audio through cycle 65; read a write-only register at that same cycle. It returns **`$33`**, undoing the POT read. This establishes an internal correctness defect independently of any disputed analog behavior.

Keep one authoritative CPU-visible bus history or exclude it from audio-state synchronization. Add equivalence tests across different render chunk sizes, polling schedules, tracing on/off, and the queue-compaction boundary.

**7. Reconcile OSC3 and ENV3 latch timing with the chip model (P1).**

Locations: `SidVoice.cs:469–473`, `SidChip.cs:239–242`.

OSC3 always returns the previous cycle's waveform for every model and selector. In a TEST-initialized 6581 saw probe with FREQ=`$8000`, the accumulator reaches `$010000` after two clocks, but **OSC3 is `$00` instead of the current saw byte `$01`**. The inspected reference makes the extra triangle/saw delay specific to the 8580. Conversely, CopperMod reads the current envelope counter directly for ENV3, whereas the reference has a distinct envelope read latch.

The current tests explicitly require delayed 6581 OSC3 and immediate ENV3. Establish a shared phi-phase convention for CPU access, register forwarding, waveform evaluation and read latching; verify it with cycle-indexed hardware readback before changing one delay in isolation. This finding is a reproduced discrepancy against inspected reference timing, not a fresh hardware measurement. [Reference OSC3 handling](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/WaveformGenerator.h), [reference ENV3 latch](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/EnvelopeGenerator.h#L181-L190).

**8. Implement the envelope pipelines and history-dependent divider (P1, source-level finding).**

Locations: `SidVoice.cs:214–294`, `475–507`, `995–1032`.

GATE changes immediately select Attack/Release; rate matches immediately step the envelope; reaching `$FF` immediately selects Decay. `_envelopeDirectionChangePending` is set and later cleared but does not delay anything. The exponential period is recomputed from the current envelope using numeric ranges, instead of retaining a divider state changed by specific counter values. These designs lose timing and history during retriggers, attack-to-release transitions and threshold crossings. Merely clocking them once per cycle does not make them cycle-exact.

The reference documents multi-cycle state and envelope pipelines from die analysis and transistor-level work. Replace the immediate state machine with explicit pipeline/latch behavior or a demonstrably equivalent representation; compare register-generated traces over gate/rate/threshold boundary sweeps. Do not assume a whole-number rate counter is inherently invalid, but require proof of equivalence through wrap and comparison timing. [Reference envelope pipelines](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/EnvelopeGenerator.h#L181-L375).

**9. Do not recharge the bus when reading a write-only register (P2).**

Location: `SidChip.cs:233–252`.

Every read calls `DriveOpenBus`, including reads that only return the retained bus value. Writing `$A5` once and reading a write-only register every 1,000 cycles still returns **`$A5` after 100,000 cycles**; continuing the polling preserves it indefinitely. The inspected reference treats these reads as discharging the bus, while readable registers actively drive it.

Separate driven reads from floating reads. Model the retention assumptions for the chosen silicon, and test dense polling and mixed readable/write-only accesses. The exact decay constant and bit order need chip-specific measurements; the current renewable lifetime is a separate problem. [Reference bus-read behavior](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/SID.cpp#L371-L406).

**10. Make accuracy evidence enforce the fidelity claim (P2).**

Locations: `CopperMod.Sid.Tests/SidPlayFpWaveformOracleTests.cs:104–109`, `PexD418MeasurementTests.cs:15–21`, `SidConformanceTests.cs:103–110`; `CopperMod.Tools/SidConformance.cs:178–425`; `ConformanceFixtures/manifest.json`.

Optional oracle/capture tests return normally when disabled or when some sources are missing, which test runners count as success. The conformance validator primarily accepts AC-level ratios, plus optional segment response/cutoff-location checks. It does not enforce sample-by-sample or cycle-readback equality. Many manifest ranges allow 0.80–1.25 or 0.67–1.50 of reference AC level. Recording correlation/difference metrics in a report does not make them acceptance criteria.

Mark unavailable coverage as skipped, and provide a mandatory accuracy job that fails when required fixtures are absent. Gate digital accuracy on cycle-indexed reads/traces, and analog accuracy on independently specified measured tolerances including polarity, DC, transient response and spectral shape. Keep CopperMod's own golden baselines as regression tests, not hardware certificates. Revisit existing tests that explicitly expect findings 2, 3, 7 and 9.

**Analog fidelity remains unestablished beyond those concrete defects.**

There is substantive analog modeling: nonlinear DAC tables, an op-amp transfer curve, cutoff/resonance networks, nonlinear filter integration, D418 transition matrices, and separate board coupling. These are useful foundations, but the current calibration is a mixture of evidence and assumptions:

- `SidReferenceCombinedWaveformData.cs:5–14` explicitly labels its calibration **sidplayfp-emulator-derived** and provisional. `SidFilterProfile.cs:389–391` similarly identifies provisional oscillator/filter calibration. `ReferenceMeasured` is not an end-to-end measured 6581 specimen.
- `SidMos6581AnalogFilter.cs:263–266` uses R4AR op-amp points, while the voice uses a single 6581 floating-output/TEST recovery choice. A filter profile such as DarkR3 does not select a coherent complete revision model.
- At `SidMos6581AnalogFilter.cs:1110–1116`, `VoiceDcVoltage` algebraically cancels from voice mapping. Varying that parameter cannot model voice bias variation in the filter. This is a source-level model limitation; the effect of restoring a physical operating point requires measurements.
- VCR signal dependence is rounded into only 16 bins (`405–411`), and clipping/drive/output trims remain phenomenological. Measure convergence and distortion across input amplitudes and capacitor values before assigning an error bound.
- EXT IN is fixed to zero (`SidChip.cs:604–607`), and POTX/POTY are fixed to disconnected `$FF` (`239–240`). This can be an explicit music-player scope, but it is not full chip-pin equivalence.
- D418 matrices are evidence for the measured D418 experiment. They do not establish oscillator, envelope, filter or general board-output accuracy. Their raw captures were unavailable here.

A meaningful analog target must identify a 6581 revision and specimen, supply and clock, external filter capacitors, output loading/coupling and temperature. Match that specimen within measured repeatability; use separate calibrated profiles for other chips. Hardware-derived recovery measurements explicitly vary with chip and temperature. [Reference measurement notes](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/WaveformGenerator.cpp#L26-L62).

**Verification performed and reproducibility.**

- The normal SID test-project run was blocked by existing `CS1729` at `CopperMod.Cust/CustMachine.cs:488`: `KickstartTrapTable` has no 17-argument constructor. The SID library itself built. No unrelated fix was made.
- An isolated project linked the unmodified core test sources: **236 passed**. A subsequent run added CPU/SID integration, CIA and VIC tests: **98 passed**. Total: **334 distinct existing tests passed**. This is not a full-suite pass.
- Seven new register/waveform/scheduling probes failed as described above. Two real-clock resampling cases failed; the integer-ratio control passed. Thus **nine failing audit cases cover eight reproduced findings**, with one passing control. They are intentionally diagnostic, outside the regular test project.
- The local research documents, saved 6502.org pages, raw Pex captures and default sidplayfp executable were absent. External audio comparison and hardware replay are unavailable coverage, not passes.
- Reference source inspected: upstream libresidfp commit `02566f917539b552e2e0a4f28f3dfa0d8b64b3a0`. This is a source audit, not execution of the repository's separately pinned sidplayfp 3.0.2 binary.

Local audit projects and result files are in the ignored `artifacts/sid-audit-20260926` directory. From the repository root:

```powershell
dotnet test artifacts\sid-audit-20260926\CoreTests.csproj
dotnet test artifacts\sid-audit-20260926\probes\Probes.csproj
```

The second command is expected to fail until the findings are resolved. Existing evidence files are `TestResults/core-tests.trx`, `TestResults/machine-tests.trx`, `probes/TestResults/fidelity-probes.trx`, and `probes/TestResults/resampling-with-control.trx`. The harness references production projects and original test files; it does not duplicate the SID implementation.

**Suggested repair order.** First establish a required cycle-readback oracle and a consistent phi-phase contract. Repair ADSR, TEST/noise, ring selection, read latches and bus-state ownership against those traces. Fix fractional output sampling and remove the static pulse-width bias. Then calibrate one identified 6581 specimen across DAC codes, all waveform selectors, frequency/width combinations, filter modes/cutoffs/resonance/input levels, and D418 transitions at several write rates. Preserve the current scheduling and resampler regression coverage while adding tests that detect the reproduced defects.
