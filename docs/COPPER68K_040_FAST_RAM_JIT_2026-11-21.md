# Copper68k 68040 Fast RAM JIT

The opt-in 68040 JIT now promotes eligible, frequently executed Fast RAM
loops to compiled code that can run whole iterations and resume after a
device deadline. Promotion follows statically known branches, calls and
returns. It requires identity-mapped execution with coherent code generations
and a CPU-private Fast RAM stack. Cached, mapped and unsupported instructions
retain their existing dispatch paths.

## What changed

- The accelerated 040 uses its minimal timing policy for compiled register
  shifts. A captured 846-instruction SysInfo iteration had been charged
  1,030 cycles because eight shifts used classic count-dependent timing.
  After the correction, interpreted and compiled runs retired the same
  846 cycles for that iteration.
- Bounded traces keep register state and eligible call/return paths together.
  Uncached Fast RAM traces recheck code generations and exit after a timed
  transfer or a write that invalidates compiled code. The compiler checks for
  already queued work and snapshot availability before copying guest code.
- The loop tier binds zero-wait Fast RAM operands and a private stack for the
  current slice. It can fold repeated updates to an independent additive
  counter once the compiled iteration confirms that the other registers and
  flags recur. It charges every guest instruction to the same cycle budget.
- A partial iteration publishes PC, registers, flags, stack and cycles at the
  device cutoff. The next slice reopens the ordinary bus boundary and checks
  that state, code generations and active return words still match. It samples
  the current Fast RAM operands before resuming. A failed guard returns to
  ordinary CPU dispatch.

## Verification

The cutoff matrix used the captured 846-instruction loop. It stopped after
each possible instruction in two counter-wrap cases and all three 040 stack
modes. All 5,070 cutoffs and 2,144,610 resumed guest instructions matched the
interpreter in registers, status, PC, opcode, stack banks, cycles and Fast RAM.
It also checked a changed return address, watched stack, code generation,
clock and registers, plus a changed timer value that takes the untaken branch.
The retained test suite contains synthetic cutoff, write-invalidation, stack,
MOVEM, and interpreter/JIT timing cases. On the integration branch, the
focused Release suite passed 693 tests and skipped two optional tests.

The native CopperScreen SysInfo 4.4 trials scored 2,102,037 Dhrystones at
1:1 PAL pace, 50.7% of the supplied 4,146,390 WinUAE score. The host placement,
input hashes, timings and validation limits are recorded in the
[CopperScreen SysInfo report](https://github.com/ilehtoranta/CopperScreen/blob/main/docs/engine/SYSINFO_040_JIT_2026-11-21.md).
The timing runs used an unpublished .NET 10 build of the shared CPU package.
