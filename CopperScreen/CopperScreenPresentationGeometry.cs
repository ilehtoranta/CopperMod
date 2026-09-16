using Avalonia;

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

	// Beam-coordinate producers retain blanking in their buffers. Legacy
	// producers already translate to capture coordinates before publication.
	public PixelRect? FullViewport { get; init; }
	public PixelRect? StandardViewport { get; init; }

	public static CopperScreenPresentationGeometry ForStandardRaster(bool isPal, bool superHighRes)
		=> new(isPal ? 358 : 362, isPal ? 285 : 241, 320, isPal ? 256 : 200,
			superHighRes ? 4 : 2, 2, isPal, superHighRes);

	public PixelRect GetCroppedViewport()
		=> StandardViewport ?? new(
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
