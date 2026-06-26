# CopperPad

CopperPad projects are grouped here as one product family.

## Layout

- `src/CopperPad` - platform-neutral controller abstractions and profile JSON support.
- `src/CopperPad.HidSharp` - desktop HidSharp provider, SDL_GameControllerDB data, diagnostics, and fallback mappers.
- `src/CopperPad.GameController` - Apple GameController provider shell and iOS-gated source.
- `src/CopperPad.Gui` - shipped Avalonia controller test/map/calibration app.
- `tests/CopperPad.Tests` - core and HidSharp provider tests.
- `tests/CopperPad.Gui.Tests` - GUI profile/editor logic tests.
- `scripts` - release helper scripts, including GUI publish/smoke testing.

See `RELEASE.md` for the release checklist.
