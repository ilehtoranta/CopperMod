# Native monitor database ownership unit

## CURRENT checkpoint — 2026-09-21 / 580 (OR/XOR strict-gap and bridge)

The independent clipping stream now inserts validated one-node sources into
strict vertical gaps and collapses exact bridges for OR and XOR. Gap links are
published transactionally; exact bridges widen the first node and retire only
the old second node after publication. Source storage and headers remain
unchanged on every decline. Focused Region/audit is 70/0/0 and full portable
scaffold is 1,940/1,940. Generated image: 1,494,582 bytes / 75,499 branches /
3,428 relays / 7 chains / 3,346 boundaries. Monitor/provider ownership is
unchanged, CyberGraphX remains excluded, and full parity remains open.

## Checkpoint 577 — strict vertical-gap one-node insertion into two-node OR destination

The vertical second-node coalescing arm is wired into the public chain without
changing monitor database ownership; the AccurateM68000 fixture verifies
in-place widening, strict ordering, first-node/link preservation, source/header
stability, and bounded empty/malformed fallback behavior.

## CURRENT checkpoint — 2026-09-20 / 566 (reverse-crossing XOR split)

The independent clipping stream now publishes both equal-span crossing XOR
forms. The mirror source `[0..12]` / destination `[8..20]` receives bands
`[0..7]` and `[13..20]`; nodes are staged and linked before publication, and
allocation failure retains the old destination. Focused Region/audit is 61/0/0
and full portable scaffold is 1,930/1,930. Generated image: 1,484,208 bytes /
74,696 branches / 3,428 relays / 7 chains / 3,346 boundaries.
Monitor/provider ownership is unchanged, CyberGraphX remains excluded, and full
parity remains open.

## Checkpoint 566 — equal-span reverse-crossing XOR split

The AccurateM68000 mirror fixture confirms transactional publication, source
preservation, and first-allocation rollback; monitor database ownership remains
outside its scope.

## CURRENT checkpoint — 2026-09-20 / 565 (destination-contained XOR split)

The independent clipping stream now publishes both equal-span containment XOR
forms: either source or destination may be `[8..12]` inside `[0..20]`, and the
destination becomes bands `[0..7]` and `[13..20]`. Replacement nodes are staged
and linked before publication; allocation failure retains the old destination.
Focused Region/audit is 60/0/0 and full portable scaffold is 1,929/1,929.
Generated image: 1,483,180 bytes / 74,638 branches / 3,428 relays / 7 chains /
3,346 boundaries. Monitor/provider ownership is unchanged, CyberGraphX remains
excluded, and full parity remains open.

## Checkpoint 565 — equal-span destination-contained XOR split

The AccurateM68000 mirror fixture confirms transactional publication, source
preservation, and first-allocation rollback; monitor database ownership remains
outside its scope.

## CURRENT checkpoint — 2026-09-20 / 564 (contained-source XOR split)

The independent clipping stream now publishes the equal-span contained-source
XOR split: source `[8..12]` inside destination `[0..20]` becomes destination
bands `[0..7]` and `[13..20]`. Both replacement nodes are staged and linked
before publication, with allocation failure retaining the old destination.
Focused Region/audit is 59/0/0 and full portable scaffold is 1,928/1,928.
Generated image: 1,482,158 bytes / 74,581 branches / 3,428 relays / 7 chains /
3,346 boundaries. Monitor/provider ownership is unchanged, CyberGraphX remains
excluded, and full parity remains open.

## Checkpoint 564 — equal-span contained-source XOR split

The AccurateM68000 fixture confirms transactional publication, source
preservation, and first-allocation rollback; monitor database ownership remains
outside its scope.

## CURRENT checkpoint — 2026-09-20 / 563 (right-edge XOR remainder)

The independent clipping stream now publishes both equal-span one-band
`XorRegionRegion` remainders in place: source `[0..8]` leaves `[9..20]`, and
source `[12..20]` leaves `[0..11]`. The arms retain canonical links, avoid
Exec, and route malformed or unsupported shapes through the prior fallback.
Focused Region/audit is 58/0/0 and full portable scaffold is 1,927/1,927.
Generated image: 1,481,144 bytes / 74,524 branches / 3,428 relays / 7 chains /
3,346 boundaries. Monitor/provider ownership is unchanged, CyberGraphX remains
excluded, and full parity remains open.

## Checkpoint 563 — equal-span right-edge XOR remainder

The AccurateM68000 fixture confirms that the complementary native publication
unit only mutates the destination Region and preserves the source and fallback
contract; monitor database ownership remains outside its scope.

## CURRENT checkpoint — 2026-09-20 / 562 (left-edge XOR remainder)

The independent graphics clipping stream now publishes the bounded equal-span
left-edge `XorRegionRegion` remainder in place: source `[0..8]` and destination
`[0..20]` leave destination `[9..20]`. The arm performs no Exec resolution and
retains canonical links, while unsupported or malformed shapes use the prior
fallback. Focused Region/audit is 57/0/0 and full portable scaffold is
1,926/1,926. Generated image: 1,480,680 bytes / 74,485 branches / 3,428
relays / 7 chains / 3,346 boundaries. Monitor/provider ownership is unchanged,
CyberGraphX remains excluded, and full parity remains open.

## Checkpoint 562 — equal-span left-edge XOR remainder

The AccurateM68000 fixture confirms that this native publication unit only
mutates the destination Region and preserves the source and fallback contract;
monitor database ownership remains outside its scope.

## CURRENT checkpoint — 2026-09-20 / 561 (OR allocation-success repair)

The empty-region and three-node L-shaped `OrRectRegion` arms now branch to
their success continuations when `AllocMem` returns nonzero. The focused
Region/audit gate is 56/0/0 and the full portable scaffold is 1,925/1,925;
the equal-span crossing XOR publication remains unchanged. Generated image:
1,480,176 bytes / 74,445 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged, CyberGraphX remains
excluded, and full parity remains open.

## Checkpoint 561 — OR allocation-success repair

The two native `OrRectRegion` continuations had inverted post-allocation
conditions and returned FALSE on successful allocations. Their success labels
now receive nonzero results, preserving the existing private staging and
publication boundary. All Region/scaffold tests pass.

## CURRENT checkpoint — 2026-09-20 / 560 (equal-span crossing XOR)

The independent graphics clipping stream now adds an equal-span crossing
one-node `XorRegionRegion` arm, in addition to the shared canonical transfers
and prior empty-source clears. Complete ownership, links, equal envelopes,
full-height rectangles, and relative bounds precede two private allocations
and atomic publication. Allocation/address-class failures release every
provisional node; malformed or missing-Exec forms retain the existing
fallback. The dedicated AccurateM68000 fixture and signed-word audit pass;
focused Region/audit is 52 passing / 4 failing and full portable scaffold is
1,921/1,925. The four failures are existing OrRect/OR-disjoint regressions
reproduced with the new XOR entry disabled. Generated image: 1,480,176 bytes /
74,445 branches / 3,428 relays / 7 chains / 3,346 boundaries.
Monitor/provider ownership is unchanged and CyberGraphX remains excluded.

## Checkpoint 560 — equal-span crossing XOR

Source `[8..20]` and destination `[0..12]` publish two relative bands
`[0..7]` and `[13..20]`; the source remains untouched and the old destination
is retired only after publication. Monitor/provider ownership is unchanged.

## Checkpoint 559 — canonical four-node OR/XOR transfer

The OR/XOR Region entries stage four public nodes privately, initialize every
relative rectangle and link, then publish the source envelope and head. This
preserves the native publication boundary; monitor/provider ownership is
unchanged and malformed, allocation-failure, or missing-Exec operands
continue through the existing operation builders.

## Checkpoint 558 — canonical three-node OR/XOR transfer

The OR/XOR Region entries stage three public nodes privately, initialize every
relative rectangle and link, then publish the source envelope and head. This
preserves the native publication boundary; monitor/provider ownership is
unchanged and malformed or noncanonical operands continue through the existing
operation builders.

## Checkpoint 557 — canonical three-node AND identity

The AND Region entry reuses the complete three-node canonical admission and
returns TRUE without allocation, Exec calls, or public writes. This preserves
the native publication boundary while malformed or non-identical operands
continue through the existing AND builder. Monitor/provider ownership is
unchanged.

## Checkpoint 556 — canonical three-node OR identity

The OR Region entry reuses the complete three-node canonical admission and
returns TRUE without allocation, Exec calls, or public writes. This preserves
the native publication boundary while malformed or non-identical operands
continue through the existing OR builder. Monitor/provider ownership is
unchanged.

## Checkpoint 555 — canonical three-node XOR identity

The independent graphics clipping stream validates both canonical three-node
lists and retires all destination nodes tail-to-head only after equality is
proven. It publishes an empty destination and returns TRUE; malformed links or
address-class failures restore D0 and continue through the existing portable
XOR path. Monitor/provider ownership is unchanged.

## Checkpoint 554 — empty-source AND over four-node destination

The independent graphics clipping stream validates a canonical four-node
destination before freeing it tail-to-head and publishing the empty header.
Malformed fourth-node links preserve the caller's D0 and retain the portable
fallback, with no monitor/provider ownership change.

## CURRENT checkpoint — 2026-09-20 / 552 (empty-source OR/XOR over four-node destination)

## CURRENT checkpoint — 2026-09-20 / 551 (empty-source OR/XOR over three-node destination)

## CURRENT checkpoint — 2026-09-19 / 538 (OR source containing both destination nodes)

The independent graphics clipping stream now admits a bounded OR form where
a canonical one-node source strictly contains both nodes of a canonical
two-node destination under a matching public envelope. One private replacement
node copies the source rectangle, publishes the one-node chain, and retires
both old nodes only after publication. Allocation/address/Exec failures free
provisional storage and preserve the old chain. The new AccurateM68000
fixture, focused Region family, and signed-word audit pass 15/0/0; generated
image: 1,457,478 bytes / 72,783 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 537 (two-node source into empty OR/XOR destination)

The independent graphics clipping stream now admits a canonical two-node
source Region into an empty destination for both `OrRegionRegion` and
`XorRegionRegion`. Complete ownership, link, envelope, and relative-bounds
admission precedes two private public-node allocations; the source envelope
and copied chain publish atomically. Allocation/address/Exec failures free
provisional nodes and preserve both Regions. The new AccurateM68000 fixture,
focused Region family, and signed-word audit pass 14/0/0; generated image:
1,457,152 bytes / 72,771 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 536 (bounded coalesced OR orientations)

The independent graphics clipping stream now admits both bounded coalesced
`OrRegionRegion` orientations: equal-Y-span horizontal overlap and equal-X-
span vertical overlap between a canonical one-node source and the first node
of a canonical two-node destination. Complete ownership, link, envelope, and
relative-bounds admission precedes in-place widening; the source, second node,
and public header remain unchanged. The coalesced fixture, focused Region
family, and signed-word audit pass 13/0/0; generated image: 1,456,300 bytes /
72,718 branches / 3,428 relays / 7 chains / 3,346 boundaries. Monitor/provider
ownership is unchanged and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-19 / 535 (bounded coalesced OR replacement)

The independent graphics clipping stream now admits a bounded coalesced
`OrRegionRegion` transaction. A canonical one-node source overlaps the first
node of a canonical two-node destination with the same Y span, while the
second node remains below. Complete ownership, link, envelope, and relative-
bounds admission precedes the in-place widening of the first node; no
allocation or retirement occurs, and the source/second-node topology remains
unchanged. The new fixture, focused Region family, and signed-word audit pass
13/0/0; generated image: 1,456,158 bytes / 72,709 branches / 3,428 relays / 7
chains / 3,346 boundaries. Monitor/provider ownership is unchanged and
CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-19 / 534 (bounded AND two-node containment identity)

The independent graphics clipping stream now admits a bounded read-only
`AndRegionRegion` identity: a canonical one-node source contains both nodes of
a canonical two-node destination under matching public envelopes. After full
ownership, link, and relative-containment admission, native success returns
without allocation, retirement, or public writes; malformed, escaping, and
non-matching forms remain on the existing Region path. The new identity
fixture, focused Region family, and signed-word audit pass 12/0/0; generated
image: 1,455,434 bytes / 72,647 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 533 (bounded overlapping OR replacement)

The independent graphics clipping stream now admits a bounded overlapping
`OrRegionRegion` transaction. A canonical one-node source strictly contains
the first node of a canonical two-node destination, with the second node
below the source under a matching public envelope. One fresh public node is
copied and linked to the existing second node, the new head is published, and
the contained old node is retired only afterward. Allocation, address-class,
and Exec failures roll back without publishing partial state; XOR and
non-rectangular overlap remain fallback paths. The new success/rollback
fixture, focused Region family, and signed-word audit pass 11/0/0; generated
image: 1,454,726 bytes / 72,584 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 532 (bounded OR/XOR one/two-node replacement)

The independent graphics clipping stream now admits a bounded disjoint
one-source-node/two-destination-node OR/XOR replacement transaction. One
fresh public node copies the source-relative rectangle and is linked ahead of
the destination pair only after complete admission; allocation, address-
class, and Exec failures roll back without publishing partial state. The new
fixture, focused Region family, and signed-word audit pass 10/0/0; generated
image: 1,453,830 bytes / 72,517 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 531 (bounded OR/XOR two-node replacement)

The independent graphics clipping stream now admits a bounded disjoint two-node
OR/XOR replacement transaction. Two fresh public nodes copy the source-relative
rectangles and are linked ahead of the destination chain only after complete
admission; allocation, address-class, and Exec failures roll back without
publishing partial state. The replacement-list fixture, focused Region family,
and signed-word audit pass 6/0/0; generated image: 1,452,174 bytes / 72,389
branches / 3,428 relays / 7 chains / 3,346 boundaries. Monitor/provider
ownership is unchanged and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-19 / 530 (native AND contained two-node destination)

The independent graphics clipping stream now admits a canonical two-node
`AndRegionRegion` destination whose public envelope matches the source and
whose corresponding relative rectangles are contained by the source nodes.
The destination is already the exact intersection, so the path returns
success without allocation, retirement, or writes. Escaping, malformed, and
non-identical forms preserve D0 through fallback. The focused Region family
and signed-word audit pass 6/0/0; the generated image remains 1,451,612 bytes /
72,367 branches / 3,428 relays / 7 chains / 3,346 boundaries. Monitor/provider
ownership is unchanged and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-19 / 529 (native XOR identical two-node identity)

The independent graphics clipping stream now admits exact identity for
canonical two-node `XorRegionRegion` operands. Complete ownership, link,
absolute-envelope, and relative-node admission precedes Exec resolution; both
destination nodes are retired exactly once and an empty header is published,
while the source remains unchanged. Malformed/non-identical pairs preserve D0
through fallback. The focused Region family and signed-word audit pass 6/0/0;
the generated image is 1,451,612 bytes / 72,367 branches / 3,428 relays /
7 chains / 3,346 boundaries. Monitor/provider ownership is unchanged and
CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-19 / 528 (native OR identical two-node identity)

The independent graphics clipping stream now admits exact identity for
canonical two-node `OrRegionRegion` operands. The OR dispatch restores its
outer D0 frame before entering the shared two-node proof; complete ownership,
link, absolute-envelope, and relative-node admission precedes the read-only
success. Malformed/non-identical pairs preserve D0 through fallback. The
focused Region family and signed-word audit pass 6/0/0; the generated image is
1,450,694 bytes / 72,289 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 527 (native Region identical two-node identity)

The independent graphics clipping stream now admits exact identity for two
canonical two-node `AndRegionRegion` operands. Complete ownership, link,
absolute-envelope, and relative-node admission precedes the read-only success;
no allocation, retirement, or public write occurs. Empty/one-node destinations
reuse the existing two-node-source arm, while malformed/non-identical pairs
preserve D0 through fallback. The new AccurateM68000 identity/mismatch fixture
and focused Region/signed-word set pass 5/0/0; the generated image is
1,449,860 bytes / 72,212 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-19 / 526 (native Region partial two-node reduction)

The independent graphics clipping stream now admits a canonical one-node
source whose inclusive intersection is confined to exactly one node of a
canonical two-node destination. Complete link/envelope/Exec admission and
source-vs-other-node disjointness precede rewriting the exact selected
node/header and retiring only the unselected node. Both node orders,
spanning-source decline, and malformed fallback pass 8/0/0; the generated
image is 1,449,034 bytes / 72,136 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-18 / 524 (native Region empty-source cleanup)

