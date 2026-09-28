# Native MonitorSpec selection unit

Part of the full Kickstart 3.1 graphics replacement. CyberGraphX stays excluded.
Native boot construction/teardown and AUTOINIT are qualified in the predecessor
MonitorSpec unit. This unit extends selection; it does not complete graphics.

## Contract and implementation order

1. Record original Kickstart 3.1 monitor-list residents and selectors on PAL and
   NTSC. Check the actual OpenMonitor/CloseMonitor entry vectors against unchanged
   ROM, not just the display-info vectors. Check original Exec allocation/free
   provenance, open/close count balance, unchanged list and no allocation leaks.
2. Turn observed selector outcomes into explicit regression expectations before
   broadening replacement admission. Cover null name/default/explicit IDs, names,
   case handling, name precedence over ID and unknown selectors. Do not infer
   monitor availability from the name alone or from host parity.
3. Implement the already-resident native default selector paths, preserving
   input registers on fallback and callee-saved registers on success; no new
   allocations or implicit foreign monitor ownership. Qualify fixed/relocated
   native bodies against original ROM and host behavior. Record any discovered
   host mismatch separately instead of treating it as an oracle.
4. Extend list-based/non-default ownership and integration in subsequent small
   verified steps until full monitor compatibility is attained. Do not treat the
   first default-only extension as completion of this requirement.

The original OpenMonitor autodoc specifies that a non-null name selects by name,
a null name selects by ID, and both null selects the default. It does not by
itself establish aliasing, case sensitivity or all accepted ID variants:
https://www.theflatnet.de/pub/cbm/amiga/amigadev.elowar.com/read/ADCD_2.1/Includes_and_Autodocs_2._guide/node05A6.html

## Source map

- NativeGraphicsRasterBodies.cs with MonitorNames/MonitorIds partials: named
  default alias/public-list exact-name matching and22 mode keys; CMMO2 registered
  explicit family admission. NULL/INVALID_ID and consistent exhausted named lists
  return NULL. Malformed/bounded-out searches and unregistered explicit IDs decline.
- GraphicsMonitorOperations.cs/Names.cs: mapped public MonitorList drives exact
  named selection; default.monitor is a case-insensitive pointer alias. Compact
  host backend availability is separate, not proof of original-ROM residency.
- KickstartRomGraphicsMonitorSelectionTests.cs: new original-ROM selector probe.
  Two profile cases each exercise294 requests twice, plus independent name/flags
  mutation controls, with original-ROM vector provenance and logged TRX outcomes.

## Evidence — 2026-09-15 / 415–416

415 build PASS63.71s, final incremental14.86s. Strict two-profile probe ran all
32 selector requests per profile, then FAILED the final no-allocation assumption:
AvailMem994008 ->988472, a5536-byte change in both profiles. Both tests reached
the final assertion; all resident/list/refcount checks passed.68521 CLOSED.
These are not replacement failures; they invalidate that oracle-test assumption.
416 pins the observed selector matrix and repeats the entire matrix while logging
per-request allocation deltas, requiring stable memory on the second pass. Do not
call the first-pass allocation a leak or a cache until repeated evidence supports
that conclusion. Follow canonical CURRENT for live build/test handles.

Observed original-ROM matrix (one default resident named pal.monitor or
ntsc.monitor; no opposite-family driver installed):

| Request | PAL | NTSC |
| --- | --- | --- |
| NULL name, FFFFFFFF | NULL | NULL |
| NULL name, 0/4/8000/1000 | default | default |
| NULL name, 11000/19004 | NULL | default |
| NULL name, 21000/29004 | default | NULL |
| NULL name, FFFF/31000/DEADBEEF | NULL | NULL |
| default.monitor or DEFAULT.MONITOR | default | default |
| pal.monitor | default | NULL |
| ntsc.monitor | NULL | default |
| PAL.MONITOR or NTSC.MONITOR | NULL | NULL |
| empty/unknown.monitor/pal/ntsc | NULL | NULL |

