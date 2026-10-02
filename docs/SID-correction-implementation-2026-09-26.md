# SID correction implementation — 2026-09-26

The software corrections below implement the actionable part of the [fidelity audit](SID-6581-fidelity-audit-2026-09-26.md). Physical specimen calibration and hardware certification remain incomplete. The public profile names and default selection are unchanged; `ReferenceMeasured` still includes provisional emulator-derived calibration.

## Delivery against the correction plan

| Work package | Delivered | Remaining evidence/work |
|---|---|---|
| Independent verification | `CopperMod.Sid.Core.Tests`, solution registration, normal CI SID job, permanent audit regressions, explicit external-test skips; all eight pinned sidplayfp checks pass | Run the required evidence job with real captures |
| Shared timing and bus ownership | Written clock contract; CPU bus survives digital/audio replay and queue compaction; write-only reads cannot renew charge | Measure specimen-specific retention and read discharge |
| ADSR | Rate LFSR, pending comparison/reset/state/step/divider events, latched exponential period, genuine GATE wrap/freeze, separate ENV3 latch | Hardware boundary sweeps; subcycle divider-reset qualification |
| Oscillator/noise | TEST preserves history and shifts on release; consistent noise seed/DAC/writeback convention; model-specific recovery defaults; RING wiring and saw selector isolation; removed pulse-width bias and pure-triangle gain overrides; MSB pulldown applies in both profiles | Full combined-noise phase/selector/tap qualification and measured recovery constants |
| Register readback | Current 6581 OSC3, separate 8580 triangle/saw stage with current pulse/noise gating, stable repeated reads | Physical cycle-indexed readback package |
| Output sampling | Exact rational fractions through song/CPU playback; fractional FIR; no phantom cycle on repeated sampling; allocation guard | Wider device/platform performance qualification |
| Analog work | Adaptive VCR lookup bounded against its existing circuit equation; stable subthreshold arithmetic | Specimen selection, physical bias model, DAC/filter/output/D418 measurements and held-out calibration |
| Integration | Full SID suite and generated conformance baselines verified; playback benchmark added; external evidence gate fails when required inputs are absent | Physical acceptance campaign; broader pre-existing Tools failures listed below |

The detailed software convention and source provenance are in [SID timing contract](SID-timing-contract.md). Package format, runner setup and remaining physical experiments are in [hardware evidence requirements](SID-hardware-evidence.md).

## Verification results

- Independent core suite: **379 passed, 1 skipped**. The skipped test is hardware readback replay without captures.
- Full SID suite with the supplied HVSC v85 corpus and sidplayfp comparisons enabled: **486 passed, 5 skipped**. This includes **25 real-tune playback/allocation cases across 13 composer-qualified files**, four fixture-resolution regressions, four new noise/TEST reference cases and all eight external emulator checks. The remaining skips are Pex measurement checks. Five allocation cases previously returned silently when missing and now explicitly skip unless enabled, then fail on missing fixtures. Core and full-suite counts overlap; they must not be added as distinct tests.
- Arkanoid CLI render: **1 passed**, now using the supplied corpus through the same fixture resolver. The missing-fixture failure is resolved.
- sidplayfp checks: **8 passed, 0 failed, 0 skipped** within the full run above. The initial two pure-noise failures were traced to inconsistent noise-register seed/taps around TEST release and corrected without relaxing thresholds. See [the investigation](SID-noise-investigation-2026-09-26.md) and [external reference results](SID-external-reference-results-2026-09-26.md) for evidence, setup and the limits of passing checks.
- All nine originally failing audit cases pass. The integer-ratio resampling control also passes.
- PAL 10 kHz resampling residual: **-145.31 dB at 44.1 kHz**, **-148.21 dB at 48 kHz**, versus approximately **-34.70 dB** before correction. These are synthetic sampler measurements, not physical SID audio error.
- End-to-end tests verify fractional timing through `SidSong` and `C64Machine`; further tests cover same-cycle sampling, long rational positions, CPU bus reads across compaction, short TEST pulses at shift boundaries, model-specific OSC3, and pending-state copies.
- Allocation guard: zero allocations after configuration during digital clocking and fractional FIR reads.
- Required hardware mode was deliberately exercised without evidence and failed, confirming missing captures cannot silently pass.
- `git diff --check` passed.

