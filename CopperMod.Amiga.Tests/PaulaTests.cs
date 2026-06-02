using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class PaulaTests
{
	[Fact]
	public void ManualAudioDataOutputsHighThenLowByteAndRequestsInterrupt()
	{
		var bus = CreateLegacyPaulaBus();
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(856, buffer, 1, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		var interruptEvent = Assert.Single(bus.Paula.DrainInterrupts());
		Assert.Equal(0, interruptEvent.Channel);
		Assert.Equal(0x0080, interruptEvent.IntreqBit);
		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);
	}

	[Fact]
	public void ManualAudioDataTransitionsOnExactIntegerPeriodCycles()
	{
		var bus = CreateLegacyPaulaBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(5, buffer, 0, 2);
		var beforeBoundary = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(6, buffer, 1, 2);
		var atBoundary = bus.Paula.GetChannelSnapshot(0);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(beforeBoundary.NextByteIsLow);
		Assert.Equal(6, beforeBoundary.NextSampleCycle);
		Assert.False(atBoundary.NextByteIsLow);
		Assert.Equal(12, atBoundary.NextSampleCycle);
	}

	[Fact]
	public void ManualAudioDataRewriteAfterLowBoundaryIsCausal()
	{
		var bus = CreateLegacyPaulaBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 5);
		var buffer = new float[6];

		bus.Paula.RenderSample(3, buffer, 0, 2);
		var beforeBoundary = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		var atBoundary = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(5, buffer, 2, 2);
		var afterRewrite = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(0x7F, beforeBoundary.CurrentSample);
		Assert.Equal(unchecked((sbyte)0x81), atBoundary.CurrentSample);
		Assert.Equal(0x40, afterRewrite.CurrentSample);
		Assert.Equal(9, afterRewrite.NextSampleCycle);
	}

	[Fact]
	public void ManualAudioDataCanBeReplacedBeforeLowByteIsPlayed()
	{
		var bus = CreateLegacyPaulaBus();
		bus.WriteWord(0x00DFF0A6, 0x0004, 0);
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
		bus.WriteWord(0x00DFF0AA, 0x4080, 4);
		var buffer = new float[6];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		bus.Paula.RenderSample(12, buffer, 2, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] > 0.10f);
		Assert.True(buffer[4] < -0.20f);
	}

	[Fact]
	public void IntenaGatesAudioInterruptEventsButNotIntreqPolling()
	{
		var bus = CreateLegacyPaulaBus();

		bus.WriteWord(0x00DFF0AA, 0x0102, 0);
		bus.Paula.AdvanceTo(0);
		Assert.Empty(bus.Paula.DrainInterrupts());
		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);

		bus.WriteWord(0x00DFF09C, 0x0080, 0);
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0AA, 0x0102, 0);
		bus.Paula.AdvanceTo(0);

		Assert.Single(bus.Paula.DrainInterrupts());
		Assert.True((bus.ReadWord(0x00DFF01C) & 0x4080) == 0x4080);
	}

	[Fact]
	public void DmaFetchesWordsAdvancesPointerAndReloadsLength()
	{
		var bus = CreateLegacyPaulaBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.ChipRam[0x1002] = 0x40;
		bus.ChipRam[0x1003] = 0xC0;
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0002, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		var buffer = new float[6];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		bus.Paula.RenderSample(8, buffer, 2, 2);
		var snapshot = bus.Paula.GetChannelSnapshot(0);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(buffer[4] > 0.10f);
		Assert.Equal(0x1004u, snapshot.CurrentAddress);
		Assert.Equal(0, snapshot.RemainingWords);
		Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Channel == 0);
	}

	[Fact]
	public void DmaFetchRequestCyclesRemainExactAcrossPeriods()
	{
		var bus = CreateLegacyPaulaBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.ChipRam[0x1002] = 0x40;
		bus.ChipRam[0x1003] = 0xC0;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(36);

		var requestedCycles = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.Select(access => access.RequestedCycle)
			.ToArray();
		Assert.Equal(new long[] { 0, 12, 24, 36 }, requestedCycles);
		Assert.Equal(42, bus.Paula.GetChannelSnapshot(0).NextSampleCycle);
	}

	[Fact]
	public void DmaLengthReloadInterruptsUseExactSampleBoundary()
	{
		var bus = CreateLegacyPaulaBus();
		bus.ChipRam[0x1000] = 0x20;
		bus.ChipRam[0x1001] = 0xE0;
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(8);

		var interruptCycles = bus.Paula.DrainInterrupts()
			.Where(interruptEvent => interruptEvent.Channel == 0)
			.Select(interruptEvent => interruptEvent.Cycle)
			.ToArray();
		Assert.Contains(0, interruptCycles);
		Assert.Contains(8, interruptCycles);
		Assert.All(interruptCycles, cycle => Assert.Contains(cycle, new long[] { 0, 8 }));
		Assert.Equal(12, bus.Paula.GetChannelSnapshot(0).NextSampleCycle);
	}

	[Fact]
	public void DmaLengthOneReloadsFromOriginalLocation()
	{
		var bus = CreateLegacyPaulaBus();
		bus.ChipRam[0x1000] = 0x20;
		bus.ChipRam[0x1001] = 0xE0;
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0001, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);

		bus.Paula.AdvanceTo(4);
		var snapshot = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(0x1002u, snapshot.CurrentAddress);
		Assert.Equal(0, snapshot.RemainingWords);
		Assert.True(bus.Paula.DrainInterrupts().Count >= 2);
	}

	[Fact]
	public void AdkconUsesSetClearSemantics()
	{
		var bus = CreateLegacyPaulaBus();

		bus.WriteWord(0x00DFF09E, 0x8011, 0);
		bus.Paula.AdvanceTo(0);
		Assert.Equal(0x0011, bus.ReadWord(0x00DFF010));
		bus.WriteWord(0x00DFF09E, 0x0010, 0);
		bus.Paula.AdvanceTo(0);
		Assert.Equal(0x0001, bus.ReadWord(0x00DFF010));
	}

	[Fact]
	public void AdkconVolumeAttachMutesSourceAndModulatesNextChannelVolume()
	{
		var bus = CreateLegacyPaulaBus();
		bus.WriteWord(0x00DFF09E, 0x8001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF0AA, 0x0020, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		var source = bus.Paula.GetChannelSnapshot(0);
		var target = bus.Paula.GetChannelSnapshot(1);

		Assert.Equal(0.0f, buffer[0]);
		Assert.Equal(0.0f, buffer[2]);
		Assert.Equal(32, target.Volume);
		Assert.Equal(0x0020, source.DataWord);
	}

	[Fact]
	public void AdkconPeriodAttachModulatesNextChannelPeriod()
	{
		var bus = CreateLegacyPaulaBus();
		bus.WriteWord(0x00DFF09E, 0x8010, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF0AA, 0x0005, 0);

		bus.Paula.AdvanceTo(4);

		Assert.Equal(5, bus.Paula.GetChannelSnapshot(1).Period);
	}

	private static void SchedulePaulaWrite(AmigaBus bus, ushort offset, ushort value, long cycle)
	{
		bus.Paula.ScheduleWrite(cycle, offset, value);
	}

	private static AmigaBus CreateLegacyPaulaBus()
	{
		return new AmigaBus(
			enableLiveAgnusDma: false,
			agnusTimingMode: AgnusTimingMode.LegacyReservation,
			audioDmaMinimumPeriod: 1);
	}
}
