# MonitorInfo ViewPosition publication unit

Status: boot-state implementation qualified; portable mutable Point ownership
implemented in366. Native storage and Intuition mutation remain unimplemented.
ROM21..24 exact transfers are proved for both
parities and PAL/NTSC boots. Boot X/Y=(129,44).
This is one unit of the full Kickstart 3.1 replacement, not whole-goal completion.

## Current implementation checkpoint

## Resume routing — 2026-09-13

The authoritative active-unit checkpoint is
[Native monitor publication](GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md).
Read its CURRENT section for source identity, live runs, results and next action.
Do not restart a listed live run or infer a blocker from historical text below.
The full Kickstart 3.1 replacement is active/incomplete; CyberGraphX is excluded.
Passing an individual unit is not completion of the full goal.

## Historical execution checkpoint — 2026-09-13 / 395

Strict Exec-init/host-publication/native-readback8 passed,0 failed/skipped;18009 CLOSED.
Full391 union remains LIVE94441. Authoritative status/next action is latest395
in GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md and native-initialization goal.
Older live handles below are historical. Full replacement remains incomplete.

## Historical execution checkpoint — 2026-09-12 / 389

384 admission7449 passed;93874 CLOSED.389 initializer8 passed; full union is
LIVE43703. Authoritative continuation is checkpoint389 in
GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md and the new
GRAPHICS_LIBRARY_NATIVE_INITIALIZATION_GOAL.md. No real Exec initialization
claim is made yet. Full replacement remains active and incomplete.

## Historical execution checkpoint — 2026-09-12 / 388

Current and original MNTR Point publication is implemented;388 build PASS20.66s
and234 focused cases passed,0 failed/skipped,1m9s.3244 CLOSED.384 full admission
suite remains LIVE93874. The latest388 checkpoint in
GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md supersedes the older snapshot below.
Qualify current388 with the unchanged full union after collecting93874.

## Historical execution checkpoint — 2026-09-12 / 384

Native owned monitor Point reads are implemented;384 build PASS18.94s. Current
verification and continuation instructions are authoritative in the latest384
section of GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md. Full native admission
and profile verification is running as session93874.374 full union is complete;
session46073 is closed. Full replacement is still incomplete; do not stop at this
unit or reuse the historical live-session claims below.

## Historical execution checkpoint — 2026-09-12 / 374

This section supersedes every lower execution status and live-session claim.
369 full union COMPLETE:12815 passed,zero failed,15 optional ROM skips,
12830 total,18m21s. Session23291 CLOSED; do not poll/restart it.

374 propagates includeNativeRuntimeDescriptor through fixed/HUNK Build and
BuildFromRasterBodies overloads. Opt-in is explicit, allocation stays explicit.
Build PASS20.10s. Full image-profile suite27 passed,zero failed/skipped,2s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-374/runtime-builders.trx
New cases verify PAL/NTSC fixed/relocated descriptors with synthetic/raster code,
relocated strings, code separation and resident allocation metadata0x264.
They do NOT execute real Exec AUTOINIT; that remains a distinct requirement.

374 ORIGINAL full union COMPLETE:12829 passed,0 failed,15 optional skips,
12844 total,19m18s. Session46073 CLOSED; do not poll or restart. This result
qualifies changes through374 only, not the later native runtime reader:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-374/graphics-full-union.trx
Immutable binaries: codex-graphics-runtime-descriptor-20260912-374 in Temp.
This supersedes planned372/373 broad runs and includes all changes through374.
Current hashes:
- ImageBuilder D8D80C8A1DB182FBF814FF7BE0E1AD867C8E85D8C5C20E73C24E35D6FAC0E384
- HunkBuilder 6EEC49F6863806F79B3CBD23D365F575A0E0DFA793F6CE88FE4AD83B7CA2B576
- ProfileTests 4F47F2F6ACC718030FC80C9F33DB5D2C237A7ABF032F3E970231A5249ED9E812
Other production remains371/373. Descriptor validity/owner/database remain
inert; the core publication lifecycle and native readers are not connected.

375 connects optional descriptor publication/invalidation to owned-CMDB core
lifecycle. Explicit positive-image size plus descriptor version admits opt-in;
legacy images stay unchanged. Payload is written while tagzero, valid CMDO tag
last. Failed writes restore previous descriptor and retain owned allocation.
Release/rebind/provider replacement invalidate our matching descriptor before
clearing ownership/freeing. A failed invalidation keeps the table for retry.
Native emitter and real Exec AUTOINIT are still NOT connected.

Build375 PASS19.73s. Focused suite40 passed,zero failed/skipped,890ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-375/runtime-publication.trx
Five new cases cover success, first-tag/owner/database/size partial writes,
retry and failed invalidation retaining the pointer/allocation.
Core SHA256 E053828A7793A4313E8A52E43C5A3DAFBB1B723789D0ECD5DE31F9439E2FCF86
Scaffold SHA256 D8DF0CEDCB256A047061E629B15D5962148BADD9703E6F97483D6272229A5772

