# Native Lemmings gameplay profile

Date: 2026-09-14. Initial diagnostic investigation requested after the
preliminary 271.05 FPS native-gameplay result; that investigation changed no
engine, CPU, device, production-selection or existing runner source. The
subsequently authorized optimization and its measurements are recorded in the
follow-up below. Gate dispositions are unchanged.

## Finding

**CPU interpretation is now the largest measured native-gameplay cost.**
Two independent captures attribute 55.01% and 60.85% of managed execution
samples exclusively to Copper68k methods, excluding their chipset callees.
The machine's scalar CPU stepping adds 7.07% and 5.91%. This is not the old
engine's DMA-dominated profile.

The earlier approximately 450 FPS workload is not a running-game CPU workload:
its synthetic ROM executes **STOP**. The current wide synthetic control also
uses STOP and has no sampled Copper68k execution in its measured interval.
Native Lemmings instead spends the interval executing the scalar CPU path.
The same `cpuMode=conservative-batch` output label does not make these equivalent
CPU workloads. The native path did not use the special NOP/BRA loop admission.

The previous [450 FPS audit](LIGHTWEIGHT_A500_450_FPS_AUDIT.md) already found
447.43 versus 445.31 FPS on matched old work. The 271 versus 450 comparison
therefore does not establish a same-work refactoring regression. These profiles
identify where to seek further native speed; they do not establish that 450
native FPS is impossible, or assign every millisecond of the workload gap to
CPU execution.

## Exclusive sampled execution shares

Each sample interval belongs to one leaf-method category below; columns do
not double-count chipset execution called by the CPU. Percentages are sampled
managed execution time, not exact hardware-counter cycle accounting.

| Leaf-method category | Native A | Native B | Wide synthetic control |
| --- | ---: | ---: | ---: |
| Copper68k interpreter | 55.01% | 60.85% | 0 sampled |
| Machine scalar CPU stepping | 7.07% | 5.91% | 0 sampled |
| Display, bitplanes, sprites | 11.98% | 10.52% | 18.92% |
| Clock advancement / CPU grant | 8.12% | 6.45% | 37.36% |
| Device coordinator, including inlined work | 5.80% | 5.60% | 30.53% |
| Blitter | 4.09% | 2.26% | 3.04% |
| Paula audio | 1.26% | 2.89% | 6.69% |
| Copper | 0.26% | 1.59% | 2.31% |
| Other machine / bus | 2.71% | 2.18% | 1.16% |
| Runtime copies | 3.71% | 1.75% | 0 sampled |

Inlining merges some rendering, device and bookkeeping work into caller
methods. These are symbol-attribution buckets, not pure isolated-device costs.
In particular, absent separate disk samples do not prove zero disk cost.
Small buckets vary substantially between captures; use the stable broad
ranking, not a claim that a 0.5% difference is meaningful. The synthetic control
has different display/DMA activity as well as a stopped CPU; its costs cannot
be subtracted as an otherwise identical native chipset baseline.

## Concrete hotspots and next experiments

1. **Instruction entry, dispatch and prefetch bookkeeping first.**
   `ExecuteSingleInstruction` alone owns 10.35–11.93% exclusive samples;
   `ExecuteInstructionBody` 6.38–8.35%; `TryExecutePlannedKind` 4.98–5.95%.
   The inlined instruction-entry setup resets/captures numerous timing fields.
   Inspect generated code and distinguish indispensable 68000 state from
   optional bus-publication/deferred-batch bookkeeping before simplifying it.
   Do not remove bus phases or interrupt sampling to save these checks.
2. **Investigate CPU value-copy overhead and branch refill.**
   All sampled `SpanHelpers.Memmove` stacks are under CPU methods, principally
   branch refill, retirement and effective-address handling, not framebuffer
   copying. Their combined exclusive share is 1.75–3.71%.
   `BranchToAndRefillTarget` constructs publication-context values using `with`
   and passes them into refill operations. The existing capture helper already
   returns `default` for a plain bus; that does not by itself prove callers
   avoid materializing/copying those values. Generated-code inspection is
   needed to associate individual copy sites and establish removable work.
   This is a candidate, not a proven speedup or permission to discard timing.
