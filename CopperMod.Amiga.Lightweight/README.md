# CopperMod.Amiga.Lightweight

Independent PAL OCS A500 engine: Copper68k 68000, 512 KiB Chip RAM,
512 KiB slow RAM, native Kickstart 1.3, one read-only standard ADF drive.
ROMs and game media are not distributed with this package.

Use the public `LightweightA500Machine` API to load a ROM, mount/eject ADF
bytes, submit input, reset and execute frames. Read `Framebuffer` and
`AudioSamples` from their reusable buffers before executing the next frame.
The engine is single-owner; marshal UI input onto its execution thread.
Host presentation, pacing and audio-device delivery remain outside this library.
Request `FramebufferWidth = 908` to retain OCS hires output.

The host does not need friend access, a Legacy Bus/Scheduler or CopperStart.
Internal CPU timing hooks are shared only between the engine and Copper68k.
Unsupported behavior is reported, not delegated to another engine.

The preview package is experimental. Compatibility is narrower than a complete
Amiga emulator; documented hardware uncertainties are not closed by package
validation. Only Release builds without diagnostics may be packed.
