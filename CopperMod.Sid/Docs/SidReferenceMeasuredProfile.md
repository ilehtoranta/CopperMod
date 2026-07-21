# ReferenceMeasured SID profile provenance

`SidEmulationProfile.ReferenceMeasured` is an opt-in accuracy lane. `Balanced`
remains the default playback profile and retains its historical tuning except
where a digital or register-semantic bug is independently confirmed.

## Authority order

1. Checked-in hardware captures are authoritative.
2. Circuit and die-analysis results constrain digital-to-analog behavior where
   the local captures do not contain the required signal.
3. The pinned sidplayfp build supplies provisional targets for uncovered
   waveform, filter, and level categories.

The conformance manifest records the reference version, executable SHA-256,
chip model, clock, sample rate, metrics, and per-category acceptance limits.
Deterministic CopperMod snapshots remain a separate regression artifact and
are not treated as evidence of hardware accuracy.

## Parameter provenance

| Stage | ReferenceMeasured source | Status |
| --- | --- | --- |
| `$D418` settled amplitudes | Checked-in Pex/Mahoney/Tufvesson 6581 and 8580 captures | Hardware-measured, authoritative |
| `$D418` transition matrices and time constants | Checked-in Pex replay captures | Hardware-measured, authoritative |
| 6581 filter-routing click polarity | Pex output polarity plus SID routing semantics | Hardware-constrained |
| Waveform DAC R/2R ratio | Saved die/circuit analysis (`2.02` for 6581, ideal `2.00` for 8580) | Circuit-derived, provisional |
| Envelope DAC R/2R ratio | Saved circuit analysis (`2.05` for 6581, ideal `2.00` for 8580) | Circuit-derived, provisional |
| Combined-waveform pulldown tables | Per-model parameters pinned to sidplayfp 3.0.2-ucrt64 | Emulator-derived, provisional |
| Voice mix level and combined-waveform gains | Deterministic sidplayfp fixture fitting | Emulator-derived, provisional |
| Cutoff curve, resonance, LP/BP/HP gains, and saturation | Deterministic sidplayfp filter fixtures | Emulator-derived, provisional |
| Chip output low-pass and final headroom | Deterministic sidplayfp fixture fitting; MOS8580 ReferenceMeasured cutoff selected by Pex replay | Mixed: 8580 cutoff hardware-constrained, remaining values provisional |

The pinned sidplayfp executable hash is
`a08d4f24a4baab726b49ea41d7e0b33026d856d2fe687879054b653da0506e35`.
The generated combined-waveform tables are model-specific, allocation-free
after startup, and their source is deliberately tagged
`sidplayfp-emulator-derived` in code.

The MOS8580 oracle uses the median triangle/saw/pulse AC ratio as a fixture
normalization before enforcing the per-waveform level limits. This preserves
the hardware-authoritative voice and `$D418` output scale while still catching
incorrect relative levels. The calibrated 8580 combined selectors therefore
use unity gain after waveform pulldown, except noise+saw (`0.25`) whose
two-phase noise writeback leaves a much larger residual signal.

MOS8580 `ReferenceMeasured` uses a provisional `22 kHz` chip-output low-pass
cutoff selected by the Pex `$D418` transition-context replay. The same replay
improved from correlation `0.478` / NRMSE `0.878` at `14 kHz` to correlation
`0.857` / NRMSE `0.515` at `22 kHz`. MOS8580 `Balanced` retains its historical
`14 kHz` cutoff.

## Digital correctness derived from the reference implementation

The local sidplayfp source is also used as a provisional digital-timing oracle.
Oscillator output and readback are sampled before a same-cycle hard-sync reset,
and a source oscillator reset on that cycle cannot cascade sync into the next
voice. ReferenceMeasured MOS8580 combined noise now performs the same delayed
pulldown writeback as the reference implementation; the historical Balanced
behavior remains unchanged.

## Calibration and acceptance

Analog calibration is applied in signal-path order: voice level and DC,
waveform/envelope DACs, combined-waveform pulldown and level, filter cutoff,
resonance, filter-mode gains, saturation, master volume, and chip output. A
global post-hoc gain is not used to conceal category-specific errors.

The enforced oracle lane uses 48 kHz PAL/NTSC metadata and checks:

- pure-wave temporal correlation of at least `0.97`;
- standalone sync and ring-modulation correlation of at least `0.90`;
- non-null, non-modulated combined-wave correlation of at least `0.80`;
- normalized noise-spectrum similarity of at least `0.90`;
- pure/filter AC ratios of `0.80..1.25` and combined-wave ratios of
  `0.67..1.50`; the generated MOS8580 waveform lane applies these to ratios
  normalized by its pure-wave median as described above;
- filter segment RMS response error no greater than `3 dB` and cutoff-location
  error no greater than `10%`;
- 44.1 and 96 kHz AC RMS within `2%` of the canonical 48 kHz render, with no
  clipping.

Combined-wave captures whose reference AC level is below `0.012` are treated
as near-null signals. Correlation is ill-conditioned in that case, so those
segments enforce absolute/relative AC-level and harmonic-shape limits instead.

The full resonance fixture has a wider aggregate AC bound because repeated
`$D417/$D418` transitions contain hardware-authoritative transient energy. Its
steady-state filter segments still enforce the `3 dB` and `10%` limits.

## Deliberately deferred

EXT IN routing state is retained and continues to clock the filter with a
zero-valued external source. A public EXT IN sample-injection path, the
external C64 board/output network, and further multi-SID board-network
calibration remain separate work. Multiple SIDs currently retain independent
register routing and chip models and are summed without normalization.
