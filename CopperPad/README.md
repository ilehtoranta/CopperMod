# CopperPad

CopperPad is a portable .NET game controller normalization library. It reads HID game controllers on Windows, macOS, and Linux and exposes a virtual Xbox-style state for host applications.

## Platform notes

- Windows 11 support depends on how the installed driver exposes a controller. Some Xbox-family devices appear through vendor-specific or XInput paths that are not fully readable as generic HID.
- macOS USB and Bluetooth controllers are supported when the operating system exposes them through readable HID APIs.
- Linux uses hidraw through HidSharp; users may need udev rules or group permissions for non-root access.

Version 1 focuses on input snapshots, device attach/detach, normalization, and app-supplied JSON profiles. Rumble/force feedback is intentionally out of scope.

## Controller mappings

CopperPad uses app/user JSON profiles first, then a bundled pinned snapshot of SDL_GameControllerDB for common controller mappings, followed by built-in compatibility fallbacks. SDL_GameControllerDB is bundled as third-party mapping data under the zlib license; see `THIRD-PARTY-NOTICES.md` and `ThirdParty/SDL_GameControllerDB/LICENSE`.
