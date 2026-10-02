# SID noise failure investigation — 2026-09-26

Both failing sidplayfp waveform suites are now passing. The cause was a mismatch between the noise-register representation and the recently corrected TEST-release operation. The fix aligns the reset seed, DAC taps and combined-waveform writeback taps. It preserves noise history during short TEST pulses and leaves every external comparison threshold unchanged.

## Root cause and evidence

The earlier core used reset seed `0x7FFFF8` and noise DAC register taps `22,20,16,13,11,7,4,2` (mapped to DAC bits 11 through 4). The inspected reference uses, expressed in CopperMod's left-shifting bit order, reset seed `0x7FFFFE` and taps `20,18,14,11,9,5,2,0`.

Let `L` denote an ordinary 23-bit noise shift, with feedback `bit22 XOR bit17`. The legacy state is two shifts ahead of the reference state: `C = L²(R)`. The legacy taps compensate for those two shifts, so uninterrupted pure noise produces the same DAC sequence in both conventions. This explains why ordinary noise clocking tests did not expose the problem.

The corrected TEST release applies a different shift, `Q`, whose feedback is `NOT bit17`. It was applied directly to the legacy state, although `Q(L²(R))` is not generally equal to `L²(Q(R))`. Thus preserving TEST history and adding the release shift exposed an inconsistent representation.

A deterministic vector demonstrates the error without audio filtering, fitted alignment or a spectrum estimator:

| Point | Reference convention | Legacy convention with the recent TEST operation |
|---|---|---|
| After 1,137 ordinary shifts | Register `0x14B433`, DAC `0xC50` | Register `0x52D0CC`, DAC `0xC50` |
| After a one-clock TEST hold and release | Register `0x296867`, DAC `0x370`, OSC3 `$37` | DAC `0x360`, OSC3 `$36` |

The next twelve reference DAC values, beginning at release, are `370 A20 420 2C0 CC0 550 900 CA0 280 B90 150 120`. These independently derived constants form the new `SidNoiseReferenceTests` regression. All four cases (6581/8580 and Balanced/ReferenceMeasured) failed before the fix and pass afterwards. The test checks the hold, release and subsequent DAC/OSC3 sequence; it does not depend on sidplayfp being installed.

The behavioral source is [libresidfp WaveformGenerator.cpp at commit 02566f917539b552e2e0a4f28f3dfa0d8b64b3a0](https://github.com/libsidplayfp/libresidfp/blob/02566f917539b552e2e0a4f28f3dfa0d8b64b3a0/src/WaveformGenerator.cpp): `shift_phase2`, `set_noise_output`, `get_noise_writeback`, and `reset`. That source stores the register in reversed bit order; the values above explicitly convert it to CopperMod's convention. This is emulator-source evidence, not a newly measured silicon trace. The independently executed reference remains the pinned sidplayfp 3.0.2 / libsidplayfp 3.0.1 binary described in [external reference results](SID-external-reference-results-2026-09-26.md).

## Correction and verification

- `SidVoice` now initializes the noise register and shift latch with `0x7FFFFE`, reads DAC taps `20,18,14,11,9,5,2,0`, and uses those same taps for combined-waveform writeback.
- Register-state expectations in existing tests were translated to that convention. The TEST timing, history preservation, gradual recovery model and analog calibration were retained.
- Waveform CSV reports now include the reference/candidate noise flatness and normalized spectrum similarity. The existing flatness bounds remain 0.65–1.35 times the reference value; the spectrum-similarity minimum remains 0.90.

| Noise comparison | 6581 | 8580 |
|---|---:|---:|
| Candidate flatness before correction | 0.3755 | 0.3539 |
| Candidate flatness after correction | 0.615812 | 0.604770 |
| Reference flatness in final run | 0.671621 | 0.690853 |
| Normalized spectrum similarity after correction | 0.999218 | 0.999484 |
| Waveform suite after correction | All 17 sections pass | All 17 sections pass |

The independent core run reports **379 passed, 1 skipped**. The full SID run with the supplied HVSC corpus and external comparisons enabled reports **486 passed, 5 skipped**, including **all eight sidplayfp checks passing**. Core and integration counts overlap. The skips are the unavailable hardware-readback capture in the core project and five Pex measurement checks in the full project.

The saved CopperMod conformance baseline was regenerated for this intentional behavioral change. Compared with the baseline immediately before this investigation, only `sid-noise-edge-cases` changes; the other 18 fixtures are identical. Its tolerance remains `0.0001`. This baseline records CopperMod output and is not independent evidence.

Result artifacts are under `artifacts/sid-noise-investigation`: `noise-regression-before.trx`, `core-after-noise.trx`, and `fixed/sid-full-after-noise.trx`; the `fixed` directory also contains waveform CSVs, reference provenance/settings and the conformance report. Earlier failed external results remain under `artifacts/sid-reference`. These directories are ignored build artifacts.

Reproduce the direct regression or the external checks with:

```powershell
dotnet test CopperMod.Sid.Core.Tests/CopperMod.Sid.Core.Tests.csproj -c Release --filter FullyQualifiedName~SidNoiseReferenceTests
./scripts/test-sid-reference.ps1 -ResultsDirectory artifacts/sid-reference-current
```

## Limits of the conclusion

The nine-frequency, 180 ms flatness estimate is sensitive to the noise sequence's position in its window. That sensitivity was investigated, but it does not explain away the independently reproduced byte-level error. No estimator or acceptance bound was changed to make these failures pass.

Passing these checks establishes the corrected register convention and compatibility with the existing emulator comparisons. Their fitted audio offsets and broad level/spectrum bounds do not certify cycle-exact physical behavior. Combined-noise selector/writeback phases, TEST recovery constants and analog behavior still require the specimen-specific captures described in [hardware evidence requirements](SID-hardware-evidence.md).