3. **Simplify the observed generic unary/EA path.**
   `ExecuteLine4Unary` owns 5.68–6.02% exclusive and 16.19–16.67% inclusive
   samples. Its memory path resolves an effective-address object/value and
   dispatches read/write/flags behavior. Confirm the active opcode/address-mode
   mix before adding a small direct path. Do not assume all this is TST or
   claim the inclusive share is recoverable; it includes real bus/device work.
4. **Display remains the next chipset target.**
   `LightweightBitplanes.Step` owns 7.30–7.95% exclusive samples and
   `RenderHiresCck` 2.66–2.71%. Keep every fetch, shifter transition and output
   pixel when investigating simpler execution. A broad arbitration redesign
   is not the first recommendation from this native profile.

An inclusive CPU number would be misleading: `ExecuteSingleInstruction` is
90.33–90.80% inclusive because CPU bus accesses advance devices. Likewise,
`TickDevices` is 22.86–23.38% inclusive but only 5.60–5.80% exclusive; those
numbers must not be added to the exclusive display/device rows as extra cost.

No optimizations were implemented in this investigation. A follow-up should
take one narrow candidate, retain a frozen reference, compare the identical
native interval and fingerprints, run focused checks plus shared Copper68k
regressions when that core changes, and keep only measured improvements.

## Capture protocol and validation

- Frozen Release engine/CPU/media assemblies from
  `.codex-tmp/lightweight-h6d-slow-window-20260914`; .NET 10. HEAD
  `e9a1ef47444c87da626ed01a62bad4d1670e9d97` plus existing uncommitted work.
- A temporary runner copy adds only an untimed stdin handshake after warmup.
  It references the frozen DLLs directly; no engine or CPU rebuild occurs.
  Clock and hardware do not advance while it waits for profiler attachment.
- Native workload: Kickstart 1.3, both existing Lemmings disks and
  `lemmings-native-level1-exploratory.json`, with hashes recorded in the
  execution plan. Same checkpoint 10,320, then 600 gameplay warmup fields;
  captured workload is fields **10,921–14,520**, 3,600 complete fields.
  No snapshot, media change or input-script change in the interval.
- Each field still generates full 908×313 pixels and real 48 kHz stereo PCM,
  consumed by the existing checksum. No presentation/pacing is added.
- Sequential capture order: native A, native B, wide synthetic control. The
  control uses 300 warmup / 3,600 measured fields with Paula DMA, serial disk,
  RAM disk DMA, CIA TOD, input replay and wide lowres output enabled.
- Fresh topology verification in each run: group 0, P-core efficiency class 1,
  logical CPU 4, mask 16, SMT sibling 5, Normal priority. P logical CPUs 0–15,
  E logical CPUs 16–23. Balanced power GUID
  `381b4222-f694-41f0-9685-ff5bb260df2e`. Host-specific evidence, not defaults.
- `dotnet-trace` sampled-thread-time profile attaches only after warmup. The
  two-second attach pause appears as unmanaged console wait and is excluded.
  Analysis keeps `CPU_TIME` stacks rooted in runner Main; native ExecuteFrame
  filtering produces the same result. Filtering the synthetic capture only
  by ExecuteFrame loses inlined execution, so is not used for the comparison.
- Native managed sampled totals: 13,631.64 and 13,870.81 ms; synthetic 8,944.34
  ms. Host checksum/setup work was not separately resolved by the samples.
  This is a sampled hot-path ranking, not an exact frame-cost ledger.
