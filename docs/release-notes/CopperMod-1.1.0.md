# CopperMod 1.1.0

Release candidate. These packages have not been published.

TFMX songs now load through the normal player and command-line exporter. Amiga
EaglePlayer routines run inside CopperMod's existing 68k/Paula playback host,
including companion samples, subsongs, reset, time seeking and channel waveforms.
The replayer's format check selects the matching TFMX variant.

Keep `.tfx` and `.sam` files together, or use the Amiga `mdat.name` / `smpl.name`
convention. Extract ZIP archives before opening the song. On first use the player
downloads and verifies the required EaglePlayer binary from its registered
Aminet archive. A cached or manually configured binary supports offline playback.

```powershell
CopperMod.exe "C:\Music\Cyber Swats.tfx"
CopperMod.exe --render "C:\Music\Cyber Swats.tfx" --out voice.wav --seconds 6
CopperMod.exe --version
```

The framework-dependent archive requires .NET 10. The Windows x64 archive
includes its runtime. Neither archive includes CopperStart ROMs, original Amiga
ROMs, music files or third-party replayer binaries. The standalone player uses
its C# host firmware and does not require the CopperStart checkout.

Validation uses Cyber Swats (TFMX Pro), including its programmed moving-loop
voice effect. Independent hardware playback equivalence and broad TFMX 1.5/7V
corpus coverage have not been established. Replay accuracy remains in progress.

## Candidate validation

- Player tests: 69 passed, including the supplied TFMX Pro fixture.
- EaglePlayer tests: 23 passed; replayer acquisition tests: 9 passed.
- Exporter tests: 35 passed, 8 failed. Seven need unavailable external MOD,
  CUST or SID fixtures. The remaining failure is
  `SidLoopDetectionCanResolveUnknownSidDuration`; the SID implementation and
  that test are unchanged from the current main branch.
- Both extracted release flavors report version 1.1.0, acquire the replayer
  with an empty cache and render six seconds of Cyber Swats with finite,
  non-silent output and matching audio hashes.

The complete exporter suite is not green. This candidate is prepared for
review; it has not been tagged or published.
