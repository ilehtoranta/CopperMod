# Monitor-driver record mutation and synchronization

## CURRENT checkpoint — 2026-09-21 / 580 (OR/XOR strict-gap and bridge)

The graphics Region stream now inserts validated one-node sources into strict
vertical gaps and collapses exact bridges for both OR and XOR. Gap publication
is transactional; bridge publication widens the first node and Exec retires
only the old second node. Malformed, allocation-failure, and missing-Exec forms
preserve the prior publication. Focused Region/audit is 70/0/0 and full
portable scaffold is 1,940/1,940; the fingerprint is 1,494,582 bytes / 75,499
branches / 3,428 relays / 7 chains / 3,346 boundaries. Monitor-driver record
mutation and provider ownership are unchanged; full replacement parity and
CyberGraphX remain open.

## Checkpoint 577 — strict vertical-gap one-node insertion into two-node OR destination

The vertical second-node coalescing arm remains independent of monitor-driver
record mutation and ownership; the AccurateM68000 fixture covers in-place
widening, strict ordering, first-node/link preservation, source/header
stability, and bounded fallback.

## CURRENT checkpoint — 2026-09-20 / 566 (reverse-crossing XOR split)

The graphics Region stream now handles both equal-span crossing XOR
orientations, including source `[0..12]` against destination `[8..20]`, which
publishes `[0..7]` and `[13..20]` transactionally. Focused Region/audit is
61/0/0 and full portable scaffold is 1,930/1,930; the fingerprint is
1,484,208 bytes / 74,696 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor-driver record mutation and provider ownership are
unchanged; full replacement parity and CyberGraphX remain open.

## Checkpoint 566 — equal-span reverse-crossing XOR split

The mirror crossing arm reuses the Region replacement transaction and remains
independent of monitor-driver record mutation and ownership.

## CURRENT checkpoint — 2026-09-20 / 565 (destination-contained XOR split)

The graphics Region stream now handles both equal-span XOR containment
orientations. Source `[8..12]` inside destination `[0..20]`, and the mirrored
destination containment, publish bands `[0..7]` and `[13..20]` transactionally.
Focused Region/audit is 60/0/0 and full portable scaffold is 1,929/1,929; the
fingerprint is 1,483,180 bytes / 74,638 branches / 3,428 relays / 7 chains /
3,346 boundaries. Monitor-driver record mutation and provider ownership are
unchanged; full replacement parity and CyberGraphX remain open.

## Checkpoint 565 — equal-span destination-contained XOR split

The mirror containment arm reuses the existing Region replacement transaction
and remains independent of monitor-driver record mutation and ownership.

## CURRENT checkpoint — 2026-09-20 / 564 (contained-source XOR split)

The graphics Region stream now handles equal-span XOR with source `[8..12]`
contained inside destination `[0..20]`, publishing replacement bands `[0..7]`
and `[13..20]` transactionally. Focused Region/audit is 59/0/0 and full
portable scaffold is 1,928/1,928; the fingerprint is 1,482,158 bytes /
74,581 branches / 3,428 relays / 7 chains / 3,346 boundaries. Monitor-driver
record mutation and provider ownership are unchanged; full replacement parity
and CyberGraphX remain open.

## Checkpoint 564 — equal-span contained-source XOR split

This topology-changing unit uses the existing Region publication transaction
and remains independent of monitor-driver record mutation and ownership.

## CURRENT checkpoint — 2026-09-20 / 563 (right-edge XOR remainder)

The graphics Region stream now handles complementary equal-span edge overlaps
in place: source `[0..8]` leaves destination `[9..20]`, and source `[12..20]`
leaves `[0..11]`. Both preserve source storage and avoid Exec. Focused
Region/audit is 58/0/0 and full portable scaffold is 1,927/1,927; the
fingerprint is 1,481,144 bytes / 74,524 branches / 3,428 relays / 7 chains /
3,346 boundaries. Monitor-driver record mutation and provider ownership are
unchanged; full replacement parity and CyberGraphX remain open.

## Checkpoint 563 — equal-span right-edge XOR remainder

This complementary unit remains independent of monitor-driver record mutation
and publication ownership; its AccurateM68000 fixture verifies the destination
left-band rebase and existing fallback contract.

## CURRENT checkpoint — 2026-09-20 / 562 (left-edge XOR remainder)

The graphics Region stream now handles the bounded equal-span left-edge
`XorRegionRegion` overlap in place: source `[0..8]` against destination
`[0..20]` publishes destination `[9..20]`, preserving the source and avoiding
Exec. Focused Region/audit is 57/0/0 and full portable scaffold is 1,926/1,926;
the fingerprint is 1,480,680 bytes / 74,485 branches / 3,428 relays / 7
chains / 3,346 boundaries. Monitor-driver record mutation and provider
ownership are unchanged; full replacement parity and CyberGraphX remain open.

## Checkpoint 562 — equal-span left-edge XOR remainder

This unit is independent of monitor-driver record mutation and publication
ownership. Its AccurateM68000 fixture verifies an in-place destination rebase
with the existing fallback contract for all unsupported forms.

## CURRENT checkpoint — 2026-09-20 / 561 (OR allocation-success repair)

The empty-region and three-node L-shaped `OrRectRegion` continuations now take
their success labels when `AllocMem` returns a nonzero address. The focused
Region/audit gate is 56/0/0 and the full portable scaffold is 1,925/1,925;
the equal-span crossing XOR unit remains unchanged. Fingerprint: 1,480,176
bytes / 74,445 branches / 3,428 relays / 7 chains / 3,346 boundaries.
Monitor-driver record mutation and provider ownership are unchanged; full
replacement parity and CyberGraphX remain open.

## Checkpoint 561 — OR allocation-success repair

Corrected the inverted post-`AllocMem` success branches in the two native
`OrRectRegion` continuations. All previously reproduced OR regressions now
pass without changing monitor-driver record mutation or ownership routing.

## CURRENT checkpoint — 2026-09-20 / 560 (equal-span crossing XOR)

