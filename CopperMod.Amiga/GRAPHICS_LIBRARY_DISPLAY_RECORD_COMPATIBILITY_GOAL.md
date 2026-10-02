# Kickstart display-record compatibility corrections

## Current continuation checkpoint — 2026-09-09

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

## Historical pointer qualification checkpoint — 2026-09-09

This is the authoritative checkpoint. All sections explicitly marked historical
below are retained evidence, not current instructions or blockers. Continue the
bounded implementation without requesting another resume. The full Kickstart
3.1 graphics.library goal remains INCOMPLETE; CyberGraphX is excluded.

OUTCOME: MNTR17..20 bounded native pointer publication is QUALIFIED on the
saved351 snapshot, including aligned DIMS regression coverage. The ORIGINAL
graphics-full-union.trx completed12772passed/0failed/13optional-ROM-skips,
12785total,17m47s; session79169 is CLOSED. Focused351 passed169/0failed/0skipped
in2m31s. No build/test handles remain live. No353 build or extra source edits
have occurred. The saved fingerprints and source/DLL hashes below identify
the qualified snapshot. This does not qualify full MNTR field parity or the
entire Kickstart3.1 replacement.

NEXT: continue GRAPHICS_LIBRARY_MONITORINFO_VIEWPOSITION_GOAL.md directly.
Freeze352's observed strict21..24 return contract at both parities and boot
profiles, with ViewPosition=(129,44); capture native/portable red tests, then
implement the value/Point owner. Preference-state ownership/mutation and
non-default monitor lifecycle remain separate gaps. Do not request another
resume or mark the whole goal complete.

### Completed implementation and evidence sequence

The shared test-build blocker is CLEARED. Removed only the redundant local
AmigaBus/OcsDisplay aliases in BitplaneStorageStopTests.cs; their identical
global aliases remain. No test logic or unrelated Intuition/hardware behavior
was changed for this cleanup. The normal full test-project Debug build using
SDK10.0.301 and fresh349 artifacts PASSED in33.09s, with no exclusions or
project-reference bypass. The old approval wait is no longer a continuation
condition.

Completed349 evidence:
- dims-aligned-final.trx:137passed/0failed/0skipped,2m20s. This includes the
  eight whole-physical-access guard cases and the aligned DIMS/profile/branch
  checks. Qualified fingerprints:PAL1442944/NTSC1442844bytes,71642branches,
  3428relays,7chains,3346boundaries.
- mntr17-20-dims-owner-rom.trx:17passed/0failed/0skipped,2m3s. All16 original
  ROM MNTR cases RETURNED exactly17..20 bytes for both destination parities
  and PAL/NTSC. Each prefix matched its own prior aligned88-byte record,
  including resident pointer00C06C30; no vector3 occurred. The separate DIMS
  owner00021440 cold boot passed its full88-byte query and sizes16..65.
  This fills that owner's transfer evidence; it does not erase the earlier
  dense/paired Copper reservation failures or qualify long-run hardware timing.
- mntr17-20-native-red.trx:16failed/0passed/0skipped,4s, as expected before
  implementation: each route lacks the required defaultMonitor field read.
  This is a captured red baseline, not a passing native publication gate.
All349 builds/tests are COMPLETE; no349 session handles remain live.

Artifacts:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-pointer-20260909-349
TRX evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-pointer-20260909-349

MNTR17..20 implementation is COMPLETE and QUALIFIED by351 below.
The dedicated default-pointer publisher accepts both destination parities after
exact-span proof. Destination/ownership, explicit portable-provider comparison
and legacy full-span controls are complete: legacy sizes16,17..20,21,87,88,89,
96,UMAX preserve their separate ownership/count contracts.

Build350 normal full test-project Debug build PASSED in21.13s. Its focused
35-case run completed in9s:32behavior passes and3old-fingerprint failures.
The32 comprise three eight-route pointer matrices plus eight legacy routes.
Structural audits passed before the expected tuple comparisons. New measured
fingerprints are PAL1443060/NTSC1442960bytes,71649branches,3428relays,7chains,
3346boundaries. Only the two measured fingerprint tuples were updated.
Those three stale-tuple failures are resolved history: final351 focused and
full-union gates pass with the measured tuples.

350 original-ROM return-contract rerun COMPLETE:
mntr-pointer-rom-return-contract.trx:16passed/0failed/0skipped,1m56s.
All16 rows now explicitly require successful return, freezing the observed
PAL/NTSC aligned/odd sizes17..20 contract.

351 initial normal build PASSED in21.26s. Independent review then strengthened
all three pointer matrices with AssertExactPointerPrefixWrites: every output
byte must be written exactly once in sequential order, with zero output reads.
This prevents stale bytes from hiding omitted writes. No production code
changed for the guard. With no tests live, the normal351 rebuild PASSED
in10.56s. Focused session24951 is COMPLETE/PASS:mntr-dims-final-focused.trx,
169passed/0failed/0skipped,2m31s, covering pointer/legacy plus DIMS, whole-access
guards, full profiles and branch audit. Do not poll this completed session.
The ORIGINAL full graphics union session79169 is COMPLETE/PASS:
graphics-full-union.trx,12772passed/0failed/13optional-ROM-skips,12785total,
17m47s, with no exclusions. ROM environment was unset for this broad run;
configured350/352 ROM results remain separate evidence. This qualifies the
saved351 bounded pointer publisher and aligned DIMS regression, not full
MNTR field parity or full Kickstart3.1 compatibility. Retain351 artifacts.

351 artifacts:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-pointer-20260909-351
351 TRX evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-pointer-20260909-351
350 ROM TRX:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-pointer-20260909-350/mntr-pointer-rom-return-contract.trx
351 source SHA256:
- NativeGraphicsRasterBodies.cs:
  8513E514866EECB126836D3FEEBF9BA1AD9D90C504E4165722D16F2B6F499A94
- NativeGraphicsDisplayInfoDataAdmissionTests.cs:
  9F0AC6A26DCAFDF5CD9E0A84961E7CD54A712DAB61BDCCACDB617E5402F45C3D
- KickstartRomGraphicsDisplayInfoTests.cs:
  90C8D5ACEDF3ABCAF29E9F9F565AD60E656210A8092AB4C7D12DC7475FEB17EF
351 DLL SHA256:
- CopperMod.Amiga.Tests.dll:
  E0FFBF9DB5AB8765902E71FC0007E00972C4AA2F935BE5B8C222DFA76034E34B
- CopperMod.Amiga.Emulator.dll:
  A06EAF20026800BF4347298BF561ACDF49EAE3495DE848298B9A093E2A0916AF

Read-only archival analysis and next-field ROM/test preparation ran in parallel
with351 broad qualification, which has now passed. Fresh352 normal full build
PASSED in37.97s; no next-field production implementation was included.
mntr-pointer-viewposition-rom.trx COMPLETE:32passed/0failed/0skipped,3m38s.
The16 existing strict pointer cases passed. All16 new ViewPosition observations
RETURNED exactly21..24 bytes for PAL/NTSC and both destination parities; each
full Point was X129/Y44 (0081002C), and no vector3 occurred.
This exposes the current zero-valued full-record Point mismatch on both boot
profiles. The new odd-Point helper is still diagnostic/permissive: the next
step must freeze the observed strict-return contract before implementation.
Archival331 analysis separately supplies72unique passing aligned
profile/mode/size combinations; do not erase the source files' retained failures.

352 is TEST-ONLY preparation, not replacement qualification of351.
Native production and admission test hashes are unchanged from351 above.
KickstartRomGraphicsDisplayInfoTests.cs SHA256 for352:
E5C92A0B411E846561DA90A5DB7012BFC1417D48F2D0ECF2CCEA894FE1F27D9C
352 artifacts:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-viewposition-20260909-352
352 TRX:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-viewposition-20260909-352/mntr-pointer-viewposition-rom.trx
All349..352 build/test handles are CLOSED. No353 build or extra edits exist.

NEXT: follow the ViewPosition continuation above.351 is the latest completed
passing broad graphics gate;342's12724pass/9optional skips remain earlier
snapshot evidence, not current counts. The next Point unit is not implemented
or qualified; its boot values and both destination parities are observed,
but preference-state ownership and mutation remain unproved.
MNTR21..87, non-default monitor registry/lifecycle, exact malformed-buffer
behavior, actual Exec AUTOINIT, Coerce/general geometry and the remaining
graphics.library units stay open; focused success is not whole-goal completion.

## Historical shared-build blocker — 2026-09-08

Archived checkpoint only; the2026-09-09 checkpoint above clears this blocker
and supersedes its approval/resume directions.

Authoritative checkpoint. Goal status was set to BLOCKED after the same shared
test-compilation blocker persisted across three consecutive goal turns.
The full objective is unchanged and is NOT complete. No build/test is live.
Previous turns made implementation/test-preparation progress; further verified
work now requires approval or the external owner correcting this file.

Fresh normal348 tests build59468 COMPLETE/FAILED14.19s. The earlier Intuition
errors have cleared. This build reported the two unrelated alias errors plus
one new guard-test accessibility error. Fixed the latter within our scope:
the public xUnit method now accepts literal byte widths, not the internal
AmigaBusAccessSize type; the eight intended cases are unchanged.
Normal348 rebuild COMPLETE/FAILED3.07s with ONLY these reported errors:
CopperMod.Amiga.Tests/BitplaneStorageStopTests.cs:5-6,CS1537,
local AmigaBus/OcsDisplay aliases duplicate existing GlobalUsings.cs aliases.

An approval question to remove ONLY those two redundant local aliases remains
unanswered. Do not treat automatic goal continuation as approval and do not
change unrelated Intuition/hardware code. Do not hide the failure by excluding
tests or claiming an older --no-build result qualifies current sources.
Native production remains unchanged; MNTR preparation and DIMS verification
requirements from the next historical checkpoint remain pending.

Current admission-test source SHA256:
F033BE58EFCCED8F1C809538CB35DDFF9A2A1A0F86D14508131C6C28CCEEE83E
Artifacts of the retained failed attempt:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-pointer-20260908-348

RESUME AFTER APPROVAL OR EXTERNAL CORRECTION:
1. Read the current file; if approved and still present, remove only the two
   duplicate local using aliases. Preserve every other change.
2. Make a confirmed normal Debug build in fresh349 artifacts using SDK10.0.301.
3. Execute the prepared ROM MNTR observations and single-owner DIMS capture,
   DIMS/profile/branch focused tests, MNTR/overlap red matrix, then continue the
   implementation/evidence order in GRAPHICS_LIBRARY_MONITORINFO_POINTER_GOAL.md.
4. Run the original full graphics union without exclusions; keep live snapshots
   immutable and record current process handles/results. Do not call the whole
   goal complete from this one field or from focused/compilation evidence.

## Historical MNTR preparation checkpoint — 2026-09-08

Authoritative continuation checkpoint; supersedes historical statuses below.
Goal ACTIVE. Previous turn was progress (DIMS implementation/ROM fault evidence);
this turn added concrete test coverage and obtained a successful production
build. No graphics job remains live. No MNTR production code has changed.

Updated GRAPHICS_LIBRARY_MONITORINFO_POINTER_GOAL.md with the reviewed exact
implementation seam, A6 boundaries, ownership limits and acceptance sequence.
Added16 original-ROM MNTR17..20 rows:PAL/NTSC x aligned/odd x four sizes.
Each compares returned bytes to its own original aligned88-byte record, or
captures a real destination-write vector3; timeout/host failures are not passes.
Added16 native red rows for literal pointer bytes and complete A6 LONG envelopes.
These tests are UNRUN. First establish ROM outcomes, capture native red baseline,
then emit the bounded pointer-prefix publisher; do not assume odd legality.

Read-only review found start-address-only trace checks could miss overlapping
WORD/LONG accesses. AssertNoDestinationAccess and the new exact field-read check
now examine every transferred byte, including24-bit physical wrap. The pointer
body guard covers0xA0 bytes. Eight pure overlap cases added; all require testing.
Do not cite the older342 broad run as qualification of these stronger guards.

Current NativeGraphicsRasterBodies.cs remains unchanged SHA256:
40E055D14D631D491F32D2FE8DC419FD493B09DF2D8F78C993337DADA70928F5
New admission tests SHA256:BF510CB83B48C9C71573118FB27C31864120823F42915287DEA3156332A9DB7D
New ROM capture source SHA256:81D42CA8818E547C1773FBE4BD8DFA009F1256BF1CBB983AAB724C0403C77BA7

Build evidence:
-346 normal tests build17509 COMPLETE/FAILED14.24s: external Intuition owner
 generic IsWritable reference; no tests ran.
-347 normal PRODUCTION Emulator build80471 COMPLETE/PASS11.85s,zero warnings.
 This is compilation evidence, not a test gate.
-Subsequent347 normal TESTS build COMPLETE/FAILED2.95s on another concurrent
 external edit: IntuitionBoopsiOwnedEntrypoints.cs:72,CString.ToUInt32 missing
 required value argument. Preserve other tasks' work; do not repair it here.
-The independent test-alias issue also remains: local AmigaBus/OcsDisplay
 aliases in BitplaneStorageStopTests.cs duplicate GlobalUsings.cs. A nonblocking
 approval question requested permission to remove ONLY those two local aliases.
 No answer was received at this checkpoint; do not assume authorization.
The shared tree is changing; these errors are not grounds to change graphics
semantics or weaken tests.347 artifact path:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-mntr-pointer-20260908-347

NEXT:
1. Check any approval reply and current shared build state. If approved, remove
   only the two redundant aliases; leave unrelated Intuition edits untouched.
   Rebuild normally in a fresh348 artifact directory with SDK10.0.301.
2. After a confirmed successful build, collect the original-ROM MNTR observations
   and single-owner DIMS00021440 capture. Run the DIMS/profile/branch focused
   union (three audited fingerprint updates still need a passing build/run).
3. Run MNTR pointer-prefix/whole-access guard red tests before production edits.
   Use their results and ROM observations, finish destination/ownership cases,
   then implement the reviewed17..20 publisher. Leave21..87 and non-default
   registry lifecycle for their own units; those remain full-goal obligations.
