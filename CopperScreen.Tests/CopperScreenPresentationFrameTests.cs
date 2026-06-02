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
}
