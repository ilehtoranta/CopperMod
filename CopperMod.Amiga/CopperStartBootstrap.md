# CopperStart native-ROM bootstrap extension

CopperStart production ROMs require this private extension before starting
native Exec/DOS. The emulator handles one bounded setup call, records a ready
bootstrap session and returns to ROM. The OS, boot disk, filesystem, task
scheduler and display initialization continue in guest code.

This is a compatibility/distribution gate. The public protocol and ROM can be
reimplemented or patched; it is not cryptographic authentication or tamper-proof
licensing. No secret key, network activation, host filesystem access or guest
Exec/DOS shim is involved.

The guest emits `FF00 43534731` at CHIP address `0x780` and calls it with JSR.
CopperMod uses its existing relocatable host gateway path; it does not overlay
ROM bytes or commandeer standard TRAP instructions. Unrelated ROMs do not
execute this explicit extension. The callback returns through the normal
emulated call stack, preserving registers other than D0 and writing only the
16-byte reply at `0x770..0x77F`.

The caller must be supervisor code returning to the 512 KiB ROM window, use
D0=`43534231`, D1=1, A0=`760`, and have a valid CHIP call stack. The request is:

| Address | 32-bit big-endian field |
| --- | --- |
| 760 | Request magic `43534231` |
| 764 | ABI version 1 |
| 768 | Guest round-trip value |
| 76C | State: 1 pending, 2 accepted, 3 refused; guest-owned |
| 770 | Reply magic `43504D31` |
| 774 | Reply ABI version 1 |
| 778 | Request value XOR reply magic XOR version |
| 77C | Capabilities: bit 0, native-ROM bootstrap available |

D0 returns the reply magic. The round-trip value detects malformed/stale replies;
it is not a cryptographic challenge. Invalid requests return D0=0 and leave RAM
unchanged. Ready state and call counters reset on cold/external hardware reset.
`CopperStartBootstrapEnabled` lets an embedding host disable the extension;
a disabled handler uses ordinary line-F exception behavior.

The ROM temporarily supplies its own line-F handler, restores the previous
vector after the call, and refuses missing or invalid replies. Its unsupported
screen and supervisor idle require no initialized OS or disk I/O. Tests cover
accepted and missing-handler execution on accurate 68000, JIT 68000, 68020 and
68040, stack restoration, bounded reply writes, invalid arguments and reset.

```sh
dotnet test CopperMod.Amiga.Bootstrap.Tests -c Release -m:3
```

This focused project shares the engine's existing test friend assembly name.
It does not depend on the separate host-shim/UI/Cust projects. Their larger
suite currently also needs CopperStart host-source configuration and a stale
Cust `KickstartTrapTable` constructor fixture updated; this change does not
claim that larger suite passes.