Named cases used both FFFFFFFF and DEADBEEF IDs with identical results, proving
name precedence for this matrix. CloseMonitor returned0 and restored the count.

### Required compatibility corrections

- The earlier native arm accepted INVALID_ID as default and declined0; original
  ROM does the opposite.418–419 correct this. Earlier414 tests used INVALID_ID only on the
  replacement, so they prove native lifecycle/ABI, NOT selector parity. Correcting
  that behavior and those stale expectations was required; setup opens in
  ROM-native integration fixtures now usezero. Do not restore the wrong alias.
- Host TryResolve previously accepted INVALID_ID and case-insensitive canonical
  PAL/NTSC names.417 corrects both against the pinned ROM contract while retaining
  the separately observed case-insensitive default.monitor alias.
- Opposite-family host allocations are not evidence that boot ROM has installed
  the corresponding driver. Model availability/registration explicitly as later
  ownership work; do not conflate host support with original boot residency.

## CURRENT checkpoint — 2026-09-17 / 452

Host ID selection now reads the same CMDO/CMDB3 authority as native lookup,
resolving default-family aliases from stored state. Empty registration claims
NULL; malformed state declines without allocation/adoption. Reference writes
reuse byte-wise rollback. Exactzero uses a published default before private-state
reads; INVALID_ID is claimed NULL without GfxBase reads. Compact host backend
remains separate.452 focused239 registered/name/close checks plus two corrected
compact INVALID_ID tests all passed (241 total);148 new cases cover91 requests
each across PAL/NTSC and direct/register calls. Final monitor regression COMPLETE
3012 passed,0 failed/skipped,2m16s;67066 CLOSED;
prior full447 LIVE35309. Follow canonical publication CURRENT for paths/results.
MNTR readback/default-family and host publication/rebind integration remain.

## Historical checkpoint — 2026-09-17 / 447

Native nonzero-ID lookup now selects CMDB3 Mspc before DefaultMonitor admission;
only exactzero bypasses the database. Names retain precedence. Stored default
family resolves aliases, valid empty mappings claim NULL, invalid state declines.
Associated borrowed/unlinked nodes need no CMMO ownership/name/flags/list match.
446 native selection520 checks passed;447 independent-pointer/fault cases and
real-Exec mutation/close coverage now passed: monitor2864/0/0, strict16/0/0,
profile/audit28/0/0. New measured native fingerprint:
PAL1444822/NTSC1444722bytes,71812branches,3428relays,7chains,3346boundaries.
Prior443 full COMPLETE14055/0/22,20m2s;63806 CLOSED. Follow canonical CURRENT for
447 live full union35309; do not duplicate it. Host selectors and MNTR readback
are still incomplete.

## Historical checkpoint — 2026-09-17 / 445

Native CMDB3 registration publication and paired teardown are implemented.
AUTOINIT publishes the selected mapping before the public node; teardown clears
only references to its retiring node before freeing it. Independent CMMO paths
preserve their foreign-CMDO boundary.444 focused checks651/0/0;445 monitor2688/0/0,
strict original-Exec registration/AUTOINIT16/0/0 and profile/audit28/0/0 all passed.
Only prior443 full union63806 remains live. Follow canonical CURRENT in
GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md for live runs and next action.
Nonzero-ID/MNTR consumers still need routing to the new slots; exact ID0 alone
must use DefaultMonitor. This lifecycle slice does not complete selection.

## Historical checkpoint — 2026-09-17 / 443

CMDB3 construction is implemented: preserve old mode/position offsets, append
stable default-family ID and two borrowed MonitorSpec registration slots. Native
and host producers propagate PAL/NTSC; pointers initialize tozero. Public ABI,
CMDO and CMMO remain unchanged.282 construction/profile/audit and2468 monitor tests
passed; strict native/original-Exec initialization36 cases also passed in5m4s.
Only full443 union63806 is live; canonical CURRENT owns paths and continuation.
Next integrate registration publication/removal and ID/MNTR reads. Empty v3 slots
are not yet consumed by OpenMonitor; representation alone is not full registration.
See GRAPHICS_LIBRARY_MONITOR_ID_REGISTRATION_GOAL.md for schema and remaining units.

