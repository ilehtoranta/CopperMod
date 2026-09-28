# Native monitor-state publication unit

## Resume routing — 2026-09-13

Read GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md CURRENT section for
the sole authoritative live-run and implementation checkpoint. Historical
continuations below do not override it. Native CMDB publication/release and
initializer integration have progressed beyond395; MonitorSpec and Intuition
remain separate required units. Full replacement active/incomplete.

## Historical checkpoint — 2026-09-13 / 395

Real Exec allocation -> host-owned CMDB publication -> native MNTR readback
qualified:8 strict ROM cases pass,0 failed/skipped,1m7s;build PASS23.07s;18009 CLOSED.
Evidence and exact boundary in native-initialization goal current395 section:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-395/exec-owned-monitor-readback.trx
No production changes. Full391 union remains LIVE94441. Native CMDB ownership
publication and Intuition mutation integration remain required; full goal active.

## Historical checkpoint — 2026-09-13 / 394

393 strict Exec provenance FAILED4: AllocMem was overlaid.45128 CLOSED; earlier
392 passes do not prove wholly native allocation.394 restores saved native Exec
vectors in the disposable oracle only, keeps strict checks, and builds PASS22.31s.
12 initializer controls pass. Strict ROM test COMPLETE4 passed,0 failed/skipped,
40s;84603 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-394/original-exec-init-strict.trx
Full391 union remains LIVE94441. Exact source/provenance/next action is in the
native-initialization goal current394 section. No production policy/CPU changes.

## Historical checkpoint — 2026-09-13 / 393

389 full union COMPLETE12990 passed,3 known stale-assertion failures,15 optional
skips,13008total;43703 CLOSED (read final TRX after handle was missing).390 fixed
all3 expectations with38 focused tests passing.391 HUNK initializer integration
passes39 focused tests; unchanged full391 union LIVE94441:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-391/graphics-full-union.trx
No failures reported so far. Do not restart94441 or poll closed43703/93874.

392 real Exec initialization4 cases passed,31s.393 adds explicit original-ROM
provenance checks on6 Exec vectors; strict test is LIVE45128:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-393/original-exec-init-strict.trx
393 initial build encountered unrelated concurrent CPU duplicate-local errors;
retry PASS21.20s without CPU edits. Exact changes/hashes and next integration
steps are in GRAPHICS_LIBRARY_NATIVE_INITIALIZATION_GOAL.md current393 section.
Full goal remains active/incomplete; no user choice or genuine blocker exists.

## Historical checkpoint — 2026-09-12 / 390

389 full union43703 is still live and has reported3 failures. Focused rerun of
the same immutable389 binaries reproduced all3 with detailed TRX evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-389/reported-failures.trx
Two scaffold tests expected DefaultViewPosition.X=0 but got ROM-correct129;
the branch audit expected the old1443068/71649 fingerprint but got the already
qualified1443486/71684.390 updates only these stale assertions (Y=44 too).
390 build PASS20.81s; corrected expectations plus initializer/profile tests38
passed,0 failed/skipped,2s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-390/corrected-expectations.trx
No production changes. Keep43703 running to discover any further failures;
do not claim its known-failing snapshot passes and do not restart it yet.
After qualification, continue native initializer integration per its unit file.

## Historical checkpoint — 2026-09-12 / 389

384 full native admission/profile run COMPLETE:7449 passed,0 failed/skipped,
20m54s.93874 CLOSED. This qualifies384, not later original-Point changes.
389 adds standalone NativeGraphicsLibraryInitializer and8 passing tests;
build PASS40.31s; no existing raster/body/builder behavior changes after388.
See GRAPHICS_LIBRARY_NATIVE_INITIALIZATION_GOAL.md for the actual integration
gap and next implementation steps; real Exec InitResident is NOT yet proved.
Initializer SHA2564564FB1C267EB76DFB66B9E149FFD784669327E88F30ACE9FCD17B28A5A91826
InitializerTests SHA2560FFB6448E40356604E52355452E740EFA3716C90D687E509D953DC2E8D2FF560