The independent graphics clipping stream now admits a canonical two-node
destination for empty-source `AndRegionRegion`. It validates both list links,
the destination envelope, and both relative node envelopes before resolving
Exec, retires both public nodes exactly once, and publishes an empty header.
Malformed links preserve D0 through fallback. The new lifecycle test and
focused Region/signed-word set pass 7/0/0; the generated image is
1,447,834 bytes / 72,058 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-18 / 523 (native Region partial intersection)

The independent graphics clipping stream now covers canonical two-node
`AndRegionRegion` partial intersections confined to either source node. The
existing destination is clipped and rewritten in place; malformed,
gap-crossing, and both-node forms remain on the portable builder. The focused
Region set and signed-word audit pass 6/0/0; the generated image is
1,447,514 bytes / 72,035 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-18 / 519 (native Region alias verification)

The native Region alias identity remains green after the continuation audit:
OR preserves the shared node, while XOR clears and retires it exactly once.
The native Region family plus generated signed-word branch audit pass 5/0/0;
monitor/provider ownership remains unchanged and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-18 / 518 (native Region alias identity)

The native Region continuation now guards `OrRegionRegion` source/destination
aliasing before the Rectangle tail path. An aliased union returns success
without allocation, retirement, or public-byte mutation; the focused
AccurateM68000 regression and generated signed-word branch audit pass 2/0/0.
Monitor/provider ownership remains
unchanged, and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-18 / 517 (Intuition resource ledger)

The optional Intuition Screen graphics-resource ledger now serializes
prepare/commit/retirement per Screen, complementing the native
provider/rebind gate. Focused Intuition and screen/view verification remains
green; the full replacement is still active/incomplete and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-18 / 516 (verification boundary)

The compatibility `TryLoadView` gate remains green in the direct AmigaBoot
display lifecycle slice (153/0/0). The broader AmigaBoot memory run exposed
seven unrelated memory-list/DisplayInfo expectation failures; no change was
made to those areas. The full replacement remains active/incomplete and
CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-18 / 515 (compatibility LoadView gate)

The compatibility scheduler-aware `TryLoadView` handoff now shares the native
provider/rebind lifetime gate with graphics-overlay publication and Intuition
rethink. Screen-to-front and close-screen blanking therefore cannot observe a
split graphics-base transaction. Focused display-lifecycle verification is
green; the full replacement remains active/incomplete and CyberGraphX remains
excluded.

## CURRENT checkpoint — 2026-09-18 / 514 (Intuition rethink transaction)

The host Intuition `RethinkDisplay` path now shares the native monitor/provider
rebind gate across View reconstruction and ActiView/native-sidecar commit. The
blocked handoff/rebind regression passes 1/0/0; focused screen/view and
Intuition verification remains green. The full replacement is still
active/incomplete and CyberGraphX remains excluded.

## CURRENT checkpoint — 2026-09-18 / 513 (Intuition rethink gate)

The Intuition `RethinkDisplay` host handoff now executes under the shared
monitor/provider lifetime gate. View reconstruction and its ActiView/native
sidecar publication cannot race a graphics-base rebind. Build and focused
screen/view/Intuition verification remain green; the full replacement is still
active/incomplete and CyberGraphX remains excluded.

Part of the full Kickstart 3.1 graphics replacement; CyberGraphX is excluded.
This unit does not complete the full goal. Native MonitorSpec lifecycle and
Intuition preference updates remain separate work.

## CURRENT checkpoint — 2026-09-18 / 512 (screen/view lifecycle gate)

`LoadView` and its timed form now hold `_monitorStateSync` across native
active-view/CurrentMonitor/TopLine publication; refresh/clear helpers and
screen-prefix/resource ownership registries use the same gate. Native-overlay
`LoadView` admission is paired with the commit, with a deterministic overlap
regression passing 1/0/0 and the combined slice at 1472/0/11. Continue
screen/view lifecycle integration; the full replacement remains incomplete.

## CURRENT checkpoint — 2026-09-18 / 511 (MonitorSpec admission transactions)

Native-overlay `OpenMonitor`, `CloseMonitor`, and `GetDisplayInfoData` now
hold `_monitorStateSync` across provider writable-span admission and the
MonitorSpec/list or readback commit. Three deterministic rebind-overlap tests
pass; the surrounding focused slice is 1292/0/11. The full replacement remains
incomplete; continue MonitorSpec/provider lifecycle integration.

## CURRENT checkpoint — 2026-09-18 / 510 (overlay preflight transaction)

Native-overlay `GfxFree` now pairs writable-span preflight with the registry
and guest-memory teardown under `_monitorStateSync`. A blocked-provider
regression proves a graphics-base rebind remains pending until the free
transaction completes. Focused validation passes 1/0/0, with the surrounding
monitor/readback/lifecycle slice at 1289/0/11. The full replacement remains
incomplete; continue MonitorSpec/provider lifecycle integration.

## CURRENT checkpoint — 2026-09-18 / 509 (extended-node handoff gate)

Extended-node publication and ownership operations now use the same
`_monitorStateSync` gate as native CMDB publication and graphics-base rebind.
A blocking allocator regression proves rebind waits for the provider-backed
`GfxNew` transaction and leaves the node backlink on the new base only after
the move commits. Focused validation passes 1/0/0, with the surrounding
monitor/readback/lifecycle slice at 1288/0/11. The full replacement remains
incomplete; continue MonitorSpec/provider lifecycle integration.

## CURRENT checkpoint — 2026-09-18 / 508 (rebind retry)

The native-base rebind path now has an explicit retry regression. A provider
write refusal keeps the old binding and owned CMDB reachable; after writability
returns, the same rebind detaches and frees the old sidecar exactly once and
the new base becomes authoritative for later extended-node publication.
Focused retry validation passes 1/0/0. The full replacement remains
incomplete; continue MonitorSpec/provider lifecycle integration.

## CURRENT checkpoint — 2026-09-18 / 507 (retry-safe teardown)

Native display-database release now returns a completion status. The host
service keeps its native-base handoff pending when a sparse/provider boundary
declines the public CMDB APTR clear or the monitor-list rebind, so it cannot
free a still-reachable sidecar or lose the retry state. A later `Dispose` pass
finishes the clear, rebind, and exactly-once free. Focused validation passes:
pending-sidecar service teardown 1/0/0, runtime descriptor release/retry and
native-base lifecycle 8/0/0 and 23/0/0. The full replacement is still
incomplete; the next bounded unit is full MonitorSpec/provider lifecycle.

## CURRENT checkpoint — 2026-09-18 / 506 (readback/provider snapshot lifetime)

Registered CMDB MNTR readback now treats the guest registration and position
cells as the authoritative source for every read-only query. Partial snapshots
are never cached, and complete read-only queries do not seed the mutation
sidecar; this prevents a short first query or a later provider edit from being
hidden by stale zero/default fields. The mutable sidecar remains owned by the
private `SetDisplayInfoData` path, so explicit driver mutations continue to
survive subsequent queries. Owned native monitor-position publication and
partial-write rollback are unchanged and remain transactional.

Fresh verification: 84 registered-readback cases plus the owned-position
regression pass; the combined monitor-family/registration/name, native
registered-info, publisher/release, registration-lifecycle and descriptor
slice is 642/0/0. Emulator and test-project builds pass with the existing
NU1902 dependency warning. Four ROM mutation probes were discovered but
skipped because this environment has no configured ROM. The next bounded unit
is full MonitorSpec/provider lifecycle integration; Intuition and screen/view
lifecycle remain separate incomplete units.

## CURRENT checkpoint — 2026-09-18 / 504 (provider/lifetime synchronization)

The emitted 68k private SetDisplayInfoData path is now implemented as an
explicit opt-in extension. `NativeGraphicsRasterBodies.BuildCodeWithMonitorMutation`
adds private LVO -750 while leaving the public `GraphicsLvo` catalog and the
default raster stream unchanged. The corresponding initializer, image builder,
and HUNK builder options allocate an extended CMDB area containing two 88-byte
MNTR family records (PAL and NTSC). The native body validates the DtagMntr
envelope, copies the requested source prefix into the selected private record,
and publishes the registration pointer only after the copy completes. The
native GetDisplayInfoData path reads that published record, so subsequent
queries observe the mutation. Register and stack state are restored on success,
zero-length, and decline paths. A custom HUNK composition must provide the
private -750 offset explicitly; the raster-body HUNK helper supplies it. Native
CMDB and registered MonitorSpec release builders take the same mutation flag,
so validation and FreeMem use the extended extent on teardown as well.

The host monitor ownership domain is now serialized by a re-entrant private gate
covering native database publication, release/rebind, monitor open/close,
monitor-driver readback and SetDisplayInfoData mutation. This prevents a
provider callback or concurrent teardown from observing a candidate allocation
before its guest pointer is
committed. The gate is host-side lifetime synchronization only; it does not claim
to emulate an Exec semaphore or inter-task signal.

Fresh validation from the current source: native mutation PAL/NTSC 2/0/0 and
the relocatable HUNK private-vector map 1/0/0;
native monitor initialization 39/0/0; initializer/descriptor/lifecycle/state
publisher 301/0/0; MonitorSpec publisher/composition 207/0/0; native MNTR
readback 196/0/0; canonical default GetDisplayInfoData 1/0/0; monitor
open/close and registration 1008/0/0; serialized publication/release 1/0/0.
Existing image-profile tests remain green, including the default (mutation disabled) image
fingerprints. The focused mutation test runs against the configured Kickstart
3.1 A500 ROM and checks full 88-byte and 19/20/21-byte boundary transfers,
source/allocator stability, and both monitor families. The mutation-enabled
native release extent check also passes 1/0/0; the default release and
registered-MonitorSpec release suites remain green. The opt-in path is deliberately separate
from CyberGraphX and does not claim the full MonitorSpec, Intuition, screen, or
view lifecycle.

## Historical checkpoint — 2026-09-17 / 493 (portable private-driver slice)

All current qualification runs are terminal. Full474 COMPLETE14819/0/22,
25m39s;87842 CLOSED, graphics-full-union.trx. No ROM environment. Final rows
verified. The full union includes the unchanged native admission class and all
current host lifecycle changes.
Strict479 original-Exec host/native lifecycle COMPLETE24/0/0,5m1s;89507 CLOSED,
host-native-lifecycle-rom.trx. Final rows verified:20 resident handoff/initialization
and4 native owned-monitor readback. Only that process had ROM environment.
Full474 is the authoritative union for the current source because its build was
created before the final479-only test additions but after the production479
changes; the separate479 union below covers the newer host tests.
Current479 union excluding the unchanged native admission class COMPLETE7335
passed/0 failed/22 optional ROM skips,4m8s;78064 CLOSED. Final rows verified,
including all3 new capture-failure cases (24 scenarios). TRX
graphics-union-except-unchanged-native-admission.trx under479 results.

The private SetDisplayInfoData oracle is now exact and terminal on the configured
Kickstart 3.1 A500 ROM. Fresh artifact488 rebuilt the test project from the
current source, then ran both PAL and NTSC size cases and both PAL/NTSC field
cases: size COMPLETE2/0/0,14s and field COMPLETE2/0/0,14s. The size cases assert
the measured return function `size < 20 ? 0 : min(size,88)-20`, source and
allocation stability, Mspc visibility at offset16, and restoration after every
probe. The field cases assert byte-for-byte full-record readback after mutations
at offsets20,24,44,76,80,84, with source and allocation stability. Results are
`monitor-mutation-size-oracle.trx` and `monitor-mutation-field-oracle.trx` under
`.codex-artifacts/graphics-mutation-488/results`.

The first portable private-driver slice is implemented and scoped separately:
`GraphicsPrivateLvo.SetDisplayInfoData` keeps -750 outside the public LVO enum,
the native-overlay registry installs a private gateway, and Core stores the
mutable 88-byte MNTR family image without changing CMDB3 size/version. Focused
host mutation/manifest/overlay tests pass2/0/0 in46ms; the combined host/display
regression slice is COMPLETE5/0/0 in94ms, and the admission oracle passes
PAL/NTSC2/0/0 in14s (artifact492); the post-snapshot host regression remains
green2/0/0 in47ms in artifact493. The emitted68k private body is still
the next unit; public readback and current native database fingerprints remain
unchanged.

476 strict COMPLETE24/0/0,3m59s;77532 CLOSED, final rows verified:20 resident
initialization/handoff and4 native owned-monitor readback.476 focused1216/0/8
and478 focused1222/0/8 are closed (76696/49737);478 duration14s, rows verified.
477 red state-preservation probe COMPLETE0pass/6fail,229ms, confirming original
Points reset at trailer offset8 (registration/default values also not preserved).
477 build PASS26.44s;48216 CLOSED.478 build PASS30.02s;9646 CLOSED.

478 Core saves all mutable CMDB3 trailer values before release/rebind: both
current/original Points, valid stored default family, and raw NTSC/PAL slots.
All reads must succeed before any saved copy changes. Unreadable/malformed
state retains the allocation/binding. Captured slots, including deliberate
zero/opaque values, supersede fresh resident bootstrap on republish and never
acquire node ownership. Provider replacement clears stale saved registration.
Constructor accepts separate original-Point input; guest layout/version remain
unchanged.479 adds24 read-failure/invalid-family retry scenarios spanning normal
release, cleared APTR and rebind.479 build PASS24.37s;50884 CLOSED.
Source identity (SHA256), production unchanged between478 and479:
GraphicsLibraryCore.cs:59ED08390CD9BA3712E4ABA23F08A15647B8EB7E9A171B8E7FA865ECFB84D788
GraphicsDisplayDatabase.cs:E4AB02F7D4D6B010E9341AB12335AB9BF3AD1EB2CEA6E274D9DAB98C407926AA
GraphicsMonitorOperations.cs:0B135544C505DFA1E6205A5C004B7915BE9B75BF65EE159C75BDD785266C0A1F
NativeGraphicsRasterBodies.MonitorInfo.cs:E7BD4CE78226FB436D84643F581FC4917BEA5669092A81CD855C07383AB9E310

Next: begin the private monitor-record oracle described in
GRAPHICS_LIBRARY_MONITOR_DRIVER_RECORD_MUTATION_GOAL.md. New bounded unit document is
GRAPHICS_LIBRARY_HOST_MONITOR_HANDOFF_GOAL.md; next oracle/implementation units
are in GRAPHICS_LIBRARY_MONITOR_DRIVER_RECORD_MUTATION_GOAL.md.
Full goal remains incomplete;
no permission/input blocker. No CPU/chipset/CyberGraphX changes.

The persistent objective is active. The full graphics goal remains incomplete;
the private mutation representation and host lifetime gate are now complete for
this slice. The next bounded unit is full MonitorSpec/provider lifecycle
integration, followed by the Intuition and screen/view units.

## Historical checkpoint — 2026-09-17 / 477 (host lifecycle continuation)

Full graphics474 remains LIVE87842; immutable artifacts are474, no ROM environment.
476 host publication/rebind regression COMPLETE1216/0/8 ROM skips,15s;76696 CLOSED.
476 strict host/native original-Exec integration LIVE77532 (24 expected cases),
host-native-publication-rom.trx. Only this process has configured ROM environment.
Native byte stream unchanged since469; fingerprint remains as in474 below.

474 extra original-Exec resident integration exposed four host-fixture failures:
COMPLETE16 passed/4 failed/0 skipped,3m29s;15859 CLOSED. Each expected lazy
OpenMonitor to allocate after marking CMDB-owned state; that is now deliberately
a lookup. The corrected fixture creates a resident in an explicit unmarked
compatibility base, then hands it to the native library through the real host
rebind/publication path. This is host takeover, not native MonitorSpec creation.
It additionally checks native MNTR raw pointers and unchanged original ROM bytes.
An initial misnamed filter matched zero tests (host-publication-rom.trx); it is
NOT a pass. The actual red20-case report is host-publication-resident-rom.trx.

That handoff exposed a real default-pointer lifetime gap: rebind moved list and
backlinks but left an owned DefaultMonitor in the old base.475 red test4failed/
1passed,234ms confirms it; build PASS24.37s.476 includes owned default detach/
attach in the existing snapshot/byte-rollback transaction. Borrowed source
defaults remain unchanged; a foreign destination default declines unchanged.
Both partial-LONG fields and retry are covered on PAL/NTSC.476 build PASS24.09s;
11470 CLOSED. Its focused result is above, original-Exec result is still live.

477 adds a red test for capture/restore of changed default family, raw NTSC/PAL
mappings and current/original Points across release, externally cleared APTR,
and rebind.477 build LIVE48216. No production change yet for that state-loss
unit. Next collect build, run the new red probe, implement explicit saved state
without adopting/dereferencing borrowed mappings, verify failure/retry, and
collect77532/87842 without restarting. Full graphics goal remains incomplete.

