# vAmigaTS stratified corpus status — 2026-07-21

## Configuration

- A500 PAL OCS, Kickstart 1.3
- Release build with hardware specialization enabled
- Raw-offset scanning disabled
- No display offsets or compensating delays
- 143 ADFs; 112 have local raw references
- Runtime: 17 minutes 23 seconds; no timeouts or stuck cases

Machine-readable results:

- `TestResults/vamigats-stratified-2026-07-21-results.jsonl`
- `TestResults/vamigats-stratified-2026-07-21-progress.log`

## Results

| Area | Total | Exact | Mismatch | Raw unavailable |
|---|---:|---:|---:|---:|
| VPOS | 30 | 10 | 20 | 0 |
| Blitter BUSY | 17 | 0 | 17 | 0 |
| Copper WAIT | 14 | 1 | 13 | 0 |
| DDF | 42 | 11 | 27 | 4 |
| CIA TOD | 27 | 0 | 0 | 27 |
| Attached sprites | 6 | 1 | 5 | 0 |
| Paula basic interrupts | 7 | 0 | 7 | 0 |
| **Total** | **143** | **23** | **89** | **31** |

Compared with the July 20 post-refactor baseline, 44 comparable cases improved,
42 regressed, and 26 were unchanged. Exact matches increased from 7 to 23.

## Restored exact cases

All 16 raw regressions identified on July 20 are exact again:

- `cycle01v`, `cycle01vh`, `cycleD9v`, `cycleD9vh`
- `ddf1`–`ddf10`
- `farright1`
- `waitblt1`

The cycle quartet also passed the independent adjacent-frame timing gate.

## Largest remaining improvements

- `copwait4`: 40,584 to 460 mismatches
- `copwait3`: 24,176 to 376 mismatches
- `copwait9`: 24,040 to 240 mismatches
- `copwait1`, `copwait2`, `copwait7`, and `copwait8`: each improved by 624 pixels
- Most Blitter BUSY cases improved by roughly 1,100–1,800 pixels

## Largest regressions

- `basicint4`: 30,952 to 45,112 mismatches
- `bbusy15`: 5,404 to 13,920 mismatches
- `basicint3`: 2,172 to 7,936 mismatches
- `vhpos2`: 5,052 to 10,104 mismatches
- `ersy1`: 13,210 to 17,838 mismatches

## Next investigation

Start with the narrow Copper WAIT cluster: `copwait9` (240), `copwait3`
(376), and `copwait4` (460). Their former broad regression is gone, leaving a
small boundary signature suitable for a first-divergent-transfer comparison.
Keep the 23 exact cases as the regression gate.
