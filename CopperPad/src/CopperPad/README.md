# CopperPad

CopperPad is a portable .NET game controller abstraction library. The core package is platform-neutral: applications discover `CopperController` instances, inspect supported profiles, read `CopperControllerSnapshot` values, and subscribe to profile/element changes.

Desktop HID support lives in `CopperPad.HidSharp`. Apple GameController support lives in `CopperPad.GameController`.

## Platform notes

- Windows 11 HID support depends on how the installed driver exposes a controller. Some Xbox-family devices appear through vendor-specific or XInput paths that are not fully readable as generic HID.
- macOS USB and Bluetooth HID controllers are supported through `CopperPad.HidSharp` when the operating system exposes them through readable HID APIs.
- Linux HID support uses hidraw through HidSharp; users may need udev rules or group permissions for non-root access.
- iOS support uses Apple's GameController APIs through `CopperPad.GameController`; validation requires Mac/iOS hardware.

Version 2 focuses on profile-first snapshots, device attach/detach, normalization, and app-supplied JSON profile overrides. Rumble/force feedback is intentionally out of scope.

## Controller mappings

`CopperPad.HidSharp` uses app/user JSON profiles first, then provider-native mappings when available, then a bundled pinned snapshot of SDL_GameControllerDB for common controller mappings, followed by built-in compatibility fallbacks. SDL_GameControllerDB is bundled with the HidSharp provider package as third-party mapping data under the zlib license.