## Historical checkpoint — 2026-09-16 / 442

435 full union COMPLETE14014 passed,0 failed,21 ROM-conditioned theories skipped,
21m49s;17394 CLOSED. Latest host439 remains91 focused and2436 monitor passes.
441 now probes original-ROM ID registration by changing only an installed
family's MonitorInfo.Mspc via original private SetDisplayInfoData(-750), reading
back through original GetDisplayInfoData, and observing selection while the
candidate is unlinked/linked/renamed/flags-cleared/unlinked again. The database is
restored before freeing the candidate and original graphics/Exec vectors checked.
441 passed2 profiles/15s;442 pins the contract and expands to330 selector checks
per profile (66 IDs across five stages), PASSED2 profiles/15s. Exact ID0 selects
DefaultMonitor; every tested nonzero valid ID follows MNTR.Mspc even when unlinked,
renamed/flags-cleared. Null Mspc yields NULL for those nonzero IDs. Registration
and restore caused no allocation changes; reference counts and snapshots restore.
No replacement registration behavior broadened yet. All handles closed. Follow
GRAPHICS_LIBRARY_MONITOR_ID_REGISTRATION_GOAL.md for the next native/host units,
especially versioned database mapping distinct from CMMO allocation ownership.
Source for private ABI: original SDK Function.offs
https://amigadev.elowar.com/read/ADCD_2.1/Includes_and_Autodocs_2._guide/node0550.html
Source for public ID/name contract and display-database relationship:
https://wiki.amigaos.net/wiki/Display_Database
Full goal incomplete; no blocker. CyberGraphX remains excluded.

## Historical checkpoint — 2026-09-16 / 439

436 implements host public-list name order/alias selection, including raw-byte
case-sensitive names, exact empty names, duplicate precedence and independent
default alias. Full mapped list is authoritative; compact unmapped-list host
backend remains separate. Returned associated pointers are borrowed, not owned;
count increments roll back rejected/partial WORD writes and CloseMonitor balances
them without adoption/free.436 focused77 passed.437 adds alias independence from
list mapping and refuses partial-list fallback allocation, with10 additional
two-profile cases. Canonical publication CURRENT owns live build437, regression436
and full-union435 handles and next verification.
436 regression2422 passed;437 focused87 passed.438/439 adds host named NULL-versus-
provider-decline propagation through core/register adapter, checking tailPred
before claiming complete absence. Four register cases preserve all input D/A
registers on malformed/partial lists; complete absence only sets D0 tozero.
Shared backend path avoids duplicate compact-list probing. Latest build/test
handles are in canonical publication CURRENT.
439 build PASS21.43s; focused91 passed,0 failed/skipped,2s.437 monitor regression
2432 passed,0 failed/skipped,2m37s; latest439 monitor COMPLETE2436 passed,0 failed/
skipped,2m25s. Only older435 full union remains live in canonical CURRENT. Do not
launch duplicates or treat old full coverage as proof for the new host adapter
path; the latter is covered by439 focused/register and monitor tests.
Still required: complete unbounded/task-serialized selection, non-default ID
registration, remaining standalone failure semantics and full driver lifecycle.
Full graphics remains incomplete; no blocker.

## Historical checkpoint — 2026-09-16 / 435

433 focused640/0/0 and strict original/native ROM20/0/0 passed. Canonical
publication CURRENT owns run handles/paths and latest disposition.432 failures
were268 fixture provenance failures plus3 stale tuples, not emitted behavior:
branch-range fallback RTS clones must be tracked alongside the canonical RTS.
433 adds construction-only clone-offset metadata, validates aligned RTS/branch
targets, and uses those PCs in both monitor fixtures (no D0 heuristic). Native
bytes unchanged since432: PAL1444670/NTSC1444570,71798 branches,3428 relays,
7 chains,3346 boundaries.434 additionally makes clone-target validation linear.