- Profiled runner rates were 263.14 / 258.38 native and 401.49 synthetic FPS.
  **Do not accept these as throughput measurements:** profiler overhead is
  present and full host-load-policy-v2 telemetry was not collected. The prior
  uninstrumented native median remains **271.05 FPS**, preliminary native-only.

All three processes exit 0, allocate zero measured bytes and report
`unsupported=none`. Native A/B match the uninstrumented run exactly:
cycle `2063321040`, CPU `25B78C17DE5341FF`, hardware `886654CADB84C05F`,
output `D9AED52B4567F91B`, endpoint 284,204 pixels / 1,922 PCM samples.
The synthetic control matches its uninstrumented screening fingerprint:
cycle `554331342`, CPU `3051D82B2D7994D3`, hardware `3B959D7FDCC85DA3`,
output `AF2F22691E456C79`, endpoint 284,204 pixels / 1,924 PCM samples.
Fingerprint equality is a behavior-preservation check, not a hardware oracle.

## Reproduction artifacts

Paths below are repository-root-relative. Temporary profiling helpers and
trace files are local evidence, not new release execution machinery.

- `.codex-tmp/h6d-native-profile-host/`: temporary runner source/project/output.
- `.codex-tmp/profile-h6d-native-gameplay.ps1`: bounded workload orchestration.
- `.codex-tmp/analyze-h6d-profile.cjs`: exclusive/inclusive stack analysis.
- `.codex-tmp/h6d-gameplay-{native-a,native-b,synthetic-wide}-profile-20260914.txt`.
- `.codex-tmp/h6d-gameplay-{native-a,native-b,synthetic-wide}-20260914.nettrace`
  and corresponding `.speedscope.json` files.
- `.codex-tmp/h6d-gameplay-{native-a,native-b,synthetic-wide}-main-analysis-20260914.json`.

| Artifact | SHA-256 |
| --- | --- |
| Engine DLL, unchanged | B63513DF87360DB4A714F1AEF2A1A219DC915DCB5421B7D890124ADC97F01893 |
| Copper68k DLL, unchanged | 5E8A62C4D32FA8D0950157AF84796F989F6E512A5BA622DF836BE29BEFEA0497 |
| CopperDisk DLL, unchanged | 2A684F2261D1FB223FD84E72F71D90B4D3EED3B152A73A0C27285B8C64C89273 |
| CopperFloat DLL, unchanged | 4DCCA673E38589763D9488DD38C34034780944D4DA9BFDA943A34DC1A64AD235 |
| Temporary profile runner DLL | 11A67D1AF56F8C29B0084085CA5F5A51F9BF00AA0A96CADCAB54F589AC1F44E8 |
| Orchestration script | 183AB116AAB03FF5F8EFA9A3518C52141688769952EC7F01A309935FEF31AAFE |
| Analysis script | 1FC248D3431A9CA4DCE77818E36CC22EDF9A8A158817C5416346BFFC4BFA3029 |
| Native A trace | 4DCF681E5126766DAB930AD6E508A69221FAAE17FFBA3A66E36B3A310FB8D61F |
| Native B trace | 606E12ECB7096725FCB966960528EEE0B5F4427FD6CAE38D5A76ECE5D9B5E63D |
| Synthetic trace | 6059308BFF9BD3CF78F54F67A29EF1CB6C70E2E33AB75FE87B20F4E4BBDAB6BE |

The Amiga timing and Copper68k skills kept this investigation observational:
no timing semantics, instrumentation hooks or diagnostic counters were added
to release execution. Existing receiver and other hardware uncertainties stay
open; H6d, historical G6/G7 and production selection are unchanged.

## Optimization follow-up: direct byte-test execution (2026-09-14)

The user authorized optimization after this profile, with simplicity and
correct execution taking priority over logging/testability. No logging or
timing checks needed to be removed from release execution for the candidate
below. The Amiga/Copper68k skills kept physical phases and interrupt sampling
in scope while using existing regression coverage outside the hot path.

### Rejected experiments