389 unchanged full graphics union LIVE session43703, immutable Temp binaries
codex-graphics-runtime-descriptor-20260912-389:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-389/graphics-full-union.trx
This supersedes the planned385/388 broad runs. Poll43703, never93874/46073.
Next connect native initializer selection and prove real Exec initialization,
while collecting the broad regression. Full goal remains active/incomplete.

## Historical checkpoint — 2026-09-12 / 388

386 reproduced the missing DefaultViewPosition field:10 failed as expected,
build PASS20.09s.387 implements separate original-Point publication in portable
and native paths;10 tests pass, with only2 measured fingerprint changes.388
updates the measured fingerprints and adds a portable prefix/provider-read test
plus core guest-original readback assertions. Build PASS20.66s.

GetDisplayInfoData accepts an optional original-position provider, invoked only
when requested output reaches offset80. Core validates owned CMDB and reads the
separate original LONG at currentCell+4; host/legacy fallback is BootDefault.
Native full MNTR captures original into saved D5 before output, including output
overlapping the source original field. Prefixes do not read original at all.
Original and current retain their separate WORD write order; stack unchanged.
This does not implement SetPrefs or alter original storage during updates.

388 current/original/descriptor/core/profile suite COMPLETE:234 passed,0 failed,
0 skipped,1m9s. Session3244 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-388/original-current-matrix.trx
384 full admission/profile suite remains LIVE93874; do not restart it. Once
collected, qualify the current388 source with unchanged full graphics union,
not the superseded planned385 broad run. Resolve failures before qualification.
PAL1443486/NTSC1443386 bytes;71684 branches,3428 relays,7 chains,3346 boundaries.
Production SHA256:
- Database B5815BC26DDBC63071A124E611672964966828C763B61CDE56B932C17FB2C89D
- Core CC939375552BD2342798AD18C0B8D3D09E77870EBD8D849D8C6FF54DEDAD2DE7
- RasterBodies36BE963106DE2550C5979D9E8D3890882F79E6E8C13A42E7F18F2C7EBAADA3CB
- MonitorState21B5EFBA3DA8A8A5B1B2775B9737E0F3BBA095583F0DDCEADB517F544F3E7CF8
Real Exec AUTOINIT and the Intuition mutation bridge remain required. Full goal
is active/incomplete. No user decision or external blocker exists.

## Historical checkpoint — 2026-09-12 / 385

385 expands all12 invalid-ownership scenarios across PAL/NTSC and fixed/relocated
direct/JSR(A2) routes, including candidate-address no-read checks for odd/wrapping
CMDB pointers.32 new tests prove exact byte-ordered odd-address owned prefixes.
Build PASS20.12s. Descriptor suite132 passed,0 failed/skipped,38s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-385/native-descriptor-matrix.trx
Session80278 CLOSED. Production unchanged384.384 full admission/profile suite
remains LIVE session93874 (confirmed this continuation); do not restart it.
Next collect93874, resolve failures, then run unchanged full union on385 binaries.

Rechecked original-ROM365 TRX directly:14 passed; before/after DefaultViewPosition
bytes remain0081002C while current NTSC Point changes to130/46. Portable
BuildMonitorInfo still writes zero at0x50/0x52, as does native full MNTR. After
this reader's broad regression, implement that separate original-Point publisher
from the distinct CMDB original field, with portable/native independent tests.
Real Exec AUTOINIT and Intuition mutation integration remain required too.

## Historical checkpoint — 2026-09-12 / 384

Supersedes lower live-session and implementation claims.374 full union completed:
12829 passed,0 failed,15 optional skips,12844 total,19m18s;46073 CLOSED.
379 exposed hard-coded native Points with40 failing owned-state cases.380 added
the native reader;48 owned/short-prefix cases passed.382 widened coverage and
found16 legacy-envelope regressions plus2 fingerprint changes.383 repaired the
legacy opt-in recognition:141 passed,only2 measured fingerprint changes remained.

