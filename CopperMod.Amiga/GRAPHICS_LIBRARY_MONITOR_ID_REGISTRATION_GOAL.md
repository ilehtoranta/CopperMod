# Display-database monitor-ID registration

Part of the full Kickstart 3.1 graphics.library replacement. CyberGraphX is
excluded. The public SDK layouts remain unchanged; native guest-memory state
must work without a managed dictionary. Host-only implementations may retain a
separate backend path, but must obey the same observable selection contract.

## Original-ROM evidence

441 PAL and NTSC probes passed. An original private SetDisplayInfoData(-750)
call changes the installed family's public MonitorInfo.Mspc pointer. Its return
was68 for an88-byte input, GetDisplayInfoData returned88, and the original bytes
were restored afterwards. No allocation change occurred during registration or
selection. All actual graphics and Exec entry vectors were checked against ROM.

With the database pointing to another MonitorSpec:

- OpenMonitor(NULL,0) still selects GfxBase.DefaultMonitor.
- Tested nonzero IDs4/1000 and explicit installed-family mode IDs select Mspc.
- Linking/unlinking the selected node, renaming it, or clearing its timing flags
  does not change ID selection. CloseMonitor balances the reference after unlink.
- This proves registration, list membership, default selection and allocation
  ownership are distinct. CMMO2 alone cannot represent the complete relationship.

442 expanded probe PASSED on PAL and NTSC: all22 supported mode keys in default/
default-marker/explicit-family forms (66 IDs) across five stages,330 requests per
profile. Null Mspc makes every tested nonzero ID return NULL while ID0 still
selects DefaultMonitor. Selected refs balance; restoring the original record
restores all original bytes; no allocation changes. Two tests passed in15s.
Follow the canonical CURRENT in GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md.

SDK private-call ABI source:
https://amigadev.elowar.com/read/ADCD_2.1/Includes_and_Autodocs_2._guide/node0550.html
Public selection contract:
https://wiki.amigaos.net/wiki/Display_Database

## Required implementation units

### Current host handoff and next driver unit — 2026-09-17 / 479

Host bootstrap, transactional owned DefaultMonitor rebinding, and captured
CMDB3 registration/Point state are implemented in bounded units described in
GRAPHICS_LIBRARY_HOST_MONITOR_HANDOFF_GOAL.md. Canonical publication CURRENT
owns the remaining qualification handles. The next driver-facing work is split
in GRAPHICS_LIBRARY_MONITOR_DRIVER_RECORD_MUTATION_GOAL.md, beginning with
original-ROM partial mutation semantics rather than an inferred Set algorithm.

### Registered host MNTR checkpoint — 2026-09-17 / 463

Host queries now use CMDO/CMDB3 read-only snapshots, raw Mspc, stored default
family and requested Point fields. Default-marker aliases and authoritative
handles work without node/list/default/refcount dependencies. Invalid declared
native state declines before output; compact backend remains separate. Native
full default records share the ROM-qualified static image but still need raw
registration and complete alias/partial-payload integration.463 focused435 and
native-span8 checks passed;462 broader host monitor tests1227/0/8. Current live
strict/broad results and source hashes remain in canonical publication CURRENT.

### MNTR family-record checkpoint — 2026-09-17 / 458

The expanded original-ROM readback probe passed: all66 mode spellings share
the full88-byte record, and Mspc readback preserves even invalid raw pointers
without acquiring a reference.457 pins literal PAL/NTSC baselines.458 corrects
host family metadata through a separate guest-layout image; native metadata
and registered read-only query admission remain outstanding. Continue via
GRAPHICS_LIBRARY_MONITOR_INFO_READBACK_GOAL.md and canonical publication CURRENT,
which also tracks the live452/458 regressions. Historical live handles below
are superseded by that CURRENT, not restart instructions.

### Host ID selection checkpoint — 2026-09-17 / 448–452

Registry.Open/TryOpen now consult native registration before the lazy backend.
Explicit CMDO runtime state and full CMDB3 admission distinguish selected nodes,
valid absent mappings and invalid/partial state. Nonzero aliases use the stored
default family, not host profile. Exactzero uses a published DefaultMonitor before
private descriptor reads; INVALID_ID claims NULL without a base read. Unknown
syntax preserves the provider request. Compact/unmarked host images retain their
separate backend; foreign or truncated declared-native state does not permit an
allocation. Borrowed selected nodes are not adopted. Count failures/partial WORD
writes restore the original bytes; internal zero-count discovery does not open.