4. Run the ORIGINAL full graphics union without exclusions on the qualified
   DIMS snapshot; keep active binaries immutable and record the live session.
5. Exact malformed-buffer behavior, resident monitor lifecycle, actual Exec
   AUTOINIT, Coerce/general geometry and full KS3.1 replacement remain open.

## Historical aligned DIMS implementation/integration checkpoint — 2026-09-08

Authoritative continuation checkpoint; supersedes every historical checkpoint below.
Goal ACTIVE. Continue without asking for another resume. No live graphics build
or test remains at this checkpoint. Do not poll completed sessions.
No CPU, bus, scheduler or CyberGraphX production code changed.

Implemented native DIMS requests34..65 as complete eight-byte rectangles after
the26-byte scalar prefix. Both admission D0 and owned D7 use
C = N<=26 ? N : 26 + 8*((min(N,66)-26)/8). Original requested D0 survives all
declines; address-space guards use C, never the unused requested tail.
DIMS34/42/50 explicitly bypasses NAME dispatch. The per-mode serializer writes
to aligned66-byte stack scratch and copies only C bytes. Public A1/D1 and
callee-saved registers, caller SP/PC and exactly one RTS are preserved.
Maximum frame118bytes; test guard moves exactly40bytes from0x2B2 to0x28A.
Independent source review and functional tests found no defect.

IMPORTANT ROM COUNTEREVIDENCE: do not implement successful odd rectangle copies
as purported ROM compatibility.343 odd-full probes all failed to return.
344's test-only instruction-boundary observer captured vector3 in all22keys:
fault address = odd destination+26, masked status05 (supervisor data write),
saved PC00F971CE, handler00F80AD0. No ROM instruction bytes were emitted.
dims-odd-address-error-rom.trx:22passed/0failed/0skipped,2m40s; session89720 COMPLETE.
Current native rectangle-bearing odd requests (C>=34) decline transactionally
before any destination/A6 access; original requested D0/D1 are restored.
Existing odd scalar policy <=33 is unchanged. Neither safe declines nor that
older scalar policy constitute exact guest-exception parity.

Evidence:
-342 full original graphics union COMPLETE session79586:
 dims27-33-rectangles-broad.trx,12724pass/0fail/9 optional unconfigured ROM skips,
 12733total,19m26s. This qualifies342, not current344.
-343 dims34-66-native-red.trx:16 expected publication failures.
-343 initial odd-capable candidate focused:126pass/3 old fingerprint failures,
 129total,2m29s; session80807 COMPLETE. Odd acceptance was subsequently withdrawn.
-343 dims34-66-rom-coldboot.trx:21pass/23fail/0skip,5m21s;70093 COMPLETE.
 Of22 aligned paired-key rows, only0440 failed: retained Copper reservation
 error after69 successful transfers, PAL owner00021440/request32 last returned.
 address000466,request20747036,OUT20747038,complete20747040,busHorizon20747034.
 Remaining22 failures were odd full calls later classified as vector3 above.
 Dense342 OCS/PAL failures and this paired-row failure remain unchanged.
 A separate single-owner00021440 cold-boot test is now added but NOT YET RUN.
 These complementary observations do not qualify continuous long-run timing.
-344 normal build32245 succeeded17.73s. Aligned/decline focused session4736
 COMPLETE:126pass/3 old fingerprint failures/0skip,129total,2m2s.
 Structural audit passed:PAL1442944/NTSC1442844bytes,71642branches,
 3428relays,7chains,3346boundaries. The three fingerprint assertions are now
 updated in source, but their final rebuild/tests have NOT passed yet.

Current integration impediment is unrelated shared-tree work; leave it untouched:
a subsequent normal rebuild failed with13 missing IntuitionIdcmpNativeAuthorityCore/
IntuitionIdcmpNativeOwnerCore references in the external CopperStart.Intuition
project. A tests-only rebuild with BuildProjectReferences=false also failed,
on duplicate AmigaBus/OcsDisplay aliases in BitplaneStorageStopTests.cs.
After observing dependency changes, fresh normal345 build38156 retried:
FAILED16.07s, now only missing IntuitionIdcmpNativeOwnerCore at
external State/IntuitionExecLibraryOwner.cs:226. A second isolated tests build
also failed on the same alias errors. No current validation job remains live.
Neither failure authorizes changes to those other units. Do not run --no-build
and present it as current-source qualification after these failed rebuilds.

Artifacts/evidence:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-complete-20260908-343
C:/Users/vsys-admin/AppData/Local/Temp/graphics-dims-complete-20260908-343
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-aligned-20260908-344
C:/Users/vsys-admin/AppData/Local/Temp/graphics-dims-aligned-20260908-344
Current NativeGraphicsRasterBodies.cs SHA256:
40E055D14D631D491F32D2FE8DC419FD493B09DF2D8F78C993337DADA70928F5
344 test-bin emulator SHA256:A3063B1518C708BF7969C73197C63DC1A4DF9841281B973689687FCD9AACB1D9
344 test-bin CPU SHA256:4E596749E592170FBB011DA2A8A4B0851EB943CB35BFC4876BFA01192ECBDF2F

NEXT ACTION, in order:
1. Recheck external build errors read-only; once repaired, make a normal fresh
   Debug build in a new346 artifact directory with the documented SDK10.0.301
   (345 is the retained failed integration attempt).
   Do not weaken/remove unrelated tests or edit their implementation to force it.
2. Run the DIMS/profile/branch focused union, the new single-owner ROM capture,
   and then the original full native/scaffold/display-data/DBufInfo union without
   exclusions. Record terminal TRX counts and actual skips. Keep live binaries
   immutable and record any new running session in this top checkpoint.
3. Advance GRAPHICS_LIBRARY_MONITORINFO_POINTER_GOAL.md: MNTR17..20 uses the
   already-published defaultMonitor pointer, no new registry prerequisite.
   First establish original-ROM partial/alignment behavior. Do not route MNTR
   through the DIMS scalar publisher's ChipRevBits0 read. The pointer goal now
   documents88-byte full transfers,118-byte maximum frame and function-array
   versus actual Exec AUTOINIT distinction.
4. Keep non-default MonitorSpec residency/lifecycle, exact malformed-buffer
   behavior, actual AUTOINIT, Coerce/general geometry and full replacement open.

## Historical DIMS27..33 and signed overscan checkpoint — 2026-09-08

Authoritative checkpoint. Goal ACTIVE. Previous turn made implementation
progress; current continuation revalidated91896 live and progressed unit342.
Parallel bounded audits reviewed transfer/count ABI and independent rectangles.
No CPU/bus/scheduler production changes; no CyberGraphX support added.

Independent rectangle audit compared616 full public DIMS records in336/341,
264unique profile/mode pairs, with zero formula/cross-run mismatches.
Lores/hires/superhires MaxX:325/651/1295; VideoX:331/663/1295;
MinX:-36/-72/-144. MinY is PAL-15 or NTSC-23, doubled for lace.
MaxY is (PAL268 or NTSC218)*laceScale-1. Nominal/Txt/Std unchanged.
No OCS/ECS512K/1MiB differences were observed; no AGA ROM proof is claimed.
Portable and native full-record MaxOScan/VideoOScan now emit these signed
coordinates; superhires width is1440, not1448. New independent literal helper
CapturedDimensionRectangles returns40bytes for all12 video/resolution/lace groups.
Portable tests cover rectangle transfer boundaries, aliases/handles and odd
buffers; new native full-record tests cover22keys/all8entry/profile routes at
aligned0x300 with an authoritative handle over invalidD2. Odd full DIMS remains
unowned and is NOT qualified by these aligned tests.

Native request27..33 now preflights26bytes, keeps original request staged for
declines, rounds owned D7 to26, preserves actual count in D4 across DBRA and
returns26. No stack growth;34..65 still unowned, full66 admission unchanged.
Extended scalar positive/negative matrices through33; exact-end/first-wrap
use actual copied26bytes, not the untransferred requested tail.
Agent transfer changes and native rectangle changes were reviewed separately.

Expanded original-ROM prefix probe through65:
dims27-65-rom.trx COMPLETE5pass/2fail/0skip,1m21s. Both failures are OCS/PAL
(two entry tests) in ReadAcceptedCopperWord, not output assertions:
address00047C,request22736508,OUT22736510,complete22736512,
ownerFree,busHorizon22736506,latchRequesterCopper,executed22736508.
This matches the previously recorded dense-oracle reservation issue.
Five other full profile runs passed. Preserve the failed long-run evidence;
OCS/PAL long-run validation remains incomplete. Earlier336/341 OCS/PAL
full-record/scalar evidence is retained, not replaced by the partial run.
Do not alter hardware timing simply to make this graphics probe pass.

Build94293 succeeded14.10s. dims27-33-rectangles-focused.trx COMPLETE:
110pass/7fail/0skip,58s. Four legacy rectangle expectations and three old
layout fingerprints failed; new functional/literal/ABI controls passed.
Audited PAL1441322/NTSC1441222bytes,71577branches,3428relays,7chains,
3346boundaries. Updated those fingerprints and only the four obsolete
rectangle controls, adding signed-origin assertions.
Final rebuild succeeded9.61s (existing unrelated xUnit warning).

Session97196 COMPLETE: dims27-33-rectangles-final.trx,
117passed/0failed/0skipped,1m19s. All corrected controls and audited layout pass.
CURRENT LIVE342 session79586: dims27-33-rectangles-broad.trx, original full
graphics scope without exclusions. Re-polled live after launch, no failures yet.
Artifacts C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-rectangles-20260907-342.
Evidence C:/Users/vsys-admin/AppData/Local/Temp/graphics-dims-rectangles-20260907-342.
Do not rebuild342 while79586 runs. ROM45818 and focused73326/97196 are COMPLETE.
Session91896 COMPLETE: dims19-26-broad.trx,12712passed/0failed/9 optional
unconfigured ROM skips,12721total,17m41s. Original full graphics filter without
exclusions, including strengthened per-call physical-bus trace checks.
This qualifies preceding341 code only, not the later342 geometry/count changes.
No341 test remains live; do not re-poll completed91896. Other tasks may have
independent test processes; leave them untouched.

NEXT: collect79586; inspect every unexpected failure without weakening trace
checks. Record the complete342 full-scope result. Continue native34..65 rectangle-granular publication
and odd full66 only with actual-span guards and independent record evidence.
Resolve the remaining OCS/PAL long-run oracle coverage without hiding the
reservation failure. Full replacement, MonitorSpec/lifecycle/real AUTOINIT,
Coerce ownership/general geometry and other recorded requirements remain open.

## Historical DIMS19..26 focused checkpoint — 2026-09-07

Authoritative checkpoint. Goal state rechecked ACTIVE; the preceding turn was
implementation progress, not a stall. No graphics test needed recovery.
Other tasks have separate test processes; leave their artifacts/processes alone.
These are correctness checks, not formal throughput measurements.

Unit341 extends the independent original-ROM dimension probe from16..18
through16..26. dims19-26-rom.trx COMPLETE:7passed/0failed/0skipped,1m3s.
Covers OCS/ECS PAL/NTSC512K and ECS1MiB, enumerated records, original-entry
provenance and untouched output tails. Existing legal local ROM only.
Native red suite dims19-26-native-red.trx:8expected publication failures,
8malformed-envelope controls passed. Failures left the depth byte at0xC3.
New positive matrix covers22keys, default/boot-owned modes, direct/handle
queries, requests19..26, aligned/odd/exact-final-byte outputs on all eight
fixed/relocated/direct/function-array/PAL/NTSC routes. Explicit scalar literals
are independent of the publisher. Negative controls include first-wrap,
foreign handle and null/odd/wrapped capability bases.

Implemented scalar-prefix admission through26. Shared header scratch now
contains26bytes: QueryHeader, MaxDepth, MinRasterWidth16/32/64,
MinRasterHeight1, MaxRasterWidth1008, MaxRasterHeight1024.
DIMS18..26 retains the exact capability guard and depth logic; DIMS17 stays
read-free. Native full66 and unowned27..65 paths unchanged.
Stack guard moved by exactly8bytes (70 to78 below callerSP; 0x2BA to0x2B2).
Existing partial/full transfer control now admits26 using its actual span.

Initial build25946 succeeded17.25s (existing unrelated xUnit warning).
Session45083 COMPLETE: dims19-26-native-focused.trx,74pass/3fail,44s.
The three failures were old size fingerprints after passing structural checks.
Audited PAL1441274/NTSC1441174bytes,71572branches,3428relays,7chains,
3346boundaries. Exactly32bytes added; branch structure unchanged. Updated
the two profile expectations and the scaffold fingerprint from these audits.

Expanded capability checks through26 exposed a real harness defect:
dims19-26-native-final.trx73pass/4fail: fixed-route reads disappeared after
the bounded65,536-entry ring filled. Invoke used Count as a lifetime cursor.
Clear the test fixture's BoundedBusAccessLog before each invocation and fail
explicitly if any individual call fills it. No bus/CPU production code changed.
dims19-26-native-trace-corrected.trx73pass/4fail then showed relocated reads
ARE observable (expected0/actual1). HUNK loading had already filled the ring.
Removed all relocated read-count exemptions in this fixture:27 capability
variables, two capability/monitor observable variables and two direct controls.
Earlier claims that relocated reads bypassed this trace were wrong. Earlier
output/ABI snapshots remain evidence, but relocated no-access assertions need
the current full revalidation; do not cite those older zeros as proof.

Build47472 succeeded9.96s (existing unrelated xUnit warning).
Session24248 COMPLETE: dims19-26-native-final-observed.trx,
77passed/0failed/0skipped,36s, with the stronger per-call trace checks.
CURRENT LIVE session91896: dims19-26-broad.trx, full original graphics filter:
NativeGraphics | GraphicsLibraryPortableScaffoldTests | DisplayInfoData |
DBufInfo | DoubleBuffer. No exclusions; includes all shared header controls.
Artifacts C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-prefix-20260907-341.
Evidence C:/Users/vsys-admin/AppData/Local/Temp/graphics-dims-prefix-20260907-341.
Do not rebuild341 while91896 runs. All other341 test/build sessions above
are COMPLETE; do not re-poll them or restart completed runs.