The new partial emitter NativeGraphicsRasterBodies.MonitorState.cs validates
descriptor ownership, public-pointer equality before CMDB reads, alignment and
nonwrapping CMDB extent, magic/version/size and selected canonical monitor ID.
It captures currentPoint before output and preserves WORD publication order in
the full record. No extra stack slot or helper return is used. Short17..20 skips
the reader entirely. Invalid recognized descriptors decline transactionally.
Size alone cannot opt in: arbitrary legacy bytes are not a descriptor unless
version1 or CMDO identifies it. If the extension cannot fit without wrapping,
legacy boot behavior remains; no extension read is attempted. This preserves the
existing public-field-only envelope fixtures. Version1 with invalid tag, or CMDO
with invalid version, declines. No MonitorSpec dereference is introduced.

Measured383 profile delta:+412 bytes/+35 branches. PAL1443480/NTSC1443380 bytes,
71684 branches,3428 relays,7 chains,3346 boundaries.384 updates these measured
assertions and adds40 overlap cases (source currentPoint is overwritten by the
output header).384 build PASS18.94s. Full native admission/profile suite is
LIVE session93874, immutable binaries codex-graphics-runtime-descriptor-20260912-384:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-384/native-admission.trx
Poll that session, resolve any failures, then run unchanged full graphics union.
384 source identities (SHA256):
- MonitorState emitter1161883710B20745A02203FC684CE3AA5315FC092F4297B0007174A9BF9F05C6
- RasterBodies FF8F69A9A68BEFF5F60E2FEBECF8B51F5310A0A11741B35B018FB449EFEBD43C
- AdmissionTests2FC33A758428B38C7EC96C355D4AF720EEB77625CD0F3F8A7B6D9BA419C17F5B
- ProfileTests C4D7DA726CA8FFD4D043700EE947AC6CB3C498E7C7F9E9FBFD1D38A29867759E
Real Exec AUTOINIT, originalPoint publication and Intuition mutation bridge remain
required; this does not complete the full goal.

## Latest checkpoint — 2026-09-12 / 378

Native admission fixtures now propagate explicit runtime-descriptor opt-in through
fixed and relocated builders. Eight PAL/NTSC/direct/JSR(A2) cases pass for requests
16..20 at even and odd destinations: exact output, preserved registers/stack,
one native return, no descriptor or MonitorSpec access, and exactly one public
DefaultMonitor read only for17..20. This is not real Exec AUTOINIT qualification.
Build PASS19.68s; focused tests8 passed,0 failed/skipped,2s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-378/native-short-prefix.trx
The first no-restore build failed because the fresh artifact directory lacked
assets; the subsequent restoring build succeeded. Production code is unchanged.
374 full union remains live session46073 at this checkpoint; do not restart it.
Next: seed valid/malformed owned descriptors and CMDB records in native fixtures,
then implement admission and retained Point reads for21..24/full88. The full
replacement goal remains active and incomplete; no user decision is needed.

Status: opt-in positive-image layout implemented in373; descriptor publication,
native consumption is pending. Core descriptor publication/invalidation is
implemented in375;376 qualifies final-tag failure/write-order with41 focused
cases passing.377 qualifies opted-in rebind/provider-replacement with47 focused
cases passing. Native admission tests/read implementation remain next. Builder
propagation is implemented in374. This is part of the full Kickstart3.1
replacement, not an alternative completion target. CyberGraphX is excluded.

## Current evidence and problem

CMDB v2 owns two monitor records (NTSC/PAL): ID, current signed Point LONG,
original signed Point LONG. Portable core reads, updates and lifecycle snapshots
are implemented through371, with35 focused cases qualified in372. Native
GetDisplayInfoData still publishes BootDefault. The host ownership booleans
are not available to 68k code.

Firmware/GraphicsLibraryImageLayout.cs preserves the SDK GfxBase through0x220,
then strings at0x220/0x234, with minimum native image size0x24C. HUNK construction
passes an explicit positiveImageSize and emits code after that image. There is
no private owner descriptor today. Reading a candidate CMDB magic is already
a dereference and cannot itself establish ownership.

## Chosen boundary