The graphics Region continuation now adds an equal-span crossing one-node
`XorRegionRegion` arm, while retaining the shared canonical transfers and
empty-source clears. Two replacement nodes are staged and published
atomically; allocation, malformed-link, and missing-Exec declines preserve
the caller frame and existing destination. The dedicated AccurateM68000
fixture and signed-word audit pass. Focused Region/audit is 52 passing / 4
failing and the full portable scaffold is 1,921/1,925; the four failures are
existing OrRect/OR-disjoint regressions reproduced with the new XOR entry
disabled. Fingerprint: 1,480,176 bytes / 74,445 branches / 3,428 relays / 7
chains / 3,346 boundaries. The monitor-driver record remains a routing
companion; CyberGraphX and full replacement parity remain open.

## Checkpoint 560 — equal-span crossing XOR

Source `[8..20]` and destination `[0..12]` publish two relative symmetric
difference bands `[0..7]` and `[13..20]`; the source remains untouched and the
old destination is retired only after publication. Monitor-driver record
mutation and provider ownership are unchanged.

## Checkpoint 559 — canonical four-node OR/XOR transfer

The OR/XOR entry stages four public nodes privately and publishes the source
envelope and linked head only after all writes complete. First/fourth
allocation rollback, malformed terminal-link, and missing-Exec paths leave
both Regions available to the existing operation stream; monitor-driver
record mutation and provider ownership are unchanged.

## Checkpoint 558 — canonical three-node OR/XOR transfer

The OR/XOR entry stages three public nodes privately and publishes the source
envelope and linked head only after all writes complete. This unit does not
alter monitor-driver record mutation or provider ownership; rollback and
fallback paths leave both Regions available to the existing operation stream.

## Checkpoint 557 — canonical three-node AND identity

The AND entry reuses the complete three-node list, envelope, relative-bounds,
and ordering proof, then returns success without allocation, Exec calls, or
public mutation. This unit does not alter monitor-driver record mutation or
provider ownership; malformed and non-identical forms retain the existing
fallback.

## Checkpoint 556 — canonical three-node OR identity

The OR entry reuses the complete three-node list, envelope, relative-bounds,
and ordering proof, then returns success without allocation, Exec calls, or
public mutation. This unit does not alter monitor-driver record mutation or
provider ownership; malformed and non-identical forms retain the existing
fallback.

## Checkpoint 555 — canonical three-node XOR identity

The independent Region stream validates equal canonical three-node source and
destination lists before freeing the destination tail-to-head and publishing
an empty header. Stack restoration is split before and after source-pointer
admission, preserving D0 for malformed or escaping fallback. This unit does
not alter monitor-driver record mutation or provider ownership.

## Checkpoint 554 — empty-source AND over four-node destination

The independent Region stream validates a canonical four-node destination
before resolving Exec, frees all four public nodes tail-to-head, and publishes
the empty header. Malformed fourth-node links preserve the caller's D0 and
retain the existing fallback. This unit does not alter monitor-driver record
mutation or provider ownership.

## CURRENT checkpoint — 2026-09-20 / 552 (empty-source OR/XOR over four-node destination)

## CURRENT checkpoint — 2026-09-20 / 551 (empty-source OR/XOR over three-node destination)

The separate graphics Region stream now admits the empty-source OR/XOR
identity against canonical two- and three-node destinations. Complete
ownership, links, public envelope, relative bounds, and strict vertical
ordering precede allocation-free success; the destination chain remains
unchanged, while malformed or escaping forms retain the existing fallback.
The new AccurateM68000 fixture, focused Region family, and signed-word audit
pass 28/0/0. Fingerprint: 1,468,356 bytes / 73,607 branches / 3,428 relays /
7 chains / 3,346 boundaries. This does not alter monitor-driver mutation or
CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 538 (OR source containing both destination nodes)

The separate graphics Region stream now admits a bounded OR form where a
one-node source strictly contains both nodes of a two-node destination under
a matching public envelope. One replacement node is initialized and
published before both old nodes are retired; allocation/address/Exec failures
leave the original chain intact. The new AccurateM68000 fixture, focused
Region family, and signed-word audit pass 15/0/0. Fingerprint: 1,457,478 bytes
/ 72,783 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 537 (two-node source into empty OR/XOR destination)

The separate graphics Region stream now transfers a canonical two-node source
into an empty destination for both `OrRegionRegion` and `XorRegionRegion`.
Complete ownership, links, envelope, and relative-bounds admission precedes
two private node allocations; the copied source envelope and chain publish
atomically. Allocation/address/Exec failures free provisional nodes and leave
both Regions unchanged. The new AccurateM68000 fixture, focused Region family,
and signed-word audit pass 14/0/0. Fingerprint: 1,457,152 bytes / 72,771
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter
monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 536 (bounded coalesced OR orientations)

The separate graphics Region stream now admits both bounded coalesced
`OrRegionRegion` orientations: equal-Y-span horizontal overlap and equal-X-
span vertical overlap between a one-node source and the first node of a
two-node destination. The first node widens in place only after complete
admission; no allocation or retirement occurs, and source/second-node
ownership remains intact. The AccurateM68000 fixture, focused Region family,
and signed-word audit pass 13/0/0. Fingerprint: 1,456,300 bytes / 72,718
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter
monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 535 (bounded coalesced OR replacement)

The separate graphics Region stream now admits a coalesced `OrRegionRegion`
form where a one-node source overlaps the first node of a two-node destination
with an identical Y span and the second node remains below. Complete
admission precedes the in-place widening of the first destination node; no
allocation or retirement occurs, and source/second-node ownership is retained.
The new AccurateM68000 fixture, focused Region family, and signed-word audit
pass 13/0/0. Fingerprint: 1,456,158 bytes / 72,709 branches / 3,428 relays /
7 chains / 3,346 boundaries. This does not alter monitor-driver mutation or
CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 534 (bounded AND two-node containment identity)

The separate graphics Region stream now admits a read-only `AndRegionRegion`
identity where a canonical one-node source contains both nodes of a canonical
two-node destination under matching public envelopes. Complete ownership,
links, and relative containment are proven before returning success; no
allocation, retirement, or public mutation occurs. Malformed and escaping
forms delegate without changing the caller's D0. The new identity fixture,
focused Region family, and signed-word audit pass 12/0/0. Fingerprint:
1,455,434 bytes / 72,647 branches / 3,428 relays / 7 chains / 3,346
boundaries. This does not alter monitor-driver mutation or CyberGraphX
ownership.