434 implements host borrowed CloseMonitor reference balancing after unlink:
validated full pointer/base envelopes, type18/kind0204/library association;
no list read, ownership adoption, allocation/free or driver call. Existing count
transaction rollback applies, including a partial high-byte write. New17 tests
cover success, malformed association, unreadable data, write refusal and retry,
and assert that _resident ownership/display lookup remains unchanged.
434 host/profile45 and monitor2362 passed,0 failed/skipped.435 adds two public
register-adapter cases proving host compatibility close versus unchanged native
overlay refusal (resident/provider owns that path); no production changes.
435 host/profile COMPLETE47 passed,0 failed/skipped,2s. All current run handles
are closed; full union435 not yet run. Host list-name ordering/alias parity remains
next after broader verification; explicit
non-default IDs, task-level synchronization and full unbounded behavior remain.
No code blocker; full graphics goal incomplete. CyberGraphX remains excluded.

## Historical checkpoint — 2026-09-16 / 432

429 original-ROM list probe COMPLETE2 passed,0 failed/skipped,18s. First exact
registered-name match wins (including empty string), duplicate canonical names
before the default win, but case-insensitive default.monitor always selects the
default. Closing a held reference after unlink decrements it without freeing.
Fixture registers/reorders two nodes with original Exec under MonitorList lock,
checks original entry provenance, list/count balance, no allocation change and
restores all original state.430/431 adds the native variant of that same probe.
431 strict original/native-list plus AUTOINIT/lifecycle COMPLETE20 passed,0
failed/skipped,2m35s;42760 CLOSED.

Native name selector now traverses actual MonitorList, independent of default
pointer except the alias. Guards base/header/tail, full nodes/type/kind/backlink,
predecessors and byte-string wrap. A consistent complete miss returns NULL;
malformed/unterminated/over-limit searches tail-chain with requests restored.
Associated-node close checks the full envelope/type/kind/backlink and decrements
nonzero count without list/allocator operations, including an unlinked reference.
Legacy default/provider admission remains for other envelopes. Named traversal
has a28-byte frame separate from44-byte refcount frame; close adds no frame.
Explicit non-default ID registration, host registry/list parity, task semaphore
integration, long names/large lists and complete fallback semantics still remain.
The native list path currently requires serialized list/lifetime mutations.

431 focused629 passed/7 failed (three fingerprints and four test provenance
heuristics equating input/output zero with fallback).432 fixes the test to observe
fallback PC only, and allows tail-sentinel processing after exactly256 nodes;
only a257th non-sentinel is refused before dereferencing. Added complete-miss
at256 nodes. Native112 list cases (four routes), four close routes each with eight
scenarios, previous name/ID/base/envelope checks retained.432 build PASS23.95s;
5652 CLOSED. Current tests in canonical CURRENT; no blocker. Full428 union now
COMPLETE13898 passed/0 failed/20 skipped,17m35s;86136 CLOSED, final rows verified.

SHA256 production432:
- MonitorNames/list/close helper:14C46E7EC23C064B65699E733528DE11DCAB282C22C44E13D6763FBF1A9D1457
- Main emitter:EEDFA66A97255BAC90A93F7295A1E3EDF4DB68ABBE7E76E1B1FAA879C78B96E3

## Historical checkpoint — 2026-09-15 / 428

426 expands original-ROM contract to294 requests/profile/pass, two passes, and
independent mutable-name/mutable-flags ID controls. COMPLETE2 passed,0 failed,
26s;11369 CLOSED. All22 OCS/ECS mode keys resolve the installed monitor even on
OCS (no SuperHires execution capability needed). Explicit PAL/NTSC requires bit12;
high-word-zero/default accepts either spelling. All12 unsupported/control probes
reject. Opposite/uninstalled families reject. ID registration survives renaming
and clearing timing flags.425 initially hypothesized bit12 optional on explicit
families:44 mismatches/profile, corrected and reverified426. No other mismatch.
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-426/monitor-id-matrix.trx

