# Native default MonitorSpec ownership unit

Status: native default initializer, publisher, quiescent teardown and AUTOINIT composition qualified; current broad regression remains.
Part of the full Kickstart 3.1 replacement; CyberGraphX stays out of scope.
Read GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md CURRENT first to
finish/collect predecessor qualification without restarting live runs.

## Ownership layout update — 2026-09-15 / 427–428

The selector unit supersedes the historical CMMO1 layout below. CMMO2 stores a
registered PAL/NTSC family ULONG at278 and requires lib_PosSize/image extent27C.
The public GfxBase/MonitorSpec and CMDO/CMDB layouts are unchanged. Construction
requires an inert ID before and after allocation, publishes ID before valid tag;
release requires the exact registered ID and clears it before FreeMem. Old CMMO1
is refused rather than inferred/adopted. This enables native ID selection without
using mutable public names/timing flags as registration identity. See selector
unit CURRENT for contract and canonical publication CURRENT for verification.

## Historical implementation checkpoint — 2026-09-14 / 414

BuildWithNativeMonitorSpec composes positive initialization, CMDB publisher,
MonitorSpec publisher and CMDB failure release. Monitor publication is the final
fallible stage: if it declines, its own candidate is already freed; then unwind
CMDB and optionally the actual Exec GfxBase extent. If CMDB release declines,
retain the library for recovery. Direct-call ownership defaults to retention;
HUNK AUTOINIT opts into library cleanup. The incoming library is private and
unpublished, and caller serializes all initialization/ownership changes. This
does not permit allocator callbacks to install foreign owners in that allocation.
D0=GfxBase/A6=ExecBase; return base/zero, preserve D2-D7/A2-A6. Maximum scratch
through the nested monitor initializer is32 bytes (guard at SP-36).

All three HUNK entry points accept a final publishNativeMonitorSpec=false option.
Opt-in requires initialized positive image, CMDO/CMMO descriptors and CMDB
publication; defaults stay unchanged. No public vectors or raster body changed.
99 new focused tests cover PAL/NTSC, OCS/ECS, direct/AUTOINIT/HUNK/raster-HUNK,
success, first/second allocation OOM, invalid monitor candidate cleanup, damaged
database retention, short-base admission, reverse free order and ABI/guards.
Combined414 lifecycle suite499 passed,0 failed/skipped,8s;build19.40s.
Strict12 new original-Exec cases plus26 regression cases COMPLETE38 passed,
0 failed/skipped,4m10s;22034 CLOSED. First/second allocation failures restore
exact AvailMem and leave the failed library unlinked. The canonical CURRENT
checkpoint has all evidence paths. No test sessions remain live.

SHA256:
- MonitorSpec release:327F9BB6D91588BEA57805C25075526A262BF83903EFFAE942A2BA24C7B5C3F4
- Release tests:A11BA8B7E05936FA01B9D41BF9CE7CF7E54C271D1E88ECC81BB1480B33F04B2A
- Library initializer:AE886201CA7A7F3050E29A164432A11C1014B465C9C7C41FA530DC82E6A15DBA
- HUNK builder:164D88F3039C826650B7AB0F0659EE9E847B0B5543DF476B65D412021F43DAD2
- Composition tests:7C290EF1A16E1B0879C1F035B5A5EA89822342420FA692EB27E360A28A1050E8
- ROM composition tests:30B2ACA6FCA09BB199D21D10BEF83E047A5AE386356993947AD7844F09F5D3BF

Next qualify a current broad snapshot, then continue explicit
monitor selection and Intuition/lifecycle integration. Full Expunge, driver
callbacks, general synchronization and PaletteExtra semaphore audit remain open.

## Historical implementation checkpoint — 2026-09-14 / 412

NativeGraphicsMonitorSpecRelease.Build(ntsc) accepts D0=GfxBase/A6=ExecBase,
returns base on release or zero on decline, preserves D2-D7/A2-A6. It requires
serialized lifetime changes, no borrowed references and a quiescent exact native
default resident. Checks cover CMMO identity/extent, public list/default pointers,
node backlink/type/profile/name/self pointers, zero open count/active view/current
monitor, empty embedded list, idle semaphore/no waiters and zero driver callbacks
(including ExtendedNodeInit). CMDO is untouched. Invalidate CMMO first; unlink and
restore inert ownership before FreeMem; no ownership writes after FreeMem.
This is not CloseMonitor or full graphics.library Expunge.

411 build39.93s and212 initial release cases pass.412 adds callback refusal:
build22.15s,400 focused lifecycle cases pass (216 release cases included),5s.
Strict41226 cases pass,2m57s, including native live-reference refusal, exact memory
recovery for MonitorSpec and CMDB, repeated-release refusal and recreation.
66880/1111 CLOSED. Broad410 COMPLETE13241/0/18,19m37s;10313 CLOSED; predates release.
Evidence paths and next action are in the canonical publication CURRENT section.