## CURRENT checkpoint — 2026-09-19 / 533 (bounded overlapping OR replacement)

The separate graphics Region stream now admits a bounded overlapping
`OrRegionRegion` form: a one-node source strictly contains the first node of a
two-node destination, while the second destination node remains below it.
One fresh public node is fully initialized and linked before the replacement
head is published; only then is the contained old node retired. Allocation,
address-class, and Exec failures roll back without public mutation. The new
success/rollback fixture, focused Region family, and signed-word audit pass
11/0/0. Fingerprint: 1,454,726 bytes / 72,584 branches / 3,428 relays / 7
chains / 3,346 boundaries. XOR and non-rectangular overlap remain outside
this native arm; monitor-driver mutation and CyberGraphX ownership are
unchanged.

## CURRENT checkpoint — 2026-09-19 / 532 (bounded OR/XOR one/two-node replacement)

The separate graphics Region stream now admits a bounded disjoint one-source-
node/two-destination-node OR/XOR replacement form. One fresh public node
copies the source-relative rectangle and is prepended to the existing
destination pair only after complete admission; allocation, address-class,
and Exec failures roll back without publishing partial state. The new fixture,
focused Region family, and signed-word audit pass 10/0/0. Fingerprint:
1,453,830 bytes / 72,517 branches / 3,428 relays / 7 chains / 3,346
boundaries. This does not alter monitor-driver mutation or CyberGraphX
ownership.

## CURRENT checkpoint — 2026-09-19 / 531 (bounded OR/XOR two-node replacement)

The separate graphics Region stream now admits a bounded disjoint two-node
OR/XOR replacement form. It allocates and validates two fresh public nodes,
copies the source-relative rectangles, prepends them to the destination chain,
and rolls back all provisional storage before publication on failure. The
replacement-list fixture, focused Region family, and signed-word audit pass
6/0/0. Fingerprint: 1,452,174 bytes / 72,389 branches / 3,428 relays / 7
chains / 3,346 boundaries. This does not alter monitor-driver mutation or
CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 530 (independent AND contained two-node destination)

The separate graphics Region stream now admits a canonical two-node
`AndRegionRegion` destination whose public envelope matches the source and
whose corresponding relative rectangles are contained by the source nodes.
The destination is already the exact intersection, so the path returns
success without allocation, retirement, or writes. Escaping, malformed, and
non-identical forms preserve D0 through fallback. The focused Region family
and signed-word audit pass 6/0/0. Fingerprint remains 1,451,612 bytes /
72,367 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 529 (independent XOR identical two-node identity)

The separate graphics Region stream now admits exact identity for canonical
two-node `XorRegionRegion` operands. After shared ownership/link/envelope
admission, the native path resolves Exec, retires both destination nodes once,
and publishes an empty header without mutating the source. Malformed/non-
identical pairs preserve D0 through fallback. The focused Region family and
signed-word audit pass 6/0/0. Fingerprint: 1,451,612 bytes / 72,367 branches /
3,428 relays / 7 chains / 3,346 boundaries. This does not alter
monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 528 (independent OR identical two-node identity)

The separate graphics Region stream now admits exact identity for canonical
two-node `OrRegionRegion` operands. The OR dispatch restores its outer D0
frame before shared two-node ownership/link/envelope admission; matching
chains return success without allocation, retirement, or public writes.
Malformed/non-identical pairs preserve D0 through fallback. The focused Region
family and signed-word audit pass 6/0/0. Fingerprint: 1,450,694 bytes /
72,289 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-19 / 527 (independent Region identical two-node identity)

The separate graphics Region stream now admits exact identity for two
canonical two-node `AndRegionRegion` operands. Complete ownership, link,
absolute-envelope, and relative-node admission precedes the read-only
success; no allocation, retirement, or public write occurs. Empty/one-node
destinations reuse the existing two-node-source arm, while malformed or
non-identical pairs preserve D0 through fallback. The identity/mismatch
fixture and focused Region/signed-word set pass 5/0/0. Fingerprint:
1,449,860 bytes / 72,212 branches / 3,428 relays / 7 chains / 3,346
boundaries. This does not alter monitor-driver mutation or CyberGraphX
ownership.

## CURRENT checkpoint — 2026-09-19 / 526 (independent Region partial reduction)

The separate graphics Region stream now handles a canonical one-node source
whose inclusive intersection is confined to exactly one node of a canonical
two-node destination. Complete link/envelope admission and source-vs-other
disjointness precede Exec; the selected node/header is rewritten to the exact
intersection in place and only the unselected node is retired. Both node
orders, spanning-source decline, and malformed fallback pass 8/0/0. This does
not alter monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-18 / 524 (independent Region cleanup)

The separate graphics Region stream now handles a canonical two-node
`AndRegionRegion` destination when the source is empty. Complete link and
envelope admission precedes Exec; both public nodes are retired exactly once
and the destination header is cleared in place. Malformed links preserve D0
through fallback. Focused AccurateM68000 coverage and the generated branch
audit pass 7/0/0. This does not alter monitor-driver mutation or CyberGraphX
ownership.

## CURRENT checkpoint — 2026-09-18 / 523 (independent Region partial intersection)

The separate Region clipping stream now handles inclusive partial
`AndRegionRegion` intersections against either node of a canonical two-node
source. The owned destination allocation remains in place; malformed,
gap-crossing, and both-node results preserve D0 through fallback. Focused
AccurateM68000 coverage and the generated branch audit pass 6/0/0. This does
not alter monitor-driver mutation or CyberGraphX ownership.

## CURRENT checkpoint — 2026-09-18 / 519 (native Region alias verification)

The native Region alias regression and generated signed-word branch audit pass
with the broader Region slice at 5/0/0. OR remains read-only for an aliased
destination; XOR performs one retirement. Monitor-driver mutation and provider
ownership remain unchanged.

## CURRENT checkpoint — 2026-09-18 / 518 (native Region alias identity)

The native Region boolean continuation now admits an aliased
`OrRegionRegion` call as an already-complete union before any Rectangle
interpretation or replacement allocation. Focused AccurateM68000 coverage and
the generated signed-word branch audit pass 2/0/0; monitor-driver mutation
and provider ownership remain unchanged.