427 adds an explicit guest registered-ID field to private CMMO2 at278 (extent27C,
version2). Public SDK layouts and CMDO/CMDB remain unchanged; old CMMO1 is declined.
The publisher writes the ID before the valid tag and release validates/clears it
before yielding to FreeMem. Native syntax admission uses all22 keys, explicit
marker semantics and no chipset gating. Explicit IDs must match admitted CMMO2
owner/ID/allocation/size/backlink and lib_PosSize, not name/flags/build profile.
Separate12-byte D0-D1/A0 frame restores requests before refcount or fallback;
maximum44-byte frame remains. Other residents/unmatched explicit IDs still decline
to provider; not yet complete standalone failure behavior or list registration.
CloseMonitor already balances these successful opens because they return default.
Non-default extension must co-change CloseMonitor before admitting those nodes.

Host selector now uses shared ID syntax instead of ModeNotAvailable, fixing OCS
SuperHires and default bit12 aliases. Supported-family host allocation is retained;
actual host monitor-registration availability remains a separate full-goal issue.
427 build PASS19.76s; focused882 passed/3 stale fingerprint failures,56s.428 adjusts
only measured tuples (PAL1444386/NTSC1444286,71770 branches,3428 relays,7 chains,
3346 boundaries) and adds16 private-base/allocation-wrap cases. Structural audit
passed independently. Build428 PASS20.34s; production unchanged since427.
Canonical CURRENT owns execution handles and next action. No blocker; no
CPU/chipset/CyberGraphX changes. Full task/driver/list lifecycle remains unfinished.
427 strict ROM COMPLETE20 passed,0 failed/skipped,2m30s;23203 CLOSED. Production
is unchanged in428.424 broad union COMPLETE13754 passed,0 failed,20 skipped,
17m58s;62260 CLOSED (predates this registration change). Final result rows verified.
428 focused admission/lifecycle/profile/branch/host matrix COMPLETE901 passed,
0 failed/skipped,1m20s;54899 CLOSED, final TRX counters/rows verified.428 monitor
regression COMPLETE2229 passed,0 failed/skipped,2m32s;89227 CLOSED. Only full428
union86136 remains LIVE; canonical CURRENT owns run handles/paths and next action.

SHA256 production:
- Layout:A3ED1B92992D5C8E4F28EF98B05DC573C1CCAB6EE731ED110287D40B6A8F88DB
- MonitorIds emitter:63AEC017C0DE1120013ACDFE2F37DCA970039A1790EF8132157895003B297B5F
- Shared selectors:D875C60E874C3383FEC099AB179B2364DDF8BA82139C0C234A5F0E3BC18DFE3C
- Publisher:45221D44BA25A7A267C85B970A3C5CB200D5AE538C1880EFD1D29B3A05230157
- Release:4A4FAB1BE5183A21543E7BC987C0CA8BAAF9AA0BC73990D80116CE72EFB7A226
- Main emitter:FF729BF3CF9BC4A913334F8A73A8F8227A229F1C4AE771C86D67FED83AF67B30
- Host operations:2C003861A701DC51C11253E69953A421C9ECE650207957195CB55B980AD709A4
- ROM selector test:601EFB3AA121B47E80FB37DA5248017B598F08E3BA522A58CFF9F4481C38FE26
- Admission test:363DF2AE4492529BD4BC4C0A8381C5A9224F522DDFD91A51B90A5FA0ABDAFE39
- Profile tests:BB393E173A52BC92F82939745B677CF44E8589CD3146B1921164EE8DB79B0405
- Scaffold tests:2CBA266A645943A6338BFF125A63F24D940C79D61764FD5A23153841EE7D7BBB
- ROM composition:10E0FADD9FE51BB85C4A8E38F306E69171A9A4D3B586A22B6699CA753A14CC59
- Publisher tests:1E9A3C2E360346D50D1D7123BDA58AD9CBED88340E694151289ADB2EB8205608
- Release tests:CE5E1335AC197EA3D52CDB9694A20E47ADA31FC6F6D5F4606D1D0CF50C275481

## Historical checkpoint — 2026-09-15 / 424

