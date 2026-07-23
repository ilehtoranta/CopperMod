# CopperStart host gateway model

CopperStart host gateways are atomic high-level emulation operations.  They
execute synchronously on the emulator thread and never create CLR tasks, use
the thread pool, or advance Paula/custom-chip state directly.

Host code returns one of three scheduler outcomes:

- **Completed**: apply register and guest-memory results, then perform the
  normal emulated subroutine return.
- **BlockCurrentTask**: retain the emulated call frame, move its ROM Task to
  the appropriate wait state, and return to the outer CPU boundary.  For a
  host wait this pushes a fixed recheck gateway and enters the native KS 3.1
  `Switch` vector; KS owns the saved `tc_SPReg` frame and selects the next
  task.  The recheck gateway performs the delayed RTS only after the condition
  is satisfied.
- **Reschedule**: complete the call normally, then honor a pending Exec
  reschedule at the next instruction/gateway boundary.

The machine scheduler is the only owner of emulated time, hardware advancement
and interrupt delivery.  Atomic host operations are zero-time by default;
interrupts are considered after the operation completes.  A routine requiring
instruction-level interruptibility or timing must be supplied as replacement
68k code rather than as a resumable CLR routine.

`Wait` and `WaitPort` use one shared host state machine in both CopperStart
and active KS 3.1 ROM-Exec modes. CopperStart's compatibility `WaitIO` uses
that same state machine. In ROM-Exec mode, the `DoIO`/`SendIO`/`CheckIO`/
`WaitIO`/`AbortIO` vectors remain native until the specific device is replaced:
the device's original `BeginIO`/`AbortIO` vectors remain authoritative.
`ObtainSemaphore` and `ObtainSemaphoreShared` use the same deferred task-wait
transition when contention occurs; `ReleaseSemaphore` grants queued waiters,
marks them ready, and only requests dispatch. This preserves ROM-first
scheduling rather than recursively running the scheduler inside a host gateway.

`trackdisk.device` is the first concrete ROM-device replacement. The ROM
resident still creates and links its Device base; CopperStart discovers that
live `DeviceList` node and registers gateways only at its Open, Close, Expunge,
ExtFunc, BeginIO, and AbortIO vectors. Its backing bytes remain untouched, and
every non-trackdisk device remains native.

The ROM Exec overlay also owns task-list, signal, and trap allocation LVOs.
Those operations edit only the live ROM-created Task and Exec list fields.
They never restore a managed CPU snapshot: `Wait`, a current `RemTask`, and a
latched reschedule enter the original KS `Switch` or `Schedule` vector at the
next outer boundary, which remains the authority for `tc_SPReg` frames and
the selected 68k task.

The emulator-private Exec compatibility vectors are `-1206` (Wait) and `-1212`
(Reschedule).  They are direct host gateways in both CopperStart and ROM-Exec
modes; they never modify a Kickstart ROM vector.