## CURRENT checkpoint — 2026-09-18 / 517 (Intuition resource ledger)

The optional Intuition Screen graphics-resource handoff now serializes its
committed ledger, preventing duplicate prepare or double retirement during
concurrent lifecycle requests. Focused verification remains green; the full
replacement is active/incomplete and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-18 / 516 (verification boundary)

The compatibility `TryLoadView` transaction remains green in the direct
AmigaBoot display lifecycle tests (153/0/0). Seven failures in the broader
memory suite are unrelated memory-list/DisplayInfo expectations and remain
outside this bounded change. The full replacement is active/incomplete and
CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-18 / 515 (compatibility LoadView gate)

The compatibility `TryLoadView` path now uses the same monitor/provider
rebind gate as native GfxBase publication. Screen-to-front and close-screen
blanking retain one View/copper/ActiView lifetime transaction. Focused display
checks remain green; the full replacement is active/incomplete and CyberGraphX
remains excluded.

## CURRENT checkpoint — 2026-09-18 / 514 (Intuition rethink transaction)

Intuition host display reconstruction now runs under the monitor/provider
rebind gate, keeping View and native GfxBase sidecar publication atomic with
the provider lifetime. The dedicated blocked handoff regression passes 1/0/0;
the full replacement remains active/incomplete and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-18 / 513 (Intuition rethink gate)

The host-side Intuition `RethinkDisplay` transaction now uses the same
provider/rebind gate as monitor and native GfxBase publication. This keeps
screen/view reconstruction from observing a split native monitor lifetime;
focused verification remains green. The full replacement is active/incomplete
and CyberGraphX remains excluded.

Bounded work under GRAPHICS_LIBRARY_MONITOR_ID_REGISTRATION_GOAL.md unit6.
Full Kickstart 3.1 replacement remains incomplete; CyberGraphX is excluded.
Canonical execution state is GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md
CURRENT; this file records the implementation evidence and remaining boundaries.

## CURRENT checkpoint — 2026-09-18 / 512 (screen/view provider gate)

The screen/view handoff now uses the same provider lifetime gate as monitor
publication: `LoadView` and timed `LoadView` serialize active-view sidecars,
while screen-prefix/resource registries serialize allocation and exact-owner
teardown. The blocked native-overlay `LoadView` regression and combined slice
pass 1/0/0 and 1472/0/11 respectively. This remains a bounded lifecycle unit;
full Intuition and screen/view integration are still incomplete.

## CURRENT checkpoint — 2026-09-18 / 511 (MonitorSpec admission gate)

The native-overlay MonitorSpec vectors now pair provider admission with their
guest transaction: `OpenMonitor`/`CloseMonitor` list and count changes, while
`GetDisplayInfoData` readback and lazy publication, cannot race graphics-base
rebind. Three deterministic overlap regressions pass and the focused slice is
1292/0/11. This remains a bounded provider/lifetime unit; full MonitorSpec,
Intuition, and screen/view lifecycle are still incomplete.

## CURRENT checkpoint — 2026-09-18 / 510 (atomic overlay free)

The native-overlay `GfxFree` path now holds the provider lifetime gate across
writable-span admission and the association/backlink teardown. This prevents a
concurrent graphics-base rebind from separating the preflight view from the
commit view. The blocked-provider regression passes 1/0/0 and the focused
surrounding slice passes 1289/0/11. This remains a bounded provider/lifetime
unit; full MonitorSpec, Intuition, and screen/view lifecycle are still
incomplete.

## CURRENT checkpoint — 2026-09-18 / 509 (extended-node provider gate)

The extended-node provider lifetime now shares the native CMDB/rebind
synchronization gate. A provider-backed `GfxNew` cannot race a graphics-base
rebind into publishing a stale `xln_Lib` backlink, and ownership/lookup/free
remain serialized with the registry move. The blocking-allocation regression
passes 1/0/0 and the focused surrounding slice passes 1288/0/11. This remains
a bounded provider/lifetime unit; full MonitorSpec, Intuition, and screen/view
lifecycle are still incomplete.

## CURRENT checkpoint — 2026-09-18 / 508 (rebind retry)

Native CMDB rebind now has end-to-end retry coverage. A declined APTR clear
retains the old binding and ownership; a later successful retry moves the
registry binding, frees the old sidecar exactly once, and routes subsequent
extended-node allocation to the new base. Focused regression 1/0/0. This
remains a bounded provider/lifetime unit; full MonitorSpec, Intuition, and
screen/view lifecycle are still incomplete.

## CURRENT checkpoint — 2026-09-18 / 507 (teardown retry state)

The native display-database release path now reports whether the guest APTR
and descriptor were safely retired. GraphicsServices preserves the native
binding markers across a declined clear or rebind and retries later, avoiding
premature free of a provider-visible CMDB sidecar. The focused service,
descriptor, and native-base lifecycle regressions are green. This remains a
bounded provider/lifetime unit; full MonitorSpec, Intuition, and screen/view
lifecycle are still incomplete.

## CURRENT checkpoint — 2026-09-18 / 506 (readback snapshot lifetime)

Read-only registered CMDB queries no longer populate the mutable MNTR sidecar.
This keeps partial field admission truly field-granular and lets subsequent
guest/provider edits to registration and position cells appear immediately.
Only explicit `SetDisplayInfoData` calls create or update the 88-byte mutation
record, preserving the private-driver contract. Focused monitor readback,
provider/lifecycle and owned-position tests are green; the full replacement
remains incomplete and the next bounded unit is MonitorSpec/provider lifecycle
integration.

## CURRENT checkpoint — 2026-09-18 / 504

Host and native private mutation are now implemented as separate, opt-in
acceptance units. The host `GraphicsPrivateLvo.SetDisplayInfoData` sidecar
stores the complete 88-byte MNTR family record and performs snapshot-before-
write validation. The native path is emitted by
`NativeGraphicsRasterBodies.BuildCodeWithMonitorMutation`, mapped privately at
-750 by the image/HUNK builders, and backed by an extended CMDB area with
separate PAL/NTSC 88-byte records. It copies only the admitted source prefix,
publishes the selected record pointer after the copy, and exposes the updated
bytes through native GetDisplayInfoData. Public LVOs, default native images,
and the unextended CMDB layout remain unchanged.