Default ID aliases4/8000/1000 now share zero's guarded native resident path; full
longword comparisons preserve the original requested ID for every decline.
Named requests still precede ID matching. This deliberately admits only the
existing ROM-proven matrix, not all low-word flags or non-default families.
No allocations or ownership changes. Added12 success cases and4 route cases
each covering24 malformed/saturation/name-precedence requests. Native ROM
composition checks balanced opens/closes and no allocation for all four IDs.
423 build PASS18.84s;303 passed/5 failed in focused run (34s). Three measured
fingerprint updates required; two new malformed-A6 fixture cases wrongly tried
negative-vector lookup through that invalid base.424 uses direct-body admission
for those cases, retains independent AUTOINIT calls, seeds empty/canonical names,
and updates fingerprints to PAL1443954/NTSC1443854,71729 branches (all structural
checks passed),3428 relays,7 chains,3346 boundaries. No production change after423.
424 build PASS24.11s; focused COMPLETE308 passed,0 failed/skipped,36s;2858 CLOSED.
Follow canonical CURRENT for remaining live execution. Tests are correctness,
not G6/G7. No CPU/chipset changes; CyberGraphX remains excluded.

SHA256424:
- Main emitter:CC9F39F0B87A4342EE06449609D45801A65BABC78738D27E7E364DD88E3C00BC
- Admission tests:1DA80FBF307F749D32C5FEBCE4A33A9266FB58BF9DA6F2E07C818A125A42F68B
- ROM composition:4DD3F76ECB45A73BDD2E438986AEA6CB5F2CBBE0070022FDDC724087FBC34841
- Profile tests:F2338A1478A3AC3DCC22CF9394F8230863AF6A3738EECF77F9BA93FE4796C1D0
- Scaffold tests:BA562D102D4E536A1D4ADDD57C277E4209953E8E9BD141C1444BFD31DD136ED2

422 strict COMPLETE20 passed,0 failed/skipped,2m16s;46062 CLOSED.
422 monitor COMPLETE2069 passed,0 failed/skipped,2m14s;71010 CLOSED.
419 full union COMPLETE13566 passed,0 failed,20 skipped,20m40s;2199 CLOSED.
All final TRX counters and actual result outcomes verified. No old live sessions.
424 strict original-ROM/native AUTOINIT COMPLETE14 passed,0 failed/skipped,1m43s;
59115 CLOSED. Focused308/strict14 final result rows verified. Full424 union62260
started (no ROM environment); monitor10920 still live at checkpoint. Canonical
CURRENT owns handles/paths; collect without restarting.
Next explicit-family selection and non-default-list selection
with balanced CloseMonitor support. Full graphics remains incomplete, no blocker.

## Historical checkpoint — 2026-09-15 / 422

AppendResidentDefaultMonitorNameAdmission in new partial MonitorNames.cs handles
the already-selected default's names. A1 names take precedence over D0 before
zero/INVALID_ID handling; GfxBase/MonitorSpec admission precedes string reads.
The default.monitor alias folds ASCII letters; the actual xln_Name comparison
is exact/case-sensitive. Name pointers may be odd; each byte read is guarded
against32-bit wrap. Matching has a64-byte including-NUL admission bound; longer
or malformed strings fall back without claiming NULL or modifying references.
This is not full non-default/list/driver selection, nor full standalone failure
behavior for unmatched names. Those remain explicit full-goal work.

Named matching uses a24-byte D0-D2/A0/A2-A3 frame and restores all inputs before
the existing refcount frame or fallback. Maximum frame remains44 bytes because
the frames do not nest. No allocation, list mutation or semaphore claim added.
168 new tests (PAL/NTSC names, fixed/HUNK and public/AUTOINIT routes) each use
INVALID_ID and a foreign ID, covering aliases/case/precedence,63-byte success,
unterminated refusal, empty/prefix/suffix/opposite names, odd and last-byte
pointers, wrap refusal, null resident-name pointers and saturation. They also
compare complete memory snapshots and callee-saved registers/stack guards.