Two isolated changes were screened against the frozen CPU with identical
runner/engine/media DLLs, verified P-core placement and host-policy-v2
telemetry. Each used the native 600-gameplay-warmup / 3,600-field interval.
Both preserved all fingerprints and zero allocation but established no speed
improvement; **both source changes were reverted**:

| Experiment | Reference FPS | Candidate FPS | Disposition |
| --- | ---: | ---: | --- |
| Replace three timing/prefetch state-holder objects with direct CPU fields | 273.73 | 273.51 | Rejected, no gain |
| Suppress six instruction-entry snapshot stores for non-consuming plain buses | 272.20 | 270.69 | Rejected, no gain |

These were single-pair screens, not precise regression claims. Logs:
`.codex-tmp/native-cpu-flat-screening-20260914.txt` and
`.codex-tmp/native-cpu-metadata-screening-20260914.txt`. Temporary candidate
CPU hashes respectively `CC0F9A38ED976395DA9C17C136EAC3AB9403F7371B520BAC399D6C9C975424AE`
and `F27FC21156A0FDA4E55771F95C5183CC1435700A40CD44E5F072572145905F51`.
Their frozen directories remain local evidence, not the selected candidate.

### Instruction mix and bounded implementation

A separate host enabled Copper68k's existing instruction-frequency counters
only after native warmup, then ran the same 3,600 fields. This instrumented
diagnostic is not a throughput measurement. It reproduced the normal endpoint
fingerprints and counted **32,524,232 instructions**:

- `TST.B d16(A5)` (`$4A2D`): 12,607,634 executions across the workload.
- At PC `$196E`, that test executes 12,568,033 times; the following conditional
  branch at `$1972` (`$67FA`) executes 12,568,034 times. This one polling loop
  accounts for approximately **77.3% of executed instructions**.
- Source/profile evidence explains the cost: the byte test previously fell
  through planned dispatch into line-4 decoding and constructed a general
  read/write effective-address operand for a simple read-only operation.

The implementation adds `TestByteDisplacement` to the existing opcode dispatch
tables for **exactly eight opcodes, `$4A28`–`$4A2F`**, covering all address
registers. Both kind-table and packed dispatch use the same small helper.
Scalar decoding remains unchanged as a comparison path. There is no new
instruction cache, scheduling mechanism, event state or title-specific address
check. The loop is not recognized or collapsed: every opcode, extension fetch,
operand read and branch still executes normally.

The helper retains extension fetch ordering, signed displacement/address
calculation, fault bookkeeping, the original 4/8-cycle floor updates, byte
read through the normal bus, condition-code effects and general retirement.
The new kind is deliberately **not admitted to fixed-plan loop batching or
the JIT microsequence mapping**. Other TST sizes/address modes and unary
instructions keep their existing routes.

### Validation

- Focused dispatch/byte-test checks: 58 passed. The new exhaustive classification
  check failed before implementation; the five behavior cases passed on the
  original scalar path before the optimization.
- Behavior comparisons exercise kind-table and packed modes, A0/A5/A7, signed
  displacement extremes, odd byte addresses, zero/negative/positive values,
  changed data during polling, delayed extension fetches and data reads,
  condition flags, bus phases, prefetch diagnostics and interrupt samples.
- Lightweight Release: **330 passed**.
- Broader Amiga CPU Release: **1,116 passed, 7 skipped**, matching the pre-change
  result. This includes the shared 68000/020/030/040/CPU-JIT filters.
- Full Copper68k Release suite: **1,476 passed, 6 failed, 6 skipped**. The six
  failures are all existing `OneExtensionRegisterOperationsUseTypedExactDescriptor`
  expectations: six immediate-register forms expect `SequentialOneExtension`
  where the current implementation reports `SequentialOneExtensionRequiredPrefetch`.
  Running those cases against the frozen pre-change CPU reproduced the same
  six failures (and one passing case). They were not changed or quarantined.
  No new failure was observed; this is not a claim of an all-green CPU suite.