NEXT: collect91896 and inspect any failures under the strengthened trace
capture. Do not assume old relocated-control expectations are valid, and do
not relax memory-access assertions to regain a pass. Record the full outcome,
then continue DIMS27..65 rectangle-granular transfers and full-record overscan
coordinates, using independent ROM evidence before implementation.
Full replacement remains incomplete; retain all subsequent
record, rectangle, ownership, lifecycle and real AUTOINIT requirements.

## Historical DIMS17/18 and raster-limit checkpoint — 2026-09-06

Authoritative checkpoint. Implementation is continuing, but the app goal state
returned PAUSED on this explicit resume (not ACTIVE as older checkpoints say).
No implementation blocker or user decision is required for the current work.
Goal-state resume is controlled by the app; editing Markdown does not change it.

Recovered prior header-control result: session97252 COMPLETE, 1429 passed,
zero failures/skips, 2m55s (dims17-18-header-controls.trx).
Fresh build70589 succeeded in12.90s with one existing unrelated xUnit warning.
New capability-boundary tests cover both profiles, fixed/relocated and
direct/function-array entries, chip bits0/2/3/4/8/12, four depth classes,
17/18-byte odd output and malformed A6 boundaries.
First run dims17-18-capability-boundaries.trx:33pass/4fail. These four failures
were the relocated route's physical-bus counter assumption, not depth output:
existing fixture controls likewise report no physical-bus reads for HUNK data.
Corrected only the traced count by route, retaining all depth, output-span,
register and memory-safety checks. Final focused run:
dims17-18-capability-boundaries-final.trx,37pass/0fail/0skip,5s.
AA8 remains an implementation-policy control, not independent AGA ROM evidence.

Bounded unit340 is IMPLEMENTED and verified by the broad run plus corrected controls.
Independent336 OCS/ECS PAL/NTSC captures show the DIMS scalar raster limits
differ from nominal dimensions: MinRasterWidth16/32/64 by resolution,
MinRasterHeight1, MaxRasterWidth1008, MaxRasterHeight1024 across22keys.
Both publishers incorrectly wrote nominal/overscan sizes into offsets18..25.
Added independent literal tests (four portable and eight native routes):
raster-limits-red.trx12fail confirmed that exact mismatch. Corrected only
those eight output bytes in both publishers; rectangle and ABI layout unchanged.
Build74130 succeeded10.05s, no warnings/errors.
raster-limits-focused.trx33pass/0fail/0skip,5s, includes native image/branch audits.
Existing code size/branch fingerprints remain unchanged.
Session98219 COMPLETE: raster-limits-broad.trx, full original filter below.
12691passed/5failed/9 optional unconfigured ROM skips,12705total,17m17s.
Inspected the completed TRX: all five failures are exactly the stale scalar
expectations listed below (200 versus1,640 versus32,1280 versus64); no others.
This is NOT an all-green single broad run. Its five corrected controls passed
in the separate final38-test rerun, without production changes or exclusions.
This immutable run reported five legacy raster-scalar expectation failures:
DisplayDatabaseReturnsNativeDisplayInfoDimensionMonitorAndNameChunks,
NativeGraphicsGetDisplayInfoDataPublishesCanonicalDtagChunksOnAccurateM68000,
DisplayDatabaseExposesEcsSuperHiresGeometryThroughPureAndRegisterPaths,
DefaultMonitorDisplayDataAndMonitorSpecFollowActiveNtscProfile, and
DisplayDatabaseExposesEcsSuperHiresDualPlayfieldProperties.
Read each control and replaced only the incorrect scalar expectations from
the ROM literals, retaining their nominal/overscan rectangle checks.
Separate final artifact build42838 succeeded35.76s (existing xUnit warning):
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-raster-limits-final-20260906-340.
Final controls session16677 COMPLETE: raster-limits-final-controls.trx,
38passed/0failed/0skipped,5s, including all five corrected legacy controls.
Production bytes are unchanged from98219; only those five tests differ.
Keep the complete broad failure record and final rerun evidence separately.
Artifacts: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-raster-limits-20260906-340.
Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-raster-limits-20260906-340.
No340 test remains live. Preserve these completed evidence files.
The339 run below remains a separate, immutable preceding-code gate.

Session17117 COMPLETE: associated-geometry-dims17-18-broad.trx,
12684passed/0failed/9 optional unconfigured ROM skips,12693total,18m.
This qualifies339 mode-selection and DIMS17/18 changes, not340 raster limits.
Full original graphics filter, no exclusions:
NativeGraphics | GraphicsLibraryPortableScaffoldTests | DisplayInfoData |
DBufInfo | DoubleBuffer.
Artifacts: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-associated-20260906-339.
Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-associated-20260906-339.
No339 test remains live. Other tasks have independent
test processes; leave those processes and artifacts untouched. This is a
correctness run, not a formal host-throughput gate.

NEXT: proceed directly to DIMS19..26 byte prefixes; no user clarification or
test-process recovery is needed. Do not poll completed17117/98219/16677.
1. Extend the existing independent ROM dimension-prefix probe through26;
   retain original-entry provenance and untouched-tail checks. Use the
   existing legal local ROM fixture, not a replacement oracle or download.
2. Add native red coverage for requests19..26 across fixed/relocated,
   direct/function-array and PAL/NTSC routes, default/owned handles, odd,
   exact-final-byte and first-wrapping spans, foreign ownership and bad A6.
   Expected scalar bytes come from CapturedRasterLimits, not the publisher.
3. Admit a bounded bytewise scalar prefix only after the evidence, preserving
   the caller's registers and exact return/count behavior. Audit any scratch
   and branch-layout change; retain the previous full-record controls.
4. Verify focused cases and the full original graphics scope, retaining
   failed-run evidence and recording corrected reruns explicitly.
Other pending units include DIMS27..65/odd full records and full-record
MaxOScan/VideoOScan rectangle coordinates (current extents/origins are not
yet ROM-equivalent), MNTR partials,
Coerce general geometry/ownership, public GetVPModeID association behavior,
MonitorSpec lifecycle and actual Exec AUTOINIT. Full replacement is incomplete.

## Historical native DIMS17/18 header-control checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn implemented native17/18
and adjusted its immediate controls. This continuation verified a fresh build
after the old build handle81448 was unavailable; no test was started on an
unconfirmed build. Build82590 succeeded37.03s.

Production changes: DIMS size admission now routes17/18 through the existing
bytewise inclusive destination proof (odd/exact-end supported). DIMS17 skips
A6 validation/read because only the invariant high depth byte0 is requested.
DIMS18 retains the existing exact capability-address guard, then emits
mode-specific depth5/4/6/2 or the existing full-AA8 override.
QueryHeader publisher now pushes a depth UWORD before its16-byte header scratch,
copies exactly D7 bytes, discards18 scratch bytes and restores the original
44-byte public frame/staged count. All other header prefixes still copy their
original counts. Full DIMS66 code and unsupported19..65 ownership unchanged.

dims17-18-native-focused.trx11fail/18pass: eight new tests returned correct
bytes/registers but guard still assumed68 rather than70 bytes of active stack;
three audited fingerprints changed. Guard moved exactly two bytes to0x2BA.
New fingerprints after structural audit:
PAL1441242/NTSC1441142 bytes,71572branches,3428relays,7chains,3346boundaries.
dims17-18-native-expanded.trx37pass/8fail: new prefix tests, full depth tests,
and image/branch audits passed; eight old transfer controls expected17 decline.
Controls now admit17/18 by actual span, retain19..65/foreign/full-span declines.
Updated the two other dims17-control cases to publish with no A6 read. The
DISP18 suite's dims18-control intentionally retains invalid-A6 decline.

CURRENT LIVE session97252: dims17-18-header-controls.trx, covering DIMS tests,
all QueryHeader prefix lengths, DISP17/18 controls and native profile/branch
audits. Re-polled live without new failures; do NOT rebuild339 while it runs.
Evidence C:/Users/vsys-admin/AppData/Local/Temp/graphics-associated-20260906-339.
Artifacts C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-associated-20260906-339.
No other current build/test handle. Prior25350 is COMPLETE (12668pass/9skip).

NEXT: finish97252, resolve remaining boundaries (including explicit DIMS18 AA
prefix controls and malformed span/handle guards as needed), then full original
graphics regression for combined339 changes without exclusions. Do not call
this broadly qualified from the earlier338 gate. Coerce remaining ownership/
general geometry and public GetVPModeID mismatch, other record partials/odd
paths, MonitorSpec lifecycle and real AUTOINIT remain pending.
No user input needed; full goal remains incomplete.

## Historical DIMS17/18 red checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Prior turn made verified Coerce progress.
Broad preceding338 session25350 is COMPLETE: native-coerce-associated-broad.trx
12668passed,zero failures,9 unconfigured ROM skips,12677total,18m30s.
This qualifies preceding338 code, not the later339 geometry corrections.
No live build/test processes remain. Coerce current339 broad qualification
still required; do not re-poll or restart the completed25350.

Prepared the next bounded transfer unit:
NativeDisplayInfoDataDimsSeventeenAndEighteenBytePrefixesMatchRom in
NativeGraphicsDisplayInfoDataAdmissionTests.cs. Eight entry/profile combinations
(fixed/relocated x direct/function-array x PAL/NTSC), four independent depth
keys lores5/hires4/HAM6/SuperHires2, requests17/18, aligned/odd/exact-final-byte
destinations. Checks no fallback, count, explicit depth bytes, full output
snapshot guards and callee-save/public-register/PC/SP/native-return invariants.
Build32880 succeeded9.78s. dims17-18-native-red.trx FAILED8/8,zero skips,2s,
as expected before implementation. No production change this turn.
Evidence/artifacts339 paths are in the checkpoint below.

Implementation map in NativeGraphicsRasterBodies.cs:
- DIMS size admission around46616 currently admits <=16 or >=66; 17/18 decline.
- Existing bytewise destination proof around46667 supports odd/exact-end safely.
- Canonical capability split around46933 makes <=16 read-free. DIMS17 only
  needs invariant high depth byte0, so should be read-free too;18 needs the
  existing guarded ChipRevBits0 capability byte for AGA depth override.
- Header publisher around47082 builds16-byte scratch and byte-copies D7 bytes.
  Add bounded DIMS17/18 routing and depth-word scratch safely; do NOT merely
  widen count and read past the current16-byte scratch. Match all mode-specific
  depths and AGA8 policy used by full DIMS. Preserve staged original count on
  decline and frame restoration. Native full DIMS66 publisher stays unchanged.
- Existing NativeDisplayInfoDataDimsCapsFullSpanAndPreservesPartialAndMalformedDeclines
  and other dims17-control scenarios still expect declines; audit/update those
  intentional boundaries when admission changes. Keep unsupported19..65 work
  explicit rather than accidentally widening to an unimplemented copy.

NEXT: implement/rerun this red unit with control/branch/profile tests,
and qualify current combined changes with original broad scope. Coerce general
geometry/overrides/default handles and GetVPModeID mismatch, remaining record
partials/odd paths, MonitorSpec lifecycle and actual AUTOINIT remain pending.
No user input required; full goal remains incomplete.

## Historical associated nominal geometry checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn added source/viewport
independence; this turn independently tested mixed widths and corrected both
portable/native selection to use associated source nominal dimensions.
Preceding338 broad session25350 remains LIVE, re-polled without new output.
338 binaries untouched; no339 build/test session is currently live.

ROM probe now independently varies320/640 width as well as viewport Modes,
association state, source key, depth, flags and PAL/NTSC:768 observations.
coerce-mixed-width-rom-observations.trx10passed,zero skips,1m3s. It proves:
associated hires at width320 still selects hires for depth4 (lores at5);
associated lores dual at width640 stays lores dual; HAM/EHB likewise stay
associated lores modes. Without association, actual viewport width controls.
This disproved the prior assumption that only feature flags came from source.
New full native/portable matrices went RED6/6 in
coerce-associated-nominal-geometry-red.trx (four native/two portable theories).

Portable CoerceMode now scores using sourceMode.Width/Height when associated.
Native uses the canonical source key's resolution/lace and feature bits plus
its derived depth class, then applies destination depth filtering. This avoids
constructing nonexistent hires HAM/EHB IDs from viewport geometry.
Combined configured coerce-associated-nominal-geometry-focused.trx session79173
COMPLETE62passed/4failed,zero skips,1m40s. All768 native,768 portable and768 ROM
matrix observations passed, plus provenance guards. Failures were3 literal
fingerprints and the legacy native geometry fixture. Updated that fixture's
associated dual/HAM/lace expectations instead of treating geometry mismatch
as fallback. Fingerprints updated after structural audit:
PAL1441140/NTSC1441040 bytes,71567branches,3428relays,7chains,3346boundaries.
Build34964 passed10.11s. coerce-associated-nominal-geometry-final.trx PASSED56/56,
zero skips,1s. Production unchanged between combined ROM run and final fixture
corrections. Current339 evidence/artifact paths remain below.

NEXT: complete25350 and collect its full TRX, then run current339 full original
graphics gate. Native ModeID overrides/default-handle/foreign-source boundaries,
general geometry and AGA SuperHires remain pending. Native still checks viewport
profile/height before source association; source-height override deserves
independent coverage. Public GetVPModeID Modes-only fallback remains a known
separate mismatch. Do not call this full Coerce or graphics compatibility.
Proceed native DIMS17/18 and other record/lifecycle/AUTOINIT units after gates.
No user input needed; full goal remains incomplete.

## Historical independent-key geometry checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn made verified native
progress; this turn expands source-vs-viewport independence and corrects native
key/geometry coupling. Broad preceding338 session25350 remains LIVE, re-polled,
no new output/failures so far.338 binaries untouched. Use isolated339 for changes.

Evidence C:/Users/vsys-admin/AppData/Local/Temp/graphics-associated-20260906-339.
Artifacts C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-associated-20260906-339.
Expanded ROM probe now tests viewport Modes equal source key or zero independently
of associated record. coerce-independent-viewport-mode-rom.trx session61896
COMPLETE10passed,zero skips,42s.384 feature observations with literal assertions
confirm association wins and geometry remains independently effective.
Both native and portable matrices expanded identically. Build8745 passed10.13s.
coerce-independent-viewport-mode-red.trx:4 native failures,2 portable passes.