## Historical checkpoint — 2026-09-17 / 474 (host registration bootstrap)

Native469 broad COMPLETE7474 passed/24 failed/0 skipped,19m30s;22096 CLOSED.
Final rows verified:8 first-even default admission,8 pointer-prefix envelopes,
8 overlapping Point publication; all share the poisoned synthetic A6=2 size.
471 seeds that fixture's compact lib_PosSize only. Focused legacy/registered
boundaries COMPLETE112/0/0,29s;6496 CLOSED. It includes all24 former failures.
Native production unchanged since469. Strict470 COMPLETE20/0/0,2m24s and focused
470 COMPLETE291/0/0,1m2s;10205/25543 CLOSED. Do not revive old live handles below.

472 adds a failing host publication/rebind regression: existing registry-owned
resident pointers were absent from newly constructed CMDB3. Red4/4 failed,
308ms, expected4000 versus slot0; host-publication-red.trx (472 build PASS22.42s).
473 Registry.TryGetResidentRegistrations snapshots known family/associated-node
ownership without allocating/opening or inferring identity from names/flags.
TryCreateNativeDatabase stages supplied NTSC/PAL slots under its whole-allocation
rollback. Core snapshots before construction, rechecks base/ownership/public APTR
and residents before publication, and frees only an unpublished candidate when
the handoff changed. Existing published/provider databases are not re-seeded.
This is initial construction (including residents moved before publication),
NOT preservation of guest-mutated mappings across database release/rebind.
473 build PASS18.96s;32670 CLOSED. Host publication/monitor regression COMPLETE
1208 passed/0 failed/8 ROM-conditioned skips,9s;46426 CLOSED; final rows/skips
verified. TRX host-publication-regression.trx under473 results.

474 adds partial-LONG rollback/retry for all3 registration-trailer fields and
post-construction provider/rebind/backlink changes with successful retry. Build
PASS19.09s;4133 CLOSED. Focused publication regression COMPLETE1211/0/8 ROM skips,
11s;28781 CLOSED. Full graphics union474 LIVE87842 (graphics-full-union.trx), no
ROM environment. Native
image fingerprint remains PAL1445774/NTSC1445674,71886branches,3428relays,7chains,
3346boundaries. No CPU/chipset/CyberGraphX changes. Full goal remains incomplete.
Artifacts/results follow C:/Users/vsys-admin/AppData/Local/Temp/
codex-graphics-monitor-selection-20260917-NNN and
graphics-monitor-selection-20260917-NNN respectively.
Next: qualify474, preserve the resulting full-union handle, then implement
capture/restore of stored default family, raw mappings and original Points on
host-owned CMDB teardown/rebind. Borrowed mappings remain raw and do not grant
ownership of their nodes. Driver mutation/synchronization remain later units.

## Historical checkpoint — 2026-09-17 / 471 (legacy boundary qualification)

Previous463 turn was verified progress (host registered readback and native
static metadata parity). Full463 COMPLETE14779 passed/1 failed/22 ROM-conditioned
skips,20m6s;15760 CLOSED. Final rows verified: its sole failure was the stale
SuperHires MNTR ViewResolution1 assertion;464 corrects it to family44, and466
passes it. Do not resurrect15760; current native broad run is469 LIVE22096.

464 adds NativeGraphicsRasterBodies.MonitorInfo.cs, called after the universal
INVALID_ID sentinel. Registered native queries admit CMDO/CMDB3, resolve all
88 selector spellings/authoritative handles through stored default family, read
Mspc/current/original only beyond16/20/80 bytes, construct a private88-byte
stack image and copy the exact capped span bytewise (including odd destinations).
No MonitorSpec/list/default reads or reference operations participate. Full
D0-D7/A0-A6 frame restores all but resultD0 on success and all on decline/legacy.
Non-MNTR/compact paths retain the existing publisher.466 focused native tests
PASS193/0/0,1m5s;6146 CLOSED. Broader qualification remains outstanding.

464 build PASS42.02s;80424 CLOSED.465 build PASS22.71s;39564 CLOSED.465 adds
native fixed/HUNK/direct/AUTOINIT/PAL/NTSC tests, changed default families,
all88 aliases, authoritative handles, opaque pointers and partial-field reads.
The existing owned-position fixture now seeds complete CMDB3 default/registration
state; short-prefix and owned-current tests now assert no DefaultMonitor reads.
465 native registered/owned-position/image-audit tests COMPLETE85 passed/82 failed,
50s;18327 CLOSED. Inspection found64 old118-byte stack-guard assumptions,
16 overlap checks forbidding legitimate CMDB source reads, and2 old fingerprints.
466 updates guards to152 bytes only for declared runtime fixtures (return4 +
saved60 + private image88), preserves immediately adjacent guard bytes, and
checks no writes to overlapping output tails while allowing required source
reads. Shared program body unchanged since464. Measured profiles now
PAL1445772/NTSC1445672bytes,71887branches,3428relays,7chains,3346boundaries.
466 build PASS25.92s;69951 CLOSED; all193 focused cases passed,1m5s.
467 adds whole-CMDB read-only checks, overlap/final-byte/wrap/empty/foreign/
malformed-pointer cases and precise executed-PC provider detection including
linker RTS clones (separate from legacy global-only fixture signal).467 build
FAILED19.94s on an int/uint switch literal;58575 CLOSED.468 corrects0 to0u;
build PASS19.58s;8736 CLOSED. New8 boundary cases PASS8/0/0,12s;54927 CLOSED.
469 preserves the legacy final MOVEQ0/RTS sequence for empty queries with a
separate restore exit. Build PASS20.09s;27587 CLOSED. Its native alias/boundary/
profile audit COMPLETE57 passed/3 failed,20s;5226 CLOSED. Only3 stale fingerprints
failed: actual PAL1445774/NTSC1445674,71886branches,3428relays,7chains,3346boundaries.
Full native display-admission class469 LIVE22096; code is the same as470.
470 pins these measured fingerprints and extends original-Exec/native integration
to query all66 installed-family spellings with borrowed/null/opaque mappings at
odd output addresses. The full88-byte expected images are independent457 ROM
literals; database bytes/counts/guards/allocation balance must stay unchanged.
470 build PASS22.33s;20450 CLOSED. Strict integration COMPLETE20/0/0,2m24s;
10205 CLOSED, registered-mntr-rom.trx. Updated host/native/owned-position/image
audits COMPLETE291/0/0,1m2s;25543 CLOSED, registered-mntr-audit.trx. Final result
rows verified for both470 reports. Only strict10205 had ROM environment;
broad469 LIVE22096 does not. Do not restart the closed focused/strict runs.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-465
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-465/native-registered-mntr.trx
Filter:FullyQualifiedName~NativeRegisteredMonitorInfo|FullyQualifiedName~NativeDisplayInfoDataMntrRuntimeDescriptor|FullyQualifiedName~NativeDisplayInfoDataMntrDefaultViewPositionReadsDistinctOriginalPoint|FullyQualifiedName~NativeBootProfileCodeHasAuditedWordBranches|FullyQualifiedName~DisplayDatabaseExposesEcsSuperHiresGeometryThroughPureAndRegisterPaths
469 artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-469
469 native broad TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-469/native-display-admission.trx
469 filter:FullyQualifiedName~NativeGraphicsDisplayInfoDataAdmissionTests
470 uses the same artifact/results roots with suffix470.
Next: collect native469 LIVE22096 without restart, diagnose any legacy regressions before claiming the
registered query unit qualified. Then run current full union and continue host
publication/rebind, monitor-driver mutation and synchronization requirements.
Current helper SHA256:E7BD4CE78226FB436D84643F581FC4917BEA5669092A81CD855C07383AB9E310
Full goal remains incomplete; no blocker. No CPU/chipset or CyberGraphX changes.

471 continuation: strict470 and focused470 are closed/passed as recorded above.
Broad469 LIVE22096 reported8 first-even synthetic-base failures after12 minutes.
Focused470 legacy-default-admission.trx confirms56 passes/8 failures;83333 CLOSED.
The A6=2 fixture poisons lib_PosSize and CMDO bytes with C3, accidentally declaring
a full malformed native envelope.471 explicitly seeds compact lib_PosSize at
that synthetic base only; actual runtime fixtures and malformed-native tests
are untouched. Production unchanged.471 build PASS37.67s;83413 CLOSED.
Focused legacy/registered boundary run started from471; collect its handle below
and broad469 without restart. Host publication source review finds newly created
host CMDB slots still zero even for existing registry-owned residents; rebind
captures only current Points, not default-family/raw mappings. Those are next
implementation units, not yet fixed or claimed complete.

## Historical checkpoint — 2026-09-17 / 463

Previous turn was progress:458 shared family image/host metadata correction.
Current turn implements host read-only registered MNTR queries and begins native
metadata parity. Full452 already COMPLETE14451/0/22,21m42s;18693 CLOSED.
458 broad Monitor|DisplayInfoData regression COMPLETE9435 passed/20 failed/
9 ROM-conditioned skips,22m8s;87017 CLOSED. Final rows verified:8 boot-profile,
4 default-PAL full-record and8 full-span cases, all old native metadata mismatch
(e.g. expected ViewResolution44,actual4). All20 cases pass on463. No other failures.
Fresh full463 LIVE15760; includes GraphicsMonitor tests beyond the prior union.

459 adds GraphicsMonitorInfoReadback: CMDO/CMDB3 structural admission, stored
default family, opaque raw Mspc, separately requested current/original Points.
Core queries snapshot before any output transaction and never Open/adopt/free
registered data. Compact/unmarked images retain the host backend; malformed
declared-native state declines before output writes. Header-only queries need
no pointer, pointer prefixes need no Point, sizes<=80 need no original Point.
Default-marker1000 aliases work in MNTR ID and authoritative-handle paths.
Normalization preserves a nonnull1000 handle as DefaultModeHandle, not NULL.
Portable providers now run only when their fields are requested. Native-overlay
preflight skips node-publication checks for registered readback and headers.

New registered tests cover direct/ordinary-adapter/native-overlay routes,
both host profiles and opposite stored defaults,88 selectors and authoritative
handles at10 sizes (21120 requests), invalid/empty/opaque pointers, unreadable
default/list/node memory, malformed descriptors/databases, separate field
boundaries, overlap with DB/CMDO and partial-byte failure rollback/retry.
NoAllocator throws if a query allocates/frees; whole guest memory is checked.
459 build PASS43.08s;25496 CLOSED; focused265/0/0,1s.
460 build PASS21.44s;27564 CLOSED. Added no-read/compact-provider checks exposed
wrapped-destination native-overlay preflight reads: red probe2pass/1fail,375ms.
461 adds an overflow guard before that preflight. Build PASS21.65s;80083 CLOSED.
461 focused COMPLETE274 passed,0 failed/skipped,1s (includes9 new boundary cases).

462 updates the existing native full default-MNTR publisher to emit the same
GraphicsMonitorInfoImage static bytes, retaining snapshotted A0/D6/D5 dynamic
pointer/Points. Other-mode payload admission and registered pointer selection
are NOT yet changed. Existing native profile parity test now additionally pins
its expected full record to independent457 literals. Corrected old scalar
assertions in NativeGraphicsGetDisplayInfoDataPublishesCanonicalDtagChunksOnAccurateM68000.
462 build PASS21.95s;78891 CLOSED. Native-family/parity/branch audit COMPLETE
33 passed/3 failed/0 skipped,44s;40761 CLOSED. All3 failures are stale size-only
fingerprints: measured PAL1444712/NTSC1444612 bytes (110 fewer), unchanged71812
branches/3428relays/7chains/3346boundaries. Structural branch checks passed.
462 host monitor/overlay regression COMPLETE1227 passed/0 failed/8 ROM-conditioned
skips,14s;65580 CLOSED; final rows and skipped identities verified.
463 pins measured image sizes, no production changes since462. Build PASS22.71s;
37916 CLOSED. Combined host/native profiles/record parity/transfer/ID-selection
checks COMPLETE435 passed/0 failed/skipped,46s;11248 CLOSED, mntr-host-native.trx.
Native full-span cases COMPLETE8/0/0,4s, mntr-native-span.trx.
Strict original-ROM lifecycle/registration COMPLETE20 passed/0 failed/skipped,
2m13s;29630 CLOSED, mntr-rom-lifecycle.trx. Final rows verified. Only that process
had ROM environment. The sole live run is now full463 LIVE15760; collect next.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-462
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-462/native-family-record.trx
461 artifacts/results follow the same roots with suffix461; focused TRX registered-mntr-host.trx.
460 red TRX empty-query-red.trx.459 focused TRX registered-mntr-host.trx.
463 artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-463
463 results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-463/
Full463 TRX:graphics-full-union.trx
Full463 filter:FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer|FullyQualifiedName~GraphicsMonitor

Next: collect current full463 terminal results without duplicates;
extend native
MNTR registered-family/raw-pointer selection and all valid aliases/partial sizes.
Host publication/rebind and driver mutation/synchronization remain later units.
Full goal incomplete; no blocker. CyberGraphX excluded; no CPU/chipset changes.

463 production identity (SHA256):
GraphicsMonitorInfoReadback.cs:4F6BC1162263781C23B0B3120E9BD73E660500786140629372DC7B5B275C1ECD
GraphicsLibraryCore.cs:35A8A0376EAE0C9B297B66A6A86DC55C6F635F1F8AD03261C41C69F4287A6BF5
GraphicsPortableHostAdapters.cs:911DBD28CCB6618D37610E66B3D5CDACCBD771E20212D26E467809BCDDECB8D7
GraphicsDisplayDatabase.cs:7FF5F7F1012D479832B4FBFC9CF5A2D23AD6A27F3E14D591CAC27DF676C70AF8
NativeGraphicsRasterBodies.cs:C4E22F53A7880C836A27D726ECA783F24D56D1ADE7E110FB6C9C3D395B106A4F

## Historical checkpoint — 2026-09-17 / 458

Resumed from455 without restarting the live452 full run. Full452 now COMPLETE
14451 passed,0 failed,22 ROM-conditioned skips,21m42s;18693 CLOSED. Final TRX
outcomes verified. Immutable artifacts/results/filter are recorded below.
455 and456 strict original-ROM probes are COMPLETE2 passed,0 failed/skipped each,
21s.457 pins literal full88-byte baselines and PASSED2/0/0,20s;11804 CLOSED.
457 build output was lost at compaction, but completed artifacts were recovered
and tested with --no-build/--no-restore. No claim of its unobserved build exit.

The full record is family data across all66 valid spellings, including default
aliases; Mspc is opaque (0/1/odd/wrapped/DEADBEEF/FFFFFFFF are returned unchanged).
MonitorInfo's resolution, ViewPositionRange, Compatibility, opaque padding,
MouseTicks and PreferredModeID differed from the old replacement assumptions.
See GRAPHICS_LIBRARY_MONITOR_INFO_READBACK_GOAL.md for measured bytes and scope.

458 introduces GraphicsMonitorInfoImage, a portable96-byte guest-layout family
image with88 transferred bytes, independently captured boot metadata and explicit
raw Mspc/current/original Point inputs. Host BuildMonitorInfo now uses it, without
changing MonitorSpec timing or existing provider/allocation/admission policy.
Corrected stale host assertions that conflated MonitorInfo with MonitorSpec.
New tests cover both profiles, all66 existing mode records, ID/authoritative-
handle paths, even/odd destinations and19 partial/full sizes (10032 queries),
plus opaque pointers, signed Points and fresh-image independence. Full expected
bytes are literal457 ROM captures, not replacement-derived expectations.

458 build COMPLETE PASS20.98s;9863 CLOSED (only existing NU1902/xUnit2013 warnings).
Focused family/transfer/host tests COMPLETE45 passed,0 failed/skipped,1s.
Additional host display/dimensions/monitor/name chunk test PASSED1/0/0,37ms
(host-chunks.trx); this covers the corrected full-record assertions directly.
Monitor/DisplayInfoData regression LIVE87017; collect before changing tests.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-458
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-458/
TRX:family-record-host.trx,monitor-regression.trx.
Filter:FullyQualifiedName~Monitor|FullyQualifiedName~DisplayInfoData
457 strict:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-457/mntr-family-record-rom.trx

Next: finish458 verification, then read-only CMDB host query admission (raw Mspc,
stored default family, aliases, granular Point reads). Native query integration
still uses old metadata/DefaultMonitor for mode0 and declines other payloads;
the native code has NOT changed in458. Do not claim host/native query parity or
full registration/query coherence. Follow the five units in the readback goal.
Full goal incomplete; no blocker. CyberGraphX excluded; no CPU/chipset changes.