The final Release rerun includes the strengthened data-read-delay and carry/
overflow checks and retains **1,476 passed / the same 6 existing failures /
6 skipped**. The focused Debug suite passes **58/58** as well. Final logs are
`.codex-tmp/native-tst-core-final-tests-20260914.txt` and
`.codex-tmp/native-tst-debug-focused-tests-20260914.txt`.
No H6d or production gate is advanced here.

### Retained result: 306.41 FPS native-gameplay median

The repeated same-work comparison supports retaining the direct byte-test
path: **306.41 versus 269.63 FPS, +13.64% throughput**, or **3.264 versus
3.709 ms per complete field**. This saves approximately **0.445 ms/field**.
The earlier 271.05 FPS result is historical; the reference was rerun during
this comparison rather than treating an old measurement as today's baseline.

| Build | FPS samples | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Frozen original CPU | 266.89, 269.63, 272.51 | 269.63 | 2.08% |
| Direct TST.B displacement path | 299.70, 306.41, 312.40 | 306.41 | 4.14% |

Order **R1/C1/C2/R2/R3/C3**, 600 gameplay warmup fields after checkpoint 10,320,
then fields 10,921–14,520, three samples per build. Identical native ROM,
disk hashes, input script, complete pixels and 48 kHz stereo PCM as above;
no profiler or instruction counters in these runs. Each process includes
untimed native boot. No tests/builds/other benchmark ran during the series.

Fresh topology/placement checks confirm logical CPU 4, mask 16, P-core
efficiency class 1, sibling 5 and Normal priority. Balanced power plan as above;
ten-second monitored preflight/inter-sample cooldowns. All placement checks,
host-load-policy-v2 telemetry and spreads pass. No core-isolation, effective-
clock or temperature claim is made. This is an engineering CPU optimization
comparison, **not a Legacy/new-engine acceptance or production cutover**.

All six executions have the unchanged fingerprints: cycle `2063321040`,
CPU `25B78C17DE5341FF`, hardware `886654CADB84C05F`, output
`D9AED52B4567F91B`, endpoint 284,204 pixels / 1,922 PCM samples,
**zero measured allocations** and `unsupported=none`. No frames, reads,
device work or output were omitted. Existing experimental hardware edges
remain open; fingerprint matching does not independently verify them.

The sampled percentages earlier in this document describe the original CPU,
not a new profile of this candidate. Branch/refill handling remains a plausible
next target from the instruction mix, but should be reprofiled before another
change. Neither of the rejected state/metadata experiments is in the retained
source. This follow-up changes CPU dispatch only, not engine selection.

### Evidence locations

- Candidate: `.codex-tmp/lightweight-native-cpu-tst-20260914`.
- Candidate CPU SHA-256:
  `EA0BE0E1089F980E3FD9DF652B53CAAA3A4A9AB0D6865925A60EF93955747332`.
- Reference CPU and all engine/runner/media hashes are unchanged from the
  frozen slow-window family above; original runner SHA-256
  `CF3130EBC617F617D02183CFB18413AEB8EDC2980735C73A1C0865343426FF5E`.
- Frequency host: `.codex-tmp/h6d-native-frequency-host`; output
  `.codex-tmp/native-instruction-frequency-20260914.txt`. Reflection and
  diagnostic counters are confined to this separate diagnostic run.
- Measurement wrapper: `.codex-tmp/measure-h6d-native-cpu-opt.ps1`, SHA-256
  `FB54E5B8F376BD311E97797F86AA6507BCEBE8AFDC89E0FA62E1FF9D1D2FC20B`.
- Measurements: `.codex-tmp/native-cpu-tst-comparison-20260914.txt`.
- Test logs: `.codex-tmp/native-tst-{focused,core,lightweight,amiga}-tests-20260914.txt`;
  original failing-test reproduction
  `.codex-tmp/native-tst-frozen-existing-failures-20260914.txt`.

