# CopperMod.Tools

`CopperMod.Tools` is the command-line export utility for CopperMod. It renders
supported module formats to audio files without opening the terminal player.

Supported input formats are the formats registered by `CopperMod.Rendering`:

- MED / OctaMED MMD modules
- ProTracker MOD modules
- Amiga CUST players
- SID tunes

## Usage

From the repository root:

```powershell
dotnet run --project .\CopperMod.Tools -- render <input> --out <output> [options]
```

When running a published build, replace the `dotnet run --project ... --` prefix
with the tool executable.

## Examples

Render 30 seconds of a ProTracker module to WAV:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.mod" --out tune.wav --seconds 30
```

Render a SID tune to raw floating-point PCM:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.sid" --out tune.pcm --seconds 30
```

Render an MP3 at a specific bitrate:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.sid" --out tune.mp3 --seconds 30 --mp3-bitrate 192
```

Render a waveform bitmap for inspection:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.mod" --out tune.bmp --seconds 30 --bitmap-width 1600 --bitmap-height 320
```

Render with the same output shaping used by the player:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.mod" --out tune.wav --seconds 30 --output player --amiga-profile a500
```

Render only SID voice 2:

```powershell
dotnet run --project .\CopperMod.Tools -- render "path\to\tune.sid" --out voice2.wav --seconds 30 --sid-solo 2
```

## Command

```text
render <input> --out <output> [options]
```

`<input>` is the module file to render. `--out` is required and selects the
output path.

The output format is inferred from the output extension when it is `.wav`,
`.pcm`, `.mp3`, or `.bmp`. Use `--format` when the extension is different.

## Options

| Option | Values | Default | Description |
| --- | --- | --- | --- |
| `--format` | `wav`, `pcm`, `mp3`, `bmp` | inferred from `--out` | Selects the output file format. |
| `--seconds` | positive number | song duration | Renders a fixed duration. Required for formats whose duration is unknown, such as many SID tunes. |
| `--subsong` | positive integer | format default | Selects a 1-based subtune when the loaded module exposes subtunes. |
| `--sample-rate` | positive integer | `44100` | Sets the output sample rate in Hz. |
| `--channels` | positive integer | `2` | Sets the interleaved output channel count. |
| `--sid-solo` | `1`, `2`, `3` | none | Renders only one SID voice. This option is SID-specific. |
| `--output` | `raw`, `player` | `raw` | `raw` writes direct renderer output. `player` applies the same Amiga or C64 output stage used by the player. |
| `--amiga-profile` | `clean`, `a500`, `led` | `a500` | Selects the Amiga player output profile. Requires `--output player`. |
| `--c64-profile` | `clean`, `c64` | `c64` | Selects the C64 player output profile. Requires `--output player`. |
| `--mp3-bitrate` | positive integer | `192` | Sets MP3 bitrate in kbps. |
| `--bitmap-width` | `128` to `8192` | `1024` | Sets BMP waveform width in pixels. Requires BMP output. |
| `--bitmap-height` | `72` to `4096` | `256` | Sets BMP waveform height in pixels. Requires BMP output. |
| `--overwrite` | flag | off | Allows replacing an existing output file. |
| `--help` | flag | off | Prints command usage. |

## Output Formats

### WAV

WAV output uses 32-bit IEEE floating-point samples. The sample rate and channel
count come from `--sample-rate` and `--channels`.

### PCM

PCM output is raw interleaved little-endian Float32 data. It has no container
header, so consumers need to know the sample rate and channel count used for the
render.

### MP3

MP3 output is encoded from 16-bit PCM through the Windows Media Foundation MP3
encoder used by NAudio. MP3 export is available only when that encoder is
available on the host system. If it is unavailable, use WAV or PCM output.

### BMP

BMP output writes a 24-bit color waveform image of the rendered PCM. The image
uses one lane per rendered output channel, up to four lanes. `--bitmap-width`
and `--bitmap-height` control the output dimensions.

## Raw vs Player Output

`--output raw` writes the backend renderer output directly. This is the default
and is useful for analysis, tests, and preserving the backend's unshaped PCM.

`--output player` runs the rendered PCM through the same output stage used by
the CopperMod player:

- Amiga-family formats use `--amiga-profile`.
- SID / C64 output uses `--c64-profile`.

Profile options are rejected unless `--output player` is selected.

## Duration Handling

If `--seconds` is provided, the tool renders exactly that duration. If playback
ends before the requested duration, the rest of the output is padded with
silence.

If `--seconds` is omitted, the tool renders until the song ends. Modules with an
unknown duration require `--seconds`.

## Exit Codes

`0` means the render completed successfully. `1` means the command line was
invalid, the input module could not be loaded, the output file could not be
written, or a required encoder was unavailable.

Successful renders print:

```text
Rendered <filename>
```