Replaced native geometry-coupled mode ladder with canonical-key validation,
geometry-derived resolution/lace bits, and independently validated source
feature bits. Associated source key need not equal viewport Modes now.
ModeID overrides/foreign associations still pending; standard geometry only.
Build1856 passed13.98s. coerce-independent-viewport-mode-focused.trx:
52passed/4failed,zero skips. All384 native and384 portable matrix cases PASS.
Three failures are literal fingerprints after successful structural checks:
actual PAL1441122/NTSC1441022 bytes,71565branches,3428relays,7chains,3346boundaries.
Fingerprint expectations NOT YET updated. Fourth is legacy native fixture
NativeGraphicsCoerceModeAdmitsStandardPalNtscProgressiveAndInterlaceViewportsOnAccurateM68000
line16945: expected PALlores135168, actual PALhires167936 for640width/Modes0.
This old expectation predates geometry/source independence. Inspect the rest
of that large fact before further edits: other deliberately mismatched geometry
cases exist, including width640 with HAM association. Do not merely accept
any emitted combination: HAM/EHB must not produce nonexistent hires feature IDs.
Mixed source/geometry cases need independent ROM observations and destination
selection constraints before broad qualification. Targeted diff check passes.

NEXT: resolve the legacy fact and source-feature/destination-geometry limits,
update fingerprints from structural audit, rerun focused/current broader gate.
Continue re-polling25350; never restart from a quiet timeout or rebuild338 while
live. No339 process remains live. Native DIMS17/18 and remaining display-record,
MonitorSpec, GetVPModeID association, ModeID overrides and AUTOINIT work remain.
No user input needed; full goal remains incomplete.

## Historical native associated-source checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn implemented portable
selection correction; this turn added native matrix and corrected admitted arm.
New NativeGraphicsCoerceAssociatedSourceMatchesIndependentRom4cases cover192
ROM-derived combinations: PAL/NTSC, flags0/1, map absent/empty/associated,
hires/HAM/EHB/dual,depth4..7. Synthetic public ColorMap native handles are seeded;
ROM probe separately verifies actual VideoControl/GetVPModeID association.
native-coerce-associated-red.trx FAILED4/4 before production change.

AppendCoerceModeStandard now clears special features when no associated record,
validates matching canonical normal-display handles, preserves associated
features, and lets deeper hires choose lores before applying lores depth limits.
Nonzero ModeID overrides, foreign handles and source-key/viewport-key differences
remain fallback-owned in this arm; these are PENDING native coverage, not full
replacement completion. General geometry selection still needs expansion.
Do not treat these bounded tests as proof of full CoerceMode/GetVPModeID behavior.
Public GetVPModeID's Modes-only fallback remains separately known incompatible.

First focused run26pass/3 fingerprint differences (structural audit passes).
New audit PAL1440962 / NTSC1440862 bytes,71550branches,3428relays,7chains,3346boundaries.
Expanded run55pass/1 old feature fixture; updated that legacy fixture to attach
matching source records while retaining separate unassociated matrix coverage.
Build65509 passed10.65s. native-coerce-associated-final.trx PASSED56/56,
zero failures/skips,1s; covers Coerce/BestMode and native profile/branch audits.
Diff checks pass. Emitter SHA2563B343FE998B063357E92833D6ED893DE0E6211383C9C1FE5B80CD77ABA429494.
Scaffold SHA2561D51F73FF5B79802E5CE2D4BD3560C644644B2388BE769C8A298A28CAA35C77B.

FULL ORIGINAL GRAPHICS GATE is LIVE in exec session25350:
native-coerce-associated-broad.trx, no exclusions. Re-poll that exact handle.
Do not rebuild its338 artifacts while live. Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-native-depth-20260906-338.
Artifacts C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-native-depth-20260906-338.
Prior35082 is COMPLETE and must not be polled/restarted.

NEXT: complete25350 and resolve failures without exclusions. Continue native
source association/selection gaps using independent probes and regression cases;
then native DIMS17/18, remaining partial/odd paths, MonitorSpec lifecycle and
actual AUTOINIT. AGA SuperHires native2 vs portable8 remains pending evidence.
No user input required; full goal remains incomplete.

## Historical portable associated-source checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn yielded decisive ROM
evidence; this turn implemented portable destination-depth/source-feature rules.
No live build/test sessions remain (35082 completed historically below).
Current338 artifacts remain at the paths recorded below.

New CoerceModeAssociatedSourceMatchesIndependentRom covers the full192-observation
matrix: PAL/NTSC x absent/allocated-only/associated ColorMap x hires/HAM/EHB/dual
x depths4/5/6/7 x flags0/1. Configured coerce-associated-portable-red-rom-assertions.trx
session52072 COMPLETE:2 portable failures,10 ROM/provenance passes,zero skips,35s.
This also verifies the previously added literal ROM output assertions, not just
observation logging. Original-entry checks include source association vectors.

Portable GraphicsDisplayDatabase.CoerceMode no longer rejects source MaxDepth.
Bitmap depth always filters candidate destinations; SpecialFlags must match a
resolved source record (ordinary when no association). Removed full-key scoring
preference so hires5 can select lores5. Native/foreign Modes admission retained.
GraphicsLibraryCore passes the associated mode via GraphicsColorOperations'
new requireAssociation=true option; default false preserves the existing public
GetVPModeID behavior pending its separate caller audit. That existing Modes-only
fallback is a known ROM mismatch, NOT a compatibility waiver or final policy.
Legacy feature test now attaches a normal display-info record through VideoControl;
SuperHires geometry is1280 with2planes rather than320 with source-key preference.

Initial focused runs30pass/1 legacy fixture failure retained. Final build52151
passed10.52s. coerce-associated-portable-final.trx PASSED31/31,zero skips,605ms.
This is focused portable qualification only; no broad gate for these edits yet.

NEXT: native CoerceMode still uses the superseded source-depth/direct-key ladder.
Add native tests for hires5 -> lores, and associated vs unassociated feature
sources, then implement resolved-source destination selection. Do not keep
blanket rejection or claim provider fallback completes native replacement.
Audit GetVPModeID's association semantics as a separate dependent unit. Preserve
RTG/provider ownership, malformed input, default-monitor and AGA controls.
After native parity, run original broader graphics gate without exclusions,
then native DIMS17/18 and remaining lifecycle/partial/AUTOINIT work.
Full goal remains incomplete; no user input required.

## Historical ROM evidence against blanket source rejection — 2026-09-06

Authoritative checkpoint. Goal ACTIVE; prior focused passes DO NOT establish
Coerce compatibility. This turn produced decisive independent evidence requiring
revision of both portable/native source-depth rejection introduced337/338.
Broad old-build session35082 is now COMPLETE: depth-selection-broad-regression.trx
12657 passed,3 failed,9 unconfigured ROM skips,12669 total,18m56s.
The3 are the historical assertions already rerun successfully in338's red
native test batch. This does NOT qualify later338 native production edits or
waive the newly disproven Coerce rules.334 binaries remained untouched.
Current338 artifacts have last build43548 success12.23s; no live338 test.

Expanded NativeGraphicsModeSelectionDepthBoundaryObservations on original OCS
PAL/NTSC,512Kchip/512Kexpansion, AccurateM68000/liveDMA. Skill amiga-cycle-exact
and timing checklist kept profiles explicit; no hardware/CPU changes.
coerce-feature-rom-observations.trx10pass23s exposed hires depth5 -> lores.
coerce-feature-colormap-rom-observations.trx10pass27s showed an allocated
ColorMap alone still yields GetVPModeID=INVALID. Corrected probe adds a third
state with FindDisplayInfo + VideoControl VTAG_NORMAL_DISP_SET(handle).
coerce-feature-associated-rom-observations.trx session83537 COMPLETE10pass,
zero skips,34s; all original ROM entries checked before/after, including
GetVPModeID/GetColorMap/FreeColorMap/FindDisplayInfo/VideoControl.
Three ColorMap states (absent, allocated-only, associated), modes8000/0800/0080/
0400, depths4/5/6/7, flags0/1, PAL/NTSC =192 feature observations.
Associated GetVPModeID assertions confirm exact monitor|mode for every case.

OBSERVED with either flag: hires depth4 -> hires,5 -> lores,6/7 -> INVALID,
even with a valid associated source ID. Associated HAM/EHB/dual preserve key
at4/5/6, reject7. Without associated ID these feature bits do not constrain
Coerce: depths4/5 -> ordinary lores,6/7 -> INVALID. Mere allocated ColorMap
does not associate a mode. Thus source MaxDepth rejection is too broad and
ViewPort.Modes alone is not sufficient source identity for this API.
Literal output assertions have JUST been added after these observations;
rebuild/rerun them before claiming asserted qualification.

NEXT: add replacement regression matrix from these observations; replace
blanket source-depth rejection with resolved-source feature constraints and
destination depth filtering (bitmap depth applies in observed flags0/1).
Audit native path as well: its direct key-based return and invalid source-depth
ladder do not cover five-plane hires -> lores or missing associated source.
Do not paper over by editing tests to current output or declaring native
fallback sufficient for the full replacement goal. Preserve origin evidence.
Then rerun the corrected implementation's broader gate, proceed native DIMS17/18.
MonitorSpec lifecycle, partial/odd records, actual AUTOINIT and AGA evidence
remain incomplete. No user input needed.
Evidence/artifacts338 paths are in the historical checkpoint immediately below.

## Historical native depth focused checkpoint — 2026-09-06

Authoritative checkpoint; previous turn made progress and verified live wait.
Goal ACTIVE. Broad preceding-build session35082 remains LIVE, latest poll no
additional failures beyond the three historical assertions listed below.
Its334 artifacts are untouched. Do not restart or rebuild that directory.

Built ISOLATED338 artifacts to continue safely:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-native-depth-20260906-338.
Evidence C:/Users/vsys-admin/AppData/Local/Temp/graphics-native-depth-20260906-338.
Build80829 passed23.28s. native-coerce-depth-red.trx: three corrected legacy
tests PASS; two new NativeGraphicsCoerceModeOrdinaryDepthMatchesIndependentRom
cases FAIL (flags0/1, original PAL ordinary source depths5/6).
Implemented native AppendCoerceModeStandard source-depth checks independent of
PRESERVE_COLORS: lores5, hires4, lores special6, SuperHires2; invalid depth in
an admitted source publishes INVALID rather than retaining input monitor.
Full AA ordinary8 policy retained. Native SuperHires still2 even under AA;
portable GetDepth(AGA)8 discrepancy remains pending evidence, not waived.
Unrecognized/malformed geometry still follows original fallback ownership.

First native focused run46pass/4fail: three code fingerprints changed and one
legacy fixture invalid. Structural branch checks passed before fingerprints:
PAL1440834 / NTSC1440734 bytes,71539 branches,3428 relays,7 chains,3346 boundaries.
Updated literal fingerprints from that audit. Legacy native geometry fixture
now uses hires4; invalid SuperHires3/ordinary7/partial-AA8 expect INVALID;
valid AA8 check retained. Restore ordinary5 before unknown-flag check.
Preserve all intermediate failed TRXs (including misleadingly named
native-coerce-depth-focused-qualified.trx, actually49pass/1fail).
Final build33091 passed10.55s. native-coerce-depth-focused-final.trx PASSED50/50,
zero failures/skips,1s. Only this final result qualifies the focused slice.
No live338 build/test session remains.

NEXT: re-poll35082 to completion and capture full TRX, then run broad regression
against current338 native change without exclusions. Add independent ROM
feature-source boundary evidence for source-depth policy beyond captured lores.
Native DIMS17/18 follows; partial/odd paths, MonitorSpec lifecycle, actual
AUTOINIT and the full graphics goal remain incomplete. No user input needed.

## Historical depth selection focused checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn produced verified ROM
evidence; this turn implemented source-depth validation in portable CoerceMode.
BestModeAndCoerceDepthBoundariesMatchIndependentRom went RED2/2 before the
correction (mode-selection-portable-red.trx). CoerceMode now resolves the source
mode even for Modes0 and rejects bitmap depth above its mode-specific maximum,
before selecting a destination. This prevents ordinary six-plane sources being
reinterpreted as HAM or coerced to lores when flags0 is used.
Four BestMode geometry/provider fixtures now use valid hires depth4; the new
independent boundary matrix retains depths4/5/6 on PAL/NTSC. The legacy Coerce
fixture now expects INVALID for ordinary depth6 under both flags, restores5
for flicker checks, and uses2 for the separate SuperHires feature check.

Build2947 passed13.91s. First focused run59 passed/1 invalid SuperHires fixture
failed; preserve mode-selection-correction-focused.trx. After fixture correction,
build passed9.17s and mode-selection-correction-focused-qualified.trx passed60/60,
zero failures/skips,4s. Includes captured DIMS depths, BestMode/Coerce, native
image profiles and signed-word branch audit. Targeted diff check passes.
Full original graphics gate is LIVE in exec session35082 (re-polled live).
Early failures reported: DisplayDatabaseReturnsNativeDisplayInfoDimensionMonitorAndNameChunks
and NativeOverlayAndHostGatewayPreserveModeSelectionParity. Source inspection
found stale assertions: hires MaxDepth6 instead of4, and a depth5 BestMode request
expecting hires instead of lores. Those two assertions are now corrected in source
ONLY (not built/tested); active session35082 still runs the preceding immutable
build. Scaffold hash below describes that build, not these new source edits.
Third early failure DirectGraphicsServicesModeSelectionHelpersUsePortableBoundary
has the same five-plane BestMode/hires expectation; changed expected result to
PAL lores in source only, retaining its RTG ownership checks. Three pending
assertion corrections total. Do not claim these failures resolved until the
complete tests are rebuilt/rerun.
Native entry is AppendCoerceModeStandard in Portable/NativeGraphicsRasterBodies.cs
at45770; its mode admission ladder begins at45900. Read depth checks46210..46285:
native validation is bypassed without PRESERVE_COLORS; legacy maximum is still6
for all non-SuperHires modes. This is a separate confirmed implementation gap,
not what caused the overlay parity assertion failure. Add independent native
boundary tests before changing the emitted ladder. Preserve AGA controls.
depth-selection-broad-regression.trx. Re-poll this exact handle; do not restart
or rebuild its artifacts while live. No exclusions; same scope as prior335 gate.
Evidence C:/Users/vsys-admin/AppData/Local/Temp/graphics-depth-correction-20260906-337.
Artifacts C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334.
Database SHA2567E38A2837C5981B4A8B7F56A9AA2E56C5637ED9A8D9E51C79DB093CDF442A39A.
Scaffold SHA256057300E91FB1A594CDD5B58554EA98A2275A70CD5C393A326323A1CDC05C99DB.
ROM probe SHA25698A55BCEC93D2C1E7B22B56FADACABF9E5180DBFF69438257DF3BDC78A2E1545.

