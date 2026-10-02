# MonitorInfo registration and family-record readback

Part of the full Kickstart3.1 graphics.library replacement. CyberGraphX remains
excluded. This goal is unit5 of GRAPHICS_LIBRARY_MONITOR_ID_REGISTRATION_GOAL.md;
it does not replace the full goal. Canonical live execution state remains in
GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md CURRENT.

## Verified ROM contract (453–457)

Original graphics GetDisplayInfoData/SetDisplayInfoData and Exec vectors are
verified against unchanged ROM. Set(-750) returns68 for the88-byte MNTR record;
Get returns88. Restore the original record before freeing any candidate node.

Unlike OpenMonitor(NULL,0), a MNTR query for ID0 follows the database Mspc rather
than GfxBase.DefaultMonitor. This is also true for4/1000 and all66 supported ID
spellings (22 keys in default/default-marker/installed-family forms). The entire
88-byte record is identical across these spellings, not just its Mspc field.
Neither linking/unlinking, renaming, nor clearing node timing flags changes it.

Mspc readback is opaque. Original Set/Get accepts and returns1, an odd candidate
pointer,FFFFFF60,DEADBEEF andFFFFFFFF unchanged. These values are NEVER opened
or dereferenced by the oracle. Queries do not change either node's count or
allocate. OpenMonitor must still validate a node before acquiring a reference;
sharing the database authority does NOT mean sharing node admission.

455/456 each passed2 profiles,21s, covering1320 readback requests plus660 balanced
Open/Close observations.456 captures full record bytes;457 pins independent
literal baselines rather than deriving expectations from replacement builders.
Follow canonical CURRENT for457 terminal results.

## Family metadata, distinct from display-mode and MonitorSpec geometry

The original SDK layout is graphics/displayinfo.h, struct MonitorInfo:
https://amigadev.elowar.com/read/ADCD_2.1/Includes_and_Autodocs_2._guide/node00BD.html
The table below is measured from this repository's original Kickstart3.1 ROM,
not inferred from field names. These are PAL/NTSC boot baselines; later monitor
driver/preferences mutation must remain representable.

| Byte offset | Field | PAL | NTSC |
| --- | --- | --- | --- |
| 10 | Mspc ULONG | raw registered pointer | raw registered pointer |
| 14 | ViewPosition Point | (129,44), mutable CMDB state | (129,44), mutable CMDB state |
| 18 | ViewResolution Point | (44,44) | (44,52) |
| 1C | ViewPositionRange Rectangle | (93,29,136,57) | (93,21,136,63) |
| 24 | TotalRows WORD | 312 | 262 |
| 26 | TotalColorClocks WORD | 226 | 226 |
| 28 | MinRow WORD | 29 | 21 |
| 2A | Compatibility WORD | 0 | 0 |
| 2C | pad[32] | preserve recorded bytes | preserve recorded bytes |
| 4C | MouseTicks Point | (22,22) | (22,26) |
| 50 | DefaultViewPosition Point | (129,44), original CMDB state | (129,44), original CMDB state |
| 54 | PreferredModeID ULONG | 00029000 | 00019000 |

Offsets in the table are hexadecimal. Do not substitute mode pixel ticks for
ViewResolution or MonitorSpec LegalView for ViewPositionRange. Do not change
MonitorSpec or hardware timing constants to fix this different public record.
Do not interpret opaque pad bytes as zeros or invent semantics for them.

88-byte reference records, Mspc normalized tozero:

PAL:
```text
80002000000210000000000300000009000000000081002C002C002C005D001D00880039013800E2001D00000000000000000000000036FF00002BFF0000000000000000000036FF00002BFF001600160081002C00029000
```

NTSC:
```text
80002000000110000000000300000009000000000081002C002C0034005D00150088003F010600E2001500000000000000000000000036FF0000289F0000000000000000000036FF0000289F0016001A0081002C00019000
```

## Implementation units and gates

1. Introduce a shared portable family-record representation with these verified
   metadata fields and opaque payload, separate from per-mode geometry. Dynamic
   Mspc/current/original positions come from admitted database state. Keep the
   public96-byte SDK envelope and existing88-byte transfer/terminator policy.
2. Make host MNTR resolution use the registered default family and canonical
   family record, including default-marker1000 aliases and authoritative handles.
   Preserve compact host backend support. Do not open, allocate, adopt or validate
   the raw Mspc as a node just to read it. Distinguish corrupt database state from
   a valid absent/opaque mapping before any output mutation. Update host tests
   that currently expect per-mode ViewResolution/PreferredModeID or Compatibility1.
3. Make native MNTR payloads read the same raw mapping and family-record image.
   Current code accepts only selected mode0 payload and uses DefaultMonitor;
   other valid family/mode payloads still fall back. Support them deliberately,
   preserving named/provider ownership, signed branch reach, register/stack ABI,
   executed-PC fallback provenance and fixed/HUNK image behavior. Keep Mspc raw.
4. Preserve field-granular partial transfers, header-only dependencies, odd/
   wrapped destination guards, overlap snapshot ordering, failed/partial host
   writes and rollback. Current/original Point reads remain separate and only
   required when their bytes are requested. A family-record implementation must
   not acquire spurious MonitorSpec/list/ownership dependencies.
