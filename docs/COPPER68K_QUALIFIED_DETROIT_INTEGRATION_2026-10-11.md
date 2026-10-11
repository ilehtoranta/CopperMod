# Exact-package qualification for Detroit and CopperScreen

The unpublished `1.5.4-qualified-dev.1` candidate passes the approved synthetic
instruction suite and affected CopperScreen consumers. Its production CPU C#
source is unchanged from mainline `5b849dbff704d5a339b64a9ceb46c39b66c5b74e`,
which includes PR #21. Only package metadata and test/documentation files change.

Mainline already passes all ten semantic regressions for Detroit's reached
`EORI.B #$9E,(A0)+` and neighboring postincrement forms. The merged general
logical handler retains its read-modify-write barrier and established operand
ordering. The older dedicated execution/timing patch is not imported. New
sparse-bus witnesses additionally check one write per destination byte, guard
memory, untouched registers, CCR, exact PC and the byte A7 stride.

Validation of the frozen candidate:

- Focused CPU/logical tests: 230 passed, zero failed or skipped.
- Full CPU suite: 5,393 passed, zero failed; 109 optional/deferred tests unavailable.
- Required report gate: 86,663,530 cases in 783 batches, with no required
  mismatches, unsupported execution or untested combinations.
- AHX: 18 passed, zero failed or skipped.
- Clean CopperScreen `b64fc62f86039d357a055dff61d1fd1f7832444f` archive consumes the
  package through NuGet: Release build has zero warnings/errors; host tests 195
  passed with six optional skips, disk tests 74 passed, separately built engine
  diagnostics 1,183 passed with seven optional skips. The default dependency-pin
  update also builds and restores in locked mode; its engine diagnostics pass.
- Four explicitly supplied Kickstart 1.3 CPU cases pass on 68010 batch/scalar
  and 68040 interpreter/JIT, including positive compiled-instruction evidence.
- Detroit stock PAL A1200 normal/scalar replays complete 34,000 fields each.
  All 1,027 captures per mode and final CPU/hardware/output fingerprints match.
  Inspected January report and February management screens show Canadian sales
  £1,250, loss £4,939 and balance £55,061. No guest/media patch or unsupported
  bypass is used. Version-mismatch and incomplete-replay controls fail as required.

The CPU DLL SHA-256 is
`f445a7d289cf6545c46731a0d94e59e7c8c56b189a0c43768f4009874c0cb6f2`.
Its bytes match the full CPU test dependency, NuGet payload, native runner, host
tests and engine diagnostics. Package/input/report identities and exact counts
are retained in the [machine-readable record](COPPER68K_QUALIFIED_DETROIT_INTEGRATION_2026-10-11.json).
The package was frozen before the full run; its original metadata records that
pre-qualification stage. No artifact with that version is replaced.

Acceptance remains [known coverage only](COPPER68K_QUALIFICATION_SCOPE.json), with
all five deferred areas visible and `roadmapComplete=false`. Optional skips are
not passing evidence. This run does not add physical hardware or external-reference
qualification. FPU arithmetic, enabled MMU, physical pipeline/cache timing and
general OS compatibility remain outside the synthetic roadmap. Native evidence
covers the first month, not saves or later gameplay; capture-inclusive throughput
is not a performance result. No package is published or release authorization inferred.
