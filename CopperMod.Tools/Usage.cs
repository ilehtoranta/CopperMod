namespace CopperMod.Tools;

internal static class Usage
{
	public const string Text = """
		Usage:
		  coppermod-tools render <input> --out <output> [options]

		Options:
		  --format wav|pcm|mp3       Optional when output extension is .wav, .pcm, or .mp3.
		  --seconds <number>         Render a fixed duration.
		  --subsong <number>         Select a 1-based subtune.
		  --sample-rate <hz>         Default: 44100.
		  --channels <count>         Default: 2.
		  --sid-solo <1|2|3>         Render only one SID voice.
		  --output raw|player        Default: raw.
		  --amiga-profile clean|a500|led
		  --c64-profile clean|c64
		  --mp3-bitrate <kbps>       Default: 192.
		  --overwrite                Replace an existing output file.
		""";
}
