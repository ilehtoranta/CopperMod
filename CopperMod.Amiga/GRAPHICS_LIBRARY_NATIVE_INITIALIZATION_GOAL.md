# Native graphics initialization unit

Part of the full Kickstart3.1 graphics replacement. CyberGraphX is excluded.
Do not mark the full thread goal complete when this unit passes.

## Evidence and current boundary — 2026-09-12 / 389

### Resume routing — 2026-09-13

Follow GRAPHICS_LIBRARY_NATIVE_MONITOR_PUBLICATION_GOAL.md CURRENT section for
authoritative source identity, results, live runs and next work. An explicit
native publication option now composes with initialization; defaults stay inert.
AUTOINIT failure must free the library base manually; direct-call ownership is
different. Do not infer Exec cleanup from the historical caller-owned design
below. The full replacement remains incomplete.

### Historical continuation — 2026-09-13 / 395

395 completes the host-publication/native-readback integration boundary in step3:
8 strict ROM cases pass,0 failed/skipped,1m7s; build PASS23.07s.18009 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-395/exec-owned-monitor-readback.trx
Test SHA2560123E070BBBCD0EC98752C00F4914B8C96FA6C48ED7FBD86A02591D115723D8D.
Four original init-only cases remain, plus PAL/NTSC fixed/relocated publication
cases. The portable GraphicsLibraryCore binds the actual Exec-created allocation;
its allocator adapter invokes original Exec AllocMem/FreeMem. The host core owns
CMDB/MonitorSpec publication. Native GetDisplayInfoData reads21..24 byte prefixes
(even/odd) and full88/capped96 records, current updates, original positions, and
republished state after release. Release clears ownership, frees CMDB through
native Exec, and native reads decline without output. Template descriptor and
the original ROM library's full MNTR record stay unchanged. Hardware callbacks
throw if unexpectedly invoked. All7 allocation/lifecycle Exec vectors are checked
against the original ROM before/after. No production code changed in395.

This is NOT native CMDB allocation/publication: the core owner is host-side.
Remaining: native ownership publication/rollback/teardown and Intuition mutation
integration. Keep the full goal active.391 full union remains LIVE94441; collect
it without restarting, then qualify newer source/tests as required.

### Historical continuation — 2026-09-13 / 394

393 strict provenance check FAILED all4 cases because AllocMem(-198) is a host
overlay in CreateNativeIntuitionOracle.45128 CLOSED.392 passes therefore qualify
native resident/initializer execution with the normal memory overlay, NOT a
wholly original Exec allocation path. This corrects the earlier interpretation.
Source confirms ExecServices.InstallKickstartRomOverlay installs selected memory
gateways; Dispose restores their saved vectors through HostLibraryGatewayRegistry.

394 changes only this disposable ROM test: after boot readiness it disposes the
active ROM Exec overlay via existing private runtime ownership, restoring native
vectors before any test allocation/init. It retains all6 before/after original-ROM
checks. Production takeover policy is unchanged. Build PASS22.31s;12 standalone
initializer controls pass,0 failed/skipped,1s. Strict test COMPLETE:4 passed,
0 failed/skipped,40s;84603 CLOSED:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-394/original-exec-init-strict.trx
Test SHA256BE005B7C50A2907ABA8D52C594FDA13FEA590FBE088AFDDE037D5C87FB81CD4D.
391 full union remains LIVE94441. This proves original Exec resident/allocation
initialization for fixed/relocated PAL/NTSC, including unchanged6 Exec entry
provenances before/after. Step3 (owned-CMDB publication/native readback against
the real allocation) remains next, followed by native ownership/Intuition work.
No genuine blocker exists.

### Historical continuation — 2026-09-13 / 393

391 connects explicit initializeAllocatedImage through all HUNK Build overloads.
It appends the initializer and relocates the resident init pointer to it without
changing public vectors/function-array entries or legacy default behavior.
Build PASS42.12s; initializer/profile tests39 passed,0 failed/skipped,2s:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-391/hunk-initializer.trx
HunkBuilder SHA256D43B0495985D08E666465849ADD0457860F6A3779A453E49688AC11045879B0D
InitializerTests SHA256835DE611604067AB1B910CAB8012ACEC1C75469AF5DE64B79925AED0150C0BE6