The checked-in CopperMod conformance baseline was regenerated after the intentional digital, waveform and sampling changes. The subsequent noise correction changes only the `sid-noise-edge-cases` fixture relative to that regenerated baseline. Its tolerance remains `0.0001`. It is a regression baseline, not an independent reference. No sidplayfp thresholds or physical measurement tolerances were loosened to accept these changes.

The full SID project initially could not compile because `CustMachine` supplied an extra `HostOk` argument to `KickstartTrapTable`. Removing that obsolete argument restores the declared constructor mapping and permits integration builds.

## Performance

BenchmarkDotNet ShortRun, .NET 10.0.12, Ryzen 5 5600X, Windows 11; three active voices and a resonant routed filter, including host-rate resampling:

| Output rate | Profile | Mean per host sample | Allocations |
|---|---|---:|---:|
| 44.1 kHz | Balanced | 7.487 µs | 0 |
| 44.1 kHz | ReferenceMeasured | 10.051 µs | 0 |
| 48 kHz | Balanced | 9.514 µs | 0 |
| 48 kHz | ReferenceMeasured | 7.170 µs | 0 |

These are local short-run observations, not portable performance guarantees or a before/after speed comparison. Initialization and capture/trace allocation are outside the measured hot loop.

## Broader checks and unresolved limitations

A targeted `CopperMod.Tools.Tests` run initially produced **19 passes and 2 failures**:

1. `SidLoopDetectionCanResolveUnknownSidDuration`: the generated 128-tick write-stream loop is not detected within eight seconds. The same fixture was replayed against an isolated copy of the **original SID source at audit HEAD**, which also reported `Detected=False`, 401 ticks, 8.0000731 seconds. This is a confirmed pre-existing loop-detector failure; the detector was not changed.
2. `RendersSidFixtureWithExplicitSecondsDespiteUnknownDuration`: initially blocked by the absent `TestTunes/SID/Galway/Arkanoid.sid`. **Resolved and rerun successfully** using `SID_CORPUS_ROOT` and the composer-qualified HVSC Arkanoid fixture.

The repository's existing `Microsoft.Build.Tasks.Git` NU1902 warning remains. The whole solution and physical hardware were not certified by these checks.

Analog calibration cannot be completed from the available files. No specimen, raw hardware bus traces, raw Pex package or local hardware research documents are available. The previously missing sidplayfp executable has now been obtained and tested. In particular, `VoiceDcVoltage` still cancels in the existing normalized model, and combined waveform/noise behavior retains provisional approximations. EXT IN remains zero and POT reads represent disconnected inputs. Physical reset, external input, paddle conversion, chip-to-chip variation and board loading prevent a full pin-for-pin equivalence claim at this stage.

Local result files are under `artifacts/sid-implementation`: `core.trx`, `integration.trx`, `required-missing.trx`, `tools-sid.trx`, and `benchmarks/results`. These files are ignored build artifacts. The reproducible tests and evidence gate are tracked source.

The subsequent corpus run is under `artifacts/sid-corpus`: `sid-corpus.trx` and `sid-corpus-cli.trx`. Reproduce it with `scripts/test-sid-corpus.ps1 -CorpusRoot <C64Music directory>`. Input versions are recorded in [SID corpus fixtures](SID-corpus-fixtures-2026-09-26.json). No emulation calibration or acceptance thresholds were changed for this corpus run; the input discovery and previously inactive coverage were corrected.

Latest verification after the noise correction is under `artifacts/sid-noise-investigation`: `core-after-noise.trx` and `fixed/sid-full-after-noise.trx`, with external waveform and conformance reports in `fixed`. These results supersede the earlier core/full-suite counts and the initially failed external comparisons.
