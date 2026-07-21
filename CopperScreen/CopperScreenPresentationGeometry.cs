using Avalonia;
using CopperMod.Amiga.Core;

namespace CopperScreen;

internal readonly record struct CopperScreenPresentationGeometry(
	int CaptureLowResWidth,
	int CaptureLowResHeight,
	int StandardLowResWidth,
	int StandardLowResHeight,
	int HorizontalSamplesPerLowResPixel,
	int VerticalSamplesPerLowResPixel,
	bool IsPal,
	bool IsSuperHighRes)
{
	// Keep the established PAL crop placement while deriving its size and scale from timing.
	private const int CropBorderLowResX = 32;
	private const int CropBorderLowResY = 16;

	public static CopperScreenPresentationGeometry FromRasterTiming(RasterTiming timing, bool superHighRes)
		=> new(
			timing.PresentationLowResWidth,
			timing.PresentationLowResHeight,
			timing.StandardLowResWidth,
			timing.StandardLowResHeight,
			superHighRes ? 4 : 2,
			2,
			timing.IsCanonicalPal,
			superHighRes);

	public PixelRect GetCroppedViewport()
		=> new(
			CropBorderLowResX * HorizontalSamplesPerLowResPixel,
			CropBorderLowResY * VerticalSamplesPerLowResPixel,
			StandardLowResWidth * HorizontalSamplesPerLowResPixel,
			StandardLowResHeight * VerticalSamplesPerLowResPixel);

	public double GetHorizontalPixelAspect(CopperScreenPixelAspectMode mode)
	{
		var lcdFactor = IsSuperHighRes ? 0.5 : 1.0;
		if (mode == CopperScreenPixelAspectMode.Lcd)
		{
			return lcdFactor;
		}

		var crtFactor = IsPal ? 16.0 / 15.0 : 5.0 / 6.0;
		return crtFactor * lcdFactor;
	}
}