392 adds real Exec InitResident calls against fixed/relocated native residents.
All4 PAL/NTSC cases pass,31s,build PASS22.54s. Returned allocation00C0C358 differs
from both template/original graphics; fields, all vectors, empty lists and inert
descriptor checked. Original graphics ROM entries remain unchanged.
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260913-392/original-exec-init.trx
393 additionally checks before/after ROM provenance for InitResident, MakeLibrary,
MakeFunctions, InitStruct, AllocMem and AddLibrary. Stronger proof is pending:
LIVE45128, original-exec-init-strict.trx under graphics-runtime-descriptor-20260913-393.
First393 build hit concurrent duplicate-local errors in Copper68k/M68kCore.cs;
normal retry passed21.20s without any CPU edits by this goal. No blocker remains.
Current ROM test SHA256537CC3B4C91F2E0C9020F1B000D54890AA27EAF40CAA843BB92FCA8688741F2B.

391 full union remains LIVE94441.389 finished12990passed/3stale-assertion failures/
15optional skips,13008total;43703 CLOSED.390 corrected those3 expectations and
38 focused tests passed. Next collect45128 and94441, then connect owned-CMDB
publication/readback against the real Exec-created allocation (step3 below).
Do not treat inert initialization as native ownership publication or SetPrefs.

Fixed NativeGraphicsLibraryImageBuilder requires an explicit libraryInitAddress;
its AUTOINIT InitStruct table pointer is zero. HUNK WriteResident also writes a
zero InitStruct pointer and currently selects the start of nativeCode as init.
NativeGraphicsRasterBodies starts with its fallback RTS. Thus positive-image
templates and function-array JSR tests do not prove initialization of the new
allocation returned by Exec. Existing AmigaBootMemoryTests InitResident coverage
uses a host Exec trap with null init function; it is not native graphics proof.

389 adds NativeGraphicsLibraryInitializer.Build: position-independent68000 code
accepting the allocated base inD0. It validates non-null/even/nonwrapping extent
and lib_PosSize before writes, copies the positive template, preserves Exec's
actual lib_NegSize/lib_PosSize, and rebases name/ID/font/monitor-list pointers to
the returned allocation. It preserves D2-D7/A2-A6, uses four scratch-stack bytes,
and returns the original base or zero on admission failure. Its descriptor is
inert (version initialized; tag/owner/database zero). It does not allocate CMDB.

Build PASS40.31s. Eight standalone native tests pass,0 failures/skips,522ms:
C:/Users/vsys-admin/AppData/Local/Temp/graphics-runtime-descriptor-20260912-389/initializer.trx
Tests use PAL/NTSC, descriptor opt-in/out, two code placements, poisoned memory,
preserved size fields/registers/guards, and null/odd/wrapping/short admissions.
They do NOT execute real Exec InitResident. The initializer is not yet connected
to image/HUNK builder selection. Existing raster fingerprints are unchanged.

## Remaining implementation and acceptance

1. Add explicit HUNK initializer selection without changing legacy default
   construction. Append the position-independent initializer and relocate the
   resident init pointer to it; do not alter public vector/function-array targets.
   Fixed images already accept its independently placed address.
2. Invoke original-ROM Exec InitResident with the emitted resident, not a direct
   call to a function-array slot. Verify a distinct returned allocation, complete
   initialized fields/sentinels/self-pointers, preserved actual allocation sizes,
   inert descriptor and linkage. Cover fixed/relocated and PAL/NTSC. Preserve the
   original ROM graphics library and all unrelated guest resources.
3. Bind/publish owned CMDB against the new returned allocation and verify native
   current/original MNTR output from it. Clearly distinguish host publication
   from a future wholly native allocation/publication routine; neither metadata
   nor a host test may be reported as native ownership initialization.
4. Keep native ownership publication, allocation failure rollback, teardown and
   Intuition mutation integration in the full plan until separately implemented.
5. Run focused construction/ABI tests, unchanged full graphics union, and the
   selected original-ROM integration tests. Record source identity and outcomes.

389 full graphics union is live43703; exact status is maintained in the latest
GRAPHICS_LIBRARY_NATIVE_MONITOR_STATE_GOAL.md checkpoint. Do not restart it while
live. No user clarification is needed to continue this unit.