376 adds a value-targeted final CMDO-tag partial-write failure and an exact
ordered LONG-attempt assertion: clear tag, owner, database, size, valid tag.
At the validity attempt the test independently reads all three completed payload
fields. A failed final commit restores inert descriptor bytes, keeps allocation
owned, and permits retry. Original initial-clear failure case remains.
Build PASS20.19s; focused suite41 passed,zero failed/skipped,924ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-376/runtime-final-tag.trx
Scaffold SHA25666A149D22B9A2622B6349CF1CA895C436F46E3CC0F28B0900E84F197D69D2385
Production unchanged375; only the fault-injection test wrapper/test changed.

377 qualifies opted-in rebind and provider replacement through both publication
and release entry points. Six new cases include failed tag clear then retry.
The replacement APTR is deliberately unmapped. Old descriptor invalidates,
foreign APTR survives and is not freed; rebind frees the old owned table and
publishes the new descriptor with the new self-base.
Build PASS20.78s; focused suite47 passed,zero failed/skipped,732ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-377/runtime-transitions.trx
Scaffold SHA256021D67798FC837B7FEAEC8E945DF31421A713EE3B864A5CA53DF233308DA9EF7
Production unchanged375. Descriptor core lifecycle has focused qualification;
native code still does not consume this descriptor or mutable Points.

374 full union session46073 remains verified LIVE; do not restart or count it
as375/377 qualification. Next collect46073 and run original full union on377
(or a later immutable snapshot if native changes are ready). Begin native
admission tests for opt-in descriptor identity, pointer mismatch, malformed
CMDB and overlapping output before emitting Point reads. Final-tag order and
opted-in transition tests are now qualified. Real native init/Intuition
integration remains required after native reads pass.
See GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md for the ownership contract.
Full Kickstart3.1 goal remains incomplete. CyberGraphX excluded.

## Historical verification checkpoint — 2026-09-10

This is the authoritative current checkpoint; all preparation checkpoints
below are historical and cannot override this disposition.

356 ORIGINAL full union is COMPLETE: 12802 passed, zero failed, 14 optional
ROM skips, 12816 total. Source TRX:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-viewposition-20260909-356/graphics-full-union.trx
The original filter was unchanged, with no exclusions. Session95825 is closed;
do not poll/restart it. Boot-state ViewPosition publication is qualified.
Full graphics.library and preference-controlled ViewPosition remain incomplete.

357 normal build PASS (22.14s). Four original-ROM preference probes failed
to reach the direct SetPrefs return sentinel at PC00F81476 (30s total).
This is harness non-completion, NOT evidence of ROM preference semantics.
Before-state captures confirm current and default Point129/44 for all four
queried modes in PAL/NTSC. Retain the failed evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-357/mntr-preferences-rom.trx

358 normal build PASS (19.27s); real-task SetPrefs returned in all four
cases. Two NTSC cases passed; two PAL cases failed in the subsequent direct
GetDisplayInfoData sentinel invocation, not SetPrefs. Session19387 is closed.
Its TRX remains historical diagnostic evidence in graphics-mntr-preferences-20260910-358.

359 keeps SetPrefs and all four subsequent GetDisplayInfoData calls inside
one synchronous Exec task, storing return counts and guarded output in guest
RAM before completion. Normal build PASS (19.00s); original-ROM probe COMPLETE:
4 passed, zero failed/skipped, 47s. Session22671 is closed. Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-359/mntr-preferences-rom.trx
Immutable binaries: codex-graphics-mntr-preferences-20260910-359 in Temp.
Test source SHA256:
325E7F94C36CD407EE5689256B77219CBBF398CF985A472C413943DC2875755B

Observed in this specific ROM fixture, for both PAL/NTSC boots: changing
preferences offsets to(+1,+2) yields NTSC Point(130,46);(-2,-1) yields(127,43).
PAL Point remains(129,44). Mode0 follows its boot monitor, so only the NTSC
boot's mode0 changes. NTSC hires also changes when queried. Every returned
DefaultViewPosition remains(129,44), and every transfer count is88.
This is not proof that SetPrefs universally chooses NTSC: inspect the copied
Preferences monitor-selection fields and fixture defaults before generalizing.

360 preference readback probe COMPLETE: normal build PASS42.36s;
4 passed, zero failed/skipped,52s. Both boots initially return offset bytes0,0
and ViewInitX/Y129,44. Only bytes118/119 change after SetPrefs; all other
232 bytes are unchanged in these captures. Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-360/mntr-preferences-rom.trx
Source360 SHA256132120CC0340C8A853CD861A6EDD22C15EFDAF1EC0ACF9E976AA4CA53E876CB7.