448 focused235 and monitor3008 cases passed.451 added unreadable-runtime and
sentinel/default independence, passing239 focused checks; its monitor run had
only two stale compact INVALID_ID fallback expectations (3010 passed/2 failed).
452 updates those to the verified ROM NULL contract and separately retains
unknown-ID decline.452 focused241/0/0 includes148 new cases,91 requests each,
plus the existing name/borrowed-close regressions.452 final monitor regression
COMPLETE3012 passed,0 failed/skipped,2m16s. Canonical CURRENT tracks the earlier
native full-graphics run, which is still live35309.

Host GetDisplayInfoData still normalizes default family through the host profile
in FindOrCreateForDisplay before calling Open; native MNTR still reads DefaultMonitor.
Do not claim query coherence. Next pin original-ROM MNTR readback for ID0/4/1000
and installed family while Mspc differs from DefaultMonitor; queries must not
increment counts. If testing malformed Mspc readback, never Open the invalid
pointer: a display query and an acquired MonitorSpec have different responsibilities.
Then implement shared mapping readback, malformed-state output admission, and
host publication/rebind before driver registration/synchronization completion.

### Native ID selection checkpoint — 2026-09-17 / 446–447

The native nonzero-ID path now selects from CMDB3 before any DefaultMonitor read.
Exact ID0 alone uses the default pointer; nonnull names retain precedence. The
reader admits owned CMDO and public pointer identity, full aligned/nonwrapping
CMDB3, both position IDs and valid stored default family. Nonzero default-family
IDs resolve through that stored value, not the compiled profile. An empty slot
claims NULL; malformed state declines with original request/register preservation.
A selected associated public160-byte node needs no CMMO allocation ownership,
list membership, name or timing flags. Count handling and saturation stay shared.

446 native selection520 cases passed; two stale fingerprint failures measured
the new stream.447 pins PAL1444822/NTSC1444722bytes,71812branches,3428relays,7chains,
3346boundaries, passing28 profile/branch audits. Added168 two-profile/fixed/HUNK/
AUTOINIT cases cover88 IDs each, including borrowed/unlinked/renamed-flagless nodes,
changed default family, null/odd/wrapped DefaultMonitor, empty/opposite mappings,
malformed database and node envelopes. Strict lifecycle uses original Exec and
132 native selections per route with borrowed/null Mspc and balanced closes.
447 monitor regression COMPLETE2864/0/0,2m26s; strict original-Exec integration
COMPLETE16/0/0,1m47s. The168 independent-pointer cases all passed. Full447 union
is live35309; canonical CURRENT owns continuation and terminal results.
Host lookup and MNTR mapping are next;
native selection is not completion of units4/5 or full monitor compatibility.

### Native registration lifecycle checkpoint — 2026-09-17 / 444–445

Explicit BuildRegistered publisher/release now pair against admitted owned CMDB3;
standalone Build remains independent of foreign CMDO. AUTOINIT uses registered
publication. Admission verifies descriptor ownership/public identity, aligned
nonwrapping v3 envelope, both position IDs and stable profile ID. Publication
requires an empty selected slot before/after AllocMem, initializes the private
node and stores its mapping before public list pointers/CMMO valid tag. Release
clears all slots equal to the retiring node before FreeMem and preserves foreign
borrowed mappings. Run monitor release before CMDB release. No writes after free.
444 focused checks651 passed, including220 new registration lifecycle cases;
445 strict-ROM assertions cover publication, busy refusal, release and AUTOINIT:
16 passed in1m53s.445 monitor regression2688 passed in2m20s and native profile/
branch audit28 passed in2s, all zero failures/skips. Canonical CURRENT owns
terminal evidence and prior443 full run. This is the native default lifecycle
portion of unit3; host mutation APIs, consumers and driver synchronization remain.

### CMDB3 construction checkpoint — 2026-09-17 / 443

The representation is implemented in the native embedded image and host allocator
path. Existing header, mode array and two12-byte position records keep their
offsets. Let T be the byte immediately after those position records:

| Offset | Guest LONG | Initial value |
| --- | --- | --- |
| T | Stable default-family ID | 11000 NTSC /21000 PAL |
| T+4 | NTSC MonitorSpec registration | 0 (absent) |
| T+8 | PAL MonitorSpec registration | 0 (absent) |

