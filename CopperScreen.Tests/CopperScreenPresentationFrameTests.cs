using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenPresentationFrameTests
{
	[Fact]
	public void EmulatorUsesLowResolutionPresentationFramebuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		Assert.Equal(AmigaConstants.PalLowResWidth, emulator.Width);
		Assert.Equal(AmigaConstants.PalLowResHeight, emulator.Height);
		Assert.Equal(emulator.Width * emulator.Height, emulator.Framebuffer.Length);
	}

	[Fact]
	public void PresentationFrameRenderMatchesPreparedFramebuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		emulator.RenderNextFrame();
		var frame = emulator.PreparePresentationFrame(frameNumber: 1);
		var rendered = new int[emulator.Framebuffer.Length];

		CopperScreenEmulator.RenderPresentationFrame(frame, rendered);

		Assert.Equal(emulator.Width, frame.Width);
		Assert.Equal(emulator.Height, frame.Height);
		Assert.Equal(1, frame.FrameNumber);
		Assert.Equal(emulator.Framebuffer, rendered);
		Assert.Equal(emulator.DisplaySnapshot.Bplcon0, frame.DisplaySnapshot.Bplcon0);
	}

	[Fact]
	public void InterlaceFieldSeedingCopiesCurrentFieldIntoMissingRows()
	{
		var interlace = new int[16];
		interlace[0] = unchecked((int)0xFFFF0000);
		interlace[1] = unchecked((int)0xFFFF0000);
		interlace[4] = unchecked((int)0xFF00FF00);
		interlace[5] = unchecked((int)0xFF00FF00);
		interlace[8] = unchecked((int)0xFF0000FF);
		interlace[9] = unchecked((int)0xFF0000FF);
		interlace[12] = unchecked((int)0xFFFFFF00);
		interlace[13] = unchecked((int)0xFFFFFF00);

		CopperScreenEmulator.SeedMissingInterlaceFieldRows(interlace, width: 4, height: 4, interlaceField: 0);

		Assert.Equal(interlace[0], interlace[4]);
		Assert.Equal(interlace[1], interlace[5]);
		Assert.Equal(interlace[8], interlace[12]);
		Assert.Equal(interlace[9], interlace[13]);
	}

	[Fact]
	public void InterlaceDownsampleProducesStableLowResolutionPixelsFromWovenRows()
	{
		var red = unchecked((int)0xFFFF0000);
		var green = unchecked((int)0xFF00FF00);
		var blue = unchecked((int)0xFF0000FF);
		var white = unchecked((int)0xFFFFFFFF);
		var interlace = new int[]
		{
			red, red, green, green,
			red, red, green, green,
			blue, blue, white, white,
			blue, blue, white, white
		};
		var output = new int[4];

		CopperScreenEmulator.DownsampleInterlacePresentationFrame(interlace, output, width: 4, height: 4);

		Assert.Equal(new[] { red, green, blue, white }, output);
	}
}