## Reprofile after the retained byte-test optimization (2026-09-14)

Two new post-warmup captures use the retained `EA0BE0E1...` CPU, the same
native gameplay script and fields 10,921–14,520. Both reproduce the complete
cycle/CPU/hardware/output fingerprints above, zero allocation and
`unsupported=none`. Sampling starts only after native boot and warmup.
The diagnostic host changes only the untimed profiler rendezvous; engine and
media libraries are unchanged. These captures are not acceptance FPS samples.

| Exclusive managed sample category | Capture A | Capture B |
| --- | ---: | ---: |
| CPU interpreter | 51.28% | 56.89% |
| Display / bitplanes / sprites | 12.64% | 10.93% |
| Clock | 10.05% | 9.53% |
| Device coordinator | 5.69% | 4.50% |
| Machine scalar CPU stepping | 5.05% | 5.48% |
| Blitter | 4.64% | 3.93% |
| Runtime copies | 3.57% | 2.93% |

The two samples agree on CPU execution as the largest remaining cost.
`BranchToAndRefillTarget` owns 5.91% / 7.59% exclusive samples,
`ReadInstructionFetchWord` 2.91% / 4.86%, and `ReadPrefetchWord` 2.39% / 3.71%.
Inlining affects method attribution; runtime-copy samples are not all proven
to belong to branches. These percentages are not additive with inclusive
stacks and do not measure the cost of individual hardware devices precisely.

A separate generated-code diagnostic, using the retained CPU and the synthetic
scalar NOP/BRA runner, emits a **5,675-byte Tier-1 branch-refill helper** and
a **2,329-byte full prefetch-top-up helper**. The branch helper has a 0x478-byte
stack reservation and duplicated inlined prefetch machinery. This motivates
one bounded experiment: let the JIT choose whether to inline the full
`TopUpPrefetchOne` overload instead of forcing it. Its by-value signature,
every physical fetch, pending-state update and interrupt ordering stay intact.
Generated-code size alone is not evidence of a speed improvement.

Evidence: `.codex-tmp/h6d-tst-{a,b}-profile-20260914.txt`,
`.codex-tmp/h6d-gameplay-tst-{a,b}-20260914.{nettrace,speedscope.json}`,
`.codex-tmp/h6d-tst-{a,b}-analysis-20260914.json`, diagnostic host
`.codex-tmp/h6d-tst-profile-host`, and
`.codex-tmp/native-branch-baseline-jit-20260914.txt`.

### Retained compiler-policy change: 347.58 FPS

Removing only `AggressiveInlining` from the full `TopUpPrefetchOne` overload
improves the matched native workload by **12.11%**, from **310.03 to 347.58 FPS**
(**3.225 to 2.877 ms/field**, saving **0.348 ms/field**). This comparison reruns
the retained byte-test CPU; its earlier 306.41 FPS median is historical, not
the reference denominator for this result.

| Build | FPS samples | Median FPS | Spread |
| --- | --- | ---: | ---: |
| Retained byte-test CPU | 304.48, 310.03, 311.44 | 310.03 | 2.24% |
| JIT-selected full-prefetch inlining | 345.73, 347.58, 351.64 | 347.58 | 1.70% |

Order R1/C1/C2/R2/R3/C3; same 600-gameplay-warmup / 3,600-measured-field
interval, native ROM/disks/script, complete pixels and 48 kHz stereo PCM.
Fresh topology confirms P-core logical CPU 4 / mask 16, sibling 5, efficiency
class 1, Normal priority and Balanced power plan. Full host-policy-v2 telemetry
and both spreads pass. No concurrent test/build/benchmark ran in the series.
Every sample retains cycle `2063321040`, CPU `25B78C17DE5341FF`, hardware
`886654CADB84C05F`, output `D9AED52B4567F91B`, zero measured allocation and
`unsupported=none`.