`GraphicsLibraryCore` now guards the monitor registration, mutable record
sidecars, and native CMDB ownership transition with one re-entrant host-side
gate. Publication, release/rebind, OpenMonitor/TryOpenMonitor/CloseMonitor,
readback, view-position updates and private SetDisplayInfoData therefore
serialize as one provider/lifetime domain; the
implementation intentionally does not model an Exec semaphore.

Current evidence is green: PAL/NTSC native mutation 2/0/0 and relocatable HUNK
private-vector mapping 1/0/0 on the configured
Kickstart 3.1 A500 ROM; native MNTR readback 196/0/0; native initialization
39/0/0; initializer/descriptor/lifecycle/state publisher 301/0/0; and
MonitorSpec publisher/composition 207/0/0. The native test covers full-record
transfer, 19/20/21-byte no-op/partial boundaries, source and allocation guards,
result count, and both family records.
The matching mutation-enabled native release extent check also passes 1/0/0;
the default release and registered-MonitorSpec release suites remain green.
The deterministic publication/release overlap test also passes 1/0/0.
The monitor Open/TryOpen/Close registration regression remains green at
1008/0/0 with the same gate in place.
Malformed/unsafe envelopes remain deliberately outside the ROM contract, and
full MonitorSpec/Intuition/screen/view lifecycle remain future units.

## Established evidence and current gap

The original-ROM registration oracle calls private SetDisplayInfoData at -750
with (handle,buffer,size,tag,id) in (A0,A1,D0,D1,D2). An88-byte MNTR source returns68;
GetDisplayInfoData returns88. Mspc changes are shared by all installed-family
aliases, remain independent of list/default/ownership, and can be raw or zero.
The original record is restored before freeing any borrowed test node.

The first mutation oracle is now measured on both PAL and NTSC. For a valid
MNTR source, SetDisplayInfoData returns 0 for source sizes 0..20, then returns
`min(size,88)-20` through size88, and remains68 for larger sizes. The source
buffer is not modified and allocation balance is unchanged. Bytes 0..15 alone
do not mutate the installed record; bytes beginning at offset16 are transferred
as a prefix, with the returned count excluding the 20-byte private prefix.
At full size the operation copies the complete88-byte family record, including
Mspc, ViewPosition, ViewPositionRange/metadata, DefaultViewPosition and
PreferredModeID. Focused size oracle480 passed2/0/0 in21s; field oracle486
passed2/0/0 in15s. Independent literal ROM records and exact field differences
are logged in the immutable TRX files under the corresponding graphics-monitor-
selection-20260917-480/486 results directories.

The strengthened exact size/field oracles were rebuilt from current source as
artifact488 and passed size2/0/0 in14s and field2/0/0 in14s. Their TRX files are
`monitor-mutation-size-oracle.trx` and `monitor-mutation-field-oracle.trx` under
`.codex-artifacts/graphics-mutation-488/results`.

Artifact492 adds the bounded admission oracle on the configured A500 ROM:
PAL/NTSC COMPLETE2/0/0 in14s. An unknown family returns zero without changing
the family record; a non-MNTR private call returns the DISP-sized result48 and
does not touch MNTR; a valid MNTR source whose tag field is deliberately
mutated still returns68, after which MNTR selection returns zero until a valid
record is restored. The oracle deliberately does not invoke the ROM with a
null or wrapping nonempty pointer: Kickstart does not return through the test
sentinel for those unsafe envelopes, so they are not a portable ownership
contract. The result is
`monitor-mutation-admission-oracle.trx` under
`.codex-artifacts/graphics-mutation-492/results`.

The field oracle deliberately excludes invalid header/tag mutation: changing
the family-record tag makes the subsequent selector unavailable until a valid
record is restored. That is a separate malformed-state admission case, not a
reason to silently accept arbitrary tags.

Do not extrapolate those observations into a generic Set transfer algorithm.
Partial sizes, writable fields, source-header handling and mutation return counts
are not yet established. Static GraphicsMonitorInfoImage boot metadata is not a
mutable driver record. CMDB3 currently stores default family, raw mappings and
current/original Points, not the complete writable family payload.

Production GraphicsLvo deliberately omits private slots -738/-744/-750. The
public enum/catalog and public ABI tests must not be silently redefined to expose
a private vector. Establish the complete private entry contract before routing
driver requests or assigning undocumented slots.

## Small implementation units

1. Extend the original-ROM oracle for supported, valid MNTR mutation inputs:
   source lengths around header/field boundaries, full records and excess size;
   NULL versus actual FindDisplayInfo handles; installed/default aliases; separate
   source header identity and argument identity. Allocate sufficient source and
   guard space. Never fabricate an arbitrary nonnull ROM handle, open an invalid
   raw mapping, or leave a modified record in place between observations.
2. Measure exact returned counts and full readback bytes after each operation.
   Mutate one field group at a time and restore the complete baseline immediately.
   Check source/guard stability, selected reference counts, allocation balance
   and original graphics/Exec vector provenance. Preserve measured opaque bytes.
3. Define the smallest explicitly versioned guest representation that can store
   the demonstrated mutable family record without conflicting authorities for
   Mspc and Points. Keep SDK offsets unchanged. Specify CMDB3 migration/rejection;
   never infer a new version from allocation length or host dictionaries.
4. Implement portable mutation with snapshot-before-write and rollback for failed
   partial writes, alias/handle precedence and ownership boundaries. Add a distinct
   private-entry integration path; do not claim host-only setters implement a
   native monitor driver. Provider-owned databases remain untouched.
   The first host slice is now present: private -750 remains outside
   `GraphicsLvo`, `GraphicsServices` overlays it separately, and a per-library
   88-byte sidecar preserves opaque fields while valid MNTR prefixes are copied
   without guest allocation. The emitted68k body and provider/lifetime
   synchronization remain separate acceptance units.
5. Implement emitted68k parity in a separate unit: fixed/HUNK entry paths,
   branch reach, register/stack ABI and executed-PC fallback provenance. Readonly
   GetDisplayInfoData must continue to avoid node validation or reference changes.