458 SHA256 source identity:
GraphicsMonitorInfoImage.cs:1685CA7D8FD2B849CE73A02FC6F2B0588623F8E50509F0BB08A97588A93252C0
GraphicsDisplayDatabase.cs:71C3A376034487DD966E4136DC7D971EC062238E16E1226F9520061409FBE793
GraphicsMonitorInfoFamilyRecordTests.cs:3251403BEEF09A5253A9C00471013D561D417E678957428B684C0CFABED73BF2
KickstartRomGraphicsMonitorIdRegistrationTests.cs:BB5D98DBF44EF5E1DF41D623A3260B99E1ED30CABF29E8F75E6BB8E1ACCCA1C7

## Historical checkpoint — 2026-09-17 / 455

Previous turn made verified progress (host ID registration452,3012 monitor
passes). Prior native full447 is now COMPLETE14451 passed,0 failed,22 original-ROM
conditioned skips,20m7s;35309 CLOSED and final outcomes verified. Full452 is now
LIVE18693, immutable452 artifacts with no ROM environment. Do not duplicate it.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-452
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-452/graphics-full-union.trx
Filter:FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer

453 extends the original-ROM registration oracle with MNTR readback for0/4/1000/
installed-family/family|4 at each linked/unlinked/renamed/null-mapping stage.
Both profiles PASSED2/0/0,21s;63290 CLOSED; build PASS23.19s;27227 CLOSED.
Unlike OpenMonitor(0), query ID0 follows the registered Mspc. Reads return88 bytes
and do not change either node's open count or allocation balance.
454 also writes opaque pointer values1,candidate+1,FFFFFF60,DEADBEEF,FFFFFFFF
through original SetDisplayInfoData, then queries them (never Open/dereference).
Both profiles PASSED2/0/0,20s;48712 CLOSED; build PASS29.01s;7913 CLOSED.
Set returns68 and readback returns the raw stored pointer unchanged for each alias.
Restore original record before freeing allocations; original ROM vectors/list/
default snapshots remain pinned. This proves query readback must NOT reuse the
selected-node validity/refcount checks from OpenMonitor.

455 expands readback to all66 valid ID spellings from the selector oracle and
compares the entire88-byte family record, logging differing offsets if the
family-record inheritance hypothesis fails. No replacement behavior changed yet.
Build455 PASS21.58s;22870 CLOSED. Strict expanded probe LIVE17341.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-455
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-455/mntr-family-record-rom.trx
Prior probes:graphics-monitor-selection-20260917-453/mntr-registration-rom.trx and
graphics-monitor-selection-20260917-454/mntr-opaque-registration-rom.trx under the
same Temp root. Next collect17341, preserve452 full18693, then implement shared
read-only CMDB selection for MNTR. Header-only/partial transfer dependencies,
stored default-family identity and family-vs-mode fields must be preserved.

Code findings for the next unit: host FindOrCreateForDisplay resolves default
through host profile; BuildMonitorInfo derives pixel ticks/PreferredModeID from
the individual ModeRecord. TryResolveMode does not currently recognize default
marker1000 aliases. Native GetDisplayInfoData admits a payload only for selected
mode0 (nonzero modes fall back); its pointer admission precedes the public frame
and currently reads DefaultMonitor, while MNTR headers are A6-read-free. Point
reads are separately staged by AppendOwnedMonitorPoint. Do not simply replace
the pointer getter with OpenMonitor: raw registered values are legal query data,
and the remaining metadata/alias/family handling must follow the ROM evidence.
Full goal incomplete; no blocker. CyberGraphX excluded; no CPU/chipset changes.

## Historical checkpoint — 2026-09-17 / 452

Previous turn made verified progress (native CMDB3 ID selection447). Full447
remains LIVE35309, immutable447 artifacts; do not duplicate it. Its exact paths
and filter are in historical447 below. Latest completed full443 is14055/0/22.

448 implements host CMDB3 ID selection in GraphicsMonitorOperations.Ids.cs,
wired into Registry.Open and TryOpen. Explicit native runtime state resolves
nonzero IDs through admitted CMDO/CMDB3 and the stored default family. Exactzero
selects DefaultMonitor independently of DB validity; names retain precedence.
Valid empty registration claims NULL; invalid/partial state declines without
allocating. Borrowed associated nodes use the existing WORD snapshot/rollback
reference transaction and never enter allocation ownership. Compact images with
no native runtime claim retain OpenBackend. A full declared but truncated suffix
or nonzero foreign marker does not authorize fallback allocation. Internal
initialOpenCount0 discovery returns the selected pointer without incrementing.

144 new PAL/NTSC/direct/register-adapter cases cover88 valid IDs each, borrowed
unlinked/name-and-flag-independent nodes, changed default family, absent/opposite
registrations, bad descriptor/database/node fields, unreadable ranges, saturation,
failed/partial count writes and retry; all use an allocator that throws if called.
449 additionally checks INVALID_ID/unknown IDs and zero-count discovery.
448 build PASS38.88s;92503 CLOSED. Focused host selection COMPLETE235 passed,
0 failed/skipped,2m48s;85192 CLOSED. Monitor regression COMPLETE3008 passed,
0 failed/skipped,3m6s;71100 CLOSED.449 build PASS22.49s;9075 CLOSED (no tests).
450 build PASS20.40s;16823 CLOSED (no tests); it keeps full-byte verification but
uses Span.SequenceEqual as its success path, retaining detailed array diagnostics
on mismatch to avoid repeated element-assertion overhead across91 requests.
451 corrects review findings: exactzero checks a published nonnull DefaultMonitor
before any CMDO read, and INVALID_ID claims NULL without reading GfxBase, matching
native admission. Unknown ID syntax declines without backend allocation. Added
unreadable-entire-runtime coverage (now148 matrix cases).451 build PASS19.50s;
1723 CLOSED.451 focused host selection COMPLETE239 passed,0 failed/skipped,2s.
451 monitor regression COMPLETE3010 passed,2 failed,0 skipped,2m18s;58506 CLOSED.
Its only failures were two stale INVALID_ID adapter
expectations (both profiles) in OpenMonitorHostSelectorsMatchOriginalRomDefaultAndCanonicalNames.
452 updates them: INVALID_ID is claimed NULL as the original ROM/native body;
an additional unknown-ID assertion retains provider decline and original D0.
452 build PASS21.88s;67166 CLOSED. No production changes since451.
452 focused host/name/close/corrected-compact checks COMPLETE241 passed,0 failed/
skipped,2s; final rows verified (148 new ID cases). Final monitor COMPLETE3012
passed,0 failed/skipped,2m16s;67066 CLOSED, final TRX rows verified.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-448
448 results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-448/
TRX:host-id-selection.trx,monitor-regression.trx.
452 artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-452
452 results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-452/
Only full447 LIVE35309 remains; collect it without restarting. All452 and earlier
test/build handles are terminal.452 monitor TRX:monitor-regression.trx;
focused TRX:host-id-selection.trx. Advance MNTR readback after verification.
MNTR readback still needs explicit shared mapping/default-family and malformed
state semantics, plus host publication/rebind integration and driver mutation.
FindOrCreateForDisplay still normalizes default family through the host profile
before its call to Open: this is not yet full query coherence. Native MNTR still
uses DefaultMonitor. Do not claim unit5 or full monitor compatibility complete.
Full goal incomplete; no blocker. CyberGraphX excluded; no CPU/chipset changes.

SHA256451/452 production:
- Host ID selector:1EF40FC764911DEEB2FB6EF11225113FA5806CB0DCEE673788DF9286982F4096
- Monitor operations:EB42A73D3667BE0B382CE288A101C04705640FFDB300105932D2F87434086BE2
- Name/register integration:0C68428D1CCF2DB46965431D7A634014163F13823337C61ED6D64BB1AE3E15E1
- Host ID tests:326DD6EC7449BB340AD0F97944E5DC8568E41452502E1ACFC73CF957FA92998F

## Historical checkpoint — 2026-09-17 / 447

Previous turn made verified progress (native registration lifecycle445). Prior
full443 is now COMPLETE14055 passed,0 failed,22 ROM-conditioned skips,20m2s;
63806 CLOSED, final TRX outcomes verified. No prior run is live.

446 replaces native CMMO/default-based nonzero-ID selection with owned CMDB3
lookup before any DefaultMonitor read. Exact ID0 and named default alias retain
the public default pointer; nonnull names bypass ID lookup. CMDO/public pointer,
version/size/alignment/envelope, both position IDs and stored default family are
validated. Nonzero aliases resolve from the stored family. A valid empty slot
returns claimed NULL. Invalid state declines preserving the original request.
Selected full160-byte node requires type18/kind0204/backlink; no CMMO allocation
ownership, canonical name, flags or list membership. Existing count saturation/
register preservation and executed-PC fallback provenance remain.

446 build PASS38.03s;96200 CLOSED. Native OpenMonitor checks520 passed; the only
two failures were stale profile fingerprints, with all structural checks passed.
Measured PAL1444822/NTSC1444722bytes,71812branches,3428relays,7chains,3346boundaries
(+152bytes/+14branches).447 updates both fingerprint assertions from that output,
adds168 fixed/HUNK/AUTOINIT/PAL/NTSC borrowed/default-independent/malformed-CMDB
cases (88 IDs each), and expands four strict native/original-Exec lifecycle tests
with132 registration-mutation selections each, closing every acquired reference.
Original-ROM mutation oracle442 remains the independent contract, unchanged.

447 build PASS20.48s;90927 CLOSED. Native profile/branch audit COMPLETE28 passed,
0 failed/skipped,2s. Monitor regression COMPLETE2864 passed,0 failed/skipped,
2m26s;1975 CLOSED (includes all168 new independent-registration cases).
Strict original-Exec lifecycle/AUTOINIT COMPLETE16 passed,0 failed/skipped,
1m47s;91049 CLOSED. Final TRX outcome rows verified. Artifacts:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-447
446 results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-446/cmdb-id-selection.trx
447 results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-447/
TRX:monitor-regression.trx,cmdb-selection-rom.trx,profile-audit.trx.
447 full graphics union is now LIVE35309, no ROM environment, same immutable447
artifacts and results directory; TRX graphics-full-union.trx. Filter:
FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer
Only35309 is live. Poll it without restarting. Latest completed full443 predates
this selector/lifecycle integration and must not be cited as447 evidence.
Next implement host registration
selection and MNTR readback against the same mapping. Those remain incomplete;
do not describe native selection alone as full monitor compatibility.
Full goal incomplete; no blocker. CyberGraphX excluded; no CPU/chipset changes.

Next source map: Registry.Open in GraphicsMonitorOperations.cs and Registry.TryOpen
in GraphicsMonitorOperations.Names.cs still fall through to lazy OpenBackend for
IDs. Add registered ID selection/claimed-absence before that fallback, preserve
compact unmapped host backend behavior, and reuse the existing associated-node
count transaction (partial WORD-write rollback). Core GetDisplayInfoData currently
passes FindOrCreateForDisplay as monitor provider; registered readback must not
allocate or increment. Native MNTR still returns DefaultMonitor and needs the
same mapping authority. Distinguish invalid database from valid absent mapping;
do not use lifecycle admission's compile-time profile as the selection family.

SHA256447:
- Native ID selector:64D65011177C78A21E83AC929E3F77BA5B8CEF401BC3A25BAA36CB968FE00A11
- Native raster body:6973CE0ED943284CE17D18C68FB63EA8BBE13A272681942FC7CA76E1AF02A773
- OpenMonitor tests:28F3DA1A66DF0F0021275B5CF783B926EA0D4BF975AFEECD536E15568CEC7E59
- Strict lifecycle tests:FDE9A9A29A319AEC64767E9B231BC6A1BBD07542FBBA47F0E5A427B6209CA1B0

## Historical checkpoint — 2026-09-17 / 445

Resumed directly from443; full goal incomplete, no blocker.444 implements the
explicit paired native registration lifecycle. BuildRegistered publisher/release
admit CMDO ownership, public pointer identity, the full aligned CMDB3 envelope,
both position-record IDs and the profile's stable default-family ID. Independent
Build paths still do not read foreign CMDO state. AUTOINIT composition now uses
registered publication. Public layouts and CMDO/CMMO versions are unchanged.

Publication refuses an occupied selected slot both before AllocMem and after its
yield. Only its fresh candidate is freed on a failed recheck. The initialized
node is registered before public list/default pointers, with CMMO tag last.
Registered release admits everything before its first write, invalidates CMMO,
clears either/both slots only when equal to the retiring node, then unlinks and
frees it. Foreign borrowed mappings survive; no writes follow FreeMem. Pair this
release with registered publication, before releasing the CMDB owner. This is
not complete driver registration, synchronization, or graphics Expunge.

444 build PASS46.93s;32053 CLOSED. Focused lifecycle/independent publisher/release/
composition COMPLETE651 passed,0 failed/skipped,10s;5249 CLOSED. This includes220
new PAL/NTSC/two-address registration cases with pre/post-allocation tampering,
invalid ownership/envelopes, existing mappings, alias cleanup and yielded new
owner preservation.445 adds original-Exec lifecycle/AUTOINIT registration asserts.
445 build PASS22.40s;64525 CLOSED. Native profile/branch audit COMPLETE28 passed,
0 failed/skipped,2s. Monitor regression COMPLETE2688 passed,0 failed/skipped,
2m20s;9430 CLOSED. Strict original-Exec lifecycle/AUTOINIT COMPLETE16 passed,
0 failed/skipped,1m53s;3505 CLOSED. Final TRX outcome rows verified. Artifacts:
C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260917-445
444 evidence: C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-444/registration-lifecycle.trx
Previous full443 is still LIVE63806; retain/poll it without duplication. Its
paths/filter are in historical443 below. Latest completed full is435,14014/0/21.
445 results: C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260917-445/
TRX files: monitor-regression.trx, registration-rom.trx, profile-audit.trx.
Only63806 remains live; all444/445 handles are terminal. Native default lifecycle
portion of unit3 is verified. Next collect443 without restarting it, then implement
nonzero-ID/MNTR registration consumers (unit4/5). Existing
OpenMonitor still has the known default-family shortcut; do not claim it fixed.
CyberGraphX excluded; no CPU/chipset changes or performance gate claims.

SHA256444/445 production:
- Admission:D842A750EEB790213B741F971CF6AC9DA53DE2BC08642FF6436979B1270CF4A8
- Publisher:A3FB7621918E03C760CCCBDF73C449FFBF6BFF77B11C1BA20244FFB5CF5E7B30
- Release:3CE4857DD8BD7F0F85696313226EE2E7FC3892D474F27BFCCF1B28763901CF06
- Initializer:2552B4CDA8A87E8C28FC959122376EEF25088A3AB71B0686F2A0C9CAB532B32F
- Lifecycle tests:C896841B238071055CA0C7336DFDF799285A590925ACDAE1E381AB7D61661BA2

## Historical checkpoint — 2026-09-17 / 443

Previous goal turn: progress (660 original-ROM ID-registration observations,
pinned results and separate implementation goal). No prior live test handles.
443 implements CMDB version3 construction in native and host paths. Header,
mode table and both12-byte current/original position records retain their offsets.
A12-byte trailer adds default-family ULONG, NTSC Mspc ULONG and PAL Mspc ULONG;
both pointers beginzero (absent registration), independent of CMMO ownership.
Native/host profile propagation selects11000 or21000 as stable default family.
Current consumers validate the explicit v3 version/size; no size-based adoption
of CMDB2. CMDO/CMMO and all public SDK GfxBase/MonitorSpec layouts are unchanged.

Native positive-image/CMDB/MonitorSpec AUTOINIT compositions propagate PAL/NTSC
profile into the embedded CMDB image. Host publication writes the trailer within
the existing snapshot/byte-rollback transaction. Native publication still copies
the whole initialized image before publishing descriptor validity last.
Tests expand publisher coverage to both profiles, preserve points, assert empty
registration slots, reject CMDB2 release, and fail/retry partial trailer writes.
This is registration representation only: default MonitorSpec publisher/release
does NOT populate/clear registration slots yet; OpenMonitor/MNTR consumers still
need routing to them. Do not describe v3 construction as complete ID registration.

