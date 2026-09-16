# G6/G7 host measurement policy v2

Owner approved replacing the overly sensitive background-process rule on
2026-09-07. Normal desktop applications, including ChatGPT, are part of the
supported measurement environment. No application-name whitelist is used.

## Unchanged acceptance requirements

- Verified P-core, Normal priority, recorded topology/affinity/build/power plan.
- 600 warmup + 360 measured frames, three samples per mode, interleaved
  candidate/legacy/legacy/candidate/candidate/legacy.
- Candidate median >=45 FPS; either mode's spread <=10% of its median.
- Exact per-mode correctness/allocation fingerprints and separate correctness
  gates. Stable fingerprints do not waive host or performance requirements.
- Production remains Legacy until the required gates and cutover approval hold.

## Revised interference rule

Another test host, explicit build, or benchmark workload invalidates a series.
Idle reusable build-server nodes alone are not treated as running builds.
Ordinary background applications are allowed. A series is INVALID / RERUN when
any one of these signals stays at or above its limit for 10 continuous seconds:

| Signal | Limit |
| --- | --- |
| Selected logical CPU activity excluding the pinned benchmark | 25% of that logical CPU |
| Activity on any SMT sibling | 25% of that logical CPU |
| Aggregate CPU activity excluding the benchmark | 25% of total logical-CPU capacity |

For example, one fully busy background thread on a 24-logical-CPU host contributes
about4.17% aggregate utilization, not100%. It does not invalidate the sample
merely by existing on another core. Its sustained occupation of the protected
core or sibling still can. Process affinity is eligibility, not evidence of
where a background process actually ran.

Counters are sampled approximately every two seconds, using monotonic wall
time for duration. The separate duration accumulators reset when their signal drops
below its limit. Preflight covers at least10 seconds; cooldown remains10 seconds.
Affinity and priority are checked during each live sampling interval as well
as at launch. Host telemetry includes warmup and the final partial interval.
No accepted sample is selected by ignoring a known contaminated interval.

The aggregate signal is a CPU-pressure proxy, not a thermal/power sensor or a
proof of interference. Thermal/clock telemetry is not currently available in
this runner. Repeatability and the absolute FPS requirement remain safeguards;
the numerical contention limits are an operational policy, not a hardware law.

## Counter implementation and failure handling

The helper uses Windows native per-logical-CPU idle/kernel/user accounting via
[SystemProcessorPerformanceInformation](https://learn.microsoft.com/windows/win32/api/winternl/nf-winternl-ntquerysysteminformation).
Utilization is1-idle/(kernel+user), using deltas; kernel includes idle time.
Each logical CPU uses its own accounting denominator because tick publication
can differ across sleeping cores. Stopwatch wall time determines sustained
duration and process CPU percentage. Native API/layout failures fail closed.
It subtracts the pinned benchmark process CPU-time delta from the selected CPU
and aggregate totals, but not from its sibling. It does not infer placement
from another process's affinity mask or classify ChatGPT by name.

Missing cores, stale/nonmonotonic counters, gaps over10 seconds and unsupported
multi-group mappings fail closed. Percentages retain fractional precision.
The initial WMI implementation was rejected in preflight53834: one idle delta
was2.28125s while its provider timestamp advanced2.1818499s. It was replaced,
not granted a larger tolerance. Native accounting rejects idle deltas greater
than kernel+user deltas; no WMI rounding allowance remains.

Implementation: `scripts/agnus-g6-host-load.ps1` and
`scripts/run-agnus-g6-controlled-performance.ps1`. Tests:
`scripts/test-agnus-g6-host-contention.ps1`.

Validation:16 deterministic tests cover core/sibling/package
boundaries, burst reset, fractional counter math, missing/stale telemetry,
independent per-core ticks and workload identification. Original15-case WMI
evidence remains separate from the native revision. Five consecutive native
live intervals completed with no stale/invalid counter or gap. These checks
validate monitoring plumbing, not emulator performance.

Historical attempts23084/85028 remain INVALID under their original rule. They
are not reclassified as passes: a fresh complete series under v2 is required.
This policy change does not accept G6 or perform a G7 cutover.
