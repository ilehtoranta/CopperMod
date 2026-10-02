# Required SID evidence package

The ordinary `sid-core` CI job runs without external tools or captures. The manual `sid-accuracy` job requires a self-hosted Windows runner labeled `sid-evidence`. It is intentionally separate from the software regression gate. Configure .NET 10, `SID_HARDWARE_EVIDENCE_ROOT`, `SIDPLAYFP_EXE` and the measurement/corpus files on that runner before enabling it. No such runner or hardware package was available during implementation.

Locally run:

```powershell
./scripts/test-sid-accuracy.ps1 -EvidenceRoot C:/SidEvidence/6581-specimen
```

The script sets `SID_ACCURACY_REQUIRED=1`, runs core plus hardware readback tests, then the full SID suite with external comparisons enabled. Missing evidence is an error. It restores the caller's environment afterwards. Ordinary external tests use explicit skip attributes; opting into an external test whose required files are absent fails.

The package contains `manifest.json` and SHA-256-pinned JSON bus-event files. Required manifest fields:

| Field | Meaning |
|---|---|
| `schema` | `1` |
| `authority` | `hardware` (never relabel emulator output) |
| `phaseConvention` | `cpu-bus-end-write-next-clock-v1` |
| `specimen`, `revision` | Unique chip identity and revision |
| `captureMethod` | Instrument/firmware version, initialization and cycle-indexing procedure |
| `clockHz`, `supplyVolts`, `temperatureC` | Recorded experimental conditions |
| `filterCapacitancePf`, `outputLoadOhms`, `boardCoupling` | External circuit and measurement point |
| `cases` | Entries containing `file`, `sha256`, `coverage` |

Required coverage labels are `envelope`, `osc3`, `test-noise`, `ring-sync`, and `open-bus`. Each case is an array of ordered events. Each event has integer `cycle`, `register` (0–31), `value` (0–255), and `operation` (`write` or `read`). A read value is the measured byte. Same-cycle event order is retained. Tests replay each file on both profiles, with tracing enabled and disabled, and require exact values at the recorded cycles. Failure reports include cycle, register, expected/actual byte and pending digital state. This first schema qualifies 6581 readback only.

Example event syntax (illustrative, not measured evidence):

```json
[
  { "cycle": 0, "operation": "write", "register": 18, "value": 40 },
  { "cycle": 1, "operation": "read", "register": 27, "value": 0 }
]
```

An evidence package must initialize the hardware to comparable state. Preserve raw captures alongside converted events and document their derivation. Capture hashes identify the exact event files; authenticity, instrument calibration and conversion review are separate experimental responsibilities.

Other required external inputs:

- The sidplayfp executable must match the existing conformance manifest's version/hash. `SIDPLAYFP_EXE` overrides its default location. The separate `scripts/test-sid-reference.ps1` runner downloads and verifies the pinned portable Windows build and runs the eight emulator comparisons without requiring hardware captures. The local run is now available: [all eight checks pass after the noise correction](SID-external-reference-results-2026-09-26.md).
- Set `SID_PEX_MEASUREMENT_ROOT` to the raw Pex measurement directory, or place it under `CopperMod.Sid/Docs/Musik_RunStop_8-bit_sample_measurements_by_Pex_Mahoney_Tufvesson`. These measurements support D418 experiments only.
- Optional music integration tests accept an HVSC `C64Music` directory via `SID_CORPUS_ROOT`. Composer-qualified paths select the intended versions even when titles have duplicate basenames. The historical `TestTunes/SID` layout is also supported for unambiguous fixtures; the two Spijkerhoek arrangements now require their explicit HVSC paths. Enable playback and allocation tests with `SID_CORPUS_TESTS=1`, or provide the corpus for the required-evidence run. Enabled tests fail if a fixture is missing.
- `SID_D418_REPLAY_CALIBRATION=1` enables the existing D418 fitting diagnostics. Those fits and broad emulator AC-level comparisons do not prove physical waveform equivalence.

Full analog qualification is still outstanding. Choose a specimen and measurement setup, establish repeatability and tolerances before fitting, then capture DAC code sweeps, all waveform selectors, pulse frequency/width sweeps, filter modes/cutoff/resonance/input levels, output DC and polarity, transients and spectra, and D418 transitions at multiple rates. Use held-out captures for acceptance. Do not widen tolerances or fit an arbitrary time/polarity offset to make a failing digital trace pass. Existing AC-level conformance bounds remain compatibility checks; they have not been relabeled as 1:1 hardware criteria.

## Music corpus verification

The supplied local collection is HVSC v85 at `C:\Users\ilkle\Music\Tunes\C64Music`, with 61,157 SID files. Run its selected playback regressions and the Arkanoid CLI render with:

```powershell
./scripts/test-sid-corpus.ps1 -CorpusRoot 'C:/Users/ilkle/Music/Tunes/C64Music'
```

The script runs the full SID suite with corpus coverage enabled, then the targeted CLI test, saves results under `artifacts/sid-corpus`, and restores the caller's environment. It reads the music files in place. It does not render all 61,157 tunes or enable the unavailable hardware/sidplayfp checks. The 13 selected input files and their SHA-256 hashes are recorded in [the corpus verification record](SID-corpus-fixtures-2026-09-26.json). These hashes identify this run's inputs; subsequent collection updates may change them without indicating an emulation regression.

The fixtures cover Martin Galway's Arkanoid, Yie Ar Kung-Fu II, Green Beret, Short Circuit, Wizball and Game Over; Rob Hubbard's Commando; Chris Hülsbeck's Great Giana Sisters; Reyn Ouwehand's Flimbo's Quest intro; Wally Beben's Tetris; Jeroen Tel's G.I. Hero; and both Edwin van Santen's and Rodney Balai's Spijkerhoek. STIL identifies the latter as a cover, so they are separate test cases. Playback tests use the loader's metadata-selected model/clock and default emulation profile, with the subtunes and time windows specified in each test. These checks establish compatibility, audibility, finite output and allocation behavior, not physical audio equivalence.