Correction to the previous proposed next step: the actual SDK Preferences
structure has NO explicit monitor-ID selector. Do not invent one or treat
reserved Pad/ext_size bytes as an API. The initial NTSC selection may instead
be Intuition state at this early fixture boot point; this remains unproved.
SDK header: https://d0.se/include/intuition/preferences.h
SetPrefs contract: https://d0.se/autodocs/intuition.library/SetPrefs

361 COMPLETE:12 passed, zero failed/skipped,2m16s. Normal build PASS20.59s.
Session33362 closed; do not restart. Original four, sizes118/119/120 on both
boots, repeated identical full updates on both boots all returned successfully.
Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-361/mntr-preferences-rom.trx
The captured NTSC target stays129/44 for size118; size119 changes only X to130;
size120 changes both to130/46. Two identical full updates still yield130/46,
not131/48. PAL records stay129/44; boot-mode0 resolution remains unchanged.
Source361 SHA256F5ECCB8580099CBC18F1C0C25DA2A30BB96DDC3F954ABD5744D45F2821A5FA09.

362 converts all observed Point outcomes to exact signed-word assertions and
asserts every other Preferences byte unchanged. The assertions explicitly
scope NTSC selection to this early-boot fixture, not a universal ROM rule.
Normal build PASS37.58s. Strict12-case ROM run COMPLETE:
12 passed, zero failed/skipped,2m33s. Session54935 closed.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-362/mntr-preferences-rom.trx
Immutable binaries: codex-graphics-mntr-preferences-20260910-362 in Temp.
Source362 SHA256:
3F3E15582DA8E18A95447441C576FCD1A74DC77A940D59420FED0DE6E0F9265A

363 adds read-only IntuitionBaseGuestCodec observations before/after SetPrefs:
FirstScreen, ActiveScreen, ViewLord.ViewPort/Modes/XOffset/YOffset. No private
Intuition offsets and no guest-state mutation. Build PASS22.65s.
363 COMPLETE:12 passed, zero failed/skipped,2m31s. Session19587 closed.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-363/mntr-preferences-rom.trx
Source363 SHA256:
E1A6714A039DE1B282861616B36C9AA93996F62BF5C92AA9BD6438350945A782
All captures have FirstScreen=ActiveScreen=ViewPort=0 and ViewLord.Modes5000.
These are definitively PRE-SCREEN preference tests. PAL ViewLord offsets
follow copied preferences; NTSC ViewLord remains129/44 in these captures.
That distinction does not change the separately asserted MNTR record values.

364 adds two real-screen observations (PAL/NTSC) to the existing12 strict
pre-screen regressions. Existing SDK NewScreen codec creates a320x200 depth2
custom screen inside the SAME task, before SetPrefs. OpenScreen must return
nonzero. New screen-case Point values are observation-only, not forced to
the pre-screen rule. Original12 retain their exact Point assertions.
364 COMPLETE:14 passed, zero failed/skipped,2m58s. Build PASS19.09s.
Session38468 closed. Both OpenScreen calls returned nonzero; FirstScreen and
ViewLord.ViewPort became nonzero. NTSC MNTR changed to130/46 in both boots;
PAL MNTR stayed129/44. DefaultViewPosition stayed129/44. PAL ViewLord itself
stayed129/44, NTSC ViewLord became130/46. ActiveScreen remainedzero.
Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-364/mntr-preferences-rom.trx
Source364 SHA256F80472903C2383B49C53A31D0FEEDDA1A7C6782BED60A7F49DF1221A509FC3E8.

365 removes the observation-only exemption for screen-case Point values.
All14 cases now assert exact MNTR coordinates, copied preference bytes,
unchanged other preference bytes, default Point, output count and guards.
365 COMPLETE:14 passed, zero failed/skipped,2m37s. Build PASS22.23s.
Session86712 closed. Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-preferences-20260910-365/mntr-preferences-rom.trx
Source365 SHA2562CF64E233155C0168BA011B712401223C680947123A69508B87CA1F868C8DCBA.

366 IMPLEMENTS portable monitor-keyed position ownership:
GraphicsMonitorPositionState stores absolute signed Points per canonical PAL/
NTSC monitor. TrySet rejects default/mode aliases and unsupported monitor IDs;
nullable axes preserve prior values. Each GraphicsLibraryCore owns its state.
Core GetDisplayInfoData now passes a position provider to BuildMonitorInfo;
default aliases resolve using the display profile on READ, never for updates.
Unset records retain BootDefault. The existing MonitorSpec provider/admission
path is preserved. This is a portable internal mutation boundary, NOT SetPrefs
integration or native resident state; those remain required.

