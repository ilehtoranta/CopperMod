# SID external reference results — 2026-09-26

The pinned sidplayfp reference is available and **all eight external emulator checks pass** after correcting the noise-register convention. The first run passed six checks and failed the MOS6581/MOS8580 pure-noise comparisons. The [failure investigation](SID-noise-investigation-2026-09-26.md) isolated a mismatch between the legacy noise seed/taps and the corrected TEST-release operation, then verified the fix with direct DAC/OSC3 regressions. No comparison threshold was loosened.

## Reference provenance and reproduction

- Download: [official sidplayfp v3.0.2 release](https://github.com/libsidplayfp/sidplayfp/releases/tag/v3.0.2), `sidplayfp-3.0.2-ucrt64.zip`.
- ZIP SHA-256: `dadd0f0ec352c1382c24728193dd293946e40c120a843295169acfb44c247173` (matches the release asset digest).
- Executable SHA-256: `a08d4f24a4baab726b49ea41d7e0b33026d856d2fe687879054b653da0506e35` (matches the existing conformance manifest).
- Runtime banner: sidplayfp **3.0.2**, libsidplayfp **3.0.1**, reSIDfp engine.
- Portable installation: `third_party/sidplayfp-3.0.2-ucrt64`; binaries remain ignored by Git.
- A local INI beside that executable isolates these runs from personal playback settings. Filter curve/range are 0.5, average combined-wave strength, standard 470 pF capacitor setting, no 8580 digiboost, and power-on delay zero. Command-line arguments force PAL and each requested SID model, mono float WAV at 48 kHz for conformance and 96 kHz for waveform diagnostics. Generated PSID fixtures run without external C64 ROM files.
- CopperMod uses ReferenceMeasured for the waveform and conformance checks; Balanced for the weak-spot, ADSR, reset and polarity checks; the D418 sine diagnostic renders both profiles and multiple output stages. Those profiles/output stages are not assumed electrically equivalent to reSIDfp.

Run on Windows with .NET 10:

```powershell
./scripts/test-sid-reference.ps1
```

The runner downloads the portable reference if absent, verifies the release archive and executable, sets the external-test flags and report locations, and restores the caller's environment afterwards. It returns a failure if any reference check fails. An optional `-ResultsDirectory` selects another report directory. It does not alter emulation calibration, fixture hashes or acceptance thresholds.

Final results are under `artifacts/sid-noise-investigation/fixed`: `sid-full-after-noise.trx`, `reference-provenance.json`, the effective `sidplayfp.ini`, waveform CSVs, ADSR traces, D418 and polarity reports, and `conformance/index.html` with WAVs and numerical results. This full run also enabled the supplied music corpus and reports 486 passed / 5 unavailable Pex checks skipped. The earlier failed results remain under `artifacts/sid-reference`, including the initial smoke-render banner in `reference-version.txt`. The original-core comparison is under `artifacts/sid-reference-baseline`; a copy of the test output directory loads the separately built original SID assembly, leaving the working implementation intact. Result directories are ignored build artifacts.

## Outcomes

| Check | Result | What it establishes |
|---|---|---|
| Generated MOS6581 waveform suite | Pass | All 17 sections pass; noise flatness 0.615812 versus reference 0.671621, spectrum similarity 0.999218 |
| Generated MOS8580 waveform suite | Pass | All 17 sections pass; noise flatness 0.604770 versus reference 0.690853, spectrum similarity 0.999484 |
| ADSR restart | Pass | Existing broad level/correlation bounds after fitted alignment |
| Weak-spot suite | Pass | Existing broad level/near-null checks |
| Reset transient | Pass | Reference/candidate renders and diagnostic report; does not assert waveform equality |
| D418 sine | Pass | Successful render/report and non-silent reference; does not assert hardware equivalence |
| Polarity probe | Pass | Audible reference/candidate probes and diagnostic report; does not assert polarity equality |
| Conformance manifest | Pass | All 19 fixture-level AC ratios satisfy the existing provisional bounds |

The waveform assertions aggregate all 17 sections instead of stopping at the first failure, and CSV reports include the noise metrics. No assertion or tolerance was removed or relaxed. Before correction, the two noise failures reproduced on both current-core runs, whereas the original SID core from audit HEAD `8fb826eacf8e9dc32948b482c878079448bd6e61` passed both suites. Restoring the original immediate TEST reseed was not the fix: reset state, DAC taps and writeback taps now consistently use the convention required by the history-preserving TEST operation.

These are compatibility checks. The waveform suite permits per-section fitted offsets up to 25 ms; the 8580 level checks use pure-wave normalization. The conformance gate validates AC ratios rather than waveform equality. A passing gate therefore does **not** establish matching waveforms or cycle-exact timing.

## Next evidence and correction work

1. Obtain the Pex/Mahoney measurement package: the 6581/8580 amplitude tables, raw 96 kHz `Pex_testfiles` WAVs, and the short 48 kHz `thcm_testfiles` WAV. Set `SID_PEX_MEASUREMENT_ROOT` and enable the raw-capture/calibration checks described in [hardware evidence requirements](SID-hardware-evidence.md). These recordings address D418 behavior.
2. Supply cycle-indexed bus/readback captures for a named physical 6581 specimen, covering envelope, OSC3, TEST/noise, ring/sync and open bus, plus the experimental metadata in the hardware manifest. Audio files alone cannot certify these cycles. Analog qualification additionally needs DAC/filter/output sweeps under a known clock, supply, temperature, filter capacitors and output load.

Five Pex-related full-suite checks and the separate core hardware-readback check remain unavailable. sidplayfp supplies an independent emulator reference; it cannot replace either physical evidence package.