Next implement explicit native AUTOINIT composition and failure unwind, then
fixed/HUNK original-Exec success and first/second allocation-failure controls.
Keep old inert/default and CMDB-only builders byte-compatible. Full replacement
remains incomplete. PaletteExtra semaphore audit remains a separate follow-up.

## Historical implementation checkpoint — 2026-09-14 / 410

409 publisher/descriptor/initializer qualification passes145 tests,0 failed/
skipped,2s; build retry PASS20.12s. Its first build failure was only the corrected
test WriteByte arity; the premature test attempt found no assembly and ran no tests.
410 propagates includeNativeMonitorDescriptor through fixed/raster/HUNK builders
and both native positive-initializer compositions. Defaults stay unchanged.
This flag initializes an inert descriptor; it does NOT auto-publish MonitorSpec.

410 build PASS22.39s;184 focused publisher/descriptor/initializer/composition
tests pass,0 failed/skipped,4s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-410/monitor-owner.trx
The strict ROM fixture now has four PAL/NTSC fixed/relocated native-ownership
cases: real Exec InitResident, native CMDB and MonitorSpec allocation/publication,
actual monitor-list links, duplicate refusal without allocation, native default
open/close/reopen identity and count, MNTR current/original coordinates and pointer,
and template/original-ROM isolation. There is no host MonitorSpec pointer fixture
or GraphicsLibraryCore owner in these cases. Resident lifetime currently ends
with disposal of the isolated guest; teardown/Expunge is explicitly not claimed.

Strict410 session84403 COMPLETE:26 passed,0 failed/skipped,3m34s. Final TRX
counters independently read. Includes these4,prior2 semaphore and20 initialization
controls, with unchanged original8 Exec vector checks.84403 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-410/native-monitor-owner-rom.trx
LIVE broad410 session10313 uses the unchanged graphics union, no ROM environment:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-410/graphics-full-union.trx
Collect10313 rather than restart. No broad failures reported yet. Strict26 is a
completed result, not a live session to poll.

Current SHA256:
- GraphicsLibraryImageLayout:6C1C36AF07CBEBC49F9972F62275171EC21B6978A29705BDB98B215BF1F0494F
- NativeGraphicsMonitorSpecPublisher:72BEE3C7FEFBCED1BACD3A78CC6D54504DC612B8836FDCAC9E07AEBEA3B391CD
- NativeGraphicsLibraryInitializer:67F2037AFA26B8D30352FA4A86B87E33FFC1DBC5E709ACCCAFB37AD15137D3C4
- Fixed builder:D74469FCABD86B4F26B7C94D359F26001B1AE273F51D759D80AF6B99C12338EA
- HUNK builder:E087F4E9D6C4641149796C30C7CFD53093D64B7A5BD9EF086E15983F07302A70
- ROM tests:0A4F3F9ED81DA8CD59F2482F159358259B664C0EF8B62EB51786A9319A46C6F9

Next after collecting qualification: implement step5 native teardown for a
quiescent exact owned default resident (including the name allocation extent).
Validate CMMO owner/version/size, public pointer identity, exact one-node list,
node backlink and zero open count before writes; refuse active monitor/waiters
or foreign/malformed state. Invalidate/unlink/reset descriptor before FreeMem,
and write no ownership fields after that yielding call. Keep CMDO untouched.
Then step6 AUTOINIT composition must unwind MonitorSpec, CMDB and the actual
Exec GfxBase extent in reverse ownership order on failure. These are distinct
from CloseMonitor(count zero), which retains residency. Caller serialization
and quiescent references remain explicit; general task synchronization is not
proved by these boot-time constructors. Full replacement remains incomplete.

Separate full-goal follow-up: audit other graphics SignalSemaphore initializers
(e.g. GraphicsColorOperations PaletteExtra) against original Exec in their own
unit. The MonitorSpec correction does not by itself qualify those structures.

## Historical implementation checkpoint — 2026-09-14 / 409

The planned descriptor is now implemented: CMMO tag264,version268,owner26C,
allocation270,size274,end278. CreateGuestImage and the standalone native positive
initializer accept explicit includeNativeMonitorDescriptor. CMDO/CMDB layout,
the264-byte image extent and default construction remain unchanged; the two
descriptors can be opted in independently. Both descriptor paths reserve the
native string boundary at250. Templates initialize version only, never ownership.