Normal366 build PASS21.77s. Focused tests5 passed, zero failed/skipped,280ms:
two boot cases, two mutable-publication matrices and instance/invalid-ID test.
Matrices cover21..24/88 bytes, default/hires/lace aliases, both profiles,
independent PAL/NTSC values, signed words and retained Y-only update.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-mutable-20260910-366/mntr-mutable-focused.trx
ORIGINAL full union COMPLETE (see disposition below), unchanged filter/no exclusions:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-mutable-20260910-366/graphics-full-union.trx
Immutable binaries: codex-graphics-mntr-mutable-20260910-366 in Temp.
Current SHA256:
- GraphicsMonitorPositionState: ABBBEADD2A0BBA77DCEA914E3C14E4F33EB222534CF8D584993E5AB25338043C
- GraphicsDisplayDatabase: E983B72147B4A32B04BE9B210CB5B7307A473229DAB53333110EECA147670329
- GraphicsLibraryCore: 0FD0FE0626157930F4A48869F15B019151D28B6B7171E66D8B5DD88376907FF8
- AdmissionTests: 4006B79B35047578B9015AD0E531277402E5B6A330B1BFF7486B8310AA6192FF

366 full union COMPLETE:12805 passed, zero failed,15 optional ROM skips,
12820 total,17m44s. Session80681 closed. The original filter had no exclusions.
The extra optional skip is the new preference theory without a configured ROM;
365 separately proved all14 strict ROM cases.

367 adds direct core lifecycle/publication tests on both profiles. They verify
MonitorSpec identity survives close/reopen, positions survive monitor close,
partial-axis changes retain the other axis, aliases follow the current display
profile without moving stored values, and a second core starts independently.
Build PASS39.10s. Focused suite7 passed, zero failed/skipped,267ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-mutable-20260910-367/mntr-mutable-core.trx
Scaffold SHA256:
6133D5364E42D4C5E6F797F94A1FB95DA6C8BF48B2B52350E007A13772F24323
367 changes tests only;366 remains the full production qualification.
## Resident layout checkpoint — 2026-09-12

368/369 implement CMDB version2 owned storage. The version1 mode-ID table
retains its16-byte header/four-byte records; a fixed tail after the reserved
full mode-table span contains two12-byte records in NTSC/PAL order:
canonical monitor ID, current signed Point LONG, original signed Point LONG.
The tail adds24 bytes. Both Points initialize to0081002C. OCS/ECS use the
same tail offset; published mode count retains its existing capability rule.
The complete allocation is initialized before the GfxBase APTR is published.
Foreign/provider pointers remain untouched. No conversion of foreign/v1
objects is attempted.

368 build PASS40.24s (pre-final test snapshot).369 build PASS22.39s.
369 focused layout/ownership/rollback suite:26 passed,zero failed/skipped,1s.
Tests cover distinct current/original cells, both capability profiles,
poisoned reused allocations, and partial LONG failure at every field in the
new24-byte tail plus the prior version-field failure. Restore/retry/free
and provider-preservation tests remain passing.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-resident-20260912-369/mntr-resident-layout.trx

369 ORIGINAL full union LIVE session23291, unchanged filter/no exclusions:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-resident-20260912-369/graphics-full-union.trx
Immutable binaries: codex-graphics-mntr-resident-20260912-369 in Temp.
Current SHA256:
- GraphicsDisplayDatabase: AC562398A13271335D46082153C5EEC1AC81B321EB5A78FB7F8E8FC21FF4162A
- ScaffoldTests: 24B1790C648EC8161427297695110EBB764BB6FEB45FAC1A2B6163CDB5B766D0
Other production source remains at366/367.

370 implements the core/resident update bridge. An explicit PAL/NTSC update
on a full native base ensures an owned table exists, validates its pointer,
magic/version/size/record ID, then updates the current Point LONG. A failed
LONG restores the old bytes and does not commit host state. Foreign/malformed
tables reject updates. Original Point is never modified. Resident creation
can seed current Points from existing host state; originals remain boot values.
Portable MNTR publication reads the valid owned guest Point, so guest edits
are visible and a partial-axis core update retains the guest's other axis.
The native emitter itself still uses BootDefault and is NOT yet connected.

Build370 PASS20.63s. Focused suite29 passed,zero failed/skipped,1s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-resident-20260912-370/mntr-resident-updates.trx
Includes direct guest-edit publication, partial-axis retention, partial-LONG
rollback, version1 rejection and foreign-pointer preservation.
Current SHA256:
- GraphicsDisplayDatabase: 8CF5023C25AFDBA65C7DC7C36B77C6421A955D69C5C6A567666E67FA597CD817
- GraphicsLibraryCore: A0CEBAB1912FC106550A1F3C8B3A8785BAEDD17E51313E82E51D90F296896AE6
- ScaffoldTests: 28FAE333AE131B373BB3B51EE2D9741507F0A1E51647F152A84337354BDC62B3

