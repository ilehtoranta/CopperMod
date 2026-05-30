using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenPresentationFrame
{
	public CopperScreenPresentationFrame(
		int width,
		int height,
		long frameNumber,
		int[] bgra,
		OcsDisplaySnapshot displaySnapshot)
	{
		Width = width;
		Height = height;
		FrameNumber = frameNumber;
		Bgra = bgra ?? throw new ArgumentNullException(nameof(bgra));
		DisplaySnapshot = displaySnapshot;
	}

	public int Width { get; }

	public int Height { get; }

	public long FrameNumber { get; }

	public int[] Bgra { get; }

	public OcsDisplaySnapshot DisplaySnapshot { get; }
}