6. Establish list/database/node lifetime serialization for driver publication,
   replacement, removal and teardown, including yielded host/Exec callbacks.
   Remove provisional valid-name/list limits only under separately tested lifetime
   and malformed-cycle handling. Allocation ownership must never follow a borrowed
   raw mapping merely because it became selected or visible.

## Acceptance

Each unit records source/build identity, exact focused results and original-ROM
evidence before proceeding. Integration requires current graphics regressions
and native structural image audits. Invalid/unavailable input must not create
accidental allocation/adoption or partially published state. A clean mutation
slice does not complete the entire monitor subsystem or graphics.library goal.

## Checkpoint 521 — independent Region clipping progress

The separate graphics Region stream now covers a canonical two-node source
against an empty owned destination or a contained canonical one-node
destination in native `AndRegionRegion`, with exact link/envelope admission
and D0-preserving fallback. Focused AccurateM68000 coverage plus the generated
branch audit pass 6/0/0. This does not alter the monitor-driver mutation
contract, and it does not include CyberGraphX.

## Checkpoint 522 — independent Region clipping progress

The separate graphics Region stream now covers the two-node source reduction
where a canonical destination contains exactly one source node and is
disjoint from the other. The destination remains the owned allocation and is
rewritten in place; spanning or malformed forms retain D0 and fall back.
Focused AccurateM68000 coverage plus the generated branch audit pass 6/0/0.
This does not alter the monitor-driver mutation contract, and it does not
include CyberGraphX.

## Checkpoint 523 — independent Region clipping progress

The separate graphics Region stream now covers inclusive partial intersections
against either node of a canonical two-node source. The existing destination
allocation is retained and clipped in place; malformed, gap-crossing, and
both-node forms preserve D0 through fallback. Focused AccurateM68000 coverage
plus the generated branch audit pass 6/0/0. This does not alter the
monitor-driver mutation contract, and it does not include CyberGraphX.

## Checkpoint 524 — independent Region empty-source cleanup

The separate graphics Region stream now validates and retires a canonical
two-node destination for empty-source `AndRegionRegion`. Both public nodes
are freed exactly once after complete link/envelope admission, and the
destination header is cleared in place; malformed links preserve D0 through
fallback. Focused AccurateM68000 coverage plus the generated branch audit
pass 7/0/0. This does not alter the monitor-driver mutation contract, and it
does not include CyberGraphX.

## Checkpoint 525 — independent Region one-node source reduction

The separate graphics Region stream now validates and reduces a canonical
one-node source contained by exactly one node of a canonical two-node
destination. The selected node/header remains in place; only the other public
node is freed, and malformed or overlapping forms preserve D0 through
fallback. Focused AccurateM68000 coverage plus the generated branch audit
pass 8/0/0. This does not alter the monitor-driver mutation contract, and it
does not include CyberGraphX.

## Checkpoint 527 — independent Region identical canonical two-node identity

The separate graphics Region stream now admits exact identity for two
canonical two-node `AndRegionRegion` operands. Complete ownership, links,
absolute envelopes, and relative node bounds are validated before a
read-only success; empty/one-node destinations reuse the existing arm, while
malformed or non-identical pairs preserve D0 through fallback. The new
identity/mismatch fixture and focused Region/signed-word set pass 5/0/0.
Fingerprint: 1,449,860 bytes / 72,212 branches / 3,428 relays / 7 chains /
3,346 boundaries. This does not alter the monitor-driver mutation contract,
and it does not include CyberGraphX.

## Checkpoint 528 — independent OR identical canonical two-node identity

The separate graphics Region stream now admits exact identity for canonical
two-node `OrRegionRegion` operands. The OR dispatch restores its outer D0
frame before shared two-node ownership/link/envelope admission; matching
chains return success without allocation, retirement, or public writes.
Malformed/non-identical pairs preserve D0 through fallback. The focused Region
family and signed-word audit pass 6/0/0. Fingerprint: 1,450,694 bytes /
72,289 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter the monitor-driver mutation contract, and it does not include
CyberGraphX.

## Checkpoint 529 — independent XOR identical canonical two-node identity

The separate graphics Region stream now admits exact identity for canonical
two-node `XorRegionRegion` operands. Complete ownership, link, absolute-
envelope, and relative-node admission precedes Exec resolution; both
destination nodes are retired exactly once and an empty header is published,
while source storage remains unchanged. Malformed/non-identical pairs preserve
D0 through fallback. The focused Region family and signed-word audit pass
6/0/0. Fingerprint: 1,451,612 bytes / 72,367 branches / 3,428 relays /
7 chains / 3,346 boundaries. This does not alter the monitor-driver mutation
contract, and it does not include CyberGraphX.

## Checkpoint 530 — independent AND contained canonical two-node destination

The separate graphics Region stream now admits a canonical two-node
`AndRegionRegion` destination with the same public envelope and corresponding
relative rectangles contained by the source nodes. The existing destination
is already the exact result, so the read-only path returns success without
allocation, retirement, or publication. Escaping nodes, malformed links, and
non-identical envelopes preserve D0 through fallback. The focused Region
family and signed-word audit pass 6/0/0. Fingerprint remains 1,451,612 bytes /
72,367 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter the monitor-driver mutation contract, and it does not include
CyberGraphX.

## Checkpoint 531 — independent bounded OR/XOR two-node replacement

The separate graphics Region stream now admits a bounded disjoint two-node
OR/XOR replacement form. Two fresh public nodes copy the source-relative
rectangles and are linked ahead of the destination chain only after complete
admission; allocation, address-class, and Exec failures roll back without
publishing partial state. The replacement-list fixture and focused
Region/signed-word set pass 6/0/0. Fingerprint: 1,452,174 bytes / 72,389
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter
the monitor-driver mutation contract, and it does not include CyberGraphX.

## Checkpoint 551 — empty-source OR/XOR identity over three-node destination