Build443 PASS28.52s;60464 CLOSED. Running immutable443 artifacts:
- Focused construction/profile/audit COMPLETE282 passed,0 failed/skipped,8s;46999 CLOSED.
- Monitor regression COMPLETE2468 passed,0 failed/skipped,2m49s;71712 CLOSED (no ROM environment).
- Strict ROM constructor/AUTOINIT/lifecycle COMPLETE36 passed,0 failed/skipped,5m4s;78508 CLOSED.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260916-443
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-443/
443 full union is now LIVE63806 (immutable443 artifacts, no ROM environment).
TRX:graphics-full-union.trx under the results directory above. Filter:
FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer
Only63806 is live; all focused/monitor/ROM handles are closed and final rows
verified. Latest completed full435 is14014/0/21 in21m49s and predates CMDB3.
Next collect63806 without duplicating it, then integrate registration publication/
removal and native/host ID/MNTR reads. The standalone CMMO publisher/release tests
deliberately use a foreign CMDO: preserve that independent ownership boundary.
Registration-aware publication must explicitly admit owned CMDO/CMDB3, recheck
after AllocMem, and never overwrite a pre-existing registration. Teardown must
clear only mappings referencing the retiring owned node before FreeMem, retaining
unrelated borrowed mappings; pairing and ABI/rollback tests are required.
Full goal incomplete; no blocker.
CyberGraphX excluded; no CPU/chipset changes.

SHA256443:
- Database:ED0EAED237FB855705AAF0B96E4B8839D718FD424A39A545F90EB0FA1D074047
- Native CMDB publisher:6C2D2496AFBB22DA51E0215A46C37E8BCE085894B4CE1EEB7DA2796BFBF32981
- Native initializer:B88D80831AB5CA127403285AAE8335AF9F8D4D4D77BE4ED5419D695B3BFA32D6
- Core:A96C556EBD81F10049DE3B1CAB37F172DA25C2F6A0067ACAA56D30BE2EC88082

## Historical checkpoint — 2026-09-16 / 442

Previous goal turn: progress (host list/name selection plus explicit named NULL
versus decline,91 focused and2436 monitor passes).435 full union now COMPLETE
14014 passed,0 failed,21 skipped,21m49s;17394 CLOSED, final TRX rows verified.
All21 skipped rows are ROM-conditioned theories; new monitor-list oracle accounts
for the extra skipped theory compared with428. No prior live test handles.
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-435/graphics-full-union.trx

Next monitor-ID registration must follow the original display-database mapping,
not infer IDs from a MonitorSpec's name, flags or MonitorList membership. SDK
Function.offs identifies private SetDisplayInfoData at-750 with registers
A0/A1/D0/D1/D2; MonitorInfo public Mspc is at16.440 adds a PAL/NTSC original-ROM
probe that changes only an existing installed family's MNTR.Mspc through that
original private function, reads back through original GetDisplayInfoData, then
observes OpenMonitor IDs while the candidate is unlinked, linked, renamed/flags
cleared and unlinked again. Every open is balanced; original database restored
before freeing the test node, and original vectors/list/default bytes verified.
441 observations: Set returns68, readback returns88; Mspc changes to the candidate
without allocation. Exact ID0 still selects DefaultMonitor; all tested nonzero
default-family/explicit IDs select the candidate through every list/name/flag
stage. This reveals the current default-family shortcut is incomplete. Registration
must be a separate mapping, not CMMO allocation ownership or MonitorList membership.
Build440 PASS43.02s;6149 CLOSED, no tests.441 includes the final cleanup/log
refinement: unlink the candidate on exceptional exit, log initial Set allocation
delta and require stable allocation only after registration. Build441 PASS20.82s;
60796 CLOSED. Strict original-ROM probe COMPLETE2 passed,0 failed/skipped,15s;
91028 CLOSED.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260916-441
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-441/monitor-id-registration-rom.trx
Full goal incomplete; no blocker. CyberGraphX remains excluded.

442 pins the observed pointer/return/allocation outcomes and expands to all22
valid keys in default/default-marker/explicit-family forms (66 IDs per stage),
plus a null-Mspc stage. Build442 PASS21.10s;68079 CLOSED. Strict probe COMPLETE2
passed,0 failed/skipped,15s;7803 CLOSED. Each profile executes330 selector checks:
with null registration every nonzero ID returns NULL, while ID0 selects the
unchanged DefaultMonitor. Every original record/list/default snapshot restores,
reference counts balance and memory is stable. All handles are now terminal.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260916-442
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-442/monitor-id-registration-rom.trx
Probe SHA256:35E013933D1F5EDAD3920DD40C647437658AB5F1AA91EA487E62E64A7698570D
The separate implementation units and gates are now in
GRAPHICS_LIBRARY_MONITOR_ID_REGISTRATION_GOAL.md. Main replacement design includes
the newly observed exact-ID-zero correction. Next implement
explicit versioned guest-memory registration/default-family state and lifecycle
integration; do not broaden native lookup using names/flags as an ID proxy.

## Historical checkpoint — 2026-09-16 / 439

Previous goal turn: progress (fallback-provenance fix, host borrowed close,
focused/monitor/ROM evidence and durable checkpoint). Continuation arrived;
goal-tool paused status is not a code blocker and must not stop this work.

435 full union now LIVE17394, using immutable435 artifacts, no ROM environment.
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-435/graphics-full-union.trx
Filter:FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer
Poll that handle; do not duplicate it. Latest completed full union remains428.

436 adds host public-list name selection in GraphicsMonitorOperations.Names.cs:
mapped MonitorList is authoritative for names, including complete absence;
case-sensitive raw-byte matching follows list order, default.monitor alias is
case-insensitive and follows DefaultMonitor instead. Names ignore the input ID.
Associated nodes increment only their count with byte-wise rollback on refused
WORD writes; no allocation/adoption/list writes. Compact unmapped-list shim and
ID-based host family registration retain their separate backend behavior.
Lookup is bounded256 nodes/64 matching name bytes as in the native admission
slice; full synchronization/unbounded behavior remain.438/439 below adds the
explicit named-absence result propagation; other standalone failure paths remain.

New58 two-profile cases cover ordered/duplicate/canonical/alias/empty names,
no default, absent/renamed names, malformed list/node pointers and metadata,
saturation, non-ASCII comparison, unterminated names, failed/partial count write
and retry. Each runs IDs0/FFFFFFFF/DEADBEEF, balanced close and full memory/
ownership checks. Build436 PASS60.82s;45709 CLOSED. Focused names/borrowed close
COMPLETE77 passed,0 failed/skipped,2s (command exited0, no handle).436 monitor
regression COMPLETE2422 passed,0 failed/skipped,2m50s;86206 CLOSED.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260916-436
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-436/
No native emitted-code changes; native433 strict20-pass evidence still applies.
Full goal incomplete. No blocker. CyberGraphX excluded, no CPU/chipset edits.

437 corrects two review findings before declaring the host name slice verified:
default.monitor alias must bypass MonitorList reads, and a readable list head
with an unreadable suffix is malformed, not a compact-shim allocation opportunity.
The alias reads DefaultMonitor directly and full names only use legacy host
registration when the list head is absent. Added10 profile cases (68 named cases
total) covering unreadable/corrupt alias lists, partial header/default and null
default alias. Build437 PASS32.99s;75684 CLOSED. Focused COMPLETE87 passed,0
failed/skipped,2s.437 monitor regression COMPLETE2432 passed,0 failed/skipped,
2m37s;50484 CLOSED. Artifacts/results use437 suffix.
Full435 remains17394.

438/439 propagates complete named absence distinctly from provider decline:
Registry.TryOpen -> GraphicsLibraryCore.TryOpenMonitor -> register adapter. A
consistent complete list miss now claims NULL; malformed/partial/bounded-out
queries preserve the original request. TailPred is checked before claiming a
complete miss. Added4 register cases, each with3 IDs and full D/A/memory checks.
The legacy family backend is factored to OpenBackend so the new TryOpen route
does not probe the compact list twice. Native overlay publication admission
remains unchanged; no allocator/provider ownership inferred from list membership.
Build438 PASS21.59s;40855 CLOSED, no tests (superseded by final backend factoring).
Build439 PASS21.43s;7346 CLOSED. Focused COMPLETE91 passed,0 failed/skipped,2s
(command exited0, no handle).439 monitor regression COMPLETE2436 passed,0
failed/skipped,2m25s;9149 CLOSED. Only full435/17394 remains live.
Artifacts/results use439 suffix. Native bytes unchanged; full435 is older host
code, so it cannot establish host439 regression coverage. No blocker; goal incomplete.

Next action: collect full435/17394 without restarting it, then continue with
native/host non-default monitor-ID registration and synchronization/long-list
admission. Native emitter remains at432 behavior, qualified by433 strict ROM
and focused tests; host named-list selection now has439 direct/register and
monitor-regression evidence. Full graphics compatibility is not complete.

SHA256 host439:
- Monitor operations:B387E0E43BD6F0981838CB43AC8B07D1B3BF996E54A6F110D53A7DA74EF9BBE5
- Registered names:EA78E7555948A8F4A33AA149B12DCAF473DA19AF156AE9C51835121E7D43C199
- Core:6F953A17394D3F83446F770DCF0FAB8C757F15F3E18A0015B30A8E21431F9652
- Register adapter:6CA8CD7E874DC9BF3BF2C17CAC839FA1C9B587AF5B7B74EAC44F2206FA5F853B

## Historical checkpoint — 2026-09-16 / 435

433 native list selection/associated close is verified: focused640 passed,0
failed/skipped,1m30s (67780 CLOSED); strict original/native ROM plus AUTOINIT and
lifecycle20 passed,0 failed/skipped,2m42s (38167 CLOSED). Build21.54s,98555 CLOSED.
433 monitor regression COMPLETE2345 passed,0 failed/skipped,2m28s;22723 CLOSED.
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260916-433
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-433/

432 finished369 passed/271 failed focused,2077 passed/268 failed monitor;
94201/61487 CLOSED. All268 behavioral failures were fallback provenance only:
the range linker emits local RTS copies, which the canonical-PC-only fixture
missed.433 records those offsets in construction-only branch audit metadata;
both open/close fixtures now observe executed fallback PCs, never D0 equality.
No native instruction change. Structural checks verify each clone is an aligned
RTS and a patched branch target. Three measured tuples updated:
PAL1444670/NTSC1444570 bytes,71798 branches,3428 relays,7 chains,3346 boundaries.

434 adds the host core/compatibility borrowed-close counterpart: full pointer/base envelopes and
type/kind/backlink admit non-owned associated nodes without list reads, adoption,
allocation or freeing. Existing byte-wise rollback protects a rejected/partial
count WORD decrement. New17 tests cover counts1/256/65535, invalid metadata and
pointers, unreadable fields, rejected/partial writes and retry, plus poison list
pointers and unchanged ownership. Profile clone audit now uses a precomputed
target set (same assertions without repeated scans). Build434 PASS20.91s;73220
CLOSED. Focused host/profile45 passed,0 failed/skipped,2s;20565 CLOSED. Monitor
regression2362 passed,0 failed/skipped,2m28s;83249 CLOSED. Native emitted bytes
unchanged since432; strict433 remains applicable.
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-434/

435 adds two register-boundary cases: ordinary host adapter balances the borrowed
reference and preserves all registers except D0; native-overlay adapter still
declines untouched so the native resident/provider implementation handles it.
This distinction is intentional: association permits reference balancing but
does NOT imply overlay/allocator ownership. No production changes since434.
Build435 PASS20.96s;54671 CLOSED. Latest host/profile COMPLETE47 passed,0
failed/skipped,2s; command exited0 (no live handle). Artifacts/results use the
same path prefix with435 suffix. All tests launched in this turn are terminal.
The latest full union remains428 (13898 passed/0 failed/20 skipped); no435 full
union has been launched. Do not confuse focused/monitor passes with a full run.

Next full union and host registered-name/list-order
selection, explicit non-default ID registration and task synchronization.
Keep borrowed nodes outside _resident ownership. Full goal incomplete; no code
blocker. Goal tool reports paused; status is product-controlled and cannot be
resumed with update_goal. User's resume request is being implemented this turn.
Do not mark full graphics complete. CyberGraphX excluded; no CPU/chipset edits.

SHA256 at435:
- Host monitor operations:D518D9468DC08D19E5384F4754C9B9B27CDA3A6A6FB1AD771DE213D4F52F70E6
- Main emitter:3D143DB6E0C52001F9FA8D2D270F4BF512AAF9BF99143E1137019B0AC5AABE5E
- Branch linker:6DFBB9A3433D1D0D9995D1CCF8AB1054B5FA23540EA424D2D547C46685931CE2

## Historical checkpoint — 2026-09-16 / 432

429 original-ROM list probe COMPLETE2 passed,0 failed/skipped,18s;91096 CLOSED,
build40.67s. It confirms first exact list-name match (including empty name),
canonical duplicates before the default win, case-insensitive default.monitor
alias overrides list collisions, and CloseMonitor balances an unlinked held
reference without freeing it. Original Exec list edits use MonitorList semaphore.

430/431 implements native list-name selection and associated-node close:
- Names are selected independently of DefaultMonitor; the alias retains its
  separate default-pointer path. Bounded256-node/64-byte search checks GfxBase,
  list header, predecessor links, full MonitorSpec, type/kind/backlink and strings.
- A consistent exhausted list returns NULL (not provider decline); malformed,
  unterminated or over-limit searches preserve requests and tail-chain. Names
  retain precedence over IDs. No allocation/list mutation or ownership adoption.
- Close recognizes full library-associated MonitorSpecs even after unlinking,
  decrements nonzero counts without list edits/free, retains legacy default and
  provider admission for other envelopes. No extra close stack frame. Name
  traversal uses28 bytes; refcount frame44 bytes is separate, not nested.
- Native list traversal still requires serialized list/lifetime mutation; task
  semaphore integration and unbounded/full provider handling remain full-goal work.

New112 fixed/HUNK and vector/AUTOINIT list cases include order/duplicates/aliases,
empty name/no default/absent NULL, malformed lists/cycles/headers, saturation,
last admitted node and bounded-out257th node. Four close routes each cover eight
associated/unlinked/invalid cases. Original-ROM fixture now also runs both native
profiles against the same registered-node matrix. Initial build430 PASS24.74s;
55662 CLOSED.431 build PASS22.43s;68282 CLOSED.431 focused COMPLETE629 passed,
7 failed,1m19s;7576 CLOSED. Three measured fingerprints need updating; four fixture
cases inferred fallback from D0 equaling the input ID (bothzero on a valid NULL
result).432 uses executed fallback PC only, and tests complete absence at exactly
256 nodes; budget is checked before a257th non-sentinel dereference, allowing the
tail check after256 nodes. This is a6-byte/one-branch production guard adjustment.
Build432 PASS23.95s;5652 CLOSED. Strict431 COMPLETE20 passed,0 failed/skipped,2m35s;
42760 CLOSED (predates only budget adjustment; original/native list matrix and
AUTOINIT/lifecycle). TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-431/monitor-list-rom.trx
432 focused/admission and monitor regression started; collect current turn handles.
Results:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260916-432/
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260916-432
Next collect tests, update measured fingerprints, then strict latest list/broad
regression and host registered-name/borrowed-close parity. Explicit non-default ID
registration and task-level synchronization also remain; no ownership adoption.
Current full428 union has passed; no older live run. Full goal active,
incomplete; no blocker. Preserve CyberGraphX exclusion and no CPU/chipset edits.

## Historical checkpoint — 2026-09-15 / 429

Previous turn: progress (native CMMO2/explicit-ID registration plus901 focused,
20 ROM and2229 monitor passes).428 full union now COMPLETE13898 passed,0 failed,
20 skipped,17m35s;86136 CLOSED. All previous runs terminal; do not restart them.
TRX:C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-428/graphics-full-union.trx

429 adds an original-ROM monitor-list oracle, PAL/NTSC: two explicitly allocated
MonitorSpec nodes are registered/reordered through original Exec list functions
under the original MonitorList semaphore. Probe duplicate-name first-match order,
case sensitivity, absent names, duplicate canonical name before the default,
case-folded default alias collisions, empty registered name, and closing a held
reference after unlink. Check refcounts/list bytes/AvailMem and restore/free only
the test nodes; original ROM graphics and Exec vectors verified before/after.
Expectations are hypotheses until the first probe completes. No new production
list-selection code yet. Build429 started; collect current turn handle.
Next pin observed contract, then implement native list-based names and matching
CloseMonitor behavior together. Do not infer allocation ownership from membership.
Full goal active/incomplete, no blocker; CyberGraphX remains excluded.

## Historical checkpoint — 2026-09-15 / 428