NativeDatabaseVersion is3; total size is old size+12. Both producer paths propagate
the actual profile, and construction initializes the entire envelope. CMDO/CMMO
and public SDK layouts are unchanged. Old versions are not reinterpreted as v3.
Native/host position consumers validate current version/size. Publication still
commits the descriptor tag last; failed/partial host trailer writes restore the
original allocation before freeing it. Initial zero registration is distinct
from an invalid database; consumers will enforce that distinction in unit4.

443 construction/profile/audit282, monitor2468 and original-ROM initialization/
lifecycle36 checks passed. Full443 union is tracked in canonical CURRENT.
This completes the representation
portion only; it does not populate registrations or change ID lookup yet.
Native default MonitorSpec publication and paired release must now integrate these
slots without weakening the independent-CMMO/foreign-CMDO ownership boundary.

1. **Pin the original-ROM contract — verified442.** The expanded probe includes
   registration removal and restore, exact ID-zero distinction, selected-node
   refcount, pointer/list independence and memory stability. Do not derive the
   expected selection from replacement code or mutable node flags/names.
2. **Version the native database representation — implemented443.** CMDB3 has
   explicit default-family identity and per-family MonitorSpec registration.
   Keep CMMO as allocation ownership only. Do not infer new fields from image
   size or silently reinterpret old versions. Preserve the current/original
   monitor position records. Specify absent registration versus invalid state.
3. **Publish and release registrations coherently.** Native default publication
   must establish its database mapping before making it visible. Registration
   updates may borrow associated MonitorSpecs; they never acquire ownership or
   free the selected node. Teardown clears mappings before freeing owned nodes;
   yielded allocation/free boundaries require the existing owner rechecks and
   no writes after ownership has changed. Update AUTOINIT unwind and tests.
4. **Route nonzero-ID selection through registration.** Native and host paths
   must use the database for every nonzero valid ID, including4 and1000; only
   exact zero bypasses it for DefaultMonitor. Select and validate the registered
   MonitorSpec independently of DefaultMonitor, and preserve close symmetry.
   Resolve default-family identity from stable registration state, not mutable
   MonitorSpec flags/name or allocation ownership. Unknown/bad data must not
   become an accidental pointer or an allocation/adoption opportunity.
5. **Use one mapping for display queries and opens.** GetDisplayInfoData MNTR.Mspc
   and OpenMonitor must agree. Reading display information must not increment
   the open count. Test host registration changes and native readback with
   fixed/HUNK images, PAL/NTSC and original-ROM behavior.
6. **Complete driver-facing registration and synchronization.** Establish how
   private Add/SetDisplayInfoData entry points and monitor drivers populate the
   replacement's own database. Do not expose a host-only test setter as a full
   native driver implementation. Finish list/database lifetime synchronization,
   unbounded valid-name/list handling and standalone failure semantics.

### Consumer integration rationale (native portion implemented447)

Before446, AppendOpenMonitorDefault loaded and admitted DefaultMonitor before
AppendRegisteredDefaultMonitorIdAdmission. Merely changing the latter's CMMO
check could not fix selection: nonzero IDs must branch to CMDB selection before any
DefaultMonitor dereference. Keep name precedence and exact ID0/alias handling
separate, then merge a validated selected pointer into the shared count path.
An admitted empty slot is a claimed NULL result, not provider fallback; malformed
database state must preserve the original request on decline. Registration must
not require CMMO ownership, list membership, canonical names or timing flags.
Keep the existing executed-PC/local-fallback-clone provenance assertions.

Host Registry.Open/TryOpen need the same separation from OpenBackend's lazy
allocation. Mapped registration absence must not allocate a replacement. Share
the selection authority with MNTR readback, without incrementing a query's count.
Include tests with DefaultMonitor null/foreign/unreadable while a distinct valid
registration remains, and with a borrowed unlinked node whose name/flags change.
Do not reuse the lifecycle admission's compile-time profile check for dynamic
family lookup: the reader must resolve aliases from the stored default-family ID.

## Verification and completion

Each unit requires actual executable checks, a source/build identity and terminal
test results in the canonical checkpoint. Include malformed/truncated/wrapped
base, database, record and selected-pointer envelopes; failed/partial host writes;
refcount saturation; no allocation/list/ownership changes during lookup; and
register/stack/fallback provenance. Update structural native-image fingerprints
only after measuring the new stream. Validate original-ROM vectors in all oracle
tests. Run the monitor regression and graphics union at appropriate integration
boundaries. No single phase completes this unit, and this unit does not complete
the overall graphics replacement.