The separate graphics Region stream extends the allocation-free empty-source
OR/XOR identity to a canonical three-node destination. The bounded native arm
validates the third node, terminal and predecessor links, all relative bounds,
and strict vertical ordering before success; malformed links preserve D0 and
retain the existing fallback. Focused Region/audit: 49/0/0. Fingerprint:
1,468,356 bytes / 73,607 branches / 3,428 relays / 7 chains / 3,346
boundaries. This does not alter monitor-driver mutation or CyberGraphX
ownership.

## Checkpoint 550 — empty-source OR/XOR identity over two-node destination

The separate graphics Region stream now admits an empty native source against
a canonical two-node destination for both OR and XOR. Complete ownership,
links, public envelope, relative bounds, and strict vertical ordering precede
allocation-free success; malformed or escaping forms restore D0 through the
existing fallback. The new AccurateM68000 fixture plus the focused
Region/signed-word set pass 27/0/0. Fingerprint: 1,467,986 bytes / 73,572
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter
monitor-driver mutation or CyberGraphX ownership.

## Checkpoint 549 — independent two-node source contains two-node destination

The separate graphics Region stream now admits the bounded OR identity where
corresponding rectangles in a canonical two-node source contain those in a
canonical two-node destination under a matching public envelope. Complete
ownership, links, relative bounds, and per-node containment precede an
allocation-free rewrite of the existing destination rectangles from the
source; list links, node addresses, and source storage remain intact, while
malformed or escaping forms retain the existing fallback. The new
AccurateM68000 fixture plus the focused Region/signed-word set pass 26/0/0.
Fingerprint: 1,467,728 bytes / 73,548 branches / 3,428 relays / 7 chains /
3,346 boundaries. This does not alter the monitor-driver mutation contract
and does not include CyberGraphX.

## Checkpoint 548 — independent two-node source below one-node destination

The separate graphics Region stream now admits the complementary bounded
disjoint two-node/one-node OR/XOR topology where both canonical source nodes
lie strictly below a one-node destination under a matching public envelope.
Complete ownership, links, relative bounds, and strict vertical gaps precede
two private source-node allocations; the new nodes append after the existing
destination node, while allocation or address failures roll back before
publication. The new AccurateM68000 fixture plus the focused Region/signed-
word set pass 25/0/0. Fingerprint: 1,466,832 bytes / 73,472 branches /
3,428 relays / 7 chains / 3,346 boundaries. This does not alter the
monitor-driver mutation contract and does not include CyberGraphX.

## Checkpoint 547 — independent two-node source above one-node destination

The separate graphics Region stream now admits the complementary bounded
disjoint two-node/one-node OR/XOR topology where both canonical source nodes
lie strictly above a one-node destination under a matching public envelope.
Complete ownership, links, relative bounds, and strict vertical gaps precede
two private source-node allocations; the new nodes prepend before the existing
destination node, while allocation or address failures roll back before
publication. The new AccurateM68000 fixture plus the focused Region/signed-
word set pass 24/0/0. Fingerprint: 1,465,740 bytes / 73,430 branches /
3,428 relays / 7 chains / 3,346 boundaries. This does not alter the
monitor-driver mutation contract and does not include CyberGraphX.

## Checkpoint 546 — independent one-node source below two-node destination

The separate graphics Region stream now admits the complementary bounded
disjoint one-node/two-node OR/XOR topology where the source node lies strictly
below both canonical destination nodes under a matching public envelope.
Complete ownership, links, relative bounds, and strict vertical gaps precede
one private source-node allocation; the new node appends after the existing
destination chain, while allocation or address failures roll back before
publication. The new AccurateM68000 fixture plus the focused Region/signed-
word set pass 23/0/0. Fingerprint: 1,464,636 bytes / 73,388 branches /
3,428 relays / 7 chains / 3,346 boundaries. This does not alter the
monitor-driver mutation contract and does not include CyberGraphX.

## Checkpoint 545 — independent destination chain precedes two-node source

The separate graphics Region stream now admits the complementary bounded
disjoint OR/XOR topology where both canonical destination nodes precede both
source nodes under a matching public envelope. Complete ownership, links,
relative bounds, and strict vertical gaps precede two private source-node
allocations; the new nodes append after the existing destination chain, while
allocation or address failures roll back before publication. The new
AccurateM68000 fixture plus the focused Region/signed-word set pass 22/0/0.
Fingerprint: 1,462,996 bytes / 73,260 branches / 3,428 relays / 7 chains /
3,346 boundaries. This does not alter the monitor-driver mutation contract
and does not include CyberGraphX.

## Checkpoint 544 — independent both-source-nodes contained by second destination

The separate graphics Region stream now admits the complementary bounded
read-only OR identity where both nodes of a canonical two-node source are
contained by the second node of a canonical two-node destination, with the
first destination node strictly above and disjoint. Complete ownership,
links, relative bounds, ordering/gap, and containment precede success without
allocation or mutation; unsupported forms retain the existing fallback. The
new AccurateM68000 fixture plus the focused Region/signed-word set pass
21/0/0. Fingerprint: 1,462,426 bytes / 73,239 branches / 3,428 relays / 7
chains / 3,346 boundaries. This does not alter the monitor-driver mutation
contract and does not include CyberGraphX.

## Checkpoint 543 — independent both-source-nodes contained by first destination

The separate graphics Region stream now admits the bounded read-only OR
identity where both nodes of a canonical two-node source are contained by the
first node of a canonical two-node destination, with the second destination
node strictly below and disjoint. Complete ownership, links, relative bounds,
ordering/gap, and containment precede success without allocation or mutation;
unsupported forms retain the existing fallback. The new AccurateM68000
fixture plus the focused Region/signed-word set pass 20/0/0. Fingerprint:
1,461,526 bytes / 73,158 branches / 3,428 relays / 7 chains / 3,346
boundaries. This does not alter the monitor-driver mutation contract and does
not include CyberGraphX.

## Checkpoint 542 — independent corresponding two-node OR containment identity

The separate graphics Region stream now admits the bounded read-only OR
identity where corresponding nodes of canonical two-node source and
destination Regions are contained under a matching public envelope. Complete
ownership, links, relative bounds, ordering/gap, and per-node containment
precede success without allocation or mutation; unsupported forms retain the
existing fallback. The new AccurateM68000 fixture plus the focused
Region/signed-word set pass 19/0/0. Fingerprint: 1,460,626 bytes / 73,077
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter
the monitor-driver mutation contract and does not include CyberGraphX.