NativeGraphicsMonitorSpecPublisher now allocates the shared profile-specific
MonitorSpec/name block using original/native Exec ABI, rechecks descriptor/list
admission after allocation, calls the private native initializer, links the node,
publishes head/tailPred/default pointer and commits CMMO last. It never adopts
foreign state and does not touch CMDO or the public display database. Empty-list
admission includes head/tail/tailPred/type/pad and null default pointer. Changed
claims, short envelopes and invalid candidates free only the returned candidate.
D0=GfxBase,A6=ExecBase; return base or zero,preserve D2-D7/A2-A6. Caller serializes
ownership mutations; this is not arbitrary concurrent-task synchronization.

409 tests cover both profiles, two code placements, fresh/foreign/malformed
claims, allocation failure and changed claims during allocation, complete-node
publication ordering, register/stack/heap guards and independent descriptors.
First build failed on one test-only WriteByte call lacking its cycle argument;
fixed. Retry build30978 is in progress. No tests have yet qualified this unit.

Next: collect30978, run the new publisher/descriptor/initializer tests; then
propagate the explicit monitor descriptor through fixed/HUNK builders and qualify
real Exec allocation plus native list/OpenMonitor/CloseMonitor/MNTR use without
the old pointer fixture. Native teardown and AUTOINIT rollback composition
remain required. Follow canonical publication goal for broad regression status:
402/403 completed13108/0/16; no broad run is currently live.

## Historical implementation checkpoint — 2026-09-13 / 408

404 added a strict original-Exec/ROM semaphore comparison. Both PAL/NTSC cases
failed only at the host-created MonitorSpec: original Exec InitSemaphore and
the original ROM resident both use queue countFFFF (-1), while our host prefix
used0000. Build PASS23.95s;2 expected red tests,19s;6218 CLOSED.
405 corrected that host field and the stale scaffold assertion. Build PASS26.52s;
the two strict-ROM cases plus the scaffold control passed3/0/0,20s;90824 CLOSED.
No Exec implementation or CPU behavior was changed.

406 extracted GraphicsMonitorSpecImage as the deterministic shared prefix and
added NativeGraphicsMonitorSpecInitializer. The host initializer retains its
existing admission and byte-transaction rollback, now staging the shared image.
Native image includes a canonical name and padding (PAL172,NTSC176 bytes),
zero resident open count, valid empty display-info list and SignalSemaphore.
Driver callbacks remain zero, not falsely claimed as implemented functions.
Five self-pointer LONGs are rebased; xln_Lib is separately set to GfxBase.
Native ABI: D0=private allocation,D1=bytes,A0=GfxBase backlink; D0=base or zero,
D2-D7/A2-A6 preserved; four scratch bytes. Invalid null/odd/wrapping/short
admissions return without writes; the backlink is not dereferenced. No native
allocation, list publication or ownership is claimed by this initializer.

406 build PASS27.29s. Native initializer + existing OpenMonitor/CloseMonitor/
MonitorSpec tests:241 passed,0 failed/skipped,17s;63162 CLOSED. Includes32
new native initializer cases for both profiles/two code placements/guards/ABI.
407 removed only helpers/constants made unused by the extraction and extended
the strict oracle to execute this initializer on a real Exec allocation. Its
rebased prefix matches host fields after name/count/backlink normalization and
its semaphore matches original Exec/ROM. Build PASS28.06s;2 strict cases passed,
0 failed/skipped,19s;19813 CLOSED.

408 adds original Exec AttemptSemaphore/ReleaseSemaphore twice recursively on
standalone, host and native-initialized semaphores. It checks ownership against
real Exec ThisTask, nesting, and byte-for-byte idle restoration. Original5 Exec
vectors are checked before/after. Build PASS27.57s; strict test COMPLETE:
2 passed,0 failed/skipped,18s;9446 CLOSED. Final TRX counters independently read.
408 reran the same monitor/initializer regression on current source:241 passed,
0 failed/skipped,14s;74750 CLOSED;408/monitor-template.trx. The broad402/403
predecessor sessions remain live as recorded in the canonical publication goal.

Evidence prefix:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-
404/monitor-semaphore-rom.trx;405/monitor-semaphore-corrected.trx;
406/monitor-template.trx;407/native-monitor-template-rom.trx;
408/native-monitor-lock-rom.trx.

SHA256 current sources:
- GraphicsMonitorOperations:571279901700C123893406F8A85E644A90B42585F290F88CD7761B0F19E3A93E
- GraphicsMonitorSpecImage:2AEC572507F7E412698852ADCDF41E37770E57B3C88663B596135C772701B679
- Native initializer:6CEA9831D5C910931AC4D059898CBF8B4F83048E7493411F585928E00F87B0C1
- Initializer tests:E784D2D198BC9B78DFD975CCFE8483DB63DF312A4E1A4995A8A4A7F7C4E64AE6
- ROM test:BCA2B4F4FCC603FE9EB092688B446B6F23F31AD5EF71AA72170DDAEF580EF740