373 establishes descriptor offsets: tag0x250, version0x254, owner0x258,
database0x25C, byte-size0x260; required positive allocation0x264. Valid tag is
CMDO/0x434D444F, version1. CreateGuestImage explicitly opts in with
includeNativeRuntimeDescriptor; only version is initialized. Other descriptor
fields stayzero, including owner (no template pointer relocation required).
Opt-out larger images remain unchanged. Strings may not cross0x250 when opted
in. Compact/short/incoherent images are rejected. Build PASS22.29s; full existing
image-profile suite plus new cases23 passed,zero failed/skipped,1s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-373/runtime-image-profile.trx
374 propagates explicit opt-in through fixed/HUNK builders.27 image-profile
cases pass, including opted-in relocated HUNK and resident allocation metadata.
Real Exec AUTOINIT execution is not proved by this metadata check and remains
required. Existing opt-out fingerprints remain unchanged. Current full union
is session46073;369 finished12815passed/0failed/15optional skips. See main goal
checkpoint for full paths/hashes. Next connect descriptor lifecycle to the core.

Add an explicitly opt-in, versioned private runtime extension after the native
positive image, not within a public/reserved GfxBase field. Keep existing compact
and minimum-size native images supported with their existing boot-only behavior.
Do not enable the extended mutable publisher solely because lib_PosSize is large.

The extended image must reserve fixed guest-width fields for a descriptor tag,
descriptor version, owning library base, owned CMDB address and CMDB byte size.
Choose final offsets only after auditing all image builders/string bounds and
relocation paths; arbitrary long ID strings must not overlap the descriptor.
No managed references or host pointer widths may enter this layout.

The builder initializes an inert descriptor; publication initializes CMDB first,
then installs its owned descriptor, with the validity tag published last. Bind
the descriptor to this library instance, and relocate/self-initialize its base
identity in both relocated and Exec AUTOINIT paths. Rebinding, release and failed
publication must invalidate the descriptor before a reachable allocation is freed.
Provider replacement must invalidate/relinquish only our descriptor, never touch
or free the foreign table. All partial-write failure paths require rollback.

## Native read contract

1. Existing public ABI/destination admission remains before output writes.
2. Requests through16 bytes remain read-free;17..20 retain the established exact
   DefaultMonitor field read without reading the runtime extension or CMDB.
3. For21..24 and full MNTR, the opt-in path validates the descriptor envelope,
   tag/version and self-base before consuming its owned CMDB address. Compare
   the public DisplayInfoDataBase pointer against that address before dereference.
4. Enforce alignment and non-wrapping extents before reading the CMDB header and
   selected record. Validate CMDB version/size/monitor ID against the descriptor.
5. Retain the selected Point before output writes, including overlapping outputs.
   Preserve exact byte counts, ordered writes, registers, one RTS and the existing
   maximum stack bound. Preserve foreign/provider declines and nondefault-mode
   ownership rules; do not silently expand accepted payloads.
6. Current Point drives ViewPosition. Original Point drives DefaultViewPosition
   only when that separately tested field is enabled; never alias their storage.

Descriptor validation is an ABI ownership check, not a memory-security boundary
against a malicious guest. Nevertheless, ordinary foreign pointers must not be
read merely to discover that they are foreign. Explicit invalid descriptors and
pointer mismatches must be declined without reading the candidate table.

## Implementation and verification order

- Add shared layout constants and image-builder/relocation tests. Demonstrate no
  change to existing opt-out images and no overlap with strings or native code.
- Connect core publication/release/rebind to the descriptor, with poisoned-memory,
  sparse/partial-write, invalidation-order and foreign-replacement tests.
- Add native admission tests first: opt-out, valid owner, bad tag/version/base,
  mismatched pointer, malformed CMDB, odd/wrapping extents and overlapping output.
- Emit native Point reads and qualify PAL/NTSC, relocated/AUTOINIT,21..24 and full
 88-byte routes. Inspect measured image changes before updating fingerprints.
- Run the unchanged full graphics union and preserve ROM preference regressions.
- Connect the Intuition update boundary to the same selected-monitor state;
  this remains required after native reads pass. Do not infer universal NTSC
  selection from the limited captured fixture scenarios.

Use GRAPHICS_LIBRARY_MONITORINFO_VIEWPOSITION_GOAL.md for the current running
sessions/evidence. Do not mark the full goal complete from this unit's tests.