5. Verify full bytes against the literal ROM baselines across all supported
   aliases/profiles, with distinct DefaultMonitor/registered pointers, raw/empty
   mappings, changed stored default family, reference saturation and unavailable
   node memory. Exercise host and emitted native code, then strict original-ROM
   differential checks and full graphics regression. Do not treat node selector
   tests as proof of query correctness or a reference query as an OpenMonitor.

## Implementation checkpoint — 2026-09-17 / 458

457 literal baselines passed on both original-ROM profiles (2/0/0,20s).
458 adds GraphicsMonitorInfoImage and uses it from the host BuildMonitorInfo.
It preserves the measured family metadata/pad, raw pointer and independently
supplied Points in the96-byte guest layout. The emitter can consume the same
image in its separate integration unit; native code has not changed yet.
Focused family/transfer/host tests passed45/0/0. Canonical CURRENT owns live
regression handles and terminal results. This completes the static host-family
image portion, NOT registered query admission, aliases or native parity.

## Registered host query checkpoint — 2026-09-17 / 459–462

GraphicsMonitorInfoReadback reads owned CMDO/CMDB3 directly, with three distinct
outcomes: compact-host backend, invalid native state, and a registered snapshot.
Core MNTR queries now use stored default family and raw Mspc, including exact0
and default-marker1000 aliases. A nonnull handle stays authoritative. Snapshot
current/original Points separately only when requested (>20/>80 bytes), and
read Mspc only for >16 bytes. Node validity, list membership, name, reference
count and managed allocation ownership do not participate. Capture before
output mutation, including overlapping database/descriptor destinations.

Native-overlay host dispatch bypasses publication checks for registered data
and header-only queries. Compact host data still has its separate lazy backend.
The new boundary tests exposed a wrapped-destination preflight state read;461
guards the range first.461 focused274/0/0 includes21120 matrix requests, opaque/
empty pointers, malformed state, field boundaries, overlap, partial byte-write
rollback/retry, no-read sentinel forms and compact provider call boundaries.

462 shares the static image with the existing native full default-mode publisher;
it does not yet change that publisher's DefaultMonitor selection or mode0-only
payload admission. Native full-record expectations are also pinned independently
to457 literals.463 measured profiles are PAL1444712/NTSC1444612 bytes, with71812
branches/3428relays/7chains/3346boundaries. Combined host/native tests435/0/0,
native span8/0/0 and original-ROM lifecycle/registration20/0/0 passed.462 broader
host monitor regression1227/0/8 passed (skips require ROM). Fresh full463 remains
live; canonical CURRENT owns its handle. This is not complete native query parity.

## Native registered query checkpoint — 2026-09-17 / 464–470

NativeGraphicsRasterBodies.MonitorInfo now admits registered CMDO/CMDB3 and
serves all88 accepted selector spellings, including authoritative handles and
the stored default family. Mspc is raw query data. Current/original Points are
captured only beyond20/80 requested bytes; the pointer only beyond16. A private
88-byte stack image permits exact byte copies at odd/final valid destinations
and snapshots before overlapping output. Maximum stack is152 bytes including
the return address. Invalid declared-native state restores the input count and
registers before declining; unknown selectors retain provider opportunity.
Compact legacy images retain the predecessor query path. Empty/sentinel forms
avoid GfxBase/output reads; 469 retains the legacy final zero-return sequence.

466 passes193 native alias/position/profile checks;468 adds8 boundary cases
(80 scenarios) covering whole-CMDB read-only behavior, overlap, wrap, empty and
malformed data. New tests measure provider terminals by executed PC including
linker clones, not unchanged D0. 470 adds original-Exec integration queries for
borrowed/null/opaque Mspc across66 installed-family aliases with literal457
record bytes and unchanged counts/database/allocation balance. Strict470 passed
20/0/0 in2m24s; focused470 audit passed291/0/0 in1m2s, final result rows verified.
Broader native469 finished7474 passed/24 failed; all24 were poisoned synthetic
legacy-base fixtures, corrected471 and passing in112 focused boundary checks.
Full474 union COMPLETE14819 passed/0 failed/22 optional ROM skips,25m39s;87842
CLOSED, final rows verified. Canonical CURRENT owns the source identity.
Measured current profile: PAL1445774/NTSC1445674 bytes,71886branches,3428relays,
7chains,3346boundaries. Full-union qualification remains tracked separately.

## Remaining integration and compatibility work

Host registered MNTR readback uses the captured family image and database state.
FindOrCreateForDisplay retains host-profile normalization only for the separate
compact backend. The registered native query path is implemented but awaits
broader qualification. Its legacy compact path still uses DefaultMonitor and
the predecessor admission boundaries. Host publication/rebind now has implemented
bootstrap, default-pointer transfer and saved registration/Point state; see
GRAPHICS_LIBRARY_HOST_MONITOR_HANDOFF_GOAL.md for bounded scope and evidence.
Monitor-driver mutation/synchronization remain required; static boot metadata is not mutable
driver-record support. Do not mark full monitor or graphics compatibility done.
