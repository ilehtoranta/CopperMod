namespace CopperMod.Tools;

internal static class Usage
{
	public const string Text = """
		Usage:
		  coppermod-tools render <input> --out <output> [options]

		Options:
		  --format wav|pcm|mp3|bmp   Optional when output extension is .wav, .pcm, .mp3, or .bmp.
		  --seconds <number|auto>    Render a fixed duration, or auto-detect SID duration.
		  --subsong <number>         Select a 1-based subtune.
		  --sample-rate <hz>         Default: 44100.
		  --channels <count>         Default: 2.
		  --sid-solo <1|2|3>         Render only one SID voice.
		  --sid-detect-loop          Use SID write-loop detection as render duration.
		  --sid-detect-duration      Detect SID duration from loop or sustained silence.
		  --sid-detect-max-seconds <n> Default: 600. Requires SID detection.
		  --output raw|player        Default: raw.
		  --amiga-profile clean|a500|led
		  --c64-profile clean|c64
		  --c64-autostart-key f3[,space] Schedule C64 cartridge startup keys.
		  --c64-autostart-delay-seconds <n> Default: 1.
		  --c64-autostart-hold-seconds <n> Default: 0.25.
		  --c64-autostart-gap-seconds <n> Default: 0.75.
		  --mp3-bitrate <kbps>       Default: 192.
		  --bitmap-width <pixels>    Default: 1024. BMP output only.
		  --bitmap-height <pixels>   Default: 256. BMP output only.
		  --overwrite                Replace an existing output file.
		""";
}
