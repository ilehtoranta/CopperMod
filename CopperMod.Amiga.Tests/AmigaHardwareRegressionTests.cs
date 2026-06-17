using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaHardwareRegressionTests
{
	[Fact]
	public void PaulaManualAudioDataWritePlaysBothWordBytesAndRequestsInterrupt()
	{
		var bus = CreateComponentBus();
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(856, buffer, 1, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.Equal(0.0f, buffer[1]);
		Assert.Equal(0.0f, buffer[3]);
		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);
	}

	[Fact]
	public void PaulaManualAudioInterruptDoesNotDependOnVolume()
	{
		var bus = CreateComponentBus();
		bus.WriteWord(0x00DFF0A8, 0x0000, 0);
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);

		bus.Paula.AdvanceTo(0);

		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);
	}

	[Fact]
	public void PaulaAcceptsEvenByteVolumeWrites()
	{
		var bus = CreateComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x7F;
		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0001, 0);
		bus.WriteByte(0x00DFF0A8, 0x20, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		var buffer = new float[2];

		bus.Paula.RenderSample(38, buffer, 0, 2);

		Assert.True(buffer[0] > 0.01f);
		Assert.Equal(0.0f, buffer[1]);
	}

	[Fact]
	public void AmigaBusUsesTwentyFourBitAddressAliasesForCustomRegisters()
	{
		var bus = CreateComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x7F;
		bus.WriteWord(0x6CDFF0A0, 0x0000, 0);
		bus.WriteWord(0x6CDFF0A2, 0x1000, 0);
		bus.WriteWord(0x6CDFF0A4, 0x0001, 0);
		bus.WriteWord(0x6CDFF0A6, 0x0001, 0);
		bus.WriteWord(0x6CDFF0A8, 0x0020, 0);
		bus.WriteWord(0x6CDFF096, 0x8201, 0);
		var buffer = new float[2];

		bus.Paula.RenderSample(34, buffer, 0, 2);

		Assert.True(buffer[0] > 0.01f);
		Assert.Equal(0.0f, buffer[1]);
		Assert.Contains(bus.CustomRegisterWrites, write => write.Address == 0x0A0);
		Assert.Contains(bus.CustomRegisterWrites, write => write.Address == 0x096);
	}

	[Fact]
	public void HalfMegChipRamMirrorsAcrossLowChipDecodeWindow()
	{
		var bus = new AmigaBus(AmigaConstants.A500BootChipRamSize);

		bus.WriteByte(0x00080000, 0x5A, 0);
		bus.WriteByte(0x00100123, 0xC3, 0);
		bus.WriteByte(0x001FFFFF, 0x7E, 0);

		Assert.Equal(0x5A, bus.ChipRam[0x00000]);
		Assert.Equal(0x5A, bus.ReadByte(0x00000000));
		Assert.Equal(0xC3, bus.ChipRam[0x00123]);
		Assert.Equal(0xC3, bus.ReadByte(0x00080123));
		Assert.Equal(0x7E, bus.ChipRam[^1]);
		Assert.Equal(0x7E, bus.ReadByte(0x0007FFFF));
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Target == AmigaBusAccessTarget.ChipRam &&
			access.Request.Address == 0x00080000);
	}

	[Fact]
	public void FullTwoMegChipRamKeepsUpperLowChipAddressesDistinct()
	{
		var bus = new AmigaBus(AmigaConstants.DefaultChipRamSize);

		bus.WriteByte(0x00000000, 0x11, 0);
		bus.WriteByte(0x00080000, 0x22, 0);

		Assert.Equal(0x11, bus.ChipRam[0x00000]);
		Assert.Equal(0x22, bus.ChipRam[0x80000]);
		Assert.Equal(0x11, bus.ReadByte(0x00000000));
		Assert.Equal(0x22, bus.ReadByte(0x00080000));
	}

	[Fact]
	public void CiaBTimerALatchExposesCpuCycleInterval()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD400, 0x00, 0);
		bus.WriteByte(0x00BFD500, 0x42, 0);

		Assert.Equal(168_960, bus.CiaBTimerAIntervalCycles);
	}

	private static AmigaBus CreateComponentBus()
	{
		return new AmigaBus(enableLiveAgnusDma: false);
	}
}