426 original-ROM expanded matrix COMPLETE2 passed,0 failed/skipped,26s;11369
CLOSED; build19.91s.294 requests/profile/pass, two passes, plus public name/flags
mutation controls.425 hypothesis failed only explicit IDs missing bit12 (44
mismatches/profile across two passes); no list/refcount failures. All22 keys,
including SuperHires on OCS, can open the installed matching monitor. Missing
bit12 on explicit PAL/NTSC IDs is rejected. Default high-word-zero accepts keys
with/without bit12. Renaming the resident or clearing its flags does not change
ID selection. No extra second-pass allocation; first opposite-family probe still
consumes5536 bytes. Expanded oracle source and TRX are authoritative.

427 implementation now in verification (build PASS19.76s;2873 CLOSED):
- CMMO version2 appends registered family ULONG at278; required extent27C. Public
  GfxBase, MonitorSpec and separate CMDO/CMDB layouts unchanged. Publisher checks
  inert ID before/after AllocMem, publishes ID before valid tag; release validates
  exact ID and clears it before FreeMem. Version1 records decline, not adopted.
- Native OpenMonitor validates all22 mode keys independently of chipset, accepts
  all default key/bit12 combinations, and admits explicit PAL/NTSC only against
  CMMO2 owner/registered-ID/allocation/size/backlink/positive-envelope admission.
  Mutable names/flags are not ID identity. D0-D1/A0 use a separate12-byte frame;
  existing maximum44-byte refcount frame unchanged. Unknown/opposite/non-default
  selection still provider-declines; standalone failure semantics remain work.
- Host selector shares syntax validation; removes incorrect ModeNotAvailable
  chipset rejection. Registry-supported opposite-family allocation behavior is
  unchanged and is NOT evidence of original-ROM driver residency.
- New native120 route/profile/registration cases each loop22 keys and negative
  opposite/missing-marker controls; success additionally checks default aliases.
  New host matrix, CMMO2 publisher/release/legacy controls and actual-ROM AUTOINIT
  opens/closes for66 IDs/profile added. Only admitted OpenCount may change.

427 focused COMPLETE882 passed/3 failed/0 skipped,56s;36105 CLOSED. Only failures
were measured fingerprint expectations:PAL1444386/NTSC1444286,71770 branches,
3428 relays,7 chains,3346 boundaries; structural branch checks passed.428 updates
those tests and adds private-base/owned-allocation wrap controls (16 cases);
no production changes since427. Build428 PASS20.34s;27837 CLOSED.

427 strict original-ROM selector/AUTOINIT/lifecycle COMPLETE20 passed,0 failed/skipped,
2m30s;23203 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-427/monitor-registration-rom.trx
424 full union COMPLETE13754 passed,0 failed,20 skipped,17m58s;62260 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-424/graphics-full-union.trx
Final strict20 and broad13774 result rows verified.424 predates registration427.

428 focused admission/lifecycle/profile/branch/host matrix COMPLETE901 passed,
0 failed/skipped,1m20s;54899 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-428/monitor-registration-admission.trx

428 monitor regression COMPLETE2229 passed,0 failed/skipped,2m32s;89227 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-428/monitor-regression.trx
Focused901 final TRX counters/result rows independently verified.

LIVE verification, collect without restarting:
- 428 unchanged full graphics union86136 (no ROM environment):
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-428/graphics-full-union.trx
  Filter:FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer
Artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260915-428
Full goal active/incomplete; no blocker. Previous turn: progress (CMMO2/native
explicit-family selection, host chipset-gating correction,901 focused/20 ROM/2229
monitor passes). Next collect86136 while progressing non-default list selection:
first pin original-ROM OpenMonitor(name)/CloseMonitor against an explicitly
registered second MonitorSpec, including duplicate/unmatched names and list order;
then co-change native name traversal and CloseMonitor admission. Registration
must not imply ownership or freeing driver allocations. Explicit IDs of further
residents need a corresponding ID-registration path, not inference from names.
Current sources/hashes in selection
CURRENT. G6/G7 performance gates are not implicated by these correctness tests.

## Historical checkpoint — 2026-09-15 / 425

Previous goal turn was progress.424 monitor regression COMPLETE2085 passed,0
failed/skipped,2m51s;10920 CLOSED.424 full union62260 revalidated LIVE, no failure
output; collect without restart (path in historical424 below).

Before broadening explicit family selection, extend the original-ROM ID matrix:
all22 literal OCS/ECS mode keys plus12 unsupported/control probes, four families
0/NTSC/PAL/VGA, bit12 present/absent, both boot profiles, two passes. Preserve
original vector provenance, list/refcount balance and second-pass memory checks.
Initial expectation is a hypothesis (native keys plus resident family), with
all mismatches accumulated after both passes; inspect TRX before implementing.
No replacement selector production changed after423 yet. Build425 started.
Do not equate mode chipset support or a canonical name with ID registration.
Full goal active/incomplete, no blocker. CyberGraphX excluded.

## Historical checkpoint — 2026-09-15 / 424

Continuing native selector implementation: admit the original-ROM-proven null-name
default-family IDs 4/8000/1000 as well as zero. Explicit monitor families and other
mode combinations still decline; no inference of driver availability. Adds success,
malformed-base/monitor, saturation and name-precedence coverage across fixed/HUNK
and public/AUTOINIT routes. Real-ROM native composition now opens/closes all four
IDs on PAL/NTSC and checks no allocation changes. Build423 PASS18.84s;77798 CLOSED.
423 focused COMPLETE303 passed/5 failed/0 skipped,34s;60592 CLOSED. Three failures
were stale size/branch fingerprints (actual PAL1443954/NTSC1443854,71729 branches,
3428 relays,7 chains,3346 boundaries; all structural audits passed). Two new test
cases attempted public-vector lookup through a deliberately invalid A6; corrected
to direct-body admission, keeping independent AUTOINIT calls. Explicitly seed
empty-name and resident-name bytes in that fixture.424 updates these tests only;
production unchanged after423. Build424 PASS24.11s;36091 CLOSED. Focused admission,
all profile tests and branch audit COMPLETE308 passed,0 failed/skipped,36s;2858
CLOSED. Full goal active, incomplete, no blocker.

424 strict original-ROM selector/native AUTOINIT COMPLETE14 passed,0 failed/skipped,
1m43s;59115 CLOSED. Focused308 and strict14 final TRX rows verified:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-424/native-ids-rom.trx

LIVE424 verification (collect without restart):
- Monitor regression10920:
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-424/monitor-regression.trx
- Full unchanged graphics union62260 (no ROM environment):
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-424/graphics-full-union.trx
  Filter:FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer
Build artifacts:C:/Users/vsys-admin/AppData/Local/Temp/codex-graphics-monitor-selection-20260915-424
Focused TRX:same results directory/native-ids-admission.trx.
Previous turn classification: progress (native ID aliases plus308 focused and14
real-ROM passes). No permission/input needed. Next collect verification and continue
explicit-family/default-resident selection;
retain actual availability and provider decline, not build-profile assumptions.
Non-default residents require co-changing CloseMonitor. Do not finish full goal.

All prior live handles CLOSED, final TRX rows independently verified:
- 422 strict ROM46062:20 passed,0 failed/skipped,2m16s.
- 422 monitor71010:2069 passed,0 failed/skipped,2m14s.
- 419 full union2199:13566 passed,0 failed,20 skipped,20m40s.
The419 snapshot predates420 direct-wrapper and421 name work. Do not restart old
sessions. Source and evidence paths follow in historical422 and selection unit.

## Historical checkpoint — 2026-09-15 / 422

Native already-resident default name admission implemented. A supplied name
precedes the ID; default.monitor is case-insensitive, actual resident name is
case-sensitive. No monitor allocation/list mutation. A separate24-byte frame
preserves request registers and A0/D1 before matching or fallback; it does not
nest with the existing refcount frame. Per-byte wrap checks, odd name pointers,
63-byte names plus NUL and unterminated-name refusal are covered. Longer names,
non-default residents and explicit nonzero IDs remain required later work.

421 build PASS21.88s; first admission suite COMPLETE265 passed,3 failed,35s;
91248 CLOSED. The three failures are only measured stale fingerprint assertions:
PAL1443916/NTSC1443816 bytes,71725 branches,3428 relays,7 chains,3346 boundaries.
Every structural branch-range/target check passed.422 updates those expectations
and extends original-ROM/real-AUTOINIT tests with mixed-case aliases and named
opens using conflicting IDs.422 build PASS19.14s;53578 CLOSED.

422 admission/profile/branch audit COMPLETE268 passed,0 failed/skipped,34s;
16666 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-422/native-names-admission.trx

LIVE422 qualification, collect without restart:
- Strict original-ROM/named AUTOINIT/lifecycle46062 (20 expected):
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-422/native-names-rom.trx
- Monitor regression71010 (FullyQualifiedName~Monitor&FullyQualifiedName!~KickstartRom):
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-422/monitor-regression.trx

419 full union2199 remains LIVE, no failure output; snapshot predates named
admission and420 direct-wrapper fix:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-419/graphics-full-union.trx
Do not restart it. Previous goal turn: progress (native zero/INVALID_ID and
direct-wrapper corrections,100 admission/40 ROM/1901 monitor passes). Full
replacement remains incomplete; no blocker. Details in selection unit CURRENT.

## Historical checkpoint — 2026-09-15 / 420

Native null-name/zero and INVALID_ID semantics corrected.419 focused native
admission/profile/branch audit COMPLETE100 passed,0 failed/skipped,16s;21570
CLOSED; build419 PASS32.72s,55557 CLOSED. The original414 full union is now
COMPLETE13556/0/19,20m19s;54638 CLOSED (predates selector corrections).

Source review found a related direct-host boundary: GraphicsServices.OpenMonitor
ignored adapter decline and returned the unchanged input ID.420 now converts
decline to NULL only in the direct-call wrapper; adapter/overlay decline still
preserves the request for fallback. Four direct failure tests plus adapter
preservation assertions added. No additional native raster changes after419.
Build420 PASS45.38s;99249 CLOSED. Monitor regression420 COMPLETE1901 passed,
0 failed/skipped,2m43s;85737 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-420/monitor-regression.trx
Filter FullyQualifiedName~Monitor&FullyQualifiedName!~KickstartRom; no ROM env.
Final TRX counters for admission100, strict40 and monitor1901 verified.

419 strict original-ROM selector/AUTOINIT/lifecycle regression COMPLETE40 passed,
0 failed/skipped,6m20s;40917 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-419/native-selector-rom.trx

LIVE419 verification, collect without restart:
- Full unchanged graphics union2199 (no ROM environment):
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-419/graphics-full-union.trx
The419 snapshot includes native/default selector fixes but predates420 direct-wrapper
fix. No failure output so far. Full graphics remains incomplete; next extend
native names/explicit-ID selection and availability from the pinned ROM matrix.
CyberGraphX and CPU/chipset behavior remain out of scope for these edits.
Only2199 remains live. Previous turn classification: progress (native selector
and direct wrapper changed, behavioral/ROM evidence collected). No user input
needed; continue selection work after collecting the live regression.

## Historical checkpoint — 2026-09-15 / 419

Native OpenMonitor default-selector correction implemented: null name/zero
selects the resident default; null name/INVALID_ID returns0 without reading
GfxBase or changing references. Named requests retain name precedence and
fallback; explicit nonzero IDs/names remain required next-unit expansion.
Existing native reference-count/envelope admission is retained. Previous turn
classification: progress (host correction plus1889 regression/four ROM passes).

414 broad union COMPLETE13556 passed,0 failed,19 skipped,13575 total,20m19s;
54638 CLOSED. This snapshot predates both host/native selector corrections.
418 build PASS49.59s. First native admission/audit run COMPLETE93 passed,3 failed,
12s;61428 CLOSED. All three failures are measured stale fingerprint expectations:
PAL1443496/NTSC1443396 bytes,71685 branches; unchanged3428 relays/7 chains/3346
boundaries. Structural branch-range checks passed.419 updates the expectations
and adds four malformed-base INVALID_ID controls (each checks four base values).
Build419 session55557 is LIVE. Collect it, then run focused admission/profile
tests and strict original-Exec integration. Older41861960/4173757 are CLOSED.

Source changes: NativeGraphicsRasterBodies.cs, OpenMonitor admission tests,
OpenMonitor-specific scaffold expectations, measured profile fingerprints and
native ROM fixture setup opens. No global INVALID_ID replacement; no CPU/chipset
changes. Whole graphics replacement remains incomplete; no blocker exists.

## Historical checkpoint — 2026-09-15 / 417

Host monitor selector correction implemented after original-ROM discovery:
INVALID_ID no longer means default; canonical PAL/NTSC names are case-sensitive.
Zero/default alias behavior is preserved. Native raster selector correction is
still required next. Detailed contract, exact matrix and source hashes are in
GRAPHICS_LIBRARY_NATIVE_MONITOR_SELECTION_GOAL.md CURRENT417.

416 ROM matrix COMPLETE2 passed,0 failed/skipped,16s;61214 CLOSED. Its repeated
pass confirms no additional allocation after the5536-byte first opposite-family
lookup.417 build PASS20.34s; strict ROM selector/semaphore suite COMPLETE4 passed,
0 failed/skipped,30s;72916 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-417/monitor-host-rom.trx

417 monitor regression COMPLETE1889 passed,0 failed/skipped,2m19s;27321 CLOSED.
Filter FullyQualifiedName~Monitor&FullyQualifiedName!~KickstartRom:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-417/monitor-host-regression.trx

LIVE, collect without restarting:
- 414 broad graphics union54638 (snapshot before selector correction):
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-414/graphics-full-union.trx
No failure output from54638 so far. Older68521/61501 are CLOSED; all other
historical live-session entries are superseded. Full replacement remains
incomplete; no blocker. This turn changes implementation and evidence, not just
status. Continue native selector correction; collect54638 without restarting.

## Historical checkpoint — 2026-09-15 / 416

Selector oracle415 exposed a required correction: original ROM returns NULL for
OpenMonitor(NULL,INVALID_ID) and default for ID0, contrary to the replacement's
native arm and host INVALID_ID alias. Canonical PAL/NTSC names are case-sensitive
in this boot ROM; default.monitor is separately case-insensitive. The exact
matrix and necessary native/host/stale-test corrections are in the selection
unit. Do not retain known wrong behavior merely to keep earlier tests green.

415 strict session68521 CLOSED2 failed: both completed32 selectors and failed
only the final assumption that memory would not change (5536 bytes in each).
416 adds pinned selector outcomes and a second pass with per-request memory
telemetry to distinguish one-time allocation from repeated growth. Build61501
LIVE, collect then run the strict selector filter.414 broad union54638 remains
LIVE, no failure output so far; exact paths/filter in historical415 below.
No production raster/selector changes yet. Previous414 passes remain lifecycle
evidence, not proof of original-ROM INVALID_ID semantics. No blocker exists.

## Historical checkpoint — 2026-09-15 / 415

Previous goal turn made progress: native AUTOINIT composition implemented and
qualified499 focused /38 strict-ROM cases. This continuation starts the next
selector unit, GRAPHICS_LIBRARY_NATIVE_MONITOR_SELECTION_GOAL.md. No production
raster body changes yet; first establish original-ROM selector evidence.

LIVE runs, collect without restart:
- 414 broad graphics union session54638, using immutable414 artifacts and the
  unchanged NativeGraphics|GraphicsLibraryPortableScaffoldTests|DisplayInfoData|
  DBufInfo|DoubleBuffer filter, no ROM environment. No failure output so far.
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-414/graphics-full-union.trx
- 415 strict selector probe session68521, PAL/NTSC,32 requests each; actual
  OpenMonitor/CloseMonitor and Exec memory vectors checked against original ROM.
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-selection-20260915-415/monitor-selection-rom.trx

415 build PASS63.71s; added direct selector-vector provenance checks before
qualification, incremental build PASS14.86s. No implementation blocker. Follow
the selection unit contract: collect observations, pin explicit expected matrix,
then expand native admission. Keep full replacement incomplete and CyberGraphX
excluded. The app goal metadata still reports paused despite this continuation
event; this does not block authorized implementation and is not proof of a code
stall. Do not repeatedly ask for resume while continuation work is arriving.

## Historical checkpoint — 2026-09-14 / 414

Native default MonitorSpec AUTOINIT composition is implemented. Explicit
BuildWithNativeMonitorSpec initializes both descriptors, publishes CMDB then
MonitorSpec, and unwinds the owned CMDB on monitor failure. AUTOINIT frees the
actual Exec library extent last; direct callers retain it by default. Damaged
CMDB ownership that prevents release retains the library for recovery. This is
a serialized private-allocation boot constructor, not concurrent initialization.
HUNK builders expose publishNativeMonitorSpec only with explicit initialization,
CMDO, CMMO and database publication options; default/CMDB-only paths stay intact.

