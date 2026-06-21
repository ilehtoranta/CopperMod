namespace CopperMod.Tools;

internal static class Usage
{
	public const string Text = """
		Usage:
		  coppermod-tools render <input> --out <output> [options]
		  coppermod-tools generate-sid-d418-matrices --input <Pex root> --out <generated C# file>
		  coppermod-tools compare-sidtest5 <split PRG dir> --out <work dir> [options]

		Options:
		  --format wav|pcm|mp3|bmp   Optional when output extension is .wav, .pcm, .mp3, or .bmp.
		  --seconds <number|auto>    Render a fixed duration, or auto-detect SID duration.
		  --subsong <number>         Select a 1-based subtune.
		  --sample-rate <hz>         Default: 44100.
		  --channels <count>         Default: 2.
		  --sid-solo <1|2|3>         Render only one SID voice.
		  --sid-profile balanced|reference Default: balanced.
		  --sid-detect-loop          Use SID write-loop detection as render duration.
		  --sid-detect-duration      Detect SID duration from loop or sustained silence.
		  --sid-detect-max-seconds <n> Default: 600. Requires SID detection.
		  --output raw|player        Default: raw.
		  --amiga-profile clean|a500|led
		  --c64-profile clean|c64
		  --c64-autostart-key a,return Schedule C64/PRG startup keys.
		  --c64-autostart-delay-seconds <n> Default: 1.
		  --c64-autostart-hold-seconds <n> Default: 0.25.
		  --c64-autostart-gap-seconds <n> Default: 0.75.
		  --c64-rom <path>          Optional 16 KiB BASIC+KERNAL or 20 KiB combo ROM.
		  --mp3-bitrate <kbps>       Default: 192.
		  --bitmap-width <pixels>    Default: 1024. BMP output only.
		  --bitmap-height <pixels>   Default: 256. BMP output only.
		  --overwrite                Replace an existing output file.

		sidtest5 comparison options:
		  --reference-dir <dir>      Existing or generated SidPlayFP WAVs. Default: <work dir>\sidplayfp.
		  --candidate-dir <dir>      Existing or generated CopperMod WAVs. Default: <work dir>\coppermod.
		  --sidplayfp <path>         Render missing SidPlayFP references with forced PAL/6581 reSIDfp.
		  --c64-rom <path>           16 KiB BASIC+KERNAL ROM for PRG execution.
		  --seconds <number>         Default: 3.
		  --sample-rate <hz>         Default: 48000.
		  --sid-profile balanced|reference Default: balanced.
		  --overwrite-reference      Re-render SidPlayFP reference WAVs.
		  --overwrite-candidate      Re-render CopperMod candidate WAVs.
		""";
}