Next: implement step3/4 below. Proposed independent CMMO
descriptor: tag264,version268,owner26C,allocation270,size274,end278. These are
planned offsets, not yet implemented ABI. Preserve CMDO v1/CMDB v2 and the
existing264-byte opt-in path. Add an explicit monitor-descriptor flag instead
of inferring opt-in from size. Constructor must admit a fresh empty list/null
default pointer, privately initialize, recheck after AllocMem, then publish
list/default/ownership with validity last. Full source qualification still
requires a new broad snapshot after the live402/403 predecessors are collected.
No blocker or user decision is required; continue automatically.

## Goal

Create the resident default MonitorSpec and its canonical name entirely in 68k
code, publish it in the native GfxBase monitor list, and exercise existing native
default OpenMonitor/CloseMonitor and MNTR readback without host pointer fixtures.
Use guest SDK field offsets and a position-independent layout; do not embed
host references. Host-native ownership remains a separate valid path.

## Known boundary

GraphicsMonitorOperations.Registry owns host allocations, names, list links and
counts. Its Initialize method builds the 0xA0 public envelope using shared
GraphicsNativeMonitorTiming, including embedded display-info list and semaphore.
Native raster emitters already increment/decrement the default resident pointer;
they do not allocate it. Their OpenMonitor arm currently admits the unnamed
INVALID_ID selector only; do not claim general name/ModeID coverage.
Legacy CMDB-only ROM fixtures retain their separate pointer precondition. The
new native-owned MonitorSpec and full AUTOINIT fixtures allocate and initialize
the node entirely through emitted 68k code and original Exec; they do not use
that host pointer fixture. Native default residency is not general selection.

## Small implementation steps

1. Establish the native boot envelope contract. Read the existing initializer
   and layouts, then verify embedded list/semaphore initial state against original
   Exec InitSemaphore and original ROM MonitorSpec. In particular, independently
   qualify the semaphore queue-count convention before copying host defaults.
   Driver callbacks and private ROM pointers are not constant template bytes.
2. Build a deterministic PAL/NTSC template and explicit guest-pointer fixups:
   name, extended-node GfxBase backlink, embedded list and semaphore sentinels.
   Start resident open count at zero; query publication must not acquire a ref.
   Compare shared public timing fields with the host path, but never let host
   parity replace original-ROM semantics.
3. Add an explicit private ownership claim outside public GfxBase/strings and
   the existing CMDO descriptor. Prefer an independently versioned extension
   over changing CMDB v2's exact size just to store MonitorSpec ownership.
   Final offsets/extent are an implementation decision requiring layout tests.
4. Native constructor: require a fresh empty monitor list and null default
   pointer, allocate/fix up privately, recheck after allocator calls, then publish
   links/default pointer and validity last. Reject foreign state without repair.
   Keep all owner mutations serialized by the lifecycle caller.
5. Native teardown: prove exact owned list/node/name extent and no live open
   references. Unlink/invalidate before freeing and do not overwrite state after
   FreeMem may yield. CloseMonitor at count zero must retain residency; teardown
   is a distinct lifetime operation, not an ordinary CloseMonitor.
6. Compose explicit AUTOINIT: on monitor-allocation failure, release owned CMDB,
   then manually free the Exec library allocation using actual NegSize/PosSize.
   Preserve existing inert and CMDB-only paths. Do not leak on partial failure.
7. Verify PAL/NTSC, fixed/relocated, fresh allocations, name/list/semaphore
   fixups, open/close/reopen identity, saturation/zero boundaries, output guards,
   allocation rollback, register/stack preservation and foreign-owner refusal.
   Run original-ROM Exec allocation/free plus native MNTR/OpenMonitor/CloseMonitor.

## Remains separate

The next selection unit should begin at NativeGraphicsRasterBodies.cs
AppendOpenMonitorDefault and GraphicsMonitorOperations.cs selector resolution.
First qualify explicit IDs/names against original ROM for the already-resident
default family; do not silently allocate other monitors or claim driver support.
Correct the ROM-proven INVALID_ID mismatch (see selection unit417), preserving
reference-count guards, unknown-name refusal and
CyberGraphX independence. Record the exact accepted selector matrix before
expanding native admission. Run the current broad union before changing raster
body fingerprints so constructor and selector regressions remain attributable.

Non-default/name-based monitor selection, dynamic monitor drivers and callbacks,
general synchronization, Intuition preference mutation, complete screen/view
lifecycle and whole-library Expunge remain required full-goal work. This first
default-resident unit must not be described as full MonitorSpec compatibility.