371 fixes guest-edit retention across owned table release/rebind. Both
validated current Points are read before either host copy is committed.
An unavailable/malformed owned table retains its allocation for retry.
The explicit release/rebind path may read our owned allocation when the
published APTR was cleared, but never follows a replacement foreign pointer.
Current positions seed the next publication; original Points stay unchanged.

Build371 PASS19.13s. Focused lifecycle suite32 passed,zero failed/skipped,817ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-resident-20260912-371/mntr-resident-lifecycle.trx
Three new cases cover normal release, external APTR clearing and rebind to
a different full GfxBase, then republish and verify both current/original pairs.
Current SHA256:
- GraphicsLibraryCore: 7177FC6D17DA2D9A197A6576BE83861B32AD7EF25E0F999BA821AB927EEB9E88
- ScaffoldTests: 0D755DAAB36A5636E5714F081F3F51C85FA732C344D95E1C521AEEDB76882124
GraphicsDisplayDatabase remains at370 hash8CF5023C...97CD817.

372 adds malformed-second-record rejection/retry cases to each owned-table
lifecycle path. Failed capture preserves the old published pointer and both
guest Points; repairing the record permits the existing release/rebind flow.
Build PASS23.87s; focused suite35 passed,zero failed/skipped,901ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-resident-20260912-372/mntr-resident-retry.trx
Scaffold SHA256:
68BBEB75DD01B4F7FA39D00CFAF9FCA3403B0A6F32791B1E5EB975E250178B28
Production remains at371.

Native admission investigation: Firmware/GraphicsLibraryImageLayout.cs defines
public native size0x220, strings at0x220/0x234, minimum positive size0x24C.
It has no independent owned-CMDB identity field. GetDisplayInfoData emitter
currently retains the DefaultMonitor APTR without dereferencing it. A native
CMDB reader cannot copy the host's ownership test because those ownership
fields exist only in GraphicsLibraryCore. A CMDB magic check alone already
dereferences the candidate foreign pointer; it is not ownership proof.
Next native step must establish an explicit owned-image/runtime admission
contract before adding those reads, preserving public SDK offsets and the
foreign/provider decline path. Do not repurpose public GfxBase fields.

Native ownership design is now recorded in
GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md: opt-in private runtime descriptor
after the positive native image, separate from public GfxBase fields/strings;
inert initialization, last-step validity publication, instance binding and
invalidation before free. Implement layout/build/relocation tests first, then
core descriptor lifecycle, then native admission/read tests and emitter.
373 implements the descriptor's opt-in positive-image layout (not publication
or native consumption). Fixed offsets0x250..0x263, inert tag/owner/database/size,
version1. CreateGuestImage rejects compact/short/string-overlap cases and keeps
opt-out images unchanged. Build PASS22.29s; image-profile suite23 passed,zero
failed/skipped,1s. Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-373/runtime-image-profile.trx
Layout SHA25691696285F05D63B67F7F180E9CBAF84DE008AEF2FBF0A50929FDE2CB7E7F37AE
ProfileTests SHA256340AB537257F35BAEC1B277FF5A78168BEB7A6BC644CFBFB4232A9DE194B627E
Next propagate explicit opt-in through fixed/HUNK builders and qualify relocated/
AUTOINIT paths, then connect descriptor lifecycle and native reads. Session23291
is still verified LIVE; its snapshot is369, not373.

369 full union session23291 remains verified LIVE (latest poll no new output).
Do not restart it or treat its result as371/372 qualification.
After collecting23291, run the original full union on immutable372 binaries
(supersedes planned371 run; production unchanged). Native full/21..24 work
must preserve read-free<=16 and pointer17..20 contracts. Intuition integration
and other monitor types remain open. Full replacement incomplete.
No user decision blocks continuation. CyberGraphX is excluded.

## Historical353–356 preparation checkpoint

This checkpoint supersedes every historical status below. Previous turn made
verified progress (pointer unit351); this turn implements the captured boot
ViewPosition and starts its qualification. Full Kickstart3.1 remains INCOMPLETE.
CyberGraphX is excluded. No user decision or repeated resume is needed.

Implemented shared signed GraphicsMonitorViewPosition value with BootDefault
(129,44), used by portable and native full MNTR plus native21..24 prefixes.
Native byte publisher stages24 bytes (76 total stack, below118 maximum);
header<=16 stays read-free,17..20 unchanged externally,25..87 remain declines.
No new capability/MonitorSpec-body reads. Preference-controlled resident record
state/mutation is NOT implemented by this boot initializer; the complete
ViewPosition unit remains open even if boot-state tests pass. DefaultViewPosition
at0x50 and non-default monitor registry/lifecycle also remain separate gaps.

