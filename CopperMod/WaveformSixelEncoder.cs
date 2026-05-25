using Terminal.Gui.Drawing;

namespace CopperMod;

internal static class WaveformSixelEncoder
{
	private const int PaletteColorCount = 8;

	public static SixelEncoder Create()
	{
		return new SixelEncoder
		{
			Quantizer = new ColorQuantizer
			{
				MaxColors = PaletteColorCount,
				PaletteBuildingAlgorithm = new PopularityPaletteWithThreshold(new EuclideanColorDistance(), 0)
			},
			AvoidBottomScroll = true
		};
	}
}