NEXT: finish broad gate, investigate failures without exclusions; inspect native
Coerce admission/fallback versus the new invalid-source behavior and add any
missing native boundary coverage. Source-depth validation beyond the captured
ordinary lores cases should receive ROM feature-mode boundary evidence, not
be claimed fully ROM-qualified from portable tests. Native DIMS17/18 follows;
remaining partial/odd paths, MonitorSpec lifecycle and real AUTOINIT still pending.
No user input needed. Full goal and depth unit remain incomplete until gates
and the required native coverage are proved. CyberGraphX remains excluded.

## Historical depth correction and independent selection evidence — 2026-09-06

Authoritative checkpoint; supersedes the historical checkpoints below. Goal ACTIVE.
ECS one-megabyte chip RAM probes completed: rom-dimensions-depth-memory-profiles.trx
15 passed, zero failed/skipped, 56s. Both ECS memory profiles report chip-rev02,
not03; all observed22-key depth maps agree. Do not force hardware capabilities.
Portable ModeRecord and native DIMS records now use lores5, hires4,
lores HAM/EHB/dual-playfield6, superhires2. AGA8 policy unchanged/unqualified.
Independent captured-depth tests went RED12/12 before correction; focused
depth-correction-selection-focused.trx then passed53 with5 selection failures.
This production depth change is NOT yet broad-qualified.

New NativeGraphicsModeSelectionDepthBoundaryObservations uses original ROM
BestModeIDA/CoerceMode entries checked before/after, PAL/NTSC cold boots and
ordinary synthetic viewports. Build73892 passed12.29s; session12088 COMPLETE:
mode-selection-rom-observations.trx passed10/10, zero skips,16s (two profiles
plus eight provenance guards). For width640 BestMode depths4/5/6 return
boot-owner hires / lores / INVALID. Coerce ordinary width320, heights256/512,
depth5 returns boot-owner lores / lores-lace; depth6 returns INVALID for
both flags0 and PRESERVE_COLORS. This disproves the replacement's HAM fallback.
Captured expectations were added to the probe and independently rerun:
build42703 passed12.08s; session51002 COMPLETE, mode-selection-rom-assertions.trx
passed10/10, zero failures/skips,18s. No live build/test session remains.

Evidence folder: C:/Users/vsys-admin/AppData/Local/Temp/graphics-depth-correction-20260906-337.
Reusable artifacts: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334.
NEXT: add portable regression cases from these independent observations; inspect
CoerceMode source validity and special-feature filtering before correcting it.
Do not merely replace the five old expectations with current output. Four old
BestMode geometry/provider tests request depth5 but expect hires: use depth4 for
their geometry purpose, preserving separate depth5/6 boundary regression coverage.
Then qualify affected BestMode/Coerce/AGA controls and full original graphics gate.
Native DIMS17/18, remaining partial/odd paths, MonitorSpec lifecycle and real
AUTOINIT remain ordered follow-on work. No CyberGraphX, commits or unrelated changes.
No user input is required; resume directly from NEXT.

## Historical four-profile depth checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. This turn added and verified independent
ROM profile coverage. No live graphics build/test sessions remain.
CreateNativeIntuitionOracle now has optional ecs=false after ntsc=false;
existing callers retain identical OCS defaults. It selects EcsPal/EcsNtsc only
when explicitly requested. The continuous depth fact delegates to shared
CaptureNativeGraphicsDimensions(false,false); a new four-case theory selects
OCS/ECS x PAL/NTSC. Chip/expansion RAM remain512K/512K, AccurateM68000,
liveDMA and legal local A5003.1 fixture. Profiles, chip-rev byte, transfer
guards, alias owner identity, Length8 and original ROM-entry provenance logged/
verified. No production library, scheduler or CPU change.

Build14358 succeeded11.96s,zero errors,one existing warning. Configured
session14435 COMPLETE: rom-dimensions-depth-profiles.trx passed13/13,zero skips,
50s. Four profiles each enumerated44records (22default aliases+22boot-owner
records), plus the original OCS PAL fact and eight origin guards.
Across176 profile-matrix records, every profile has identical22key depth map:
0000/0004=5;
0080/0084/0400/0404/0440/0444/0800/0804=6;
8000/8004/8400/8404/8440/8444=4;
8020/8024/8420/8424/8460/8464=2.
Each record checked requested88 -> copied66 plus16/17/18 prefixes and guards.
These remain depth OBSERVATIONS, not production-comparison assertions.
Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-rom-depth-20260906-336.
Artifacts: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334.
Probe SHA256BCDB5004EBDE1F12447A6B17A1D100A4652FA5B2E62AC17B612B619EE629E948.
Shared oracle SHA2562579C7DFC99404E40661AAF75187AB10D151B95FFE22C79819367D459B7A2D7D.

Important profile qualification: OCS ROM chip-rev=00, ECS-configured ROM
chip-rev=02. GraphicsContracts.cs GraphicsChipRevision defines HrDenise=02,
HrAgnus=01, SetEcs=03. Thus ECS Denise detection is observed, but do not claim
the ROM reports full03 or guess why HrAgnus is absent with512K chip RAM.
Skill amiga-cycle-exact kept this distinction explicit; no forced register
patches or timing workaround. Diff checks pass.

NEXT: add bounded ECS PAL/NTSC depth observations with1MB chip RAM while
retaining512K expansion and existing factory defaults; inspect MachineOptions
WithChipRam/profile behavior first. Record actual chip revision and all depth
keys, do not force expected capability bits. Then encode the independently
observed22key MaxDepth table in failing portable/native tests and implement
the non-AGA depth correction. Production currently ModeRecord.Depth defaults6
except explicit superhires2; native BuildNativeDisplayDimensionsRecord likewise
returns2or6. ModeRecord.GetDepth also affects BestMode/Coerce selection, so
audit those consumers and qualify their gates, not just DIMS bytes.
AGA8-depth policy remains unverified by these profiles. Native DIMS17/18,
remaining partial/odd paths, MonitorSpec lifecycle and real AUTOINIT still
required. MNTR335 broad-qualified below. Preserve prior failed ROM TRXs.
No user input, CyberGraphX expansion, unrelated edits or commits.

## Historical MNTR qualification and OCS depth checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Session80437 is COMPLETE:
mntr-transfer-broad-regression.trx passed12,646/12,652,zero failures and six
unconfigured ROM skips,20m47s. Full original broad scope, no exclusions.
All335 production/admission/scaffold hashes reverified unchanged. MNTR transfer
correction is broad-qualified; native partial/odd/nondefault-pointer paths,
MaxDepth and real AUTOINIT remain required, so the full goal is NOT complete.

Moving to ordered MaxDepth work, using amiga-cycle-exact skill and timing
checklist. Existing continuous A500 PAL OCS ROM depth probe still had obsolete
DIMS88-byte return/Length9/default-header-ID assumptions. Corrected transfer
to independently observed331 scalar/rectangle rule, Length8, and added machine
state capture. No production or hardware timing changes.
Configured legal local3.1 A500 ROM (524288bytes), OCS PAL Agnus/Denise,
AccurateM68000,512Kchip+512Kexpansion,liveDMA; no ECS/AGA generalization.
Full build26683 succeeded11.68s. rom-dimensions-continuous-corrected.trx
session65785 completed8origin-guard passes/1probe failure,zero skips,9s:
first default DIMS copied66, MaxDepth5; old expected headerID0 got21000.
Preserve that failed TRX. Corrected this remaining alias assertion with the
already330-qualified PAL-owner rule; no portable depth expectations added.

Build45508 COMPLETE: success26.06s,zero errors,one existing warning, artifacts
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334.
Configured session36260 COMPLETE: rom-dimensions-continuous-alias-corrected.trx
passed9/9,zero skips,20s: complete continuous depth fact plus eight ROM-origin
guards. Evidence folder C:/Users/vsys-admin/AppData/Local/Temp/graphics-rom-depth-20260906-336.
No live graphics sessions remain. Probe SHA256
25EF1A7C3292998ADA82AEE8512108C6083781B995644EA9D57788D5C5F239B8.
All44 enumerated OCS PAL records (22default aliases+22PAL explicit records)
passed full transfer/header/identity and16/17/18-byte prefix+memory guards.
Observed depth: ordinary lores5; lores HAM/EHB/DPF/DPF2=6; hires4; superhires2;
lace variants unchanged. This probe records independent depth observations,
NOT yet a production depth comparison. It does not enumerate NTSC records in
PAL boot; do not claim all66 portable modes or ECS/AGA covered.

NEXT: extend independent profile evidence before editing shared depth rules.
CreateNativeIntuitionOracle in KickstartRomIntuitionBaselineTests.cs:4096
currently accepts ntsc bool and selects OcsPal/OcsNtsc; inspect profile/config
types and safely parameterize ECS without changing existing defaults. Keep
depth tests profile-labelled, liveDMA and original ROM-entry guards. Capture
ECS and NTSC results; retain existing336 and331 failed TRXs. Then encode
independent failing portable/native MaxDepth tests, implement the correction,
and proceed to native DIMS17/18. Never patch shared timing to make these probes
pass. A passing336 sweep does not resolve prior331 physical-reservation failures.
No user input needed, no CyberGraphX work or commits.

## Historical MNTR legacy/host and live-regression checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn made implementation progress.
CURRENT LIVE SESSION80437: mntr-transfer-broad-regression.trx in
C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-transfer-20260906-335.
Scope unchanged: NativeGraphics OR GraphicsLibraryPortableScaffoldTests OR
DisplayInfoData OR DBufInfo OR DoubleBuffer. No exclusions. Normal console
output is large; summarize batches and use terminal TRX for authoritative totals.
Do NOT rebuild C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334
while80437 runs. This snapshot uses successful full build71800 (36.40s,
zero errors,one existing xUnit2013 warning). No other graphics sessions live.

This turn corrected seven legacy full MNTR output spans/max helper/header
oracle to88, retaining96-byte requests and actual-end/wrap derivation.
Scaffold changes: native full-success return88 plus explicit unchanged88..95
sentinel guard; header/tuple loops; nine inline and five multiline core full
success returns plus adapter success. Nondefault native, missing monitor,
DTAG_VEC and other provider-decline original96 assertions remain unchanged.
No production changes after the preceding101-pass focused snapshot.

Added three host-overlay cases for88/96/max requests with writable88-byte output
and read-only8-byte SDK tail. Writable native GfxBase monitor-list sentinels
and a mapping allocator exercise actual resident MonitorSpec creation:
nonzero pointer, zero open count, stable identity/no additional allocation
on the second query, correct header/owner/Length9, unchanged tail/PC/cycles.
Existing read-only monitor-list rejection is unchanged.
mntr-host-tail.trx passed4/4,zero skips,148ms.
mntr-legacy-host-focused.trx passed44/44,zero skips,2s after full build71800:
complete DisplayDatabase groups, default-profile monitor group, all overlay
span cases, read-only monitor-list decline, local header Length fact, native
canonical-record fact and monitor-header portable oracle.
Full admission/public-frame groups are included in LIVE broad80437.

Current test SHA256:
admission 0FD78C6592A9DB861F556223F7117B929AA62CCF5A74C86213A90732AE4ADBDF
scaffold 1F9193BFC05B2D04524922AB2E7105037FD914A67B951C1BF363677327B2DBA8
Other source hashes unchanged from335 implementation below. Diff check passes
(line-ending warning only). No commits or unrelated edits.

NEXT: collect80437 without restart; inspect any failures, correct complete
affected groups without weakening guards, retain original failed TRXs, and
record exact terminal result before qualifying MNTR. Then proceed with ordered
MaxDepth/native partials/MonitorSpec lifecycle/real AUTOINIT work. No user input
needed. The full Kickstart goal remains incomplete; CyberGraphX stays excluded.

## Historical MNTR implementation/focused checkpoint — 2026-09-06

Authoritative checkpoint. Goal ACTIVE. This turn made implementation progress.
No live graphics test/build sessions remain. DIMS334 qualified below;99614 and
39137 are COMPLETE and must not be restarted or treated as live.

MNTR335 implementation:
- Seven independent portable boundary tests: PAL/NTSC x handle/direct x nine
  modes x sizes0..129/max, exact88-byte mappings including odd destination and
  final guest byte, true wrap rejected before MonitorSpec callback/memory access.
  mntr-transfer-portable-red.trx reproduced6failures/1pass with old96-byte cap.
- Shared portable transfer/admission now caps MNTR88; SDK backing size unchanged.
- Native full threshold96->88, last valid destinationFFFF_FFA8; removed LONG
  clears0x58/0x5C, return88. Default-only pointer admission, original requested
  size on decline, unsupported17..87 native partials and odd full declines
  remain unchanged intermediate boundaries. No MaxDepth/lifecycle expansion.
- Eight native edge theories x42queries cover two profiles, fixed/relocated,
  direct/function-array routes,17/87/88/89/96/max requests, ordinary/final-byte/
  wrap/odd/nondefault/foreign/null-monitor scenarios; complete memory/public
  frame/PC/SP/return/provenance guards. Existing32 boot-profile cases now query
  default full MNTR88/89/96/128/max too.
- Initial focused32passes/3fingerprint failures; two removed10-byte stores
  explain20-byte image shrink. Exact audited fingerprints updated to
  PAL1,440,798 /NTSC1,440,698;71,535branches/3,428relays/7chains/3,346boundaries.
