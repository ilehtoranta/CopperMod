# Synthetic suite reference qualification

Milestone 6 is in progress. This is scoped software-reference evidence, not
exhaustive external coverage, physical CPU qualification or desktop readiness.
68010 and 68060 remain diagnostic profiles.

## Sources and reproducibility

The [Motorola programmer reference](https://www.nxp.com/docs/en/reference-manual/M68000PM.pdf)
and applicable processor manuals establish architectural expectations. Independent
software corpora are useful for finding disagreements; their assertions require
review when they disagree with documented behavior.

The reused Musashi adapter executes self-checking programs from
[`kstenerud/Musashi`](https://github.com/kstenerud/Musashi/tree/72c1d74800f3087b45a0c1a7342601bbed898881/test),
pinned to `72c1d74800f3087b45a0c1a7342601bbed898881`. It does not execute the
Musashi CPU as a differential oracle. Each requested profile uses the public CPU
factory, including the A1200 EC020 factory. Both complete fixture directories
(60 mc68000 programs and 18 mc68040 programs) are required. Reports distinguish
passing, mismatching and explicitly excluded programs, record input SHA-256
identities and retired instruction counts, and retain recent retirement diagnostics.
Program counts do not imply coverage of every instruction/address combination.

```powershell
git clone https://github.com/kstenerud/Musashi artifacts/reference-musashi
git -C artifacts/reference-musashi checkout 72c1d74800f3087b45a0c1a7342601bbed898881
./scripts/test-copper68k-synthetic.ps1 -Deep -Seed 68020 -Samples 10000 -MusashiPath artifacts/reference-musashi
```

Requested audits fail for missing fixture directories, incomplete selections,
unknown/duplicate model IDs or any mismatch. The command rejects modified pinned
inputs. SHA-256 identities are written to `musashi-inputs.json` and the per-program
`musashi-model-audit.json`; selected profiles must each execute programs.

| Profile | Passing programs | Explicit exclusions |
| --- | ---: | ---: |
| 68000 | 56 | 22 |
| 68010 | 56 | 22 |
| 68EC020 | 72 | 6 |
| 68020 | 72 | 6 |
| 68030 | 72 | 6 |
| 68040 | 72 | 6 |
| 68060 | 66 | 12 |
| A1200 EC020 | 72 | 6 |
| Total | 538 | 86 |

Four mc68000 fixtures retain the previous invalid-BCD/undefined-DIV-flags caveats.
000/010 exclude the advanced directory because its programs assume 020+
instructions without unavailable-instruction handlers. All advanced profiles
exclude `cmp2.bin`'s conflicting carry assertion and `chk2.bin`: its final
`CHK2.W (A7),A0` at binary offset 0xEC (`02D7 8800`) uses long bounds
`10000000/70000000` but expects a long-sized out-of-range trap. Word-sized bounds
are instead `1000/0000`, sign-extended and compared against all 32 bits of A0;
the wrapped interval contains `70000001`. The binary agrees with its unsuffixed
source instruction, not the intended long operation. Inputs remain unchanged.
060 additionally excludes MOVEP, CHK2, CAS2 and 64-bit multiply/divide programs
without software handlers, and the interrupt program's required MSP/format-1 pair.

## Disagreement found and corrected

[MC68040UM section 8.2.6](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf)
lists NOP, MOVES, CAS/CAS2, MOVE USP, MOVEC and cache/MMU serializers among T0
events. The earlier suite used 020/030 flow rules for 040. New scenarios reproduced
481 mismatches on 040 and none on the other profiles. Correcting the 040 classifier
passes all selected profiles. Tests include all initial CCRs, both privilege modes,
aborting exceptions and batch-boundary trace changes. TAS remains a negative case:
the external branch-status list differs from the architectural T0 list.
FPU tracing and enabled-MMU execution remain outside this roadmap.

## Consolidation proof

`M68kShiftTests.AslByteSetsOverflowWhenSignChanges` is replaced by
`SyntheticBitShiftTests.ShiftsCountsValuesAndFlags`. The named
`<model>/ASL/1/D2/historical-sign-change/op=E302/value=6891C884/count=1/ccr=00`
scenario preserves its input and assertions through shared independent expectations.
It also checks exact PC, untouched registers/memory and a following sentinel on
all eight profiles. The larger shift matrix covers all CCRs and counts.

Before retirement, the `000-asl-overflow` mutation disabled the production
68000 overflow update. Both the original regression and the synthetic replacement
failed in the same run. `040-t0-serializers` separately removes the new classifier
and reproduces the trace failure. Evidence is under
`artifacts/m6-mutation-proof-verified/`; `mutation-proof.json` records
`originalRegressionDetected=true` for the former and exact failing case IDs.
Sources are restored and rebuilt by the mutation command.

```powershell
./scripts/test-copper68k-synthetic-mutations.ps1 -Scope Consolidation
```

Only this pure semantic duplicate is retired. Timing-policy, cache, prefetch,
bus ordering, detailed fault sequencing, JIT and native ROM/media tests remain.

## Remaining qualification and implementation gaps

- Existing SingleStepTests and WinUAE adapters remain available; no new external
  corpus execution is claimed for them in this slice. WinUAE integer integration
  currently selects 68000; multi-model native bridge/fixtures still need qualification.
- RTE internal restart remains untested: 010 format 8, 020/030 formats 9/A/B,
  and 040 format 7. The current advanced decoder does not implement all these
  legal restoration protocols; this is an implementation gap, not invalid encoding.
- Nested 000 address errors during exception stacking can recurse on an odd SSP.
  The eventual fix needs documented double-fault halt behavior and fault-bus tests.
- 040 PFLUSH currently overconsumes an extension word; the PTEST mask comparison
  is unreachable and its decoder also assumes an extension. Motorola's encodings
  in PRM sections 6-35/6-71 are single-word instructions. These need a separate
  correction, privilege checks and selected disabled-MMU semantic coverage.
- External BKPT replacement, physical MOVES function-code spaces, LPSTOP
  CPU-space broadcast and real CALLM/RTM access-control responses remain unqualified.
- Exhaustive independent addressing references, hardware captures, physical
  cache/pipeline timing, enabled MMU and FPU arithmetic are not claimed here.

Milestone 6 remains open until the planned remaining reference work and review
are completed. Published packages are immutable; publication is a separate release.

## Validation checkpoint

The full CPU suite passes 4,593 tests with six optional external-reference skips.
The report gate passes all 8,333,190 deterministic cases in 338 batches, plus
320,000 seeded cases (seed 68020, 10,000 per profile/group). The pinned Musashi
audit passes 538 programs; 86 exclusions remain explicit. A requested missing
fixture audit fails as expected. AHX passes 18 consumer tests.

Private package `1.5.2-synthetic-dev.33` has SHA-256
`817c0bc8a27e9ec0ad768eeb9b3c39c93f2f487a6fcf978716ab14ee47c127e6`.
The isolated CopperScreen consumer at baseline `d9beae8` resolves that package
through NuGet, builds Release with zero warnings/errors, and passes 149 host,
74 disk and 1,080 separately built diagnostic tests. Six optional host/media
skips remain unavailable coverage. Native replay separately passes Workbench 3.1
at 0/2 MiB Fast RAM and the A1200 eight-plane hard-disk boot with persistence
across a desktop reopen (three executed cases, no skips). These are correctness
results, not throughput or physical-timing qualification. Root consumer working
changes remain outside this validation checkout; the package is not published.

Evidence: `artifacts/m6-final/`, `artifacts/m6-ahx/`,
`artifacts/m6-mutation-proof-verified/`, `artifacts/m6-negative-inputs/`,
`artifacts/synthetic-private-feed-33/`, and the isolated consumer's
`artifacts/m6-validation/`.