## Checkpoint 541 — independent two-node OR source contained by one-node destination

The separate graphics Region stream now admits the bounded read-only OR
identity where a canonical two-node source is fully contained by a canonical
one-node destination under a matching public envelope. Complete ownership,
links, relative bounds, and per-node containment precede success without
allocation or mutation; unsupported forms retain the existing fallback. The
new AccurateM68000 fixture plus the focused Region/signed-word set pass
18/0/0. Fingerprint: 1,459,726 bytes / 72,996 branches / 3,428 relays / 7
chains / 3,346 boundaries. This does not alter the monitor-driver mutation
contract and does not include CyberGraphX.

## Checkpoint 540 — independent OR source contained by first destination node

The separate graphics Region stream now admits the symmetric bounded read-only
OR identity where a canonical one-node source is contained by the first node
of a canonical two-node destination and disjoint from the second. Complete
ownership, links, envelope, relative containment, and gap proof return success
without allocation or mutation. The new AccurateM68000 fixture plus the
focused Region/signed-word set pass 17/0/0. Fingerprint: 1,458,962 bytes /
72,925 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter the monitor-driver mutation contract, and it does not include
CyberGraphX.

## Checkpoint 539 — independent OR source contained by second destination node

The separate graphics Region stream now admits a bounded read-only OR identity
where a canonical one-node source is contained by the second node of a
canonical two-node destination and disjoint from the first. Complete
ownership, links, envelope, relative containment, and gap proof return success
without allocation or mutation. The new AccurateM68000 fixture plus the
focused Region/signed-word set pass 16/0/0. Fingerprint: 1,458,220 bytes /
72,854 branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not
alter the monitor-driver mutation contract, and it does not include
CyberGraphX.

## Checkpoint 538 — independent OR source containing both destination nodes

The separate graphics Region stream now admits a bounded OR union where a
canonical one-node source strictly contains both nodes of a canonical
two-node destination under a matching envelope. A private replacement node
is copied and published as the one-node result before both old nodes are
retired; allocation/address/Exec failures release provisional storage and
preserve the old chain. The new AccurateM68000 fixture plus the focused
Region/signed-word set pass 15/0/0. Fingerprint: 1,457,478 bytes / 72,783
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter
the monitor-driver mutation contract, and it does not include CyberGraphX.

## Checkpoint 537 — independent two-node source into empty OR/XOR destination

The separate graphics Region stream now admits a canonical two-node source
into an empty destination for both `OrRegionRegion` and `XorRegionRegion`.
Complete admission precedes two private public-node allocations, source
envelope/rectangle copies, and atomic destination publication; allocation,
address-class, and Exec failures release provisional nodes without mutating
either Region. The new AccurateM68000 fixture plus the focused Region and
signed-word set pass 14/0/0. Fingerprint: 1,457,152 bytes / 72,771 branches /
3,428 relays / 7 chains / 3,346 boundaries. This does not alter the
monitor-driver mutation contract, and it does not include CyberGraphX.

## Checkpoint 536 — independent bounded coalesced two-node OR orientations

The separate graphics Region stream now admits equal-Y-span horizontal and
equal-X-span vertical coalesced overlap between a one-node source and the
first node of a two-node destination. The first node is widened in place only
after complete admission; no allocation or retirement occurs. The coalesced
fixture plus the focused Region and signed-word set pass 13/0/0. Fingerprint:
1,456,300 bytes / 72,718 branches / 3,428 relays / 7 chains / 3,346
boundaries. This does not alter the monitor-driver mutation contract, and it
does not include CyberGraphX.

## Checkpoint 535 — independent bounded coalesced two-node OR replacement

The separate graphics Region stream now admits a canonical one-node source
overlapping the first node of a canonical two-node destination with the same Y
span and a second node below. The first destination node is widened in place
only after complete admission; no allocation or retirement occurs, and source
and second-node ownership remain unchanged. The new fixture plus the focused
Region and signed-word set pass 13/0/0. Fingerprint: 1,456,158 bytes / 72,709
branches / 3,428 relays / 7 chains / 3,346 boundaries. This does not alter the
monitor-driver mutation contract, and it does not include CyberGraphX.

## Checkpoint 534 — independent bounded source-containing-two-node AND identity

The separate graphics Region stream now admits a read-only identity where a
canonical one-node source contains both nodes of a canonical two-node
destination under matching public envelopes. Ownership, links, and relative
containment are complete before success; no allocation, retirement, or public
mutation occurs, while malformed and escaping forms delegate unchanged. The
new identity fixture plus the focused Region and signed-word set pass 12/0/0.
Fingerprint: 1,455,434 bytes / 72,647 branches / 3,428 relays / 7 chains /
3,346 boundaries. This does not alter the monitor-driver mutation contract,
and it does not include CyberGraphX.

## Checkpoint 533 — independent bounded contained-overlap OR replacement

The separate graphics Region stream now admits a canonical one-node source
that strictly contains the first node of a canonical two-node destination,
with the second node below the source under a matching envelope. One fresh
public node is initialized and linked before the replacement head is
published; the contained old node is retired only afterward. Allocation,
address-class, and Exec failures roll back without public mutation. The new
success/rollback fixture plus the focused Region and signed-word set pass
11/0/0. Fingerprint: 1,454,726 bytes / 72,584 branches / 3,428 relays / 7
chains / 3,346 boundaries. This does not alter the monitor-driver mutation
contract, and it does not include CyberGraphX.

## Checkpoint 532 — independent bounded OR/XOR one-source/two-destination replacement

The separate graphics Region stream now admits one canonical source node and
two canonical destination nodes with matching public envelopes and strict
vertical gaps. One fresh public node copies the source-relative rectangle and
is linked ahead of the existing destination pair only after complete
admission; source and old destination ownership remain intact, and
allocation, address-class, and Exec failures roll back privately. The new
AccurateM68000 fixture plus the focused Region/signed-word set pass 10/0/0.
Fingerprint: 1,453,830 bytes / 72,517 branches / 3,428 relays / 7 chains /
3,346 boundaries. This does not alter the monitor-driver mutation contract,
and it does not include CyberGraphX.
