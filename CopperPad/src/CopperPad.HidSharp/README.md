# CopperPad.HidSharp

`CopperPad.HidSharp` is the desktop HID provider for the platform-neutral CopperPad core API. It contains HidSharp enumeration/reading, raw report diagnostics, profile-backed mapping, SDL_GameControllerDB lookup, and fallback mappers.

Mapping precedence is:

1. user/app profile JSON
2. provider-native mappings where available
3. bundled SDL_GameControllerDB data
4. built-in fallback mappers
5. raw diagnostics only

The bundled SDL_GameControllerDB snapshot is third-party mapping data under the zlib license. See the package `THIRD-PARTY-NOTICES.md` and `ThirdParty/SDL_GameControllerDB/LICENSE`.