413 first build caught a test constant typo; corrected retry PASS9.25s,155
composition/initializer cases pass,7s.414 build PASS19.40s; complete focused
lifecycle/composition499 passed,0 failed/skipped,8s;39587 CLOSED.
Strict414 original-ROM suite COMPLETE:38 passed,0 failed/skipped,4m10s;
22034 CLOSED. No test sessions remain live. Final TRX counters verified:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-414/monitor-composition-rom.trx
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-414/monitor-composition-lifecycle.trx
New12 strict cases cover PAL/NTSC fixed/HUNK success plus injected first/second
AllocMem failures with exact AvailMem recovery and failed-library nonpublication.
Original8 Exec vectors pass before/after provenance checks.
Latest completed broad snapshot remains410 (13241/0/18), predating teardown and
composition. A current full union remains to be run after strict qualification.

Whole replacement is incomplete, with no implementation blocker. Goal metadata
reports PAUSED. A prose request starts normal work but has not changed that
app-owned status. Use `/goal resume` to resume the persistent goal; do not create
a duplicate goal or edit stored app state. Official guidance:
https://learn.chatgpt.com/use-cases/follow-goals
Next run current broad regression and continue the next
bounded monitor-selection/Intuition lifecycle unit. CyberGraphX remains excluded.

## Historical checkpoint — 2026-09-14 / 412

Native owned MonitorSpec teardown is implemented and qualified. Build412 PASS
22.15s; focused lifecycle400 passed,0 failed/skipped,5s; strict original-ROM26
passed,0 failed/skipped,2m57s. Sessions66880 and1111 CLOSED. Strict cases prove
live-reference refusal, release/recreation and exact monitor/CMDB memory recovery.
Broad410 is COMPLETE:13241 passed,0 failed,18 skipped,13259 total,19m37s;
10313 CLOSED. No test sessions remain live. This broad snapshot predates teardown.
Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-412/monitor-lifecycle.trx
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-412/native-monitor-release-rom.trx
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-410/graphics-full-union.trx

Next: compose native positive initialization, CMDB publication and MonitorSpec
publication with failure unwind. Keep direct-caller versus AUTOINIT allocation
ownership explicit. Whole replacement is incomplete; no implementation blocker.
The app goal metadata currently reports PAUSED; ordinary user-requested work is
continuing, but tools cannot change that scheduler-controlled status.

## Historical checkpoint — 2026-09-14 / 410

Native MonitorSpec ownership publisher, independent CMMO descriptor and builder
opt-in propagation are implemented.409 passes145 focused tests;410 passes184.
Build410 PASS22.39s. Detailed source identity/next work lives in the MonitorSpec
unit CURRENT section. Strict410 ROM integration COMPLETE26 passed,0 failed/
skipped,3m34s;84403 CLOSED. Current broad410 union LIVE10313; no failure output:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-410/native-monitor-owner-rom.trx
C:/Users/vsys-admin/AppData/Local/Temp/graphics-monitor-owner-20260914-410/graphics-full-union.trx
Collect10313, do not restart or poll closed84403. Next is native owned-monitor teardown, then full
initialization failure unwind. Whole replacement stays active/incomplete.

## Historical checkpoint — 2026-09-14 / 409

402 and403 broad runs are COMPLETE; both final TRX files were read after their
handles disappeared. Each has13108 passed,0 failed,16 NotExecuted/skipped,
13124 total. TRX counters incorrectly report notExecuted=0; actual outcome
groups contain16 NotExecuted. Elapsed wall time402=23m53s,403=22m29s.
81414 and69651 CLOSED; do not restart/poll them.403 is the latest completed
clean broad snapshot, but predates405+ MonitorSpec changes.

409 implements the independent explicit CMMO descriptor (264..277, image278),
native MonitorSpec allocation/list/default-pointer publication, and focused
layout/admission/rollback/ABI/order tests. Initial build caught a test-only
WriteByte arity error; corrected before qualification. Follow the native
MonitorSpec unit CURRENT section for exact next steps. No goal blocker exists.

## Historical checkpoint — 2026-09-13 / 408

Active implementation has advanced to GRAPHICS_LIBRARY_NATIVE_MONITORSPEC_GOAL.md
CURRENT section: corrected a ROM-proven semaphore queue-count mismatch, added
a shared MonitorSpec template and native private initializer.241 focused tests
and2 strict template/ROM cases pass.408 adds recursive original-Exec lock/release
verification; build PASS27.57s, strict test COMPLETE2 passed,0 failed/skipped,
18s;9446 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-408/native-monitor-lock-rom.trx
The separate unit contains source hashes, exact boundaries and the next native
ownership descriptor/publication implementation. No blocker or user decision.
Current408 focused monitor/initializer regression also passes241/0/0,14s;
74750 CLOSED,408/monitor-template.trx. Next implement the independent monitor
ownership descriptor and native list/default-pointer publication (unit steps3/4).

402 broad union81414 and403 broad union69651 both remain LIVE, confirmed again
this turn with no new failure output. Their exact paths/filter are preserved in
the historical403 section below. Do not restart them. Their source predates the
405 semaphore correction /406 shared template, so collect them and qualify a
newer broad snapshot too. Full replacement remains active/incomplete.

## Historical checkpoint — 2026-09-13 / 403

Native CMDB allocation/publication, release/recreate, and explicit AUTOINIT
composition are implemented and focused/strict-ROM qualified. Whole replacement
is still active/incomplete. No blocker or user decision is needed.

403 build PASS25.70s. Focused initializer/publisher/release/composition tests:
123 passed,0 failed/skipped,4s;13730 CLOSED. Strict original-ROM integration:
20 passed,0 failed/skipped,3m7s;71340 CLOSED. Final TRX counters independently read.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-403/autoinit-cleanup.trx
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-403/autoinit-cleanup-rom.trx