- Full build8640 succeeded20.34s,zero errors,one existing xUnit2013 warning.
  mntr-transfer-focused.trx session39137 completed101/101passes,zero skips,54s:
  full portable boundary class (all three tags), three native edge groups,
  all boot-profile parity cases, complete image-profile class and scaffold audit.

Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-mntr-transfer-20260906-335.
Build artifacts reused only AFTER prior runs ended:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334.
Source SHA256:
database AFC78652D7B9EEF57161D18888B58BC99BAF3231E43ADE4D5741F9DE7D6C51F0
emitter FCFF22C4853D3E91838F14463C3AB1977C89DC7DCDE2E3189E925E900E818825
boundary 33177AAE9B88A8F6CC560238E15383BA8BB887A55AF05C0E1DCBC0E9E80BB717
admission ABA779D15504E9AC964F8431260FE2EC59958D651BF9AF1A1DB220AF28B2F7C1
profile B62D88A0B2FCED678472F8A3BF544EDA559CAA79E0F658A7A3054689FF2E5916
scaffold C2435F3CCECFB9DB050CA07995CECAD8BF1694EAA88E04ECC7E37C83C83D854C

NEXT: audit/correct legacy full MNTR96-byte successful-result/output assertions
without changing96-byte requests, provider declines or guards. Known locations:
admission outputBytes0x60 at then-lines1597/1605/1753/2015/2296/2312/2328;
portable monitor header oracle asserts96 at11405; helper maximum still0x60.
Inspect each context before edits. Also scaffold tuple/tag loops, core/adapter/
native full return assertions, and tail fields at0x58/0x5C. Add host-overlay
read-only88+8 tail admission coverage with correct MonitorSpec lifecycle setup.
Rebuild, run complete affected groups, then full original broad graphics filter
(NativeGraphics OR GraphicsLibraryPortableScaffoldTests OR DisplayInfoData OR
DBufInfo OR DoubleBuffer), no exclusions. MNTR is NOT broad-qualified yet.
Continue full ordered plan afterward; no user decision, CyberGraphX or commits.

## DIMS transfer correction qualified — historical checkpoint, 2026-09-06

Authoritative checkpoint. Goal ACTIVE. Previous turn was a verified wait;
session99614 is now COMPLETE, not live. Broad334 completed12,621passed,
1failed,6unconfigured ROM skips,12,628total in23m46s. Its sole failure was
DisplayDatabaseEnumeratesSupportedPalNtscModesAndReportsAvailability, already
corrected and passed in dims-transfer-host-tail-corrected.trx (28/28passes).
Exact test-name audit:12,628unique passing cases across broad+focused,
zero unresolved failures (includes six new overlay cases absent from broad).
This is a COMPOSITE gate; preserve the original failed broad TRX. No claim
that the unconfigured ROM skips passed or that full native compatibility is done.
Database/emitter/scaffold hashes reverified unchanged from latest334 checkpoint.
No live graphics sessions remain;333/334 artifact folders may now be rebuilt.

NEXT: implement MNTR88-byte transfer end-to-end: independent boundary tests,
portable admission/publication cap, native full minimum request/address proof,
remove clears0x58/0x5C, return88. Preserve default-only native pointer ownership,
original requested size on declines and unsupported partial-provider routes.
Then qualify focused and broad gates. Native partial/odd-address completion,
MaxDepth, MonitorSpec lifecycle and actual AUTOINIT/ROM boot remain required
later units (see ordered compatibility plan). No CyberGraphX work.

## Historical DIMS implementation and live-regression checkpoint — 2026-09-06

Authoritative resume checkpoint. Goal ACTIVE; no user decision needed.
Latest verified waits:99614 remains LIVE; no new failures in the last two
collected batches. One observed failure so far (not a terminal total):
DisplayDatabaseEnumeratesSupportedPalNtscModesAndReportsAvailability expected
tuple size88 for DIMS. Corrected only that successful return expectation to66,
retaining88-byte request and all header-Length checks. This is NOW RETESTED:
separate full build48472 succeeded40.78s,zero errors,one existing warning in
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-dims-transfer-20260906-334.
The live333 artifacts were not modified. dims-transfer-host-tail-corrected.trx
passed28/28,zero skips,1s in334 evidence folder: entire corrected enumeration
fact, six new DIMS host-overlay read-only-tail cases, DISP overlay guard,
portable DISP/DIMS boundary class and native branch audits.
New DIMS requests27/33/41/65/66/max return26/26/34/58/66/66 with only that span
writable and the remainder of the88-byte envelope read-only. Assert correct
tag/mode/header Length8, unchanged tail, caller PC and cycles.
Current scaffold SHA2561C7FA9B4E6062F0FD5613C3C36C5B1EA76D81B021C0B0FEE0BFEACD3345CEF23.
Scaffold hash below identifies the RUNNING snapshot before this correction
and six new tests. Other listed source hashes remain unchanged. Diff check
passes (line-ending warning only). Do not rebuild333 while99614 runs.
After99614 finishes, audit exact test-name coverage against this28-pass retest
and inspect/correct any other failures before qualifying the DIMS unit.
CURRENT LIVE SESSION99614: dims-transfer-broad-regression.trx in
C:/Users/vsys-admin/AppData/Local/Temp/graphics-dims-transfer-20260906-334.
Full scope: NativeGraphics OR GraphicsLibraryPortableScaffoldTests OR
DisplayInfoData OR DBufInfo OR DoubleBuffer. No test exclusions.
Uses confirmed successful build19943 (15.71s,zero errors,one existing warning)
in C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-disp-host-tail-20260906-333.
Do NOT rebuild333 artifacts while99614 runs. Collect its terminal TRX without
restarting; large console output is not a reliable total-count source.

Completed this turn:
- DISP composite gate qualified12,604unique passes,zero unresolved failures
  with six unconfigured ROM skips; details below. Full goal remains incomplete.
- Added10 independent DIMS portable tests: both boot profiles/handle routes,
  nine modes, sizes0..129 plus uint.MaxValue; exact-end partial/full spans and
  true-wrap rejection. Initial red run7failed/3passed proves previous mismatch.
- Portable transaction/admission now share ROM-observed scalar-prefix/complete-
  rectangle transfer: N if N<=26, else26+8*floor((min(N,66)-26)/8).
- Native full DIMS admission threshold88->66, last valid baseFFFF_FFBE,
  full return66. Removed six LONG clears0x40..0x54 from each of66 mode arms;
  five rectangle stores already fill every payload byte through0x41.
  Native17..65 partials and odd full destinations remain provider-owned.
  Original requested size survives all declines. Other tags unchanged.
- Added8 native profile/relocation/entry-route edge tests with40queries each:
  ordinary/final-byte/wrap/odd/foreign x17/26/33/65/66/67/88/max requests.
  Preserved complete memory/register/PC/SP/provenance guards.
  Existing32 boot-profile cases now exercise full DIMS66/67/88/128/max.
- Initial focused run25passes/3 expected fingerprint failures. Native image
  shrank3,960bytes (66arms x6removed10-byte clears); new audited fingerprints:
  PAL1,440,818 /NTSC1,440,718;71,535branches/3,428relays/7chains/3,346boundaries.
  Exact fingerprint assertions updated, not removed.
- dims-transfer-focused.trx session32120 COMPLETE86/86passes,zero skips,49s:
  portable DISP/DIMS boundaries, both native edge groups, all boot-profile
  parity cases, complete image-profile class and scaffold branch audit.
- Then corrected ten legacy full DIMS output spans to66 (88-byte requests
  retained; exact-end/wrap addresses derive from actual output). Corrected
  portable header oracle full-result/length, helper maximum, scaffold host/
  native/header-Length expectations, and four core plus one adapter result.
  No guards removed. These last legacy edits await the live broad gate.

Current source SHA256:
database C7527F79BA6FF77E783B2B5EBD923F1FA4912D30A02846E28E9DCB0B45B3062E
emitter 0C1F95781C1569B7505CD2080BAD24166C9EF05267DED0A061CF75E3B1C428BC
boundary 6AC88BC121CEA4837D9980328053E51A347F1FC560C22165A2A74927469DE841
admission 892CC41E6ABA51F9D5A1B8AABB76FD561A6088575A4844E08BEE582893952F65
profile 06C177934B0A6ACDAB719C1117E2984FF873DFAE63BF6C112F0C46BD3CE27884
scaffold F62F76ED2E10032383F61F34E4B54BD93208E4F83496C33FC04450BE0172A8D9

NEXT: finish99614, inspect failures and correct complete affected groups without
weakening assertions; preserve failed TRXs. DIMS is focused-qualified only.
Then implement MNTR88-byte transfer independently; MaxDepth, MonitorSpec
pointer lifecycle and actual Exec AUTOINIT/ROM boot still pending. Preserve
independent331 ROM evidence and its unresolved hardware timing failures.
No CyberGraphX expansion, no commits, no unrelated shared-tree edits.

## DISP qualified by composite regression — historical checkpoint, 2026-09-06

Authoritative checkpoint; older sections below are historical. Goal ACTIVE.
No live graphics build/test sessions remain. Sessions82000 and28176 are gone.
Because28176's terminal build output was unavailable, confirmed no333 process
was live and completed a fresh full build18448: success15.33s,zero errors,
one existing xUnit2013 warning. No stale-assembly or test-exclusion workaround.

Broad disp-transfer-broad-regression.trx completed12,603passed/1failed/6skipped,
12,610total in19m23s. The sole failure was the complete
NativeDisplayInfoDataPublishesLocalQueryHeaderDoubleLongLength fact's stale
DISP56-byte return expectation. Corrected only that assertion to48 for DISP;
retained56-byte request, four tag/header-Length checks and direct/overlay routes.
disp-transfer-broad-legacy-corrected.trx passed12/12,zero skips,487ms, covering
the whole corrected fact, transferred-span guard, portable boundary class and
native branch audits. Exact test-name union with broad:12,604unique passes,
zero unresolved failures. This is a COMPOSITE gate, not an all-green broad TRX.
Preserve the original failed TRX and six unconfigured ROM observation skips.
Admission separately passed7,198/7,198 in332; do not rerun completed gates.

Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-disp-host-tail-20260906-333.
Artifacts: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-disp-host-tail-20260906-333.
Scaffold SHA256570F65DF13B939EABE7B76FD784A0268469E349972444FDF8BEA1035A2035619.
Unchanged databaseA672928D5AA9B5D48FDDFAEDF3A01E79D4C64DBB75ADD8C27EB68046DD4D4C38;
emitter524BADD7B342AD9783CAAF64309CF74E1B3958C0D42319150F14222762A2CA1B.

NEXT UNIT: DIMS transfer contract. Independent331 ROM observations establish
N for N<=26; otherwise26+8*floor((min(N,66)-26)/8). First add independent
portable admission/write-span boundary regressions, including partial rectangles,
exact-end addresses and wrap rejection. Then coordinate portable/native full
publication, admission and return values; retain provider-owned unsupported
native partials until explicitly implemented. Remove all writes beyond66.
MNTR88-byte transfer, MaxDepth, MonitorSpec lifecycle and real AUTOINIT/ROM
boot remain separate pending units. CyberGraphX remains excluded.
No user decision needed; continue implementation, do not end at a planning checkpoint.

## Historical DISP admission checkpoint — 2026-09-06

Session19140 is COMPLETE: disp-transfer-admission-corrected.trx passed7,198/7,198,
zero failures,zero skips,22m26s. This is the entire original admission filter,
not a sampled retest or a composite replacement for missing cases. Preserve the
original3,642-failure TRX as historical evidence. Do not restart/wait on19140.
The standalone compatibility goal records the precise pending DIMS/MNTR
out-of-range clear-store hazards. No production or test changes this turn.

Authoritative checkpoint. Goal ACTIVE. Previous turn was a verified wait; this
turn obtained terminal passing evidence and started the next required gate.
CURRENT LIVE SESSION:82000, disp-transfer-broad-regression.trx, in
C:/Users/vsys-admin/AppData/Local/Temp/graphics-disp-host-tail-20260906-333.
Uses the successful56661 build in codex-graphics-disp-host-tail-20260906-333.
Do NOT rebuild333 artifacts while82000 runs. Normal console verbosity now exposes
individual completed cases; the terminal TRX remains authoritative for totals.
Scope: NativeGraphics OR GraphicsLibraryPortableScaffoldTests OR DisplayInfoData
OR DBufInfo OR DoubleBuffer. No exclusions or narrowed replacement gate.

While that snapshot ran, inspected remaining host-facing uses of DisplayInfoChunkSize.
Corrected four additional successful DISP return expectations to48, retaining
56-byte requests. The malformed host-service decline still expects original56.
DisplayDatabaseReturnsNativeDisplayInfoDimensionMonitorAndNameChunks now seeds
and verifies0xA5A5A5A5 at offsets0x30/0x34, proving no writes to SDK terminators.
Added NativeOverlayDisplayInfoDataAdmitsOnlyTheTransferredDispSpan: request
uint.MaxValue with writable48-byte output plus a read-only8-byte tail; succeeds
with48, correct header, untouched tail and unchanged caller PC/cycles.
Kept the existing fully-read-only-output decline test unchanged.

Used a separate full build folder so19140's live artifacts were not modified:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-disp-host-tail-20260906-333.
Initial build14746 failed in unrelated AmigaBusTimingTests.cs (undefined
inputCycle/outputCycle at then-line5597). Read-only inspection showed that file
had since changed and the offending code was corrected externally. Did not edit
it, suppress tests, or run stale outputs. Full confirmation build56661 succeeded
21.43s,zero errors,one existing xUnit2013 warning.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-disp-host-tail-20260906-333/
disp-host-tail-focused.trx completed6/6passes,zero skips,499ms: all four corrected
host-facing methods, new tail test, and existing read-only-output decline.
This is additional focused evidence. Full admission now passes above; broader
graphics qualification is still pending82000.

Current scaffold SHA25671FDD46052DB4BD048145E814926350997DB6E93556C4F44FEDAE1B0961E5E70.
DatabaseA672928D5AA9B5D48FDDFAEDF3A01E79D4C64DBB75ADD8C27EB68046DD4D4C38 and
emitter524BADD7B342AD9783CAAF64309CF74E1B3958C0D42319150F14222762A2CA1B reverified
unchanged. Admission retains6B51DFB34F77CA0234450DB812CCFF7EB2F06D382B3D5F130A35D28EA6ED5C06.
Tracked scaffold diff check passes (LF/CRLF warning only).
Standalone compatibility document now separates superseded identity preparation
from current ordered units;330-qualified header/profile work is no longer
presented as pending. Real AUTOINIT/ROM boot lifecycle remains pending.