Evidence:
- Normal353 build PASS25.40s; mntr-viewposition-red.trx18failed/0passed/0skipped,
  3s: eight missing native partials, eight native full zero values, two portable
  zero values. This is preserved red evidence, not a current passing gate.
- 353 mntr-pointer-viewposition-strict-rom.trx32passed/0failed/0skipped,3m33s.
  Both fields now require successful exact returns at both parities; ViewPosition
  asserts129/44 on both PAL/NTSC. Session54699 CLOSED.
- Normal354 build PASS22.87s; mntr-viewposition-focused-audit.trx50behavior
  passes plus3old-fingerprint failures,12s. Structural checks passed first.
  New measured PAL1443068/NTSC1442968bytes;71649branches,3428relays,7chains,
  3346boundaries. Updated only the measured byte-count fingerprints.
- Normal355 build PASS19.22s. Added signed-word tests and overlapping source/
  destination tests; expanded A6/destination/ownership matrices through24.
  Positive physical write checks still prove every requested byte once;
  only the explicitly overlapping, exactly-once source LONG read is exempt
  from the destination-read prohibition in the overlap test.

355 focused64234 COMPLETE/PASS199/0failed/0skipped,2m24s:
mntr-viewposition-final-focused.trx, covering the expanded boundaries/overlap,
signed Point, DIMS, profiles and structural audits.
Only355 full union80773 remains LIVE (immutable snapshot):
graphics-full-union.trx, ORIGINAL union without exclusions, ROM environment
unset. It has reported one legacy assertion failure so far:
NativeGraphicsGetDisplayInfoDataPublishesCanonicalDtagChunksOnAccurateM68000,
line23740, expected0/actual129. Read and corrected ONLY its ViewPosition
expectations to129/44, as proved by ROM; other field expectations unchanged.
Do not stop or restart80773: collect its complete result and any further failures.

Fresh356 normal build COMPLETE/PASS20.05s with that test correction.
mntr-viewposition-legacy-correction.trx COMPLETE/PASS4/0failed/0skipped,1s,
including the corrected legacy case and all three image/branch audits.
356 artifacts/evidence use the same paths below with355 replaced by356.
Current scaffold SHA256:
8E88189ECC94C1BD933A1E07EE6D323F9B14AF467B4B5C8C0A5A3535D931D69C.
Production and other source hashes below are unchanged. No356 full run has
started yet. After80773 finishes and any additional failures are addressed,
run the ORIGINAL full union on356 (or a fresh build if more edits are needed).
Do not qualify this change from a composite of the failing355 broad run and
focused356 correction; require a complete clean full run.
Next investigate preference-controlled ViewPosition ownership/mutation, rather
than treating BootDefault as a permanent substitute for editable display data.

Artifacts: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-viewposition-20260909-355
Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-viewposition-20260909-355
Prior red/ROM evidence uses the matching353 directory; initial green/audit354.
Source SHA256:
- NativeGraphicsRasterBodies.cs:105D7C22CDFBD44906E39A73B569924DD38AC60C52CDBBF1EAE39C6231030066
- GraphicsDisplayDatabase.cs:CB67DDCB629682D9B7A2D0EE8632F8BC963F895D34935E678B7DDDD033A408AA
- GraphicsMonitorViewPosition.cs:AE2FF0AC233EF338D572E161864D47014FD36CF1C27A7B1F48FD90558F2ED0E0
- NativeGraphicsDisplayInfoDataAdmissionTests.cs:14EFCF227E24E986627CA321F2C1C543D06D789B504D03C1323E11E9FE9664BA
- KickstartRomGraphicsDisplayInfoTests.cs:EC3C6053BC27BE9935A37EB89369B4789273D544725744D603DA230340629013

## Historical design and ROM preparation

### Preference evidence lead — 2026-09-10