The402 OOM probe caught a real contract error: all4 failure controls returned
zero but leaked1672 bytes (AvailMem994072 ->992400);16 other cases passed.
6998 CLOSED. Original Exec does NOT free a failed initializer's library base.
The [InitResident contract](https://developer.amigaos3.net/autodocs/exec.library/InitResident.html)
requires manual cleanup.403 adds explicit releaseLibraryOnFailure composition:
HUNK AUTOINIT opts in; fixed AUTOINIT tests opt in. Direct callers default to
retaining their allocation. After successful positive-image admission, failed
CMDB publication frees GfxBase-lib_NegSize for actual NegSize+PosSize through
native FreeMem. Invalid incoming envelopes are never blindly freed. The wrapper
preserves D2-D7/A2-A6; maximum nested scratch through AllocMem is28 bytes.
All4 corrected ROM OOM cases restore the exact original free-memory total and
leave the failed library unlinked. Original8 Exec vectors remain unchanged.

### Live runs — collect, do not restart

- 402 broad graphics union: session81414 LIVE, no failure output so far. Its
  known ROM OOM defect is superseded by403; this older snapshot remains useful
  for detecting unrelated regressions, not proving corrected OOM behavior.
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-402/graphics-full-union.trx
- 403 broad graphics union: session69651 LIVE, no failure output so far. This
  is the current corrected snapshot that must be collected for qualification.
  C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-403/graphics-full-union.trx

Both use the unchanged union filter:
FullyQualifiedName~NativeGraphics|FullyQualifiedName~GraphicsLibraryPortableScaffoldTests|FullyQualifiedName~DisplayInfoData|FullyQualifiedName~DBufInfo|FullyQualifiedName~DoubleBuffer
ROM environment is set only in strict-test processes, not these broad runs.
If a session handle disappears, read its final TRX before considering a rerun.
These are correctness tests, not formal throughput gates.391 is the last completed
clean broad snapshot:12997 passed,0 failed,15 optional skips,21m32s;94441 CLOSED.

### Current source identity / next action

- Initializer SHA256:67402E5F6447BC31ED3B198D25BEBE878E835D61DF99476536039638EDEEA671
- HunkBuilder:0444FE5022D40324316143013C7015AD75A1A677EC84D6B89E054A667EC01C4A
- Composition tests:D210091196B01F8DEC8CF421503F51141B2B8B900D75FE90B17557868C89C67F
- ROM tests:C630A9DA9D8A338C0C8AEFA233F5BEBCD868819CB41316C704DB2DA7CB997952
- Publisher/release hashes remain as recorded in402 below. Public raster reader
  bytes and the existing default/inert builder behavior are unchanged.

1. Collect81414/69651; inspect final failures/counters and update this section.
2. Continue GRAPHICS_LIBRARY_NATIVE_MONITORSPEC_GOAL.md without waiting for a
   user resume: first qualify the embedded semaphore/list boot envelope against
   original Exec/ROM, then construct a native default resident and ownership.
3. Keep general monitor selection, driver callbacks, Intuition mutation and full
   library/screen/view lifecycle visible as incomplete. The successful CMDB unit
   still uses a separate MonitorSpec pointer fixture; do not overstate it.

Top-level design/goal documents now route here instead of duplicating stale
live-run claims. Keep this CURRENT section authoritative across context rollover.
Repowise tools/CLI were unavailable; source review and focused tests were used.
No CPU implementation or chipset behavior was changed. Stable dependency/analyzer
warnings remain NU1902 (Microsoft.Build.Tasks.Git) and xUnit2013 (unrelated test).

## Historical checkpoint — 2026-09-13 / 402

398 native publication / original-Exec integration:12 passed,0 failed/skipped,
1m34s; build PASS22.86s;24441 CLOSED.399 native release adds exact descriptor,
database header and canonical-ID validation. It invalidates first, clears all
published ownership before native FreeMem, and writes nothing afterward.
Its success returns the base, decline returns zero; current/original state is
destroyed, unlike host capture/rebind. Both routines require lifecycle-owner
serialization and valid Exec allocation semantics; allocator callback safety
is NOT proof of arbitrary concurrent-task synchronization.
399 publisher/release tests:72 passed,0 failed/skipped,741ms;build PASS24.88s.
400 original-Exec native publish/release/recreate:12 passed,0 failed/skipped,
1m40s;build PASS24.99s;82030 CLOSED. No host CMDB cleanup remains in these cases.

401 adds explicit BuildWithNativeMonitorState and HUNK options
publishNativeMonitorDatabase/supportsEcsDisplay. Publication requires both
initializeAllocatedImage and includeNativeRuntimeDescriptor. Default construction
is unchanged. The composed initializer preserves the library allocation for its
caller when returning zero; it owns only its CMDB candidate. 123 focused tests
pass,0 failed/skipped,4s;build PASS21.97s;44303 CLOSED. Covers fixed, synthetic
HUNK and raster-HUNK, both profiles/chipsets, OOM and short-envelope rejection.

402 extends the strict ROM oracle to20 cases: combined InitResident publication
and four injected CMDB-OOM controls. The fault injection replaces only the emitted
publisher allocation instruction; native Exec vectors remain original. These
controls must prove original Exec recovers the caller-owned library allocation
(AvailMem before/after), returns zero and does not link a failed library. Build
and ROM qualification pending. Collect build15945, then run the strict oracle.

Evidence directory prefix:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-
398/native-publisher-rom.trx;399/native-lifecycle.trx;
400/native-lifecycle-rom.trx;401/combined-init.trx.

SHA256 source identity at402:
- Publisher:77CACE5136CC87F0D23CD198A9251982D2F73D7EE923A388705AB2A7731B9BEA
- Release:C81F97970EE5D6868C656F5DD36814303A5EC78B8E8776C4EE24F94F44BC0D07
- Initializer:7F157D89E52681657CF9773A1E9DF80AC0694465856FD26E9FC14726363CE7B5
- HunkBuilder:EFFB624A8A7D614039E929955E5E19E19A69292A310CD7B3FAF1798228FAA228
- ROM test:9341575A4B9FA35500B445B6CE4677F15FDD3BBB1C20897EEADD5B459A1CD21A

Next: collect402, investigate failures without changing original Exec/CPU, run
the unchanged broad graphics union on402. Then address native MonitorSpec
construction/ownership and Intuition mutation through separate units. The CMDB
initializer alone does not provide OpenMonitor/default MonitorSpec or a full
screen lifecycle. No blocker or new user authority is required.

## Historical checkpoint — 2026-09-13 / 398

396 adds a deterministic CMDB version-2 boot image. Both OCS and ECS images
match the existing host allocation byte for byte (2 tests passed, 242ms).
397 adds NativeGraphicsMonitorStatePublisher.Build: position-independent 68000
code with D0=initialized GfxBase, A6=valid ExecBase; returns the base on success
or zero on decline/failure, preserving D2-D7/A2-A6. It accepts only the fresh
inert opt-in descriptor, never adopts or replaces an existing provider.
It calls AllocMem for its private image, checks alignment/extent, rechecks the
claim after allocation, copies the entire database, and publishes CMDO last.
Failed candidate admission frees only that candidate; foreign state is untouched.

397 focused tests: 32 passed, 0 failed/skipped, 716ms. Includes both chipsets,
allocation failure, invalid returned addresses, changed claims during allocation,
register/stack preservation, guards, and validity-last ordering. The build output
handle was lost at context rollover; the emitted 397 test assembly was present
and executed successfully. Do not invent a build duration/exit transcript.
Evidence:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-397/publisher.trx

398 adds four original-Exec publication cases to the existing strict resident
initialization oracle (PAL/NTSC, fixed/relocated). CMDB allocation/publication
executes native code without GraphicsLibraryCore. The test supplies a separate
MonitorSpec pointer fixture, checks native MNTR current/original reads and guest
current changes, duplicate-constructor refusal, and template isolation. Cleanup
is currently test-only, NOT native teardown. Build/test results pending.

The prior full graphics union 391 is COMPLETE: 12997 passed, 0 failed,
15 optional skips, 13012 total, 21m32s; session 94441 CLOSED. This qualifies
391, not subsequent publisher changes. No live full-union run remains.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-391/graphics-full-union.trx

## Next actions / acceptance

1. Collect 398 build and run strict original-ROM integration with the legal local
   Kickstart 3.1 image. Retain before/after original-Exec vector provenance checks.
2. Implement native owned-CMDB teardown. Validate the exact ownership claim;
   invalidate before freeing, refuse foreign/malformed claims, preserve ABI,
   and make no descriptor writes after FreeMem can yield to a new owner.
3. Qualify native release/recreate with original Exec and native MNTR readback.
4. Connect an explicit native initialization/publication option without changing
   existing inert/default builder behavior. Define failure ownership before
   returning failure to Exec. Keep native MonitorSpec and Intuition integration
   visible as separate remaining work; never claim host fixtures implement them.
5. Run focused tests and the unchanged broad graphics union on the new snapshot.

No blocker or user choice is needed. On resume, read this checkpoint first,
collect existing runs rather than restarting them, and continue the next action.

## Checkpoint 521 — graphics Region clipping unit resumed

While the native monitor publication work remains separately staged, the
graphics clipping stream completed the bounded two-node `AndRegionRegion`
source admission for an empty destination and for a contained canonical
one-node destination. Its AccurateM68000 regression proves read-only success,
malformed-link fallback, and non-contained fallback; the focused Region set
and signed-word audit pass 6/0/0. The generated image is 1,446,562 bytes,
71,959 branches, 3,428 relays, 7 chains, and 3,346 boundaries. No monitor
ownership or CyberGraphX claim is implied by this checkpoint.

## Checkpoint 522 — graphics Region clipping unit resumed

The clipping stream now covers the canonical two-node `AndRegionRegion`
single-node reduction in both source-node orders. A destination containing one
source rectangle is rewritten in place only after proving disjointness from
the other node; the empty, contained, malformed, and crossing forms remain
covered. The focused Region set and signed-word audit pass 6/0/0. The
generated image is 1,446,972 bytes, 71,987 branches, 3,428 relays, 7 chains,
and 3,346 boundaries. No monitor ownership or CyberGraphX claim is implied.

## Checkpoint 523 — graphics Region clipping unit resumed

The clipping stream now covers partial `AndRegionRegion` intersections for
both nodes of a canonical two-node source. A destination overlapping only one
source rectangle is clipped and rewritten in place; gap-crossing and
both-node forms remain on the portable builder. The focused Region set and
signed-word audit pass 6/0/0. The generated image is 1,447,514 bytes,
72,035 branches, 3,428 relays, 7 chains, and 3,346 boundaries. No monitor
ownership or CyberGraphX claim is implied.

## Checkpoint 524 — graphics Region empty-source two-node cleanup

The clipping stream now validates and retires a canonical two-node destination
when `AndRegionRegion` receives an empty native source. Both public nodes are
freed exactly once after complete link/envelope admission, and the destination
header is cleared in place; malformed links preserve D0 through fallback. The
new lifecycle fixture and focused Region/signed-word set pass 7/0/0. The
generated image is 1,447,834 bytes, 72,058 branches, 3,428 relays, 7 chains,
and 3,346 boundaries. No monitor ownership or CyberGraphX claim is implied.

## Checkpoint 525 — graphics Region one-node source / two-node destination

The clipping stream now reduces a canonical one-node source contained by
exactly one node of a canonical two-node destination and disjoint from the
other. The selected node/header is rewritten in place and only the other
public node is retired after complete admission; malformed links preserve D0
through fallback. The new two-order fixture and focused Region/signed-word
set pass 8/0/0. The generated image is 1,448,998 bytes, 72,141 branches,
3,428 relays, 7 chains, and 3,346 boundaries. No monitor ownership or
CyberGraphX claim is implied.

## Checkpoint 551 — graphics empty-source OR/XOR identity over three-node destination

The independent graphics clipping stream extends the allocation-free
empty-source OR/XOR identity to canonical three-node destinations. The native
continuation validates the third node, terminal/predecessor links, envelope,
relative bounds, and strict vertical ordering before returning TRUE; malformed
links decline with D0 preserved. Focused Region/audit: 49/0/0; generated
image: 1,468,356 bytes / 73,607 branches / 3,428 relays / 7 chains / 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## Checkpoint 550 — graphics empty-source OR/XOR identity over two-node destination

The independent graphics clipping stream now admits an empty native source
against a canonical two-node destination for both OR and XOR. Complete
ownership, links, public envelope, relative bounds, and strict vertical
ordering precede allocation-free success; malformed or escaping forms restore
D0 through the existing fallback. The new AccurateM68000 fixture plus the
focused Region/signed-word set pass 27/0/0; generated image: 1,467,986 bytes /
73,572 branches / 3,428 relays / 7 chains / 3,346 boundaries. Monitor/provider
ownership is unchanged and CyberGraphX remains excluded.

## Checkpoint 549 — graphics two-node OR source contains two-node destination

The independent graphics clipping stream now admits the bounded OR identity
where corresponding rectangles in a canonical two-node source contain those
in a canonical two-node destination under a matching public envelope. Complete
ownership, links, relative bounds, and per-node containment precede an
allocation-free rewrite of the existing destination rectangles from the
source; list links, node addresses, and source storage remain intact, while
malformed or escaping forms retain the existing fallback. The new
AccurateM68000 fixture plus the focused Region/signed-word set pass 26/0/0.
Generated image: 1,467,728 bytes / 73,548 branches / 3,428 relays / 7 chains /
3,346 boundaries. Monitor/provider ownership is unchanged and CyberGraphX
remains excluded.

## Checkpoint 548 — graphics two-node source below one-node destination

The independent graphics clipping stream now admits the complementary bounded
disjoint two-node/one-node OR/XOR topology where both canonical source nodes
lie strictly below a one-node destination under a matching public envelope.
Complete ownership, links, relative bounds, and strict vertical gaps precede
two private source-node allocations; the new nodes append after the existing
destination node, while allocation or address failures roll back before
publication. The new AccurateM68000 fixture plus the focused Region/signed-
word set pass 25/0/0. Generated image: 1,466,832 bytes / 73,472 branches /
3,428 relays / 7 chains / 3,346 boundaries. Monitor/provider ownership is
unchanged and CyberGraphX remains excluded.

## Checkpoint 547 — graphics two-node source above one-node destination

The clipping stream now admits the complementary bounded disjoint two-node/
one-node OR/XOR topology where both canonical source nodes lie strictly above
a one-node destination under a matching public envelope. Complete ownership,
links, relative bounds, and strict vertical gaps precede two private
source-node allocations; the new nodes prepend before the existing destination
node, while allocation or address failures roll back before publication. The
new AccurateM68000 fixture plus the focused Region/signed-word set pass 24/0/0.
Generated image: 1,465,740 bytes, 73,430 branches, 3,428 relays, 7 chains,
and 3,346 boundaries. Monitor/provider ownership is unchanged and
CyberGraphX remains excluded.

## Checkpoint 546 — graphics one-node source below two-node destination

The clipping stream now admits the complementary bounded disjoint one-node/
two-node OR/XOR topology where the source node lies strictly below both
canonical destination nodes under a matching public envelope. Complete
ownership, links, relative bounds, and strict vertical gaps precede one
private source-node allocation; the new node appends after the existing
destination chain, while allocation or address failures roll back before
publication. The new AccurateM68000 fixture plus the focused Region/signed-
word set pass 23/0/0. Generated image: 1,464,636 bytes, 73,388 branches,
3,428 relays, 7 chains, and 3,346 boundaries. Monitor/provider ownership is
unchanged and CyberGraphX remains excluded.

## Checkpoint 545 — graphics destination chain precedes two-node source

The clipping stream now admits the complementary bounded disjoint OR/XOR
topology where both canonical destination nodes precede both source nodes
under a matching public envelope. Complete ownership, links, relative bounds,
and strict vertical gaps precede two private source-node allocations; the new
nodes append after the existing destination chain, while allocation or
address failures roll back before publication. The new AccurateM68000 fixture
plus the focused Region/signed-word set pass 22/0/0. Generated image:
1,462,996 bytes, 73,260 branches, 3,428 relays, 7 chains, and 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## Checkpoint 544 — graphics both-source-nodes contained by second destination

The clipping stream now admits the complementary bounded read-only OR identity
where both nodes of a canonical two-node source are contained by the second
node of a canonical two-node destination, with the first destination node
strictly above and disjoint. Complete ownership, links, relative bounds,
ordering/gap, and containment precede success without allocation or mutation;
unsupported forms retain the existing fallback. The new AccurateM68000
fixture plus the focused Region/signed-word set pass 21/0/0. Generated image:
1,462,426 bytes, 73,239 branches, 3,428 relays, 7 chains, and 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## Checkpoint 543 — graphics both-source-nodes contained by first destination

The clipping stream now admits the bounded read-only OR identity where both
nodes of a canonical two-node source are contained by the first node of a
canonical two-node destination, with the second destination node strictly
below and disjoint. Complete ownership, links, relative bounds, ordering/gap,
and containment precede success without allocation or mutation; unsupported
forms retain the existing fallback. The new AccurateM68000 fixture plus the
focused Region/signed-word set pass 20/0/0. Generated image: 1,461,526 bytes,
73,158 branches, 3,428 relays, 7 chains, and 3,346 boundaries. Monitor/
provider ownership is unchanged and CyberGraphX remains excluded.

## Checkpoint 542 — graphics corresponding two-node OR containment identity

The clipping stream now admits the bounded read-only OR identity where
corresponding nodes of canonical two-node source and destination Regions are
contained under a matching public envelope. Complete ownership, links,
relative bounds, ordering/gap, and per-node containment precede success
without allocation or mutation; unsupported forms retain the existing
fallback. The new AccurateM68000 fixture plus the focused Region/signed-word
set pass 19/0/0. Generated image: 1,460,626 bytes, 73,077 branches, 3,428
relays, 7 chains, and 3,346 boundaries. Monitor/provider ownership is
unchanged and CyberGraphX remains excluded.

## Checkpoint 541 — graphics two-node OR source contained by one-node destination

The clipping stream now admits the bounded read-only OR identity where a
canonical two-node source is fully contained by a canonical one-node
destination under a matching public envelope. Complete ownership, links,
relative bounds, and per-node containment precede success without allocation
or mutation; unsupported forms retain the existing fallback. The new
AccurateM68000 fixture plus the focused Region/signed-word set pass 18/0/0.
Generated image: 1,459,726 bytes, 72,996 branches, 3,428 relays, 7 chains,
and 3,346 boundaries. Monitor/provider ownership is unchanged and
CyberGraphX remains excluded.

## Checkpoint 540 — graphics OR source contained by first destination node

The clipping stream now admits the symmetric bounded read-only OR identity
where a canonical one-node source is contained by the first node of a
canonical two-node destination and strictly disjoint from the second.
Complete admission proves ownership, links, envelope, relative containment,
and the inclusive gap before returning success without allocation or writes.
The new AccurateM68000 fixture plus the focused Region and signed-word set
pass 17/0/0. Generated image: 1,458,962 bytes, 72,925 branches, 3,428
relays, 7 chains, and 3,346 boundaries. Monitor/provider ownership is
unchanged and CyberGraphX remains excluded.

## Checkpoint 539 — graphics OR source contained by second destination node

The clipping stream now admits a bounded read-only OR identity where a
canonical one-node source is contained by the second node of a canonical
two-node destination and strictly disjoint from the first. Complete admission
proves ownership, links, envelope, relative containment, and the inclusive
gap before returning success without allocation or writes. The new
AccurateM68000 fixture plus the focused Region and signed-word set pass
16/0/0. Generated image: 1,458,220 bytes, 72,854 branches, 3,428 relays,
7 chains, and 3,346 boundaries. Monitor/provider ownership is unchanged and
CyberGraphX remains excluded.

## Checkpoint 538 — graphics OR source containing both destination nodes

The clipping stream now admits a bounded OR union where a canonical one-node
source strictly contains both nodes of a canonical two-node destination under
a matching envelope. Complete admission precedes one private replacement
allocation; the source rectangle is published as a one-node destination and
both old nodes are retired afterward. Allocation/address/Exec failures leave
the existing chain untouched. The new AccurateM68000 fixture plus the focused
Region and signed-word set pass 15/0/0. Generated image: 1,457,478 bytes,
72,783 branches, 3,428 relays, 7 chains, and 3,346 boundaries. Monitor/
provider ownership is unchanged and CyberGraphX remains excluded.

## Checkpoint 537 — graphics two-node source into empty OR/XOR destination

The clipping stream now admits a canonical two-node source Region into an
empty destination for both `OrRegionRegion` and `XorRegionRegion`. Complete
ownership, links, envelope, and relative-bounds admission precedes two private
allocations; the source envelope and copied rectangles publish only after the
new chain is fully linked. Allocation/address/Exec failures release
provisional nodes without mutating either Region. The new AccurateM68000
fixture plus the focused Region and signed-word set pass 14/0/0. Generated
image: 1,457,152 bytes, 72,771 branches, 3,428 relays, 7 chains, and 3,346
boundaries. Monitor/provider ownership is unchanged and CyberGraphX remains
excluded.

## Checkpoint 536 — graphics bounded coalesced two-node OR orientations

The clipping stream now admits equal-Y-span horizontal and equal-X-span
vertical coalesced overlap between a canonical one-node source and the first
node of a canonical two-node destination, with the second node below the
union. Complete admission precedes in-place widening; no allocation or
retirement occurs, and source/second-node topology remains unchanged. The
coalesced fixture plus the focused Region and signed-word set pass 13/0/0. The
generated image is 1,456,300 bytes, 72,718 branches, 3,428 relays, 7 chains,
and 3,346 boundaries. No monitor ownership or CyberGraphX claim is implied.

## Checkpoint 535 — graphics bounded coalesced two-node OR replacement

The clipping stream now admits a canonical one-node source overlapping the
first node of a canonical two-node destination with the same Y span and a
second node below. Complete ownership, link, envelope, and relative-bounds
admission precedes in-place widening of the first destination node; no
allocation or retirement occurs, and the source/second-node topology remains
unchanged. The new fixture plus the focused Region and signed-word set pass
13/0/0. The generated image is 1,456,158 bytes, 72,709 branches, 3,428
relays, 7 chains, and 3,346 boundaries. No monitor ownership or CyberGraphX
claim is implied.

## Checkpoint 534 — graphics bounded source-containing-two-node AND identity

The clipping stream now admits a canonical one-node source containing both
nodes of a canonical two-node destination under matching public envelopes.
After complete ownership, link, and relative-containment admission, the
destination is already the exact intersection, so native success performs no
allocation, retirement, or public writes. Malformed, escaping, and
non-matching forms remain on the existing Region path. The new identity
fixture plus the focused Region and signed-word set pass 12/0/0. The
generated image is 1,455,434 bytes, 72,647 branches, 3,428 relays, 7 chains,
and 3,346 boundaries. No monitor ownership or CyberGraphX claim is implied.

## Checkpoint 533 — graphics bounded contained-overlap OR replacement

The clipping stream now admits a canonical one-node source that strictly
contains the first node of a canonical two-node destination, while the second
node remains below the source under a matching public envelope. One fresh
public node copies the source-relative rectangle and links to the existing
second node before the replacement head is published; only then is the
contained old node retired. Allocation, address-class, and Exec failures roll
back without publishing partial state. The new success/rollback fixture plus
the focused Region and signed-word set pass 11/0/0. The generated image is
1,454,726 bytes, 72,584 branches, 3,428 relays, 7 chains, and 3,346
boundaries. No monitor ownership or CyberGraphX claim is implied.

## Checkpoint 532 — graphics bounded OR/XOR one-source/two-destination replacement

The clipping stream now admits one canonical source node and two canonical
destination nodes under matching public envelopes and strict vertical gaps.
One fresh public node copies the source-relative rectangle and is linked ahead
of the destination pair only after complete admission; allocation,
address-class, and Exec failures roll back without publishing partial state.
The new AccurateM68000 fixture and focused Region/signed-word set pass
10/0/0. The generated image is 1,453,830 bytes, 72,517 branches, 3,428
relays, 7 chains, and 3,346 boundaries. No monitor ownership or CyberGraphX
claim is implied.

## Checkpoint 527 — graphics Region identical canonical two-node identity

The clipping stream now admits exact identity for two canonical two-node
`AndRegionRegion` operands. It validates both complete chains, absolute
envelopes, and all four relative node rectangles before returning success
without allocation, retirement, or public writes. Empty/one-node destinations
reuse the existing two-node-source arm; malformed/non-identical pairs
preserve D0 through fallback. The new identity/mismatch fixture and focused
Region/signed-word set pass 5/0/0. The generated image is 1,449,860 bytes,
72,212 branches, 3,428 relays, 7 chains, and 3,346 boundaries. No monitor
ownership or CyberGraphX claim is implied.

## Checkpoint 528 — graphics Region identical canonical two-node OR identity

The clipping stream now admits exact identity for canonical two-node
`OrRegionRegion` operands. The OR dispatch restores its outer D0 frame before
the shared two-node proof; ownership, both links, absolute envelopes, and all
four relative node rectangles are validated before read-only success.
Malformed/non-identical pairs preserve D0 through fallback. The focused Region
family and signed-word audit pass 6/0/0. The generated image is 1,450,694
bytes, 72,289 branches, 3,428 relays, 7 chains, and 3,346 boundaries. No
monitor ownership or CyberGraphX claim is implied.

## Checkpoint 529 — graphics Region identical canonical two-node XOR identity

The clipping stream now admits exact identity for canonical two-node
`XorRegionRegion` operands. Complete ownership, both links, absolute
envelopes, and all four relative node rectangles are validated before Exec
resolution; both destination nodes are retired exactly once and an empty
header is published without mutating the source. Malformed/non-identical pairs
preserve D0 through fallback. The focused Region family and signed-word audit
pass 6/0/0. The generated image is 1,451,612 bytes, 72,367 branches, 3,428
relays, 7 chains, and 3,346 boundaries. No monitor ownership or CyberGraphX
claim is implied.

## Checkpoint 530 — graphics Region contained canonical two-node AND destination

The clipping stream now admits a canonical two-node `AndRegionRegion`
destination with the same public envelope and corresponding relative rectangles
contained by the source nodes. The existing destination is already the exact
result, so the read-only path returns success without allocation, retirement,
or publication. Escaping nodes, malformed links, and non-identical envelopes
preserve D0 through fallback. The focused Region family and signed-word audit
pass 6/0/0. The generated image remains 1,451,612 bytes, 72,367 branches,
3,428 relays, 7 chains, and 3,346 boundaries. No monitor ownership or
CyberGraphX claim is implied.

## Checkpoint 531 — graphics bounded OR/XOR two-node replacement

The clipping stream now admits a bounded disjoint two-node OR/XOR replacement
transaction. Two fresh public nodes copy the source-relative rectangles and
are linked ahead of the destination chain only after complete admission;
allocation, address-class, and Exec failures roll back without publishing
partial state. The replacement-list fixture and focused Region/signed-word
set pass 6/0/0. The generated image is 1,452,174 bytes, 72,389 branches,
3,428 relays, 7 chains, and 3,346 boundaries. No monitor ownership or
CyberGraphX claim is implied.
