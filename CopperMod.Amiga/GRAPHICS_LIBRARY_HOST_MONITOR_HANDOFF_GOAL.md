# Host monitor publication and handoff

Small implementation units within the full Kickstart 3.1 graphics.library goal.
CyberGraphX is excluded. Public structures and CMDB3 guest layout stay unchanged;
host-side lifecycle bookkeeping is not a replacement for native monitor drivers.
The authoritative execution checkpoint is the CURRENT section of
GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md.

## Contract

- Fresh host CMDB publication can bootstrap mappings from registry-owned,
  associated resident nodes. Do not infer ownership or family from DefaultMonitor,
  list membership, mutable names, flags, or reference counts.
- Stage mappings in the private database image before publishing its APTR/tag.
  Partial guest writes restore the complete recycled allocation before freeing it.
  Recheck base, resident snapshot, ownership and public APTR after construction.
- Rebinding moves an owned default pointer together with the list and backlinks.
  Clear the old pointer, publish the new pointer after the new links, and roll
  back every affected byte on refusal. Borrowed source defaults stay untouched;
  a foreign destination default is not overwritten.
- Capture all mutable CMDB3 trailer values before releasing an owned table:
  both current/original Points, stored default family and raw NTSC/PAL mappings.
  Read all values before committing any host copy. An unreadable field or invalid
  family keeps the allocation and binding available for retry.
- Recreate captured state exactly, including explicit zero and opaque mappings.
  Never Open, adopt, validate as a node, or free a raw mapping merely to preserve
  it. Replaced provider tables remain provider-owned and invalidate stale saved
  registration state rather than being interpreted or copied.

## Implemented units and evidence

1. Initial registry-to-CMDB bootstrap (473): a red four-route test demonstrated
   missing resident mappings. Constructor rollback now includes both mappings.
   Initial/rebound host residents, absent families, renamed/flagless nodes,
   unchanged counts and idempotent guest-modified mappings are covered.
2. Publication ownership rechecks (474): provider APTR changes, rebind and node
   backlink changes during construction discard only the private candidate.
   All three trailer LONG failures restore the full allocation and allow retry.
   Focused regression1211 passed,8 optional ROM skips.
3. Owned DefaultMonitor rebind (476): red4fail/1pass; implementation includes
   old/new default fields in the existing rollback transaction. Partial writes,
   foreign destination refusal and borrowed source preservation are covered.
   Focused regression1216 passed,8 optional ROM skips. Original-Exec integration
   passed24/0/0,3m59s. The host oracle explicitly creates a compatibility resident
   before native takeover; OpenMonitor on a registered empty table must not
   silently create a resident. Native creation is tested through its own publisher.
4. Captured state preservation (478): red6fail/0pass showed original Points and
   registration state being reset. Saved registration now survives release,
   externally cleared APTR and rebind, with null/odd/wrapped/opaque mappings and
   changed default family. Focused regression1222 passed,8 optional ROM skips.
   479 adds all-field read failure/invalid-family refusal and successful retry:
   all24 scenarios passed in the7335-pass integration union excluding unchanged
   native admission (22 optional ROM skips,4m8s). Current original-Exec lifecycle
   integration passed24/0/0,5m1s. Canonical CURRENT owns the remaining full474 run.

## Remaining work / resume order

Collect current qualification results, then finish native/host monitor-driver
record mutation and lifetime synchronization as separate units. Explicit host
creation after native CMDB ownership, callbacks during teardown, provider takeover,
and the split between native CMMO allocation ownership and host resident ownership
must remain deliberate entry paths. Do not treat this handoff as a full private
Add/SetDisplayInfoData implementation or complete monitor-driver compatibility.

Static family metadata is still a ROM-qualified boot image. Arbitrary driver
record updates, complete list/name behavior, task synchronization and the rest
of the graphics replacement remain part of the unfinished full goal.