This is **not a new prefetch implementation**. The JIT may still inline this
method when its heuristics favor doing so. No statement, bus call, signature,
pending operation, interrupt sample or release diagnostic was removed.
The diff against the retained byte-test source is one deleted attribute.

The narrower hypothesis that this must shrink the branch helper is **not
established**: the matched synthetic ROM-loop diagnostic still emits a
5,675-byte Tier-1 branch helper (full top-up 2,317 bytes versus 2,329 before).
Thus the observed gain is attributable to changing the compiler's inlining
policy across call sites, but its exact native hot-caller/code-layout mechanism
has not been isolated. Do not claim that the branch helper became smaller.
An initial candidate diagnostic accidentally used the default Chip-RAM loop;
its different fingerprints and code sizes are not compared with the ROM loop.
The corrected ROM-loop capture matches the baseline diagnostic fingerprints.
Neither diagnostic's FPS is acceptance evidence.

Focused Release branch/prefetch/byte-test checks pass **91/91**. The full CPU
Release suite remains **1,476 passed, the same 6 existing descriptor-label
failures, 6 skipped**. The failure identities match the prior frozen-baseline
reproduction; this is not an all-green suite claim.

Broader Amiga CPU Release checks pass **1,116 with 7 skipped**; Lightweight
Release passes **330/330**; the same focused CPU checks pass **91/91 in Debug**.
The normal Release runner rebuild succeeds. The existing package advisory
warning is unchanged; no dependency update was included in this optimization.

The freshly rebuilt normal runner also completes the same native interval with
all four fingerprints unchanged, zero allocation and no unsupported features.
That final smoke check is unpinned and has no host telemetry: its FPS is not
used for comparison. Its CPU hash matches the frozen measured candidate, but
rebuilt engine/runner identities differ from the held-constant benchmark DLLs:
engine `FCF0A055F99E055E45948577F351CDEB8DCA643255994E1CB0F405F6EBE1C558`,
runner `3809993BBC11A63168B7F6FAD85C37F2AC59DE12F78C2FDA6D28595CED56DF59`.
The 347.58 FPS result belongs to the frozen candidate identified below, not
to a formal repeat of this final rebuilt assembly set. Smoke-check evidence:
`.codex-tmp/branch-inline-rebuilt-runner-validation-20260914.txt`.

Evidence:

- Candidate `.codex-tmp/lightweight-branch-inline-20260914`, CPU SHA-256
  `F9C16D9746DF10DDD543A679982AB2379668D35FB6A3B18545114A7BE1539FB3`.
  Runner, engine and media DLLs are identical to the retained byte-test family.
- Reference `.codex-tmp/lightweight-native-cpu-tst-20260914`, CPU SHA-256
  `EA0BE0E1089F980E3FD9DF652B53CAAA3A4A9AB0D6865925A60EF93955747332`.
- `.codex-tmp/branch-inline-comparison-20260914.txt`; measurement wrapper
  `.codex-tmp/measure-h6d-branch-inline.ps1`, SHA-256
  `E5678E8E729941CB3DE5D395A0093C4C5AF564DBD489FA65E98F322AB19D3C1D`.
- `.codex-tmp/native-branch-candidate-rom-jit-20260914.txt` (corrected ROM
  diagnostic); `.codex-tmp/native-branch-candidate-jit-20260914.txt` (unmatched
  Chip-RAM diagnostic, not comparison evidence).
- `.codex-tmp/branch-inline-{focused,core,amiga,lightweight,debug}-tests-20260914.txt`
  and `.codex-tmp/branch-inline-runner-build-20260914.txt`.

H6d hardware uncertainties, matched Legacy acceptance and production selection
remain unchanged. This completes the bounded optimization pass; next return
to H6d readiness and the matched complete-workload comparison, rather than
opening another chipset redesign on the strength of a CPU profile.
