# Copper68k trace exception development fix — 2026-09-18

Development package: `1.4.1-trace.1`, unpublished. Source branch:
`codex/68000-trace-exception`, based on
`713ad6c1bc1bc31efc038996c50faccec31208d6`. The fix is committed on that branch;
the base commit alone does not contain it. The tested package was built before
the source commit; its original bytes and hashes are retained. Do not publish or replace any existing
package version as part of this work.

The interpreter now samples SR.T before each instruction and raises vector 9
after a completed instruction. It saves the resulting SR and next PC, clears
live T, enters supervisor mode and wakes STOP. Clearing T during the instruction
does not cancel its trace; enabling T starts tracing with the next instruction.
Completed instruction traps precede trace; aborted instructions suppress it.
Trace entry is part of the same host instruction boundary. No recursive CPU
dispatch or new bus API is introduced.

Fixed-plan/deferred batches and both compiled JIT paths decline execution when
architectural trace is active, so they cannot skip the exception boundary.
The untraced execution paths remain available.

Primary evidence: [Motorola MC68000 User's Manual](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf),
sections 6.2.3 and 6.3.8. The asserted 68000 timing is NOP 4 + trace entry 34
clocks. This does not certify all 68010/020/040 trace timings or implement 020
T0 branch tracing.

`M68000TraceExceptionTests` covers scalar/planned dispatch, SR transitions,
STOP, RTE/user stack, saved flags/PC, illegal instructions, TRAP ordering,
interrupt ordering and packed/kind-table batches with/without fast memory windows.
`M68000JitDirectZeroWaitTests` checks warmed classic and V2 compiled-loop fallback.
Six existing fixtures that intentionally set T now inspect the saved trace
frame instead of asserting that execution incorrectly stayed outside vector 9.

Validation: full Release CPU suite **1,499 passed, six skipped**, no failures.
The six optional external-corpus cases are unavailable coverage. The original
focused reproducer failed ten cases before the fix. Logs are under ignored
`artifacts/trace-exception/`. Build emits the pre-existing SourceLink build-time
NU1902 warning for Microsoft.Build.Tasks.Git 10.0.300.

Pack this working tree with `dotnet pack Copper68k/Copper68k.csproj -c Release -o <local-feed>`.
CopperScreen consumes the resulting package through its ignored development feed
and an exact version pin, without a project reference to this checkout.