421 build21.88s;265 behavior tests passed,3 old fingerprint assertions failed,
35s. All structural audit checks passed.422 adjusts only measured fingerprints:
PAL1443916/NTSC1443816 bytes,71725 branches,3428 relays,7 chains,3346 boundaries.
Original-ROM selector probe now has36 requests per pass (72/profile), adding
mixed-case alias/canonical requests. Native AUTOINIT success cases additionally
open four accepted name forms with INVALID_ID/foreign ID, close each and verify
no extra allocations. Current build/test handles are in canonical CURRENT.

SHA256 before the final422 test-only fingerprint update:
- Names helper:4F90414F94CB8ACB0C61B606508F20EAE7438C286C61DE42EFDB1ECA43688E66
- Main emitter:D11649C5163A4D147DAE0100382209E87808784F5FABCFDE3932A1F86EABBE71
- Admission tests:0D97E3B731131149AFF8ECEDECBBDAD42A15E1E7624F271DEB4F84F6E288FA68

Next collect422 qualification and live419 union, then implement explicit-ID and
non-default resident-list selection, preserving the ROM-proven availability
boundary and provider fallback. Full task synchronization, native drivers and
Intuition/view lifecycle remain required; do not mark full graphics complete.
When adding non-default OpenMonitor, co-change CloseMonitor's default-only
admission so successful opens have a native balanced close path. Do not adopt
driver allocation ownership merely because a node is registered in the list.

422 build PASS19.14s; admission/profile/audit COMPLETE268 passed,0 failed/skipped,
34s;16666 CLOSED. Strict20-case46062 and monitor regression71010 are LIVE; the
canonical checkpoint also retains live419 broad union2199. Final test source hashes:
- ROM composition:EBBE7D5ADD2580495B5D939900FAF4D8E82443A6563535A2DEFE701840091FAE
- ROM selector:34E46C9102EFCEC8200B217F60EB33A6B3D51A9CD57B85EE49B9CAAB1EEF8DD6
- Profile tests:5F50DC58252E68B9DFB1F23A0C60F1C39AF999987F6F960C69159B47928D8632

## Historical checkpoint — 2026-09-15 / 420

Native default selector now admits NULL/zero and returns NULL for NULL/INVALID_ID
without reading GfxBase. Name precedence is retained: named INVALID_ID still
declines untouched to the provider path. All reference-count, base/envelope,
callee-save and saturation guards remain. Native names and other explicit IDs
are still unimplemented in this arm; full selection is NOT complete.

Native admission fixtures now usezero, include INVALID_ID native-null provenance,
and check invalid-ID calls with null/odd/wrapping GfxBase values without mutation.
Native ROM AUTOINIT/lifecycle fixtures now open default usingzero; the AUTOINIT
success cases also verify INVALID_ID returnszero without changing the count.
41893 behavior cases passed; three stale fingerprint expectations failed only
on the measured new totals.419 updates them and passes100 focused checks (16s).
PAL1443496/NTSC1443396 bytes,71685 branches,3428 relays,7 chains,3346 boundaries;
all branch targets/ranges checked before the fingerprint comparison.
419 strict ROM regression COMPLETE40 passed,0 failed/skipped,6m20s;40917 CLOSED.
This includes the corrected native setup opens, AUTOINIT invalid-ID refusal and
the independently pinned original-ROM selector matrix.420 monitor regression
COMPLETE1901 passed,0 failed/skipped,2m43s;85737 CLOSED. Build420 PASS45.38s.
Only full419 union2199 remains LIVE, with no failure output so far.

420 fixes direct GraphicsServices.OpenMonitor refusal-to-NULL conversion. The
register adapter's bool-decline contract remains unchanged for tail chaining.
Four direct unknown-name/invalid-ID cases and adapter input-preservation checks
are added. Follow canonical CURRENT for build/test handles and evidence paths.

