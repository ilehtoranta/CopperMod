using CopperMod.Amiga;
using CopperScreen;
using System.Diagnostics;

namespace CopperScreen.Tests;

public sealed class CopperScreenPresentationFrameTests
{
	[Fact]
	public void PresentationFrameStopUsesVposwForcedShortPalCadence()
	{
		using var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var machine = GetPrivateField<Machine>(emulator, "_machine");
		var cycle = 0L;
		machine.Bus.WriteWord(0x00DFF02A, 0x0001, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var expectedShortFrameCycles =
			(long)AmigaConstants.A500PalShortRasterLines * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		Assert.Equal(expectedShortFrameCycles, emulator.GetPresentationFrameStopCycle(0));
	}

	[Fact]
	public void EmulatorUsesHighResolutionPresentationFramebuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		Assert.Equal(AmigaConstants.PalHighResWidth, emulator.Width);
		Assert.Equal(AmigaConstants.PalHighResHeight, emulator.Height);
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
	public void CrtPhosphorAllocatesOnlyWhenAnInterlacedFieldIsSubmittedAndDecaysBetweenTicks()
	{
		var phosphor = new CrtPhosphorComposer();
		Assert.False(phosphor.HasBuffers);
		var active = unchecked((int)0xFF204080);
		var fieldHistory = new int[]
		{
			active, active, active, active,
			active, active, active, active,
			active, active, active, active,
			active, active, active, active
		};

		var timestamp = Stopwatch.GetTimestamp();
		phosphor.SubmitField(fieldHistory, width: 4, height: 4, interlaceField: 1, fieldDurationSeconds: 1.0 / 50.0, timestamp);
		Assert.True(phosphor.HasBuffers);
		var beforeDecay = phosphor.Output[4];
		phosphor.Advance(timestamp + Stopwatch.Frequency / 20);
		Assert.True((uint)phosphor.Output[4] < (uint)beforeDecay);
		phosphor.Reset();
		Assert.False(phosphor.HasBuffers);
	}

	private static T GetPrivateField<T>(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(
			fieldName,
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsType<T>(field.GetValue(instance));
	}
}