NEXT: collect live82000 without restart, inspect any failures and correct whole
affected groups without weakening guards/provider ownership/ABI checks. Record
terminal broad results and hashes before advancing DIMS/MNTR transfer corrections.
Normal console output can be large; summarize progress rather than inferring
exact totals from truncated chunks. Preserve the full goal and unresolved ROM
timing evidence. No user decision needed.

## DISP legacy regression correction — 2026-09-06

Authoritative resume checkpoint; older sections are historical. Goal ACTIVE.
Previous turn was progress. Recovered84937 as terminal: its handle is gone,
no matching graphics process is live, and its complete TRX exists. Do not restart
it or infer a stall from the stale LIVE marker below.
disp-transfer-admission-regression.trx completed7,198tests:3,556passed,
3,642failed,zero skips. Preserve this original failed run in332 evidence folder.
Failure grouping found17 shared portable seam helpers still asserting56-byte
DISP full results; the variable-length component/tail helpers additionally
expected49..55 bytes. Most native cases failed inside these helpers before their
own execution; this was not evidence of3,642 independent production defects.

Corrected17 full-result and buffer-length pairs to48, retaining56-byte requests.
Component/tail seam expectations cap at48; prefix comparisons and all66modes x
fourchip profiles remain. Tail byte validation stops at actual transferred end.
Tail exact-end/wrap scenarios now calculate addresses from min(prefixSize,48),
preserving their true boundary coverage at oversized requests. Following-byte
and complete memory guards, register checks and access/provenance checks remain.
Build87307 succeeded23.94s. disp-transfer-seam-corrected-smoke.trx passed16/16,
zero skips,3s: reserved-tail smoke plus portable boundaries and native edge cases.

Then corrected destination admission to accept byte-addressed odd DISP output,
use48 for exact-end/wrap spans and return actual length only when admitted.
Capability/default-monitor/public-frame tests likewise retain original requested
size on declines, and use48 for successful DISP output; other tags unchanged.
Legacy scaffold now expects48 for its seven full DISP queries while still asking
for56, and asserts bytes48..55 retain sentinel0xA5 instead of being cleared.
No production changes this turn. Do not blanket-label remaining failures stale.

Build54933 succeeded16.46s, zero errors, existing xUnit2013 warning.
CURRENT LIVE SESSION:19140, disp-transfer-admission-corrected.trx, same entire
7,198-case filter as original (whole admission class, transfer boundary class,
image profile class and two scaffold facts). Verified live by the test handle.
Artifact folder: C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-disp-transfer-20260905-332.
Evidence folder: C:/Users/vsys-admin/AppData/Local/Temp/graphics-disp-transfer-20260905-332.
Do not rebuild these artifacts during19140. Collect result, group remaining
failures, read affected methods and fix real behavior/contract issues, then
qualify the separate332 broad graphics union.330 PAL/NTSC qualification remains
valid only for its earlier snapshot; full replacement goal is not complete.
Current test hashes: admission6B51DFB34F77CA0234450DB812CCFF7EB2F06D382B3D5F130A35D28EA6ED5C06;
scaffold327D4EABE90B84FC30570E139F908C64461D67FE915E1DAD6D897B4F977E823C.
Production and other332 hashes retain the values below. No ROM timing fix,
CyberGraphX implementation, native AUTOINIT completion or throughput claim.

## DISP transfer correction implemented; qualification in progress — 2026-09-05

Authoritative checkpoint. Goal remains ACTIVE; no user decision is required.
330 PAL/NTSC alias integration is QUALIFIED: native-ntsc-profile-regression.trx/
session42663 completed12,588passes,zero failures,4unconfigured ROM skips,
12,592total,25m2s. This is an all-green broad test run, not composite evidence.
Assembly SHA2560CD82FB036B11D6FF416D3F552748575CA8BC2F82A37BCADE2C4D34F6DEF447E.
It qualifies the330 source snapshot recorded below, NOT newer332 DISP edits.
Do not repeat the completed330 broad regression or wait on closed42663.
331 per-tag ROM observations session83998 completed61passes/1failure/zero skips/
62total,11m3s. One PAL0x11000 MNTR sample hit the same Copper reservation defect:
address0x42A,request22452140,OUT22452142,complete22452144,busHorizon22452138,
ownerFree,executed22452140. Keep both failed ROM runs. The earlier longer sample
covers that mode/tag successfully. Exact union audit of COMPLETE passing samples
(15 longer cases plus53 per-tag cases, excluding all failed prefixes) proves
12,740 queries,54mode/profile/tag groups,129unique lengths in every group,
zero missing groups or transfer-count mismatches against the formula below.
Eight synthetic origin tests also pass. This qualifies OCS transfer observations,
not the longer/per-tag whole runs or a timing fix. Both probe sessions are closed.

332 evidence folder: C:/Users/vsys-admin/AppData/Local/Temp/graphics-disp-transfer-20260905-332.
New GraphicsDisplayInfoTransferBoundaryTests: four profile/handle cases sweep
nine mode IDs x70 lengths (0..65,88,96,128,uint.MaxValue), plus two exact mapping/
final guest byte cases and one genuine wrap rejection. Build41615 passed45.50s;
disp-transfer-red.trx:6fail/1pass, proving incorrect49-byte admission and rejection
of valid48-byte mapped spans. Portable GetDisplayInfoData and admission now share
GetDisplayInfoDataTransferLength; DISP caps48 while backing array remains56.
DIMS/MNTR/NAME remain unchanged. Build56723 passed10.86s; portable-focused7/7pass.

disp-transfer-native-red.trx/session25195:8DISPfail/24other-tagpass.
Native publisher now caps only DISP's admitted span, framed counter and successful
return to48. Original requested size remains staged for all provider declines.
The byte publisher handles capped full requests, including odd addresses; no live
DisplayFlags reads added. Build passed7.21s. disp-transfer-native-focused.trx/
session68767:39behavior passes,2stale fingerprint failures,41total,42s.
Measured PAL1444778/71535/3428/7/3346; NTSC1444678/71535/3428/7/3346.
Both fingerprints and scaffold assertion updated. Existing32profile matrix now
also tests DISP47/48/49/128/uint.MaxValue. Build17873 succeeded13.13s; session39944/
disp-transfer-final-focused.trx completed60/60passes,zero skips,43s (matrix,
portable boundary tests, complete image-profile class and scaffold branch audit).
Added eight NativeDisplayInfoDataDispCapsOddAndFinalAddressesAndPreservesDeclines
cases: both profiles x fixed/relocated x direct/function-array, each16 queries.
Tests cover odd0x301, exact last-byte0xFFFFFFD0, actual wrap0xFFFFFFD1 and foreign
mode decline at requests48/49/56/uint.MaxValue. Guard memory, no declined output
access, public registers, PC/SP and original declined size are checked.
Build77458 succeeded11.76s; disp-transfer-native-edges.trx passed8/8,zero skips,2s.
This yields68 focused passes, not broad qualification. Tracked diff check passed;
untracked files are not covered by that git check.

CURRENT LIVE SESSION:84937, disp-transfer-admission-regression.trx in332 folder,
using codex-graphics-disp-transfer-20260905-332 artifacts. Covers whole admission
class, portable transfer class, image profile class and two legacy scaffold facts.
Already observed one expected56/actual48 legacy scaffold failure; do not assume
all subsequent failures are stale. Collect the complete TRX, inspect each whole
failed method/group and correct contract expectations without weakening guards,
register/read-access/provider checks. Then rebuild and retest complete groups,
and perform a clearly separate332 broad regression. Do not call332 qualified
from330 results. No need to rerun completed68 focused tests unless code changes.
Do not rebuild332 artifacts while84937 runs. No other graphics session is live.
Current332 SHA256: databaseA672928D5AA9B5D48FDDFAEDF3A01E79D4C64DBB75ADD8C27EB68046DD4D4C38;
emitter524BADD7B342AD9783CAAF64309CF74E1B3958C0D42319150F14222762A2CA1B;
new boundary tests7F5D0F359B3626408752AA741DFAF452A2CAC4258480B6BBFA13E8900318F83E;
admission37E84076BCE0F4CA9C6671CEF5B44A70CA6748C092C1D9E9A6DAB4A6A767D5BD;
profile tests1B50E021E8E106EE2A5BA65230F33D93759905D78229CC78ED2546CD3DF51C9D;
scaffoldC69BABD027CD668211B6E44426721E8984393970F8FBA60FAEFC81B3DE57CEFD.
No commits, CyberGraphX work, ROM initialization completion or throughput claims.

## Native PAL/NTSC boot-profile integration — verification in progress, 2026-09-05

Authoritative resume checkpoint; older checkpoints below are historical.
Build30303 succeeded (21.32s, zero errors, existing xUnit2013 warning).
native-ntsc-profile-final-focused.trx/session85355: 53/53 passed, zero skips,
37s, including matching-code checks in both image builders. Broad regression
native-ntsc-profile-regression.trx is running as session42663 against that build;
four unconfigured ROM probe skips observed, no completion yet. Do not restart it.
Separate follow-on dense ROM transfer probe added to KickstartRomGraphicsDisplayInfoTests.cs:
18 fresh-boot mode/profile cases, each390 queries (lengths0..128, three tags,
plus full reference records), sentinel guards and original-ROM provenance.
Isolated build31105 succeeded in1m25s. Configured dense probe91579 completed:
15 passed,3 failed,zero skips,18total,5m18s. Preserve its failed TRX at
C:/Users/vsys-admin/AppData/Local/Temp/graphics-rom-dense-transfer-20260905-331/
rom-dense-transfer-observations.trx. Failures are all PAL Copper physical
reservation exceptions: mode0x8020,address0x470,request21315464; mode0x29000,
address0x47C,request23020712; mode0x29020,address0x484,request23304932.
Each reports OUT=request+2,complete=request+4,busHorizon=request-2,ownerFree,
latchRequester/Copper,latchKind/Copper,executed=request. No scheduler/chip fix.
All15 completed cases verify guard bytes, unchanged ROM provenance, and prefixes.
Their5,850 queries (1,950 per tag,129 unique sizes per tag across15 mode/profiles)
match: DISP=min(N,48); MNTR=min(N,88); DIMS=N forN<=26, otherwise
26+8*floor((min(N,66)-26)/8). Zero count mismatches. This is OCS-only evidence,
not a passing whole longer sweep or replacement conformance.

Added complementary per-tag fresh boots without removing the longer red probe:
54 cases x130 queries, plus8 synthetic origin checks. Build3313 succeeded24.05s.
Configured session83998 is RUNNING; expected62 tests, but collect actual result.
TRX: rom-per-tag-dense-transfer-observations.trx in331 evidence folder.
Uses separate codex-graphics-dense-transfer-20260905-331 artifacts; do not rebuild
them while83998 runs. Probe source SHA256:
877D30599C05AE02A4D7FEF3FD014183C235F8A6CA2E8C19E7EBA69E5391261F.
Other production code is unchanged during regression. Collect42663 and83998.
Next: audit all completed per-tag rows, then promote independent ROM count
assertions and add red replacement DISP transfer/admission tests.

Goal remains ACTIVE. Native code now specializes default aliases from the explicit
NativePal/NativeNtsc image profile; both convenience image builders forward that
profile. Queries do not reread live DisplayFlags. Default alias NAME classification
also now matches the portable policy; this is not independent ROM NAME proof.

Evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-native-ntsc-profile-20260905-330.
native-ntsc-profile-corrected.trx completed 52 passes and one stale PAL fingerprint
failure. All 32 profile query cases passed (14,416 combinations across profiles,
relocation, entry routes, tags, aliases, lengths and live flag changes); 18 image
layout cases and two branch audits passed. Measured PAL fingerprint:
1444752/71533/3428/7/3346; NTSC:1444652/71533/3428/7/3346.
The stale fingerprint is corrected. Matching-code and exact fingerprint assertions
are included in the 53/53 final focused pass above.

Recovery found no live graphics build/test process and no recoverable completion
output. Confirmation build30303 succeeded before focused/broad tests were started.
Do not rebuild size20-full-source-20260904-310 artifacts while42663 is running.
Other tasks have unrelated tests running; leave them alone.
NEXT: collect broad regression and dense ROM observations. Preserve any failed TRX;
verify the complete dense count matrix before promoting a transfer formula or
changing production copy/admission logic. No need to repeat the 53-case pass.
SHA256 for the tested330 source: emitterD75391E89A2F043B82F8B47CAAA9D0ABDDF432B833184FA6BDC5623CFEC4BBBD;
fixed builderE0F9A4F30F84770B55B0FE39039450D54774F7EE9095097EBCC73E02174AABEB;
HUNK builderA9BCDB8E4D9366DB6E4F54A6A29180B03EDABEAD69E57BB814B1EC125D727485;
profile tests73EE64B02ABCF83F5EBF1286653C166FDC0FD0BFBAAA13C70822852E9A266CC7;
admission777B46E378C4DB16CE6474F5901ED9CCA18A73EDE2608CD7597C1E38DA064878;
scaffold2242DEB98CD67654C0E0124D70A907BC5028DB91B044491AE2B0E44A6565576A.
Real Exec AUTOINIT positive initialization and generic ROM boot detection remain
unfinished. CyberGraphX remains excluded. No throughput claim is made.