SHA256:
- Native raster emitter:D4FB0C9C310E5E3E61B5CA9FC5A2623024214018FF092DBB0327A3C76D37E463
- Native OpenMonitor tests:5A471B1F7A873E371FB505C8FB782910BD4E4B06B80A7DAC5BA18AB1EADB0393
- Profile tests:523585E6DFE3591244CABE70310EC8C5FBA659BB3C5C22CFB9BE0A58D314F4BD
- ROM composition tests:0056E42270759A54845AB4093C8EC23E12E9648E9E924DBE4974D6084DB22F75
- Direct GraphicsServices:54CA176829AFD997617BC3261F17A2F2AEE3E59D3E82B13EF00A5F671618ADC0
- Scaffold tests:DB2489EF4F660948722CE829CC0D1AFDED289D5E3ADB324BE03CF925C4EEF719
- ROM MonitorSpec tests:BD4610767C6EC15A3BF372026EE3BDAB0745A0F9B4DC9BEAE5BFF700D5B73AA0

Next collect current tests, then implement native resident name/explicit-ID
selection using the proven matrix and actual registration/availability. Maintain
name precedence, case-sensitive canonical names, case-insensitive default alias,
zero reference leaks, fallback preservation and no implicit foreign ownership.
Distinguish a full ROM API NULL result from an overlay decline, especially when
the default native fallback is only RTS and would retain an unsupported input ID.

## Historical checkpoint — 2026-09-15 / 417

416 pinned ROM selector suite COMPLETE2 passed,0 failed/skipped,16s;61214 CLOSED.
The first opposite-family ID request (PAL11000/NTSC21000) accounts for the entire
5536-byte decrease; a repeated full matrix has zero additional allocation.
This proves one-time allocation in this test, not the internal cache mechanism.
Both profiles execute64 requests (two passes of32) with explicit result checks,
balanced counts, unchanged lists and original OpenMonitor/CloseMonitor provenance.

417 changes host TryResolve: NULL/INVALID_ID declines, NULL/zero selects default;
PAL/NTSC canonical names use ordinal comparison; default.monitor keeps its
case-insensitive alias. Updated stale scaffold expectations and host-side ROM
semaphore fixture to usezero. Added PAL/NTSC host named-selector matrix with
unknown names, case mismatches, name precedence and count preservation.
Native raster bodies are unchanged and their wrong INVALID_ID alias remains the
next required correction; do not mark native selector compatibility complete.

Build417 PASS20.34s. Strict selector/semaphore integration COMPLETE4 passed,
0 failed/skipped,30s;72916 CLOSED. Monitor regression COMPLETE1889 passed,
0 failed/skipped,2m19s;27321 CLOSED. Broad41454638 remains LIVE with no failure
output so far. Paths in canonical CURRENT.

SHA256:
- ROM selector tests:FF1D4851CCAB63A789BAF2D88F3BB87AC6623F7FE219DBF61D72BFE900E759C1
- Host monitor operations:955A5A1453CDFC2EE6AB6141CC8B3715D6C76F9B8042164B11AE12773E9D0A6B
- Scaffold tests:9B6C6FDD4D9F5974B7B3040B81A7AE7DC6AE634EF3A95428F7EBABBDCF568BB1
- ROM MonitorSpec tests:6F64DE1F9E27934D0AA26FF69F05D5A4EEB7DC21AC84C8531BEE8AAF419E3621

Next: collect broad54638 without restart. Correct AppendOpenMonitorDefault, replacing the
wrong alias with null-name/zero selection and proper NULL result for INVALID_ID;
preserve malformed-envelope and callee-save/fallback invariants. Update only
OpenMonitor-specific stale tests and native integration setup opens (do not
globally replace INVALID_ID, which is valid for other display-database APIs).
Then expand named and explicit-ID lookup using the proven selector matrix and
native resident availability. Test both fixed/relocated emitted code and real
Exec AUTOINIT residents; update raster fingerprint tests only from measured
output and retain full admission/branch-range qualification.

Prior goal turn classification: progress (native AUTOINIT code plus499 focused
and38 original-ROM passes). This turn continues with regression and selector
evidence, not a blocker/status-only turn. Full scope remains unchanged.