The SDK [SetPrefs contract](https://d0.se/autodocs/intuition.library/SetPrefs)
supports partial preference updates and identifies view centering/size as
default-monitor-specific. V36+ fields may be superseded by extended preferences;
do not assume every Preferences member still controls display data.
The [Preferences layout](https://d0.se/include/intuition/preferences.h) contains
signed ViewXOffset/ViewYOffset and ViewInitX/ViewInitY. Observe changes through
original Intuition before choosing how they affect MonitorInfo.ViewPosition;
do not assume simple coordinate addition without evidence.

Existing tests provide GetPrefs invocation examples in
CopperMod.Amiga.Tests/KickstartRomIntuitionBaselineTests.cs around705 and
original SetPrefs entry inspection around1007. Reuse IntuitionLvo definitions
and original-ROM provenance checks. A fresh-machine probe should copy current
preferences first, change only view offsets, suppress notification with Inform
false, then compare default/explicit monitor MNTR Point values and unchanged
DefaultViewPosition. Keep changes inside the disposable guest, never host prefs.
This is a concrete next probe, not observed mutation evidence yet.

Current-state lead for the remaining value owner: GraphicsLibraryCore already
tracks an owned native DisplayInfoDataBase sidecar, publishes it through
TryPublishNativeDisplayDatabase, and preserves foreign/provider APTRs.
GraphicsDisplayDatabase.TryCreateNativeDatabase owns its allocation/layout.
Inspect that existing contract before inventing another GfxBase extension.
This is a candidate dependency, not proof that it supports mutable ViewPosition;
native query publication still uses the boot initializer introduced above.
CyberGraphX is excluded and remains responsible for its own patches.

The [main checkpoint](GRAPHICS_LIBRARY_REPLACEMENT_GOALS.md) governs execution;
no repeated permission/resume request is needed. Read-only audits and ROM/test
preparation may accompany prior-unit qualification. The [pointer unit](GRAPHICS_LIBRARY_MONITORINFO_POINTER_GOAL.md)
is now qualified:351 full regression completed 12772 passed/0 failed/13 optional
ROM skips,12785 total,17m47s. All previous handles are closed; proceed below.

352 build PASSED in37.97s; ROM session11269 COMPLETE:32 passed/0 failed/0 skipped,
3m38s. All16 new size21..24 cases RETURNED exact requested counts at both parities
on PAL/NTSC; reference X/Y=(129,44), bytes0081002C, pointer00C06C30 stable within
boot, no faults. All16 existing strict pointer-return cases also passed.
Production unchanged; ROM source SHA256: `E5C92A0B411E846561DA90A5DB7012BFC1417D48F2D0ECF2CCEA894FE1F27D9C`.
Artifacts: `C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-viewposition-20260909-352`.
Evidence destination: [352 combined ROM TRX](C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-viewposition-20260909-352/mntr-pointer-viewposition-rom.trx).

## Objective and known layout

Implement native `GetDisplayInfoData(DTAG_MNTR)` sizes 21 through 24, extending
the existing twenty-byte header/pointer prefix with the `ViewPosition` Point.
The [SDK displayinfo.h](https://d0.se/include/graphics/displayinfo.h) declares
this Point immediately after `Mspc`: signed X occupies offsets 0x14..0x15 and
signed Y occupies 0x16..0x17. It is preference-editable, unlike the distinct
`DefaultViewPosition` at 0x50. `ViewResolution` begins at 0x18 and is not part
of this unit. Header metadata remains the previously established MNTR shape.

Both `BuildMonitorInfo` and the native full record write X=0/Y=0, now disproved
for observed PAL/NTSC boot states. Full field parity is not qualified; portable
policy is not an oracle. Size21..24 odd support comes from352, not pointer inference.

## Reused archival evidence — 2026-09-09 audit

- [322 corrected fresh-boot capture](C:/Users/vsys-admin/AppData/Local/Temp/graphics-raw-rom-probe-20260905-322/ocs-capture/raw-graphics-ocs-fresh-boot-corrected.trx)
  completed 17 passed, 0 failed. Its nine valid-mode full MNTR records on a PAL
  boot all contain `0081002C` at 0x14: signed ViewPosition=(129,44), not (0,0).
  Modes:0/8000/8020/11000/19000/19020/21000/29000/29020; identical bytes at0x50
  prove a separate DefaultViewPosition mismatch, reserved for its own unit.
- [331 per-tag dense capture](C:/Users/vsys-admin/AppData/Local/Temp/graphics-rom-dense-transfer-20260905-331/rom-per-tag-dense-transfer-observations.trx)
  contains 17 completed passing MNTR cases: 68 size-21..24 observations, all
  copied exactly the requested count (PAL 32, NTSC 36). PAL mode11000 failed
  and contributes no evidence here; whole-file totals remain 61 passed/1 failed.
- [331 all-tag dense capture](C:/Users/vsys-admin/AppData/Local/Temp/graphics-rom-dense-transfer-20260905-331/rom-dense-transfer-observations.trx)
  contains 15 completed passing cases: 60 MNTR size-21..24 observations, all
  exact counts (PAL 24, NTSC 36). PAL modes8020/29000/29020 failed and are
  excluded; whole-file totals remain 15 passed/3 failed.
- The union is 128 observations, 72 unique profile/mode/size combinations:
  all nine modes above on both PAL and NTSC boots, each size21..24. These use
  aligned allocation+2 destinations and assert full-prefix equality and guards.
  These archives have counts, not hex;352 separately supplies NTSC boot values
  and both-parity evidence. Reuse archives without rerunning dense captures and
  retain all failures. Preference-state semantics still require separate evidence.

## Immediate next actions and ROM contract

- Freeze352's diagnostic observations into strict return/count/X129/Y44 assertions
  on both profiles and parities; current diagnostic code still permits an odd
  short/fault outcome. Then add native/portable red value/prefix tests and implement.
- Keep original entries, full88 reference comparison, exact prefix/tail guards,
  and fault diagnostics. Any short return, destination fault, timeout, or host
  Copper reservation failure is now a regression, never a supported contract.
- Compare default aliases with explicit boot-monitor ownership and at least one
  resolution-key variant. This observes field identity, not permission to claim
  non-default native payload ownership before monitor-registry work is complete.
- Preserve pointer identity within each boot, entry provenance, and untouched
  guards. Do not compare resident pointer addresses across independent boots.
- Correct the known PAL/NTSC-boot zero mismatch in portable/native full records
  and new prefixes, using the independently captured values.
  Never preserve a known mismatch merely to keep legacy assertions green.

## Value ownership and preference gap

Boot captures establish only observed state. Determine preference changes to
`ViewPosition` before claiming preference-state compatibility. Do not
infer its source from `ActiView` offsets, `MonitorSpec.ms_xoffset/ms_yoffset`,
or `DefaultViewPosition`; those are distinct fields with unproven relationships.
Record missing mutation/source evidence as an explicit remaining requirement.

Prefer one guest-layout-compatible owner for the Point's value, reusable by
portable and native publishers and suitable for future CopperSharp68k ROM code.
A host-only lookup must not become a required value source. Alternate host and
68k paths are allowed if they implement the same proved state/ownership contract.
Do not silently replace preference-controlled state with a permanent boot literal.

## Admission and implementation constraints

- Resolve authoritative handles before fallback ModeID. Retain the current
  default/private-handle payload ownership and foreign/provider declines;
  non-default monitor registry/lifecycle remains a separately tracked gap.
- Prove the actual output span before any resident-field read. Keep null output,
  wrapping span, rejected owner, invalid A6, and null-pointer paths transactional.
- Reuse the single validated `GfxBase.defaultMonitor` LONG read: field offset
  0x18E, last valid even A6=0xFFFFFE6E, first wrapping even A6=0xFFFFFE70.
  Do not read capability bytes or dereference MonitorSpec merely to copy its
  address. Any new Point state dependency requires its own proved input envelope.
- Keep the shared header-only cutoff at 16. Route the new field through a
  dedicated MNTR publisher, never DIMS scalar/capability or NAME table logic.
  Preserve D1, D2-D7, A1-A6, caller PC/SP, and exact returned count.
- A 24-byte header/pointer/Point scratch uses 76 total stack bytes: caller return
  4 + staged request 4 + public MOVEM frame 44 + scratch 24. This is below the
  existing 118-byte maximum. Prove any alternative stack footprint explicitly.
- Do not widen sizes 25..87, alter full >=88 alignment/span policy, change other
  tags, or fold monitor-registry creation into this field publication unit.

## Required acceptance evidence

1. Add tests-only red cases before production changes across all eight native
   routes: PAL/NTSC x fixed/relocated x direct/function-array entry. Function-array
   tests are not actual Exec AUTOINIT proof. Derive field bytes from independent
   ROM observations and explicit state, never the production publisher.
2. Exercise all four sizes, both destination parities according to ROM evidence,
   null output, exact end, first wrap, and first even wrap. For exact requested
   counts 21/22/23/24, last starts are FFFFFFEB/FFFFFFEA/FFFFFFE9/FFFFFFE8;
   first even wrapping starts are FFFFFFEC/FFFFFFEC/FFFFFFEA/FFFFFFEA. If ROM
   transfers fewer bytes, derive the span from that observed count instead.
3. Cover authoritative/private/null default handles, canonical non-defaults,
   foreign modes, sentinel, all A6 boundary forms, and null/nonzero pointers.
   Prove one complete defaultMonitor read, zero capability reads, and no
   unrequired MonitorSpec-body reads. Test input/output overlap explicitly.
4. Assert exact positive physical write evidence: every transferred byte written
   once in sequential order, no destination reads, no extra writes, and no
   accesses beyond the actual prefix, accounting for 24-bit physical wrapping.
   Clear bounded traces per invocation and reject overflow; snapshots alone
   cannot detect omitted writes when old bytes already equal the expected value.
5. Retain seams 16/17/20/25/87/88/96/UMAX and other-tag controls. Compare portable
   output with an explicit resident monitor provider and independent Point values.
   Require preserved registers, stack, return path, guards, and unchanged declines.
6. Build before running tests. Complete focused behavior/branch audits; update
   only measured fingerprints, rebuild, and run the original full graphics union
   without exclusions. Record ROM skips separately from compatibility evidence.
7. Save TRX paths, counts, build identity, source hashes, exact field-state scope,
   remaining preference/registry gaps, and the next unit in the main checkpoint.
   This correctness gate proves neither throughput nor full Kickstart completion.