Status: header-length correction is qualified by11,905 unique composite passes.
Explicit PAL/NTSC MNTR owner identity is QUALIFIED:1711 focused behavior passes,
then12,418 broad passes/zero failures/three unconfigured ROM-probe skips in20m53s.
Measured fingerprint1444564/71526/3428/7/3346; all source hashes match the tested
build. Evidence is monitor-owner-regression.trx in the325 evidence directory
recorded at the top of the replacement goals. Session11128 is complete.
CURRENT: portable default-alias identity and the existing PAL-native alias
path are corrected. The326 twelve native red cases now pass. A new complete
payload matrix also caught and fixed non-lores default aliases selecting NTSC
geometry/properties in full native records. Focused577 behavior passes and
119 payload/layout/audit passes are recorded in graphics-native-pal-alias-20260905-328.
PAL correction now QUALIFIED: broad12643 completed12,524passes/30 obsolete
header-expectation failures/3skips; corrected whole groups passed171/171.
TRX-name union proves12,554 unique passes,zero unresolved failures (composite,
not an all-green original broad TRX). No session is running. See top checkpoint.
Explicit NTSC-native selection, transfer-boundary and depth work remain pending.
New independent OCS PAL/NTSC boot probes in graphics-rom-default-selection-20260905-329
prove aliases follow the initialized boot profile and do NOT remap when only
DisplayFlags PAL/NTSC bits are changed afterward. Exact header assertions now
pass10/10 configured checks (162 queries across both profiles, plus8 origin checks).
This supersedes the previous uncertainty about a per-query DisplayFlags read:
do not implement that incorrect selection model. Native integration must use
the initialized database profile, with explicit backend/layout ownership.
Native positive-image profiles are now implemented and focused-tested42/42
(graphics-native-profile-20260905-327). CompactHost remains unchanged; native
PAL/NTSC images use0x24C minimum size, strings0x220/0x234 and DisplayFlags0xCE.
This is layout/initialization of mapped bytes only: native query semantics and
real Exec AUTOINIT positive-image initialization are still required. See the
latest top checkpoint for hashes, exact evidence and the integration next step.
This supersedes further pointer or
MaxDepth prefix expansion until the discovered record-contract mismatches are
addressed. It is part of the existing full replacement goal, not a new or
narrower definition of success. CyberGraphX remains provider-owned.

## Evidence and scope

The repository profile `CopperScreen/Profiles/a500-kickstart31-host-exec.json`
references the existing local fixture `CopperScreen/ROM/kickstart-3.1-a500.rom`.
It is 512 KiB with version word40. Do not download or redistribute ROM bytes.
Configure `COPPER_AMIGA_KICKSTART_ROM` to that absolute path and
`COPPER_AMIGA_KICKSTART_VERSION=3.1` only for the probe process.

`KickstartRomGraphicsDisplayInfoTests.cs` runs the ROM's display entry points
after the natural cold-start Intuition bootstrap on A500 PAL OCS. It verifies
that the entry jumps target the unchanged mapped ROM before and after a
successful observation run. No graphics replacement or synthetic blitter
leaf is installed by this probe. This is not ECS/AGA hardware conformance.

Evidence root:
`C:\Users\vsys-admin\AppData\Local\Temp\graphics-raw-rom-probe-20260905-322\ocs-capture`.

- `raw-graphics-ocs-capture.trx`: original red, requested88/returned66.
- `raw-graphics-ocs-transfer-diagnostic.trx`: same red with actual mode and
  buffer logged before the assertion. Default mode0 resolves to0x00021000;
  DIMS Length8, MaxDepth5,66 transferred bytes, untouched trailing sentinel.
- `raw-graphics-ocs-observations.trx`: 1,082 observations then a Copper
  physical-reservation exception. Eight synthetic origin checks pass. This
  is incomplete capture, not a passing whole-database comparison.
- `raw-graphics-ocs-fresh-boot.trx`: six valid-mode samples pass; three fail
  because the fixture used bit0x20 alone instead of SUPER_KEY0x8020. Those
  invalid IDs returned no data. This is a test-input mistake, not a production
  defect. Corrected fresh-boot samples use the complete SuperHires keys.
- `raw-graphics-ocs-fresh-boot-corrected.trx`: completed17/17 passing tests,
  zero skips,59s: nine fresh-boot samples and eight origin checks. Each sample
  records80 queries and57 positive transfers (720/513 total). The corrected
  SuperHires samples report MaxDepth2; Lores/Hires report5/4. Final build9.52s,
  zero errors, one existing xUnit2013 warning. This is observation validation,
  not replacement conformance.

The completed nine valid fresh-boot samples demonstrate:

| Record | Returned bytes for request128 | QueryHeader.Length |
| --- | ---: | ---: |
| DISP | 48 | 4 |
| DIMS | 66 | 8 |
| MNTR | 88 | 9 |
| NAME | 0 (not available in this cold-start fixture) | unobserved |

Default aliases resolve to a PAL record ID in this machine profile. Explicit
PAL/NTSC DISP and DIMS headers retain their canonical resolution-specific ID.
MNTR headers identify the monitor record (0x00021000 or0x00011000), including
when the query requests Hires. The sampled OCS Lores/Hires MaxDepth values
are5/4, respectively. Do not extrapolate AGA/ECS depth from these samples.

An audit of the completed fresh-boot TRX confirms the following transfer counts
in all nine sampled modes (each requested-size/tag pair has nine observations).
DIMS requests43/44 return42,55/56 return50,65 returns58, and66+ returns66.
All three available tags return the requested size at1,15,16,17,18,19,20.
DISP43/44 return43/44 and55+ returns48; MNTR remains bytewise at43,44,55,56,
65,66,67,87,88 and caps at88 for95,96,128. NAME returns zero throughout.
The DIMS observations suggest26+whole8-byte rectangles, but requests21..42
and the immediate rectangle boundaries still require a complete sweep before
freezing the rule. This evidence is no longer dependent on the incomplete
continuous capture; it is still OCS-only and not replacement conformance.

The [API return contract](https://d0.se/autodocs/graphics.library/GetDisplayInfoData)
is the number of bytes actually transferred. The
[SDK structures](https://d0.se/include/graphics/displayinfo.h) include reserved
terminators; sizeof(struct) does not establish transfer size or Length.
NAME absence may depend on loaded display data; do not globally remove names
or freeze a NAME header constant from an absent record.

## Historical identity preparation (superseded by330)

Record-identity preparation (read-only investigation while header regression
64393 runs): portable `ModeRecord.ResolveDefaultProfile` changes PAL/NTSC
geometry but deliberately retains the alias ModeId. `BuildData` currently
writes that ModeId for every tag; `Registry.FindOrCreateForDisplay` already
resolves the monitor owner independently using mask0xFFFF1000 and its supplied
defaultMonitorNtsc profile. Do not change registry identity merely to correct
the public header.

The SDK's [GfxBase definition](https://d0.se/include/graphics/gfxbase.h)
documents power-on DisplayFlags (NTSC1/PAL4). That does not by itself prove
that changing the field later changes ROM alias resolution. Confirm the
selection contract and input envelope before using it in native code.
`CopperMod.Amiga/Firmware/GraphicsLibraryImageLayout.cs` currently places the
host image's name at0xA0 and ID string at0xB4, inside native GfxBase field
space; even its optional full0x220 envelope retains those string locations.
Do not mistake compact host string bytes for native DisplayFlags. Future
native-compatible field publication needs an explicit layout/initialization
path, while preserving the allowed compact-host alternative.

Split identity work at the genuine dependency: explicit PAL/NTSC MNTR header
owner IDs can be corrected with the existing monitor mask without new A6
reads; default aliases require a verified PAL/NTSC-selection and layout
contract. Both remain required. Tiny prefixes that do not publish any
profile-dependent ID byte should not acquire speculative input reads.

## Ordered implementation units (current)

1. Preserve and audit independent ROM observations. Keep the original failing
   compatibility assertion visible until replaced by a genuine differential
   comparison. Keep the continuous-sweep timing failure visible; fresh-boot
   samples complement it and must not claim to fix scheduler behavior.
2. QUALIFIED: QueryHeader lengths for verified DISP/DIMS/MNTR are4/8/9 in
   portable and native full/partial records, with11,905 composite passes.
   NAME retains its prior policy pending actual named-record evidence.
3. QUALIFIED through330: public header identity, default alias resolution and
   MNTR monitor-owner IDs, explicit PAL/NTSC image/code profile selection and
   read-free tiny prefixes. Native positive-image layout is implemented, but
   real Exec AUTOINIT initialization and generic ROM boot detection remain
   lifecycle work. Preserve canonical and foreign ownership; private handles
   are not ROM DisplayInfoHandle pointers and require an explicit adapter boundary.
4. Correct transfer lengths, structured partial-copy boundaries, and untouched
   bytes. Sweep every boundary around scalar/rectangle/padding fields and
   include requests beyond structure size. Do not cap every tag to66 or make
   every partial record a bytewise prefix based on the current portable oracle.
   DISP is qualified by333 composite evidence. DIMS portable/full-native
   correction is qualified by334 composite evidence (top checkpoint).
   MNTR88-byte transfer correction is broad-qualified335:12,646passes,
   zero failures,six unconfigured ROM skips; session80437 complete.
5. Correct MaxDepth from mode/profile evidence (including HAM/EHB/dual-playfield)
   and then implement native DIMS17/18. Separate OCS-derived facts from ECS/AGA
   requirements. Obtain profile-specific observations before freezing those.
6. Resume the MonitorSpec pointer goal after its header and transfer assumptions
   are corrected. Continue native registry/lifecycle work for non-default
   monitors; a default-only prefix remains an intermediate implementation unit.
7. Complete the remaining native public-copy paths, not just safe declines:
   DIMS19..65 after the depth-dependent17/18 unit, and MNTR17..87 alongside
   its pointer/registry prerequisites. Implement legal odd destinations using
   byte-safe publication. Differentially verify scalar and complete-rectangle
   boundaries, requested sizes, aliases and profiles, and untouched tails.
   Provider-owned fallback for recognized records is an INTERMEDIATE boundary,
   not sufficient evidence of full native Kickstart compatibility. Retain
   genuine foreign/CyberGraphX ownership; that support stays out of scope.

For each unit: add failing tests, implement portable semantics then adapters,
verify original ROM comparisons where available, and run focused plus broad
regression gates. Record hashes and exact terminal TRX results. The existing
11,858-pass native/portable regression is internal consistency evidence only;
it cannot override observed Kickstart differences or prove the full goal done.

## Transfer-unit implementation map (2026-09-06, current)

DISP48-byte cap is qualified. DIMS has the shared scalar/complete-rectangle
portable transfer resolver and full native66-byte admission/publication/return;
native LONG clears0x40..0x54 were removed. Scalar words0x10..0x18 and five
rectangles0x1A/0x22/0x2A/0x32/0x3A fill through0x41. Its334 composite broad
gate is qualified. No MaxDepth semantics changed merely to fit transfer lengths.
Full default MNTR now excludes clears0x58/0x5C and transfers88 bytes, ending at
the ModeID LONG at0x54. Minimum request, admitted span, clear loop and result
were coordinated in335 (101focused and12,646broad passes). Non-default MNTR
ownership and unsupported native partial payloads remain lifecycle/publication work.

Do not conflate SDK backing-buffer size with bytes actually transferred.
Portable GetDisplayInfoData and TryGetDisplayInfoDataOutputLength now share
GetDisplayInfoDataTransferLength. Preserve this side-effect-free resolver
between admission and publication while backing arrays retain SDK envelopes.
MNTR now caps88. Do not preflight,
rewrite, roll back or demand mapping for untouched trailing bytes. Check wrap
bounds using actual output length. Preserve mode/handle/provider precedence,
null/zero behavior and NAME policy. Test uint.MaxValue requested lengths too.

Native AppendDisplayInfoDataCanonical has separate short-prefix and full-record
paths, with inline emitted copy stores and size-dependent destination admission.
Changing only a return register or one chunk constant is insufficient. First
correct one tag end-to-end (DISP is the smallest), then DIMS and MNTR separately.
Keep unsupported native partial payloads provider-owned until implemented;
test that fallback preserves the original size and all public inputs. After
each slice audit branch ranges and qualify fixed/relocated, both profiles and
direct/function-array entry routes. Explicit/alias identity remains unchanged.

Dense331 and complementary per-tag probes are COMPLETE. Their complete passing
cases collectively cover54profile/mode/tag groups x129distinct sizes with
12,740observations and zero transfer-count mismatches. Preserve both failed
whole-run TRXs and retained Copper physical-reservation failures. This is
composite observation evidence, not an all-green ROM run or a timing fix.
Do not change scheduler/chip behavior in this graphics transfer unit.

## Historical identity implementation map (2026-09-05)

During explicit-owner broad regression11128, read-only inspection established
the following separation. No native profile reads or default-alias code changes
have been made yet.

- Portable GetDisplayInfoData already receives defaultMonitorNtsc and resolves
  geometry before BuildData. Correct the public header independently; do not
  change ModeRecord.ModeId globally, because provider calls and enumeration
  consume it too. For verified DISP/DIMS, a default alias publishes the selected
  monitor OR resolution key; MNTR publishes only that monitor. Keep NAME policy
  separate. Tests must distinguish PAL/NTSC and prefixes that first expose the
  monitor byte, with provider arguments and failure-side effects unchanged.
- Native builders both call GraphicsLibraryImageLayout.CreateGuestImage.
  Fixed-image Build and relocatable HUNK Build accept positiveImageSize, but
  neither currently accepts a default-monitor profile or explicit layout kind.
  Merely increasing size to0x220 still puts strings at0xA0/0xB4. Existing tests
  named FullNativeGfxBaseImageInitializesTheOptionalMonitorListEnvelope and
  NativeGraphicsImageBuilderCanOptIntoTheFullGfxBaseEnvelope validate the tail
  list, not the absence of string overlap in other native fields.
- A native layout path must place strings outside its field envelope and keep
  name/ID pointers, relocation records, positive-size publication and AUTOINIT
  allocation coherent. Preserve compact-host defaults and test both variants;
  do not silently reinterpret existing positiveImageSize callers.
- The SDK DisplayFlags is a power-on field (NTSC1/PAL4), not proof of dynamic
  alias remapping. Confirm the ROM selection behavior and exact native offset
  before emitting an A6 read. Neither byte0xCE of a compact image nor a
  MonitorSpec flag is a substitute for that contract. Preserve read-free tiny
  prefixes where no profile-dependent byte is copied, and validate alignment,
  wrap bounds, unavailable inputs and both native entry routes when reads begin.

These are safe in-scope implementation dependencies, not a request for user
approval. Resume from the live top checkpoint and advance the next bounded unit.
